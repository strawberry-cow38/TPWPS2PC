using System.Buffers.Binary;

namespace TPW.PS2.Data;

/// <summary>Shift-JIS to exported image ordinal, NOT Unicode or a BFF descriptor index.
/// The ELF consumer at 0x130058 adds 0x23b to a signed table entry. See findings/kanji-table.md.
/// The file loader / assignment of its global pointer was not found.</summary>
public sealed class KanjiTable
{
    public const int RuntimeBase = 0x23b;
    public const int MaximumSlots = 0x15aa;
    public ushort ImageCount { get; }
    public IReadOnlyList<short> Slots { get; }

    public KanjiTable(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 4 || (data.Length & 1) != 0 || data.Length > 2 + 2 * MaximumSlots)
            throw new InvalidDataException("Kanji table: invalid word-array extent");
        ImageCount = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (ImageCount == 0 || ImageCount > short.MaxValue)
            throw new InvalidDataException("Kanji table: invalid image count");
        var slots = new short[(data.Length - 2) / 2];
        var seen = new bool[ImageCount];
        for (int i = 0; i < slots.Length; i++)
        {
            short id = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(2 + 2 * i));
            if (id < -1 || id >= ImageCount || (id >= 0 && seen[id]))
                throw new InvalidDataException($"Kanji table: invalid or duplicate ordinal at slot {i}");
            slots[i] = id;
            if (id >= 0) seen[id] = true;
        }
        // An observed export invariant, not a claim that the retail consumer validates it.
        if (seen.Any(x => !x)) throw new InvalidDataException("Kanji table: missing image ordinal");
        Slots = Array.AsReadOnly(slots);
    }

    /// <summary>Exact half-open ranges from 0x12fee8; this is not a general Shift-JIS validator.</summary>
    public static int SlotForCode(ushort code) => code switch
    {
        >= 0x8140 and < 0x84bf => code - 0x8140,
        >= 0x8540 and < 0x8797 => code - 0x8540 + 0x37f,
        >= 0x889f and < 0x9873 => code - 0x889f + 0x5d6,
        _ => -1
    };

    public static ushort CodeForSlot(int slot)
    {
        if (slot < 0 || slot >= MaximumSlots) throw new ArgumentOutOfRangeException(nameof(slot));
        return (ushort)(slot < 0x37f ? slot + 0x8140 :
            slot < 0x5d6 ? slot - 0x37f + 0x8540 : slot - 0x5d6 + 0x889f);
    }

    /// <summary>Returns the export ordinal; false for a hole or a code outside the stored extent.
    /// Unlike the unchecked ELF lookup, this never reads beyond the file or converts -1 to 570.</summary>
    public bool TryGetImageOrdinal(ushort shiftJis, out int ordinal)
    {
        int slot = SlotForCode(shiftJis);
        ordinal = slot >= 0 && slot < Slots.Count ? Slots[slot] : -1;
        return ordinal >= 0;
    }
}
