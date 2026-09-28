namespace TPW.PS2.Data;

/// ⚠⚠ THE SCENE ELEMENT'S NAME DOES NOT TELL YOU THE WIDGET. `main_bh_items` names its two
/// widgets `ExcitementSlider` and `ReliabilitySlider`, and `main_bh_staff` names its one
/// `MotivationSlider` -- and all three are drawn as BARS. Master: "those arent meant to be
/// sliders, they're meant to be bars." This file had all three wrong because it read the names.
///
/// ⭐ THE WIDGET IS WHICH FUNCTION THE DRAW CALLS: `FUN_00115590` is a bar, `FUN_001DAAE0` a
/// slider. Counting those calls per draw gives the invariant in <see cref="Widgets"/>, and the
/// ride screen is the control that proves the method -- `FUN_001d5210` calls the bar exactly 4
/// times and the slider exactly 3, which is precisely what <see cref="Ride"/> already declared.
/// This is the same trap as picking `PROG_BAR` over `BARPROG` by name.
///
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
    IReadOnlyList<LaptopRow> Rows,
    /// <summary>⭐ The `◀▶` beside a PAGEABLE title, for the list screens whose subject you step
    /// through. Null on a screen about one fixed thing. ⚠ The scene's name for it differs per
    /// screen -- `ItemSelectArrows` on rides and shops, `ItemSelectArrow` on sideshows and
    /// toilets, `itemarrows` on staff -- so it is carried here rather than guessed at.</summary>
    string TitleArrowElement = null,
    /// <summary>⭐⭐ How far a ROW'S WIDGET steps per row, when several rows share ONE element.
    /// Zero (the default) means every widget sits at its own element's row, which is what every
    /// other screen does -- the ride's seven widgets are authored at 118/150/182/214/246/280/310,
    /// 32 apart then 34 then 30, so stepping them would drift by the third slider.
    ///
    /// ⚠ All Staff is the exception and the reason this exists: it authors ONE `infobars` frame
    /// at (215, 175) and draws THREE bars from it, stepping the same 32 the labels step
    /// (`findings/staff-management.md` §12.1). Without this the three would stack on one row.</summary>
    int WidgetStep = 0,
    /// <summary>⚠ Keep the labels on the authored grid and never re-centre one on its widget.
    ///
    /// The centring rule exists for All Rides, whose bars are authored 34 apart (148, 182, 216)
    /// against labels stepping 32 from 108, so by the third bar the label sits 8px above its own
    /// bar. Where a screen's widget already sits ON the grid the rule does the opposite: it moves
    /// the label OFF it, by half the difference between a 30-tall line and a 22-tall bar.
    ///
    /// ⚠⚠ Training is that case and the damage is not cosmetic. §12.3 authors its labels at
    /// InfoText + 32 / 64 / 96, so Skill Level belongs at 271; centring drew it at 267, which put
    /// its CLICK RECT at 267..297 against Monthly Wage's 239..269 -- two rows overlapping by 2px,
    /// where a click lands on whichever is tested first. Found by astraclaw on the merged
    /// screen, 2026-09-28.</summary>
    bool LabelsOnGrid = false,
    /// <summary>⚠ The row index the label grid's stepping STARTS from. Zero (the default) means
    /// row 0 sits on the element and every row steps from there, which is every screen but one.
    ///
    /// Game Options sets 2: its rows 0 and 1 are the sliders, drawn at their own label elements,
    /// and the four text rows below them step 32 from `TextOptions` -- so row 2 belongs AT the
    /// element, not two steps below it. The console reads those four from its own table at
    /// `0x2b6288`, which is the same statement in the other direction.</summary>
    int StepBase = 0,
    /// <summary>⚠ Draw a row's VALUE at its widget element's row rather than its label's.
    ///
    /// Research is why: the binder keeps only `ResearchItem`'s COLUMN, and the draw puts the item
    /// names on the BARS' row (MIPS `0x1b5a9c` reads `ResearchBars`' row, `0x1b5b40` the item's
    /// column). Labels are at 215 + 32i and the names at 220 + 32i -- five pixels apart. Taking
    /// the label's row instead would be a plausible five-pixel lie.</summary>
    bool ValueOnWidgetRow = false,
    /// <summary>⚠ The scene element holding this screen's GRAPH, or null. `findings/graph-widget.md`.
    /// The three graph screens author it as `graph`, 147x200 at (315, 208) -- the model window's
    /// frame, which is why no graph screen also has a model.</summary>
    string GraphElement = null,
    /// <summary>⚠ When a row carries its own value element, take only its COLUMN and keep the
    /// label's row. All Staff again: `InfoValues` is authored at row 220 and **that row is never
    /// read** -- `0x10b980` stores only `DAT_002AA8D4`, its column -- and Monthly Wage draws at
    /// the `infobars` COLUMN (215) on the label's own row. Everywhere else the element's full
    /// position is right, so this is off by default.</summary>
    bool ValueColumnOnly = false)
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

    /// <summary>⭐ The BUILD screen, `main_bh_items` -- what "Build &amp; Hire" opens for items.
    /// Drawn by `FUN_00198b48`, whose label ids read **780, 375, 969, 61, 1060** in that order.
    ///
    /// ⭐⭐ EXCITEMENT AND RELIABILITY REUSE THE RIDE SCREEN'S OWN IDS (61 and 1060), which is a
    /// corroboration rather than a coincidence: the same two numbers already label the ride info
    /// screen in <see cref="Ride"/>. The draw also carries the 32 row step ten times over and the
    /// (200,130) label colour, so it is the same widget family throughout.
    ///
    /// ⚠ The `.sce` comment says "PurchaseCost/Balance/NumberOwned/Excitment Reliability" and this
    /// time it is right -- but only because the last two are SLIDERS with their own elements, not
    /// text rows. The comment was checked against the draw rather than believed.</summary>
    public static readonly LaptopScreen Build = new(
        "main_bh_items.sce", 0x00, "ItemSelect", "TextItems", "NumericItems", "Model",
        new LaptopRow[]
        {
            new(780,  LaptopRowKind.Money),                          // Purchase Cost
            new(375,  LaptopRowKind.Money),                          // Balance
            new(969,  LaptopRowKind.Value),                          // No. Owned
            new(61,   LaptopRowKind.Bar, "ExcitementSlider"),        // Excitement
            new(1060, LaptopRowKind.Bar, "ReliabilitySlider"),       // Reliability
        });

    /// <summary>⭐ The HIRE screen, `main_bh_staff`. Drawn by `FUN_00199008`, label ids
    /// **773, 886, 833**. ⚠ Three rows, and the scene comment agrees for once.
    /// Motivation is both a label and a slider, which is why 833 carries an element.
    /// ⭐ The name pages through the tab's candidates by `StaffSelectArrows` (findings/staff.md §6).</summary>
    public static readonly LaptopScreen Hire = new(
        "main_bh_staff.sce", 0x00, "StaffSelect", "TextItems", "NumericItems", "Model",
        new LaptopRow[]
        {
            new(773, LaptopRowKind.Text),                            // Pay Grade
            new(886, LaptopRowKind.Money),                           // Monthly Wage
            new(833, LaptopRowKind.Bar, "MotivationSlider"),         // Motivation
        },
        TitleArrowElement: "StaffSelectArrows");

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
            // ⚠⚠ AND THE VALUE NAMES ARE CROSSED TOO, not just the arrows. Rendered and read:
            // with the names paired the obvious way, a prize of 30 printed "$3" against **Game
            // Price** and a price of 10 printed "$1" against **Prize Cost** -- each number under
            // the other's label. The cause is the authored rows: `PricePerGameValue` sits at 340
            // and `CostOfPrizeValue` at 372, so the one NAMED for the price is the HIGHER of the
            // two, and Cost of Prize is the higher LABEL. Pairing by position rather than by name
            // puts each number under its own label, which is the same correction this file already
            // makes for the arrows one line below -- the scene's names for this pair of rows do
            // not describe their contents.
            // ⭐ The magnitudes are right and were checked: the console formats both through
            // `FUN_00142908`, which is what <see cref="Money.Format"/> reproduces, tenths and all.
            new(832, LaptopRowKind.Money,  "PricePerGameValue", "PricePerGameArrow"),
            new(190, LaptopRowKind.Money,  "CostOfPrizeValue",  "CostOfPrizeArrow"),
        });

    /// <summary>⭐ The TOILET screen, `main_i_bathroom_data`, menu 23, drawn by `FUN_001DA008`.
    /// Labels are the `STR_SINGLEBOG_*` family. This completes the four "Single ..." screens, which
    /// the console builds on one shared base class (`FUN_001D9500`) -- see `findings/laptop-screens.md`.
    ///
    /// ⭐ THE ROWS CAME FROM THE DRAW, which is short and unusually clear: three `FUN_00138580`
    /// label calls at ids 168, 1077 and 132, stepped 0x20 apart, then two values and one bar. All
    /// three resolve inside `STR_SINGLEBOG_*`, which is the check that they belong to this screen
    /// and not a neighbour.
    ///
    /// ⭐⭐ AND THE SCENE FILE'S OWN COMMENTS AGREE, WHICH IS WHY THIS ONE IS SOLID. It labels
    /// `textoptions` *"text - users/lastcleaned/cleanliness"* -- the same three rows in the same
    /// order the draw emits them -- and `NumericItems` *"numbers - int/date"*. The comment is
    /// corroboration and not the authority (the ride's comment omits a row the draw plainly emits),
    /// but here the two independent readings match.
    ///
    /// ⚠⚠ LAST CLEANED IS A COUNT OF DAYS WITH A `d` SUFFIX -- "15d" -- and NOT a date. Master,
    /// 2026-09-27: "the last cleaned should have a d suffix". I had it as a date and this comment
    /// said so; the correction is recorded here because the wrong reading was the confident one.
    ///
    /// ⭐ What the draw shows is that the two values take DIFFERENT formatters, and THAT part was
    /// right: Users takes `FUN_00142B68`, which <see cref="Money"/> documents as the
    /// digits-with-thousands-separators writer, while Last Cleaned takes `FUN_00142948`, which is
    /// neither that nor the money formatter (`FUN_00142908`). ⚠ What was wrong was reading the
    /// scene's word "date" as the answer to what `FUN_00142948` writes. It writes days.
    ///
    /// ⚠ One bar and no sliders, which agrees with the constructor's widget census
    /// (`FUN_00115468` once, `FUN_001DA630` never) and with the scene's single `cleanlinessbar`.
    /// ⚠ Its title element is named `text`, not `ItemText` -- and it is authored at row 115 col 45,
    /// the SAME place as the shop's, which is how the mapping was settled rather than by the name.</summary>
    public static readonly LaptopScreen Toilet = new(
        "main_i_bathroom_data.sce", 23, "text", "textoptions", "NumericItems", "Model",
        new LaptopRow[]
        {
            new(168,  LaptopRowKind.Value),                     // Users         int
            new(1077, LaptopRowKind.Days),                      // Last Cleaned  "15d", FUN_00142948
            new(132,  LaptopRowKind.Bar, "cleanlinessbar"),     // Cleanliness
        });

    /// <summary>⭐⭐ THE "ALL ..." LIST SCREENS. Structurally these are the same thing as the
    /// "Single ..." screens -- a title, a label column, a value column, widgets and a model -- with
    /// one difference: the title is a SELECTOR you page through (`ItemSelect` + its arrows) instead
    /// of a fixed name. So they need no new drawing, only rows. Which asset kinds each one lists is
    /// <see cref="LaptopListScreens"/>; this is what it draws about the selected item.
    ///
    /// ⭐ EVERY ROW BELOW IS THREE AGREEING MEASUREMENTS, not one reading:
    /// 1. the LABELS and their order, from the draw's `FUN_00138580` calls (ids read out of the
    ///    instructions, because these draws decompile badly at their vtable entry points);
    /// 2. how many of the rows are BARS, from the CONSTRUCTOR's `FUN_00115468` count -- the draws
    ///    call the bar function zero times because the base draws them;
    /// 3. which of the rest are money, from the formatter each value goes through: `FUN_00142908`
    ///    is the money writer and `FUN_00142B68` the plain digits one (see <see cref="Money"/>).
    ///
    /// ⭐ That formatter census has two controls and passes both: Single Shop comes out
    /// `digits, money, money, money` and Single Toilet `digits, date`, which is exactly what
    /// <see cref="Shop"/> and <see cref="Toilet"/> already declare from other evidence.
    ///
    /// ⚠ And the bar ELEMENT names match the bar LABELS one for one on every screen here
    /// (ExcitementBar against Excitement, and so on), which is the check that the bars were paired
    /// with the right rows rather than just counted.</summary>
    public static readonly LaptopScreen AllRides = new(
        "main_i_ride.sce", 13, "ItemSelect", "InfoText", "InfoVal", "ThingModel",
        new LaptopRow[]
        {
            new(771, LaptopRowKind.Value),                           // Users            digits
            new(857, LaptopRowKind.Bar, "ExcitementBar"),            // Excitement
            new(128, LaptopRowKind.Bar, "StateOfRepairBar"),         // State of Repair
            new(636, LaptopRowKind.Bar, "RemainingLifeBar"),         // Remaining Life
        }, "ItemSelectArrows");

    /// <summary>⚠ Two of its four labels are `STR_SINGLESHOP_*` rather than `STR_ALLSHOPS_*`
    /// (Takings 106 and Profit 986) -- the console reuses the single-shop strings for the same two
    /// concepts. That is the disc's own choice and is left as it is.</summary>
    public static readonly LaptopScreen AllShops = new(
        "main_i_shop.sce", 16, "ItemSelect", "InfoText", "InfoValues", "Model",
        new LaptopRow[]
        {
            new(691, LaptopRowKind.Value),                           // Customers        digits
            new(106, LaptopRowKind.Money),                           // Takings          $
            new(986, LaptopRowKind.Money),                           // Profit           $
            new(451, LaptopRowKind.Bar, "satisfactionbar"),          // Satisfaction
        }, "ItemSelectArrows");

    public static readonly LaptopScreen AllSideshows = new(
        "main_i_sideshow.sce", 20, "ItemSelect", "InfoText", "InfoValues", "Model",
        new LaptopRow[]
        {
            new(488, LaptopRowKind.Value),                           // Customers        digits
            new(365, LaptopRowKind.Money),                           // Total Profit     $
            new(907, LaptopRowKind.Bar, "excitementbar"),            // Excitement
            new(537, LaptopRowKind.Bar, "satisfactionbar"),          // Satisfaction
        }, "ItemSelectArrow");

    /// <summary>⚠ ONE ROW, and its draw is the shortest on the laptop: a single label
    /// (`STR_ALLTOILETS_OVERALL_CLEANLINESS`) and a single bar, with NO value formatter called at
    /// all -- which agrees with the constructor's one bar and the scene's `CleanlinessBar`.
    ///
    /// ⚠ This screen authors NO value column. `CleanlinessText` is given as the value element
    /// only because the lookup cannot take a null, and it is never consulted: the one row is a Bar
    /// that carries its own element.</summary>
    public static readonly LaptopScreen AllToilets = new(
        "main_i_bathroom.sce", 26, "ItemSelect", "CleanlinessText", "CleanlinessText", "Model",
        new LaptopRow[]
        {
            new(269, LaptopRowKind.Bar, "CleanlinessBar"),           // Cleanliness
        }, "ItemSelectArrow");

    /// <summary>⭐⭐ ALL STAFF, `main_i_staff` -- the screen this port held back until the shape of
    /// it was settled. Drawn by `0x10C138`; layout from `findings/staff-management.md` §12.1.
    ///
    /// ⚠⚠ IT BREAKS THE ONE-LABEL-ONE-WIDGET RULE every other screen keeps, which is exactly why
    /// it waited: the scene authors a SINGLE `infobars` frame at (215, 175) and the draw puts
    /// THREE bars through it, stepping the same 32 the labels step. Hence
    /// <see cref="LaptopScreen.WidgetStep"/>.
    ///
    /// ⚠ And `InfoValues` is authored at row 220 which the console NEVER READS -- `0x10B980`
    /// keeps only its column -- while Monthly Wage draws at the `infobars` COLUMN instead. Hence
    /// <see cref="LaptopScreen.ValueColumnOnly"/>. Both are this screen's, not general.
    ///
    /// ⚠ Rows 555 and 676 (Status) are NOT drawn, and "Overall Motivation" (`0x10C0B0`) is
    /// computed every draw and thrown away (MIPS `0x10C240..0x10C248`). Neither is here, because
    /// the console does not show them.</summary>
    public static readonly LaptopScreen AllStaff = new(
        "main_i_staff.sce", 15, "item", "textoptions", "InfoValues", "Model",
        new LaptopRow[]
        {
            new(683, LaptopRowKind.Bar,   "infobars"),   // Skill Level -- level x 25
            new(827, LaptopRowKind.Bar,   "infobars"),   // Motivation  -- ((100 - tired) + morale) / 2
            new(363, LaptopRowKind.Bar,   "infobars"),   // Tiredness
            new(669, LaptopRowKind.Value),               // Time Employed -- InfoValues column
            new(886, LaptopRowKind.Money, "infobars"),   // Monthly Wage  -- infobars COLUMN
        },
        "itemarrows", WidgetStep: RowStep, ValueColumnOnly: true);

    /// <summary>⭐ TRAINING -- the one laptop screen you BUY from rather than read. Layout from
    /// `findings/staff-management.md` §12.3; ctor `0x1ff4a8`, drawn by `0x1ff830`.
    ///
    /// ⚠⚠ EVERY FIGURE ON IT IS THE LEVEL *AFTER* TRAINING, never the member's current one: the
    /// bar is `min(L + 1, 5) x 25` and the wage is `wage(kind, min(L + 1, 5))`. A screen that
    /// showed the present skill and wage would look entirely plausible and be one level stale on
    /// every row at once, which is why this is called out here and not left to the caller.
    ///
    /// ⚠ The first row's LABEL is per-member -- the level being offered -- so this is a factory
    /// rather than a static. Its text id comes from `StaffTables.TrainingLevelTextRows`, which is
    /// a TABLE and not arithmetic: the ids are 110, 111, 112, 114, 116, because 113 and 115 are
    /// `STR_PARKSTATS_GOOD` and `STR_PARKSTATS_PARK_VALUE`. `110 + L` would print "Good" as the
    /// fourth training level.
    ///
    /// ⚠ `TrainingVal` (250, 125) is authored in the scene and bound by the ctor, but `0x1ff830`
    /// never draws it, so it is not a row here.</summary>
    public static LaptopScreen TrainingFor(int levelTextRow) => new(
        "main_i_staff_opts_training.sce", 27, "Item", "InfoText", "CostWageVal", "Model",
        new LaptopRow[]
        {
            new(levelTextRow, LaptopRowKind.Text),         // "Level L+2" -- the level being bought
            new(118, LaptopRowKind.Money),                 // Training Cost
            new(886, LaptopRowKind.Money),                 // Monthly Wage -- AFTER training
            new(709, LaptopRowKind.Bar, "SkillLevelBar"),  // Skill Level  -- min(L+1,5) x 25
        }, LabelsOnGrid: true);

    /// <summary>⭐ SINGLE STAFF -- what a person's own screen offers. §12.2; ctor `0x1d8ee8`,
    /// drawn by `0x1d9088`. **There are no statistics on it**: `Staffname` (45, 115), then the
    /// option texts down `textoptions` (45, 175) stepping 32, and the model.
    ///
    /// ⚠ The OPTIONS ARE NOT FIXED -- Fire drops out for a mechanic who is mid-job and Training
    /// at level 4 -- so the rows are whatever `ParkStaff.SingleStaffOptions` returned, in its
    /// order, and this is a factory. Grab is never added (§5).
    ///
    /// ⚠ The scene authors no value column, and no row here carries a value, so `ValueElement`
    /// names `textoptions` purely because the layout lookup cannot take a null. Nothing reads it.
    /// </summary>
    public static LaptopScreen SingleStaffFor(IReadOnlyList<int> optionRows)
    {
        var rows = new LaptopRow[optionRows.Count];
        for (int i = 0; i < optionRows.Count; i++) rows[i] = new LaptopRow(optionRows[i], LaptopRowKind.Text);
        return new LaptopScreen("main_i_staff_opts.sce", 18, "Staffname", "textoptions", "textoptions",
                                "Model", rows);
    }

    /// <summary>⭐ GAME OPTIONS (menu id 1). `findings/hardcoded-screens.md`; draw `0x13a258`,
    /// input `0x139df0`, binder `FUN_00139968`.
    ///
    /// ⚠ NOT a Single-item screen: its vtable is 16 slots off the ROOT base, so there is no state
    /// machine and no slot 26 -- the draw IS slot 2. It also registers **no title** (the ctor
    /// makes no `FUN_00165948(this+0xa0, ...)` call), which is why this passes an empty one.
    ///
    /// ⚠⚠ "TV Mode" is NOT on this screen. An earlier note had the ctor registering
    /// `STR_OPTIONS_TV_MODE`; that came from the immediate `0x328` at `0x139cc8`, which is
    /// `addiu s0, s6, 0x328` -- a FIELD ADDRESS, not a text id. Six rows, no heading.
    ///
    /// ⚠ The tutorial and vibration rows' text ids are the CURRENT STATE (On/Off are different
    /// strings, not a suffix), so this is a factory.</summary>
    public static LaptopScreen GameOptionsFor(bool tutorialOn, bool vibrationOn) => new(
        "main_gameoptions.sce", 1, "TextOptions", "TextOptions", "TextOptions", "TextOptions",
        new LaptopRow[]
        {
            new(399, LaptopRowKind.Slider, "MusicSlider", LabelElement: "MusicSliderText"),
            new(823, LaptopRowKind.Slider, "SfxSlider",   LabelElement: "SfxSliderText"),
            new(tutorialOn  ? 210 : 843, LaptopRowKind.Text),   // STR_FRONTEND_SPEECH_ON / _OFF
            new(vibrationOn ? 212 : 881, LaptopRowKind.Text),   // STR_OPTIONS_VIBRATION_ON / _OFF
            new(235, LaptopRowKind.Text),                       // Save Game
            new(763, LaptopRowKind.Text),                       // Quit Current Game
        },
        LabelsOnGrid: true, StepBase: 2);

    /// <summary>Game Options' row indices, named rather than spelled at the use site.</summary>
    public const int OptMusic = 0, OptSfx = 1, OptTutorial = 2, OptVibration = 3,
                     OptSaveGame = 4, OptQuit = 5;

    /// <summary>⭐ RESEARCH (menu id 9). `findings/hardcoded-screens.md`; draw `0x1b5920`, input
    /// `0x1b5668`, binder `FUN_001b5218`. Title 1013 `STR_RESEARCH_RESEARCH`.
    ///
    /// The five rows ARE the manager's five slots, one per category. Each shows its project's
    /// name (or "Nothing") and a progress bar, RED while the slot is idle and GREEN while it is
    /// researching.
    ///
    /// ⚠⚠ THE BUDGET SLIDER WAS CUT ON PS2 -- do not port it. The scene does not author
    /// `OverallBar` or `OverallText`, their globals are written by the binder only when authored
    /// and **nothing reads them**, and the constructor's one slider is never drawn or updated by
    /// either slot. `STR_RESEARCH_OVER_ALL_RESEARCH`, `_ALTER_RESEARCH` and `_APPLY` have no
    /// reader in the class. The budget is simply forced to 100 when the screen opens.
    ///
    /// ⚠ One authored element, five bars, stepping the same 32 the labels step -- the All Staff
    /// shape, hence <see cref="LaptopScreen.WidgetStep"/>.</summary>
    public static readonly LaptopScreen Research = new(
        "main_research.sce", 9, "TextOptions", "TextOptions", "ResearchItem", "TextOptions",
        new LaptopRow[]
        {
            new(531, LaptopRowKind.Bar, "ResearchBars"),   // Rides
            new(65,  LaptopRowKind.Bar, "ResearchBars"),   // Shops
            new(185, LaptopRowKind.Bar, "ResearchBars"),   // Sideshows
            new(155, LaptopRowKind.Bar, "ResearchBars"),   // Features
            new(834, LaptopRowKind.Bar, "ResearchBars"),   // Upgrades
        },
        WidgetStep: RowStep, ValueOnWidgetRow: true);

    /// <summary>`STR_RESEARCH_NOTHING` -- an idle slot's item column.</summary>
    public const int ResearchNothingTextId = 605;

    /// <summary>⭐ FINANCE STATISTICS (menu id 21). `findings/graph-widget.md` §2; draw
    /// `FUN_00135620`, binder `FUN_00133960`. Five series, ONE shown at a time -- selecting an
    /// item clears every toggle then flips that one, and the first is on by default.</summary>
    public static readonly LaptopScreen FinanceStats = new(
        "main_fi_financestats.sce", 21, "Items", "Items", "Items", "Items",
        new LaptopRow[]
        {
            new(447, LaptopRowKind.Text),   // Money In
            new(565, LaptopRowKind.Text),   // Gate Takings
            new(954, LaptopRowKind.Text),   // Shop Takings
            new(818, LaptopRowKind.Text),   // Sideshow Takings
            new(371, LaptopRowKind.Text),   // Staff Wages
        },
        LabelsOnGrid: true, GraphElement: "graph");

    /// <summary>⭐ OVERALL STATISTICS (menu id 22). §3; draw `FUN_00135190`. Two series.
    /// ⚠ Bank Balance can go NEGATIVE and `min` is still 0, so a negative month clamps to the
    /// bottom edge rather than plotting below it. That is the console's, not a guard of ours.</summary>
    public static readonly LaptopScreen OverallStats = new(
        "main_fi_overallstats.sce", 22, "Items", "Items", "Items", "Items",
        new LaptopRow[]
        {
            new(3,   LaptopRowKind.Text),   // Bank Balance
            new(813, LaptopRowKind.Text),   // Park Value
        },
        LabelsOnGrid: true, GraphElement: "graph");

    /// <summary>The five FinanceStats series colours (`0x35e4a0 + 4i`) and the two OverallStats
    /// ones (`0x35e490`), in row order. ⚠ Straight off the disc -- they are the only thing telling
    /// the player which line is which, because `GraphLegend` is bound and never read.</summary>
    public static readonly (byte R, byte G, byte B)[] FinanceSeriesRgb =
        { (254, 1, 1), (231, 102, 27), (254, 254, 1), (1, 176, 60), (64, 64, 64) };
    public static readonly (byte R, byte G, byte B)[] OverallSeriesRgb =
        { (254, 0, 0), (231, 102, 27) };

    /// <summary>⭐ The PARK STATISTICS menu (id 8), in draw order at (45, 115 + 32i).
    /// `findings/parkstats-screens.md` §2. All four rows are appended ENABLED, so all four are
    /// selectable; the laptop's title stays 530 "Information" on the menu and on every page.
    ///
    /// ⚠ Note the fourth: Awards is a page of this menu, which makes five screens on this one
    /// class (with Gold Tickets), not four.</summary>
    public static readonly (int TextId, string Opens)[] ParkStatsMenu =
    {
        (841,  "visitorinfo"),   // STR_PARKSTATS_PARK_INFORMATION -- "Visitor Information"
        (1063, "statistics"),    // STR_PARKSTATS_GRAPH            -- the graph page
        (164,  "parkfinance"),   // STR_PARKSTATS_DETAILS          -- "Park Finance"
        (101,  "awards"),        // STR_GIZMO_CPP_AWARDS           -- "Awards"
    };

    /// <summary>⭐ PARK STATISTICS / Statistics (menu id 24) -- the third graph screen.
    /// `findings/graph-widget.md` §4; draw `FUN_00185c48`.
    /// ⚠ `GraphLegend` is authored and NEVER READ, so nothing draws a colour key.</summary>
    public static readonly LaptopScreen ParkStatistics = new(
        "main_ps_statistics.sce", 24, "Items", "Items", "Items", "Items",
        new LaptopRow[]
        {
            new(512, LaptopRowKind.Text),   // People In Park
            new(618, LaptopRowKind.Text),   // Arrival Rate
            new(926, LaptopRowKind.Text),   // Happiness      (value is int + "%")
            new(539, LaptopRowKind.Text),   // Time In Park   (value is int + "d")
            new(727, LaptopRowKind.Text),   // Overall Rating (value is int + "%")
        },
        LabelsOnGrid: true, GraphElement: "graph");

    /// <summary>The five Statistics series colours (`0x364150`), in row order.</summary>
    public static readonly (byte R, byte G, byte B)[] ParkStatsSeriesRgb =
        { (254, 1, 1), (231, 102, 27), (254, 254, 1), (1, 176, 60), (1, 178, 235) };

    public static readonly LaptopScreen[] AllList = { AllRides, AllShops, AllSideshows, AllToilets };

    /// <summary>The four "Single ..." item screens the console builds on one base class. ⚠ Build
    /// and Hire are NOT here: they are sub-panels of Build &amp; Hire, not registered scenes.</summary>
    public static readonly LaptopScreen[] SingleItem = { Shop, Ride, Sideshow, Toilet };
}

/// <summary>One labelled row. <paramref name="Element"/> names the scene element that carries the
/// row's widget or value, when it has one of its own; a null means the row's value sits on the
/// screen's shared value column at the label's own height.</summary>
public readonly record struct LaptopRow(int TextId, LaptopRowKind Kind, string Element = null,
                                       string ArrowElement = null,
                                       /// <summary>⚠ The scene element this row's LABEL sits at,
                                       /// when the row does not sit on the screen's label grid.
                                       /// Null (the default) means the grid, which is what every
                                       /// screen built before Game Options does.
                                       ///
                                       /// Game Options is why this exists: its first two rows are
                                       /// authored at their own elements (`MusicSliderText` row
                                       /// 116, `SfxSliderText` row 150) while rows 2..5 step 32
                                       /// from `TextOptions` at 185. One grid cannot describe
                                       /// both.</summary>
                                       string LabelElement = null);

/// <summary>⭐⭐ HOW MANY BARS AND SLIDERS EACH DRAW ACTUALLY EMITS, counted as calls to
/// `FUN_00115590` (bar) and `FUN_001DAAE0` (slider) in the decompiled draw. A spec whose row kinds
/// disagree with these has been written from the element names again.</summary>
public static class LaptopWidgetCounts
{
    /// <summary>screen -> (bars, sliders), read off the draw.</summary>
    public static readonly (string Screen, string Draw, int Bars, int Sliders)[] Decoded =
    {
        ("Ride",     "FUN_001d5210", 4, 3),   // the CONTROL: matches Ride's spec exactly
        ("Build",    "FUN_00198b48", 2, 0),
        ("Hire",     "FUN_00199008", 1, 0),
    };
}

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
    /// <summary>⭐ A count of DAYS, written with a `d` suffix -- "15d". Master, 2026-09-27:
    /// "the last cleaned should have a d suffix". `FUN_00142948` is the writer; it is the toilet
    /// screen's Last Cleaned and is neither the money formatter nor the plain-digits one.</summary>
    Days,
    /// <summary>An orange frame with a cyan fill; full scale 100.</summary>
    Bar,
    /// <summary>A yellow track with a green knob.</summary>
    Slider,
}
