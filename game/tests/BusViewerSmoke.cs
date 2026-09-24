using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>
/// Shipping Viewer bus bridge, not a separately constructed controller. Run on a rendering
/// display with normal --disc=... --map=JUNGLE --mode=park startup arguments (also supports
/// FANTASY, HALLOW and SPACE). No screenshots, extracted assets, synthetic guests or cap0.
/// Only the frame scheduler is stopped: every simulated frame uses Viewer's StepPark(.04).
/// </summary>
public partial class BusViewerSmoke : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string name) => typeof(Viewer).GetField(name, Hidden)
        ?? throw new MissingMemberException("Viewer." + name);
    static T Field<T>(Viewer v, string name) => (T)Member(name).GetValue(v);
    static void Set(Viewer v, string name, object value) => Member(name).SetValue(v, value);
    static object Call(Viewer v, string name, params object[] args)
    {
        var method = typeof(Viewer).GetMethod(name, Hidden)
            ?? throw new MissingMemberException("Viewer." + name);
        var parameters = method.GetParameters();
        if (args.Length < parameters.Length)
            args = args.Concat(parameters.Skip(args.Length).Select(p => p.HasDefaultValue ? p.DefaultValue
                : throw new ArgumentException("Missing required argument for Viewer." + name))).ToArray();
        // Do not swallow invocation exceptions: the full inner exception is reported by _Ready.
        return method.Invoke(v, args);
    }
    int _checks;
    void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++;
    }
    static bool Live(Node node) => node != null && IsInstanceValid(node)
        && node.IsInsideTree() && !node.IsQueuedForDeletion();
    static IEnumerable<MeshInstance3D> Meshes(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is MeshInstance3D mesh) yield return mesh;
            foreach (var nested in Meshes(child)) yield return nested;
        }
    }
    static List<(Node3D Node, int RuntimeId, RideDefinition Definition)> Placements(Viewer v)
        => Field<List<(Node3D Node, int RuntimeId, RideDefinition Definition)>>(v, "_busPlacements");
    static IReadOnlyList<NativeBusDemand.Attraction> Objects(Viewer v)
        => (IReadOnlyList<NativeBusDemand.Attraction>)Call(v, "BusObjects");

    public override async void _Ready()
    {
        string world = "unknown";
        try
        {
            Check(DisplayServer.GetName() != "headless", "rendering display required");
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            string map = args.LastOrDefault(a => a.StartsWith("--map="))?["--map=".Length..]
                ?? throw new ArgumentException("Pass normal Viewer --map=WORLD startup argument");
            string mode = args.LastOrDefault(a => a.StartsWith("--mode="))?["--mode=".Length..];
            Check(string.Equals(mode, "park", StringComparison.OrdinalIgnoreCase), "pass --mode=park");
            world = new[] { "JUNGLE", "FANTASY", "HALLOW", "SPACE" }.Single(w =>
                map.Equals(w, StringComparison.OrdinalIgnoreCase)
                || map.StartsWith(w + " ", StringComparison.OrdinalIgnoreCase));
            // Reject other fixtures/capture switches before Viewer._Ready can execute them.
            Check(!args.Any(a => a.StartsWith("--shot=") || a.Contains("-film=")
                || a.StartsWith("--sound-census=") || a.EndsWith("-test")
                || a.EndsWith("-audit") || a == "--ghost-press")
                && string.IsNullOrWhiteSpace(OS.GetEnvironment("TPW_PS2_SHOT")),
                "ordinary startup only; no captures or other test fixtures");

            var viewer = new Viewer { Name = "Viewer" };
            // Do NOT assign _wantMap/_wantMode/_guestCap or initialize any simulation member.
            AddChild(viewer);
            viewer.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var park = Field<Park>(viewer, "_park");
            var library = Field<AssetLibrary>(viewer, "_lib");
            var terrain = Field<Model>(viewer, "_terrainModel");
            var entrances = Field<ParkEntrance>(viewer, "_entranceTable");
            Check(park?.Field != null && library != null && terrain?.Field != null && entrances != null,
                "normal Viewer startup loaded terrain, library and entrance");
            Check(Field<object>(viewer, "_mode").ToString() == "Park", "actual Viewer is in Park mode");
            Check(string.Equals(System.IO.Path.GetFileNameWithoutExtension(library.WadName), world,
                StringComparison.OrdinalIgnoreCase), "actual archive matches requested world");
            Check(Field<int>(viewer, "_guestCap") > 0, "normal automatic admission is enabled");
            Check(park.Placed.Count == 0 && Placements(viewer).Count == 0
                && Field<int>(viewer, "_busAdmitted") == 0 && Field<int>(viewer, "_busBatches") == 0
                && Field<int>(viewer, "_parkTicks") == 0, "empty unstepped normal startup");

            var entry = entrances.Fit(terrain.Field, ParkEntrance.WalkwayColumnFromPoles(terrain), out _);
            Check(!entry.Empty, "authored entrance fits terrain");
            var corridor = new List<(int X, int Y)>();
            for (int z = entry.ZEnd; z < entry.ZEnd + 8; z++) corridor.Add((entry.XCol, z));
            Call(viewer, "LayLeg", corridor, PathTool.Kind.Path, 0);
            Call(viewer, "RefreshFloor");
            Check((bool)Call(viewer, "OpenGate"), "real guest layer opens on authored entrance");
            var walk = Field<GuestWalk>(viewer, "_guests");
            Check(corridor.All(c => walk.Paths.Open(new ParkCell(c.X, c.Y))), "real laid corridor is walkable");
            Check(walk.Guests.Count == 0, "no manually seeded guests");

            string stem = world switch
            {
                "JUNGLE" => "/Shops/IceCream/IceCream",
                "FANTASY" => "/Shops/icecream/icecream",
                _ => "/Shops/ices/ices",
            };
            Call(viewer, "ToggleBuildMenu");
            Call(viewer, "ShowBuildCategory", "Shops");
            var rows = Field<List<int>>(viewer, "_buildRows");
            var matches = Enumerable.Range(0, rows.Count).Where(row =>
            {
                var model = library.Rides[rows[row]].Model;
                var d = (RideDefinition)Call(viewer, "DefinitionFor", model);
                return d?.Compiled?.Product == 4
                    && string.Equals(System.IO.Path.ChangeExtension(model.Path, null), stem, StringComparison.OrdinalIgnoreCase)
                    && d.Source.EndsWith(stem + ".sam", StringComparison.OrdinalIgnoreCase);
            }).ToArray();
            Check(matches.Length == 1, "Shops menu contains exact world icecream identity");
            int shopRow = matches.Single();
            var asset = library.Rides[rows[shopRow]];
            var definition = (RideDefinition)Call(viewer, "DefinitionFor", asset.Model);
            Check(definition.Sells && definition.Shape != null && definition.CompiledEntry != null
                && asset.Script != null, "real compiled icecream definition, footprint and script");
            var blueprint = Field<Placement>(viewer, "_place");
            bool placed = false;
            for (int turn = 0; turn < 4 && !placed; turn++)
            {
                Call(viewer, "ArmFromList", shopRow);
                blueprint.Turn(turn);
                for (int z = entry.ZEnd; z < entry.ZEnd + 8 && !placed; z++)
                    for (int x = entry.XCol - 4; x <= entry.XCol + 4 && !placed; x++)
                    {
                        if (!blueprint.Fits(park, x, z)) continue;
                        var stubs = blueprint.Stubs(park, x, z).Where(s => s.Entrance).ToArray();
                        if (stubs.Length != 1 || !ParkPaths.Neighbours(new(stubs[0].X, stubs[0].Y))
                            .Any(c => walk.Paths.Open(c) && corridor.Contains((c.X, c.Z)))) continue;
                        int before = park.Placed.Count;
                        Set(viewer, "_cursorOverride", (x, z));
                        try { Call(viewer, "PlaceHeld"); }
                        finally { Set(viewer, "_cursorOverride", null); }
                        placed = park.Placed.Count == before + 1;
                    }
            }
            Check(placed && park.Placed.Count == 1, "actual Shops ArmFromList/PlaceHeld built one shop");
            Call(viewer, "CloseTool");
            var sim = Field<ParkSim>(viewer, "_sim");
            Check(sim != null && sim.Rides.Count == 1, "placement registered one real simulation ride");
            var ride = sim.Rides.Single();
            Check(ReferenceEquals(ride.Definition, definition) && ride.Machine != null && ride.Host != null
                && ride.Entrance != null && ride.Fault == null, "placed shop owns working real script and entrance");
            Check(Placements(viewer).Count == 1, "Viewer registered exactly one bus placement");
            var registered = Placements(viewer).Single();
            Check(ReferenceEquals(registered.Node, park.Placed.Single().Node) && Live(registered.Node)
                && registered.RuntimeId == ride.Id && ReferenceEquals(registered.Definition, definition),
                "bus placement retains actual node/runtime/definition identity");
            var objects = Objects(viewer);
            Check(objects.Count == 1 && objects[0].Kind == (int)definition.CompiledEntry.Kind
                && objects[0].Key == definition.CompiledEntry.Key, "BusObjects contains exactly the placed compiled identity");

            NativeBus bus = null;
            NativeBusCatalogue catalogue = null;
            ParkVisitors visitors = null;
            int ticks = 0, prePhase2Ticks = 0;
            bool sawPhase2 = false, checkedHighParameter = false;
            const int busOwner = int.MinValue + 97;
            RideSounds firstSounds = null;
            // One StepPark(.04) is one console tick. No subsequent tick may run before checking
            // newly admitted cells: TickPark updates guests BEFORE the bus calls Arrive(point0).
            for (; ticks < 1200 && Field<int>(viewer, "_busAdmitted") == 0;)
            {
                int beforeTicks = Field<int>(viewer, "_parkTicks");
                Call(viewer, "StepPark", .04);
                ticks++;
                Check(Field<int>(viewer, "_parkTicks") == beforeTicks + 1, "StepPark(.04) executes exactly one tick");
                if (ride.Fault != null) throw new InvalidOperationException("icecream script fault: " + ride.Fault);
                bus = Field<NativeBus>(viewer, "_nativeBus");
                catalogue = Field<NativeBusCatalogue>(viewer, "_busCatalogue");
                visitors = Field<ParkVisitors>(viewer, "_visitors");
                Check(bus != null && catalogue != null && visitors != null
                    && !Field<bool>(viewer, "_busLoadFailed"), "shipping TickPark initializes real native bus and visitors");
                Check(ReferenceEquals(visitors.Walk, walk) && ReferenceEquals(visitors.Sim, sim)
                    && walk.Paths.Open(catalogue.Point0), "actual catalogue point0 is open on shared guest grid");
                sawPhase2 |= bus.Controller.AppliedState == 2;
                if (bus.Controller.AppliedState == 2 && !checkedHighParameter)
                {
                    firstSounds = Field<RideSounds>(viewer, "_sounds");
                    Check(firstSounds?.ParameterValue?.Invoke(busOwner, 20) == 51,
                        "live sound accessor reads high bus parameter on phase2");
                    int previousLevel = ride.ScreamLevel;
                    try
                    {
                        ride.ScreamLevel = 73;
                        Check(firstSounds.ParameterValue(ride.Id, 6) == 73,
                            "bus sound accessor preserves changing per-ride scream selector");
                    }
                    finally { ride.ScreamLevel = previousLevel; }
                    checkedHighParameter = true;
                }
                if (!sawPhase2)
                {
                    prePhase2Ticks++;
                    Check(Field<int>(viewer, "_busAdmitted") == 0 && Field<int>(viewer, "_busBatches") == 0,
                        "no admissions or batch callback before phase2");
                }
                if (Field<int>(viewer, "_busAdmitted") == 0)
                {
                    Check(walk.Guests.Count == 0 && visitors.Plans.Count == 0, "no arrivals outside bus admission");
                    if (ticks % 8 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
            int admitted = Field<int>(viewer, "_busAdmitted");
            Check(admitted > 0 && ticks <= 1200, "real bus admitted within 1200 executed ticks");
            Check(firstSounds?.ParameterValue?.Invoke(busOwner, 20) == 0,
                "strict two-second bus parameter reset reached live sound accessor");
            Check(prePhase2Ticks > 0 && sawPhase2 && bus.Controller.AppliedState == 2
                && bus.Controller.State == 3 && bus.Controller.EndHold,
                "first admission follows observed phase2 completion, never approach");
            Check(Field<int>(viewer, "_busBatches") == 1 && walk.Guests.Count == admitted,
                "first real batch count equals newly created walkers");
            var newborns = walk.Guests.ToArray();
            Check(newborns.Select(g => g.Id).Distinct().Count() == admitted
                && newborns.All(g => g.Cell == catalogue.Point0),
                "every actual new guest cell equals catalogue Point0 on the first admitted tick");
            Check(visitors.Plans.Count == admitted && newborns.All(g =>
                visitors.Plans.TryGetValue(g.Id, out var plan) && plan.Guest == g.Id
                && plan.At == catalogue.Point0 && plan.Intent == VisitorIntent.Wandering),
                "actual Plans identities seeded at point0 by shipping Arrive");
            Check(visitors.Needs != null && newborns.All(g => visitors.Needs.Has(g.Id)),
                "actual admitted identities have seeded needs");
            var oldRoot = bus.Root;
            Check(Live(oldRoot) && oldRoot.GetParent() == viewer
                && Meshes(oldRoot).Any(m => Live(m) && m.Mesh != null), "actual bus model root and meshes are in scene tree");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Check(Live(oldRoot), "real bus survives a rendered frame");
            GD.Print($"BUS VIEWER SMOKE arrival world={world} ticks={ticks} admitted={admitted} point0={catalogue.Point0}");

            // LoadMap takes an index into _maps, NOT a build/list row. Switch terrain within
            // the same WAD so this also catches stale per-path caching. Keep processing stopped
            // and inspect reset state before any new StepPark can reinitialize the bus.
            var maps = Field<List<(string Wad, string Path, string Label)>>(viewer, "_maps");
            string oldPath = Field<string>(viewer, "_terrainPath");
            int next = maps.FindIndex(m => string.Equals(m.Wad, library.WadName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(m.Path, oldPath, StringComparison.OrdinalIgnoreCase));
            Check(next >= 0, "another terrain in same world exists for teardown test");
            Call(viewer, "LoadMap", next);
            Check(!IsInstanceValid(oldRoot) || oldRoot.IsQueuedForDeletion(), "LoadMap queues/frees old native bus root");
            void CheckReset()
            {
                Check(Field<NativeBus>(viewer, "_nativeBus") == null
                    && Field<NativeBusCatalogue>(viewer, "_busCatalogue") == null
                    && Field<Model>(viewer, "_nativeBusMesh") == null, "old bus adapter/catalogue/model cleared before reinitialization");
                Check(Field<int>(viewer, "_busAdmitted") == 0 && Field<int>(viewer, "_busBatches") == 0
                    && Field<double>(viewer, "_busElapsedMs") == 0 && Field<int>(viewer, "_busTraffic") == 0,
                    "bus admission counters, clock and traffic reset");
                Check(Placements(viewer).Count == 0 && park.Placed.Count == 0 && Objects(viewer).Count == 0,
                    "old placements and BusObjects reset");
                Check(Field<ParkVisitors>(viewer, "_visitors") == null && Field<GuestWalk>(viewer, "_guests") == null
                    && Field<ParkSim>(viewer, "_sim") == null && Field<int>(viewer, "_parkTicks") == 0,
                    "old simulation/guest owners reset before reinitialization");
            }
            CheckReset();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!IsInstanceValid(oldRoot) || oldRoot.IsQueuedForDeletion(), "old bus remains queued/freed after process frame");
            CheckReset();
            Check(string.Equals(Field<string>(viewer, "_terrainPath"), maps[next].Path, StringComparison.OrdinalIgnoreCase)
                && !ReferenceEquals(Field<Model>(viewer, "_terrainModel"), terrain) && park.Field != null,
                "LoadMap actually loaded the other same-world terrain");
            // A second visit in an EMPTY second park must still have its own sound owner.
            // No new shop may be needed to construct sound or install parameter20.
            for (int tick = 0; tick < 250; tick++)
            {
                Call(viewer, "StepPark", .04);
                var secondBus = Field<NativeBus>(viewer, "_nativeBus");
                Check(secondBus != null, "second map initializes its own bus without placing a shop");
                if (secondBus.Controller.AppliedState == 2) break;
                if (tick % 8 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var second = Field<NativeBus>(viewer, "_nativeBus");
            var secondSounds = Field<RideSounds>(viewer, "_sounds");
            Check(second.Controller.AppliedState == 2 && secondSounds != null
                && !ReferenceEquals(firstSounds, secondSounds)
                && secondSounds.ParameterValue?.Invoke(busOwner, 20) == 51,
                "second sound owner, constructed before any shop, receives live parameter20");
            Check(Placements(viewer).Count == 0 && Objects(viewer).Count == 0
                && Field<int>(viewer, "_busAdmitted") == 0,
                "second empty park inherits neither placed demand nor first-park admissions");
            GD.Print($"BUS VIEWER SMOKE PASS world={world} map={map} checks={_checks}");
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.PrintErr($"BUS VIEWER SMOKE FAIL world={world} checks={_checks}: {e}");
            GetTree().Quit(2);
        }
    }
}
