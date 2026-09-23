using TPW.PS2.Launcher;

if (args.FirstOrDefault() == "--process-fixture")
{
    switch (args.ElementAtOrDefault(1))
    {
        case "echo": Console.WriteLine(args.ElementAtOrDefault(2)); Console.Error.WriteLine("stderr marker"); return 0;
        case "fail": Console.Error.WriteLine("error: deliberate fixture failure"); return 17;
        case "sleep": Console.WriteLine("before timeout"); Console.Out.Flush(); await Task.Delay(10000); return 0;
        case "noisy":
            for (int i = 0; i < 64; i++) { Console.Write(new string('o', 4096)); Console.Error.Write(new string('e', 4096)); }
            Console.Write("stdout tail"); Console.Error.Write("error: stderr tail"); return 0;
        default: return 99;
    }
}

int bad = 0;
void Check(bool ok, string message) { Console.WriteLine((ok ? "ok   " : "FAIL ") + message); if (!ok) bad++; }
var exe = new byte[SelfUpdate.MinPlausibleBytes]; exe[0] = (byte)'M'; exe[1] = (byte)'Z';
foreach (string hash in new[] { "a", "abcd", new string('x', 64), new string('a', 63), new string('a', 65) })
{
    try { Check(!SelfUpdate.Check(exe, hash).Accept, $"malformed hash length {hash.Length} is rejected without throwing"); }
    catch (Exception e) { Check(false, $"malformed hash length {hash.Length} threw {e.GetType().Name}"); }
}
Check(SelfUpdate.Check(exe, SelfUpdate.Sha256Of(exe).ToLowerInvariant()).Accept, "matching hash is accepted case-insensitively");
Check(!SelfUpdate.Check(exe, new string('0', 64)).Accept, "well-formed mismatching hash is rejected");
Check(SelfUpdate.Check(exe, null).Accept, "existing explicit shape-only policy for absent hash remains unchanged");
Check(!SelfUpdate.Check(Array.Empty<byte>(), null).Accept, "empty download rejected");
foreach (var bytes in new[] { Array.Empty<byte>(), new byte[] { (byte)'M' } })
{
    try { Check(!SelfUpdate.LooksLikeWindowsExe(bytes, 0), "small custom floor cannot bypass two-byte signature bounds"); }
    catch (Exception e) { Check(false, $"short shape check threw {e.GetType().Name}"); }
}
Check(!SelfUpdate.ShouldSelfUpdate(2, "<html>404</html>"), "malformed release version rejected");
// Synthetic files only: never execute an updater, git reset, or the real launcher.
string temporary = Path.Combine(Path.GetTempPath(), "tpw-launcher-audit-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    string assembly = Path.Combine(temporary, "viewer.dll"), path = Path.Combine(temporary, "ready.json");
    string revision = new('a', 40), next = new('b', 40);
    var receipt = new ViewerBuildReceipt(path);
    File.WriteAllText(assembly, "old artifact");
    Check(!receipt.CanLaunch(revision, assembly), "legacy/stale DLL without successful-build receipt cannot launch");
    receipt.RecordSuccess(revision, assembly);
    Check(receipt.CanLaunch(revision, assembly), "successful matching build is launchable");
    Check(new ViewerBuildReceipt(path).CanLaunch(revision, assembly), "successful readiness survives launcher restart");
    Check(!receipt.CanLaunch(next, assembly), "new checkout cannot launch an older recorded assembly");
    Check(!receipt.CanLaunch(null, assembly), "unknown checkout revision cannot launch");
    receipt.Invalidate();
    // Model a failed build that nevertheless emitted an output DLL.
    File.WriteAllText(assembly, "artifact emitted before later target failed");
    Check(!receipt.CanLaunch(revision, assembly), "failed build with emitted DLL stays unready");
    Check(!new ViewerBuildReceipt(path).CanLaunch(revision, assembly), "failed-build readiness remains false after restart");
    // Model deletion failure: the old DLL stays on disk after invalidation.
    Check(File.Exists(assembly) && !receipt.CanLaunch(next, assembly), "undeleted artifact cannot bypass invalidation");
    receipt.RecordSuccess(next, assembly);
    Check(receipt.CanLaunch(next, assembly), "successful retry restores readiness");
    File.AppendAllText(assembly, "changed");
    Check(!receipt.CanLaunch(next, assembly), "changed artifact invalidates its receipt");
    File.WriteAllText(path, "{broken");
    Check(!receipt.CanLaunch(next, assembly), "corrupt receipt fails closed");
    File.WriteAllText(path, "null");
    Check(!receipt.CanLaunch(next, assembly), "null receipt fails closed");
    File.WriteAllText(path, new string('x', 4097));
    Check(!receipt.CanLaunch(next, assembly), "oversized receipt fails closed");
    receipt.Invalidate(); File.Delete(assembly);
    try { receipt.RecordSuccess(revision, assembly); Check(false, "missing artifact must not be recorded"); }
    catch (FileNotFoundException) { Check(!receipt.CanLaunch(revision, assembly), "missing artifact cannot be recorded"); }
}
finally { Directory.Delete(temporary, recursive: true); }
string host = Environment.ProcessPath;
string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
string[] Child(params string[] values) => Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
    ? new[] { assemblyPath, "--process-fixture" }.Concat(values).ToArray()
    : new[] { "--process-fixture" }.Concat(values).ToArray();
var echo = await LauncherProcess.RunAsync(host, Child("echo", "path with spaces [and brackets]"),
    Environment.CurrentDirectory, TimeSpan.FromSeconds(10));
Check(echo.Succeeded && echo.Stdout.Contains("path with spaces [and brackets]") && echo.Stderr.Contains("stderr marker"),
      "process arguments preserve spaces and both pipes are captured");
var failed = await LauncherProcess.RunAsync(host, Child("fail"), Environment.CurrentDirectory, TimeSpan.FromSeconds(10));
Check(!failed.Succeeded && failed.ExitCode == 17 && failed.Stderr.Contains("deliberate fixture failure"),
      "nonzero process exit preserves diagnostics and fails");
var noisy = await LauncherProcess.RunAsync(host, Child("noisy"), Environment.CurrentDirectory, TimeSpan.FromSeconds(10), 1024);
Check(noisy.Succeeded && noisy.Truncated && noisy.Stdout.Length <= 1024 && noisy.Stderr.Length <= 1024
      && noisy.Stdout.EndsWith("stdout tail") && noisy.Stderr.EndsWith("error: stderr tail"),
      "large simultaneous output is drained without deadlock and keeps bounded diagnostic tails");
var timer = System.Diagnostics.Stopwatch.StartNew();
var stalled = await LauncherProcess.RunAsync(host, Child("sleep"), Environment.CurrentDirectory, TimeSpan.FromSeconds(1));
Check(stalled.TimedOut && !stalled.Succeeded && stalled.Stdout.Contains("before timeout") && timer.Elapsed < TimeSpan.FromSeconds(5),
      "stalled process times out and retains pre-timeout diagnostics");
var cleanup = await LauncherProcess.RunCoreAsync(host, Child("sleep"), Environment.CurrentDirectory,
    TimeSpan.FromMilliseconds(500), 1024, process =>
    {
        process.Kill(entireProcessTree: true);
        throw new AggregateException("synthetic partial-tree cleanup failure");
    });
Check(cleanup.TimedOut && !cleanup.Succeeded && cleanup.Error.Contains("synthetic partial-tree") && cleanup.ExitCode != null,
      "exceptional tree cleanup still reports timeout and completes exit/pipe cleanup");
var absent = await LauncherProcess.RunAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
    Array.Empty<string>(), Environment.CurrentDirectory, TimeSpan.FromSeconds(1));
Check(!absent.Succeeded && absent.ExitCode == null && absent.Error != null,
      "missing executable reports launch failure without inventing a child exit code");
Console.WriteLine(bad == 0 ? "PASS launcher audit" : $"FAIL: {bad}");
return bad == 0 ? 0 : 1;
