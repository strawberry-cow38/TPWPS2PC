using System.Text;

namespace TPW.PS2.Data;

/// <summary>
/// The user's own Theme Park World **PSX** disc: optional, read-only, and never shipped.
///
/// Nothing here is required to play. The PS2 game runs the same with or without it. When the player
/// points the port at a PSX image, this identifies it and reads what the PSX build holds (attraction
/// records, the English text table, the eight park maps), so later work can offer PSX content
/// alongside the PS2's. No PSX bytes are ever written into this repository.
///
/// Sources: the TPW-PSX project's reports (`fable/folio.md`, `fable/rides.md`, `fable/paths.md`),
/// read from the PAL executable SLES-026.88. Addresses below are that executable's.
///
/// ⚠ Identification is structural, like <c>DiscLocator</c>: a PSX rip comes as `.bin`/`.cue`,
/// raw 2352-byte sectors or a cooked 2048-byte `.iso`, and one hash would reject most real copies.
/// What is checked is that the image behaves like the game:
/// - SYSTEM.CNF boots a Theme Park World executable;
/// - FOLIO.GAZ carries its entry table;
/// - TPW.BIN is present.
/// Only PAL SLES-026.88 has been read. Anything else that passes is reported as an unknown build.
/// </summary>
public sealed class PsxDisc : IDisposable
{
    public enum Status { None, NotFound, NotADisc, NotTpw, UnknownBuild, Ok }

    public sealed record Check(Status Status, string Path, string Message, string Layout = null, string BootId = null)
    {
        /// <summary>Readable as TPW PSX data: a known build, or an unknown one that still has the files.</summary>
        public bool Readable => Status is Status.Ok or Status.UnknownBuild;
    }

    /// <summary>Builds that have been read end to end, by the executable SYSTEM.CNF boots. The
    /// values are the FOLIO entry count and the TPW.BIN size in that build.</summary>
    static readonly Dictionary<string, (int FolioEntries, int ExecutableSize)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SLES_026.88"] = (422, 1_065_308), // PAL, En/Fr/De
    };

    public static Check Identify(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return new(Status.None, null, "No PSX disc chosen.");
        if (!File.Exists(path)) return new(Status.NotFound, path, "Nothing at that path.");
        Disc disc;
        try { disc = Disc.Open(path); }
        catch (Exception e) { return new(Status.NotADisc, path, $"Could not read it as a disc image: {e.Message}"); }
        if (disc == null) return new(Status.NotADisc, path, "No ISO9660 volume under any sector layout (2352+24, 2048, 2352+16).");
        using (disc)
        {
            var files = disc.Files();
            Disc.Entry Find(string name) => files.FirstOrDefault(f => !f.IsDirectory
                && f.Path.Equals("/" + name, StringComparison.OrdinalIgnoreCase));
            var cnf = Find("SYSTEM.CNF");
            string boot = cnf == null ? null : BootId(Encoding.ASCII.GetString(disc.Read(cnf.Extent, cnf.Size)));
            var folio = Find("FOLIO.GAZ");
            var exe = Find("TPW.BIN");
            if (boot == null || folio == null || exe == null)
                return new(Status.NotTpw, path,
                    $"A disc, but not Theme Park World PSX: boot {boot ?? "none"}, FOLIO.GAZ {(folio != null ? "present" : "missing")}, TPW.BIN {(exe != null ? "present" : "missing")}.",
                    disc.Layout, boot);
            string folioProblem;
            try { folioProblem = PsxFolio.Validate(disc.Read(folio.Extent, folio.Size)); }
            catch (Exception e) { folioProblem = e.Message; }
            if (folioProblem != null)
                return new(Status.NotTpw, path, $"FOLIO.GAZ is not the game's pack: {folioProblem}", disc.Layout, boot);
            int entries = BitConverter.ToInt32(disc.Read(folio.Extent, 4), 0);
            if (Known.TryGetValue(boot, out var k) && k.FolioEntries == entries && k.ExecutableSize == exe.Size)
                return new(Status.Ok, path, $"Theme Park World PSX, {boot} ({disc.Layout}-byte sectors), FOLIO {entries} entries.", disc.Layout, boot);
            return new(Status.UnknownBuild, path,
                $"Theme Park World PSX files, but not a build that has been read ({boot}, FOLIO {entries} entries, TPW.BIN {exe.Size} bytes). Readable, unverified.",
                disc.Layout, boot);
        }
    }

    /// <summary>"SLES_026.88" from `BOOT=cdrom:\SLES_026.88;1` (PSX; the PS2 writes BOOT2).</summary>
    public static string BootId(string cnf)
    {
        foreach (var raw in cnf.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("BOOT", StringComparison.OrdinalIgnoreCase) || line.StartsWith("BOOT2", StringComparison.OrdinalIgnoreCase)) continue;
            int slash = line.LastIndexOfAny(new[] { '\\', ':' });
            var id = line[(slash + 1)..];
            int semi = id.IndexOf(';');
            return semi >= 0 ? id[..semi] : id;
        }
        return null;
    }

    public Check Identity { get; }
    public PsxFolio Folio { get; }
    public byte[] Executable { get; }
    PsxText _text;
    public PsxText Text => _text ??= PsxText.English(Folio);

    PsxDisc(Check identity, PsxFolio folio, byte[] exe) { Identity = identity; Folio = folio; Executable = exe; }
    public void Dispose() { }

    /// <summary>Open a readable image. Throws with the identification message otherwise.</summary>
    public static PsxDisc Open(string path)
    {
        var check = Identify(path);
        if (!check.Readable) throw new InvalidDataException(check.Message);
        using var disc = Disc.Open(path);
        var files = disc.Files();
        byte[] Read(string name)
        {
            var e = files.First(f => !f.IsDirectory && f.Path.Equals("/" + name, StringComparison.OrdinalIgnoreCase));
            return disc.Read(e.Extent, e.Size);
        }
        return new PsxDisc(check, new PsxFolio(Read("FOLIO.GAZ")), Read("TPW.BIN"));
    }

    PsxAttraction[] _attractions;
    /// <summary>Every attraction definition record in FOLIO, in entry order.</summary>
    public IReadOnlyList<PsxAttraction> Attractions => _attractions ??= PsxAttraction.All(Folio, Text).ToArray();

    PsxParkMap[] _maps;
    /// <summary>Every park tile map in FOLIO, in entry order.</summary>
    public IReadOnlyList<PsxParkMap> Maps => _maps ??= PsxParkMap.All(Folio).ToArray();
}

/// <summary>
/// FOLIO.GAZ, the PSX game's one pack file (folio.md §1.4). Its layout:
/// `u32 n; u32 0x17; {u32 offset, u32 size}[n]`, with the pairs starting at +8, not +4 (0x800BBFA0
/// reads `[table + 8 + 8i]`). The table covers only the first part of the file. Streamed strips past
/// its end are read by other paths (folio.md §4.3) and are not entries.
/// </summary>
public sealed class PsxFolio
{
    public readonly record struct Entry(int Index, int Offset, int Size);
    readonly byte[] _data;
    public IReadOnlyList<Entry> Entries { get; }

    public PsxFolio(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        var problem = Validate(data);
        if (problem != null) throw new InvalidDataException(problem);
        int n = BitConverter.ToInt32(data, 0);
        var list = new Entry[n];
        for (int i = 0; i < n; i++)
            list[i] = new Entry(i, BitConverter.ToInt32(data, 8 + 8 * i), BitConverter.ToInt32(data, 12 + 8 * i));
        Entries = list;
    }

    /// <summary>Null when the bytes look like the pack; otherwise what is wrong with them.</summary>
    public static string Validate(byte[] data)
    {
        if (data.Length < 16) return "shorter than its header";
        int n = BitConverter.ToInt32(data, 0), marker = BitConverter.ToInt32(data, 4);
        if (n <= 0 || n > 4096) return $"entry count {n}";
        if (marker != 0x17) return $"second word 0x{marker:X}, not 0x17";
        if (8 + 8L * n > data.Length) return "entry table runs past the file";
        for (int i = 0; i < n; i++)
        {
            long off = BitConverter.ToUInt32(data, 8 + 8 * i), size = BitConverter.ToUInt32(data, 12 + 8 * i);
            if (off + size > data.Length) return $"entry {i} runs past the file";
        }
        return null;
    }

    public ReadOnlySpan<byte> Span(int index)
    {
        var e = Entries[index];
        return _data.AsSpan(e.Offset, e.Size);
    }

    public byte[] Read(int index) => Span(index).ToArray();
}

/// <summary>
/// A FOLIO string table (`fable/text.py`): `u32 n; u32 offset[n]`, then NUL-terminated Latin-1
/// strings at those offsets. The PAL disc carries one table per language. English is the one whose
/// strings include "Build" and a sentence containing " the "; in SLES-026.88 that is entry 407, with
/// 1031 strings.
/// </summary>
public sealed class PsxText
{
    public int Entry { get; }
    readonly string[] _strings;
    public int Count => _strings.Length;
    public string this[int id] => id >= 0 && id < _strings.Length ? _strings[id] : null;

    PsxText(int entry, string[] strings) { Entry = entry; _strings = strings; }

    public static PsxText Parse(int entry, ReadOnlySpan<byte> d)
    {
        if (d.Length < 8) return null;
        int n = BitConverter.ToInt32(d[..4]);
        if (n <= 0 || n > 20000 || 4 + 4L * n > d.Length) return null;
        var outList = new string[n];
        var latin1 = Encoding.Latin1;
        for (int i = 0; i < n; i++)
        {
            int off = BitConverter.ToInt32(d.Slice(4 + 4 * i, 4));
            if (off < 4 + 4 * n || off >= d.Length) return null;
            int end = d[off..].IndexOf((byte)0);
            if (end < 0) return null;
            outList[i] = latin1.GetString(d.Slice(off, end));
        }
        return new PsxText(entry, outList);
    }

    public static PsxText English(PsxFolio folio)
    {
        PsxText best = null;
        foreach (var e in folio.Entries)
        {
            var t = Parse(e.Index, folio.Span(e.Index));
            if (t == null || t.Count < 100) continue;
            if (t._strings.Any(s => s.Contains("Build")) && t._strings.Any(s => s.Contains(" the "))
                && !t._strings.Any(s => s.Contains(" der ") || s.Contains(" les ")))
            { best = t; break; }
        }
        return best ?? throw new InvalidDataException("no English string table in FOLIO");
    }
}

/// <summary>PSX attraction kinds, as `record+0` stores them (rides.json `type_enum`).</summary>
public enum PsxAttractionType { RollerCoaster = 1, Feature = 2, Ride = 3, Shop = 4, Sideshow = 5, TrackRide = 6, TourRide = 7, TrackUpgrade = 8 }

/// <summary>
/// An attraction's definition record (rides.md §1.3). The FOLIO entry starts
/// `0x96, 1, 0, 0, 0, recOff, size, 4`, and the record is at `data + data[0x14]` (0x800307DC). The
/// fields read here:
/// - +0 u32 type;
/// - +4 u32 name text id;
/// - +8 u8 footprint width, +0xA u8 footprint depth;
/// - +0xC s16[2] entrance offset, +0x10 s16[2] exit offset;
/// - +0x18 u32 base intensity;
/// - +0x20 build price for features, shops and sideshows (level 0's is used for rides).
/// ⚠ The TPW-PSX census (records.json) holds 197 of these. A record-by-record read finds 244: it
/// missed 47 small features, mostly 1×1 bins, toilets, loudspeakers and bushes.
/// </summary>
public sealed record PsxAttraction(int Folio, PsxAttractionType Type, int TextId, string Name,
    int FootprintWidth, int FootprintDepth, (short X, short Z) Entrance, (short X, short Z) Exit, int BaseIntensity)
{
    public static IEnumerable<PsxAttraction> All(PsxFolio folio, PsxText text)
    {
        foreach (var e in folio.Entries)
        {
            var d = folio.Span(e.Index);
            if (d.Length < 0x20 || BitConverter.ToUInt32(d[..4]) != 0x96) continue;
            int rec = BitConverter.ToInt32(d.Slice(0x14, 4));
            if (rec < 0x20 || rec + 0x1C > d.Length) continue;
            var r = d[rec..];
            int type = BitConverter.ToInt32(r[..4]), textId = BitConverter.ToInt32(r.Slice(4, 4));
            if (type is < 1 or > 8 || textId < 0 || textId >= text.Count) continue;
            yield return new PsxAttraction(e.Index, (PsxAttractionType)type, textId, text[textId]?.Trim(),
                r[8], r[0xA],
                (BitConverter.ToInt16(r.Slice(0xC, 2)), BitConverter.ToInt16(r.Slice(0xE, 2))),
                (BitConverter.ToInt16(r.Slice(0x10, 2)), BitConverter.ToInt16(r.Slice(0x12, 2))),
                BitConverter.ToInt32(r.Slice(0x18, 4)));
        }
    }
}

/// <summary>
/// A park's tile map (paths.md §1.1, loader 0x800544E0):
/// `u32 N; u32 tab[N]; u32 w; u32 h; tile[w*h]`, with 8-byte tiles.
/// - Tile +0 is the type: 0 grass, 2 path, 4 queue.
/// - Tile +7 holds the flags. Bit 1 (0x02) means nothing may be built, the same bit the PS2 fills
///   at 0x14E700.
/// - The last row and column never count (0x800508C8), so the usable area is `(w−1)×(h−1)`.
/// SLES-026.88 holds eight: entries 34/35, 116/117, 203/204 and 355/356, all 44×74.
/// </summary>
public sealed class PsxParkMap
{
    public int Folio { get; }
    public int Width { get; }
    public int Height { get; }
    readonly byte[] _tiles;

    PsxParkMap(int folio, int w, int h, byte[] tiles) { Folio = folio; Width = w; Height = h; _tiles = tiles; }

    public byte Type(int x, int z) => _tiles[(x + z * Width) * 8];
    public byte Flags(int x, int z) => _tiles[(x + z * Width) * 8 + 7];
    public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Width - 1 && z < Height - 1;
    public bool Buildable(int x, int z) => InBounds(x, z) && (Flags(x, z) & 0x02) == 0;

    public static PsxParkMap TryParse(int folio, ReadOnlySpan<byte> d)
    {
        if (d.Length < 16) return null;
        uint n = BitConverter.ToUInt32(d[..4]);
        if (n > 256) return null;
        long p = 4 + 4L * n;
        if (p + 8 > d.Length) return null;
        int w = BitConverter.ToInt32(d.Slice((int)p, 4)), h = BitConverter.ToInt32(d.Slice((int)p + 4, 4));
        if (w is < 8 or > 256 || h is < 8 or > 256 || p + 8 + (long)w * h * 8 > d.Length) return null;
        var tiles = d.Slice((int)p + 8, w * h * 8).ToArray();
        // A map's tiles carry small type codes (0 grass, 2 path, 4 queue, 13 the path/queue join).
        for (int i = 0; i < tiles.Length; i += 8) if (tiles[i] > 20) return null;
        return new PsxParkMap(folio, w, h, tiles);
    }

    public static IEnumerable<PsxParkMap> All(PsxFolio folio)
    {
        foreach (var e in folio.Entries)
            if (TryParse(e.Index, folio.Span(e.Index)) is { } map) yield return map;
    }
}
