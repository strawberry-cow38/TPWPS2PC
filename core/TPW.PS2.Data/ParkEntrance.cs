using System.Buffers.Binary;

namespace TPW.PS2.Data;

/// <summary>One park's entrance, as the game's own table gives it: where the walkway in from the
/// gate runs.</summary>
/// <param name="XStart">Where the cross-corridor begins.</param>
/// <param name="ZRow">The row that corridor runs along.</param>
/// <param name="XCol">The left column of the two-wide walkway.</param>
/// <param name="ZEnd">One past the walkway's last row; its mouth is <c>ZEnd - 1</c>.</param>
/// <param name="PathRows">The u16 at +0x0C: how far the path the park STARTS WITH runs into the
/// park past its mouth. The run covers rows <c>ZEnd</c> to <c>ZEnd + PathRows</c> inclusive, so
/// the path is <c>PathRows + 1</c> long -- 4 where this is 3. See <see cref="StartingPath"/>.</param>
public readonly record struct ParkEntranceEntry(int XStart, int ZRow, int XCol, int ZEnd, int PathRows = 0)
{
    public bool Empty => XStart == 0 && ZRow == 0 && XCol == 0 && ZEnd == 0;

    /// <summary>The four fields 0x14E5B0 paints the walkway from, and NOTHING ELSE.
    ///
    /// ⚠⚠ THE AMBIGUITY TEST MUST COMPARE THIS, NOT THE WHOLE RECORD, AND I BROKE IT BY
    /// FORGETTING THAT. Adding <see cref="PathRows"/> as a fifth positional component put it into
    /// the record's generated equality, so entries 3 and 9 -- which carry the SAME walkway and
    /// differ only in how far the starting path runs, 3 against 4 -- stopped comparing equal.
    /// <see cref="ParkEntrance.Fit"/> then saw two distinct answers for any park with XCol 47,
    /// called it ambiguous, and returned `default`: no entrance at all in HALLOW t1 and SPACE t1,
    /// which spawned guests at (0,0) and crashed. astraclaw found it at f2ef76e.
    ///
    /// The question Fit asks is "which WALKWAY is this park's", so that is what it compares.</summary>
    public (int XStart, int ZRow, int XCol, int ZEnd) Walkway => (XStart, ZRow, XCol, ZEnd);

    /// <summary>The cells this entry paints, with the kind the game writes on each.
    ///
    /// ⭐⭐ THE SHAPE IS THE CODE'S, not a rectangle drawn round the numbers. 0x14E5B0 writes, in
    /// this order: the row <see cref="ZRow"/> from <see cref="XStart"/> to <c>XCol + 1</c>; then
    /// the two columns <see cref="XCol"/> and <c>XCol + 1</c> from <see cref="ZRow"/> up to
    /// <c>ZEnd - 2</c>; then the single row <c>ZEnd - 1</c> across both columns as kind 0x0E --
    /// the mouth where the walkway meets the park.
    ///
    /// Checked cell for cell against a live park: FANTASY's entry gives 27 cells of kind 0x0C at
    /// x 39..40, z 6..17 and 2 of kind 0x0E at z 18, and that is exactly what master's savestate
    /// holds in its tile map.</summary>
    public IEnumerable<(int X, int Z, int Kind)> Cells()
    {
        if (Empty) yield break;
        for (int x = XStart; x <= XCol + 1; x++) yield return (x, ZRow, 0x0C);
        for (int z = ZRow; z < ZEnd - 1; z++)
        {
            if (z != ZRow) yield return (XCol, z, 0x0C);
            yield return (XCol + 1, z, 0x0C);
        }
        yield return (XCol, ZEnd - 1, 0x0E);
        yield return (XCol + 1, ZEnd - 1, 0x0E);
    }

    /// <summary>⭐⭐ THE PATH THE PARK IS GIVEN, inside the plot, past the walkway's mouth.
    /// Master, playing it: "a 4 long 2 wide path tile extends into the park from the entrance on a
    /// blank park."
    ///
    /// ⭐ READ FROM 0x14E5B0, not measured off a picture. After it has painted the walkway the
    /// function calls the path layer with FOUR corners:
    ///
    ///   0x15F4C0(XCol,     ZEnd + C)   0x15F4C0(XCol,     ZEnd)
    ///   0x15F4C0(XCol + 1, ZEnd)       0x15F4C0(XCol + 1, ZEnd + C)
    ///
    /// each followed by 0x15F4C8 with the same cell -- the two columns of the walkway carried
    /// <c>C</c> rows further in. <c>C</c> is `(short)(*(u64*)(entry + 8) &gt;&gt; 32)`, the third
    /// u16 of the record, and it is 3 for JUNGLE's three entries and 5 for FANTASY's (both read
    /// off a park, not assumed), 3 and 4 for the other two worlds' pairs. Rows
    /// <c>ZEnd .. ZEnd + C</c> inclusive is <c>C + 1</c> rows: FOUR for JUNGLE, which is the park
    /// master was looking at.
    ///
    /// ⚠ AND ONE CASE IS READ BUT NOT APPLIED. 0x14E5B0 ends with `if (world == 0 &amp;&amp;
    /// (park == 0 || park == 2))`, which lays a second run from (32,65) to (35,65) and sets bits
    /// 0x80 then 0x02 on byte 7 of those four cells. It needs the loader's own world and park
    /// NUMBERS, which no file on the disc carries and which this class deliberately does not have
    /// -- it FITS an entry against the grid instead. Stated here rather than guessed at.</summary>
    public IEnumerable<(int X, int Z)> StartingPath()
    {
        if (Empty || PathRows < 0) yield break;
        for (int z = ZEnd; z <= ZEnd + PathRows; z++) { yield return (XCol, z); yield return (XCol + 1, z); }
    }
}

/// <summary>The park's own entrance walkway -- the way in from the gate, which exists before
/// anybody builds anything.
///
/// ⭐⭐ IT IS A TABLE IN THE EXECUTABLE, at 0x2B71B0, and it is painted straight into the runtime
/// tile map by 0x14E5B0 -- the same function that fills the map from the authored grid. Twelve
/// entries of 0x12 bytes, 0x36 to a world, so three parks each.
///
/// ⚠⚠ THE ENTRANCE IS NOT IN THE TERRAIN DATA. Every cell of the walkway carries authored
/// `byte0 = 0x01` and `byte1 = 0` -- identical to the skipped cells either side of it. Nothing in
/// the grid distinguishes it; it exists only because this table paints it. That is why looking for
/// it in the terrain found nothing, and why a rule built on which MESH covers a cell was never
/// going to be the game's: rasterising `A_ROAD`, `A_BUS STOP` and `ticket_booths` gave 147 cells
/// at z 33..47 for FANTASY against the game's 29 at x 39..40, z 6..18. Not a margin -- a different
/// shape in a different place.
///
/// ⭐ `ZRow` is 6 and `ZEnd` is 19 in EVERY entry and only x moves, which is findings/gates.md's
/// "the entrance is one prefab translated in x" reached a second time, from the code rather than
/// from the mesh anchors.
///
/// See findings/paths.md.</summary>
public sealed class ParkEntrance
{
    const uint Table = 0x2B71B0;
    const int Stride = 0x12, PerWorld = 0x36, Entries = 12;

    public IReadOnlyList<ParkEntranceEntry> All { get; }

    ParkEntrance(ParkEntranceEntry[] all) { All = all; }

    public static ParkEntrance Read(Disc disc)
    {
        var entry = disc.Files().SingleOrDefault(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        if (entry == null) throw new InvalidDataException("The entrance table needs the European SLES_500.32 executable");
        return ReadExecutable(disc.Read(entry.Extent, entry.Size));
    }

    public static ParkEntrance ReadExecutable(byte[] elf)
    {
        if (elf.Length < 52 || !elf.AsSpan(0, 6).SequenceEqual(new byte[] { 127, 69, 76, 70, 1, 1 }))
            throw new InvalidDataException("The entrance table requires a little-endian ELF32 executable");
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off, 4));
        int U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(off, 2));
        int ph = checked((int)U32(28));
        int at = -1;
        for (int i = 0; i < U16(44); i++)
        {
            int p = ph + i * U16(42);
            if (U32(p) == 1 && Table >= U32(p + 8) && (ulong)Table + Entries * Stride <= (ulong)U32(p + 8) + U32(p + 16))
                at = checked((int)(U32(p + 4) + Table - U32(p + 8)));
        }
        if (at < 0) throw new InvalidDataException($"The entrance table address 0x{Table:x} is not in PT_LOAD");

        var all = new ParkEntranceEntry[Entries];
        for (int i = 0; i < Entries; i++)
        {
            int o = at + i * Stride;
            // ⚠ +0x0C is a u16 and the function reads it as one -- `(short)(u64 at +8 >> 32)`.
            all[i] = new ParkEntranceEntry(elf[o], elf[o + 1], elf[o + 0x10], elf[o + 0x11],
                                           U16(o + 0x0C));
        }
        // ⚠ A CHECK THAT CAN FAIL, because the alternative is silently painting a walkway across
        // the middle of a park. Every filled entry shares ZRow and ZEnd -- the entrance is one
        // prefab -- and its columns must sit left of its end.
        var filled = all.Where(e => !e.Empty).ToArray();
        if (filled.Length == 0) throw new InvalidDataException($"The entrance table at 0x{Table:x} is empty");
        if (filled.Any(e => e.ZRow != filled[0].ZRow || e.ZEnd != filled[0].ZEnd || e.XStart > e.XCol || e.ZEnd <= e.ZRow + 1))
            throw new InvalidDataException($"The entries at 0x{Table:x} are not one entrance prefab");
        return new ParkEntrance(all);
    }

    public ParkEntranceEntry For(int world, int park)
    {
        int i = world * (PerWorld / Stride) + park;
        return i >= 0 && i < All.Count ? All[i] : default;
    }

    /// <summary>⭐⭐ WHICH ENTRY IS THIS PARK'S, ASKED OF THE PARK. The table is indexed by a world
    /// number and a park number that the loader holds and a file on disc does not, so rather than
    /// carry a name-to-number map that would be wrong the day a world is added, each entry is
    /// TRIED against the grid and the one that fits is the answer.
    ///
    /// The fit is the shape's own precondition: every cell the entry would paint must be a cell
    /// the terrain draws NO ground on -- the walkway runs through the skipped block outside the
    /// park -- while the cell just past its mouth must be drawn, because that is the park. A
    /// wrong entry fails both ways round, which is what makes this a test rather than a search.
    ///
    /// ⚠ Returns default when NO entry fits or when more than one does; the caller says so out
    /// loud rather than picking one.</summary>
    /// <summary>⭐⭐ THE FLAGPOLES BREAK A TIE THE GRID CANNOT. SPACE's second park matches BOTH
    /// entry 7 (walkway at x 37) and entry 10 (x 35): their columns are undrawn there and the cell
    /// past each mouth is drawn, so the shape test passes twice and honestly reports "ambiguous" --
    /// which left that park with NO entrance and guests spawning at (0,0). That predates the
    /// PathRows change; astraclaw hit both faults at once at f2ef76e.
    ///
    /// The park itself settles it. `A_POLES &amp; BOLLARDS` stands at the bus stop, and across all
    /// EIGHT parks its first pole sits exactly **5.865** left of the walkway's left column:
    ///
    ///   pole 23.135 29.135 31.135 33.135 37.135 41.135  ->  XCol 29 35 37 39 43 47
    ///
    /// ⚠ That is a MEASURED correspondence, not something read out of the code, so it is used
    /// only to CHOOSE between entries the grid already accepted -- never to invent one. If the
    /// hint matches nothing that fits, the ambiguity is reported as before.</summary>
    public static int? WalkwayColumnFromPoles(Model terrain)
    {
        var mesh = terrain?.Meshes.FirstOrDefault(
            m => m.Name.Contains("POLES", StringComparison.OrdinalIgnoreCase));
        if (mesh == null) return null;
        var world = terrain.WorldTransforms();
        var pts = terrain.Vertices(mesh).Pos
            .Select(p => System.Numerics.Vector3.Transform(p, world[mesh.Offset])).ToArray();
        if (pts.Length == 0) return null;
        float top = pts.Max(p => p.Y);
        var tall = pts.Where(p => p.Y > top * 0.6f).ToArray();
        if (tall.Length == 0) return null;
        return (int)Math.Round(tall.Min(p => p.X) + 5.865f);
    }

    public ParkEntranceEntry Fit(Model.HeightField field, out string report) => Fit(field, null, out report);

    /// <param name="walkwayColumn">The column this park's poles say the walkway is in, from
    /// <see cref="WalkwayColumnFromPoles"/>, or null. Used ONLY to break a tie.</param>
    public ParkEntranceEntry Fit(Model.HeightField field, int? walkwayColumn, out string report)
    {
        var hits = new List<int>();
        for (int i = 0; i < All.Count; i++)
        {
            var e = All[i];
            if (e.Empty) continue;
            bool inside = true;
            foreach (var (x, z, _) in e.Cells())
            {
                if (x < 0 || z < 0 || x >= field.Width || z >= field.Height || field.Drawn(x, z)) { inside = false; break; }
            }
            if (!inside) continue;
            // and the park itself, one step past the mouth
            int mx = e.XCol, mz = e.ZEnd;
            if (mz >= field.Height || !field.Drawn(mx, mz) || !field.Drawn(mx + 1, mz)) continue;
            hits.Add(i);
        }
        // ⚠⚠ SEVERAL ENTRIES FIT, AND THAT IS NOT AMBIGUITY -- the table REPEATS. Jungle's three
        // park slots hold the same four numbers, and entries 3 and 9 carry the same walkway, so
        // "entries 0, 1 and 2 all fit" was one answer counted three times. What matters is how
        // many DISTINCT WALKWAYS fit, not how many rows -- see ParkEntranceEntry.Walkway for why
        // comparing the whole record instead cost HALLOW and SPACE their entrance entirely.
        var distinct = hits.Select(i => All[i]).DistinctBy(e => e.Walkway).ToArray();
        string chose = "";
        int matched = -1;
        if (distinct.Length > 1 && walkwayColumn is { } want)
        {
            var picked = distinct.Where(e => e.XCol == want).ToArray();
            matched = picked.Length;
            if (picked.Length == 1)
            {
                chose = $" (of {distinct.Length}, chosen by this park's poles at column {want})";
                hits = hits.Where(i => All[i].XCol == want).ToList();
                distinct = picked;
            }
        }
        if (distinct.Length != 1)
        {
            // ⚠ SAY WHAT THE HINT DID, not just that it failed. "matches none of them" when it
            // in fact matched BOTH sends the next reader looking at the poles instead of at the
            // reason two entries with the SAME column are being told apart at all.
            string hint = walkwayColumn is { } w
                ? $"; the poles say column {w}, which {matched switch
                    { 0 => "matches none of them", 1 => "matched one", _ => $"matches {matched} of them" }}"
                : "";
            report = distinct.Length == 0 ? "no entrance entry fits this grid"
                   : $"entries {string.Join(", ", hits)} fit and their walkways disagree -- ambiguous" + hint;
            return default;
        }
        // ⭐ ONE WALKWAY, AND THE TIED ENTRIES CAN STILL DISAGREE ON HOW FAR THE PATH RUNS.
        // HALLOW's first park and SPACE's have the identical walkway at XCol 47 and differ only
        // here -- 3 rows against 4 -- and the grid cannot tell them apart, because the difference
        // is INSIDE the park rather than in the shape being matched. The shorter run is taken and
        // SAID OUT LOUD: it lays one row less than retail in one park of one world, against a
        // guess that would be silently wrong in whichever of the two it got backwards. Resolving
        // it needs the loader's world number, which the disc does not carry.
        var rows = hits.Select(i => All[i].PathRows).Distinct().OrderBy(r => r).ToArray();
        var pick = distinct[0] with { PathRows = rows[0] };
        report = $"entry {hits[0]}{(hits.Count > 1 ? $" (and {string.Join(", ", hits.Skip(1))}, same walkway)" : "")}{chose}: {pick}"
               + (rows.Length > 1 ? $"  ⚠ those entries give {string.Join("/", rows)} starting-path rows; "
                                  + $"taking {rows[0]}, the grid cannot choose" : "");
        return pick;
    }
}
