using Godot;
using System;
using System.Collections.Generic;

namespace TPWPS2Viewer;

/// <summary>⭐⭐ The laptop's GRAPH, as the console actually draws it. `findings/graph-widget.md`;
/// widget draw `FUN_0013f1c8`, series draw `FUN_0013f3b8`, plotter `FUN_0013f560`.
///
/// ⚠⚠ IT IS NOT A POLYLINE. The only primitive the plotter can emit (`FUN_00213838` ->
/// `FUN_00213980`) writes an axis-aligned left/top/right/bottom rectangle and a flat-colour
/// sprite -- it has **no way to draw a slope**. For each neighbouring pair it emits ONE filled
/// rectangle from the baseline up to the LOWER of the two values, so the result is a staircase
/// whose steps sit UNDER the line. Drawing a nice anti-aliased line here would look better and be
/// wrong.
///
/// ⚠ There are TWO passes: the series colour, then the same staircase 3px lower in `(0,56,51)` --
/// which is also the default series colour when none is assigned. That second pass is an ERASER:
/// what survives is a 3px-thick stepped line. On the console the two are separated by z
/// (`zdraw = colour == 0x35f0f0 ? Z : Z - 1`); here the later draw covers the earlier, which is
/// the same result in a painter's-algorithm canvas.
///
/// ⚠ The arithmetic is done in NATIVE units and only then scaled. The staircase's quantisation --
/// integer division, `>> 16` on the x step -- IS the look, and doing it in float screen space
/// would smooth away the thing being reproduced.</summary>
public static class LaptopGraph
{
    /// <summary>A series is always 24 buckets over the selected span; the x step is `w/24` in
    /// 16.16, so the last 1/24 of the box stays empty. That gap is the console's, not a bug.</summary>
    public const int Buckets = 24;

    /// <summary>`range = (max - min) + off` and `y = h - h*(v-min)/range + off`, with `off = -8`.
    /// ⚠ So a ZERO value does NOT sit on the bottom edge -- it plots 8px up, and a zero series
    /// reads as a line 8px above the floor of the plot area.</summary>
    public const int Offset = -8;

    /// <summary>⚠ The widget sizes each series to `h - 10`, so the bottom 10px of the authored
    /// rect are never painted by the plotter.</summary>
    public const int PlotInset = 10;

    /// <summary>The second pass's colour, and the default series colour: `0x35f0f0` = (0,56,51).</summary>
    public static readonly Color EraseColour = Color.Color8(0, 56, 51);

    /// <summary>How far below the first pass the eraser pass is drawn.</summary>
    public const int EraseDrop = 3;

    /// <summary>The year ticks along the bottom are BLUE, and only appear when the span is more
    /// than one year.</summary>
    public static readonly Color YearTick = Color.Color8(0, 0, 255);

    /// <summary>One staircase step, in NATIVE units: the rect the console would emit.</summary>
    readonly record struct Step(int Left, int Top, int Right, int Bottom);

    /// <summary>⭐ The plotter, followed literally. `values` are the buckets (oldest first);
    /// `h`/`w` are the series box in native units.</summary>
    static List<Step> Steps(IReadOnlyList<int> values, int w, int h, int min, int max)
    {
        var outSteps = new List<Step>();
        // ⚠ The console returns at once with fewer than two points; one bucket draws nothing.
        if (values == null || values.Count < 2) return outSteps;

        long xstep = ((long)w << 16) / Buckets;
        int range = (max - min) + Offset;
        if (range < 1) range = 1;

        for (int i = 0; i < values.Count - 1; i++)
        {
            int x0 = (int)((i * xstep) >> 16);
            int x1 = (int)(((i + 1) * xstep) >> 16);
            int v0 = values[i], v1 = values[i + 1];
            // ⚠ Integer division, as the console does it.
            int y0 = h - h * (v0 - min) / range + Offset;
            int y1 = h - h * (v1 - min) / range + Offset;
            y0 = Math.Max(Math.Min(y0, h), 0);
            y1 = Math.Max(Math.Min(y1, h), 0);

            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int len = (int)Math.Sqrt((double)dx * dx + (double)dy * dy);
            int s1, s0;
            if (len != 0)
            {
                // round(dx/len) and round(1.709 * dy/len) -- the console's own fixed-point
                // rounding, which widens a steep rise by up to 2px a side and narrows a fall.
                s1 = (int)((((long)dx << 12) / len + 2048) >> 12);
                s0 = (int)((((long)dy * 7000) / len + 2048) >> 12);
            }
            else { s1 = dx; s0 = dy; }

            int bottom = h;
            // ⭐ RISING takes the LOWER point (i) as the top; flat or falling takes point i+1.
            outSteps.Add(y1 < y0
                ? new Step(x0 - s0, bottom, x1 + s0, y0 + s1)
                : new Step(x0 + s0, bottom, x1 - s0, y1 + s1));
        }
        return outSteps;
    }

    /// <summary>⭐ Draw one series into <paramref name="box"/> (screen space, already scaled).
    /// <paramref name="scale"/> converts native units to that box.</summary>
    public static void DrawSeries(CanvasItem ci, Rect2 box, IReadOnlyList<int> values,
                                  int min, int max, Color colour, float scale)
    {
        int w = (int)Math.Round(box.Size.X / scale);
        int h = (int)Math.Round(box.Size.Y / scale) - PlotInset;
        if (h <= 0) return;
        var steps = Steps(values, w, h, min, max);

        // ⚠ Order IS the z rule here: the series colour first, then the eraser 3px lower on top.
        // Reversing them paints the whole staircase dark and loses the line entirely.
        Paint(ci, box, steps, colour, 0, scale);
        Paint(ci, box, steps, EraseColour, EraseDrop, scale);
    }

    static void Paint(CanvasItem ci, Rect2 box, List<Step> steps, Color c, int drop, float scale)
    {
        foreach (var st in steps)
        {
            // The emitter sorts left/right and top/bottom before drawing, so a step whose
            // widening crossed its own edges still covers a sane rect.
            float l = Math.Min(st.Left, st.Right), r = Math.Max(st.Left, st.Right);
            float t = Math.Min(st.Top, st.Bottom) + drop, b = Math.Max(st.Top, st.Bottom) + drop;
            if (b <= t || r <= l) continue;
            var rect = new Rect2(box.Position + new Vector2(l, t) * scale,
                                 new Vector2(r - l, b - t) * scale);
            // ⚠ Clipped to the box: the eraser pass drops 3px and would otherwise paint below it.
            var clipped = rect.Intersection(box);
            if (clipped.Size.X > 0 && clipped.Size.Y > 0) ci.DrawRect(clipped, c);
        }
    }

    /// <summary>⭐ The peak's headroom. The series' own max is the peak times a factor -- 1.1 on
    /// the finance screens -- so the peak itself plots above the top edge and is clamped there.
    /// </summary>
    public static int HeadroomMax(IReadOnlyList<int> values, float factor)
    {
        int peak = 0;
        if (values != null) foreach (var v in values) if (v > peak) peak = v;
        return (int)(peak * factor);
    }
}
