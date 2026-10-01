using System.Collections.Generic;
using System.Linq;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>⭐ The park's statistics, as the laptop's Park Statistics and Finance pages read them (strawberry,
/// 2026-09-30: "flesh out a bunch of statistics, etc in the laptop uis for park statistics, financial etc").
/// The recording is core's <see cref="ParkStatistics"/> and <see cref="ParkFinances"/>; this is what feeds them
/// from the live park.</summary>
public partial class Viewer
{
    /// <summary>The visitor statistics `0x16AE90` for THIS park (dropped with the sim on a park load).</summary>
    ParkStatistics _parkStats;

    /// <summary>`0x16B478`'s walk of the guest list: n, Σ happiness `g+0x75` (signed, as the console sums `char`s),
    /// Σ days in park `0x211D48`.</summary>
    (int People, int HappinessSum, int TimeSum) VisitorSample()
    {
        var needs = _visitors?.Needs;
        if (_visitors == null || needs == null) return (0, 0, 0);
        int n = 0, happy = 0, time = 0;
        foreach (int id in _visitors.Plans.Keys)
        {
            if (!needs.Has(id)) continue;
            n++;
            happy += unchecked((sbyte)needs.Of(id).Happiness);
            time += _parkStats?.DaysInPark(id, _calendar.TotalDays) ?? 0;
        }
        return (n, happy, time);
    }

    /// <summary>`0x153650`, the park rating, from the same census the advisor reads.</summary>
    int ParkRatingNow() => new AdvisorProducers(_calendar, _sim, _staff, _visitors) { Placements = AdvisorPlacements }.ParkRating();

    /// <summary>⭐ Visitor Information's dominant thoughts (`0x184618`, parkstats-screens.md §3.5): bucket every guest by
    /// `0x211C80`, take the three largest of buckets 1..8 (earliest on ties, class 0 never), then the console's own
    /// fill-in -- 2nd := 1st if unset; if the 3rd is unset, 3rd := 2nd and 2nd := 1st. So a park thinking only one
    /// thing shows that icon three times. Null with nobody in classes 1..8: no row.</summary>
    List<int> DominantThoughts()
    {
        var needs = _visitors?.Needs;
        if (_guests == null || needs == null) return null;
        var buckets = new int[9];
        foreach (var g in _guests.Guests)
            if (needs.Has(g.Id)) buckets[AdvisorProducers.NeedClass(needs.Of(g.Id))]++;
        var top = new int[] { -1, -1, -1 };
        for (int pass = 0; pass < 3; pass++)
        {
            int best = -1;
            for (int c = 1; c <= 8; c++) if (buckets[c] > 0 && (best < 0 || buckets[c] > buckets[best])) best = c;
            if (best < 0) break;
            top[pass] = best; buckets[best] = 0;
        }
        if (top[0] < 0) return null;
        if (top[1] < 0) top[1] = top[0];
        if (top[2] < 0) { top[2] = top[1]; top[1] = top[0]; }
        return top.ToList();
    }

    /// <summary>⭐ `--stats-demo` (with `--wind-months=N`): A CONTROL WITH A KNOWN ANSWER. A harness park has no guests,
    /// rides or money moving, so every statistic winds to 0 -- and a screen of zeros looks the same whether the
    /// recording works or not. This swaps the month end's three inputs for a feed whose every value is
    /// predictable from the month index m: people 10+5m, happiness 40+3m %, time 2+m days, rating 10+4m (Good
    /// in December at m=12, Excellent at m=13 -- so this year and last year read DIFFERENT words), park value
    /// $5,000+$1,000m; and each day $200 in, $120 out and every third day one admission. The thoughts row has no
    /// guest to read, so it shows <see cref="DemoThoughts"/> -- that part is a drawing check only.</summary>
    bool _statsDemo;
    static readonly List<int> DemoThoughts = new() { 6, 2, 3 };

    void StartStatsDemo()
    {
        _parkStats ??= new ParkStatistics();
        _management.VisitorSample = () =>
        {
            int m = _parkStats.Months, n = 10 + 5 * m;
            return (n, n * (40 + 3 * m), n * (2 + m));
        };
        _management.Rating = () => 10 + 4 * _parkStats.Months;
        _management.ParkValue = () => (5000 + 1000 * _parkStats.Months) * 10;
        GD.Print("[stats] --stats-demo: people 10+5m, happiness 40+3m, time 2+m, rating 10+4m, value $5,000+$1,000m");
    }

    void StatsDemoDay(int day)
    {
        _sim?.Finances.Credit(2000);
        _sim?.Finances.Debit(1200);
        if (day % 3 == 0) _parkStats.Admitted(1_000_000 + day, _calendar.TotalDays);
    }

    /// <summary>`0x142B68`: a decimal with thousands commas, no `$`.</summary>
    static string Thousands(int v) => v.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);
}
