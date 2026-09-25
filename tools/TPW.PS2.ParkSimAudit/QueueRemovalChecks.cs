using TPW.PS2.Data;

/// <summary>Deleting a ride takes its QUEUE and leaves everything else standing.
///
/// ⭐⭐ Master drew the line precisely: "deletes the ride including the queue. (but not exit
/// paths + combo entry/exits)". Those are three different `PathTool.Kind` values on the same
/// grid -- `Queue` goes, `Path` (an exit route people walk) stays, and `Both` (the single cell
/// where a queue meets a path, the combo entry/exit) stays. A sweep that took "everything the
/// ride owns" would cut the park's walkable network exactly where it joins, and nothing in a
/// build log would say so.</summary>
static class QueueRemovalChecks
{
    public static void Run(Model terrain, PathPieces pieces, string world, Action<bool, string> check)
    {
        void Check(bool ok, string m) => check(ok, "queue removal: " + m);

        var tool = new PathTool(terrain, pieces);
        if (!tool.Ready) { Check(false, $"path tool would not start -- {tool.Report}"); return; }

        // Find a run of layable cells to work on, rather than trusting a fixed coordinate.
        var f = terrain.Field;
        int sx = -1, sy = -1;
        for (int y = 2; y < f.Height - 6 && sx < 0; y++)
            for (int x = 2; x < f.Width - 6; x++)
            {
                bool room = true;
                for (int i = 0; i < 6 && room; i++) room = tool.CanLay(x + i, y) && tool.CanLay(x, y + i);
                if (room) { sx = x; sy = y; break; }
            }
        if (sx < 0) { Check(false, $"{world}: no clear 6x6 to test on"); return; }

        const int Mine = 7, Theirs = 9;
        tool.BeginLeg();
        // ours: a queue, and a path crossing it so one cell becomes Both
        // ⚠⚠ THE QUEUE IS LAID WITH ITS RUN BITS, as the game lays one. The first version of this
        // fixture passed `runBits` 0, so every cell of the queue was linked to nothing -- and the
        // join cell's bits read 0x00 with the queue still standing. A check that the join cell
        // stops wearing the queue's arm would have passed on a fixture where it never wore one:
        // vacuous, and it would have certified a fix that does nothing. The control below is what
        // caught that, by failing.
        //
        // ⭐ A queue is a LINE and carries the links it was drawn with (LinksFor: a Queue returns
        // `bits | _run[at]` and finds no neighbours of its own), so each cell must be told about
        // the ones either side of it.
        for (int i = 0; i < 4; i++)
        {
            int bits = 0;
            if (i > 0) bits |= PathTool.BitToward(sx + i, sy, sx + i - 1, sy);
            if (i < 3) bits |= PathTool.BitToward(sx + i, sy, sx + i + 1, sy);
            tool.Lay(sx + i, sy, PathTool.Kind.Queue, Mine, bits);
        }
        tool.Lay(sx + 2, sy, PathTool.Kind.Path, Mine);              // -> Both, this ride's join
        tool.Lay(sx, sy + 2, PathTool.Kind.Path);                    // an exit path
        for (int i = 0; i < 3; i++) tool.Lay(sx + i, sy + 4, PathTool.Kind.Queue, Theirs);

        int mineBefore = 0, bothBefore = 0;
        for (int i = 0; i < 4; i++) if (tool.KindAt(sx + i, sy) == PathTool.Kind.Queue) mineBefore++;
        if (tool.KindAt(sx + 2, sy) == PathTool.Kind.Both) bothBefore++;
        Check(mineBefore >= 2, $"{world}: laid a queue to remove ({mineBefore} cells)");
        Check(bothBefore == 1, $"{world}: a queue crossed by a path makes one Both cell");

        // ⚠ THE CONTROL for the arm check: while the queue stands, the join cell MUST wear it.
        // Without this the check above would pass on a tool that never draws arms at all.
        int armBefore = PathTool.BitToward(sx + 2, sy, sx + 1, sy);
        Check((tool.LinkBits(sx + 2, sy) & armBefore) != 0,
              $"{world}: control -- with the queue standing the join cell DOES link down it "
            + $"(bits 0x{tool.LinkBits(sx + 2, sy):X2})");

        int gone = tool.ClearQueue(Mine);
        // ⚠ The count is cells that WENT. The join cell is demoted, not removed, so it is not one.
        Check(gone == mineBefore, $"{world}: removed {gone} of {mineBefore} of its own queue cells");

        // ⚠ THE EXCLUSIONS, which are the whole point of the check.
        // ⭐⭐ THE JOIN CELL SURVIVES AS **PATH**, NOT AS `Both`. Master's rule is that deleting a
        // ride must not tear up the combo entry/exit -- the park's walkable network joins there --
        // and it does not: the cell is still laid and still walkable. But this check used to
        // assert it stayed `Both`, and `Both` is what kept it wearing the queue's arm: `LinksFor`
        // ors `_run` in for a Both cell, so the tile went on drawing a limb down a queue that had
        // been deleted. Master: "the queues of deleted rides are refreshing sprites of
        // once-connected path tiles."
        //
        // ⚠ So the check now asserts what the rule actually means -- the cell is still there and
        // still walkable -- and NOT the implementation detail that broke it.
        Check(tool.KindAt(sx + 2, sy) == PathTool.Kind.Path,
              $"{world}: the combo entry/exit cell SURVIVES, as plain path now that the queue it "
            + $"joined is gone (is {tool.KindAt(sx + 2, sy)})");
        // ⭐⭐ AND THE CHECK THAT REJECTS THE SPRITE BUG: no arm may point down the dead queue.
        // The queue ran west from the join cell, so bit-toward (sx+1, sy) must be clear.
        int arm = PathTool.BitToward(sx + 2, sy, sx + 1, sy);
        Check((tool.LinkBits(sx + 2, sy) & arm) == 0,
              $"{world}: and it no longer links back down the deleted queue "
            + $"(bits 0x{tool.LinkBits(sx + 2, sy):X2}, dead arm 0x{arm:X2})");
        Check(tool.KindAt(sx, sy + 2) == PathTool.Kind.Path,
              $"{world}: the exit path SURVIVES (is {tool.KindAt(sx, sy + 2)})");
        for (int i = 0; i < 3; i++)
            Check(tool.KindAt(sx + i, sy + 4) == PathTool.Kind.Queue,
                  $"{world}: another ride's queue SURVIVES at +{i}");
        Check(tool.OwnerAt(sx, sy) == 0, $"{world}: a removed cell forgets its owner");

        // ⭐ And removing a ride that owns nothing must not touch the grid.
        int none = tool.ClearQueue(4242);
        Check(none == 0, $"{world}: removing a ride with no queue takes nothing ({none})");
    }
}
