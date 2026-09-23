using System.Buffers.Binary;

namespace TPW.PS2.Data;

/// <summary>One park's entrance, as the game's own table gives it: where the walkway in from the
/// gate runs.</summary>
/// <param name="XStart">Where the cross-corridor begins.</param>
/// <param name="ZRow">The row that corridor runs along.</param>
/// <param name="XCol">The left column of the two-wide walkway.</param>
/// <param name="ZEnd">One past the walkway's last row; its mouth is <c>ZEnd - 1</c>.</param>
public readonly record struct ParkEntranceEntry(int XStart, int ZRow, int XCol, int ZEnd)
{
    public bool Empty => XStart == 0 && ZRow == 0 && XCol == 0 && ZEnd == 0;

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
            all[i] = new ParkEntranceEntry(elf[o], elf[o + 1], elf[o + 0x10], elf[o + 0x11]);
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
    public ParkEntranceEntry Fit(Model.HeightField field, out string report)
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
        // park slots hold the same four numbers, and entries 3 and 9 are identical to each other,
        // so "entries 0, 1 and 2 all fit" was one answer counted three times. What matters is how
        // many DISTINCT walkways fit, not how many rows.
        var distinct = hits.Select(i => All[i]).Distinct().ToArray();
        report = distinct.Length == 0 ? "no entrance entry fits this grid"
               : distinct.Length > 1
                 ? $"entries {string.Join(", ", hits)} fit and disagree -- ambiguous"
                 : $"entry {hits[0]}{(hits.Count > 1 ? $" (and {string.Join(", ", hits.Skip(1))}, identical)" : "")}: {distinct[0]}";
        return distinct.Length == 1 ? distinct[0] : default;
    }
}
