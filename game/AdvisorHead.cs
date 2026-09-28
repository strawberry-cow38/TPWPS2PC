using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

/// <summary>
/// ⭐⭐ THE ADVISOR'S HEAD, drawn (findings/advisor-messages.md §4.3..§4.7): the registry's model 488
/// (`Data\Generic\Advisor\advisor.mps` + `.aps`, 32 meshes, section 5's 15 records), on channel 0 as the
/// console drives it (<see cref="AdvisorHeadChannel"/>: enter 13, talk 0..6 looping, exit 14 queued behind the
/// talk loop or cut short), dressed by the playing variant's costume byte (`0x107160`) and lip-synced by the
/// core's mouth (`0x105FC8`) -- both written into the model's own node flags, as the console writes them.
///
/// READ, and used as read:
/// - the transform, every tick of `0x1066B0`: position (0.6, −0.5, 0) (`0x2AA714`, `0x2AA718`) and scale
///   (s, 4s/3, s), s = 0.013 (`0x2AA71C`), on render layer 4;
/// - the model's own axes (measured off the bind pose): world y is up (the hats at +y, the body at −y), x
///   across, and the face looks down −z -- the side the constructor's look-at camera stands on (eye
///   (0, 0, −256), `adv+0x118`, `0x115A40`).
///
/// ⚠ ADAPTERS, INFERRED because the projection is NOT traced (research "Still unknown"):
/// - A SCREEN-SPACE OVERLAY that does not move with the park camera: its own world and orthographic camera,
///   drawn under the 2D HUD. (0.6, −0.5) is taken in the frame the HUD glyph blit `0x2137C0` uses -- x_ndc =
///   x/256 − 1, y_ndc = 1 − y/256 over the 512-square console space -- so the head sits at console (410, 384):
///   LOWER RIGHT, as the research infers.
/// - The scale REPLACES the model's root node's bind scale (`Position Dummy`, 0.02) rather than multiplying it:
///   multiplied, the head would be 0.3% of the screen wide; replacing it gives the head a radius of 0.14 of the
///   half-width, and the enter animation's 52-unit rise (`Body` z −73.2 → −21.2) then starts the whole bug
///   exactly below the frame's bottom edge -- which is what "rises from below the frame" (§4.7) needs.
/// - 4/3 is on the VERTICAL (world y): the aspect correction of a 4:3 frame. Beyond 4:3 both axes take the
///   vertical factor -- the HUD's own rule for its glyphs (one factor, from the height) -- so the head is never
///   stretched on a wide window.
/// - HIDDEN WHEN IDLE: drawn from the first request on the channel until the exit record is end-held. The
///   console registers the model once and never hides it (`0x105E00` has no callers); after an exit it is below
///   the frame, but its bind pose -- before the first message -- stands up wearing every prop at once, which
///   nothing the player sees suggests. The research calls that bind-pose visibility INFERRED.
/// - Lighting: the model shader's own (the park's light terms), as every other model in the port; what light
///   render layer 4 uses is not traced.
/// - Frames between passes are interpolated at the park clock's alpha, as the staff's are.
/// </summary>
public sealed class AdvisorHead : IAdvisorHead
{
    /// <summary>`0x106080`: `0x17BB48(0x1E8)` and `vt+0xC(0x1E8, 0, −1)` -- registry entry 287, `Advisor`,
    /// world 4, category 13.</summary>
    public const int RegistryId = 0x1E8;
    /// <summary>`0x2AA714`, `0x2AA718` and the literal 0 `0x1066B0` stores at `+0x48`.</summary>
    public const float AnchorX = 0.6f, AnchorY = -0.5f, AnchorZ = 0f;
    /// <summary>`0x2AA71C`.</summary>
    public const float Scale = 0.013f;
    /// <summary>`0x105DB8 → 0x17C9D8`, scene `vt+0x24(…, 4, −1)`: render layer 4 (logged; the overlay is its own world).</summary>
    public const int RenderLayer = 4;
    /// <summary>The APS section every advisor request names (`0x1abc80(…, 5, record, …)`).</summary>
    public const int Section = 5;
    /// <summary>`0x1F1F78(model, 0x400, id)`: the space the costume's fittings answer to.</summary>
    public const uint FittingSpace = 0x400;
    /// <summary>`0x106D88`'s names (`0x36A508..0x36A548`), matched without case (`0x29D808`), shapes 0..4.</summary>
    public static readonly string[] MouthNames = { "mouth - normal", "mouth - aah", "mouth - eee", "mouth - ooh", "mouth - sss" };

    readonly Model _model;
    readonly Aps.Record[] _records;
    readonly int[] _mouths = new int[5];
    readonly SubViewport _view;
    readonly Camera3D _camera;
    readonly Node3D _pivot;
    readonly float _rootScale;
    int _bound = AdvisorHeadChannel.None;

    public AnimatedModel Drawn { get; }
    public AdvisorHeadChannel Channel { get; }
    /// <summary>The full-screen layer the head is drawn into: add it under the HUD.</summary>
    public TextureRect Overlay { get; }
    /// <summary>The overlay's own viewport, for a check that reads its pixels.</summary>
    public SubViewport View => _view;
    /// <summary>The last costume selector dressed, −1 before any.</summary>
    public int Dressed { get; private set; } = -1;
    /// <summary>The mouth shape last written, −1 before any (the model's own flags show all five until then).</summary>
    public int MouthShown { get; private set; } = -1;
    public string Summary { get; }

    /// <param name="model">⚠ The head's OWN parse of `advisor.mps`: its node flags are written at run time.</param>
    public AdvisorHead(Model model, Aps aps, Func<string, (ImageTexture Tex, bool Soft)> texture)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        if (aps == null) throw new ArgumentNullException(nameof(aps));
        _records = aps.Records().Where(r => r.Slot == Section).ToArray();
        if (_records.Length <= ParkAdvisor.RecordExit)
            throw new InvalidDataException($"advisor.aps: section {Section} has {_records.Length} records, the advisor asks for 0..{ParkAdvisor.RecordExit}");
        Channel = new AdvisorHeadChannel(r => r >= 0 && r < _records.Length ? _records[r].DurationFrames : 0);
        for (int i = 0; i < 5; i++)
            _mouths[i] = _model.Meshes.ToList().FindIndex(m => string.Equals(m.Name, MouthNames[i], StringComparison.OrdinalIgnoreCase));
        if (_mouths.Any(i => i < 0)) GD.PrintErr($"[advisor] head: mouth meshes not all found ({string.Join(",", _mouths)})");
        // The root helper whose bind scale the per-tick transform stands in for (see the class note).
        int root = Enumerable.Range(_model.Meshes.Count, _model.HelperCount).FirstOrDefault(n => _model.NodeParent(n) < 0, -1);
        _rootScale = 1f;
        if (root >= 0 && _model.LocalTransforms().TryGetValue(_model.NodeOffset(root), out var bind))
            _rootScale = MathF.Sqrt(bind.M11 * bind.M11 + bind.M12 * bind.M12 + bind.M13 * bind.M13);
        // Built on the enter record, so the node visibility is the ordinary APS kind from the start.
        Drawn = new AnimatedModel(_model, aps, _records[ParkAdvisor.RecordEnter], texture);
        Drawn.SetFrame(0);

        _view = new SubViewport
        {
            Name = "AdvisorHeadView", TransparentBg = true, OwnWorld3D = true, Size = new Vector2I(640, 512),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, GuiDisableInput = true,
        };
        _camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal, KeepAspect = Camera3D.KeepAspectEnum.Height,
            Size = 2f, Near = 0.05f, Far = 50f, Position = new Vector3(0, 0, 10), Current = true,
        };
        _view.AddChild(_camera);
        _pivot = new Node3D { Name = "Advisor" };
        _view.AddChild(_pivot);
        _pivot.AddChild(Drawn.Root);
        Overlay = new TextureRect
        {
            Name = "AdvisorHead", Texture = _view.GetTexture(), MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            Visible = false,
        };
        Overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Overlay.AddChild(_view);
        Summary = $"{_model.Meshes.Count} meshes, section {Section} {_records.Length} records "
                + $"(enter {_records[ParkAdvisor.RecordEnter].DurationFrames} f, exit {_records[ParkAdvisor.RecordExit].DurationFrames} f), "
                + $"mouths {string.Join(",", _mouths)}, root scale {_rootScale:0.####}, {_model.Fittings.Count} fittings; {Drawn.Summary}";
    }

    // ---------------------------------------------------------------------------------------------
    // What the state machine asks (IAdvisorHead).

    public void Play(int record, int flags) { Channel.Play(record, flags); Bind(); }
    public bool ChannelDone => Channel.Done;
    public void Step(int milliseconds) { Channel.Step(milliseconds); Bind(); }

    /// <summary>The record on channel 0 bound to the drawn model (its channels and its visibility lists).</summary>
    void Bind()
    {
        if (Channel.Record == _bound || Channel.Record == AdvisorHeadChannel.None) return;
        _bound = Channel.Record;
        Drawn.UseRecord(_records[_bound]);
    }

    /// <summary>⭐ `0x107160`: `0x106FA8` puts every prop away and the hands and plain antennae back, then
    /// `0x1070B8` per the selector (MIPS/decompile `0x106FA8..0x107290`):
    /// <code>
    ///   0x106FA8  hide 14 12; show 21; hide 1 2 3 16 17 18 4..11 13; show 19 20
    ///   selector  5→1 Fantasy Helm  6→3 Axe  7→7 Handyman Hat  8→5 Hardhat  9→8 Kiosk Hat + 14 Spatula
    ///             10→4 Pith Helmet  11→10 Research Hat  12→2 Space Helmet  13→6 Security Hat  14→9 Ticket Hat
    ///   0x1070B8(id)  3: show 16 17, then as 1 · 1 2 4 5 6 7 9 10 11 13: hide 19 20 · 14: hide 21 · then show id
    /// </code>
    /// A fitting id is looked up in space 0x400 (`0x1F1F78`); show clears `0x10` and sets `0x80000000`
    /// (`0x106E30`), hide sets `0x80000010` (`0x106EF0`) -- the protection bit keeps it through the next record.</summary>
    public void Dress(int selector)
    {
        Dressed = selector;
        Hide(14); Hide(12); Show(21);
        foreach (int f in new[] { 1, 2, 3, 16, 17, 18, 4, 5, 6, 7, 8, 9, 10, 11, 13 }) Hide(f);
        Show(19); Show(20);
        switch (selector)
        {
            case 5: Put(1); break;
            case 6: Put(3); break;
            case 7: Put(7); break;
            case 8: Put(5); break;
            case 9: Put(8); Put(14); break;
            case 10: Put(4); break;
            case 11: Put(10); break;
            case 12: Put(2); break;
            case 13: Put(6); break;
            case 14: Put(9); break;
        }
    }

    /// <summary>`0x1070B8`.</summary>
    void Put(int id)
    {
        switch (id)
        {
            case 3: Show(16); Show(17); Hide(19); Hide(20); break;
            case 1: case 2: case 4: case 5: case 6: case 7: case 9: case 10: case 11: case 13: Hide(19); Hide(20); break;
            case 14: Hide(21); break;
        }
        if (id != 0) Show(id);
    }

    void Show(int fitting) => Fitting(fitting, 0x80000000u, 0x10u);       // 0x106E30
    void Hide(int fitting) => Fitting(fitting, 0x80000010u, 0u);          // 0x106EF0

    void Fitting(int id, uint set, uint clear)
    {
        if (_model.FindFitting(id, FittingSpace) is not { } f) return;   // 0x1F1F78 == −1: nothing
        Drawn.WriteNodeFlags(f.Node, set, clear);
    }

    /// <summary>The mesh a costume fitting id names, or −1.</summary>
    public int FittingMesh(int id) => _model.FindFitting(id, FittingSpace) is { } f ? f.Node : -1;

    /// <summary>⭐ `0x105FC8`: the chosen mouth mesh loses bit `0x8000`, the other four gain it.</summary>
    public void Mouth(int shape)
    {
        MouthShown = shape;
        for (int i = 0; i < 5; i++)
            if (_mouths[i] >= 0) Drawn.WriteNodeFlags(_mouths[i], i == shape ? 0u : 0x8000u, i == shape ? 0x8000u : 0u);
    }

    /// <summary>The mouth meshes drawn now (their indices) -- the OUTPUT a check reads.</summary>
    public IEnumerable<int> MouthsDrawn => _mouths.Where(i => i >= 0 && Drawn.MeshDrawn(i));
    public int MouthMesh(int shape) => shape is >= 0 and < 5 ? _mouths[shape] : -1;

    // ---------------------------------------------------------------------------------------------
    // The frame.

    /// <summary>Up: a record is on the channel and it is not the exit, held at its end.</summary>
    public bool Up => Channel.Record != AdvisorHeadChannel.None
                   && !(Channel.Record == ParkAdvisor.RecordExit && Channel.EndHeld);

    /// <summary>Draw the head for this rendered frame: <paramref name="alpha"/> of a pass of
    /// <paramref name="passMs"/> since the last, into a view of <paramref name="size"/> pixels;
    /// <paramref name="allowed"/> false hides it (the park is not the scene on screen).</summary>
    public void Present(float alpha, int passMs, Vector2 size, bool allowed)
    {
        bool show = allowed && Up && size.X >= 1 && size.Y >= 1;
        Overlay.Visible = show;
        _view.RenderTargetUpdateMode = show ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (!show) return;
        var px = new Vector2I(Math.Max(1, (int)size.X), Math.Max(1, (int)size.Y));
        if (_view.Size != px) _view.Size = px;
        float aspect = size.X / size.Y;
        // ⚠ The console's (0.6, −0.5) in its NDC, with the vertical factor on both axes (see the class note).
        float k = Scale * 4f / 3f / _rootScale;
        _pivot.Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(k, k, k)),
                                           new Vector3(AnchorX * aspect, AnchorY, AnchorZ));
        Bind();
        int length = Channel.Length(Channel.Record);
        float frame = Channel.EndHeld ? length
                    : Math.Min(length, (Channel.ElapsedMs + Math.Clamp(alpha, 0f, 1f) * passMs) * AdvisorHeadChannel.FramesPerSecond / 1000f);
        Drawn.SetFrame(frame);
    }

    /// <summary>Free the overlay, the viewport and the model's nodes (park teardown).</summary>
    public void Free()
    {
        if (GodotObject.IsInstanceValid(Overlay)) Overlay.QueueFree();
    }
}
