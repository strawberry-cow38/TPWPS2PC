using System.Buffers.Binary;

namespace TPW.PS2.Data;

/// <summary>ars{,us,jap}db.dba: indexed park asset definitions, NOT advisor speech.
/// Only the directory and common prefix are interpreted. See findings/advisor.md.</summary>
public sealed class AssetResourceDatabase
{
    public record Entry(uint Key, int Offset, ReadOnlyMemory<byte> Payload)
    {
        public uint RawKindWord => BinaryPrimitives.ReadUInt32LittleEndian(Payload.Span);
        public uint TextRow => BinaryPrimitives.ReadUInt32LittleEndian(Payload.Span[4..]);
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
            entries.Add(new Entry(key, (int)offset, owned.AsMemory((int)offset, (int)size)));
            previousEnd = (int)(offset + size);
        }
        if (previousEnd != owned.Length) throw new InvalidDataException("Unconsumed DBA bytes");
        Entries = entries.AsReadOnly();
    }

    /// <summary>The executable scans in file order and returns the FIRST matching key.
    /// Retail includes two different payloads keyed FFFFFFFF; do not collapse them in a dictionary.</summary>
    public Entry Find(uint key) => Entries.FirstOrDefault(e => e.Key == key);
}
