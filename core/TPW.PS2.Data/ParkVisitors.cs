namespace TPW.PS2.Data;

/// <summary>What a guest is currently up to.</summary>
public enum VisitorIntent
{
    /// <summary>Going nowhere in particular, along the paths.</summary>
    Wandering,
    /// <summary>Walking to a ride's queue stub.</summary>
    Heading,
    /// <summary>Standing at the stub, handed to the ride. ⚠ NOT ON THE PATH GRID any more --
    /// the ride's own script owns them from here, through WALKON.</summary>
    Queued,
}

/// <summary>⭐⭐ THE TWO HALVES, JOINED. <see cref="GuestWalk"/> moves people over the park's
/// paths and <see cref="ParkSim"/> runs the rides' own bytecode; each was provable on its own and
/// neither was a game. This is the piece between them: a guest walks to a ride's queue, is handed
/// over, disappears into the script's world for the length of a cycle, and is handed back at the
/// exit to walk off again.
///
/// ⭐ THE HANDOVER IS TOTAL, and that is the design. Once a guest joins a ride they are REMOVED
/// from the walking layer, because from that moment the script is moving them -- WALKON puts them
/// on a seat node, the ticker carries them, WALKOFF sends them out. Two systems each thinking they
/// own a body is how you get a guest standing in the queue and riding at the same time.
///
/// ⚠⚠ WHOM TO VISIT AND HOW LONG TO DAWDLE ARE OURS. The console's guest AI has not been read:
/// there is no needs model, no money, no happiness, no queue-length judgement here, and the ride
/// a guest picks is picked at random from the ones they can reach. What IS the game's is
/// everything either side of it -- the routing over real laid path, and the whole boarding
/// handshake through VAR_LETMEON/VAR_LETMEOFF that the scripts define.</summary>
public sealed class ParkVisitors
{
    public GuestWalk Walk { get; }
    public ParkSim Sim { get; }

    /// <summary>A guest's plan. ⚠ Keyed by guest id and NOT by <see cref="Guest"/>, because a
    /// queued guest has no Guest object at all -- they left the walking layer.</summary>
    public sealed record Plan(int Guest, VisitorIntent Intent, int RideId, ParkCell At);
    readonly Dictionary<int, Plan> _plans = new();
    public IReadOnlyDictionary<int, Plan> Plans => _plans;

    /// <summary>How many guests have finished a ride and walked away from it. The honest measure
    /// of whether this whole stack does anything: it only moves when a guest routed to a queue,
    /// was boarded by a script, was handed back by that script, and walked off.</summary>
    public int Rides { get; private set; }
    public int Boardings { get; private set; }

    readonly Func<int> _random;

    public ParkVisitors(ParkSim sim, GuestWalk walk, Func<int> random = null)
    {
        Sim = sim ?? throw new ArgumentNullException(nameof(sim));
        Walk = walk ?? throw new ArgumentNullException(nameof(walk));
        var rng = new Random(7);
        _random = random ?? (() => rng.Next());
    }

    /// <summary>A ride a guest could actually go to: open, and with a queue stub to stand on.
    /// ⚠ A ride whose script never declares VAR_LETMEON cannot take anybody, so it is not a
    /// destination -- offering a guest to one would strand them in a queue forever.</summary>
    public bool Takes(ParkRide ride) =>
        ride is { Entrance: not null } && ride.Has("VAR_LETMEON") && ride.Fault == null;

    public IEnumerable<ParkRide> Open => Sim.Rides.Where(Takes);

    /// <summary>Put a guest in at the gate and set them wandering.</summary>
    public Guest Arrive(ParkCell at, ParkCell to)
    {
        var g = Walk.Spawn(at, to);
        _plans[g.Id] = new Plan(g.Id, VisitorIntent.Wandering, 0, at);
        return g;
    }

    /// <summary>Send a walking guest to a ride's queue. False when there is no way there, and the
    /// guest is left doing whatever they were doing rather than stuck mid-plan.</summary>
    public bool SendTo(Guest guest, ParkRide ride)
    {
        if (guest == null || !Takes(ride)) return false;
        if (!Walk.Send(guest, ride.Entrance.Value)) return false;
        _plans[guest.Id] = new Plan(guest.Id, VisitorIntent.Heading, ride.Id, guest.Cell);
        return true;
    }

    /// <summary>One tick of the whole park: the walking layer, the scripts, and the two handovers
    /// between them.
    ///
    /// ⚠ ORDER MATTERS AND IT IS THIS ONE. Guests are collected from the rides BEFORE the sim
    /// runs, so a guest handed back on the previous tick is standing on the exit before the script
    /// can hand back another; and guests are delivered INTO queues after they have moved, so a
    /// guest who arrived this tick joins this tick rather than a frame late.</summary>
    public void Step(double deltaSeconds, Func<ParkCell> wander)
    {
        Collect();
        Walk.Advance(deltaSeconds);
        Sim.Advance(deltaSeconds);
        Deliver();
        Idle(wander);
    }

    /// <summary>Guests the scripts have finished with go back on the path at the ride's exit.
    ///
    /// ⚠ AT THE EXIT, NOT WHERE THEY QUEUED. A ride that dropped its riders back at its entrance
    /// would send them straight round again through the queue they just left, and the park would
    /// look busy while nobody ever went anywhere.</summary>
    void Collect()
    {
        foreach (var ride in Sim.Rides)
        {
            if (ride.Left.Count == 0) continue;
            var back = ride.Exit ?? ride.Entrance;
            foreach (int guest in ride.Left)
            {
                _plans.Remove(guest);
                if (back is not { } cell) continue;
                // ⭐ THE SAME PERSON, not a new one. Spawn would issue a fresh id, and then the
                // guest who queued and the guest who walked away would be different people to
                // anything counting them -- which is every feature that comes after this one.
                var g = Walk.Readmit(guest, cell, cell);
                _plans[g.Id] = new Plan(g.Id, VisitorIntent.Wandering, 0, cell);
                Rides++;
            }
            ride.ClearLeft();
        }
    }

    ParkRide RideOf(Plan plan) => Sim.Rides.FirstOrDefault(r => r.Id == plan.RideId);

    /// <summary>A guest who has reached the stub they were heading for joins that ride's queue and
    /// leaves the walking layer.</summary>
    void Deliver()
    {
        foreach (var g in Walk.Guests.ToArray())
        {
            if (!_plans.TryGetValue(g.Id, out var plan) || plan.Intent != VisitorIntent.Heading) continue;
            if (g.State != GuestState.Arrived) continue;
            // ⚠⚠ ARRIVED SOMEWHERE IS NOT ARRIVED HERE. This used to join the queue on State alone,
            // so a guest heading for a ride who finished any other walk -- re-routed round a dug
            // path, or re-sent while still Heading -- was handed to that ride's queue from
            // wherever they happened to be standing. Caught by the agent wiring this into the
            // viewer, where it would have looked like teleporting into a queue.
            var ride = RideOf(plan);
            if (ride != null && g.Cell != ride.Entrance) continue;
            // ⚠ THE RIDE MAY HAVE GONE, or closed, or broken, while they walked. Then this is not
            // a queue any more and they are just somebody standing in a park.
            if (ride == null || !Takes(ride))
            {
                _plans[g.Id] = plan with { Intent = VisitorIntent.Wandering, RideId = 0 };
                continue;
            }
            ride.Join(g.Id);
            Boardings++;
            _plans[g.Id] = plan with { Intent = VisitorIntent.Queued, At = g.Cell };
            Walk.Remove(g.Id);
        }
    }

    /// <summary>Anybody standing about picks something to do: a ride if one will take them,
    /// otherwise a walk to wherever <paramref name="wander"/> suggests.</summary>
    void Idle(Func<ParkCell> wander)
    {
        foreach (var g in Walk.Guests.ToArray())
        {
            // ⚠ A GUEST WHO CANNOT GET THERE MUST BE ABLE TO GIVE UP. Only Arrived was handled
            // here, so somebody Heading for a ride whose path was dug up under them stayed
            // Stranded forever with a plan nobody would ever complete -- a slowly filling pool of
            // people standing still. They go back to Wandering and are re-tasked like anyone else.
            if (g.State is GuestState.NoRoute or GuestState.Stranded
                && _plans.TryGetValue(g.Id, out var stuck) && stuck.Intent == VisitorIntent.Heading)
                _plans[g.Id] = stuck with { Intent = VisitorIntent.Wandering, RideId = 0 };
            if (g.State != GuestState.Arrived) continue;
            if (_plans.TryGetValue(g.Id, out var plan) && plan.Intent == VisitorIntent.Heading) continue;
            var rides = Open.ToArray();
            if (rides.Length > 0 && SendTo(g, rides[(int)((uint)_random() % (uint)rides.Length)])) continue;
            if (wander?.Invoke() is { } cell) Walk.Send(g, cell);
        }
    }
}
