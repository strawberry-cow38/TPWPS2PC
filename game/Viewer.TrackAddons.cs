using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

public partial class Viewer
{
    /// <summary>⭐⭐ TRACK-RIDE ADD-ONS: the console's tool mode 10 (`0x389610`, vtable `0x35bef0`).
    /// An add-on is bought from the build menu's Addons (catalogue kind 8, `0x197f48` jump table →
    /// `0x19807c`) and dropped onto two straights of a track ride: a mammoth tunnel, a lava jump, a
    /// water tunnel... It becomes a 4×4 piece in the track's chain (<see cref="TrackLayout.AddUpgrade"/>)
    /// and the straight after it is hidden under its model.
    ///
    /// The tool (READ `0x129cf0`..`0x12a014`): enter makes a preview piece of type `idx·4 + 0x28` on
    /// the SELECTED ride (`0x125460` passes `*(0x1497b0()+0x88)` for mode 10) and loads the price,
    /// catalogue kind 8 payload `+0x20`; every frame the preview follows the cursor and `0x1fea58`
    /// judges it; Cross (`0x129e38`) offers it to each track ride in turn through `0x202980`, and on
    /// success removes the preview, returns to mode 0, plays (bank 2, `0xb8`) and debits price × 10;
    /// Triangle (`0x129f88`) removes the preview and plays `0xd8`.
    ///
    /// ⚠ PORT DEPARTURES, named: (1) with no ride selected the port uses the park's first track ride,
    /// where the console would build its preview on a null ride; (2) the console's cursor is free and
    /// the box's corner IS the cursor, so the one valid spot per pair of straights has to be hit
    /// exactly -- the port tries every 4×4 box that covers the cursor cell and shows the nearest one
    /// the rule accepts; (3) the port's tool sounds are its own four cues, so place plays Lay and
    /// cancel is silent; (4) the ride panel's route in (`0x1d5dc8`) waits for the laptop's ride
    /// screen.</summary>
    sealed class AddonTool
    {
        public TrackRideView View;
        public AssetLibrary.RideAssets Asset;
        public int Kind, Price;
        public Node3D Preview;
        public int PreviewType = -1;
        public ParkCell Box;
        public bool Valid;
    }

    AddonTool _addonTool;

    /// <summary>The compiled record's key for a library asset, which is what the catalogue lists hold.</summary>
    uint? DbaKey(AssetLibrary.RideAssets r) => r?.Model == null ? null : DefinitionFor(r.Model)?.CompiledEntry?.Key;

    /// <summary>Is this build-menu row something this park sells? Everything is, except an add-on
    /// missing from the park's own list (TrackUpgrades): the menu is per park on the console, and the
    /// port's library is per world.</summary>
    bool SoldHere(AssetLibrary.RideAssets r)
        => (BuildKind(r) != AssetResourceDatabase.AssetKind.TrackUpgrade
            || DbaKey(r) is uint k && TrackUpgrades.KindOf(TrackWorld, TrackPark, k) >= 0)
           && ResearchedHere(r);                                    // Viewer.Research.cs: this park's catalogue, researched

    /// <summary>The library asset of the park's add-on <paramref name="kind"/>: its model is the piece's
    /// mesh (shapes 12 and 13, whose ids in `0x2ecad0` are these same keys).</summary>
    AssetLibrary.RideAssets AddonAsset(int kind)
    {
        var list = TrackUpgrades.ForPark(TrackWorld, TrackPark);
        if (kind < 0 || kind >= list.Count || _lib?.Rides == null) return null;
        uint key = list[kind];
        return _lib.Rides.FirstOrDefault(r => DbaKey(r) == key && BuildKind(r) == AssetResourceDatabase.AssetKind.TrackUpgrade);
    }

    Node3D AddonModel(AssetLibrary.RideAssets assets, out AnimatedModel model, out Aps animation)
    {
        model = LoadPlaceable(assets, out animation, out _);
        if (model?.Root == null) return null;
        model.Root.Scale = Vector3.One;
        var holder = new Node3D { Name = "addon_" + Leaf(assets.Name) };
        holder.AddChild(model.Root);
        return holder;
    }

    /// <summary>The build menu's Addons row: enter mode 10 (`0x129cf0`).</summary>
    bool BeginAddonTool(AssetLibrary.RideAssets r)
    {
        int kind = DbaKey(r) is uint key ? TrackUpgrades.KindOf(TrackWorld, TrackPark, key) : -1;
        if (kind < 0)
        {
            _toolSfx?.Play(ToolSounds.Cue.Refused);
            Status($"{Leaf(r.Name)} isn't sold in this park");
            return false;
        }
        TrackRideView v = null;
        if (_selected >= 0 && _selected < _park.Placed.Count) _tracks.TryGetValue(_park.Placed[_selected].Id, out v);
        v ??= _tracks.Values.FirstOrDefault();
        if (v == null)
        {
            _toolSfx?.Play(ToolSounds.Cue.Refused);
            Status("add-ons go on a track ride's track: build the track ride first");
            return false;
        }
        if (_toolOpen) CloseTool();
        _place.Clear();
        CancelAddonTool(quiet: true);
        var def = DefinitionFor(r.Model);
        int price = def?.CompiledEntry?.SimpleEconomy?.PurchaseCost ?? 0;
        _addonTool = new AddonTool { View = v, Asset = r, Kind = kind, Price = price };
        _previewCost = price * 10; _previewStock = null;
        GD.Print($"[addon] {Leaf(r.Name)}: kind {kind} of this park's {TrackUpgrades.ForPark(TrackWorld, TrackPark).Count}, "
               + $"{price} ({Money.Format(price * 10)}), onto ride {v.Id} ({v.Layout.Upgrades.Count} of {TrackLayout.MaxUpgrades} used)");
        Status($"{DisplayName(r, def)}: put it on two straights of the track -- click to buy, Esc to cancel");
        UpdateAddonGhost();
        return true;
    }

    /// <summary>Triangle (`0x129f88`): drop the preview and leave the tool.</summary>
    void CancelAddonTool(bool quiet = false)
    {
        var t = _addonTool;
        if (t == null) return;
        _addonTool = null;
        if (t.Preview != null && IsInstanceValid(t.Preview)) t.Preview.QueueFree();
        _ghostView?.Clear();
        _previewCost = null; _previewStock = null;
        if (!quiet) Status("add-on put back");
    }

    /// <summary>The piece of ANY track ride covering a cell, the way `0x14a420` asks.</summary>
    (TrackRideView View, TrackPiece Piece) AnyTrackPieceAt(ParkCell c)
    {
        foreach (var v in _tracks.Values)
            if (v.Layout.PieceAt(c) is { } p) return (v, p);
        return (null, null);
    }

    /// <summary>`0x202848`: pieces an add-on may not be laid over even when they are plain straights --
    /// with the loop closed, slot 2 (the exit straight) and the last piece; open, slots 0 and 3 and the
    /// last. The slots are the piece array's (`ride+0x1d8 + i·0x108`).</summary>
    static bool AddonExcluded(TrackLayout layout, TrackPiece piece)
    {
        int i = -1;
        for (int k = 0; k < layout.Pieces.Count; k++) if (ReferenceEquals(layout.Pieces[k], piece)) { i = k; break; }
        int last = layout.Pieces.Count - 1;
        return layout.Closed ? i == 2 || i == last : i == 0 || i == 3 || i == last;
    }

    /// <summary>A tile's height as `tile[+1] × 4`: the loader writes 2 on a raised (`byte0 & 0x40`) cell,
    /// else 0 (`0x14e700`; <see cref="Park.CellY"/>).</summary>
    int TileHeight4(int x, int z)
    {
        var f = _terrainModel?.Field;
        if (f == null || x < 0 || z < 0 || x >= f.Width || z >= f.Height) return 0;
        byte b = f.Raw0(x, z);
        return (b & 0x40) != 0 && (b & 1) == 0 ? 8 : 0;
    }

    bool InGrid(ParkCell c) => c.X >= 0 && c.Z >= 0 && c.X < _park.Width && c.Z < _park.Height;

    /// <summary>⭐ `0x1fea58`, the preview's verdict for the 4×4 box at <paramref name="box"/>. The pieces
    /// over (x+1, z+1) and (x+2, z+2) must be two different plain straights of the same type on this
    /// ride; that type gives the add-on its direction (4→0, 5→2, 6→1, 7→3 as the model's rotation).
    /// Then every in-grid cell must have neighbour steps ≤ 64 to +x, +z and +x+z, and pass `0x1e66c8`
    /// -- this ride's plain straight not excluded by `0x202848`, or, with no track on it, kind-0 ground
    /// that may be built on (`0x1e63c0`) -- and hold no piece but those two. Marks are 165 (0xa5) and
    /// 175 (0xaf); with the pair wrong, cells on the ride's straights show 165 and the rest 175.</summary>
    (bool Valid, int Dir, List<(int X, int Y, int Marker, int Turns)> Marks) AddonVerdict(TrackRideView v, ParkCell box)
    {
        var marks = new List<(int, int, int, int)>();
        var (va, a) = AnyTrackPieceAt(box.Offset(1, 1));
        var (vb, b) = AnyTrackPieceAt(box.Offset(2, 2));
        bool pair = InGrid(box.Offset(0, 4)) && a != null && b != null && va == v && vb == v && !ReferenceEquals(a, b)
                    && a.Type is >= 4 and <= 7 && b.Type == a.Type;
        int dir = pair ? a.Type - 4 : -1;
        bool CellPasses(ParkCell c)   // 0x1e66c8
        {
            var (cv, cp) = AnyTrackPieceAt(c);
            if (cp != null) return cv == v && cp.Info.Shape == 0 && !AddonExcluded(v.Layout, cp);
            var kind = _paths?.KindAt(c.X, c.Z) ?? PathTool.Kind.None;
            return kind == PathTool.Kind.None && (_paths?.CanLay(c.X, c.Z) ?? false) && _park.Vacant(c.X, c.Z);
        }
        bool valid = pair;
        for (int dz = 0; dz < 4; dz++)
            for (int dx = 0; dx < 4; dx++)
            {
                var c = box.Offset(dx, dz);
                if (!InGrid(c)) continue;
                if (!pair)
                {
                    bool teal = CellPasses(c) && AnyTrackPieceAt(c).Piece != null;
                    marks.Add((c.X, c.Z, teal ? 165 : 175, 0));
                    continue;
                }
                int h = TileHeight4(c.X, c.Z);
                bool ok = Math.Abs(TileHeight4(c.X + 1, c.Z) - h) <= 64 && Math.Abs(TileHeight4(c.X, c.Z + 1) - h) <= 64
                          && Math.Abs(TileHeight4(c.X + 1, c.Z + 1) - h) <= 64;
                if (!CellPasses(c)) ok = false;
                var here = AnyTrackPieceAt(c).Piece;
                if (here != null && !ReferenceEquals(here, a) && !ReferenceEquals(here, b)) ok = false;
                if (!ok) valid = false;
                marks.Add((c.X, c.Z, ok ? 165 : 175, 0));
            }
        return (valid, dir, marks);
    }

    /// <summary>Per frame: the box under the cursor, its verdict, and the preview model on it.</summary>
    void UpdateAddonGhost()
    {
        var t = _addonTool;
        if (t == null || !CursorCell(out int x, out int y)) return;
        var cursor = new ParkCell(x, y);
        // Port departure (2): every box covering the cursor cell, best verdict first, then nearest.
        (int Rank, float Far, ParkCell Box, bool Valid, int Dir, List<(int, int, int, int)> Marks) best = default;
        bool any = false;
        for (int bz = 0; bz < 4; bz++)
            for (int bx = 0; bx < 4; bx++)
            {
                var box = cursor.Offset(-bx, -bz);
                var (valid, dir, marks) = AddonVerdict(t.View, box);
                int rank = valid ? 0 : dir >= 0 ? 1 : 2;
                float far = MathF.Abs(box.X + 2 - (x + 0.5f)) + MathF.Abs(box.Z + 2 - (y + 0.5f));
                if (rank == 2 && !(bx == 1 && bz == 1)) continue;   // an unpaired box is shown only centred
                if (!any || rank < best.Rank || rank == best.Rank && far < best.Far)
                { best = (rank, far, box, valid, dir, marks); any = true; }
            }
        if (!any) return;
        bool afford = _sim == null || _sim.Finances.Unlimited || _sim.Finances.Balance >= t.Price * 10;
        t.Box = best.Box;
        t.Valid = best.Valid && afford;   // 0x129dc8: the verdict, gated by the tool's affordable flag
        _ghostView?.ShowTurnedCells(best.Marks.Select(m => (m.Item1, m.Item2, afford ? m.Item3 : 175, m.Item4)), _park);
        ShowAddonPreview(t, 0x28 + t.Kind * 4 + Math.Max(0, best.Dir));
        _previewCost = t.Price * 10; _previewStock = null;
        Status($"add-on {(t.Valid ? "fits" : afford ? "doesn't fit here" : "can't afford it")} at ({t.Box.X},{t.Box.Z})   "
             + $"Cost: {Money.Format(t.Price * 10)}   ({t.View.Layout.Upgrades.Count} of {TrackLayout.MaxUpgrades} on this ride)");
    }

    /// <summary>The preview piece (`ride+0x2700`): the add-on's model at the box, turned like the pieces.</summary>
    void ShowAddonPreview(AddonTool t, int type)
    {
        if (t.Preview == null || !IsInstanceValid(t.Preview))
        {
            t.Preview = AddonModel(t.Asset, out _, out _);
            if (t.Preview == null) return;
            t.Preview.Name = "AddonPreview";
            t.View.Frame.AddChild(t.Preview);
        }
        t.PreviewType = type;
        t.Preview.Transform = PieceTransform(new TrackPiece(type, t.Box));
    }

    /// <summary>`0x1fd0c0` over `0x1fcdb8`: a piece mesh's place in the track frame.</summary>
    static Transform3D PieceTransform(TrackPiece p)
    {
        var info = p.Info;
        int r = info.Rot, w = info.Width << 8, d = info.Depth << 8;
        int ox = p.Anchor.X * 256, oz = p.Anchor.Z * 256;
        switch (r) { case 1: oz += w; break; case 2: ox += w; oz += d; break; case 3: ox += d; break; }
        float angle = r * Mathf.Pi / 2;
        float yaw;
        if (p.Type is >= 12 and <= 19 or >= 28 and <= 31) yaw = 2 * Mathf.Pi - angle;
        else
        {
            yaw = Mathf.Pi - angle;
            (int cx, int cz) = r switch { 0 => (w, d), 1 => (w, -d), 2 => (-w, -d), _ => (-w, d) };
            ox += cx; oz += cz;
        }
        return new Transform3D(new Basis(Vector3.Up, -yaw), new Vector3(ox / 256f, 0, oz / 256f));
    }

    /// <summary>Cross (`0x129e38`): offer the add-on to each track ride until one takes it.</summary>
    void PressAddonTool()
    {
        var t = _addonTool;
        if (t == null) return;
        UpdateAddonGhost();
        if (!t.Valid) { _toolSfx?.Play(ToolSounds.Cue.Refused); return; }
        int cost = t.Price * 10;
        TrackRideView took = null;
        foreach (var v in _tracks.Values)
        {
            // 0x202980's own tests: room for a fourth, a 4-cell margin inside the grid, and the piece at
            // (x+1, z+1) is this ride's.
            var b = t.Box;
            if (v.Layout.Upgrades.Count >= TrackLayout.MaxUpgrades
                || b.X < 0 || b.X + 4 >= _park.Width - 1 || b.Z < 0 || b.Z + 4 >= _park.Height - 1
                || v.Layout.PieceAt(b.Offset(1, 1)) == null) continue;
            if (v.Sim.AddUpgrade(t.Kind, b)) { took = v; break; }
        }
        if (took == null)
        {
            _toolSfx?.Play(ToolSounds.Cue.Refused);
            Status(t.View.Layout.Upgrades.Count >= TrackLayout.MaxUpgrades
                   ? "that track already has its three add-ons" : "no track ride takes an add-on there");
            return;
        }
        if (cost > 0 && _sim != null) { _sim.Finances.Debit(cost); _paidFor[took.Id] = _paidFor.GetValueOrDefault(took.Id) + cost; }
        RebuildTrackView(took);
        _toolSfx?.Play(ToolSounds.Cue.Lay);
        GD.Print($"[addon] {Leaf(t.Asset.Name)} laid on ride {took.Id} at box {t.Box} for {Money.Format(cost)}: "
               + $"upgrades [{string.Join(", ", took.Layout.Upgrades)}], pieces {string.Join(",", took.Layout.Pieces.Select(p => p.Type))}");
        CancelAddonTool(quiet: true);
        Status($"{Leaf(t.Asset.Name)} built");
    }
}
