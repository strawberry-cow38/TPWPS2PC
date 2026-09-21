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
    readonly Dictionary<string, (ImageTexture Tex, bool Soft)> _texCache = new();
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
    string _wantRide, _wantAnim, _wantWad, _wantMode, _wantImage, _wantSound, _wantPlay;
    string _discPath;

    ItemList _rideList;
    CheckBox _texOn;
    OptionButton _wadPick, _animPick, _modePick;
    Label _info;
    HSlider _scrub;

    /// <summary>What the left-hand list is showing. The archive is the same either way; only what
    /// the viewer does with an entry changes.</summary>
    enum Mode { Models, Textures, Sounds, Movies }
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

        _wantMode = Env("TPW_PS2_MODE");
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
        _discPath = disc;
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
        // ⚠ Every switch has an environment fallback, because arguments after `--` do not survive
        // cmd's quoting and a silent empty argument presents as a hang rather than an error.
        if (_wantMode != null && _wantMode.StartsWith("mov", StringComparison.OrdinalIgnoreCase))
        {
            _modePick.Select(3); SetMode(Mode.Movies);
        }
        else if (_wantMode != null && _wantMode.StartsWith("sou", StringComparison.OrdinalIgnoreCase))
        {
            _modePick.Select(2); SetMode(Mode.Sounds);
            if (_wantSound != null)
            {
                for (int b = 0; b < _wadPick.ItemCount; b++)
                    if (_wadPick.GetItemText(b).Contains(_wantSound, StringComparison.OrdinalIgnoreCase))
                    { _wadPick.Select(b); OpenBank(b); break; }
            }
            if (_wantPlay != null)
                for (int k = 0; k < _rideList.ItemCount; k++)
                    if (_rideList.GetItemText(k).Contains(_wantPlay, StringComparison.OrdinalIgnoreCase))
                    { _rideList.Select(k); ShowSound(k); PlaySelected(); break; }
        }
        else if (_wantMode != null && _wantMode.StartsWith("tex", StringComparison.OrdinalIgnoreCase))
        {
            _modePick.Select(1); SetMode(Mode.Textures);
            if (_wantImage != null)
                for (int i = 0; i < _rideList.ItemCount; i++)
                    if (_rideList.GetItemText(i).Contains(_wantImage, StringComparison.OrdinalIgnoreCase))
                    { _rideList.Select(i); ShowImage(i); break; }
        }
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

        // The image pane sits BEHIND the side panel and in front of the 3D view, so switching mode
        // is just a visibility flip -- the models stay built and come back instantly.
        // An opaque backdrop, or the 3D scene shows through the image pane.
        _imageBack = new ColorRect { Visible = false, Color = new Color(0.07f, 0.07f, 0.09f),
                                     MouseFilter = Control.MouseFilterEnum.Ignore, OffsetLeft = 280 };
        _imageBack.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(_imageBack);

        _imageView = new TextureRect
        {
            Visible = false,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            OffsetLeft = 280,
        };
        _imageView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(_imageView);

        _modePick = new OptionButton();
        _modePick.AddItem("Models"); _modePick.AddItem("Textures");
        _modePick.AddItem("Sounds"); _modePick.AddItem("Movies");
        _modePick.ItemSelected += i => SetMode((Mode)(int)i);
        col.AddChild(_modePick);

        _wadPick = new OptionButton();
        _wadPick.ItemSelected += i => { if (_mode == Mode.Sounds) OpenBank((int)i); else OpenWad((int)i); };
        col.AddChild(_wadPick);

        _rideList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _rideList.ItemSelected += i =>
        {
            if (_mode == Mode.Models) ShowRide((int)i);
            else if (_mode == Mode.Textures) ShowImage((int)i);
            else if (_mode == Mode.Movies) ShowMovie((int)i);
            else ShowSound((int)i);
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
        _video = new VideoStreamPlayer { Visible = false, Expand = true, OffsetLeft = 280,
                                         MouseFilter = Control.MouseFilterEnum.Ignore };
        _video.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(_video);
        ui.MoveChild(_video, 0);
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
        if (_current != null) _current.Root.Visible = m == Mode.Models;
        if (m == Mode.Sounds) FillBankPicker();
        else if (m == Mode.Movies) FillMovieList();
        else FillWadPicker();
    }

    void FillWadPicker()
    {
        _wadPick.Clear();
        foreach (var w in _lib.Wads()) _wadPick.AddItem(w);
        if (_wadPick.ItemCount > 0) { _wadPick.Select(0); OpenWad(0); }
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
        _wadPick.Clear(); _rideList.Clear(); _movies.Clear();
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
        { _movies.Add(f); _rideList.AddItem(Path.GetFileNameWithoutExtension(f)); }
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
        _info.Text = Path.GetFileName(path) + "\n" + (len / 1024 / 1024.0).ToString("0.0") + " MB\n"
                   + "Ogg Theora, converted from MPEG-2\n⚠ silent: the EA audio codec is unidentified";
    }

    /// <summary>Open one `.SDT` and list what is in it.</summary>
    void OpenBank(int i)
    {
        _rideList.Clear();
        _bank = null;
        if (i < 0 || i >= _banks.Count) return;
        try { _bank = new SoundBank(_lib.ReadDisc(_banks[i])); }
        catch (Exception ex) { _info.Text = _banks[i].Path + "\n" + ex.Message; return; }
        foreach (var snd in _bank.Sounds)
        {
            string kind = snd.IsEmpty ? "empty" : snd.IsAdpcm ? "vag" : snd.Channels == 2 ? "stereo" : "mono";
            _rideList.AddItem(snd.Name + "   " + kind + "  " + (snd.Milliseconds / 1000.0).ToString("0.0") + "s");
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
        _rideList.Clear();
        if (_mode == Mode.Sounds) return;      // the bank picker fills this list instead
        if (_mode == Mode.Models)
        {
            foreach (var r in _lib.Rides) _rideList.AddItem(r.Name);
            if (_lib.Rides.Count > 0) { _rideList.Select(0); ShowRide(0); }
        }
        else
        {
            _images = _lib.Images();
            foreach (var e in _images) _rideList.AddItem(e.Path.TrimStart('/'));
            if (_images.Count > 0) { _rideList.Select(0); ShowImage(0); }
            else _info.Text = "no images in this archive";
        }
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
                       + (tga.PartialAlpha * 100 > texels ? "blended (soft alpha)" : "cutout");
        }
        catch (Exception ex) { _info.Text = e.Path + "\ndid not decode: " + ex.Message; }
    }

    void OpenWad(int i)
    {
        GD.Print($"[v] opening {_wadPick.GetItemText(i)}"); _lib.OpenWad(_wadPick.GetItemText(i)); GD.Print($"[v] indexed {_lib.Rides.Count} rides");
        _texCache.Clear();
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
                    _animPick.AddItem($"{rec.SlotName}  {_anim.Length(rec)} frames"
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
                if (TextureFor(mat).Tex != null) got++;
                else { missed++; if (misses.Count < 8) misses.Add(mat); }
            }
            GD.Print($"[tex] {got} resolved, {missed} missing" +
                     (misses.Count > 0 ? ": " + string.Join(", ", misses) : ""));
            _current = new AnimatedModel(model, _anim, rec, TextureFor);
            _current.Root.Visible = _mode == Mode.Models;
            AddChild(_current.Root); GD.Print($"[v] built: {_current.Summary}");
            _time = 0;
            _current.SetFrame(0);
            FrameCamera(model);
            _info.Text = $"{_ride.Name}\n{_current.Summary}\n" +
                         $"{_records.Count} animations\n" +
                         "left-drag orbit  |  right-drag or shift+drag or WASD to pan  |  wheel zoom\n" +
                         "SPACE play/pause  |  arrows step a frame  |  R re-frame";
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
        if (_texCache.TryGetValue(material, out var t)) return t;
        var tga = _lib.Texture(_ride, material);
        (ImageTexture, bool) made = (null, false);
        if (tga != null)
        {
            var img = Image.CreateFromData(tga.Width, tga.Height, false, Image.Format.Rgba8, tga.Pixels);
            img.GenerateMipmaps();
            made = (ImageTexture.CreateFromImage(img),
                    tga.PartialAlpha * 100 > tga.Width * tga.Height);
        }
        _texCache[material] = made;
        return made;
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
