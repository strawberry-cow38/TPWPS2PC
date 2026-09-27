using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>What a guard's or an entertainer's target field (`C+0x20`) holds when it names a GUEST:
/// natively the guest's map object (`N+8`), here its visitor id. ⚠ ADAPTER: ids are unique for the
/// life of a park's walk (<see cref="GuestWalk.Spawn"/> counts them up), so the id is the identity;
/// where the guest's body is, and whether it is still in the park, is asked of
/// <see cref="ParkVisitors"/> each time (<see cref="ParkStaff.LocateGuest"/>).</summary>
public sealed record GuestTarget(int Id);

/// <summary>The model a guard carries out: `0x140880` makes a model instance (`0x230A98`) with the
/// caught guest's own model id (`guest vt+0x14 → +4`), plays logical 17 on it with flags 2, and
/// registers it for drawing (`0x17CE10`). ⚠ The port's guests pick their body by id in the view, so
/// the copy carries the guest id and the view resolves the model the same way.
/// ⭐ The copy is its OWN drawn object: it is repositioned only by `0x1409E8`, destroyed only by
/// `0x140990`, the next `0x140880` or the destructor `0x140588`, and firing the guard (release
/// `vt+0x194` = base `0x1925A8`) does NOT touch it -- nor does re-hiring (`0x140600` zeroes
/// `P+0x5C..P+0x68`, not the copy at `P+0x58`). READ, findings/staff-mechanics-guards.md §5.4/§5.6.</summary>
public sealed class CarriedGuest
{
    internal CarriedGuest(int guestId, uint serial) { GuestId = guestId; Serial = serial; }
    /// <summary>The caught guest's visitor id: the view's key to the body it wore.</summary>
    public int GuestId { get; }
    /// <summary>A new number for every `0x140880`, so a view rebuilds on a new copy.</summary>
    public uint Serial { get; }
    /// <summary>`0x1409E8`: the guard's fine (x, 0, z) at the last reposition.</summary>
    public Point Position { get; internal set; }
    /// <summary>⭐ The yaw `0x1409E8` hands the copy is `0 - fmod(facing + 3π, 2π)` (`__subdf3(0, x)`
    /// at `0x140A70`, modulus `[0x35F170]` = 2π, READ in MIPS `0x140A34..0x140A84`), which is
    /// `π − facing` mod 2π -- the SAME yaw the person sync gives the guard himself. ⚠ findings §5.4
    /// reads it as "facing + π" and misses the subtraction. Kept here as the guard's facing, in quarter
    /// turns, at the last reposition.</summary>
    public int FacingQuarterTurns { get; internal set; }
    /// <summary>The logical animation played on the copy: 17 (`vt+0x5C(1.0, copy, 0x11, 0, 0, 2)`),
    /// first s3/0, main s3/1, last s3/2.</summary>
    public const int Logical = 17;
}

/// <summary>⭐⭐ THE GUARD, vtable `0x35F2E0`, READ in findings/staff-mechanics-guards.md §5 against the
/// decompile of `0x140540..0x141CD8` (agent_staff/out/guard.c) and the MIPS. Guards NEVER look for
/// trouble: find work only patrols. They are SENT, by a prank (<see cref="ParkStaff.DispatchGuard"/>,
/// `0x14D3E0`) or by a heckled entertainer (<see cref="Entertainer"/>, `0x12E320`), through
/// <see cref="Chase"/> (`0x1417D0`).
/// <code>
///   0      find work 0x1413E0: 1-in-16 sound 0xA7; speed 15; not tired/striking → sound 0xA6, 0xD (patrol)
///   0x21   chase 0x140E08: target gone or queueing → give up (-5); past the deadline → give up (-5);
///          other cell → mode 8, route to the guest (flags 0x11), stamp = now (THE DEADLINE IS GONE),
///          push, 0xB; same cell → CATCH: +10 morale, +3 tiredness
///   (walk) 0x140B48: mode 8 → queueing/gone → give up (-2); same cell → catch (no bonus); else the copy
///          follows. Arrival 0x140CD8 by mode (below).
///   0x3C   0x141120: copy follows; logical 16 current at phase 1 → 0x27
///   0x27   0x1411C0 "Chucking out": route (0x21) to the staging point, mode 0xE
///   0x2E   waiting at the turnstile; the coordinator's event 9 → 0x2F
///   0x2F   0x141900: a one-slot straight leg from x to (x, staging z), mode 0x10, state 3
///   0x30   0x1419C0: route (1) to the exit point, mode 9 → arrival: 0x3B, copy destroyed, logical 13
///   0x3B   0x141A70: route (1) back to the staging point, mode 0xF → 0x2E again
///   0x37   0x141298: up to 5 requests (0x80) to the park mouth, mode 0x15 → arrival: state 0
/// </code>
/// ⭐ THE CHASE IS ONE ROUTE LEG. `0x1417D0` sets the deadline `now + 3600`; the first re-route stores
/// `now` into the same word (`0x1410E0`), so after that leg the next 0x21 tick finds `deadline &lt; now`
/// and gives up. Reproduced as shipped. ⭐ CAPTURE REMOVES THE GUEST AT ONCE (event 4 → `0x20F588` →
/// `0x14B368`, then the removal notice `0x14B9D0` resets every guard chasing him, the catcher
/// included), and the guard carries a COPY of the guest's model (<see cref="CarriedGuest"/>) out
/// through the gate turnstile the guests use.
///
/// ⚠ ADAPTERS: the gate (<see cref="ParkStaff.Gate"/>), the staging cell
/// (<see cref="ParkStaff.StagingCell"/>) and the animation phase query
/// (<see cref="ParkStaff.AnimationState"/>) are the port's; a guest the port has no body for is
/// treated like a null target (see <see cref="ParkStaff.LocateGuest"/>).</summary>
public sealed class Guard : StaffMember
{
    /// <summary>Modes of his walks (staff-person.md §8): 8 chase, 9 exit, 0xE/0xF the gate legs,
    /// 0x10 the straight crossing, 0x15 back to the park mouth.</summary>
    public const byte ModeChase = 0x08, ModeExit = 0x09, ModeToStaging = 0x0E, ModeBackToStaging = 0x0F,
                      ModeCrossing = 0x10, ModeToMouth = 0x15;
    /// <summary>His own states (names from the debug table `0x10CA98` where it has one): 0x21 "Chase",
    /// 0x27 "Chucking out", 0x2E/0x2F the gate wait and crossing (shared with guests), 0x30 to the exit,
    /// 0x37 back to the park mouth, 0x3B back to the gate, 0x3C carrying off, 0x3D (empty, no writer).</summary>
    public const byte StateChase = 0x21, StateChuckingOut = 0x27, StateAtGate = 0x2E, StateCrossing = 0x2F,
                      StateToExit = 0x30, StateToMouth = 0x37, StateBackToGate = 0x3B, StateCarrying = 0x3C,
                      StateEmpty3D = 0x3D;
    /// <summary>Bank-8 events: 0xA7 the 1-in-16 idle (handle `P+0x60`), 0xA6 off to patrol (`P+0x64`),
    /// 0x88 dispatched (`P+0x68`), 0x89 the copy repositioned (`P+0x5C`, " EVT_GUARD").</summary>
    public const int SoundIdle = 0xA7, SoundPatrol = 0xA6, SoundDispatched = 0x88, SoundCarrying = 0x89;
    /// <summary>The handle offsets those four keep (`P+`), for a view that must not restart a voice.</summary>
    public const int HandleCarrying = 0x5C, HandleIdle = 0x60, HandlePatrol = 0x64, HandleDispatched = 0x68;
    /// <summary>`0x1417D0`: deadline `now + 0xE10`.</summary>
    public const uint ChaseTicks = 0xE10;
    /// <summary>Speed bits: 15 in find work (`| 0x78`), 30 from dispatch (`| 0xF0`) until the next state 0.</summary>
    public const int PatrolSpeed = 15, ChaseSpeed = 30;
    /// <summary>Ledger (§5.7): a catch in state 0x21 +10 morale, +3 tiredness; giving up in 0x21 -5,
    /// mid-walk -2.</summary>
    public const int CatchMorale = 10, CatchTiredness = 3, GiveUpMorale = 5, GiveUpWalkingMorale = 2;
    /// <summary>`0x141298`: the park-mouth request is tried up to 5 times (`iVar5 = 4 .. -1`).</summary>
    public const int MouthTries = 5;
    /// <summary>`C+0x2C` bit 0x20, set by event 2's one exit retry (flags 0x23) and cleared by event 1
    /// (the same bit the wander calls its map-centre flag).</summary>
    const ushort FlagExitRetried = 0x0020;

    internal Guard(ParkStaff park, int poolSlot) : base(park, StaffKind.Guard, poolSlot) { }

    /// <summary>`C+0x20` as the s16 LEG FLAG (`P+0x28`, written with `sh`): 1 after the outbound
    /// staging arrival (mode 0xE), 0 after the inbound one (mode 0xF); the crossing's arrival reads it
    /// (1 → 0x30 out to the exit, 0 → 0x37 back to the mouth) and zeroes the word. ⚠ The SAME field as
    /// the target: the port keeps it apart and clears <see cref="StaffMember.Target"/> whenever it is
    /// written, as the native word then holds 0 or 1, never a pointer.</summary>
    public int Leg { get; private set; }
    /// <summary>`P+0x58`: the copy of a caught guest he carries, or null. See <see cref="CarriedGuest"/>.</summary>
    public CarriedGuest Carried { get; private set; }
    /// <summary>Instrumentation, not native fields.</summary>
    public int Catches { get; private set; }
    public int GiveUps { get; private set; }
    public int Dispatches { get; private set; }

    /// <summary>`0x1416F0`, READ (MIPS `0x1416F0..0x1417C8`): busy when the state is 0x21, 0x3C, 0x3D,
    /// 0x27, 0xF, 0x2E or 0x2F, or when walking (state 2 or 3) with mode 8, 9, 5, 0xE, 0xF, 0x10 or 0x30.
    /// ⭐ NOT busy, hence sendable: resting hidden in a staff room (0x32), setting off to rest (0x31),
    /// waiting for a chase route (0xB), walking to a staff room (mode 0x11) or back into the park
    /// (mode 0x15). Mode 0x30 exists nowhere (an INFERRED slip), kept.</summary>
    public bool Busy
    {
        get
        {
            if (State is StateChase or StateCarrying or StateEmpty3D or StateChuckingOut or StateStriking
                         or StateAtGate or StateCrossing) return true;
            if (State is StateSegmentEnd or StateWalk)
                return Mode is ModeChase or ModeExit or ModeStrike or ModeToStaging or ModeBackToStaging
                               or ModeCrossing or 0x30;
            return false;
        }
    }

    /// <summary>`0x141580`: 0x21/0x27/0x2F/0x30/0x37/0x3B/0x3C/0x3D are his, 0x2E waits; 0 is find
    /// work; the rest the staff base's.</summary>
    protected override bool UpdateJobState()
    {
        switch (State)
        {
            case StateAtGate: return true;                               // waits for event 9
            case StateChase: Chasing(); return true;
            case StateChuckingOut: ChuckOut(); return true;
            case StateCrossing: Cross(); return true;
            case StateToExit: ToExit(); return true;
            case StateToMouth: ToMouth(); return true;
            case StateBackToGate: BackToGate(); return true;
            case StateCarrying: CarryingOff(); return true;
            case StateEmpty3D: return true;                              // 0x141290: jr ra
            default: return false;
        }
    }

    /// <summary>`vt+0x1A4` = `0x1413E0`: ⭐ no search for crime. `(rand & 0xF) == 0` → sound 0xA7; speed
    /// bits := 15; if `vt+0x1BC` (tired/strike) returns 0: sound 0xA6, depth 0, state 0xD.</summary>
    protected override void FindWork()
    {
        if (Park.Random(16) == 0) Park.RaiseGuardSound(this, SoundIdle, HandleIdle);
        SpeedBits = PatrolSpeed;
        if (TiredOrStriking()) return;
        Park.RaiseGuardSound(this, SoundPatrol, HandlePatrol);
        GoalDepth = 0; State = StatePatrol;
    }

    /// <summary>⭐ `0x1417D0(guard, guest)`, the ONE entry to a chase: free the route (`C+0x28 = -1`),
    /// deadline `now + 3600`, target the guest, depth 0, state 0x21, speed bits 30, sound 0x88. The mode
    /// is not written, and a guard resting HIDDEN in a staff room is sent without being shown.</summary>
    internal void Chase(GuestTarget guest)
    {
        FreeRoute();
        Stamp = unchecked(Park.Now + ChaseTicks);
        Target = guest; Leg = 0;
        GoalDepth = 0; State = StateChase;
        SpeedBits = ChaseSpeed;
        Park.RaiseGuardSound(this, SoundDispatched, HandleDispatched);
        Dispatches++;
    }

    /// <summary>State 0x21, `0x140E08` (MIPS `0x140E08..0x141118`):
    /// <code>
    ///   g = target; null (or the leg flag), or queueing (0x12/0x13/0x14)  → give up, morale -5
    ///   now &gt; deadline                                                  → give up, morale -5
    ///   cell(me) != cell(g): mode 8; request(g's fine x/z, 0x11): refused → stay (retry next tick);
    ///                        admitted → deadline := now (0x1410E0), push 0x21, state 0xB
    ///   same cell: logical 16; 0x140880 (the copy); event 4 to g; depth 0; state 0x3C; +10 morale, +3 tiredness
    /// </code>
    /// ⚠ A guest the port holds no body for (on a ride, inside a facility) is taken as the null target.</summary>
    void Chasing()
    {
        uint now = Park.Now;
        var (place, body, fine) = Park.LocateGuest(Target as GuestTarget);
        if (place is ParkStaff.GuestPlace.Gone or ParkStaff.GuestPlace.NoBody or ParkStaff.GuestPlace.Queueing
            || !(now <= Stamp))
        {
            GiveUp(GiveUpMorale);
            return;
        }
        if (Cell != new ParkCell(fine.X >> 8, fine.Z >> 8))
        {
            Mode = ModeChase;
            if (!Request(fine, 0x11)) return;
            Stamp = now;                                                   // ⭐ 0x1410E0: the deadline is overwritten
            PushGoal(); State = StateWaitForRoute;
            return;
        }
        LogicalRequest = StaffTables.LogicalWork;                          // 16
        Catch(body);
        Morale = (sbyte)Math.Min(100, Morale + CatchMorale);
        Tiredness = (sbyte)Math.Min(100, Tiredness + CatchTiredness);
    }

    /// <summary>The shared tail of both catches: `0x140880` (the copy), event 4 to the guest (removed
    /// from the park at once, and the removal notice resets every guard chasing him -- this one
    /// included: target 0, route freed, depth 0, state 0, shown), then depth 0, state 0x3C.</summary>
    void Catch(Guest body)
    {
        int guest = ((GuestTarget)Target).Id;
        AttachCopy(guest);
        Park.CatchGuest(this, guest, body);                                // event {0x35A548, 4}
        GoalDepth = 0; State = StateCarrying;
        Catches++;
    }

    /// <summary>give_up(d): state 0, depth 0, target 0, morale -d (min 0). The route is NOT freed.</summary>
    void GiveUp(int morale)
    {
        GoalDepth = 0; State = StateIdle; Target = null; Leg = 0;
        Morale = (sbyte)Math.Max(0, Morale - morale);
        GiveUps++;
    }

    /// <summary>`0x140880`: destroy any previous copy, make one of the guest's model playing logical 17
    /// (flags 2), register it, position it (`0x1409E8`, which also plays 0x89), then logical 16 on the
    /// guard and `C+0x2C |= 0x200` (the one-shot "push with flags 2", findings §5.4).</summary>
    void AttachCopy(int guestId)
    {
        Carried = new CarriedGuest(guestId, Park.NextCopySerial());
        Reposition();
        LogicalRequest = StaffTables.LogicalWork;
        Flags |= FlagCutRecord;
    }

    /// <summary>`0x1409E8`, a no-op without a copy: the copy at the guard's fine (x, 0, z), turned to
    /// his yaw, and sound 0x89 at the guard on handle `P+0x5C` -- EVERY time, which is every walk tick
    /// while he carries (" EVT_GUARD").</summary>
    void Reposition()
    {
        if (Carried == null) return;
        Carried.Position = Position;
        Carried.FacingQuarterTurns = FacingQuarterTurns;
        Park.RaiseGuardSound(this, SoundCarrying, HandleCarrying);
    }

    /// <summary>`0x140990`: destroy the copy if any, and logical 13 either way.</summary>
    void DropCopy()
    {
        Carried = null;
        LogicalRequest = StaffTables.LogicalWalk;
    }

    /// <summary>`vt+0x14C` = `0x140B48`: the person's walk step `0x191E98`, then in mode 8 the same
    /// target tests as the chase -- null/gone or queueing → give up with morale **-2** (target 0, depth
    /// 0, state 0); the same cell → the catch WITHOUT the bonuses (event 4, depth 0, state 0x3C) -- else,
    /// and in every other mode, the copy follows (`0x1409E8`).</summary>
    protected override void WalkStep()
    {
        base.WalkStep();
        if (Mode != ModeChase) { Reposition(); return; }
        var (place, body, fine) = Park.LocateGuest(Target as GuestTarget);
        if (place is ParkStaff.GuestPlace.Gone or ParkStaff.GuestPlace.NoBody or ParkStaff.GuestPlace.Queueing)
        {
            Morale = (sbyte)Math.Max(0, Morale - GiveUpWalkingMorale);
            Target = null; Leg = 0; GoalDepth = 0; State = StateIdle;
            GiveUps++;
            return;
        }
        if (Cell == new ParkCell(fine.X >> 8, fine.Z >> 8)) { Catch(body); return; }
        Reposition();
    }

    /// <summary>`0x191D78`'s a1 = 1: a guard never re-asserts logical 13 at a waypoint (staff-person.md
    /// §5.3); only `0x140990` sets it.</summary>
    protected override bool ReassertsWalkLogical => false;

    /// <summary>`vt+0x144` = `0x140CD8`: with no slot, by mode --
    /// 8 → depth 0, state 0x21; 9 → target/leg 0, depth 0, state 0x3B, `0x140990`;
    /// 0xE → depth 0, leg 1, state 0x2E, `0x153298` (P++), facing π; 0xF → leg 0, depth 0, state 0x2E,
    /// P++, facing 0; 0x10 → leg 1 ? 0x30 : 0x37, target/leg 0, depth 0, `0x1532B0` (P--, R++);
    /// 0x15 → depth 0, state 0; anything else, and every slot-present pass, the base `0x1DB970(C, 1)`.
    /// Then `0x1409E8` either way.</summary>
    protected override void SegmentEnd()
    {
        if (RouteSlot == -1 && Mode is ModeChase or ModeExit or ModeToStaging or ModeBackToStaging or ModeCrossing or ModeToMouth)
        {
            FreeRoute();
            switch (Mode)
            {
                case ModeChase: GoalDepth = 0; State = StateChase; break;
                case ModeExit:
                    Target = null; Leg = 0; GoalDepth = 0; State = StateBackToGate;
                    DropCopy();
                    break;
                case ModeToStaging:
                    GoalDepth = 0; Target = null; Leg = 1; State = StateAtGate;
                    Park.GateStage(this);
                    FacingQuarterTurns = 2;                                // 0x40490FDB = pi
                    break;
                case ModeBackToStaging:
                    Target = null; Leg = 0; GoalDepth = 0; State = StateAtGate;
                    Park.GateStage(this);
                    FacingQuarterTurns = 0;
                    break;
                case ModeCrossing:
                    State = Leg == 0 ? StateToMouth : StateToExit;
                    Target = null; Leg = 0; GoalDepth = 0;
                    Park.GateCross(this);
                    break;
                case ModeToMouth: GoalDepth = 0; State = StateIdle; break;
            }
        }
        else BaseSegmentEnd();
        Reposition();
    }

    /// <summary>`vt+0x16C` = `0x1406D8`, READ:
    /// <code>
    ///   1: depth 0, state 3, C+0x2C &amp;= ~0x20
    ///   2, mode 9:   0x20 clear → rand(0x14E288() = 1), exit point, mode 9, request(0x23):
    ///                admitted → |= 0x20, depth 0, state 0xB; refused (or 0x20 set) → depth 0, state 0
    ///   2, mode 0x15: depth 0, state 0x37 (the mouth, retried)
    ///   2, mode 8:   depth 0, target 0, state 0, then the base 0x1DB768 (→ 0xD)
    ///   2, other:    the base 0x1DB768
    ///   9: depth 0, state 0x2F
    /// </code>
    /// ⭐ A guard whose exit route fails twice, or whose gate legs fail, returns to patrol WITH THE COPY
    /// (only 0x140990 destroys it) and the staging count he added is never taken back (§5.6).</summary>
    internal override void RouteEvent(int code)
    {
        switch (code)
        {
            case 1:
                GoalDepth = 0; State = StateWalk; Flags &= unchecked((ushort)~FlagExitRetried);
                return;
            case 2 when Mode == ModeExit:
                if ((Flags & FlagExitRetried) == 0)
                {
                    int index = Park.Random(1);                            // rand(0x14E288()), always 0
                    var exit = Park.ExitPoint(index);
                    Mode = ModeExit;
                    if (exit is { } p && Request(p, 0x23))
                    {
                        Flags |= FlagExitRetried; GoalDepth = 0; State = StateWaitForRoute;
                        return;
                    }
                }
                GoalDepth = 0; State = StateIdle;
                return;
            case 2 when Mode == ModeToMouth:
                GoalDepth = 0; State = StateToMouth;
                return;
            case 2 when Mode == ModeChase:
                GoalDepth = 0; Target = null; Leg = 0; State = StateIdle;
                BaseRouteEvent(code);
                return;
            case 2:
                BaseRouteEvent(code);
                return;
            case 9:
                GoalDepth = 0; State = StateCrossing;
                return;
        }
    }

    /// <summary>State 0x3C, `0x141120`: the copy follows; then `0x10EC48` on his own model -- when it
    /// answers (a visual with a live handle) with logical 16 CURRENT at phase 1 (the carry's first
    /// section s4 has played and its main s5 is on), depth 0, state 0x27. ⚠ The query is
    /// <see cref="ParkStaff.AnimationState"/>; with none (no visual), the first section is taken as
    /// played at once.</summary>
    void CarryingOff()
    {
        Reposition();
        var (current, phase) = Park.AnimationState?.Invoke(this) ?? ((byte)StaffTables.LogicalWork, (byte)1);
        if (current == StaffTables.LogicalWork && phase == 1) { GoalDepth = 0; State = StateChuckingOut; }
    }

    /// <summary>State 0x27 "Chucking out", `0x1411C0`: `0x14E288()` (always 1; ⚠ here: whether the
    /// port knows the entrance's staging cell) → else depth 0, state 0. Free the route; the staging
    /// point `0x1532D8` (entry `+2/+3` centre, x + rand(256)); mode 0xE; request (flags 0x21): admitted
    /// → stamp, depth 0, state 0xB; refused → stay and retry. The copy follows either way.</summary>
    void ChuckOut()
    {
        if (!Park.HasGate) { GoalDepth = 0; State = StateIdle; Reposition(); return; }
        FreeRoute();
        var staging = Park.StagingPoint().Value;
        Mode = ModeToStaging;
        if (Request(staging, 0x21)) { Stamp = Park.Now; GoalDepth = 0; State = StateWaitForRoute; }
        Reposition();
    }

    /// <summary>State 0x2F, `0x141900`: free the route; ONE output slot (`0x192768`), none → stay and
    /// retry; else mode 0x10, `0x1532D8(1 - leg)` (its argument is unused, its rand(256) is drawn), the
    /// slot's target (my x, staging z), terminal link (`|= 0x7FF`), depth 0, state 3.</summary>
    void Cross()
    {
        FreeRoute();
        int slot = Park.Routes.Allocate();
        if (slot == -1) return;
        Mode = ModeCrossing;
        var staging = Park.StagingPoint() ?? Position;
        Park.Routes.SetTarget(slot, new Point(Position.X, staging.Z));
        Park.Routes.SetNext(slot, -1);
        AdoptRoute(slot);
        GoalDepth = 0; State = StateWalk;
    }

    /// <summary>State 0x30, `0x1419C0`: rand(1) → the exit point `0x14E290(0)` (entry `+0/+1` centre),
    /// mode 9, request (flags 1): admitted → stamp, depth 0, state 0xB; refused → retry.</summary>
    void ToExit()
    {
        int index = Park.Random(1);
        Mode = ModeExit;
        if (Park.ExitPoint(index) is { } exit && Request(exit, 0x01)) { Stamp = Park.Now; GoalDepth = 0; State = StateWaitForRoute; }
    }

    /// <summary>State 0x3B, `0x141A70`: free the route; the staging point `0x1532D8(0)`; mode 0xF;
    /// request (flags 1): admitted → stamp, depth 0, state 0xB.</summary>
    void BackToGate()
    {
        FreeRoute();
        var staging = Park.StagingPoint();
        Mode = ModeBackToStaging;
        if (staging is { } p && Request(p, 0x01)) { Stamp = Park.Now; GoalDepth = 0; State = StateWaitForRoute; }
    }

    /// <summary>State 0x37, `0x141298`: up to 5 requests (flags 0x80: paths, walkway and mouth only) to
    /// the park mouth `0x153380` (entry `+0x10/+0x11` centre, NO random term), clamped to
    /// `[0, W&lt;&lt;8] x [0, H&lt;&lt;8]`; the tile lookup `0x14E138` it makes is discarded. Admitted → stamp,
    /// mode 0x15, push, state 0xB; five refusals → stay and retry next tick.</summary>
    void ToMouth()
    {
        for (int i = 0; i < MouthTries; i++)
        {
            if (Park.MouthPoint() is not { } p) return;
            if (!Request(p, 0x80)) continue;
            Stamp = Park.Now; Mode = ModeToMouth; PushGoal(); State = StateWaitForRoute;
            return;
        }
    }

    // A guard's release (vt+0x194) and route-reset release (0x1418E0) are the base's: 0x1925A8 and
    // 0x1DC780. Neither touches the copy.
}
