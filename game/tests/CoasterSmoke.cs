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
/// picks a coaster (default: the park's first on terrain 1, its last on terrain 2); `TPW_COASTER_TURNS`
/// turns the station.</summary>
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

    /// <summary>The audit's oval, in the exit's own frame: (along the station's travel, to one side).
    /// By hand only, never in the matrix: `TPW_COASTER_RING="F,S;F,S;..."` and `TPW_COASTER_HILLS="h,h,..."` lay
    /// another ring and raise it to other heights -- e.g. a self-crossing ring whose stacked crest rates Ultimate.</summary>
    static readonly (int F, int S)[] Oval =
        System.Environment.GetEnvironmentVariable("TPW_COASTER_RING") is { Length: > 0 } ringEnv
            ? ringEnv.Split(';').Select(p => p.Split(',')).Select(p => (int.Parse(p[0]), int.Parse(p[1]))).ToArray()
            : new[] { (6, 0), (11, 4), (11, 10), (6, 14), (0, 14), (-6, 14), (-11, 10), (-11, 5), (-9, 0) };
    static readonly int[] Hills =
        System.Environment.GetEnvironmentVariable("TPW_COASTER_HILLS") is { Length: > 0 } hillsEnv
            ? hillsEnv.Split(',').Select(int.Parse).ToArray()
            : new[] { 375, 800, 1200, 1200, 400, 100, 300, 375, 375 };

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
            // Terrain 2 builds the park's LAST listed coaster (the list is in WAD order), so the matrix's
            // eight runs cover eight coasters rather than four twice, Temple of Gloom's lattice check among
            // them. (The second listed missed Temple: JUNGLE lists Gorilla Thrilla there.)
            int terrain = map.Contains("terrain_2", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
            int chosen = want == null ? (terrain == 2 ? rows.Count - 1 : 0) : Enumerable.Range(0, rows.Count).First(r =>
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
            // ⭐ The two track chevrons (166) point the way the track runs: out at the exit, in at the
            // entry, which is the same way. Read off the DRAWN preview, not off the turn formula: the
            // chevron's point is its texture's left edge, so its direction on the ground is the vertex
            // at UV (0,0) minus the one at (1,0) (strawberry: "the i/o tiles for track into the station
            // are facing the wrong way" -- they were a quarter off).
            {
                Set(viewer, "_cursorOverride", spot.Value);
                try { Call(viewer, "UpdatePlacementGhost"); } finally { Set(viewer, "_cursorOverride", null); }
                var (cx0, cy0) = blueprint.CornerFor(spot.Value.X, spot.Value.Y);
                var (ex0, st0, en0) = Ends(cx0, cy0);
                var (fx0, fz0) = Dir(st0);
                var run = park.CellCentre(ex0.X, ex0.Z) - park.CellCentre(ex0.X - fx0, ex0.Z - fz0);
                run.Y = 0; run = run.Normalized();
                var ghostRoot = ((GhostMarkers)Member("_ghostView").GetValue(viewer)).Root;
                foreach (var (cell, name) in new[] { (ex0, "exit"), (en0, "entry") })
                {
                    var at = park.CellCentre(cell.X, cell.Z);
                    Vector3? u0 = null, u1 = null;
                    foreach (var mi in ghostRoot.GetChildren().OfType<MeshInstance3D>())
                    {
                        var arr = mi.Mesh.SurfaceGetArrays(0);
                        var vs = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array(); var uvs = arr[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                        for (int i = 0; i < vs.Length; i++)
                        {
                            var w = mi.GlobalTransform * vs[i];
                            if (MathF.Abs(w.X - at.X) > Park.CellSize * 0.51f || MathF.Abs(w.Z - at.Z) > Park.CellSize * 0.51f) continue;
                            if (uvs[i].DistanceTo(new Vector2(0, 0)) < 0.01f) u0 = w;
                            if (uvs[i].DistanceTo(new Vector2(1, 0)) < 0.01f) u1 = w;
                        }
                    }
                    Check(u0 != null && u1 != null, $"the {name} chevron is drawn at {cell}");
                    var tip = u0.Value - u1.Value; tip.Y = 0; tip = tip.Normalized();
                    Check(tip.Dot(run) > 0.99f, $"the {name} chevron at {cell} points the way the track runs (dot {tip.Dot(run):F2})");
                }
                if (shots != null)
                {
                    foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = false;
                    Set(viewer, "_freeCam", true);
                    var cam = Field<Camera3D>(viewer, "_cam");
                    var aim = park.CellCentre(ex0.X, ex0.Z).Lerp(park.CellCentre(en0.X, en0.Z), 0.5f);
                    cam.GlobalPosition = aim + new Vector3(0.5f, 9f, 5f);
                    cam.LookAt(aim, Vector3.Up);
                    for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Call(viewer, "SaveShot", System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}_{folder.ToLowerInvariant()}_station_preview.png"));
                    foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = true;
                }
            }
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

            // ⭐ A STACK (TPW_COASTER_STACK=height, run by hand; the audit pins the base in the matrix): the
            // audit's pentagon back onto its first pylon, the upper pylon at the given height. On the console
            // the upper pylon's own post stands on the lower one's stacker, at its own angle (strawberry's
            // capture). Its drawn foot must meet the stacker's drawn top -- the helper the base is read from.
            if (System.Environment.GetEnvironmentVariable("TPW_COASTER_STACK") is { } stackH)
            {
                var (sfx, sfz) = Dir(step); int ssx = -sfz * side, ssz = sfx * side;
                (int F, int S)[] penta = { (6, 0), (11, 3), (11, 9), (6, 11), (2, 6), (6, 0) };
                var e0 = track.Exit.Cell;
                for (int i = 0; i < penta.Length; i++)
                    track.AddPylon(e0.Offset(penta[i].F * sfx + penta[i].S * ssx, penta[i].F * sfz + penta[i].S * ssz),
                                   i == penta.Length - 1 ? int.Parse(stackH) : 375, 0, false, CoasterNodeKind.Normal);
                var upper = track.Last; var low = upper.Below;
                Check(low != null && track.IsValid(upper, (CoasterTrack.IGround)Call(viewer, "get_Ground"), true),
                      $"a pylon h{upper.Height} stacks on the first (h{low?.Height}) and is valid");
                Call(viewer, "RebuildCoaster", view);
                var pyl = (IDictionary)F(view, "Pylons");
                (float Lo, float Hi, bool Shown) Span(CoasterNode nn, string part)
                {
                    float lo = float.MaxValue, hi = float.MinValue; bool shown = false;
                    foreach (var mi in ((Node3D)pyl[nn]).FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                    {
                        if (mi.Mesh == null || !mi.GetParent().Name.ToString().Contains(part, StringComparison.OrdinalIgnoreCase) && !mi.Name.ToString().Contains(part, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!mi.IsVisibleInTree()) continue;
                        shown = true;
                        var box = mi.GlobalTransform * mi.GetAabb();
                        lo = Math.Min(lo, box.Position.Y); hi = Math.Max(hi, box.End.Y);
                    }
                    return (lo, hi, shown);
                }
                var mesh0 = new Model(lib.Read(lib.Rides.First(r => r.Model != null && r.Model.Path.EndsWith($"/{type.PylonFolder}/stdpylon.mps", StringComparison.OrdinalIgnoreCase)).Model));
                string post = mesh0.Meshes[0].Name, stacker = mesh0.Meshes.Count > 1 ? mesh0.Meshes[1].Name : null;
                var up = Span(upper, post); var sk = stacker == null ? (Lo: 0f, Hi: 0f, Shown: false) : Span(low, stacker);
                Check(up.Shown && sk.Shown, $"the upper pylon's post ({post}) is drawn, and the lower one's stacker ({stacker})");
                Check(MathF.Abs(up.Lo - sk.Hi) < 0.05f, $"the upper post stands on the stacker's top: foot {up.Lo:F2}, stacker top {sk.Hi:F2}");
                if (shots != null)
                {
                    foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = false;
                    Set(viewer, "_freeCam", true);
                    var cam = Field<Camera3D>(viewer, "_cam");
                    var aim = park.CellCentre(upper.CellX, upper.CellZ) + new Vector3(0, 3f, 0);
                    cam.GlobalPosition = aim + new Vector3(4f, 2f, 5f);
                    cam.LookAt(aim, Vector3.Up);
                    for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Call(viewer, "SaveShot", System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}_{folder.ToLowerInvariant()}_stack_{stackH}.png"));
                }
                GD.Print($"COASTER SMOKE PASS checks={_checks}; world={world} (stack)");
                GetTree().Quit(0);
                return;
            }
            // Lay the ring press by press, then close it on the entry cell.
            var ring = OvalOf(track.Exit.Cell, step, side);
            var sim = Field<ParkSim>(viewer, "_sim");
            for (int ri = 0; ri < ring.Length; ri++)
            {
                var c = ring[ri];
                Set(viewer, "_cursorOverride", (c.X, c.Z));
                try { Call(viewer, "UpdateCoasterGhost", 0.0); Call(viewer, "PressCoasterTool"); }
                finally { Set(viewer, "_cursorOverride", null); }
                if (ri == 2) await FieldCheck(c, ring[ri + 1]);
                if (ri == ring.Length - 1) await EntryCheck(c);
            }
            // ⭐ The entry cell, once the ring can close on it, wears the field (171) under its chevron (166):
            // strawberry, "show both the i/o icon and the 'can build here' icon under it".
            async Task EntryCheck(ParkCell at)
            {
                Set(viewer, "_cursorOverride", (at.X, at.Z));
                try { for (int f = 0; f < 6; f++) Call(viewer, "UpdateCoasterGhost", 0.0); }
                finally { Set(viewer, "_cursorOverride", null); }
                var field = Field<List<ParkCell>>(viewer, "_coasterField");
                var entry = track.Entry.Cell;
                Check(field.Contains(entry), $"the ring can close on the entry cell {entry}, so the field lists it");
                // Cursor off the entry, on another listed cell, so the entry shows its chevron.
                var other = field.First(q => q != entry);
                Set(viewer, "_cursorOverride", (other.X, other.Z));
                try { Call(viewer, "UpdateCoasterGhost", 0.0); }
                finally { Set(viewer, "_cursorOverride", null); }
                var root = ((GhostMarkers)Member("_ghostView").GetValue(viewer)).Root;
                var at0 = park.CellCentre(entry.X, entry.Z);
                // (Not the ones queued for deletion: each redraw QueueFrees the last, and they linger to frame end.)
                int layers = root.GetChildren().OfType<MeshInstance3D>().Where(mi => !mi.IsQueuedForDeletion()).Count(mi =>
                {
                    var vs = mi.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    for (int i = 0; i + 2 < vs.Length; i += 3)
                    {
                        var ctr = mi.GlobalTransform * ((vs[i] + vs[i + 1] + vs[i + 2]) / 3f);
                        if (MathF.Abs(ctr.X - at0.X) < Park.CellSize * 0.5f && MathF.Abs(ctr.Z - at0.Z) < Park.CellSize * 0.5f) return true;
                    }
                    return false;
                });
                Check(layers == 2, $"the entry cell carries both markers ({layers} layers: the field under the chevron)");
                if (shots != null)
                {
                    foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = false;
                    Set(viewer, "_freeCam", true);
                    var cam = Field<Camera3D>(viewer, "_cam");
                    cam.GlobalPosition = at0 + new Vector3(2f, 6f, 4f);
                    cam.LookAt(at0, Vector3.Up);
                    for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Call(viewer, "SaveShot", System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}_{folder.ToLowerInvariant()}_entry.png"));
                    foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = true;
                }
            }
            // ⭐ The valid-cell field (0x11aa70, §4.7): after a press the scan restarts around the cursor,
            // 4 rows a frame, and is drawn once complete. Every cell it lists is one the next pylon could
            // take under the cheap rules, so each lies 3..8 cells (0x300..0x800) from the last pylon; the
            // ring's own next cell is among them and the last pylon's cell is not.
            async Task FieldCheck(ParkCell at, ParkCell next)
            {
                Set(viewer, "_cursorOverride", (at.X, at.Z));
                try { for (int f = 0; f < 6; f++) Call(viewer, "UpdateCoasterGhost", 0.0); }
                finally { Set(viewer, "_cursorOverride", null); }
                var field = Field<List<ParkCell>>(viewer, "_coasterField");
                // The status line names the rule that refuses (CoasterTrack.Why): on the pylon just laid,
                // the distance rule.
                Check(Field<string>(viewer, "_coasterWhy") is { } why0 && why0.StartsWith("too close"),
                      $"on the pylon just laid the tool says why not: \"{Field<string>(viewer, "_coasterWhy")}\"");
                var last = track.Last;
                double Dist(ParkCell q) => Math.Sqrt(Math.Pow((q.X - last.CellX) * 256.0, 2) + Math.Pow((q.Z - last.CellZ) * 256.0, 2));
                Check(Field<bool>(viewer, "_coasterFieldDone") && field.Count > 0 && field.All(q => Dist(q) >= 0x300 && Dist(q) <= 0x800)
                      && field.Contains(next) && !field.Contains(last.Cell),
                      $"the valid-cell field is scanned in 5 frames: {field.Count} cells, all 3..8 from the last pylon, the ring's next among them");
                // Wherever the mouse is when the scan restarts, the field is the same: the window sits on
                // the last pylon (strawberry: "some tiles that are valid targets arent being shown").
                var here = field.ToHashSet();
                Call(viewer, "RestartCoasterField");
                Set(viewer, "_cursorOverride", (at.X + 12, at.Z + 12));
                try { for (int f = 0; f < 6; f++) Call(viewer, "UpdateCoasterGhost", 0.0); }
                finally { Set(viewer, "_cursorOverride", null); }
                var away = Field<List<ParkCell>>(viewer, "_coasterField").ToHashSet();
                Check(Field<bool>(viewer, "_coasterFieldDone") && away.SetEquals(here),
                      $"the field does not follow the mouse: scanned with it 12 cells off, {away.Count} cells, the same {here.Count}");
                var ghost = (GhostMarkers)Member("_ghostView").GetValue(viewer);
                Check(ghost.Root.GetChildren().OfType<MeshInstance3D>().Count() >= 2,
                      "the field is drawn (171 beside the cursor's own tile)");
                // The field's colour follows the ghost: on the pylon just laid it is refused (175, red), on
                // the next ring cell it is valid, so the field goes to 171 -- the grey in the console shot.
                Set(viewer, "_cursorOverride", (next.X, next.Z));
                try { Call(viewer, "UpdateCoasterGhost", 0.0); }
                finally { Set(viewer, "_cursorOverride", null); }
                Check(Field<bool>(viewer, "_coasterGhostOk"), $"the ghost on the ring's next cell {next} is valid, so the field draws as 171");
                if (shots != null)
                {
                    foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = false;
                    Set(viewer, "_freeCam", true);
                    var cam = Field<Camera3D>(viewer, "_cam");
                    var aim = park.CellCentre(at.X, at.Z);
                    cam.GlobalPosition = aim + new Vector3(6f, 9f, 8f);
                    cam.LookAt(aim, Vector3.Up);
                    for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Call(viewer, "SaveShot", System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}_{folder.ToLowerInvariant()}_field.png"));
                    foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = true;
                }
            }
            // The HUD's stock and cost lines follow the tool: 32 less the pylons laid, and the pylon's price.
            Check(Member("_previewStock").GetValue(viewer) is int stockLeft && stockLeft == CoasterTrack.MaxPylons - ring.Length
                  && Member("_previewCost").GetValue(viewer) is int costNow && costNow == (int)F(view, "Price") * 10
                  && (int)Member("_previewStockTextId").GetValue(viewer) == 223,
                  $"the HUD reads Pylon Stock {Member("_previewStock").GetValue(viewer)} (32 less {ring.Length}) and Cost {Member("_previewCost").GetValue(viewer)}");
            Check(track.Pylons.Count == ring.Length && !track.Closed && track.Pylons.All(n => n.Height == type.ExitHeight),
                  $"{ring.Length} presses lay {track.Pylons.Count} pylons, every one at the station's height {type.ExitHeight}");
            Set(viewer, "_cursorOverride", (track.Entry.CellX, track.Entry.CellZ));
            try { Call(viewer, "UpdateCoasterGhost", 0.0); Call(viewer, "PressCoasterTool"); }
            finally { Set(viewer, "_cursorOverride", null); }
            Check(track.Closed && track.Valid && track.Pylons.Count == ring.Length,
                  "a press on the entry cell closes the ring without a pylon, and every node is valid");
            Check(Member("_coasterTool").GetValue(viewer) == view && Member("_coasterMode").GetValue(viewer).ToString() == "Edit",
                  "closing from the station session goes on to the pylon edit (0x11b6a4)");
            // ⭐ The pylon edit's keys do not pause the park. Through Godot's own input pipeline, so the
            // event reaches every handler it would in play: Left/Right were also the model viewer's
            // frame step and Space its play toggle, and `_playing` is the park's clock too.
            Set(viewer, "_playing", true);
            foreach (var key in new[] { Key.Left, Key.Right, Key.Space })
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Check(Field<bool>(viewer, "_playing"), "Left, Right and Space in the pylon edit leave the park running");
            // The pause moved to H (strawberry, 2026-09-28): the same pipeline, so a binding that never reaches
            // the switch fails here rather than reading as "still running".
            async System.Threading.Tasks.Task Tap(Key key)
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await Tap(Key.H);
            bool pausedByH = !Field<bool>(viewer, "_playing");
            await Tap(Key.H);
            Check(pausedByH && Field<bool>(viewer, "_playing"), "H pauses the park and H again resumes it");
            // The D-pad in the edit: raise a hill, as a player would, one pylon at a time.
            for (int i = 0; i < track.Pylons.Count; i++) track.Pylons[i].Height = Hills[i];
            track.Recompute();
            foreach (var n in track.Pylons) n.Valid = track.IsValid(n, (CoasterTrack.IGround)Call(viewer, "get_Ground"), true);
            Check(track.Valid, "the raised ring is still valid");
            // ⭐ THE STATS SCREEN (0x11bd28; strawberry: "coaster scoring is done after u finish editing pylons").
            // Triangle runs the test lap and puts the screen up; the tool stays open behind it until OK.
            Call(viewer, "FinishCoasterTool");
            var statsView = (CoasterStatsView)Member("_coasterStatsView").GetValue(viewer);
            var lapSim = (CoasterSim)F(view, "Sim");
            var lap = (CoasterStats)F(view, "Stats");
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(Member("_coasterMode").GetValue(viewer).ToString() == "Stats" && Member("_coasterTool").GetValue(viewer) == view
                  && !Field<bool>(viewer, "_toolOpen") && statsView is { Visible: true } && ReferenceEquals(statsView.Stats, lap)
                  && lap != CoasterStats.None,
                  $"finishing the pylon edit runs the lap and puts up the stats screen, the tool still open behind it ({lap})");
            // What was DRAWN, read back off the view: every label, value and unit at the console's own x/y, in
            // the EUR English text rows, then "Coaster Rating:" and the verdict 0x122ed0 gives this record.
            var expectLabels = new[] { "Duration", "Length", "Maximum Speed", "Number of Drops", "Steepest Drop",
                                       "Max Vert +Gs", "Max Vert -Gs", "Max Lat Gs" };
            var expectUnits = new[] { "secs", "meters", "kph", null, "deg", "g", "g", "g" };
            var lines = CoasterStatsScreen.Lines(lap);
            var shown = statsView.ShownText;
            bool ShownAt(string text, int x, int y) => shown.Any(t => t.Text == text && t.X == x && t.Y == y);
            bool statsRows = true;
            for (int i = 0; i < 8; i++)
                statsRows &= ShownAt(expectLabels[i], 0x32, lines[i].Y) && ShownAt(lines[i].Value, 300, lines[i].Y)
                        && (expectUnits[i] == null ? !shown.Any(t => t.X == 0x168 && t.Y == lines[i].Y) : ShownAt(expectUnits[i], 0x168, lines[i].Y));
            var (ratingRow, _) = CoasterStatsScreen.Rating(lap);
            string verdict = shown.FirstOrDefault(t => t.X == 300 && t.Y == 0x184).Text;
            Check(statsRows && ShownAt("Coaster Rating:", 0x32, 0x184) && !string.IsNullOrEmpty(verdict) && shown.Count == 25,
                  $"the screen draws the eight rows and the rating at the console's x/y: [{string.Join(" | ", shown.Select(t => t.Text))}]");
            Check(statsView.PanelBlits == 22,
                  $"the panel is 0x142090's: 13 fill rows + the 8-unit remainder + 4 corners + 4 edges ({statsView.PanelBlits} blits)");
            if (shots != null)
                Call(viewer, "SaveShot", System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}_{folder.ToLowerInvariant()}_stats.png"));
            // Back is Triangle (Escape): the screen goes, the pylon edit comes back, and the lap's trains go with it.
            await Tap(Key.Escape);
            Check(Member("_coasterMode").GetValue(viewer).ToString() == "Edit" && !statsView.Visible && statsView.Stats == null
                  && lapSim.Trains.Count == 0 && Member("_coasterTool").GetValue(viewer) == view,
                  $"Back (Escape) returns to the pylon edit with the screen down and no trains ({lapSim.Trains.Count})");
            // Circle is ignored on the screen; OK is Cross (Enter) -- from a station build it removes the trains and
            // hands over to the queue tool (0x11bca8).
            Call(viewer, "FinishCoasterTool");
            Check(Member("_coasterMode").GetValue(viewer).ToString() == "Stats" && lapSim.Trains.Count > 0,
                  $"finishing again puts the screen back up, the lap's service trains respawned ({lapSim.Trains.Count})");
            await Tap(Key.Enter);
            Check(Member("_coasterTool").GetValue(viewer) == null && Field<bool>(viewer, "_toolOpen") && !statsView.Visible
                  && lapSim.Trains.Count == 0,
                  "OK (Enter) closes the screen, removes the trains and hands over to the queue tool");
            // ⭐ The award's wiring, on THIS park before its clock has run (it once went through the calendar's
            // object and recorded nothing): bit world·8 + park·4 + ordinal, and the HUD's count with it.
            {
                var awards = Field<ParkAwards>(viewer, "_awards");
                int maskBefore = awards.UltimateMask;
                awards.UltimateMask = 0;
                var sel = NativeParkSelection.Ordinary(Field<AssetLibrary>(viewer, "_lib").WadName, (string)Member("_terrainPath").GetValue(viewer));
                bool recorded = (bool)Call(viewer, "RecordUltimateCoaster", view);
                Check(recorded && awards.HasUltimate(sel.World, sel.Variant, type.Ordinal) && awards.UltimateCoasters == 1
                      && awards.UltimateMask == 1 << (sel.World * 8 + sel.Variant * 4 + type.Ordinal),
                      $"an Ultimate verdict here records {world} park {sel.Variant} ordinal {type.Ordinal} (mask 0x{awards.UltimateMask:x})");
                awards.UltimateMask = maskBefore;
            }
            // By hand: the whole ring from above and to one side, the trains out on it.
            if (shots != null)
            {
                for (int i = 0; i < 240; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = false;
                Set(viewer, "_freeCam", true);
                var cam = Field<Camera3D>(viewer, "_cam");
                var mid = track.Pylons.Aggregate(Vector3.Zero, (a, n) => a + park.CellCentre(n.CellX, n.CellZ)) / track.Pylons.Count;
                float span = track.Pylons.Max(n => park.CellCentre(n.CellX, n.CellZ).DistanceTo(mid));
                cam.GlobalPosition = mid + new Vector3(0.9f, 0.75f, 1.1f) * span * 1.15f;
                cam.LookAt(mid + Vector3.Up * 2f, Vector3.Up);
                for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Call(viewer, "SaveShot", System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}_{folder.ToLowerInvariant()}_ring.png"));
                foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = true;
            }
            Call(viewer, "CloseTool");

            var segments = (IDictionary)F(view, "Segments");
            var pylons = (IDictionary)F(view, "Pylons");
            int expectSegs = track.Nodes().Count(n => CoasterMesh.Visible(track, n));
            Check(segments.Count == expectSegs && pylons.Count == track.Pylons.Count,
                  $"every visible segment is drawn ({segments.Count} of {expectSegs}) and every pylon ({pylons.Count})");
            Check(segments.Values.Cast<MeshInstance3D>().All(m => m.Mesh.GetSurfaceCount() > 0),
                  $"every segment has its {type.Style}-style strips");
            // ⭐ The pylons, measured rather than looked at: each stands on the drawn floor, reaches
            // the track, and turns with it. Strawberry saw them floating: the loft keys are deltas and
            // were read as positions (AnimatedModel.Additive), and the yaw sign was assumed.
            var frameT = ((Node3D)F(view, "Frame")).GlobalTransform;
            float? restTop = null;
            foreach (var n in track.Pylons)
            {
                var holder = (Node3D)pylons[n];
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var mi in holder.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                {
                    if (!mi.IsVisibleInTree() || mi.Mesh == null) continue;
                    var box = mi.GetAabb();
                    for (int c = 0; c < 8; c++)
                    {
                        var w = mi.GlobalTransform * box.GetEndpoint(c);
                        lo = Math.Min(lo, w.Y); hi = Math.Max(hi, w.Y);
                    }
                }
                float floor = park.CellY(n.CellX, n.CellZ);
                float rail = (frameT * new Vector3(n.X / 256f, n.TrackY / 256f, n.Z / 256f)).Y;
                var dummy = holder.HasMeta("track_dummy")
                    ? ((Node3D)holder.GetChild(0)).GlobalTransform * (Vector3)holder.GetMeta("track_dummy") : new Vector3(float.NaN, float.NaN, float.NaN);
                // ⭐ THE LOFT, NOT A REFERENCE POINT. The posts are authored to different things --
                // Temple's and Caterpillar's tops meet their track dummy, Chak Atak's and Hades' meet the
                // rail -- so neither is the invariant. What the loft guarantees is that every post's top
                // rises its loft per full loft (the keys' +90 at 0.1 scale, +80 on Gorilla Thrilla): top -
                // loft·h/2560 is one number per coaster. The broken absolute morph pinned the top instead.
                // Rest height = the measured top minus the loft's rise (9L on most), which must be the post's own BIND
                // top out of the .mps: the loft ADDS to the bind pose. (The collapsed absolute morph
                // rose 9L too, from nothing: rest -0.01 against a bind top of 1.0 on Chak Atak.)
                if (System.Environment.GetEnvironmentVariable("TPW_COASTER_PROBE") == "1")
                {
                    // PROBE: the drawn track near this node against the post: bottom/top gap and centre offset.
                    var cellC = holder.GlobalTransform * new Vector3(0.5f, 0, 0.5f);
                    var postLo = new Vector2(float.MaxValue, float.MaxValue); var postHi = new Vector2(float.MinValue, float.MinValue);
                    foreach (var mi in holder.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                        if (mi.IsVisibleInTree() && mi.Mesh != null)
                            for (int sfc = 0; sfc < mi.Mesh.GetSurfaceCount(); sfc++)
                                foreach (var pv in mi.Mesh.SurfaceGetArrays(sfc)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                                {
                                    var w = mi.GlobalTransform * pv;
                                    if (w.Y > hi - 0.3f) { postLo = postLo.Min(new Vector2(w.X, w.Z)); postHi = postHi.Max(new Vector2(w.X, w.Z)); }
                                }
                    var postTopC = (postLo + postHi) / 2;
                    float trB = float.MaxValue, trT = float.MinValue; Vector2 sum = Vector2.Zero; int cnt = 0;
                    foreach (var seg in segments.Values.Cast<MeshInstance3D>())
                        for (int sfc = 0; sfc < seg.Mesh.GetSurfaceCount(); sfc++)
                            foreach (var pv in seg.Mesh.SurfaceGetArrays(sfc)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                            {
                                var w = seg.GlobalTransform * pv;
                                if (new Vector2(w.X - cellC.X, w.Z - cellC.Z).Length() > 0.35f) continue;
                                trB = Math.Min(trB, w.Y); trT = Math.Max(trT, w.Y); sum += new Vector2(w.X, w.Z); cnt++;
                            }
                    var tc = cnt > 0 ? sum / cnt : Vector2.Zero;
                    GD.Print($"[probe] {type.Name} pylon ({n.CellX},{n.CellZ}) h{n.Height}: post top {hi - floor:F3} above floor, "
                           + $"track bottom {trB - hi:+0.000;-0.000} / top {trT - hi:+0.000;-0.000} from the post top ({cnt} verts); "
                           + $"track centre - post-top centre ({tc.X - postTopC.X:+0.000;-0.000}, {tc.Y - postTopC.Y:+0.000;-0.000}), "
                           + $"post-top centre - cell centre ({postTopC.X - cellC.X:+0.000;-0.000}, {postTopC.Y - cellC.Z:+0.000;-0.000}); spline {rail - hi:+0.000;-0.000}");
                }
                float rest = hi - floor - (type.LoftTo - type.LoftFrom) / 10f * Math.Clamp(n.Height / 2560f, 0f, 1f);
                float bindTop = holder.HasMeta("rest_top") ? (float)holder.GetMeta("rest_top") : float.NaN;
                restTop ??= rest;
                Check(MathF.Abs(lo - floor) < 0.05f && MathF.Abs(rest - bindTop) < 0.05f && MathF.Abs(rest - restTop.Value) < 0.05f,
                      $"pylon ({n.CellX},{n.CellZ}) h{n.Height} stands on the floor ({lo - floor:F2} off) and its top is the bind top plus the loft "
                      + $"(rest {rest:F2}, bind top {bindTop:F2}); rail {rail - hi:+0.00;-0.00} from its top, dummy {dummy.Y - hi:+0.00;-0.00}");
                // The lattice tiles up the post: additive UV keys keep the authored U and add V with the
                // loft (0x1ad378). Written as absolute UVs every U collapses to 0 and the post bands.
                // ⭐ And ALL FOUR channels add, not just the loft: incline held at 0.5 and bank at 0 each
                // put V deltas on the post even though they morph nothing there. The console's post then
                // spans V 0.911 + 7.493L (loft alone: 0.496 + 7.493L, a fifth of a cross on a low post).
                // ⚠ On top of that the port squares the lattice (CoasterPylon.SquareLattice, strawberry's
                // call, NOT the console): the lofted ring's V = its foot's 0.991 + face height / face
                // width, (0.6 + 9L) / 0.7 on MineCart's 0.7-wide post. The console's 0.911 + 7.493L fails it.
                float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
                foreach (var mi in holder.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                    if (mi.IsVisibleInTree() && mi.Mesh != null)
                        for (int sfc = 0; sfc < mi.Mesh.GetSurfaceCount(); sfc++)
                            foreach (var uv in mi.Mesh.SurfaceGetArrays(sfc)[(int)Mesh.ArrayType.TexUV].AsVector2Array())
                            { u0 = Math.Min(u0, uv.X); u1 = Math.Max(u1, uv.X); v0 = Math.Min(v0, uv.Y); v1 = Math.Max(v1, uv.Y); }
                float loft = Math.Clamp(n.Height / 2560f, 0f, 1f);
                if (type.Folder == "MineCart")
                {
                    // × the U span: the faces run U 0 → 0.999, and a square texel is square against the U drawn.
                    float ringV = 0.991f + (0.6f + 9f * loft) / 0.7f * (u1 - u0);
                    Check(u1 - u0 > 0.5f && MathF.Abs(v1 - ringV) < 0.01f,
                          $"pylon ({n.CellX},{n.CellZ}) keeps its lattice, square: U spans {u1 - u0:F3}, the ring's V is {v1:F3} for loft {loft:F3} "
                          + $"(foot 0.991 + (0.6 + 9L) / 0.7 x U span = {ringV:F3})");
                }
                var across = (holder.GlobalTransform.Basis.X).Normalized();
                var s0 = n.S[2];
                var sideV = (frameT.Basis * new Vector3(s0.X, s0.Y, s0.Z)).Normalized();
                Check(MathF.Abs(across.Dot(sideV)) > 0.99f,
                      $"pylon ({n.CellX},{n.CellZ}) turns with the track: its cross axis lies along the side vector (|dot| {MathF.Abs(across.Dot(sideV)):F3}, heading {n.Heading}+{n.HalfTurn})");
            }

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
            // The tallest post and the shortest, close up: the lattice's crosses are what gets checked by eye.
            foreach (var (tag, pick) in new[] { ("pylon_tall", track.Pylons.MaxBy(p => p.Height)), ("pylon_short", track.Pylons.MinBy(p => p.Height)) })
            {
                var box = ((Node3D)pylons[pick]).FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
                    .Where(m => m.IsVisibleInTree() && m.Mesh != null).Select(m => m.GlobalTransform * m.GetAabb()).Aggregate((x, y) => x.Merge(y));
                aimAt = box.GetCenter(); aimFar = box.Size.Y * 1.2f + 1.5f;
                await Shot(tag);
            }
            aimAt = null; aimFar = null;
            var ride = sim.Rides.Single(r => r.Coaster == csim);
            int riders = nc * type.Seats + 2;
            for (int g = 9001; g < 9001 + riders; g++) ride.Join(g);
            float top = 0; int left = 0; bool ridersChecked = false;
            var rumbleClips = new HashSet<string>();
            var cars = (IDictionary)F(view, "Cars");
            for (int i = 0; i < 9000 && left < riders; i++)
            {
                Call(viewer, "StepPark", .04);
                top = Math.Max(top, csim.Trains.Max(t => t.PrevSpeed));
                left += ride.Left.Count; ride.ClearLeft();
                if (i % 200 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (i == 700)
                {
                    await Shot("cars");
                    // The station side-on, once it has built: where the track meets the station model.
                    var frameS = frame.GlobalTransform;
                    Vector3 At(CoasterNode n) => frameS * new Vector3(n.X / 256f, n.TrackY / 256f, n.Z / 256f);
                    Vector3 ex = At(track.Exit), en = At(track.Entry);
                    if (System.Environment.GetEnvironmentVariable("TPW_COASTER_PROBE") == "1")
                        GD.Print($"[probe] {type.Name} station: exit track y {ex.Y:F3}, entry {en.Y:F3}; "
                                 + $"floor at the station {park.CellY(park.Placed[^1].X, park.Placed[^1].Y):F3}, at the exit cell {park.CellY(track.Exit.CellX, track.Exit.CellZ):F3}");
                    if (shots != null)
                    {
                        var mid = (ex + en) / 2; mid.Y = (ex.Y + park.CellY(track.Exit.CellX, track.Exit.CellZ)) / 2;
                        var run = en - ex; run.Y = 0; run = run.Normalized();
                        var across = new Vector3(-run.Z, 0, run.X);
                        foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = false;
                        Set(viewer, "_freeCam", true);
                        var cam = Field<Camera3D>(viewer, "_cam");
                        float back = (ex - en).Length() * 1.1f + 3f;
                        cam.GlobalPosition = mid + across * back + Vector3.Up * 0.6f;
                        cam.LookAt(mid, Vector3.Up);
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        Call(viewer, "SaveShot", System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}_{folder.ToLowerInvariant()}_station.png"));
                    }
                }
                if (i % 5 == 0 && Field<RideSounds>(viewer, "_sounds") is { } snd
                    && typeof(RideSounds).GetField("_voices", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(snd) is IEnumerable vs)
                    foreach (var vo in vs)
                    {
                        int owner = (int)vo.GetType().GetField("Ride").GetValue(vo);
                        if ((owner & 0x40000000) != 0) rumbleClips.Add((string)vo.GetType().GetField("Name").GetValue(vo));
                    }
                // Riders sit in their cars' seats (fitting id seat + 1, space 0x80), once some are aboard.
                if (!ridersChecked && csim.Trains.Any(t => t.State == CoasterTrainState.Run && t.Cars.Any(c => c.Riders.Count > 0)))
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Call(viewer, "SeatRiders");
                    var seated = Field<IDictionary>(viewer, "_seated");
                    foreach (var car in csim.Trains.SelectMany(t => t.Cars).Where(c => c.Riders.Count > 0))
                    {
                        var cv = cars[car];
                        var node = (Node3D)F(cv, "Node");
                        var carMesh = (Model)F(cv, "Mesh");
                        for (int s = 0; s < car.Riders.Count; s++)
                        {
                            if (carMesh.FindFitting(s + 1, 0x80) == null) { Check(!seated.Contains(car.Riders[s]), $"a rider in seat {s + 1}, which the car has no fitting for, is not drawn"); continue; }
                            Check(seated.Contains(car.Riders[s]), $"the rider in seat {s + 1} is seated");
                            var at = ((Transform3D)((System.Runtime.CompilerServices.ITuple)seated[car.Riders[s]])[0]).Origin;
                            Check(at.DistanceTo(node.GlobalPosition) < 1.2f, $"the rider in seat {s + 1} sits on their car ({at.DistanceTo(node.GlobalPosition):F2} from its origin)");
                        }
                    }
                    ridersChecked = true;
                    if (shots != null) { aimAt = ((Node3D)F(cars[csim.Trains.First(t => t.Cars.Any(c => c.Riders.Count > 0)).Cars.First(c => c.Riders.Count > 0)], "Node")).GlobalPosition; aimFar = 2.5f; await Shot("riders"); aimAt = null; aimFar = null; }
                }
            }
            Check(left == riders, $"all {riders} queued guests board, ride and come off at the exit");
            Check(ridersChecked, "riders were seen aboard a moving train and checked in their seats");
            // The rumble (category 4 event 0x11) walks its graph by parameter 7, the train's state:
            // more than one band's clip plays over a lap. Family 1 asks a map without the event.
            if (type.SoundFamily == 1) Check(rumbleClips.Count == 0, "a family-1 coaster's rumble is silent: WTRSFX.MAP has no event 0x11");
            else Check(rumbleClips.Select(n => new string(n.TakeWhile(char.IsLetter).ToArray())).Distinct().Count() >= 2,
                       $"the rumble's clip band follows the train: {string.Join(", ", rumbleClips.OrderBy(n => n))}");
            Check(top > 0.1f, $"the trains run the hill on gravity (top speed {top:F3} cells a tick)");
            Check(cars.Count == csim.Trains.Sum(t => t.Cars.Length) && cars.Count > 0, $"every car is drawn ({cars.Count})");
            foreach (var car in csim.Trains.SelectMany(t => t.Cars))
            {
                var node = (Node3D)F(cars[car], "Node");
                var want3 = frame.GlobalTransform * new Vector3(car.Pos.X, car.Pos.Y, car.Pos.Z);
                Check(node.GlobalPosition.DistanceTo(want3) < 0.6f, $"a car stands on its track point ({node.GlobalPosition.DistanceTo(want3):F2} off)");
            }
            if (shots != null && csim.Trains.Count > 0)
            {
                var lead = (Node3D)F(cars[csim.Trains[0].Cars[0]], "Node");
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
