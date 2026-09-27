using System.Reflection;
using Godot;
using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPWPS2Viewer.Tests;

/// <summary>A handyman on the shipping Viewer, end to end, through the real paths: a path and a
/// toilet laid by the build tool; the laptop's Hire row, its tabs and a candidate CLICKED through
/// the panel; the hire tool carrying him on the cursor and the real press putting him down; then the
/// park stepped until he walks (section 0, the baked walk, drawn where the sim has him), sweeps the
/// litter and the vomit dropped near him, and hides in a dirty toilet to come out with it clean.
/// findings/staff*.md; the spec is ghidra_tpw/notes/SPEC-staff-viewer.md.
///
/// Run with --map=WORLD --mode=park. `TPW_STAFF_SHOT=dir` saves the hire carry, the walk, litter and
/// vomit on the ground, and the sweep.
///
/// ⭐ Each check says what would break it, and the ones that measure a drawn quantity carry a control
/// that must disagree (the wrong yaw, the previous tick's position), so a check that cannot fail
/// is visible as one.</summary>
public partial class StaffSmoke : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string name) => typeof(Viewer).GetField(name, Hidden)
        ?? throw new MissingMemberException("Viewer." + name);
    static T Field<T>(Viewer v, string name) => (T)Member(name).GetValue(v);
    static void Set(Viewer v, string name, object value) => Member(name).SetValue(v, value);
    static object Call(Viewer v, string name, params object[] args)
    {
        var method = typeof(Viewer).GetMethod(name, Hidden) ?? throw new MissingMemberException("Viewer." + name);
        var parameters = method.GetParameters();
        if (args.Length < parameters.Length)
            args = args.Concat(parameters.Skip(args.Length).Select(p => p.HasDefaultValue ? p.DefaultValue
                : throw new ArgumentException("Missing required argument for Viewer." + name))).ToArray();
        return method.Invoke(v, args);
    }
    static T Panel<T>(LaptopShopScreen p, string name) => (T)(typeof(LaptopShopScreen).GetField(name, Hidden)
        ?? throw new MissingMemberException("LaptopShopScreen." + name)).GetValue(p);
    static object F(object o, string name) => o.GetType().GetField(name)?.GetValue(o)
        ?? throw new MissingMemberException(o.GetType().Name + "." + name);

    int _checks;
    void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++;
        GD.Print("STAFF SMOKE ok: " + label);
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
            string shots = System.Environment.GetEnvironmentVariable("TPW_STAFF_SHOT");
            if (shots != null) System.IO.Directory.CreateDirectory(shots);
            string park2 = map.Contains("terrain_2", StringComparison.OrdinalIgnoreCase) ? "2" : "1";

            viewer = new Viewer { Name = "Viewer" };
            Set(viewer, "_guestCap", 0);          // no arrivals: the handyman is the subject
            AddChild(viewer);
            viewer.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var park = Field<Park>(viewer, "_park");
            var lib = Field<AssetLibrary>(viewer, "_lib");
            var terrain = Field<Model>(viewer, "_terrainModel");
            var entrances = Field<ParkEntrance>(viewer, "_entranceTable");
            Check(park?.Field != null && terrain != null && entrances != null && lib != null, "the park loaded");
            var entry = entrances.Fit(terrain.Field, ParkEntrance.WalkwayColumnFromPoles(terrain), out _);
            Check(!entry.Empty, "the authored entrance fits");

            // A corridor in from the gate and a bar across it, laid by the path tool's own leg.
            var paths = Field<PathTool>(viewer, "_paths");
            var corridor = new List<(int X, int Y)>();
            for (int z = entry.ZEnd; z < entry.ZEnd + 10; z++) corridor.Add((entry.XCol, z));
            Call(viewer, "LayLeg", corridor, PathTool.Kind.Path, 0);
            int barZ = entry.ZEnd + 6;
            var bar = new List<(int X, int Y)>();
            for (int x = entry.XCol - 5; x <= entry.XCol + 5; x++)
                if (park.IsPlayable(x, barZ) && park.Vacant(x, barZ)) bar.Add((x, barZ));
            Call(viewer, "LayLeg", bar, PathTool.Kind.Path, 0);
            Call(viewer, "RefreshFloor");
            Check((bool)Call(viewer, "OpenGate"), "the guest layer opened on the corridor");
            var grid = Field<GuestWalk>(viewer, "_guests").Paths;
            var laid = corridor.Concat(bar).Where(c => grid.Open(new ParkCell(c.X, c.Y))).ToList();
            Check(laid.Count >= 12, $"{laid.Count} path cells are open to walk");

            // A toilet (a Feature with DBA +0x2E bit 0) placed by the build tool beside the corridor.
            Call(viewer, "ShowBuildCategory", "Feature");
            var rows = Field<List<int>>(viewer, "_buildRows");
            var loos = Enumerable.Range(0, rows.Count).Where(r =>
            {
                var a = lib.Rides[rows[r]];
                return a.Script != null && ((RideDefinition)Call(viewer, "DefinitionFor", a.Model))?.CompiledEntry?.FeatureFlag0 == true;
            }).ToList();
            Check(loos.Count > 0, $"{world}: the Features list has {loos.Count} scripted toilet(s) (DBA +0x2E bit 0)");
            var blueprint = Field<Placement>(viewer, "_place");
            bool placed = false;
            foreach (int row in loos)
            {
                for (int turn = 0; turn < 4 && !placed; turn++)
                {
                    Call(viewer, "ArmFromList", row); blueprint.Turn(turn);
                    for (int z = entry.ZEnd + 1; z < entry.ZEnd + 10 && !placed; z++)
                        for (int x = entry.XCol - 5; x <= entry.XCol + 5 && !placed; x++)
                        {
                            if (!blueprint.Fits(park, x, z)) continue;
                            var stubs = blueprint.Stubs(park, x, z).ToArray();
                            if (stubs.Length == 0 || !stubs.All(s => ParkPaths.Neighbours(new(s.X, s.Y))
                                    .Any(c => laid.Contains((c.X, c.Z))))) continue;
                            int count = park.Placed.Count;
                            Set(viewer, "_cursorOverride", (x, z));
                            try { Call(viewer, "PlaceHeld"); } finally { Set(viewer, "_cursorOverride", null); }
                            placed = park.Placed.Count == count + 1;
                        }
                }
                if (placed) break;
            }
            Check(placed, "the build tool placed a toilet beside the path");
            Call(viewer, "CloseTool");
            Call(viewer, "TickPark");
            var sim = Field<ParkSim>(viewer, "_sim");
            var visitors = Field<ParkVisitors>(viewer, "_visitors");
            var staff = Field<ParkStaff>(viewer, "_staff");
            var toilet = sim.Rides.Single(r => r.ProvidesRelief);
            Check(visitors != null && staff != null && ReferenceEquals(visitors.Staff, staff),
                  "every park: the viewer attached a ParkStaff to its visitors (so the toilet stand-in is off)");
            var features = staff.Features().ToList();
            Check(features.Any(f => f.IsToilet && ReferenceEquals(f.Ride, toilet)),
                  $"Features lists the placed toilet with DBA bit 0 ({features.Count} feature(s))");
            // Walk the park a moment so the toilet's script stands it up.
            for (int i = 0; i < 50; i++) Call(viewer, "TickPark");

            // ---------------------------------------------------------------------------------
            // The laptop, clicked. Main menu -> Hire -> Cleaners -> a candidate -> Hire.
            Call(viewer, "LoadHudFont");         // what the park's first frame does (ShowMoney)
            var panel = Field<LaptopShopScreen>(viewer, "_shopPanel");
            Check(panel != null, "the laptop panel exists");
            async Task Frames(int n = 2)
            {
                for (int i = 0; i < n; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
            }
            void Click(Vector2 at)
            {
                foreach (var ev in new InputEvent[]
                         {
                             new InputEventMouseMotion { Position = at, GlobalPosition = at },
                             new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = true },
                             new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = false },
                         })
                    panel.GetViewport().PushInput(ev, true);
            }
            var laptopBack = Field<List<(string Kind, string Arg)>>(viewer, "_laptopBack");
            Call(viewer, "ToggleLaptop");
            await Frames();
            var mainOpts = LaptopMainMenu.VisibleMain(parkOpen: Field<bool>(viewer, "_laptopParkOpen")).ToList();
            int hireRow = mainOpts.FindIndex(o => o.Opens == "main_bh_staff");
            Check(panel.Open && hireRow >= 0, $"the laptop is open and its main menu has Hire at row {hireRow}");
            Click(panel.MenuRowScreenBox(hireRow).GetCenter());
            await Frames();
            var menu = Panel<List<string>>(panel, "_menu");
            var text = Field<TextDatabase>(viewer, "_text");
            string Tab(StaffKind k) => text?.Text("eng", StaffTables.PurchaseTextRow(k)) ?? k.ToString();
            var allTabs = StaffTables.HireTabs.Select(Tab).ToList();
            Check(laptopBack.Count == 1 && laptopBack[0].Kind == "hiretabs" && menu.SequenceEqual(allTabs),
                  $"clicking Hire opens the five tabs in 0x197C40's order: {string.Join(", ", menu)}");
            // The tab predicate 5 - count > 0: fill Cleaners to five and the tab goes, empty it and it is back.
            var extra = Enumerable.Range(0, 5).Select(s => staff.Hire(StaffKind.Handyman, s)).Where(m => m != null).ToList();
            Call(viewer, "ShowLaptopLevel");
            var without = Panel<List<string>>(panel, "_menu").ToList();
            foreach (var m in extra) staff.CancelHire(m);
            Call(viewer, "ShowLaptopLevel");
            var back = Panel<List<string>>(panel, "_menu").ToList();
            Check(extra.Count == 5 && !without.Contains(Tab(StaffKind.Handyman)) && without.Count == 4 && back.SequenceEqual(allTabs),
                  $"with 5 cleaners hired the Cleaners tab is gone ({without.Count} tabs), with 0 it is back ({back.Count})");
            // Every kind draws through the registry: the four fixed models, and the entertainer in THIS
            // park's costume (0x17D7E8: variant = park index + 1).
            var costume = new Dictionary<string, string[]>
            {
                ["JUNGLE"] = new[] { "Dino", "Hunter" }, ["HALLOW"] = new[] { "Franky", "Vampire" },
                ["FANTASY"] = new[] { "Flower", "Gnome" }, ["SPACE"] = new[] { "Spaceman", "Alien" },
            }[world][park2 == "2" ? 1 : 0];
            var models = new Dictionary<StaffKind, string>
            {
                [StaffKind.Mechanic] = "FatMechanic", [StaffKind.Entertainer] = costume, [StaffKind.Handyman] = "Handyman",
                [StaffKind.Guard] = "Guard", [StaffKind.Researcher] = "Researcher",
            };
            var trial = Enum.GetValues<StaffKind>().Select(k => staff.Hire(k, 0)).ToList();
            Call(viewer, "SyncStaffActors");
            var drawnKinds = new List<string>();
            foreach (var m in trial)
            {
                var actor = ((System.Collections.IDictionary)Member("_staffActors").GetValue(viewer))[m];
                var path = (string)F(actor, "ModelPath");
                var model = (AnimatedModel)F(actor, "Drawn");
                if (model != null && model.Record is { Slot: 0 } && model.BakedParts > 0
                    && path.Equals($"/Chars/{models[m.Kind]}/{models[m.Kind]}.mps", StringComparison.OrdinalIgnoreCase))
                    drawnKinds.Add($"{m.Kind}={models[m.Kind]}({model.BakedParts})");
            }
            foreach (var m in trial) staff.CancelHire(m);
            Check(drawnKinds.Count == 5 && staff.Members.Count == 0,
                  $"each kind builds its registry model with the baked walk bound: {string.Join(" ", drawnKinds)}");
            await Frames();
            int cleaners = Array.IndexOf(StaffTables.HireTabs, StaffKind.Handyman);
            Click(panel.MenuRowScreenBox(cleaners).GetCenter());
            await Frames();
            var available = staff.Candidates.Available(StaffKind.Handyman).ToList();
            string Title() => Panel<string>(panel, "_title");
            var cells = Panel<List<(string Text, int Fraction)>>(panel, "_cells");
            var c0 = available[0];
            Check(laptopBack[^1] == ("hire", $"{(int)StaffKind.Handyman}:0") && Title() == c0.Name(text)
                  && cells.Count == 3 && cells[0].Text == (c0.PayGrade + 1).ToString()
                  && cells[1].Text == Money.Format(c0.MonthlyWage * 10) && cells[2].Fraction == c0.Motivation,
                  $"the Cleaners tab shows {c0.Name(text)}: pay grade {cells[0].Text}, wage {cells[1].Text}, motivation bar {cells[2].Fraction} (0x199008)");
            var arrows = Panel<Rect2>(panel, "_pageArrows");
            Check(arrows.Size.X > 0, $"StaffSelectArrows is drawn beside the name ({arrows.Size.X:F0}px)");
            Click(arrows.Position + new Vector2(arrows.Size.X * 0.75f, arrows.Size.Y / 2));
            await Frames();
            Check(available.Count < 2 || Title() == available[1].Name(text),
                  $"the right arrow pages to the next available candidate ({Title()})");
            Click(arrows.Position + new Vector2(arrows.Size.X * 0.25f, arrows.Size.Y / 2));
            await Frames();
            Check(Title() == c0.Name(text), "the left arrow pages back");
            int balance = sim.Finances.Balance;
            var hireBox = panel.BuildRowScreenBox;
            Check(panel.HasBuildRow && hireBox.Size.X > 0, "the Hire row sits under the candidate");
            Click(hireBox.GetCenter());
            await Frames(1);
            var held = Field<StaffMember>(viewer, "_hireHeld");
            Check(held != null && held.Held && held.Kind == StaffKind.Handyman && held.CandidateSlot == c0.Slot
                  && !c0.Available && staff.Count(StaffKind.Handyman) == 1 && !panel.Open,
                  $"clicking Hire hands {c0.Name(text)} to the hire tool: held, the laptop closed, 1 cleaner on the books");
            Check(sim.Finances.Balance == balance, $"hiring costs nothing (balance {balance} -> {sim.Finances.Balance})");
            Check(held.LogicalRequest == StaffTables.LogicalCarry, $"while held he asks for logical {held.LogicalRequest} (18, 0x1DB644)");

            // ---------------------------------------------------------------------------------
            // The carry: the cursor's point, unsnapped, and the screen->point inversion under it.
            var cam = Field<Camera3D>(viewer, "_cam");
            Set(viewer, "_freeCam", true);
            void Aim(Vector3 at, float far = 7f)
            {
                cam.GlobalPosition = at + new Vector3(far * 0.55f, far, far * 0.75f);
                cam.LookAt(at, Vector3.Up);
            }
            Vector3 World(float x, float z) => (Vector3)Call(viewer, "GuestWorld", new Vector3(x, 0, z),
                                                           new ParkCell(Mathf.FloorToInt(x), Mathf.FloorToInt(z)));
            var dropCell = corridor.First(c => c.Y == entry.ZEnd + 3);
            Check(grid.Open(new ParkCell(dropCell.X, dropCell.Y)), $"the drop cell {dropCell} is on the corridor");
            float px = dropCell.X + 0.3f, pz = dropCell.Y + 0.7f;
            Aim(World(px, pz));
            await Frames(1);
            {
                var w = World(px, pz); w.Y = park.BaseY;
                var screen = cam.UnprojectPosition(w);
                var args3 = new object[] { screen, null };
                bool hit = (bool)typeof(Viewer).GetMethod("PointAtScreen", Hidden).Invoke(viewer, args3);
                var got = (Point)args3[1];
                Check(hit && Math.Abs(got.X - px * 256) <= 2 && Math.Abs(got.Z - pz * 256) <= 2,
                      $"the cursor's point is the floor hit in 1/256 cell: ({px * 256:F0},{pz * 256:F0}) -> ({got.X},{got.Z})");
            }
            var carryPoint = new Point((short)(px * 256), (short)(pz * 256));
            Set(viewer, "_cursorPointOverride", (Point?)carryPoint);
            Call(viewer, "UpdateHireCarry");
            Check(held.Position == carryPoint && held.Held, $"the carry puts him at the cursor, NOT snapped ({held.Position.X},{held.Position.Z}) and still held");
            for (int i = 0; i < 40; i++) { Call(viewer, "TickPark"); Call(viewer, "UpdateHireCarry"); }
            Call(viewer, "PlaceStaff", 1f);
            var actors = (System.Collections.IDictionary)Member("_staffActors").GetValue(viewer);
            object Actor(StaffMember m) => actors[m] ?? throw new InvalidOperationException($"no actor for {m}");
            var heldActor = Actor(held);
            var heldNode = (Node3D)F(heldActor, "Node");
            var heldDrawn = (AnimatedModel)F(heldActor, "Drawn");
            Check(heldNode.Visible && heldNode.Position.DistanceTo(World(carryPoint.X / 256f, carryPoint.Z / 256f)) < 1e-3f,
                  $"he is drawn while held, at the cursor ({heldNode.Position})");
            Check(held.State == 0 && held.Position == carryPoint, "held, he is not updated: state 0 and where the carry left him");
            Check(heldDrawn.Record is { Slot: 3 },
                  $"held, logical 18 plays main s3 (drawing section {heldDrawn.Record?.Slot})");
            // The props: each record's own +0x18 list says which to hide (s0/s2/s3/s5 the placard, s7 the broom).
            bool MeshShown(AnimatedModel m, string mesh) => m.Surfaces().Where(x => x.Mesh.Equals(mesh, StringComparison.OrdinalIgnoreCase)).All(x => x.Node.Visible)
                                                           && m.Surfaces().Any(x => x.Mesh.Equals(mesh, StringComparison.OrdinalIgnoreCase));
            Check(!MeshShown(heldDrawn, "plackard") && MeshShown(heldDrawn, "broom"),
                  "carried (s3) he holds his broom and not the strike placard (s3's own list names node 2)");
            // Shots look back at the subject from the park side, so the gate is behind it, not in front.
            var intoPark = (World(entry.XCol + 0.5f, entry.ZEnd + 9.5f) - World(entry.XCol + 0.5f, entry.ZEnd + 0.5f)).Normalized();
            var across = intoPark.Cross(Vector3.Up).Normalized();
            async Task Shot(string name, Vector3 at, float far = 1.8f)
            {
                if (shots == null) return;
                foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = false;
                foreach (var ui in viewer.GetChildren().OfType<Control>()) ui.Visible = false;
                cam.GlobalPosition = at + intoPark * far * 0.8f + across * far * 0.35f + Vector3.Up * far * 0.8f;
                cam.LookAt(at + Vector3.Up * 0.25f, Vector3.Up);
                await Frames(3);
                Call(viewer, "SaveShot", System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}{park2}_{name}.png"));
            }
            await Shot("carry", heldNode.GlobalPosition);

            // ---------------------------------------------------------------------------------
            // The drop: refused on the toilet, accepted on the path; the press is the real one.
            var sounds = Field<RideSounds>(viewer, "_sounds");
            int cuesBefore = sounds?.Census.Count ?? 0;
            var loo = toilet.Origin;
            Set(viewer, "_cursorPointOverride", (Point?)new Point((short)(loo.X * 256 + 0x80), (short)(loo.Z * 256 + 0x80)));
            Call(viewer, "PressHireTool");
            Check(!staff.CanDrop(loo) && held.Held && Field<StaffMember>(viewer, "_hireHeld") == held,
                  $"a press over the toilet at {loo} is refused (0x1E65B8) and he stays held");
            Set(viewer, "_cursorPointOverride", (Point?)carryPoint);
            Call(viewer, "PressHireTool");
            Set(viewer, "_cursorPointOverride", null);
            Check(!held.Held && held.State == 0 && held.LogicalRequest == StaffTables.LogicalWalk
                  && held.Position == carryPoint && Field<StaffMember>(viewer, "_hireHeld") == null,
                  $"a press on the path puts him down where the cursor was: unheld, state 0, logical 13 ({held})");
            var cues = sounds?.Census.Skip(cuesBefore).ToList() ?? new List<string>();
            Check(cues.Any(c => c.Contains("evt 175")) && cues.Any(c => c.Contains("evt 303")),
                  $"the refusal cues 0xAF and the drop 0x12F: {string.Join(" | ", cues.Select(c => c[(c.IndexOf("evt") >= 0 ? c.IndexOf("evt") : 0)..]))}");

            // ---------------------------------------------------------------------------------
            // The walk: section 0 drawn as itself, where the sim has him, facing where he goes.
            var h = held;
            var node = (Node3D)F(Actor(h), "Node");
            var drawn = (AnimatedModel)F(Actor(h), "Drawn");
            int moving = 0, baked = 0, faced = 0, wrongFaced = 0, lagged = 0, maxBaked = 0, alongX = 0, alongZ = 0; float worst = 0;
            var walkShotAt = -1;
            for (int t = 0; t < 3000 && (moving < 60 || alongX == 0 || alongZ == 0); t++)
            {
                var was = h.Position;
                Call(viewer, "TickPark");
                Call(viewer, "PlaceStaff", 1f);
                var at = World(h.Position.X / 256f, h.Position.Z / 256f);
                worst = Math.Max(worst, node.Position.DistanceTo(at));
                int dx = h.Position.X - was.X, dz = h.Position.Z - was.Z;
                if (dx == 0 && dz == 0) continue;
                moving++;
                if (drawn.Record is { Slot: 0 } && drawn.BakedParts > 0) { baked++; maxBaked = Math.Max(maxBaked, drawn.BakedParts); }
                // The walk step's facing rule (0x191E98): an x move decides it, else the z move.
                var step = dx != 0 ? new Vector3(Math.Sign(dx), 0, 0) : new Vector3(0, 0, Math.Sign(dz));
                if (dx != 0) alongX++; else alongZ++;
                float yaw = (float)F(Actor(h), "Yaw");
                var heading = (Vector3)typeof(Viewer).GetMethod("StaffHeading", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { yaw });
                if (heading.Dot(step) > 0.99f) faced++;
                // CONTROL: the same test with the pi dropped, yaw = -facing, must disagree on every step.
                var wrong = (Vector3)typeof(Viewer).GetMethod("StaffHeading", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { -h.FacingRadians });
                if (wrong.Dot(step) <= 0.99f) wrongFaced++;
                // CONTROL: the previous tick's position is a different place while he moves.
                if (node.Position.DistanceTo(World(was.X / 256f, was.Z / 256f)) > 1e-3f) lagged++;
                if (moving == 30 && walkShotAt < 0) { walkShotAt = t; await Shot("walking", node.GlobalPosition); }
            }
            Check(moving >= 60, $"dropped on the path he walks: {moving} moving ticks (state 0x{h.State:x2}, {h.WaypointsRetired} waypoints)");
            Check(baked == moving && maxBaked > 0,
                  $"every moving tick draws section 0, the baked walk ({baked} of {moving}, {maxBaked} baked parts; no s1 stand-in)");
            Check(worst < 1e-3f && lagged == moving,
                  $"the drawn position follows Position (worst {worst:E1}); control: the previous tick's is off on {lagged} of {moving}");
            Check(faced == moving && alongX > 0 && alongZ > 0 && wrongFaced == moving,
                  $"yaw pi - facing points him along every step ({faced} of {moving}: {alongX} across x, {alongZ} along z); "
                  + $"control: yaw = -facing is wrong on {wrongFaced}");

            // ---------------------------------------------------------------------------------
            // Litter and vomit on the ground, then swept.
            var litter = staff.Litter;
            // On the bar, away from the gate: two cells apart, both near him.
            var near = bar.Where(c => grid.Open(new ParkCell(c.X, c.Y)) && (c.X != h.Cell.X || c.Y != h.Cell.Z))
                          .OrderBy(c => Math.Abs(c.X - h.Cell.X) + Math.Abs(c.Y - h.Cell.Z)).Take(4).ToList();
            Check(near.Count == 4, $"four bar cells to drop on ({string.Join(" ", near)})");
            var plain = litter.Drop(new Point((short)(near[1].X * 256 + 0x80), (short)(near[1].Y * 256 + 0x80)), vomit: false);
            var sick = litter.Drop(new Point((short)(near[3].X * 256 + 0x80), (short)(near[3].Y * 256 + 0x80)), vomit: true);
            var litterActors = (System.Collections.IDictionary)Member("_litterActors").GetValue(viewer);
            Check(plain != null && sick != null && litterActors.Count == 2, $"two items dropped, two drawn ({litterActors.Count})");
            string PathOf(LitterItem item) => (string)((System.Runtime.CompilerServices.ITuple)litterActors[item])[3];
            Node3D NodeOf(LitterItem item) => (Node3D)((System.Runtime.CompilerServices.ITuple)litterActors[item])[0];
            Check(PathOf(plain)?.StartsWith("/Generic/MiscMesh/litter", StringComparison.OrdinalIgnoreCase) == true
                  && PathOf(sick)?.Equals("/Generic/MiscMesh/puke.mps", StringComparison.OrdinalIgnoreCase) == true,
                  $"registry models: litter {plain.ModelId} -> {PathOf(plain)}, vomit {sick.ModelId} -> {PathOf(sick)}");
            Check(NodeOf(plain).Visible && NodeOf(sick).Visible
                  && NodeOf(plain).Position.DistanceTo(World(plain.Position.X / 256f, plain.Position.Z / 256f)) < 1e-3f,
                  "both shown, at their own 1/256 positions");
            await Shot("litter", (NodeOf(plain).GlobalPosition + NodeOf(sick).GlobalPosition) / 2, 2.2f);
            bool claimed = false, swept = false, sweepPose = false; int sweepTicks = 0;
            int sweptBefore = litter.Swept;
            for (int t = 0; t < 4000 && litter.Count > 0; t++)
            {
                Call(viewer, "TickPark");
                Call(viewer, "PlaceStaff", 1f);
                claimed |= plain.Claimant == h || sick.Claimant == h;
                if (h.State == Handyman.StateSweeping)
                {
                    sweepTicks++;
                    swept |= h.LogicalRequest == StaffTables.LogicalWork;
                    if (!sweepPose && drawn.Record is { Slot: 5 })
                    {
                        sweepPose = true;
                        Check(!MeshShown(drawn, "plackard") && MeshShown(drawn, "broom"),
                              "sweeping (s5) he holds his broom and not the strike placard");
                        // CONTROL: the same records through the port's default visibility (ordinary lists
                        // only) leave the placard on after the walk -- what this view used to draw.
                        var ctrlModel = ((System.Collections.Generic.Dictionary<string, Model>)Member("_charModels").GetValue(viewer))[(string)F(Actor(h), "ModelPath")];
                        var ctrlAps = (TPW.PS2.Data.Animation)F(Actor(h), "Anim");
                        var ctrl = new AnimatedModel(ctrlModel, ctrlAps, ctrlAps.Records().First(r => r.Slot == 0), _ => (null, false));
                        ctrl.UseRecord(ctrlAps.Records().First(r => r.Slot == 5)); ctrl.SetFrame(0);
                        bool ctrlPlacard = MeshShown(ctrl, "plackard");
                        ctrl.Root.Free();
                        Check(ctrlPlacard, "control: without the skeletal lists the placard shows on s5 after s0");
                        await Shot("sweeping", node.GlobalPosition);
                    }
                }
            }
            Check(claimed, "he claims an item (litter +0x2C = him)");
            Check(swept && sweepTicks > 0, $"sweeping is state 0x1B asking for logical 16 ({sweepTicks} ticks)");
            Check(sweepPose, "the sweep is drawn: logical 16's main s5 on the handyman (no s4: one held update first)");
            Check(litter.Count == 0 && litter.Swept - sweptBefore == 2 && !plain.Active && !sick.Active,
                  $"both are swept away ({litter.Swept - sweptBefore}) -- the only remover of litter");
            await Frames(1);
            Check(litterActors.Count == 0, "and their models are gone");

            // ---------------------------------------------------------------------------------
            // The toilet: dirtied below 60, he hides inside, it comes back 100 and stamped today.
            var calendar = Field<ParkClock>(viewer, "_calendar");
            for (int i = 0; i < 64 && calendar.TotalDays < 2; i++) calendar.Advance(ParkClock.UnitsPerDay / 8, out _, out _, out _);
            Check(calendar.TotalDays >= 2 && toilet.LastCleanedDay != calendar.TotalDays,
                  $"the calendar is on day {calendar.TotalDays}, the toilet last cleaned on day {toilet.LastCleanedDay}");
            toilet.Wear(toilet.Condition - 55);
            Check(toilet.Condition == 55, "the toilet is dirtied to 55 (< 60, a handyman's candidate)");
            bool hid = false, hiddenDrawn = true;
            int serviced = visitors.Serviced;
            for (int t = 0; t < 6000 && toilet.Condition < 100; t++)
            {
                Call(viewer, "TickPark");
                Call(viewer, "PlaceStaff", 1f);
                if (h.State == Handyman.StateCleaningToilet)
                {
                    hid |= !h.Shown;
                    hiddenDrawn &= !node.Visible;
                }
            }
            Check(hid && hiddenDrawn, "he goes inside to clean: state 0x33, hidden, and not drawn");
            Check(toilet.Condition == 100 && toilet.LastCleanedDay == calendar.TotalDays,
                  $"he comes out with it at 100 and Last Cleaned = today (day {toilet.LastCleanedDay})");
            Call(viewer, "TickPark"); Call(viewer, "PlaceStaff", 1f);
            Check(h.Shown && node.Visible, "shown and drawn again");
            Check(visitors.Serviced == serviced, "no stand-in cleaned it: ParkVisitors.Serviced did not move");

            Field<RideSounds>(viewer, "_sounds")?.Clear();
            viewer.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            GD.Print($"STAFF SMOKE PASS checks={_checks}; world={world}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"STAFF SMOKE FAILED world={world} checks={_checks}: {ex}");
            GetTree().Quit(2);
        }
    }
}
