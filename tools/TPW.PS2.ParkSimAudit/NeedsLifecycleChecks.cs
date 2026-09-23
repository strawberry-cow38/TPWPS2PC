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
            var visitors = new ParkVisitors(sim, new GuestWalk(paths)) { Needs = new VisitorNeeds(123) };
            // Freeze CHOSEN rates to isolate storage continuity from arithmetic.
            foreach (string name in visitors.Needs.Rates.Keys.ToArray())
                visitors.Needs.Rates[name] = new VisitorNeeds.Rate(0, 0, false);
            var guest = visitors.Arrive(entrance, entrance);
            visitors.Needs.Set(guest.Id, new VisitorWants
            {
                Happiness = 83, Sick = 71, Hunger = 94, Toilet = 88, Thirst = 96,
                Litter = 61, Unknown78 = 62, Unknown7B = 63, Cash = 1234, Thought = Thought.Good,
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

        var queued = Fresh(); var original = queued.Visitors.Needs.Of(queued.Guest.Id);
        Queue(queued.Visitors, queued.Ride, queued.Guest);
        Check(Same(original, queued.Visitors.Needs.Of(queued.Guest.Id)), "boarding preserves every stored need field");
        queued.Sim.Remove(1); queued.Visitors.Step(0, null);
        Check(queued.Visitors.Walk.Guests.Count == 1
              && !ReferenceEquals(queued.Guest, queued.Visitors.Walk.Guests[0])
              && Same(original, queued.Visitors.Needs.Of(queued.Guest.Id)),
              "removal readmits a new walking object without respawning its needs");

        var seated = Fresh(); original = seated.Visitors.Needs.Of(seated.Guest.Id);
        Queue(seated.Visitors, seated.Ride, seated.Guest);
        for (int i = 0; i < 6000 && !seated.Ride.Host.Seats.Values.Contains(seated.Guest.Id); i++) seated.Visitors.Step(.04, null);
        Check(seated.Ride.Host.Seats.Values.Contains(seated.Guest.Id), "real script actually seats the needs-bearing guest");
        Check(Same(original, seated.Visitors.Needs.Of(seated.Guest.Id)), "seated guest retains its entire side-table state");
        seated.Sim.Remove(1); seated.Visitors.Step(0, null);
        Check(Same(original, seated.Visitors.Needs.Of(seated.Guest.Id)), "seated removal retains needs");

        var returning = Fresh(); original = returning.Visitors.Needs.Of(returning.Guest.Id);
        Queue(returning.Visitors, returning.Ride, returning.Guest); returning.Ride.Set("VAR_STARTNOW", 1);
        for (int i = 0; i < 12000 && !returning.Ride.Left.Contains(returning.Guest.Id); i++) returning.Sim.Advance(.04);
        Check(returning.Ride.Left.Contains(returning.Guest.Id), "real script reports normal completion");
        returning.Sim.SetOpen(1, false); returning.Visitors.Step(0, null);
        Check(returning.Visitors.Rides == 1 && Same(original, returning.Visitors.Needs.Of(returning.Guest.Id)),
              "normal completion/readmission does not reseed needs");

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
        Check(recovery.Visitors.Walk.Guests.Count == 1 && Same(original, recovery.Visitors.Needs.Of(recovery.Guest.Id)),
              "delayed recovery preserves needs until readmission succeeds");

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
        clock.Visitors.Needs.Set(clock.Guest.Id, original with { Hunger = 10 });
        clock.Visitors.Step(0, null);
        Check(clock.Visitors.Needs.Of(clock.Guest.Id).Hunger == 10, "zero elapsed time does not age needs");

        (long Walk, long Park, byte Hunger) AfterFrames(int frames, double delta)
        {
            var f = Fresh(); f.Sim.SetOpen(1, false);
            f.Visitors.Needs.Rates["hunger"] = new VisitorNeeds.Rate(1, 0, false);
            f.Visitors.Needs.SecondsPerRise = .25; // controlled test cadence, not retail rate evidence
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
