// MPEG-1 intra reconstruction and integer simple IDCT adapted from FFmpeg n7.0.2:
// libavcodec/mpeg12.c, simple_idct_template.c, aarch64/simple_idct_neon.S.
// Copyright (c) 2000, 2001 Fabrice Bellard; 2001-2004 Michael Niedermayer;
// 2008 Mans Rullgard; 2017 Matthieu Bouron.
// SPDX-License-Identifier: LGPL-2.1-or-later
// See THIRD_PARTY_NOTICES.md and licenses/LGPL-2.1.txt.
namespace TPW.PS2.Data;

/// <summary>The GM subset of IPU: MPEG-1 coefficients, IDP/AS/IVF/QST/DTD all zero.
/// Output is planar Y, Cb, Cr in coded raster order. All arithmetic is integer and
/// matches the reference's AArch64 simple_idct_neon rounding, using portable scalars.</summary>
internal static partial class IpuDecoder
{
    static readonly int[] Zigzag =
    [
         0, 1, 8,16, 9, 2, 3,10,17,24,32,25,18,11, 4, 5,
        12,19,26,33,40,48,41,34,27,20,13, 6, 7,14,21,28,
        35,42,49,56,57,50,43,36,29,22,15,23,30,37,44,51,
        58,59,52,45,38,31,39,46,53,60,61,54,47,55,62,63
    ];
    static readonly ushort[] LumaVlc, ChromaVlc, AcVlc;

    static IpuDecoder()
    {
        LumaVlc = MakeVlc(LumaCodes, LumaLengths, 9);
        ChromaVlc = MakeVlc(ChromaCodes, ChromaLengths, 10);
        AcVlc = MakeVlc(AcCodesAndLengths.Where((_, i) => i % 2 == 0).ToArray(),
            AcCodesAndLengths.Where((_, i) => i % 2 != 0).ToArray(), 16);
    }

    public static byte[] Decode(ReadOnlySpan<byte> stream, int width, int height)
    {
        if (width <= 0 || height <= 0 || (width & 15) != 0 || (height & 15) != 0)
            throw new InvalidDataException("IPU coded dimensions must be positive multiples of 16.");
        int pixels = checked(width * height);
        var output = new byte[checked(pixels + pixels / 2)];
        var bits = new BitReader(stream);
        Span<int> dc = stackalloc int[] { 128, 128, 128 };
        Span<short> block = stackalloc short[64];
        int quantiser = 1;
        for (int y = 0; y < height; y += 16)
            for (int x = 0; x < width; x += 16)
            {
                // IPU accepts only address increment 1, with no address before the first MB.
                if ((x != 0 || y != 0) && bits.Read(1) != 1)
                    throw new InvalidDataException("Invalid IPU macroblock address increment.");
                if (bits.Read(1) == 0)
                {
                    if (bits.Read(1) != 1) throw new InvalidDataException("Invalid IPU intra macroblock type.");
                    quantiser = bits.Read(5) * 2; // mpeg_get_qscale, QST = 0
                }
                for (int n = 0; n < 6; n++)
                {
                    block.Clear();
                    int component = n < 4 ? 0 : n - 3;
                    int size = component == 0 ? bits.Vlc(LumaVlc, 9) : bits.Vlc(ChromaVlc, 10);
                    int difference = bits.Read(size);
                    if (size != 0 && difference < 1 << (size - 1)) difference -= (1 << size) - 1;
                    dc[component] = unchecked(dc[component] + difference);
                    block[0] = unchecked((short)(dc[component] * 8));
                    int position = 0;
                    while (true)
                    {
                        int symbol = bits.Vlc(AcVlc, 16);
                        if (symbol == 112) break; // EOB
                        int run, level;
                        if (symbol == 111)
                        {
                            run = bits.Read(6) + 1;
                            level = unchecked((sbyte)bits.Read(8));
                            if (level == -128) level = bits.Read(8) - 256;
                            else if (level == 0) level = bits.Read(8);
                        }
                        else
                        {
                            run = Runs[symbol] + 1;
                            level = Levels[symbol];
                            if (bits.Read(1) != 0) level = -level;
                        }
                        position += run;
                        if (position > 63) throw new InvalidDataException("IPU coefficient run exceeds its block.");
                        int at = Zigzag[position];
                        int value = ((Math.Abs(level) * quantiser * QuantMatrix[at] >> 4) - 1) | 1;
                        // MPEG-1 mismatch control oddifies each AC magnitude. The reference
                        // stores int16 here; it does NOT apply MPEG-2's block[63] parity toggle
                        // or a separate 12-bit coefficient clamp on this code path.
                        block[at] = unchecked((short)(level < 0 ? -value : value));
                    }
                    int stride = n < 4 ? width : width / 2;
                    int offset = n < 4 ? (y + (n / 2) * 8) * width + x + (n % 2) * 8 :
                        pixels + (n - 4) * (pixels / 4) + (y / 2) * stride + x / 2;
                    IdctPut(block, output.AsSpan(offset), stride);
                }
            }
        // The caller has already validated and removed the GM end marker. Only the final
        // partially consumed byte may remain (FFmpeg aligns before requiring its 4-byte tail).
        if ((bits.Position + 7) / 8 != stream.Length)
            throw new InvalidDataException("IPU macroblocks do not consume the stream exactly.");
        return output;
    }

    static void IdctPut(Span<short> block, Span<byte> output, int stride)
    {
        Span<short> transformed = stackalloc short[8];
        for (int y = 0; y < 8; y++)
        {
            var row = block.Slice(y * 8, 8);
            // The reference NEON path multiplies even DC-only rows by 16383. Replacing
            // that with the C/x86 shift shortcut changes rounding on real textures.
            Transform(row, 1, transformed, 11, false);
            transformed.CopyTo(row);
        }
        for (int x = 0; x < 8; x++)
        {
            Transform(block[x..], 8, transformed, 20, true);
            for (int y = 0; y < 8; y++) output[y * stride + x] = (byte)Math.Clamp((int)transformed[y], 0, 255);
        }
    }

    static void Transform(ReadOnlySpan<short> s, int stride, Span<short> d, int shift, bool column)
    {
        // Explicit unchecked arithmetic preserves the reference's 32-bit wrap, 16-bit
        // column bias wrap, arithmetic shifts, and row narrowing on every platform.
        // Saturation occurs at pixel output.
        unchecked
        {
            const int w1 = 22725, w2 = 21407, w3 = 19266, w4 = 16383,
                w5 = 12873, w6 = 8867, w7 = 4520;
            int s0 = column ? (short)(s[0] + 32) : s[0];
            int s1 = s[stride], s2 = s[2 * stride], s3 = s[3 * stride],
                s4 = s[4 * stride], s5 = s[5 * stride], s6 = s[6 * stride], s7 = s[7 * stride];
            int bias = column ? 0 : 1024;
            int a0 = w4 * s0 + bias + w2 * s2 + w4 * s4 + w6 * s6;
            int a1 = w4 * s0 + bias + w6 * s2 - w4 * s4 - w2 * s6;
            int a2 = w4 * s0 + bias - w6 * s2 - w4 * s4 + w2 * s6;
            int a3 = w4 * s0 + bias - w2 * s2 + w4 * s4 - w6 * s6;
            int b0 = w1 * s1 + w3 * s3 + w5 * s5 + w7 * s7;
            int b1 = w3 * s1 - w7 * s3 - w1 * s5 - w5 * s7;
            int b2 = w5 * s1 - w1 * s3 + w7 * s5 + w3 * s7;
            int b3 = w7 * s1 - w5 * s3 + w3 * s5 - w1 * s7;
            d[0] = Narrow((a0 + b0) >> shift); d[7] = Narrow((a0 - b0) >> shift);
            d[1] = Narrow((a1 + b1) >> shift); d[6] = Narrow((a1 - b1) >> shift);
            d[2] = Narrow((a2 + b2) >> shift); d[5] = Narrow((a2 - b2) >> shift);
            d[3] = Narrow((a3 + b3) >> shift); d[4] = Narrow((a3 - b3) >> shift);
        }
    }

    static short Narrow(int value) => unchecked((short)value);

    static ushort[] MakeVlc(int[] codes, int[] lengths, int width)
    {
        var table = new ushort[1 << width];
        for (int symbol = 0; symbol < codes.Length; symbol++)
        {
            int suffix = width - lengths[symbol];
            Array.Fill(table, (ushort)((symbol << 5) | lengths[symbol]), codes[symbol] << suffix, 1 << suffix);
        }
        return table;
    }

    ref struct BitReader(ReadOnlySpan<byte> data)
    {
        public int Position { get; private set; }
        readonly ReadOnlySpan<byte> data = data;

        int Peek(int count)
        {
            int offset = Position >> 3, shift = Position & 7;
            uint value = 0;
            for (int i = 0; i < 3; i++)
                value = (value << 8) | (offset + i < data.Length ? data[offset + i] : 0u);
            return (int)((value >> (24 - shift - count)) & ((1u << count) - 1));
        }

        public int Read(int count)
        {
            if (count == 0) return 0;
            if ((long)Position + count > (long)data.Length * 8) throw new InvalidDataException("Truncated IPU bitstream.");
            int value = Peek(count);
            Position += count;
            return value;
        }

        public int Vlc(ushort[] table, int width)
        {
            int entry = table[Peek(width)], length = entry & 31;
            if (length == 0) throw new InvalidDataException("Invalid IPU variable-length code.");
            Read(length);
            return entry >> 5;
        }
    }
}
