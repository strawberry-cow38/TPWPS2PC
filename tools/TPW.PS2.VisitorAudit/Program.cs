using TPW.PS2.Audits;
using TPW.PS2.Data;

if (args.Length != 1) { Console.Error.WriteLine("Usage: VisitorAudit /path/to/disc.bin"); return 2; }
string context = "census unavailable (assets not read)";
try
{
    using var disc = new Disc(args[0]);
    WadArchive Wad(string world)
    {
        var e = disc.Files().Single(e => e.Path.Equals($"/DATA/{world}.WAD", StringComparison.OrdinalIgnoreCase));
        return new WadArchive(disc.Read(e.Extent, e.Size));
    }
    var data = Wad("DATA");
    var verdicts = new List<string>();
    // FANTASY comes first: reverting CanBuild to Raw0 == 0 must fail before any cycle.
    foreach (string world in new[] { "FANTASY", "SPACE", "HALLOW" })
    {
        var wad = Wad(world);
        foreach (int terrain in new[] { 1, 2 })
        {
            var expected = new VisitorExpectations(wad, world, terrain, line => { context = line; Console.WriteLine(line); });
            Cycle(wad, data, expected);
            if (terrain == 1) Controls(wad, expected);
            verdicts.Add(expected.Evidence);
        }
    }
    Console.WriteLine("PASS: Ada's exact positions, RSS/APS timelines, guest identities and controls; " + string.Join("; ", verdicts));
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine($"FAIL [{context}]: {ex}"); return 1; }

static void Eq<T>(T actual, T expected, string label)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected)) throw new Exception($"{label}: expected {expected}, got {actual}");
}
static void Yes(bool value, string label) { if (!value) throw new Exception(label); }
static void Sequence<T>(IEnumerable<T> actual, IEnumerable<T> expected, string label) =>
    Eq(string.Join('|', actual), string.Join('|', expected), label);
static void Cycle(WadArchive wad, WadArchive data, VisitorExpectations expected)
{
    var scenario = new VisitorScenario(wad, expected.World, expected.TerrainNumber);
    expected.CheckLayout(scenario);
    var sim = scenario.Simulation; var vm = sim.Machine; var entrance = expected.Spawn;
    var ada = sim.Visitors.Single(g => g.Id == 101); Eq(ada.Name, "Ada", "guest identity");
    foreach (var g in sim.Visitors)
    {
        var e = data.Find(g.ModelPath) ?? throw new Exception($"Missing {g.Name}'s model {g.ModelPath}");
        Eq(e.Path.ToLowerInvariant(), g.ModelPath.ToLowerInvariant(), "character path identity");
        var m = new Model(data.Read(e)); Yes(m.Meshes.Any(mesh => m.Triangles(mesh).Any()), g.Name + " has no drawable geometry");
    }
    var traces = new List<VisitorSimulation.Transition>();
    var heads = new List<(RseOpcode Op, int Id)>();
    var seats = new Dictionary<int, int>();
    sim.Host.HeadChanged += e =>
    {
        if (e.Guest != 0) { seats.Add(e.Slot, e.Guest); heads.Add((RseOpcode.ADDHEAD, e.Guest)); }
        else { heads.Add((RseOpcode.DELHEAD, seats[e.Slot])); seats.Remove(e.Slot); }
    };
    sim.Changed += e =>
    {
        traces.Add(e);
        if (e.GuestId == 101 && e.Action != "step") Console.WriteLine($"{expected.Label} {e}");
        if (e.Action == "board")
        {
            int seat = Array.IndexOf(new[] { 101, 202, 303, 404 }, e.GuestId) + 1;
            Eq(e.LetMeOn, 0, e.Name + " consumed mailbox"); Eq(e.OnRide, seat, e.Name + " script occupancy");
            Eq(e.SpaceLeft, expected.Capacity - seat, e.Name + " script capacity left");
            if (expected.World != "FANTASY") Eq(e.StartNow, (int)e.Time + expected.Timeout, e.Name + " script deadline reset");
            Sequence(vm.GuestIds, new[] { 101, 202, 303, 404 }.Take(seat), e.Name + " HUSH identity");
        }
        if (e.Action == "unload requested")
        {
            Eq(e.LetMeOff, e.GuestId, e.Name + " HOP identity");
            Yes(!vm.GuestIds.Contains(e.GuestId), e.Name + " still on HUSH stack after HOP");
        }
    };
    int lastRunning = 0; var runningEdges = new List<(long, int)>();
    for (int t = 100; t <= expected.DepartAt; t += 100)
    {
        scenario.AdvanceTo(t);
        if (vm["VAR_RUNNING"] != lastRunning) { lastRunning = vm["VAR_RUNNING"]; runningEdges.Add((t, lastRunning)); }
        if (t == 100)
        {
            Eq(ada.State, VisitorState.Walking, "Ada spawned"); Eq(ada.Cell, entrance, "Ada first cell");
            Eq(ada.NextCell, (ParkCell?)entrance.Offset(1, 0), "Ada first edge");
            Eq(ada.EdgeProgress, 100, "Ada movement at 100ms");
        }
        if (t == 1000) { Eq(ada.Cell, entrance.Offset(1, 0), "Ada first waypoint"); Eq(ada.EdgeProgress, 0, "Ada exact waypoint progress"); }
        var pose = expected.AdaAt(t);
        Eq(ada.State, pose.State, $"Ada state at {t}"); Eq(ada.Cell, pose.Cell, $"Ada cell at {t}");
        Eq(ada.NextCell, pose.Next, $"Ada next cell at {t}"); Eq(ada.EdgeProgress, pose.Progress, $"Ada progress at {t}");
        if (expected.World != "FANTASY" && t == expected.RunningAt - 100) { Eq(vm["VAR_RUNNING"], 0, "timeout equality must still wait"); Eq(vm["VAR_TEMP"], 0, "deadline minus time at equality"); }
        if (t == expected.RunningAt) Eq(vm["VAR_RUNNING"], 1, "source starts ride");
        if (t == expected.StartAnimationAt) Eq(vm["VAR_COUNT"], 1, "source copies duration");
        if (t >= expected.UnloadAt && t < expected.UnloadAt + 1000) { Eq(vm["VAR_LETMEOFF"], 404, "Dee unload mailbox held"); Eq(vm["VAR_ONRIDE"], 4, "script waits for Dee to clear portal"); }
        if (t == expected.AdaAck) { Eq(vm["VAR_ONRIDE"], 0, "script decremented after Ada ack"); Eq(vm["VAR_COUNT"], 0, "source completed one run"); }
        var active = sim.Visitors.Where(g => g.State is VisitorState.Walking or VisitorState.Queuing or VisitorState.Alighting or VisitorState.Leaving).ToArray();
        foreach (var g in active)
        {
            Yes(sim.Paths.Walkable(g.Cell), $"{g.Name} left path at {g.Cell}");
            if (g.NextCell is ParkCell next) Yes(sim.Paths.Walkable(next), $"{g.Name} targets non-path {next}");
            foreach (var other in active.Where(o => o.Id != g.Id))
                Yes(g.Cell != other.Cell && g.NextCell != other.Cell && (g.NextCell == null || g.NextCell != other.NextCell),
                    $"{g.Name}/{other.Name} cell reservation collision at {t}");
        }
    }
    Sequence(runningEdges, expected.RunningEdges, "ride source/APS running transitions");
    Sequence(traces.Where(e => e.Action == "board").Select(e => (e.GuestId, e.Time)),
        new[] { 101, 202, 303, 404 }.Zip(expected.Board), "FIFO boarding identities/times");
    Sequence(traces.Where(e => e.Action == "unload requested").Select(e => (e.GuestId, e.Time)), new[] { 404, 303, 202, 101 }.Zip(expected.Unload), "LIFO unloading identities/times");
    Sequence(heads, new[] { (RseOpcode.ADDHEAD, 101), (RseOpcode.ADDHEAD, 202), (RseOpcode.ADDHEAD, 303), (RseOpcode.ADDHEAD, 404),
        (RseOpcode.DELHEAD, 404), (RseOpcode.DELHEAD, 303), (RseOpcode.DELHEAD, 202), (RseOpcode.DELHEAD, 101) }, "script head effect identities");
    Sequence(traces.Where(e => e.GuestId == 101 && e.Action == "step").Select(e => e.Cell),
        expected.Inward.Skip(1).Concat(expected.Outward.Skip(1)), "Ada full waypoint identity");
    Sequence(traces.Where(e => e.GuestId == 101 && e.Action != "step").Select(e => (e.Action, e.Time)),
        new[] { ("spawn", 100L), ("queue", expected.QueueAt), ("offer", expected.Board[0]), ("board", expected.Board[0]),
            ("unload requested", expected.Unload[^1]), ("unload acknowledged", expected.AdaAck), ("depart", expected.DepartAt) },
        "Ada complete visit identity");
    foreach (var g in sim.Visitors) { Eq(g.State, VisitorState.Departed, g.Name + " departure"); Eq(g.Cell, entrance, g.Name + " left via park entrance"); }
    Eq(vm["VAR_ONRIDE"], 0, "empty ride"); Sequence(vm.GuestIds, Array.Empty<int>(), "no stranded guest IDs in script");
    Console.WriteLine($"CYCLE PASS {expected.Evidence}: exact route and visibility state; HUSH 101 -> HOP 101; RUNNING {expected.RunningAt}..{expected.UnloadAt}ms");
}
static void Controls(WadArchive wad, VisitorExpectations expected)
{
    var s = new VisitorScenario(wad, expected.World); var p = s.Simulation.Paths;
    // These cells fooled the first grid-only placement. The renderer's entrance scenery owns
    // them even though byte0 permits tiles. A screenshot exposed this missing collision input.
    if (expected.World == "SPACE")
    {
        Yes(p.SceneryBlocks(new ParkCell(44, 28)), "SPACE entrance scenery intersection identity");
        Yes(!p.CanBuild(new ParkCell(44, 28)), "fixed entrance scenery must block construction");
    }
    // The original field has ground materials, including the palette's zero sentinel, not a path.
    foreach (var c in p.Cells)
    {
        byte source = s.Terrain.Field.Raw0(c.X, c.Z);
        Eq(p.Field.Raw0(c.X, c.Z), source, "path placement preserves source flag byte");
        if ((source & 1) != 0) Yes(!p.Walkable(c), $"bit-0 blocked terrain became walkable at {c}");
        if (s.LaidCells.Contains(c)) Yes(p.Walkable(c), $"laid bit-0-clear tile is not walkable at {c}, byte0={source:X2}");
        if (!s.LaidCells.Contains(c)) Eq(p.Field.Material(c.X, c.Z), s.Terrain.Field.Material(c.X, c.Z), "unlaid material identity");
    }
    Eq(ParkPaths.Classify("Jpa_QuE1.SSH"), ParkPathKind.Queue, "case-folded queue identity");
    var cut = s.Simulation.Entrance.Offset(2, 0);
    p.Field.Cells[(cut.Z * p.Field.Width + cut.X) * 2 + 1] = (byte)s.Terrain.Field.Material(cut.X, cut.Z);
    Yes(p.Route(s.Simulation.Entrance, s.Simulation.QueueCells[0]) == null, "cut path must disconnect Ada from ride");
    s.AdvanceTo(9000); var ada = s.Simulation.Visitors.Single(g => g.Id == 101);
    Eq(ada.State, VisitorState.Walking, "disconnected Ada cannot queue"); Eq(ada.Cell, s.Simulation.Entrance, "disconnected Ada cell");
    Eq(ada.NextCell, null, "disconnected Ada edge"); Eq(ada.EdgeProgress, 0, "disconnected Ada progress"); Yes(ada.Blocked, "Ada blockage is explicit");
    Eq(s.Simulation.Machine["VAR_LETMEON"], 0, "disconnected guest not offered");
    // Hold the ride closed after the scenario's open time. Guests queue but cannot board.
    s = new VisitorScenario(wad, expected.World); s.AdvanceTo(5000); s.Simulation.SetRideOpen(false); s.AdvanceTo(18000);
    ada = s.Simulation.Visitors.Single(g => g.Id == 101);
    Eq(ada.State, VisitorState.Queuing, "closed ride keeps Ada queued"); Eq(ada.Cell, s.Simulation.QueueCells[0], "Ada queue front");
    Eq(ada.EdgeProgress, 0, "Ada stopped exactly at queue front"); Eq(ada.NextCell, null, "Ada has no pending edge");
    Sequence(s.Simulation.Queue, new[] { 101, 202, 303, 404 }, "closed FIFO identity");
    Eq(s.Simulation.Machine["VAR_LETMEON"], 0, "closed boarding mailbox"); Eq(s.Simulation.Machine["VAR_ONRIDE"], 0, "closed script occupancy");
    s.AdvanceTo(19000); Eq(ada.EdgeProgress, 0, "closed Ada exact motion floor after one second");
    s.Simulation.SetRideOpen(true); s.AdvanceTo(expected.ReopenedBoardBy); Eq(ada.State, VisitorState.Riding, "reopened ride boards Ada");
    // Different caller cadence must preserve actual identities and integer state.
    var one = new VisitorScenario(wad, expected.World); var split = new VisitorScenario(wad, expected.World);
    one.AdvanceTo(expected.DepartAt); foreach (long t in new long[] { 117, 877, 2301, 10049, 17111, 39873, expected.DepartAt }) split.AdvanceTo(t);
    Sequence(split.Simulation.Machine.Variables, one.Simulation.Machine.Variables, "caller cadence VM state");
    Sequence(split.Simulation.Visitors.Select(g => (g.Id, g.State, g.Cell, g.EdgeProgress)),
        one.Simulation.Visitors.Select(g => (g.Id, g.State, g.Cell, g.EdgeProgress)), "caller cadence guest identities");
    var arrival = new VisitorSimulation(new VisitorScenario(wad, expected.World).Simulation.Paths,
        s.Simulation.Machine.Program, s.RideAnimation, expected.Capacity, s.Simulation.Entrance, s.Simulation.QueueCells, s.Simulation.ExitPortal);
    arrival.Schedule(808, "Later", "/Chars/Boy1a/boy1a.mps", 1500);
    arrival.Schedule(909, "First", "/Chars/Girl1a/girl1a.mps", 0);
    var order = new List<int>(); arrival.Changed += e => { if (e.Action == "board") order.Add(e.GuestId); };
    arrival.SetRideOpen(true); while (arrival.Time < 20000) arrival.Step();
    Sequence(order, new[] { 909, 808 }, "FIFO follows arrival, not registration or numeric ID");
    Console.WriteLine($"CONTROLS PASS {expected.Evidence}: broken route, closed queue, reopening, caller cadence, arrival order, bit-0 and material preservation");
}
