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
    UiPanel _panel;
    string _line1 = "", _line2 = "";
    string[] _buttons = Array.Empty<string>();

    // ⭐ Current and target, in console units. ⚠⚠ INTEGERS, because the original's arithmetic
    // IS the snap condition: `step = (target - current) >> 1; if (step == 0) current = target;`
    // -- so a gap of 1 shifts to 0 and lands exactly. A float version of the same loop halves
    // forever and NEVER settles; the box drew its frame and never its text, because the content
    // is gated on having settled.
    int _w, _h, _wTarget, _hTarget;
    string _probe;

    public bool Open { get; private set; }

    /// <summary>Which button is picked, or -1 when the box has none. ⚠ A box with no buttons is
    /// not a degenerate prompt -- it is the normal case: the park-name box carries none.</summary>
    public int Button { get; private set; } = -1;

    /// <summary>True when this box is a PROMPT (has buttons) rather than a plain caption.</summary>
    public bool IsPrompt => _buttons.Length > 0;

    /// <summary>⚠ For the instrument only: the size `FUN_0012CCE0` would have computed.</summary>
    public int TargetWidth => _wTarget;
    public int TargetHeight => _hTarget;

    public LobbyMessageBox()
    {
        MouseFilter = MouseFilterEnum.Ignore;   // the lobby is arrows-only; this never takes input
        Visible = false;
    }

    public void Configure(FontText font, UiPanel panel = null)
    {
        _font = font;
        if (panel != null) _panel = panel;
    }

    /// <summary>Show the box. ⚠ It opens from NOTHING every time -- `FUN_0012c968`'s ease starts
    /// from whatever the current size is, and a fresh prompt has been zeroed, so the grow is part
    /// of the box appearing rather than a flourish added here.</summary>
    public void Show(string line1, string line2, params string[] buttons)
    {
        _line1 = line1 ?? ""; _line2 = line2 ?? "";
        _buttons = buttons ?? Array.Empty<string>();
        Measure();
        Button = _buttons.Length > 0 ? 0 : -1;    // ⚠ OK is index 0, as `FUN_00219338` reads it
        _w = 0; _h = 0;
        Open = true; Visible = true;
        QueueRedraw();
    }

    /// <summary>⚠ CLAMPED, not wrapped -- and a box with no buttons ignores it entirely.</summary>
    public void MoveButton(int by)
    {
        if (!Open || _buttons.Length == 0) return;
        Button = Mathf.Clamp(Button + by, 0, _buttons.Length - 1);
        QueueRedraw();
    }

    /// <summary>⚠ FOR THE SHOT HARNESS. The open eases about eleven steps, roughly 0.2 s at 60fps
    /// -- correct in play and impossible to photograph at the 1 fps a wound render manages, which
    /// is why the prompt kept coming back as an empty frame. This skips the ease; nothing in the
    /// game calls it.</summary>
    public void SnapForShot() { _w = _wTarget; _h = _hTarget; QueueRedraw(); }

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

        // ⭐⭐⭐ THE FRAME, from the disc. `UI.WAD/messages/Messcorner|Messedge|Messfill`, the
        // same nine-slice the context menu already drew -- registered by `FUN_00216028` as sprite
        // ids 0x2F / 0x30, all three pieces 16x16.
        //
        // ⚠⚠ I SHIPPED THIS WITH NO FRAME AT ALL FOR ONE COMMIT. The widget path that looks like
        // it draws one (`FUN_00142610`: four corners, four edges) is EIGHT STUBS, and so is every
        // painter they call, so reading only the code said "nothing is drawn" -- and I even put a
        // control on the stub detector, which passed. What I never checked was whether the ART
        // existed. Master, who has played it: "there is a frame btw". An empty function proves
        // that function draws nothing; it does not prove the thing is not drawn.
        _panel?.Draw(this, rect, s);

        // ⭐ CONTENT ONLY ONCE THE BOX HAS FINISHED GROWING -- `if (!bVar1)` in FUN_0012c968.
        if (!Settled)
        {
            if (_probe != $"{_w}x{_h}->{_wTarget}x{_hTarget}")
            {
                _probe = $"{_w}x{_h}->{_wTarget}x{_hTarget}";
                GD.Print($"[box] not settled: {_w}x{_h} -> {_wTarget}x{_hTarget}, "
                       + $"lines \"{_line1}\" / \"{_line2}\", {_buttons.Length} buttons");
            }
            return;
        }

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
            // ⭐ The picked button is yellow, like every other selected row in this game's UI.
            DrawTextureRect(tex, new Rect2(
                rect.Position + new Vector2(slot * i + slot / 2f - size.X / 2f,
                                            rect.Size.Y - size.Y - 8f * s), size), false,
                i == Button ? new Color(1f, 1f, 0f) : Colors.White);
        }
    }
}
