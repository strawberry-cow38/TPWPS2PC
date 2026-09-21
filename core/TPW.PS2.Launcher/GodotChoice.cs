namespace TPW.PS2.Launcher;

/// <summary>Which Godot binary to launch, and whether the one asked for was actually found.</summary>
public readonly record struct GodotChoice(string Path, bool Satisfied)
{
    public bool Found => Path != null;
}

public static class GodotLocator
{
    /// <summary>⚠ The engine VERSION must match the SDK the project was built against. A 4.6-stable
    /// binary running a project built on Godot.NET.Sdk 4.6.2 loads no C# at all and simply shows an
    /// empty window -- which reads as "my code is broken" rather than "wrong engine".</summary>
    public const string RequiredVersion = "4.6.2";

    public static readonly string[] Probes =
    {
        @"C:\ProgramData\chocolatey\lib\godot-mono\tools\Godot_v4.6.2-stable_mono_win64",
        @"C:\Program Files\Godot",
        @"C:\godot",
    };

    /// <summary>Prefer the console build when <paramref name="console"/> is asked for, but say so
    /// honestly when only the other one exists.</summary>
    public static GodotChoice Find(bool console, IEnumerable<string> extraProbes = null)
    {
        foreach (var dir in (extraProbes ?? Array.Empty<string>()).Concat(Probes))
        {
            if (!Directory.Exists(dir)) continue;
            var exes = Directory.EnumerateFiles(dir, "*.exe")
                                .Where(f => Path.GetFileName(f).StartsWith("Godot", StringComparison.OrdinalIgnoreCase))
                                .ToList();
            string want = exes.FirstOrDefault(f => f.Contains("console", StringComparison.OrdinalIgnoreCase) == console);
            string other = exes.FirstOrDefault(f => f != want);
            if (want != null) return new GodotChoice(want, true);
            if (other != null) return new GodotChoice(other, false);
        }
        return new GodotChoice(null, false);
    }
}
