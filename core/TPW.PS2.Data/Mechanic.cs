namespace TPW.PS2.Data;

/// <summary>⭐⭐ THE MECHANIC ("FatMechanic", model 0x1AB), vtable `0x3625C8`, READ in
/// findings/staff-mechanics-guards.md §2-§3 against the decompile `mech.c` (`0x178310..0x179508`) and
/// the MIPS named on each member.
///
/// <code>
///   state 0     find work 0x178DD8: tired/strike; tiredness +6, morale +1; rand(2):
///               0 → nearest broken ride (repair), else nearest upgrade (install);
///               1 → nearest upgrade (install), then THE SAME SEARCH AGAIN dispatched as a repair
///               (dead: it can only find what the first call just failed to take); nothing → 0xD
///   0x38/0x39   free route; route to the work cell vt+0x17C, flags 0x11; mode 6 / 0x14; → 0xB
///   0xB → 3 ↔ 2 walking; each waypoint in mode 6 costs morale -2, tiredness -2 (0x178BE8)
///   arrival     mode 6 → 0x10, mode 0x14 → 0x34, mode 0x16 → target 0, state 0
///   0x10        flag clear: chatter, service(1); flag set: ride → 6, deadline now + T[L], → 0xE
///   0xE         logical 16, repair noise (bank 2 0x6F, handle P+0x60), face the ride;
///               deadline != 0 and deadline &lt; now → 0x11
///   0x34        flag clear: service(2), chatter; flag set: ride → 6, deadline, → 0x36
///   0x36        logical 16, chatter, face; deadline passed → install 0x116268, → 0x11, off the list
///   0x11        pass A (flag set): ride → 7, unassign, chatter, flag cleared
///               pass B: logical 13, → 0x3A, shown, morale +10
///   0x3A        route to the leave cell vt+0xF4 (the queue mouth), mode 0x16, → 0xB
/// </code>
/// `T[L]` = u16 `0x3627C8 + 4L` = 240/180/120/60/60 (<see cref="StaffTables.MechanicWorkTicks"/>).
/// Strikes and tiredness are asked ONLY in state 0, so a mechanic always finishes a job he started.
///
/// ⚠ ADAPTERS, each labelled where it is used: the tick counter <see cref="ParkStaff.Now"/>; the
/// queue mouth (<see cref="ParkStaff.QueueMouth"/>, the ride's `+0xA0` list the port keeps only in
/// the view's path tool); the handle test <see cref="ParkStaff.HandlePlaying"/>; the ride's native
/// rotation (<see cref="ParkRide.NativeRotation"/>); the broken-ride iterator's order within a pool
/// (newest first, INFERRED natively and PlacedDestination.InNativeOrder here).</summary>
public sealed class Mechanic : StaffMember
{
    /// <summary>Modes of his walks (`C+0x2E`, staff-person.md §8): 6 repair, 0x14 install, 0x16 leave.</summary>
    public const byte ModeRepair = 0x06, ModeInstall = 0x14, ModeLeave = 0x16;
    /// <summary>His states (`C+0x2F`), with the debug names `0x10CA98` gives: 0x10 "Closing ride",
    /// 0xE "Repairing", 0x11 "Opening ride"; 0x34/0x36 the install's arrival and work; 0x38/0x39 the
    /// dispatch (repair/install); 0x3A the walk out.</summary>
    public const byte StateRepairing = 0x0E, StateClosingRide = 0x10, StateOpeningRide = 0x11,
                      StateInstallArrival = 0x34, StateInstalling = 0x36,
                      StateGoRepair = 0x38, StateGoInstall = 0x39, StateLeave = 0x3A;
    /// <summary>Sounds: chatter `0x1781B8` plays bank 8 0xA2 (handle `P+0x58`) or 0xA3 (`P+0x5C`) by
    /// `rand &amp; 1`; the repair noise `0x178880` plays bank 2 (`AUDIO/GLOBAL/ride`) 0x6F on `P+0x60`.</summary>
    public const int SoundChatterA = 0xA2, SoundChatterB = 0xA3, SoundRepairNoise = 0x6F;
    public const int HandleChatterA = 0x58, HandleChatterB = 0x5C, HandleRepairNoise = 0x60;

    internal Mechanic(ParkStaff park, int poolSlot) : base(park, StaffKind.Mechanic, poolSlot) { }

    /// <summary>`vt+0x18C` = `0x179308`: `u16 [0x3627CA + 4L]` -- 9/12/14/16/18.</summary>
    public override int Speed => StaffTables.MechanicSpeed[Level];

    /// <summary>The ride he is working on or walking to or from (his `C+0x20`), or null.</summary>
    public ParkRide Job => Target as ParkRide;

    /// <summary>`vt+0x1E4` = `0x179430` (MIPS): the list box offers Fire unless he is repairing (0xE),
    /// closing (0x10) or opening (0x11) a ride, or at an install (0x34, 0x36).</summary>
    public override bool CanBeFired => State is not (StateRepairing or StateClosingRide or StateOpeningRide
                                                     or StateInstallArrival or StateInstalling);

    /// <summary>Instrumentation, not native fields: dispatches taken (repair, install).</summary>
    public int RepairDispatches { get; private set; }
    public int InstallDispatches { get; private set; }

    // --------------------------------------------------------------------------------------------
    // The "available" predicate (§2.5).

    /// <summary>⭐⭐ "AVAILABLE", EXACTLY AS SHIPPED -- inlined three times: Call Mechanic `0x124158`
    /// (MIPS `0x1241B4..0x124208`), the breakdown advisor `0x103658` and the route hand-off `0x178458`:
    /// <code>
    ///   target == 0 &amp;&amp; !(state == 0x1A || state == 0x0F)
    ///               &amp;&amp; !(state in {2,3} &amp;&amp; mode in {5, 0x11}) &amp;&amp; MODE != 0x32
    /// </code>
    /// ⚠ The last test compares the MODE byte (`lbu v1, 0x2E(a1)` in the delay slot at `0x1241E4`,
    /// `bne v1, a3=0x32` at `0x1241FC`) where the resting STATE 0x32 was surely meant; no mode 0x32
    /// exists, so that test never refuses anyone. ⭐ It is also unobservable in play, which corrects the
    /// findings' INFERRED consequence: a resting member's target is his staff room (`0x1DBB80` stores
    /// it, `sw s0, 0x20(s1)` at `0x1DBD64`), so the FIRST test already refuses him -- resting mechanics
    /// are NOT called out.</summary>
    public bool Available => Target == null
        && !(State is StateWalkToStrike or StateStriking)
        && !(State is StateSegmentEnd or StateWalk && Mode is ModeStrike or ModeStaffRoom)
        && Mode != 0x32;

    // --------------------------------------------------------------------------------------------
    // Find work (§2.1-§2.3).

    /// <summary>⭐⭐ `0x178DD8`, find work, READ (MIPS `0x178DD8..0x178EF4`):
    /// <code>
    ///   if (vt+0x1BC(C)) return;                         // tired → 0x31, strike → 0x1A (0x1DBA90)
    ///   tiredness = min(100, +6); morale = min(100, +1); // every attempt that gets this far
    ///   if (rand(2) == 0) { r = 0x153B80; if (r &amp;&amp; dispatch(r, REPAIR)) return;
    ///                       r = 0x153D40; if (r &amp;&amp; dispatch(r, INSTALL)) return; }
    ///   else              { r = 0x153D40; if (r &amp;&amp; dispatch(r, INSTALL)) return;
    ///                       r = 0x153D40; if (r &amp;&amp; dispatch(r, REPAIR)) return; }   // the duplicated branch
    ///   depth = 0; state = 0xD;
    /// </code>
    /// ⭐ The duplicated `0x153D40` (MIPS `0x178E90..0x178ECC`) is kept exactly as shipped and is DEAD:
    /// it is reached only when the first call returned nothing (an unassigned ride always passes the
    /// install dispatch), and nothing between the two calls changes the list. So on a coin flip of 1 a
    /// mechanic ONLY installs, and a broken ride is looked for on half of his idle decisions.
    /// ⭐ No range and no patrol rectangle: both searches are park-wide.</summary>
    protected override void FindWork()
    {
        if (TiredOrStriking()) return;
        Tiredness = (sbyte)Math.Min(100, Tiredness + 6);
        Morale = (sbyte)Math.Min(100, Morale + 1);
        if (Park.Random(2) == 0)
        {
            var broken = NearestBrokenRide();
            if (broken != null && Dispatch(broken, repair: true)) return;
            var upgrade = NearestUpgrade();
            if (upgrade != null && Dispatch(upgrade, repair: false)) return;
        }
        else
        {
            var upgrade = NearestUpgrade();
            if (upgrade != null && Dispatch(upgrade, repair: false)) return;
            var again = NearestUpgrade();                                // 0x153D40 AGAIN (0x178EA8)
            if (again != null && Dispatch(again, repair: true)) return;  // ...dispatched as a REPAIR
        }
        GoalDepth = 0; State = StatePatrol;
    }

    /// <summary>⭐ `0x153B80`, READ (decompile s2.c): over the ride-family iterator `0x1E5B20(it, 1)`
    /// -- ordinary, then track, then coaster, then tour, stopping before shops/sideshows/features
    /// (`0x1E5BA8` with `it[1] != 0`) -- keep the BROKEN ones (`vt+0xC4`) whose assigned mechanic is
    /// nobody or me; score `|dx| + |dz|` from my cell to the ride's ORIGIN cell (`vt+0x74`), best
    /// starting at `0xFFFFFFFF` and replaced only by a strictly smaller score, so the first found wins
    /// a tie. ⚠ Within a family the pool's active list is newest first (INFERRED natively;
    /// <see cref="PlacedDestination.InNativeOrder"/> is the port's same reading).</summary>
    internal ParkRide NearestBrokenRide()
    {
        ParkRide best = null; uint bestScore = uint.MaxValue;
        var me = Cell;
        foreach (var r in PlacedDestination.InNativeOrder(Park.Sim.Rides))
        {
            if (r.ServiceClass == RideServiceClass.None || !r.Broken) continue;
            if (r.AssignedMechanic != null && !ReferenceEquals(r.AssignedMechanic, this)) continue;
            uint score = unchecked((uint)(Math.Abs(me.X - r.Origin.X) + Math.Abs(me.Z - r.Origin.Z)));
            if (score < bestScore) { best = r; bestScore = score; }
        }
        return best;
    }

    /// <summary>⭐ `0x153D40` → `0x1539C0(list 0x3953E8, n, me)`, READ: over the upgrade list in order,
    /// entries with NO assigned mechanic (even me excludes), nearest by the same Manhattan score; the
    /// first such entry is taken unconditionally, a later one only when strictly nearer.</summary>
    internal ParkRide NearestUpgrade()
    {
        ParkRide best = null; uint bestScore = 0;
        var me = Cell;
        foreach (var r in Park.Sim.UpgradeList)
        {
            if (r.AssignedMechanic != null) continue;
            uint score = unchecked((uint)(Math.Abs(me.X - r.Origin.X) + Math.Abs(me.Z - r.Origin.Z)));
            if (best == null || score < bestScore) { best = r; bestScore = score; }
        }
        return best;
    }

    /// <summary>⭐ `0x178CF8(mech, ride, repair)`, READ:
    /// <code>
    ///   a = ride+0x80; if (a &amp;&amp; a != me) return 0;
    ///   if (repair &amp;&amp; (!broken(vt+0xC4) || Life(vt+0x2CC) == 0)) return 0;   // a CONDEMNED ride is never repaired
    ///   ride+0x80 = me; target = ride; depth = 0; state = repair ? 0x38 : 0x39; return 1;
    /// </code>
    /// It does not free the current route (states 0x38/0x39 do). An install has no precondition.</summary>
    public bool Dispatch(ParkRide ride, bool repair)
    {
        ArgumentNullException.ThrowIfNull(ride);
        if (ride.AssignedMechanic != null && !ReferenceEquals(ride.AssignedMechanic, this)) return false;
        if (repair && (!ride.Broken || ride.Life == 0)) return false;
        ride.AssignedMechanic = this;
        Target = ride; GoalDepth = 0;
        State = repair ? StateGoRepair : StateGoInstall;
        if (repair) RepairDispatches++; else InstallDispatches++;
        return true;
    }

    // --------------------------------------------------------------------------------------------
    // The job states (§3.2), dispatcher `0x1791A8`.

    protected override bool UpdateJobState()
    {
        switch (State)
        {
            case StateRepairing: Repairing(); return true;              // 0x178880
            case StateClosingRide: ClosingRide(); return true;          // 0x1785F8
            case StateOpeningRide: OpeningRide(); return true;          // 0x1786D0
            case StateInstallArrival: InstallArrival(); return true;    // 0x178A38
            case StateInstalling: Installing(); return true;            // 0x178B10
            case StateGoRepair: GoToRide(ModeRepair); return true;      // 0x178EF8
            case StateGoInstall: GoToRide(ModeInstall); return true;    // 0x178FE0
            case StateLeave: Leave(); return true;                      // 0x1790C8
            default: return false;
        }
    }

    /// <summary>`0x178EF8` / `0x178FE0`: free the route (`0x192840`, slot -1); request a route to the
    /// ride's WORK CELL (<see cref="ParkSim.WorkCell"/>, `vt+0x17C` = `0x116EC0`: just outside its
    /// entrance door) at the cell centre, flags 0x11; admitted → stamp, mode 6 / 0x14, push, 0xB.
    /// Refused → stay, retried every tick.</summary>
    void GoToRide(byte mode)
    {
        FreeRoute();
        if (Target is not ParkRide ride) { GoalDepth = 0; State = StateIdle; return; }   // ⚠ managed guard
        var cell = ParkSim.WorkCell(ride);
        if (!Request(CellCentre(cell.X, cell.Z), 0x11)) return;
        Stamp = Park.Now; Mode = mode; PushGoal(); State = StateWaitForRoute;
    }

    /// <summary>`0x1790C8`: a route to the ride's LEAVE CELL (`vt+0xF4` = `0x117280`, the last cell of
    /// its queue list -- the mouth), flags 0x11, mode 0x16; admitted → stamp, push, 0xB. It does NOT
    /// free a route first. Arrival (mode 0x16) clears the target and returns him to state 0.</summary>
    void Leave()
    {
        if (Target is not ParkRide ride) { GoalDepth = 0; State = StateIdle; return; }   // ⚠ managed guard
        var cell = Park.LeaveCell(ride);
        if (!Request(CellCentre(cell.X, cell.Z), 0x11)) return;
        Stamp = Park.Now; Mode = ModeLeave; PushGoal(); State = StateWaitForRoute;
    }

    /// <summary>⭐ `0x178BE8` (`vt+0x144`)'s slot-present half: in MODE 6 ONLY (the repair walk) every
    /// waypoint costs morale -2 then tiredness -2, each floored at 0 -- BEFORE the base's phase +1
    /// (`0x1DB970`). An install walk (0x14) and the walk out (0x16) cost nothing extra.</summary>
    protected override void WaypointCost()
    {
        if (Mode == ModeRepair)
        {
            Morale = (sbyte)Math.Max(0, Morale - 2);
            Tiredness = (sbyte)Math.Max(0, Tiredness - 2);
        }
        base.WaypointCost();
    }

    /// <summary>`0x178BE8`'s no-slot half, the ARRIVAL: depth 0, state 0, then by mode -- 0x14 with a
    /// target: deadline 0, state 0x34; 6 with a target: deadline 0, state 0x10; 0x16: target 0 (and
    /// stay in 0: find work next tick); anything else the base `0x1DB970`.</summary>
    protected override void Arrive()
    {
        switch (Mode)
        {
            case ModeInstall:
                GoalDepth = 0; State = StateIdle;
                if (Target == null) return;
                Stamp = 0; State = StateInstallArrival;
                return;
            case ModeRepair:
                GoalDepth = 0; State = StateIdle;
                if (Target == null) return;
                Stamp = 0; State = StateClosingRide;
                return;
            case ModeLeave:
                GoalDepth = 0; State = StateIdle; Target = null;
                return;
            default:
                base.Arrive();
                return;
        }
    }

    /// <summary>⭐ State 0x10 "Closing ride", `0x1785F8`: service flag clear → chatter and
    /// `0x118568(ride, 1)`; set → depth 0, state 0xE, ride → 6 (`vt+0x1F4`), deadline = now + T[L]. A
    /// broken ride's own check sets the flag every update, so it arrives set and 6 comes on the first
    /// 0x10 tick; a working ride takes one tick more.</summary>
    void ClosingRide()
    {
        if (Target is not ParkRide ride) { GoalDepth = 0; State = StateIdle; return; }   // ⚠ managed guard
        if (!ride.ServiceFlag)
        {
            Chatter();
            Park.Sim.Service(ride, 1);
            return;
        }
        GoalDepth = 0; State = StateRepairing;
        Park.Sim.SetRideStatus(ride, 6);
        Stamp = unchecked(Park.Now + (uint)StaffTables.MechanicWorkTicks[Level]);
    }

    /// <summary>⭐ State 0xE "Repairing", `0x178880`, READ: logical 16 every tick; if the noise's handle
    /// `P+0x60` is idle (`0x111CC8`) play bank 2 event 0x6F at him; face the ride (`0x1787D0`:
    /// rotation 0/1/2/3 → 0, π/2, π, 3π/2); when `deadline != 0 &amp;&amp; deadline &lt; now` (MIPS
    /// `0x1789F4..0x178A10`, strict) → depth 0, state 0x11 (and `0x153D08(ride)`, which returns 0).
    /// So he works `T[L] + 1` ticks: the deadline is set in the 0x10 tick, this runs from the next.</summary>
    void Repairing()
    {
        LogicalRequest = StaffTables.LogicalWork;
        uint now = Park.Now;
        if (!Park.IsHandlePlaying(this, HandleRepairNoise)) Park.RaiseSound(this, 2, SoundRepairNoise, HandleRepairNoise);
        if (Target is ParkRide ride) FacingQuarterTurns = ride.NativeRotation;
        if (Stamp != 0 && Stamp < now) { GoalDepth = 0; State = StateOpeningRide; }
    }

    /// <summary>State 0x34, `0x178A38`: flag clear → `0x118568(ride, 2)` then chatter (so an upgrading
    /// ride raises VAR_BREAKSTAT and smokes like a broken one, INFERRED from the shared write); set →
    /// depth 0, state 0x36, ride → 6, deadline = now + T[L].</summary>
    void InstallArrival()
    {
        if (Target is not ParkRide ride) { GoalDepth = 0; State = StateIdle; return; }   // ⚠ managed guard
        if (!ride.ServiceFlag)
        {
            Park.Sim.Service(ride, 2);
            Chatter();
            return;
        }
        GoalDepth = 0; State = StateInstalling;
        Park.Sim.SetRideStatus(ride, 6);
        Stamp = unchecked(Park.Now + (uint)StaffTables.MechanicWorkTicks[Level]);
    }

    /// <summary>State 0x36, `0x178B10`: logical 16, chatter EVERY tick; deadline passed → the install
    /// `0x116268(ride, 0)` (<see cref="ParkSim.InstallUpgrade"/>: tier + 1, the new tier's defaults, the
    /// cost debited now, sound 0xB8/0xE1), depth 0, state 0x11, the ride off the upgrade list
    /// (`0x153D70`); then face the ride.</summary>
    void Installing()
    {
        LogicalRequest = StaffTables.LogicalWork;
        uint now = Park.Now;
        Chatter();
        if (Stamp != 0 && Stamp < now)
        {
            var ride = Target as ParkRide;
            if (ride != null) Park.Sim.InstallUpgrade(ride, silent: false);
            GoalDepth = 0; State = StateOpeningRide;
            if (ride != null) Park.Sim.RemoveUpgrade(ride);
        }
        if (Target is ParkRide r) FacingQuarterTurns = r.NativeRotation;
    }

    /// <summary>⭐ State 0x11 "Opening ride", `0x1786D0`, READ: pass A, flag SET → ride → 7 (the
    /// parent `0x116660`: 2, reliability 100.0, 10 -- LIFE IS NOT RESTORED), unassign (`0x1E1DF8(ride,
    /// 0)`), chatter, flag cleared (`0x118678`: VAR_BREAKSTAT back to 0, which sends a scripted ride to
    /// its "fixed" branch); pass B, the next tick → depth 0, logical 13, state 0x3A, shown, morale +10.</summary>
    void OpeningRide()
    {
        if (Target is ParkRide ride && ride.ServiceFlag)
        {
            Park.Sim.SetRideStatus(ride, 7);
            ride.AssignedMechanic = null;
            Chatter();
            Park.Sim.ClearService(ride);
            return;
        }
        GoalDepth = 0; LogicalRequest = StaffTables.LogicalWalk; State = StateLeave;
        SetShown(true);
        Morale = (sbyte)Math.Min(100, Morale + 10);
    }

    /// <summary>`0x1781B8`, the chatter, READ (MIPS `0x1781B8..0x178300`): on every tick whose count is
    /// NOT a multiple of `[0x2BED10]` = 63 (`divu`, `beqz` skips the multiples), bank 8 0xA2 on handle
    /// `P+0x58` or 0xA3 on `P+0x5C`, chosen by `0x144870() &amp; 1`. ⚠ The draw is taken through the
    /// port's `rand(2)`; that a handle play does not restart a sound still playing is INFERRED.</summary>
    void Chatter()
    {
        if (Park.Now % StaffTables.MechanicChatterPeriod == 0) return;
        if (Park.Random(2) != 0) Park.RaiseSound(this, 8, SoundChatterA, HandleChatterA);
        else Park.RaiseSound(this, 8, SoundChatterB, HandleChatterB);
    }

    // --------------------------------------------------------------------------------------------
    // Events and releases.

    /// <summary>⭐ `vt+0x16C` = `0x178458`, READ: 1 → depth 0, state 3; 3 → ignored; 2 (route failed)
    /// in mode 6 or 0x14 → unassign the ride, then walk the mechanic list FROM MY OWN NEXT POINTER
    /// (`*P`) and hand the job to the first AVAILABLE one (`0x178CF8(it, ride, mode == 6)`, result
    /// ignored) -- ⭐ so only mechanics AFTER me in list order (older hires) can take it; me: depth 0,
    /// state 0, target 0. 2 in mode 0x16 → state 0, target 0, depth 0. Else the base `0x1DB768`.</summary>
    internal override void RouteEvent(int code)
    {
        switch (code)
        {
            case 1: GoalDepth = 0; State = StateWalk; return;
            case 3: return;
            case 2 when Mode is ModeRepair or ModeInstall:
            {
                var ride = Target as ParkRide;
                if (ride != null) ride.AssignedMechanic = null;
                var list = Park.Active(StaffKind.Mechanic);
                for (int i = IndexIn(list) + 1; i < list.Count; i++)
                    if (list[i] is Mechanic next && next.Available)
                    {
                        if (ride != null) next.Dispatch(ride, Mode == ModeRepair);
                        break;
                    }
                GoalDepth = 0; State = StateIdle; Target = null;
                return;
            }
            case 2 when Mode == ModeLeave:
                State = StateIdle; Target = null; GoalDepth = 0;
                return;
            default:
                BaseRouteEvent(code);
                return;
        }
    }

    int IndexIn(IReadOnlyList<StaffMember> list)
    {
        for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], this)) return i;
        return list.Count;
    }

    /// <summary>`vt+0x194` = `0x1794C8` (fire): if he has a target, `0x1E1DF8(target, 0)` and target 0,
    /// then `0x1925A8`. ⭐ The ride keeps whatever status it had: fired in 6, an ordinary ride, a
    /// coaster or a tour stays 6 (their checks only move 2/10 → 4 and the search looks only at 4/5)
    /// until reloaded; a track ride below 10.0 is forced back to 4 and found again (§2.7).</summary>
    internal override void Release()
    {
        if (Target is ParkRide ride) ride.AssignedMechanic = null;
        Target = null;
        base.Release();
    }

    /// <summary>⭐ `0x179328`, the route-system reset, READ: the route is freed; walking (2/3/0xB) in mode
    /// 0x14 or 6 RE-DISPATCHES (`0x178CF8(me, ride, mode == 6)`, and nothing else if that fails), mode
    /// 0x16 → depth 0, state 0x3A; working (0xE/0x10/0x11/0x34/0x36) keeps working; everything else
    /// unassigns its target (`0x1E1DF8(target, 0)` -- whoever it is assigned to) and freezes
    /// (`0x1DC780`), which the reset's end undoes.</summary>
    internal override void OnRouteReset()
    {
        FreeRoute();                           // the pool is already reset: a stale chain is dropped
        switch (State)
        {
            case StateSegmentEnd: case StateWalk: case StateWaitForRoute:
                if (Mode == ModeInstall && Target is ParkRide installing) { Dispatch(installing, repair: false); return; }
                if (Mode == ModeRepair && Target is ParkRide repairing) { Dispatch(repairing, repair: true); return; }
                if (Mode == ModeLeave) { GoalDepth = 0; State = StateLeave; return; }
                break;
            case StateRepairing: case StateClosingRide: case StateOpeningRide:
            case StateInstallArrival: case StateInstalling:
                return;
        }
        if (Target is ParkRide ride) ride.AssignedMechanic = null;
        base.OnRouteReset();
    }
}

/// <summary>⭐ AREA C, the staff side: the park-level operations that read the mechanic list.</summary>
public sealed partial class ParkStaff
{
    /// <summary>⚠ ADAPTER for `vt+0xF4` = `0x117280`, the LAST cell of the ride's `+0xA0` queue list (its
    /// mouth, where the queue meets the path), which the port keeps only in the view's path tool (the
    /// viewer supplies it from its queue shape). Null here, or no answer, falls back to the ride's
    /// <see cref="ParkRide.Entrance"/> (its stub: a one-cell queue's mouth); a ride with neither is the
    /// console's n = 0 edge, which reads cell (0,0) (INFERRED, findings §3.1).</summary>
    public Func<ParkRide, ParkCell?> QueueMouth { get; set; }
    internal ParkCell LeaveCell(ParkRide ride) => QueueMouth?.Invoke(ride) ?? ride.Entrance ?? new ParkCell(0, 0);

    /// <summary>⭐ `0x103658`, the breakdown message the enter-5 handler posts, READ: no mechanics
    /// (`0x14D6B0() == 0`) → 0x37 `BREAKDOWN_NO_MECHANICS`; some mechanic AVAILABLE (the shipped
    /// predicate) → 0x39 `BREAKDOWN_MECHANIC_ON_IT`; else 0x38 `BREAKDOWN_BUSY_MECHANICS`. ⭐ "On his
    /// way" is claimed when an idle mechanic merely EXISTS -- nothing is dispatched by this code.</summary>
    public int BreakdownAdvisorMessage()
    {
        if (Count(StaffKind.Mechanic) == 0) return ParkSim.AdvisorNoMechanics;
        foreach (var m in Active(StaffKind.Mechanic))
            if (m is Mechanic mech && mech.Available) return ParkSim.AdvisorMechanicOnIt;
        return ParkSim.AdvisorBusyMechanics;
    }

    /// <summary>⭐ "Call Mechanic", list-box row 179 → `0x124250` → `0x124158(1)`, READ (MIPS
    /// `0x124158..0x124248`): no ride, or a ride that already has a mechanic → nothing; else the FIRST
    /// AVAILABLE mechanic in list order (newest first; no distance test) is dispatched as a REPAIR and
    /// the loop ends WHATEVER the dispatch returns -- a ride that is not broken, or is condemned,
    /// silently gets nobody and nobody else is tried. Returns whether a dispatch took.</summary>
    public bool CallMechanic(ParkRide ride)
    {
        if (ride == null || ride.AssignedMechanic != null) return false;
        foreach (var m in Active(StaffKind.Mechanic))
            if (m is Mechanic mech && mech.Available) return mech.Dispatch(ride, repair: true);
        return false;
    }

    /// <summary>The outcome of <see cref="RequestUpgrade"/>.</summary>
    public enum UpgradeRequest
    {
        /// <summary>On the list (or already there); `+0x128` = 1.</summary>
        Queued,
        /// <summary>⚠ The list held 15: `0x153950` returned 0 WITHOUT adding, and `+0x128` was still set to 1.</summary>
        ListFull,
        /// <summary>No mechanic hired: `0x1073F0(adv, 0x7C)` (opcode 8's retract -- row 310, a no-op), message
        /// 0xCF submitted, error sound 0xAF. ⚠ CORRECTED (findings/advisor-messages.md §10.1): 0xCF
        /// UPGRADE_NO_MECHANICS has no text row but HAS A VOICE (sound 37, `PS2_18`) -- the player hears it.</summary>
        NoMechanics,
        /// <summary>Mechanics on strike (`0x16C988(cal, 2)`): `0x1073F0(adv, 0x7D)` (a no-op) and message 0x7D
        /// submitted -- silent (row 310, no voice) -- and sound 0xAF.</summary>
        MechanicsOnStrike,
    }

    /// <summary>⭐⭐ THE RIDE SCREEN'S "Apply upgrade" (`0x1D5C00`, a slot of the ride info screen's
    /// vtable), for its item 0, READ (MIPS `0x1D5C00..0x1D5DC0`):
    /// <code>
    ///   if (mechanic count == 0)   { 0x1073F0(0x7C); submit 0xCF (voiced); sound 0xAF; return; }
    ///   if (0x16C988(cal, 2))      { 0x1073F0(0x7D); submit 0x7D (silent); sound 0xAF; return; }   // on strike
    ///   0x124270() = 0x153D10(ride): onto the list (15 max);   ride+0x128 = 1;
    /// </code>
    /// ⭐ NO MONEY, TIER OR LIFE TEST: the new tier's cost is debited when a mechanic FINISHES
    /// (`0x116268`) and may overdraw. ⚠ The error sound 0xAF is the caller's to play (it is a UI sound).
    /// ⚠ The console's only caller is the ride screen, which acts on item 0 only (`bgtz` at `0x1D5CB8`);
    /// whether its build hides the item past the last tier is not traced -- see
    /// <see cref="ParkSim.InstallUpgrade"/> for what the port does there.</summary>
    public UpgradeRequest RequestUpgrade(ParkRide ride)
    {
        ArgumentNullException.ThrowIfNull(ride);
        if (Count(StaffKind.Mechanic) == 0) { Advisor?.Invoke(StaffTables.UpgradeNoMechanicsMessage); return UpgradeRequest.NoMechanics; }
        if (IsStriking(StaffKind.Mechanic)) { Advisor?.Invoke(StaffTables.UpgradeMechanicsStrikingMessage); return UpgradeRequest.MechanicsOnStrike; }
        bool added = Sim.AddUpgrade(ride);
        ride.UpgradePending = true;
        return added ? UpgradeRequest.Queued : UpgradeRequest.ListFull;
    }
}
