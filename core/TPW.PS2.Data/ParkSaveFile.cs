using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

/// <summary>Versioned, bounded transport for explicit snapshot DTOs. Does NOT capture a park.
/// The coordinator must capture all owners at one safe tick boundary and validate a complete
/// new world before replacing live state. Never deserialize executable type names/delegates.</summary>
public static class ParkSaveFile
{
    public const int Version = 1;
    public const int MaximumPayloadBytes = 64 * 1024 * 1024;
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("TPWSAVE1");
    static readonly JsonSerializerOptions Json = new()
    {
        MaxDepth = 64,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>Writes beside the destination, flushes, then replaces it with a retained .bak.
    /// Exceptions before replacement leave the old file alone. Files are player data, not disc assets.</summary>
    public static void Write<T>(string path, T snapshot) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(snapshot);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, Json);
        if (payload.Length == 0 || payload.Length > MaximumPayloadBytes)
            throw new InvalidDataException("Snapshot is larger than the supported save-file limit.");
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(Magic); writer.Write(Version); writer.Write(payload.Length);
                    writer.Write(SHA256.HashData(payload)); writer.Write(payload); writer.Flush();
                }
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(full)) File.Replace(temporary, full, full + ".bak");
            else File.Move(temporary, full);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Checks envelope/version/length/hash/JSON before returning a DTO. This never mutates
    /// an owner, silently repairs a corrupt save, loads a backup, or executes data from the file.</summary>
    public static T Read<T>(string path) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        const int HeaderSize = 8 + 4 + 4 + 32;
        if (stream.Length < HeaderSize || stream.Length > HeaderSize + (long)MaximumPayloadBytes)
            throw new InvalidDataException("Save-file length is invalid or unsupported.");
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Not a TPW park save.");
        if (reader.ReadInt32() != Version) throw new InvalidDataException("Unsupported save-file version.");
        int length = reader.ReadInt32();
        if (length <= 0 || length > MaximumPayloadBytes || stream.Length != HeaderSize + (long)length)
            throw new InvalidDataException("Save payload is truncated, oversized or has trailing data.");
        byte[] hash = reader.ReadBytes(32);
        byte[] payload = reader.ReadBytes(length);
        if (payload.Length != length || !CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(payload)))
            throw new InvalidDataException("Save payload checksum does not match.");
        try { return JsonSerializer.Deserialize<T>(payload, Json) ?? throw new InvalidDataException("Empty save payload."); }
        catch (JsonException e) { throw new InvalidDataException("Save payload does not match its snapshot schema.", e); }
    }
}
