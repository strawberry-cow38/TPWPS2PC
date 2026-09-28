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

    /// <summary>What one line of `Large.bff` advances, in native units. A label element's row is
    /// the TEXT'S TOP, so a line's middle is its row plus half of this.</summary>
    public const float LineAdvance = 30f;

    ImageTexture _chrome;
    readonly ImageTexture _barFrame, _barFill, _slideTrack, _slideKnob;
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

    bool _open;
    /// <summary>⚠ Setting this also switches the Control's MouseFilter. A full-rect `Stop` is
    /// what makes the laptop modal while it is up -- and it would swallow every click in the park
    /// if it stayed on once the laptop closed, so the two move together and cannot drift apart.</summary>
    public bool Open
    {
        get => _open;
        private set { _open = value; MouseFilter = value ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore; }
    }

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
        // ⭐ The laptop takes the mouse now. Master: "does the 'laptop' ui support mouse hover to
        // highlight, click to open / select" -- it did not; this was Ignore and the class had no
        // input handler at all.
        //
        // ⚠⚠ AND `Stop` ALONE DID NOTHING, which is the bug master hit next: "the laptop menu
        // doesnt block clicks behind it, so you cant interact with the menu itself." A Control
        // stops the mouse over its RECT, and this one never set anchors or a size -- so its rect
        // was empty, `_GuiInput` never fired, and every click went straight through to the park.
        // Filling the viewport gives it the area its MouseFilter needs.
        SetAnchorsPreset(LayoutPreset.FullRect);
        // ⚠⚠ START IGNORING, NOT STOPPING. The `Open` setter turns Stop on when the laptop is
        // shown; setting Stop here instead left a freshly built, CLOSED laptop swallowing every
        // click in the park -- the opposite of master's bug and worse than it. The audit caught
        // this before it shipped ("a closed laptop lets clicks through (filter Stop)").
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        ZIndex = 110;
    }

    /// <summary>Build it from the disc, or answer null when a piece is missing.
    /// ⚠ Answering null rather than substituting: a laptop drawn without its own chrome would be
    /// the same mistake this screen exists to correct.</summary>
    /// <param name="world">The world AT BUILD TIME, which may be null -- see <see cref="_world"/>.</param>
    /// <param name="worldNow">⭐ Asked again every frame, so the chrome follows the park the player
    /// is actually in rather than whichever world happened to be open when the UI was built.</param>
    public static LaptopShopScreen Create(AssetLibrary lib, FontText font, TextDatabase text,
                                          string world, string language = "eng",
                                          Func<string> worldNow = null)
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
        screen._chromeName = chromeName;   // ⭐ so RefreshChrome knows what is already loaded
        screen._world = worldNow;
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
        // ⭐ The LIVE price, not the compiled one. This read the record directly, so the number
        // could be shown and never changed; the player's price lives on the shop.
        _price = shop.SalePrice;
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

    /// <summary>⭐⭐ THE WORLD IS ASKED FOR, NOT REMEMBERED. Master: "its also showing the
    /// halloween background graphic, not the park dependant one."
    ///
    /// ⚠⚠ It was not halloween -- it was `LAPTOP_512`, the FALLBACK, whose art is the same
    /// ghoul-and-stone-wall picture as `LAPTOP_HALLOW`. So a chrome that looked like a wrong
    /// world was really "no world matched at all".
    ///
    /// ⚠ And it matched nothing because this screen is built from `LoadHudFont`, which can run
    /// BEFORE `AssetLibrary.OpenWad` has been called -- `WadName` is set only by `OpenWad`, so at
    /// construction there was no world to name. The old code froze that null forever. Holding a
    /// `Func` and asking it each frame is the fix, and it is a lesson this port already had
    /// written down: a constructor freezes a field, and call order is not file order.</summary>
    Func<string> _world;
    string _chromeName;

    /// <summary>⭐⭐ ONE RENDERER FOR THE SHOP, THE RIDE AND THE SIDESHOW, because the console
    /// has one: all three bind their own element list out of their own `.sce` and then draw with
    /// the SAME widget calls. The difference between them is <see cref="LaptopScreen"/> data.
    ///
    /// <paramref name="cells"/> is one entry per row of the spec: the text to put in the value
    /// column for a text row, or the 0..100 fraction for a bar or slider. A null text on a row
    /// leaves that row blank, which is how the shop's ingredient row disappears while keeping
    /// its slot.</summary>
    /// <summary>⭐ Per-row bar tints, or null for the art's own colour. Research is why: its bars
    /// are RED while a slot is idle and GREEN while it is researching (`0x365ed8` / `0x365ee0`),
    /// which is the screen's whole state signal.</summary>
    IReadOnlyList<Color?> _barTints;

    /// <summary>⭐ The one series a graph screen is showing, or null. Only ONE shows at a time on
    /// all three graph screens: selecting an item clears every toggle then flips that one.</summary>
    public readonly record struct GraphSeries(IReadOnlyList<int> Values, int Min, int Max,
                                              Color Colour, int Years);
    GraphSeries? _graph;

    /// <summary>⚠ Park Finance answers every label TWICE. `_column2` is the second answer per row
    /// and `_headers` the two column titles, drawn on the row the labels deliberately skip.</summary>
    IReadOnlyList<string> _column2, _headers;

    /// <summary>⭐ Visitor Information's three feelings fractions (0..100), or null. Drawn as an
    /// icon and a bar inside a blue pill, three rows stepping 40 down the `FeelingsClouds` region.
    /// </summary>
    IReadOnlyList<int> _feelings;

    /// <summary>The three thought faces and the pill's end cap. ⚠ Loaded by PATH because this
    /// port has no sprite registry; the console reaches the same files by id 5/6/7 and 0x33.</summary>
    public Texture2D[] FeelingsIcons { get; set; }
    public Texture2D PillCap { get; set; }

    /// <summary>⭐ The Awards rows: five medals and fourteen coaster stars, each either its own
    /// art (earned) or the blank icon. `findings/awards.md`.</summary>
    public Texture2D[] MedalArt { get; set; }
    public Texture2D[] StarArt { get; set; }
    public Texture2D BlankMedal { get; set; }
    public Texture2D BlankStar { get; set; }

    /// <summary>⚠ Which medal ART each CELL shows. The draw's cell order is not the registry's --
    /// see ParkAwards.MedalOfHiddenAward -- so the row is indexed through this.</summary>
    public IReadOnlyList<int> MedalCellOrder { get; set; }
    IReadOnlyList<bool> _medalsEarned, _starsEarned;

    /// <summary>⭐ The nine-slice the graph sits in. `findings/graph-widget.md` §1.3: the series
    /// draw builds a temporary element over the plot rect and issues `FUN_00142090` -- sprite
    /// `0x30` tiled in 16px rows with `0x2f` corners and `0x32` edges, which is the SAME
    /// `UI.WAD/messages/Messcorner|Messedge|Messfill` art the lobby message box uses.
    ///
    /// ⚠ Master, on the first graph render: "may also b missing a 'container' box for the graphs".
    /// It was -- I had skipped step 3 of the series draw entirely.</summary>
    public UiPanel GraphPanel { get; set; }

    public void ShowScreen(LaptopScreen spec, string title, IReadOnlyList<(string Text, int Fraction)> cells,
                           bool buildRow = false, int buildTextId = LaptopMainMenu.BuildTextId,
                           IReadOnlyList<Color?> barTints = null, GraphSeries? graph = null,
                           IReadOnlyList<string> column2 = null, IReadOnlyList<string> headers = null,
                           IReadOnlyList<int> feelings = null,
                           IReadOnlyList<bool> medals = null, IReadOnlyList<bool> stars = null)
    {
        _feelings = feelings;
        _medalsEarned = medals;
        _starsEarned = stars;
        _barTints = barTints;
        _graph = graph;
        _column2 = column2;
        _headers = headers;
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _title = title ?? "";
        _rows.Clear();
        // ⚠⚠ A DATA SCREEN IS NOT A MENU, AND LEAVING THE MENU BEHIND MADE IT ACT LIKE ONE.
        // `ShowMenu` cleared `_spec`, but this never cleared `_menu` -- so a data screen still
        // carried the previous menu's rows, `_GuiInput` went on hit-testing them, and clicking
        // anywhere on e.g. the Build screen fired MenuActivated and navigated. Master: "i click
        // it, and it takes me to the ride info (with sliders) page for the crazy ape ride."
        _menu.Clear();
        _menuHover = -1; _menuScroll = 0; _focusSent = -2;
        _buildRow = buildRow; _buildHover = false; _buildTextId = buildTextId;
        _cells.Clear();
        if (cells != null) _cells.AddRange(cells);
        bool wasShut = !Open;
        Open = true; Visible = true;
        // ⚠ Only on the way IN. Stepping between screens is not opening the laptop again.
        if (wasShut) Cue(LaptopSounds.Cue.Open);
        FitToViewport();
        QueueRedraw();
    }

    /// <summary>⭐⭐ THE LAPTOP'S MAIN MENU -- a list, not a data screen. `main.sce` has exactly one
    /// element, `textoptions` at row 115 col 45 left-justified, so there is no title row, no value
    /// column and no model window to draw; the options ARE the screen.
    ///
    /// The option order and text ids come from the console's own table at `0x2b97c0`; see
    /// <see cref="LaptopMainMenu"/>, which holds the reading.</summary>
    public void ShowMenu(IReadOnlyList<string> options, int selected, string sceneFile = null)
    {
        _menuScene = sceneFile;
        _spec = null;
        _pageArrows = new Rect2();   // ⚠ a menu has none; a stale rect would eat clicks
        _menu.Clear();
        if (options != null) _menu.AddRange(options);
        _menuSelected = selected;
        // ⚠ OFF BY DEFAULT on every screen change. A capability that has to be turned OFF by
        // each caller is one that will be left on by the caller that forgets.
        RightClickInspects = false;
        // ⚠ A new list starts at the top. Carrying the old scroll over would open a short list
        // scrolled past its own end (the draw clamps, but the first frame would jump).
        _menuScroll = 0; _menuHover = -1; _focusSent = -2;
        _buildRow = false; _buildHover = false;
        bool wasShut = !Open;
        Open = true; Visible = true;
        if (wasShut) Cue(LaptopSounds.Cue.Open);
        FitToViewport();
        QueueRedraw();
    }

    readonly List<string> _menu = new();
    int _menuSelected; string _menuScene;

    /// <summary>⭐ First visible menu row. The panel holds <see cref="LaptopMainMenu.MaxRows"/>
    /// and the Rides category alone has 47 things, so without this most of the archive is
    /// unreachable. Master: "add a scrollbar when any menu exceeds the space on the ui."</summary>
    int _menuScroll;
    /// <summary>The scrollbar's clickable track, in screen pixels, or a zero rect when the list
    /// fits. Kept from the draw so the hit-test cannot disagree with what was drawn.</summary>
    Rect2 _scrollTrack;

    /// <summary>⭐⭐ THE BALANCE, SWOOPING IN FROM THE LEFT. Master asked for it on Build and its
    /// submenus. `null` means draw nothing; the float runs 0 to 1 and eases the slide.</summary>
    string _balance;
    float _swoop;
    /// <summary>The Build row under the info list on a purchase screen, when there is one.</summary>
    bool _buildRow, _buildHover;
    Rect2 _buildRowRect;
    /// <summary>The confirm row's text: 801 Build on a purchase screen, 291 Hire on the Hire screen.</summary>
    int _buildTextId = LaptopMainMenu.BuildTextId;
    public event Action BuildRequested;

    /// <summary>⭐ RIGHT-CLICK ON A MENU ROW. Master: "rmb opens the current ride build info page.
    /// lmb just goes straight to placing." Left is <see cref="MenuActivated"/>.</summary>
    public event Action<int> MenuInspected;

    /// <summary>⭐ The laptop's voice. Set by the viewer; null means a silent park, which must
    /// still be navigable. The panel fires the cues rather than the viewer because MOVE is a
    /// thing only the panel knows about -- the row under the cursor, and the arrow keys.</summary>
    public LaptopSounds Sounds { get; set; }
    void Cue(LaptopSounds.Cue c) => Sounds?.Play(c);

    /// <summary>Show a money figure sliding in, or clear it. ⚠ Re-showing the SAME figure does not
    /// restart the slide -- moving between Build submenus should not make it fly in again.</summary>
    public void ShowBalance(string money)
    {
        if (money == _balance) return;
        _balance = money;
        _swoop = money == null ? 0f : 0.0001f;
        QueueRedraw();
    }



    /// <summary>Which menu row the pointer is over, or -1. ⚠ Separate from the SELECTED row: the
    /// console has no pointer, so highlight-under-cursor is this port's addition and must not
    /// quietly move the selection a controller would be driving.</summary>
    int _menuHover = -1;

    /// <summary>Which device owns the highlight. ⚠ Set by the movers, not guessed from state:
    /// hover and selection are both always SET, so only the order of events can tell them apart.</summary>
    bool _keyboardCursor;

    /// <summary>⭐ The menu row the 3D preview should be showing, hover first and the keyboard
    /// selection when the pointer is away. Master, 2026-09-27: "add a preview of the 3d model in
    /// the building rides/etc list in the bottom right when hovering over each thing" -- hovering
    /// leads, but a list driven by the arrow keys has no pointer to hover with, so the selection
    /// is the fallback rather than a blank pane.</summary>
    public int MenuFocus
    {
        get
        {
            // ⭐⭐ THE PREVIEW SHOWS THE ROW THAT IS LIT, AND ONLY THAT ROW. These were two
            // rules and they disagreed: once "no hover" stopped lighting anything, the preview
            // still fell back to the selection, so a model could sit in the pane belonging to a
            // row nothing was highlighting. One notion of "focused" drives both.
            int row = _keyboardCursor ? _menuSelected : _menuHover;
            return row >= 0 && row < _menu.Count ? row : -1;
        }
    }

    int _focusSent = -2;

    /// <summary>Raised when <see cref="MenuFocus"/> lands on a different row. ⚠ Edge-triggered and
    /// PUMPED ONCE A FRAME FROM `_Process`, not from each of the six places that move hover or
    /// selection: a preview that rebuilds a model is too expensive to fire per mouse-move event,
    /// and one notice per frame is impossible to forget when a seventh mover is added.
    ///
    /// ⚠⚠ NOT from `_Draw`, where this started. A subscriber that swaps a model is mutating the
    /// scene tree, which a drawing callback is the wrong place to do -- and it also lands one
    /// frame late, so the pane showed the PREVIOUS subject for a frame. `_Process` runs before
    /// the frame is drawn, so the model is in place by the time the panel samples it.</summary>
    public event System.Action<int> MenuFocusChanged;

    public void PumpMenuFocus()
    {
        int now = MenuFocus;
        if (now == _focusSent) return;
        _focusSent = now;
        MenuFocusChanged?.Invoke(now);
    }

    /// <summary>⭐⭐ THE BACK AND CLOSE BUTTONS, which master asked for on every laptop screen.
    /// They live in the chrome's top-right lump -- cols 321..499, rows 17..183, measured off the
    /// lossless TGA -- which is the space the console filled with its face-button legend
    /// (triangle Back, square Mainmenu, circle Close) and which this port had left empty.
    ///
    /// ⚠ A DELIBERATE ADDITION, not a restoration: the console drew glyphs for a pad it assumed
    /// you were holding. These are words, because a mouse has no face buttons.
    /// ⭐ The words are the game's own -- `STR_GIZMO_CPP_BACK` (545) and `STR_GIZMO_CPP_CLOSE`
    /// (967), the same two the gizmo bar uses -- rather than invented English.</summary>
    public const int BackTextId = 545, CloseTextId = 967;
    /// <summary>Authored-space boxes for the two buttons, inside the lump.</summary>
    static readonly Rect2 BackBox = new(336, 60, 150, 34), CloseBox = new(336, 104, 150, 34);
    int _btnHover = -1;   // 0 = back, 1 = close

    /// <summary>Raised when a menu row is clicked, with its index.</summary>
    public event Action<int> MenuActivated;
    /// <summary>Raised when Back or Close is clicked.</summary>
    public event Action<bool> Dismissed;   // true = close, false = back

    /// <summary>⭐ Page between the park's items on a DATA screen: -1 back, +1 on. The console's
    /// list screens carry an `ItemSelect` with its own arrows, and this is the keyboard half of it.
    /// ⚠ Only fires when a spec screen is up with no menu, which is exactly when Up and Down do
    /// nothing else -- `MoveSelection` returns immediately with an empty list, so nothing is taken
    /// away from the menus to pay for this.</summary>
    public event Action<int> Paged;

    Rect2 Screen(Rect2 authored, float s, Vector2 o) =>
        new(o + authored.Position * s, authored.Size * s);

    /// <summary>A menu row's clickable box. ⚠ Wider than the glyphs: a row is a TARGET, and
    /// hit-testing the rendered text would make short words like "Build" harder to hit than long
    /// ones, which is a worse UI than the console's.</summary>
    ///
    /// <para>⚠⚠ It takes the row's index in the LIST and subtracts the scroll itself, so a scrolled
    /// list cannot hit-test one row and draw another. A row above or below the window gets a zero
    /// rect, which <c>HasPoint</c> never matches -- an off-screen row is not clickable.</para>
    Rect2 MenuRowBox(int i, SceneLayout.Element list, float s, Vector2 o)
    {
        int slot = i - _menuScroll;
        if (slot < 0 || slot >= RowWindow) return new Rect2();
        return Screen(new Rect2(list.X - 4, list.Y + LaptopMainMenu.RowStep * slot, 260, LaptopMainMenu.RowStep), s, o);
    }

    /// <summary>The menu row under a point, or -1. ⭐ One place, used by the hover, the wheel and
    /// both buttons, so they cannot disagree about which row is under the pointer.</summary>
    int RowAt(Vector2 p, float s, Vector2 o)
    {
        if (_menu.Count == 0) return -1;
        if (LayoutFor(_menuScene ?? LaptopMainMenu.MainScene)[LaptopMainMenu.ListElement] is not { } l) return -1;
        for (int i = 0; i < _menu.Count; i++) if (MenuRowBox(i, l, s, o).HasPoint(p)) return i;
        return -1;
    }

    /// <summary>Scroll the menu by whole rows and clamp. Returns true when it actually moved.</summary>
    bool ScrollMenu(int by)
    {
        int cap = Math.Max(0, _menu.Count - RowWindow);
        int want = Math.Clamp(_menuScroll + by, 0, cap);
        if (want == _menuScroll) return false;
        _menuScroll = want;
        return true;
    }

    /// <summary>⚠ FOR THE AUDIT AND THE SHOT HARNESS. The row's clickable box in SCREEN pixels --
    /// the very rect `_GuiInput` tests, not a re-derivation of it. A check that rebuilt the
    /// geometry itself would agree with a draw that had drifted; this one cannot.</summary>
    public Rect2 MenuRowScreenBox(int i)
    {
        if (LayoutFor(_menuScene ?? LaptopMainMenu.MainScene)[LaptopMainMenu.ListElement] is not { } l)
            return new Rect2();
        return MenuRowBox(i, l, Scale, Origin);
    }

    /// <summary>How many rows the list may use -- all of them. ⚠ It briefly gave one up to the
    /// balance; master: "above where the list starts, in that empty space". The readout lives in
    /// the chrome's own empty band above row 115, so it costs the list nothing.</summary>
    int RowWindow => LaptopMainMenu.MaxRows;

    /// <summary>⭐⭐ THE ELEMENT THE BALANCE SITS ABOVE. Master: "the swoop in balance should
    /// still show on buildable-specific pages" -- so it has to anchor on a DATA screen too, where
    /// there is no menu list. On a menu that is the option list; on a purchase screen it is the
    /// TITLE, which is the topmost text either way, so the readout lands in the same band on both
    /// and does not jump as you step into a ride.</summary>
    SceneLayout.Element? BalanceAnchor() =>
        _spec != null ? LayoutFor(_spec)[_spec.TitleElement]
                      : LayoutFor(_menuScene ?? LaptopMainMenu.MainScene)[LaptopMainMenu.ListElement];

    /// <summary>⭐ The balance's row: ONE ROW ABOVE the list's first, on the list's own grid, in
    /// the band the chrome leaves between its top edge and row 115. The Back/Close column sits at
    /// column 336, so the left of that band is empty.</summary>
    Rect2 BalanceRow(SceneLayout.Element list) =>
        new(list.X, list.Y - LaptopMainMenu.RowStep, 260, LaptopMainMenu.RowStep);

    /// <summary>⚠ FOR THE AUDIT. Where the readout comes to rest, in screen pixels -- so a check
    /// can assert it does not land on the list rather than re-deriving the geometry and agreeing
    /// with a draw that had drifted.</summary>
    public Rect2 BalanceScreenBox
    {
        get
        {
            if (_balance == null) return new Rect2();
            if (BalanceAnchor() is not { } l) return new Rect2();
            return Screen(BalanceRow(l), Scale, Origin);
        }
    }

    /// <summary>The first visible row, and a keyboard/harness way to move it. <see cref="Scroll"/>
    /// returns false at either end, which is what makes the wheel fall through to the camera.</summary>
    public int ScrollRow => _menuScroll;
    public int Selected => _menuSelected;

    /// <summary>The panel's own scale and top-left, so a check can convert a screen rect back into
    /// the authored 512 space the disc measures in.</summary>
    public float PanelScale => Scale;
    public Vector2 PanelOrigin => Origin;
    public bool Scroll(int rows) { if (!ScrollMenu(rows)) return false; QueueRedraw(); return true; }

    /// <summary>What the audit reads back about the Build row and the balance.</summary>
    public bool HasBuildRow => _buildRow;
    public Rect2 BuildRowScreenBox => _buildRowRect;
    public bool BalanceShown => _balance != null;
    public float BalanceSwoop => _swoop;

    /// <summary>⚠ FOR THE SHOT HARNESS ONLY. A still cannot show a slide, so a render can neither
    /// demonstrate where the balance comes to rest nor catch it if it stops arriving. This forces
    /// the one field `_Process` drives, so a screenshot is evidence rather than an assertion.</summary>
    public void SetBalanceSwoop(float t) { _swoop = Mathf.Clamp(t, 0f, 1f); QueueRedraw(); }

    /// <summary>⚠ FOR THE SHOT HARNESS ONLY. A still cannot show a hover state, so a render can
    /// neither demonstrate the highlight works nor catch it if it stops. This forces the same two
    /// fields the pointer sets, so a screenshot is evidence rather than an assertion.</summary>
    public void ForceHoverForShot(int menuRow, int button)
    {
        _menuHover = menuRow; _btnHover = button; QueueRedraw();
    }

    /// <summary>⭐⭐ MAKE THE CONTROL'S RECT *BE* THE VIEWPORT, every time, rather than trusting
    /// an anchor preset to resolve.
    ///
    /// ⚠⚠ THIS IS THE BUG MASTER HIT TWICE. `_GuiInput` reports positions in the Control's LOCAL
    /// space, but every box this screen hit-tests is built from <see cref="Origin"/> and
    /// <see cref="Scale"/>, which are computed from `GetViewportRect()` -- VIEWPORT space. The two
    /// are only the same coordinate system when the Control sits at the origin and is exactly the
    /// viewport's size. It did not, so:
    ///   * most clicks landed outside the rect entirely and never reached `_GuiInput` at all
    ///     -- "i cant click any options";
    ///   * the few that did land were compared against boxes in the wrong space and matched
    ///     whichever row the arithmetic happened to give -- "it just selects a random option".
    /// One cause, both symptoms.
    ///
    /// ⚠ And I had already written that the anchored size was untested in game and assumed it
    /// worked. It did not. Setting it outright removes the assumption rather than re-testing it.</summary>
    /// <summary>⭐⭐ ONLY THE BLUE PANEL EATS THE MOUSE. Master, 2026-09-27: "the laptop ui
    /// should block all interaction behind it (but not in the open space either side of it, just
    /// under the blue panel)."
    ///
    /// The Control has to FILL the viewport, because the panel is centred and its rect moves with
    /// the window -- but filling it with `MouseFilter.Stop` made the whole screen swallow clicks,
    /// including the park either side of a 512-square panel on a wide monitor. Godot asks this
    /// before picking, so answering for the panel's rect alone gives blocking exactly under the
    /// chrome and a live park beside it.
    ///
    /// ⚠ A closed panel must answer false for every point, or an invisible screen keeps eating
    /// clicks; `Open` also drops MouseFilter to Ignore, and these two agree rather than one
    /// covering for the other.</summary>
    public override bool _HasPoint(Vector2 point)
        => Open && new Rect2(Origin, new Vector2(Native, Native) * Scale).HasPoint(point);

    void FitToViewport()
    {
        var want = GetViewportRect().Size;
        if (Position != Vector2.Zero) Position = Vector2.Zero;
        if (Size != want) Size = want;
    }

    /// <summary>⚠ DIAGNOSTIC. Two fixes for master's click bug were reasoned from the code and
    /// both failed ("identical behavior"). These record what actually reaches the Control, so the
    /// third attempt starts from a measurement instead of a third theory.</summary>
    /// <summary>Whether a right-click on a row means "inspect" on the screen now showing. ⚠ Only
    /// the build list has an inspect; everywhere else right-click is Back.</summary>
    public bool RightClickInspects { get; set; }

    public int GuiEvents, GuiMotion, GuiClicks;
    public string LastGui = "(none)";

    public override void _Process(double delta)
    {
        if (!Open || _balance == null || _swoop >= 1f) return;
        _swoop = Mathf.Min(1f, _swoop + (float)delta * 4f);   // ~0.25s
        QueueRedraw();
    }

    /// <summary>⭐⭐ THE KEYBOARD. Master: "add support for arrow keys operating these menus, too."
    ///
    /// ⚠ `_UnhandledKeyInput`, NOT `_GuiInput`: a Control only sees key events through `_GuiInput`
    /// when it holds focus, and this panel never takes focus -- it would have worked in a test and
    /// done nothing in the game. Unhandled input reaches it whether or not anything is focused.
    ///
    /// ⚠ Up/Down accept ECHO so holding an arrow walks the list, which is what a 47-row category
    /// needs; Enter and Escape do not, so a held key cannot fire an action twice.</summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Open || @event is not InputEventKey { Pressed: true } k) return;
        switch (k.Keycode)
        {
            case Key.Up:   if (PageInstead(-1)) break; MoveSelection(-1); break;
            case Key.Down: if (PageInstead(+1)) break; MoveSelection(+1); break;
            case Key.Enter or Key.KpEnter or Key.Space:
                if (k.Echo) return;
                // ⭐ The same thing the LEFT button does, so a pad and a mouse agree: activate the
                // selected row, or press Build when the screen has one and no list.
                if (_menu.Count > 0 && _menuSelected >= 0 && _menuSelected < _menu.Count)
                { Cue(LaptopSounds.Cue.Choose); MenuActivated?.Invoke(_menuSelected); }
                else if (_buildRow) { Cue(LaptopSounds.Cue.Choose); BuildRequested?.Invoke(); }
                else return;
                break;
            case Key.Right:
                // ⭐ The keyboard's INSPECT, matching the right mouse button.
                if (k.Echo || _menu.Count == 0) return;
                Cue(LaptopSounds.Cue.Choose);
                MenuInspected?.Invoke(_menuSelected);
                break;
            case Key.Escape or Key.Backspace or Key.Left:
                if (k.Echo) return;
                Cue(LaptopSounds.Cue.Back);
                Dismissed?.Invoke(false);   // Back, never Close: Close is a decision, not a slip
                break;
            default: return;
        }
        // ⚠ AcceptEvent() is for _GuiInput only; from unhandled input this is the way to stop the
        // key reaching the park -- Escape would otherwise also clear whatever is held.
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Move the selection and keep it on screen. ⚠ It also drops the HOVER: the draw
    /// prefers hover over selection, so without this the highlight would stay under a stationary
    /// mouse while the arrows moved something invisible.</summary>
    /// <summary>On a data screen, Up and Down page between items instead of moving a selection
    /// there is none of. Answers whether it took the key.</summary>
    bool PageInstead(int by)
    {
        if (_spec == null || _menu.Count > 0) return false;
        Cue(LaptopSounds.Cue.Move);
        Paged?.Invoke(by);
        return true;
    }

    void MoveSelection(int by)
    {
        if (_menu.Count == 0) return;
        _keyboardCursor = true;
        int was = _menuSelected;
        _menuSelected = Math.Clamp(_menuSelected + by, 0, _menu.Count - 1);
        // ⚠ At either end the arrow moves nothing, and a tick there would say otherwise.
        if (_menuSelected != was) Cue(LaptopSounds.Cue.Move);
        _menuHover = -1;
        // ⭐ Scroll only as far as it must to bring the selection back into the window.
        if (_menuSelected < _menuScroll) _menuScroll = _menuSelected;
        else if (_menuSelected >= _menuScroll + RowWindow) _menuScroll = _menuSelected - RowWindow + 1;
        _menuScroll = Math.Clamp(_menuScroll, 0, Math.Max(0, _menu.Count - RowWindow));
        QueueRedraw();
    }

    /// <summary>⚠ Modulate so the YELLOW art lands on the orange the real screen shows. Godot
    /// multiplies, so the factor is rendered/art per channel.</summary>
    static Color ArrowTint => new(LaptopArrows.Rendered.R / (float)LaptopArrows.Art.R,
                                  LaptopArrows.Rendered.G / (float)LaptopArrows.Art.G,
                                  LaptopArrows.Rendered.B / 255f);

    /// <summary>The paging arrows' drawn rectangle, in screen pixels, or empty when none is up.</summary>
    Rect2 _pageArrows;

    /// <summary>⭐ Each SLIDER's drawn rectangle, by row index. Remembered rather than re-derived,
    /// for the same reason the arrows are: `_GuiInput` must test the very rect that was drawn.</summary>
    readonly Dictionary<int, Rect2> _sliderRects = new();

    /// <summary>⭐ A slider was moved: the ROW INDEX it belongs to and its new value as a percent
    /// of its own track, 0..100. ⚠ A percent, not a setting: this control knows where the knob is
    /// and nothing about speed, capacity or duration. The caller owns the range and the clamp,
    /// because the console's ranges are per ride and live in its tier.</summary>
    public event Action<int, int> SliderMoved;

    /// <summary>Which slider the pointer is dragging, or -1.</summary>
    int _dragSlider = -1;

    /// <summary>⭐ The SHOP screen's own sliders by what they set. That screen draws through the
    /// `_rows` path rather than a <see cref="LaptopScreen"/> spec, so it keeps its own rects.</summary>
    readonly Dictionary<ShopScreen.Selection, Rect2> _shopSliders = new();

    /// <summary>⭐ A shop's quality or additive was dragged, as a percent of its track.
    /// ⚠ A percent, like <see cref="SliderMoved"/>: the clamps (both 0..100, and the price 1..500)
    /// are the console's, live in its screen setup `0x1D6D28`, and are applied by the caller.</summary>
    public event Action<ShopScreen.Selection, int> ShopSettingChanged;

    ShopScreen.Selection? _dragShop;

    /// <summary>⭐ A NUDGE ARROW's drawn rect, by row index -- the pairs a row gets when its value
    /// can be stepped. Same rule as the others: hit-test the rect that was drawn.</summary>
    readonly Dictionary<int, Rect2> _rowArrows = new();
    /// <summary>Every spec row's drawn band, by row index -- what a plain click hits.</summary>
    readonly Dictionary<int, Rect2> _specRows = new();
    /// <summary>The shop's own price arrows, which live outside the row list.</summary>
    Rect2 _shopPriceArrows;

    /// <summary>⭐ A nudge arrow was clicked: the ROW INDEX and -1 or +1. ⚠ A direction, not a
    /// value -- the step and the clamp are the caller's, where the field is.</summary>
    public event Action<int, int> RowNudged;
    /// <summary>A spec row was clicked, by row index. ⚠ Raised for ANY row; the handler decides
    /// which ones mean something.</summary>
    public event Action<int> RowActivated;
    /// <summary>The shop's price arrows: -1 or +1.</summary>
    public event Action<int> PriceNudged;

    /// <summary>The percent along a slider's track that a point sits at, clamped to it.</summary>
    static int PercentIn(Rect2 r, Vector2 at)
        => r.Size.X <= 0 ? 0 : Math.Clamp((int)Math.Round((at.X - r.Position.X) / r.Size.X * 100f), 0, 100);

    public override void _GuiInput(InputEvent @event)
    {
        GuiEvents++;
        if (@event is InputEventMouse m0)
            LastGui = $"{@event.GetType().Name} at local({m0.Position.X:F0},{m0.Position.Y:F0}) "
                    + $"rect({Position.X:F0},{Position.Y:F0} {Size.X:F0}x{Size.Y:F0}) open={Open}";
        if (!Open) return;
        FitToViewport();
        float s = Scale; var o = Origin;
        // ⭐ DRAGGING A SLIDER. The press starts it, motion carries it, and the release ends it --
        // so the knob follows the pointer instead of jumping once per click.
        if (_dragSlider >= 0 && @event is InputEventMouseMotion drag)
        {
            if (_sliderRects.TryGetValue(_dragSlider, out var dr))
            { SliderMoved?.Invoke(_dragSlider, PercentIn(dr, drag.Position)); AcceptEvent(); }
            return;
        }
        if (_dragShop is { } ds && @event is InputEventMouseMotion sdrag)
        {
            if (_shopSliders.TryGetValue(ds, out var sr))
            { ShopSettingChanged?.Invoke(ds, PercentIn(sr, sdrag.Position)); AcceptEvent(); }
            return;
        }
        if ((_dragSlider >= 0 || _dragShop != null) && @event is InputEventMouseButton { Pressed: false })
        { _dragSlider = -1; _dragShop = null; QueueRedraw(); AcceptEvent(); return; }

        if (@event is InputEventMouseMotion motion)
        {
            GuiMotion++;
            int wasMenu = _menuHover, wasBtn = _btnHover;
            _btnHover = Screen(BackBox, s, o).HasPoint(motion.Position) ? 0
                      : Screen(CloseBox, s, o).HasPoint(motion.Position) ? 1 : -1;
            bool wasBuild = _buildHover;
            _buildHover = _buildRow && _buildRowRect.HasPoint(motion.Position);
            if (_buildHover != wasBuild) QueueRedraw();
            _menuHover = RowAt(motion.Position, s, o);
            _keyboardCursor = false;
            // ⚠ Only when it lands ON a row, and only when it CHANGES -- otherwise every pixel of
            // mouse movement across one row would tick.
            if (_menuHover >= 0 && _menuHover != wasMenu) Cue(LaptopSounds.Cue.Move);
            if (_menuHover != wasMenu || _btnHover != wasBtn) QueueRedraw();
            return;
        }
        // ⭐ The wheel walks the list a row at a time. Only swallow it when it moved something --
        // at either end the event belongs to whatever is behind (the camera zoom).
        if (@event is InputEventMouseButton { Pressed: true } w
            && (w.ButtonIndex == MouseButton.WheelUp || w.ButtonIndex == MouseButton.WheelDown))
        {
            if (ScrollMenu(w.ButtonIndex == MouseButton.WheelUp ? -1 : 1))
            {
                // ⚠ The rows have moved UNDER a cursor that has not: re-hover from the pointer,
                // or the highlight would point at one row until the mouse next twitched.
                _menuHover = RowAt(w.Position, s, o);
                QueueRedraw(); AcceptEvent();
            }
            return;
        }
        if (@event is InputEventMouseButton { Pressed: true } b
            && b.ButtonIndex is MouseButton.Left or MouseButton.Right)
        {
            GuiClicks++;
            // ⭐ A SLIDER TAKES THE CLICK BEFORE ANYTHING ELSE, and keeps it until the button is
            // released. ⚠ Tested against the rect that was DRAWN, never a re-derivation.
            if (b.ButtonIndex == MouseButton.Left)
                foreach (var (idx, rect) in _sliderRects)
                    if (rect.HasPoint(b.Position))
                    {
                        _dragSlider = idx;
                        Cue(LaptopSounds.Cue.Move);
                        SliderMoved?.Invoke(idx, PercentIn(rect, b.Position));
                        AcceptEvent();
                        return;
                    }
            if (b.ButtonIndex == MouseButton.Left)
                foreach (var (which, rect) in _shopSliders)
                    if (rect.HasPoint(b.Position))
                    {
                        _dragShop = which; _selected = which;
                        Cue(LaptopSounds.Cue.Move);
                        ShopSettingChanged?.Invoke(which, PercentIn(rect, b.Position));
                        AcceptEvent();
                        return;
                    }
            if (b.ButtonIndex == MouseButton.Left)
            {
                if (_shopPriceArrows.Size.X > 0 && _shopPriceArrows.HasPoint(b.Position))
                {
                    Cue(LaptopSounds.Cue.Move);
                    PriceNudged?.Invoke(b.Position.X < _shopPriceArrows.Position.X
                                        + _shopPriceArrows.Size.X / 2f ? -1 : +1);
                    AcceptEvent(); return;
                }
                foreach (var (idx, rect) in _rowArrows)
                    if (rect.HasPoint(b.Position))
                    {
                        Cue(LaptopSounds.Cue.Move);
                        RowNudged?.Invoke(idx, b.Position.X < rect.Position.X + rect.Size.X / 2f ? -1 : +1);
                        AcceptEvent(); return;
                    }
            }
            // ⭐ THE PAGING ARROWS, LEFT HALF BACK AND RIGHT HALF ON. `UIarrow.ssh` is one
            // sprite holding both triangles, symmetric about its middle (208 opaque pixels left,
            // 205 right), so the halves are the two buttons.
            if (b.ButtonIndex == MouseButton.Left && _pageArrows.Size.X > 0
                && _pageArrows.HasPoint(b.Position))
            {
                Cue(LaptopSounds.Cue.Move);
                Paged?.Invoke(b.Position.X < _pageArrows.Position.X + _pageArrows.Size.X / 2f ? -1 : +1);
                AcceptEvent();
                return;
            }
            // ⚠⚠ HIT-TEST THE CLICK, DO NOT TRUST THE HOVER. `_menuHover` is set by MOTION, and
            // the wheel moves rows under a cursor that never moved -- so a scroll followed by a
            // click with no twitch in between would have activated whatever row used to be there.
            int row = RowAt(b.Position, s, o);
            LastGui += $" -> btn={b.ButtonIndex} btnHover={_btnHover} row={row}";

            // ⭐ RIGHT-CLICK IS INSPECT. Master: "rmb opens the current ride build info page."
            // It does nothing anywhere else -- Back, Close and the Build row are actions, and a
            // right-click on them should not quietly perform them.
            if (b.ButtonIndex == MouseButton.Right)
            {
                // ⭐ RIGHT-CLICK IS INSPECT ONLY WHERE INSPECT EXISTS -- and BACK everywhere else.
                // Master, 2026-09-27: "pressing rmb on a laptop menu option that doesnt already
                // have a rmb interaction should go back". It used to raise MenuInspected on every
                // screen; the handler ignored it off the build list, so right-click was simply
                // dead on most of the laptop.
                if (RightClickInspects && row >= 0)
                {
                    _menuSelected = row; QueueRedraw();
                    Cue(LaptopSounds.Cue.Choose);
                    MenuInspected?.Invoke(row); AcceptEvent();
                    return;
                }
                Cue(LaptopSounds.Cue.Back);
                Dismissed?.Invoke(false);   // false = Back, the same as the Back button
                AcceptEvent();
                return;
            }
            if (_buildRow && _buildRowRect.HasPoint(b.Position))
            { Cue(LaptopSounds.Cue.Choose); BuildRequested?.Invoke(); AcceptEvent(); return; }
            // ⭐ A plain click on a spec row. ⚠ AFTER the sliders, the nudge arrows and the pager,
            // all of which sit inside a row's band and must win -- a row-wide rect registered
            // first would swallow every one of them.
            if (b.ButtonIndex == MouseButton.Left)
                foreach (var (idx, rect) in _specRows)
                    if (rect.HasPoint(b.Position))
                    { Cue(LaptopSounds.Cue.Choose); RowActivated?.Invoke(idx); AcceptEvent(); return; }

            // ⚠⚠ HIT-TEST THE CLICK HERE TOO. `_btnHover` is set by MOTION, exactly like
            // `_menuHover` -- and the comment above already says why trusting that is wrong. A
            // click away from the buttons must not fire whichever one the pointer last crossed.
            int btn = Screen(BackBox, s, o).HasPoint(b.Position) ? 0
                    : Screen(CloseBox, s, o).HasPoint(b.Position) ? 1 : -1;
            if (btn >= 0)
            {
                Cue(btn == 1 ? LaptopSounds.Cue.Close : LaptopSounds.Cue.Back);
                Dismissed?.Invoke(btn == 1); AcceptEvent(); return;
            }
            if (row >= 0)
            {
                _menuSelected = row;
                QueueRedraw();
                Cue(LaptopSounds.Cue.Choose);
                MenuActivated?.Invoke(row);
                AcceptEvent();
            }
        }
    }

    /// <summary>⭐ Back and Close, drawn on EVERY screen. Hovering brightens a row to the same
    /// yellow the selected row uses, because the laptop has exactly two text colours and inventing
    /// a third for hover would not be this UI.</summary>
    void DrawButtons(float s, Vector2 o)
    {
        void One(Rect2 box, int textId, bool hot)
        {
            string label = Row(textId);
            if (label == null) return;
            var at = Screen(box, s, o);
            DrawRun(label, at.Position + new Vector2(0, 2 * s), s,
                    Of(hot ? ShopScreen.Highlight : ShopScreen.Label), "left");
        }
        One(BackBox, BackTextId, _btnHover == 0);
        One(CloseBox, CloseTextId, _btnHover == 1);
    }

    /// <summary>⭐ The menu's own draw. It steps the SAME 32 the info screens do -- `DAT_002e9ca8`
    /// -- and uses the same two colours, yellow for the row under the cursor and orange for the
    /// rest, because the laptop has exactly two text colours and no third "disabled" one.</summary>
    void DrawMenu(float s, Vector2 o)
    {
        var layout = LayoutFor(_menuScene ?? LaptopMainMenu.MainScene);

        // ⭐⭐ THE 3D PREVIEW, BOTTOM RIGHT, FOLLOWING THE HOVERED ROW. Master, 2026-09-27:
        // "add a preview of the 3d model in the building rides/etc list in the bottom right when
        // hovering over each thing".
        //
        // ⚠ The frame is NOT invented and NOT hardcoded. `main.sce` -- the scene a menu draws
        // through -- declares only `textoptions`, no model pane, because the console never shows
        // a plain menu here: its build list IS `main_bh_items.sce`, one screen carrying the list
        // (`ItemSelect` row 115 col 45), the numbers AND the model. This port split that into a
        // menu plus a detail page, so the pane is read from the screen the console would have
        // used, and lands exactly where the console puts it.
        //
        // ⭐ All 14 model-bearing screens author this window at the SAME frame -- row 208,
        // col 315, 147x240 -- so there is no per-screen geometry to get wrong.
        if (ModelTexture != null
            && LayoutFor(LaptopScreen.Build.SceneFile)[LaptopScreen.Build.ModelElement] is { } pane)
            DrawTextureRect(ModelTexture,
                new Rect2(o + new Vector2(pane.X, pane.Y) * s, new Vector2(pane.Width, pane.Height) * s),
                false);

        if (layout[LaptopMainMenu.ListElement] is not { } list) return;
        var at = o + new Vector2(list.X, list.Y) * s;
        int max = RowWindow;
        _menuScroll = Math.Clamp(_menuScroll, 0, Math.Max(0, _menu.Count - max));
        int first = _menuScroll, last = Math.Min(_menu.Count, first + max);
        // ⭐ The bar sits just right of the label column, inside the panel's own content area.
        _scrollTrack = _menu.Count > max
            ? new Rect2(o + new Vector2(list.X + 268, list.Y) * s, new Vector2(6, max * LaptopMainMenu.RowStep) * s)
            : new Rect2();
        if (_menu.Count > max)
        {
            DrawRect(_scrollTrack, Of(ShopScreen.Label) with { A = 0.35f });
            float frac = max / (float)_menu.Count;
            float pos  = first / (float)_menu.Count;
            DrawRect(new Rect2(_scrollTrack.Position + new Vector2(0, _scrollTrack.Size.Y * pos),
                               new Vector2(_scrollTrack.Size.X, Math.Max(8f, _scrollTrack.Size.Y * frac))),
                     Of(ShopScreen.Highlight));
        }
        for (int i = first; i < last; i++)
        {
            // ⭐⭐ EXACTLY ONE ROW IS EVER HIGHLIGHTED. Master, on the first render: "are there
            // meant to be 2 options highlighted in the first pic?" -- there were, and no.
            //
            // ⚠ The first version lit `_menuSelected || _menuHover`, so the selected row AND the
            // row under the pointer both went yellow and nothing told them apart. The reasoning
            // behind it was right about STATE -- hovering must not move a selection a pad is
            // driving -- but it let that produce two identical highlights, which is a different
            // question and a plain UI fault.
            //
            // ⭐ While the pointer is over a row, THAT row is the highlight; with the pointer
            // away, the selection shows through again. The selection itself is untouched either
            // way, so a pad and a mouse still agree on what is chosen.
            // ⭐⭐ NOTHING IS LIT WHEN THE POINTER IS OFF THE ROWS. Master, 2026-09-27: "if not
            // hovering over an option ... dont highlight any option". This used to fall back to
            // `_menuSelected`, so a row stayed yellow under a pointer that had left it -- and it
            // read as clickable when clicking it did nothing.
            //
            // ⚠ BUT THE ARROW KEYS STILL NEED A VISIBLE CURSOR, or `MoveSelection` would move an
            // invisible one. So the highlight follows whichever device was used last: the pointer
            // while it is being moved, the selection once a key has been pressed.
            int lit = MenuFocus;
            var colour = Of(i == lit ? ShopScreen.Highlight : ShopScreen.Label);
            int slot = i - first;   // ⚠ the ROW's place on screen, not its index in the list
            DrawRun(_menu[i], at + new Vector2(0, LaptopMainMenu.RowStep * slot * s), s, colour, list.Justify);
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

        // ⭐ THE `◀▶` BESIDE A PAGEABLE TITLE. The list screens step through the park's items of
        // their kind, and until now the only way to do it was the keyboard -- the arrows the scene
        // authors were not drawn at all. ⚠ The rect is REMEMBERED, not re-derived: `_GuiInput`
        // hit-tests this very rectangle, which is the rule the rest of this file already follows.
        if (_spec.TitleArrowElement != null && _arrows != null
            && layout[_spec.TitleArrowElement] is { } pager)
        {
            var asize = new Vector2(LaptopArrows.NativeWidth, LaptopArrows.NativeHeight) * s;
            _pageArrows = new Rect2(At(pager) - new Vector2(0, asize.Y / 2f), asize);
            DrawTextureRect(_arrows, _pageArrows, false, ArrowTint);
        }

        // ⭐ AWARDS: five medals across `MedalRow`, fourteen coaster stars across `StarRow`.
        // ⚠ The SPACING is derived from the authored rect (265x96 for five, 427x180 for fourteen
        // in two rows of seven), not read -- the registry gives the ORDER and the icons are 32px,
        // but the row widget's own step was not found. Named so nobody takes it for measured.
        if (_medalsEarned != null) DrawAwardRow(layout, "MedalRow", _medalsEarned, MedalArt, BlankMedal, 5, s, o, MedalCellOrder);
        if (_starsEarned  != null) DrawAwardRow(layout, "StarRow",  _starsEarned,  StarArt,  BlankStar,  7, s, o);

        // ⭐ VISITOR INFORMATION's feelings rows: a blue pill, then the icon and the bar on it.
        // ⚠ The pills OVERLAP by 8px because the row advance (40) is less than the pill height
        // (48). That is what the console draws, so they are not spaced out to look tidier.
        if (_feelings != null && layout["FeelingsClouds"] is { } clouds)
        {
            var pillBlue = Color.Color8(0, 0, 255);
            for (int r = 0; r < _feelings.Count && r < 3; r++)
            {
                float top = At(clouds).Y + LaptopScreen.FeelingsRowStep * r * s;
                float bodyX = Origin.X + LaptopScreen.FeelingsIconCol * s;
                float bodyW = (LaptopScreen.FeelingsBarCol + 72 - LaptopScreen.FeelingsIconCol) * s;
                var body = new Rect2(new Vector2(bodyX, top - 8 * s),
                                     new Vector2(bodyW, LaptopScreen.FeelingsPillHeight * s));
                DrawRect(body, pillBlue);
                // The end caps are 12x48, mirrored on the right, sitting just outside the body.
                if (PillCap != null)
                {
                    var capL = new Rect2(new Vector2(bodyX - 9 * s, body.Position.Y),
                                         new Vector2(9 * s, body.Size.Y));
                    var capR = new Rect2(new Vector2(body.End.X, body.Position.Y),
                                         new Vector2(9 * s, body.Size.Y));
                    DrawTextureRect(PillCap, capL, false, pillBlue);
                    DrawTextureRect(PillCap, capR, false, pillBlue);
                }
                if (FeelingsIcons != null && r < FeelingsIcons.Length && FeelingsIcons[r] != null)
                    DrawTextureRect(FeelingsIcons[r],
                        new Rect2(new Vector2(bodyX, top), new Vector2(32 * s, 32 * s)), false);
                var bar = new Rect2(
                    new Vector2(Origin.X + LaptopScreen.FeelingsBarCol * s,
                                top + LaptopScreen.FeelingsBarDy * s),
                    new Vector2(72 * s, 22 * s));
                // ⚠ The three bars are given colours by the constructor, but style 1 never applies
                // a tint -- so they draw in the art's own colour, like every other progress bar.
                DrawBar(bar, _feelings[r], s);
            }
        }

        // ⭐ The GRAPH, drawn before the rows for the same reason the model window is. ⚠ Its
        // element is the model window's own frame (315, 208, 147x200), which is why no graph
        // screen also has a model.
        if (_spec.GraphElement != null && _graph is { } g && layout[_spec.GraphElement] is { } gbox)
        {
            var rect = new Rect2(At(gbox), new Vector2(gbox.Width, gbox.Height) * s);
            // ⚠ BEHIND the plot. The console issues the panel at Z with the colour pass at Z-1,
            // and the doc flags that z rule as inferred rather than read -- but a container the
            // plot is drawn INSIDE is the only reading that produces a graph you can look at.
            GraphPanel?.Draw(this, rect, s);
            LaptopGraph.DrawSeries(this, rect, g.Values, g.Min, g.Max, g.Colour, s);
            // ⚠ The year ticks appear ONLY when the span is more than one year, and they are
            // BLUE -- the console's own colour, and the nearest thing this screen has to a legend.
            if (g.Years > 1)
                for (int y = 1; y < g.Years; y++)
                {
                    float fx = rect.Position.X + rect.Size.X * y / g.Years;
                    DrawRun(y.ToString(), new Vector2(fx, rect.End.Y - LineAdvance * s), s,
                            LaptopGraph.YearTick, "left");
                }
        }

        // ⭐ The model window. Drawn before the rows so nothing it overlaps can be hidden by it.
        if (ModelTexture != null && layout[_spec.ModelElement] is { } window)
            DrawTextureRect(ModelTexture,
                new Rect2(At(window), new Vector2(window.Width, window.Height) * s), false);

        // ⭐ Column headers, drawn on the row the label grid skips (Park Finance's row 200).
        if (_headers != null && _spec.ValueElement2 != null
            && layout[_spec.ValueElement] is { } h1 && layout[_spec.ValueElement2] is { } h2)
        {
            if (_headers.Count > 0 && _headers[0] != null)
                DrawRun(_headers[0], At(h1), s, Of(ShopScreen.Highlight), h1.Justify);
            if (_headers.Count > 1 && _headers[1] != null)
                DrawRun(_headers[1], At(h2), s, Of(ShopScreen.Label), h2.Justify);
        }

        var screenLabels = layout[_spec.LabelElement];
        var values = layout[_spec.ValueElement];
        for (int i = 0; i < _spec.Rows.Count; i++)
        {
            var row = _spec.Rows[i];
            var (text, fraction) = i < _cells.Count ? _cells[i] : (null, 0);

            // ⭐ A ROW CAN OWN ITS LABEL'S ELEMENT, and then it sits AT that element and does not
            // step at all. Game Options is why: its two sliders are labelled at `MusicSliderText`
            // (116) and `SfxSliderText` (150), while its four text rows step 32 from `TextOptions`
            // (185). One grid cannot describe both, and the console does not try -- it reads those
            // four from a table of its own.
            //
            // ⚠ Everything below this line that reads `labels` now reads the ROW's label, which is
            // what makes the value column and the click rect follow a row that moved.
            var labels = row.LabelElement != null ? layout[row.LabelElement] : screenLabels;
            // ⚠ An EXPLICIT row wins over the grid: the Balance Sheet steps 42 once, in the
            // middle, and a uniform step cannot say that.
            float dy = row.LabelElement != null
                     ? 0f
                     : _spec.RowYs != null && i < _spec.RowYs.Count && labels is { } rg
                       ? (_spec.RowYs[i] - rg.Y) * s
                       : LaptopScreen.RowStep * (i - _spec.StepBase) * s;

            // ⭐⭐ A ROW THAT OWNS A SIZED WIDGET TAKES ITS LABEL'S HEIGHT FROM THE WIDGET, not
            // from the step. The label grid steps 32, but an authored widget sits exactly where the
            // scene puts it, and the two disagree: All Rides' bars are 34 apart (148, 182, 216)
            // against labels stepping 32 from 108, so by the third bar the label is 8px above its
            // own bar. Master, 2026-09-27: "ur formatting on these pages is a little misaligned".
            //
            // ⭐ THE RULE IS THE DISC'S, AND ALL TOILETS IS THE CONTROL -- it has ONE row, so no
            // stepping is involved and only the authored numbers speak. CleanlinessText sits at 175
            // and CleanlinessBar at 180: the label's middle is 175 + 15 = 190, and the 22-tall bar's
            // middle is 180 + 11 = 191. One pixel apart. The console authors a label CENTRED on its
            // widget, and that is what this reproduces rather than a nudge that looked right.
            //
            // ⚠ Only SIZED elements re-anchor a label. A row whose Element is a text position
            // (the sideshow's CostOfPrizeValue, say) has no width or height and must keep the step.
            // Shared stepped widgets keep the authored label grid (All Staff:175+32*i).
            // Its wage uses infobars for X ONLY; centering it on that box collapses it too.
            if (!_spec.LabelsOnGrid && _spec.WidgetStep == 0 && labels is { } lrow && row.Element != null
                && layout[row.Element] is { HasSize: true } sized)
                dy = (sized.Y + sized.Height / 2f - LineAdvance / 2f - lrow.Y) * s;

            string label = Row(row.TextId);
            if (labels is { } l && label != null)
                DrawRun(label, At(l) + new Vector2(0, dy), s, Of(ShopScreen.Label), l.Justify);

            // ⭐ A ROW IS CLICKABLE. Only some do anything -- the Upgrades row asks for an upgrade
            // -- but the rect is registered for every row and the CALLER decides, because which
            // rows act is a property of the screen's data and not of the drawing.
            // ⚠ Registered from the DRAWN position (label row plus the same `dy` the text got), so
            // a row re-anchored onto a sized widget is clickable where it actually appears.
            if (labels is { } lr)
                _specRows[i] = new Rect2(
                    new Vector2(Origin.X, At(lr).Y + dy),
                    new Vector2(Native * s, LineAdvance * s));

            // ⭐ A widget sits at ITS OWN element's row, not on the label grid. The ride screen
            // places its seven widgets at 118/150/182/214/246/280/310 -- 32 apart for the bars and
            // then 34 and 30 -- so stepping them with the labels would drift by the third slider.
            if (row.Kind is LaptopRowKind.Bar or LaptopRowKind.Slider)
            {
                if (row.Element == null || layout[row.Element] is not { } w) continue;
                // ⭐ Rows that SHARE one widget element step it; see LaptopScreen.WidgetStep.
                var wat = At(w) + new Vector2(0, _spec.WidgetStep * i * s);
                var rect = new Rect2(wat, new Vector2(w.Width, w.Height) * s);
                if (row.Kind == LaptopRowKind.Bar)
                    DrawBar(rect, fraction, s, _barTints != null && i < _barTints.Count ? _barTints[i] : null);
                else { _sliderRects[i] = rect; DrawSlider(rect, fraction, s, selected: _dragSlider == i); }
                // ⭐ A WIDGET ROW CAN ALSO CARRY TEXT -- Research draws the project's name beside
                // its bar. A row whose cell has no text stops here, which is every widget row
                // written before Research, so nothing already drawn changes.
                if (text == null) continue;
            }

            // ⭐ The nudge arrows, for a row whose value the player can change. They are drawn
            // whether or not the row has text this frame, because they belong to the row.
            if (row.ArrowElement != null && _arrows != null && layout[row.ArrowElement] is { } arrow)
            {
                // ⚠ Modulate so the YELLOW art lands on the orange the real screen shows; see
                // LaptopArrows. Godot multiplies, so the factor is rendered/art per channel.
                var want = ArrowTint;
                // ⚠⚠ THE ELEMENT'S ROW IS THE SPRITE'S CENTRE, and getting this wrong twice is
                // what master saw: "the thing misaligned were the arrows on prize and price for
                // sideshows". Derived rather than nudged -- a label row is the TEXT'S TOP and
                // Large.bff advances 30, so a line's middle is row + 15; the arrow elements sit
                // at 353 and 384 against labels at 339 and 371, i.e. 14 and 13 below. That is the
                // half-line, so the row is where the arrow's middle goes.
                var size = new Vector2(LaptopArrows.NativeWidth, LaptopArrows.NativeHeight) * s;
                var arect = new Rect2(At(arrow) - new Vector2(0, size.Y / 2f), size);
                _rowArrows[i] = arect;
                DrawTextureRect(_arrows, arect, false, want);
            }

            // ⭐ The SECOND value column, at its own element's column and this row's height.
            if (_spec.ValueElement2 != null && _column2 != null && i < _column2.Count
                && _column2[i] != null && layout[_spec.ValueElement2] is { } v2
                && labels is { } l2)
                DrawRun(_column2[i], new Vector2(At(v2).X, At(l2).Y + dy), s,
                        Of(ShopScreen.Label), v2.Justify);

            if (text == null) continue;
            // ⭐⭐ CHECKED FIRST, and the order is the point: a Research row HAS its own element
            // (its bar), so the own-element branch below would win and stack all five item names
            // on that one element's position. Research puts its names at `ResearchItem`'s COLUMN
            // but the BARS' row -- MIPS `0x1b5a9c` reads the bars' row, `0x1b5b40` the item's
            // column -- five pixels below their own labels.
            if (_spec.ValueOnWidgetRow && values is { } vw && row.Element != null
                && layout[row.Element] is { } welem)
                DrawRun(text,
                        new Vector2(At(vw).X, At(welem).Y + _spec.WidgetStep * i * s),
                        s, ValueTint, vw.Justify);
            // A row with its own value element uses it; otherwise the shared value column, at the
            // label's height.
            else if (row.Element != null && layout[row.Element] is { } own)
                // ⚠ Column only where the screen says so -- All Staff's value elements carry a
                // row that the console never reads.
                DrawRun(text,
                        _spec.ValueColumnOnly && labels is { } lb
                          ? new Vector2(At(own).X, At(lb).Y + dy) : At(own),
                        s, ValueTint, own.Justify);
            else if (values is { } v && labels is { } lab)
                // ⚠ THE VALUE COLUMN CONTRIBUTES ITS X, AND THE LABEL ITS Y. On the shop the two
                // elements share a row (both 175) so either reading works; on the ride they do
                // NOT -- its value elements sit at 338/400/436 against labels from 115 -- and
                // taking the value element's row as a baseline threw the text off the screen.
                DrawRun(text, new Vector2(At(v).X, At(lab).Y + dy), s, ValueTint, v.Justify);
        }
    }

    static string Money(int v) => v < 0 ? $"-${-v:N0}" : $"${v:N0}";

    /// <summary>One row of award icons, evenly spread across its authored rect.</summary>
    void DrawAwardRow(SceneLayout layout, string element, IReadOnlyList<bool> earned,
                      Texture2D[] art, Texture2D blank, int perRow, float s, Vector2 origin,
                      IReadOnlyList<int> order = null)
    {
        if (layout[element] is not { } box || earned == null) return;
        // ⚠ `At` is a local function of the draw; this helper takes the same origin instead.
        var at = origin + new Vector2(box.X, box.Y) * s;
        float cell = box.Width * s / perRow;
        float rowH = box.Height * s / Math.Max(1, (earned.Count + perRow - 1) / perRow);
        for (int i = 0; i < earned.Count; i++)
        {
            // ⚠ A cell's ART index is not its cell index where an order is given (the medals).
            int ai = order != null && i < order.Count ? order[i] : i;
            var tex = earned[i] && art != null && ai < art.Length && art[ai] != null ? art[ai] : blank;
            if (tex == null) continue;
            var pos = new Vector2(at.X + cell * (i % perRow), at.Y + rowH * (i / perRow));
            DrawTextureRect(tex, new Rect2(pos, new Vector2(32 * s, 32 * s)), false);
        }
    }

    /// <summary>⭐ Load the Awards art once: each medal and coaster star by the path the texture
    /// registry names, plus the two blank icons an unearned slot shows.</summary>
    public void EnsureAwardArt(AssetLibrary lib)
    {
        if (MedalArt != null || lib == null) return;
        var m = new Texture2D[GoldTicketScreen.Medals.Length];
        for (int i = 0; i < m.Length; i++) m[i] = LoadSsh(lib, "/" + GoldTicketScreen.Medals[i].Art);
        MedalArt = m;
        var st = new Texture2D[GoldTicketScreen.UltimateStars.Length];
        for (int i = 0; i < st.Length; i++) st[i] = LoadSsh(lib, "/" + GoldTicketScreen.UltimateStars[i].Art);
        StarArt = st;
        BlankMedal ??= LoadSsh(lib, "/laptop/AWARD_MEDAL_32.ssh");
        BlankStar  ??= LoadSsh(lib, "/laptop/AWARD_STAR_32.ssh");
    }

    /// <summary>⭐ Load Visitor Information's art once: the three thought faces and the pill's
    /// end cap. ⚠ By PATH -- this port has no sprite registry, and the console's own registry
    /// entries name these files, so the paths are the registry's answer rather than a guess.
    /// A face that will not load leaves its slot null and the row draws without an icon.</summary>
    public void EnsureVisitorArt(AssetLibrary lib)
    {
        if (FeelingsIcons != null || lib == null) return;
        var icons = new Texture2D[LaptopScreen.FeelingsIconPaths.Length];
        for (int i = 0; i < icons.Length; i++)
        {
            icons[i] = LoadSsh(lib, LaptopScreen.FeelingsIconPaths[i]);
            if (icons[i] == null)
                GD.PrintErr($"[laptop] visitor icon missing: {LaptopScreen.FeelingsIconPaths[i]}");
        }
        FeelingsIcons = icons;
        PillCap ??= LoadSsh(lib, "/laptop/PROG_WBIT.ssh");
    }

    /// <summary>⚠ Measure text in NATIVE units, for checking a column against the font rather
    /// than nudging it until it looks right.</summary>
    public void ReportTextWidth(params string[] samples)
    {
        if (_font == null) return;
        foreach (var t in samples)
        {
            var tex = _font.Render(t);
            GD.Print($"[laptop] width \"{t}\" = {(tex == null ? -1 : tex.GetWidth())} native "
                   + $"({(tex == null ? 0 : tex.GetWidth() / (float)Math.Max(1, t.Length)):F1}/char)");
        }
    }

    /// <summary>One UI sprite off the disc, or null. Same shape as the local loader `Create` uses;
    /// separate because this one runs later, when a screen first asks for its art.</summary>
    static ImageTexture LoadSsh(AssetLibrary lib, string name)
    {
        var raw = lib?.ReadUi(name);
        if (raw == null) return null;
        try
        {
            var ssh = new Ssh(raw);
            return ImageTexture.CreateFromImage(
                Image.CreateFromData(ssh.Width, ssh.Height, false, Image.Format.Rgba8, ssh.Pixels));
        }
        catch (Exception ex) { GD.PrintErr($"[laptop] {name}: {ex.Message}"); return null; }
    }

    /// <summary>⚠ A value's colour. Almost every data screen picks its figures out in the
    /// highlight, but the Balance Sheet issues ONE colour before its row loop and never changes
    /// it, so its figures are the same amber as its labels.</summary>
    Color ValueTint => _spec is { MonochromeValues: true }
        ? Of(ShopScreen.Label) : Of(ShopScreen.Highlight);

    static Color Of((byte R, byte G, byte B) c) => Color.Color8(c.R, c.G, c.B);

    /// <summary>The shadow's colour, from <see cref="ShopScreen.TextShadowRgba"/>.</summary>
    static readonly Color ShadowTint = Color.Color8(ShopScreen.TextShadowRgba.R, ShopScreen.TextShadowRgba.G,
                                                    ShopScreen.TextShadowRgba.B, ShopScreen.TextShadowRgba.A);

    /// <summary>⭐ Re-pick the chrome if the park has changed worlds since it was last loaded.
    /// ⚠ Guarded on the resolved FILENAME rather than on the world string, so a world that maps to
    /// the same chrome -- or a null that keeps mapping to the fallback -- costs one comparison and
    /// no reload.</summary>
    void RefreshChrome()
    {
        if (_world == null || _lib == null) return;
        string want = ShopScreen.ChromeFor(_world());
        if (want == _chromeName) return;
        var raw = _lib.ReadUi(want);
        if (raw == null) return;                 // ⚠ keep what we have rather than draw nothing
        try
        {
            var ssh = new Ssh(raw);
            var tex = ImageTexture.CreateFromImage(
                Image.CreateFromData(ssh.Width, ssh.Height, false, Image.Format.Rgba8, ssh.Pixels));
            _chrome = TrimRim(tex, _lib, want);
            _chromeName = want;
            GD.Print($"[laptop] chrome -> {want} (world \"{_world()}\")");
        }
        catch (Exception ex) { GD.PrintErr($"[laptop] chrome {want}: {ex.Message}"); }
    }

    /// <summary>⭐ The Build row, under the last row of information on a purchase screen. Master:
    /// "on the per-ride/etc page, add a build button at the bottom of the list of information".
    ///
    /// It uses text 801, `STR_MAINMENU_BUILD` -- the console's own Build string, the same one the
    /// main menu's Build option draws -- so it reads in whatever language the rest of the UI does
    /// instead of being an English literal wired into the port.</summary>
    void DrawBuildRow(float s, Vector2 o, SceneLayout layout)
    {
        _buildRowRect = new Rect2();
        if (!_buildRow) return;
        string label = Row(_buildTextId);
        if (string.IsNullOrEmpty(label)) return;
        if (layout[_spec.LabelElement] is not { } l) return;
        // ⚠ One row BELOW the last one, on the info list's own grid, and never past the panel's
        // inner edge -- the chrome has no content below row 469.
        int y = l.Y + LaptopScreen.RowStep * (_spec.Rows.Count + 1);
        if (y + LaptopMainMenu.RowStep > LaptopMainMenu.ContentBottom)
            y = LaptopMainMenu.ContentBottom - LaptopMainMenu.RowStep;
        _buildRowRect = Screen(new Rect2(l.X - 4, y, 260, LaptopMainMenu.RowStep), s, o);
        DrawRun(label, o + new Vector2(l.X, y) * s, s,
                Of(_buildHover ? ShopScreen.Highlight : ShopScreen.Label), l.Justify);
    }

    /// <summary>⭐⭐ THE BALANCE, SWOOPING IN FROM THE LEFT -- master's words. Build and its
    /// submenus are the screens where money decides whether a row is even worth clicking, so the
    /// figure follows the player into them and slides away with them.
    ///
    /// ⚠ The slide is in AUTHORED units and then scaled, like every other coordinate here, so it
    /// travels the same distance relative to the panel at 1080p as at 1440p.</summary>
    void DrawBalance(float s, Vector2 o)
    {
        if (_balance == null) return;
        if (BalanceAnchor() is not { } list) return;
        // ⭐ ONE ROW ABOVE THE LIST, in the chrome's empty band -- master: "above where the list
        // starts, in that empty space". The list keeps all eleven of its own rows.
        var rest = BalanceRow(list);
        float restX = rest.Position.X;
        float y = rest.Position.Y;
        // Ease-out cubic: fast off the mark, settling rather than stopping dead.
        float t = 1f - Mathf.Pow(1f - Mathf.Clamp(_swoop, 0f, 1f), 3f);
        float x = Mathf.Lerp(BalanceFrom, restX, t);
        string label = Row(LaptopMainMenu.BalanceTextId);
        DrawRun(label == null ? _balance : $"{label}  {_balance}",
                o + new Vector2(x, y) * s, s, Of(ShopScreen.Highlight), "left");
    }

    /// <summary>The authored x the balance flies in from -- off the panel's left edge, which the
    /// chrome's content starts 45 in from.</summary>
    const float BalanceFrom = -260f;

    public override void _Draw()
    {
        if (!Open) return;
        // ⚠⚠ EVERY HIT-TEST RECT IS CLEARED HERE, ONCE, BEFORE ANY OF THEM IS DRAWN.
        //
        // Master, 2026-09-27: "clicking on an empty progress bar on a shop's config panel... opens
        // a ride details page?" -- and it did. `_sliderRects` was cleared at the top of the SPEC
        // draw, but a shop draws through the `_rows` path, which cleared only its own
        // `_shopSliders`. So a ride's slider rects survived into a shop's panel, a click on that
        // spot raised SliderMoved, and the handler re-opened the last ride it had been shown.
        //
        // ⭐ The invariant these rects need is "what was drawn THIS frame", and the only place
        // that can hold unconditionally is before the branch. Clearing them in each path is what
        // let one path forget. This is the same fault as the build cache that survived a park
        // switch: a cache is only as good as its owner, and per-branch owners are not one owner.
        _sliderRects.Clear();
        _shopSliders.Clear();
        _rowArrows.Clear();
        _specRows.Clear();
        _pageArrows = new Rect2();
        _shopPriceArrows = new Rect2();
        FitToViewport();
        RefreshChrome();
        float s = Scale;
        var o = Origin;
        DrawTextureRect(_chrome, new Rect2(o, new Vector2(Native, Native) * s), false);
        if (_spec != null)
        {
            DrawSpecScreen(s, o);
            DrawBuildRow(s, o, LayoutFor(_spec));
            DrawBalance(s, o);
            DrawButtons(s, o);
            return;
        }
        if (_menu.Count > 0) { DrawMenu(s, o); DrawBalance(s, o); DrawButtons(s, o); return; }

        Vector2 At(SceneLayout.Element e) => o + new Vector2(e.X, e.Y) * s;

        // ⭐⭐ THE MODEL WINDOW, ON THE PATH THAT NEVER HAD ONE. Master, 2026-09-27:
        // "everything should animate on that menu".
        //
        // A shop, sideshow or toilet opened from its own RMB "Details" draws through THIS path --
        // the original `_rows` panel, written before the spec screens existed -- and the model
        // draw was only ever added to `DrawSpecScreen`. So the one screen you reach by right-
        // clicking a shop was the one screen with an empty model pane.
        //
        // ⚠ The tell was that an instrument in the spec draw printed NOTHING: not a wrong
        // rect, not a null texture, no line at all. A block that never runs looks exactly like a
        // block that runs and draws nothing -- see the build-cache and slider-rect faults above,
        // all three of them "two paths, one of them forgotten".
        //
        // ⭐ `main_i_shop_data`, `main_i_bathroom_data`, `main_i_sideshow_data` and the ride's
        // `main_i_ride_data` all author this window at the SAME frame -- row 208, col 315,
        // 147x240 -- so the ride page's geometry was already the right geometry for all of them.
        if (ModelTexture != null && _layout["Model"] is { } modelWindow)
            DrawTextureRect(ModelTexture,
                new Rect2(At(modelWindow), new Vector2(modelWindow.Width, modelWindow.Height) * s),
                false);

        if (_layout["ItemText"] is { } title)
            DrawRun(_title, At(title), s, Of(ShopScreen.Highlight), title.Justify);
        DrawButtons(s, o);

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
        {
            var qr = new Rect2(At(quality), new Vector2(quality.Width, quality.Height) * s);
            _shopSliders[ShopScreen.Selection.Quality] = qr;
            DrawSlider(qr, _quality, s, _selected == ShopScreen.Selection.Quality);
        }
        // ⚠ Only when the shop HAS an additive. A shop without one authors no control, and a
        // rect registered for a slider that is not drawn would eat clicks over empty chrome.
        if (_hasAdditive && _layout["AdditiveSlider"] is { } additive)
        {
            var ar = new Rect2(At(additive), new Vector2(additive.Width, additive.Height) * s);
            _shopSliders[ShopScreen.Selection.Additive] = ar;
            DrawSlider(ar, _additive, s, _selected == ShopScreen.Selection.Additive);
        }

        // ⭐ THE SHOP'S PRICE ARROWS. The scene authors CostItemArrows beside the price, and until
        // now nothing drew them -- so the one control on this screen the console lets you step was
        // invisible as well as dead.
        if (_arrows != null && _layout["CostItemArrows"] is { } pricearrow)
        {
            var psz = new Vector2(LaptopArrows.NativeWidth, LaptopArrows.NativeHeight) * s;
            _shopPriceArrows = new Rect2(At(pricearrow) - new Vector2(0, psz.Y / 2f), psz);
            DrawTextureRect(_arrows, _shopPriceArrows, false, ArrowTint);
        }
        if (_layout["CostItem"] is { } cost)
            DrawRun(Money(_price), At(cost), s,
                    Of(_selected == ShopScreen.Selection.SalePrice ? ShopScreen.Highlight : ShopScreen.Label),
                    cost.Justify);
    }

    /// <summary>One run, honouring the justification the scene file authored for that element.
    /// ⚠ `justify=center` in these files means centred ON the authored column, which is why the
    /// x is shifted by half the measured width rather than centred inside some box: no box is
    /// authored for a text element.</summary>
    /// <summary>⭐⭐ EVERY RUN OF LAPTOP TEXT IS DRAWN TWICE: a shadow, then the glyphs. Master:
    /// "missing drop shadows on all laptop ui text."
    ///
    /// ⭐ The offset is the HUD money readout's, which is DECODED -- `MoneyShadow = 2` console
    /// pixels, drawn from the same `Large.bff` in the same UI layer -- so this is the port's one
    /// measured text shadow applied to the rest of the text rather than a figure I picked.
    ///
    /// ⚠ What the shadow's COLOUR is, is not decoded either here or there: the console draws it in
    /// palette slot `colour + 8` and that slot has not been read. Black at 55% is a shadow's usual
    /// job, and it is marked as an assumption in both places rather than claimed.
    ///
    /// ⚠ It scales with `s` like every other coordinate, so the shadow sits 2 AUTHORED units off
    /// at any window size instead of 2 screen pixels, which would vanish at 1440p.</summary>
    void DrawRun(string text, Vector2 at, float s, Color tint, string justify)
    {
        if (string.IsNullOrEmpty(text)) return;
        var tex = _font.Render(text);
        if (tex == null) return;
        float w = tex.GetWidth() * s;
        // ⚠⚠ `justify=right` NEVER RIGHT-ALIGNS IN THIS ENGINE, and the census is complete:
        // exactly TWO scenes in the whole game author it -- main_fi_balancesheet and
        // main_fi_newloan -- and the code for BOTH is documented as drawing the column LEFT
        // (`FUN_001389C0(ctx, justify)` with mode 2 applies no shift; the balance sheet's justify
        // global has no reader at all). So mode 2 is "no shift", not "right".
        //
        // Master saw the consequence: "the amounts overlapping the row names". It is not a
        // rounding fudge -- MEASURED, "Repayment" is 111 native and ends at 156, while a value
        // right-aligned to col 215 would START at 127. A 29px overlap the font cannot avoid.
        // Left-aligned at 215 it clears the label by 59px. The scene says right; the code says
        // left; the code wins.
        float x = justify != null && justify.StartsWith("cent", StringComparison.OrdinalIgnoreCase)
                ? at.X - w / 2f
                : at.X;
        var size = new Vector2(w, tex.GetHeight() * s);
        float d = ShopScreen.TextShadowOffset * s;
        DrawTextureRect(tex, new Rect2(new Vector2(x + d, at.Y + d), size), false, ShadowTint);
        DrawTextureRect(tex, new Rect2(new Vector2(x, at.Y), size), false, tint);
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
    void DrawBar(Rect2 r, int value, float s, Color? tint = null)
    {
        // Measured off the art: 128 columns, the trough's interior spans 3..124.
        const float ArtWidth = 128f, InnerX = 3f, InnerWidth = 122f;
        float fraction = Mathf.Clamp(value / (float)ShopScreen.SatisfactionMax, 0f, 1f);
        // ⭐ Continuous, because the console is: FUN_00115530 computes (value - min) / (max - min)
        // as a FLOAT and hands that to the sprite. Nothing quantises to a tile.
        float cut = InnerX + InnerWidth * fraction;
        if (_barFill != null && cut > 0f)
            // ⚠ The FILL is tinted, never the frame: the console colours the bar's value, and the
            // trough is the same orange art on every screen.
            DrawTextureRectRegion(_barFill,
                new Rect2(r.Position, new Vector2(cut / ArtWidth * r.Size.X, r.Size.Y)),
                new Rect2(0, 0, cut, _barFill.GetHeight()), tint);
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
