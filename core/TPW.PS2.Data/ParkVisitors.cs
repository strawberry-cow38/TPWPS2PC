namespace TPW.PS2.Data;

/// <summary>What a guest is currently up to.</summary>
public enum VisitorIntent
{
    /// <summary>Going nowhere in particular, along the paths.</summary>
    Wandering,
    /// <summary>Walking to a ride stub or a compiled shop's inside entrance.</summary>
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
/// Selection still uses the port's needs-first/random policy, not the native weighted
/// scorer. The post-completion destination gate now follows the traced counter expressions;
/// other native AI states remain separate integration work. Compiled placed shops now walk
/// into the inside entrance and preserve that position across service.
/// In particular, native shops use common guest walking/service states, not an RSE WALKON
/// handshake merely because this managed adapter currently connects them through scripts.
/// See findings/native-shop-flow.md for proven consumer paths and remaining boundaries.</summary>
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
    readonly Dictionary<int, GuestTerminal> _serviceTerminals = new();
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
    readonly GuestDecisionSchedule _decisions;
    // One monotonic producer for this coordinator: actual executed park ticks, not render
    // calls, offered delta, or the needs component's independently adjustable test rate.
    // This is the port's clock mapping, not proof of unconditional native wall-time pacing.
    uint DecisionTick => unchecked((uint)(Sim.Time / ParkSim.TickMilliseconds));

    public ParkVisitors(ParkSim sim, GuestWalk walk, Func<int> random = null)
    {
        Sim = sim ?? throw new ArgumentNullException(nameof(sim));
        Walk = walk ?? throw new ArgumentNullException(nameof(walk));
        var rng = new Random(7);
        _random = random ?? (() => rng.Next());
        _decisions = new GuestDecisionSchedule(_random);
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


/// <summary>How worn each lavatory is, by ride id -- a VIEW over
    /// <see cref="ParkRide.Condition"/> rather than a tally of its own.
    ///
    /// ⚠ The port used to accumulate this upward in a side table, which is the same arithmetic
    /// the wrong way round: the console keeps a CONDITION on the facility that use depletes
    /// toward zero (`+0xb4`, `FUN_00130948`). Now that the field lives where the console puts it,
    /// this is simply `100 - Condition` -- kept because two audits read it, and because "how
    /// dirty" is the question a caller actually asks.
    ///
    /// ⚠ Absent means UNWORN, not unknown: a facility at full condition has no entry.</summary>
    public IReadOnlyDictionary<int, int> Soil =>
        Sim.Rides.Where(r => r.Condition < 100).ToDictionary(r => r.Id, r => 100 - r.Condition);

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
    /// The default ride value remains chosen. The executable image's scale constants and
    /// preference-dependent happiness bands are now read; the properties below retain explicit
    /// port/test overrides rather than claiming all four inputs are still unknown.
    ///
    /// ⚠⚠ AND THIS USED TO SAY "an intensity below 30 settles the stomach", WHICH IS BACKWARDS.
    /// Nothing settles a stomach: `0x20F248` branches PAST the sickness term unless the ride's
    /// value is above 55, with no other arm. The 30 is real but it is the pivot inside the term,
    /// not a threshold around it -- so the port reduced sickness below30 and added it at30..55,
    /// including its default45, where the original leaves sickness unchanged. astraclaw found
    /// it by reading the consumer rather than the
    /// constant, which is the difference between knowing a number and knowing what it does.
    ///
    /// ⭐ Exposed as properties so an audit can set them and assert the exact arithmetic,
    /// rather than having to know a constant buried in a method.</summary>
    public int RideIntensity { get; set; } = 45;

    /// ⭐⭐ THREE OF THESE ARE NOW READ, NOT CHOSEN. `FUN_0020EDD8`'s three globals were decoded
    /// by reading them out of the executable's image at the addresses the decompile names:
    ///
    ///   `DAT_002eeb44` = **15**   -> best happiness band; the other bands are10/5.
    ///                                RideHappiness is a fallback for an unspecified preference.
    ///   `DAT_002eeb30` = **1212** -> sick += 1212 * (intensity - 30) * 0x1000 >> 0x18,
    ///                                gated at56; scale1212/4096 = 0.2959. Was0.25.
    ///   `DAT_002eeb34` = **4096** -> boredom -= 4096 * intensity * 0x1000 >> 0x18. 4096*4096 is
    ///                                exactly 2^24, so the shift cancels it: boredom falls by the
    ///                                intensity ITSELF, scale 1.0. Was 0.5.
    ///
    /// ⭐ The 30 in the sickness term is the same 30 this class already documented from a separate
    /// reading of the pivot -- two routes to the same number, which is what makes it a corroboration
    /// rather than a second guess.
    ///
    /// ⚠ READ FROM THE IMAGE, which is the weaker reading; a savestate would confirm them against
    /// a running machine. <see cref="RideIntensity"/> remains chosen; connecting the correct
    /// per-ride producer is separate work, not established by reading these globals.
    public int RideHappiness { get; set; } = 15;
    public float RideSickScale { get; set; } = 1212f / 4096f;
    public float RideBoredomScale { get; set; } = 1f;

    public Guest Arrive(ParkCell at, ParkCell to)
    {
        var g = Walk.Spawn(at, to);
        _decisions.Forget(g.Id); // a reused identity must not inherit somebody else's deadline
        Wander(g.Id, at);
        Needs?.Spawn(g.Id);
        return g;
    }

    /// <summary>Send a walking guest to a ride's queue. False when there is no way there, and the
    /// guest is left doing whatever they were doing rather than stuck mid-plan.</summary>
    public bool SendTo(Guest guest, ParkRide ride)
    {
        if (guest == null || !Walk.Guests.Contains(guest) || !Takes(ride) || !Sim.Rides.Contains(ride)) return false;
        if (ride.ServiceEntry is ParkCell entry)
        {
            var terminal=new GuestTerminal(ride,ride.Entrance.Value,entry,()=>Sim.Rides.Contains(ride) && Takes(ride));
            if (!Walk.SendToTerminal(guest,terminal)) return false;
        }
        else if (!Walk.Send(guest, ride.Entrance.Value)) return false;
        _plans[guest.Id] = new Plan(guest.Id, VisitorIntent.Heading, ride.Id, guest.Cell);
        _owners[guest.Id] = ride;
        _returning.Remove(guest.Id);
        _decisions.Forget(guest.Id); // route succeeded; explicit SendTo remains an explicit command
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
        _decisions.Reconcile(_plans.Keys);
        Maintain(deltaSeconds);
    }

    /// <summary>⚠⚠ SOMEBODY HAS TO CLEAN THE LAVATORIES, and shipping the punishment without the
    /// cure made the park unplayable within minutes.
    ///
    /// Every use wears a facility down by up to 26 (`(toilet-60)*2/3`) from a starting 100, and
    /// `VisitorNeeds.DirtyLavatory` then costs the NEXT guest 10 happiness and 10 sickness below
    /// 50. With nothing to undo it that is a **one-way ratchet**: three visits and a lavatory is
    /// filthy forever, every guest afterwards is angry, their happiness grinds to the go-home
    /// floor and the park empties. Master saw exactly that on the first playtest after the
    /// penalty landed -- "they just infinitely use the toilet, get mad, and then leave".
    ///
    /// ⭐ THE CONSOLE HAS A HANDYMAN. `FUN_00130978` -- condition back to 100, plus a timestamp
    /// -- has exactly ONE caller, `0x1456D8`, which is a STAFF MEMBER finishing a clean: it bumps
    /// the staff's own `+0x53`/`+0x54` stats, resets the facility, then clears their goal stack
    /// and activity the same way a guest's ride exit does. Staff are a whole entity this port
    /// does not have.
    ///
    /// ⚠⚠ SO THIS IS A STAND-IN AND IS LABELLED AS ONE. The console's OUTCOME is reproduced
    /// (lavatories get cleaned, so dirt is a recurring cost rather than a death spiral); WHO
    /// cleans them is stubbed until staff exist, and the cadence is invented. ⭐ The alternative
    /// was dropping the dirt penalty entirely, which would have thrown away something that IS
    /// read to avoid modelling something that is not.
    ///
    /// ⚠ Deliberately unconditional, like `FUN_00130978`: it sets 100, it does not top up by a
    /// little. A handyman either visited or did not.</summary>
    public double SecondsPerService { get; set; } = 45;
    double _sinceService;

    /// <summary>⭐⭐ THE PARK GETS PAID. `FUN_001D18E8` is the shop's till and it books a sale in
    /// three places at once:
    ///
    /// <code>
    ///   margin = shop[0xb8] - FUN_001D1B08(shop);        // price - the cost of its goods
    ///   if (margin &lt; 1)  FUN_00100698(park, -margin * 10);          // a LOSS: debit the park
    ///   else             FUN_001007D8(park, kind, margin * 10);      // credit, filed by kind
    ///   shop[0xbc] += price;   shop[0xc0] += margin;
    /// </code>
    ///
    /// ⭐ So `BaseCostOfGoods` is doing double duty and BOTH readings of it are right: it scales
    /// how much a guest wants the thing (<see cref="VisitorNeeds.WantScore"/>) AND it is the
    /// shop's cost per sale. A field named for one job and used for two is exactly the kind of
    /// thing that makes a value-based join look wrong.
    ///
    /// ⭐ The x10 is the same x10 the guest is charged, so the park's money and the guest's are
    /// in the same units -- which is the check that the two halves were read consistently.
    ///
    /// ⚠ THE CATEGORY IS NOT PASSED. The console takes it from a vtable call on the facility and
    /// switches on 4 and 5; nothing read so far says which kind is which number, and filing
    /// income under a guessed heading is worse than filing it under none. The money lands in the
    /// balance either way -- that is <see cref="ParkFinances.Credit"/>'s first act, before the
    /// switch -- so nothing is lost but the breakdown.
    ///
    /// ⚠ A sale whose definition never joined a compiled record has no cost of goods, so its
    /// margin would be the whole price. That is not a decision this can make honestly, so an
    /// unjoined shop books its takings and NO margin, and the park is not paid for it. Better a
    /// visible zero than invented income.</summary>
    void Take(ParkRide shop, RideDefinition def)
    {
        int price = def.PricePerUse ?? 0;
        if (def.Compiled is not { } record) { shop.Book(price, 0); return; }
        // ⭐ The COST is the same expression the want score is built on -- `FUN_001D1B08` serves
        // both -- so the player's two sliders move what a sale costs and what it is worth at once.
        int cost = record.BaseCostOfGoods * ((shop.Quality >> 2) + 75 - (shop.Setting0xAC >> 2)) / 100;
        int margin = price - cost;
        shop.Book(price, margin);
        if (margin < 1) Sim.Finances.Debit(-margin * 10);
        else Sim.Finances.Credit(margin * 10);
    }

    void Maintain(double deltaSeconds)
    {
        if (!AutoService || deltaSeconds <= 0) return;
        _sinceService += deltaSeconds;
        if (_sinceService < SecondsPerService) return;
        _sinceService = 0;
        foreach (var ride in Sim.Rides)
            if (ride.ProvidesRelief && ride.Condition < 100) { ride.Service(); Serviced++; }
    }

    /// <summary>⚠ Off switches the stand-in off, for a check that wants to watch dirt accumulate
    /// -- and for the day staff arrive, when this should be deleted rather than left switched
    /// off. An unused knob reads like a decision.</summary>
    public bool AutoService { get; set; } = true;
    public int Serviced { get; private set; }

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
            if (walking == null && _serviceTerminals.TryGetValue(guest,out var terminal))
            {
                walking=Walk.ReadmitTerminal(guest,terminal);
                _serviceTerminals.Remove(guest);
            }
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
                // 20EDD8 writes G+2C before the kind-specific effect/purchase. This applies
                // to genuine completion even when Buy refuses; aborted removal is not a use.
                // G+6C is a DIFFERENT timer (entertainer watching), not this gate.
                _decisions.Completed(guest, DecisionTick);
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
        _decisions.Forget(guest);
        _serviceTerminals.Remove(guest);
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
            if (ride != null && g.Cell != (ride.ServiceEntry ?? ride.Entrance)) continue;
            if (ride?.ServiceEntry != null && (g.Next != null || g.Progress != 0
                || !ReferenceEquals(g.OccupiedTerminal?.Owner,ride))) continue;
            // ⚠ THE RIDE MAY HAVE GONE, or closed, or broken, while they walked. Then this is not
            // a queue any more and they are just somebody standing in a park.
            if (ride == null || !Takes(ride))
            {
                Wander(g.Id, g.Cell);
                continue;
            }
            ride.Join(g.Id);
            if (g.OccupiedTerminal is { } terminal) _serviceTerminals[g.Id]=terminal;
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
            // ⚠⚠ AND `Leaving` STRANDS THE SAME WAY -- I added the intent and did not extend
            // this, so a guest walking out when the path broke under them stayed stuck even after
            // it was repaired and a route existed again. astraclaw reproduced it. It is the
            // one-way-door shape: nothing is stale, the guest simply left a state with no path
            // back, and every snapshot of them looks individually fine.
            //
            // ⭐ RESETTING THE PLAN IS NOT ENOUGH, which is why the original fix was half a fix:
            // `Wander` rewrites the plan but leaves the WALK state Stranded, and the `!= Arrived`
            // guard below then skips them forever -- so they swapped one stuck state for another.
            // Sending them to the cell they already stand on marks them Arrived (GuestWalk.Send
            // returns early when there is nowhere to go), and the ordinary re-task takes it from
            // there -- including retrying the gate, because WantsToGoHome is still true.
            // ⚠⚠ KEYED ON THE STATE, NOT THE INTENT, and keying it on the intent is what kept
            // this broken through TWO fixes. The original matched `Heading`; I extended it to
            // `Leaving` and the guest STILL never came back, because by then their plan said
            // neither: the first failed attempt had already reset them to Wandering, and a
            // Wandering guest who is NoRoute matched nothing and was skipped forever. The intent
            // says what they were trying to do, which is exactly the thing that has already been
            // lost by the time they are stuck. Being unable to move is a fact about the STATE.
            if (g.State is GuestState.NoRoute or GuestState.Stranded
                && _plans.TryGetValue(g.Id, out var stuck)
                && stuck.Intent is VisitorIntent.Wandering or VisitorIntent.Heading or VisitorIntent.Leaving)
            {
                Wander(g.Id, g.Cell);
                // Standing still on ground that exists again is Arrived, and the ordinary
                // re-task below does the rest. On ground that is still gone this fails and they
                // are tried again next tick, which is the retry the bug was missing.
                Walk.Send(g, g.Cell);
            }
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
            // Stop the proven completion -> Heading -> Queued loop on a zero-time call.
            // Native state-0 arm0 uses strict now > saved + 60 + rand300. This is the
            // post-completion destination gate only, not the full weighted native chooser.
            // Going home above still has priority; no affordability veto is invented here.
            if (!_decisions.CanSelect(g.Id, DecisionTick)) continue;
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
    /// ⚠ `UsageInfo.LitterEffect` IS AUTHORED AND IS STILL NOT USED -- but litter IS now added,
    /// from the read base `DAT_002EEB60` = 30 plus `rand(25)`, inside `VisitorNeeds.Buy`. The
    /// authored 50 is simply not the number that runs, which by now is the rule rather than the
    /// exception. `FatigueEffect` remains authored with no field at all to receive it.
    ///
    /// ⚠⚠ This block previously said litter was not applied anywhere, which stopped being true
    /// the moment the base was read. astraclaw caught it against the body. A comment that has
    /// drifted from its code is worse than no comment, because someone will believe it.</summary>
    void Serve(int guest, ParkRide used)
    {
        if (Needs == null || !Needs.Has(guest)) return;
        if (used != null && used.ProvidesRelief)
        {
            int soil = Needs.UseToilet(guest);
            // ⭐ The console does not collect mess; it WEARS THE FACILITY DOWN by that amount and
            // floors it at zero -- `FUN_00130948`, sole caller the relief path. See ParkRide.Condition.
            used.Wear(soil);
            // ⭐⭐ THEN READS IT BACK, which is the order the console uses: the guest who fouled
            // the place can be the one who walks out disgusted. See VisitorNeeds.DirtyLavatory.
            if (used.Condition < VisitorNeeds.FilthyBelow) Needs.DirtyLavatory(guest);
            Relieved++;
            return;
        }
        if (used?.Definition is { Sells: true } def)
        {
            // ⚠⚠ THE COMPILED HAPPINESS IS A BASE, NOT THE PAYOUT. `0x20E450` scales it by the
            // SHOP'S QUALITY before adding, and quality is not modelled here at all -- so a
            // guest currently receives the unscaled base. astraclaw read the consumer; recorded
            // here rather than in a findings file because this is the line that spends it.
            // ⚠ Only a purchase that HAPPENED counts: Buy refuses when the guest cannot afford
            // it, and a counter that ticked anyway would report a trade that never occurred.
            // ⚠ The COMPILED product picks the arm. A definition with no compiled record falls
            // back to Food, which is what this did before the arms existed -- stated rather than
            // silently defaulted, because a drink shop landing on the food arm raises the thirst
            // it is supposed to quench.
            // ⭐⭐ AND THE COMPILED BASE GOES IN, which is what turns "can they afford it" into
            // "do they want it". ⚠ `?? 0` is not a silent default here -- zero is the documented
            // signal for "this definition never joined a compiled record", and VisitorNeeds.Buy
            // skips the want test rather than refusing everyone. Two facilities per world really
            // do fail to join.
            if (Needs.Buy(guest, def.PricePerUse ?? 0, def.HungerEffect ?? 0, def.ThirstEffect ?? 0,
                          def.HappinessEffect ?? 0, def.VomitEffect ?? 0,
                          def.Compiled?.Product ?? VisitorNeeds.Food,
                          def.Compiled?.BaseCostOfGoods ?? 0, used.Quality, used.Setting0xAC))
            {
                Purchases++;
                Take(used, def);
            }
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
