using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;
using Service = TPW.PS2.Data.StaffRouteService;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

/// <summary>Disc-free bounded pending-service checks, NOT a StaffMember save implementation.
/// Owners below are independently constructed same-state free pool members. Their state is
/// deliberately not restored by the service; existing live cursors need a future owner save.</summary>
public static class StaffRouteServiceSaveChecks
{
    static string Json<T>(T value) => JsonSerializer.Serialize(value);
    static T Round<T>(T value) => JsonSerializer.Deserialize<T>(Json(value))!;
    sealed class World
    {
        internal ParkPaths Paths;
        internal GuestWalk Walk;
        internal ParkStaff Staff;
        internal StaffMember A, B;
        internal Service Service => Staff.RouteRequests;
        internal Service.SnapshotBindings Bind => new()
        {
            PathsKey = "staff/grid", TilesKey = "staff/tiles-and-provider", PoolKey = "shared/routes",
            Staff = Staff, Owners = new Dictionary<string, StaffMember>(StringComparer.Ordinal)
            { ["owner/alpha"] = A, ["owner/beta"] = B }
        };
        internal World(World? source = null)
        {
            Paths = source == null ? new ParkPaths(Terrain())
                : ParkPaths.FromState(Round(source.Paths.CaptureState("terrain")), "terrain", Terrain());
            Walk = source == null ? new GuestWalk(Paths)
                : GuestWalk.FromState(Round(source.Walk.CaptureState("staff/grid")), "staff/grid", Paths).Walk;
            Staff = new ParkStaff(new ParkVisitors(new ParkSim(Paths), Walk), new ParkClock(), new NativeActivationSequence(0, "staff-save-check"), _ => 0);
            A = Staff.Free(StaffKind.Handyman)[0]; B = Staff.Free(StaffKind.Mechanic)[0];
            // Empty guest fixture: no watching controller participates in these Pump-only checks.
            Walk.Paused = null;
        }
        internal string OwnerState() => Json(new[] { A, B }.Select(o => new
        { o.State, o.Mode, o.GoalDepth, o.RouteSlot, o.Position, o.Flags, o.Stamp, o.WaypointsRetired }));
        internal string PoolState() => Json(Walk.NativeRoutes.CaptureState());
        internal string Save() => Json(Service.CaptureState(Bind));
    }

    public static int Run()
    {
        int count = 0;
        void C(bool ok, string why)
        { if (!ok) throw new InvalidOperationException("staff route save: " + why); count++; }
        var source = new World();
        // Ten actual admissions, newest first, multiple owners AND duplicate-owner requests.
        for (int i = 0; i < Service.Records; i++)
            C(source.Service.Submit(i % 2 == 0 ? source.A : source.B,
                new Point(-17, 401), new Point((short)(780 + i), 901), i == 9 ? 0 : 0x03), "admit");
        C(!source.Service.Submit(source.A, new(0, 0), new(0, 0), 0), "eleventh refused");
        var snapshot = source.Service.CaptureState(source.Bind);
        C(snapshot.Pending.Length == 10 && snapshot.Refused == 1 && snapshot.Admitted == 10,
            "capacity and counters");
        C(snapshot.Pending[0].OwnerKey == "owner/beta" && snapshot.Pending[0].TargetX == 789
            && snapshot.Pending[0].FromX == 0 && snapshot.Pending[0].FromZ == 1
            && snapshot.Pending[0].GoalX == 3 && snapshot.Pending[0].GoalZ == 3,
            "head order, clamped stored start, exact goal/target");
        C(source.Service.ReferencedOwners.SequenceEqual(new[] { source.B, source.A }), "distinct reference inventory");
        string file = Path.Combine(Path.GetTempPath(), "staff-route-" + Guid.NewGuid().ToString("N") + ".json");
        Service.Snapshot loaded;
        try
        {
            File.WriteAllText(file, Json(snapshot));
            loaded = JsonSerializer.Deserialize<Service.Snapshot>(File.ReadAllText(file))!;
        }
        finally { File.Delete(file); }
        var restored = new World(source);
        string beforeOwner = restored.OwnerState(), beforePool = restored.PoolState();
        restored.Service.RestoreState(loaded, restored.Bind);
        C(beforeOwner == restored.OwnerState() && beforePool == restored.PoolState(), "restore replays no events or allocations");
        C(source.Save() == restored.Save() && !ReferenceEquals(source.B, restored.Service.Next)
            && ReferenceEquals(restored.B, restored.Service.Next), "JSON file roundtrip and owner rebinding");
        Array.Reverse(loaded.Pending);
        C(source.Save() == restored.Save(), "detached request order");
        for (int tick = 0; tick < 10; tick++)
        {
            source.Service.Pump(); restored.Service.Pump();
            C(source.Save() == restored.Save() && source.OwnerState() == restored.OwnerState()
                && source.PoolState() == restored.PoolState(), "real next Pump continuation " + tick);
            C(restored.Service.Pending == 9 - tick, "one request per tick");
        }
        C(restored.Service.Unreachable == 1 && restored.Service.Built == 9, "real success/failure events and counters");
        C(restored.A.State == StaffMember.StateWalk && restored.A.RouteSlot >= 0, "built route adopted by same bound pool");

        // Output exhaustion is a real Pump outcome, with pool values restored externally.
        var full = new World();
        for (int i = 0; i < NativeRoutePool.Capacity; i++) full.Walk.NativeRoutes.Allocate();
        full.Service.Submit(full.B, new(300, 300), new(900, 900), 3);
        var fullCopy = new World(full);
        fullCopy.Service.RestoreState(Round(full.Service.CaptureState(full.Bind)), fullCopy.Bind);
        full.Service.Pump(); fullCopy.Service.Pump();
        C(full.Service.OutputExhausted == 1 && full.Save() == fullCopy.Save()
            && full.PoolState() == fullCopy.PoolState() && full.OwnerState() == fullCopy.OwnerState(), "output exhaustion continuation");

        // Empty queues retain nonzero history; restoring the service does not touch live owner routes.
        string history = restored.Save(), routedOwners = restored.OwnerState(), routedPool = restored.PoolState();
        restored.Service.RestoreState(Round(restored.Service.CaptureState(restored.Bind)), restored.Bind);
        restored.Service.Pump();
        C(history == restored.Save() && routedOwners == restored.OwnerState() && routedPool == restored.PoolState(),
            "empty queue nonzero history roundtrip and no-op Pump");
        string exhaustedHistory = fullCopy.Save();
        fullCopy.Service.RestoreState(Round(fullCopy.Service.CaptureState(fullCopy.Bind)), fullCopy.Bind);
        C(exhaustedHistory == fullCopy.Save(), "nonzero exhausted counter roundtrip");

        var target = new World();
        target.Service.Submit(target.A, new(200, 200), new(500, 500), 3);
        void Bad(Service.Snapshot bad, string why, Service.SnapshotBindings? bindings = null)
        {
            string service = target.Save(), owners = target.OwnerState(), pool = target.PoolState();
            bool rejected = false;
            try { target.Service.RestoreState(bad, bindings ?? target.Bind); }
            catch (ArgumentException) { rejected = true; }
            C(rejected && service == target.Save() && owners == target.OwnerState() && pool == target.PoolState(),
                "reject without mutation: " + why);
        }
        Bad(snapshot with { Version = 999 }, "version");
        Bad(snapshot with { PathsKey = "wrong" }, "paths key");
        Bad(snapshot with { TilesKey = "wrong" }, "tile provider key");
        Bad(snapshot with { PoolKey = "wrong" }, "pool key");
        Bad(snapshot with { Pending = null! }, "null table");
        Bad(snapshot with { Pending = Enumerable.Repeat(snapshot.Pending[0], 11).ToArray() }, "eleven rows");
        Bad(snapshot with { Refused = -1 }, "negative counter");
        Bad(snapshot with { Admitted = 9 }, "counter conservation");
        void Row(Service.RequestSnapshot row, string why) => Bad(snapshot with
        { Pending = new[] { row }.Concat(snapshot.Pending.Skip(1)).ToArray() }, why);
        Row(snapshot.Pending[0] with { OwnerKey = "missing" }, "missing owner");
        Row(snapshot.Pending[0] with { OwnerKey = "OWNER/BETA" }, "ordinal identity");
        Row(snapshot.Pending[0] with { FromX = -1 }, "start bounds");
        Row(snapshot.Pending[0] with { GoalZ = 8 }, "goal bounds");
        Row(snapshot.Pending[0] with { TargetX = 1 }, "goal/target mismatch");
        Row(null!, "null row");
        Bad(snapshot, "wrong resource instances", target.Bind with { Staff = new World().Staff });
        Bad(snapshot, "foreign owner", target.Bind with { Owners = new Dictionary<string, StaffMember>
        { ["owner/alpha"] = new World().A, ["owner/beta"] = target.B } });
        Bad(snapshot, "owner aliases", target.Bind with { Owners = new Dictionary<string, StaffMember>
        { ["owner/alpha"] = target.A, ["owner/beta"] = target.A } });

        // No invented restrictions on Submit flags or exact endpoints outside the map.
        var edge = new World();
        edge.Service.Submit(edge.A, new(short.MinValue, short.MaxValue), new(short.MaxValue, -23), int.MinValue);
        var edgeState = Round(edge.Service.CaptureState(edge.Bind));
        edge.Service.RestoreState(edgeState, edge.Bind);
        C(edgeState.Pending[0].TargetX == short.MaxValue && edgeState.Pending[0].GoalX == 7
            && edgeState.Pending[0].GoalZ == 0 && edgeState.Pending[0].Flags == int.MinValue, "full runtime flags/target domain");
        void Required(JsonObject obj, bool row)
        {
            foreach (string key in obj.Select(p => p.Key).ToArray())
            {
                var missing = obj.DeepClone().AsObject(); missing.Remove(key); bool rejected = false;
                try
                {
                    if (row) JsonSerializer.Deserialize<Service.RequestSnapshot>(missing.ToJsonString());
                    else JsonSerializer.Deserialize<Service.Snapshot>(missing.ToJsonString());
                }
                catch (JsonException) { rejected = true; }
                C(rejected, "required JSON field " + key);
            }
            obj["Unknown"] = 0;
            bool unknown = false;
            try
            {
                if (row) JsonSerializer.Deserialize<Service.RequestSnapshot>(obj.ToJsonString());
                else JsonSerializer.Deserialize<Service.Snapshot>(obj.ToJsonString());
            }
            catch (JsonException) { unknown = true; }
            C(unknown, "unknown JSON field");
        }
        Required(JsonNode.Parse(Json(snapshot))!.AsObject(), false);
        Required(JsonNode.Parse(Json(snapshot.Pending[0]))!.AsObject(), true);
        return count;
    }

    // Authored public Model bytes; no private assets/disc extraction.
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
