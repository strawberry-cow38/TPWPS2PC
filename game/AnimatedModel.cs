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
/// * a node is not drawn before its appear frame or after its disappear frame;
/// * ⭐ a SKELETAL record (the characters in DATA.WAD) is sampled into one bone matrix per
///   helper the way `FUN_001a8da8` does -- no hierarchy, the keys are the whole transform -- and
///   every part that carries a skin at `mesh+0x90` is re-skinned from it each frame, through the
///   same `mesh+0x98` map the morph path uses. The arithmetic is core's (Model.Skin,
///   SkeletalPose) and tools/TPW.PS2.SkinAudit checks it against the disc without Godot.</summary>
public sealed class AnimatedModel
{
    sealed class Part
    {
        public Model.Mesh Mesh;
        public int NodeOffset;
        public List<System.Numerics.Vector3> BindPos;
        /// <summary>⭐ The positions as last DRAWN -- morphed or skinned, not the bind pose.
        /// `RebuildGeometry` used to compute these into a local and drop them; a fitting pinned to
        /// this mesh's surface has to read the same vertices the player is looking at, or it
        /// tracks a face that is not there any more.</summary>
        public List<System.Numerics.Vector3> LivePos;
        public List<Godot.Vector2> Uv;
        public List<Godot.Vector3> Normal;
        public List<Model.Triangle> Tris;
        public int[] AnimMap;
        /// <summary>Vertex -> UV-group, from the run list at mesh +0x9c.</summary>
        public int[] UvMap;
        /// <summary>One key list per UV group, for the record now playing, or null.</summary>
        public List<Aps.UvKey[]> UvKeys;
        /// <summary>The mesh's skin, or null for a mesh that is not skinned (every ride part).</summary>
        public Model.Skin Skin;
        public List<(int[] Times, System.Numerics.Vector3[] Keys)> Morph;
        /// <summary>Per-vertex sums of every <see cref="AddLayer"/> record's morph and UV deltas, or
        /// null. Constant: a layer is posed once, the record now playing moves on top of it.</summary>
        public System.Numerics.Vector3[] LayerPos;
        public Godot.Vector2[] LayerUv;
        public MeshInstance3D[] Surfaces;          // one per material
        public int[] SurfaceMaterial;
        /// <summary>This mesh's node and every node above it, for inherited visibility.</summary>
        public List<int> Ancestry;
    }

    readonly Model _model;
    readonly Aps _anim;
    readonly bool _nativeNodeVisibility;
    readonly Func<string, (ImageTexture Tex, bool Soft)> _texture;
    readonly List<Aps.TextureTrack> _textureTracks = new();
    readonly Dictionary<int, ShaderMaterial> _materials = new();
    readonly int[] _textureIndices;
    readonly List<Part> _parts = new();
    readonly Dictionary<int, List<(int Time, System.Numerics.Quaternion Q)>> _rot = new();
    readonly Dictionary<int, int> _rotTrack = new();   // node -> its track, for the easing curves
    readonly Dictionary<int, List<(int Time, System.Numerics.Vector3 S)>> _scale = new();
    Dictionary<int, int[]> _vis = new();
    /// <summary>A skeletal record's per-MESH show/hide lists (SkeletalPose.MeshVisibility): the
    /// mechanic's toolbox and the hunter's guns come and go by these.</summary>
    Dictionary<int, int[]> _meshVis = new();
    /// <summary>Nodes currently hidden. ⚠ SURVIVES UseRecord on purpose: see SetFrame.</summary>
    readonly HashSet<int> _hidden = new();
    bool _ordinaryVisibility;
    readonly Dictionary<int, Aps.Path> _path = new();
    readonly Dictionary<int, ModelPathChannel> _modelPath = new();
    readonly HashSet<int> _facing = new();
    List<Aps.SkeletalTrack> _skel;
    /// <summary>True when the selected record drives a biped rather than vertex morph.</summary>
    public bool Skeletal { get; private set; }
    /// <summary>The record the channels are bound to; null for a model shown in its bind pose.</summary>
    public Aps.Record Record { get; private set; }

    /// <summary>⚠⚠ THE GAME'S SPACE IS LEFT-HANDED AND GODOT'S IS RIGHT-HANDED. Everything under
    /// this node is mirrored in Z to convert, which is why the viewer rendered a mirror image of
    /// the Python reference until this was added. Applying it at the ROOT rather than per-vertex
    /// conjugates the whole assembled scene -- vertices, node transforms and the hierarchy together
    /// -- so nothing can be half-converted.
    ///
    /// ⚠ A mirror reverses triangle orientation on the way to the screen. Godot allows for that
    /// itself: an instance whose global transform has a negative determinant is drawn with its
    /// cull mode reversed, so the mesh-space facing that Model.Triangles establishes survives this
    /// root. Verified by the cull_back control render, where the ground stays visible from above
    /// under this mirror. A true 180-degree yaw is (-1,1,-1), determinant +1;
    /// TPW_PS2_YAW180=1 renders it that way so the two can be compared.</summary>
    public Node3D Root { get; } = new Node3D
    {
        Scale = System.Environment.GetEnvironmentVariable("TPW_PS2_YAW180") == "1"
            ? new Godot.Vector3(-1, 1, -1)
            : new Godot.Vector3(1, 1, -1),
    };
    public int Frames { get; private set; }

    /// <summary>⭐⭐ MODEL HEADER `+0x1c & 4`: THE CHANNELS ADD TO THE BIND POSE. `0x1a7f48` passes that
    /// bit to the morph player `0x1a6d68` as its last argument, and with it set each vertex is
    /// `lerp(keys) + its current position` instead of `lerp(keys)`; the UV consumer `0x1ad378` adds
    /// its keys the same way; a path adds to the bind
    /// translation and a rotation composes onto the bind (findings/coaster-geometry.md §4.2).
    /// 18 of the disc's 496 models carry it: the 15 coaster pylons and three coaster cars. A pylon's
    /// loft keys are DELTAS (16 vertices +0 → +90 in y, 4 fixed at +0), so read as positions they
    /// collapsed the post onto its origin and left the parts above it floating.</summary>
    public bool Additive { get; }

    /// <summary>A last word on a part's UVs after the channels have run, given its drawn positions, or
    /// null. The pylon's square lattice uses it (a DEPARTURE from the console, see CoasterPylon). Gets
    /// its own copy of the list, so the bind UVs are never written.</summary>
    public Action<Model.Mesh, IReadOnlyList<System.Numerics.Vector3>, List<Godot.Vector2>> UvRewrite { get; set; }

    /// <summary>Which texture each material slot is currently showing. ⭐ The animation's OUTPUT,
    /// so a check can watch it change instead of watching the clock and hoping.</summary>
    public IReadOnlyList<int> TextureChoices => _textureIndices;
    public string Summary { get; private set; }

    /// <summary>Surfaces on the blend shader, i.e. ones whose ordering depends on the depth
    /// pre-pass. ⭐ Named so a transparency complaint can be checked against a NUMBER rather
    /// than against whether a picture looks right.</summary>
    public int BlendSurfaces { get; private set; }

    public AnimatedModel(Model model, Aps anim, Aps.Record rec,
                         Func<string, (ImageTexture Tex, bool Soft)> texture, bool nativeNodeVisibility = false)
    {
        _model = model; _anim = anim; _texture = texture;
        _nativeNodeVisibility = nativeNodeVisibility;
        Additive = model.D.Length >= 0x20 && (BitConverter.ToUInt32(model.D, 0x1c) & 4) != 0;
        if (nativeNodeVisibility)
            foreach (int offset in model.LocalTransforms().Keys)
                if ((BitConverter.ToUInt32(model.D, offset) & 0x10) != 0)
                    _hidden.Add(model.NodeIndex(offset));
        _textureIndices = new int[model.MaterialTextures.Count];
        FindUvDrivenMaterials();
        UseRecord(rec);

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
                UvMap = _model.UvVertexMap(mesh),
                Ancestry = _model.Ancestry(mesh.Index),
                Morph = MorphFor(rec, mesh.Index),
            };
            try { p.Skin = _model.ReadSkin(mesh); }
            catch (InvalidDataException e) { GD.PrintErr($"[part] {mesh.Name}: skin rejected: {e.Message}"); }
            // ⚠ The mesh+0x98 run list must name exactly the skin's animated vertices, or a slot
            // would read past the skin's tables. Checked once here, not per frame.
            if (p.Skin != null && (p.AnimMap == null || p.AnimMap.Max() + 1 != p.Skin.VertexCount))
            {
                GD.PrintErr($"[part] {mesh.Name}: skin has {p.Skin.VertexCount} animated vertices but the run list maps "
                          + (p.AnimMap == null ? "nothing" : (p.AnimMap.Max() + 1).ToString()) + " -- not skinned");
                p.Skin = null;
            }
            BindUvTrack(p, rec);
            BuildSurfaces(p);
            _parts.Add(p);
            var vs = _vis.TryGetValue(mesh.Index, out var vv) ? "vis[" + string.Join(",", vv) + "]" : "always";
            GD.Print($"[part] {mesh.Name,-10} tris={tris.Count,-5} verts={pos.Count,-5} " +
                     $"morph={(p.Morph != null ? p.Morph.Count.ToString() : "-"),-5} " +
                     $"skin={(p.Skin != null ? p.Skin.Bones.Count() + "b" : "-"),-4} " +
                     $"map={(p.AnimMap != null ? "yes" : "NO "),-4} {vs}");
        }
        RefreshSummary();
        RefreshOrdinaryVisibility();
    }

    /// <summary>Ordinary native flags0 transition, as used by the bus. 1AB938 invokes
    /// 1AA460 before binding: old-listed, unprotected nodes lose self-hidden0x10.
    /// This bounded API does not pretend to implement skeletal/index-list cleanup.</summary>
    public void ActivateNativeRecord(Aps.Record next)
    {
        if (!_nativeNodeVisibility) throw new InvalidOperationException("native transition requires native node visibility");
        if (next == null || next.Skeletal || next.IndexCount != 0 || next.Shared
            || Record is { Skeletal: true } || Record is { IndexCount: > 0 })
            throw new InvalidOperationException("unsupported native record transition");
        if (Record != null)
            for (int i = 0; i < Record.TrackCount; i++)
            {
                int node = _anim.TrackNode(_anim.TrackAt(Record, i));
                uint flags = BitConverter.ToUInt32(_model.D, _model.NodeOffset(node));
                if ((flags & 0x80000000) == 0) _hidden.Remove(node);
            }
        UseRecord(next);
        // Activation changes native node flags before the next animation evaluation.
        // Refresh visibility now, without inventing a frame-zero sample or changing pose.
        foreach (var part in _parts)
            foreach (var surface in part.Surfaces) surface.Visible = NativeMeshShown(part);
    }

    bool NativeMeshShown(Part part) =>
        AnimationNodeVisibility.Shown(_model, part.Mesh.Index, _hidden);

    /// <summary>Bind a different record of the same `.aps` to the geometry already built. A ride's
    /// `.rse` asks for a different slot as it runs -- Create, Idle, Load, Start, Main, End, Unload
    /// -- and the meshes, surfaces and materials are the MODEL's, so they stay; only the channels
    /// are rebound. The constructor comes through here as well, so a switched model is in the
    /// state a freshly built one would be in.</summary>
    public void UseRecord(Aps.Record rec)
    {
        // ⚠⚠ NOT ON A SKELETAL RECORD. A skeletal record's tracks are 20 bytes, not 48, so asking
        // for texture tracks points a 48-byte reader at a 20-byte table and it walks off the end
        // -- "Index was out of range ... (Parameter 'startIndex')" from inside the APS reader.
        // The file already warns about this further down, but the warning sat BELOW the one call
        // that breaks it. It threw for girl1a (25 tracks) and girl4a (24) and not for girl2a (22)
        // or boy1a (22), which is the shape of an overrun: whether it lands past the end depends
        // on how many tracks the record has, so it looks like a per-character quirk.
        // ⚠ The UV keys belong to the RECORD, so they must follow a record change; binding them
        // only in the constructor would leave a shop animating its Create keys forever.
        foreach (var p in _parts) BindUvTrack(p, rec);
        var textureTracks = rec is { Skeletal: true } ? new() : _anim?.TextureTracks(rec) ?? new();
        foreach (var track in textureTracks)
        {
            if (track.Material >= _model.MaterialTextures.Count ||
                track.Keys.Any(k => k.TextureIndex >= _model.MaterialTextures[track.Material].Length))
                throw new InvalidDataException($"APS texture track targets invalid material/texture: slot {track.Material}");
        }
        if (!_model.IsLegacyMd2 && (AnimationNodeVisibility.Ordinary(rec) || _ordinaryVisibility))
        {
            if (!_ordinaryVisibility) AnimationNodeVisibility.Initialize(_model, _hidden);
            _ordinaryVisibility = true;
            AnimationNodeVisibility.Transition(_model, _anim, Record, rec, _hidden);
        }
        Record = rec;
        // ⚠ EVERY CHANNEL IS EMPTIED BEFORE THE NEW RECORD FILLS IT. They are keyed by node, and a
        // node the old record drove that the new one leaves alone would otherwise keep its old keys
        // and carry on moving after the animation changed.
        _textureTracks.Clear(); _rot.Clear(); _rotTrack.Clear(); _scale.Clear(); _path.Clear(); _modelPath.Clear(); _facing.Clear();
        _vis = new(); _meshVis = new(); _skel = null; Frames = 0;
        // ⭐ _textureIndices is left alone on purpose. It is model-sized and mirrors what each
        // material is showing NOW, which is the `previous` that TextureTrack.Sample retains before
        // a track's first key -- the game's own consumer does (0x1a6b60-0x1a6bd4) -- so a slot the
        // new record never keys keeps its texture across a switch just as it does across a frame.
        // ⚠ Zeroing it without re-binding the materials would be worse than either choice: SetFrame
        // skips a slot whose sample equals the mirror, so a track whose first key is 0 would leave
        // the old texture on screen while the mirror claimed 0.
        _textureTracks.AddRange(textureTracks);
        // ⚠ THE TWO TRACK FORMATS ARE NOT INTERCHANGEABLE. A skeletal record's tracks are 20 bytes,
        // not 48, so none of the channel readers below may be pointed at one. A skeletal record is
        // kept whole in _skel and sampled by SetFrame into the parts' skins; a record with flag
        // 0x80 (Shared) has no tracks in this file -- Boy2a/3a/4a reuse Boy1a's -- and leaves the
        // character in its bind pose until it is given the file that has them.
        Skeletal = rec != null && rec.Skeletal;
        if (rec != null && !rec.Skeletal)
        {
            for (int i = 0; i < rec.TrackCount; i++)
            {
                int t = _anim.TrackAt(rec, i), node = _anim.TrackNode(t);
                var r = _anim.Rotation(t); if (r != null) { _rot[node] = r; _rotTrack[node] = t; }
                var s = _anim.Scale(t); if (s != null) _scale[node] = s;
                // ⚠⚠ THE PATH CHANNEL WAS READ AND THEN NEVER APPLIED. 1,468 of the disc's 6,155
                // morph-format tracks carry a Catmull-Rom path, and every car, train, boat and
                // gondola on them was sitting at its rest position. That is what "sub parts are
                // rotated or positioned wrong" looks like from the outside.
                var modelPath = ModelPathChannel.Read(_model, _anim, rec, t);
                if (modelPath != null) _modelPath[node] = modelPath;
                var sp = _anim.SplineAt(t);
                if (sp != null)
                {
                    _path[node] = sp;
                    if ((_anim.TrackFlags(t) & (uint)Aps.TrackFlag.OrientAlongPath) != 0) _facing.Add(node);
                }
            }
            _vis = _anim.Visibility(rec);
            Frames = Math.Max(rec.DurationFrames, 1);
        }
        else if (rec != null)
        {
            _skel = _anim.SkeletalTracks(rec);
            _meshVis = SkeletalPose.MeshVisibility(_anim, rec, _model.Meshes.Count);
            Frames = Math.Max(rec.DurationFrames, 1);
        }
        // The morph channel lives on the part, so it is re-pointed in place rather than rebuilt.
        // ⚠ A part the old record morphed and the new one does not goes back on its bind positions
        // HERE: SetFrame only rebuilds a part that has a morph, so without this it would stay
        // frozen at whatever frame the old record left it on.
        // ⚠ And a part the old record SKINNED goes back to its bind positions the same way when
        // the new record has no pose to give it.
        bool posed = _posed; _posed = false;
        foreach (var p in _parts)
        {
            bool morphed = p.Morph != null;
            p.Morph = MorphFor(rec, p.Mesh.Index);
            if ((morphed && p.Morph == null) || (posed && p.Skin != null && _skel == null)) RebuildGeometry(p, 0);
        }
        RefreshSummary();
        RefreshOrdinaryVisibility();
    }

    /// <summary>The record's morph track for a node, or null when it has none.</summary>
    List<(int[] Times, System.Numerics.Vector3[] Keys)> MorphFor(Aps.Record rec, int node)
    {
        // ⚠⚠ NOT ON A SKELETAL RECORD -- THIS was the "Index was out of range" on the riders.
        // The loop below walks the record's tracks at the 48-byte stride; a skeletal record's
        // tracks are 20 bytes, so past the first it is reading the middle of other tracks and
        // then the key data as if they were track headers. Measured over all 24 characters:
        // 130 of those garbage "tracks" carry a node below the mesh count AND the 0x1000 morph
        // bit, so Morph() dereferences a garbage header pointer. ⚠ CORRECTED after simulating
        // Morph()'s reads byte for byte: EVERY one of those pointers lands outside the file
        // (negative or past the end), so the failure is always the exception and never a silent
        // garbage morph -- 93 of the disc's 177 skeletal records throw (girl1a's and girl4a's
        // Load v0 among them) and 84 never reach a garbage track that passes the test (boy1a's
        // Load v0). No record's own 20-byte table overruns; it is what the wrong stride reads
        // THROUGH it that does. The check on rec.Skeletal is the record's own flag 0x20, read by
        // the loader (FUN_00167358) -- it does not depend on the helper-index reading of the bone
        // byte.
        if (rec == null || rec.Skeletal) return null;
        for (int i = 0; i < rec.TrackCount; i++)
        {
            int t = _anim.TrackAt(rec, i);
            if (_anim.TrackNode(t) == node) return _anim.Morph(t);
        }
        return null;
    }

    void RefreshOrdinaryVisibility()
    {
        if (!_ordinaryVisibility) return;
        foreach (var part in _parts)
            foreach (var surface in part.Surfaces)
                surface.Visible = AnimationNodeVisibility.Shown(_model, part.Mesh.Index, _hidden);
    }

    void RefreshSummary() =>
        Summary = $"{_parts.Count} parts, {Frames} frames, {_model.Materials.Count} materials"
                  + $", {_textureTracks.Count} texture tracks"
                  + (Skeletal ? $", {_skel?.Count ?? 0} bone tracks over {_parts.Count(p => p.Skin != null)} skinned parts"
                               + (_skel == null ? " (shared record, no tracks here: bind pose)" : "") : "");

    // ⚠ Culling is owned by the shader's render_mode, not by a material property. It is built
    // into the shader source from TPW_PS2_CULL rather than hardwired -- it used to say
    // `cull_disabled` while the switch below claimed a default of `back`, so the switch was a
    // fiction and nothing ever culled.
    //
    // ⭐⭐ BACK CULLING IS THE MODE THIS DATA WAS AUTHORED FOR, AND THE FILE SAYS WHICH WAY EACH
    // TRIANGLE FACES. Bit 0 of every vertex's Y word is a per-triangle facing flag, and the game's
    // VU1 microprogram culls with it (findings/formats.md, "winding is a per-triangle flag").
    // Model.Triangles honours it and hands out every triangle in OUTWARD order -- right-hand
    // normal out, counter-clockwise front. Godot's front face is CLOCKWISE, so the triangles below
    // are emitted reversed (A, C, B), uniformly, with no per-triangle guessing.

    /// ⚠ TRIED AND REJECTED, kept switchable so nobody re-tries it blind: duplicating every
    /// triangle puts two coincident faces at the SAME depth, which z-fights per pixel and looks
    /// worse than either plain mode. `TPW_PS2_CULL=two` still selects it; it is not the default.
    /// <summary>`back` is the default and the mode the data was authored for. `off` is the control:
    /// it is what the viewer did for its first weeks, and it shows the underside of every roof
    /// through the roof wherever the two are near-coplanar.</summary>
    static string CullMode => (OS.GetEnvironment("TPW_PS2_CULL") ?? "back").ToLowerInvariant();

    static bool TwoSided => CullMode is "two" or "twosided";

    /// <summary>The shader's cull render_mode. ⚠⚠ IT USED TO BE HARDWIRED TO `cull_disabled`
    /// WHILE THE SWITCH ABOVE CLAIMED A DEFAULT OF `back`, so nothing ever culled and the ground
    /// under every park drew its own underside on top of itself.</summary>
    static string CullRenderMode => CullMode is "off" or "disabled" or "none" or "two" or "twosided"
        ? "cull_disabled" : "cull_back";

    // Lighting is evaluated in the vertex stage from the stored signed-byte normals.
    // FRONT_FACING never changes it, including beneath the mirrored root.
    static Shader BlendShader => Ps2Materials.Shader(true, CullRenderMode, linearFilter: Ps2Materials.Bilinear);
    static Shader ViewerShader => Ps2Materials.Shader(false, CullRenderMode, linearFilter: Ps2Materials.Bilinear);

    /// <summary>The surface a mesh draws with a given MATERIAL index, or null.
    ///
    /// ⚠⚠ THIS EXISTS BECAUSE THE NAMING TRAP HAS NOW CAUGHT TWO CALLERS. A surface is named
    /// `"{mesh}#{material}"`, and the number is the MATERIAL INDEX, not the surface's ordinal --
    /// there is a comment saying exactly that a few lines below, and `LightingAudit` still read
    /// `CLIFFS#0` as "the first surface" and silently compared another material's vertices. Before
    /// that the water finder made the mirror-image mistake. A comment was not enough twice, so
    /// callers can now ASK instead of parsing a name.
    ///
    /// ⭐ Ask with the material you already know -- `Model.Triangles(mesh)[i].Material` -- and a
    /// mesh that does not draw that material returns null rather than handing back a neighbour.</summary>
    public MeshInstance3D SurfaceFor(string mesh, int material)
    {
        foreach (var p in _parts)
        {
            if (!string.Equals(p.Mesh.Name, mesh, StringComparison.OrdinalIgnoreCase)) continue;
            if (p.SurfaceMaterial == null || p.Surfaces == null) continue;
            for (int i = 0; i < p.SurfaceMaterial.Length; i++)
                if (p.SurfaceMaterial[i] == material) return p.Surfaces[i];
        }
        return null;
    }

    /// <summary>Every surface, as (mesh, material, node). ⭐ For a caller that wants to ENUMERATE
    /// rather than guess at a name -- the material is handed over, not encoded in a string.</summary>
    public IEnumerable<(string Mesh, int Material, MeshInstance3D Node)> Surfaces()
    {
        foreach (var p in _parts)
        {
            if (p.SurfaceMaterial == null || p.Surfaces == null) continue;
            for (int i = 0; i < p.SurfaceMaterial.Length; i++)
                yield return (p.Mesh.Name, p.SurfaceMaterial[i], p.Surfaces[i]);
        }
    }

    /// <summary>The animated-UV variant of the model shader. ⚠⚠ `rawNormals: true`, matching
    /// BlendShader/ViewerShader above. The terrain's water asks for `false` because the ground is
    /// built with byte normals; handing a MODEL the terrain's variant relights it, so a coconut
    /// that started twisting would also change colour and the twist would get the blame.</summary>
    static Shader MovingShader(bool soft) => Ps2Materials.Shader(
        soft, CullRenderMode, rawNormals: true, linearFilter: Ps2Materials.Bilinear, animated: true);

    readonly System.Collections.Generic.Dictionary<int, Godot.Vector2> _pivot = new();

    /// <summary>Where a spinning texture TURNS: the midpoint of the UV box every triangle wearing
    /// this material actually occupies.
    ///
    /// ⚠⚠ THIS IS NOT (0.5, 0.5), AND ASSUMING IT WAS IS THE BUG MASTER CAUGHT. The Coconut's
    /// drink is mapped to **U 1.000..1.999**, V 0.000..0.999 -- the SECOND tile of a repeating
    /// texture, not the first. Its real centre is (1.5, 0.5). Pivoting at (0.5, 0.5) put the
    /// pivot a whole tile away and entirely OUTSIDE the patch, so the liquid swung around a
    /// distant point instead of turning on the spot. Master: "ur rotating via the corner, not
    /// the center".
    ///
    /// ⭐ So the pivot is MEASURED off the mesh. A texture patch can sit anywhere in UV space and
    /// nothing says it starts at zero -- which is exactly the kind of thing this port keeps
    /// having to stop assuming.
    ///
    /// ⭐ Computed on demand and cached, so a terrain with 145 meshes and no swirl at all never
    /// pays for it.</summary>
    Godot.Vector2 SpinPivot(int material)
    {
        if (_pivot.TryGetValue(material, out var had)) return had;
        float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
        bool any = false;
        foreach (var mesh in _model.Meshes)
        {
            var tris = _model.Triangles(mesh);
            if (tris.Count == 0) continue;
            bool uses = false;
            foreach (var t in tris) if (t.Material == material) { uses = true; break; }
            if (!uses) continue;
            var (_, uv, _) = _model.Vertices(mesh);
            foreach (var t in tris)
            {
                if (t.Material != material) continue;
                foreach (int i in new[] { t.A, t.B, t.C })
                {
                    if (i < 0 || i >= uv.Count) continue;
                    var c = uv[i];
                    u0 = System.Math.Min(u0, c.X); u1 = System.Math.Max(u1, c.X);
                    v0 = System.Math.Min(v0, c.Y); v1 = System.Math.Max(v1, c.Y);
                    any = true;
                }
            }
        }
        return _pivot[material] = any
            ? new Godot.Vector2((u0 + u1) * 0.5f, (v0 + v1) * 0.5f)
            : new Godot.Vector2(0.5f, 0.5f);
    }

    /// <summary>The authored turn rate, in radians a second, for a material whose texture twists.
    ///
    /// ⭐⭐ READ, NOT CHOSEN. The game does not scroll this procedurally: it ships PER-VERTEX UV
    /// KEYFRAMES on an APS track whose flag is 0x10000 -- the channel this port's own enum still
    /// calls `Unknown0x24`, with its payload pointer at track+0x24. Decoding the Coconut's track
    /// shows those keys trace a CIRCLE: centre (1.4987, 0.4980), radius 0.4975..0.5005 across all
    /// 24 keys, with the angle rising monotonically and returning to its start at the last key.
    /// So the motion is exactly a rotation about the patch centre, and its SPEED is the record's
    /// own authored length -- one full turn per loop.
    ///
    /// ⭐ Which is why no global rate was ever findable: the speed is per object. The Coconut's
    /// Main record is 75 frames (2.5s at 30fps, 2.513 rad/s); the Fountain's is 50 (1.667s).
    ///
    /// ⚠ This still drives a shader rotation rather than replaying the keys vertex by vertex. The
    /// keys ARE a circle to within 0.3% of the radius, so the visible result matches; what it does
    /// not reproduce is any authored EASING between keys.</summary>
    float AuthoredSpin(int material)
    {
        if (_anim == null) return 0f;
        foreach (var rec in _anim.Records())
        {
            if (rec.Skeletal || rec.Tracks == 0 || rec.DurationFrames <= 0) continue;
            for (int t = 0; t < rec.TrackCount; t++)
            {
                int off = _anim.TrackAt(rec, t);
                if ((_anim.TrackFlags(off) & 0x10000) == 0) continue;
                // ⚠ The track names a NODE; only accept it if that node's mesh actually draws
                // this material, or a model with two twisting parts would take the wrong rate.
                string node = _model.NodeName(_anim.TrackNode(off)) ?? "";
                foreach (var mesh in _model.Meshes)
                {
                    if (!string.Equals(mesh.Name, node, StringComparison.OrdinalIgnoreCase)) continue;
                    bool draws = false;
                    foreach (var tri in _model.Triangles(mesh)) if (tri.Material == material) { draws = true; break; }
                    if (draws) return Mathf.Tau / (rec.DurationFrames / (float)Aps.Fps);
                }
            }
        }
        return 0f;
    }

    /// <summary>Attach this part's UV keyframes for the record now playing.
    ///
    /// ⚠ The run list and the track must AGREE: the mesh's +0x9c list has to name exactly as many
    /// groups as the track has entries, or an entry index would address a group that does not
    /// exist. For `cn_stall` both are 49 over 68 vertices. A mismatch means this is not the list
    /// the track meant, so the keys are dropped rather than guessed at.</summary>
    void BindUvTrack(Part p, Aps.Record rec) => p.UvKeys = UvKeysFor(rec, p);

    List<Aps.UvKey[]> UvKeysFor(Aps.Record rec, Part p)
    {
        if (_anim == null || rec == null || rec.Skeletal || rec.Tracks == 0 || p.UvMap == null) return null;
        for (int t = 0; t < rec.TrackCount; t++)
        {
            int off = _anim.TrackAt(rec, t);
            if ((_anim.TrackFlags(off) & 0x10000) == 0) continue;
            if (!string.Equals(_model.NodeName(_anim.TrackNode(off)), p.Mesh.Name,
                               StringComparison.OrdinalIgnoreCase)) continue;
            var keys = _anim.UvTrack(off);
            return keys != null && p.UvMap.Max() + 1 == keys.Count ? keys : null;
        }
        return null;
    }

    /// <summary>⭐⭐ A SECOND CHANNEL POSED UNDER THE ONE PLAYING, the way a pylon is posed: `0x19cdd0`
    /// starts four channels on one instance (loft, rotate, incline, bank) and `0x1ac6e8` applies
    /// them all, and on an <see cref="Additive"/> model each one ADDS its morph and UV deltas to
    /// what the last left. So a record whose keys are not zero at the frame it is held at still
    /// changes the mesh: MineCart's incline, held at its neutral 0.5 (frame 10 of 20), morphs
    /// nothing but adds +0.5 to the V of the post's lofted ring, and its bank adds +0.25. Without
    /// them a h100 post drew a fifth of a cross where the console draws half of one.
    ///
    /// Morph and UV only. The layer's node transforms (the incline and bank roll the invisible
    /// `TrackCentreDummy`) are not composed; nothing the port draws hangs off them.</summary>
    public void AddLayer(Aps.Record rec, float frame)
    {
        if (!Additive || _anim == null || rec == null || rec.Skeletal) return;
        foreach (var p in _parts)
        {
            if (MorphFor(rec, p.Mesh.Index) is { } morph && p.AnimMap != null && p.AnimMap.Max() < morph.Count)
            {
                var ev = morph.Select(v => Sample(v.Times, v.Keys, frame)).ToArray();
                p.LayerPos ??= new System.Numerics.Vector3[p.BindPos.Count];
                for (int j = 0; j < p.LayerPos.Length; j++) p.LayerPos[j] += ev[p.AnimMap[j]];
            }
            if (UvKeysFor(rec, p) is { } keys)
            {
                p.LayerUv ??= new Godot.Vector2[p.Uv.Count];
                for (int j = 0; j < p.LayerUv.Length; j++)
                {
                    var (u, v) = Aps.SampleUv(keys[p.UvMap[j]], frame);
                    p.LayerUv[j] += new Godot.Vector2(u, v);
                }
            }
            if (p.LayerPos != null || p.LayerUv != null) RebuildGeometry(p, 0);
        }
    }

    /// <summary>Materials whose UVs the disc animates with authored keys.
    ///
    /// ⚠⚠ COMPUTED FROM THE MODEL, NOT FROM `_parts`, AND THAT IS THE WHOLE POINT. `BuildSurfaces`
    /// runs inside the constructor's mesh loop and `_parts.Add` happens AFTER it, so a version of
    /// this that walked `_parts` saw an empty list, reported "not key-driven", and let the shader
    /// spin the swirl on top of its own keyframes. The audit caught it; it is the same ordering
    /// trap `SpinPivot` already had to dodge.
    ///
    /// ⭐ Any record counts, not just the one playing: a material animated in one record must not
    /// carry a shader spin that would still be running during another.</summary>
    readonly System.Collections.Generic.HashSet<int> _uvDriven = new();

    void FindUvDrivenMaterials()
    {
        if (_anim == null) return;
        foreach (var rec in _anim.Records())
        {
            if (rec.Skeletal || rec.Tracks == 0) continue;
            for (int t = 0; t < rec.TrackCount; t++)
            {
                int off = _anim.TrackAt(rec, t);
                if ((_anim.TrackFlags(off) & 0x10000) == 0) continue;
                string node = _model.NodeName(_anim.TrackNode(off)) ?? "";
                foreach (var mesh in _model.Meshes)
                {
                    if (!string.Equals(mesh.Name, node, StringComparison.OrdinalIgnoreCase)) continue;
                    var map = _model.UvVertexMap(mesh);
                    var keys = _anim.UvTrack(off);
                    if (map == null || keys == null || map.Max() + 1 != keys.Count) continue;
                    foreach (var tri in _model.Triangles(mesh)) _uvDriven.Add(tri.Material);
                }
            }
        }
    }

    bool UvDriven(int material) => _uvDriven.Contains(material);

    void SetTexture(ShaderMaterial material, int slot, int index)
    {
        string name = slot >= 0 && slot < _model.MaterialTextures.Count
            ? _model.MaterialTextures[slot][index] : null;
        var (tex, soft) = name != null ? _texture(name) : (null, false);
        // ⭐ A TEXTURE carries its own motion, so this is decided by the name the model asked for
        // rather than by which model is wearing it -- the engine's `fScrollRate` sits beside
        // `pcTextureFilename`, not on the material. See TextureMotion.
        var motion = TextureMotion.ForModelTexture(name);
        // ⭐⭐ KEYS WIN. A material the disc animates by keyframes gets the ordinary shader: the
        // motion is already in its vertices, and turning it again in the shader would compound
        // the two. The name-matched fallback is only for a surface the data does not cover.
        if (UvDriven(slot)) motion = TextureMotion.Still;
        material.Shader = motion.Moves ? MovingShader(soft) : (soft ? BlendShader : ViewerShader);
        if (motion.Moves)
        {
            material.SetShaderParameter("uv_scroll", new Godot.Vector2(motion.ScrollU, motion.ScrollV));
            // ⭐ Prefer the rate the disc states over the fallback in TextureMotion.
            float spin = motion.Spin != 0f ? (AuthoredSpin(slot) is var a and > 0f ? a : motion.Spin) : 0f;
            material.SetShaderParameter("uv_spin", spin);
            if (spin != 0f) material.SetShaderParameter("uv_spin_center", SpinPivot(slot));
            Ps2Materials.Register(material);
            MovingSurfaces++;
        }
        if (soft) BlendSurfaces++;
        material.SetShaderParameter("albedo_tex", tex);
        material.SetShaderParameter("has_tex", tex != null);
        Ps2Materials.BindLight(material);
    }

    /// <summary>How many of this model's surfaces wear a moving texture. ⭐ An instrument: a
    /// coconut that reports 0 has not matched its swirl, which is a different fault from a
    /// clock that is not advancing.</summary>
    public int MovingSurfaces { get; private set; }

    void BuildSurfaces(Part p)
    {
        var byMat = p.Tris.GroupBy(t => t.Material).ToList();
        p.Surfaces = new MeshInstance3D[byMat.Count];
        p.SurfaceMaterial = new int[byMat.Count];
        for (int i = 0; i < byMat.Count; i++)
        {
            // ⭐ Named after the mesh it came from, so a surface can be pointed at by name. The
            // terrain is one model with dozens of meshes in it; without names the only way to aim
            // at "the bus stop" is to guess coordinates.
            // ⚠ The number after the hash is the MATERIAL INDEX, not the surface's ordinal. It
            // used to be the ordinal, and anything reading it as a material -- the water finder
            // did -- silently matched nothing and looked exactly like "this terrain has no river".
            int m = byMat[i].Key;
            var mi = new MeshInstance3D { Name = $"{p.Mesh.Name}#{m}" };
            if (!_materials.TryGetValue(m, out var mat))
            {
                _materials[m] = mat = new ShaderMaterial();
                SetTexture(mat, m, 0);
            }
            mi.MaterialOverride = mat;
            p.SurfaceMaterial[i] = m;
            p.Surfaces[i] = mi;
            Root.AddChild(mi);
        }
        RebuildGeometry(p, 0);
    }

    void RebuildGeometry(Part p, float now, Matrix4x4[] pose = null)
    {
        var pos = p.BindPos;
        // (LivePos is assigned once `pos` is final, below.)
        if (pose != null && p.Skin != null && p.AnimMap != null)
        {
            // ⭐ The game's own skinning per animated vertex (Model.Skin.Deform), fanned out to
            // the strip slots through the same run list the morph path uses.
            var ev = new System.Numerics.Vector3[p.Skin.VertexCount];
            for (int i = 0; i < ev.Length; i++) ev[i] = p.Skin.Deform(i, pose);
            pos = p.AnimMap.Select(i => ev[i]).ToList();
        }
        else if (p.Morph != null && p.AnimMap != null)
        {
            var ev = p.Morph.Select(v => Sample(v.Times, v.Keys, now)).ToArray();
            pos = Additive ? p.AnimMap.Select((i, j) => p.BindPos[j] + ev[i]).ToList()
                           : p.AnimMap.Select(i => ev[i]).ToList();
        }
        if (p.LayerPos != null && pose == null) pos = pos.Select((x, j) => x + p.LayerPos[j]).ToList();
        // ⭐ Kept now that it is final, for fittings pinned to this surface. Same list object as
        // BindPos when nothing deforms, which is correct: then the bind pose IS what is drawn.
        p.LivePos = pos;
        // ⭐⭐ AUTHORED UV KEYFRAMES, the game's real moving-texture channel. One sample per
        // GROUP, fanned out to vertices through the +0x9c run list -- the same shape the position
        // paths above use, because the console walks one run list per channel.
        var uv = p.Uv;
        if (p.UvKeys != null && p.UvMap != null)
        {
            var sampled = new Godot.Vector2[p.UvKeys.Count];
            for (int i = 0; i < sampled.Length; i++)
            {
                var (u, v) = Aps.SampleUv(p.UvKeys[i], now);
                sampled[i] = new Godot.Vector2(u, v);
            }
            // Additive models ADD the keys to the authored UV (0x1ad378's last argument is the same
            // header bit): a pylon's loft keys run V 0 -> 7.493 on the top vertices, so the lattice
            // tiles up the post instead of pinning every vertex to U = 0.
            uv = Additive ? p.UvMap.Select((i, j) => p.Uv[j] + sampled[i]).ToList()
                          : p.UvMap.Select(i => sampled[i]).ToList();
        }
        if (p.LayerUv != null) uv = uv.Select((x, j) => x + p.LayerUv[j]).ToList();
        if (UvRewrite != null) { uv = new List<Godot.Vector2>(uv); UvRewrite(p.Mesh, pos, uv); }
        int si = 0;
        foreach (var grp in p.Tris.GroupBy(t => t.Material))
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            st.SetCustomFormat(0, SurfaceTool.CustomFormat.RgbFloat);
            bool two = TwoSided;
            foreach (var t in grp)
            {
                // ⭐ Model.Triangles already put every triangle in OUTWARD order (right-hand normal
                // out) from the file's own per-triangle facing flag. Godot's front face is
                // CLOCKWISE, so emit reversed: A, C, B. One rule for every triangle.
                //
                // ⚠⚠ This used to pick the order per triangle by comparing the geometric normal
                // with the stored vertex normals -- and had the sign backwards, so it faced EVERY
                // triangle away from its normal. With culling off nobody could see that; the first
                // real cull_back render lost the ground, the road and the sea (63% of the frame)
                // while the bus shelters, inside out, happened to look plausible. The reader's
                // parity swap before that pointed half the ground down too. Neither guess is
                // needed now that the file's flag is read.
                foreach (var idx in new[] { t.A, t.C, t.B })
                {
                    st.SetUV(uv[idx]);
                    // ⭐ The model's OWN normal, not one derived from triangle order.
                    st.SetNormal(p.Normal[idx]);
                    var raw = p.Normal[idx] * 127f;
                    st.SetCustom(0, new Color(Mathf.Round(raw.X), Mathf.Round(raw.Y), Mathf.Round(raw.Z), 0));
                    var v = pos[idx];
                    st.AddVertex(new Godot.Vector3(v.X, v.Y, v.Z));
                }
                if (!two) continue;
                foreach (var idx in new[] { t.A, t.B, t.C })  // back copy
                {
                    st.SetUV(uv[idx]);
                    var raw = -p.Normal[idx] * 127f;
                    st.SetCustom(0, new Color(Mathf.Round(raw.X), Mathf.Round(raw.Y), Mathf.Round(raw.Z), 0));
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
        // The APS clock drives texture choices too. Shared materials update every surface using
        // the slot; image lookup still goes through the viewer's owner-scoped texture cache.
        foreach (var track in _textureTracks)
        {
            int slot = track.Material, index = track.Sample(now, _textureIndices[slot]);
            if (index == _textureIndices[slot]) continue;
            if (_materials.TryGetValue(slot, out var material)) SetTexture(material, slot, index);
            _textureIndices[slot] = index;
        }
        // ⭐⭐ VISIBILITY IS STATE ON THE NODE, NOT A PROPERTY OF THE RECORD. `0x1a7f48` reads a
        // track's appear/disappear list ONLY when the track carries flag `0x20000`, and a track
        // without it falls straight through and touches nothing -- it does not make the node
        // visible. The flag it sets is `0x10` on the node itself, so the answer outlives the
        // animation that gave it.
        //
        // ⚠⚠ THAT IS THE BUG MASTER SPOTTED IN THE APE. Only Crazy Ape's `Create` record carries
        // visibility -- the crate appears at 28 and is smashed at 100, the shards appear at 100
        // and vanish at 138 -- and every other record (Idle, Load, Start, seven Mains, End, Break)
        // has three or four tracks and says nothing about them. Treating "not keyed" as "visible"
        // brought a destroyed crate and its debris back the moment the script left Create, which
        // is what the flying bananas in the first film were. Across JUNGLE it is the rule and not
        // an exception: 27 `Create` records carry visibility against 13 Mains and 4 others.
        foreach (var (node, timeline) in _vis)
        {
            if (Aps.VisibleAt(timeline, now)) _hidden.Remove(node); else _hidden.Add(node);
        }
        // ⭐ And the skeletal path's own lists, per MESH, by FUN_001a8c30's rule -- the same node
        // state bit (0x10 on the mesh header), so it lives in the same set and survives UseRecord
        // the same way. A mesh's index IS its node index.
        foreach (var (mesh, times) in _meshVis)
        {
            if (SkeletalPose.MeshShown(times, now, !_hidden.Contains(mesh))) _hidden.Remove(mesh); else _hidden.Add(mesh);
        }
        var world = WorldAt(now);
        // ⭐ THE BIPED. A skeletal record's tracks are each bone's whole transform -- there is no
        // hierarchy to compose -- sampled the sampler's way into one matrix per helper and shared
        // by every part, exactly as FUN_001a8da8 fills one matrix array and then walks every mesh.
        var pose = Skeletal && _skel != null ? SkeletalPose.At(_skel, now, _model.HelperCount) : null;
        if (pose != null) _posed = true;
        foreach (var p in _parts)
        {
            // ⚠⚠ VISIBILITY IS INHERITED. 183 of the disc's 2,033 appear/disappear entries are
            // keyed on a HELPER rather than a mesh -- the game hides `Dummy01` to take the arms
            // off with it. Checking only the mesh's own entry left those parts on screen.
            bool shown = true;
            foreach (var node in p.Ancestry ?? new List<int> { p.Mesh.Index })
                if (_hidden.Contains(node)) { shown = false; break; }
            if (_ordinaryVisibility || _nativeNodeVisibility)
                shown = AnimationNodeVisibility.Shown(_model, p.Mesh.Index, _hidden);
            foreach (var surface in p.Surfaces) surface.Visible = shown;
            // Native hiding is a draw state, not a reason to freeze evaluated pose.
            if (!shown && !_ordinaryVisibility && !_nativeNodeVisibility) continue;
            // ⚠ `|| p.UvKeys != null` -- a part whose ONLY animation is its UVs has no morph and
            // no skin, so the old gate skipped it and it would never have been rebuilt at all.
            if (((p.Morph != null || (pose != null && p.Skin != null)) && p.AnimMap != null)
                || p.UvKeys != null || p.LayerPos != null || p.LayerUv != null) RebuildGeometry(p, now, pose);
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
        if (_rot.Count == 0 && _scale.Count == 0 && _path.Count == 0 && _modelPath.Count == 0)
        {
            OverriddenNodes = new HashSet<int>();
            return LastWorld = _model.WorldTransforms();
        }

        var locals = _model.LocalTransforms();
        foreach (var node in _rot.Keys.Concat(_scale.Keys).Concat(_path.Keys).Concat(_modelPath.Keys).Distinct())
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
                L = Compose || Additive ? Matrix4x4.CreateFromQuaternion(q) * L : Replace(L, q);
            }
            if (_scale.TryGetValue(node, out var sk))
                L = Renormalise(L, Sample(sk.Select(x => x.Time).ToArray(),
                                          sk.Select(x => x.S).ToArray(), now));

            L.M41 = bind.M41; L.M42 = bind.M42; L.M43 = bind.M43;   // translation stays put
            if (_path.TryGetValue(node, out var path))
            {
                // The path REPLACES the bind translation: the curve is where the thing actually is --
                // unless the model is additive, when it is added to it.
                var at = path.At(now);
                if (Additive) { L.M41 = bind.M41 + at.X; L.M42 = bind.M42 + at.Y; L.M43 = bind.M43 + at.Z; }
                else { L.M41 = at.X; L.M42 = at.Y; L.M43 = at.Z; }
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
            if (_modelPath.TryGetValue(node, out var modelPath)) L = modelPath.Apply(L, now);
            locals[off] = L;
        }
        var world = _model.WorldTransforms(locals);
        LastWorld = world;
        OverriddenNodes = new HashSet<int>(_rot.Keys.Concat(_scale.Keys).Concat(_path.Keys).Concat(_modelPath.Keys));
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

    /// <summary>The positions a mesh node was last drawn with, or null. ⚠ Null before the first
    /// rebuild, which is a real state: a fitting asked for too early must fall back rather than
    /// read a bind pose and pretend it is live.</summary>
    public List<System.Numerics.Vector3> LivePositions(int nodeOffset)
    {
        foreach (var p in _parts) if (p.NodeOffset == nodeOffset) return p.LivePos;
        return null;
    }

    /// <summary>⚠ FOR A CHECK: the bind pose, which is what an offline read of the disc sees.</summary>
    public List<System.Numerics.Vector3> BindPositions(int nodeOffset)
    {
        foreach (var p in _parts) if (p.NodeOffset == nodeOffset) return p.BindPos;
        return null;
    }
    /// <summary>Node indices that had a track overriding their local matrix.</summary>
    public HashSet<int> OverriddenNodes = new();

    bool _dumped;
    /// <summary>Whether SetFrame has skinned the parts from a skeletal pose since the last UseRecord.</summary>
    bool _posed;

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
