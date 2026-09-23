using System.Numerics;

namespace TPW.PS2.Data;

/// <summary>M3D2 skinning: the descriptor at <c>mesh+0x90</c> and the arithmetic the game's own
/// sampler applies to it. This is the path every character in DATA.WAD's <c>/Chars</c> takes
/// and that nothing in a world archive uses (findings/animation.md, "the 20-byte track").
///
/// The consumer is the skeletal half of <c>FUN_001a8da8</c>: after building one 4x4 per animated
/// node it walks every mesh, and for each mesh with a non-zero <c>+0x90</c> it reads
///
/// <code>
/// skin+0x00 u16 A   animated vertices          skin+0x08 u32 -> B x u8   bone
/// skin+0x02 u16 B   influences in total        skin+0x0C u32 -> B x f32  weight
/// skin+0x04 u32 -> A x { u16 first, u8 count, u8 ? }   skin+0x10 u32 -> B x f32[3] position
/// </code>
///
/// and writes, for each animated vertex, <c>sum over its influences of weight * (position x M[bone])</c>
/// into the mesh's GS packet through the same <c>mesh+0x98</c> run list the morph path uses --
/// the position is a full affine transform through the bone's 4x4 (words 0..10 and 12..14),
/// and the WHOLE result is scaled by the weight, translation included. <see cref="Skin.Deform"/>
/// is that loop.
///
/// ⚠ The influence count is a BYTE at <c>group+2</c>. Read as a u16 it still tiles on most meshes
/// and produces counts of 65,282 on the rest -- the trap <c>mesh+0x66</c> already documents.
///
/// ⭐⭐ THE BONE BYTE IS A HELPER INDEX, NOT A "MESHES THEN HELPERS" NODE INDEX. The sampler never
/// resolves it against the model at all: a track's node u16 picks a slot in a 35-entry stack
/// array of matrices (<c>afStack_9c0 + node*16</c>) and the skin's bone byte picks from the same
/// array, so the two share one index space and the model's tables are not consulted. Which space
/// it is comes from the data, three ways: (1) under this reading the tracks of every kid cover
/// exactly every helper except <c>Bip01 Footsteps</c> and the <c>Dummy</c> nodes -- the nodes an
/// animator would not key; (2) the head mesh's two bones become Neck and Head, where the other
/// reading makes them Pelvis and Spine; (3) the bind-pose control in tools/TPW.PS2.SkinAudit
/// closes to a hundredth of a unit on every character under it and misses by thousands under
/// the other. tools/skin.py's docstring says "meshes then helpers"; it was never checked, and
/// its own bind_positions() does not reproduce a single vertex.</summary>
public sealed partial class Model
{
    public sealed class Skin
    {
        public int Offset;
        /// <summary>A: the animated vertices, which the mesh+0x98 list maps strip slots onto.</summary>
        public int VertexCount;
        /// <summary>B: influences over all vertices.</summary>
        public int InfluenceCount;
        /// <summary>Per animated vertex: its first influence and how many it has (a BYTE).</summary>
        public ushort[] First;
        public byte[] Count;
        /// <summary>Per influence: the helper index of the bone, its weight, and the vertex's
        /// position in THAT BONE's space -- in the same units as the mesh's own vertices.</summary>
        public byte[] Bone;
        public float[] Weight;
        public Vector3[] Position;

        /// <summary>Every bone this skin references, ascending.</summary>
        public IEnumerable<int> Bones => Bone.Select(b => (int)b).Distinct().OrderBy(b => b);

        /// <summary>The game's per-vertex loop, verbatim: each influence's bone-space position
        /// through its bone's 4x4 (row-vector: x*row0 + y*row1 + z*row2 + row3), the whole
        /// affine result scaled by the weight, summed. <paramref name="bones"/> is indexed by
        /// helper index, as the sampler's matrix array is.</summary>
        public Vector3 Deform(int vertex, ReadOnlySpan<Matrix4x4> bones)
        {
            int first = First[vertex], n = Count[vertex];
            var sum = Vector3.Zero;
            for (int j = first; j < first + n; j++)
            {
                ref readonly var b = ref bones[Bone[j]];
                var p = Position[j];
                sum += Weight[j] * new Vector3(
                    p.X * b.M11 + p.Y * b.M21 + p.Z * b.M31 + b.M41,
                    p.X * b.M12 + p.Y * b.M22 + p.Z * b.M32 + b.M42,
                    p.X * b.M13 + p.Y * b.M23 + p.Z * b.M33 + b.M43);
            }
            return sum;
        }
    }

    int _helperCount = -1;
    /// <summary>How many helper entries the model carries -- the size of the skeletal index space.</summary>
    public int HelperCount => _helperCount >= 0 ? _helperCount : _helperCount = HelperOffsets().Count();

    /// <summary>The node a skin bone / skeletal track index names: helpers only, after the meshes.</summary>
    public int SkinBoneNode(int bone) => Meshes.Count + bone;

    /// <summary>The skin at <c>mesh+0x90</c>, or null for a mesh that has none. MPS only. Every
    /// table is bounds-checked against the file and every bone against <see cref="HelperCount"/>,
    /// so a wrong read fails here with the mesh's name rather than as a character drawn inside out.</summary>
    public Skin ReadSkin(Mesh m)
    {
        if (IsLegacyMd2) return null;
        int sk = (int)U32(m.Offset + 0x90);
        if (sk == 0) return null;
        if (sk < 0 || sk + 0x14 > D.Length) throw new InvalidDataException($"{m.Name}: skin descriptor outside the file");
        int a = U16(sk), b = U16(sk + 2);
        int groups = (int)U32(sk + 4), bones = (int)U32(sk + 8), weights = (int)U32(sk + 0x0C), positions = (int)U32(sk + 0x10);
        if (a == 0 || b == 0) throw new InvalidDataException($"{m.Name}: skin with {a} vertices and {b} influences");
        if (groups <= 0 || (long)groups + a * 4L > D.Length || bones <= 0 || (long)bones + b > D.Length ||
            weights <= 0 || (long)weights + b * 4L > D.Length || positions <= 0 || (long)positions + b * 12L > D.Length)
            throw new InvalidDataException($"{m.Name}: skin tables outside the file");
        var skin = new Skin
        {
            Offset = sk, VertexCount = a, InfluenceCount = b,
            First = new ushort[a], Count = new byte[a],
            Bone = new byte[b], Weight = new float[b], Position = new Vector3[b],
        };
        for (int i = 0; i < a; i++)
        {
            skin.First[i] = U16(groups + i * 4);
            skin.Count[i] = D[groups + i * 4 + 2];
            if (skin.Count[i] == 0 || skin.First[i] + skin.Count[i] > b)
                throw new InvalidDataException($"{m.Name}: skin vertex {i} runs off the influence table");
        }
        for (int j = 0; j < b; j++)
        {
            skin.Bone[j] = D[bones + j];
            if (skin.Bone[j] >= HelperCount)
                throw new InvalidDataException($"{m.Name}: skin bone {skin.Bone[j]} but only {HelperCount} helpers");
            skin.Weight[j] = F32(weights + j * 4);
            skin.Position[j] = new Vector3(F32(positions + j * 12), F32(positions + j * 12 + 4), F32(positions + j * 12 + 8));
        }
        return skin;
    }

    /// <summary>⭐⭐ THE BONE MATRIX EXACTLY AS THE GAME BUILDS IT, word for word. <c>FUN_001a8da8</c>
    /// evaluates the track's quaternion <c>(x, y, z, w)</c> -- the order is fixed by the formula's
    /// own structure: the three diagonal terms are <c>1-2(y²+z²)</c>, <c>1-2(x²+z²)</c>,
    /// <c>1-2(x²+y²)</c> and the fourth component only ever appears in cross products, which is
    /// where w goes and nowhere x can -- and hands twelve floats to <c>FUN_0016f220</c>, which
    /// stores them in order: eight in registers into words 0-2, 4-6, 8-9; the ninth from the stack
    /// into word 10; the position from the stack into words 12, 13, 14; zeros at 3, 7, 11 and
    /// 1.0 at 15. The call site (0x1a90b8-0x1a90d0) stores the position as
    /// <c>key.x -> word 12, key.z -> word 13, key.y -> word 14</c>.
    ///
    /// ⭐ THAT SWAP IS THE GAME'S. The keys and the skin's bone-space positions are in the
    /// exporter's Z-up frame; the vertices the mesh draws are Y-up, and this matrix -- rotation
    /// columns and translation alike -- converts on the way through. Read against the data it
    /// lands: the pelvis of every kid's walk cycle at frame 0 sits on its bind-pose position to
    /// within a unit in x and z and one gait-bob (about 1,000 of 15,000 units) in y.
    ///
    /// The result is used row-vector style, <c>v = p * M</c>, by <see cref="Skin.Deform"/>.</summary>
    public static Matrix4x4 BoneMatrix(Quaternion q, Vector3 key)
    {
        float x = q.X, y = q.Y, z = q.Z, w = q.W;
        float xx = x + x, yy = y + y, zz = z + z;
        return new Matrix4x4(
            1f - yy * y - zz * z, xx * z + yy * w,      xx * y - zz * w,      0f,
            xx * y + zz * w,      yy * z - xx * w,      1f - xx * x - zz * z, 0f,
            xx * z - yy * w,      1f - xx * x - yy * y, yy * z + xx * w,      0f,
            key.X,                key.Z,                key.Y,                1f);
    }

    /// <summary>The bind-pose ROTATION of a skin bone in the mesh's vertex space, from the model's
    /// own node matrices -- what a skin audit skins the bind pose with.
    ///
    /// ⚠ MEASURED, NOT READ. The game never poses a character in its bind pose and stores no
    /// bind matrices; the skin's bone-space positions were made against matrices the exporter
    /// kept to itself. What IS on the disc is the helper hierarchy, and over all 25 characters
    /// the bone's bind rotation is, to better than 1e-3 on every element of every well-determined
    /// bone (tools/TPW.PS2.SkinAudit):
    ///
    /// <code>R = D1 * chain(bone) * D2</code>
    ///
    /// where <c>chain</c> composes the helper's parent-relative matrices (basis rows normalised to
    /// length 1) up to but EXCLUDING <c>Bip01</c>, <c>D1</c> swaps the bone-space y and z axes
    /// (the same Z-up-to-Y-up swap <see cref="BoneMatrix"/> shows the game applying to the keys)
    /// and <c>D2</c> is a quarter turn about Y. Stopping at Bip01 is what makes it one rule: the
    /// kids' Bip01 is their root, while every adult rig hangs Bip01 under a "picked up pivot"
    /// helper, and the alternatives that also fit some rigs (stopping at the pivot, or not at all)
    /// only did so where that rig's Bip01 happened to be a signed axis permutation.
    ///
    /// ⚠ THE TRANSLATIONS DO NOT FOLLOW. The hierarchy's translations are in a different unit
    /// from the vertices and scale differently from bone to bone (Spine 9,345 : Spine1 4,566 :
    /// Neck 2,907 : Head 33,827 vertex units per hierarchy unit on Boy1a), so no scalar or affine
    /// map of them lands on the skinned bind, and props such as a character's placard carry their
    /// own mesh-local offset besides. The audit solves each (mesh, bone) translation by least
    /// squares and says so; this method deliberately does not return one.</summary>
    public Matrix4x4 SkinBindRotation(int bone)
    {
        int node = SkinBoneNode(bone);
        int bip = -1;
        for (int h = 0; h < HelperCount; h++)
            if (NameAt((int)U32(NodeOffset(Meshes.Count + h) + 0x54)) == "Bip01") { bip = Meshes.Count + h; break; }
        var local = LocalTransforms();
        Matrix4x4 Chain(int n, int depth)
        {
            int off = NodeOffset(n);
            int parent = NodeIndex((int)U32(off + 4));
            var l = Normalised(local[off]);
            if (parent < 0 || parent == bip || depth > 64) return l;
            return l * Chain(parent, depth + 1);
        }
        var d1 = new Matrix4x4(1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1);
        var d2 = new Matrix4x4(0, 0, -1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1);
        var r = d1 * Chain(node, 0) * d2;
        r.M41 = 0; r.M42 = 0; r.M43 = 0;
        return r;
    }

    /// <summary>A node matrix with each basis row scaled to unit length and its translation
    /// dropped -- the rotation alone, however the row was scaled.</summary>
    static Matrix4x4 Normalised(Matrix4x4 m)
    {
        static void Row(ref float a, ref float b, ref float c)
        {
            float len = MathF.Sqrt(a * a + b * b + c * c);
            if (len > 0) { a /= len; b /= len; c /= len; }
        }
        Row(ref m.M11, ref m.M12, ref m.M13);
        Row(ref m.M21, ref m.M22, ref m.M23);
        Row(ref m.M31, ref m.M32, ref m.M33);
        m.M41 = 0; m.M42 = 0; m.M43 = 0; m.M14 = 0; m.M24 = 0; m.M34 = 0; m.M44 = 1;
        return m;
    }
}
