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
    readonly int _sectorSize, _userOffset;
    public Disc(string path) : this(path, SectorSize, UserOffset) { }

    /// <summary>An image in some other sector layout: a 2,048-byte cooked ISO (<c>2048, 0</c>) or a
    /// Mode 1 raw track (<c>2352, 16</c>). The PS2 rip always uses the parameterless constructor.</summary>
    public Disc(string path, int sectorSize, int userOffset)
    {
        if (sectorSize is not (2048 or 2352) || userOffset < 0 || userOffset + UserSize > sectorSize)
            throw new ArgumentException($"unsupported sector layout {sectorSize}/{userOffset}");
        _fs = File.OpenRead(path);
        _sectorSize = sectorSize; _userOffset = userOffset;
    }
    public void Dispose() => _fs.Dispose();

    /// <summary>"2048" or "2352+24" style, for reports.</summary>
    public string Layout => _sectorSize == 2048 ? "2048" : $"{_sectorSize}+{_userOffset}";

    /// <summary>Open an image whose layout is not known in advance, by finding the ISO9660 primary
    /// volume descriptor ("\x01CD001" at sector 16) under each candidate layout. A <c>.cue</c> is
    /// followed to its first FILE. Null when no layout finds one.</summary>
    public static Disc Open(string path)
    {
        if (path.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)) path = CueTarget(path);
        foreach (var (size, offset) in new[] { (2352, 24), (2048, 0), (2352, 16) })
        {
            Disc d = null;
            try
            {
                d = new Disc(path, size, offset);
                if ((16L + 1) * size > d._fs.Length) { d.Dispose(); continue; }
                var pvd = d.Sector(16);
                if (pvd[0] == 1 && pvd[1] == (byte)'C' && pvd[2] == (byte)'D' && pvd[3] == (byte)'0'
                    && pvd[4] == (byte)'0' && pvd[5] == (byte)'1') return d;
                d.Dispose();
            }
            catch (IOException) { d?.Dispose(); }
        }
        return null;
    }

    /// <summary>The first <c>FILE "…"</c> of a cue sheet, relative to the cue's folder.</summary>
    public static string CueTarget(string cue)
    {
        foreach (var line in File.ReadLines(cue))
        {
            var t = line.Trim();
            if (!t.StartsWith("FILE", StringComparison.OrdinalIgnoreCase)) continue;
            int a = t.IndexOf('"'), b = a < 0 ? -1 : t.IndexOf('"', a + 1);
            if (b > a) return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cue)) ?? "", t[(a + 1)..b]);
        }
        throw new InvalidDataException($"{cue}: no FILE line");
    }

    public byte[] Sector(long n)
    {
        var b = new byte[UserSize];
        _fs.Seek(n * _sectorSize + _userOffset, SeekOrigin.Begin);
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
