using System.Globalization;
using System.Text.Json;
using TPW.PS2.Data;

// Every SSH remains in the denominator, including missing/ambiguous partners and decode errors.
// Never infer a pass from a successful process exit or a plausible-looking preview.
if (args.Length == 1 && args[0] == "--self-test") return SelfTests.Run();
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: tpwps2sshscore <pairs-directory> [--tolerance 5] [--alpha-tolerance 1] [--json report-path]");
    return 1;
}
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
double tolerance = 5, alphaTolerance = 1;
string? report = null;
try
{
    for (int i = 1; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length) throw new ArgumentException("Missing option value.");
        switch (args[i])
        {
            case "--tolerance": tolerance = double.Parse(args[i + 1]); break;
            case "--alpha-tolerance": alphaTolerance = double.Parse(args[i + 1]); break;
            case "--json": report = args[i + 1]; break;
            default: throw new ArgumentException($"Unknown option {args[i]}.");
        }
    }
    if (!double.IsFinite(tolerance) || tolerance < 0 || !double.IsFinite(alphaTolerance) || alphaTolerance < 0)
        throw new ArgumentException("Tolerances must be finite nonnegative numbers.");
    var pairs = Pairing.Find(args[0]);
    if (pairs.Count == 0) throw new ArgumentException("No SSH files found; no score is possible.");
    Console.WriteLine($"Case-insensitive partners: {pairs.Count(p => p.Error == null)} of {pairs.Count} SSH files.");
    Console.WriteLine($"PASS means per-image mean absolute RGB error <= {tolerance}, alpha error <= {alphaTolerance}; channels 0..255.");
    Console.WriteLine("RGB includes transparent pixels. Exact flat-colour results are also counted separately.");
    var results = new List<Score>();
    foreach (var pair in pairs)
    {
        var score = new Score { File = Path.GetRelativePath(args[0], pair.Ssh), Reference = pair.Tga, Error = pair.Error };
        try
        {
            if (pair.Error != null) throw new InvalidDataException(pair.Error);
            var reference = ReferenceImage.Read(pair.Tga!);
            score.Flat = Metrics.IsFlat(reference.Pixels);
            var image = new Ssh(File.ReadAllBytes(pair.Ssh));
            if (image.Width != reference.Width || image.Height != reference.Height)
                throw new InvalidDataException($"Dimensions differ: SSH {image.Width}x{image.Height}, TGA {reference.Width}x{reference.Height}.");
            (double rgb, double alpha, int maxRgb, int maxAlpha) = Metrics.Compare(image.Pixels, reference.Pixels);
            score.RgbMae = rgb; score.AlphaMae = alpha; score.MaxRgb = maxRgb; score.MaxAlpha = maxAlpha;
            score.Pass = rgb <= tolerance && alpha <= alphaTolerance;
            Console.WriteLine($"{(score.Pass ? "PASS" : "FAIL")} {score.File} RGB_MAE={rgb:F6} RGB_MAX={maxRgb} A_MAE={alpha:F6} A_MAX={maxAlpha}{(score.Flat ? (score.RgbExact ? " FLAT_EXACT_RGB" : " FLAT_NOT_EXACT_RGB") : "")}");
        }
        catch (Exception ex)
        {
            score.Error = ex.Message;
            Console.WriteLine($"ERROR {score.File}: {ex.Message}");
        }
        results.Add(score);
    }
    Summary("ALL", results);
    Summary("lowercase .tga subset", results.Where(s => Path.GetExtension(s.Reference) == ".tga").ToList());
    Summary("uppercase .TGA subset", results.Where(s => Path.GetExtension(s.Reference) == ".TGA").ToList());
    var flat = results.Where(s => s.Flat).ToList();
    Console.WriteLine($"FLAT: RGB exact {flat.Count(s => s.RgbMae == 0)} of {flat.Count}; RGBA exact {flat.Count(s => s.RgbMae == 0 && s.AlphaMae == 0)} of {flat.Count}.");
    if (report != null)
        File.WriteAllText(report, JsonSerializer.Serialize(new { tolerance, alphaTolerance, results }, new JsonSerializerOptions { WriteIndented = true }));
    return results.All(s => s.Pass) ? 0 : 2;
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

static void Summary(string name, List<Score> rows)
{
    int total = rows.Count, passed = rows.Count(s => s.Pass), decoded = rows.Count(s => s.RgbMae.HasValue);
    Console.WriteLine($"{name}: within tolerance {passed} of {total}; decoded {decoded} of {total}; " +
        $"RGBA exact {rows.Count(s => s.RgbMae == 0 && s.AlphaMae == 0)} of {total}.");
    if (decoded > 0)
        Console.WriteLine($"  Image-weighted RGB mean {rows.Where(s => s.RgbMae.HasValue).Average(s => s.RgbMae):F6} over {decoded} of {total} images; " +
            $"alpha mean {rows.Where(s => s.AlphaMae.HasValue).Average(s => s.AlphaMae):F6} over {decoded} of {total}.");
}

internal sealed class Score
{
    public required string File { get; init; }
    public string? Reference { get; init; }
    public string? Error { get; set; }
    public bool Flat { get; set; }
    public double? RgbMae { get; set; }
    public double? AlphaMae { get; set; }
    public int? MaxRgb { get; set; }
    public int? MaxAlpha { get; set; }
    public bool Pass { get; set; }
    public bool RgbExact => RgbMae == 0;
    public bool RgbaExact => RgbMae == 0 && AlphaMae == 0;
}

internal sealed record Pair(string Ssh, string? Tga, string? Error);

internal static class Pairing
{
    public static List<Pair> Find(string directory)
    {
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        // Include relative directory in the key: different rides can both have a sign.tga.
        string Key(string path) => Path.ChangeExtension(Path.GetRelativePath(directory, path), null);
        var tgas = files.Where(f => Path.GetExtension(f).Equals(".tga", StringComparison.OrdinalIgnoreCase))
            .GroupBy(Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        return files.Where(f => Path.GetExtension(f).Equals(".ssh", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ThenBy(f => f, StringComparer.Ordinal)
            .Select(f => !tgas.TryGetValue(Key(f), out var matches) ? new Pair(f, null, "Missing TGA partner.") :
                matches.Length != 1 ? new Pair(f, null, "Ambiguous case-insensitive TGA partners.") : new Pair(f, matches[0], null)).ToList();
    }
}

internal static class Metrics
{
    public static bool IsFlat(byte[] rgba)
    {
        for (int i = 4; i < rgba.Length; i += 4)
            for (int c = 0; c < 3; c++) if (rgba[i + c] != rgba[c]) return false;
        return true;
    }

    public static (double Rgb, double Alpha, int MaxRgb, int MaxAlpha) Compare(byte[] actual, byte[] expected)
    {
        if (actual.Length == 0 || actual.Length != expected.Length || actual.Length % 4 != 0)
            throw new InvalidDataException("Invalid RGBA comparison lengths.");
        long rgb = 0, alpha = 0;
        int maxRgb = 0, maxAlpha = 0;
        for (int i = 0; i < actual.Length; i++)
        {
            int error = Math.Abs(actual[i] - expected[i]);
            if (i % 4 == 3) { alpha += error; maxAlpha = Math.Max(maxAlpha, error); }
            else { rgb += error; maxRgb = Math.Max(maxRgb, error); }
        }
        return (rgb / (actual.Length / 4.0 * 3), alpha / (actual.Length / 4.0), maxRgb, maxAlpha);
    }
}
