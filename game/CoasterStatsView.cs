using System;
using System.Collections.Generic;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>⭐ THE COASTER STATS SCREEN, DRAWN (`0x11BD28`, see <see cref="CoasterStatsScreen"/> for every number).
/// It used to be a status line; strawberry, 2026-10-02: "coaster scoring is done after u finish editing pylons".
///
/// What is drawn, in this order (smaller z nearer, findings/advisor-visuals.md §1.5; every piece here is
/// alpha-sorted -- `messfill`/`messcorner`/`messedge` are type 0x85, findings/graph-widget.md):
/// - the text's drop shadow, black at (+2, +2) and z 0+8 (`0x20B258(ctx, 2, 2)`, READ in `0x11BD28`);
/// - the panel at z 4: `0x142090` with fill selector 1 = sprite 0x30 `messfill`, the translucent blue the
///   advisor's records use (17,30,255 at alpha 110), in `h &gt;&gt; 4` rows of w×16 (216 = 13·16 + 8, so one
///   8-unit remainder row), then the 8-unit corners and edges;
/// - the text at z 0, white.
/// So the shadow shows THROUGH the translucent panel rather than over it -- what the z order says, not a choice.
///
/// ⚠ ADAPTER, the advisor box's (<see cref="AdvisorStackView"/>): positions are the console's 512-unit space with
/// the panel's centre line placed as a fraction of the window and everything else hung off it by one factor from
/// the height, so the panel keeps its shape on a wide window.</summary>
public partial class CoasterStatsView : Control
{
    const float Native = 512f;
    const int Frame8 = 8, FillRow = 16, Shadow = 2;
    const int Centre = CoasterStatsScreen.PanelX + CoasterStatsScreen.PanelWidth / 2;
    static readonly Color ShadowColour = new(0f, 0f, 0f, 1f);

    UiPanel _panel;
    Func<FontText> _font;
    Func<int, string> _text;

    /// <summary>The record on screen; null hides the screen.</summary>
    public CoasterStats Stats { get; set; }

    /// <summary>One string as drawn: its text and where, in the console's units -- the OUTPUT a check reads.</summary>
    public readonly record struct Shown(string Text, int X, int Y);
    readonly List<Shown> _shown = new();
    public IReadOnlyList<Shown> ShownText => _shown;
    /// <summary>Panel pieces drawn last frame (fill rows, corners, edges).</summary>
    public int PanelBlits { get; private set; }

    public void Configure(AssetLibrary lib, Func<FontText> font, Func<int, string> text)
    {
        _panel ??= lib != null ? UiPanel.Load(lib) : null;
        _font = font;
        _text = text;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        _shown.Clear();
        PanelBlits = 0;
        if (Stats == null) return;
        var view = GetViewportRect().Size;
        float sx = view.X / Native, k = view.Y / Native;
        float X(int u) => Centre * sx + (u - Centre) * k;
        Rect2 R(int u, int v, int w, int h) => new(new Vector2(X(u), v * k), new Vector2(w * k, h * k));

        var strings = new List<(string Text, int X, int Y)>();
        void Put(int x, int y, string s) { if (!string.IsNullOrEmpty(s)) strings.Add((s, x, y)); }
        string Row(int row) => _text?.Invoke(row) ?? "";
        foreach (var line in CoasterStatsScreen.Lines(Stats))
        {
            Put(CoasterStatsScreen.LabelX, line.Y, Row(line.LabelRow));
            Put(CoasterStatsScreen.ValueX, line.Y, line.Value);
            if (line.UnitRow != 0) Put(CoasterStatsScreen.UnitX, line.Y, Row(line.UnitRow));
        }
        Put(CoasterStatsScreen.LabelX, CoasterStatsScreen.RatingY, Row(CoasterStatsScreen.RatingLabelRow));
        Put(CoasterStatsScreen.ValueX, CoasterStatsScreen.RatingY, Row(CoasterStatsScreen.Rating(Stats).Row));

        var font = _font?.Invoke();
        // z 8: the shadows, furthest back.
        if (font != null)
            foreach (var (s, x, y) in strings)
            {
                var tex = font.Render(s);
                DrawTextureRect(tex, R(x + Shadow, y + Shadow, tex.GetWidth(), tex.GetHeight()), false, ShadowColour);
            }
        // z 4: the panel (0x142090).
        if (_panel != null)
        {
            int x = CoasterStatsScreen.PanelX, y = CoasterStatsScreen.PanelY;
            int w = CoasterStatsScreen.PanelWidth, h = CoasterStatsScreen.PanelHeight;
            for (int i = 0; i < h >> 4; i++) { DrawTextureRect(_panel.Fill, R(x, y + FillRow * i, w, FillRow), false); PanelBlits++; }
            if ((h & 15) != 0) { DrawTextureRect(_panel.Fill, R(x, y + (h & ~15), w, h & 15), false); PanelBlits++; }
            DrawTextureRect(_panel.CornerTopRight, R(x + w, y - Frame8, Frame8, Frame8), false);
            DrawTextureRect(_panel.CornerBottomRight, R(x + w, y + h, Frame8, Frame8), false);
            DrawTextureRect(_panel.CornerTopLeft, R(x - Frame8, y - Frame8, Frame8, Frame8), false);
            DrawTextureRect(_panel.CornerBottomLeft, R(x - Frame8, y + h, Frame8, Frame8), false);
            DrawTextureRect(_panel.EdgeTop, R(x, y - Frame8, w, Frame8), false);
            DrawTextureRect(_panel.EdgeBottom, R(x, y + h, w, Frame8), false);
            DrawTextureRect(_panel.EdgeRight, R(x + w, y, Frame8, h), false);
            DrawTextureRect(_panel.EdgeLeft, R(x - Frame8, y, Frame8, h), false);
            PanelBlits += 8;
        }
        // z 0: the text, white, drawn from the pen (justify 0).
        foreach (var (s, x, y) in strings)
        {
            if (font != null)
            {
                var tex = font.Render(s);
                DrawTextureRect(tex, R(x, y, tex.GetWidth(), tex.GetHeight()), false, Colors.White);
            }
            _shown.Add(new Shown(s, x, y));
        }
    }
}
