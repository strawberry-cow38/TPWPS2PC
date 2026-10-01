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
/// Training now exercises the real screen's row click; its opening is explicit because
/// Single Staff navigation is a separate UI. Patrol/fire tests below still name their core calls.</summary>
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
            // A second member of one type discriminates real per-type paging from a no-op
            // pager and from a single mixed-person list. Retire this fixture before finance checks.
            Call(viewer,"BeginHire",StaffKind.Entertainer,staff.Candidates.Available(StaffKind.Entertainer).First().Slot);
            var screenExtra=Field<StaffMember>(viewer,"_hireHeld");
            var extraCell=bar[0];
            Set(viewer,"_cursorPointOverride",(Point?)new Point((short)(extraCell.X*256+128),(short)(extraCell.Y*256+128)));
            Call(viewer,"PressHireTool");Set(viewer,"_cursorPointOverride",null);
            Check(screenExtra!=null&&screenExtra.Active&&!screenExtra.Held&&staff.MembersOfType(StaffKind.Entertainer).Count()==2,
                "a second real entertainer exercises within-type paging");
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
            Check(laptopBack.Count == 2 && laptopBack[^1].Kind == "stafftypes", "Staff Information opens the type list, not a mixed-person pager");
            var text = Field<TextDatabase>(viewer, "_text");
            var types = staff.TypesWithStaff().ToList();
            Check(Panel<List<string>>(panel, "_menu").SequenceEqual(new[]{68,864,421,887,69}.Select(id=>text.Text("eng",id))),
                "type captions and order match Entertainers/Mechanics/Guards/Researchers/Cleaners");
            if (shots != null) Call(viewer, "SaveShot", ShotPath("stafftypes"));
            int sane=0;
            var activated = new List<int>();
            void RowClick(int i) => activated.Add(i);
            panel.RowActivated += RowClick;
            for (int typeIndex=0; typeIndex<types.Count; typeIndex++)
            {
                Click(panel.MenuRowScreenBox(typeIndex).GetCenter()); await Frames();
                Check(laptopBack.Count==3&&laptopBack[^1].Kind=="staffitem", "type click opens its member screen");
                var members=staff.MembersOfType(types[typeIndex]).ToList();
                for(int page=0;page<members.Count;page++)
                {
                    if(page>0){Call(viewer,"OnLaptopPage",1);await Frames();}
                    var who=members[page];
                    var cells=Panel<List<(string Text,int Fraction)>>(panel,"_cells");
                    string title=Panel<string>(panel,"_title");
                    var (wholeMonths,tenths,unitRow)=who.TimeEmployed;
                    string time=$"{wholeMonths}.{tenths}{text.Text("eng",unitRow)}";
                    Check(title==who.Candidate.Name(text),"page title is the selected candidate's real name");
                    Check(cells.Count==5&&cells[0].Fraction==who.SkillBar&&cells[1].Fraction==who.DisplayedMotivation
                        &&cells[2].Fraction==who.Tiredness&&cells[3].Text==time&&cells[4].Text==Money.Format(who.MonthlyWage*10),
                        "all five displayed values belong to this named member");
                    sane++;
                    var boxes=Panel<Dictionary<int,Rect2>>(panel,"_specRows");
                    Check(boxes.Count==5,"all five drawn rows have hitboxes");
                    for(int row=0;row<5;row++)
                    {
                        float actual=(boxes[row].Position.Y-panel.PanelOrigin.Y)/panel.PanelScale;
                        Check(Math.Abs(actual-(175+32*row))<.01f,$"drawn label/hitbox row {row} at native175+32*i, not shared-widget Y: {actual}");
                        if(row>0)Check(!boxes[row-1].Intersects(boxes[row]),"adjacent staff rows do not overlap");
                        int beforeClicks=activated.Count;
                        Click(new Vector2(panel.PanelOrigin.X+65*panel.PanelScale,boxes[row].GetCenter().Y)); await Frames();
                        Check(activated.Count==beforeClicks+1&&activated[^1]==row,"clicking a drawn row dispatches that row, not row zero");
                    }
                    if(shots!=null&&typeIndex<2)Call(viewer,"SaveShot",ShotPath($"allstaff{typeIndex}"));
                }
                string last=Panel<string>(panel,"_title");
                Call(viewer,"OnLaptopPage",1);await Frames();
                Check(Panel<string>(panel,"_title")==last,"pager cannot step into the next staff type");
                Click(panel.PanelOrigin+new Vector2(365,75)*panel.PanelScale);await Frames();
                Check(laptopBack.Count==2&&laptopBack[^1].Kind=="stafftypes","Back returns to staff types");
            }
            panel.RowActivated-=RowClick;
            Check(sane==staff.Members.Count,"every staff member visited through its type");
            staff.Fire(screenExtra);
            Check(staff.Members.Count==5,"screen-only second-member fixture retired before management finance checks");
            Call(viewer, "ToggleLaptop");
            await Frames();

            // ---------------------------------------------------------------------------------
            // Training: the Training screen's Cross (0x1FF6F8 -> 0x1FF610), through the core call it makes.
            int before = sim.Finances.Balance, level = researcher.Level;
            // The newly merged Training screen is a control: WidgetStep defaults to0, so
            // its existing per-widget placement must survive the All Staff correction.
            // Single Staff navigation is a separate screen; open its target through the real stack.
            laptopBack.Clear();laptopBack.Add(("info",null));
            laptopBack.Add(("stafftraining",staff.Members.ToList().IndexOf(researcher).ToString()));
            Call(viewer,"ShowLaptopLevel");await Frames();
            var trainingCells=Panel<List<(string Text,int Fraction)>>(panel,"_cells");
            int cost = StaffTables.TrainingCost(StaffKind.Researcher, level);
            Check(Panel<string>(panel,"_title")==researcher.Candidate.Name(text),"Training retains its screen and now shares the real candidate name");
            Check(trainingCells.Count==4&&trainingCells[1].Text==Money.Format(cost*10)
                &&trainingCells[2].Text==Money.Format(researcher.WageAfterTraining*10)
                &&trainingCells[3].Fraction==researcher.TrainingBar,"Training's post-training figures survive the merge");
            var trainRows=Panel<Dictionary<int,Rect2>>(panel,"_specRows");
            // Native §12.3 uses InfoText+96 for this label, independently of its bar height.
            Check(trainRows.Count==4&&Math.Abs((trainRows[3].Position.Y-panel.PanelOrigin.Y)/panel.PanelScale-271)<.01f
                && !trainRows[2].Intersects(trainRows[3]),
                "Training skill label stays on native row271 and its hitbox does not overlap the wage");
            if(shots!=null)Call(viewer,"SaveShot",ShotPath("training"));
            Click(new Vector2(panel.PanelOrigin.X+65*panel.PanelScale,trainRows[0].GetCenter().Y));await Frames();
            Check(before - sim.Finances.Balance == cost * 10 && researcher.Level == level + 1
                  && researcher.Tiredness == 0 && researcher.Morale == 100,
                  $"training (real screen row click): the researcher L{level} -> L{researcher.Level} for {Money.Format(cost * 10)}, tiredness 0, morale 100");

            Call(viewer,"ToggleLaptop");await Frames();

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
            int statsBefore = Field<ParkStatistics>(viewer, "_parkStats")?.Months ?? 0;
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
            // ---------------------------------------------------------------------------------
            // The laptop's statistics. That month end recorded one month of the visitor statistics (0x16B478) and
            // filed the year's spending; Visitor Information's gate price spinner (0x207B10, mode 2) steps the fee
            // the gate charges, once per console frame while an arrow is HELD, clamped at $0.
            var pstats = Field<ParkStatistics>(viewer, "_parkStats");
            Check(pstats != null && pstats.Months == statsBefore + 1 && sim.Finances.YearSpending > 0
                  && sim.Finances.LastYearSpending == 0,
                  $"the month end recorded one month of visitor statistics ({statsBefore} -> {pstats?.Months}) and this "
                  + $"year's spending ({Money.Format(sim.Finances.YearSpending)}) with nothing yet in last year's column");
            laptopBack.Clear(); laptopBack.Add(("visitorinfo", null));
            Call(viewer, "ShowLaptopLevel");
            await Frames();
            int spin = LaptopScreen.VisitorInfo.SpinnerRow;
            var arrows = Panel<Dictionary<int, Rect2>>(panel, "_rowArrows");
            Check(panel.Open && arrows.TryGetValue(spin, out var gateArrows) && gateArrows.Size.X > 0,
                  "Visitor Information draws the gate price arrows on its Ticket Price row");
            var gate = arrows[spin];
            // ⚠ Through Input.ParseInputEvent, not PushInput: the spinner asks Input whether the button is
            // still down, and only a parsed event moves that state -- a pushed one would step exactly once.
            async Task Hold(Vector2 at, double seconds)
            {
                Input.WarpMouse(at);
                Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = true });
                await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
                Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = false });
                await Frames();
            }
            var cellsNow = () => Panel<List<(string Text, int Fraction)>>(panel, "_cells");
            int fee0 = Field<int>(viewer, "_entranceFee");
            await Hold(new Vector2(gate.End.X - gate.Size.X / 4f, gate.GetCenter().Y), 0.4);
            int fee1 = Field<int>(viewer, "_entranceFee"), steps = (fee1 - fee0) / 10;
            // 0.4 s at the console's 25 frames a second is 10 steps; the bounds take the timer's granularity.
            Check(fee1 % 10 == 0 && steps >= 5 && steps <= 20 && cellsNow()[spin].Text == Money.Format(fee1),
                  $"holding the right arrow 0.4s steps the gate price {Money.Format(fee0)} -> {Money.Format(fee1)}, "
                  + $"{steps} whole-dollar steps, and the row reads the new price");
            await Frames(6);
            Check(Field<int>(viewer, "_entranceFee") == fee1, "letting go stops the spinner");
            await Hold(new Vector2(gate.Position.X + gate.Size.X / 4f, gate.GetCenter().Y), 2.5);
            Check(Field<int>(viewer, "_entranceFee") == 0 && cellsNow()[spin].Text == Money.Format(0),
                  $"holding the left arrow runs the price down to $0 and it stops there (clamped, no wrap): {cellsNow()[spin].Text}");

            // The graph pages' year selector and series toggles (graph-widget.md §1.6-1.7), through clicks: the
            // arrows step 1 -> 2 -> 6 -> 12 and wrap, an item shows its series, and choosing it AGAIN keeps it shown
            // (clear-then-flip at 0x185AF8 lands on a zeroed toggle).
            laptopBack.Clear(); laptopBack.Add(("statistics", null));
            panel.GraphCursor = 0;
            Call(viewer, "ShowLaptopLevel");
            await Frames();
            var yearArrows = Panel<Rect2>(panel, "_yearArrows");
            Check(yearArrows.Size.X > 0 && Field<int>(viewer, "_parkGraphYears") == 1 && panel.GraphCursor == 0,
                  "Park Statistics draws its year selector, at 1 year, with the cursor on the Years row");
            var spans = new List<int>();
            for (int step = 0; step < 4; step++)
            {
                Click(new Vector2(yearArrows.End.X - yearArrows.Size.X / 4f, yearArrows.GetCenter().Y));
                await Frames();
                spans.Add(Field<int>(viewer, "_parkGraphYears"));
                yearArrows = Panel<Rect2>(panel, "_yearArrows");
            }
            Check(spans.SequenceEqual(new[] { 2, 6, 12, 1 }), $"the right arrow steps the span {string.Join(" -> ", spans)} (1, 2, 6, 12, wrapping)");
            var itemRows = Panel<Dictionary<int, Rect2>>(panel, "_specRows");
            Click(itemRows[2].GetCenter());
            await Frames();
            var pickedReadout = Panel<(string Label, string Value)?>(panel, "_graphReadout");
            Check(Field<Dictionary<string, int>>(viewer, "_graphPick")["statistics"] == 2 && panel.GraphCursor == 3
                  && pickedReadout?.Label == text.Text("eng", LaptopScreen.ParkStatistics.Rows[2].TextId),
                  $"choosing Happiness shows its series and readout ({pickedReadout?.Label}: {pickedReadout?.Value})");
            Click(Panel<Dictionary<int, Rect2>>(panel, "_specRows")[2].GetCenter());
            await Frames();
            Check(Field<Dictionary<string, int>>(viewer, "_graphPick")["statistics"] == 2
                  && Panel<LaptopShopScreen.GraphSeries?>(panel, "_graph") is { Values.Count: > 0 },
                  "choosing it again keeps it shown: the toggles are cleared, then the chosen one flipped ON");

            // The park's loans, through the pages (LoanChecks has the arithmetic): no Existing Loans row until a loan is
            // taken; New Loan's arrows step the lender; a click takes the offer and the page flips to Loan Taken; the
            // menu grows its fifth row; Existing Loans shows the record (Interest WITHOUT its %); a month end repays it.
            void Back() => Click(panel.PanelOrigin + new Vector2(365, 75) * panel.PanelScale);
            laptopBack.Clear(); laptopBack.Add(("financialinfo", null));
            Call(viewer, "ShowLaptopLevel");
            await Frames();
            string existingLoans = text.Text("eng", 472);
            var finMenu = Panel<List<string>>(panel, "_menu");
            Check(finMenu.Count == 4 && !finMenu.Contains(existingLoans), "Financial Information lists four rows, and no Existing Loans before a loan");
            Click(panel.MenuRowScreenBox(3).GetCenter());
            await Frames();
            var pager = Panel<Rect2>(panel, "_pageArrows");
            Check(laptopBack[^1].Kind == "newloan" && pager.Size.X > 0, "New Loan opens from its row, with the lender arrows drawn");
            Click(new Vector2(pager.End.X - pager.Size.X / 4f, pager.GetCenter().Y));
            await Frames();
            Check(Field<int>(viewer, "_loanLender") == 1 && Panel<string>(panel, "_title") == Lender.All[1].Name,
                  $"the right arrow steps the lender to {Panel<string>(panel, "_title")}");
            int balBefore = sim.Finances.Balance;
            Click(Panel<Dictionary<int, Rect2>>(panel, "_specRows")[0].GetCenter());
            await Frames();
            var dabb = sim.Finances.Loans[1];
            Check(dabb.Taken && sim.Finances.Balance - balBefore == dabb.Amount * 10
                  && ReferenceEquals(Panel<LaptopScreen>(panel, "_spec"), LaptopScreen.NewLoanTaken),
                  $"clicking the offer takes it (+{Money.Format(sim.Finances.Balance - balBefore)}) and the page shows only Loan Taken");
            Back();
            await Frames();
            finMenu = Panel<List<string>>(panel, "_menu");
            Check(laptopBack[^1].Kind == "financialinfo" && finMenu.Count == 5 && finMenu[4] == existingLoans,
                  "back on the menu, Existing Loans is its fifth row");
            Click(panel.MenuRowScreenBox(4).GetCenter());
            await Frames();
            var loansPage = Panel<LaptopShopScreen.LoansPage?>(panel, "_loansPage");
            Check(loansPage is { Lender: "Ms Dabb" } lp && lp.Values[0] == Money.Display(dabb.Amount) && lp.Values[2] == "20"
                  && lp.Values[3] == Money.Display(dabb.Repayment) && lp.Values[5] == Money.Display(dabb.Outstanding),
                  $"Existing Loans shows Ms Dabb's record, Interest without its %: {string.Join(" | ", loansPage?.Values ?? Array.Empty<string>())}");
            int owed = dabb.Outstanding, left = dabb.MonthsRemaining, monthsNow = mgmt.MonthChanges, loanDays = 0;
            while (mgmt.MonthChanges == monthsNow && loanDays++ < 40) Call(viewer, "AdvanceCalendar", ParkClock.UnitsPerDay);
            Check(dabb.Outstanding == owed - dabb.Repayment && dabb.MonthsRemaining == left - 1
                  && sim.Finances.LoansOutstanding == dabb.Outstanding,
                  $"the next month end repays {Money.Display(dabb.Repayment)}: {Money.Display(owed)} -> {Money.Display(dabb.Outstanding)} over {dabb.MonthsRemaining} months");

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
