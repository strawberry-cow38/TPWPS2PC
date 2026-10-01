using System.Buffers.Binary;
using TPW.PS2.Data;

/// <summary>⭐ THE PARK'S LOANS (strawberry, 2026-10-01: "move onto the rest"). Taking one (`0x100CA8`), the month
/// end's repayment walk inside `0x100A18`, the balance ring filed before the bills, payoff, and the two readers the
/// laptop shows (the Balance Sheet's debt `0x100E88`, the gold tickets' money figure `0x100E40`). Each claim carries
/// the control that would read differently if the rule were the obvious one.</summary>
static class LoanChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "loans: " + label);
        var exe = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        Executable(disc.Read(exe.Extent, exe.Size), Check);
        Take(Check);
        MonthEnd(Check);
        Payoff(Check);
        Readers(Check);
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
        // sw a2,0xbc(v1) / lw v0,0x28(t0) / lw a0,0x14(t0) / lw v1,0x18(t0) / sw v0,0x14(t0) / sw v1,0x10(t0) /
        // jal 0x100698 -- the balance ring, then the walk's flag, outstanding and repayment, its two decrements, and
        // the ONE debit, in that order.
        Check(At(0x100a70) == 0xac6600bc && At(0x100a78) == 0x8d020028 && At(0x100a84) == 0x8d040014
              && At(0x100a88) == 0x8d030018 && At(0x100aa8) == 0xad020014 && At(0x100ab0) == 0xad030010
              && At(0x100b18) == 0x0c0401a6,
              "the month end 0x100A18 files the balance ring (0x100A70) BEFORE the loan walk (+0x28, +0x14, +0x18; "
              + "decrements at 0x100AA8/0x100AB0) and the single debit (0x100B18)");
    }

    static void Take(Action<bool, string> Check)
    {
        var f = new ParkFinances();
        int before = f.Balance;
        bool took = f.TakeLoan(0);
        var l = f.Loans[0];
        Check(took && l.Taken && f.AnyLoanTaken && f.Balance - before == 1_000_000 && f.TotalIncome == 1_000_000
              && f.YearIncome == 1_000_000 && f.IncomeInPeriod(0) == 0
              && l.Amount == 100_000 && l.TermMonths == 36 && l.MonthsRemaining == 36 && l.Repayment == 4_800
              && l.Outstanding == 172_800 && l.Total == 172_800,
              $"taking Mr Byrne's loan credits {Money.Format(f.Balance - before)} as income and books {Money.Display(l.Outstanding)} "
              + $"over {l.MonthsRemaining} months at {Money.Display(l.Repayment)}");
        int balance = f.Balance;
        Check(!f.TakeLoan(0) && f.Balance == balance,
              "taking it a second time is refused, silently, and moves no money -- the only refusal there is");
        var w = new ParkFinances();
        w.TakeLoan(3);
        Check(w.Loans[3].Repayment == 551 && w.Loans[3].Total == 13_224 && w.Loans[3].Outstanding == 13_224,
              $"Ms West: 10,000 x 1.15^2 = 13,225 compounded, but the page and the debt are the RE-DERIVED "
              + $"{Money.Display(w.Loans[3].Repayment)} x 24 = {Money.Display(w.Loans[3].Total)}");
    }

    static void MonthEnd(Action<bool, string> Check)
    {
        // ⚠ One empty month end first: the ring getters answer k only while k < months (`0x100F68`), so with a
        // single month on the books k = 1 is out of range. After two, k = 1 is the loan's month.
        var f = new ParkFinances();
        f.MonthEnd(0);
        f.TakeLoan(0);
        int balance = f.Balance, spent = f.TotalSpending, wageAcc = f.WageAccumulator;
        f.MonthEnd(5_000);
        var l = f.Loans[0];
        Check(balance - f.Balance == 48_000 + 5_000 && f.TotalSpending - spent == 53_000 && f.YearSpending == 53_000
              && f.WageAccumulator - wageAcc == 5_000 && f.WagesInPeriod(1) == 5_000
              && l.Outstanding == 168_000 && l.MonthsRemaining == 35,
              $"one month end with $500 of wages: the repayment and the wages leave in one debit ({Money.Format(balance - f.Balance)}), "
              + $"both in Cash Out, but only the wages reach the wage total and ring ({Money.Format(f.WagesInPeriod(1))})");
        Check(f.BalanceInPeriod(1) == balance && f.BalanceInPeriod(1) != f.Balance,
              $"the Bank Balance ring holds {Money.Format(f.BalanceInPeriod(1))}, the balance BEFORE the month's bills, "
              + $"not the {Money.Format(f.Balance)} after them");
    }

    static void Payoff(Action<bool, string> Check)
    {
        var f = new ParkFinances();
        f.TakeLoan(0);
        for (int m = 0; m < 36; m++) f.MonthEnd(0);
        var l = f.Loans[0];
        int balance = f.Balance;
        f.MonthEnd(1_000);
        Check(l.Outstanding == 0 && l.MonthsRemaining == 0 && balance - f.Balance == 1_000 && l.Repayment == 4_800,
              $"after 36 month ends it is paid off ({Money.Display(l.Outstanding)}, {l.MonthsRemaining} months), the 37th takes "
              + $"only its wages, and the record still shows its {Money.Display(l.Repayment)} repayment");
        Check(l.Taken && !f.TakeLoan(0),
              "a paid-off loan is still TAKEN: nothing sets +0x28 back, so each lender lends once per park");
    }

    static void Readers(Action<bool, string> Check)
    {
        var f = new ParkFinances();
        f.Credit(50_000);
        int goal = f.GoalMoney;
        f.TakeLoan(0); f.TakeLoan(3);
        Check(f.LoansOutstanding == 172_800 + 13_224 && goal - f.GoalMoney == (72_800 + 3_224) * 10,
              $"two loans owe {Money.Display(f.LoansOutstanding)} (the Balance Sheet's Loans row), and the gold tickets' money "
              + $"figure drops by exactly their interest x 10 ({goal - f.GoalMoney}) the moment they are taken");
    }
}
