using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;
using Queues = TPW.PS2.Data.NativeRideQueues;

/// <summary>The native ride queue (findings/native-ride-queue.md): 117340's spots and 210428's waiting
/// arithmetic by hand-computed values, then the controller driving real walkers into a real ride's
/// script. Each walked scenario states the console rule it holds the controller to.</summary>
static class NativeRideQueueChecks
{
    static Point P(int x, int z) => new(checked((short)x), checked((short)z));

    public static void Run(Action<bool, string> check)
    {
        void C(bool ok, string why) => check(ok, "native ride queue: " + why);

        // ---- 117340 ----
        var straight = new NativeQueueShape(new(5, 5), new ParkCell[] { new(6, 5), new(7, 5), new(8, 5) }, 0);
        C(Enumerable.Range(0, 10).All(i => NativeQueueSpots.Spot(straight, i) == P(1408 + 64 * i, 1408)),
            "straight queue: the head stands at the entrance cell's centre (1408,1408) and each guest behind a quarter cell further (1408+64i)");
        C(NativeQueueSpots.Spot(straight, 10) == null,
            "straight queue: the 11th spot would stand on the mouth, so 117340 refuses it (2 in the entrance cell + 4 in each of 2 queue cells)");
        var bent = new NativeQueueShape(new(5, 5), new ParkCell[] { new(5, 6), new(5, 7), new(6, 7), new(7, 7) }, 0);
        var bentWant = Enumerable.Range(0, 9).Select(i => P(1408, 1408 + 64 * i))
            .Concat(Enumerable.Range(1, 5).Select(i => P(1408 + 64 * i, 1920))).ToArray();
        C(Enumerable.Range(0, 14).All(i => NativeQueueSpots.Spot(bent, i) == bentWant[i]),
            "L-shaped queue: the line turns only at the CENTRE of the corner cell (index 8 at (1408,1920), index 9 at (1472,1920))");
        C(NativeQueueSpots.Spot(bent, 14) == null, "L-shaped queue: 14 spots, the 15th would be on the mouth");
        var shortQueue = new NativeQueueShape(new(5, 5), new ParkCell[] { new(6, 5), new(7, 5) }, 0);
        C(NativeQueueSpots.Spot(shortQueue, 5) == P(1728, 1408) && NativeQueueSpots.Spot(shortQueue, 6) == null,
            "a one-cell queue before its mouth holds 6: the physical limit, separate from the head count");
        var westward = new NativeQueueShape(new(8, 5), new ParkCell[] { new(7, 5), new(6, 5) }, 0);
        C(NativeQueueSpots.Spot(westward, 2) == P(2048, 1408) && NativeQueueSpots.Spot(westward, 6) == P(1792, 1408)
            && NativeQueueSpots.Spot(westward, 7) == null,
            "running toward -x the entrance cell holds THREE (offsets 128, 64, 0; x>>8 floors 2048 into cell 8), so the same one-cell queue holds 7, not 6");
        var onEntrance = Enumerable.Range(0, 4).Select(r => new NativeQueueShape(new(5, 5),
            new ParkCell[] { new(5, 5), new(6, 5), new(7, 5) }, r)).ToArray();
        C(Enumerable.Range(0, 7).All(i => onEntrance.All(s => NativeQueueSpots.Spot(s, i) == NativeQueueSpots.Spot(onEntrance[0], i)))
            && NativeQueueSpots.Spot(onEntrance[0], 1) == P(1472, 1408),
            "when the first queue cell is the entrance cell, the rotation's direction is replaced at the first step, so no rotation changes a spot");
        C(NativeQueueSpots.Walk(straight, new(8, 5), new(6, 5)).SequenceEqual(new ParkCell[] { new(8, 5), new(7, 5), new(6, 5) })
            && NativeQueueSpots.Walk(straight, new(6, 5), new(8, 5)).SequenceEqual(new ParkCell[] { new(6, 5), new(7, 5), new(8, 5) })
            && NativeQueueSpots.Walk(bent, new(7, 7), new(5, 5)).SequenceEqual(new ParkCell[] { new(7, 7), new(6, 7), new(5, 7), new(5, 6), new(5, 5) })
            && NativeQueueSpots.Walk(straight, new(9, 9), new(6, 5)).SequenceEqual(new ParkCell[] { new(9, 9), new(6, 5) }),
            "a walk inside the queue follows its cells in either direction; an end off the line goes straight");

        // ---- 210428 ----
        (NativeQueueWaiting.Outcome Outcome, VisitorWants W, uint Deadline, int Facing, List<int> Ranges) Wait(
            byte impatience, byte boredom, uint now, uint phase, bool broken, int taste, int value, uint deadline, params int[] draws)
        {
            var w = new VisitorWants { Unknown78 = impatience, Boredom = boredom };
            var ranges = new List<int>();
            int next = 0;
            var outcome = NativeQueueWaiting.Step(ref w, now, phase, broken, taste, value,
                n => { ranges.Add(n); return next < draws.Length ? draws[next++] : n - 1; }, ref deadline, out int facing);
            return (outcome, w, deadline, facing, ranges);
        }
        var roll = Wait(30, 20, 16, 8, false, 50, 50, 16, 1, 9);
        C(roll.Outcome == NativeQueueWaiting.Outcome.Wait && roll.W.Unknown78 == 31 && roll.W.Boredom == 19
            && roll.Ranges.SequenceEqual(new[] { 75, 100 }),
            "eighth-phase update: rand(100 - 50/2) = rand(75) under 2 adds impatience, rand(100) under 10 relieves boredom");
        var edge = Wait(30, 20, 16, 8, false, 50, 50, 16, 2, 10);
        C(edge.W.Unknown78 == 30 && edge.W.Boredom == 20, "the rolls are strict: rand = 2 and rand = 10 change nothing");
        C(Wait(30, 20, 16, 0, false, 20, 100, 16).Ranges.FirstOrDefault() == 60
            && Wait(30, 20, 16, 0, false, 0, 100, 16).Ranges.FirstOrDefault() == 50,
            "a worse taste match shortens the range: |20-100| = 80 gives rand(60), |0-100| = 100 gives rand(50)");
        C(Wait(30, 20, 17, 8, false, 50, 50, 17).Ranges.Count == 0, "off the guest's eighth phase nothing is drawn");
        var brokenTick = Wait(30, 20, 12, 0, true, 50, 50, 12);
        C(brokenTick.W.Unknown78 == 31 && brokenTick.Ranges.Count == 0
            && Wait(30, 20, 12, 0, false, 50, 50, 12).W.Unknown78 == 30
            && Wait(30, 20, 13, 0, true, 50, 50, 13).W.Unknown78 == 30,
            "a broken ride adds 1 every fourth update on the guest's phase, with no draw");
        var capped = Wait(100, 0, 8, 0, true, 50, 50, 8, 0, 0);
        C(capped.W.Unknown78 == 100 && capped.W.Boredom == 0, "impatience caps at 100 and boredom floors at 0");
        var leave = Wait(80, 20, 8, 0, false, 50, 50, 0, 0, 50);
        C(leave.Outcome == NativeQueueWaiting.Outcome.Leave && leave.W.Unknown78 == 81 && leave.Ranges.SequenceEqual(new[] { 75, 100 })
            && Wait(81, 20, 9, 0, false, 50, 50, 0).Outcome == NativeQueueWaiting.Outcome.Leave
            && Wait(80, 20, 9, 0, false, 50, 50, 9).Outcome == NativeQueueWaiting.Outcome.Wait,
            "81 leaves, 80 stays; the leave comes after the boredom roll and draws no facing");
        var face = Wait(30, 20, 100, 0, false, 50, 50, 99, 37, 3, 5);
        C(face.Outcome == NativeQueueWaiting.Outcome.Faced && face.Deadline == 137 && face.Facing == 3
            && face.Ranges.SequenceEqual(new[] { 300, 4, 10 }),
            "past its deadline a waiting guest draws rand(300) for the next deadline, rand(4) quarter turns, then rand(10)");
        C(Wait(30, 20, 100, 0, false, 50, 50, 100).Ranges.Count == 0, "the deadline test is strict: deadline == now waits");
    }

    /// <summary>The controller over real walkers and a real ride script. The queue is laid straight off
    /// a corridor cell (its mouth) into the grass; the native lease walks it, as the console's planner
    /// walks queue tiles no ordinary route may use.</summary>
    public static void RunWalked(Model terrain, ParkPaths source, IReadOnlyList<ParkCell> corridor, byte[] script,
        Animation aps, int capacity, Func<string, byte[]> sibling, int seats, Action<bool, string> check)
    {
        void C(bool ok, string why) => check(ok, "native ride queue walked: " + why);
        var mouth = corridor.Skip(4).OrderByDescending(c => c.X).ThenBy(c => c.Z).First();
        var spawn = corridor[0];

        Fixture Fresh(int queueCells, int tier, int guests, bool boarding)
        {
            var paths = new ParkPaths(terrain);
            source.Field.Cells.CopyTo(paths.Field.Cells, 0);
            var cells = Enumerable.Range(0, queueCells).Select(i => new ParkCell(mouth.X + queueCells - 1 - i, mouth.Z)).ToArray();
            var shape = new NativeQueueShape(new(mouth.X + queueCells, mouth.Z), cells, 0);
            var sim = new ParkSim(paths);
            var ride = sim.Add(1, "native queue ride", shape.Entrance, 1, 1, script, aps, capacity, cells[0], spawn,
                out var fault, sibling: sibling, headSlots: seats) ?? throw new InvalidOperationException("queue ride did not start: " + fault);
            sim.SetOpen(1, true);
            ride.Set("VAR_BROKEN", 0);
            if (!boarding) ride.Set("VAR_CAPACITY", 0); // VAR_ONRIDE (0) >= VAR_CAPACITY: 1FA528 calls it full
            var walk = new GuestWalk(paths);
            var visitors = new ParkVisitors(sim, walk) { Needs = new VisitorNeeds(seed: 20260925) };
            var rng = new Random(11);
            var f = new Fixture(visitors, ride, shape, spawn);
            f.Queues = new Queues(visitors, new Queues.Services
            {
                Shape = r => ReferenceEquals(r, ride) ? f.Shape : null, Tier = _ => tier,
                Random = n => f.MaxStagger && n == 3 ? 2 : rng.Next(n), // nothing else in a tick draws rand(3)
            });
            visitors.NativeQueueMouth = f.Queues.Mouth;
            visitors.NativeQueueArrival = f.Queues.Arrive;
            walk.BeforeStep = t => { f.Now = t; f.Queues.Tick(t); };
            for (int i = 0; i < guests; i++)
            {
                var g = visitors.Arrive(spawn, spawn);
                if (!visitors.SendTo(g, ride)) throw new InvalidOperationException("queue fixture cannot reach its mouth");
                f.Guests.Add(g);
            }
            return f;
        }

        // Head count and physical limit, with boarding held off.
        int Fill(Fixture f, int ticks)
        {
            int peak = 0;
            for (int i = 0; i < ticks; i++)
            {
                f.Step();
                peak = Math.Max(peak, f.Queues.Members(f.Ride).Count);
            }
            return peak;
        }
        var full = Fresh(4, 0, 14, boarding: false);
        int peak0 = Fill(full, 900);
        var still = full.Queues.Observations;
        C(peak0 == 7 && full.Queues.Refused > 0 && full.Ride.Queue.Count == 0,
            $"20D530: a tier-0 ride's queue stops at 7 (peak {peak0}); later arrivals are refused ({full.Queues.Refused}) and nobody joins the legacy list");
        C(still.Count(o => o.Index >= 0) == 7 && still.Where(o => o.Index >= 0).All(o => o.Step == Queues.Step.Waiting && o.Position == o.Spot),
            "all seven stand still at their own 117340 spots");
        C(still.Where(o => o.Index >= 0).Select(o => o.Position).Distinct().Count() == 7
            && still.Where(o => o.Index >= 0).OrderBy(o => o.Index).Zip(still.Where(o => o.Index >= 0).OrderBy(o => o.Index).Skip(1))
                .All(p => Math.Abs(p.First.Position.X - p.Second.Position.X) + Math.Abs(p.First.Position.Z - p.Second.Position.Z) == 64),
            "neighbours in the line stand exactly a quarter cell apart");
        C(still.Where(o => o.Index >= 0).All(o => full.Visitors.Plans[o.Guest.Id].Intent == VisitorIntent.Queueing
                && full.Visitors.Walk.IsLive(o.Guest)) && full.Visitors.Boardings == 0,
            "queued guests stay on the walk as Queueing, and a full ride (VAR_ONRIDE >= VAR_CAPACITY) boards nobody");
        int peak1 = Fill(Fresh(4, 1, 14, boarding: false), 900);
        C(peak1 == 11, $"teeth: tier 1 raises the head count to 7 + 4 = 11 on the same queue (peak {peak1})");
        var shortLine = Fresh(2, 1, 14, boarding: false);
        int physical = Enumerable.Range(0, 32).TakeWhile(i => NativeQueueSpots.Spot(shortLine.Shape, i) != null).Count();
        int peakShort = Fill(shortLine, 900);
        C(physical < 11 && peakShort == physical,
            $"teeth: a queue one cell long stops at its {physical} physical spots even at tier 1 (peak {peakShort})");

        // Impatience in the middle of the line, and the staggered close-up behind it.
        var line = full.Queues.Observations.Where(o => o.Index >= 0).OrderBy(o => o.Index).ToArray();
        var leaver = line[2].Guest;
        var behind = line.Skip(3).Select(o => o.Guest).ToArray();
        var behindSpots = line.Skip(3).ToDictionary(o => o.Guest, o => NativeQueueSpots.Spot(full.Shape, o.Index - 1).Value,
            ReferenceEqualityComparer.Instance);
        var needs = full.Visitors.Needs;
        var lw = needs.Of(leaver.Id); lw.Unknown78 = 81; needs.Set(leaver.Id, lw);
        full.MaxStagger = true;
        full.Step();
        full.MaxStagger = false;
        var after = full.Queues.Observations;
        var left = after.Single(o => ReferenceEquals(o.Guest, leaver));
        var ripple = behind.Select(g => after.Single(o => ReferenceEquals(o.Guest, g))).ToArray();
        C(left.Step == Queues.Step.Quit && left.Index < 0 && needs.Of(leaver.Id).Thought == Thought.BadQueue
            && full.Queues.Impatient == 1,
            "at 81 a waiting guest leaves the line with thought 9 (BadQueue) and walks out");
        C(ripple.Length == 4 && ripple.All(o => o.Step == Queues.Step.MoveUp)
            && ripple.Select(o => o.Deadline - full.Now).SequenceEqual(new uint[] { 0, 6, 12, 18 }),
            $"everyone behind moves up at now + 3 × a cumulative rand(3), the first with none: with every rand(3) = 2, +{string.Join(",", ripple.Select(o => o.Deadline - full.Now))} (want +0,6,12,18)");
        ParkCell? releasedAt = null;
        for (int i = 0; i < 600 && releasedAt == null; i++)
        {
            full.Step();
            if (full.Visitors.Plans.TryGetValue(leaver.Id, out var plan) && plan.Intent != VisitorIntent.Queueing) releasedAt = leaver.Cell;
        }
        for (int i = 0; i < 300; i++) full.Step();
        var closed = full.Queues.Observations;
        C(releasedAt == mouth && full.Queues.Quits == 1, $"the quitter walks back out and is handed back on the mouth ({releasedAt})");
        C(behind.All(g => closed.Any(o => ReferenceEquals(o.Guest, g) && o.Step == Queues.Step.Waiting && o.Position == behindSpots[g])),
            "each guest behind the leaver now stands on the spot one place further forward");

        // Boarding: only a waiting head, only while VAR_ONRIDE < VAR_CAPACITY and LETMEON is free.
        full.Ride.Set("VAR_CAPACITY", capacity);
        for (int i = 0; i < 2500 && full.Queues.Boarded < 3; i++) full.Step();
        var boardings = full.Queues.Boardings.ToArray();
        var bad = boardings.Where(b => !(b.Step == Queues.Step.Waiting && b.Spot is { } front && b.Position == front
            && b.OnRide < b.Capacity && b.LetMeOn == 0 && b.ScriptQueue == 0
            && !full.Visitors.Walk.IsLive(b.Guest))).ToArray();
        C(boardings.Length >= 3 && bad.Length == 0,
            $"{boardings.Length} boardings, each a WAITING head at the front spot while VAR_ONRIDE < VAR_CAPACITY "
            + $"(seen {string.Join(",", boardings.Select(b => $"{b.OnRide}<{b.Capacity}"))}) and LETMEON was 0 ({bad.Length} otherwise)");
        C(full.Visitors.Boardings >= 3, "a boarded guest leaves the walk into the ride's script queue");

        // A breakdown empties the queue: everyone walks back out and is handed back on the mouth.
        int queued = full.Queues.Members(full.Ride).Count, quitsBefore = full.Queues.Quits;
        full.Ride.Set("VAR_BROKEN", 1);
        full.Step();
        C(queued > 0 && full.Queues.Members(full.Ride).Count == 0
            && full.Queues.Observations.Count(o => o.Step == Queues.Step.Quit) >= queued,
            $"a breakdown sends all {queued} queued guests event 7 at once");
        for (int i = 0; i < 900 && full.Queues.Observations.Count > 0; i++) full.Step();
        C(full.Queues.Observations.Count == 0 && full.Queues.Quits - quitsBefore >= queued
            && !full.Visitors.Plans.Values.Any(p => p.Intent == VisitorIntent.Queueing),
            "they all walk out and are handed back to ordinary visiting");

        // Closing sends no event: the line stays, nobody boards, and a guest who has to move up quits.
        var shut = Fresh(4, 0, 14, boarding: false);
        Fill(shut, 900);
        shut.Visitors.Sim.SetOpen(1, false);
        for (int i = 0; i < 60; i++) shut.Step();
        var waiting = shut.Queues.Observations.Where(o => o.Index >= 0).OrderBy(o => o.Index).ToArray();
        C(waiting.Length == 7 && waiting.All(o => o.Step == Queues.Step.Waiting),
            "closing a ride leaves its seven waiting (no 117798 event on the state-3 path)");
        var sw = shut.Visitors.Needs.Of(waiting[2].Guest.Id); sw.Unknown78 = 81; shut.Visitors.Needs.Set(waiting[2].Guest.Id, sw);
        for (int i = 0; i < 60; i++) shut.Step();
        var rest = shut.Queues.Members(shut.Ride);
        C(rest.Count == 2 && ReferenceEquals(rest[0], waiting[0].Guest) && ReferenceEquals(rest[1], waiting[1].Guest),
            "at a closed ride the guests behind a leaver fail 20D530 when they come to move up, and quit; the two ahead stay");

        // Demolition: event 10, each guest handed back where it stands, at its own cell's centre.
        var gone = Fresh(4, 0, 14, boarding: false);
        Fill(gone, 900);
        var inLine = gone.Queues.Members(gone.Ride).ToArray();
        var stood = gone.Queues.Observations.ToDictionary(o => o.Guest, o => NativeQueueSpots.CellOf(o.Position),
            ReferenceEqualityComparer.Instance);
        gone.Visitors.Sim.Remove(1);
        var handedAt = new Dictionary<Guest, (ParkCell Cell, bool Centre)>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < 300 && handedAt.Count < inLine.Length; i++)
        {
            gone.Step();
            foreach (var g in inLine.Where(g => !handedAt.ContainsKey(g) && gone.Visitors.Plans[g.Id].Intent != VisitorIntent.Queueing))
                handedAt[g] = (g.Cell, !g.HasNativeRoute);
        }
        C(inLine.Length == 7 && handedAt.Count == 7 && gone.Queues.Released == 7
            && inLine.All(g => handedAt[g].Centre && handedAt[g].Cell == stood[g]),
            "demolition (event 10) hands all seven back where they stood, each after reaching its own cell's centre");
        C(gone.Visitors.Walk.NativeRoutes.Available == NativeRoutePool.Capacity && gone.Queues.Observations.Count == 0,
            "every native route slot comes back");
    }

    sealed class Fixture
    {
        internal readonly ParkVisitors Visitors;
        internal readonly ParkRide Ride;
        internal readonly NativeQueueShape Shape;
        internal readonly ParkCell Home;
        internal Queues Queues;
        internal uint Now;
        internal bool MaxStagger;
        internal readonly List<Guest> Guests = new();
        internal Fixture(ParkVisitors visitors, ParkRide ride, NativeQueueShape shape, ParkCell home)
        { Visitors = visitors; Ride = ride; Shape = shape; Home = home; }
        internal void Step() => Visitors.Step(ParkSim.TickMilliseconds / 1000.0, () => Home);
    }
}
