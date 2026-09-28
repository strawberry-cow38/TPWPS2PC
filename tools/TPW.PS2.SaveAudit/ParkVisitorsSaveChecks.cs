using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

/// <summary>Disc-free owner graph/consumer checks. Not a whole-user-save or UI claim.</summary>
public static class ParkVisitorsSaveChecks
{
    static string Json<T>(T state) => JsonSerializer.Serialize(state);
    static T Round<T>(T state) => JsonSerializer.Deserialize<T>(Json(state))!;

    public static void Run(Action<bool, string> check)
    {
        void C(bool ok, string why) => check(ok, "visitor save: " + why);
        var random = new SnapshotRandom(7);
        var schedule = new GuestDecisionSchedule(random.Next);
        schedule.Completed(11, 42); schedule.Completed(12, uint.MaxValue - 100);
        schedule.NextAction(11, 43);
        var r2 = SnapshotRandom.FromState(Round(random.CaptureState()));
        var d2 = GuestDecisionSchedule.FromState(Round(schedule.CaptureState()), r2.Next);
        C(d2.NextAction(11, 43) == GuestIdleAction.None && Json(random.CaptureState()) == Json(r2.CaptureState()),
            "decision repeated-tick latch restored without RNG draw");
        bool same = true;
        for (uint tick = 44; tick < 1200; tick++)
        {
            same &= schedule.NextAction(11, tick) == d2.NextAction(11, tick);
            if (tick % 7 == 0) same &= random.Next() == r2.Next(); // interleaved OWNER draw
        }
        C(same && Json(random.CaptureState()) == Json(r2.CaptureState()), "decision and owner share exact continuing RNG stream");
        var clock = new ReliefServiceClock(uint.MaxValue - 100);
        var c2 = ReliefServiceClock.FromState(Round(clock.CaptureState()));
        C(c2.Deadline == clock.Deadline && c2.LastTick == clock.LastTick, "wrapped relief deadline preserved, not recomputed");
        foreach (uint tick in new[] { clock.Deadline, clock.Deadline + 1, clock.Deadline + 1, clock.Deadline + 2 })
        {
            C(clock.Advance(tick) == c2.Advance(tick) && Json(clock.CaptureState()) == Json(c2.CaptureState()), "relief strict wait/finish/completion " + tick);
            c2 = ReliefServiceClock.FromState(Round(c2.CaptureState()));
        }
        C(c2.Completed, "relief completion occurs on later update, once");

        var terrain = SyntheticTerrain();
        var paths = new ParkPaths(terrain);
        var walk = new GuestWalk(paths);
        var sim = new ParkSim(paths);
        byte[] bytes = ParkSimSaveChecks.Script(false);
        var program = new RseProgram(bytes);
        var simBindings = new ParkSim.ScriptedBindings
        {
            IdentifyProgram = p => new("fixture/script", p, null!),
            ResolveProgram = key => key == "fixture/script" ? new(key, program, null!) : throw new ArgumentException(),
            IdentifyAnimation = _ => "headless", ResolveAnimation = _ => null!
        };
        var at = new ParkCell(2, 2);
        var ride = sim.Add(10, "ride", new(6, 6), 1, 1, bytes, null!, 3, at, at, out var fault);
        C(ride != null && fault == null && paths.Open(at), "synthetic real model/path/script fixture");
        var visitors = new ParkVisitors(sim, walk) { AutoService = false };
        var aboard = visitors.Arrive(at, at);
        var active = visitors.Arrive(new(3, 2), new(4, 2));
        C(visitors.SendTo(aboard, ride!), "route guest to ride");
        visitors.Step(0, () => new(5, 2));
        C(visitors.Plans[aboard.Id].Intent == VisitorIntent.Queued && !walk.Guests.Contains(aboard)
            && visitors.ReferencedGuests.Contains(aboard), "inactive ride-owned body retained by registry");
        // Another body with the same numeric ID must not merge with the retained visitor.
        var otherWalk = new GuestWalk(paths);
        var reusedBody = otherWalk.Readmit(aboard.Id, new(1, 1), new(1, 1));
        var retired = new ParkRide { Id = ride!.Id };
        GuestWalk.GuestGraph CaptureWalk(ParkVisitors v, Guest extra) => v.Walk.CaptureGraph("fixture/grid", null!,
            v.ReferencedGuests.Concat(new[] { extra }));
        ParkVisitors.StateBindings Bind(GuestWalk.GuestGraph graph, ParkRide live, ParkRide old) => new()
        {
            GuestGraph = graph,
            IdentifyReference = value => ReferenceEquals(value, live) ? "ride/live" : ReferenceEquals(value, old) ? "ride/retired" : throw new ArgumentException("unknown identity"),
            ResolveReference = key => key == "ride/live" ? live : key == "ride/retired" ? old : throw new ArgumentException("unknown reference")
        };
        var graph = CaptureWalk(visitors, reusedBody);
        var state = Round(visitors.CaptureState(Bind(graph, ride, retired)));
        C(graph.GuestGraphId(aboard) != graph.GuestGraphId(reusedBody), "graph IDs distinguish reused numeric guest ID");
        // Stage actual history values: no selection/replay used to create a restored owner.
        state = state with { DestinationHistoryLayout = new IntMapLayout { Slots = new int?[]{aboard.Id}, FreeBottomFirst=Array.Empty<int>() }, DestinationHistory = new[] { new ParkVisitors.HistoryState { Guest = aboard.Id,
            RideReferenceIds = new[] { "ride/retired", "ride/live", null!, null! } } } };
        var ground = ParkGroundSnapshot.Capture(paths, paths.Field, null!, "fixture/grid", "none");
        var freshPaths = ParkGroundSnapshot.Restore(Round(ground), "fixture/grid", "none", terrain, null!).Paths;
        var freshGraph = GuestWalk.AllocateState(Round(graph.Snapshot), "fixture/grid", freshPaths);
        var freshSim = ParkSim.FromScriptedState(Round(sim.CaptureScriptedState(simBindings)), freshPaths, simBindings);
        var freshRetired = new ParkRide { Id = ride.Id };
        var freshBindings = Bind(freshGraph, freshSim.Rides[0], freshRetired);
        var fresh = ParkVisitors.AllocateState(state, freshSim, freshBindings);
        freshGraph.Hydrate(null!); fresh.HydrateStateBindings(freshBindings);
        C(Json(state) == Json(fresh.CaptureState(freshBindings)), "complete JSON owner graph roundtrip (no Spawn/activation)");
        C(ReferenceEquals(fresh.QueuedOwner(aboard.Id), freshSim.Rides[0])
            && fresh.ReferencedRides.Count == 2 && fresh.ReferencedRides.Contains(freshRetired),
            "retired and live rides with reused numeric ID remain distinct instances");
        C(!ReferenceEquals(freshGraph.GuestByGraphId(graph.GuestGraphId(aboard)),
            freshGraph.GuestByGraphId(graph.GuestGraphId(reusedBody))), "inactive duplicate-ID graph objects do not alias");
        // Establish the same history on the first branch through pure restore, so comparisons
        // include nonempty retired references rather than hiding them from the equality check.
        visitors = ParkVisitors.FromState(state, sim, Bind(graph, ride, retired));
        string stable = Json(visitors.CaptureState(Bind(graph, ride, retired)));
        void Reject(ParkVisitors.State bad, string why)
        {
            bool rejected = false;
            try { ParkVisitors.FromState(bad, sim, Bind(graph, ride, retired)); } catch (ArgumentException) { rejected = true; }
            C(rejected && stable == Json(visitors.CaptureState(Bind(graph, ride, retired))), why + " rejects without source mutation");
        }
        Reject(state with { Version = 99 }, "unsupported schema");
        Reject(state with { GuestObjects = state.GuestObjects.Select(g => g with { GraphId = 999 }).ToArray() }, "bad guest reference");
        Reject(state with { Owners = new[] { new ParkVisitors.ReferenceState { Guest = aboard.Id, ReferenceId = "missing" } } }, "bad ride reference");
        Reject(state with { OwnedRandom = null!, SharedRandomId = null! }, "missing RNG stream");
        var node = JsonNode.Parse(Json(state))!.AsObject(); node.Remove("Decisions");
        bool required = false;
        try { JsonSerializer.Deserialize<ParkVisitors.State>(node.ToJsonString()); } catch (JsonException) { required = true; }
        C(required, "required fields reject missing scheduler");
        // Continue actual coordinator Step + sim/walk Advance, including the completion mailbox
        // and post-completion idle decisions. Fixed wander input is an external policy, not RNG.
        ride.Set("VAR_LETMEOFF", aboard.Id); freshSim.Rides[0].Set("VAR_LETMEOFF", aboard.Id);
        same = true;
        for (int i = 0; i < 180; i++)
        {
            double dt = i % 3 == 0 ? .057 : .04;
            visitors.Step(dt, () => new(5, 2)); fresh.Step(dt, () => new(5, 2));
            var a = CaptureWalk(visitors, reusedBody);
            var b = CaptureWalk(fresh, freshGraph.GuestByGraphId(graph.GuestGraphId(reusedBody)));
            same &= Json(visitors.CaptureState(Bind(a, ride, retired))) == Json(fresh.CaptureState(Bind(b, freshSim.Rides[0], freshRetired)))
                && Json(a.Snapshot) == Json(b.Snapshot)
                && Json(sim.CaptureScriptedState(simBindings)) == Json(freshSim.CaptureScriptedState(simBindings));
        }
        C(same && fresh.Rides > 0, "actual Step/Advance/handback and RNG decision continuation after JSON load");
        C(visitors.ReferencedGuests.All(g => g != aboard), "readmission registry follows replacement body, not stale inactive identity");

        // A custom stream may not silently become the default seed. Its state is owned outside
        // visitors and restored once; the provider key is mandatory even with zero decisions.
        var external = new SnapshotRandom(123);
        Func<int> provider = external.Next;
        var custom = new ParkVisitors(new ParkSim(paths), new GuestWalk(paths), provider);
        var customGraph = custom.Walk.CaptureGraph("fixture/grid");
        bool missingProvider = false;
        try { custom.CaptureState(new() { GuestGraph = customGraph }); } catch (ArgumentException) { missingProvider = true; }
        var customState = custom.CaptureState(new() { GuestGraph = customGraph,
            IdentifyReference = value => ReferenceEquals(value, provider) ? "shared/rng" : throw new ArgumentException() });
        C(missingProvider && customState.OwnedRandom == null && customState.SharedRandomId == "shared/rng", "external RNG requires explicit shared provider key");
    }

    // Minimal authored grid/zero-geometry marker read by the production Model loader. No disc,
    // reflection, uninitialized objects, private files or extracted assets are involved.
    static Model SyntheticTerrain()
    {
        var data = new byte[0x240];
        void U(int at, uint value) => BitConverter.GetBytes(value).CopyTo(data, at);
        void F(int at, float value) => BitConverter.GetBytes(value).CopyTo(data, at);
        U(0, Model.Magic); U(0x44, 0x140); U(0x48, 0x80); data[0x30] = 1;
        U(0x14c, 8); U(0x150, 8); F(0x158, 1);
        for (int i = 0; i < 64; i++) data[0x170 + i * 2 + 1] = 1;
        U(0xd4, 0x220); // mesh +54 name
        System.Text.Encoding.ASCII.GetBytes("heightfield\0").CopyTo(data, 0x220);
        for (int i = 0; i < 4; i++) F(0x90 + i * 20, 1);
        F(0x100, 8 * 1.004f); F(0x108, 8 * 1.004f);
        var result = new Model(data);
        result.Materials.Add("sentinel"); result.Materials.Add("jpa_str1");
        return result;
    }
}
