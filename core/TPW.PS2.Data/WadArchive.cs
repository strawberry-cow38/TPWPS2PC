namespace TPW.PS2.Data;

/// <summary>The `FKNL` archive that every `.WAD` on the disc is.
///
/// ⚠ Despite sharing the extension, this is **not** the PC release's `.WAD` container -- OpenTPW's
/// archive magics are `BFMU`/`BFST`/`BILZ`/`DWFB` and none of them is `FKNL`.
///
/// <code>
/// header : 'FKNL', u32 0, u32 dataStart, u32 hash, u32 stringTable, u32 rootBlock
/// block  : u32 fileTable, u32 dirTable, u32 fileCount, u32 dirCount
/// file   : u32 nameOffset, u32 dataOffset, u32 storedSize, u32 decompressedSize   [16 bytes]
/// dir    : u32 nameOffset, u32 blockOffset                                        [8 bytes]
/// </code>
///
/// Names are NUL-terminated at an absolute offset. Verified on JUNGLE.WAD at 2,545/2,545 entries,
/// and five further archives (DATA, FANTASY, HALLOW, LOBBY, SPACE -- 12,985 more files) parse clean
/// with the same reader, which was written against JUNGLE alone.</summary>
public sealed class WadArchive
{
    public record Entry(string Path, int Offset, int StoredSize, int DecompressedSize)
    {
        public string Name => Path[(Path.LastIndexOf('/') + 1)..];
        /// <summary>⚠ Stored raw when RefPack could not beat the original.</summary>
        public bool IsRaw => StoredSize == DecompressedSize;
    }

    readonly byte[] _d;
    public IReadOnlyList<Entry> Entries { get; }

    public WadArchive(byte[] data)
    {
        _d = data;
        if (_d[0] != 'F' || _d[1] != 'K' || _d[2] != 'N' || _d[3] != 'L')
            throw new InvalidDataException("not FKNL: " + System.Text.Encoding.ASCII.GetString(_d, 0, 4));
        var list = new List<Entry>();
        Walk(U32(0x14), "", list, 0);
        Entries = list;
    }

    uint U32(int o) => BitConverter.ToUInt32(_d, o);

    string Name(int o)
    {
        int e = o;
        while (e < _d.Length && _d[e] != 0) e++;
        return System.Text.Encoding.Latin1.GetString(_d, o, e - o);
    }

    void Walk(uint block, string path, List<Entry> outList, int depth)
    {
        uint ftab = U32((int)block), dtab = U32((int)block + 4);
        uint nfiles = U32((int)block + 8), ndirs = U32((int)block + 12);
        for (uint i = 0; i < nfiles; i++)
        {
            int r = (int)(ftab + i * 16);
            outList.Add(new Entry(path + "/" + Name((int)U32(r)),
                                  (int)U32(r + 4), (int)U32(r + 8), (int)U32(r + 12)));
        }
        for (uint i = 0; i < ndirs; i++)
        {
            int r = (int)(dtab + i * 8);
            var nm = path + "/" + Name((int)U32(r));
            if (depth < 16) Walk(U32(r + 4), nm, outList, depth + 1);
        }
    }

    /// <summary>One entry's bytes, decompressing only when it actually shrank.</summary>
    public byte[] Read(Entry e)
    {
        if (e.IsRaw) return _d.AsSpan(e.Offset, e.StoredSize).ToArray();
        var outBuf = RefPack.Decompress(_d, e.Offset, out int declared);
        if (declared != e.DecompressedSize)
            throw new InvalidDataException(
                $"{e.Path}: refpack header says {declared}, the archive says {e.DecompressedSize}");
        return outBuf;
    }

    public Entry Find(string endsWith) =>
        Entries.FirstOrDefault(e => e.Path.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
}
