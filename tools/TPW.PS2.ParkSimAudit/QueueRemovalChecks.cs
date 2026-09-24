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
        for (int i = 0; i < 4; i++) tool.Lay(sx + i, sy, PathTool.Kind.Queue, Mine);
        tool.Lay(sx + 2, sy, PathTool.Kind.Path);                    // -> Both
        tool.Lay(sx, sy + 2, PathTool.Kind.Path);                    // an exit path
        for (int i = 0; i < 3; i++) tool.Lay(sx + i, sy + 4, PathTool.Kind.Queue, Theirs);

        int mineBefore = 0, bothBefore = 0;
        for (int i = 0; i < 4; i++) if (tool.KindAt(sx + i, sy) == PathTool.Kind.Queue) mineBefore++;
        if (tool.KindAt(sx + 2, sy) == PathTool.Kind.Both) bothBefore++;
        Check(mineBefore >= 2, $"{world}: laid a queue to remove ({mineBefore} cells)");
        Check(bothBefore == 1, $"{world}: a queue crossed by a path makes one Both cell");

        int gone = tool.ClearQueue(Mine);
        Check(gone == mineBefore, $"{world}: removed {gone} of {mineBefore} of its own queue cells");

        // ⚠ THE EXCLUSIONS, which are the whole point of the check.
        Check(tool.KindAt(sx + 2, sy) == PathTool.Kind.Both,
              $"{world}: the combo entry/exit cell SURVIVES (is {tool.KindAt(sx + 2, sy)})");
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
