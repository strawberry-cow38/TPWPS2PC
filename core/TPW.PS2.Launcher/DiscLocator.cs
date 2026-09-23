using TPW.PS2.Data;

namespace TPW.PS2.Launcher;

public enum DiscStatus { None, NotFound, NotADisc, Ok }

public readonly record struct DiscResult(DiscStatus Status, string Path, string Message, int FileCount)
{
    public bool CanLaunch => Status == DiscStatus.Ok;
}

/// <summary>Finds and identifies the user's own Theme Park World disc.
///
/// ⚠ **Bring your own disc.** Nothing is shipped and nothing is downloaded: the viewer reads an
/// image the user already has. A ROM site is not an authorised source no matter who owns the disc.
///
/// The check is deliberately structural rather than a hash. A PS2 rip is legitimately several
/// shapes -- `.bin`/`.cue` pairs, `.iso`, different rippers -- and demanding one hash would reject
/// most real copies. What it verifies instead is that the file behaves like the game: Mode 2/2352
/// sectors, an ISO9660 tree, and `DATA/JUNGLE.WAD` inside it.</summary>
public static class DiscLocator
{
    /// <summary>Where to look when the user has not chosen. ⚠ Real paths from real machines, not a
    /// guess at a convention.</summary>
    public static readonly string[] DefaultProbes =
    {
        @"D:\ROM\ps2",
        @"C:\Games\Theme Park World",
        @"C:\TPW",
    };

    public static DiscResult Identify(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new(DiscStatus.None, null, "No disc chosen yet.", 0);
        if (Directory.Exists(path))
        {
            var hit = Directory.EnumerateFiles(path)
                .Where(f => f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => new FileInfo(f).Length)     // smallest first: a cheap probe costs less
                .FirstOrDefault(f => Identify(f).CanLaunch);
            return hit != null
                ? Identify(hit)
                : new(DiscStatus.NotFound, path, "No Theme Park World image in that folder.", 0);
        }
        if (!File.Exists(path))
            return new(DiscStatus.NotFound, path, "Nothing at that path.", 0);

        try
        {
            using var disc = new Disc(path);
            var files = disc.Files();
            bool jungle = files.Any(f => !f.IsDirectory
                && f.Path.Equals("/DATA/JUNGLE.WAD", StringComparison.OrdinalIgnoreCase));
            if (!jungle)
                return new(DiscStatus.NotADisc, path,
                           $"Readable, but no DATA/JUNGLE.WAD -- {files.Count} entries. Is this Theme Park World?",
                           files.Count);
            return new(DiscStatus.Ok, path,
                       $"Theme Park World (PS2) -- {files.Count} entries, JUNGLE.WAD present.", files.Count);
        }
        catch (Exception e)
        {
            // ⚠ Report the reason. "Not a disc" alone cannot tell a wrong file from an unreadable one.
            return new(DiscStatus.NotADisc, path, $"Could not read it as a Mode 2/2352 disc: {e.Message}", 0);
        }
    }

    public static DiscResult Probe()
    {
        foreach (var p in DefaultProbes)
        {
            var r = Identify(p);
            if (r.CanLaunch) return r;
        }
        return new(DiscStatus.None, null, "No disc found in the usual places. Use Locate…", 0);
    }
}
