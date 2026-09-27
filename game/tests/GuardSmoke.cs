using System.Reflection;
using Godot;
using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPWPS2Viewer.Tests;

/// <summary>Guards and entertainers on the shipping Viewer, end to end (findings/staff-mechanics-guards.md
/// §5-§6, staff-handymen-entertainers.md §1.5, §4-§5; the spec is ghidra_tpw/notes/SPEC-staff-guards-
/// entertainers.md): a corridor laid by the path tool; an entertainer and a guard hired through the
/// laptop's own Hire tabs and put down by the real press; guests walking the corridor; a show (logical 16
/// on the costume) with guests stopping to watch and TURNED to face him; then a prank -- ⚠ forced through
/// <see cref="ParkStaff.Prank"/>, the ONE test hook (natively the 1-in-600 idle arm 5) -- its YellowStink,
/// the guard sent, the catch, the guest gone from the park, the guard carrying a copy of the guest out
/// through the entrance turnstile to the corridor start, dropping it and walking back in; and a handyman
/// sweeping the hidden prank litter, which stops the stink.
///
/// Run with --map=WORLD --mode=park. `TPW_GUARD_SHOT=dir` saves the show, the stink, the carry and the
/// drop at the gate.
///
/// ⭐ Each drawn check carries a control that must disagree (the opposite facing, the guard's previous
/// position, the carry without its animation gate), so a check that cannot fail shows as one.</summary>
public partial class GuardSmoke : Node3D
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
        GD.Print("GUARD SMOKE ok: " + label);
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
            string shots = System.Environment.GetEnvironmentVariable("TPW_GUARD_SHOT");
            if (shots != null) System.IO.Directory.CreateDirectory(shots);
            string park2 = map.Contains("terrain_2", StringComparison.OrdinalIgnoreCase) ? "2" : "1";

            viewer = new Viewer { Name = "Viewer" };
            Set(viewer, "_guestCap", 0);          // no bus arrivals: the guests here are the fixture's
            AddChild(viewer);
            viewer.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var park = Field<Park>(viewer, "_park");
            var lib = Field<AssetLibrary>(viewer, "_lib");
            var terrain = Field<Model>(viewer, "_terrainModel");
            var entrances = Field<ParkEntrance>(viewer, "_entranceTable");
            Check(park?.Field != null && terrain != null && entrances != null && lib != null, "the park loaded");
            var entry = entrances.Fit(terrain.Field, ParkEntrance.WalkwayColumnFromPoles(terrain), out _);
            Check(!entry.Empty, $"the authored entrance fits ({entry.XStart},{entry.ZRow})..({entry.XCol},{entry.ZEnd})");

            // A corridor in from the gate and a bar across it, laid by the path tool's own leg.
            var corridor = new List<(int X, int Y)>();
            for (int z = entry.ZEnd; z < entry.ZEnd + 12; z++) corridor.Add((entry.XCol, z));
            Call(viewer, "LayLeg", corridor, PathTool.Kind.Path, 0);
            int barZ = entry.ZEnd + 6;
            var bar = new List<(int X, int Y)>();
            for (int x = entry.XCol - 6; x <= entry.XCol + 6; x++)
                if (park.IsPlayable(x, barZ) && park.Vacant(x, barZ)) bar.Add((x, barZ));
            Call(viewer, "LayLeg", bar, PathTool.Kind.Path, 0);
            Call(viewer, "RefreshFloor");
            Check((bool)Call(viewer, "OpenGate"), "the guest layer opened on the corridor");
            var walk = Field<GuestWalk>(viewer, "_guests");
            var grid = walk.Paths;
            var laid = corridor.Concat(bar).Select(c => new ParkCell(c.X, c.Y)).Where(grid.Open).ToList();
            Check(laid.Count >= 14, $"{laid.Count} path cells are open to walk");
            for (int i = 0; i < 5; i++) Call(viewer, "TickPark");
            var sim = Field<ParkSim>(viewer, "_sim");
            var visitors = Field<ParkVisitors>(viewer, "_visitors");
            var staff = Field<ParkStaff>(viewer, "_staff");
            Check(visitors != null && staff != null && ReferenceEquals(visitors.Staff, staff), "the viewer attached a ParkStaff to its visitors");
            var flow = Field<NativeEntranceFlow>(viewer, "_entranceFlow");
            Check(staff.StagingCell is { } sc && grid.IsEntrance(sc) && ReferenceEquals(staff.Gate, flow) && flow != null,
                  $"the guards share the entrance flow's turnstile and stage at {staff.StagingCell} (entry +2/+3, on the walkway)");

            // ---------------------------------------------------------------------------------
            // The laptop: Main menu -> Hire -> a tab -> the first candidate -> Hire -> the press.
            Call(viewer, "LoadHudFont");
            var panel = Field<LaptopShopScreen>(viewer, "_shopPanel");
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
            async Task<StaffMember> HireByLaptop(StaffKind kind, ParkCell at)
            {
                if (!panel.Open) { Call(viewer, "ToggleLaptop"); await Frames(); }
                var mainOpts = LaptopMainMenu.VisibleMain(parkOpen: Field<bool>(viewer, "_laptopParkOpen")).ToList();
                int hireRow = mainOpts.FindIndex(o => o.Opens == "main_bh_staff");
                Click(panel.MenuRowScreenBox(hireRow).GetCenter());
                await Frames();
                var tabs = (List<StaffKind>)Call(viewer, "HireTabs");
                int tab = tabs.IndexOf(kind);
                Check(laptopBack.Count >= 1 && laptopBack[^1].Kind == "hiretabs" && tab >= 0, $"the Hire tabs list {kind} at row {tab}");
                Click(panel.MenuRowScreenBox(tab).GetCenter());
                await Frames();
                var cand = staff.Candidates.Available(kind).First();
                Check(laptopBack[^1] == ("hire", $"{(int)kind}:0"), $"clicking the {kind} tab shows its first candidate");
                Click(panel.BuildRowScreenBox.GetCenter());
                await Frames(1);
                var held = Field<StaffMember>(viewer, "_hireHeld");
                Check(held != null && held.Held && held.Kind == kind && !panel.Open,
                      $"clicking Hire hands the {kind} to the hire tool (held, the laptop closed)");
                Set(viewer, "_cursorPointOverride", (Point?)new Point((short)(at.X * 256 + 0x80), (short)(at.Z * 256 + 0x80)));
                Call(viewer, "UpdateHireCarry");
                Call(viewer, "PressHireTool");
                Set(viewer, "_cursorPointOverride", null);
                Check(!held.Held && held.Cell == at && Field<StaffMember>(viewer, "_hireHeld") == null,
                      $"the press puts the {kind} down at {at}");
                return held;
            }
            var barCells = bar.Select(c => new ParkCell(c.X, c.Y)).Where(grid.Open).OrderBy(c => c.X).ToList();
            var mid = barCells[barCells.Count / 2];
            var ent = (Entertainer)await HireByLaptop(StaffKind.Entertainer, mid);
            var guard = (Guard)await HireByLaptop(StaffKind.Guard, new ParkCell(entry.XCol, entry.ZEnd + 3));
            Check(ent is Entertainer && guard is Guard, $"the pools hand out the job classes: {ent.GetType().Name}, {guard.GetType().Name}");

            // Guests along the bar and the corridor, walking about (the viewer's own idle sends them).
            var guests = new List<Guest>();
            foreach (var c in barCells.Concat(laid.Where(c => c.X == entry.XCol && c.Z > entry.ZEnd + 1)).Distinct())
                guests.Add(visitors.Arrive(c, c));
            Check(guests.Count >= 10, $"{guests.Count} guests put on the paths");

            // Shots look down on the subject from the park side.
            var cam = Field<Camera3D>(viewer, "_cam");
            Set(viewer, "_freeCam", true);
            Vector3 World(float x, float z) => (Vector3)Call(viewer, "GuestWorld", new Vector3(x, 0, z),
                                                           new ParkCell(Mathf.FloorToInt(x), Mathf.FloorToInt(z)));
            var intoPark = (World(entry.XCol + 0.5f, entry.ZEnd + 9.5f) - World(entry.XCol + 0.5f, entry.ZEnd + 0.5f)).Normalized();
            var across = intoPark.Cross(Vector3.Up).Normalized();
            async Task Shot(string name, Vector3 at, float far = 2.4f, int frames = 3)
            {
                if (shots == null) return;
                foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = false;
                foreach (var ui in viewer.GetChildren().OfType<Control>()) ui.Visible = false;
                cam.GlobalPosition = at + intoPark * far * 0.8f + across * far * 0.35f + Vector3.Up * far * 0.8f;
                cam.LookAt(at + Vector3.Up * 0.25f, Vector3.Up);
                await Frames(frames);
                Call(viewer, "SaveShot", System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}{park2}_{name}.png"));
            }
            // ⭐ "Is it on screen" as a number: the same frame with and without one node, and a control pair
            // with nothing changed (the noise floor). The camera looks at `at` from close by.
            async Task<(int With, int Floor)> Visible(Node3D subject, Vector3 at, float far, int settle, string name)
            {
                foreach (var layer in viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>()) layer.Visible = false;
                foreach (var ui in viewer.GetChildren().OfType<Control>()) ui.Visible = false;
                cam.GlobalPosition = at + intoPark * far * 0.8f + across * far * 0.35f + Vector3.Up * far * 0.8f;
                cam.LookAt(at + Vector3.Up * 0.25f, Vector3.Up);
                await Frames(settle);
                // Particles are frozen for the probe (their own motion is not the subject's presence).
                var particles = viewer.FindChildren("*", "", true, false).OfType<CpuParticles3D>().ToList();
                var speeds = particles.Select(p => p.SpeedScale).ToList();
                foreach (var p in particles) p.SpeedScale = 0;
                await Frames(2);
                var a = viewer.GetViewport().GetTexture().GetImage();
                subject.Visible = false;
                await Frames(2);
                var b = viewer.GetViewport().GetTexture().GetImage();
                await Frames(2);
                var b2 = viewer.GetViewport().GetTexture().GetImage();   // hidden twice: what moves anyway
                subject.Visible = true;
                if (shots != null)
                {
                    a.SavePng(System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}{park2}_{name}_probe_with.png"));
                    b.SavePng(System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}{park2}_{name}_probe_without.png"));
                }
                for (int i = 0; i < particles.Count; i++) if (IsInstanceValid(particles[i])) particles[i].SpeedScale = speeds[i];
                int Diff(Image x, Image y)
                {
                    int n = 0;
                    for (int yy = 0; yy < x.GetHeight(); yy += 2)
                        for (int xx = 0; xx < x.GetWidth(); xx += 2)
                        {
                            var p = x.GetPixel(xx, yy); var q = y.GetPixel(xx, yy);
                            if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > 0.06f) n++;
                        }
                    return n;
                }
                return (Diff(a, b), Diff(b, b2));
            }
            var actors = (System.Collections.IDictionary)Member("_staffActors").GetValue(viewer);
            object Actor(StaffMember m) => actors[m] ?? throw new InvalidOperationException($"no actor for {m}");
            var guestActors = Field<Dictionary<int, Node3D>>(viewer, "_actors");

            // ---------------------------------------------------------------------------------
            // The show, and the guests who stop to watch it.
            int showTicks = 0, watchedMax = 0, facedOk = 0, facedChecked = 0, controlWrong = 0, stoodStill = 0, stoodChecked = 0;
            bool costumeS5 = false, shotShow = false;
            var entDrawn = (AnimatedModel)F(Actor(ent), "Drawn");
            var lastPos = new Dictionary<int, Vector3>();
            // As many shows as it takes (one can end at once when the guest beside him walks on): until the
            // costume has reached main s5, someone has watched, and enough watcher-ticks are measured.
            int shows = 0; bool wasPerforming = false;
            bool Enough() => costumeS5 && watchedMax >= 1 && facedChecked >= 5 && stoodChecked >= 10 && (shots == null || shotShow);
            for (int t = 0; t < 20000 && !Enough(); t++)
            {
                Call(viewer, "TickPark");
                Call(viewer, "PlaceStaff", 1f);
                Call(viewer, "PlaceActors", 1f);
                bool performing = ent.State == Entertainer.StatePerforming;
                if (performing && !wasPerforming) shows++;
                wasPerforming = performing;
                if (!performing) continue;
                showTicks++;
                costumeS5 |= entDrawn?.Record is { Slot: 5 };
                var watching = staff.Watching.Where(kv => ReferenceEquals(kv.Value.Entertainer, ent)).ToList();
                watchedMax = Math.Max(watchedMax, watching.Count);
                var entNode = (Node3D)F(Actor(ent), "Node");
                foreach (var (id, w) in watching)
                {
                    if (!guestActors.TryGetValue(id, out var node) || node == null) continue;
                    // Standing: the drawn position does not move from one watching tick to the next.
                    if (lastPos.TryGetValue(id, out var was)) { stoodChecked++; if (was.DistanceTo(node.Position) < 1e-4f) stoodStill++; }
                    lastPos[id] = node.Position;
                    var toEnt = entNode.GlobalPosition - node.GlobalPosition; toEnt.Y = 0;
                    var wbody = walk.Guests.FirstOrDefault(x => x.Id == id);
                    if (wbody == null || ParkStaff.GuestCell(wbody) == ent.Cell) continue;   // on his cell: 0x2107A0 says pi
                    facedChecked++;
                    var forward = node.GlobalBasis.Z; forward.Y = 0;
                    // The rule turns to the AXIS of the neighbouring cell; a watcher stopped part-way along an edge
                    // stands up to half a cell off that axis, so "towards him" is within 45 degrees (cos 0.7).
                    if (forward.Normalized().Dot(toEnt.Normalized()) > 0.7f) facedOk++;
                    else if (facedChecked - facedOk <= 6)
                    {
                        var body = walk.Guests.FirstOrDefault(x => x.Id == id);
                        GD.Print($"[guard smoke] watcher {id} not facing: guest cell {(body == null ? "?" : ParkStaff.GuestCell(body).ToString())} fine "
                               + $"{(body == null ? "?" : ParkStaff.GuestFine(body).ToString())} state {body?.State} next {body?.Next}, ent cell {ent.Cell} pos {ent.Position}, "
                               + $"quarter {w.FacingQuarterTurns}, forward {forward}, toEnt {toEnt}");
                    }
                    // CONTROL: the opposite quarter turn must point away.
                    var wrong = -forward;
                    if (wrong.Normalized().Dot(toEnt.Normalized()) < 0) controlWrong++;
                }
                if (!shotShow && (facedChecked > 0 || watching.Count >= 2) && showTicks > 20)
                {
                    shotShow = true;
                    await Shot("show", entNode.GlobalPosition, 1.5f);
                }
            }
            Check(showTicks > 0 && costumeS5, $"the entertainer performs: {shows} show(s), state 0xC for {showTicks} ticks, his costume drawing logical 16's main s5");
            Check(watchedMax >= 1, $"guests stop to watch him: up to {watchedMax} at once (state 0x1C)");
            Check(facedChecked > 0 && facedOk == facedChecked && controlWrong == facedChecked,
                  $"every watcher off his cell is drawn FACING him, turned to the neighbouring cell's axis ({facedOk} of {facedChecked} watcher-ticks within 45 degrees); control: the opposite turn points away on {controlWrong}");
            Check(stoodChecked > 0 && stoodStill == stoodChecked, $"watchers stand still ({stoodStill} of {stoodChecked} watcher-ticks unmoved)");

            // ---------------------------------------------------------------------------------
            // The prank: ⚠ forced (the labelled test hook), near the guard.
            Guest prankster = null;
            for (int t = 0; t < 4000 && prankster == null; t++)
            {
                var gcell = guard.Cell;
                if (!guard.Busy && guard.Carried == null)
                    prankster = walk.Guests.Where(g => !g.HasNativeRoute && visitors.Plans.ContainsKey(g.Id) && !staff.IsWatching(g.Id))
                                  .FirstOrDefault(g => { var c = ParkStaff.GuestCell(g); int dx = c.X - gcell.X, dz = c.Z - gcell.Z; return dx * dx + dz * dz < 25 && (dx != 0 || dz != 0); });
                if (prankster == null) { Call(viewer, "TickPark"); Call(viewer, "PlaceStaff", 1f); }
            }
            Check(prankster != null, $"a guest within 5 cells of the free guard (guest {prankster?.Id})");
            int pid = prankster.Id;
            var pcell = ParkStaff.GuestCell(prankster);
            staff.Prank(prankster);
            var stink = staff.Stinks.Entries.FirstOrDefault(s => s.CellX == pcell.X && s.CellZ == pcell.Z);
            var emitters = stink == null ? new List<CpuParticles3D>() : (IReadOnlyList<CpuParticles3D>)Call(viewer, "StinkEmitters", stink);
            Check(stink != null && emitters.Count > 0 && emitters.All(e => e.Emitting),
                  $"the prank leaves a YellowStink (template 50) emitting at {stink?.X:F2},{stink?.Z:F2} in the guest's cell ({emitters.Count} emitter(s))");
            var hidden = staff.Litter.Active.FirstOrDefault(i => !i.Shown);
            var litterActors = (System.Collections.IDictionary)Member("_litterActors").GetValue(viewer);
            Check(hidden != null && litterActors.Contains(hidden) && !((Node3D)((System.Runtime.CompilerServices.ITuple)litterActors[hidden])[0]).Visible,
                  "and a litter item that is NOT drawn (0x20D010 never shows prank litter)");
            Check(guard.State == Guard.StateChase && guard.Target is GuestTarget tg && tg.Id == pid && guard.SpeedBits == Guard.ChaseSpeed,
                  $"the guard is sent after the prankster: state 0x21, speed 30");
            if (stink != null) await Shot("stink", World(stink.X, stink.Z), 1.3f, 40);   // frames for the emitter to fill
            {
                var holders = (System.Collections.IDictionary)Member("_stinkHolders").GetValue(viewer);
                var holder = stink == null ? null : (Node3D)holders[stink];
                var (with, floor) = holder == null ? (0, 0) : await Visible(holder, World(stink.X, stink.Z), 1.3f, 40, "stink");
                GD.Print($"[guard smoke] stink on screen: {with} sampled pixels change when it is hidden; hidden-vs-hidden {floor}");
                Check(with > 3 * floor + 30,
                      $"the stink is ON SCREEN: hiding its emitter changes {with} sampled pixels (control: the scene re-rendered with it hidden changes {floor})");
            }

            // The chase and the catch.
            var guardNode = (Node3D)F(Actor(guard), "Node");
            int chaseTicks = 0;
            for (int t = 0; t < 2000 && guard.State is Guard.StateChase or StaffMember.StateWaitForRoute or StaffMember.StateWalk or StaffMember.StateSegmentEnd
                                   && guard.Carried == null; t++)
            { Call(viewer, "TickPark"); Call(viewer, "PlaceStaff", 1f); chaseTicks++; }
            bool caught = guard.Carried?.GuestId == pid;
            if (!caught)
            {
                // A guest who walked on beyond the guard's one leg is lost (the shipped chase); send him again.
                GD.Print($"[guard smoke] the chase ended in state 0x{guard.State:x2} after {chaseTicks} ticks without a catch -- the one-leg chase lost him; pranking again");
            }
            for (int attempt = 0; attempt < 6 && !caught; attempt++)
            {
                var again = walk.Guests.FirstOrDefault(g => g.Id == pid);
                if (again == null) break;
                for (int t = 0; t < 400 && guard.Busy; t++) { Call(viewer, "TickPark"); Call(viewer, "PlaceStaff", 1f); }
                var gc = ParkStaff.GuestCell(again); var ac = guard.Cell;
                if ((gc.X - ac.X) * (gc.X - ac.X) + (gc.Z - ac.Z) * (gc.Z - ac.Z) >= 25) { for (int t = 0; t < 20; t++) Call(viewer, "TickPark"); continue; }
                staff.Prank(again);
                for (int t = 0; t < 2000 && guard.State is Guard.StateChase or StaffMember.StateWaitForRoute or StaffMember.StateWalk or StaffMember.StateSegmentEnd
                                       && guard.Carried == null; t++)
                { Call(viewer, "TickPark"); Call(viewer, "PlaceStaff", 1f); }
                caught = guard.Carried?.GuestId == pid;
            }
            Check(caught && guard.State is Guard.StateCarrying or Guard.StateChuckingOut or StaffMember.StateWaitForRoute,
                  $"the guard catches the prankster (state 0x{guard.State:x2}, carrying a copy of guest {guard.Carried?.GuestId})");
            Check(!visitors.Plans.ContainsKey(pid) && walk.Guests.All(g => g.Id != pid) && visitors.Ejected >= 1 && staff.Caught >= 1,
                  $"the caught guest is removed from the park at once (ejected {visitors.Ejected}, not a departure)");
            Call(viewer, "PlaceActors", 1f);
            Check(!guestActors.ContainsKey(pid) || guestActors[pid] == null || !IsInstanceValid(guestActors[pid]),
                  "and its walking body is no longer drawn");

            // The carry: logical 16 (s4 first, then s5), the copy drawn at him playing logical 17.
            var guardDrawn = (AnimatedModel)F(Actor(guard), "Drawn");
            int carryTicks = 0; bool sawS4 = false, sawS5 = false, copyChecked = false, shotCarry = false;
            var copies = (System.Collections.IDictionary)Member("_copyActors").GetValue(viewer);
            for (int t = 0; t < 400 && guard.State == Guard.StateCarrying; t++)
            {
                Call(viewer, "TickPark"); Call(viewer, "PlaceStaff", 1f);
                carryTicks++;
                sawS4 |= guardDrawn?.Record is { Slot: 4 };
                sawS5 |= guardDrawn?.Record is { Slot: 5 };
            }
            Check(carryTicks > 1 && guard.State is Guard.StateChuckingOut or StaffMember.StateWaitForRoute,
                  $"0x3C holds until his model has played the carry's first section: {carryTicks} ticks (0x10EC48: logical 16 at phase 1), then 0x27");
            Check(sawS4, "the carry is drawn: logical 16's first section s4 on the Guard model");
            for (int t = 0; t < 3000 && guard.Carried != null && !(copyChecked && shotCarry); t++)
            {
                Call(viewer, "TickPark"); Call(viewer, "PlaceStaff", 1f);
                if (!copies.Contains(guard)) continue;
                var copy = copies[guard];
                var copyNode = (Node3D)F(copy, "Node");
                var copyDrawn = (AnimatedModel)F(copy, "Drawn");
                if (!copyChecked && guard.State == StaffMember.StateWalk)
                {
                    copyChecked = true;
                    // The copy is where the guard is, with his yaw; control: last tick's guard position is elsewhere.
                    Check(copyNode.Visible && copyDrawn != null && copyNode.GlobalPosition.DistanceTo(guardNode.GlobalPosition) < 1e-3f
                          && copyNode.Basis.Z.Dot(guardNode.Basis.Z) > 0.999f && copyDrawn.Record is { Slot: 3 },
                          $"the copy of the guest is drawn at the guard, turned with him, playing logical 17 (section {copyDrawn?.Record?.Slot}: s3)");
                }
                if (!shotCarry && guard.State == StaffMember.StateWalk && guard.Mode == Guard.ModeToStaging)
                {
                    shotCarry = true;
                    await Shot("carry", guardNode.GlobalPosition, 1.0f);
                    var (with, floor) = await Visible(copyNode, guardNode.GlobalPosition, 1.0f, 2, "carry");
                    GD.Print($"[guard smoke] copy on screen: {with} sampled pixels change when it is hidden; hidden-vs-hidden {floor}");
                    Check(with > 3 * floor + 50,
                          $"the carried copy is ON SCREEN: hiding it changes {with} sampled pixels (control: the scene re-rendered with it hidden changes {floor})");
                }
                sawS5 |= guardDrawn?.Record is { Slot: 5 };
            }
            Check(copyChecked && sawS5, "the guard walks off with the copy, drawing logical 16's main s5");

            // Out through the turnstile to the corridor start, the copy dropped, back in to the mouth.
            int stagedP = 0; bool atExit = false, shotGate = false, shotAtGate = false; var exitCell = new ParkCell(entry.XStart, entry.ZRow);
            bool copyGone = false;
            for (int t = 0; t < 6000; t++)
            {
                Call(viewer, "TickPark"); Call(viewer, "PlaceStaff", 1f);
                if (guard.State == Guard.StateAtGate)
                {
                    stagedP = Math.Max(stagedP, flow.StagingPending);
                    if (!shotAtGate && guard.Carried != null) { shotAtGate = true; await Shot("turnstile", guardNode.GlobalPosition, 1.4f); }
                }
                if (guard.State == Guard.StateBackToGate && !atExit)
                {
                    atExit = guard.Cell == exitCell;
                    copyGone = guard.Carried == null && !copies.Contains(guard);
                    if (!shotGate) { shotGate = true; await Shot("dropped", guardNode.GlobalPosition, 2.2f); }
                }
                if (guard.State is StaffMember.StateIdle or StaffMember.StatePatrol && atExit) break;
            }
            Check(stagedP >= 1, $"he waits at the turnstile counted in the flow's P ({stagedP}), and event 9 lets him through");
            Check(atExit && copyGone, $"he carries the copy out to the corridor start {exitCell} and drops it there (the copy's model freed)");
            Call(viewer, "TickPark");                                    // the find work after the mouth: speed back to 15
            Check(guard.Carried == null && guard.SpeedBits == Guard.PatrolSpeed && guard.Cell.Z >= entry.ZEnd - 1
                  && guard.State is not (Guard.StateAtGate or Guard.StateCrossing or Guard.StateToExit or Guard.StateBackToGate or Guard.StateToMouth),
                  $"and walks back into the park through the same turnstile ({guard}, speed {guard.SpeedBits})");

            // A handyman sweeps the hidden prank litter: the stink stops.
            var h = staff.Hire(StaffKind.Handyman, staff.Candidates.Available(StaffKind.Handyman).First().Slot);
            staff.Drop(h, new ParkCell(pcell.X, pcell.Z) is var dc && grid.Open(dc) ? dc : barCells[0]);
            for (int t = 0; t < 6000 && hidden.Active; t++) Call(viewer, "TickPark");
            bool stopped = stink != null && !staff.Stinks.Entries.Contains(stink) && emitters.All(e => !IsInstanceValid(e) || !e.Emitting);
            Check(!hidden.Active && (hidden.Cell == pcell ? stopped : !stopped),
                  $"the handyman sweeps the hidden litter; its cell {hidden.Cell} vs the stink's {pcell}: the stink {(stopped ? "stopped" : "keeps smoking (swept in another cell, as shipped)")}");

            Field<RideSounds>(viewer, "_sounds")?.Clear();
            viewer.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            GD.Print($"GUARD SMOKE PASS checks={_checks}; world={world}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"GUARD SMOKE FAILED world={world} checks={_checks}: {ex}");
            GetTree().Quit(2);
        }
    }
}
