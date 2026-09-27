using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TPW.PS2.Data;
// ⚠ Godot has an `Animation` of its own; alias ours, exactly as the other Viewer files do.
using Aps = TPW.PS2.Data.Animation;
using Model = TPW.PS2.Data.Model;

namespace TPWPS2Viewer;

/// <summary>⭐⭐ THE LOBBY -- the game's `WorldMapSelector`, the "world map" you pick a park from.
///
/// The research behind every number here is in `findings/lobby-menu.md`. The two things that
/// shape this file:
///
/// ⭐⭐ **It is a 3D scene, not a menu.** All 29 `.sce` layouts in `MENUS.WAD` are `main_*` laptop
/// screens; there is no lobby layout anywhere. `LOBBY.WAD` is `base.mps` -- an island with a
/// `heightfield`, like a park -- and one diorama per park standing on it.
///
/// ⭐⭐ **Navigation is an authored table, not geometry.** `FUN_00217e20` takes the slot to move to
/// out of the record's own neighbour bytes. Nearest-model-in-that-direction is a different
/// machine and would disagree with the console wherever the table is asymmetric, which it is.
/// </summary>
public partial class Viewer
{
    bool _lobbyMode;
    LobbySlots _lobbySlots;
    int _lobbyRecord;                       // the RECORD index, 0..7 -- not the model index
    Node3D _lobbyRoot;
    Model _lobbyBaseMesh;
    readonly List<AnimatedModel> _lobbyParks = new();
    float _lobbyModelTime;
    bool _lobbyAimed;

    /// <summary>What the lobby root undoes: `base.mps` is authored ten times the size its own
    /// root scale draws it at. ⚠ Every world position taken out of the scene is therefore in
    /// PRE-scale units and must go through the root, which is why the camera reads
    /// `GlobalPosition` rather than the model's local one.</summary>
    const float AuthoredScale = 10f;

    /// <summary>⭐ `base` first (load id 1), then the eight parks. ⚠ The archive is opened as the
    /// CURRENT wad so the existing indexer enumerates its `.mps`/`.aps` pairs for us -- the same
    /// move `LoadMap` makes when it switches world.</summary>
    void EnterLobby()
    {
        var wad = _lib.WadFiles().FirstOrDefault(
            f => f.Path.EndsWith("LOBBY.WAD", StringComparison.OrdinalIgnoreCase));
        if (wad == null) { GD.PrintErr("[lobby] no LOBBY.WAD on the disc"); return; }
        _lib.OpenWad(wad.Path);
        _texCache.Clear();
        IndexRides();

        try { _lobbySlots = LobbySlots.Read(_lib.Disc); }
        catch (Exception e) { GD.PrintErr($"[lobby] no slot table: {e.Message}"); return; }

        _lobbyRoot?.QueueFree();
        _lobbyRoot = new Node3D();
        // ⭐⭐ THE LOBBY IS DRAWN AT ITS AUTHORED SCALE, NOT A TENTH OF IT.
        //
        // `base.mps` carries a 0.1 on its root, so composing the hierarchy puts the island at
        // x 0..85, z 0..113 -- and every seat matrix comes out with a basis scale of exactly
        // 0.100, which is the same 0.1 seen from the other end. The raw authored mesh positions
        // are ten times those numbers (a bridge at z 1012, another at x 851).
        //
        // ⚠ IT IS NOT A COSMETIC CHOICE. `GameCamera.MinBehind` is 384 WORLD UNITS: against a
        // 113-unit island the console camera cannot be brought closer than three times its width,
        // so the whole lobby rendered as a dot in open water and no `Behind` could fix it -- the
        // clamp silently swallowed every value I passed. Undoing the 0.1 here puts the scene in
        // the same units the camera was built for.
        _lobbyRoot.Scale = Vector3.One * AuthoredScale;
        AddChild(_lobbyRoot);
        _lobbyParks.Clear();

        AssetLibrary.RideAssets Find(string stem) => _lib.Rides.FirstOrDefault(
            r => string.Equals(Leaf(r.Name), stem + ".mps", StringComparison.OrdinalIgnoreCase)
              || string.Equals(Leaf(r.Name), stem, StringComparison.OrdinalIgnoreCase));

        // ⚠ `/Backup/base.mps` is a second copy of the room. Take the one at the archive ROOT --
        // the loader asks for "base" under `data\lobby\`, not under a subdirectory.
        var baseAssets = _lib.Rides.FirstOrDefault(
            r => Leaf(r.Name).Equals("base.mps", StringComparison.OrdinalIgnoreCase)
              && !r.Name.Contains("Backup", StringComparison.OrdinalIgnoreCase)) ?? Find("base");
        if (baseAssets == null) { GD.PrintErr("[lobby] LOBBY.WAD has no base.mps"); return; }

        var room = LoadPlaceable(baseAssets, out _, out _lobbyBaseMesh);
        if (room?.Root == null) { GD.PrintErr("[lobby] base.mps would not load"); return; }
        room.SetFrame(0);
        _lobbyRoot.AddChild(room.Root);

        // ⭐⭐ EACH PARK SITS ON A FITTING OF `base`, found by ID, under mask 0x400.
        // `FUN_00216f90` searches with its own loop index plus one -- so the id is the MODEL's
        // number, which is why nothing here is indexed by record.
        var fits = _lobbyBaseMesh.Fittings;
        var world = _lobbyBaseMesh.WorldTransforms();
        int seated = 0;
        for (int model = 0; model < LobbySlots.ModelNames.Length; model++)
        {
            var assets = Find(LobbySlots.ModelNames[model]);
            if (assets == null) { GD.PrintErr($"[lobby] no {LobbySlots.ModelNames[model]}.mps"); continue; }
            var drawn = LoadPlaceable(assets, out var anim, out _);
            if (drawn?.Root == null) continue;

            // ⭐ APS section 5 at rate 1.0 -- `(vt+0x5c)(1.0f, obj, 5, 0, 0, 1)`, the SAME call
            // the laptop's model window makes. Two unrelated sites asking for the same thing.
            var rec = anim?.Records().FirstOrDefault(r => r.Slot == 5 && r.Skeletal)
                   ?? anim?.Records().FirstOrDefault(r => r.Slot == 5);
            if (rec != null) { drawn.UseRecord(rec); drawn.SetFrame(0); }
            else drawn.SetFrame(Math.Max(0, drawn.Frames - 1));

            int wantId = LobbySlots.SeatFittingId(model);
            var fit = fits.FirstOrDefault(f => f.Id == wantId && (f.Flags & LobbySlots.SeatMask) != 0);
            var seat = Transform3D.Identity;
            // ⚠ TWO DIFFERENT FAILURES, SAID APART. The first version reported both as "no
            // fitting", so a node whose transform would not resolve read as a missing fitting --
            // and base.mps HAS all eight. The message sent me looking in the wrong table.
            if (fit.Id != wantId)
                GD.PrintErr($"[lobby] {LobbySlots.ModelNames[model]}: base has no 0x400 fitting with id {wantId}");
            else if (!world.TryGetValue(_lobbyBaseMesh.NodeOffset(fit.Node), out var m))
                GD.PrintErr($"[lobby] {LobbySlots.ModelNames[model]}: fitting id {wantId} is node "
                          + $"{fit.Node}, which has no world transform");
            else
            {
                seat = ToGodot(m); seated++;
                GD.Print($"[lobby] seat {LobbySlots.ModelNames[model],-9} fitting id {wantId} "
                       + $"node {fit.Node} at ({seat.Origin.X:F1}, {seat.Origin.Y:F1}, {seat.Origin.Z:F1})");
            }

            // ⭐⭐ TAKE THE SEAT'S PLACE, NOT ITS SIZE. Every one of these fitting nodes carries a
            // basis scale of exactly 0.100 -- they are markers, drawn down small. Multiplying the
            // park model by it as well made each diorama 0.07 of its authored size: on the
            // overview render the eight parks came out as coloured specks on a 1130-unit island,
            // which is how this was caught. Orthonormalising keeps the seat's position and
            // FACING -- both of which are the console's -- and drops only the marker's own scale.
            //
            // ⚠ Scale PRE-multiplied into the basis, never assigned through Rotation: these
            // models carry a mirrored basis and Godot's euler round trip does not survive one.
            var placed = new Transform3D(seat.Basis.Orthonormalized(), seat.Origin);
            drawn.Root.Transform = placed.ScaledLocal(Vector3.One * LobbySlots.ModelScale);
            _lobbyRoot.AddChild(drawn.Root);
            _lobbyParks.Add(drawn);
        }

        _lobbyMode = true;
        _lobbyRecord = 0;
        _lobbyAimed = false;
        GD.Print($"[lobby] base + {_lobbyParks.Count} parks, {seated} seated on base fittings; "
               + $"{_lobbySlots.All.Count} slot records ({_lobbySlots.Parks.Count} parks)");
        LobbyReport();
        LobbyAimCamera();
    }

    static Transform3D ToGodot(System.Numerics.Matrix4x4 m) => new(
        new Vector3(m.M11, m.M12, m.M13), new Vector3(m.M21, m.M22, m.M23),
        new Vector3(m.M31, m.M32, m.M33), new Vector3(m.M41, m.M42, m.M43));

    /// <summary>The selected park's own name, from the record's `STR_MAP_*` text id.</summary>
    string LobbyName(int record)
    {
        if (_lobbySlots == null || record < 0 || record >= _lobbySlots.All.Count) return "";
        int id = _lobbySlots.All[record].NameTextId;
        // ⚠ The record carries a text ROW INDEX, so it is read positionally -- the same way
        // every other id in this port is. `STR_MAP_LOSTKINGDOM_1` and friends live there.
        if (_text == null || id < 0 || id >= _text.Keys.Length) return $"#{id}";
        return _text.Text("eng", id) ?? $"#{id}";
    }

    void LobbyReport()
    {
        if (_lobbySlots == null) return;
        var s = _lobbySlots.All[_lobbyRecord];
        int mi = _lobbySlots.ModelIndex(_lobbyRecord);
        string name = LobbyName(_lobbyRecord).Replace("\n", " / ");
        GD.Print($"[lobby] on record {_lobbyRecord} (world {s.World} park {s.ParkInWorld}) "
               + $"= model {mi} {(mi >= 0 ? LobbySlots.ModelNames[mi] : "?")}: {name}");
        Status($"{name} -- arrows to move, or Esc");
    }

    /// <summary>⭐ Move the way the console moves: the record's own neighbour byte for that
    /// direction. ⚠ A dead end (the 20 sentinel) moves NOTHING -- it does not wrap and it does
    /// not fall back to a nearest-in-direction search.</summary>
    void LobbyMove(int dir)
    {
        if (_lobbySlots == null) return;
        if (_lobbySlots.All[_lobbyRecord].Neighbour(dir) is not { } next)
        { Status($"{LobbyName(_lobbyRecord).Replace("\n", " / ")} -- nothing that way"); return; }
        _lobbyRecord = next;
        LobbyReport();
        LobbyAimCamera();
    }

    /// <summary>Put the game camera over the selected park. ⚠ The park models sit on `base`'s
    /// fittings, so the camera follows the SEATED model's world position rather than anything
    /// computed from the record.</summary>
    void LobbyAimCamera()
    {
        int mi = _lobbySlots?.ModelIndex(_lobbyRecord) ?? -1;
        if (_game == null || mi < 0 || mi >= _lobbyParks.Count) return;
        var at = _lobbyParks[mi].Root.GlobalPosition;
        _freeCam = false;
        // ⚠ The lobby sets no plot, so the camera would keep the last park's border. Widen it to
        // the scene itself or the clamp drags every aim back inside a rectangle that is not here.
        _game.MinTileX = float.NegativeInfinity; _game.MaxTileX = float.PositiveInfinity;
        _game.MinTileZ = float.NegativeInfinity; _game.MaxTileZ = float.PositiveInfinity;
        // ⚠ AND SET THE DISTANCE HERE. `StartGameCam` runs after the mode dispatch and calls
        // `_game.Reset()`, which puts `Behind` back to its park default -- so a `--cam=` passed on
        // the command line was being discarded before the lobby ever drew. Explicit, overridable.
        // ⭐ DISTANCE FROM THE SCENE'S OWN SIZE, not a number picked by eye. `base` spans about
        // 85 x 113 world units and a seated park is a fraction of that, so the park default (700,
        // tuned for a 128-cell park) put the camera five islands away and everything on screen was
        // a dot. Two thirds of the island's span frames a park with its neighbours around it.
        _game.Behind = Mathf.Clamp(
            int.TryParse(System.Environment.GetEnvironmentVariable("TPW_LOBBY_BEHIND"), out int b)
                ? b : (int)(LobbySpan() * 0.66f),
            GameCamera.MinBehind, GameCamera.MaxBehind);
        // ⚠ A CONTROL, NOT A FEATURE. Aiming at one park cannot show whether the other seven are
        // seated right -- and jungle1 happens to sit on the island's edge, so its view is mostly
        // sea and looks like a mistake either way. `TPW_LOBBY_OVERVIEW=1` centres on all eight so
        // one render answers the question the per-park view cannot.
        if (_lobbyOverview)
        {
            // ⚠⚠ THE FREE CAMERA, NOT THE CONSOLE ONE. On the console camera the zoom IS the
            // pitch: 679 behind sits inside one park, 1029 and 3132 both look flat across the sea
            // and the island never enters frame. There is no `Behind` that frames a 1029-unit
            // island from above, because that camera was never meant to. The overview is a
            // DEBUG view, so it uses the orbit camera and aims exactly.
            var c = LobbyCentre();
            float span = LobbySpan();
            _freeCam = true;
            _focus = c;
            _dist = span * 1.1f;
            _pitch = -0.95f;              // most of the way down, so the whole island is in frame
            _yaw = 0f;
            GD.Print($"[lobby] OVERVIEW at ({c.X:F0}, {c.Z:F0}), free cam {_dist:F0} out, span {span:F0}");
            return;
        }
        _game.PlaceAt(at.X, at.Z);
        GD.Print($"[lobby] camera on {LobbySlots.ModelNames[mi]} at ({at.X:F1}, {at.Y:F1}, {at.Z:F1}), "
               + $"{_game.Behind} behind");
    }

    /// <summary>The middle of the seated parks, in world units.</summary>
    Vector3 LobbyCentre()
    {
        var sum = Vector3.Zero; int n = 0;
        foreach (var m in _lobbyParks)
        {
            if (m?.Root == null || !IsInstanceValid(m.Root)) continue;
            sum += m.Root.GlobalPosition; n++;
        }
        return n == 0 ? Vector3.Zero : sum / n;
    }

    /// <summary>The widest span of the seated parks, in world units -- what the camera frames
    /// against. ⚠ Measured off the SEATS rather than the meshes: the island's own bounds include
    /// bridges that run well past the parks.</summary>
    float LobbySpan()
    {
        if (_lobbyParks.Count == 0) return 100f;
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        foreach (var m in _lobbyParks)
        {
            if (m?.Root == null || !IsInstanceValid(m.Root)) continue;
            // ⚠ GLOBAL, not local. The root carries `AuthoredScale`, so a local span is a tenth
            // of the real one -- which came out as 103 against a 1030-unit island and was then
            // swallowed whole by the MinBehind clamp, so the camera never moved at all.
            var p = m.Root.GlobalPosition;
            x0 = Mathf.Min(x0, p.X); x1 = Mathf.Max(x1, p.X);
            z0 = Mathf.Min(z0, p.Z); z1 = Mathf.Max(z1, p.Z);
        }
        return x1 <= x0 ? 100f : Mathf.Max(x1 - x0, z1 - z0);
    }

    /// <summary>One frame of every lobby model. ⭐ Real seconds at `Animation.Fps`, the same clock
    /// the laptop's preview runs on -- the console starts these at rate 1.0.</summary>
    void StepLobby(double delta)
    {
        if (!_lobbyMode) return;
        // ⚠⚠ AIM AFTER STARTUP, NOT DURING IT. `StartGameCam` resets the camera and places it
        // on the plot centre, and it runs after the mode dispatch that builds this scene -- so an
        // aim inside EnterLobby is simply overwritten. The lobby has no plot, so the reset left
        // the camera wherever the default is, looking at open water.
        if (!_lobbyAimed) { _lobbyAimed = true; LobbyAimCamera(); }
        _lobbyModelTime += (float)delta * Aps.Fps;
        foreach (var m in _lobbyParks)
        {
            if (m?.Root == null || !IsInstanceValid(m.Root) || m.Frames <= 0) continue;
            m.SetFrame(_lobbyModelTime % m.Frames);
        }
    }
}
