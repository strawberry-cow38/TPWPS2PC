using TPW.PS2.Data;

/// <summary>What a run of path or queue costs, and where the money stops it.
///
/// ⭐ EVERY FIGURE HERE IS THE CONSOLE'S -- see <see cref="PathPrices"/> for the instructions.
/// These checks exist to REJECT the two ways this could be wrong: a price that is not the one the
/// boot code sets, and a gate whose boundary is off by one tile or one tenth.</summary>
static class PathPriceChecks
{
    public static void Run(Model terrain, PathPieces pieces, Action<bool, string> Check)
    {
        // ---- the constants, straight off 0x8001B580 --------------------------------------
        Check(PathPrices.PathPounds == 10, "a path tile costs 10, as 0x8001B584 passes to the setter");
        Check(PathPrices.QueuePounds == 25, "a queue tile costs 25, as 0x8001B58C passes to the setter");
        Check(PathPrices.Pounds(PathTool.Kind.Path) == 10, "kind 2 takes the path price (0x8004F3A8)");
        Check(PathPrices.Pounds(PathTool.Kind.Both) == 10, "kind 13 takes the PATH price, not the queue's (0x8004F3B0)");
        Check(PathPrices.Pounds(PathTool.Kind.Queue) == 25, "kind 4 takes the queue price (0x8004F3C8)");
        Check(PathPrices.Pounds(PathTool.Kind.None) == 0, "no other kind is priced -- s4 keeps the zero of 0x8004F368");

        // ⭐ A CONTROL FOR THE x10. `Tenths` must not be the identity, or every money figure in
        // this file would agree with a pounds figure by accident.
        Check(PathPrices.Tenths(1) == 10, "0x8005002C turns 1 into 10 -- ((n<<2)+n)<<1");
        Check(PathPrices.Tenths(10) == 100 && PathPrices.Tenths(25) == 250, "a path tile is 100 tenths and a queue tile 250");

        // ---- which tiles are charged for (0x8004F484..0x8004F4AC) -------------------------
        Check(PathPrices.Charges(PathTool.Kind.None, PathTool.Kind.Path), "bare ground is charged for");
        Check(!PathPrices.Charges(PathTool.Kind.Path, PathTool.Kind.Path), "path over path is free (beq v1,s3)");
        Check(!PathPrices.Charges(PathTool.Kind.Queue, PathTool.Kind.Queue), "queue over queue is free (beq v1,s3)");
        Check(!PathPrices.Charges(PathTool.Kind.Both, PathTool.Kind.Path), "path over a junction is free (v1==13 && s3==2)");
        Check(PathPrices.Charges(PathTool.Kind.Both, PathTool.Kind.Queue), "queue over a junction IS charged -- the 13 arm only exempts path");
        Check(PathPrices.Charges(PathTool.Kind.Path, PathTool.Kind.Queue), "queue over path is charged -- the cell changes");

        // ---- the boundary (0x80050014 returns *a <= *b, and non-zero refuses) -------------
        Check(!PathPrices.CanAfford(PathPrices.Tenths(10), 10), "a till holding EXACTLY the price refuses -- the test is <= 0, not < 0");
        Check(PathPrices.CanAfford(PathPrices.Tenths(10) + 1, 10), "one tenth more and it lays");
        Check(!PathPrices.CanAfford(0, 0), "an empty till refuses even a free run -- balance - 0 <= 0");

        // ---- a run, priced as it is drawn -------------------------------------------------
        var tool = new PathTool(terrain, pieces);
        if (!tool.Ready) { Check(false, "the price fixture needs a terrain whose path tiles were found"); return; }
        var f = terrain.Field;
        (int X, int Y) start = (-1, -1);
        for (int y = 0; y < f.Height && start.X < 0; y++)
            for (int x = 0; x + 5 < f.Width && start.X < 0; x++)
                if (Enumerable.Range(0, 6).All(i => tool.CanLay(x + i, y))) start = (x, y);
        Check(start.X >= 0, "the fixture found a straight run of six buildable cells to price");
        if (start.X < 0) return;

        var ghost = new PathGhost(tool);
        ghost.Set(start.X, start.Y, start.X + 5, start.Y, PathTool.Kind.Path);
        Check(ghost.Tiles.Count == 6 && ghost.Layable, "six bare cells draw a layable run");
        Check(ghost.RunPounds == 6 * PathPrices.PathPounds, $"six path tiles cost {6 * PathPrices.PathPounds}, not {ghost.RunPounds}");

        // ⭐⭐ THE INVARIANT, NOT A COPY OF THE SUM: the run charges for exactly the cells that
        // change, so what `Lay` reports MUST be what was priced. A pricing loop that drifted from
        // the laying loop passes the figure above and fails here.
        int priced = ghost.RunPounds / PathPrices.PathPounds;
        int laid = ghost.Lay(PathTool.Kind.Path);
        Check(priced == laid, $"the run priced {priced} tiles and laid {laid} -- they must be the same cells");

        // ⭐ Drawn back over itself, the same ground is free (the beq v1,s3 arm, through the real tool).
        var again = new PathGhost(tool);
        again.Set(start.X, start.Y, start.X + 5, start.Y, PathTool.Kind.Path);
        Check(again.RunPounds == 0, $"a run redrawn along path already laid costs nothing, not {again.RunPounds}");

        // ---- the money gate ---------------------------------------------------------------
        // A fresh tool on fresh ground, so the six cells are all chargeable again.
        var t2 = new PathTool(terrain, pieces);
        var broke = new PathGhost(t2) { Balance = () => PathPrices.Tenths(30) };   // three tiles' worth and no more
        broke.Set(start.X, start.Y, start.X + 5, start.Y, PathTool.Kind.Path);
        Check(!broke.Layable, "a run the till cannot cover is refused");
        int red = broke.Tiles.Count(t => t.Verdict == PathGhost.Verdict.Refused);
        // 30 pounds buys tiles while balance - total*10 > 0, with the total EXCLUDING the tile
        // being judged: totals 0,10,20 pass and 30 does not, so three lay and three go red.
        Check(red == 3, $"the ghost goes red on the fourth tile, not the {broke.Tiles.Count - red + 1}th");
        Check(broke.RunPounds == 30, $"only the affordable tiles were priced ({broke.RunPounds})");

        // ⭐⭐ THE CONTROL THE GATE MUST NOT SWALLOW: with no till wired the gate is absent, so
        // the same run on the same empty tool lays. Without this a gate that refused everything
        // would pass every check above.
        var free = new PathGhost(new PathTool(terrain, pieces));
        free.Set(start.X, start.Y, start.X + 5, start.Y, PathTool.Kind.Path);
        Check(free.Layable && free.RunPounds == 60, "with no till wired there is no gate at all, and the run still prices");

        // ⭐ And a park that spends freely is not gated either -- Unlimited is the console's park[8].
        var rich = new PathGhost(new PathTool(terrain, pieces)) { Balance = () => int.MaxValue };
        rich.Set(start.X, start.Y, start.X + 5, start.Y, PathTool.Kind.Path);
        Check(rich.Layable, "a till that can cover it lays the whole run");

        // ---- and the queue costs more than the path, through the same wiring ---------------
        var q = new PathGhost(new PathTool(terrain, pieces));
        q.Set(start.X, start.Y, start.X + 5, start.Y, PathTool.Kind.Queue);
        Check(q.RunPounds == 6 * PathPrices.QueuePounds, $"six queue tiles cost {6 * PathPrices.QueuePounds}, not {q.RunPounds}");
        Check(q.RunPounds > ghost.RunPounds, "a queue tile costs more than a path tile");
    }
}
