namespace TPW.PS2.Data;

/// <summary>⭐⭐ THE PARK'S TILL — where a guest's money actually goes, which until now was
/// nowhere. `VisitorNeeds.Buy` debited the guest and the amount simply vanished, so a park could
/// not be run at a profit or a loss and "making this into an actual game" had no economy in it.
///
/// Read from the park object (`FUN_001005D8` returns the singleton, `0x12F4` bytes):
///
/// <code>
///   FUN_00100750(park, amount)          // CREDIT
///       park[4]      += amount          // the balance
///       park[0x12d8] += amount          // income, running total
///       park[0x12dc] += amount          // income, this period
///       park[0x2fc + (period % 0x90)*4] += amount     // 144-slot income graph
///       if (balance &gt;= 0) clear the overdrawn warning
///
///   FUN_00100698(park, amount)          // DEBIT, and it can REFUSE
///       if (park[8] == 0 &amp;&amp; park[4] &lt; amount) return 0;    // cannot afford
///       park[4]      -= amount
///       park[0x12e4] += amount;  park[0x12d4] += amount
///       park[0xbfc + (period % 0x90)*4] += amount     // 144-slot spending graph
///
///   FUN_001007D8(park, category, amount)    // credit, then FILE IT
///       FUN_00100750(park, amount);   then per-category ring + total (4 and 5 are switched)
/// </code>
///
/// ⭐ `park[8]` is a "spend anything" flag -- with it set the debit never refuses -- which is how
/// a sandbox or a scripted scenario would be done. Modelled as <see cref="Unlimited"/>.
///
/// ⚠ THE STARTING BALANCE IS STILL NOT READ -- see <see cref="OpeningBalance"/>, which carries
/// master's $30,000 explicitly labelled as theirs rather than as a decode.
///
/// ⭐ THE PERIOD COUNTER `park[0x12bc]` IS READ NOW: the month end `0x100A18` advances it (below,
/// <see cref="MonthEnd"/>), so the two rings the advisor's WAGES_HIGH reads are kept -- income
/// `+0x2FC` and wages `+0xE3C` (findings/staff-management.md §6.3-§6.4). ⚠ ONLY THOSE TWO: the
/// balance `+0xBC`, spending `+0xBFC`, value `+0x107C` and the `+0x53C/+0x77C/+0x9BC` rings are
/// written natively too, and nothing the port runs reads them yet, so they are not kept.</summary>
public sealed class ParkFinances
{
    /// <summary>`park[4]`. ⚠ May go negative: the console credits without a floor and only uses
    /// the sign to raise or clear a warning.</summary>
    public int Balance { get; set; } = OpeningBalance;

    /// <summary>⭐⭐ **$30,000**, in this class's tenths. Master: "you are meant to start with
    /// $30,000."
    ///
    /// ⚠⚠ MASTER'S NUMBER, NOT A DECODED CONSTANT, and the difference is recorded rather than
    /// blurred. A search of the image for 300000 and 30000 as a composed immediate finds seven
    /// hits and every one is a graph clamp inside the statistics dispatcher `FUN_0010DE38`
    /// (`if (30000 &lt; v) v = 30000`) -- no code path seeds the balance. That agrees with
    /// `FUN_00100470`, the park constructor, which zeroes every running total and sets the
    /// spend-anything flag but never writes `park[4]`: the opening figure arrives with the
    /// scenario, whose loader is untraced.
    ///
    /// ⭐ So this is the right VALUE from someone who knows the game, sitting where the scenario
    /// loader's result belongs -- and it is one line to delete when that loader is read.</summary>
    public const int OpeningBalance = 300_000;

    /// <summary>`park[8]`. When set, <see cref="Debit"/> never refuses for want of money.
    ///
    /// ⭐⭐ DEFAULTS TRUE, AND THAT IS READ: the park constructor `FUN_00100470` writes
    /// `park[8] = 1` along with zeroing every running total. So a park object that nothing has
    /// loaded into spends freely -- which is exactly the state this port is in until a scenario
    /// sets a budget. ⚠ What clears it is the scenario/save load, which is not traced; when that
    /// lands it sets this and <see cref="Balance"/> together.
    ///
    /// ⚠ Deliberately NOT defaulted false "to be safe": false would refuse every purchase in a
    /// park with no starting money, which is a behaviour nothing in the executable asks for.</summary>
    public bool Unlimited { get; set; } = true;

    /// <summary>`park[0x12d8]` -- the LIFETIME income, unaffected by the period counter this port
    /// does not model. This is the figure the Balance Sheet's "Cash In" reads.</summary>
    public int TotalIncome { get; private set; }

    /// <summary>⚠⚠ MISLABELLED UNTIL 2026-09-28: this is `park[0x12e4]`, which is the spend SINCE
    /// THE YEAR ROLL, not a lifetime total. The lifetime spend is `park[0x12d4]`, and the Balance
    /// Sheet's "Cash Out" reads THAT one. `FUN_00100698` bumps both on every debit, so they are
    /// numerically identical in this port -- which has no year roll to separate them -- and the
    /// error is invisible today and would appear the moment one is added.
    ///
    /// ⚠ Park Finance wants `0x12e4`'s real meaning (this year) alongside `0x12e8` (last year),
    /// so when the year roll lands these need to become two different numbers rather than one.</summary>
    public int TotalSpending { get; private set; }

    /// <summary>⭐ Income filed by category, as `FUN_001007D8` does. The console switches on two
    /// (4 and 5) and files everything else under the plain credit only; the dictionary keeps
    /// whatever it is handed rather than hard-coding that pair, because which number means what
    /// is not yet read and a switch that silently drops a category is worse than a dictionary
    /// that records an unexpected one.</summary>
    public IReadOnlyDictionary<int, int> IncomeByCategory => _byCategory;
    readonly Dictionary<int, int> _byCategory = new();

    /// <summary>`FUN_00100750`. ⚠ A negative amount is NOT a debit -- the console has a separate
    /// function for that which can refuse -- so this rejects one rather than quietly running the
    /// balance down through the wrong door.</summary>
    public void Credit(int amount, int? category = null)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "a credit is not a negative debit -- call Debit");
        Balance += amount;
        TotalIncome += amount;
        _income[Slot(PeriodCount)] += amount;                              // park[0x2FC + (period % 0x90)*4]
        if (category is { } c) _byCategory[c] = _byCategory.GetValueOrDefault(c) + amount;
    }

    /// <summary>`FUN_00100698`. <returns>false when the park cannot afford it and nothing was
    /// taken -- the console returns 0 there and the caller is expected to abandon the
    /// purchase.</returns> ⭐ In a free-build park (<see cref="FreeBuild"/>) it returns 1 and takes
    /// NOTHING (the first test in `0x100698`).</summary>
    public bool Debit(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "a debit is not a negative credit -- call Credit");
        if (FreeBuild) return true;
        if (!Unlimited && Balance < amount) return false;
        Balance -= amount;
        TotalSpending += amount;
        return true;
    }

    // --------------------------------------------------------------------------------------------
    // The month end and the staff wages (findings/staff-management.md §3, §6.3, §6.4).

    /// <summary>⚠ `DAT_002A60B8 || 0x154428()` (park index 2): the free-build modes, in which
    /// `0x100698` debits nothing and training costs 0 (`0x1DC2A8`). The port has no free-build park,
    /// so this defaults false and nothing sets it; it is here because the rules that read it are.</summary>
    public bool FreeBuild { get; set; }

    /// <summary>The rings are 144 slots (`% 0x90`).</summary>
    public const int PeriodSlots = 0x90;
    readonly int[] _income = new int[PeriodSlots];
    readonly int[] _wages = new int[PeriodSlots];
    // ⭐ The takings rings the finance graphs read (getters `0x101010`, `0x101068`, `0x1010C0`, the same
    // shape as the income getter `0x100FB8`): gate `park+0x53C`, shop `+0x77C`, sideshow `+0x9BC`.
    readonly int[] _gate = new int[PeriodSlots];
    readonly int[] _shop = new int[PeriodSlots];
    readonly int[] _sideshow = new int[PeriodSlots];

    /// <summary>`park+0x12C8`: every admission ever taken (`0x100D28`).</summary>
    public int GateTotal { get; private set; }
    /// <summary>`park+0x12CC`: every shop credit ever filed (`0x1007D8`, kind 4).</summary>
    public int ShopTotal { get; private set; }
    /// <summary>`park+0x12C4`: every sideshow credit ever filed (`0x1007D8`, kind 5).</summary>
    public int SideshowTotal { get; private set; }
    /// <summary>⭐ The bank balance AT EACH MONTH END -- the console's `park+0xbc`, which Overall
    /// Statistics plots as its Bank Balance series. Filed by <see cref="MonthEnd"/> alongside the
    /// wages, in the same slot.</summary>
    readonly int[] _balance = new int[PeriodSlots];
    static int Slot(int period) => ((period % PeriodSlots) + PeriodSlots) % PeriodSlots;

    /// <summary>`park[0x12BC]`: completed months. The month end `0x100A18` bumps it after filing
    /// the month (and clears the next slot of every ring).</summary>
    public int PeriodCount { get; private set; }

    /// <summary>`park[0x12D0]`: every wage ever paid, the month ends' AND the firings' (`0x100C78`
    /// adds before it debits). ⚠ No reader of this accumulator is ported (nor traced).</summary>
    public int WageAccumulator { get; private set; }

    /// <summary>`0x100C78(park, amount)`: the wage paid on FIRING -- `park[0x12D0] += amount`, then the
    /// debit `0x100698` with the same amount (MIPS `0x100C84..0x100C90` keeps `a1`). Not filed in the
    /// month's wage ring: only the month end writes that.</summary>
    public bool PayWage(int amount)
    {
        WageAccumulator += amount;
        return Debit(amount);
    }

    /// <summary>⭐ `0x100A18`, the park's month end, READ (MIPS `0x100A18..0x100B30`), called by the
    /// calendar `0x16B060` AFTER the strike check:
    /// <code>
    ///   balanceHistory[i] = balance                                  (⚠ not kept)
    ///   4 loan slots: repayment min(+0x14, +0x18) x 10 into `loans`  (⚠ the port has no loans: 0)
    ///   park[0x12D0] += W;  wageRing[i] = W                          (W = 0x1008B8, the staff's wages x 10)
    ///   debit(loans + W)      -- ONE call; it refuses only as Debit does, and the ring keeps W anyway
    ///   credit(0)             -- 0x100750(park, 0): files nothing, clears the in-the-red count if >= 0
    ///   period += 1; the next slot of every ring cleared
    /// </code>
    /// `i = period % 144`. <paramref name="wagesTenths"/> is `0x1008B8`'s sum, already x 10.</summary>
    public void MonthEnd(int wagesTenths)
    {
        int loans = 0;                                                     // ⚠ no loan slots in the port
        WageAccumulator += wagesTenths;
        _wages[Slot(PeriodCount)] = wagesTenths;
        Debit(loans + wagesTenths);
        Credit(0);
        // ⭐ The balance is filed for the month that just CLOSED, so it is recorded before the
        // counter moves on -- the same slot the wages above went into.
        _balance[Slot(PeriodCount)] = Balance;
        PeriodCount += 1;
        _income[Slot(PeriodCount)] = 0;
        _wages[Slot(PeriodCount)] = 0;
        _gate[Slot(PeriodCount)] = 0;
        _shop[Slot(PeriodCount)] = 0;
        _sideshow[Slot(PeriodCount)] = 0;
    }

    /// <summary>`0x100F68(park, k)`: the ring slot of the k-th completed month back, or -1 when
    /// `k &gt;= period count`; k = 0 is treated as 1 (`movz`). READ, MIPS `0x100F68..0x100FB4`.</summary>
    int PeriodIndex(int k)
    {
        if (!(k < PeriodCount)) return -1;
        int back = k == 0 ? 1 : k;
        int i = PeriodCount % PeriodSlots - back;
        while (i < 0) i += PeriodSlots;
        return i;
    }

    /// <summary>`0x100FB8(park, k)`: income of the k-th completed month back (0 before there was one).</summary>
    public int IncomeInPeriod(int k) => PeriodIndex(k) is var i && i < 0 ? 0 : _income[i];
    /// <summary>`0x101170(park, k)`: wages of the k-th completed month back (0 before there was one).</summary>
    public int WagesInPeriod(int k) => PeriodIndex(k) is var i && i < 0 ? 0 : _wages[i];
    /// <summary>`0x101010(park, k)`: gate takings of the k-th completed month back.</summary>
    public int GateInPeriod(int k) => PeriodIndex(k) is var i && i < 0 ? 0 : _gate[i];
    /// <summary>`0x101068(park, k)`: shop takings of the k-th completed month back.</summary>
    public int ShopInPeriod(int k) => PeriodIndex(k) is var i && i < 0 ? 0 : _shop[i];
    /// <summary>`0x1010C0(park, k)`: sideshow takings of the k-th completed month back.</summary>
    public int SideshowInPeriod(int k) => PeriodIndex(k) is var i && i < 0 ? 0 : _sideshow[i];

    /// <summary>⭐ `0x100D28`, an admission, READ (MIPS `0x100D28..0x100DA0`): `0x100750` credits the fee,
    /// then `park+0x12C8 += fee` and the gate ring `park+0x53C[period % 144] += fee`. The console reads the
    /// fee itself (`0x100D20`, `park+0`); the port's caller passes the amount it charged.</summary>
    public void CreditAdmission(int fee)
    {
        Credit(fee);
        GateTotal += fee;
        _gate[Slot(PeriodCount)] += fee;
    }

    /// <summary>⭐ `0x1007D8(park, kind, amount)`, READ (MIPS `0x1007D8..0x1008B4`, jump table `0x353AE0`):
    /// `0x100750` credits the amount, then kind 4 (Shop) files it in `park+0x77C[period]` and `+0x12CC`, kind
    /// 5 (Sideshow) in `park+0x9BC[period]` and `+0x12C4`; the other twelve kinds file nothing. The kind is
    /// the object's `vt+0xA0` at both call sites (`0x1D194C`, `0x1D25DC`). ⚠ INFERRED that `vt+0xA0` is the
    /// DBA kind (4 Shop, 5 Sideshow = the port's AssetKind); cow tools' graph research names the same rings.</summary>
    public void CreditByKind(int kind, int amount)
    {
        Credit(amount);
        if (kind == 4) { _shop[Slot(PeriodCount)] += amount; ShopTotal += amount; }
        else if (kind == 5) { _sideshow[Slot(PeriodCount)] += amount; SideshowTotal += amount; }
    }

    /// <summary>`0x100DA8(park, k)`: the bank balance of the k-th completed month back.
    ///
    /// ⚠ IT DOES NOT BEHAVE LIKE THE ACCUMULATOR GETTERS. For `k = 0` the console answers the
    /// LIVE balance (`park+4`) rather than a ring slot, which is why this is not just another
    /// `PeriodIndex` call. That difference is also why the graph's leftmost-bucket quirk (always
    /// zero for Money In and Wages) does NOT apply to this series.</summary>
    public int BalanceInPeriod(int k) =>
        k == 0 ? Balance : PeriodIndex(k) is var i && i < 0 ? 0 : _balance[i];

    /// <summary>⭐ Advisor variable 49, the WAGES_HIGH producer (MIPS `0x10E32C..0x10E3D8`):
    /// `income(1)/10 &lt; wages(1)/10 &amp;&amp; income(2)/10 &lt; wages(2)/10` -- wages above ALL income in
    /// each of the last two completed months. Signed integer divisions.</summary>
    public bool WagesHigh =>
        IncomeInPeriod(1) / 10 < WagesInPeriod(1) / 10 && IncomeInPeriod(2) / 10 < WagesInPeriod(2) / 10;
}
