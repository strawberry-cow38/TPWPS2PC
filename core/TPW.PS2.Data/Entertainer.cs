namespace TPW.PS2.Data;

/// <summary>⭐⭐ THE ENTERTAINER, vtable `0x35C708`, READ in findings/staff-handymen-entertainers.md §4
/// against the decompile of `0x12DA48..0x12E768` (agent_staff/out/ent.c) and the MIPS.
/// <code>
///   0     find work 0x12DE40: 1-in-16 sound 0xA5; free any effector; tired/strike check; then 0x12DCC8:
///         rand(3) == 0 and a guest within 1 cell (0x12DAE0) → start = now, an EFFECTOR at my cell
///         (radius² 1, flags 2; none if all 20 are taken), depth 0, state 0xC;
///         otherwise depth 0, state 0xD, sound 0xA4
///   0xC   performing 0x12DF40: logical 16 every update; on the 4-tick phase tiredness +2, morale +1;
///         now &gt; start + 600, or nobody within 1 cell → depth 0, state 0, sound 0x87 "TADA", logical 11
///   0x20  Shocked 0x12E320 (pushed over whatever it interrupted): morale -10 EVERY update; past the
///         deadline → the nearest free guard within Manhattan 6 chases the heckler, else morale -5; pop
/// </code>
/// ⭐ The effector is NOT freed when a show ends: the next find work frees it (the tick after), as do a
/// heckle, a release and a fire. Guests watch the effector, not the entertainer
/// (<see cref="ParkStaff"/>'s guest side, `0x20FB88`/`0x2107A0`). Walk speed: no `vt+0x18C` override,
/// the base 15. His level only feeds the guests' watch time `300 + 60·L` and his wage.
/// ⚠ ADAPTERS: "a guest" is a guest with a body on the walk (<see cref="ParkStaff.GuestAdjacent"/>);
/// natively a guest hidden in a ride or shop counts if its stored position is adjacent.</summary>
public sealed class Entertainer : StaffMember
{
    /// <summary>His own states: 0xC "Entertaining", 0x20 "Shocked" (debug table `0x10CA98`).</summary>
    public const byte StatePerforming = 0x0C, StateShocked = 0x20;
    /// <summary>Bank-8 events: 0xA5 the 1-in-16 idle (handle `P+0x60`), 0xA4 no show (`P+0x5C`),
    /// 0x87 "TADA" at a show's end (`P+0x64`).</summary>
    public const int SoundIdle = 0xA5, SoundNoShow = 0xA4, SoundTada = 0x87;
    public const int HandleNoShow = 0x5C, HandleIdle = 0x60, HandleTada = 0x64;
    /// <summary>`0x12DF40`: `start + 600U &lt; now` (unsigned) ends the show.</summary>
    public const uint ShowTicks = 600;
    /// <summary>`0x12DCC8`: `rand(3) == 0`.</summary>
    public const int ShowChance = 3;
    /// <summary>`0x12DCC8`: the effector's radius² 1 (`+0x10`) and flags 2 (`+0x14`).</summary>
    public const uint EffectorRadius2 = 1;
    public const int EffectorFlags = 2;
    /// <summary>`0x12E278`: morale -5 on a heckle, deadline `now + rand(5)·60` (0..240 ticks).</summary>
    public const int HeckleMorale = 5, ShockSteps = 5, ShockStepTicks = 60;
    /// <summary>`0x12E320`: morale -10 every Shocked update; -5 more if no guard is sent.</summary>
    public const int ShockedMorale = 10, NoGuardMorale = 5;
    /// <summary>`0x12E320`: the guard must be within Manhattan 6 (`6 &lt; d` refuses), strict &lt; from 0xFFFF.</summary>
    public const uint GuardReach = 6;
    /// <summary>`0x12DF40`: the 4-tick quantum's tiredness +2 and morale +1 while performing.</summary>
    public const int PerformTiredness = 2, PerformMorale = 1;

    internal Entertainer(ParkStaff park, int poolSlot) : base(park, StaffKind.Entertainer, poolSlot) { }

    /// <summary>`P+0x58`: his effector record while one is registered, else null.</summary>
    public StaffEffector Effector { get; private set; }
    /// <summary>`C+0x20` while Shocked: the heckler. ⚠ NOT cleared when the heckler leaves: the
    /// guests' removal notice `0x14B9D0` goes to guards only, so it dangles (findings §9).</summary>
    public GuestTarget Heckler => Target as GuestTarget;
    /// <summary>Instrumentation, not native fields.</summary>
    public int Shows { get; private set; }
    public int Heckles { get; private set; }
    public int GuardsCalled { get; private set; }

    /// <summary>`0x12E0A8`: `P+0x58..P+0x64` zeroed (the effector pointer WITHOUT a free: a fire has
    /// always freed it by then).</summary>
    protected override void OnActivateJob() => Effector = null;

    /// <summary>`0x12E160`: 0x20 → Shocked, 0 → find work, 0xC → performing, else the base.</summary>
    protected override bool UpdateJobState()
    {
        if (State == StateShocked) { Shocked(); return true; }
        if (State == StatePerforming) { Perform(); return true; }
        return false;
    }

    /// <summary>`vt+0x1A4` = `0x12DE40`, then `0x12DCC8`, READ. ⭐ `rand(3)` is drawn whenever he is
    /// not tired or striking, before the guest test; the state test (`!= 0xC`) cannot fail here.</summary>
    protected override void FindWork()
    {
        if (Park.Random(16) == 0) Park.RaiseEntertainerSound(this, SoundIdle, HandleIdle);
        FreeEffector();
        if (TiredOrStriking()) return;
        if (Park.Random(ShowChance) == 0 && Park.GuestAdjacent(Cell) && State != StatePerforming)
        {
            Stamp = Park.Now;                                          // C+0x24: the show's start
            Effector = Park.Effectors.Allocate();                      // 0x14CFB8, null when 20 are in use
            if (Effector != null)
            {
                Effector.X = Cell.X; Effector.Z = Cell.Z;              // vt+0x74's 8 bytes: my CELL
                Effector.Radius2 = EffectorRadius2; Effector.Flags = EffectorFlags;
            }
            GoalDepth = 0; State = StatePerforming;
            Shows++;
            return;
        }
        GoalDepth = 0; State = StatePatrol;
        Park.RaiseEntertainerSound(this, SoundNoShow, HandleNoShow);
    }

    /// <summary>State 0xC, `0x12DF40` (MIPS `0x12DF78..0x12DFC8`). The adjacency scan is skipped once
    /// the time is up.</summary>
    void Perform()
    {
        uint now = Park.Now;
        LogicalRequest = StaffTables.LogicalWork;                          // 16, every update
        if (OnPhase)
        {
            Tiredness = (sbyte)Math.Min(100, Tiredness + PerformTiredness);
            Morale = (sbyte)Math.Min(100, Morale + PerformMorale);
        }
        if (!(unchecked(Stamp + ShowTicks) < now) && Park.GuestAdjacent(Cell)) return;
        GoalDepth = 0; State = StateIdle;
        Park.RaiseEntertainerSound(this, SoundTada, HandleTada);
        LogicalRequest = StaffTables.LogicalAfterShow;                     // 11
    }

    /// <summary>⭐ `0x12E278(ent, guest)`, the heckle -- its only caller is the guest's idle arm 2
    /// (`0x20CECC`), <see cref="ParkStaff.HeckleArm"/>:
    /// <code>
    ///   if state != 0x20: push(state); state = 0x20
    ///   morale = max(0, morale - 5); heckler = guest
    ///   deadline = now + rand(5) * 60          // overwrites the show's start
    ///   free the effector
    /// </code>
    /// Any entertainer can be heckled, one resting hidden in a staff room or striking included. A heckle
    /// during a show pops back into 0xC WITHOUT an effector (no new watchers) and with `start` = the
    /// Shocked deadline, so the show can run up to 600 more ticks (INFERRED, findings §4.6).</summary>
    internal void Heckled(GuestTarget guest)
    {
        if (State != StateShocked) { PushGoal(); State = StateShocked; }
        Morale = (sbyte)Math.Max(0, Morale - HeckleMorale);
        Target = guest;
        uint now = Park.Now;
        Stamp = unchecked(now + (uint)(Park.Random(ShockSteps) * ShockStepTicks));
        FreeEffector();
        Heckles++;
    }

    /// <summary>State 0x20, `0x12E320` (MIPS `0x12E34C..0x12E368`): morale -10 unconditionally, then
    /// when `deadline &lt; now`: the nearest guard with `!busy` (`0x1416F0`) by Manhattan cells over the
    /// guard list `0x14D228` (newest first, strict &lt;, from 0xFFFF); found and `d &lt;= 6` →
    /// `0x1417D0(guard, heckler)`, else morale -5; then pop.
    /// ⚠ The pop reads the stack at depth-1; natively depth 0 there reads `C+0x17` (not a goal). It
    /// cannot happen through the paths ported here (every change of state away from 0x20 goes through
    /// the events, which leave 0x20 first); the port refuses it loudly rather than read junk.</summary>
    void Shocked()
    {
        uint now = Park.Now;
        Morale = (sbyte)Math.Max(0, Morale - ShockedMorale);
        if (!(Stamp < now)) return;
        Guard best = null; uint bestDistance = 0xFFFF;
        foreach (var member in Park.Active(StaffKind.Guard))
        {
            if (member is not Guard g || g.Busy) continue;
            uint d = (uint)(Math.Abs(g.Cell.X - Cell.X) + Math.Abs(g.Cell.Z - Cell.Z));
            if (d < bestDistance) { bestDistance = d; best = g; }
        }
        if (best == null || bestDistance > GuardReach) Morale = (sbyte)Math.Max(0, Morale - NoGuardMorale);
        else { best.Chase(Heckler); GuardsCalled++; }
        if (GoalDepth == 0) throw new InvalidOperationException("entertainer Shocked with an empty goal stack (native would read C+0x17)");
        GoalDepth--;
        State = Goals[GoalDepth];
    }

    /// <summary>`0x14D038` on `P+0x58`, then zero it.</summary>
    void FreeEffector()
    {
        if (Effector == null) return;
        Park.Effectors.Free(Effector);
        Effector = null;
    }

    /// <summary>`vt+0x194` = `0x12E5B0`: free the effector, then `0x1925A8`. The fire override
    /// `0x12E768` (`0x1DC6F0` then the same free) reaches the same end through
    /// <see cref="ParkStaff.Fire"/>, which releases after dismissing.</summary>
    internal override void Release()
    {
        FreeEffector();
        base.Release();
    }

    // The route-reset release 0x12E590 is 0x1DC780: the base's. The arrival 0x12DC88 is 0x1DB970(C, 0)
    // on both branches: the base's. Events: the base 0x1DB768.
}
