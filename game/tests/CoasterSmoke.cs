using System.Collections;
using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>A roller coaster on the shipping Viewer, end to end: the build menu's Roller Coasters
/// category, the real placement press (station, doors and queue stubs), the coaster tool laying a
/// ring press by press and closing it on the entry cell, the pylon edit raising a hill, the track and
/// pylons drawn, then trains spawned, riders boarded from the ride's own queue and handed back.
/// Run with --map=WORLD --mode=park. `TPW_COASTER_SHOT=dir` saves pictures; `TPW_COASTER=folder`
/// picks a coaster (default: the park's first); `TPW_COASTER_TURNS` turns the station.</summary>
public partial class CoasterSmoke : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string name) => typeof(Viewer).GetField(name, Hidden)
        ?? throw new MissingMemberException("Viewer." + name);
    static T Field<T>(Viewer v, string name) => (T)Member(name).GetValue(v);
    static void Set(Viewer v, string name, object value) => Member(name).SetValue(v, value);
    static object Call(Viewer v, string name, params object[] args)
        => (typeof(Viewer).GetMethod(name, Hidden) ?? throw new MissingMemberException("Viewer." + name)).Invoke(v, args);
    static object F(object o, string name) => o.GetType().GetField(name)?.GetValue(o)
        ?? throw new MissingMemberException(o.GetType().Name + "." + name);
    int _checks;
    void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++;
        GD.Print("COASTER ok: " + label);
    }

    /// <summary>The audit's oval, in the exit's own frame: (along the station's travel, to one side).</summary>
    static readonly (int F, int S)[] Oval = { (6, 0), (11, 4), (11, 10), (6, 14), (0, 14), (-6, 14), (-11, 10), (-11, 5), (-9, 0) };
    static readonly int[] Hills = { 375, 800, 1200, 1200, 400, 100, 300, 375, 375 };

    public override async void _Ready()
    {
        string world = "unknown";
        Viewer viewer = null;
        try
        {
            Check(DisplayServer.GetName() != "headless", "rendering display required");
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            string map = args.LastOrDefault(a => a.StartsWith("--map="))?["--map=".Length..]
                ?? throw new ArgumentException("Pass --map=WORLD");
            world = new[] { "JUNGLE", "FANTASY", "HALLOW", "SPACE" }.Single(w =>
                map.Equals(w, StringComparison.OrdinalIgnoreCase) || map.StartsWith(w + " ", StringComparison.OrdinalIgnoreCase));
            string shots = System.Environment.GetEnvironmentVariable("TPW_COASTER_SHOT");
            if (shots != null) System.IO.Directory.CreateDirectory(shots);

            viewer = new Viewer { Name = "Viewer" };
            AddChild(viewer);
            viewer.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var park = Field<Park>(viewer, "_park");
            var lib = Field<AssetLibrary>(viewer, "_lib");

            Call(viewer, "ShowBuildCategory", "Coaster");
            var rows = Field<List<int>>(viewer, "_buildRows");
            Check(rows.Count > 0, $"{world}: the Roller Coasters category lists {rows.Count}");
            string want = System.Environment.GetEnvironmentVariable("TPW_COASTER");
            int chosen = want == null ? 0 : Enumerable.Range(0, rows.Count).First(r =>
                lib.Rides[rows[r]].Model.Path.Contains("/" + want + "/", StringComparison.OrdinalIgnoreCase));
            string stationPath = lib.Rides[rows[chosen]].Model.Path;
            Call(viewer, "ArmFromList", chosen);
            var blueprint = Field<Placement>(viewer, "_place");
            // ⭐ The station's own .sam now: a real shape with an entrance and an exit, and a queue.
            Check(blueprint.Active && blueprint.IsRide && blueprint.Base.EntryX >= 0 && blueprint.Base.ExitX >= 0,
                  $"armed {stationPath}: {blueprint.Base.Width}x{blueprint.Base.Height} with an entrance and an exit, and it takes a queue");
            int turns = int.TryParse(System.Environment.GetEnvironmentVariable("TPW_COASTER_TURNS"), out var tt) ? tt & 3 : 0;
            for (int i = 0; i < turns; i++) blueprint.Turn(1);

            string folder = stationPath[..stationPath.LastIndexOf('/')]; folder = folder[(folder.LastIndexOf('/') + 1)..];
            var type = CoasterType.ForFolder(folder);
            Check(type != null, $"{folder} is one of the fourteen ({type?.Name})");
            var def = (RideDefinition)Call(viewer, "DefinitionFor", lib.Rides[rows[chosen]].Model);
            Check(def?.CompiledEntry?.Kind == AssetResourceDatabase.AssetKind.Coaster,
                  $"the station resolves to its own .sam ({def?.Source}) and so to its compiled record");
            var link = typeof(Viewer).GetMethod("StationLink", BindingFlags.NonPublic | BindingFlags.Static);
            (ParkCell E, int Step, ParkCell R) Ends(int cx, int cy)
            {
                var t = (System.Runtime.CompilerServices.ITuple)link.Invoke(null, new object[] {
                    def.CompiledEntry.Payload, cx, cy, turns, blueprint.Base.Width, blueprint.Base.Height });
                return ((ParkCell)t[0], (int)t[1], (ParkCell)t[2]);
            }
            static (int, int) Dir(int step) => (step & 3) switch { 0 => (0, -1), 1 => (-1, 0), 2 => (0, 1), _ => (1, 0) };
            ParkCell[] OvalOf(ParkCell e, int step, int side)
            {
                var (fx, fz) = Dir(step);
                int sx = -fz * side, sz = fx * side;
                return Oval.Select(o => e.Offset(o.F * fx + o.S * sx, o.F * fz + o.S * sz)).ToArray();
            }
            var paths = Field<PathTool>(viewer, "_paths");
            bool Clear(int x, int y) => park.IsPlayable(x, y) && park.Vacant(x, y) && paths.KindAt(x, y) == PathTool.Kind.None;
            (int X, int Y)? spot = null; int side = 1;
            for (int y = 16; y < park.Height - 18 && spot == null; y++)
                for (int x = 16; x < park.Width - 18 && spot == null; x++)
                {
                    if (!blueprint.Fits(park, x, y)) continue;
                    var (cx, cy) = blueprint.CornerFor(x, y);
                    var (e0, st0, r0) = Ends(cx, cy);
                    foreach (int s in new[] { 1, -1 })
                    {
                        var pts = OvalOf(e0, st0, s).Append(e0).Append(r0).ToList();
                        bool ok = pts.All(p => p.X >= 2 && p.Z >= 2 && p.X < park.Width - 3 && p.Z < park.Height - 3);
                        foreach (var p in pts.Take(Oval.Length))
                            for (int dz = -1; dz <= 1 && ok; dz++)
                                for (int dx = -1; dx <= 1 && ok; dx++) if (!Clear(p.X + dx, p.Z + dz)) ok = false;
                        if (ok && (!Clear(e0.X, e0.Z) || !Clear(r0.X, r0.Z))) ok = false;
                        int fw = blueprint.Turned.Width, fh = blueprint.Turned.Height;
                        foreach (var p in pts.Take(Oval.Length))
                            if (p.X >= cx - 1 && p.X <= cx + fw && p.Z >= cy - 1 && p.Z <= cy + fh) ok = false;
                        if (ok) { spot = (x, y); side = s; break; }
                    }
                }
            Check(spot != null, $"{world}: found clear ground for a station and a 23x15 ring");
            int placedBefore = park.Placed.Count;
            Set(viewer, "_cursorOverride", spot.Value);
            try { Call(viewer, "PlaceHeld"); } finally { Set(viewer, "_cursorOverride", null); }
            var coasters = Field<IDictionary>(viewer, "_coasters");
            Check(park.Placed.Count == placedBefore + 1 && coasters.Count == 1, "the press placed the station and made its coaster");
            var view = coasters.Values.Cast<object>().Single();
            var track = (CoasterTrack)F(view, "Track");
            var csim = (CoasterSim)F(view, "Sim");
            var (exitCell, step, entryCell) = Ends(park.Placed[^1].X, park.Placed[^1].Y);
            Check(track.Exit.Cell == exitCell && track.Entry.Cell == entryCell && track.Exit.Height == type.ExitHeight,
                  $"the station nodes stand one cell out of the station: exit {track.Exit.Cell}, entry {track.Entry.Cell}, height {track.Exit.Height}");
            Check(Member("_coasterTool").GetValue(viewer) == view && !Field<bool>(viewer, "_toolOpen"),
                  "placing a coaster opens the coaster tool; the queue tool waits for the track");
            var queueStubs = Enumerable.Range(0, park.Width).SelectMany(x => Enumerable.Range(0, park.Height).Select(y => (x, y)))
                .Count(c => paths.KindAt(c.x, c.y) == PathTool.Kind.Queue);
            Check(queueStubs >= 1, $"the station laid its queue stub ({queueStubs} queue tile)");

            // Lay the ring press by press, then close it on the entry cell.
            var ring = OvalOf(track.Exit.Cell, step, side);
            var sim = Field<ParkSim>(viewer, "_sim");
            foreach (var c in ring)
            {
                Set(viewer, "_cursorOverride", (c.X, c.Z));
                try { Call(viewer, "UpdateCoasterGhost", 0.0); Call(viewer, "PressCoasterTool"); }
                finally { Set(viewer, "_cursorOverride", null); }
            }
            Check(track.Pylons.Count == ring.Length && !track.Closed && track.Pylons.All(n => n.Height == type.ExitHeight),
                  $"{ring.Length} presses lay {track.Pylons.Count} pylons, every one at the station's height {type.ExitHeight}");
            Set(viewer, "_cursorOverride", (track.Entry.CellX, track.Entry.CellZ));
            try { Call(viewer, "UpdateCoasterGhost", 0.0); Call(viewer, "PressCoasterTool"); }
            finally { Set(viewer, "_cursorOverride", null); }
            Check(track.Closed && track.Valid && track.Pylons.Count == ring.Length,
                  "a press on the entry cell closes the ring without a pylon, and every node is valid");
            Check(Member("_coasterTool").GetValue(viewer) == view && Member("_coasterMode").GetValue(viewer).ToString() == "Edit",
                  "closing from the station session goes on to the pylon edit (0x11b6a4)");
            // The D-pad in the edit: raise a hill, as a player would, one pylon at a time.
            for (int i = 0; i < track.Pylons.Count; i++) track.Pylons[i].Height = Hills[i];
            track.Recompute();
            foreach (var n in track.Pylons) n.Valid = track.IsValid(n, (CoasterTrack.IGround)Call(viewer, "get_Ground"), true);
            Check(track.Valid, "the raised ring is still valid");
            Call(viewer, "FinishCoasterTool");
            Check(Member("_coasterTool").GetValue(viewer) == null && Field<bool>(viewer, "_toolOpen"),
                  "finishing hands over to the queue tool");
            Call(viewer, "CloseTool");

            var segments = (IDictionary)F(view, "Segments");
            var pylons = (IDictionary)F(view, "Pylons");
            int expectSegs = track.Nodes().Count(n => CoasterMesh.Visible(track, n));
            Check(segments.Count == expectSegs && pylons.Count == track.Pylons.Count,
                  $"every visible segment is drawn ({segments.Count} of {expectSegs}) and every pylon ({pylons.Count})");
            Check(segments.Values.Cast<MeshInstance3D>().All(m => m.Mesh.GetSurfaceCount() > 0),
                  $"every segment has its {type.Style}-style strips");

            var frame = (Node3D)F(view, "Frame");
            var cells = ring.Append(track.Exit.Cell).ToList();
            Vector3 Centre() => frame.GlobalTransform * new Vector3((float)cells.Average(c => c.X) + 0.5f, 1.5f, (float)cells.Average(c => c.Z) + 0.5f);
            Vector3? aimAt = null; float? aimFar = null; float lift = 0.8f;
            async Task Shot(string name)
            {
                if (shots == null) return;
                foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = false;
                foreach (var ui in viewer.GetChildren().OfType<Control>()) ui.Visible = false;
                Set(viewer, "_freeCam", true);
                var camera = Field<Camera3D>(viewer, "_cam");
                var aim = aimAt ?? Centre();
                float far = aimFar ?? (float.TryParse(System.Environment.GetEnvironmentVariable("TPW_COASTER_DIST"), out var dd) ? dd : 18f);
                camera.GlobalPosition = aim + new Vector3(far * 0.55f, far * lift, far * 0.75f);
                camera.LookAt(aim, Vector3.Up);
                for (int i = 0; i < 4; i++) { Call(viewer, "StepPark", .0); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                Call(viewer, "SaveShot", System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}_{folder.ToLowerInvariant()}_{name}.png"));
            }

            // Trains: the ride is open, the ring closed and valid, so the status tick spawns them.
            Call(viewer, "StepPark", .04);
            int nc = type.CarsPerTrain, expect = Math.Clamp((track.Pylons.Count / 3 + 2) / nc, 2, 6);
            Check(csim.Trains.Count == expect, $"{csim.Trains.Count} trains of {nc} spawn: clamp((pylons/3+2)/cars, 2, 6) = {expect}");
            await Shot("track");
            lift = 0.15f; await Shot("side"); lift = 0.8f;
            var ride = sim.Rides.Single(r => r.Coaster == csim);
            int riders = nc * type.Seats + 2;
            for (int g = 9001; g < 9001 + riders; g++) ride.Join(g);
            float top = 0; int left = 0;
            var cars = (IDictionary)F(view, "Cars");
            for (int i = 0; i < 9000 && left < riders; i++)
            {
                Call(viewer, "StepPark", .04);
                top = Math.Max(top, csim.Trains.Max(t => t.PrevSpeed));
                left += ride.Left.Count; ride.ClearLeft();
                if (i % 200 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (i == 700) await Shot("cars");
            }
            Check(left == riders, $"all {riders} queued guests board, ride and come off at the exit");
            Check(top > 0.1f, $"the trains run the hill on gravity (top speed {top:F3} cells a tick)");
            Check(cars.Count == csim.Trains.Sum(t => t.Cars.Length) && cars.Count > 0, $"every car is drawn ({cars.Count})");
            foreach (var car in csim.Trains.SelectMany(t => t.Cars))
            {
                var node = (Node3D)F(cars[car], "Item1");
                var want3 = frame.GlobalTransform * new Vector3(car.Pos.X, car.Pos.Y, car.Pos.Z);
                Check(node.GlobalPosition.DistanceTo(want3) < 0.6f, $"a car stands on its track point ({node.GlobalPosition.DistanceTo(want3):F2} off)");
            }
            if (shots != null && csim.Trains.Count > 0)
            {
                var lead = (Node3D)F(cars[csim.Trains[0].Cars[0]], "Item1");
                aimAt = lead.GlobalPosition; aimFar = 2.5f;
                await Shot("closeup");
                aimAt = null; aimFar = null;
            }

            // Deleting the station takes the coaster with it.
            Set(viewer, "_selected", park.Placed.Count - 1);
            Call(viewer, "DeleteSelected");
            Check(coasters.Count == 0 && (!IsInstanceValid(frame) || frame.IsQueuedForDeletion()), "deleting the station removes its track and trains");

            Field<RideSounds>(viewer, "_sounds")?.Clear();
            viewer.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            GD.Print($"COASTER SMOKE PASS checks={_checks}; world={world}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"COASTER SMOKE FAIL world={world} checks={_checks}: {ex}");
            GetTree().Quit(2);
        }
    }
}
