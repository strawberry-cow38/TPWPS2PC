using TPW.PS2.Data;

// ⭐⭐ THE PARK SIM, WITH NO ENGINE. This runs the rides and their scripts as a console app --
// no Godot, no window, no renderer -- which is the line the port holds: core/ decides, game/
// draws. If this passes, the game loop is right whatever the view does with it.
//
// ⚠ IT NEEDS THE OWNER'S DISC. Nothing is committed and nothing is cached.

if (args.Length < 1) { Console.Error.WriteLine("Usage: ParkSimAudit /path/to/disc.bin [WORLD]"); return 2; }
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
var paths = new ParkPaths(terrain);

// The entrance the park comes with, from the game's own table in the owner's executable.
var exe = disc.Files().SingleOrDefault(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
var entrance = exe == null ? null : ParkEntrance.ReadExecutable(disc.Read(exe.Extent, exe.Size));
Console.WriteLine($"{world} terrain_1: {paths.Field.Width}x{paths.Field.Height}; entrance {paths.SetEntrance(entrance)}");
Check(paths.EntranceCells.Count > 0, $"the park has an entrance to walk in by ({paths.EntranceCells.Count} cells)");

// ⚠⚠ RIDES, NOT EVERYTHING WITH A SCRIPT. The first twelve .rse files alphabetically are all
// /Features/ -- rocks and bushes -- whose scripts ask for an animation their model does not have,
// so the first run of this audit reported eleven of twelve "would not start" about scenery. A
// census has to census the thing it is about.
var sim = new ParkSim(paths);
int placed = 0, scriptless = 0, faulted = 0;
var faults = new Dictionary<string, int>(StringComparer.Ordinal);
int id = 0, col = 4;
foreach (var e in wad.Entries.Where(e => e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)
                                      && e.Path.StartsWith("/Rides/", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
{
    string stem = e.Path[..^4];
    var samEntry = wad.Find(stem + ".sam");
    var apsEntry = wad.Find(stem + ".aps");
    if (samEntry == null) continue;
    var def = RideDefinition.Parse(System.Text.Encoding.ASCII.GetString(wad.Read(samEntry)), stem + ".sam");
    var shape = def.Shape;
    int w = shape == null ? 1 : shape.Max(r => r.TrimEnd().Length), h = shape?.Length ?? 1;
    Animation aps = null;
    try { if (apsEntry != null) aps = new Animation(wad.Read(apsEntry)); } catch { }
    // ⭐ SPAWNCHILD LOOKS IN THE RIDE'S OWN FOLDER. `0x1be91c` builds the path as directory +
    // name, which matters here because seven different rides each ship a file called
    // EventMap.rse -- a lookup by name alone would hand six of them the wrong script.
    string dir = e.Path[..(e.Path.LastIndexOf('/') + 1)];
    byte[] Sibling(string child)
    {
        var c = wad.Entries.FirstOrDefault(x => x.Path.Equals(dir + child, StringComparison.OrdinalIgnoreCase));
        return c == null ? null : wad.Read(c);
    }
    var ride = sim.Add(++id, def.Name ?? stem, new ParkCell(col, 20), w, h,
                       wad.Read(e), aps, def.UpgradeCapacity(0) ?? 1, null, null, out string fault,
                       sibling: Sibling);
    col += w + 1;
    if (ride == null)
    {
        if (fault == "no script") scriptless++;
        else { faulted++; faults[Kind(fault)] = faults.GetValueOrDefault(Kind(fault)) + 1; }
    }
    else placed++;
}
Console.WriteLine($"rides with a script: {placed} started, {faulted} would not start, {scriptless} scriptless");
foreach (var (k, n) in faults.OrderByDescending(kv => kv.Value))
    Console.WriteLine($"  would not start x{n,-3} {k}");
Check(placed > 0, "at least one ride's script started");

// ⭐ The kind of a fault, not its instance: "Unimplemented RSSE instruction: 4: FINDSCRIPTRAND s+4
// v2" and the same opcode at another address are ONE thing to fix, and counting them separately
// turns a short list of missing opcodes into a long list of addresses.
static string Kind(string fault)
{
    if (fault == null) return "(none)";
    int i = fault.IndexOf("Unimplemented RSSE instruction:", StringComparison.Ordinal);
    if (i >= 0)
    {
        var rest = fault[(i + 31)..].Trim();
        int colon = rest.IndexOf(':');
        var name = colon >= 0 ? rest[(colon + 1)..].Trim() : rest;
        int sp = name.IndexOf(' ');
        return "unimplemented opcode " + (sp > 0 ? name[..sp] : name);
    }
    if (fault.Contains("APS has no animation", StringComparison.Ordinal)) return "script wants an animation its model lacks";
    return fault.Length > 60 ? fault[..60] : fault;
}

// ⭐ AND THEY MUST ACTUALLY RUN. A ride that never leaves the slot it started in is a statue with
// a script attached, which is exactly what the park had before this. ⚠ The partner is the SAME
// rides left CLOSED: they must NOT move, or "it animates" is just a clock ticking.
var shut = new Dictionary<int, HashSet<int>>();
foreach (var r in sim.Rides) shut[r.Id] = new HashSet<int> { r.Slot };
for (int i = 0; i < 250; i++) { sim.Advance(0.04); foreach (var r in sim.Rides) shut[r.Id].Add(r.Slot); }
int movedShut = shut.Count(kv => kv.Value.Count > 1);
Console.WriteLine($"closed for 10s: {movedShut} of {sim.Rides.Count} rides changed animation");

foreach (var r in sim.Rides) sim.SetOpen(r.Id, true);
var seen = new Dictionary<int, HashSet<int>>();
foreach (var r in sim.Rides) seen[r.Id] = new HashSet<int> { r.Slot };
for (int i = 0; i < 1500; i++) { sim.Advance(0.04); foreach (var r in sim.Rides) seen[r.Id].Add(r.Slot); }
int movedOpen = seen.Count(kv => kv.Value.Count > 1);
Console.WriteLine($"open for 60s:   {movedOpen} of {sim.Rides.Count} rides changed animation");
var live = sim.Rides.Where(r => r.Fault == null).ToList();
Console.WriteLine($"  {live.Count} of {sim.Rides.Count} still running after 60s");
foreach (var r in sim.Rides.Take(14))
    Console.WriteLine($"  {r.Name,-22} slots {string.Join(",", seen[r.Id].OrderBy(s => s))}"
                    + $"  now {r.Slot}:{r.Variant} frame {r.Frame:F1}"
                    + (r.Fault != null ? $"  FAULT {Kind(r.Fault)}" : ""));
var running = new Dictionary<string, int>(StringComparer.Ordinal);
foreach (var r in sim.Rides.Where(r => r.Fault != null))
    running[Kind(r.Fault)] = running.GetValueOrDefault(Kind(r.Fault)) + 1;
foreach (var (k, n) in running.OrderByDescending(kv => kv.Value))
    Console.WriteLine($"  stopped while running x{n,-3} {k}");

Check(movedOpen > 0, "an open ride works through its animation slots");
Check(movedOpen > movedShut, $"opening rides makes MORE of them move ({movedOpen} open vs {movedShut} closed)");
Check(sim.Time == 1750 * ParkSim.TickMilliseconds || sim.Time > 0, $"the sim's own clock advanced to {sim.Time}ms");
Check(live.Count > 0, $"{live.Count} rides ran the whole 60s without faulting");

// ⭐⭐ AND NOW SOMEBODY RIDES THEM. The sixty seconds above ran every ride open with NOBODY
// queuing, which makes it the control this section needs: VAR_ONRIDE is the script's own count,
// incremented by its own `ADD VAR_ONRIDE 1` as it boards, so if it has moved already then
// something is boarding guests that do not exist and every number below is worthless.
int ghosts = sim.Rides.Count(r => r.OnRide != 0);
Console.WriteLine($"\n60s open with an empty queue: {ghosts} rides claim riders, {sim.Rides.Sum(r => r.Left.Count)} claim leavers");
Check(ghosts == 0, "no ride boards a guest who does not exist");

int next = 1000;
var sent = new Dictionary<int, List<int>>();
foreach (var r in sim.Rides)
{
    sent[r.Id] = new List<int>();
    if (r.Fault != null) continue;
    for (int i = 0; i < 4; i++) { int g = next++; r.Join(g); sent[r.Id].Add(g); }
}
var peak = sim.Rides.ToDictionary(r => r.Id, _ => 0);
for (int i = 0; i < 4500; i++)
{
    sim.Advance(0.04);
    foreach (var r in sim.Rides) peak[r.Id] = Math.Max(peak[r.Id], r.OnRide);
}
var offered = sim.Rides.Where(r => sent[r.Id].Count > 0).ToList();
var boarded = offered.Where(r => peak[r.Id] > 0).ToList();
var returned = offered.Where(r => r.Left.Count > 0).ToList();
// ⚠ THE IDS, NOT THE COUNT. A ride that hands back four of SOMETHING is not a ride that hands
// back the four guests it was given, and only the second one means the guest went through.
var wrong = offered.Where(r => r.Left.Any(g => !sent[r.Id].Contains(g))).ToList();
Console.WriteLine($"4 guests queued at each of {offered.Count} running rides, 180s:");
Console.WriteLine($"  {boarded.Count} boarded at least one, {returned.Count} handed at least one back");
foreach (var r in offered.OrderByDescending(r => peak[r.Id]).Take(10))
    Console.WriteLine($"  {r.Name,-22} peak on ride {peak[r.Id]}  came back {r.Left.Count}/{sent[r.Id].Count}"
                    + $"  queue left {r.Queue.Count}  walks {(r.Machine.WalksAreTimed ? "timed" : "at the floor")}");
Check(boarded.Count > 0, $"a queued guest gets on a ride ({boarded.Count} rides boarded one)");
Check(returned.Count > 0, $"a ride gives its guests back ({returned.Count} rides did)");
Check(wrong.Count == 0, $"every guest handed back is one that was queued"
                      + (wrong.Count > 0 ? $" -- {wrong[0].Name} returned a stranger" : ""));

Console.WriteLine(bad == 0 ? "PASS" : $"FAIL: {bad}");
return bad == 0 ? 0 : 1;
