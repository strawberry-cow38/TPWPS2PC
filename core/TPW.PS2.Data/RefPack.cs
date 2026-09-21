namespace TPW.PS2.Data;

/// <summary>EA RefPack (QFS), the compression every entry in a `.WAD` uses.
///
/// Header: <c>0x10 0xFB</c>, an optional 3-byte big-endian COMPRESSED size when bit 0 of byte 0 is
/// set, then the 3-byte big-endian DECOMPRESSED size.
///
/// ⚠ An entry is only RefPacked if it SHRANK. When the packer could not beat the original the
/// archive stores it raw, and those entries have <c>stored == decompressed</c> and no <c>10 FB</c>
/// magic -- 83 of JUNGLE.WAD's 2,545 files are like that, mostly small TGAs. Treating a failed
/// decompress as a broken file loses exactly the files that compress worst.</summary>
public static class RefPack
{
    public static bool LooksPacked(ReadOnlySpan<byte> d, int off = 0) =>
        off + 1 < d.Length && d[off + 1] == 0xFB;

    /// <summary>Decompress at <paramref name="off"/>. Returns the bytes; <paramref name="declared"/>
    /// is the size the header claims, which the caller should check against the result.</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> d, int off, out int declared)
    {
        byte b0 = d[off], b1 = d[off + 1];
        if (b1 != 0xFB) throw new InvalidDataException($"not refpack: {b0:X2} {b1:X2}");
        int p = off + 2;
        if ((b0 & 0x01) != 0) p += 3;                       // compressed size present
        declared = (d[p] << 16) | (d[p + 1] << 8) | d[p + 2];
        p += 3;

        var outBuf = new byte[declared];
        int n = 0;
        void Lit(ReadOnlySpan<byte> src, int count)
        {
            src.Slice(0, count).CopyTo(outBuf.AsSpan(n));
            n += count;
        }

        while (p < d.Length)
        {
            int c = d[p], proceed, run, dist;
            if (c < 0x80)
            {
                int a = d[p + 1]; p += 2;
                proceed = c & 3;
                run = ((c & 0x1C) >> 2) + 3;
                dist = ((c & 0x60) << 3) + a + 1;
            }
            else if (c < 0xC0)
            {
                int a = d[p + 1], b = d[p + 2]; p += 3;
                proceed = a >> 6;
                run = (c & 0x3F) + 4;
                dist = ((a & 0x3F) << 8) + b + 1;
            }
            else if (c < 0xE0)
            {
                int a = d[p + 1], b = d[p + 2], e = d[p + 3]; p += 4;
                proceed = c & 3;
                run = ((c & 0x0C) << 6) + e + 5;
                dist = ((c & 0x10) << 12) + (a << 8) + b + 1;
            }
            else if (c < 0xFC)
            {
                p += 1;
                proceed = ((c & 0x1F) << 2) + 4;
                run = dist = 0;
            }
            else
            {
                p += 1;
                proceed = c & 3;
                Lit(d.Slice(p), proceed);
                break;                                       // terminator
            }
            Lit(d.Slice(p), proceed); p += proceed;
            for (int i = 0; i < run; i++, n++) outBuf[n] = outBuf[n - dist];
        }
        return outBuf;
    }
}
