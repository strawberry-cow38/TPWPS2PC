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
    /// <summary>⭐⭐ THE CONSOLE'S OWN LOBBY CAMERA, one authored node per park. Each park has TWO
    /// fittings on `base` under the same id: `0x400` is where the park STANDS and **`0x1000` is
    /// where the camera sits** -- and `FUN_00217b48` looks the second one up with exactly that
    /// mask and the record->model key. Measured, all eight: 18..23 units above the park and a
    /// steady 21..29 away from it. That is a camera rig, not a coincidence.
    ///
    /// ⚠ Stored in `base`'s space, like the seats, and taken through `base.Root` when used.</summary>
    readonly List<Transform3D> _lobbyCams = new();
    Node3D _lobbyBaseRoot;
    LobbyMessageBox _lobbyBox;
    bool _lobbyBoxHasFont;
    UiPanel _lobbyPanel;
    bool _lobbyPrompt;
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
    /// <summary>⭐ THE FRONT END. The console starts here and reaches the lobby through it, so
    /// this is the route rather than `--lobby` being the front door.
    ///
    /// ⚠ Movies are skipped with a note on screen, on master's instruction -- the sequencing is
    /// decoded (pair table `0x35ED50`, pair index = the lobby's world index) but nothing plays an
    /// `.MPC` here.</summary>
    void EnterMainMenu()
    {
        LoadHudFont();
        if (_uiRoot == null || _hudFont == null)
        { GD.PrintErr("[menu] no UI root or font -- front end stays off"); return; }
        _mainMenu ??= MainMenu.Create(_lib, _hudFont, _text);
        if (_mainMenu == null) return;
        if (_mainMenu.GetParent() == null) _uiRoot.AddChild(_mainMenu);
        _mainMenu.Chosen -= OnMenuChosen;
        _mainMenu.Chosen += OnMenuChosen;
        HideParkScene();
        _mainMenu.Open_();
        GD.Print("[menu] front end open");
        Status("main menu -- arrows, Enter to choose");
    }

    void OnMenuChosen(MainMenu.Action what)
    {
        GD.Print($"[menu] chose {what}");
        switch (what)
        {
            case MainMenu.Action.MainGame:
                _mainMenu.Hide();
                EnterLobby();           // ⭐ the console's own route: front end -> world map
                return;
            case MainMenu.Action.Exit:
                _mainMenu.Hide();
                Status("exit -- nothing to quit to in this port");
                return;
        }
    }

    void EnterLobby()
    {
        var wad = _lib.WadFiles().FirstOrDefault(
            f => f.Path.EndsWith("LOBBY.WAD", StringComparison.OrdinalIgnoreCase));
        if (wad == null) { GD.PrintErr("[lobby] no LOBBY.WAD on the disc"); return; }
        _lib.OpenWad(wad.Path);
        _texCache.Clear();
        IndexRides();

        // ⚠⚠ THE HUD FONT IS LOADED LAZILY FROM `ShowMoney`, WHICH HAS NOT RUN YET. EnterLobby
        // happens during startup, before the first `_Process`, so the message box was being
        // configured with a null font and silently drew nothing -- the instrument said
        // "font=NULL" with the right text and the right size sitting behind it. `LoadHudFont` is
        // idempotent, so asking for it here costs nothing and removes the ordering entirely.
        LoadHudFont();

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
        // ⚠ MEASURE WHAT IS ACTUALLY DRAWN, not what the matrices say. The bridges look
        // clustered on the overview while `base`'s own node origins span x 0..85, z 0..113 -- so
        // either the parts are not being placed by those matrices or the geometry is not where
        // the origins are, and a picture cannot tell those two apart.
        var (rlo, rhi) = Park.DrawnBounds(room.Root, inParent: true);
        GD.Print($"[lobby] base drawn bounds x {rlo.X:F0}..{rhi.X:F0} y {rlo.Y:F0}..{rhi.Y:F0} "
               + $"z {rlo.Z:F0}..{rhi.Z:F0}");

        // ⭐⭐ EACH PARK SITS ON A FITTING OF `base`, found by ID, under mask 0x400.
        // `FUN_00216f90` searches with its own loop index plus one -- so the id is the MODEL's
        // number, which is why nothing here is indexed by record.
        _lobbyBaseRoot = room.Root;
        _lobbyCams.Clear();
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
            // ⭐⭐ PARENTED TO `base`, NOT TO THE LOBBY ROOT. The seat matrix is expressed in
            // `base`'s OWN space -- it came out of `base`'s node table -- but `AnimatedModel`
            // gives `base.Root` a transform of its own, so its drawn geometry lands at
            // x -138..258, z -258..138 while the raw node origins are x 0..85, z 0..113. Adding
            // the parks beside `base` put them in the second frame and the island in the first:
            // they overlapped enough to look nearly right and were nowhere near aligned.
            //
            // ⚠ Hanging them off `base.Root` makes the seat mean what it says, whatever that root
            // does -- and it cannot drift if that transform ever changes.
            room.Root.AddChild(drawn.Root);
            _lobbyParks.Add(drawn);

            // ⭐ The camera node for this same id, under the OTHER mask.
            var camFit = fits.FirstOrDefault(f => f.Id == wantId && (f.Flags & LobbySlots.CameraMask) != 0);
            var camAt = seat;
            if (camFit.Id == wantId && world.TryGetValue(_lobbyBaseMesh.NodeOffset(camFit.Node), out var cm))
                camAt = ToGodot(cm);
            else GD.PrintErr($"[lobby] {LobbySlots.ModelNames[model]}: no {LobbySlots.CameraMask:x} camera node");
            _lobbyCams.Add(camAt);
            var (plo, phi) = Park.DrawnBounds(drawn.Root, inParent: true);
            GD.Print($"[lobby]   {LobbySlots.ModelNames[model],-9} drawn x {plo.X:F0}..{phi.X:F0} "
                   + $"z {plo.Z:F0}..{phi.Z:F0}");
        }

        // ⭐ CENSUS, ONCE, AT LOAD: which node axis points at each park. One run answers it for
        // all eight instead of eight runs answering it for one -- and if they disagree, that is
        // the finding rather than a wrong camera I would have to notice by eye.
        for (int i = 0; i < _lobbyCams.Count && i < _lobbyParks.Count; i++)
        {
            var ct = _lobbyBaseRoot.GlobalTransform * _lobbyCams[i];
            var dir = (_lobbyParks[i].Root.GlobalPosition - ct.Origin).Normalized();
            var cand = new (string N, Vector3 V)[]
            {
                ("+X", ct.Basis.X), ("-X", -ct.Basis.X), ("+Y", ct.Basis.Y),
                ("-Y", -ct.Basis.Y), ("+Z", ct.Basis.Z), ("-Z", -ct.Basis.Z),
            };
            string bn = "?"; float bd = float.MinValue;
            foreach (var (n, v) in cand)
            {
                if (v.LengthSquared() < 1e-6f) continue;
                float d = v.Normalized().Dot(dir);
                if (d > bd) { bd = d; bn = n; }
            }
            GD.Print($"[lobby] axis census {LobbySlots.ModelNames[i],-9} -> {bn} (dot {bd:F2}), "
                   + $"basis det {ct.Basis.Determinant():F2}");
        }

        // ⭐ The centred box that names the park. ⚠ Parented to the UI root, not to the lobby's
        // 3D root -- it is a Control, and it must outlive nothing but the lobby itself.
        if (_uiRoot != null && (_lobbyBox == null || !IsInstanceValid(_lobbyBox)))
        {
            _lobbyBox = new LobbyMessageBox();
            _lobbyBox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            // ⚠ The Viewer is a Node3D, so there is no `this` to fall back on for a Control --
            // without a UI root the box simply is not built, rather than being parented somewhere
            // that would never draw it.
            _uiRoot?.AddChild(_lobbyBox);
        }
        // ⚠ The panel art comes from UI.WAD, so it needs the library -- loaded once and kept.
        _lobbyPanel ??= UiPanel.Load(_lib);
        if (_lobbyPanel == null) GD.PrintErr("[lobby] UI.WAD/messages panel art missing -- box draws bare");
        _lobbyBox?.Configure(_hudFont, _lobbyPanel);
        _lobbyBoxHasFont = _hudFont != null;

        _lobbyMode = true;
        _lobbyRecord = 0;
        // ⭐ Stand on the park we just closed, if that is how we got here.
        if (_lobbyWantRecord >= 0)
        {
            int w = _lobbyWantRecord >> 8, pk = _lobbyWantRecord & 0xFF;
            _lobbyWantRecord = -1;
            for (int i = 0; i < _lobbySlots.All.Count; i++)
                if (_lobbySlots.All[i].IsPark && _lobbySlots.All[i].World == w
                    && _lobbySlots.All[i].ParkInWorld == pk) { _lobbyRecord = i; break; }
        }
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
        // ⭐⭐ The box carries the park's own `STR_MAP_*` name, and the console's own buttons:
        // `STR_MAP_OK` (489) and `STR_MAP_CANCEL` (304). ⚠ The name is TWO lines on the disc
        // ("Lost Kingdom:\nPrehistoric World"), which is exactly why the box has a second line and
        // sizes itself differently when one is present.
        ShowLobbyBox(_lobbyRecord);
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

    Transform3D _lobbyCamNow, _lobbyCamTarget;
    bool _lobbyCamReady;

    /// <summary>⭐⭐⭐ THE CAMERA IS THE NODE'S WHOLE TRANSFORM -- POSITION **AND** FACING.
    ///
    /// Master, who has played it: "the camera positions for each park is completely wrong". They
    /// were. This used to aim at the park's CENTRE from the node's position; the node carries its
    /// own rotation, the console uses it, and I was throwing it away.
    ///
    /// ⭐ `FUN_00217b48` says so plainly -- it takes the `0x1000` node's matrix and then
    ///   - `FUN_001a9940` makes a quaternion of it and `FUN_001a9bb8(0.8f, ...)` SLERPS that into
    ///     `this+0x2a0`, so the ORIENTATION is the node's, eased at 0.8;
    ///   - the translation LERPs into `this+0x2e0..0x2e8` at **0.2** (0.1 while `0x2ef8c4` is set,
    ///     and **1.0** when its second argument is non-zero -- the snap).
    /// Nothing in it looks at the park. Reading the consumer settled what measuring the node could
    /// not: the offsets were real, and the use I made of them was the invention.</summary>
    void LobbyAimCamera(bool snap = false)
    {
        int mi = _lobbySlots?.ModelIndex(_lobbyRecord) ?? -1;
        if (mi < 0 || mi >= _lobbyCams.Count || _lobbyBaseRoot == null
            || !IsInstanceValid(_lobbyBaseRoot)) return;
        // ⚠⚠ THE AUTHORED BASIS IS MIRRORED AND CANNOT BE USED AS A CAMERA BASIS DIRECTLY.
        // The plot frame reflects, so `GlobalTransform.Basis` here has a NEGATIVE determinant --
        // `Orthonormalized()` keeps the reflection, `Basis.Slerp` then throws
        // "Quaternion is not normalized", and the whole frame dies before the shot is saved.
        //
        // ⭐ So the facing is taken as a DIRECTION and a clean right-handed basis is built from
        // it. Which of the node's axes is "forward" is not assumed: each of the six is tested
        // against the direction to the park and the best one wins, which both gets the view right
        // and MEASURES the convention. The chosen axis is logged, so if it is the same one on all
        // eight parks that is a fact worth writing down rather than a guess worth keeping.
        var t = _lobbyBaseRoot.GlobalTransform * _lobbyCams[mi];
        var toPark = _lobbyParks[mi].Root.GlobalPosition - t.Origin;
        var axes = new (string Name, Vector3 V)[]
        {
            ("+X", t.Basis.X), ("-X", -t.Basis.X), ("+Y", t.Basis.Y),
            ("-Y", -t.Basis.Y), ("+Z", t.Basis.Z), ("-Z", -t.Basis.Z),
        };
        string best = "-Z"; var fwd = -t.Basis.Z; float bestDot = float.MinValue;
        foreach (var (name, v) in axes)
        {
            if (v.LengthSquared() < 1e-6f) continue;
            float d = v.Normalized().Dot(toPark.Normalized());
            if (d > bestDot) { bestDot = d; best = name; fwd = v; }
        }
        // ⚠ `LookingAt` needs an up that is not parallel to the forward; world up unless the
        // camera is looking straight down, which these do not but a future one might.
        var up = Mathf.Abs(fwd.Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
        _lobbyCamTarget = new Transform3D(Basis.LookingAt(fwd.Normalized(), up), t.Origin);
        GD.Print($"[lobby] {LobbySlots.ModelNames[mi]} camera forward = node {best} "
               + $"(dot {bestDot:F2} with the direction to the park)");
        if (snap || !_lobbyCamReady) { _lobbyCamNow = _lobbyCamTarget; _lobbyCamReady = true; }
        _freeCam = true;                 // keep the console camera out of it; the lobby drives
        GD.Print($"[lobby] camera on {LobbySlots.ModelNames[mi]}: authored node at "
               + $"({_lobbyCamTarget.Origin.X:F0},{_lobbyCamTarget.Origin.Y:F0},{_lobbyCamTarget.Origin.Z:F0})"
               + (snap ? " (snapped)" : ""));
    }

    /// <summary>⚠ A DEBUG view, not the game's: straight down over the middle of the islands so
    /// one render shows all eight. The console has no such camera.</summary>
    void LobbyOverviewCamera()
    {
        var c = LobbyCentre();
        float span = LobbySpan();
        var eye = c + new Vector3(0.01f, span * 1.1f, 0.01f);
        _lobbyCamTarget = new Transform3D(Basis.LookingAt(c - eye, Vector3.Forward), eye);
        _lobbyCamNow = _lobbyCamTarget; _lobbyCamReady = true;
        GD.Print($"[lobby] OVERVIEW (debug) over ({c.X:F0},{c.Z:F0}), {span * 1.1f:F0} up");
    }

    /// <summary>Ease toward the authored node the way the console does: orientation fast (0.8),
    /// position slower (0.2), per CONSOLE frame so the glide runs at one speed anywhere.</summary>
    void StepLobbyCamera(double delta)
    {
        if (!_lobbyCamReady || _cam == null) return;
        float step = Mathf.Clamp((float)delta * ConsoleClock.TicksPerSecond, 0f, 1f);
        _lobbyCamNow = new Transform3D(
            _lobbyCamNow.Basis.Slerp(_lobbyCamTarget.Basis, Mathf.Clamp(0.8f * step, 0f, 1f)).Orthonormalized(),
            _lobbyCamNow.Origin.Lerp(_lobbyCamTarget.Origin, Mathf.Clamp(0.2f * step, 0f, 1f)));
        _cam.Transform = _lobbyCamNow;
    }

    /// <summary>⭐⭐ "CLOSE PARK" LEAVES FOR THE MAP SCREEN, standing on the park you left.
    ///
    /// Master: "route close park to the lobby, on the park u pressed close park in". The disc
    /// agrees with the name: `Close Park` is text 844, **`STR_MAINMENU_EXIT_TO_MAP_SCREEN`**, so
    /// it was never the opposite of Open Park -- which is also why the console appends it with no
    /// condition while Open Park is conditional.
    ///
    /// ⚠ The park you were in is found from the MAP that is loaded, not from anything the lobby
    /// remembers: archive name gives the world, `terrain_1`/`terrain_2` gives the park, and the
    /// record with that pair is the slot to stand on. That is the exact inverse of
    /// <see cref="LobbyEnterPark"/>, so the two cannot disagree.</summary>
    readonly List<Node3D> _hiddenForLobby = new();

    /// <summary>⚠⚠ THE PARK IS NOT ONE NODE, AND NAMING THEM ALL DOES NOT WORK.
    ///
    /// First attempt hid `_park.Root` and HALLOW's gate stayed. The second named the terrain, gate
    /// and flags too, and master: "the gate and bus (and probably more stuff) of the park u exited
    /// still exist". There is no fixed list -- the bus, the park vehicles and the entrance
    /// furniture are each added straight to the viewer by their own file, and the next one added
    /// would be missed the same way.
    ///
    /// ⭐ So the rule is inverted: hide EVERY top-level 3D child except what the lobby itself
    /// needs, and remember exactly what was hidden so leaving restores that and nothing else. A
    /// node added tomorrow is covered without touching this.
    ///
    /// ⚠ The sky and the weather stay -- the lobby is an island in the sea and wants both -- as
    /// do the camera and the lobby's own root.</summary>
    void HideParkScene()
    {
        _hiddenForLobby.Clear();
        foreach (var child in GetChildren())
        {
            if (child is not Node3D n || !IsInstanceValid(n)) continue;
            if (ReferenceEquals(n, _lobbyRoot) || ReferenceEquals(n, _cam)
                || ReferenceEquals(n, _sky) || ReferenceEquals(n, _weather?.Root)) continue;
            if (!n.Visible) continue;                 // already hidden: not ours to restore
            n.Visible = false;
            _hiddenForLobby.Add(n);
        }
        GD.Print($"[lobby] hid {_hiddenForLobby.Count} park nodes: "
               + string.Join(",", _hiddenForLobby.Select(n => n.Name.ToString())));
    }

    /// <summary>Put back exactly what <see cref="HideParkScene"/> took away.</summary>
    void ShowParkScene()
    {
        foreach (var n in _hiddenForLobby) if (IsInstanceValid(n)) n.Visible = true;
        _hiddenForLobby.Clear();
    }

    void CloseParkToLobby()
    {
        _lobbyWantRecord = -1;
        if (_loadedMap >= 0 && _loadedMap < _maps.Count)
        {
            string world = Leaf(_maps[_loadedMap].Wad).Replace(".WAD", "").ToUpperInvariant();
            int w = world switch
            {
                "JUNGLE" => 0, "HALLOW" => 1, "FANTASY" => 2, "SPACE" => 3, _ => -1
            };
            // ⚠ `terrain_2` is the SECOND park; anything else is the first. Matched on the leaf,
            // because the full path differs per archive.
            int pk = Leaf(_maps[_loadedMap].Path).Contains("_2", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            _lobbyWantRecord = (w << 8) | pk;      // packed; EnterLobby resolves it against the table
            GD.Print($"[lobby] close park: {_maps[_loadedMap].Label} -> world {w} park {pk}");
        }
        _shopPanel?.Hide();
        _laptopBack.Clear();
        HideParkScene();                 // the island REPLACES the park, it does not join it
        EnterLobby();
    }

    /// <summary>⭐⭐ CHOOSE THE SELECTED PARK AND GO. The lobby's whole job.
    ///
    /// ⚠ The record already says which park this is, in the game's own terms -- `World` 0..3 and
    /// `ParkInWorld` 0..1 -- so nothing here has to be derived from a model name or a slot order.
    /// Those two fields ARE the map: world picks the archive, park picks `terrain_1` or
    /// `terrain_2`.
    ///
    /// ⚠ The world NAMES are the archives', not the display names. The lobby shows "Lost
    /// Kingdom" and "Wonder Land"; the files are `JUNGLE` and `FANTASY`. Mapping display names to
    /// files would be a second table to keep in step with this one.</summary>
    void LobbyEnterPark()
    {
        if (_lobbySlots == null || !_lobbySlots.All[_lobbyRecord].IsPark) return;
        var rec = _lobbySlots.All[_lobbyRecord];
        string world = rec.World switch
        {
            0 => "JUNGLE", 1 => "HALLOW", 2 => "FANTASY", 3 => "SPACE", _ => null
        };
        if (world == null) { Status($"world {rec.World} has no archive"); return; }
        string want = $"terrain_{rec.ParkInWorld + 1}.mps";
        int idx = _maps.FindIndex(m => m.Label.Contains(world, StringComparison.OrdinalIgnoreCase)
                                    && m.Label.EndsWith(want, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
        {
            GD.PrintErr($"[lobby] no map for world {world} {want}; have "
                      + string.Join(", ", _maps.Select(m => m.Label)));
            Status($"no map for {world} {want}");
            return;
        }
        string name = LobbyName(_lobbyRecord).Replace("\n", " / ");
        GD.Print($"[lobby] entering {name} -> {_maps[idx].Label}");
        LeaveLobby();
        ShowParkScene();                 // put back exactly what CloseParkToLobby hid
        LoadMap(idx);
        Status($"{name}");
    }

    /// <summary>Tear the scene down. ⚠ The models hang off `base.Root`, so freeing the lobby root
    /// takes all nine with it -- but the list has to be cleared too or the stepper keeps walking
    /// freed nodes.</summary>
    void LeaveLobby()
    {
        _lobbyMode = false;
        _lobbyPrompt = false;
        if (_lobbyBox != null && IsInstanceValid(_lobbyBox)) _lobbyBox.Hide();
        _lobbyParks.Clear();
        _lobbyCams.Clear();
        _lobbyBaseRoot = null;
        _lobbyBaseMesh = null;
        if (_lobbyRoot != null && IsInstanceValid(_lobbyRoot)) _lobbyRoot.QueueFree();
        _lobbyRoot = null;
    }

    /// <summary>⭐ Put the selected park's name in the centred box. The disc's names are two
    /// lines -- "Lost Kingdom:\nPrehistoric World" -- and the box's own sizing has a separate
    /// branch for a second line, so the split is the data's rather than a wrap invented here.</summary>
    void ShowLobbyBox(int record)
    {
        // ⚠ Say WHICH precondition failed. A box that never appears reads the same whether it was
        // never built, has no font, or was built and told nothing.
        if (_lobbyBox == null || !IsInstanceValid(_lobbyBox) || _lobbySlots == null)
        {
            GD.PrintErr($"[lobby] no message box: box={(_lobbyBox == null ? "null" : "live")} "
                      + $"uiRoot={(_uiRoot == null ? "null" : "live")} slots={(_lobbySlots == null ? "null" : "ok")}");
            return;
        }
        string raw = LobbyName(record);
        var parts = raw.Split('\n');
        // ⚠⚠ NO BUTTONS ON THE NAME BOX. The prompt table at `0x36DF18` is 6 bytes a mode
        // -- {title id, body id, button bits} -- and the buttons belong to the PROMPTS (modes 2,
        // 4, 5 and 6 carry OK+Cancel; 1, 3 and 7 carry OK alone). The park's name is not one of
        // them, so hanging OK/Cancel on it, as I first did, put a prompt's furniture on a caption.
        _lobbyBox.Show(parts[0], parts.Length > 1 ? parts[1] : "");
        GD.Print($"[lobby] message box: font={(_hudFont == null ? "NULL" : "ok")} "
               + $"\"{parts[0]}\" / \"{(parts.Length > 1 ? parts[1] : "")}\" "
               + $"no buttons, target {_lobbyBox.TargetWidth}x{_lobbyBox.TargetHeight}");
    }

    /// <summary>⭐⭐ PROMPT MODE 6 -- "do you want to enter this park?".
    ///
    /// The mode table at `0x36DF18` is 6 bytes a mode: `{u16 title id, u16 body id, u8 buttons}`.
    /// Mode 6 is `{1062, 810, 0x03}` -- `STR_MAP_TITLE_DO_YOU_WANT_TO_PLAY_THIS_ISLAND` /
    /// `STR_MAP_BODY_...`, with bit 0 = OK and bit 1 = Cancel. The key names name the mode.
    ///
    /// ⭐ And `FUN_00219338`'s case 6 is what the buttons do: index **0** confirms -- it sets the
    /// park (`FUN_001C38B0`), latches leaving and writes **`+0x74 = 1`**, which is the very flag
    /// the story movie is gated on (`findings/main-menu.md`) -- and ANY OTHER index cancels.</summary>
    void ShowLobbyPrompt()
    {
        if (_lobbyBox == null || !IsInstanceValid(_lobbyBox) || _text == null) return;
        string T(int id) => id >= 0 && id < _text.Keys.Length ? _text.Text("eng", id) ?? $"#{id}" : $"#{id}";
        string ok = T(0x1E9), cancel = T(0x130);
        _lobbyPrompt = true;
        _lobbyBox.Show(T(1062).Replace("\n", " "), T(810).Replace("\n", " "), ok, cancel);
    }

    /// <summary>⚠ Index 0 is OK and anything else cancels -- `FUN_00219338` tests exactly that
    /// (`if (param_2 != 0) { ...clear...; return; }`), so it is the INDEX that decides, not the
    /// label.</summary>
    void LobbyPromptAnswer()
    {
        _lobbyPrompt = false;
        if (_lobbyBox is { Button: 0 }) { LobbyEnterPark(); return; }
        ShowLobbyBox(_lobbyRecord);        // cancelled: back to the park's name
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
        StepLobbyCamera(delta);
        if (_lobbyBox != null && IsInstanceValid(_lobbyBox))
        {
            // ⚠ If the font arrives after the box was shown, its measurement is stale -- re-show
            // rather than just re-configure, because the SIZE came from the fallback.
            if (!_lobbyBoxHasFont && _hudFont != null)
            { _lobbyBoxHasFont = true; _lobbyBox.Configure(_hudFont, _lobbyPanel); ShowLobbyBox(_lobbyRecord); }
            _lobbyBox.Step(delta);
        }
        // ⚠⚠ AIM AFTER STARTUP, NOT DURING IT. `StartGameCam` resets the camera and places it
        // on the plot centre, and it runs after the mode dispatch that builds this scene -- so an
        // aim inside EnterLobby is simply overwritten. The lobby has no plot, so the reset left
        // the camera wherever the default is, looking at open water.
        if (!_lobbyAimed)
        {
            _lobbyAimed = true;
            LobbyAimCamera(snap: true);     // the opening view does not glide in from nowhere
            if (_lobbyOverview) LobbyOverviewCamera();
            // ⚠ After the aim, and once: entering tears the scene down, and doing that from
            // inside EnterLobby would free the nodes the rest of startup still walks.
            // ⚠ Snapped, because a wound render draws at about 1 fps and the open takes ~11
            // steps -- the picture would otherwise always catch it mid-grow, which is exactly what
            // it did twice.
            if (_lobbyPromptTest)
            { _lobbyPromptTest = false; ShowLobbyPrompt(); _lobbyBox?.SnapForShot(); }
            if (_lobbyEnter > 0)
            {
                int want = _lobbyEnter - 1;
                _lobbyEnter = 0;
                if (want >= 0 && want < _lobbySlots.All.Count && _lobbySlots.All[want].IsPark)
                { _lobbyRecord = want; LobbyReport(); }
                LobbyEnterPark();
                return;
            }
        }
        _lobbyModelTime += (float)delta * Aps.Fps;
        foreach (var m in _lobbyParks)
        {
            if (m?.Root == null || !IsInstanceValid(m.Root) || m.Frames <= 0) continue;
            m.SetFrame(_lobbyModelTime % m.Frames);
        }
    }
}
