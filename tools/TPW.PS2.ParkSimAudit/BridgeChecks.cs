using TPW.PS2.Data;

/// <summary>The park's own bridge. Master: "theres a bridge on the jungle 1 map. it should count
/// as path tiles, be unbuildable, and have paths connect to it." -- three asks, three groups.</summary>
static class BridgeChecks
{
    public static void Run(Model terrain, PathPieces pieces, string world, Action<bool, string> check)
    {
        void Check(bool ok, string m) => check(ok, "bridge: " + m);

        var grid = new ParkPaths(terrain);
        var deck = grid.Cells.Where(grid.IsBridge).ToList();

        // ⚠⚠ THE CONTROL THAT OUTRANKS THE REST. Classify feeds the sprite tables and PathTool
        // refuses to start unless they come to exactly sixteen path and four queue. If the bridge
        // had been routed through Classify this would be 17/4 and the path tool would be DEAD in
        // every park -- a far worse bug than the one being fixed, and silent.
        var tool = new PathTool(terrain, pieces);
        Check(tool.Ready, $"the path tool still starts -- {tool.Report}");

        string park = $"{world} {Path.GetFileNameWithoutExtension(AuditPark.Mps)}";
        if (park != "JUNGLE terrain_1")
        {
            // ⭐ Searched all eight parks: JUNGLE park 1 owns the only bridge on the disc. Asserted
            // per PARK (not per world: JUNGLE park 2 has none) so a park quietly growing one fails.
            Check(deck.Count == 0, $"{park} has no bridge ({deck.Count} cells)");
            return;
        }

        // ---- it exists, and where ---------------------------------------------------------
        Check(deck.Count == 4, $"JUNGLE park 1 has a four-cell bridge ({deck.Count})");
        if (deck.Count == 0) return;
        Check(deck.All(c => c.Z == deck[0].Z) && deck.Max(c => c.X) - deck.Min(c => c.X) == 3,
              $"and it is a four-wide span on one row (x {deck.Min(c => c.X)}..{deck.Max(c => c.X)}, z {deck[0].Z})");

        // ---- 1. it counts as path ---------------------------------------------------------
        Check(deck.All(c => grid.Kind(c) == ParkPathKind.Path), "every bridge cell reads as PATH");
        Check(deck.All(grid.Walkable), "and a guest may stand on it");
        Check(deck.All(c => tool.KindAt(c.X, c.Z) == PathTool.Kind.Path), "the path tool agrees it is path");

        // ---- 2. it is unbuildable ---------------------------------------------------------
        Check(deck.All(c => !grid.CanBuild(c)), "nothing may be built on it");
        Check(deck.All(c => !tool.CanLay(c.X, c.Z)), "and no path may be laid over it");
        // ⚠ THE POINT OF THAT: the terrain's own no-build bit says these cells ARE buildable, so
        // the refusal has to come from the bridge. A check that passed because the ground already
        // refused would be testing nothing.
        Check(deck.All(c => terrain.Field.Buildable(c.X, c.Z)),
              "CONTROL: the terrain itself calls them buildable -- the refusal is the bridge's");

        // ---- 3. paths connect to it -------------------------------------------------------
        var end = deck.OrderBy(c => c.X).First();
        var beside = new ParkCell(end.X - 1, end.Z);
        if (tool.CanLay(beside.X, beside.Z))
        {
            tool.Lay(beside.X, beside.Z);
            int bits = tool.LinkBits(beside.X, beside.Z);
            int toward = PathTool.BitToward(beside.X, beside.Z, end.X, end.Z);
            Check((bits & toward) != 0,
                  $"a path laid beside it links TOWARD it (bits 0x{bits:X2}, wanted 0x{toward:X2})");
            // ⭐ And the control: the same cell must NOT claim a link into bare ground on its
            // other side, or "it links to everything" would pass this just as well.
            int away = PathTool.BitToward(beside.X, beside.Z, beside.X - 1, beside.Z);
            Check((bits & away) == 0, "CONTROL: and not into the bare ground behind it");
        }
        else Check(false, $"the fixture needs a layable cell beside the bridge at ({beside.X},{beside.Z})");
    }
}
