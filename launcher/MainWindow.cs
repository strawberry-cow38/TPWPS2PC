using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Net.Http;
using TPW.PS2.Launcher;

/// <summary>Locate the user's disc, then launch the viewer against it.
///
/// ⚠ Every decision here is a call into core/TPW.PS2.Launcher. Nothing about what counts as a valid
/// disc, or which Godot to run, is decided in this file -- a second copy of those rules inside the
/// window is how a launcher comes to disagree with its own tests.</summary>
public class MainWindow : Window
{
    static readonly IBrush Bg = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1f));
    static readonly IBrush TextMain = new SolidColorBrush(Color.FromRgb(0xe8, 0xe8, 0xee));
    static readonly IBrush TextDim = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0xa6));
    static readonly IBrush Good = new SolidColorBrush(Color.FromRgb(0x7d, 0xd8, 0x7d));
    static readonly IBrush Bad = new SolidColorBrush(Color.FromRgb(0xe8, 0x8a, 0x7d));

    readonly TextBlock _discStatus = new() { Foreground = TextDim, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _godotStatus = new() { Foreground = TextDim, TextWrapping = TextWrapping.Wrap };
    readonly Button _launch = new() { Content = "Launch viewer", MinWidth = 168, MinHeight = 40, IsEnabled = false };
    readonly CheckBox _console = new() { Content = "Console window", Foreground = TextDim };
    // ⚠ A fresh Avalonia TextBox has Text == null, not "". Appending to it without this default
    // dereferences null on the very first line written.
    readonly TextBox _log = new()
    {
        Text = "", IsReadOnly = true, AcceptsReturn = true, MinHeight = 120,
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x16)), Foreground = TextDim,
        FontFamily = new FontFamily("Consolas,monospace"), FontSize = 12,
    };

    DiscResult _disc;
    string _projectDir;

    // The number is the release; bump it when publishing a new launcher exe.
    const int LauncherVersion = 1;
    const string Repo = "https://raw.githubusercontent.com/strawberry-cow38/TPWPS2PC/main/launcher/dist";
    const string VersionUrl = Repo + "/version.txt";
    const string ExeUrl = Repo + "/TPWPS2Launcher.exe";
    const string Sha256Url = Repo + "/TPWPS2Launcher.exe.sha256";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public MainWindow()
    {
        Title = "Theme Park World (PS2) — asset viewer";
        Width = 620; Height = 520; Background = Bg;

        var update = new Button { Content = "Check for launcher update", MinWidth = 190 };
        update.Click += async (_, _) => await CheckSelfUpdateAsync(manual: true);
        var locate = new Button { Content = "Locate…", MinWidth = 90 };
        locate.Click += async (_, _) => await LocateAsync();
        _launch.Click += (_, _) => Launch();

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Theme Park World", Foreground = TextMain, FontSize = 16,
                                    FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "PlayStation 2 release — model and animation viewer",
                                    Foreground = TextDim, FontSize = 13 },
                    new Border
                    {
                        Padding = new Thickness(12), CornerRadius = new CornerRadius(6),
                        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x29)),
                        Child = new StackPanel
                        {
                            Spacing = 6,
                            Children =
                            {
                                new TextBlock { Text = "Your disc", Foreground = TextMain, FontSize = 13,
                                                FontWeight = FontWeight.SemiBold },
                                _discStatus,
                                new TextBlock
                                {
                                    Text = "Bring your own disc: this reads an image you already have. " +
                                           "Nothing is bundled and nothing is downloaded.",
                                    Foreground = TextDim, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                                },
                                locate,
                            }
                        }
                    },
                    new Border
                    {
                        Padding = new Thickness(12), CornerRadius = new CornerRadius(6),
                        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x29)),
                        Child = new StackPanel
                        {
                            Spacing = 6,
                            Children =
                            {
                                new TextBlock { Text = "Godot", Foreground = TextMain, FontSize = 13,
                                                FontWeight = FontWeight.SemiBold },
                                _godotStatus, _console,
                            }
                        }
                    },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
                                     Children = { _launch, update } },
                    new TextBlock { Text = $"launcher v{LauncherVersion}", Foreground = TextDim, FontSize = 11 },
                    _log,
                }
            }
        };

        _projectDir = FindProject();
        Refresh(DiscLocator.Probe());
        RefreshGodot();
    }

    /// <summary>The Godot project next to the launcher, or up the tree when running from a build dir.</summary>
    static string FindProject()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
        {
            var g = Path.Combine(d.FullName, "game", "project.godot");
            if (File.Exists(g)) return Path.GetDirectoryName(g);
        }
        return null;
    }

    async Task LocateAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select your Theme Park World disc image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Disc image") { Patterns = new[] { "*.bin", "*.iso" } },
            },
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path != null) Refresh(DiscLocator.Identify(path));
    }

    void Refresh(DiscResult r)
    {
        _disc = r;
        _discStatus.Text = r.Message;
        _discStatus.Foreground = r.CanLaunch ? Good : Bad;
        UpdateLaunchable();
        Log(r.CanLaunch ? $"disc ok: {r.Path}" : $"disc: {r.Message}");
    }

    GodotChoice _godot;
    void RefreshGodot()
    {
        _godot = GodotLocator.Find(_console.IsChecked == true);
        if (!_godot.Found)
        {
            _godotStatus.Text = $"Godot {GodotLocator.RequiredVersion} (mono) not found. " +
                                "Install it, or put it beside the launcher.";
            _godotStatus.Foreground = Bad;
        }
        else
        {
            // ⚠ Say what we WILL do, not what was asked for.
            _godotStatus.Text = _godot.Satisfied
                ? _godot.Path
                : $"{_godot.Path}\n(the other build; the one you ticked is not installed)";
            _godotStatus.Foreground = Good;
        }
        UpdateLaunchable();
    }

    void UpdateLaunchable() =>
        _launch.IsEnabled = _disc.CanLaunch && _godot.Found && _projectDir != null;

    /// <summary>The viewer's C# assembly, which Godot needs before it can instantiate any script.
    /// ⚠ Without it Godot reports "Cannot instantiate C# script ... class could not be found",
    /// which reads as a broken script rather than an unbuilt project.</summary>
    static string AssemblyPath(string projectDir) =>
        Path.Combine(projectDir, ".godot", "mono", "temp", "bin", "Debug", "TPWPS2Viewer.dll");

    bool EnsureBuilt()
    {
        if (File.Exists(AssemblyPath(_projectDir))) return true;
        Log("viewer assembly not built yet -- running dotnet build (first run only)");
        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = _projectDir,
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            psi.ArgumentList.Add("build");
            using var p = Process.Start(psi);
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 || !File.Exists(AssemblyPath(_projectDir)))
            {
                foreach (var line in (stdout + stderr).Split('\n').Where(l => l.Contains("error")).Take(6))
                    Log("  " + line.Trim());
                Log("build failed -- is the .NET 8 SDK installed?");
                return false;
            }
            Log("built ok");
            return true;
        }
        catch (Exception e) { Log("could not run dotnet: " + e.Message); return false; }
    }

    void Launch()
    {
        if (_projectDir == null) { Log("no game/project.godot found next to the launcher"); return; }
        if (!EnsureBuilt()) return;
        RefreshGodot();
        var psi = new ProcessStartInfo(_godot.Path) { UseShellExecute = false };
        psi.ArgumentList.Add("--path");
        psi.ArgumentList.Add(_projectDir);
        // ⚠ The disc path goes through the ENVIRONMENT, not the command line. It routinely contains
        // spaces and brackets, and an argument mangled by a shell arrives empty -- which presents as
        // the viewer hanging rather than as a bad path.
        psi.Environment["TPW_PS2_DISC"] = _disc.Path;
        Log($"launching: {_godot.Path}");
        Log($"  project: {_projectDir}");
        Log($"  disc:    {_disc.Path}");
        try { Process.Start(psi); }
        catch (Exception e) { Log("failed: " + e.Message); }
    }

    /// <summary>Replace this launcher with the published build, via a shim that runs after we exit.
    ///
    /// ⚠⚠ A PROCESS CANNOT OVERWRITE ITS OWN RUNNING EXECUTABLE ON WINDOWS -- the file is locked
    /// while it runs. So the new build is written BESIDE the old one, a tiny batch file is started
    /// that WAITS for this PID to disappear, swaps the files, relaunches and deletes itself, and
    /// only then do we close. The shim exists solely to be the thing still alive when the launcher
    /// is not.</summary>
    async Task<bool> CheckSelfUpdateAsync(bool manual = false)
    {
        // Windows-only, because the shim is a .bat. Say so rather than silently never updating.
        if (!OperatingSystem.IsWindows())
        {
            if (manual) Log("self-update is Windows-only (the swap shim is a .bat)");
            return false;
        }
        string exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return false;
        try
        {
            string raw = await Http.GetStringAsync(VersionUrl);
            if (!SelfUpdate.ShouldSelfUpdate(LauncherVersion, raw))
            {
                if (manual) Log($"launcher is up to date (v{LauncherVersion}; published '{raw.Trim()}')");
                return false;
            }
            Log($"launcher update available: v{LauncherVersion} -> v{raw.Trim()}. Downloading…");
            byte[] bytes = await Http.GetByteArrayAsync(ExeUrl);

            // ⚠ The published hash, if any. Its ABSENCE must not look like success -- Check()
            // returns a reason either way and we log it, so a launcher that has stopped verifying
            // says so out loud.
            string expected = null;
            try { expected = (await Http.GetStringAsync(Sha256Url)).Trim().Split(' ')[0]; } catch { }

            var verdict = SelfUpdate.Check(bytes, expected);
            Log("update check: " + verdict.Reason);
            if (!verdict.Accept) { Log("update ABORTED — still running the current launcher."); return false; }

            string newExe = exePath + ".new";
            await File.WriteAllBytesAsync(newExe, bytes);

            int pid = Environment.ProcessId;
            string bat = Path.Combine(Path.GetTempPath(), "tpwps2_selfupdate.bat");
            const string q = "\"";
            // ⚠ WAIT ON THE PID, never just sleep. A fixed delay is a race: too short and the move
            // fails against a locked file, too long and the user stares at nothing.
            await File.WriteAllTextAsync(bat, string.Join("\r\n", new[]
            {
                "@echo off",
                ":wait",
                $"tasklist /FI {q}PID eq {pid}{q} | find {q}{pid}{q} >nul && (ping -n 2 127.0.0.1 >nul & goto wait)",
                $"move /y {q}{newExe}{q} {q}{exePath}{q} >nul",
                $"start {q}{q} {q}{exePath}{q}",
                // ⚠ The shim deletes itself LAST. Leaving it behind means the next update may find
                // a stale file from a previous version and run that instead.
                $"del {q}%~f0{q}",
            }) + "\r\n");

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c {q}{bat}{q}")
            { UseShellExecute = false, CreateNoWindow = true });
            Log("restarting into the new launcher…");
            Dispatcher.UIThread.Post(Close);
            return true;
        }
        catch (Exception e) { if (manual) Log("update check failed: " + e.Message); return false; }
    }

    void Log(string line) => Dispatcher.UIThread.Post(() =>
    {
        _log.Text += (_log.Text.Length > 0 ? "\n" : "") + line;
    });
}
