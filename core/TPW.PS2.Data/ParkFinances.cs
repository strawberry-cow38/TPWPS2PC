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
/// `+0x2FC` and wages `+0xE3C` (findings/staff-management.md §6.3-§6.4), and since 2026-09-30 the balance
/// `+0xBC`, value `+0x107C` and takings `+0x53C/+0x77C/+0x9BC` rings the laptop's graphs read. ⚠ The spending ring
/// `+0xBFC` is still not kept: nothing reads it. The loan records are <see cref="Loans"/> (findings/loans.md).</summary>
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

    /// <summary>`park[0x12d4]` -- the LIFETIME spend, the Balance Sheet's "Cash Out". `FUN_00100698` bumps it
    /// and <see cref="YearSpending"/> (`0x12e4`) on every debit; the year roll `0x100EF8` zeroes only the
    /// latter, which is what separates them. (Until 2026-09-30 this property WAS the one field, labelled
    /// `0x12e4`, because the port had no year roll.)</summary>
    public int TotalSpending { get; private set; }

    /// <summary>`park[0x12dc]` -- money in SINCE THE YEAR ROLL (Park Finance, "Money In" this year).
    /// `park[0x12e0]` is last year's, copied by <see cref="YearRoll"/>.</summary>
    public int YearIncome { get; private set; }
    public int LastYearIncome { get; private set; }
    /// <summary>`park[0x12e4]` -- money out since the year roll; `park[0x12e8]` last year's.</summary>
    public int YearSpending { get; private set; }
    public int LastYearSpending { get; private set; }
    /// <summary>`park[0x12ec]` / `park[0x12f0]`: the park value and the balance snapshotted by the month end
    /// every twelfth month (see <see cref="MonthEnd"/>). Park Finance's "Last Year" column.</summary>
    public int LastYearParkValue { get; private set; }
    public int LastYearBalance { get; private set; }

    /// <summary>⭐ `0x100EF8`, the year roll, READ (a frameless leaf): `0x12e0 = 0x12dc; 0x12e8 = 0x12e4`,
    /// then both this-year figures zeroed. Its only caller is the calendar `0x16B060` at `0x16B1BC`, when
    /// the YEAR changes at a month end (<see cref="ParkManagement.MonthChanged"/>).</summary>
    public void YearRoll()
    {
        LastYearIncome = YearIncome; LastYearSpending = YearSpending;
        YearIncome = 0; YearSpending = 0;
    }

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
        YearIncome += amount;                                              // park[0x12dc]
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
        TotalSpending += amount;                                           // park[0x12d4]
        YearSpending += amount;                                            // park[0x12e4]
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
    /// <summary>⭐ `park+0x107c`: the park value at each month end (`0x1011C8`), which Overall Statistics plots
    /// as its Park Value series.</summary>
    readonly int[] _value = new int[PeriodSlots];
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

    /// <summary>⭐ The four loan records `park+0xC+i*0x2C`, built by the park ctor (`0x100470`, each available).</summary>
    public IReadOnlyList<ParkLoan> Loans => _loans;
    readonly ParkLoan[] _loans = Enumerable.Range(0, Lender.All.Length).Select(i => new ParkLoan(i)).ToArray();

    /// <summary>Some record is taken -- the Financial Information menu's condition for its Existing Loans row
    /// (`0x1340C0`, MIPS `0x134150`). Once true it stays true.</summary>
    public bool AnyLoanTaken => _loans.Any(l => l.Taken);

    /// <summary>`0x100E88`: the debt still owed over the taken records, in DOLLARS -- the Balance Sheet's Loans
    /// row (the console sums `+0x14 x 10` and the page shows it `/ 10`).</summary>
    public int LoansOutstanding => _loans.Where(l => l.Taken).Sum(l => l.Outstanding);

    /// <summary>`0x100E40`, the gold tickets' money goal figure (weekly `0x16BC70`, not ported yet): Cash In - the
    /// debt x 10 - Cash Out, in tenths. A loan costs its whole interest here the moment it is taken.</summary>
    public int GoalMoney => TotalIncome - 10 * LoansOutstanding - TotalSpending;

    /// <summary>⭐ `0x100CA8(park, slot)`, the accept: refused (false) when the slot is already taken -- the ONLY
    /// refusal, silent, with no balance or count test anywhere -- otherwise the record is taken and its amount is
    /// CREDITED as ordinary income (`amount x 10` through `0x100750`, so it lands in Cash In, the year's Money In and
    /// the income ring). Its "APPLIED FOR LOAN" call `0x107E48` is an empty function: no message, no sound.</summary>
    public bool TakeLoan(int slot)
    {
        if (slot < 0 || slot >= _loans.Length || !_loans[slot].Take()) return false;
        Credit(_loans[slot].Amount * 10);
        return true;
    }

    /// <summary>⭐ `0x100A18`, the park's month end, READ (MIPS `0x100A18..0x100B30`), called by the
    /// calendar `0x16B060` AFTER the strike check:
    /// <code>
    ///   balanceHistory[i] = balance                                  (0x100A70 -- BEFORE any bill)
    ///   4 loan slots: pay min(+0x14, +0x18); if pay, +0x14 -= pay, +0x10 -= 1; loans += pay x 10
    ///   park[0x12D0] += W;  wageRing[i] = W                          (W = 0x1008B8, the staff's wages x 10)
    ///   debit(loans + W)      -- ONE call; it refuses only as Debit does, and the ring keeps W anyway
    ///   credit(0)             -- 0x100750(park, 0): files nothing, clears the in-the-red count if >= 0
    ///   period += 1; the next slot of every ring cleared
    /// </code>
    /// `i = period % 144`. <paramref name="wagesTenths"/> is `0x1008B8`'s sum, already x 10.</summary>
    public void MonthEnd(int wagesTenths, int parkValueTenths = 0)
    {
        // ⭐ The balance is filed FIRST (`0x100A70`, before the loan walk and the wages): Overall Statistics' Bank
        // Balance is the balance the month closed on BEFORE its bills. ⚠ Until 2026-10-01 this sat after the debit,
        // which put every point a month's wages lower than the console's.
        _balance[Slot(PeriodCount)] = Balance;
        int loans = 0;
        foreach (var loan in _loans) loans += loan.MonthEnd() * 10;
        WageAccumulator += wagesTenths;                                    // 0x12D0 -- wages only, never repayments
        _wages[Slot(PeriodCount)] = wagesTenths;                           // 0xE3C ring -- likewise
        Debit(loans + wagesTenths);                                        // ONE debit, its result ignored (0x100B18)
        Credit(0);
        // ⭐ The park value `0x1011C8` into `park+0x107c` (findings/graph-widget.md §1.5), and every twelfth
        // month the last-year snapshots `0x12ec`/`0x12f0` (`0x100B80`/`0x100B8C`, when `count % 12 == 0 &&
        // count != 0`, BEFORE the increment -- so they land at month ends 13, 25, 37..., one month after the
        // money roll when the calendar starts in month 0. ⚠ A real quirk, kept; parkstats-screens.md §4.3).
        _value[Slot(PeriodCount)] = parkValueTenths;
        if (PeriodCount % 12 == 0 && PeriodCount != 0) { LastYearParkValue = parkValueTenths; LastYearBalance = Balance; }
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

    /// <summary>The park value at the k-th completed month back (`park+0x107c`, the accumulator getters' shape).</summary>
    public int ValueInPeriod(int k) => PeriodIndex(k) is var i && i < 0 ? 0 : _value[i];

    /// <summary>⭐ Advisor variable 49, the WAGES_HIGH producer (MIPS `0x10E32C..0x10E3D8`):
    /// `income(1)/10 &lt; wages(1)/10 &amp;&amp; income(2)/10 &lt; wages(2)/10` -- wages above ALL income in
    /// each of the last two completed months. Signed integer divisions.</summary>
    public bool WagesHigh =>
        IncomeInPeriod(1) / 10 < WagesInPeriod(1) / 10 && IncomeInPeriod(2) / 10 < WagesInPeriod(2) / 10;
}
