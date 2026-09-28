using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>The advisor on the shipping Viewer, end to end (findings/advisor-messages.md §3..§5, §11; the spec is
/// ghidra_tpw/notes/SPEC-advisor-B-viewer.md). Every check reads what the VIEW produced -- the head's drawn
/// meshes and channel, the voice player, the buses, the stack view's drawn count and text, the camera -- not
/// only what the core meant:
/// <list type="number">
/// <item>a CONTROL: the envelope, authored 40×40, draws square by the HUD's mapping;</item>
/// <item>park start: the head hidden while idle; the head's origin, disc and root axes measured off its drawn
/// nodes (advisor-visuals.md §2: 80% across, 75% down, 0.013 on both screen axes, the 4/3 on depth); the greeting (171, voice only) rises with its costume, speaks
/// with the talk record its length picks, its mouth following the lip track, drops (queued behind the talk
/// pass) and cools down 100 ticks; Music and SFX duck to 25 by 8 a pass and come back; it never reaches the
/// stack;</item>
/// <item>a RULE fires through its real variables: the one hook sets event counter 0x15 (the litter counter a
/// guest's dropped litter raises, `ParkStaff.AdvisorEvent`) and the scheduler's rule 47 posts PRANK_LITTER --
/// text and voice -- which lands in the stack: the envelope counts it;</item>
/// <item>the stack: L2 (C) opens it, the text shows in the box, Delete removes it; the READ box (advisor-visuals.md
/// §1): wboxfill as its face, the blue Mess corners and edges as its frame at the read geometry, the fill behind,
/// Small.bff text in (48,48,48) centred on x 320 with its ⚠ inferred shadow, one framebuffer pixel of the face;</item>
/// <item>a ride's breakdown (⚠ its reliability forced, <see cref="ParkRide.ForceReliabilityForTest"/>, the
/// mechanic smoke's hook) posts its messages WITH the ride: Select jumps the camera there; deleting the ride
/// (the viewer's DeletePlaced → ParkSim.Remove) takes its records, and a jump to a ride that has gone is
/// refused;</item>
/// <item>a paused park pauses the voice; a modal message (⚠ 268 submitted directly: the tutorial dispatcher that
/// sends it is out of scope) interrupts the speaking rule message -- the voice stopped, the exit cut short, the
/// message lost -- then locks the pad, flaps its mouth with no lip track, and Triangle (T) skips it;</item>
/// <item>Tutorial On/Off is flag 0x40; Close Park takes the head off the screen and stops the voice; the park's
/// teardown frees the head.</item>
/// </list>
/// Run with --map=WORLD --mode=park. `TPW_ADVISOR_SHOT=dir` saves the head rising, the head up and talking,
/// the envelope, the stack open with a message and the modal message.</summary>
public partial class AdvisorSmoke : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string name) => typeof(Viewer).GetField(name, Hidden)
        ?? throw new MissingMemberException("Viewer." + name);
    static T Field<T>(Viewer v, string name) => (T)Member(name).GetValue(v);
    static void Set(Viewer v, string name, object value) => Member(name).SetValue(v, value);
    static object Call(Viewer v, string name, params object[] args)
    {
        var method = typeof(Viewer).GetMethod(name, Hidden | BindingFlags.Public) ?? throw new MissingMemberException("Viewer." + name);
        var parameters = method.GetParameters();
        if (args.Length < parameters.Length)
            args = args.Concat(parameters.Skip(args.Length).Select(p => p.HasDefaultValue ? p.DefaultValue
                : throw new ArgumentException("Missing required argument for Viewer." + name))).ToArray();
        return method.Invoke(v, args);
    }

    /// <summary>Rides whose script answers a breakdown (the mechanic smoke's list, a core probe of 2026-09-27).</summary>
    static readonly Dictionary<string, string[]> Subjects = new()
    {
        ["JUNGLE"] = new[] { "snake", "Totem", "IncaGod", "Bouncy" },
        ["FANTASY"] = new[] { "bigapple", "bbugs", "flyfoun" },
        ["HALLOW"] = new[] { "candle", "Demon", "brainb" },
        ["SPACE"] = new[] { "slide", "spawheel", "bumper", "mbuggy" },
    };

    // ---------------------------------------------------------------------------------------------
    // The research's numbers (ghidra_tpw/notes/advisor-V-visuals.md), the test's own copy -- not the view's code.

    /// <summary>§2.3: the head's origin, NDC (0.6, −0.5) = 80% across, 75% down; (409.6, 384) in HUD units.</summary>
    const float HeadAcross = 0.8f, HeadDown = 0.75f;
    /// <summary>§2.3: the `Bug Head` disc in the bind pose, in HUD units: x 373.8–445.4, y 348.2–419.8.</summary>
    const float DiscWideUnits = 445.4f - 373.8f, DiscTallUnits = 419.8f - 348.2f;
    /// <summary>§2.2: `0x16FD18(s, 4s/3, s)` -- the root's local y row, which its bind turns into depth, is 4/3 the others.</summary>
    const float DepthRow = 4f / 3f;

    /// <summary>The head AS DRAWN: the `Bug Head` disc's vertices through its drawn surfaces' global transforms and
    /// the overlay's camera (window pixels: the overlay's viewport is the window's size), the root node's origin
    /// the same way, and the root's axes in the overlay's world -- (x, y, z) lengths of its local x, y, z images and
    /// how far its local y lies along the camera's view axis.</summary>
    static (Rect2 Disc, Vector2 Origin, Vector3 Axes, float YOnDepth) MeasureHead(AdvisorHead head)
    {
        var cam = head.Camera;
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var (mesh, _, node) in head.Drawn.Surfaces())
        {
            if (!mesh.Equals("Bug Head", StringComparison.OrdinalIgnoreCase) || !node.Visible || node.Mesh is not ArrayMesh am) continue;
            for (int surface = 0; surface < am.GetSurfaceCount(); surface++)
                foreach (var v in (Vector3[])am.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex])
                {
                    var p = cam.UnprojectPosition(node.GlobalTransform * v);
                    x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
                }
        }
        var w = head.Drawn.LastWorld[head.RootOffset];
        var root = head.Drawn.Root.GlobalTransform * new Transform3D(
            new Basis(new Vector3(w.M11, w.M12, w.M13), new Vector3(w.M21, w.M22, w.M23), new Vector3(w.M31, w.M32, w.M33)),
            new Vector3(w.M41, w.M42, w.M43));
        var view = -cam.GlobalTransform.Basis.Z.Normalized();
        return (new Rect2(x0, y0, x1 - x0, y1 - y0), cam.UnprojectPosition(root.Origin),
                new Vector3(root.Basis.X.Length(), root.Basis.Y.Length(), root.Basis.Z.Length()),
                Math.Abs(root.Basis.Y.Normalized().Dot(view)));
    }

    /// <summary>§1.2: `0x20ACF8` breaks lines on `\n`, `\r`, `\r\n` or `\n\r` and never wraps (the test's copy).</summary>
    static List<string> BreakLines(string text)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n' && text[i] != '\r') continue;
            lines.Add(text[start..i]);
            if (i + 1 < text.Length && text[i + 1] != text[i] && (text[i + 1] == '\n' || text[i + 1] == '\r')) i++;
            start = i + 1;
        }
        lines.Add(text[start..]);
        return lines;
    }

    /// <summary>The mean colour of a texture's pixels at least half opaque, how many that is, and whether every pixel
    /// is opaque.</summary>
    static (Color Mean, bool Opaque, int Counted) Art(Texture2D tex)
    {
        var img = tex.GetImage();
        float r = 0, g = 0, b = 0; int n = 0; bool opaque = true;
        for (int y = 0; y < img.GetHeight(); y++)
            for (int x = 0; x < img.GetWidth(); x++)
            {
                var c = img.GetPixel(x, y);
                if (c.A < 1f) opaque = false;
                if (c.A < 0.5f) continue;
                r += c.R; g += c.G; b += c.B; n++;
            }
        return (n == 0 ? new Color(0, 0, 0, 0) : new Color(r / n, g / n, b / n), opaque, n);
    }

    int _checks;
    void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++;
        GD.Print("ADVISOR SMOKE ok: " + label);
    }

    async Task Frames(int n = 2)
    {
        for (int i = 0; i < n; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }

    /// <summary>One pass of the advisor, as the park runs it, and what the view shows after it.</summary>
    sealed record Pass(long Tick, AdvisorState State, int Record, int Queued, bool Held, bool Up, int ElapsedMs,
                       int Mouth, int MouthShown, int[] MouthsDrawn, bool Gate, bool Lip, int Sfx, int Music, int SfxTarget,
                       int MusicTarget, float SfxGain, float SfxBusDb, bool Voice, bool Speaking);

    /// <summary>findings/advisor-messages.md §4.6, the costume selector → the hat's fitting id (the test's own
    /// copy of the research table, not the head's code).</summary>
    static readonly Dictionary<int, int> Hat = new() { [5] = 1, [6] = 3, [7] = 7, [8] = 5, [9] = 8, [10] = 4, [11] = 10, [12] = 2, [13] = 6, [14] = 9 };

    public override async void _Ready()
    {
        string world = "unknown";
        try
        {
            Check(DisplayServer.GetName() != "headless", "rendering display required");
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            string map = args.LastOrDefault(a => a.StartsWith("--map="))?["--map=".Length..]
                ?? throw new ArgumentException("Pass --map=WORLD");
            world = new[] { "JUNGLE", "FANTASY", "HALLOW", "SPACE" }.Single(w =>
                map.Equals(w, StringComparison.OrdinalIgnoreCase) || map.StartsWith(w + " ", StringComparison.OrdinalIgnoreCase));
            string park2 = map.Contains("terrain_2", StringComparison.OrdinalIgnoreCase) ? "2" : "1";
            string shots = System.Environment.GetEnvironmentVariable("TPW_ADVISOR_SHOT");
            if (shots != null) System.IO.Directory.CreateDirectory(shots);
            string ShotPath(string name) => System.IO.Path.Combine(shots, $"{world.ToLowerInvariant()}{park2}_{name}.png");

            var viewer = new Viewer { Name = "Viewer" };
            Set(viewer, "_guestCap", 0);          // no arrivals: the advisor is the subject
            AddChild(viewer);
            viewer.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var park = Field<Park>(viewer, "_park");
            var lib = Field<AssetLibrary>(viewer, "_lib");
            var terrain = Field<Model>(viewer, "_terrainModel");
            var entrances = Field<ParkEntrance>(viewer, "_entranceTable");
            var text = Field<TextDatabase>(viewer, "_text");
            Check(park?.Field != null && terrain != null && entrances != null && lib != null && text != null, "the park loaded");
            var entry = entrances.Fit(terrain.Field, ParkEntrance.WalkwayColumnFromPoles(terrain), out _);
            Check(!entry.Empty, "the authored entrance fits");

            // The mechanic smoke's ground: a corridor in from the gate and three bars across it, laid by the path tool.
            var paths = Field<PathTool>(viewer, "_paths");
            var corridor = new List<(int X, int Y)>();
            for (int z = entry.ZEnd; z < entry.ZEnd + 18; z++)
                if (park.IsPlayable(entry.XCol, z) && park.Vacant(entry.XCol, z)) corridor.Add((entry.XCol, z));
            Call(viewer, "LayLeg", corridor, PathTool.Kind.Path, 0);
            var bar = new List<(int X, int Y)>();
            foreach (int barZ in new[] { entry.ZEnd + 3, entry.ZEnd + 10, entry.ZEnd + 17 })
            {
                var leg = new List<(int X, int Y)>();
                for (int x = entry.XCol - 10; x <= entry.XCol + 10; x++)
                    if (park.IsPlayable(x, barZ) && park.Vacant(x, barZ)) leg.Add((x, barZ));
                Call(viewer, "LayLeg", leg, PathTool.Kind.Path, 0);
                bar.AddRange(leg);
            }
            Call(viewer, "RefreshFloor");
            Check((bool)Call(viewer, "OpenGate"), "the guest layer opened on the corridor");
            var grid = Field<GuestWalk>(viewer, "_guests").Paths;
            var open = corridor.Concat(bar).Where(c => grid.Open(new ParkCell(c.X, c.Y))).ToHashSet();
            var laid = new List<(int X, int Y)>();
            if (corridor.Count > 0 && open.Contains(corridor[0]))
            {
                var frontier = new Queue<(int X, int Y)>(); frontier.Enqueue(corridor[0]);
                var seen = new HashSet<(int X, int Y)> { corridor[0] };
                while (frontier.TryDequeue(out var c))
                {
                    laid.Add(c);
                    foreach (var n in new[] { (c.X + 1, c.Y), (c.X - 1, c.Y), (c.X, c.Y + 1), (c.X, c.Y - 1) })
                        if (open.Contains(n) && seen.Add(n)) frontier.Enqueue(n);
                }
            }
            Check(laid.Count >= 12, $"{laid.Count} path cells are open to walk and joined to the gate");
            Call(viewer, "LoadHudFont");
            Set(viewer, "_freeCam", false);
            Call(viewer, "TickPark");

            // ---------------------------------------------------------------------------------
            // The advisor, built with the park's staff.
            var adv = Field<ParkAdvisor>(viewer, "_parkAdvisor");
            var head = Field<AdvisorHead>(viewer, "_advisorHead");
            var stackView = Field<AdvisorStackView>(viewer, "_advisorStackView");
            var voice = Field<AudioStreamPlayer>(viewer, "_advisorVoice");
            var mix = Field<GameAudioMix>(viewer, "_audioMix");
            var settings = viewer.Settings;
            var sim = Field<ParkSim>(viewer, "_sim");
            Check(adv != null && head != null && ReferenceEquals(adv.Head, head) && stackView != null && voice != null,
                  $"the viewer built the park's advisor with the drawn head (registry 488: {head?.Summary})");
            Check(adv.Flags == (ParkAdvisor.ParkStartFlags | ParkAdvisor.FlagRules | (settings.Tutorial ? ParkAdvisor.FlagTutorial : 0)),
                  $"park-start flags 0x{adv.Flags:X2} (0x37 | 8, | 0x40 with Tutorial {(settings.Tutorial ? "on" : "off")})");
            var uiSounds = new List<int>();
            var priorUi = adv.UiSound; adv.UiSound = s => { uiSounds.Add(s); priorUi?.Invoke(s); };
            var stackSounds = new List<int>();
            var priorStackUi = adv.Stack.UiSound; adv.Stack.UiSound = s => { stackSounds.Add(s); priorStackUi?.Invoke(s); };
            var played = new List<AdvisorPlayback>();
            var priorPlayed = adv.Played; adv.Played = p => { played.Add(p); priorPlayed?.Invoke(p); };
            int masterBus = AudioServer.GetBusIndex("Master");
            float masterDb = AudioServer.GetBusVolumeDb(masterBus);

            async Task Present(int frames = 3)
            {
                Call(viewer, "ShowMoney");
                Call(viewer, "PresentAdvisor");
                await Frames(frames);
            }
            async Task Shot(string name)
            {
                if (shots == null) { await Present(1); return; }
                // The F3 panel off for the picture, as F3 does: it covers the bottom-left HUD the shot is of.
                var debugPanel = Field<Control>(viewer, "_panel");
                bool was = debugPanel?.Visible ?? false;
                if (debugPanel != null) debugPanel.Visible = false;
                await Present(3);
                Call(viewer, "SaveShot", ShotPath(name));
                if (debugPanel != null) debugPanel.Visible = was;
            }
            int sfxBus = AudioServer.GetBusIndex(GameAudioMix.SfxBus);
            Pass Snap() => new(adv.Ticks, adv.State, head.Channel.Record, head.Channel.Queued, head.Channel.EndHeld, head.Up,
                               head.Channel.ElapsedMs, adv.MouthShape, head.MouthShown, head.MouthsDrawn.ToArray(), adv.LipGate,
                               adv.LipTrackAttached, mix.Sfx, mix.Music, mix.SfxTarget, mix.MusicTarget, mix.SfxGain,
                               AudioServer.GetBusVolumeDb(sfxBus), voice.Playing, adv.Speaking);
            // One pass as the running viewer makes it (_Process): a park tick and the calendar's share of a pass.
            void Tick() { Call(viewer, "TickPark"); Call(viewer, "AdvanceCalendar", ParkClock.UnitsPerPass); }
            async Task<List<Pass>> RunCycle(Func<bool> start, int max, string risingShot = null, string talkingShot = null,
                                             Func<int, Task> speakingPass = null)
            {
                var list = new List<Pass>();
                bool began = false; int speaking = -1;
                for (int t = 0; t < max; t++)
                {
                    Tick();
                    var p = Snap();
                    list.Add(p);
                    began |= start();
                    if (risingShot != null && p.State == AdvisorState.Entering && p.ElapsedMs == 80) { await Shot(risingShot); risingShot = null; }
                    if (p.State == AdvisorState.Speaking && speaking < 0) speaking = t;
                    if (speakingPass != null && p.State == AdvisorState.Speaking && speaking >= 0) await speakingPass(t - speaking);
                    if (talkingShot != null && speaking >= 0 && t == speaking + 30) { await Shot(talkingShot); talkingShot = null; }
                    if (began && p.State == AdvisorState.Idle && list.Count > 2 && list[^2].State == AdvisorState.Cooldown) break;
                }
                return list;
            }

            await Present();
            Check(!head.Up && !head.Overlay.Visible && adv.State == AdvisorState.StartDelay,
                  "idle, the head is hidden: nothing on channel 0, the overlay not drawn (the start delay)");
            Check(stackView.EnvelopeShown && stackView.ShownCount == 0,
                  $"the envelope is drawn bottom left with its count {stackView.ShownCount} (0x108D40 at (32, 420), \"%d\" at (80, 430))");
            // The port's HUD mapping, the test's own copy: a POSITION is a fraction of the window, a SIZE goes by one
            // factor from the height. A control for the head below: an authored 40×40 must draw square by it.
            var win = viewer.GetViewport().GetVisibleRect().Size;
            float kH = win.Y / 512f;
            var env = stackView.EnvelopeRect;
            float envRatio = env.Size.Y / env.Size.X;
            Check(Math.Abs(envRatio - 1f) < 1e-4f && Math.Abs(env.Size.Y - 40 * kH) < 0.01f
                  && env.Position.DistanceTo(new Vector2(32 / 512f * win.X, 420 / 512f * win.Y)) < 0.01f,
                  $"CONTROL: the envelope, authored 40×40 at (32, 420), draws {env.Size.X:0.##}×{env.Size.Y:0.##} px at ({env.Position.X:0.#}, {env.Position.Y:0.#}) "
                  + $"in a {win.X}×{win.Y} window -- height/width {envRatio:0.####}: square, so the HUD mapping carries no pixel-aspect factor");

            // ---------------------------------------------------------------------------------
            // 1. The greeting: 171 LOST_KINGDOM_FIRST_GOALS (no gold ticket ever earned), voice only.
            var greetMsg = Field<AdvisorCatalogue>(viewer, "_advisor").Messages[ParkAdvisor.GreetingFirst];
            var heads = new List<(int At, Rect2 Disc, Vector2 Origin, Vector3 Axes, float YOnDepth)>();
            var g = await RunCycle(() => played.Any(p => p.Id == ParkAdvisor.GreetingFirst), 1500, "rising", "talking",
                async since =>
                {
                    if (since != 0 && since != 30) return;
                    await Present(1);
                    var m = MeasureHead(head);
                    heads.Add((since, m.Disc, m.Origin, m.Axes, m.YOnDepth));
                    GD.Print($"[advisor smoke] head at speaking+{since}: disc {m.Disc.Size.X:0.##}×{m.Disc.Size.Y:0.##} px at "
                             + $"({m.Disc.Position.X:0.#}, {m.Disc.Position.Y:0.#}), origin ({m.Origin.X:0.##}, {m.Origin.Y:0.##}), "
                             + $"root axes {m.Axes.X:0.#####}/{m.Axes.Y:0.#####}/{m.Axes.Z:0.#####}, local y on depth {m.YOnDepth:0.###}");
                });
            var gp = played.FirstOrDefault(p => p.Id == ParkAdvisor.GreetingFirst);
            Check(gp.Id == ParkAdvisor.GreetingFirst && gp.SoundId != 0 && !gp.TextAdded,
                  $"the greeting 171 is presented after the 50-tick start delay: voice sound {gp.SoundId}, no text row ({greetMsg.TextRow})");
            var enter = g.Where(p => p.State == AdvisorState.Entering).ToList();
            var talk = g.Where(p => p.State == AdvisorState.Speaking).ToList();
            var exit = g.Where(p => p.State == AdvisorState.Exiting).ToList();
            var cool = g.Where(p => p.State == AdvisorState.Cooldown).ToList();
            // 13 ends on the first update STRICTLY past 30 frames: 40·n·30/1000 > 30 at n = 26 -- the take's pass plus 25.
            int risePasses = Enumerable.Range(1, 100).First(n => 40 * n * 30 / 1000f > 30);
            Check(enter.Count == risePasses && enter.All(p => p.Record == ParkAdvisor.RecordEnter && p.Up && !p.Held),
                  $"it RISES: record 13 on channel 0, the head drawn, for {enter.Count} passes -- the take's and {risePasses - 1} more, until 13 is past its 30 frames");
            int selector = greetMsg.Voices[gp.Variant].AnimationSelector;
            int helmet = head.FittingMesh(4), plainL = head.FittingMesh(19), plainR = head.FittingMesh(20), hardhat = head.FittingMesh(5);
            Check(head.Dressed == selector && Hat[selector] == 4 && head.Drawn.MeshDrawn(helmet) && !head.Drawn.MeshDrawn(plainL)
                  && !head.Drawn.MeshDrawn(plainR) && !head.Drawn.MeshDrawn(hardhat) && head.Drawn.MeshDrawn(head.FittingMesh(21)),
                  $"its COSTUME is the variant's byte {selector}: the Pith Helmet (fitting 4, mesh {helmet}) drawn, the plain antennae and the other hats not, the hands shown (0x107160)");
            int talkRecord = ParkAdvisor.TalkRecord(adv.SpeechLengthMs);
            Check(talk.Count > 0 && voice.Stream is AudioStreamWav w && Math.Abs((int)Math.Round(w.GetLength() * 1000) - gp.SpeechMs) <= 1
                  && gp.TalkRecord == talkRecord && talk.All(p => p.Record == talkRecord && p.Up) && talk.First().Voice,
                  $"it SPEAKS: the voice plays the {gp.SpeechMs} ms stream (its decoded length is what the state machine was fed) and channel 0 loops talk record {gp.TalkRecord} (§4.3 for {gp.SpeechMs} ms)");
            Check(talk.Count == (gp.SpeechMs + 39) / 40,
                  $"state 3 lasts the speech: {talk.Count} passes of 40 ms for {gp.SpeechMs} ms");
            // The head's placement and size, measured off the drawn nodes (§2.3, the bind pose = the talk pose).
            var h0 = heads.FirstOrDefault(x => x.At == 0);
            Check(heads.Count == 2 && h0.Disc.Size.X > 0,
                  $"the head was measured as drawn at the start of its speech and 30 passes in ({heads.Count} readings)");
            var wantOrigin = new Vector2(HeadAcross * win.X, HeadDown * win.Y);
            Check(heads.All(x => x.Origin.DistanceTo(wantOrigin) < 1f),
                  $"the head's ORIGIN (its root node, projected) is at {string.Join(", ", heads.Select(x => $"({x.Origin.X:0.##}, {x.Origin.Y:0.##})"))} px: "
                  + $"80% across and 75% down ({wantOrigin.X:0.##}, {wantOrigin.Y:0.##}) -- NDC (0.6, −0.5), (409.6, 384) in HUD units");
            float discRatio = h0.Disc.Size.Y / h0.Disc.Size.X, wantRatio = DiscTallUnits / DiscWideUnits;
            Check(Math.Abs(h0.Disc.Size.Y / (DiscTallUnits * kH) - 1f) < 0.04f && Math.Abs(h0.Disc.Size.X / (DiscWideUnits * kH) - 1f) < 0.04f
                  && Math.Abs(discRatio - wantRatio) < 0.04f,
                  $"the head's disc draws {h0.Disc.Size.X:0.#}×{h0.Disc.Size.Y:0.#} px, height/width {discRatio:0.###}: the research's "
                  + $"{DiscWideUnits:0.#}×{DiscTallUnits:0.#} HUD units ({wantRatio:0.###}) by the envelope's one-factor rule "
                  + $"({DiscWideUnits * kH:0.#}×{DiscTallUnits * kH:0.#}) -- 0.013 on both screen axes, not 4/3 of it");
            Check(heads.All(x => Math.Abs(x.Axes.Y / x.Axes.X - DepthRow) < 1e-3f && Math.Abs(x.Axes.Z / x.Axes.X - 1f) < 1e-3f && x.YOnDepth > 0.999f),
                  $"the 4/3 is the root's own local y row (0x16FD18(s, 4s/3, s)) and it lies along the camera's view axis "
                  + $"({h0.YOnDepth:0.####}): root axes x {h0.Axes.X:0.#####}, y {h0.Axes.Y:0.#####}, z {h0.Axes.Z:0.#####} -- depth, which the orthographic layer never shows");
            var written = talk.Where(p => p.MouthShown >= 0).ToList();
            var mouthsSeen = written.Select(p => string.Join("+", p.MouthsDrawn)).Distinct().ToList();
            bool meshFollows = written.Count > talk.Count / 2 && written.All(p => p.MouthsDrawn.Length == 1 && p.MouthsDrawn[0] == head.MouthMesh(p.Mouth));
            // The marks: where the gate flips, a consumed mark drew Aah (gate set) or Normal (gate clear).
            var flips = talk.Zip(talk.Skip(1)).Where(x => x.First.Gate != x.Second.Gate).Select(x => x.Second).ToList();
            bool marksDrawn = flips.Count > 0 && flips.All(p => p.MouthsDrawn.Length == 1 && p.MouthsDrawn[0] == head.MouthMesh(p.Gate ? 1 : 0));
            Check(talk.All(p => p.Lip) && flips.Count >= 3, $"a lip track rides with the voice ({adv.Ticks}: {flips.Count} gate flips while it speaks)");
            Check(meshFollows && marksDrawn && mouthsSeen.Count >= 2,
                  $"its MOUTH follows the lip track: every pass after the first mouth write exactly one mouth mesh is drawn, the core's shape, "
                  + $"and on each of the {flips.Count} marks it is Aah (gate set) or Normal (gate clear) -- {mouthsSeen.Count} meshes seen: {string.Join(",", mouthsSeen)}");
            int waiting = exit.Count(p => p.Record == talkRecord), dropping = exit.Count(p => p.Record == ParkAdvisor.RecordExit);
            int dropPasses = Enumerable.Range(1, 100).First(n => 40 * n * 30 / 1000f > 20);
            Check(exit.Count > 0 && exit.Where(p => p.Record == talkRecord).All(p => p.Queued == ParkAdvisor.RecordExit && p.Up)
                  && exit.SkipWhile(p => p.Record == talkRecord).All(p => p.Record == ParkAdvisor.RecordExit && !p.Held && p.Up)
                  && dropping == dropPasses && cool.Count > 0 && cool[0].Record == ParkAdvisor.RecordExit && cool[0].Held,
                  $"it DROPS: the exit (record 14, flags 0) waits QUEUED while the talk record finishes its pass ({waiting} passes), then plays for "
                  + $"{dropping} passes until it is held past its 20 frames");
            Check(cool.Count == ParkAdvisor.CooldownTicks && cool.All(p => !p.Up),
                  $"then the 100-tick cooldown ({cool.Count} passes), the head hidden, before the advisor is idle again");
            // Ducking, 0x1066B0 states 2/4 and 0x151C00 -- the test's own copy of the ramp (8 a pass, landing from within 8).
            static int Toward(int live, int target) => live == target ? live : Math.Abs(live - target) < 9 ? target : live > target ? live - 8 : live + 8;
            int setting = settings.Sfx;
            // The take is the pass that ENTERS state 2; state 2's body ducks from the next pass on.
            var ducked = g.SkipWhile(p => p.State != AdvisorState.Entering).Skip(1).TakeWhile(p => p.State != AdvisorState.Exiting).ToList();
            var down = ducked.Select(p => p.Sfx).Distinct().ToList();
            var expectDown = new List<int> { setting };
            while (expectDown[^1] != 25) expectDown.Add(Toward(expectDown[^1], 25));
            Check(setting > 25 && ducked.All(p => p.SfxTarget == 25 && p.MusicTarget == 25)
                  && down.SequenceEqual(expectDown.Skip(1)) && ducked.Last().Sfx == 25 && ducked.Last().Music == 25
                  && Math.Abs(ducked.Last().SfxGain - 25f / setting) < 1e-4 && Math.Abs(ducked.Last().SfxBusDb - Mathf.LinearToDb(25f / setting)) < 0.01f,
                  $"while it is up MUSIC and SFX DUCK to 25: targets 25, live {setting} → {string.Join(" → ", down)} by 8 a pass, the SFX bus at {25f / setting:0.###} of its level");
            var after = g.SkipWhile(p => p.State != AdvisorState.Exiting).ToList();
            var up = after.Select(p => p.Sfx).Distinct().ToList();
            var expectUp = new List<int> { 25 };
            while (expectUp[^1] != setting) expectUp.Add(Toward(expectUp[^1], setting));
            Check(after.All(p => p.SfxTarget == setting) && up.SequenceEqual(expectUp.Skip(1)) && after.Last().Music == settings.Music
                  && Math.Abs(mix.SfxGain - 1f) < 1e-6 && Math.Abs(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(GameAudioMix.SfxBus))) < 0.01f
                  && Math.Abs(AudioServer.GetBusVolumeDb(masterBus) - masterDb) < 1e-4 && voice.Bus == "Master",
                  $"and COME BACK from the exit on: targets {setting}, live {string.Join(" → ", up)}, the SFX bus back at 1; the speech is on Master, which never moved");
            Check(!gp.TextAdded && greetMsg.TextRow == AdvisorCatalogue.BlankTextRow && adv.Stack.Count == 0,
                  "a VOICE-ONLY message never reaches the stack: 171 (row 310) was spoken and the stack is still empty");

            // ---------------------------------------------------------------------------------
            // 2. A ride's breakdown: its messages carry the ride into the stack.
            Call(viewer, "ShowBuildCategory", "Rides");
            var rows = Field<List<int>>(viewer, "_buildRows");
            string Leaf(AssetLibrary.RideAssets a) => System.IO.Path.GetFileNameWithoutExtension(a.Model?.Path ?? a.Name);
            var candidates = Subjects[world].SelectMany(stem => Enumerable.Range(0, rows.Count)
                    .Where(i => Leaf(lib.Rides[rows[i]]).Equals(stem, StringComparison.OrdinalIgnoreCase)))
                .Where(i => ((RideDefinition)Call(viewer, "DefinitionFor", lib.Rides[rows[i]].Model))?.CompiledEntry?.Kind == AssetResourceDatabase.AssetKind.Ride)
                .ToList();
            var blueprint = Field<Placement>(viewer, "_place");
            bool placed = false;
            foreach (int row in candidates)
            {
                for (int turn = 0; turn < 4 && !placed; turn++)
                {
                    Call(viewer, "ArmFromList", row); blueprint.Turn(turn);
                    for (int z = entry.ZEnd + 1; z < entry.ZEnd + 18 && !placed; z++)
                        for (int x = entry.XCol - 12; x <= entry.XCol + 12 && !placed; x++)
                        {
                            if (!blueprint.Fits(park, x, z)) continue;
                            var stubs = blueprint.Stubs(park, x, z).ToArray();
                            if (stubs.Length == 0 || !stubs.All(s => ParkPaths.Neighbours(new(s.X, s.Y)).Any(c => laid.Contains((c.X, c.Z))))) continue;
                            int count = park.Placed.Count;
                            Set(viewer, "_cursorOverride", (x, z));
                            try { Call(viewer, "PlaceHeld"); } finally { Set(viewer, "_cursorOverride", null); }
                            placed = park.Placed.Count == count + 1;
                        }
                }
                if (placed) break;
            }
            Check(placed, "the build tool placed a ride that answers a breakdown");
            Call(viewer, "CloseTool");
            for (int i = 0; i < 400; i++) Tick();
            var ride = sim.Rides.Single(x => x.ServiceClass == RideServiceClass.Ordinary);
            ride.ForceReliabilityForTest(0x5000);      // ⚠ the mechanic smoke's hook: 5.0, below 10.0 -- it breaks
            for (int i = 0; i < 1500 && !adv.Stack.Records.Any(x => ReferenceEquals(x.Object, ride)); i++) Tick();
            for (int i = 0; i < 1500 && !(adv.State == AdvisorState.Idle && adv.RingCount == 0 && !adv.PendingSet); i++) Tick();
            var about = adv.Stack.Records.Where(x => ReferenceEquals(x.Object, ride)).ToList();
            Check(about.Count >= 1 && about.All(x => x.Type == AdvisorRecordType.Object),
                  $"{ride.Name} breaks down: its message reaches the stack as a type-2 record WITH the ride (0x107CB0) -- {about.Count} of them");

            // ---------------------------------------------------------------------------------
            // 3. A rule through its real variables: counter 0x15 (litter dropped far from a bin) → rule 47.
            int stackBefore = adv.Stack.Count;
            // ⚠ THE ONE TEST HOOK of this step: event counter 0x15, +1 -- what ParkVisitors raises when a guest drops
            // litter with no bin in 5 (0x1073C0 → 0x10DDD8). The scheduler, the snapshot into v77 and rule 47 are real.
            adv.CountEvent(0x15, 1);
            AdvisorRules.Result? rule47 = null;
            var r = await RunCycle(() =>
            {
                if (adv.Scheduler.Last.ConsideredRule == 47 && adv.Scheduler.Last.Due) rule47 ??= adv.Scheduler.Last.Result;
                return played.Any(p => p.Id == 0x64);
            }, 3000, null, "talking-litter");
            var lp = played.FirstOrDefault(p => p.Id == 0x64);
            var litterMsg = Field<AdvisorCatalogue>(viewer, "_advisor").Messages[0x64];
            Check(rule47 == AdvisorRules.Result.Completed && lp.Id == 0x64 && lp.TextAdded && lp.TextRow == litterMsg.TextRow && lp.SoundId != 0,
                  $"rule 47 fires from its real variables (v77 = counter 0x15's snapshot > 0) and posts 0x64 PRANK_LITTER: text row {lp.TextRow} and voice {lp.SoundId}");
            int costume = litterMsg.Voices[lp.Variant].AnimationSelector;
            int hat = Hat.TryGetValue(costume, out var h) ? head.FittingMesh(h) : -1;
            Check(head.Dressed == costume && hat >= 0 && head.Drawn.MeshDrawn(hat) && !head.Drawn.MeshDrawn(helmet),
                  $"PRANK_LITTER is dressed by its own byte {costume}: fitting {h} (mesh {hat}) drawn, the greeting's Pith Helmet put away");
            await Present();
            Check(adv.Stack.Count == stackBefore + 1 && adv.Stack.Records[^1].Row == (short)litterMsg.TextRow
                  && stackView.ShownCount == stackBefore + 1,
                  $"a TEXT message lands in the stack and the envelope's drawn count goes {stackBefore} → {stackView.ShownCount}");
            await Shot("envelope");

            // ---------------------------------------------------------------------------------
            // 4. The stack: L2 opens it on the oldest, the SELECTED record's text shows, Delete removes one, Select jumps.
            void Key(Key key)
            {
                viewer._UnhandledKeyInput(new InputEventKey { Keycode = key, Pressed = true });
            }
            async Task Settle() { for (int i = 0; i < 12; i++) Tick(); await Present(); }
            string Words(AdvisorStackRecord x) => x.Row == -1 ? x.Text : text.Text("eng", x.Row);
            Key(Viewer.AdvisorL2Key);
            Tick();
            Check(adv.Stack.IsOpen && adv.Stack.Cursor == 0, $"L2 ({Viewer.AdvisorL2Key}) opens the stack on the next pass, the cursor on the OLDEST record");
            await Settle();
            var oldest = adv.Stack.Records[0];
            Check(ReferenceEquals(oldest.Object, ride) && stackView.ShownText == Words(oldest) && stackView.ShownRecords >= 2,
                  $"the box shows the SELECTED (oldest) record's text, not the newest's: \"{stackView.ShownText?.Replace("\n", " ")}\"; {stackView.ShownRecords} records drawn");
            for (int i = 0; i < adv.Stack.Count && adv.Stack.Selected?.Row != (short)litterMsg.TextRow; i++) { Key(Godot.Key.Up); Tick(); }
            await Settle();
            string words = text.Text("eng", litterMsg.TextRow);
            Check(adv.Stack.Selected?.Row == (short)litterMsg.TextRow && stackView.ShownText == words && !string.IsNullOrEmpty(words),
                  $"Up moves to the newer record and its text is drawn in the 260-wide box from the text database: \"{stackView.ShownText?.Replace("\n", " ")}\"");
            // ---- The READ box (§1.3..§1.5), the test's own copy of the geometry in HUD units, through the port's mapping:
            // the box's centre line x 320 is a position (a fraction of the window), every other x hangs off it by k.
            var art = stackView.Panel;
            float cx = 320 / 512f * win.X;
            Rect2 Units(float u, float v, float w, float h) => new(cx + (u - 320) * kH, v * kH, w * kH, h * kH);
            bool Same(Rect2 a, Rect2 b) => a.Position.DistanceTo(b.Position) < 0.01f && a.Size.DistanceTo(b.Size) < 0.01f;
            var blits = stackView.BoxBlits.ToList();
            var fills = blits.Where(b => b.Piece == "fill").ToList();
            // The face as DRAWN (the first row's texture), against the disc's pale opaque wboxfill (§1.4).
            var face = fills.Count > 0 ? Art(fills[0].Texture) : default;
            Check(art != null && fills.Count == 13 && fills.All(b => ReferenceEquals(b.Texture, art.Sheet) && b.Modulate == Colors.White)
                  && fills.Select((b, i) => Same(b.Rect, Units(174, 196 + 16 * i, 292, 16))).All(x => x)
                  && face.Opaque && face.Counted > 0 && face.Mean.R > 0.6f && face.Mean.G > 0.85f && face.Mean.B > 0.85f,
                  $"the box's FACE is wboxfill (sprite 0x31): {fills.Count} rows drawn, the face opaque {face.Opaque}, mean ({face.Mean.R8},{face.Mean.G8},{face.Mean.B8}) over {face.Counted} texels at least half opaque; "
                  + "13 rows of 292×16 stretched across from (174, 196) -- the widget rect widened OUTWARD by its 16×14 margins (0x1DD1E8, 0x142090)");
            var frame = blits.Where(b => b.Piece is "corner" or "edge").ToList();
            var wantFrame = art == null ? new List<(string, Texture2D, Rect2)>() : new List<(string, Texture2D, Rect2)>
            {
                ("corner", art.CornerTopRight, Units(466, 188, 8, 8)), ("corner", art.CornerBottomRight, Units(466, 404, 8, 8)),
                ("corner", art.CornerTopLeft, Units(166, 188, 8, 8)), ("corner", art.CornerBottomLeft, Units(166, 404, 8, 8)),
                ("edge", art.EdgeTop, Units(174, 188, 292, 8)), ("edge", art.EdgeBottom, Units(174, 404, 292, 8)),
                ("edge", art.EdgeRight, Units(466, 196, 8, 208)), ("edge", art.EdgeLeft, Units(166, 196, 8, 208)),
            };
            var frameArt = frame.Select(b => Art(b.Texture)).ToList();
            var outer = frame.Select(b => b.Rect).DefaultIfEmpty().Aggregate((a, b) => a.Merge(b));
            Check(frame.Count == 8 && wantFrame.All(f => frame.Count(b => b.Piece == f.Item1 && ReferenceEquals(b.Texture, f.Item2) && Same(b.Rect, f.Item3)) == 1)
                  && frame.All(b => b.Modulate == Colors.White) && frameArt.All(a => a.Counted > 0 && a.Mean.B > 0.8f && a.Mean.R < 0.2f && a.Mean.G < 0.25f)
                  && Same(outer, Units(166, 188, 308, 224)),
                  "its FRAME is the stack's blue messcorner (0x2F) 8×8 at the four corners, flipped, and messedge (0x32) 8 wide on the four sides, "
                  + $"turned -- outer edge (166, 188)–(474, 412) in HUD units, ({outer.Position.X:0.#}, {outer.Position.Y:0.#})–({outer.End.X:0.#}, {outer.End.Y:0.#}) px");
            int lastFill = blits.FindLastIndex(b => b.Piece == "fill");
            Check(lastFill >= 0 && blits.Skip(lastFill + 1).All(b => b.Piece != "fill") && blits.FindIndex(b => b.Piece != "fill") == lastFill + 1
                  && blits.FindLastIndex(b => b.Piece == "shadow") < blits.FindIndex(b => b.Piece == "text"),
                  "DEPTH: the opaque fill first, behind everything sorted; the text's shadow (z 18) behind the text (z 10)");
            var smallBff = lib.ReadGeneric("/Fonts/European/Small.bff");
            var small = smallBff != null ? new BitmapFont(smallBff) : null;
            int SmallWidth(string line) { int n = 0; foreach (char c in line) if (small.TryGetGlyph(c, out var gl)) n += gl.Advance; return n; }
            var lines = BreakLines(words).Select((l, i) => (Line: l, Row: i)).Where(x => x.Line.Length > 0).ToList();
            var texts = blits.Where(b => b.Piece == "text").ToList();
            var grey = new Color(48 / 255f, 48 / 255f, 48 / 255f);
            Check(small != null && small.LineAdvance == 21 && lines.Count > 0 && texts.Count == lines.Count
                  && lines.Zip(texts).All(z => Math.Abs(z.Second.Rect.Size.X - SmallWidth(z.First.Line) * kH) < 0.01f
                                            && Math.Abs(z.Second.Rect.Position.Y - (210 + 21 * z.First.Row) * kH) < 0.01f
                                            && Math.Abs(z.Second.Rect.GetCenter().X - cx) <= 0.5f * kH + 0.01f
                                            && z.Second.Modulate == grey),
                  $"its TEXT is Small.bff (font id 0: advance {small?.LineAdvance}, every line as wide as Small's advances make it) in (48,48,48), "
                  + $"each of its {lines.Count} lines CENTRED on x 320 ({cx:0.#} px), the first line's top at y 210, 21 apart, no wrap");
            var shadows = blits.Where(b => b.Piece == "shadow").ToList();
            Check(shadows.Count == texts.Count && shadows.Zip(texts).All(z => z.First.Rect.Position.DistanceTo(z.Second.Rect.Position + new Vector2(2, 2) * kH) < 0.01f
                                                                          && ReferenceEquals(z.First.Texture, z.Second.Texture) && z.First.Modulate == Colors.Black),
                  "⚠ INFERRED: each line carries a black 2,2 drop shadow (the render pass's default, 0x1C55C8 → 0x20B258(ctx, 2, 2))");
            var frameImage = GetViewport().GetTexture().GetImage();
            var probe = new Vector2I((int)cx, (int)(390 * kH));
            var onScreen = frameImage.GetPixelv(probe);
            Check(onScreen.G > 0.8f && onScreen.B > 0.8f && onScreen.R > 0.55f && onScreen.R < 0.9f,
                  $"on screen, the fill's bottom margin (320, 390) reads ({onScreen.R8},{onScreen.G8},{onScreen.B8}) at {probe}: the pale ruled sheet, not a dark box");
            await Shot("stack");
            int held = adv.Stack.Count;
            Key(Godot.Key.Delete);
            for (int i = 0; i < 30 && adv.Stack.Count == held; i++) Tick();
            await Present();
            Check(adv.Stack.Count == held - 1 && adv.Stack.Records.All(x => x.Row != (short)litterMsg.TextRow)
                  && stackSounds.Contains(AdvisorMessageStack.SoundOpenClose) && stackView.ShownCount == held - 1,
                  $"Delete (Circle) slides the record out and removes it, sound 0x1F; the envelope draws {stackView.ShownCount}");
            var game = Field<GameCamera>(viewer, "_game");
            Call(viewer, "StartGameCam");
            var home = (game.CursorX, game.CursorZ);
            if (!adv.Stack.IsOpen) { Key(Viewer.AdvisorL2Key); Tick(); }
            for (int i = 0; i < adv.Stack.Count && !ReferenceEquals(adv.Stack.Selected?.Object, ride); i++) { Key(Godot.Key.Down); Tick(); }
            Check(adv.Stack.IsOpen && ReferenceEquals(adv.Stack.Selected?.Object, ride), "the cursor is back on the ride's record");
            Key(Godot.Key.Enter);
            Tick();
            var centre = park.CellCentre(ride.Origin.X + ride.Width / 2, ride.Origin.Z + ride.Height / 2);
            Check(!adv.Stack.IsOpen && (game.CursorX, game.CursorZ) != home
                  && game.CursorX == (int)(centre.X * GameCamera.TileUnits) && game.CursorZ == (int)(centre.Z * GameCamera.TileUnits),
                  $"Cross (Enter) on it JUMPS the camera to the ride ({home} → ({game.CursorX},{game.CursorZ}), its footprint's centre) and closes the stack (0x108FF8)");
            // Delete the ride through the viewer's own delete: ParkSim.Remove → 0x14A7B0.
            for (int i = 0; i < 1500 && !(adv.State == AdvisorState.Idle && adv.RingCount == 0 && !adv.PendingSet); i++) Tick();
            int index = park.Placed.Select((p, i) => (p.Id, i)).Where(x => x.Id == ride.Id).Select(x => x.i).DefaultIfEmpty(-1).First();
            int held2 = adv.Stack.Count, others = adv.Stack.Records.Count(x => x.Type == AdvisorRecordType.Object && !ReferenceEquals(x.Object, ride));
            Check(index >= 0 && (bool)Call(viewer, "DeletePlaced", index, true), $"the viewer deletes {ride.Name} (DeletePlaced)");
            Tick();
            Check(!sim.Rides.Contains(ride) && others == 0 && adv.Stack.Records.All(x => !ReferenceEquals(x.Object, ride)),
                  $"its records leave the stack with it ({held2} → {adv.Stack.Count}): 0x1E12E0 → 0x1088E0, then 0x13D8C0");
            // The camera back home first: it is still on the ride from the jump, where a wrong second jump would change nothing.
            Call(viewer, "StartGameCam");
            var camNow = (game.CursorX, game.CursorZ);
            var rideCell = ((int)(centre.X * GameCamera.TileUnits), (int)(centre.Z * GameCamera.TileUnits));
            adv.Stack.FocusObject(ride);
            Check(camNow != rideCell && (game.CursorX, game.CursorZ) == camNow,
                  $"a jump to a ride that has left the park is refused: the camera stays at {camNow}, not {rideCell}");

            // ---------------------------------------------------------------------------------
            // 5. Pause, an interruption, a modal message. Rule 47 speaks again (its delay is 60 days; the same counter).
            for (int i = 0; i < 1500 && !(adv.State == AdvisorState.Idle && adv.RingCount == 0 && !adv.PendingSet); i++) Tick();
            async Task<bool> RuleAgain()
            {
                for (int d = 0; d < 61; d++) Call(viewer, "AdvanceCalendar", ParkClock.UnitsPerDay);
                adv.CountEvent(0x15, 1);
                for (int i = 0; i < 3000 && !(adv.State == AdvisorState.Speaking && adv.SpeechElapsedMs > 400); i++) Tick();
                await Present(1);
                return adv.State == AdvisorState.Speaking && adv.SpeechMessage == 0x64 && voice.Playing;
            }
            Check(await RuleAgain(), "rule 47 speaks again after its 60-day delay");
            Set(viewer, "_playing", false);
            Call(viewer, "PresentAdvisor");
            bool paused = voice.StreamPaused;
            Set(viewer, "_playing", true);
            Call(viewer, "PresentAdvisor");
            Check(paused && !voice.StreamPaused && voice.Playing,
                  "⚠ pausing the park pauses the voice with it, and unpausing resumes it (the console's is not established)");
            // ⚠ 268 WELCOME_MAIN submitted directly -- the tutorial dispatcher that sends it is out of scope. 208..274 is
            // always IMMEDIATE, so in state 3 it interrupts (0x107640): the speech is stopped and the head cut out.
            int litterPlays = played.Count(p => p.Id == 0x64);
            var panel = Field<LaptopShopScreen>(viewer, "_shopPanel");
            adv.Submit(0x10C);
            Check(adv.State == AdvisorState.Exiting && !adv.Speaking && !voice.Playing && adv.PendingSet && adv.PendingId == 0x10C
                  && head.Channel.Record == ParkAdvisor.RecordExit && head.Channel.ElapsedMs == 0 && head.Channel.Queued == AdvisorHeadChannel.None,
                  "an immediate message INTERRUPTS the speech (§2.1): the voice stopped (\"SHUTTING UP ADVISOR\"), the exit cut short (flags 2), 268 pending");
            var m = new List<Pass>();
            for (int i = 0; i < 400 && !(adv.State == AdvisorState.Speaking && m.Count(p => p.State == AdvisorState.Speaking) >= 40); i++)
            { Tick(); m.Add(Snap()); }
            Check(adv.Modal && adv.PadLocked && adv.State == AdvisorState.Speaking && !adv.LipTrackAttached
                  && played.Count(p => p.Id == 0x64) == litterPlays,
                  "268 plays next and the interrupted message is LOST (not replayed); 268 is MODAL (208..274): the pad is locked, and it has no lip track (PS2_)");
            viewer._UnhandledKeyInput(new InputEventKey { Keycode = Godot.Key.Tab, Pressed = true });
            Check(panel == null || !panel.Open, "a locked pad refuses the game's keys: Tab does not open the laptop");
            var flaps = m.Where(p => p.State == AdvisorState.Speaking && p.MouthsDrawn.Length == 1).Select(p => p.MouthsDrawn[0]).ToList();
            int changes = flaps.Zip(flaps.Skip(1), (a, b) => a != b).Count(c => c);
            // One pass in five draws a new shape, always a different one: expect ~8 changes in 40, need only that it moves.
            Check(flaps.Distinct().Count() >= 2 && changes >= 2,
                  $"with no lip track the drawn mouth FLAPS at random ({changes} changes over {flaps.Count} passes, {flaps.Distinct().Count()} meshes)");
            await Shot("modal");
            Input.ParseInputEvent(new InputEventKey { Keycode = AdvisorTriangle, Pressed = true });
            await Frames(1);
            Tick();
            bool latched = adv.SkipLatched && uiSounds.Contains(ParkAdvisor.SoundSkip) && adv.State == AdvisorState.Speaking;
            Input.ParseInputEvent(new InputEventKey { Keycode = AdvisorTriangle, Pressed = false });
            await Frames(1);
            Tick();
            Check(latched && adv.State == AdvisorState.Exiting && !adv.Speaking && !voice.Playing
                  && head.Channel.Record == ParkAdvisor.RecordExit && head.Channel.ElapsedMs <= 40 && head.Channel.Queued == AdvisorHeadChannel.None,
                  $"Triangle ({AdvisorTriangle}) held then released SKIPS it: sound 299, the voice stopped, the exit CUT short -- record 14 at once, nothing queued");
            for (int i = 0; i < 400 && adv.PadLocked; i++) Tick();
            viewer._UnhandledKeyInput(new InputEventKey { Keycode = Godot.Key.Tab, Pressed = true });
            Check(!adv.PadLocked && panel != null && panel.Open, "the cooldown unlocks the pad: Tab opens the laptop again");
            viewer._UnhandledKeyInput(new InputEventKey { Keycode = Godot.Key.Tab, Pressed = true });

            // ---------------------------------------------------------------------------------
            // 6. Options, leaving, teardown.
            byte flags = adv.Flags;
            Call(viewer, "GameOptionChose", LaptopScreen.OptTutorial);
            bool flipped = (adv.Flags ^ flags) == ParkAdvisor.FlagTutorial && settings.Tutorial == ((adv.Flags & 0x40) != 0);
            Call(viewer, "GameOptionChose", LaptopScreen.OptTutorial);
            Check(flipped && adv.Flags == flags, "Game Options' Tutorial On/Off flips the advisor's flag 0x40 and nothing else, and back");
            if (panel != null && panel.Open) Call(viewer, "ToggleLaptop");
            for (int i = 0; i < 1500 && !(adv.State == AdvisorState.Idle && adv.RingCount == 0 && !adv.PendingSet); i++) Tick();
            Check(await RuleAgain(), "rule 47 speaks a third time, 60 days on");
            var laptopBack = Field<List<(string Kind, string Arg)>>(viewer, "_laptopBack");
            laptopBack.Clear();
            Call(viewer, "ShowLaptopLevel");
            var mainOpts = LaptopMainMenu.VisibleMain(parkOpen: Field<bool>(viewer, "_laptopParkOpen")).ToList();
            int closeRow = mainOpts.FindIndex(o => o.Index == LaptopMainMenu.CloseParkIndex);
            Check(closeRow >= 0, $"the laptop's main menu has Close Park at row {closeRow}");
            Call(viewer, "OnLaptopRow", closeRow);
            Check(adv.State == AdvisorState.Exiting && !adv.Speaking && !voice.Playing && head.Channel.Record == ParkAdvisor.RecordExit,
                  "Close Park takes the advisor off the screen (0x1510F8 → 0x1072C8): the voice stopped, the exit cut short");
            await Present();
            Check(!head.Overlay.Visible && !stackView.EnvelopeShown, "in the lobby neither the head nor the envelope is drawn");
            var overlay = head.Overlay;
            Call(viewer, "ResetGuests");
            await Frames(2);
            Check(Field<ParkAdvisor>(viewer, "_parkAdvisor") == null && Field<AdvisorHead>(viewer, "_advisorHead") == null
                  && !GodotObject.IsInstanceValid(overlay) && !voice.Playing && mix.SfxGain == 1f,
                  "the park's teardown frees the head (0x106488), stops the voice and puts the mix back");

            Field<RideSounds>(viewer, "_sounds")?.Clear();
            viewer.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            GD.Print($"ADVISOR SMOKE PASS checks={_checks}; world={world}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ADVISOR SMOKE FAILED world={world} checks={_checks}: {ex}");
            GetTree().Quit(2);
        }
    }

    static Key AdvisorTriangle => Viewer.AdvisorTriangleKey;
}
