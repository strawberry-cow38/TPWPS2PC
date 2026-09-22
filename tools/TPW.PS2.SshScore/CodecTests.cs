using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using TPW.PS2.Data;

internal static class CodecTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Synthetic MPEG-1 recipes, SHA-256 of the 384-byte YUV output independently
        // decoded by the specified FFmpeg 7.0.2 AArch64 reference. No game bytes.
        (string Name, string Ac, int Quantiser, string Sha256)[] vectors =
        [
            ("dc", "", 1, "F83545D43C6939EC393B6B8310959B6174FD764B08A12FC22D908408A7E6A43E"),
            ("positive", "110", 15, "68737D3FE471D46F5C29B54D5B2B19FD7061E5253F47F1A2949D6948A99D9B7F"),
            ("negative", "111", 15, "37B6EF866CE45975C7F4BB9BDD1A08508D30FF9BAF5BEED08086930244005F1E"),
            ("escape_zero", "0000010000000000000011001000", 31, "64B11751E24E4317B8F26990D848CF5874CBEFBD5BEEFD81B1369DFAD778F38F"),
            ("escape_128", "0000010000001000000000111000", 31, "0BF89F66FC96538A35AC2E5796CE8CFC8DF5F87F697868D4A734316943317503"),
            ("last_ac", "00000111111000000111", 17, "B4E94E9948F263A585DD613ECD9D56B8A500A227CAEDB7F5816FADD27ACACC2E"),
            ("vertical_dc", "00000100000101100100", 31, "3A2F9BD35E60D1608C8D32B821F01421C63B798C1854F437955F1706AEB5693A"),
            ("mixed", "11001110100000000100001011111001", 9, "43EF3FF206986C9634692A4586163F32FE03C709B4908A4AA849652054FCCCEF"),
        ];
        foreach (var v in vectors)
        {
            byte[] stream = Stream(v.Ac, v.Quantiser);
            byte[] decoded = IpuDecoder.Decode(stream, 16, 16);
            check(Convert.ToHexString(SHA256.HashData(decoded)) == v.Sha256, "synthetic YUV: " + v.Name);
            for (int length = 0; length < stream.Length; length++)
            {
                bool rejected = false;
                try { IpuDecoder.Decode(stream.AsSpan(0, length), 16, 16); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected, "every truncated coefficient prefix is rejected");
            }
        }
        foreach (byte[] invalid in new[]
        {
            Stream("00000111111100000001", 1), // run takes AC beyond coefficient 63
            Stream("0000000000000000", 1), // no valid VLC
            Stream("", 1).Concat(new byte[] { 0 }).ToArray(), // unconsumed stream byte
            new byte[8] // invalid macroblock type
        })
        {
            bool rejected = false;
            try { IpuDecoder.Decode(invalid, 16, 16); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "invalid IPU stream rejected");
        }
        Png(check);
        Paletted(check);
    }

    static byte[] Stream(string ac, int quantiser)
    {
        string bits = "01" + Convert.ToString(quantiser, 2).PadLeft(5, '0') + "100" + ac + "10" +
            "100101001010010" + "00100010";
        bits = bits.PadRight((bits.Length + 7) / 8 * 8, '0');
        return Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
    }

    static void Png(Action<bool, string> check)
    {
        const int width = 3, height = 5, stride = width * 4;
        byte[] rgba = Enumerable.Range(0, stride * height).Select(i => (byte)(i * 73 + 17)).ToArray();
        byte[] filtered = new byte[(stride + 1) * height];
        // Each row uses a different PNG filter; all predictors cross RGBA channel boundaries.
        for (int y = 0; y < height; y++)
        {
            filtered[y * (stride + 1)] = (byte)y;
            for (int x = 0; x < stride; x++)
            {
                int i = y * stride + x;
                int left = x < 4 ? 0 : rgba[i - 4], up = y == 0 ? 0 : rgba[i - stride];
                int corner = x < 4 || y == 0 ? 0 : rgba[i - stride - 4];
                int p = left + up - corner;
                int paeth = new[] { left, up, corner }.OrderBy(v => Math.Abs(p - v)).First();
                int prediction = y switch { 0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2, _ => paeth };
                filtered[y * (stride + 1) + x + 1] = unchecked((byte)(rgba[i] - prediction));
            }
        }
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string type, byte[] body)
        {
            byte[] chunk = new byte[body.Length + 12];
            BinaryPrimitives.WriteInt32BigEndian(chunk, body.Length);
            System.Text.Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
            body.CopyTo(chunk, 8);
            BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ReferenceImage.Crc(chunk.AsSpan(4, body.Length + 4)));
            png.Write(chunk);
        }
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true)) z.Write(filtered);
        byte[] data = compressed.ToArray();
        Chunk("IDAT", data[..(data.Length / 2)]); Chunk("IDAT", data[(data.Length / 2)..]);
        Chunk("IEND", []);
        byte[] encoded = png.ToArray();
        var result = ReferenceImage.Read(encoded);
        check(result.Width == width && result.Height == height && result.Pixels.SequenceEqual(rgba),
            "PNG all five filters, split IDAT, straight RGBA, top-origin rows");
        encoded[29] ^= 1;
        bool rejected = false;
        try { ReferenceImage.Read(encoded); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "PNG damaged CRC rejected");
    }

    static void Paletted(Action<bool, string> check)
    {
        const int entry = 32, count = 256, palette = entry + 16 + count;
        byte[] data = new byte[palette + 1040];
        "SHPS"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), entry);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(entry), ((16 + count) << 8) | 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entry + 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entry + 6), 16);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(palette), (1040 << 8) | 0x21);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(palette + 4), 256);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(palette + 6), 1);
        for (int i = 0; i < count; i++)
        {
            data[entry + 16 + i] = (byte)i;
            data[palette + 16 + i * 4] = (byte)i;
            data[palette + 17 + i * 4] = (byte)(255 - i);
            data[palette + 18 + i * 4] = (byte)(i ^ 0x55);
            data[palette + 19 + i * 4] = (byte)i;
        }
        var decoded = new Ssh(data);
        check(decoded.HasAlpha && decoded.Width == 16 && decoded.Height == 16, "paletted metadata");
        foreach (var (index, stored) in new[] { (0, 0), (7, 7), (8, 16), (15, 23), (16, 8),
            (23, 15), (24, 24), (31, 31), (32, 32), (255, 255) })
            check(decoded.Pixels.AsSpan(index * 4, 4).SequenceEqual(new byte[]
                { (byte)stored, (byte)(255 - stored), (byte)(stored ^ 0x55), (byte)Math.Min(255, stored * 2) }),
                "linear palette indices, CSM1 block edges, RGBA channel order, alpha saturation");
        void Reject(byte[] bad)
        {
            bool rejected = false;
            try { _ = new Ssh(bad); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException) { rejected = true; }
            check(rejected, "invalid paletted image rejected");
        }
        byte[] bad = (byte[])data.Clone(); bad[palette] = 0x22; Reject(bad);
        bad = (byte[])data.Clone(); bad[palette + 4] = 255; Reject(bad);
        bad = data[..^1]; BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(4), bad.Length); Reject(bad);
        bad = (byte[])data.Clone(); bad[entry + 1]--; Reject(bad);
        // A following directory entry is not allowed to supply this image's palette bytes.
        bad = (byte[])data.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(8), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(28), palette + 16);
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(palette + 16), (16 << 8) | 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bad.AsSpan(palette + 20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bad.AsSpan(palette + 22), 1);
        Reject(bad);
    }
}
