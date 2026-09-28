namespace TPW.PS2.Data;

/// <summary>The park's calendar, read out of `FUN_0016b240` -- the function that advances it.
///
/// ⭐⭐ THE DATE IS REAL AND THE PORT HAD NO CLOCK AT ALL. Master's HUD shows `04/08/2002` bottom
/// left. Searching the executable for a `%d/%d/%d` or a month name finds NOTHING, and I reported
/// that as "the date is not in this build" -- which was a claim about the GAME made off a failed
/// SEARCH. The HUD composes the date from digits and draws them with the bitmap font, exactly as
/// the money readout does, so there is no format string to find and there never was.
///
/// The advance, `FUN_0016b240`, on a six-word struct:
/// <code>
///   clock[0] += FrameTime();              // 0x1c4920, the 0x1000-a-tick getter the camera uses
///   if (clock[0] > 0xEFFFF) {             // one DAY
///       dim = daysInMonth[clock[1]];
///       clock[5] -= 1;                    // a countdown
///       clock[4] += 1;                    // total days elapsed
///       clock[0] = 0;
///       clock[3] += 1;                    // day of month
///       if (dim &lt;= clock[3]) {
///           clock[3] = 0;
///           clock[1] += 1;
///           if (clock[1] > 0xb) { clock[1] = 0; clock[2] += 1; }
///       }
///   }
/// </code>
///
/// ⚠ THE MONTH IS 0-BASED AND SO IS THE TABLE. I first called the table "1-based, `[0,31,28,..]`
/// at 0x361d87" off the byte pattern -- there is a zero byte before it. The consumer indexes
/// **0x361d88** with a month that wraps at `> 0xb`, so the table starts at 31 and the leading zero
/// is not part of it. Read the code that indexes a table, never the bytes.
///
/// ⚠⚠ NO LEAP YEARS. February is 28 in the table and nothing anywhere tests the year. Kept.</summary>
public sealed class ParkClock
{
    public const int StateSchemaVersion = 1;

    /// <summary>All six clock words, plus the publicly mutable global calendar table.
    /// Dates are zero-based and countdown may be negative. The date and elapsed days need not
    /// agree: Set changes the date without resetting the elapsed/deadline counters.</summary>
    public sealed record State
    {
        public required int SchemaVersion { get; init; }
        public required int Accumulator { get; init; }
        public required int Month { get; init; }
        public required int Year { get; init; }
        public required int Day { get; init; }
        public required int TotalDays { get; init; }
        public required int Countdown { get; init; }
        public required byte[] DaysInMonth { get; init; }
    }

    /// <summary>Detached state; capture/restore must run with simulation updates paused.</summary>
    public State CaptureState() => new()
    {
        SchemaVersion = StateSchemaVersion, Accumulator = Accumulator,
        Month = Month, Year = Year, Day = Day, TotalDays = TotalDays, Countdown = Countdown,
        DaysInMonth = (byte[])DaysInMonth.Clone()
    };

    /// <summary>Validates everything before mutation. Restoring the global calendar table
    /// affects every ParkClock in the process; a whole-park coordinator must account for that.</summary>
    public void RestoreState(State state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != StateSchemaVersion
            || state.Accumulator < 0 || state.Accumulator >= UnitsPerDay
            || state.Month < 0 || state.Month >= 12
            || state.Year < 0 || state.Year > int.MaxValue - EpochYear
            || state.TotalDays < 0 || state.DaysInMonth == null || state.DaysInMonth.Length != 12)
            throw new ArgumentException("Invalid clock state schema, counters or calendar.", nameof(state));
        var days = (byte[])state.DaysInMonth.Clone();
        if (days.Any(n => n < 1 || n > 31) || state.Day < 0 || state.Day >= days[state.Month])
            throw new ArgumentException("Invalid clock calendar or day of month.", nameof(state));
        Accumulator = state.Accumulator;
        Month = state.Month;
        Year = state.Year;
        Day = state.Day;
        TotalDays = state.TotalDays;
        Countdown = state.Countdown;
        days.CopyTo(DaysInMonth, 0);
    }

    /// <summary>`0x361d88`, indexed by a 0-based month.</summary>
    public static readonly byte[] DaysInMonth =
        { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

    /// <summary>`0x16b268`: the accumulator rolls over a DAY when it passes `0xEFFFF`.</summary>
    public const int UnitsPerDay = 0xF0000;

    /// <summary>`FUN_001c4920` hands out frame time in units of `0x1000` a tick, which is the same
    /// unit the camera and the selection box's breath already use.</summary>
    public const int UnitsPerTick = 0x1000;

    /// <summary>240 ticks -- and at the console's 50Hz that is <b>4.8 seconds an in-game day</b>,
    /// so a month is about 2.4 minutes and a year is 29.2. A number to CHECK against the real
    /// game rather than a rate to tune by feel.</summary>
    public const int TicksPerDay = UnitsPerDay / UnitsPerTick;

    /// <summary>⭐ A park starts on <b>01/01/2000</b>. Master, who can see the real game, settled
    /// it: "its meant to be 1/1/2000 as the start date". So the counters all start at zero and the
    /// year is this plus <see cref="Year"/>.
    ///
    /// ⚠ Still not read out of the image -- the executable holds no year literal at all (1999,
    /// 2000, 2001 and 2002 appear nowhere as an immediate), so the epoch arrives from a scenario
    /// or save that is not traced. It is master's observation, which is a source, not a guess --
    /// but it is not the disc, and that distinction is kept here on purpose.</summary>
    public const int EpochYear = 2000;

    /// <summary>`clock[0]`.</summary>
    public int Accumulator { get; private set; }
    /// <summary>`clock[1]`, 0..11.</summary>
    public int Month { get; private set; }
    /// <summary>`clock[2]`, counting up from the epoch.</summary>
    public int Year { get; private set; }
    /// <summary>`clock[3]`, 0-based within the month.</summary>
    public int Day { get; private set; }
    /// <summary>`clock[4]`, every day the park has been open.</summary>
    public int TotalDays { get; private set; }
    /// <summary>`clock[5]`. ⚠ The one field here that is INFERRED: it only ever decrements, once a
    /// day, which is the shape of a scenario deadline. Nothing read confirms what it is for.</summary>
    public int Countdown { get; private set; }

    /// <summary>Day and month as people read them, both 1-based.</summary>
    public int DayOfMonth => Day + 1;
    public int MonthOfYear => Month + 1;
    public int DisplayYear => EpochYear + Year;

    /// <summary>`FUN_0016d2e8`: how long the PREVIOUS month was, wrapping -1 to December.</summary>
    public int DaysInPreviousMonth => DaysInMonth[Month == 0 ? 11 : Month - 1];

    /// <summary>⭐ Returns true on the day it rolls, so a caller can hang the monthly and weekly
    /// work off it the way `FUN_0016b060` does -- that function snapshots month, day and year,
    /// ticks, and then compares: a changed MONTH runs the finance rollover and the in-the-red
    /// warning chain, and a changed day whose number divides by 7 runs the weekly pass.</summary>
    public bool Advance(int frameUnits, out bool dayRolled, out bool monthRolled, out bool yearRolled)
    {
        int month = Month, year = Year;
        dayRolled = monthRolled = yearRolled = false;
        Accumulator += frameUnits;
        if (Accumulator <= UnitsPerDay - 1) return false;
        Accumulator = 0;
        Countdown -= 1;
        TotalDays += 1;
        Day += 1;
        dayRolled = true;
        if (DaysInMonth[Month] <= Day)
        {
            Day = 0;
            Month += 1;
            if (Month > 11) { Month = 0; Year += 1; }
        }
        monthRolled = Month != month;
        yearRolled = Year != year;
        return true;
    }

    /// <summary>`dd/mm/yyyy`, which is the shape master's HUD shows.</summary>
    public string Format() => $"{DayOfMonth:00}/{MonthOfYear:00}/{DisplayYear:0000}";

    public void Set(int day, int month, int year)
    { Day = day; Month = month; Year = year; Accumulator = 0; }
}
