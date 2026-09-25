namespace TPW.PS2.Data;

/// <summary>⭐⭐ THE LAPTOP'S MENUS, read off the console's own table AND its builders.
///
/// ⚠⚠ THE FIRST VERSION OF THIS FILE WAS WRONG, and master caught it in one line: "the list
/// continues off the bottom". It listed all thirteen table entries as one flat menu. They are not
/// one menu and they are not all shown: the table is a POOL, and two builders pick from it by
/// index under conditions. Reading an initialiser and assuming its contents were the screen is the
/// same mistake as reading a scene file's comment instead of its draw.
///
/// **The chain, all read:**
/// * `MENUS.WAD/main.sce` and `main_info.sce` each hold ONE element -- `textoptions`, row 115,
///   col 45, left. Two scene files, because there are two menus.
/// * `FUN_001fc778` builds the menu registry at `0x2ec718`, 16 bytes per entry. ⭐ Its index is one
///   BELOW the menu id, corroborated three ways against ride_data 0x0E, shop_data 0x11 and
///   sideshow_data 0x13, which sit at indices 13, 16 and 18. So `main` is id 2.
/// * `FUN_0016ef68` fills the option POOL at **`0x2b97b8`**, 8 bytes per entry `{u32 text id,
///   handler}`. ⚠ Not `0x2b97c0` as first written: nothing in the image materialises that, while
///   `0x2b97b8` is materialised twice, so entry 0 is a real row rather than a stray.
/// * `FUN_0016e520(menu, n)` appends pool entry **n** to the live list at `0x3ae108`.
/// * `FUN_0016e558` builds the MAIN menu, `FUN_0016e710` the INFORMATION submenu -- each a
///   straight-line run of guarded appends, which is where the conditions below come from.</summary>
public static class LaptopMainMenu
{
    public const string MainScene = "main.sce", InfoScene = "main_info.sce";
    public const string ListElement = "textoptions";
    public const int MenuId = 2;
    public const int RowStep = ShopScreen.RowStep;

    /// <summary>⭐ How many rows the panel actually holds. Rows start at 115 and step 32, and the
    /// chrome's inner content edge is row **469** -- measured up the lossless TGA at col 150, where
    /// the bevel's bright face gives way to the interior. Eleven rows end at 435 with text to ~465;
    /// a twelfth would put text at ~497, ON the bevel. ⭐ The ride screen is the existence proof:
    /// it draws exactly eleven label rows, 115 to 435.</summary>
    public const int ContentBottom = 469, MaxRows = 11;

    /// <summary>One pooled option. <paramref name="Index"/> is what `FUN_0016e520` is passed.</summary>
    public readonly record struct Option(int Index, int TextId, string Handler, string Condition = null,
                                         string Opens = null);

    /// <summary>⭐⭐ The MAIN menu, in `FUN_0016e558`'s append order. At most EIGHT rows, which is
    /// why it fits where thirteen did not.</summary>
    public static readonly Option[] Main =
    {
        new(0,  530,  null,           null,                       "main_info"),          // Information
        new(6,  752,  "FUN_001c7220", "FUN_0014c928 && FUN_0014c8b8", "main_buildhire"), // Build & Hire
        new(7,  1042, "FUN_001c7108", null,                       "main_research"),      // Research
        new(8,  485,  "FUN_001c7a98", null,                       "main_parkstats"),     // Park Statistics
        new(9,  441,  "FUN_001c78d0", null,                       "main_financialinfo"), // Financial Information
        new(10, 549,  "FUN_001c5ce0", null,                       "main_gameoptions"),   // Game Options
        new(11, 617,  "FUN_001c7c00", "FUN_0014e538 == 0"),                              // Open Park
        new(12, 844,  "FUN_001c7bb0"),                                                   // Close Park
    };

    /// <summary>⚠ Index 13 (`Build`, text 801) replaces the whole first group in
    /// `FUN_0016e558`'s ELSE arm, taken when `FUN_00153410()` is non-zero -- a mode this port has
    /// not identified. Recorded rather than drawn.</summary>
    public static readonly Option AltModeBuild = new(13, 801, "FUN_001c7650", "FUN_00153410 != 0");

    /// <summary>⭐⭐ The INFORMATION submenu, `FUN_0016e710`. Every row is conditional: an entry
    /// appears only when the park CONTAINS one of that thing, which is why a fresh park's laptop
    /// is nearly empty. That is decoded, not assumed -- each append sits behind its own predicate.</summary>
    public static readonly Option[] Information =
    {
        new(1, 420,  "FUN_001c5e08", "any of FUN_0014cba8/cbf0/cca0/cc58", "main_i_ride"),
        new(2, 1041, "FUN_001c60d0", "FUN_0014cd40",                       "main_i_shop"),
        new(3, 760,  "FUN_001c6398", "FUN_0014cd88",                       "main_i_sideshow"),
        new(4, 429,  "FUN_001c6660", "FUN_0014cdd0",                       "main_i_bathroom"),
        new(5, 995,  "FUN_001c6980", "FUN_00153410==0 && any staff",       "main_i_staff"),
    };

    /// <summary>What the main menu shows. ⚠ `parkOpen` is the ONE condition this port models; the
    /// rest are park-content predicates the caller does not yet have, so they default to present.
    /// ⭐ `Close Park` is text 844, `STR_MAINMENU_EXIT_TO_MAP_SCREEN` -- it is "leave for the map
    /// screen", not the opposite of Open Park, which is why the console appends it unconditionally.</summary>
    public static IEnumerable<Option> VisibleMain(bool parkOpen = false, bool canBuildAndHire = true)
    {
        foreach (var o in Main)
        {
            if (o.Index == 11 && parkOpen) continue;          // Open Park: only while closed
            if (o.Index == 6 && !canBuildAndHire) continue;
            yield return o;
        }
    }
}
