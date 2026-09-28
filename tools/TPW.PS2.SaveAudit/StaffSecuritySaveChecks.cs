using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

/// <summary>Disc-free security graph and real guard/entertainer continuation checks.</summary>
public static class StaffSecuritySaveChecks
{
    static string Json<T>(T value) => JsonSerializer.Serialize(value);
    static T Round<T>(T value) => JsonSerializer.Deserialize<T>(Json(value))!;
    sealed class World
    {
        internal readonly ParkStaff Staff;
        internal readonly Guard Guard;
        internal readonly Entertainer Entertainer;
        internal int Draws, Events;
        readonly Dictionary<string, object> bindings = new();
        internal World()
        {
            var paths = new ParkPaths(Terrain());
            Staff = new ParkStaff(new ParkVisitors(new ParkSim(paths), new GuestWalk(paths)),
                new ParkClock(), new NativeActivationSequence(0, "security-test"), n => { Draws++; return 0; });
            Guard = (Guard)Staff.Hire(StaffKind.Guard, 1);
            Entertainer = (Entertainer)Staff.Hire(StaffKind.Entertainer, 1);
            Staff.Drop(Guard, new NativeGuestMotion.Point(384, 384)); Staff.Drop(Entertainer, new NativeGuestMotion.Point(384, 384));
            Staff.Sound = (_, _, _) => Events++;
            Staff.HandleSound = (_, _, _, _) => Events++;
            Staff.Stinks.Started = _ => Events++;
            Staff.Stinks.Stopped = _ => Events++;
            Staff.Heckled = (_, _) => Events++;
            Staff.GuestCaught = (_, _) => Events++;
            Staff.AnimationState = _ => (16, 1);
            bindings.Add("started", Staff.Stinks.Started); bindings.Add("stopped", Staff.Stinks.Stopped);
            bindings.Add("heckled", Staff.Heckled); bindings.Add("caught", Staff.GuestCaught);
            bindings.Add("animation", Staff.AnimationState);
        }
        internal string Key(object o)
        {
            foreach (var p in bindings) if (ReferenceEquals(p.Value, o)) return p.Key;
            if (o is GuestTarget g) return "guest/" + g.Id;
            if (o is StaffCandidate c) return $"candidate/{(int)c.Kind}/{c.Slot}";
            throw new ArgumentException("fixture identity");
        }
        internal object Resolve(string key)
        {
            if (bindings.TryGetValue(key, out var value)) return value;
            var p = key.Split('/');
            if (p[0] == "guest") return new GuestTarget(int.Parse(p[1]));
            if (p[0] == "candidate") return Staff.Candidates.For((StaffKind)int.Parse(p[1]), int.Parse(p[2]));
            throw new ArgumentException("fixture binding");
        }
        internal string Save() => Json(Staff.CaptureSecurityState(Key));
        internal string Pool() => Json(Staff.Routes.CaptureState());
    }
    public static int Run()
    {
        int count = 0;
        void C(bool ok, string why) { if (!ok) throw new InvalidOperationException("security save: " + why); count++; }
        var a = new World(); var b = new World();
        a.Staff.Stinks.Add(1, 2); a.Staff.Stinks.Add(3, 4); a.Staff.Stinks.Remove(1, 2);
        a.Staff.Stinks.Add(3, 4); // refused, no draw
        var state = Round(a.Staff.CaptureSecurityState(a.Key));
        var slots = state.Effectors.Slots.ToArray();
        slots[19] = slots[19] with { Active = true, X = 1, Z = 1, Radius2 = 1, Flags = 2 };
        var jobs = state.Entertainers.ToArray();
        jobs[a.Entertainer.PoolSlot] = jobs[a.Entertainer.PoolSlot] with { EffectorSlot = 19, Shows = 4 };
        var guards = state.Guards.ToArray();
        guards[a.Guard.PoolSlot] = guards[a.Guard.PoolSlot] with { Leg = 1, Catches = 7,
            Carried = new ParkStaff.CarriedGuestSnapshot { GuestId = 42, Serial = 91, X = 124, Z = -256, Facing = 3 } };
        state = state with { Effectors = state.Effectors with { Slots = slots, Free = Enumerable.Range(0, 19).Reverse().ToArray(), Active = new[] { 19 } },
            Entertainers = jobs, Guards = guards, CopySerial = 91, Pranks = 12,
            WatchingLayout=new(){Slots=new int?[]{77},FreeBottomFirst=Array.Empty<int>()},
            CooldownLayout=new(){Slots=new int?[]{77},FreeBottomFirst=Array.Empty<int>()},
            FirstSeenLayout=new(){Slots=new int?[]{77},FreeBottomFirst=Array.Empty<int>()},
            Watching = new[] { new ParkStaff.WatchSnapshot { GuestId = 77, EntertainerSlot = a.Entertainer.PoolSlot, Until = 1234, Facing = 2 } },
            WatchCooldown = new[] { new ParkStaff.GuestTimeSnapshot { GuestId = 77, Tick = 33 } },
            FirstSeen = new[] { new ParkStaff.GuestTimeSnapshot { GuestId = 77, Tick = 2 } }, LiveTargets = new[] { 42 } };
        var shell = b.Staff.Effectors.SnapshotSlot(19);
        int draws = b.Draws, events = b.Events; string pool = b.Pool();
        b.Staff.ValidateSecuritySnapshot(state, b.Resolve);
        b.Staff.RestoreSecurityState(Round(state), b.Resolve);
        C(Json(state) == b.Save(), "exact JSON round trip");
        C(draws == b.Draws && events == b.Events && pool == b.Pool(), "no replay, RNG or pool allocation");
        C(ReferenceEquals(shell, b.Entertainer.Effector) && ReferenceEquals(b.Staff.Watching[77].Entertainer, b.Entertainer), "slot identity");
        C(b.Guard.Carried.GuestId == 42 && b.Guard.Carried.Position.Z == -256, "carried model state");
        var detached = b.Staff.CaptureSecurityState(b.Key); detached.AdvisorEvents[0] = 13; detached.LiveTargets[0] = 88;
        C(Json(state) == b.Save(), "capture detached arrays");
        void Bad(ParkStaff.SecuritySnapshot bad, string why, Func<string, object>? resolve = null)
        {
            string before = b.Save(); bool rejected = false;
            try { b.Staff.RestoreSecurityState(bad, resolve ?? b.Resolve); } catch (ArgumentException) { rejected = true; }
            C(rejected && before == b.Save() && pool == b.Pool() && b.Events == events && b.Draws == draws, "atomic reject " + why);
        }
        Bad(state with { Version = 2 }, "version");
        Bad(state with { AdvisorEvents = new short[21] }, "advisor shape");
        Bad(state with { Effectors = state.Effectors with { Free = new int[19] } }, "partition");
        Bad(state with { LiveTargets = new[] { 1, 1 } }, "duplicate live ID");
        Bad(state with { Stinks = state.Stinks with { ExternalRandKey = "bad" } }, "RNG ambiguity");
        Bad(state with { GuestCaughtKey = "unknown" }, "late binding");
        var badJobs = state.Entertainers.ToArray(); badJobs[0] = badJobs[0] with { EffectorSlot = 18 };
        Bad(state with { Entertainers = badJobs }, "free effector owner");
        foreach (var key in JsonNode.Parse(Json(state))!.AsObject().Select(p => p.Key).ToArray())
        {
            var node = JsonNode.Parse(Json(state))!.AsObject(); node.Remove(key); bool rejected = false;
            try { JsonSerializer.Deserialize<ParkStaff.SecuritySnapshot>(node.ToJsonString()); } catch (JsonException) { rejected = true; }
            C(rejected, "required field " + key);
        }
        a.Staff.RestoreSecurityState(Round(state), a.Resolve);
        for (int i = 0; i < 6; i++)
        {
            a.Staff.Stinks.Add(10 + i, 20 + i); b.Staff.Stinks.Add(10 + i, 20 + i);
            C(a.Save() == b.Save(), "owned libc stream continuation " + i);
        }
        // Independent same-state ParkStaff owners; actual guard Chase/GiveUp and entertainer
        // Perform/FindWork run, including freeing the restored effector and sounds.
        a.Guard.RestoreState(a.Guard.CaptureState(a.Key) with { State = Guard.StateChase,
            TargetKey = "guest/999", Stamp = 300, Mode = Guard.ModeChase }, a.Resolve);
        a.Entertainer.RestoreState(a.Entertainer.CaptureState(a.Key) with { State = Entertainer.StatePerforming, Stamp = 0 }, a.Resolve);
        b.Guard.RestoreState(Round(a.Guard.CaptureState(a.Key)), b.Resolve);
        b.Entertainer.RestoreState(Round(a.Entertainer.CaptureState(a.Key)), b.Resolve);
        a.Draws = b.Draws = a.Events = b.Events = 0;
        for (int tick = 0; tick < 2; tick++)
        {
            a.Staff.Update(); b.Staff.Update();
            C(a.Save() == b.Save() && a.Pool() == b.Pool() && a.Draws == b.Draws && a.Events == b.Events
                && Json(a.Guard.CaptureState(a.Key)) == Json(b.Guard.CaptureState(b.Key))
                && Json(a.Entertainer.CaptureState(a.Key)) == Json(b.Entertainer.CaptureState(b.Key)), "real job continuation " + tick);
        }
        C(a.Guard.GiveUps == 1 && a.Entertainer.Effector == null && a.Events > 0, "jobs executed, effector freed");
        // External RNG is explicitly a provider binding, never inferred from its seed.
        var x = new PrankStinks(() => 4); var xs = x.CaptureState(_ => "external-rng");
        var y = new PrankStinks(); int calls = 0;
        y.RestoreState(Round(xs), _ => (Func<int>)(() => { calls++; return 4; }));
        C(calls == 0 && xs.OwnedRandState == null && xs.ExternalRandKey != null, "external RNG binding without replay");
        x.Add(1, 1); y.Add(1, 1);
        C(calls == 2 && x.Entries[0].UnitsX == y.Entries[0].UnitsX, "external RNG continuation");
        return count;
    }
    // Authored public Model bytes; no disc/private assets.
    static Model Terrain()
    {
        var data = new byte[0x240];
        void U(int at, uint value) => BitConverter.GetBytes(value).CopyTo(data, at);
        void F(int at, float value) => BitConverter.GetBytes(value).CopyTo(data, at);
        U(0, Model.Magic); U(0x44, 0x140); U(0x48, 0x80); data[0x30] = 1;
        U(0x14c, 8); U(0x150, 8); F(0x158, 1);
        for (int i = 0; i < 64; i++) data[0x170 + i * 2 + 1] = 1;
        U(0xd4, 0x220); System.Text.Encoding.ASCII.GetBytes("heightfield\0").CopyTo(data, 0x220);
        for (int i = 0; i < 4; i++) F(0x90 + i * 20, 1);
        F(0x100, 8 * 1.004f); F(0x108, 8 * 1.004f);
        var result = new Model(data); result.Materials.Add("sentinel"); result.Materials.Add("jpa_str1");
        return result;
    }
}
