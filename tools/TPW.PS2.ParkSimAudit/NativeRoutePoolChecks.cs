using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;
using Flow = TPW.PS2.Data.NativeEntranceFlow;
using Assignment = TPW.PS2.Data.GuestWalk.NativeAssignment;

/// <summary>Shared output slots and their real walking/controller consumers.
/// The explicit BFS output transform is not a native pathfinder or search-pool test.</summary>
static class NativeRoutePoolChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string why) => check(ok, "native route pool: " + why);
        PoolChecks(Check);
        OutputChecks(Check);

        // Same public authored-corridor fixture as NativeWalkConsumerChecks; no private fixture access.
        var archive = disc.Files().Single(f => f.Path.Equals("/DATA/JUNGLE.WAD", StringComparison.OrdinalIgnoreCase));
        var wad = new WadArchive(disc.Read(archive.Extent, archive.Size));
        var terrain = new Model(wad.Read(wad.Find("/terrain/terrain_1.mps")));
        var executable = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        var entrance = ParkEntrance.ReadExecutable(disc.Read(executable.Extent, executable.Size));
        var entry = entrance.Fit(terrain.Field, ParkEntrance.WalkwayColumnFromPoles(terrain), out _);
        var start = new ParkCell(entry.XCol, entry.ZEnd - 2);
        ParkPaths Ground()
        {
            var paths = new ParkPaths(terrain);
            paths.SetEntrance(entrance);
            return paths;
        }
        Check(!entry.Empty && Ground().Open(start) && Ground().Open(start.Offset(0, -1)),
            "authored corridor fixture is open (not an inferred native queue location)");
        ConsumerChecks(new GuestWalk(Ground()), start, Check);
        ControllerChecks(() => new Fixture(Ground(), start), Check);
    }

    static Point Centre(ParkCell c) => new(checked((short)(c.X * 256 + 128)), checked((short)(c.Z * 256 + 128)));
    static Point Packed(Point p) => NativeGuestMotion.DecodeTarget(NativeGuestMotion.EncodeTarget(0, p));
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    // An explicit competing resource owner, not a detached availability counter.
    static List<int> Fill(NativeRoutePool pool)
    {
        var owned = new List<int>();
        for (int i = 0; i < NativeRoutePool.Capacity; i++)
        {
            int slot = pool.Allocate();
            if (slot < 0) break;
            owned.Add(slot);
        }
        return owned;
    }

    static void PoolChecks(Action<bool, string> check)
    {
        var p = new NativeRoutePool();
        check(NativeRoutePool.Capacity == 1000 && p.Available == 1000 && p.AllocatedCount == 0 && p.Hint == 0,
            "literal capacity1000, initial hint and counts");
        for (int i = 0; i < 1000; i++)
        {
            int slot = p.Allocate();
            check(slot == i && p.IsAllocated(slot) && p.Next(slot) == -1
                && (p.ReadWord(slot) & 0x87ffu) == 0x87ffu
                && p.Available == 999 - i && p.AllocatedCount == i + 1 && p.Hint == i + 1,
                $"allocation {i + 1}/1000: handle, bit15, terminal link, count and forward hint");
        }
        check(p.Allocate() == -1 && p.Allocate() == -1 && p.Available == 0
            && p.AllocatedCount == 1000 && p.Hint == 1000, "repeated exhaustion changes neither count nor hint");
        p.FreeOne(731); p.FreeOne(12);
        check(p.Hint == 12 && p.Available == 2 && !p.IsAllocated(12) && !p.IsAllocated(731),
            "free lowers hint to earliest hole and clears only ownership");
        check(p.Allocate() == 12 && p.Hint == 13 && p.Allocate() == 731 && p.Hint == 732
            && p.Available == 0, "forward scan reuses freed slots, skipping still-live slots");
        check(p.Allocate() == -1 && p.Hint == 732 && p.Available == 0,
            "failed scan with hint below capacity does not move hint");
        p.Reset();
        int a = p.Allocate(), b = p.Allocate();
        p.SetNext(a, b);
        var target = new Point(-79, 431);
        uint linked = p.ReadWord(a);
        p.SetTarget(a, target);
        check((p.ReadWord(a) & 0x87ffu) == (linked & 0x87ffu) && p.Target(a) == Packed(target)
            && p.Next(a) == b, "target codec preserves allocation bit and live link");
        uint encoded = p.ReadWord(a);
        p.SetNext(a, -1);
        check((p.ReadWord(a) & ~0x7ffu) == (encoded & ~0x7ffu) && p.Next(a) == -1,
            "link write preserves bit15 and target bits");
        p.SetNext(a, b);
        ulong epoch = p.ResetGeneration;
        p.Reset();
        check(p.ResetGeneration != epoch && p.Hint == 0 && p.Available == 1000
            && !p.IsAllocated(a) && !p.IsAllocated(b), "reset clears allocation bits and invalidates handles");
        check(Throws<InvalidOperationException>(() => p.ReadWord(a)), "managed reset handle read is refused");
        check(p.Allocate() == a && p.Target(a) == Packed(target) && p.Next(a) == -1,
            "reset preserves encoded target; reallocation restores terminal link");
        // Public reads require allocation; reset's retained low11 bits cannot be read before Allocate overwrites them.

        p.Reset();
        var points = new[] { new Point(128, 128), new Point(192, 128), new Point(192, 256) };
        check(p.TryBuild(points, out int head) && head == 2 && p.Next(head) == 1 && p.Next(1) == 0 && p.Next(0) == -1
            && p.Target(2) == points[0] && p.Target(1) == points[1] && p.Target(0) == points[2]
            && p.Available == 997, "reverse-prepend allocates tail first; head handle is not waypoint array index");
        int next = p.Next(head);
        p.FreeOne(head);
        check(!p.IsAllocated(head) && p.IsAllocated(next) && p.IsAllocated(0) && p.Available == 998,
            "FreeOne retires exactly head, not linked tail");
        p.FreeChain(next);
        check(p.Available == 1000 && p.Hint == 0, "FreeChain releases entire saved remainder");
        p.FreeChain(-1);
        check(p.TryBuild(Array.Empty<Point>(), out head) && head == -1 && p.Available == 1000 && p.Hint == 0,
            "terminal free and empty build are resource no-ops");

        var other = Fill(p);
        p.SetTarget(other[0], target);
        p.SetNext(other[0], other[1]);
        uint otherWord = p.ReadWord(other[0]);
        p.FreeOne(other[^1]); p.FreeOne(other[^2]);
        check(!p.TryBuild(points, out head) && head == -1 && p.Available == 2 && p.AllocatedCount == 998
            && p.Hint == 998 && p.ReadWord(other[0]) == otherWord && p.Next(other[0]) == other[1]
            && other.Take(998).All(p.IsAllocated), "partial build rollback frees only its two private slots, not another owner's chain");
        check(p.Allocate() == 998 && p.Allocate() == 999, "rollback slots immediately reusable via ordinary free hint");

        p.Reset(); a = p.Allocate(); b = p.Allocate();
        foreach (int invalid in new[] { -2, -1, 1000, 2047, int.MaxValue })
            check(Throws<ArgumentOutOfRangeException>(() => p.IsAllocated(invalid))
                && Throws<ArgumentOutOfRangeException>(() => p.FreeOne(invalid))
                && Throws<ArgumentOutOfRangeException>(() => p.ReadWord(invalid)),
                $"managed index guard {invalid}, independent of 11-bit terminal representation");
        check(Throws<InvalidOperationException>(() => p.Target(2))
            && Throws<InvalidOperationException>(() => p.SetTarget(2, target))
            && Throws<InvalidOperationException>(() => p.SetNext(a, 2))
            && Throws<ArgumentOutOfRangeException>(() => p.SetNext(a, 1000)), "managed unallocated and invalid successor guards");
        p.SetNext(a, b);
        p.SetTarget(b, target);
        uint before = p.ReadWord(b);
        check(Throws<InvalidOperationException>(() => p.SetNext(b, a))
            && Throws<InvalidOperationException>(() => p.SetNext(a, a))
            && p.ReadWord(b) == before && p.Next(a) == b && p.Available == 998,
            "managed cycle/self-link guards reject before mutation");
        p.FreeOne(b);
        check(Throws<InvalidOperationException>(() => p.FreeOne(b))
            && Throws<InvalidOperationException>(() => p.Next(a))
            && Throws<InvalidOperationException>(() => p.FreeChain(a))
            && p.IsAllocated(a) && p.Available == 999, "double free and dangling chain preflight cannot partially free head");
        check(p.Allocate() == b && p.ReadWord(b) == before && p.Next(a) == b && p.Available == 998,
            "FreeOne/reallocation preserves target bits and restores live successor without rewriting inbound link");
        p.FreeChain(a);
        check(Throws<ArgumentNullException>(() => p.TryBuild(null, out _)) && p.Available == 1000,
            "null build input has no resource side effects");

        using var isolated = new NativeGuestRoute(points[0], points);
        using var second = new NativeGuestRoute(points[0], points);
        check(!ReferenceEquals(isolated.Pool, second.Pool) && isolated.Pool.AllocatedCount == 3
            && isolated.SlotIndex == 2, "default cursor owns a private pool, with real reverse-allocated handles");
        isolated.Dispose(); isolated.Dispose();
        check(isolated.Pool.Available == 1000 && second.Pool.AllocatedCount == 3,
            "cursor Dispose frees whole remainder idempotently without another cursor's private pool");
        var stale = new NativeGuestRoute(points[0], points);
        stale.Pool.Reset();
        var recycled = Fill(stale.Pool); // include the old head: unallocated-read guards alone must not pass this test
        check(Throws<InvalidOperationException>(() => { _ = stale.CurrentTarget; })
            && Throws<InvalidOperationException>(() => stale.Step(15, 0x4000, 10, 10, true))
            && Throws<InvalidOperationException>(() => stale.Dispose()) && recycled.All(stale.Pool.IsAllocated) && stale.Pool.Available == 0,
            "managed reset epoch prevents stale cursor from reading, stepping or freeing recycled ownership");
    }

    static void ConsumerChecks(GuestWalk walk, ParkCell start, Action<bool, string> check)
    {
        Point centre = Centre(start), quarter = new(centre.X, (short)(centre.Z - 64));
        var owner = new object(); var otherOwner = new object();
        var inputs = new NativeMotionInputs(() => 15, () => 0x4000, () => true) { AutomaticStep = false };
        var guest = walk.Spawn(start, start);
        var other = walk.Spawn(start, start);
        check(walk.AssignNativeRoute(other, otherOwner, new[] { centre, quarter }, inputs) == Assignment.Assigned
            && walk.AssignNativeRoute(guest, owner, new[] { quarter, centre }, inputs) == Assignment.Assigned,
            "two actual consumers assign into the shared pool");
        int otherHead = walk.NativeRouteState(other, otherOwner).Value.SlotIndex;
        int guestHead = walk.NativeRouteState(guest, owner).Value.SlotIndex;
        check(otherHead == 1 && guestHead == 3 && walk.NativeRoutes.Next(guestHead) == 2
            && walk.NativeRoutes.Target(guestHead) == quarter && walk.NativeRoutes.AllocatedCount == 4,
            "actual guest snapshots expose shared pool handles, not local array indices");
        walk.StepOwnedNative(guest, owner);
        Point precise = walk.NativeRouteState(guest, owner).Value.Position;
        check(precise == new Point(centre.X, (short)(centre.Z - 15)), "replacement fixture has unquantized exact source position");
        var occupied = Fill(walk.NativeRoutes);
        check(walk.AssignNativeRoute(guest, otherOwner, new[] { centre }, inputs) == Assignment.Refused
            && walk.NativeRouteState(guest, owner).Value.SlotIndex == guestHead && walk.NativeRoutes.Available == 0,
            "Refused is ownership refusal, not destructive exhaustion");
        check(walk.AssignNativeRoute(guest, owner, new[] { centre, quarter, centre }, inputs) == Assignment.Exhausted
            && walk.NativeRouteState(guest, owner) is { SlotIndex: -1, Position: var at } && at == precise
            && guest.HasNativeRoute && !walk.Send(guest, start.Offset(0, -1)) && walk.NativeRoutes.Available == 2
            && !walk.NativeRoutes.IsAllocated(guestHead) && !walk.NativeRoutes.IsAllocated(2)
            && walk.NativeRoutes.IsAllocated(otherHead) && occupied.All(walk.NativeRoutes.IsAllocated),
            "Exhausted disposes old chain BEFORE replacement, rolls back partial output, retains empty lease at exact pose");
        check(walk.BeginNativeRoute(guest, owner, new[] { centre, quarter }, inputs)
            && walk.NativeRoutes.Available == 0 && walk.NativeRouteState(guest, owner).Value.Position == precise,
            "bool wrapper succeeds using exactly old-chain capacity without snapping");
        check(!walk.BeginNativeRoute(guest, owner, new[] { centre, quarter, centre }, inputs)
            && walk.NativeRoutes.Available == 2 && guest.HasNativeRoute,
            "bool wrapper maps destructive Exhausted to false, retaining owner");
        check(walk.AssignNativeRoute(guest, owner, new[] { centre }, inputs) == Assignment.Assigned,
            "prepare old direct-replacement chain");
        int oldDirect = walk.NativeRouteState(guest, owner).Value.SlotIndex;
        Fill(walk.NativeRoutes); // consume the remaining hole as a competing owner
        int callbacks = 0;
        check(walk.AssignDirectNativeRoute(guest, owner, () =>
            {
                callbacks++;
                check(walk.NativeRoutes.Available == 0 && walk.NativeRoutes.IsAllocated(oldDirect)
                    && walk.NativeRoutes.Next(oldDirect) == -1
                    && walk.NativeRouteState(guest, owner) is { SlotIndex: -1, Position: var source } && source == precise,
                    "direct target callback sees old lease disposed and slot already allocated");
                return quarter;
            }, inputs) == Assignment.Assigned && callbacks == 1,
            "direct replacement frees old before allocation even with no spare slot");
        walk.AssignNativeRoute(guest, owner, Array.Empty<Point>(), inputs);
        int competing = walk.NativeRoutes.Allocate();
        check(walk.AssignDirectNativeRoute(guest, owner, () => { callbacks++; return centre; }, inputs) == Assignment.Exhausted
            && callbacks == 1 && walk.NativeRoutes.Available == 0
            && walk.NativeRouteState(guest, owner) is { SlotIndex: -1, Position: var source2 } && source2 == precise,
            "direct capacity failure never evaluates target supplier and retains precise empty lease");
        walk.NativeRoutes.FreeOne(competing);
        check(walk.AssignDirectNativeRoute(guest, owner, () => { callbacks++; return centre; }, inputs) == Assignment.Assigned
            && callbacks == 2 && walk.NativeRouteState(guest, owner).Value.SlotIndex == competing,
            "freeing one actual competing slot permits same guest/owner direct retry");
        walk.Remove(guest.Id);
        check(walk.NativeRoutes.Available == 1 && !guest.HasNativeRoute && walk.NativeRouteState(guest, owner) == null
            && walk.NativeRoutes.IsAllocated(otherHead), "Remove frees this owned remainder only");
        ulong generation = walk.NativeRoutes.ResetGeneration;
        walk.Clear();
        check(walk.NativeRoutes.Available == 1000 && walk.NativeRoutes.Hint == 0
            && walk.NativeRoutes.ResetGeneration != generation && !other.HasNativeRoute
            && walk.NativeRouteState(other, otherOwner) == null && !walk.IsLive(guest) && !walk.IsLive(other),
            "Clear disposes remaining actor chains then resets shared pool and invalidates old references");
        var fresh = walk.Spawn(start, start);
        check(!walk.BeginNativeRoute(guest, owner, new[] { centre }, inputs)
            && !walk.BeginNativeRoute(other, otherOwner, new[] { centre }, inputs), "old references cannot acquire reset pool after ID reuse");
        other = walk.Spawn(start, start);
        walk.AssignNativeRoute(other, otherOwner, new[] { quarter, centre }, inputs);
        otherHead = walk.NativeRouteState(other, otherOwner).Value.SlotIndex;
        walk.AssignNativeRoute(fresh, owner, new[] { centre, quarter }, inputs);
        guestHead = walk.NativeRouteState(fresh, owner).Value.SlotIndex;
        walk.StepOwnedNative(fresh, owner);
        check(walk.NativeRouteState(fresh, owner) is { ExecutionState: 2, Finished: false, SlotIndex: var arrived }
            && arrived == guestHead && walk.NativeRoutes.Available == 996 && walk.NativeRoutes.IsAllocated(guestHead),
            "reaching waypoint retains slot until later state2 retirement");
        walk.StepOwnedNative(fresh, owner);
        check(walk.NativeRouteState(fresh, owner) is { ExecutionState: 3, Position: var unchanged, SlotIndex: var tail }
            && unchanged == centre && tail == 2 && walk.NativeRoutes.Available == 997
            && !walk.NativeRoutes.IsAllocated(guestHead) && walk.NativeRoutes.IsAllocated(tail),
            "state2 frees ONE slot and moves no coordinates");
        for (int i = 0; i < 5; i++) walk.StepOwnedNative(fresh, owner);
        check(walk.NativeRouteState(fresh, owner) is { ExecutionState: 2, Finished: false }
            && walk.NativeRoutes.Available == 997, "final arrival still owns final slot");
        walk.StepOwnedNative(fresh, owner);
        check(walk.NativeRouteState(fresh, owner) is { ExecutionState: 3, SlotIndex: -1, Finished: false }
            && walk.NativeRoutes.Available == 998, "final retirement precedes terminal no-slot walking phase");
        walk.StepOwnedNative(fresh, owner);
        check(walk.NativeRouteState(fresh, owner) is { ExecutionState: 2, Finished: false }, "no-slot walking precedes completion dispatch");
        walk.StepOwnedNative(fresh, owner);
        check(walk.NativeRouteState(fresh, owner) is { Finished: true } && walk.NativeRoutes.Available == 998
            && walk.NativeRoutes.IsAllocated(otherHead) && walk.NativeRoutes.Next(otherHead) == 0,
            "one guest's finish cannot free other guest's chain");

        var fast = new NativeMotionInputs(() => 127, () => 0x400000, () => true) { AutomaticStep = false };
        walk.AssignNativeRoute(fresh, owner, new[] { new Point(-256, centre.Z), centre, quarter }, fast);
        Point beforeFailure = walk.NativeRouteState(fresh, owner).Value.Position;
        walk.StepOwnedNative(fresh, owner);
        check(walk.NativeRouteState(fresh, owner) is { ExecutionState: 0, Failed: true, SlotIndex: -1, Position: var failedAt }
            && failedAt == beforeFailure && walk.NativeRoutes.Available == 998 && walk.NativeRoutes.IsAllocated(otherHead),
            "invalid candidate position frees WHOLE remaining chain, preserves last pose and other guest");
        walk.Remove(other.Id);
        check(walk.NativeRoutes.Available == 1000, "removing other guest frees both unretired slots");
        walk.Clear();
    }

    static void OutputChecks(Action<bool, string> check)
    {
        var root = new ParkCell(4, 4);
        // Check the packed output consumed by a real pool; endpoint quantization belongs to its codec.
        void Output(string label, ParkCell[] path, Point endpoint, params Point[] expected)
        {
            var output = NativeRouteOutput.FromCells(path, endpoint);
            var pool = new NativeRoutePool();
            bool built = pool.TryBuild(output, out int head);
            var actual = new List<Point>();
            for (int slot = head, budget = 1000; slot != -1 && budget-- > 0; slot = pool.Next(slot))
                actual.Add(pool.Target(slot));
            check(built && actual.SequenceEqual(expected.Select(Packed)) && pool.AllocatedCount == expected.Length,
                "explicit BFS output transform (not native pathfinder): " + label);
            pool.FreeChain(head);
        }
        foreach (var (dx, dz, name) in new[] { (1, 0, "east"), (-1, 0, "west"), (0, 1, "south"), (0, -1, "north") })
        {
            var last = root.Offset(2 * dx, 2 * dz);
            var end = Centre(last);
            end = new Point((short)(end.X + 17), (short)(end.Z - 33));
            var expected = dz == -1 ? new[] { end } : new[] { Centre(root), end };
            Output(name + " straight: root skipped ONLY when previous direction is0; exact endpoint not centre",
                new[] { root, root.Offset(dx, dz), last }, end, expected);
        }
        var east = root.Offset(1, 0);
        var north = root.Offset(0, -1);
        var cornerEnd = root.Offset(1, -1);
        Point fractional = new((short)(Centre(cornerEnd).X + 64), (short)(Centre(cornerEnd).Z - 64));
        Output("east then north: reverse emissions restore root, corner, exact endpoint",
            new[] { root, east, cornerEnd }, fractional, Centre(root), Centre(east), fractional);
        Output("north then east: direction0 root suppressed, corner precedes endpoint",
            new[] { root, north, cornerEnd }, fractional, Centre(north), fractional);
        Output("singleton root emits endpoint because previous startsFFFF, not0",
            new[] { root }, fractional, fractional);
        Output("no duplicate elision when root centre equals exact endpoint",
            new[] { root, east }, Centre(root), Centre(root), Centre(root));
        Output("repeated input cells are not silently removed; zero delta has direction4",
            new[] { root, root }, Centre(root), Centre(root), Centre(root));
        Output("diagonal control uses nonzero dx before dz, not independent axis turns",
            new[] { root, east, east.Offset(1, -1) }, fractional, Centre(root), fractional);
        Output("opposite diagonal dx sign is a direction change even with same dz",
            new[] { root, root.Offset(1, -1), root.Offset(0, -2) }, fractional,
            Centre(root), Centre(root.Offset(1, -1)), fractional);
    }

    static void ControllerChecks(Func<Fixture> create, Action<bool, string> check)
    {
        var async = create();
        var guest = async.Add();
        if (async.Until(() => async.Obs(guest) is { State: Flow.State.Pending, Mode: 11 }, check, "queue request pending"))
        {
            var competing = Fill(async.Walk.NativeRoutes);
            Point before = async.Flow.Position(guest).Value;
            async.RefuseRequests = true; // expose retry state instead of immediately submitting another pending token
            async.Step();
            check(async.Obs(guest) is { State: Flow.State.RequestQueue, InQueue: true, PendingToken: null, Stopped: false }
                && async.Flow.Counts == (1, 0) && async.Flow.Position(guest) == before && guest.HasNativeRoute
                && async.Events.Any(e => e.Trace.Event == "route output exhausted; retry state restored")
                && async.Walk.NativeRoutes.Available == 0 && competing.All(async.Walk.NativeRoutes.IsAllocated),
                "async output exhaustion restores retry state with membership and precise empty lease retained");
            for (int i = 0; i < 3; i++) async.Step();
            check(async.Flow.Counts == (1, 0) && async.WanderCalls == 0 && async.Visitors.WentHome == 0
                && async.Visitors.Plans[guest.Id].Intent == VisitorIntent.Entering
                && async.Events.Count(e => e.Trace.Event == "queue registered") == 1
                && !async.Walk.Send(guest, async.Start.Offset(0, -1)) && ReferenceEquals(async.Walk.Guests.Single(), guest),
                "bounded full-pool retries neither duplicate membership nor allow ordinary AI to steal identity");
            async.Walk.NativeRoutes.FreeOne(competing[0]);
            async.RefuseRequests = false;
            async.Step();
            check(async.Obs(guest) is { State: Flow.State.Pending, Mode: 11 } && async.Walk.NativeRoutes.Available == 1,
                "freeing real slot allows resubmission but does not complete asynchronous request inline");
            async.Step();
            check(async.Obs(guest) is { State: Flow.State.Moving, Mode: 11, InQueue: true }
                && async.Walk.NativeRoutes.Available == 0 && async.Flow.Counts == (1, 0)
                && ReferenceEquals(async.Walk.Guests.Single(), guest), "next BeforeStep consumes freed resource for same queued identity");
        }
        async.Clear();

        var direct = create();
        direct.Traffic = 2;
        guest = direct.Add();
        if (direct.Until(() => direct.Obs(guest).State == Flow.State.Staged, check, "mode15 staging completes"))
        {
            var competing = Fill(direct.Walk.NativeRoutes);
            int reads = direct.StagingReads;
            Point before = direct.Flow.Position(guest).Value;
            direct.Traffic = 0;
            for (int i = 0; i < 3; i++) direct.Step();
            check(direct.Obs(guest) is { State: Flow.State.CrossStaging, StageCounted: true, Stopped: false }
                && direct.Obs(guest).Mode != 16 && direct.StagingReads == reads && direct.Walk.NativeRoutes.Available == 0
                && direct.Flow.Position(guest) == before && direct.Flow.StagingPending == 1 && direct.Flow.EpisodeProcessed == 0
                && direct.Events.Count(e => e.Trace.Event == "mode16 allocation exhausted") == 3
                && direct.WanderCalls == 0, "mode16 allocation fails before staging/RNG supplier on every bounded full-pool retry");
            direct.Walk.NativeRoutes.FreeOne(competing[0]);
            direct.Step();
            check(direct.Obs(guest) is { State: Flow.State.Moving, Mode: 16, StageCounted: true }
                && direct.StagingReads == reads + 1 && direct.StagingAvailable.Last() == 0
                && direct.Flow.Position(guest) == before && direct.Walk.NativeRoutes.Available == 0
                && direct.Flow.StagingPending == 1 && direct.Flow.EpisodeProcessed == 0,
                "mode16 retry allocates before supplier; same identity moves only in later pass and completion counters stay unchanged");
            direct.Until(() => direct.Obs(guest).State == Flow.State.RequestQueue, check, "same identity completes mode16 after slot recovery");
            check(direct.Flow.StagingPending == 0 && direct.Flow.EpisodeProcessed == 1
                && direct.Walk.NativeRoutes.Available == 1 && ReferenceEquals(direct.Walk.Guests.Single(), guest),
                "mode16 terminal dispatch, not allocation, updates P/R and retires recovered slot");
        }
        direct.Clear();

        // Optional ExitCandidates service: lazy enumeration must surround each actual allocation attempt.
        var exit = create();
        guest = exit.Add();
        if (exit.Until(() => exit.Obs(guest).State == Flow.State.Accepted, check, "accepted fixture reaches mode13 boundary"))
        {
            var competing = Fill(exit.Walk.NativeRoutes);
            int moves = 0, probes = exit.DirectTargets.Count, fallback = exit.ExitGoalCalls;
            uint firstTick = 0, secondTick = 0;
            IEnumerable<Point> Candidates(Guest candidateGuest)
            {
                moves++;
                firstTick = exit.Tick;
                check(ReferenceEquals(candidateGuest, guest) && exit.Walk.NativeRoutes.Available == 0,
                    "mode13 first candidate observes real full shared pool");
                yield return exit.Staging;
                moves++;
                secondTick = exit.Tick;
                check(exit.DirectTargets.Count == probes + 1
                    && exit.Events.Any(e => e.Tick == exit.Tick && e.Trace.Event == "mode13 allocation exhausted")
                    && exit.Walk.NativeRoutes.Available == 0,
                    "mode13 resumes enumeration only AFTER first candidate allocation failed, not eager ToArray");
                exit.Walk.NativeRoutes.FreeOne(competing[0]);
                yield return exit.Centre;
                moves++; // must not enumerate after successful allocation
            }
            exit.ExitScan = Candidates;
            exit.Step();
            check(moves == 2 && firstTick == secondTick && secondTick == exit.Tick
                && exit.DirectTargets.Skip(probes).SequenceEqual(new[] { exit.Staging, exit.Centre })
                && exit.Obs(guest) is { State: Flow.State.Moving, Mode: 13 }
                && exit.Walk.NativeRoutes.Available == 0 && exit.ExitGoalCalls == fallback,
                "mode13 tries later candidate in SAME BeforeStep tick and stops at first real allocation success");
            if (exit.Until(() => !exit.Flow.Owns(guest), check, "mode13 deferred handback", visitors: false))
                check(ReferenceEquals(exit.Walk.Guests.Single(), guest) && !guest.HasNativeRoute
                    && exit.Walk.NativeRoutes.Available == 1 && exit.Visitors.Plans[guest.Id].Intent == VisitorIntent.Wandering,
                    "successful later exit candidate hands back same identity and frees only its slot");
        }
        exit.Clear();

        // Explicit compatibility control: Services without optional ExitCandidates still uses ExitGoal.
        var fallbackFixture = new Fixture(exit.Walk.Paths, exit.Start, candidates: false);
        guest = fallbackFixture.Add();
        if (fallbackFixture.Until(() => !fallbackFixture.Flow.Owns(guest), check, "legacy exitGoal fallback handback", visitors: false))
            check(fallbackFixture.ExitGoalCalls == 1 && !guest.HasNativeRoute
                && fallbackFixture.Walk.NativeRoutes.Available == 1000, "optional ExitCandidates preserves old exitGoal fallback");
        fallbackFixture.Clear();
    }

    sealed class Fixture
    {
        public readonly GuestWalk Walk;
        public readonly ParkVisitors Visitors;
        public readonly Flow Flow;
        public readonly ParkCell Start;
        public readonly Point Centre, Staging;
        public readonly List<(uint Tick, Flow.TraceEvent Trace)> Events = new();
        public readonly List<Point> DirectTargets = new();
        public readonly List<int> StagingAvailable = new();
        readonly List<Flow.RouteResult> results = new();
        public Func<Guest, IEnumerable<Point>> ExitScan;
        public bool RefuseRequests;
        public int Traffic, StagingReads, WanderCalls, ExitGoalCalls;
        public uint Tick;

        public Fixture(ParkPaths paths, ParkCell start, bool candidates = true)
        {
            Start = start; Centre = NativeRoutePoolChecks.Centre(start);
            Staging = new Point(Centre.X, (short)(Centre.Z - 64));
            Walk = new GuestWalk(paths);
            Visitors = new ParkVisitors(new ParkSim(paths), Walk, () => 0)
                { Needs = new VisitorNeeds(71) { SecondsPerRise = 1_000_000 } };
            foreach (var key in Visitors.Needs.Rates.Keys.ToArray()) Visitors.Needs.Rates[key] = new(0, 0, false);
            ExitScan = _ => Array.Empty<Point>();
            Flow = new Flow(Visitors, new Flow.Services(
                _ => 0,
                _ => { StagingReads++; StagingAvailable.Add(Walk.NativeRoutes.Available); return Staging; },
                Centre,
                (token, g, mode, from, target) =>
                {
                    if (RefuseRequests) return false;
                    results.Add(new Flow.RouteResult(token, g, new[] { target }, null));
                    return true;
                },
                () => { var batch = results.ToArray(); results.Clear(); return batch; },
                target => { DirectTargets.Add(target); return true; },
                _ => true, () => 0x4000, _ => true,
                _ => { ExitGoalCalls++; return Centre; }, _ => { }, Walk.StepOwnedNative,
                e => Events.Add((Tick, e)), candidates ? g => ExitScan(g) : null));
            Walk.BeforeStep = tick => { Tick = tick; Traffic = Flow.Tick(tick, busState: 2, traffic: Traffic); };
        }

        public Guest Add()
        {
            var guest = Visitors.Arrive(Start, Start);
            Visitors.Needs.Set(guest.Id, new VisitorWants { Cash = 1234, Happiness = 0 });
            Flow.Add(guest, 15);
            return guest;
        }
        public Flow.Observation Obs(Guest g) => Flow.Observations.Single(o => ReferenceEquals(o.Guest, g));
        public void Step(bool visitors = true)
        {
            if (visitors) Visitors.Step(.04, () => { WanderCalls++; return Start.Offset(0, -1); });
            else Walk.Step();
        }
        public bool Until(Func<bool> done, Action<bool, string> check, string why, bool visitors = true)
        {
            for (int i = 0; i < 256 && !done(); i++) Step(visitors);
            bool ok = done();
            check(ok, why + " within 256 actual BeforeStep ticks");
            return ok;
        }
        public void Clear()
        {
            Flow.Clear((_, _) => { }, Visitors.DiscardEntranceGuest);
            Walk.Clear();
        }
    }
}
