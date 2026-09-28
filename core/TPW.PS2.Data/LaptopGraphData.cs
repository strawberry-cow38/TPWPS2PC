using System;

namespace TPW.PS2.Data;

/// <summary>⭐⭐ The 24 buckets a laptop graph plots, built the way the console builds them
/// (`FUN_001363a8` for finance, `FUN_00186d38` for park statistics).
/// `findings/graph-widget.md` §1.5.
///
/// Both screens keep 25 words per series: `[0]` is the max used for scaling and `[1..24]` are the
/// buckets. Bucket 0 is the OLDEST month and bucket 23 the newest, so a young park fills from the
/// left.</summary>
public static class LaptopGraphData
{
    public const int Buckets = 24;

    /// <summary>`arr[0] = max * 0x119a >> 12` -- the headroom factor, 1.1001 rather than a round
    /// 1.1. Kept exact because the peak's plotted y depends on it.</summary>
    public const int HeadroomNumerator = 0x119a, HeadroomShift = 12;

    /// <summary>⭐ The builder, followed literally.
    ///
    /// <paramref name="getter"/> takes MONTHS AGO and returns the console's raw value (money in
    /// TENTHS -- it is divided by 10 here, as the console does inside the loop).
    /// <paramref name="months"/> is the park's month counter, <paramref name="years"/> the span
    /// the year selector holds (1, 2, 6 or 12).
    ///
    /// ⚠⚠ RETAIL ARITHMETIC, REPRODUCED ON PURPOSE. Two things look like bugs and are the
    /// console's:
    /// <list type="bullet">
    /// <item>`m` is reset to `lower` after each bucket, which RE-INCLUDES the boundary month, so
    /// with a one-year span every month lands in TWO buckets.</item>
    /// <item>The first bucket asks for month `span`, which is one past the accumulator rings
    /// (their getter answers 0 at `k >= count`), so the LEFTMOST bucket of Money In / Gate / Shop
    /// / Sideshow / Wages is always 0 and the next is halved.</item>
    /// </list>
    /// Fixing either would make our graph disagree with the console's on every park.</summary>
    public static int[] Build(Func<int, int> getter, int months, int years, out int max)
    {
        var bucket = new int[Buckets];
        max = 0;
        if (getter == null || years <= 0) return bucket;

        int span = Math.Min(months, years * 12);
        int m = span;
        for (int b = 0; b < Buckets; b++)
        {
            int lower = span - b * years / 2;          // integer division, as the console does
            long sum = 0; int count = 0;
            while (m >= lower && m >= 0) { sum += getter(m) / 10; count++; m--; }
            m = lower;                                  // ⚠ re-includes the boundary month
            if (count != 0) sum /= count;
            bucket[b] = (int)sum;
            if (bucket[b] > max) max = bucket[b];
            if (lower <= 0) break;
        }
        max = (int)((long)max * HeadroomNumerator >> HeadroomShift);
        return bucket;
    }

    /// <summary>How many buckets a park this young actually fills: `min(24, 24 * months / span)`.
    /// ⚠ The plotter needs at least TWO points or it draws nothing at all, which on a brand new
    /// park is the correct and slightly surprising answer.</summary>
    public static int Filled(int months, int years)
    {
        int span = Math.Max(1, years * 12);
        return Math.Max(0, Math.Min(Buckets, Buckets * months / span));
    }
}
