using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>⚠ ADAPTER: one object of the native placed-object list `0x14CD30` (`*(0x3952A0 + 8)`) as
/// the staff and guest code read it -- the toilets, staff rooms, bins and cameras.
///
/// Natively each is a feature (class vtable `0x35DC70`) whose DBA record `+0x2E` bits say what it is:
/// bit 0 toilet (`0x130718`), bit 1 staff room (`0x1308C8`), bit 2 bin (`0x1307E8`), bit 3 camera
/// (`0x130858`) -- READ, findings/staff.md §1.6. The port keeps a placed ride or feature only while
/// it runs a script (<see cref="ParkSim.Rides"/>), and a bin need not have one, so
/// <see cref="ParkStaff.Features"/> is a supplier the view can extend with its scriptless placements.</summary>
/// <param name="Origin">`vt+0x74` = `0x1E1FA8`: the stored ORIGIN cell (not the entry). Every
/// distance the staff and guests measure to a feature is to this cell.</param>
/// <param name="Entry">Origin plus the rotated entry offset `0x1E1760` (DBA `+0xC`): where a
/// handyman or a resting member is routed to, at its centre.</param>
/// <param name="Flags">DBA `+0x2E`.</param>
/// <param name="Status">`+0xA2`, the placed-object status; the rest search skips 0.</param>
/// <param name="Ride">The live facility when it has one (a toilet must: its condition lives there).</param>
/// <param name="Key">The identity a staff member's target holds -- the ride, or whatever the
/// supplier gives a scriptless feature.</param>
public sealed record StaffFeature(ParkCell Origin, ParkCell Entry, byte Flags, byte Status, ParkRide Ride, object Key)
{
    public bool IsToilet => (Flags & 1) != 0;
    /// <summary>⚠ Includes SPACE's Laser Show (key 356), whose DBA record carries bit 1 too: every
    /// staff-room test is that one bit, so natively it IS a staff room (findings/staff-management.md
    /// §8.4). Reproduced deliberately, not filtered.</summary>
    public bool IsStaffRoom => (Flags & 2) != 0;
    public bool IsBin => (Flags & 4) != 0;
    public bool IsCamera => (Flags & 8) != 0;

    /// <summary>The feature a placed <see cref="ParkRide"/> is, or null for a ride that is not one.
    /// Flags are the joined compiled record's `+0x2E`; ⚠ an UNJOINED lavatory (no compiled record)
    /// reads bit 0 from <see cref="ParkRide.ProvidesRelief"/>, the port's existing fallback.
    /// ⚠ Entry: `0x1E1760` -- <see cref="ParkRide.DestinationEntry"/> for a placed compiled record
    /// (or origin + (-1,-1) when its connection is negative), else the validated <see cref="ParkRide.ServiceEntry"/>, else the approach stub, else
    /// the origin. ⚠ Status is <see cref="ParkRide.DestinationState"/>.</summary>
    public static StaffFeature Of(ParkRide ride)
    {
        if (ride == null) return null;
        var record = ride.Definition?.CompiledEntry;
        byte flags = record?.Kind == AssetResourceDatabase.AssetKind.Feature
            ? record.RawFeatureFlags.GetValueOrDefault()
            : (byte)(ride.ProvidesRelief ? 1 : 0);
        if (flags == 0) return null;
        // 0x1E1760: a NEGATIVE +0xC (x or z) takes DAT_00369A30 = (-1, -1, -1) UNROTATED, READ.
        // The Laser Show is the one staff room that has no connection, so its "entry" is the cell
        // diagonally before its origin, whatever that cell is.
        var entry = record?.Kind == AssetResourceDatabase.AssetKind.Feature && ride.PlacementTurns.HasValue
                    && (record.ConnectionA.X < 0 || record.ConnectionA.Z < 0)
            ? ride.Origin.Offset(-1, -1)
            : ride.DestinationEntry ?? ride.ServiceEntry ?? ride.Entrance ?? ride.Origin;
        return new StaffFeature(ride.Origin, entry, flags, ride.DestinationState, ride, ride);
    }
}

/// <summary>⭐⭐ THE PARK'S STAFF: five pools of five, the candidate database, the staff route
/// requests, the litter, and one update a park tick. Steps 1 and 2 of the staff port (findings/
/// staff-person.md, staff-handymen-entertainers.md, staff-management.md §1-§3).
///
/// **For the view** (the next step), everything is here or on <see cref="StaffMember"/>:
/// <list type="bullet">
/// <item><see cref="Candidates"/> for the Hire tabs (<see cref="CanHire"/> is the tab predicate);</item>
/// <item><see cref="Hire"/> → a held member the view carries with <see cref="Carry"/> every frame and
/// drops with <see cref="Drop(StaffMember, Point)"/> (refused → play 0xAF), or cancels with
/// <see cref="CancelHire"/>; <see cref="Fire"/>;</item>
/// <item><see cref="Members"/> (update order) and <see cref="Active"/> (per type) for drawing and
/// the laptop: position, facing, logical request, shown, model id, tiredness, morale, level, wage;</item>
/// <item><see cref="Litter"/> for the litter models; <see cref="Sound"/> and <see cref="Advisor"/>;</item>
/// <item><see cref="AnimationReady"/> for the model's readiness, and <see cref="Features"/> to add
/// scriptless bins and staff rooms.</item>
/// </list>
///
/// **Attach** with <c>visitors.Staff = staff</c>: then <see cref="ParkVisitors.Step"/> runs
/// <see cref="Update"/> once per park tick, guests drop litter and vomit into <see cref="Litter"/>
/// and feel it every 64 ticks, and the toilet stand-in `ParkVisitors.Maintain` stops -- handymen
/// clean instead. Unattached, nothing in the guest code changes.
///
/// Native order within a frame (§2.5): the route pump `0x18D7F8`, then every map object's `vt+0x3C`
/// newest first (`0x14BE60` over `[0x395208]`), then the render bumps the tick counter `[0x397644]`.
/// <see cref="Update"/> is that, for the staff: pump, members newest first, <see cref="Now"/>+1.
///
/// ⚠ ADAPTERS, each labelled where it lives: the tick counter (<see cref="Now"/>); the random
/// stream (<see cref="Random"/>); the tiles (<see cref="Tiles"/>); the search
/// (<see cref="RouteRequests"/>); the placed-object list (<see cref="Features"/>); readiness
/// (<see cref="AnimationReady"/>); the removal notice, detected at the next update rather than sent
/// at removal; the placeholder find-work of the types not built yet (<see cref="StaffMember"/>).
///
/// ⭐ Management (step 5) is ParkStaff.Management.cs: strikes, wages due, training, the Staff Room,
/// the research manager and researchers, the advisor producers and the patrol-area tool; the
/// calendar that drives the monthly and weekly work is <see cref="ParkManagement"/>.
///
/// ⚠ OUT OF SCOPE, said so: save/load (the 16-byte records `0x1DC0E8/0x1DC178`, the double
/// activation on load, the litter counts, the strike calendar `0x16CB40/0x16D030` -- findings/
/// staff-person.md §9); the grab tool (dead on PS2, findings/staff-management.md §5); the load scatter
/// of litter.</summary>
public sealed partial class ParkStaff
{
    readonly Dictionary<StaffKind, StaffMember[]> _slots = new();
    readonly Dictionary<StaffKind, List<StaffMember>> _free = new();     // index 0 = head
    readonly Dictionary<StaffKind, List<StaffMember>> _active = new();   // index 0 = head = newest
    readonly List<StaffMember> _mapList = new();                          // [0x395208], index 0 = newest
    readonly bool[] _striking = new bool[6];                              // by type code 1..5
    StaffCandidateDatabase _candidates;
    ulong _poolEpoch;
    SnapshotRandom _ownedRandom;
    bool _snapshotReady = true;

    /// <param name="visitors">The park's coordinator: its walk's <see cref="GuestWalk.NativeRoutes"/>
    /// is THE output-slot pool the staff route into, shared with every guest.</param>
    /// <param name="clock">The park calendar: hire days, the toilet's day stamp, days employed.</param>
    /// <param name="activations">The shared activation counter `0x1093B0` -- the one the guests'
    /// entrance controller uses, so staff and litter serials interleave with theirs.</param>
    /// <param name="random">`rand(n)` = `0x1448E0`, 0..n-1. ⚠ Natively the one guest stream; the
    /// console's interleaving of draws across guests, planner and staff is not reproducible in any
    /// case, so a caller may pass the guests' stream or its own. Default: a fixed-seed generator.</param>
    public ParkStaff(ParkVisitors visitors, ParkClock clock, NativeActivationSequence activations,
                     Func<int, int> random = null) : this(visitors, clock, activations, random, false) {}

    // Restoring builds stable member shells without changing Walk or consuming litter RNG.
    ParkStaff(ParkVisitors visitors, ParkClock clock, NativeActivationSequence activations,
              Func<int,int> random, bool staged)
    {
        _snapshotReady = !staged;
        Visitors = visitors ?? throw new ArgumentNullException(nameof(visitors));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Activations = activations ?? throw new ArgumentNullException(nameof(activations));
        if (random == null) { _ownedRandom = new SnapshotRandom(0x5747); random = DefaultRandom; }
        Random = random;
        Tiles = new NativeTileView(Visitors.Walk.Paths, DefaultPlacedRides);
        RouteRequests = new StaffRouteService(Visitors.Walk.Paths, Tiles, Routes);
        if (!staged) Litter = new ParkLitter(Random, Activations);
        Features = DefaultFeatures;
        if (!staged) Visitors.Walk.Paused = DefaultPaused;   // a guest watching a show stands still
        _poolEpoch = Routes.ResetGeneration;
        // 0x147EB0: every pool built once, its five slots pushed on the free list AT THE HEAD, so
        // the first allocation gets slot 4.
        foreach (var kind in StaffTables.PoolBuildOrder)
        {
            var slots = new StaffMember[StaffTables.PoolSize];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = kind switch
                {
                    StaffKind.Handyman => new Handyman(this, i),
                    StaffKind.Mechanic => new Mechanic(this, i),
                    StaffKind.Researcher => new Researcher(this, i),         // step 5 (ParkStaff.Management.cs)
                    _ => new StaffMember(this, kind, i),
                };
            _slots[kind] = slots;
            AttachSecurityJobs(kind, slots);                             // ParkStaff.Security.cs: guards, entertainers
            _free[kind] = new List<StaffMember>();
            _active[kind] = new List<StaffMember>();
            for (int i = 0; i < slots.Length; i++) _free[kind].Insert(0, slots[i]);
        }
    }

    public ParkVisitors Visitors { get; }
    public ParkSim Sim => Visitors.Sim;
    public ParkPaths Paths => Visitors.Walk.Paths;
    /// <summary>⭐ The ONE output-slot pool (`0x3AE1B8`), the guests' own instance.</summary>
    public NativeRoutePool Routes => Visitors.Walk.NativeRoutes;
    public ParkClock Clock { get; }
    public NativeActivationSequence Activations { get; }
    public Func<int, int> Random { get; }
    public NativeTileView Tiles { get; private set; }
    public StaffRouteService RouteRequests { get; private set; }
    public ParkLitter Litter { get; private set; }

    /// <summary>⚠ ADAPTER for `[0x397644]`, the frame counter `0x1C4930` reads: the port's own count
    /// of staff updates, bumped at the END of <see cref="Update"/> as the render bumps the native one
    /// after the update. Every phase test, stamp and deadline is in these ticks.</summary>
    public uint Now { get; private set; }

    /// <summary>⚠ ADAPTER for the placed-object list `0x14CD30` (see <see cref="StaffFeature"/>).
    /// Default: every scripted placement in <see cref="ParkSim.Rides"/> that is a feature, in
    /// placement order. ⚠ The native list's order is not traced (it decides toilet and staff-room
    /// ties). A view with scriptless bins or rooms wraps this to add them.</summary>
    public Func<IEnumerable<StaffFeature>> Features { get; set; }
    internal IEnumerable<StaffFeature> PlacedFeatures() => Features?.Invoke() ?? Enumerable.Empty<StaffFeature>();

    /// <summary>⚠ ADAPTER for `0x191E10`'s model query. Null means "no visual object", which the
    /// native test permits unconditionally. A view supplies
    /// <c>m =&gt; NativeLogicalAnimationControl.MovementPermitted(m.LogicalRequest, hasVisual, current)</c>
    /// from the member's model, which gates a walk while logical 13 is requested but not yet current
    /// (the "first-leg" hold after a job, findings/staff-person.md §5.3).</summary>
    public Func<StaffMember, bool> AnimationReady { get; set; }

    /// <summary>A staff sound: (member, bank, event). Bank 8 is `AUDIO/GLOBAL/staf`, played natively
    /// with `0x111428(audio, 8, event, pos, &amp;handle, 0)` at the member's position.</summary>
    public Action<StaffMember, int, int> Sound { get; set; }
    internal void RaiseSound(StaffMember member, int eventId) => Sound?.Invoke(member, 8, eventId);

    /// <summary>⭐ A HANDLE play: (member, native category, event, handle offset `P+`). The mechanic's
    /// chatter (bank 8 0xA2 at `P+0x58`, 0xA3 at `P+0x5C`) and repair noise (bank 2 0x6F at `P+0x60`)
    /// pass their handle to `0x111428`; a view should not start a second voice on a handle that is
    /// still sounding (INFERRED, findings/staff-mechanics-guards.md §3.2). Null falls back to
    /// <see cref="Sound"/>.</summary>
    public Action<StaffMember, int, int, int> HandleSound { get; set; }
    /// <summary>⚠ ADAPTER for `0x111CC8(audio, &amp;handle)`, "is that handle still playing" -- which the
    /// repair noise asks before every restart. Null = never playing, so the noise is raised on every
    /// repairing tick and the view's own handle test decides.</summary>
    public Func<StaffMember, int, bool> HandlePlaying { get; set; }
    internal void RaiseSound(StaffMember member, int bank, int eventId, int handle)
    {
        if (HandleSound != null) HandleSound(member, bank, eventId, handle);
        else Sound?.Invoke(member, bank, eventId);
    }
    internal bool IsHandlePlaying(StaffMember member, int handle) => HandlePlaying?.Invoke(member, handle) ?? false;

    /// <summary>An advisor message id the staff code posts (ADD_MAX from the hire drop). ⚠ The ride
    /// side's breakdown messages go to <see cref="ParkSim.Advisor"/>, with the ride.</summary>
    public Action<int> Advisor { get; set; }

    // --------------------------------------------------------------------------------------------
    // Pools and lists.

    /// <summary>`0x12A550`, a lazy singleton: the 25 candidates are rolled the first time anything asks.</summary>
    public StaffCandidateDatabase Candidates => _candidates ??= new StaffCandidateDatabase(Random);

    /// <summary>The map-object list `[0x395208]` as far as staff go: every hired member, NEWEST
    /// first -- the update order (`0x14DA60` pushes at the head; `0x14BE60` walks from it).</summary>
    public IReadOnlyList<StaffMember> Members => _mapList;

    /// <summary>A type's active list, pool `+8`, NEWEST first (`0x14D640` handymen etc.). This is the
    /// order "the first available mechanic" and similar scans see.</summary>
    public IReadOnlyList<StaffMember> Active(StaffKind kind) => _active[kind];
    /// <summary>A type's free list, head first -- the next hire takes element 0.</summary>
    public IReadOnlyList<StaffMember> Free(StaffKind kind) => _free[kind];
    /// <summary>Pool `+0xC`, the active count (`0x14D6A0` handymen etc.).</summary>
    public int Count(StaffKind kind) => _active[kind].Count;
    /// <summary>`0x14CA20(10, key) &gt; 0` = `5 - hired count`: whether the Hire panel offers the tab.</summary>
    public bool CanHire(StaffKind kind) => Count(kind) < StaffTables.PoolSize;

    // --------------------------------------------------------------------------------------------
    // Hire, place, fire (§2.2-§2.6; findings/staff-management.md §2-§3).

    /// <summary>⭐ The hire tool's ENTER `0x128690`: allocate from the kind's pool (`0x14B0E0` etc.:
    /// free-list head → active-list head, count+1, `C+0x49` = slot, ACTIVATION, map-object head), then
    /// `0x1DC780 → 0x1928B0` freezes it -- held (`C+0x2C |= 0x40`), so it is drawn (logical 18) but
    /// not updated until dropped. ⭐ The hire day, tiredness 0..29 and morale 70..99 are fixed NOW.
    /// ⚠ Managed: returns null when the pool is full or the candidate is taken -- natively there is
    /// no guard (the Hire panel caps a type at 5 and lists only available candidates).</summary>
    public StaffMember Hire(StaffKind kind, int candidateSlot)
    {
        RequireSnapshotReady();
        if ((uint)candidateSlot >= StaffTables.PoolSize) throw new ArgumentOutOfRangeException(nameof(candidateSlot));
        if (!CanHire(kind) || !Candidates.For(kind, candidateSlot).Available) return null;
        var member = _free[kind][0];
        _free[kind].RemoveAt(0);
        _active[kind].Insert(0, member);
        member.Active = true;
        member.Activate(candidateSlot);
        if (!_mapList.Contains(member)) _mapList.Insert(0, member);      // 0x14DA60 (scans first: idempotent)
        member.Freeze();
        _hireHeld = member;                                               // 0x14D6E0's "held by tool 1"
        return member;
    }

    /// <summary>The hire carry `0x128760`, every frame while held: the member stands at the cursor's
    /// world position (1/256 cell, NOT snapped) and stays held.</summary>
    public void Carry(StaffMember member, Point position)
    {
        RequireActive(member);
        member.Carry(position);
    }

    /// <summary>⭐ `0x1E65B8` through the tile adapter: may a carried member be dropped on this cell?
    /// Path and plain ground yes; queues, buildings, the walkway and no-build ground no. See
    /// <see cref="NativeTileView.AcceptsStaffDrop"/> for what the port cannot test.</summary>
    public bool CanDrop(ParkCell cell) { Tiles.Refresh(); return Tiles.AcceptsStaffDrop(cell); }

    /// <summary>⭐ The hire tool's CROSS `0x128918`: the tile at `x&gt;&gt;8, z&gt;&gt;8` must pass
    /// <see cref="CanDrop"/> (refused: the tool plays 0xAF and stays -- false here, the member still
    /// held). Then `0x128888`: unfreeze, state 0, depth 0, logical 13, sound 0x12F, the hire FEE
    /// debit -- which is ALWAYS 0 (tool `+8` has one writer, `sw $zero`, findings/staff-management.md
    /// §2.2) -- and ADD_MAX when this type's count is now 5. The next tick runs find work at the
    /// dropped, unsnapped position.</summary>
    public bool Drop(StaffMember member, Point position)
    {
        RequireActive(member);
        if (!member.Held) throw new InvalidOperationException("only a held (carried) member can be dropped");
        if (!CanDrop(new ParkCell(position.X >> 8, position.Z >> 8))) return false;
        member.Place(position);
        if (ReferenceEquals(_hireHeld, member)) _hireHeld = null;
        if (Count(member.Kind) == StaffTables.PoolSize) Advisor?.Invoke(StaffTables.AddMaxAdvisorMessage(member.Kind));
        return true;
    }

    /// <summary>Drop at a cell's centre. ⚠ The native drop keeps the cursor's fractional position;
    /// use <see cref="Drop(StaffMember, Point)"/> to reproduce that.</summary>
    public bool Drop(StaffMember member, ParkCell cell)
        => Drop(member, new Point(unchecked((short)(cell.X * 256 + 0x80)), unchecked((short)(cell.Z * 256 + 0x80))));

    /// <summary>The hire tool's TRIANGLE `0x128A90`: free the member (`0x14B608`), candidate available
    /// again (`0x12AE68`), sound 0x130. No wage: nobody was employed.</summary>
    public void CancelHire(StaffMember member)
    {
        RequireActive(member);
        if (ReferenceEquals(_hireHeld, member)) _hireHeld = null;
        FreeMember(member);
        member.Candidate.Available = true;
    }

    /// <summary>⭐ List-box Fire `0x124300`: `vt+0x1F4` = `0x1DC6F0` (candidate back UNCHANGED, route
    /// freed, the PRO-RATA WAGE `0x1DC338` paid through `0x100C78(park, wage * 10)` -- the wage
    /// accumulator `park+0x12D0` += it, then the debit), then `0x14B608` (release: a handyman unclaims
    /// his litter; unlink; count-1; active → FREE-LIST HEAD). No confirmation. ⭐ So a long-serving
    /// member fired on day 1 of a month costs a full extra month (findings/staff-management.md §3).
    /// ⚠ The list box offers Fire only when <see cref="StaffMember.CanBeFired"/>; this call does not test it.
    /// ⚠ A pending route request is CANCELLED -- a deliberate difference, see <see cref="StaffRouteService"/>.
    /// ⭐ Then, whoever was fired, `0x124300` asks the MECHANIC list (`0x14D650`) and an empty one clears
    /// the ride upgrade list (`0x1542A0`) -- so firing the last mechanic drops every pending upgrade.</summary>
    public void Fire(StaffMember member)
    {
        RequireActive(member);
        int wage = member.ProRatedWage;
        member.Dismiss();
        Sim.Finances.PayWage(wage * 10);                                  // 0x100C78: += 0x12D0, then debit
        if (ReferenceEquals(_hireHeld, member)) _hireHeld = null;
        FreeMember(member);
        if (Count(StaffKind.Mechanic) == 0) Sim.ClearUpgrades();           // 0x124300 → 0x14D650 → 0x1542A0
    }

    /// <summary>`0x14B608`: `vt+0x194` release, then the pool free (`0x14B8B0` etc.): unlink the map
    /// object, count-1, active → free-list head.</summary>
    void FreeMember(StaffMember member)
    {
        member.Release();
        RouteRequests.Cancel(member);
        _mapList.Remove(member);                                          // 0x14DAC0
        _active[member.Kind].Remove(member);
        _free[member.Kind].Insert(0, member);
        member.Active = false;
    }

    void RequireActive(StaffMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (!member.Active || !ReferenceEquals(_slots[member.Kind][member.PoolSlot], member))
            throw new InvalidOperationException("not a hired member of this park's staff");
    }

    // --------------------------------------------------------------------------------------------
    // Strikes (area D drives the flag; §3.4, findings/staff-management.md §9).

    /// <summary>`0x16C988(calendar, type code)`: the type's strike flag. Default off.</summary>
    public bool IsStriking(StaffKind kind) => _striking[StaffTables.TypeCode(kind)];
    /// <summary>The calendar's per-type strike flag (`cal + 0x309 + (t-1)*8`), which the monthly
    /// ladder `0x16C2A0` sets and clears. ⚠ Area D will drive it; here it is a plain switch.</summary>
    public void SetStriking(StaffKind kind, bool on) => _striking[StaffTables.TypeCode(kind)] = on;

    /// <summary>`0x1497C0`, READ (MIPS `0x1497C0..0x14985C`): with `e` the park's entrance entry,
    /// `x = e.XCol*256 + rand(512)`, `z = e.ZEnd*256 + 0x80` -- a random spot across the walkway's two
    /// columns on the first park row past the mouth. Null without a fitted entrance (⚠ the port only).</summary>
    internal Point? StrikePoint()
    {
        if (Paths.EntranceEntry is not { } e) return null;
        int x = e.XCol * 256 + Random(512);
        int z = e.ZEnd * 256 + 0x80;
        return new Point(unchecked((short)x), unchecked((short)z));
    }

    // --------------------------------------------------------------------------------------------
    // The tick.

    /// <summary>⭐ One park tick of staff: refresh the tile view, catch a route-system reset, send
    /// removal notices, pump ONE route request (the newest), update every member newest first, then
    /// bump <see cref="Now"/>.</summary>
    public void Update()
    {
        RequireSnapshotReady();
        Tiles.Refresh();
        if (Routes.ResetGeneration != _poolEpoch) RouteSystemReset();
        NoticeRemovals();
        SecurityBeforeMembers();                                         // guests' removal notices; the gate
        PollStaffRooms();                                                // ⚠ 0x130510's status change, polled
        RouteRequests.Pump();                                            // 0x18D7F8, before 0x14BE60
        foreach (var member in _mapList.ToArray())                       // ⚠ a snapshot: the native
            if (member.Active) member.Update();                          // next-pointer hazard is not copied
        GuestThink();                                                    // the guests' show-watching (0x20FB88, 0x2107A0)
        Now = unchecked(Now + 1);                                        // 0x1C4A58 bumps [0x397644]
    }

    /// <summary>⚠ The route-system reset `0x18C340`/`0x18C4B8` freezes every person, calls each
    /// type's release, then unfreezes. The port sees it as the shared pool's epoch changing (e.g.
    /// <see cref="GuestWalk.Clear"/>), drops every pending staff request, and runs each member's
    /// <see cref="StaffMember.OnRouteReset"/>.</summary>
    void RouteSystemReset()
    {
        _poolEpoch = Routes.ResetGeneration;
        RouteRequests.Clear();
        foreach (var member in _mapList) member.OnRouteReset();
    }

    /// <summary>⚠ ADAPTER for the removal notice `0x14B9D0 → vt+0x19C`: natively sent when an object
    /// is removed; the port's <see cref="ParkSim.Remove"/> has no event, so a member whose target is a
    /// ride no longer in the sim gets the notice at the next update.</summary>
    void NoticeRemovals()
    {
        HashSet<ParkRide> live = null;
        foreach (var member in _mapList)
        {
            if (member.Target is not ParkRide ride) continue;
            live ??= Sim.Rides.ToHashSet();
            if (!live.Contains(ride)) member.NoticeRemoved(ride);
        }
    }
}
