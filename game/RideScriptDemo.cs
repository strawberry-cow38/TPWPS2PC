using Godot;
using System.Linq;
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
    ParticleLibrary _particles;
    RideParticles _burst;
    RideSounds _sounds;
    string _capture, _film;
    int _filmStep = 200, _filmFrames = 120, _filmSaved;
    float _filmZoom = 1f;
    int _filmControl = -1;
    int _fxProbe;
    // ⚠ The measured "engine damps at 0.73x what I set" was taken entirely at 0.25x. If that
    // factor is really Euler error it MUST change with the step; if it is constant the engine is
    // simply applying less damping than asked. One flag, one run, and the two are told apart.
    double _fxSlow = 0.25;
    bool _fxMarker;
    ulong _probeT0;
    int _fxNode = -1;
    AssetLibrary _particleWad;
    int _captureFrames;
    int _aliveFrames;

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
            // ⚠⚠ BOTH SPELLINGS, IN BOTH LISTS. `--disc=PATH` was only ever checked against
            // `GetCmdlineArgs()` (the args BEFORE `--`) and `--disc PATH` only against the user
            // args AFTER it -- so the perfectly reasonable `-- --disc=PATH` matched neither, threw
            // here, and left the scene sitting in a failed state forever. That is where every
            // leaked fx process came from.
            foreach (string arg in OS.GetCmdlineArgs().Concat(argv))
                if (arg.StartsWith("--disc=")) disc = arg[7..];
            for (int i = 0; i + 1 < argv.Length; i++) if (argv[i] == "--disc") disc = argv[i + 1];
            if (string.IsNullOrEmpty(disc)) throw new Exception("Set TPW_PS2_DISC or pass -- --disc /path/to/disc.bin");
            GetTree().SetMeta("tpw_disc", disc);
            _lib = new AssetLibrary(disc);
            // ⭐ The effects library, from its own WAD. A second AssetLibrary because the first
            // one has the ride's world open and this must not disturb it.
            try
            {
                // ⚠ KEPT, not scoped to this block: RideParticles reads the effects' textures out
                // of the same WAD when it first draws one, so it must still be open then.
                _particleWad = new AssetLibrary(disc);
                _particleWad.OpenWad("/DATA/PARTICLE.WAD");
                _particles = new ParticleLibrary(_particleWad.Read(_particleWad.Wad.Find("/Tp2.plb")));
                GD.Print($"[fx] Tp2.plb: {_particles.Effects.Count} effects; "
                       + $"{_particles.RampDisagreements().Count()} whose colours disagree with their name");
            }
            catch (Exception e) { GD.PrintErr($"[fx] no particle library: {e.Message}"); }
            Restart();
            int captureTime = 19000;
            // ⚠⚠ `--shot=PATH[:FRAME]` AS WELL AS `--shot PATH`, AND THIS IS WHY THE BOX WAS FULL
            // OF DEAD GODOTS. Every script that drives this scene passes the joined form, which
            // the two-argument loop below never matched -- so `_capture` stayed null, the capture
            // branch never ran, and the scene ran FOREVER with nothing to quit it. Twenty-one
            // headless processes were still alive, the oldest a day old. A harness that silently
            // does nothing is bad; one that then never exits is a leak.
            foreach (var a in argv)
            {
                if (!a.StartsWith("--shot=")) continue;
                var spec = a["--shot=".Length..];
                // ⚠ The `:FRAME` suffix is the park viewer's spelling. Split on the LAST colon so
                // a Windows drive letter (`C:\...`) is not mistaken for it.
                int colon = spec.LastIndexOf(':');
                if (colon > 1 && int.TryParse(spec[(colon + 1)..], out _)) spec = spec[..colon];
                _capture = spec;
            }
            for (int i = 0; i + 1 < argv.Length; i++)
            {
                if (argv[i] == "--shot") _capture = argv[i + 1];
                if (argv[i] == "--at-ms") captureTime = int.Parse(argv[i + 1]);
                if (argv[i] == "--film") _film = argv[i + 1];
                if (argv[i] == "--step-ms") _filmStep = int.Parse(argv[i + 1]);
                if (argv[i] == "--frames") _filmFrames = int.Parse(argv[i + 1]);
                // ⚠ Applied AFTER Restart, which is what sets _distance from the bounds.
                if (argv[i] == "--fx-control") _filmControl = int.Parse(argv[i + 1]);
                // ⭐⭐ THE MOTION PROBE. Master, on being sent a video as proof of an easing curve:
                // "you send a screenshot. to measure movement lol." Right -- a clip cannot show
                // position against time. This emits ONE particle and stamps every captured frame
                // with the real engine time, so the decay can be fitted from the PNGs instead of
                // eyeballed. The demo's camera is already fixed in film mode, which matters: an
                // orbiting camera is what made me misread a render as "worse" earlier today.
                // 1 = shipped exponential drag, 2 = the linear control. See RideParticles.Emit.
                if (argv[i] == "--fx-probe") _fxProbe = int.Parse(argv[i + 1]);
                if (argv[i] == "--fx-marker") _fxMarker = argv[i + 1] == "1";
                if (argv[i] == "--fx-slow")
                    _fxSlow = double.Parse(argv[i + 1], System.Globalization.CultureInfo.InvariantCulture);
                // ⭐ Where to put the control burst. ⚠ DEFAULT -1 KEEPS THE DIAGNOSTIC: the burst
                // is normally fired at the camera's focus owing NOTHING to the node table, so that
                // "it did not appear" means the emitter is wrong rather than the place. Naming a
                // node trades that away for a burst where the RIDE would actually put it -- which
                // is what you want once the emitter is known good. Master, on a control shot:
                // "so why is there still a puff way above his head?" -- because focus+up IS above
                // his head, and that was the harness, not the motion.
                if (argv[i] == "--fx-node") _fxNode = int.Parse(argv[i + 1]);
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
                // ⭐ A CONTROL IN A KNOWN-GOOD PLACE. One burst at the camera's focus, owing
                // nothing to the node table or the script: if this does not appear, the emitter
                // is wrong; if it appears and the script's do not, the POSITION is wrong.
                if (_burst != null && _filmControl >= 0)
                {
                    // ⭐ THE SCALE BAR. Emitted two cells above the one under test and frozen
                    // there, so every captured frame carries its own pixels-per-cell as the gap
                    // between the two blobs. Measuring distance off a separately-calibrated run
                    // put a 4% wobble under every number.
                    // ⚠ TWO markers, at +2 and +6 cells. One marker gives a scale that cannot be
                    // checked; two give a ratio that MUST come out 3.0, so the calibration has to
                    // earn its place before a 2x claim gets hung on it. A ratio under 3 also
                    // measures the perspective, which is the one thing a single gap hides.
                    // ⚠ OPT-IN NOW. The marker sits 2 cells above the spawn and the puff rises
                    // about one, so at full speed the two blobs merge after eight frames and the
                    // fit degenerates. It has already done its job -- it pinned the projection to
                    // 0.0004 cells, and the projection is arithmetic from the logged camera, so
                    // it does not need re-proving on every run.
                    if (_fxMarker)
                        foreach (int up in new[] { 3, 7 })
                            GD.Print($"[fx] scale marker {up - 1} cells up: "
                                   + (_burst.Emit(_filmControl, _focus + Vector3.Up * up, null, 4) != null));
                    // ⭐⭐ PRINT THE CAMERA, DON'T INFER THE SCALE. Every pixels-per-cell I took
                    // off the screen disagreed with the next one (27, 58, and a gap that moved
                    // 23% between runs) because the camera sits CLOSE and above: a particle
                    // rising toward it grows in pixels while it slows, which flatters the decay
                    // and reads as a lambda lower than the record's. With the camera's own
                    // numbers the world->pixel projection is exact arithmetic and the blob gaps
                    // become a check on it rather than the source of it.
                    GD.Print($"[probe] camera pos {_camera.GlobalPosition} fov {_camera.Fov} "
                           + $"near {_camera.Near} focus {_focus} distance {_distance} "
                           + $"viewport {GetViewport().GetVisibleRect().Size} "
                           + $"keep {_camera.KeepAspect}");
                    // ⭐⭐ SLOW MOTION, BECAUSE THE RIG RAN OUT OF RESOLUTION. ApeSnot's whole
                    // decay is over in about eight captured frames, and the grab is out of phase
                    // with the simulation by up to one (the first two PNGs of every run are the
                    // same picture). Measuring a time constant with eight jittery samples is how
                    // the same data gave lambda = 4.9, 6.1, 6.5 and 3.8 on four passes. Scaling
                    // time stretches the SAME motion over 4x the frames without altering a single
                    // particle parameter -- more samples of the identical physics.
                    if (_fxProbe > 0) Engine.TimeScale = _fxSlow;
                    _probeT0 = Time.GetTicksUsec();
                    GD.Print($"[fx] control burst {_filmControl} at focus {_focus}: "
                           + (_burst.Emit(_filmControl, _focus + Vector3.Up, null, _fxProbe) != null));
                }
                GD.Print($"[film] {_world}{_stem}: {_filmFrames} frames every {_filmStep}ms");
            }
            if (_capture != null)
            {
                if (captureTime < 0 || captureTime > 240000) throw new Exception("Capture time must be 0..240000ms");
                while (_time < captureTime) { _time += 100; _preview.Tick(_time); }
                // ⭐⭐ THE CONTROL BURST WORKS ON A STILL TOO. It only ever fired in FILM mode, so
                // `--fx-control N --shot X` wound the clock forward and photographed an empty
                // park -- "Particles: 0 asked for" on a run that had asked for one. Fired AFTER
                // the wind-forward so the burst is young when the shutter opens, and at the
                // camera's focus, owing nothing to the node table: if this does not appear the
                // EMITTER is wrong, and if it appears while the script's do not, the POSITION is.
                if (_burst != null && _filmControl >= 0)
                {
                    var spot = _fxNode >= 0 ? NodeAt(_fxNode, 0x100) : null;
                    var where = spot ?? _focus + Vector3.Up;
                    GD.Print($"[fx] control burst {_filmControl} at "
                           + (spot is { } ? $"NODE {_fxNode} {where}" : $"focus+up {where} (no node asked for)")
                           + $": {_burst.Emit(_filmControl, where) != null}");
                }
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
            _burst?.Clear();
            _burst = _particles == null ? null : new RideParticles(this, _particles, _particleWad);
            _sounds?.Clear();
            try { _sounds ??= new RideSounds(this, new SoundCatalogue(_lib.Disc, _world, 1), new SoundCatalogue(_lib.Disc, _world, 2)); }
            catch (Exception e) { GD.PrintErr($"[snd] no sound catalogue: {e.Message}"); _sounds = null; }
            _fx = 0;
            // ⭐⭐ THE SCRIPT ASKS AND THIS ANSWERS. EVENT and ADDOBJ carry a KIND first: 1 and 2
            // reach the particle library (`0x18b5a8`/`0x18b0f8`), 3 goes to a different manager
            // entirely and is NOT a particle -- its ids run past the library's 105.
            _preview.Host.EffectRequested += fx =>
            {
                GD.Print($"[fx] {fx.Time}ms {fx.Opcode} {string.Join(" ", fx.Arguments)}");
                // ⭐ Sound first: kinds 3..11 are the OBJ_SOUND_* groups and the model sits at the
                // origin here, so node -1 is the origin and a named node is its 0x200 fitting.
                if (_sounds != null && fx.Arguments.Count >= 1)
                {
                    var sa = fx.Arguments;
                    if (fx.Opcode is RseOpcode.EVENT or RseOpcode.ADDOBJ && sa.Count >= 3 && SoundCatalogue.IsSoundGroup(sa[0]))
                    {
                        var sat = sa[1] < 0 ? Vector3.Zero : NodeAt(sa[1], 0x200);
                        _sounds.Cue(0, _preview.Machine.Name ?? _stem, fx.Time, fx.Opcode, sa[0], sa[1], sa[2],
                                    fx.Opcode == RseOpcode.ADDOBJ && sa.Count > 3 ? sa[3] : 1000, sat ?? Vector3.Zero, sat == null && sa[1] >= 0);
                    }
                    else if (fx.Opcode == RseOpcode.KILLOBJ && sa.Count >= 1) _sounds.Kill(0, _preview.Machine.Name ?? _stem, sa[0], fx.Time);
                    else if (fx.Opcode == RseOpcode.FADEOBJ && sa.Count >= 1) _sounds.Fade(0, _preview.Machine.Name ?? _stem, sa[0], fx.Time);
                }
                if (_burst == null || fx.Arguments.Count < 3) return;
                if (fx.Opcode != RseOpcode.EVENT && fx.Opcode != RseOpcode.ADDOBJ) return;
                int kind = fx.Arguments[0], node = fx.Arguments[1], id = fx.Arguments[2];
                if (kind is not (1 or 2)) return;
                // `0x1bbf28`: kind 1 resolves the node in space 0x100, kind 2 the same with a
                // direction as well.
                var at = NodeAt(node, 0x100);
                var made = at is { } place ? _burst.Emit(id, place) : null;
                // ⚠ THE POSITION, NOT JUST THE NAME. Two films came back with the counter going up
                // and nothing on screen, which looks identical whether the burst is invisible or
                // in the wrong place. (0,0,0) here means the node lookup failed.
                GD.Print($"[fx] -> {made?.Name ?? "(no fitting)"} id {id} node {node} at {at?.ToString() ?? "-"} "
                       + $"life {made?.LifetimeGuess}ms count {made?.CountGuess} size {made?.SizeGuess} "
                       + $"mid {made?.ColourAt(0.5f)}");
                if (made != null) { _fx++; _lastFx = $"{made.Name} at node {node}"; }
            };
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
            _burst?.Step();
            _sounds?.Step(delta);
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
                    // ⚠ The REAL elapsed time, not the script clock. Particles age on engine time
                    // and the film steps script time -- conflating them is what made the earlier
                    // frame-advance test inconclusive.
                    if (_fxProbe > 0)
                        GD.Print($"[probe] f{_filmSaved:D4} "
                               + $"t={(Time.GetTicksUsec() - _probeT0) / 1e6 * Engine.TimeScale:F5}s");
                    if (++_filmSaved >= _filmFrames)
                    {
                        GD.Print($"[film] {_filmSaved} frames at {_filmStep}ms to {_time / 1000d:F1}s");
                        if (_sounds != null) GD.Print(_sounds.Summary());
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
            // ⚠⚠ A HARD STOP, BECAUSE THE LAST ONE COST A DAY OF STUCK PROCESSES. Whatever the
            // reason -- a shot that never resolves, a film that never advances, an argument
            // spelled a way nothing matches -- a headless run of this scene must not outlive its
            // job. 60 seconds is far longer than any capture here needs and far shorter than a
            // process anyone would notice leaking.
            // ⚠⚠ ON A **CAPTURE** RUN, not a headless one -- and that distinction is the whole
            // bug. The first version of this guard tested `DisplayServer.GetName() == "headless"`,
            // but a capture needs a real viewport, so every shot harness runs WINDOWED and the
            // guard never fired for the runs that were actually leaking. Eighteen of the twenty
            // stuck processes were windowed `--mode=park` captures.
            //
            // ⭐ A run that was ASKED for a picture is automated by definition; a person driving
            // this scene passes neither --shot nor --film and is left alone.
            if ((_capture != null || _film != null) && ++_aliveFrames > 3600)
            {
                GD.PrintErr("[fx] capture run hit its 60s cap without finishing -- quitting "
                          + "rather than leaking the process");
                GetTree().Quit(3);
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
            + $"Running: {vm[9]}    Remaining cycles: {vm["VAR_COUNT"]}"
            + (_burst == null ? "" : $"\nParticles: {_fx} asked for" + (_lastFx == "" ? "" : $" -- last {_lastFx}"));
    }
    int _fx; string _lastFx = "";

    /// <summary>Where the instruction's node is, in the scene.
    ///
    /// ⚠⚠ THIS READING IS NOT ESTABLISHED. The RSE's node numbers are taken here as the MODEL's
    /// node order -- meshes then helpers -- because that is the order the animation's own track
    /// nodes use. But the console resolves them through `0x1b9388(vm, out, out, node, SPACE)`,
    /// and the space argument (`0x80`, `0x100`, `0x200`, `0x800`) selects between different
    /// tables, so the number is very likely an index into a space-specific one and not into this.
    /// The symptom: Crazy Ape's `EVENT 2 1 22` lands on the model ORIGIN while its `2 2 22` lands
    /// somewhere plausible, which is what a wrong table looks like when one entry happens to fit.
    /// Left as it is, and said out loud, rather than nudged until a puff appears in a nice spot.</summary>
    Vector3? NodeAt(int node, uint space)
    {
        var drawn = _presenter?.Drawn;
        if (drawn?.LastWorld == null || node < 0) return null;
        // ⭐⭐ THE SCRIPT'S NODE IS A FITTING, FOUND BY ID AND KIND -- not an index. See
        // Model.Fittings: 0x1f1f78 searches the table at model+0x74 for the entry whose id
        // matches and whose flags share a bit with the space, and a miss means the instruction
        // does nothing at all, so a miss here draws nothing rather than falling back to the
        // origin. (It used to fall back, which is how Crazy Ape's snot ended up at its feet.)
        var fit = _presenter.Model?.FindFitting(node, space);
        if (fit is not { Node: >= 0 } f) return null;
        int off = _presenter.Model.NodeOffset(f.Node);
        if (!drawn.LastWorld.TryGetValue(off, out var w)) return null;
        return new Vector3(w.M41, w.M42, -w.M43);
    }

    /// <summary>⚠⚠ A FAILED CAPTURE RUN MUST DIE, NOT SIT THERE. This used to pause and print,
    /// which is right for a person driving the scene and catastrophic for a harness: the run had
    /// nothing left to do, nothing to quit it, and no window anyone would notice. Twenty of them
    /// accumulated on the box over a day.
    ///
    /// ⭐ "Was a picture asked for" is the test, because that is exactly the run nobody is
    /// watching. A person gets the old behaviour: paused, with the message on screen.</summary>
    void Fail(Exception ex)
    {
        _paused = true; _status.Text = ex.Message; GD.PrintErr(ex);
        bool automated = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs())
            .Any(a => a.StartsWith("--shot") || a.StartsWith("--film"));
        if (automated) { GD.PrintErr("[fx] capture run failed -- quitting instead of hanging"); GetTree().Quit(4); }
    }
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
