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
return faceBad == 0 && tgaBad == 0 && decBad == 0 && samControlOk == samChecked ? 0 : 2;
