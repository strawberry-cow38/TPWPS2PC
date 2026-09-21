namespace TPW.PS2.Data;

/// <summary>The disc's TGA textures, as straight RGBA.
///
/// ⚠ **32-bit is not optional.** 1,710 of the disc's 5,695 TGAs are 32-bit BGRA, and a loader that
/// tests <c>bpp != 24</c> rejects every one of them silently -- the model then renders untextured
/// and it reads as missing geometry rather than a rejected format.
///
/// ⚠ And 32-bit means **alpha cutout**, not merely a wider pixel: their names say what they are
/// (`pl1_leaf`, `leaf1`). Discard the alpha and foliage becomes an opaque black wedge.
///
/// ⚠⚠ **RLE AND PALETTED ARE ALSO ON THE DISC.** Counting every TGA in every WAD: 3,865 are
/// kind 2 / 24bpp, 1,710 kind 2 / 32bpp, **111 are kind 10 (RLE)** and **8 are 8bpp** (kind 1
/// colour-mapped and kind 2 greyscale). Throwing on those 119 is 119 textures that silently
/// do not appear.
///
/// ⭐ <see cref="PartialAlpha"/> is the count of texels that are neither clear nor solid. **1,543
/// of the 32-bit textures have some** -- soft edges, glass, smoke -- and a renderer that only
/// cutouts (`discard` below a threshold, then opaque) throws all of it away. That is what
/// "some textures are missing alpha" looks like from the outside.</summary>
public sealed class Targa
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>RGBA8, top row first.</summary>
    public byte[] Pixels { get; }
    /// <summary>Texels with alpha in 16..249: soft edges rather than a binary cutout.</summary>
    public int PartialAlpha { get; }
    /// <summary>Texels with alpha below 16: the cutout holes.</summary>
    public int ClearTexels { get; }

    public Targa(byte[] buf)
    {
        int idlen = buf[0], cmapType = buf[1], kind = buf[2];
        int cmapFirst = BitConverter.ToUInt16(buf, 3), cmapLen = BitConverter.ToUInt16(buf, 5);
        int cmapBits = buf[7];
        Width = BitConverter.ToUInt16(buf, 12);
        Height = BitConverter.ToUInt16(buf, 14);
        int bpp = buf[16], desc = buf[17];

        int baseKind = kind & 7;                    // 1 colour-mapped, 2 true-colour, 3 greyscale
        bool rle = (kind & 8) != 0;                 // kinds 9/10/11 are the RLE forms
        if (baseKind is not (1 or 2 or 3) || Width <= 0 || Height <= 0)
            throw new InvalidDataException($"unsupported TGA: kind {kind}, {bpp}bpp, {Width}x{Height}");

        int src = 18 + idlen;
        int cmapStride = 0;
        if (cmapType != 0)
        {
            cmapStride = (cmapBits + 7) / 8;
            src += cmapLen * cmapStride;     // the palette sits between the header and the pixels
        }
        int cmapOff = 18 + idlen;

        // ⚠ Four Sky/*_front2.tga (FANTASY, HALLOW, JUNGLE, SPACE) declare cmapType 0 with kind 2 --
        // true-colour, no colour map -- at 8bpp, which cannot exist. Their body is a 256-entry
        // 32-bit palette followed by Width*Height indices. Believing the header skips no palette, so
        // the first 1,024 pixels ARE the palette, the stream runs 1,024 bytes short, and 8bpp then
        // falls into the greyscale branch below and throws the alpha away. That alpha is the whole
        // point of these four: every palette entry is white and only its opacity varies, because
        // they are the cloud masks composited over Sky/*_back.tga. Trust the body, not the header.
        if (bpp == 8 && cmapType == 0 && baseKind == 2 && !rle
            && buf.Length - cmapOff >= Width * Height + 1024)
        {
            baseKind = 1; cmapFirst = 0; cmapStride = 4; src = cmapOff + 1024;
        }

        int n = (bpp + 7) / 8;
        int count = Width * Height;
        var raw = new byte[count * n];
        if (rle)
        {
            // ⚠ A packet's run CAN cross a scanline. Decoding row by row and stopping at the row
            // end drops the remainder and shears the image; decode the whole pixel stream.
            int o = src, w = 0;
            while (w < count * n && o < buf.Length)
            {
                int hdr = buf[o++], len = (hdr & 0x7F) + 1;
                if ((hdr & 0x80) != 0)
                {
                    for (int k = 0; k < len && w < raw.Length; k++)
                        for (int j = 0; j < n; j++) raw[w++] = buf[o + j];
                    o += n;
                }
                else
                {
                    for (int k = 0; k < len * n && w < raw.Length; k++) raw[w++] = buf[o++];
                }
            }
        }
        else
        {
            Array.Copy(buf, src, raw, 0, Math.Min(raw.Length, buf.Length - src));
        }

        Pixels = new byte[count * 4];
        int partial = 0, clear = 0;
        for (int y = 0; y < Height; y++)
        {
            int row = (desc & 0x20) != 0 ? y : Height - 1 - y;   // origin bit
            for (int x = 0; x < Width; x++)
            {
                int o = (y * Width + x) * n, d = (row * Width + x) * 4;
                byte r, g, b, a = 255;
                if (baseKind == 1)                               // colour-mapped
                {
                    int e = cmapOff + (raw[o] - cmapFirst) * cmapStride;
                    if (e < 0 || e + cmapStride > buf.Length) { r = g = b = 0; }
                    else
                    {
                        b = buf[e]; g = cmapStride > 1 ? buf[e + 1] : b; r = cmapStride > 2 ? buf[e + 2] : b;
                        if (cmapStride > 3) a = buf[e + 3];
                    }
                }
                else if (baseKind == 3 || n == 1) { r = g = b = raw[o]; }
                else
                {
                    b = raw[o]; g = raw[o + 1]; r = raw[o + 2];
                    if (n == 4) a = raw[o + 3];
                }
                Pixels[d + 0] = r; Pixels[d + 1] = g; Pixels[d + 2] = b; Pixels[d + 3] = a;
                if (a < 16) clear++; else if (a < 250) partial++;
            }
        }
        PartialAlpha = partial; ClearTexels = clear;
    }
}
