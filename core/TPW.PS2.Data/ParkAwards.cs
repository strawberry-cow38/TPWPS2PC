namespace TPW.PS2.Data;

/// <summary>What the park has WON: the gold tickets it holds and the awards behind them.
///
/// ⚠⚠ WHAT EARNS ANY OF THIS IS NOT READ. The five medals' PURPOSES are known -- the advisor says
/// them outright (Aesthetic for a park full of features, Green for litter bins, Path Economy for
/// short walks, Security for coverage, Upgrade for upgraded rides) -- but no threshold for any of
/// them has been found in the executable, and neither has the rule that promotes a coaster to
/// "Ultimate". So these are counters the game can be TOLD about; nothing here earns them yet.
/// See findings/awards.md.</summary>
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
}
