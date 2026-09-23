using System.Numerics;
using TPW.PS2.Data;

// ⭐⭐ GUESTS WALKING THE PARK, WITH NO ENGINE. Spawn a crowd at the walkway's mouth, send them
// to a handful of cells on a small path network, run a minute of ticks and count what happened
// -- as a console app, no Godot, no window, in about a second.
//
// ⚠⚠ THIS CENSUSES OUR ROUTING, NOT THE CONSOLE'S. GuestWalk says so in its own doc; the PS2's
// guest pathfinder has not been read. What this audit CAN establish is that the walking layer is
// self-consistent -- guests only ever stand on open ground, move at exactly the declared pace,
// take the shortest route, re-route round a hole and give up (rather than throw) when there is
// no way round -- and every check here has a partner that fails if the code were broken.
//
// ⚠ IT NEEDS THE OWNER'S DISC. Nothing is committed and nothing is cached.

if (args.Length < 1) { Console.Error.WriteLine("Usage: GuestAudit /path/to/disc.bin [WORLD]"); return 2; }
string world = args.Length > 1 ? args[1].ToUpperInvariant() : "JUNGLE";
int bad = 0;
void Check(bool ok, string line) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + line); if (!ok) bad++; }

using var disc = new Disc(args[0]);
WadArchive Wad(string name)
{
    var e = disc.Files().Single(f => f.Path.Equals($"/DATA/{name}.WAD", StringComparison.OrdinalIgnoreCase));
    return new WadArchive(disc.Read(e.Extent, e.Size));
}
var wad = Wad(world);
var terrain = new Model(wad.Read(wad.Find("/terrain/terrain_1.mps")));

// The entrance the park comes with, from the game's own table in the owner's executable. Without
// it there is no walkway and nowhere for a guest to come in by, so that is the end of the audit.
var exe = disc.Files().SingleOrDefault(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
if (exe == null) { Console.WriteLine("FAIL: no SLES_500.32 on this disc, so no entrance table and nowhere to spawn"); return 1; }
var table = ParkEntrance.ReadExecutable(disc.Read(exe.Extent, exe.Size));

// ⚠ SEVERAL GRIDS, NOT ONE. The controls below dig paths up, and a control that shares its grid
// with the census it is checking is no control.
string entranceReport = null;
ParkPaths Grid() { var p = new ParkPaths(terrain); entranceReport = p.SetEntrance(table); return p; }
var paths = Grid();
Console.WriteLine($"{world} terrain_1: {paths.Field.Width}x{paths.Field.Height}; entrance {entranceReport}");
Check(paths.EntranceCells.Count > 0, $"the park has an entrance to walk in by ({paths.EntranceCells.Count} cells)");

// ⭐ THE MOUTH: the two kind-0x0E cells where the walkway meets the park. That is where the game
// paints the walkway's last row, and it is where guests come in.
var entry = table.Fit(paths.Field, out _);
var mouth = entry.Cells().Where(c => c.Kind == 0x0E).Select(c => new ParkCell(c.X, c.Z)).ToArray();
Check(mouth.Length == 2 && mouth.All(paths.Open), $"the walkway has a two-cell mouth on open ground: {string.Join(" ", mouth)}");
if (mouth.Length != 2) { Console.WriteLine("FAIL: no mouth, nowhere to spawn"); return 1; }

// ⚠ THE PARK COMES WITH NO PATHS -- zero path-tiled cells in every shipped terrain (findings/
// paths.md) -- so this lays a small network of its own with the `jpa_squ1` tile (the art is
// wrong for a run and irrelevant here: ParkPaths classifies by NAME, and a squ is a path). Two
// columns straight in from the mouth, a bar across their end, and a spur down from each end of
// the bar: a loop-free tree whose only two-wide stretch is the corridor, which is the shape the
// cut controls need -- dig one column of it and there is a way round, dig both and there is none.
int xl = entry.XCol, xr = entry.XCol + 1, z0 = entry.ZEnd;   // the park begins one row past the mouth
const int Trunk = 8, Arm = 6, Spur = 4;
int bar = z0 + Trunk - 1;
var layout = new List<ParkCell>();
for (int z = z0; z < z0 + Trunk; z++) { layout.Add(new(xl, z)); layout.Add(new(xr, z)); }
for (int i = 1; i <= Arm; i++) { layout.Add(new(xl - i, bar)); layout.Add(new(xr + i, bar)); }
for (int i = 1; i <= Spur; i++) { layout.Add(new(xl - Arm, bar + i)); layout.Add(new(xr + Arm, bar + i)); }
int squ = paths.MaterialIndex("jpa_squ1.ssh");

// ⚠ EVERY REFUSAL IS PRINTED. Lay throws for ground that is unbuildable or under fixed scenery;
// skipping those quietly would leave a network with a gap in it and a census that blames the
// router for the gap.
List<ParkCell> LayNetwork(ParkPaths p)
{
    var refused = new List<ParkCell>();
    foreach (var c in layout)
    {
        try { p.Lay(c, squ); }
        catch (InvalidOperationException) { refused.Add(c); }
    }
    return refused;
}
var refused = LayNetwork(paths);
Console.WriteLine($"laid {layout.Count - refused.Count} of {layout.Count} path cells in from the mouth"
                + (refused.Count > 0 ? $"; REFUSED {refused.Count}: {string.Join(" ", refused)}" : ""));
Check(refused.Count == 0, "every cell of the layout could be laid (a refusal is unbuildable or scenery-covered ground)");
// ⚠ WHY, for each one, and WHICH MESH. A refusal is either the game's own no-build bit or
// ParkPaths' scenery projection, and those are different claims: the bit is read out of the
// loader, the projection is a conservative policy of ours. The coverage is rebuilt here with the
// same triangle test the constructor uses, so the mesh named is the one that actually did it.
if (refused.Count > 0)
{
    var transforms = terrain.WorldTransforms();
    foreach (var c in refused)
    {
        var covering = new List<string>();
        foreach (var mesh in terrain.Meshes)
        {
            var vertices = terrain.Vertices(mesh).Pos.Select(v => Vector3.Transform(v, transforms[mesh.Offset]))
                .Select(v => new Vector2(v.X - paths.Origin.X, v.Z - paths.Origin.Y)).ToArray();
            if (terrain.Triangles(mesh).Any(t => ParkPaths.TriangleCoversCell(vertices[t.A], vertices[t.B], vertices[t.C], c.X, c.Z)))
                covering.Add(mesh.Name);
        }
        Console.WriteLine($"  refused {c}: game no-build bit {(paths.Field.Buildable(c.X, c.Z) ? "clear" : "SET")}, "
                        + $"scenery projection {(paths.SceneryBlocks(c) ? "BLOCKS" : "clear")}"
                        + (covering.Count > 0 ? $", covered by {string.Join(", ", covering.Distinct())}" : ""));
    }
}
// ⭐ THE ONE THAT DECIDES EVERYTHING ELSE: if neither cell past the mouth takes a path, the gate
// is sealed and every "no route" below is about the gate, not the router.
bool sealed_ = refused.Contains(new ParkCell(xl, z0)) && refused.Contains(new ParkCell(xr, z0));
Check(!sealed_, $"the park can be entered: a cell past the mouth, {new ParkCell(xl, z0)} or {new ParkCell(xr, z0)}, takes a path");
if (sealed_) Console.WriteLine("  GATE SEALED under ParkPaths' rules: nothing in this park is reachable from the walkway, so every"
                             + " no-route and every idle control below is about the gate, not the router. Try another world.");

// Two control cells. ⚠ CONTROLS FIRST, because "everybody arrived" is also what a router that
// walks on grass would say. A guest sent to the grass beside the corridor must have no route;
// and a guest sent to a path tile laid ON ITS OWN, far from the network, must have no route
// EITHER -- that cell is open ground, and only the search's refusal to jump gaps keeps it out.
var grass = new ParkCell(xl - 1, z0 + 2);
ParkCell? island = null;
foreach (var c in paths.Cells.OrderByDescending(c => Math.Abs(c.X - xl) + Math.Abs(c.Z - z0)).ThenBy(c => c.Z).ThenBy(c => c.X))
    if (paths.CanBuild(c) && paths.Kind(c) == ParkPathKind.None && !paths.IsEntrance(c)
        && ParkPaths.Neighbours(c).All(n => !paths.Open(n))) { island = c; break; }
if (island is ParkCell isle) paths.Lay(isle, squ);
Check(paths.Contains(grass) && !paths.Open(grass), $"control: {grass} beside the corridor is not open ground");
Check(island is ParkCell i0 && paths.Open(i0), $"control: a lone path tile at {island} is open ground with no open neighbour");

ParkPaths Build()
{
    var p = Grid();
    LayNetwork(p);
    if (island is ParkCell isle) p.Lay(isle, squ);
    return p;
}

// The map, so the shape is on the record: W walkway, M mouth, # laid path, x refused,
// . buildable but bare, blank unbuildable or under scenery.
{
    int c0 = xl - Arm - 2, c1 = xr + Arm + 2, r0 = entry.ZRow, r1 = bar + Spur + 1;
    Console.WriteLine($"  map x {c0}..{c1}, z {r0}..{r1}:");
    for (int z = r0; z <= r1; z++)
    {
        var row = new System.Text.StringBuilder($"  z{z,3} ");
        for (int x = c0; x <= c1; x++)
        {
            var c = new ParkCell(x, z);
            row.Append(!paths.Contains(c) ? '?' : mouth.Contains(c) ? 'M' : paths.IsEntrance(c) ? 'W'
                     : paths.Open(c) ? '#' : refused.Contains(c) ? 'x' : paths.CanBuild(c) ? '.' : ' ');
        }
        Console.WriteLine(row.ToString());
    }
}

// The crowd: N guests at the mouth, round-robin over five cells -- the two spur tips, the two
// ends of the bar, and one part-way down the corridor -- plus the two control guests last.
const int N = 12;
var destinations = new[]
{
    new ParkCell(xl - Arm, bar + Spur), new ParkCell(xr + Arm, bar + Spur),
    new ParkCell(xl - Arm, bar), new ParkCell(xr + Arm, bar), new ParkCell(xr, z0 + 3),
};
GuestWalk Crowd(ParkPaths p)
{
    var w = new GuestWalk(p);
    for (int i = 0; i < N; i++) w.Spawn(mouth[i % 2], destinations[i % destinations.Length]);
    w.Spawn(mouth[0], grass);
    if (island is ParkCell isle) w.Spawn(mouth[1], isle);
    return w;
}
static int Manhattan(ParkCell a, ParkCell b) => Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z);
static bool Adjacent(ParkCell a, ParkCell b) => Manhattan(a, b) == 1;
static string Describe(Guest g) => $"guest {g.Id,2} {g.Cell} -> {g.Destination}: {g.State}"
    + (g.Route != null ? $", route {g.Route.Count - 1} cells" : "") + $", {g.Steps} steps"
    + (g.Reroutes > 0 ? $", re-routed x{g.Reroutes}" : "") + (g.Reason != null ? $" -- {g.Reason}" : "");

// ── The census ──────────────────────────────────────────────────────────────────────────────
const int Seconds = 60;
int ticks = (int)(Seconds * 1000 / GuestWalk.TickMilliseconds);
// ⭐ DERIVED, NOT COPIED: how many ticks a cell takes falls out of the two constants, so if the
// pace is ever changed the checks below move with it instead of going stale.
int ticksPerCell = (GuestWalk.UnitsPerCell + GuestWalk.UnitsPerTick - 1) / GuestWalk.UnitsPerTick;
Console.WriteLine($"pace: {GuestWalk.UnitsPerTick} of {GuestWalk.UnitsPerCell} units a tick, {ticksPerCell} ticks ({ticksPerCell * GuestWalk.TickMilliseconds} ms) a cell");

var walk = Crowd(paths);
var crowd = walk.Guests.Take(N).ToList();
var first = crowd.FirstOrDefault(g => g.State == GuestState.Walking && g.Route.Count >= 2);
Check(first != null, "at least one guest of the crowd has a route to walk");
var arrivedAt = new Dictionary<int, int>();
int violations = 0;
void Violation(string what) { if (violations++ < 6) Console.WriteLine("  VIOLATION " + what); }
for (int t = 1; t <= ticks; t++)
{
    walk.Step();
    // ⭐ THE ARITHMETIC, at two points a broken carry cannot fake: part-way along the first edge
    // the progress is exactly ticks x units, and at the first whole cell the guest is exactly on
    // the route's second cell with exactly the carry the division leaves.
    if (first != null && t == 10)
        Check(first.Cell == first.Route[0] && first.Next == first.Route[1] && first.Progress == 10 * GuestWalk.UnitsPerTick,
              $"after 10 ticks guest {first.Id} is {first.Progress} of {GuestWalk.UnitsPerCell} units along its first edge {first.Route[0]} -> {first.Route[1]}");
    if (first != null && t == ticksPerCell)
        Check(first.Cell == first.Route[1] && first.Progress == ticksPerCell * GuestWalk.UnitsPerTick - GuestWalk.UnitsPerCell,
              $"after {ticksPerCell} ticks it stands on {first.Cell}, the route's second cell, carry {first.Progress}");
    foreach (var g in walk.Guests)
    {
        if (g.State == GuestState.Arrived && !arrivedAt.ContainsKey(g.Id)) arrivedAt[g.Id] = t;
        // ⚠ THE INVARIANTS, every guest every tick: on open ground, stepping only to an open
        // neighbour, progress inside the edge, and the route agreeing about where it is.
        if (g.State == GuestState.Walking)
        {
            if (!paths.Open(g.Cell)) Violation($"t={t} guest {g.Id} stands on {g.Cell}, not open ground");
            if (g.Next is ParkCell n && (!paths.Open(n) || !Adjacent(g.Cell, n))) Violation($"t={t} guest {g.Id} steps {g.Cell} -> {n}");
            if (g.Progress < 0 || g.Progress >= GuestWalk.UnitsPerCell) Violation($"t={t} guest {g.Id} progress {g.Progress}");
            if (g.Route == null || g.RouteIndex >= g.Route.Count || g.Route[g.RouteIndex] != g.Cell) Violation($"t={t} guest {g.Id} route index {g.RouteIndex} does not say {g.Cell}");
        }
        else if (g.State == GuestState.Arrived && (g.Cell != g.Destination || g.Next != null || g.Progress != 0))
            Violation($"t={t} guest {g.Id} arrived but stands at {g.Cell} progress {g.Progress}");
    }
}
Check(violations == 0, $"{violations} invariant violations over {ticks} ticks");

int arrived = crowd.Count(g => g.State == GuestState.Arrived), noRoute = crowd.Count(g => g.State == GuestState.NoRoute);
int stranded = crowd.Count(g => g.State == GuestState.Stranded), walking = crowd.Count(g => g.State == GuestState.Walking);
var routed = crowd.Where(g => g.Route != null).ToList();
Console.WriteLine($"census after {Seconds}s ({ticks} ticks), {N} guests spawned at the mouth:");
Console.WriteLine($"  arrived {arrived}, no route {noRoute}, stranded {stranded}, still walking {walking}");
if (routed.Count > 0)
{
    var longest = routed.MaxBy(g => g.Route.Count);
    Console.WriteLine($"  longest route {longest.Route.Count - 1} cells (guest {longest.Id} to {longest.Destination}); "
                    + $"shortest {routed.Min(g => g.Route.Count) - 1}");
}
if (arrived > 0)
    Console.WriteLine($"  mean steps {crowd.Where(g => g.State == GuestState.Arrived).Average(g => g.Steps):F2}; "
                    + $"mean time to arrive {arrivedAt.Where(kv => kv.Key <= N).Average(kv => kv.Value) * GuestWalk.TickMilliseconds / 1000.0:F2}s; "
                    + $"last arrival {arrivedAt.Where(kv => kv.Key <= N).Max(kv => kv.Value) * GuestWalk.TickMilliseconds / 1000.0:F2}s");
foreach (var d in destinations)
{
    var here = crowd.Where(g => g.Destination == d).ToList();
    Console.WriteLine($"  -> {d}: {here.Count} guests, " + string.Join("; ", here.Select(g =>
        g.State == GuestState.Arrived ? $"{g.Id} arrived {g.Steps} steps at {arrivedAt[g.Id] * GuestWalk.TickMilliseconds}ms" : $"{g.Id} {g.State}")));
}
// ⚠ EVERY FAILURE BY NAME. A census that only counts them is where a real gap goes to hide.
foreach (var g in crowd.Where(g => g.State != GuestState.Arrived)) Console.WriteLine("  " + Describe(g));

Check(arrived > 0, $"{arrived} guests arrived");
Check(noRoute == 0, $"{noRoute} of the crowd could not be routed at all");
Check(stranded == 0 && walking == 0, $"in an undisturbed park nobody is stranded ({stranded}) or still walking after {Seconds}s ({walking})");
// ⭐ THE ROUTE LENGTH IS DERIVED FROM THE GEOMETRY, not read off the output: on this tree every
// destination lies down-and-across from the mouth with the corridor and bar between, so a
// shortest route is exactly the Manhattan distance. Longer means the search is not shortest;
// shorter means it walked on something that is not a path.
Check(routed.All(g => g.Route.Count - 1 == Manhattan(g.Route[0], g.Destination)),
      "every route is exactly as long as the Manhattan distance from the mouth to its destination");
Check(crowd.Where(g => g.State == GuestState.Arrived).All(g => g.Steps == g.Route.Count - 1
        && arrivedAt[g.Id] == (g.Steps * GuestWalk.UnitsPerCell + GuestWalk.UnitsPerTick - 1) / GuestWalk.UnitsPerTick),
      "every arrival took exactly its route's cells, at exactly the declared pace");

// The two controls in the same walk.
var grassGuest = walk.Guests[N];
Check(grassGuest.State == GuestState.NoRoute, "control: the guest sent to grass has no route -- " + Describe(grassGuest));
if (island != null)
{
    var islandGuest = walk.Guests[N + 1];
    Check(islandGuest.State == GuestState.NoRoute, "control: the guest sent to the lone tile has no route -- " + Describe(islandGuest));
}

// ── Dig up one column of the corridor: everyone re-routes round it ─────────────────────────
// ⚠ Un-laying is putting the terrain's own material byte back, the way the visitor audit's cut
// control does it; ParkPaths has no un-lay because the viewer's tool keeps its own kind bytes.
void Dig(ParkPaths p, ParkCell c) => p.Field.Cells[(c.Z * p.Field.Width + c.X) * 2 + 1] = (byte)terrain.Field.Material(c.X, c.Z);
int digAt = 3 * ticksPerCell;           // three cells in: everybody is on the corridor, short of the hole
var cut = new ParkCell(xl, z0 + 5);
{
    var p2 = Build(); var w2 = Crowd(p2);
    for (int t = 1; t <= digAt; t++) w2.Step();
    var through = w2.Guests.Take(N).Where(g => g.State == GuestState.Walking && g.Route.Skip(g.RouteIndex + 1).Contains(cut)).Select(g => g.Id).ToHashSet();
    var before = w2.Guests.Take(N).ToDictionary(g => g.Id, g => g.Route?.Count ?? 0);
    Check(w2.Guests.Take(N).All(g => g.Next != cut && g.Cell != cut), $"at the dig ({digAt} ticks) nobody is on or committed to {cut}");
    Dig(p2, cut);
    Check(!p2.Open(cut), $"control: {cut} dug up, no longer open");
    string threw = null; int trespass = 0;
    try
    {
        for (int t = digAt + 1; t <= ticks; t++)
        {
            w2.Step();
            foreach (var g in w2.Guests) if (g.Cell == cut || g.Next == cut) trespass++;
        }
    }
    catch (Exception e) { threw = e.Message; }
    var c2 = w2.Guests.Take(N).ToList();
    Console.WriteLine($"one column dug at {cut}: {through.Count} guests had it ahead of them; "
                    + $"{c2.Count(g => g.State == GuestState.Arrived)} arrived, {c2.Count(g => g.State == GuestState.Stranded)} stranded, "
                    + $"{c2.Count(g => g.Reroutes > 0)} re-routed");
    Check(threw == null, threw == null ? "digging under a walking crowd throws nothing" : "the walk THREW: " + threw);
    Check(through.Count > 0, "the hole lies on somebody's route (otherwise this control tests nothing)");
    Check(trespass == 0, $"nobody walked on the hole afterwards ({trespass} trespasses)");
    Check(c2.Where(g => through.Contains(g.Id)).All(g => g.State == GuestState.Arrived && g.Reroutes >= 1),
          "everyone who had the hole ahead re-routed and still arrived");
    Check(c2.Where(g => !through.Contains(g.Id)).All(g => g.State == GuestState.Arrived && g.Reroutes == 0),
          "everyone else arrived without re-routing");
    // ⭐ DERIVED BOUND: a way round one cell of a two-wide corridor costs at most two extra steps
    // (over and back), and never fewer than the route it replaced.
    Check(c2.Where(g => through.Contains(g.Id)).All(g => g.Steps >= before[g.Id] - 1 && g.Steps <= before[g.Id] + 1),
          "each detour cost between 0 and 2 extra steps: " + string.Join(", ", c2.Where(g => through.Contains(g.Id)).Select(g => $"{g.Id}: {before[g.Id] - 1}->{g.Steps}")));
    foreach (var g in c2.Where(g => g.State != GuestState.Arrived)) Console.WriteLine("  " + Describe(g));
}

// ── Dig up both columns: the far side is cut off and those guests give up, cleanly ─────────
{
    var p3 = Build(); var w3 = Crowd(p3);
    for (int t = 1; t <= digAt; t++) w3.Step();
    var cut2 = new ParkCell(xr, cut.Z);
    Dig(p3, cut); Dig(p3, cut2);
    string threw = null;
    try { for (int t = digAt + 1; t <= ticks; t++) w3.Step(); }
    catch (Exception e) { threw = e.Message; }
    var c3 = w3.Guests.Take(N).ToList();
    var far = c3.Where(g => g.Destination.Z > cut.Z).ToList();
    var near = c3.Where(g => g.Destination.Z <= cut.Z).ToList();
    Console.WriteLine($"both columns dug at row {cut.Z}: {far.Count} guests bound past it, {near.Count} short of it; "
                    + $"{c3.Count(g => g.State == GuestState.Arrived)} arrived, {c3.Count(g => g.State == GuestState.Stranded)} stranded, "
                    + $"{c3.Count(g => g.State == GuestState.Walking)} still walking");
    Check(threw == null, threw == null ? "cutting the crowd off throws nothing" : "the walk THREW: " + threw);
    Check(far.Count > 0 && near.Count > 0, "both sides of the cut have guests bound for them (otherwise this control tests nothing)");
    Check(far.All(g => g.State == GuestState.Stranded), "everyone bound past the cut gave up");
    Check(far.All(g => g.Next == null && g.Progress == 0 && p3.Open(g.Cell) && g.Cell.Z < cut.Z && g.Reason != null),
          "...standing still on open ground short of the hole, each with a reason");
    Check(near.All(g => g.State == GuestState.Arrived), "everyone bound short of the cut still arrived");
    foreach (var g in c3.Where(g => g.State != GuestState.Arrived)) Console.WriteLine("  " + Describe(g));
}

// ── Cadence: the same minute in ragged frames is the same minute ───────────────────────────
// ⭐ 17 + 33 + 100 + 250 ms is 400 ms, ten ticks a cycle with a remainder that has to be carried
// correctly for the clocks to agree at the end; 150 cycles is the census's minute.
{
    var p4 = Build(); var w4 = Crowd(p4);
    for (int i = 0; i < 150; i++) foreach (var dt in new[] { 0.017, 0.033, 0.1, 0.25 }) w4.Advance(dt);
    static string Snapshot(GuestWalk w) => string.Join("|", w.Guests.Select(g => $"{g.Id}:{g.State}:{g.Cell}:{g.Next}:{g.Progress}:{g.Steps}:{g.Reroutes}"));
    Check(w4.Time == walk.Time, $"ragged frames reach the same clock: {w4.Time}ms vs {walk.Time}ms");
    Check(Snapshot(w4) == Snapshot(walk), "ragged frames leave every guest in the same state as fixed ticks");
}

Console.WriteLine(bad == 0 ? "PASS" : $"FAIL: {bad}");
return bad == 0 ? 0 : 1;
