namespace TPW.PS2.Data;

/// <summary>The PlayStation 2 disc image and its ISO9660 tree.
///
/// ⚠ The track is <c>MODE2/2352</c>: each sector is 2,352 bytes of which the **user data is 2,048
/// bytes at offset 24** (16-byte sync and header, then an 8-byte subheader). Reading it as a plain
/// 2,048-byte-per-sector ISO gives nothing but noise, which is the first thing to check when a disc
/// "will not parse".</summary>
public sealed class Disc : IDisposable
{
    public const int SectorSize = 2352, UserOffset = 24, UserSize = 2048;

    readonly FileStream _fs;
    public Disc(string path) { _fs = File.OpenRead(path); }
    public void Dispose() => _fs.Dispose();

    public byte[] Sector(long n)
    {
        var b = new byte[UserSize];
        _fs.Seek(n * SectorSize + UserOffset, SeekOrigin.Begin);
        _fs.ReadExactly(b, 0, UserSize);
        return b;
    }

    /// <summary>Read <paramref name="size"/> bytes starting at sector <paramref name="extent"/>.</summary>
    public byte[] Read(long extent, int size)
    {
        var outBuf = new byte[size];
        int done = 0;
        for (long s = extent; done < size; s++)
        {
            var sec = Sector(s);
            int take = Math.Min(UserSize, size - done);
            Array.Copy(sec, 0, outBuf, done, take);
            done += take;
        }
        return outBuf;
    }

    public record Entry(string Path, long Extent, int Size, bool IsDirectory);

    /// <summary>Every file on the disc, from the primary volume descriptor at sector 16.</summary>
    public List<Entry> Files()
    {
        var pvd = Sector(16);
        long rootExtent = BitConverter.ToUInt32(pvd, 156 + 2);
        int rootLen = BitConverter.ToInt32(pvd, 156 + 10);
        var outList = new List<Entry>();
        Walk(rootExtent, rootLen, "", outList, 0);
        return outList;
    }

    void Walk(long extent, int length, string path, List<Entry> outList, int depth)
    {
        var data = Read(extent, length);
        int p = 0;
        while (p < data.Length)
        {
            int rl = data[p];
            if (rl == 0)
            {
                p = (p / UserSize + 1) * UserSize;           // records never straddle a sector
                if (p >= data.Length) break;
                continue;
            }
            long ext = BitConverter.ToUInt32(data, p + 2);
            int size = BitConverter.ToInt32(data, p + 10);
            int flags = data[p + 25];
            int nlen = data[p + 32];
            var raw = data.AsSpan(p + 33, nlen);
            p += rl;

            if (nlen == 1 && (raw[0] == 0 || raw[0] == 1)) continue;   // "." and ".."
            var name = System.Text.Encoding.ASCII.GetString(raw);
            int semi = name.IndexOf(';');
            if (semi >= 0) name = name[..semi];
            var full = path + "/" + name;
            bool dir = (flags & 2) != 0;
            outList.Add(new Entry(full, ext, size, dir));
            if (dir && depth < 8) Walk(ext, size, full, outList, depth + 1);
        }
    }
}
