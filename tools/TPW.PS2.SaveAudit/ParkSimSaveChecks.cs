using System.Text;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

/// <summary>Disc-free scripted runtime consumer, not a full park save test.</summary>
public static class ParkSimSaveChecks
{
    public static void Run(Action<bool, string> check)
    {
        string Json(ParkSim.ScriptedState s) => JsonSerializer.Serialize(s);
        ParkSim.ScriptedState Read(string s) => JsonSerializer.Deserialize<ParkSim.ScriptedState>(s)!;
        byte[] rootBytes = Script(true), childBytes = Script(false);
        var rootProgram = new RseProgram(rootBytes); var childProgram = new RseProgram(childBytes);
        const string rootKey = "folder/rev/root.asset", childKey = "folder/rev/effect.asset";
        int bindingsCalled = 0;
        ParkSim.ScriptAsset Asset(string key) => new(key, key == rootKey ? rootProgram :
            key == childKey ? childProgram : throw new ArgumentException("Unknown asset"), "folder/rev");
        var bindings = new ParkSim.ScriptedBindings
        {
            IdentifyProgram = p => new(p.CodeWords == rootProgram.CodeWords ? rootKey : childKey, p, "folder/rev"),
            ResolveProgram = Asset,
            ResolveChild = (source, name) => source == rootKey && name == "child" ? Asset(childKey) : null!,
            IdentifyAnimation = _ => "headless/rev", ResolveAnimation = key => key == "headless/rev" ? null! : throw new ArgumentException(),
            BindCallbacks = (_, _) => bindingsCalled++
        };
        ParkSim.ScriptedState Capture(ParkSim p) => p.CaptureScriptedState(bindings);
        ParkSim Restore(ParkSim.ScriptedState s) => ParkSim.FromScriptedState(s, null!, bindings);
        var original = new ParkSim(null!);
        ParkRide Add(int id) => original.Add(id, "display-not-key", new(1, 2), 1, 1, rootBytes, null!, 3,
            null, null, out _, sibling: name => name == "child" ? childBytes : null!);
        var first = Add(70); var second = Add(19); var removed = Add(81);
        int liveHandle = original.HandleOf(first.Machine), deadHandle = original.HandleOf(removed.Machine);
        original.Remove(removed.Id);
        first.Join(91); first.Join(92); first.Join(93); first.Set("VAR_LETMEOFF", 73);
        first.Book(9, -2); first.Service(4); first.Wear(12);
        original.Finances.CreditByKind(4, 31); original.Finances.MonthEnd(7);
        original.Advance(.057);
        for (int i = 0; i < 17; i++) original.FindRandom("runtime");
        string before = Json(Capture(original));
        var input = Read(before); var restored = Restore(input);
        check(input.Carry == 17 && original.Time == 40 && first.Left.SequenceEqual(new[] { 73 }), "ParkSim: nonzero carry and leaver fixture");
        check(Json(Capture(restored)) == before && bindingsCalled == 1, "ParkSim: complete roundtrip without init/binder replay during capture");
        check(restored.Rides.Select(r => r.Id).SequenceEqual(new[] { 70, 19 })
            && !ReferenceEquals(restored.Rides[0].Machine, restored.Rides[1].Machine)
            && ReferenceEquals(restored.Rides[0].Machine.Program, restored.Rides[1].Machine.Program), "ParkSim: ordered placements, distinct VMs, shared program asset");
        check(ReferenceEquals(restored.ByHandle(liveHandle), restored.Rides[0].Machine)
            && restored.ByHandle(deadHandle) == null && restored.HandleOf(restored.Rides[1].Machine) == 3,
            "ParkSim: live/dead handles and allocator high-water mark");
        original.HandleOf(second.Machine);
        bool rng = true;
        for (int i = 0; i < 60; i++)
            rng &= original.HandleOf(original.FindRandom("runtime")) == restored.HandleOf(restored.FindRandom("runtime"));
        check(rng, "ParkSim: directory RNG continuation");
        // Neither changing the input DTO nor a returned snapshot aliases either live park.
        input.Rides[0].Queue[0] = -123;
        check(Json(Capture(original)) == Json(Capture(restored)), "ParkSim: restore queue ownership");
        var clone = Capture(restored); clone.AdvisorCounters[0] = 99; clone.Machines[0].Vm.Variables[0] = -44;
        check(restored.Rides[0].Machine["v0"] == 1 && restored.AdvisorCounters[0] != 99, "ParkSim: capture ownership and no init replay");
        // Advance both through the future SPAWN, then save children whose keys were not runtime NAME.
        bool continuation = true;
        foreach (double dt in new[] { .063, .017, .041, .12, .029 })
        { original.Advance(dt); restored.Advance(dt); continuation &= Json(Capture(original)) == Json(Capture(restored)); }
        var graph = Capture(restored); var again = Restore(Read(Json(graph)));
        check(continuation && graph.Machines.Length == 6 && graph.Hosts.Length == 2, "ParkSim: actual Advance and future Spawn continuation");
        var root = again.Rides[0].Machine;
        check(root.Child != null && root.SoundChild != null && ReferenceEquals(root.Child.Parent, root)
            && !ReferenceEquals(root.Child, root.SoundChild) && ReferenceEquals(root.Child.Program, root.SoundChild.Program)
            && graph.Machines.Count(m => m.HostId == graph.Rides[0].HostId) == 3
            && graph.Machines.Count(m => m.Vm.ProgramKey == childKey) == 4, "ParkSim: parent/child/sound identity, shared hosts, persistent child keys");
        // The child uses the park directory, not a new/empty directory, after restore.
        check(root.Child!.LastValue != 0 && again.ByHandle(root.Child.LastValue) != null, "ParkSim: future child shares park directory");
        string stable = Json(Capture(original));
        void Reject(Action<JsonObject> corrupt, string label)
        {
            var node = JsonNode.Parse(Json(graph))!.AsObject(); corrupt(node); bool rejected = false;
            try { Restore(Read(node.ToJsonString())); } catch (ArgumentException) { rejected = true; }
            catch (NotSupportedException) { rejected = true; }
            catch (JsonException) { rejected = true; }
            check(rejected && Json(Capture(original)) == stable, "ParkSim rejects " + label + " without source mutation");
        }
        Reject(n => n["Machines"]![0]!["Vm"]!["ChildId"] = "missing", "dangling VM link");
        Reject(n => n["Machines"]![1]!["Id"] = n["Machines"]![0]!["Id"]!.GetValue<string>(), "duplicate VM ID");
        Reject(n => n["Hosts"]![0]!["Host"]!["AssetKey"] = "wrong", "animation identity");
        Reject(n => n["Machines"]![0]!["Vm"]!["ProgramKey"] = childKey, "program mismatch");
        Reject(n => n["Machines"]![0]!["SiblingScope"] = "other/folder", "sibling scope mismatch");
        Reject(n => n["Machines"]![0]!["Vm"]!["RandomKind"] = 1, "external RNG");
        Reject(n => n["Machines"]![0]!["Vm"]!["Pc"] = -1, "invalid PC");
        Reject(n => n["Rides"]![0]!["TrackId"] = "track", "track owner");
        Reject(n => n["Rides"]![0]!["CoasterId"] = "coaster", "coaster owner");
        Reject(n => n["Rides"]![0]!["AssignedMechanicId"] = "staff/70", "missing independent staff binding");
        Reject(n => n["Upgrades"] = new JsonArray(999), "upgrade outside graph");
        Reject(n => n["HandleAllocator"] = 0, "allocator below issued handles");
        Reject(n => n["Carry"] = 321, "bad carry");
        Reject(n => n["Hosts"]![0]!["Host"]!["HeadSlots"] = ParkSim.ScriptedValueLimit + 1, "oversized host allocation");
        Reject(n => n["Rides"]![1]!["Id"] = 70, "duplicate placement ID");
        Reject(n => n["Rides"]![0]!["HostId"] = "missing", "missing host");
        Reject(n => n["WearEvents"] = -1, "invalid instrumentation");
        Reject(n => { foreach(var h in n["Hosts"]!.AsArray()) h!["Host"]!["HeadSlots"] = 150_000;
            foreach(var m in n["Machines"]!.AsArray()) m!["Vm"]!["Heads"] = null; }, "aggregate per-VM prospective heads");
        Reject(n => n.Remove("Time"), "missing required field");
        // Stage private instrumentation/list state with DTOs: never gameplay-replay it.
        var staged = JsonNode.Parse(Json(graph))!.AsObject();
        staged["Upgrades"] = new JsonArray(19, 70); staged["AdvisorCounters"]![2] = -13;
        staged["WearEvents"] = 123; staged["UpgradesPastLastTier"] = 4;
        var stagedPark = Restore(Read(staged.ToJsonString()));
        check(Json(Capture(stagedPark)) == staged.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
            "ParkSim: ordered upgrade list and instrumentation hydrate directly");
        // Metadata must not extend the life of replaced children. Spawn directly without
        // stepping them (the normal child script obtains handles, which intentionally retain).
        var collectPark=Restore(Read(before));
        var forgotten=DetachSpawnedChildren(collectPark.Rides[0].Machine,rootKey);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        check(forgotten.All(w=>!w.IsAlive), "ParkSim: replaced unhandled children collect while restored park remains live");
        GC.KeepAlive(collectPark);
        // A large live queue must fail before any asset resolution or VM/host snapshot copies.
        var oversized=Restore(Read(before));
        for(int i=0;i<=ParkSim.ScriptedValueLimit;i++) oversized.Rides[0].Join(i);
        bool assetTouched=false, preflightRejected=false;int countBefore=oversized.Rides[0].Queue.Count;
        try { oversized.CaptureScriptedState(new ParkSim.ScriptedBindings
            { IdentifyProgram=p=>{assetTouched=true;throw new Exception("late preflight");},
              ResolveProgram=k=>{assetTouched=true;throw new Exception("late preflight");} }); }
        catch(ArgumentException e) { preflightRejected=e.Message.Contains("capture collection bound"); }
        check(preflightRejected&&!assetTouched&&oversized.Rides[0].Queue.Count==countBefore,
            "ParkSim: capture preflights queue bounds before resolvers/copies without mutation");
        // Parent-only closure beyond Chain's depth bound is also owned, not silently discarded.
        var deep = JsonNode.Parse(Json(graph))!.AsObject(); var nodes = deep["Machines"]!.AsArray();
        string previous = nodes[0]!["Id"]!.GetValue<string>();
        for (int i = 0; i < 12; i++)
        {
            var next = nodes[0]!.DeepClone(); next["Id"] = "ancestor" + i;
            next["Vm"]!["ParentId"] = previous; next["Vm"]!["ChildId"] = null; next["Vm"]!["SoundChildId"] = null;
            nodes.Add(next); previous = "ancestor" + i;
        }
        nodes[0]!["Vm"]!["ParentId"] = previous;
        check(Capture(Restore(Read(deep.ToJsonString()))).Machines.Length == graph.Machines.Length + 12,
            "ParkSim: complete cyclic parent closure, no depth-limited capture");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference[] DetachSpawnedChildren(RseMachine root,string key)
    {
        root.RunSlice(200);
        var forgotten=new[]{new WeakReference(root.Child!),new WeakReference(root.SoundChild!)};
        var node=JsonNode.Parse(JsonSerializer.Serialize(root.CaptureState(key,m=>ReferenceEquals(m,root.Child)?"child":"sound")))!.AsObject();
        node["ChildId"]=null;node["SoundChildId"]=null;
        root.RestoreState(JsonSerializer.Deserialize<RseMachine.State>(node.ToJsonString())!,key,
            _=>throw new Exception("unexpected retained link"));
        return forgotten;
    }

    static byte[] Script(bool root)
    {
        var words = new List<uint>();
        void Emit(RseOpcode op, params uint[] args) { words.Add(0x80000000u | (uint)op); words.AddRange(args); }
        Emit(RseOpcode.NAME, 0x10000000); Emit(RseOpcode.ADD, 0x40000000, 1);
        if (root) { Emit(RseOpcode.WAIT, 100); Emit(RseOpcode.SPAWNCHILD, 0x10000008); Emit(RseOpcode.SPAWNSOUND, 0x10000008); }
        int loop = words.Count;
        if (root) Emit(RseOpcode.RAND, 0x40000001, 10000);
        else Emit(RseOpcode.FINDSCRIPTRAND, 0x10000000, 0x40000001);
        Emit(RseOpcode.WAIT, 40); Emit(RseOpcode.BRANCH, 0x20000000u | (uint)loop);
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.ASCII, true);
        w.Write("RSSE"u8); w.Write(0x10f51); w.Write(4); w.Write(16); w.Write(1000);
        w.Write(0); w.Write(0); w.Write(0); w.Write("Pad Pad Pad Pad "u8);
        w.Write(words.Count); foreach (uint word in words) w.Write(word);
        byte[] strings = Encoding.ASCII.GetBytes("runtime\0child\0"); w.Write(strings.Length); w.Write(strings);
        foreach (string name in new[] { "v0", "v1", "VAR_LETMEON", "VAR_LETMEOFF" })
        { byte[] bytes = Encoding.ASCII.GetBytes(name + "\0"); w.Write(bytes.Length); w.Write(bytes); }
        return stream.ToArray();
    }
}
