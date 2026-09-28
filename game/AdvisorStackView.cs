using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>
/// ⭐⭐ THE MESSAGE STACK, DRAWN (findings/advisor-messages.md §5.3; READ `0x108348`, `0x108D40`, `0x108E98`,
/// `0x108AB0`, `0x108458`): the envelope and its count at the bottom left, the records sliding in from the
/// left edge, and the selected record's text in the 260-wide box. The stack itself -- records, slides, cursor,
/// scroll, delete, select -- is the core's (<see cref="AdvisorMessageStack"/>); this only draws what it holds,
/// and only when no laptop screen is up (`0x108568`: `*HUD == 0`).
///
/// <code>
///   0x108D40(…, 0x20, 0x1D6, count)  "%d" at (0x50, 0x1D6 − [0x2AA738]=40) = (80, 430), font 1, white, shadow 2,2
///                                    sprite 0x2A at (0x20, 430 − 10) = (32, 420), 40×40
///   0x108348  smoothed scroll += (top·70 − it) &gt;&gt; 2; record i at (32, 250 − 70·i + scroll)
///   0x108E98  x += slide, y += slideY; drawn when x &gt; 0 and −70 &lt; y &lt; 581; y &gt; 258: x −= 2(y − 250);
///             y &lt; 70: x −= 2(70 − y); sprite 0x2C tutorial / 0x2B selected / 0x2A, 40×40, layer 0
///   0x108AB0  (x &gt; −40, y &gt; −40) layer 4: 0x30 at (0, y) (x+40)×40; 0x2F at (x+40, y−8) and flipped at
///             (x+40, y+40), 8×8; 0x32 at (0, y−8) and flipped at (0, y+40), (x+40)×8, turned at (x+40, y) 8×40
///   0x108458  open: widget at x (0x20A120() − 260)/2 = (640 − 260)/2 = 190, y 210, 260×180, margins 16×14,
///             text colour (48,48,48), justify 1 (centre), string translate(row); then 0x1DD1E8(widget, 0, 0, 10)
///   0x1DD1E8  0x1DD0E0 (the text), then the rect widened OUTWARD by the margins -- (174, 196) 292×208 -- and
///             0x141F68 → kind 1 → 0x142090 (the nine-slice), then the rect restored
///   0x142090  fill sprite 0x31 (+0x48 = 0) in h&gt;&gt;4 = 13 rows of 292×16, the whole 16×16 tile stretched across each
///             row; corners 0x2F 8×8 at (x+w, y−8) raw, (x+w, y+h) flip y, (x−8, y−8) flip x, (x−8, y+h) flip xy;
///             edges 0x32: top (x, y−8) w×8, bottom (x, y+h) w×8 flip y, right (x+w, y) 8×h turn 2, left (x−8, y)
///             8×h turn 0 -- outer edge (166, 188)–(474, 412)
///   0x1DD0E0  font id 0 = Small.bff (0x20BA28), colour word 0x80303030, 0x20ACF8(x + w/2 = 320, y = 210): each
///             line centred on x 320 (0x1D2FD0 mode 1), the first line's top at 210, the font's advance (21), no
///             wrap; lines break on \n, \r, \r\n, \n\r
/// </code>
///
/// ⭐ THE SPRITES ARE THE REGISTRY'S. `0x216028` builds the UI sprite table at `0x2EEFF0` (name `+0`, id
/// `+0x10`, stride 0x18; read here by evaluating its stores): 0x2A `messages\letterclosed.ssh`, 0x2B
/// `letteropen`, 0x2C `tutorial`, 0x2F `messcorner`, 0x30 `messfill`, 0x31 `wboxfill`, 0x32 `messedge` -- the
/// record frames and the read box draw the same corner and edge art (<see cref="UiPanel"/>). The research named
/// the table `0x2EEFF4`.
///
/// ⭐ THE READ BOX IS READ (ghidra_tpw/notes/advisor-V-visuals.md §1, findings/advisor-visuals.md): the widget's
/// class draws through vtable `0x3697A8` slot `+0x10` = `0x1DD1E8`, kind 1 = `0x142090`, the painter the graph
/// panel uses (findings/graph-widget.md §1.3) with the other fill. Its face is `wboxfill` (0x31, the pale ruled
/// sheet, type 0x84: opaque), its frame the stack's own blue `messcorner`/`messedge`. `wboxcorner`/`wboxedge`
/// are on the disc and never named by the executable. (48,48,48) is the TEXT colour, not the fill.
/// DEPTH (§1.5, smaller z nearer): the records in front (letter z 0, frame z 4); the text and the frame at z 10;
/// the fill on the opaque chain, behind everything sorted -- so here: fill, shadow, frame, text, then records.
///
/// ⚠ ADAPTERS, each said once:
/// - Frame: the HUD's (<see cref="Viewer"/>'s money readout): the console's 512-square space, a POSITION as a
///   fraction of the window (u/512·W, v/512·H), a SIZE -- or an offset from a placed point -- by one factor from
///   the height, k = H/512, so nothing stretches on a wide window. A record hangs off its x 32; the read box hangs
///   off its centre line, x 320 = 190 + 260/2, where the console centres every line of its text (62.5% across,
///   right of the window's centre, as the console's 640-pixel width in 512-unit space puts it). The console
///   stretches its 512-unit space over the frame (a unit is 1.25 px across on PAL 640×512); the port does not.
/// - Layers: a record's frame (layer 4) is drawn BEHIND its letter (layer 0) -- the fill spans the letter's
///   own square, so the other order would hide it (INFERRED: a lower layer is nearer).
/// - The box text's SHADOW is INFERRED (§1.2): black at (+2, +2), z+8, from the render pass's default
///   (`0x1C55C8` → `0x20B258(ctx, 2, 2)`); `0x1DD0E0` neither sets nor clears it, and whether an earlier draw in
///   the frame leaves it off was not traced. Its colour is the strip's black sprite colour with the glyph's
///   coverage as alpha. The text's (48,48,48) opaque is itself INFERRED from the colour word (how the glyph
///   rasteriser applies `obj+0x4C` was not read).
/// - A line's width is the sum of its advances (<see cref="FontText.Measure"/>); `0x1D2FD0`'s `(e0 − f0)` was
///   read only as "the line's width". The text is drawn from the text database at run time.
/// - The side edges: `0x2136D0(2)` sets packet flags 0x1010 and `(0)` sets 0x1020 (MIPS `0x2136D0..0x213758`);
///   `0x2329C8` swaps U on 0x10, V on 0x20 and exchanges corners 1 and 2's UVs on 0x1000 (`0x232B84..0x232C40`):
///   two opposite quarter turns. Which is which rests on the corner art's cut facing outward when drawn raw at
///   the top right (<see cref="UiPanel"/>'s measurement): then 2 is a clockwise turn, the record frames' side
///   edge, and 0 the counter-clockwise one -- each edge's band outward. INFERRED; the sprite's v0/v1 order is
///   not read.
/// - Where the box and the head overlap (frame x 333–474, y 312–412 units), the box is drawn over the head: the
///   head is 3D content under the whole HUD. The console's order there is NOT resolved (both go into list
///   `0x311160`; the head's sort flag was not read).
/// - The smoothed scroll steps once a simulation pass (<see cref="Pass"/>), as the stack's own update does;
///   the console steps it in its draw.
/// - The HUD's button bar (Close / Delete / Select / Replay, `0x13E340`) is not drawn anywhere in the port; the
///   view says the keys in its status line instead.
/// </summary>
public partial class AdvisorStackView : Control
{
    public const float Native = 512f;
    const int EnvelopeX = 0x20, EnvelopeBase = 0x1D6, CountLift = 40, EnvelopeDrop = 10, CountX = 0x50;
    const int Sprite = 0x28, Edge = 8;
    const int RecordX = 0x20, RecordY = 0xFA, RecordPitch = 0x46;
    const int ShownAbove = -0x46, ShownBelow = 0x245, NearTop = 0x46, NearBottom = 0x102;
    const int TextShadow = 2;
    /// <summary>`0x20A120()`, the screen WIDTH `0x20A0E0(0x280, 0x200)` stores at boot: 640, in pixels, used by
    /// `0x108458` in the 512-unit space.</summary>
    public const int ScreenWidth = 0x280;
    /// <summary>`0x108458`'s widget rect (`+8`, `+0xA`, `+0x14`, `+0x16`) and margins (`+0x5C`, `+0x60`).</summary>
    public const int BoxWidth = 0x104, BoxHeight = 0xB4, BoxY = 0xD2, MarginX = 0x10, MarginY = 0x0E;
    public const int BoxX = (ScreenWidth - BoxWidth) >> 1;
    /// <summary>The text's x after `0x1DD0E0`'s `px += w/2` (flags &amp; 2): each line is centred on it.</summary>
    public const int BoxCentre = BoxX + BoxWidth / 2;
    /// <summary>`0x142090`: the fill's row height (`h &gt;&gt; 4` rows) and the frame's 8-unit pieces.</summary>
    const int FillRow = 16, Frame8 = 8;
    /// <summary>`0x108458`: `0x1DD2E0(widget, 0x30, 0x30, 0x30)` -- the TEXT's colour.</summary>
    public static readonly Color TextColour = new(0x30 / 255f, 0x30 / 255f, 0x30 / 255f);
    /// <summary>⚠ INFERRED (see the class note): the strip's second pass in black, coverage as alpha.</summary>
    public static readonly Color BoxShadow = new(0f, 0f, 0f, 1f);
    /// <summary>⚠ Palette slot `colour + 8` is not read; the money readout's half-black, as it uses.</summary>
    static readonly Color ShadowTint = new(0f, 0f, 0f, 0.55f);

    AdvisorMessageStack _stack;
    Func<AdvisorStackRecord, string> _text;
    ImageTexture _closed, _open, _tutorial;
    UiPanel _panel;
    Func<FontText> _countFont;
    FontText _textFont;

    /// <summary>`+0x18`, the smoothed scroll in pixels of the 512 space.</summary>
    public int SmoothedScroll { get; private set; }
    /// <summary>Drawn at all: the park HUD is up and no laptop screen is (`*HUD == 0`).</summary>
    public bool Allowed { get; set; }
    /// <summary>What the last draw put on screen -- the OUTPUT a check reads.</summary>
    public string ShownText { get; private set; }
    public int ShownCount { get; private set; } = -1;
    public int ShownRecords { get; private set; }
    public bool EnvelopeShown { get; private set; }
    /// <summary>Where the envelope was drawn, in window pixels.</summary>
    public Rect2 EnvelopeRect { get; private set; }

    /// <summary>One blit of the read box, as handed to the renderer: which piece, its texture, its rect in window
    /// pixels and its colour. <see cref="BoxBlits"/> is the last draw's, in draw order -- the OUTPUT a check reads.</summary>
    public readonly record struct Blit(string Piece, Texture2D Texture, Rect2 Rect, Color Modulate);
    readonly List<Blit> _boxBlits = new();
    public IReadOnlyList<Blit> BoxBlits => _boxBlits;
    /// <summary>The panel art and the text face the box draws with, for a check that compares against the disc.</summary>
    public UiPanel Panel => _panel;
    public FontText TextFace => _textFont;
    /// <summary>Which art resolved, for the log.</summary>
    public string Report { get; private set; } = "not loaded";

    public AdvisorStackView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Name = "AdvisorStack";
    }

    /// <param name="text">The record's words: `translate(row)` (`0x1DFA58`) or its own text.</param>
    /// <param name="countFont">The HUD's `Large.bff` (font index 1, `0x20A958`), asked for at each draw: the HUD
    /// loads it with the park's first frame.</param>
    /// <param name="textFont">The box's `Small.bff`, font id 0 (`0x1DD0E0` → `0x20A958(ctx, 0)`; `0x20BA28` names
    /// record 0 "Small.bff").</param>
    public void Configure(AdvisorMessageStack stack, Func<AdvisorStackRecord, string> text, AssetLibrary lib,
                          Func<FontText> countFont, FontText textFont)
    {
        _stack = stack; _text = text; _countFont = countFont; _textFont = textFont;
        ImageTexture Sprite(string path)
        {
            try
            {
                var raw = lib?.ReadUi(path);
                if (raw == null) return null;
                var ssh = new Ssh(raw);
                return ImageTexture.CreateFromImage(Image.CreateFromData(ssh.Width, ssh.Height, false, Image.Format.Rgba8, ssh.Pixels));
            }
            catch (Exception e) { GD.PrintErr($"[advisor] stack sprite {path}: {e.Message}"); return null; }
        }
        _closed = Sprite("/messages/letterclosed.ssh");                   // 0x2A
        _open = Sprite("/messages/letteropen.ssh");                       // 0x2B
        _tutorial = Sprite("/messages/tutorial.ssh");                     // 0x2C
        _panel = UiPanel.Load(lib);                                       // 0x2F, 0x30, 0x32
        Report = $"letterclosed {(_closed != null ? $"{_closed.GetWidth()}x{_closed.GetHeight()}" : "MISSING")}, "
               + $"letteropen {(_open != null ? "ok" : "MISSING")}, tutorial {(_tutorial != null ? "ok" : "MISSING")}, "
               + $"messcorner/messfill/wboxfill/messedge {(_panel != null ? "ok" : "MISSING")}; count in Large.bff, text in Small.bff "
               + $"({(_textFont != null ? "loaded" : "MISSING")})";
    }

    /// <summary>`0x108348`'s scroll ease, once a pass.</summary>
    public void Pass()
    {
        if (_stack == null) return;
        SmoothedScroll += (_stack.ScrollTop * RecordPitch - SmoothedScroll) >> 2;
    }

    public void Present() => QueueRedraw();

    int _countValue = -1;
    string _countText;

    public override void _Draw()
    {
        EnvelopeShown = false; ShownRecords = 0; ShownText = null;
        if (_stack == null || !Allowed) { ShownCount = -1; return; }
        var view = GetViewportRect().Size;
        float sx = view.X / Native, k = view.Y / Native;
        // 0x108D40: the count, then the envelope.
        int textY = EnvelopeBase - CountLift;
        // ⚠ PERF: the count's string is rebuilt only when the count changes -- this runs every frame.
        if (_stack.Count != _countValue || _countText == null) { _countValue = _stack.Count; _countText = _countValue.ToString(); }
        string count = _countText;
        if (_countFont?.Invoke() is { } countFont)
        {
            var tex = countFont.Render(count);
            var at = new Vector2(CountX * sx, textY * k);
            var sz = tex.GetSize() * k;
            DrawTextureRect(tex, new Rect2(at + new Vector2(TextShadow * k, TextShadow * k), sz), false, ShadowTint);
            DrawTextureRect(tex, new Rect2(at, sz), false, Colors.White);
        }
        EnvelopeRect = new Rect2(new Vector2(EnvelopeX * sx, (textY - EnvelopeDrop) * k), new Vector2(Sprite, Sprite) * k);
        if (_closed != null) DrawTextureRect(_closed, EnvelopeRect, false);
        EnvelopeShown = true;
        ShownCount = int.TryParse(count, out int drawn) ? drawn : -1;          // what was drawn, not what is held
        // 0x108458: the box, when open -- drawn before the records, which are in front of it.
        _boxBlits.Clear();
        if (_stack.IsOpen && _stack.Selected is { } sel)
        {
            string words = _text?.Invoke(sel) ?? "";
            ShownText = words;
            ReadBox(words, sx, k);
        }
        // 0x108E98 per record, oldest at the bottom.
        var records = _stack.Records;
        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            int x = RecordX + r.SlideX, y = RecordY - RecordPitch * i + SmoothedScroll + r.SlideY;
            if (!(x > 0 && ShownAbove < y && y < ShownBelow)) continue;
            if (y > NearBottom) x += (y - RecordY) * -2;
            if (y < NearTop) x += (NearTop - y) * -2;
            var sprite = r.Type == AdvisorRecordType.Tutorial ? _tutorial : r.State == 2 ? _open : _closed;
            float left = RecordX * sx + (x - RecordX) * k, top = y * k;
            Frame(left, top, k);                                          // 0x108AB0, layer 4
            if (sprite != null) DrawTextureRect(sprite, new Rect2(new Vector2(left, top), new Vector2(Sprite, Sprite) * k), false);
            ShownRecords++;
        }
    }

    /// <summary>`0x1DD1E8` on the open stack's widget: the fill, the text's (inferred) shadow, the frame, the text.
    /// Every coordinate below is the console's, in its 512-unit space; <c>X</c>/<c>Y</c> are the port's HUD mapping
    /// (see the class note): the box's centre line x 320 is a POSITION, a fraction of the window, and every other
    /// x hangs off it by k.</summary>
    void ReadBox(string words, float sx, float k)
    {
        float X(int u) => BoxCentre * sx + (u - BoxCentre) * k;
        float Y(int v) => v * k;
        Rect2 R(int u, int v, int w, int h) => new(new Vector2(X(u), Y(v)), new Vector2(w * k, h * k));
        void Put(string piece, Texture2D tex, Rect2 rect, Color colour)
        {
            if (tex == null) return;
            DrawTextureRect(tex, rect, false, colour);
            _boxBlits.Add(new Blit(piece, tex, rect, colour));
        }
        // 0x1DD1E8: the rect widened OUTWARD by the margins before the panel is drawn.
        int x = BoxX - MarginX, y = BoxY - MarginY, w = BoxWidth + 2 * MarginX, h = BoxHeight + 2 * MarginY;
        // 0x142090: the fill, sprite 0x31 (`+0x48` is 0, never written), white -- h >> 4 rows of w×16, the whole tile
        // stretched across each row. 208 = 13·16 leaves no remainder row. Opaque (type 0x84): behind all the rest.
        if (_panel != null)
            for (int i = 0; i < h >> 4; i++) Put("fill", _panel.Sheet, R(x, y + FillRow * i, w, FillRow), Colors.White);
        // 0x1DD0E0's layout: one strip per line, centred on x 320, the first line's top at y 210, the font's advance.
        var lines = Lines(words);
        if (_textFont != null)
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].Length > 0)
                {
                    var tex = _textFont.Render(lines[i]);
                    int width = _textFont.Measure(lines[i]);
                    // ⚠ INFERRED: the shadow pass, black at (+2, +2) and z+8 -- behind the text and the frame.
                    Put("shadow", tex, R(BoxCentre - width / 2 + TextShadow, BoxY + _textFont.LineAdvance * i + TextShadow, width, tex.GetHeight()), BoxShadow);
                }
        if (_panel != null)
        {
            // Corners, sprite 0x2F, 8×8, white (0x213668 flips).
            Put("corner", _panel.CornerTopRight, R(x + w, y - Frame8, Frame8, Frame8), Colors.White);
            Put("corner", _panel.CornerBottomRight, R(x + w, y + h, Frame8, Frame8), Colors.White);
            Put("corner", _panel.CornerTopLeft, R(x - Frame8, y - Frame8, Frame8, Frame8), Colors.White);
            Put("corner", _panel.CornerBottomLeft, R(x - Frame8, y + h, Frame8, Frame8), Colors.White);
            // Edges, sprite 0x32: top and bottom w×8, the sides 8×h turned (0x2136D0(2) right, (0) left).
            Put("edge", _panel.EdgeTop, R(x, y - Frame8, w, Frame8), Colors.White);
            Put("edge", _panel.EdgeBottom, R(x, y + h, w, Frame8), Colors.White);
            Put("edge", _panel.EdgeRight, R(x + w, y, Frame8, h), Colors.White);
            Put("edge", _panel.EdgeLeft, R(x - Frame8, y, Frame8, h), Colors.White);
        }
        if (_textFont != null)
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].Length > 0)
                {
                    var tex = _textFont.Render(lines[i]);
                    int width = _textFont.Measure(lines[i]);
                    Put("text", tex, R(BoxCentre - width / 2, BoxY + _textFont.LineAdvance * i, width, tex.GetHeight()), TextColour);
                }
    }

    string _linesOf;
    readonly List<string> _lines = new();

    /// <summary>`0x20ACF8`'s line breaks -- `\n`, `\r`, `\r\n` or `\n\r`, one break each -- and no width wrap.
    /// ⚠ PERF: split only when the words change; this runs every frame the box is open.</summary>
    List<string> Lines(string words)
    {
        if (ReferenceEquals(words, _linesOf) || words == _linesOf) return _lines;
        _linesOf = words;
        _lines.Clear();
        int start = 0;
        for (int i = 0; i < words.Length; i++)
        {
            char c = words[i];
            if (c != '\n' && c != '\r') continue;
            _lines.Add(words[start..i]);
            if (i + 1 < words.Length && words[i + 1] != c && (words[i + 1] == '\n' || words[i + 1] == '\r')) i++;
            start = i + 1;
        }
        _lines.Add(words[start..]);
        return _lines;
    }

    /// <summary>`0x108AB0`: a bar out of the left edge to the letter's right side, edged top, bottom and right.
    /// <paramref name="left"/>/<paramref name="top"/> are the letter's corner in pixels.</summary>
    void Frame(float left, float top, float k)
    {
        if (_panel == null || left <= -Sprite * k || top <= -Sprite * k) return;
        float right = left + Sprite * k, e = Edge * k, h = Sprite * k;
        DrawTextureRect(_panel.Fill, new Rect2(0, top, right, h), false);
        DrawTextureRect(_panel.EdgeTop, new Rect2(0, top - e, right, e), false);
        DrawTextureRect(_panel.EdgeBottom, new Rect2(0, top + h, right, e), false);
        DrawTextureRect(_panel.EdgeRight, new Rect2(right, top, e, h), false);
        DrawTextureRect(_panel.CornerTopRight, new Rect2(right, top - e, e, e), false);
        DrawTextureRect(_panel.CornerBottomRight, new Rect2(right, top + h, e, e), false);
    }
}
