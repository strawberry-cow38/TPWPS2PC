namespace TPW.PS2.Data;

/// <summary>The build ghost: the run of tiles between where a path run started and where the
/// cursor is, each carrying the verdict the game would give it.
///
/// ⭐ The run is L-SHAPED AND AXIS-SNAPPED, not a line and not free-form: the game walks the
/// LONGER axis first and draws the corner tile last (FUN_001279C8). A diagonal drag therefore
/// lays two straight legs.
///
/// ⭐⭐ A REFUSAL LATCHES. Once a tile refuses, every later tile of the run is drawn refused too,
/// whatever it would have said on its own, and the press lays nothing. That is the game's rule and
/// it is what makes the ghost readable: the run is good up to the first thing in the way, and then
/// it is all bad, rather than speckled.
///
/// See findings/ghost-cursor.md.</summary>
public sealed class PathGhost
{
    /// <summary>The verdict codes the game's validator returns (FUN_001E81E0). They index the
    /// marker table at 0x35B5E0, which is why they are these numbers and not 0,1,2,3.</summary>
    public enum Verdict { Lay = 0, Refused = 1, Joins = 5, Already = 6 }

    public readonly record struct Cell(int X, int Y, Verdict Verdict);

    /// <summary>The tiles of the run, start first, with the latch already applied.</summary>
    public IReadOnlyList<Cell> Tiles => _tiles;
    readonly List<Cell> _tiles = new();

    /// <summary>Whether the whole run may be laid. ⚠ The game gates the press on this, not on the
    /// tile under the cursor: one bad tile anywhere refuses the lot.</summary>
    public bool Layable { get; private set; }

    readonly PathTool _tool;
    public PathGhost(PathTool tool) { _tool = tool; }

    /// <summary>Work out the run from (x0,y0) to (x1,y1).</summary>
    public void Set(int x0, int y0, int x1, int y1, PathTool.Kind kind)
    {
        _tiles.Clear();
        Layable = false;
        if (_tool == null || !_tool.Ready) return;

        // ⭐ The longer axis first, exactly as the walker picks it.
        int dx = x1 - x0, dy = y1 - y0;
        var run = new List<(int X, int Y)>();
        if (Math.Abs(dy) < Math.Abs(dx))
        {
            int step = dx < 0 ? -1 : 1;
            for (int x = x0; x != x1; x += step) run.Add((x, y0));
            for (int y = y0; y != y1; y += dy < 0 ? -1 : 1) run.Add((x1, y));
        }
        else
        {
            int step = dy < 0 ? -1 : 1;
            for (int y = y0; y != y1; y += step) run.Add((x0, y));
            for (int x = x0; x != x1; x += dx < 0 ? -1 : 1) run.Add((x, y1));
        }
        run.Add((x1, y1));   // ⚠ the corner -- the last tile is drawn on its own, after both legs

        bool refused = false;
        for (int i = 0; i < run.Count; i++)
        {
            var (x, y) = run[i];
            var v = Judge(x, y, kind, last: i == run.Count - 1);
            if (refused) v = Verdict.Refused;
            else if (v == Verdict.Refused) refused = true;
            _tiles.Add(new Cell(x, y, v));
        }
        Layable = !refused && _tiles.Count > 0;
    }

    /// <summary>What the game's validator would say about one tile.
    ///
    /// ⚠ NOT ALL OF IT. The console also refuses on MONEY (`cash - price*10 &lt; 1`), on an object
    /// standing on the tile, and on a ride's own entrance and exit tiles. There is no money and no
    /// ride placement in the viewer yet, so those arms are absent rather than wrong -- a tile this
    /// says Lay to might still be refused by the game for a reason not modelled here.</summary>
    Verdict Judge(int x, int y, PathTool.Kind kind, bool last)
    {
        if (!_tool.CanLay(x, y)) return Verdict.Refused;
        var had = _tool.KindAt(x, y);
        if (had == PathTool.Kind.None) return Verdict.Lay;
        if (had == kind) return Verdict.Already;
        // ⭐ A queue reaching a path may only join on the run's LAST tile. That tile becomes the
        // one that is both, and it is the only join between a queue and a path network -- so the
        // ghost marks it before the press rather than leaving it to be discovered.
        if (kind == PathTool.Kind.Queue && had is PathTool.Kind.Path or PathTool.Kind.Both)
            return last ? Verdict.Joins : Verdict.Refused;
        if (kind is PathTool.Kind.Path or PathTool.Kind.Both && had == PathTool.Kind.Queue)
            return Verdict.Joins;
        return Verdict.Refused;
    }

    /// <summary>Lay the whole run. Returns how many cells changed, or 0 when it was refused.</summary>
    public int Lay(PathTool.Kind kind)
    {
        if (!Layable) return 0;
        int n = 0;
        foreach (var t in _tiles) if (_tool.Lay(t.X, t.Y, kind)) n++;
        return n;
    }

    /// <summary>The marker each verdict wears, by the console's own table at 0x35B5E0:
    /// 165 blue, 175 red, 173 the link rings.</summary>
    public static int Marker(Verdict v) => v switch
    {
        Verdict.Lay => 165,
        Verdict.Joins or Verdict.Already => 173,
        _ => 175,
    };
}
