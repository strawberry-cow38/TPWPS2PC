using Godot;
using TPW.PS2.Data;
// ⚠ Godot has an `Animation` of its own. Alias ours so the clash is impossible rather than
// resolved differently in each file.
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

/// <summary>The viewer: pick an archive, pick a ride, pick one of its animations, watch it play.
///
/// ⚠ BYO-disc. The path comes from the launcher (or `--disc=`); nothing is bundled.</summary>
public partial class Viewer : Node3D
{
    AssetLibrary _lib;
    AnimatedModel _current;
    readonly Dictionary<string, (ImageTexture Tex, bool Soft)> _texCache = new(StringComparer.OrdinalIgnoreCase);
    AssetLibrary.RideAssets _ride;
    Aps _anim;
    List<Aps.Record> _records = new();
    int _recordIndex;

    Camera3D _cam;
    float _yaw = 0.6f, _pitch = -0.35f, _dist = 60f;
    Vector3 _focus;
    float _time;
    bool _playing = true;
    // Capture mode, the same shape the PSX port uses: --shot=<path>:<frame> renders one frame and
    // quits, so a render can be checked over ssh without a display.
    string _shotPath; int _shotFrame = -1, _shotWait;
    string _wantRide, _wantAnim, _wantWad, _wantMode, _wantImage, _wantSound, _wantPlay, _wantMap;
    string _discPath;
    // The park, and the two tables it needs: every ride's design data, and the text the player
    // is actually shown. Loaded once -- RideCatalogue walks every WAD.
    /// <summary>The park's size in cells. Big enough that a ride is in a place rather than on a
    /// plinth, small enough to see the edges.</summary>
    const int ParkCells = 24;
    Park _park;
    AnimatedModel _terrain;
    string _terrainPath;
    Vector2 _terrainSize;
    Vector2 _holeOrigin, _holeSize;
    bool[,] _holeCells;
    float _holeY;
    RideCatalogue _cat;
    TextDatabase _text;
    AdvisorCatalogue _advisor;
    WadArchive _advisorLips;
    string _advisorLanguage, _advisorError;

    ItemList _rideList;
    CheckBox _texOn;
    OptionButton _wadPick, _animPick;
    /// <summary>Side panel width. The image panes are offset by it, so it is one number.</summary>
    const int PanelW = 320;

    TabBar _tabs;
    Control _panel;

    /// <summary>List row -> data index. Category headers are -1, so a click on a heading selects
    /// nothing rather than the wrong item.</summary>
    readonly List<int> _rows = new();

    /// <summary>Every park on the disc: which archive, which terrain file. Built once, lazily.</summary>
    readonly List<(string Wad, string Path, string Label)> _maps = new();
    bool _mapsBuilt;

    /// <summary>The camera the console actually runs. ⭐ THE DEFAULT in park mode, at master's
    /// call -- the orbit camera is the debug view, not the game's.</summary>
    readonly GameCamera _game = new();
    /// <summary>The park's entrance arch, Features/Gates/Gates.mps, one per archive.</summary>
    AnimatedModel _gate;
    /// <summary>The park's sky, rebuilt when the archive changes.</summary>
    WorldEnvironment _sky;
    /// <summary>Kept so the sky can be taken away outside park mode and put back without a rebuild.
    /// ⚠ WorldEnvironment is a plain Node, so it has no Visible to toggle.</summary>
    Godot.Environment _skyEnv;
    /// <summary>The plain backdrop used everywhere that is not a park.</summary>
    Godot.Environment _flatEnv;
    /// <summary>Rain and snow. ⚠ OFF by default -- a park that is always raining is not the park.</summary>
    readonly Weather _weather = new();
    /// <summary>Weather asked for but not built yet. ⚠ It cannot be built inside the park load:
    /// the camera's transform is written in _Process, so at that moment _cam is still wherever the
    /// LAST park left it, and the volume would be preprocessed around the wrong place.</summary>
    Weather.Kind? _weatherWanted;
    /// <summary>The buildable overlay, rebuilt with the park. B toggles it.</summary>
    Node3D _buildable;
    /// <summary>The path tool, the console tables behind it, and the terrain model it works on.</summary>
    PathTool _paths;
    PathGhost _ghost;
    GhostMarkers _ghostView;
    /// <summary>Whether the path tool is open. ⭐ On the console a build tool is a MODE you open
    /// and close, not a key you tap: while it is open the ghost follows the cursor and a press
    /// lays a run.</summary>
    bool _toolOpen;
    PathTool.Kind _toolKind = PathTool.Kind.Path;
    /// <summary>Where the current run started, or -1 when no run is going.</summary>
    int _runX = -1, _runY = -1;
    /// <summary>A cell to use instead of the mouse, for captures. Null in normal use.</summary>
    (int X, int Y)? _cursorOverride;
    bool _pickChecked;
    /// <summary>The run the ghost was last built for, so it is not rebuilt every frame.</summary>
    (int Sx, int Sy, int X, int Y) _ghostAt = (-1, -1, -1, -1);
    /// <summary>Where a mouse button went down, to tell a CLICK from a DRAG. ⚠ Both buttons
    /// already drive the camera: right drags pan and left drags orbit, so acting on the press
    /// would open the tool every time the view is moved.</summary>
    Vector2 _downAt;
    MouseButton _downButton = MouseButton.None;
    PathPieces _pieces;
    Model _terrainModel;
    bool _pathTest;
    bool _ghostTest;
    string _wantSegments;
    string _wantCam;
    /// <summary>Nudge on the gate's z, in units, starting at master's own correction.
    ///
    /// ⭐ Fantasy's pad alone put the gate a quarter unit too far from the road, and master — who
    /// can see the park — fixed it with one press of `]`. So the shipped value is the pad's
    /// reading plus 0.25, and `[` / `]` still move it from there.
    ///
    /// ⚠ THIS IS A JUDGEMENT, NOT AN ANCHOR, and the two numbers agreeing is NOT corroboration:
    /// the nudge moves the pad path and the constant path by the SAME amount, so of course they
    /// still agree. What it is: one calibration by the only pair of eyes on the real thing,
    /// applied to all four parks because the entrance is one prefab (see the anchors in
    /// findings/, where A_ROAD and ticket_booths are identical in every park).</summary>
    float _gateNudge = 0.25f;
    /// <summary>G swaps to the free orbit camera.</summary>
    bool _freeCam;
    /// <summary>Ground height per TILE in world units, the same lookup the game does. Baked when
    /// the terrain loads: 0x14F820 asks for a tile's height, not for a ray hit.</summary>
    int[,] _ground;
    Vector2I _groundOrigin;
    /// <summary>A ride is standing in the park because one was asked for, not by default.</summary>
    bool _parkRide;

    /// <summary>The terrain file the park tab asked for, or null for the first one.</summary>
    string _wantTerrain;
    Label _info;
    HSlider _scrub;

    /// <summary>What the left-hand list is showing. The archive is the same either way; only what
    /// the viewer does with an entry changes.</summary>
    enum Mode { Models, Textures, Sounds, Movies, Park }
    Mode _mode = Mode.Models;
    TextureRect _imageView;
    ColorRect _imageBack;
    List<WadArchive.Entry> _images = new();

    List<Disc.Entry> _banks = new();
    VideoStreamPlayer _video;
    List<string> _movies = new();
    SoundBank _bank;
    AudioStreamPlayer _player;
    Button _playBtn;

    public override void _Ready()
    {
        // ⚠⚠ BOTH LISTS. `OS.GetCmdlineArgs()` does NOT carry what follows `--` -- those go to
        // `GetCmdlineUserArgs()` alone -- so reading only the first one makes every switch on a
        // launch line vanish and the viewer reports "no disc path given" while staring at one.
        var argv = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
        string disc = null;
        foreach (var a in argv)
            if (a.StartsWith("--disc=")) disc = a["--disc=".Length..];
        disc ??= OS.GetEnvironment("TPW_PS2_DISC");
        if (string.IsNullOrWhiteSpace(disc)) disc = GetTree().GetMeta("tpw_disc", "").AsString();
        // ⚠ Environment fallbacks for every switch. Arguments after `--` do not survive some shells
        // intact, and a disc path with spaces is the common case -- losing them silently is how this
        // looked like a hang rather than a missing argument.
        string Env(string k) { var v = OS.GetEnvironment(k); return string.IsNullOrWhiteSpace(v) ? null : v; }
        foreach (var a in argv)
        {
            if (a.StartsWith("--shot="))
            {
                var v = a["--shot=".Length..];
                int c = v.LastIndexOf(':');
                if (c > 2) { _shotPath = v[..c]; int.TryParse(v[(c + 1)..], out _shotFrame); }
                else _shotPath = v;
            }
            // ⚠ ENV DOES NOT ALWAYS REACH THIS PROCESS. A capture is launched through
            // Start-Process on the render box and the child did not inherit the switches, so
            // everything a shot needs has a command-line form too. The env reads below are the
            // fallback, not the other way round.
            else if (a.StartsWith("--map=")) _wantMap = a["--map=".Length..];
            else if (a.StartsWith("--mode=")) _wantMode = a["--mode=".Length..];
            else if (a == "--path-test") _pathTest = true;
            else if (a == "--ghost-test") _ghostTest = true;
            else if (a.StartsWith("--segments=")) _wantSegments = a["--segments=".Length..];
            else if (a.StartsWith("--cam=")) _wantCam = a["--cam=".Length..];
            else if (a.StartsWith("--ride=")) _wantRide = a["--ride=".Length..];
            else if (a.StartsWith("--anim=")) _wantAnim = a["--anim=".Length..];
            else if (a.StartsWith("--wad=")) _wantWad = a["--wad=".Length..];
        }

        _wantMode ??= Env("TPW_PS2_MODE");
        _wantMap ??= Env("TPW_PS2_MAP");
        _wantSound = Env("TPW_PS2_SOUND");
        _wantPlay = Env("TPW_PS2_PLAY");
        _wantImage = Env("TPW_PS2_IMAGE");
        _wantRide ??= Env("TPW_PS2_RIDE");
        _wantAnim ??= Env("TPW_PS2_ANIM");
        _wantWad ??= Env("TPW_PS2_WAD");
        if (_shotPath == null && Env("TPW_PS2_SHOT") != null)
        {
            _shotPath = Env("TPW_PS2_SHOT");
            int.TryParse(Env("TPW_PS2_FRAME") ?? "0", out _shotFrame);
        }
        BuildUi();
        GD.Print($"[v] start; args={string.Join(" ", argv)}");
        if (string.IsNullOrWhiteSpace(disc) || !File.Exists(disc))
        {
            // ⚠ Say WHICH path failed. "No disc" alone cannot tell a missing argument from a
            // mistyped path, and a disc path with spaces in it is exactly how the argument arrives
            // empty when a shell mangles the quoting.
            var msg = string.IsNullOrWhiteSpace(disc)
                ? "No disc path given. Pass --disc=<the .bin>, set TPW_PS2_DISC, or use the launcher."
                : $"Disc not found at: {disc}";
            _info.Text = msg;
            GD.Print("[v] " + msg);
            return;
        }
        _discPath = disc;
        GD.Print("[v] opening disc"); _lib = new AssetLibrary(disc);
        using (var lightingDisc = new Disc(disc)) Ps2Materials.Lighting = Lighting.Read(lightingDisc);
        GD.Print($"[light] ELF clear weather: ambient={Ps2Materials.Lighting.Ambient}, directional={Ps2Materials.Lighting.Directional}, ray={Ps2Materials.Lighting.RayDirection}");
        _park = new Park();
        AddChild(_park.Root);
        _park.Root.Visible = false;
        // ⚠ Only DATA.WAD is read up front, for the text. The ride definitions come from whichever
        // archive is open, added by OpenWad -- loading all sixteen here cost minutes of sector reads
        // for fifteen archives the viewer was not showing.
        try
        {
            foreach (var f in _lib.WadFiles())
                if (f.Path.EndsWith("/DATA.WAD", StringComparison.OrdinalIgnoreCase))
                    _text = TextDatabase.Load(new WadArchive(_lib.ReadDisc(f)), Env("TPW_PS2_TEXT_REGION") ?? "eur");
            GD.Print($"[park] text {(_text == null ? "MISSING" : _text.Keys.Length + " rows")}");
        }
        catch (Exception ex) { GD.PrintErr($"[park] text failed: {ex}"); }
        var wads = _lib.Wads(); GD.Print($"[v] {wads.Count} wads"); foreach (var w in wads) _wadPick.AddItem(w);
        if (_wadPick.ItemCount > 0)
        {
            int w = 0;
            var wantWad = _wantWad ?? (_wantRide != null ? "JUNGLE" : null);
            if (wantWad != null)
                for (int i = 0; i < _wadPick.ItemCount; i++)
                    if (_wadPick.GetItemText(i).Contains(wantWad, StringComparison.OrdinalIgnoreCase)) { w = i; break; }
            _wadPick.Select(w); OpenWad(w);
        }
        // ⚠⚠ A ROW IS NOT A DATA INDEX. The lists carry category headings now, so every one of
        // these selectors has to map the row back through _rows -- passing the row straight to
        // ShowRide asked for 'bigpalm.mps' and got bus1, which reads as a model-loading bug.
        if (_wantRide != null)
            for (int i = 0; i < _rideList.ItemCount; i++)
                if (Row(i) >= 0 && _rideList.GetItemText(i).Contains(_wantRide, StringComparison.OrdinalIgnoreCase))
                { _rideList.Select(i); ShowRide(Row(i)); break; }
        if (_wantAnim != null && int.TryParse(_wantAnim, out var ai) && ai < _animPick.ItemCount)
        { _animPick.Select(ai); _recordIndex = ai; Rebuild(); }
        // ⚠ Every switch has an environment fallback, because arguments after `--` do not survive
        // cmd's quoting and a silent empty argument presents as a hang rather than an error.
        if (_wantMode != null && _wantMode.StartsWith("mov", StringComparison.OrdinalIgnoreCase))
        {
            _tabs.CurrentTab = ModeTab(Mode.Movies); SetMode(Mode.Movies);
        }
        else if (_wantMode != null && _wantMode.StartsWith("sou", StringComparison.OrdinalIgnoreCase))
        {
            _tabs.CurrentTab = ModeTab(Mode.Sounds); SetMode(Mode.Sounds);
            if (_wantSound != null)
            {
                for (int b = 0; b < _wadPick.ItemCount; b++)
                    if (_wadPick.GetItemText(b).Contains(_wantSound, StringComparison.OrdinalIgnoreCase))
                    { _wadPick.Select(b); OpenBank(b); break; }
            }
            if (_wantPlay != null)
                for (int k = 0; k < _rideList.ItemCount; k++)
                    if (_rideList.GetItemText(k).Contains(_wantPlay, StringComparison.OrdinalIgnoreCase))
                    { _rideList.Select(k); ShowSound(Row(k)); PlaySelected(); break; }
        }
        else if (_wantMode != null && _wantMode.StartsWith("tex", StringComparison.OrdinalIgnoreCase))
        {
            _tabs.CurrentTab = ModeTab(Mode.Textures); SetMode(Mode.Textures);
            if (_wantImage != null)
                for (int i = 0; i < _rideList.ItemCount; i++)
                    if (_rideList.GetItemText(i).Contains(_wantImage, StringComparison.OrdinalIgnoreCase))
                    { _rideList.Select(i); ShowImage(Row(i)); break; }
        }
        else if (_wantMode != null && _wantMode.StartsWith("par", StringComparison.OrdinalIgnoreCase))
        {
            _tabs.CurrentTab = ModeTab(Mode.Park); SetMode(Mode.Park);
            // ⚠ The park tab lists MAPS, not rides, so a row here is a map. Picking one AFTER the
            // mode change is what puts a ride on ground -- a --shot run that only set the mode
            // photographed the model standing on nothing at all.
            // ⚠ Default to a map in the archive that is already open. The list spans every
            // archive, so taking its first row would silently switch away from --wad.
            int pick = _rideList.ItemCount > 0
                ? _rows.FindIndex(v => v >= 0 && string.Equals(_maps[v].Wad, _lib.WadName,
                                                               StringComparison.OrdinalIgnoreCase))
                : -1;
            if (pick < 0) pick = _rows.FindIndex(v => v >= 0);
            // ⚠ Matched against the map's LABEL, not the row text: every row reads "terrain_1.mps"
            // and only the heading says which world, so the row text alone cannot pick one.
            if (_wantMap != null)
            {
                int at = _rows.FindIndex(v => v >= 0
                    && _maps[v].Label.Contains(_wantMap, StringComparison.OrdinalIgnoreCase));
                if (at >= 0) pick = at;
            }
            if (pick >= 0) { _rideList.Select(pick); LoadMap(Row(pick)); }
            // The ride shown standing on that ground is still TPW_PS2_RIDE.
            if (_wantRide != null)
                for (int i = 0; i < _lib.Rides.Count; i++)
                    if (_lib.Rides[i].Name.Contains(_wantRide, StringComparison.OrdinalIgnoreCase))
                    { ShowRide(i); break; }
        }
    }

    void BuildUi()
    {
        // ⚠ Godot's default near plane is 0.05 against a far of 4000, and this world is 210 units
        // across -- 80,000:1 spends nearly all the depth range on the first few centimetres. The
        // subject is never closer than about a unit, so the near plane starts there. Overridable,
        // because it is also the control that tells z-fighting (changes) from a coplanar-by-design
        // surface (does not).
        float near = 0.5f, far = 6000f;
        if (float.TryParse(OS.GetEnvironment("TPW_PS2_NEAR"), out var n) && n > 0f) near = n;
        _cam = new Camera3D { Current = true, Near = near, Far = far };
        AddChild(_cam);
        // ⚠⚠ ONE WorldEnvironment, kept. Adding a second for the sky put two in the tree and
        // Godot simply used the other one -- the sky loaded, reported itself, and drew nothing.
        _flatEnv = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.10f, 0.10f, 0.13f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.Black,
            AmbientLightEnergy = 0f,
            AmbientLightSkyContribution = 0f,
        };
        _sky = new WorldEnvironment { Environment = _flatEnv };
        AddChild(_sky);
        AddChild(_weather.Root);
        _ghostView = new GhostMarkers(path => _lib?.ReadGeneric(path));
        AddChild(_ghostView.Root);

        // ⚠⚠ A full-screen Control swallows mouse events before _UnhandledInput ever sees them.
        // Orbit appeared to work only because the left button is also used by the widgets; a
        // right-drag over the empty area was consumed and the camera never heard about it.
        // Ignore on the ROOT, Pass on the panel: the actual widgets still take their own clicks.
        var ui = new Control { AnchorRight = 1, AnchorBottom = 1,
                               MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(ui);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(PanelW, 0),
                                         MouseFilter = Control.MouseFilterEnum.Pass };
        _panel.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        ui.AddChild(_panel);
        var col = new VBoxContainer();
        _panel.AddChild(col);

        // The image pane sits BEHIND the side panel and in front of the 3D view, so switching mode
        // is just a visibility flip -- the models stay built and come back instantly.
        // An opaque backdrop, or the 3D scene shows through the image pane.
        _imageBack = new ColorRect { Visible = false, Color = new Color(0.07f, 0.07f, 0.09f),
                                     MouseFilter = Control.MouseFilterEnum.Ignore, OffsetLeft = PanelW };
        _imageBack.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(_imageBack);

        _imageView = new TextureRect
        {
            Visible = false,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            OffsetLeft = PanelW,
        };
        _imageView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(_imageView);

        // ⭐ Tabs rather than a dropdown, in the order master asked for. The Mode enum keeps its
        // old numbering so saved command-line args still work, so the tab order is mapped, not
        // assumed to match.
        // ⚠ Five tabs do not fit at the default font: the bar clips and puts scroll arrows over
        // the two end tabs, which hides Models and Movies entirely. Smaller text and no clipping.
        _tabs = new TabBar { ClipTabs = false };
        _tabs.AddThemeFontSizeOverride("font_size", 12);
        foreach (var t in new[] { "Models", "Parks", "Textures", "Sounds", "Movies" }) _tabs.AddTab(t);
        _tabs.TabSelected += i => SetMode(TabMode((int)i));
        col.AddChild(_tabs);

        var scripts = new Button { Text = "Run ride scripts" };
        scripts.Pressed += () =>
        {
            if (string.IsNullOrWhiteSpace(_discPath)) return;
            GetTree().SetMeta("tpw_disc", _discPath);
            _lib?.Dispose();
            GetTree().ChangeSceneToFile("res://RideScriptDemo.tscn");
        };
        col.AddChild(scripts);


        var visitors = new Button { Text = "Visit the park" };
        visitors.Pressed += () =>
        {
            if (string.IsNullOrWhiteSpace(_discPath)) return;
            GetTree().SetMeta("tpw_disc", _discPath);
            _lib?.Dispose();
            GetTree().ChangeSceneToFile("res://VisitorDemo.tscn");
        };
        col.AddChild(visitors);

        _wadPick = new OptionButton();
        _wadPick.ItemSelected += i => { if (_mode == Mode.Sounds) OpenBank((int)i); else OpenWad((int)i); };
        col.AddChild(_wadPick);

        _rideList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _rideList.ItemSelected += row =>
        {
            int i = Row((int)row);
            if (i < 0) return;                       // a category heading
            if (_mode == Mode.Park) LoadMap(i);
            else if (_mode == Mode.Models) ShowRide(i);
            else if (_mode == Mode.Textures) ShowImage(i);
            else if (_mode == Mode.Movies) ShowMovie(i);
            else ShowSound(i);
        };
        col.AddChild(_rideList);

        _animPick = new OptionButton();
        _animPick.ItemSelected += i => { _recordIndex = (int)i; Rebuild(); };
        col.AddChild(_animPick);

        _texOn = new CheckBox { Text = "Textures", ButtonPressed = true };
        _texOn.Toggled += _ => Rebuild();
        col.AddChild(_texOn);

        _scrub = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.001 };
        _scrub.DragStarted += () => _playing = false;
        _scrub.ValueChanged += v => { if (!_playing && _current != null) { _time = (float)v * _current.Frames; _current.SetFrame(_time); } };
        col.AddChild(_scrub);

        _playBtn = new Button { Text = "Play", Visible = false };
        _playBtn.Pressed += () => PlaySelected();
        col.AddChild(_playBtn);

        _info = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart,
                            MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddChild(_info);

        _player = new AudioStreamPlayer();
        AddChild(_player);

        // ⭐ Godot plays Ogg Theora with no plugin and no native build, which is why the movies are
        // converted once rather than decoded at runtime: the eleven .MPC files are MPEG-2
        // elementary streams inside EA's own container, and nothing off the shelf opens that.
        _video = new VideoStreamPlayer { Visible = false, Expand = true, OffsetLeft = PanelW,
                                         MouseFilter = Control.MouseFilterEnum.Ignore };
        _video.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(_video);
        ui.MoveChild(_video, 0);
    }

    /// <summary>Tab order is master's -- Models, Parks, Textures, Sounds, Movies -- and the Mode
    /// enum keeps its original numbering so existing --mode arguments still work. Mapped both
    /// ways rather than assumed to line up.</summary>
    static Mode TabMode(int tab) => tab switch
    {
        1 => Mode.Park, 2 => Mode.Textures, 3 => Mode.Sounds, 4 => Mode.Movies, _ => Mode.Models,
    };

    static int ModeTab(Mode m) => m switch
    {
        Mode.Park => 1, Mode.Textures => 2, Mode.Sounds => 3, Mode.Movies => 4, _ => 0,
    };

    /// <summary>The data index behind a list row, or -1 for a category heading.</summary>
    int Row(int row) => row >= 0 && row < _rows.Count ? _rows[row] : -1;

    /// <summary>Add a category heading. ⚠ Not selectable -- a heading that can be clicked reads as
    /// an item and shows the wrong thing.</summary>
    void AddHeading(string text)
    {
        int at = _rideList.AddItem("── " + text);
        _rideList.SetItemSelectable(at, false);
        _rideList.SetItemCustomFgColor(at, new Color(0.55f, 0.75f, 1f));
        _rows.Add(-1);
    }

    /// <summary>Clear the list AND its row map together. ⚠ They are one thing: clearing only the
    /// list leaves the map from the previous mode behind, and every row then shows the wrong item.</summary>
    void ClearList() { _rideList.Clear(); _rows.Clear(); }

    void AddRow(string text, int index)
    {
        _rideList.AddItem(text);
        _rows.Add(index);
    }

    /// <summary>F3 hides the whole panel, for looking at the scene without it.</summary>
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.F3 } && _panel != null)
            _panel.Visible = !_panel.Visible;
        if (e is not InputEventKey { Pressed: true } k) return;
        if (k.Keycode == Key.G)
        {
            _freeCam = !_freeCam;
            if (!_freeCam) StartGameCam();
            GD.Print($"[cam] {(_freeCam ? "free orbit" : "the game's camera")}");
        }
        // ⚠ Turning is an EVENT, not a held key: the console adds a whole quarter turn per press
        // and eases to it. Repeating it per frame would spin.
        else if (GameCamActive && k.Keycode == Key.Q) _game.Turn(-1);
        else if (GameCamActive && k.Keycode == Key.E) _game.Turn(1);
        else if (GameCamActive && k.Keycode == Key.Home) StartGameCam();
        else if (k.Keycode == Key.B && _mode == Mode.Park && _buildable != null)
        {
            _buildable.Visible = !_buildable.Visible;
            GD.Print($"[build] overlay {(_buildable.Visible ? "on" : "off")}");
        }
        else if (k.Keycode == Key.V && _mode == Mode.Park)
        {
            var next = _weather.Current switch
            {
                Weather.Kind.None => Weather.Kind.Rain,
                Weather.Kind.Rain => Weather.Kind.Snow,
                _ => Weather.Kind.None,
            };
            GD.Print($"[weather] {next}: {_weather.Set(_lib, next, _cam.GlobalPosition)}");
        }
        // ⭐ [ and ] slide the gate along z and print where its front edge lands. Master can see
        // the park and I cannot, so this turns "not quite right" into a number.
        // ⭐ P lays a path under the cursor, shift+P a queue, O takes back everything this
        // session laid. The cursor is the game camera's own, the one WASD already drives, because
        // on the console that IS the build cursor.
        else if (k.Keycode == Key.P && _mode == Mode.Park)
        {
            var want = k.ShiftPressed ? PathTool.Kind.Queue : PathTool.Kind.Path;
            if (!_toolOpen || _toolKind != want) OpenTool(want); else PressTool();
        }
        else if (k.Keycode == Key.Escape && _toolOpen) { CloseTool(); GD.Print("[tool] closed"); }
        // ⭐ M switches between a straight segment and an elbow. Both are kept: straight is what
        // the game allows, the elbow is what the executable's own walker does, and which one the
        // path tool really hands it is not settled.
        else if (k.Keycode == Key.M && _mode == Mode.Park && _ghost != null)
        {
            _ghost.Shape = _ghost.Shape == PathGhost.Segment.Straight
                ? PathGhost.Segment.Elbow : PathGhost.Segment.Straight;
            _ghostAt = (-1, -1, -1, -1);
            GD.Print($"[tool] segments: {_ghost.Shape}");
        }
        else if (k.Keycode == Key.O && _mode == Mode.Park && _paths != null)
        {
            _paths.Undo();
            _runX = _runY = -1;
            RebuildFloor();
            GD.Print("[path] taken back");
        }
        else if (k.Keycode is Key.Bracketleft or Key.Bracketright && _mode == Mode.Park)
        {
            _gateNudge += k.Keycode == Key.Bracketright ? 0.25f : -0.25f;
            LoadGate();
        }
    }

    void SetMode(Mode m)
    {
        _mode = m;
        _imageView.Visible = _imageBack.Visible = m == Mode.Textures;
        _animPick.Visible = _texOn.Visible = _scrub.Visible = m == Mode.Models;
        _playBtn.Visible = m == Mode.Sounds || m == Mode.Movies;
        _video.Visible = m == Mode.Movies;
        if (m != Mode.Movies) _video.Stop();
        // ⚠ Hide the model too. A transparent image pane over a lit 3D scene reads as a bug.
        if (_current != null) _current.Root.Visible = m == Mode.Models || (m == Mode.Park && _parkRide);
        if (_park != null) _park.Root.Visible = m == Mode.Park;
        if (_gate != null) _gate.Root.Visible = m == Mode.Park;
        if (_sky != null) _sky.Environment = m == Mode.Park && _skyEnv != null ? _skyEnv : _flatEnv;
        _weather.Root.Visible = m == Mode.Park;
        if (_buildable != null && m != Mode.Park) _buildable.Visible = false;
        if (m == Mode.Sounds) FillBankPicker();
        else if (m == Mode.Movies) FillMovieList();
        else FillWadPicker();
    }

    /// <summary>Refill the archive picker, KEEPING the archive already open.
    ///
    /// ⚠ This used to `Select(0); OpenWad(0)` unconditionally, so every mode change silently threw
    /// the user back to DATA.WAD -- and DATA.WAD holds no rides at all, so switching to Park after
    /// choosing JUNGLE landed on an empty catalogue and a character model, which reads as the park
    /// being broken rather than as the archive having been changed underneath it.</summary>
    void FillWadPicker()
    {
        var keep = _lib.WadName;
        _wadPick.Clear();
        var wads = _lib.Wads();
        foreach (var w in wads) _wadPick.AddItem(w);
        if (_wadPick.ItemCount == 0) return;
        int at = keep == null ? 0 : wads.FindIndex(w => w.Equals(keep, StringComparison.OrdinalIgnoreCase));
        if (at < 0) at = 0;
        _wadPick.Select(at);
        // Only re-read when it actually changed; re-opening costs a full archive decompress.
        if (keep == null || at != wads.FindIndex(w => w.Equals(keep, StringComparison.OrdinalIgnoreCase)))
            OpenWad(at);
        else FillList();
    }

    void FillBankPicker()
    {
        _banks = _lib.SoundBanks();
        _wadPick.Clear();
        foreach (var b in _banks) _wadPick.AddItem(b.Path);
        if (_banks.Count > 0) { _wadPick.Select(0); OpenBank(0); }
        else _info.Text = "no sound banks on this disc";
    }

    /// <summary>The converted movies. They are GAME DATA, so they never live in the repo -- the
    /// folder is given by TPW_PS2_MOVIES, or found beside the disc as `movies/`.</summary>
    void FillMovieList()
    {
        _wadPick.Clear(); ClearList(); _movies.Clear();
        var dir = OS.GetEnvironment("TPW_PS2_MOVIES");
        if (string.IsNullOrWhiteSpace(dir) && !string.IsNullOrEmpty(_discPath))
            dir = Path.Combine(Path.GetDirectoryName(_discPath) ?? ".", "movies");
        _wadPick.AddItem(dir ?? "(no movie folder)");
        if (dir == null || !Directory.Exists(dir))
        {
            _info.Text = "No converted movies.\n\nThe disc's 11 .MPC files are MPEG-2 elementary\n"
                       + "streams in EA's own container, which nothing off\nthe shelf opens. "
                       + "Convert them once with ffmpeg and\npoint TPW_PS2_MOVIES at the folder.\n\n"
                       + "looked in: " + (dir ?? "nowhere");
            return;
        }
        foreach (var f in Directory.GetFiles(dir, "*.ogv").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        { AddRow(Path.GetFileNameWithoutExtension(f), _movies.Count); _movies.Add(f); }
        _info.Text = _movies.Count + " movies in " + dir;
        if (_movies.Count > 0) { _rideList.Select(0); ShowMovie(0); }
    }

    void ShowMovie(int i)
    {
        if (i < 0 || i >= _movies.Count) return;
        var path = _movies[i];
        _video.Stop();
        var st = new VideoStreamTheora();
        st.File = path;
        _video.Stream = st;
        var len = new FileInfo(path).Length;
        // ⚠ This line used to say "silent: the EA audio codec is unidentified". It is identified --
        // adpcm_ea, 48 kHz stereo -- and the movies carry sound. A stale note that ACCUSES the data
        // of a gap that has been closed is worse than no note at all.
        _info.Text = Path.GetFileName(path) + "\n" + (len / 1024 / 1024.0).ToString("0.0") + " MB\n"
                   + "Ogg Theora + Vorbis\nconverted from MPEG-2 + EA ADPCM";
    }

    /// <summary>Open one `.SDT` and list what is in it.</summary>
    void OpenBank(int i)
    {
        ClearList();
        _bank = null;
        _advisorLanguage = null;
        _advisorError = null;
        if (i < 0 || i >= _banks.Count) return;
        try { _bank = new SoundBank(_lib.ReadDisc(_banks[i])); }
        catch (Exception ex) { _info.Text = _banks[i].Path + "\n" + ex.Message; return; }
        if (_banks[i].Path.Contains("/ADVISOR/", StringComparison.OrdinalIgnoreCase))
        {
            _advisorLanguage = _banks[i].Path.Split('/')[3].ToUpperInvariant() switch
            { "ENGLISH" => "English", "FRENCH" => "French", "GERMAN" => "German", _ => null };
            try
            {
                using var disc = new Disc(_discPath);
                _advisor ??= AdvisorCatalogue.Load(disc);
                _advisor.ValidateText(_text);
                if (_advisorLips == null)
                {
                    var file = disc.Files().Single(f => f.Path.Equals("/DATA/LIPS.WAD", StringComparison.OrdinalIgnoreCase));
                    _advisorLips = new WadArchive(disc.Read(file.Extent, file.Size));
                }
            }
            catch (Exception ex) { _advisorError = ex.Message; GD.PrintErr("[advisor] " + ex.Message); }
        }
        for (int k = 0; k < _bank.Sounds.Count; k++)
        {
            var snd = _bank.Sounds[k];
            string kind = snd.IsEmpty ? "empty" : snd.IsAdpcm ? "vag" : snd.Channels == 2 ? "stereo" : "mono";
            AddRow(snd.Name + "   " + kind + "  " + (snd.Milliseconds / 1000.0).ToString("0.0") + "s", k);
        }
        _info.Text = _banks[i].Path + "\n" + _bank.Sounds.Count + " sounds";
        if (_bank.Sounds.Count > 0) { _rideList.Select(0); ShowSound(0); }
    }

    void ShowSound(int i)
    {
        if (_bank == null || i < 0 || i >= _bank.Sounds.Count) return;
        var s = _bank.Sounds[i];
        string what = s.IsEmpty ? "an empty slot"
                    : s.IsAdpcm ? "Sony PS-ADPCM"
                    : "MPEG-2 Layer II, " + (s.Channels == 2 ? "stereo" : "mono");
        _info.Text = s.Name + "\n" + what + "\n"
                   + (s.End - s.Start) + " bytes, " + (s.Milliseconds / 1000.0).ToString("0.00") + " s"
                   + "\ntag 0x" + s.Tag.ToString("x2");
        if (_advisorError != null) _info.Text += "\nAdvisor metadata unavailable: " + _advisorError;
        else if (_advisorLanguage != null && _advisor != null)
        {
            string language = _advisorLanguage switch { "French" => "fre", "German" => "ger", _ => "eng" };
            var descriptions = new List<string>();
            foreach (var message in _advisor.Messages)
                for (int v = 0; v < message.VariantCount; v++)
                    if (message.Voices[v].SoundIndex == i)
                    {
                        try
                        {
                            var binding = _advisor.Bind(message.Id, v, _advisorLanguage, _bank, _advisorLips, _text, language);
                            string dialogue = binding.Subtitle ?? (message.HasText
                                ? $"No {language} text in {_text.Locale}." : "This message has no displayed text.");
                            descriptions.Add(dialogue + "\n" + (binding.Lip == null ? "Lip sync unavailable."
                                : $"Lip sync: {binding.Lip.Microseconds.Count} transitions.")
                                + (binding.SoundStemMatches ? "" : "\nSound and lip names differ in the disc data."));
                        }
                        catch (Exception ex) { descriptions.Add("Advisor metadata unavailable: " + ex.Message); }
                    }
            _info.Text += "\n\n" + (descriptions.Count == 0 ? "No advisor message references this sound."
                : string.Join("\n\n", descriptions.Distinct()));
        }
        _playBtn.Disabled = s.IsEmpty;
    }

    /// <summary>Decode the selected sound and play it.
    ///
    /// ⭐ PS-ADPCM is decoded here and handed over as plain PCM, so it is exact. The sample RATE is
    /// not in the bank, so it is DERIVED: the block count gives the sample count and the header's
    /// own length in milliseconds gives the duration, and one divides into the other.
    ///
    /// ⚠ MPEG Layer II is handed to Godot's MP3 stream, which may or may not accept it -- the
    /// engine's decoder is built for Layer III. If it refuses, the panel says so rather than
    /// failing silently.</summary>
    void PlaySelected()
    {
        int i = _rideList.GetSelectedItems().Length > 0 ? _rideList.GetSelectedItems()[0] : -1;
        if (_mode == Mode.Movies) { _video.Play(); return; }
        if (_bank == null || i < 0 || i >= _bank.Sounds.Count) return;
        var s = _bank.Sounds[i];
        if (s.IsEmpty) return;
        _player.Stop();
        try
        {
            if (s.IsAdpcm)
            {
                var pcm = Vag.Decode(_bank.Data, s.Start, s.End);
                // ⭐ 22050 Hz, and it is MEASURED rather than assumed: decoding all 356 PS-ADPCM
                // sounds and dividing each one's sample count by the duration its own header
                // declares puts every single one between 22,050 and 22,700 Hz -- the spread is the
                // millisecond field's rounding, not a spread of rates. Same rate as the MPEG side.
                const int rate = 22050;
                var bytes = new byte[pcm.Length * 2];
                Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
                _player.Stream = new AudioStreamWav
                {
                    Format = AudioStreamWav.FormatEnum.Format16Bits,
                    MixRate = rate, Stereo = false, Data = bytes,
                };
                _info.Text += "\nplaying: " + pcm.Length + " samples at " + rate + " Hz";
            }
            else
            {
                var raw = new byte[s.End - s.Start];
                Array.Copy(_bank.Data, s.Start, raw, 0, raw.Length);
                // ⚠⚠ NOT AudioStreamMP3. The engine's decoder is built for Layer III and returns a
                // zero-length stream for the Layer II the disc uses -- 1,849 of its 2,220 sounds.
                // Decoded here to PCM instead, which also means one code path for every sound.
                var dec = Mpeg.DecodeToPcm16(raw);
                if (dec == null) { _info.Text += "\nthe Layer II decoder returned nothing"; return; }
                var (pcm2, rate2, ch2) = dec.Value;
                _player.Stream = new AudioStreamWav
                {
                    Format = AudioStreamWav.FormatEnum.Format16Bits,
                    MixRate = rate2, Stereo = ch2 == 2, Data = pcm2,
                };
                _info.Text += "\nplaying: " + (pcm2.Length / 2 / ch2) + " samples at "
                            + rate2 + " Hz, " + (ch2 == 2 ? "stereo" : "mono");
            }
            _player.Play();
            GD.Print("[snd] " + s.Name + " -> " + _info.Text.Replace("\n", " | "));
        }
        catch (Exception ex)
        {
            _info.Text += "\ncould not play: " + ex.Message;
            GD.PrintErr("[snd] " + s.Name + " FAILED: " + ex);
        }
    }

    void FillList()
    {
        ClearList();
        if (_mode == Mode.Sounds) return;      // the bank picker fills this list instead
        if (_mode == Mode.Park) { FillMapList(); return; }
        if (_mode == Mode.Models)
        {
            // ⭐ Grouped by the first path segment, which is the disc's own categorisation:
            // Rides, Features, Shops, Sideshow. 300-odd flat entries is a scroll, not a list.
            foreach (var g in _lib.Rides.Select((r, i) => (r, i))
                                        .GroupBy(t => Category(t.r.Name))
                                        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                AddHeading($"{g.Key}  ({g.Count()})");
                foreach (var (r, i) in g) AddRow("   " + Leaf(r.Name), i);
            }
            int first = _rows.FindIndex(v => v >= 0);
            if (first >= 0) { _rideList.Select(first); ShowRide(_rows[first]); }
        }
        else
        {
            _images = _lib.Images();
            foreach (var g in _images.Select((e, i) => (e, i))
                                     .GroupBy(t => Category(t.e.Path.TrimStart('/')))
                                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                AddHeading($"{g.Key}  ({g.Count()})");
                foreach (var (e, i) in g) AddRow("   " + Leaf(e.Path.TrimStart('/')), i);
            }
            int first = _rows.FindIndex(v => v >= 0);
            if (first >= 0) { _rideList.Select(first); ShowImage(_rows[first]); }
            else _info.Text = "no images in this archive";
        }
    }

    static string Category(string path)
    {
        int i = path.IndexOf('/');
        return i > 0 ? path[..i] : "(root)";
    }

    static string Leaf(string path)
    {
        int i = path.LastIndexOf('/');
        return i >= 0 ? path[(i + 1)..] : path;
    }

    /// <summary>Every park on the disc, grouped by world. ⚠ Built once: finding them means opening
    /// each archive, so it is done on the first visit to the tab rather than at startup.</summary>
    /// <summary>Dump the cell-byte distributions for every park on the disc, then quit.
    ///
    /// ⭐ The cell is THREE TABLE INDICES, not a height: bits 0-3, bits 4-7 and byte1. Jungle
    /// cannot referee any hypothesis about them -- its bits 4-7 are {0,4} and its 0x3C is zero
    /// throughout -- so the FIRST thing any reading of them needs is what the other seven parks
    /// actually contain. Printed per park and per field so a claim can be checked against a
    /// distribution rather than against one world that is blind to the question.</summary>
    void DumpFieldStats()
    {
        var open = _lib.WadName;
        GD.Print("park                     cells  drawn   bits0-3 histogram                  "
               + "bits4-7 histogram                  0x3C non-zero");
        foreach (var m in _maps)
        {
            try
            {
                _lib.OpenWad(m.Wad);
                var entry = _lib.TerrainModels().FirstOrDefault(
                    e => string.Equals(e.Path, m.Path, StringComparison.OrdinalIgnoreCase));
                if (entry == null) continue;
                var f = new Model(_lib.Read(entry)).Field;
                if (f == null) { GD.Print($"{m.Label,-24} no field"); continue; }
                var lo = new int[16]; var hi = new int[16];
                int drawn = 0, nz3c = 0;
                for (int y = 0; y < f.Height; y++)
                    for (int x = 0; x < f.Width; x++)
                    {
                        byte b = f.Raw0(x, y);
                        lo[b & 0x0F]++; hi[(b >> 4) & 0x0F]++;
                        if (f.Drawn(x, y)) drawn++;
                        if ((b & 0x3C) != 0) nz3c++;
                    }
                GD.Print($"{m.Label,-24} {f.Count,6} {drawn,6}   {Hist(lo),-34} {Hist(hi),-34} {nz3c,6}");
            }
            catch (Exception ex) { GD.PrintErr($"[stats] {m.Label}: {ex.Message}"); }
        }
        if (open != null) _lib.OpenWad(open);
        GetTree().Quit();
    }

    /// <summary>Only the values that OCCUR, as value:count. A row of zeroes hides the shape.</summary>
    static string Hist(int[] counts) => string.Join(" ",
        counts.Select((c, v) => (c, v)).Where(t => t.c > 0).Select(t => $"{t.v}:{t.c}"));

    void FillMapList()
    {
        if (!_mapsBuilt)
        {
            _mapsBuilt = true;
            var open = _lib.WadName;
            var t0 = Time.GetTicksMsec();
            foreach (var wad in _lib.Wads())
            {
                try
                {
                    _lib.OpenWad(wad);
                    foreach (var m in _lib.TerrainModels())
                        _maps.Add((wad, m.Path, $"{Leaf(wad).Replace(".WAD", "")}  {Leaf(m.Path)}"));
                }
                catch (Exception ex) { GD.PrintErr($"[maps] {wad}: {ex.Message}"); }
            }
            if (open != null) _lib.OpenWad(open);
            GD.Print($"[maps] {_maps.Count} parks across {_lib.Wads().Count} archives "
                   + $"in {Time.GetTicksMsec() - t0} ms");
        }
        foreach (var g in _maps.Select((m, i) => (m, i))
                               .GroupBy(t => t.m.Wad)
                               .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddHeading(Leaf(g.Key).Replace(".WAD", ""));
            foreach (var (m, i) in g) AddRow("   " + Leaf(m.Path), i);
        }
        if (_maps.Count == 0) _info.Text = "no parks found";
        if (System.Environment.GetEnvironmentVariable("TPW_PS2_FIELD_STATS") == "1") DumpFieldStats();
    }

    /// <summary>Open the archive a park lives in and load that terrain file.</summary>
    void LoadMap(int i)
    {
        if (i < 0 || i >= _maps.Count) return;
        var m = _maps[i];
        if (!string.Equals(m.Wad, _lib.WadName, StringComparison.OrdinalIgnoreCase))
        {
            _lib.OpenWad(m.Wad);
            _texCache.Clear();
            IndexRides();
            for (int w = 0; w < _wadPick.ItemCount; w++)
                if (_wadPick.GetItemText(w) == m.Wad) { _wadPick.Select(w); break; }
        }
        // ⚠ Force a reload: the terrain is cached by path, and switching park within one archive
        // would otherwise keep the old one.
        _wantTerrain = m.Path;
        _terrainPath = null;
        _terrain?.Root.QueueFree();
        _terrain = null;
        _park.Field = null;
        ShowParkOnly();
        // ⭐ Park mode has its own camera and its own keys, and the help text left over from the
        // orbit view describes none of them. Master was being told the controls over chat.
        _info.Text = m.Label + "\n\nthe game's own camera\n"
                   + "WASD move  |  Q/E turn a quarter  |  R/F zoom\n"
                   + "Z/X dolly  |  Home reset  |  G free orbit\n"
                   + "[ / ] nudge the gate  |  V weather  |  B buildable  |  F3 hide this panel\n"
                   + "RMB path tool (shift+RMB queue)  |  LMB press: start a run, again to lay\n"
                   + "O take it back  |  M straight/elbow segments  |  Esc close the tool";
    }

    /// <summary>Show one image at its own size, or the reason it cannot be shown.</summary>
    void ShowImage(int i)
    {
        if (i < 0 || i >= _images.Count) return;
        var e = _images[i];
        _imageView.Texture = null;
        if (e.Path.EndsWith(".ssh", StringComparison.OrdinalIgnoreCase))
        {
            // ⚠ Listed deliberately. `.ssh` is an MPEG intra picture the PS2 hands to its IPU; the
            // container is read (see tools/ssh.py) and the pixels are not decoded yet. Showing the
            // entry with an honest reason beats leaving 806 images out of the list.
            try
            {
                var raw = _lib.Read(e);
                uint n = BitConverter.ToUInt32(raw, 8);
                int off = (int)BitConverter.ToUInt32(raw, 0x14);
                int w = BitConverter.ToUInt16(raw, off + 4), h = BitConverter.ToUInt16(raw, off + 6);
                _info.Text = e.Path + "\n" + w + "x" + h + ", " + n + (n == 1 ? " entry, " : " entries, ")
                           + raw.Length + " bytes\n"
                           + "SHPS: an MPEG intra picture for the PS2's IPU.\n"
                           + "Container read, pixels not decoded yet.";
            }
            catch (Exception ex) { _info.Text = e.Path + "\n" + ex.Message; }
            return;
        }
        try
        {
            var tga = new Targa(_lib.Read(e));
            var img = Image.CreateFromData(tga.Width, tga.Height, false, Image.Format.Rgba8, tga.Pixels);
            _imageView.Texture = ImageTexture.CreateFromImage(img);
            int texels = tga.Width * tga.Height;
            _info.Text = e.Path + "\n" + tga.Width + "x" + tga.Height + "\n"
                       + tga.ClearTexels + " clear, " + tga.PartialAlpha + " partly clear of " + texels + "\n"
                       + (tga.Translucent ? "blended (translucent)" : "cutout");
        }
        catch (Exception ex) { _info.Text = e.Path + "\ndid not decode: " + ex.Message; }
    }

    /// <summary>Ride definitions for the archive now open. Per-WAD on purpose: the viewer shows
    /// one at a time, and loading all sixteen cost minutes of sector reads at startup.</summary>
    void IndexRides()
    {
        try
        {
            _cat = new RideCatalogue();
            _cat.AddWad(_lib.Wad, _lib.WadName);
            GD.Print($"[park] {_lib.WadName}: {_cat.All.Count} rides, {_cat.ById.Count} ids, "
                     + $"{_cat.All.Count(d => d.ModelPath != null)} with a model");
        }
        catch (Exception ex) { GD.PrintErr($"[park] catalogue failed: {ex}"); _cat = null; }
    }

    void OpenWad(int i)
    {
        GD.Print($"[v] opening {_wadPick.GetItemText(i)}"); _lib.OpenWad(_wadPick.GetItemText(i)); GD.Print($"[v] indexed {_lib.Rides.Count} rides");
        _texCache.Clear();
        IndexRides();
        FillList();
    }

    void ShowRide(int i)
    {
        _ride = _lib.Rides[i];
        _texCache.Clear();
        _anim = null; _records.Clear(); _animPick.Clear(); _recordIndex = 0;
        if (_ride.Animation != null)
        {
            try
            {
                _anim = new Aps(_lib.Read(_ride.Animation));
                foreach (var rec in _anim.Records())
                {
                    _records.Add(rec);
                    _animPick.AddItem($"{rec.SlotName}  {rec.DurationFrames} frames"
                                      + (rec.Skeletal ? "  skeletal" : "")
                                      + (rec.Shared ? "  (shared)" : ""));
                }
            }
            catch (Exception ex)
            {
                // ⚠ RESET EVERYTHING THE TRY TOUCHED. Leaving _records populated while _anim went
                // null is what turned a parse error into a NullReferenceException three frames
                // away, in a place that had nothing to do with the real fault.
                GD.PrintErr($"[v] animation failed: {ex}");
                _anim = null; _records.Clear(); _animPick.Clear();
            }
        }
        if (_animPick.ItemCount > 0) _animPick.Select(0);
        Rebuild();
    }

    /// <summary>The ride whose `.sam` sits in the same folder as the model being shown. A ride is
    /// a directory bundle, so the definition is found by PATH rather than by name -- names repeat
    /// across worlds and 32 of the 36 repeats carry a different id.</summary>
    RideDefinition DefinitionFor(WadArchive.Entry model)
    {
        if (_cat == null || model == null) return null;
        int slash = model.Path.LastIndexOf('/');
        if (slash < 0) return null;
        var dir = model.Path[..(slash + 1)];
        foreach (var d in _cat.All)
        {
            int s2 = d.Source.LastIndexOf('/');
            if (s2 < 0) continue;
            if (d.Source[..(s2 + 1)].EndsWith(dir, StringComparison.OrdinalIgnoreCase)) return d;
        }
        return null;
    }

    /// <summary>Stand the current ride on park ground at its own footprint, and say what the game
    /// would say about it. ⚠ The headline is the TABLE's name, not `Info.Name`: those disagree on
    /// 70 of the 273 rides that reach a row.</summary>
    /// <summary>Put the world's own ground under the park. Built exactly like a ride -- terrain is
    /// a `.mps`, not a format of its own -- and rebuilt only when the archive changes, since it is
    /// the largest model on the disc at ~700 KB and 17,000 triangles.</summary>
    void LoadTerrain()
    {
        var models = _lib.TerrainModels();
        if (models.Count == 0)
        {
            if (_terrain != null) { _park.SetTerrain(null); _terrain = null; _terrainPath = null; _terrainModel = null; }
            _park.ShowGrass = true;
            return;
        }
        // ⭐ The park tab picks the terrain file; models[0] is only the default.
        var pick = models[0];
        if (_wantTerrain != null)
            pick = models.FirstOrDefault(m =>
                string.Equals(m.Path, _wantTerrain, StringComparison.OrdinalIgnoreCase)) ?? models[0];
        if (_terrainPath == pick.Path && _terrain != null) return;
        try
        {
            var tm = new Model(_lib.Read(pick));
            // ⚠ A new terrain is a NEW grid. The path tool holds the old one, and its cells are
            // sized to it, so keeping it would write turns for a grid that is no longer drawn.
            _terrainModel = tm;
            _paths = null;
            _terrain = new AnimatedModel(tm, null, null, m => TextureNear(pick.Path, m));
            _terrain.SetFrame(0);
            AddChild(_terrain.Root);
            _park.SetTerrain(_terrain.Root);
            _terrainPath = pick.Path;
            // ⚠ Hide the synthetic grass. Two floors at the same height read as z-fighting.
            // ⚠ The grid is the PLAYABLE FLOOR once it is sized to the hole, not decoration. It
            // was hidden on the assumption that any grass under real terrain is a second floor --
            // true when it spanned the whole world, wrong once it fills the hole the terrain
            // leaves for it.
            var (lo, hi) = Park.DrawnBounds(_terrain.Root, inParent: true);
            // ⚠ Say how many of its materials found a texture. A material that silently resolves to
            // null renders flat white, which reads as "the terrain has no textures" rather than as
            // "the lookup did not find them" -- and the first is a fact about the disc, the second
            // a bug in here.
            int got = 0, missed = 0;
            foreach (var mat in tm.Materials)
            {
                if (mat == null) continue;
                if (TextureNear(pick.Path, mat).Tex != null) got++; else missed++;
            }
            GD.Print($"[terrain] {pick.Path}  {tm.Meshes.Count} meshes  "
                   + $"extent {hi.X - lo.X:F1} x {hi.Z - lo.Z:F1}  height {hi.Y - lo.Y:F1}  "
                   + $"textures {got} resolved, {missed} MISSING");
            _terrainSize = new Vector2(hi.X - lo.X, hi.Z - lo.Z);
            // ⚠ Bisect the renderer against the offline reader. The bbox from DrawnBounds matched
            // python's to four decimals, yet the marked hole renders half a world from the gap the
            // geometry has -- and a bounding box is invariant under exactly the transforms that
            // would move the contents. Per-mesh centres are not.
            if (System.Environment.GetEnvironmentVariable("TPW_HOLE_DEBUG") == "1")
            {
                int shown = 0;
                void Dump(Node n, Transform3D acc)
                {
                    var t = n is Node3D n3 && n != _terrain.Root ? acc * n3.Transform : acc;
                    if (n is MeshInstance3D mi && mi.Mesh != null && shown++ < 200)
                    {
                        var bx = mi.GetAabb();
                        var a = t * bx.Position; var b = t * (bx.Position + bx.Size);
                        GD.Print($"[mesh] {shown - 1,3}  X {Math.Min(a.X, b.X),9:F2} ..{Math.Max(a.X, b.X),9:F2}"
                               + $"   Z {Math.Min(a.Z, b.Z),9:F2} ..{Math.Max(a.Z, b.Z),9:F2}");
                    }
                    foreach (var c in n.GetChildren()) Dump(c, t);
                }
                Dump(_terrain.Root, Transform3D.Identity);
            }
            // ⭐ The plot's ground comes off the disc, not out of a Color. `jgr_bas2..6` are the
            // jungle ground tiles (64x64, green); `jpa_*` are the paths. Named by the model's own
            // convention so the ordinary resolver finds them beside the terrain.
            // ⭐ Each cell names its own ground tile: byte1 indexes THIS model's material table.
            // Resolved per index and cached, with the terrain's own path as the lookup owner so
            // the nearest-wins search starts in /terrain/ where the tiles live.
            // ⭐ And the other half of a laid tile: which way round it faces. The authored ground
            // has no rotation, so this is 0 everywhere until a path is laid.
            _park.TurnsForCell = (x, y) => _paths?.Turns(x, y) ?? 0;
            var matCache = new Dictionary<int, Material>();
            _park.MaterialForCell = idx =>
            {
                if (matCache.TryGetValue(idx, out var got)) return got;
                Material made = null;
                // ⚠ 0 is a sentinel (gte_wal1 in jungle, sgr_tnk2 in space) -- not a ground tile.
                if (idx > 0 && idx < tm.Materials.Count && tm.Materials[idx] != null)
                {
                    var t = TextureNear(pick.Path, tm.Materials[idx]);
                    if (t.Tex != null)
                        made = Ps2Materials.Ground(t.Tex);
                }
                matCache[idx] = made;
                return made;
            };

            var tile = _lib.GroundTileName();
            var grass = tile != null ? TextureNear(pick.Path, tile + ".ssh") : (null, false);
            GD.Print($"[park] ground tile '{tile ?? "(none found)"}' -> {(grass.Tex != null ? "resolved" : "UNRESOLVED")}");
            if (grass.Tex != null)
                _park.GroundMaterial = Ps2Materials.Ground(grass.Tex);
            else GD.PrintErr("[park] no ground tile resolved -- falling back to flat colour");

            // ⭐⭐ Ask the disc where the park is before measuring anything.
            Vector2? apO = null, apS = null;
            var ap = Park.AuthoredPlot(tm, _terrain.Root.Transform);
            if (ap != null)
            {
                _park.PlotSpace = ap;
                // corners of the plot in world space, whatever its orientation
                var c0 = ap.Value.ToWorld * ap.Value.LocalMin;
                var c1 = ap.Value.ToWorld * (ap.Value.LocalMin + ap.Value.LocalSize);
                var pmin = new Vector3(Math.Min(c0.X, c1.X), Math.Min(c0.Y, c1.Y), Math.Min(c0.Z, c1.Z));
                var pmax = new Vector3(Math.Max(c0.X, c1.X), Math.Max(c0.Y, c1.Y), Math.Max(c0.Z, c1.Z));
                apO = new Vector2(pmin.X, pmin.Z);
                apS = new Vector2(pmax.X - pmin.X, pmax.Z - pmin.Z);
                GD.Print($"[plot] AUTHORED {pmin} .. {pmax}  => {apS.Value.X:F2} x {apS.Value.Y:F2} cells; "
                       + $"node basis X {ap.Value.ToWorld.Basis.X} Z {ap.Value.ToWorld.Basis.Z}");
            }
            else GD.PrintErr("[plot] no heightfield node -- falling back to the measured hole");

            // ⭐ The authored grid wins over anything measured: same numbers, but it carries the
            // per-cell data too.
            _park.Field = tm.Field;
            if (tm.Field != null)
                GD.Print($"[field] authored grid {tm.Field.Width} x {tm.Field.Height} = {tm.Field.Count} cells "
                       + $"from the terrain file (runtime copies this verbatim)");

            BakeGround();
            var (ho, hs, hy, hc) = Park.FindHole(_terrain.Root, 160, apO, apS);
            _holeOrigin = ho; _holeSize = hs; _holeCells = hc;
            // ⚠⚠ The floor goes at the height of the ground AROUND the hole, NOT at the terrain's
            // minimum. The terrain spans only -1.3..9.9 in Y and that minimum is the sea floor, so
            // `lo.Y` laid the grid UNDER the island: bounds said it was in the hole, the picture
            // showed no grid, and I spent two renders reading the miss as a lateral placement bug.
            _holeY = hy;
            int play = 0;
            if (hc != null) foreach (var b in hc) if (b) play++;
            GD.Print($"[hole] origin {ho.X:F1},{ho.Y:F1}  box {hs.X:F1} x {hs.Y:F1}  "
                   + $"GROUND {play} cells ({100.0 * play / Math.Max(1, hs.X * hs.Y):F0}% of the box)  "
                   + $"floor y={hy:F2} (terrain base {lo.Y:F2}, top {hi.Y:F2})");
        }
        catch (Exception ex) { GD.PrintErr($"[terrain] {pick.Path}: {ex.Message}"); }
    }

    /// <summary>Put this world's own sky behind the park. ⚠ Only in park mode: a model on the
    /// Models tab is being LOOKED AT, and a sky behind it is scenery competing with the subject.</summary>
    void LoadSky()
    {
        var sky = SkyDome.Build(_lib, out var report);
        GD.Print($"[sky] {report}");
        if (sky == null) { _skyEnv = null; _sky.Environment = _flatEnv; return; }
        _skyEnv = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            // Mesh lighting comes from the ELF through Ps2Materials; the sky is a backdrop.
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.Black,
            AmbientLightEnergy = 0f,
            AmbientLightSkyContribution = 0f,
        };
        _sky.Environment = _mode == Mode.Park && _skyEnv != null ? _skyEnv : _flatEnv;
    }

    /// <summary>Lay a flat marker over every cell nothing may be built on, so the authored map is
    /// something you can look at rather than a histogram.
    ///
    /// ⚠ Only over cells the terrain DRAWS. A cell that is both undrawn and unbuildable is
    /// simply outside the park, and colouring those paints the whole surround red and says
    /// nothing.</summary>
    /// <summary>Make the path tool for the park that has just loaded.
    ///
    /// ⚠ The tables are read from the owner's own executable, never baked here. A disc whose build
    /// does not carry them leaves the tool off rather than laying guessed art.</summary>
    void MakePathTool()
    {
        _paths = null;
        if (_terrainModel?.Field == null) return;
        if (_pieces == null)
        {
            try
            {
                var exe = _lib.Executable();
                if (exe != null) _pieces = PathPieces.ReadExecutable(exe);
                else GD.Print("[path] no SLES_500.32 on this disc -- path laying is off");
            }
            catch (Exception e) { GD.PrintErr($"[path] the piece tables would not read: {e.Message}"); }
        }
        if (_pieces == null) return;
        _paths = new PathTool(_terrainModel, _pieces);
        _ghost = new PathGhost(_paths);
        if (_wantSegments != null)
            _ghost.Shape = _wantSegments.StartsWith("elbow", StringComparison.OrdinalIgnoreCase)
                ? PathGhost.Segment.Elbow : PathGhost.Segment.Straight;
        CloseTool();
        GD.Print($"[path] {_paths.Report}"
               + (_paths.Ready ? $"; {_pieces.Path.Count} path and {_pieces.Queue.Count} queue pieces" : ""));
        if (!_paths.Ready) { _paths = null; return; }
        if (_pathTest || System.Environment.GetEnvironmentVariable("TPW_PATH_TEST") == "1") LayTestPath();
        if (_ghostTest || System.Environment.GetEnvironmentVariable("TPW_GHOST_TEST") == "1") ShowTestGhost();
    }

    /// <summary>⭐ A CONTROL RUN, not a feature. It lays a shape that MUST come out wearing one of
    /// every piece -- a crossroads at the middle, four straight arms, four ends, and an L off the
    /// east arm for a corner and a T -- so a render says whether the table, the tile lists and the
    /// rotation are all right, instead of only whether something appeared.</summary>
    void LayTestPath()
    {
        var f = _park.Field;
        int cx = f.Width / 2, cy = f.Height / 2;
        // Walk out to a cell that can actually be built on, so the run is not silently refused.
        for (int r = 0; r < 12 && !_paths.CanLay(cx, cy); r++) { cx += 1; if (!_paths.CanLay(cx, cy)) cy += 1; }
        int laid = 0;
        for (int i = -4; i <= 4; i++)
        {
            if (_paths.Lay(cx + i, cy)) laid++;
            if (_paths.Lay(cx, cy + i)) laid++;
        }
        for (int i = 1; i <= 3; i++) if (_paths.Lay(cx + 4, cy + i)) laid++;   // the L: a corner, then a T
        for (int i = 1; i <= 2; i++) if (_paths.Lay(cx + 4 + i, cy + 2)) laid++;
        GD.Print($"[path] control run at ({cx},{cy}): {laid} cells laid; "
               + $"centre {_paths.Describe(cx, cy)}, corner {_paths.Describe(cx + 4, cy)}");
        // ⭐ Say WHERE each piece landed, in world coordinates as well as cells. A picture of a
        // symmetric cross cannot tell a correct layout from a mirrored one; the L can, and only if
        // it is possible to say which end of the screen it should be at.
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
                if (_paths.KindAt(x, y) != PathTool.Kind.None)
                {
                    var w = _park.CellCentre(x, y);
                    GD.Print($"[path]   ({x,3},{y,3}) -> {_park.Field.Material(x, y),3} turns {_paths.Turns(x, y)} "
                           + $"at world {w.X:F1},{w.Z:F1}");
                }
        RebuildFloor();
    }

    /// <summary>⭐ A CONTROL FOR THE GHOST, not a feature. It lays a short run of real path, then
    /// opens the tool and starts a run whose second leg walks straight along that path -- so the
    /// picture MUST show two different markers, the plain one on the empty leg and the link rings
    /// on the leg that is already path. One marker everywhere would mean the verdict never reached
    /// the art, which is exactly the failure a screenshot of a single-colour ghost would hide.</summary>
    void ShowTestGhost()
    {
        if (_paths == null || _ghost == null) return;
        var f = _park.Field;
        int cx = f.Width / 2, cy = f.Height / 2;
        for (int r = 0; r < 12 && !_paths.CanLay(cx, cy); r++) { cx += 1; if (!_paths.CanLay(cx, cy)) cy += 1; }
        for (int i = 0; i <= 3; i++) _paths.Lay(cx, cy + i);
        RebuildFloor();
        OpenTool(PathTool.Kind.Path);
        // ⭐ Pin the build cursor to the target cell, so the ghost the frame loop rebuilds is the
        // one printed below rather than a second, different run. A capture has no mouse to put it
        // under, and a control that only exercised a separate code route would be worth nothing.
        // ⭐ The run ENDS ON the path just laid, so the picture must show a straight segment of
        // plain markers with ONE connect symbol on its last tile. A string of symbols, or none,
        // are both visible failures.
        // ⚠ OFF-AXIS ON PURPOSE. A cursor square-on to the start draws the same picture in both
        // shapes, so it would photograph a straight segment and say nothing about which one ran.
        // From (cx-5, cy+3) to (cx, cy+1) the straight segment snaps to x and stops at (cx,cy+3);
        // the elbow runs the same leg and then turns down two more.
        int ex = cx, ey = cy + 1;
        _cursorOverride = (ex, ey);
        _runX = cx - 5; _runY = cy + 3;
        _ghost.Set(_runX, _runY, ex, ey, _toolKind);
        _ghostView.Show(_ghost, _park);
        GD.Print($"[ghost] run ({_runX},{_runY}) -> ({ex},{ey}), layable {_ghost.Layable}: "
               + string.Join(" ", _ghost.Tiles.Select(t => $"({t.X},{t.Y}){t.Verdict}")));
    }

    /// <summary>The plot cell under the game camera's cursor. ⚠ Found by nearest centre rather
    /// than by inverting the plot's transform: the plot may sit under an authored node transform,
    /// and a wrong inverse would be a silent one-cell-off rather than a miss.</summary>
    bool CursorCell(out int bx, out int by)
    {
        bx = by = -1;
        var f = _park?.Field;
        if (f == null) return false;
        // ⭐ A capture has no mouse. The control run puts the cell it wants here so a headless
        // render exercises the same path as a press does, rather than a second code route that
        // could be right while the live one is wrong.
        if (_cursorOverride is { } fixedCell) { bx = fixedCell.X; by = fixedCell.Y; return true; }

        // ⭐⭐ THE BUILD CURSOR IS THE MOUSE, not the camera's pan cursor. The mouse ray is cast
        // at the plot's own floor height and the cell is the one nearest where it lands.
        // ⚠ Cast at the floor, NOT at y=0: the plot sits on the terrain, so a ray aimed at the
        // world plane would land a cell or two off wherever the park is not at zero.
        var mouse = GetViewport().GetMousePosition();
        if (_panel != null && _panel.Visible && mouse.X < PanelW) return false;
        return CellAtScreen(mouse, out bx, out by);
    }

    /// <summary>The plot cell a point on the screen picks out: cast the camera's ray at the plot's
    /// floor height and take the cell nearest where it lands.
    ///
    /// ⚠ Nearest CENTRE rather than inverting the plot's transform -- the plot can sit under an
    /// authored node transform, and a wrong inverse is a silent one-cell-off, not a miss.</summary>
    bool CellAtScreen(Vector2 screen, out int bx, out int by)
    {
        bx = by = -1;
        var f = _park?.Field;
        if (f == null || _cam == null) return false;
        var from = _cam.ProjectRayOrigin(screen);
        var dir = _cam.ProjectRayNormal(screen);
        if (Mathf.Abs(dir.Y) < 1e-5f) return false;
        // ⚠ The plot's OWN floor height, not y=0: the park sits on the terrain, so aiming at the
        // world plane lands a cell or two out wherever the floor is not at zero.
        float t = (_park.BaseY - from.Y) / dir.Y;
        if (t <= 0f) return false;                       // the floor is behind the camera
        var hit = from + dir * t;
        var want = new Vector2(hit.X, hit.Z);
        float best = float.MaxValue;
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                var c = _park.CellCentre(x, y);
                float d = want.DistanceSquaredTo(new Vector2(c.X, c.Z));
                if (d < best) { best = d; bx = x; by = y; }
            }
        return best <= Park.CellSize * Park.CellSize;
    }

    /// <summary>⭐ A CONTROL FOR THE MOUSE PICKING that works without a mouse: put a known cell's
    /// centre on the screen with the camera's own projection, then send that screen point back
    /// through the picking. It must come back as the cell it started from. A capture cannot move
    /// a pointer, and a control that skipped the ray would be testing the override instead.</summary>
    void CheckMousePicking()
    {
        var f = _park?.Field;
        if (f == null || _cursorOverride is not { } want) return;
        _pickChecked = true;
        var screen = _cam.UnprojectPosition(_park.CellCentre(want.X, want.Y));
        bool got = CellAtScreen(screen, out int gx, out int gy);
        GD.Print($"[pick] cell ({want.X},{want.Y}) projects to screen {screen.X:F0},{screen.Y:F0} "
               + $"and picks back as ({gx},{gy}) -- {(got && gx == want.X && gy == want.Y ? "same" : "DIFFERENT")}");
        // ⚠ AND A CONTROL THAT MUST DISAGREE. A round trip that only ever returns the cell it was
        // given is also what a picking that ignores the screen entirely would print, so a second
        // point well away from the first has to come back as a different cell.
        bool off = CellAtScreen(screen + new Vector2(0f, 60f), out int ox, out int oy);
        GD.Print($"[pick] 60px lower picks ({ox},{oy}) -- "
               + $"{(off && (ox != want.X || oy != want.Y) ? "different, as it must be" : "SAME, so the pick ignores the screen")}");
    }

    /// <summary>Open or close the path tool. ⭐ Closing ends the run and takes the ghost off the
    /// ground: a ghost left behind reads as laid path.</summary>
    void OpenTool(PathTool.Kind kind)
    {
        if (_paths == null) { GD.Print("[path] the tool is off for this park"); return; }
        _toolOpen = true;
        _toolKind = kind;
        _runX = _runY = -1;
        GD.Print($"[tool] {kind} open -- press to start a run, press again to lay it");
    }

    void CloseTool()
    {
        _toolOpen = false;
        _runX = _runY = -1;
        _ghostAt = (-1, -1, -1, -1);
        _ghostView?.Clear();
    }

    /// <summary>The ghost, every frame the tool is open: from the run's start to the cursor, or
    /// just the cursor tile when no run has been started.</summary>
    void UpdateGhost()
    {
        if (_ghost == null || _paths == null) return;
        if (!CursorCell(out int x, out int y)) { _ghostView.Clear(); _ghostAt = (-1, -1, -1, -1); return; }
        int sx = _runX < 0 ? x : _runX, sy = _runY < 0 ? y : _runY;
        // ⚠ Only when it MOVED. The ghost is rebuilt geometry, and rebuilding the same run every
        // frame is a mesh churn that buys nothing -- the run only changes when a cell boundary is
        // crossed or a run is started.
        if (_ghostAt == (sx, sy, x, y)) return;
        _ghostAt = (sx, sy, x, y);
        _ghost.Set(sx, sy, x, y, _toolKind);
        _ghostView.Show(_ghost, _park);
    }

    /// <summary>A press of the open tool. The first starts a run, the second lays it -- and ⭐ the
    /// run CARRIES ON from where it ended, which is what makes a path drawn in legs rather than
    /// one click per tile.</summary>
    void PressTool()
    {
        if (!_toolOpen || _paths == null || _ghost == null) return;
        if (!CursorCell(out int x, out int y)) { GD.Print("[path] the cursor is off the plot"); return; }
        if (_runX < 0) { _runX = x; _runY = y; GD.Print($"[path] run starts at ({x},{y})"); return; }
        _ghost.Set(_runX, _runY, x, y, _toolKind);
        if (!_ghost.Layable)
        {
            // ⚠ Refused is not an error to swallow: say WHICH tile stopped it, because the whole
            // run goes red after the first one and the picture alone cannot tell you which.
            var bad = _ghost.Tiles.FirstOrDefault(t => t.Verdict == PathGhost.Verdict.Refused);
            GD.Print($"[path] refused at ({bad.X},{bad.Y}): {_paths.Describe(bad.X, bad.Y)}");
            return;
        }
        int laid = _ghost.Lay(_toolKind);
        RebuildFloor();
        // ⚠ From the SEGMENT'S END, not from the cursor. The cursor is snapped to one axis, so
        // carrying on from where the mouse was would put the next segment's start off the end of
        // the one just laid -- by however far the cursor had drifted off the line.
        (_runX, _runY) = _ghost.End;
        _ghostAt = (-1, -1, -1, -1);
        GD.Print($"[path] laid {laid} of {_ghost.Tiles.Count}; the run goes on from ({_runX},{_runY}); {_paths.Laid} total");
    }

    void BuildBuildableOverlay()
    {
        _buildable?.QueueFree();
        _buildable = null;
        var f = _park?.Field;
        if (f == null || _holeSize.X <= 1f) return;

        // ⭐ The no-build cells are exactly the cells the terrain draws no ground on: the loader
        // at 0x14E700 writes flags 0x23 (NoGround | Unbuildable | 0x20) for a cell whose authored
        // byte0 bit 0 is set, and 0 for every other. See Model.HeightField.Buildable.
        Func<int, int, bool> unbuildable = (x, y) => !f.Buildable(x, y);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int no = 0, yes = 0;
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                if (!unbuildable(x, y)) { yes++; continue; }
                no++;
                var c = _park.CellCentre(x, y) + new Vector3(0f, Park.CellSize * 0.04f, 0f);
                float h = Park.CellSize * 0.5f;
                var a = c + new Vector3(-h, 0, -h); var b = c + new Vector3(h, 0, -h);
                var d = c + new Vector3(h, 0, h);   var e = c + new Vector3(-h, 0, h);
                foreach (var v in new[] { a, b, d, a, d, e }) st.AddVertex(v);
            }
        if (no == 0) { GD.Print($"[build] nothing marked of {yes} drawn cells"); return; }
        _buildable = new MeshInstance3D
        {
            Mesh = st.Commit(),
            Visible = System.Environment.GetEnvironmentVariable("TPW_PARK_BUILD") == "1",
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.15f, 0.15f, 0.42f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        AddChild(_buildable);
        GD.Print($"[build] {no} of {no + yes} drawn cells marked ({100.0 * no / (no + yes):F0}%)");
    }

    /// <summary>The bounds of the terrain surfaces whose mesh name matches, in the terrain's
    /// parent space. Several surfaces share one mesh name, so they are merged.</summary>
    bool TerrainBounds(string name, out Aabb box)
    {
        box = default;
        bool any = false;
        if (_terrain == null) return false;
        foreach (var child in _terrain.Root.GetChildren())
        {
            if (child is not MeshInstance3D mi || mi.Mesh == null) continue;
            if (!mi.Name.ToString().Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
            var b = _terrain.Root.Transform * mi.Transform * mi.Mesh.GetAabb();
            box = any ? box.Merge(b) : b;
            any = true;
        }
        return any;
    }

    /// <summary>Stand the park's entrance arch on its pad.
    ///
    /// ⭐ WHERE IT GOES COMES FROM THE TERRAIN, NOT FROM A CONSTANT. Fantasy ships a flat 6x4
    /// pad mesh called `gatebase01` at exactly the gate's place; the other three worlds do not,
    /// but all four put `ticket_booths` on the entrance axis at the same depth, and Fantasy's pad
    /// sits 5.5 units past its booths. So the pad is used where it exists and that offset
    /// reproduces it where it does not -- one rule, checkable against the world that states the
    /// answer.</summary>
    void LoadGate()
    {
        _gate?.Root.QueueFree();
        _gate = null;
        var ride = _lib.Rides.FirstOrDefault(
            r => r.Name.Contains("gates", StringComparison.OrdinalIgnoreCase) && r.Model != null);
        if (ride == null) { GD.PrintErr("[gate] no Gates model in this archive"); return; }

        // ⭐⭐ THE GATE MODEL CARRIES ITS OWN POSITION. Measured: all four are authored standing
        // on the ground (y starts at 0) across x 45..51 -- centred on 48, which is the entrance X
        // of the two parks that use 48. Their Z ranges DIFFER from each other, so they are NOT
        // interchangeable and centring them on one point is wrong. Master asked "are you sure?"
        // about exactly that, and was right.
        //
        // So: keep the authored transform and translate only by this park's entrance offset from
        // the 48 the models are drawn at. ⚠ For HALLOW and SPACE that offset is ZERO, which is the
        // control -- if the authored position is the real one, those two must land correctly with
        // nothing moved at all.
        const float AuthoredX = 48f;
        // ⚠ AND THE MODELS ARE AUTHORED FORWARD OF WHERE THEY STAND. Master, having walked up to
        // them: "all gates are too far forward (towards the road)". Fantasy's gatebase01 pad says
        // by how much -- the pad is centred at z -21.00 and Fantasy's gate is authored centred at
        // z -18.71, so the authoring sits 2.29 toward the road of the pad it belongs on. Its front
        // edge agrees to within a hundredth (-19.00 against -16.70 + 2.30), because that gate is
        // exactly as deep as the pad.
        //
        // ⚠ IT IS STILL ONE PARK'S WORD. Fantasy is the only world that ships a pad, so where a
        // pad exists its own z is used outright and elsewhere this offset stands in for it.
        const float AuthoredZBias = -2.29f;
        // ⭐ PER-PARK, because the gates are not interchangeable and master calibrates them one
        // at a time by eye. Each entry is one press of `]` that they asked for in that park and
        // that park only; anything not listed rides the shared bias above.
        //   JUNGLE ("LOST KINGDOM"): +0.25, 2026-09-22.
        float perPark = (_lib.WadName ?? "").Contains("JUNGLE", StringComparison.OrdinalIgnoreCase)
            ? 0.25f : 0f;
        if (!TerrainBounds("ticket_booths", out var booths))
        { GD.PrintErr("[gate] no ticket_booths -- cannot find this park's entrance axis"); return; }
        float shift = booths.Position.X + booths.Size.X * 0.5f - AuthoredX;
        bool hasPad = TerrainBounds("gatebase01", out var pad);

        // ⭐ "whys there no coord to read?" -- master, and a fair question. A feature is placed by
        // the game, so a position ought to be DATA somewhere. Each WAD carries its own Gates.sam,
        // so a per-park coordinate could be sitting in it. Print every key rather than guess which.
        var def = DefinitionFor(ride.Model);
        if (def != null)
        {
            GD.Print($"[gate.sam] {def.Source}  {def.Fields.Count} keys, {def.Blocks.Count} blocks");
            foreach (var kv in def.Fields.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                GD.Print($"[gate.sam]   {kv.Key} = {kv.Value}");
            foreach (var b in def.Blocks)
                GD.Print($"[gate.sam]   [{b.Key}] {string.Join(" / ", b.Value)}");
        }
        else GD.PrintErr("[gate.sam] no .sam beside the gate model");
        try
        {
            var gm = new Model(_lib.Read(ride.Model));
            Aps anim = null; Aps.Record rec = null;
            if (ride.Animation != null)
                try { anim = new Aps(_lib.Read(ride.Animation)); rec = anim.Records().FirstOrDefault(); }
                catch (Exception ex) { GD.PrintErr($"[gate] animation: {ex.Message}"); }
            _gate = new AnimatedModel(gm, anim, rec, m => TextureNear(ride.Model.Path, m));
            _gate.SetFrame(0);
            AddChild(_gate.Root);
            // ⚠ Seat it on the pad by its OWN base, not by its centre: the arch is tall and
            // centring it buries half of it.
            var (lo, hi) = Park.DrawnBounds(_gate.Root, inParent: true);
            float dz = (hasPad
                ? pad.Position.Z + pad.Size.Z * 0.5f - (lo.Z + hi.Z) * 0.5f
                : AuthoredZBias) + _gateNudge + perPark;
            _gate.Root.Position += new Vector3(shift, 0f, dz);
            _gate.Root.Visible = _mode == Mode.Park;
            GD.Print($"[gate] {ride.Name}: authored x {lo.X:F2}..{hi.X:F2}  y {lo.Y:F2}..{hi.Y:F2}  "
                   + $"z {lo.Z:F2}..{hi.Z:F2}; shifted {shift:+0.0;-0.0;0} x, {dz:+0.00;-0.00;0} z "
                   + (hasPad ? "onto its own gatebase01 pad" : "by the offset Fantasy's pad states")
                   + (_gateNudge != 0f ? $"  [nudged {_gateNudge:+0.00;-0.00}]" : "")
                   + (perPark != 0f ? $"  [this park {perPark:+0.00;-0.00}]" : "")
                   + $"\n[gate] front edge now z={hi.Z + dz:F2} -- the road ends at -18.90 and the "
                   + $"booths' back is -16.12, in every park");
        }
        catch (Exception ex) { GD.PrintErr($"[gate] {ride.Model.Path}: {ex.Message}"); }
    }

    /// <summary>Print the authored footprint around the park entrance as a map.
    ///
    /// ⭐ `byte0` bit 0 is the engine's own SKIP flag, so a block of skipped cells is a thing the
    /// designers reserved -- tinyclaw measured the bus stop's 36 cells as 100% skipped. That makes
    /// the skip map a PER-PARK measurement of where the gate stands, instead of one park's pad
    /// offset applied to four.</summary>
    void DumpEntranceSkip()
    {
        if (_park?.Field == null || _holeSize.X <= 1f) { GD.PrintErr("[skip] no authored field"); return; }
        if (!TerrainBounds("ticket_booths", out var booths)) { GD.PrintErr("[skip] no booths"); return; }
        var f = _park.Field;
        int bx = Mathf.RoundToInt(booths.Position.X + booths.Size.X * 0.5f - _holeOrigin.X);
        int bz = Mathf.RoundToInt(booths.Position.Z + booths.Size.Z * 0.5f - _holeOrigin.Y);
        GD.Print($"[skip] booths at cell ({bx},{bz}) of {f.Width}x{f.Height}; "
               + "'.' drawn, '#' skipped, rows are z increasing (into the park)");
        for (int z = bz - 4; z <= bz + 12; z++)
        {
            if (z < 0 || z >= f.Height) continue;
            var row = new System.Text.StringBuilder();
            for (int x = bx - 8; x <= bx + 8; x++)
                row.Append(x < 0 || x >= f.Width ? ' ' : f.Drawn(x, z) ? '.' : '#');
            GD.Print($"[skip] z={z,3} (world {_holeOrigin.Y + z,7:F1})  {row}");
        }
    }

    /// <summary>Sample the terrain's surface once per tile, because that is the shape of the
    /// question the game asks: `0x14F820` looks the ground up by TILE INDEX
    /// (`eyeX * 0x10000 >> 0x18`), never by a ray. One Godot unit is one tile here.</summary>
    void BakeGround()
    {
        if (_terrain == null) { _ground = null; return; }
        var (lo, hi) = Park.DrawnBounds(_terrain.Root, inParent: true);
        _groundOrigin = new Vector2I(Mathf.FloorToInt(lo.X), Mathf.FloorToInt(lo.Z));
        int w = Mathf.CeilToInt(hi.X) - _groundOrigin.X + 1;
        int h = Mathf.CeilToInt(hi.Z) - _groundOrigin.Y + 1;
        if (w <= 0 || h <= 0 || w > 4096 || h > 4096) { _ground = null; return; }
        var t0 = Time.GetTicksMsec();
        var top = Park.SurfaceHeights(_terrain.Root, _groundOrigin, w, h, 1f);
        _ground = new int[w, h];
        int got = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (top[x, y].HasValue)
                { _ground[x, y] = (int)(top[x, y].Value * GameCamera.TileUnits); got++; }
        GD.Print($"[ground] {w} x {h} tiles, {got} with terrain under them, "
               + $"in {Time.GetTicksMsec() - t0} ms");
    }

    /// <summary>The ground under a tile in world units. ⚠ Off the baked grid returns the last
    /// known floor rather than zero: zero is the sea bed here, and dropping the camera to it on
    /// the first tile past the edge reads as the camera falling through the world.</summary>
    int GroundAt(int tx, int tz)
    {
        if (_ground == null) return 0;
        int x = Mathf.Clamp(tx - _groundOrigin.X, 0, _ground.GetLength(0) - 1);
        int y = Mathf.Clamp(tz - _groundOrigin.Y, 0, _ground.GetLength(1) - 1);
        return _ground[x, y];
    }

    /// <summary>Is the game's own camera driving? Only in park mode, and only until G.</summary>
    bool GameCamActive => _mode == Mode.Park && !_freeCam && _ground != null;

    /// <summary>Drop the game camera onto the middle of the plot.</summary>
    void StartGameCam()
    {
        // ⚠ Reset FIRST: it clears the started flag, so placing after it is what stops the
        // height easing in from wherever the last park left the camera.
        _game.Reset();
        if (_holeSize.X > 1f)
            _game.PlaceAt(_holeOrigin.X + _holeSize.X * 0.5f, _holeOrigin.Y + _holeSize.Y * 0.5f);
        else _game.PlaceAt(_focus.X, _focus.Z);
        // ⭐ A shot run cannot hold a key, so the three axes are settable for renders. This is
        // what makes "the zoom is the pitch" checkable in a picture instead of in a paragraph.
        var set = _wantCam ?? System.Environment.GetEnvironmentVariable("TPW_CAM");
        if (!string.IsNullOrWhiteSpace(set))
        {
            var f = set.Split(',');
            if (f.Length > 0 && int.TryParse(f[0], out var b) && b > 0)
                _game.Behind = Mathf.Clamp(b, GameCamera.MinBehind, GameCamera.MaxBehind);
            if (f.Length > 1 && int.TryParse(f[1], out var q)) { _game.Turn(q); _game.Yaw = _game.TargetYaw; }
            if (f.Length > 2 && int.TryParse(f[2], out var d))
                _game.Dolly = Mathf.Clamp(d, GameCamera.MinDolly, GameCamera.MaxDolly);
        }
        GD.Print($"[cam] game camera at the plot centre, {_game.Behind} behind and "
               + $"{_game.Above} up -- {_game.PitchDegrees:F1} degrees down");
    }

    /// <summary>One frame of the game's camera, and the keys that drive it. ⚠ Held keys, not
    /// events: the zoom and the pan are per-frame accumulations on the console too.</summary>
    void StepGameCam(double delta)
    {
        int pan = (int)(6 * GameCamera.TileUnits * delta);
        int a = _game.Yaw & 0xFFF;
        // Pan along the way the camera faces, which is what the cursor does on the console.
        float s = Mathf.Sin(a * Mathf.Tau / GameCamera.TurnUnits);
        float c = Mathf.Cos(a * Mathf.Tau / GameCamera.TurnUnits);
        int fwd = (Input.IsKeyPressed(Key.W) ? 1 : 0) - (Input.IsKeyPressed(Key.S) ? 1 : 0);
        int side = (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
        // ⚠ The side term is NEGATED against the forward one. Taking right as (cos, -sin) of the
        // same angle reads correct and drives A and D the wrong way round -- master hit it in the
        // first minute. The camera looks along +(sin, cos), so its right is -(cos, -sin).
        _game.CursorX += (int)((fwd * s - side * c) * pan);
        _game.CursorZ += (int)((fwd * c + side * s) * pan);
        if (Input.IsKeyPressed(Key.R)) _game.Zoom(-1);
        if (Input.IsKeyPressed(Key.F)) _game.Zoom(1);
        if (Input.IsKeyPressed(Key.Z)) _game.Push(-1);
        if (Input.IsKeyPressed(Key.X)) _game.Push(1);
        _game.Step(GameCamera.FrameTime60, GroundAt);
        _cam.Transform = new Transform3D(
            Basis.LookingAt(_game.Look - _game.Eye, _game.Up), _game.Eye);
    }

    /// <summary>Point the camera at the surfaces whose mesh name contains TPW_PARK_MESH.
    /// ⭐ The terrain is ONE model holding dozens of meshes -- the bus stop, the gate, the
    /// embankment -- so "look at the bus stop" is otherwise a coordinate guess.</summary>
    void AimAtMesh()
    {
        var want = System.Environment.GetEnvironmentVariable("TPW_PARK_MESH");
        if (string.IsNullOrWhiteSpace(want) || _terrain == null) return;
        Aabb? box = null;
        int hits = 0;
        foreach (var child in _terrain.Root.GetChildren())
        {
            if (child is not MeshInstance3D mi || mi.Mesh == null) continue;
            if (!mi.Name.ToString().Contains(want, StringComparison.OrdinalIgnoreCase)) continue;
            hits++;
            // In the terrain root's parent space, which is where the camera lives.
            var b = _terrain.Root.Transform * mi.Transform * mi.Mesh.GetAabb();
            box = box.HasValue ? box.Value.Merge(b) : b;
        }
        if (!box.HasValue)
        {
            // ⭐ Say what IS there. A miss with no list is a dead end; a miss with the names is
            // a survey of what the terrain model actually holds.
            GD.PrintErr($"[aim] no mesh matches '{want}'. The terrain has:");
            foreach (var c in _terrain.Root.GetChildren())
                if (c is MeshInstance3D m2 && m2.Mesh != null)
                {
                    var bb = _terrain.Root.Transform * m2.Transform * m2.Mesh.GetAabb();
                    GD.PrintErr($"    {m2.Name,-28} at {bb.Position + bb.Size * 0.5f} size {bb.Size}");
                }
            return;
        }
        var a = box.Value;
        _focus = a.Position + a.Size * 0.5f;
        _dist = Mathf.Max(a.Size.Length() * 0.9f, 0.5f);
        _pitch = -0.25f;
        // ⚠ The aim is an orbit-camera viewpoint, and StartGameCam runs right after this and puts
        // the game's camera at the plot centre whenever the ground bake succeeds -- which silently
        // replaced the aimed shot with the default park view. Aiming means the free camera.
        _freeCam = true;
        GD.Print($"[aim] '{want}': {hits} surfaces, centre {_focus}, size {a.Size}");
    }

    /// <summary>The park debug viewpoints, shared by every path that frames the park. ⚠ They used
    /// to live only in FrameParkCamera, so opening a map without a ride silently ignored them.</summary>
    void ParkCameraOverrides()
    {
        // ⚠ A DEBUG VIEWPOINT MUST TURN THE GAME CAMERA OFF. StartGameCam runs after this and
        // puts the camera back at the plot centre, so asking for the top-down or the close-up and
        // getting the ordinary game view is not the switch failing -- it is being overruled one
        // line later. Same fault fable hit with the mesh aim.
        if (System.Environment.GetEnvironmentVariable("TPW_HOLE_DEBUG") == "1"
            || System.Environment.GetEnvironmentVariable("TPW_PARK_CLOSEUP") == "1"
            || System.Environment.GetEnvironmentVariable("TPW_PARK_UNDER") == "1")
            _freeCam = true;
        // ⚠ Straight down on demand. An orbited view cannot be read for placement -- which way +X
        // and +Z run on screen is unknown, and I misjudged the same picture three times arguing
        // the floor was off the island when its coordinates said otherwise. Top-down makes screen
        // axes world axes, so the floor's position against the hole is a thing you can see.
        if (System.Environment.GetEnvironmentVariable("TPW_HOLE_DEBUG") == "1") _pitch = -1.5533f;

        // ⭐ An INDEPENDENT check on triangle winding. Looking up from underneath asks a different
        // question than "what fraction flipped": if the ground is wound correctly it is
        // back-facing from below and should be CULLED, so the terrain largely disappears. If the
        // sign is inverted the ground is solid from below and missing from above. The flip-count
        // agreement cannot see this, because it and the census come from the same model data.
        if (System.Environment.GetEnvironmentVariable("TPW_PARK_UNDER") == "1")
        {
            _pitch = 1.30f;
            _dist = Math.Max(_terrainSize.X, _terrainSize.Y) * 0.55f;
        }

        // A ground-level look along the plot, for comparing against a screenshot of the real game.
        // The island-wide shot cannot show a one-unit step: it is about 1% of the frame.
        if (System.Environment.GetEnvironmentVariable("TPW_PARK_CLOSEUP") == "1" && _holeSize.X > 1f)
        {
            _focus = new Vector3(_holeOrigin.X + _holeSize.X * 0.45f, _holeY + 2f,
                                 _holeOrigin.Y + _holeSize.Y * 0.45f);
            _dist = Math.Max(_holeSize.X, _holeSize.Y) * 0.42f;
            _pitch = -0.30f;
        }
    }

    /// <summary>Lay the playable plot on the loaded terrain. Shared by the park tab, which shows
    /// the park alone, and by BuildPark, which then stands a ride in it.</summary>
    void BuildPlot()
    {
        // ⚠ The terrain is loaded FIRST: the playable grid's position and size come off its own
        // geometry, so building the park before it would place the grid at the origin and leave it
        // sitting outside the island.
        LoadTerrain();
        if (_holeSize.X <= 1f) { _park.Build(ParkCells, ParkCells); return; }
        _park.Origin = _holeOrigin;
        _park.BaseY = _holeY;
        // ⚠ The mesh-coverage mask is BACK, at master's call. I dropped it because it was
        // deleting the cells I was extruding into blocks -- but those blocks were fabricated
        // (a cell is three tile indices, not a height), so there is nothing left for the mask
        // to destroy. What it does do is keep the park floor from being laid straight over
        // terrain the mesh already draws: the embankment, the roads, the banks. Without it the
        // plot is a slab covering real geometry, which is extra terrain we invented.
        //
        // ⚠ The grid still comes from the authored field; only which cells get a floor is
        // masked. When the corner tables are decoded this stops being a mask and becomes the
        // tile shapes.
        // Sample the model's own surface height per cell so the floor's edge can close
        // against it instead of leaving an open seam.
        if (_terrain != null && _park.Field != null)
            _park.TerrainTop = Park.SurfaceHeights(_terrain.Root, _holeOrigin,
                _park.Field.Width, _park.Field.Height, Park.CellSize);
        RebuildFloor();
    }

    /// <summary>Lay the plot's floor again from the grid as it stands. ⭐ Cheap and complete: a
    /// path tile changes the cell's ground byte and its neighbours', and the floor is built FROM
    /// those bytes, so re-running it is how a laid tile appears -- no separate path geometry.</summary>
    void RebuildFloor()
    {
        if (_holeSize.X <= 1f) { _park.Build(ParkCells, ParkCells); return; }
        _park.Build(Mathf.RoundToInt(_holeSize.X), Mathf.RoundToInt(_holeSize.Y), _holeCells);
    }

    /// <summary>The park by itself. ⚠ NO RIDE: opening a map used to stand the archive's first
    /// model in the dead centre of the plot, which reads as content rather than as the debug
    /// default it was. A ride appears when one is asked for.</summary>
    void ShowParkOnly()
    {
        _parkRide = false;
        BuildPlot();
        if (_current != null) _current.Root.Visible = false;
        // ⭐ The control for "is the terrain already drawing ground here?". With the plot hidden,
        // whatever fills the plot's footprint is the terrain mesh's own ground -- which is the
        // question behind every overlap complaint about this pair of surfaces.
        if (System.Environment.GetEnvironmentVariable("TPW_PARK_HIDE") == "1") _park.Floor.Visible = false;
        // ⭐ And the other half of the same question: the plot floor with nothing under it, which
        // is how you tell an overlap between the two surfaces from one INSIDE the floor itself.
        if (System.Environment.GetEnvironmentVariable("TPW_TERRAIN_HIDE") == "1" && _terrain != null)
            _terrain.Root.Visible = false;
        var (lo, hi) = Park.DrawnBounds(_terrain != null ? _terrain.Root : _park.Root,
                                        inParent: _terrain != null);
        _focus = new Vector3((lo.X + hi.X) * 0.5f, lo.Y + (hi.Y - lo.Y) * 0.3f, (lo.Z + hi.Z) * 0.5f);
        _dist = Mathf.Max(Mathf.Max(hi.X - lo.X, hi.Z - lo.Z) * 0.99f, 1e-3f);
        _pitch = -0.55f;
        ParkCameraOverrides();
        // ⚠ LAST. Everything above sets the camera, so aiming before them aims at nothing.
        LoadGate();
        LoadSky();
        MakePathTool();
        BuildBuildableOverlay();
        var want = (System.Environment.GetEnvironmentVariable("TPW_PS2_WEATHER") ?? "").ToLowerInvariant();
        var kind = want.StartsWith("rain") ? Weather.Kind.Rain
                 : want.StartsWith("snow") ? Weather.Kind.Snow : Weather.Kind.None;
        if (kind != Weather.Kind.None || _weather.Current != Weather.Kind.None) _weatherWanted = kind;
        if (System.Environment.GetEnvironmentVariable("TPW_PARK_SKIP") == "1") DumpEntranceSkip();
        AimAtMesh();
        StartGameCam();
    }

    void BuildPark(Model mesh)
    {
        var def = DefinitionFor(_ride.Model);
        if (def == null)
        {
            // Still lay the park. Empty grass says "this model has no ride definition"; no park
            // at all reads as the park mode being broken.
            LoadTerrain();
            _park.Build(ParkCells, ParkCells);
            _info.Text = $"{_ride.Name}\nno .sam beside this model -- nothing to place it by";
            return;
        }
        var fp = def.Shape != null ? Park.Footprint.From(def.Shape)
                                   : new Park.Footprint(1, 1, new bool[1, 1], -1, -1);

        string display = null;
        if (_text != null)
        {
            var parts = def.Source.Split('/', StringSplitOptions.RemoveEmptyEntries);
            int wi = Array.FindIndex(parts, x => x.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase));
            if (wi >= 0)
            {
                int row = _text.IndexOf(TextDatabase.GraphicsKey(
                    parts[wi][..^4], string.Join('/', parts.Skip(wi + 1))));
                if (row >= 0) display = _text.Text("eng", row);
            }
        }

        // ⚠ A fixed park, not ground cut to whichever ride is selected. The ride is placed INTO
        // it at a position, which is what makes the next class of bug -- overlap, edges, footprints
        // that do not fit -- possible to have at all.
        // ⚠ The terrain is loaded FIRST: the playable grid's position and size come off its own
        // geometry, so building the park before it would place the grid at the origin and leave it
        // sitting outside the island.
        BuildPlot();
        // ⚠ Aim at the PARK's middle, not at ParkCells/2 -- that constant is the fallback size and
        // has nothing to do with the plot once the plot comes off the terrain.
        _parkRide = true;
        _current.Root.Visible = true;
        bool placed = _park.TryPlaceNear(_current.Root, fp, def.Id ?? 1, display ?? def.Name ?? "?");
        int px = _park.LastX, py = _park.LastY;
        // ⚠ AFTER the placement, never before. Rebuild frames the camera on the model in its own
        // space and Place then MOVES it onto the footprint, so framing first aims the shot at where
        // the ride used to be -- which photographs empty grass and looks like the ride failed to load.
        FrameParkCamera(fp, _current.Root, px, py);

        // ⚠ Report the model against its cells rather than assuming it fits. A ride overflowing
        // its footprint is a real thing here -- the cell size itself was measured, not given.
        // ⭐ The invariant: cells claimed must equal the footprints placed. If a placement
        // overlapped and was written anyway, these diverge and the park is quietly corrupt.
        int want = _park.Placed.Sum(r => r.Fp.Occupied);
        string inv = _park.OccupiedCells == want
            ? $"{_park.OccupiedCells} cells claimed, matches"
            : $"⚠ {_park.OccupiedCells} claimed vs {want} expected -- OVERLAP";

        // ⚠ Bisect reader against builder. Park.Bounds asks the READER (vertices through
        // WorldTransforms); Park.DrawnBounds measures the GEOMETRY THE BUILDER ACTUALLY MADE. If
        // those two disagree, the fault is in AnimatedModel, not in the transforms -- and if they
        // agree, the model really is that size and my expectation is what is wrong.
        var (rmin, rmax) = Park.Bounds(mesh);
        GD.Print($"[bisect] reader {rmax.X - rmin.X:F3} x {rmax.Z - rmin.Z:F3}   "
               + $"builder {Park.DrawnBounds(_current.Root).Max.X - Park.DrawnBounds(_current.Root).Min.X:F3} x "
               + $"{Park.DrawnBounds(_current.Root).Max.Z - Park.DrawnBounds(_current.Root).Min.Z:F3}   "
               + $"plot {fp.Width}x{fp.Height}");

        // ⭐ Self-triggering: only speaks when reader and builder actually disagree, so it needs no
        // switch to arm -- the env var version never fired because the variable did not arrive.
        if (rmax.X - rmin.X > 0 && (Park.DrawnBounds(_current.Root).Max.X - Park.DrawnBounds(_current.Root).Min.X)
            / (rmax.X - rmin.X) > 2.0f && _current.LastWorld != null)
        {
            var bindWorld = mesh.WorldTransforms();
            foreach (var m in mesh.Meshes)
            {
                if (!_current.LastWorld.TryGetValue(m.Offset, out var w)) continue;
                if (!bindWorld.TryGetValue(m.Offset, out var bw)) continue;
                float bl = new Vector3(bw.M11, bw.M12, bw.M13).Length();
                float al = new Vector3(w.M11, w.M12, w.M13).Length();
                GD.Print($"[scale] {m.Name,-10} bind {bl:F4}  anim {al:F4}  ratio {(bl > 0 ? al / bl : 0),7:F3}"
                       + $"  overridden={_current.OverriddenNodes.Contains(mesh.NodeIndex(m.Offset))}");
            }
        }

        var (min, max) = Park.DrawnBounds(_current.Root);
        // ⚠ `Visible` is a node's OWN flag. A hidden ancestor leaves it true and draws nothing,
        // so the flag that matters is IsVisibleInTree.
        int meshes = 0;
        void Count(Node n) { if (n is MeshInstance3D) meshes++; foreach (var c in n.GetChildren()) Count(c); }
        Count(_current.Root);
        GD.Print($"[park] drawn {min}..{max} placed {_current.Root.Position} plot "
                 + $"{fp.Width * Park.CellSize}x{fp.Height * Park.CellSize}");
        // ⚠ Markers on the COMPUTED corners. I read this render wrong three times running -- a
        // blob is not labelled, and "that pale quad is my floor" was a guess each time. Posts at
        // the coordinates the code actually chose make the picture answer the question itself.
        if (System.Environment.GetEnvironmentVariable("TPW_HOLE_DEBUG") == "1" && _holeSize.X > 1f)
        {
            var post = new BoxMesh { Size = new Vector3(2.5f, 40f, 2.5f) };
            // ⚠ Posts at KNOWN, ASYMMETRIC world points, one colour each. Corner posts on the
            // computed hole cannot say which screen axis is +X and which is +Z, so every reading
            // of the picture needed a guess about the camera -- and the guesses disagreed. Three
            // labelled points fix the projection outright.
            void Post(float wx, float wz, Color c) =>
                _park.Root.AddChild(new MeshInstance3D
                {
                    Mesh = post,
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                    Position = new Vector3(wx, _holeY + 20f, wz),
                });
            Post(0f, 0f, new Color(1f, 0f, 0f));        // RED   = world origin
            Post(50f, 0f, new Color(0f, 1f, 0f));       // GREEN = +50 along X
            Post(0f, 50f, new Color(0f, 0.4f, 1f));     // BLUE  = +50 along Z
            foreach (var (cx, cz) in new[] { (0f, 0f), (_holeSize.X, 0f), (0f, _holeSize.Y), (_holeSize.X, _holeSize.Y) })
                Post(_holeOrigin.X + cx, _holeOrigin.Y + cz, new Color(1f, 1f, 1f));   // WHITE = computed hole
        }

        // Terrain alone, markers kept: the only way to see whether the marked region is the gap.
        if (System.Environment.GetEnvironmentVariable("TPW_HIDE_GRID") == "1") _park.ShowGrass = false;

        var (gmin, gmax) = Park.DrawnBounds(_park.GroundRoot);
        GD.Print($"[floor] ground {gmin}..{gmax}  visible={_park.GroundRoot.IsVisibleInTree()} "
               + $"tiles={_park.GroundRoot.GetChildCount()}  terrain {Park.DrawnBounds(_terrain?.Root ?? _park.Root, inParent: true)}");
        GD.Print($"[park] meshes={meshes} modelInTree={_current.Root.IsVisibleInTree()} "
                 + $"rideInTree={_park.RideVisible} parkInTree={_park.Root.IsVisibleInTree()} "
                 + $"parkVisible={_park.Root.Visible} parkParent={_park.Root.GetParent()?.Name}");
        var over = (max.X - min.X) / Math.Max(fp.Width, 1) / Park.CellSize;
        var overZ = (max.Z - min.Z) / Math.Max(fp.Height, 1) / Park.CellSize;
        _info.Text = Park.Describe(def, display, fp)
                     + $"\n\npark {_park.Width}x{_park.Height} box, {_park.PlayableCells} ground cells"
                     + $", {_park.MaterialCount} ground materials"
                     + $"\n{(placed ? $"placed at {px},{py}" : "WOULD NOT FIT")}"
                     + $"\n{inv}"
                     + $"\nmodel fills {over:P0} x {overZ:P0} of its cells"
                     + $"\n{_current.Summary}"
                     + "\nleft-drag orbit | wheel zoom | R re-frame";
    }

    void Rebuild()
    {
        if (_current != null) { _current.Root.QueueFree(); _current = null; }
        if (_ride?.Model == null) return;
        try
        {
            GD.Print($"[v] building {_ride.Name}"); var model = _lib.LoadModel(_ride);
            var rec = _recordIndex < _records.Count ? _records[_recordIndex] : null;
            // ⚠ Report which materials got a texture and which did not. A material that silently
            // resolves to null renders flat-shaded and reads as missing geometry, not a missing file.
            int got = 0, missed = 0; var misses = new List<string>();
            foreach (var mat in model.Materials)
            {
                if (mat == null) continue;
                if (TextureFor(mat).Tex != null) got++;
                else { missed++; if (misses.Count < 8) misses.Add(mat); }
            }
            GD.Print($"[tex] {got} resolved, {missed} missing" +
                     (misses.Count > 0 ? ": " + string.Join(", ", misses) : ""));
            _current = new AnimatedModel(model, _anim, rec, TextureFor);
            _current.Root.Visible = _mode == Mode.Models || _mode == Mode.Park;
            AddChild(_current.Root); GD.Print($"[v] built: {_current.Summary}, "
                                                   + $"{_current.BlendSurfaces} blended surfaces");
            _time = 0;
            _current.SetFrame(0);
            FrameCamera(model);
            if (_mode == Mode.Park) BuildPark(model);
            else
                _info.Text = $"{_ride.Name}\n{_current.Summary}\n" +
                             $"{_records.Count} animations\n" +
                             "left-drag orbit  |  right-drag or shift+drag or WASD to pan  |  wheel zoom\n" +
                             "SPACE play/pause  |  arrows step a frame  |  R re-frame  |  F3 panel";
        }
        catch (Exception ex)
        {
            // ⚠ The STACK, not just the message. "Object reference not set" on its own
            // names no line and sent me guessing at three different nulls.
            GD.PrintErr($"[v] build failed: {ex}");
            _info.Text = $"{_ride?.Name}\nfailed: {ex.Message}";
        }
    }

    /// <summary>A material's texture, and whether it needs BLENDING rather than a cutout.
    ///
    /// ⭐ Soft means more than one per cent of its texels sit at an alpha that is neither clear nor
    /// solid. Measured across the disc that is 1,532 of the 1,710 32-bit TGAs, and the distribution
    /// is flat rather than clustered near zero -- these are real translucency (`Scifi_Glass` 63%,
    /// `Research_Hair` 39%), not resampling fuzz on a cutout edge.</summary>
    (ImageTexture Tex, bool Soft) TextureFor(string material)
    {
        // ⚠ The toggle must be checked HERE, not at build time, or turning textures off would
        // still hand the material a texture it had already cached.
        if (material == null || _texOn?.ButtonPressed == false) return (null, false);
        return TextureNear("/" + _ride.Name, material);
    }

    /// <summary>The same lookup, for a model that is not the selected ride.
    ///
    /// ⚠ The cache is keyed by OWNER AND MATERIAL. Keying on the material alone let the terrain and
    /// a ride that share a material name -- and they do, both draw from Sharetex -- hand each other
    /// the other's texture, whichever was built first.</summary>
    (ImageTexture Tex, bool Soft) TextureNear(string ownerPath, string material)
    {
        if (material == null || _texOn?.ButtonPressed == false) return (null, false);
        var key = ownerPath + "|" + material;
        if (_texCache.TryGetValue(key, out var t)) return t;
        (ImageTexture, bool) made = (null, false);
        try
        {
            var texture = _lib.TextureNear(ownerPath, material);
            if (texture != null)
            {
                var img = Image.CreateFromData(texture.Width, texture.Height, false, Image.Format.Rgba8, texture.Pixels);
                img.GenerateMipmaps();
                made = (ImageTexture.CreateFromImage(img), texture.Translucent);
                GD.Print($"[tex] {_lib.WadName}{ownerPath} '{material}' -> {texture.SourceWad}{texture.SourcePath} ({texture.Format})");
            }
            else GD.PrintErr($"[tex] UNRESOLVED {_lib.WadName}{ownerPath} '{material}'");
        }
        catch (Exception ex) { GD.PrintErr($"[tex] DECODE THREW: {ex.Message}"); }
        _texCache[key] = made;
        return made;
    }

    /// <summary>Frame the model on its ACTUAL geometry, not on `mesh+0x70/+0x80`.
    ///
    /// ⚠⚠ Those declared bounds cover the whole MORPH RANGE -- every position the vertices can
    /// reach across the entire animation -- so framing on them pulls the camera far enough back
    /// that the model occupies a couple of hundred pixels no matter the window size, and thin
    /// geometry (chains, the sign, banana shapes) falls below one pixel and simply vanishes. That
    /// is what made the viewer look like it was missing artwork the Python renderer had.</summary>
    /// <summary>Aim at the park rather than the ride: the footprint's centre, pulled back far
    /// enough to hold the laid ground as well as whatever is standing on it.</summary>
    void FrameParkCamera(Park.Footprint fp, Node3D drawn, int px, int py)
    {
        var (min, max) = Park.DrawnBounds(drawn);
        float w = Math.Max(fp.Width, 1) * Park.CellSize, h = Math.Max(fp.Height, 1) * Park.CellSize;
        // Aim at the ride where it now stands IN the park, not at a footprint sitting at the origin.
        _focus = new Vector3((px + fp.Width * 0.5f) * Park.CellSize,
                             (max.Y - min.Y) * 0.35f,
                             (py + fp.Height * 0.5f) * Park.CellSize);
        // The ground is laid with six cells of padding on every side; frame a little of it rather
        // than the ride alone, so the footprint reads against the grass around it.
        float span = Math.Max(w, h) + Park.CellSize * 8f;
        // ⚠ With real terrain loaded the subject is the PARK, not the ride standing in it. Framing
        // 4 cells inside a 210-cell world puts the camera underground and looks like the terrain
        // failed to load.
        if (_terrain != null && _terrainSize.X > 1f)
        {
            var (lo, hi) = Park.DrawnBounds(_terrain.Root, inParent: true);
            _focus = new Vector3((lo.X + hi.X) * 0.5f, lo.Y + (hi.Y - lo.Y) * 0.3f, (lo.Z + hi.Z) * 0.5f);
            span = Math.Max(_terrainSize.X, _terrainSize.Y) * 1.1f;
        }
        _dist = Math.Max(span * 0.9f, 1e-3f);
        _pitch = -0.55f;
        ParkCameraOverrides();
    }

    void FrameCamera(Model model)
    {
        var world = model.WorldTransforms();
        var pts = new List<Vector3>();
        foreach (var m in model.Meshes)
        {
            if (!world.TryGetValue(m.Offset, out var w)) continue;
            // Frame what is actually DRAWN. A mesh with no triangles is skipped by the builder, so
            // letting it into the bounds aims the camera at something invisible.
            if (model.Triangles(m).Count == 0) continue;
            var (pos, _, _) = model.Vertices(m);
            foreach (var p in pos)
            {
                var v = System.Numerics.Vector3.Transform(p, w);
                pts.Add(new Vector3(v.X, v.Y, v.Z));
            }
        }
        if (pts.Count == 0) return;
        var lo = pts.Aggregate((a, b) => new Vector3(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y), Mathf.Min(a.Z, b.Z)));
        var hi = pts.Aggregate((a, b) => new Vector3(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y), Mathf.Max(a.Z, b.Z)));
        _focus = (lo + hi) * 0.5f;
        // ⚠ THE FLOOR WAS 2.0 AND IT SWALLOWED THE CHARACTERS. A ride is 10-30 units across, so the
        // floor never bound; DATA.WAD's guests are UNDER ONE UNIT tall, so all of them clamped to
        // the same distance and rendered at their true relative size -- Girl1a, a child, came out
        // eight per cent of frame height. It is only there to survive a zero-size model.
        _dist = Mathf.Max((hi - lo).Length() * 0.85f, 1e-3f);
    }

    public override void _Process(double delta)
    {
        // ⚠ The camera is placed FIRST, before any early return. It used to sit below the capture
        // branch, so a --shot run photographed the origin and produced a perfectly black frame with
        // a perfectly correct UI beside it -- the geometry was fine the whole time.
        if (_current != null && _playing && _shotPath == null)
        {
            _time += (float)delta * Aps.Fps;
            if (_time >= _current.Frames) _time = _current.Frames > 0 ? _time % _current.Frames : 0;
            _current.SetFrame(_time);
            _scrub.SetValueNoSignal(_time / Mathf.Max(_current.Frames, 1));
        }
        if (GameCamActive) StepGameCam(delta);
        else
        {
            var eye = _focus + new Vector3(
                Mathf.Cos(_pitch) * Mathf.Sin(_yaw), Mathf.Sin(-_pitch), Mathf.Cos(_pitch) * Mathf.Cos(_yaw)) * _dist;
            _cam.Transform = new Transform3D(Basis.LookingAt(_focus - eye, Vector3.Up), eye);
        }
        // ⚠ AFTER the camera is placed, both of them: the volume follows the eye, and a pending
        // build happens here rather than in the park load for the reason on _weatherWanted.
        if (_toolOpen) UpdateGhost();
        // ⚠ AFTER the camera has been placed for this frame, or the projection is a frame stale
        // and the check is of the wrong camera.
        if (_ghostTest && !_pickChecked && _mode == Mode.Park) CheckMousePicking();
        _weather.Follow(_cam.GlobalPosition);
        if (_weatherWanted is { } wk)
        {
            _weatherWanted = null;
            GD.Print($"[weather] {wk}: {_weather.Set(_lib, wk, _cam.GlobalPosition)}");
        }

        if (_shotPath != null)
        {
            if (_current != null) { _time = _shotFrame < 0 ? 0 : _shotFrame; _current.SetFrame(_time); }
            if (++_shotWait > 6)
            {
                var img = GetViewport().GetTexture().GetImage();
                img.SavePng(_shotPath);
                GD.Print($"wrote {_shotPath} ({img.GetWidth()}x{img.GetHeight()})");
                GetTree().Quit();
            }
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            // LEFT drag orbits.
            if ((mm.ButtonMask & MouseButtonMask.Left) != 0)
            {
                _yaw -= mm.Relative.X * 0.01f;
                _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.01f, -1.5f, 1.5f);
            }
            // RIGHT or MIDDLE drag pans, in the camera's own plane so it moves with the view
            // rather than along world axes. Scaled by distance so it feels the same when zoomed in.
            else if ((mm.ButtonMask & (MouseButtonMask.Right | MouseButtonMask.Middle)) != 0
                     || ((mm.ButtonMask & MouseButtonMask.Left) != 0 && Input.IsKeyPressed(Key.Shift)))
            {
                var b = _cam.GlobalTransform.Basis;
                float k = _dist * 0.0016f;
                _focus += b.X * -mm.Relative.X * k + b.Y * mm.Relative.Y * k;
            }
        }
        if (e is InputEventMouseButton mb)
        {
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) _dist = Mathf.Max(_dist * 0.9f, 0.05f);
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) _dist *= 1.1f;
            // ⭐ RIGHT CLICK OPENS AND CLOSES THE TOOL, left click is the press -- but only a
            // CLICK. A drag of either button is the camera's, so the button is judged on release
            // by how far the mouse travelled since it went down.
            if (mb.Pressed && mb.ButtonIndex is MouseButton.Right or MouseButton.Left)
            {
                _downAt = mb.Position;
                _downButton = mb.ButtonIndex;
            }
            else if (!mb.Pressed && mb.ButtonIndex == _downButton && _mode == Mode.Park)
            {
                _downButton = MouseButton.None;
                if (mb.Position.DistanceTo(_downAt) <= 4f)
                {
                    if (mb.ButtonIndex == MouseButton.Right)
                    {
                        if (_toolOpen) { CloseTool(); GD.Print("[tool] closed"); }
                        else OpenTool(Input.IsKeyPressed(Key.Shift) ? PathTool.Kind.Queue : PathTool.Kind.Path);
                    }
                    else if (_toolOpen) PressTool();
                }
            }
        }
        if (e is InputEventKey k2 && k2.Pressed)
        {
            switch (k2.Keycode)
            {
                case Key.Space: _playing = !_playing; break;
                // ⚠ Panning can lose the model off-screen with no way back. R re-frames it.
                case Key.R: ReFrame(); break;
                // Keyboard panning, because a right-drag is not delivered on every setup.
                case Key.W: Pan(0, 1); break;
                case Key.S: Pan(0, -1); break;
                case Key.A: Pan(1, 0); break;
                case Key.D: Pan(-1, 0); break;
                case Key.Left: StepFrame(-1); break;
                case Key.Right: StepFrame(1); break;
            }
        }
    }

    void Pan(float dx, float dy)
    {
        var b = _cam.GlobalTransform.Basis;
        float k = _dist * 0.05f;
        _focus += b.X * dx * k + b.Y * dy * k;
    }

    void StepFrame(int d)
    {
        if (_current == null) return;
        _playing = false;
        _time = Mathf.PosMod(_time + d, Mathf.Max(_current.Frames, 1));
        _current.SetFrame(_time);
        _scrub.SetValueNoSignal(_time / Mathf.Max(_current.Frames, 1));
    }

    void ReFrame()
    {
        if (_ride?.Model == null) return;
        try { FrameCamera(_lib.LoadModel(_ride)); } catch { }
    }

}
