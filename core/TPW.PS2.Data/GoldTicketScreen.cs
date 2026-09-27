namespace TPW.PS2.Data;

/// <summary>The laptop's Gold Tickets screen -- `main_goldtickets`, menu <b>3</b>.
///
/// ⭐ THE MENU ID IS DERIVED WITH TWO CONTROLS, not guessed. `FUN_001fc778` registers all 27
/// scenes at a 0x10 stride; taking the slot index and solving for the offset against the two ids
/// this port already knows -- Shop `0x11` and Ride `0x0E` -- gives the SAME answer from both, and
/// that offset puts `main_goldtickets` at 3.
///
/// ⚠ There is no Gold Tickets row in the laptop's main menu (<see cref="LaptopMainMenu"/>), and
/// every string on the screen is `STR_PARKSTATS_*`, so it opens from Park Statistics.
///
/// The layout ships four elements:
/// <code>
///   awardstext  row 65  col 45            the "Awards" heading
///   MedalRow    row 102 col 45  265x96    medal cutouts and icons
///   uctext      row 227 col 45            the "Ultimate Coasters" heading
///   StarRow     row 265 col 45  427x180   star cutouts and icons
/// </code>
///
/// ⚠ This screen is NOT a <see cref="LaptopScreen"/>. That record describes label/value rows on a
/// 32-row grid; this one is two headings over two icon grids and shares none of that arithmetic.
/// </summary>
public static class GoldTicketScreen
{
    public const string SceneFile = "main_goldtickets.sce";
    public const int MenuId = 3;

    public const string AwardsElement = "awardstext";
    public const string MedalElement = "MedalRow";
    public const string UltimateElement = "uctext";
    public const string StarElement = "StarRow";

    /// <summary>`STR_PARKSTATS_AWARDS`.</summary>
    public const int AwardsTextId = 475;
    /// <summary>`STR_PARKSTATS_AWARDS_ULTIMATE_COASTER` -- and the string really is spelled
    /// "Ulimate Coasters" on the disc. ⚠ Not corrected: it is what the game shows.</summary>
    public const int UltimateTextId = 895;
    /// <summary>`STR_YOU_HAVE_GOLD_TICKETS`, "You have %d Ticket(s)".</summary>
    public const int YouHaveTicketsTextId = 867;

    /// <summary>A hidden award: its medal art, the label, and the texture id the UI registry gives
    /// it. ⭐ THE ORDER IS THE GAME'S. `FUN_00216028` registers the five medals at consecutive ids
    /// 0x25..0x29 and this list is in that order, so the row does not need an invented one.</summary>
    public readonly record struct Medal(int TextureId, int TextId, string Art, string EarnedBy);

    public static readonly Medal[] Medals =
    {
        new(0x25, 690, "awards/m_upgrade.ssh",   "a large percentage of the park's rides upgraded"),
        new(0x26,  53, "awards/m_security.ssh",  "thorough security coverage"),
        new(0x27, 856, "awards/m_path.ssh",      "visitors do not walk too far to reach a ride"),
        new(0x28, 761, "awards/m_green.ssh",     "enough shops carry litter bins"),
        new(0x29, 428, "awards/m_aesthetic.ssh", "a large park full of beautiful features"),
    };

    /// <summary>⭐ ONE STAR PER COASTER, FOURTEEN OF THEM, at consecutive ids 0x17..0x24 and
    /// grouped by world in world order: JUNGLE 3, HALLOW 5, FANTASY 3, SPACE 3.
    ///
    /// ⭐⭐ Fourteen is also exactly how many `.sam` shapes carry the `<`/`>` station markers, and
    /// the per-world split agrees -- two censuses run for different reasons landing on the same
    /// set, which is the check that this list is complete.</summary>
    public static readonly (int TextureId, string Art)[] UltimateStars =
    {
        (0x17, "ultimatec/s_cart.ssh"),       (0x18, "ultimatec/s_apehead.ssh"),
        (0x19, "ultimatec/s_croc.ssh"),       (0x1a, "ultimatec/s_hades.ssh"),
        (0x1b, "ultimatec/s_devil.ssh"),      (0x1c, "ultimatec/s_bat.ssh"),
        (0x1d, "ultimatec/s_ghost.ssh"),      (0x1e, "ultimatec/s_shakey.ssh"),
        (0x1f, "ultimatec/s_bigdrip.ssh"),    (0x20, "ultimatec/s_catapillar.ssh"),
        (0x21, "ultimatec/s_candy.ssh"),      (0x22, "ultimatec/s_moonshot.ssh"),
        (0x23, "ultimatec/s_mega.ssh"),       (0x24, "ultimatec/s_shocker.ssh"),
    };

    /// <summary>The generic unearned star, `0x2e`, and the ticket itself, `0x2d`.</summary>
    public const int BlankStarTextureId = 0x2e, TicketTextureId = 0x2d;

    /// <summary>The small laptop icons, `0x15` and `0x16`. ⚠ `UI.WAD` also ships
    /// `AWARD_MEDAL_64`, which is NOT in the registry -- the big row's art loads by a path that
    /// has not been found, so the 32px icons are what this port can cite.</summary>
    public const int SmallMedalTextureId = 0x15, SmallStarTextureId = 0x16;

    /// <summary>⚠ NOT DECODED, and listed so nobody mistakes the medal row for the whole economy:
    /// a medal is only one of the ways a Gold Ticket is won. The advisor also pays out for the Log
    /// Flume, Go Kart, Roller Coaster and Water Ride, for the tutorial, for visitor, business and
    /// profit goals, for placement, and for a mini-game -- about ten more sources with no row on
    /// this screen. What TRIPS any of them is unread.</summary>
    public static readonly int[] NonMedalTicketMessageTextIds =
        { 71, 103, 535, 641, 319, 538, 934, 952, 1033, 599 };
}
