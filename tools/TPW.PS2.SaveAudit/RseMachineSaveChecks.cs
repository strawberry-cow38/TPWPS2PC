using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

/// <summary>Disc-free checks against real RseProgram bytecode and RseMachine execution.</summary>
public static class RseMachineSaveChecks
{
    public static void Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        const string key = "builtin/rse-save-check/v1";
        string Json<T>(T value) => JsonSerializer.Serialize(value);
        RseMachine.State Read(string json) => JsonSerializer.Deserialize<RseMachine.State>(json)!;
        var program = Fixture();
        var host = new Host();
        var child = new RseMachine(program);
        var sound = new RseMachine(program);
        var control = new RseMachine(program, host, spawn: name => name == "child" ? child : sound);
        string Identify(RseMachine m) => ReferenceEquals(m, control) ? "main" :
            ReferenceEquals(m, child) ? "child" : ReferenceEquals(m, sound) ? "sound" : throw new Exception("Unknown VM");
        RseMachine.State Capture() => control.CaptureState(key, Identify);
        check(control.RunSlice(10) == RseYield.Wait, "RSE pauses in subroutine WAIT");
        var state = Read(Json(Capture()));
        check(state.GuestTop == 2 && state.CallTop == state.StackSize - 1 && state.WaitUntil == 110,
            "RSE simultaneous call frame, guest stack and pending wait");
        check(state.Walks[0].State == 1 && state.TimedWalk && state.AttemptedWalk && state.Bouncing == 1 &&
            state.LimboUsed == 1 && state.Heads.Count(x => x != 0) == 1 && state.LoopSlot == 2 &&
            state.SparkFrom == 8 && state.SparkTo == 9 && state.FloatFor == 3000 && state.FloatA == 4 &&
            state.FloatB == 5 && state.BumpRate == 77 && state.Turbo == 19 && state.Timer == 910,
            "RSE populated walk/bounce/limbo/head/timer/loop/effect state");
        check(state.ParentId == null && state.ChildId == "child" && state.SoundChildId == "sound",
            "RSE links are stable IDs only");
        var restoredHost = new Host();
        var restored = new RseMachine(program, restoredHost, spawn: _ => throw new Exception("Restore spawned a script"));
        var restoredChild = new RseMachine(program);
        var restoredSound = new RseMachine(program);
        RseMachine Resolve(string id) => id switch { "main" => restored, "child" => restoredChild,
            "sound" => restoredSound, _ => throw new Exception("Unknown ID") };
        string RestoredId(RseMachine m) => ReferenceEquals(m, restored) ? "main" :
            ReferenceEquals(m, restoredChild) ? "child" : ReferenceEquals(m, restoredSound) ? "sound" : throw new Exception("Unknown restored VM");
        restored.RestoreState(state, key, Resolve);
        restoredChild.RestoreState(Read(Json(child.CaptureState(key, Identify))), key, Resolve);
        restoredSound.RestoreState(Read(Json(sound.CaptureState(key, Identify))), key, Resolve);
        check(restoredHost.Events.Count == 0, "RSE allocate/hydrate does not call host (including HeadSlots) or replay effects");
        check(ReferenceEquals(restored.Child, restoredChild) && ReferenceEquals(restoredChild.Parent, restored) &&
            ReferenceEquals(restored.SoundChild, restoredSound) && restoredSound.Parent == null,
            "RSE parent/child/sound identities preserved without inventing sound-parent links");
        check(Json(state) == Json(restored.CaptureState(key, RestoredId)), "RSE full JSON state round trip");

        void Scribble(RseMachine.State s)
        {
            s.Variables[0] ^= 123; s.Stack[0] ^= 456; s.Heads[0] ^= 789;
            s.Walks[0] = default; s.Bounce[0] = default; s.Limbo[0] = default;
            s.Random.SeedArray[1] ^= 123;
        }
        var detached = Capture();
        string original = Json(Capture());
        Scribble(detached);
        check(original == Json(Capture()), "RSE every capture array detached");
        string loaded = Json(restored.CaptureState(key, RestoredId));
        Scribble(state);
        check(loaded == Json(restored.CaptureState(key, RestoredId)), "RSE every restore array detached including RNG");
        state = Read(original);

        host.Events.Clear();
        for (int t = 50; t <= 8050; t += 40)
        {
            check(control.RunSlice(t) == restored.RunSlice(t), $"RSE resumed yield t={t}");
            check(Json(Capture()) == Json(restored.CaptureState(key, RestoredId)), $"RSE resumed full state and RNG t={t}");
            check(child[0] == restoredChild[0], $"RSE resumed child variable identity t={t}");
        }
        check(host.Events.SequenceEqual(restoredHost.Events), "RSE resumed host effect sequence matches uninterrupted execution");
        check(control.GuestCount == 0 && control.WalkSlotsInUse == 0 && control.Bouncing == 0 && Capture().LimboUsed == 0,
            "RSE continuation actually drains guest/walk/bounce/limbo tables");
        check(original == Json(state), "RSE ticking leaves supplied snapshot untouched");

        // Each failure must be rejected before invoking even the first link resolver.
        void Reject(Action<JsonObject> corrupt, string label)
        {
            var node = JsonNode.Parse(original)!.AsObject();
            corrupt(node);
            string before = Json(restored.CaptureState(key, RestoredId));
            int callbacks = 0;
            bool rejected = false;
            try { restored.RestoreState(Read(node.ToJsonString()), key, id => { callbacks++; return Resolve(id); }); }
            catch (ArgumentException) { rejected = true; }
            catch (JsonException) { rejected = true; }
            check(rejected && callbacks == 0 && before == Json(restored.CaptureState(key, RestoredId)),
                "RSE corrupt " + label + ": rejected without mutation/callback");
        }
        Reject(n => n["Version"] = 99, "version");
        Reject(n => n["ProgramKey"] = "another/revision", "program key");
        foreach (string name in new[] { "VariableCount", "StackSize", "WalkCapacity", "BounceCapacity", "LimboCapacity", "SliceBudget", "CodeWords" })
            Reject(n => n[name] = -1, name);
        foreach (string name in new[] { "Variables", "Stack", "Walks", "Bounce", "Limbo" })
        {
            Reject(n => n[name] = null, name + " null");
            Reject(n => n[name] = new JsonArray(), name + " size");
        }
        Reject(n => n["Pc"] = 1, "PC in operand");
        Reject(n => n["Stack"]![state.CallTop] = 1, "call return in operand");
        Reject(n => n["CallTop"] = -1, "negative call top");
        Reject(n => n["GuestTop"] = state.StackSize, "overlapping stack tops");
        Reject(n => n["Yield"] = 99, "yield");
        Reject(n => n["Time"] = -1, "time");
        Reject(n => n["Time"] = (long)uint.MaxValue + 1, "time overflow");
        Reject(n => n["Name"] = null, "name");
        Reject(n => n["Walks"]![0]!["State"] = 5, "walk state");
        Reject(n => n["Walks"]![0]!["Angle"] = -1, "walk angle");
        Reject(n => n["ChildId"] = " ", "link ID");
        Reject(n => n["RandomKind"] = 99, "random marker");
        Reject(n => n["Random"] = null, "random missing");
        Reject(n => n["Random"]!["Inext"] = -1, "random cursor");
        Reject(n => n["Random"]!["SeedArray"]![55] = -1, "random array");
        Reject(n => n["UnrecognizedState"] = 1, "unknown member");
        foreach (string name in JsonNode.Parse(original)!.AsObject().Select(p => p.Key))
            Reject(n => n.Remove(name), "required " + name);
        Reject(n => n["Walks"]![0]!.AsObject().Remove("End"), "required nested walk member");

        string beforeLinks = Json(restored.CaptureState(key, RestoredId));
        bool missing = false;
        try { restored.RestoreState(state, key, id => id == "sound" ? null! : Resolve(id)); }
        catch (ArgumentException) { missing = true; }
        check(missing && beforeLinks == Json(restored.CaptureState(key, RestoredId)), "RSE unresolved second link is atomic");
        bool threw = false;
        try { restored.RestoreState(state, key, _ => throw new InvalidOperationException("lookup failed")); }
        catch (InvalidOperationException) { threw = true; }
        check(threw && beforeLinks == Json(restored.CaptureState(key, RestoredId)), "RSE throwing resolver is atomic");
        bool duplicate = false;
        try { restored.RestoreState(state, key, _ => restoredChild); }
        catch (ArgumentException) { duplicate = true; }
        check(duplicate && beforeLinks == Json(restored.CaptureState(key, RestoredId)), "RSE ambiguous resolver identities rejected");

        // Nullable lazy heads, animation cursors, stale stack cells, faults and critical locks.
        var fresh = new RseMachine(program);
        var freshState = Read(Json(fresh.CaptureState(key, null!)));
        fresh.RestoreState(freshState, key, null!);
        check(freshState.Heads == null && fresh.CaptureState(key, null!).Heads == null, "RSE null heads stay lazy without host access");
        var stale = Read(original);
        stale.Stack[3] = unchecked((int)0xdeadbeef);
        restored.RestoreState(stale, key, Resolve);
        check(restored.CaptureState(key, RestoredId).Stack[3] == stale.Stack[3], "RSE unused full stack cells survive");

        var animBuilder = new Script();
        animBuilder.Emit(RseOpcode.TRIGWAITANIM, 5, 2, V(0));
        animBuilder.Emit(RseOpcode.WAIT4ANIM);
        animBuilder.Emit(RseOpcode.BRANCH, 0x20000000);
        var animProgram = animBuilder.Build(budget: 1);
        var animHost = new Host();
        var anim = new RseMachine(animProgram, animHost);
        anim.RunSlice(20);
        var animState = Read(Json(anim.CaptureState(key, null!)));
        check(animState.TriggerSlot == 5 && animState.AnimationUntil == 720, "RSE pending animation trigger/deadline populated");
        // The host belongs to the caller: hydrate its slot explicitly, not through VM restore.
        var animHost2 = new Host { Slot = animHost.Slot };
        var anim2 = new RseMachine(animProgram, animHost2);
        anim2.RestoreState(animState, key, null!);
        check(animHost2.Events.Count == 0, "RSE animation restore does not replay animation");
        foreach (int t in new[] { 30, 40, 300, 720, 900 })
        {
            anim.RunSlice(t); anim2.RunSlice(t);
            check(Json(anim.CaptureState(key, null!)) == Json(anim2.CaptureState(key, null!)), "RSE animation continuation " + t);
        }
        var faultBuilder = new Script();
        faultBuilder.Emit(RseOpcode.CRIT_LOCK);
        faultBuilder.Emit(RseOpcode.RETURN);
        var fault = new RseMachine(faultBuilder.Build());
        try { fault.RunSlice(7); } catch (InvalidOperationException) { }
        var faultState = Read(Json(fault.CaptureState(key, null!)));
        var fault2 = new RseMachine(fault.Program);
        fault2.RestoreState(faultState, key, null!);
        check(fault2.Fault != null && fault2.Critical && Json(faultState) == Json(fault2.CaptureState(key, null!)),
            "RSE fault text and critical lock preserved");
        bool remainsFaulted = false;
        try { fault2.RunSlice(8); } catch (InvalidOperationException) { remainsFaulted = true; }
        check(remainsFaulted, "RSE restored fault remains faulted");

        // An injected stream is intentionally NOT guessed from seed 1 or serialized as a delegate.
        var rng = new SnapshotRandom(73);
        var external = new RseMachine(animProgram, new Host(), rng.Next);
        external.RunSlice(20);
        var externalState = Read(Json(external.CaptureState(key, null!)));
        check(externalState.RandomKind == RseMachine.RandomStateKind.ExternalCallerManaged && externalState.Random == null,
            "RSE external stream explicitly marked, no pretend owned state");
        var externalRng2 = SnapshotRandom.FromState(rng.CaptureState());
        var external2 = new RseMachine(animProgram, new Host(), externalRng2.Next);
        string externalBefore = Json(external2.CaptureState(key, null!));
        bool refused = false;
        try { external2.RestoreState(externalState, key, null!); } catch (NotSupportedException) { refused = true; }
        check(refused && externalBefore == Json(external2.CaptureState(key, null!)), "RSE external RNG requires caller acknowledgement before mutation");
        external2.RestoreState(externalState, key, null!, externalRandomRestored: true);
        check(Json(externalState) == Json(external2.CaptureState(key, null!)), "RSE separately persisted external stream accepted explicitly");
        bool wrongOwner = false;
        try { anim2.RestoreState(externalState, key, null!, externalRandomRestored: true); }
        catch (ArgumentException) { wrongOwner = true; }
        check(wrongOwner, "RSE external snapshot cannot silently replace owned RNG");
    }

    static uint V(int index) => 0x40000000u | (uint)index;

    static RseProgram Fixture()
    {
        var s = new Script();
        s.Emit(RseOpcode.NAME, 0x10000000);
        s.Emit(RseOpcode.SPAWNCHILD, 0x10000005);
        s.Emit(RseOpcode.SPAWNSOUND, 0x1000000b);
        s.Emit(RseOpcode.COPY, V(0), 7);
        s.Emit(RseOpcode.RAND, V(1), 30000);
        s.Emit(RseOpcode.HUSH, 202); s.Emit(RseOpcode.HUSH, 101);
        s.Emit(RseOpcode.SETTIMER, 900);
        s.Emit(RseOpcode.WALKON, 101, 1, 2, 3, 4, 4, 9);
        s.Emit(RseOpcode.BOUNCESETNODE, 9); s.Emit(RseOpcode.BOUNCESETBASE, 13);
        s.Emit(RseOpcode.BOUNCE, 202, 3); s.Emit(RseOpcode.LIMBO, 303, 2);
        s.Emit(RseOpcode.SPARK, 8, 9, 0, 0); s.Emit(RseOpcode.TURBO, 19);
        s.Emit(RseOpcode.BUMP, 17, 77); s.Emit(RseOpcode.WALKST_FLOAT, 3, 4, 5);
        s.Emit(RseOpcode.ADDHEAD, 404); s.Emit(RseOpcode.LOOPANIM, 2, 3);
        int call = s.Emit(RseOpcode.JSR, 0);
        int loop = s.Position;
        s.Emit(RseOpcode.RAND, V(1), 30000); s.Emit(RseOpcode.HOP, V(2));
        s.Emit(RseOpcode.WALKOFF, V(2)); s.Emit(RseOpcode.WAIT, 60);
        s.Emit(RseOpcode.WALKGET, V(3)); s.Emit(RseOpcode.UNLIMBO, V(4));
        s.Emit(RseOpcode.FORCEUNBOUNCE, V(5)); s.Emit(RseOpcode.GETTIMER, V(6));
        s.Emit(RseOpcode.SETVARINCHILD, 0, V(1)); s.Emit(RseOpcode.GETVARINCHILD, V(7), 0);
        s.Emit(RseOpcode.ENDSLICE); s.Emit(RseOpcode.BRANCH, 0x20000000u | (uint)loop);
        s.Patch(call + 1, 0x20000000u | (uint)s.Position);
        s.Emit(RseOpcode.RAND, V(1), 30000); s.Emit(RseOpcode.WAIT, 100); s.Emit(RseOpcode.RETURN);
        return s.Build();
    }

    // Same little-endian RSSE layout consumed by RseAudit/RseProgram. No archive/disc input.
    // Existing RseAudit obtains images from disc; this small writer keeps this check standalone.
    sealed class Script
    {
        readonly List<uint> words = new();
        public int Position => words.Count;
        public int Emit(RseOpcode opcode, params uint[] operands)
        {
            int pc = Position; words.Add(0x80000000u | (uint)opcode); words.AddRange(operands); return pc;
        }
        public void Patch(int word, uint value) => words[word] = value;
        public RseProgram Build(int budget = 1000)
        {
            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream, Encoding.ASCII, true);
            w.Write("RSSE"u8); w.Write(0x10f51); w.Write(8); w.Write(16); w.Write(budget);
            w.Write(2); w.Write(2); w.Write(2); w.Write("Pad Pad Pad Pad "u8);
            w.Write(words.Count); foreach (uint word in words) w.Write(word);
            byte[] strings = Encoding.ASCII.GetBytes("main\0child\0sound\0");
            w.Write(strings.Length); w.Write(strings);
            for (int i = 0; i < 8; i++) { byte[] name = Encoding.ASCII.GetBytes($"v{i}\0"); w.Write(name.Length); w.Write(name); }
            return new RseProgram(stream.ToArray());
        }
    }

    sealed class Host : IRseHost
    {
        public readonly List<string> Events = new();
        public int Slot = -1;
        public int HeadSlots { get { Events.Add("head-slots"); return 5; } }
        public int AnimationSlot => Slot;
        public void AdvanceTo(long milliseconds) => Events.Add("time:" + milliseconds);
        public int PlayAnimation(int slot, int variant, bool loop)
        { Slot = slot; Events.Add($"animation:{slot}:{variant}:{loop}"); return 1000; }
        public int PlayAnimationOn(int channel, int slot, int variant, bool loop) => PlayAnimation(slot, variant, loop);
        public int PlayAnimationSpeed(int slot, int variant, int speedPerMille) => PlayAnimation(slot, variant, false);
        public void FlushAnimation() => Events.Add("flush");
        public bool TryEffect(RseOpcode opcode, IReadOnlyList<int> arguments) { Events.Add("effect:" + opcode); return true; }
        public bool TryNodePosition(int node, int space, out float x, out float y, out float z)
        { x = node / 10f; y = 0; z = 0; return true; }
        public void WalkerPose(int guest, int fromNode, int toNode, int mode, int perMille, int angle) =>
            Events.Add($"walk:{guest}:{fromNode}:{toNode}:{mode}:{perMille}:{angle}");
        public int AnimationRemainingOn(int channel) => -1;
        public void HeadAt(int slot, int guest) => Events.Add($"head:{slot}:{guest}");
        public void GuestVisible(int guest, bool visible) => Events.Add($"visible:{guest}:{visible}");
    }
}
