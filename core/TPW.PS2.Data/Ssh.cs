using System.Buffers.Binary;
using System.Text;

namespace TPW.PS2.Data;

/// <summary>EA SHPS compressed types 4/5 (GM), decoded in managed code.
/// Returns straight RGBA8, top row first. No files are written by this reader.
/// This is lossy texture compression: see findings/ssh.md for measured source-image errors.</summary>
public sealed class Ssh
{
    public sealed record Entry(string Name, byte Type, int Width, int Height, int Offset, int Length);

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public bool HasAlpha { get; }

    public Ssh(byte[] data, int entryIndex = 0)
    {
        var entries = ReadEntries(data);
        if ((uint)entryIndex >= entries.Count) throw new ArgumentOutOfRangeException(nameof(entryIndex));
        var entry = entries[entryIndex];
        Width = entry.Width;
        Height = entry.Height;
        if (entry.Type is not (0x84 or 0x85))
            throw new NotSupportedException($"SHPS type 0x{entry.Type:X2}; only compressed types 4/5 are supported.");

        var gm = data.AsSpan(entry.Offset + 16, entry.Length - 16);
        if (gm.Length < 8 || gm[0] != 'G' || gm[1] != 'M')
            throw new InvalidDataException("Compressed SHPS image has no GM header.");
        uint header = BinaryPrimitives.ReadUInt32LittleEndian(gm[4..]);
        int length = (int)(header & 0xfffff);
        // All 400 fixtures carry bit 23; bit 27 adds the alpha plane. Other flags are unverified.
        if ((header & 0xfff00000) is not (0x00800000 or 0x08800000))
            throw new NotSupportedException($"Unverified GM flags 0x{header >> 20:X3}.");
        if (length < 16 || length > gm.Length || (length & 15) != 0)
            throw new InvalidDataException("Invalid GM block length.");
        gm = gm[..length];
        int codedWidth = gm[2] * 16, codedHeight = gm[3] * 16;
        if (codedWidth == 0 || codedHeight == 0 ||
            codedWidth != Math.Max(16, Width) || codedHeight != Math.Max(16, Height))
            throw new NotSupportedException("GM dimensions do not match a supported SHPS image layout.");
        // Sub-macroblock images in the fixtures are 8x8 packed into the start of a 16x16 buffer.
        if ((Width < 16 || Height < 16) && (Width != 8 || Height != 8))
            throw new NotSupportedException("Only the measured 8x8 sub-macroblock layout is supported.");

        HasAlpha = (header & 0x08000000) != 0;
        if (HasAlpha != (entry.Type == 0x85))
            throw new InvalidDataException("SHPS type and GM alpha flag disagree.");
        int codedPixels = checked(codedWidth * codedHeight);
        int alphaOffset = HasAlpha ? length - codedPixels : length;
        if (alphaOffset < 16 || (alphaOffset & 15) != 0)
            throw new InvalidDataException("Invalid GM alpha extent.");
        // GM terminates the macroblock stream with 00 00 01 34, followed by QWC padding.
        // Do not search the alpha plane, where arbitrary pixel bytes can resemble markers.
        // The last marker whose tail is padding wins; FFmpeg checks exact macroblock consumption.
        int end = -1;
        for (int i = alphaOffset - 4; i >= Math.Max(8, alphaOffset - 19); i--)
        {
            if (gm[i] != 0 || gm[i + 1] != 0 || gm[i + 2] != 1 || gm[i + 3] != 0x34) continue;
            if (((i + 4 + 15) & ~15) != alphaOffset) continue;
            bool zeroPadding = true;
            for (int j = i + 4; j < alphaOffset; j++) zeroPadding &= gm[j] == 0;
            if (zeroPadding) { end = i; break; }
        }
        if (end < 0) throw new InvalidDataException("GM end marker or alignment padding is missing.");

        // GM carries MPEG-1 IPU coefficients directly, without transport/picture headers.
        byte[] yuv = IpuDecoder.Decode(gm[8..end], codedWidth, codedHeight);

        Pixels = new byte[checked(Width * Height * 4)];
        for (int pixel = 0; pixel < Width * Height; pixel++)
        {
            int x = pixel % Width, y = pixel / Width;
            int sx, sy;
            if (Width < 16)
            {
                sx = pixel % codedWidth;
                sy = pixel / codedWidth;
            }
            else
            {
                // The decoder lays stream macroblocks across rows; GM traverses image columns.
                int mb = (x / 16) * (codedHeight / 16) + y / 16;
                sx = (mb % (codedWidth / 16)) * 16 + x % 16;
                sy = (mb / (codedWidth / 16)) * 16 + y % 16;
            }
            int chroma = (sy / 2) * (codedWidth / 2) + sx / 2;
            int cb = yuv[codedPixels + chroma] - 128;
            int cr = yuv[codedPixels * 5 / 4 + chroma] - 128;
            int lum = (149 * Math.Max(0, yuv[sy * codedWidth + sx] - 16)) >> 6;
            // Documented IPU integer BT.601 conversion; nearest 2x2 chroma replication.
            // Arithmetic shifts and the separate term rounding matter.
            Pixels[pixel * 4] = Clamp((lum + ((204 * cr) >> 6) + 1) >> 1);
            Pixels[pixel * 4 + 1] = Clamp((lum + ((-104 * cr) >> 6) + ((-50 * cb) >> 6) + 1) >> 1);
            Pixels[pixel * 4 + 2] = Clamp((lum + ((258 * cb) >> 6) + 1) >> 1);
            // Alpha is an independent linear plane, not the IPU macroblock order.
            // Unlike RGB's packed 8x8 special case, alpha retains the coded row stride.
            Pixels[pixel * 4 + 3] = HasAlpha ? Clamp(gm[alphaOffset + y * codedWidth + x] * 2) : (byte)255;
        }
    }

    public static IReadOnlyList<Entry> ReadEntries(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || !data[..4].SequenceEqual("SHPS"u8))
            throw new InvalidDataException("Not an SHPS container.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != data.Length)
            throw new InvalidDataException("SHPS declared length does not match its buffer.");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        if (count == 0 || count > (data.Length - 16) / 8)
            throw new InvalidDataException("Invalid SHPS directory count.");
        int directoryEnd = 16 + (int)count * 8;
        var offsets = new int[count];
        for (int i = 0; i < offsets.Length; i++)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data[(20 + i * 8)..]);
            if (offset < directoryEnd || offset > data.Length - 16)
                throw new InvalidDataException("SHPS entry offset is out of bounds.");
            offsets[i] = (int)offset;
        }
        var sortedOffsets = offsets.Distinct().Order().ToArray();
        var result = new List<Entry>(offsets.Length);
        for (int i = 0; i < offsets.Length; i++)
        {
            int offset = offsets[i];
            int index = Array.BinarySearch(sortedOffsets, offset);
            int limit = index + 1 < sortedOffsets.Length ? sortedOffsets[index + 1] : data.Length;
            int length = data[offset + 1] | data[offset + 2] << 8 | data[offset + 3] << 16;
            if (length == 0) length = limit - offset;
            if (length < 16 || length > limit - offset)
                throw new InvalidDataException("SHPS image block overlaps another entry or ends outside the file.");
            int width = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 4)..]);
            int height = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 6)..]);
            if (width == 0 || height == 0)
                throw new InvalidDataException("SHPS dimensions must be positive.");
            string name = Encoding.Latin1.GetString(data.Slice(16 + i * 8, 4)).TrimEnd('\0');
            result.Add(new Entry(name, data[offset], width, height, offset, length));
        }
        return result.AsReadOnly();
    }

    static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

}
