using System.Collections;
using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>A track ride on the shipping Viewer, end to end: the build menu's Track Rides category,
/// the real placement press, the track tool drawing a closed loop press by press, the pieces drawn,
/// then four riders boarded through the ride's own queue, driven round and handed back.
/// Run with --map=WORLD --mode=park. `TPW_TRACK_SHOT=dir` saves a picture of the track and one of
/// the cars running.
///
/// ⚠ No visitors: the park gate stays shut and the riders are joined to the queue directly, so this
/// tests the ride, not the walk to it. ParkSimAudit's "track ride: park:" checks do the same through
/// ParkSim with the real script.</summary>
public partial class TrackRideSmoke : Node3D
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
        GD.Print("TRACK RIDE ok: " + label);
    }

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
            string shots = System.Environment.GetEnvironmentVariable("TPW_TRACK_SHOT");
            if (shots != null) System.IO.Directory.CreateDirectory(shots);

            viewer = new Viewer { Name = "Viewer" };
            AddChild(viewer);
            viewer.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var park = Field<Park>(viewer, "_park");
            var lib = Field<AssetLibrary>(viewer, "_lib");

            // The port-turn -> console-station mapping, carried two independent ways (exit and entry),
            // for both station shapes (karts 4x3, water 4x4) and all four turns.
            var frameOf = typeof(Viewer).GetMethod("StationFrame", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var (w, d) in new[] { (4, 3), (4, 4) })
            {
                var agree = Enumerable.Range(0, 4).Select(t => (bool)((System.Runtime.CompilerServices.ITuple)frameOf.Invoke(null, new object[] { new ParkCell(20, 20), t, w, d }))[2]).ToList();
                Check(agree.All(a => a), $"a {w}x{d} station's console frame agrees with the port's turn at all four turns (exit and entry carried separately)");
            }

            Call(viewer, "ShowBuildCategory", "TrackRide");
            var rows = Field<List<int>>(viewer, "_buildRows");
            Check(rows.Count > 0, $"{world}: the Track Rides category lists {rows.Count} rides");
            int chosen = Enumerable.Range(0, rows.Count).FirstOrDefault(r =>
                lib.Rides[rows[r]].Model.Path.Contains("gokarts", StringComparison.OrdinalIgnoreCase));
            // Terrain 2 builds the water ride, so the matrix runs both kinds (it only ever built karts).
            bool water = System.Environment.GetEnvironmentVariable("TPW_TRACK_WATER") is { } tw
                ? tw == "1" : map.Contains("terrain_2", StringComparison.OrdinalIgnoreCase);
            if (water)
                chosen = Enumerable.Range(0, rows.Count).First(r => lib.Rides[rows[r]].Model.Path.Contains("wateride", StringComparison.OrdinalIgnoreCase));
            string stationPath = lib.Rides[rows[chosen]].Model.Path;
            Call(viewer, "ArmFromList", chosen);
            var blueprint = Field<Placement>(viewer, "_place");
            Check(blueprint.Active && blueprint.Turns == 0, $"armed {stationPath}");

            // Optionally turned (TPW_TRACK_TURNS); the loop goes out of the exit, 8 to one side and back
            // into the entry, so a spot is one where all of that ground is clear.
            int turns = int.TryParse(System.Environment.GetEnvironmentVariable("TPW_TRACK_TURNS"), out var tt) ? tt & 3 : 0;
            for (int i = 0; i < turns; i++) blueprint.Turn(1);
            var frameOfStation = typeof(Viewer).GetMethod("StationFrame", BindingFlags.NonPublic | BindingFlags.Static);
            (ParkCell E, ParkCell R) Ends(int cx, int cy)
            {
                var fr = (System.Runtime.CompilerServices.ITuple)frameOfStation.Invoke(null, new object[] { new ParkCell(cx, cy), turns, blueprint.Base.Width, blueprint.Base.Height });
                var anchor = (ParkCell)fr[0]; int rot = (int)fr[1];
                return (TrackPieces.Exit(anchor, rot), TrackPieces.Return(anchor, rot));
            }
            static ParkCell[] LoopOf(ParkCell e, ParkCell r, int s)
            {
                int ox = Math.Sign(e.X - r.X), oz = Math.Sign(e.Z - r.Z), sx = -oz * s, sz = ox * s;
                return new[] { e.Offset(6 * ox, 6 * oz), e.Offset(6 * ox + 8 * sx, 6 * oz + 8 * sz),
                               e.Offset(-8 * ox + 8 * sx, -8 * oz + 8 * sz), e.Offset(-8 * ox, -8 * oz), r };
            }
            (int X, int Y)? spot = null; int side = 1;
            bool Clear(int x, int y) => park.IsPlayable(x, y) && park.Vacant(x, y)
                && Field<PathTool>(viewer, "_paths").KindAt(x, y) == PathTool.Kind.None;
            for (int y = 12; y < park.Height - 14 && spot == null; y++)
                for (int x = 12; x < park.Width - 14 && spot == null; x++)
                {
                    if (!blueprint.Fits(park, x, y)) continue;
                    var (cx, cy) = blueprint.CornerFor(x, y);
                    var (e0, r0) = Ends(cx, cy);
                    foreach (int s in new[] { 1, -1 })
                    {
                        var pts = LoopOf(e0, r0, s).Append(e0).ToList();
                        int x0 = pts.Min(p => p.X) - 1, x1 = pts.Max(p => p.X) + 2, z0 = pts.Min(p => p.Z) - 1, z1 = pts.Max(p => p.Z) + 2;
                        bool ok = x0 >= 1 && z0 >= 1 && x1 < park.Width - 3 && z1 < park.Height - 3;
                        int fw = blueprint.Turned.Width, fh = blueprint.Turned.Height;
                        for (int zz = z0; zz <= z1 && ok; zz++)
                            for (int xx = x0; xx <= x1 && ok; xx++)
                            {
                                bool footprint = xx >= cx - 1 && xx <= cx + fw && zz >= cy - 1 && zz <= cy + fh;
                                if (!footprint && !Clear(xx, zz)) ok = false;
                            }
                        if (ok) { spot = (x, y); side = s; break; }
                    }
                }
            Check(spot != null, $"{world}: found clear ground for a station and a 16x9 loop");
            Set(viewer, "_cursorOverride", spot.Value);
            try { Call(viewer, "PlaceHeld"); } finally { Set(viewer, "_cursorOverride", null); }
            var tracks = Field<IDictionary>(viewer, "_tracks");
            Check(park.Placed.Count == 1 && tracks.Count == 1, "the press placed the station and made its track");
            var view = tracks.Values.Cast<object>().Single();
            var layout = (TrackLayout)F(view, "Layout");
            var trackSim = (TrackRideSim)F(view, "Sim");
            Check(Member("_trackTool").GetValue(viewer) == view, "placing a track ride opens the track tool, not the queue tool");
            Check(!Field<bool>(viewer, "_toolOpen"), "the queue tool waits for the track");
            Check(layout.Pieces.Count == 3 && !layout.Closed, $"before any leg: station, connector and exit straight ({layout.Pieces.Count} pieces), loop open");

            // Draw it press by press, exactly as a player would.
            var e = layout.ExitCell; var ret = layout.ReturnCell;
            var legs = LoopOf(e, ret, side);
            var sim = Field<ParkSim>(viewer, "_sim");
            int before = sim.Finances.Balance;
            for (int li = 0; li < legs.Length; li++)
            {
                var c = legs[li];
                Set(viewer, "_cursorOverride", (c.X, c.Z));
                try { Call(viewer, "UpdateTrackGhost"); Call(viewer, "PressTrackTool"); }
                finally { Set(viewer, "_cursorOverride", null); }
                if (li != 0) continue;
                // ⭐ A click on the last waypoint, or a cell off it, is a zero-length leg. The console
                // finishes the tool there with the loop open; with a mouse that is a near-miss, so here
                // it lays nothing and the tool stays open (strawberry's loop that "was closed").
                int wp = layout.Waypoints.Count;
                foreach (var near in new[] { c, c.Offset(1, 0), c.Offset(1, 1) })
                {
                    Set(viewer, "_cursorOverride", (near.X, near.Z));
                    try { Call(viewer, "UpdateTrackGhost"); Call(viewer, "PressTrackTool"); }
                    finally { Set(viewer, "_cursorOverride", null); }
                }
                Check(Member("_trackTool").GetValue(viewer) == view && layout.Waypoints.Count == wp,
                      $"clicks on and a cell off the last waypoint lay nothing and leave the tool open ({layout.Waypoints.Count} waypoints)");
            }
            int price = (int)F(view, "Price");
            Check(layout.Closed && layout.Pieces.Count == 22, $"five presses close the loop: {layout.Pieces.Count} pieces, {string.Join(",", layout.Pieces.Select(p => p.Type))}");
            Check(price > 0 && before - sim.Finances.Balance == price * 10 * 19 || sim.Finances.Unlimited,
                  $"the legs cost {price} a piece (record +0xCC): {before - sim.Finances.Balance} tenths for 19 pieces");
            Check(Member("_trackTool").GetValue(viewer) == null && Field<bool>(viewer, "_toolOpen"),
                  "closing the loop finishes the track tool and hands over to the queue tool");
            Call(viewer, "CloseTool");
            var pieces = (IList)F(view, "Pieces");
            int drawn = layout.Pieces.Count(p => p.Info.Shape is not (15 or 99));
            Check(pieces.Count == drawn && pieces.Cast<Node3D>().All(n => n.FindChildren("*", "MeshInstance3D", true, false).Count > 0),
                  $"every piece that has a mesh is drawn ({pieces.Count} of {drawn}), from {F(view, "Prefix")}*");
            Check(trackSim.Status == TrackRideStatus.Running || trackSim.Status == TrackRideStatus.Unloading || trackSim.Status == TrackRideStatus.Loading,
                  $"a closed loop runs ({trackSim.Status})");
            // ⭐ The water flows: 0x1fd818 starts every piece on its Main record, looping (strawberry: the
            // track pieces' textures "dont scroll/flow"). Between two moments a straight piece's water has
            // moved by exactly what its own Main keys say for the frames between (JUNGLE's run V 1.0 per
            // 50 frames, the other worlds' half that: the rate is not assumed).
            var flowing = ((IList)F(view, "Flowing")).Cast<AnimatedModel>().ToList();
            if (water)
            {
                Check(flowing.Count == pieces.Count, $"every water piece plays its Main loop ({flowing.Count} of {pieces.Count})");
                // By layout order, not name: Godot renames the sibling holders @Node3D@n. Pieces and Flowing are
                // appended in step with the drawn layout pieces (every water piece has a Main, checked above).
                var drawnShapes = layout.Pieces.Where(p => p.Info.Shape is not (15 or 99)).Select(p => p.Info.Shape).ToList();
                int si = drawnShapes.IndexOf(0);
                var straight = si >= 0 && drawnShapes.Count == flowing.Count ? flowing[si] : null;
                Check(straight != null, $"a straight piece (shape 0) is among them, piece {si} of {drawnShapes.Count}");
                List<Vector2> Uvs() => straight.Root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Where(mi => mi.Mesh != null)
                    .SelectMany(mi => Enumerable.Range(0, mi.Mesh.GetSurfaceCount()).SelectMany(sf => mi.Mesh.SurfaceGetArrays(sf)[(int)Mesh.ArrayType.TexUV].AsVector2Array())).ToList();
                Call(viewer, "PresentTracks", 0f);
                int f0 = (int)F(view, "FlowFrame"); var uv0 = Uvs();
                for (int i = 0; i < 5; i++) Call(viewer, "StepPark", .04);
                Call(viewer, "PresentTracks", 0f);
                int f1 = (int)F(view, "FlowFrame"); var uv1 = Uvs();
                string dir = (string)F(view, "Dir"), prefix = (string)F(view, "Prefix");
                var straightAssets = lib.Rides.First(r => r.Model != null && r.Model.Path.Equals(dir + prefix + "trcks.mps", StringComparison.OrdinalIgnoreCase));
                var aps = new TPW.PS2.Data.Animation(lib.Read(straightAssets.Animation));
                var main = aps.Records().First(r => r.Slot == 5);
                float want = 0;
                for (int t = 0; t < main.TrackCount; t++)
                {
                    int off = aps.TrackAt(main, t);
                    if ((aps.TrackFlags(off) & 0x10000) == 0 || aps.UvTrack(off) is not { } groups) continue;
                    foreach (var g in groups)
                        want = MathF.Max(want, MathF.Abs(TPW.PS2.Data.Animation.SampleUv(g, f1 % main.DurationFrames).V
                                                         - TPW.PS2.Data.Animation.SampleUv(g, f0 % main.DurationFrames).V));
                }
                float moved = uv0.Zip(uv1, (a, b) => MathF.Abs(b.Y - a.Y)).Max();
                Check(f1 > f0 && want > 0.01f && uv0.Count == uv1.Count && MathF.Abs(moved - want) < 0.01f,
                      $"the water moves: frame {f0} -> {f1}, a straight piece's V by {moved:F3} (keys: {want:F3})");
            }

            var frame = (Node3D)F(view, "Frame");
            var loopCells = legs.Append(e).ToList();
            Vector3 Centre() => frame.GlobalTransform * new Vector3((float)loopCells.Average(c => c.X) + 1, 0, (float)loopCells.Average(c => c.Z) + 1);
            Vector3? aimAt = null; float? aimFar = null;
            async Task Shot(string name)
            {
                if (shots == null) return;
                foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = false;
                foreach (var ui in viewer.GetChildren().OfType<Control>()) ui.Visible = false;
                Set(viewer, "_freeCam", true);
                var camera = Field<Camera3D>(viewer, "_cam");
                var aim = aimAt ?? Centre();
                float far = aimFar ?? (float.TryParse(System.Environment.GetEnvironmentVariable("TPW_TRACK_DIST"), out var dd) ? dd : 13f);
                camera.GlobalPosition = aim + new Vector3(far * 0.55f, far, far * 0.75f);
                camera.LookAt(aim, Vector3.Up);
                for (int i = 0; i < 4; i++) { Call(viewer, "StepPark", .0); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                Call(viewer, "SaveShot", System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}_{name}.png"));
            }
            await Shot("track");

            // Riders, straight into the ride's queue.
            var ride = sim.Rides.Single();
            for (int g = 9001; g <= 9004; g++) ride.Join(g);
            int ticks = 0;
            while (trackSim.Cars.Count < 4 && ticks < 400) { Call(viewer, "StepPark", .04); ticks++; }
            Check(trackSim.Cars.Count == 4 && trackSim.Cars.All(c => c.Guest is >= 9001 and <= 9004),
                  $"four queued guests each board their own car ({ticks} ticks)");
            for (int i = 0; i < 300; i++) { Call(viewer, "StepPark", .04); if (i % 25 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            var cars = (IDictionary)F(view, "Cars");
            Check(cars.Count == trackSim.Cars.Count && cars.Count > 0, $"every car is drawn ({cars.Count})");
            // Each car stands over its own track: within a cell of the centre line at its distance.
            foreach (var car in trackSim.Cars)
            {
                var (x, _, z) = layout.Position(car.Distance, 128);
                var line = frame.GlobalTransform * new Vector3(x / 256f, 0, z / 256f);
                var node = (Node3D)F(cars[car], "Node");
                var at = node.GlobalPosition;
                Check(new Vector2(at.X - line.X, at.Z - line.Z).Length() < 1.2f, $"{car} is drawn on the track ({new Vector2(at.X - line.X, at.Z - line.Z).Length():F2} off the centre line)");
            }
            // Riders sit on the cars' seat fittings, and the cars' engine note resolves to the track bank.
            var seated = Field<IDictionary>(viewer, "_seated");
            foreach (var car in trackSim.Cars)
            {
                var node = (Node3D)F(cars[car], "Node");
                Check(car.Guest is int g && seated.Contains(g), $"{car}'s rider is seated");
                var at = ((Transform3D)((System.Runtime.CompilerServices.ITuple)seated[car.Guest.Value])[0]).Origin;
                Check(at.DistanceTo(node.GlobalPosition) < 1.0f, $"{car}'s rider sits on the car ({at.DistanceTo(node.GlobalPosition):F2} from its origin)");
            }
            var sounds = Field<RideSounds>(viewer, "_sounds");
            var engine = sounds?.Census.Where(l => l.Contains("NativeRidesTrack") && l.Contains("evt   4")).ToList() ?? new();
            Check(engine.Count > 0 && engine.All(l => !l.Contains("(no event")) && engine.Any(l => l.Contains("ms ")),
                  $"the cars' engine note (native category 6 event 4) resolves to a clip in the track bank ({engine.Count} cues): {engine.FirstOrDefault()}");
            await Shot("cars");
            // Close on the lead car, side-on, to see its scale, height over the deck and heading.
            if (shots != null && trackSim.Cars.Count > 0)
            {
                var leadCar = trackSim.Cars[0];
                var lead = (Node3D)F(cars[leadCar], "Node");
                aimAt = lead.GlobalPosition; aimFar = 1.6f;
                await Shot("closeup");
                aimAt = null; aimFar = null;
                // Which way the model's local +z points against the way the car is going.
                var p0 = lead.GlobalPosition;
                for (int i = 0; i < 6; i++) Call(viewer, "StepPark", .04);
                var travel = lead.GlobalPosition - p0; travel.Y = 0;
                var fwd = lead.GlobalTransform.Basis.Z; fwd.Y = 0;
                GD.Print($"[track] lead car travels {travel.Length():F2} over 6 ticks; its model +z is {Mathf.RadToDeg(fwd.AngleTo(travel)):F0} degrees off the travel direction");
                // And one picture from straight ahead of its model +z, low, to see which end that is.
                var camera = Field<Camera3D>(viewer, "_cam");
                camera.GlobalPosition = lead.GlobalPosition + fwd.Normalized() * 1.2f + new Vector3(0, 0.35f, 0);
                camera.LookAt(lead.GlobalPosition, Vector3.Up);
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Call(viewer, "SaveShot", System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}_plusz.png"));
            }

            int left = 0;
            for (int i = 0; i < 6000 && left < 4; i++)
            {
                Call(viewer, "StepPark", .04);
                left += ride.Left.Count; ride.ClearLeft();
                if (i % 200 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Check(left == 4 && trackSim.Cars.Count == 0, $"all four riders come off after {trackSim.Duration} laps and the cars are gone");
            Check(cars.Count == 0, "and so are their models");

            // ⭐ A path laid UNDER the track (0x1E81E0's track arm, then 0x1E6CF0's re-lay): allowed
            // under a straight, refused under a bend and on the station's exit block, and the straight
            // it crosses becomes a hump; a second crossing beside it makes ramp-up + ramp-down.
            {
                var ghost = Field<PathGhost>(viewer, "_ghost");
                var paths = Field<PathTool>(viewer, "_paths");
                var wp1 = layout.Waypoints[2];   // the long leg, 14 cells: a bend, then six straights
                var wp2 = layout.Waypoints[3];
                int px = Math.Sign(wp2.X - wp1.X), pz = Math.Sign(wp2.Z - wp1.Z);   // along the long leg
                int ux = pz != 0 ? 1 : 0, uz = px != 0 ? 1 : 0;                      // across it
                ParkCell Along(int k) => wp1.Offset(2 * px * k, 2 * pz * k);
                PathGhost.Verdict One(ParkCell c) { ghost.Set(c.X, c.Z, c.X, c.Z, PathTool.Kind.Path); return ghost.Tiles[0].Verdict; }
                var a = Along(3); var b = Along(4);
                var pa = layout.PieceAt(a); var pb = layout.PieceAt(b);
                Check(pa?.Info.Shape == 0 && pb?.Info.Shape == 0 && pa.Anchor == a && pb.Anchor == b,
                      $"the long leg's middle pieces are plain straights ({pa}, {pb})");
                Check(One(a) == PathGhost.Verdict.Lay, $"a path may go under a straight ({a}: {One(a)})");
                var bend = layout.PieceAt(wp1);
                Check(bend?.Info.Shape is 1 or 2 && One(wp1) == PathGhost.Verdict.Refused,
                      $"but not under a bend ({bend}: {One(wp1)})");
                var exitPiece = layout.PieceAt(layout.ExitCell);
                Check(exitPiece?.Info.Shape == 0 && One(layout.ExitCell) == PathGhost.Verdict.Refused,
                      $"nor on the station's exit block, though the piece there is a straight ({exitPiece}: {One(layout.ExitCell)})");
                bool Across(ParkCell c)
                {
                    ghost.Set(c.X - ux, c.Z - uz, c.X + 2 * ux, c.Z + 2 * uz, PathTool.Kind.Path);
                    if (!ghost.Layable) return false;
                    paths.BeginLeg();
                    return ghost.Lay(PathTool.Kind.Path, 0) == 4;
                }
                bool laidA = Across(a);
                var ha = layout.PieceAt(a);
                Check(laidA && ha?.Type is >= 24 and <= 27 && paths.KindAt(a.X, a.Z) == PathTool.Kind.Path,
                      $"a path laid across it turns that straight into a hump ({ha}), the path under it ({paths.KindAt(a.X, a.Z)})");
                Check(layout.Closed && layout.Pieces.Count == 22 && trackSim.Status != TrackRideStatus.Closed,
                      $"the re-laid track is the same loop: {layout.Pieces.Count} pieces, closed, {trackSim.Status}");
                bool laidB = Across(b);
                var ua = layout.PieceAt(a); var db = layout.PieceAt(b);
                Check(laidB && ua?.Type is >= 28 and <= 31 && db?.Type is >= 36 and <= 39,
                      $"a second path beside it makes ramp-up then ramp-down ({ua}, {db})");
                Check(One(a) == PathGhost.Verdict.Already || One(a) == PathGhost.Verdict.Lay,
                      $"the path under the ramp is still path ground to the tool ({One(a)})");
                int drawnNow = layout.Pieces.Count(p => p.Info.Shape is not (15 or 99));
                Check(pieces.Count == drawnNow && pieces.Cast<Node3D>().All(n => IsInstanceValid(n) && n.FindChildren("*", "MeshInstance3D", true, false).Count > 0),
                      $"the ramps are drawn ({pieces.Count} of {drawnNow})");
                aimAt = ((Node3D)pieces[layout.Pieces.Where(p => p.Info.Shape is not (15 or 99)).ToList().IndexOf(ua)]).GlobalPosition;
                aimFar = 5f;
                await Shot("bridge");
                aimAt = null; aimFar = null;
                paths.UndoLeg();
                var back = layout.PieceAt(b); var hump = layout.PieceAt(a);
                Check(back?.Info.Shape == 0 && hump?.Type is >= 24 and <= 27,
                      $"undoing the second path puts its straight back and the first is a hump again ({hump}, {back})");
            }

            // ⭐ ADD-ONS (tool mode 10): the build menu lists this park's catalogue in order, a pick arms
            // the tool, the ghost judges 0x1FEA58's box, and a press lays the add-on into the chain.
            {
                int addonWorld = (int)typeof(Viewer).GetProperty("TrackWorld", Hidden).GetValue(viewer);
                int addonPark = (int)typeof(Viewer).GetProperty("TrackPark", Hidden).GetValue(viewer);
                var sold = TrackUpgrades.ForPark(addonWorld, addonPark);
                Call(viewer, "ShowBuildCategory", "upgrades");
                var addonRows = Field<List<int>>(viewer, "_buildRows");
                var keys = addonRows.Select(r => (uint?)Call(viewer, "DbaKey", lib.Rides[r])).ToList();
                Check(keys.SequenceEqual(sold.Select(k => (uint?)k)),
                      $"{world} park {addonPark + 1}: Addons lists this park's catalogue, in order [{string.Join(",", keys)}] (want [{string.Join(",", sold)}])");
                if (sold.Count > 0)
                {
                    var wp1 = layout.Waypoints[2];
                    var wp2 = layout.Waypoints[3];
                    int px = Math.Sign(wp2.X - wp1.X), pz = Math.Sign(wp2.Z - wp1.Z);
                    ParkCell Along(int k) => wp1.Offset(2 * px * k, 2 * pz * k);
                    var first = layout.PieceAt(Along(1));
                    int d = first.Type - 4;
                    var box = d switch { 0 => first.Anchor.Offset(-1, 0), 1 => first.Anchor.Offset(-1, -2), 2 => first.Anchor.Offset(0, -1), _ => first.Anchor.Offset(-2, -1) };
                    Check(first.Type is >= 4 and <= 7 && layout.PieceAt(Along(2)).Type == first.Type,
                          $"the long leg's first two pieces after the bend are straights of one type ({first}, {layout.PieceAt(Along(2))})");
                    int paidBefore = sim.Finances.Balance;
                    Call(viewer, "ArmFromList", sold.Count - 1);
                    var tool = Member("_addonTool").GetValue(viewer);
                    Check(tool != null && (int)F(tool, "Kind") == sold.Count - 1, $"picking the park's last add-on arms the add-on tool, kind {sold.Count - 1}");
                    int addonPrice = (int)F(tool, "Price");
                    // Off in the grass: no pair of straights, so nothing to buy.
                    var far = Enumerable.Range(0, 40).Select(n => new ParkCell(wp1.X - 6 * pz - n * pz, wp1.Z + 6 * px + n * px))
                        .Concat(Enumerable.Range(0, 40).Select(n => new ParkCell(wp1.X + 6 * pz + n * pz, wp1.Z - 6 * px - n * px)))
                        .First(c => park.IsPlayable(c.X, c.Z) && layout.PieceAt(c) == null && Enumerable.Range(-4, 9).All(o => layout.PieceAt(c.Offset(o, 0)) == null && layout.PieceAt(c.Offset(0, o)) == null));
                    Set(viewer, "_cursorOverride", (far.X, far.Z));
                    try { Call(viewer, "UpdateAddonGhost"); Call(viewer, "PressAddonTool"); }
                    finally { Set(viewer, "_cursorOverride", null); }
                    Check(!(bool)F(tool, "Valid") && layout.Upgrades.Count == 0 && Member("_addonTool").GetValue(viewer) == tool,
                          $"on bare ground ({far}) the ghost is refused and a press buys nothing");
                    // On the track, one cell in: the port tries every box over the cursor and keeps the valid one.
                    var aim = Along(1).Offset(px != 0 ? 0 : 1, pz != 0 ? 0 : 1);
                    Set(viewer, "_cursorOverride", (aim.X, aim.Z));
                    try { Call(viewer, "UpdateAddonGhost"); }
                    finally { Set(viewer, "_cursorOverride", null); }
                    var got = (ParkCell)F(tool, "Box");
                    var preview = (Node3D)F(tool, "Preview");
                    Check((bool)F(tool, "Valid") && got == box && preview != null && preview.FindChildren("*", "MeshInstance3D", true, false).Count > 0,
                          $"over the straights the ghost finds 0x1FEA58's box {got} (want {box}) and shows the add-on's model");
                    aimAt = preview.GlobalPosition; aimFar = 6f;
                    await Shot("addon_ghost");
                    Set(viewer, "_cursorOverride", (aim.X, aim.Z));
                    try { Call(viewer, "PressAddonTool"); }
                    finally { Set(viewer, "_cursorOverride", null); }
                    int slot = layout.Pieces.ToList().FindIndex(p => p.Type >= 40);
                    var up = slot < 0 ? null : layout.Pieces[slot];
                    Check(layout.Upgrades.Count == 1 && up != null && up.Type == 40 + (sold.Count - 1) * 4 + d && up.Anchor == box
                          && layout.Pieces[slot + 1].Type == 8 + d && layout.Closed && layout.Pieces.Count == 22,
                          $"a press lays it: {up} at slot {slot}, connector {layout.Pieces[slot + 1]} after it, still a closed loop of {layout.Pieces.Count}");
                    Check(sim.Finances.Unlimited || paidBefore - sim.Finances.Balance == addonPrice * 10,
                          $"and charges its price, {addonPrice} x 10 = {Money.Format(addonPrice * 10)} ({paidBefore - sim.Finances.Balance})");
                    Check(Member("_addonTool").GetValue(viewer) == null && !IsInstanceValid(preview) || preview.IsQueuedForDeletion(),
                          "the tool closes and the preview goes");
                    var addonNodes = pieces.Cast<Node3D>().Where(n => IsInstanceValid(n) && !n.IsQueuedForDeletion() && n.Name.ToString().StartsWith("addon_")).ToList();
                    Check(addonNodes.Count == 1 && addonNodes[0].FindChildren("*", "MeshInstance3D", true, false).Count > 0,
                          $"the add-on is drawn ({string.Join(",", addonNodes.Select(n => n.Name))})");
                    var lift = TrackUpgrades.Offset(addonWorld, addonPark, sold.Count - 1, 1);
                    Check(up.Samples[1].Height == up.Samples[0].Height + lift.Y,
                          $"the cars' height through it follows the park's offsets ({string.Join(",", up.Samples.Select(q => q.Height))}; +{lift.Y} at sample 1)");
                    aimAt = addonNodes[0].GlobalPosition; aimFar = 6f;
                    await Shot("addon");
                    aimAt = null; aimFar = null;
                }
                else
                {
                    Check(addonRows.Count == 0, $"{world} park {addonPark + 1} sells no add-ons, and the menu offers none");
                }
            }

            // Deleting the ride takes the track with it.
            Set(viewer, "_selected", 0);
            Call(viewer, "DeleteSelected");
            Check(tracks.Count == 0 && !IsInstanceValid(frame) || frame.IsQueuedForDeletion(), "deleting the station removes its track");

            Field<RideSounds>(viewer, "_sounds")?.Clear();
            viewer.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            GD.Print($"TRACK RIDE SMOKE PASS checks={_checks}; world={world}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"TRACK RIDE SMOKE FAIL world={world} checks={_checks}: {ex}");
            GetTree().Quit(2);
        }
    }
}
