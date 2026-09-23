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
    /// <summary>Which ride a cell belongs to. 0 is a free path that belongs to nobody.</summary>
    readonly int[] _owner;
    /// <summary>⭐⭐ A QUEUE'S LINKS ARE THE RUN IT WAS DRAWN AS, and nothing else. These bits are
    /// written when the cell is laid -- the cell before it and the cell after it in the run -- and
    /// are never derived from what happens to be next to it afterwards. Master: "the queue is
    /// EXACTLY as its drawn with the tool"; the PSX build says the same thing from the other side,
    /// where a queue tile joins only the tile BEHIND it along the run. Without this a queue that
    /// doubles back beside itself fuses into a slab, and two rides whose queues run side by side
    /// merge into one.</summary>
    readonly int[] _run;
    /// <summary>A ride's doors: the cell, whose it is, and WHICH door it is.
    ///
    /// ⭐⭐ A QUEUE ONLY EVER REACHES AN ENTRANCE. Master: "the queue shouldnt get the connected
    /// sprite for the 'internal' path tile of the exit" -- a queue that wore an arm toward the way
    /// out would be drawing a route nobody walks. A PATH may reach either, because a path is what
    /// people leave by.</summary>
    readonly Dictionary<int, (int Ride, bool Entrance, int Dx, int Dy)> _doors = new();

    /// <summary>⭐⭐ THE WALKWAY THE PARK COMES WITH. The game paints it straight into the runtime
    /// tile map from its own table (see <see cref="ParkEntrance"/>), so it is path as far as
    /// anything that walks or joins is concerned, and a run laid up to its mouth has to attach.
    ///
    /// ⚠ IT IS NOT GROUND THIS TOOL PAINTS. Every cell of it is one the terrain draws NO ground
    /// on -- the entrance prefab's mesh stands there instead -- so writing a path tile into the
    /// grid would lay `jpa_` art over the road. It counts as path and is never drawn.</summary>
    readonly HashSet<int> _walkway = new();
    readonly Dictionary<int, byte> _before = new();

    /// <summary>⭐⭐ ONE LEG, ONE UNDO. The queue tool's right button steps a run BACK, not out --
    /// master: "with queues, rmb becomes 'undo last leg' up until the single tile sticking out,
    /// then it becomes close tool". So every press that lays something records what it found
    /// there first, and the record is what the step back puts back.
    ///
    /// ⚠ NEIGHBOURS ARE NOT RECORDED, they are RE-PICKED. A leg touches the art of everything
    /// within two cells of it, and journalling all of that would be storing a value that is
    /// derivable -- the kind of duplicate state that goes stale. Restoring the cells and picking
    /// their surroundings again reaches the same answer from the rule.</summary>
    readonly record struct Was(int At, Kind Kind, int Owner, int Run, byte Tile, int Turns);
    readonly List<List<Was>> _legs = new();
    List<Was> _leg;

    /// <summary>How many undoable legs have been laid. ⭐ The caller compares this against what it
    /// was when the tool opened, so a step back can never walk out of its own run and into one
    /// somebody laid earlier.</summary>
    public int LegCount => _legs.Count;

    /// <summary>Start recording a leg. Everything laid until the next call goes back together.</summary>
    public void BeginLeg() { _leg = new List<Was>(); _legs.Add(_leg); }

    /// <summary>Put the most recent leg back. Returns false when there is none.</summary>
    public bool UndoLeg()
    {
        if (_legs.Count == 0) return false;
        var leg = _legs[^1];
        _legs.RemoveAt(_legs.Count - 1);
        if (ReferenceEquals(_leg, leg)) _leg = null;
        foreach (var w in leg)
        {
            if (_kind[w.At] != Kind.None && w.Kind == Kind.None) Laid--;
            _kind[w.At] = w.Kind; _owner[w.At] = w.Owner; _run[w.At] = w.Run;
            _field.Cells[w.At * 2 + 1] = w.Tile; _turns[w.At] = w.Turns;
        }
        foreach (var w in leg) RepickAround(w.At % _field.Width, w.At / _field.Width);
        return true;
    }

    /// <summary>Note what a cell was before it is written. ⚠ ONCE per leg: a cell laid twice in
    /// one leg must go back to what it was before the leg, not to what it was halfway through.</summary>
    void Record(int at)
    {
        if (_leg == null) return;
        foreach (var w in _leg) if (w.At == at) return;
        _leg.Add(new Was(at, _kind[at], _owner[at], _run[at], _field.Cells[at * 2 + 1], _turns[at]));
    }

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
        _owner = new int[_field.Count];
        _run = new int[_field.Count];

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
        // ⚠ ONE OF THESE HAS NO ART ON THE DISC. Jungle names `jpa_que4` at index 77 and no WAD
        // carries a file of that stem. It is the FIRST queue-classified material, so by the same
        // list-order rule the path block confirms name-for-name (squ1 end1 str1 cnr2 against
        // sprites 0 1 2 3) it is queue sprite 0 -- the tile for a cell joined to NOTHING. A queue
        // always carries its run link or its ride's door, so it should never be picked. See
        // findings/paths.md; this is left as it is rather than papered over with a substitute.
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
    public bool CanLay(int x, int y) => Ready && In(x, y) && _field.Buildable(x, y) && !_walkway.Contains(At(x, y));

    public Kind KindAt(int x, int y)
        => !In(x, y) ? Kind.None
         : _walkway.Contains(At(x, y)) ? Kind.Path
         : _kind[At(x, y)];

    /// <summary>Is this one of the park's own entrance cells?</summary>
    public bool IsWalkway(int x, int y) => In(x, y) && _walkway.Contains(At(x, y));

    /// <summary>Tell the tool where the park's own walkway runs. ⚠ Replaces whatever was there,
    /// because it belongs to the park and not to anything the player did.</summary>
    public void SetWalkway(IEnumerable<(int X, int Y)> cells)
    {
        if (!Ready) return;
        var was = _walkway.ToArray();
        _walkway.Clear();
        foreach (var (x, y) in cells) if (In(x, y)) _walkway.Add(At(x, y));
        foreach (var at in was.Concat(_walkway)) RepickAround(at % _field.Width, at / _field.Width);
    }

    static readonly (int Dx, int Dy, int Bit)[] Ring =
    {
        (0, -1, PathPieces.North),     (1, -1, PathPieces.NorthEast),
        (1, 0, PathPieces.East),       (1, 1, PathPieces.SouthEast),
        (0, 1, PathPieces.South),      (-1, 1, PathPieces.SouthWest),
        (-1, 0, PathPieces.West),      (-1, -1, PathPieces.NorthWest),
    };

    /// <summary>The ring bit that points from (x,y) at its neighbour (nx,ny), or 0 if that is
    /// not a neighbour at all.</summary>
    public static int BitToward(int x, int y, int nx, int ny)
    {
        foreach (var (dx, dy, bit) in Ring) if (x + dx == nx && y + dy == ny) return bit;
        return 0;
    }

    /// <summary>The bits pointing at a ride door this cell is allowed to use.
    ///
    /// ⭐ A DOOR IS A LINK. Master: "paths and queues should have the sprite as if they are
    /// connected to the entry/exit points" -- the tile outside a ride's door has to look attached
    /// to it, and the only way a ground tile can look attached to anything is an arm in the piece.
    /// ⚠ ORTHOGONAL ONLY: a doorway is a side of a square, not a corner of one.
    /// ⚠ And a QUEUE may only use its OWN ride's doors -- master: "paths should carry a ride ID,
    /// they should not connect to other rides' queues". A free path (owner 0) may use any, which
    /// is what lets one path serve every ride in the park.</summary>
    int DoorBits(int x, int y, int owner, bool queue)
    {
        int bits = 0;
        foreach (var (dx, dy, bit) in Ring)
        {
            if (dx != 0 && dy != 0) continue;
            if (!In(x + dx, y + dy)) continue;
            if (!_doors.TryGetValue(At(x + dx, y + dy), out var door)) continue;
            // ⭐⭐ A DOOR FACES ONE WAY. Master: "the imaginary secret exit path tile / entry-exit
            // tile should only attach in the direction the path tile is in". A doorway opens onto
            // exactly one tile -- the stub laid with the ride -- so a path merely running PAST the
            // door cell along the ride's wall is not at the door and gets no arm. Without this,
            // every path that brushed the side of a ride grew a stub toward a doorway nobody could
            // reach from it.
            if (door.Dx != -dx || door.Dy != -dy) continue;
            if (queue && !door.Entrance) continue;
            if (queue && owner != 0 && door.Ride != owner) continue;
            bits |= bit;
        }
        return bits;
    }

    /// <summary>⭐⭐ IS THIS QUEUE CELL THE END OF ITS RUN? The console's rule, straight across from
    /// the PSX build: a queue tile with FEWER THAN TWO links is an end. Run links and the door
    /// count; anything else does not, so a queue lying beside a path is still an end only at the
    /// tip. This is what master's "paths should only have the connected sprite if they are
    /// connected at the end of queues" turns into -- the path asks the queue whether it is a tip,
    /// and joins only if it is.</summary>
    public bool IsQueueEnd(int x, int y)
    {
        if (!In(x, y)) return false;
        int at = At(x, y);
        if (_kind[at] is not (Kind.Queue or Kind.Both)) return false;
        int bits = _run[at] | DoorBits(x, y, _owner[at], queue: true);
        int n = 0;
        for (int b = bits; b != 0; b &= b - 1) n++;
        return n < 2;
    }

    /// <summary>What a PATH is willing to join: other path, and a queue only at its tip.</summary>
    bool PathJoins(int x, int y)
    {
        if (!In(x, y)) return false;
        if (_walkway.Contains(At(x, y))) return true;
        var k = _kind[At(x, y)];
        if (k is Kind.Path or Kind.Both) return true;
        return k == Kind.Queue && IsQueueEnd(x, y);
    }

    /// <summary>The eight link bits of a cell.
    ///
    /// ⭐⭐ THE TWO KINDS ARE NOT LINKED THE SAME WAY, and that is the whole of master's complaint
    /// that "the sprites also shouldnt connect to eachother freely". A PATH is a network and finds
    /// its neighbours, by the game's own rule (PSX 0x8004E20C). A QUEUE is a LINE and carries the
    /// links it was drawn with; it finds nothing.</summary>
    int LinksFor(int x, int y)
    {
        int at = At(x, y);
        var kind = _kind[at];
        if (kind == Kind.None) return 0;
        int bits = DoorBits(x, y, _owner[at], queue: kind == Kind.Queue);
        if (kind == Kind.Queue) return bits | _run[at];

        foreach (var (dx, dy, bit) in Ring)
        {
            if (!PathJoins(x + dx, y + dy)) continue;
            // ⚠ A diagonal needs BOTH cells between it and this one. Without that test a path
            // laid round the outside of a corner reads as a solid block and wears the centre tile.
            if (dx != 0 && dy != 0 && !(PathJoins(x + dx, y) && PathJoins(x, y + dy))) continue;
            bits |= bit;
        }
        // A cell that is BOTH is drawn as a path, and it is also the tile a queue run ends on, so
        // it keeps the arm back down its own queue.
        if (kind == Kind.Both) bits |= _run[at];
        return bits;
    }

    /// <summary>A cell's link bits, for a control to read. ⚠ Bits, not a picture: two queue runs
    /// a cell apart look identical whether or not they are joined, and only the mask says.</summary>
    public int LinkBits(int x, int y) => In(x, y) ? LinksFor(x, y) : 0;

    /// <summary>Tell the tool about a ride's entrance or exit cell, so the ground beside it can
    /// look attached to it.</summary>
    /// <summary>⚠ (dx,dy) is THE WAY THE DOOR OPENS -- from the door cell toward the tile outside
    /// it. Only that one tile may wear an arm back at it.</summary>
    public void AddDoor(int x, int y, int rideId, bool entrance, int dx, int dy)
    {
        if (!Ready || !In(x, y)) return;
        _doors[At(x, y)] = (rideId, entrance, dx, dy);
        RepickAround(x, y);
    }

    /// <summary>Pick this cell and everything within two of it again.
    ///
    /// ⚠ TWO, NOT ONE. Laying a queue cell changes whether the cell BEFORE it is still a tip, and
    /// that changes the piece worn by the paths around THAT cell -- which are two away from the one
    /// that moved. A one-ring repick left a path with an arm pointing at the middle of a queue.</summary>
    void RepickAround(int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                Repick(x + dx, y + dy);
    }

    /// <summary>Pick a cell's tile again from what it is now joined to, and write it into the grid.</summary>
    void Repick(int x, int y)
    {
        if (!In(x, y)) return;
        var kind = _kind[At(x, y)];
        if (kind == Kind.None) return;
        // A cell that is both is a path first: the path table carries the queue's shapes too.
        var table = kind == Kind.Queue ? _pieces.Queue : _pieces.Path;
        var piece = PathPieces.Choose(table, LinksFor(x, y));
        var sprites = piece.List == 2 ? _queueSprites : _pathSprites;
        if (piece.Sprite >= sprites.Length) return;
        _turns[At(x, y)] = piece.Turns;
        _field.Cells[At(x, y) * 2 + 1] = (byte)sprites[piece.Sprite];
    }

    /// <summary>Lay one cell and re-pick it and everything it touches. Returns false when the
    /// game's own rule says nothing may be built there.</summary>
    public bool Lay(int x, int y, Kind kind = Kind.Path, int owner = 0, int runBits = 0)
    {
        if (!CanLay(x, y)) return false;
        int at = At(x, y);
        Record(at);
        var was = _kind[at];
        // ⚠ THE RUN BITS GO ON EVEN WHEN NOTHING ELSE CHANGES. A run is laid a segment at a time
        // and each segment STARTS on the last cell of the one before, so that shared cell is laid
        // twice -- and the second lay is the one that carries the link onward. Returning early on
        // "already this kind" before writing them left every corner of a queue unlinked.
        if (kind == Kind.Queue) _run[at] |= runBits;
        if (was == kind || was == Kind.Both)
        {
            if (runBits != 0) RepickAround(x, y);
            return false;
        }
        // ⭐ A queue laid onto a path makes the one cell that is BOTH -- the only join between a
        // path network and a queue run. Reproduced because from outside it is invisible: the two
        // are drawn touching either way, and without it nobody can walk between them.
        _before.TryAdd(at, _field.Cells[at * 2 + 1]);
        _kind[at] = was == Kind.None ? kind
                  : (was == Kind.Path && kind == Kind.Queue) || (was == Kind.Queue && kind == Kind.Path)
                    ? Kind.Both : kind;
        // ⭐ A CELL REMEMBERS WHOSE IT IS -- master: "paths should carry a ride ID". It GATES a
        // queue (which may only reach its own ride's doors) and it is only a record on a path,
        // because a path that would not serve every ride is a path no visitor can use.
        if (owner != 0) _owner[at] = owner;
        Laid++;
        RepickAround(x, y);
        return true;
    }

    /// <summary>Whose queue a cell is, or 0.</summary>
    public int OwnerAt(int x, int y) => In(x, y) ? _owner[At(x, y)] : 0;

    /// <summary>Quarter turns for a cell's ground tile, for the plot to turn its UVs by.</summary>
    public int Turns(int x, int y) => In(x, y) ? _turns[At(x, y)] : 0;

    /// <summary>Put back every cell this tool changed. ⚠ Only its own.</summary>
    public void Undo()
    {
        foreach (var (at, was) in _before)
        { _field.Cells[at * 2 + 1] = was; _kind[at] = Kind.None; _turns[at] = 0; _owner[at] = 0; _run[at] = 0; }
        _before.Clear();
        _legs.Clear(); _leg = null;
        Laid = 0;
    }

    /// <summary>What the tool would say about a cell, for the debug line.</summary>
    public string Describe(int x, int y)
    {
        if (!In(x, y)) return $"({x},{y}) off the grid";
        if (!_field.Buildable(x, y)) return $"({x},{y}) no-build";
        var kind = _kind[At(x, y)];
        if (kind == Kind.None)
            return _doors.TryGetValue(At(x, y), out var who)
                 ? $"({x},{y}) ride {who.Ride}'s {(who.Entrance ? "entrance" : "exit")} facing {who.Dx},{who.Dy}"
                 : $"({x},{y}) clear";
        return $"({x},{y}) {kind} links {LinksFor(x, y):X2} turns {_turns[At(x, y)]}"
             + (_owner[At(x, y)] != 0 ? $" of ride {_owner[At(x, y)]}" : "")
             + (IsQueueEnd(x, y) ? " (tip)" : "");
    }
}
