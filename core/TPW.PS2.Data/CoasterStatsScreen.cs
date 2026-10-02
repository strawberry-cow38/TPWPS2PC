using System.Globalization;

namespace TPW.PS2.Data;

/// <summary>⭐ The coaster stats screen `0x11BD28`, as data: what the tool draw `0x11B2F8` puts up every frame
/// while `[0x2AC430]` is set -- from the finish (Triangle, `0x11BA00` → `0x11BBD8`) until OK (`0x11BCA8`) or Back
/// (Triangle again). Strawberry, 2026-10-02: "coaster scoring is done after u finish editing pylons".
///
/// READ, `0x11BD28`: a kind-1 nine-slice widget (`0x141EA0`; `+0x18` = 1, `+0x1C` visible, `+0x38 |= 1`) at
/// (0x22, 0xC0) sized 0x1B0 × 0xD8 with fill selector `+0x48` = 1, so the fill is sprite 0x30 `messfill`, not the
/// advisor box's 0x31 `wboxfill`; drawn at z 4. Then the text, through the context `[0x2EEA58]`: font id 0
/// (`Small.bff`), justify 0 (drawn from the pen: `0x20ACF8` moves the pen only for justify 1), drop shadow
/// (2, 2) on (`0x20B258`), colour (0xFF, 0xFF, 0xFF). Labels at x 0x32, values at x 300, units at x 0x168; the
/// rows 0x14 apart from y 0xD0, then the rating at y 0x184. Integers truncate (`cvt.w.s`) into `"%4i"`
/// (`0x20B0F8`), the three g figures go through `"%4.1f"` (`0x20B198`, format at `0x36C878`).
///
/// ⭐ The rating is NOT stored: `0x11BD28` calls `0x122ED0` on the record every frame, which is also what records
/// the Ultimate award (<see cref="ParkAwards.RecordUltimate"/>). So an open ring's zeroed record still reads a
/// verdict -- "Too Slow" -- not a blank.</summary>
public static class CoasterStatsScreen
{
    /// <summary>The panel widget's rect `+8`, `+0xA`, `+0x14`, `+0x16`.</summary>
    public const int PanelX = 0x22, PanelY = 0xC0, PanelWidth = 0x1B0, PanelHeight = 0xD8;
    public const int LabelX = 0x32, ValueX = 300, UnitX = 0x168;
    /// <summary>`STR_COASTERSTATS_*`'s "Coaster Rating:" and the verdict beside it.</summary>
    public const int RatingLabelRow = 0x228, RatingY = 0x184;
    /// <summary>`0x11BBD8`'s button bar `0x13E340(Back 0x221, blank 0x136, OK 0x243, blank 0x136)`: Triangle Back,
    /// Cross OK.</summary>
    public const int BackRow = 0x221, OkRow = 0x243, BlankRow = 0x136;

    /// <summary>One row: its y, the label's text row, the value as drawn, and the unit's text row (0: none --
    /// "Number of Drops" has no unit).</summary>
    public readonly record struct Line(int Y, int LabelRow, string Value, int UnitRow);

    /// <summary>The eight rows, in `0x11BD28`'s draw order.</summary>
    public static Line[] Lines(CoasterStats s) => new[]
    {
        new Line(0xD0, 0xF4, Int4(s.Duration), 0x2D),           // Duration, secs
        new Line(0xE4, 0x244, Int4(s.Length), 0x227),           // Length, meters
        new Line(0xF8, 0x335, Int4(s.MaxSpeed), 0x17D),         // Maximum Speed, kph
        new Line(0x10C, 0x382, Int4(s.Drops), 0),               // Number of Drops
        new Line(0x120, 0xDB, Int4(s.SteepestDrop), 0x143),     // Steepest Drop, deg
        new Line(0x134, 0xB4, Float41(s.MaxVertPos), 0x2D6),    // Max Vert +Gs, g
        new Line(0x148, 0x53, Float41(s.MaxVertNeg), 0x2D6),    // Max Vert -Gs, g
        new Line(0x15C, 0xC9, Float41(s.MaxLat), 0x2D6),        // Max Lat Gs, g
    };

    /// <summary>`(int)v` into `"%4i"`: truncated toward zero, padded to four with spaces.</summary>
    public static string Int4(float v) => ((int)v).ToString(CultureInfo.InvariantCulture).PadLeft(4);

    /// <summary>`"%4.1f"` the way a C printf rounds it: half to even on the EXACT binary value, and the sign kept
    /// on a negative that rounds to zero ("-0.0"). ⚠ .NET's "0.0" rounds half away from zero, so 0.25 would read
    /// "0.3" where printf says "0.2" -- and the −Gs figure is a quarter of a float, which makes exact halves
    /// likely. INFERRED that the EE's libc rounds like every dtoa; its sprintf was not read.</summary>
    public static string Float41(float v)
    {
        double tenths = Math.Round((double)v * 10.0, MidpointRounding.ToEven);   // exact: a float times 10 fits a double
        long n = (long)Math.Abs(tenths);
        string s = (float.IsNegative(v) ? "-" : "") + (n / 10).ToString(CultureInfo.InvariantCulture) + "."
                 + (n % 10).ToString(CultureInfo.InvariantCulture);
        return s.PadLeft(4);
    }

    /// <summary>`0x122ED0` on the record, as the screen calls it.</summary>
    public static (int Row, bool Ultimate) Rating(CoasterStats s) => CoasterSim.Rate(s.MaxLat, s.MaxSpeed, s.Drops);
}
