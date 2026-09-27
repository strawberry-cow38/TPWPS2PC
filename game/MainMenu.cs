using System;
using System.Collections.Generic;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>⭐⭐ THE FRONT END -- the game's main menu. Research: `findings/main-menu.md`.
///
/// ⭐ EVERY COORDINATE IS THE CONSOLE'S, read out of the page draws (`0x16D8A8` for the main page,
/// `0x16DEA8` for the New Game sub-menu) rather than chosen:
///
///   x     = **0x100** (256) -- the centre of the 512 UI square, passed straight to the text call
///   y     = **250** (`DAT_002B9740`, set into the page's `+0x20` at Enter)
///   pitch = **0x20** (32), the page's `+0x1c`
///   the selected row is **yellow (255,255,0)**, every other row **white**
///
/// ⚠ The background is `Mainback2` -- `Mainback1` belongs to the LEGAL screen, which is the one
/// that says "Press START". Both ship `_US` and `_JAP` variants; this takes the European one to
/// match the rest of the port.
///
/// ⚠ MOVIES ARE SKIPPED, on master's instruction ("skip movies with placeholder text"). The
/// sequencing is decoded -- pair table at `0x35ED50`, pair index = the lobby's world index -- but
/// nothing here plays an `.MPC`; the entries that would start one say so on screen instead.</summary>
public partial class MainMenu : Control
{
    public const float Native = 512f;

    /// <summary>`DAT_002B9740`, written into the page's y-origin by every page's Enter.</summary>
    const int YOrigin = 250;
    /// <summary>The page's `+0x1c`.</summary>
    const int RowPitch = 0x20;
    /// <summary>The x every row is drawn at -- the centre of the 512 square.</summary>
    const int RowX = 0x100;

    static readonly Color Selected = new(1f, 1f, 0f);      // 0xff,0xff,0
    static readonly Color Plain = Colors.White;

    /// <summary>A page is a list of rows; a row is a text id and what choosing it does.</summary>
    public enum Action { NewGamePage, LoadGame, Options, MainGame, TestPark, Exit, Back }

    readonly record struct Row(int TextId, Action Does);

    // ⭐ The label table at 0x2b9748, three zero-terminated groups. Group 2 (Main Game / Exit) is
    // the one used when FUN_00154378() is zero -- the test park not unlocked.
    static readonly Row[] Page0 =
    {
        new(922, Action.NewGamePage),   // New Game
        new(203, Action.LoadGame),      // Load Game
        new(723, Action.Options),       // Options
    };
    static readonly Row[] Page3Unlocked =
    {
        new(766, Action.MainGame),      // Main Game
        new(169, Action.TestPark),      // Rollercoaster Test Park
        new(107, Action.Exit),          // Exit
    };
    static readonly Row[] Page3Locked =
    {
        new(766, Action.MainGame),      // Main Game
        new(107, Action.Exit),          // Exit
    };

    FontText _font;
    TextDatabase _text;
    ImageTexture _back;
    Row[] _rows = Page0;
    int _sel;
    string _note = "";

    /// <summary>Raised with what the player chose.</summary>
    public event Action<Action> Chosen;

    public bool Open { get; private set; }

    public MainMenu() { MouseFilter = MouseFilterEnum.Ignore; Visible = false; }

    /// <summary>⚠ `Mainback2` is the front end's; `Mainback1` is the legal screen's.</summary>
    public static MainMenu Create(AssetLibrary lib, FontText font, TextDatabase text)
    {
        if (lib == null || font == null) return null;
        var m = new MainMenu { _font = font, _text = text };
        var raw = lib.ReadSide("FRONTEND.WAD", "/Mainmenu/Mainback2.ssh");
        if (raw != null)
        {
            try
            {
                var ssh = new Ssh(raw);
                var img = Image.CreateFromData(ssh.Width, ssh.Height, false, Image.Format.Rgba8, ssh.Pixels);
                m._back = ImageTexture.CreateFromImage(img);
            }
            catch (Exception e) { GD.PrintErr($"[menu] Mainback2: {e.Message}"); }
        }
        else GD.PrintErr("[menu] FRONTEND.WAD/Mainmenu/Mainback2.ssh missing -- menu draws on black");
        return m;
    }

    public void Open_() { Open = true; Visible = true; _rows = Page0; _sel = 0; _note = ""; QueueRedraw(); }
    public new void Hide() { Open = false; Visible = false; QueueRedraw(); }

    string Label(int id) => _text != null && id >= 0 && id < _text.Keys.Length
                          ? _text.Text("eng", id) ?? $"#{id}" : $"#{id}";

    /// <summary>⚠ Up/Down CLAMP rather than wrap -- the page keeps a row range 0..2 and the
    /// console's own move clamps inside it.</summary>
    public void Move(int by)
    {
        if (!Open || _rows.Length == 0) return;
        _sel = Mathf.Clamp(_sel + by, 0, _rows.Length - 1);
        QueueRedraw();
    }

    public void Confirm()
    {
        if (!Open || _sel < 0 || _sel >= _rows.Length) return;
        var a = _rows[_sel].Does;
        switch (a)
        {
            case Action.NewGamePage:
                // ⚠ Which group depends on FUN_00154378() -- what unlocks the test park is not
                // decoded, so the port shows the UNLOCKED group and says so rather than hiding a
                // row on a rule it does not have.
                _rows = Page3Unlocked; _sel = 0; _note = ""; QueueRedraw();
                return;
            case Action.Back:
                _rows = Page0; _sel = 0; QueueRedraw();
                return;
            // ⚠ PLACEHOLDERS, on master's instruction. These are the entries whose real
            // behaviour starts a movie or needs save/load, neither of which the port has.
            case Action.LoadGame: _note = "Load Game -- no save/load in this port yet"; QueueRedraw(); return;
            case Action.Options:  _note = "Options -- not built yet"; QueueRedraw(); return;
            case Action.TestPark: _note = "Rollercoaster Test Park -- practice mode not built yet"; QueueRedraw(); return;
        }
        Chosen?.Invoke(a);
    }

    /// <summary>⚠ Cancel goes back a page, and does nothing on the top page -- the console's own
    /// pages only ever switch to 0, 1 and 3.</summary>
    public void Cancel()
    {
        if (!Open) return;
        if (_rows != Page0) { _rows = Page0; _sel = 0; _note = ""; QueueRedraw(); }
    }

    public override void _Draw()
    {
        if (!Open || _font == null) return;
        var view = GetViewportRect().Size;
        float s = Mathf.Max(0.05f, Mathf.Min(view.X, view.Y) / Native);
        var origin = (view - new Vector2(Native, Native) * s) / 2f;

        // ⭐ The background fills the UI square, like every other console screen.
        if (_back != null)
            DrawTextureRect(_back, new Rect2(origin, new Vector2(Native, Native) * s), false);
        else
            DrawRect(new Rect2(origin, new Vector2(Native, Native) * s), Colors.Black);

        // ⭐ x = 0x100 CENTRED, y from 250, stepping 0x20 -- and the selected row yellow.
        for (int i = 0; i < _rows.Length; i++)
        {
            var tex = _font.Render(Label(_rows[i].TextId));
            var size = tex.GetSize() * s;
            var at = origin + new Vector2(RowX * s - size.X / 2f, (YOrigin + RowPitch * i) * s);
            DrawTextureRect(tex, new Rect2(at, size), false, i == _sel ? Selected : Plain);
        }

        if (_note.Length == 0) return;
        var n = _font.Render(_note);
        var ns = n.GetSize() * s;
        DrawTextureRect(n, new Rect2(
            origin + new Vector2(RowX * s - ns.X / 2f, (YOrigin + RowPitch * (_rows.Length + 1)) * s),
            ns), false, Plain);
    }
}
