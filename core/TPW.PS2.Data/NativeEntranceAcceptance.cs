namespace TPW.PS2.Data;

/// <summary>210B38 / 210C98 arithmetic and ordered money consumer. The caller supplies
/// actual current object+1D4 sum and fee; this does not discover scenario fee overrides.</summary>
public static class NativeEntranceAcceptance
{
    public static int Classify(int valueSum, int fee, int random5001)
    {
        if ((uint)random5001 >= 5001) throw new ArgumentOutOfRangeException(nameof(random5001));
        int q = unchecked(valueSum << 12) / (10000 + random5001);
        int f = fee / 10;
        int a = unchecked(q * 0x1400) >> 12;
        int b = unchecked(q * 0x1800) >> 12;
        int c = unchecked(q * 0x0c00) >> 12;
        return f < a ? f <= c ? 1 : 0 : f < b ? -1 : -2;
    }

    /// <param name="classified">⭐ `0x210C78`: the class goes to the advisor's event counter 19 (v75, the
    /// ticket-price counter rules 13..15 read) -- every class, the refusal's −2 included, unless the debug
    /// switch "ForceKidsToEnter" (`0x230260`, a stub returning 0). Null: not counted.</param>
    public static bool TryCharge(VisitorNeeds needs, int guest, ParkFinances finance,
        Func<int> fee, Func<int> valueSum, Func<int,int> random, Action accepted, Action<int> classified = null)
    {
        if (!needs.Has(guest)) throw new InvalidOperationException("Entrance acceptance requires the live guest's cash row.");
        int quoted = fee();
        if (quoted >= needs.Of(guest).Cash) return false; // equality rejects BEFORE the class/RNG
        int sum = valueSum();
        int roll = random(5001);
        int classFee = fee(); // 210BD4 re-reads after the sum and RNG, not the cash-test quote
        int cls = Classify(sum, classFee, roll);
        classified?.Invoke(cls);
        if (cls <= -2) return false;
        accepted(); // native manager counter increments before finance
        int charged = fee(); // 100D28 re-reads the fee and returns this amount
        finance.CreditAdmission(charged); // 100D28: the credit, then the gate total and ring
        var wants = needs.Of(guest); // 210D38 reads current cash AFTER the finance call
        wants.Cash = unchecked(wants.Cash - charged);
        needs.Set(guest, wants);
        return true;
    }
}
