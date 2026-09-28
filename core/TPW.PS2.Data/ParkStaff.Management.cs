namespace TPW.PS2.Data;

/// <summary>What training a member came to (<see cref="ParkStaff.Train"/>).</summary>
public enum TrainingResult
{
    /// <summary>Trained: debited, level +1, morale 100, tiredness 0; sound 0x12F.</summary>
    Trained,
    /// <summary>The balance is below `cost * 10`: sound 0xAF, nothing changes (`0x1FF6F8`).</summary>
    CannotAfford,
    /// <summary>The list box never offers Training at level 4 (`0x15DA8C`): nothing happens.</summary>
    NotOffered,
}

/// <summary>⭐⭐ STAFF STEP 5, MANAGEMENT: strikes, wages due, training, the Staff Room's management side,
/// the research manager, the advisor's staff producers and the patrol-area tool's core, READ in
/// findings/staff-management.md §3-§12 (re-checked against the MIPS where cited).
///
/// ⭐ For the laptop's four staff screens (cow tools builds them), the data API is here and on
/// <see cref="StaffMember"/>:
/// <list type="bullet">
/// <item>All Staff (`0x10BB68`/`0x10C138`): <see cref="TypesWithStaff"/> and <see cref="MembersOfType"/>;
/// per member <see cref="StaffMember.SkillBar"/>, <see cref="StaffMember.DisplayedMotivation"/>,
/// <see cref="StaffMember.Tiredness"/>, <see cref="StaffMember.DaysEmployed"/> with
/// <see cref="StaffTables.TimeEmployed"/>, <see cref="StaffMember.MonthlyWage"/>, <see cref="StaffMember.Candidate"/>'s name;</item>
/// <item>Single Staff (`0x1D8EE8`): <see cref="SingleStaffOptions"/>; Fire = <see cref="Fire"/>, Zoom To =
/// <see cref="StaffMember.ZoomTarget"/>, Set Patrol Area = <see cref="StaffPatrolTool"/> (the viewer's
/// `BeginPatrolArea`), Training = <see cref="TrainingOffered"/>;</item>
/// <item>Training (`0x1FF4A8`): <see cref="StaffMember.TrainingCost"/>, <see cref="StaffMember.WageAfterTraining"/>,
/// <see cref="StaffMember.TrainingBar"/>, <see cref="Train"/>;</item>
/// <item>Staff Room (`0x1DCAA0`): <see cref="StaffRoomCounts"/>, <see cref="KickOutOptions"/>, <see cref="KickOut"/>.</item>
/// </list>
///
/// ⚠ ADAPTERS, each labelled where it lives: <see cref="ParkRunning"/> (`0x151258`),
/// <see cref="UiSound"/> and <see cref="Focus"/> (the view's), the staff-room ambience poll
/// (<see cref="StaffRoomAmbience"/>), and the feature classification of the coverage and count producers
/// (<see cref="StaffFeature"/>'s DBA bits standing in for `vt+0x134`, `0x130858`, `0x1308C8`).</summary>
public sealed partial class ParkStaff
{
    // ============================================================================================
    // Strikes: the calendar's per-type records `cal + 0x304 + (t-1)*8` (findings §9).

    readonly int[] _strikeStamp = new int[6];                             // +0x304, by type code
    readonly byte[] _strikeStage = new byte[6];                           // +0x308

    /// <summary>⚠ ADAPTER for `0x151258()` = `[0x2B7288]`, set 1 during park start (`0x151748`) and
    /// cleared at `0x1512C4`: the monthly strike check runs only while it is set. A park the port is
    /// playing is running; default true.</summary>
    public bool ParkRunning { get; set; } = true;
    /// <summary>`[0x2B74B0]`: cleared at `0x1515E8`; its only setter `0x14E278` has no caller (INFERRED a
    /// debug switch). When set, the check runs regardless of <see cref="ParkRunning"/> and every type is
    /// unhappy. Default false.</summary>
    public bool DebugEveryoneStrikes { get; set; }

    /// <summary>`cal + 0x308 + (t-1)*8`: the type's ladder stage 0..5.</summary>
    public int StrikeStage(StaffKind kind) => _strikeStage[StaffTables.TypeCode(kind)];
    /// <summary>`cal + 0x304 + (t-1)*8`: the month (0..11) last processed for the type; 0 at init
    /// (`0x16AF70`).</summary>
    public int StrikeStamp(StaffKind kind) => _strikeStamp[StaffTables.TypeCode(kind)];

    /// <summary>⭐ `0x16C120`, the monthly strike check, READ: if `[0x2B74B0] || 0x151258()`, for each
    /// type in the order ent, mech, guard, res, handy THAT HAS A STAFF MEMBER and whose stamp is not this
    /// month: stamp = month; striking → clear the flag and post STRIKE_END_BAD (`base + 0x2A`, silent) and
    /// run NO ladder this month; else the ladder `0x16C2A0`. Called by the calendar on a month change,
    /// BEFORE the wages (<see cref="ParkManagement"/>).</summary>
    public void MonthlyStrikeCheck(int month)
    {
        if (!DebugEveryoneStrikes && !ParkRunning) return;
        foreach (var kind in StaffTables.TypeCodeOrder)
        {
            if (Count(kind) == 0) continue;                                // list head != 0
            int t = StaffTables.TypeCode(kind);
            if (_strikeStamp[t] == month) continue;
            _strikeStamp[t] = month;
            if (IsStriking(kind))
            {
                SetStriking(kind, false);                                  // 0x16C9A0
                Advisor?.Invoke(StaffTables.StrikeMessage(kind, StaffTables.MessageStrikeEndBad));
            }
            else StrikeLadder(kind);
        }
    }

    /// <summary>⭐ `0x16C500`, the unhappy test, READ (MIPS; the energy test is `strikev`, the morale test
    /// `strikeh`):
    /// <code>
    ///   if [0x2B74B0]: unhappy
    ///   n = the type's staff; if n &gt;= 2 and Σ(100 − tiredness)/n &lt; 15 (signed div): unhappy
    ///   if n &gt;= 2 and !(14 &lt; (uint)(Σmorale/n)): unhappy
    /// </code>
    /// A single employee of a type never triggers it.</summary>
    public bool Unhappy(StaffKind kind)
    {
        if (DebugEveryoneStrikes) return true;
        var list = _active[kind];
        int n = list.Count;
        if (n < StaffTables.StrikeMinimumStaff) return false;
        int energy = 0, morale = 0;
        foreach (var m in list) { energy = energy + 100 - m.Tiredness; morale += m.Morale; }
        if (energy / n < StaffTables.StrikeEnergyBelow) return true;
        return !((uint)StaffTables.StrikeMoraleAtMost < (uint)(morale / n));
    }

    /// <summary>⭐ `0x16C2A0`, the ladder, READ (MIPS `0x16C30C..0x16C4D8`):
    /// <code>
    ///   not unhappy: stage 1 → HAPPIER (0x25+b, text+voice); any stage → 0; strike flag cleared
    ///   unhappy:     0 → 1 UNHAPPY (0x16+b, text+voice)
    ///                1 → 2 strike ON, VERY_UNHAPPY (0x1B+b, silent)
    ///                2,3,4 → 3,4,5 strike ON, STRIKING (0x20+b, silent)
    ///                5 → 0 strike OFF, STRIKE_END_BAD (0x2A+b, silent)
    /// </code></summary>
    void StrikeLadder(StaffKind kind)
    {
        int t = StaffTables.TypeCode(kind);
        if (!Unhappy(kind))
        {
            if (_strikeStage[t] == 1) Advisor?.Invoke(StaffTables.StrikeMessage(kind, StaffTables.MessageHappier));
            _strikeStage[t] = 0;
            SetStriking(kind, false);
            return;
        }
        switch (_strikeStage[t])
        {
            case 0: _strikeStage[t] = 1; Advisor?.Invoke(StaffTables.StrikeMessage(kind, StaffTables.MessageUnhappy)); break;
            case 1: SetStriking(kind, true); _strikeStage[t] = 2; Advisor?.Invoke(StaffTables.StrikeMessage(kind, StaffTables.MessageVeryUnhappy)); break;
            case 2: case 3: case 4:
                SetStriking(kind, true); _strikeStage[t]++; Advisor?.Invoke(StaffTables.StrikeMessage(kind, StaffTables.MessageStriking)); break;
            case 5: SetStriking(kind, false); _strikeStage[t] = 0; Advisor?.Invoke(StaffTables.StrikeMessage(kind, StaffTables.MessageStrikeEndBad)); break;
        }
    }

    // ============================================================================================
    // Wages (findings §6).

    /// <summary>⭐ `0x1008B8`: `Σ 10 × 0x1DC338` over the mechanics (`0x14D650`), entertainers (`0x14D670`),
    /// guards (`0x14D228`), researchers (`0x14D660`) and handymen (`0x14D640`) -- in tenths, what the
    /// month end debits. A member standing on strike (state 0xF) contributes 0; one hired this month his
    /// days over the PREVIOUS month's length.</summary>
    public int WagesDue()
    {
        int w = 0;
        foreach (var kind in new[] { StaffKind.Mechanic, StaffKind.Entertainer, StaffKind.Guard, StaffKind.Researcher, StaffKind.Handyman })
            foreach (var m in _active[kind]) w += m.ProRatedWage * 10;
        return w;
    }

    // ============================================================================================
    // Training (findings §7).

    /// <summary>The view's UI sounds (the category-0 `0x111150(audio, 0, id, 0)` calls): 0xAF refused,
    /// 0x12F trained, 0x1F the tool debit, 0xDB a patrol-tool press, 0xC5 a gold ticket.</summary>
    public Action<int> UiSound { get; set; }
    /// <summary>⚠ ADAPTER for `0x14BC28(staff)`, training's focus call: the focus becomes the member and the
    /// cursor moves to him unless UI mode `[0x3951D0]` is 0xC or 0xD. ⭐ Natively the next object loop
    /// clears that focus before any staff update (findings/staff-person.md §6), so all it leaves behind is
    /// the moved cursor -- the view's.</summary>
    public Action<StaffMember> Focus { get; set; }

    /// <summary>`0x15DA8C..0x15DAC4`: the list box adds "Training" (930) only while `L &lt; 4` and
    /// `*(int*)0x1497B0() != 0`. ⚠ That second word is not traced (findings §15); taken as set.</summary>
    public bool TrainingOffered(StaffMember member) => member.Level < StaffTables.MaxLevel;

    /// <summary>⭐ The Training screen's Cross `0x1FF6F8` → `0x1FF610`, READ:
    /// <code>
    ///   cost = 0x1DC2A8(S) (0 in free-build);  if balance (0x100688) &lt; cost*10: sound 0xAF, nothing
    ///   target = 0x3988A8[0] = L + 1                                   (0x1FF578)
    ///   0x14BC28(S)            -- focus the member
    ///   debit cost*10          -- 0x100698
    ///   if (L &amp; 7) &lt; target (unsigned): morale = 100, tiredness = 0
    ///   L = target &amp; 7; sound 0x12F
    /// </code>
    /// ⭐ The affordability test reads the raw balance even when the park may overspend (`park+8`):
    /// the screen refuses a purchase the debit itself would have allowed. There is no limit on how often
    /// or how soon; the cap is <see cref="TrainingOffered"/>.</summary>
    public TrainingResult Train(StaffMember member)
    {
        RequireActive(member);
        if (!TrainingOffered(member)) return TrainingResult.NotOffered;
        var money = Sim.Finances;
        int cost = member.TrainingCost(money.FreeBuild);                   // 0x1DC2A8
        if (money.Balance < cost * 10) { UiSound?.Invoke(StaffTables.UiSoundRefused); return TrainingResult.CannotAfford; }
        int target = member.Level + 1;                                     // 0x3988A8[0] = L + 1
        Focus?.Invoke(member);                                             // 0x14BC28
        money.Debit(cost * 10);                                            // 0x100698
        member.Train(target);                                              // the 0x1FF610 tail (= 0x1DC968)
        UiSound?.Invoke(StaffTables.UiSoundTrained);
        TrainingsBought++;
        return TrainingResult.Trained;
    }
    /// <summary>Instrumentation.</summary>
    public int TrainingsBought { get; private set; }

    /// <summary>⭐ The Single Staff list box `0x15D958 → 0x15DA74` (class 10), in its order: Set Patrol Area
    /// (79, always), Fire (906, when `vt+0x1E4`: base 1, a mechanic not in 0xE/0x10/0x11/0x34/0x36),
    /// Training (930, <see cref="TrainingOffered"/>), Zoom To (391, `*0x1497B0()` ⚠ taken as set). Grab
    /// (971) is NEVER added (its builder `0x15D718` has no caller). Values are the text rows.</summary>
    public IReadOnlyList<int> SingleStaffOptions(StaffMember member)
    {
        RequireActive(member);
        var rows = new List<int> { StaffTables.SetPatrolAreaTextRow };
        if (member.CanBeFired) rows.Add(StaffTables.FireTextRow);
        if (TrainingOffered(member)) rows.Add(StaffTables.TrainingTextRow);
        rows.Add(StaffTables.ZoomToTextRow);
        return rows;
    }

    // ============================================================================================
    // The laptop's lists (findings §12.1, §12.4).

    /// <summary>All Staff's type list (`0x10BB68`): the types that have staff, in the order Entertainers,
    /// Mechanics, Guards, Researchers, Cleaners (labels <see cref="StaffTables.TypeLabelRow"/>), from the
    /// raw counts `0x14D6D0`, `0x14D6B0`, `0x14D680`, `0x14D6C0`, `0x14D6A0`.</summary>
    public IEnumerable<StaffKind> TypesWithStaff() => StaffTables.TypeCodeOrder.Where(k => Count(k) > 0);

    /// <summary>All Staff's per-staff list, `0x15C7C8(list, class 10, sel)`: every map object (`0x14DA50`,
    /// the update order -- NEWEST first) of class 10 whose type code is `sel + 1`.</summary>
    public IEnumerable<StaffMember> MembersOfType(StaffKind kind) => _mapList.Where(m => m.Kind == kind);

    // ============================================================================================
    // The Staff Room (findings §8.3-§8.4, §12.4).

    /// <summary>`0x14AA90(room, type)`: members of the type in state 0x32 whose target is the room. The
    /// room is a <see cref="StaffFeature.Key"/> (the <see cref="ParkRide"/> for a scripted one).</summary>
    public int RestingIn(object room, StaffKind kind)
        => _active[kind].Count(m => m.State == StaffMember.StateResting && Equals(m.Target, room));

    /// <summary>⭐ The Staff Room screen's counts, `0x1DCC18`: <see cref="RestingIn"/> for type codes 1..5,
    /// i.e. in <see cref="StaffTables.TypeCodeOrder"/> (Entertainers, Mechanics, Guards, Researchers,
    /// Cleaners). The draw `0x1DCCA0` shows, for each nonzero count, label <see cref="StaffTables.TypeLabelRows"/>
    /// and the count at label column + 0xA0, rows stepping 20; all zero → "Empty" (row 55).</summary>
    public int[] StaffRoomCounts(object room) => StaffTables.TypeCodeOrder.Select(k => RestingIn(room, k)).ToArray();

    /// <summary>`0x15D850`: the room's Kick Out entries, one per type with a member resting in THIS room,
    /// in the order Mechanics, Researchers, Cleaners, Entertainers, Guards.</summary>
    public IReadOnlyList<StaffKind> KickOutOptions(object room)
        => StaffTables.KickOutOrder.Where(k => RestingIn(room, k) > 0).ToList();

    /// <summary>⭐ Kick Out, `0x124478..0x124678`: walk the type's active list and call `0x1DBFD0` (shown,
    /// target 0, state 0xD -- the rest's own end) on every member in state 0x32 whose target is the room.
    /// ⭐ TIREDNESS IS NOT TOUCHED, so anyone still &gt;= 81 heads back to the nearest room at his next find
    /// work (INFERRED consequence, findings §8.3). Returns how many were kicked out.</summary>
    public int KickOut(object room, StaffKind kind)
    {
        int n = 0;
        foreach (var m in _active[kind].ToArray())
            if (m.State == StaffMember.StateResting && Equals(m.Target, room)) { m.EndRest(); n++; }
        return n;
    }

    /// <summary>⭐ The staff room's ambience (`0x130510`, the feature's status change `vt+0x164`): when the
    /// new status is 2 and the feature has DBA bit 1, start looping bank 8 event 0xBC at the room (handle
    /// `+0xB8`, zeroed first WITHOUT a stop); `vt+0x10C` = `0x130498` (its removal) stops it. So it plays
    /// whenever the room is open, occupied or not -- and the Laser Show, which carries bit 1, plays it too.
    /// (feature, true) = start, (feature, false) = stop.
    ///
    /// ⚠ ADAPTER: the port's placements raise no status-change call, so <see cref="Update"/> polls: a room
    /// seen with a status it did not have last update and now 2 starts; a room gone from the list stops.
    /// ⭐ `VAR_STAFFIN` HAS NO WRITER (findings §8.4): the room's own script particle `P_EFFECT_BrewUp` and
    /// sound `EVT_STAFF_ROOM` never start on PS2. Nothing here writes it, deliberately.</summary>
    public Action<StaffFeature, bool> StaffRoomAmbience { get; set; }
    readonly Dictionary<object, (StaffFeature Feature, byte Status)> _rooms = new();
    void PollStaffRooms()
    {
        var seen = new HashSet<object>();
        foreach (var f in PlacedFeatures())
        {
            if (!f.IsStaffRoom || f.Key == null) continue;
            seen.Add(f.Key);
            bool had = _rooms.TryGetValue(f.Key, out var old);
            _rooms[f.Key] = (f, f.Status);
            if ((!had || old.Status != f.Status) && f.Status == 2) StaffRoomAmbience?.Invoke(f, true);
        }
        foreach (var gone in _rooms.Keys.Where(k => !seen.Contains(k)).ToArray())
        {
            StaffRoomAmbience?.Invoke(_rooms[gone].Feature, false);
            _rooms.Remove(gone);
        }
    }

    // ============================================================================================
    // Research (findings §10).

    ResearchManager _research;
    /// <summary>⭐ `0x1B6798`, the research manager singleton, built lazily on first use (budget 80) and
    /// owned by this park (natively freed by `0x1B67F8` at teardown). Its mechanic count for message 0x7E
    /// is this park's.</summary>
    public ResearchManager Research => _research ??= new ResearchManager
    {
        MechanicCount = () => Count(StaffKind.Mechanic),
        Advisor = id => Advisor?.Invoke(id),
    };

    // ============================================================================================
    // The advisor's producers (findings §11.1, §11.3, §11.4).

    StaffMember _hireHeld;
    /// <summary>The member the hire tool (mode 1) is carrying, or null (`0x14D6E0`'s "held by tool 1").</summary>
    public StaffMember HireHeld => _hireHeld;

    IEnumerable<StaffMember> OfMask(int mask, bool researchers) => mask switch
    {
        1 => _active[StaffKind.Mechanic],
        2 => _active[StaffKind.Handyman],
        4 => _active[StaffKind.Guard],
        8 => _active[StaffKind.Entertainer],
        0x10 when researchers => _active[StaffKind.Researcher],
        _ => Enumerable.Empty<StaffMember>(),
    };

    /// <summary>Variables 6..10, `0x14D830/0x14D7C0/0x14D750/0x14D7F8/0x14D788`: the type's count, MINUS ONE
    /// when the hire tool is in mode 1 carrying one of that type (`0x14D6E0`).</summary>
    public int AdvisorCount(StaffKind kind) => Count(kind) - (_hireHeld != null && _hireHeld.Kind == kind ? 1 : 0);

    /// <summary>`0x1053A8(mask)`, READ: `100 − set*100/n` when fewer than all n of the type have a patrol
    /// area, else 0 (so 0 with none employed). Masks 1, 2, 4, 8 only.</summary>
    public int NoPatrolAreaPercent(int mask)
    {
        int n = 0, set = 0;
        foreach (var m in OfMask(mask, researchers: false)) { n++; if (m.HasPatrolArea) set++; }
        return set < n ? (100 - set * 100 / n) & 0xFFFF : 0;
    }

    /// <summary>`0x105538(mask)`, READ: `ΣL * 100 / (n*4)`, 0 with none. Masks 1, 2, 4, 8, 0x10.</summary>
    public int TrainingPercent(int mask)
    {
        int n = 0, levels = 0;
        foreach (var m in OfMask(mask, researchers: true)) { n++; levels += m.Level; }
        return n == 0 ? 0 : (levels * 100 / (n * 4)) & 0xFFFF;
    }

    /// <summary>`0x105688(mask)`: the highest tiredness of the type (0 with none); `0x105798` = the max over
    /// masks 1, 2, 4, 8, 0x10 (<see cref="MaxTiredness()"/>, advisor variable 55).</summary>
    public int MaxTiredness(int mask)
    {
        int max = 0;
        foreach (var m in OfMask(mask, researchers: true)) if (max < m.Tiredness) max = m.Tiredness;
        return max & 0xFFFF;
    }
    public int MaxTiredness() => new[] { 1, 2, 4, 8, 0x10 }.Max(MaxTiredness);

    /// <summary>⭐ `0x104FB0(mask)`, patrol COVERAGE of the paths, READ (decompile + MIPS):
    /// <code>
    ///   W4 = W &gt;&gt; 2, H4 = H &gt;&gt; 2; block (c, r) = 1 if any of (4c|4c+2, 4r|4r+2) is a path tile
    ///   each staff of the type WITH an area: blocks x0&gt;&gt;2..x1&gt;&gt;2 × z0&gt;&gt;2..z1&gt;&gt;2 (inclusive) |= 2,
    ///       indexed r*W4 + c -- a column past W4-1 spills into the next row, as natively
    ///   covered = blocks == 3, path = blocks ∈ {1, 3}
    ///   0 if covered == 0; 100 if path == 0 or path &lt;= covered; else covered*100/path
    /// </code>
    /// Masks 1, 2, 4, 8 (a researcher mask marks nothing). ⚠ Natively a spill past the LAST row writes
    /// beyond the `*0x2A6A7C` buffer; the port drops those writes.</summary>
    public int PatrolCoverage(int mask)
    {
        int w4 = Tiles.Width >> 2, h4 = Tiles.Height >> 2;
        var grid = new byte[Math.Max(0, w4 * h4)];
        for (int r = 0; r < h4; r++)
            for (int c = 0; c < w4; c++)
            {
                bool path = false;
                for (int x = 4 * c; x < 4 * c + 4 && !path; x += 2)
                    for (int z = 4 * r; z < 4 * r + 4 && !path; z += 2)
                        path = Tiles.IsPath(x, z);                         // 0x14E138 → 0x1E6338
                grid[r * w4 + c] = (byte)(path ? 1 : 0);
            }
        foreach (var m in OfMask(mask, researchers: false))
        {
            if (!m.HasPatrolArea) continue;                                // 0x1DC698
            for (int c = m.PatrolX0 >> 2; c <= m.PatrolX1 >> 2; c++)       // 0x1DC4F8: s8 → s16, >> 2
                for (int r = m.PatrolZ0 >> 2; r <= m.PatrolZ1 >> 2; r++)
                {
                    int i = r * w4 + c;
                    if ((uint)i < (uint)grid.Length) grid[i] |= 2;
                }
        }
        int covered = 0, paths = 0;
        foreach (byte b in grid) { if (b == 1) paths++; else if (b == 3) { covered++; paths++; } }
        if (covered == 0) return 0;
        if (paths == 0 || paths <= covered) return 100;
        return covered * 100 / paths & 0xFFFF;
    }

    /// <summary>`0x103970(mask)`, the placed-feature half (bits 0x20, 0x40, 0x80), READ: every feature with
    /// status `+0xA2` != 0 is classed ONCE, in the order toilet (`vt+0x134`), camera (`0x130858`), staff room
    /// (`0x1308C8`), and counted when its class's bit is in the mask -- advisor variables 18 (0x20), 19
    /// (0x80, incl. the Laser Show), 20 (0x40). ⚠ The other bits (1, 2, 4, 8, 0x100, 0x200: other object
    /// counts `0x14CE68..0x14CF88`; 0x10: every other feature) are not staff's and are refused here.</summary>
    public int FeatureCount(int mask)
    {
        if ((mask & ~0xE0) != 0) throw new ArgumentOutOfRangeException(nameof(mask), "only 0x20 | 0x40 | 0x80 are ported");
        int n = 0;
        foreach (var f in PlacedFeatures())
        {
            if (f.Status == 0) continue;
            int bit = f.IsToilet ? 0x20 : f.IsCamera ? 0x40 : f.IsStaffRoom ? 0x80 : 0;
            if ((mask & bit) != 0) n++;
        }
        return n;
    }

    /// <summary>⭐ `0x104CE0(mask)`, feature COVERAGE, READ (decompile + MIPS): a bit grid of 16×16-cell
    /// blocks, `ceil(W/128)` bytes a row and `ceil(H/16)` rows; each placed feature with status != 0 that
    /// matches ANY bit of the mask (0x20 toilet, 0x40 camera, 0x80 staff room -- not exclusive, unlike
    /// <see cref="FeatureCount"/>) sets bit `(x&gt;&gt;4) &amp; 7` of byte `((x&gt;&gt;4) &gt;&gt; 3) + (z&gt;&gt;4)*bytesPerRow` at its
    /// `vt+0x74` ORIGIN cell; result = `popcount * 100 / bytes`. ⭐ With 8 blocks per byte that is 8× the
    /// covered fraction (INFERRED unintended, findings §11.4). ⚠ Mask 0x10 (any feature) is not ported.</summary>
    public int FeatureCoverage(int mask)
    {
        if ((mask & ~0xE0) != 0) throw new ArgumentOutOfRangeException(nameof(mask), "only 0x20 | 0x40 | 0x80 are ported");
        int bytesPerRow = (Tiles.Width + 0x7F) >> 7, rows = (Tiles.Height + 0xF) >> 4;
        int n = bytesPerRow * rows;
        if (n == 0) throw new DivideByZeroException("0x104CE0 traps (break 7) on an empty grid");
        var bits = new byte[n];
        foreach (var f in PlacedFeatures())
        {
            if (f.Status == 0) continue;
            bool match = ((mask & 0x20) != 0 && f.IsToilet) || ((mask & 0x40) != 0 && f.IsCamera)
                      || ((mask & 0x80) != 0 && f.IsStaffRoom);
            if (!match) continue;
            int bx = f.Origin.X >> 4, bz = f.Origin.Z >> 4;
            int i = (bx >> 3) + bz * bytesPerRow;
            if ((uint)i < (uint)n) bits[i] |= (byte)(1 << (bx & 7));
        }
        int count = 0;
        foreach (byte b in bits) count += System.Numerics.BitOperations.PopCount(b);
        return count * 100 / n & 0xFFFF;
    }

    /// <summary>⭐ The advisor's rule-VM variables that STAFF produce (the producer switch `0x10DE38`, table
    /// `0x359860`; findings §11.1), written into <paramref name="v"/> (79 slots, as
    /// <see cref="AdvisorRules.Evaluate"/> takes them) as signed halfwords:
    /// <code>
    ///   v6..v10  AdvisorCount(ent, mech, guard, res, handy)    v11  their sum
    ///   v13      litter items (0x14D208)
    ///   v18 v19 v20  FeatureCount(0x20 toilets / 0x80 staff rooms / 0x40 cameras)
    ///   v33..v36 NoPatrolAreaPercent(1 mech, 2 handy, 4 guard, 8 ent)
    ///   v37..v40 PatrolCoverage(1, 2, 4, 8)
    ///   v41..v45 TrainingPercent(1, 2, 4, 8, 0x10 res)
    ///   v46      mechanics striking (0x16C988(cal, 2); no rule reads it)
    ///   v49      WAGES_HIGH (ParkFinances.WagesHigh)
    ///   v55      the highest tiredness of any staff member (0x105798)
    /// </code>
    /// ⚠ Nothing else is written: v12 (guests in the park, `0x14D690`) is the guests' side, the event
    /// counters (v56..v77) live with whoever raises them, v52 is unresolved (findings §15). And ⚠ the rule
    /// SCHEDULER (`0x10DC50`: one state slot and one rule per invocation, the delay/failure days) is the
    /// advisor's and is not built -- these producers are what it would call.</summary>
    public void FillAdvisorVariables(Span<short> v)
    {
        if (v.Length < 79) throw new ArgumentException("the advisor VM has 79 variables", nameof(v));
        short S(int x) => unchecked((short)x);
        v[6] = S(AdvisorCount(StaffKind.Entertainer)); v[7] = S(AdvisorCount(StaffKind.Mechanic));
        v[8] = S(AdvisorCount(StaffKind.Guard)); v[9] = S(AdvisorCount(StaffKind.Researcher));
        v[10] = S(AdvisorCount(StaffKind.Handyman));
        v[11] = S(v[6] + v[7] + v[8] + v[9] + v[10]);
        v[13] = S(Litter.Count);
        v[18] = S(FeatureCount(0x20)); v[19] = S(FeatureCount(0x80)); v[20] = S(FeatureCount(0x40));
        int[] four = { 1, 2, 4, 8 };
        for (int i = 0; i < 4; i++) { v[33 + i] = S(NoPatrolAreaPercent(four[i])); v[37 + i] = S(PatrolCoverage(four[i])); }
        int[] five = { 1, 2, 4, 8, 0x10 };
        for (int i = 0; i < 5; i++) v[41 + i] = S(TrainingPercent(five[i]));
        v[46] = S(IsStriking(StaffKind.Mechanic) ? 1 : 0);
        v[49] = S(Sim.Finances.WagesHigh ? 1 : 0);
        v[55] = S(MaxTiredness());
    }
}

/// <summary>⭐⭐ THE PATROL-AREA TOOL, tool mode 17 (object `0x388F38`, vtable `0x35BC38`), READ in
/// findings/staff-management.md §4 against `0x128C48..0x128E8C` (decompile + MIPS). The laptop's "Set
/// Patrol Area" (`0x1242A0`) sets mode 17 through `0x125460`, which first calls `vt+0x6C` = `0x129068`
/// (`tool+0x24 = staff`) and then enter.
/// <code>
///   enter   0x128C48  +0x14 = 1 (armed), +0x18 = 0 (no first corner)
///   cursor  0x128C68  tool+0..+7 = the cursor cell; no first corner → corner = cursor;
///                     staff C+0x2C |= 0x40 (HELD -- only the flag: state and route are kept)
///   draw    0x128CE8  0x1504F8(minX, minZ, 0xA5, 0, (maxX−minX+1) &amp; 0xFF, (maxZ−minZ+1) &amp; 0xFF)
///   Cross   0x128D98  first press: +0x18 = 1, corner = cursor.  second press: corner.x += 1,
///                     corner.z += 1, 0x1DC490(staff, corner, cursor), unhold, 0x126408 (debit
///                     tool+8 × 10 = 0 -- +8 is never written, .bss -- and sound 0x1F), mode 0.
///                     Sound 0xDB on either press.
///   Triangle 0x128E78 unhold; first corner placed → forget it and stay; else leave (mode 0)
/// </code>
/// ⭐⭐ THE OFF-BY-ONE, SHIPPED (§4.3): the highlight is min..max of (first corner, cursor) inclusive, but
/// the rectangle stored is min/max of (first corner + 1, cursor) and the patrol walks `[x0, x1)`. With the
/// first corner the LARGER, the walked cells are exactly the highlighted ones; with it the SMALLER both
/// edges are lost (c+1..u−1), and u == c+1 stores an empty range that still counts as "set".</summary>
public sealed class StaffPatrolTool
{
    readonly ParkStaff _staff;

    /// <summary>`0x125460(toolmgr, 17)`: `vt+0x6C` then enter.</summary>
    public StaffPatrolTool(ParkStaff staff, StaffMember member)
    {
        _staff = staff ?? throw new ArgumentNullException(nameof(staff));
        Member = member ?? throw new ArgumentNullException(nameof(member));   // 0x129068: tool+0x24
        if (!member.Active) throw new InvalidOperationException("the patrol tool needs a hired member");
        Armed = true; CornerPlaced = false;                                     // 0x128C48
        Open = true;
    }

    /// <summary>`tool+0x24`, the staff member whose area is being set.</summary>
    public StaffMember Member { get; }
    /// <summary>`+0x14`: set by enter; a press with it clear plays 0xAF (never, enter sets it).</summary>
    public bool Armed { get; }
    /// <summary>`+0x18`: the first corner has been placed.</summary>
    public bool CornerPlaced { get; private set; }
    /// <summary>`tool+0..+7`: the cursor cell (`mgr+0x3C`), set by <see cref="MoveCursor"/>.</summary>
    public ParkCell Cursor { get; private set; }
    /// <summary>`+0x1C/+0x20`: the first corner (tracks the cursor until placed).</summary>
    public ParkCell Corner { get; private set; }
    /// <summary>False once the tool has left (mode 0).</summary>
    public bool Open { get; private set; }

    /// <summary>`vt+0x14` = `0x128C68`, every frame: the cursor cell; with no first corner yet the corner
    /// follows it; the member is HELD (`C+0x2C |= 0x40`) so he stands still while the area is drawn.</summary>
    public void MoveCursor(ParkCell cell)
    {
        if (!Open) return;
        Cursor = cell;
        if (!CornerPlaced) Corner = cell;
        Member.SetHeld(true);
    }

    /// <summary>`0x128CE8`: the ground highlight `0x1504F8(minX, minZ, 0xA5, 0, w, h)` -- min..max of the
    /// corner and the cursor INCLUSIVE, the sizes masked to a byte. Texture id 0xA5 (165, the teal marker).</summary>
    public (int X, int Z, int Width, int Depth) Highlight
    {
        get
        {
            int x0 = Math.Min(Cursor.X, Corner.X), z0 = Math.Min(Cursor.Z, Corner.Z);
            int x1 = Math.Max(Cursor.X, Corner.X), z1 = Math.Max(Cursor.Z, Corner.Z);
            return (x0, z0, (x1 - x0 + 1) & 0xFF, (z1 - z0 + 1) & 0xFF);
        }
    }
    public const int HighlightTexture = 0xA5;

    /// <summary>`vt+0x2C` = `0x128D98`, Cross. Returns true when the area was set (the tool has left).</summary>
    public bool Press()
    {
        if (!Open) return false;
        if (!Armed) { _staff.UiSound?.Invoke(StaffTables.UiSoundRefused); return false; }
        bool set = false;
        if (!CornerPlaced) { CornerPlaced = true; Corner = Cursor; }
        else
        {
            var bumped = new ParkCell(unchecked((short)(Corner.X + 1)), unchecked((short)(Corner.Z + 1)));   // ⭐ the off-by-one
            Corner = bumped;
            Member.SetPatrolArea(bumped, Cursor);                                // 0x1DC490(staff, corner, cursor)
            Member.SetHeld(false);                                               // C+0x2C &= 0xFFBF
            _staff.Sim.Finances.Debit(0);                                        // 0x126408: tool+8 * 10 = 0
            _staff.UiSound?.Invoke(StaffTables.UiSoundCash);                     //           and sound 0x1F
            Open = false;                                                        // 0x126460: mode 0
            set = true;
        }
        _staff.UiSound?.Invoke(StaffTables.UiSoundPatrolPress);                  // 0xDB on either press
        return set;
    }

    /// <summary>`vt+0x34` = `0x128E78`, Triangle: unhold; with a first corner placed forget it and stay,
    /// else leave. Returns true when the tool has left.</summary>
    public bool Cancel()
    {
        if (!Open) return true;
        Member.SetHeld(false);
        if (!CornerPlaced) { Open = false; return true; }                       // 0x126460
        CornerPlaced = false;
        return false;
    }
}
