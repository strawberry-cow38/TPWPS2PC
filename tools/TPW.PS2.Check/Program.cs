using TPW.PS2.Data;
using TPW.PS2.Launcher;

// A self-test that runs the readers against a real disc and reports numbers that can FAIL.
// Every figure it prints was validated in Python first; this proves the C# port agrees.
if (args.Length < 1) { Console.WriteLine("usage: tpwps2check <disc.bin>"); return 1; }

using var disc = new Disc(args[0]);
var files = disc.Files();
Console.WriteLine($"disc: {files.Count} entries");

var wadEntry = files.First(f => f.Path.EndsWith("JUNGLE.WAD", StringComparison.OrdinalIgnoreCase));
var wad = new WadArchive(disc.Read(wadEntry.Extent, wadEntry.Size));
Console.WriteLine($"JUNGLE.WAD: {wad.Entries.Count} entries, {wad.Entries.Count(e => e.IsRaw)} stored raw");

int ok = 0, bad = 0;
foreach (var e in wad.Entries)
{
    try { if (wad.Read(e).Length == e.DecompressedSize) ok++; else bad++; }
    catch { bad++; }
}
Console.WriteLine($"  decompress: {ok} to their declared size, {bad} failed");

// ⭐ The format's own checksum on a geometry reader: mesh+0x62 is a face count.
int meshes = 0, faceOk = 0, animated = 0;
foreach (var e in wad.Entries.Where(x => x.Path.EndsWith(".mps", StringComparison.OrdinalIgnoreCase)))
{
    Model m;
    try { m = new Model(wad.Read(e)); } catch { continue; }
    foreach (var mesh in m.Meshes)
    {
        if (mesh.BatchCount == 0) continue;
        meshes++;
        if (m.Triangles(mesh).Count == mesh.FaceCount) faceOk++;
        if (mesh.AnimVertexList != 0) animated++;
    }
}
Console.WriteLine($"models: {meshes} meshes, {faceOk} matching their own face count " +
                  $"({100.0 * faceOk / Math.Max(meshes, 1):F2}%), {animated} animated");

int aps = 0, apsOk = 0, rot = 0, scale = 0, spline = 0, morph = 0;
foreach (var e in wad.Entries.Where(x => x.Path.EndsWith(".aps", StringComparison.OrdinalIgnoreCase)))
{
    aps++;
    try
    {
        var a = new Animation(wad.Read(e));
        foreach (var rec in a.Records())
        {
            if (rec.Skeletal) continue;
            for (int i = 0; i < rec.TrackCount; i++)
            {
                int t = a.TrackAt(rec, i);
                if (a.Rotation(t) != null) rot++;
                if (a.Scale(t) != null) scale++;
                if (a.SplinePath(t) != null) spline++;
                if (a.Morph(t) != null) morph++;
            }
        }
        apsOk++;
    }
    catch (Exception ex) { Console.WriteLine($"  FAILED {e.Path}: {ex.Message}"); }
}
Console.WriteLine($"animation: {apsOk}/{aps} parsed; channels -- rotation {rot}, scale {scale}, " +
                  $"spline {spline}, morph {morph}");

// The launcher's own rules, exercised against the same disc. These decide what the user sees
// before anything renders, so a wrong answer here is the first thing they meet.
Console.WriteLine();
var real = DiscLocator.Identify(args[0]);
Console.WriteLine($"locator, the real disc      : {real.Status} -- {real.Message}");
var dir = DiscLocator.Identify(Path.GetDirectoryName(args[0]));
Console.WriteLine($"locator, its folder         : {dir.Status} -- {dir.Message}");
var missing = DiscLocator.Identify(@"Z:\nope\nothing.bin");
Console.WriteLine($"locator, a path that is not : {missing.Status} -- {missing.Message}");
var notdisc = DiscLocator.Identify(System.Reflection.Assembly.GetEntryAssembly().Location);
Console.WriteLine($"locator, a file that is not : {notdisc.Status} -- {notdisc.Message}");
var g = GodotLocator.Find(console: true);
Console.WriteLine($"godot                       : found={g.Found} satisfied={g.Satisfied} {g.Path}");
return 0;
