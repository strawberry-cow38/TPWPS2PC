namespace TPW.PS2.Data;

/// <summary>A park's three goals, the record `0x16C008(world, park)` returns (READ, MIPS `0x16C008..0x16C0E0`).
/// Only the three fields the goal notices print are named here.</summary>
/// <param name="Visitors">`+0xC`: goal 1, visitors (text row 376).</param>
/// <param name="Profit">`+0x10`: goal 2, profit above the starting balance (row 894), printed as stored.</param>
/// <param name="Years">`+0x14`: goal 3, years in business (row 515).</param>
public readonly record struct ParkGoalRecord(int Visitors, int Profit, int Years);

/// <summary>⭐ The park's goals and the three notices that open every ordinary park's message stack.
///
/// `0x16C008(world, park)` is a switch over the world and the park (0 or 1) that returns one of eight
/// static 0x38-byte records at `0x3621E8..0x3623A7`, and 0 for anything else. The values below are those
/// records' `+0xC/+0x10/+0x14` words, read from the ELF.
///
/// `0x16BA58(cal)` posts the notices, called from the advisor's state 0 after the greeting
/// (<see cref="ParkAdvisor.GoalNotices"/>): nothing in the test park (`0x153410`); then for goal n = 1, 2, 3
/// in turn, if bit n of `cal+0x24` is clear (`0x16B900`), `sprintf(buf, translate(row), field)` (`0x29D628`)
/// and `0x16B9F8(buf)`, a type-4 record with its own text and no object. So goal 3 is the newest record.
/// ⚠ `0x16BA58` does not test the record for 0 before it reads `+0xC`; the port only asks for parks 0 and 1 of
/// the four worlds, which always have one, and answers nothing where natively it would read low memory.</summary>
public static class ParkGoals
{
    /// <summary>The notice rows: goal 1 people (0x178), goal 2 profit (0x37E), goal 3 business (0x203).</summary>
    public const int RowPeople = 0x178, RowProfit = 0x37E, RowBusiness = 0x203;

    /// <summary>By world (`0x14E170`: 0 jungle, 1 hallow, 2 fantasy, 3 space) and park (`0x14E160`, 0 or 1).</summary>
    static readonly ParkGoalRecord[,] Records =
    {
        { new(100, 2000, 1), new(200, 3000, 2) },   // 0x3622C8, 0x362300
        { new(150, 2500, 1), new(250, 3000, 2) },   // 0x362258, 0x362290
        { new(150, 2500, 1), new(500, 5000, 5) },   // 0x3621E8, 0x362220
        { new(250, 3000, 2), new(500, 5000, 5) },   // 0x362338, 0x362370
    };

    /// <summary>`0x16C008(world, park)`, or null where it returns 0.</summary>
    public static ParkGoalRecord? For(int world, int park) =>
        (uint)world < 4 && (uint)park < 2 ? Records[world, park] : null;

    /// <summary>`0x16BA58`'s texts, oldest first: one per goal whose bit (1..3) in <paramref name="goalBits"/>
    /// (`cal+0x24`) is clear. <paramref name="translate"/> is the text database; a row it cannot give is
    /// skipped rather than posted as a blank record.</summary>
    public static IReadOnlyList<string> Notices(int world, int park, int goalBits, bool testPark, Func<int, string> translate)
    {
        var list = new List<string>(3);
        if (testPark || For(world, park) is not { } goals) return list;
        (int Bit, int Row, int Value)[] each =
            { (1, RowPeople, goals.Visitors), (2, RowProfit, goals.Profit), (3, RowBusiness, goals.Years) };
        foreach (var (bit, row, value) in each)
        {
            if ((goalBits & (1 << bit)) != 0) continue;                  // 0x16B900(cal, bit)
            if (translate?.Invoke(row) is { } template) list.Add(FormatInt(template, value));
        }
        return list;
    }

    /// <summary>`sprintf` with one integer, as these rows use it: the first `%d` becomes the value.</summary>
    public static string FormatInt(string template, int value)
    {
        int at = template.IndexOf("%d", StringComparison.Ordinal);
        return at < 0 ? template : string.Concat(template.AsSpan(0, at), value.ToString(), template.AsSpan(at + 2));
    }
}
