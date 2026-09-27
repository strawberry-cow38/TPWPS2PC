using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TPW.PS2.Data;

/// <summary>One selectable place in the lobby -- the "world map" the game shows between parks.
///
/// ⭐⭐ THE FOUR NEIGHBOURS ARE AUTHORED, NOT GEOMETRIC. `FUN_00217e20` reads the pressed
/// direction and takes the slot to move to straight out of this record; it never compares
/// positions. So a port that picks "the nearest park in that direction" is a different machine:
/// this table is free to be asymmetric, and it is (slot 2 goes to 1 on direction 0, but slot 1
/// goes to 6).
///
/// ⚠ <see cref="None"/> (20) is out of range of the nineteen records on purpose -- it is the
/// "there is nothing that way" sentinel, and every direction that carries it also carries a zero
/// <see cref="Yaw"/>.</summary>
public readonly record struct LobbySlot(
    int Kind, int Field2, int Field4, int World, int ParkInWorld,
    int Id, int NameTextId, byte[] Neighbours, int[] Yaw)
{
    /// <summary>The "no neighbour this way" sentinel: 20, past the last record.</summary>
    public const byte None = 20;

    /// <summary>A park's entry. ⚠ Kind 1 records carry three bytes and nothing else; the eleven
    /// that follow the parks are all zero, so they are spare rather than meaningful.</summary>
    public bool IsPark => Kind == 0;

    /// <summary>Where direction <paramref name="dir"/> (0..3) leads, or null at a dead end.</summary>
    public int? Neighbour(int dir)
        => dir < 0 || dir > 3 || Neighbours[dir] == None ? null : Neighbours[dir];
}

/// <summary>⭐⭐ THE LOBBY'S OWN TABLE, read out of the executable rather than retyped.
///
/// `FUN_00216f90` copies it into the `WorldMapSelector` at `this+0x8c`, one `0x1C`-byte record per
/// slot, from `0x36DD00` up to `0x36DF14` -- **nineteen** records, of which the first eight are
/// the parks and the remaining eleven are empty kind-1 entries.
///
/// ⭐⭐⭐ RECORD ORDER IS NOT MODEL ORDER, and mixing them up is the trap this class exists to
/// close. The records are grouped BY WORLD (jungle1, jungle2, hallow1, hallow2, fantasy1,
/// fantasy2, space1, space2) -- which is what `+0x06`/`+0x07` say, and what the `STR_MAP_*` text
/// ids at `+0x0A` independently confirm. The MODELS load in a different order entirely
/// (jungle1, hallow1, fantasy1, jungle2, space1, hallow2, fantasy2, space2). The game converts
/// with the little table at `0x36DCC0`, and <see cref="ModelIndex"/> is that conversion:
/// checked against all eight, `table[record] == modelIndex + 1` exactly.</summary>
public sealed class LobbySlots
{
    const uint Table = 0x36DD00, TableEnd = 0x36DF14, KeyTable = 0x36DCC0;
    const int Stride = 0x1C;

    /// <summary>The nineteen records, in the game's own order.</summary>
    public IReadOnlyList<LobbySlot> All { get; }

    /// <summary>Just the eight parks -- <see cref="LobbySlot.IsPark"/> -- in record order.</summary>
    public IReadOnlyList<LobbySlot> Parks => All.Where(s => s.IsPark).ToList();

    /// <summary>The `0x36DCC0` key table, indexed by record.</summary>
    public IReadOnlyList<int> Keys { get; }

    LobbySlots(LobbySlot[] all, int[] keys) { All = all; Keys = keys; }

    /// <summary>⭐ Which loaded model a record wants: the key table minus one.
    ///
    /// The key is also what the game hands `0x1f1f78` to find the park's seat on `base`, so the
    /// same number is both "which model" and "which fitting id" -- see
    /// <see cref="SeatFittingId"/>.</summary>
    public int ModelIndex(int record)
        => record < 0 || record >= Keys.Count ? -1 : Keys[record] - 1;

    /// <summary>The fitting id on `base.mps` that a MODEL (not a record) is seated on.
    /// `FUN_00216f90` searches with the model's own loop index plus one, under mask
    /// <see cref="SeatMask"/>.</summary>
    public static int SeatFittingId(int modelIndex) => modelIndex + 1;

    /// <summary>The flag bit the seat search matches. ⚠ The selection path uses `0x1000` against
    /// the SAME table, so the mask is what tells the two kinds of node apart, not the id.</summary>
    public const uint SeatMask = 0x400;

    /// <summary>⭐ Every park model is scaled by this. `UNK_0036DCE0` is eight floats and all
    /// eight are 0.7 -- it is per-slot in the code and uniform in the data, so it is read as a
    /// constant here and noted as a table there.</summary>
    public const float ModelScale = 0.7f;

    /// <summary>The eight models, in the order `FUN_00216f90` loads them from `0x2EF7D8`
    /// (load ids 2..9; `base` is id 1).</summary>
    public static readonly string[] ModelNames =
        { "jungle1", "hallow1", "fantasy1", "jungle2", "space1", "hallow2", "fantasy2", "space2" };

    public static LobbySlots Read(Disc disc)
    {
        var entry = disc.Files().SingleOrDefault(
            f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        if (entry == null) throw new InvalidDataException("The lobby table needs the European SLES_500.32 executable");
        return ReadExecutable(disc.Read(entry.Extent, entry.Size));
    }

    public static LobbySlots ReadExecutable(byte[] elf)
    {
        if (elf.Length < 52 || !elf.AsSpan(0, 6).SequenceEqual(new byte[] { 127, 69, 76, 70, 1, 1 }))
            throw new InvalidDataException("The lobby table requires a little-endian ELF32 executable");
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off, 4));
        int U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(off, 2));

        // ⚠ The same PT_LOAD walk ParkEntrance does, and for the same reason: a raw file offset
        // would be right only by luck.
        int At(uint vaddr, int bytes)
        {
            int ph = checked((int)U32(28)), at = -1;
            for (int i = 0; i < U16(44); i++)
            {
                int p = ph + i * U16(42);
                if (U32(p) == 1 && vaddr >= U32(p + 8)
                    && (ulong)vaddr + (ulong)bytes <= (ulong)U32(p + 8) + U32(p + 16))
                    at = checked((int)(U32(p + 4) + vaddr - U32(p + 8)));
            }
            if (at < 0) throw new InvalidDataException($"Lobby address 0x{vaddr:x} is not in PT_LOAD");
            return at;
        }

        int count = (int)((TableEnd - Table) / Stride);          // nineteen
        int at = At(Table, count * Stride);
        var all = new LobbySlot[count];
        for (int i = 0; i < count; i++)
        {
            int o = at + i * Stride;
            var neigh = new byte[4];
            Array.Copy(elf, o + 0x0C, neigh, 0, 4);
            var yaw = new int[4];
            for (int d = 0; d < 4; d++) yaw[d] = U16(o + 0x10 + d * 2);
            all[i] = new LobbySlot(
                Kind: elf[o], Field2: U16(o + 2), Field4: U16(o + 4),
                World: elf[o + 6], ParkInWorld: elf[o + 7],
                Id: U16(o + 8), NameTextId: U16(o + 0x0A),
                Neighbours: neigh, Yaw: yaw);
        }

        int kat = At(KeyTable, 8 * 4);
        var keys = new int[8];
        for (int i = 0; i < 8; i++) keys[i] = (int)U32(kat + i * 4);
        return new LobbySlots(all, keys);
    }
}
