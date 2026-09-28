namespace TPW.PS2.Data;

/// <summary>⭐ THE RESEARCHER, vtable `0x365F50`, READ in findings/staff-management.md §10.2 against the
/// decompile of `0x1B5F28..0x1B6618` and the MIPS of `0x1B60A8..0x1B62FC`.
///
/// <code>
///   state 0     find work 0x1B60A8: r = rand(10) FIRST; tired/strike check; then r &lt; 3 → state 0x1F,
///               else state 0xD (patrol) with sound 8/0xA8 (handle P+0x58)
///   state 0x1F  0x1B61C0, ONE tick: logical 16; 0x1B6A38(mgr, R[L]); tiredness += (budget − 80) divu 3
///               (as s8; capped 100, no floor); state 0, depth 0; sound 8/0x8A (handle P+0x5C)
/// </code>
/// Everything else is the staff base's (`0x1B6308` → `0x1DC018`; arrival `0x1B6068` → `0x1DB970`;
/// release `0x1B63F8` → `0x1DC780`). No idle 1-in-16 sound: the researcher's find-work has none. The
/// research rate depends on how often he comes back to state 0 -- 70 % of his decisions are patrols
/// (INFERRED, §10.2).</summary>
public sealed partial class Researcher : StaffMember
{
    /// <summary>State 0x1F (`sb` at `0x1B6100`): researching, for one tick.</summary>
    public const byte StateResearching = 0x1F;

    internal Researcher(ParkStaff park, int poolSlot) : base(park, StaffKind.Researcher, poolSlot) { }

    /// <summary>Instrumentation: research quanta this member has run.</summary>
    public long Quanta { get; private set; }

    /// <summary>`0x1B6308`: 0 → find work, 0x1F → the quantum, else the base.</summary>
    protected override bool UpdateJobState()
    {
        if (State != StateResearching) return false;
        Research();
        return true;
    }

    /// <summary>`vt+0x1A4` = `0x1B60A8`, READ (MIPS `0x1B60C4..0x1B61A4`): the `rand(10)` is drawn BEFORE
    /// the tired/strike check, so a resting or striking researcher still consumes it.</summary>
    protected override void FindWork()
    {
        int r = Park.Random(StaffTables.ResearchChanceOutOf);
        if (TiredOrStriking()) return;
        GoalDepth = 0;
        if (r < StaffTables.ResearchChanceBelow) { State = StateResearching; return; }
        State = StatePatrol;
        Park.RaiseSound(this, 8, StaffTables.SoundResearcherPatrol, StaffTables.HandleResearcherPatrol);
    }

    /// <summary>`0x1B61C0`, READ (MIPS `0x1B61C0..0x1B62FC`).</summary>
    void Research()
    {
        LogicalRequest = StaffTables.LogicalWork;                          // C+0x30 = (.. & ~0x1F) | 0x10
        var mgr = Park.Research;                                           // 0x1B6798
        mgr.Contribute(StaffTables.ResearcherWork[Level]);                 // 0x1B6A38(mgr, [0x366150 + 4L])
        sbyte extra = unchecked((sbyte)(byte)(unchecked((uint)(mgr.Budget - 80)) / 3u));   // addiu -0x50; divu 3; s8
        int t = Tiredness + extra;
        Tiredness = t < 0x65 ? unchecked((sbyte)t) : (sbyte)100;       // slti 0x65; sb
        GoalDepth = 0; State = StateIdle;
        Quanta++;
        Park.RaiseSound(this, 8, StaffTables.SoundResearcherWork, StaffTables.HandleResearcherWork);
    }
}
