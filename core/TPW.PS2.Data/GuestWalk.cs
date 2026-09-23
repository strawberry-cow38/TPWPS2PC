using System.Numerics;

namespace TPW.PS2.Data;

/// <summary>Where a guest is in its walk. ⚠ TWO WAYS OF NOT GETTING THERE, kept apart because a
/// census that merges them hides which one happened: <see cref="NoRoute"/> never had a way (the
/// destination is off the paths, or on paths that do not join these); <see cref="Stranded"/> had
/// one and lost it under its feet.</summary>
public enum GuestState { Walking, Arrived, NoRoute, Stranded }

/// <summary>One guest on the park's public ground, going somewhere.
///
/// ⭐ A CELL, THE NEXT CELL, AND HOW FAR BETWEEN. <see cref="Cell"/> is where the guest is,
/// <see cref="Next"/> the cell it is stepping into (null while it stands), and
/// <see cref="Progress"/> how far along that edge in integer units of
/// <see cref="GuestWalk.UnitsPerCell"/>. No floats in the state, so two runs are the same run;
/// the view turns it into a position through <see cref="Fraction"/> or <see cref="Position"/>,
/// which is the split <see cref="ParkSim"/> makes for rides -- console-rate logic, interpolated
/// drawing.</summary>
public sealed class Guest
{
    public int Id { get; init; }
    public ParkCell Cell { get; internal set; }
    public ParkCell? Next { get; internal set; }
    public int Progress { get; internal set; }
    public ParkCell Destination { get; internal set; }
    public GuestState State { get; internal set; }

    /// <summary>The route being walked, from the cell the guest stood in when it was found to
    /// <see cref="Destination"/>, both included; <see cref="RouteIndex"/> is where
    /// <see cref="Cell"/> sits in it. Null for a guest that has none.</summary>
    public IReadOnlyList<ParkCell> Route { get; internal set; }
    public int RouteIndex { get; internal set; }

    /// <summary>Whole cells stepped so far, and how many times the way ahead vanished and a new
    /// route was found instead. Both are for a census; the guest does not steer by them.</summary>
    public int Steps { get; internal set; }
    public int Reroutes { get; internal set; }
    /// <summary>Why a <see cref="GuestState.NoRoute"/> or <see cref="GuestState.Stranded"/>
    /// guest stopped, in words, so a report can say it rather than only count it.</summary>
    public string Reason { get; internal set; }

    /// <summary>0..1 along the edge from <see cref="Cell"/> to <see cref="Next"/>; 0 standing.</summary>
    public float Fraction => Progress / (float)GuestWalk.UnitsPerCell;
    /// <summary>In cell space -- x across, z down the grid, y zero -- the frame of
    /// <see cref="ParkPaths.Centre"/>. ⚠ NOT world space: the scene mirrors Z (grid +z is world
    /// -Z), and that conversion belongs to the view, as it does for everything else on the grid.</summary>
    public Vector3 Position => Next is ParkCell next
        ? Vector3.Lerp(ParkPaths.Centre(Cell), ParkPaths.Centre(next), Fraction)
        : ParkPaths.Centre(Cell);
}

/// <summary>Guests walking the park's paths: in at the gate, along the public ground, to
/// somewhere. The engine-free layer under whatever draws them.
///
/// ⚠⚠ THE ROUTING IS OURS, NOT THE CONSOLE'S. The PS2's guest pathfinder has not been read out
/// of SLES_500.32 -- not how it searches, not what a turn costs, not how fast anybody walks, not
/// what a guest does when the path is dug out from under it. What IS the game's here is the
/// GROUND: <see cref="ParkPaths.Open"/> is the kind-2 path tile plus the 0x0C/0x0E entrance
/// walkway, both read from the executable (findings/paths.md), so a guest stands only where the
/// console would let one stand. On that ground this class runs a plain breadth-first search over
/// the four cardinal neighbours and walks the result at one cell a second. Every one of those
/// choices is a placeholder for a thing still to be read, and nothing that comes out of it
/// should be quoted as the game's behaviour.
///
/// ⭐⭐ ENGINE-FREE, DELIBERATELY, like <see cref="ParkSim"/>: no Godot, no file, no clock. The
/// caller hands in ticks and the walk is a pure function of them, which is why
/// `tools/TPW.PS2.GuestAudit` can census a park's worth of walking as a console app.
///
/// ⭐ EXACT INTEGER MOTION. A tick moves a guest <see cref="UnitsPerTick"/> units along its
/// edge; <see cref="UnitsPerCell"/> of them is a whole cell, and anything left over carries into
/// the next edge within the same tick. The same spawns give the same walk at any frame rate, and
/// the view interpolates off <see cref="Guest.Fraction"/>. The pace -- 1000 units a cell, 40 a
/// tick, 25 ticks a cell, ONE CELL A SECOND -- is the pace <see cref="VisitorSimulation"/>
/// already walks Ada at (100 units per 100 ms), kept so the two managed sims agree with each
/// other. ⚠ It is not a measurement of the console.
///
/// ⚠ A ROUTE CAN GO BAD UNDER A GUEST: a path is one byte in the grid and the tool can un-lay it
/// at any time. So the route is trusted no further than the next cell: each time a guest is
/// about to commit to an edge it looks, and if the cell ahead is no longer open it searches again
/// from where it stands. No way through means <see cref="GuestState.Stranded"/> -- standing
/// where it was, for good -- and never an exception: one guest with nowhere to go must not stop
/// the park. What the console does with such a guest is another thing to read.
///
/// ⚠ NO CROWDING. Guests pass through each other; there are none of the cell reservations
/// <see cref="VisitorSimulation"/> keeps. Answering that before the console's rule has been read
/// would be one more placeholder.</summary>
public sealed class GuestWalk
{
    /// <summary>The rides' tick, so one loop drives both.</summary>
    public const long TickMilliseconds = ParkSim.TickMilliseconds;
    public const int UnitsPerCell = 1000;
    public const int UnitsPerTick = 40;

    public ParkPaths Paths { get; }
    public long Time { get; private set; }
    long _carry;
    int _lastId;

    readonly List<Guest> _guests = new();
    public IReadOnlyList<Guest> Guests => _guests;

    public GuestWalk(ParkPaths paths) { Paths = paths ?? throw new ArgumentNullException(nameof(paths)); }

    /// <summary>Put a guest down at <paramref name="at"/> -- open ground, or it is the caller's
    /// mistake and throws -- bound for <paramref name="to"/>. The route is found now: a guest with
    /// no way there is returned in <see cref="GuestState.NoRoute"/> rather than dropped, so a
    /// census counts it and a view can show it standing at the gate looking lost.</summary>
    public Guest Spawn(ParkCell at, ParkCell to)
    {
        // ⚠ WALKABLE, not open: a guest handed back by a ride is put down on that ride's own
        // stub, and a queue tile is ground a guest may stand on even though nobody may walk
        // through it. Leaving it is the start exemption in Route.
        if (!Paths.Walkable(at)) throw new ArgumentException($"A guest cannot be put down at {at}: not walkable ground");
        var guest = new Guest { Id = ++_lastId, Cell = at, Destination = to, State = GuestState.Walking };
        _guests.Add(guest);
        if (!Assign(guest)) { guest.State = GuestState.NoRoute; guest.Reason = $"no route from {at} to {to}"; }
        else if (at == to) guest.State = GuestState.Arrived;
        return guest;
    }

    /// <summary>⭐⭐ PUT THE SAME PERSON BACK. A guest handed to a ride LEAVES this layer
    /// entirely -- the script owns them while they are aboard -- and comes back when the script
    /// is done with them. Spawning them again would give them a NEW id, so the person who queued
    /// and the person who walked away would be different people as far as anything counting them
    /// is concerned: money, needs, which rides they have been on, even which of the eight kid
    /// models they were wearing.
    ///
    /// ⚠ The id is not checked against the living; a caller readmitting somebody who never left
    /// gets two of them, which is the caller's mistake and looks like one.</summary>
    public Guest Readmit(int id, ParkCell at, ParkCell to)
    {
        if (!Paths.Walkable(at)) throw new ArgumentException($"A guest cannot be put down at {at}: not walkable ground");
        var guest = new Guest { Id = id, Cell = at, Destination = to, State = GuestState.Walking };
        _guests.Add(guest);
        if (!Assign(guest)) { guest.State = GuestState.NoRoute; guest.Reason = $"no route from {at} to {to}"; }
        else if (at == to) guest.State = GuestState.Arrived;
        return guest;
    }

    public void Clear() { _guests.Clear(); Time = 0; _carry = 0; _lastId = 0; }
    public void Remove(int id) => _guests.RemoveAll(g => g.Id == id);

    /// <summary>Give a guest somewhere new to go, from where it stands. False when it cannot get
    /// there -- the guest is left in <see cref="GuestState.NoRoute"/> with the reason, exactly
    /// as a spawn would leave it -- or when it is mid-edge, because a guest changes its mind on a
    /// cell, not between two. ⭐ The start is exempt from being open, so a stranded guest can be
    /// sent off the cell it is stranded on.</summary>
    public bool Send(Guest g, ParkCell to)
    {
        if (g.Next != null) return false;
        g.Destination = to; g.Progress = 0; g.Reason = null;
        if (g.Cell == to) { g.State = GuestState.Arrived; return true; }
        if (!Assign(g)) { g.State = GuestState.NoRoute; g.Reason = $"no route from {g.Cell} to {to}"; return false; }
        g.State = GuestState.Walking;
        return true;
    }

    /// <summary>The shortest walk over open ground from one cell to another, both included; null
    /// when there is none. Breadth-first over <see cref="ParkPaths.Neighbours"/>: every step
    /// costs the same, so the first arrival is a shortest route, and with a fixed neighbour order
    /// it is the SAME shortest route every time.
    ///
    /// ⭐ THE START IS EXEMPT from being open. <see cref="ParkPaths.Route"/> refuses a start it
    /// would not let anyone stand on, which is right for a search that begins at a portal; this
    /// one begins wherever a guest already IS, and a guest standing on a cell that was path a
    /// moment ago has to be allowed to step off it.
    ///
    /// ⭐ AND THE END MAY BE A QUEUE TILE. A ride's queue stub is where a guest goes to join it,
    /// and it is a queue tile -- walkable, not open -- so the destination is allowed to be any
    /// walkable cell while every cell BETWEEN stays open ground: a guest may walk INTO a queue,
    /// never THROUGH one (the rule VisitorSimulation already walks Ada by). Those two exemptions
    /// are the only differences from ParkPaths' search -- the neighbour order and the open test
    /// are its own.</summary>
    public IReadOnlyList<ParkCell> Route(ParkCell from, ParkCell to)
    {
        if (!Paths.Contains(from) || !Paths.Walkable(to)) return null;
        var previous = new Dictionary<ParkCell, ParkCell> { [from] = from };
        var pending = new Queue<ParkCell>(); pending.Enqueue(from);
        while (pending.TryDequeue(out var c))
        {
            if (c == to)
            {
                var result = new List<ParkCell> { c };
                while (c != from) { c = previous[c]; result.Add(c); }
                result.Reverse(); return result.AsReadOnly();
            }
            foreach (var next in ParkPaths.Neighbours(c))
                if ((Paths.Open(next) || next == to) && previous.TryAdd(next, c)) pending.Enqueue(next);
        }
        return null;
    }

    /// <summary>Advance by a real delta, in whole ticks, keeping the remainder -- the same carry
    /// and the same ceiling as <see cref="ParkSim.Advance"/>, so a frame that drives both keeps
    /// them in step.</summary>
    public int Advance(double deltaSeconds)
    {
        _carry += (long)Math.Round(deltaSeconds * 1000.0);
        int ticks = 0;
        while (_carry >= TickMilliseconds && ticks < 8)
        {
            _carry -= TickMilliseconds;
            Step();
            ticks++;
        }
        if (_carry > TickMilliseconds * 8) _carry = 0;
        return ticks;
    }

    /// <summary>One tick for everybody, in spawn order.</summary>
    public void Step()
    {
        Time += TickMilliseconds;
        foreach (var g in _guests)
        {
            if (g.State != GuestState.Walking) continue;
            int budget = UnitsPerTick;
            while (budget > 0 && g.State == GuestState.Walking)
            {
                if (g.Next == null && !Commit(g)) break;
                int take = Math.Min(budget, UnitsPerCell - g.Progress);
                g.Progress += take; budget -= take;
                if (g.Progress < UnitsPerCell) continue;
                // ⭐ A whole cell: land on it, and carry the rest of the tick into the next edge.
                g.Cell = g.Next.Value; g.Next = null; g.Progress = 0; g.RouteIndex++; g.Steps++;
                if (g.Cell == g.Destination) g.State = GuestState.Arrived;
            }
        }
    }

    /// <summary>Choose the edge to walk next: the route's next cell if it is still there to be
    /// walked on, otherwise a fresh route from here. False when the guest is going nowhere.</summary>
    bool Commit(Guest g)
    {
        if (g.Cell == g.Destination) { g.State = GuestState.Arrived; return false; }
        var ahead = Ahead(g);
        if (ahead is ParkCell cell && Standable(g, cell)) { g.Next = cell; return true; }
        // ⚠ The way ahead has gone -- un-laid since this route was found. Look again from here;
        // the start is exempt from being open, so a guest whose OWN cell went can still walk off it.
        if (!Assign(g))
        {
            g.State = GuestState.Stranded;
            g.Reason = !Paths.Walkable(g.Destination) ? $"the destination {g.Destination} is no longer a path"
                     : ahead is ParkCell lost ? $"the path at {lost} is gone and there is no other way from {g.Cell} to {g.Destination}"
                     : $"no way from {g.Cell} to {g.Destination}";
            return false;
        }
        g.Reroutes++;
        g.Next = Ahead(g).Value;
        return true;
    }

    static ParkCell? Ahead(Guest g) => g.Route != null && g.RouteIndex + 1 < g.Route.Count ? g.Route[g.RouteIndex + 1] : null;

    /// <summary>May this guest step onto this cell: open ground, or its own destination if that
    /// is merely walkable -- the queue-tile exemption Route makes, applied at the moment of the
    /// step so a stub is not read as "the way ahead has gone".</summary>
    bool Standable(Guest g, ParkCell c) => Paths.Open(c) || (c == g.Destination && Paths.Walkable(c));

    /// <summary>A fresh route from where the guest stands. ⚠ On failure the OLD route is left in
    /// place: a stranded guest's record says what it was walking when the way went, which is what
    /// a census wants to print.</summary>
    bool Assign(Guest g)
    {
        var route = Route(g.Cell, g.Destination);
        if (route == null) return false;
        g.Route = route; g.RouteIndex = 0;
        return true;
    }
}
