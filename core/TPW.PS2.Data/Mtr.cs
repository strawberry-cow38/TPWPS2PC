using System.Numerics;

namespace TPW.PS2.Data;

/// <summary>Legacy MD2 node/matrix/index companion, stamp 0x2E5915AF.
/// This is NOT an established material table. No texture names or PS2 consumer were found.
/// The structural layout and companion identities are documented in findings/mtr.md.</summary>
public sealed class Mtr
{
    public const uint Magic = 0x2E5915AF;
    public const int NodeSize = 0x358;
    readonly CheckedBinary _r;

    public sealed record Node(int Offset, string Name, uint OneBasedIndex,
        IReadOnlyList<Matrix4x4> Matrices, IReadOnlyList<uint> VertexSourceFaces,
        IReadOnlyList<uint> VertexSourceCorners, IReadOnlyList<SourceTriangle> SourceTriangles,
        IReadOnlyList<SourceTriangle> UnduplicatedFaces)
    {
        /// <summary>Byte-identical to the named MD2 node's local matrix in all four pairs.</summary>
        public Matrix4x4 Local => Matrices[1];
    }
    public readonly record struct SourceTriangle(uint A, uint B, uint C);

    /// <summary>6 in this corpus; likely a revision, but no consumer/version check is known.</summary>
    public uint FormatWord { get; }
    public int MeshCount { get; }
    /// <summary>Matches MD2 +0x48; its meaning has not been established.</summary>
    public uint UnknownCount { get; }
    public int NodeTable { get; }
    public IReadOnlyList<Node> Nodes { get; }

    public Mtr(byte[] data)
    {
        _r = new CheckedBinary(data, "MTR");
        _r.Range(0, 24);
        if (_r.U32(0) != Magic) throw new InvalidDataException("not MTR");
        FormatWord = _r.U32(4);
        // A supported layout discriminator, not a claim that the engine interprets it as a version.
        if (FormatWord != 6) throw new InvalidDataException($"MTR: unsupported format word {FormatWord}");
        int count = _r.Int(8);
        MeshCount = _r.Int(12);
        UnknownCount = _r.U32(16);
        NodeTable = _r.Table(20, count, NodeSize);
        if (MeshCount > count || NodeTable < 24 || (NodeTable & 3) != 0 ||
            (long)NodeTable + (long)count * NodeSize != data.Length)
            throw new InvalidDataException("MTR: invalid node table or trailing data");

        var nodes = new List<Node>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        int cursor = 24;
        for (int i = 0; i < count; i++)
        {
            int b = NodeTable + i * NodeSize;
            string name = _r.Name(b, 256);
            uint id = _r.U32(b + 0x100);
            if (!names.Add(name) || id != i + 1)
                throw new InvalidDataException($"MTR: duplicate node or unexpected ordinal at {i}");
            var matrices = Enumerable.Range(0, 9).Select(k => _r.Matrix(b + 0x104 + k * 64)).ToArray();
            int nv = _r.Int(b + 0x344), nf = _r.Int(b + 0x350);
            if (i >= MeshCount && (nv != 0 || nf != 0))
                throw new InvalidDataException($"MTR: helper {name} has index arrays");
            int a = ArrayAt(b + 0x348, nv, 4), c = ArrayAt(b + 0x34c, nv, 4);
            int t = ArrayAt(b + 0x354, nf, 12);
            var mapA = new uint[nv]; var mapB = new uint[nv];
            for (int v = 0; v < nv; v++)
            {
                mapA[v] = _r.U32(a + v * 4); mapB[v] = _r.U32(c + v * 4);
                if (mapA[v] >= nf || mapB[v] > 2)
                    throw new InvalidDataException($"MTR: {name} index pair {v} outside observed face/corner domains");
            }
            var triangles = new SourceTriangle[nf];
            for (int f = 0; f < nf; f++)
                triangles[f] = new(_r.U32(t + f * 12), _r.U32(t + f * 12 + 4), _r.U32(t + f * 12 + 8));
            // Backface copies repeat the preceding source triple. The vertex maps address the
            // list BEFORE those copies: confirmed against every MD2 position index, not counts.
            var sourceFaces = new List<SourceTriangle>();
            foreach (var triangle in triangles)
                if (sourceFaces.Count == 0 || sourceFaces[^1] != triangle) sourceFaces.Add(triangle);
            if (mapA.Any(f => f >= sourceFaces.Count))
                throw new InvalidDataException($"MTR: {name} references a nonexistent unduplicated face");
            nodes.Add(new(b, name, id, Array.AsReadOnly(matrices), Array.AsReadOnly(mapA),
                          Array.AsReadOnly(mapB), Array.AsReadOnly(triangles), sourceFaces.AsReadOnly()));
        }
        if (cursor != NodeTable) throw new InvalidDataException("MTR: unaccounted bytes before node table");
        Nodes = nodes.AsReadOnly();

        int ArrayAt(int field, int count, int stride)
        {
            int offset = _r.Table(field, count, stride);
            if (count == 0)
            {
                if (offset != 0) throw new InvalidDataException("MTR: nonnull empty index array");
                return 0;
            }
            // All four files pack A, B, triangles in node order. Reject unsupported layouts instead
            // of returning a plausible partial walk, or letting arrays alias records/header bytes.
            if (offset != cursor || (long)offset + (long)count * stride > NodeTable)
                throw new InvalidDataException("MTR: index arrays overlap, have gaps, or cross the node table");
            cursor += count * stride;
            return offset;
        }
    }

    /// <summary>Checks named node identities, exact local matrices, and every source triangle
    /// against the MD2's independent position-index and face-normal-index streams.
    /// Also verifies each vertex's source face/corner, using the unduplicated face domain.</summary>
    public void ValidateAgainst(Model model)
    {
        if (!model.IsLegacyMd2) throw new InvalidDataException("MTR companions belong to legacy MD2, not MPS");
        var r = new CheckedBinary(model.D, "MD2/MTR binding");
        if (Nodes.Count != r.U16(0x42) || MeshCount != model.Meshes.Count || UnknownCount != r.U16(0x48))
            throw new InvalidDataException("MTR: companion header mismatch");
        for (int i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i]; int b = model.NodeOffset(i);
            int nameOffset = r.Table(b + 0x54, 1, 1);
            string name = r.Name(nameOffset, model.D.Length - nameOffset);
            if (node.Name != name || node.OneBasedIndex != r.U32(b + 0x50) + 1 ||
                !_r.Data.AsSpan(node.Offset + 0x144, 64).SequenceEqual(model.D.AsSpan(b + 0x10, 64)))
                throw new InvalidDataException($"MTR: node {i} '{node.Name}' does not match MD2 '{name}'");
            if (i >= MeshCount) continue;
            var mesh = model.Meshes[i];
            if (node.VertexSourceFaces.Count != mesh.VertexCount || node.SourceTriangles.Count != mesh.FaceCount)
                throw new InvalidDataException($"MTR: geometry size mismatch for {name}");
            foreach (var t in node.SourceTriangles)
                if (t.A >= r.U16(b + 0x58) || t.B >= r.U16(b + 0x58) || t.C >= r.U16(b + 0x58))
                    throw new InvalidDataException($"MTR: source triangle outside {name}'s position table");
            int faces = r.Int(b + 0x70), remap = r.Int(b + 0x94);
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                var source = node.UnduplicatedFaces[(int)node.VertexSourceFaces[v]];
                uint point = node.VertexSourceCorners[v] switch { 0 => source.A, 1 => source.B, _ => source.C };
                if (point != r.U16(remap + v * 2))
                    throw new InvalidDataException($"MTR: vertex {v} source identity mismatch for {name}");
            }
            for (int f = 0; f < mesh.FaceCount; f++)
            {
                int face = faces + f * 8, source = r.U16(face) - mesh.VertexCount;
                if (source < 0 || source >= node.SourceTriangles.Count)
                    throw new InvalidDataException($"MTR: invalid MD2 source face for {name}/{f}");
                var t = node.SourceTriangles[source];
                uint a = r.U16(remap + r.U16(face + 2) * 2);
                uint b1 = r.U16(remap + r.U16(face + 4) * 2);
                uint c = r.U16(remap + r.U16(face + 6) * 2);
                if (a != t.A || !((b1 == t.C && c == t.B) || (b1 == t.B && c == t.C)))
                    throw new InvalidDataException($"MTR: source triangle identity mismatch for {name}/{f}");
            }
        }
    }
}
