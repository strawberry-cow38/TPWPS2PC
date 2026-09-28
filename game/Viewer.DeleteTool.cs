using Godot;
using System.Linq;
using System.Collections.Generic;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>⭐⭐ THE DELETE TOOL. Master, 2026-09-28: "press del to turn on, rmb to turn off.
/// drags a square, anything with any part in that zone gets deleted, paths return to grass."
///
/// ⚠⚠ AND WHAT IT MUST NOT TAKE. Master, same breath: "queue tiles themselves cannot be deleted,
/// but if their ride gets deleted, delete em. same with coaster pylons and track ride tracks. only
/// the ride that owns em can delete em."
///
/// ⭐ Both halves of that fall out of the data rather than needing a filter here:
/// <list type="bullet">
/// <item>"any part in that zone" is <see cref="Park.PlacedAt"/>, which tests a thing's FOOTPRINT
/// cell by cell -- so sweeping the marquee's cells finds anything overlapping it, however little
/// of it is inside.</item>
/// <item>track pieces, coaster pylons and add-ons are NOT in `Park.Placed` at all -- they lie on
/// the grass in the track's own chain -- so the sweep cannot reach them even by accident. Their
/// ride takes them through `RemoveTrackView` / `RemoveCoasterView` in
/// <see cref="Viewer.DeletePlaced"/>, which is exactly "only the ride that owns em".</item>
/// <item>a queue cell is refused by <see cref="PathTool.TearUp"/> itself, so the one route in is
/// the owning ride's `ClearQueue`.</item>
/// </list>
/// </summary>
public partial class Viewer
{
    /// <summary>The marquee, or null. `Anchor` is where the drag began; `Cursor` follows.</summary>
    sealed class DeleteTool
    {
        public ParkCell? Anchor;
        public ParkCell Cursor;
        public bool Dragging => Anchor.HasValue;
    }

    DeleteTool _deleteTool;

    /// <summary>The overlay tile the marquee paints. Shares the patrol tool's, which is already
    /// the "a tool is claiming these cells" marker.</summary>
    static int DeleteHighlightTexture => StaffPatrolTool.HighlightTexture;

    bool DeleteToolOpen => _deleteTool != null;

    /// <summary>Del opens it. ⚠ Anything else being held is put down first: reaching for delete
    /// while carrying a blueprint means delete, not place-then-delete.</summary>
    void OpenDeleteTool()
    {
        if (_park == null || _paths == null) { Status("no park to delete from"); return; }
        if (_toolOpen) CloseTool();
        if (_place.Active) { _place.Clear(); _ghostView?.Clear(); }
        CancelAddonTool(quiet: true);
        _deleteTool = new DeleteTool();
        GD.Print("[delete] tool open");
        Status("delete -- drag a box over what to remove; right-click to stop");
    }

    void CloseDeleteTool(bool quiet = false)
    {
        if (_deleteTool == null) return;
        _deleteTool = null;
        _ghostView?.Clear();
        if (!quiet) { GD.Print("[delete] tool closed"); Status("delete tool off"); }
    }

    /// <summary>Follow the cursor and repaint the marquee. ⚠ While NOT dragging it still paints
    /// the single cell under the pointer, so the tool visibly has focus before the first press --
    /// an invisible armed delete tool is how you delete something you did not mean to.</summary>
    void UpdateDeleteTool()
    {
        if (_deleteTool == null) return;
        if (!CursorCell(out int x, out int y)) return;
        _deleteTool.Cursor = new ParkCell(x, y);
        ShowDeleteMarquee();
    }

    /// <summary>The marquee's corners, inclusive, clamped to the park.</summary>
    (int X0, int Z0, int X1, int Z1) DeleteBox()
    {
        var c = _deleteTool.Cursor;
        var a = _deleteTool.Anchor ?? c;
        int x0 = Mathf.Min(a.X, c.X), x1 = Mathf.Max(a.X, c.X);
        int z0 = Mathf.Min(a.Z, c.Z), z1 = Mathf.Max(a.Z, c.Z);
        return (Mathf.Max(x0, 0), Mathf.Max(z0, 0),
                Mathf.Min(x1, (_park?.Width ?? 1) - 1), Mathf.Min(z1, (_park?.Height ?? 1) - 1));
    }

    void ShowDeleteMarquee()
    {
        if (_deleteTool == null || _ghostView == null || _park == null) return;
        var (x0, z0, x1, z1) = DeleteBox();
        var cells = new List<(int, int, int, int)>();
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                cells.Add((x, z, DeleteHighlightTexture, 0));
        _ghostView.ShowTurnedCells(cells, _park);
    }

    /// <summary>Left press: drop the anchor. The drag runs until release.</summary>
    void PressDeleteTool()
    {
        if (_deleteTool == null) return;
        if (!CursorCell(out int x, out int y)) { Status("that click was not over the park"); return; }
        _deleteTool.Cursor = new ParkCell(x, y);
        _deleteTool.Anchor = _deleteTool.Cursor;
        ShowDeleteMarquee();
    }

    /// <summary>Left release: delete everything the box touches. ⚠ A press and release on ONE cell
    /// is a 1x1 box and deletes what is under it -- a click IS a drag of zero size, and making it
    /// a special case would give the tool two behaviours to get right instead of one.</summary>
    void ReleaseDeleteTool()
    {
        if (_deleteTool is not { Dragging: true }) return;
        if (CursorCell(out int x, out int y)) _deleteTool.Cursor = new ParkCell(x, y);
        var (x0, z0, x1, z1) = DeleteBox();
        _deleteTool.Anchor = null;
        CommitDelete(x0, z0, x1, z1);
        ShowDeleteMarquee();
    }

    /// <summary>`--delete-test=x0,z0,x1,z1`: run one marquee through the REAL commit, and report
    /// what the park held before and after. ⚠ Reports the BOX'S CONTENTS, not just a count of
    /// deletions -- "3 deleted" reads the same whether it took the right three or the wrong three.
    /// </summary>
    /// <summary>⭐⭐ astraclaw's case, CONSTRUCTED rather than hoped for. The park-wide scan in
    /// <see cref="RunDeleteTest"/> is honest but it reported VACUOUS on every run -- `--place-test`
    /// never happens to make a queue-over-path junction, so a check that only observes could not
    /// reject the bug it exists for. This builds one: lay path, lay queue over it (that cell is now
    /// `Both`), clear the queue, and require the survivor to tear back to its ground.
    ///
    /// ⚠ ISOLATED BY A SYNTHETIC OWNER above every placed ride's id, so the `ClearQueue` here
    /// cannot touch a real ride's queue. It ends by tearing its own cell up, which restores the
    /// ground and leaves the grid as it was found.
    ///
    /// ⚠ It leaves undo entries behind (`Lay` snapshots), which is acceptable in a harness and
    /// noted so nobody reads a longer undo stack as a defect.</summary>
    void ProbeGroundRecordInvariant()
    {
        if (_paths == null || _park == null) return;
        int owner = 9000;
        foreach (var pl in _park.Placed) if (pl.Id >= owner) owner = pl.Id + 1;

        int fx = -1, fz = -1;
        for (int z = 0; z < _park.Height && fx < 0; z++)
            for (int x = 0; x < _park.Width; x++)
            {
                if (_paths.KindAt(x, z) != PathTool.Kind.None) continue;
                if (!_paths.Lay(x, z, PathTool.Kind.Path)) continue;
                fx = x; fz = z; break;
            }
        if (fx < 0) { GD.Print("[delete] GROUND PROBE: nowhere to lay a test path -- probe VACUOUS"); return; }

        bool laidQueue = _paths.Lay(fx, fz, PathTool.Kind.Queue, owner);
        bool isBoth = _paths.KindAt(fx, fz) == PathTool.Kind.Both;
        bool unknown = false;
        void OnUnknown(int ux, int uz) { if (ux == fx && uz == fz) unknown = true; }
        _paths.GroundUnknown += OnUnknown;
        _paths.ClearQueue(owner);
        bool nowPath = _paths.KindAt(fx, fz) == PathTool.Kind.Path;
        // ⭐ THE LINE THAT REJECTS THE BUG. With the old `_before.Remove(at)` in ClearQueue this is
        // false, TearUp below raises GroundUnknown, and the paving stays on the ground.
        bool kept = _paths.GroundKnownAt(fx, fz);
        bool tore = _paths.TearUp(fx, fz);
        _paths.GroundUnknown -= OnUnknown;

        GD.Print($"[delete] GROUND PROBE at ({fx},{fz}): queueLaid={laidQueue} both={isBoth} "
               + $"afterClear=Path:{nowPath} groundKept={kept} tearUp={tore} groundUnknown={unknown}");
        if (!isBoth)
            GD.Print("[delete] GROUND PROBE INCONCLUSIVE: could not make a Both cell there");
        else if (!nowPath)
            GD.Print("[delete] GROUND PROBE FAILED: ClearQueue did not leave the join cell as plain path");
        else if (!kept)
            GD.Print("[delete] GROUND PROBE FAILED: the demoted cell LOST its ground record (astraclaw's bug)");
        else if (!tore || unknown)
            GD.Print($"[delete] GROUND PROBE FAILED: tearUp={tore} groundUnknown={unknown} on the survivor");
        else
            GD.Print("[delete] GROUND PROBE OK: a demoted Both cell still tears back to its ground");
    }

    void RunDeleteTest(string arg)
    {
        ProbeGroundRecordInvariant();
        var bits = (arg ?? "").Split(',');
        if (bits.Length != 4 || !int.TryParse(bits[0], out int x0) || !int.TryParse(bits[1], out int z0)
            || !int.TryParse(bits[2], out int x1) || !int.TryParse(bits[3], out int z1))
        { GD.PrintErr("[delete] --delete-test wants x0,z0,x1,z1"); return; }

        OpenDeleteTool();
        if (_deleteTool == null) return;
        _deleteTool.Anchor = new ParkCell(x0, z0);
        _deleteTool.Cursor = new ParkCell(x1, z1);
        var (bx0, bz0, bx1, bz1) = DeleteBox();

        // ⚠⚠ THE CONTROL HAS TO ASK THE RIGHT QUESTION. "Did any queue cell vanish?" is the
        // WRONG one and it failed the first run for a correct delete: master's rule is that a
        // queue goes WHEN ITS RIDE DOES. So each queue cell is recorded with its OWNER, and what
        // must hold afterwards is that a queue cell whose ride still stands is still there.
        int pathsBefore = 0;
        var queueOwners = new List<(int X, int Z, int Owner)>();
        // ⚠⚠ THE `Both` CELLS ARE TRACKED SEPARATELY, for the invariant checked after the commit.
        // A Both cell is queue drawn onto park path: deleting its ride DEMOTES it to plain path
        // rather than tearing it, so it is still there afterwards and the player can still delete
        // it -- which means its original ground must still be on file. astraclaw found it was not
        // (ClearQueue removed the record), so tearing that survivor later left paving behind.
        // ⚠⚠ PARK-WIDE, NOT JUST THE BOX. ClearQueue sweeps the WHOLE grid for the deleted ride's
        // cells, so a Both cell well outside the marquee is demoted too -- and the first version of
        // this check only looked inside the box, found none, and honestly reported itself vacuous.
        // A check whose scope is narrower than the thing it checks proves nothing on most runs.
        var bothCells = new List<(int X, int Z)>();
        if (_paths != null)
            for (int z = 0; z < _park.Height; z++)
                for (int x = 0; x < _park.Width; x++)
                    if (_paths.KindAt(x, z) == PathTool.Kind.Both) bothCells.Add((x, z));
        for (int z = bz0; z <= bz1; z++)
            for (int x = bx0; x <= bx1; x++)
            {
                var k = _paths?.KindAt(x, z) ?? PathTool.Kind.None;
                if (k == PathTool.Kind.Path) pathsBefore++;
                else if (k is PathTool.Kind.Queue or PathTool.Kind.Both)
                    queueOwners.Add((x, z, _paths.OwnerAt(x, z)));
            }
        GD.Print($"[delete] {bothCells.Count} Both (queue-over-path) cells in the park before the delete"
               + (bothCells.Count > 0 ? ": " + string.Join(" ", bothCells.Take(8).Select(c => $"({c.X},{c.Z})")) : ""));
        int queueBefore = queueOwners.Count;
        // ⭐⭐ THE DIRECT TEST OF THE RULE, and it needs no geometry luck: ask TearUp to take each
        // queue cell and require it to REFUSE. That is the half the observed run cannot show --
        // there, a queue cell legitimately vanished because its ride went. A refusal probe is
        // harmless precisely because it is supposed to refuse.
        int refused = 0, wronglyTaken = 0;
        foreach (var (qx, qz, _) in queueOwners)
            if (_paths.TearUp(qx, qz)) wronglyTaken++; else refused++;
        if (queueOwners.Count > 0)
            GD.Print(wronglyTaken == 0
                ? $"[delete] RULE OK: TearUp refused all {refused} queue cells offered to it directly"
                : $"[delete] RULE FAILED: TearUp took {wronglyTaken} queue cells directly");

        GD.Print($"[delete] BEFORE box ({bx0},{bz0})..({bx1},{bz1}): {_park.Placed.Count} placed in park, "
               + $"{pathsBefore} plain path cells and {queueBefore} queue cells inside it");
        foreach (var p in _park.Placed) GD.Print($"[delete]   standing: {p.Name} id {p.Id} at ({p.X},{p.Y})");

        CommitDelete(bx0, bz0, bx1, bz1);

        int pathsAfter = 0, queueAfter = 0;
        for (int z = bz0; z <= bz1; z++)
            for (int x = bx0; x <= bx1; x++)
            {
                var k = _paths?.KindAt(x, z) ?? PathTool.Kind.None;
                if (k == PathTool.Kind.Path) pathsAfter++;
                else if (k is PathTool.Kind.Queue or PathTool.Kind.Both) queueAfter++;
            }
        GD.Print($"[delete] AFTER: {_park.Placed.Count} placed in park, {pathsAfter} plain path "
               + $"and {queueAfter} queue cells left in the box");

        // ⭐ THE GROUND-RECORD INVARIANT. Every `Both` cell that survived as plain path must still
        // know the ground it was laid over, or a later tear-up of it leaves paving on bare terrain.
        // ⚠ It names its own coverage: with no Both cells in the box the check is VACUOUS, and a
        // vacuous pass printed as a pass is how a regression gets waved through.
        int demoted = 0, groundLost = 0;
        foreach (var (bxc, bzc) in bothCells)
        {
            if ((_paths?.KindAt(bxc, bzc) ?? PathTool.Kind.None) != PathTool.Kind.Path) continue;
            demoted++;
            if (!_paths.GroundKnownAt(bxc, bzc)) { groundLost++; GD.Print($"[delete]   ({bxc},{bzc}) lost its ground record"); }
        }
        GD.Print(bothCells.Count == 0
            ? "[delete] GROUND RECORD: no Both cells in the park -- check VACUOUS, proves nothing"
            : demoted == 0
                ? $"[delete] GROUND RECORD: {bothCells.Count} Both cells and none demoted -- check VACUOUS"
                : groundLost == 0
                    ? $"[delete] GROUND RECORD OK: all {demoted} demoted Both cells kept their ground record"
                    : $"[delete] GROUND RECORD FAILED: {groundLost} of {demoted} demoted cells lost it");
        // ⭐ THE CONTROL: a queue cell whose RIDE STILL STANDS must still be there. One whose
        // ride went is supposed to have gone with it.
        int orphanedWrongly = 0, wentWithRide = 0, checkedOwned = 0;
        foreach (var (qx, qz, owner) in queueOwners)
        {
            bool rideStands = false;
            foreach (var pl in _park.Placed) if (pl.Id == owner) { rideStands = true; break; }
            var now = _paths.KindAt(qx, qz);
            bool stillQueue = now is PathTool.Kind.Queue or PathTool.Kind.Both;
            if (owner == 0) continue;                       // unowned: not this rule's business
            checkedOwned++;
            if (rideStands && !stillQueue) orphanedWrongly++;
            if (!rideStands && !stillQueue) wentWithRide++;
        }
        GD.Print(orphanedWrongly == 0
            ? $"[delete] CONTROL OK: of {checkedOwned} owned queue cells in the box, "
              + $"{wentWithRide} went with their deleted ride and none was taken from a standing one"
            : $"[delete] CONTROL FAILED: {orphanedWrongly} queue cells were taken from rides that still stand");
        if (checkedOwned == 0)
            GD.Print("[delete] ⚠ the queue control was VACUOUS -- no owned queue cell was in the box");
        foreach (var p in _park.Placed) GD.Print($"[delete]   left standing: {p.Name} id {p.Id} at ({p.X},{p.Y})");
        CloseDeleteTool(quiet: true);
    }

    /// <summary>⭐ Delete everything the box touches: placed things first, then loose path.</summary>
    void CommitDelete(int x0, int z0, int x1, int z1)
    {
        if (_park == null) return;

        // ⚠ IDS FIRST, THEN DELETE. Deleting renumbers `Park.Placed`, so collecting indices and
        // walking them would delete the wrong things from the second one on. Ids are stable; the
        // index is looked up again for each.
        var ids = new List<int>();
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                if (_park.PlacedAt(x, z) is { } hit && !ids.Contains(hit.Id))
                    ids.Add(hit.Id);

        int things = 0;
        foreach (int id in ids)
        {
            int at = -1;
            for (int i = 0; i < _park.Placed.Count; i++)
                if (_park.Placed[i].Id == id) { at = i; break; }
            // ⚠ It may already be gone: deleting a track ride takes its add-ons, and a big
            // marquee can name both.
            if (at >= 0 && DeletePlaced(at, quiet: true)) things++;
        }

        // ⭐ Paths after the things, because deleting a ride tears its queue up and repicks the
        // ground around it -- doing the loose path first would repick against cells that a ride
        // was about to free.
        var torn = new List<(int X, int Y)>();
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                if (_paths?.TearUp(x, z) == true) torn.Add((x, z));
        if (torn.Count > 0)
        {
            _paths.RepickAfterTear(torn);
        }
        // ⭐ ONE rebuild for the whole box, not one per thing: `RefreshFloor` is `Park.Rebuild`,
        // and a marquee over nine rides would otherwise rebuild the floor nine times.
        if (things > 0 || torn.Count > 0) RefreshFloor();

        GD.Print($"[delete] box ({x0},{z0})..({x1},{z1}): {things} placed, {torn.Count} path cells");
        Status(things == 0 && torn.Count == 0
            ? "nothing in that box to delete"
            : $"deleted {things} thing{(things == 1 ? "" : "s")}"
              + (torn.Count > 0 ? $" and {torn.Count} path cell{(torn.Count == 1 ? "" : "s")}" : ""));
    }
}
