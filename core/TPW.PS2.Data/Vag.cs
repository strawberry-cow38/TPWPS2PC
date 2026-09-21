namespace TPW.PS2.Data;

/// <summary>Sony PS-ADPCM, as the `.vag` sounds in a `.SDT` bank store it.
///
/// 16-byte blocks: a shift/filter byte, a flags byte, then 28 samples as 14 bytes of nibbles.
/// Each sample is the nibble sign-extended and scaled, plus a two-tap prediction from the previous
/// two outputs. 356 of the disc's 2,220 sounds are stored this way, and every one of them opens on
/// the customary silent block and is a whole number of blocks long.</summary>
public static class Vag
{
    // The SPU's five filters, as fixed-point sixths.
    static readonly int[] F0 = { 0, 60, 115, 98, 122 };
    static readonly int[] F1 = { 0, 0, -52, -55, -60 };

    /// <summary>Decode to signed 16-bit mono PCM.</summary>
    public static short[] Decode(byte[] d, int start, int end)
    {
        var outList = new List<short>(((end - start) / 16) * 28);
        int prev1 = 0, prev2 = 0;
        for (int p = start; p + 16 <= end; p += 16)
        {
            int shift = d[p] & 0x0F, filter = (d[p] >> 4) & 0x0F;
            int flags = d[p + 1];
            if (flags == 7) break;                       // end marker, stop
            if (filter >= F0.Length) filter = 0;
            for (int i = 0; i < 28; i++)
            {
                int nib = (d[p + 2 + i / 2] >> ((i & 1) * 4)) & 0x0F;
                int s = nib > 7 ? nib - 16 : nib;        // sign-extend the 4-bit sample
                int v = (s << 12) >> shift;
                v += (prev1 * F0[filter] + prev2 * F1[filter]) >> 6;
                v = Math.Clamp(v, short.MinValue, short.MaxValue);
                outList.Add((short)v);
                prev2 = prev1; prev1 = v;
            }
        }
        return outList.ToArray();
    }
}
