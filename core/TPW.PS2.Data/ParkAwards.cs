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

    readonly int[] _parkGoals = new int[8];

    /// <summary>⭐ `[0x3975C8 + world·8 + park·4]` (read `0x1C3790`, written `0x1C3768`): the goals a park has
    /// met, bit n for goal n (1..3). The calendar copies it into `cal+0x24` at construction (`0x16AF70`), and the
    /// goal notices skip a set bit (<see cref="ParkGoals.Notices"/>). ⚠ Nothing in the port sets one yet -- the
    /// weekly goal tests of `0x16BC70` are not ported -- so every park opens with its three goals.</summary>
    public int ParkGoalBits(int world, int park) =>
        (uint)world < 4 && (uint)park < 2 ? _parkGoals[world * 2 + park] : 0;

    /// <summary>`0x1C3768(world, park, bits)`.</summary>
    public void SetParkGoalBits(int world, int park, int bits)
    {
        if ((uint)world < 4 && (uint)park < 2) _parkGoals[world * 2 + park] = bits;
    }

    // --------------------------------------------------------------------------------------------
    // ⭐⭐ WHAT TICKETS BUY: ISLANDS. The save's twelve park slots (`0x1C3290`: world x 3 + park, park 2 the
    // test park; 8 bytes each, the state at +4) and the world map that spends on them (`0x2186B0`, `0x219338`).

    /// <summary>A slot's state byte (`slot+4`): 0 open, 1 closed (built, reopened free -- map mode 4), 2 locked
    /// (bought with tickets -- map modes 1 and 2). The world map copies it into each island's `+0x06`
    /// (`0x2184D0`).</summary>
    public const int SlotOpen = 0, SlotClosed = 1, SlotLocked = 2;

    readonly byte[] _slots = NewSlots();

    /// <summary>`0x1C3290`: every slot `0x1C4330` -- state 2, no saved park -- then `0x1C3528(0, 0)` and
    /// `0x1C3528(0, 2)`: a new game holds JUNGLE 1 and the test park open, and nothing else.</summary>
    static byte[] NewSlots()
    {
        var slots = new byte[12];
        Array.Fill(slots, (byte)SlotLocked);
        slots[0] = slots[2] = SlotOpen;
        return slots;
    }

    /// <summary>`0x1C3620(world, park)`, the slot's state; anything off the 4 x 3 table reads locked.</summary>
    public int SlotState(int world, int park) =>
        (uint)world < 4 && (uint)park < 3 ? _slots[world * 3 + park] : SlotLocked;

    /// <summary>`0x3975B8`, the slots in state 0 -- 2 in a new game (JUNGLE 1 and the test park).</summary>
    public int OpenParks
    {
        get { int n = 0; foreach (byte b in _slots) if (b == SlotOpen) n++; return n; }
    }

    /// <summary>`0x1C3528(world, park)`: the slot opens (the world map's `0x218E70(.., 0)`).</summary>
    public void OpenSlot(int world, int park)
    {
        if ((uint)world < 4 && (uint)park < 3) _slots[world * 3 + park] = SlotOpen;
    }

    /// <summary>`[0x2E98C0]`, a debug switch: `0x1C36D8` then answers 255 tickets in hand (and stores it). The port
    /// sets it with `--all-tickets` / `TPW_ALL_TICKETS=1`, for reaching islands without earning them.</summary>
    public bool DebugAllTickets { get; set; }

    /// <summary>`0x1C36D8`, the tickets in hand as the world map asks: 255 under <see cref="DebugAllTickets"/>.</summary>
    public int TicketsInHand()
    {
        if (DebugAllTickets) GoldTickets = 0xFF;
        return GoldTickets;
    }

    /// <summary>`0x1C3920(n)`: tickets in hand go down by <paramref name="n"/>. ⚠ No test of its own: the world map
    /// checks `0x1C36D8() &lt; cost` before offering the purchase, so this never runs short.</summary>
    public void SpendGoldTickets(int n) => GoldTickets -= n;

    static int Bits(int v, int count)
    {
        int n = 0;
        for (int i = 0; i < count; i++) if ((v & (1 << i)) != 0) n++;
        return n;
    }

    /// <summary>`0x1C3810`: tickets earnt as the world map counts them -- the set goal bits of all eight parks
    /// (bits 0..14, `0x1C37B8`) plus the hidden awards won (`0x1C3728`). ⚠ Not <see cref="GoldTicketsEarned"/>,
    /// which counts PAYOUTS; they agree because every payout sets exactly one bit.</summary>
    public int TicketsEarntCount
    {
        get
        {
            int n = Bits(HiddenAwards, 5);
            foreach (int bits in _parkGoals) n += Bits(bits, 15);
            return n;
        }
    }

    /// <summary>Mode 7's denominator: the five hidden awards plus every park's `+0x31` -- 53 on the disc.</summary>
    public static int TicketsInGame
    {
        get
        {
            int n = 5;
            for (int w = 0; w < 4; w++) for (int p = 0; p < 2; p++) n += ParkGoals.For(w, p)?.Tickets ?? 0;
            return n;
        }
    }

    /// <summary>Mode 7's "Tickets available in this park": its `+0x31` less the goal bits it has set.</summary>
    public int ParkTicketsLeft(int world, int park) =>
        (ParkGoals.For(world, park)?.Tickets ?? 0) - Bits(ParkGoalBits(world, park), 15);

    /// <summary>Mode 7's "Floating tickets available": the hidden awards not yet won, of 5.</summary>
    public int FloatingTicketsLeft => 5 - Bits(HiddenAwards, 5);
}
