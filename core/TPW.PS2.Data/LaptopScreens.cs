namespace TPW.PS2.Data;

/// <summary>What one laptop info screen is made of: which scene file lays it out, which text
/// rows it labels, and which of those rows is a number, a bar or a slider.
///
/// ⭐⭐ THE THREE SCREENS ARE ONE WIDGET SET. The shop, the ride and the sideshow each bind their
/// own element list out of their own `.sce`, then draw with the SAME pieces -- `FUN_00115590` for
/// a bar, `FUN_001DAAE0` for a slider, `FUN_00138798` for a value, `FUN_0017E150` for the model.
/// So the difference between them is data, and this file is that data.
///
/// ⚠ EVERY ROW LIST HERE CAME FROM THE DRAW FUNCTION, NOT FROM THE SCENE FILE'S COMMENT. The
/// ride's comment lists ten rows and the screen has eleven: it omits **Addons**
/// (`STR_SINGLER_TRACKUPGRADES`, id 454), which `FUN_001D5210` plainly draws and which is visible
/// on the real screen. An authored comment says what someone meant.</summary>
public sealed record LaptopScreen(
    string SceneFile,
    int MenuId,
    string TitleElement,
    string LabelElement,
    string ValueElement,
    string ModelElement,
    IReadOnlyList<LaptopRow> Rows)
{
    /// <summary>The console's row step, `DAT_002E9CA8` = 32. The sideshow's draw shows it in the
    /// clear: its labels go out at `iVar9`, `+0x20`, `+0x40`, `+0x60`, i.e. 32 apart.</summary>
    public const int RowStep = ShopScreen.RowStep;

    /// <summary>The model window's authored extent, the same on all three screens: 147x240 at
    /// col 315, the right column below the chrome's notch.</summary>
    public const int ModelWidth = 147, ModelHeight = 240;

    /// <summary>⭐ The SHOP screen, `main_i_shop_data`, menu 0x11. Labels resolve to
    /// `STR_SINGLESHOP_*`; see <see cref="ShopScreen"/>, which holds its arithmetic.</summary>
    public static readonly LaptopScreen Shop = new(
        ShopScreen.SceneFile, 0x11, "ItemText", "TextOptions", "NumericItems", "Model",
        new LaptopRow[]
        {
            new(82,   LaptopRowKind.Value),        // Customers            int
            new(17,   LaptopRowKind.Money),        // Cost of Goods        $
            new(106,  LaptopRowKind.Money),        // Takings              $
            new(986,  LaptopRowKind.Money),        // Profit               $
            new(1074, LaptopRowKind.Bar,    "SatisfactionBar"),
            new(312,  LaptopRowKind.Slider, "QualitySlider"),
            new(0,    LaptopRowKind.Slider, "AdditiveSlider"),   // label is per-shop: Fat/Ice/Sugar/Salt
            new(594,  LaptopRowKind.Money,  "CostItem", "CostItemArrows"),   // Sale Price
        });

    /// <summary>⭐ The RIDE screen, `main_i_ride_data`, menu 0x0E, bound by `FUN_001D3F98` and
    /// drawn by `FUN_001D5210`. Labels are the `STR_SINGLER_*` family.
    ///
    /// ⚠ Its bars and sliders are NOT on the label grid: the scene places each one individually at
    /// 118 / 150 / 182 / 214 / 246 / 280 / 310, which is 32 apart for the four bars and then 34 and
    /// 30. So each widget is drawn at ITS OWN element's row rather than at a stepped offset --
    /// which is why the element name is carried per row here.</summary>
    public static readonly LaptopScreen Ride = new(
        "main_i_ride_data.sce", 0x0E, "ItemSelect", "TextOptions", "AgeVal", "ThingModel",
        new LaptopRow[]
        {
            new(61,   LaptopRowKind.Bar,    "ExcitementBar"),
            new(1060, LaptopRowKind.Bar,    "ReliabilityBar"),
            new(644,  LaptopRowKind.Bar,    "RepairBar"),
            new(1073, LaptopRowKind.Bar,    "LifeBar"),
            new(436,  LaptopRowKind.Slider, "SpeedSlider"),
            new(919,  LaptopRowKind.Slider, "CapacitySlider"),
            new(769,  LaptopRowKind.Slider, "DurationSlider"),
            // ⚠⚠ UPGRADES HAS A VALUE ELEMENT AND THIS FILE SAID IT DID NOT. The comment here read
            // "blank on the real screen", which was an inference; `main_i_ride_data.sce` authors
            // **UpgradeVal at row 338**, one unit above the Upgrades label at 339 -- the same
            // 1-3 unit offset AgeVal (400 vs 403) and UsersVal (436 vs 435) carry. Leaving it
            // unbound sent the value through the shared-column fallback instead of its own
            // element. Found by checking every declared element against its scene file: it was
            // the ONE element, across all three screens, that the scene provides and the port
            // never used.
            new(119,  LaptopRowKind.Text,   "UpgradeVal"),       // Upgrades
            // ⭐ Addons genuinely has no value element -- the scene stops at UpgradeVal/AgeVal/
            // UsersVal -- so this row is label-only. That asymmetry is the game's, not a gap.
            new(454,  LaptopRowKind.Text),                       // Addons, absent from the .sce comment
            new(493,  LaptopRowKind.Text,   "AgeVal"),           // Age
            new(415,  LaptopRowKind.Value,  "UsersVal"),         // Users
        });

    /// <summary>⭐ The SIDESHOW screen, `main_i_sideshow_data`, menu 0x13, bound by `FUN_001D7A30`
    /// and drawn by `FUN_001D8288`. Labels are `STR_SINGLESHOW_*`.
    ///
    /// ⚠ Its scene file carries a note between two of the developers -- "Chris: I had to add this
    /// because the menu lets you adjust both. OK :-)" -- beside the second arrow element, which is
    /// why there are two arrow rows here and only one on the shop.</summary>
    public static readonly LaptopScreen Sideshow = new(
        "main_i_sideshow_data.sce", 0x13, "SideshowName", "InfoText", "InfoValues", "Model",
        new LaptopRow[]
        {
            new(707, LaptopRowKind.Value),         // Customers
            new(637, LaptopRowKind.Value),         // Winners
            new(949, LaptopRowKind.Money),         // Takings
            new(238, LaptopRowKind.Money),         // Profit
            new(899, LaptopRowKind.Bar,    "ExcitementBar"),
            new(743, LaptopRowKind.Bar,    "SatisfactionBar"),
            new(295, LaptopRowKind.Slider, "ChanceofWinningSlider"),
            // ⚠⚠ THE ARROW NAMES ARE CROSSED RELATIVE TO THE VALUES, and only the arrows are.
            // Pair them by name and the offsets are nonsense -- CostOfPrizeArrow (353) reads 19
            // ABOVE CostOfPrizeValue (372) while PricePerGameArrow (384) reads 44 BELOW
            // PricePerGameValue (340). Pair each arrow with its NEAREST value and they agree:
            // 353 is 13 below 340, and 384 is 12 below 372. One consistent offset beats two
            // contradictory ones, so each row takes the arrow that actually belongs to it.
            new(832, LaptopRowKind.Money,  "CostOfPrizeValue",  "PricePerGameArrow"),
            new(190, LaptopRowKind.Money,  "PricePerGameValue", "CostOfPrizeArrow"),
        });
}

/// <summary>One labelled row. <paramref name="Element"/> names the scene element that carries the
/// row's widget or value, when it has one of its own; a null means the row's value sits on the
/// screen's shared value column at the label's own height.</summary>
public readonly record struct LaptopRow(int TextId, LaptopRowKind Kind, string Element = null,
                                       string ArrowElement = null);

/// <summary>The `◀▶` pair a row gets when its value can be nudged.
///
/// ⭐ ONE SPRITE, BOTH TRIANGLES: `UI.WAD/Arrow/UIarrow.ssh` is 32x32 and symmetric -- 208 opaque
/// pixels in its left half against 205 in its right. The scene files call it the "Yellow
/// Selection Arrows" and the art is indeed yellow, (255,254,0).
///
/// ⚠ BUT IT DOES NOT RENDER YELLOW. Measured off master's screenshot of the real Drinks Shop, the
/// Sale Price arrows sit at **(206,138,3)** -- the same orange as an unselected label. So the port
/// draws them in the label colour rather than the art's own. That is matched to the picture, not
/// decoded: which call tints them, and whether it is the same label-colour call, is unread.
///
/// ⚠⚠ THE SIZE IS ONLY PARTLY MEASURED, and the honest version is worth more than a confident
/// one. The scene gives the arrows a position and NO extent. On the reference a pair is 43 pixels
/// tall against a row pitch of 55-57, so it is about three quarters of a row -- and that ratio is
/// the one figure every attempt agreed on, because a ratio of two like things survives a bad
/// scale. The WIDTH did not converge: isolating one element by colour on a compressed screenshot
/// gave 3.139, 2.653 and 1.774 pixels per authored unit on three tries, so the width here is the
/// art's own aspect applied to that height rather than a measurement.
///
/// ⭐ THE ROW IS THE SPRITE'S BOTTOM, not its top. Every arrow element sits 12-13 units BELOW the
/// value it belongs to, so drawing downward from it puts the pair under the number -- master, on
/// the first version: "the thing misaligned were the arrows on prize and price for sideshows".
/// Drawn upward, a 24-tall pair spans row-24..row and the value 13 above the row sits level
/// inside it.</summary>
public static class LaptopArrows
{
    public const string Sprite = "/Arrow/UIarrow.ssh";
    /// <summary>About three quarters of a row tall, which is what the reference shows.</summary>
    public const int NativeHeight = 24;
    /// <summary>The art's aspect at that height: 32 wide over 30 opaque rows.</summary>
    public const int NativeWidth = 26;
    /// <summary>What the arrows must come out as, measured on the reference.</summary>
    public static readonly (byte R, byte G, byte B) Rendered = (206, 138, 3);
    /// <summary>The art's own colour, so a caller can work out its own modulate.</summary>
    public static readonly (byte R, byte G, byte B) Art = (255, 254, 0);
}

public enum LaptopRowKind
{
    /// <summary>A plain integer in the value column.</summary>
    Value,
    /// <summary>A currency amount.</summary>
    Money,
    /// <summary>A word, not a number -- "Unavailable", "1yr".</summary>
    Text,
    /// <summary>An orange frame with a cyan fill; full scale 100.</summary>
    Bar,
    /// <summary>A yellow track with a green knob.</summary>
    Slider,
}
