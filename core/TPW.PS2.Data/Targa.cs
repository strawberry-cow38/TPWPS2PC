namespace TPW.PS2.Data;

/// <summary>The disc's TGA textures, as straight RGBA.
///
/// ⚠ **32-bit is not optional.** 261 of JUNGLE.WAD's 984 TGAs are 32-bit BGRA, and a loader that
/// tests <c>bpp != 24</c> rejects every one of them silently -- the model then renders untextured
/// and it reads as missing geometry rather than a rejected format.
///
/// ⚠ And 32-bit means **alpha cutout**, not merely a wider pixel: 227 of those 261 are more than 2%
/// fully transparent, and their names say what they are (`pl1_leaf`, `leaf1`). Discard the alpha and
/// foliage becomes an opaque black wedge.</summary>
public sealed class Targa
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>RGBA8, top row first.</summary>
    public byte[] Pixels { get; }

    public Targa(byte[] buf)
    {
        int idlen = buf[0], kind = buf[2];
        Width = BitConverter.ToUInt16(buf, 12);
        Height = BitConverter.ToUInt16(buf, 14);
        int bpp = buf[16], desc = buf[17];
        if (kind != 2 || (bpp != 24 && bpp != 32))
            throw new InvalidDataException($"unsupported TGA: kind {kind}, {bpp}bpp");
        int n = bpp / 8, src = 18 + idlen;
        Pixels = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        {
            int row = (desc & 0x20) != 0 ? y : Height - 1 - y;   // origin bit
            for (int x = 0; x < Width; x++)
            {
                int o = src + (y * Width + x) * n, d = (row * Width + x) * 4;
                Pixels[d + 0] = buf[o + 2];
                Pixels[d + 1] = buf[o + 1];
                Pixels[d + 2] = buf[o + 0];
                Pixels[d + 3] = n == 4 ? buf[o + 3] : (byte)255;
            }
        }
    }
}
