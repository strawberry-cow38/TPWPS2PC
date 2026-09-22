using System.Numerics;

namespace TPW.PS2.Data;

public sealed partial class Model
{
    public const uint LegacyMd2Magic = 0x1CD15D46;
    public bool IsLegacyMd2 { get; }
    /// <summary>Validated node/matrix/index metadata. Materials come from MD2 itself.</summary>
    public Mtr Companion { get; }
    int _md2NodeCount;
    readonly Dictionary<int, List<Triangle>> _md2Triangles = new();
    readonly Dictionary<int, (List<Vector3> Pos, List<Vector2> Uv, List<Vector3> Normal)> _md2Vertices = new();

    // This is the on-disc legacy layout, not the MPS/VU layout under an extra accepted magic.
    // Evidence and limits (including absent PS2 consumer) are in findings/mtr.md.
    void ReadMd2()
    {
        var r = new CheckedBinary(D, "legacy MD2");
        r.Range(0, 0xb8);
        if (r.U32(4) != 0xdd || r.U32(8) != 0xcb)
            throw new InvalidDataException("unsupported legacy MD2 revision");
        int materialCount = r.U16(0x36), meshCount = r.U16(0x44);
        _md2NodeCount = r.U16(0x42);
        if (meshCount > _md2NodeCount) throw new InvalidDataException("MD2: more meshes than nodes");
        int materialRefs = r.Table(0x50, materialCount, 8);
        int descriptors = r.Table(0x54, materialCount, 16);
        for (int i = 0; i < materialCount; i++)
        {
            int b = descriptors + i * 16, count = r.U16(b + 10);
            if (count == 0) throw new InvalidDataException($"MD2: material {i} has no textures");
            int names = r.Table(b + 12, count, 20);
            var textures = Enumerable.Range(0, count).Select(j => r.Name(names + j * 20, 20)).ToArray();
            MaterialTextures.Add(textures); Materials.Add(textures[0]);
        }
        MeshTable = r.Table(0x70, meshCount, 160);
        HelperTable = r.Table(0x74, _md2NodeCount - meshCount, 88);
        var parents = new Dictionary<int, int>();
        for (int i = 0; i < _md2NodeCount; i++)
        {
            int b = NodeOffsetLegacy(i), name = r.Table(b + 0x54, 1, 1);
            r.Name(name, D.Length - name);
            if (r.U32(b + 0x50) != i) throw new InvalidDataException($"MD2: unexpected node ordinal {i}");
            r.Matrix(b + 0x10);
            parents.Add(b, r.Int(b + 4));
        }
        foreach (int b in parents.Keys)
        {
            var seen = new HashSet<int>(); int at = b;
            while (at != 0)
            {
                if (!seen.Add(at) || !parents.TryGetValue(at, out at))
                    throw new InvalidDataException("MD2: invalid or cyclic parent link");
            }
        }
        for (int i = 0; i < meshCount; i++)
        {
            int b = MeshTable + i * 160;
            int points = r.U16(b + 0x58), groups = r.U16(b + 0x5a);
            int nf = r.U16(b + 0x5c), nv = r.U16(b + 0x5e);
            int positions = r.Table(b + 0x60, (points + 3) / 4, 48);
            int normals = r.Table(b + 0x64, nv + nf, 12);
            int uvs = r.Table(b + 0x68, (nv + 3) / 4, 32);
            int groupTable = r.Table(b + 0x6c, groups, 16);
            int faceTable = r.Table(b + 0x70, nf, 8);
            int remap = r.Table(b + 0x94, nv, 2);
            var mesh = new Mesh
            {
                Index = i, Offset = b, Parent = r.U32(b + 4),
                Name = r.Name(r.Int(b + 0x54), D.Length - r.Int(b + 0x54)),
                Local = r.Matrix(b + 0x10), VertexCount = nv, FaceCount = nf,
                BoundsMin = new(r.Float(b + 0x78), r.Float(b + 0x7c), r.Float(b + 0x80)),
                BoundsMax = new(r.Float(b + 0x84), r.Float(b + 0x88), r.Float(b + 0x8c)),
            };
            Meshes.Add(mesh);
            var pos = new List<Vector3>(); var uv = new List<Vector2>(); var nor = new List<Vector3>();
            for (int v = 0; v < nv; v++)
            {
                int point = r.U16(remap + v * 2);
                if (point >= points) throw new InvalidDataException($"MD2: {mesh.Name} vertex {v} outside position table");
                // Four-wide structure-of-arrays: XXXX YYYY ZZZZ, and UUUU VVVV.
                int p = positions + point / 4 * 48 + point % 4 * 4;
                int t = uvs + v / 4 * 32 + v % 4 * 4;
                pos.Add(new(r.Float(p), r.Float(p + 16), r.Float(p + 32)));
                uv.Add(new(r.Float(t), r.Float(t + 16)));
                nor.Add(new(r.Float(normals + v * 12), r.Float(normals + v * 12 + 4), r.Float(normals + v * 12 + 8)));
            }
            var faceMaterials = Enumerable.Repeat(-1, nf).ToArray();
            for (int g = 0; g < groups; g++)
            {
                int o = groupTable + g * 16, pointer = r.Int(o);
                int material = (pointer - materialRefs) / 8;
                if (pointer < materialRefs || (pointer - materialRefs) % 8 != 0 || material >= materialCount)
                    throw new InvalidDataException($"MD2: {mesh.Name} group {g} has invalid material reference");
                int first = r.U16(o + 4), count = r.U16(o + 6);
                if ((long)first + count > nf || count == 0)
                    throw new InvalidDataException($"MD2: {mesh.Name} group {g} outside faces");
                // Group records are NOT in face order (e.g. sq_body). Their explicit ranges own faces.
                for (int f = first; f < first + count; f++)
                {
                    if (faceMaterials[f] != -1) throw new InvalidDataException($"MD2: overlapping groups on {mesh.Name}");
                    faceMaterials[f] = material;
                }
            }
            var triangles = new List<Triangle>();
            for (int f = 0; f < nf; f++)
            {
                int o = faceTable + f * 8, normal = r.U16(o);
                int a = r.U16(o + 2), c = r.U16(o + 4), e = r.U16(o + 6);
                if (normal < nv || normal >= nv + nf || a >= nv || c >= nv || e >= nv || faceMaterials[f] < 0)
                    throw new InvalidDataException($"MD2: invalid or unassigned face {mesh.Name}/{f}");
                triangles.Add(new(a, c, e, faceMaterials[f]));
            }
            _md2Vertices.Add(i, (pos, uv, nor)); _md2Triangles.Add(i, triangles);
        }

        // Meshes.Count is still growing while the node table is validated.
        int NodeOffsetLegacy(int i) => i < meshCount ? MeshTable + i * 160 : HelperTable + (i - meshCount) * 88;
    }
}
