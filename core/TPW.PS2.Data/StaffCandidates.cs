namespace TPW.PS2.Data;

/// <summary>One candidate record of CStaffDatabase: 0x20 bytes at `factory + 0xF4 + n*0x20`,
/// built by `0x12A758` (findings/staff-management.md §1.2, staff.md §2.4). READ.
///
/// ⭐ The candidate is the PERSON, the staff member is the JOB. Firing hands the same record back
/// unchanged (`0x1DC6F0` sets `+0x1C = 1`), so the name, pay grade and motivation come back with it
/// -- but a trained level lives on the staff object and is lost (`0x1DB618` re-reads `+0x14`).</summary>
public sealed partial class StaffCandidate
{
    internal StaffCandidate(StaffKind kind, int slot, int payGrade, int motivation)
    {
        Kind = kind; Slot = slot; PayGrade = payGrade; Motivation = motivation;
        NameRow = StaffTables.CandidateNameRows(kind)[slot];
        RecordConstant = StaffTables.CandidateRecordConstant(kind);
    }

    /// <summary>`+0x10`, the DB kind.</summary>
    public StaffKind Kind { get; }
    /// <summary>`+2` (u16), 0..4: the hire tool's mode-1 argument and the staff's `C+0x49`.</summary>
    public int Slot { get; }
    /// <summary>`+4`: the text row of the candidate's name, from the per-kind table
    /// (<see cref="StaffTables.CandidateNameRows"/>). Resolve with <see cref="Name"/>.</summary>
    public int NameRow { get; }
    /// <summary>`+0xC`: 9/0xD/0xB/0xA/0xC by kind; ⚠ no reader found.</summary>
    public int RecordConstant { get; }
    /// <summary>`+0x14`, **pay grade = starting level**, `rand_u32 &amp; 1` (`0x12B5B8`). The Hire
    /// screen shows it plus one; activation copies `&amp; 7` into `C+0x48`.</summary>
    public int PayGrade { get; }
    /// <summary>`+0x18`, `rand_u32 % 60 + 20` (`divu`, so 20..79). ⭐ DISPLAY-ONLY: the Hire screen's
    /// bar is its only consumer -- the activation's copy at `C+0x4A` has no reader anywhere in the
    /// ELF (findings/staff-management.md §1.3). All Staff's "Motivation" is a different number,
    /// <see cref="StaffMember.DisplayedMotivation"/>.</summary>
    public int Motivation { get; }
    /// <summary>`+0x1C`: 1 available, 0 hired. Activation clears it; fire (`0x1DC6F0`) and a
    /// cancelled hire (`0x12AE68`) set it back.</summary>
    public bool Available { get; internal set; } = true;

    /// <summary>The Hire screen's "Monthly Wage": `0x12B630` with `L &lt; 0`, i.e. the pay grade.</summary>
    public int MonthlyWage => StaffTables.MonthlyWage(Kind, PayGrade);

    /// <summary>`0x12B548 → 0x1DFA58`: the name, from the text database's row.</summary>
    public string Name(TextDatabase text, string language = "eng") => text?.Text(language, NameRow);
}

/// <summary>⭐⭐ CStaffDatabase: 25 candidates, five per kind, rolled ONCE per park session and
/// never refreshed (findings/staff-management.md §1.1).
///
/// `0x12A878` runs kind 0..4, slot 0..`GetNumberOfStaff(kind)`-1 (= 5), and `0x12A758` appends
/// each record; `0x12B5B8` then draws **pay grade first** (`rand_u32 &amp; 1`) and **motivation
/// second** (`rand_u32 % 0x3C + 0x14`). READ from the decompile of `0x12B5B8`.
///
/// ⚠ THE STREAM: `rand_u32` is `0x144870`, the guest stream. `rand_u32 &amp; 1` and
/// `rand_u32 % 60` are exactly `rand(2)` and `rand(60)` of one draw (`rand(n)` is `0x1448E0` =
/// `rand_u32 % n`), so the port's injected `rand(n)` expresses them without a second generator.
/// Which draw of the console's stream lands here is not reproducible anyway (native-guest-motion.md).
///
/// ⚠ LIFETIME: natively a lazy singleton made at first use in a park session (`0x12A550`) and freed
/// at park teardown (`0x150E80 → 0x12A5B8`). <see cref="ParkStaff"/> builds it lazily too, so the
/// draws happen at the same moment -- the first time anything asks for a candidate.
///
/// ⚠ NOT SAVED: `0x1C2968` saves DB categories 1..8 (research) only, so a loaded park re-rolls the
/// unhired candidates (INFERRED). Save/load is out of scope for this step.</summary>
public sealed partial class StaffCandidateDatabase
{
    readonly StaffCandidate[] _records;

    public StaffCandidateDatabase(Func<int, int> random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var list = new List<StaffCandidate>();
        for (int kind = 0; kind < 5; kind++)                       // 0x12A878: kind 0..4
            for (int slot = 0; slot < StaffTables.PoolSize; slot++) // slot 0..GetNumberOfStaff-1
            {
                int payGrade = random(2);                            // 0x12B5B8: rand_u32 & 1
                int motivation = random(60) + 20;                    //           rand_u32 % 60 + 20
                list.Add(new StaffCandidate((StaffKind)kind, slot, payGrade, motivation));
            }
        _records = list.ToArray();
    }

    /// <summary>All 25, in the order `0x12A878` appended them (kind-major).</summary>
    public IReadOnlyList<StaffCandidate> All => _records;

    /// <summary>`0x12AD28(factory, kind, slot)`: the slot-th record of that kind.</summary>
    public StaffCandidate For(StaffKind kind, int slot)
    {
        if ((uint)slot >= StaffTables.PoolSize) throw new ArgumentOutOfRangeException(nameof(slot));
        return _records[(int)kind * StaffTables.PoolSize + slot];
    }

    /// <summary>`0x15D2A0(list, kind)`: the Hire tab's list -- slots 0..4 in order, only those
    /// with `+0x1C != 0`.</summary>
    public IEnumerable<StaffCandidate> Available(StaffKind kind)
    {
        for (int slot = 0; slot < StaffTables.PoolSize; slot++)
            if (For(kind, slot).Available) yield return For(kind, slot);
    }
}
