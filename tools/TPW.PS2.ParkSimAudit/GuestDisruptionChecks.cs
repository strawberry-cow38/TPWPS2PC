using System.Security.Cryptography;
using System.Text;
using TPW.PS2.Data;

/// <summary>Repeated managed-port disruptions with real scripts, not retail evacuation/AI policy.</summary>
static class GuestDisruptionChecks
{
    const int Population = 16, Phases = 12;
    sealed record Report(string Digest, string Failure, int FailureAt, int InjectedAt, int Samples, int Replacements,
                         int RecoveringSamples, int SeatedSamples, int Boardings, int Completed, bool Returned);

    static string Census(ParkVisitors visitors, HashSet<int> ids)
    {
        string ownership = GuestConservationChecks.Census(visitors, ids);
        if (ownership != null) return ownership;
        if (!ids.SetEquals(visitors.Needs.All.Keys)) return "needs identity set changed";
        if (ids.Any(id => visitors.Needs.Of(id).Cash != 6000 + id)) return "cash sentinel changed/reseeded";
        if (visitors.Rides < 0 || visitors.Boardings < visitors.Rides) return "completion count exceeds boardings";
        if (visitors.Sim.Rides.Any(ride => ride.Fault != null)) return "a real script faulted";
        return null;
    }

    public static void Run(Model terrain, ParkPaths sourcePaths, byte[] script, Animation animation, int capacity,
                           ParkCell entrance, ParkCell exit, Func<string, byte[]> sibling, int headSlots,
                           Action<bool, string> check)
    {
        void Check(bool ok, string message) => check(ok, "disruption: " + message);
        (ParkPaths Paths, ParkVisitors Visitors, HashSet<int> Ids) Fresh()
        {
            var paths = new ParkPaths(terrain); sourcePaths.Field.Cells.CopyTo(paths.Field.Cells, 0);
            var visitors = new ParkVisitors(new ParkSim(paths), new GuestWalk(paths)) { Needs = new VisitorNeeds(901) };
            foreach (string key in visitors.Needs.Rates.Keys.ToArray()) visitors.Needs.Rates[key] = new VisitorNeeds.Rate(0, 0, false);
            var ids = new HashSet<int>();
            for (int i = 0; i < Population; i++)
            {
                var guest = visitors.Arrive(entrance, entrance); ids.Add(guest.Id);
                var needs = visitors.Needs.Of(guest.Id); needs.Cash = 6000 + guest.Id; visitors.Needs.Set(guest.Id, needs);
            }
            return (paths, visitors, ids);
        }
        ParkRide Add(ParkVisitors visitors, int id, bool open = true)
        {
            var ride = visitors.Sim.Add(id, $"disruption ride {id}", entrance, 1, 1, script, animation,
                capacity, entrance, exit, out string fault, sibling: sibling, headSlots: headSlots)
                ?? throw new InvalidOperationException("Disruption fixture could not start: " + fault);
            visitors.Sim.SetOpen(id, open); ride.Set("VAR_BROKEN", 0);
            return ride;
        }
        var negative = Fresh(); int firstId = negative.Ids.Min();
        var original = negative.Visitors.Needs.Of(firstId); var changed = original; changed.Cash++;
        negative.Visitors.Needs.Set(firstId, changed);
        Check(Census(negative.Visitors, negative.Ids) == "cash sentinel changed/reseeded", "negative control rejects a reseeded/changed cash record");
        negative.Visitors.Needs.Set(firstId, original); negative.Visitors.Needs.Set(99999, original);
        Check(Census(negative.Visitors, negative.Ids) == "needs identity set changed", "negative control rejects an orphan needs record");

        Report Replay(bool alteredFirstStep, string lateCorruption = null)
        {
            var (paths, visitors, ids) = Fresh(); var sim = visitors.Sim;
            byte[] ground = (byte[])paths.Field.Cells.Clone();
            var input = new Random(517);
            double[] deltas = { 0, .01, .03, .04, .08, .32, 2.0 };
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            string failure = null; int samples = 0, replacements = 0, recovery = 0, seated = 0;
            int previousBoardings = 0, previousCompleted = 0;
            bool injectOnNextSample = false; int failureAt = 0, injectedAt = 0;
            void Require(bool ok, string reason)
            {
                if (!ok && failure == null) { failure = $"sample {samples}: {reason}"; failureAt = samples; }
            }
            void Sample()
            {
                samples++;
                Require(visitors.Boardings >= previousBoardings && visitors.Rides >= previousCompleted,
                        "boarding/completion counters went backwards");
                previousBoardings = visitors.Boardings; previousCompleted = visitors.Rides;
                string current = Census(visitors, ids);
                if (failure == null && current != null) { failure = $"sample {samples}: {current}"; failureAt = samples; }
                if (visitors.Plans.Values.Any(p => p.Intent == VisitorIntent.Recovering)) recovery++;
                if (sim.Rides.Any(r => r.Host.Seats.Count > 0)) seated++;
                string needs = string.Join(';', visitors.Needs.All.OrderBy(p => p.Key).Select(p =>
                {
                    var n = p.Value;
                    return $"{p.Key}:{n.Cash}:{n.Happiness}:{n.Sick}:{n.Hunger}:{n.Thirst}:{n.Toilet}:{n.Litter}:{n.Unknown78}:{n.Unknown7B}:{(int)n.Thought}:{n.PreferredIntensity}";
                }));
                // Hash observed state only, never the input seed or action labels.
                hash.AppendData(Encoding.UTF8.GetBytes(GuestConservationChecks.Snapshot(visitors) + "|" + needs + "\n"));
            }
            void Step(double delta)
            {
                visitors.Step(delta, null);
                // Late corruption goes through the normal Sample path, after the
                // coordinator has run; it is not a direct call to the census oracle.
                if (injectOnNextSample)
                {
                    injectOnNextSample = false; injectedAt = samples + 1;
                    int id = ids.Min(); var value = visitors.Needs.Of(id);
                    if (lateCorruption == "cash") { value.Cash++; visitors.Needs.Set(id, value); }
                    else if (lateCorruption == "orphan") visitors.Needs.Set(99999, value);
                }
                Sample();
            }
            Report Result()
            {
                bool returned = visitors.Walk.Guests.Count == Population && ids.SetEquals(visitors.Walk.Guests.Select(g => g.Id))
                    && visitors.Plans.Values.All(p => p.Intent == VisitorIntent.Wandering) && sim.Rides.Count == 0;
                return new(Convert.ToHexString(hash.GetHashAndReset()), failure, failureAt, injectedAt, samples, replacements, recovery, seated,
                           visitors.Boardings, visitors.Rides, returned);
            }
            for (int id = 1; id <= 3; id++) Add(visitors, id);
            foreach (var guest in visitors.Walk.Guests.ToArray())
                Require(visitors.SendTo(guest, sim.Rides[(guest.Id - 1) % 3]), "initial dispatch failed");
            Sample(); Step(alteredFirstStep ? .08 : .04);
            // Establish actual completion, not merely a park full of aborted queues.
            foreach (var ride in sim.Rides) ride.Set("VAR_STARTNOW", 1);
            for (int i = 0; i < 12000 && visitors.Rides == 0; i++) Step(.04);

            for (int phase = 0; phase < Phases; phase++)
            {
                for (int i = 0; i < 80; i++)
                {
                    int id = 1 + input.Next(3); var ride = sim.Rides.Single(r => r.Id == id);
                    if (i == 12) sim.SetOpen(id, false);
                    if (i == 24) ride.Set("VAR_BROKEN", 1);
                    if (i == 36) foreach (var r in sim.Rides) { sim.SetOpen(r.Id, true); r.Set("VAR_BROKEN", 0); r.Set("VAR_STARTNOW", 1); }
                    Step(deltas[input.Next(deltas.Length)]);
                }
                // An occupied removal is a phase-local precondition, not inferred
                // from seats observed somewhere in an earlier cycle.
                foreach (var r in sim.Rides) { sim.SetOpen(r.Id, true); r.Set("VAR_BROKEN", 0); r.Set("VAR_STARTNOW", 1); }
                for (int i = 0; i < 6000 && !sim.Rides.Any(r => r.Host.Seats.Count > 0); i++) Step(.04);
                var occupied = sim.Rides.Where(r => r.Host.Seats.Count > 0).OrderBy(r => r.Id).ToArray();
                Require(occupied.Length > 0, $"phase {phase}: no occupied removal target");
                var victimRide = occupied.Length > 0 ? occupied[input.Next(occupied.Length)] : sim.Rides[0];
                int victim = victimRide.Id;
                var affected = visitors.Plans.Values.Where(p => p.Intent == VisitorIntent.Queued && p.RideId == victim)
                    .Select(p => p.Guest).ToHashSet();
                Require(affected.Count > 0, $"phase {phase}: removed instance owns nobody");
                var reported = new HashSet<int>();
                void CaptureHandbacks()
                {
                    foreach (var r in sim.Rides)
                        foreach (var p in visitors.Plans.Values.Where(p => p.Intent == VisitorIntent.Queued && p.RideId == r.Id))
                            if (r.Left.Contains(p.Guest) || r.Get("VAR_LETMEOFF") == p.Guest) reported.Add(p.Guest);
                }
                CaptureHandbacks(); int completedBeforeDark = visitors.Rides;
                // Synthetic material erasure, not viewer path-tool behavior.
                for (int i = 1; i < paths.Field.Cells.Length; i += 2) paths.Field.Cells[i] = 0;
                paths.SetEntrance(null);
                Require(!paths.Cells.Any(paths.Walkable), $"phase {phase}: supposed blackout still has walkable ground");
                sim.Remove(victim); var replacement = Add(visitors, victim, open: false); replacements++;
                void DarkStep(double delta)
                {
                    CaptureHandbacks(); Step(delta); CaptureHandbacks();
                    Require(affected.All(id => visitors.Plans.TryGetValue(id, out var p)
                            && p.Intent == VisitorIntent.Recovering && p.RideId == 0),
                            $"phase {phase}: removed instance's guests did not retain recovery ownership");
                    Require(!GuestConservationChecks.Bodies(replacement).Overlaps(affected),
                            $"phase {phase}: replacement inherited the removed instance's guests");
                    Require(visitors.Rides == completedBeforeDark, $"phase {phase}: missing-ground readmission counted a completion");
                }
                DarkStep(0); // replacement exists BEFORE reconciliation
                for (int i = 0; i < 16; i++) DarkStep(deltas[input.Next(deltas.Length)]);
                CaptureHandbacks(); sim.Clear(); DarkStep(0);
                Require(visitors.Plans.Values.All(p => p.Intent != VisitorIntent.Queued && p.Intent != VisitorIntent.Heading),
                        $"phase {phase}: Clear retains a ride intent");
                for (int i = 0; i < 16; i++) DarkStep(deltas[input.Next(deltas.Length)]);
                ground.CopyTo(paths.Field.Cells, 0);
                int boardedBeforeRestore = visitors.Boardings;
                injectOnNextSample = phase == Phases / 2 && lateCorruption != null;
                Step(0);
                Require(visitors.Rides == completedBeforeDark + reported.Count,
                        $"phase {phase}: recovery completion count differs from actual script handbacks");
                Require(visitors.Boardings == boardedBeforeRestore,
                        $"phase {phase}: restoration invented a boarding");
                Require(visitors.Walk.Guests.Count == Population && ids.SetEquals(visitors.Walk.Guests.Select(g => g.Id))
                        && visitors.Plans.Values.All(p => p.Intent == VisitorIntent.Wandering),
                        $"phase {phase}: restoration did not recover all sixteen identities");
                if (phase == Phases / 2 && lateCorruption != null) return Result();
                // Resend physical walkers after topology restoration; do not invent
                // automatic guest recovery from an externally restored fixture grid.
                foreach (var guest in visitors.Walk.Guests.ToArray())
                    Require(visitors.Walk.Send(guest, entrance), $"phase {phase}: restored walker cannot route to entrance");
                for (int i = 0; i < 6000 && visitors.Walk.Guests.Any(g => g.State != GuestState.Arrived); i++) Step(.04);
                Require(visitors.Walk.Guests.All(g => g.State == GuestState.Arrived && g.Next == null),
                        $"phase {phase}: restored walkers did not finish their return route");
                if (phase + 1 < Phases)
                {
                    for (int id = 1; id <= 3; id++) Add(visitors, id);
                    foreach (var guest in visitors.Walk.Guests.ToArray())
                        Require(visitors.SendTo(guest, sim.Rides[input.Next(3)]), $"phase {phase}: next dispatch failed");
                }
            }
            return Result();
        }
        var reports = new[] { Replay(false), Replay(false), Replay(true) };
        for (int i = 0; i < reports.Length; i++)
        {
            var report = reports[i];
            Check(report.Failure == null, $"per-step identity/needs census run {i} ({report.Samples} samples): {report.Failure ?? "conserved"}");
            Check(report.Replacements == Phases, $"run {i} executes twelve same-ID replacement/Clear cycles");
            Check(report.RecoveringSamples >= Phases * 16, $"run {i} retains recovery ownership through sustained missing ground ({report.RecoveringSamples} samples)");
            Check(report.SeatedSamples > 0 && report.Completed > 0 && report.Boardings >= Population * 2,
                  $"run {i} exercises real seats and repeated boarding/completion ({report.Boardings}/{report.Completed})");
            Check(report.Returned, $"run {i} ends with all sixteen original identities walking exactly once");
        }
        var cashControl = Replay(false, "cash"); var orphanControl = Replay(false, "orphan");
        Check(cashControl.Replacements == 7 && cashControl.InjectedAt > 1000 && cashControl.FailureAt == cashControl.InjectedAt
              && cashControl.Failure?.Contains("cash sentinel changed/reseeded") == true,
              "late-run negative control catches changed cash through ordinary per-step sampling");
        Check(orphanControl.Replacements == 7 && orphanControl.InjectedAt > 1000 && orphanControl.FailureAt == orphanControl.InjectedAt
              && orphanControl.Failure?.Contains("needs identity set changed") == true,
              "late-run negative control catches orphan needs through ordinary per-step sampling");
        Check(reports[0] == reports[1], $"identical disruption inputs replay the entire observed ledger (SHA256 {reports[0].Digest})");
        Check(reports[0].Digest != reports[2].Digest, "changed simulation input changes the observed-state replay digest");
    }
}
