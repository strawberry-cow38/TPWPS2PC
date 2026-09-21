using System.Diagnostics;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TPW.PS2.Launcher;

/// <summary>Install, update and play, behind ONE button.
///
/// ⚠ Every decision here is a call into core/TPW.PS2.Launcher. Nothing about what counts as a valid
/// disc, or which Godot to run, is decided in this file -- a second copy of those rules inside the
/// window is how a launcher comes to disagree with its own tests.</summary>
public class MainWindow : Window
{
    // ---- what the one button is currently for ----
    enum Mode { Busy, NeedDisc, Install, Update, Play, Broken }

    const int LauncherVersion = 2;
    const string RepoUrl = "https://github.com/strawberry-cow38/TPWPS2PC.git";
    const string Branch = "main";
    // ⭐ The launcher's own build lives on RELEASES, not in the repo: a ~9 MB exe per version would
    // sit in git history forever and binaries do not delta-compress.
    const string Rel = "https://github.com/strawberry-cow38/TPWPS2PC/releases/download/launcher";
    const string VersionUrl = Rel + "/launcher.version";
    const string ExeUrl = Rel + "/TPWPS2Launcher-win-x64.exe";
    const string Sha256Url = Rel + "/launcher.sha256";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    static readonly IBrush Bg = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1f));
    static readonly IBrush Card = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x29));
    static readonly IBrush TextMain = new SolidColorBrush(Color.FromRgb(0xe8, 0xe8, 0xee));
    static readonly IBrush TextDim = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0xa6));
    static readonly IBrush Good = new SolidColorBrush(Color.FromRgb(0x7d, 0xd8, 0x7d));
    static readonly IBrush Bad = new SolidColorBrush(Color.FromRgb(0xe8, 0x8a, 0x7d));

    readonly Button _action = new() { MinWidth = 190, MinHeight = 42, FontSize = 15, IsEnabled = false };
    readonly TextBlock _status = new() { Foreground = TextDim, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _discStatus = new() { Foreground = TextDim, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _buildState = new() { Foreground = TextDim, FontSize = 12 };
    // ⚠ A fresh Avalonia TextBox has Text == null, not "". Appending without this default
    // dereferences null on the very first line written.
    readonly TextBox _log = new()
    {
        Text = "", IsReadOnly = true, AcceptsReturn = true, MinHeight = 150,
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x16)), Foreground = TextDim,
        FontFamily = new FontFamily("Consolas,monospace"), FontSize = 12,
    };

    Mode _mode = Mode.Busy;
    DiscResult _disc;
    GodotChoice _godot;
    readonly string _baseDir;
    readonly string _repoDir;
    string ProjectDir => Path.Combine(_repoDir, "game");
    string AssemblyPath => Path.Combine(ProjectDir, ".godot", "mono", "temp", "bin", "Debug", "TPWPS2Viewer.dll");

    public MainWindow()
    {
        Title = "Theme Park World (PS2) — asset viewer";
        Width = 660; Height = 600; Background = Bg;
        _baseDir = AppContext.BaseDirectory;
        _repoDir = Path.Combine(_baseDir, "TPWPS2PC");

        var locate = new Button { Content = "Locate disc…", MinWidth = 110 };
        locate.Click += async (_, _) => await LocateAsync();
        _action.Click += async (_, _) => await OnActionAsync();

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Theme Park World", Foreground = TextMain, FontSize = 17,
                                    FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "PlayStation 2 — model and animation viewer",
                                    Foreground = TextDim, FontSize = 13 },
                    Box("Your disc", _discStatus,
                        new TextBlock { Text = "Bring your own disc: this reads an image you already have. "
                                             + "Nothing is bundled and nothing is downloaded from EA.",
                                        Foreground = TextDim, FontSize = 11,
                                        TextWrapping = TextWrapping.Wrap },
                        locate),
                    Box("Viewer", _buildState),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12,
                                     Children = { _action, _status } },
                    _log,
                    new TextBlock { Text = $"launcher v{LauncherVersion}", Foreground = TextDim, FontSize = 11 },
                }
            }
        };
        _ = StartupAsync();
    }

    static Control Box(string title, params Control[] rows)
    {
        var col = new StackPanel { Spacing = 6 };
        col.Children.Add(new TextBlock { Text = title, Foreground = TextMain, FontSize = 13,
                                         FontWeight = FontWeight.SemiBold });
        foreach (var r in rows) col.Children.Add(r);
        return new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(6),
                            Background = Card, Child = col };
    }

    async Task StartupAsync()
    {
        SetMode(Mode.Busy, "…", "Looking for your disc…");
        if (await CheckSelfUpdateAsync()) return;              // may close the window
        Refresh(DiscLocator.Probe());
        _godot = GodotLocator.Find(console: false);
        await RefreshStateAsync();
    }

    // ------------------------------------------------------------------ state

    /// <summary>Decide what the single button should do right now. ⚠ Order matters: a missing disc
    /// beats a missing build, because installing without a disc leaves the user with a viewer that
    /// still cannot open anything.</summary>
    async Task RefreshStateAsync()
    {
        if (!_disc.CanLaunch) { SetMode(Mode.NeedDisc, "Locate disc…", _disc.Message); return; }
        if (!_godot.Found)
        {
            SetMode(Mode.Broken, "—",
                    $"Godot {GodotLocator.RequiredVersion} (mono) not found. Install it and reopen.");
            return;
        }
        if (!Directory.Exists(Path.Combine(_repoDir, ".git")))
        {
            _buildState.Text = "Not installed yet.";
            SetMode(Mode.Install, "Install and play", "Downloads the viewer, builds it, then opens it.");
            return;
        }
        var (local, remote) = await HeadsAsync();
        _buildState.Text = local == null ? "Installed: unknown"
            : $"Installed: {Short(local)}" + (remote != null && remote != local
                ? $"   →  {Short(remote)} available" : "   (up to date)");
        if (remote != null && remote != local)
            SetMode(Mode.Update, "Update and play", "A newer viewer is available.");
        else if (!File.Exists(AssemblyPath))
            SetMode(Mode.Update, "Build and play", "Installed but not built yet.");
        else
            SetMode(Mode.Play, "Play", "Ready.");
    }

    async Task OnActionAsync()
    {
        var was = _mode;
        try
        {
            switch (was)
            {
                case Mode.NeedDisc: await LocateAsync(); return;
                case Mode.Install:
                case Mode.Update:
                    SetMode(Mode.Busy, "…", "Working…");
                    if (!await InstallOrUpdateAsync()) { await RefreshStateAsync(); return; }
                    Play();
                    break;
                case Mode.Play: Play(); break;
            }
        }
        catch (Exception e) { Log("ERROR: " + e.Message); SetMode(Mode.Broken, "Retry", e.Message); return; }
        await RefreshStateAsync();
    }

    void SetMode(Mode m, string label, string status) => Dispatcher.UIThread.Post(() =>
    {
        _mode = m;
        _action.Content = label;
        _action.IsEnabled = m is not (Mode.Busy or Mode.Broken) || m == Mode.Broken && label == "Retry";
        _status.Text = status;
        _status.Foreground = m == Mode.Broken ? Bad : m == Mode.Play ? Good : TextDim;
    });

    // ------------------------------------------------------------------ install / update

    /// <summary>Clone or hard-reset the repo, then build. Returns true only if the viewer assembly
    /// actually exists afterwards.
    ///
    /// ⚠⚠ THIS IS WHY UPDATES DID NOT APPLY BEFORE. Copying files over an existing tree leaves
    /// anything the new version deleted, and leaves a STALE COMPILED ASSEMBLY that Godot happily
    /// keeps running -- so the update looks applied and nothing changes. `git reset --hard` makes
    /// the tree match origin exactly, and the assembly is DELETED before rebuilding so a build
    /// failure cannot masquerade as a successful update.</summary>
    async Task<bool> InstallOrUpdateAsync()
    {
        string git = Which("git");
        if (git == null) { Log("git not found on PATH — install Git for Windows."); return false; }

        if (!Directory.Exists(Path.Combine(_repoDir, ".git")))
        {
            Log($"cloning {Branch}…");
            if (!await RunAsync(git, new[] { "clone", "--branch", Branch, RepoUrl, _repoDir }, _baseDir))
                return false;
        }
        else
        {
            Log("fetching…");
            if (!await RunAsync(git, new[] { "fetch", "origin", $"+{Branch}:refs/remotes/origin/{Branch}" }, _repoDir))
                return false;
            // ⚠ reset --hard, not pull: a local edit or a half-applied previous update would make
            // a merge fail and leave the tree in neither state.
            if (!await RunAsync(git, new[] { "reset", "--hard", "origin/" + Branch }, _repoDir)) return false;
            await RunAsync(git, new[] { "clean", "-fd", "-e", ".godot" }, _repoDir);
        }

        if (File.Exists(AssemblyPath)) File.Delete(AssemblyPath);
        Log("building…");
        if (!await RunAsync("dotnet", new[] { "build" }, ProjectDir)) { Log("build FAILED"); return false; }
        if (!File.Exists(AssemblyPath))
        {
            // ⚠ A zero exit code is not proof. The assembly is the artifact; check for it.
            Log("build reported success but produced no assembly — refusing to launch");
            return false;
        }
        Log("ready");
        return true;
    }

    async Task<(string local, string remote)> HeadsAsync()
    {
        string git = Which("git");
        if (git == null) return (null, null);
        string local = (await CaptureAsync(git, new[] { "-C", _repoDir, "rev-parse", "HEAD" }))?.Trim();
        string remote = null;
        var ls = await CaptureAsync(git, new[] { "ls-remote", RepoUrl, "refs/heads/" + Branch });
        if (!string.IsNullOrWhiteSpace(ls)) remote = ls.Split('\t', ' ')[0].Trim();
        return (string.IsNullOrWhiteSpace(local) ? null : local, string.IsNullOrWhiteSpace(remote) ? null : remote);
    }

    static string Short(string sha) => sha != null && sha.Length >= 7 ? sha[..7] : sha;

    void Play()
    {
        var psi = new ProcessStartInfo(_godot.Path) { UseShellExecute = false };
        psi.ArgumentList.Add("--path");
        psi.ArgumentList.Add(ProjectDir);
        // ⚠ The disc path travels by ENVIRONMENT, never argv: it routinely contains spaces and
        // brackets, and an argument a shell mangles arrives EMPTY -- which presents as the viewer
        // hanging rather than as a bad path.
        psi.Environment["TPW_PS2_DISC"] = _disc.Path;
        Log($"launching {Path.GetFileName(_godot.Path)}");
        Process.Start(psi);
    }

    // ------------------------------------------------------------------ disc

    async Task LocateAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select your Theme Park World disc image",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Disc image")
                                     { Patterns = new[] { "*.bin", "*.iso" } } },
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path != null) Refresh(DiscLocator.Identify(path));
        await RefreshStateAsync();
    }

    void Refresh(DiscResult r)
    {
        _disc = r;
        Dispatcher.UIThread.Post(() =>
        {
            _discStatus.Text = r.Message;
            _discStatus.Foreground = r.CanLaunch ? Good : Bad;
        });
        Log(r.CanLaunch ? $"disc ok: {r.Path}" : $"disc: {r.Message}");
    }

    // ------------------------------------------------------------------ self-update

    /// <summary>Replace this launcher with the published build, via a shim that runs after we exit.
    ///
    /// ⚠⚠ A PROCESS CANNOT OVERWRITE ITS OWN RUNNING EXECUTABLE ON WINDOWS. The new build is written
    /// BESIDE the old one, a batch file WAITS for this PID to disappear, swaps, relaunches and
    /// deletes itself. The shim exists solely to be the thing still alive when the launcher is not.</summary>
    async Task<bool> CheckSelfUpdateAsync()
    {
        if (!OperatingSystem.IsWindows()) return false;
        string exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return false;
        try
        {
            string raw = await Http.GetStringAsync(VersionUrl);
            if (!SelfUpdate.ShouldSelfUpdate(LauncherVersion, raw)) return false;
            Log($"launcher update: v{LauncherVersion} -> v{raw.Trim()}. Downloading…");
            byte[] bytes = await Http.GetByteArrayAsync(ExeUrl);
            string expected = null;
            try { expected = (await Http.GetStringAsync(Sha256Url)).Trim().Split(' ')[0]; } catch { }
            var verdict = SelfUpdate.Check(bytes, expected);
            Log("update check: " + verdict.Reason);
            if (!verdict.Accept) { Log("update ABORTED — keeping the current launcher."); return false; }

            string newExe = exePath + ".new";
            await File.WriteAllBytesAsync(newExe, bytes);
            int pid = Environment.ProcessId;
            string bat = Path.Combine(Path.GetTempPath(), "tpwps2_selfupdate.bat");
            const string q = "\"";
            // ⚠ WAIT ON THE PID, never sleep: too short and the move hits a locked file, too long
            // and the user stares at nothing.
            await File.WriteAllTextAsync(bat, string.Join("\r\n", new[]
            {
                "@echo off", ":wait",
                $"tasklist /FI {q}PID eq {pid}{q} | find {q}{pid}{q} >nul && (ping -n 2 127.0.0.1 >nul & goto wait)",
                $"move /y {q}{newExe}{q} {q}{exePath}{q} >nul",
                $"start {q}{q} {q}{exePath}{q}",
                $"del {q}%~f0{q}",     // ⚠ last: a leftover shim can run on the next update
            }) + "\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c {q}{bat}{q}")
            { UseShellExecute = false, CreateNoWindow = true });
            Log("restarting into the new launcher…");
            Dispatcher.UIThread.Post(Close);
            return true;
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Normal until a build has been published; not an error worth alarming anyone with.
            return false;
        }
        catch (Exception e) { Log("launcher update check failed: " + e.Message); return false; }
    }

    // ------------------------------------------------------------------ process plumbing

    static string Which(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var cand in OperatingSystem.IsWindows()
                     ? new[] { exe + ".exe", exe + ".cmd", exe } : new[] { exe })
            {
                try { var p = Path.Combine(dir, cand); if (File.Exists(p)) return p; } catch { }
            }
        }
        return null;
    }

    async Task<bool> RunAsync(string exe, string[] args, string cwd)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        var text = (await so) + (await se);
        // ⚠ Surface the tool's OWN error lines. "build failed" with no reason sends people to the
        // wrong place; the compiler already said what was wrong.
        foreach (var line in text.Split('\n')
                 .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).Take(8))
            Log("  " + line.Trim());
        return p.ExitCode == 0;
    }

    async Task<string> CaptureAsync(string exe, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            var s = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? s : null;
        }
        catch { return null; }
    }

    void Log(string line) => Dispatcher.UIThread.Post(() =>
        _log.Text += (_log.Text.Length > 0 ? "\n" : "") + line);
}
