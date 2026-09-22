using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

public partial class VisitorDemo : Node3D
{
    AssetLibrary _world, _data;
    VisitorParkView _view;
    VisitorScenario _scenario;
    Label _status;
    Camera3D _camera;
    Button _pause;
    bool _paused;
    int _terrain = 1, _speed = 1;
    double _clock;
    Vector3 _focus;
    float _distance = 16, _yaw = 0.1f, _pitch = 0.75f;
    string _capture;
    int _captureFrames;
    public override void _Ready()
    {
        var canvas = new CanvasLayer(); AddChild(canvas);
        var panel = new VBoxContainer { Position = new Vector2(18, 18) }; canvas.AddChild(panel);
        panel.AddChild(new Label { Text = "Visitors • The Orbiter" });
        var choice = new OptionButton(); choice.AddItem("Space park 1"); choice.AddItem("Space park 2"); panel.AddChild(choice);
        choice.ItemSelected += i => { _terrain = (int)i + 1; Restart(); };
        var buttons = new HBoxContainer(); panel.AddChild(buttons);
        _pause = new Button { Text = "Pause" }; buttons.AddChild(_pause);
        _pause.Pressed += () => { _paused = !_paused; _pause.Text = _paused ? "Resume" : "Pause"; };
        var restart = new Button { Text = "Restart" }; buttons.AddChild(restart); restart.Pressed += Restart;
        var speed = new Button { Text = "1×" }; buttons.AddChild(speed);
        speed.Pressed += () => { _speed = _speed == 1 ? 4 : 1; speed.Text = $"{_speed}×"; };
        var back = new Button { Text = "Asset viewer" }; panel.AddChild(back);
        back.Pressed += () => GetTree().ChangeSceneToFile("res://Main.tscn");
        panel.AddChild(new Label { Text = "Follow Ada from the path to the queue and back out.\nDrag to orbit • Wheel to zoom" });
        _status = new Label(); panel.AddChild(_status);
        AddChild(new WorldEnvironment { Environment = new Godot.Environment {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.08f, 0.12f, 0.17f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.8f } });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, -30, 0), LightEnergy = 1.4f });
        _camera = new Camera3D { Current = true }; AddChild(_camera);
        try
        {
            string path = OS.GetEnvironment("TPW_PS2_DISC");
            if (string.IsNullOrWhiteSpace(path)) path = GetTree().GetMeta("tpw_disc", "").AsString();
            if (string.IsNullOrWhiteSpace(path)) throw new Exception("Set TPW_PS2_DISC to your disc image.");
            GetTree().SetMeta("tpw_disc", path);
            _world = new AssetLibrary(path); _data = new AssetLibrary(path); _data.OpenWad("/DATA/DATA.WAD");
            var args = OS.GetCmdlineUserArgs(); int at = 9000;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "--shot") _capture = args[i + 1];
                if (args[i] == "--at-ms") at = int.Parse(args[i + 1]);
                if (args[i] == "--terrain") _terrain = int.Parse(args[i + 1]);
            }
            if (_terrain is not (1 or 2)) throw new ArgumentOutOfRangeException("terrain");
            choice.Select(_terrain - 1);
            Restart();
            if (_capture != null)
            {
                if (at < 0 || at > 120000) throw new ArgumentOutOfRangeException("capture time");
                _scenario.AdvanceTo(at); _clock = at; _view.Update(); _paused = true; Status();
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    void Restart()
    {
        try
        {
            if (_world == null) return;
            _view?.Dispose(); _view = null;
            _world.OpenWad("/DATA/SPACE.WAD"); _scenario = new VisitorScenario(_world.Wad, "SPACE", _terrain);
            _view = new VisitorParkView(this, _scenario, _world, _data);
            var head = _scenario.Simulation.QueueCells[0];
            _focus = new Vector3(_view.Park.Origin.X + head.X + 0.5f, 0.5f, _view.Park.Origin.Y + head.Z + 1.5f);
            _clock = 0; _paused = false; _pause.Text = "Pause"; Status();
        }
        catch (Exception ex) { Fail(ex); }
    }
    void Status()
    {
        var sim = _scenario.Simulation;
        _status.Text = $"{sim.Time / 1000d:F1}s • {(sim.Machine["VAR_RUNNING"] == 1 ? "Running" : "Loading / unloading")}\n"
            + string.Join('\n', sim.Visitors.Select(g => $"{g.Name}: {g.State}"));
    }
    void Fail(Exception ex) { _paused = true; _status.Text = ex.Message; GD.PrintErr(ex); }
    public override void _Process(double delta)
    {
        try
        {
            if (_view != null && !_paused)
            { _clock += Math.Min(delta, 0.25) * 1000 * _speed; _scenario.AdvanceTo((long)_clock); _view.Update(); Status(); }
            _camera.Position = _focus + new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), Mathf.Sin(_pitch),
                Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _distance; _camera.LookAt(_focus);
            if (_capture != null && ++_captureFrames > 6)
            { using var image = GetViewport().GetTexture().GetImage(); image.SavePng(_capture); _capture = null; GetTree().Quit(); }
        }
        catch (Exception ex) { Fail(ex); }
    }
    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventMouseMotion m && (m.ButtonMask & MouseButtonMask.Left) != 0)
        { _yaw -= m.Relative.X * 0.01f; _pitch = Math.Clamp(_pitch + m.Relative.Y * 0.01f, 0.1f, 1.4f); }
        if (ev is InputEventMouseButton b && b.Pressed)
        { if (b.ButtonIndex == MouseButton.WheelUp) _distance *= 0.9f; if (b.ButtonIndex == MouseButton.WheelDown) _distance *= 1.1f; }
    }
    public override void _ExitTree() { _view?.Dispose(); _world?.Dispose(); _data?.Dispose(); }
}
