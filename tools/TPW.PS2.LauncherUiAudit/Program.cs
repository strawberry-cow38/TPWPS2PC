using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TPW.PS2.Data;
using TPW.PS2.Launcher;

int bad = 0;
void Check(bool ok, string message) { Console.WriteLine((ok ? "ok   " : "FAIL ") + message); if (!ok) bad++; }
using var session = HeadlessUnitTestSession.StartNew(typeof(Application));
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await session.Dispatch(async () =>
{
    await using (var f = new Fixture())
    {
        f.BuildExit = 1; f.SetMode("Update");
        await f.Click();
        Check(f.Window.IsVisible, "build failure keeps the recovery window open");
        Check(f.Builds == 1 && f.Starts == 0, "failed build that emitted a DLL never starts Godot");
        Check(f.Mode == "Broken" && f.Action.IsEnabled && f.Action.Content?.ToString() == "Retry",
              "failed build leaves an enabled Retry action");
        Check(f.Status.Contains("failed") && !f.Receipt.CanLaunch(Fixture.Revision, f.Assembly),
              "failed build retains error status and invalidates launch readiness");
        await f.Refresh();
        Check(f.Mode == "Update", "refresh cannot advertise Play for an emitted but unsuccessful build");
        f.SetMode("Broken"); f.BuildExit = 0;
        await f.Click();
        Check(!f.Window.IsVisible, "successful build handoff closes the launcher");
        Check(f.Builds == 2 && f.Starts == 1 && f.Receipt.CanLaunch(Fixture.Revision, f.Assembly),
              "Retry repeats the failed build and launches only after successful readiness");
    }
    await using (var f = new Fixture())
    {
        f.MakeReady(); f.ThrowOnStart = true; f.SetMode("Play");
        await f.Click();
        Check(f.Window.IsVisible, "process-start exception keeps the launcher open");
        Check(f.Mode == "Broken" && f.Action.IsEnabled && f.Status.Contains("synthetic start failure"),
              "actual Play start exception remains visible with enabled Retry");
        f.ThrowOnStart = false; await f.Click();
        Check(!f.Window.IsVisible, "successful Play retry closes the launcher");
        Check(f.Starts == 2 && f.Builds == 0, "Play retry retries process start without unnecessary rebuilding");
    }
    await using (var f = new Fixture())
    {
        f.MakeReady(); f.SetMode("Play"); await f.Click();
        Check(f.Starts == 1 && f.LastPsx == null, "with no PSX disc chosen, the viewer gets no TPW_PSX_DISC at all");
    }
    await using (var f = new Fixture())
    {
        const string psx = "synthetic psx [two].iso";
        f.SetPsx(new PsxDisc.Check(PsxDisc.Status.Ok, psx, "fixture"));
        f.MakeReady(); f.SetMode("Play"); await f.Click();
        Check(f.Starts == 1 && f.LastPsx == psx, "a readable PSX choice is handed to the viewer as TPW_PSX_DISC");
    }
    await using (var f = new Fixture())
    {
        f.SetPsx(new PsxDisc.Check(PsxDisc.Status.NotTpw, "not a psx disc.iso", "fixture"));
        f.MakeReady(); f.SetMode("Play"); await f.Click();
        Check(f.Starts == 1 && f.LastPsx == null, "an unreadable PSX choice is never handed on");
    }
    await using (var f = new Fixture())
    {
        f.SetMode("Busy");
        Check(!f.LocatePsx.IsEnabled && !f.ClearPsx.IsEnabled, "a pending operation disables the PSX picker and Clear");
        f.SetMode("Play");
        Check(f.LocatePsx.IsEnabled && f.ClearPsx.IsEnabled, "and they come back after it");
    }
    await using (var f = new Fixture())
    {
        f.MakeReady(); f.NullStart = true; f.SetMode("Play"); await f.Click();
        Check(f.Window.IsVisible, "null process result keeps the launcher open");
        Check(f.Mode == "Broken" && f.Status.Contains("Godot did not start") && f.Action.IsEnabled,
              "null process result also preserves its failure and Retry action");
    }
    await using (var f = new Fixture())
    {
        f.MakeReady(); File.AppendAllText(f.Assembly, "changed"); f.SetMode("Play"); await f.Click();
        Check(f.Window.IsVisible, "blocked stale artifact leaves recovery available");
        Check(f.Starts == 0 && f.Mode == "Broken" && f.Status.Contains("refusing"),
              "last-moment artifact change is blocked by the actual Play gate");
        await f.Click();
        Check(!f.Window.IsVisible, "successful stale-artifact rebuild closes after handoff");
        Check(f.Builds == 1 && f.Starts == 1, "invalid artifact Retry switches to rebuilding, not repeated stale Play");
    }
    await using (var f = new Fixture())
    {
        f.BlockBuild = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.SetMode("Update"); f.RaiseClick(); Dispatcher.UIThread.RunJobs();
        Check(f.Mode == "Busy" && !f.Action.IsEnabled && !f.Locate.IsEnabled,
              "pending build disables action and disc picker");
        f.RaiseClick(); Dispatcher.UIThread.RunJobs();
        Check(f.Builds == 1, "a queued duplicate click cannot start a second operation");
        f.BlockBuild.SetResult(new(1, false, "", "error: blocked fixture", false, null));
        await f.WaitForAction();
        Check(f.Window.IsVisible, "asynchronously failed build retains its window");
        Check(f.Mode == "Broken" && f.Action.IsEnabled && f.Locate.IsEnabled,
              "completion of the pending failure restores recovery controls");
    }
    await using (var f = new Fixture())
    {
        // Initial retry mode is startup. A regression routing here must fail closed,
        // never reach discovery, HTTP self-update or the updater process shim.
        f.SetMode("Broken"); await f.Click();
        Check(f.Mode == "Broken" && f.Status.Contains("Environment startup disabled") && f.Starts == 0 && f.Builds == 0,
              "unexpected startup in the isolated host cannot access real environment actions");
    }
    return 0;
}, deadline.Token);
Console.WriteLine(bad == 0 ? "PASS launcher UI audit" : $"FAIL: {bad}");
return bad == 0 ? 0 : 1;

sealed class Fixture : IAsyncDisposable
{
    public const string Revision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string Disc = "synthetic selected disc [one].bin";
    readonly string _root = Path.Combine(Path.GetTempPath(), "tpw-launcher-ui-" + Guid.NewGuid().ToString("N"));
    readonly List<Process> _handles = new();
    public MainWindow Window { get; }
    public string Assembly { get; }
    public ViewerBuildReceipt Receipt { get; }
    public int BuildExit, Builds, Starts;
    public string LastPsx = "unset";
    public bool ThrowOnStart, NullStart;
    public TaskCompletionSource<LauncherProcess.Result> BlockBuild;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, Private).GetValue(Window);
    void Set(string name, object value) => typeof(MainWindow).GetField(name, Private).SetValue(Window, value);
    public Button Action => Field<Button>("_action");
    public Button Locate => Field<Button>("_locate");
    public Button LocatePsx => Field<Button>("_locatePsx");
    public Button ClearPsx => Field<Button>("_clearPsx");
    /// <summary>Put a PSX identification into the window's choice without a disc.</summary>
    public void SetPsx(PsxDisc.Check check) =>
        typeof(PsxDiscChoice).GetProperty("Current").SetValue(Field<PsxDiscChoice>("_psx"), check);
    public string Mode => Field<object>("_mode").ToString();
    public string Status => Field<TextBlock>("_status").Text ?? "";

    public Fixture()
    {
        string repo = Path.Combine(_root, "TPWPS2PC"); Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Assembly = Path.Combine(repo, "game", ".godot", "mono", "temp", "bin", "Debug", "TPWPS2Viewer.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(Assembly));
        Receipt = new ViewerBuildReceipt(Path.Combine(_root, "viewer-build.json"));
        var services = new LauncherWindowServices
        {
            AllowEnvironmentActions = false,
            FindExecutable = name => name == "git" ? "fake-git" : throw new InvalidOperationException("unexpected executable lookup"),
            Execute = (exe, args, cwd, timeout) =>
            {
                if (!Path.GetFullPath(cwd).StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && Path.GetFullPath(cwd) != _root) throw new InvalidOperationException("command escaped test directory");
                if (exe == "dotnet" && args.SequenceEqual(new[] { "build" }))
                {
                    Builds++; File.WriteAllText(Assembly, "synthetic viewer output " + Builds);
                    return BlockBuild?.Task ?? Task.FromResult(new LauncherProcess.Result(BuildExit, false, "", "", false, null));
                }
                if (exe != "fake-git") throw new InvalidOperationException("unexpected real command");
                if (args.Contains("rev-parse") || args.Contains("ls-remote"))
                    return Task.FromResult(new LauncherProcess.Result(0, false, Revision, "", false, null));
                if (args[0] is "fetch" or "reset" or "clean")
                    return Task.FromResult(new LauncherProcess.Result(0, false, "", "", false, null));
                throw new InvalidOperationException("unexpected Git operation");
            },
            Start = info =>
            {
                Starts++;
                if (info.FileName != "fake-godot") throw new InvalidOperationException("unexpected process start");
                if (info.UseShellExecute || !info.ArgumentList.SequenceEqual(new[] { "--path", Path.Combine(repo, "game") })
                    || info.Environment["TPW_PS2_DISC"] != Disc)
                    throw new InvalidOperationException("viewer launch arguments/environment contract changed");
                LastPsx = info.Environment.ContainsKey("TPW_PSX_DISC") ? info.Environment["TPW_PSX_DISC"] : null;
                if (ThrowOnStart) throw new InvalidOperationException("synthetic start failure");
                if (NullStart) return null;
                var handle = new Process(); _handles.Add(handle); return handle; // never started
            },
        };
        Window = new MainWindow(_root, services, startAutomatically: false);
        Set("_disc", new DiscResult(DiscStatus.Ok, Disc, "fixture", 1));
        Set("_godot", new GodotChoice("fake-godot", true));
        Window.Show(); Dispatcher.UIThread.RunJobs();
    }
    public void MakeReady() { File.WriteAllText(Assembly, "synthetic ready viewer"); Receipt.RecordSuccess(Revision, Assembly); }
    public void SetMode(string mode)
    {
        var type = typeof(MainWindow).GetNestedType("Mode", BindingFlags.NonPublic);
        typeof(MainWindow).GetMethod("SetMode", Private).Invoke(Window, new[] { Enum.Parse(type, mode), (object)mode, "fixture" });
        Dispatcher.UIThread.RunJobs();
    }
    public void RaiseClick() => Action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    public async Task Click() { RaiseClick(); await WaitForAction(); }
    public async Task WaitForAction()
    {
        await Window.PendingAction.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
    }
    public async Task Refresh()
    {
        await (Task)typeof(MainWindow).GetMethod("RefreshStateAsync", Private).Invoke(Window, null);
        Dispatcher.UIThread.RunJobs();
    }
    public async ValueTask DisposeAsync()
    {
        BlockBuild?.TrySetResult(new(1, false, "", "fixture disposal", false, null));
        await Window.PendingAction.WaitAsync(TimeSpan.FromSeconds(5));
        Window.Close(); Dispatcher.UIThread.RunJobs();
        foreach (var handle in _handles) handle.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
