namespace TPW.PS2.Launcher;

/// <summary>A probed engine choice. Satisfied means the requested console variant was found;
/// ReportedVersion is capability evidence, not a guarantee that every project API will load.</summary>
public readonly record struct GodotChoice(string Path, bool Satisfied, string ReportedVersion = null)
{
    public bool Found => Path != null;
}

public static class GodotLocator
{
    /// <summary>The project's current SDK target. Discovery checks Godot 4/.NET capability;
    /// it does not invent an exact patch-version compatibility gate.</summary>
    public const string RequiredVersion = "4.6.2";
    public const int MaxVersionProbes = 16;

    public static readonly string[] Probes =
    {
        @"C:\ProgramData\chocolatey\lib\godot-mono\tools\Godot_v4.6.2-stable_mono_win64",
        @"C:\Program Files\Godot",
        @"C:\godot",
    };

    /// <summary>Godot's --version reports its build capabilities; mono is appended by the
    /// engine's mono module. A filename alone cannot distinguish the standard and .NET builds.
    /// This checks advertised capability, not executable authenticity or full API compatibility.</summary>
    public static bool ReportsManagedGodot4(string raw)
    {
        string text = (raw ?? "").Trim();
        if (text.Length > 256 || text.Contains('\n') || text.Contains('\r')) return false;
        var parts = text.Split('.');
        static bool Number(string value) => value.Length > 0 && value.All(char.IsAsciiDigit)
            && int.TryParse(value, out _);
        if (parts.Length < 4 || parts.Any(p => p.Length == 0) || parts[0] != "4" || !Number(parts[1])) return false;
        int statusIndex = Number(parts[2]) ? 3 : 2;
        if (parts.Length <= statusIndex + 1) return false;
        string status = parts[statusIndex];
        bool knownStage = new[] { "stable", "dev", "alpha", "beta", "rc" }.Any(stage =>
            status.StartsWith(stage, StringComparison.OrdinalIgnoreCase)
            && status[stage.Length..].All(char.IsAsciiDigit));
        return knownStage && parts.Skip(statusIndex + 1).Any(p => p.Equals("mono", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Find an engine only after a bounded caller-supplied --version probe. Failures
    /// in one directory/candidate do not mask a working installation elsewhere. Default paths
    /// remain Windows paths; other environments can supply explicit probe directories.</summary>
    public static async Task<GodotChoice> FindAsync(bool console, Func<string, Task<string>> readVersion,
        IEnumerable<string> extraProbes = null, bool includeDefaultProbes = true, Action<string> report = null)
    {
        ArgumentNullException.ThrowIfNull(readVersion);
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var directories = (extraProbes ?? Array.Empty<string>()).Concat(includeDefaultProbes ? Probes : Array.Empty<string>());
        GodotChoice fallback = default; int attempted = 0;
        foreach (string dir in directories)
        {
            string[] candidates;
            try
            {
                if (!Directory.Exists(dir)) continue;
                candidates = Directory.EnumerateFiles(dir)
                    .Where(f => Path.GetExtension(f).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                             && Path.GetFileName(f).StartsWith("Godot", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => IsConsole(f) == console ? 0 : 1)
                    .ThenBy(f => f, StringComparer.Ordinal).ToArray();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                report?.Invoke($"could not inspect engine directory: {e.Message}"); continue;
            }
            foreach (string path in candidates)
            {
                if (!seen.Add(Path.GetFullPath(path))) continue;
                if (attempted++ >= MaxVersionProbes)
                {
                    report?.Invoke($"engine discovery reached its {MaxVersionProbes}-candidate probe limit; later candidates were not checked");
                    return fallback;
                }
                report?.Invoke($"probing engine capability: {Path.GetFileName(path)}");
                string version;
                try { version = await readVersion(path); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException)
                {
                    report?.Invoke($"engine probe failed for {Path.GetFileName(path)}: {e.Message}"); continue;
                }
                if (!ReportsManagedGodot4(version))
                {
                    report?.Invoke($"skipping {Path.GetFileName(path)}: no usable Godot 4 .NET version response"); continue;
                }
                var choice = new GodotChoice(path, IsConsole(path) == console, version.Trim());
                if (choice.Satisfied) return choice;
                if (!fallback.Found) fallback = choice;
            }
        }
        return fallback;
    }

    static bool IsConsole(string path) => Path.GetFileNameWithoutExtension(path)
        .EndsWith("_console", StringComparison.OrdinalIgnoreCase);
}
