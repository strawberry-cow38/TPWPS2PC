using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

/// <summary>BYO-disc live ride execution. Script ticks and rendered APS frames share one clock.</summary>
public partial class RideScriptDemo : Node3D
{
    AssetLibrary _lib;
    RseRidePreview _preview;
    RseModelPresenter _presenter;
    Label _status;
    readonly Dictionary<string, (ImageTexture, bool)> _textures = new();
    long _time;
    double _accumulator;
    bool _paused;
    int _speed = 1;
    string _world = "JUNGLE", _stem = "/Rides/Monkey/Monkey";
    Camera3D _camera;
    Button _pauseButton;
    Vector3 _focus;
    float _distance, _yaw = 0.65f, _pitch = 0.35f;
    string _capture, _film;
    int _filmStep = 200, _filmFrames = 120, _filmSaved;
    float _filmZoom = 1f;
    int _captureFrames;

    public override void _Ready()
    {
        var canvas = new CanvasLayer(); AddChild(canvas);
        var panel = new VBoxContainer { Position = new Vector2(20, 20) }; canvas.AddChild(panel);
        panel.AddChild(new Label { Text = "Ride scripts • live preview", ThemeTypeVariation = "HeaderLarge" });
        var choices = new OptionButton(); panel.AddChild(choices);
        var rides = new[] { ("Crazy Ape", "JUNGLE", "/Rides/Monkey/Monkey"),
            ("Spider", "JUNGLE", "/Rides/Spider/Spider"), ("Bugs TV", "FANTASY", "/Rides/bugstv/bugstv"),
            ("Phantom", "HALLOW", "/rides/phantom/Phantom"), ("Orbiter", "SPACE", "/Rides/orbiter/orbiter") };
        foreach (var ride in rides) choices.AddItem(ride.Item1);
        choices.ItemSelected += i => { _world = rides[i].Item2; _stem = rides[i].Item3; Restart(); };
        var buttons = new HBoxContainer(); panel.AddChild(buttons);
        var pause = _pauseButton = new Button { Text = "Pause" }; buttons.AddChild(pause);
        pause.Pressed += () => { _paused = !_paused; pause.Text = _paused ? "Resume" : "Pause"; };
        var restart = new Button { Text = "Restart" }; buttons.AddChild(restart); restart.Pressed += Restart;
        var speed = new Button { Text = "1×" }; buttons.AddChild(speed);
        speed.Pressed += () => { _speed = _speed == 1 ? 4 : 1; speed.Text = $"{_speed}×"; };
        var back = new Button { Text = "Asset viewer" }; buttons.AddChild(back);
        back.Pressed += () => GetTree().ChangeSceneToFile("res://Main.tscn");
        _status = new Label(); panel.AddChild(_status);
        panel.AddChild(new Label { Text = "Opens, boards two guests, runs, unloads and closes.\nSound, particles and guest models are not rendered.\nDrag to orbit • Wheel to zoom" });
        var env = new WorldEnvironment { Environment = new Godot.Environment {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.08f, 0.12f, 0.17f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White,
            AmbientLightEnergy = 0.7f } };
        AddChild(env);
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, -30, 0), LightEnergy = 1.4f });
        _camera = new Camera3D { Current = true }; AddChild(_camera);
        try
        {
            string disc = OS.GetEnvironment("TPW_PS2_DISC");
            if (string.IsNullOrWhiteSpace(disc)) disc = GetTree().GetMeta("tpw_disc", "").AsString();
            var argv = OS.GetCmdlineUserArgs();
            foreach (string arg in OS.GetCmdlineArgs()) if (arg.StartsWith("--disc=")) disc = arg[7..];
            for (int i = 0; i + 1 < argv.Length; i++) if (argv[i] == "--disc") disc = argv[i + 1];
            if (string.IsNullOrEmpty(disc)) throw new Exception("Set TPW_PS2_DISC or pass -- --disc /path/to/disc.bin");
            GetTree().SetMeta("tpw_disc", disc);
            _lib = new AssetLibrary(disc); Restart();
            int captureTime = 19000;
            for (int i = 0; i + 1 < argv.Length; i++)
            {
                if (argv[i] == "--shot") _capture = argv[i + 1];
                if (argv[i] == "--at-ms") captureTime = int.Parse(argv[i + 1]);
                if (argv[i] == "--film") _film = argv[i + 1];
                if (argv[i] == "--step-ms") _filmStep = int.Parse(argv[i + 1]);
                if (argv[i] == "--frames") _filmFrames = int.Parse(argv[i + 1]);
                // ⚠ Applied AFTER Restart, which is what sets _distance from the bounds.
                if (argv[i] == "--zoom") _filmZoom = float.Parse(argv[i + 1], System.Globalization.CultureInfo.InvariantCulture);
                if (argv[i] == "--stem") _stem = argv[i + 1];
                if (argv[i] == "--world") { _world = argv[i + 1]; }
            }
            if (_film != null)
            {
                // ⚠ The normal update must NOT also advance the clock, or every film frame is
                // two steps on and the strip runs at double speed.
                _paused = true;
                Restart(); _paused = true; _distance *= _filmZoom;
                GD.Print($"[film] {_world}{_stem}: {_filmFrames} frames every {_filmStep}ms");
            }
            if (_capture != null)
            {
                if (captureTime < 0 || captureTime > 240000) throw new Exception("Capture time must be 0..240000ms");
                while (_time < captureTime) { _time += 100; _preview.Tick(_time); }
                _presenter.Update(_preview.Host); ShowStatus(); _paused = true;
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    void Restart()
    {
        try
        {
            if (_lib == null) return;
            _presenter?.Dispose(); _presenter = null;
            foreach (var texture in _textures.Values) texture.Item1.Dispose(); _textures.Clear();
            _lib.OpenWad($"/DATA/{_world}.WAD");
            byte[] Read(string extension) => _lib.Read(_lib.Wad.Find(_stem + extension)
                ?? throw new Exception($"Missing {_stem}{extension}"));
            var model = new Model(Read(".mps")); var animation = new Aps(Read(".aps"));
            _preview = new RseRidePreview(new RseProgram(Read(".rse")), animation);
            _presenter = new RseModelPresenter(this, model, animation, Texture);
            _time = 0; _accumulator = 0; _paused = false;
            _pauseButton.Text = "Pause";
            _preview.Tick(0); _presenter.Update(_preview.Host);
            var (min, max) = Park.Bounds(model);
            _focus = new Vector3((min.X + max.X) / 2, (min.Y + max.Y) / 2, -(min.Z + max.Z) / 2);
            _distance = Math.Max((max - min).Length() * 1.2f, 1);
            ShowStatus();
        }
        catch (Exception ex) { Fail(ex); }
    }
    (ImageTexture, bool) Texture(string name)
    {
        if (_textures.TryGetValue(name, out var cached)) return cached;
        var data = _lib.TextureNear(_stem + ".mps", name);
        if (data == null) return (null, false);
        using var image = Image.CreateFromData(data.Width, data.Height, false, Image.Format.Rgba8, data.Pixels);
        var value = (ImageTexture.CreateFromImage(image), data.PartialAlpha * 100 > data.Width * data.Height);
        _textures[name] = value; return value;
    }
    public override void _Process(double delta)
    {
        try
        {
            if (_preview != null && !_paused)
            {
                _accumulator += Math.Min(delta, 0.25) * 1000 * _speed;
                while (_accumulator >= 100) { _time += 100; _preview.Tick(_time); _accumulator -= 100; }
                _presenter.Update(_preview.Host); ShowStatus();
            }
            if (_camera != null && _distance > 0)
            {
                _camera.Position = _focus + new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch),
                    Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _distance;
                _camera.LookAt(_focus);
            }
            // ⭐⭐ A FILM, NOT A FRAME. Master asked to SEE a ride run its script, and one
            // picture of a ride and one picture of a frozen ride are the same picture. This
            // steps the preview by a fixed slice and saves one PNG per step, so the status panel
            // -- state, riders, animation slot and frame -- moves in the strip beside the model.
            //
            // ⚠ ONE SAVE PER _Process. The viewport has to have actually drawn the frame before
            // its texture is worth reading, so the sim is stepped here and grabbed here, never
            // in a loop.
            if (_film != null)
            {
                if (++_captureFrames > 3)
                {
                    using var shot = GetViewport().GetTexture().GetImage();
                    shot.SavePng($"{_film}{_filmSaved:D4}.png");
                    if (++_filmSaved >= _filmFrames)
                    {
                        GD.Print($"[film] {_filmSaved} frames at {_filmStep}ms to {_time / 1000d:F1}s");
                        GetTree().Quit(); _film = null; return;
                    }
                    _time += _filmStep; _preview.Tick(_time);
                    _presenter.Update(_preview.Host); ShowStatus();
                }
                return;
            }
            if (_capture != null && ++_captureFrames > 6)
            {
                using var image = GetViewport().GetTexture().GetImage();
                image.SavePng(_capture); GetTree().Quit(); _capture = null;
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    void ShowStatus()
    {
        var vm = _preview.Machine; var host = _preview.Host;
        string state = _preview.Completed ? "Closed • cycle complete" : vm[9] != 0 ? "Running" : vm[1] != 0 ? "Unloading"
            : host.AnimationSlot == 0 ? "Building" : vm[6] != 0 ? "Closed" : "Loading";
        _status.Text = $"{vm.Name} • {_time / 1000d:F1}s\n{state}\nRiders: {vm[5]} / {vm[2]}    Unloaded: {_preview.Unloaded}\n"
            + $"Animation: {host.Current?.Record.SlotName} {host.AnimationVariant} • frame {host.Frame:F1}\n"
            + $"Running: {vm[9]}    Remaining cycles: {vm["VAR_COUNT"]}";
    }
    void Fail(Exception ex) { _paused = true; _status.Text = ex.Message; GD.PrintErr(ex); }
    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Left) != 0)
        { _yaw -= motion.Relative.X * 0.01f; _pitch = Math.Clamp(_pitch + motion.Relative.Y * 0.01f, 0.05f, 1.4f); }
        if (ev is InputEventMouseButton button && button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.WheelUp) _distance *= 0.9f;
            if (button.ButtonIndex == MouseButton.WheelDown) _distance *= 1.1f;
        }
    }
    public override void _ExitTree()
    {
        _presenter?.Dispose();
        foreach (var texture in _textures.Values) texture.Item1.Dispose();
        _lib?.Dispose();
    }
}
