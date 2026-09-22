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
/// kind 2 / 24bpp, 1,710 kind 2 / 32bpp, **111 are kind 10 (RLE)** and **8 are 8bpp**. Throwing
/// on those 119 is 119 textures that silently do not appear.
///
/// ⚠⚠ **NONE OF THOSE 8 IS GREYSCALE** -- an earlier version of this comment said four of them
/// were, and that sentence is what kept the bug below alive. All 8 are paletted: 4 declare it
/// (`Sky/*_back`, cmapType 1) and 4 lie about it (`Sky/*_front2`, cmapType 0 / kind 2 at 8bpp,
/// which is true-colour with no colour map and cannot exist). The liars are the CLOUD MASKS --
/// every palette entry is (255,255,255,a), pure white with only opacity varying -- so believing
/// the header renders each world's cloud layer as an opaque grey sheet that looks entirely fine.
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
        else if (bpp == 8 && !rle && baseKind == 2 && buf.Length - 18 - idlen - Width * Height >= 256)
        {
            // ⚠⚠ The four sky cloud masks (`Sky/*_front2.tga`, one per world) declare cmapType 0
            // and kind 2 -- true-colour, NO colour map -- at 8bpp, which cannot exist. They carry
            // a 256-entry 32-bit palette regardless. Keying the skip on the DECLARED type means
            // the first 1,024 pixels ARE the palette, the stream then runs 1,024 short, and 8bpp
            // falls into the greyscale branch below which forces a = 255. The result is not a
            // rejection -- it is a plausible grey pattern that renders happily, so every world's
            // cloud layer becomes an OPAQUE SHEET and nothing ever points at the TGA reader.
            // Derive the palette from the body size instead. Their honest siblings `Sky/*_back.tga`
            // declare cmapType 1 and must go down the branch above untouched: that is the control.
            cmapStride = Math.Clamp((buf.Length - 18 - idlen - Width * Height) / 256, 1, 4);
            cmapFirst = 0;
            baseKind = 1;
            src += 256 * cmapStride;
        }
        int cmapOff = 18 + idlen;

        // ⚠⚠ MERGE NOTE. tinyclaw and I found the cloud-mask bug independently and wrote a fix each,
        // in the same method, both correct. Git auto-merged them and left BOTH -- and because the
        // branch above sets baseKind = 1, the second one's `baseKind == 2` could never be true
        // again, so it was dead code that read like a second safeguard. A clean auto-merge is not a
        // correct one. Theirs is kept because it derives the palette stride from the body rather
        // than assuming 4; mine only ever handled the 32-bit case these four files happen to use.

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
