using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPW.PS2.Data;

/// <summary>
/// A ride's queue as the console lays it out (findings/native-ride-queue.md).
/// <see cref="Entrance"/> is the ride's entrance connection cell (vtable +17C, 116EC0), where the
/// head stands. <see cref="Cells"/> is the ride+A0 list, from the first queue cell outside the ride
/// to the <see cref="Mouth"/>: the last cell (vtable +F4, 117280), where the queue meets the path. A
/// guest arrives at the mouth, and a guest who quits walks back out to it.
/// </summary>
public sealed record NativeQueueShape(ParkCell Entrance, IReadOnlyList<ParkCell> Cells, int Rotation)
{
    public ParkCell Mouth => Cells[^1];
}

/// <summary>117340: where the guest at a given index in the queue stands.</summary>
public static class NativeQueueSpots
{
    /// <summary>One guest's worth of queue: a quarter cell (the sll 6 at 11746C/1174C8).</summary>
    public const int Step = 64;

    public static Point Centre(ParkCell c) => new(checked((short)(c.X * 256 + 128)), checked((short)(c.Z * 256 + 128)));
    public static ParkCell CellOf(Point p) => new(p.X >> 8, p.Z >> 8);

    static int Sign(int v) => v == 0 ? 0 : v > 0 ? 1 : -1;

    /// <summary>1174D4..117528: when the first queue cell IS the entrance cell, the ride's rotation
    /// gives the direction. 0 is -z, 1 is -x, 2 is +z, 3 is +x.</summary>
    static (int X, int Z) Rotated(int rotation) => (rotation & 3) switch
    {
        0 => (0, -Step), 1 => (-Step, 0), 2 => (0, Step), _ => (Step, 0),
    };

    /// <summary>The spot of the guest at <paramref name="index"/> (0 is the head), or null where 117340
    /// returns 0. That happens when the walk runs off the end of the list (117384), or when the spot
    /// would land on the last cell, the mouth (117658). A joining guest gets the spot after the tail,
    /// which is the same walk one step further.
    /// <para>The walk starts at the entrance cell's centre and steps a quarter cell per guest ahead.
    /// It turns only on reaching a queue cell's centre, towards the next cell in the list.</para></summary>
    public static Point? Spot(NativeQueueShape shape, int index)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        var cells = shape.Cells;
        if (cells.Count == 0) return null;
        var pos = Centre(shape.Entrance);
        int at = 0;
        var cell = cells[0];
        int dx = Sign(cell.X - shape.Entrance.X) * Step, dz = Sign(cell.Z - shape.Entrance.Z) * Step;
        if (dx == 0 && dz == 0) (dx, dz) = Rotated(shape.Rotation);
        var last = cells[^1];
        for (int k = 0; k < index; k++)
        {
            if (pos == Centre(cell))
            {
                if (++at == cells.Count) return null;
                var previous = cell;
                cell = cells[at];
                dx = Sign(cell.X - previous.X) * Step;
                dz = Sign(cell.Z - previous.Z) * Step;
            }
            pos = new Point(unchecked((short)(pos.X + dx)), unchecked((short)(pos.Z + dz)));
            if (CellOf(pos) == last) return null;
        }
        return pos;
    }

    /// <summary>The queue's cells from <paramref name="from"/> to <paramref name="to"/>, in walking
    /// order. The line is the entrance followed by the queue cells. When either end is off the line
    /// the walk goes straight between the two.</summary>
    public static IReadOnlyList<ParkCell> Walk(NativeQueueShape shape, ParkCell from, ParkCell to)
    {
        var line = new List<ParkCell>(shape.Cells.Count + 1) { shape.Entrance };
        line.AddRange(shape.Cells);
        int a = line.LastIndexOf(from), b = line.LastIndexOf(to);
        var chain = new List<ParkCell> { from };
        if (a < 0 || b < 0)
        {
            if (to != from) chain.Add(to); // an end off the line: straight there
            return chain;
        }
        for (int i = a; i != b; )
        {
            i += Math.Sign(b - i);
            chain.Add(line[i]);
        }
        return chain;
    }
}

/// <summary>210428, state 0x12: one update of a guest waiting in a ride's queue.</summary>
public static class NativeQueueWaiting
{
    public enum Outcome { Wait, Faced, Leave }

    /// <summary>
    /// <para>Every 4 updates on the guest's phase, a broken ride adds 1 to N+78, capped at 100.</para>
    /// <para>Every 8 updates on its phase, with w = max(50, |taste - value|):</para>
    /// <list type="bullet">
    /// <item>rand(100 - w/2) &lt; 2 adds 1 to N+78, capped at 100;</item>
    /// <item>rand(100) &lt; 10 takes 1 off boredom (N+7B), floored at 0.</item>
    /// </list>
    /// <para>Then, below 81: once the deadline N+2C is strictly before <paramref name="now"/>, the
    /// deadline becomes now + rand(300) and the guest faces rand(4) quarter turns. A rand(10) follows
    /// that does nothing visible. At 81 or more the guest leaves (the caller writes thought 9).</para>
    /// <para>⚠ The draw ranges are the console's. A mismatch over 200 would make the first range
    /// zero or negative (the console traps at zero). Byte tastes and values up to 100 cannot reach
    /// that, so here it simply adds nothing.</para>
    /// </summary>
    public static Outcome Step(ref VisitorWants w, uint now, uint phase, bool broken, int taste, int value,
        Func<int, int> random, ref uint deadline, out int facing)
    {
        ArgumentNullException.ThrowIfNull(random);
        facing = -1;
        int weight = Math.Max(50, Math.Abs(taste - value));
        if (broken && (now & 3) == (phase & 3)) w.Unknown78 = (byte)Math.Min(100, w.Unknown78 + 1);
        if ((now & 7) == (phase & 7))
        {
            int range = 100 - (weight >> 1);
            if (range > 0 && random(range) < 2) w.Unknown78 = (byte)Math.Min(100, w.Unknown78 + 1);
            if (random(100) < 10) w.Boredom = (byte)Math.Max(0, w.Boredom - 1);
        }
        if (w.Unknown78 >= 81) return Outcome.Leave;
        if (!(deadline < now)) return Outcome.Wait;
        deadline = unchecked(now + (uint)random(300));
        facing = random(4); // N+3C = rand(4) * 2 * 2pi/8
        random(10);         // 21058C: a 1-in-10 branch with an empty body
        return Outcome.Faced;
    }
}

/// <summary>
/// The console's ride queues, driving real guests that stand in line (findings/native-ride-queue.md).
/// OPT-IN research controller. From a guest's arrival at the mouth until it boards or walks back out,
/// this controller holds the guest's native route lease. Its leases step automatically; it never
/// steps a guest itself. Call <see cref="Tick"/> once per park update, before the guests step.
/// <para>Per guest, in the console's terms:</para>
/// <list type="bullet">
/// <item>arrive at the mouth: 20D628 case 0, then 20D530;</item>
/// <item>walk in: state 0x29 (20F3F0), then 20D628 case 3;</item>
/// <item>wait: state 0x12 (210428), the impatience clock;</item>
/// <item>move up: state 0x13 (20F2D0) after 3 × a cumulative rand(3) stagger, then case 10;</item>
/// <item>quit: 1180B0 and event 7, then state 0x3A (2112D0), walking back out to the mouth;</item>
/// <item>board: 1166A8 → 1FA368 → 117C90, only for a head standing at its spot, while the ride
///   is operating, VAR_ONRIDE &lt; VAR_CAPACITY and the LETMEON slot is free.</item>
/// </list>
/// <para>Whole-queue events:</para>
/// <list type="bullet">
/// <item>a breakdown empties the queue with event 7, everyone walking out (the state-4 and state-5
///   entries at vtable +21C and +224 call 117798 with 0);</item>
/// <item>demolition empties it with event 10: state 0 where each guest stands (116458 calls
///   117758 with 1);</item>
/// <item>closing (state 3) sends no event. Guests keep waiting, nobody boards, and a guest that has
///   to move up fails 20D530's eligibility and quits.</item>
/// </list>
/// <para>⚠ LABELLED ADAPTERS:</para>
/// <list type="bullet">
/// <item>the ride's upgrade tier (+126) is supplied, and is 0 where the port has no tiers;</item>
/// <item>eligibility (+2DC, placed state 2/10/11) is <see cref="ParkVisitors.Takes"/>;</item>
/// <item>"broken" is supplied from the port's ride state. Ordinary rides keep their queue through
///   state 4 while +2CC (u16 +94) is positive; that exception is not modelled;</item>
/// <item>the phase (N+14) and the walking speed are the caller's;</item>
/// <item>thought 9 is written; effect 0x7E, the sound and the 2E28D0 bookkeeping are not ported;</item>
/// <item>routes inside the queue follow the queue cells. The console plans them with flags 0x10 and
///   0x11 over the same tiles;</item>
/// <item>event 10's guest is released at a cell centre, so it first walks to its own cell's centre;</item>
/// <item>a boarded guest leaves the walk and joins the ride's script queue, where ParkSim offers it
///   through LETMEON;</item>
/// <item>when 117340 fails for a guest already queued (the queue was shortened under it), the guest
///   quits. The console's case 3 and case 10 drop it to state 0 without unlinking it;</item>
/// <item>the guest updates run in queue order, before the rides board. The console's order between
///   the guest list and the ride list was not read.</item>
/// </list>
/// </summary>
public sealed class NativeRideQueues
{
    public enum Step { WalkIn, Waiting, MoveUp, Quit, Release }

    public sealed class Services
    {
        public Func<ParkRide, NativeQueueShape> Shape { get; init; } = _ => null;
        public Func<int, int> Random { get; init; } = _ => 0;
        public Func<Guest, uint> Phase { get; init; } = g => (uint)g.Id;
        public Func<Guest, sbyte> Speed { get; init; } = _ => 22;
        public Func<Guest, bool> AnimationReady { get; init; } = _ => true;
        public Action<Guest> SlotAdvanced { get; init; }
        public Func<ParkRide, bool> Broken { get; init; } = r => r.Get("VAR_BROKEN") != 0 || r.Fault != null;
        public Func<ParkRide, int> Tier { get; init; } = _ => 0;
    }

    public readonly record struct Observation(Guest Guest, ParkRide Ride, int Index, Step Step, Point Position,
        Point? Spot, uint Deadline);

    /// <summary>What the boarding test saw, taken the moment it passed: research instrumentation, the
    /// last 64 only. A snapshot between park ticks cannot show it, because a head that finishes moving
    /// up turns Waiting and boards inside one tick.</summary>
    public readonly record struct Boarding(Guest Guest, Step Step, Point Position, Point? Spot, int OnRide,
        int Capacity, int LetMeOn, int ScriptQueue, uint Tick);
    readonly Queue<Boarding> _boardings = new();
    public IReadOnlyCollection<Boarding> Boardings => _boardings;

    sealed class Member
    {
        internal readonly Guest Guest;
        internal readonly ParkRide Ride;
        internal readonly NativeMotionInputs Inputs;
        internal Step Step;
        internal uint Deadline;   // N+2C: the move-up deadline, and the facing deadline while waiting
        internal bool Routed;     // the current leg has been assigned (an exhausted pool retries)
        internal Member(Guest guest, ParkRide ride, NativeMotionInputs inputs) { Guest = guest; Ride = ride; Inputs = inputs; }
    }

    readonly ParkVisitors _visitors;
    readonly Services _services;
    readonly object _owner = new();
    readonly Dictionary<ParkRide, List<Member>> _queues = new(ReferenceEqualityComparer.Instance);
    readonly List<Member> _members = new();
    uint _now;

    public NativeRideQueues(ParkVisitors visitors, Services services)
    {
        _visitors = visitors ?? throw new ArgumentNullException(nameof(visitors));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>20D530's head count for rides: 7 + 4 × the ride's upgrade tier.</summary>
    public int Capacity(ParkRide ride) => 7 + 4 * _services.Tier(ride);
    public bool Owns(Guest guest) => _members.Any(m => ReferenceEquals(m.Guest, guest));
    public int Joined { get; private set; }
    public int Boarded { get; private set; }
    public int Quits { get; private set; }
    public int Impatient { get; private set; }
    public int Refused { get; private set; }
    public int Released { get; private set; }

    /// <summary>The guests linked in a ride's queue, head first.</summary>
    public IReadOnlyList<Guest> Members(ParkRide ride) =>
        _queues.TryGetValue(ride, out var list) ? list.Select(m => m.Guest).ToArray() : Array.Empty<Guest>();

    public IReadOnlyList<Observation> Observations => _members.Select(m =>
    {
        int index = _queues.TryGetValue(m.Ride, out var list) ? list.IndexOf(m) : -1;
        var shape = _services.Shape(m.Ride);
        var position = _visitors.Walk.NativeRouteState(m.Guest, _owner)?.Position ?? NativeQueueSpots.Centre(m.Guest.Cell);
        return new Observation(m.Guest, m.Ride, index, m.Step, position,
            index >= 0 && shape != null ? NativeQueueSpots.Spot(shape, index) : null, m.Deadline);
    }).ToArray();

    /// <summary>ParkVisitors' mouth seam: where a guest heading for this ride walks to.</summary>
    public ParkCell? Mouth(ParkRide ride) => _services.Shape(ride)?.Mouth;

    /// <summary>ParkVisitors' arrival seam: a guest heading for <paramref name="ride"/> is standing on
    /// its mouth. 20D628 case 0 stamps N+2C and asks 20D530; state 0x29 then asks 20D530 and 117340
    /// again and plans the walk in. True means this controller now holds the guest's lease.</summary>
    public bool Arrive(Guest guest, ParkRide ride)
    {
        if (guest == null || ride == null || Owns(guest) || _services.Shape(ride) is not { } shape) return false;
        var list = Queue(ride);
        if (!_visitors.Takes(ride) || list.Count >= Capacity(ride)
            || NativeQueueSpots.Spot(shape, list.Count) is not { } spot)
        {
            Refused++;
            return false;
        }
        var inputs = new NativeMotionInputs(() => _services.Speed(guest), () => 0x4000,
            () => _services.AnimationReady(guest)) { SlotAdvanced = () => _services.SlotAdvanced?.Invoke(guest) };
        var route = NativeRouteOutput.FromCells(NativeQueueSpots.Walk(shape, guest.Cell, NativeQueueSpots.CellOf(spot)), spot);
        var result = _visitors.AssignQueueRoute(guest, ride, _owner, route, inputs);
        if (result == GuestWalk.NativeAssignment.Refused) { Refused++; return false; }
        var m = new Member(guest, ride, inputs)
        {
            Step = Step.WalkIn, Deadline = _now, Routed = result == GuestWalk.NativeAssignment.Assigned,
        };
        list.Add(m);
        _members.Add(m);
        Joined++;
        return true;
    }

    List<Member> Queue(ParkRide ride)
    {
        if (!_queues.TryGetValue(ride, out var list)) _queues[ride] = list = new List<Member>();
        return list;
    }

    /// <summary>One queue update at console time <paramref name="now"/> (1C4930), before the guests step.</summary>
    public void Tick(uint now)
    {
        _now = now;
        foreach (var (ride, list) in _queues.ToArray())
        {
            if (!_visitors.Sim.Rides.Contains(ride) || _services.Shape(ride) == null)
                foreach (var m in list.ToArray()) Unlink(m, ripple: false, Step.Release);  // event 10
            else if (_services.Broken(ride))
                foreach (var m in list.ToArray()) Unlink(m, ripple: false, Step.Quit);     // event 7
        }
        foreach (var m in _members.ToArray()) if (_members.Contains(m)) Update(m);
        foreach (var (ride, list) in _queues.ToArray())
        {
            Board(ride, list);
            if (list.Count == 0) _queues.Remove(ride);
        }
    }

    int IndexOf(Member m) => _queues.TryGetValue(m.Ride, out var list) ? list.IndexOf(m) : -1;

    void Update(Member m)
    {
        if (!_visitors.Walk.IsLive(m.Guest) || _visitors.Walk.NativeRouteState(m.Guest, _owner) is not { } motion)
        {
            Forget(m, ripple: true); // the guest vanished (a teardown); the queue closes up behind it
            return;
        }
        var shape = _services.Shape(m.Ride);
        int index = IndexOf(m);
        switch (m.Step)
        {
            case Step.WalkIn:
                if (!m.Routed)
                {
                    // 20F3F0: 20D530 (already queued, so eligibility only) and 117340, then the route.
                    if (!_visitors.Takes(m.Ride) || NativeQueueSpots.Spot(shape, index) is not { } target) { Quit(m); return; }
                    var route = NativeRouteOutput.FromCells(
                        NativeQueueSpots.Walk(shape, NativeQueueSpots.CellOf(motion.Position), NativeQueueSpots.CellOf(target)), target);
                    m.Routed = _visitors.AssignQueueRoute(m.Guest, m.Ride, _owner, route, m.Inputs)
                        == GuestWalk.NativeAssignment.Assigned;
                    return;
                }
                if (!motion.Finished) return;
                m.Routed = false;
                // Case 3: at the spot, wait (0x12). Otherwise move up (0x13). N+2C is left as it was.
                if (NativeQueueSpots.Spot(shape, index) is not { } walkedTo) { Quit(m); return; }
                m.Step = motion.Position == walkedTo ? Step.Waiting : Step.MoveUp;
                return;
            case Step.MoveUp:
                if (m.Routed)
                {
                    if (!motion.Finished) return;
                    m.Routed = false;
                    // Case 10: at the spot, wait (0x12). Otherwise plan the walk again (0x29).
                    if (NativeQueueSpots.Spot(shape, index) is not { } movedTo) { Quit(m); return; }
                    m.Step = motion.Position == movedTo ? Step.Waiting : Step.WalkIn;
                    return;
                }
                // 20F2D0: strictly after the deadline, 20D530 and 117340 again, then a DIRECT one-slot walk.
                if (!(m.Deadline < _now)) return;
                if (!_visitors.Takes(m.Ride) || NativeQueueSpots.Spot(shape, index) is not { } next) { Quit(m); return; }
                m.Routed = _visitors.Walk.AssignDirectNativeRoute(m.Guest, _owner, () => next, m.Inputs)
                    == GuestWalk.NativeAssignment.Assigned;
                return;
            case Step.Waiting:
                Wait(m);
                return;
            case Step.Quit:
                if (!m.Routed)
                {
                    // 2112D0: back along the queue to the mouth (+F4).
                    var cells = shape == null
                        ? new[] { NativeQueueSpots.CellOf(motion.Position) }
                        : NativeQueueSpots.Walk(shape, NativeQueueSpots.CellOf(motion.Position), shape.Mouth);
                    var route = NativeRouteOutput.FromCells(cells, NativeQueueSpots.Centre(cells[^1]));
                    m.Routed = _visitors.Walk.AssignNativeRoute(m.Guest, _owner, route, m.Inputs)
                        == GuestWalk.NativeAssignment.Assigned;
                    return;
                }
                if (!motion.Finished) return;
                // Mode 0x16 completes: state 0, no target.
                if (_visitors.ReleaseFromQueue(m.Guest, _owner)) { Forget(m, ripple: false); Quits++; }
                return;
            case Step.Release:
                var centre = NativeQueueSpots.Centre(NativeQueueSpots.CellOf(motion.Position));
                if (motion.Position != centre || m.Routed && !motion.Finished)
                {
                    if (!m.Routed)
                        m.Routed = _visitors.Walk.AssignDirectNativeRoute(m.Guest, _owner, () => centre, m.Inputs)
                            == GuestWalk.NativeAssignment.Assigned;
                    return;
                }
                if (_visitors.ReleaseFromQueue(m.Guest, _owner)) { Forget(m, ripple: false); Released++; }
                return;
        }
    }

    void Wait(Member m)
    {
        var needs = _visitors.Needs;
        if (needs == null || !needs.Has(m.Guest.Id)) return;
        var w = needs.Of(m.Guest.Id);
        var outcome = NativeQueueWaiting.Step(ref w, _now, _services.Phase(m.Guest), _services.Broken(m.Ride),
            w.PreferredIntensity, m.Ride.Value ?? 0, _services.Random, ref m.Deadline, out int facing);
        if (outcome == NativeQueueWaiting.Outcome.Leave)
        {
            w.Thought = Thought.BadQueue; // N+40 = 9
            needs.Set(m.Guest.Id, w);
            Impatient++;
            Quit(m);
            return;
        }
        needs.Set(m.Guest.Id, w);
        if (outcome == NativeQueueWaiting.Outcome.Faced) _visitors.Walk.SetNativeFacing(m.Guest, _owner, facing);
    }

    /// <summary>1166A8's boarding test for the head, then 117C90.</summary>
    void Board(ParkRide ride, List<Member> list)
    {
        if (list.Count == 0 || list[0].Step != Step.Waiting || !_visitors.Takes(ride)) return;
        if (ride.Get("VAR_LETMEON") != 0 || ride.Queue.Count != 0) return;
        if (ride.Get("VAR_ONRIDE") >= ride.Get("VAR_CAPACITY")) return; // 1FA528: var 5 >= var 2 is full
        var head = list[0];
        var seen = new Boarding(head.Guest, head.Step,
            _visitors.Walk.NativeRouteState(head.Guest, _owner)?.Position ?? default,
            _services.Shape(ride) is { } shape ? NativeQueueSpots.Spot(shape, 0) : null,
            ride.Get("VAR_ONRIDE"), ride.Get("VAR_CAPACITY"), ride.Get("VAR_LETMEON"), ride.Queue.Count, _now);
        if (!_visitors.BoardFromQueue(head.Guest, ride, _owner)) return;
        _boardings.Enqueue(seen);
        while (_boardings.Count > 64) _boardings.Dequeue();
        Boarded++;
        Forget(head, ripple: true);
    }

    /// <summary>1180B0 and event 7: out of the list, the rest close up, and the guest walks out.</summary>
    void Quit(Member m) => Unlink(m, ripple: true, Step.Quit);

    void Unlink(Member m, bool ripple, Step then)
    {
        if (_queues.TryGetValue(m.Ride, out var list))
        {
            int at = list.IndexOf(m);
            if (at >= 0)
            {
                list.RemoveAt(at);
                if (ripple) Ripple(list, at);
            }
        }
        m.Step = then;
        m.Routed = false;
    }

    /// <summary>1180B0 and 117C90 send each guest behind a move-up event carrying a stagger that grows
    /// by rand(3) per guest, the first getting 0. 20F588 applies it only to a waiting guest:
    /// deadline = now + 3 × stagger, state 0x13.</summary>
    void Ripple(List<Member> list, int from)
    {
        int stagger = 0;
        for (int i = from; i < list.Count; i++)
        {
            var m = list[i];
            if (m.Step == Step.Waiting)
            {
                m.Step = Step.MoveUp;
                m.Routed = false;
                m.Deadline = unchecked(_now + (uint)(3 * stagger));
            }
            stagger += _services.Random(3);
        }
    }

    void Forget(Member m, bool ripple)
    {
        if (_queues.TryGetValue(m.Ride, out var list))
        {
            int at = list.IndexOf(m);
            if (at >= 0)
            {
                list.RemoveAt(at);
                if (ripple) Ripple(list, at);
            }
        }
        _members.Remove(m);
    }

    /// <summary>Teardown: hand every guest this controller holds to <paramref name="resolve"/> with the
    /// lease owner (a map reset discards them) and forget them.</summary>
    public void Clear(Action<Guest, object> resolve)
    {
        foreach (var m in _members.ToArray()) resolve?.Invoke(m.Guest, _owner);
        _members.Clear();
        _queues.Clear();
    }
}
