using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;
using Snapshot = TPW.PS2.Data.StaffMember.Snapshot;

/// <summary>Disc-free member checks. Continuation uses real independently constructed ParkStaff
/// pools/lists and shared GuestWalk resources, not fabricated staff owners or private reflection.</summary>
public static class StaffMemberSaveChecks
{
    static string Json<T>(T value) => JsonSerializer.Serialize(value);
    static T Round<T>(T value) => JsonSerializer.Deserialize<T>(Json(value))!;
    sealed class World
    {
        internal readonly ParkStaff Staff;
        internal readonly GuestWalk Walk;
        internal readonly ParkPaths Paths;
        internal readonly StaffMember[] Members;
        internal readonly object DeferredTarget = new();
        internal int Draws, Events;
        internal World(World? source = null, bool hire = false)
        {
            Paths = source == null ? new ParkPaths(Terrain())
                : ParkPaths.FromState(Round(source.Paths.CaptureState("terrain")), "terrain", Terrain());
            Walk = source == null ? new GuestWalk(Paths)
                : GuestWalk.FromState(Round(source.Walk.CaptureState("grid")), "grid", Paths).Walk;
            Staff = new ParkStaff(new ParkVisitors(new ParkSim(Paths), Walk), new ParkClock(),
                new NativeActivationSequence(0, "member-save-check"), _ => { Draws++; return 0; });
            Walk.Paused = null;
            // Explicitly stage candidate identities outside member restore (no lazy random draws).
            _ = Staff.Candidates;
            var kinds = new[] { StaffKind.Handyman, StaffKind.Mechanic, StaffKind.Researcher };
            Members = kinds.Select(k => hire ? Staff.Hire(k, 1) : Staff.Free(k)[0]).ToArray();
            if (hire)
                foreach (var m in Members)
                    if (!Staff.Drop(m, new NativeGuestMotion.Point(384, 384)))
                        throw new InvalidOperationException("fixture drop rejected");
            Staff.Sound = (_, _, _) => Events++;
            Staff.HandleSound = (_, _, _, _) => Events++;
            Staff.Advisor = _ => Events++;
        }
        internal string Key(object value)
        {
            if (ReferenceEquals(value, DeferredTarget)) return "deferred/world/project";
            if (value is StaffCandidate c && ReferenceEquals(c, Staff.Candidates.For(c.Kind, c.Slot)))
                return $"candidate/{(int)c.Kind}/{c.Slot}";
            throw new ArgumentException("unknown fixture object");
        }
        internal object Resolve(string key)
        {
            if (key == "deferred/world/project") return DeferredTarget;
            var p = key.Split('/');
            if (p.Length == 3 && p[0] == "candidate")
                return Staff.Candidates.For((StaffKind)int.Parse(p[1]), int.Parse(p[2]));
            throw new ArgumentException("unknown fixture key");
        }
        internal string Save() => Json(Members.Select(m => m.CaptureState(Key)).ToArray());
        internal string Pool() => Json(Staff.Routes.CaptureState());
    }

    public static int Run()
    {
        int count = 0;
        void C(bool ok, string why)
        { if (!ok) throw new InvalidOperationException("staff member save: " + why); count++; }
        var source = new World();
        var copy = new World(source);
        for (int i = 0; i < source.Members.Length; i++)
        {
            var member = source.Members[i];
            var s = member.CaptureState(source.Key) with
            {
                Active = false, Serial = uint.MaxValue, GoalDepth = 2, Goals = new byte[] { 11, 12, 99, 255 },
                PositionX = -321, PositionZ = 999, TargetKey = "deferred/world/project", Stamp = uint.MaxValue,
                RouteEpoch = 42, Flags = 0xFFFF, Mode = 0xFE, State = 0xFD, LogicalRequest = 18,
                FacingQuarterTurns = 3, CandidateKey = $"candidate/{(int)member.Kind}/2", CandidateSlot = 2,
                HireDay = 89, PatrolX0 = -128, PatrolZ0 = 7, PatrolX1 = 127, PatrolZ1 = -4,
                Level = 4, SpeedBits = 31, MotivationCopy = 78, Tiredness = -20, Morale = 81,
                WaypointsRetired = 1234, BadStates = 345,
                Mechanic = member is Mechanic ? new Mechanic.JobSnapshot
                    { Version = 1, RepairDispatches = 23, InstallDispatches = 17 } : null,
                Researcher = member is Researcher ? new Researcher.JobSnapshot { Version = 1, Quanta = 6789 } : null
            };
            var loaded = Round(s);
            int draws = copy.Draws, events = copy.Events;
            string pool = copy.Pool();
            copy.Staff.Features = () => throw new InvalidOperationException("provider invoked by restore");
            copy.Staff.AnimationReady = _ => throw new InvalidOperationException("animation invoked by restore");
            copy.Members[i].ValidateSnapshot(loaded, copy.Resolve);
            copy.Members[i].RestoreState(loaded, copy.Resolve);
            C(Json(s) == Json(copy.Members[i].CaptureState(copy.Key)), "exact detached stale slot/job " + i);
            C(draws == copy.Draws && events == copy.Events && pool == copy.Pool(), "no gameplay callbacks/draws/route mutation");
            C(ReferenceEquals(copy.Members[i].Target, copy.DeferredTarget)
                && !ReferenceEquals(source.DeferredTarget, copy.Members[i].Target), "deferred identity rebound");
            C(copy.Members[i].ReferencedTargets.Single() == copy.DeferredTarget
                && copy.Members[i].ReferencedSnapshotObjects.Count == 2, "inventory includes stale refs");
            loaded.Goals[3] = 0;
            C(copy.Members[i].Goals[3] == 255, "restore detached goal array");
            var captured = copy.Members[i].CaptureState(copy.Key);
            captured.Goals[0] = 0;
            C(copy.Members[i].Goals[0] == 11, "capture detached goal array");

            void Bad(Snapshot bad, string why, Func<string, object>? resolver = null)
            {
                string before = copy.Save(), beforePool = copy.Pool(); bool rejected = false;
                try { copy.Members[i].RestoreState(bad, resolver ?? copy.Resolve); }
                catch (ArgumentException) { rejected = true; }
                C(rejected && before == copy.Save() && beforePool == copy.Pool(), "atomic reject " + why);
            }
            Bad(s with { Version = 99 }, "version");
            Bad(s with { PoolSlot = 0 }, "identity");
            Bad(s with { GoalDepth = 5 }, "depth");
            Bad(s with { Goals = new byte[3] }, "goal shape");
            Bad(s with { SpeedBits = 32 }, "packed bits");
            Bad(s with { CandidateKey = "candidate/0/0" }, "candidate key mismatch");
            Bad(s with { TargetKey = " " }, "target key");
            Bad(s, "late resolver failure", key => key.StartsWith("candidate/")
                ? throw new ArgumentException("missing candidate") : copy.Resolve(key));
            if (s.Mechanic != null) Bad(s with { Mechanic = s.Mechanic with { Version = 2 } }, "job schema");
            if (s.Researcher != null) Bad(s with { Researcher = s.Researcher with { Quanta = -1 } }, "job counter");
            foreach (var key in JsonNode.Parse(Json(s))!.AsObject().Select(p => p.Key).ToArray())
            {
                var node = JsonNode.Parse(Json(s))!.AsObject(); node.Remove(key); bool rejected = false;
                try { JsonSerializer.Deserialize<Snapshot>(node.ToJsonString()); }
                catch (JsonException) { rejected = true; }
                C(rejected, "required JSON field " + key);
            }
        }

        // Real route cursor at a nontrivial waypoint in the shared pool. The independently
        // repeated hires stage ONLY the surrounding graph; member restore itself never hires.
        var walking = new World(hire: true);
        var a = walking.Members[0];
        C(walking.Staff.RouteRequests.Submit(a, a.Position, new(901, 777), 3), "route admission");
        walking.Staff.RouteRequests.Pump();
        C(a.RouteSlot >= 0, "real built route");
        var continued = new World(walking, hire: true);
        string originalPool = continued.Pool();
        for (int i = 0; i < walking.Members.Length; i++)
            continued.Members[i].RestoreState(Round(walking.Members[i].CaptureState(walking.Key)), continued.Resolve);
        C(walking.Save() == continued.Save() && originalPool == continued.Pool(), "cursor exact, zero output allocation");
        var live = a.CaptureState(walking.Key);
        string beforeRoute = continued.Save(); bool invalidRoute = false;
        try { continued.Members[0].RestoreState(live with
            { Route = live.Route! with { SlotIndex = NativeRoutePool.Capacity } }, continued.Resolve); }
        catch (ArgumentException) { invalidRoute = true; }
        C(invalidRoute && beforeRoute == continued.Save() && originalPool == continued.Pool(), "bad cursor atomic");
        for (int tick = 0; tick < 80; tick++)
        {
            walking.Staff.Update(); continued.Staff.Update();
            C(walking.Save() == continued.Save() && walking.Pool() == continued.Pool()
                && walking.Events == continued.Events && walking.Draws == continued.Draws,
                "actual independent staff continuation " + tick);
        }
        C(walking.Members[0].WaypointsRetired > 0 && ((Researcher)walking.Members[2]).Quanta > 0,
            "continuation retires real route and runs research job");
        return count;
    }

    // Authored public Model bytes; no disc/private assets.
    internal static Model Terrain()
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
