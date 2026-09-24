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
    /// <summary>The build panel's width. ⚠ Wide enough for the five category buttons the jungle
    /// archive names; the bar wraps, so a park with more or longer ones still shows them all.</summary>
    const int BuildW = 430;

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
    /// <summary>The terrain's moving water, rebuilt with the terrain.</summary>
    Water _water;
    float _waterTime;
    /// <summary>Weather asked for but not built yet. ⚠ It cannot be built inside the park load:
    /// the camera's transform is written in _Process, so at that moment _cam is still wherever the
    /// LAST park left it, and the volume would be preprocessed around the wrong place.</summary>
    Weather.Kind? _weatherWanted;
    /// <summary>The buildable overlay, rebuilt with the park. B toggles it.</summary>
    Node3D _buildable;
    /// <summary>The path tool, the console tables behind it, and the terrain model it works on.</summary>
    /// <summary>The console's clock. ⭐ EVERYTHING time-dependent belongs on it.</summary>
    readonly ConsoleClock _clock = new();
    /// <summary>What the build menu has handed the cursor, if anything.</summary>
    readonly Placement _place = new();
    Control _buildPanel;
    Control _buildBox;
    ItemList _buildList;
    HFlowContainer _buildTabBar;
    Button[] _buildTabs;
    string _buildCategory;
    readonly List<int> _buildRows = new();
    AssetLibrary.RideAssets _armedRide;
    PathTool _paths;
    ToolSounds _toolSfx;
    PathGhost _ghost;
    GhostMarkers _ghostView;
    SelectionBox _selectView;
    /// <summary>⭐ THE GATE'S OWN BOX, and the console can hold three at once (0x1B3038 walks
    /// three slots at 0x397470), so a second standing box is the hardware's own arrangement and
    /// not a second system bolted on. This one is not hover-driven: it marks a zone that is
    /// always there.</summary>
    SelectionBox _gateBox;
    /// <summary>The eight flags on the bus stop's poles, built rather than loaded -- see
    /// <see cref="EntranceFlags"/>.</summary>
    readonly EntranceFlags _flags = new();
    /// <summary>What is selected in the park, by its index in Park.Placed. ⚠ An INDEX, not an id:
    /// two of the same ride share an id, and a selection means the one you clicked.</summary>
    int _selected = -1;
    /// <summary>What the pointer is over. ⭐ The outline follows the POINTER, and a selection only
    /// takes over when the pointer is off everything -- master: "wire the outline to be when
    /// hovering over the ride", then selecting on top of that.</summary>
    int _hovered = -1;
    /// <summary>Whether the path tool is open. ⭐ On the console a build tool is a MODE you open
    /// and close, not a key you tap: while it is open the ghost follows the cursor and a press
    /// lays a run.</summary>
    bool _toolOpen;
    PathTool.Kind _toolKind = PathTool.Kind.Path;
    /// <summary>Where the current run started, or -1 when no run is going.</summary>
    int _runX = -1, _runY = -1;
    /// <summary>Whose run the open tool is laying. ⭐ A queue belongs to the ride it came out of,
    /// and carries that id into every cell so it can only ever reach that ride's door.</summary>
    int _toolOwner;
    /// <summary>Where the path out of a just-placed ride's EXIT should start, held while its queue
    /// is being run. ⚠ Cleared once it has been handed over, or the next ride inherits it.</summary>
    (int X, int Y)? _pathFrom;
    /// <summary>Which ride the pending exit path belongs to, held across the queue tool.</summary>
    int _pathOwner;
    /// <summary>Which way the pending exit opens, so the camera can face along it.</summary>
    (int Dx, int Dy) _pathFacing;
    /// <summary>One number per thing PUT DOWN. ⚠ Not the definition's id: two of the same ride
    /// share that, and then one ride's queue would happily walk into the other's door.</summary>
    int _rideSerial;
    /// <summary>The corners of the run in progress, oldest first -- the tile it started on and
    /// the end of every leg laid since. ⭐ A step back pops one and the run carries on from the
    /// one underneath, so undo and lay are the same list read from opposite ends.</summary>
    readonly List<(int X, int Y)> _runStack = new();
    /// <summary>What <see cref="PathTool.LegCount"/> was when the tool opened. ⚠ A step back must
    /// never walk out of this run and into one laid before it.</summary>
    int _legBase;
    /// <summary>Rides part way through their Create animation, with where each one's clock is.
    /// ⚠ They come OFF this list when they finish, so a park full of built rides costs nothing.</summary>
    readonly List<(AnimatedModel Model, float Frame)> _building = new();

    /// <summary>⭐⭐ THE PARK, RUNNING. Every ride put down gets its `.rse` started here, and from
    /// then on the SCRIPT decides what its model is doing -- the slot, the variant and the frame
    /// all come back out of <see cref="ParkSim"/>. Nothing in this file animates a placed ride any
    /// more, which is the point: `core/` decides and `game/` draws.
    ///
    /// ⚠ A RIDE WITH A WORKING SCRIPT MUST NOT ALSO BE IN `_building`. The script's own first
    /// instruction is `WAITANIM 0 0` -- it plays its Create animation itself -- so winding the
    /// same model by hand would have two clocks fighting over one mesh.</summary>
    ParkSim _sim;
    readonly List<(ParkRide Ride, AnimatedModel Model, Aps Anim, int Slot, int Variant)> _scripted = new();
    /// <summary>The voices the scripted rides ask for; see <see cref="RideSounds"/>.</summary>
    RideSounds _sounds;
    /// <summary>`--sound-census=N`: run the park for N seconds in REAL frames rather than winding
    /// it, so every cue gets its voice verdict, then print the census and quit.</summary>
    int _soundCensus;
    /// <summary>`--ride-film=N` (+ `--film-fps=F`, default 12): film the guest test's ride for N
    /// frames at F park-frames a second with the orbit on the ride, one PNG per frame; the
    /// `[film]` lines carry the park time so every `[snd]` cue maps to a video timestamp.</summary>
    int _rideFilm, _filmFps = 12; long _filmStartMs; float _filmYaw0 = 0.8f;
    /// <summary>The particles the scripted rides ask for, drawn as the demo scene draws them.</summary>
    RideParticles _burst;
    /// <summary>Each scripted ride's own <see cref="Model"/>, by ride id, for its fittings -- the
    /// seats ADDHEAD names are the model's `0x80` fittings, found by slot + 1.</summary>
    readonly Dictionary<int, Model> _rideMeshes = new();
    /// <summary>A cell to use instead of the mouse, for captures. Null in normal use.</summary>
    (int X, int Y)? _cursorOverride;
    bool _pickChecked;
    bool _animChecked;
    /// <summary>The park's own animation clock, separate from the Models tab's scrubber.</summary>
    float _parkTime;
    /// <summary>The run the ghost was last built for, so it is not rebuilt every frame.</summary>
    (int Sx, int Sy, int X, int Y) _ghostAt = (-1, -1, -1, -1);
    /// <summary>A button being held, so a CLICK can be told from a DRAG. ⚠ Both buttons already
    /// drive the camera -- right drags pan and left drags orbit -- so acting on the press would
    /// open the tool every time the view is moved.</summary>
    struct Held
    {
        public bool Down;
        public Vector2 At;
        public ulong Ms;
        /// <summary>Set once the pointer has travelled far enough that this is a drag, and it
        /// never goes back: a drag that returns to where it started is still a drag.</summary>
        public bool Dragged;
    }
    Held _left, _right;

    /// <summary>How far a click may travel, and how long it may last if it travels further.
    ///
    /// ⚠⚠ THIS WAS 4px AND NOTHING ELSE, and it ate clicks. A deliberate click on a real mouse
    /// moves a few pixels, so some presses simply vanished -- "sometimes the tool doesn't
    /// respond", which is exactly what a threshold set one pixel too tight looks like. A quick
    /// press is now a click however far it slid, and a slow one still is if it barely moved.</summary>
    const float ClickSlop = 10f;
    const ulong ClickMs = 350;
    PathPieces _pieces;
    Model _terrainModel;
    bool _pathTest;
    bool _ghostTest;
    bool _linkTest;
    bool _placeTest;
    bool _walkAudit;
    bool _typeAudit;
    bool _guestTest;
    /// <summary>Which ride the control run stands, by display name; Crazy Ape unless told.</summary>
    string _guestRide = "Crazy Ape";
    /// <summary>The sim's own grid, built once per park. ⭐⭐ ITS CELLS ARE THE GROUND'S. ParkPaths
    /// clones the terrain's cell bytes when it is made, and the tool writes the LIVE ones, so a
    /// clone shows the park as it was when it was asked for -- which is why the overlay reads laid
    /// path from the tool. <see cref="WalkGrid"/> swaps the clone for the live array instead: the
    /// byte the tool writes is the byte the guests read, the same frame, with nothing to keep in
    /// step and nothing to copy. ⚠ Which also means anything that writes THIS grid writes the park.</summary>
    ParkPaths _walkGrid;
    ParkEntrance _entranceTable;
    MeshInstance3D _walkView;
    bool _ghostPress;
    bool _animTest;
    bool _buildTest;
    bool _buildChecked;
    string _wantSegments;
    string _wantCam;
    /// <summary>Nudge on the gate's z, in units -- the by-eye correction on top of the z that
    /// comes out of the disc.
    ///
    /// ⭐ NO LONGER A LIVE KNOB. Master settled JUNGLE at **-2.0** and asked for the `[`/`]`
    /// keys to move to the flags, so this is seeded per world in <see cref="LoadGate"/> and the
    /// tool now drives <see cref="EntranceFlags.NudgeY"/> instead.</summary>
    float _gateNudge;
    /// <summary>The world <see cref="_gateNudge"/> was last seeded for, so a redraw does not
    /// clobber a nudge in progress. ⚠ Null, not "", so the first load always seeds.</summary>
    string _gateNudgeWorld;

    /// <summary>The by-eye gate nudge per world. ⚠⚠ JUNGLE ONLY IS MEASURED -- master playtested
    /// it and said "-2.0 on jungle". The others are **not zero by decision**, they are zero
    /// because nobody has looked yet, and a single number copied across four worlds is exactly
    /// the kind of guess this port keeps having to undo. They stay 0 until someone looks at them.</summary>
    /// <summary>The gate's z correction. ⚠⚠ NOT PER WORLD EITHER: master tested all four and it
    /// is **-2.0 in every park**, exactly as the flags' -12.95 was. ⭐ Two independent by-eye
    /// corrections, each constant across four parks with different terrain and different authored
    /// offsets, are two SYSTEMATIC errors in how this port places entrance furniture -- not eight
    /// tuning values. Named so they read as the defects they are; neither is actually fixed.</summary>
    public const float GateZError = -2.0f;

    /// <summary>The flag offset along z. ⚠⚠ NOT PER WORLD, AND THAT IS THE FINDING: I wrote
    /// this as a per-world tune and predicted "-12.95 will not survive the other three worlds".
    /// Master tested all four and it is **-12.95 in every park**.
    ///
    /// ⭐⭐ A NUMBER THAT IS IDENTICAL IN FOUR INDEPENDENT PARKS IS NOT A TUNE, IT IS A BUG WITH
    /// A CONSTANT. The four entrances have different terrain, different pole meshes and different
    /// authored x -- if the anchor derivation were merely imprecise the correction would differ
    /// between them. It does not, so <see cref="EntranceFlags.Anchors"/> is placing every flag
    /// the same distance wrong along z, and this constant is cancelling it rather than fixing it.
    ///
    /// ⚠ Kept as a single named constant precisely so it reads as the outstanding defect it is,
    /// instead of hiding as four numbers that happen to agree. The real fix is upstream and is
    /// NOT done.</summary>
    public const float FlagAnchorZError = -12.95f;
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
    Label _toolStatus;
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
            else if (a == "--link-test") _linkTest = true;
            else if (a == "--place-test") { _buildTest = true; _placeTest = true; }
            else if (a == "--walk-audit") _walkAudit = true;
            else if (a == "--guest-test") _guestTest = true;
            else if (a.StartsWith("--walk-film=")) int.TryParse(a["--walk-film=".Length..], out _walkFilm);
            // ⭐ The census borrows --guest-test's park (corridor, Crazy Ape, guests) and replaces
            // its wind-and-shoot with real frames: a voice needs frames to advance in.
            else if (a.StartsWith("--sound-census=")) { int.TryParse(a["--sound-census=".Length..], out _soundCensus); _guestTest = true; }
            else if (a.StartsWith("--ride-film=")) { int.TryParse(a["--ride-film=".Length..], out _rideFilm); _guestTest = true; }
            else if (a.StartsWith("--film-fps=")) { int.TryParse(a["--film-fps=".Length..], out _filmFps); if (_filmFps <= 0) _filmFps = 12; }
            else if (a.StartsWith("--guest-ride=")) _guestRide = a["--guest-ride=".Length..];
            else if (a == "--type-audit") _typeAudit = true;
            else if (a == "--ghost-press") { _ghostTest = true; _ghostPress = true; }
            else if (a == "--anim-test") _animTest = true;
            else if (a == "--build-test") _buildTest = true;
            else if (a.StartsWith("--segments=")) _wantSegments = a["--segments=".Length..];
            else if (a.StartsWith("--cam=")) _wantCam = a["--cam=".Length..];
            else if (a.StartsWith("--ride=")) _wantRide = a["--ride=".Length..];
            else if (a.StartsWith("--anim=")) _wantAnim = a["--anim=".Length..];
            else if (a.StartsWith("--wad=")) _wantWad = a["--wad=".Length..];
            // ⭐ TEXTURE FILTERING. Bilinear is the default (the console's) -- this is the way
            // to ask for the crunchy one, and for a render to state which it used rather than
            // inherit whatever the last keypress left behind.
            else if (a == "--nearest") Ps2Materials.Bilinear = false;
            else if (a == "--bilinear") Ps2Materials.Bilinear = true;
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
        _selectView = new SelectionBox(path => _lib?.ReadGeneric(path));
        AddChild(_selectView.Root);
        _gateBox = new SelectionBox(path => _lib?.ReadGeneric(path));
        AddChild(_gateBox.Root);
        AddChild(_flags.Root);
        AddChild(_thoughts.Root);

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

        // ⭐⭐ THE BUILD MENU, ITS OWN PANEL. ⚠ It used to live inside the F3 panel, so hiding the
        // debug readout hid the way to build and showing it brought a wall of diagnostics back --
        // two different jobs sharing one switch. It anchors down the RIGHT, opens on Tab and
        // answers to nothing else.
        //
        // The categories are the ARCHIVE'S OWN FOLDERS -- Rides, Shops, Sideshow, Features,
        // Upgrades -- so the grouping is the game's, and it is per park because the open WAD is
        // the park.
        var buildBox = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Pass,
                                            CustomMinimumSize = new Vector2(BuildW, 0) };
        buildBox.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        buildBox.OffsetLeft = -BuildW;
        ui.AddChild(buildBox);
        _buildPanel = new VBoxContainer();
        buildBox.AddChild(_buildPanel);
        _buildPanel.AddChild(new Label { Text = "BUILD  (Tab)" });
        // ⭐⭐ THE CATEGORY BAR WRAPS. At 300 wide in one row the five folders ran off the edge and
        // Shops and Upgrades were simply not on screen -- master: "expand the build tab to be wide
        // enough to show shops too". Widening alone would only move the cliff: the categories are
        // the ARCHIVE'S folder names, so their number and their length are the disc's to choose
        // and not mine to size a panel around. A flow container puts what fits on a row and the
        // rest on the next, so every category is reachable at any width.
        _buildTabBar = new HFlowContainer();
        _buildPanel.AddChild(_buildTabBar);
        _buildTabs = Array.Empty<Button>();
        // ⚠⚠ NO KEYBOARD FOCUS. An ItemList with focus swallows every key press -- R, the commas,
        // WASD, the lot -- so picking a ride left the whole keyboard dead until you clicked the
        // world again. Master: "the list of rides etc is eating my keyboard control inputs".
        _buildList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, AllowReselect = true,
                                    FocusMode = Control.FocusModeEnum.None };
        _buildList.ItemSelected += i => ArmFromList((int)i);
        _buildPanel.AddChild(_buildList);
        _buildBox = buildBox;

        // ⭐⭐ THE TOOL SAYS WHAT IT THINKS, ON SCREEN. Every refusal already printed a reason to
        // the console, which nobody playing the game can see -- so a click over the panel, or one
        // whose ray missed the plot, looked exactly like a click that was lost. Now the difference
        // is readable without a terminal.
        _toolStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart,
                                  MouseFilter = Control.MouseFilterEnum.Ignore };
        _toolStatus.AddThemeColorOverride("font_color", new Color(0.6f, 0.85f, 1f));
        col.AddChild(_toolStatus);

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
        // ⭐ L flips the texture filter, live, on everything already standing.
        else if (k.Keycode == Key.L)
        {
            Ps2Materials.Bilinear = !Ps2Materials.Bilinear;
            int n = Ps2Materials.Refilter(this);
            string line = $"textures {(Ps2Materials.Bilinear ? "bilinear" : "nearest")} ({n} materials)";
            Status(line);
            GD.Print($"[tex] {line}");
        }
        else if (k.Keycode == Key.B && _mode == Mode.Park && _buildable != null)
        {
            _buildable.Visible = !_buildable.Visible;
            GD.Print($"[build] overlay {(_buildable.Visible ? "on" : "off")}");
        }
        // ⭐ N shows what the SIM thinks it can walk on: the entrance the park came with, in blue,
        // and everything laid since, in green for path and amber for queue.
        else if (k.Keycode == Key.N && _mode == Mode.Park) ToggleWalkOverlay();
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
        else if (k.Keycode == Key.Tab && _mode == Mode.Park) ToggleBuildMenu();
        // ⭐ R and . turn it clockwise, , turns it back. ⚠ R is the camera's zoom-in elsewhere;
        // while something is HELD it belongs to the thing being turned, which is the same bargain
        // the mouse buttons make with the build tools.
        else if (k.Keycode is Key.R or Key.Period && _place.Active)
        { _place.Turn(1); _ghostAt = (-1, -1, -1, -1); }
        else if (k.Keycode == Key.Comma && _place.Active)
        { _place.Turn(-1); _ghostAt = (-1, -1, -1, -1); }
        else if (k.Keycode == Key.Escape && _place.Active)
        {
            _place.Clear();
            _ghostView?.Clear();
            Status("nothing held");
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
            RefreshFloor();
            _toolSfx?.Play(ToolSounds.Cue.Undo);
            GD.Print("[path] taken back");
        }
        else if (k.Keycode is Key.Bracketleft or Key.Bracketright && _mode == Mode.Park)
        {
            // ⭐ FINE BY DEFAULT, COARSE ON SHIFT. 0.25 was too big to settle the entrance with --
            // master, calibrating it by eye, asked for a smaller step. 0.05 is a twentieth of a
            // cell, which is about where a tile edge stops being ambiguous; shift keeps the old
            // 0.25 so crossing a whole tile is still five presses rather than twenty-five.
            float step = Input.IsKeyPressed(Key.Shift) ? 0.25f : 0.05f;
            // ⭐⭐ BACK ON THE GATE. The flags are settled -- master measured the same -12.95 in
            // all four parks, so that is a constant now and not something to keep tuning -- and
            // they asked for the tool back to do the other three parks' gates.
            _gateNudge += k.Keycode == Key.Bracketright ? step : -step;
            // ⚠ Rounded, or repeated float additions drift into 0.15000000000000002 and the log
            // becomes unreadable at exactly the moment it is being used to write a number down.
            _gateNudge = Mathf.Round(_gateNudge * 1000f) / 1000f;
            // ⚠⚠ ON SCREEN, NOT DOWN A TERMINAL. Master: "i also dont see where my nudge is
            // being printed?" -- because GD.Print goes to a console nobody playing the game has in
            // front of them. This panel was built for exactly that ("every refusal already printed
            // a reason to the console, which nobody playing the game can see") and I still wrote
            // the ONE number master is meant to read off and hand back into the console alone.
            // ⚠ THE WORLD IS IN THE READOUT. The gate's nudge is per park and master is about to
            // do three more, so a bare number would be one they have to remember the park for.
            var rw = (_lib?.WadName ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
            int ri = Array.FindIndex(rw, x => x.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase));
            string line = $"{(ri >= 0 ? rw[ri][..^4] : "?")} gate nudge {_gateNudge:+0.00;-0.00;0}"
                        + $"  ({(step > 0.1f ? "coarse" : "fine")}, shift for the other)";
            Status(line);
            GD.Print($"[gate] {line}");
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
        if (_gateBox != null) _gateBox.Root.Visible = m == Mode.Park;
        _flags.Root.Visible = m == Mode.Park;
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

    /// <summary>The build category a thing belongs in. ⭐⭐ THE ARCHIVE FOLDER, EXCEPT THAT A
    /// COASTER IS ITS OWN CATEGORY. Master asked for "rides, track, coasters, shops, features etc.
    /// as their own buttons", and the folders do not separate them -- every coaster is filed under
    /// Rides beside the flat rides.
    ///
    /// ⭐ The .sam files separate them completely, and the difference is not a name to match on.
    /// A coaster declares `sCoasterType`, `sTrainType`, `asCarTypes`, `asCrossSections` and
    /// `asPylonControls` -- 252 keys describing a track to be BUILT -- and declares NO
    /// `Info.Shape`, `Info.Name` or `Info.Id`. A ride declares exactly those three, plus
    /// `UsageInfo` and `Upgrades`, and none of the coaster keys. Measured on jungle's coaster1
    /// against Dizzy Dinos: the two key sets are disjoint in every one of those fields.
    ///
    /// ⚠ And "Track" is NOT a category of things. `STR_LISTBOX_BUILD_TRACK` = "Build Track" and
    /// `STR_LISTBOX_EDIT_TRACK` = "Edit Track" are LISTBOX strings -- entries in a list, which is
    /// to say ACTIONS on a coaster, not a shelf of items to place. So it is not made into a button
    /// holding nothing.</summary>
    string BuildCategory(AssetLibrary.RideAssets r)
    {
        var def = r.Model == null ? null : DefinitionFor(r.Model);
        return def != null && def.Fields.Keys.Concat(def.Blocks.Keys)
                   .Any(k => k.StartsWith("sCoasterType.", StringComparison.OrdinalIgnoreCase))
             ? "Coasters" : Category(r.Name);
    }

    /// <summary>The name a thing is listed under. ⭐⭐ `Info.Name` FIRST, THEN THE TEXT DATABASE.
    /// A coaster's .sam declares no name at all, so the build list showed `coaster1.mps` --
    /// but the disc knows it: `STR_GRAPHICS_JUNGLE_RIDES_COASTER1_COASTER1` reads "Chak Atak".
    /// That key is built from the path, which is why it works for a file that says nothing about
    /// itself. Falls back to the filename, which is what it always did.</summary>
    string DisplayName(AssetLibrary.RideAssets r, RideDefinition def)
    {
        if (def?.Name is { Length: > 0 } named) return named;
        // ⚠⚠ THE KEY IS BUILT FROM THE MODEL, NOT FROM THE .sam. For most things the two share a
        // stem -- `Rides/dizzyd/dizzyd` either way -- so keying on the definition worked and
        // looked general. A coaster's do NOT: `/Rides/Coaster1/coaster.sam` beside
        // `/Rides/Coaster1/coaster1.mps`, and the table's key is
        // STR_GRAPHICS_JUNGLE_RIDES_COASTER1_COASTER1. Keying on the .sam asked for ..._COASTER
        // and got -1, which is how "the name is not on the disc" and "I asked for the wrong name"
        // look the same. The model is tried first and the definition kept as the fallback.
        // ⚠⚠ THE WORLD COMES FROM THE .sam's PATH AND THE STEM FROM THE MODEL'S, because the two
        // paths are not the same shape: `def.Source` is a full disc path
        // (/DATA/JUNGLE.WAD/Rides/Coaster1/coaster.sam) and `Model.Path` is WAD-RELATIVE
        // (/Rides/Coaster1/coaster1.mps). Keying on the .sam alone asks for ..._COASTER1_COASTER
        // and misses; keying on the model alone finds no `.WAD` element and misses too, silently,
        // in both cases looking exactly like "the disc does not know this name".
        if (_text != null && def != null)
        {
            var src = def.Source.Split('/', StringSplitOptions.RemoveEmptyEntries);
            int wi = Array.FindIndex(src, x => x.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase));
            if (wi >= 0)
            {
                string world = src[wi][..^4];
                foreach (var p2 in new[] { r?.Model?.Path, string.Join('/', src.Skip(wi + 1)) })
                {
                    if (string.IsNullOrEmpty(p2)) continue;
                    int row = _text.IndexOf(TextDatabase.GraphicsKey(world, p2));
                    if (row >= 0 && _text.Text("eng", row) is { Length: > 0 } t) return t;
                }
            }
        }
        return Leaf(r.Name);
    }

    /// <summary>⚠ A COASTER'S FOLDER HOLDS ITS PARTS TOO. `/Rides/coaster1/` ships the track, the
    /// car (`croccar`) and the pylon (`StdPylon`) as separate models, and DefinitionFor matches a
    /// .sam by directory suffix, so all three resolved to the same coaster definition and all
    /// three appeared in the build list under their filenames. One folder is one coaster: the
    /// entry whose own stem matches the folder is the one that lists.</summary>
    static bool IsCoasterPart(string path)
    {
        int slash = path.LastIndexOf('/');
        if (slash <= 0) return false;
        string stem = path[(slash + 1)..];
        int dot = stem.LastIndexOf('.');
        if (dot > 0) stem = stem[..dot];
        int prev = path.LastIndexOf('/', slash - 1);
        string folder = path[(prev + 1)..slash];
        return !stem.Equals(folder, StringComparison.OrdinalIgnoreCase);
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
                   + "N walkable  |  O take it back  |  M straight/elbow segments  |  Esc close the tool\n"
                   + "Tab build menu  |  LMB or RMB opens the path tool; RMB shuts it or drops\n"
                   + "what you hold  |  . turns it  |  shift stamps  |  queues come with the ride\n"
                   + "in the park the mouse buttons are the TOOL'S -- pan with the middle drag";
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
            // ⚠⚠ AFTER THE CATALOGUE IS POPULATED, NOT BEFORE. This ran between the constructor
            // and AddWad, so it walked an EMPTY list and attached nothing -- the join was dead at
            // runtime while its checks stayed green, because they exercise CompiledAssets
            // directly instead of this path. Exactly the shape this port spent the night
            // deleting: correct code, passing tests, never reached. astraclaw caught it.
            AttachCompiledRecords();
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
    /// <summary>⭐⭐ GIVE EVERY DEFINITION THE RECORD THE GAME ACTUALLY LOADS. The .sam is the
    /// authored source and the compiled DBA is what ships, and they disagree -- three worlds
    /// author 15 happiness for their balloon shop and every compiled row reads 10.
    ///
    /// ⭐ Joined on the traced `STR_GRAPHICS_<WORLD>_<PATH>` identity, NEVER on matching numbers:
    /// a value join could only ever find the rows that already agree, and would drop exactly the
    /// disagreements this exists for. astraclaw's named-record check is the control -- Balloon
    /// 239, VampShop 153, FatFairy 66, Droid 388 all carry 10 -- and Droid settles the method,
    /// since its compiled price/cost is 50/35 where the other three are 45/30, so a profile match
    /// would have mis-grouped it.
    ///
    /// ⚠ The `.sam` source is disc-absolute (`/DATA/JUNGLE.WAD/Shops/...`) while the identity
    /// wants world plus WAD-RELATIVE path, which is the same split the display-name lookup above
    /// already handles.
    ///
    /// ⚠ Silent on success and LOUD on a miss: a join that quietly covered half the shops would
    /// look identical to one that worked, and half a join is how a wrong number reaches a guest.</summary>
    void AttachCompiledRecords()
    {
        if (_cat == null || _text == null) return;
        try
        {
            WadArchive data = null;
            foreach (var f in _lib.WadFiles())
                if (f.Path.EndsWith("/DATA.WAD", StringComparison.OrdinalIgnoreCase))
                    data = new WadArchive(_lib.ReadDisc(f));
            var dba = data?.Entries.FirstOrDefault(e => e.Path.Equals("/arsdb.dba", StringComparison.OrdinalIgnoreCase));
            if (dba == null) { GD.Print("[park] compiled records: /arsdb.dba MISSING -- authored .sam values stand"); return; }
            var compiled = new CompiledAssets(new AssetResourceDatabase(data.Read(dba)), _text);
            compiled.Attach(_cat.All, out string report);
            GD.Print("[park] compiled records: " + report);
        }
        catch (Exception ex) { GD.PrintErr($"[park] compiled records failed: {ex.Message}"); }
    }

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
            _water = new Water(_terrain.Root, tm, mat => TextureNear(pick.Path, mat).Soft);
            GD.Print($"[water] {_water.Report}");
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
        // ⚠ The walk grid and the guests belong to the park that is going, not the one coming. The
        // grid was never reset before this, so a second map walked the first map's ground.
        ResetGuests();
        _walkGrid = null;
        // ⭐ AND THE SIM WITH IT: it was made on that grid, and its rides stood on that park.
        _sim = null; _scripted.Clear(); _rideMeshes.Clear(); _shotWound = false;
        // ⭐⭐ AND THE SOUND, FOR THE SAME REASON THE GRID IS RESET TWO LINES UP. `_sounds` is
        // built `??=` from `SoundCatalogue(disc, world, 1)` and `(.., 2)` -- the CURRENT world's
        // event maps -- so keeping it across a world change resolves the new park's cues against
        // the old park's maps, and leaves the old park's voices playing over the new one.
        //
        // ⚠ Nothing reset it, and `RideSounds.Clear()` had no caller anywhere in this file.
        // `RideScriptDemo` -- the sibling scene -- already does `_sounds?.Clear(); _sounds = null;`
        // correctly, so this was the Viewer alone. astraclaw found it auditing the audio
        // lifecycle. Same bug as the walk grid, same function, one object later.
        //
        // ⚠ `_burst` KEEPS ITS HOLDER but loses its INSTANCES, and that distinction is
        // astraclaw's -- I had the first half and missed the second. The library is
        // `/DATA/PARTICLE.WAD`, one file for the whole game rather than per world, so rebuilding
        // it would be waste. The live emitters are NOT global: they were parented to the last
        // park's rides, and keeping the holder kept them too. Cache retained, instances cleared.
        _sounds?.Clear(); _sounds = null;
        _burst?.Clear();
        _standingPlaces.Clear(); _standing.Clear();
        if (_terrainModel?.Field == null) return;
        if (_pieces == null)
        {
            try
            {
                var exe = _lib.Executable();
                if (exe != null)
                {
                    _pieces = PathPieces.ReadExecutable(exe);
                    // ⭐ The park's own entrance walkway comes out of the same executable, from the
                    // table 0x14E5B0 paints it with -- not out of the terrain, which does not
                    // carry it.
                    try { _entranceTable = ParkEntrance.ReadExecutable(exe); }
                    catch (Exception e) { GD.PrintErr($"[walk] the entrance table would not read: {e.Message}"); }
                }
                else GD.Print("[path] no SLES_500.32 on this disc -- path laying is off");
            }
            catch (Exception e) { GD.PrintErr($"[path] the piece tables would not read: {e.Message}"); }
        }
        if (_pieces == null) return;
        _paths = new PathTool(_terrainModel, _pieces);
        // ⭐ The park's own entrance walkway, from the game's table. It joins like path and
        // nothing may be laid on it, so a run brought up to the gate attaches to the way in.
        var entry = default(ParkEntranceEntry);
        if (_entranceTable != null && _terrainModel.Field is { } fld)
        {
            entry = _entranceTable.Fit(fld, ParkEntrance.WalkwayColumnFromPoles(_terrainModel), out string which);
            if (!entry.Empty) _paths.SetWalkway(entry.Cells().Select(c => (c.X, c.Z)));
            GD.Print($"[path] the park's walkway: {which}");
        }
        _ghost = new PathGhost(_paths) { Occupied = (x, y) => !_park.Vacant(x, y) };
        // ⭐ The blueprint asks the tool what is already on a cell, so a stub can tell path from
        // queue. Through a lambda, because _paths is rebuilt with every park.
        _place.GroundAt = (x, y) => _paths?.KindAt(x, y) ?? PathTool.Kind.None;
        // ⚠ Once, not per park: the UI bank is the same file whichever park is up, and decoding it
        // again on every load would be four decodes for nothing.
        if (_toolSfx == null) { _toolSfx = new ToolSounds(_lib, this); GD.Print($"[sfx] {_toolSfx.Report}"); }
        if (_wantSegments != null)
            _ghost.Shape = _wantSegments.StartsWith("elbow", StringComparison.OrdinalIgnoreCase)
                ? PathGhost.Segment.Elbow : PathGhost.Segment.Straight;
        CloseTool();
        GD.Print($"[path] {_paths.Report}"
               + (_paths.Ready ? $"; {_pieces.Path.Count} path and {_pieces.Queue.Count} queue pieces" : ""));
        if (!_paths.Ready) { _paths = null; return; }
        LayStartingPath(entry);
        if (_pathTest || System.Environment.GetEnvironmentVariable("TPW_PATH_TEST") == "1") LayTestPath();
        if (_ghostTest || System.Environment.GetEnvironmentVariable("TPW_GHOST_TEST") == "1") ShowTestGhost();
        if (_linkTest || System.Environment.GetEnvironmentVariable("TPW_LINK_TEST") == "1") CheckLinking();
        if (_walkAudit) WalkAudit();
        if (_typeAudit) TypeAudit();
        if (_guestTest) GuestTest();
        // ⭐ So a capture can photograph the overlay, which has no key to press.
        if (System.Environment.GetEnvironmentVariable("TPW_WALK_OVERLAY") == "1") ToggleWalkOverlay();
    }

    /// <summary>The path a blank park comes with: two cells wide, running in from the walkway's
    /// mouth. Master, playing it: "a 4 long 2 wide path tile extends into the park from the
    /// entrance on a blank park."
    ///
    /// ⭐ THE LENGTH IS THE TABLE'S, from 0x14E5B0's own four calls to the path layer -- see
    /// <see cref="ParkEntranceEntry.StartingPath"/>. It is 4 rows in JUNGLE and 6 in FANTASY,
    /// the two worlds an entry has been matched to a real park in, and nothing here rounds it to
    /// what master happened to count in the one they had open.
    ///
    /// ⚠ Laid with the ordinary tool, so it wears the same pieces, joins the walkway and can
    /// be taken up again -- which is what it is in the game, not scenery.</summary>
    void LayStartingPath(ParkEntranceEntry entry)
    {
        if (entry.Empty || _paths == null) return;
        var want = entry.StartingPath().ToList();
        int laid = want.Count(c => _paths.Lay(c.X, c.Z));
        RefreshFloor();
        GD.Print($"[path] the park's own path: {laid} of {want.Count} cells laid, "
               + $"2 wide and {entry.PathRows + 1} long from the mouth at z {entry.ZEnd}"
               + (laid == want.Count ? "" : " -- SOME REFUSED, the plot may not reach that far"));
    }

    /// <summary>Put each walking or standing-service guest's want over their head.
    ///
    /// ⚠ THE FACILITY FLAGS ARE PASSED TRUE, and that is a decision worth stating. The one path
    /// that has been READ (`0x20C930` case 0) sets the hungry/thirsty/toilet thought only after
    /// `FUN_0020F888` finds a shop of type 0x34/0x32/0x3B -- so on that path a guest with nothing
    /// to buy thinks nothing. But that is ONE arm of a `rand(6)` switch and the other arms, and
    /// whatever else writes `+0x40`, are unread: "the console never shows a want it cannot meet"
    /// would be a claim about code I have not looked at. The want is shown, and this says why.
    ///
    /// ⚠⚠ AND IT WAS BRIEFLY CHANGED TO ASK THE PARK, WHICH WAS WRONG. Once facilities became
    /// real and routable it looked obvious to gate the bubble on one existing -- but that is the
    /// same unread claim as above, made with no new evidence, and it would have taken every
    /// hunger bubble out of any park with no shop in it. Routing is a separate question and
    /// already only picks facilities that exist. Caught in review by astraclaw.
    ///
    /// Walking and outside-service guests have full bodies to anchor a bubble. Hidden,
    /// seated and unresolved scripted-walk guests do not gain a fabricated standing anchor.</summary>
    void PlaceThoughts()
    {
        if (_visitors?.Needs is not { } needs || !_thoughts.Ready) return;
        // ⚠⚠ SHOOT THE FRAME AFTER THE SIGHTING, UNCONDITIONALLY. SaveShot grabs the viewport
        // as it was last DRAWN, so a shot taken on the same frame the camera moved photographs the
        // old camera -- and waiting for a second sighting never came true, because the park has
        // exactly one frame with a wanting walker in it before they are handed to the ride. So the
        // sighting arms this, and the very next frame fires it whatever the park has done since.
        if (_wantArmed)
        {
            _wantArmed = false;
            SaveShot(System.Environment.GetEnvironmentVariable("TPW_WANT_SHOT"));
            GetTree().Quit();
            return;
        }
        var visibleGuests = _guests.Guests.Select(g => g.Id).Concat(_standing.Keys).Distinct().ToArray();
        foreach (int id in visibleGuests)
        {
            if (!needs.Has(id)) continue;
            var want = needs.Decide(id, foodNearby: true, drinkNearby: true, toiletNearby: true);
            // Both walking and outside-service bodies use world-space bubble anchors.
            if (_actors.TryGetValue(id, out var actor) && IsInstanceValid(actor))
                _thoughts.Show(id, want, actor.GlobalPosition);
            else _thoughts.Hide(id);
        }
        _thoughts.Sweep(visibleGuests.ToHashSet());

        // ⭐ AND A CAMERA THAT FINDS ONE. A bubble is a third of a guest's height, so at the park
        // view it is small and "no bubble in the picture" says little. This points the free camera
        // at somebody who actually wants something, which is a framing the feature is legible in.
        //
        // ⚠⚠ THIS USED TO SAY "the camera sits 2576 units up -- so it is SUB-PIXEL", and both
        // halves were wrong. `GameCamera` holds its pose in RAW units and `Build` divides by
        // `TileUnits` (256), so the reset pose is 10.06 Godot units up and ~12.2 of slant range,
        // where a bubble is small but plainly visible. I had taken a raw-unit constant out of a
        // comment and used it as a Godot distance; astraclaw caught it. The tell was there to be
        // noticed -- 2576 units over a park with ONE-unit cells is two and a half thousand tiles
        // of altitude.
        if (System.Environment.GetEnvironmentVariable("TPW_WANT_CAM") == "1")
        {
            var who = _guests.Guests.FirstOrDefault(
                g => needs.Has(g.Id) && needs.Of(g.Id).Thought != Thought.Normal
                     && _actors.TryGetValue(g.Id, out var a) && IsInstanceValid(a));
            if (who != null && _actors.TryGetValue(who.Id, out var body) && IsInstanceValid(body))
            {
                _freeCam = true;
                _focus = body.GlobalPosition + new Vector3(0f, 0.45f, 0f);
                _dist = 2.2f;
                _pitch = -0.12f;
                // ⭐⭐ AND SHOOT WHEN THERE IS SOMETHING TO SHOOT. Chasing the guest-test's own
                // staging cost four renders that each photographed a moment with no wants in it --
                // by the frame it shoots, the walkers have been handed to the ride. This waits for
                // the condition instead of for a frame number, which is the difference between an
                // instrument and a guess.
                if (System.Environment.GetEnvironmentVariable("TPW_WANT_SHOT") != null)
                {
                    // ⚠⚠ SIGHTINGS, NOT CONSECUTIVE FRAMES, and that distinction is the whole
                    // bug. The guest test winds the sim in jumps of hundreds of ticks between
                    // render frames, so "a wanting walker on two frames in a row" can never come
                    // true: the park hands everyone to the ride in the gap. Counting sightings
                    // lets the second one arrive whenever it arrives, which is what waiting for a
                    // condition means.
                    GD.Print($"[want] armed on guest {who.Id} thinking {needs.Of(who.Id).Thought} "
                           + $"at {body.GlobalPosition}; shooting next frame");
                    _wantArmed = true;
                }
            }
        }

        // ⭐ A CENSUS, because a picture cannot tell "no bubbles because nobody wants anything"
        // from "no bubbles because the sprite is broken". It prints what the needs actually ARE.
        if (_parkTicks - _lastWantCensus < 250) return;
        _lastWantCensus = _parkTicks;
        var held = _guests.Guests.Where(g => needs.Has(g.Id)).Select(g => needs.Of(g.Id)).ToList();
        if (held.Count == 0)
        {
            // ⚠⚠ SAY WHAT WAS COUNTED. This used to print "nobody in the park", and it is a
            // census of WALKING guests -- everyone handed to a ride has left the walk, so a full
            // park mid-ride reads as empty. astraclaw, from a smoke run: 68 boardings, 40 returns,
            // 28 still queued or riding at 180s, and my line still said nobody. It cost me
            // several renders today: I read it as the park emptying and went hunting for a frame
            // with people in it, when the people were on the ride.
            int elsewhere = needs.All.Count;
            GD.Print($"[want] no WALKING guests to draw a bubble over"
                   + (elsewhere == 0 ? "; and nobody in the park at all"
                                     : $"; {elsewhere} still have needs -- queued or riding, off the walk"));
            return;
        }
        var tally = held.GroupBy(v => v.Thought).OrderByDescending(x => x.Count())
                        .Select(x => $"{x.Key} {x.Count()}");
        GD.Print($"[want] t={_parkTicks * ParkSim.TickMilliseconds / 1000.0:F1}s {held.Count} WALKING"
               + (needs.All.Count > held.Count ? $" (+{needs.All.Count - held.Count} off the walk)" : "") + ": "
               + string.Join(", ", tally)
               + $"  | hunger {held.Min(v => v.Hunger)}..{held.Max(v => v.Hunger)}"
               + $"  thirst {held.Min(v => v.Thirst)}..{held.Max(v => v.Thirst)}"
               + $"  toilet {held.Min(v => v.Toilet)}..{held.Max(v => v.Toilet)}"
               + $"  happy {held.Min(v => v.Happiness)}..{held.Max(v => v.Happiness)}");
    }

    long _lastWantCensus = -1000;
    bool _wantArmed;

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
        RefreshFloor();
    }

    /// <summary>⭐ A CONTROL FOR THE LINKING RULES, and every case in it has a partner that must
    /// come out the OTHER way -- an answer that is right everywhere says nothing about whether the
    /// rule is doing any work.
    ///
    /// The four claims under test, in master's words:
    ///   "the queue is EXACTLY as its drawn with the tool"         -> a folded queue does not fuse
    ///   "they should not connect to other rides' queues"          -> two runs side by side stay apart
    ///   "only ... connected at the end of queues"                 -> a path joins the tip, not the middle
    ///   "connected to the entry/exit points"                      -> a door is an arm
    ///
    /// ⚠ Read as LINK BITS, not as a picture. N=01 NE=02 E=04 SE=08 S=10 SW=20 W=40 NW=80, so a
    /// queue running north-south reads 11 in the middle and 01 or 10 at its ends. A photograph of
    /// two queue runs a cell apart cannot tell "joined" from "not joined" at all.</summary>
    void CheckLinking()
    {
        if (_paths == null) return;
        var f = _park.Field;
        int cx = f.Width / 2 + 8, cy = f.Height / 2 - 6;
        for (int r = 0; r < 16 && !_paths.CanLay(cx, cy); r++) { cx += 1; if (!_paths.CanLay(cx, cy)) cy += 1; }

        // Ride 1's queue: five cells north to south, laid as one run.
        var runA = new List<(int X, int Y)>();
        for (int i = 0; i < 5; i++) runA.Add((cx, cy + i));
        LayRun(runA, PathTool.Kind.Queue, 1);
        // Ride 2's queue: five more, ONE CELL EAST of it. Different ride, touching along its
        // whole length -- if anything derives a queue's links from its neighbours, this is where
        // the two runs fuse into a slab.
        var runB = new List<(int X, int Y)>();
        for (int i = 0; i < 5; i++) runB.Add((cx + 1, cy + i));
        LayRun(runB, PathTool.Kind.Queue, 2);

        GD.Print("[link] two queues, one cell apart, one run each:");
        for (int i = 0; i < 5; i++)
            GD.Print($"[link]   ride1 {_paths.Describe(cx, cy + i)} | ride2 {_paths.Describe(cx + 1, cy + i)}");
        bool fused = false;
        for (int i = 0; i < 5; i++)
        {
            if ((_paths.LinkBits(cx, cy + i) & PathPieces.East) != 0) fused = true;
            if ((_paths.LinkBits(cx + 1, cy + i) & PathPieces.West) != 0) fused = true;
        }
        GD.Print($"[link] two rides' queues side by side: {(fused ? "FUSED -- they link across" : "apart, as they must be")}");

        // ⭐ THE PARTNER CASE. The same two cells, laid as ONE run that steps east, MUST link --
        // otherwise "apart" above is just the code never linking anything.
        int jx = cx + 4, jy = cy + 8;
        // ⚠ BOTH cells, not just the first. A search that only asked about the near one parked the
        // run half on a no-build tile, and the control then reported the WIRING as broken when the
        // only thing broken was where it had put itself.
        for (int r = 0; r < 24 && !(_paths.CanLay(jx, jy) && _paths.CanLay(jx + 1, jy)); r++) jy += 1;
        LayRun(new List<(int X, int Y)> { (jx, jy), (jx + 1, jy) }, PathTool.Kind.Queue, 3);
        bool joined = (_paths.LinkBits(jx, jy) & PathPieces.East) != 0
                   && (_paths.LinkBits(jx + 1, jy) & PathPieces.West) != 0;
        GD.Print($"[link] two cells of ONE run at ({jx},{jy}): {_paths.Describe(jx, jy)} | {_paths.Describe(jx + 1, jy)}"
               + $" -- {(joined ? "linked, as they must be" : "NOT LINKED -- the run order never reached the piece")}");

        // A path beside the queue's MIDDLE and a path beside its TIP.
        int px = cx - 1;
        _paths.Lay(px, cy + 2, PathTool.Kind.Path);          // beside the middle of ride 1's queue
        _paths.Lay(px, cy + 4, PathTool.Kind.Path);          // beside its last cell -- the tip
        int mid = _paths.LinkBits(px, cy + 2), tip = _paths.LinkBits(px, cy + 4);
        // ⭐⭐ NEITHER, NOW. Master: "its possible for them to be adjacent, but not connected."
        // A path lying beside a queue -- at its middle or at its very tip -- has not been joined
        // to it, and must show nothing. The connection is made by running the queue ONTO the path,
        // which is the pair below.
        GD.Print($"[link] path beside the queue's middle {mid:X2}, beside its tip {tip:X2}"
               + $" -- {((mid & PathPieces.East) == 0 && (tip & PathPieces.East) == 0 ? "neither joins, as it must be" : "WRONG -- adjacent is not connected")}");

        // ⚠ AND THE PARTNER: a queue actually RUN ONTO a path makes the one cell that is BOTH, and
        // that cell must carry an arm back down the queue. Without this half, "nothing joins" would
        // be satisfied by a tool that never joins anything.
        int jx2 = cx - 8, jy2 = cy;
        for (int r = 0; r < 24 && !(_paths.CanLay(jx2, jy2) && _paths.CanLay(jx2, jy2 + 1)
                                 && _paths.CanLay(jx2, jy2 + 2)); r++) jy2 += 1;
        _paths.Lay(jx2, jy2, PathTool.Kind.Path);
        LayRun(new List<(int X, int Y)> { (jx2, jy2 + 2), (jx2, jy2 + 1), (jx2, jy2) }, PathTool.Kind.Queue, 8);
        int junction = _paths.LinkBits(jx2, jy2);
        GD.Print($"[link] a queue run ONTO a path at ({jx2},{jy2}): now {_paths.Describe(jx2, jy2)}"
               + $" -- {(_paths.KindAt(jx2, jy2) == PathTool.Kind.Both && (junction & PathPieces.South) != 0 ? "BOTH, with an arm back down the queue, as it must be" : "NOT JOINED")}");

        // A door: register one east of a fresh path cell and watch the arm appear.
        int dx2 = cx + 6, dy2 = cy + 2;
        for (int r = 0; r < 12 && !(_paths.CanLay(dx2, dy2) && _paths.CanLay(dx2 + 1, dy2)); r++) dy2 += 1;
        _paths.Lay(dx2, dy2, PathTool.Kind.Path);
        int before = _paths.LinkBits(dx2, dy2);
        _paths.AddDoor(dx2 + 1, dy2, 9, entrance: true, dx: -1, dy: 0);
        int after = _paths.LinkBits(dx2, dy2);
        GD.Print($"[link] a lone path cell reads {before:X2}; with a ride door to its east {after:X2}"
               + $" -- {((before & PathPieces.East) == 0 && (after & PathPieces.East) != 0 ? "the door is an arm, as it must be" : "THE DOOR DID NOTHING")}");

        // ⭐ A QUEUE MAY REACH AN ENTRANCE AND MUST NOT REACH AN EXIT. Master: "the queue shouldnt
        // get the connected sprite for the 'internal' path tile of the exit". Two queue cells of
        // the same ride, one with each kind of door beside it -- the pair is the control, because
        // one of them alone cannot tell "the rule works" from "doors never link to queues".
        int qx = cx - 5, qy = cy + 2;
        for (int r = 0; r < 20 && !(_paths.CanLay(qx, qy) && _paths.CanLay(qx + 1, qy)
                                 && _paths.CanLay(qx, qy + 3) && _paths.CanLay(qx + 1, qy + 3)); r++) qy += 1;
        _paths.Lay(qx, qy, PathTool.Kind.Queue, 7);
        _paths.AddDoor(qx + 1, qy, 7, entrance: true, dx: -1, dy: 0);
        _paths.Lay(qx, qy + 3, PathTool.Kind.Queue, 7);
        _paths.AddDoor(qx + 1, qy + 3, 7, entrance: false, dx: -1, dy: 0);
        int toEntrance = _paths.LinkBits(qx, qy) & PathPieces.East;
        int toExit = _paths.LinkBits(qx, qy + 3) & PathPieces.East;
        GD.Print($"[link] queue beside its ride's ENTRANCE {toEntrance:X2}, beside its EXIT {toExit:X2}"
               + $" -- {(toEntrance != 0 && toExit == 0 ? "in only, as it must be" : "WRONG")}");

        // ⭐ AND A LEG GOES BACK. Two legs laid, one stepped back: the second must vanish and the
        // first must survive, or "undo" is either doing nothing or undoing the lot.
        int lx = cx + 10, ly = cy;
        for (int r = 0; r < 20 && !_paths.CanLay(lx, ly); r++) ly += 1;
        LayLeg(new List<(int X, int Y)> { (lx, ly), (lx, ly + 1), (lx, ly + 2) }, PathTool.Kind.Path, 0);
        LayLeg(new List<(int X, int Y)> { (lx, ly + 2), (lx + 1, ly + 2), (lx + 2, ly + 2) }, PathTool.Kind.Path, 0);
        var mid1 = _paths.KindAt(lx, ly + 1);
        var far1 = _paths.KindAt(lx + 2, ly + 2);
        bool undone = _paths.UndoLeg();
        // ⭐⭐ AND THE PARK'S OWN WALKWAY. A path laid at its mouth must grow an arm INTO it, and
        // the walkway itself must refuse to be built on. ⚠ The pair matters: "it joins" and "it
        // is buildable" would both be satisfied by simply treating it as ordinary ground.
        // ⚠ THE MOUTH IS THE WALKWAY'S LAST ROW, the end that touches the park -- not the first
        // cell a scan happens to reach. Scanning from y=0 found the far end of the cross-corridor
        // and then tested a cell inside the skipped block, which cannot be built on at all: the
        // control read "NO ARM" about a tile that was never laid.
        var mouth = (X: -1, Y: -1);
        for (int y = f.Height - 2; y >= 0 && mouth.X < 0; y--)
            for (int x = 0; x < f.Width; x++)
                if (_paths.IsWalkway(x, y) && f.Drawn(x, y + 1)) { mouth = (x, y); break; }
        if (mouth.X >= 0)
        {
            int px2 = mouth.X, py2 = mouth.Y + 1;
            _paths.Lay(px2, py2, PathTool.Kind.Path);
            int bits2 = _paths.LinkBits(px2, py2);
            bool up = (bits2 & PathPieces.North) != 0;
            // ⚠ THE WALKWAY IS PHANTOM: master asked for "not path linkable or buildable either",
            // so the arm must NOT appear. The first version grew one and this line said so
            // approvingly -- it was testing the wrong thing, not failing to test.
            GD.Print($"[link] the walkway's mouth is ({mouth.X},{mouth.Y}); a path at ({px2},{py2}) "
                   + $"reads {bits2:X2} -- {(up ? "AN ARM INTO IT, which it must not have" : "no arm, as it must be")}"
                   + $"; laying on the walkway itself is {(_paths.CanLay(mouth.X, mouth.Y) ? "ALLOWED" : "refused, as it must be")}");
        }
        else GD.Print("[link] this park has no walkway to test against");

        GD.Print($"[link] two legs then one step back: first leg {_paths.KindAt(lx, ly + 1)} (was {mid1}), "
               + $"second leg {_paths.KindAt(lx + 2, ly + 2)} (was {far1}) -- "
               + $"{(undone && _paths.KindAt(lx, ly + 1) == PathTool.Kind.Path && _paths.KindAt(lx + 2, ly + 2) == PathTool.Kind.None ? "the second went, the first stayed, as it must be" : "WRONG")}");
        RefreshFloor();
    }

    /// <summary>⭐ A CONTROL FOR WHAT A STUB MAY LAND ON. Master: "allow overlapping that point
    /// over other paths. (but not queues)".
    ///
    /// ⚠ THREE CASES, and the first two are each other's control: the SAME shop over the SAME
    /// cell must be allowed when that cell is path and refused when it is queue, so neither answer
    /// can come from the placement simply always agreeing or always refusing. The third is the
    /// other half of the rule -- a RIDE's stub is a queue tile and a queue tile may not go on path
    /// either, or placing a ride would quietly make the one cell that is BOTH.</summary>
    void CheckStubOverlap()
    {
        int row = -1;
        ShowBuildCategory("Shops");
        for (int i = 0; i < _buildRows.Count && row < 0; i++)
        {
            var d = DefinitionFor(_lib.Rides[_buildRows[i]].Model);
            if (d?.Shape != null && Park.Footprint.From(d.Shape).EntryX >= 0) row = i;
        }
        if (row < 0) { GD.Print("[stub] nothing in Shops declares an entrance"); return; }

        var f = _park.Field;
        int sx = f.Width / 2 - 4, sy = f.Height / 2 + 8;
        ArmFromList(row);
        for (int r = 0; r < 24 && !_place.Fits(_park, sx, sy); r++) sy += 1;
        if (_place.Stubs(_park, sx, sy).FirstOrDefault() is not { Ok: true } stub)
        { GD.Print($"[stub] {_place.Display} will not fit anywhere near ({sx},{sy}) to test with"); return; }
        GD.Print($"[stub] {_place.Display} is a shop (IsRide {_place.IsRide}); its one node is "
               + $"({stub.X},{stub.Y}) and it wants a {(stub.Queue ? "QUEUE" : "path")} tile there");

        // ⭐⭐ THE SHOP, THROUGH ALL FOUR TURNS. Master: "the rotation of the entry/exit tiles isnt
        // rotating properly... its on shops mainly." A shop is small and square, so its entrance
        // has SEVERAL free sides and the side it opens onto is a tie-break -- which used to be
        // re-answered in fixed grid directions at every rotation, so the model turned and the node
        // sat still. Four turns must give four DIFFERENT offsets, going round.
        //
        // ⚠ The offsets are from the CURSOR, not absolute: the footprint's corner moves as its
        // width and height swap, and absolute cells would read as noise.
        var seen = new List<(int X, int Y)>();
        for (int q = 0; q < 4; q++)
        {
            ArmFromList(row);
            _place.Turn(q);
            var one = _place.Stubs(_park, sx, sy).FirstOrDefault(t => t.Entrance);
            seen.Add((one.X - sx, one.Y - sy));
        }
        GD.Print($"[stub] {_place.Display}'s node at turns 0/90/180/270, offset from the cursor: "
               + string.Join(" ", seen.Select(o => $"({o.X},{o.Y})"))
               + $" -- {(seen.Distinct().Count() == 4 ? "four different sides, as it must be" : "IT DOES NOT TURN")}");
        ArmFromList(row);

        _paths.BeginLeg();
        _paths.Lay(stub.X, stub.Y, PathTool.Kind.Path);
        bool overPath = _place.Fits(_park, sx, sy);
        _paths.UndoLeg();
        // ⚠ AND THE OTHER HALF OF THE SAME RULE: the same path, one cell further in, under the
        // shop's BODY rather than its node, must REFUSE. Without this pair "the node may overlap"
        // is indistinguishable from "anything may overlap".
        var body = _place.Cells(_park, sx, sy).First();
        _paths.BeginLeg();
        _paths.Lay(body.X, body.Y, PathTool.Kind.Path);
        bool bodyOverPath = _place.Fits(_park, sx, sy);
        _paths.UndoLeg();
        GD.Print($"[stub] the same shop with path under its BODY ({body.X},{body.Y}) "
               + $"{(bodyOverPath ? "FITS" : "refused")} -- "
               + $"{(bodyOverPath ? "WRONG -- a shop standing in a walkway" : "as it must be")}");
        _paths.BeginLeg();
        _paths.Lay(stub.X, stub.Y, PathTool.Kind.Queue, 99);
        bool overQueue = _place.Fits(_park, sx, sy);
        _paths.UndoLeg();
        GD.Print($"[stub] a shop's node over PATH {(overPath ? "fits" : "REFUSED")}, over QUEUE "
               + $"{(overQueue ? "FITS" : "refused")} -- "
               + $"{(overPath && !overQueue ? "path yes, queue no, as it must be" : "WRONG")}");

        // And the ride's side of it.
        ShowBuildCategory("Rides");
        int rr = -1;
        for (int i = 0; i < _buildRows.Count && rr < 0; i++)
        {
            var d = DefinitionFor(_lib.Rides[_buildRows[i]].Model);
            if (d?.Shape != null && Park.Footprint.From(d.Shape).EntryX >= 0) rr = i;
        }
        if (rr < 0) return;
        ArmFromList(rr);
        int rx = f.Width / 2 - 4, ry = f.Height / 2 + 14;
        for (int r = 0; r < 24 && !_place.Fits(_park, rx, ry); r++) ry += 1;
        if (_place.Stubs(_park, rx, ry).FirstOrDefault(t => t.Queue) is not { Ok: true } qs)
        { GD.Print("[stub] no ride queue stub to test with"); _place.Clear(); return; }
        _paths.BeginLeg();
        _paths.Lay(qs.X, qs.Y, PathTool.Kind.Path);
        bool rideOverPath = _place.Fits(_park, rx, ry);
        _paths.UndoLeg();
        GD.Print($"[stub] a RIDE's queue node over PATH {(rideOverPath ? "FITS" : "refused")}"
               + $" -- {(rideOverPath ? "WRONG -- that would make a BOTH cell nobody asked for" : "as it must be")}");
        _place.Clear();
        _ghostView?.Clear();
    }

    /// <summary>The grid cell the centre of every part whose name carries <paramref name="needle"/>
    /// lands in. ⚠ Found by NEAREST CELL CENTRE rather than by inverting the plot's transform --
    /// the plot may sit under an authored node transform and a wrong inverse is a silent one-cell
    /// miss, which is exactly the size of the thing being measured.</summary>
    (int X, int Y)? PartCell(Node3D root, string needle)
    {
        var sum = Vector3.Zero;
        int n = 0;
        foreach (var mi in Walk(root))
        {
            if (!((string)mi.Name).Contains(needle, StringComparison.OrdinalIgnoreCase)) continue;
            sum += (mi.GlobalTransform * mi.GetAabb()).GetCenter();
            n++;
        }
        if (n == 0) return null;
        var at = sum / n;
        var f = _park.Field;
        (int X, int Y)? best = null;
        float near = float.MaxValue;
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                var c = _park.CellCentre(x, y);
                float d = (c.X - at.X) * (c.X - at.X) + (c.Z - at.Z) * (c.Z - at.Z);
                if (d < near) { near = d; best = (x, y); }
            }
        return best;
    }

    static IEnumerable<MeshInstance3D> Walk(Node n)
    {
        if (n is MeshInstance3D m && m.Visible && m.Mesh != null) yield return m;
        foreach (var c in n.GetChildren()) foreach (var g in Walk(c)) yield return g;
    }

    /// <summary>The world rectangle a footprint at (cx,cy) covers: its centre and its size. ⭐ Read
    /// off the plot's own corners, so it is the same arithmetic the floor is built from rather than
    /// a second guess at where a cell is.</summary>
    (Vector3 Centre, float W, float H) FootprintRect(int cx, int cy, int w, int h)
    {
        var a = _park.CellCorner(cx, cy);
        var b = _park.CellCorner(cx + w, cy + h);
        return (new Vector3((a.X + b.X) * 0.5f, a.Y, (a.Z + b.Z) * 0.5f),
                Mathf.Abs(b.X - a.X), Mathf.Abs(b.Z - a.Z));
    }

    /// <summary>Lay a run as one undoable leg, the way a press does.</summary>
    void LayLeg(List<(int X, int Y)> run, PathTool.Kind kind, int owner)
    {
        _paths.BeginLeg();
        LayRun(run, kind, owner);
    }

    /// <summary>Lay a run the way the ghost does, so the control goes through the same wiring the
    /// tool does rather than a second one written for the test.</summary>
    void LayRun(List<(int X, int Y)> run, PathTool.Kind kind, int owner)
    {
        for (int i = 0; i < run.Count; i++)
        {
            int bits = 0;
            if (i > 0) bits |= PathTool.BitToward(run[i].X, run[i].Y, run[i - 1].X, run[i - 1].Y);
            if (i < run.Count - 1) bits |= PathTool.BitToward(run[i].X, run[i].Y, run[i + 1].X, run[i + 1].Y);
            _paths.Lay(run[i].X, run[i].Y, kind, owner, bits);
        }
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
        RefreshFloor();
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
        // ⭐ And the other half of the control: press it. A run that ends on the path just laid
        // must CLOSE the tool, so the line after this one has to say so -- and the picture has to
        // show laid path with no ghost over it.
        if (_ghostPress)
        {
            // ⚠ THE CONTROL THAT MUST NOT CLOSE, first. A run over clear ground joins nothing, so
            // the tool has to stay open -- otherwise "closes on a join" is indistinguishable from
            // "closes on every press", and the joining case below would prove nothing.
            _cursorOverride = (cx - 4, cy + 5);
            _runX = cx - 8; _runY = cy + 5;
            PressTool();
            GD.Print($"[ghost] after a run over clear ground the tool is "
                   + $"{(_toolOpen ? "open, as it must be" : "CLOSED, so it closes on any press")}");

            // ⚠ AND A START THAT MUST BE REFUSED. Find a tile the engine marks no-build and try
            // to begin a run on it: the run must not start, or "cannot start on red" is a claim
            // about code nobody exercised.
            if (!_toolOpen) OpenTool(PathTool.Kind.Path);
            int nx = -1, ny = -1;
            for (int yy = 0; yy < f.Height && ny < 0; yy++)
                for (int xx = 0; xx < f.Width; xx++)
                    if (!_paths.CanLay(xx, yy)) { nx = xx; ny = yy; break; }
            _cursorOverride = (nx, ny);
            _runX = _runY = -1;
            PressTool();
            GD.Print($"[ghost] starting on the no-build tile ({nx},{ny}) left the run "
                   + $"{(_runX < 0 ? "unstarted, as it must be" : "STARTED")}");

            if (!_toolOpen) OpenTool(PathTool.Kind.Path);
            _cursorOverride = (ex, ey);
            _runX = cx - 5; _runY = cy + 3;
            PressTool();
            GD.Print($"[ghost] after a run that joins, the tool is "
                   + $"{(_toolOpen ? "STILL OPEN" : "closed, as it must be")}");

            // ⭐ ONE TILE closes the tool too. ⚠ And the control has to show it is the LENGTH that
            // closed it, not the joining -- so this one lands on bare ground where nothing joins.
            if (!_toolOpen) OpenTool(PathTool.Kind.Path);
            _cursorOverride = (cx - 7, cy + 6);
            _runX = cx - 7; _runY = cy + 6;
            int had = _paths.Laid;
            PressTool();
            GD.Print($"[ghost] a single tile on bare ground laid {_paths.Laid - had} and left the tool "
                   + $"{(_toolOpen ? "OPEN" : "closed, as it must be")}");
        }
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

    /// <summary>⭐ Does a ride standing IN THE PARK play its animated textures?
    ///
    /// ⚠ It drives the real per-frame path rather than calling SetFrame itself -- a check that
    /// pokes the model directly would pass even if nothing in the park ever advanced it, which is
    /// the exact thing being asked about. A capture normally freezes the clock, so the shot path
    /// is set aside for the duration and put back.</summary>
    void CheckParkAnimation()
    {
        _animChecked = true;
        if (_current == null || _current.TextureChoices.Count == 0)
        { GD.Print("[anim] no model in the park with material slots to animate"); return; }
        int animated = _current.TextureChoices.Count;
        var saved = _shotPath;
        _shotPath = null;
        var seen = new List<string>();
        for (int i = 0; i < 8; i++)
        {
            _Process(4.0 / Aps.Fps);
            seen.Add(string.Join(",", _current.TextureChoices));
        }
        _shotPath = saved;
        int distinct = seen.Distinct().Count();
        // ⚠ "One set" is only a failure if the model HAS something to animate. Most models do
        // not -- 7 of JUNGLE's 113 carry a multi-choice material -- so reporting a still model as
        // a fault would cry wolf on almost every ride there is.
        bool couldMove = _current.Frames > 1 && seen.Count > 0;
        GD.Print($"[anim] {animated} slots; over 8 steps the park model showed {distinct} distinct"
               + $" choice sets {(distinct > 1 ? "-- it animates" : couldMove ? "-- nothing in this model's materials animates" : "-- this model has no clock")}"
               + $": {string.Join(" ", seen.Distinct())}");
        // ⚠ And the gate, which is the one thing the park ALWAYS has. Its clock is reported even
        // when it has nothing to animate, so "the park does not animate" can be told from "the
        // park has nothing animated in it" -- they look identical from a screenshot.
        if (_water != null && _terrain != null) GD.Print($"[water] {_water.State(_terrain.Root)}");
        GD.Print(_gate == null
            ? "[anim] no gate in this park"
            : $"[anim] the gate has {_gate.Frames} frames and {_gate.TextureChoices.Count} material slots,"
              + $" clock now {_parkTime:F1}");
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

        // ⭐ AND THE TURN MUST LAND SQUARELY. A quarter turn eased at a real refresh rate has to
        // arrive at exactly a quarter, on its own instance so the live camera is untouched. The
        // integer ease steps by an EIGHTH of what is left, so on short frames it rounds to nothing
        // and stops short -- this is the check that says so in a number instead of by eye.
        {
            // ⚠ ONE check, not one per refresh rate. It used to run at 50, 60, 144 and 240Hz and
            // print four lines; now that the camera advances in WHOLE console frames the refresh
            // rate cannot reach it, and four identical lines labelled by Hz would imply it still
            // could. The clock is what makes them identical, and that is the point.
            (int Yaw, int Last) Land(bool creep)
            {
                var sim = new GameCamera { SnapWhenStarved = creep };
                sim.Turn(1);
                int last = 0;
                for (int i = 0; i < 4000 && sim.Yaw != sim.TargetYaw; i++)
                {
                    int was = sim.Yaw;
                    sim.Step(GameCamera.FrameTick, (_, _) => 0);
                    if (sim.Yaw != was) last = Math.Abs(sim.Yaw - was);
                }
                return (sim.Yaw, last);
            }
            // ⭐ BOTH WAYS, AND THE LAST STEP TOO. Without the creep the turn must fall SHORT --
            // otherwise the creep guards nothing. And the step that ARRIVES has to be small: a
            // turn that lands by closing the whole remaining gap in one frame is the pop master
            // could see, and it would pass a test that only checked where it ended up.
            var (with, lastStep) = Land(true);
            var (without, _) = Land(false);
            // ⭐⭐ AND IT MUST NOT WOBBLE ON THE WAY. Sampling the pose right through a turn, the
            // eye's distance from the point it is looking at has to stay put: a camera whose
            // in-between frames are a straight line between two ticks CUTS THE ARC, so the eye
            // pulls in and springs out every tick. That is what master saw as jitter, and a test
            // that only checked where the turn ENDED would never have caught it.
            {
                var sim = new GameCamera();
                sim.Step(GameCamera.FrameTick, (_, _) => 0);
                sim.Turn(1);
                float lo = float.MaxValue, hi = 0f;
                for (int tick = 0; tick < 40; tick++)
                {
                    sim.Step(GameCamera.FrameTick, (_, _) => 0);
                    for (int k = 0; k < 16; k++)
                    {
                        var (e, l, _) = sim.PoseAt(k / 16f);
                        float r = (l - e).Length();
                        if (r < lo) lo = r;
                        if (r > hi) hi = r;
                    }
                }
                GD.Print($"[cam] through a turn the eye stays {lo:F3}..{hi:F3} from what it watches"
                       + $" -- spread {(hi - lo) / hi * 100:F2}%"
                       + $" {((hi - lo) / hi < 0.01f ? "-- steady" : "-- IT WOBBLES")}");
            }
            // ⭐ THE BORDER, AND A CONTROL THAT MUST HIT IT. Driving the cursor far past the plot
            // and reading it back is the whole test -- "it stops" and "it never moved" look the
            // same from inside the park, so the pair is: a push OUT must be pinned to the border,
            // and a push to the middle must land exactly where it was aimed.
            if (_game.Bounded)
            {
                int wasX = _game.CursorX, wasZ = _game.CursorZ;
                _game.CursorX = (int)((_game.MaxTileX + 500) * GameCamera.TileUnits);
                _game.CursorZ = (int)((_game.MinTileZ - 500) * GameCamera.TileUnits);
                _game.ClampCursor();
                float outX = _game.CursorX / (float)GameCamera.TileUnits;
                float outZ = _game.CursorZ / (float)GameCamera.TileUnits;
                float midX = (_game.MinTileX + _game.MaxTileX) * 0.5f;
                float midZ = (_game.MinTileZ + _game.MaxTileZ) * 0.5f;
                _game.CursorX = (int)(midX * GameCamera.TileUnits);
                _game.CursorZ = (int)(midZ * GameCamera.TileUnits);
                _game.ClampCursor();
                float inX = _game.CursorX / (float)GameCamera.TileUnits;
                GD.Print($"[cam] border x {_game.MinTileX:F0}..{_game.MaxTileX:F0} z {_game.MinTileZ:F0}..{_game.MaxTileZ:F0};"
                       + $" driven 500 out lands at {outX:F0},{outZ:F0}"
                       + $" -- {(Mathf.Abs(outX - _game.MaxTileX) < 1f && Mathf.Abs(outZ - _game.MinTileZ) < 1f ? "pinned to the border, as it must be" : "NOT PINNED")};"
                       + $" driven to the middle lands at {inX:F0}"
                       + $" -- {(Mathf.Abs(inX - midX) < 1f ? "untouched, as it must be" : "CLAMPED WHEN IT SHOULD NOT BE")}");
                _game.CursorX = wasX; _game.CursorZ = wasZ;
            }
            else GD.Print("[cam] this park has no border to test");
            GD.Print($"[cam] a quarter turn lands on {with} of {GameCamera.QuarterTurn}"
                   + $" -- {(with == GameCamera.QuarterTurn ? "square" : "SHORT")},"
                   + $" arriving by {lastStep} {(lastStep <= 1 ? "-- a creep, no pop" : "-- A JUMP")};"
                   + $" without it {without}"
                   + $" -- {(without == GameCamera.QuarterTurn ? "also square" : "short, which is the bug")}");
        }
    }

    /// <summary>Open or close the path tool. ⭐ Closing ends the run and takes the ghost off the
    /// ground: a ghost left behind reads as laid path.</summary>
    void OpenTool(PathTool.Kind kind, int owner = 0)
    {
        if (_paths == null) { GD.Print("[path] the tool is off for this park"); return; }
        _toolOpen = true;
        _shownBox = -1; _hovered = -1; _selectView?.Hide();
        _toolKind = kind;
        _toolOwner = owner;
        _runX = _runY = -1;
        _runStack.Clear();
        _legBase = _paths.LegCount;
        GD.Print($"[tool] {kind} open -- press to start a run, press again to lay it");
        // The PSX tool plays its first-click sound when it OPENS as well, because opening a queue
        // starts a run at the ride's door -- so opening and starting share a cue there too.
        _toolSfx?.Play(ToolSounds.Cue.Start);
        Status($"{kind} tool open -- click to start a run");
    }

    /// <summary>Swing the game camera onto a grid cell. ⭐ Through CellCentre, which is the plot's
    /// own cell-to-world map -- the camera takes world units and one of them is one tile.
    /// ⚠ Does nothing when the free orbit camera is driving; it is not the game's to move.</summary>
    void LookAtCell(int x, int y, (int Dx, int Dy)? facing = null)
    {
        if (_game == null || !GameCamActive || _park?.Field == null) return;
        // ⭐⭐ AIMED AT THE DOOR'S OWN DIRECTION, not turned a fixed half. Master: "or be based
        // relative to the exit / entrance direction". A half turn assumes where the camera already
        // was; the door knows which way it opens, so the camera is aimed by it.
        //
        // ⚠⚠ AND IT LOOKS BACK AT THE DOOR, not out through it. I first pointed the view ALONG the
        // way the door opens, reasoning that the player wants to see the ground the run will cover
        // -- master, looking at it: "its still opposite". Facing the OTHER way puts the camera
        // outside the door with the ride beyond it, so the thing just placed and the tile the run
        // starts on are both in front of you. Hence the negation.
        //
        // ⚠ This ride's doors both open along z, so the sign of the grid-to-world map and the sign
        // of the intent would BOTH show up as "opposite" here and cannot be told apart by it. The
        // map is not the guess: grid +x is world +X and grid +y is world -Z, because Park.Build
        // lays row y at `Origin.Y + (H - y - 0.5)` and its own comment cites the ticket booths.
        if (facing is { } f && (f.Dx != 0 || f.Dy != 0)) _game.Face(-f.Dx, f.Dy);
        var c = _park.CellCentre(x, y);
        _game.GlideTo(c.X, c.Z);
        GD.Print($"[cam] looking at cell ({x},{y}) -- world {c.X:F1},{c.Z:F1}"
               + (facing is { } g ? $", facing grid {g.Dx},{g.Dy} -> yaw {_game.TargetYaw:X3}" : ""));
    }

    /// <summary>Hand the exit's path over: open the path tool with a run already begun outside
    /// the exit, the way the console does when a queue is finished.</summary>
    void StartExitPath((int X, int Y) from)
    {
        int owner = _pathOwner;
        var facing = _pathFacing;
        _pathFrom = null; _pathOwner = 0; _pathFacing = (0, 0);
        // ⚠ BOTH TESTS. `CanLay` is the terrain's own no-build bit and knows nothing about what is
        // standing there -- an exit facing another ride handed the tool a run beginning inside it,
        // which is one of the ways the entry-exit hand-over was "a lil broken".
        if (_paths == null || !_paths.CanLay(from.X, from.Y) || !_park.Vacant(from.X, from.Y))
        {
            Status("no room for a path at the exit");
            GD.Print($"[build] no exit path: ({from.X},{from.Y}) is {_paths?.Describe(from.X, from.Y) ?? "off"}"
                   + (_park.Vacant(from.X, from.Y) ? "" : " and something is standing on it"));
            return;
        }
        // ⭐⭐ ALREADY CONNECTED IS ALREADY DONE. Master: "if the exit path is already touching
        // another path, dont open the exit path tool." The stub itself is laid with the ride, so
        // the question is whether anything ELSE beside it is path -- if so the way out already
        // joins the network and there is nothing to draw.
        foreach (var (dx, dy) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
            if (_paths.KindAt(from.X + dx, from.Y + dy) is PathTool.Kind.Path or PathTool.Kind.Both)
            {
                Status($"the exit at ({from.X},{from.Y}) already meets a path");
                GD.Print($"[build] no exit path tool: ({from.X},{from.Y}) already touches path at "
                       + $"({from.X + dx},{from.Y + dy})");
                return;
            }
        OpenTool(PathTool.Kind.Path, owner);
        _runX = from.X; _runY = from.Y;
        _runStack.Add(from);
        // ⭐⭐ AND THE CAMERA COMES ROUND. Master: "when thats done, rotate the camera around and
        // focus the camera on the exit tile." A ride's exit is on the far side from its queue, so
        // a half turn puts the player behind it rather than looking at the back of the ride they
        // just walked the queue around.
        LookAtCell(from.X, from.Y, facing);
        _ghostAt = (-1, -1, -1, -1);
        Status($"now the path out -- run it from ({from.X},{from.Y})");
        GD.Print($"[build] exit path mode from ({from.X},{from.Y})");
    }

    /// <summary>Step a queue run back one leg. Returns false when there is nothing of THIS run
    /// left to step back through, which is the caller's signal to shut the tool instead.
    ///
    /// ⭐ ONLY QUEUES. Master asked for it on the queue, and the queue is where it earns its keep:
    /// a queue is drawn exactly as laid, so a leg in the wrong place cannot be corrected by laying
    /// over it the way a path network can.
    ///
    /// ⚠ The stub the ride made is NOT a leg, so the last step back leaves the run sitting on it
    /// and the press after that closes the tool -- which is the behaviour asked for, and it falls
    /// out of the stub having been laid outside any leg rather than out of a count written here.</summary>
    bool UndoLeg()
    {
        if (!_toolOpen || _toolKind != PathTool.Kind.Queue || _paths == null) return false;
        if (_paths.LegCount <= _legBase || _runStack.Count < 2) return false;
        if (!_paths.UndoLeg()) return false;
        _runStack.RemoveAt(_runStack.Count - 1);
        (_runX, _runY) = _runStack[^1];
        _ghostAt = (-1, -1, -1, -1);
        RefreshFloor();
        _toolSfx?.Play(ToolSounds.Cue.Undo);
        GD.Print($"[tool] queue stepped back to ({_runX},{_runY}); {_paths.LegCount - _legBase} legs left");
        Status($"took back the last leg -- the run goes on from ({_runX},{_runY})");
        return true;
    }

    void CloseTool()
    {
        // ⭐ Leaving the QUEUE is what starts the exit path, whether the queue joined something or
        // was simply abandoned -- master's call, and the console's: the run finishing hands over,
        // and giving up on it should not leave the ride half-connected with no prompt.
        bool wasQueue = _toolOpen && _toolKind == PathTool.Kind.Queue;
        var handOver = wasQueue ? _pathFrom : null;
        _toolOpen = false;
        _toolOwner = 0;
        _runX = _runY = -1;
        _runStack.Clear();
        _ghostAt = (-1, -1, -1, -1);
        _ghostView?.Clear();
        Status("click to open the path tool");
        if (handOver is { } from) StartExitPath(from);
    }

    /// <summary>The ghost, every frame the tool is open: from the run's start to the cursor, or
    /// just the cursor tile when no run has been started.</summary>
    void UpdateGhost()
    {
        if (_ghost == null || _paths == null) return;
        if (!CursorCell(out int x, out int y))
        {
            _ghostView.Clear();
            _ghostAt = (-1, -1, -1, -1);
            Status($"{_toolKind} tool open -- the pointer is not over the park");
            return;
        }
        int sx = _runX < 0 ? x : _runX, sy = _runY < 0 ? y : _runY;
        // ⚠ Only when it MOVED. The ghost is rebuilt geometry, and rebuilding the same run every
        // frame is a mesh churn that buys nothing -- the run only changes when a cell boundary is
        // crossed or a run is started.
        if (_ghostAt == (sx, sy, x, y)) return;
        _ghostAt = (sx, sy, x, y);
        _ghost.Set(sx, sy, x, y, _toolKind);
        _ghostView.Show(_ghost, _park);
        Status(_runX < 0
            ? $"{_toolKind} tool open at ({x},{y}) -- click to start a run"
            : $"{_toolKind} run ({_runX},{_runY}) to ({x},{y}), {_ghost.Tiles.Count} tiles"
              + (_ghost.Layable ? " -- click to lay" : " -- BLOCKED"));
    }

    /// <summary>Wind every ride that is still building itself forward. ⚠ A model whose node has
    /// been freed -- the park rebuilding drops the lot -- must come off the list rather than be
    /// asked for a frame, because a freed wrapper answers as if it were alive right up until it
    /// throws.</summary>
    /// <summary>The sim's own copy of the ground, made once. ⚠ Null is an answer: a world whose
    /// terrain has no `heightfield` marker has no grid to walk, and the caller says so rather
    /// than running a park over nothing.</summary>
    ParkPaths WalkGrid()
    {
        if (_walkGrid != null || _terrainModel == null) return _walkGrid;
        try { _walkGrid = new ParkPaths(_terrainModel); }
        catch (Exception e) { GD.PrintErr($"[walk] no sim grid: {e.Message}"); return null; }
        // ⭐⭐ ONE ARRAY, NOT A COPY. PathTool writes its path bytes into the terrain's own field and
        // Park draws from it; pointing the sim's grid at that same array is the cheapest way of
        // keeping the two in step, because there are no longer two. Mirroring every Lay would need
        // a hook in each of the tool's mutators, and a rebuild would re-project the scenery for a
        // byte's worth of change.
        _walkGrid.Field.Cells = _terrainModel.Field.Cells;
        GD.Print($"[walk] entrance: {_walkGrid.SetEntrance(_entranceTable)}");
        return _walkGrid;
    }

    /// <summary>Start a placed ride's script. Returns false when there is nothing to run -- no
    /// `.rse`, no `.aps`, or a program that would not load -- and the caller falls back to winding
    /// the Create animation by hand, which is what every ride did before this.</summary>
    bool StartScript(int id, AssetLibrary.RideAssets assets, AnimatedModel model, Aps anim,
                     int cx, int cy, int w, int h, ParkCell? entrance = null, ParkCell? exit = null,
                     Model mesh = null)
    {
        if (assets?.Script == null || anim == null || model == null) return false;
        _sim ??= new ParkSim(WalkGrid());
        // ⭐ THE SEATS ARE THE MODEL'S 0x80 FITTINGS -- ADDHEAD indexes them by slot + 1 -- so the
        // ride is told how many it has, or it seats nobody however many the script boards.
        // ⚠ COUNTED THE LOADER'S WAY (Model.HeadSlotCount): 0x1bfdf8 walks up from id 1 and stops
        // at the first id with no 0x80 fitting, so a gap truncates the run. A plain count of the
        // 0x80 fittings agrees on every model in JUNGLE.WAD, which is exactly how a simpler rule
        // looks right without being the rule.
        int headSlots = mesh?.HeadSlotCount ?? 0;
        // ⭐ SPAWNCHILD LOOKS IN THE RIDE'S OWN FOLDER (0x1be91c builds directory + name). Seven
        // jungle rides each ship a file called EventMap.rse, so a lookup by name alone would hand
        // six of them somebody else's script.
        string dir = assets.Script.Path[..(assets.Script.Path.LastIndexOf('/') + 1)];
        byte[] Sibling(string child)
        {
            var e = _lib.Wad?.Entries.FirstOrDefault(
                x => x.Path.Equals(dir + child, StringComparison.OrdinalIgnoreCase));
            return e == null ? null : _lib.Read(e);
        }
        var ride = _sim.Add(id, _place.Display ?? Leaf(assets.Name), new ParkCell(cx, cy), w, h,
                            _lib.Read(assets.Script), anim, _place.Def?.UpgradeCapacity(0) ?? 1,
                            entrance, exit, out string fault, sibling: Sibling, headSlots: headSlots,
                            definition: _place.Def);
        if (ride == null) { GD.PrintErr($"[sim] {Leaf(assets.Name)} script would not start: {fault}"); return false; }
        RegisterStandingService(ride, model.Root, _place.Turns);
        _scripted.Add((ride, model, anim, -1, -1));
        if (mesh != null) _rideMeshes[id] = mesh;
        // ⭐⭐ AND ITS SOUNDS. The same EffectRequested the particles would use; the voice stands
        // at the ride root for node -1 (the console's own reading of a negative node: 0x1b9388
        // takes the instance's position through 0x1b9220) or at the named fitting.
        _sounds ??= MakeSounds(); _burst ??= MakeParticles();
        if (_sounds != null || _burst != null) ride.Host.EffectRequested += fx => OnRideEffect(ride, model, fx);
        // ⭐⭐ THE SCRIPT ASKS WHERE ITS NODES ARE, and the placed model answers -- the ANIMATED one,
        // LastWorld through the ride root, not the bind pose -- so WALKON's legs take the real
        // distance between the ride's own fittings instead of the 100 ms floor. Null, never a
        // zero, when a fitting does not resolve: RseMachine.WalksAreTimed is built on that
        // difference, and a zero would read as a real node at the origin. ⚠ Called from inside
        // RunSlice, so nothing is cached that the park rebuild could free -- the node is looked up
        // and checked on every call.
        if (mesh != null)
            ride.Host.NodeSource = (node, space) =>
            {
                var at = NodeWorld(id, node, (uint)space);
                return at is { } p ? (p.X, p.Y, p.Z) : null;
            };
        // ⭐ A RIDE IS BUILT CLOSED and opens once it stands. king.RSE spins on VAR_RIDECLOSED
        // right after its Create animation, so a ride left closed would build itself and then
        // stand there -- which is correct, and is also not a park.
        _sim.SetOpen(id, true);
        GD.Print($"[sim] {Leaf(assets.Name)} running its script ({ride.Variables.Count} variables), doors {entrance}/{exit}, {headSlots} seats");
        return true;
    }

    RideSounds MakeSounds()
    {
        try
        {
            string world = System.IO.Path.GetFileNameWithoutExtension(_lib.WadName ?? "").ToUpperInvariant();
            if (world.Length == 0) return null;
            // ⚠ Park 1 first, park 2 second, and the line says which served: a world's two parks
            // ship different ride maps and the game loads exactly one.
            var s = new RideSounds(this, new SoundCatalogue(_lib.Disc, world, 1), new SoundCatalogue(_lib.Disc, world, 2));
            GD.Print($"[snd] catalogue for {world}: park maps 1 and 2, positional voices reach {RideSounds.MaxDistance} units");
            return s;
        }
        catch (Exception e) { GD.PrintErr($"[snd] no sound catalogue: {e.Message}"); return null; }
    }

    RideParticles MakeParticles()
    {
        try
        {
            // ⭐ A second AssetLibrary, as the demo scene does: the first has the world open and
            // the effects live in their own WAD.
            var pw = new AssetLibrary(_discPath);
            pw.OpenWad("/DATA/PARTICLE.WAD");
            var lib = new ParticleLibrary(pw.Read(pw.Wad.Find("/Tp2.plb")));
            GD.Print($"[fx] Tp2.plb: {lib.Effects.Count} effects; bursts drawn at the script's fittings");
            return new RideParticles(this, lib);
        }
        catch (Exception e) { GD.PrintErr($"[fx] no particle library: {e.Message}"); return null; }
    }

    void OnRideEffect(ParkRide ride, AnimatedModel model, RsePreviewHost.Effect fx)
    {
        var a = fx.Arguments;
        try
        {
            switch (fx.Opcode)
            {
                case RseOpcode.EVENT or RseOpcode.ADDOBJ when a.Count >= 3 && a[0] is 1 or 2:
                {
                    // ⭐ Particles as the demo draws them: kinds 1 and 2 resolve the node in space
                    // 0x100 (0x1bbf28), and a fitting that does not resolve draws nothing.
                    if (_burst == null) return;
                    var at = NodeWorld(ride.Id, a[1], 0x100);
                    var made = at is { } p ? _burst.Emit(a[2], p) : null;
                    GD.Print($"[fx] {fx.Time / 1000.0,7:F1}s {ride.Name,-22} {fx.Opcode,-7} kind {a[0]} node {a[1],3} id {a[2],3} -> {made?.Name ?? "(no fitting or no such effect)"}"
                           + (at is { } q ? $" at ({q.X:F1},{q.Y:F1},{q.Z:F1})" : ""));
                    break;
                }
                case RseOpcode.EVENT or RseOpcode.ADDOBJ when a.Count >= 3 && SoundCatalogue.IsSoundGroup(a[0]):
                {
                    if (_sounds == null || model?.Root == null || !IsInstanceValid(model.Root)) return;
                    int node = a[1];
                    Vector3? at = node < 0 ? model.Root.GlobalPosition : NodeWorld(ride.Id, node, 0x200);
                    int tag = fx.Opcode == RseOpcode.ADDOBJ && a.Count > 3 ? a[3] : 1000;
                    _sounds.Cue(ride.Id, ride.Name, fx.Time, fx.Opcode, a[0], node, a[2], tag,
                                at ?? model.Root.GlobalPosition, fellBack: at == null && node >= 0);
                    break;
                }
                case RseOpcode.KILLOBJ when a.Count >= 1: _sounds?.Kill(ride.Id, ride.Name, a[0], fx.Time); break;
                case RseOpcode.FADEOBJ when a.Count >= 1: _sounds?.Fade(ride.Id, ride.Name, a[0], fx.Time); break;
            }
        }
        catch (Exception e) { GD.PrintErr($"[snd] {ride.Name}: {e.Message}"); }
    }

    /// <summary>`--ride-film=N`: the guest test's park, filmed from park time zero with the orbit
    /// on the ride -- the build with its crate cues, the guests walking in, boarding, the cycle --
    /// one PNG per frame at a fixed 1/F s of park time each, so frame k IS park time start + k/F
    /// and a `[snd]` cue at t lands at video time t - start. ⚠ The park is STEPPED by the film,
    /// never wound: a wound park fires every cue in one frame and no voice can advance.</summary>
    void RideFilmStart()
    {
        _filmFrame = 0; _filmStartMs = _parkTicks * ParkSim.TickMilliseconds;
        // ⚠ The help panel covered a third of every frame of the first film; a video is not a
        // debugging view.
        if (_panel != null) _panel.Visible = false;
        RideFilmCamera();
        GD.Print($"[film] ride film: {_rideFilm} frames at {_filmFps}/s = {_rideFilm / (float)_filmFps:F1}s of park time from t={_filmStartMs / 1000.0:F2}s"
               + $" | audio: a cue at park time t belongs at video time t - {_filmStartMs / 1000.0:F2}s");
    }

    void RideFilmFrame()
    {
        SaveShot(ShotSibling(_shotPath, $"-f{_filmFrame:D4}"));
        _filmFrame++;
        if (_filmFrame >= _rideFilm)
        {
            GD.Print($"[film] done: {_filmFrame} frames to park t={_parkTicks * ParkSim.TickMilliseconds / 1000.0:F2}s; riders {_seated.Count}, guests {_guests.Guests.Count}");
            if (_sounds != null) GD.Print(_sounds.Summary());
            GetTree().Quit(); return;
        }
        StepPark(1.0 / _filmFps);
        RideFilmCamera();
        if (_filmFrame % (_filmFps * 5) == 0)
        {
            var first = _scripted.FirstOrDefault();
            GD.Print($"[film] f{_filmFrame:D4} t={_parkTicks * ParkSim.TickMilliseconds / 1000.0:F2}s riders={_seated.Count} guests={_guests.Guests.Count}"
                   + (first.Ride != null ? $" {first.Ride.Name} slot {first.Ride.Slot}:{first.Ride.Variant} onride {first.Ride.Get("VAR_ONRIDE")}" : ""));
        }
    }

    /// <summary>A slow orbit round the ride, high enough to keep a six-unit ape and its riders
    /// in frame.</summary>
    void RideFilmCamera()
    {
        if (_guestTestRide is not { } r || _mouth == null || _mouth.Count == 0) return;
        var centre = GuestWorld(new Vector3(r.X + r.W * 0.5f, 0f, r.Y + r.H * 0.5f), _mouth[0]);
        float t = _filmFrame / (float)_filmFps;
        _freeCam = true; _focus = centre + new Vector3(0, 2.6f, 0); _dist = 10f; _pitch = -0.3f; _yaw = _filmYaw0 + t * 0.1f;
    }

    void SoundCensusReport()
    {
        if (_sounds == null) { GD.Print("[snd] census: no catalogue, nothing played"); return; }
        GD.Print($"[snd] census after {_parkTicks * ParkSim.TickMilliseconds / 1000.0:F1}s of park time, {_scripted.Count} scripted rides");
        GD.Print(_sounds.Summary());
    }

    /// <summary>Hand every scripted ride the frame its own script asked for.
    ///
    /// ⚠ THE RECORD ONLY CHANGES WHEN THE SLOT DOES. `UseRecord` rebuilds the model's animation
    /// tracks, so calling it every frame would rebuild a mesh sixty times a second to show the
    /// same animation; the last slot and variant are remembered for exactly that reason.</summary>
    bool _shotWound;

    /// <summary>Run the park forward to a given animation frame in one go, for a render.
    ///
    /// ⚠ IN THE SIM'S OWN TICKS, not one big delta. <see cref="ParkSim.Advance"/> deliberately
    /// caps a single call at eight ticks so a stalled frame cannot make the park sprint, which
    /// means handing it four seconds at once would quietly drop most of them.</summary>
    void WindPark(int frames)
    {
        if (frames <= 0) return;
        long target = (long)(frames * 1000f / Aps.Fps);
        int guard = 0;
        // ⭐ A LINE PER SLOT CHANGE EVEN WHILE WOUND: presenting per tick without the frame
        // keeps "-> slot 0:0" (Create) in the log before whatever the ride reached, which is the
        // proof the script played its build rather than being dropped into its cycle.
        while (_parkTicks * ParkSim.TickMilliseconds < target && guard++ < 100_000) { TickPark(); PresentScripted(frames: false); }
        PresentScripted();
        if (_guests != null) PlaceActors(1f);
        GD.Print($"[sim] wound to {_parkTicks * ParkSim.TickMilliseconds}ms for the shot ({_scripted.Count} scripted, {_guests?.Guests.Count ?? 0} walking)");
    }

    /// <summary>Hand every scripted ride the frame its own script asked for. ⚠ PRESENTATION
    /// ONLY: the sim is advanced by <see cref="TickPark"/>, on the park's one clock, so rides and
    /// guests can never be a tick apart.</summary>
    /// <summary>⭐⭐ `alpha` IS THE FRACTION OF A TICK ALREADY ELAPSED, and without it a ride's
    /// animation steps at the PARK's rate rather than the renderer's. `ride.Frame` comes from the
    /// host's clock, which only moves when <see cref="TickPark"/> runs -- 25 Hz -- so every frame
    /// drawn between two ticks showed the same pose and the ride juddered at 25 fps however fast
    /// the viewer was running. Master: "like they still last the same amount of time, but run
    /// smoothly."
    ///
    /// ⚠ THE DURATION IS UNCHANGED. This adds only the part-tick that has already passed, so the
    /// animation reaches the same frame at the same wall time; it is drawn at the positions
    /// BETWEEN the steps rather than played faster. A tick is TickMilliseconds long and the
    /// records run at Aps.Fps, so a whole tick is that many frames.
    ///
    /// ⭐ It smooths the RIDERS too, for free: SeatPose reads model.LastWorld, which SetFrame
    /// writes, so seats interpolate with the arm they are bolted to.</summary>
    void PresentScripted(bool frames = true, float alpha = 0f)
    {
        if (_sim == null) return;
        for (int i = _scripted.Count - 1; i >= 0; i--)
        {
            var (ride, model, anim, slot, variant) = _scripted[i];
            if (model?.Root == null || !GodotObject.IsInstanceValid(model.Root)) { _sounds?.Drop(ride.Id); _scripted.RemoveAt(i); continue; }
            int want = ride.Slot, wantVariant = ride.Variant;
            if (want < 0) continue;
            if (want != slot || wantVariant != variant)
            {
                var rec = anim.Records().Where(r => r.Slot == want).Skip(Math.Max(0, wantVariant)).FirstOrDefault()
                       ?? anim.Records().FirstOrDefault(r => r.Slot == want);
                if (rec != null) model.UseRecord(rec);
                // ⭐ A LINE PER CHANGE, because a shot of a ride standing still and a shot of one
                // whose script never moved look the same. This is the proof that it ran.
                GD.Print($"[sim] {ride.Name} -> slot {want}:{wantVariant}"
                       + $" ({rec?.DurationFrames ?? 0} frames)" + (ride.Fault != null ? $" FAULT {ride.Fault}" : ""));
                _scripted[i] = (ride, model, anim, want, wantVariant);
            }
            if (frames) model.SetFrame(ride.Frame + alpha * (ParkSim.TickMilliseconds * Aps.Fps / 1000f));
        }
    }

    /// <summary>⭐⭐ PEOPLE. Guests come in at the walkway's mouth -- the two kind-0x0E cells the
    /// game's own table paints -- and walk the park's laid paths, one in every couple of seconds
    /// while there is somewhere to go and room for more. Once a ride with a script stands in the
    /// park, <see cref="ParkVisitors"/> takes the crowd over: a guest picks a ride it can reach,
    /// walks to that ride's queue stub, is handed to the script (and LEAVES the walking layer --
    /// the script owns them until it hands them back at the exit), and walks off again. Until
    /// then they wander. `core/` decides all of it and this draws it, the line ParkSim holds.
    ///
    /// ⚠⚠ THE ROUTING, THE PACE AND THE DAWDLING ARE OURS -- GuestWalk's and ParkVisitors' own
    /// docs say so; the console's guest AI has not been read -- and SO IS "one every two seconds,
    /// to a random cell", a demo policy chosen so that a park with a path has people on it. None
    /// of it should be quoted as the game's behaviour.
    ///
    /// ⭐ THE BODIES ARE THE DISC'S. DATA.WAD ships twenty-four characters under /Chars; the eight
    /// kids -- Boy1a..Boy4a and Girl1a..Girl4a -- are its guests, the same meshes VisitorParkView
    /// walks Ada in, and they are drawn here in their BIND POSE: every one ships an .aps beside
    /// it, but the skeletal animation path those use is decoded and not yet executed, so a guest
    /// slides rather than walks. That is a gait still to do, not a placeholder body.
    ///
    /// ⚠ A GUEST ON A RIDE HAS NO BODY HERE. Handing over is total (ParkVisitors removes the
    /// Guest), so its actor is freed the frame it goes, and the one that comes back out at the
    /// exit is a NEW guest with a new id -- and, by id, possibly a different kid. Drawing riders
    /// on their seats is the script's WALKON/WALKOFF, still to be wired to the model.
    ///
    /// ⭐ ONE CLOCK FOR THE PARK. Rides and guests step together, 25 a second, through
    /// <see cref="_parkClock"/>; the screen lerps the actors by its Alpha, which is what makes
    /// 25 Hz look like 60.</summary>
    static readonly string[] GuestModels =
    {
        "/Chars/Girl1a/girl1a.mps", "/Chars/Boy1a/boy1a.mps", "/Chars/Girl2a/girl2a.mps", "/Chars/Boy2a/boy2a.mps",
        "/Chars/Girl3a/girl3a.mps", "/Chars/Boy3a/boy3a.mps", "/Chars/Girl4a/girl4a.mps", "/Chars/Boy4a/boy4a.mps",
    };
    GuestWalk _guests;
    /// <summary>The two halves joined, once there is a ride to join them over. Null until the
    /// first scripted ride stands; guests just wander until then.</summary>
    ParkVisitors _visitors;
    readonly ConsoleClock _parkClock = new();
    /// <summary>Whole park ticks so far, the number a capture winds to.</summary>
    int _parkTicks;
    Node3D _guestRoot;
    readonly Dictionary<int, Node3D> _actors = new();
    /// <summary>The bubbles over their heads -- see <see cref="ThoughtBubbles"/>.</summary>
    readonly ThoughtBubbles _thoughts = new();
    /// <summary>Each actor's drawn model and its sitting record, so a rider can be posed and a
    /// walker un-posed. Where says which file the pose came from, or why there is none.</summary>
    readonly Dictionary<int, (AnimatedModel Drawn, Aps.Record Sit, string Where)> _drawn = new();
    /// <summary>Each actor's WALK record -- slot 1 of the same .aps its model was built with (its
    /// own file, or Boy1a's for the boys whose records are Shared) -- with that file and model
    /// beside it so a census can measure the pose it is drawn in. Null when the file has no
    /// slot-1 record with tracks (the adults), which leaves that kid in bind pose and says so.</summary>
    readonly Dictionary<int, (Aps Anim, Aps.Record Walk, Aps.Record Idle, Model Model)> _walkRec = new();
    /// <summary>Every slot-2 record a guest could idle in, and when it last changed.</summary>
    readonly Dictionary<int, Aps.Record[]> _idles = new();
    readonly Dictionary<int, int> _idleSince = new();
    /// <summary>Which record each guest is actually playing, so a change of record restarts the
    /// clock. ⚠ `_gaitFrom` alone could not tell a walk from an idle and a guest who stopped kept
    /// the walk's frame counter, so the idle started mid-cycle at whatever the walk had reached.</summary>
    readonly Dictionary<int, Aps.Record> _gaitRec = new();
    /// <summary>Guests whose walk record is playing, with the park tick their walk began: the
    /// gait's phase is the walk's own, not a clock the guest never started.</summary>
    readonly Dictionary<int, int> _gaitFrom = new();
    /// <summary>`--walk-film=N`: instead of one frame, FOLLOW the W walker for N frames at sixty
    /// a second of park time, saving each as `<shot>-fNNNN.png` for ffmpeg -- the clip a gait
    /// needs, since no still can show one. Master: "cant see walking with a pic lol".</summary>
    int _walkFilm, _filmGuest = -1, _filmFrame, _filmCensusTick;
    Vector3 _filmCensusAt; float _filmYaw;
    /// <summary>Each actor's two ways of standing on its node: the whole kid with its feet at the
    /// origin, or its head alone hung on the origin by RiderHeadAnchor -- with the body and legs
    /// meshes to hide for the second, and the head's size for the log.</summary>
    readonly Dictionary<int, (Vector3 Feet, Vector3 HeadAt, List<MeshInstance3D> Body, string Head)> _parts = new();
    readonly HashSet<int> _headOnly = new();
    /// <summary>⚠ Unused for a head-only rider: a head has nothing to pose. Left wired for whatever
    /// shows a whole guest in a seat -- an open-topped ride, or the queue -- rather than ripped out.</summary>
    const bool PoseSeatedRiders = false;
    /// <summary>Where a rider's head hangs on its seat fitting. ADDHEAD carries no offset, so the
    /// fitting is either the head's BASE or its CENTRE. Base was measured first: the head's
    /// bottom -0.01 from the helper, its top 0.12-0.18 above the car's rim -- and master looked
    /// at that picture twice, Hot Pot and ape, and called the heads floating. So the centre
    /// reading is drawn: the head sunk to its middle, top showing, about half a head (~0.14)
    /// lower. Base stays reachable here so the two readings can be compared without an
    /// archaeology dig; the census prints both distances whichever is drawn.</summary>
    enum HeadAnchor { Base, Centre }
    const HeadAnchor RiderHeadAnchor = HeadAnchor.Centre;
    /// <summary>Who is currently held in the sitting pose.</summary>
    readonly HashSet<int> _posed = new();
    readonly Dictionary<string, Aps> _charAnims = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Where each guest was before the last tick, in cell space, for the lerp.</summary>
    readonly Dictionary<int, Vector3> _guestPrev = new();
    /// <summary>Ticks a stopped guest has left to stand before it is sent somewhere else, or,
    /// under ParkVisitors, before a guest with no way is asked to try again.</summary>
    readonly Dictionary<int, int> _guestDwell = new();
    /// <summary>DATA.WAD, open beside the park's own archive: the guests live there whatever
    /// world is up, and the park's library holds one archive at a time.</summary>
    AssetLibrary _charLib;
    readonly Dictionary<string, Model> _charModels = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (ImageTexture Tex, bool Soft)> _charTex = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The mouth cells, or null before the gate has been looked for.</summary>
    List<ParkCell> _mouth;
    bool _gateClosed;
    int _gateTimer, _gateEvery = 50, _guestCap = 12;
    /// <summary>⚠ Seeded, so the same park with the same path gets the same crowd every run --
    /// which is what lets a capture's numbers be compared with the last capture's.</summary>
    Random _guestRng = new(1);
    /// <summary>The laid cells a guest may be sent to, and the tool's count they were read at.</summary>
    List<ParkCell> _guestPool;
    int _guestPoolLaid = -1;
    long _guestPoolAt = -1;
    /// <summary>The capture's earlier stage: where everyone was, and what it was called.</summary>
    Dictionary<int, Vector3> _guestAt;
    string _guestLabel;
    /// <summary>Which of the capture's stages has run, and the frame it ran on.</summary>
    int _guestStage, _guestSince;
    /// <summary>The corner of the ride the control run put down, for aiming its camera.</summary>
    (int X, int Y, int W, int H)? _guestTestRide;

    void ResetGuests()
    {
        if (_guestRoot != null && IsInstanceValid(_guestRoot)) _guestRoot.QueueFree();
        _guestRoot = null; _guests = null; _visitors = null; _mouth = null; _gateClosed = false;
        _thoughts.Clear();
        _actors.Clear(); _drawn.Clear(); _parts.Clear(); _headOnly.Clear(); _posed.Clear(); _guestPrev.Clear(); _guestDwell.Clear();
        _walkRec.Clear(); _gaitFrom.Clear(); _gaitRec.Clear(); _idles.Clear(); _idleSince.Clear();
        _guestPool = null; _guestPoolLaid = -1; _guestPoolAt = -1; _guestAt = null; _guestLabel = null;
        _gateTimer = 0; _guestRng = new Random(1); _guestStage = 0; _guestSince = 0; _guestTestRide = null;
        _parkTicks = 0;
        _parkClock.Reset();
    }

    /// <summary>Find the gate and make the walk. False, once and for all this park, when there is
    /// no grid to walk or no entrance to come in by -- said in the log rather than retried every
    /// frame.</summary>
    bool OpenGate()
    {
        if (_guests != null) return true;
        if (_gateClosed) return false;
        var grid = WalkGrid();
        var field = _terrainModel?.Field;
        var entry = _entranceTable != null && field != null
            ? _entranceTable.Fit(field, ParkEntrance.WalkwayColumnFromPoles(_terrainModel), out _) : default;
        if (grid == null || entry.Empty)
        {
            _gateClosed = true;
            GD.Print($"[guest] no gate: {(grid == null ? "no sim grid" : "no entrance entry fits this park")} -- nobody comes in");
            return false;
        }
        _mouth = entry.Cells().Where(c => c.Kind == 0x0E).Select(c => new ParkCell(c.X, c.Z)).Where(grid.Open).ToList();
        if (_mouth.Count == 0)
        {
            _gateClosed = true;
            GD.Print("[guest] the walkway's mouth is not on open ground -- nobody comes in");
            return false;
        }
        _guests = new GuestWalk(grid);
        _guestRoot = new Node3D { Name = "guests" };
        AddChild(_guestRoot);
        GD.Print($"[guest] the gate is at {string.Join(" ", _mouth)}; guests come in once a path meets it");
        return true;
    }

    /// <summary>A rendered frame's worth of park: whole console ticks, then the picture between
    /// them.</summary>
    void StepPark(double delta)
    {
        int ticks = _parkClock.Advance(delta);
        for (int i = 0; i < ticks; i++) TickPark();
        // ⚠⚠ PRESENT BEFORE PLACING, NOT AFTER -- THIS ORDER IS THE RIDERS' ONE-FRAME LAG.
        // PlaceActors -> SeatRiders -> SeatPose reads `model.LastWorld`, and `LastWorld` is only
        // written by `model.SetFrame()` inside PresentScripted. Placing first therefore seats
        // every rider on the PREVIOUS frame's ride matrices while the ride mesh draws at the
        // current one, so the riders trail the arm they are bolted to by exactly one frame.
        //
        // ⭐ The tell was that STILLS were always right and only film showed it: WindPark, the
        // path a screenshot takes, already called PresentScripted() and then PlaceActors(). Two
        // paths through the same pair in opposite orders, and only the moving one could show the
        // difference -- which is why this survived every seat-position check we ran. Those checks
        // measured WHERE a rider was, never WHEN.
        PresentScripted(alpha: _parkClock.Alpha);
        if (_guests != null) PlaceActors(_parkClock.Alpha);
    }

    /// <summary>One console tick of everything that moves in the park.
    ///
    /// ⭐⭐ ONE GRID, ONE CLOCK. The rides' sim was made on <see cref="WalkGrid"/> and so was the
    /// walk, so <see cref="ParkVisitors"/> sees one ParkPaths from both sides -- the control run
    /// logs the reference check. And both are stepped HERE, by the same tick, never separately:
    /// under ParkVisitors through its Step, which orders the two handovers between them; before
    /// any ride stands, the walk and the sim (if any) one after the other.</summary>
    void TickPark()
    {
        _parkTicks++;
        if (_visitors == null && _sim != null && OpenGate())
        {
            _visitors = new ParkVisitors(_sim, _guests)
            {
                // ⭐ The wants, from the executable's own spawn distributions. Seeded only on
                // Arrive and reconciled against the live plans -- see VisitorNeeds.
                Needs = new VisitorNeeds(seed: 20260923),
            };
            GD.Print($"[guest] guests now visit rides; the sim and the walk share one grid: {ReferenceEquals(_sim.Paths, _guests.Paths)}");
            GD.Print($"[want] {_thoughts.Load(path => _lib?.ReadGeneric(path))}"
                   + $"; cam={System.Environment.GetEnvironmentVariable("TPW_WANT_CAM")}"
                   + $" shot={System.Environment.GetEnvironmentVariable("TPW_WANT_SHOT")}");
        }
        if (_visitors != null)
        {
            Gate();
            Snapshot();
            _visitors.Step(ConsoleClock.TickSeconds, Wander);
            Retry();
            return;
        }
        if (OpenGate()) { Gate(); Snapshot(); _guests.Step(); Dawdle(); }
        _sim?.Advance(ConsoleClock.TickSeconds);
    }

    /// <summary>The gate lets somebody in: one guest per <see cref="_gateEvery"/> ticks while
    /// there is a laid cell to go to and room for more.</summary>
    void Gate()
    {
        // ⚠ THE WHOLE POPULATION, not the walkers: a queued or riding guest is off the walk, and
        // counting only the walk let the gate admit a new guest for every one that joined a
        // queue -- ninety-three boardings and a forty-four-deep queue in three minutes.
        int population = _guests.Guests.Count
                       + (_visitors?.Plans.Values.Count(p => p.Intent == VisitorIntent.Queued) ?? 0);
        if (population >= _guestCap || ++_gateTimer < _gateEvery) return;
        var pool = GuestPool();
        if (pool.Count == 0) return;
        _gateTimer = 0;
        var at = _mouth[_guests.Guests.Count % _mouth.Count];
        var to = pool[_guestRng.Next(pool.Count)];
        var g = _visitors != null ? _visitors.Arrive(at, to) : _guests.Spawn(at, to);
        GD.Print($"[guest] #{g.Id} in at {at}, bound for {g.Destination}: {g.State}"
               + (g.Route != null ? $", {g.Route.Count - 1} cells" : $" -- {g.Reason}"));
    }

    void Snapshot()
    {
        _guestPrev.Clear();
        foreach (var g in _guests.Guests) _guestPrev[g.Id] = Cell(g.Position);
    }

    /// <summary>Without rides: whoever has stopped -- arrived, or found no way -- stands a moment
    /// and picks somewhere else.</summary>
    void Dawdle()
    {
        foreach (var g in _guests.Guests)
        {
            if (g.State == GuestState.Walking) { _guestDwell.Remove(g.Id); continue; }
            if (!_guestDwell.TryGetValue(g.Id, out int dwell))
            {
                // Just stopped: a while where it meant to stop, a moment where it could not go on.
                _guestDwell[g.Id] = g.State == GuestState.Arrived ? 25 + _guestRng.Next(50) : 25;
                if (g.State != GuestState.Arrived) GD.Print($"[guest] #{g.Id} {g.State} at {g.Cell}: {g.Reason}");
                continue;
            }
            if (dwell > 1) { _guestDwell[g.Id] = dwell - 1; continue; }
            if (!_guests.Send(g, Wander())) _guestDwell[g.Id] = 25;
            else _guestDwell.Remove(g.Id);
        }
    }

    /// <summary>Under ParkVisitors, which re-sends everyone who ARRIVES, the only guests left
    /// standing are the ones with no way: asked again a second later.
    ///
    /// ⚠ A GUEST HEADING FOR A RIDE IS RE-SENT THROUGH PARKVISITORS, never straight through the
    /// walk: its plan says which ride it is walking to, and a plan left pointing at a ride while
    /// the guest wanders off somewhere else would hand them to that ride from wherever they
    /// happened to stop.</summary>
    void Retry()
    {
        foreach (var g in _guests.Guests.ToArray())
        {
            if (g.State is GuestState.Walking or GuestState.Arrived) { _guestDwell.Remove(g.Id); continue; }
            if (!_guestDwell.TryGetValue(g.Id, out int wait))
            {
                _guestDwell[g.Id] = 25;
                GD.Print($"[guest] #{g.Id} {g.State} at {g.Cell}: {g.Reason}");
                continue;
            }
            if (wait > 1) { _guestDwell[g.Id] = wait - 1; continue; }
            _guestDwell.Remove(g.Id);
            bool sent;
            if (_visitors.Plans.TryGetValue(g.Id, out var plan) && plan.Intent == VisitorIntent.Heading)
            {
                var ride = _sim.Rides.FirstOrDefault(r => r.Id == plan.RideId);
                sent = ride != null && _visitors.SendTo(g, ride);
            }
            else sent = _guests.Send(g, Wander());
            if (!sent) _guestDwell[g.Id] = 25;
        }
    }

    /// <summary>Somewhere to walk to when there is nothing better: a laid cell at random, or the
    /// mouth when nothing is laid -- ParkVisitors asks for a cell, not for a maybe.</summary>
    ParkCell Wander()
    {
        var pool = GuestPool();
        return pool.Count > 0 ? pool[_guestRng.Next(pool.Count)] : _mouth[0];
    }

    /// <summary>Every laid cell a guest may be sent to -- open ground that is not the walkway.
    /// ⚠ Re-read when the tool's count moves and every ten seconds regardless, because an undo
    /// takes a cell away without moving the count.</summary>
    List<ParkCell> GuestPool()
    {
        int laid = _paths?.Laid ?? 0;
        if (_guestPool != null && laid == _guestPoolLaid && _guests.Time - _guestPoolAt < 10_000) return _guestPool;
        var grid = _guests.Paths;
        _guestPool = grid.Cells.Where(c => grid.Open(c) && !grid.IsEntrance(c)).ToList();
        _guestPoolLaid = laid; _guestPoolAt = _guests.Time;
        return _guestPool;
    }

    static Vector3 Cell(System.Numerics.Vector3 p) => new(p.X, p.Y, p.Z);

    /// <summary>Place a guest in the same frame that builds the playable floor. Raw
    /// ParkPaths.Origin is a data/overlay frame: it displaced HALLOW by114.5 units and SPACE
    /// by10 while JUNGLE happened to agree. Do not repair that with per-world offsets.
    /// A built authored plot owns the transform; the legacy fallback remains for contexts
    /// without a built authored plot. Vertical guest offsets and the selected cell's raised height survive.</summary>
    Vector3 GuestWorld(Vector3 p, ParkCell cell)
    {
        float y = _park.CellY(cell.X, cell.Z) + p.Y;
        if (_park.PlotSpace != null && _park.Width > 0 && _park.Height > 0)
        {
            int x = Mathf.FloorToInt(p.X), z = Mathf.FloorToInt(p.Z);
            var corner = _park.CellCorner(x, z);
            var at = corner + (p.X - x) * (_park.CellCorner(x + 1, z) - corner)
                            + (p.Z - z) * (_park.CellCorner(x, z + 1) - corner);
            return new Vector3(at.X, y, at.Z);
        }
        return new Vector3(_guests.Paths.Origin.X + p.X, y, -(_guests.Paths.Origin.Y + p.Z));
    }

    // Map direction as well as position. Keep the actor upright: applying a mirrored/scaled
    // plot basis to the body itself would mirror/scale its geometry instead of just its route.
    Vector3 GuestHeading(Vector3 grid)
    {
        if (_park.PlotSpace != null && _park.Width > 0 && _park.Height > 0)
        {
            var corner = _park.CellCorner(0, 0);
            var at = grid.X * (_park.CellCorner(1, 0) - corner)
                   + grid.Z * (_park.CellCorner(0, 1) - corner);
            return new Vector3(at.X, 0, at.Z);
        }
        return new Vector3(grid.X, 0, -grid.Z);
    }

    /// <summary>Where each seated rider was last drawn, by guest id, with the seat it sits in.</summary>
    readonly Dictionary<int, (Transform3D At, string Where, Vector3 Forward, string Part, float PartTop)> _seated = new();

    /// <summary>⭐ THE WALK PATH'S BASIS FOR A WORLD HEADING -- the one known-good facing in the
    /// viewer (nobody has ever said a walker moonwalks), so it is the reference the seat path is
    /// measured against. Heading in the viewer's frame: +X across, +Z toward the gate.</summary>
    static Basis WalkBasis(Vector3 heading) => Basis.Identity.Rotated(Vector3.Up, Mathf.Atan2(heading.X, heading.Z));

    /// <summary>A basis as a rotation: its angle, axis and determinant. ⚠ The determinant is
    /// printed rather than assumed: a mirror or a squash has no axis, and reporting one for it
    /// would be the instrument lying.</summary>
    static (float Degrees, Vector3 Axis, float Det) RotationOf(Basis r)
    {
        float det = r.Determinant();
        float trace = r.X.X + r.Y.Y + r.Z.Z;
        float angle = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp((trace - 1f) / 2f, -1f, 1f)));
        // Columns are r.X, r.Y, r.Z; r.Y.Z is row Z of column Y, i.e. M[2][1].
        var axis = new Vector3(r.Y.Z - r.Z.Y, r.Z.X - r.X.Z, r.X.Y - r.Y.X);
        if (axis.LengthSquared() > 1e-8f) axis = axis.Normalized();
        else axis = new Vector3(Mathf.Sqrt(Mathf.Max(0f, (r.X.X + 1f) / 2f)), Mathf.Sqrt(Mathf.Max(0f, (r.Y.Y + 1f) / 2f)), Mathf.Sqrt(Mathf.Max(0f, (r.Z.Z + 1f) / 2f)));
        return (angle, axis, det);
    }
    static string Describe((float Degrees, Vector3 Axis, float Det) rot)
        => Mathf.Abs(rot.Det - 1f) > 0.01f ? $"det {rot.Det:F2} -- NOT A ROTATION"
         : rot.Degrees < 0.5f ? "identity"
         : $"{rot.Degrees:F1} deg about ({rot.Axis.X:F2}, {rot.Axis.Y:F2}, {rot.Axis.Z:F2}), det {rot.Det:F2}";
    /// <summary>⭐⭐ THE ONE FLIP IN THE SEAT PATH, measured in and not guessed. A seated kid's pose is
    /// ride root x seat basis x THIS, and the census's R = WalkBasis(s)^-1 x B_seat read exactly
    /// 180 degrees about the seat's own up on every rider, at two different tilts, with the walker
    /// control identity -- master's "180 out relative to its seat". The factor here used to be the
    /// Z mirror diag(1,1,-1), which keeps the mirror count odd (the kid's own root carries one, the
    /// ride root one) but negates the kid's local Z, and local Z is the facing. diag(-1,1,1) is
    /// that same mirror composed with a half-turn about Y: still odd, so the determinant control
    /// still reads -1 on every kid, and the kid now faces its seat's forward. ⚠ A head is
    /// left-right symmetric, so which axis carries the odd mirror is invisible on it; on a whole
    /// seated kid it would swap left and right, which is the day this needs the ride's own
    /// convention read rather than inferred.</summary>
    static readonly Transform3D Mirror = new(new Basis(new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1)), Vector3.Zero);

    /// <summary>⭐⭐ RIDERS SIT WHERE ADDHEAD PUT THEM. The script takes a random free head slot
    /// and attaches the guest to fitting `slot + 1` in the `0x80` space (the lead's reading of
    /// its handler), so a seated guest is drawn on that fitting's node -- and follows it, because
    /// the node's world matrix is read every frame from the animated model. ⚠ AT THE NODE'S
    /// ORIGIN: the fitting's three floats are an offset within the part and are not understood
    /// -- read now through <see cref="Model.FittingLocal"/>, whose frame is inferred rather than
    /// walked. ⚠ A rider STANDS on its seat point in bind pose: the sitting pose, like the walking
    /// gait, is skinning work and belongs with it, not here. ⚠ The yaw comes from the Head's own
    /// Z row, which is plausible and unread -- left until a kid looks wrong rather than tuned
    /// until one looks right. ⚠ Position and yaw only, never the node's basis: the ride's root carries the
    /// Z mirror and the node its bind scale, and a kid drawn through both came out mirrored and
    /// a tenth the size.</summary>
    /// <summary>The pose a rider takes in a seat, and the seat's own forward and up in the world,
    /// from the animated model. ⭐ ONE FUNCTION for riders and for empty seats, so a census over
    /// every seat of a ride measures exactly what a rider would be drawn with.</summary>
    bool SeatPose(Model mesh, AnimatedModel model, Transform3D root, Model.Fitting fit,
                  out Transform3D pose, out Vector3 forward, out Vector3 up)
    {
        pose = Transform3D.Identity; forward = Vector3.Zero; up = Vector3.Zero;
        if (!model.LastWorld.TryGetValue(mesh.NodeOffset(fit.Node), out var w)) return false;
        // ⭐ THE SEAT IS A POINT ON THE PART (Model.FittingLocal, frame inferred), through the
        // node's world matrix and then the ride root. The node's three axis rows, each divided by
        // its own length, are its orientation with the bind scale dropped; that is the rider's
        // basis in the ride root's space, and root x that x Mirror is a proper transform
        // assigned whole -- never Euler on a mirrored basis.
        var seat = System.Numerics.Vector3.Transform(mesh.FittingLocal(fit), w);
        var seatLocal = new Vector3(seat.X, seat.Y, seat.Z);
        var ax = new Vector3(w.M11, w.M12, w.M13);
        var ay = new Vector3(w.M21, w.M22, w.M23);
        var az = new Vector3(w.M31, w.M32, w.M33);
        var seatForward = root.Basis * az; var seatUp = root.Basis * ay;
        forward = seatForward.LengthSquared() > 1e-10f ? seatForward.Normalized() : Vector3.Zero;
        up = seatUp.LengthSquared() > 1e-10f ? seatUp.Normalized() : Vector3.Zero;
        if (ax.LengthSquared() > 1e-10f && ay.LengthSquared() > 1e-10f && az.LengthSquared() > 1e-10f)
            pose = root * new Transform3D(new Basis(ax.Normalized(), ay.Normalized(), az.Normalized()), seatLocal) * Mirror;
        else
        {
            // ⚠ A degenerate axis: the yaw-only reading rather than a squashed basis.
            var f = seatForward; f.Y = 0;
            float yaw = f.LengthSquared() > 1e-6f ? Mathf.Atan2(f.X, f.Z) : 0f;
            pose = new Transform3D(Basis.Identity.Rotated(Vector3.Up, yaw), root * seatLocal);
        }
        return true;
    }

    void SeatRiders()
    {
        _seated.Clear();
        foreach (var (ride, model, _, _, _) in _scripted)
        {
            if (ride.Host == null || ride.Host.Seats.Count == 0 || model?.Root == null
                || !IsInstanceValid(model.Root) || model.LastWorld == null) continue;
            if (!_rideMeshes.TryGetValue(ride.Id, out var mesh)) continue;
            var root = model.Root.GlobalTransform;
            foreach (var (slot, guest) in ride.Host.Seats)
            {
                if (mesh.FindFitting(slot + 1, 0x80) is not { Node: >= 0 } fit) continue;
                if (!SeatPose(mesh, model, root, fit, out var pose, out var seatForward, out _)) continue;
                // ⭐ NAMED, so the log says Head09 and not "node 19" -- and says out loud when a
                // rider lands on anything that is not a Head, which would be the fitting reading
                // failing. On Crazy Ape every 0x80 fitting is a Head helper under an arm.
                string node = mesh.NodeName(fit.Node);
                // The PART the seat is in: the first mesh up the helper's parent chain (a car, a
                // crate), and its top in the world -- what a head has to clear to be seen.
                int pn = fit.Node;
                for (int guard = 0; pn >= mesh.Meshes.Count && guard < 32; guard++) pn = mesh.NodeParent(pn);
                string part = pn >= 0 && pn < mesh.Meshes.Count ? mesh.Meshes[pn].Name : "(no mesh above)";
                float partTop = float.NaN;
                if (pn >= 0 && pn < mesh.Meshes.Count)
                {
                    var (plo, phi) = Park.DrawnBounds(model.Root, inParent: true, onlyNamed: part);
                    if (phi.Y >= plo.Y) partTop = phi.Y;
                }
                _seated[guest] = (pose,
                                  $"{ride.Name} seat {slot} on {(node.Length > 0 ? node : "node " + fit.Node)}"
                                  + (node.StartsWith("Head", StringComparison.OrdinalIgnoreCase) ? "" : " -- NOT A HEAD"),
                                  seatForward, part, partTop);
            }
        }
    }

    /// <summary>A script node's place in the world, or null: the fitting by id and space, its
    /// node's world matrix from the animated model, FittingLocal through it, then the ride root.
    /// The one resolver behind NodeSource, the seats and the walkers, so all three agree.</summary>
    Vector3? NodeWorld(int rideId, int node, uint space)
    {
        if (!_rideMeshes.TryGetValue(rideId, out var mesh)) return null;
        var entry = _scripted.FirstOrDefault(e => e.Ride.Id == rideId);
        var model = entry.Model;
        if (model?.Root == null || !IsInstanceValid(model.Root) || model.LastWorld == null) return null;
        if (mesh.FindFitting(node, space) is not { Node: >= 0 } fit) return null;
        if (!model.LastWorld.TryGetValue(mesh.NodeOffset(fit.Node), out var w)) return null;
        var p = System.Numerics.Vector3.Transform(mesh.FittingLocal(fit), w);
        return model.Root.GlobalTransform * new Vector3(p.X, p.Y, p.Z);
    }

    /// <summary>Where each guest the scripts are WALKING was last drawn: between two of the
    /// ride's nodes, the script's per-mille of the way along.</summary>
    readonly Dictionary<int, (Transform3D At, string Where)> _walking = new();
    /// <summary>Legs that could not be placed, said once each rather than sixty times a second.</summary>
    readonly HashSet<(int Ride, int From, int To)> _unplacedLegs = new();

    /// <summary>⭐ A GUEST WALKING TO ITS SEAT IS DRAWN WHERE THE SCRIPT SAYS. WALKON moves a guest
    /// from a park node (space 0x800) to a ride node (0x80 for a seat), and the host records each
    /// pose as WalkerPose(guest, from, to, mode, perMille, angle); the kid is drawn at that
    /// fraction of the way between the two nodes as this frame's model places them, facing along
    /// the leg. A leg whose nodes do not resolve is not drawn and is logged once: a guest drawn
    /// at the origin would be a lie, and the vanish is the script's own shape. ⚠ Crazy Ape's
    /// script has no WALKON at all -- HUSH/ADDHEAD straight to the seat -- so on it this draws
    /// nobody, correctly; king, incagod and manic walk their riders.</summary>
    void WalkRiders()
    {
        _walking.Clear();
        foreach (var (ride, model, _, _, _) in _scripted)
        {
            if (ride.Host == null || ride.Host.Walkers.Count == 0 || ride.Machine == null) continue;
            // ⚠ THE WALKER TABLE REMEMBERS EVERY LAST POSE, so it holds guests who finished their
            // leg and stand waiting, and guests who have long since HOPped off and are walking the
            // park again. Only a guest the script still HOLDS -- on its HUSH stack, between
            // boarding and leaving -- and has not seated is a walker to draw; the first version
            // drew eighteen kids on one point at 100% of a leg, some of them also out on the paths.
            var held = ride.Machine.GuestIds;
            foreach (var (guest, w) in ride.Host.Walkers)
            {
                if (_seated.ContainsKey(guest) || !held.Contains(guest)) continue;
                var from = NodeWorld(ride.Id, w.FromNode, 0x800) ?? NodeWorld(ride.Id, w.FromNode, 0x80);
                var to = NodeWorld(ride.Id, w.ToNode, 0x80) ?? NodeWorld(ride.Id, w.ToNode, 0x800);
                if (from is not { } a || to is not { } b)
                {
                    if (_unplacedLegs.Add((ride.Id, w.FromNode, w.ToNode)))
                        GD.Print($"[guest] walker #{guest} on {ride.Name}: leg node {w.FromNode} -> {w.ToNode} does not resolve on the model, not drawn");
                    continue;
                }
                var at = a.Lerp(b, Mathf.Clamp(w.PerMille / 1000f, 0f, 1f));
                var d = b - a; d.Y = 0;
                float yaw = d.LengthSquared() > 1e-6f ? Mathf.Atan2(d.X, d.Z) : 0f;
                _walking[guest] = (new Transform3D(Basis.Identity.Rotated(Vector3.Up, yaw), at),
                                   $"{ride.Name} leg {w.FromNode} -> {w.ToNode} {w.PerMille / 10}% mode {w.Mode}");
            }
        }
    }

    readonly Dictionary<ParkRide, (Node3D Root, StandingServicePose Pose)> _standingPlaces = new();
    readonly Dictionary<int, Transform3D> _standing = new();

    void RegisterStandingService(ParkRide ride, Node3D root, int turns)
    {
        if (StandingServicePose.TryCreate(ride.Definition, ride.Origin, turns, out var pose)
            || StandingServicePose.TryCreateAtEntryStub(ride.Definition, turns, ride.Entrance, out pose))
            _standingPlaces[ride] = (root, pose);
    }

    /// <summary>Small relief facilities and authored 2x2 shops can have standing customers, not seat/WALK poses.
    /// The coordinator owns the identity; a HUSH stack is not required for outside service.
    /// Waiting guests keep the queue-stub position (no invented queue spacing);
    /// accepted guests use the authored stand point where present. For small shops with both
    /// coordinates absent, keep the actual arrival cell for the whole service: labelled port
    /// policy, not an inferred counter point or console default. Facing is presentation policy.</summary>
    void StandingRiders()
    {
        _standing.Clear();
        if (_visitors == null || _sim == null || _guests == null) return;
        foreach (var ride in _standingPlaces.Keys.ToArray())
            if (!_sim.Rides.Contains(ride) || !IsInstanceValid(_standingPlaces[ride].Root)
                || _standingPlaces[ride].Root.IsQueuedForDeletion()) _standingPlaces.Remove(ride);
        var onWalk = _guests.Guests.Select(g => g.Id).ToHashSet();
        foreach (var (id, plan) in _visitors.Plans)
        {
            var owner = _visitors.QueuedOwner(id);
            if (owner == null || !_standingPlaces.TryGetValue(owner, out var place)
                || !place.Root.IsInsideTree() || onWalk.Contains(id) || _seated.ContainsKey(id) || _walking.ContainsKey(id)) continue;
            if (owner.Host.Visibility.TryGetValue(id, out var visibility) && !visibility.Visible) continue;
            // Never substitute a standing pose for an unresolved scripted leg or seat.
            // Small outside-service fixtures have neither; larger service modes remain separate.
            if (owner.Host.Seats.Values.Contains(id) || owner.Host.Walkers.ContainsKey(id)) continue;
            bool waiting = owner.Queue.Contains(id) || owner.Get("VAR_LETMEON") == id;
            bool atArrival = waiting || place.Pose.IsEntryStubFallback;
            Vector3 cell = atArrival ? Cell(ParkPaths.Centre(plan.At))
                : new Vector3(place.Pose.CellPoint.X, 0, place.Pose.CellPoint.Y);
            var heightCell = atArrival ? plan.At : place.Pose.HeightCell;
            var forward = GuestHeading(new Vector3(place.Pose.Inward.X, 0, place.Pose.Inward.Y));
            _standing[id] = new Transform3D(WalkBasis(forward), GuestWorld(cell, heightCell));
        }
    }

    void PlaceActors(float alpha)
    {
        SeatRiders();
        WalkRiders();
        StandingRiders();
        // ⭐ Whoever has left the walk -- handed to a ride -- loses their body this frame unless a
        // seat has them. The script has them now; a kid standing in the queue AND riding would be
        // two bodies for one guest, which is exactly what the total handover exists to prevent.
        // Standing outside-service guests have an explicit full-body pose below; other
        // unseated queues still require their own presentation contract.
        var alive = new HashSet<int>(_guests.Guests.Select(g => g.Id));
        alive.UnionWith(_seated.Keys);
        alive.UnionWith(_walking.Keys);
        alive.UnionWith(_standing.Keys);
        foreach (int id in _actors.Keys.Where(id => !alive.Contains(id)).ToArray())
        {
            if (_actors[id] is { } gone && IsInstanceValid(gone)) gone.QueueFree();
            _actors.Remove(id); _guestDwell.Remove(id);
        }
        foreach (var (id, seat) in _seated)
        {
            if (!_actors.TryGetValue(id, out var rider)) rider = MakeActor(id);
            if (rider == null) continue;
            rider.Transform = seat.At;
            Show(id, headOnly: true);
            if (PoseSeatedRiders) Pose(id, sitting: true);
        }
        foreach (var (id, leg) in _walking)
        {
            if (!_actors.TryGetValue(id, out var walker)) walker = MakeActor(id);
            if (walker == null) continue;
            walker.Transform = leg.At;
            Show(id, headOnly: false);
            Pose(id, sitting: false);
            Gait(id, walking: true, alpha);
        }
        foreach (var (id, at) in _standing)
        {
            if (!_actors.TryGetValue(id, out var customer)) customer = MakeActor(id);
            if (customer == null) continue;
            customer.Transform = at;
            Show(id, headOnly: false);
            Pose(id, sitting: false);
            Gait(id, walking: false, alpha);
        }
        foreach (var g in _guests.Guests)
        {
            if (!_actors.TryGetValue(g.Id, out var actor)) actor = MakeActor(g.Id);
            if (actor == null) continue;
            Show(g.Id, headOnly: false);
            Pose(g.Id, sitting: false);
            Gait(g.Id, g.State == GuestState.Walking, alpha);
            var now = Cell(g.Position);
            var was = _guestPrev.TryGetValue(g.Id, out var p) ? p : now;
            actor.Position = GuestWorld(was.Lerp(now, alpha), g.Cell);
            // Facing the step, in the same mirrored frame. Standing guests keep their last facing.
            // ⚠ Assigned as a whole basis, not through Rotation: a kid back from a seat still carries
            // the seat's full basis, and Euler on that is the round trip this codebase already lost.
            if (g.Next is ParkCell next)
                actor.Basis = WalkBasis(GuestHeading(new Vector3(next.X - g.Cell.X, 0, next.Z - g.Cell.Z)));
            else if (actor.Basis.Determinant() < 0 || Mathf.Abs(actor.Basis.Y.Dot(Vector3.Up) - 1f) > 1e-3f)
                actor.Basis = Basis.Identity;
        }
        PlaceThoughts(); // use this frame's actual standing/walking transforms
    }

    /// <summary>A body for a guest: one of the disc's eight kids, by id, stood on its feet at the
    /// origin of its own node. Null -- logged, and not asked again -- when DATA.WAD would not
    /// give one up.</summary>
    Node3D MakeActor(int id)
    {
        string path = GuestModels[(id - 1) % GuestModels.Length];
        try
        {
            if (_charLib == null) { _charLib = new AssetLibrary(_discPath); _charLib.OpenWad("/DATA/DATA.WAD"); }
            if (!_charModels.TryGetValue(path, out var model))
            {
                var entry = _charLib.Wad.Find(path) ?? throw new InvalidDataException($"DATA.WAD has no {path}");
                model = new Model(_charLib.Read(entry));
                _charModels[path] = model;
            }
            // ⚠⚠ A NEW ACTOR IS PLAYING NOTHING, SO THE "ALREADY PLAYING" FLAGS MUST GO WITH THE
            // OLD BODY. `_gaitFrom` is what Gait() checks to avoid re-calling UseRecord every
            // frame -- but it is keyed on the GUEST, while the body it describes is this actor.
            // A guest handed to a ride and not yet seated is in none of the alive sets (see
            // PlaceActors: "a queued guest not yet seated has no body at all"), so its actor is
            // freed and then rebuilt here on the way out. The rebuilt one is constructed with a
            // NULL record; if `_gaitFrom` still held the id, Gait() concluded the walk was
            // already running, never applied it, and the guest walked home in bind pose.
            //
            // ⭐ That is master's "guests lose their animations after leaving a ride", and it is
            // the same shape as the seat-pose lag: state cached against one lifetime, read during
            // another. Clear it where the lifetime actually starts.
            _gaitFrom.Remove(id); _gaitRec.Remove(id); _idleSince.Remove(id); _posed.Remove(id); _headOnly.Remove(id);
            var (aps, sit, where) = SittingRecord(path);
            var drawn = new AnimatedModel(model, aps, null, m => CharTexture(path, m));
            drawn.SetFrame(0);
            _drawn[id] = (drawn, sit, where);
            // ⭐ THE WALK is slot 1 of the SAME file the model was built against: measured on every
            // kid's tracks, slot 1 record 0 (16 frames) is the walk cycle -- both feet travel ~6,300
            // vertex units along the forward axis in anti-phase with an 800-unit pelvis bob -- and
            // slot 2's six records are idles. A record must be read from the file that holds its
            // tracks, which SittingRecord already chose (own, or Boy1a's for the Shared boys).
            var walk = aps?.Records().FirstOrDefault(r => r.Slot == 1 && r.Skeletal && !r.Shared);
            // ⭐⭐ AND THE IDLE, which this comment has described correctly for weeks while the
            // code left a standing guest in BIND POSE -- arms out, dead still. Master, playtesting:
            // "we're also missing the idle/wait animations for visitors".
            //
            // ⭐ SIX VARIANTS, and spreading the crowd across them is the whole point of there
            // being six: every guest on variant 0 is a chorus line. Chosen by id so a guest keeps
            // the same idle across frames and a render is reproducible.
            //
            // ⚠⚠ WHICH variant the console would pick is NOT READ. The guest carries a 5-bit
            // state in `guest[0x38] & 0x1f` -- censused at 4, 11, 12, 13 and 14, with 13 set at
            // spawn (`FUN_00211A00`) and on every facility exit (`FUN_0020EDD8`), and 11 set by
            // the walking path (`FUN_0020D628`) -- but those values run past the end of the
            // slot table, so the field is a STATE and something maps state to record. That table
            // has not been found, so this picks a variant rather than claiming to know one.
            var idles = aps?.Records().Where(r => r.Slot == 2 && r.Skeletal && !r.Shared).ToArray()
                        ?? Array.Empty<Aps.Record>();
            var idle = idles.Length == 0 ? null : idles[Math.Abs(id) % idles.Length];
            _idles[id] = idles;
            _walkRec[id] = (aps, walk, idle, model);
            GD.Print($"[guest] #{id} walk: " + (walk == null ? $"no slot-1 record with tracks in {where.Split(" from ").Last()} -- bind pose when walking" : $"slot 1 v0, {walk.DurationFrames} frames, {where.Split(" from ").Last()}")
                   + "; idle: " + (idle == null ? "no slot-2 record with tracks -- bind pose when standing" : $"slot 2 v{Array.IndexOf(idles, idle)} of {idles.Length}, {idle.DurationFrames} frames"));
            var actor = new Node3D { Name = $"Guest_{id}" };
            actor.AddChild(drawn.Root);
            // Feet on the node's origin and centred on it, measured the way VisitorParkView does.
            var (lo, hi) = Park.DrawnBounds(drawn.Root, inParent: true);
            var feet = new Vector3(-(lo.X + hi.X) / 2, -lo.Y, -(lo.Z + hi.Z) / 2);
            drawn.Root.Position = feet;
            // ⭐⭐ A RIDER IS A HEAD. Every kid is three meshes -- girl1head, girl1body, girl1legs --
            // and ADDHEAD means what it says: the seats are Head fittings, the opcode adds the
            // head and DELHEAD takes it away, and a rider in a TPW car is a head above the rim,
            // not a whole child in it. The seat spacing said so first: seats 0.20 apart against a
            // 0.31-wide body. So the body and legs meshes are kept aside to hide while seated,
            // and the head's base is measured so it can sit on the seat node.
            var body = new List<MeshInstance3D>(); string headName = null;
            void Walk(Node n)
            {
                if (n is MeshInstance3D mi)
                {
                    if (((string)mi.Name).Contains("head", StringComparison.OrdinalIgnoreCase)) headName ??= ((string)mi.Name).Split('#')[0];
                    else body.Add(mi);
                }
                foreach (var c in n.GetChildren()) Walk(c);
            }
            Walk(drawn.Root);
            var headAt = feet;
            if (headName != null)
            {
                var (hlo, hhi) = Park.DrawnBounds(drawn.Root, inParent: true, onlyNamed: headName);
                // The head's bounds are measured with the feet offset applied, so the anchor comes off
                // it: the head's base, or its centre half a head lower -- RiderHeadAnchor says which.
                float hang = RiderHeadAnchor == HeadAnchor.Centre ? (hlo.Y + hhi.Y) / 2 : hlo.Y;
                headAt = feet - new Vector3((hlo.X + hhi.X) / 2, hang, (hlo.Z + hhi.Z) / 2);
                GD.Print($"[guest] #{id} wears {Leaf(path)}, {hi.Y - lo.Y:F2} tall, {hi.X - lo.X:F2} wide; "
                       + $"head {headName} {hhi.Y - hlo.Y:F2} tall x {hhi.X - hlo.X:F2} wide, base {hlo.Y:F2} above the feet, hung by its {RiderHeadAnchor}");
            }
            else GD.Print($"[guest] #{id} wears {Leaf(path)}, {hi.Y - lo.Y:F2} tall, {hi.X - lo.X:F2} wide -- NO HEAD MESH FOUND, a rider will show whole");
            _parts[id] = (feet, headAt, body, headName ?? "(none)");
            _guestRoot.AddChild(actor);
            _actors[id] = actor;
            return actor;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[guest] #{id} has no body: {e.Message}");
            _actors[id] = null;
            return null;
        }
    }

    /// <summary>⭐ THE SITTING POSE IS SLOT 3, `Load`, MEASURED OFF THE KNEES (findings, 62fbf90):
    /// of a kid's sixteen skeletal records, fourteen hang the leg down at about 46 degrees from
    /// vertical and exactly two -- both variants of slot 3 -- raise the thigh past horizontal
    /// (140 and 143 degrees), which is what sitting is. The slot's ride-table name agrees and is
    /// only a bonus; the angles are the evidence.
    ///
    /// ⚠⚠ ONE INFERENCE ON TOP OF ANOTHER: those angles rest on the skinning agent's reading that
    /// a skeletal track's node is `meshCount + bone`, under which they are sitting and under the
    /// old `skin.py` reading they would be noise. Both have controls under them; neither is a
    /// consumer walked in the executable.
    ///
    /// ⚠ A SHARED RECORD HAS NO TRACKS, AND THE FLAG DECIDES, PER CHARACTER, PER RECORD. Checked
    /// on the disc: every girl's slot 3 carries its own tracks (girl1a 25, girl2a 22, girl3a 28,
    /// girl4a 24 -- flags 0x25, and NOT the same skeleton as each other, so one girl's record on
    /// another's rig would drive the wrong bones); only boy2a/3a/4a are flagged Shared (0xa5)
    /// and reuse Boy1a's. So a kid is built against its own file whenever its record has tracks,
    /// against the family's *1a file only when the flag says Shared or the record is absent, and
    /// with no usable record at all it stays in bind pose and says so. The log prints which file
    /// posed whom; "own record Shared, no tracks" must never print for a girl.</summary>
    (Aps Anim, Aps.Record Sit, string Where) SittingRecord(string modelPath)
    {
        Aps Load(string mps)
        {
            string apsPath = System.IO.Path.ChangeExtension(mps, ".aps");
            if (_charAnims.TryGetValue(apsPath, out var cached)) return cached;
            Aps aps = null;
            try
            {
                var entry = _charLib.Wad.Find(apsPath);
                if (entry != null) aps = new Aps(_charLib.Read(entry));
            }
            catch (Exception e) { GD.PrintErr($"[guest] {apsPath} would not load: {e.Message}"); }
            _charAnims[apsPath] = aps;
            return aps;
        }
        static Aps.Record Slot3(Aps aps) => aps?.Records().FirstOrDefault(r => r.Slot == 3);
        static bool Shared(Aps.Record r) => r != null && (r.Flags & 0x80) != 0;
        var own = Load(modelPath);
        var rec = Slot3(own);
        if (rec != null && !Shared(rec)) return (own, rec, $"Load v0 from {Leaf(modelPath)}'s own file");
        // Shared or absent: the family's first file carries the tracks.
        // ⚠ "${1}1a", braced: "$11a" reads as group ELEVEN and replaces nothing, which left every
        // boy but Boy1a "Shared and no sibling tracks" in the first render.
        string family = System.Text.RegularExpressions.Regex.Replace(modelPath, @"(Boy|Girl)\d[a-z]", "${1}1a", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!family.Equals(modelPath, StringComparison.OrdinalIgnoreCase))
        {
            var sibling = Load(family);
            var sibRec = Slot3(sibling);
            if (sibRec != null && !Shared(sibRec)) return (sibling, sibRec, $"Load v0 from {Leaf(family)} (own record {(rec == null ? "absent" : "Shared, no tracks")})");
        }
        return (own, null, rec == null ? "no slot 3 record -- bind pose" : "slot 3 Shared and no sibling tracks -- bind pose");
    }

    /// <summary>⭐⭐ THE GAIT. A walking guest plays its walk record; a standing one goes back to
    /// bind; a seated one is the sitting pose's and is left alone. The frame is the park's own
    /// clock -- 25 ticks a second against the animation's 30 -- counted from the tick this guest
    /// started walking, with the screen's alpha carrying it between ticks; the record loops at
    /// its declared length because the sampler holds its last pose past the last key.
    ///
    /// ⚠ PREDICTION, NOT A TUNING: the walk's stride is 6,334 vertex units = 0.164 model units per
    /// swing, about 0.31 units/s in place, while GuestWalk moves 1.0 cell/s (our policy, unread
    /// from the console), so the feet should visibly slide about 3x on a video. If they do, the
    /// pace and the stride disagree and one of them is ours to question -- not the skinning.</summary>
    void Gait(int id, bool walking, float alpha)
    {
        if (!_drawn.TryGetValue(id, out var d) || d.Drawn?.Root == null || !IsInstanceValid(d.Drawn.Root)) return;
        if (!_walkRec.TryGetValue(id, out var w) || _posed.Contains(id)) return;
        // ⭐ ONE RECORD PER STATE, and standing is a state with a record of its own now rather
        // than the absence of one.
        if (!walking) Fidget(id, ref w);
        var want = walking ? w.Walk : w.Idle;
        if (want == null)
        {
            // ⚠ Still the honest fallback: a rig whose file has no record with tracks for this
            // state goes to bind and the load line above said which rig and which slot.
            if (_gaitRec.Remove(id)) { _gaitFrom.Remove(id); d.Drawn.UseRecord(null); d.Drawn.SetFrame(0); }
            return;
        }
        if (!_gaitRec.TryGetValue(id, out var playing) || playing != want)
        {
            try { d.Drawn.UseRecord(want); }
            catch (Exception e)
            {
                GD.PrintErr($"[guest] #{id} would not take its {(walking ? "walk" : "idle")} record: {e.Message}");
                // ⚠ Drop only the record that failed, not both: a rig with a bad idle should
                // still walk. The old line nulled the walk whatever had gone wrong.
                _walkRec[id] = walking ? (w.Anim, null, w.Idle, w.Model) : (w.Anim, w.Walk, null, w.Model);
                return;
            }
            _gaitRec[id] = want; _gaitFrom[id] = _parkTicks;
        }
        d.Drawn.SetFrame(GaitFrame(id, alpha));
    }

    /// <summary>⭐⭐ A STANDING GUEST CHANGES ITS MIND. `FUN_002106E8` is the console's own idle
    /// picker and it does not hold one pose:
    ///
    /// <code>
    ///   if (guest[0x2c] + 0x78 &lt; now) { if (state == 0xb) goto pick; }   // 120 ticks
    ///   else if (state == 0xb) return;                                    // still waiting
    ///   if (rand(100) &gt; 9) return;                                       // 1 in 10 otherwise
    /// pick:
    ///   state = DAT_002EEC18[rand(4) * 4] &amp; 0x1f;
    /// </code>
    ///
    /// ⭐ **FOUR** idle states, and the table really is four long: `FUN_001448E0(4)` bounds it in
    /// the CODE, and the data agrees -- entries 0..3 are `14, 5, 6, 13` and entry 4 is
    /// `0x3F800000`, a float 1.0, plainly something else. ⚠ The code's bound is the proof; the
    /// change of character in the data is only corroboration, because adjacency never bounds a
    /// table (this repo has been wrong that way before).
    ///
    /// ⭐ **120 ticks** is read, and at the park's 25 Hz that is 4.8 seconds.
    ///
    /// ⚠⚠ WHAT IS PORTED AND WHAT IS NOT. The COUNT (four) and the INTERVAL (120 ticks) are the
    /// console's. The 1-in-10 re-roll is deliberately NOT ported: it fires per call of a function
    /// whose cadence has not been read, and a 10% roll on the wrong clock is a guest flickering
    /// between poses every few frames -- an invented cadence is the one thing that could make
    /// this look worse than the frozen pose it replaces.
    ///
    /// ⚠ And WHICH picture each state means is still unread -- `guest[0x38] &amp; 0x1f` holds a
    /// STATE whose values (4, 5, 6, 11, 12, 13, 14) run past the end of the slot table -- so this
    /// cycles the first four of the six slot-2 variants rather than claiming a mapping.</summary>
    const int IdleStates = 4, IdleTicks = 120;

    void Fidget(int id, ref (Aps Anim, Aps.Record Walk, Aps.Record Idle, Model Model) w)
    {
        if (!_idles.TryGetValue(id, out var choices) || choices.Length < 2) return;
        if (_idleSince.TryGetValue(id, out int since) && _parkTicks - since < IdleTicks) return;
        _idleSince[id] = _parkTicks;
        // Deterministic per guest and per interval, so a film re-runs identically.
        int pick = Math.Abs(id * 31 + _parkTicks / IdleTicks) % Math.Min(IdleStates, choices.Length);
        w = (w.Anim, w.Walk, choices[pick], w.Model);
        _walkRec[id] = w;
    }

    float GaitFrame(int id, float alpha)
    {
        if (!_gaitFrom.TryGetValue(id, out int from) || !_gaitRec.TryGetValue(id, out var rec) || rec == null) return 0f;
        float frame = (_parkTicks - from + alpha) * Aps.Fps / (1000f / ParkSim.TickMilliseconds);
        // ⚠ The PLAYING record's length, not the walk's: an idle of a different duration looped
        // at the walk's length would either cut short or hold its last pose.
        return frame % Math.Max(1, rec.DurationFrames);
    }

    /// <summary>What a guest's gait is doing right now, as text for a census: the record and
    /// frame it is drawn at, and the two feet's separation MEASURED off that pose -- the foot
    /// bones through the game's own matrices and the mesh's world -- in model units.</summary>
    string GaitCensus(int id)
    {
        if (!_walkRec.TryGetValue(id, out var w) || !_gaitRec.TryGetValue(id, out var rec) || rec == null
            || !_gaitFrom.ContainsKey(id)) return "gait: bind pose (no record playing)";
        float frame = GaitFrame(id, 1f);
        string text = $"gait: slot {rec.Slot} frame {frame:F1} of {rec.DurationFrames}, playing since tick {_gaitFrom[id]}";
        var tracks = w.Anim.SkeletalTracks(rec);
        if (tracks == null || w.Model.Meshes.Count == 0) return text;
        var pose = SkeletalPose.At(tracks, frame, w.Model.HelperCount);
        int lf = -1, rf = -1;
        for (int h = 0; h < w.Model.HelperCount; h++)
        {
            string n = w.Model.NodeName(w.Model.SkinBoneNode(h)) ?? "";
            if (n.EndsWith("L Foot", StringComparison.OrdinalIgnoreCase)) lf = h;
            if (n.EndsWith("R Foot", StringComparison.OrdinalIgnoreCase)) rf = h;
        }
        if (lf < 0 || rf < 0) return text;
        var mw = w.Model.WorldTransforms()[w.Model.Meshes[0].Offset];
        var l = System.Numerics.Vector3.Transform(pose[lf].Translation, mw);
        var r = System.Numerics.Vector3.Transform(pose[rf].Translation, mw);
        return text + $"; feet apart {(l - r).Length():F3} model units (bind: together)";
    }

    /// <summary>⭐⭐ ONE FRAME OF THE WALKER FILM. Save what was drawn, step the park a sixtieth of a
    /// second, re-aim the side-on orbit at where the followed guest now stands, and every fifteen
    /// frames say where it is, how fast its body moved and what its feet are doing -- the numbers
    /// the clip is FOR. ⚠ The prediction this instrument tests, stated before the film was shot:
    /// the walk's stride is 0.164 model units per swing (a 16-frame cycle at 30 fps, 0.53 s), about
    /// 0.31 units/s in place, while GuestWalk carries the body at 1.0 cell/s -- OUR pace, unread
    /// from the console -- so the planted foot should slide backward about 3x faster than a
    /// matching pace would plant it. If it does, the pace or the stride is ours to question, not
    /// the skinning. The film ends at the asked count, or early if the walker is handed to a ride
    /// and loses its body, and says which.</summary>
    void FilmFrame()
    {
        SaveShot(ShotSibling(_shotPath, $"-f{_filmFrame:D4}"));
        _filmFrame++;
        bool have = _actors.TryGetValue(_filmGuest, out var actor) && actor != null && IsInstanceValid(actor);
        if (_filmFrame >= _walkFilm || !have)
        {
            GD.Print($"[guest] film: {_filmFrame} frames = {_filmFrame / 60f:F2}s of park time following #{_filmGuest}"
                   + (have ? "" : " -- walker lost its body (handed to a ride?), film ended early"));
            GetTree().Quit(); return;
        }
        StepPark(1.0 / 60.0);
        var g = _guests.Guests.FirstOrDefault(x => x.Id == _filmGuest);
        var at = actor.Position;
        if (g?.Next is ParkCell next)
        {
            var heading = new Vector3(next.X - g.Cell.X, 0, g.Cell.Z - next.Z).Normalized();
            var side = new Vector3(heading.Z, 0, -heading.X);
            _filmYaw = Mathf.Atan2(side.X, side.Z);
        }
        _freeCam = true; _focus = at + new Vector3(0, 0.3f, 0); _dist = 3f; _pitch = -0.35f; _yaw = _filmYaw;
        if (_filmFrame % 15 == 0 && g != null)
        {
            float dt = (_parkTicks - _filmCensusTick) * ParkSim.TickMilliseconds / 1000f;
            float moved = (at - _filmCensusAt).Length();
            GD.Print($"[guest] film f{_filmFrame:D4} t={_parkTicks * ParkSim.TickMilliseconds / 1000.0:F2}s #{g.Id} {g.State} at {g.Cell}"
                   + (g.Next is ParkCell n2 ? $" -> {n2} {g.Fraction:F2}" : "")
                   + $" world ({at.X:F2}, {at.Y:F2}, {at.Z:F2}); body moved {moved:F2} units in {dt:F2}s"
                   + (dt > 0 ? $" = {moved / dt:F2} units/s" : "") + $"; {GaitCensus(g.Id)}");
            _filmCensusAt = at; _filmCensusTick = _parkTicks;
        }
    }

    /// <summary>⭐⭐ A WALKER MID-STEP, BY CONDITION AND NOT BY CLOCK. Every picture so far is people
    /// standing, queueing or riding; a park that teleported guests between poses would pass all
    /// of them. So after stage A the park is wound one tick at a time until some guest is Walking
    /// with 0.3-0.7 of a cell behind it -- off the cell centre, where a teleport could not put it
    /// -- and at least 0.5 cells clear of every other guest, so it is not the queue conga line
    /// drawn on top of itself. The free orbit then looks at that kid from its side, three units
    /// out, and the census says what to expect BEFORE the file is opened: with the walk playing,
    /// feet about 0.16 model units apart at gait frames 0 and 8 and crossing at 4 and 12, one arm
    /// forward and one back; without it, feet together and arms down -- a slide. The feet are
    /// also MEASURED off the pose it is drawn in, so the picture has a number to be checked
    /// against rather than a squint. False when no such guest turns up in the allowance, and the
    /// log says so rather than photographing whatever stood there.</summary>
    bool WalkerCloseUp(int maxTicks)
    {
        if (_guests == null) return false;
        int start = _parkTicks;
        while (_parkTicks < start + maxTicks)
        {
            Guest pick = null; float bestClear = 0;
            // ⚠ NOT THE FIRST KID THROUGH THE GATE: the first frame taken fired on walker #1 still
            // inside the gateway, and the orbit sat inside a gate post -- beams and sky. And with
            // nobody else in the park the clearance clause compared against no one and printed
            // float.MaxValue as if it had passed. So: at least one other guest to be clear OF, and
            // the walker three cells out from the mouth, on the corridor with nothing in the way.
            if (_guests.Guests.Count < 2) { TickPark(); PresentScripted(frames: false); continue; }
            foreach (var g in _guests.Guests)
            {
                if (g.State != GuestState.Walking || g.Next is not ParkCell || g.Fraction < 0.3f || g.Fraction > 0.7f) continue;
                if (_mouth != null && _mouth.Any(m => Math.Abs(m.X - g.Cell.X) + Math.Abs(m.Z - g.Cell.Z) < 3)) continue;
                var me = Cell(g.Position); float clear = float.MaxValue;
                foreach (var o in _guests.Guests) if (o.Id != g.Id) clear = Mathf.Min(clear, (Cell(o.Position) - me).Length());
                if (clear >= 0.5f && clear > bestClear) { bestClear = clear; pick = g; }
            }
            if (pick != null)
            {
                PresentScripted(); PlaceActors(1f);   // present first: see StepPark
                var g = pick; var next = g.Next.Value;
                var at = GuestWorld(Cell(g.Position), g.Cell);
                var heading = GuestHeading(new Vector3(next.X - g.Cell.X, 0, next.Z - g.Cell.Z)).Normalized();    // the same frame as the actual walking actor
                var side = new Vector3(heading.Z, 0, -heading.X);
                _freeCam = true; _focus = at + new Vector3(0, 0.3f, 0); _dist = 3f; _pitch = -0.35f; _yaw = Mathf.Atan2(side.X, side.Z);
                double t = _parkTicks * ParkSim.TickMilliseconds / 1000.0;
                _filmGuest = g.Id; _filmYaw = _yaw; _filmCensusAt = at; _filmCensusTick = _parkTicks; _filmFrame = 0;
                GD.Print($"[guest] W t={t:F2}s walker #{g.Id} at {g.Cell} -> {next} fraction {g.Fraction:F2}, heading ({heading.X:F0}, {heading.Z:F0}), "
                       + (bestClear < 1e6f ? $"nearest other guest {bestClear:F2} cells" : "no other guest to compare against")
                       + $"; {_guests.Guests.Count} guests in the park; world ({at.X:F2}, {at.Y:F2}, {at.Z:F2}); {GaitCensus(g.Id)}");
                GD.Print($"[guest] W camera: free orbit on ({_focus.X:F2}, {_focus.Y:F2}, {_focus.Z:F2}), {_dist:F0} out, {Mathf.RadToDeg(-_pitch):F0} degrees down, side-on"
                       + " | expected: legs scissored ~0.16 units at gait frames 0/8 and crossing at 4/12, one arm forward -- or feet together and arms down if the gait is not playing");
                return true;
            }
            TickPark(); PresentScripted(frames: false);
        }
        GD.Print($"[guest] W: no guest was Walking mid-cell and 0.5 cells clear of the crowd in {maxTicks} ticks after A -- no walker shot taken");
        return false;
    }

    /// <summary>Show the whole kid, or its head alone with the head's base on the node.
    /// ⚠ WHERE THE HEAD SITS ON THE NODE IS INFERRED: "a head above the rim" puts its base at the
    /// fitting; the console may hang it from its centre or its neck, and that is a picture away.</summary>
    void Show(int id, bool headOnly)
    {
        if (!_parts.TryGetValue(id, out var parts) || !_drawn.TryGetValue(id, out var d) || d.Drawn?.Root == null || !IsInstanceValid(d.Drawn.Root)) return;
        if (headOnly == _headOnly.Contains(id)) return;
        bool canHead = headOnly && parts.Head != "(none)";
        foreach (var mi in parts.Body) if (IsInstanceValid(mi)) mi.Visible = !canHead;
        d.Drawn.Root.Position = canHead ? parts.HeadAt : parts.Feet;
        if (canHead) _headOnly.Add(id); else _headOnly.Remove(id);
    }

    /// <summary>Hold a rider in its sitting record, or put a walker back in bind pose. Frame 0
    /// of the record, held: the lead measured the pose there, and a rider does not act out a
    /// boarding while the ride runs.</summary>
    void Pose(int id, bool sitting)
    {
        if (!_drawn.TryGetValue(id, out var d) || d.Drawn?.Root == null || !IsInstanceValid(d.Drawn.Root)) return;
        if (sitting == _posed.Contains(id)) return;
        try
        {
            d.Drawn.UseRecord(sitting ? d.Sit : null);
            d.Drawn.SetFrame(0);
        }
        catch (Exception e)
        {
            // ⚠ INTO THE CENSUS, NOT ONLY STDERR, and not retried every frame: a record the skinning
            // path will not take (girl1a's and girl4a's Load v0 throw "Index was out of range" in
            // UseRecord where girl2a's and boy1a's do not) is that kid's bind pose with the reason
            // beside it -- the skinning path's question, said where the seat lines are read.
            GD.PrintErr($"[guest] #{id} would not take its {(sitting ? "sitting" : "bind")} pose: {e.Message}");
            if (sitting) _drawn[id] = (d.Drawn, null, $"{d.Where} -- UseRecord threw: {e.Message}");
            return;
        }
        if (sitting && d.Sit != null) _posed.Add(id); else _posed.Remove(id);
    }

    /// <summary>TextureNear for a character: the same decode, against DATA.WAD, in its own cache
    /// -- the park's cache is keyed on the park's archive and a character is not in it.</summary>
    (ImageTexture Tex, bool Soft) CharTexture(string ownerPath, string material)
    {
        if (material == null) return (null, false);
        var key = ownerPath + "|" + material;
        if (_charTex.TryGetValue(key, out var t)) return t;
        (ImageTexture, bool) made = (null, false);
        try
        {
            var texture = _charLib.TextureNear(ownerPath, material);
            if (texture != null)
            {
                var img = Image.CreateFromData(texture.Width, texture.Height, false, Image.Format.Rgba8, texture.Pixels);
                img.GenerateMipmaps();
                made = (ImageTexture.CreateFromImage(img), texture.Translucent);
            }
            else GD.PrintErr($"[guest] UNRESOLVED {ownerPath} '{material}'");
        }
        catch (Exception ex) { GD.PrintErr($"[guest] texture threw: {ex.Message}"); }
        _charTex[key] = made;
        return made;
    }

    /// <summary>⭐ A CONTROL RUN, not a feature. Lays the GuestAudit's network in from the gate --
    /// two columns straight in from the mouth and a bar across their end -- through the real
    /// tool, the way a press would; stands a ride that BOARDS beside it, through the real
    /// placement, so its stubs go down with it and its script starts; and lets guests in twenty
    /// ticks apart so they are spread along the corridor rather than stacked on one cell. The
    /// capture then photographs it twice (see the shot branch in _Process) and logs, at both
    /// times, how many queued, boarded and came back out: people standing in one picture prove
    /// nothing about walking, and walking proves nothing about riding.</summary>
    void GuestTest()
    {
        var f = _park.Field;
        if (f == null || _entranceTable == null || _paths == null) { GD.Print("[guest] no grid, no entrance table or no tool -- nothing to test"); return; }
        var e = _entranceTable.Fit(f, ParkEntrance.WalkwayColumnFromPoles(_terrainModel), out _);
        if (e.Empty) { GD.Print("[guest] no entrance entry fits this park -- nothing to test"); return; }
        int xl = e.XCol, xr = e.XCol + 1, z0 = e.ZEnd;
        var left = new List<(int X, int Y)>(); var right = new List<(int X, int Y)>(); var bar = new List<(int X, int Y)>();
        for (int z = z0; z < z0 + 8; z++) { left.Add((xl, z)); right.Add((xr, z)); }
        for (int x = xl - 6; x <= xr + 6; x++) bar.Add((x, z0 + 7));
        int before = _paths.Laid;
        LayLeg(left, PathTool.Kind.Path, 0); LayLeg(right, PathTool.Kind.Path, 0); LayLeg(bar, PathTool.Kind.Path, 0);
        RefreshFloor();
        // ⚠ Distinct cells, not run lengths: the bar shares its two middle cells with the columns'
        // last row, and the tool counts a cell once however many runs cross it.
        int cells = left.Concat(right).Concat(bar).Distinct().Count();
        GD.Print($"[guest] laid {_paths.Laid - before} of {cells} path cells in from the mouth ({xl},{z0 - 1}) ({xr},{z0 - 1})");
        // ⚠ MORE GUESTS THAN THE RIDE CAN SWALLOW, on purpose: with a cap the queue could hold,
        // everyone was queued or riding by three minutes and the moved-metric had nobody on the
        // paths to compare. A busier park is a park; a guest who chooses to wander instead of ride
        // would be a behaviour nobody has read, so the cap goes up rather than the policy.
        _guestCap = 28; _gateEvery = 20; _gateTimer = _gateEvery - 1;
        if (!OpenGate()) { GD.Print("[guest] the gate would not open, so there is nobody to photograph"); return; }
        GuestTestRide(xl, z0);
    }

    /// <summary>Stand Crazy Ape beside the corridor. ⚠ A RIDE THAT BOARDS: Chac Atak, Gorilla
    /// Thrilla, Temple Of Gloom, Dino Karts, Splish Splash and Jurassic Tours take a guest and
    /// then wait forever on COAST/BUMP/TOUR, which this executable answers with zero -- the
    /// game's behaviour, and a useless demo. Crazy Ape boards and returns its riders, and it is
    /// master's standing pick for a preview.
    ///
    /// The site is found by scanning, in a fixed order, for a cursor cell where the whole thing
    /// fits, the queue stub (bare ground -- a queue may never be laid over path) touches the laid
    /// network, and the exit stub is on it or touches it, so a rider handed back has a way home.
    /// Placed through <see cref="PlaceHeld"/>, exactly as a press would.</summary>
    void GuestTestRide(int xl, int z0)
    {
        ToggleBuildMenu();
        ShowBuildCategory("Rides");
        int row = -1;
        for (int i = 0; i < _buildRows.Count && row < 0; i++)
        {
            var d = DefinitionFor(_lib.Rides[_buildRows[i]].Model);
            if (d?.Name != null && d.Name.Contains(_guestRide, StringComparison.OrdinalIgnoreCase)) row = i;
        }
        if (row < 0) { GD.Print($"[guest] no '{_guestRide}' among this archive's Rides -- nothing to board"); return; }
        var grid = _guests.Paths;
        bool Touches(int x, int y) => ParkPaths.Neighbours(new ParkCell(x, y)).Any(grid.Open);
        int tried = 0, fits = 0, doors = 0;
        for (int turn = 0; turn < 4; turn++)
        {
            // ⚠ Armed once per turn: Fits() only asks, and re-arming per cell logged a line each.
            ArmFromList(row);
            _place.Turn(turn);
            for (int y = z0 - 2; y < z0 + 14; y++)
                for (int x = xl - 12; x < xl + 14; x++)
                {
                    tried++;
                    if (!_place.Fits(_park, x, y)) continue;
                    fits++;
                    var stubs = _place.Stubs(_park, x, y).ToList();
                    if (!stubs.Any(s => s.Entrance) || !stubs.Any(s => !s.Entrance)) continue;
                    doors++;
                    var q = stubs.First(s => s.Entrance); var o = stubs.First(s => !s.Entrance);
                    // ⭐ THE QUEUE STUB MUST TOUCH THE NETWORK -- that is the cell a guest walks to.
                    // The exit stub need not: a ride's doors may face opposite ways, and the way
                    // home from the exit is a path the player lays afterwards, so it is laid below.
                    if (!Touches(q.X, q.Y)) continue;
                    var (cx, cy) = _place.CornerFor(x, y);
                    int w = _place.Turned.Width, h = _place.Turned.Height;
                    int placed = _park.Placed.Count;
                    _cursorOverride = (x, y);
                    PlaceHeld();
                    _cursorOverride = null;
                    if (_park.Placed.Count == placed) { GD.Print($"[guest] the ride was REFUSED at ({x},{y}) turned {turn * 90} although it fitted"); continue; }
                    CloseTool();
                    _guestTestRide = (cx, cy, w, h);
                    var ride = _sim?.Rides.LastOrDefault();
                    GD.Print($"[guest] {_place.Display ?? _guestRide} at ({cx},{cy}) {w}x{h} turned {turn * 90} after {tried} cells tried ({fits} fitted, {doors} with both doors): "
                           + $"queue stub ({q.X},{q.Y}) {_paths.KindAt(q.X, q.Y)}, exit stub ({o.X},{o.Y}) {_paths.KindAt(o.X, o.Y)}; "
                           + $"the sim's ride has entrance {ride?.Entrance} exit {ride?.Exit}"
                           + $"{(ride == null ? " -- NO SCRIPT STARTED" : ride.Has("VAR_LETMEON") ? "" : " -- declares no VAR_LETMEON, so it takes nobody")}");
                    ConnectExit(o.X, o.Y);
                    // ⚠ The menu that armed it would otherwise stand over half the picture.
                    if (_buildBox != null && _buildBox.Visible) ToggleBuildMenu();
                    return;
                }
        }
        _place.Clear();
        GD.Print($"[guest] no site takes {_guestRide} with its queue stub on the network: {tried} cells tried, {fits} fitted, {doors} of those with both doors, none touching");
    }

    /// <summary>The way home from a ride's exit: the shortest run of layable, empty ground from
    /// the exit stub to the nearest network cell, laid through the tool -- what the exit-path tool
    /// that opens after a placement is for, done by hand because a capture has no hand.
    /// ⚠ A SEARCH, NOT A STRAIGHT LINE: the first version tried the four straight runs and the
    /// ride's own body blocked every one, so thirty-one riders were handed back onto a stub with
    /// no way off it.</summary>
    void ConnectExit(int ex, int ey)
    {
        var grid = _guests.Paths;
        var start = new ParkCell(ex, ey);
        if (ParkPaths.Neighbours(start).Any(grid.Open)) { GD.Print($"[guest] the exit stub ({ex},{ey}) already meets the network"); return; }
        var prev = new Dictionary<ParkCell, ParkCell> { [start] = start };
        var pending = new Queue<ParkCell>(); pending.Enqueue(start);
        ParkCell? hit = null;
        while (hit == null && prev.Count < 600 && pending.TryDequeue(out var c))
            foreach (var n in ParkPaths.Neighbours(c))
            {
                if (prev.ContainsKey(n)) continue;
                if (grid.Open(n)) { prev[n] = c; hit = n; break; }
                if (!_paths.CanLay(n.X, n.Z) || !_park.Vacant(n.X, n.Z) || _paths.KindAt(n.X, n.Z) != PathTool.Kind.None) continue;
                prev[n] = c; pending.Enqueue(n);
            }
        if (hit is not { } joined)
        { GD.Print($"[guest] the exit stub ({ex},{ey}) could not be joined to the network -- riders handed back there will be stranded"); return; }
        var run = new List<(int X, int Y)>();
        for (var c = prev[joined]; ; c = prev[c]) { run.Add((c.X, c.Z)); if (c == start) break; }
        run.Reverse();
        run.Add((joined.X, joined.Z));
        LayLeg(run, PathTool.Kind.Path, 0);
        RefreshFloor();
        GD.Print($"[guest] exit path: {run.Count - 2} cells laid from ({ex},{ey}) to the network at {joined}");
    }

    /// <summary>The free camera, inside the park looking back at the gate, so guests walk toward
    /// it down the corridor -- pulled back to take in the ride when the control run stood one.
    /// ⚠ Free, not the game's: the game camera is placed by tile through its own frame, and a
    /// control wants its eye in the frame the guests are drawn in.</summary>
    void GuestTestCamera()
    {
        if (_guests == null || _mouth == null || _mouth.Count == 0) return;
        var m = _mouth[0];
        _freeCam = true;
        var look = new Vector3(m.X + 1f, 0.4f, m.Z + 4f);
        _dist = 10f;
        if (_guestTestRide is { } r)
        {
            look = new Vector3((look.X + r.X + r.W * 0.5f) * 0.5f, 0.4f, (look.Z + r.Y + r.H * 0.5f) * 0.5f);
            _dist = 16f;
        }
        _focus = GuestWorld(look, m);
        _pitch = -0.5f; _yaw = Mathf.Pi;
        GD.Print($"[guest] camera: free orbit on ({_focus.X:F1}, {_focus.Y:F1}, {_focus.Z:F1}), {_dist:F0} out, "
               + $"{Mathf.RadToDeg(-_pitch):F0} degrees down, facing the gate");
    }

    /// <summary>The free camera close on the ride the control run stood, from the side its
    /// riders sit on, so a seated kid is a kid in the picture and not a pixel.</summary>
    void GuestTestCloseUp()
    {
        if (_guestTestRide is not { } r || _mouth == null || _mouth.Count == 0) return;
        _freeCam = true;
        var centre = GuestWorld(new Vector3(r.X + r.W * 0.5f, 0f, r.Y + r.H * 0.5f), _mouth[0]);
        // ⭐ AT THE RIDERS, if there are any: their mean seat is where the picture should look.
        if (_seated.Count > 0)
        {
            var mean = Vector3.Zero;
            foreach (var seat in _seated.Values) mean += seat.At.Origin;
            centre = mean / _seated.Count;
        }
        // ⭐ ONE RIDER, FACE ON: the eye is put along ONE seat's forward -- seat 0's if somebody
        // is in it, else the first rider's -- close in, so the picture is a face or the back of a
        // head and nothing else. A ring's MEAN forward points nowhere (the Hot Pot gave three
        // backs of heads), and a swing's crate wall hid King's twice. With no seats, the gate side.
        float yaw = 0f; float dist = 7f; float pitch = -0.3f;
        if (_seated.Count > 0)
        {
            var pick = _seated.FirstOrDefault(kv => kv.Value.Where.Contains(" seat 0 "));
            if (pick.Value.Where == null) pick = _seated.First();
            var f = pick.Value.Forward; f.Y = 0;
            if (f.LengthSquared() > 1e-6f) yaw = Mathf.Atan2(f.X, f.Z);
            centre = pick.Value.At.Origin + new Vector3(0, 0.12f, 0);
            dist = 3.5f; pitch = -0.15f;
            GD.Print($"[guest] close-up camera: on rider #{pick.Key} in {pick.Value.Where}, eye {dist:F1} out along its seat's forward ({Mathf.Sin(yaw):F2}, 0, {Mathf.Cos(yaw):F2})");
        }
        _focus = centre; _dist = dist; _pitch = pitch; _yaw = yaw;
        GD.Print($"[guest] close-up camera: free orbit on ({_focus.X:F1}, {_focus.Y:F1}, {_focus.Z:F1}), {_dist:F1} out, "
               + $"{Mathf.RadToDeg(-_pitch):F0} degrees down, {_seated.Count} riders seated");
    }

    /// <summary>Wind the park to a tick and put every guest in the log with its cell, its step
    /// and its world position -- and, from the second stage on, how far it moved since the last,
    /// which is the number a picture cannot give -- then the rides' census: queued, boarded,
    /// back out.</summary>
    void GuestTestStage(string label, int tick)
    {
        if (_guests == null) return;
        while (_parkTicks < tick) { TickPark(); PresentScripted(frames: false); }
        PresentScripted();                      // present first: see StepPark
        PlaceActors(1f);
        double t = _parkTicks * ParkSim.TickMilliseconds / 1000.0;
        int moved = 0, comparable = 0; float farthest = 0;
        foreach (var g in _guests.Guests)
        {
            var p = Cell(g.Position);
            var world = GuestWorld(p, g.Cell);
            string motion = "";
            if (_guestAt != null && _guestAt.TryGetValue(g.Id, out var was))
            {
                float d = (p - was).Length();
                comparable++;
                if (d > 0.01f) moved++;
                farthest = Mathf.Max(farthest, d);
                motion = $", moved {d:F2} cells since {_guestLabel}";
            }
            GD.Print($"[guest] {label} t={t:F2}s #{g.Id} {g.State} at {g.Cell}"
                   + (g.Next is ParkCell n ? $" -> {n} {g.Fraction:F2}" : "")
                   + $" world ({world.X:F2}, {world.Y:F2}, {world.Z:F2}), bound for {g.Destination}{motion}");
        }
        // ⚠ ONLY GUESTS PRESENT AT BOTH STAGES CAN BE COMPARED. A guest who rode in between left
        // the walk and came back, and one who came in later has no earlier position; a verdict
        // over the whole crowd once said "NOBODY MOVED" about a park where everybody had.
        if (_guestAt != null)
            GD.Print($"[guest] {label}: of {_guests.Guests.Count} walking, {comparable} were also walking at {_guestLabel}; "
                   + $"{moved} of those moved, the farthest {farthest:F2} cells"
                   + (comparable == 0 ? " -- nobody to compare" : moved == 0 ? " -- NOBODY MOVED" : ""));
        if (_visitors != null)
        {
            int queued = _visitors.Plans.Values.Count(p => p.Intent == VisitorIntent.Queued);
            GD.Print($"[guest] {label} t={t:F2}s rides: {_visitors.Boardings} boarded so far, {_visitors.Rides} came back out, "
                   + $"{queued} queued or riding now, {_guests.Guests.Count} walking");
            foreach (var r in _sim.Rides)
                GD.Print($"[guest] {label} {r.Name}: queue {r.Queue.Count}, on ride {r.OnRide}, {(r.Running ? "running" : "standing")}, "
                       + $"slot {r.Slot}:{r.Variant}, doors {r.Entrance}/{r.Exit}, seats {r.Host?.Seats.Count ?? 0} of {r.Host?.HeadSlots ?? 0} taken"
                       + (r.Fault != null ? $" FAULT {r.Fault}" : ""));
            foreach (var (id, seat) in _seated)
                GD.Print($"[guest] {label} rider #{id} in {seat.Where} at world ({seat.At.Origin.X:F2}, {seat.At.Origin.Y:F2}, {seat.At.Origin.Z:F2})"
                       + (_actors.TryGetValue(id, out var body) && body != null ? "" : " -- NO BODY")
                       + (_headOnly.Contains(id) ? $"; drawn as a head ({(_parts.TryGetValue(id, out var pt) ? pt.Head : "?")})" : "; drawn WHOLE")
                       + (PoseSeatedRiders && _drawn.TryGetValue(id, out var dr) ? $"; pose: {(_posed.Contains(id) ? dr.Where : "BIND -- " + dr.Where)}" : ""));
            // ⭐⭐ THE YAW, AS A NUMBER AND NOT A SQUINT. Master: the rider is 180 out RELATIVE TO ITS
            // SEAT, and the seat helpers are authored per seat (findings 819c42d: dodgems park at
            // five angles, the volcano's ring at sixteen), so no flip may be keyed off the ride's
            // data. The walk path's basis for a heading is known-good, so for each rider the seat's
            // world forward s is fed to WalkBasis as if it were a heading and
            // R = WalkBasis(s)^-1 * B_seat is what the seat path does DIFFERENTLY for the same
            // facing: identity means the two agree; a 180 yaw is the bug exactly; a pitch or a
            // det -1 means a reading of one of the paths is wrong and NOTHING gets patched.
            // ⭐⭐ WHERE THE HEAD ACTUALLY LANDED, three numbers on one line. Master: "the heads are
            // floating above the seats. its as if the body was removed under em" -- and Show()'s
            // own doc said the base-at-the-fitting reading was a picture away from being wrong.
            // So per rider: the head mesh's world Y range (and the ALL-VISIBLE bounds beside it --
            // if the two differ, the body is still being drawn or onlyNamed is not filtering),
            // the seat helper's world Y, and the top of the part the seat is in. The differences
            // name the fix; nothing is nudged until they do.
            foreach (var (id, seat) in _seated)
            {
                if (!_actors.TryGetValue(id, out var ra) || ra == null || !IsInstanceValid(ra) || !_parts.TryGetValue(id, out var rp)) continue;
                var (hl, hh) = Park.DrawnBounds(ra, inParent: true, onlyNamed: rp.Head);
                var (al, ah) = Park.DrawnBounds(ra, inParent: true);
                bool headOnlyDrawn = hh.Y >= hl.Y && Mathf.Abs(al.Y - hl.Y) < 1e-3f && Mathf.Abs(ah.Y - hh.Y) < 1e-3f;
                float helperY = seat.At.Origin.Y;
                string partTop = float.IsNaN(seat.PartTop) ? "n/a" : seat.PartTop.ToString("F2");
                GD.Print($"[guest] {label} height: rider #{id} head {rp.Head} Y {hl.Y:F2}..{hh.Y:F2} (all visible {al.Y:F2}..{ah.Y:F2}{(headOnlyDrawn ? "" : " -- NOT JUST THE HEAD")}); "
                       + $"seat helper Y {helperY:F2}; part {seat.Part} top {partTop}; "
                       + $"head base - helper {hl.Y - helperY:+0.00;-0.00}, head centre - helper {(hl.Y + hh.Y) / 2 - helperY:+0.00;-0.00} (hung by {RiderHeadAnchor})"
                       + (float.IsNaN(seat.PartTop) ? "" : $", helper - part top {helperY - seat.PartTop:+0.00;-0.00}, head top - part top {hh.Y - seat.PartTop:+0.00;-0.00}"));
            }
            // ⚠ THE VERDICT IS A DIRECT TEST, not a reading of R's axis: the kid's forward (its
            // basis Z, the walk convention) against the seat's forward, and the kid's up against
            // the seat's up. "Backward" is forward = -s with up = seat up -- a half-turn about the
            // seat's own up whatever the seat's pitch -- which R alone reported as "180 about
            // (0, 0.98, 0.20)" on a pitched swing, because a yaw-only WalkBasis leaves the seat's
            // pitch inside R. R is still printed, raw, beside the verdict.
            static string Facing(Basis kid, Vector3 s, Vector3 up)
            {
                if (s.LengthSquared() < 1e-6f) return "seat forward undefined";
                float f = kid.Z.Normalized().Dot(s), u = up.LengthSquared() > 1e-6f ? kid.Y.Normalized().Dot(up) : 1f;
                return f > 0.98f && u > 0.98f ? "FACES ITS SEAT" : f < -0.98f && u > 0.98f ? "BACKWARD (half-turn about the seat's up)" : $"other (forward dot {f:F2}, up dot {u:F2})";
            }
            int yaw180 = 0, agree = 0, other = 0;
            foreach (var (id, seat) in _seated)
            {
                var sf = seat.Forward; sf.Y = 0;
                string rstr = sf.LengthSquared() > 1e-6f ? Describe(RotationOf(WalkBasis(sf.Normalized()).Inverse() * seat.At.Basis)) : "undefined";
                string verdict = Facing(seat.At.Basis, seat.Forward, seat.At.Basis.Y);
                if (verdict.StartsWith("BACKWARD")) yaw180++; else if (verdict.StartsWith("FACES")) agree++; else other++;
                GD.Print($"[guest] {label} yaw: rider #{id} seat forward ({seat.Forward.X:F2}, {seat.Forward.Y:F2}, {seat.Forward.Z:F2}); R = {rstr}; {verdict}");
            }
            // ⭐⭐ EVERY SEAT OF EVERY RIDE, RIDER OR NOT. R depends on the seat's basis and this
            // code's composition, not on who sits there, so a ride whose script boards nobody
            // still gives every seat to the census, and a ride with all its seats parallel
            // (Crazy Ape: sixteen identical bases) cannot pass off one test as sixteen. Measured
            // through the same SeatPose a rider is drawn with.
            foreach (var (ride, model, _, _, _) in _scripted)
            {
                if (ride.Host == null || model?.Root == null || !IsInstanceValid(model.Root) || model.LastWorld == null || !_rideMeshes.TryGetValue(ride.Id, out var rm)) continue;
                var rroot = model.Root.GlobalTransform;
                int faces = 0, back = 0, odd = 0, seatsSeen = 0; var yaws = new List<int>();
                for (int slot = 0; slot < ride.Host.HeadSlots; slot++)
                {
                    if (rm.FindFitting(slot + 1, 0x80) is not { Node: >= 0 } fit) continue;
                    if (!SeatPose(rm, model, rroot, fit, out var pose, out var sfw, out var sup)) continue;
                    seatsSeen++;
                    string verdict = Facing(pose.Basis, sfw, sup);
                    if (verdict.StartsWith("FACES")) faces++; else if (verdict.StartsWith("BACKWARD")) back++; else odd++;
                    var flat = sfw; flat.Y = 0;
                    yaws.Add(flat.LengthSquared() > 1e-6f ? Mathf.RoundToInt(Mathf.RadToDeg(Mathf.Atan2(flat.X, flat.Z))) : 999);
                    string rot = flat.LengthSquared() > 1e-6f ? Describe(RotationOf(WalkBasis(flat.Normalized()).Inverse() * pose.Basis)) : "undefined";
                    GD.Print($"[guest] {label} seat {slot} of {ride.Name} on {rm.NodeName(fit.Node)}: seat forward ({sfw.X:F2}, {sfw.Y:F2}, {sfw.Z:F2}) yaw {yaws[^1]} deg; R = {rot}; {verdict}");
                }
                var distinct = yaws.Distinct().OrderBy(y => y).ToList();
                GD.Print($"[guest] {label} seats of {ride.Name}: {seatsSeen} measured, {faces} face their seat, {back} backward, {odd} other; "
                       + $"seat yaws {string.Join(" ", distinct)} ({distinct.Count} distinct -- {(distinct.Count > 1 ? "a discriminating ride" : "ALL PARALLEL: one test, not " + seatsSeen)})");
            }
            // ⚠ THE CONTROL: a WALKER's own R against its own heading must be identity, or the
            // instrument is broken and the rider rows mean nothing. Never skipped for seeming
            // obvious; if nobody is walking at the census, one guest is let in and stepped until
            // it has a heading, and measured through the same PlaceActors path as every walker.
            var control = _guests.Guests.FirstOrDefault(g => g.Next is not null && _actors.TryGetValue(g.Id, out var ca) && ca != null && IsInstanceValid(ca));
            if (control == null && _mouth != null && _mouth.Count > 0)
            {
                var pool = GuestPool();
                if (pool.Count > 0)
                {
                    var letIn = _visitors != null ? _visitors.Arrive(_mouth[0], pool[_guestRng.Next(pool.Count)]) : _guests.Spawn(_mouth[0], pool[_guestRng.Next(pool.Count)]);
                    for (int i = 0; i < 3 && letIn.Next is null; i++) TickPark();
                    PlaceActors(1f);
                    if (letIn.Next is not null && _actors.TryGetValue(letIn.Id, out var la) && la != null) control = letIn;
                }
            }
            if (control != null && control.Next is ParkCell cn && _actors.TryGetValue(control.Id, out var cactor))
            {
                var h = new Vector3(cn.X - control.Cell.X, 0, control.Cell.Z - cn.Z);
                var rot = RotationOf(WalkBasis(h).Inverse() * cactor.Basis);
                GD.Print($"[guest] {label} yaw control: walker #{control.Id} heading ({h.X:F0}, 0, {h.Z:F0}); R = {Describe(rot)}"
                       + (Describe(rot) == "identity" ? " -- instrument OK" : " -- NOT IDENTITY: INSTRUMENT BROKEN, the rider rows above mean nothing"));
            }
            else GD.Print($"[guest] {label} yaw control: NO WALKER could be measured -- the rider rows above are UNVERIFIED");
            if (_seated.Count > 0)
                GD.Print($"[guest] {label} yaw: of {_seated.Count} riders, {yaw180} face BACKWARD (a half-turn about their seat's up), {agree} face their seat, {other} other");
            foreach (var (id, leg) in _walking)
                GD.Print($"[guest] {label} walker #{id} on {leg.Where} at world ({leg.At.Origin.X:F2}, {leg.At.Origin.Y:F2}, {leg.At.Origin.Z:F2})");
            // ⭐ THE MIRROR COUNT, AS A NUMBER. A kid's mesh must end up with an ODD number of
            // mirrors whether it walks (its own root: one) or sits (ride root, the Mirror factor,
            // its own root: three); a cancelled pair would leave the seated kid inside out on a
            // silhouette too symmetric to show it. So the determinant sign of each kid's FINAL
            // basis is printed and the seated ones must match the walking one.
            float? walkDet = null;
            foreach (var g in _guests.Guests)
                if (_actors.TryGetValue(g.Id, out var wa) && wa != null && IsInstanceValid(wa) && wa.GetChildCount() > 0 && wa.GetChild(0) is Node3D wroot)
                { walkDet = wroot.GlobalTransform.Basis.Determinant(); break; }
            var seatDets = new List<float>();
            foreach (var id in _seated.Keys)
                if (_actors.TryGetValue(id, out var sa) && sa != null && IsInstanceValid(sa) && sa.GetChildCount() > 0 && sa.GetChild(0) is Node3D sroot)
                    seatDets.Add(sroot.GlobalTransform.Basis.Determinant());
            if (seatDets.Count > 0)
            {
                bool allNegative = seatDets.All(d => d < 0);
                bool match = walkDet is not { } wd || seatDets.All(d => Math.Sign(d) == Math.Sign(wd));
                GD.Print($"[guest] {label} mirror: walking kid det {(walkDet is { } w0 ? w0.ToString("F2") : "n/a (nobody walking)")}; "
                       + $"seated kids det {string.Join(" ", seatDets.Select(d => d.ToString("F2")))} -- "
                       + (allNegative && match ? "one net mirror each, same as a walking kid" : "SIGN MISMATCH: a seated kid is INSIDE OUT"));
            }
            foreach (var r in _sim.Rides)
                GD.Print($"[guest] {label} {r.Name}: {r.Host?.Walkers.Count ?? 0} walker poses on record, {r.Machine?.GuestIds.Count ?? 0} guests held by the script, "
                       + $"{_walking.Count(kv => kv.Value.Where.StartsWith(r.Name))} drawn walking node to node, "
                       + $"walks {(r.Machine?.WalksAreTimed == true ? "TIMED from the model's fittings" : "at the floor (no node resolved yet)")}");
            // ⭐ THE CLOSEST PAIR is the number that says whether seats stack: at the node origins
            // it was 0.00 for three riders on the crate.
            if (_seated.Count > 1)
            {
                var list = _seated.ToList();
                float closest = float.MaxValue; string pair = "";
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        float d = (list[i].Value.At.Origin - list[j].Value.At.Origin).Length();
                        if (d < closest) { closest = d; pair = $"#{list[i].Key} and #{list[j].Key}"; }
                    }
                GD.Print($"[guest] {label} seats: closest pair {pair} are {closest:F2} apart" + (closest < 0.05f ? " -- STACKED" : ""));
            }
            // ⭐ SCALE, MEASURED IN ONE UNIT: the ride, the parts the riders sit on, a kid, and the
            // terrain's ticket booths -- the game's own human-sized prop -- all through DrawnBounds
            // in world units, so "the kids look small" can be checked against a number rather than
            // against how big a gorilla ought to look.
            foreach (var (ride, model, _, _, _) in _scripted)
            {
                if (model?.Root == null || !IsInstanceValid(model.Root) || !_rideMeshes.TryGetValue(ride.Id, out var mesh)) continue;
                var (lo, hi) = Park.DrawnBounds(model.Root, inParent: true);
                // The seat's PART is the first mesh up its parent chain (a Head helper hangs off an
                // arm), and that is what a kid's height is compared against.
                var parts = ride.Host.Seats.Keys.Select(slot => mesh.FindFitting(slot + 1, 0x80))
                    .Where(f => f is { Node: >= 0 })
                    .Select(f => { int n = f.Value.Node; for (int guard = 0; n >= mesh.Meshes.Count && guard < 32; guard++) n = mesh.NodeParent(n); return n; })
                    .Where(n => n >= 0 && n < mesh.Meshes.Count)
                    .Select(n => mesh.Meshes[n].Name).Distinct().ToList();
                string partSizes = string.Join(", ", parts.Select(name =>
                {
                    var (plo, phi) = Park.DrawnBounds(model.Root, inParent: true, onlyNamed: name);
                    return phi.Y >= plo.Y ? $"{name} {phi.Y - plo.Y:F2} tall x {phi.X - plo.X:F2} x {phi.Z - plo.Z:F2}" : $"{name} (no drawn mesh)";
                }));
                GD.Print($"[guest] scale: {ride.Name} stands {hi.Y - lo.Y:F2} tall, {hi.X - lo.X:F2} x {hi.Z - lo.Z:F2} on its plot; seat parts: {partSizes}");
            }
            var kid = _actors.Values.FirstOrDefault(a => a != null && IsInstanceValid(a));
            if (kid != null)
            {
                var (klo, khi) = Park.DrawnBounds(kid, inParent: true);
                GD.Print($"[guest] scale: a kid stands {khi.Y - klo.Y:F2} tall, {khi.X - klo.X:F2} wide (cell = 1.00)");
            }
            if (_terrain?.Root != null && IsInstanceValid(_terrain.Root))
            {
                var (blo, bhi) = Park.DrawnBounds(_terrain.Root, inParent: true, onlyNamed: "ticket_booths");
                GD.Print(bhi.Y >= blo.Y ? $"[guest] scale: the terrain's ticket_booths stand {bhi.Y - blo.Y:F2} tall, {bhi.X - blo.X:F2} x {bhi.Z - blo.Z:F2} -- the game's own human-sized prop"
                                        : "[guest] scale: this terrain draws no ticket_booths mesh to compare against");
            }
        }
        else GD.Print($"[guest] {label} t={t:F2}s no ride stands, so nobody rides");
        _guestAt = _guests.Guests.ToDictionary(g => g.Id, g => Cell(g.Position));
        _guestLabel = label;
        // ⭐ DOES THE RIDE CARRY THEM? A seat is a node on an animated part, so a rider drawn
        // from LastWorld each frame should move while the ride runs. Measured rather than
        // assumed: one more second of ticks after the last stage, and each rider's displacement,
        // with whether the ride was running -- a zero on a standing ride is not a finding.
        // ⚠ So the final shot is one second later than the B census it follows.
        if (label == "B" && _seated.Count > 0)
        {
            var before = _seated.ToDictionary(kv => kv.Key, kv => kv.Value.At);
            var running = _sim.Rides.Where(r => r.Running).Select(r => r.Name).ToList();
            for (int i = 0; i < 25; i++) { TickPark(); PresentScripted(); }
            PlaceActors(1f);
            // ⭐ AND THE TURN, not only the move: a rider on a banana that swings up must tip, so
            // the forward and up vectors are printed a second apart with the angle between them --
            // position that moves while the vectors stay put is the yaw-only mistake, in numbers.
            int carried = 0, turned = 0; float mostTurn = 0;
            foreach (var (id, seat) in _seated)
                if (before.TryGetValue(id, out var was))
                {
                    float d = (seat.At.Origin - was.Origin).Length();
                    var f0 = was.Basis.Z.Normalized(); var f1 = seat.At.Basis.Z.Normalized();
                    var u0 = was.Basis.Y.Normalized(); var u1 = seat.At.Basis.Y.Normalized();
                    float turnF = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(f0.Dot(f1), -1f, 1f)));
                    float turnU = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(u0.Dot(u1), -1f, 1f)));
                    if (d > 0.001f) carried++;
                    if (turnF > 0.5f || turnU > 0.5f) turned++;
                    mostTurn = Mathf.Max(mostTurn, Mathf.Max(turnF, turnU));
                    GD.Print($"[guest] carry: rider #{id} in {seat.Where} moved {d:F3} units over 1 s; "
                           + $"forward ({f0.X:F2}, {f0.Y:F2}, {f0.Z:F2}) -> ({f1.X:F2}, {f1.Y:F2}, {f1.Z:F2}) turned {turnF:F1} deg/s; "
                           + $"up ({u0.X:F2}, {u0.Y:F2}, {u0.Z:F2}) -> ({u1.X:F2}, {u1.Y:F2}, {u1.Z:F2}) tipped {turnU:F1} deg/s");
                }
            GD.Print($"[guest] carry: {carried} of {before.Count} riders moved and {turned} of {before.Count} turned with the ride (the most {mostTurn:F1} deg/s)"
                   + (turned > 0 && mostTurn < 5f ? " -- BARELY TURNING for a swinging seat, suspicious" : "")
                   + (running.Count > 0 ? $" ({string.Join(", ", running)} running)" : " -- but no ride was running, so this says nothing about carrying")
                   + (carried > 0 && turned == 0 ? " -- MOVING WITHOUT TURNING: the basis is not reaching them" : ""));
        }
    }

    void SaveShot(string path)
    {
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path);
        GD.Print($"wrote {path} ({img.GetWidth()}x{img.GetHeight()})");
    }

    /// <summary>`C:\x\shot.png` + `-a` = `C:\x\shot-a.png`: a second picture beside the one asked for.</summary>
    static string ShotSibling(string path, string suffix)
    {
        string dir = System.IO.Path.GetDirectoryName(path) ?? "";
        string stem = System.IO.Path.GetFileNameWithoutExtension(path), ext = System.IO.Path.GetExtension(path);
        return System.IO.Path.Combine(dir, stem + suffix + ext);
    }

    void StepBuilding(float frames)
    {
        for (int i = _building.Count - 1; i >= 0; i--)
        {
            var (m, at) = _building[i];
            if (m?.Root == null || !GodotObject.IsInstanceValid(m.Root)) { _building.RemoveAt(i); continue; }
            at += frames;
            if (at >= m.Frames - 1)
            {
                m.SetFrame(Mathf.Max(m.Frames - 1, 0));
                _building.RemoveAt(i);
                continue;
            }
            m.SetFrame(at);
            _building[i] = (m, at);
        }
    }

    void Status(string text) { if (_toolStatus != null) _toolStatus.Text = text; }

    /// <summary>⭐ A CONTROL FOR THE BUILD MENU that needs no mouse: open it, read back the
    /// categories the archive gave it, take the first thing in each, and try to put one down.
    ///
    /// ⚠ It goes through the SAME calls the menu and the click do. A check that armed a placement
    /// by hand would pass with the menu unwired, which is most of what there is to get wrong.</summary>
    void CheckBuildMenu()
    {
        _buildChecked = true;
        ToggleBuildMenu();
        GD.Print($"[build] {_buildTabs.Length} categories: "
               + string.Join(" ", _buildTabBar.GetChildren().OfType<Button>().Select(b => b.Text)));
        foreach (var b in _buildTabBar.GetChildren().OfType<Button>().ToList())
        {
            b.EmitSignal(Button.SignalName.Pressed);
            GD.Print($"[build]   {b.Text} -> {_buildRows.Count} listed, first \"{(_buildList.ItemCount > 0 ? _buildList.GetItemText(0) : "-")}\"");
        }
        // Arm something with a real footprint and try it on a cell the park will take.
        ShowBuildCategory("Rides");
        for (int row = 0; row < _buildRows.Count; row++)
        {
            // ⚠ Judge the row BEFORE arming it. Arming says so in the log, and arming all
            // fifty-six to find one drowns the check in its own noise.
            var candidate = DefinitionFor(_lib.Rides[_buildRows[row]].Model);
            if (!HasQueue(candidate) || candidate.Shape == null) continue;
            ArmFromList(row);
            if (!_place.Active || _place.Turned.Width < 2) continue;
            var f = _park.Field;
            int cx = f.Width / 2, cy = f.Height / 2;
            _cursorOverride = (cx, cy);
            int before = _park.Placed.Count;
            bool fits = _place.Fits(_park, cx, cy);
            GD.Print($"[build] holding {_place.Display} {_place.Turned.Width}x{_place.Turned.Height}"
                   + $" entry {_place.DoorFor(cx, cy)?.ToString() ?? "none"}"
                   + $" exit {_place.ExitFor(cx, cy)?.ToString() ?? "none"}"
                   + $" at ({cx},{cy}): {(fits ? "fits" : "blocked")}");
            PlaceHeld();
            GD.Print($"[build] after the press the park holds {_park.Placed.Count} things"
                   + $" (was {before}) -- {(_park.Placed.Count > before ? "it went down" : "NOTHING WAS PLACED")}");
            // ⚠ And a control that must REFUSE: the same thing hung off the edge of the plot.
            // ⭐ The hand-over. Placing a queued ride must leave the QUEUE tool open on the tile
            // outside its entrance; closing that must open the PATH tool outside its exit.
            GD.Print($"[build] after placing: tool {(_toolOpen ? _toolKind.ToString() : "shut")}"
                   + $" run from ({_runX},{_runY})"
                   + $" -- {(_toolOpen && _toolKind == PathTool.Kind.Queue ? "queue mode, as it must be" : "NO QUEUE MODE")}");
            CloseTool();
            GD.Print($"[build] after leaving the queue: tool {(_toolOpen ? _toolKind.ToString() : "shut")}"
                   + $" run from ({_runX},{_runY})"
                   + $" -- {(_toolOpen && _toolKind == PathTool.Kind.Path ? "exit path, as it must be" : "no exit path")}");
            var exitAt = (_runX, _runY);
            CloseTool();

            // ⚠ AND THE OTHER WAY ROUND, which is master's rule: "if the exit path is already
            // touching another path, dont open the exit path tool." The SAME exit, with a path
            // laid beside it, must refuse -- and the line above is its partner, because "it never
            // opens" would satisfy this one on its own.
            if (exitAt.Item1 >= 0)
            {
                _paths.Lay(exitAt.Item1 + 1, exitAt.Item2, PathTool.Kind.Path);
                _toolOpen = false;
                StartExitPath((exitAt.Item1, exitAt.Item2));
                GD.Print($"[build] with a path beside the exit, the tool is "
                       + $"{(_toolOpen ? "OPEN -- it should have been skipped" : "shut, as it must be")}");
                CloseTool();
            }

            // ⚠ AND A CONTROL THAT MUST REFUSE: the same thing hung off the edge of the plot.
            ArmFromList(row);
            _cursorOverride = (0, 0);
            int now = _park.Placed.Count;
            PlaceHeld();
            GD.Print($"[build] at the plot corner the park holds {_park.Placed.Count}"
                   + $" -- {(_park.Placed.Count == now ? "refused, as it must be" : "IT WENT DOWN ANYWAY")}");
            break;
        }
        _place.Clear();
        _cursorOverride = null;
        _ghostView?.Clear();
    }

    /// <summary>⭐ A CONTROL FOR TURNING, and it is a PICTURE as much as a readout -- what is being
    /// claimed is that the mesh, the hole it cuts in the floor and the two door arrows all end up
    /// facing the same way, and no one number says that.
    ///
    /// It puts the same ride down at three different quarter turns and leaves a fourth held over
    /// the cursor so the ghost and its arrows draw. ⚠ The door cells are printed per turn as well,
    /// because a picture of a square ride cannot tell a turned footprint from an unturned one --
    /// the door moving is the part that can only be read off the numbers.</summary>
    void CheckPlacement()
    {
        _buildChecked = true;
        ToggleBuildMenu();
        ShowBuildCategory("Rides");
        int chosen = -1;
        for (int row = 0; row < _buildRows.Count && chosen < 0; row++)
        {
            var d = DefinitionFor(_lib.Rides[_buildRows[row]].Model);
            if (d?.Shape == null) continue;
            var fp = Park.Footprint.From(d.Shape);
            // ⚠ BOTH doors and a shape that is NOT square: a square ride turned a quarter looks
            // exactly like itself, so it would photograph a working rotation and a dead one alike.
            if (fp.EntryX >= 0 && fp.ExitX >= 0 && fp.Width != fp.Height) chosen = row;
        }
        if (chosen < 0) { GD.Print("[place] nothing in Rides has two doors and an oblong shape"); return; }

        var f = _park.Field;
        int bx = f.Width / 2 - 9, by = f.Height / 2 - 4;
        // ⭐ The door markers of every ride placed, kept so the shot can show them ON the built
        // rides -- the ghost only ever draws for what is HELD, so a placed ride's doors are
        // invisible and "the model faces one way and its door is on another" cannot be looked at.
        var doorMarks = new List<(int X, int Y, int Marker, int Turns)>();
        for (int t = 0; t < 3; t++)
        {
            ArmFromList(chosen);
            _place.Turn(t);
            int cx = bx + t * 7, cy = by;
            _cursorOverride = (cx, cy);
            var door = _place.DoorFor(cx, cy);
            var exit = _place.ExitFor(cx, cy);
            var outD = door is { } d0 ? _place.OutsideOf(d0, cx, cy) : null;
            var outE = exit is { } d1 ? _place.OutsideOf(d1, cx, cy) : null;
            GD.Print($"[place] {_place.Display} turned {t * 90} at ({cx},{cy}): "
                   + $"{_place.Turned.Width}x{_place.Turned.Height} "
                   + $"entry {door?.ToString() ?? "none"} out {outD?.ToString() ?? "none"} "
                   + $"turn {(door is { } dd && outD is { } oo ? GhostMarkers.TurnToward(Placement.FacingOf(dd, oo).Dx, Placement.FacingOf(dd, oo).Dy) : -1)} | "
                   + $"exit {exit?.ToString() ?? "none"} out {outE?.ToString() ?? "none"} "
                   + $"turn {(exit is { } xd && outE is { } xo ? GhostMarkers.TurnToward(Placement.FacingOf(xd, xo).Dx, Placement.FacingOf(xd, xo).Dy) : -1)}");
            int before = _park.Placed.Count;
            // ⚠ CAPTURED BEFORE THE PRESS. PlaceHeld drops the blueprint, so asking it afterwards
            // where its stubs were would be asking nothing at all.
            var want = _place.Stubs(_park, cx, cy).ToList();
            foreach (var st in want)
            {
                var which = st.Entrance ? _place.DoorFor(cx, cy) : _place.ExitFor(cx, cy);
                if (which is not { } dc) continue;
                var (ddx, ddy) = Placement.FacingOf(dc, (st.X, st.Y));
                doorMarks.Add((st.X, st.Y, st.Entrance ? (_place.IsRide ? 168 : 172) : 169,
                               GhostMarkers.TurnToward(ddx, ddy)));
            }
            // ⚠ CAPTURED BEFORE THE PRESS, like the stubs: PlaceHeld drops the blueprint, and the
            // cursor cell is not the footprint's CORNER -- asking afterwards would measure a hole
            // half a shape away from the one the ride went into.
            var (fx, fy) = _place.CornerFor(cx, cy);
            int fpw = _place.Turned.Width, fph = _place.Turned.Height;
            GD.Print($"[place]   stubs " + string.Join(" ", want
                        .Select(t => $"({t.X},{t.Y}){(t.Queue ? "queue" : "path")}{(t.Ok ? "" : " BLOCKED")}")));
            PlaceHeld();
            GD.Print($"[place]   {(_park.Placed.Count > before ? "down" : "REFUSED")}; stubs are now "
                   + string.Join(" ", want.Select(t => $"({t.X},{t.Y}){_paths.KindAt(t.X, t.Y)}"))
                   + $" -- {(want.All(t => _paths.KindAt(t.X, t.Y) == (t.Queue ? PathTool.Kind.Queue : PathTool.Kind.Path)) ? "laid with the ride, as they must be" : "NOT LAID")}");
            // ⭐⭐ THREE STAGES OF ONE BUILD, IN ONE PICTURE. Each ride is wound to a different
            // point of its Create animation and taken off the list, so the shot shows a quarter
            // built, two thirds built and finished side by side. A single finished ride cannot
            // tell an animation that played from one that was never wound at all.
            if (_building.Count > 0)
            {
                var m = _building[^1].Model;
                _building.Clear();
                float frac = System.Environment.GetEnvironmentVariable("TPW_BUILD_STAGES") == "1"
                           ? (t == 0 ? 0.25f : t == 1 ? 0.6f : 1f) : 1f;
                float at = Mathf.Max(m.Frames - 1, 0) * frac;
                m.SetFrame(0);
                var b0 = Park.DrawnBounds(m.Root, inParent: true);
                m.SetFrame(at);
                var b1 = Park.DrawnBounds(m.Root, inParent: true);
                // ⚠⚠ A SPAN IS NOT A POSITION. The first version of this line printed the bounding
                // box's DIAGONAL LENGTH at two frames and called them nearly equal -- and a box
                // that has slid sideways has exactly the same diagonal as one that has not. What
                // the alignment question asks about is the CENTRE, so the centre is what it says.
                var (fc, fw, fh) = FootprintRect(fx, fy, fpw, fph);
                // ⭐⭐ DOES THE MODEL AGREE WITH ITS OWN DOOR? Master: "the model visually changes
                // but the tile it comes out of is wrong". A ride's signage stands AT its entrance,
                // so the cell the sign parts land in must be the cell the footprint calls the
                // entrance -- at every turn. Two things derived by different routes from the same
                // quarter turn: if the footprint's rotation and the mesh's ever disagree in sign,
                // this is where it shows, and a picture of a symmetric ride never could.
                foreach (var (needle, what) in new[] { ("sign", "signage"), ("post", "posts") })
                {
                    if (PartCell(m.Root, needle) is not { } pc) continue;
                    GD.Print($"[place]   {what} lands on ({pc.X},{pc.Y}); the footprint's entrance is "
                           + $"({want.FirstOrDefault(q => q.Entrance).X},{want.FirstOrDefault(q => q.Entrance).Y}) outside "
                           + $"-- {(Math.Abs(pc.X - want.FirstOrDefault(q => q.Entrance).X) <= 1 && Math.Abs(pc.Y - want.FirstOrDefault(q => q.Entrance).Y) <= 1 ? "same end, as it must be" : "DIFFERENT ENDS")}");
                }
                // ⭐ BOTH CENTRINGS, EVERY TIME. The claim being made is that aligning on the
                // floor parts changes only the rides that carry them wrongly -- so the control
                // prints what the whole-model box would have said as well, and a ride the change
                // moves is a ride that shows up here rather than one that quietly shifts.
                var whole = Park.DrawnBounds(m.Root, inParent: true);
                var flr = Park.DrawnBounds(m.Root, inParent: true, onlyNamed: "floor");
                bool hasFloor = flr.Max.X > flr.Min.X;
                GD.Print($"[place]   whole-model centre {(whole.Min.X + whole.Max.X) * 0.5f:F2},{(whole.Min.Z + whole.Max.Z) * 0.5f:F2}"
                       + $" size {whole.Max.X - whole.Min.X:F2}x{whole.Max.Z - whole.Min.Z:F2}; floor "
                       + (hasFloor ? $"centre {(flr.Min.X + flr.Max.X) * 0.5f:F2},{(flr.Min.Z + flr.Max.Z) * 0.5f:F2}"
                                   + $" size {flr.Max.X - flr.Min.X:F2}x{flr.Max.Z - flr.Min.Z:F2}"
                                   + $" -- the two disagree by {(flr.Min.X + flr.Max.X - whole.Min.X - whole.Max.X) * 0.5f:F2},"
                                   + $"{(flr.Min.Z + flr.Max.Z - whole.Min.Z - whole.Max.Z) * 0.5f:F2}"
                                 : "NONE -- aligned on the whole model"));
                GD.Print($"[place]   Create {m.Summary}; centre at frame 0 {b0.Min.X + b0.Max.X:F1},{b0.Min.Z + b0.Max.Z:F1}"
                       + $" -> at frame {at:F0}/{m.Frames} {b1.Min.X + b1.Max.X:F1},{b1.Min.Z + b1.Max.Z:F1} (doubled)");
                if (t == 0)
                {
                    // ⭐ NAME THE PART. "The model overhangs" is not an answer anybody can act on;
                    // WHICH piece of it overhangs, and by how much, is. Each drawn surface against
                    // the hole it is supposed to sit in.
                    foreach (var mi in Walk(m.Root))
                    {
                        // ⚠⚠ TRANSFORM TIMES AABB, not the other way round. `aabb * transform` is
                        // the INVERSE transform in Godot, and since every surface here carries its
                        // own animated transform the inverse differs per node -- so the parts came
                        // out in a dozen different spaces and read as metres apart from each other
                        // inside a model four units wide, which is impossible and was the clue.
                        var ab = mi.GlobalTransform * mi.GetAabb();
                        float ox = Mathf.Max(fc.X - fw * 0.5f - ab.Position.X, ab.End.X - (fc.X + fw * 0.5f));
                        float oz = Mathf.Max(fc.Z - fh * 0.5f - ab.Position.Z, ab.End.Z - (fc.Z + fh * 0.5f));
                        GD.Print($"[ride] {mi.Name,-28} {ab.Size.X:F2}x{ab.Size.Y:F2}x{ab.Size.Z:F2}"
                               + $" at {ab.GetCenter().X:F2},{ab.GetCenter().Y:F2},{ab.GetCenter().Z:F2}"
                               + $"{(ox > 0.01f || oz > 0.01f ? $"  OVER by {Mathf.Max(ox, 0):F2},{Mathf.Max(oz, 0):F2}" : "")}");
                    }
                }
                GD.Print($"[place]   hole centre {fc.X:F1},{fc.Z:F1} {fw:F1}x{fh:F1}; built model centre "
                       + $"{(b1.Min.X + b1.Max.X) * 0.5f:F1},{(b1.Min.Z + b1.Max.Z) * 0.5f:F1} "
                       + $"size {b1.Max.X - b1.Min.X:F1}x{b1.Max.Z - b1.Min.Z:F1} -- off by "
                       + $"{(b1.Min.X + b1.Max.X) * 0.5f - fc.X:F2},{(b1.Min.Z + b1.Max.Z) * 0.5f - fc.Z:F2}");
            }
            CloseTool();
        }
        // ⭐ AND A CONTROL FOR SELECTION, which is a pair: a cell the middle ride stands on must
        // select it, and a cell nothing stands on must clear. "It selects" alone is satisfied by
        // a thing that selects whatever you last clicked and never lets go.
        {
            var mid = _park.Placed.Count > 1 ? _park.Placed[1] : default;
            if (mid.Fp.Width > 0)
            {
                // ⭐ HOVER FIRST, with no click at all: the outline must appear on the ride and
                // go again off it. ⚠ Its partner is the empty cell -- "it shows a box" is
                // satisfied by something that shows one everywhere.
                // ⚠ THE TOOL OFF FIRST. Placing the three rides leaves the exit-path tool open,
                // and hover is now suppressed while a tool owns the cursor -- so the first run of
                // this control reported "hovering gives NOTHING, WRONG" about the new rule working
                // exactly as asked. The control's setup was the fault, not the code.
                _toolOpen = false; _place.Clear();
                _cursorOverride = (mid.X + mid.Fp.Width / 2, mid.Y + mid.Fp.Height / 2);
                UpdateHover();
                bool onRide = _hovered == 1;
                _cursorOverride = (mid.X, _park.Field.Height - 3);
                UpdateHover();
                bool offRide = _hovered < 0;
                GD.Print($"[select] hovering {mid.Name} gives {(onRide ? "its outline" : "NOTHING")}, "
                       + $"hovering empty ground gives {(offRide ? "none" : "ONE ANYWAY")}"
                       + $" -- {(onRide && offRide ? "follows the pointer, as it must be" : "WRONG")}");

                _cursorOverride = (mid.X + mid.Fp.Width / 2, mid.Y + mid.Fp.Height / 2);
                int camWas = _game.CursorX;
                bool got = SelectUnderCursor();
                // ⚠⚠ AND THE SELECTION MUST SURVIVE ITS OWN CAMERA MOVE. Focusing on a selection
                // glides the cursor, which is the very thing WASD does to clear one -- so if the
                // clear were driven by the cursor moving rather than by the keys, a selection
                // would drop the instant it was made. This is that trap, asked directly.
                GD.Print($"[select] on {mid.Name}'s own cell: {(got && _selected == 1 ? "selected it" : "MISSED")}"
                       + $"; camera cursor {camWas} -> {_game.CursorX}"
                       + $" -- {(got && _selected == 1 ? "still selected after the camera moved, as it must be" : "WRONG")}");

                // ⭐⭐ AND NOT WHILE A TOOL OWNS THE CURSOR. Master: "selection boxes shouldnt show
                // with path or a blueprint on ur cursor." Each half has its partner right above
                // it -- the same cell hovered with nothing held gave the outline.
                _cursorOverride = (mid.X + mid.Fp.Width / 2, mid.Y + mid.Fp.Height / 2);
                _toolOpen = true; UpdateHover(); bool withTool = _hovered >= 0;
                _toolOpen = false;
                ArmFromList(chosen); UpdateHover(); bool withBlueprint = _hovered >= 0;
                _place.Clear();
                UpdateHover(); bool withNeither = _hovered >= 0;
                GD.Print($"[select] on the same cell: with the path tool open {(withTool ? "A BOX" : "none")}, "
                       + $"holding a blueprint {(withBlueprint ? "A BOX" : "none")}, with neither {(withNeither ? "a box" : "NONE")}"
                       + $" -- {(!withTool && !withBlueprint && withNeither ? "only when the cursor is free, as it must be" : "WRONG")}");

                // ⭐ The ray the hover really uses, asked directly -- a box the ray passes through
                // and one it misses, because a test that only hits proves nothing about picking.
                var rlo = new Vector3(0, 0, 0); var rhi = new Vector3(2, 2, 2);
                bool hits = RayHitsBox(new Vector3(1, 10, 1), Vector3.Down, rlo, rhi, out float tHit);
                bool misses = RayHitsBox(new Vector3(9, 10, 9), Vector3.Down, rlo, rhi, out _);
                GD.Print($"[select] ray down through a 2x2x2 box: {(hits ? $"hits at {tHit:F1}" : "MISSES")}, "
                       + $"beside it: {(misses ? "HITS ANYWAY" : "misses")}"
                       + $" -- {(hits && Mathf.Abs(tHit - 8f) < 0.01f && !misses ? "picks by the model's box, as it must be" : "WRONG")}");

                // Somewhere well clear of all three.
                _cursorOverride = (mid.X, _park.Field.Height - 3);
                bool none = SelectUnderCursor();
                GD.Print($"[select] clicking empty ground: {(!none && _selected < 0 ? "cleared, as it must be" : "STILL HOLDING ONE")}");

                // ⭐ THE BREATH, MEASURED. Stepped through two seconds in console ticks, the pulse
                // must sweep 0 .. sin(1) = 0.8415 and repeat every 51.2 ticks (the |sin| halves the
                // 102.4-tick turn) -- and it must pass through the 0.6958 master's savestate held,
                // or the chain is not the game's.
                var probe = new SelectionBox(null);
                probe.Show(Vector3.Zero, Vector3.One);
                float lo = 9f, hi = -9f; int peaks = 0; float prev = 0f, prev2 = 0f;
                bool hitSample = false;
                for (int t = 0; t < 200; t++)
                {
                    probe.Step(1.0 / GameCamera.TicksPerSecond);
                    float v = probe.Pulse;
                    lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
                    if (t > 1 && prev >= prev2 && prev > v) peaks++;
                    if (Mathf.Abs(v - 0.6958f) < 0.01f) hitSample = true;
                    prev2 = prev; prev = v;
                }
                GD.Print($"[select] pulse over 200 ticks: {lo:F4} .. {hi:F4} (want 0 .. 0.8415), "
                       + $"{peaks} peaks (want ~3.9 at 51.2 ticks), "
                       + $"{(hitSample ? "passes through the savestate's 0.6958" : "NEVER REACHES 0.6958")}"
                       + $" -- {(lo < 0.02f && Mathf.Abs(hi - 0.8415f) < 0.01f && peaks >= 3 && peaks <= 4 && hitSample ? "the game's breath, as it must be" : "WRONG")}");
                probe.Hide();
                // Put it back for the picture.
                _cursorOverride = (mid.X + mid.Fp.Width / 2, mid.Y + mid.Fp.Height / 2);
                SelectUnderCursor();
            }
        }

        CheckStubOverlap();

        // ⭐ THE PICTURE THE QUESTION NEEDS: three rides at three turns with their OWN door markers
        // drawn on the tiles they were laid on, so the mesh and its doorway are in one frame.
        if (System.Environment.GetEnvironmentVariable("TPW_DOOR_MARKS") == "1")
        {
            // ⚠⚠ THE TOOL FIRST. Placing a queued ride hands over to the QUEUE tool and closing
            // that hands over to the EXIT PATH tool -- so the path tool is still open here, and
            // UpdateGhost redraws its own run into this same node every frame, wiping the markers
            // before the capture. They were being made (the node held two surfaces) and painted
            // over, which looks exactly like never being made at all.
            _toolOpen = false; _runX = _runY = -1; _pathFrom = null;
            _place.Clear();
            _ghostView.ShowTurnedCells(doorMarks, _park);
            if (_park.Placed.Count > 1)
            {
                _cursorOverride = (_park.Placed[1].X + _park.Placed[1].Fp.Width / 2,
                                   _park.Placed[1].Y + _park.Placed[1].Fp.Height / 2);
                SelectUnderCursor();
            }
            if (_buildBox != null) _buildBox.Visible = false;
            if (_panel != null) _panel.Visible = false;
            // ⚠ SAY WHETHER ANYTHING WAS ACTUALLY MADE. "Drew 6 markers" is a count of what was
            // ASKED FOR; the node's child count is what came out, and a marker whose texture would
            // not read is cached as a null and silently contributes nothing.
            GD.Print($"[place] asked for {doorMarks.Count} door markers "
                   + $"({string.Join(" ", doorMarks.Select(d => $"({d.X},{d.Y})#{d.Marker}t{d.Turns}"))})"
                   + $"; the ghost node holds {_ghostView.Root.GetChildCount()} surfaces");
            return;
        }

        // The fourth stays on the cursor, turned once, so the shot carries a live ghost with both
        // arrows in it.
        // ⚠ THE PATH TOOL OFF FIRST. Each placement hands over to the queue and then to the exit
        // path, and an open tool draws its OWN run into the same ghost node every frame -- so the
        // picture would be two ghosts fighting over one mesh and neither would be evidence.
        _toolOpen = false; _runX = _runY = -1; _ghostView?.Clear();
        ArmFromList(chosen);
        _place.Turn(1);
        // ⚠ On CLEAR GROUND. A blocked ghost draws every cell red and the doors are then the only
        // thing in the picture that is not red, which is a much weaker reading than a green shape
        // with two arrows off its ends.
        int gx = bx + 21, gy = by + 8;
        for (int r = 0; r < 20 && !_place.Fits(_park, gx, gy); r++) gy += 1;
        _cursorOverride = (gx, gy);
        _ghostAt = (-1, -1, -1, -1);
        UpdatePlacementGhost();
        GD.Print($"[place] holding {_place.Display} turned 90 at ({gx},{gy}) for the picture, "
               + $"{(_place.Fits(_park, gx, gy) ? "clear" : "BLOCKED")}");
        // ⭐ THE FRONT RANK, AT EVERY TURN. The line has to move round the shape as it is turned,
        // and four ranks that are all the same row would draw a line that never moved -- which is
        // exactly the shop bug wearing different clothes.
        for (int q = 0; q < 4; q++)
        {
            _place.Turn(q == 0 ? 0 : 1);
            var (fdx2, fdy2) = _place.Facing;
            GD.Print($"[place]   turned {_place.Turns * 90}: faces {fdx2},{fdy2}, line turn "
                   + $"{GhostMarkers.TurnToward(-fdx2, -fdy2)}, front rank "
                   + string.Join(" ", _place.Front(gx, gy).Select(c => $"({c.X},{c.Y})")));
        }
        _place.Turn(1);   // back to the quarter the picture wants
        // ⚠ BOTH PANELS OUT OF THE WAY. They cover two thirds of a 1280-wide frame, and the thing
        // being photographed is where four rides are standing -- hidden DIRECTLY rather than
        // through the Tab toggle, which drops what is held and would take the ghost with it.
        if (_buildBox != null) _buildBox.Visible = false;
        if (_panel != null) _panel.Visible = false;
    }

    // ------------------------------------------------------------------ the build menu

    /// <summary>Tab opens and shuts it. ⚠ Shutting it also drops whatever was held: a ghost left
    /// following the cursor with no menu to explain it reads as the park being stuck.</summary>
    void ToggleBuildMenu()
    {
        if (_buildBox == null) return;
        _buildBox.Visible = !_buildBox.Visible;
        if (!_buildBox.Visible) { _place.Clear(); _ghostView?.Clear(); Status("build menu closed"); return; }
        if (_toolOpen) CloseTool();
        FillBuildCategories();
    }

    /// <summary>⭐ The categories ARE THE ARCHIVE'S FOLDERS. Every .sam in a park sits under
    /// Rides, Shops, Sideshow, Features or Upgrades, and the catalogue keeps that path in its
    /// name, so the grouping is the game's own rather than a list I made up. Case differs between
    /// worlds -- jungle has "Features", space "features" -- so they are folded.</summary>
    void FillBuildCategories()
    {
        // ⚠ NOT THE TERRAIN. The catalogue carries the terrain files too, and they are the park
        // itself rather than something to put in it -- without this the menu offers a category
        // called Terrain holding terrain_1.mps. ⚠ Excluded by PATH, not by "has a definition":
        // DefinitionFor matches a .sam by directory SUFFIX and hands the terrain one anyway, so
        // that test looked like it would work and did not.
        var groups = _lib.Rides
            .Where(r => r.Model != null && !IsTerrain(r.Model.Path) && DefinitionFor(r.Model) != null)
            .Where(r => !(BuildCategory(r).Equals("Coasters", StringComparison.OrdinalIgnoreCase)
                          && IsCoasterPart(r.Model.Path)))
            .GroupBy(BuildCategory, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();
        var bar = _buildTabBar;
        foreach (var c in bar.GetChildren()) c.QueueFree();
        _buildTabs = groups.Select(g =>
        {
            var b = new Button { Text = $"{Title(g.Key)} ({g.Count()})",
                                 FocusMode = Control.FocusModeEnum.None };
            string key = g.Key;
            b.Pressed += () => ShowBuildCategory(key);
            bar.AddChild(b);
            return b;
        }).ToArray();
        ShowBuildCategory(_buildCategory != null && groups.Any(g => g.Key.Equals(_buildCategory, StringComparison.OrdinalIgnoreCase))
            ? _buildCategory : groups.FirstOrDefault()?.Key);
    }

    /// <summary>Whether a thing takes a queue.
    ///
    /// ⚠⚠ NOT `Info.HasQueue`. That key exists but it is 0 or absent on EVERY .sam in jungle --
    /// seven zeroes and seventy absences, not one 1 -- so reading it would have meant no ride ever
    /// queues. It says something, but not this.
    ///
    /// ⭐ The rule is the PSX's: placing a ride lays kind 4, a QUEUE, at its entrance for ride
    /// types 1, 3, 6 and 7, and kind 2, a path, for everything else. I cannot resolve those type
    /// numbers yet -- `Info.RideTypeStringIndex` indexes a table of names I have not found -- so
    /// the stand-in is DECLARING a ride type at all, which only rides and sideshows do (jungle:
    /// values 1..16 on Rides, 19/21/22 on Sideshow, none on Features, Shops or Upgrades). It also
    /// has to have an entrance for a queue to start at, which a bin does not.
    ///
    /// ⚠ So this is narrower than the game's rule in one direction and wider in another, and it
    /// is a stand-in until that table is read, not a reading.</summary>
    /// <summary>⚠ A STAND-IN, AND ONLY THE CONTROLS USE IT NOW. The live answer is the build
    /// CATEGORY (see <see cref="Placement.IsRide"/>); this picks a row out of a listing that has
    /// an entrance to aim a control at, which is all it was ever good for.</summary>
    static bool HasQueue(RideDefinition def)
        => def?.Shape != null && Park.Footprint.From(def.Shape).EntryX >= 0;

    static bool IsTerrain(string path) => path.Contains("/terrain/", StringComparison.OrdinalIgnoreCase);

    static string Title(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    /// <summary>The items of one category, for THIS park -- the catalogue holds only the open
    /// archive, so no filtering by world is needed or wanted.</summary>
    void ShowBuildCategory(string category)
    {
        if (category == null) return;
        _buildCategory = category;
        _buildList.Clear();
        _buildRows.Clear();
        for (int i = 0; i < _lib.Rides.Count; i++)
        {
            var r = _lib.Rides[i];
            if (r.Model == null || IsTerrain(r.Model.Path)
                || !BuildCategory(r).Equals(category, StringComparison.OrdinalIgnoreCase)) continue;
            var def = DefinitionFor(r.Model);
            if (def == null) continue;
            // ⚠ One folder is one coaster; its car and its pylon are not separate things to place.
            if (BuildCategory(r).Equals("Coasters", StringComparison.OrdinalIgnoreCase)
                && IsCoasterPart(r.Model.Path)) continue;
            _buildList.AddItem(DisplayName(r, def));
            _buildRows.Add(i);
        }
        Status($"{Title(category)}: {_buildRows.Count} things -- pick one, then click the park");
    }

    /// <summary>Take an item out of the menu and hold it over the park.</summary>
    void ArmFromList(int row)
    {
        if (row < 0 || row >= _buildRows.Count) return;
        var r = _lib.Rides[_buildRows[row]];
        var def = DefinitionFor(r.Model);
        if (def == null) { Status($"{Leaf(r.Name)} has no .sam beside it -- nothing to place it by"); return; }
        var fp = def.Shape != null ? Park.Footprint.From(def.Shape)
                                   : new Park.Footprint(1, 1, new[,] { { true } }, -1, -1);
        // ⭐ The CATEGORY decides whether it has a queue, and the category is where the row came
        // from -- not a field read back off the .sam.
        _place.Arm(def, DisplayName(r, def), def.Id ?? 1, fp,
                   isRide: "Rides".Equals(_buildCategory, StringComparison.OrdinalIgnoreCase));
        _armedRide = r;
        _ghostAt = (-1, -1, -1, -1);
        Status($"holding {_place.Display} ({fp.Width}x{fp.Height}) -- click to put it down, . to turn");
        GD.Print($"[build] holding {_place.Display} {fp.Width}x{fp.Height} entry {fp.EntryX},{fp.EntryY}"
               + $" exit {fp.ExitX},{fp.ExitY} facing {fp.ExitDX},{fp.ExitDY}"
               + $" shape [{string.Join("|", def.Shape ?? new string[0])}]");
    }

    /// <summary>Draw what is held, tile by tile. ⭐ Green where the park will take it, red where it
    /// will not, and the door marker on the tile the shape declares as its entrance -- the same
    /// markers the path ghost uses, which is what the console does too.</summary>
    void UpdatePlacementGhost()
    {
        if (!_place.Active || _ghostView == null) return;
        if (!CursorCell(out int x, out int y)) { _ghostView.Clear(); return; }
        if (_ghostAt == (x, y, _place.Turns, 0)) return;
        _ghostAt = (x, y, _place.Turns, 0);
        // ⭐⭐ A BLUEPRINT IS BLUE. Master: "the blueprints for all of this should be blue, not
        // green. and red when not allowed." 165 is the console's own flat blue and 175 its red --
        // the same two the path ghost has always used, so one build tool reads one way.
        // ⭐⭐ AND A LINE ALONG THE FRONT. Master: "blueprints should have a row of 170's. with the
        // line against the direction its facing." 170 is the same flat blue with a green bar down
        // one edge, so the front rank wears it and the bar is turned to sit against the outside.
        //
        // ⚠ The bar is at the BOTTOM of the texture, which is the OPPOSITE end from the one the
        // turn table names -- turn 0 puts texture-up at grid +y, so it puts the bar at grid -y.
        // The turn wanted is therefore TurnToward of the facing NEGATED, and reading the table
        // straight would have laid the line down the back of everything.
        var front = new HashSet<(int, int)>(_place.Front(x, y));
        var (fdx, fdy) = _place.Facing;
        int frontTurn = GhostMarkers.TurnToward(-fdx, -fdy);
        var cells = _place.Cells(_park, x, y)
                          .Select(c => (c.X, c.Y,
                                        c.Ok ? (front.Contains((c.X, c.Y)) ? 170 : 165) : 175,
                                        front.Contains((c.X, c.Y)) ? frontTurn : 0))
                          .ToList();
        // ⭐⭐ THE DOORS STICK OUT A TILE. Master: "the path entrance/exit needs to stick out a
        // tile from the blueprint". They mark where the queue and the path will START, which is
        // the tile OUTSIDE the shape -- drawn on the shape's own edge they said where the door is
        // rather than where you are about to be asked to build, and the footprint already shows
        // its own edge.
        void Door(( int X, int Y)? cell, int marker, bool ok)
        {
            if (cell is not { } c) return;
            var outside = _place.OutsideOf(c, x, y);
            if (outside is not { } o) return;
            cells.RemoveAll(t => t.Item1 == o.X && t.Item2 == o.Y);
            // ⚠ RED WHEN IT WILL NOT GO DOWN. The stub is part of the thing now, so it has to be
            // able to say no -- an arrow drawn over a tile the park refuses would be the only part
            // of a blocked placement still claiming to be fine.
            if (!ok) { cells.Add((o.X, o.Y, 175, 0)); return; }
            // ⭐ THE ARROW IS TURNED TO FACE THE DOOR. Both door sprites are drawn against their
            // tile's top edge with the ride on the other side of it, so the turn that puts that
            // edge against the door is the one that makes either arrow read right.
            var (dx, dy) = Placement.FacingOf(c, o);
            cells.Add((o.X, o.Y, marker, GhostMarkers.TurnToward(dx, dy)));
        }
        var stubs = _place.Stubs(_park, x, y).ToList();
        bool StubOk(bool entrance)
        {
            foreach (var t in stubs) if (t.Entrance == entrance) return t.Ok;
            return true;      // no stub of that kind at all -- nothing to refuse
        }
        // ⭐⭐ 172 IS BOTH ARROWS ON ONE TILE -- green in beside orange out, which is exactly what a
        // shop's single node IS. Master named the number and the art agrees: the two separate
        // arrows are 168 and 169, and 172 is the pair. A ride keeps them apart because its way in
        // and its way out are two different tiles.
        Door(_place.DoorFor(x, y), _place.IsRide ? 168 : 172, StubOk(true));
        Door(_place.ExitFor(x, y), 169, StubOk(false));
        _ghostView.ShowTurnedCells(cells, _park);
        bool ok = _place.Fits(_park, x, y);
        Status($"{_place.Display} at ({x},{y}) turned {_place.Turns * 90} degrees"
             + (ok ? " -- click to put it down" : " -- BLOCKED"));
    }

    /// <summary>Put it down. ⚠ Only if EVERY tile agrees, which is the console's rule for a
    /// placement press as much as for a path run.</summary>
    void PlaceHeld()
    {
        if (!_place.Active) return;
        if (!CursorCell(out int x, out int y)) { Status("that click was not over the park"); return; }
        if (!_place.Fits(_park, x, y))
        {
            Status($"{_place.Display} does not fit there");
            _toolSfx?.Play(ToolSounds.Cue.Refused);
            return;
        }
        var (cx, cy) = _place.CornerFor(x, y);
        var built = LoadPlaceable(_armedRide, out var builtAnim, out var builtMesh);
        var model = built?.Root;
        if (model == null) { Status($"{_place.Display} has no model to place"); return; }
        if (!_park.TryPlace(model, _place.Turned, _place.Id, _place.Display, cx, cy, _place.Turns))
        {
            Status($"{_place.Display} does not fit there");
            _toolSfx?.Play(ToolSounds.Cue.Refused);
            return;
        }
        // ⭐⭐ THE DOORS GO INTO THE PATH TOOL. A ground tile can only look attached to a ride if
        // its own piece carries an arm that way, and the tool cannot know where a door is unless
        // it is told -- master: "paths and queues should have the sprite as if they are connected
        // to the entry/exit points". Registered BEFORE the floor is redrawn so the first draw
        // already has them. Every ride gets a fresh id so two rides' doors never read as one.
        int ride = ++_rideSerial;
        // ⚠ CAPTURED BEFORE THE PRESS DROPS THE BLUEPRINT, and handed to the sim below: the
        // queue stub is where a guest goes to join this ride and the path stub is where the script
        // hands them back, so a ride that started without them could never take anybody.
        var stubs = _place.Stubs(_park, x, y).ToList();
        ParkCell? queueStub = stubs.Where(s => s.Entrance).Select(s => (ParkCell?)new ParkCell(s.X, s.Y)).FirstOrDefault();
        ParkCell? pathStub = stubs.Where(s => !s.Entrance).Select(s => (ParkCell?)new ParkCell(s.X, s.Y)).FirstOrDefault();
        if (_paths != null)
        {
            // ⚠ WITH THE WAY EACH ONE OPENS -- the step from the door cell to its stub. A door
            // registered without a facing linked to anything that walked past it.
            void Door(( int X, int Y)? cell, bool entrance)
            {
                if (cell is not { } c || _place.OutsideOf(c, x, y) is not { } o) return;
                _paths.AddDoor(c.X, c.Y, ride, entrance, o.X - c.X, o.Y - c.Y);
            }
            Door(_place.DoorFor(x, y), true);
            Door(_place.ExitFor(x, y), false);
            // ⭐⭐ AND THE STUBS GO DOWN WITH THE RIDE. Master: they "should always be created with
            // the ride". The entrance's is the first tile of its queue and the exit's is the first
            // tile of its path out -- so they are laid as those kinds, owned by this ride, and the
            // queue tool opens onto one that already exists rather than onto bare grass.
            //
            // ⚠ OUTSIDE ANY LEG. They belong to the ride, not to a press, so a step back through
            // the queue can never take them with it.
            foreach (var st in stubs)
                _paths.Lay(st.X, st.Y, st.Queue ? PathTool.Kind.Queue : PathTool.Kind.Path, ride);
        }
        // ⭐⭐ AND IT BUILDS ITSELF. The ride goes down on frame 0 of its Create animation -- which
        // is the UNBUILT state -- and is wound forward from there. Every ride placed so far has
        // been sitting frozen at the first frame of its own construction.
        // ⭐⭐ THE SCRIPT BUILDS IT, IF IT HAS ONE. A ride's `.rse` opens with `WAITANIM 0 0` --
        // it plays its own Create animation -- so a scripted ride is handed to the sim and nothing
        // here touches its clock again. `_building` stays for the ones with no script, which is
        // what every ride used before, and the two must never hold the same model.
        int w = _place.Turned.Width, h = _place.Turned.Height;
        if (!StartScript(ride, _armedRide, built, builtAnim, cx, cy, w, h, queueStub, pathStub, builtMesh)
            && built.Frames > 1) { built.SetFrame(0); _building.Add((built, 0f)); }
        // ⭐ The ground under it goes now that the cells are claimed.
        RefreshFloor();
        _toolSfx?.Play(ToolSounds.Cue.Lay);
        GD.Print($"[build] placed {_place.Display} at ({cx},{cy}) turned {_place.Turns * 90}");
        _ghostAt = (-1, -1, -1, -1);
        // ⭐ SHIFT STAMPS. Held, the blueprint stays on the cursor for the next one; let go, one
        // press puts one thing down and the cursor comes away empty, which is what a build tool
        // that is not being used to lay a row should do.
        if (Input.IsKeyPressed(Key.Shift))
        {
            Status($"stamped {_place.Display} at ({cx},{cy}) -- still holding it");
            return;
        }
        string was = _place.Display;
        // ⭐⭐ THE QUEUE IS THE RIDE'S, AND ONLY THE RIDE'S. A shop or a sideshow has one combined
        // node, so there is no queue to run and the tool that opens is the PATH tool, on the one
        // tile outside that node.
        bool queued = _place.IsRide;
        var entrance = _place.DoorFor(x, y);
        var exitDoor = _place.ExitFor(x, y);
        var queueFrom = queued && entrance is { } e ? _place.OutsideOf(e, x, y) : null;
        // ⭐⭐ NO TOOL FOR A SHOP. Master: "the shops, sideshows, features shouldnt have the 'path
        // building' tool open. the 1 queue tile lands where it lands." Their one node is laid with
        // them and that is the whole job -- opening a path run from it made every stall purchase
        // into a building session nobody asked for.
        _pathFrom = queued && exitDoor is { } xd ? _place.OutsideOf(xd, x, y) : null;
        _pathFacing = exitDoor is { } xd2 && _pathFrom is { } pf2
                    ? (pf2.X - xd2.X, pf2.Y - xd2.Y) : (0, 0);
        _pathOwner = ride;
        _place.Clear();
        _ghostView?.Clear();
        Status($"put {was} down at ({cx},{cy})");
        // ⭐⭐ THE CONSOLE'S ORDER: a ride with a queue drops you straight into the queue tool with
        // a run already started outside its entrance, and when that run finishes the tool hands
        // over to the PATH tool starting outside the exit. Read from the PSX build, where the
        // queue tool's press does exactly that on a ride just placed.
        if (queued && queueFrom is { } q)
        {
            OpenTool(PathTool.Kind.Queue, ride);
            _runX = q.X; _runY = q.Y;
            _runStack.Add(q);
            // ⭐ The camera follows the job, and it comes ROUND to do it. Master: "when placing a
            // ride, focus the camera on the queue stub tile", then "the camera for both of those
            // needs to rotate 180 degrees around" -- the queue hand-over turns as much as the exit
            // one does. Placing looks at the ride from the front; working its queue happens from
            // behind.
            LookAtCell(q.X, q.Y, entrance is { } e2 ? (q.X - e2.X, q.Y - e2.Y) : null);
            _ghostAt = (-1, -1, -1, -1);
            Status($"{was} is in -- run its queue from ({q.X},{q.Y})");
            GD.Print($"[build] queue mode from ({q.X},{q.Y}); the exit path will start at "
                   + $"{(_pathFrom is { } pf ? $"({pf.X},{pf.Y})" : "nowhere -- no exit on this shape")}");
        }
        else if (_pathFrom is { } only)
        {
            StartExitPath(only);
        }
    }

    /// <summary>The model for the held thing, built the same way the viewer builds any other.</summary>
    AnimatedModel LoadPlaceable(AssetLibrary.RideAssets ride) => LoadPlaceable(ride, out _, out _);
    AnimatedModel LoadPlaceable(AssetLibrary.RideAssets ride, out Aps animation) => LoadPlaceable(ride, out animation, out _);

    AnimatedModel LoadPlaceable(AssetLibrary.RideAssets ride, out Aps animation, out Model mesh)
    {
        animation = null; mesh = null;
        if (ride?.Model == null) return null;
        try
        {
            mesh = new Model(_lib.Read(ride.Model));
            Aps anim = null;
            Aps.Record rec = default;
            if (ride.Animation != null)
            {
                anim = new Aps(_lib.Read(ride.Animation));
                // ⭐⭐ SLOT 0 IS "Create" -- the animation of the thing BUILDING ITSELF, named by
                // the disc's own ride scripts (`WAITANIM ANIM_Create 0`, harvested over 352 script
                // pairs with no disagreement; see Animation.SlotNames). Master: "rides should play
                // their 'create' animation on build". Asked for by SLOT rather than taken as the
                // first record, because a thing that does not build itself -- the Super Bog is a
                // portaloo -- has nothing in slot 0 and its first record is something else
                // entirely, which would have played its Main loop as if it were a build.
                rec = anim.Records().FirstOrDefault(r => r.Slot == 0) ?? anim.Records().FirstOrDefault();
            }
            var drawn = new AnimatedModel(mesh, anim, rec, m => TextureNear(ride.Model.Path, m));
            // ⭐⭐ HANDED BACK ON ITS LAST FRAME -- the BUILT thing. The park centres a model on its
            // drawn bounds, and frame 0 of a Create animation is the ride flat-packed: the Belly
            // Bounce is an inflatable dinosaur and its first frame is the deflated heap, which is
            // nothing like the shape or the size of what ends up standing there. Centring on that
            // put the finished ride off its own plot. The caller winds it back to 0 once it has
            // been placed, so the measurement and the animation do not fight over the clock.
            drawn.SetFrame(Math.Max(drawn.Frames - 1, 0));
            animation = anim;
            return drawn;
        }
        catch (Exception e) { GD.PrintErr($"[build] {Leaf(ride.Name)} would not load: {e.Message}"); return null; }
    }

    /// <summary>What the pointer is over that would rather have the click than the path tool, or
    /// null for bare ground.
    ///
    /// ⚠⚠ IT ALWAYS RETURNS NULL TODAY, and that is not an oversight to be read as "nothing is
    /// clickable". NOTHING IN THE SCENE IS CLICKABLE YET -- no ride answers a click, no shop, no
    /// guest -- so there is nothing for it to find and inventing one would be inventing a feature.
    /// It exists as the one place that decision goes when the first of them arrives, so that
    /// "left-click opens the path tool" does not have to be unpicked from the input handler then.</summary>
    /// <summary>What the pointer is over that would rather have the click than the path tool.
    /// ⭐ A PLACED THING. Until now this returned null and the comment above it described a rule
    /// nothing implemented -- every left click went to the path tool, including one aimed squarely
    /// at a ride.</summary>
    string InteractiveUnderCursor()
        => _park != null && CursorCell(out int x, out int y) && _park.PlacedAt(x, y) is { } hit
         ? hit.Name : null;

    /// <summary>Which placed thing covers a cell, by INDEX. ⚠ Not the occupancy id: two of the
    /// same ride share that, and the pointer is over one of them.</summary>
    int PlacedIndexAt(int x, int y)
    {
        if (_park == null) return -1;
        for (int i = 0; i < _park.Placed.Count; i++)
        {
            var p = _park.Placed[i];
            int fx = x - p.X, fy = y - p.Y;
            if (fx < 0 || fy < 0 || fx >= p.Fp.Width || fy >= p.Fp.Height) continue;
            if (p.Fp.Cells[fx, fy]) return i;
        }
        return -1;
    }

    /// <summary>Follow the pointer. ⭐ The box is drawn for whatever is under it, and falls back to
    /// the SELECTION when the pointer is over nothing -- so a selected ride keeps its outline
    /// while you point elsewhere, and hovering another shows that one instead.</summary>
    /// <summary>What the pointer is over, by RAY AGAINST THE MODEL rather than by the cell under
    /// it. ⭐ Master: "the hover should be anywhere on the thing's model". A ride's mesh overhangs
    /// its footprint and stands well above it, so picking by the ground cell means the top of a
    /// tall ride -- the part you are actually looking at -- picks whatever cell happens to be
    /// behind it.
    ///
    /// ⚠ NEAREST HIT, not first: boxes overlap on screen and the one in front is the one meant.
    /// ⚠ And the cell route is kept for the controls, which have no mouse to cast from.</summary>
    int PointedAt()
    {
        if (_park == null) return -1;
        if (_cursorOverride is { } fixedCell) return PlacedIndexAt(fixedCell.X, fixedCell.Y);
        if (_cam == null) return -1;
        var mouse = GetViewport().GetMousePosition();
        if (_panel != null && _panel.Visible && mouse.X < PanelW) return -1;
        if (_buildBox != null && _buildBox.Visible && mouse.X > GetViewport().GetVisibleRect().Size.X - BuildW)
            return -1;
        var from = _cam.ProjectRayOrigin(mouse);
        var dir = _cam.ProjectRayNormal(mouse);
        int best = -1; float near = float.MaxValue;
        for (int i = 0; i < _park.Placed.Count; i++)
        {
            var node = _park.Placed[i].Node;
            if (node == null || !IsInstanceValid(node)) continue;
            var (lo, hi) = Park.DrawnBounds(node, inParent: true);
            if (RayHitsBox(from, dir, lo, hi, out float t) && t < near) { near = t; best = i; }
        }
        return best;
    }

    /// <summary>Slab test. ⚠ A zero component of the direction is handled by the infinities falling
    /// out of the division rather than by a branch -- the branch is where this is usually wrong,
    /// because a ray exactly along an axis is the common case for a camera looking down one.</summary>
    static bool RayHitsBox(Vector3 from, Vector3 dir, Vector3 lo, Vector3 hi, out float t)
    {
        float t0 = 0f, t1 = float.MaxValue;
        for (int a = 0; a < 3; a++)
        {
            float o = from[a], d = dir[a];
            float aLo = (lo[a] - o) / d, aHi = (hi[a] - o) / d;
            if (aLo > aHi) (aLo, aHi) = (aHi, aLo);
            t0 = Mathf.Max(t0, aLo); t1 = Mathf.Min(t1, aHi);
            if (t0 > t1 || float.IsNaN(t0) || float.IsNaN(t1)) { t = 0f; return false; }
        }
        t = t0;
        return true;
    }

    void UpdateHover()
    {
        int was = _hovered;
        // ⭐⭐ NOT WHILE SOMETHING ELSE OWNS THE CURSOR. Master: "selection boxes shouldnt show
        // with path or a blueprint on ur cursor." Both of those draw their own ghost on the tile
        // under the pointer, and a selection outline on top of it is two answers to one question.
        bool busy = _toolOpen || _place.Active;
        _hovered = busy ? -1 : PointedAt();
        int want = busy ? -1 : (_hovered >= 0 ? _hovered : _selected);
        if (_hovered != was || want != _shownBox) { _shownBox = want; ShowBoxFor(want); }
        // ⭐ The gate's zone follows the same rule as a ride's box: the pointer first, a click
        // second, and NEITHER while a tool or blueprint owns the cursor.
        UpdateGateBox(busy);
    }

    /// <summary>The gate zone's world box, for the hover test. ⚠ Null until a gate is placed;
    /// the no-build zone only exists once <see cref="PlaceGateNoBuild"/> has read one.</summary>
    (Vector3 Lo, Vector3 Hi)? _gateBounds;
    /// <summary>The zone's middle cell, for click-to-focus.</summary>
    (int X, int Y)? _gateCell;
    /// <summary>The gate has been clicked. ⭐ Separate from <see cref="_selected"/>, which is an
    /// index into `Park.Placed` -- the gate is not a placed ride and has no index.</summary>
    bool _gateSelected;

    /// <summary>Which index the box is currently drawn for, so it is not rebuilt every frame.</summary>
    int _shownBox = -1;

    void ShowBoxFor(int at)
    {
        if (_selectView == null) return;
        if (at < 0 || _park == null || at >= _park.Placed.Count) { _selectView.Hide(); return; }
        var sel = _park.Placed[at];
        // ⭐ The box the game draws is a WORLD BOX, so it is given one. X and Z come from the
        // footprint's own world rectangle (min and max, not grid order -- the plot mirrors), and
        // the height from the model, because a footprint has none.
        var c0 = _park.CellCorner(sel.X, sel.Y);
        var c1 = _park.CellCorner(sel.X + sel.Fp.Width, sel.Y + sel.Fp.Height);
        float bx0 = Mathf.Min(c0.X, c1.X), bx1 = Mathf.Max(c0.X, c1.X);
        float bz0 = Mathf.Min(c0.Z, c1.Z), bz1 = Mathf.Max(c0.Z, c1.Z);
        float top = _park.BaseY + Mathf.Max(bx1 - bx0, bz1 - bz0);
        if (sel.Node != null && IsInstanceValid(sel.Node))
            top = Mathf.Max(Park.DrawnBounds(sel.Node, inParent: true).Max.Y, _park.BaseY + 1f);
        var min = new Vector3(bx0, _park.BaseY, bz0);
        var size = new Vector3(bx1 - bx0, top - _park.BaseY, bz1 - bz0);
        _selectView.Show(min, size);
    }

    /// <summary>Take whatever the pointer is over as the SELECTION, and send the camera to it.
    /// Master: "wire up selecting with lmb, which focuses the camera on the selected thing."
    /// Returns true when something was taken.</summary>
    bool SelectUnderCursor()
    {
        UpdateHover();
        // ⭐ The gate takes the click when the pointer is on it and nothing else is, so selecting
        // it is the same gesture as selecting a ride.
        if (_hovered < 0 && PointingAtGate())
        {
            // ⚠⚠ ONE SELECTION AT A TIME. Master: "make sure gate selection boxes and other
            // selection boxes cant exist at the same time." `_gateSelected` and `_selected` were
            // independent, so selecting a ride and then the gate left BOTH boxes drawn -- two
            // answers to "what is selected", which is the same mistake the tool/blueprint rule
            // already exists to prevent.
            _selected = -1;
            _shownBox = -1;
            ShowBoxFor(-1);
            _gateSelected = true;
            UpdateGateBox(false);
            // ⭐ AND IT FOCUSES, like a ride does. Master: "give gate the click-to-focus and move
            // away to unfocus like rides get." The camera goes to the zone's own cells, so the
            // gesture is the same one selecting anything else in the park already is.
            if (_gateCell is { } gc) LookAtCell(gc.X, gc.Y);
            GD.Print("[select] the gate's no-build zone -- camera to it");
            Status("gate selected -- move away or right click to clear");
            return true;
        }
        // ⭐ And taking a ride drops the gate, the other half of the same rule.
        if (_gateSelected) { _gateSelected = false; UpdateGateBox(false); }
        if (_hovered < 0) { ClearSelection(); return false; }
        _selected = _hovered;
        var sel = _park.Placed[_selected];
        ShowBoxFor(_selected);
        // ⚠ The camera is moved by GLIDING THE CURSOR, which is also what WASD does -- so the
        // clear-on-move below is driven by the KEYS, not by the cursor having moved. Otherwise
        // focusing on a selection would immediately drop it.
        LookAtCell(sel.X + sel.Fp.Width / 2, sel.Y + sel.Fp.Height / 2);
        GD.Print($"[select] {sel.Name} at ({sel.X},{sel.Y}) {sel.Fp.Width}x{sel.Fp.Height} -- camera to it");
        Status($"{sel.Name} selected -- right click to clear");
        return true;
    }

    /// <summary>Is the pointer over the gate's no-build zone? ⚠ Its own ray test, because the
    /// gate is not in `Park.Placed` and so `PointedAt` cannot see it.</summary>
    bool PointingAtGate()
    {
        if (_gateBounds is not { } b || _cam == null || _mode != Mode.Park) return false;
        var m = GetViewport().GetMousePosition();
        return RayHitsBox(_cam.ProjectRayOrigin(m), _cam.ProjectRayNormal(m), b.Lo, b.Hi, out _);
    }

    /// <summary>Show the gate's zone only while it is pointed at or selected -- master's rule.</summary>
    void UpdateGateBox(bool busy)
    {
        if (_gateBox?.Root == null) return;
        bool show = !busy && _mode == Mode.Park && _gateBounds != null
                 && (_gateSelected || PointingAtGate());
        if (_gateBox.Root.Visible != show) _gateBox.Root.Visible = show;
    }

    void ClearSelection()
    {
        if (_selected >= 0 || _gateSelected) { GD.Print("[select] nothing selected"); Status("nothing selected"); }
        _selected = -1;
        _gateSelected = false;
        UpdateGateBox(false);
        _shownBox = _hovered;
        ShowBoxFor(_hovered);
    }

    /// <summary>A press of the open tool. The first starts a run, the second lays it -- and ⭐ the
    /// run CARRIES ON from where it ended, which is what makes a path drawn in legs rather than
    /// one click per tile.</summary>
    void PressTool()
    {
        if (!_toolOpen || _paths == null || _ghost == null) return;
        if (!CursorCell(out int x, out int y))
        {
            GD.Print("[path] the cursor is off the plot");
            Status("that click was not over the park");
            _toolSfx?.Play(ToolSounds.Cue.Refused);
            return;
        }
        if (_runX < 0)
        {
            // ⭐ A RUN CANNOT START ON A RED TILE. The ghost already says whether the cell under
            // the pointer would be refused, so the start is judged by THAT rather than by a second
            // rule written here -- one tile, one verdict, the same one the marker is drawn from.
            _ghost.Set(x, y, x, y, _toolKind);
            if (!_ghost.Layable)
            {
                GD.Print($"[path] cannot start at ({x},{y}): {_paths.Describe(x, y)}");
                Status($"cannot start on ({x},{y}) -- that tile is blocked");
                _toolSfx?.Play(ToolSounds.Cue.Refused);
                return;
            }
            _runX = x; _runY = y;
            _runStack.Add((x, y));
            _ghostAt = (-1, -1, -1, -1);
            GD.Print($"[path] run starts at ({x},{y})");
            Status($"run starts at ({x},{y}) -- click again to lay it");
            _toolSfx?.Play(ToolSounds.Cue.Start);
            return;
        }
        _ghost.Set(_runX, _runY, x, y, _toolKind);
        if (!_ghost.Layable)
        {
            // ⚠ Refused is not an error to swallow: say WHICH tile stopped it, because the whole
            // run goes red after the first one and the picture alone cannot tell you which.
            var bad = _ghost.Tiles.FirstOrDefault(t => t.Verdict == PathGhost.Verdict.Refused);
            GD.Print($"[path] refused at ({bad.X},{bad.Y}): {_paths.Describe(bad.X, bad.Y)}");
            Status($"blocked at ({bad.X},{bad.Y}) -- nothing laid");
            _toolSfx?.Play(ToolSounds.Cue.Refused);
            return;
        }
        // ⭐ Does this run END ON something it joins? Read BEFORE laying, while the verdicts
        // still describe the ground the run was drawn over.
        var last = _ghost.Tiles[^1];
        bool joined = last.Verdict is PathGhost.Verdict.Joins or PathGhost.Verdict.Already;
        bool single = _ghost.Tiles.Count == 1;
        _paths.BeginLeg();
        int laid = _ghost.Lay(_toolKind, _toolOwner);
        _runStack.Add(_ghost.End);
        RefreshFloor();
        // ⭐ ONE TILE IS A WHOLE JOB. A run of a single tile is somebody dropping one piece, not
        // starting a line, so it lays and the tool shuts -- master's call. ⚠ Shift keeps it open,
        // the same modifier that keeps it open on a connection and that stamps a blueprint, so
        // there is one "I am still building" key rather than three.
        if (single && !Input.IsKeyPressed(Key.Shift))
        {
            _toolSfx?.Play(ToolSounds.Cue.Lay);
            // ⭐ ONE TILE CAN STILL BE A CONNECTION. Master: "if the one tile is a link, play the
            // link sound still". The short-circuit for a single tile was returning before the
            // joined branch below could say so, which took the cue away from exactly the case it
            // is for -- dropping one piece into a gap is the most connecting thing the tool does.
            if (joined) _toolSfx?.Play(ToolSounds.Cue.Connect);
            GD.Print($"[path] laid one tile at ({x},{y}){(joined ? " -- a connection" : "")} -- tool closed");
            Status($"laid one tile at ({x},{y})");
            CloseTool();
            return;
        }
        // ⭐⭐ A RUN THAT CONNECTS CLOSES THE TOOL. Master's call, and the PSX report has the same
        // rule from the other build -- its path tool closes itself when a run finishes on existing
        // path, and gives that case its own sound and its own ghost marker. Finishing a path is a
        // finished job, so the tool does not sit open waiting for a press nobody meant.
        // ⭐ SHIFT KEEPS IT OPEN. Finishing a path is normally a finished job, so the tool shuts
        // -- but holding shift says "I am still building", and then the run simply carries on from
        // the tile it joined.
        if (joined && !Input.IsKeyPressed(Key.Shift))
        {
            CloseTool();
            GD.Print($"[path] laid {laid} of {_ghost.Tiles.Count}; joined at ({last.X},{last.Y}) "
                   + $"-- tool closed; {_paths.Laid} total");
            Status($"laid {laid}, joined at ({last.X},{last.Y}) -- tool closed");
            // ⭐ BOTH. The connect sound is layered ON TOP of the lay sound in the game, on its own
            // voice, so neither cuts the other -- it is the success case of laying a run, not a
            // replacement for it.
            _toolSfx?.Play(ToolSounds.Cue.Lay);
            _toolSfx?.Play(ToolSounds.Cue.Connect);
            return;
        }
        if (joined)
        {
            (_runX, _runY) = _ghost.End;
            _ghostAt = (-1, -1, -1, -1);
            GD.Print($"[path] laid {laid}; joined at ({last.X},{last.Y}) -- shift held, still open");
            Status($"joined at ({last.X},{last.Y}) -- shift held, the run goes on");
            _toolSfx?.Play(ToolSounds.Cue.Lay);
            _toolSfx?.Play(ToolSounds.Cue.Connect);
            return;
        }
        // ⚠ From the SEGMENT'S END, not from the cursor. The cursor is snapped to one axis, so
        // carrying on from where the mouse was would put the next segment's start off the end of
        // the one just laid -- by however far the cursor had drifted off the line.
        (_runX, _runY) = _ghost.End;
        _ghostAt = (-1, -1, -1, -1);
        GD.Print($"[path] laid {laid} of {_ghost.Tiles.Count}; the run goes on from ({_runX},{_runY}); {_paths.Laid} total");
        Status($"laid {laid} -- the run goes on from ({_runX},{_runY})");
        _toolSfx?.Play(ToolSounds.Cue.Lay);
    }

    /// <summary>⭐ WHAT THE SIM WALKS ON, drawn on the park. Master asked to see the routes.
    ///
    /// Blue is the entrance the park came with -- bus stop, road, turnstiles -- which is MESH the
    /// prefab brings, not tiles: the authored grid ships no path at all. Green is laid path and
    /// amber a queue.
    ///
    /// ⚠⚠ DRAWN AT THE VIEWER'S CELLS, and in three worlds of four that is NOT where the sim
    /// thinks they are. ParkPaths takes the grid's origin from the `heightfield` marker and the
    /// viewer takes it through Park.FindHole; jungle agrees, space is 10 out in z and hallow is
    /// off entirely. So in jungle this overlay is the truth and elsewhere the offset you can see
    /// IS the bug -- which is more use than quietly drawing it in the right place and hiding it.</summary>
    void ToggleWalkOverlay()
    {
        if (_walkView != null && IsInstanceValid(_walkView))
        { _walkView.QueueFree(); _walkView = null; GD.Print("[walk] overlay off"); return; }
        var f = _park?.Field;
        if (f == null || _terrainModel == null) { GD.PrintErr("[walk] no park"); return; }
        if (WalkGrid() == null) { GD.PrintErr("[walk] no sim grid"); return; }

        var by = new Dictionary<int, SurfaceTool>();
        int ent = 0, path = 0, queue = 0;
        float half = Park.CellSize * 0.5f;
        // ⚠⚠ EACH SET IN THE FRAME IT ACTUALLY LIVES IN. There are THREE origins in this park and
        // they do not agree: ParkPaths takes one off the `heightfield` marker, the viewer's
        // _holeOrigin comes out of Park.FindHole, and Park.CellCentre draws from PlotSpace. In
        // JUNGLE the first two match each other and PlotSpace is EIGHT cells away from both -- so
        // the entrance drawn at CellCentre landed past the gate and off the plot, and read as not
        // drawn at all. Until that is settled, the entrance is drawn where ParkPaths means it
        // (Origin + cell, un-mirrored into world Z) and laid path where the build grid means it,
        // so each lands on the thing it is talking about.
        void Cell(Vector3 c, int layer)
        {
            if (!by.TryGetValue(layer, out var st))
            { st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles); by[layer] = st; }
            // ⚠⚠ THE PLOT'S FLOOR IS FLAT AND THE ENTRANCE IS NOT. The bus stop and the road sit on
            // an embankment well above the plot's base height, so an overlay drawn at CellY was
            // BURIED under the road -- the whole entrance set was being drawn and none of it could
            // be seen, which reads exactly like not being drawn at all. The baked per-tile ground
            // is the same lookup the game camera uses, so the overlay rides whatever is there.
            float ground = GroundAt(Mathf.FloorToInt(c.X), Mathf.FloorToInt(c.Z)) / (float)GameCamera.TileUnits;
            float h = Mathf.Max(c.Y, ground) + Park.CellSize * (0.05f + layer * 0.005f);
            var a = new Vector3(c.X - half, h, c.Z - half);
            var b2 = new Vector3(c.X + half, h, c.Z - half);
            var d = new Vector3(c.X + half, h, c.Z + half);
            var e = new Vector3(c.X - half, h, c.Z + half);
            foreach (var v in new[] { a, b2, d, a, d, e }) { st.SetNormal(Vector3.Up); st.AddVertex(v); }
        }
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                var kind = _paths?.KindAt(x, y) ?? PathTool.Kind.None;
                if (kind is PathTool.Kind.Queue) { Cell(_park.CellCentre(x, y), 2); queue++; }
                else if (kind is PathTool.Kind.Path or PathTool.Kind.Both) { Cell(_park.CellCentre(x, y), 1); path++; }
            }
        // ⭐ The entrance at the plot's own cells. ⚠ MINUS on Z: ParkPaths.Origin is in the
        // terrain MODEL's space and the scene mirrors Z, so a cell's world position is
        // -(Origin.Y + cellZ). This used to read `+` and agree with the picture, because the row
        // order was reversed at the other end too and the two errors cancelled -- the blue landed
        // on the turnstiles for the wrong reason.
        foreach (var c in _walkGrid.EntranceCells)
        {
            Cell(new Vector3(_walkGrid.Origin.X + c.X + 0.5f, _park.BaseY,
                             -(_walkGrid.Origin.Y + c.Z + 0.5f)), 0);
            ent++;
        }
        if (by.Count == 0) { GD.Print("[walk] nothing walkable to draw"); return; }

        _walkView = new MeshInstance3D { Name = "walkable", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        var colours = new[] { new Color(0.25f, 0.6f, 1f, 0.45f),      // the entrance, blue
                              new Color(0.3f, 0.95f, 0.35f, 0.45f),   // laid path, green
                              new Color(1f, 0.75f, 0.2f, 0.5f) };     // a queue, amber
        var mesh = new ArrayMesh();
        int surface = 0;
        foreach (var (layer, st) in by.OrderBy(kv => kv.Key))
        {
            var m = st.Commit();
            if (m == null || m.GetSurfaceCount() == 0) continue;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, m.SurfaceGetArrays(0));
            mesh.SurfaceSetMaterial(surface++, new StandardMaterial3D
            {
                AlbedoColor = colours[layer],
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            });
        }
        _walkView.Mesh = mesh;
        AddChild(_walkView);
        GD.Print($"[walk] overlay on: {ent} entrance (blue), {path} path (green), {queue} queue (amber)");
        Status($"walkable: {ent} entrance, {path} path, {queue} queue -- N to hide");
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
    /// answer.
    ///
    /// ⚠⚠ AND THE CROSSING LIGHTS ARE NOT PART OF IT, though they used to be placed here.
    /// `/Features/Lights/lights.mps` sits in every archive next to the gate and I stood it beside
    /// the gate on the strength of that. Master, who has the retail game in front of them: "the
    /// lights DONT ship in the final game". ON THE DISC IS NOT IN THE GAME -- the archives carry
    /// a pile of features the console never places (Speaker1-4, Statue1/2, Ferry, SeaPlane), and
    /// a file existing is not evidence that anything draws it. Removed; do not re-derive it from
    /// the file's existence a second time.</summary>
    void LoadGate()
    {
        // ⭐ The gate's by-eye correction is seeded per world so it follows whichever archive is
        // open rather than being typed in again each session.
        //
        // ⚠⚠ ONLY WHEN THE WORLD CHANGES. The nudge tool calls LoadGate() to redraw after every
        // keypress, so re-seeding unconditionally here would overwrite master's nudge the instant
        // they made it and the tool would do NOTHING -- silently, and looking fine. Caught before
        // shipping by reading the call order rather than the line.
        var wn = (_lib?.WadName ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        int wi = Array.FindIndex(wn, x => x.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase));
        string world = wi >= 0 ? wn[wi][..^4] : "";
        if (!string.Equals(world, _gateNudgeWorld, StringComparison.OrdinalIgnoreCase))
        {
            _gateNudgeWorld = world;
            _gateNudge = GateZError;
        }
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
        if (!TerrainBounds("ticket_booths", out var booths))
        { GD.PrintErr("[gate] no ticket_booths -- cannot find this park's entrance axis"); return; }
        float shift = booths.Position.X + booths.Size.X * 0.5f - AuthoredX;

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
            // ⚠ Measured, not trusted -- the bounds below are what the log prints against the
            // zone, and that comparison is the whole check.
            var (lo, hi) = Park.DrawnBounds(_gate.Root, inParent: true);
            // ⭐⭐ THE GATE NEEDS NO Z AT ALL, AND THE .SAM SAYS SO. `EngineMapOffsetOverride`
            // + `EngineFootprint*Override` IS the gate's own authored rectangle, measured against
            // the four meshes:
            //
            //   world     gate authored z      .sam zone z   booths z        road ends
            //   JUNGLE    17.00 .. 18.50       16 .. 19      14.88..16.12    18.90
            //   HALLOW    17.00 .. 18.60       16 .. 19      14.88..16.12    18.90
            //   SPACE     17.00 .. 19.00       15 .. 19      14.88..16.12    18.90
            //   FANTASY   16.70 .. 20.71       16 .. 21      14.88..16.12    18.90
            //
            // Every gate is authored x 45..51 and every .sam says offset 45 width 6 -- EXACT, four
            // times. Every gate's z sits inside its own zone, with the zone's HEIGHT varying per
            // world exactly enough to contain it (5 for Fantasy's 4.01-deep arch, 3 for Jungle's
            // 1.50). And the zone is the gap between the ticket booths' back (16.12) and the end
            // of A_ROAD (18.90), both identical in all four parks. Four worlds, three files.
            //
            // ⭐ THE CONTROL IS FREE: HALLOW and SPACE have their booths centred on 48, so their
            // shift is ZERO. If the authored position is the real one they must land correctly
            // with nothing moved on either axis -- and they do.
            //
            // ⚠⚠ SO WHERE DID -2.29 COME FROM? `gatebase01`, and it is not the gate's base.
            // Fantasy's pad is x 37..43, z 19..23. Its x matches the SHIFTED gate exactly, which
            // is what made it look like the gate's own pad -- but z 19 is `zEnd`, the first row of
            // the PARK, and the entrance's own starting path runs x 39..40, z 19..24 straight
            // over it. The pad is the paving under the path INSIDE the park, in front of the
            // gate. Centring the gate on it dragged every gate 2.29 units into the park, which is
            // the "couple of tiles" master kept seeing and kept nudging back.
            //
            // ⚠ And ff8c76e's `MapOffsetY + FootprintHeight` put it in the sea, off ONE number
            // agreeing ONCE: Fantasy's 16 + 5 = 21 against that same pad's centre. n = 1 is not a
            // control. The table above is what a control looks like.
            float dz = _gateNudge;
            _gate.Root.Position += new Vector3(shift, 0f, dz);
            _gate.Root.Visible = _mode == Mode.Park;
            GD.Print($"[gate] {ride.Name}: authored x {lo.X:F2}..{hi.X:F2}  y {lo.Y:F2}..{hi.Y:F2}  "
                   + $"z {lo.Z:F2}..{hi.Z:F2}; shifted {shift:+0.0;-0.0;0} x onto this park's "
                   + "entrance, and NOTHING in z"
                   + (_gateNudge != 0f ? $"  [nudged {_gateNudge:+0.00;-0.00} by hand]" : "")
                   + $"\n[gate] front edge z={hi.Z + dz:F2} -- the road ends at -18.90 and the "
                   + $"booths' back is -16.12, in every park");
            PlaceGateNoBuild(def, lo, hi, shift, dz);
        }
        catch (Exception ex) { GD.PrintErr($"[gate] {ride.Model.Path}: {ex.Message}"); }
    }

    /// <summary>The gate's no-build zone -- the rectangle the park keeps clear around its
    /// entrance -- reserved so nothing can be built in it, and drawn with the game's own box.
    ///
    /// ⭐⭐ THE SIZE IS THE .SAM'S, AND IT IS THE ONE THING IN THERE MASTER HAS STATED
    /// OUTRIGHT: "the footprint height/width is the width of the no-build zone the gate creates
    /// around it". `Info.EngineFootprintWidthOverride` / `HeightOverride`, which ship per world:
    ///
    ///   JUNGLE 6x3 · HALLOW 6x3 · SPACE 6x4 · FANTASY 6x5
    ///
    /// ⭐ AND THE WIDTH IS SIX IN ALL FOUR, which is the gate's own authored width -- the
    /// meshes stand across x 45..51 and `Info.EngineMapOffsetOverrideX` is 45 in all four .sams.
    /// Two files that know nothing of each other giving the same six cells at the same column is
    /// what makes this a reading rather than a hope.
    ///
    /// ⭐⭐ AND THE COLUMN IS IN THE **AUTHORED** FRAME, NOT THE PARK GRID, WHICH IS
    /// SETTLED BY A CONTROL. The entrance table gives each park its own walkway column -- JUNGLE
    /// 29, SPACE 43 and 47, HALLOW 35 and 47, FANTASY 37 and 39 -- while `Gates.sam` ships ONCE
    /// PER WORLD and says 45 every time. One number cannot be two parks' grid columns, so 45 is
    /// the frame the gate is drawn in and the runtime moves it to the park, exactly as the mesh
    /// is moved. The same `shift` is therefore applied here.
    ///
    /// ⭐ THE ROW IS NOT SHIFTED, because the entrance table states in its own data that only
    /// x moves: `ZRow` is 6 and `ZEnd` is 19 in every filled entry of all twelve. And it lands
    /// where that says it should -- `MapOffsetY + Height` is 19 for JUNGLE, HALLOW and SPACE,
    /// and 19 is the first row of the park at the entrance (`ZEnd`). Three worlds, two tables,
    /// one number. FANTASY is 21, two rows further in, and is the odd one out.
    ///
    /// ⚠⚠ AND IT STILL DOES NOT PLACE THE MESH. ff8c76e added `MapOffsetY + Height` to
    /// the gate's z and put it in the sea. The zone is drawn FROM the data and the gate is placed
    /// by its own measured bounds, so the two are independent -- if the log below shows them
    /// disagreeing, that is the gate's placement being measured, not the zone being wrong.</summary>
    void PlaceGateNoBuild(RideDefinition def, Vector3 lo, Vector3 hi, float shift, float dz)
    {
        _gateBox?.Hide();
        _park?.ClearReservations();
        if (_park == null || _park.Width <= 0) return;
        int w = def?.NoBuildWidthOverride ?? 0, h = def?.NoBuildHeightOverride ?? 0;
        if (w <= 0 || h <= 0)
        {
            GD.Print("[gate.zone] this Gates.sam states no EngineFootprint override -- no zone");
            return;
        }
        int x0 = (def.MapOffsetX ?? 0) + Mathf.RoundToInt(shift), y0 = def.MapOffsetY ?? 0;
        int cells = _park.Reserve(x0, y0, w, h);
        var (centre, rw, rh) = FootprintRect(x0, y0, w, h);
        // ⚠ Tall enough to enclose the arch: a flat ring on the floor is not what the console
        // draws, and the box's own shape (a pulled-out cube) only reads as one at height.
        float tall = Mathf.Max(1f, hi.Y - lo.Y);
        // ⭐⭐ THE BOX MOVES WITH THE GATE. Master: "move the selection box of the gate to
        // actually match the gate's new position." The zone's cells are READ from the .sam and
        // its x already carries `shift`, but the gate itself is then nudged along z -- so the box
        // sat where the zone was authored while the arch stood somewhere else, and the log has
        // been printing that gap as "off by" for as long as the nudge has existed. Reporting a
        // mismatch is not the same as not having one.
        // ⚠ The RESERVATION is untouched: which cells refuse a build is authored data, and only
        // the drawn box follows the eye-tuned gate.
        var boxAt = new Vector3(centre.X - rw * 0.5f, centre.Y, centre.Z - rh * 0.5f + dz);
        var boxSize = new Vector3(rw, tall, rh);
        _gateBox.Show(boxAt, boxSize);
        // ⭐ THE ZONE IS NOW A SELECTION, NOT A PERMANENT FIXTURE. Master: "the selection box
        // should only be with the gate hovered/selected." It was drawn for the whole time the
        // park was open, which made a build restriction look like scenery.
        _gateBounds = (boxAt, boxAt + boxSize);
        _gateCell = (x0 + w / 2, y0 + h / 2);
        _gateBox.Root.Visible = false;
        float gx = (lo.X + hi.X) * 0.5f + shift, gz = (lo.Z + hi.Z) * 0.5f + dz;
        GD.Print($"[gate.zone] {w}x{h} cells at grid ({x0},{y0}) -- .sam offset ({def.MapOffsetX},"
               + $"{def.MapOffsetY}) shifted {Mathf.RoundToInt(shift):+0;-0;0} in x\n"
               + $"[gate.zone] world x {centre.X - rw * 0.5f:F2}..{centre.X + rw * 0.5f:F2}  "
               + $"z {centre.Z - rh * 0.5f:F2}..{centre.Z + rh * 0.5f:F2}; {cells} of {w * h} cells "
               + "are on the plot and now refuse a build"
               + (cells == w * h ? "\n" : $"; the other {w * h - cells} are off it, on the walkway\n")
               + $"[gate.zone] the gate itself is centred ({gx:F2}, {gz:F2}); the zone is centred "
               + $"({centre.X:F2}, {centre.Z:F2}) -- off by ({gx - centre.X:+0.00;-0.00;0}, "
               + $"{gz - centre.Z:+0.00;-0.00;0}). The zone is READ, the gate is TUNED.");
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
        // ⚠⚠ THE ROW ORDER, which this line did not apply. Park.Build maps row y to world
        // `Origin.Y + (H - y - 0.5)`; subtracting Origin.Y alone counts from the far end, so the
        // booths came out at cell 61 of 76 instead of 15 and every dump below it was of the wrong
        // end of the park.
        int bz = f.Height - Mathf.RoundToInt(booths.Position.Z + booths.Size.Z * 0.5f - _holeOrigin.Y);
        GD.Print($"[skip] booths at cell ({bx},{bz}) of {f.Width}x{f.Height}; "
               + "'.' drawn, '#' skipped, rows are z increasing (out of the park)");
        for (int z = bz - 4; z <= bz + 12; z++)
        {
            if (z < 0 || z >= f.Height) continue;
            var row = new System.Text.StringBuilder();
            for (int x = bx - 8; x <= bx + 8; x++)
                row.Append(x < 0 || x >= f.Width ? ' ' : f.Drawn(x, z) ? '.' : '#');
            GD.Print($"[skip] z={z,3} (world {_holeOrigin.Y + (f.Height - z),7:F1})  {row}");
        }
    }

    /// <summary>⭐ IS `Info.RideTypeStringIndex` AN INDEX INTO THE TEXT DATABASE? Master wants
    /// track and coasters as their own build categories, and the archive folders do not separate
    /// them -- everything with a track is filed under Rides. The .sam files declare a ride type
    /// NUMBER (jungle: 1..16 on Rides, 19/21/22 on Sideshow) and its name says it indexes a table
    /// of strings, which has never been located.
    ///
    /// ⚠ The obvious candidate is the localisation database, so this asks it directly and prints
    /// what comes back. A row that reads like a ride type answers the question; a row that reads
    /// like anything else answers it just as well, which is why the rows are printed rather than
    /// a verdict.</summary>
    void TypeAudit()
    {
        if (_text == null) { GD.PrintErr("[type] no text database loaded"); return; }
        GD.Print($"[type] {_text.Keys.Length} rows in {_text.Locale}; keys mentioning a ride type:");
        for (int i = 0; i < _text.Keys.Length; i++)
        {
            var k = _text.Keys[i];
            if (k == null) continue;
            if (!k.Contains("RIDETYPE", StringComparison.OrdinalIgnoreCase)
             && !k.Contains("COASTER", StringComparison.OrdinalIgnoreCase)
             && !k.Contains("TRACK", StringComparison.OrdinalIgnoreCase)) continue;
            GD.Print($"[type]   {i,4} {k} = \"{_text.Text("eng", i)}\"");
        }
        GD.Print("[type] the first 26 rows, in case the index is simply the row:");
        for (int i = 0; i < 26 && i < _text.Keys.Length; i++)
            GD.Print($"[type]   {i,4} {_text.Keys[i]} = \"{_text.Text("eng", i)}\"");

        // And what the .sam files actually declare, per category.
        var byType = new Dictionary<int, List<string>>();
        foreach (var r in _lib.Rides)
        {
            if (r.Model == null || IsTerrain(r.Model.Path)) continue;
            var def = DefinitionFor(r.Model);
            if (def?.Int("Info.RideTypeStringIndex") is not { } t) continue;
            if (!byType.TryGetValue(t, out var list)) byType[t] = list = new List<string>();
            list.Add($"{Category(r.Name)}/{def.Name ?? Leaf(r.Name)}");
        }
        // ⚠⚠ AND THE ANSWER, WHICHEVER WAY IT FALLS. If the rows above read like ride types the
        // index is the text database; if they read like "Salt" and "Edit Track" it is not, and the
        // next place to look is what the .sam files themselves say. So both are printed.
        //
        // ⭐ The Rides folder visibly holds coaster PARTS as well as rides -- coaster1, croccar,
        // StdPylon show in the build list under their filenames because they declare no name. What
        // separates them from Dizzy Dinos is what a category split has to be built on, so the key
        // sets of one of each are printed side by side.
        var keysOf = new Dictionary<string, string[]>();
        foreach (var want in new[] { "coaster1", "croccar", "stdpylon", "dizzy", "bellybounce" })
        {
            var hit = _lib.Rides.FirstOrDefault(r => r.Model != null && !IsTerrain(r.Model.Path)
                        && r.Model.Path.Contains(want, StringComparison.OrdinalIgnoreCase));
            if (hit == null) continue;
            var def = DefinitionFor(hit.Model);
            if (def == null) continue;
            keysOf[want] = def.Fields.Keys.Concat(def.Blocks.Keys).OrderBy(k => k).ToArray();
            var parts2 = def.Source.Split('/', StringSplitOptions.RemoveEmptyEntries);
            int wi2 = Array.FindIndex(parts2, x => x.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase));
            string key2 = wi2 < 0 ? "(no WAD in source)"
                : TextDatabase.GraphicsKey(parts2[wi2][..^4], string.Join('/', parts2.Skip(wi2 + 1)));
            GD.Print($"[type] {want,-12} source={def.Source}");
            GD.Print($"[type] {want,-12} key={key2} -> row {_text.IndexOf(key2)} = \"{(_text.IndexOf(key2) >= 0 ? _text.Text("eng", _text.IndexOf(key2)) : "-")}\"");
            GD.Print($"[type] {want,-12} name={def.Name ?? "(none)"} id={def.Id?.ToString() ?? "-"} "
                   + $"type={def.Int("Info.RideTypeStringIndex")?.ToString() ?? "-"} "
                   + $"shape={(def.Shape == null ? "none" : $"{def.Shape.Max(r => r.Length)}x{def.Shape.Length}")} "
                   + $"{def.Fields.Count + def.Blocks.Count} keys");
        }
        if (keysOf.TryGetValue("coaster1", out var ck) && keysOf.TryGetValue("dizzy", out var dk))
        {
            GD.Print($"[type] keys coaster1 has and Dizzy Dinos does not: {string.Join(" ", ck.Except(dk))}");
            GD.Print($"[type] keys Dizzy Dinos has and coaster1 does not: {string.Join(" ", dk.Except(ck))}");
        }

        GD.Print($"[type] {byType.Count} distinct ride types declared in this park:");
        foreach (var (t, list) in byType.OrderBy(kv => kv.Key))
            GD.Print($"[type]   type {t,3} ({list.Count,2}) {string.Join(", ", list.Take(4))}"
                   + $"{(list.Count > 4 ? " ..." : "")}"
                   + $"   text row {t}: \"{_text.Text("eng", t)}\"");
    }

    /// <summary>⭐ WHAT IS ALREADY WALKABLE, read off the disc rather than assumed.
    ///
    /// Master: wire "what tiles are counted as path for the sim. like from the bus stops to the
    /// turnstiles to the gate" and "the pre-built paths that are there from the start".
    ///
    /// ⚠⚠ THIS EXISTS TO RE-TEST A NEGATIVE I RECORDED EARLIER. PathTool's own notes say "no park
    /// ships with a path already laid (measured: zero path-tiled cells in all four worlds)". If
    /// master is right that pre-built paths exist, that measurement was wrong, and a negative
    /// result with no control behind it is exactly the kind that is. So this prints the histogram
    /// the claim came from, WITH its control: the material table's own jpa_ block, which the
    /// classifier must recognise whether or not any cell uses it. A classifier that recognises
    /// nothing and a park that contains nothing read identically otherwise.</summary>
    void WalkAudit()
    {
        var f = _park?.Field;
        if (f == null || _terrainModel == null) { GD.PrintErr("[walk] no authored field"); return; }

        // 1. The control: does the classifier see the tiles the material table names?
        int pathMats = 0, queueMats = 0;
        for (int i = 1; i < _terrainModel.Materials.Count; i++)
        {
            var k = ParkPaths.Classify(_terrainModel.Materials[i] ?? "");
            if (k == ParkPathKind.Path) pathMats++;
            else if (k == ParkPathKind.Queue) queueMats++;
        }
        GD.Print($"[walk] the material table holds {_terrainModel.Materials.Count} entries, of which the "
               + $"classifier calls {pathMats} path and {queueMats} queue -- "
               + $"{(pathMats > 0 && queueMats > 0 ? "it has something to hit" : "IT HITS NOTHING, so a zero below means nothing")}");
        // ⭐ NAME THEM, and say whether each one's ART is actually on the disc. The queue table
        // chooses sprite 3 for EVERY corner a queue turns, so a queue tile with no texture is not
        // an oddity in a list -- it is every turn the player draws.
        for (int i = 1; i < _terrainModel.Materials.Count; i++)
        {
            var k = ParkPaths.Classify(_terrainModel.Materials[i] ?? "");
            if (k == ParkPathKind.None) continue;
            var got = TextureNear(_terrainModel == null ? "" : _lib.Rides.FirstOrDefault(r => r.Model != null
                        && IsTerrain(r.Model.Path))?.Model.Path ?? "", _terrainModel.Materials[i]);
            GD.Print($"[walk]   {i,3} {k,-5} {_terrainModel.Materials[i],-18} "
                   + $"{(got.Tex != null ? "resolved" : "NO ART ON THE DISC")}");
        }

        // 2. The histogram of what the cells actually use.
        var counts = new Dictionary<int, int>();
        int drawn = 0;
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                if (!f.Drawn(x, y)) continue;
                drawn++;
                int m = f.Material(x, y);
                counts[m] = counts.GetValueOrDefault(m) + 1;
            }
        int laidPath = 0, laidQueue = 0;
        foreach (var (m, n) in counts)
        {
            if (m <= 0 || m >= _terrainModel.Materials.Count) continue;
            var k = ParkPaths.Classify(_terrainModel.Materials[m] ?? "");
            if (k == ParkPathKind.Path) laidPath += n;
            else if (k == ParkPathKind.Queue) laidQueue += n;
        }
        GD.Print($"[walk] {drawn} drawn cells of {f.Width * f.Height}; {counts.Count} distinct materials; "
               + $"{laidPath} already path, {laidQueue} already queue");
        foreach (var (m, n) in counts.OrderByDescending(kv => kv.Value).Take(14))
            GD.Print($"[walk]   {n,6} x {m,3} {(m > 0 && m < _terrainModel.Materials.Count ? _terrainModel.Materials[m] : "<sentinel>")}"
                   + $"{(m > 0 && m < _terrainModel.Materials.Count && ParkPaths.Classify(_terrainModel.Materials[m] ?? "") != ParkPathKind.None ? "   <-- PATH" : "")}");

        // 3. The entrance, cell by cell, with what each one is made of -- the route master named.
        if (!TerrainBounds("ticket_booths", out var booths)) { GD.PrintErr("[walk] no ticket_booths"); return; }
        int bx = Mathf.RoundToInt(booths.Position.X + booths.Size.X * 0.5f - _holeOrigin.X);
        // ⚠ Row order, as Park.Build applies it: world -> cell counts from the OTHER end.
        int bz = f.Height - Mathf.RoundToInt(booths.Position.Z + booths.Size.Z * 0.5f - _holeOrigin.Y);
        // ⭐⭐ A POSITIONAL CONTROL, which is what was missing. The game's table puts the walkway
        // at z 6..18 with its mouth at 18, so the TICKET BOOTHS -- the thing you walk between --
        // must land inside that, near its far end. Cell counts cannot see a flipped grid; a
        // landmark can, and this is the one findings/gates.md already anchored in every park.
        GD.Print($"[walk] ticket_booths span world z {booths.Position.Z:F1}..{booths.Position.Z + booths.Size.Z:F1}"
               + $" -> cell z {bz}; the table's walkway is z 6..18");
        var legend = new Dictionary<char, string>();
        GD.Print($"[walk] the entrance, booths at cell ({bx},{bz}); '#' no ground, else a letter per material");
        for (int z = bz - 6; z <= bz + 14; z++)
        {
            if (z < 0 || z >= f.Height) continue;
            var row = new System.Text.StringBuilder();
            for (int x = bx - 10; x <= bx + 10; x++)
            {
                if (x < 0 || x >= f.Width) { row.Append(' '); continue; }
                if (!f.Drawn(x, z)) { row.Append('#'); continue; }
                int m = f.Material(x, z);
                string name = m > 0 && m < _terrainModel.Materials.Count ? _terrainModel.Materials[m] : "<0>";
                char c = (char)('a' + (m % 26));
                legend[c] = $"{m} {name}";
                row.Append(c);
            }
            GD.Print($"[walk] z={z,3}  {row}");
        }
        foreach (var (c, name) in legend.OrderBy(kv => kv.Key)) GD.Print($"[walk]   '{c}' = {name}");

        // 4. ⭐⭐ WHAT IS DRAWN ON THE SKIPPED CELLS. The authored grid ships NO path tiles (the
        // histogram above says 0, and the control above says the classifier would have seen them),
        // so whatever is already walkable at the entrance is MESH, not tiles. This rasterises the
        // terrain's own parts into cells the same way ParkPaths does for scenery, but keeps the
        // part's NAME -- so the question "which mesh is the bus stop cell under" has an answer
        // instead of a guess.
        // ⚠⚠ THE SAME COVERAGE TEST THE GRID USES, not a bounding box. A box-rasterised map read
        // against ParkPaths' exact set said the embankment had gone walkable, and that
        // disagreement was entirely the map's: the bank's boxes reach over the road, and
        // first-name-wins then painted the bank on top of it.
        var cover = new Dictionary<(int X, int Y), List<string>>();
        var tf = _terrainModel.WorldTransforms();
        foreach (var mesh in _terrainModel.Meshes)
        {
            var vs = _terrainModel.Vertices(mesh).Pos
                .Select(v => System.Numerics.Vector3.Transform(v, tf[mesh.Offset]))
                .Select(v => new Vector2(v.X - _holeOrigin.X, -v.Z - _holeOrigin.Y)).ToArray();
            foreach (var tri in _terrainModel.Triangles(mesh))
            {
                var a3 = vs[tri.A]; var b3 = vs[tri.B]; var c3 = vs[tri.C];
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a3.X, Mathf.Min(b3.X, c3.X))));
                int x1 = Mathf.Min(f.Width - 1, Mathf.FloorToInt(Mathf.Max(a3.X, Mathf.Max(b3.X, c3.X))));
                int z0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a3.Y, Mathf.Min(b3.Y, c3.Y))));
                int z1 = Mathf.Min(f.Height - 1, Mathf.FloorToInt(Mathf.Max(a3.Y, Mathf.Max(b3.Y, c3.Y))));
                if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 20000) continue;
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        // ⚠ System.Numerics, not Godot's -- the grid's geometry is all in the
                        // core library's vector type and the two do not convert implicitly.
                        if (!ParkPaths.TriangleCoversCell(
                                new System.Numerics.Vector2(a3.X, a3.Y),
                                new System.Numerics.Vector2(b3.X, b3.Y),
                                new System.Numerics.Vector2(c3.X, c3.Y), x, z)) continue;
                        if (!cover.TryGetValue((x, z), out var list)) cover[(x, z)] = list = new List<string>();
                        if (!list.Contains(mesh.Name)) list.Add(mesh.Name);
                    }
            }
        }
        var marks = new Dictionary<string, char>();
        char next = 'A';
        GD.Print($"[walk] the same window, by the terrain PART covering each cell:");
        for (int z = bz - 6; z <= bz + 14; z++)
        {
            if (z < 0 || z >= f.Height) continue;
            var row = new System.Text.StringBuilder();
            for (int x = bx - 10; x <= bx + 10; x++)
            {
                if (x < 0 || x >= f.Width) { row.Append(' '); continue; }
                if (!cover.TryGetValue((x, z), out var names)) { row.Append(f.Drawn(x, z) ? '.' : '#'); continue; }
                string nm = names[0];
                if (!marks.TryGetValue(nm, out char mk)) { mk = next; marks[nm] = mk; next = (char)(next + 1); }
                row.Append(f.Drawn(x, z) ? char.ToLowerInvariant(mk) : mk);
            }
            GD.Print($"[walk] z={z,3}  {row}");
        }
        GD.Print("[walk] UPPERCASE = the terrain draws no ground there, lowercase = it does");
        foreach (var (nm, mk) in marks.OrderBy(kv => kv.Value)) GD.Print($"[walk]   '{mk}' = {nm}");

        // 5. ⭐⭐ AND THE GAME'S OWN ENTRANCE, out of the table at 0x2B71B0.
        ParkPaths grid;
        try { grid = new ParkPaths(_terrainModel); }
        catch (Exception e) { GD.PrintErr($"[walk] no park grid: {e.Message}"); return; }
        GD.Print($"[walk] entrance: {grid.SetEntrance(_entranceTable)}");
        if (_entranceTable != null)
            for (int i = 0; i < _entranceTable.All.Count; i++)
                GD.Print($"[walk]   entry {i,2}: {_entranceTable.All[i]}");
        var ent = grid.EntranceCells.ToList();
        if (ent.Count == 0) { GD.PrintErr("[walk] no entrance cells -- nothing more to say"); return; }
        GD.Print($"[walk] {ent.Count} entrance cells, x {ent.Min(c => c.X)}..{ent.Max(c => c.X)}, "
               + $"z {ent.Min(c => c.Z)}..{ent.Max(c => c.Z)}");

        // ⚠ THE FIT'S OWN PRECONDITION, RESTATED AS A TEST. Every painted cell must be one the
        // terrain draws no ground on, and the cell past the mouth must be drawn -- a wrong entry
        // fails both ways round, which is what makes the fit a test rather than a search.
        int off = ent.Count(c => f.Drawn(c.X, c.Z));
        GD.Print($"[walk] of {ent.Count} entrance cells, {off} are on DRAWN ground -- "
               + $"{(off == 0 ? "none, as it must be" : "THE WALKWAY IS ON THE PARK")}");

        // ⚠ AND CONNECTIVITY: the far end must reach the near end with nothing laid.
        var outer = ent.OrderByDescending(c => c.Z).First();
        var inner = ent.OrderBy(c => c.Z).First();
        var route = grid.Route(outer, inner, c => grid.Open(c));
        GD.Print($"[walk] from {outer} to {inner}: "
               + $"{(route == null ? "NO ROUTE" : $"{route.Count} cells, as it must be")}");

        GD.Print("[walk] the entrance set: 'E' walkable, '#' skipped, '.' ordinary ground");
        int wz0 = Math.Max(0, ent.Min(c => c.Z) - 2), wz1 = Math.Min(f.Height - 1, ent.Max(c => c.Z) + 3);
        int wx0 = Math.Max(0, ent.Min(c => c.X) - 6), wx1 = Math.Min(f.Width - 1, ent.Max(c => c.X) + 6);
        for (int z = wz0; z <= wz1; z++)
        {
            var row = new System.Text.StringBuilder();
            for (int x = wx0; x <= wx1; x++)
                row.Append(grid.IsEntrance(new ParkCell(x, z)) ? 'E' : f.Drawn(x, z) ? '.' : '#');
            GD.Print($"[walk] z={z,3}  {row}");
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
        {
            _game.PlaceAt(_holeOrigin.X + _holeSize.X * 0.5f, _holeOrigin.Y + _holeSize.Y * 0.5f);
            // ⭐⭐ THE MAP'S CAMERA BORDER. The plot rectangle, with a margin so the edge of the
            // park can be looked AT rather than only stood on -- the camera sits `Behind` back
            // from its focus, so a cursor pinned exactly to the last cell still shows what is
            // past it, but a little slack is what stops the far edge feeling walled off.
            // ⚠ TPW_CAM_MARGIN widens it for looking at things outside the plot.
            float margin = float.TryParse(System.Environment.GetEnvironmentVariable("TPW_CAM_MARGIN"), out float m) ? m : 4f;
            _game.MinTileX = _holeOrigin.X - margin;
            _game.MaxTileX = _holeOrigin.X + _holeSize.X + margin;
            _game.MinTileZ = _holeOrigin.Y - margin;
            _game.MaxTileZ = _holeOrigin.Y + _holeSize.Y + margin;
            GD.Print($"[cam] border x {_game.MinTileX:F0}..{_game.MaxTileX:F0}, "
                   + $"z {_game.MinTileZ:F0}..{_game.MaxTileZ:F0} (plot + {margin:F0})");
        }
        else
        {
            // ⚠ No plot, no border: unbounded, so a park the hole-finder could not measure still
            // pans freely rather than being pinned at a nonsense rectangle.
            _game.MinTileX = _game.MaxTileX = _game.MinTileZ = _game.MaxTileZ = float.NegativeInfinity;
            _game.PlaceAt(_focus.X, _focus.Z);
        }
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
    /// <summary>⭐⭐ WHOLE CONSOLE FRAMES, THEN DRAW IN BETWEEN. The camera advances in ticks of
    /// <see cref="ConsoleClock.TicksPerSecond"/>, each one worth exactly one console frame, so its
    /// integer ease gets the steps it was written for and the speed is the console's on any
    /// machine. The picture is then drawn between the last tick and this one, which is where the
    /// smoothness comes from -- not from stepping the simulation faster.
    ///
    /// ⚠ THE INPUT IS READ PER TICK, not per frame. Held keys that moved the cursor once a frame
    /// panned faster on a faster machine, which is the same fault the ease had.</summary>
    void StepGameCam(double delta)
    {
        int ticks = _clock.Advance(delta);
        for (int i = 0; i < ticks; i++)
        {
            int pan = (int)(6 * GameCamera.TileUnits * ConsoleClock.TickSeconds);
            int a = _game.Yaw & 0xFFF;
            // Pan along the way the camera faces, which is what the cursor does on the console.
            float s = Mathf.Sin(a * Mathf.Tau / GameCamera.TurnUnits);
            float c = Mathf.Cos(a * Mathf.Tau / GameCamera.TurnUnits);
            int fwd = (Input.IsKeyPressed(Key.W) ? 1 : 0) - (Input.IsKeyPressed(Key.S) ? 1 : 0);
            int side = (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
            // ⚠ The side term is NEGATED against the forward one. Taking right as (cos, -sin) of
            // the same angle reads correct and drives A and D the wrong way round -- master hit it
            // in the first minute. The camera looks along +(sin, cos), so its right is -(cos, -sin).
            // ⭐⭐ MOVING DROPS A SELECTION, TURNING DOES NOT. Master: "moving with a ride
            // selected will get rid of its selected state. q/e wont." Read off the KEYS rather
            // than off the cursor having moved, because focusing the camera ON a selection moves
            // the cursor too and would otherwise drop it the instant it was made.
            if ((fwd != 0 || side != 0) && (_selected >= 0 || _gateSelected)) ClearSelection();
            _game.CursorX += (int)((fwd * s - side * c) * pan);
            _game.CursorZ += (int)((fwd * c + side * s) * pan);
            // ⭐ The map's border. Clamped every tick rather than only when a key is pressed, so a
            // limit that changes -- a different park -- takes hold on its own.
            _game.ClampCursor();
            // ⚠ R is the turn while something is held; the camera only gets it back when the
            // cursor is empty.
            if (Input.IsKeyPressed(Key.R) && !_place.Active) _game.Zoom(-1);
            if (Input.IsKeyPressed(Key.F)) _game.Zoom(1);
            if (Input.IsKeyPressed(Key.Z)) _game.Push(-1);
            if (Input.IsKeyPressed(Key.X)) _game.Push(1);
            // ⭐ A WHOLE console frame, every time. No fractions reach the console's own maths.
            _game.Step(GameCamera.FrameTick, GroundAt);
        }
        // ⚠ A capture renders one frame and must not photograph a half-eased camera; it runs the
        // tick and stands on it.
        float alpha = _shotPath != null ? 1f : _clock.Alpha;
        var (eye, look, up) = _game.PoseAt(alpha);
        if ((look - eye).LengthSquared() < 1e-8f) { eye = _game.Eye; look = _game.Look; up = _game.Up; }
        _cam.Transform = new Transform3D(Basis.LookingAt(look - eye, up), eye);
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
        BuildFloor();
    }

    /// <summary>Lay the plot's floor again from the grid as it stands. ⭐ Cheap and complete: a
    /// path tile changes the cell's ground byte and its neighbours', and the floor is built FROM
    /// those bytes, so re-running it is how a laid tile appears -- no separate path geometry.</summary>
    /// <summary>Lay the plot for the FIRST time, from the hole the terrain leaves. ⚠ This one
    /// empties the park, which is right when a park is being loaded and wrong at every other
    /// moment -- see RefreshFloor.</summary>
    void BuildFloor()
    {
        ClearSelection();
        if (_holeSize.X <= 1f) { _park.Build(ParkCells, ParkCells); return; }
        _park.Build(Mathf.RoundToInt(_holeSize.X), Mathf.RoundToInt(_holeSize.Y), _holeCells);
    }

    /// <summary>Lay the ground again during play. ⚠⚠ KEEPS what is standing in the park. This runs
    /// on every path press, and a full Build frees the ride node's children and clears the placed
    /// list -- so a path press used to delete every ride in the park. ⚠ And pointing the FIRST
    /// build at this instead was worse: with no size yet it returns having built nothing, so the
    /// whole plot came up unplayable and every placement read "blocked".</summary>
    void RefreshFloor() => _park.Rebuild();

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
        _flags.NudgeZ = FlagAnchorZError;
        _flags.Build(_terrainModel, path => _lib?.ReadGeneric(path));
        GD.Print($"[flags] {_flags.Report}");
        _flags.Root.Visible = _mode == Mode.Park;
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
        // ⭐ THE PARK'S OWN MODELS TICK TOO. Only the model on the Models tab was ever advanced,
        // so anything standing in the park that was not the chosen ride was frozen -- the gate is
        // built WITH its animation and its record and then never asked for a frame. Its doors
        // cannot open if nobody moves its clock.
        // ⚠⚠ A GATE OPENS ONCE. This used to wrap -- `_parkTime %= _gate.Frames` -- so the arch
        // played its opening over and over for as long as the park was on screen. Master, having
        // watched it in every park: "the gate loops an opening animation. nothing triggers the
        // animation." Both halves are true: nothing starts it, and nothing stops it either.
        //
        // ⭐ It now runs once and HOLDS ITS LAST FRAME, which is the open gate -- the same shape
        // as StepBuilding twelve lines down, where a ride builds itself and then stays built.
        // A thing that has finished happening should look like it has happened.
        //
        // ⚠ WHAT STARTS IT IS STILL UNREAD. The park has no "opening time" wired to this, so it
        // opens on load. Whatever the console triggers it from is not decoded, and this is only
        // the loop half of master's report.
        if (_gate != null && _playing && _shotPath == null && _mode == Mode.Park
            && _gate.Frames > 0 && _parkTime < _gate.Frames - 1)
        {
            _parkTime = Mathf.Min(_parkTime + (float)delta * Aps.Fps, _gate.Frames - 1);
            _gate.SetFrame(_parkTime);
        }
        // ⭐ A RIDE BUILDING ITSELF runs ONCE and then holds on its last frame, which is the built
        // thing. ⚠ Backwards, because a finished one is removed as we go.
        if (_playing && _shotPath == null && _building.Count > 0)
            StepBuilding((float)delta * Aps.Fps);
        // ⭐ And the ones that run themselves. Paused means paused: the park's clock is the
        // viewer's, so nothing advances while the game is held still.
        //
        // ⚠⚠ A SHOT MUST RUN THE SIM TOO, and the first version of this did not: the guard was
        // copied from `_building` above, so every render came back with the park frozen on
        // whatever its scripts had reached in six frames. `--shot=out.png:400` means the park AT
        // frame 400, so shot mode winds the sim forward once, in the sim's own fixed ticks, and
        // the picture is of a park that has been running.
        //
        // ⚠ AND NOT ON THE FIRST FRAME. `--place-test` puts its rides down LATER IN THIS SAME
        // `_Process`, so a one-shot flag latched here burns before the park contains anything --
        // which is what happened: the log showed three scripts starting and the shot still came
        // back unwound. Latch only once there is something to wind.
        if (_shotPath != null && _soundCensus <= 0)
        {
            // ⚠ Not under --guest-test: its capture branch winds the park itself, in two stages.
            if (!_guestTest && !_shotWound && _scripted.Count > 0) { _shotWound = true; WindPark(_shotFrame); }
        }
        // ⭐ Rides AND people, on the console's tick, with the screen interpolating between ticks.
        // ⚠ And under a sound census even with a shot asked for: a wound park fires every cue in
        // one frame and no voice can advance, which is exactly the "resolves but never plays"
        // that the census exists to catch.
        else if (_playing && _mode == Mode.Park) StepPark(delta);
        _sounds?.Step(delta); _burst?.Step();
        if (_soundCensus > 0 && _mode == Mode.Park && _parkTicks * ParkSim.TickMilliseconds >= _soundCensus * 1000L)
        {
            SoundCensusReport();
            if (_shotPath != null) SaveShot(_shotPath);
            GetTree().Quit(); _soundCensus = 0; return;
        }
        // ⭐ The selection breathes on its own clock, and like the console's it stands still
        // while the game is paused.
        if (_mode == Mode.Park) UpdateHover();
        if (_playing) _selectView?.Step(delta);
        if (_playing && _mode == Mode.Park) _gateBox?.Step(delta);
        if (_playing && _mode == Mode.Park) _flags.Step(delta);
        if (GameCamActive) StepGameCam(delta);
        else
        {
            var eye = _focus + new Vector3(
                Mathf.Cos(_pitch) * Mathf.Sin(_yaw), Mathf.Sin(-_pitch), Mathf.Cos(_pitch) * Mathf.Cos(_yaw)) * _dist;
            _cam.Transform = new Transform3D(Basis.LookingAt(_focus - eye, Vector3.Up), eye);
        }
        // ⚠ AFTER the camera is placed, both of them: the volume follows the eye, and a pending
        // build happens here rather than in the park load for the reason on _weatherWanted.
        // ⭐ On the console's clock like everything else that moves, and in SECONDS because a
        // scroll and a sine are continuous -- there is nothing to quantise to a tick and nothing
        // to interpolate between.
        if (_water != null && _mode == Mode.Park)
        {
            _waterTime += (float)delta;
            _water.Advance(_waterTime);
        }
        if (_place.Active) UpdatePlacementGhost();
        else if (_toolOpen) UpdateGhost();
        // ⚠ AFTER the camera has been placed for this frame, or the projection is a frame stale
        // and the check is of the wrong camera.
        if (_ghostTest && !_pickChecked && _mode == Mode.Park) CheckMousePicking();
        if (_animTest && !_animChecked && _mode == Mode.Park) CheckParkAnimation();
        if (_buildTest && !_buildChecked && _mode == Mode.Park) { if (_placeTest) CheckPlacement(); else CheckBuildMenu(); }
        _weather.Follow(_cam.GlobalPosition);
        if (_weatherWanted is { } wk)
        {
            _weatherWanted = null;
            GD.Print($"[weather] {wk}: {_weather.Set(_lib, wk, _cam.GlobalPosition)}");
        }

        if (_shotPath != null)
        {
            if (_current != null) { _time = _shotFrame < 0 ? 0 : _shotFrame; _current.SetFrame(_time); }
            ++_shotWait;
            // ⭐⭐ A GUEST SHOT IS TWO SHOTS. One frame proves a guest EXISTS; only two prove it
            // MOVED. The clock is held still under a capture, so the park is wound by hand: to
            // sixty seconds, photographed (`<shot>-a.png`), then to three minutes, photographed
            // again as the shot asked for -- and the positions and the rides' census at both are
            // in the log beside each other.
            // ⚠ The camera is aimed on the first frame: the eye is placed at the TOP of _Process,
            // so a change made down here reaches the picture one frame later than the walk does.
            // ⚠ LATCHED ON THE GATE, NOT ON THE FRAME COUNT: the park may come up a few frames
            // after the first _Process, and a stage keyed to frame ten would photograph an empty
            // park. Each grab is two frames after its stage -- one for the camera, one for the draw.
            const int warm = 10;
            if (_soundCensus > 0) { }   // the census ends itself above, after its seconds of real frames
            else if (_rideFilm > 0 && _guestTest && _guests != null)
            {
                // ⚠⚠ THE CAMERA NEEDS TWO FRAMES TO LAND, AND FRAME 0 WAS BEING SHOT BEFORE IT DID.
                // RideFilmStart sets _focus/_yaw, but those are turned into `_cam.Transform`
                // later in _Process, so a capture on the very next tick photographs wherever the
                // camera WAS -- an empty field, while Crazy Ape's Create animation (215 frames,
                // ~7 s of a crate opening into an ape) played off-screen. The stage note ten
                // lines up already says each grab wants two frames, one for the camera and one
                // for the draw; the film branch was the one place not honouring it.
                //
                // ⭐ These warm frames do NOT step the park, so frame k is still exactly park
                // time start + k/F and the audio alignment is untouched.
                if (_guestStage == 0 && _shotWait >= warm) { RideFilmStart(); _guestStage = 4; }
                else if (_guestStage == 4) { RideFilmCamera(); _guestStage = 5; }
                else if (_guestStage == 5) _guestStage = 6;
                else if (_guestStage == 6) RideFilmFrame();
            }
            else if (_guestTest && _guests != null)
            {
                if (_guestStage == 0 && _shotWait >= warm)
                {
                    GuestTestCamera();
                    // ⭐ W FIRST, while the gate is still streaming guests in: one every twenty ticks
                    // at a cell a second puts them 0.8 cells apart on the way to the queue, which is
                    // the only time they are both walking and clear of each other. Searched AFTER
                    // stage A the first time, it found nobody in 750 ticks -- by then the crowd is
                    // queued, riding, or stacked in the queue line.
                    if (WalkerCloseUp(Math.Max(0, 1500 - _parkTicks))) { _guestStage = 4; _guestSince = _shotWait; }
                    else { GuestTestStage("A", 1500); _guestStage = 1; _guestSince = _shotWait; }
                }
                else if (_guestStage == 4 && _shotWait >= _guestSince + 2)
                {
                    if (_walkFilm > 0)
                    {
                        // ⭐ THE CLIP instead of the still: follow the walker for N frames (FilmFrame).
                        GD.Print($"[guest] film: following #{_filmGuest} for {_walkFilm} frames at 60 a second = {_walkFilm / 60f:F2}s of park time"
                               + " | expected: legs scissor twice per 16-frame cycle (0.53s) with the arms swinging opposite; body at 1.0 cell/s against a 0.164-unit stride, so the planted foot should slide back ~3x");
                        _guestStage = 5;
                    }
                    else { SaveShot(ShotSibling(_shotPath, "-w")); GuestTestCamera(); GuestTestStage("A", 1500); _guestStage = 1; _guestSince = _shotWait; }
                }
                else if (_guestStage == 5) FilmFrame();
                else if (_guestStage == 1 && _shotWait >= _guestSince + 2)
                { SaveShot(ShotSibling(_shotPath, "-a")); GuestTestStage("B", 4500); _guestStage = 2; _guestSince = _shotWait; }
                else if (_guestStage == 2 && _shotWait >= _guestSince + 2)
                {
                    SaveShot(_shotPath);
                    // ⭐ And a third, close on the ride, once one stands: the park shot cannot show
                    // a half-unit kid on a six-unit ape from sixteen units out.
                    if (_guestTestRide != null) { GuestTestCloseUp(); _guestStage = 3; _guestSince = _shotWait; }
                    else GetTree().Quit();
                }
                else if (_guestStage == 3 && _shotWait >= _guestSince + 2) { SaveShot(ShotSibling(_shotPath, "-c")); GetTree().Quit(); }
            }
            else if (_shotWait > (_guestTest ? 600 : warm))
            {
                if (_guestTest) GD.Print("[guest] the test never opened the gate -- shooting the park as it is");
                SaveShot(_shotPath); GetTree().Quit();
            }
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            bool lDown = (mm.ButtonMask & MouseButtonMask.Left) != 0;
            bool rDown = (mm.ButtonMask & MouseButtonMask.Right) != 0;
            bool mDown = (mm.ButtonMask & MouseButtonMask.Middle) != 0;
            // ⭐ The camera does not move until the pointer has left the click's slop. Without
            // this a click that slides two pixels nudges the view AND is thrown away, which is the
            // worst of both: nothing happens and the scene drifts.
            if (lDown && _left.Down && !_left.Dragged && mm.Position.DistanceTo(_left.At) > ClickSlop)
                _left.Dragged = true;
            if (rDown && _right.Down && !_right.Dragged && mm.Position.DistanceTo(_right.At) > ClickSlop)
                _right.Dragged = true;
            // ⭐⭐ IN THE PARK THE BUTTONS BELONG TO THE TOOL, not the camera. Building means the
            // pointer is moving when you press -- to start a run, to lay it, to open or shut the
            // tool -- so any slop test on a button the tool uses is a test the player keeps
            // failing by doing the thing the tool is for. Once a button cannot move the camera
            // there is nothing to tell apart, and every press of it is a press.
            //
            // The right button is the tool's whenever a park is up; the left is the tool's while
            // the tool is open. Panning is the MIDDLE drag, and shift with the left button when
            // the tool is shut. The camera's own keys -- WASD, Q/E, R/F -- are untouched.
            bool tools = _mode == Mode.Park && _paths != null && GameCamActive;
            bool orbiting = lDown && !tools && (_left.Dragged || !_left.Down);
            bool panning = (rDown && !tools && (_right.Dragged || !_right.Down)) || mDown
                        || (orbiting && Input.IsKeyPressed(Key.Shift));
            // LEFT drag orbits.
            if (orbiting && !panning)
            {
                _yaw -= mm.Relative.X * 0.01f;
                _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.01f, -1.5f, 1.5f);
            }
            // RIGHT or MIDDLE drag pans, in the camera's own plane so it moves with the view
            // rather than along world axes. Scaled by distance so it feels the same when zoomed in.
            else if (panning)
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
            // CLICK. A drag of either button is the camera's, so the button is judged on release.
            // ⚠ The two buttons are tracked SEPARATELY. One slot meant pressing the second button
            // while the first was down threw the first one's release away.
            if (mb.ButtonIndex is MouseButton.Right or MouseButton.Left)
            {
                ref var held = ref (mb.ButtonIndex == MouseButton.Left ? ref _left : ref _right);
                if (mb.Pressed)
                {
                    held.Down = true;
                    held.At = mb.Position;
                    held.Ms = Time.GetTicksMsec();
                    held.Dragged = false;
                }
                else if (held.Down)
                {
                    held.Down = false;
                    // A quick press counts however far it slid; a slow one still counts if it
                    // barely moved. ⭐ And a button the tool owns needs no test at all: the right
                    // one whenever a park is up, the left one while the tool is open.
                    // ⚠ BOTH buttons, open or shut. Opening has the same fault placing had --
                    // you are moving the mouse when you reach for the tool -- so a slop test on
                    // the button that opens it is one the player fails by aiming at the tile they
                    // want. ⚠ Only while the GAME camera is up: under the free camera (G) the
                    // buttons still drag the view, which is the whole point of it.
                    bool toolsOwn = _mode == Mode.Park && _paths != null && GameCamActive;
                    bool click = toolsOwn
                              || (!held.Dragged
                                  && (Time.GetTicksMsec() - held.Ms <= ClickMs
                                      || mb.Position.DistanceTo(held.At) <= ClickSlop));
                    if (click && _mode == Mode.Park && _paths != null)
                    {
                        // ⭐ LEFT OPENS AND WORKS IT, RIGHT ONLY SHUTS IT. Master's layout: the
                        // button you build with is the button you reach for, and the other one
                        // gets you out.
                        if (mb.ButtonIndex == MouseButton.Right && _place.Active)
                        {
                            // ⭐ The right button puts the blueprint down before it touches the
                            // path tool: holding something and reaching for cancel means cancel
                            // THAT, not open a different tool underneath it.
                            GD.Print($"[build] dropped {_place.Display}");
                            Status("nothing held");
                            _place.Clear();
                            _ghostView?.Clear();
                            _ghostAt = (-1, -1, -1, -1);
                        }
                        else if (mb.ButtonIndex == MouseButton.Right && UndoLeg())
                        {
                            // ⭐⭐ THE QUEUE'S RIGHT BUTTON STEPS BACK. Handled inside UndoLeg so
                            // that "there was nothing to step back" falls through to the ordinary
                            // close below -- master: "up until the single tile sticking out, then
                            // it becomes close tool".
                        }
                        else if (mb.ButtonIndex == MouseButton.Right)
                        {
                            // ⭐ Right toggles, and it opens WITHOUT the under-the-cursor test
                            // that the left button gets. That test is there so a left click can
                            // reach a ride rather than lay path over it; the right button has no
                            // such duty, which makes it the way to open the tool while pointing
                            // at something that will one day answer a click.
                            if (_toolOpen) { CloseTool(); GD.Print("[tool] closed"); }
                            // ⭐ And it drops a selection before it opens anything, the same
                            // bargain it already makes with a held blueprint: reaching for cancel
                            // means cancel THAT, not start something else underneath it.
                            else if (_selected >= 0 || _gateSelected) ClearSelection();
                            else OpenTool(PathTool.Kind.Path);
                        }
                        else if (_place.Active) PlaceHeld();
                        else if (_toolOpen) PressTool();
                        // ⭐⭐ A LEFT CLICK REACHES A RIDE BEFORE IT REACHES THE GROUND. That was
                        // always the intent -- the comment on the right button says so -- and it
                        // had never been wired: InteractiveUnderCursor returned null, so a click
                        // aimed at a ride opened the path tool on the cell underneath it.
                        else if (SelectUnderCursor()) { }
                        // ⚠ ALWAYS A PATH. A queue is not something you lay wherever you like:
                        // it belongs to a ride, it starts at that ride's entrance, and the game
                        // puts you in it when you place one. Offering it on a modifier let you
                        // build a queue attached to nothing.
                        else OpenTool(PathTool.Kind.Path);
                    }
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
