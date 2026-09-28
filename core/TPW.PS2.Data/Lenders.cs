using System;

namespace TPW.PS2.Data;

/// <summary>⭐ The four lenders New Loan offers, and the interest they charge.
/// `findings/finance-screens.md` §3; built by the park constructor `FUN_00100470`, named by
/// `FUN_0017E480` from `0x363B80`.
///
/// ⚠ The names are plain C strings, not text ids -- they do NOT localise, so they are literals
/// here rather than rows in the text table. Spelling them as text ids would silently give the
/// wrong thing in every other language.</summary>
public readonly record struct Lender(string Name, int MaxLoan, int Rate, int MaxTermYears)
{
    /// <summary>The four, in the order the lender spinner steps through them (1..4, wrapping).</summary>
    public static readonly Lender[] All =
    {
        new("Mr Byrne",  100_000, 20, 3),
        new("Ms Dabb",    50_000, 20, 2),
        new("Mr Howell",  25_000, 20, 2),
        new("Ms West",    10_000, 15, 2),
    };

    /// <summary>⭐ `FUN_0017E5D0`, in 12.12 fixed point: ANNUAL compounding at the lender's rate
    /// over the term in YEARS, then a flat monthly repayment.
    ///
    /// <code>
    /// total       = amount * (1 + rate/100)^years
    /// repayment   = total / months          // integer division
    /// total       = repayment * months      // re-derived from the rounded repayment
    /// </code>
    ///
    /// ⚠ The re-derivation is not tidying: the console writes `repayment * months` back over the
    /// total, so the figure the player is shown is the one they will actually pay, and it can sit
    /// a little BELOW the compounded amount. Ms West is the visible case -- 10,000 x 1.15² is
    /// 13,225, but 13,225 / 24 = 551 and 551 x 24 = 13,224, so the screen says 13,224.
    ///
    /// ⚠ The console's `pow` is `exp(log(a) * b)` in fixed point through floats, so an exact
    /// figure can differ from this by a unit. Settling that needs a savestate taken after a loan,
    /// reading `park+0xC+i*0x2C`.</summary>
    public (int Total, int Repayment) Quote(int amount, int years)
    {
        int months = Math.Max(1, years * 12);
        // ⚠⚠ INTEGER COMPOUNDING, NOT Math.Pow. `Math.Pow(1.2, 3)` is 1.7279999999999998, so
        // 100,000 x it truncates to 172,799 and the whole page comes out a unit light:
        // 4,799/month and a 172,764 total against the console's 4,800 and 172,800. Caught because
        // finance-screens.md states the nominal figures and the render disagreed with them.
        //
        // `amount * (100+rate)^years / 100^years` is exact for every lender here: Mr Byrne is
        // 100,000 x 120³ / 100³ = 172,800 and Ms West 10,000 x 115² / 100² = 13,225.
        long num = amount, den = 1;
        for (int y = 0; y < years; y++) { num *= 100 + Rate; den *= 100; }
        long total = num / den;
        int repayment = (int)(total / months);
        return (repayment * months, repayment);
    }

    /// <summary>What the page shows with nothing changed. ⚠⚠ ONLY THE LENDER SPINNER TAKES INPUT
    /// on the PS2 -- the amount and term spinners are built and ranged but the bit that lets them
    /// read the pad is never set for them, so the amount is ALWAYS the lender's maximum and the
    /// term ALWAYS the maximum. That is why the page's own labels read "Max.Loan" and "Max.Term"
    /// rather than "Amount" and "Term". Porting the spinners as adjustable would be a plausible
    /// improvement and a departure.</summary>
    public (int Total, int Repayment) DefaultQuote() => Quote(MaxLoan, MaxTermYears);
}
