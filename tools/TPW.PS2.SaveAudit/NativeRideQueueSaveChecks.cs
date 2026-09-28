using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;
using QueueOwner = TPW.PS2.Data.NativeRideQueues;

/// <summary>Disc-free queue owner checks, not a whole-save claim. Shape and other provider
/// values are deliberately restored independently of the queue DTO.</summary>
public static class NativeRideQueueSaveChecks
{
    static string Json<T>(T value) => JsonSerializer.Serialize(value);
    static T Round<T>(T value) => JsonSerializer.Deserialize<T>(Json(value))!;

    sealed class Provider
    {
        internal int Calls, RandomIndex, Slots;
        internal bool Ready = true, Broken;
        internal NativeQueueShape Shape = new(new(2, 2), new[] { new ParkCell(2, 3), new(2, 4), new(2, 5) }, 2);
        internal QueueOwner.Services Services;
        internal Provider()
        {
            Services = new() {
                Shape = _ => { Calls++; return Shape; },
                Random = n => { Calls++; return RandomIndex++ % n; },
                Phase = g => { Calls++; return (uint)g.Id; },
                Speed = _ => { Calls++; return 22; },
                AnimationReady = _ => { Calls++; return Ready; },
                SlotAdvanced = _ => { Calls++; Slots++; },
                Broken = _ => { Calls++; return Broken; },
                Tier = _ => { Calls++; return 0; }
            };
        }
        internal Provider Copy() => new() { Calls = Calls, RandomIndex = RandomIndex, Slots = Slots,
            Ready = Ready, Broken = Broken,
            Shape = Shape == null ? null! : new(Shape.Entrance, Shape.Cells.ToArray(), Shape.Rotation) };
    }

    sealed class World
    {
        internal ParkPaths Paths = null!;
        internal GuestWalk Walk = null!;
        internal ParkSim Sim = null!;
        internal ParkRide Ride = null!;
        internal ParkVisitors Visitors = null!;
        internal VisitorNeeds Needs = null!;
        internal QueueOwner Queue = null!;
        internal Provider Provider = new();
        internal Guest? Extra;
        internal uint Now;
        internal Guest[] Inventory() => Visitors.ReferencedGuests.Concat(Queue.ReferencedGuests)
            .Concat(Extra == null ? Array.Empty<Guest>() : new[] { Extra }).ToArray();
        internal string InputId(NativeMotionInputs input) => "input/" + Queue.ReferencedGuests
            .Single(g => Queue.Owns(g) && ReferenceEquals(Queue.StateInputs(g), input)).Id;
        internal GuestWalk.StateBindings WalkBindings() => new() {
            IdentifyNativeOwner = o => ReferenceEquals(o, Queue.StateOwner) ? "queue" : throw new ArgumentException(),
            IdentifyNativeInputs = InputId,
            ResolveNativeOwner = id => id == "queue" ? Queue.StateOwner : throw new ArgumentException(),
            ResolveNativeInputs = Queue.StateInputs
        };
        internal GuestWalk.GuestGraph Graph() => Walk.CaptureGraph("grid", WalkBindings(), Inventory());
        internal QueueOwner.StateBindings Bind(GuestWalk.GuestGraph graph) => new() {
            GuestGraph = graph, OwnerId = "queue", ServicesId = "provider", Services = Provider.Services,
            IdentifyInputs = InputId,
            IdentifyRide = r => Sim.Rides.Contains(r) ? "ride/" + r.Id : throw new ArgumentException(),
            ResolveRide = id => Sim.Rides.Single(r=>"ride/" + r.Id == id)
        };
        internal ParkVisitors.StateBindings VisitorBindings(GuestWalk.GuestGraph graph) => new() {
            GuestGraph = graph,
            IdentifyReference = o => o is ParkRide r && Sim.Rides.Contains(r) ? "ride/" + r.Id : ReferenceEquals(o, Needs) ? "needs"
                : o is Func<ParkRide, ParkCell?> ? "mouth" : throw new ArgumentException("unknown visitor binding"),
            ResolveReference = id => id.StartsWith("ride/") ? Sim.Rides.Single(r=>"ride/" + r.Id == id) : id switch { "needs" => Needs,
                "mouth" => (Func<ParkRide, ParkCell?>)Queue.Mouth, _ => throw new ArgumentException() }
        };
        internal VisitorNeeds.StateBindings NeedsBindings() => new() {
            IdentifySounded = _ => "sound", ResolveSounded = _ => Visitors.StateForwardSound,
            IdentifyNearbyLitter = _ => "litter", ResolveNearbyLitter = _ => Visitors.StateNearbyLitter
        };
        internal QueueOwner.State Save() { var g = Graph(); return Queue.CaptureState(Bind(g)); }
        internal void Tick() { Queue.Tick(++Now); Walk.Advance(.04); }
        internal Guest Add(ParkRide? selected = null)
        {
            var target = selected ?? Ride;
            var guest = Visitors.Arrive(Provider.Shape.Mouth, Provider.Shape.Mouth);
            if (!Visitors.SendTo(guest, target)) throw new Exception($"fixture SendTo failed: takes={Visitors.Takes(Ride)}, closed={Ride.Get("VAR_RIDECLOSED")}, entrance={Ride.Entrance}, service={Ride.ServiceEntry}, state={guest.State}, open={Paths.Open(guest.Cell)}");
            if (!Queue.Arrive(guest, target)) throw new Exception("fixture queue Arrive failed");
            return guest;
        }
    }

    static readonly byte[] ScriptBytes = Script();
    static readonly RseProgram Program = new(ScriptBytes);
    static ParkSim.ScriptedBindings SimBindings() => new() {
        IdentifyProgram = p => new("queue/script", p, null!),
        ResolveProgram = id => id == "queue/script" ? new(id, Program, null!) : throw new ArgumentException(),
        IdentifyAnimation = _ => "headless", ResolveAnimation = _ => null!
    };
    static World New()
    {
        var w = new World { Paths = new(Terrain()) };
        w.Walk = new(w.Paths); w.Sim = new(w.Paths);
        w.Ride = w.Sim.Add(10, "queue fixture", new(6, 6), 1, 1, ScriptBytes, null!, 3,
            new(2, 2), new(1, 2), out var fault)!;
        if (w.Ride == null || fault != null) throw new Exception("fixture ride: " + fault);
        w.Sim.SetOpen(w.Ride.Id, true);
        w.Ride.Set("VAR_CAPACITY", 0);
        w.Visitors = new(w.Sim, w.Walk) { AutoService = false };
        w.Needs = new(); w.Visitors.Needs = w.Needs;
        w.Queue = new(w.Visitors, w.Provider.Services); w.Visitors.NativeQueueMouth = w.Queue.Mouth;
        return w;
    }

    public sealed record FileBundle(ParkPaths.State Paths, GuestWalk.State Walk, ParkSim.ScriptedState Sim,
        ParkVisitors.State Visitors, VisitorNeeds.State Needs, QueueOwner.State Queue);
    static FileBundle FileCut(World w, GuestWalk.GuestGraph g)
    {
        string path=Path.Combine(Path.GetTempPath(),"tpw-queue-"+Guid.NewGuid().ToString("N")+".save");
        try {
            ParkSaveFile.Write(path,new FileBundle(w.Paths.CaptureState("grid"),g.Snapshot,
                w.Sim.CaptureScriptedState(SimBindings()),w.Visitors.CaptureState(w.VisitorBindings(g)),
                w.Needs.CaptureState(w.NeedsBindings()),w.Queue.CaptureState(w.Bind(g))));
            return ParkSaveFile.Read<FileBundle>(path);
        } finally {File.Delete(path);File.Delete(path+".bak");}
    }

    static World Restore(World source, Action<bool, string> check)
    {
        var graph = source.Graph();
        var bundle=FileCut(source,graph);
        var state = bundle.Queue;
        var walkState = bundle.Walk;
        var visitorState = bundle.Visitors;
        var needsState = bundle.Needs;
        var fresh = new World { Paths = ParkPaths.FromState(bundle.Paths, "grid", Terrain()),
            Provider = source.Provider.Copy(), Now = source.Now };
        var g = GuestWalk.AllocateState(walkState, "grid", fresh.Paths); fresh.Walk = g.Walk;
        fresh.Sim = ParkSim.FromScriptedState(bundle.Sim, fresh.Paths, SimBindings());
        fresh.Ride = fresh.Sim.Rides.Single(r=>r.Id==source.Ride.Id);
        fresh.Visitors = ParkVisitors.AllocateState(visitorState, fresh.Sim, fresh.VisitorBindings(g));
        fresh.Needs = VisitorNeeds.FromState(needsState, fresh.NeedsBindings());
        fresh.Extra = source.Extra == null ? null : g.GuestByGraphId(graph.GuestGraphId(source.Extra));
        int calls = fresh.Provider.Calls;
        string pool = Json(fresh.Walk.NativeRoutes.CaptureState());
        fresh.Queue = QueueOwner.AllocateState(state, fresh.Visitors, fresh.Bind(g));
        bool blocked = false;
        try { fresh.Queue.Tick(999); } catch (InvalidOperationException) { blocked = true; }
        check(blocked && !fresh.Queue.StateBindingsHydrated, "shell blocks gameplay before hydration");
        // Public DTOs must not remain backing storage of the shell.
        Array.Reverse(state.Members);
        foreach (var q in state.Queues) Array.Reverse(q.Members);
        g.Hydrate(fresh.WalkBindings());
        fresh.Visitors.HydrateStateBindings(fresh.VisitorBindings(g));
        fresh.Queue.HydrateStateBindings();
        check(calls == fresh.Provider.Calls && pool == Json(fresh.Walk.NativeRoutes.CaptureState()),
            "restore invokes no services, motion callbacks, RNG, assignment or pool allocation");
        check(!ReferenceEquals(source.Queue.StateOwner, fresh.Queue.StateOwner)
            && !ReferenceEquals(source.Walk.NativeRoutes, fresh.Walk.NativeRoutes), "fresh owner and single fresh walking pool");
        check(Json(source.Save()) == Json(fresh.Save()) && Json(walkState) == Json(fresh.Graph().Snapshot),
            "complete JSON queue/clock/deadline/history/order/lease/cursor/pool roundtrip");
        return fresh;
    }

    public static int Run()
    {
        int count = 0;
        void C(bool ok, string why) { if (!ok) throw new Exception("native queue save: " + why); count++; }
        var a = New(); var head = a.Add(); var middle = a.Add(); var tail = a.Add();
        for (int i = 0; i < 400 && a.Queue.Observations.Any(o => o.Step != QueueOwner.Step.Waiting); i++) a.Tick();
        C(a.Queue.Observations.All(o => o.Step == QueueOwner.Step.Waiting) && a.Queue.Members(a.Ride).Count == 3,
            "real arrivals walk from mouth then wait in head/tail order");
        a.Tick(); // genuine waiting deadlines / random-facing work
        var newcomer = a.Add(); a.Tick();
        // Same display ID, distinct inactive body, never used as an owner identity.
        a.Extra = new GuestWalk(a.Paths).Readmit(head.Id, new(1, 1), new(1, 1));
        var inventory = a.Graph();
        C(inventory.GuestGraphId(head) != inventory.GuestGraphId(a.Extra) && a.Extra.Id == head.Id,
            "graph references distinguish duplicate numeric IDs");
        C(a.Queue.Observations.Any(o => o.Step == QueueOwner.Step.WalkIn)
            && a.Queue.Observations.Any(o => o.Step == QueueOwner.Step.Waiting), "mixed moving/waiting snapshot");
        var b = Restore(a, C);
        bool moving = false, quitting = false, facing = false;
        for (int i = 0; i < 260; i++)
        {
            if (i == 2) { a.Ride.Set("VAR_CAPACITY", 1); b.Ride.Set("VAR_CAPACITY", 1); }
            if (i == 80)
            {
                foreach (var w in new[] { a, b }) { var needs = w.Needs.Of(tail.Id); needs.Unknown78 = 81; w.Needs.Set(tail.Id, needs); }
            }
            a.Tick(); b.Tick();
            var obs = a.Queue.Observations;
            _ = b.Queue.Observations; // observations sample Shape; keep probe calls symmetric.
            moving |= obs.Any(o => o.Step == QueueOwner.Step.MoveUp);
            quitting |= obs.Any(o => o.Step == QueueOwner.Step.Quit);
            facing |= obs.Any(o => o.Step == QueueOwner.Step.Waiting && o.Deadline > a.Now);
            C(Json(a.Save()) == Json(b.Save()) && Json(a.Graph().Snapshot) == Json(b.Graph().Snapshot)
                && Json(a.Needs.CaptureState(a.NeedsBindings())) == Json(b.Needs.CaptureState(b.NeedsBindings()))
                && Json(a.Visitors.CaptureState(a.VisitorBindings(a.Graph()))) == Json(b.Visitors.CaptureState(b.VisitorBindings(b.Graph())))
                && a.Provider.Calls == b.Provider.Calls && a.Provider.RandomIndex == b.Provider.RandomIndex && a.Provider.Slots == b.Provider.Slots,
                "real queue continuation tick " + i);
            if (i == 3 || i == 81 || i == 180) b = Restore(b, C); // move-up, leave, and history snapshots
        }
        C(moving && quitting && facing && a.Queue.Boarded == 1 && a.Queue.Impatient == 1 && a.Queue.Quits == 1,
            "boarding head ripples move-up; waiting tail impatience walks out and releases");
        C(a.Queue.Boardings.Single().Guest == head && !a.Walk.IsLive(head), "inactive history guest inventory retained");
        C(a.Queue.Members(a.Ride).Select(g => g.Id).SequenceEqual(new[] { middle.Id, newcomer.Id }), "remaining head/tail order");
        // Event 7 creates unlinked moving members, event 10 creates Release members.
        foreach (bool broken in new[] { true, false })
        {
            var x = New(); x.Add(); x.Add(); x.Tick();
            if (broken) x.Provider.Broken = true; else x.Provider.Shape = null!;
            x.Tick();
            C(x.Queue.Observations.All(o => o.Index == -1 && o.Step == (broken ? QueueOwner.Step.Quit : QueueOwner.Step.Release)),
                "whole queue event unlinks but retains walking membership");
            var y = Restore(x, C);
            for (int i = 0; i < 160; i++) { x.Tick(); y.Tick(); C(Json(x.Save()) == Json(y.Save())
                && Json(x.Graph().Snapshot) == Json(y.Graph().Snapshot), "event continuation " + broken + "/" + i); }
            C(broken ? x.Queue.Quits == 2 : x.Queue.Released == 2, "event handback completes");
        }
        // Real queue-map removal leaves a hole. A later ride must reuse the SAME
        // iteration slot, rather than being appended after the other queues on load.
        var churn = New();
        ParkRide Another(World w, int id)
        {
            var r=w.Sim.Add(id,"queue fixture",new(6,6),1,1,ScriptBytes,null!,3,
                new(2,2),new(1,2),out var fault)!;
            C(r!=null && fault==null,"churn ride exists");
            w.Sim.SetOpen(id,true);r.Set("VAR_CAPACITY",0);return r;
        }
        var r2=Another(churn,20);var r3=Another(churn,30);var r4=Another(churn,40);
        var refused=churn.Visitors.Arrive(new(2,5),new(2,5));
        C(!churn.Queue.Arrive(refused,churn.Ride),"unassigned guest refuses but leaves empty queue entry");
        churn.Add(r2);churn.Add(r3);churn.Tick();
        C(churn.Save().QueueLayout.FreeBottomFirst.Length==1,"actual removed queue leaves one map hole");
        var loaded=Restore(churn,C);
        churn.Add(r4);loaded.Add(loaded.Sim.Rides.Single(r=>r.Id==40));
        C(churn.Save().Queues.Select(q=>q.RideId).SequenceEqual(new[]{"ride/40","ride/20","ride/30"}),
            "new queue reuses hole ahead of older live queues");
        for(int i=0;i<150;i++) { churn.Tick();loaded.Tick();
            C(Json(churn.Save())==Json(loaded.Save()) && Json(churn.Graph().Snapshot)==Json(loaded.Graph().Snapshot)
                && churn.Provider.RandomIndex==loaded.Provider.RandomIndex,"churn ordering continuation "+i); }
        Malformed(C);
        return count;
    }

    static void Malformed(Action<bool, string> check)
    {
        var w = New(); w.Add(); w.Add(); w.Tick();
        var g = w.Graph(); var bindings = w.Bind(g); var good = w.Queue.CaptureState(bindings);
        void Bad(QueueOwner.State bad, string why)
        {
            int calls = w.Provider.Calls; string pool = Json(w.Walk.NativeRoutes.CaptureState());
            string source = Json(w.Save()); bool rejected = false;
            try { QueueOwner.AllocateState(bad, w.Visitors, bindings); } catch (ArgumentException) { rejected = true; }
            check(rejected && calls == w.Provider.Calls && pool == Json(w.Walk.NativeRoutes.CaptureState())
                && source == Json(w.Save()), "malformed rejected before effects: " + why);
        }
        Bad(good with { Version = 99 }, "version");
        Bad(good with { OwnerId = "wrong" }, "owner");
        Bad(good with { ServicesId = "wrong" }, "services");
        Bad(good with { HasSlotAdvanced = false }, "callback shape");
        Bad(good with { Members = null! }, "null members");
        Bad(good with { Members = new QueueOwner.MemberState[QueueOwner.StateMemberLimit + 1] }, "bounded members");
        Bad(good with { Joined = -1 }, "counter");
        Bad(good with { Members = new[] { good.Members[0], good.Members[0] } }, "duplicate guest");
        Bad(good with { Queues = Array.Empty<QueueOwner.QueueState>() }, "missing linked order");
        Bad(good with { Queues = new[] { good.Queues[0] with { Members = new[] { good.Members[0].GuestGraphId, good.Members[0].GuestGraphId } } } }, "duplicate linked member");
        void Row(QueueOwner.MemberState row, string why) => Bad(good with { Members = new[] { row, good.Members[1] } }, why);
        Row(good.Members[0] with { GuestGraphId = 9999 }, "unknown graph guest");
        Row(good.Members[0] with { InputsId = "wrong" }, "lease input identity");
        Row(good.Members[0] with { Step = (QueueOwner.Step)999 }, "step");
        Row(good.Members[0] with { Step = QueueOwner.Step.Quit }, "linked quitter");
        Row(good.Members[0] with { Step = QueueOwner.Step.Waiting, Routed = true }, "waiting latch");
        Required(good, check); Required(good.Members[0], check); Required(good.Queues[0], check);
        // Readiness comes from the rebound provider; restoring never samples it.
        w.Provider.Ready = false;
        var fresh = Restore(w, check);
        w.Tick(); fresh.Tick();
        check(Json(w.Save()) == Json(fresh.Save()) && Json(w.Graph().Snapshot) == Json(fresh.Graph().Snapshot), "rebound readiness hold");
    }

    static void Required<T>(T value, Action<bool, string> check)
    {
        var node = JsonNode.Parse(Json(value))!.AsObject();
        foreach (string key in node.Select(p => p.Key).ToArray())
        {
            var bad = node.DeepClone().AsObject(); bad.Remove(key); bool rejected = false;
            try { JsonSerializer.Deserialize<T>(bad.ToJsonString()); } catch (JsonException) { rejected = true; }
            check(rejected, typeof(T).Name + " requires " + key);
        }
        node["Unknown"] = 0; bool unknown = false;
        try { JsonSerializer.Deserialize<T>(node.ToJsonString()); } catch (JsonException) { unknown = true; }
        check(unknown, typeof(T).Name + " rejects unknown JSON fields");
    }

    internal static byte[] Script()
    {
        uint[] words = { 0x80000000u | (uint)RseOpcode.NAME, 0x10000000,
            0x80000000u | (uint)RseOpcode.WAIT, 40,
            0x80000000u | (uint)RseOpcode.BRANCH, 0x20000002 };
        string[] names = { "VAR_LETMEON", "VAR_LETMEOFF", "VAR_CAPACITY", "VAR_ONRIDE", "VAR_RIDECLOSED", "VAR_BROKEN" };
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.ASCII, true);
        w.Write("RSSE"u8); w.Write(0x10f51); w.Write(names.Length); w.Write(16); w.Write(1000);
        w.Write(0); w.Write(0); w.Write(0); w.Write("Pad Pad Pad Pad "u8);
        w.Write(words.Length); foreach (uint word in words) w.Write(word);
        byte[] text = Encoding.ASCII.GetBytes("queue\0"); w.Write(text.Length); w.Write(text);
        foreach (string name in names) { byte[] bytes = Encoding.ASCII.GetBytes(name + "\0"); w.Write(bytes.Length); w.Write(bytes); }
        return stream.ToArray();
    }

    // Authored public Model bytes, no extraction, reflection or private fixtures.
    static Model Terrain()
    {
        var data = new byte[0x240];
        void U(int at, uint value) => BitConverter.GetBytes(value).CopyTo(data, at);
        void F(int at, float value) => BitConverter.GetBytes(value).CopyTo(data, at);
        U(0, Model.Magic); U(0x44, 0x140); U(0x48, 0x80); data[0x30] = 1;
        U(0x14c, 8); U(0x150, 8); F(0x158, 1);
        for (int i = 0; i < 64; i++) data[0x170 + i * 2 + 1] = 1;
        U(0xd4, 0x220); Encoding.ASCII.GetBytes("heightfield\0").CopyTo(data, 0x220);
        for (int i = 0; i < 4; i++) F(0x90 + i * 20, 1);
        F(0x100, 8 * 1.004f); F(0x108, 8 * 1.004f);
        var result = new Model(data); result.Materials.Add("sentinel"); result.Materials.Add("jpa_str1");
        return result;
    }
}
