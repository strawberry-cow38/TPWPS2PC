using TPW.PS2.Data;

// A self-test that runs the readers against a real disc and reports numbers that CAN FAIL.
//
// ⚠⚠ IT COVERS EVERY WAD, AND IT EXCLUDES NOTHING. The previous version tested JUNGLE.WAD alone
// and skipped meshes with no batches, and reported "935 / 935, 100.00%" while 198 meshes on the
// disc were coming back with no geometry at all. A self-test that filters its own input is how a
// broken reader keeps its perfect score.
if (args.Length < 1) { Console.WriteLine("usage: tpwps2check <disc.bin>"); return 1; }

using var disc = new Disc(args[0]);
var files = disc.Files();
Console.WriteLine($"disc: {files.Count} entries");

var wads = files.Where(f => !f.IsDirectory && f.Path.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase))
                .ToList();

int entries = 0, decOk = 0, decBad = 0, alias = 0;
int meshes = 0, faceOk = 0, faceBad = 0, noBatches = 0, models = 0, modelBad = 0;
int tga = 0, tgaOk = 0, tgaBad = 0, tga24 = 0, tga32 = 0, tga8 = 0, tgaRle = 0, tgaPal = 0;
int tgaPng = 0, tgaLies = 0;
int withPartial = 0, withCutout = 0;
int aps = 0, apsOk = 0, apsRecords = 0, apsSkeletal = 0, apsShared = 0;
int apsPlain = 0, apsSkelOnly = 0, apsSharedOnly = 0, apsBoth = 0;
int sam = 0, samNamed = 0, samPrintable = 0, samShape = 0, samHoarding = 0, samFields = 0;
var samTiers = new int[3];
int samChecked = 0, samControlOk = 0;
// name -> { Info.Id, Upgrades[0..2].InitCapacity }, every value READ OFF THE DISC. The capacities
// of the first five agree to the digit with what TPW-PSXPC reads out of the PSX executable; the
// last two are PS2 rebalances that deliberately DISAGREE with it, so a parser cannot pass here by
// returning PSX numbers.
// ⚠ This table's first draft had five ids written from memory rather than read. The capacities were
// measured and passed; the invented ids failed the moment the control ran. That is the whole reason
// the id is in here next to the numbers it travels with.
var RideControls = new Dictionary<string, int[]>
{
    ["Crazy Ape"]       = new[] { 1101,  8, 11, 14 },   // Monkey.sam
    ["Sun God"]         = new[] { 1106, 16, 22, 28 },   // Incagod.sam
    ["Mumbo"]           = new[] { 1109,  5,  5,  5 },   // Mumbo.sam
    ["Tom Tom Twister"] = new[] { 1113, 20, 30, 40 },   // spider.sam
    ["Aztec Mayhem"]    = new[] { 1104,  5,  8, 12 },   // TVSim.sam
    ["Chac Atak"]       = new[] { 1185,  6,  6,  6 },   // coaster1.sam  -- PS2 rebalance
    ["Jurassic Tours"]  = new[] { 1170,  9, 18, 27 },   // tourride.sam  -- PS2 rebalance
};
var extCount = new Dictionary<string, int>();
var firstFails = new List<string>();

foreach (var w in wads)
{
    WadArchive wad;
    try { wad = new WadArchive(disc.Read(w.Extent, w.Size)); }
    catch (Exception ex) { Console.WriteLine($"  {w.Path}: not a WAD ({ex.Message})"); continue; }
    foreach (var e in wad.Entries)
    {
        entries++;
        if (WadArchive.IsAlias(e)) { alias++; continue; }
        byte[] data;
        try { data = wad.Read(e); } catch { decBad++; continue; }
        if (data.Length == e.DecompressedSize) decOk++; else { decBad++; continue; }

        var ext = Path.GetExtension(e.Path).ToLowerInvariant();
        // Every entry gets counted by extension, examined or not. Three extensions are read below;
        // the rest are reported as present-but-unexamined rather than silently vanishing from the
        // totals -- a number that does not account for its whole population is not a measurement.
        extCount[ext] = extCount.GetValueOrDefault(ext) + 1;
        if (ext == ".mps")
        {
            models++;
            Model m;
            try { m = new Model(data); } catch { modelBad++; continue; }
            foreach (var mesh in m.Meshes)
            {
                meshes++;
                if (mesh.BatchCount == 0) { noBatches++; continue; }
                int t;
                try { t = m.Triangles(mesh).Count; } catch { t = -1; }
                if (t == mesh.FaceCount) faceOk++;
                else
                {
                    faceBad++;
                    if (firstFails.Count < 10)
                        firstFails.Add($"{w.Path}{e.Path} [{mesh.Name}] {t} tris vs {mesh.FaceCount} declared");
                }
            }
        }
        // ⚠ /DATA/UI.WAD/UltimateC/Star.tga is a PNG wearing a .tga extension -- 89 50 4E 47, IHDR
        // 32x32 RGBA. Counting it as a TGA that failed reports 5,694 of 5,695 and reads as a gap in
        // the decoder, when a perfect TGA decoder scores 5,694 of 5,694 and this file is simply not
        // one. Name it separately rather than letting it sit in the reject pile.
        else if (ext == ".tga" && data.Length > 8 && data[0] == 0x89
                 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G')
        {
            tgaPng++;
        }
        else if (ext == ".tga")
        {
            tga++;
            int kind = data.Length > 2 ? data[2] : 0, bpp = data.Length > 16 ? data[16] : 0;
            int tw = data.Length > 13 ? BitConverter.ToUInt16(data, 12) : 0;
            int th = data.Length > 15 ? BitConverter.ToUInt16(data, 14) : 0;
            // Count by what the body IS, not by what the header claims -- see Targa.cs. Four files
            // declare true-colour and are paletted, and a breakdown that believes them does not sum.
            bool lies = bpp == 8 && data[1] == 0 && (kind & 7) == 2
                        && data.Length - 18 - data[0] >= tw * th + 1024;
            if ((kind & 8) != 0) tgaRle++;
            if ((kind & 7) == 1 || lies) tgaPal++;
            if (lies) tgaLies++;
            if (bpp == 24) tga24++; else if (bpp == 32) tga32++; else if (bpp == 8) tga8++;
            try
            {
                var t = new Targa(data);
                tgaOk++;
                if (t.PartialAlpha > 0) withPartial++;
                if (t.ClearTexels > 0) withCutout++;
            }
            catch
            {
                tgaBad++;
                if (firstFails.Count < 10) firstFails.Add($"{w.Path}{e.Path} kind {kind} {bpp}bpp did not decode");
            }
        }
        else if (ext == ".sam")
        {
            sam++;
            var txt = System.Text.Encoding.Latin1.GetString(data);
            if (txt.All(c => c >= 32 && c < 127 || c is '\t' or '\r' or '\n')) samPrintable++;
            var def = RideDefinition.Parse(txt, w.Path + e.Path);
            samFields += def.Fields.Count;
            if (def.Name != null) samNamed++;
            if (def.Shape != null) samShape++;
            if (def.Hoarding != null) samHoarding++;
            for (int t = 0; t < 3; t++) if (def.UpgradeCapacity(t) is not null) samTiers[t]++;
            // Known answers. These are not a budget that can drift green -- the disc is fixed, and
            // every one of them was read independently: the capacities off the PSX executable by
            // TPW-PSXPC, the ids off a second extraction of this disc by tinyclaw. If the parser
            // ever stops reproducing them it has broken, and a silent reformat is the likely cause.
            if (def.Name is string rn && RideControls.TryGetValue(rn, out var want))
            {
                samChecked++;
                var got = new[] { def.Id, def.UpgradeCapacity(0), def.UpgradeCapacity(1), def.UpgradeCapacity(2) };
                if (got.SequenceEqual(want.Select(x => (int?)x))) samControlOk++;
                else if (firstFails.Count < 10)
                    firstFails.Add($"ride control {rn}: got [{string.Join(",", got)}] want [{string.Join(",", want)}]");
            }
        }
        else if (ext == ".aps")
        {
            aps++;
            try
            {
                var a = new Animation(data);
                foreach (var rec in a.Records())
                {
                    apsRecords++;
                    // Skeletal (0x20) and Shared (0x80) are independent bits, so counting each on
                    // its own names 271 of 1,451 records and leaves 1,180 in no stated category.
                    // Count the four combinations instead: they partition the population.
                    if (rec.Skeletal) apsSkeletal++;
                    if (rec.Shared) apsShared++;
                    if (rec.Skeletal && rec.Shared) apsBoth++;
                    else if (rec.Skeletal) apsSkelOnly++;
                    else if (rec.Shared) apsSharedOnly++;
                    else apsPlain++;
                    a.Length(rec);                  // must not throw on either track format
                }
                apsOk++;
            }
            catch { if (firstFails.Count < 10) firstFails.Add($"{w.Path}{e.Path} animation threw"); }
        }
    }
}

Console.WriteLine($"archives: {wads.Count} WADs, {entries} entries");
Console.WriteLine($"  decompress: {decOk} to their declared size, {decBad} failed, " +
                  $"{alias} aliases holding no bytes of their own");
Console.WriteLine($"models: {models} files ({modelBad} unreadable), {meshes} meshes");
Console.WriteLine($"  face count: {faceOk} match, {faceBad} DO NOT, {noBatches} have no batches " +
                  $"({100.0 * faceOk / Math.Max(faceOk + faceBad, 1):F2}% of those with geometry)");
Console.WriteLine($"textures: {tga} TGAs, {tgaOk} decoded, {tgaBad} REJECTED " +
                  $"({tga24} 24bpp + {tga32} 32bpp + {tga8} 8bpp = {tga24 + tga32 + tga8}; " +
                  $"{tgaRle} RLE, {tgaPal} paletted of which {tgaLies} DECLARE true-colour)");
if (tgaPng > 0)
    Console.WriteLine($"  plus {tgaPng} PNG file(s) carrying a .tga extension -- not TGAs, not failures");
Console.WriteLine($"  alpha: {withCutout} have clear texels, {withPartial} have PARTIAL alpha");
Console.WriteLine($"animation: {aps} .aps files, {apsOk} read, {apsRecords} records " +
                  $"({apsSkeletal} skeletal, {apsShared} whose tracks live in another file)");
Console.WriteLine($"  records partitioned: {apsPlain} plain + {apsSkelOnly} skeletal-only + " +
                  $"{apsSharedOnly} shared-only + {apsBoth} both = " +
                  $"{apsPlain + apsSkelOnly + apsSharedOnly + apsBoth} of {apsRecords}");

Console.WriteLine($"rides: {sam} .sam files, {samPrintable} fully printable, {samNamed} named, "
                  + $"{samShape} with a footprint, {samHoarding} with a hoarding, {samFields} fields");
Console.WriteLine($"  upgrade tiers carrying a capacity: {samTiers[0]} / {samTiers[1]} / {samTiers[2]}");
Console.WriteLine($"  known-answer controls: {samControlOk} of {samChecked} reproduce (id + 3 capacities each)");

// The localisation database, and the join that gives a ride its PLAYER-FACING name.
{
    WadArchive? dataWad = null;
    foreach (var w in wads)
        if (w.Path.EndsWith("/DATA.WAD", StringComparison.OrdinalIgnoreCase))
            try { dataWad = new WadArchive(disc.Read(w.Extent, w.Size)); } catch { }
    if (dataWad is not null)
    {
        foreach (var locale in new[] { "eur", "usa", "jap" })
        {
            var db = TextDatabase.Load(dataWad, locale);
            if (db is null) { Console.WriteLine($"text: {locale} NOT FOUND"); continue; }
            int withFmt = db.Formats.Count(x => !string.IsNullOrEmpty(x));
            int hasField = db.Formats.Count(x => x is not null);
            Console.WriteLine($"text: {locale} {db.Keys.Length} rows x {db.Languages.Count} languages "
                              + $"({string.Join(",", db.Languages.Keys.OrderBy(x => x))}); "
                              + $"{hasField} carry a format field ({withFmt} non-empty), {db.Formats.Length - hasField} none");
        }
        // The join, measured rather than assumed: Info.Name is not the displayed name.
        var eur = TextDatabase.Load(dataWad, "eur");
        if (eur is not null)
        {
            var cat2 = RideCatalogue.Load(disc);
            int joined = 0, agree = 0;
            foreach (var d in cat2.All)
            {
                var parts = d.Source.Split('/', StringSplitOptions.RemoveEmptyEntries);
                int wi = Array.FindIndex(parts, x => x.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase));
                if (wi < 0 || d.Name is null) continue;
                var world = parts[wi][..^4];
                var rel = string.Join('/', parts.Skip(wi + 1));
                int row = eur.IndexOf(TextDatabase.GraphicsKey(world, rel));
                if (row < 0) continue;
                joined++;
                if (eur.Text("eng", row) == d.Name) agree++;
            }
            Console.WriteLine($"  ride name join: {joined} rides reach a STR_GRAPHICS row; "
                              + $"{agree} match Info.Name, {joined - agree} DIFFER (the table wins)");
        }
    }
}

// The id is the identity, not the name -- and the thousands digit is the world for bands 1-4.
// Band 5 is the sideshows and spans every world, so it is counted and excluded by NAME here
// rather than being quietly dropped into the "impure" pile.
{
    var cat = RideCatalogue.Load(disc);
    var bands = new Dictionary<int, HashSet<string>>();
    foreach (var d in cat.All)
    {
        if (d.IdBand is not int b) continue;
        var world = d.Source.Split('/').FirstOrDefault(x => x.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase)) ?? "?";
        if (!bands.TryGetValue(b, out var set)) bands[b] = set = new HashSet<string>();
        set.Add(world);
    }
    int pure = bands.Count(kv => kv.Key != 5 && kv.Value.Count == 1);
    Console.WriteLine($"  catalogue: {cat.All.Count} rides, {cat.ById.Count} distinct ids, "
                      + $"{cat.Unnumbered.Count} unnumbered, {cat.IdCollisions.Count} id collisions; "
                      + $"{cat.All.Count(d => d.ModelPath != null)} with a model, "
                      + $"{cat.All.Count(d => d.ModelAmbiguous)} ambiguous, "
                      + $"{cat.All.Count(d => d.ModelPath == null && !d.ModelAmbiguous)} script-only; "
                      + $"id bands 1-4 pure in {pure} of 4, "
                      + $"band 5 (sideshows) spans {(bands.TryGetValue(5, out var s5) ? s5.Count : 0)} worlds");
}

// ⭐⭐ EVERY PARK MUST RESOLVE AN ENTRANCE, and this check exists because two did not and
// nothing noticed. A park whose entrance comes back empty has no way in: the sim spawns its
// guests at (0,0) and falls over. Both faults were invisible to every other audit, which tested
// JUNGLE and FANTASY and found them healthy.
//
//  * HALLOW t1 and SPACE t1 -- mine, at f2ef76e. Adding `PathRows` as a fifth positional
//    component of ParkEntranceEntry pulled it into the record's equality, so table entries 3 and
//    9 (same walkway, different starting-path length) stopped comparing equal and Fit called a
//    repeat an ambiguity.
//  * SPACE t2 -- older, and honest: entries 7 and 10 genuinely both fit its grid. The park's own
//    flagpoles break the tie.
//
// The check is cheap, it covers all eight parks, and a miss here is a crash there.
int parks = 0, parkless = 0;
{
    ParkEntrance entranceTable = null;
    try { entranceTable = ParkEntrance.Read(disc); }
    catch (Exception e) { Console.WriteLine($"entrance table: {e.Message}"); }
    if (entranceTable != null)
        foreach (var w in wads)
        {
            WadArchive wad2 = null;
            try { wad2 = new WadArchive(disc.Read(w.Extent, w.Size)); } catch { continue; }
            foreach (var t in wad2.Entries.Where(e => !WadArchive.IsAlias(e)
                         && e.Path.Contains("/terrain/terrain_", StringComparison.OrdinalIgnoreCase)
                         && e.Path.EndsWith(".mps", StringComparison.OrdinalIgnoreCase)))
            {
                Model terrain;
                try { terrain = new Model(wad2.Read(t)); } catch { continue; }
                if (terrain.Field == null) continue;
                parks++;
                var fitted = entranceTable.Fit(terrain.Field,
                    ParkEntrance.WalkwayColumnFromPoles(terrain), out string why);
                if (!fitted.Empty && fitted.Cells().Any()) continue;
                parkless++;
                Console.WriteLine($"   NO ENTRANCE  {w.Path.Split('/')[^1]} {t.Path.Split('/')[^1]}: {why}");
            }
        }
    Console.WriteLine($"entrances: {parks - parkless} of {parks} parks resolve one"
                      + (parkless == 0 ? "" : $"  <-- {parkless} WITH NO WAY IN"));
}

// ⭐⭐ THE VISITORS' WANTS, checked by their SHAPE and not by their code. Every number below is
// the console's, off `FUN_0020BCD0` and the two roll helpers, and each check is written so that
// using the WRONG one fails it -- the three distributions are easy to swap and impossible to tell
// apart from a mean alone.
int needsBad = 0;
{
    var needs = new VisitorNeeds(seed: 12345);
    const int N = 40000;
    var w = new VisitorWants[N];
    for (int i = 0; i < N; i++) w[i] = needs.Spawn(i);
    void Need(bool ok, string what) { if (!ok) { needsBad++; Console.WriteLine("   NEEDS FAIL  " + what); } }

    Need(w.All(v => v.Happiness == 50), "happiness spawns at exactly 50 -- the only need seeded to a constant");
    Need(w.All(v => v.Hunger < 70), "hunger spawns in [0,69] (rand(70))");
    Need(w.All(v => v.Cash >= 2000 && v.Cash <= 4990), "cash spawns in [2000,4990] ((rand(300)+200)*10)");

    // ⭐ THE CONTROL THAT REJECTS A FLAT ROLL. The toilet and thirst are seeded
    // `rand(100)*rand(100)/100`, which piles up near zero: about 31% land under 10, where a flat
    // rand(100) would put 10% there. A check on the MEAN alone would pass either.
    double lowToilet = w.Count(v => v.Toilet < 10) / (double)N;
    double lowThirst = w.Count(v => v.Thirst < 10) / (double)N;
    Need(lowToilet > 0.25 && lowToilet < 0.40, $"the toilet spawns biased LOW: {lowToilet:P1} under 10, flat would be 10%");
    Need(lowThirst > 0.25 && lowThirst < 0.40, $"thirst spawns biased LOW: {lowThirst:P1} under 10, flat would be 10%");
    double lowHunger = w.Count(v => v.Hunger < 7) / (double)N;
    Need(lowHunger > 0.07 && lowHunger < 0.13, $"hunger is FLAT, not biased: {lowHunger:P1} in its lowest tenth");

    // ⚠ The high roll is the one a hand disassembly got backwards. `n - rand(n)*rand(n)/n`
    // clusters near n; if it were ever replaced by the product itself the mean would invert.
    double high = Enumerable.Range(0, N).Average(_ => needs.RollHigh(100));
    double centred = Enumerable.Range(0, N).Average(_ => needs.RollCentred(100));
    double centredTail = Enumerable.Range(0, N).Count(_ => needs.RollCentred(100) < 10) / (double)N;
    Need(high > 70 && high < 80, $"the HIGH roll clusters near n: mean {high:F1} of 100");
    Need(centred > 45 && centred < 55, $"the CENTRED roll sits mid-range: mean {centred:F1} of 100");
    Need(centredTail < 0.04, $"and it is TRIANGULAR, not flat: {centredTail:P1} under 10, flat would be 10%");

    // Eating fills the bladder -- the arithmetic that identified +0x79 in the first place.
    var one = needs.Spawn(N + 1);
    needs.Set(N + 1, one with { Hunger = 80, Toilet = 10, Thirst = 80, Happiness = 50, Sick = 0 });
    needs.Buy(N + 1, price: 30, hungerReduction: 25, thirstReduction: 0, happinessEffect: 5, vomitIncrease: 15);
    var after = needs.Of(N + 1);
    Need(after.Hunger == 55 && after.Toilet == 35 && after.Happiness == 55 && after.Sick == 15,
         $"a burger feeds AND fills the bladder: hunger {after.Hunger}, toilet {after.Toilet}, "
         + $"happy {after.Happiness}, sick {after.Sick}");

    // ⚠⚠ THE RISE MUST FOLLOW THE CLOCK, NOT THE CALL COUNT, and this check exists because the
    // first version did not: astraclaw's independent test took hunger 10 -> 35 at 25 Hz and
    // 10 -> 60 at 50 Hz off the same simulated second. A zero-spread rate makes one rise worth
    // exactly +1, so the two step sizes below must land on the SAME number or the clock is wrong.
    int Advance(double total, double step)
    {
        var n = new VisitorNeeds(seed: 7);
        n.Rates["hunger"] = new VisitorNeeds.Rate(1, 0, High: false);
        n.Spawn(0);
        n.Set(0, n.Of(0) with { Hunger = 0 });
        // ⚠ A zero step would loop forever, and the first version of this check DID -- it hung
        // the whole self-test. Ask it once and let Step decide, which is the thing being tested.
        if (step <= 0d) n.Step(0d);
        else for (double t = 0; t < total - 1e-9; t += step) n.Step(step);
        return n.Of(0).Hunger;
    }
    int coarse = Advance(25.6, 25.6), fine = Advance(25.6, 0.01), idle = Advance(25.6, 0.0);
    // ⚠ ±1, not exact. Summing 0.01 two and a half thousand times lands a hair under 25.6 in
    // binary floating point, so a total sitting exactly on a rise boundary can fall either side of
    // it. That is an epsilon, and it is not what this check is for: the bug it rejects turned one
    // simulated second into 25 rises or 60 depending on the frame rate, and shows up here as 0
    // against 40.
    Need(Math.Abs(coarse - fine) <= 1,
         $"the rise follows the CLOCK: 25.6s in one step gives {coarse}, in 2560 steps {fine}");
    Need(coarse == 10, $"and 25.6s at 2.56s a rise is 10 rises: got {coarse}");
    Need(idle == 0, $"a zero-time step ages nobody: got {idle}");

    // Ids are reused; a stale entry would hand the next arrival a dead stranger's hunger.
    int forgotten = needs.Reconcile(Enumerable.Range(0, 10));
    Need(forgotten == N - 9 && needs.All.Count == 10, $"Reconcile forgets the retired: dropped {forgotten}, kept {needs.All.Count}");

    Console.WriteLine($"visitor needs: {(needsBad == 0 ? "all spawn/roll/purchase checks pass" : $"{needsBad} FAILED")}");
}

// Every archive entry, named. The readers above cover three extensions; the rest are present and
// unexamined, and saying so is the difference between a known gap and an invisible one.
var examined = new[] { ".mps", ".tga", ".aps", ".sam" };
int seen = extCount.Values.Sum();
Console.WriteLine($"entry census: {seen} entries + {alias} aliases + {decBad} unreadable = " +
                  $"{seen + alias + decBad} of {entries}");
foreach (var kv in extCount.OrderByDescending(k => k.Value))
    Console.WriteLine($"   {(examined.Contains(kv.Key) ? "read " : "     ")}{kv.Key,-8} {kv.Value,6}");
if (firstFails.Count > 0)
{
    Console.WriteLine("first failures:");
    foreach (var f in firstFails) Console.WriteLine("   " + f);
}
return faceBad == 0 && tgaBad == 0 && decBad == 0 && samControlOk == samChecked
       && parkless == 0 && needsBad == 0 ? 0 : 2;
