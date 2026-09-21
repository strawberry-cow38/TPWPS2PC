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
int tga = 0, tgaOk = 0, tgaBad = 0, tga24 = 0, tga32 = 0, tgaRle = 0, tgaPal = 0;
int withPartial = 0, withCutout = 0;
int aps = 0, apsOk = 0, apsRecords = 0, apsSkeletal = 0, apsShared = 0;
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
        else if (ext == ".tga")
        {
            tga++;
            int kind = data.Length > 2 ? data[2] : 0, bpp = data.Length > 16 ? data[16] : 0;
            if ((kind & 8) != 0) tgaRle++;
            if ((kind & 7) == 1) tgaPal++;
            if (bpp == 24) tga24++; else if (bpp == 32) tga32++;
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
        else if (ext == ".aps")
        {
            aps++;
            try
            {
                var a = new Animation(data);
                foreach (var rec in a.Records())
                {
                    apsRecords++;
                    if (rec.Skeletal) apsSkeletal++;
                    if (rec.Shared) apsShared++;
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
                  $"({tga24} 24bpp, {tga32} 32bpp, {tgaRle} RLE, {tgaPal} paletted)");
Console.WriteLine($"  alpha: {withCutout} have clear texels, {withPartial} have PARTIAL alpha");
Console.WriteLine($"animation: {aps} .aps files, {apsOk} read, {apsRecords} records " +
                  $"({apsSkeletal} skeletal, {apsShared} whose tracks live in another file)");
if (firstFails.Count > 0)
{
    Console.WriteLine("first failures:");
    foreach (var f in firstFails) Console.WriteLine("   " + f);
}
return faceBad == 0 && tgaBad == 0 && decBad == 0 ? 0 : 2;
