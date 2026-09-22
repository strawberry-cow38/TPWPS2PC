using System.Security.Cryptography;
using System.Text.Json;
using TPW.PS2.Data;

// Build this same instrument against the historical Data project to capture the real old
// decoder. Snapshots belong OUTSIDE the repository. The comparison never starts a process.
if (args.Length is < 3 or > 4 || args[0] is not ("capture" or "compare"))
{
    Console.Error.WriteLine("usage: SshDiff capture|compare <pairs-directory> <external-snapshot-directory> [--perturb]");
    return 1;
}
bool capture = args[0] == "capture", perturb = args.Length == 4 && args[3] == "--perturb";
if (args.Length == 4 && !perturb) throw new ArgumentException("Unknown option.");
string root = Path.GetFullPath(args[1]), snapshots = Path.GetFullPath(args[2]);
for (var parent = new DirectoryInfo(snapshots); parent != null; parent = parent.Parent)
    if (File.Exists(Path.Combine(parent.FullName, ".git")) || Directory.Exists(Path.Combine(parent.FullName, ".git")))
        throw new ArgumentException("Snapshots must be outside every Git checkout.");
var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
string Key(string p) => Path.ChangeExtension(Path.GetRelativePath(root, p), null);
var partners = files.Where(p => Path.GetExtension(p).Equals(".tga", StringComparison.OrdinalIgnoreCase))
    .GroupBy(Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
var pairs = files.Where(p => Path.GetExtension(p).Equals(".ssh", StringComparison.OrdinalIgnoreCase) &&
    partners.TryGetValue(Key(p), out int count) && count == 1)
    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.Ordinal).ToArray();
if (pairs.Length == 0) throw new InvalidDataException("No scoreable pairs.");
string manifest = Path.Combine(snapshots, "manifest.json");
if (capture && Directory.Exists(snapshots)) throw new IOException("Use a new snapshot directory for each capture.");
var rows = capture ? new List<Row>() : JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(manifest))!;
if (capture) Directory.CreateDirectory(snapshots);
else if (!rows.Select(r => r.File).SequenceEqual(pairs.Select(p => Path.GetRelativePath(root, p))))
    throw new InvalidDataException("Snapshot population differs from the current case-insensitive pairs.");
int decoded = 0, exact = 0, different = 0, sameErrors = 0, added = 0, failed = 0, worst = -1;
string worstDescription = "none";
bool perturbed = false;
for (int index = 0; index < pairs.Length; index++)
{
    string file = Path.GetRelativePath(root, pairs[index]);
    byte[] source = File.ReadAllBytes(pairs[index]);
    string hash = Convert.ToHexString(SHA256.HashData(source));
    if (!capture && hash != rows[index].Sha256) throw new InvalidDataException($"Source changed: {file}");
    Ssh? image = null;
    string? error = null;
    try { image = new Ssh(source); }
    catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
    if (image != null) decoded++;
    string rgbaPath = Path.Combine(snapshots, index + ".rgba");
    if (capture)
    {
        rows.Add(new Row(file, hash, image?.Width ?? 0, image?.Height ?? 0, error));
        if (image != null) File.WriteAllBytes(rgbaPath, image.Pixels);
        continue;
    }
    var old = rows[index];
    if (old.Error != null)
    {
        if (error == old.Error) sameErrors++;
        else if (image != null && source[Ssh.ReadEntries(source)[0].Offset] == 2)
        {
            added++;
            Console.WriteLine($"ADDED type 0x02 {file}; old={old.Error}");
        }
        else { failed++; Console.WriteLine($"ERROR CHANGED {file}; old={old.Error}; new={error}"); }
        continue;
    }
    if (image == null || image.Width != old.Width || image.Height != old.Height)
    {
        failed++; Console.WriteLine($"DECODE/DIMENSION FAILURE {file}: {error}"); continue;
    }
    byte[] expected = File.ReadAllBytes(rgbaPath), actual = image.Pixels;
    if (actual.Length != expected.Length) throw new InvalidDataException($"RGBA length differs: {file}");
    if (perturb && !perturbed) { actual[0] ^= 1; perturbed = true; }
    int first = -1, max = 0, maxOffset = 0, countDifferent = 0;
    for (int i = 0; i < actual.Length; i++)
    {
        int delta = Math.Abs(actual[i] - expected[i]);
        if (delta == 0) continue;
        if (first < 0) first = i;
        countDifferent++;
        if (delta > max) { max = delta; maxOffset = i; }
    }
    if (first < 0) { exact++; continue; }
    different++;
    string description = $"{file}: different RGBA bytes {countDifferent} of {actual.Length}; " +
        $"first offset={first} reference={expected[first]} managed={actual[first]}; " +
        $"max offset={maxOffset} reference={expected[maxOffset]} managed={actual[maxOffset]} delta={max}";
    Console.WriteLine("DIFFERENT " + description);
    if (max > worst) { worst = max; worstDescription = description; }
}
if (capture) File.WriteAllText(manifest, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{(capture ? "CAPTURE" : "COMPARE")}: decoded {decoded} of {pairs.Length} scoreable pairs.");
if (capture) return 0;
int referenceDecoded = rows.Count(r => r.Error == null);
Console.WriteLine($"RGBA byte-identical {exact} of {pairs.Length} scoreable pairs; {exact} of {referenceDecoded} reference-decodable pairs.");
Console.WriteLine($"Different {different} of {referenceDecoded}; unchanged errors {sameErrors} of {pairs.Length}; " +
    $"new type 0x02 decodes {added} of {pairs.Length}; unexpected failures {failed} of {pairs.Length}.");
Console.WriteLine("Worst difference (zero-based RGBA byte offsets): " + worstDescription);
return different == 0 && failed == 0 && exact == referenceDecoded ? 0 : 2;

record Row(string File, string Sha256, int Width, int Height, string? Error);
