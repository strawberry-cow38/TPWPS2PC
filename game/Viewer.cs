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
    readonly Dictionary<string, ImageTexture> _texCache = new();
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
    string _wantRide, _wantAnim, _wantWad;

    ItemList _rideList;
    OptionButton _wadPick, _animPick;
    Label _info;
    HSlider _scrub;

    public override void _Ready()
    {
        string disc = null;
        foreach (var a in OS.GetCmdlineArgs())
            if (a.StartsWith("--disc=")) disc = a["--disc=".Length..];
        disc ??= OS.GetEnvironment("TPW_PS2_DISC");
        // ⚠ Environment fallbacks for every switch. Arguments after `--` do not survive some shells
        // intact, and a disc path with spaces is the common case -- losing them silently is how this
        // looked like a hang rather than a missing argument.
        string Env(string k) { var v = OS.GetEnvironment(k); return string.IsNullOrWhiteSpace(v) ? null : v; }
        foreach (var a in OS.GetCmdlineArgs())
        {
            if (a.StartsWith("--shot="))
            {
                var v = a["--shot=".Length..];
                int c = v.LastIndexOf(':');
                if (c > 2) { _shotPath = v[..c]; int.TryParse(v[(c + 1)..], out _shotFrame); }
                else _shotPath = v;
            }
            else if (a.StartsWith("--ride=")) _wantRide = a["--ride=".Length..];
            else if (a.StartsWith("--anim=")) _wantAnim = a["--anim=".Length..];
            else if (a.StartsWith("--wad=")) _wantWad = a["--wad=".Length..];
        }

        _wantRide ??= Env("TPW_PS2_RIDE");
        _wantAnim ??= Env("TPW_PS2_ANIM");
        _wantWad ??= Env("TPW_PS2_WAD");
        if (_shotPath == null && Env("TPW_PS2_SHOT") != null)
        {
            _shotPath = Env("TPW_PS2_SHOT");
            int.TryParse(Env("TPW_PS2_FRAME") ?? "0", out _shotFrame);
        }
        BuildUi();
        GD.Print($"[v] start; args={string.Join(" ", OS.GetCmdlineArgs())}");
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
        GD.Print("[v] opening disc"); _lib = new AssetLibrary(disc);
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
        if (_wantRide != null)
            for (int i = 0; i < _rideList.ItemCount; i++)
                if (_rideList.GetItemText(i).Contains(_wantRide, StringComparison.OrdinalIgnoreCase))
                { _rideList.Select(i); ShowRide(i); break; }
        if (_wantAnim != null && int.TryParse(_wantAnim, out var ai) && ai < _animPick.ItemCount)
        { _animPick.Select(ai); _recordIndex = ai; Rebuild(); }
    }

    void BuildUi()
    {
        _cam = new Camera3D { Current = true };
        AddChild(_cam);
        AddChild(new DirectionalLight3D
        {
            Transform = new Transform3D(Basis.LookingAt(new Vector3(-0.4f, -0.8f, -0.45f), Vector3.Up),
                                        Vector3.Zero),
            LightEnergy = 1.1f,
        });
        var env = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.10f, 0.10f, 0.13f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.45f, 0.45f, 0.5f),
                AmbientLightEnergy = 1.0f,
            }
        };
        AddChild(env);

        var ui = new Control { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(ui);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(280, 0) };
        panel.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        ui.AddChild(panel);
        var col = new VBoxContainer();
        panel.AddChild(col);

        _wadPick = new OptionButton();
        _wadPick.ItemSelected += i => OpenWad((int)i);
        col.AddChild(_wadPick);

        _rideList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _rideList.ItemSelected += i => ShowRide((int)i);
        col.AddChild(_rideList);

        _animPick = new OptionButton();
        _animPick.ItemSelected += i => { _recordIndex = (int)i; Rebuild(); };
        col.AddChild(_animPick);

        _scrub = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.001 };
        _scrub.DragStarted += () => _playing = false;
        _scrub.ValueChanged += v => { if (!_playing && _current != null) { _time = (float)v * _current.Frames; _current.SetFrame(_time); } };
        col.AddChild(_scrub);

        _info = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        col.AddChild(_info);
    }

    void OpenWad(int i)
    {
        GD.Print($"[v] opening {_wadPick.GetItemText(i)}"); _lib.OpenWad(_wadPick.GetItemText(i)); GD.Print($"[v] indexed {_lib.Rides.Count} rides");
        _texCache.Clear();
        _rideList.Clear();
        foreach (var r in _lib.Rides) _rideList.AddItem(r.Name);
        if (_lib.Rides.Count > 0) { _rideList.Select(0); ShowRide(0); }
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
                    if (rec.Skeletal) continue;
                    _records.Add(rec);
                    _animPick.AddItem($"#{_records.Count - 1}  {_anim.Length(rec)} frames");
                }
            }
            catch { _anim = null; }
        }
        if (_animPick.ItemCount > 0) _animPick.Select(0);
        Rebuild();
    }

    void Rebuild()
    {
        if (_current != null) { _current.Root.QueueFree(); _current = null; }
        if (_ride?.Model == null) return;
        try
        {
            GD.Print($"[v] building {_ride.Name}"); var model = new Model(_lib.Read(_ride.Model));
            var rec = _recordIndex < _records.Count ? _records[_recordIndex] : null;
            _current = new AnimatedModel(model, _anim, rec, TextureFor);
            AddChild(_current.Root); GD.Print($"[v] built: {_current.Summary}");
            _time = 0;
            _current.SetFrame(0);
            FrameCamera(model);
            _info.Text = $"{_ride.Name}\n{_current.Summary}\n" +
                         $"{_records.Count} animations  |  SPACE play/pause  |  drag to orbit, wheel to zoom";
        }
        catch (Exception ex) { _info.Text = $"{_ride?.Name}\nfailed: {ex.Message}"; }
    }

    ImageTexture TextureFor(string material)
    {
        if (material == null) return null;
        if (_texCache.TryGetValue(material, out var t)) return t;
        var tga = _lib.Texture(_ride, material);
        ImageTexture tex = null;
        if (tga != null)
        {
            var img = Image.CreateFromData(tga.Width, tga.Height, false, Image.Format.Rgba8, tga.Pixels);
            img.GenerateMipmaps();
            tex = ImageTexture.CreateFromImage(img);
        }
        _texCache[material] = tex;
        return tex;
    }

    void FrameCamera(Model model)
    {
        var world = model.WorldTransforms();
        var pts = new List<Vector3>();
        foreach (var m in model.Meshes)
        {
            if (!world.TryGetValue(m.Offset, out var w)) continue;
            foreach (var c in new[] { m.BoundsMin, m.BoundsMax })
            {
                var v = System.Numerics.Vector3.Transform(c, w);
                pts.Add(new Vector3(v.X, v.Y, v.Z));
            }
        }
        if (pts.Count == 0) return;
        var lo = pts.Aggregate((a, b) => new Vector3(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y), Mathf.Min(a.Z, b.Z)));
        var hi = pts.Aggregate((a, b) => new Vector3(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y), Mathf.Max(a.Z, b.Z)));
        _focus = (lo + hi) * 0.5f;
        _dist = Mathf.Max((hi - lo).Length() * 1.6f, 4f);
    }

    public override void _Process(double delta)
    {
        // ⚠ The camera is placed FIRST, before any early return. It used to sit below the capture
        // branch, so a --shot run photographed the origin and produced a perfectly black frame with
        // a perfectly correct UI beside it -- the geometry was fine the whole time.
        if (_current != null && _playing && _shotPath == null)
        {
            _time += (float)delta * Aps.Fps;
            if (_time >= _current.Frames) _time = 0;
            _current.SetFrame(_time);
            _scrub.SetValueNoSignal(_time / Mathf.Max(_current.Frames, 1));
        }
        var eye = _focus + new Vector3(
            Mathf.Cos(_pitch) * Mathf.Sin(_yaw), Mathf.Sin(-_pitch), Mathf.Cos(_pitch) * Mathf.Cos(_yaw)) * _dist;
        _cam.Transform = new Transform3D(Basis.LookingAt(_focus - eye, Vector3.Up), eye);

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
        if (e is InputEventMouseMotion mm && (mm.ButtonMask & MouseButtonMask.Left) != 0)
        {
            _yaw -= mm.Relative.X * 0.01f;
            _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.01f, -1.5f, 1.5f);
        }
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) _dist *= 0.9f;
            if (mb.ButtonIndex == MouseButton.WheelDown) _dist *= 1.1f;
        }
        if (e is InputEventKey k && k.Pressed && k.Keycode == Key.Space) _playing = !_playing;
    }
}
