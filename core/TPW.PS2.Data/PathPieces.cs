using System.Buffers.Binary;

namespace TPW.PS2.Data;

/// <summary>One record of the game's path-piece table: which tile art a path cell wears, and how
/// far round, for a given set of joined neighbours.</summary>
/// <param name="List">0 grass, 1 the world's path tiles, 2 its queue tiles.</param>
/// <param name="Sprite">The index within that list.</param>
/// <param name="Turns">Quarter turns clockwise (the table stores degrees).</param>
/// <param name="Mask">The eight neighbour bits this record answers.</param>
public readonly record struct PathPiece(int List, int Sprite, int Turns, int Mask);

/// <summary>The tables that turn "which neighbours am I joined to" into "which tile do I wear, and
/// which way round". READ from the owner's own SLES_500.32 -- nothing here is fitted to a render.
///
/// ⭐⭐ THE PIECE IS NOT STORED, IT IS DERIVED. A path cell has no shape of its own: the game
/// recomputes it from the eight neighbour bits every time anything next to it changes, which is why
/// laying one tile repaints its neighbours too.
///
/// Located in the PAL executable by the table's own shape -- a 12-byte record whose middle word is
/// a quarter-turn angle -- and then pinned by which one the code loads: the chooser at 0x1E6950
/// takes `0x2B8128` with its count at `0x3611DC` for a path (tile kind 13 in that branch, 2 in the
/// one above it) and `0x2B80A0` / `0x3611D8` for a queue. A second, identical-shaped pair at
/// 0x2E23F0 / 0x2E2368 carries different masks and NOTHING REFERENCES IT, so it is not used here.
///
/// ⭐ Three independent checks agree, from two different sources and methods:
///   * the counts are 49 and 11, which is what the PSX build's tables hold (findings/paths.md in
///     the PSX port, read from a different executable by different means);
///   * the bit assignment falls out of the data -- the four one-arm records carry 0x01, 0x04, 0x10
///     and 0x40, so North, East, South and West are bits 0, 2, 4 and 6 and the diagonals are the
///     odd bits between, exactly the PSX assignment;
///   * the sprite indices land on the right ART. Sprite 0 answers mask 0 (alone) and is
///     `jpa_squ1`; 1 answers the four single arms and is `jpa_end1`; 2 answers 0x11 and 0x44 and is
///     `jpa_str1`; 3 the four right angles, `jpa_cnr2`; 4 the four three-arms, `jpa_tju1`; 5 the
///     four-arm 0x55, `jpa_xrd1`; and 13 answers 0xFF -- every neighbour joined -- and is
///     `jpa_ctr1`, the centre. Seven names predicted, seven hit.
///
/// ⚠ NOT READ: where a PS2 tile keeps its chosen piece. The PSX packs `sprite | turns &lt;&lt; 12` into
/// the tile's ground word; no such pack appears in this region on PS2, so the turn is kept beside
/// the grid here rather than claimed to be in it.</summary>
public sealed class PathPieces
{
    /// <summary>The neighbour bits, as the one-arm records give them. z grows southwards.</summary>
    public const int North = 0x01, NorthEast = 0x02, East = 0x04, SouthEast = 0x08,
                     South = 0x10, SouthWest = 0x20, West = 0x40, NorthWest = 0x80;

    const uint PathTable = 0x2B8128, PathCount = 0x3611DC;
    const uint QueueTable = 0x2B80A0, QueueCount = 0x3611D8;

    public IReadOnlyList<PathPiece> Path { get; }
    public IReadOnlyList<PathPiece> Queue { get; }

    PathPieces(PathPiece[] path, PathPiece[] queue) { Path = path; Queue = queue; }

    public static PathPieces Read(Disc disc)
    {
        var entry = disc.Files().SingleOrDefault(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        if (entry == null) throw new InvalidDataException("Path pieces need the European SLES_500.32 executable");
        return ReadExecutable(disc.Read(entry.Extent, entry.Size));
    }

    public static PathPieces ReadExecutable(byte[] elf)
    {
        if (elf.Length < 52 || !elf.AsSpan(0, 6).SequenceEqual(new byte[] { 127, 69, 76, 70, 1, 1 }))
            throw new InvalidDataException("Path pieces require a little-endian ELF32 executable");
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off, 4));
        int U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(off, 2));
        int Offset(uint va, int size)
        {
            int ph = checked((int)U32(28));
            for (int i = 0; i < U16(44); i++)
            {
                int p = ph + i * U16(42);
                if (U32(p) == 1 && va >= U32(p + 8) && (ulong)va + (uint)size <= (ulong)U32(p + 8) + U32(p + 16))
                    return checked((int)(U32(p + 4) + va - U32(p + 8)));
            }
            throw new InvalidDataException($"Path-piece address 0x{va:x} is not in PT_LOAD");
        }
        // ⚠ The counts come out of the executable beside the tables, not from a constant here: if
        // this is a different build the tables move, and a wrong count is how that would show.
        PathPiece[] Table(uint table, uint countAt, int expected, int list)
        {
            int n = checked((int)U32(Offset(countAt, 4)));
            if (n != expected)
                throw new InvalidDataException($"Expected {expected} records at 0x{table:x}, the executable says {n}");
            int at = Offset(table, n * 12);
            var recs = new PathPiece[n];
            for (int i = 0; i < n; i++)
            {
                uint piece = U32(at + i * 12);
                int degrees = BinaryPrimitives.ReadInt32LittleEndian(elf.AsSpan(at + i * 12 + 4, 4));
                int mask = elf[at + i * 12 + 8];
                if (degrees % 90 != 0 || degrees < 0 || degrees >= 360 || (piece >> 16) > 2 || (piece & 0xFFFF) >= 16)
                    throw new InvalidDataException($"Record {i} at 0x{table:x} is not a path piece");
                recs[i] = new PathPiece((int)(piece >> 16), (int)(piece & 0xFFFF), degrees / 90, mask);
            }
            // ⭐ The catch-all must be last and must be this list's own sprite 0: it is what
            // Choose falls back on, so a table without it would silently leave cells unpainted.
            if (recs[^1].Mask != 0 || recs[^1].Sprite != 0 || recs[^1].List != list)
                throw new InvalidDataException($"The table at 0x{table:x} has no catch-all record");
            return recs;
        }
        return new PathPieces(Table(PathTable, PathCount, 49, 1), Table(QueueTable, QueueCount, 11, 2));
    }

    /// <summary>The piece a cell with these links wears: the LAST record whose mask is exactly the
    /// links, or failing that the FIRST whose mask the links contain. The catch-all always answers.
    ///
    /// ⚠ Last-then-first, not first-then-first. Sprite 12 appears twice with the same mask at
    /// different angles (records 27/29 and 28/30), so "the first exact match" would pick a
    /// different turn from the game's.</summary>
    public static PathPiece Choose(IReadOnlyList<PathPiece> table, int links)
    {
        int found = -1;
        for (int i = 0; i < table.Count; i++) if (table[i].Mask == links) found = i;
        if (found < 0)
            for (int i = 0; i < table.Count; i++) if ((table[i].Mask & links) == table[i].Mask) { found = i; break; }
        return found < 0 ? default : table[found];
    }
}
