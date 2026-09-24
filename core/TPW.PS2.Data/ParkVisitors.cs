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
    /// <summary>The old ride no longer owns this guest, but no valid ground is available yet.
    /// The coordinator retains the identity and retries readmission on later steps.</summary>
    Recovering,
    /// <summary>Had enough, and walking to the gate to go home. ⚠ Still an ordinary walker until
    /// they reach it -- they are drawn, they take up path, and a want can still rise on them.</summary>
    Leaving,
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
    // Numeric ride IDs may be reused after deletion. Ownership belongs to the
    // actual instance, so a replacement cannot silently inherit the old riders.
    readonly Dictionary<int, ParkRide> _owners = new();
    /// <summary>⚠ <paramref name="Ride"/> is the place they were IN, kept because `_owners` is
    /// cleared the moment they start coming back and <see cref="RecoverGuests"/> still has to ask
    /// what that place was. ⚠ Positional component added deliberately: this record is only ever a
    /// dictionary VALUE and is never compared or Distinct()ed -- the same addition to
    /// ParkEntrance silently changed its equality and cost two worlds their entrance.</summary>
    sealed record ReturnToPark(ParkCell[] Preferred, bool CompletedRide, ParkRide Ride);
    readonly Dictionary<int, ReturnToPark> _returning = new();
    public IReadOnlyDictionary<int, Plan> Plans => _plans;

    /// <summary>How many guests have finished a ride and walked away from it. The honest measure
    /// of whether this whole stack does anything: it only moves when a guest routed to a queue,
    /// was boarded by a script, was handed back by that script, and walked off.</summary>
    public int Rides { get; private set; }
    public int Boardings { get; private set; }

    /// <summary>How many guests have gone home. ⭐ Counted because "the park empties" and "the
    /// park never fills" look identical in a population graph and need different fixes.</summary>
    public int WentHome { get; private set; }

    /// <summary>Where a guest who has had enough walks to. ⚠ The park's own entrance cells, which
    /// are also its exit -- the game has one gate. Null-safe: with no entrance registered nobody
    /// can leave, which is the honest outcome rather than deleting them where they stand.</summary>
    ParkCell? Gate => Walk.Paths.EntranceCells.Count == 0 ? null
                    : Walk.Paths.EntranceCells.OrderBy(c => c.Z).ThenBy(c => c.X).First();

    readonly Func<int> _random;

    public ParkVisitors(ParkSim sim, GuestWalk walk, Func<int> random = null)
    {
        Sim = sim ?? throw new ArgumentNullException(nameof(sim));
        Walk = walk ?? throw new ArgumentNullException(nameof(walk));
        var rng = new Random(7);
        _random = random ?? (() => rng.Next());
    }

    /// <summary>A ride a guest could actually go to: open, unbroken, and with a queue stub to stand on.
    /// ⚠ A ride whose script never declares VAR_LETMEON cannot take anybody, so it is not a
    /// destination -- offering a guest to one would strand them in a queue forever.</summary>
    public bool Takes(ParkRide ride) =>
        ride is { Entrance: not null } && ride.Has("VAR_LETMEON")
        && ride.Get("VAR_RIDECLOSED") == 0 && ride.Get("VAR_BROKEN") == 0 && ride.Fault == null;

    public IEnumerable<ParkRide> Open => Sim.Rides.Where(Takes);

    /// <summary>⭐⭐ WHAT A PLACE IS FOR, and every one of these is read rather than decided here:
    /// `UsageInfo.ProvidesRelief` marks a lavatory (exactly 7 .sam files across the four worlds,
    /// and the SAME 7 scripts are the only ones declaring VAR_WORNON -- 7 of 277, no exceptions
    /// either way), and the shop effects carry the developers' own comments, e.g.
    /// <c>UsageInfo.HungerEffect 25 //How much hunger to deduct</c>.
    ///
    /// ⚠ A FACILITY MUST ALSO BE ABLE TO TAKE SOMEBODY. Being a toilet is not enough -- a closed
    /// or broken one is not an answer to anything, so each goes through <see cref="Takes"/> and a
    /// guest is never sent to a door that will not open.</summary>
    public bool Relieves(ParkRide r) => Takes(r) && r.ProvidesRelief;
    public bool Feeds(ParkRide r) => Takes(r) && (r.Definition?.HungerEffect ?? 0) > 0;
    public bool Waters(ParkRide r) => Takes(r) && (r.Definition?.ThirstEffect ?? 0) > 0;

    /// <summary>How many guests have been served, by kind. ⭐ Counters rather than a bool, because
    /// "the toilet worked" and "the toilet worked once per visit" are different claims and an
    /// audit should be able to tell them apart.</summary>
    public int Relieved { get; private set; }
    public int Purchases { get; private set; }

    readonly Dictionary<int, int> _soil = new();

    /// <summary>Mess left in each lavatory, by ride id, from <see cref="VisitorNeeds.UseToilet"/>.
    ///
    /// ⚠⚠ THE SINK IS THE PORT'S; the amount is not. `(toilet - 60) * 2 / 3` is decoded, but where
    /// the console PUTS it has not been found -- and it is NOT VAR_WORNON, which was the obvious
    /// guess and is wrong: the toilet scripts TEST it, COPY 1 into it and COPY 0 back out, so it
    /// is a single-occupancy latch the script owns and writing mess into it would fight the
    /// script. Until the real channel turns up this accumulates here and drives nothing.</summary>
    public IReadOnlyDictionary<int, int> Soil => _soil;

    /// <summary>The ride a guest is CURRENTLY inside or queued for, or null. For the renderer:
    /// a guest who is Queued has been taken off the walking layer, so this is the only handle on
    /// where their body should be drawn.
    ///
    /// ⭐ THE LIVE OBJECT, NOT AN ID. Ride ids are reused as rides are demolished and replaced, so
    /// an id held across a frame can name a different ride than the one the guest went into; this
    /// hands back the instance and only while the sim still holds it.
    ///
    /// ⚠ AND `Machine.GuestIds` IS NOT THE SAME QUESTION. A Small Toilet's script never HUSHes
    /// anybody -- it has no LIMBO at all -- so its guest never appears in the machine's list, and
    /// a renderer gating on that would draw nobody at exactly the facility that needs drawing.
    /// Asked for by astraclaw for the standing-service body.</summary>
    public ParkRide QueuedOwner(int guest) =>
        _plans.TryGetValue(guest, out var plan) && plan.Intent == VisitorIntent.Queued
        && _owners.TryGetValue(guest, out var ride) && Sim.Rides.Contains(ride) ? ride : null;

    /// <summary>Put a guest in at the gate and set them wandering.</summary>
    /// <summary>⭐⭐ THE VISITORS' WANTS, and they are seeded HERE AND NOWHERE ELSE. Readmission
    /// after a ride keeps a guest's id but builds a NEW <see cref="Guest"/>, so needs must live
    /// beside the walking layer rather than on it, and must not be re-rolled when the person comes
    /// back off a rollercoaster (agreed with astraclaw while they were landing the removal
    /// lifecycle, 2026-09-23). Null leaves the park exactly as it was before needs existed.</summary>
    public VisitorNeeds Needs { get; set; }

    /// <summary>What a ride does to a rider, applied ONCE on genuine completion.
    ///
    /// ⚠⚠ ALL FOUR ARE CHOSEN. `FUN_0020EDD8` reads the ride's own value and three globals
    /// (`DAT_002EEB30/34/44`), and neither has been decoded -- so these stand in, carrying the
    /// console's SHAPE only: sickness is measured against **30**, so an intensity below that
    /// settles the stomach and above it turns one.
    ///
    /// ⭐ Exposed as properties so an audit can set them and assert the exact arithmetic,
    /// rather than having to know a constant buried in a method.</summary>
    public int RideIntensity { get; set; } = 45;
    public int RideHappiness { get; set; } = 8;
    public float RideSickScale { get; set; } = 0.25f;
    public float RideBoredomScale { get; set; } = 0.5f;

    public Guest Arrive(ParkCell at, ParkCell to)
    {
        var g = Walk.Spawn(at, to);
        Wander(g.Id, at);
        Needs?.Spawn(g.Id);
        return g;
    }

    /// <summary>Send a walking guest to a ride's queue. False when there is no way there, and the
    /// guest is left doing whatever they were doing rather than stuck mid-plan.</summary>
    public bool SendTo(Guest guest, ParkRide ride)
    {
        if (guest == null || !Walk.Guests.Contains(guest) || !Takes(ride) || !Sim.Rides.Contains(ride)) return false;
        if (!Walk.Send(guest, ride.Entrance.Value)) return false;
        _plans[guest.Id] = new Plan(guest.Id, VisitorIntent.Heading, ride.Id, guest.Cell);
        _owners[guest.Id] = ride;
        _returning.Remove(guest.Id);
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
        ReconcileRemovedRides();
        Collect();
        RecoverGuests();
        // ⭐ KEEP WHAT THE PARK ACTUALLY RAN. Both clocks cap at eight ticks a call and DROP the
        // remainder, so the time that passed for the guests is the ticks they took -- not the
        // delta they were offered.
        int ticks = Walk.Advance(deltaSeconds);
        Sim.Advance(deltaSeconds);
        Deliver();
        Idle(wander);
        // ⚠⚠ LAST, AND AFTER Idle. `_plans` is the live set -- it still holds guests in
        // Recovering, and it has already dropped anyone retired this step -- so reconciling here
        // forgets exactly the people who have gone. Ids are REUSED; a stale entry hands the next
        // arrival a dead stranger's hunger.
        // ⚠⚠ THE PARK'S CLOCK, NOT THE FRAME'S, AND THAT IS A SECOND BUG ON TOP OF THE FIRST.
        // The rise already follows a clock rather than the call count -- but it was the FRAME'S
        // delta, and a frame is not what the park experiences. Walk.Advance and Sim.Advance run at
        // most EIGHT ticks (320 ms) per call and throw the rest away, exactly as a dropped frame
        // should; feeding the raw delta to the needs aged a guest ten seconds while the park moved
        // 0.32 of one. astraclaw's probe: hunger 50 against 11 for the same 320 ms of park time.
        //
        // ⭐ So the needs take the ticks the walk ACTUALLY took. Reconcile still runs either
        // way, because a guest can be retired on a step that moved no clock at all.
        if (Needs != null)
        {
            int rises = Needs.Step(ticks * GuestWalk.TickMilliseconds / 1000.0);
            // ⭐⭐ WAITING IN A QUEUE COSTS SOMETHING, and it did not until now. `VisitorNeeds.Queue`
            // was decoded off `FUN_0020C6A8` -- happiness down, `+0x78` up -- documented, carried
            // eighteen references from the checks, and was CALLED FROM NOWHERE: queueing was free.
            // Found by tools/dead_port_audit.py one commit after WantsToGoHome, which is the same
            // shape and the reason that tool now exists in this port too.
            //
            // ⚠ THE CADENCE IS CHOSEN, THE EFFECT IS NOT. How often the console charges this has
            // not been read. Rather than invent a second clock it rides the one the needs already
            // use, so there is still exactly one chosen constant (`SecondsPerRise`) instead of two
            // -- and a guest who waits twice as long pays twice, which is the part that matters.
            // ⚠⚠ STILL IN THE QUEUE, NOT MERELY `Queued`. That intent covers BOTH waiting at
            // the stub and being aboard -- the ride owns them from WALKON onwards without the
            // plan changing -- so charging on the intent alone billed riders a waiting cost and
            // mutated a seated guest's record. The needs-lifecycle checks caught it: "seated
            // guest retains its entire side-table state" went red. The ride's own queue is the
            // honest test, because the script removes them from it when it takes them.
            if (rises > 0)
                foreach (var (guest, plan) in _plans)
                    if (plan.Intent == VisitorIntent.Queued
                        && _owners.TryGetValue(guest, out var waitingAt)
                        && waitingAt.Queue.Contains(guest))
                        for (int i = 0; i < rises; i++) Needs.Queue(guest);
            Needs.Reconcile(_plans.Keys);
        }
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
            foreach (int guest in ride.Left)
            {
                // A stale/repeated mailbox value must not invent a person or
                // readmit somebody already walking (Readmit itself permits duplicates).
                if (!_plans.TryGetValue(guest, out var plan) || plan.Intent != VisitorIntent.Queued
                    || !_owners.TryGetValue(guest, out var owner) || !ReferenceEquals(owner, ride)) continue;
                QueueReturn(plan, ride, completed: true);
            }
            ride.ClearLeft();
        }
    }

    void Wander(int guest, ParkCell at)
    {
        _plans[guest] = new Plan(guest, VisitorIntent.Wandering, 0, at);
        _owners.Remove(guest);
        _returning.Remove(guest);
    }

    void QueueReturn(Plan plan, ParkRide ride, bool completed)
    {
        bool stillWaiting = !completed && (ride.Queue.Contains(plan.Guest) || ride.Get("VAR_LETMEON") == plan.Guest);
        var preferred = new[] { stillWaiting ? ride.Entrance : ride.Exit,
                                stillWaiting ? ride.Exit : ride.Entrance, (ParkCell?)plan.At }
            .Where(c => c.HasValue).Select(c => c.Value).Distinct().ToArray();
        _returning[plan.Guest] = new ReturnToPark(preferred, completed, ride);
        _plans[plan.Guest] = new Plan(plan.Guest, VisitorIntent.Recovering, 0, preferred[0]);
        _owners.Remove(plan.Guest);
    }

    /// <summary>Removal/Clear is a managed-port lifecycle boundary, not a decoded
    /// console evacuation rule. Heading guests keep their physical walk; only the
    /// destination's ride intent is cancelled. Ride-owned identities return once.
    /// Whole-park teardown should discard this coordinator together with its walk.</summary>
    void ReconcileRemovedRides()
    {
        var live = Sim.Rides.ToHashSet();
        foreach (var (guest, ride) in _owners.ToArray())
        {
            if (live.Contains(ride)) continue;
            if (!_plans.TryGetValue(guest, out var plan)) { _owners.Remove(guest); continue; }
            var walking = Walk.Guests.FirstOrDefault(g => g.Id == guest);
            if (plan.Intent == VisitorIntent.Queued)
            {
                // A handback already reported by the script is still a completed
                // ride even if deletion happens before the coordinator collects it.
                bool completed = ride.Left.Contains(guest) || ride.Get("VAR_LETMEOFF") == guest;
                QueueReturn(plan, ride, completed);
            }
            else if (walking != null) Wander(guest, walking.Cell);
            else { _plans.Remove(guest); _owners.Remove(guest); }
        }
    }

    void RecoverGuests()
    {
        ParkCell[] publicGround = null; // lazy, shared across this step if endpoints are gone
        foreach (var (guest, returning) in _returning.ToArray())
        {
            var walking = Walk.Guests.FirstOrDefault(g => g.Id == guest);
            if (walking == null)
            {
                ParkCell? at = returning.Preferred.Where(c => Walk.Paths.Contains(c) && Walk.Paths.Walkable(c))
                    .Select(c => (ParkCell?)c).FirstOrDefault();
                if (at == null)
                {
                    publicGround ??= Walk.Paths.Cells.Where(Walk.Paths.Open).ToArray();
                    var origin = returning.Preferred[0];
                    at = publicGround.OrderBy(c => Math.Abs((long)c.X - origin.X) + Math.Abs((long)c.Z - origin.Z))
                        .ThenBy(c => c.Z).ThenBy(c => c.X).Select(c => (ParkCell?)c).FirstOrDefault();
                }
                if (at == null) continue; // retain explicit ownership, retry when ground is restored
                walking = Walk.Readmit(guest, at.Value, at.Value);
            }
            Wander(guest, walking.Cell); // only relinquish recovery ownership after readmission
            if (returning.CompletedRide)
            {
                Rides++;
                // ⭐⭐ THE RIDE CHANGED HOW THEY FEEL, and this is the ONE place that knows a
                // ride genuinely happened. I first put it in `Collect`, reasoning that a
                // demolished ride must not pay out a ride's worth of happiness -- astraclaw
                // corrected me: `ReconcileRemovedRides` marks completion only when `Left` or
                // `VAR_LETMEOFF` has ALREADY reported the handback, so ordinary demolition is not
                // completion at all. Here catches both routes, skips aborted rides, and fires once
                // per guest because `Wander` drops them from `_returning` on the same pass.
                //
                // ⚠ This CHANGES needs -- that is the feature -- so a continuity check asserts
                // these effects happened exactly once, not that the record is untouched. Cash is
                // the reseed tripwire: nothing on this path touches it.
                Serve(guest, returning.Ride);
            }
        }
    }

    /// <summary>They reach the gate and are gone. ⭐ Dropping the PLAN is what retires them:
    /// `Needs.Reconcile(_plans.Keys)` reaps any record with no plan behind it, so a departure
    /// cannot leave a needs row behind to be inherited by whoever gets that id next.</summary>
    void ShowOut(int guest)
    {
        Walk.Remove(guest);
        _plans.Remove(guest);
        _owners.Remove(guest);
        _returning.Remove(guest);
        WentHome++;
    }

    ParkRide RideOf(Plan plan) => _owners.TryGetValue(plan.Guest, out var ride) && Sim.Rides.Contains(ride) ? ride : null;

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
                Wander(g.Id, g.Cell);
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
                Wander(g.Id, g.Cell);
            if (g.State != GuestState.Arrived) continue;
            if (_plans.TryGetValue(g.Id, out var plan) && plan.Intent == VisitorIntent.Heading) continue;
            // ⭐⭐ AND HAVING HAD ENOUGH BEATS BOTH. `WantsToGoHome` was decoded, documented
            // and CALLED FROM NOWHERE -- the arithmetic had its own checks while the park it
            // described could never lose a single guest. Dead code reads exactly like a feature
            // from the outside, which is why this is asked before anything else a guest might do.
            if (Needs != null && Needs.WantsToGoHome(g.Id) && Gate is { } gate)
            {
                if (g.Cell == gate) { ShowOut(g.Id); continue; }
                if (Walk.Send(g, gate))
                {
                    _plans[g.Id] = new Plan(g.Id, VisitorIntent.Leaving, 0, g.Cell);
                    _owners.Remove(g.Id);
                    continue;
                }
                // No route to the gate: they stay in the park and try again next tick rather
                // than standing still forever with a plan nobody completes.
            }
            // ⭐⭐ SOMETHING PRESSING BEATS SOMETHING FUN. A guest who needs a lavatory and
            // picks a rollercoaster instead is the whole reason wants looked wired-up but dead:
            // the need rose, the bubble appeared, and then they queued for the Crazy Ape and it
            // rose some more. The errand is tried first and only falls through when nothing is
            // urgent or nowhere answers it.
            // ⚠ Down the list until one takes them, so an unreachable near one is skipped
            // rather than ending the errand -- see Errand.
            bool onErrand = false;
            foreach (var errand in Errand(g)) if (SendTo(g, errand)) { onErrand = true; break; }
            if (onErrand) continue;
            var rides = Open.ToArray();
            if (rides.Length > 0 && SendTo(g, rides[(int)((uint)_random() % (uint)rides.Length)])) continue;
            if (wander?.Invoke() is { } cell) Walk.Send(g, cell);
        }
    }

    /// <summary>What the place they have just come out of did to them.
    ///
    /// ⭐⭐ THE NUMBERS ARE THE GAME'S OWN. A shop's effects are read off its .sam, so a Burger
    /// Shop deducts the 25 hunger IT declares rather than a constant chosen here -- which is the
    /// difference between this and <see cref="RideIntensity"/> and friends, and those stay
    /// invented only because the globals behind them have not been decoded.
    ///
    /// ⚠ ONE KIND EACH, and a lavatory is tested FIRST: nothing in the game both relieves and
    /// sells, but if a mod ever did, relief is the need with a hard threshold behind it.
    ///
    /// ⚠ `UsageInfo.LitterEffect` IS AUTHORED AND IS NOT APPLIED HERE. The guest has a Litter
    /// field and 23 shops declare the effect, but the decoded purchase path
    /// (`0x20E380..0x20E45C`, findings/dba.md) does exactly four things and littering is not one
    /// of them. Applying it anyway would be inventing a cadence and calling it a decode; it waits
    /// for the litter path. Same for FatigueEffect, which has no field at all yet.</summary>
    void Serve(int guest, ParkRide used)
    {
        if (Needs == null || !Needs.Has(guest)) return;
        if (used != null && used.ProvidesRelief)
        {
            int soil = Needs.UseToilet(guest);
            if (soil > 0) _soil[used.Id] = _soil.TryGetValue(used.Id, out var had) ? had + soil : soil;
            Relieved++;
            return;
        }
        if (used?.Definition is { Sells: true } def)
        {
            Needs.Buy(guest, def.PricePerUse ?? 0, def.HungerEffect ?? 0, def.ThirstEffect ?? 0,
                      def.HappinessEffect ?? 0, def.VomitEffect ?? 0);
            Purchases++;
            return;
        }
        Needs.Ride(guest, RideIntensity, RideHappiness, RideSickScale, RideBoredomScale);
    }

    /// <summary>Where a guest with something pressing on their mind is trying to get to, or null
    /// if nothing is pressing or nowhere answers it.
    ///
    /// ⭐⭐ THE BAR IS THE CONSOLE'S ONE BAR. `FUN_0020F888` opens `if (need &lt; 0x5b) return 0`
    /// -- 91, the same number for hunger, thirst and toilet, and the same one
    /// <see cref="VisitorNeeds.Decide"/> puts the bubble up at. So a guest walks to a facility
    /// exactly when they are thinking about it, rather than on a second threshold invented here.
    ///
    /// ⚠ THE ORDER IS THE CONSOLE'S, hunger then thirst then toilet, and it only breaks TIES:
    /// the most pressing need wins, and `&gt;` rather than `&gt;=` keeps the earlier one on a draw.</summary>
    /// ⚠⚠ EVERY CANDIDATE, NEAREST FIRST -- NOT the nearest one. This returned a single ride,
    /// and a guest whose closest lavatory had no walkable route to it gave up on the errand
    /// entirely and took a random ride instead, with the need still burning. The one nearby that
    /// happens to be cut off must not hide the three that are not. Found in review by astraclaw.
    IEnumerable<ParkRide> Errand(Guest g)
    {
        if (Needs == null || !Needs.Has(g.Id)) return Array.Empty<ParkRide>();
        var w = Needs.Of(g.Id);
        Func<ParkRide, bool> chosen = null;
        int worst = VisitorNeeds.Urgent - 1;
        foreach (var (need, answers) in new (int, Func<ParkRide, bool>)[]
                 { (w.Hunger, Feeds), (w.Thirst, Waters), (w.Toilet, Relieves) })
        {
            if (need <= worst) continue;
            // ⚠ Must still be a want something in this park ANSWERS, or a guest who is merely
            // hungrier than they are desperate would claim the errand and then walk nowhere,
            // shadowing the toilet they could actually have reached.
            if (!Sim.Rides.Any(answers)) continue;
            chosen = answers;
            worst = need;
        }
        return chosen == null ? Array.Empty<ParkRide>() : Ordered(g.Cell, chosen);
    }

    /// <summary>The closest place that answers <paramref name="answers"/>. ⚠ Manhattan on the
    /// QUEUE STUB, not the building: the stub is where they are actually walking to, and a big
    /// ride's origin can be several cells from its door. Ties break on id so a park full of
    /// identical toilets still routes the same way twice.</summary>
    IEnumerable<ParkRide> Ordered(ParkCell from, Func<ParkRide, bool> answers) =>
        Sim.Rides.Where(r => answers(r))
            .OrderBy(r => Math.Abs((long)r.Entrance.Value.X - from.X)
                        + Math.Abs((long)r.Entrance.Value.Z - from.Z))
            .ThenBy(r => r.Id);
}
