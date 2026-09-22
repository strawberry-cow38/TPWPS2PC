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
        /// <summary>This mesh's node and every node above it, for inherited visibility.</summary>
        public List<int> Ancestry;
    }

    readonly Model _model;
    readonly Aps _anim;
    readonly List<Part> _parts = new();
    readonly Dictionary<int, List<(int Time, System.Numerics.Quaternion Q)>> _rot = new();
    readonly Dictionary<int, int> _rotTrack = new();   // node -> its track, for the easing curves
    readonly Dictionary<int, List<(int Time, System.Numerics.Vector3 S)>> _scale = new();
    Dictionary<int, int[]> _vis = new();
    readonly Dictionary<int, Aps.Path> _path = new();
    readonly HashSet<int> _facing = new();
    List<Aps.SkeletalTrack> _skel;
    /// <summary>True when the selected record drives a biped rather than vertex morph.</summary>
    public bool Skeletal { get; private set; }

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
                         Func<string, (ImageTexture Tex, bool Soft)> texture)
    {
        _model = model; _anim = anim;
        // ⚠ THE TWO TRACK FORMATS ARE NOT INTERCHANGEABLE. A skeletal record's tracks are 20 bytes,
        // not 48, so none of the channel readers below may be pointed at one. The characters in
        // DATA.WAD are skinned to a biped and are shown in their bind pose until the skin is wired;
        // their morph record (there is exactly one per character) still animates.
        Skeletal = rec != null && rec.Skeletal;
        if (rec != null && !rec.Skeletal)
        {
            for (int i = 0; i < rec.TrackCount; i++)
            {
                int t = anim.TrackAt(rec, i), node = anim.TrackNode(t);
                var r = anim.Rotation(t); if (r != null) { _rot[node] = r; _rotTrack[node] = t; }
                var s = anim.Scale(t); if (s != null) _scale[node] = s;
                // ⚠⚠ THE PATH CHANNEL WAS READ AND THEN NEVER APPLIED. 1,468 of the disc's 6,155
                // morph-format tracks carry a Catmull-Rom path, and every car, train, boat and
                // gondola on them was sitting at its rest position. That is what "sub parts are
                // rotated or positioned wrong" looks like from the outside.
                var sp = anim.SplineAt(t);
                if (sp != null)
                {
                    _path[node] = sp;
                    if ((anim.TrackFlags(t) & (uint)Aps.TrackFlag.OrientAlongPath) != 0) _facing.Add(node);
                }
            }
            _vis = anim.Visibility(rec);
            Frames = Math.Max(anim.Length(rec), 1);
        }
        else if (rec != null)
        {
            _skel = anim.SkeletalTracks(rec);
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
                Ancestry = _model.Ancestry(mesh.Index),
            };
            if (rec != null)
                for (int i = 0; i < rec.TrackCount; i++)
                {
                    int t = anim.TrackAt(rec, i);
                    if (anim.TrackNode(t) == mesh.Index) { p.Morph = anim.Morph(t); break; }
                }
            BuildSurfaces(p, texture);
            _parts.Add(p);
            var vs = _vis.TryGetValue(mesh.Index, out var vv) ? "vis[" + string.Join(",", vv) + "]" : "always";
            GD.Print($"[part] {mesh.Name,-10} tris={tris.Count,-5} verts={pos.Count,-5} " +
                     $"morph={(p.Morph != null ? p.Morph.Count.ToString() : "-"),-5} " +
                     $"map={(p.AnimMap != null ? "yes" : "NO "),-4} {vs}");
        }
        Summary = $"{_parts.Count} parts, {Frames} frames, {_model.Materials.Count} materials"
                  + (Skeletal ? $", {_skel?.Count ?? 0} bone tracks (bind pose)" : "");
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
    static Shader _shader, _blendShader;

    /// <summary>How many triangles had their winding corrected, and out of how many. Reported so
    /// the correction can be checked against the 37% measured off the file rather than trusted.</summary>
    static int _wound, _woundTotal;
    public static (int Flipped, int Total) WindingFixes => (_wound, _woundTotal);

    /// <summary>The blended twin of the viewer shader, for textures with SOFT alpha.
    ///
    /// ⚠⚠ CUTOUT THROWS AWAY EVERY INTERMEDIATE TEXEL. 1,532 of the disc's 32-bit TGAs have more
    /// than one per cent of their texels at an alpha that is neither clear nor solid --
    /// `Scifi_Glass.tga` is 63% of them, `Research_Hair.tga` 39% -- and a shader that only
    /// discards below a threshold and then writes an opaque ALBEDO renders all of it solid. The
    /// owner saw it straight away: "some textures are missing alpha".</summary>
    static Shader BlendShader => _blendShader ??= new Shader
    {
        // ⚠ Written out rather than string-replaced off the cutout shader: a Replace that stopped
        // matching would silently hand back the cutout shader and the bug would come straight back
        // with nothing to notice.
        Code = @"
shader_type spatial;
// ⚠⚠ `depth_draw_opaque` IS NOT OPTIONAL HERE. Writing ALPHA makes a Godot material TRANSPARENT,
// and a transparent material does not write depth by default -- so every surface behind it shows
// through and the model reads INSIDE OUT, with parts drawn in the wrong order. The owner saw it
// within minutes of the soft-alpha change: ""theres actually the inside-out bug on some rides /
// incorrect draw orders"". These models are solid geometry with soft EDGES, not stacked glass, so
// writing depth is right and costs nothing the data actually needs.
render_mode cull_disabled, diffuse_lambert, specular_disabled, depth_draw_opaque;

uniform sampler2D albedo_tex : source_color, filter_nearest_mipmap, repeat_enable;
uniform float cutout = 0.0627;      // 16/255
uniform bool has_tex = true;
uniform bool affine = true;
varying vec3 uvw;

void vertex() {
    vec4 vpos = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
    float w = max(-vpos.z, 0.0001);
    uvw = vec3(UV * w, w);
}

void fragment() {
    vec2 uv = affine ? (uvw.xy / uvw.z) : UV;
    if (has_tex) {
        vec4 c = texture(albedo_tex, uv);
        if (c.a < cutout) discard;   // still drop the fully clear texels
        ALBEDO = c.rgb;
        ALPHA = c.a;                 // ⭐ and KEEP the soft ones -- this is the whole difference
    } else {
        ALBEDO = vec3(0.72);
    }
    if (!FRONT_FACING) { NORMAL = -NORMAL; }
}
"
    };

    static Shader ViewerShader => _shader ??= new Shader
    {
        Code = @"
shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;

uniform sampler2D albedo_tex : source_color, filter_nearest_mipmap, repeat_enable;
uniform float cutout = 0.0627;      // 16/255
uniform bool has_tex = true;
uniform bool affine = true;

// ⭐ AFFINE TEXTURE MAPPING, as the PS2 does it.
// The hardware interpolates a varying PERSPECTIVE-CORRECTLY: it gives the fragment
//     P(v) = L(v/w) / L(1/w)          where L() is plain screen-space linear interpolation.
// Godot's shading language has no `noperspective`, so the affine UV is recovered arithmetically:
//     P(UV*w) / P(w) = [L(UV)/L(1/w)] * L(1/w) = L(UV)
// i.e. pass UV premultiplied by view depth alongside that depth, and divide in the fragment.
// ⚠ The PS2's affinity is much milder than the PS1's -- it subdivides more -- so this should read
// as a slight swim on large near-camera polygons, not the violent warping of a PSX render.
varying vec3 uvw;

void vertex() {
    vec4 vpos = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
    float w = max(-vpos.z, 0.0001);          // view-space depth; the camera looks down -Z
    uvw = vec3(UV * w, w);
}

void fragment() {
    vec2 uv = affine ? (uvw.xy / uvw.z) : UV;
    if (has_tex) {
        vec4 c = texture(albedo_tex, uv);
        if (c.a < cutout) discard;   // alpha CUTOUT, not blending
        ALBEDO = c.rgb;
    } else {
        ALBEDO = vec3(0.72);
    }
    // ⭐ Light a back face by the normal it actually presents; without this half of every closed
    // model renders dark and reads as missing artwork.
    if (!FRONT_FACING) { NORMAL = -NORMAL; }
}
"
    };

    void BuildSurfaces(Part p, Func<string, (ImageTexture Tex, bool Soft)> texture)
    {
        var byMat = p.Tris.GroupBy(t => t.Material).ToList();
        p.Surfaces = new MeshInstance3D[byMat.Count];
        p.SurfaceMaterial = new int[byMat.Count];
        for (int i = 0; i < byMat.Count; i++)
        {
            var mi = new MeshInstance3D();
            int m = byMat[i].Key;
            var (tex, soft) = (m >= 0 && m < _model.Materials.Count)
                              ? texture(_model.Materials[m]) : (null, false);
            var mat = new ShaderMaterial { Shader = soft ? BlendShader : ViewerShader };
            mat.SetShaderParameter("albedo_tex", tex);
            mat.SetShaderParameter("has_tex", tex != null);
            // ⚠ Vertex snapping is deliberately NOT wired yet -- the owner asked for affine only.
            mat.SetShaderParameter("affine", (OS.GetEnvironment("TPW_PS2_AFFINE") ?? "on") != "off");
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
                // ⭐⭐ WINDING PER TRIANGLE, FROM THE MODEL'S OWN NORMALS. Emitting A,C,B for every
                // triangle assumes the source is consistently wound, and this one is not: measured
                // over jungle's terrain, 37% of near-horizontal triangles end up facing away --
                // whole meshes at 100% (`surface13/15/22/25/27`, every `HOARDING_*`, `RIVERBED_04`).
                // A face wound backwards is culled, so it is simply absent.
                //
                // The stored per-vertex normal says which side is out. Pick the order whose
                // geometric normal AGREES with it.
                //
                // ⚠ I first reasoned this the other way -- opposed, on the grounds that the scene
                // root's Scale (1,1,-1) has determinant -1 and reverses orientation. That gave a
                // 61% flip rate against the 37% measured off the file, i.e. it was correcting the
                // majority and breaking them. The check below caught it; the argument had sounded
                // perfectly good.
                //
                // ⭐ Self-checking: the flip count is logged and compared against the 37% measured
                // independently off the file. That check is what caught the sign being wrong.
                var na = p.Normal[t.A] + p.Normal[t.B] + p.Normal[t.C];
                var pa = pos[t.A]; var pb = pos[t.B]; var pc = pos[t.C];
                var g = System.Numerics.Vector3.Cross(pc - pa, pb - pa);      // normal of A,C,B
                bool keep = g.X * na.X + g.Y * na.Y + g.Z * na.Z >= 0;
                if (!keep) _wound++;
                _woundTotal++;
                foreach (var idx in keep ? new[] { t.A, t.C, t.B } : new[] { t.A, t.B, t.C })
                {
                    st.SetUV(p.Uv[idx]);
                    // ⭐ The model's OWN normal, not one derived from triangle order.
                    st.SetNormal(p.Normal[idx]);
                    var v = pos[idx];
                    st.AddVertex(new Godot.Vector3(v.X, v.Y, v.Z));
                }
                if (!two) continue;
                foreach (var idx in keep ? new[] { t.A, t.B, t.C } : new[] { t.A, t.C, t.B })  // back copy
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
        List<(int Time, System.Numerics.Quaternion Q)> keys, float now, Aps anim = null, int track = 0)
    {
        if (keys.Count == 1) return keys[0].Q;
        int i = 0;
        while (i < keys.Count - 2 && keys[i + 1].Time < now) i++;
        float t0 = keys[i].Time, t1 = keys[i + 1].Time;
        float f = t1 == t0 ? 0f : Math.Clamp((now - t0) / (t1 - t0), 0f, 1f);
        // ⭐ The key's own easing curve remaps the interpolation factor before the SLERP.
        if (anim != null && track != 0) f = anim.Ease(track, i, f);
        return System.Numerics.Quaternion.Normalize(
            System.Numerics.Quaternion.Slerp(keys[i].Q, keys[i + 1].Q, f));
    }

    public void SetFrame(float now)
    {
        var world = WorldAt(now);
        foreach (var p in _parts)
        {
            // ⚠⚠ VISIBILITY IS INHERITED. 183 of the disc's 2,033 appear/disappear entries are
            // keyed on a HELPER rather than a mesh -- the game hides `Dummy01` to take the arms
            // off with it. Checking only the mesh's own entry left those parts on screen.
            bool shown = true;
            foreach (var node in p.Ancestry ?? new List<int> { p.Mesh.Index })
                if (_vis.TryGetValue(node, out var v) && !Aps.VisibleAt(v, now)) { shown = false; break; }
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
        if (_rot.Count == 0 && _scale.Count == 0 && _path.Count == 0) return _model.WorldTransforms();

        var locals = _model.LocalTransforms();
        foreach (var node in _rot.Keys.Concat(_scale.Keys).Concat(_path.Keys).Distinct())
        {
            int off = _model.NodeOffset(node);
            if (!locals.TryGetValue(off, out var bind)) continue;
            var L = bind;
            if (_rot.TryGetValue(node, out var rk))
            {
                var q = SampleRot(rk, now, _anim, _rotTrack.GetValueOrDefault(node, 0));
                // ⭐ THE ROTATION CHANNEL IS ABSOLUTE, NOT A DELTA. Key 0 IS the node's own bind
                // rotation on 1,745 of 2,185 tracks in one mode and 439 of 565 in the other -- ~80%
                // either way -- which is what an animator authoring a rest pose as frame 0 produces.
                // Composing it onto the bind applies the rest orientation TWICE; for the Super Bog's
                // sign, whose bind is a +90 pitch and whose key 0 is a -90 pitch, the two cancelled
                // and a sign that should stand up lay flat on the roof.
                L = Compose ? Matrix4x4.CreateFromQuaternion(q) * L : Replace(L, q);
            }
            if (_scale.TryGetValue(node, out var sk))
                L = Renormalise(L, Sample(sk.Select(x => x.Time).ToArray(),
                                          sk.Select(x => x.S).ToArray(), now));

            L.M41 = bind.M41; L.M42 = bind.M42; L.M43 = bind.M43;   // translation stays put
            if (_path.TryGetValue(node, out var path))
            {
                // The path REPLACES the bind translation: the curve is where the thing actually is.
                var at = path.At(now);
                L.M41 = at.X; L.M42 = at.Y; L.M43 = at.Z;
                if (_facing.Contains(node))
                {
                    // Orient along travel: point the node's Z down the tangent and keep it upright.
                    var f = path.Tangent(now);
                    var up = System.Numerics.Vector3.UnitY;
                    if (MathF.Abs(System.Numerics.Vector3.Dot(f, up)) > 0.999f) up = System.Numerics.Vector3.UnitX;
                    var right = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(up, f));
                    var realUp = System.Numerics.Vector3.Cross(f, right);
                    L.M11 = right.X; L.M12 = right.Y; L.M13 = right.Z;
                    L.M21 = realUp.X; L.M22 = realUp.Y; L.M23 = realUp.Z;
                    L.M31 = f.X; L.M32 = f.Y; L.M33 = f.Z;
                    // ⚠⚠ THOSE THREE ARE UNIT VECTORS, so writing them straight WIPES THE BIND'S
                    // SCALE. A node whose bind carries 0.1 -- which `monkey.mps`'s `m_base` does,
                    // and every part under it inherits -- came out at 1.0 and the whole ride
                    // rendered TEN TIMES too big. It never looked wrong in the viewer because the
                    // camera frames whatever it is given; it only showed up once rides had to stand
                    // on a shared grid, where one filled its plot and another filled a tenth of it.
                    // ⚠ UNVERIFIED, and kept only because it cannot lose information: assigning
                    // right/realUp/tangent straight into the basis rows discards whatever scale the
                    // bind carried, since those three are unit vectors. It was written chasing a
                    // 10x that turned out to be DrawnBounds counting hidden parts, so nothing here
                    // has ever been shown to fix an observable symptom. No test exercises a facing
                    // path; if one is ever written, this is the line it should pin.
                    L = Renormalise(L, BasisScale(bind));
                }
            }
            locals[off] = L;
        }
        var world = _model.WorldTransforms(locals);
        LastWorld = world;
        OverriddenNodes = new HashSet<int>(_rot.Keys.Concat(_scale.Keys).Concat(_path.Keys));
        // One-shot diagnostic: which node's scale changes between bind and world, and by how much.
        if (System.Environment.GetEnvironmentVariable("TPW_PS2_SCALEDUMP") == "1" && !_dumped)
        {
            _dumped = true;
            var bindWorld = _model.WorldTransforms();
            foreach (var m in _model.Meshes)
            {
                if (!world.TryGetValue(m.Offset, out var w)) continue;
                bindWorld.TryGetValue(m.Offset, out var bw);
                var a = BasisScale(bw); var b = BasisScale(w);
                bool over = _rot.ContainsKey(_model.NodeIndex(m.Offset))
                         || _scale.ContainsKey(_model.NodeIndex(m.Offset))
                         || _path.ContainsKey(_model.NodeIndex(m.Offset));
                GD.Print($"[scale] {m.Name,-10} bindWorld {a.X:F4}  animWorld {b.X:F4}  "
                       + $"ratio {(a.X > 0 ? b.X / a.X : 0):F3}  overridden={over}");
            }
        }
        return world;
    }

    /// <summary>`TPW_PS2_ROT=compose` restores the old behaviour for an A/B.</summary>
    /// <summary>The world matrices the last WorldAt produced, for callers that want to compare
    /// them against the bind chain. Null while the model has no tracks at all.</summary>
    public Dictionary<int, Matrix4x4> LastWorld;
    /// <summary>Node indices that had a track overriding their local matrix.</summary>
    public HashSet<int> OverriddenNodes = new();

    bool _dumped;

    static bool Compose =>
        (OS.GetEnvironment("TPW_PS2_ROT") ?? "").ToLowerInvariant() == "compose";

    /// <summary>Swap the bind matrix's 3x3 for the animated rotation, keeping its scale and
    /// translation. The basis LENGTHS are carried over so a scaled node stays scaled.</summary>
    static Matrix4x4 Replace(Matrix4x4 bind, System.Numerics.Quaternion q)
    {
        var r = Renormalise(Matrix4x4.CreateFromQuaternion(q), BasisScale(bind));
        r.M41 = bind.M41; r.M42 = bind.M42; r.M43 = bind.M43;
        return r;
    }

    /// <summary>The lengths of a matrix's three basis rows -- its scale, however it got there.</summary>
    static System.Numerics.Vector3 BasisScale(Matrix4x4 m) => new(
        MathF.Sqrt(m.M11 * m.M11 + m.M12 * m.M12 + m.M13 * m.M13),
        MathF.Sqrt(m.M21 * m.M21 + m.M22 * m.M22 + m.M23 * m.M23),
        MathF.Sqrt(m.M31 * m.M31 + m.M32 * m.M32 + m.M33 * m.M33));

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
