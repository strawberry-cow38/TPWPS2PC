namespace TPW.PS2.Data;

/// <summary>⭐⭐ THE CALENDAR'S DAILY DRIVER, `0x16B060`, READ (findings/staff-management.md §6.3, §9.2,
/// §11.4; awards.md "The calendar is the economy's spine"):
/// <code>
///   snapshot month, day, year; advance the clock (0x16B240)
///   MONTH changed:  0x16C120 strikes (gated by 0x151258)      -- ParkStaff.MonthlyStrikeCheck
///                   0x100A18 the park's month end: wages      -- ParkFinances.MonthEnd(ParkStaff.WagesDue)
///                   balance &gt;= 0 ? 0x100E88(park), counter 0 : the in-the-red chain (cal+0x18) -- RedMonths
///                   0x16B478(cal); cal+0x1C += 1; year changed → 0x100EF8(park)
///   DAY changed and day-of-month % 7 == 0:  0x16BC70, the weekly pass (days 1, 8, 15, 22, 29 --
///                   so the 1st of every month is also a weekly day)
/// </code>
/// ⭐ STRIKES FIRST, THEN WAGES: a strike that starts at this change is paid in full (nobody is standing
/// in 0xF yet); the month a strike covered is unpaid for those who reached 0xF.
///
/// ⭐ The in-the-red chain IS ported (advisor step A, READ decompile `0x16B0E8..0x16B194`): <see cref="RedMonths"/>.
/// ⭐ `0x16B478` (the visitor statistics, <see cref="ParkStatistics"/>) and `0x100EF8` (the year roll,
/// <see cref="ParkFinances.YearRoll"/>) are ported as of 2026-09-30, with the month end's park value.
/// ⚠ NOT PORTED, said so: `0x100E88` (untraced here) and every weekly test
/// but the Security Award's. ⚠ The loans `0x100A18` repays are not modelled (the port has none).
///
/// Core, so the audit drives it exactly as the viewer does: one <see cref="Advance"/> per frame.</summary>
public sealed class ParkManagement
{
    public ParkManagement(ParkClock clock, ParkAwards awards)
    {
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Awards = awards ?? throw new ArgumentNullException(nameof(awards));
    }

    public ParkClock Clock { get; }
    public ParkAwards Awards { get; }
    /// <summary>The park object's finances (`0x1005D8`): null → no month end runs.</summary>
    public ParkFinances Finances { get; set; }
    /// <summary>The park's staff: null → no strikes, wages 0, no Security Award (the award's coverage reads
    /// the placed-feature list through it).</summary>
    public ParkStaff Staff { get; set; }

    /// <summary>The calendar's advisor messages -- the award (0xA0) and the in-the-red chain (0xCE, 0x79, 0x7A,
    /// 0x7B) -- <see cref="ParkAdvisor.Submit"/>. ⚠ The staff's own (strikes, research) go through
    /// <see cref="ParkStaff.Advisor"/>.</summary>
    public Action<int> Advisor { get; set; }
    /// <summary>The award's UI sound 0xC5 (`0x1C38C0` → `0x111150(audio, 0, 0xC5, 0)`).</summary>
    public Action<int> UiSound { get; set; }
    /// <summary>⚠ ADAPTER for `0x16C0E8()` = `0x16C008(world, park)`: the park's goals record. The weekly
    /// pass returns at once without one. Default: present (every ordinary park the port loads).</summary>
    public Func<bool> GoalsRecordPresent { get; set; } = () => true;
    /// <summary>⚠ ADAPTER for `0x153410()` = `[0x2B72A8]` (test-park mode, which also zeroes ride wear):
    /// the weekly pass returns at once while it is set. Default: off.</summary>
    public Func<bool> TestPark { get; set; } = () => false;

    /// <summary>The visitor statistics `0x16AE90` -- null: nothing recorded.</summary>
    public ParkStatistics Stats { get; set; }
    /// <summary>⚠ ADAPTER for `0x16B478`'s walk of the guest list: (n, Σ happiness `g+0x75`, Σ days in park).</summary>
    public Func<(int People, int HappinessSum, int TimeSum)> VisitorSample { get; set; }
    /// <summary>⚠ ADAPTER for `0x153650`, the park rating (<see cref="AdvisorProducers.ParkRating"/>).</summary>
    public Func<int> Rating { get; set; }
    /// <summary>⚠ ADAPTER for `0x1011C8`, the park value in tenths (half the purchase price of everything placed).</summary>
    public Func<int> ParkValue { get; set; }

    /// <summary>`cal+0x1C`: months elapsed.</summary>
    public int MonthsElapsed { get; private set; }
    /// <summary>⭐ `cal+0x18`, the in-the-red counter: months ended with a negative balance in a row (0 again
    /// at any month end at or above zero).</summary>
    public int RedMonths { get; private set; }
    /// <summary>The chain's messages (`0x16B0E8..0x16B194`): 0xCE IN_THE_RED (voiced) then 0x79 at the first red
    /// month, 0x7A at the third, 0x7B BANKRUPTED (modal: its end is game over) at the fourth.</summary>
    public const int MessageInTheRed = 0xCE, MessageRedFirst = 0x79, MessageRedThird = 0x7A, MessageBankrupted = 0x7B;
    /// <summary>Instrumentation: month changes and weekly passes run.</summary>
    public int MonthChanges { get; private set; }
    public int WeeklyPasses { get; private set; }
    /// <summary>The last month end's wage bill, tenths.</summary>
    public int LastWages { get; private set; }

    /// <summary>⭐ `0x16B060`: advance the calendar by one frame's units and run what the rollovers
    /// trigger, in the console's order. Returns true when a day rolled.</summary>
    public bool Advance(int frameUnits)
    {
        int day = Clock.Day;
        bool rolled = Clock.Advance(frameUnits, out bool dayRolled, out bool monthRolled, out bool yearRolled);
        if (monthRolled) MonthChanged(yearRolled);
        if (dayRolled && Clock.Day != day && Clock.Day % 7 == 0) WeeklyPass();
        return rolled;
    }

    /// <summary>The month-change half of `0x16B060`: strikes, then the park's month end with the wage
    /// bill `0x1008B8`.</summary>
    public void MonthChanged(bool yearRolled = false)
    {
        MonthChanges++;
        Staff?.MonthlyStrikeCheck(Clock.Month);                           // 0x16C120
        if (Finances != null)
        {
            LastWages = Staff?.WagesDue() ?? 0;                           // 0x1008B8
            Finances.MonthEnd(LastWages, ParkValue?.Invoke() ?? 0);       // 0x100A18
            // 0x100688 (the balance): at or above zero → 0x100E88 (⚠ untraced) and cal+0x18 = 0; below →
            // cal+0x18 += 1 and at 1: 0xCE then 0x79; 3: 0x7A; 4: 0x7B; 2 and 5+: nothing.
            if (Finances.Balance >= 0) RedMonths = 0;
            else
            {
                RedMonths++;
                if (RedMonths == 1) { Advisor?.Invoke(MessageInTheRed); Advisor?.Invoke(MessageRedFirst); }
                else if (RedMonths == 3) Advisor?.Invoke(MessageRedThird);
                else if (RedMonths == 4) Advisor?.Invoke(MessageBankrupted);
            }
        }
        // 0x16B478, before the counter: the December sample is "year != 0 && month == 0" -- the clock has already
        // rolled into the new month here.
        if (Stats != null)
        {
            var (people, happy, time) = VisitorSample?.Invoke() ?? (0, 0, 0);
            Stats.Record(people, happy, time, Rating?.Invoke() ?? 0, Clock.Year != 0 && Clock.Month == 0);
        }
        MonthsElapsed++;                                                  // cal+0x1C
        if (yearRolled) Finances?.YearRoll();                             // 0x100EF8 at 0x16B1BC
    }

    /// <summary>⭐ `0x16BC70`, the weekly pass -- ONLY its Security Award test is ported (READ):
    /// <code>
    ///   no goals record (0x16C0E8) or test park (0x153410): return
    ///   ... the visitor/profit/business/placement tests (⚠ not ported)
    ///   bit 0 of cal+0x28 clear and 0x104CE0(0x40) &gt; 0x50:
    ///       0x16BB38(cal, 0xA0) = 0x1C38C0(1) (a gold ticket, UI sound 0xC5) then message 0xA0; set bit 0
    ///   ... the other four hidden awards (⚠ not ported)
    /// </code>
    /// ⭐ `0x104CE0` is 8× the covered fraction of 16×16-cell blocks (<see cref="ParkStaff.FeatureCoverage"/>),
    /// so on a 64×76 park cameras in 5 distinct blocks win it.</summary>
    public void WeeklyPass()
    {
        WeeklyPasses++;
        if (!GoalsRecordPresent() || TestPark()) return;
        if (Staff == null) return;                                        // ⚠ no placed-object list to read
        if (!Awards.HasHiddenAward(StaffTables.SecurityAwardBit)
            && Staff.FeatureCoverage(0x40) > StaffTables.SecurityAwardCoverageAbove)
        {
            Awards.AwardGoldTickets(1);                                   // 0x1C38C0(1)
            UiSound?.Invoke(StaffTables.UiSoundGoldTicket);
            Advisor?.Invoke(StaffTables.SecurityAwardMessage);            // 0x107CA8(msg, 0xA0)
            Awards.GrantHiddenAward(StaffTables.SecurityAwardBit);        // 0x16B918(cal, 0, 1)
        }
    }
}
