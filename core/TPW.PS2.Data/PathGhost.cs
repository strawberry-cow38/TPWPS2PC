namespace TPW.PS2.Data;

/// <summary>The build ghost: the run of tiles between where a path run started and where the
/// cursor is, each carrying the verdict the game would give it.
///
/// ⭐⭐ A SEGMENT IS STRAIGHT. The cursor does not drag out a shape -- it is SNAPPED to whichever
/// axis it has moved further along, and the segment runs along that one axis only. A press lays
/// that segment and the run carries on from its end, so a corner is two presses, not one drag.
/// Master, who plays the game, called this; the PSX report agrees independently, where the path
/// tool "lays from the last corner to the cursor (axis-snapped)" and counts the run in CORNERS.
///
/// ⚠ The PS2 walker at FUN_001279C8 does walk an L -- longer axis, then the other, then the
/// corner tile. That is read and it stands; what is NOT established is that the path tool ever
/// hands it an off-axis end. BOTH ARE HERE, on <see cref="Shape"/>: straight is the default
/// because it is what master says the game allows, and the elbow is kept because it is what the
/// walker in the executable demonstrably does, and deleting it would throw away a reading to
/// match a recollection.
///
/// ⭐⭐ A REFUSAL LATCHES. Once a tile refuses, every later tile of the run is drawn refused too,
/// whatever it would have said on its own, and the press lays nothing. That is the game's rule and
/// it is what makes the ghost readable: the run is good up to the first thing in the way, and then
/// it is all bad, rather than speckled.
///
/// See findings/ghost-cursor.md.</summary>
public sealed class PathGhost
{
    /// <summary>How a segment reaches the cursor. ⭐ STRAIGHT is one axis only, so a corner costs
    /// two presses; ELBOW covers both axes in one, the way the executable's walker does.</summary>
    public enum Segment { Straight, Elbow }

    /// <summary>Which shape a segment takes. Straight by default.</summary>
    public Segment Shape { get; set; } = Segment.Straight;

    /// <summary>The verdict codes the game's validator returns (FUN_001E81E0). They index the
    /// marker table at 0x35B5E0, which is why they are these numbers and not 0,1,2,3.</summary>
    public enum Verdict { Lay = 0, Refused = 1, Joins = 5, Already = 6 }

    public readonly record struct Cell(int X, int Y, Verdict Verdict);

    /// <summary>Where the segment actually ends, once the cursor has been snapped to one axis.
    /// ⚠ NOT the point passed in: a run must carry on from here, or the next segment starts off
    /// the end of the one just laid.</summary>
    public (int X, int Y) End { get; private set; }

    /// <summary>The tiles of the run, start first, with the latch already applied.</summary>
    public IReadOnlyList<Cell> Tiles => _tiles;
    readonly List<Cell> _tiles = new();

    /// <summary>Whether the whole run may be laid. ⚠ The game gates the press on this, not on the
    /// tile under the cursor: one bad tile anywhere refuses the lot.</summary>
    public bool Layable { get; private set; }

    readonly PathTool _tool;

    /// <summary>Whether something is STANDING on a cell. ⚠ The path tool knows the terrain's own
    /// no-build bit but nothing about what the player has put down since, so the park has to say.
    /// Without it a run was laid straight across a ride.</summary>
    public Func<int, int, bool> Occupied { get; set; }

    public PathGhost(PathTool tool) { _tool = tool; }

    /// <summary>Work out the run from (x0,y0) to (x1,y1).</summary>
    public void Set(int x0, int y0, int x1, int y1, PathTool.Kind kind)
    {
        _tiles.Clear();
        Layable = false;
        if (_tool == null || !_tool.Ready) return;

        int dx = x1 - x0, dy = y1 - y0;
        // ⭐ SNAP to the axis the cursor has moved further along, and keep the other one. The
        // segment's end is then not the cursor: it is the cursor projected onto that axis, which
        // is why End is published rather than the caller reusing the point it passed in.
        if (Shape == Segment.Straight) { if (Math.Abs(dx) >= Math.Abs(dy)) y1 = y0; else x1 = x0; }
        End = (x1, y1);

        var run = new List<(int X, int Y)>();
        if (Shape == Segment.Straight || dx == 0 || dy == 0)
        {
            if (x1 != x0)
                for (int x = x0; x != x1; x += x1 < x0 ? -1 : 1) run.Add((x, y0));
            else
                for (int y = y0; y != y1; y += y1 < y0 ? -1 : 1) run.Add((x0, y));
        }
        // ⭐ The elbow: the LONGER axis first, then the other, exactly as the walker picks them.
        else if (Math.Abs(dy) < Math.Abs(dx))
        {
            for (int x = x0; x != x1; x += dx < 0 ? -1 : 1) run.Add((x, y0));
            for (int y = y0; y != y1; y += dy < 0 ? -1 : 1) run.Add((x1, y));
        }
        else
        {
            for (int y = y0; y != y1; y += dy < 0 ? -1 : 1) run.Add((x0, y));
            for (int x = x0; x != x1; x += dx < 0 ? -1 : 1) run.Add((x, y1));
        }
        run.Add((x1, y1));   // ⚠ the end tile is judged last: only it may wear a connect symbol

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
        // ⚠ THE PARK'S OWN WALKWAY IS NOT BUILDABLE AND IS STILL PATH. CanLay refuses it -- it
        // stands on cells the terrain draws no ground on -- but a run that reaches it has reached
        // the way in, and must say so rather than go red at the one tile the player is aiming for.
        if (!_tool.CanLay(x, y) && !_tool.IsWalkway(x, y)) return Verdict.Refused;
        // ⭐ A RIDE'S TILES ARE NOT GROUND TO BUILD ON. Master: paths "should be invalid on tiles
        // occupied by rides, they shouldn't delete the rides".
        if (Occupied?.Invoke(x, y) == true) return Verdict.Refused;
        var had = _tool.KindAt(x, y);
        if (had == PathTool.Kind.None) return Verdict.Lay;
        // ⭐⭐ ONLY THE RUN'S LAST TILE SAYS "already". The console gates that arm on its
        // last-tile flag (`DAT_002B839C`, set once in the walker just before the corner tile is
        // judged): every earlier tile that is already this kind falls through to the ordinary
        // answer instead. So a run drawn along path you have already laid shows ONE connect
        // symbol, at its end, not a string of them -- which is what master saw and what the
        // validator says, and I had it wrong in both places.
        if (kind == PathTool.Kind.Path)
        {
            // ⭐⭐ A CELL THAT IS BOTH IS PATH GROUND. It is the tile a queue run finished on, and
            // it is drawn from the path table -- so a path run walking over it is walking over its
            // own path. Treating it as a foreign kind marked the one tile where a queue meets the
            // network as no-build, and because a refusal latches, every run drawn THROUGH that
            // junction went red from there on. Master: "the tile where a queue connects to a path
            // is still marked no-build".
            if (had is PathTool.Kind.Path or PathTool.Kind.Both) return last ? Verdict.Already : Verdict.Lay;
            // ⚠⚠ A PATH MAY NOT BE LAID ON A QUEUE. Master's rule, and it replaces the overlap tile
            // the PSX uses: the two meet by standing NEXT to each other, and the path tile beside
            // the queue's TIP wears a piece with an arm pointing at it.
            return Verdict.Refused;
        }
        if (had == PathTool.Kind.Queue) return last ? Verdict.Already : Verdict.Lay;
        // ⭐ A queue reaching a path may only join on the run's LAST tile. That tile becomes the
        // one that is both, and it is the only join between a queue and a path network -- so the
        // ghost marks it before the press rather than leaving it to be discovered.
        return last ? Verdict.Joins : Verdict.Refused;
    }

    /// <summary>Lay the whole run. Returns how many cells changed, or 0 when it was refused.
    ///
    /// ⭐⭐ THE RUN'S OWN ORDER IS THE QUEUE'S WIRING. Each cell is handed the bits pointing at the
    /// cell before it and the cell after it, and those are the only links a queue tile ever gets.
    /// Master: "the queue is EXACTLY as its drawn with the tool". Deriving them from neighbours --
    /// which is right for a path network and wrong for a line -- is what made a queue folded back
    /// on itself read as a slab and two rides' queues fuse where they ran side by side.</summary>
    public int Lay(PathTool.Kind kind, int owner = 0)
    {
        if (!Layable) return 0;
        int n = 0;
        for (int i = 0; i < _tiles.Count; i++)
        {
            var t = _tiles[i];
            int bits = 0;
            if (i > 0) bits |= PathTool.BitToward(t.X, t.Y, _tiles[i - 1].X, _tiles[i - 1].Y);
            if (i < _tiles.Count - 1) bits |= PathTool.BitToward(t.X, t.Y, _tiles[i + 1].X, _tiles[i + 1].Y);
            if (_tool.Lay(t.X, t.Y, kind, owner, bits)) n++;
        }
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
