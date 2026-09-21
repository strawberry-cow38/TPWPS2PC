using System.Buffers.Binary;
using System.Text;
using TPW.PS2.Data;

internal static class SelfTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            checks++;
            if (!condition) throw new Exception($"Self-test failed: {name}");
        }
        void Reject(byte[] data, string name)
        {
            bool rejected = false;
            try { _ = new Ssh(data); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException) { rejected = true; }
            Check(rejected, name);
        }
        try
        {
            CodecTests.Run(Check);
            byte[] data = Synthetic(32, 32, [32, 64, 96, 128], true);
            var image = new Ssh(data);
            // Four independent constant macroblocks have unique luminance; a raster/column mixup
            // exchanges the top-right and bottom-left quadrants. Alpha deliberately uses rows.
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    int mb = (x / 16) * 2 + y / 16;
                    int expected = ((((mb + 1) * 32 - 16) * 149 >> 6) + 1) >> 1;
                    int at = (y * 32 + x) * 4;
                    Check(image.Pixels[at] == expected && image.Pixels[at + 1] == expected && image.Pixels[at + 2] == expected,
                        "column macroblock placement and IPU colour arithmetic");
                    Check(image.Pixels[at + 3] == Math.Min(255, y * 4 + 128), "linear alpha placement and saturation");
                }
            var opaque = new Ssh(Synthetic(16, 16, [128], false));
            Check(opaque.Pixels.Chunk(4).All(p => p.SequenceEqual(new byte[] { 130, 130, 130, 255 })), "opaque exact synthetic flat");
            var small = new Ssh(Synthetic(8, 8, [128], false));
            Check(small.Pixels.Length == 8 * 8 * 4 && small.Pixels.Chunk(4).All(p => p[0] == 130), "sub-macroblock image size");
            var smallAlpha = new Ssh(Synthetic(8, 8, [128], true));
            Check(Enumerable.Range(0, 64).All(i => smallAlpha.Pixels[i * 4 + 3] == 128 + i / 8 * 4), "8x8 alpha retains coded stride");
            foreach (var dimensions in new[] { (64, 32), (32, 64) })
            {
                int width = dimensions.Item1, height = dimensions.Item2;
                int[] values = Enumerable.Range(0, 8).Select(i => 32 + i * 16).ToArray();
                var rectangle = new Ssh(Synthetic(width, height, values, false));
                for (int by = 0; by < height / 16; by++)
                    for (int bx = 0; bx < width / 16; bx++)
                    {
                        int source = bx * (height / 16) + by;
                        int expected = (((values[source] - 16) * 149 >> 6) + 1) >> 1;
                        Check(rectangle.Pixels[(by * 16 * width + bx * 16) * 4] == expected, "rectangular macroblock order");
                    }
            }
            // A reversed directory order must not change which data entryIndex selects.
            byte[] first = Synthetic(16, 16, [32], false), second = Synthetic(16, 16, [128], false);
            var multi = new byte[first.Length + second.Length - 32];
            first.CopyTo(multi, 0); second.AsSpan(32).CopyTo(multi.AsSpan(first.Length));
            BinaryPrimitives.WriteInt32LittleEndian(multi.AsSpan(4), multi.Length);
            BinaryPrimitives.WriteInt32LittleEndian(multi.AsSpan(8), 2);
            BinaryPrimitives.WriteInt32LittleEndian(multi.AsSpan(20), first.Length);
            "NEXT"u8.CopyTo(multi.AsSpan(24));
            BinaryPrimitives.WriteInt32LittleEndian(multi.AsSpan(28), 32);
            Check(Ssh.ReadEntries(multi).Count == 2 && new Ssh(multi, 0).Pixels[0] == 130 && new Ssh(multi, 1).Pixels[0] == 19,
                "multiple entries, directory order and zero-length block limits");
            Reject(data[..^1], "truncated file");
            byte[] bad = (byte[])data.Clone(); bad[20] = 255; bad[21] = 255;
            Reject(bad, "entry offset bounds");
            bad = (byte[])data.Clone(); bad[32] = 2;
            Reject(bad, "GM payload is not a valid type 2 image");
            bad = (byte[])data.Clone(); bad[55] |= 0x40;
            Reject(bad, "unknown GM flags");
            bad = (byte[])data.Clone(); bad[52] = 0;
            Reject(bad, "invalid GM size");
            bad = (byte[])data.Clone();
            int marker = bad.AsSpan(56).IndexOf(new byte[] { 0, 0, 1, 0x34 }) + 56;
            bad[marker + 3] = 0;
            Reject(bad, "missing end marker");
            // Corrupt coefficients while retaining the container, marker and padding.
            bad = (byte[])data.Clone(); Array.Clear(bad, 56, marker - 56);
            Reject(bad, "managed decoder rejects invalid macroblocks instead of returning a placeholder frame");
            Check(Metrics.Compare(opaque.Pixels, opaque.Pixels) == (0.0, 0.0, 0, 0), "zero means exactly equal");
            byte[] changed = (byte[])opaque.Pixels.Clone(); changed[0] += 3; changed[3] = 0;
            var error = Metrics.Compare(changed, opaque.Pixels);
            Check(error.Rgb == 1.0 / 256 && error.Alpha == 255.0 / 256 && error.MaxRgb == 3 && error.MaxAlpha == 255,
                "RGB and alpha denominators are independent");

            string temp = Path.Combine(Path.GetTempPath(), "tpw-ssh-selftest-" + Guid.NewGuid());
            Directory.CreateDirectory(temp);
            try
            {
                foreach (string path in new[] { "MiXeD.SSH", "mixed.TGA", "missing.ssh", "a/sign.ssh", "a/SIGN.tga", "b/sign.ssh" })
                {
                    string full = Path.Combine(temp, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    File.WriteAllBytes(full, []);
                }
                var pairs = Pairing.Find(temp);
                Check(pairs.Count == 4 && pairs.Count(p => p.Error == null) == 2, "case folding, directory scope, missing pairs remain in denominator");
                // A case-sensitive filesystem can also contain ambiguous names; never pick one.
                if (!File.Exists(Path.Combine(temp, "mixed.tga")))
                {
                    File.WriteAllBytes(Path.Combine(temp, "mixed.tga"), []);
                    pairs = Pairing.Find(temp);
                    Check(pairs.Count == 4 && pairs.Count(p => p.Error == null) == 1, "ambiguous partners remain failures");
                }
            }
            finally { Directory.Delete(temp, true); }
            Console.WriteLine($"Self-tests: {checks} of {checks} assertions passed (only synthetic data).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    // A standards-based DC-only IPU stream, generated in memory. No fixture bytes are embedded.
    static byte[] Synthetic(int width, int height, int[] luma, bool alpha)
    {
        string[] lumaCodes = ["100", "00", "01", "101", "110", "1110", "11110", "111110", "1111110"];
        string[] chromaCodes = ["00", "01", "10", "110", "1110", "11110", "111110", "1111110", "11111110"];
        var bits = new StringBuilder();
        int predictor = 128;
        void Dc(int difference, string[] codes)
        {
            int size = 0;
            for (int value = Math.Abs(difference); value > 0; value >>= 1) size++;
            bits.Append(codes[size]);
            if (size > 0)
            {
                int encoded = difference < 0 ? difference + (1 << size) - 1 : difference;
                bits.Append(Convert.ToString(encoded, 2).PadLeft(size, '0'));
            }
            bits.Append("10"); // end of block
        }
        for (int mb = 0; mb < luma.Length; mb++)
        {
            if (mb != 0) bits.Append('1'); // macroblock address increment = 1
            bits.Append('1'); // intra, no quantizer change
            for (int block = 0; block < 4; block++) { Dc(luma[mb] - predictor, lumaCodes); predictor = luma[mb]; }
            Dc(0, chromaCodes); Dc(0, chromaCodes);
        }
        while (bits.Length % 8 != 0) bits.Append('0');
        byte[] stream = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.ToString(i * 8, 8), 2)).ToArray();
        int gw = Math.Max(16, width), gh = Math.Max(16, height);
        int alphaOffset = (8 + stream.Length + 4 + 15) & ~15;
        int gmLength = alphaOffset + (alpha ? gw * gh : 0);
        var data = new byte[48 + gmLength];
        "SHPS"u8.CopyTo(data); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 1); "GIMXTEST"u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), 32);
        data[32] = alpha ? (byte)0x85 : (byte)0x84;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(36), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(38), (ushort)height);
        "GM"u8.CopyTo(data.AsSpan(48)); data[50] = (byte)(gw / 16); data[51] = (byte)(gh / 16);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(52), gmLength | 0x00800000 | (alpha ? 0x08000000 : 0));
        stream.CopyTo(data, 56); data[56 + stream.Length + 2] = 1; data[56 + stream.Length + 3] = 0x34;
        if (alpha)
            for (int i = 0; i < gw * gh; i++) data[48 + alphaOffset + i] = (byte)(64 + i / gw * 2);
        return data;
    }
}
