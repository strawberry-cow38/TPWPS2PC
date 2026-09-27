using System;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>⭐⭐⭐ THE LOBBY'S CENTRED MESSAGE BOX -- the panel that names the park you are standing
/// on. `this+0x1950` of the `WorldMapSelector`: built by `FUN_0012c818`, filled by
/// `FUN_00218f78`, drawn by `FUN_0012c968`. See `findings/lobby-menu.md`.
///
/// ⭐ EVERY NUMBER HERE IS THE CODE'S. None of this box is authored in a scene file -- unlike the
/// laptop, whose coordinates all come out of `MENUS.WAD` -- so the geometry had to be read out of
/// the draw, and it is entirely arithmetic:
///
///   width  = max(textWidth(line1), textWidth(line2)) + 0x3C, capped at 0x1C2
///   height = textHeight + 0x32          (one line)
///          = h1 + 100 + h2              (two lines)
///   x      = (0x1FF - width)  >> 1      ⭐ centred in the console's 512-square UI space
///   y      = (0x1FF - height) >> 1
///
/// ⚠ The 0x1C2 (450) cap sits INSIDE the two-line branch in `FUN_0012CCE0`, so a very wide single
/// line is NOT clamped. That is the shape of the original and it is kept, not tidied.</summary>
public partial class LobbyMessageBox : Control
{
    /// <summary>The console's UI square. ⭐ The same 512 the laptop panel fits itself to, which is
    /// why `0x1FF` appears as the centring constant in the draw.</summary>
    public const float Native = 512f;

    const int Margin = 0x3C;        // 60, added to the widest line
    const int MaxWidth = 0x1C2;     // 450, two-line branch only
    const int OneLinePad = 0x32;    // 50
    const int TwoLinePad = 100;
    const int Line1Y = 0x10;        // 16
    const int Line2Y = 0x30;        // 48

    FontText _font;
    string _line1 = "", _line2 = "";
    string[] _buttons = Array.Empty<string>();

    // ⭐ Current and target, in console units. ⚠⚠ INTEGERS, because the original's arithmetic
    // IS the snap condition: `step = (target - current) >> 1; if (step == 0) current = target;`
    // -- so a gap of 1 shifts to 0 and lands exactly. A float version of the same loop halves
    // forever and NEVER settles; the box drew its frame and never its text, because the content
    // is gated on having settled.
    int _w, _h, _wTarget, _hTarget;

    public bool Open { get; private set; }

    /// <summary>⚠ For the instrument only: the size `FUN_0012CCE0` would have computed.</summary>
    public int TargetWidth => _wTarget;
    public int TargetHeight => _hTarget;

    public LobbyMessageBox()
    {
        MouseFilter = MouseFilterEnum.Ignore;   // the lobby is arrows-only; this never takes input
        Visible = false;
    }

    public void Configure(FontText font) => _font = font;

    /// <summary>Show the box. ⚠ It opens from NOTHING every time -- `FUN_0012c968`'s ease starts
    /// from whatever the current size is, and a fresh prompt has been zeroed, so the grow is part
    /// of the box appearing rather than a flourish added here.</summary>
    public void Show(string line1, string line2, params string[] buttons)
    {
        _line1 = line1 ?? ""; _line2 = line2 ?? "";
        _buttons = buttons ?? Array.Empty<string>();
        Measure();
        _w = 0; _h = 0;
        Open = true; Visible = true;
        QueueRedraw();
    }

    public new void Hide() { Open = false; Visible = false; QueueRedraw(); }

    /// <summary>`FUN_0012CCE0`: the target size, recomputed whenever a line is added.</summary>
    void Measure()
    {
        int lh = _font?.LineAdvance ?? 16;
        int w = (_font?.Measure(_line1) ?? _line1.Length * 8) + Margin;
        int h = lh + OneLinePad;
        if (_line2.Length > 0)
        {
            h = lh + TwoLinePad + lh;
            int w2 = (_font?.Measure(_line2) ?? _line2.Length * 8) + Margin;
            if (w < w2) w = w2;
            if (w > MaxWidth) w = MaxWidth;     // ⚠ two-line branch only, as the original has it
        }
        _wTarget = w; _hTarget = h;
    }

    /// <summary>⭐ HALVE THE GAP EACH FRAME, and snap when the step rounds to nothing -- exactly
    /// `(target - current) >> 1` with the zero case assigning the target. ⚠ Per CONSOLE frame, so
    /// the box opens at the same speed whatever the machine draws at.</summary>
    public void Step(double delta)
    {
        if (!Open) return;
        int steps = Math.Clamp((int)Math.Round(delta * ConsoleClock.TicksPerSecond), 1, 8);
        for (int i = 0; i < steps; i++)
        {
            int dw = (_wTarget - _w) >> 1;
            if (dw == 0) _w = _wTarget; else _w += dw;
            int dh = (_hTarget - _h) >> 1;
            if (dh == 0) _h = _hTarget; else _h += dh;
        }
        QueueRedraw();
    }

    bool Settled => _w == _wTarget && _h == _hTarget;

    public override void _Draw()
    {
        if (!Open || _font == null) return;
        var view = GetViewportRect().Size;
        float s = Mathf.Max(0.05f, Mathf.Min(view.X, view.Y) / Native);
        var origin = (view - new Vector2(Native, Native) * s) / 2f;

        // ⭐ (0x1FF - size) >> 1 on BOTH axes -- the original's own centring, not "put it in the
        // middle" reimplemented.
        float x = (0x1FF - _w) * 0.5f, y = (0x1FF - _h) * 0.5f;
        // ⚠ Still computed: it is the LAYOUT rect the text and buttons are placed inside, even
        // though nothing paints it.
        var rect = new Rect2(origin + new Vector2(x, y) * s, new Vector2(_w, _h) * s);

        // ⭐⭐⭐ THERE IS NO FRAME. Master: "then get it from the source" -- so I did, and the
        // source draws nothing at all.
        //
        // `FUN_00141F68` dispatches on the widget kind (`this+0x18`, which the box's constructor
        // sets to **2**) to `FUN_00142610`, which is an eight-piece border: four corners at the
        // rect's corners and four edges taking the width or the height. **Every one of the eight
        // is a stub** -- `jr ra; nop`, 8 bytes -- and so is every painter they call
        // (`0x1424C0`, `0x1424C8`, `0x1424D0`, `0x1424D8`). The whole border compiles to nothing.
        //
        // ⚠ The control matters here, because "everything is a stub" is exactly what a broken
        // stub-detector says: the text call `0x138798` and the colour call `0x1388E8` on the same
        // path are REAL code. It is the frame specifically that is empty.
        //
        // ⭐ So the console shows centred TEXT and BUTTONS over the lobby with no panel behind
        // them, and the blue rectangle that used to be here was mine. `1:1` means drawing what it
        // draws, including nothing.

        // ⭐ CONTENT ONLY ONCE THE BOX HAS FINISHED GROWING -- `if (!bVar1)` in FUN_0012c968.
        if (!Settled) return;

        void Centred(string text, float lineY)
        {
            if (string.IsNullOrEmpty(text)) return;
            var tex = _font.Render(text);
            var size = tex.GetSize() * s;
            // ⭐ x + width/2 in the original, with the text call's own centre justify.
            DrawTextureRect(tex, new Rect2(
                rect.Position + new Vector2(rect.Size.X / 2f - size.X / 2f, lineY * s), size), false);
        }
        Centred(_line1, Line1Y);
        Centred(_line2, Line2Y);

        // ⭐ Up to three, drawn from the box's own corner. Spread along the foot: the original
        // hands each button its own draw through a vtable and those are not read, so the row
        // layout here is this port's and says so.
        if (_buttons.Length == 0) return;
        float slot = rect.Size.X / _buttons.Length;
        for (int i = 0; i < _buttons.Length && i < 3; i++)
        {
            var tex = _font.Render(_buttons[i]);
            var size = tex.GetSize() * s;
            DrawTextureRect(tex, new Rect2(
                rect.Position + new Vector2(slot * i + slot / 2f - size.X / 2f,
                                            rect.Size.Y - size.Y - 8f * s), size), false);
        }
    }
}
