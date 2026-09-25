using TPW.PS2.Data;

/// <summary>Independent identity/clock integration checks; no claim about retail rise rates.</summary>
static class NeedsLifecycleChecks
{
    public static void Run(Model terrain, ParkPaths sourcePaths, byte[] script, Animation animation, int capacity,
                           ParkCell entrance, ParkCell exit, Func<string, byte[]> sibling, int headSlots,
                           Action<bool, string> check)
    {
        void Check(bool ok, string message) => check(ok, "needs lifecycle: " + message);
        (ParkPaths Paths, ParkSim Sim, ParkVisitors Visitors, ParkRide Ride, Guest Guest) Fresh()
        {
            var paths = new ParkPaths(terrain); sourcePaths.Field.Cells.CopyTo(paths.Field.Cells, 0);
            var sim = new ParkSim(paths);
            var ride = sim.Add(1, "needs regression", entrance, 1, 1, script, animation, capacity,
                entrance, exit, out _, sibling: sibling, headSlots: headSlots)
                ?? throw new InvalidOperationException("Needs fixture script did not start");
            sim.SetOpen(1, true); ride.Set("VAR_BROKEN", 0);
            var visitors = new ParkVisitors(sim, new GuestWalk(paths))
            {
                // Above the original consumer's56 sickness gate; keep the exactly-once
                // arithmetic observable, not a passing zero-effect comparison.
                Needs = new VisitorNeeds(123), RideIntensity = 60, RideHappiness = 7,
                RideSickScale = .5f, RideBoredomScale = .5f,
            };
            // Freeze CHOSEN rates to isolate storage continuity from arithmetic.
            foreach (string name in visitors.Needs.Rates.Keys.ToArray())
                visitors.Needs.Rates[name] = new VisitorNeeds.Rate(0, 0, false);
            // ⚠⚠ AND THE MOOD BARS OUT OF REACH, which freezing the rates does NOT cover. This
            // fixture's guest carries Thirst 96, and the day a THIRST bar appeared at 85 the
            // continuity cases started failing on a happiness drain working perfectly -- in
            // checks about whether a seated guest keeps its side-table row. ⭐ A fixture that
            // pins a need HIGH is a fixture that will one day cross a bar somebody adds later.
            visitors.Needs.Unknown78Bar = visitors.Needs.SickBar = visitors.Needs.ToiletBar =
                visitors.Needs.HungerBar = visitors.Needs.ThirstBar = 101;
            // ⚠ AND NO BUBBLE, for the same reason one step further on. These cases compare the
            // WHOLE stored struct across a boarding or a removal, and `Thought` is DERIVED -- the
            // mood ladder rewrites it every 128 ticks, correctly. A budget of zero stops it being
            // taken at all, so the comparison stays about the fields that really must survive.
            // ⭐ Not the same as excluding `Thought` from the comparison: if some other path ever
            // starts writing it, this fixture will still notice.
            visitors.Needs.BubbleBudget = 0;
            var guest = visitors.Arrive(entrance, entrance);
            visitors.Needs.Set(guest.Id, new VisitorWants
            {
                Happiness = 83, Sick = 61, Hunger = 94, Toilet = 88, Thirst = 96,
                Litter = 61, Unknown78 = 62, Boredom = 63, Cash = 1234, Thought = Thought.Good,
                PreferredIntensity = 0, // explicitly exercise the configurable fallback below
            });
            return (paths, sim, visitors, ride, guest);
        }
        void Queue(ParkVisitors visitors, ParkRide ride, Guest guest)
        {
            Check(visitors.SendTo(guest, ride), "fixture guest can reach ride");
            visitors.Step(0, null);
            Check(!visitors.Walk.Guests.Any(g => g.Id == guest.Id)
                  && visitors.Plans[guest.Id].Intent == VisitorIntent.Queued, "fixture transfers guest to ride ownership");
        }
        bool Same(VisitorWants a, VisitorWants b) => a.Equals(b);

        // ⭐⭐ AFTER A REMOVAL THE NEEDS SURVIVE AND THE THOUGHT IS RE-DECIDED. That is not the
        // fixture rotting -- it is the fixture doing its job. Its own note above says a budget of
        // zero stops the mood LADDER taking a bubble but that "if some other path ever starts
        // writing `Thought`, this fixture will still notice", and one did: `Decide` was given its
        // real call site at `SelectDestination` (c35aaa4), and a readmitted guest walks straight
        // into it. Hunger 94 and Thirst 96 then read as HungryAndThirsty, correctly.
        //
        // ⚠ So the comparison is narrowed to the fields that must SURVIVE, and the thought is
        // asserted OUTRIGHT rather than excluded -- excluding it would let a path that scrambles
        // the thought pass unnoticed, which is exactly what the original note was guarding.
        bool SameNeeds(VisitorWants a, VisitorWants b) => Same(a with { Thought = b.Thought }, b);
        void CheckReDecided(VisitorWants before, VisitorWants after, string what)
        {
            Check(SameNeeds(before, after), what + ": every stored need survives");
            Check(after.Thought == Thought.HungryAndThirsty,
                  what + $": and the thought is re-decided from Hunger {before.Hunger} / Thirst "
                       + $"{before.Thirst} (got {after.Thought})");
        }
        bool Readmitted(ParkVisitors visitors, int id) => visitors.Walk.Guests.Count(g => g.Id == id) == 1
            && visitors.Plans.TryGetValue(id, out var plan) && plan.Intent == VisitorIntent.Wandering && plan.RideId == 0;
        bool HasOneRideEffect(VisitorWants before, VisitorWants after, ParkVisitors visitors)
        {
            static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 100);
            var expected = before with
            {
                Happiness = Clamp(before.Happiness + visitors.RideHappiness),
                Sick = Clamp(before.Sick + (int)(visitors.RideSickScale * (visitors.RideIntensity - 30))),
                Unknown78 = Clamp(before.Unknown78 - (int)(visitors.RideBoredomScale * visitors.RideIntensity)),
                Boredom = after.Boredom, // the specified rand(20) is bounded, not pinned
            };
            return Same(expected, after) && after.Boredom >= Math.Max(0, before.Boredom - 19)
                && after.Boredom <= before.Boredom;
        }


        var queued = Fresh(); var original = queued.Visitors.Needs.Of(queued.Guest.Id);
        Queue(queued.Visitors, queued.Ride, queued.Guest);
        Check(Same(original, queued.Visitors.Needs.Of(queued.Guest.Id)), "boarding preserves every stored need field");
        queued.Sim.Remove(1); queued.Visitors.Step(0, null);
        Check(queued.Visitors.Walk.Guests.Count == 1
              && !ReferenceEquals(queued.Guest, queued.Visitors.Walk.Guests[0]),
              "removal readmits a new walking object");
        CheckReDecided(original, queued.Visitors.Needs.Of(queued.Guest.Id), "removal readmission");

        // ⚠⚠ THE CONTROL, and without it the thought assertion above is a rubber stamp: a guest
        // who is NOT hungry or thirsty must NOT come back HungryAndThirsty. If Decide were
        // writing a constant, or the field were simply being left alone at some default, the
        // check above would pass and this one would not.
        var fed = Fresh();
        fed.Visitors.Needs.Set(fed.Guest.Id, fed.Visitors.Needs.Of(fed.Guest.Id)
                               with { Hunger = 2, Thirst = 2, Thought = Thought.Good });
        Queue(fed.Visitors, fed.Ride, fed.Guest);
        fed.Sim.Remove(1); fed.Visitors.Step(0, null);
        Check(fed.Visitors.Needs.Of(fed.Guest.Id).Thought != Thought.HungryAndThirsty,
              "control: a fed and watered guest is NOT re-decided as hungry and thirsty "
            + $"(got {fed.Visitors.Needs.Of(fed.Guest.Id).Thought})");

        var seated = Fresh(); original = seated.Visitors.Needs.Of(seated.Guest.Id);
        Queue(seated.Visitors, seated.Ride, seated.Guest);
        for (int i = 0; i < 6000 && !seated.Ride.Host.Seats.Values.Contains(seated.Guest.Id); i++) seated.Visitors.Step(.04, null);
        Check(seated.Ride.Host.Seats.Values.Contains(seated.Guest.Id), "real script actually seats the needs-bearing guest");
        Check(Same(original, seated.Visitors.Needs.Of(seated.Guest.Id)), "seated guest retains its entire side-table state");
        seated.Sim.Remove(1); seated.Visitors.Step(0, null);
        CheckReDecided(original, seated.Visitors.Needs.Of(seated.Guest.Id), "seated removal");

        var returning = Fresh(); original = returning.Visitors.Needs.Of(returning.Guest.Id);
        Queue(returning.Visitors, returning.Ride, returning.Guest); returning.Ride.Set("VAR_STARTNOW", 1);
        for (int i = 0; i < 12000 && !returning.Ride.Left.Contains(returning.Guest.Id); i++) returning.Sim.Advance(.04);
        Check(returning.Ride.Left.Contains(returning.Guest.Id), "real script reports normal completion");
        returning.Sim.SetOpen(1, false); returning.Visitors.Step(0, null);
        var afterRide = returning.Visitors.Needs.Of(returning.Guest.Id);
        Check(returning.Visitors.Rides == 1 && Readmitted(returning.Visitors, returning.Guest.Id)
              && HasOneRideEffect(original, afterRide, returning.Visitors),
              "normal completion applies the configured effect once without reseeding unaffected fields");
        Check(afterRide.Cash == 1234 && afterRide.Happiness == 90 && afterRide.Sick == 76 && afterRide.Unknown78 == 32,
              "non-clamping completion sentinels distinguish one effect from a double or fresh spawn");
        returning.Visitors.Step(0, null);
        Check(returning.Visitors.Rides == 1 && Same(afterRide, returning.Visitors.Needs.Of(returning.Guest.Id)),
              "subsequent steps neither repeat completion effects nor reroll their random reduction");

        // A real completed/readmitted guest with an explicit nonzero preference exercises
        // the banded consumer rather than the intentionally unspecified fallback fixture.
        var tasted = Fresh();
        var tasteBefore = tasted.Visitors.Needs.Of(tasted.Guest.Id); tasteBefore.PreferredIntensity = 90;
        tasted.Visitors.Needs.Set(tasted.Guest.Id, tasteBefore);
        Queue(tasted.Visitors, tasted.Ride, tasted.Guest);
        for (int i = 0; i < 6000 && !tasted.Ride.Left.Contains(tasted.Guest.Id); i++) tasted.Sim.Advance(.04);
        Check(tasted.Ride.Left.Contains(tasted.Guest.Id), "preference fixture receives a genuine script completion");
        tasted.Sim.SetOpen(1, false); tasted.Visitors.Step(0, null);
        var tasteAfter = tasted.Visitors.Needs.Of(tasted.Guest.Id);
        Check(tasted.Visitors.Rides == 1 && Readmitted(tasted.Visitors, tasted.Guest.Id)
              && tasteAfter.PreferredIntensity == 90 && tasteAfter.Happiness == 93
              && tasteAfter.Sick == 76 && tasteAfter.Unknown78 == 32 && tasteAfter.Cash == 1234,
              "completion preserves nonzero preference and applies its middle band instead of fallback7");
        tasted.Visitors.Step(0, null);
        Check(tasted.Visitors.Rides == 1 && Same(tasteAfter, tasted.Visitors.Needs.Of(tasted.Guest.Id)),
              "preference-dependent completion is not repeated or reseeded on later steps");

        foreach (bool mailbox in new[] { true, false })
        {
            var finished = Fresh(); var before = finished.Visitors.Needs.Of(finished.Guest.Id);
            Queue(finished.Visitors, finished.Ride, finished.Guest); finished.Ride.Set("VAR_STARTNOW", 1);
            bool Reported() => mailbox ? finished.Ride.Get("VAR_LETMEOFF") == finished.Guest.Id
                                     : finished.Ride.Left.Contains(finished.Guest.Id);
            for (int i = 0; i < 12000 && !Reported(); i++) finished.Sim.Advance(.04);
            Check(Reported(), "completion-removal fixture observes " + (mailbox ? "exit mailbox" : "collected handback"));
            finished.Sim.Remove(1); finished.Visitors.Step(0, null);
            var after = finished.Visitors.Needs.Of(finished.Guest.Id);
            Check(finished.Visitors.Rides == 1 && Readmitted(finished.Visitors, finished.Guest.Id)
                  && HasOneRideEffect(before, after, finished.Visitors),
                  "already-reported completion retains its one effect when the ride is removed");
            finished.Visitors.Step(0, null);
            Check(Same(after, finished.Visitors.Needs.Of(finished.Guest.Id)) && finished.Visitors.Rides == 1,
                  "removed completed ride cannot apply its effect again");
        }

        var delayed = Fresh(); var beforeDelay = delayed.Visitors.Needs.Of(delayed.Guest.Id);
        int restoredMaterial = delayed.Paths.Field.Material(exit.X, exit.Z);
        Queue(delayed.Visitors, delayed.Ride, delayed.Guest); delayed.Ride.Set("VAR_STARTNOW", 1);
        for (int i = 0; i < 12000 && !delayed.Ride.Left.Contains(delayed.Guest.Id); i++) delayed.Sim.Advance(.04);
        Check(delayed.Ride.Left.Contains(delayed.Guest.Id), "delayed-effect fixture has a genuine script handback");
        delayed.Sim.SetOpen(1, false);
        for (int i = 1; i < delayed.Paths.Field.Cells.Length; i += 2) delayed.Paths.Field.Cells[i] = 0;
        delayed.Paths.SetEntrance(null); delayed.Visitors.Step(0, null);
        Check(delayed.Visitors.Plans[delayed.Guest.Id].Intent == VisitorIntent.Recovering && delayed.Visitors.Rides == 0
              && !delayed.Visitors.Walk.Guests.Any(g => g.Id == delayed.Guest.Id) && Same(beforeDelay, delayed.Visitors.Needs.Of(delayed.Guest.Id)),
              "completed guest awaiting ground keeps its pending effect unapplied until readmission");
        delayed.Paths.Lay(exit, restoredMaterial); delayed.Visitors.Step(0, null);
        var afterDelay = delayed.Visitors.Needs.Of(delayed.Guest.Id);
        Check(delayed.Visitors.Rides == 1 && Readmitted(delayed.Visitors, delayed.Guest.Id)
              && HasOneRideEffect(beforeDelay, afterDelay, delayed.Visitors),
              "successful delayed readmission applies the preserved completion effect");
        delayed.Visitors.Step(0, null);
        Check(Same(afterDelay, delayed.Visitors.Needs.Of(delayed.Guest.Id)) && delayed.Visitors.Rides == 1,
              "delayed completion effect is consumed exactly once");

        var recovery = Fresh(); original = recovery.Visitors.Needs.Of(recovery.Guest.Id);
        int material = recovery.Paths.Field.Material(entrance.X, entrance.Z);
        Queue(recovery.Visitors, recovery.Ride, recovery.Guest);
        for (int i = 1; i < recovery.Paths.Field.Cells.Length; i += 2) recovery.Paths.Field.Cells[i] = 0;
        recovery.Paths.SetEntrance(null); recovery.Sim.Remove(1);
        for (int i = 0; i < 3; i++) recovery.Visitors.Step(.04, null);
        Check(recovery.Visitors.Plans[recovery.Guest.Id].Intent == VisitorIntent.Recovering
              && recovery.Visitors.Needs.Has(recovery.Guest.Id)
              && Same(original, recovery.Visitors.Needs.Of(recovery.Guest.Id)),
              "temporary absence of ground/body does not retire or reseed needs");
        recovery.Paths.Lay(entrance, material); recovery.Visitors.Step(0, null);
        Check(recovery.Visitors.Walk.Guests.Count == 1, "delayed recovery readmits once the ground is back");
        // ⚠ The readmission is the moment the guest reaches SelectDestination, so this is the
        // same re-decide as an ordinary removal -- the check above it, which asserts the WHOLE
        // struct, is about the window BEFORE readmission, where nothing may touch anything.
        CheckReDecided(original, recovery.Visitors.Needs.Of(recovery.Guest.Id), "delayed recovery");

        var retired = Fresh();
        Check(retired.Visitors.SendTo(retired.Guest, retired.Ride), "retirement fixture has a heading owner");
        retired.Visitors.Walk.Remove(retired.Guest.Id); retired.Sim.Remove(1); retired.Visitors.Step(0, null);
        Check(!retired.Visitors.Plans.ContainsKey(retired.Guest.Id) && !retired.Visitors.Needs.Has(retired.Guest.Id),
              "retirement removes both plan and needs record");
        retired.Visitors.Walk.Clear(); // reset walk IDs only after its sole guest has retired
        var reused = retired.Visitors.Arrive(entrance, entrance);
        var spawned = retired.Visitors.Needs.Of(reused.Id);
        Check(reused.Id == retired.Guest.Id && spawned.Happiness == 50 && spawned.Hunger < 70 && spawned.Cash >= 2000,
              "fresh arrival with a reused ID receives spawn state, not the retired guest's wants");
        // Arrive must overwrite even a stale record: reconciliation alone is not
        // enough if a new identity arrives before another coordinator step.
        retired.Visitors.Walk.Clear(); retired.Visitors.Needs.Set(reused.Id, original);
        var immediate = retired.Visitors.Arrive(entrance, entrance);
        Check(immediate.Id == reused.Id && retired.Visitors.Needs.Of(immediate.Id).Happiness == 50,
              "arrival overwrites stale needs before the next reconcile step");

        var clock = Fresh(); clock.Sim.SetOpen(1, false);
        clock.Visitors.Needs.Rates["hunger"] = new VisitorNeeds.Rate(1, 0, false);
        clock.Visitors.Needs.SecondsPerTick = .25 / VisitorNeeds.HungerTicks;
        clock.Visitors.Needs.Set(clock.Guest.Id, original with { Hunger = 10 });
        clock.Visitors.Step(0, null);
        Check(clock.Visitors.Needs.Of(clock.Guest.Id).Hunger == 10, "zero elapsed time does not age needs");

        (long Walk, long Park, byte Hunger) AfterFrames(int frames, double delta)
        {
            var f = Fresh(); f.Sim.SetOpen(1, false);
            f.Visitors.Needs.Rates["hunger"] = new VisitorNeeds.Rate(1, 0, false);
            // ⭐ HUNGER HAS ITS OWN CLOCK NOW, and these cases use hunger as the INSTRUMENT for a
            // property that is not about hunger at all -- frame-rate independence, park time vs
            // discarded frame time, the catch-up ceiling. So drive the clock that actually moves
            // it, sized so one hunger fire still lands every .25 s and every expected value below
            // is unchanged. ⚠ `HungerTicks` stays `const` on purpose: it is read from the image,
            // and a test that could edit it would be able to hide a wrong port of it.
            f.Visitors.Needs.SecondsPerRise = .25; // controlled test cadence, not retail rate evidence
            f.Visitors.Needs.SecondsPerTick = .25 / VisitorNeeds.HungerTicks;
            f.Visitors.Needs.Set(f.Guest.Id, original with { Hunger = 10 });
            for (int i = 0; i < frames; i++) f.Visitors.Step(delta, null);
            return (f.Visitors.Walk.Time, f.Sim.Time, f.Visitors.Needs.Of(f.Guest.Id).Hunger);
        }
        // 1.2 seconds is safely away from a .25-second period boundary, so the
        // check neither tolerates a missing rise nor trips on boundary rounding.
        var slow = AfterFrames(30, .04); var fast = AfterFrames(60, .02);
        Check(slow.Hunger == 14, "clock control actually applies four rises rather than passing with no updates");
        Check(slow == fast, $"equal simulated time has frame-rate-independent need updates (25Hz={slow.Hunger}, 50Hz={fast.Hunger})");
        var stalled = AfterFrames(1, 10); var matched = AfterFrames(8, .04);
        Check(stalled.Walk == 320 && stalled.Park == 320, "stall fixture actually exercises the eight-tick catch-up ceiling");
        Check(stalled == matched && stalled.Hunger == 11,
              $"needs ages by consumed park time, not discarded frame time (stalled={stalled.Hunger}, normal={matched.Hunger})");
        Check(AfterFrames(253, .04).Hunger == 50,
              "ordinary longer running still ages needs rather than globally capping total progress");
    }
}
