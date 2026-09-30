using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>The pointer against the park, on the shipping Viewer (strawberry, 2026-09-30): hover picks the TILES a
/// thing occupies rather than its model's box; the gate has a right-click menu offering Open Park; opening it
/// sounds the gate. Run with --map=WORLD --mode=park.
///
/// ⭐ Real mouse motion is pushed through the viewport at points projected from the park, so the live
/// `GetMousePosition` path is what is tested -- not `_cursorOverride`, which was always tile-based and would
/// pass whatever the live path did. And each hover claim carries its control: a point on the model's own box
/// whose floor is NOT one of its tiles, which the old box test took and this one must not.</summary>
public partial class PointerTilesSmoke : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string name) => typeof(Viewer).GetField(name, Hidden)
        ?? throw new MissingMemberException("Viewer." + name);
    static T Field<T>(Viewer v, string name) => (T)Member(name).GetValue(v);
    static void Set(Viewer v, string name, object value) => Member(name).SetValue(v, value);
    static object Call(Viewer v, string name, params object[] args)
        => (typeof(Viewer).GetMethod(name, Hidden) ?? throw new MissingMemberException("Viewer." + name)).Invoke(v, args);
    static object Get(Viewer v, string name)
        => (typeof(Viewer).GetProperty(name, Hidden) ?? throw new MissingMemberException("Viewer." + name)).GetValue(v);
    int _checks;
    void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++;
        GD.Print("POINTER TILES ok: " + label);
    }

    public override async void _Ready()
    {
        string world = "unknown";
        try
        {
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            string map = args.LastOrDefault(a => a.StartsWith("--map="))?["--map=".Length..]
                ?? throw new ArgumentException("Pass --map=WORLD");
            world = new[] { "JUNGLE", "FANTASY", "HALLOW", "SPACE" }.Single(w =>
                map.Equals(w, StringComparison.OrdinalIgnoreCase) || map.StartsWith(w + " ", StringComparison.OrdinalIgnoreCase));

            var viewer = new Viewer { Name = "Viewer" };
            AddChild(viewer);
            viewer.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(Field<object>(viewer, "_mode").ToString() == "Park", "actual Viewer is in Park mode");
            Call(viewer, "StepPark", .04);
            // ⚠ THE CAMERA IS PLACED BY _Process, and with it held the camera sat at its default transform, at floor
            // level -- the floor was edge-on and every pointer ray hit near the origin (the first run's diagnostic).
            // A few real frames put the park camera where a player's is; then it is held still again.
            viewer.SetProcess(true);
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            viewer.SetProcess(false);
            if (Field<Control>(viewer, "_panel") is { } panel) panel.Visible = false;   // PointedAt ignores the panel's strip
            var park = Field<Park>(viewer, "_park");
            var cam = Field<Camera3D>(viewer, "_cam");
            var size = GetViewport().GetVisibleRect().Size;
            bool OnScreen(Vector3 p) => !cam.IsPositionBehind(p) && new Rect2(Vector2.Zero, size).HasPoint(cam.UnprojectPosition(p));
            // ⚠ WARPED, NOT PUSHED. On the root window GetMousePosition asks the display server where the pointer
            // really is, so an event pushed through the viewport never moves it -- the first version of this did that,
            // and every hover read "nothing" whatever the picker did. The pointer is warped (xvfb has a real one) and
            // the instrument checks it landed before any hover is believed.
            int misplaced = 0;
            int HoverAt(Vector2 screen)
            {
                Input.WarpMouse(screen);
                if (GetViewport().GetMousePosition().DistanceTo(screen) > 1.5f) misplaced++;
                Call(viewer, "UpdateHover");
                return Field<int>(viewer, "_hovered");
            }
            const int GateIndex = -2;

            // --- a placed thing: any ride from the list, on the first cell it fits, well away from the gate --------------
            Call(viewer, "ShowBuildCategory", "Rides");
            var rows = Field<List<int>>(viewer, "_buildRows");
            var blueprint = Field<Placement>(viewer, "_place");
            int placedAt = -1;
            // Nearest the middle first, so the default park camera has it in view.
            var cells = Enumerable.Range(2, park.Width - 4).SelectMany(x => Enumerable.Range(2, park.Height - 4).Select(z => (x, z)))
                .OrderBy(c => (c.x - park.Width / 2) * (c.x - park.Width / 2) + (c.z - park.Height / 2) * (c.z - park.Height / 2)).ToList();
            for (int r = 0; r < rows.Count && placedAt < 0; r++)
            {
                Call(viewer, "ArmFromList", r);
                foreach (var (x, z) in cells)
                    {
                        if (placedAt >= 0) break;
                        if (!blueprint.Fits(park, x, z)) continue;
                        int count = park.Placed.Count;
                        Set(viewer, "_cursorOverride", (x, z));
                        try { Call(viewer, "PlaceHeld"); } finally { Set(viewer, "_cursorOverride", null); }
                        if (park.Placed.Count == count + 1) placedAt = count;
                    }
            }
            Call(viewer, "CloseTool");
            // ⚠ The blueprint stays armed after a placement (for placing another), and UpdateHover shows nothing while
            // one owns the cursor -- master's rule. Put it down, as Escape does, before asking what the pointer is over.
            blueprint.Clear();
            Set(viewer, "_toolOpen", false);
            Field<GhostMarkers>(viewer, "_ghostView")?.Clear();
            Check(placedAt >= 0, $"a ride from the build list stands in the park ({(placedAt >= 0 ? park.Placed[placedAt].Name : "none")})");
            var p = park.Placed[placedAt];
            // Look at it, and let the camera glide there before any probe.
            Call(viewer, "LookAtCell", p.X + p.Fp.Width / 2, p.Y + p.Fp.Height / 2, null);
            viewer.SetProcess(true);
            for (int i = 0; i < 90; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            viewer.SetProcess(false);

            // Its tiles: every footprint cell on screen must hover it.
            int tiles = 0, tileHits = 0;
            for (int fy = 0; fy < p.Fp.Height; fy++)
                for (int fx = 0; fx < p.Fp.Width; fx++)
                {
                    if (!p.Fp.Cells[fx, fy]) continue;
                    var c = park.CellCentre(p.X + fx, p.Y + fy);
                    var floor = new Vector3(c.X, park.BaseY, c.Z);
                    if (!OnScreen(floor)) continue;
                    tiles++;
                    if (HoverAt(cam.UnprojectPosition(floor)) == placedAt) tileHits++;
                    else if (tiles - tileHits == 1)
                    {
                        var cellArgs = new object[] { cam.UnprojectPosition(floor), -1, -1 };
                        bool got = (bool)(typeof(Viewer).GetMethod("CellAtScreen", Hidden)!.Invoke(viewer, cellArgs));
                        GD.Print($"[diag] want cell ({p.X + fx},{p.Y + fy}); CellAtScreen={got} ({cellArgs[1]},{cellArgs[2]}); "
                               + $"PlacedIndexAt there={Call(viewer, "PlacedIndexAt", (int)cellArgs[1], (int)cellArgs[2])}; "
                               + $"PointedAt={Call(viewer, "PointedAt")}; toolOpen={Field<bool>(viewer, "_toolOpen")} place={blueprint.Active}; "
                               + $"mouse={GetViewport().GetMousePosition()} wanted {cam.UnprojectPosition(floor)}; placed at ({p.X},{p.Y}) index {placedAt} of {park.Placed.Count}; hovered={Field<int>(viewer, "_hovered")}");
                        var ms = GetViewport().GetMousePosition();
                        var o = cam.ProjectRayOrigin(ms); var d = cam.ProjectRayNormal(ms);
                        var h = o + d * ((park.BaseY - o.Y) / d.Y);
                        GD.Print($"[diag] field={(park.Field != null)} sameCam={ReferenceEquals(cam, Field<Camera3D>(viewer, "_cam"))} camCurrent={cam.Current} "
                               + $"floor={floor} hit={h} baseY={park.BaseY} cellsize={Park.CellSize} fieldWH={park.Field?.Width}x{park.Field?.Height} parkWH={park.Width}x{park.Height}");
                    }
                }
            Check(misplaced == 0, $"the pointer lands where it is warped ({tiles} probes, {misplaced} off) -- the instrument moved");
            Check(tiles > 0 && tileHits == tiles, $"{p.Name}: {tileHits} of {tiles} footprint tiles on screen hover it");

            // The control: points on the model's own box whose floor is NOT one of its tiles. The box test took them.
            var (lo, hi) = Park.DrawnBounds(p.Node, inParent: true);
            int boxOnly = 0, boxOnlyTaken = 0;
            for (int i = 0; i <= 8; i++)
                for (int j = 0; j <= 8; j++)
                {
                    var q = new Vector3(Mathf.Lerp(lo.X, hi.X, i / 8f), hi.Y, Mathf.Lerp(lo.Z, hi.Z, j / 8f));
                    if (!OnScreen(q)) continue;
                    var s = cam.UnprojectPosition(q);
                    var from = cam.ProjectRayOrigin(s); var dir = cam.ProjectRayNormal(s);
                    float t = (park.BaseY - from.Y) / dir.Y;
                    var hit = from + dir * t;
                    bool onOwnTile = Enumerable.Range(0, p.Fp.Height).Any(fy => Enumerable.Range(0, p.Fp.Width).Any(fx =>
                    {
                        if (!p.Fp.Cells[fx, fy]) return false;
                        var c = park.CellCentre(p.X + fx, p.Y + fy);
                        return Mathf.Abs(hit.X - c.X) <= Park.CellSize / 2 && Mathf.Abs(hit.Z - c.Z) <= Park.CellSize / 2;
                    }));
                    if (onOwnTile) continue;
                    boxOnly++;
                    if (HoverAt(s) == placedAt) boxOnlyTaken++;
                }
            Check(boxOnly > 0 && boxOnlyTaken == 0,
                  $"the control: {boxOnly} points on {p.Name}'s box top whose floor is off its tiles; {boxOnlyTaken} of them hover it (the box test took all of them)");

            // --- the gate: its tiles hover it, its arch above them does not ----------------------------------------------
            if (Field<(int X, int Y)?>(viewer, "_gateCell") is { } gcell)
            {
                Call(viewer, "LookAtCell", gcell.X, gcell.Y, null);
                viewer.SetProcess(true);
                for (int i = 0; i < 90; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                viewer.SetProcess(false);
            }
            var gb = Field<(Vector3 Lo, Vector3 Hi)?>(viewer, "_gateBounds");
            Check(gb != null, "the gate's tile rectangle is known");
            var (glo, ghi) = gb.Value;
            var gateFloor = new Vector3((glo.X + ghi.X) / 2, glo.Y, (glo.Z + ghi.Z) / 2);
            Check(OnScreen(gateFloor) && HoverAt(cam.UnprojectPosition(gateFloor)) == GateIndex, "the middle of the gate's tiles hovers the gate");
            var gateRoot = Field<AnimatedModel>(viewer, "_gate").Root;
            var (alo, ahi) = Park.DrawnBounds(gateRoot, inParent: true);
            var archTop = new Vector3((alo.X + ahi.X) / 2, ahi.Y, (alo.Z + ahi.Z) / 2);
            {
                var s = cam.UnprojectPosition(archTop);
                var from = cam.ProjectRayOrigin(s); var dir = cam.ProjectRayNormal(s);
                var hit = from + dir * ((glo.Y - from.Y) / dir.Y);
                bool floorInZone = hit.X >= glo.X && hit.X <= ghi.X && hit.Z >= glo.Z && hit.Z <= ghi.Z;
                int h = HoverAt(s);
                Check(OnScreen(archTop) && (floorInZone ? h == GateIndex : h != GateIndex),
                      $"the arch's top: its floor is {(floorInZone ? "inside" : "outside")} the gate's tiles, and it hovers {(h == GateIndex ? "the gate" : h.ToString())}");
            }

            // --- the gate's right-click menu: Open Park while closed, nothing once open --------------------------------
            Check(!Field<bool>(viewer, "_laptopParkOpen"), "the park loads closed");
            var at = cam.UnprojectPosition(gateFloor);
            HoverAt(at);
            bool opened = (bool)Call(viewer, "OpenMenuUnderCursor", at);
            var menu = Field<ObjectMenu>(viewer, "_objMenu");
            string caption = (string)Get(viewer, "OpenParkCaption");
            Check(opened && menu.Open && Field<bool>(viewer, "_gateSelected"),
                  $"a right click on the gate's tiles selects it and opens its menu, offering \"{caption}\" (text 617)");
            menu.Hide();
            Call(viewer, "OnObjectMenu", caption);
            Check(Field<bool>(viewer, "_laptopParkOpen"), "choosing it opens the park (0x14E4C0's flag)");
            HoverAt(at);
            Check(!(bool)Call(viewer, "OpenMenuUnderCursor", at), "once open, the gate offers nothing -- the laptop's own condition");

            // --- the opening sound: the world's gate clip, once ---------------------------------------------------------
            Call(viewer, "StepGateSound");
            Call(viewer, "StepGateSound");
            var sounds = Field<RideSounds>(viewer, "_sounds");
            var gateLines = sounds?.Census.Where(l => l.Contains(" gate ")).ToList() ?? new List<string>();
            string clip = world switch { "JUNGLE" => "jungleGteOp", "HALLOW" => "hallowGteOp", "FANTASY" => "FantGteOp", _ => "spacegate1" };
            Check(gateLines.Count == 1 && gateLines[0].Contains(clip, StringComparison.OrdinalIgnoreCase),
                  $"opening sounds the gate once, the world's own clip {clip}: {string.Join(" | ", gateLines)}");

            GD.Print($"POINTER TILES SMOKE PASS world={world} checks={_checks}");
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.PrintErr($"POINTER TILES SMOKE FAIL world={world} checks={_checks}: {e.GetBaseException()}");
            GetTree().Quit(1);
        }
    }
}
