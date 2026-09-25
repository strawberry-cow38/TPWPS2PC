using TPW.PS2.Data;

/// <summary>Guests walk a queue as it was drawn.
///
/// ⭐⭐ strawberry, 2026-09-25: guests "dont use the full queue, just short-cutting from a path tile
/// next to the queue tile of the entrance". The walker's old rule let a queue tile be only a
/// DESTINATION, entered from any open neighbour, so a stub with a path beside it was joined from
/// that path and the line was never walked. <see cref="GuestWalk.QueueStep"/> now takes the
/// queue's run links from <see cref="PathTool.QueueStep"/>.
///
/// The fixture is laid with the real tool on this park's own ground, then read back through a
/// fresh <see cref="ParkPaths"/> exactly as the viewer's walk grid reads the laid sprites:
/// <code>
///   row y:    B P P P P P P P     a path row; B is where the queue was drawn onto it (Both)
///   row y+1:  Q . S               S = the stub, the ride end, directly beside the path above it
///   row y+2:  Q Q Q
/// </code>
/// ⚠ The old rule is run on the SAME fixture as the control: it must take the shortcut, or this
/// fixture does not reproduce the report and every check below would pass for free.</summary>
static class QueueWalkChecks
{
    public static void Run(Model terrain, PathPieces pieces, string world, Action<bool, string> check)
    {
        void Check(bool ok, string m) => check(ok, "queue walk: " + m);
        var saved = (byte[])terrain.Field.Cells.Clone();
        try
        {
            var tool = new PathTool(terrain, pieces);
            if (!tool.Ready) { Check(false, $"{world}: path tool would not start -- {tool.Report}"); return; }
            var f = terrain.Field;
            int sx = -1, sy = -1;
            for (int y = 2; y < f.Height - 8 && sx < 0; y++)
                for (int x = 2; x < f.Width - 10; x++)
                {
                    bool room = true;
                    for (int dy = 0; dy < 4 && room; dy++)
                        for (int dx = 0; dx < 9 && room; dx++) room = tool.CanLay(x + dx, y + dy);
                    if (room) { sx = x; sy = y; break; }
                }
            if (sx < 0) { Check(false, $"{world}: no clear 9x4 to lay the fixture on"); return; }

            tool.BeginLeg();
            for (int i = 0; i < 8; i++) tool.Lay(sx + i, sy, PathTool.Kind.Path);
            var run = new List<(int X, int Y)> { (sx, sy), (sx, sy + 1), (sx, sy + 2), (sx + 1, sy + 2), (sx + 2, sy + 2), (sx + 2, sy + 1) };
            tool.BeginLeg();
            for (int i = 0; i < run.Count; i++)
            {
                int bits = 0;
                if (i > 0) bits |= PathTool.BitToward(run[i].X, run[i].Y, run[i - 1].X, run[i - 1].Y);
                if (i < run.Count - 1) bits |= PathTool.BitToward(run[i].X, run[i].Y, run[i + 1].X, run[i + 1].Y);
                tool.Lay(run[i].X, run[i].Y, PathTool.Kind.Queue, 7, bits);
            }
            var paths = new ParkPaths(terrain);
            ParkCell C((int X, int Y) c) => new(c.X, c.Y);
            var mouth = C(run[0]); var stub = C(run[^1]); var beside = new ParkCell(sx + 2, sy);
            var far = new ParkCell(sx + 7, sy); var middle = C(run[4]);
            Check(tool.KindAt(mouth.X, mouth.Z) == PathTool.Kind.Both && paths.Open(mouth)
                  && run.Skip(1).All(c => paths.Kind(C(c)) == ParkPathKind.Queue) && paths.Open(beside),
                  $"{world}: fixture reads back as a path row, a Both mouth and five queue cells, with the stub beside the path");

            var walk = new GuestWalk(paths);
            var legacy = walk.Route(far, stub);
            var legacyMiddle = walk.Route(far, middle);
            Check(legacy != null && legacy.Count == 7 && legacy[^2] == beside,
                  $"{world}: control: the old rule takes the shortcut from the path tile beside the stub ({legacy?.Count} cells)");
            Check(legacyMiddle == null, $"{world}: control: the old rule cannot reach a queue cell with no path beside it");

            walk.QueueStep = (a, b) => tool.QueueStep(a.X, a.Z, b.X, b.Z);
            var expected = Enumerable.Range(0, 8).Select(i => new ParkCell(sx + 7 - i, sy)).Concat(run.Skip(1).Select(C)).ToList();
            var drawn = walk.Route(far, stub);
            Check(drawn != null && drawn.SequenceEqual(expected),
                  $"{world}: a guest joins at the mouth and walks the whole line to the stub ({drawn?.Count} cells, want {expected.Count})");
            var back = walk.Route(stub, far);
            Check(back != null && back.SequenceEqual(Enumerable.Reverse(expected)),
                  $"{world}: leaving from the stub walks back out along the line, not onto the path beside it");
            Check(walk.Route(far, middle) is { } mid && mid.Contains(mouth) && !mid.Contains(stub),
                  $"{world}: a queue cell with no path beside it is reached along the line");
            Check(!tool.QueueStep(beside.X, beside.Z, stub.X, stub.Z) && tool.QueueStep(mouth.X, mouth.Z, run[1].X, run[1].Y),
                  $"{world}: the tool links the mouth into the line and does not link the path beside the stub");
        }
        finally { saved.CopyTo(terrain.Field.Cells, 0); }
    }
}
