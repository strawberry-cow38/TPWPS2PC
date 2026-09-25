namespace TPW.PS2.Data;

/// <summary>The "Single Shop" laptop screen, as the console draws it.
///
/// ⭐⭐ EVERY NUMBER HERE IS READ. The chain is `main_i_shop_data.sce` (MENUS.WAD) -> the binder
/// `FUN_001d68b0` (menu id 0x11) -> globals `0x2e9c48..0x2e9cec` -> the draw `FUN_001d70c8`.
/// This file holds only what the EXECUTABLE contributes; the geometry is not here because the
/// geometry is not in the executable -- it is in the `.sce`, and <see cref="SceneLayout"/> reads
/// it from the disc rather than restating it.
///
/// ⭐ The game's own name for the screen is in the text keys: every label resolves to a
/// `STR_SINGLESHOP_*` row.</summary>
public static class ShopScreen
{
    /// <summary>The scene file that authors this screen's layout.</summary>
    public const string SceneFile = "main_i_shop_data.sce";

    /// <summary>⭐⭐ The row step, in the `.sce`'s own units: `DAT_002e9ca8`, added after every
    /// label and every value in `FUN_001d70c8`.
    ///
    /// This one is a CONSTANT IN THE IMAGE, not a runtime global, and the distinction is the
    /// evidence. The layout globals beside it (`0x2e9c48`, `0x2e9c4c`, `0x2e9c60`, `0x2e9c64`)
    /// all read 0 in the file because the `.sce` binder writes them at load. `0x2e9ca8` reads
    /// **32** and a cross-reference sweep finds **no store to it anywhere** -- a negative given a
    /// control (`0x2e9c48`, whose store the same sweep does find) so it is a real negative.
    ///
    /// ⭐ And it is corroborated ACROSS FILES. Stepping the eight label rows from the `.sce`'s
    /// first row (175) by 32 predicts 303 / 335 / 367 / 399 for the four rows that also have a
    /// widget; the `.sce` independently places those widgets at 305 / 338 / 370 / 400. Two
    /// different files on the disc agreeing within 3px is why this is 32 and not a fitted value.
    /// (The small positive deltas are a 22-high widget being centred against a text baseline.)</summary>
    public const int RowStep = 32;

    /// <summary>The satisfaction bar's full-scale value: `*(screen + 0x9fc) = 100`.</summary>
    public const int SatisfactionMax = 100;

    /// <summary>Text-table rows for the label column, in the order `FUN_001d70c8` draws them.
    /// Row 7 is absent here because it is not a constant: it is
    /// <see cref="ShopIngredient"/>'s id, read per shop, and skipped when it is 0.</summary>
    public static readonly int[] LabelTextIds = { 82, 17, 106, 986, 1074, 312, /* ingredient */ 594 };

    /// <summary>The same rows by symbolic key, for a caller that would rather not trust an index
    /// -- and, in a test, as the CHECK that the indices above are the rows they are claimed to be.
    /// ⚠ Index 6 is the ingredient slot: it has no constant key, and this array holds null there
    /// so the two arrays stay index-aligned instead of silently shifting by one.</summary>
    public static readonly string[] LabelKeys =
    {
        "STR_SINGLESHOP_CUSTOMERS",
        "STR_SINGLESHOP_COST_OF_GOODS",
        "STR_SINGLESHOP_MONTHLY_TAKINGS",
        "STR_SINGLESHOP_MONTHLY_PROFIT",
        "STR_SINGLESHOP_CUSTOMER_SATISFACTION",
        "STR_SINGLESHOP_QUALITY_OF_GOODS",
        null, // the ingredient row: Fat / Ice / Sugar / Salt, per shop
        "STR_SINGLESHOP_SALE_PRICE",
    };

    /// <summary>Where the ingredient row sits once it is present.</summary>
    public const int IngredientRow = 6;

    /// <summary>How many of the rows carry a number in the value column. ⭐ Only the first four:
    /// the draw calls the text writer four times against `NumericItems`, and the `.sce`'s own
    /// comment says "Customers/CostofGoods/Takings/Profit - int/$/$/$". The rows below those are
    /// a bar, two sliders and the price widget, none of which is text.</summary>
    public const int NumericRows = 4;

    /// <summary>⭐ The first value is an INTEGER and the other three are MONEY. The draw uses a
    /// different formatter for it (`FUN_00142b68`) than for the rest (`FUN_00142908`), which is
    /// the same split the scene file's comment states.</summary>
    public static bool IsMoneyRow(int row) => row is >= 1 and < NumericRows;

    /// <summary>Text colours, from `FUN_001388e8(ctx, r, g, b)` in the draw.
    /// ⚠ There are exactly TWO: there is no separate "disabled" or "heading" colour, and the
    /// title uses the same yellow as a selected row.</summary>
    public static readonly (byte R, byte G, byte B) Highlight = (255, 255, 0);
    public static readonly (byte R, byte G, byte B) Label = (200, 130, 0);

    /// <summary>⭐⭐ THE SLIDER IS TINTED GREEN, and its own art is gold. Master, looking at the
    /// render: "why is our drag slider 'knob' orange? should it be green?" -- it should.
    ///
    /// The slider's constructor `FUN_001DA630` installs four colour pointers, and `+0xB0`, the one
    /// `FUN_001DAA88` copies into the drawn sprite, is `0x2E9E18` = **(54, 249, 77)**. The others
    /// are `0x2E9E10` (85,140,160), `0x2E9E20` (255,64,16) and `0x2E9E28` (13,62,19).
    ///
    /// ⭐ Static, not runtime: a cross-reference sweep of `0x2E9E10..0x2E9E30` finds only the four
    /// address materialisations in that constructor and NO store -- and the sweep's control, a
    /// nearby address that IS written, fired. So the image is the authority here.
    ///
    /// ⚠⚠ AND 0x80 IS UNITY, NOT 0xFF. The GS texture MODULATE is `(texel * colour) >> 7`, so a
    /// component of 128 leaves the texel alone. The engine's own neutral call agrees:
    /// `FUN_00214180(sprite, 0x60808080)` in `FUN_00214C20` sets a plain grey 0x80 per channel.
    /// ⚠ What that choice decides is BRIGHTNESS, not hue -- a correction to an earlier draft of
    /// this comment. The tint's green component dwarfs its red and blue, so the knob reads green
    /// at either unity; 255-unity gives (54,206,44) against 0x80-unity's (107,255,87). Only the
    /// correct 0x80 drives green to saturation, which is what the shipped render shows.</summary>
    public static readonly (byte R, byte G, byte B) SliderTint = (54, 249, 77);

    /// <summary>What a tint component of "no change" is on this hardware.</summary>
    public const float TintUnity = 128f;

    /// <summary>Which row the player is adjusting: `*(screen + 0x9cc)`. It selects BOTH the
    /// yellow label and the live slider -- 0 drives the quality slider, 1 the additive slider,
    /// and the sale-price arrows take the remaining case.</summary>
    public enum Selection { Quality = 0, Additive = 1, SalePrice = 2 }

    /// <summary>The per-world laptop chrome, named by `Data/Ui/Laptop/laptop_&lt;world&gt;.ssh` in the
    /// executable and loaded four-at-a-time by `FUN_00214c20`.
    /// ⚠ `LAPTOP_512` is NOT in that list; it is the art the archive also ships, and this port
    /// uses it only when a world has no chrome of its own.</summary>
    public static string ChromeFor(string world)
    {
        string w = (world ?? "").ToUpperInvariant();
        foreach (var known in new[] { "JUNGLE", "HALLOW", "FANTASY", "SPACE" })
            if (w.Contains(known)) return $"/laptop/LAPTOP_{known}.ssh";
        return "/laptop/LAPTOP_512.ssh";
    }
}
