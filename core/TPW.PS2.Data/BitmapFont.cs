using System.Buffers.Binary;

namespace TPW.PS2.Data;

/// <summary>The PS2 <c>2FFB</c> bitmap font, not the PC <c>F4FB</c> format.
/// Three range arrays map 16-bit character codes to 14-byte glyph descriptors. Pixels are
/// continuous, high-nibble-first, four-bit coverage. See findings/bff-font.md for the loader
/// and consumer addresses, and for fields whose meanings remain unknown.</summary>
public sealed class BitmapFont
{
    public byte Version { get; }
    /// <summary>Header +5. Meaning not established; do not use as the line advance.</summary>
    public byte Metric05 { get; }
    /// <summary>Header +6. Meaning not established.</summary>
    public byte Metric06 { get; }
    /// <summary>Header +7: vertical pen movement between lines, in pixels.</summary>
    public byte LineAdvance { get; }
    /// <summary>Header +8..9, ignored by the loader. Padding is an inference.</summary>
    public ushort Reserved08 { get; }
    /// <summary>High byte of each chunk's size word. Both are 1 on this disc; meaning unknown.</summary>
    public byte MapChunkTag { get; }
    public byte GlyphChunkTag { get; }
    public int GlyphDataOffset { get; }
    public IReadOnlyList<CharacterRange> Ranges { get; }
    public IReadOnlyList<Glyph> Glyphs { get; }

    /// <summary>Inclusive code range. Glyph index = code - Subtract (no 16-bit wrapping).</summary>
    public sealed record CharacterRange(ushort Start, ushort End, ushort Subtract);

    /// <param name="Coverage">One byte per pixel, 0..15, left to right and top to bottom.
    /// Rows have no padding, even when Width is odd.</param>
    /// <param name="Left">Signed offset from the horizontal pen position.</param>
    /// <param name="Top">Signed offset down from the line origin, not from a baseline.</param>
    /// <param name="Advance">Horizontal pen movement, independent of bitmap width.</param>
    /// <param name="Unknown13">Last descriptor byte. Possibly padding; not interpreted.</param>
    public sealed record Glyph(int Index, int Width, int Height, int Left, int Top, int Advance,
        uint Flags, uint RelativeDataOffset, byte Unknown13, ReadOnlyMemory<byte> Coverage);

    public BitmapFont(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidDataException("BFF: " + message);
        }
        ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at, 2));
        uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));

        Require(data.Length >= 20, "truncated header");
        Require(data.AsSpan(0, 4).SequenceEqual("2FFB"u8), "expected 2FFB magic");
        Version = data[4];
        Require(Version == 1, "unsupported version");
        Metric05 = data[5]; Metric06 = data[6]; LineAdvance = data[7];
        Reserved08 = U16(8);
        Require(data.AsSpan(10, 4).SequenceEqual("ULGU"u8), "expected ULGU map chunk");
        uint mapWord = U32(14);
        MapChunkTag = (byte)(mapWord >> 24);
        int n = U16(18), mapBytes = n * 6;
        Require(n > 0 && (mapWord & 0xffffff) == mapBytes, "invalid map size");
        int chunk = 20 + mapBytes;
        Require(chunk <= data.Length - 8, "truncated ranges/glyph chunk");
        Require(data.AsSpan(chunk, 4).SequenceEqual("DGFB"u8), "expected DGFB glyph chunk");
        uint glyphWord = U32(chunk + 4);
        GlyphChunkTag = (byte)(glyphWord >> 24);
        Require(MapChunkTag == 1 && GlyphChunkTag == 1, "unsupported chunk tag");
        GlyphDataOffset = chunk + 8;
        int glyphBytes = (int)(glyphWord & 0xffffff);
        Require(glyphBytes == data.Length - GlyphDataOffset, "glyph chunk size / EOF mismatch");

        var ranges = new CharacterRange[n];
        int glyphCount = 0;
        for (int i = 0; i < n; i++)
        {
            var range = new CharacterRange(U16(20 + (n + i) * 2), U16(20 + i * 2),
                U16(20 + (2 * n + i) * 2));
            Require(range.Start <= range.End && (i == 0 || ranges[i - 1].End < range.Start),
                $"unordered/overlapping range {i}");
            Require(range.Start >= range.Subtract, $"negative glyph index in range {i}");
            glyphCount = Math.Max(glyphCount, range.End - range.Subtract + 1);
            ranges[i] = range;
        }
        // There is no glyph-count field. Derive the descriptor extent from mapped indices.
        int descriptorBytes = glyphCount * 14;
        Require(descriptorBytes <= glyphBytes, "mapped glyph outside descriptor table");
        var glyphs = new Glyph[glyphCount];
        for (int i = 0; i < glyphCount; i++)
        {
            int at = GlyphDataOffset + i * 14;
            uint flags = U32(at), relative = U32(at + 4);
            // The PS2 decoder rejects bit 0. No shipped glyph sets any bits. Do not import
            // PC packing modes or assign a meaning to the unused PS2 RLE helper.
            Require(flags == 0, $"unsupported flags 0x{flags:x} at glyph {i}");
            int width = data[at + 8], height = data[at + 9], pixels = width * height;
            int stored = (pixels + 1) / 2;
            var coverage = new byte[pixels];
            if (pixels != 0)
            {
                long start = (long)at + relative;
                Require(start >= GlyphDataOffset + descriptorBytes && start <= data.Length - stored,
                    $"bitmap outside glyph payload at glyph {i}");
                for (int pixel = 0; pixel < pixels; pixel++)
                {
                    byte packed = data[(int)start + pixel / 2];
                    coverage[pixel] = (byte)((pixel & 1) == 0 ? packed >> 4 : packed & 15);
                }
            }
            glyphs[i] = new Glyph(i, width, height, unchecked((sbyte)data[at + 10]),
                unchecked((sbyte)data[at + 11]), data[at + 12], flags, relative, data[at + 13], coverage);
        }
        Ranges = Array.AsReadOnly(ranges);
        Glyphs = Array.AsReadOnly(glyphs);
    }

    /// <summary>Look up a raw 16-bit code; false for a gap, with no automatic fallback.
    /// European fonts (including Jap/Console) use Unicode codes. Jap/Large and Jap/Small
    /// use packed Shift-JIS codes, e.g. 0x82a0 for あ. Encoding is not declared in the header.</summary>
    public bool TryGetGlyph(ushort code, out Glyph glyph)
    {
        int lo = 0, hi = Ranges.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (Ranges[mid].End < code) lo = mid + 1;
            else hi = mid;
        }
        if (lo == Ranges.Count || code < Ranges[lo].Start)
        {
            glyph = null;
            return false;
        }
        glyph = Glyphs[code - Ranges[lo].Subtract];
        return true;
    }
}
