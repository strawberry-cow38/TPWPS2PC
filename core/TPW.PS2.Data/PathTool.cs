namespace TPW.PS2.Data;

/// <summary>Laying park paths and queues the way the game does.
///
/// ⭐⭐ A PATH IS A GROUND TILE, NOT A STRUCTURE. The cell's second authored byte indexes the
/// terrain model's own material table, and the path art is the `jpa_*` block at the end of it --
/// sixteen path tiles then four queue tiles, the same twenty in every world. So laying a path is
/// writing one byte, and the plot draws it like any other ground.
///
/// ⭐⭐ WHICH tile, and which way round, is DERIVED from the neighbours by the game's own table
/// (<see cref="PathPieces"/>): a cell joins the path cells around it, the eight link bits pick a
/// record, and the record names the tile and the quarter turns. So a cell's art is not its own --
/// every neighbour of a change has to be re-picked too, which is why attaching to a straight run
/// turns the tile you attached to into a T.
///
/// The link rule is the game's (PSX 0x8004E20C, whose PS2 twin uses the same tables): an
/// orthogonal neighbour of the same kind is always joined; a DIAGONAL one only when both cells
/// between it and this one are path as well, so a path never joins round the outside of a corner.
///
/// ⚠ This keeps its own kind and link bytes. The authored grid on the disc has no room for them --
/// the runtime tile map does, at +0 and +2, and no park ships with a path already laid (measured:
/// zero path-tiled cells in all four worlds), so a park always starts from bare ground.</summary>
public sealed class PathTool
{
    public enum Kind { None = 0, Path = 2, Queue = 4, Both = 13 }

    readonly Model.HeightField _field;
    readonly PathPieces _pieces;
    readonly int[] _pathSprites, _queueSprites;   // list index -> material index
    readonly Kind[] _kind;
    readonly int[] _turns;
    readonly Dictionary<int, byte> _before = new();

    public int Laid { get; private set; }
    public bool Ready => _pieces != null && _pathSprites != null;
    public string Report { get; }

    public PathTool(Model terrain, PathPieces pieces)
    {
        _field = terrain?.Field;
        _pieces = pieces;
        if (_field == null) { Report = "this terrain carries no authored grid"; return; }
        _kind = new Kind[_field.Count];
        _turns = new int[_field.Count];

        // ⭐ The lists are the material table's own order, filtered by what the name says the tile
        // IS -- not by a world letter. Every world ships the same twenty tiles under the jungle's
        // `jpa_` prefix, at a different base index per file (45, 47, 61), so an index would be
        // wrong in three worlds out of four and a prefix wrong in none.
        var path = new List<int>();
        var queue = new List<int>();
        for (int i = 1; i < terrain.Materials.Count; i++)
        {
            var kind = ParkPaths.Classify(terrain.Materials[i] ?? "");
            if (kind == ParkPathKind.Path) path.Add(i);
            else if (kind == ParkPathKind.Queue) queue.Add(i);
        }
        // ⚠ A CHECK THAT CAN FAIL. The tables index these lists, so a terrain with a different
        // number of tiles would quietly paint the wrong art rather than refuse.
        if (path.Count != 16 || queue.Count != 4)
        {
            Report = $"expected 16 path and 4 queue tiles in the material table, found {path.Count} and {queue.Count}";
            return;
        }
        _pathSprites = path.ToArray();
        _queueSprites = queue.ToArray();
        Report = $"{path.Count} path and {queue.Count} queue tiles, bases {path[0]} and {queue[0]}";
    }

    bool In(int x, int y) => x >= 0 && y >= 0 && x < _field.Width && y < _field.Height;
    int At(int x, int y) => y * _field.Width + x;

    /// <summary>⭐ The engine's own rule, from the tile-map fill at 0x14E700: a cell is unbuildable
    /// exactly when its authored byte0 bit 0 is set, which is the same bit that says the terrain
    /// draws no ground there.</summary>
    public bool CanLay(int x, int y) => Ready && In(x, y) && _field.Buildable(x, y);

    public Kind KindAt(int x, int y) => In(x, y) ? _kind[At(x, y)] : Kind.None;
    bool IsPath(int x, int y) => KindAt(x, y) is Kind.Path or Kind.Both;
    bool IsQueue(int x, int y) => KindAt(x, y) is Kind.Queue or Kind.Both;

    static readonly (int Dx, int Dy, int Bit)[] Ring =
    {
        (0, -1, PathPieces.North),     (1, -1, PathPieces.NorthEast),
        (1, 0, PathPieces.East),       (1, 1, PathPieces.SouthEast),
        (0, 1, PathPieces.South),      (-1, 1, PathPieces.SouthWest),
        (-1, 0, PathPieces.West),      (-1, -1, PathPieces.NorthWest),
    };

    /// <summary>The eight link bits of a cell, by the game's rule.</summary>
    int Links(int x, int y, Func<int, int, bool> joins)
    {
        int bits = 0;
        foreach (var (dx, dy, bit) in Ring)
        {
            if (!joins(x + dx, y + dy)) continue;
            // ⚠ A diagonal needs BOTH cells between it and this one. Without that test a path
            // laid round the outside of a corner reads as a solid block and wears the centre tile.
            if (dx != 0 && dy != 0 && !(joins(x + dx, y) && joins(x, y + dy))) continue;
            bits |= bit;
        }
        return bits;
    }

    /// <summary>Pick a cell's tile again from what it is now joined to, and write it into the grid.</summary>
    void Repick(int x, int y)
    {
        if (!In(x, y)) return;
        var kind = _kind[At(x, y)];
        if (kind == Kind.None) return;
        // A cell that is both is a path first: the path table carries the queue's shapes too.
        var table = kind == Kind.Queue ? _pieces.Queue : _pieces.Path;
        var joins = kind == Kind.Queue ? (Func<int, int, bool>)IsQueue : IsPath;
        var piece = PathPieces.Choose(table, Links(x, y, joins));
        var sprites = piece.List == 2 ? _queueSprites : _pathSprites;
        if (piece.Sprite >= sprites.Length) return;
        _turns[At(x, y)] = piece.Turns;
        _field.Cells[At(x, y) * 2 + 1] = (byte)sprites[piece.Sprite];
    }

    /// <summary>Lay one cell and re-pick it and everything it touches. Returns false when the
    /// game's own rule says nothing may be built there.</summary>
    public bool Lay(int x, int y, Kind kind = Kind.Path)
    {
        if (!CanLay(x, y)) return false;
        var was = _kind[At(x, y)];
        if (was == kind || was == Kind.Both) return false;
        // ⭐ A queue laid onto a path makes the one cell that is BOTH -- the only join between a
        // path network and a queue run. Reproduced because from outside it is invisible: the two
        // are drawn touching either way, and without it nobody can walk between them.
        _before.TryAdd(At(x, y), _field.Cells[At(x, y) * 2 + 1]);
        _kind[At(x, y)] = was == Kind.None ? kind
                        : (was == Kind.Path && kind == Kind.Queue) || (was == Kind.Queue && kind == Kind.Path)
                          ? Kind.Both : kind;
        Laid++;
        Repick(x, y);
        foreach (var (dx, dy, _) in Ring) Repick(x + dx, y + dy);
        return true;
    }

    /// <summary>Quarter turns for a cell's ground tile, for the plot to turn its UVs by.</summary>
    public int Turns(int x, int y) => In(x, y) ? _turns[At(x, y)] : 0;

    /// <summary>Put back every cell this tool changed. ⚠ Only its own.</summary>
    public void Undo()
    {
        foreach (var (at, was) in _before) { _field.Cells[at * 2 + 1] = was; _kind[at] = Kind.None; _turns[at] = 0; }
        _before.Clear();
        Laid = 0;
    }

    /// <summary>What the tool would say about a cell, for the debug line.</summary>
    public string Describe(int x, int y)
    {
        if (!In(x, y)) return $"({x},{y}) off the grid";
        if (!_field.Buildable(x, y)) return $"({x},{y}) no-build";
        var kind = _kind[At(x, y)];
        return kind == Kind.None
            ? $"({x},{y}) clear"
            : $"({x},{y}) {kind} links {Links(x, y, kind == Kind.Queue ? IsQueue : IsPath):X2} turns {_turns[At(x, y)]}";
    }
}
