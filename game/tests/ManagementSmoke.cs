using System.Reflection;
using Godot;
using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

namespace TPWPS2Viewer.Tests;

/// <summary>Staff management on the shipping Viewer, end to end (ghidra_tpw/notes/SPEC-staff-management.md,
/// findings/staff-management.md): a crew of all five kinds hired through the hire tool's real press; the
/// laptop's All Staff screen (cow tools') opened through the real Information menu and its rows compared
/// with the member each page shows; a member trained through <see cref="ParkStaff.Train"/> (the Training
/// screen's Cross); a patrol area drawn with the patrol-area tool (mode 17) the viewer's cursor drives,
/// the off-by-one included; a member fired; and a month ended through the viewer's own calendar advance
/// (`0x16B060`), with the wage bill debited.
///
/// Run with --map=WORLD --mode=park. `TPW_MGMT_SHOT=dir` saves the All Staff screen, the patrol-area
/// highlight and the park after the month.
///
/// ⚠ The Training and Single Staff screens are cow tools'; until they exist this drives the core calls
/// they will make, and says so in the check labels.</summary>
public partial class ManagementSmoke : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string name) => typeof(Viewer).GetField(name, Hidden)
        ?? throw new MissingMemberException("Viewer." + name);
    static T Field<T>(Viewer v, string name) => (T)Member(name).GetValue(v);
    static void Set(Viewer v, string name, object value) => Member(name).SetValue(v, value);
    static object Call(Viewer v, string name, params object[] args)
    {
        var method = typeof(Viewer).GetMethod(name, Hidden | BindingFlags.Public) ?? throw new MissingMemberException("Viewer." + name);
        var parameters = method.GetParameters();
        if (args.Length < parameters.Length)
            args = args.Concat(parameters.Skip(args.Length).Select(p => p.HasDefaultValue ? p.DefaultValue
                : throw new ArgumentException("Missing required argument for Viewer." + name))).ToArray();
        return method.Invoke(v, args);
    }
    static T Panel<T>(LaptopShopScreen p, string name) => (T)(typeof(LaptopShopScreen).GetField(name, Hidden)
        ?? throw new MissingMemberException("LaptopShopScreen." + name)).GetValue(p);

    int _checks;
    void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++;
        GD.Print("MANAGEMENT SMOKE ok: " + label);
    }

    public override async void _Ready()
    {
        string world = "unknown";
        try
        {
            Check(DisplayServer.GetName() != "headless", "rendering display required");
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            string map = args.LastOrDefault(a => a.StartsWith("--map="))?["--map=".Length..]
                ?? throw new ArgumentException("Pass --map=WORLD");
            world = new[] { "JUNGLE", "FANTASY", "HALLOW", "SPACE" }.Single(w =>
                map.Equals(w, StringComparison.OrdinalIgnoreCase) || map.StartsWith(w + " ", StringComparison.OrdinalIgnoreCase));
            string park2 = map.Contains("terrain_2", StringComparison.OrdinalIgnoreCase) ? "2" : "1";
            string shots = System.Environment.GetEnvironmentVariable("TPW_MGMT_SHOT");
            if (shots != null) System.IO.Directory.CreateDirectory(shots);
            string ShotPath(string name) => System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}{park2}_{name}.png");

            var viewer = new Viewer { Name = "Viewer" };
            Set(viewer, "_guestCap", 0);
            AddChild(viewer);
            viewer.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var park = Field<Park>(viewer, "_park");
            var terrain = Field<Model>(viewer, "_terrainModel");
            var entrances = Field<ParkEntrance>(viewer, "_entranceTable");
            Check(park?.Field != null && terrain != null && entrances != null, "the park loaded");
            var entry = entrances.Fit(terrain.Field, ParkEntrance.WalkwayColumnFromPoles(terrain), out _);
            Check(!entry.Empty, "the authored entrance fits");

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
            var grid = Field<GuestWalk>(viewer, "_guests").Paths;
            var laid = corridor.Concat(bar).Where(c => grid.Open(new ParkCell(c.X, c.Y))).ToList();
            Check(laid.Count >= 14, $"{laid.Count} path cells are open to walk");
            Call(viewer, "TickPark");
            var sim = Field<ParkSim>(viewer, "_sim");
            var staff = Field<ParkStaff>(viewer, "_staff");
            var clock = Field<ParkClock>(viewer, "_calendar");
            Check(sim != null && staff != null, "every park: the viewer attached a ParkStaff");

            // ---------------------------------------------------------------------------------
            // A crew: one of each kind, through the hire tool (mode 1) and its real press.
            var crew = new List<StaffMember>();
            int k = 0;
            foreach (var kind in StaffTables.HireTabs)
            {
                int slot = staff.Candidates.Available(kind).First().Slot;
                Call(viewer, "BeginHire", kind, slot);
                var held = Field<StaffMember>(viewer, "_hireHeld");
                var cell = bar[(k * 2 + 1) % bar.Count];
                Set(viewer, "_cursorPointOverride", (Point?)new Point((short)(cell.X * 256 + 0x80), (short)(cell.Y * 256 + 0x80)));
                Call(viewer, "PressHireTool");
                Set(viewer, "_cursorPointOverride", null);
                if (held != null && !held.Held && held.Active) crew.Add(held);
                k++;
            }
            Check(crew.Count == 5 && crew.Select(m => m.Kind).Distinct().Count() == 5 && staff.Members.Count == 5,
                  $"a crew of five hired through the hire tool: {string.Join(", ", crew.Select(m => $"{m.Kind} L{m.Level}"))}");
            // Some days on the books, so Time Employed has something to say.
            for (int d = 0; d < 20; d++) Call(viewer, "AdvanceCalendar", ParkClock.UnitsPerDay);
            for (int t = 0; t < 30; t++) Call(viewer, "TickPark");
            var guard = crew.Single(m => m.Kind == StaffKind.Guard);
            var mech = crew.Single(m => m.Kind == StaffKind.Mechanic);
            var researcher = crew.Single(m => m.Kind == StaffKind.Researcher);

            // ---------------------------------------------------------------------------------
            // All Staff (cow tools' screen), through the real laptop: main menu -> Information -> row 4.
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
            Call(viewer, "ToggleLaptop");
            await Frames();
            var mainOpts = LaptopMainMenu.VisibleMain(parkOpen: Field<bool>(viewer, "_laptopParkOpen")).ToList();
            int infoRow = mainOpts.FindIndex(o => o.Opens == "main_info");
            Check(panel.Open && infoRow >= 0, $"the laptop is open with Information at row {infoRow}");
            Click(panel.MenuRowScreenBox(infoRow).GetCenter());
            await Frames();
            Check(laptopBack.Count == 1 && laptopBack[0].Kind == "info", "clicking Information opens its submenu");
            Click(panel.MenuRowScreenBox(4).GetCenter());
            await Frames(3);
            Check(laptopBack.Count == 2 && laptopBack[^1].Kind == "staffitem", $"clicking row 4 (Staff Information, text 995) opens All Staff ({laptopBack[^1].Kind})");
            var text = Field<TextDatabase>(viewer, "_text");
            var pages = new List<string>();
            int sane = 0;
            for (int page = 0; page < staff.Members.Count; page++)
            {
                if (page > 0) { Call(viewer, "OnLaptopPage", 1); await Frames(); }
                var who = staff.Members[page];
                var cells = Panel<List<(string Text, int Fraction)>>(panel, "_cells");
                string title = Panel<string>(panel, "_title");
                var (wholeMonths, tenths, unitRow) = who.TimeEmployed;
                string time = $"{wholeMonths}.{tenths}{text?.Text("eng", unitRow)}";
                bool ok = cells.Count == 5 && cells[0].Fraction == who.SkillBar && cells[1].Fraction == who.DisplayedMotivation
                          && cells[2].Fraction == who.Tiredness && cells[3].Text == time && cells[4].Text == Money.Format(who.MonthlyWage * 10);
                if (ok) sane++;
                pages.Add($"[{title}: skill {cells.ElementAtOrDefault(0).Fraction} motivation {cells.ElementAtOrDefault(1).Fraction} "
                        + $"tired {cells.ElementAtOrDefault(2).Fraction} time '{cells.ElementAtOrDefault(3).Text}' wage {cells.ElementAtOrDefault(4).Text}"
                        + $"{(ok ? "" : $" -- expected {who.SkillBar}/{who.DisplayedMotivation}/{who.Tiredness}/'{time}'/{Money.Format(who.MonthlyWage * 10)}")}]");
                if (shots != null && page < 2) { await Frames(2); Call(viewer, "SaveShot", ShotPath($"allstaff{page}")); }
            }
            GD.Print("[smoke] All Staff pages: " + string.Join(" ", pages));
            Check(sane == staff.Members.Count,
                  $"All Staff shows, for each of the {staff.Members.Count} members, Skill L x 25, Motivation ((100-t)+m)/2, Tiredness, Time Employed d/28.(d%28)x10/28 and the full wage ({sane} sane)");
            Call(viewer, "ToggleLaptop");
            await Frames();

            // ---------------------------------------------------------------------------------
            // Training: the Training screen's Cross (0x1FF6F8 -> 0x1FF610), through the core call it makes.
            int before = sim.Finances.Balance, level = researcher.Level;
            var trained = staff.Train(researcher);
            int cost = StaffTables.TrainingCost(StaffKind.Researcher, level);
            Check(trained == TrainingResult.Trained && before - sim.Finances.Balance == cost * 10 && researcher.Level == level + 1
                  && researcher.Tiredness == 0 && researcher.Morale == 100,
                  $"training (core call of the Training screen): the researcher L{level} -> L{researcher.Level} for {Money.Format(cost * 10)}, tiredness 0, morale 100");

            // ---------------------------------------------------------------------------------
            // The patrol-area tool (mode 17): Single Staff's "Set Patrol Area", the cursor and two presses.
            Call(viewer, "BeginPatrolArea", guard);
            var tool = Field<StaffPatrolTool>(viewer, "_patrolTool");
            Check(tool != null && tool.Member == guard && !panel.Open, "Set Patrol Area hands the guard to the patrol tool and the laptop is closed");
            var first = new ParkCell(entry.XCol - 4, barZ - 2);
            var second = new ParkCell(entry.XCol + 3, barZ + 3);
            Set(viewer, "_cursorOverride", (first.X, first.Z));
            Call(viewer, "UpdatePatrolTool");
            Check(guard.Held && tool.Highlight == (first.X, first.Z, 1, 1), $"the cursor holds the guard (0x40) and lights one cell at {first}");
            Call(viewer, "PressPatrolTool");
            Set(viewer, "_cursorOverride", (second.X, second.Z));
            Call(viewer, "UpdatePatrolTool");
            var ghost = Field<GhostMarkers>(viewer, "_ghostView");
            Check(tool.CornerPlaced && tool.Highlight == (first.X, first.Z, 8, 6) && ghost.Root.GetChildCount() > 0,
                  $"after the first corner the ground lights {tool.Highlight} with marker 0xA5 ({ghost.Root.GetChildCount()} surface(s))");
            if (shots != null)
            {
                var cam = Field<Camera3D>(viewer, "_cam");
                Set(viewer, "_freeCam", true);
                var mid = (Vector3)Call(viewer, "GuestWorld", new Vector3(entry.XCol + 0.5f, 0, barZ + 0.5f), new ParkCell(entry.XCol, barZ));
                cam.GlobalPosition = mid + new Vector3(5f, 11f, 9f);
                cam.LookAt(mid, Vector3.Up);
                for (int i = 0; i < 3; i++) Call(viewer, "PlaceStaff", 1f);
                await Frames(3);
                Call(viewer, "SaveShot", ShotPath("patrol"));
            }
            Call(viewer, "PressPatrolTool");
            Set(viewer, "_cursorOverride", null);
            Check(Field<StaffPatrolTool>(viewer, "_patrolTool") == null && !guard.Held
                  && guard.PatrolX0 == first.X + 1 && guard.PatrolX1 == second.X && guard.PatrolZ0 == first.Z + 1 && guard.PatrolZ1 == second.Z,
                  $"the second press stores ({guard.PatrolX0},{guard.PatrolZ0})..({guard.PatrolX1},{guard.PatrolZ1}): the first corner (the smaller) +1 -- the shipped off-by-one -- and lets him go");

            // ---------------------------------------------------------------------------------
            // Fire: the pro-rata wage paid again (0x100C78).
            int wage = mech.ProRatedWage;
            before = sim.Finances.Balance;
            staff.Fire(mech);
            Check(!mech.Active && before - sim.Finances.Balance == wage * 10 && staff.Members.Count == 4,
                  $"firing the mechanic ({clock.Format()}, {mech.DaysEmployed} days) pays {Money.Format(wage * 10)} (0x1DC338 x 10) and leaves 4 on the books");

            // ---------------------------------------------------------------------------------
            // A month end through the viewer's own calendar advance: strikes, then the wage bill.
            var mgmt = Field<ParkManagement>(viewer, "_management");
            int months = mgmt.MonthChanges, days = 0;
            while (mgmt.MonthChanges == months && days++ < 40)
            {
                before = sim.Finances.Balance;
                Call(viewer, "AdvanceCalendar", ParkClock.UnitsPerDay);
            }
            // All four were hired on 1 January, so at 1 February each has a whole previous month (31 of 31
            // days) and is paid his FULL monthly wage -- the trained researcher at his new level.
            var four = crew.Where(m => m.Active).ToList();
            int due = four.Sum(m => m.MonthlyWage) * 10;
            Check(mgmt.MonthChanges == months + 1 && clock.Day == 0 && four.Count == 4 && four.All(m => m.HireDay == 0)
                  && before - sim.Finances.Balance == due && mgmt.LastWages == due,
                  $"the month ends ({clock.Format()}): the four's full wages ({string.Join("+", four.Select(m => Money.Format(m.MonthlyWage * 10)))} = {Money.Format(due)}) are debited ({Money.Format(before)} -> {Money.Format(sim.Finances.Balance)})");
            if (shots != null)
            {
                for (int t = 0; t < 20; t++) Call(viewer, "TickPark");
                Call(viewer, "PlaceStaff", 1f);
                await Frames(3);
                Call(viewer, "SaveShot", ShotPath("month"));
            }

            Field<RideSounds>(viewer, "_sounds")?.Clear();
            viewer.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            GD.Print($"MANAGEMENT SMOKE PASS checks={_checks}; world={world}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MANAGEMENT SMOKE FAILED world={world} checks={_checks}: {ex}");
            GetTree().Quit(2);
        }
    }
}
