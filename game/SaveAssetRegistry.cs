using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

/// <summary>
/// Disc-only asset identity, not a world snapshot. The application supplies BOTH the disc path
/// and an exact origin allowlist; neither is taken from a save. No extraction, offsets, runtime
/// callbacks or Create replay. Single-threaded, quiescent use. Dispose after staging finishes.
/// Shared core readers expose mutable collections: borrowers MUST treat them as read-only.
/// PrivateModel is the explicit writable path. Private source bytes are never lent to readers.
/// </summary>
public sealed class SaveAssetRegistry : IDisposable
{
    public enum AssetKind { Raw, Model, Animation, Program, RideDefinition, CompiledDefinition }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Origin(string Archive, string Entry, AssetKind Kind);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record DiscIdentity(long Length, string Sha256);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record AssetEntry(string Id, Origin Origin, int Length, string Sha256);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Manifest(int Version, DiscIdentity Disc, AssetEntry[] Assets);

    const int Limit = 16384, MaxManifestBytes = 16 * 1024 * 1024;
    readonly AssetLibrary library;
    readonly HashSet<Origin> allowed;
    readonly Dictionary<string, WadArchive> archives = new(StringComparer.Ordinal);
    readonly Dictionary<string, AssetEntry> entries = new(StringComparer.Ordinal);
    readonly Dictionary<string, byte[]> bytes = new(StringComparer.Ordinal);
    readonly Dictionary<string, object> shared = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> definitionSignatures = new(StringComparer.Ordinal);
    readonly Dictionary<object, string> sourceIds = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<string, Dictionary<string, string>> catalogues = new(StringComparer.Ordinal);
    readonly bool capture;
    bool disposed;
    public DiscIdentity Disc { get; }

    static void Require(bool ok, string why)
    { if (!ok) throw new InvalidDataException("Save asset registry: " + why); }
    static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    static bool Digest(string s) => s != null && s.Length == 64 && s.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    static void ArchivePath(string s)
    {
        Require(s != null && s.Length is > 1 and <= 1024 && s[0] == '/' &&
            !s.Contains('\\') && !s.Contains(':') && !s.Any(char.IsControl) &&
            s[1..].Split('/').All(p => p.Length > 0 && p != "." && p != ".."), "invalid archive member path");
    }
    static void Validate(Origin o)
    {
        Require(o != null && Enum.IsDefined(o.Kind), "origin/kind");
        ArchivePath(o.Archive); ArchivePath(o.Entry);
        Require(o.Archive.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase), "not a WAD");
        string ext = Path.GetExtension(o.Entry).ToLowerInvariant();
        Require(o.Kind switch { AssetKind.Raw => true, AssetKind.Model => ext == ".mps",
            AssetKind.Animation => ext == ".aps", AssetKind.Program => ext == ".rse",
            AssetKind.RideDefinition or AssetKind.CompiledDefinition => ext == ".sam", _ => false }, "kind/extension mismatch (legacy MD2 unsupported)");
    }
    static DiscIdentity Fingerprint(string trustedPath)
    {
        using var stream = File.OpenRead(trustedPath);
        long length = stream.Length;
        string hash = Convert.ToHexString(SHA256.HashData(stream));
        Require(length == stream.Length, "disc changed while hashing");
        return new(length, hash);
    }
    string Identity(Origin o, int length, string hash) => "disc-asset/v1/" + Hash(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { Disc, Origin = o, Length = length, Sha256 = hash })));

    /// <summary>Capture may register only these exact application-approved archive members.</summary>
    public static SaveAssetRegistry Capture(string trustedDiscPath, IEnumerable<Origin> trustedAllowedOrigins)
        => new(trustedDiscPath, trustedAllowedOrigins, null);
    /// <summary>Fresh registry. All manifest members are read and verified before returning.</summary>
    public static SaveAssetRegistry Open(string trustedDiscPath, IEnumerable<Origin> trustedAllowedOrigins, Manifest manifest)
    { ArgumentNullException.ThrowIfNull(manifest); return new(trustedDiscPath, trustedAllowedOrigins, manifest); }

    SaveAssetRegistry(string path, IEnumerable<Origin> policy, Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(policy);
        allowed = new HashSet<Origin>();
        foreach (var origin in policy) { Validate(origin); Require(allowed.Count < Limit, "policy bound"); allowed.Add(origin); }
        Disc = Fingerprint(path);
        capture = manifest == null;
        if (!capture) Require(manifest.Version == 1 && manifest.Disc == Disc && manifest.Assets != null &&
            manifest.Assets.Length <= Limit, "manifest version/disc fingerprint/length/count mismatch");
        library = new AssetLibrary(path);
        try {
            if (!capture) foreach (var e in manifest.Assets) {
                Require(e != null && e.Id != null && e.Length > 0 && Digest(e.Sha256), "invalid asset entry");
                Validate(e.Origin);
                Require(e.Id == Identity(e.Origin, e.Length, e.Sha256), "asset ID/fingerprint mismatch");
                Require(!entries.ContainsKey(e.Id), "duplicate asset ID");
                var data = ReadOrigin(e.Origin);
                Require(data.Length == e.Length && Hash(data) == e.Sha256, "content fingerprint/length mismatch");
                entries.Add(e.Id, e); bytes.Add(e.Id, data);
            }
        } catch { library.Dispose(); throw; }
    }
    void Alive() { ObjectDisposedException.ThrowIf(disposed, this); }
    WadArchive Archive(string name)
    {
        Alive();
        if (archives.TryGetValue(name, out var wad)) return wad;
        var matches = library.WadFiles().Where(f => f.Path == name).ToArray();
        Require(matches.Length == 1, "unknown/ambiguous exact archive: " + name);
        wad = new WadArchive(library.ReadDisc(matches[0])); archives.Add(name, wad); return wad;
    }
    byte[] ReadOrigin(Origin origin)
    {
        Alive(); Validate(origin); Require(allowed.Contains(origin), "origin not allowed by application");
        var wad = Archive(origin.Archive);
        var matches = wad.Entries.Where(e => e.Path == origin.Entry && !WadArchive.IsAlias(e)).ToArray();
        Require(matches.Length == 1, "unknown/ambiguous exact archive entry: " + origin.Entry);
        Require(matches[0].DecompressedSize is > 0 and <= 128 * 1024 * 1024, "asset size bound");
        return wad.Read(matches[0]);
    }
    /// <summary>Verify supplied original bytes against the disc, never label an object by name alone.</summary>
    public string Register(Origin origin, byte[] originalBytes)
    {
        Alive(); Require(capture, "cold registry is sealed"); ArgumentNullException.ThrowIfNull(originalBytes);
        var data = ReadOrigin(origin);
        Require(data.AsSpan().SequenceEqual(originalBytes), "source bytes do not match claimed origin");
        string hash = Hash(data), id = Identity(origin, data.Length, hash);
        if (!entries.ContainsKey(id)) {
            Require(entries.Count < Limit, "asset count bound");
            entries.Add(id, new(id, origin, data.Length, hash)); bytes.Add(id, data);
        }
        return id;
    }
    public string RegisterModel(Origin origin, Model source)
    {
        Require(origin?.Kind == AssetKind.Model && source != null && !source.IsLegacyMd2, "model source");
        return Remember(source, Register(origin, source.D));
    }
    public string RegisterAnimation(Origin origin, Aps source)
    {
        Require(origin?.Kind == AssetKind.Animation && source != null, "animation source");
        return Remember(source, Register(origin, source.D));
    }
    string Remember(object source, string id)
    {
        Require(!sourceIds.TryGetValue(source, out var previous) || previous == id, "source registered under different origin");
        sourceIds[source] = id; return id;
    }
    public string Identify(object source)
    {
        Alive(); Require(source != null && sourceIds.ContainsKey(source), "unregistered source identity");
        string id = sourceIds[source];
        if (source is Model m) Require(Hash(m.D) == entries[id].Sha256, "source model mutated; use original asset, not private instance");
        if (source is Aps a) Require(Hash(a.D) == entries[id].Sha256, "source animation mutated");
        if (source is RideDefinition d) Require(DefinitionSignature(d) == definitionSignatures[id], "source definition mutated");
        return id;
    }
    public AssetEntry Describe(string id)
    { Alive(); Require(id != null && entries.ContainsKey(id), "unknown asset ID"); return entries[id]; }
    void Kind(string id, AssetKind kind) => Require(Describe(id).Origin.Kind == kind, "asset kind mismatch");
    /// <summary>Always a copy, including Raw assets. No writable source storage escapes.</summary>
    public byte[] CopyBytes(string id) { Describe(id); return (byte[])bytes[id].Clone(); }
    T Shared<T>(string id, AssetKind kind, Func<byte[], T> parse) where T : class
    {
        Kind(id, kind);
        if (!shared.TryGetValue(id, out var value)) { value = parse(CopyBytes(id)); shared.Add(id, value); Remember(value, id); }
        if (value is Model m) Require(Hash(m.D) == entries[id].Sha256, "shared model bytes mutated");
        if (value is Aps a) Require(Hash(a.D) == entries[id].Sha256, "shared animation bytes mutated");
        return (T)value;
    }
    public Model SharedModel(string id) => Shared(id, AssetKind.Model, d => new Model(d));
    public Model PrivateModel(string id) { Kind(id, AssetKind.Model); return new Model(CopyBytes(id)); }
    public Aps SharedAnimation(string id) => Shared(id, AssetKind.Animation, d => new Aps(d));
    public RseProgram SharedProgram(string id) => Shared(id, AssetKind.Program, d => new RseProgram(d));

    /// <summary>Use the existing catalogue's exact model/APS pairing, not a duplicate SAM DTO.
    /// Compiled definitions explicitly select the existing DBA/text identity join; they cannot masquerade as SAM-only assets.</summary>
    public RideDefinition SharedDefinition(string id)
    {
        var kind=Describe(id).Origin.Kind;
        Require(kind is AssetKind.RideDefinition or AssetKind.CompiledDefinition,"definition kind");
        var definition = Shared(id, kind, _ => {
        var origin = entries[id].Origin;
        var catalogue = new RideCatalogue(); catalogue.AddWad(Archive(origin.Archive), origin.Archive);
        var result=catalogue.All.Single(d => d.Source == origin.Archive + origin.Entry);
        if(kind==AssetKind.CompiledDefinition) {
            // Fixed application decoder inputs, NOT paths chosen by the manifest. Full-disc
            // identity covers DBA and localization key tables, in addition to the SAM hash.
            var data=Archive("/DATA/DATA.WAD");var entry=data.Find("/arsdb.dba")??throw new InvalidDataException("compiled DBA missing");
            var text=TextDatabase.Load(data,"eur")??throw new InvalidDataException("compiled identity text missing");
            new CompiledAssets(new AssetResourceDatabase(data.Read(entry)),text).Attach(new[]{result},out var report);
            Require(result.CompiledEntry!=null,"compiled identity join missing");
        }
        return result;
        });
        string signature = DefinitionSignature(definition);
        if (definitionSignatures.TryGetValue(id, out var expected)) Require(signature == expected, "shared definition mutated");
        else definitionSignatures.Add(id, signature);
        return definition;
    }
    static string DefinitionSignature(RideDefinition d)
    {
        return JsonSerializer.Serialize(new { d.Source, d.ModelPath, d.AnimationPath, d.ModelAmbiguous,
            CompiledKey=d.CompiledEntry?.Key,CompiledPayload=d.CompiledEntry?.Payload.ToArray(),Shop=d.Compiled,
            Fields = d.Fields.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            Blocks = d.Blocks.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray() });
    }
    public string RegisterDefinition(Origin origin, RideDefinition source, byte[] originalSamBytes)
    {
        Require(origin?.Kind is AssetKind.RideDefinition or AssetKind.CompiledDefinition && source != null, "definition source");
        Require((origin.Kind==AssetKind.CompiledDefinition)==(source.CompiledEntry!=null),"explicit compiled definition kind");
        string id = Register(origin, originalSamBytes);
        Require(DefinitionSignature(source) == DefinitionSignature(SharedDefinition(id)), "definition differs from disc catalogue");
        return Remember(source, id);
    }

    /// <summary>RseProgram does not retain public raw bytes. Verify all public VM inputs including
    /// every original string boundary, not script NAME or a guessed filename. Hash identity remains
    /// that of verified disc bytes. No machine is created or executed.</summary>
    public string RegisterProgram(Origin origin, RseProgram source, byte[] originalProgramBytes)
    {
        Require(origin?.Kind == AssetKind.Program && source != null, "program source");
        string id = Register(origin, originalProgramBytes);
        var expected = SharedProgram(id);
        string Signature(RseProgram p) => JsonSerializer.Serialize(new { p.VariableCount, p.StackSize, p.SliceBudget,
            p.LimboCapacity, p.BounceCapacity, p.WalkCapacity, p.CodeWords, p.VariableNames,
            Instructions = p.Instructions.Select(i => new { i.Address, i.Opcode, Words = i.Operands.Select(o => o.Word).ToArray() }).ToArray() });
        Require(Signature(source) == Signature(expected), "program VM inputs differ");
        int start = checked(56 + expected.CodeWords * 4);
        int length = BitConverter.ToInt32(bytes[id], start - 4);
        for (int offset = 0; offset < length; offset++)
            if (offset == 0 || bytes[id][start + offset - 1] == 0)
                Require(source.StringAt(offset) == expected.StringAt(offset), "program strings differ");
        return Remember(source, id);
    }
    static string DirectoryOf(string path) => path[..(path.LastIndexOf('/') + 1)];
    string Scope(Origin o) => "disc-scope/v1/" + Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Disc, o.Archive, Directory = DirectoryOf(o.Entry) })));

    /// <summary>Explicitly verify the COMPLETE sibling RSE directory, including allowed origins and
    /// parsability. Capture adds its closure; cold mode requires that closure already in the manifest.
    /// Unsupported RSE formats fail, never guessed or silently omitted. Call BEFORE owner hydration.
    /// No scope exists until this succeeds; ResolveChild otherwise throws rather than disabling spawn.</summary>
    public void VerifyProgramCatalogue(string programId)
    {
        Kind(programId, AssetKind.Program); var origin = entries[programId].Origin;
        string scope = Scope(origin); if (catalogues.ContainsKey(scope)) return;
        var catalogue = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Archive(origin.Archive).Entries.Where(e => !WadArchive.IsAlias(e) &&
            DirectoryOf(e.Path).Equals(DirectoryOf(origin.Entry), StringComparison.OrdinalIgnoreCase) &&
            e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase))) {
            var o = new Origin(origin.Archive, e.Path, AssetKind.Program);
            Require(allowed.Contains(o), "sibling program missing from trusted catalogue policy");
            string id;
            if (capture) id = Register(o, ReadOrigin(o));
            else { var match = entries.Values.SingleOrDefault(a => a.Origin == o);
                Require(match != null, "incomplete saved program catalogue"); id = match.Id; }
            SharedProgram(id); // Parse actual program, not guessed script names.
            Require(catalogue.TryAdd(e.Name, id), "ambiguous sibling program name");
        }
        Require(catalogue.Values.Contains(programId), "program absent from catalogue");
        catalogues.Add(scope, catalogue);
    }
    public ParkSim.ScriptAsset ResolveProgram(string id)
    {
        var program = SharedProgram(id); string scope = Scope(entries[id].Origin);
        Require(catalogues.ContainsKey(scope), "program catalogue not verified; call VerifyProgramCatalogue before staging");
        return new(id, program, scope);
    }
    public ParkSim.ScriptAsset IdentifyProgram(RseProgram source)
    {
        var asset = ResolveProgram(Identify(source));
        // Capture bindings must return the actual registered VM program, not our parsed twin.
        return asset with { Program = source };
    }
    public ParkSim.ScriptAsset ResolveChild(string sourceId, string operand)
    {
        var parent = ResolveProgram(sourceId);
        Require(!string.IsNullOrEmpty(operand) && operand.Length <= 255 && !operand.Contains('/') &&
            !operand.Contains('\\') && !operand.Contains(':') && !operand.Any(char.IsControl) && operand is not "." and not "..", "invalid sibling operand");
        return catalogues[parent.SiblingScope].TryGetValue(operand, out var id) ? ResolveProgram(id) : null;
    }

    /// <summary>Root joins stay in their owners. These use caller-supplied saved IDs, never paths as IDs.
    /// Repeated IDs preserve character Model/Animation identity; ride slots receive private models.</summary>
    public Dictionary<string, Model> CharacterModels(IEnumerable<string> ids) => ids.ToDictionary(
        id => Describe(id).Origin.Entry, SharedModel, StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Aps> CharacterAnimations(IEnumerable<string> ids) => ids.ToDictionary(
        id => Describe(id).Origin.Entry, SharedAnimation, StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, Model> PrivateRideModels(IReadOnlyDictionary<int, string> slots)
        => slots.ToDictionary(p => p.Key, p => PrivateModel(p.Value));
    public Manifest Export() { Alive(); return new(1, Disc, entries.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToArray()); }
    /// <summary>Streams are opened by the application; manifests never choose filesystem paths.</summary>
    public void WriteManifest(Stream destination) => JsonSerializer.Serialize(destination, Export());
    public static Manifest ReadManifest(Stream source)
    {
        using var buffer = new MemoryStream(); var chunk = new byte[8192]; int n;
        while ((n = source.Read(chunk, 0, chunk.Length)) != 0) {
            Require(buffer.Length + n <= MaxManifestBytes, "manifest byte bound"); buffer.Write(chunk, 0, n);
        }
        return JsonSerializer.Deserialize<Manifest>(buffer.ToArray(), new JsonSerializerOptions { MaxDepth = 16 })
            ?? throw new InvalidDataException("Null asset manifest");
    }
    public void Dispose() { if (disposed) return; disposed = true; library.Dispose(); archives.Clear(); bytes.Clear(); shared.Clear(); sourceIds.Clear(); }
}
