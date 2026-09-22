using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>Everything the viewer can show, read straight out of the user's own disc.
///
/// ⚠ BYO-disc: nothing is shipped. The library opens whatever image the launcher found and reads
/// the archives in place.</summary>
public sealed class AssetLibrary : IDisposable
{
    public enum TextureFormat { Tga, Ssh }

    /// <summary>Decoded RGBA8, top row first, with the exact archive entry that supplied it.</summary>
    public sealed class TextureImage
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }
        public int PartialAlpha { get; }
        public int ClearTexels { get; }
        public TextureFormat Format { get; }
        public string SourceWad { get; }
        public string SourcePath { get; }

        /// <summary>Blends rather than cuts out. Same rule as <see cref="Targa.IsTranslucent"/>,
        /// because a texture must not change how it draws with which decoder read it.</summary>
        public bool Translucent => Targa.IsTranslucent(Width, Height, ClearTexels, PartialAlpha);

        internal TextureImage(int width, int height, byte[] pixels, TextureFormat format,
                              string sourceWad, string sourcePath)
        {
            Width = width; Height = height; Pixels = pixels;
            Format = format; SourceWad = sourceWad; SourcePath = sourcePath;
            // Match Targa's alpha classification so both decoders render with the same rules.
            for (int i = 3; i < pixels.Length; i += 4)
                if (pixels[i] < 16) ClearTexels++;
                else if (pixels[i] < 250) PartialAlpha++;
        }
    }

    public sealed class RideAssets
    {
        public string Name;                       // "Rides/Monkey"
        public WadArchive.Entry Model;            // .mps, or a legacy .MD2
        public WadArchive.Entry Animation;        // the .aps beside it
        public WadArchive.Entry Companion;        // .mtr for a legacy .MD2 only
    }

    readonly Disc _disc;
    public WadArchive Wad { get; private set; }
    public string WadName { get; private set; }
    public List<RideAssets> Rides { get; } = new();
    /// <summary>The shared texture set, keyed by filename including extension, used when nothing
    /// nearer to the model has one. TGA and SSH entries with the same stem remain separate.</summary>
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

    /// <summary>The console executable, for the tables that live in it. ⚠ Null when the disc is a
    /// different build: nothing here is baked, so a table that cannot be read is a missing feature
    /// rather than a wrong one.</summary>
    public byte[] Executable()
    {
        var f = _disc.Files().FirstOrDefault(
            x => !x.IsDirectory && x.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        return f == null ? null : _disc.Read(f.Extent, f.Size);
    }

    public List<string> Wads() => WadFiles().Select(f => f.Path).ToList();

    /// <summary>The archives as disc entries, for a caller that wants to read one itself.</summary>
    public List<Disc.Entry> WadFiles() => _disc.Files()
        .Where(f => !f.IsDirectory && f.Path.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase))
        .ToList();

    WadArchive _generic;

    /// <summary>The shared archive, opened alongside whichever world is current and kept.
    ///
    /// ⭐ DATA.WAD holds what every park draws from -- the weather sprites, the water, the logo,
    /// the characters. `OpenWad` has exactly one archive open, so reading a generic asset while a
    /// park is loaded needs a SECOND one rather than a swap: swapping would throw away the park's
    /// index and everything built from it.</summary>
    public byte[] ReadGeneric(string pathContains)
    {
        if (_generic == null)
        {
            var f = WadFiles().FirstOrDefault(
                x => x.Path.EndsWith("DATA.WAD", StringComparison.OrdinalIgnoreCase));
            if (f == null) return null;
            _generic = new WadArchive(_disc.Read(f.Extent, f.Size));
        }
        // ⚠ Skip ALIAS entries: they carry a path but no bytes, so taking the first match by name
        // can hand back an empty record while the real file sits later in the table.
        var e = _generic.Entries.FirstOrDefault(
            x => !WadArchive.IsAlias(x) && x.Path.EndsWith(pathContains, StringComparison.OrdinalIgnoreCase));
        return e == null ? null : _generic.Read(e);
    }

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
            if (ext is ".tga" or ".ssh")
            {
                var key = Path.GetFileName(e.Path);
                if (dir.Contains("Sharetex", StringComparison.OrdinalIgnoreCase)) { SharedTextures[key] = e; continue; }
                // A `textures/` subfolder dresses the model in the folder above it; a texture anywhere
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
            if (ext is ".mps" or ".md2") Get(e.Path).Model = e;
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
            if (r.Model.Path.EndsWith(".md2", StringComparison.OrdinalIgnoreCase))
            {
                string path = Path.ChangeExtension(r.Model.Path, ".mtr");
                r.Companion = Wad.Entries.SingleOrDefault(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
                // The co-located APS belongs to MPS, whose node indices and geometry differ.
                continue;
            }
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

    /// <summary>Load the selected model with its exact-stem legacy companion, when present.
    /// MTR validates node/triangle identities; texture choices are authored inside the model.</summary>
    public Model LoadModel(RideAssets ride) => new(Read(ride.Model),
        ride.Companion == null ? null : new Mtr(Read(ride.Companion)));

    /// <summary>The open archive's terrain models, in path order. Every world WAD has a `terrain/`
    /// folder with two of them; DATA, UI and the rest have none.</summary>
    public List<WadArchive.Entry> TerrainModels() => Wad.Entries
        .Where(e => !WadArchive.IsAlias(e)
                 && e.Path.EndsWith(".mps", StringComparison.OrdinalIgnoreCase)
                 && e.Path.Contains("/terrain/", StringComparison.OrdinalIgnoreCase))
        .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The ground tile to floor the park with.
    ///
    /// ⚠ WHICH tile floors an empty park is a CHOICE, not a finding -- the plot has no geometry on
    /// the disc and terrain_2 is the same island rather than a tile set, so nothing states it. The
    /// RULE is what is defensible, and it is: a 64x64 under `/terrain/` or `sharetex` whose stem is
    /// `_bas` + A DIGIT, preferring the world's own initial, then `gr_bas` (ground) over `fl_bas`
    /// (floor), then lowest-numbered.
    ///
    /// ⚠⚠ `_bas` ALONE IS NOT THE TEST. It also matches ride and shop art -- `ab_base`,
    /// `TV_BASE_C_0`, `PURPLE_BASE`, `CR_base1b` -- and SPACE has 165 such files against 6 real
    /// ground tiles. The ground set mostly spells it `_bas1` where prop bases spell it `_base`.
    ///
    /// ⚠⚠ MOSTLY: that name test is 20 of 21, and THE PATH SCOPE IS WHAT CATCHES THE 21st. The
    /// exception is `bmp_bas3`, bumper-car art in `SPACE.WAD/Rides/bumper/textures`, spelled with
    /// a digit exactly like a tile. It stays out of the set because of WHERE it is, not what it is
    /// called -- so the terrain/sharetex scope above is load-bearing, not belt-and-braces. Drop it
    /// and trust the name alone and SPACE gets a bumper car in its floor. (tinyclaw)
    ///
    /// ⭐ `?gr_` is ground -- `jgr_`, `hgr_`. `hrk_` is ROCK, not a second hallow ground set, which
    /// is why `gr_bas` is preferred above rather than merely first alphabetically.
    ///
    /// ⚠⚠ AND NOT ONLY UNDER `/terrain/`. Looking there alone found nothing for HALLOW and fell
    /// back to a shared tile, when HALLOW's ground set (`hgr_bas2`, `hrk_bas1..7`) is in
    /// `HALLOW.WAD/sharetex` -- the same global pool that had `jgr_bas1`. Thanks to tinyclaw.
    ///
    /// ⚠ The world's own prefix is a PREFERENCE, never a requirement: FANTASY ships JUNGLE's
    /// `jgr_*` tiles and there is no `fgr_*` anywhere on the disc, so deriving the prefix from the
    /// world name works for three worlds and silently finds nothing for the fourth. (Those files
    /// are not copies -- FANTASY's `jgr_bas1` is a different image from JUNGLE's under the same
    /// name, so it has to resolve inside the open archive.)
    ///
    /// ⚠ Folded case on the WHOLE PATH: the disc mixes case in the directory too, `Sharetex` in
    /// JUNGLE against `sharetex` in HALLOW, and in the stem and the extension besides.</summary>
    public string GroundTileName()
    {
        char mine = char.ToLowerInvariant((WadName ?? "?").TrimStart('/').FirstOrDefault());
        var hits = Wad.Entries
            .Where(e => !WadArchive.IsAlias(e))
            .Select(e => (Path: e.Path, Low: e.Path.ToLowerInvariant()))
            .Where(e => (e.Low.EndsWith(".tga") || e.Low.EndsWith(".ssh"))
                     && (e.Low.Contains("/terrain/") || e.Low.Contains("/sharetex/")))
            .Select(e => System.IO.Path.GetFileNameWithoutExtension(e.Path))
            .Where(n => GroundTile.IsMatch(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => char.ToLowerInvariant(n[0]) == mine ? 0 : 1)
            .ThenBy(n => n.Contains("gr_bas", StringComparison.OrdinalIgnoreCase) ? 0
                       : n.Contains("fl_bas", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return hits.FirstOrDefault();
    }

    /// <summary>`_bas` followed by a digit -- the ground set, as opposed to every `_base` prop.</summary>
    static readonly System.Text.RegularExpressions.Regex GroundTile =
        new("_bas[0-9]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Every image in the open archive, in path order. Both TGA and SSH have managed
    /// decoders; material lookup prefers the pre-compression TGA source when available.</summary>
    public List<WadArchive.Entry> Images(bool includeSsh = true) => Wad.Entries
        .Where(e => e.Path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)
                 || (includeSsh && e.Path.EndsWith(".ssh", StringComparison.OrdinalIgnoreCase)))
        .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>A material's texture: prefer TGA, then SSH, walking the model's OWN folder first,
    /// then each folder above it (including the archive root), then the archive-wide shared set.
    ///
    /// ⚠ The order is the whole point, not a detail. A flat by-name search over JUNGLE.WAD once put
    /// Mumbo's sign on Crazy Ape, because 22 rides each ship a sign_eng.tga. Nearest of the preferred
    /// format wins.</summary>
    public TextureImage Texture(RideAssets ride, string materialName) =>
        TextureNear("/" + ride.Name, materialName);

    /// <summary>A texture for <paramref name="materialName"/>, looked up from
    /// <paramref name="modelPath"/>'s own folder outwards, then the shared pool: the complete TGA
    /// search before the SSH search. Null means no entry was found; decode failures throw with
    /// source and material context instead of silently turning into an untextured surface.
    ///
    /// ⚠ The owner PATH is the argument, not a ride. Resolving the terrain's materials against
    /// whichever ride happened to be selected walked /Rides/Monkey and found nothing, so 62 of its
    /// 81 materials fell through to Sharetex and came back null -- it rendered flat white and read
    /// as "the terrain has no textures" rather than "the lookup was pointed at the wrong folder".
    /// 62 of them sit in /terrain/textures/ and the other 19 genuinely are shared.</summary>
    public TextureImage TextureNear(string modelPath, string materialName)
    {
        var stem = Path.GetFileNameWithoutExtension(materialName);   // "m_back.ssh" -> "m_back"
        // Finish the TGA walk before considering SSH. Choosing by format inside each
        // folder would let a nearby SSH displace a TGA that the viewer already used farther out.
        var e = FindTexture(modelPath, stem + ".tga") ?? FindTexture(modelPath, stem + ".ssh");
        if (e == null) return null;
        try
        {
            if (e.Path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            {
                var tga = new Targa(Wad.Read(e));
                return new TextureImage(tga.Width, tga.Height, tga.Pixels, TextureFormat.Tga, WadName, e.Path);
            }
            var ssh = new Ssh(Wad.Read(e));
            return new TextureImage(ssh.Width, ssh.Height, ssh.Pixels, TextureFormat.Ssh, WadName, e.Path);
        }
        catch (Exception ex)
        {
            // A decode failure is not a missing entry. Keep it observable to callers and the audit.
            throw new InvalidDataException($"{WadName}{e.Path} for {modelPath} material '{materialName}': {ex.Message}", ex);
        }
    }

    WadArchive.Entry FindTexture(string modelPath, string filename)
    {
        WadArchive.Entry e = null;
        var start = modelPath;
        start = start[..Math.Max(start.LastIndexOf('/'), 0)];
        for (var d = start; e == null; d = d[..Math.Max(d.LastIndexOf('/'), 0)])
        {
            if (_folders.TryGetValue(d, out var f)) f.TryGetValue(filename, out e);
            // The empty string is the archive root, including the owner of /Textures/. Skipping
            // it made every root-level LOBBY model miss its own textures (and affected /Backup/).
            if (d.Length == 0) break;
        }
        if (e == null) SharedTextures.TryGetValue(filename, out e);
        return e;
    }
}
