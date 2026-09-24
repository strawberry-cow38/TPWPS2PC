using Godot;
using System;
using System.Collections.Generic;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The shop's configuration screen -- what the game calls the **Laptop**.
///
/// ⭐⭐ THIS REPLACES A PANEL THAT WAS BUILT OUT OF THE WRONG FURNITURE. The first attempt drew
/// the right NUMBERS into a 260x180 message box made of `/messages/Mess*`, the same nine-slice the
/// little object menu uses. That is a message dialog. This screen is a full 512-wide laptop:
/// `Data/Ui/Laptop/laptop_&lt;world&gt;.ssh`, a photographic backdrop per world, with the shop's model
/// spinning in a window on the right. The executable names it itself -- `'Laptop is %s'`,
/// `hidden`/`visible` -- and every label resolves to a `STR_SINGLESHOP_*` text row.
///
/// ⭐⭐ NOTHING HERE IS POSITIONED BY HAND. The geometry is read at runtime from
/// `MENUS.WAD/main_i_shop_data.sce` via <see cref="SceneLayout"/>, because that file IS the
/// layout -- see the chain written up on <see cref="ShopScreen"/>. The executable's layout
/// globals read 0 in the image precisely because this file fills them at load, which is what
/// made them look unknowable the first time round.
///
/// ⭐ SCALE. Master's instruction was to measure percentages of screen space rather than bake
/// pixel counts, and the console authors in a 512x512 space (the chrome is 512x512 and the
/// scene's own extremes -- col 315+147=462, row 412 -- sit inside it). So everything below is in
/// those units and <see cref="Scale"/> converts once, against the live viewport. The laptop keeps
/// its authored proportions at any window size instead of stretching.</summary>
public sealed partial class LaptopShopScreen : Control
{
    /// <summary>The console's UI space. The chrome art is exactly this, and the scene file's
    /// coordinates are in the same units.</summary>
    public const float Native = 512f;

    readonly ImageTexture _chrome, _barFrame, _barCap, _barBody, _slideTrack, _slideKnob;
    readonly FontText _font;
    readonly SceneLayout _layout;
    readonly TextDatabase _text;
    readonly string _language;

    readonly List<(string Label, string Value, bool Highlight)> _rows = new();
    string _title = "";
    int _satisfaction, _quality, _additive, _price;
    bool _hasAdditive;
    ShopScreen.Selection _selected = ShopScreen.Selection.Quality;

    public bool Open { get; private set; }

    /// <summary>A fraction of the viewport, never a pixel count. ⚠ The smaller of the two ratios:
    /// scaling each axis independently would stretch a 512x512 laptop into the window's aspect.</summary>
    float Scale => Mathf.Max(0.05f, Mathf.Min(GetViewportRect().Size.X, GetViewportRect().Size.Y) / Native);

    /// <summary>Top-left of the laptop in screen pixels, centred in the viewport.</summary>
    Vector2 Origin => (GetViewportRect().Size - new Vector2(Native, Native) * Scale) / 2f;

    LaptopShopScreen(ImageTexture chrome, ImageTexture barFrame, ImageTexture barCap, ImageTexture barBody,
                     ImageTexture slideTrack, ImageTexture slideKnob,
                     SceneLayout layout, FontText font, TextDatabase text, string language)
    {
        _chrome = chrome; _barFrame = barFrame; _barCap = barCap; _barBody = barBody;
        _slideTrack = slideTrack; _slideKnob = slideKnob;
        _layout = layout; _font = font; _text = text; _language = language;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        ZIndex = 110;
    }

    /// <summary>Build it from the disc, or answer null when a piece is missing.
    /// ⚠ Answering null rather than substituting: a laptop drawn without its own chrome would be
    /// the same mistake this screen exists to correct.</summary>
    public static LaptopShopScreen Create(AssetLibrary lib, FontText font, TextDatabase text,
                                          string world, string language = "eng")
    {
        if (lib == null || font == null) return null;
        ImageTexture Load(string name)
        {
            var raw = lib.ReadUi(name);
            if (raw == null) return null;
            try
            {
                var ssh = new Ssh(raw);
                return ImageTexture.CreateFromImage(
                    Image.CreateFromData(ssh.Width, ssh.Height, false, Image.Format.Rgba8, ssh.Pixels));
            }
            catch (Exception ex) { GD.PrintErr($"[laptop] {name}: {ex.Message}"); return null; }
        }

        var sce = lib.ReadMenu(ShopScreen.SceneFile);
        if (sce == null) { GD.PrintErr($"[laptop] MENUS.WAD/{ShopScreen.SceneFile} missing -- screen stays off"); return null; }
        var layout = SceneLayout.Parse(sce);

        var chrome = Load(ShopScreen.ChromeFor(world)) ?? Load("/laptop/LAPTOP_512.ssh");
        // ⭐ BARPROG, not PROG_BAR: the smooth trough, measured to have the same continuous
        // interior as BARSLIDE. PROG_BAR is an eleven-cell notched gauge and belongs to a
        // different widget -- see DrawBar.
        var barFrame = Load("/laptop/BARPROG.ssh");
        var barCap = Load("/laptop/PROG_CBIT.ssh");
        var barBody = Load("/laptop/PROG_VBIT.ssh");
        var track = Load("/laptop/BARSLIDE.ssh");
        var knob = Load("/laptop/BARKNOB.ssh");
        if (chrome == null || barFrame == null || track == null || knob == null)
        {
            GD.PrintErr("[laptop] UI.WAD laptop art missing -- screen stays off");
            return null;
        }
        return new LaptopShopScreen(chrome, barFrame, barCap, barBody, track, knob, layout, font, text, language);
    }

    /// <summary>Put the screen up for a shop.
    /// ⭐ The row order is the draw function's, by text id; nothing here is arranged by taste.</summary>
    public void ShowFor(ParkRide shop, string displayName)
    {
        _rows.Clear();
        _title = displayName ?? shop?.Name ?? "";
        if (shop == null) { Hide(); return; }

        var settings = shop.Definition?.CompiledEntry?.Shop;
        _price = settings?.InitialPrice ?? 0;
        int baseCost = settings?.BaseCostOfGoods ?? 0;
        _quality = shop.Quality;
        _additive = shop.Setting0xAC;
        _satisfaction = 0; // ⚠ shop[0xb0]/[0xb4] is decoded but not yet tracked; see below.

        // ⭐ The console's own arithmetic, from the purchase path: the cost moves with Quality and
        // with the additive, both quartered.
        int cost = baseCost * ((_quality >> 2) + 75 - (_additive >> 2)) / 100;

        var payload = shop.Definition?.CompiledEntry?.Payload ?? default;
        int ingredient = payload.Length > 0 ? ShopIngredient.IdFrom(payload.Span) : -1;
        int ingredientText = ingredient >= 0 ? ShopIngredient.TextIdFor(ingredient) : 0;
        _hasAdditive = ingredientText != 0;

        string[] values = { shop.Customers.ToString(), Money(cost), Money(shop.Takings), Money(shop.Profit) };

        for (int i = 0; i < ShopScreen.LabelKeys.Length; i++)
        {
            // ⭐⭐ THE INGREDIENT ROW KEEPS ITS SLOT WHEN THE SHOP HAS NO INGREDIENT. In
            // `FUN_001d70c8` the label is inside `if (FUN_001d1f60(shop) != 0)` but the row step
            // that follows it is OUTSIDE -- so a shop with no additive leaves a GAP and Sale Price
            // stays on row 8. Dropping the entry instead would slide Sale Price up one row, which
            // is a whole 32 units and would have looked like a layout bug rather than an off-by-one.
            bool blank = i == ShopScreen.IngredientRow && !_hasAdditive;
            int id = i == ShopScreen.IngredientRow ? ingredientText
                   : ShopScreen.LabelTextIds[i > ShopScreen.IngredientRow ? i - 1 : i];
            string label = blank ? null : Row(id) ?? FallbackLabel(i, ingredient);
            string value = i < ShopScreen.NumericRows ? values[i] : null;
            _rows.Add((label, value, Highlighted(i)));
        }

        Open = true; Visible = true;
        QueueRedraw();
    }

    /// <summary>The yellow row. ⭐ `*(screen + 0x9cc)` picks it, and it is the SAME index that
    /// decides which slider is live -- so this is one piece of state, not two.</summary>
    bool Highlighted(int row) => _selected switch
    {
        ShopScreen.Selection.Quality => row == 5,
        ShopScreen.Selection.Additive => row == ShopScreen.IngredientRow,
        _ => row == ShopScreen.LabelKeys.Length - 1,
    };

    /// <summary>Move the selection, skipping the additive row on a shop that has none --
    /// which is the same condition that stops its slider being drawn.</summary>
    public void Select(ShopScreen.Selection selection)
    {
        if (selection == ShopScreen.Selection.Additive && !_hasAdditive) return;
        _selected = selection;
        // Recompute, rather than clear: the highlight is a function of the selection, and the row
        // list keeps every slot so the index here is the same one `Highlighted` was built against.
        for (int i = 0; i < _rows.Count; i++) _rows[i] = (_rows[i].Label, _rows[i].Value, Highlighted(i));
        QueueRedraw();
    }

    string Row(int id) => id <= 0 ? null : _text?.Text(_language, id);

    /// <summary>⚠ Only for a disc whose text tree would not load. The real answer is the text
    /// table, in the player's own language; this exists so a missing table shows the screen with
    /// English words rather than eight blank rows that look like a layout bug.</summary>
    static string FallbackLabel(int row, int ingredient) => row switch
    {
        0 => "Customers", 1 => "Cost of Goods", 2 => "Takings", 3 => "Profit",
        4 => "Satisfaction", 5 => "Quality",
        ShopScreen.IngredientRow => ShopIngredient.CaptionOf(ingredient) ?? "",
        _ => "Sale Price",
    };

    public new void Hide() { Open = false; Visible = false; _rows.Clear(); QueueRedraw(); }

    static string Money(int v) => v < 0 ? $"-${-v:N0}" : $"${v:N0}";

    static Color Of((byte R, byte G, byte B) c) => Color.Color8(c.R, c.G, c.B);

    public override void _Draw()
    {
        if (!Open) return;
        float s = Scale;
        var o = Origin;
        DrawTextureRect(_chrome, new Rect2(o, new Vector2(Native, Native) * s), false);

        Vector2 At(SceneLayout.Element e) => o + new Vector2(e.X, e.Y) * s;

        if (_layout["ItemText"] is { } title)
            DrawRun(_title, At(title), s, Of(ShopScreen.Highlight), title.Justify);

        // ⭐ Labels and values are two independent columns that step together: the draw advances
        // both by the same `RowStep` and they start on the same row.
        var labels = _layout["TextOptions"];
        var numbers = _layout["NumericItems"];
        for (int i = 0; i < _rows.Count; i++)
        {
            var (label, value, hot) = _rows[i];
            float dy = ShopScreen.RowStep * i * s;
            if (labels is { } l)
                DrawRun(label, At(l) + new Vector2(0, dy), s,
                        Of(hot ? ShopScreen.Highlight : ShopScreen.Label), l.Justify);
            if (value != null && numbers is { } n)
                DrawRun(value, At(n) + new Vector2(0, dy), s, Of(ShopScreen.Highlight), n.Justify);
        }

        if (_layout["SatisfactionBar"] is { } bar)
            DrawBar(new Rect2(At(bar), new Vector2(bar.Width, bar.Height) * s), _satisfaction, s);
        if (_layout["QualitySlider"] is { } quality)
            DrawSlider(new Rect2(At(quality), new Vector2(quality.Width, quality.Height) * s), _quality, s);
        if (_hasAdditive && _layout["AdditiveSlider"] is { } additive)
            DrawSlider(new Rect2(At(additive), new Vector2(additive.Width, additive.Height) * s), _additive, s);

        if (_layout["CostItem"] is { } cost)
            DrawRun(Money(_price), At(cost), s,
                    Of(_selected == ShopScreen.Selection.SalePrice ? ShopScreen.Highlight : ShopScreen.Label),
                    cost.Justify);
    }

    /// <summary>One run, honouring the justification the scene file authored for that element.
    /// ⚠ `justify=center` in these files means centred ON the authored column, which is why the
    /// x is shifted by half the measured width rather than centred inside some box: no box is
    /// authored for a text element.</summary>
    void DrawRun(string text, Vector2 at, float s, Color tint, string justify)
    {
        if (string.IsNullOrEmpty(text)) return;
        var tex = _font.Render(text);
        if (tex == null) return;
        float w = tex.GetWidth() * s;
        float x = justify != null && justify.StartsWith("cent", StringComparison.OrdinalIgnoreCase) ? at.X - w / 2f
                : justify != null && justify.StartsWith("right", StringComparison.OrdinalIgnoreCase) ? at.X - w
                : at.X;
        DrawTextureRect(tex, new Rect2(new Vector2(x, at.Y), new Vector2(w, tex.GetHeight() * s)), false, tint);
    }

    /// <summary>The satisfaction bar: `BARPROG`'s smooth trough, filled with `PROG_CBIT`'s rounded
    /// cap followed by `PROG_VBIT` tiles.
    ///
    /// ⚠⚠ THIS WAS `PROG_BAR` (the NOTCHED frame) AND THAT WAS WRONG. Master, on the first
    /// version: "im not sure if the satisfaction bar is meant to be notched" -- then, on my A/B:
    /// "its pretty messed up. check again". Both were right, for two separate reasons:
    ///
    /// ⭐ The frames are structurally different, measured off their own alpha at the middle row.
    /// `BARPROG`'s interior is ONE continuous run, columns 3..124 -- and `BARSLIDE`'s is the
    /// identical run, which is what makes them a matched pair (one takes a fill, one a knob).
    /// `PROG_BAR`'s interior is ELEVEN separate cells at a pitch of 11, each 10 wide. A tiled
    /// fill cannot go in an eleven-cell gauge; those cells want one sprite each.
    ///
    /// ⚠ And the fill sprites are NOT 16 wide despite being 16-wide images: `PROG_CBIT` is opaque
    /// only over columns 0..9 (a rounded cap) and `PROG_WBIT` over 0..12. Stretching a 10-wide cap
    /// across a 16-wide slot -- which is what the first attempt did -- leaves its empty tail
    /// showing as a gap, which is the "messed up" master saw. Each bit is drawn at its OWN width
    /// and the SOURCE is clipped when the fill ends mid-tile; the destination is never squashed.</summary>
    void DrawBar(Rect2 r, int value, float s)
    {
        // Measured off the art: 128 columns, trough interior 3..124, cap 10 wide, body 16.
        const float ArtWidth = 128f, InnerX = 3f, InnerWidth = 122f, CapCols = 10f, BitCols = 16f;
        float unit = r.Size.X / ArtWidth;
        float fraction = Mathf.Clamp(value / (float)ShopScreen.SatisfactionMax, 0f, 1f);
        float filled = InnerWidth * fraction;
        if (_barCap != null && _barBody != null)
            for (float at = 0f; at < filled; )
            {
                bool cap = at <= 0f;
                var tex = cap ? _barCap : _barBody;
                float cols = cap ? CapCols : BitCols;
                float take = Mathf.Min(cols, filled - at);
                DrawTextureRectRegion(tex,
                    new Rect2(r.Position.X + (InnerX + at) * unit, r.Position.Y, take * unit, r.Size.Y),
                    new Rect2(0, 0, take, tex.GetHeight()));
                at += cols;
            }
        DrawTextureRect(_barFrame, r, false);
    }

    /// <summary>A slider: the yellow track with the gold knob riding it.
    /// ⚠ The knob art is square (32x32) and the track 128x32, so the knob is drawn at the track's
    /// HEIGHT rather than a share of its width -- scaling it by the track's aspect would squash it.</summary>
    void DrawSlider(Rect2 r, int value, float s)
    {
        DrawTextureRect(_slideTrack, r, false);
        float fraction = Mathf.Clamp(value / (float)ShopScreen.SatisfactionMax, 0f, 1f);
        float d = r.Size.Y;
        float x = r.Position.X + (r.Size.X - d) * fraction;
        DrawTextureRect(_slideKnob, new Rect2(new Vector2(x, r.Position.Y), new Vector2(d, d)), false);
    }

    /// <summary>The scene's own frame for an element, for a caller that places something this
    /// screen does not draw -- the 3D model window in particular, which is a viewport and not a
    /// sprite. ⭐ `Model` is authored at 147x240 at col 315, i.e. the right column BELOW the
    /// chrome's notch.</summary>
    public Rect2? Frame(string element)
    {
        if (_layout[element] is not { } e) return null;
        float s = Scale;
        return new Rect2(Origin + new Vector2(e.X, e.Y) * s, new Vector2(e.Width, e.Height) * s);
    }
}
