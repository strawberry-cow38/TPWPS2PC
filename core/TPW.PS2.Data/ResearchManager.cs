namespace TPW.PS2.Data;

/// <summary>One of the research manager's five project slots, `mgr + 0xC + 0x1C·i`, READ
/// (`0x1B7330` ctor, `0x1B7370/0x1B7378/0x1B7388/0x1B7490/0x1B7498/0x1B74A0/0x1B7600/0x1B7650`,
/// findings/staff-management.md §10.1).</summary>
public sealed class ResearchProject
{
    internal ResearchProject(int slot) => Slot = slot;

    /// <summary>Which of the manager's five slots (0..4).</summary>
    public int Slot { get; }
    /// <summary>`+0`: set by `0x1B6850 → 0x1B7370` (100 per the findings). ⚠ No reader traced.</summary>
    public int Weight { get; internal set; }
    /// <summary>`+4`: the required work, `DBA research work << 12` (`0x1B7378`, `sll 0xC`). Unsigned.</summary>
    public uint Required { get; internal set; }
    /// <summary>`+8`: work done, in the same 1/4096 units (`addu`, wraps as a u32 would).</summary>
    public uint Progress { get; internal set; }
    /// <summary>`+0xC`: being researched. `0x1B6A38` shares each quantum among the active slots.</summary>
    public bool Active { get; internal set; }
    /// <summary>`+0x10`: finished (`0x1B74A0(slot, 1)`); cleared again when the slot is restarted.</summary>
    public bool Complete { get; internal set; }
    /// <summary>`+0x14`: the item index in its category, -1 when none (the ctor, and after completion).</summary>
    public int Item { get; internal set; } = -1;
    /// <summary>`+0x18`: the research category 1..8, -1 when none. Its completion message:
    /// 1, 3, 6, 7 ride-or-addon (§ <see cref="ResearchManager.CompletionMessage"/>), 2 feature, 4 shop,
    /// 5 sideshow, 8 addon.</summary>
    public int Category { get; internal set; } = -1;

    /// <summary>`0x1B7388`'s percentage, what it files into the research database (`0x12BAF8`) and
    /// prints: `progress*100 / required` (`divu`), or 100 when nothing is required.</summary>
    public uint Percent => Required == 0 ? 100u : unchecked(Progress * 100u) / Required;

    public override string ToString() => $"slot {Slot}: cat {Category} item {Item} {(Active ? "active" : "idle")}{(Complete ? " complete" : "")} {Progress}/{Required} ({Percent}%)";
}

/// <summary>⭐⭐ THE RESEARCH MANAGER, the lazy singleton `0x1B6798` (`[0x2E7540]`, 0xAC bytes; freed by
/// `0x1B67F8`), READ in findings/staff-management.md §10 against `0x1B6640`, `0x1B6A38`, `0x1B7388`,
/// `0x1B74A0` and their MIPS.
///
/// <code>
///   +0          1 at init; set 1 when a project completes (0x1B74A0)          -- ⚠ no reader traced
///   +4          BUDGET %: 80 at init (0x1B6640), 100 whenever the Research screen is built (0x1B5410)
///   +0xC+0x1C·i five project slots (ResearchProject)
///   +0x98+4·i   per-slot group threshold, read only when a project is started (0x1B6880) -- ⚠ untraced
/// </code>
///
/// A researcher's quantum (state 0x1F, <see cref="Researcher"/>) calls <see cref="Contribute"/> with
/// `R[L]` (`0x366150` = 20, 30, 35, 40, 43). ⭐ There is no money cost anywhere near it: the budget is a
/// fixed 80 → 100 switch, and at 100 each quantum also costs the researcher 6 tiredness.
///
/// ⭐ PORTED 2026-10-01 (findings/research.md): <see cref="ResearchDatabase"/>, <see cref="ResearchCatalogue"/>, the thresholds
/// (<see cref="Refresh"/>), eligibility (<see cref="Eligible"/>) and the Research screen's picking. What follows was the
/// state before, kept for its addresses -- ⚠ WHAT WAS NOT PORTED: the research-state DATABASE (`0x389650`, **60 records of
/// 4 bytes** `{cat, item, percent, level}`, lazily created and hard-capped at 60 -- find
/// `0x12BEE8`, alloc `0x12BF38`, level `0x12BA08`, percent `0x12B928`, availability `0x12B6D0`,
/// write `0x12BAF8`), the per-world catalogues (`PTR_DAT_00360850[world]`, item index = ordinal in
/// the kind's list), the five group thresholds (`mgr+0x98[slot]`, `0x1B6BE0`/`0x1B6D68`/`0x1B6FB8`)
/// and the eligibility rule that builds a row's candidate list (`0x15CA78` walking the catalogue,
/// appending where `0x1B7208(mgr, slot, cat, item)` passes).
///
/// ⚠ CORRECTED 2026-09-28: an earlier note here said `0x12AED8` unlocks. **It does not** --
/// `0x12AED8` is the RELEASE half of a fetch/release pair with `0x12AE78` (and for categories 1..7
/// it calls the EMPTY `0x10F2A0`). The unlock is the **`level++` in `0x12BAF8`**, run when a
/// record's filed percent reaches 100 during `0x1B7388`. Availability everywhere is
/// `record.level > tier`, where a ride's tier is the item's research LEVEL.
///
/// This class takes those as caller-supplied answers: <see cref="Start"/> is told the required work
/// and the percent already done, <see cref="ItemLevel"/> answers `0x12BA08`, <see cref="Researched"/>
/// is raised where `0x12BAF8` does the level++, and <see cref="AnythingLeftToResearch"/> answers
/// `0x104358(0xFFFF)`. Full shape: `findings/hardcoded-screens.md`.</summary>
public sealed class ResearchManager
{
    public const int SlotCount = 5;
    readonly ResearchProject[] _slots = new ResearchProject[SlotCount];

    /// <summary>`0x1B6640`: flag 1, budget 80, `+8` 0, every slot constructed idle (`0x1B7498(slot, 0)`)
    /// and every threshold `+0x98` zeroed.</summary>
    public ResearchManager()
    {
        for (int i = 0; i < SlotCount; i++) _slots[i] = new ResearchProject(i);
        Budget = StaffTables.ResearchBudgetInitial;
        CompletedFlag = true;
        ItemLevel = (cat, item) => Database?.Level(cat, item) ?? 0;
        AnythingLeftToResearch = () => Database?.AnythingLeft() ?? true;
    }

    public IReadOnlyList<ResearchProject> Slots => _slots;
    /// <summary>`+4`, the budget percentage. Saved and loaded as a byte (`0x1B66C0`/`0x1B6720`).</summary>
    public int Budget { get; private set; }
    /// <summary>`+0`: 1 at init and after every completion -- the THRESHOLDS' DIRTY FLAG: `0x1B7130` recomputes them
    /// only while it is set, then clears it (findings/research.md §2.1).</summary>
    public bool CompletedFlag { get; private set; }

    /// <summary>⭐ The park's research state (<see cref="ResearchDatabase"/>). Null = the old caller-supplied mode, where
    /// <see cref="Start"/> is told the work and nothing is filed.</summary>
    public ResearchDatabase Database { get; set; }
    /// <summary>`0x12AC78(cat, item)`: how many of that catalogue item are built -- the Upgrades row offers a ride only
    /// once one stands. Null answers 0.</summary>
    public Func<int, int, int> BuiltCount { get; set; }

    /// <summary>`mgr + 0x98 + 4·slot`: each row's group threshold -- a locked item is a candidate only while its tier-0
    /// group is at or under it. Row 4's is computed and never read.</summary>
    public IReadOnlyList<int> Thresholds => _thresholds;
    readonly int[] _thresholds = new int[SlotCount];

    /// <summary>The kinds each row researches, in the candidate list's order (`0x15CA78` callers, §3.3).</summary>
    public static IReadOnlyList<int> RowKinds(int row) => row switch
    {
        0 => new[] { 3, 6, 7, 1 },
        1 => new[] { 4 },
        2 => new[] { 5 },
        3 => new[] { 2 },
        4 => new[] { 3, 6, 7, 1, 8 },
        _ => Array.Empty<int>(),
    };

    /// <summary>⭐ `0x1B7130`, READ (MIPS for the three loops): while dirty, for rows 0..3 count every item of the row's
    /// pool by its tier-0 group, and how many of them are available; the threshold is the FIRST GROUP UNDER TWO-THIRDS
    /// RESEARCHED (`done·3 &lt; total·2`, an empty group passing). Pools: row 0 kinds 6, 7, 1, 3; then 4; 5; 2.
    /// ⚠ The console's loop walks off its 8-word stack arrays when groups 1..4 are all two-thirds done; any threshold
    /// past the largest retail tier-0 group (3) admits everything, so it stops at 8 here.</summary>
    public void Refresh()
    {
        if (!CompletedFlag || Database is not { } db) return;
        for (int slot = 0; slot < 4; slot++)
        {
            var total = new int[9]; var done = new int[9];
            foreach (int cat in slot == 0 ? new[] { 6, 7, 1, 3 } : RowKinds(slot))
                for (int i = 0; i < db.Count(cat); i++)
                {
                    int g = Math.Clamp(db.Group(cat, i, 0), 0, 8);
                    total[g]++;
                    if (db.Available(cat, i, 0)) done[g]++;
                }
            if ((uint)(done[0] * 3) < (uint)(total[0] * 2)) continue;     // threshold left as it was
            int t = 1;
            while (t < 8 && (uint)(done[t] * 3) >= (uint)(total[t] * 2)) t++;
            _thresholds[slot] = t;
        }
        CompletedFlag = false;
    }

    /// <summary>⭐ `0x1B7208(mgr, slot, cat, item)`, READ (MIPS `0x1B7208..0x1B732C`): rows 0..3 offer a LOCKED item whose
    /// tier-0 group is within the row's threshold; row 4 offers an AVAILABLE ride kind with one built and a level under 3
    /// (its next upgrade), or an add-on not yet available once the park's track ride 0 is.</summary>
    public bool Eligible(int slot, int cat, int item)
    {
        if (Database is not { } db) return false;
        Refresh();
        bool a = db.Available(cat, item, 0);
        if (cat == 8) return db.Count(6) > 0 && db.Available(6, 0, 0) && !a;
        if (slot == 4) return a && (BuiltCount?.Invoke(cat, item) ?? 0) > 0 && db.Level(cat, item) < 3;
        return db.Group(cat, item, 0) <= _thresholds[slot] && !a;
    }

    /// <summary>The row's candidates (`0x15CA78` per kind, catalogue order), WITHOUT the "Nothing" the screen always
    /// appends after them.</summary>
    public List<(int Cat, int Item)> Candidates(int row)
    {
        var list = new List<(int, int)>();
        if (Database is not { } db) return list;
        foreach (int cat in RowKinds(row))
            for (int i = 0; i < db.Count(cat); i++)
                if (Eligible(row, cat, i)) list.Add((cat, i));
        return list;
    }

    /// <summary>⭐ `0x1B6880` with the database (§2.3): the tier researched is the item's current LEVEL, resumed from the
    /// percent filed for it; refused if the slot is busy or (rows 0..3) the tier's group is over the threshold.</summary>
    public bool StartResearch(int slot, int cat, int item)
    {
        if (Database is not { } db || (uint)slot >= SlotCount) return false;
        int level = db.Level(cat, item);
        int pct = db.Percent(cat, item, level);
        int g = db.Group(cat, item, level);
        int work = db.Work(cat, item, level);
        if (_slots[slot].Active) return false;
        Refresh();
        if (slot != 4 && _thresholds[slot] < g) return false;
        return Start(slot, cat, item, work, pct);
    }

    /// <summary>`0x1B71D8`: the row's project stops -- inactive, its item and category kept; the percent stays filed.</summary>
    public void Stop(int slot)
    {
        if ((uint)slot < SlotCount) _slots[slot].Active = false;
    }
    public int ActiveCount => _slots.Count(s => s.Active);

    /// <summary>`0x1B6848(mgr, v)`: `+4 = v`. The getter `0x1B5910` has no caller.</summary>
    public void SetBudget(int percent) => Budget = percent;

    /// <summary>⭐ What constructing the Research screen does to the budget: `0x1B5410` calls
    /// `0x1B6848(mgr, 100)` (`0x1B54CC..0x1B54D4`), every time it is built with `param_2 == 0`.
    /// ⚠ The port has no Research screen yet: whoever builds one calls this when it opens.</summary>
    public void OpenResearchScreen() => SetBudget(StaffTables.ResearchBudgetScreen);

    /// <summary>⚠ `0x12BA08(db, cat, item)`: the item's research LEVEL in the (unported) research
    /// database. Its only use here is the completion message of categories 1, 3, 6, 7 (&lt; 2 = a new
    /// ride, else an add-on). Null answers 0 -- "a new ride" -- and is labelled so.</summary>
    public Func<int, int, int> ItemLevel { get; set; }
    /// <summary>⚠ `0x14D6B0()`, the mechanic count, for message 0x7E. The owner (<see cref="ParkStaff"/>)
    /// wires it; null answers 1.</summary>
    public Func<int> MechanicCount { get; set; }
    /// <summary>⚠ `0x104358(0xFFFF)`: is anything left to research. Null = not asked (the "all
    /// researched" call `0x151998` is then never made).</summary>
    public Func<bool> AnythingLeftToResearch { get; set; }

    /// <summary>A completed project. ⚠ The unlock is the `level++` in `0x12BAF8`, NOT `0x12AED8`
    /// as this said before -- that one is a release, not an unlock. Not ported: the caller decides
    /// what the category/item opens.</summary>
    public Action<ResearchProject> Researched { get; set; }
    /// <summary>The completion's advisor message (0x4B..0x4F, 0x7E), posted through `0x107CC0`.</summary>
    public Action<int> Advisor { get; set; }
    /// <summary>`0x151998`: nothing is left to research -- UI sound 0xBA (while its handle `[0x2B72C0]` is
    /// free) and a UI-state write (`[0x2ABE30] = 0x19` when `[0x2ABE20] &gt;= 0x1A`, untraced).</summary>
    public Action AllResearched { get; set; }

    /// <summary>Instrumentation: quanta shared out, projects completed.</summary>
    public long Quanta { get; private set; }
    public int Completions { get; private set; }

    /// <summary>⚠ PART of `0x1B6880(mgr, slot, cat, item)`: once the (untraced) eligibility test has
    /// passed, the slot is reset (`0x1B7600`: complete 0, active 1, progress 0, weight 0, item -1), then
    /// item and category set, `required = work &lt;&lt; 12` (`0x1B7378`) and `progress = percentDone *
    /// required / 100` (`0x1B7650`, `divu`) -- a project resumes from the percent the database holds.
    /// ⚠ The eligibility test (slot 4, or the DBA group `0x12B758` &lt;= threshold `+0x98[slot]`, after
    /// `0x1B7130`) is not traced: the caller has made that decision. Returns false (nothing changed) when
    /// the slot is already active, as `0x1B6880` does.</summary>
    public bool Start(int slot, int category, int item, int requiredWork, int percentDone = 0)
    {
        if ((uint)slot >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
        var s = _slots[slot];
        if (s.Active) return false;
        s.Complete = false; s.Active = true; s.Progress = 0; s.Weight = 0; s.Item = -1;   // 0x1B7600
        s.Item = item;                                                                     // 0x1B7490
        s.Category = category;
        s.Required = unchecked((uint)requiredWork << 12);                                  // 0x1B7378
        s.Progress = unchecked((uint)percentDone * s.Required) / 100u;                     // 0x1B7650
        return true;
    }

    /// <summary>⭐ `0x1B6A38(mgr, work)`, READ (MIPS `0x1B6A38..0x1B6B24`): n = active slots; none → nothing.
    /// For each active slot in order: `amount = (budget * work) &lt;&lt; 12` `divu` `(n * 100)`, then
    /// `0x1B7388` (progress += amount; percent filed; `progress &gt;= required` (unsigned) → complete). A
    /// slot that completed in this call prints "Researched" and asks `0x104358(0xFFFF)`; nothing left →
    /// `0x151998`.</summary>
    public void Contribute(int work)
    {
        uint n = (uint)_slots.Count(s => s.Active);
        if (n == 0) return;
        Quanta++;
        uint share = unchecked((uint)(Budget * work) << 12) / (n * 100u);
        foreach (var s in _slots)
        {
            if (!s.Active) continue;
            s.Progress = unchecked(s.Progress + share);                   // 0x1B7388
            // ⭐ EVERY quantum files the percent of the tier being researched (`0x12BAF8`), and at 100 that IS the
            // unlock -- the level++ that makes the build menu offer it.
            if (Database is { } db)
                db.File(s.Category, s.Item, db.Level(s.Category, s.Item), (int)s.Percent);
            if (!(s.Progress < s.Required)) Complete(s);                  // sltu
            if (!s.Complete) continue;
            if (AnythingLeftToResearch is { } left && !left()) AllResearched?.Invoke();   // 0x104358 / 0x151998
        }
    }

    /// <summary>`0x1B74A0(slot, 1)`: complete, inactive, the thresholds' dirty flag set, the category's message,
    /// item = -1. ⚠ NOT the unlock (`0x12AED8` there is a release): the unlock is the level++ the same quantum's
    /// <see cref="ResearchDatabase.File"/> made. And nothing starts the next item (findings/research.md §2.5).</summary>
    void Complete(ResearchProject s)
    {
        s.Complete = true;
        s.Active = false;                                                  // 0x1B7498(slot, 0)
        CompletedFlag = true;                                              // *0x1B6798() = 1
        Completions++;
        Researched?.Invoke(s);                                             // level++ in 0x12BAF8
        if (CompletionMessage(s.Category, s.Item) is int message) Advisor?.Invoke(message);
        s.Item = -1;
    }

    /// <summary>⭐ `0x1B74A0`'s message, READ (jump table `0x3661F0`, MIPS `0x1B7510..0x1B75D8`): by category
    /// 1, 3, 6, 7 → `0x12BA08` &lt; 2 ? 0x4B RIDE_RESEARCHED : (no mechanics ? 0x7E NEW_UPGRADE_HIRE_MECHANIC
    /// : 0x4C ADDON_RESEARCHED); 2 → 0x4F FEATURE; 4 → 0x4D SHOP; 5 → 0x4E SIDESHOW; 8 → 0x4C ADDON; any
    /// other category posts the request's UNSET id -- ⚠ CORRECTED (findings/advisor-messages.md §10.2): the id is
    /// never set, so it is the ctor's 0x114 = 276 (<see cref="AdvisorRequest.UnsetId"/>), past the 275-record
    /// table (INFERRED unreachable: categories 1..8 are all handled). Only rows 740/566/713/652/556 carry
    /// text; none has a voice; 0x7E is row 310, silent.</summary>
    public int? CompletionMessage(int category, int item) => category switch
    {
        1 or 3 or 6 or 7 => (ItemLevel?.Invoke(category, item) ?? 0) < 2 ? 0x4B
                            : (MechanicCount?.Invoke() ?? 1) == 0 ? 0x7E : 0x4C,
        2 => 0x4F,
        4 => 0x4D,
        5 => 0x4E,
        8 => 0x4C,
        _ => AdvisorRequest.UnsetId,
    };
}
