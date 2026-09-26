using System;
using System.Collections.Generic;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>⭐ THE TESTING HUD. Master: "add an fps counter to the top right. and add a bunch of
/// park debug options / 'cheats' to make testing easier. ie spawn buttons for guests, seaplane,
/// ferry, bus, give money etc."
///
/// ⚠⚠ NOTHING HERE IS THE CONSOLE'S. Every other panel in this port is traced to something in the
/// executable; this one is openly ours, so it is kept in its own file and behind F4 rather than
/// mixed into the park UI where a later reader might take it for game behaviour.
///
/// ⭐ The buttons drive the REAL systems -- the bus's own admission, the vehicles' own scripts,
/// ParkFinances.Credit -- rather than shortcutting past them. A cheat that spawns guests by a
/// different route than the bus does would test a path the game never runs, which is the opposite
/// of what a testing aid is for.</summary>
public partial class Viewer
{
    Label _fps;
    PanelContainer _cheatPanel;
    Label _cheatStatus;
    double _fpsNext;
    bool _cheatsAtStart;

    /// <summary>Top right, and the ONE thing here that is always on screen.</summary>
    void BuildDebugHud(Control ui)
    {
        _fps = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, Text = "-- fps" };
        _fps.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _fps.GrowHorizontal = Control.GrowDirection.Begin;
        _fps.OffsetLeft = -110; _fps.OffsetTop = 6; _fps.OffsetRight = -8;
        _fps.HorizontalAlignment = HorizontalAlignment.Right;
        _fps.AddThemeColorOverride("font_color", new Color(0.72f, 0.95f, 0.72f));
        ui.AddChild(_fps);

        // ⚠ PanelContainer sizes to its child, but anchoring its right edge and letting it grow
        // left does not widen the BACKGROUND to match -- the first render had "Unlimited" and
        // "Clear guests" hanging off the end of the panel into the sky. A minimum width and real
        // padding, rather than trusting the grow direction to work it out.
        var pad = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            pad.AddThemeConstantOverride($"margin_{side}", 8);
        var col = new VBoxContainer();
        col.AddChild(new Label { Text = "DEBUG  (F4)" });
        void Row(string label, params (string Text, Action Go)[] buttons)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(64, 0) });
            foreach (var (text, go) in buttons)
            {
                var b = new Button { Text = text };
                b.Pressed += () => { try { go(); } catch (Exception ex) { CheatSay(ex.Message); } };
                row.AddChild(b);
            }
            col.AddChild(row);
        }
        Row("Guests", ("+1", () => CheatGuests(1)), ("+10", () => CheatGuests(10)),
                      ("+50", () => CheatGuests(50)));
        Row("Arrivals", ("Bus", CheatBus), ("Seaplane", () => CheatVehicle("seaplane")),
                        ("Ferry", () => CheatVehicle("ferry")));
        Row("Money", ("+£10k", () => CheatMoney(10_000)), ("+£100k", () => CheatMoney(100_000)),
                     ("Unlimited", CheatUnlimited));
        Row("Park", ("Open/Shut", CheatToggleOpen), ("Clear guests", CheatClearGuests));
        Row("Weather", ("Rain", () => CheatWeather(true)), ("Clear", () => CheatWeather(false)));
        _cheatStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart,
                                   CustomMinimumSize = new Vector2(0, 0) };
        _cheatStatus.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.55f));
        col.AddChild(_cheatStatus);

        pad.AddChild(col);
        _cheatPanel = new PanelContainer { Visible = false,
                                           CustomMinimumSize = new Vector2(420, 0) };
        _cheatPanel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _cheatPanel.GrowHorizontal = Control.GrowDirection.Begin;
        _cheatPanel.OffsetTop = 30; _cheatPanel.OffsetRight = -8;
        _cheatPanel.AddChild(pad);
        ui.AddChild(_cheatPanel);
    }

    void TickDebugHud(double delta)
    {
        if (_fps == null) return;
        // ⚠ Four times a second, not every frame: a label that rewrites itself 60 times a second
        // is unreadable, and the number it shows is an average anyway.
        _fpsNext -= delta;
        if (_fpsNext > 0) return;
        _fpsNext = 0.25;
        double fps = Engine.GetFramesPerSecond();
        _fps.Text = $"{fps:F0} fps";
        _fps.Modulate = fps >= 50 ? Colors.White : fps >= 30 ? new Color(1f, 0.85f, 0.4f)
                                                             : new Color(1f, 0.5f, 0.5f);
    }

    void ToggleCheats()
    {
        if (_cheatPanel == null) return;
        _cheatPanel.Visible = !_cheatPanel.Visible;
        if (_cheatPanel.Visible) CheatSay("debug panel -- none of this is the console's");
    }

    /// <summary>⚠ The cheat panel sits top-RIGHT, and the world click guard only ever knew about
    /// the left panel's width. Without this a press on "+10 guests" would spawn the guests AND
    /// place a ride under the button.</summary>
    bool PointerOverCheats(Vector2 mouse) =>
        _cheatPanel is { Visible: true } p && p.GetGlobalRect().HasPoint(mouse);

    void CheatSay(string text)
    {
        if (_cheatStatus != null) _cheatStatus.Text = text;
        GD.Print($"[cheat] {text}");
    }

    /// <summary>Where a guest appears: the bus's own drop-off if the catalogue loaded, else the
    /// gate. ⚠ Not "any walkable cell" -- a guest dropped somewhere the bus never uses would walk
    /// a route the game never asks for.</summary>
    ParkCell? ArrivalPoint()
    {
        if (_busCatalogue is { } cat && _guests?.Paths?.Open(cat.Point0) == true) return cat.Point0;
        if (_gateCell is { } g)
        {
            var c = new ParkCell(g.X, g.Y);
            if (_guests?.Paths?.Open(c) == true) return c;
        }
        return null;
    }

    void CheatGuests(int n)
    {
        if (_visitors == null || _guests == null) { CheatSay("no park running"); return; }
        if (ArrivalPoint() is not { } at) { CheatSay("no open arrival point -- lay a path from the gate"); return; }
        int made = 0;
        for (int i = 0; i < n; i++)
        {
            // ⭐ The same two lines the bus's own admission runs, so a cheated guest is
            // indistinguishable from an arriving one -- including its entrance-flow record.
            uint? serial = _entranceFlow == null ? null : ActivationSequence().Activate("guest");
            var guest = _visitors.Arrive(at, at);
            _entranceFlow?.Add(guest, checked((sbyte)(15 + _guestRng.Next(15))), serial);
            made++;
        }
        CheatSay($"{made} guest{(made == 1 ? "" : "s")} at {at}");
    }

    void CheatBus() { AdmitBusBatch(0); CheatSay($"bus batch {_busBatches}, {_busAdmitted} admitted so far"); }

    /// <summary>Skip whatever the seaplane or ferry is currently waiting out, including the first
    /// visit's start delay. ⚠ It does NOT teleport them: the script still plays the arrival, so
    /// what you see is the real sequence, just without the timetable.</summary>
    void CheatVehicle(string stem)
    {
        var v = _vehicles.Find(x => x.Stem == stem);
        if (v == null) { CheatSay($"no {stem} in this park"); return; }
        long now = (long)_busElapsedMs;
        if (now < v.StartMs) { v.StartMs = now; CheatSay($"{stem}: first visit now"); return; }
        v.HeldSince = 0;
        CheatSay($"{stem}: skipping its wait at status {v.LastStatus}");
    }

    /// <summary>The same setter V cycles through, so the debug button and the key cannot drift.</summary>
    void CheatWeather(bool rain)
    {
        var k = rain ? Weather.Kind.Rain : Weather.Kind.None;
        CheatSay($"weather {k}: {_weather.Set(_lib, k, _cam.GlobalPosition)}");
    }

    void CheatMoney(int pounds)
    {
        if (_sim?.Finances == null) { CheatSay("no park running"); return; }
        _sim.Finances.Credit(PathPrices.Tenths(pounds));
        ShowMoney();
        CheatSay($"+{Money.Format(PathPrices.Tenths(pounds))} -> {Money.Format(_sim.Finances.Balance)}");
    }

    /// <summary>⚠ This flips the LAPTOP's idea of whether the park is open, which is what gates
    /// the menu rows. There is no separate simulation switch to flip yet, so it is exactly as
    /// honest as the thing it toggles.</summary>
    void CheatToggleOpen()
    {
        _laptopParkOpen = !_laptopParkOpen;
        CheatSay($"park {(_laptopParkOpen ? "OPEN" : "shut")}");
    }

    void CheatClearGuests()
    {
        int n = _guests?.Guests.Count ?? 0;
        _guests?.Clear();
        CheatSay($"cleared {n} guest{(n == 1 ? "" : "s")}");
    }

    void CheatUnlimited()
    {
        if (_sim?.Finances == null) { CheatSay("no park running"); return; }
        _sim.Finances.Unlimited = !_sim.Finances.Unlimited;
        ShowMoney();
        CheatSay($"unlimited money {(_sim.Finances.Unlimited ? "ON" : "off")}");
    }
}
