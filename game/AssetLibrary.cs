using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>Everything the viewer can show, read straight out of the user's own disc.
///
/// ⚠ BYO-disc: nothing is shipped. The library opens whatever image the launcher found and reads
/// the archives in place.</summary>
public sealed class AssetLibrary : IDisposable
{
    public sealed class RideAssets
    {
        public string Name;                       // "Rides/Monkey"
        public WadArchive.Entry Model;            // the .mps
        public WadArchive.Entry Animation;        // the .aps beside it
    }

    readonly Disc _disc;
    public WadArchive Wad { get; private set; }
    public string WadName { get; private set; }
    public List<RideAssets> Rides { get; } = new();
    /// <summary>The shared texture set, used when nothing nearer to the model has one.</summary>
    public readonly Dictionary<string, WadArchive.Entry> SharedTextures = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Every folder's textures, keyed by the folder they belong TO. A `textures/`
    /// subfolder is filed under its parent, so it sits beside the model it dresses.</summary>
    readonly Dictionary<string, Dictionary<string, WadArchive.Entry>> _folders = new(StringComparer.OrdinalIgnoreCase);

    public AssetLibrary(string discPath) { _disc = new Disc(discPath); }
    public void Dispose() => _disc.Dispose();

    /// <summary>Every `.SDT` sound bank on the disc, in path order. These are loose files, not
    /// archive members -- 41 of them, holding 2,220 sounds and just under four hours of audio.</summary>
    public List<Disc.Entry> SoundBanks() => _disc.Files()
        .Where(f => !f.IsDirectory && f.Path.EndsWith(".SDT", StringComparison.OrdinalIgnoreCase))
        .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();

    public byte[] ReadDisc(Disc.Entry f) => _disc.Read(f.Extent, f.Size);

    public List<string> Wads() => WadFiles().Select(f => f.Path).ToList();

    /// <summary>The archives as disc entries, for a caller that wants to read one itself.</summary>
    public List<Disc.Entry> WadFiles() => _disc.Files()
        .Where(f => !f.IsDirectory && f.Path.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase))
        .ToList();

    public void OpenWad(string path)
    {
        var f = _disc.Files().First(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        Wad = new WadArchive(_disc.Read(f.Extent, f.Size));
        WadName = path;
        Index();
    }

    /// <summary>Group the archive into things a person would want to look at: a model, the animation
    /// beside it, and its own textures.</summary>
    void Index()
    {
        Rides.Clear(); SharedTextures.Clear(); _folders.Clear();
        var byDir = new Dictionary<string, RideAssets>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in Wad.Entries)
        {
            var slash = e.Path.LastIndexOf('/');
            if (slash < 0) continue;
            var dir = e.Path[..slash];
            var ext = Path.GetExtension(e.Path).ToLowerInvariant();

            // ⚠ TEXTURE LOOKUP MUST BE SCOPED. 22 files in JUNGLE.WAD are called sign_eng.tga, one
            // per ride with that ride's NAME painted on it. A flat by-name search picks whichever
            // comes first -- which is how a textured Crazy Ape once wore Mumbo's sign.
            if (ext == ".tga")
            {
                var key = Path.GetFileNameWithoutExtension(e.Path);
                if (dir.Contains("Sharetex", StringComparison.OrdinalIgnoreCase)) { SharedTextures[key] = e; continue; }
                // A `textures/` subfolder dresses the model in the folder above it; a .tga anywhere
                // else belongs to its own folder. DATA.WAD needs both: the characters keep their
                // models in /Chars/<name>/ and share ONE /Chars/Textures/ between all 24 of them,
                // while /Chars/Dino keeps its texture beside the model.
                var leaf = dir[(dir.LastIndexOf('/') + 1)..];
                var owner = leaf.Equals("textures", StringComparison.OrdinalIgnoreCase)
                            ? dir[..dir.LastIndexOf('/')]
                            : dir;
                if (!_folders.TryGetValue(owner, out var f))
                    _folders[owner] = f = new Dictionary<string, WadArchive.Entry>(StringComparer.OrdinalIgnoreCase);
                f[key] = e;
                continue;
            }
            // ⚠⚠ ONE ENTRY PER MODEL, NOT PER FOLDER. 33 folders on the disc hold more than one
            // .mps -- /Rides/gokarts has TWELVE (the track pieces, the karts, the pylons) and
            // /Rides/wateride thirteen. Keying on the folder kept whichever came last and hid the
            // other 160 models on the disc, which reads as a ride with most of its geometry
            // missing rather than as a viewer that is only showing you one piece.
            if (ext == ".mps") Get(e.Path).Model = e;
        }

        // The animation beside a model: its OWN stem first, then the folder's only .aps. A folder
        // with several models and one animation still pairs them; one with a .aps per model pairs
        // them by name.
        var apsByPath = new Dictionary<string, WadArchive.Entry>(StringComparer.OrdinalIgnoreCase);
        var apsByDir = new Dictionary<string, List<WadArchive.Entry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Wad.Entries)
        {
            if (!Path.GetExtension(e.Path).Equals(".aps", StringComparison.OrdinalIgnoreCase)) continue;
            var d2 = e.Path[..Math.Max(e.Path.LastIndexOf('/'), 0)];
            apsByPath[Path.ChangeExtension(e.Path, null)] = e;
            if (!apsByDir.TryGetValue(d2, out var l)) apsByDir[d2] = l = new();
            l.Add(e);
        }
        foreach (var r in byDir.Values)
        {
            var stem = Path.ChangeExtension("/" + r.Name, null);
            if (apsByPath.TryGetValue(stem, out var a)) r.Animation = a;
            else
            {
                var d2 = ("/" + r.Name)[..Math.Max(("/" + r.Name).LastIndexOf('/'), 0)];
                if (apsByDir.TryGetValue(d2, out var l) && l.Count == 1) r.Animation = l[0];
            }
        }

        RideAssets Get(string key)
        {
            if (!byDir.TryGetValue(key, out var r))
                byDir[key] = r = new RideAssets { Name = key.TrimStart('/') };
            return r;
        }

        Rides.AddRange(byDir.Values.Where(r => r.Model != null).OrderBy(r => r.Name));
    }

    public byte[] Read(WadArchive.Entry e) => Wad.Read(e);

    /// <summary>Every image in the open archive, in path order -- the `.tga` the viewer can already
    /// decode, and the `.ssh` beside it, which is an MPEG intra picture the IPU decodes on hardware
    /// and this reader cannot yet. Both are listed so the gap is visible rather than silent.</summary>
    public List<WadArchive.Entry> Images(bool includeSsh = true) => Wad.Entries
        .Where(e => e.Path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)
                 || (includeSsh && e.Path.EndsWith(".ssh", StringComparison.OrdinalIgnoreCase)))
        .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>A material's texture: the model's OWN folder first, then each folder above it, then
    /// the archive-wide shared set.
    ///
    /// ⚠ The order is the whole point, not a detail. A flat by-name search over JUNGLE.WAD once put
    /// Mumbo's sign on Crazy Ape, because 22 rides each ship a sign_eng.tga. Nearest wins.</summary>
    public Targa Texture(RideAssets ride, string materialName)
    {
        var stem = Path.GetFileNameWithoutExtension(materialName);   // "m_back.ssh" -> "m_back"
        WadArchive.Entry e = null;
        // ⚠ Name is the MODEL'S PATH now, so start the walk at its folder, not at the file.
        var start = "/" + ride.Name;
        start = start[..Math.Max(start.LastIndexOf('/'), 0)];
        for (var d = start; e == null && d.Length > 0; d = d[..Math.Max(d.LastIndexOf('/'), 0)])
            if (_folders.TryGetValue(d, out var f)) f.TryGetValue(stem, out e);
        if (e == null) SharedTextures.TryGetValue(stem, out e);
        if (e == null) return null;
        try { return new Targa(Wad.Read(e)); } catch { return null; }
    }
}
