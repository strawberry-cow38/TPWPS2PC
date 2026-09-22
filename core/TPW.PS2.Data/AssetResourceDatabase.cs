using System.Buffers.Binary;

namespace TPW.PS2.Data;

/// <summary>ars{,us,jap}db.dba: indexed park asset definitions, NOT advisor speech.
/// Retail payload layouts and their confirmed consumers are documented in findings/dba.md.
/// Unknown fields are preserved; this is not a complete park simulation.</summary>
public sealed class AssetResourceDatabase
{
    public enum AssetKind : ushort
    {
        Coaster = 1, Feature = 2, Ride = 3, Shop = 4, Sideshow = 5,
        TrackRide = 6, TourRide = 7, TrackUpgrade = 8
    }

    public readonly record struct Connection(short X, short Z, byte Direction)
    {
        public bool IsPresent => X >= 0 && Z >= 0;
    }

    public readonly record struct FootprintCell(short TileId, ushort RawFlags)
    {
        // 0x1e2d04 branches on SIGN, not equality with FFFF. All retail cells take this branch.
        public bool UsesDefaultTerrain => TileId < 0;
        public int QuarterTurns => RawFlags & 3;
        public bool TerrainBit14 => (RawFlags & 4) != 0;
        public bool TerrainBit15 => (RawFlags & 8) != 0;
    }

    public record Economy(int PurchaseCost, int ResearchWork, int ResearchGroup);
    public record RideTier(uint UnknownFlags, int MinSpeedDamage, int MinCapacityDamage,
        int WearRate, int CapacityParameter, int InitialCondition, int MinSpeed, int MaxSpeed,
        int MinDuration, int MaxDuration, int ResearchGroup, int ResearchWork, int PurchaseCost);
    public record ShopSettings(ushort InitialPrice, ushort BaseCostOfGoods, byte Product,
        byte Unknown31, byte HungerReduction, byte ThirstReduction, byte HappinessEffect,
        byte Unknown35, byte VomitIncrease, byte Unknown37);
    public record SideshowSettings(ushort InitialPrice, ushort PrizeValue, byte WinPercentage,
        ReadOnlyMemory<byte> Unknown31To33);

    public record Entry(uint Key, int Offset, ReadOnlyMemory<byte> Payload)
    {
        uint U32(int p) => BinaryPrimitives.ReadUInt32LittleEndian(Payload.Span[p..]);
        int I32(int p) => BinaryPrimitives.ReadInt32LittleEndian(Payload.Span[p..]);
        ushort U16(int p) => BinaryPrimitives.ReadUInt16LittleEndian(Payload.Span[p..]);
        short I16(int p) => BinaryPrimitives.ReadInt16LittleEndian(Payload.Span[p..]);
        byte B(int p) => Payload.Span[p];

        public uint RawKindWord => U32(0);
        public AssetKind Kind => (AssetKind)U16(0);
        /// <summary>Zero on disc; overwritten with the category-local index at 0x12a710.</summary>
        public ushort RuntimeCategoryIndex => U16(2);
        public uint TextRow => U32(4);
        public byte Width => B(8);
        public byte UnknownHeightByte => B(9);
        public byte Depth => B(10);
        public byte Unknown0B => B(11);
        public Connection ConnectionA => new(I16(12), I16(14), B(20));
        public Connection ConnectionB => new(I16(16), I16(18), B(21));
        public ushort Minigame => U16(22);
        public int BaseExcitement => I32(24);
        public int TypeDataLength => checked((int)U32(28));
        public int FootprintOffset => 32 + TypeDataLength;
        public ReadOnlyMemory<byte> TypeData => Payload.Slice(32, TypeDataLength);
        public ReadOnlyMemory<byte> FootprintBytes => Payload[FootprintOffset..];
        public bool HasRideTiers => Kind is AssetKind.Coaster or AssetKind.Ride or AssetKind.TrackRide or AssetKind.TourRide;

        /// <summary>Null for ride layouts, whose economy is tier-specific.</summary>
        public Economy SimpleEconomy => HasRideTiers ? null : new(I32(32), I32(36), I32(40));

        public RideTier Tier(int index)
        {
            if (!HasRideTiers) throw new InvalidOperationException("This DBA kind has no ride tiers");
            if ((uint)index >= 3) throw new ArgumentOutOfRangeException(nameof(index));
            int p = 32 + index * 52;
            return new(U32(p), I32(p + 4), I32(p + 8), I32(p + 12), I32(p + 16),
                I32(p + 20), I32(p + 24), I32(p + 28), I32(p + 32), I32(p + 36),
                I32(p + 40), I32(p + 44), I32(p + 48));
        }

        /// <summary>Kind-specific bytes after the tiers or simple economy. Partly unresolved.</summary>
        public ReadOnlyMemory<byte> Extra => Payload.Slice(HasRideTiers ? 0xbc : 0x2c,
            FootprintOffset - (HasRideTiers ? 0xbc : 0x2c));
        public ShopSettings Shop => Kind != AssetKind.Shop ? null : new(U16(44), U16(46),
            B(48), B(49), B(50), B(51), B(52), B(53), B(54), B(55));
        public SideshowSettings Sideshow => Kind != AssetKind.Sideshow ? null :
            new(U16(44), U16(46), B(48), Payload.Slice(49, 3));
        /// <summary>Service classification bits; observed identities and consumer masks are in the findings.</summary>
        public byte? RawFeatureFlags => Kind == AssetKind.Feature ? B(46) : null;

        public FootprintCell Cell(int x, int z)
        {
            if ((uint)x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)z >= Depth) throw new ArgumentOutOfRangeException(nameof(z));
            int p = FootprintOffset + 4 * (z * Width + x);
            return new(I16(p), U16(p + 2));
        }

        internal void Validate()
        {
            if (Payload.Length < 32) throw new InvalidDataException($"DBA {Key:x}: truncated common header");
            // Exact retail variant extents, independently checked in all three regional files.
            int expected = Kind switch {
                AssetKind.Coaster => 0xb4, AssetKind.Feature => 0x10, AssetKind.Ride => 0xa0,
                AssetKind.Shop => 0x18, AssetKind.Sideshow => 0x14, AssetKind.TrackRide => 0xe8,
                AssetKind.TourRide => 0xb8, AssetKind.TrackUpgrade => 0x10,
                _ => throw new InvalidDataException($"DBA {Key:x}: unsupported kind {U16(0)}") };
            if (U32(28) != expected) throw new InvalidDataException($"DBA {Key:x}: wrong type-data extent for {Kind}");
            if (Width == 0 || Depth == 0 || Payload.Length != 32 + expected + 4 * Width * Depth)
                throw new InvalidDataException($"DBA {Key:x}: footprint does not exactly fill payload");
        }
    }
    public IReadOnlyList<Entry> Entries { get; }
    public ReadOnlyMemory<byte> DirectoryGap { get; }

    public AssetResourceDatabase(byte[] data)
    {
        if (data.Length < 4) throw new InvalidDataException("Truncated DBA count");
        uint n = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (n == 0 || n > (data.Length - 4) / 12) throw new InvalidDataException("Invalid DBA directory");
        var owned = (byte[])data.Clone();
        var entries = new List<Entry>();
        int previousEnd = 4 + (int)n * 12;
        for (int i = 0; i < n; i++)
        {
            var d = owned.AsSpan(4 + i * 12);
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(d);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(d[4..]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(d[8..]);
            if (offset > owned.Length || size < 8 || size > owned.Length - offset || offset < previousEnd ||
                (i > 0 && offset != previousEnd)) throw new InvalidDataException($"Invalid DBA span at entry {i}");
            if (i == 0) DirectoryGap = owned.AsMemory(previousEnd, (int)offset - previousEnd);
            var entry = new Entry(key, (int)offset, owned.AsMemory((int)offset, (int)size));
            entry.Validate();
            entries.Add(entry);
            previousEnd = (int)(offset + size);
        }
        if (previousEnd != owned.Length) throw new InvalidDataException("Unconsumed DBA bytes");
        Entries = entries.AsReadOnly();
    }

    /// <summary>The executable scans in file order and returns the FIRST matching key.
    /// Retail includes two different payloads keyed FFFFFFFF; do not collapse them in a dictionary.</summary>
    public Entry Find(uint key) => Entries.FirstOrDefault(e => e.Key == key);
}
