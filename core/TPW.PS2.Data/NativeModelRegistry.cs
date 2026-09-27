using System.Buffers.Binary;

namespace TPW.PS2.Data;

/// <summary>⭐⭐ THE MODEL REGISTRY `0x2BF2B8`: 36-byte entries `{name, world, category, id, variant,
/// name override, ...}` that turn a model ID (what the native code asks for) into a folder on the
/// disc. Read from the owner's PAL executable, not copied.
///
/// - Lookup `0x17D7E8(id)`, READ (MIPS `0x17D7E8..0x17D8E4`, findings/coaster-operation.md §8.4,
///   staff-person.md §5.6): the FIRST entry with `+0xC == id` whose world is the park's world or 4
///   (shared) and whose variant `(+0x10 &amp; ~8)` is 0 or the PARK INDEX + 1 -- or, in park 2, any
///   entry with variant bit 8. This is the entertainer costume rule: every entertainer asks for 424
///   and gets dino/hunter, franky/vampire, flower/gnome or spaceman/alien by world and park.
/// - Folder `0x17B240`, READ (MIPS `0x17B2F4..0x17B374`, findings/bus-native-catalogue.md): the
///   category row `0x2BF218 + type*8` is `{flags, name}`; flag 0x40 takes the base `[0x2BF2B0]`
///   ("Data") instead of the world folder `[0x2BF2A0 + world*4]`; flag 1 formats
///   `"%s\%s\%s"` (base, category, entry name or its `+0x14` override), else `"%s\%s"` (base,
///   category or the override).
///
/// ⚠ TWO ADAPTERS, said here once:
/// - The entry COUNT `[0x2C3300]` is set at run time (it reads 0 in the file). The table ends
///   with an entry named `EOL` -- the sentinel `0x17D7E8(0)` finds (coaster-operation.md §8.5) --
///   so the port reads up to it.
/// - The FILE inside that folder is not composed by `0x17B240`; the loader's step was not traced.
///   <see cref="ModelPath"/> takes `name.mps` in the folder, which is where every staff and litter
///   model sits on the disc (`/Chars/Handyman/handyman.mps`, `/Generic/MiscMesh/litter1.mps`),
///   matched without case as the archive's own `Find` does.</summary>
public sealed class NativeModelRegistry
{
    public const uint TableAddress = 0x2BF2B8, CategoryTable = 0x2BF218, WorldTable = 0x2BF2A0,
                      SharedBase = 0x2BF2B0;
    public const int EntrySize = 0x24, CategoryCount = 16;
    /// <summary>World 4 is "every world" (`+4 == 4` passes the world test).</summary>
    public const int AnyWorld = 4;

    /// <summary>One registry row. <paramref name="Variant"/> is `+0x10` raw (bit 8 = the test park).</summary>
    public sealed record Entry(int Index, string Name, int World, int Category, int Id, int Variant, string Override);
    public sealed record Category(int Type, uint Flags, string Name);

    public IReadOnlyList<Entry> Entries { get; }
    public IReadOnlyList<Category> Categories { get; }
    /// <summary>`0x2BF2A0`: Data\Jungle, Data\Hallow, Data\Fantasy, Data\Space.</summary>
    public IReadOnlyList<string> Worlds { get; }
    /// <summary>`[0x2BF2B0]`, the base a flag-0x40 category uses instead of the world folder.</summary>
    public string Shared { get; }

    public static NativeModelRegistry Read(Disc disc)
    {
        var entry = disc.Files().SingleOrDefault(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        if (entry == null) throw new InvalidDataException("the model registry needs the PAL SLES_500.32 executable");
        return new NativeModelRegistry(disc.Read(entry.Extent, entry.Size));
    }

    public NativeModelRegistry(byte[] elf)
    {
        if (elf.Length < 52 || !elf.AsSpan(0, 6).SequenceEqual(new byte[] { 127, 69, 76, 70, 1, 1 }))
            throw new InvalidDataException("the model registry requires a little-endian ELF32 executable");
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off, 4));
        int U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(off, 2));
        int? Offset(uint va)
        {
            int ph = checked((int)U32(28));
            for (int i = 0; i < U16(44); i++)
            {
                int p = ph + i * U16(42);
                if (U32(p) == 1 && va >= U32(p + 8) && va < (ulong)U32(p + 8) + U32(p + 16))
                    return checked((int)(U32(p + 4) + va - U32(p + 8)));
            }
            return null;
        }
        uint Word(uint va) => Offset(va) is int o ? U32(o)
            : throw new InvalidDataException($"model registry address 0x{va:x} is not in a file-backed PT_LOAD");
        string Text(uint va)
        {
            if (va == 0) return null;
            if (Offset(va) is not int o) throw new InvalidDataException($"string 0x{va:x} is not file-backed");
            int end = Array.IndexOf(elf, (byte)0, o);
            if (end < 0 || end - o > 64) throw new InvalidDataException($"string 0x{va:x} is not terminated within 64 bytes");
            return System.Text.Encoding.Latin1.GetString(elf, o, end - o);
        }

        var categories = new List<Category>();
        for (int t = 0; t < CategoryCount; t++)
            categories.Add(new Category(t, Word(CategoryTable + (uint)t * 8), Text(Word(CategoryTable + (uint)t * 8 + 4))));
        Categories = categories;
        Worlds = Enumerable.Range(0, 4).Select(w => Text(Word(WorldTable + (uint)w * 4))).ToArray();
        Shared = Text(Word(SharedBase));

        var entries = new List<Entry>();
        for (int i = 0; ; i++)
        {
            if (i > 4096) throw new InvalidDataException("no EOL entry within 4096 registry rows");
            uint at = TableAddress + (uint)(i * EntrySize);
            string name = Text(Word(at));
            if (name == "EOL") break;
            int category = checked((int)Word(at + 8));
            if ((uint)category >= CategoryCount) throw new InvalidDataException($"registry row {i} ({name}) has category {category}");
            entries.Add(new Entry(i, name, checked((int)Word(at + 4)), category, checked((int)Word(at + 0xC)),
                                  checked((int)Word(at + 0x10)), Text(Word(at + 0x14))));
        }
        Entries = entries;
    }

    /// <summary>⭐ `0x17D7E8(id)` for the park `(world, park)`: the first entry that passes, or null.
    /// `park` is the index `[0x3952E8]` (0 or 1 for the ordinary parks, 2 the test park).</summary>
    public Entry Find(int id, int world, int park)
    {
        foreach (var e in Entries)
        {
            if (e.Id != id) continue;
            bool test = park == 2 && (e.Variant & 8) != 0;
            bool worldOk = e.World == world || e.World == AnyWorld || test;
            int variant = e.Variant & ~8;
            bool parkOk = variant == 0 || variant == park + 1 || test;
            if (worldOk && parkOk) return e;
        }
        return null;
    }

    /// <summary>`0x17B240`'s folder for an entry, backslashed as the executable writes it, e.g.
    /// `Data\Chars\Handyman` or `Data\Generic\MiscMesh`.</summary>
    public string Directory(Entry e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var c = Categories[e.Category];
        string root = (c.Flags & 0x40) != 0 ? Shared
            : (uint)e.World < (uint)Worlds.Count ? Worlds[e.World]
            : throw new InvalidOperationException($"{e.Name}: world {e.World} with no flag-0x40 category has no folder");
        return (c.Flags & 1) != 0 ? $@"{root}\{c.Name}\{e.Override ?? e.Name}" : $@"{root}\{e.Override ?? c.Name}";
    }

    /// <summary>The archive and path of an entry's model: `Data\...` is DATA.WAD, `Data\Jungle\...`
    /// the world's WAD, and the file is `name.mps` in the folder (⚠ see the class summary).
    /// <paramref name="Archive"/> is "DATA" or the world's name (JUNGLE, HALLOW, FANTASY, SPACE).</summary>
    public (string Archive, string Path) ModelPath(Entry e)
    {
        var parts = Directory(e).Split('\\');
        if (parts.Length < 2 || !parts[0].Equals("Data", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{e.Name}: folder {Directory(e)} is not under Data");
        int skip = 1;
        string archive = "DATA";
        if ((Categories[e.Category].Flags & 0x40) == 0) { archive = parts[1].ToUpperInvariant(); skip = 2; }
        return (archive, "/" + string.Join("/", parts.Skip(skip)) + "/" + e.Name + ".mps");
    }
}
