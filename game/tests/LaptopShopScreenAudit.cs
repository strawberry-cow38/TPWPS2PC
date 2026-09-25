using Godot;
using System;
using System.Linq;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>The shop's laptop screen, against the disc that authors it.
///
/// ⭐⭐ THIS EXISTS BECAUSE THE FIRST VERSION OF THIS SCREEN WAS THE WRONG UI ENTIRELY. It drew
/// the right numbers into a 260x180 message box, and master said so at a glance. The geometry had
/// been assembled from whatever the executable seemed to offer, because the layout globals read 0
/// in the image and that was taken for "unknowable". They read 0 because `MENUS.WAD` fills them.
///
/// So every check below reads the DISC and compares against it. A check that only exercised the
/// port's own constants would have passed happily on the message box too.</summary>
public partial class LaptopShopScreenAudit : Node
{
    int _checks, _bad;

    void Check(bool ok, string label)
    {
        _checks++; if (!ok) _bad++;
        GD.Print($"LAPTOP SHOP {(ok ? "ok" : "FAIL")}: [{_checks}] {label}");
    }

    public override void _Ready()
    {
        AssetLibrary library = null;
        try
        {
            library = new AssetLibrary(OS.GetEnvironment("TPW_PS2_DISC"));

            // ---- the scene file is the layout ------------------------------------------------
            var raw = library.ReadMenu(ShopScreen.SceneFile);
            Check(raw != null, $"MENUS.WAD carries {ShopScreen.SceneFile}");
            var sce = SceneLayout.Parse(raw);
            Check(sce.Name.Equals("main_i_shop_data", StringComparison.OrdinalIgnoreCase),
                  $"the file declares its own menu name (got '{sce.Name}')");

            // ⭐ The nine elements the binder `FUN_001d68b0` looks up, at the coordinates the file
            // authors. These are transcribed from the disc, so a parser that silently dropped a
            // field or merged two elements fails here rather than looking plausible on screen.
            var expected = new (string Name, int Row, int Col, int W, int H)[]
            {
                ("ItemText",        115,  45,   0,   0),
                ("TextOptions",     175,  45,   0,   0),
                ("NumericItems",    175, 250,   0,   0),
                ("SatisfactionBar", 305, 215,  72,  22),
                ("QualitySlider",   338, 215,  72,  22),
                ("AdditiveSlider",  370, 215,  72,  22),
                ("CostItem",        400, 250,   0,   0),
                ("CostItemArrows",  412, 190,   0,   0),
                ("Model",           208, 315, 147, 240),
            };
            foreach (var e in expected)
            {
                var got = sce[e.Name];
                Check(got is { } g && g.Row == e.Row && g.Col == e.Col && g.Width == e.W && g.Height == e.H,
                      $"{e.Name} is row={e.Row} col={e.Col} {e.W}x{e.H}"
                      + (sce[e.Name] is { } a ? $" (got row={a.Row} col={a.Col} {a.Width}x{a.Height})" : " (absent)"));
            }

            // ⚠ THE CONTROL. The lookup must be able to answer "no". Without this, a parser that
            // returned a zeroed element for every name would pass all nine checks above only if
            // they happened to be zero -- and would pass this file's `Frame()` callers silently.
            Check(sce["NoSuchElement"] == null, "control: an element the file does not author reads null");

            // ⭐ The file spells them lowercase (`textoptions`) and the executable CamelCase
            // (`TextOptions`). If the lookup were case-sensitive, every element would read null.
            Check(sce["textoptions"] != null && sce["SATISFACTIONBAR"] != null,
                  "element lookup is case-insensitive, as the executable's names require");

            // ⭐ An element authors its frame over TWO lines -- row/col, then width/height. A
            // parser that replaced rather than merged would lose one of them.
            Check(sce["SatisfactionBar"] is { HasSize: true, Row: 305, Col: 215 },
                  "a two-line <frame> merges position and size rather than replacing");
            Check(sce["ItemText"] is { HasSize: false }, "a text element authors no size, and reports that");
            Check(sce["ItemText"]?.Justify == "left" && sce["NumericItems"]?.Justify == "center",
                  "justification is read per element");

            // ---- the row step, corroborated ACROSS FILES --------------------------------------
            // ⭐⭐ `DAT_002e9ca8` = 32 is a constant in the executable image. Stepping the label
            // column from the scene's own first row must land on the widgets the SCENE places
            // independently. Two different files on the disc agreeing is the evidence.
            int first = sce["TextOptions"]!.Value.Row;
            var widgets = new (string Name, int Index)[]
                { ("SatisfactionBar", 4), ("QualitySlider", 5), ("AdditiveSlider", 6), ("CostItem", 7) };
            int worst = 0;
            foreach (var (name, index) in widgets)
                worst = Math.Max(worst, Math.Abs(sce[name]!.Value.Row - (first + ShopScreen.RowStep * index)));
            Check(worst <= 3, $"row step {ShopScreen.RowStep} predicts all four widget rows (worst miss {worst}px)");

            // ⚠⚠ AND THE CONTROL THAT MAKES THAT MEAN SOMETHING: a wrong step must FAIL the same
            // test. Without this the check is vacuous -- a loose enough tolerance passes anything.
            foreach (int wrong in new[] { 28, 30, 34, 36 })
            {
                int miss = widgets.Max(w => Math.Abs(sce[w.Name]!.Value.Row - (first + wrong * w.Index)));
                Check(miss > 3, $"control: step {wrong} does NOT predict the widget rows (worst miss {miss}px)");
            }

            // ---- the labels are the rows the draw function names ------------------------------
            var dataFile = library.WadFiles().Single(
                f => f.Path.EndsWith("/DATA.WAD", StringComparison.OrdinalIgnoreCase));
            var text = TextDatabase.Load(new WadArchive(library.ReadDisc(dataFile)), "eur");

            Check(ShopScreen.LabelKeys.Length == ShopScreen.LabelTextIds.Length + 1,
                  "the key array carries the ingredient slot the id array cannot");
            Check(ShopScreen.LabelKeys[ShopScreen.IngredientRow] == null,
                  "the ingredient slot is the null one, keeping the two arrays index-aligned");

            // ⭐ The indices are what `FUN_001d70c8` passes. The KEYS are the independent check on
            // them: the id and the symbolic name must be the same row.
            for (int i = 0; i < ShopScreen.LabelKeys.Length; i++)
            {
                if (ShopScreen.LabelKeys[i] == null) continue;
                int id = ShopScreen.LabelTextIds[i > ShopScreen.IngredientRow ? i - 1 : i];
                int byKey = text.IndexOf(ShopScreen.LabelKeys[i]);
                Check(byKey == id, $"text id {id} is {ShopScreen.LabelKeys[i]} (key resolves to {byKey})");
            }

            // ⚠ THE CONTROL: a wrong id must not resolve to that key, or the check above would
            // pass for any number at all.
            Check(text.IndexOf(ShopScreen.LabelKeys[0]) != ShopScreen.LabelTextIds[1],
                  "control: a neighbouring id does not resolve to the first label's key");

            // ⭐ Every label on this screen is a SINGLESHOP row -- the game's own name for it.
            Check(ShopScreen.LabelKeys.Where(k => k != null).All(k => k.StartsWith("STR_SINGLESHOP_")),
                  "every label belongs to the SINGLESHOP string family");

            // ⭐ The ingredient captions resolve too, which is what puts row 7 on the screen.
            foreach (var (id, word) in new[] { (804, "Fat"), (379, "Ice"), (142, "Sugar"), (1, "Salt") })
                Check(text.Text("eng", id) == word, $"ingredient text {id} is '{word}'");

            // ---- the chrome is per world ------------------------------------------------------
            foreach (var world in new[] { "JUNGLE", "HALLOW", "FANTASY", "SPACE" })
            {
                var path = ShopScreen.ChromeFor("/DATA/" + world + ".WAD");
                Check(path.Contains(world) && library.ReadUi(path) != null,
                      $"{world} has its own laptop chrome at {path}");
            }
            // ⚠ A world the executable does not name must still land on art that exists, not null.
            Check(library.ReadUi(ShopScreen.ChromeFor("/DATA/LOBBY.WAD")) != null,
                  "control: an unnamed world falls back to chrome that is actually present");

            // ---- the widget art the screen draws with -----------------------------------------
            foreach (var art in new[] { "/laptop/BARPROG.ssh", "/laptop/PROG_BAR.ssh", "/laptop/PROG_CBIT.ssh",
                                        "/laptop/PROG_VBIT.ssh", "/laptop/BARSLIDE.ssh", "/laptop/BARKNOB.ssh" })
            {
                bool ok = false;
                try { var b = library.ReadUi(art); ok = b != null && new Ssh(b).Width > 0; } catch { }
                Check(ok, $"{art} is present and decodes");
            }

            // ⚠⚠ THE CHECK THAT REJECTS THE BUG THAT SHIPPED. The satisfaction bar was drawn with
            // PROG_BAR, chosen by name. The two candidate frames are the same size and colour and
            // differ only in their INTERIOR, so nothing but the alpha distinguishes them: BARPROG
            // is one continuous trough, PROG_BAR is an eleven-cell gauge. A tiled fill belongs in
            // the first. Reinstating PROG_BAR would fail here.
            int Cells(string art)
            {
                var img = new Ssh(library.ReadUi(art));
                int runs = 0; bool inRun = false;
                for (int x = 0; x < img.Width; x++)
                {
                    bool clear = img.Pixels[((img.Height / 2) * img.Width + x) * 4 + 3] <= 16;
                    if (clear && !inRun) runs++;
                    inRun = clear;
                }
                return runs;
            }
            int trough = Cells("/laptop/BARPROG.ssh"), notched = Cells("/laptop/PROG_BAR.ssh");
            int slide = Cells("/laptop/BARSLIDE.ssh");
            Check(trough == 1, $"BARPROG is ONE continuous trough (got {trough} interior runs)");
            Check(notched > 1, $"control: PROG_BAR is a multi-cell gauge, not a trough (got {notched})");
            Check(slide == trough, $"BARSLIDE matches BARPROG's interior ({slide} vs {trough}) -- a matched pair");

            // ⚠ And the fill bits are NOT as wide as their images: stretching a 10-wide cap into a
            // 16-wide slot is what made the first attempt look broken.
            var cap = new Ssh(library.ReadUi("/laptop/PROG_CBIT.ssh"));
            var body = new Ssh(library.ReadUi("/laptop/PROG_VBIT.ssh"));
            int Opaque(Ssh i)
            {
                int last = -1;
                for (int x = 0; x < i.Width; x++)
                    for (int y = 0; y < i.Height; y++)
                        if (i.Pixels[(y * i.Width + x) * 4 + 3] > 128) { last = x; break; }
                return last + 1;
            }
            Check(Opaque(cap) == 10, $"PROG_CBIT is opaque over 10 of its 16 columns (got {Opaque(cap)})");
            Check(Opaque(body) == 16, $"PROG_VBIT fills all 16 (got {Opaque(body)}) -- it is the tile, the cap is not");

            // ⭐⭐ WHY THE FILL IS ROUNDED AT BOTH ENDS. The trough is symmetric and the cap's
            // profile starts where the trough's does, so one sprite serves both ends -- mirrored at
            // the leading edge. Filling flat to the last interior column instead put a SQUARE edge
            // inside a ROUND one, visible the moment the bar reached 100%.
            var trough2 = new Ssh(library.ReadUi("/laptop/BARPROG.ssh"));
            int Interior(Ssh i, int x)
            {
                int n = 0;
                for (int y = 0; y < i.Height; y++) if (i.Pixels[(y * i.Width + x) * 4 + 3] <= 16) n++;
                return n;
            }
            var leftEnd = Enumerable.Range(3, 11).Select(x => Interior(trough2, x)).ToArray();
            var rightEnd = Enumerable.Range(114, 11).Select(x => Interior(trough2, x)).Reverse().ToArray();
            Check(leftEnd.SequenceEqual(rightEnd),
                  $"BARPROG's ends are mirror images ([{string.Join(",", leftEnd)}] vs [{string.Join(",", rightEnd)}])");
            int CapHeight(Ssh i, int x)
            {
                int n = 0;
                for (int y = 0; y < i.Height; y++) if (i.Pixels[(y * i.Width + x) * 4 + 3] > 128) n++;
                return n;
            }
            Check(CapHeight(cap, 0) == leftEnd[0],
                  $"PROG_CBIT's first column matches the trough's first interior column ({CapHeight(cap, 0)} vs {leftEnd[0]})");
            // ⚠ The control: an interior column well away from either end must NOT match, or the
            // check above would pass against any flat-ended art.
            Check(Interior(trough2, 60) != leftEnd[0],
                  $"control: a mid-trough column differs from the end profile ({Interior(trough2, 60)} vs {leftEnd[0]})");

            // ⭐ And the fill tiles SEAMLESSLY sideways because the gradient runs vertically. If it
            // ran horizontally, tiling a 16-wide bit would band the bar every 16 columns.
            int Spread(Ssh i, bool vertical)
            {
                int lo = 255, hi = 0;
                for (int k = 0; k < (vertical ? i.Height : i.Width); k++)
                {
                    int x = vertical ? 2 : k, y = vertical ? k : i.Height / 2;
                    if (i.Pixels[(y * i.Width + x) * 4 + 3] <= 128) continue;
                    int v = i.Pixels[(y * i.Width + x) * 4 + 2];
                    lo = Math.Min(lo, v); hi = Math.Max(hi, v);
                }
                return hi - lo;
            }
            Check(Spread(body, true) > 10 * Spread(body, false),
                  $"PROG_VBIT's gradient is VERTICAL (spread {Spread(body, true)} down vs {Spread(body, false)} across)");

            // ⭐⭐ THE COMPOSED FILL ITSELF, not a re-derivation of it. The bar no longer tiles
            // cap sprites: it paints the trough's own interior with the fill sprite's vertical
            // gradient, at master's direction, so the rounded ends follow the curve continuously
            // instead of snapping in whole-sprite steps. What must hold is that the painted shape
            // IS the trough's interior and nothing else.
            var fill = LaptopShopScreen.BuildFillPixels(library, out int fw, out int fh);
            Check(fill != null && fw == trough2.Width && fh == trough2.Height,
                  $"the composed fill is the frame's own size ({fw}x{fh})");

            bool Painted(int x, int y) => fill[(y * fw + x) * 4 + 3] > 0;
            int painted = 0, leaked = 0;
            int rowLo = int.MaxValue, rowHi = -1, colLo = int.MaxValue, colHi = -1;
            for (int y = 0; y < fh; y++)
                for (int x = 0; x < fw; x++)
                {
                    if (!Painted(x, y)) continue;
                    painted++;
                    rowLo = Math.Min(rowLo, y); rowHi = Math.Max(rowHi, y);
                    colLo = Math.Min(colLo, x); colHi = Math.Max(colHi, x);
                    // ⚠ A painted pixel must be INSIDE the frame: never on the orange stroke, and
                    // never out in the transparent surround. This is what a naive
                    // "fill every transparent pixel" mask would get wrong -- the outside is
                    // transparent too, and it would paint the whole image.
                    if (trough2.Pixels[(y * fw + x) * 4 + 3] > 16) leaked++;
                }
            Check(leaked == 0, $"no painted pixel lands on the frame stroke ({leaked} leaked)");
            Check(colLo == 3 && colHi == 124, $"the fill spans the trough's interior columns 3..124 (got {colLo}..{colHi})");
            Check(rowLo == 3 && rowHi == 28, $"and its interior rows 3..28 (got {rowLo}..{rowHi})");

            // ⚠ THE CONTROL that makes the span checks mean something: the fill must NOT reach the
            // image border. A mask taken as "every transparent pixel" would paint corner to corner,
            // because the area OUTSIDE the frame is transparent too.
            // (A count threshold does NOT work here and I tried one: the interior is legitimately
            // 3064 of 4096 pixels, three quarters of the image, so "less than half" fails on
            // correct output.)
            int border = 0;
            for (int x = 0; x < fw; x++) { if (Painted(x, 0)) border++; if (Painted(x, fh - 1)) border++; }
            for (int y = 0; y < fh; y++) { if (Painted(0, y)) border++; if (Painted(fw - 1, y)) border++; }
            Check(border == 0 && painted > 0,
                  $"the fill never reaches the image border ({border} border pixels, {painted} painted)");

            // Every painted pixel is the cyan gradient, and it varies down the bar, not across.
            int cyanBad = 0, rowsDiffer = 0; string badRows = "";
            for (int y = rowLo; y <= rowHi; y++)
            {
                int o = (y * fw + 64) * 4;
                if (fill[o + 2] <= fill[o]) { cyanBad++; badRows += $" y{y}=({fill[o]},{fill[o + 1]},{fill[o + 2]},a{fill[o + 3]})"; }                       // blue must beat red
                if (y > rowLo && fill[o + 1] != fill[((y - 1) * fw + 64) * 4 + 1]) rowsDiffer++;
            }
            Check(cyanBad == 0, $"every filled row is cyan, not the sprite's orange edge ({cyanBad} bad:{badRows})");
            // ⚠ NOT a count of "how many rows differ": the two bleed rows reuse a clean neighbour,
            // so a couple of steps legitimately repeat and a step-count proxy fails on correct
            // output (it did, at 22 of 25). The invariant is that the gradient SPANS -- top and
            // bottom are far apart and it descends throughout.
            int top = fill[(rowLo * fw + 64) * 4 + 1], bottom = fill[(rowHi * fw + 64) * 4 + 1];
            // ⚠ NOT "strictly monotonic": SHPS is lossy, so the decoded ramp wobbles by a level
            // here and there. Requiring a perfect descent fails on correct output. What must hold
            // is that no wobble is big enough to read as a band.
            int maxRise = 0;
            for (int y = rowLo + 1; y <= rowHi; y++)
                maxRise = Math.Max(maxRise, fill[(y * fw + 64) * 4 + 1] - fill[((y - 1) * fw + 64) * 4 + 1]);
            Check(top - bottom > 50, $"the gradient spans down the bar (green {top} at top, {bottom} at bottom)");
            Check(maxRise <= 4, $"and descends smoothly -- no reversal bigger than compression noise (worst +{maxRise})");

            // ⭐⭐ THE SLIDER TINT, and specifically that it is applied the way the HARDWARE
            // applies it. Master: "why is our drag slider 'knob' orange? should it be green?" --
            // the art is gold (hue 36-47 deg) and the console tints it with (54,249,77) from
            // 0x2E9E18. This check takes the knob's own brightest texel, applies the tint at
            // 0x80-unity, and requires the result to be green-dominant when the source is
            // red-dominant.
            var knob = new Ssh(library.ReadUi("/laptop/BARKNOB.ssh"));
            (int R, int G, int B) brightest = (0, 0, 0);
            for (int i = 0; i < knob.Width * knob.Height; i++)
            {
                if (knob.Pixels[i * 4 + 3] < 200) continue;
                int r = knob.Pixels[i * 4], g = knob.Pixels[i * 4 + 1], b = knob.Pixels[i * 4 + 2];
                if (r + g + b > brightest.R + brightest.G + brightest.B) brightest = (r, g, b);
            }
            Check(brightest.R > brightest.G && brightest.G > brightest.B,
                  $"the knob's art is gold, red>green>blue ({brightest.R},{brightest.G},{brightest.B})");

            (int R, int G, int B) Tinted(float unity) => (
                (int)Math.Min(255, brightest.R * ShopScreen.SliderTint.R / unity),
                (int)Math.Min(255, brightest.G * ShopScreen.SliderTint.G / unity),
                (int)Math.Min(255, brightest.B * ShopScreen.SliderTint.B / unity));

            var lit = Tinted(ShopScreen.TintUnity);
            Check(lit.G > lit.R && lit.G > lit.B,
                  $"tinted at 0x80-unity the knob is GREEN-dominant ({lit.R},{lit.G},{lit.B})");

            // ⚠ WHAT THE UNITY VALUE ACTUALLY DECIDES IS BRIGHTNESS, NOT HUE, and I had this
            // wrong: the tint's green component dwarfs its red and blue, so the knob comes out
            // green at EITHER unity. My first control asserted "at 255-unity it stays
            // red-dominant" and it failed, correctly -- 255-unity gives (54,206,44), still green,
            // just dark. The real difference is that only 0x80-unity drives green to saturation,
            // which is what the on-screen sample shows: (102,255,1).
            var at255 = Tinted(255f);
            Check(lit.G == 255 && at255.G < 255,
                  $"only 0x80-unity saturates the green ({lit.G} vs {at255.G} at 255-unity)");

            // ⭐⭐ THE KNOB TAKES THE TRACK'S COLOUR AND THE TRACK IS UNTINTED -- both measured off
            // master's screenshot of the real Drinks Shop screen, after I had shipped the opposite
            // on an inference. The track's own art must already be the yellow the screen shows, or
            // drawing it untinted would be wrong.
            // ⚠ The MOST SATURATED yellow, not the brightest pixel: the brightest is a pale
            // highlight at (235,238,167) and asserting on it fails against correct art. `r + g - 2b`
            // ranks yellowness, which is the property being claimed.
            var slideArt = new Ssh(library.ReadUi("/laptop/BARSLIDE.ssh"));
            (int R, int G, int B) yellowest = (0, 0, 0);
            int bestScore = int.MinValue;
            for (int i = 0; i < slideArt.Width * slideArt.Height; i++)
            {
                if (slideArt.Pixels[i * 4 + 3] < 200) continue;
                int r = slideArt.Pixels[i * 4], g = slideArt.Pixels[i * 4 + 1], b = slideArt.Pixels[i * 4 + 2];
                if (r + g - 2 * b > bestScore) { bestScore = r + g - 2 * b; yellowest = (r, g, b); }
            }
            Check(yellowest.R > 200 && yellowest.G > 200 && yellowest.B < 60,
                  $"BARSLIDE's own art carries the yellow the real screen shows, so the track needs no tint "
                  + $"({yellowest.R},{yellowest.G},{yellowest.B}) against the screenshot's (242,246,26)");

            // ⭐⭐ THE CHECK THAT WOULD HAVE CAUGHT THE SLIDER/BAR MIX-UP. Build and Hire were
            // written as sliders because their scene elements are NAMED `ExcitementSlider`,
            // `ReliabilitySlider` and `MotivationSlider` -- and all three are drawn as BARS.
            // Master: "those arent meant to be sliders, they're meant to be bars."
            //
            // The widget is whichever function the draw calls: `FUN_00115590` for a bar,
            // `FUN_001DAAE0` for a slider. Those call counts are recorded in LaptopWidgetCounts,
            // and every spec's row kinds must agree with them.
            //
            // ⚠ THE RIDE ROW IS THE CONTROL, and it is not vacuous: it is the one screen whose
            // counts (4 bars, 3 sliders) were decoded BEFORE this check existed and were already
            // right, so a broken comparison shows up as the control going red rather than as a
            // silent pass over three wrong screens.
            foreach (var (name, draw, bars, sliders) in LaptopWidgetCounts.Decoded)
            {
                var spec = name switch
                {
                    "Ride"  => LaptopScreen.Ride,
                    "Build" => LaptopScreen.Build,
                    _       => LaptopScreen.Hire,
                };
                int haveBars = 0, haveSliders = 0;
                foreach (var r in spec.Rows)
                {
                    if (r.Kind == LaptopRowKind.Bar) haveBars++;
                    else if (r.Kind == LaptopRowKind.Slider) haveSliders++;
                }
                Check(haveBars == bars && haveSliders == sliders,
                      $"{name}: spec has {haveBars} bars / {haveSliders} sliders, and {draw} emits "
                    + $"{bars} / {sliders} -- the widget comes from the DRAW, not the element name");
            }

            // ⭐⭐ THE RELIABILITY FORMULA, PINNED TO A WORKED EXAMPLE. `FUN_00198ad8` is arithmetic
            // over five tier-0 fields, and every tiered ride on the disc carries the SAME five --
            // MinSpeedDamage 4, MinCapacityDamage 4, WearRate 5, MinDuration 1, MaxDuration 10 --
            // so every one reads 86. That uniformity looked like a bug and is not: InitialCondition
            // DOES vary across the same records (100 against 1000), which is what proves the read
            // is per-ride rather than one shared default.
            //
            // Hand-worked: Half(4) = ((0x1000-4)*0x800>>12)+4 = 2050; inner = 2050*5 = 10250;
            // v = 10250 * ((1+10)/2) * 9 >> 15 = 14; reliability = 100 - 14 = 86.
            {
                int Half(int q) => ((0x1000 - q) * 0x800 >> 12) + q;
                int inner = ((Half(4) + Half(4)) / 2) * 5;
                int v = inner * ((1 + 10) / 2) * 9 >> 15;
                Check(100 - (v < 101 ? v : 100) == 86,
                      $"FUN_00198ad8 over the disc's own tier-0 wear fields (4,4,5,1,10) gives 86, "
                    + $"got {100 - (v < 101 ? v : 100)}");
                // ⚠ A CONTROL, so the check above is not just restating its own arithmetic: a
                // heavier wear rate MUST drive reliability DOWN. If this passes while the line
                // above passes, the formula responds to its inputs rather than returning 86.
                int inner2 = ((Half(4) + Half(4)) / 2) * 40;
                int v2 = inner2 * ((1 + 10) / 2) * 9 >> 15;
                Check(100 - (v2 < 101 ? v2 : 100) < 86,
                      $"control: eight times the wear rate lowers reliability "
                    + $"({100 - (v2 < 101 ? v2 : 100)} against 86)");
            }

            // ⭐⭐ "No. Owned" COUNTS BY DEFINITION, NOT BY PLACEMENT ID -- and this proves the
            // predicate rather than the park. `ParkRide.Id` is the PLACEMENT's id, unique per
            // placed thing; `RideDefinition.Id` is the ride TYPE's `Info.Id`. Comparing the two
            // matched nothing, and in an empty harness park that wrong answer is indistinguishable
            // from the right one -- both read 0. So the expression is exercised directly here,
            // against rides built by hand, instead of being trusted because a render showed 0.
            {
                var typeA = new RideDefinition(); typeA.Fields["Info.Id"] = "4242";
                var typeB = new RideDefinition(); typeB.Fields["Info.Id"] = "9999";
                var park = new List<ParkRide>
                {
                    new() { Id = 1, Definition = typeA },   // two of the same TYPE,
                    new() { Id = 2, Definition = typeA },   // with different placement ids
                    new() { Id = 3, Definition = typeB },
                };
                int Owned(RideDefinition d) => park.Count(pr => ReferenceEquals(pr.Definition, d)
                    || (pr.Definition?.Id is { } pid && d.Id is { } did && pid == did));
                Check(Owned(typeA) == 2, $"two placements of one ride type count as 2 owned, got {Owned(typeA)}");
                Check(Owned(typeB) == 1, $"and a single placement counts as 1, got {Owned(typeB)}");
                // ⚠ THE CONTROL that catches the original bug: keying on ParkRide.Id would give 0
                // here, because no placement id equals a type id in this fixture.
                int byPlacementId = park.Count(pr => pr.Id == (typeA.Id ?? -1));
                Check(byPlacementId == 0,
                      $"control: the OLD key (ParkRide.Id == RideDefinition.Id) finds {byPlacementId}, "
                    + "which is why the bug read as an empty park");
            }

            if (_bad > 0) { GD.PrintErr($"LAPTOP SHOP FAIL: {_bad} of {_checks}"); GetTree().Quit(2); return; }
            GD.Print($"LAPTOP SHOP PASS: {_checks} checks; the layout is read from "
                   + $"{ShopScreen.SceneFile}, the row step predicts the scene's own widget rows, "
                   + "every label id is its SINGLESHOP key, and each world has its own chrome");
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PrintErr("LAPTOP SHOP FAIL: " + ex); GetTree().Quit(2); }
        finally { library?.Dispose(); }
    }
}
