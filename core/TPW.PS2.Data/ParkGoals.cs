namespace TPW.PS2.Data;

/// <summary>A park's goals and award thresholds, the record `0x16C008(world, park)` returns (READ, MIPS
/// `0x16C008..0x16C0E0`), as the weekly pass `0x16BC70` reads it.</summary>
/// <param name="Visitors">`+0xC`: goal 1, guests ever admitted reach this (`stats+0x20`; text row 376).</param>
/// <param name="Profit">`+0x10`: goal 2, `0x100E40` above this x 10 (row 894), printed as stored.</param>
/// <param name="Years">`+0x14`: goal 3, whole years since Open Park (row 515).</param>
/// <param name="FeatureCost">`+0x18`: the Aesthetic award's bar -- the summed purchase cost of every placed
/// feature (`0x16BBD0`). 2000 in every park.</param>
/// <param name="PathCells">`+0x1C`: the Path Economy award's ceiling on path cells (`0x1531D8`). 100 everywhere.</param>
/// <param name="StarterGoal">`+0x30`: the fourth goal (message 0xAA) is armed -- JUNGLE 1 only: a sideshow,
/// a shop, a feature and two rides (`0x14CF88/58/28`, `0x14CE68/98/C8/F8`).</param>
/// <param name="Tickets">`+0x31`: the tickets this park can pay out, its goals and its mini-games; the
/// world map's "Tickets available in this park" (`0x218F78` mode 7).</param>
public readonly record struct ParkGoalRecord(int Visitors, int Profit, int Years,
    int FeatureCost = 2000, int PathCells = 100, bool StarterGoal = false, int Tickets = 0);

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
        { new(100, 2000, 1, StarterGoal: true, Tickets: 7), new(200, 3000, 2, Tickets: 6) },   // 0x3622C8, 0x362300
        { new(150, 2500, 1, Tickets: 5), new(250, 3000, 2, Tickets: 7) },                     // 0x362258, 0x362290
        { new(150, 2500, 1, Tickets: 5), new(500, 5000, 5, Tickets: 7) },                     // 0x3621E8, 0x362220
        { new(250, 3000, 2, Tickets: 5), new(500, 5000, 5, Tickets: 6) },                     // 0x362338, 0x362370
    };

    /// <summary>The record addresses, by world and park, so the audit can re-read every field from the ELF.</summary>
    public static readonly uint[,] RecordAddresses =
    {
        { 0x3622C8, 0x362300 }, { 0x362258, 0x362290 }, { 0x3621E8, 0x362220 }, { 0x362338, 0x362370 },
    };

    /// <summary>The goal messages of `0x16BC70`, by goal bit 1..4 (index 0 unused).</summary>
    public static readonly int[] GoalMessages = { 0, 0x9D, 0x9E, 0x9F, 0xAA };

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
    public static string FormatInt(string template, int value) => FormatInts(template, value);

    /// <summary>`sprintf` with integers only: each `%d` in turn takes the next value; a `%d` past the last value is
    /// left as written.</summary>
    public static string FormatInts(string template, params int[] values)
    {
        var sb = new System.Text.StringBuilder();
        int next = 0, i = 0;
        while (i < template.Length)
        {
            if (next < values.Length && i + 1 < template.Length && template[i] == '%' && template[i + 1] == 'd')
            { sb.Append(values[next++]); i += 2; continue; }
            sb.Append(template[i++]);
        }
        return sb.ToString();
    }
}
