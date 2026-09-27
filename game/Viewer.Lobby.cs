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

        // ⭐⭐ STAND WHERE THE GAME STANDS. The authored camera node beats anything computed: it
        // is per park, it is already in the island's frame, and it carries the height and the
        // offset the console uses. What this port had instead was a distance derived from the
        // scene's span -- a reasonable guess, and not the game's.
        //
        // ⚠ The free camera is used rather than the console's `_game`, because that one couples
        // zoom to pitch through `Behind` and cannot be put at an arbitrary eye point at all.
        // Inverting its own placement -- eye = focus + (cos p sin y, sin -p, cos p cos y) * dist.
        if (mi < _lobbyCams.Count && _lobbyBaseRoot != null && IsInstanceValid(_lobbyBaseRoot))
        {
            var eye = (_lobbyBaseRoot.GlobalTransform * _lobbyCams[mi]).Origin;
            var off = eye - at;
            float d = off.Length();
            if (d > 0.01f)
            {
                _freeCam = true;
                _focus = at;
                _dist = d;
                _pitch = -Mathf.Asin(Mathf.Clamp(off.Y / d, -1f, 1f));
                _yaw = Mathf.Atan2(off.X, off.Z);
                GD.Print($"[lobby] camera on {LobbySlots.ModelNames[mi]}: authored eye "
                       + $"({eye.X:F0},{eye.Y:F0},{eye.Z:F0}) -> focus ({at.X:F0},{at.Y:F0},{at.Z:F0}), "
                       + $"dist {d:F0}, pitch {Mathf.RadToDeg(_pitch):F0} deg");
                return;
            }
        }
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
    /// <summary>⚠⚠ THE PARK IS NOT ONE NODE. `_park.Root` holds the placed things, but the
    /// terrain, the entrance gate, the flags, the guests and the tool overlays are all SEPARATE
    /// children of the viewer -- so hiding the park root alone leaves the gate, the ground and the
    /// crowd standing in the middle of the lobby. The first close-park render came back showing
    /// HALLOW's gate with the lobby camera behind it, which is exactly that.
    ///
    /// ⚠ The sky and the weather stay: the lobby is an island in the sea and wants both.</summary>
    void ShowParkScene(bool on)
    {
        // ⚠ NAME WHAT WAS ACTUALLY TOUCHED. The first attempt reported nothing and the render
        // was unchanged, which reads identically to "the nodes are not the park" and to "the call
        // never ran". A count of what was found tells those two apart.
        var hit = new List<string>();
        if (_park?.Root != null) hit.Add("park");
        if (_terrain?.Root != null && IsInstanceValid(_terrain.Root)) hit.Add("terrain");
        if (_gateBox?.Root != null && IsInstanceValid(_gateBox.Root)) hit.Add("gate");
        if (_flags?.Root != null && IsInstanceValid(_flags.Root)) hit.Add("flags");
        GD.Print($"[lobby] park scene -> {(on ? "shown" : "hidden")}: {(hit.Count == 0 ? "NOTHING" : string.Join(",", hit))}");
        if (_park != null) _park.Root.Visible = on;
        if (_terrain?.Root != null && IsInstanceValid(_terrain.Root)) _terrain.Root.Visible = on;
        if (_gateBox?.Root != null && IsInstanceValid(_gateBox.Root)) _gateBox.Root.Visible = on;
        if (_flags?.Root != null && IsInstanceValid(_flags.Root)) _flags.Root.Visible = on;
        if (_thoughts?.Root != null && IsInstanceValid(_thoughts.Root)) _thoughts.Root.Visible = on;
        if (_selectView?.Root != null && IsInstanceValid(_selectView.Root)) _selectView.Root.Visible = on;
        if (_ghostView?.Root != null && IsInstanceValid(_ghostView.Root)) _ghostView.Root.Visible = on;
        // ⚠ NOT `_player` -- that is an AudioStreamPlayer, not a visual node. It is a child of
        // the viewer like the rest, which is exactly why "hide everything I added" is the wrong
        // rule and each node has to be named.
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
        ShowParkScene(false);            // the island REPLACES the park, it does not join it
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
        ShowParkScene(true);             // hidden by CloseParkToLobby
        LoadMap(idx);
        Status($"{name}");
    }

    /// <summary>Tear the scene down. ⚠ The models hang off `base.Root`, so freeing the lobby root
    /// takes all nine with it -- but the list has to be cleared too or the stepper keeps walking
    /// freed nodes.</summary>
    void LeaveLobby()
    {
        _lobbyMode = false;
        _lobbyParks.Clear();
        _lobbyCams.Clear();
        _lobbyBaseRoot = null;
        _lobbyBaseMesh = null;
        if (_lobbyRoot != null && IsInstanceValid(_lobbyRoot)) _lobbyRoot.QueueFree();
        _lobbyRoot = null;
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
        if (!_lobbyAimed)
        {
            _lobbyAimed = true;
            LobbyAimCamera();
            // ⚠ After the aim, and once: entering tears the scene down, and doing that from
            // inside EnterLobby would free the nodes the rest of startup still walks.
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
