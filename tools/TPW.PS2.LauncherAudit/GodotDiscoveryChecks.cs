using TPW.PS2.Launcher;

static class GodotDiscoveryChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        void Check(bool ok, string message) => check(ok, "engine discovery: " + message);
        foreach (string good in new[] { "4.6.stable.mono.official.89cea1439", "4.6.2.stable.mono.official.hash", "4.7.dev.mono.custom", "4.6.2.rc1.mono.custom" })
            Check(GodotLocator.ReportsManagedGodot4(good), "recognizes advertised Godot 4 .NET capability: " + good);
        foreach (string bad in new[] { "", "4.6.stable.official.hash", "4.6.stable.notmono.hash", "3.6.stable.mono.hash",
            "Godot 4.6 mono", "4.x.stable.mono.hash", "4.6.stable.mono.hash\nextra output",
            "4.6..mono", "4.6.garbage.mono", "4.+6.stable.mono.hash", "4. 6.stable.mono.hash" })
            Check(!GodotLocator.ReportsManagedGodot4(bad), "rejects absent/standard/wrong-generation/malformed capability response");
        string root = Path.Combine(Path.GetTempPath(), "tpw-godot-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string early = Path.Combine(root, "console-directory-name"), later = Path.Combine(root, "later");
            Directory.CreateDirectory(early); Directory.CreateDirectory(later);
            string standard = Path.Combine(early, "Godot_00.exe"), managed = Path.Combine(early, "Godot_01.exe");
            string console = Path.Combine(early, "Godot_02_console.exe"), other = Path.Combine(later, "Godot_03.EXE");
            foreach (string p in new[] { standard, managed, console, other }) File.WriteAllText(p, "not an executable; probe is faked");
            var versions = new Dictionary<string, string>
            {
                [standard] = "4.6.stable.official.hash", [managed] = "4.6.stable.mono.official.hash",
                [console] = "4.6.stable.mono.official.hash", [other] = "4.6.2.stable.mono.official.hash",
            };
            var seen = new List<string>();
            Task<string> Probe(string p) { seen.Add(p); return Task.FromResult(versions[p]); }
            var choice = await GodotLocator.FindAsync(false, Probe, new[] { early }, false);
            Check(choice.Path == managed && choice.Satisfied, "rejects earlier standard engine and ignores console in directory names");
            choice = await GodotLocator.FindAsync(true, Probe, new[] { early }, false);
            Check(choice.Path == console && choice.Satisfied, "prefers the requested console executable");
            versions[managed] = null;
            choice = await GodotLocator.FindAsync(false, Probe, new[] { early }, false);
            Check(choice.Path == console && !choice.Satisfied, "validates and honestly reports console-only fallback");
            choice = await GodotLocator.FindAsync(false, Probe, new[] { early, later }, false);
            Check(choice.Path == other && choice.Satisfied, "preferred validated variant in later directory beats fallback");
            versions[console] = "standard build";
            choice = await GodotLocator.FindAsync(false, Probe, new[] { early }, false);
            Check(!choice.Found, "no valid advertised .NET engine means no launch candidate");
            Task<string> FailedProbe(string p) => p == standard ? throw new TimeoutException("synthetic timeout") : Probe(p);
            choice = await GodotLocator.FindAsync(false, FailedProbe, new[] { early, later }, false);
            Check(choice.Path == other, "one failed/timed-out candidate cannot hide another installation");
            seen.Clear();
            await GodotLocator.FindAsync(false, Probe, new[] { early, early }, false);
            Check(seen.Count == seen.Distinct().Count(), "duplicate probe directories do not execute candidates twice");
            for (int i = 0; i < 30; i++) File.WriteAllText(Path.Combine(later, $"Godot_{i:000}.exe"), "synthetic");
            int probes = 0;
            await GodotLocator.FindAsync(false, _ => { probes++; return Task.FromResult<string>(null); }, new[] { later }, false);
            Check(probes == GodotLocator.MaxVersionProbes, "candidate executions have a finite upper bound");
            versions[console] = "4.6.stable.mono.official.hash";
            var reports = new List<string>();
            choice = await GodotLocator.FindAsync(false, p => Task.FromResult(versions.GetValueOrDefault(p)),
                new[] { early, later }, false, reports.Add);
            Check(choice.Path == console && !choice.Satisfied && reports.Any(r => r.Contains("probe limit")),
                  "probe-limit exhaustion reports incomplete discovery and preserves validated fallback metadata");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
