using System.Text;
using System.Text.RegularExpressions;
using TPW.PS2.Data;

if (args.Length != 1) { Console.Error.WriteLine("Usage: VisitorAudit /path/to/disc.bin"); return 2; }
try
{
    using var disc = new Disc(args[0]);
    WadArchive Wad(string world)
    {
        var e = disc.Files().Single(e => e.Path.Equals($"/DATA/{world}.WAD", StringComparison.OrdinalIgnoreCase));
        return new WadArchive(disc.Read(e.Extent, e.Size));
    }
    var space = Wad("SPACE"); var data = Wad("DATA");
    RequireSource(space);
    Cycle(space, data, 1, new(83, 15), new(81, 24));
    Cycle(space, data, 2, new(40, 22), new(38, 31));
    Controls(space, Wad("FANTASY"));
    Console.WriteLine("PASS: Ada's exact route, RSSE guest identity, source-owned variables, unload backpressure and departure; SPACE terrain_1 and terrain_2");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }

static void Eq<T>(T actual, T expected, string label)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected)) throw new Exception($"{label}: expected {expected}, got {actual}");
}
static void Yes(bool value, string label) { if (!value) throw new Exception(label); }
static void Sequence<T>(IEnumerable<T> actual, IEnumerable<T> expected, string label) =>
    Eq(string.Join('|', actual), string.Join('|', expected), label);
static void RequireSource(WadArchive wad)
{
    string source = Encoding.ASCII.GetString(wad.Read(wad.Find("/Rides/orbiter/orbiter.rss")));
    source = string.Join('\n', source.Split('\n').Select(l => Regex.Replace(l.Split(';')[0], @"\s+", " ").Trim()));
    foreach (string s in new[] { "HUSH VAR_LETMEON", "COPY VAR_LETMEON 0", "ADD VAR_ONRIDE 1",
        "ADD VAR_SPACELEFT -1", "ADD VAR_STARTNOW 10000", "SUB VAR_TEMP VAR_STARTNOW VAR_TEMP",
        "BRANCH_NV run", "COPY VAR_RUNNING 1", "COPY VAR_COUNT VAR_DURATION", "ADD VAR_COUNT -1",
        "HOP VAR_LETMEOFF", "TEST VAR_LETMEOFF", "BRANCH_NZ wait2", "ADD VAR_ONRIDE -1" })
        Yes(source.Contains(s), "Orbiter source no longer supports: " + s);
}
static void Cycle(WadArchive wad, WadArchive data, int terrain, ParkCell origin, ParkCell entrance)
{
    var scenario = new VisitorScenario(wad, "SPACE", terrain); var sim = scenario.Simulation; var vm = sim.Machine;
    Eq(scenario.RideOrigin, origin, "named SPACE ride placement"); Eq(sim.Entrance, entrance, "named SPACE spawn cell");
    Eq(scenario.Definition.Id, (int?)3104, "Orbiter SAM identity"); Eq(vm["VAR_CAPACITY"], 10, "SAM capacity");
    var ada = sim.Visitors.Single(g => g.Id == 101); Eq(ada.Name, "Ada", "guest identity");
    foreach (var g in sim.Visitors)
    {
        var e = data.Find(g.ModelPath) ?? throw new Exception($"Missing {g.Name}'s model {g.ModelPath}");
        Eq(e.Path.ToLowerInvariant(), g.ModelPath.ToLowerInvariant(), "character path identity");
        var m = new Model(data.Read(e)); Yes(m.Meshes.Any(mesh => m.Triangles(mesh).Any()), g.Name + " has no drawable geometry");
    }
    var traces = new List<VisitorSimulation.Transition>();
    var heads = new List<(RseOpcode Op, int Id)>();
    sim.Host.EffectRequested += e => { if (e.Opcode is RseOpcode.ADDHEAD or RseOpcode.DELHEAD) heads.Add((e.Opcode, e.Arguments[0])); };
    sim.Changed += e =>
    {
        traces.Add(e);
        if (e.GuestId == 101 && e.Action != "step") Console.WriteLine($"SPACE/{terrain} {e}");
        if (e.Action == "board")
        {
            int seat = Array.IndexOf(new[] { 101, 202, 303, 404 }, e.GuestId) + 1;
            Eq(e.LetMeOn, 0, e.Name + " consumed mailbox"); Eq(e.OnRide, seat, e.Name + " script occupancy");
            Eq(e.SpaceLeft, 10 - seat, e.Name + " script capacity left"); Eq(e.StartNow, (int)e.Time + 10000, e.Name + " script deadline reset");
            Sequence(vm.GuestIds, new[] { 101, 202, 303, 404 }.Take(seat), e.Name + " HUSH identity");
        }
        if (e.Action == "unload requested")
        {
            Eq(e.LetMeOff, e.GuestId, e.Name + " HOP identity");
            Yes(!vm.GuestIds.Contains(e.GuestId), e.Name + " still on HUSH stack after HOP");
        }
    };
    int lastRunning = 0; var runningEdges = new List<(long, int)>();
    for (int t = 100; t <= 57000; t += 100)
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
        if (t == 7000) { Eq(ada.State, VisitorState.Queuing, "Ada reached queue"); Eq(ada.Cell, sim.QueueCells[^1], "Ada queue tail identity"); }
        if (t == 26000) { Eq(vm["VAR_RUNNING"], 0, "timeout equality must still wait"); Eq(vm["VAR_TEMP"], 0, "deadline minus time at equality"); }
        if (t == 26100) { Eq(vm["VAR_RUNNING"], 1, "strict negative timeout starts Orbiter"); Eq(vm["VAR_COUNT"], 1, "source copies duration"); }
        if (t is >= 34000 and < 35000) { Eq(vm["VAR_LETMEOFF"], 404, "Dee unload mailbox held"); Eq(vm["VAR_ONRIDE"], 4, "script waits for Dee to clear portal"); }
        if (t == 41000) { Eq(vm["VAR_ONRIDE"], 0, "script decremented after Ada ack"); Eq(vm["VAR_COUNT"], 0, "source completed one run"); }
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
    Sequence(runningEdges, new[] { (26100L, 1), (34000L, 0) }, "Orbiter source/APS running transitions");
    Sequence(traces.Where(e => e.Action == "board").Select(e => (e.GuestId, e.Time)),
        new[] { (101, 10000L), (202, 12000L), (303, 14000L), (404, 16000L) }, "FIFO boarding identities/times");
    Sequence(traces.Where(e => e.Action == "unload requested").Select(e => e.GuestId), new[] { 404, 303, 202, 101 }, "LIFO unloading identity");
    Sequence(heads, new[] { (RseOpcode.ADDHEAD, 101), (RseOpcode.ADDHEAD, 202), (RseOpcode.ADDHEAD, 303), (RseOpcode.ADDHEAD, 404),
        (RseOpcode.DELHEAD, 404), (RseOpcode.DELHEAD, 303), (RseOpcode.DELHEAD, 202), (RseOpcode.DELHEAD, 101) }, "script head effect identities");
    var head = sim.QueueCells[0];
    var inward = Enumerable.Range(1, 4).Select(x => entrance.Offset(x, 0))
        .Concat(Enumerable.Range(1, 6).Select(z => entrance.Offset(4, -z)));
    var outward = Enumerable.Range(2, 3).Select(x => head.Offset(x, 0))
        .Concat(Enumerable.Range(1, 6).Select(z => head.Offset(4, z)))
        .Concat(Enumerable.Range(1, 8).Select(x => head.Offset(4 - x, 6)));
    Sequence(traces.Where(e => e.GuestId == 101 && e.Action == "step").Select(e => e.Cell), inward.Concat(outward), "Ada full waypoint identity");
    Sequence(traces.Where(e => e.GuestId == 101 && e.Action != "step").Select(e => (e.Action, e.Time)),
        new[] { ("spawn", 100L), ("queue", 7000L), ("offer", 10000L), ("board", 10000L), ("unload requested", 39000L),
            ("unload acknowledged", 41000L), ("depart", 57000L) }, "Ada complete visit identity");
    foreach (var g in sim.Visitors) { Eq(g.State, VisitorState.Departed, g.Name + " departure"); Eq(g.Cell, entrance, g.Name + " left via park entrance"); }
    Eq(vm["VAR_ONRIDE"], 0, "empty ride"); Sequence(vm.GuestIds, Array.Empty<int>(), "no stranded guest IDs in script");
    Console.WriteLine($"SPACE/{terrain}: Ada walked the exact L route and exit loop; HUSH 101 -> HOP 101; RUNNING 26100..34000ms");
}
static void Controls(WadArchive space, WadArchive fantasy)
{
    var s = new VisitorScenario(space, "SPACE"); var p = s.Simulation.Paths;
    // These cells fooled the first grid-only placement. The renderer's entrance scenery owns
    // them even though byte0 permits tiles. A screenshot exposed this missing collision input.
    Yes(p.SceneryBlocks(new ParkCell(44, 28)), "SPACE entrance scenery intersection identity");
    Yes(!p.CanBuild(new ParkCell(44, 28)), "fixed entrance scenery must block construction");
    // The original field has ground materials, including the palette's zero sentinel, not a path.
    foreach (var c in p.Cells)
    {
        byte source = s.Terrain.Field.Raw0(c.X, c.Z);
        Eq(p.Field.Raw0(c.X, c.Z), source, "path placement preserves source flag byte");
        if (source != 0) Yes(!p.Walkable(c), $"nonzero terrain flags became walkable at {c}");
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
    s = new VisitorScenario(space, "SPACE"); s.AdvanceTo(5000); s.Simulation.SetRideOpen(false); s.AdvanceTo(18000);
    ada = s.Simulation.Visitors.Single(g => g.Id == 101);
    Eq(ada.State, VisitorState.Queuing, "closed ride keeps Ada queued"); Eq(ada.Cell, s.Simulation.QueueCells[0], "Ada queue front");
    Eq(ada.EdgeProgress, 0, "Ada stopped exactly at queue front"); Eq(ada.NextCell, null, "Ada has no pending edge");
    Sequence(s.Simulation.Queue, new[] { 101, 202, 303, 404 }, "closed FIFO identity");
    Eq(s.Simulation.Machine["VAR_LETMEON"], 0, "closed boarding mailbox"); Eq(s.Simulation.Machine["VAR_ONRIDE"], 0, "closed script occupancy");
    s.AdvanceTo(19000); Eq(ada.EdgeProgress, 0, "closed Ada exact motion floor after one second");
    s.Simulation.SetRideOpen(true); s.AdvanceTo(19500); Eq(ada.State, VisitorState.Riding, "reopened ride boards Ada");
    // Different caller cadence must preserve actual identities and integer state.
    var one = new VisitorScenario(space, "SPACE"); var split = new VisitorScenario(space, "SPACE");
    one.AdvanceTo(57000); foreach (long t in new long[] { 117, 877, 2301, 10049, 17111, 39873, 57000 }) split.AdvanceTo(t);
    Sequence(split.Simulation.Machine.Variables, one.Simulation.Machine.Variables, "caller cadence VM state");
    Sequence(split.Simulation.Visitors.Select(g => (g.Id, g.State, g.Cell, g.EdgeProgress)),
        one.Simulation.Visitors.Select(g => (g.Id, g.State, g.Cell, g.EdgeProgress)), "caller cadence guest identities");
    var arrival = new VisitorSimulation(new VisitorScenario(space, "SPACE").Simulation.Paths,
        s.Simulation.Machine.Program, s.RideAnimation, 10, s.Simulation.Entrance, s.Simulation.QueueCells, s.Simulation.ExitPortal);
    arrival.Schedule(808, "Later", "/Chars/Boy1a/boy1a.mps", 1500);
    arrival.Schedule(909, "First", "/Chars/Girl1a/girl1a.mps", 0);
    var order = new List<int>(); arrival.Changed += e => { if (e.Action == "board") order.Add(e.GuestId); };
    arrival.SetRideOpen(true); while (arrival.Time < 20000) arrival.Step();
    Sequence(order, new[] { 909, 808 }, "FIFO follows arrival, not registration or numeric ID");
    try { _ = new VisitorScenario(fantasy, "FANTASY"); throw new Exception("FANTASY should reject an unsupported terrain site"); }
    catch (InvalidOperationException ex) when (ex.Message == "No eligible site for the complete ride/path layout") { }
    Console.WriteLine("CONTROLS: broken route blocks Ada, closed queue holds exact cells, reopening works, caller cadence agrees; FANTASY site explicitly rejected");
}
