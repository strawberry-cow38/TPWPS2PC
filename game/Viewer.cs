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
    CheckBox _texOn;
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

        // ⚠⚠ A full-screen Control swallows mouse events before _UnhandledInput ever sees them.
        // Orbit appeared to work only because the left button is also used by the widgets; a
        // right-drag over the empty area was consumed and the camera never heard about it.
        // Ignore on the ROOT, Pass on the panel: the actual widgets still take their own clicks.
        var ui = new Control { AnchorRight = 1, AnchorBottom = 1,
                               MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(ui);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(280, 0),
                                         MouseFilter = Control.MouseFilterEnum.Pass };
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

        _texOn = new CheckBox { Text = "Textures", ButtonPressed = true };
        _texOn.Toggled += _ => Rebuild();
        col.AddChild(_texOn);

        _scrub = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.001 };
        _scrub.DragStarted += () => _playing = false;
        _scrub.ValueChanged += v => { if (!_playing && _current != null) { _time = (float)v * _current.Frames; _current.SetFrame(_time); } };
        col.AddChild(_scrub);

        _info = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart,
                            MouseFilter = Control.MouseFilterEnum.Ignore };
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
            // ⚠ Report which materials got a texture and which did not. A material that silently
            // resolves to null renders flat-shaded and reads as missing geometry, not a missing file.
            int got = 0, missed = 0; var misses = new List<string>();
            foreach (var mat in model.Materials)
            {
                if (mat == null) continue;
                if (TextureFor(mat) != null) got++;
                else { missed++; if (misses.Count < 8) misses.Add(mat); }
            }
            GD.Print($"[tex] {got} resolved, {missed} missing" +
                     (misses.Count > 0 ? ": " + string.Join(", ", misses) : ""));
            _current = new AnimatedModel(model, _anim, rec, TextureFor);
            AddChild(_current.Root); GD.Print($"[v] built: {_current.Summary}");
            _time = 0;
            _current.SetFrame(0);
            FrameCamera(model);
            _info.Text = $"{_ride.Name}\n{_current.Summary}\n" +
                         $"{_records.Count} animations\n" +
                         "left-drag orbit  |  right-drag or shift+drag or WASD to pan  |  wheel zoom\n" +
                         "SPACE play/pause  |  arrows step a frame  |  R re-frame";
        }
        catch (Exception ex) { _info.Text = $"{_ride?.Name}\nfailed: {ex.Message}"; }
    }

    ImageTexture TextureFor(string material)
    {
        // ⚠ The toggle must be checked HERE, not at build time, or turning textures off would
        // still hand the material a texture it had already cached.
        if (material == null || _texOn?.ButtonPressed == false) return null;
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

    /// <summary>Frame the model on its ACTUAL geometry, not on `mesh+0x70/+0x80`.
    ///
    /// ⚠⚠ Those declared bounds cover the whole MORPH RANGE -- every position the vertices can
    /// reach across the entire animation -- so framing on them pulls the camera far enough back
    /// that the model occupies a couple of hundred pixels no matter the window size, and thin
    /// geometry (chains, the sign, banana shapes) falls below one pixel and simply vanishes. That
    /// is what made the viewer look like it was missing artwork the Python renderer had.</summary>
    void FrameCamera(Model model)
    {
        var world = model.WorldTransforms();
        var pts = new List<Vector3>();
        foreach (var m in model.Meshes)
        {
            if (!world.TryGetValue(m.Offset, out var w)) continue;
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
        _dist = Mathf.Max((hi - lo).Length() * 0.85f, 2f);
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
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) _dist = Mathf.Max(_dist * 0.9f, 0.05f);
            if (mb.ButtonIndex == MouseButton.WheelDown) _dist *= 1.1f;
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
        try { FrameCamera(new Model(_lib.Read(_ride.Model))); } catch { }
    }

}
