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
        public readonly Dictionary<string, WadArchive.Entry> Textures = new(StringComparer.OrdinalIgnoreCase);
    }

    readonly Disc _disc;
    public WadArchive Wad { get; private set; }
    public string WadName { get; private set; }
    public List<RideAssets> Rides { get; } = new();
    /// <summary>The shared texture set, used when a ride's own folder does not have one.</summary>
    public readonly Dictionary<string, WadArchive.Entry> SharedTextures = new(StringComparer.OrdinalIgnoreCase);

    public AssetLibrary(string discPath) { _disc = new Disc(discPath); }
    public void Dispose() => _disc.Dispose();

    public List<string> Wads() => _disc.Files()
        .Where(f => !f.IsDirectory && f.Path.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase))
        .Select(f => f.Path).ToList();

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
        Rides.Clear(); SharedTextures.Clear();
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
                if (dir.Contains("Sharetex", StringComparison.OrdinalIgnoreCase))
                    SharedTextures[key] = e;
                else
                {
                    var owner = dir[..Math.Max(dir.LastIndexOf('/'), 0)];
                    Get(owner).Textures[key] = e;
                }
                continue;
            }
            if (ext == ".mps") Get(dir).Model = e;
            else if (ext == ".aps") Get(dir).Animation = e;
        }

        RideAssets Get(string dir)
        {
            if (!byDir.TryGetValue(dir, out var r))
                byDir[dir] = r = new RideAssets { Name = dir.TrimStart('/') };
            return r;
        }

        Rides.AddRange(byDir.Values.Where(r => r.Model != null).OrderBy(r => r.Name));
    }

    public byte[] Read(WadArchive.Entry e) => Wad.Read(e);

    /// <summary>A material's texture: the ride's own folder FIRST, then the shared set.</summary>
    public Targa Texture(RideAssets ride, string materialName)
    {
        var stem = Path.GetFileNameWithoutExtension(materialName);   // "m_back.ssh" -> "m_back"
        WadArchive.Entry e = null;
        if (!ride.Textures.TryGetValue(stem, out e)) SharedTextures.TryGetValue(stem, out e);
        if (e == null) return null;
        try { return new Targa(Wad.Read(e)); } catch { return null; }
    }
}
