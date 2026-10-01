using System.Buffers.Binary;
using TPW.PS2.Data;

/// <summary>⭐ THE LAPTOP'S STATISTICS (strawberry, 2026-09-30: "flesh out a bunch of statistics, etc in the laptop
/// uis for park statistics, financial etc"). The core half: the year roll `0x100EF8` and the last-year snapshots,
/// the visitor-statistics recorder `0x16B478`, the park rating `0x153650`, Park Statistics' bucket walk
/// `0x186D38`, and the calendar driving all of it. Each claim carries the control that would read differently if
/// the rule were the obvious one instead of the read one.</summary>
static class ParkStatsChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "parkstats: " + label);
        var exe = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        Executable(disc.Read(exe.Extent, exe.Size), Check);
        YearRoll(Check);
        Recorder(Check);
        Rating(Check);
        Buckets(Check);
        Calendar(Check);
    }

    static void Executable(byte[] elf, Action<bool, string> Check)
    {
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off, 4));
        int U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(off, 2));
        uint At(uint va)
        {
            int ph = checked((int)U32(28));
            for (int i = 0; i < U16(44); i++)
            {
                int p = ph + i * U16(42);
                if (U32(p) == 1 && va >= U32(p + 8) && va < U32(p + 8) + U32(p + 16)) return U32(checked((int)(U32(p + 4) + va - U32(p + 8))));
            }
            throw new InvalidDataException($"0x{va:x} is not file-backed");
        }
        // lw v1,0x12dc / lw v0,0x12e4 / sw v1,0x12e0 / sw v0,0x12e8 / sw zero,0x12dc / sw zero,0x12e4 (delay slot)
        Check(At(0x100ef8) == 0x8c8312dc && At(0x100f00) == 0x8c8212e4 && At(0x100f08) == 0xac8312e0
              && At(0x100f0c) == 0xac8212e8 && At(0x100f18) == 0xac8012dc && At(0x100f20) == 0xac8012e4,
              "the year roll 0x100EF8 copies 0x12dc -> 0x12e0 and 0x12e4 -> 0x12e8, then zeroes both");
        // ⭐ Park Statistics' bucket loop ASSIGNS: `jal 0x16B378` with `sw v0,0(t0)` in its delay slot stores the
        // People getter's result straight into the bucket, and t0 is reloaded from the stack, not read through.
        // A summing loop would `lw` the bucket and `addu` before the store. This is the instruction behind the
        // half-height staircase every Park Statistics graph draws (one-year span: every second point is one month
        // halved, and the plotter takes the lower point of each rise) -- so it is pinned here, not taken on trust.
        Check(At(0x186e9c) == 0x8fa80008 && At(0x186ea8) == 0x0c05acde && At(0x186eac) == 0xad020000
              && At(0x186e90) == 0x0c05acce && At(0x186e98) == 0x26310001,
              "Park Statistics' walk 0x186D38 stores each getter's result into the bucket (0x186EAC sw v0,0(t0) in the "
              + "delay slot of jal 0x16B378) and only counts (s1++): it assigns, it does not sum");
    }

    static void YearRoll(Action<bool, string> Check)
    {
        var f = new ParkFinances();
        f.Credit(500); f.Debit(200);
        bool before = f.YearIncome == 500 && f.YearSpending == 200 && f.TotalIncome == 500 && f.TotalSpending == 200;
        f.YearRoll();
        f.Credit(70);
        Check(before && f.LastYearIncome == 500 && f.LastYearSpending == 200 && f.YearIncome == 70 && f.YearSpending == 0
              && f.TotalIncome == 570 && f.TotalSpending == 200,
              $"money in/out: this year {f.YearIncome}/{f.YearSpending} after the roll, last year {f.LastYearIncome}/{f.LastYearSpending}; "
              + $"the lifetime Cash In/Out ({f.TotalIncome}/{f.TotalSpending}) do not roll -- the control that they are different fields");
        // The snapshot lands when the period count is 12 BEFORE it increments: the 13th month end.
        var g = new ParkFinances();
        int at = -1;
        for (int m = 1; m <= 26 && at < 0; m++)
        {
            g.MonthEnd(0, parkValueTenths: m * 100);
            if (g.LastYearParkValue != 0) at = m;
        }
        Check(at == 13 && g.LastYearParkValue == 1300 && g.ValueInPeriod(1) == 1300,
              $"the last-year park value snapshot lands at month end {at} (count % 12 == 0 && != 0, before the increment), "
              + $"and the value ring holds it ({g.ValueInPeriod(1)}) -- one month after a calendar year");
    }

    static void Recorder(Action<bool, string> Check)
    {
        var s = new ParkStatistics();
        Check(s.People(0) == 0 && s.Rating(0) == 0, "before the first month end every series reads 0 (the getter's k >= count)");
        s.Record(people: 10, happinessSum: 600, timeSum: 30, rating: 45, decemberSample: false);
        s.Record(people: 14, happinessSum: 700, timeSum: 56, rating: 48, decemberSample: false);
        s.Record(people: 4, happinessSum: 300, timeSum: 40, rating: 50, decemberSample: true);
        // ⚠ The getter answers k only while k < months (0x16B2E8: `sltu a1, count`), so when the SECOND month is
        // recorded people(1) is still out of range and its arrival is the whole headcount -- a retail off-by-one,
        // kept. From the third on, arrival is the growth over last month, clamped at 0.
        Check(s.People(2) == 14 && s.People(1) == 4 && s.Arrival(2) == 14 && s.Arrival(1) == 0 && s.Happiness(1) == 75
              && s.TimeInPark(1) == 10 && s.Rating(1) == 50 && s.LastYearRating == 50,
              $"three month ends: people 10, {s.People(2)}, {s.People(1)}; arrivals {s.Arrival(2)} (the 2nd month's whole headcount -- "
              + $"the getter's k < count) then {s.Arrival(1)} (the park shrank: clamped), happiness {s.Happiness(1)}% (300/4), "
              + $"time {s.TimeInPark(1)}d, December rating {s.LastYearRating}");
        Check(s.People(0) == s.People(1),
              "k = 0 reads as k = 1: Park Statistics' 'now' is LAST month-end's recording, not the live park");
        var d = new ParkStatistics();
        d.Admitted(7, day: 20); d.Admitted(8, day: 25);
        Check(d.PeopleVisited == 2 && d.DaysInPark(7, 30) == 10 && d.DaysInPark(99, 30) == 0,
              $"People Visited counts admissions ({d.PeopleVisited}); days in park is today minus the arrival stamp ({d.DaysInPark(7, 30)})");
    }

    static void Rating(Action<bool, string> Check)
    {
        var clock = new ParkClock();
        AdvisorProducers With(params AdvisorPlacement[] p) => new(clock) { Placements = () => p };
        AdvisorPlacement Of(AssetResourceDatabase.AssetKind k, int tier = 0) =>
            new(k, 1, 0, k is AssetResourceDatabase.AssetKind.Ride ? new ParkRide { CurrentTier = tier } : null);
        var census = new List<AdvisorPlacement>
        {
            Of(AssetResourceDatabase.AssetKind.Ride, 2), Of(AssetResourceDatabase.AssetKind.Ride), Of(AssetResourceDatabase.AssetKind.Ride),
        };
        for (int i = 0; i < 6; i++) census.Add(Of(AssetResourceDatabase.AssetKind.Shop));
        census.Add(Of(AssetResourceDatabase.AssetKind.Sideshow));
        for (int i = 0; i < 12; i++) census.Add(Of(AssetResourceDatabase.AssetKind.Feature));
        int r = With(census.ToArray()).ParkRating();
        // rides 3*3/2 = 4, tier > 1: 1, shops min(12, 10) = 10, sideshows 2, features min(12, 10) = 10 -> 27
        Check(r == 27, $"the rating of 3 rides (one at tier 2), 6 shops, 1 sideshow, 12 features and nobody: {r} = 4 + 1 + 10 + 2 + 10");
        var big = Enumerable.Range(0, 30).Select(_ => Of(AssetResourceDatabase.AssetKind.Ride, 3)).ToArray();
        int rb = With(big).ParkRating();
        Check(rb == 30, $"the caps: 30 rides all at tier 3 rate {rb} = 20 + 10, not 45 + 30 -- the control that the terms are capped");
    }

    static void Buckets(Action<bool, string> Check)
    {
        // Months recorded 24; the getter answers the month index itself so a bucket's source is visible.
        var b = LaptopGraphData.BuildParkStats(k => k, months: 24, years: 1, series: 0, out int max);
        var sum = LaptopGraphData.Build(k => k * 10, months: 24, years: 1, out _);
        // Bucket 2 is the first to span two months (12 and 11): assigned, it keeps 11 and halves it; summed, (12 + 11) / 2.
        Check(max == 110 && b[2] == 5 && sum[2] == 11,
              $"Park Statistics' walk assigns rather than sums: the first two-month bucket is {b[2]} where the finance builder makes {sum[2]}; People's max is the fixed {max}");
    }

    static void Calendar(Action<bool, string> Check)
    {
        var clock = new ParkClock();
        var fin = new ParkFinances();
        var stats = new ParkStatistics();
        var m = new ParkManagement(clock, new ParkAwards())
        {
            Finances = fin, Stats = stats,
            VisitorSample = () => (5, 250, 15), Rating = () => 63, ParkValue = () => 4000,
        };
        fin.Credit(900);
        int ends = 0, guard = 0;
        while (clock.Year == 0 && guard++ < 10_000_000) { m.Advance(1000); }
        ends = stats.Months;
        Check(stats.Months >= 12 && stats.People(1) == 5 && stats.Happiness(1) == 50 && stats.TimeInPark(1) == 3
              && stats.Rating(1) == 63 && stats.LastYearRating == 63 && fin.LastYearIncome == 900 && fin.YearIncome == 0,
              $"a year of the calendar: {ends} month ends recorded (people {stats.People(1)}, happiness {stats.Happiness(1)}%, "
              + $"rating {stats.Rating(1)}), the December sample {stats.LastYearRating}, and the year roll moved {fin.LastYearIncome} to last year");
    }
}
