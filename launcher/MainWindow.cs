using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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

    public MainWindow()
    {
        Title = "Theme Park World (PS2) — asset viewer";
        Width = 620; Height = 520; Background = Bg;

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
                                     Children = { _launch } },
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

    void Launch()
    {
        if (_projectDir == null) { Log("no game/project.godot found next to the launcher"); return; }
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

    void Log(string line) => Dispatcher.UIThread.Post(() =>
    {
        _log.Text += (_log.Text.Length > 0 ? "\n" : "") + line;
    });
}
