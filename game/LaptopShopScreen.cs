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

    readonly ImageTexture _chrome, _barFrame, _barFill, _slideTrack, _slideKnob;
    ImageTexture _arrows;
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

    LaptopShopScreen(ImageTexture chrome, ImageTexture barFrame, ImageTexture barFill,
                     ImageTexture slideTrack, ImageTexture slideKnob,
                     SceneLayout layout, FontText font, TextDatabase text, string language)
    {
        _chrome = chrome; _barFrame = barFrame; _barFill = barFill;
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

        // ⚠ The mask must come from the TGA beside the art that ACTUALLY loaded. Falling back to
        // LAPTOP_512 while still naming the world's file would cut one chrome to another's outline
        // -- and LAPTOP_512's outline differs (inset 11/11/16/17 against the worlds' 12/13/17/18).
        string chromeName = ShopScreen.ChromeFor(world);
        var chrome = Load(chromeName);
        if (chrome == null) { chromeName = "/laptop/LAPTOP_512.ssh"; chrome = Load(chromeName); }
        if (chrome != null) chrome = TrimRim(chrome, lib, chromeName);
        // ⭐ BARPROG, not PROG_BAR: the smooth trough, measured to have the same continuous
        // interior as BARSLIDE. PROG_BAR is an eleven-cell notched gauge and belongs to a
        // different widget -- see DrawBar.
        var barFrame = Load("/laptop/BARPROG.ssh");
        var barFill = BuildFill(lib);
        var track = Load("/laptop/BARSLIDE.ssh");
        var knob = Load("/laptop/BARKNOB.ssh");
        if (chrome == null || barFrame == null || track == null || knob == null)
        {
            GD.PrintErr("[laptop] UI.WAD laptop art missing -- screen stays off");
            return null;
        }
        var screen = new LaptopShopScreen(chrome, barFrame, barFill, track, knob, layout, font, text, language);
        screen._lib = lib;                 // for the other screens' scene files, read on demand
        screen._arrows = Load(LaptopArrows.Sprite);
        screen._layouts[ShopScreen.SceneFile] = layout;
        return screen;
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

    public new void Hide() { Open = false; Visible = false; _rows.Clear(); _spec = null; _menu.Clear(); QueueRedraw(); }

    // ---- the general path: any of the three info screens --------------------------------------

    /// <summary>The info screen's model, rendered by the viewer into its own viewport. Null
    /// until one is built; the screen simply leaves the window empty then.</summary>
    public Texture2D ModelTexture { get; set; }

    LaptopScreen _spec;
    readonly Dictionary<string, SceneLayout> _layouts = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(string Text, int Fraction)> _cells = new();
    AssetLibrary _lib;

    /// <summary>⭐⭐ ONE RENDERER FOR THE SHOP, THE RIDE AND THE SIDESHOW, because the console
    /// has one: all three bind their own element list out of their own `.sce` and then draw with
    /// the SAME widget calls. The difference between them is <see cref="LaptopScreen"/> data.
    ///
    /// <paramref name="cells"/> is one entry per row of the spec: the text to put in the value
    /// column for a text row, or the 0..100 fraction for a bar or slider. A null text on a row
    /// leaves that row blank, which is how the shop's ingredient row disappears while keeping
    /// its slot.</summary>
    public void ShowScreen(LaptopScreen spec, string title, IReadOnlyList<(string Text, int Fraction)> cells)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _title = title ?? "";
        _rows.Clear();
        _cells.Clear();
        if (cells != null) _cells.AddRange(cells);
        Open = true; Visible = true;
        QueueRedraw();
    }

    /// <summary>⭐⭐ THE LAPTOP'S MAIN MENU -- a list, not a data screen. `main.sce` has exactly one
    /// element, `textoptions` at row 115 col 45 left-justified, so there is no title row, no value
    /// column and no model window to draw; the options ARE the screen.
    ///
    /// The option order and text ids come from the console's own table at `0x2b97c0`; see
    /// <see cref="LaptopMainMenu"/>, which holds the reading.</summary>
    public void ShowMenu(IReadOnlyList<string> options, int selected)
    {
        _spec = null;
        _menu.Clear();
        if (options != null) _menu.AddRange(options);
        _menuSelected = selected;
        Open = true; Visible = true;
        QueueRedraw();
    }

    readonly List<string> _menu = new();
    int _menuSelected;

    /// <summary>⭐ The menu's own draw. It steps the SAME 32 the info screens do -- `DAT_002e9ca8`
    /// -- and uses the same two colours, yellow for the row under the cursor and orange for the
    /// rest, because the laptop has exactly two text colours and no third "disabled" one.</summary>
    void DrawMenu(float s, Vector2 o)
    {
        var layout = LayoutFor(LaptopMainMenu.SceneFile);
        if (layout[LaptopMainMenu.ListElement] is not { } list) return;
        var at = o + new Vector2(list.X, list.Y) * s;
        for (int i = 0; i < _menu.Count; i++)
        {
            var colour = Of(i == _menuSelected ? ShopScreen.Highlight : ShopScreen.Label);
            DrawRun(_menu[i], at + new Vector2(0, LaptopMainMenu.RowStep * i * s), s, colour, list.Justify);
        }
    }

    /// <summary>The layout for a screen, read from `MENUS.WAD` the first time it is asked for.
    /// ⚠ Each screen has its OWN scene file; they are not variations on one layout.</summary>
    SceneLayout LayoutFor(LaptopScreen spec) => LayoutFor(spec.SceneFile);

    /// <summary>⭐ By scene FILE, because the main menu has no <see cref="LaptopScreen"/> spec --
    /// it is a list, not a data screen, so there is nothing for a spec to describe.</summary>
    SceneLayout LayoutFor(string sceneFile)
    {
        if (_layouts.TryGetValue(sceneFile, out var had)) return had;
        var raw = _lib?.ReadMenu(sceneFile);
        var made = raw == null ? SceneLayout.Parse("") : SceneLayout.Parse(raw);
        if (raw == null) GD.PrintErr($"[laptop] MENUS.WAD/{sceneFile} missing -- {sceneFile} draws empty");
        _layouts[sceneFile] = made;
        return made;
    }

    void DrawSpecScreen(float s, Vector2 o)
    {
        var layout = LayoutFor(_spec);
        Vector2 At(SceneLayout.Element e) => o + new Vector2(e.X, e.Y) * s;

        if (layout[_spec.TitleElement] is { } title)
            DrawRun(_title, At(title), s, Of(ShopScreen.Highlight), title.Justify);

        // ⭐ The model window. Drawn before the rows so nothing it overlaps can be hidden by it.
        if (ModelTexture != null && layout[_spec.ModelElement] is { } window)
            DrawTextureRect(ModelTexture,
                new Rect2(At(window), new Vector2(window.Width, window.Height) * s), false);

        var labels = layout[_spec.LabelElement];
        var values = layout[_spec.ValueElement];
        for (int i = 0; i < _spec.Rows.Count; i++)
        {
            var row = _spec.Rows[i];
            var (text, fraction) = i < _cells.Count ? _cells[i] : (null, 0);
            float dy = LaptopScreen.RowStep * i * s;

            string label = Row(row.TextId);
            if (labels is { } l && label != null)
                DrawRun(label, At(l) + new Vector2(0, dy), s, Of(ShopScreen.Label), l.Justify);

            // ⭐ A widget sits at ITS OWN element's row, not on the label grid. The ride screen
            // places its seven widgets at 118/150/182/214/246/280/310 -- 32 apart for the bars and
            // then 34 and 30 -- so stepping them with the labels would drift by the third slider.
            if (row.Kind is LaptopRowKind.Bar or LaptopRowKind.Slider)
            {
                if (row.Element == null || layout[row.Element] is not { } w) continue;
                var rect = new Rect2(At(w), new Vector2(w.Width, w.Height) * s);
                if (row.Kind == LaptopRowKind.Bar) DrawBar(rect, fraction, s);
                else DrawSlider(rect, fraction, s, selected: false);
                continue;
            }

            // ⭐ The nudge arrows, for a row whose value the player can change. They are drawn
            // whether or not the row has text this frame, because they belong to the row.
            if (row.ArrowElement != null && _arrows != null && layout[row.ArrowElement] is { } arrow)
            {
                // ⚠ Modulate so the YELLOW art lands on the orange the real screen shows; see
                // LaptopArrows. Godot multiplies, so the factor is rendered/art per channel.
                var want = new Color(LaptopArrows.Rendered.R / (float)LaptopArrows.Art.R,
                                     LaptopArrows.Rendered.G / (float)LaptopArrows.Art.G,
                                     LaptopArrows.Rendered.B / 255f);
                // ⚠⚠ THE ELEMENT'S ROW IS THE SPRITE'S CENTRE, and getting this wrong twice is
                // what master saw: "the thing misaligned were the arrows on prize and price for
                // sideshows". Derived rather than nudged -- a label row is the TEXT'S TOP and
                // Large.bff advances 30, so a line's middle is row + 15; the arrow elements sit
                // at 353 and 384 against labels at 339 and 371, i.e. 14 and 13 below. That is the
                // half-line, so the row is where the arrow's middle goes.
                var size = new Vector2(LaptopArrows.NativeWidth, LaptopArrows.NativeHeight) * s;
                DrawTextureRect(_arrows, new Rect2(At(arrow) - new Vector2(0, size.Y / 2f), size), false, want);
            }

            if (text == null) continue;
            // A row with its own value element uses it; otherwise the shared value column, at the
            // label's height.
            if (row.Element != null && layout[row.Element] is { } own)
                DrawRun(text, At(own), s, Of(ShopScreen.Highlight), own.Justify);
            else if (values is { } v && labels is { } lab)
                // ⚠ THE VALUE COLUMN CONTRIBUTES ITS X, AND THE LABEL ITS Y. On the shop the two
                // elements share a row (both 175) so either reading works; on the ride they do
                // NOT -- its value elements sit at 338/400/436 against labels from 115 -- and
                // taking the value element's row as a baseline threw the text off the screen.
                DrawRun(text, new Vector2(At(v).X, At(lab).Y + dy), s, Of(ShopScreen.Highlight), v.Justify);
        }
    }

    static string Money(int v) => v < 0 ? $"-${-v:N0}" : $"${v:N0}";

    static Color Of((byte R, byte G, byte B) c) => Color.Color8(c.R, c.G, c.B);

    public override void _Draw()
    {
        if (!Open) return;
        float s = Scale;
        var o = Origin;
        DrawTextureRect(_chrome, new Rect2(o, new Vector2(Native, Native) * s), false);
        if (_spec != null) { DrawSpecScreen(s, o); return; }
        if (_menu.Count > 0) { DrawMenu(s, o); return; }

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
            DrawSlider(new Rect2(At(quality), new Vector2(quality.Width, quality.Height) * s), _quality, s,
                       _selected == ShopScreen.Selection.Quality);
        if (_hasAdditive && _layout["AdditiveSlider"] is { } additive)
            DrawSlider(new Rect2(At(additive), new Vector2(additive.Width, additive.Height) * s), _additive, s,
                       _selected == ShopScreen.Selection.Additive);

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


    /// <summary>Compose the satisfaction bar's fill: the trough's own INTERIOR shape, painted with
    /// the fill sprite's vertical gradient.
    ///
    /// ⭐⭐ THIS IS A DELIBERATE IMPROVEMENT ON THE CONSOLE, at master's direction: "i genuinely
    /// have no idea why they made the bar act this way. even in the real game, the low and high end
    /// 'snap' instead of moving. this would be an improvement." The console builds the fill from
    /// sprites -- `PROG_CBIT` as a fixed 10-wide rounded cap and `PROG_VBIT` as a 16-wide tile -- so
    /// the rounded ends appear and disappear in whole-sprite steps rather than following the curve.
    /// Everywhere else this port copies the console bug-for-bug; this is an exception master asked
    /// for, and it is flagged rather than quietly "corrected".
    ///
    /// ⭐ The two pieces MEASURE as made for each other, which is why this reconstructs the intent
    /// rather than inventing a look: the trough's interior occupies rows 3..28, and `PROG_VBIT`'s
    /// cyan gradient occupies exactly rows 3..28 too (its rows 0..2 and 29..31 are the orange inner
    /// edge, which the mask drops). The gradient runs top-to-bottom, (5,223,211) to (5,147,225).
    ///
    /// ⚠ The mask is the interior, NOT simply "transparent": the area OUTSIDE the frame is equally
    /// transparent. It is found by flooding inwards from the border and keeping what the flood
    /// cannot reach, so the rounded ends come out of the art instead of being modelled.</summary>
    static ImageTexture BuildFill(AssetLibrary lib)
    {
        var px = BuildFillPixels(lib, out int fw, out int fh);
        return px == null ? null
             : ImageTexture.CreateFromImage(Image.CreateFromData(fw, fh, false, Image.Format.Rgba8, px));
    }

    /// <summary>The composed fill as raw RGBA, so an audit can assert on what actually ships
    /// rather than re-deriving the mask and drifting from it.
    /// ⚠ It reads the `.ssh`, NOT the `.tga` sibling: those are lossy-compressed and differ by a
    /// few levels, which is enough to move a "is this pixel cyan" row boundary by one.</summary>
    /// <summary>⭐⭐ CUT THE SURROUND OFF THE CHROME so the park shows through around the panel.
    /// Master: "cut out the darker border around the rounded edges of the background image.
    /// instead of a gray background, just dont hide the park behind".
    ///
    /// ⚠⚠ THE FIRST VERSION WAS A MAGIC ERASER AND MASTER REJECTED IT: "id do a measured version.
    /// magic eraser just eats too much." It flooded inward over anything darker than luma 24. That
    /// cleared **13.9%** of the image against a true surround of **12.0%** -- it ate ~2% of the
    /// bevel's antialiasing, exactly the complaint.
    ///
    /// ⭐⭐ WHAT THE ART ACTUALLY IS, measured on the lossless TGAs: the surround is one EXACT flat
    /// colour, **(0,0,100)**, and all four world chromes agree on both that colour and its area --
    /// 31,522 / 31,524 / 31,522 / 31,522 pixels. Four independently-skinned images agreeing to
    /// within two pixels is a measurement, not a fit. (`LAPTOP_512`, the fallback art, uses
    /// (0,0,150) over 29,250.) So there is no threshold to choose: the boundary is exactly where
    /// the pixel stops being the key colour.
    ///
    /// ⚠ BUT THE COLOUR IS NOT THE TEST ON ITS OWN. Two pixels of the exact key sit STRANDED
    /// INSIDE the panel on `LAPTOP_HALLOW` and on `LAPTOP_512`, so a plain colour match punches
    /// two transparent pinholes in them. The flood from the border is what excludes those, and it
    /// reaches 31,522 on every world.
    ///
    /// ⚠⚠ AND THE MASK CANNOT COME FROM THE `.ssh`, which is why this reads the `.tga`. The
    /// shipped SSH is lossy: over the pixels the TGA says are exactly the key, the decoded values
    /// drift by up to **66**, while the nearest NON-surround pixel is **1** away. The two
    /// populations OVERLAP in colour space, so no tolerance on the decoded image can separate
    /// them. Nor is the silhouette a rounded rectangle -- the best circular fit leaves a 4px
    /// residual and the four corners are not even identical -- so a radius would be the same
    /// fitting mistake in a different costume.
    ///
    /// ⭐ UI.WAD ships BOTH (79 `.tga` against 103 `.ssh`), so the lossless art is simply there at
    /// runtime. The chrome is still DRAWN from the `.ssh`, which is what the console displays; only
    /// the mask is read off the TGA, where it is exact.</summary>
    static ImageTexture TrimRim(ImageTexture tex, AssetLibrary lib, string sshName)
    {
        var img = tex.GetImage();
        if (img == null) return tex;
        byte[] raw = lib.ReadUi(System.IO.Path.ChangeExtension(sshName, ".tga"));
        if (raw == null) { GD.PrintErr($"[laptop] {sshName}: no .tga beside it -- chrome stays opaque"); return tex; }
        Targa art;
        try { art = new Targa(raw); }
        catch (Exception ex) { GD.PrintErr($"[laptop] chrome .tga: {ex.Message} -- chrome stays opaque"); return tex; }

        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        int w = img.GetWidth(), h = img.GetHeight();
        // ⚠ A mask of the wrong size would cut the wrong shape rather than fail, so refuse instead.
        if (art.Width != w || art.Height != h)
        {
            GD.PrintErr($"[laptop] chrome .tga is {art.Width}x{art.Height} but the .ssh is {w}x{h}"
                      + " -- chrome stays opaque rather than cut to the wrong outline");
            return tex;
        }

        var px = img.GetData();
        byte kr = art.Pixels[0], kg = art.Pixels[1], kb = art.Pixels[2];   // the corner IS the key
        bool Key(int i) => art.Pixels[i * 4] == kr && art.Pixels[i * 4 + 1] == kg && art.Pixels[i * 4 + 2] == kb;

        var seen = new bool[w * h];
        var queue = new Queue<int>();
        void Seed(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int i = y * w + x;
            if (seen[i] || !Key(i)) return;
            seen[i] = true; queue.Enqueue(i);
        }
        for (int x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
        for (int y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }
        int cleared = 0;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, y = i / w;
            px[i * 4 + 3] = 0; cleared++;
            Seed(x - 1, y); Seed(x + 1, y); Seed(x, y - 1); Seed(x, y + 1);
        }

        // ⭐ The control, and it is a real one: the flood must come in UNDER the raw colour count,
        // because the stranded interior pixels are exactly what it is there to exclude. Equal on
        // every world would mean the connectivity guard had never been exercised.
        int rawKey = 0;
        for (int i = 0; i < w * h; i++) if (Key(i)) rawKey++;
        GD.Print($"[laptop] chrome surround cut: key ({kr},{kg},{kb}), {cleared} of {w * h} px "
               + $"({100f * cleared / (w * h):F1}%); {rawKey - cleared} stranded interior px kept");
        return ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rgba8, px));
    }

    internal static byte[] BuildFillPixels(AssetLibrary lib, out int width, out int height)
    {
        width = height = 0;
        byte[] frameRaw = lib.ReadUi("/laptop/BARPROG.ssh"), bodyRaw = lib.ReadUi("/laptop/PROG_VBIT.ssh");
        if (frameRaw == null || bodyRaw == null) return null;
        try
        {
            var frame = new Ssh(frameRaw);
            var body = new Ssh(bodyRaw);
            int w = frame.Width, h = frame.Height;

            bool Clear(int x, int y) => frame.Pixels[(y * w + x) * 4 + 3] <= 16;
            var outside = new bool[w * h];
            var queue = new Queue<int>();
            void Seed(int x, int y)
            {
                if (!Clear(x, y) || outside[y * w + x]) return;
                outside[y * w + x] = true; queue.Enqueue(y * w + x);
            }
            for (int x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
            for (int y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }
            while (queue.Count > 0)
            {
                int at = queue.Dequeue(); int x = at % w, y = at / w;
                if (x > 0) Seed(x - 1, y);
                if (x < w - 1) Seed(x + 1, y);
                if (y > 0) Seed(x, y - 1);
                if (y < h - 1) Seed(x, y + 1);
            }

            // One column IS the gradient: it varies by ~250 down and ~7 across, so any column does.
            //
            // ⚠⚠ BUT NOT EVERY ROW OF IT. `PROG_VBIT` carries the trough's orange inner edge on its
            // first and last three rows, and SHPS is lossy: the decode bleeds that orange into the
            // cyan rows either side of it, so rows 3 and 28 come back muddy -- (122,184,97) and
            // (105,125,89) instead of (5,223,211) and (5,147,225). Those are exactly the rows the
            // mask needs, being the top and bottom of the trough, so the mud would land on the bar.
            // Rows whose blue does not beat their red are compression bleed, not gradient, and take
            // the nearest clean row instead.
            //
            // ⚠ I nearly missed this: a check that "the SSH decodes identically to the TGA" passed,
            // but the two converters both write `<name>.png` beside the source, so it had compared
            // the TGA against ITSELF. The audit below reads the composed pixels, which cannot be
            // fooled that way.
            int mid = body.Width / 2;
            bool CleanRow(int y)
            {
                int o = (y * body.Width + mid) * 4;
                return body.Pixels[o + 2] > body.Pixels[o];
            }
            var source = new int[body.Height];
            for (int y = 0; y < body.Height; y++)
            {
                source[y] = y;
                for (int d = 1; d < body.Height && !CleanRow(source[y]); d++)
                {
                    if (y - d >= 0 && CleanRow(y - d)) source[y] = y - d;
                    else if (y + d < body.Height && CleanRow(y + d)) source[y] = y + d;
                }
            }

            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int g = (source[Math.Min(y, body.Height - 1)] * body.Width + mid) * 4;
                for (int x = 0; x < w; x++)
                {
                    if (!Clear(x, y) || outside[y * w + x]) continue;   // frame, or outside it
                    int o = (y * w + x) * 4;
                    px[o] = body.Pixels[g]; px[o + 1] = body.Pixels[g + 1];
                    px[o + 2] = body.Pixels[g + 2]; px[o + 3] = 255;
                }
            }
            width = w; height = h;
            return px;
        }
        catch (Exception ex) { GD.PrintErr($"[laptop] bar fill: {ex.Message}"); return null; }
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
        // Measured off the art: 128 columns, the trough's interior spans 3..124.
        const float ArtWidth = 128f, InnerX = 3f, InnerWidth = 122f;
        float fraction = Mathf.Clamp(value / (float)ShopScreen.SatisfactionMax, 0f, 1f);
        // ⭐ Continuous, because the console is: FUN_00115530 computes (value - min) / (max - min)
        // as a FLOAT and hands that to the sprite. Nothing quantises to a tile.
        float cut = InnerX + InnerWidth * fraction;
        if (_barFill != null && cut > 0f)
            DrawTextureRectRegion(_barFill,
                new Rect2(r.Position, new Vector2(cut / ArtWidth * r.Size.X, r.Size.Y)),
                new Rect2(0, 0, cut, _barFill.GetHeight()));
        DrawTextureRect(_barFrame, r, false);
    }

    /// <summary>A slider: the yellow track with the gold knob riding it.
    /// ⚠ The knob art is square (32x32) and the track 128x32, so the knob is drawn at the track's
    /// HEIGHT rather than a share of its width -- scaling it by the track's aspect would squash it.</summary>
    /// ⚠ `selected` is NOT used for colour: measured off the real screen, the knob is the same
    /// on the row being adjusted as on the row that is not. It is kept because the console does
    /// branch on that bit somewhere, and the caller already knows the answer.
    void DrawSlider(Rect2 r, int value, float s, bool selected)
    {
        // ⭐⭐ TINTED, because the console tints it: the drawn sprite takes the colour at the
        // slider's `+0xB0`, which is (54,249,77). The art is gold; the console is not.
        // ⚠ Divided by 128, not 255 -- see ShopScreen.SliderTint. Godot multiplies by `modulate`
        // exactly as the GS does, so a component above 1 brightens rather than clipping the input.
        var tint = new Color(ShopScreen.SliderTint.R / ShopScreen.TintUnity,
                             ShopScreen.SliderTint.G / ShopScreen.TintUnity,
                             ShopScreen.SliderTint.B / ShopScreen.TintUnity);
        // ⚠ THE TRACK IS DRAWN UNTINTED. Its art is already the yellow outline the real screen
        // shows -- master's screenshot measures (242,246,26) there, which a green tint could not
        // produce. Tinting it was my inference, and it was wrong.
        DrawTextureRect(_slideTrack, r, false);
        float fraction = Mathf.Clamp(value / (float)ShopScreen.SatisfactionMax, 0f, 1f);
        float d = r.Size.Y;
        float x = r.Position.X + (r.Size.X - d) * fraction;
        DrawTextureRect(_slideKnob, new Rect2(new Vector2(x, r.Position.Y), new Vector2(d, d)), false, tint);
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
