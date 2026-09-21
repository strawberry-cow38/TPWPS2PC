using System.Buffers.Binary;
using System.IO.Compression;
using TPW.PS2.Data;

internal static class ReferenceImage
{
    public static Targa Read(string path) => Read(File.ReadAllBytes(path));

    internal static Targa Read(byte[] data)
    {
        // UI/UltimateC/Star.tga is actually an 8-bit RGBA PNG. Keep it in the population
        // without an external decoder. Reject PNG features not implemented here.
        if (!data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return new Targa(data);
        using var compressed = new MemoryStream();
        int offset = 8, width = 0, height = 0;
        bool header = false, ended = false, sawData = false;
        while (offset <= data.Length - 12)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset)));
            if (length > data.Length - offset - 12) throw new InvalidDataException("Truncated PNG chunk.");
            var type = data.AsSpan(offset + 4, 4);
            var body = data.AsSpan(offset + 8, length);
            uint crc = Crc(data.AsSpan(offset + 4, length + 4));
            if (crc != BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 8 + length)))
                throw new InvalidDataException("PNG chunk CRC mismatch.");
            if (!header && !type.SequenceEqual("IHDR"u8)) throw new InvalidDataException("PNG must start with IHDR.");
            if (type.SequenceEqual("IHDR"u8))
            {
                if (header || length != 13) throw new InvalidDataException("Invalid PNG header.");
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(body));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]));
                if (width is <= 0 or > 65535 || height is <= 0 or > 65535 ||
                    body[8] != 8 || body[9] != 6 || body[10] != 0 || body[11] != 0 || body[12] != 0)
                    throw new NotSupportedException("PNG reference reader supports only noninterlaced 8-bit RGBA.");
                header = true;
            }
            else if (type.SequenceEqual("IDAT"u8)) { compressed.Write(body); sawData = true; }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0 || !sawData) throw new InvalidDataException("Invalid PNG end.");
                ended = true;
            }
            else if ((type[0] & 32) == 0 && !type.SequenceEqual("PLTE"u8))
                throw new NotSupportedException("Unknown critical PNG chunk.");
            offset += length + 12;
            if (ended) break;
        }
        if (!ended || offset != data.Length) throw new InvalidDataException("Missing PNG end or trailing data.");
        int stride = checked(width * 4);
        var raw = new byte[checked((stride + 1) * height)];
        compressed.Position = 0;
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            zlib.ReadExactly(raw);
            if (zlib.ReadByte() != -1) throw new InvalidDataException("Extra PNG scanline bytes.");
        }
        // A top-origin BGRA TGA retains the scorer's existing image interface.
        var tga = new byte[checked(18 + stride * height)];
        tga[2] = 2; tga[16] = 32; tga[17] = 0x28;
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(14), (ushort)height);
        for (int y = 0; y < height; y++)
        {
            int row = y * (stride + 1), filter = raw[row];
            if (filter > 4) throw new InvalidDataException("Unknown PNG filter.");
            for (int x = 0; x < stride; x++)
            {
                int at = row + 1 + x;
                int a = x >= 4 ? raw[at - 4] : 0;
                int b = y > 0 ? raw[at - stride - 1] : 0;
                int c = y > 0 && x >= 4 ? raw[at - stride - 5] : 0;
                int predictor = filter switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2, _ => Paeth(a, b, c) };
                raw[at] = unchecked((byte)(raw[at] + predictor));
                int channel = x % 4;
                tga[18 + y * stride + x - channel + (channel == 0 ? 2 : channel == 2 ? 0 : channel)] = raw[at];
            }
        }
        return new Targa(tga);
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    internal static uint Crc(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        return ~crc;
    }
}
