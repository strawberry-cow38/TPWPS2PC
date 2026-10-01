namespace TPW.PS2.Data;

/// <summary>⭐⭐ THE VISITOR STATISTICS -- the console's stats singleton `0x16AE90` (`DAT_002B9728`, 0x32C bytes), the
/// part of it Park Statistics reads. It is the calendar object too: its month counter `+0x1C` is the one
/// <see cref="ParkManagement.MonthsElapsed"/> mirrors.
///
/// ⭐ READ, the month-end recorder `0x16B478(stats)`, called by the calendar `0x16B060` at a month change, BEFORE
/// `+0x1C` increments (findings/graph-widget.md §1.5, decompiled 2026-09-30):
/// <code>
///   rating = 0x153650()                                   (the park rating, AdvisorProducers.ParkRating)
///   walk the guests: n++, happy += (s8)g+0x75, time += 0x211D48(g)  (= day counter - g+0x70, the arrival day)
///   i = +0x1C % 144
///   if (year != 0 &amp;&amp; month == 0) +0x2FC = rating                (the December sample: "last year")
///   people[i]  (+0x2C)  = n
///   arrival[i] (+0xBC)  = max(0, n - people(1))            (people(1) is the PREVIOUS month: i is not yet counted)
///   happy[i]   (+0x14C) = n ? happy / n : 0
///   time[i]    (+0x1DC) = n ? time / n : 0
///   rating[i]  (+0x26C) = rating
/// </code>
/// The rings are BYTES (`sb`), read back signed -- Time In Park's headroom is `max(|peak|)`, which only means
/// something for a signed series. People Visited is `+0x20`, bumped once per guest admitted at the gate
/// (`0x210C98`: the price is under the guest's cash and a route exists).</summary>
public sealed class ParkStatistics
{
    public const int Slots = 0x90;
    readonly sbyte[] _people = new sbyte[Slots], _arrival = new sbyte[Slots], _happiness = new sbyte[Slots],
                     _time = new sbyte[Slots], _rating = new sbyte[Slots];
    readonly Dictionary<int, int> _arrivedDay = new();

    /// <summary>`+0x1C`: months recorded.</summary>
    public int Months { get; private set; }
    /// <summary>`+0x20`: guests ever admitted at the gate.</summary>
    public int PeopleVisited { get; private set; }
    /// <summary>`+0x2FC`: the rating sampled as the calendar rolled into month 0 of a year after the first; 0 before.</summary>
    public int LastYearRating { get; private set; }

    /// <summary>`0x210C98`'s `+0x20` increment, and the arrival stamp `g+0x70` (the stats day counter).</summary>
    public void Admitted(int guest, int day)
    {
        PeopleVisited++;
        _arrivedDay[guest] = day;
    }

    /// <summary>`0x211D48(g)`: days since the guest arrived. A guest the port never stamped (spawned by a harness
    /// or a cheat, which the console has no route for) reads 0.</summary>
    public int DaysInPark(int guest, int today) => _arrivedDay.TryGetValue(guest, out int d) ? today - d : 0;

    /// <summary>`0x16B478`, given the walk's three sums and the rating; then the month is counted.</summary>
    public void Record(int people, int happinessSum, int timeSum, int rating, bool decemberSample)
    {
        int i = Months % Slots;
        if (decemberSample) LastYearRating = (sbyte)rating;
        _people[i] = (sbyte)people;
        _arrival[i] = (sbyte)Math.Max(0, people - People(1));
        _happiness[i] = (sbyte)(people == 0 ? 0 : happinessSum / people);
        _time[i] = (sbyte)(people == 0 ? 0 : timeSum / people);
        _rating[i] = (sbyte)rating;
        Months++;
    }

    /// <summary>`0x16B2E8(stats, k)`: the ring slot of the k-th recorded month back, -1 when k &gt;= months; k = 0
    /// reads as 1 -- so "now" on Park Statistics is LAST month-end's recording, not the live park.</summary>
    int Index(int k)
    {
        if (!(k < Months)) return -1;
        int back = k == 0 ? 1 : k;
        int i = Months % Slots - back;
        while (i < 0) i += Slots;
        return i;
    }

    int At(sbyte[] ring, int k) => Index(k) is var i && i < 0 ? 0 : ring[i];
    /// <summary>`0x16B338`, `0x16B378`, `0x16B3B8`, `0x16B3F8`, `0x16B438`.</summary>
    public int People(int k) => At(_people, k);
    public int Arrival(int k) => At(_arrival, k);
    public int Happiness(int k) => At(_happiness, k);
    public int TimeInPark(int k) => At(_time, k);
    public int Rating(int k) => At(_rating, k);

    /// <summary>Park Finance's rating word: `DAT_002C42C0[min(3, rating / 20)]` -- 57 Poor, 767 Average, 113 Good,
    /// 1004 Excellent (parkstats-screens.md §4.2).</summary>
    public static int RatingTextId(int rating) => new[] { 57, 767, 113, 1004 }[Math.Clamp(rating / 20, 0, 3)];
}
