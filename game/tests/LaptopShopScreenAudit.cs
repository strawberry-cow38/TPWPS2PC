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
            foreach (var art in new[] { "/laptop/PROG_BAR.ssh", "/laptop/PROG_CBIT.ssh", "/laptop/PROG_VBIT.ssh",
                                        "/laptop/BARSLIDE.ssh", "/laptop/BARKNOB.ssh" })
            {
                bool ok = false;
                try { var b = library.ReadUi(art); ok = b != null && new Ssh(b).Width > 0; } catch { }
                Check(ok, $"{art} is present and decodes");
            }

            // ⭐ The face, corroborated by the step rather than chosen: Large.bff's line advance
            // must FIT the row step, and the other two faces must be the ones that leave holes.
            var advances = new[] { "Large", "Small", "Console" }
                .ToDictionary(n => n, n => new BitmapFont(library.ReadGeneric($"/Fonts/European/{n}.bff")).LineAdvance);
            Check(advances["Large"] <= ShopScreen.RowStep && ShopScreen.RowStep - advances["Large"] <= 4,
                  $"Large.bff's line advance {advances["Large"]} fits the {ShopScreen.RowStep} row step");
            Check(ShopScreen.RowStep - advances["Small"] > 4 && ShopScreen.RowStep - advances["Console"] > 4,
                  $"control: Small ({advances["Small"]}) and Console ({advances["Console"]}) do not");

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
