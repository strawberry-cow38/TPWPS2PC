using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TPW.PS2.Data;
using TPWPS2Viewer;

if (args.Length == 0 || args.Skip(1).Any(a => a is not "--require-all-textures" and not "--inject-material-swap"))
{
    Console.Error.WriteLine("usage: MtrAudit disc.bin [--require-all-textures] [--inject-material-swap]");
    return 2;
}
try
{
    var asm = Assembly.GetExecutingAssembly();
    using var stream = asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(n => n.EndsWith("identities.json")));
    var expected = JsonSerializer.Deserialize<Identity[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    using var lib = new AssetLibrary(args[0]);
    var discovered = new List<string>();
    foreach (string wad in lib.Wads())
    {
        lib.OpenWad(wad);
        foreach (var e in lib.Wad.Entries.Where(e => e.Path.EndsWith(".mtr", StringComparison.OrdinalIgnoreCase)))
            discovered.Add(wad + e.Path);
    }
    var wanted = expected.Select(e => "/DATA/JUNGLE.WAD/" + Path.ChangeExtension(e.Path, ".mtr"));
    Require(discovered.Order().SequenceEqual(wanted.Order()), "MTR corpus differs from the four audited paths");
    lib.OpenWad("/DATA/JUNGLE.WAD");
    int checks = 0, nodes = 0, faces = 0, vertices = 0, missing = 0;
    var missingChoices = new HashSet<string>();
    foreach (var e in expected)
    {
        var ride = lib.Rides.Single(r => r.Name == e.Path);
        Require(ride.Companion != null && ride.Animation == null, $"{e.Path}: companion/APS pairing wrong");
        var model = lib.LoadModel(ride);
        if (args.Contains("--inject-material-swap") && e.Path.Contains("sgsquark/sgsquark"))
        {
            var data = lib.Read(ride.Model);
            int mesh = U32(data, 0x70) + 2 * 160, group = U32(data, mesh + 0x6c);
            Write(data, group, (uint)(U32(data, 0x50) + 7 * 8));
            model = new Model(data, new Mtr(lib.Read(ride.Companion)));
        }
        CheckIdentity(model, e);
        Require(Digest(model.Companion) == e.MtrDecodedSha256, $"{e.Path}: decoded MTR field identity changed");
        Require(GeometryDigest(model) == e.Md2GeometrySha256, $"{e.Path}: decoded positions/UVs/normals/face identity changed");
        var world = model.WorldTransforms();
        foreach (var node in model.Companion.Nodes)
        {
            int index = (int)node.OneBasedIndex - 1;
            Require(MatrixValues(world[model.NodeOffset(index)]).Zip(MatrixValues(node.Matrices[0]))
                .All(x => Math.Abs(x.First - x.Second) <= 0.0003f), $"{e.Path}/{node.Name}: world matrix identity");
        }
        foreach (var mesh in model.Meshes)
        {
            var (pos, uv, normals) = model.Vertices(mesh);
            var tris = model.Triangles(mesh);
            int normalTable = U32(model.D, mesh.Offset + 0x64), faceTable = U32(model.D, mesh.Offset + 0x70);
            for (int i = 0; i < tris.Count; i++)
            {
                var t = tris[i];
                int ni = BitConverter.ToUInt16(model.D, faceTable + i * 8);
                var n = new Vector3(BitConverter.ToSingle(model.D, normalTable + ni * 12),
                    BitConverter.ToSingle(model.D, normalTable + ni * 12 + 4), BitConverter.ToSingle(model.D, normalTable + ni * 12 + 8));
                float dot = Vector3.Dot(Vector3.Cross(pos[t.B] - pos[t.A], pos[t.C] - pos[t.A]), n);
                Require(dot > 0, $"{e.Path}/{mesh.Name}/face {i}: position/face-normal orientation mismatch");
            }
            faces += tris.Count;
            vertices += pos.Count;
        }
        foreach (var name in model.MaterialTextures.SelectMany(t => t).Distinct())
        {
            var texture = lib.TextureNear(ride.Model.Path, name);
            if (texture == null)
            {
                Console.WriteLine($"UNRESOLVED {e.Path}: {name}"); missing++;
                missingChoices.Add(e.Path + ":" + name);
            }
            else Require(texture.Pixels.Length == texture.Width * texture.Height * 4, $"{name}: invalid decoded texture");
        }
        var witness = e.Path.Contains("sgrace/") ? ("new02", 56, "dinohead1.tga")
            : e.Path.Contains("glove.") ? ("ears01", 0, "gloves.tga")
            : e.Path.Contains("sgsquark/sgsquark") ? ("sq_body", 52, "sq_feather.TGA")
            : ("hammer", 16, "gb_hammer2.tga");
        var namedMesh = model.Meshes.Single(m => m.Name == witness.Item1);
        int slot = model.Triangles(namedMesh)[witness.Item2].Material;
        Require(model.Materials[slot] == witness.Item3, $"{e.Path}: wrong named material witness");
        var image = lib.TextureNear(ride.Model.Path, model.Materials[slot]);
        Require(image != null, $"{e.Path}: witness texture disappeared");
        string expectedPath = "/" + e.Path[..e.Path.LastIndexOf('/')] + "/Textures/" + witness.Item3;
        Require(image.SourcePath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase), $"{e.Path}: wrong texture owner {image.SourcePath}");
        Console.WriteLine($"IDENTITY {e.Path} :: {namedMesh.Name} face {witness.Item2} -> slot {slot} {model.Materials[slot]} -> {image.SourceWad}{image.SourcePath} ({image.Width}x{image.Height})");
        nodes += model.Companion.Nodes.Count;
        checks += Mutations(lib.Read(ride.Model), lib.Read(ride.Companion), e);
    }
    Require(missingChoices.SetEquals(new[] {
        "Sideshow/sgrace/sgrace.MD2:dinobody.tga", "Sideshow/sgrace/sgrace.MD2:dinohead.tga",
        "Sideshow/sgsquark/sgsquark.MD2:sq_eye.tga" }), "legacy texture-resolution baseline changed");
    Console.WriteLine($"PASS: four pairs, {nodes} named nodes/local+world matrices, {vertices} vertex identities, {faces} face identities/material assignments/normals; {checks} rejected corruptions.");
    Console.WriteLine($"LIMITS: {missing} unresolved legacy texture choices; no PS2 MTR consumer, framebuffer comparison, legacy animation, or interpretation of format word 6 / count +0x10 / seven remaining matrices.");
    if (args.Contains("--require-all-textures") && missing > 0)
    {
        Console.Error.WriteLine($"MTR TEXTURE COVERAGE FAIL: {missing} unresolved choices");
        return 2;
    }
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine("MTR AUDIT FAIL: " + ex); return 2; }

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}
static int U32(byte[] d, int o) => checked((int)BitConverter.ToUInt32(d, o));
static void Write(byte[] d, int o, uint v) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(o), v);

static void CheckIdentity(Model model, Identity e)
{
    Require(model.Companion.Nodes.Select(n => n.Name).SequenceEqual(e.Nodes), $"{e.Path}: node names/order");
    Require(model.MaterialTextures.Count == e.Textures.Length, $"{e.Path}: material table size");
    for (int i = 0; i < e.Textures.Length; i++)
        Require(model.MaterialTextures[i].SequenceEqual(e.Textures[i]), $"{e.Path}: texture choices for slot {i}");
    Require(model.Meshes.Count == e.Meshes.Length, $"{e.Path}: mesh table size");
    foreach (var mesh in e.Meshes)
    {
        var actual = model.Meshes.Single(m => m.Name == mesh.Name);
        var tris = model.Triangles(actual);
        Require(tris.Count == mesh.Groups.Sum(g => g.Count), $"{e.Path}/{mesh.Name}: face coverage");
        foreach (var g in mesh.Groups)
            for (int f = g.First; f < g.First + g.Count; f++)
                Require(tris[f].Material == g.Material, $"{e.Path}/{mesh.Name}/face {f}: expected slot {g.Material} '{e.Textures[g.Material][0]}', got {tris[f].Material}");
    }
}

static int Mutations(byte[] md, byte[] mtr, Identity e)
{
    int checks = 0, table = U32(mtr, 20);
    void Reject(string label, Action run)
    {
        try { run(); }
        catch (InvalidDataException) { checks++; return; }
        throw new Exception($"negative control accepted: {e.Path} / {label}");
    }
    void MtrWord(string label, int offset, uint value)
    {
        var bad = (byte[])mtr.Clone(); Write(bad, offset, value);
        Reject(label, () => new Model(md, new Mtr(bad)));
    }
    Reject("truncated header", () => new Mtr(mtr[..20]));
    Reject("truncated last record", () => new Mtr(mtr[..^1]));
    MtrWord("bad magic", 0, 0);
    MtrWord("unknown format word", 4, 7);
    MtrWord("count overflow", 8, uint.MaxValue);
    MtrWord("table into header", 20, 0);
    MtrWord("overlapping arrays", table + 0x34c, (uint)U32(mtr, table + 0x348));
    MtrWord("corner out of range", U32(mtr, table + 0x34c), 3);
    MtrWord("wrong ordinal", table + 0x100, 2);
    MtrWord("wrong local matrix with same sizes", table + 0x144, 0x40000000);
    MtrWord("wrong source triangle with same sizes", U32(mtr, table + 0x354), 0xfffffffe);
    var renamed = (byte[])mtr.Clone(); renamed[table] ^= 1;
    Reject("wrong name with same size", () => new Model(md, new Mtr(renamed)));
    var sameDomain = (byte[])mtr.Clone();
    int map = U32(mtr, table + 0x34c);
    Write(sameDomain, map, (BitConverter.ToUInt32(sameDomain, map) + 1) % 3);
    Reject("valid-domain map mutation", () => Require(Digest(new Mtr(sameDomain)) == e.MtrDecodedSha256, "map digest changed"));
    Reject("wrong source corner with same counts and legal indices", () => new Model(md, new Mtr(sameDomain)));
    var flippedUv = (byte[])md.Clone();
    int uvTable = U32(md, U32(md, 0x70) + 0x68);
    Write(flippedUv, uvTable, BitConverter.ToUInt32(md, uvTable + 16));
    Write(flippedUv, uvTable + 16, BitConverter.ToUInt32(md, uvTable));
    Reject("UV component swap with identical counts", () => Require(
        GeometryDigest(new Model(flippedUv, new Mtr(mtr))) == e.Md2GeometrySha256, "UV identity changed"));
    if (e.Textures.Length > 1)
    {
        var swap = (byte[])md.Clone(); int mesh = U32(md, 0x70), group = U32(md, mesh + 0x6c);
        int refs = U32(md, 0x50), slot = (U32(md, group) - refs) / 8;
        Write(swap, group, (uint)(refs + (slot + 1) % e.Textures.Length * 8));
        Reject("valid material swap with identical counts", () => CheckIdentity(new Model(swap, new Mtr(mtr)), e));
    }
    return checks;
}

static IEnumerable<float> MatrixValues(Matrix4x4 m) => new[] {
    m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,
    m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44 };

static string Digest(Mtr mtr)
{
    var text = new StringBuilder();
    foreach (var n in mtr.Nodes)
    {
        text.Append(n.Name).Append('\n').Append(n.OneBasedIndex).Append('\n');
        foreach (var m in n.Matrices)
            text.AppendJoin(',', MatrixValues(m).Select(f => BitConverter.SingleToUInt32Bits(f).ToString("X8"))).Append('\n');
        text.AppendJoin(',', n.VertexSourceFaces).Append('\n').AppendJoin(',', n.VertexSourceCorners).Append('\n');
        text.AppendJoin(',', n.SourceTriangles.SelectMany(t => new[] { t.A, t.B, t.C })).Append('\n');
    }
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
}

static string GeometryDigest(Model model)
{
    var text = new StringBuilder();
    foreach (var mesh in model.Meshes)
    {
        text.Append(mesh.Name).Append('\n');
        var (positions, uvs, normals) = model.Vertices(mesh);
        for (int v = 0; v < positions.Count; v++)
        {
            var p = positions[v]; var uv = uvs[v]; var n = normals[v];
            text.AppendJoin(',', new[] { p.X, p.Y, p.Z, uv.X, uv.Y, n.X, n.Y, n.Z }
                .Select(f => BitConverter.SingleToUInt32Bits(f).ToString("X8"))).Append('\n');
        }
        foreach (var t in model.Triangles(mesh))
            text.Append(t.A).Append(',').Append(t.B).Append(',').Append(t.C).Append(',').Append(t.Material).Append('\n');
    }
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
}

sealed record Identity(string Path, string MtrDecodedSha256, string[] Nodes, string[][] Textures, MeshIdentity[] Meshes, string Md2GeometrySha256);
sealed record MeshIdentity(string Name, GroupIdentity[] Groups);
sealed record GroupIdentity(int First, int Count, int Material);
