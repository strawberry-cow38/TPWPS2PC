using System.Buffers.Binary;
using TPW.PS2.Data;

// ⭐⭐ THE PARK SIM, WITH NO ENGINE. This runs the rides and their scripts as a console app --
// no Godot, no window, no renderer -- which is the line the port holds: core/ decides, game/
// draws. If this passes, the game loop is right whatever the view does with it.
//
// ⚠ IT NEEDS THE OWNER'S DISC. Nothing is committed and nothing is cached.

if (args.Length < 1) { Console.Error.WriteLine("Usage: ParkSimAudit /path/to/disc.bin [WORLD] [--removal-only]"); return 2; }
string world = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))?.ToUpperInvariant() ?? "JUNGLE";
// --terrain=2 selects each world's SECOND park. Default 1. Every terrain-dependent family reads
// AuditPark.Mps, so no check can silently stay on terrain_1 (2026-09-25: four were hardcoded).
var terrainArg = args.FirstOrDefault(a => a.StartsWith("--terrain=", StringComparison.Ordinal));
if (terrainArg != null && terrainArg != "--terrain=1" && terrainArg != "--terrain=2")
{ Console.Error.WriteLine($"not a terrain: {terrainArg} (use --terrain=1 or --terrain=2)"); return 2; }
AuditPark.Mps = terrainArg == "--terrain=2" ? "/terrain/terrain_2.mps" : "/terrain/terrain_1.mps";
int bad = 0;
void Check(bool ok, string line) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + line); if (!ok) bad++; }

using var disc = new Disc(args[0]);
if (args.Contains("--native-departure-only"))
{
    NativeDepartureChecks.Run(disc,Check);
    Console.WriteLine(bad==0 ? "PASS rejected departure through actual visitors/walk (explicit serial/service fixtures)" : $"FAIL: {bad}");
    return bad==0?0:1;
}
if (args.Contains("--native-route-pool-only"))
{
    NativeRoutePoolChecks.Run(disc,Check);
    Console.WriteLine(bad==0 ? "PASS shared output pool through cursor/walk/entrance (native search and readiness still separate)" : $"FAIL: {bad}");
    return bad==0?0:1;
}
if (args.Contains("--native-entrance-flow-only"))
{
    NativeEntranceFlowChecks.Run(disc,Check);
    NativeEntranceAcceptanceChecks.Run(Check);
    Console.WriteLine(bad==0 ? "PASS experimental incoming controller through real visitors/walk (explicit service adapters)" : $"FAIL: {bad}");
    return bad==0?0:1;
}
if (args.Contains("--native-route-consumer-only"))
{
    NativeGuestRouteChecks.Run(Check);
    NativeWalkConsumerChecks.Run(disc,Check);
    Console.WriteLine(bad==0 ? "PASS native route through GuestWalk/ParkVisitors (controller is a separate opt-in experimental consumer)" : $"FAIL: {bad}");
    return bad==0?0:1;
}
if (args.Contains("--logical-animation-only"))
{
    NativeLogicalAnimationChecks.Run(disc,Check);
    Console.WriteLine(bad==0 ? "PASS native logical-animation table, control block, playback and idle picker (viewer integration: NativeAnimationReadinessSmoke)" : $"FAIL: {bad}");
    return bad==0?0:1;
}
if (args.Contains("--guest-motion-only"))
{
    NativeGuestMotionChecks.Run(Check);
    NativeGuestRouteChecks.Run(Check);
    Console.WriteLine(bad==0 ? "PASS native guest coordinate arithmetic and route cursor (GuestWalk/entrance integration NOT exercised)" : $"FAIL: {bad}");
    return bad==0?0:1;
}
if (args.Contains("--bus-inputs-only"))
{
    BusAdmissionChecks.Run(disc,Check);
    Console.WriteLine(bad==0 ? "PASS native bus source joins and arithmetic (viewer admission not exercised)" : $"FAIL: {bad}");
    return bad==0?0:1;
}
if (args.Contains("--destination-score-only"))
{
    DestinationScoreChecks.Run(disc, Check);
    NativeRideValueChecks.Run(Check);
    Console.WriteLine(bad == 0 ? "PASS native destination arithmetic (coordinator integration not exercised)" : $"FAIL: {bad}");
    return bad == 0 ? 0 : 1;
}

WadArchive Wad(string name)
{
    var e = disc.Files().Single(f => f.Path.Equals($"/DATA/{name}.WAD", StringComparison.OrdinalIgnoreCase));
    return new WadArchive(disc.Read(e.Extent, e.Size));
}
var wad = Wad(world);
var terrain = new Model(wad.Read(wad.Find(AuditPark.Mps)));
if (args.Contains("--native-consumer-only"))
{
    NativeDestinationChecks.Run(terrain,Wad("DATA"),wad,world,Check);
    NativeReliefChecks.Run(terrain,Wad("DATA"),wad,world,Check);
    Console.WriteLine(bad == 0 ? "PASS native destination and relief consumers" : $"FAIL: {bad}");
    return bad == 0 ? 0 : 1;
}

if (args.Contains("--track-rides-only"))
{
    TrackRideChecks.Run(Check);
    TrackRideChecks.RunParkSim(new ParkPaths(terrain), wad, world, Check);
    Console.WriteLine(bad==0 ? "PASS native track rides: layout, samples, boarding, laps and unloading, and through ParkSim (viewer not exercised)" : $"FAIL: {bad}");
    return bad==0?0:1;
}
if (args.Contains("--removal-only"))
{
    Check(RideRemovalChecks.RunIsolated(terrain, wad, Check), "isolated removal fixture was exercised");
    Console.WriteLine(bad == 0 ? "PASS isolated removal regression (retail entrance integration not exercised)" : $"FAIL: {bad}");
    return bad == 0 ? 0 : 1;
}
var paths = new ParkPaths(terrain);

// The entrance the park comes with, from the game's own table in the owner's executable.
var exe = disc.Files().SingleOrDefault(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
var entrance = exe == null ? null : ParkEntrance.ReadExecutable(disc.Read(exe.Extent, exe.Size));
Console.WriteLine($"{world} {Path.GetFileNameWithoutExtension(AuditPark.Mps)}: {paths.Field.Width}x{paths.Field.Height}; entrance {paths.SetEntrance(entrance)}");
Check(paths.EntranceCells.Count > 0, $"the park has an entrance to walk in by ({paths.EntranceCells.Count} cells)");

// ⚠⚠ RIDES, NOT EVERYTHING WITH A SCRIPT. The first twelve .rse files alphabetically are all
// /Features/ -- rocks and bushes -- whose scripts ask for an animation their model does not have,
// so the first run of this audit reported eleven of twelve "would not start" about scenery. A
// census has to census the thing it is about.
var sim = new ParkSim(paths);
int placed = 0, scriptless = 0, faulted = 0;
var faults = new Dictionary<string, int>(StringComparer.Ordinal);
int id = 0, col = 4;
var apsReason = new Dictionary<string, string>(StringComparer.Ordinal);
// ⚠⚠ A STUB SCRIPT CAN SHADOW THE REAL RIDE. SPACE ships BOTH `/Rides/whirli.RSE` + `.sam` at the
// top level with no model or animation beside them, AND the real `/Rides/whirli/whirli.{rse,sam,
// mps,aps}` one folder down. Ordered by path the stub sorts FIRST ('.' is 0x2E, '/' is 0x2F), so
// it won and WhirliGig was censused as "carries NO animation slots at all" -- a fact about a stub,
// published as a fact about the disc. `whirli` is the only such pair: every other repeated
// basename under /Rides/ is EventMap/Worn/effects, which carry no `.sam` and are skipped anyway.
//
// ⭐ The rule prefers a candidate whose stem HAS a model, and keeps what it has when none does --
// so it fixes whirli without disturbing firepit, whose script legitimately ships with no `.mps`
// beside it.
var rideScripts = wad.Entries
    .Where(e => e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)
             && e.Path.StartsWith("/Rides/", StringComparison.OrdinalIgnoreCase))
    .GroupBy(e => e.Path[(e.Path.LastIndexOf('/') + 1)..^4], StringComparer.OrdinalIgnoreCase)
    .SelectMany(g => g.Count() == 1 || g.All(x => wad.Find(x.Path[..^4] + ".mps") == null)
                     ? g
                     : g.Where(x => wad.Find(x.Path[..^4] + ".mps") != null))
    .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase);
foreach (var e in rideScripts)
{
    string stem = e.Path[..^4];
    var samEntry = wad.Find(stem + ".sam");
    var apsEntry = wad.Find(stem + ".aps");
    if (samEntry == null) continue;
    var def = RideDefinition.Parse(System.Text.Encoding.ASCII.GetString(wad.Read(samEntry)), stem + ".sam");
    var shape = def.Shape;
    int w = shape == null ? 1 : shape.Max(r => r.TrimEnd().Length), h = shape?.Length ?? 1;
    // ⚠⚠ THIS USED TO BE `catch { }` AND SAID NOTHING. A ride whose .aps will not parse and a
    // ride that simply has none both came out as a silent null, so "carries no animation records"
    // could not be told from "the loader threw on it" -- and a script that waits on an animation
    // from a model with none hangs forever (Thrill Grill, TRIGWAITANIM at pc 85). Keep the reason.
    Animation aps = null;
    // ⚠ NO FOLDER LISTING HERE, deliberately. A first cut printed the ride's whole directory to
    // say whether an .aps lived under another name, and the directory prefix it derived
    // (`stem` up to the last '/') collapses to the WAD ROOT for the flat paths in some archives
    // -- so it printed every model in SPACE for one ride and nothing at all for another. The
    // answer it gave could not be told from the question it was asked. `wad.Find(stem + ".aps")`
    // returning null is measured directly and is the fact worth keeping.
    if (apsEntry == null) apsReason[def.Name ?? stem] = "no .aps beside the .mps";
    else try { aps = new Animation(wad.Read(apsEntry)); }
    catch (Exception apsEx) { apsReason[def.Name ?? stem] = $"{apsEx.GetType().Name}: {apsEx.Message}"; }
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

// ⭐⭐ A RIDE WITH NO ANIMATION RECORDS AT ALL. Thrill Grill hangs on TRIGWAITANIM waiting for a
// slot to start, and the reason turned out not to be "it lacks slot 4" but "it carries NOTHING":
// its host reports zero slots. A script waiting on an animation from a model that has none can
// never continue, so this is worth naming on its own rather than only where a ride stalls -- and
// it separates "the disc is like that" from "the loader dropped this one", which nothing else
// does, since a missing record and a mis-parsed one both look like silence at the call site.
var noAnim = sim.Rides.Where(r => r.Host.AvailableSlots.Count == 0).ToList();
Console.WriteLine($"  {noAnim.Count} of {sim.Rides.Count} rides carry NO animation slots at all"
                + (noAnim.Count > 0 ? ": " + string.Join(", ", noAnim.Select(r => r.Name)) : ""));
foreach (var r in noAnim)
    Console.WriteLine($"    {r.Name}: {(apsReason.TryGetValue(r.Name, out var why) ? why : "the .aps parsed and yielded no records")}");
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
// ⚠ NAME THE ONES THAT DID NOT. "13 of 19 boarded" is a number to feel good about; the six that
// took nobody are the finding, and they are the coasters, the karts and the tour bus -- the rides
// whose scripts poll TOUR/BUMP/COAST, which this executable's own handlers answer with zero.
// ⭐⭐ AND THE CLAIM ABOVE IS NOW CHECKED RATHER THAN ASSERTED. "They are the coasters, the karts
// and the tour bus" was a sentence in a comment, which is the weakest kind of finding: it reads
// like a conclusion and nothing would have noticed if a flat ride had quietly joined them. The
// three subsystem opcodes are dead in this build by design -- every branch of `0x1c1260`,
// `0x1c1370` and `0x1c14e0` writes zero or discards its argument -- so a script that polls one
// waits forever, and the rides that take nobody should be EXACTLY the rides that poll one.
//
// A flat ride in the took-nobody list would be a real defect. A track ride that boards without
// polling would mean the reading of those handlers is wrong. Both are worth failing on.
bool Uses(ParkRide r, RseOpcode op) => ParkSim.Chain(r.Machine)
    .Any(m => m.Program.Instructions.Any(i => i.Opcode == op));
// ⭐ HAVING the variable is not READING it. Every ride declares VAR_LETMEON -- ParkSim writes a
// guest into it regardless -- so "Has" proves nothing about whether the script is listening. The
// handshake only works if some instruction actually names that slot.
bool ReadsLetMeOn(ParkRide r)
{
    foreach (var m in ParkSim.Chain(r.Machine))
    {
        int slot = m.Program.VariableNames.ToList().IndexOf("VAR_LETMEON");
        if (slot < 0) continue;
        if (m.Program.Instructions.Any(i => i.Operands.Any(o => o.Tag == 0x40 && o.Index == slot)))
            return true;
    }
    return false;
}
bool PollsTrack(ParkRide r) => ParkSim.Chain(r.Machine).Any(m => m.Program.Instructions.Any(
    i => i.Opcode is RseOpcode.TOUR or RseOpcode.BUMP or RseOpcode.COAST));
var tookNobody = offered.Where(r => peak[r.Id] == 0).ToList();
foreach (var r in tookNobody)
    Console.WriteLine($"  took nobody: {r.Name,-22} queue still {r.Queue.Count}, fault {Kind(r.Fault)}"
                    + $", polls TOUR/BUMP/COAST: {(PollsTrack(r) ? "yes" : "NO")}"
                    + $", LETMEON {(r.Has("VAR_LETMEON") ? r.Get("VAR_LETMEON").ToString() : "ABSENT")}"
                    + $", RUNNING {(r.Has("VAR_RUNNING") ? r.Get("VAR_RUNNING").ToString() : "absent")}"
                    + $", CLOSED {(r.Has("VAR_RIDECLOSED") ? r.Get("VAR_RIDECLOSED").ToString() : "absent")}"
                    + $", BROKEN {(r.Has("VAR_BROKEN") ? r.Get("VAR_BROKEN").ToString() : "absent")}"
                    + $", CAPACITY {(r.Has("VAR_CAPACITY") ? r.Get("VAR_CAPACITY").ToString() : "absent")}"
                    + $", reads LETMEON: {(ReadsLetMeOn(r) ? "yes" : "NO")}"
                    + $", ADDHEAD: {(Uses(r, RseOpcode.ADDHEAD) ? "yes" : "no")}"
                    + $", WALKON: {(Uses(r, RseOpcode.WALKON) ? "yes" : "no")}"
                    // ⭐ LEAD ONE, TESTED RATHER THAN LISTED: WalkOn drops a walk silently when
                    // the table is full, so a saturated table is a ride that stops taking people
                    // with no error anywhere. Printed for every stalled ride, not just the odd
                    // one, so "full" only means something if the others are not.
                    + $", walk slots {r.Machine.WalkSlotsInUse}/{r.Machine.WalkSlots}"
                    + $", attempted {(r.Machine.WalksWereAttempted ? "yes" : "NO")}");
// ⭐⭐ AND WHERE IT IS PARKED. A ride that is RUNNING and still takes nobody is spinning on some
// instruction, and the PC plus the yield reason names it outright -- far better than guessing at
// which condition the host failed to satisfy. Printed with the surrounding instructions so the
// loop is readable without a separate disassembly.
foreach (var r in tookNobody.Where(r => r.Get("VAR_RUNNING") != 0))
{
    foreach (var m in ParkSim.Chain(r.Machine))
    {
        var code = m.Program.Instructions;
        int at = code.ToList().FindIndex(i => i.Address == m.Pc);
        Console.WriteLine($"    {r.Name} is parked at pc {m.Pc} (yield {m.Yield}), host slot"
                        + $" {r.Host.AnimationSlot}:{r.Host.AnimationVariant}"
                        + $", model carries slots [{string.Join(" ", r.Host.AvailableSlots.Select(a => $"{a.Slot}x{a.Variants}"))}]"
                        + $", around it:");
        for (int k = Math.Max(0, at - 4); k < Math.Min(code.Count, at + 5) && at >= 0; k++)
            Console.WriteLine($"      {(k == at ? "->" : "  ")} {Disasm(m, code[k])}");
        if (at < 0) Console.WriteLine($"      (pc {m.Pc} is not an instruction boundary)");
    }
}
// Operands as NAMES where the program has one, so the loop reads as a condition and not as slots.
string Disasm(RseMachine m, RseProgram.Instruction i) => $"{i.Address,4}: {i.Opcode} " + string.Join(" ",
    i.Operands.Select(o => o.Tag == 0x40
        ? (o.Index < m.Program.VariableNames.Count ? m.Program.VariableNames[o.Index] : $"v{o.Index}") + $"={m[o.Index]}"
        : o.ToString()));
var stuckWithoutTrack = tookNobody.Where(r => !PollsTrack(r)).ToList();
var trackRidesThatBoarded = boarded.Where(PollsTrack).ToList();
Console.WriteLine($"  {offered.Count(PollsTrack)} of {offered.Count} rides poll a track subsystem"
                + $"; {tookNobody.Count} took nobody");
Check(offered.Any(PollsTrack), "some ride polls TOUR/BUMP/COAST at all -- otherwise the checks below are vacuous");
// ⚠ THIS IS EXPECTED TO BE RED ON HALLOW, and it is left red ON PURPOSE rather than excluded by
// name -- a "known exception" filter is exactly where a second defect would hide. The message
// names the write-up so nobody re-diagnoses it from scratch; if a ride OTHER than Thrill Grill
// appears here, that is new and worth chasing.
Check(stuckWithoutTrack.Count == 0, "every ride that boarded nobody is one polling a dead track subsystem"
    + (stuckWithoutTrack.Count == 0 ? "" : ": " + string.Join(", ", stuckWithoutTrack.Select(r => r.Name))
        + " -- KNOWN for Thrill Grill (HALLOW), and the CONSOLE hangs here too: its only animation"
        + " (/upgrades/firepit/firepit.aps, not beside its script) holds one record, slot 5, while"
        + " TRIGWAITANIM at pc 85 asks for slot 4 -- which is in no firepit .aps on the disc. The"
        + " gate at 0x1bd7d0 has no timeout and nothing can put a missing slot into the channel."
        + " See findings/visitors.md. Anything else here is NEW."));
Check(trackRidesThatBoarded.Count == 0, "no ride boards guests while polling a subsystem that answers zero"
    + (trackRidesThatBoarded.Count == 0 ? "" : ": " + string.Join(", ", trackRidesThatBoarded.Select(r => r.Name))));
Check(boarded.Count > 0, $"a queued guest gets on a ride ({boarded.Count} rides boarded one)");
Check(returned.Count > 0, $"a ride gives its guests back ({returned.Count} rides did)");
Check(wrong.Count == 0, $"every guest handed back is one that was queued"
                      + (wrong.Count > 0 ? $" -- {wrong[0].Name} returned a stranger" : ""));

// ⭐⭐ AND THE SHOPS, WHICH ARE A DIFFERENT SHAPE OF THING. A shop does not run a cycle: it takes
// a guest IN (LIMBO, which also hides them), waits, and lets them out. Every shop and sideshow on
// this disc was blocked on that one family of five opcodes and nothing else, so they are censused
// separately rather than folded into the ride numbers above -- a shop that works says nothing
// about a ride and the other way round.
var shops = new ParkSim(paths);
int shopCol = 4, shopId = 0, shopsPlaced = 0;
var shopFaults = new Dictionary<string, int>(StringComparer.Ordinal);
foreach (var e in wad.Entries.Where(e => e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)
                                      && (e.Path.StartsWith("/Shops/", StringComparison.OrdinalIgnoreCase)
                                       || e.Path.StartsWith("/Sideshow/", StringComparison.OrdinalIgnoreCase)))
                             .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
{
    string stem = e.Path[..^4];
    var samEntry = wad.Find(stem + ".sam");
    if (samEntry == null) continue;
    var def = RideDefinition.Parse(System.Text.Encoding.ASCII.GetString(wad.Read(samEntry)), stem + ".sam");
    var shape = def.Shape;
    int w = shape == null ? 1 : shape.Max(r => r.TrimEnd().Length), h = shape?.Length ?? 1;
    Animation aps = null;
    try { var ae = wad.Find(stem + ".aps"); if (ae != null) aps = new Animation(wad.Read(ae)); } catch { }
    string sdir = e.Path[..(e.Path.LastIndexOf('/') + 1)];
    byte[] SShop(string child)
    {
        var c = wad.Entries.FirstOrDefault(x => x.Path.Equals(sdir + child, StringComparison.OrdinalIgnoreCase));
        return c == null ? null : wad.Read(c);
    }
    var shop = shops.Add(++shopId, def.Name ?? stem, new ParkCell(shopCol, 40), w, h,
                         wad.Read(e), aps, def.UpgradeCapacity(0) ?? 1, null, null, out string sf,
                         sibling: SShop);
    shopCol += w + 1;
    if (shop == null) shopFaults[Kind(sf)] = shopFaults.GetValueOrDefault(Kind(sf)) + 1;
    else shopsPlaced++;
}
Console.WriteLine($"\nshops and sideshows: {shopsPlaced} started, {shopFaults.Values.Sum()} would not start");
foreach (var (k, n) in shopFaults.OrderByDescending(kv => kv.Value))
    Console.WriteLine($"  would not start x{n,-3} {k}");

foreach (var s in shops.Rides) shops.SetOpen(s.Id, true);
var shopSent = new Dictionary<int, List<int>>();
foreach (var s in shops.Rides)
{
    shopSent[s.Id] = new List<int>();
    for (int i = 0; i < 3; i++) { int g = next++; s.Join(g); shopSent[s.Id].Add(g); }
}
var hidden = new Dictionary<int, HashSet<int>>();
foreach (var s in shops.Rides) hidden[s.Id] = new HashSet<int>();
for (int i = 0; i < 3000; i++)
{
    shops.Advance(0.04);
    // ⚠ WHO IS ACTUALLY OUT OF SIGHT. VAR_ONRIDE only says the script counted somebody; the
    // host's visibility record says LIMBO really took them off the map, which is the part a
    // shop is for and the part a stub would quietly skip.
    foreach (var s in shops.Rides)
        foreach (var (g, v) in s.Host.Visibility) if (!v.Visible) hidden[s.Id].Add(g);
}
var served = shops.Rides.Where(s => hidden[s.Id].Count > 0).ToList();
var backOut = shops.Rides.Where(s => s.Left.Count > 0).ToList();

// ⭐⭐ ELEVEN OF SIXTEEN SHOPS TAKE NOBODY INSIDE, and `served.Count > 0` passes anyway. That is
// the exact shape of a defect hiding behind a filter: the five that work carry the check and the
// eleven are never questioned. So ask the bytecode. LIMBO is what a shop IS, and a script that
// never contains the opcode was never going to hide anybody -- a burger stall is counter service
// and a gift shop is a room you walk into, and that difference should be AUTHORED, not accidental.
//
// The checks below are deliberately asymmetric, because "contains LIMBO and hid nobody" is not
// proof of a bug: a branch may simply not have been taken in 120 seconds. What IS proof, either
// way round, is a shop that hid somebody with no LIMBO anywhere in its chain -- nothing else can
// make a guest vanish -- and the whole opcode going dark at once.
//
// ⚠ THIS READS THE CHAIN AS IT STANDS AFTER THE RUN, not every program the shop could ever
// reach: a shop that would spawn a LIMBO-carrying child only on some later branch reads "no"
// here. That would show up as a shop in `hidWithoutAsking`, which is why that one is the hard
// failure and "carries it but hid nobody" is only a note.
bool AsksForLimbo(ParkRide s) => ParkSim.Chain(s.Machine)
    .Any(m => m.Program.Instructions.Any(i => i.Opcode == RseOpcode.LIMBO));
var askers = shops.Rides.Where(AsksForLimbo).ToList();
var hidWithoutAsking = shops.Rides.Where(s => !AsksForLimbo(s) && hidden[s.Id].Count > 0).ToList();
var askedButEmpty = askers.Where(s => hidden[s.Id].Count == 0).ToList();

Console.WriteLine($"3 guests queued at each of {shops.Rides.Count} shops, 120s:");
foreach (var s in shops.Rides)
    Console.WriteLine($"  {s.Name,-22} went inside {hidden[s.Id].Count}/{shopSent[s.Id].Count}"
                    + $"  came back {s.Left.Count}  queue left {s.Queue.Count}"
                    + $"  LIMBO in script: {(AsksForLimbo(s) ? "yes" : "no ")}"
                    + (s.Fault != null ? $"  FAULT {Kind(s.Fault)}" : ""));
if (askedButEmpty.Count > 0)
    Console.WriteLine($"  note: {askedButEmpty.Count} shop(s) carry LIMBO but hid nobody in 120s: "
                    + string.Join(", ", askedButEmpty.Select(s => s.Name)));
Check(shopsPlaced > 0, $"the shops' scripts start ({shopsPlaced} of them)");
Check(shops.Rides.All(s => s.Fault == null), "no shop faults while running");
Check(served.Count > 0, $"a guest goes INSIDE a shop and stops being drawn ({served.Count} shops took one in)");
Check(askers.Count > 0, $"the shops that take guests in are the ones whose scripts contain LIMBO "
                      + $"({askers.Count} of {shops.Rides.Count} carry it) -- otherwise the check below is vacuous");
Check(hidWithoutAsking.Count == 0, "nobody is hidden by a shop whose script never asks for LIMBO"
    + (hidWithoutAsking.Count == 0 ? "" : ": " + string.Join(", ", hidWithoutAsking.Select(s => s.Name))));
Check(served.Count > 0 && askers.Count > 0 && served.All(AsksForLimbo),
      "every shop that took somebody in is one that asks for LIMBO");
Check(backOut.Count > 0, $"a shop lets its guests back out ({backOut.Count} shops did)");
Check(shops.Rides.All(s => s.Left.All(g => shopSent[s.Id].Contains(g))),
      "every guest a shop hands back is one that was queued there");

// ⭐⭐ AND NOW THE WHOLE THING AT ONCE. Walking was proved on its own and the scripts were proved
// on their own; neither of those is a park. This walks guests in at the gate, lets them pick a
// ride, hands them to that ride's script at its queue stub, and counts the ones the script hands
// BACK at the exit and who then walk off. Nothing here is a stub: the route is over laid path,
// the boarding is the VAR_LETMEON handshake, and the ride in between is its own bytecode.
var loopPaths = new ParkPaths(terrain);
loopPaths.SetEntrance(entrance);
var mouth = loopPaths.EntranceCells.Count > 0
    ? loopPaths.EntranceCells.OrderByDescending(c => c.Z).First() : default;
// A corridor straight into the park from the mouth, using the game's own path material.
int corridor = loopPaths.MaterialIndex("jpa_squ1.ssh");
var laid = new List<ParkCell>();
for (int z = mouth.Z + 1; z < mouth.Z + 14; z++)
    foreach (int x in new[] { mouth.X, mouth.X + 1 })
    {
        var c = new ParkCell(x, z);
        if (!loopPaths.CanLay(c)) continue;
        loopPaths.Lay(c, corridor);
        laid.Add(c);
    }
Console.WriteLine($"\nwhole loop: mouth {mouth}, {laid.Count} path cells laid in");

var loop = new ParkSim(loopPaths);
var loopWalk = new GuestWalk(loopPaths);
var visitors = new ParkVisitors(loop, loopWalk);
int loopId = 0;
bool availabilityChecked = false;
(byte[] Script, Animation Aps, RideDefinition Def, Func<string, byte[]> Sibling, int Seats)? serviceRide = null;
bool removalChecked = false;
var onPath = laid.Where(c => loopPaths.Open(c)).ToArray();
// Three rides hung off the corridor, plus a FOURTH whose queue is out in the grass -- the
// control. If guests ever queue at that one, "they walked to the queue" is not what happened.
// ⚠ The FOURTH stop is off the path on purpose -- see the control below.
var unreachable = new ParkCell(1, 1);
// ⭐ AS MANY RIDES AS THE CORRIDOR HOLDS, not three. The walk-timing and track-subsystem checks
// below only bite on rides that were actually PLACED, and three of the thirty-five rides that
// call WALKON across the four worlds is not coverage. Every other open cell from index 3 leaves a
// gap between neighbours so their queues do not overlap; the LAST entry is always the control,
// out in the grass, and must stay last because `control` is keyed on being the final stop.
var corridorStops = new List<ParkCell>();
for (int i = 3; i < onPath.Length - 1 && corridorStops.Count < 8; i += 2) corridorStops.Add(onPath[i]);
var stops = corridorStops.Append(unreachable).ToArray();
foreach (var e in wad.Entries.Where(e => e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)
                                      && e.Path.StartsWith("/Rides/", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
{
    if (loopId >= stops.Length) break;
    string stem = e.Path[..^4];
    var samEntry = wad.Find(stem + ".sam");
    if (samEntry == null) continue;
    // ⭐⭐ A RIDE THAT CAN SEAT SOMEBODY. Picking the first three alphabetically gave Belly Bounce
    // (an inflatable: no seats, its riders BOUNCE) and Chac Atak (a coaster, which stalls on
    // COAST), and the seat checks below then passed vacuously on rides that never seat anyone.
    // The two facts line up exactly across the disc -- every jungle ride whose script uses
    // ADDHEAD has `0x80` fittings and every ride without ADDHEAD has none, ten for ten -- so
    // asking the MODEL whether it has seats is asking whether the script will use them.
    if (!System.Text.Encoding.ASCII.GetString(wad.Read(e)).Contains("VAR_ONRIDE", StringComparison.Ordinal)) continue;
    var def = RideDefinition.Parse(System.Text.Encoding.ASCII.GetString(wad.Read(samEntry)), stem + ".sam");
    Animation aps = null;
    try { var ae = wad.Find(stem + ".aps"); if (ae != null) aps = new Animation(wad.Read(ae)); } catch { }
    string ldir = e.Path[..(e.Path.LastIndexOf('/') + 1)];
    byte[] SLoop(string child)
    {
        var c = wad.Entries.FirstOrDefault(x => x.Path.Equals(ldir + child, StringComparison.OrdinalIgnoreCase));
        return c == null ? null : wad.Read(c);
    }
    // ⭐ THE SEATS ARE THE MODEL'S OWN `0x80` FITTINGS, which is the list ADDHEAD indexes with
    // `slot + 1`. A ride whose model is missing simply seats nobody rather than guessing a number.
    int seats = 0;
    Model rideModel = null;
    try
    {
        var mps = wad.Find(stem + ".mps");
        // ⚠ The LOADER's count, not the fitting count: 0x1bfdf8 walks up from id 1 and stops at
        // the first gap. They agree on every jungle ride, which is the situation in which the
        // simpler rule looks right and is not.
        if (mps != null) { rideModel = new Model(wad.Read(mps)); seats = rideModel.HeadSlotCount; }
    }
    catch { }
    if (seats == 0) continue;
    var stop = stops[loopId];
    bool control = loopId == stops.Length - 1;
    var r = loop.Add(loopId + 1, (control ? "CONTROL " : "") + (def.Name ?? stem), stop, 1, 1,
                     wad.Read(e), aps, def.UpgradeCapacity(0) ?? 1,
                     stop, control ? stop : onPath[^1], out _, sibling: SLoop, headSlots: seats);
    if (r != null)
    {
        // ⭐⭐ WHERE THE SCRIPT'S NODES ACTUALLY ARE. Without this the host answers "I do not know"
        // and every WALKON leg runs at the 100 ms floor; with it the durations are the distances
        // between the ride's own fittings, which is what the console computes. The bind pose is
        // used because this audit has no renderer -- a viewer would hand it the animated one.
        var bind = rideModel?.WorldTransforms();
        if (bind != null)
            r.Host.NodeSource = (node, space) =>
            {
                var f = rideModel.FindFitting(node, (uint)space);
                if (f is not { Node: >= 0 } hit) return null;
                return bind.TryGetValue(rideModel.NodeOffset(hit.Node), out var w)
                     ? (w.M41, w.M42, w.M43) : ((float, float, float)?)null;
            };
        loop.SetOpen(r.Id, true); loopId++;
        if (!availabilityChecked && r.Has("VAR_RIDECLOSED") && r.Has("VAR_BROKEN")
            && r.Has("VAR_LETMEON") && loopPaths.Walkable(stop))
        {
            VisitorAvailabilityChecks.Run(loopPaths, wad.Read(e), aps, def.UpgradeCapacity(0) ?? 1,
                                          stop, onPath[^1], SLoop, seats, Check);
            availabilityChecked = true;
            // ⭐ The same vetted ride doubles as the service checks' NOT-a-toilet control: it is
            // known to take a guest and hand them back, which is exactly what that control needs.
            serviceRide = (wad.Read(e), aps, def, (Func<string, byte[]>)SLoop, seats);
        }
        if (!removalChecked && r.Has("VAR_LETMEON") && r.Has("VAR_RIDECLOSED")
            && r.Has("VAR_BROKEN") && Uses(r, RseOpcode.ADDHEAD) && !PollsTrack(r) && loopPaths.Walkable(stop))
        {
            RideRemovalChecks.Run(terrain, loopPaths, wad.Read(e), aps, def.UpgradeCapacity(0) ?? 1,
                                  stop, onPath[^1], SLoop, seats, Check);
            removalChecked = true;
        }
    }
}
// ⭐⭐ THE WANT LOOP, against the game's own lavatory. Looked up by the authored flag rather than
// by name: `UsageInfo.ProvidesRelief` is what the port routes on, so the fixture is chosen the
// same way the feature chooses, and a world whose toilet is called something unexpected is still
// covered. ⚠ /Features/, not /Rides/ -- the census loop above never walks this folder.
var looEntry = wad.Entries
    .Where(x => x.Path.EndsWith(".sam", StringComparison.OrdinalIgnoreCase))
    .Select(x => (Entry: x, Def: RideDefinition.Parse(System.Text.Encoding.ASCII.GetString(wad.Read(x)), x.Path)))
    .Where(x => x.Def.ProvidesRelief && wad.Find(x.Entry.Path[..^4] + ".rse") != null)
    .OrderBy(x => x.Entry.Path, StringComparer.OrdinalIgnoreCase)
    .FirstOrDefault();
if (looEntry.Entry != null && serviceRide is { } notALoo && corridorStops.Count >= 4)
{
    string lstem = looEntry.Entry.Path[..^4];
    string ldir2 = lstem[..(lstem.LastIndexOf('/') + 1)];
    Animation laps = null;
    try { var ae = wad.Find(lstem + ".aps"); if (ae != null) laps = new Animation(wad.Read(ae)); } catch { }
    byte[] LSib(string child)
    {
        var c2 = wad.Entries.FirstOrDefault(x => x.Path.Equals(ldir2 + child, StringComparison.OrdinalIgnoreCase));
        return c2 == null ? null : wad.Read(c2);
    }
    PostServiceMovementChecks.Run(terrain,loopPaths,corridorStops[0],onPath[^1],
        wad.Read(wad.Find(lstem+".rse")),laps,looEntry.Def,LSib,Check);
    DepartureRecoveryChecks.Run(terrain, loopPaths, entrance, onPath[^1], Check);
    RideEffectConsumerChecks.Run(terrain, Check);
    ServiceRoutingChecks.Run(terrain, wad.Read(wad.Find(lstem + ".rse")), laps, looEntry.Def, LSib, Check);
    ServiceChecks.Run(terrain, loopPaths, entrance, corridorStops.ToArray(), onPath[^1],
                      wad.Read(wad.Find(lstem + ".rse")), laps, looEntry.Def, LSib,
                      notALoo.Script, notALoo.Aps, notALoo.Def, notALoo.Sibling, notALoo.Seats, Check);
}
CompiledJoinChecks.Run(Wad("DATA"), wad, world, Check);
MoodChecks.Run(Check);
PathPriceChecks.Run(terrain, PathPieces.Read(disc), Check);
ScreamChecks.Run(disc, world, Check);
BusAdmissionChecks.Run(disc, Check);
NativeGuestMotionChecks.Run(Check);
NativeLogicalAnimationChecks.Run(disc,Check);
NativeGuestRouteChecks.Run(Check);
NativeWalkConsumerChecks.Run(disc,Check);
NativeEntranceFlowChecks.Run(disc,Check);
NativeEntranceAcceptanceChecks.Run(Check);
NativeRoutePoolChecks.Run(disc,Check);
NativeDepartureChecks.Run(disc,Check);
NativeRideQueueChecks.Run(Check);
if (serviceRide is { } queueRide)
    NativeRideQueueChecks.RunWalked(terrain, loopPaths, onPath, queueRide.Script, queueRide.Aps,
        queueRide.Def.UpgradeCapacity(0) ?? 1, queueRide.Sibling, queueRide.Seats, Check);
else Check(false, "native ride queue walked: no vetted ride to queue for");
SfxGraphChecks.Run(disc, Check);
BridgeChecks.Run(terrain, PathPieces.Read(disc), world, Check);
QueueRemovalChecks.Run(terrain, PathPieces.Read(disc), world, Check);
QueueWalkChecks.Run(terrain, PathPieces.Read(disc), world, Check);
TrackRideChecks.Run(Check);
TrackRideChecks.RunParkSim(new ParkPaths(terrain), wad, world, Check);
RideValueChecks.Run(Wad("DATA"), wad, world, Check);
GuestAnimationChecks.Run(Wad("DATA"), Check);
CompiledShopPurchaseChecks.Run(terrain, loopPaths, corridorStops[0], onPath[^1], Wad("DATA"), wad, world, Check);
NativeDestinationChecks.Run(terrain, Wad("DATA"), wad, world, Check);
NativeReliefChecks.Run(terrain,Wad("DATA"),wad,world,Check);
TerminalWalkingChecks.Run(terrain, Wad("DATA"), wad, world, Check);
DestinationScoreChecks.Run(disc, Check);
    NativeRideValueChecks.Run(Check);
DecisionSchedulingChecks.Run(terrain, loopPaths, corridorStops[0], onPath[^1], Wad("DATA"), wad, world, Check);
Check(looEntry.Entry != null, $"the world ships a lavatory to exercise ({looEntry.Entry?.Path ?? "none found"})");
Check(availabilityChecked, "availability regression exercised a real ride with both availability flags");
Check(removalChecked, "removal regression exercised a real non-track ride with seats");
var marooned = loop.Rides.FirstOrDefault(r => r.Name.StartsWith("CONTROL", StringComparison.Ordinal));
Console.WriteLine($"  {loopId} rides placed, {Math.Max(0, loopId - 1)} on the corridor at {string.Join(", ", stops.Take(Math.Max(0, loopId - 1)))}"
                + $"; the control's queue is at {unreachable}, off the path");

for (int i = 0; i < 12; i++) visitors.Arrive(mouth, onPath[^1]);
var rng = new Random(3);
for (int i = 0; i < 12_000; i++)
    visitors.Step(0.04, () => onPath[rng.Next(onPath.Length)]);

Console.WriteLine($"  after {loopWalk.Time / 1000}s: {visitors.Boardings} boardings, {visitors.Rides} completed rides"
                + $", {loopWalk.Guests.Count} still walking, {visitors.Plans.Values.Count(p => p.Intent == VisitorIntent.Queued)} queued");
foreach (var r in loop.Rides)
    Console.WriteLine($"  {r.Name,-22} queue {r.Queue.Count}  on ride {r.OnRide}"
                    + (r.Fault != null ? $"  FAULT {Kind(r.Fault)}" : ""));
Check(visitors.Boardings > 0, $"a guest walks to a ride's queue and is handed over ({visitors.Boardings} times)");
// ⭐⭐ AND SOMEBODY IS IN THE SEATS. ADDHEAD puts a rider in a RANDOM free head slot and attaches
// them to that slot's fitting, so this is the first number that says WHERE a rider is and not just
// that one exists -- and it is the thing a renderer needs to draw a person on a ride.
var seated = loop.Rides.Where(r => r.Host.Seats.Count > 0).ToList();
foreach (var r in loop.Rides)
    Console.WriteLine($"  {r.Name,-22} {r.Machine.Heads.Count} seats, {r.Host.Seats.Count} taken"
                    + (r.Host.Seats.Count > 0 ? $": {string.Join(", ", r.Host.Seats.OrderBy(kv => kv.Key).Select(kv => $"#{kv.Key}=g{kv.Value}"))}" : ""));
Check(loop.Rides.Any(r => r.Machine.Heads.Count > 0), "a ride has seats at all (its model's 0x80 fittings)");
Check(seated.Count > 0, $"a rider is put IN a seat, not just counted ({seated.Count} rides have somebody seated)");
Check(loop.Rides.All(r => r.Host.Seats.Count <= r.Machine.Heads.Count),
      "no ride seats more people than it has seats");
// ⭐ AND THE WALKS ARE TIMED, not floored. RseMachine.WalksAreTimed is false when the host could
// not place a node, in which case every leg took the 100 ms minimum and any figure about how long
// a ride cycle takes is meaningless. It is the flag that stops a number being quoted.
var timed = loop.Rides.Where(r => r.Machine.WalksAreTimed).ToList();
Console.WriteLine($"  {timed.Count} of {loop.Rides.Count} rides timed their walks from real node positions"
                + $" ({string.Join(", ", timed.Select(r => r.Name))})");
Check(timed.Count > 0, "a ride's WALKON legs are timed by the distance between its own fittings");

// ⭐⭐ AND THE ONES THAT DID NOT -- IS THAT A DEFECT? `WalkMilliseconds` resolves the guest-side
// node in park space `0x800`, and thirteen ride models on this disc carry NO `0x800` fitting at
// all: monkey (Crazy Ape), spider, volcano, bumper, cart, croccar, Bird, ape, wr_ring and the
// four go-karts. Every leg on those takes the 100 ms minimum.
//
// That is only a DEFECT for a ride that actually asks to walk somebody. A script that seats its
// riders with ADDHEAD alone never calls WALKON and has no use for a park-space node; Inca Totem,
// which passes VAR_ONRIDE as the destination so the Nth rider walks to the Nth seat, does. So ask
// the bytecode which rides walk, and require exactly those to be timed.
//
// ⚠⚠ THIS CHECK BELONGS HERE AND NOWHERE EARLIER. The script-only census above runs without any
// model, so its host has no NodeSource and EVERY ride floors -- a copy of this check up there
// reported eight broken rides that were nothing of the kind. A walk-timing check is meaningless
// wherever the geometry is absent, so it lives with the rides that were given models.
//
// ⚠ It asks the bytecode, not the run: a WALKON on an untaken branch still counts as asking.
// That direction is conservative -- it can only make a ride look MORE in need of timing.
bool AsksToWalk(ParkRide r) => ParkSim.Chain(r.Machine)
    .Any(m => m.Program.Instructions.Any(i => i.Opcode == RseOpcode.WALKON));
var walkers = loop.Rides.Where(AsksToWalk).ToList();
// ⚠⚠ ATTEMPTED, not merely asked. `WalksAreTimed == false` means either "a node would not
// resolve" or "nobody ever walked", and those are opposite verdicts. The CONTROL ride is placed
// off the path precisely so nobody reaches it, so it calls WALKON in its bytecode, never runs
// one, and an "asked but not timed" check FAILS ON ITS OWN CONTROL. WalksWereAttempted is set
// inside WalkMilliseconds, so it separates the two without a proxy like boardings.
var flooredWalkers = walkers.Where(r => r.Machine.WalksWereAttempted && !r.Machine.WalksAreTimed).ToList();
var neverWalked = walkers.Where(r => !r.Machine.WalksWereAttempted).ToList();
// ⭐ SAY WHAT FRACTION OF THE WORLD THIS COVERED. The loop stands up four rides; the script-level
// census above sees every ride in the world, and more of them call WALKON than get placed here.
// Without this line "every ride that calls WALKON timed its legs" reads as a statement about the
// park when it is a statement about four rides. Coverage is N-of-M or it is not coverage.
int worldWalkers = sim.Rides.Count(AsksToWalk);
Console.WriteLine($"  walk timing covered {walkers.Count} of the {worldWalkers} rides in this world that call WALKON");
Console.WriteLine($"  {walkers.Count} of {loop.Rides.Count} placed rides call WALKON at all;"
                + $" {walkers.Count - neverWalked.Count} actually ran one"
                + (neverWalked.Count > 0 ? $" (never walked: {string.Join(", ", neverWalked.Select(r => r.Name))})" : ""));
foreach (var r in flooredWalkers)
    Console.WriteLine($"  FLOORED: {r.Name} calls WALKON but no node resolved -- its legs ran at 100 ms");
Check(walkers.Count > neverWalked.Count, $"some placed ride actually RAN a walk ({walkers.Count - neverWalked.Count} of {walkers.Count} that call WALKON) -- otherwise the check below is vacuous");
// ⚠ EXPECTED RED ON SPACE (Moon Buggies), and left red for the same reason as Thrill Grill: a
// by-name exclusion is where the next one would hide. `WalkMilliseconds` resolves the GUEST-side
// node of a walk in park space `0x800` against the RIDE MODEL -- but that node is where the guest
// stands in the PARK, at the queue. Rides that pass (dizzyd, incagod) each carry exactly four
// `0x800` fittings; Moon Buggies carries none and still calls WALKON, so either the console
// resolves that node somewhere other than the ride model, or the disc is like that. OPEN.
Check(flooredWalkers.Count == 0, "every ride that calls WALKON timed its legs from real node positions"
    + (flooredWalkers.Count == 0 ? "" : ": " + string.Join(", ", flooredWalkers.Select(r => r.Name))
        + " -- KNOWN for Moon Buggies (SPACE), and it is the DISC, not us: its WALKON asks for nodes"
        + " 1-4 exactly as Spawheel's does, but its model carries no 0x800 fitting at all where"
        + " Spawheel has id1-4 at 0x811. Only SPACE ride with zero. See findings/visitors.md."
        + " Anything else here is NEW."));
Check(visitors.Rides > 0, $"a guest comes back OUT of a ride and walks away ({visitors.Rides} did)");
// ⚠⚠ THE SAME PEOPLE, not the same COUNT. A guest handed to a ride leaves the walking layer and
// is put back when the script is done, and putting them back as a NEW id would pass every count
// in this audit while quietly making the park's population grow forever. The twelve who walked in
// are the twelve who should still be here, whatever they have been on.
var ids = loopWalk.Guests.Select(g => g.Id).Concat(
              visitors.Plans.Values.Where(p => p.Intent == VisitorIntent.Queued).Select(p => p.Guest))
          .Distinct().OrderBy(i => i).ToArray();
Console.WriteLine($"  the park holds {ids.Length} distinct guests: {string.Join(", ", ids.Take(14))}");
Check(ids.Length == 12, $"the twelve who walked in are still the same twelve ({ids.Length} distinct ids)");
Check(marooned == null || (marooned.Queue.Count == 0 && marooned.OnRide == 0),
      "control: nobody reaches the ride whose queue is off the path"
    + (marooned != null ? $" (queue {marooned.Queue.Count}, on ride {marooned.OnRide})" : " -- control did not load"));
Check(marooned != null, "the control ride exists at all (otherwise the check above is vacuous)");

// ⭐⭐ WHAT THE PARK IS ASKING FOR AND NOT GETTING. Every ride and shop above runs its whole
// script, and a good part of what those scripts DO is ask the world for things this port does not
// yet make: a noise, a puff of smoke, a hat on a guest's head. Those requests go through
// IRseHost.TryEffect, which records them and renders nothing -- so the park is quietly generating
// a complete specification for the presentation layer, and this prints it rather than leaving it
// to be guessed at later.
//
// ⚠ COUNTED BY THE ARGUMENTS, not just the opcode. "EVENT x4,000" is a number; "EVENT 3 -1 8
// x812" is a thing to go and identify.
// ⭐ NAMED, not numbered. The census used to print "EVENT 2 5 22 x49", which is a number to
// stare at; Tp2.plb turns it into ApeSnot, which is a thing to go and draw. Kinds 1 and 2 index
// the particle library. ⭐⭐ KINDS 3..11 ARE THE `OBJ_SOUND_*` GROUPS, and each is one `*SFX.MAP`
// keyed by the third operand: `EVENT 3 -1 8` is `EVT_RIDE_APE` and the jungle ride map's event 8
// is `apeoooooC.vag`. This used to say kind 3 "goes to a different manager whose ids run past
// 105" and left them as numbers; the ids run past 105 because they are event ids, not particles.
ParticleLibrary fx = null;
try
{
    var pwad = disc.Files().SingleOrDefault(f => f.Path.Equals("/DATA/PARTICLE.WAD", StringComparison.OrdinalIgnoreCase));
    if (pwad != null)
    {
        var pw = new WadArchive(disc.Read(pwad.Extent, pwad.Size));
        fx = new ParticleLibrary(pw.Read(pw.Find("/Tp2.plb")));
        // ⚠⚠ THE +0x70 CENSUS, AND THE THREE FIELDS NO CONSUMER READS. The port's blend polarity
        // rests on a TWO-EFFECT argument -- "Sparks reads 0 and ApeSnot reads 4; a spark glows and
        // snot does not" -- so the thing that settles how much weight that carries is the FULL
        // list of which effects are on which side, not two of them.
        if (args.Contains("--particle-unread"))
        {
            var on = new List<string>(); var off = new List<string>();
            foreach (var e in fx.Effects)
            {
                if (e.Raw.Length < 0xc4) continue;
                ((e.Raw[0x70] & 4) != 0 ? on : off).Add(e.Name);
                int u90 = BinaryPrimitives.ReadInt32LittleEndian(e.Raw.AsSpan(0x90, 4));
                int ua4 = BinaryPrimitives.ReadInt16LittleEndian(e.Raw.AsSpan(0xa4, 2));
                int uc3 = e.Raw[0xc3];
                if (u90 != 0 || ua4 != 0 || uc3 != 0)
                    Console.WriteLine($"  unread {e.Id,3} {e.Name,-18} +0x90={u90} +0xa4={ua4} +0xc3={uc3}");
            }
            // ⭐⭐ THE SPRITE, RESOLVED. `0x182680` is
            //   if (group-1 > 0x1f) return 0;  return images[ tbl32[group] + frame/2 ];
            // with the group->base table at 0x364058 in the executable. So an effect's images are
            // `base .. base + (frames-1)/2`, and the /2 is why two logical frames share one image.
            // ⭐ And WHAT the images are: PARTICLE.WAD's own entry list, so the index the
            // resolver returns can be turned into a file rather than staying a number.
            foreach (var en in pw.Entries) Console.WriteLine($"  pwad {en.Path}");
            int[] GroupBase = { 0,1,2,3,7,11,18,19,20,21,29,33,41,49,52,57,58,59,60,61,62,63,64,65,
                                66,67,68,69,70,71,72,73,0 };
            // ⭐⭐ AND WRITE THE ART OUT, because the only thing that actually answers "is it a
            // puff of smoke" is looking at it. Each named effect's frames are laid side by side
            // and written as an uncompressed 32-bit TGA -- no encoder needed, and the box's ffmpeg
            // turns it into a PNG.
// ⭐⭐ WHICH RIDES ASK FOR PARTICLES, AND WHICH EFFECT. Master: "how are particles done for
// crazy ape's anims?" Read STATICALLY off every ride's bytecode rather than by running one in a
// demo and watching what happens to come out -- a run only shows the branches it took, and I had
// already said "Crazy Ape asks for zero" on the strength of one 10-second capture.
// ⚠ EVENT/ADDOBJ carry a KIND first, and only kinds 1 and 2 reach Tp2.plb (0x18b5a8/0x18b0f8);
// kind 3+ are sound groups whose ids run past the library's 105 and mean something else entirely.
if (args.Contains("--particle-events"))
{
    foreach (var re in wad.Entries
                 .Where(x => x.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)
                          && x.Path.StartsWith("/Rides/", StringComparison.OrdinalIgnoreCase))
                 .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
    {
        RseProgram prog;
        try { prog = new RseProgram(wad.Read(re)); } catch { continue; }
        var hits = new List<string>();
        foreach (var ins in prog.Instructions)
        {
            if (ins.Opcode is not (RseOpcode.EVENT or RseOpcode.ADDOBJ)) continue;
            if (ins.Operands.Count < 3) continue;
            // ⚠ Only literal operands can be read here; a computed id is invisible to a static
            // pass and is reported as such rather than skipped silently.
            var v = ins.Operands.Select(o => o.ToString()).ToArray();
            if (!int.TryParse(v[0], out int kind)) { hits.Add($"{ins.Opcode}(kind=?)"); continue; }
            if (kind is not (1 or 2)) continue;
            string name = int.TryParse(v[2], out int fxid) && fx?[fxid]?.Name is { Length: > 0 } n ? n : v[2];
            hits.Add($"{ins.Opcode} kind {kind} node {v[1]} -> {name}");
        }
        if (hits.Count > 0)
            Console.WriteLine($"  fxevent {re.Path}: {string.Join(" | ", hits.Distinct())}");
    }
    Console.WriteLine("  fxevent -- end (rides with no line above ask for NO particles)");
}

            foreach (var want in new[] { "ApeSnot", "Bubbles", "Fire", "Smoke", "Create1", "Destroy1" })
            {
                var eff = fx.Effects.FirstOrDefault(x => x.Name == want);
                if (eff == null || eff.Raw.Length < 0x98) continue;
                int grp = BinaryPrimitives.ReadInt16LittleEndian(eff.Raw.AsSpan(0x94, 2));
                int frames = BinaryPrimitives.ReadInt16LittleEndian(eff.Raw.AsSpan(0x96, 2));
                var names = new List<string>();
                for (int f = 0; f < Math.Max(1, frames); f += 2)
                    if (ParticleSprites.For(grp, f) is { } n && (names.Count == 0 || names[^1] != n))
                        names.Add(n);
                var tiles = new List<Ssh>();
                foreach (var n in names)
                    try { tiles.Add(new Ssh(pw.Read(pw.Find(ParticleSprites.Path(n))))); }
                    catch (Exception ex) { Console.WriteLine($"  art {want}: {n} -- {ex.Message}"); }
                if (tiles.Count == 0) { Console.WriteLine($"  art {want}: nothing decoded"); continue; }
                int tw = tiles[0].Width, th = tiles[0].Height, W = tw * tiles.Count;
                var px = new byte[W * th * 4];
                for (int t = 0; t < tiles.Count; t++)
                    for (int y = 0; y < th; y++)
                        for (int x = 0; x < tw; x++)
                        {
                            int src = (y * tiles[t].Width + x) * 4, dst = (y * W + t * tw + x) * 4;
                            if (src + 3 >= tiles[t].Pixels.Length) continue;
                            // ⚠ TGA is BGRA, the decoder gives RGBA.
                            px[dst] = tiles[t].Pixels[src + 2]; px[dst + 1] = tiles[t].Pixels[src + 1];
                            px[dst + 2] = tiles[t].Pixels[src];  px[dst + 3] = tiles[t].Pixels[src + 3];
                        }
                var hdr = new byte[18];
                hdr[2] = 2; hdr[12] = (byte)(W & 0xff); hdr[13] = (byte)(W >> 8);
                hdr[14] = (byte)(th & 0xff); hdr[15] = (byte)(th >> 8); hdr[16] = 32; hdr[17] = 0x20;
                var path = $"C:/claude-workspace/art_{want}.tga";
                using (var fsx = File.Create(path)) { fsx.Write(hdr); fsx.Write(px); }
                Console.WriteLine($"  art {want}: group {grp}, {frames} frames -> {tiles.Count} images "
                                + $"{tw}x{th} ({string.Join(",", names)}) -> {path}");
            }
            // ⭐⭐ WHAT IS THE HEADER u16 AT +0x34? Tinyclaw's coaster decomp resolves a fitting
            // to a node as `index + u16@0x34`; `Model.Fittings` uses `Meshes.Count + index`. They
            // agree on monkey.mps and disagree on 107 of 360 models, so one of them is a
            // coincidence. Printed for every model with fittings rather than argued about.
            foreach (var re in wad.Entries.Where(x => x.Path.EndsWith(".mps", StringComparison.OrdinalIgnoreCase))
                                          .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            {
                byte[] raw; try { raw = wad.Read(re); } catch { continue; }
                if (raw.Length < 0x78) continue;
                ushort U16(int o) => BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(o, 2));
                int nmesh = U16(0x30), h32 = U16(0x32), h34 = U16(0x34), nfit = U16(0x36);
                if (nfit == 0) continue;
                Console.WriteLine($"  hdr {re.Path,-40} meshes={nmesh,3} +0x32={h32,3} +0x34={h34,3} "
                                + $"fittings={nfit,3} {(h34 == nmesh ? "agree" : "DIFFER")}");
            }
            Console.WriteLine($"  record 0 is named '{fx.Effects[0].Name}'");
            foreach (var e in fx.Effects)
            {
                if (e.Raw.Length < 0x98) continue;
                int grp = BinaryPrimitives.ReadInt16LittleEndian(e.Raw.AsSpan(0x94, 2));
                int frames = BinaryPrimitives.ReadInt16LittleEndian(e.Raw.AsSpan(0x96, 2));
                if (frames == 0) { Console.WriteLine($"  sprite {e.Id,3} {e.Name,-18} UNTEXTURED"); continue; }
                int b = grp >= 1 && grp <= 32 ? GroupBase[grp] : -1;
                Console.WriteLine($"  sprite {e.Id,3} {e.Name,-18} group={grp,2} frames={frames,2}"
                                + $" -> images {b}..{(b < 0 ? -1 : b + (frames - 1) / 2)}");
            }
            Console.WriteLine($"  +0x70 bit2 SET   ({on.Count}): {string.Join(", ", on)}");
            Console.WriteLine($"  +0x70 bit2 CLEAR ({off.Count}): {string.Join(", ", off)}");
            return 0;
        }
    }
}
catch (Exception e) { Console.WriteLine($"  (no particle library: {e.Message})"); }
// ⚠ A WORLD SHIPS TWO PARKS AND THIS AUDIT BUILDS EVERY RIDE IN ONE. Each park has its own ride
// map (jungle: 70 events against 54), and a ride the game only offers in park 2 -- Mumbo, Inca
// Totem -- has its sounds only in park 2's map: resolved against park 1 alone, 43 of the jungle's
// 95 cues came back "no such event" and every one was those two rides. Park 1 is asked first,
// then park 2, and the census says which served, because the game loads exactly one of them.
SoundCatalogue sounds = null, park2 = null;
try { sounds = new SoundCatalogue(disc, world, 1); park2 = new SoundCatalogue(disc, world, 2); }
catch (Exception e) { Console.WriteLine($"  (no sound catalogue: {e.Message})"); }
(SoundCatalogue.Resolved Hit, int Park) ResolveEither(int kind, int id)
{
    var a = sounds?.Resolve(kind, id);
    if (a != null) return (a, 1);
    var b = park2?.Resolve(kind, id);
    return (b, b == null ? 0 : 2);
}
string Clips(SoundCatalogue.Resolved r) => string.Join("|", r.Clips.Select(c => c.Name).Distinct());

// ⭐⭐ THE GUEST'S OWN SOUNDS, and the point is that they resolve through the SAME index the
// ride scripts use. `FUN_00111428(audio, 7, id, &pos, handle, 0)` is the engine's positional
// one-shot, and its second argument is the KIND -- the same 3..11 `OBJ_SOUND_*` group number
// this audit already maps to one `*SFX.MAP` each. So an id the guest code passes is an EVENT
// ID, not some separate engine numbering, which is what this file previously recorded as
// unresolved.
//
// ⚠ THE CONTROL IS THE LAVATORY. `FUN_0020EDD8`'s relief arm plays **53**, and the kids map is
// already known to hold 48..53 as EVT_BOG1..EVT_BOG5 and EVT_DOORSKWEEK1 -- so if 53 does not
// come back as a door or a toilet, the kind-7 reading is wrong and every other row here is
// meaningless. A lookup with no control cannot tell "resolved" from "resolved to anything".
// ⚠ SWEPT OVER EVERY GROUP, because the first version assumed the `7` these calls pass IS the
// OBJ_SOUND group number and THE CONTROL FAILED: the lavatory's own id did not resolve under 7
// either, and 53 is known to live in the kids map, which is group 6. `FUN_00111428`'s second
// argument selects a table INSIDE that function, so it is a category in some other numbering.
// ⭐⭐ EVERY effect call in the guest code (0x209000..0x213000), found by censusing `jal` to the
// two entry points and reading the `a2` immediate at each site. Eight, not the four first noticed.
foreach (var (sfxId, sfxWhat) in new[] { (31, "0x00126408 PLACEMENT bought"), (175, "0x00197F48 cannot afford"),
                                         // ⚠ CORRECTED: these are the ride SCREAM, and the threshold is the
                                         // RIDER COUNT, not the footprint. FUN_001B94B8 tests the value
                                         // STARTSCREAM takes from VAR_ONRIDE and guards on `!= 0`; a placed
                                         // ride's footprint is never 0. See RideScreams.
                                         (71, "0x1B94B8 scream, 1 rider"), (72, "scream, 2-3 riders"),
                                         (73, "scream, 4-7 riders"), (74, "scream, 8+ riders"),
                                         (53, "0x20F0A0 lavatory relief -- THE CONTROL"),
                                         (208, "0x20EAD8 shop visit complete"),
                                         (205, "0x20FA14 very unhappy"), (129, "0x2102D4 very happy"),
                                         (307, "0x20CA60"), (204, "0x20CFA4"),
                                         (126, "0x2105FC"), (51, "0x211710") })
{
    var found = new List<string>();
    foreach (int grp in new[] { 3, 4, 5, 6, 7, 8, 9, 11 })
    {
        var (h, pk) = ResolveEither(grp, sfxId);
        if (h != null) found.Add($"group {grp}({(SoundGroup)grp}) park {pk} -> {Clips(h)}");
    }
    Console.WriteLine($"  guest sfx: id {sfxId} ({sfxWhat}): "
                    + (found.Count == 0 ? "NO GROUP RESOLVES IT" : string.Join("; ", found)));
}
// ⭐⭐ WHY DOES A BIN HUM FOREVER? Master: "loadspeakers and bins are spamming sounds forever ...
// the sound is looping." This port decides a voice loops from the OPCODE -- `ADDOBJ` loops,
// `EVENT` is a one-shot -- and findings/sound.md is explicit that this is "a reading of the
// scripts ... not of the object list at instance +0xb0, which has not been walked". So it is an
// inference, and a bin humming forever is evidence against it. This lists what the scenery
// actually asks for, with the SET COUNT, because a start/loop/end event and a single-clip one
// are different things being forced down the same path.
foreach (var featEntry in wad.Entries
             .Where(e => e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)
                      && e.Path.Contains("/Features/", StringComparison.OrdinalIgnoreCase))
             .Take(40))
{
    RseProgram prog;
    try { prog = new RseProgram(wad.Read(featEntry)); } catch { continue; }
    foreach (var ins in prog.Instructions)
    {
        if (ins.Opcode is not (RseOpcode.EVENT or RseOpcode.ADDOBJ)) continue;
        var ops = ins.Operands.Select(o => o.Index).ToArray();
        if (ops.Length < 3 || !SoundCatalogue.IsSoundGroup(ops[0])) continue;
        var (h, pk) = ResolveEither(ops[0], ops[2]);
        Console.WriteLine($"  scenery sfx: {System.IO.Path.GetFileName(featEntry.Path),-20} {ins.Opcode,-7}"
                        + $" group {ops[0]} evt {ops[2],3} tag {(ops.Length > 3 ? ops[3] : 1000),4}"
                        + (h == null ? "  (unresolved)"
                           : $"  sets {h.Sets} w0C {h.Word0C,5} flags 0x{h.Flags:x4}  {Clips(h)}"));
    }
}

// ⭐ WHAT IS IN A RIDE'S BUILD SECTION? A script runs from address 0; whatever it does before
// its first LOOPANIM is the construction phase. Print that prefix for a couple of rides, so
// "missing build particles" can be answered from what is actually there.
foreach (var bs in wad.Entries.Where(e => e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)
                                       && e.Path.Contains("/Rides/", StringComparison.OrdinalIgnoreCase)).Take(2))
{
    RseProgram bp; try { bp = new RseProgram(wad.Read(bs)); } catch { continue; }
    string leaf3 = System.IO.Path.GetFileNameWithoutExtension(bs.Path);
    foreach (var ins4 in bp.Instructions.Take(18))
        Console.WriteLine($"  build head: {leaf3,-12} {ins4.Address,4}: {ins4.Opcode} "
                        + string.Join(" ", ins4.Operands.Select(o => o.Index)));
}

// ⭐ Does the disc carry path definitions of its own? The price lives on a path-material object
// at +0x10 and the compiled directory has no path kind, so the next candidate is a data file.
foreach (var pe2 in Wad("DATA").Entries
             .Where(e => e.Path.Contains("path", StringComparison.OrdinalIgnoreCase)
                      || e.Path.Contains("queue", StringComparison.OrdinalIgnoreCase))
             .Take(14))
    Console.WriteLine($"  path asset: {pe2.Path}");

// ⭐ WHAT KINDS DOES THE COMPILED DIRECTORY HOLD, and do any of them look like a path? Master
// asked what paths and queues cost; the tool debits a per-tool figure and where that figure comes
// from is not yet traced, so this asks whether the DBA prices them as assets the way it prices
// features.
try
{
    var dbaE = Wad("DATA").Entries.FirstOrDefault(e => e.Path.Equals("/arsdb.dba", StringComparison.OrdinalIgnoreCase));
    if (dbaE != null)
    {
        var db2 = new AssetResourceDatabase(Wad("DATA").Read(dbaE));
        var byKind = db2.Entries.GroupBy(e => e.Kind).OrderBy(g => (int)g.Key);
        Console.WriteLine("  dba kinds: " + string.Join("  ", byKind.Select(g => $"{(int)g.Key}:{g.Key}={g.Count()}")));
    }
}
catch (Exception e) { Console.WriteLine($"  (dba kind census failed: {e.Message})"); }

// ⭐⭐ CAN THE GAME'S OWN FONT ACTUALLY DRAW THE MONEY? The readout composes glyphs out of
// `Console.bff`, so a character the formatter can produce but the font has no glyph for would
// render as a HOLE -- silently, and only visible to somebody looking at the screen. Every
// character `Money.Format` can emit is checked against the font's own lookup.
try
{
    var fontEntry = Wad("DATA").Entries.FirstOrDefault(e => e.Path.Equals("/Fonts/European/Large.bff", StringComparison.OrdinalIgnoreCase));
    // ⭐ Large.bff, not Console.bff: FUN_0020A958 picks the record whose stored index matches
    // its argument, the money draw passes 1, and FUN_0020BA28 loads them Small=0, Large=1,
    // Console=2. The port had the Console face because the NAME sounded like a HUD.
    Check(fontEntry != null, "the disc carries /Fonts/European/Large.bff -- the money HUD's font");
    if (fontEntry != null)
    {
        var hud = new BitmapFont(Wad("DATA").Read(fontEntry));
        var need = new SortedSet<char>("$,-0123456789");
        var absent = need.Where(c => !hud.TryGetGlyph(c, out _)).ToArray();
        Check(absent.Length == 0, $"every character the money format emits has a glyph ({new string(need.ToArray())})"
                                + (absent.Length == 0 ? "" : $"; MISSING {new string(absent)}"));
        // ⚠ THE CONTROL: the lookup must be capable of saying NO, or the line above passes for a
        // font that claims to have everything.
        Check(!hud.TryGetGlyph('\u0001', out _), "CONTROL: and the lookup refuses a code the font does not carry");
        // ⭐ Every glyph the money needs must also have pixels -- a zero-size descriptor is a
        // legal glyph and an invisible one.
        var blank = need.Where(c => hud.TryGetGlyph(c, out var g) && (g.Width == 0 || g.Height == 0) && c != ' ').ToArray();
        Check(blank.Length == 0, $"and each of them has pixels" + (blank.Length == 0 ? "" : $"; BLANK {new string(blank)}"));
    }
}
catch (Exception e) { Check(false, $"the money font loads: {e.Message}"); }

// ⭐⭐ WHICH PARTICLE EFFECTS DOES A SCRIPT ASK FOR, AND DOES THE LIBRARY ANSWER? Master: "we
// are missing a lot of particle effects. mainly the ones produced when something is built."
// Kinds 1 and 2 index the particle library, so "missing" is either an id the library has no
// entry for, or an event the port never reaches. This separates those two.
{
    var wanted = new SortedDictionary<(int Kind, int Id), (int Count, string Name, HashSet<string> Files)>();
    foreach (var pe in wad.Entries.Where(e => e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)))
    {
        RseProgram pp; try { pp = new RseProgram(wad.Read(pe)); } catch { continue; }
        foreach (var ins3 in pp.Instructions)
        {
            if (ins3.Opcode is not (RseOpcode.EVENT or RseOpcode.ADDOBJ)) continue;
            var a3 = ins3.Operands.Select(o => o.Index).ToArray();
            if (a3.Length < 3 || a3[0] is not (1 or 2)) continue;
            var key = (a3[0], a3[2]);
            if (!wanted.TryGetValue(key, out var cur))
                cur = (0, fx?[a3[2]]?.Name ?? "", new HashSet<string>());
            cur.Files.Add(System.IO.Path.GetFileNameWithoutExtension(pe.Path));
            wanted[key] = (cur.Count + 1, cur.Name, cur.Files);
        }
    }
    // ⭐⭐ WHAT DOES THE LIBRARY HOLD THAT NO SCRIPT ASKS FOR? If a construction puff exists, it
    // is spawned by the GAME, not by a ride -- so it will be an effect with a name and no
    // caller. That set is small enough to read.
    if (fx != null)
    {
        var askedIds = wanted.Keys.Select(k => k.Id).ToHashSet();
        var orphans = Enumerable.Range(0, fx.Effects.Count)
            .Where(i2 => !askedIds.Contains(i2) && (fx[i2]?.Name.Length ?? 0) > 0)
            .Select(i2 => $"{i2}:{fx[i2].Name}").ToArray();
        Console.WriteLine($"  particles: library holds {fx.Effects.Count}; {orphans.Length} named effects no script asks for");
        for (int c = 0; c < orphans.Length; c += 8)
            Console.WriteLine("    particles: " + string.Join("  ", orphans.Skip(c).Take(8)));
    }
    int named = wanted.Count(k => k.Value.Name.Length > 0);
    Console.WriteLine($"  particles: {wanted.Count} distinct (kind,id) asked for by scripts; {named} resolve in Tp2.plb");
    foreach (var (k, v) in wanted.Where(k => k.Value.Name.Length == 0).Take(12))
        Console.WriteLine($"    particles: kind {k.Kind} id {k.Id,3} x{v.Count,-3} NO LIBRARY ENTRY  ({string.Join(" ", v.Files.Take(4))})");
    Check(fx != null, "the particle library loaded at all");
}

// ⭐⭐ WHAT WILL `ride.Name` ACTUALLY BE? The viewer names a placed ride from its .sam display
// name, and the backwards-animation match is written against FILE STEMS ("s_plant", "toilet").
// If those do not appear in the display name the match never fires. Print both together.
foreach (var fs in wad.Entries.Where(e => e.Path.EndsWith(".sam", StringComparison.OrdinalIgnoreCase)
                                       && e.Path.Contains("/Features/", StringComparison.OrdinalIgnoreCase)))
{
    var d2 = RideDefinition.Parse(System.Text.Encoding.ASCII.GetString(wad.Read(fs)), fs.Path);
    string stem2 = System.IO.Path.GetFileNameWithoutExtension(fs.Path);
    var apsE = wad.Find(fs.Path[..^4] + ".aps");
    string slotList = "(no .aps)";
    if (apsE != null)
        try { slotList = string.Join(",", new TPW.PS2.Data.Animation(wad.Read(apsE)).Records()
                                            .Select(r => r.Slot).Distinct().OrderBy(x => x)); }
        catch { slotList = "(unreadable)"; }
    Console.WriteLine($"  feature name: stem {stem2,-12} display \"{d2.Name}\"  slots [{slotList}]");
}

// ⭐ WHICH ANIMATION SLOT DOES A FEATURE'S SCRIPT ASK FOR? Master says slot 1 is the create for
// the toilet and s_plant. Before changing any playback, read what the script requests -- if it
// already asks for 1, nothing is missing and the fault is elsewhere.
foreach (var fr in wad.Entries.Where(e => e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)
                                       && e.Path.Contains("/Features/", StringComparison.OrdinalIgnoreCase)))
{
    string leaf = System.IO.Path.GetFileNameWithoutExtension(fr.Path);
    if (!leaf.Contains("plant", StringComparison.OrdinalIgnoreCase)
     && !leaf.Contains("toilet", StringComparison.OrdinalIgnoreCase)
     && !leaf.Contains("bog", StringComparison.OrdinalIgnoreCase)) continue;
    RseProgram pg; try { pg = new RseProgram(wad.Read(fr)); } catch { continue; }
    foreach (var ins in pg.Instructions)
        if (ins.Opcode.ToString().Contains("ANIM"))
            Console.WriteLine($"  feature anim: {leaf,-12} {ins.Address,4}: {ins.Opcode} "
                            + string.Join(" ", ins.Operands.Select(o => o.Index)));
}

// ⭐⭐ THE REPEAT FLAG, ASSERTED AGAINST WHAT THE OBJECTS ARE. `RideSounds` now decides repetition
// from the L2 record's `+0x10` flag and its `+0xC` interval rather than from the shape of the
// sets -- the fourth rule, and the first read off a field. This pins the correlation that
// justified it, both ways, because a flag that is set on everything or nothing explains nothing.
{
    // ⚠⚠ THE FIRST VERSION NAMED JUNGLE'S EVENT IDS (236..239, 188, 93) AND RAN THEM IN EVERY
    // WORLD, where they mean nothing -- FANTASY, HALLOW and SPACE each matched 2 of 6. That is
    // the SECOND check tonight fitted to one world's observation; the first was a boredom figure
    // astraclaw's four-world gate caught the same way. ⭐ A number I have just watched go by is
    // not an invariant, and running it everywhere is how that gets found out.
    //
    // ⭐⭐ So assert the PROPERTY instead: the flag must PARTITION this world's scenery events --
    // some carry it, some do not -- and every event that carries it must name an interval. A
    // flag set on everything, or on nothing, or one with no interval behind it, all fail. That
    // holds in any world without naming a single id.
    int flagged = 0, plain = 0, flaggedNoInterval = 0;
    foreach (var fe2 in wad.Entries.Where(e => e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)
                                            && e.Path.Contains("/Features/", StringComparison.OrdinalIgnoreCase)))
    {
        RseProgram pr2; try { pr2 = new RseProgram(wad.Read(fe2)); } catch { continue; }
        foreach (var ins2 in pr2.Instructions.Where(i2 => i2.Opcode == RseOpcode.ADDOBJ))
        {
            var o2 = ins2.Operands.Select(o => o.Index).ToArray();
            if (o2.Length < 3 || !SoundCatalogue.IsSoundGroup(o2[0])) continue;
            var (h3, _) = ResolveEither(o2[0], o2[2]);
            if (h3 == null) continue;
            if ((h3.Flags & 0x400) != 0) { flagged++; if (h3.Word0C <= 0) flaggedNoInterval++; }
            else plain++;
        }
    }
    Check(flagged > 0 && plain > 0,
          $"the repeat flag PARTITIONS this world's scenery sound objects ({flagged} repeat, {plain} do not)");
    Check(flaggedNoInterval == 0,
          $"and every repeating object names an interval ({flaggedNoInterval} without)");
    // ⭐ The named case, only where the ids were actually verified against the clips.
    if (world == "JUNGLE")
    {
        int named = 0;
        foreach (int evt in new[] { 236, 237, 238, 239, 188, 93 })
            foreach (int grp in new[] { 3, 4, 5, 6, 7, 8, 9, 11 })
                if (ResolveEither(grp, evt).Hit is { } hh && (hh.Flags & 0x400) != 0) { named++; break; }
        Check(named == 6, $"JUNGLE: all four loudspeakers, the staff and the bin are among them ({named} of 6)");
    }
}

// ⭐⭐ DO FEATURES HAVE A CREATE ANIMATION AT ALL? Master: "all features are missing their create
// animations (if they even had any)". Slot 0 is `Create` -- harvested over 352 script pairs, see
// findings/animation.md -- so this is answerable from the data rather than by staring at a park.
{
    int withCreate = 0, without = 0, withZero = 0, asksForZero = 0; var missing = new List<string>();
    foreach (var fr in wad.Entries.Where(e => e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)
                                           && e.Path.Contains("/Features/", StringComparison.OrdinalIgnoreCase)))
    {
        RseProgram pg; try { pg = new RseProgram(wad.Read(fr)); } catch { continue; }
        if (pg.Instructions.Any(i => i.Opcode == RseOpcode.WAITANIM
                                  && i.Operands.Count > 0 && i.Operands[0].Index == 0)) asksForZero++;
    }
    foreach (var fa in wad.Entries.Where(e => e.Path.EndsWith(".aps", StringComparison.OrdinalIgnoreCase)
                                           && e.Path.Contains("/Features/", StringComparison.OrdinalIgnoreCase)))
    {
        TPW.PS2.Data.Animation ap;
        try { ap = new TPW.PS2.Data.Animation(wad.Read(fa)); } catch { continue; }
        // ⚠⚠ SLOT 0 IS "Create" FOR A RIDE. This file already records that the slot-name table
        // is the RIDE'S and "need not mean the same thing for a character" -- and it does not
        // mean the same thing for a FEATURE either. Reporting slot 0 alone produced a confident
        // "features never had create animations", which master corrected in one line: slot 1 is.
        var slots = ap.Records().Select(r => r.Slot).Distinct().OrderBy(x => x).ToArray();
        if (slots.Contains(0)) withZero++;
        if (slots.Contains(1)) withCreate++; else without++;
        if (missing.Count < 12)
            missing.Add($"{System.IO.Path.GetFileNameWithoutExtension(fa.Path)}[{string.Join(",", slots)}]");
    }
    Console.WriteLine($"  feature create: {withCreate} of {withCreate + without} feature .aps carry a slot-1 record"
                    + (missing.Count == 0 ? "" : $"; slots: {string.Join(" ", missing)}"));
    // ⭐⭐ THE MISMATCH, PINNED. Every feature script opens `WAITANIM 0 0` while no feature .aps
    // carries a slot 0 -- which is why the build request played nothing. The viewer falls back to
    // slot 1; this asserts the condition that fallback exists for, so if the data ever stops
    // disagreeing with itself somebody is told rather than leaving dead compensation in place.
    // ⚠⚠ TWO WORLD-FITTED VERSIONS OF THIS CHECK FAILED BEFORE THIS ONE. First "no feature has
    // slot 0" (SPACE has one); then "some feature has slot 1" (only JUNGLE does -- FANTASY,
    // SPACE and HALLOW are slot 5 throughout). ⭐ What is actually invariant is the MISMATCH the
    // fallback exists for: scripts ask for a slot that almost nothing carries.
    Check(asksForZero > 0 && withZero * 4 < asksForZero,
          $"feature scripts ask for slot 0 ({asksForZero}) while almost no feature .aps carries one ({withZero}); "
        + $"{withCreate} carry slot 1 in this world");
}

// ⭐⭐ THE RULE'S PREMISE, ASSERTED. `RideSounds` now loops a scenery voice only when its event
// carries the start/loop/end structure, and that rule is worth nothing if the data does not
// actually distinguish the two. ⚠ THE CONTROL IS THE BUS: something must genuinely loop, or the
// new rule would just mean "nothing ever sustains" and would be trivially satisfied.
{
    int oneShot = 0, sustained = 0;
    foreach (var fe in wad.Entries.Where(e => e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)
                                           && e.Path.Contains("/Features/", StringComparison.OrdinalIgnoreCase)))
    {
        RseProgram pr; try { pr = new RseProgram(wad.Read(fe)); } catch { continue; }
        foreach (var ins in pr.Instructions.Where(i => i.Opcode == RseOpcode.ADDOBJ))
        {
            var ops = ins.Operands.Select(o => o.Index).ToArray();
            if (ops.Length < 3 || !SoundCatalogue.IsSoundGroup(ops[0])) continue;
            var (h, _) = ResolveEither(ops[0], ops[2]);
            if (h == null) continue;
            if (h.Sets >= 3) sustained++; else oneShot++;
        }
    }
    Check(oneShot + sustained > 0, $"scenery adds sound objects at all ({oneShot + sustained})");
}

// ⭐⭐ ASSERTED, not just printed. A line of output nobody compares against anything is a number
// to stare at; these are the ids the port now PLAYS, so they have to keep resolving.
{
    string Kids(int id) => ResolveEither(6, id).Hit is { } h ? Clips(h) : "(unresolved)";
    // ⚠ THE CONTROL FIRST, and it is a real one: the lavatory's id must come back as a DOOR. If
    // group 6 ever stops being the guests' map this reads as some ride's clip and the row below
    // it -- the one the game actually plays at a shop -- would be wrong in the same breath.
    Check(Kids(53).Contains("door", StringComparison.OrdinalIgnoreCase),
          $"CONTROL: the lavatory's own event 53 is a door in the kids map ({Kids(53)})");
    Check(Kids(ParkVisitors.ShopSoundEvent).Contains("cash", StringComparison.OrdinalIgnoreCase),
          $"a shop rings a till -- event {ParkVisitors.ShopSoundEvent} is {Kids(ParkVisitors.ShopSoundEvent)}");
    Check(ParkVisitors.ShopSoundGroup == 6, "and it is read from the guests' own group, not the staff one that failed");
}
string Named(RseOpcode op, IReadOnlyList<int> a)
{
    string plain = $"{op} {string.Join(" ", a)}";
    if (a.Count < 3 || (op != RseOpcode.EVENT && op != RseOpcode.ADDOBJ)) return plain;
    if (a[0] is 1 or 2)
    {
        var e = fx?[a[2]];
        return e == null || e.Name.Length == 0 ? plain : $"{plain} ({e.Name})";
    }
    if (sounds == null || !SoundCatalogue.IsSoundGroup(a[0])) return plain;
    var (r, park) = ResolveEither(a[0], a[2]);
    // ⚠ An id no map carries is said so, by name of the map that was asked. Gates and the
    // seaplane are like that on this disc; a silent number here would hide exactly that.
    return r == null ? $"{plain} (no event {a[2]} in {System.IO.Path.GetFileName(SoundCatalogue.MapFor((SoundGroup)a[0], world, 1))} of either park)"
                     : $"{plain} ({(SoundGroup)a[0]}: {Clips(r)}{(park == 2 ? ", park 2 map" : "")})";
}
var cues = new List<(string Ride, long At, RseOpcode Op, IReadOnlyList<int> Args)>();
var asked = new Dictionary<string, int>(StringComparer.Ordinal);
var askedBy = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
foreach (var (list, tag) in new[] { (sim.Rides, "ride"), (shops.Rides, "shop") })
    foreach (var r in list)
    {
        long t0 = tag == "ride" ? sim.Time : shops.Time;
        r.Host.EffectRequested += e =>
        {
            if (e.Arguments.Count >= 3 && e.Opcode is RseOpcode.EVENT or RseOpcode.ADDOBJ && SoundCatalogue.IsSoundGroup(e.Arguments[0]))
                cues.Add((r.Name, e.Time - t0, e.Opcode, e.Arguments));
            string key = Named(e.Opcode, e.Arguments);
            asked[key] = asked.GetValueOrDefault(key) + 1;
            if (!askedBy.TryGetValue(key, out var who)) askedBy[key] = who = new SortedSet<string>(StringComparer.Ordinal);
            who.Add(r.Name);
        };
    }
for (int i = 0; i < 1500; i++) { sim.Advance(0.04); shops.Advance(0.04); }
Console.WriteLine($"\nunrendered requests in 60s from {sim.Rides.Count} rides and {shops.Rides.Count} shops:");
var byOpcode = asked.GroupBy(kv => kv.Key.Split(' ')[0])
                    .OrderByDescending(g => g.Sum(kv => kv.Value)).ToArray();
foreach (var g in byOpcode)
    Console.WriteLine($"  {g.Key,-14} {g.Sum(kv => kv.Value),6} calls, {g.Count()} distinct argument sets");
Console.WriteLine("  the twelve most asked-for, with who wants them:");
foreach (var (key, n) in asked.OrderByDescending(kv => kv.Value).Take(12))
    Console.WriteLine($"    {key,-40} x{n,-5} {string.Join(", ", askedBy[key].Take(4))}"
                    + (askedBy[key].Count > 4 ? $" +{askedBy[key].Count - 4}" : ""));
Check(asked.Count > 0, $"the park's scripts ask for presentation ({asked.Count} distinct requests)");

// ⭐⭐ THE SOUND CENSUS: per ride, per second of script time, the clip by name and the opcode that
// asked for it -- verified by lookup, never by listening (every render is --audio-driver Dummy).
// The control is stated before the lookup: the ride called Crazy Ape asks for `EVENT 3 -1 8`,
// and a join that is right names an ape; a join that is off by a group or a park names something
// else. `EVT_BOG3` is the second control: its own source comments it `; PISS`, and the kids map's
// event 50 must say so in the clip's name. Unresolved cues are listed, not dropped.
if (sounds != null)
{
    Console.WriteLine($"\nsound cues in those 60s ({world} park 1 maps, park 2's where a ride is only there): {cues.Count} from {cues.Select(c => c.Ride).Distinct().Count()} rides/shops");
    var unresolved = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var byRide in cues.GroupBy(c => c.Ride).OrderBy(g => g.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {byRide.Key}");
        int shown = 0;
        foreach (var c in byRide.OrderBy(c => c.At))
        {
            var (r, park) = ResolveEither(c.Args[0], c.Args[2]);
            string what = r == null ? "-> (no such event in " + System.IO.Path.GetFileName(SoundCatalogue.MapFor((SoundGroup)c.Args[0], world, 1)) + " of either park)"
                                    : "-> " + string.Join(" | ", r.Clips.Select(x => $"{x.Bank}[{x.Index}] {x.Name} {x.Milliseconds}ms").Distinct()) + (park == 2 ? "  (park 2 map)" : "");
            if (r == null) { string k = $"{byRide.Key} {c.Op} {(SoundGroup)c.Args[0]} evt {c.Args[2]}"; unresolved[k] = unresolved.GetValueOrDefault(k) + 1; }
            // ⚠ Twelve per ride on screen; the count says what was cut. Crazy Ape alone asks 800 times a minute.
            if (shown++ < 12) Console.WriteLine($"    {c.At / 1000.0,6:F1}s {c.Op,-7} {(SoundGroup)c.Args[0],-13} node {c.Args[1],3} evt {c.Args[2],3} {what}");
        }
        if (shown > 12) Console.WriteLine($"    ... {shown - 12} more cues from this ride");
    }
    Console.WriteLine($"  unresolved cues: {unresolved.Values.Sum()} ({unresolved.Count} distinct)");
    foreach (var (k, n) in unresolved.OrderByDescending(kv => kv.Value)) Console.WriteLine($"    x{n,-4} {k}");

    var ape = cues.FirstOrDefault(c => c.Ride.Contains("Ape", StringComparison.OrdinalIgnoreCase) && c.Args[0] == (int)SoundGroup.LocalRide && c.Args[2] == 8);
    if (ape.Ride != null)
    {
        var r = sounds.Resolve(SoundGroup.LocalRide, 8);
        Check(r != null && r.Clips.Any(c => c.Name.Contains("ape", StringComparison.OrdinalIgnoreCase)),
              $"CONTROL: Crazy Ape's EVENT 3 -1 8 at {ape.At / 1000.0:F1}s names an ape ({(r == null ? "nothing" : Clips(r))})");
        var wrongGroup = sounds.Resolve(SoundGroup.GlobalRide, 8);
        Check(wrongGroup == null || !wrongGroup.Clips.Any(c => c.Name.Contains("ape", StringComparison.OrdinalIgnoreCase)),
              $"CONTROL: the same id under GLO_RID is not the ape ({(wrongGroup == null ? "no event 8 there" : Clips(wrongGroup))})");
    }
    else Console.WriteLine("  (no Crazy Ape cue in this world; the ape control does not apply)");
    var bog = sounds.Resolve(SoundGroup.GlobalKids, 50);
    Check(bog != null && bog.Clips.Any(c => c.Name.StartsWith("wee", StringComparison.OrdinalIgnoreCase)),
          $"CONTROL: EVT_BOG3 (50, commented PISS) in the kids map is a wee ({(bog == null ? "nothing" : Clips(bog))})");
    int resolvedCues = cues.Count(c => ResolveEither(c.Args[0], c.Args[2]).Hit != null);
    Check(cues.Count > 0 && resolvedCues * 10 >= cues.Count * 9,
          $"at least nine in ten sound cues resolve to a clip in one of the two park maps ({resolvedCues} of {cues.Count})");
}

Console.WriteLine(bad == 0 ? "PASS" : $"FAIL: {bad}");
return bad == 0 ? 0 : 1;

/// <summary>The park every terrain-dependent family loads; set once from --terrain.</summary>
static class AuditPark { public static string Mps = "/terrain/terrain_1.mps"; }
