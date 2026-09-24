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
/// ⚠ THE STARTING BALANCE IS NOT READ. The constructor zeroes every running total and sets the
/// spend-anything flag but never writes `park[4]`, so the opening balance arrives with the
/// scenario or save -- untraced. This opens at zero and the caller sets it; a number invented
/// here would look like a decoded one.
///
/// ⚠ THE GRAPHS ARE NOT MODELLED. Both ring buffers are indexed by a period counter at
/// `park[0x12bc]` whose advance has not been read, and a 144-slot history advanced by a clock
/// nobody has found would be 144 slots of zero pretending to be a feature. The running totals
/// ARE kept, because those need no clock.</summary>
public sealed class ParkFinances
{
    /// <summary>`park[4]`. ⚠ May go negative: the console credits without a floor and only uses
    /// the sign to raise or clear a warning.</summary>
    public int Balance { get; set; }

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

    /// <summary>`park[0x12d8]` and `park[0x12e4]` -- lifetime totals, unaffected by the period
    /// counter this port does not model.</summary>
    public int TotalIncome { get; private set; }
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
        if (category is { } c) _byCategory[c] = _byCategory.GetValueOrDefault(c) + amount;
    }

    /// <summary>`FUN_00100698`. <returns>false when the park cannot afford it and nothing was
    /// taken -- the console returns 0 there and the caller is expected to abandon the
    /// purchase.</returns></summary>
    public bool Debit(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "a debit is not a negative credit -- call Credit");
        if (!Unlimited && Balance < amount) return false;
        Balance -= amount;
        TotalSpending += amount;
        return true;
    }
}
