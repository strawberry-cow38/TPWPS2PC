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
///   0x108458  open: box x = (W − 260)/2, y 210, 260×180, margins 16×14, colour (48,48,48); translate(row)
/// </code>
///
/// ⭐ THE SPRITES ARE THE REGISTRY'S. `0x216028` builds the UI sprite table at `0x2EEFF0` (name `+0`, id
/// `+0x10`, stride 0x18; read here by evaluating its stores): 0x2A `messages\letterclosed.ssh`, 0x2B
/// `letteropen`, 0x2C `tutorial`, 0x2F `messcorner`, 0x30 `messfill`, 0x32 `messedge` -- the last three the
/// same nine-slice the lobby box draws (<see cref="UiPanel"/>). The research named the table `0x2EEFF4`.
///
/// ⚠ ADAPTERS, each said once:
/// - Frame: the HUD's (<see cref="Viewer"/>'s money readout): the console's 512-square space, positions as
///   fractions of the window, sizes by the height (one factor, so nothing stretches on a wide window).
/// - Layers: a record's frame (layer 4) is drawn BEHIND its letter (layer 0) -- the fill spans the letter's
///   own square, so the other order would hide it (INFERRED: a lower layer is nearer).
/// - The box's TEXT FACE is `Console.bff`, MEASURED rather than read: the widget's font is not traced, and it
///   is the only European face in which every authored line of all 95 advisor texts fits the box's 228 px
///   (widest 217 px, at most 7 lines = 98 of 152 px); `Large.bff` overflows 83 of them, `Small.bff` 54. The
///   colour is white (not read). No border: `0x108458` draws none of the frame sprites and the widget's own
///   painter was not traced. The text is drawn from the text database at run time.
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
    const int BoxWidth = 0x104, BoxY = 0xD2, BoxHeight = 0xB4, MarginX = 0x10, MarginY = 0x0E, TextShadow = 2;
    static readonly Color BoxColour = new(0x30 / 255f, 0x30 / 255f, 0x30 / 255f);
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
               + $"messcorner/messfill/messedge {(_panel != null ? "ok" : "MISSING")}; count in Large.bff, text in Console.bff "
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
        if (_closed != null)
            DrawTextureRect(_closed, new Rect2(new Vector2(EnvelopeX * sx, (textY - EnvelopeDrop) * k), new Vector2(Sprite, Sprite) * k), false);
        EnvelopeShown = true;
        ShownCount = int.TryParse(count, out int drawn) ? drawn : -1;          // what was drawn, not what is held
        // 0x108458: the box, when open.
        if (_stack.IsOpen && _stack.Selected is { } sel)
        {
            var box = new Rect2(new Vector2((view.X - BoxWidth * k) / 2f, BoxY * k), new Vector2(BoxWidth, BoxHeight) * k);
            DrawRect(box, BoxColour);
            string words = _text?.Invoke(sel) ?? "";
            ShownText = words;
            if (_textFont != null)
            {
                float y = box.Position.Y + MarginY * k;
                foreach (var line in words.Split('\n'))
                {
                    if (line.Length > 0)
                    {
                        var tex = _textFont.Render(line);
                        DrawTextureRect(tex, new Rect2(new Vector2(box.Position.X + MarginX * k, y), tex.GetSize() * k), false, Colors.White);
                    }
                    y += _textFont.LineAdvance * k;
                }
            }
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
