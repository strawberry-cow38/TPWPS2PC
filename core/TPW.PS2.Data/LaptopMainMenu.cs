namespace TPW.PS2.Data;

/// <summary>⭐⭐ THE LAPTOP'S MAIN MENU, read off the console's own option table.
///
/// The chain, all of it read rather than guessed:
/// * `MENUS.WAD/main.sce` lays the screen out, and it has exactly ONE element -- `textoptions`
///   at row 115, col 45, left-justified. So the screen IS a list; there is no title element and
///   no value column.
/// * `FUN_001fc778` builds the menu registry at `0x2ec718`, 16 bytes per entry
///   (`"<name>.sce"`, `"<name>"`, -1, 0). `main` is the second entry.
///   ⭐ The registry index is one BELOW the menu id, corroborated three ways: this port already
///   had ride_data = 0x0E, shop_data = 0x11 and sideshow_data = 0x13 decoded from their binders,
///   and those sit at registry indices 13, 16 and 18. Three for three at index+1.
/// * `FUN_0016ef68` fills the option table at **`0x2b97c0`**, 8 bytes per entry:
///   `{u32 text id, handler}`. That is the list below.
///
/// ⭐ HOW IT WAS FOUND, because the method is reusable: scanning the image for
/// `addiu rt, zero, imm` with each `STR_MAINMENU_*` text id as the immediate, then clustering the
/// hit addresses. The ids land on a regular 0x18 stride from `0x16ef84` to `0x16f0b8`. ⚠ The same
/// scan run without an address-range and size control first pointed at a 7,586-line decompiler
/// artefact outside the code range, which contains most 3-digit integers by chance. The control
/// that made it trustworthy: the SHOP's seven known label ids cluster at `0x1d7288`, inside the
/// already-decoded shop draw `FUN_001d70c8`.</summary>
public static class LaptopMainMenu
{
    /// <summary>`main.sce`, one `textoptions` element.</summary>
    public const string SceneFile = "main.sce";
    /// <summary>The element the options are drawn from.</summary>
    public const string ListElement = "textoptions";
    /// <summary>Registry index 1, so menu id 2.</summary>
    public const int MenuId = 2;
    /// <summary>The same 32 the info screens step by; see <see cref="ShopScreen.RowStep"/>.</summary>
    public const int RowStep = ShopScreen.RowStep;

    /// <summary>One option: its text-table row, the console handler it came from, and the menu it
    /// opens where this port knows one.</summary>
    public readonly record struct Option(int TextId, string Handler, string Opens = null);

    /// <summary>⭐⭐ The table at `0x2b97c0`, IN ORDER. Each entry is `{text id, handler}` and the
    /// handler addresses are kept so the next person can pick up where this stopped.
    ///
    /// ⚠ `Open Park` (617) and `Close Park` (844) are two entries in the table but cannot both be
    /// on screen: twelve rows from 115 at a 32 step ends at 467, and thirteen would end at 499 --
    /// past the panel's own bottom edge at 493. A park is either open or closed, so they are
    /// treated as one slot. ⚠ That is a geometric argument, not a decoded one: the condition that
    /// picks between them has not been read.</summary>
    public static readonly Option[] Options =
    {
        new(420,  "FUN_001c5e08", "main_i_ride"),      // Ride Information
        new(1041, "FUN_001c60d0", "main_i_shop"),      // Shop Information
        new(760,  "FUN_001c6398", "main_i_sideshow"),  // Side Show Information
        new(429,  "FUN_001c6660", "main_i_bathroom"),  // Toilet Information
        new(995,  "FUN_001c6980", "main_i_staff"),     // Staff Information
        new(752,  "FUN_001c7220", "main_buildhire"),   // Build & Hire
        new(1042, "FUN_001c7108", "main_research"),    // Research
        new(485,  "FUN_001c7a98", "main_parkstats"),   // Park Statistics
        new(441,  "FUN_001c78d0", "main_financialinfo"), // Financial Information
        new(549,  "FUN_001c5ce0", "main_gameoptions"), // Game Options
        new(617,  "FUN_001c7c00"),                     // Open Park   -- exclusive with Close Park
        new(844,  "FUN_001c7bb0"),                     // Close Park  -- exclusive with Open Park
        new(801,  "FUN_001c7650"),                     // Build
    };

    /// <summary>The row the table carries ahead of the options -- `{0x212, null}` at `0x2b97b8`,
    /// text id 530 (`STR_PARKSTATS_INFORMATION`, "Information") with a NULL handler.
    /// ⚠ Its role is NOT settled. A null handler means it cannot be chosen, which fits a heading,
    /// but `main.sce` gives the screen no title element to put it in and thirteen rows do not fit
    /// below 115. It may equally be the terminator of the table before this one. Recorded with its
    /// address so the question stays askable; nothing here draws it.</summary>
    public const int HeadingTextId = 530;

    /// <summary>Which options are on screen, given whether the park is open. ⭐ This is where the
    /// Open/Close exclusion lives, so a caller never sees both.</summary>
    public static IEnumerable<Option> Visible(bool parkOpen)
    {
        foreach (var o in Options)
        {
            if (o.TextId == 617 && parkOpen) continue;    // already open: offer Close
            if (o.TextId == 844 && !parkOpen) continue;   // already closed: offer Open
            yield return o;
        }
    }
}
