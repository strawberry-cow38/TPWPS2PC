using System.Reflection;
using Godot;
using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPWPS2Viewer.Tests;

/// <summary>A mechanic on the shipping Viewer, end to end (findings/staff-mechanics-guards.md; the spec is
/// ghidra_tpw/notes/SPEC-staff-mechanics.md): an ordinary ride put down by the build tool beside a path;
/// its reliability forced below 10.0 (⚠ the ONE test hook, <see cref="ParkRide.ForceReliabilityForTest"/>)
/// so it breaks, and its own script answers VAR_BREAKSTAT; a mechanic hired through the laptop's
/// Mechanics tab and put down by the real press; "Call Mechanic" from the ride's menu; his walk to the
/// door, the repair (logical 16: s4, s5, s6, facing the ride, the noise on its handle), the ride open
/// again at 100 on the Details page; then an upgrade requested and installed, paid for at completion.
///
/// Run with --map=WORLD --mode=park. `TPW_MECH_SHOT=dir` saves the broken ride, the repair, the ride
/// reopened and the install.
///
/// ⚠ The ride is chosen per world from ones whose script sets VAR_BROKEN and raises a particle effect
/// when VAR_BREAKSTAT goes to 1 (a core probe over every ordinary ride, 2026-09-27), so "visibly broken"
/// has something to see; the list is the fixture, not a claim about the others.</summary>
public partial class MechanicSmoke : Node3D
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

    static readonly Dictionary<string, string[]> Subjects = new()
    {
        ["JUNGLE"] = new[] { "snake", "Totem", "IncaGod", "Bouncy" },
        ["FANTASY"] = new[] { "bigapple", "bbugs", "flyfoun" },
        ["HALLOW"] = new[] { "candle", "Demon", "brainb" },
        ["SPACE"] = new[] { "slide", "spawheel", "bumper", "mbuggy" },
    };

    int _checks;
    void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++;
        GD.Print("MECHANIC SMOKE ok: " + label);
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
            string shots = System.Environment.GetEnvironmentVariable("TPW_MECH_SHOT");
            if (shots != null) System.IO.Directory.CreateDirectory(shots);
            string park2 = map.Contains("terrain_2", StringComparison.OrdinalIgnoreCase) ? "2" : "1";

            viewer = new Viewer { Name = "Viewer" };
            Set(viewer, "_guestCap", 0);          // no arrivals: the ride and the mechanic are the subject
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

            // A corridor in from the gate and three bars across it, seven rows apart so a 5-deep ride
            // fits between two with its door stubs on the upper one; laid by the path tool's own leg.
            var paths = Field<PathTool>(viewer, "_paths");
            var corridor = new List<(int X, int Y)>();
            for (int z = entry.ZEnd; z < entry.ZEnd + 18; z++)
                if (park.IsPlayable(entry.XCol, z) && park.Vacant(entry.XCol, z)) corridor.Add((entry.XCol, z));
            Call(viewer, "LayLeg", corridor, PathTool.Kind.Path, 0);
            var bar = new List<(int X, int Y)>();
            foreach (int barZ in new[] { entry.ZEnd + 3, entry.ZEnd + 10, entry.ZEnd + 17 })
            {
                var leg = new List<(int X, int Y)>();
                for (int x = entry.XCol - 10; x <= entry.XCol + 10; x++)
                    if (park.IsPlayable(x, barZ) && park.Vacant(x, barZ)) leg.Add((x, barZ));
                Call(viewer, "LayLeg", leg, PathTool.Kind.Path, 0);
                bar.AddRange(leg);
            }
            Call(viewer, "RefreshFloor");
            Check((bool)Call(viewer, "OpenGate"), "the guest layer opened on the corridor");
            var grid = Field<GuestWalk>(viewer, "_guests").Paths;
            // Only the cells joined to the corridor's first cell count: a bar with a gap in it (a cell that
            // could not be laid) leaves a stretch the walk cannot reach from the gate.
            var open = corridor.Concat(bar).Where(c => grid.Open(new ParkCell(c.X, c.Y))).ToHashSet();
            var laid = new List<(int X, int Y)>();
            if (corridor.Count > 0 && open.Contains(corridor[0]))
            {
                var frontier = new Queue<(int X, int Y)>(); frontier.Enqueue(corridor[0]);
                var seen = new HashSet<(int X, int Y)> { corridor[0] };
                while (frontier.TryDequeue(out var c))
                {
                    laid.Add(c);
                    foreach (var n in new[] { (c.X + 1, c.Y), (c.X - 1, c.Y), (c.X, c.Y + 1), (c.X, c.Y - 1) })
                        if (open.Contains(n) && seen.Add(n)) frontier.Enqueue(n);
                }
            }
            Check(laid.Count >= 12, $"{laid.Count} path cells are open to walk and joined to the gate ({open.Count} laid)");

            // The ride, placed by the build tool with its door stub on the path.
            Call(viewer, "ShowBuildCategory", "Rides");
            var rows = Field<List<int>>(viewer, "_buildRows");
            string Leaf(AssetLibrary.RideAssets a) => System.IO.Path.GetFileNameWithoutExtension(a.Model?.Path ?? a.Name);
            var wanted = Subjects[world];
            var candidates = wanted.SelectMany(stem => Enumerable.Range(0, rows.Count)
                    .Where(r => Leaf(lib.Rides[rows[r]]).Equals(stem, StringComparison.OrdinalIgnoreCase)))
                .Where(r => ((RideDefinition)Call(viewer, "DefinitionFor", lib.Rides[rows[r]].Model))?.CompiledEntry?.Kind
                            == AssetResourceDatabase.AssetKind.Ride)
                .ToList();
            Check(candidates.Count > 0, $"{world}: the Rides list has a subject ride ({string.Join("/", wanted)}): {candidates.Count}"
                                        + (candidates.Count > 0 ? "" : $" [of {string.Join(" ", rows.Select(r => Leaf(lib.Rides[r])))}]"));
            var blueprint = Field<Placement>(viewer, "_place");
            bool placed = false; string subjectName = null;
            foreach (int row in candidates)
            {
                for (int turn = 0; turn < 4 && !placed; turn++)
                {
                    Call(viewer, "ArmFromList", row); blueprint.Turn(turn);
                    for (int z = entry.ZEnd + 1; z < entry.ZEnd + 18 && !placed; z++)
                        for (int x = entry.XCol - 12; x <= entry.XCol + 12 && !placed; x++)
                        {
                            if (!blueprint.Fits(park, x, z)) continue;
                            var stubs = blueprint.Stubs(park, x, z).ToArray();
                            // Every stub beside the path, and the ENTRANCE stub on free ground, so its queue is
                            // one cell to lay.
                            if (stubs.Length == 0 || !stubs.All(s => ParkPaths.Neighbours(new(s.X, s.Y)).Any(c => laid.Contains((c.X, c.Z))))
                                || !stubs.Where(s => s.Entrance).All(s => paths.KindAt(s.X, s.Y) == PathTool.Kind.None)) continue;
                            int count = park.Placed.Count;
                            Set(viewer, "_cursorOverride", (x, z));
                            try { Call(viewer, "PlaceHeld"); } finally { Set(viewer, "_cursorOverride", null); }
                            placed = park.Placed.Count == count + 1;
                        }
                }
                if (placed) { subjectName = lib.Rides[rows[row]].Name; break; }
            }
            Check(placed, $"the build tool placed {subjectName ?? "a subject ride"} with its stub on the path");
            Call(viewer, "CloseTool");
            Call(viewer, "TickPark");
            var sim = Field<ParkSim>(viewer, "_sim");
            var staff = Field<ParkStaff>(viewer, "_staff");
            var ride = sim.Rides.Single(r => r.ServiceClass == RideServiceClass.Ordinary);
            int placedIndex = park.Placed.Select((p, i) => (p.Id, i)).Where(x => x.Id == ride.Id).Select(x => x.i).DefaultIfEmpty(-1).First();
            Check(staff != null && placedIndex >= 0 && ride.Entrance is { } stub0 && ParkSim.WorkCell(ride) == stub0,
                  $"{ride.Name}: an ordinary ride (class 3) whose work cell vt+0x17C is its door stub {ride.Entrance}");
            // The queue: the door stub to the path, one run laid as the queue tool lays it. The mechanic's
            // work cell vt+0x17C is that stub, and his way out (vt+0xF4) the queue's mouth on the path.
            var stubCell = ride.Entrance.Value;
            var mouth = ParkPaths.Neighbours(stubCell).First(c => laid.Contains((c.X, c.Z)));
            Call(viewer, "LayLeg", new List<(int X, int Y)> { (stubCell.X, stubCell.Z), (mouth.X, mouth.Z) }, PathTool.Kind.Queue, ride.Id);
            Call(viewer, "RefreshFloor");
            var shape = (NativeQueueShape)Call(viewer, "RideQueueShape", ride);
            Check(shape?.Mouth == mouth && grid.Kind(stubCell) == ParkPathKind.Queue,
                  $"a one-cell queue from the stub {stubCell} to the path, read back with its mouth {shape?.Mouth} (the leave-cell adapter)");
            var advisors = new List<int>();
            var priorAdvisor = sim.Advisor;
            sim.Advisor = (id, r) => { advisors.Add(id); priorAdvisor?.Invoke(id, r); };
            // Long enough for every subject's script to finish standing up and reach its breaktest loop
            // (candle takes its Create animation first; a break before that is answered later).
            for (int i = 0; i < 400; i++) Call(viewer, "TickPark");
            Check(ride.Status == 2 && ride.Reliability == ParkSim.FullReliability && !ride.ServiceFlag && ride.Machine[4] == 0,
                  $"it opens at status 2 with reliability 100.0 and VAR_BREAKSTAT 0 (status {ride.Status}, rel 0x{ride.Reliability:X})");

            // The Details page: State of Repair (644) is ride[0xE4] >> 12 now.
            Call(viewer, "LoadHudFont");
            var panel = Field<LaptopShopScreen>(viewer, "_shopPanel");
            Check(panel != null, "the laptop panel exists");
            int repairRow = LaptopScreen.Ride.Rows.ToList().FindIndex(r => r.TextId == 644);
            int Repair()
            {
                Call(viewer, "ShowRideDetails", ride);
                return Panel<List<(string Text, int Fraction)>>(panel, "_cells")[repairRow].Fraction;
            }
            Check(repairRow >= 0 && Repair() == 100, $"Details: State of Repair (row 644) reads 100 on a new ride ({Repair()})");

            // ---------------------------------------------------------------------------------
            // The breakdown: below 10.0 from status 2 (0x116D68); the script answers VAR_BREAKSTAT.
            var effects = new List<RsePreviewHost.Effect>();
            ride.Host.EffectRequested += fx => effects.Add(fx);
            var burst = Field<RideParticles>(viewer, "_burst");
            int spawnedBefore = burst?.Spawned ?? 0, stoppedBefore = burst?.Stopped ?? 0;
            ride.ForceReliabilityForTest(0x5000);      // ⚠ TEST HOOK: 20.0 -> 0x5000 is 5.0 (5 in the bar)
            Call(viewer, "TickPark");
            Call(viewer, "TickPark");
            Check(ride.Status == 4 && ride.ServiceFlag && ride.Machine[4] == 1 && advisors.Contains(ParkSim.AdvisorBreakdownImminent),
                  $"below 10.0 it breaks: status 4, advisor 0x36, the service flag and VAR_BREAKSTAT = 1 (script variable 4) (status {ride.Status})");
            Check(Repair() == 5, $"Details: State of Repair reads 5 on the broken ride (0x5000 >> 12) -- the old Condition binding would still say 100 ({Repair()})");
            int brokenVar = ride.Machine.Program.VariableNames.ToList().IndexOf("VAR_BROKEN");
            bool scriptBroken = false;
            for (int i = 0; i < 1500 && !(scriptBroken && effects.Any(Particle)); i++)
            {
                Call(viewer, "TickPark");
                scriptBroken |= brokenVar >= 0 && ride.Machine[brokenVar] == 1;
            }
            static bool Particle(RsePreviewHost.Effect fx) => fx.Opcode is RseOpcode.ADDOBJ or RseOpcode.EVENT && fx.Arguments.Count >= 3 && fx.Arguments[0] is 1 or 2;
            int spawned = (burst?.Spawned ?? 0) - spawnedBefore;
            Check(scriptBroken && effects.Any(Particle) && spawned > 0,
                  $"its own script answers: VAR_BROKEN = 1 and a particle effect ({string.Join(",", effects.Where(Particle).Select(e => $"{e.Opcode} node {e.Arguments[1]} id {e.Arguments[2]}").Distinct().Take(4))}), {spawned} drawn");
            var cam = Field<Camera3D>(viewer, "_cam");
            Set(viewer, "_freeCam", true);
            async Task Frames(int n = 2)
            {
                for (int i = 0; i < n; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
            }
            Vector3 World(float x, float z) => (Vector3)Call(viewer, "GuestWorld", new Vector3(x, 0, z),
                                                           new ParkCell(Mathf.FloorToInt(x), Mathf.FloorToInt(z)));
            var rideCentre = World(ride.Origin.X + ride.Width / 2f, ride.Origin.Z + ride.Height / 2f);
            var doorAt = World(ride.Entrance.Value.X + 0.5f, ride.Entrance.Value.Z + 0.5f);
            var outward = (doorAt - rideCentre) with { Y = 0 };
            outward = outward.LengthSquared() > 1e-4f ? outward.Normalized() : Vector3.Back;
            async Task Shot(string name, Vector3 at, float far, float lift = 0.3f, double settle = 0)
            {
                if (shots == null) return;
                // The UI is hidden for the frame and put back after: the laptop is clicked later.
                var layers = viewer.FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>().Where(l => l.Visible).ToList();
                var uis = viewer.GetChildren().OfType<Control>().Where(u => u.Visible).ToList();
                foreach (var layer in layers) layer.Visible = false;
                foreach (var ui in uis) ui.Visible = false;
                var side = outward.Cross(Vector3.Up).Normalized();
                cam.GlobalPosition = at + outward * far + side * far * 0.4f + Vector3.Up * far * 0.75f;
                cam.LookAt(at + Vector3.Up * lift, Vector3.Up);
                // Particles age in WALL time: give the smoke time to rise (or to die out) first.
                if (settle > 0) await ToSignal(GetTree().CreateTimer(settle), SceneTreeTimer.SignalName.Timeout);
                await Frames(3);
                Call(viewer, "SaveShot", System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}{park2}_{name}.png"));
                foreach (var layer in layers) layer.Visible = true;
                foreach (var ui in uis) ui.Visible = true;
            }
            await Shot("broken", rideCentre, 4.5f, 1.2f, 2.0);

            // ---------------------------------------------------------------------------------
            // The laptop, clicked: Hire -> Mechanics -> a candidate -> Hire; carried and put down.
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
            if (panel.Open) Call(viewer, "ToggleLaptop");
            await Frames();
            var laptopBack = Field<List<(string Kind, string Arg)>>(viewer, "_laptopBack");
            laptopBack.Clear();
            Call(viewer, "ToggleLaptop");
            await Frames();
            var mainOpts = LaptopMainMenu.VisibleMain(parkOpen: Field<bool>(viewer, "_laptopParkOpen")).ToList();
            int hireRow = mainOpts.FindIndex(o => o.Opens == "main_bh_staff");
            Check(panel.Open && hireRow >= 0, $"the laptop is open and its main menu has Hire at row {hireRow}");
            Click(panel.MenuRowScreenBox(hireRow).GetCenter());
            await Frames();
            int mechanicsTab = Array.IndexOf(StaffTables.HireTabs, StaffKind.Mechanic);
            Check(laptopBack.Count >= 1 && laptopBack[^1].Kind == "hiretabs" && mechanicsTab >= 0,
                  $"clicking Hire opens the tabs; Mechanics is tab {mechanicsTab}");
            Click(panel.MenuRowScreenBox(mechanicsTab).GetCenter());
            await Frames();
            var text = Field<TextDatabase>(viewer, "_text");
            var c0 = staff.Candidates.Available(StaffKind.Mechanic).First();
            Check(laptopBack[^1] == ("hire", $"{(int)StaffKind.Mechanic}:0") && Panel<string>(panel, "_title") == c0.Name(text),
                  $"the Mechanics tab shows {c0.Name(text)}");
            Click(panel.BuildRowScreenBox.GetCenter());
            await Frames(1);
            var held = Field<StaffMember>(viewer, "_hireHeld");
            Check(held is Mechanic && held.Held && held.CandidateSlot == c0.Slot && staff.Count(StaffKind.Mechanic) == 1 && !panel.Open,
                  $"clicking Hire hands {c0.Name(text)} to the hire tool as a Mechanic, held, 1 mechanic on the books");
            var m = (Mechanic)held;
            var dropCell = corridor.First(c => c.Y == entry.ZEnd + 3);
            var dropPoint = new Point((short)(dropCell.X * 256 + 0x80), (short)(dropCell.Y * 256 + 0x80));
            Set(viewer, "_cursorPointOverride", (Point?)dropPoint);
            Call(viewer, "UpdateHireCarry");
            Call(viewer, "PressHireTool");
            Set(viewer, "_cursorPointOverride", null);
            Check(!m.Held && m.State == 0 && m.Target == null && Field<StaffMember>(viewer, "_hireHeld") == null,
                  $"the press puts him down on the path, idle ({m})");

            // "Call Mechanic" from the ride's own menu (list box row 179 -> 0x124158).
            Set(viewer, "_selected", placedIndex);
            var entries = ((IEnumerable<string>)Call(viewer, "MenuEntriesFor", placedIndex)).ToList();
            Check(entries.Contains("Call Mechanic"), $"the ride's menu offers Call Mechanic: {string.Join(" / ", entries)}");
            Call(viewer, "OnObjectMenu", "Call Mechanic");
            Check(ReferenceEquals(m.Job, ride) && ReferenceEquals(ride.AssignedMechanic, m) && m.State == Mechanic.StateGoRepair
                  && m.RepairDispatches == 1,
                  $"Call Mechanic dispatches him as a REPAIR (0x178CF8: ride +0x80 = him, state 0x38) -- state 0x{m.State:X}");

            // ---------------------------------------------------------------------------------
            // The walk, the repair, the reopening.
            var actors = (System.Collections.IDictionary)Member("_staffActors").GetValue(viewer);
            object Actor() => actors[m] ?? throw new InvalidOperationException("no actor for the mechanic");
            var node = (Node3D)F(Actor(), "Node");
            var drawn = (AnimatedModel)F(Actor(), "Drawn");
            Check(drawn != null && node != null, $"he is drawn ({F(Actor(), "ModelPath")})");
            var sounds = Field<RideSounds>(viewer, "_sounds");
            int cuesBefore = sounds?.Census.Count ?? 0;
            int arrivedAt = -1, sixAt = -1, sevenAt = -1, leftAt = -1, repairTicks = 0, facedTicks = 0, wrongFaced = 0, t0 = 0;
            ParkCell arrivedCell = default, leftCell = default;
            var slots = new List<int>();
            bool shotRepair = false;
            for (int t = 0; t < 6000 && leftAt < 0; t++)
            {
                byte before = m.State;
                Call(viewer, "TickPark");
                Call(viewer, "PlaceStaff", 1f);
                if (arrivedAt < 0 && m.State == Mechanic.StateClosingRide)
                {
                    arrivedAt = t; arrivedCell = m.Cell;
                    // ⚠ FIXTURE: turn him AWAY from the ride as he arrives. His last step already points
                    // at the door, so without this "faces the ride" would pass on the walk's facing alone;
                    // now only 0x1787D0 in the 0xE ticks can turn him back.
                    typeof(StaffMember).GetProperty("FacingQuarterTurns")!.SetValue(m, (ride.NativeRotation + 2) & 3);
                }
                if (sixAt < 0 && ride.Status == 6) sixAt = t;
                if (sixAt >= 0 && sevenAt < 0 && ride.Status != 6) sevenAt = t;
                if (sevenAt >= 0 && leftAt < 0 && m.Mode == Mechanic.ModeLeave && m.Target == null) { leftAt = t; leftCell = m.Cell; }
                if (arrivedAt >= 0 && drawn.Record is { } rec && (slots.Count == 0 || slots[^1] != rec.Slot)) slots.Add(rec.Slot);
                if (before == Mechanic.StateRepairing)
                {
                    repairTicks++;
                    float yaw = (float)F(Actor(), "Yaw");
                    float want = Mathf.PosMod(Mathf.Pi - ride.NativeRotation * (Mathf.Pi / 2), Mathf.Tau);
                    float wrong = Mathf.PosMod(Mathf.Pi - (ride.NativeRotation + 1) * (Mathf.Pi / 2), Mathf.Tau);
                    if (m.FacingQuarterTurns == ride.NativeRotation && Mathf.Abs(Mathf.AngleDifference(yaw, want)) < 1e-3f) facedTicks++;
                    if (Mathf.Abs(Mathf.AngleDifference(yaw, wrong)) > 0.5f) wrongFaced++;
                    if (!shotRepair && drawn.Record is { Slot: 5 } && repairTicks > 20) { shotRepair = true; await Shot("repairing", node.GlobalPosition, 2.2f); }
                }
                t0 = t;
            }
            int T = StaffTables.MechanicWorkTicks[m.Level];
            var work = ParkSim.WorkCell(ride);
            Check(arrivedAt > 0 && arrivedCell == work,
                  $"he walks to the work cell outside the door {work} (arrived at {arrivedCell}, tick {arrivedAt})"
                  + (arrivedAt > 0 ? "" : $" [now {m}; the work cell is {grid.Kind(work)}; "
                                        + $"its neighbours {string.Join(" ", ParkPaths.Neighbours(work).Select(c => $"{c}:{grid.Kind(c)}"))}]"));
            Check(sixAt == arrivedAt + 1 && repairTicks == T + 1 && sevenAt == sixAt + T + 2,
                  $"level {m.Level}: status 6 the tick after arrival, {repairTicks} repairing ticks (T+1 = {T + 1}), 7 at +{sevenAt - sixAt} (T+2)");
            Check(facedTicks == repairTicks && wrongFaced == repairTicks,
                  $"he faces the ride while he works (0x1787D0: quarter turns = its rotation {ride.NativeRotation}; yaw pi - facing on {facedTicks} of {repairTicks}); control: a quarter turn off is wrong on {wrongFaced}");
            Check(leftAt > sevenAt && leftCell == mouth,
                  $"then he walks out to the queue's mouth {mouth} (vt+0xF4, the adapter) and lets the ride go (at {leftCell}, tick {leftAt})");
            int i4 = slots.IndexOf(4), i5 = slots.IndexOf(5), i6 = slots.IndexOf(6);
            Check(i4 >= 0 && i5 > i4 && i6 > i5,
                  $"the work is drawn: logical 16 plays s4, then s5, then s6 as he stops (slot changes {string.Join(",", slots)})");
            var cues = sounds?.Census.Skip(cuesBefore).ToList() ?? new List<string>();
            var noise = cues.Where(c => c.Contains("staff Mechanic") && c.Contains("GlobalRide") && c.Contains("evt 111")).ToList();
            Check(noise.Count > 0 && noise.Count < repairTicks,
                  $"the repair noise (bank 2 event 0x6F on handle P+0x60) is cued at him and not restarted while it sounds: {noise.Count} cues over {repairTicks} ticks");
            Check(ride.Status == 10 && ride.ReliabilityPercent == 100 && !ride.ServiceFlag && ride.Machine[4] == 0 && ride.AssignedMechanic == null,
                  $"the ride reopens: 7 -> 2 -> 10 ({ride.Status}), reliability {ride.ReliabilityPercent}, flag and VAR_BREAKSTAT clear, unassigned");
            bool scriptFixed = false;
            for (int i = 0; i < 600 && !scriptFixed; i++) { Call(viewer, "TickPark"); Call(viewer, "PlaceStaff", 1f); scriptFixed = ride.Machine[brokenVar] == 0; }
            Check(scriptFixed, "its script takes its fixed branch: VAR_BROKEN back to 0");
            for (int i = 0; i < 600 && burst.Stopped - stoppedBefore < spawned; i++) { Call(viewer, "TickPark"); Call(viewer, "PlaceStaff", 1f); }
            Check(burst.Stopped - stoppedBefore == spawned,
                  $"and its KILLOBJ stops the smoke it started: {burst.Stopped - stoppedBefore} of {spawned} continuous emitters stopped (RideParticles.Stop)");
            Check(Repair() == 100, $"Details: State of Repair reads 100 again ({Repair()})");
            await Shot("reopened", rideCentre, 4.5f, 1.2f, 4.5);

            // ---------------------------------------------------------------------------------
            // An upgrade: requested with no money taken, installed by him, paid for at completion.
            var e0 = ride.Definition.CompiledEntry;
            int balance = sim.Finances.Balance;
            var request = staff.RequestUpgrade(ride);
            Check(request == ParkStaff.UpgradeRequest.Queued && ride.UpgradePending && sim.UpgradeList.Contains(ride) && sim.Finances.Balance == balance,
                  $"RequestUpgrade (0x1D5C00): queued, +0x128 set, no money taken ({sim.Finances.Balance - balance})");
            cuesBefore = sounds?.Census.Count ?? 0;
            int paid = 0, installTicks = 0; bool smokedForUpgrade = false, shotInstall = false;
            for (int t = 0; t < 8000 && ride.CurrentTier == 0; t++)
            {
                int bal = sim.Finances.Balance;
                byte before = m.State;
                Call(viewer, "TickPark");
                Call(viewer, "PlaceStaff", 1f);
                if (sim.Finances.Balance != bal && ride.CurrentTier == 0) paid++;
                if (before == Mechanic.StateInstalling)
                {
                    installTicks++;
                    smokedForUpgrade |= ride.Machine[4] == 1 && ride.Status == 6;
                    if (!shotInstall && installTicks > 20) { shotInstall = true; await Shot("installing", node.GlobalPosition, 2.2f); }
                }
            }
            int cost = e0.Tier(1).PurchaseCost * 10;
            Check(ride.CurrentTier == 1 && paid == 0 && balance - sim.Finances.Balance == cost,
                  $"he installs it: tier 0 -> 1, and the new tier's cost x 10 = {cost} is debited at COMPLETION ({balance} -> {sim.Finances.Balance})");
            Check(installTicks == StaffTables.MechanicWorkTicks[m.Level] + 1 && smokedForUpgrade,
                  $"the install is state 0x36 for T+1 = {installTicks} ticks with the ride in 6 and VAR_BREAKSTAT 1 (kind 2)");
            cues = sounds?.Census.Skip(cuesBefore).ToList() ?? new List<string>();
            Check(cues.Any(c => c.Contains("GlobalRide") && c.Contains("evt 184")),
                  $"the upgrade sound, bank 2 event 0xB8, is cued at the ride: {cues.Count(c => c.Contains("evt 184"))}");
            for (int i = 0; i < 4; i++) { Call(viewer, "TickPark"); Call(viewer, "PlaceStaff", 1f); }
            Check(!ride.UpgradePending && !sim.UpgradeList.Contains(ride) && ride.Status == 10 && ride.ReliabilityPercent == 100,
                  $"then it opens again: off the list, status {ride.Status}, reliability {ride.ReliabilityPercent}");
            await Shot("installed", rideCentre, 4.5f, 1.2f, 1.0);

            Field<RideSounds>(viewer, "_sounds")?.Clear();
            viewer.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            GD.Print($"MECHANIC SMOKE PASS checks={_checks}; world={world}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MECHANIC SMOKE FAILED world={world} checks={_checks}: {ex}");
            GetTree().Quit(2);
        }
    }
}
