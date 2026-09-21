using System.Numerics;
using Godot;
using TPW.PS2.Data;
// ⚠ Godot has an `Animation` of its own. Alias ours so the clash is impossible rather than
// resolved differently in each file.
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

/// <summary>One ride, built into Godot meshes and driven by its own `.aps` data.
///
/// The animation channels are applied exactly as the game applies them:
/// * rotation POST-MULTIPLIES the bind rotation rather than replacing it -- key 0 is identity, which
///   is what keeps the rest pose intact at frame 0;
/// * scale renormalises each basis vector to the interpolated length;
/// * vertex morph replaces positions through the model's own `mesh+0x98` map;
/// * a node is not drawn before its appear frame or after its disappear frame.</summary>
public sealed class AnimatedModel
{
    sealed class Part
    {
        public Model.Mesh Mesh;
        public int NodeOffset;
        public List<System.Numerics.Vector3> BindPos;
        public List<Godot.Vector2> Uv;
        public List<Godot.Vector3> Normal;
        public List<Model.Triangle> Tris;
        public int[] AnimMap;
        public List<(int[] Times, System.Numerics.Vector3[] Keys)> Morph;
        public MeshInstance3D[] Surfaces;          // one per material
        public int[] SurfaceMaterial;
    }

    readonly Model _model;
    readonly Aps _anim;
    readonly List<Part> _parts = new();
    readonly Dictionary<int, List<(int Time, System.Numerics.Quaternion Q)>> _rot = new();
    readonly Dictionary<int, List<(int Time, System.Numerics.Vector3 S)>> _scale = new();
    Dictionary<int, (int Appear, int? Gone)> _vis = new();

    /// <summary>⚠⚠ THE GAME'S SPACE IS LEFT-HANDED AND GODOT'S IS RIGHT-HANDED. Everything under
    /// this node is mirrored in Z to convert, which is why the viewer rendered a mirror image of
    /// the Python reference until this was added. Applying it at the ROOT rather than per-vertex
    /// conjugates the whole assembled scene -- vertices, node transforms and the hierarchy together
    /// -- so nothing can be half-converted.
    ///
    /// ⚠ A mirror reverses triangle orientation, so front faces become back faces. That costs
    /// nothing here because the shader lights both sides via FRONT_FACING; it would matter if
    /// culling were ever re-enabled.</summary>
    public Node3D Root { get; } = new Node3D { Scale = new Godot.Vector3(1, 1, -1) };
    public int Frames { get; private set; }
    public string Summary { get; private set; }

    public AnimatedModel(Model model, Aps anim, Aps.Record rec,
                         Func<string, ImageTexture> texture)
    {
        _model = model; _anim = anim;
        if (rec != null)
        {
            for (int i = 0; i < rec.TrackCount; i++)
            {
                int t = anim.TrackAt(rec, i), node = anim.TrackNode(t);
                var r = anim.Rotation(t); if (r != null) _rot[node] = r;
                var s = anim.Scale(t); if (s != null) _scale[node] = s;
            }
            _vis = anim.Visibility(rec);
            Frames = Math.Max(anim.Length(rec), 1);
        }

        foreach (var mesh in _model.Meshes)
        {
            var tris = _model.Triangles(mesh);
            if (tris.Count == 0) continue;
            var (pos, uv, nor) = _model.Vertices(mesh);
            var p = new Part
            {
                Mesh = mesh,
                NodeOffset = mesh.Offset,
                BindPos = pos,
                Uv = uv.Select(v => new Godot.Vector2(v.X, v.Y)).ToList(),
                Normal = nor.Select(v => new Godot.Vector3(v.X, v.Y, v.Z)).ToList(),
                Tris = tris,
                AnimMap = _model.AnimVertexMap(mesh),
            };
            if (rec != null)
                for (int i = 0; i < rec.TrackCount; i++)
                {
                    int t = anim.TrackAt(rec, i);
                    if (anim.TrackNode(t) == mesh.Index) { p.Morph = anim.Morph(t); break; }
                }
            BuildSurfaces(p, texture);
            _parts.Add(p);
            var vs = _vis.TryGetValue(mesh.Index, out var vv) ? $"appear {vv.Appear}" + (vv.Gone is int g2 ? $" gone {g2}" : "") : "always";
            GD.Print($"[part] {mesh.Name,-10} tris={tris.Count,-5} verts={pos.Count,-5} " +
                     $"morph={(p.Morph != null ? p.Morph.Count.ToString() : "-"),-5} " +
                     $"map={(p.AnimMap != null ? "yes" : "NO "),-4} {vs}");
        }
        Summary = $"{_parts.Count} parts, {Frames} frames, {_model.Materials.Count} materials";
    }

    // ⚠ Culling is now owned by the shader's `render_mode cull_disabled`, not by a material
    // property, so there is no CullFromEnv any more -- it would have been dead code that looked
    // live. The measured fact it existed to record stays in findings/formats.md: M3D2 front faces
    // are CLOCKWISE, which is why the triangles below are emitted reversed.

    /// ⚠ TRIED AND REJECTED, kept switchable so nobody re-tries it blind: duplicating every
    /// triangle puts two coincident faces at the SAME depth, which z-fights per pixel and looks
    /// worse than either plain mode. `TPW_PS2_CULL=two` still selects it; it is not the default.
    static bool TwoSided =>
        (OS.GetEnvironment("TPW_PS2_CULL") ?? "back").ToLowerInvariant() is "two" or "twosided";

    /// <summary>The viewer's material, as a shader rather than a StandardMaterial3D.
    ///
    /// ⚠⚠ THE REASON IS TWO-SIDED LIGHTING. With culling disabled, Godot's StandardMaterial3D does
    /// NOT flip the normal on back faces, so every surface you are seeing from behind is lit by a
    /// normal pointing away from the light and comes out dark. The owner described it exactly:
    /// "all the faces are there, its like they are textured on the wrong side half of the time."
    /// `FRONT_FACING` is the only way to fix that, and it needs a shader.
    ///
    /// It also carries the two settings this data actually needs: repeat, because `m_boxes` UVs run
    /// u 0..4, and a cutout threshold of 16/255 rather than the engine default of 0.5, which would
    /// discard the ~50% of texels at or below alpha 128.</summary>
    static Shader _shader;
    static Shader ViewerShader => _shader ??= new Shader
    {
        Code = @"
shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;

uniform sampler2D albedo_tex : source_color, filter_nearest_mipmap, repeat_enable;
uniform float cutout = 0.0627;      // 16/255
uniform bool has_tex = true;

void fragment() {
    if (has_tex) {
        vec4 c = texture(albedo_tex, UV);
        if (c.a < cutout) discard;   // alpha CUTOUT, not blending
        ALBEDO = c.rgb;
    } else {
        ALBEDO = vec3(0.72);
    }
    // ⭐ The whole point: light a back face by the normal it actually presents.
    if (!FRONT_FACING) { NORMAL = -NORMAL; }
}
"
    };

    void BuildSurfaces(Part p, Func<string, ImageTexture> texture)
    {
        var byMat = p.Tris.GroupBy(t => t.Material).ToList();
        p.Surfaces = new MeshInstance3D[byMat.Count];
        p.SurfaceMaterial = new int[byMat.Count];
        for (int i = 0; i < byMat.Count; i++)
        {
            var mi = new MeshInstance3D();
            var mat = new ShaderMaterial { Shader = ViewerShader };
            int m = byMat[i].Key;
            var tex = (m >= 0 && m < _model.Materials.Count) ? texture(_model.Materials[m]) : null;
            mat.SetShaderParameter("albedo_tex", tex);
            mat.SetShaderParameter("has_tex", tex != null);
            mi.MaterialOverride = mat;
            p.SurfaceMaterial[i] = m;
            p.Surfaces[i] = mi;
            Root.AddChild(mi);
        }
        RebuildGeometry(p, 0);
    }

    void RebuildGeometry(Part p, float now)
    {
        var pos = p.BindPos;
        if (p.Morph != null && p.AnimMap != null)
        {
            var ev = p.Morph.Select(v => Sample(v.Times, v.Keys, now)).ToArray();
            pos = p.AnimMap.Select(i => ev[i]).ToList();
        }
        int si = 0;
        foreach (var grp in p.Tris.GroupBy(t => t.Material))
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            bool two = TwoSided;
            foreach (var t in grp)
            {
                // ⭐ REVERSED: A, C, B -- M3D2 front faces are clockwise. See the CullMode note.
                foreach (var idx in new[] { t.A, t.C, t.B })
                {
                    st.SetUV(p.Uv[idx]);
                    // ⭐ The model's OWN normal, not one derived from triangle order.
                    st.SetNormal(p.Normal[idx]);
                    var v = pos[idx];
                    st.AddVertex(new Godot.Vector3(v.X, v.Y, v.Z));
                }
                if (!two) continue;
                foreach (var idx in new[] { t.A, t.B, t.C })      // the back copy
                {
                    st.SetUV(p.Uv[idx]);
                    st.SetNormal(-p.Normal[idx]);                 // ⚠ flipped, or it lights inside-out
                    var v = pos[idx];
                    st.AddVertex(new Godot.Vector3(v.X, v.Y, v.Z));
                }
            }
            p.Surfaces[si++].Mesh = st.Commit();
        }
    }

    static System.Numerics.Vector3 Sample(int[] times, System.Numerics.Vector3[] keys, float now)
    {
        if (keys.Length == 1) return keys[0];
        int i = 0;
        while (i < times.Length - 2 && times[i + 1] < now) i++;
        float t0 = times[i], t1 = times[i + 1];
        float f = t1 == t0 ? 0f : Math.Clamp((now - t0) / (t1 - t0), 0f, 1f);
        return System.Numerics.Vector3.Lerp(keys[i], keys[i + 1], f);
    }

    static System.Numerics.Quaternion SampleRot(
        List<(int Time, System.Numerics.Quaternion Q)> keys, float now)
    {
        if (keys.Count == 1) return keys[0].Q;
        int i = 0;
        while (i < keys.Count - 2 && keys[i + 1].Time < now) i++;
        float t0 = keys[i].Time, t1 = keys[i + 1].Time;
        float f = t1 == t0 ? 0f : Math.Clamp((now - t0) / (t1 - t0), 0f, 1f);
        return System.Numerics.Quaternion.Normalize(
            System.Numerics.Quaternion.Slerp(keys[i].Q, keys[i + 1].Q, f));
    }

    public void SetFrame(float now)
    {
        var world = WorldAt(now);
        foreach (var p in _parts)
        {
            bool shown = true;
            if (_vis.TryGetValue(p.Mesh.Index, out var v))
                shown = now >= v.Appear && (v.Gone == null || now < v.Gone);
            foreach (var s in p.Surfaces) s.Visible = shown;
            if (!shown) continue;
            if (p.Morph != null && p.AnimMap != null) RebuildGeometry(p, now);
            var w = world[p.NodeOffset];
            var t = new Transform3D(
                new Godot.Basis(new Godot.Vector3(w.M11, w.M12, w.M13),
                                new Godot.Vector3(w.M21, w.M22, w.M23),
                                new Godot.Vector3(w.M31, w.M32, w.M33)),
                new Godot.Vector3(w.M41, w.M42, w.M43));
            foreach (var s in p.Surfaces) s.Transform = t;
        }
    }

    /// <summary>World transforms with the animated rotation POST-MULTIPLIED onto the bind rotation
    /// and the animated scale applied to the basis lengths. Translation is kept from the bind pose,
    /// and the hierarchy is recomposed from the overridden locals so an animated parent carries its
    /// children with it.</summary>
    Dictionary<int, Matrix4x4> WorldAt(float now)
    {
        if (_rot.Count == 0 && _scale.Count == 0) return _model.WorldTransforms();

        var locals = _model.LocalTransforms();
        foreach (var node in _rot.Keys.Concat(_scale.Keys).Distinct())
        {
            int off = _model.NodeOffset(node);
            if (!locals.TryGetValue(off, out var bind)) continue;
            var L = bind;
            if (_rot.TryGetValue(node, out var rk))
                L = Matrix4x4.CreateFromQuaternion(SampleRot(rk, now)) * L;   // bind first
            if (_scale.TryGetValue(node, out var sk))
                L = Renormalise(L, Sample(sk.Select(x => x.Time).ToArray(),
                                          sk.Select(x => x.S).ToArray(), now));
            L.M41 = bind.M41; L.M42 = bind.M42; L.M43 = bind.M43;   // translation stays put
            locals[off] = L;
        }
        return _model.WorldTransforms(locals);
    }

    static Matrix4x4 Renormalise(Matrix4x4 m, System.Numerics.Vector3 s)
    {
        void Fix(ref float a, ref float b, ref float c, float want)
        {
            float len = MathF.Sqrt(a * a + b * b + c * c);
            if (len <= 0) return;
            float f = want / len; a *= f; b *= f; c *= f;
        }
        Fix(ref m.M11, ref m.M12, ref m.M13, s.X);
        Fix(ref m.M21, ref m.M22, ref m.M23, s.Y);
        Fix(ref m.M31, ref m.M32, ref m.M33, s.Z);
        return m;
    }
}
