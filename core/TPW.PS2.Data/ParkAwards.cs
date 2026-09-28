namespace TPW.PS2.Data;

/// <summary>What the park has WON: the gold tickets it holds and the awards behind them.
///
/// ⭐ One award IS earned now: the Security Award, by <see cref="ParkManagement.WeeklyPass"/>
/// (`0x16BC70`, findings/staff-management.md §11.4). ⚠⚠ The rest is still counters the game can be
/// TOLD about. The five medals' PURPOSES are known -- the advisor says them outright (Aesthetic for a
/// park full of features, Green for litter bins, Path Economy for short walks, Security for coverage,
/// Upgrade for upgraded rides) -- and the same weekly pass `0x16BC70` tests the other four and the
/// visitor/profit/business tickets too, but only the Security test has been read to the end; the rule
/// that promotes a coaster to "Ultimate" is not found. See findings/awards.md.</summary>
public sealed class ParkAwards
{
    /// <summary>Tickets in hand. ⭐ Spent on opening new parks, not on rides:
    /// `STR_MAP_BODY_USE_TICKETS_TO_ACCESS_ISLAND` and `WorldMapGoldTickets`.</summary>
    public int GoldTickets { get; set; }

    /// <summary>Coasters rated "Ultimate" -- the star row. There are 14 coasters on the disc, one
    /// star icon each.</summary>
    public int UltimateCoasters { get; set; }

    /// <summary>The five hidden awards, in the order the UI registry gives them (texture ids
    /// 0x25..0x29): upgrade, security, path, green, aesthetic.</summary>
    public readonly bool[] Medals = new bool[5];

    public int MedalCount
    {
        get { int n = 0; foreach (bool m in Medals) if (m) n++; return n; }
    }

    // --------------------------------------------------------------------------------------------
    // The hidden awards as the calendar keeps them (findings/staff-management.md §11.4).

    /// <summary>⭐ `[0x3975E8]`: the five hidden awards as BITS, READ -- the calendar copies it into
    /// `cal+0x28` at construction (`0x16AF70` → `0x1C3718`) and back at teardown (`0x16B010` → `0x1C3708`),
    /// so the bits outlive the park: an award is won ONCE A GAME, not once a park (a correction to
    /// findings §11.4's "once per park"). `0x16B8E8(cal, i)` tests bit i, `0x16B918(cal, i, 1)` sets it,
    /// `0x1C3728` counts them. Bit i is the award whose message is 0xA0 + i: 0 Security (BROTHER),
    /// 1 Upgrade, 2 Aesthetic, 3 Green, 4 Economy.</summary>
    public int HiddenAwards { get; set; }
    public bool HasHiddenAward(int bit) => (HiddenAwards & (1 << bit)) != 0;

    /// <summary>⭐⭐ CONFIRMED 2026-09-28 -- this was INFERRED from the message key against the
    /// texture name, and the Awards draw `FUN_00186238` has now been read: its medal cells go by
    /// bit k of `stats+0x28` to sprites `0x26, 0x25, 0x29, 0x28, 0x27` -- security, upgrade,
    /// aesthetic, green, path. That is this array exactly.
    ///
    /// ⭐ So the cell order is NOT the texture registry's `0x25..0x29`, and a screen that laid the
    /// medals out in registry order would show the right five icons in the wrong places. It is
    /// also the map from a hidden-award BIT to its <see cref="Medals"/> slot, which is why one
    /// array serves both: 0 security → 1, 1 upgrade → 0, 2 aesthetic → 4, 3 green → 3,
    /// 4 economy → 2 (`m_path`, "Path Economy").</summary>
    public static readonly int[] MedalOfHiddenAward = { 1, 0, 4, 3, 2 };

    /// <summary>`0x16B918(cal, bit, 1)`, and the medal it shows.</summary>
    public void GrantHiddenAward(int bit)
    {
        HiddenAwards |= 1 << bit;
        if ((uint)bit < (uint)MedalOfHiddenAward.Length) Medals[MedalOfHiddenAward[bit]] = true;
    }

    /// <summary>⭐ `0x1C38C0(n)`, READ: `[0x3975BC] += n` (tickets in hand -- `0x1C3920` spends from it) and
    /// `[0x3975C0] += n` (never spent: tickets earned); then UI sound 0xC5 (the caller's).</summary>
    public void AwardGoldTickets(int n)
    {
        GoldTickets += n;
        GoldTicketsEarned += n;
    }
    /// <summary>`[0x3975C0]`, tickets ever earned.</summary>
    public int GoldTicketsEarned { get; set; }
}
