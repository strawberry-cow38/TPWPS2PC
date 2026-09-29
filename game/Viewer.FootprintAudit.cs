using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>⭐ WHICH RIDES SIT WRONG IN THEIR FOOTPRINT, AND BY HOW MUCH -- as a list, not an
/// impression. Master: "some rides are misaligned in their footprint. some a lot more than others.
/// and some footprints may be wrong."
///
/// ⚠⚠ THE PORT CENTRES A BOUNDING BOX, WHICH IS NOT WHAT THE CONSOLE DOES. `Park.TryPlace` measures
/// the model's drawn bounds and slides the model until that box's centre lands on the footprint's
/// centre. A model whose geometry is lopsided -- a fence down one side, a sign, a hoarding -- has a
/// bounding box whose centre is nowhere near the thing you look at, so it lands off by exactly half
/// that asymmetry. That is precisely "some a lot more than others": the symmetric rides are fine
/// and the lopsided ones are not. The Belly Bounce already needed a special case (`onlyNamed:
/// "floor"`) for this, which is the same bug appearing once.
///
/// ⭐ THE ALTERNATIVE THIS MEASURES: the model's OWN ORIGIN. If the console anchors a ride by its
/// origin rather than by a box it measures at runtime, then across the catalogue the origin should
/// sit at a consistent place in the shape -- and the rides that are visibly off should be exactly
/// the ones where the origin and the box centre disagree. This prints both for every buildable so
/// the question is settled by the spread of the numbers rather than by picking a ride and looking.
///
/// ⚠ It reports UNFILTERED. Every buildable, including the ones that look fine, because a list of
/// only the bad ones cannot show whether a proposed rule is better or merely different.</summary>
public partial class Viewer
{
    bool _footprintAudit;

    void FootprintAudit()
    {
        // ⚠⚠ NOT PurchasableRides(). That filters to PlacementCost > 0, and a COASTER is priced
        // per track unit rather than by a fixed cost -- so every coaster station was excluded from
        // the census, which is precisely the set that needed checking when the anchor rule changed
        // under tinyclaw's station placement. An audit's exclusions are where its blind spot is.
        var rows = new List<(AssetLibrary.RideAssets Assets, RideDefinition Def)>();
        foreach (int i in BuildableRows())
        {
            var rr = _lib.Rides[i];
            var dd = DefinitionFor(rr.Model);
            if (dd != null) rows.Add((rr, dd));
        }
        GD.Print($"[fp] {rows.Count} buildable things in {_lib.WadName} (UNFILTERED -- "
               + "includes the per-unit-priced coasters)");
        GD.Print("[fp] name | shape WxH | model cells | origin-in-shape (cells) | "
               + "box-centre-in-shape | disagreement | floor?");
        var offsets = new List<float>();
        int noShape = 0, wrongSize = 0;
        foreach (var (r, def) in rows)
        {
            var drawn = LoadPlaceable(r);
            if (drawn?.Root == null) { GD.Print($"[fp] {Leaf(r.Name)}: model would not load"); continue; }
            // ⚠ inParent: false -- the model's OWN frame, so the origin is at (0,0,0) by
            // definition and the bounds are measured against it.
            var (lo, hi) = Park.DrawnBounds(drawn.Root, inParent: false);
            var (flo, fhi) = Park.DrawnBounds(drawn.Root, inParent: false, onlyNamed: "floor");
            bool hasFloor = fhi.X > flo.X && fhi.Z > flo.Z;
            drawn.Root.QueueFree();

            float wCells = (hi.X - lo.X) / Park.CellSize, hCells = (hi.Z - lo.Z) / Park.CellSize;
            string shape = def.Shape == null ? "(none)" : $"{Park.Footprint.From(def.Shape).Width}x{Park.Footprint.From(def.Shape).Height}";
            if (def.Shape == null) noShape++;
            else
            {
                var fp = Park.Footprint.From(def.Shape);
                if (Mathf.Abs(wCells - fp.Width) > 0.5f || Mathf.Abs(hCells - fp.Height) > 0.5f) wrongSize++;
            }
            // Where the model's origin sits inside its own drawn box, in cells, measured from the
            // box's minimum corner. A consistent rule shows up as a consistent pair here.
            float ox = -lo.X / Park.CellSize, oz = -lo.Z / Park.CellSize;
            // And where the box CENTRE sits, which is what the port currently uses.
            float cx = (hi.X - lo.X) * 0.5f / Park.CellSize, cz = (hi.Z - lo.Z) * 0.5f / Park.CellSize;
            float disagree = Mathf.Sqrt((ox - cx) * (ox - cx) + (oz - cz) * (oz - cz));
            offsets.Add(disagree);
            GD.Print($"[fp] {Leaf(r.Name),-28} | {shape,-7} | {wCells,5:F2}x{hCells,-5:F2} | "
                   + $"({ox,6:F2},{oz,6:F2}) | ({cx,6:F2},{cz,6:F2}) | {disagree,6:F2} | "
                   + (hasFloor ? "floor" : "-"));
        }
        offsets.Sort();
        if (offsets.Count > 0)
            GD.Print($"[fp] origin vs box-centre disagreement over {offsets.Count}: "
                   + $"median {offsets[offsets.Count / 2]:F3} cells, "
                   + $"90th {offsets[(int)(offsets.Count * 0.9)]:F3}, worst {offsets[^1]:F3}");
        GD.Print($"[fp] {noShape} with no Info.Shape (footprint measured off the model); "
               + $"{wrongSize} whose model extent disagrees with the declared shape by over half a cell");
    }
}
