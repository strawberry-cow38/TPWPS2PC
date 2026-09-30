using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>
/// ⭐⭐ THE RIDE HOARDING: the construction fence that rises round a broken, condemned or upgrading ride
/// (findings/ride-hoarding.md; core <see cref="RideHoarding"/>). strawberry, on the console: "broken down
/// rides dont get the construction fences build around them".
///
/// What is native here, and where it was read:
/// - the geometry is `0x1f3dd0`'s, built once per placed model from its `.sam`'s `Info.Hoarding` and its
///   root node's vertices (<see cref="HoardingGeometry"/>), hidden until raised;
/// - it hangs under the model's INSTANCE node -- <see cref="AnimatedModel.Root"/>, the node
///   <see cref="Park.TryPlace"/> turns and places -- in its model space (1 unit = 1 cell, footprint corner at
///   the origin, y 0 at the base): the console draws it as a model chained to the ride's (`0x1f6230` links
///   `+0x98`) with the ride's instance matrix and an identity root (`0x170380`). Not under the root MESH's
///   own 0.1-scale matrix, which only the ride's vertices carry;
/// - raised and lowered by the ride service's own calls (<see cref="ParkSim.HoardingRaise"/>,
///   <see cref="ParkSim.HoardingLower"/>), ticked with the park's PAUSABLE wall-clock seconds, reshaped as it
///   moves; textures `Closed/Hoarding/Condemn/Upgrade.ssh` from DATA.WAD `/Generic/MiscMesh/textures/`,
///   opaque, both faces drawn (the descriptor's `+0xc |= 1` mirrors every vertex into a second winding).
///
/// ⚠ ADAPTERS, each said again where it lives:
/// - drawn with the viewer's ordinary PS2 model shader, `cull_disabled` for the two windings; the model's
///   render flags 6 (`node |= 0x80300`, `model +0x1c |= 0x4020`) are untraced (research §9);
/// - the mesh is built at the state's CURRENT progress, never at the as-built full height: whether the
///   console's instance tick runs before the first raise can draw that for one frame is untraced (§9);
/// - the tick is one call a rendered frame after the park's step (`0x1f2fc0` is a registered callback, its
///   order against the ride update untraced), with the frame's delta while the park runs.
/// </summary>
public partial class Viewer
{
    sealed class HoardingView
    {
        public RideHoarding State;
        public MeshInstance3D Node;
        public ArrayMesh Mesh;
        public string Name;
        /// <summary>What the node shows now, so an unchanged frame rebuilds nothing.</summary>
        public float DrawnProgress = -1f;
        public HoardingTexture DrawnTexture = (HoardingTexture)255;
        public int Rebuilds;
    }
    /// <summary>By placement id, the id the sim's <see cref="ParkRide.Id"/> carries.</summary>
    readonly Dictionary<int, HoardingView> _hoardings = new();
    /// <summary>One material per texture handle k, shared by every fence (the console's list `0x2ea790` is
    /// one global set, not per world).</summary>
    ShaderMaterial[] _hoardingMaterials;
    /// <summary>For the smoke: the texture each material was loaded from.</summary>
    ImageTexture[] _hoardingTextures;

    /// <summary>Build a placed model's fence, hidden, if its definition has an `Info.Hoarding` block and its
    /// root passes `0x1f3dd0`'s gate. Every placed thing with a block gets one, as every template does on the
    /// console -- a shop's is built and simply never raised, since only rides reach the service.</summary>
    void BuildHoarding(int id, RideDefinition def, AnimatedModel drawn, Model mesh, string name)
    {
        if (def?.Hoarding is not { } rows || drawn?.Root == null) return;
        var grid = HoardingGrid.Parse(rows, out string fault);
        if (grid == null) { GD.Print($"[hoarding] {name}: Info.Hoarding does not parse ({fault}) -- no fence"); return; }
        if (!HoardingGeometry.RootVertices(mesh, out var root, out string gate))
        {
            GD.Print($"[hoarding] {name}: the root gate refuses it ({gate}) -- no fence, as on the console");
            return;
        }
        var state = new RideHoarding(HoardingGeometry.Build(grid, root));
        var node = new MeshInstance3D { Name = "hoarding", Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        var view = new HoardingView { State = state, Node = node, Mesh = new ArrayMesh(), Name = name };
        node.Mesh = view.Mesh;
        RemoveHoarding(id);
        drawn.Root.AddChild(node);
        _hoardings[id] = view;
        EnsureHoardingSinks();
        GD.Print($"[hoarding] {name}: {state.Geometry.Panels.Count} panels over {grid.Width}x{grid.Height} ({gate}), hidden until the service raises it");
    }

    /// <summary>The sim's sinks, pointed here once the sim exists (a ride that can raise one has always
    /// been through <see cref="StartScript"/>, which makes it).</summary>
    void EnsureHoardingSinks()
    {
        if (_sim == null) return;
        _sim.HoardingRaise = OnHoardingRaise;
        _sim.HoardingLower = OnHoardingLower;
    }

    void OnHoardingRaise(ParkRide ride, int bits)
    {
        if (ride == null || !_hoardings.TryGetValue(ride.Id, out var v)) return;
        bool was = v.State.Shown;
        v.State.Raise(bits);
        if (!was) GD.Print($"[hoarding] {v.Name}: raised with bits {bits} ({v.State.Texture}.ssh) from p {v.State.Progress:F2}");
        PresentHoarding(v);
    }

    void OnHoardingLower(ParkRide ride)
    {
        if (ride == null || !_hoardings.TryGetValue(ride.Id, out var v)) return;
        // ⚠⚠ READ THE KIND BEFORE LOWERING IT. `Lower()` sets the texture back to Closed, so asking
        // afterwards always answers "Closed" and the reason the fence went up is gone.
        var was = v.State.Texture;
        v.State.Lower();
        if (v.State.Shown) GD.Print($"[hoarding] {v.Name}: lowering from p {v.State.Progress:F2}");
        PresentHoarding(v);
        // ⭐⭐ THE FENCE COMES DOWN FOR MORE THAN ONE REASON, and the effects differ. RideService
        // says "the fence and the repair sparkle go together" -- true, but an UPGRADE finishing
        // drops the same fence, and the console has its own `89 Upgrade` for that. The first
        // version of this threw the repair spiral at both: the mechanic scene fired it TWICE,
        // once for the repair and once for the upgrade, which is how it was caught.
        //
        // ⚠ Condemn and Closed emit NOTHING rather than a guess. A condemned ride keeps its skull
        // and what the console throws when that fence drops is not read -- and inventing a sparkle
        // for it would be a made-up effect on a state nobody has looked at.
        int fx = was switch
        {
            HoardingTexture.Hoarding => RepairEffectId,     // 51 Repair -- a mechanic finished
            HoardingTexture.Upgrade  => UpgradeEffectId,    // 89 Upgrade -- an upgrade installed
            _ => 0,
        };
        if (fx > 0 && v.Node != null && IsInstanceValid(v.Node))
            EngineFx(fx, v.Node.GlobalPosition, $"{was.ToString().ToLowerInvariant()} {v.Name}");
    }

    /// <summary>A rendered frame's worth: the park's pausable clock, so a held park holds its fences.</summary>
    void StepHoardings(double delta)
    {
        if (!ParkSimulationRunning || _hoardings.Count == 0) return;
        TickHoardings((float)delta);
    }

    /// <summary>`0x1f2e70` → `0x1f5c10(dt, inst)` for every fence, then the reshape where it moved.</summary>
    void TickHoardings(float dtSeconds)
    {
        List<int> gone = null;
        foreach (var (id, v) in _hoardings)
        {
            if (!IsInstanceValid(v.Node)) { (gone ??= new()).Add(id); continue; }
            bool shown = v.State.Shown;
            if (v.State.Tick(dtSeconds) || shown != v.State.Shown) PresentHoarding(v);
            if (shown && !v.State.Shown) GD.Print($"[hoarding] {v.Name}: down and hidden");
        }
        if (gone != null) foreach (int id in gone) _hoardings.Remove(id);
    }

    /// <summary>`0x1f5750`'s reshape: each panel's top at its height f, its bottom UVs at v = 1 - f.</summary>
    void PresentHoarding(HoardingView v)
    {
        if (!IsInstanceValid(v.Node)) return;
        var s = v.State;
        v.Node.Visible = s.Shown;
        if (!s.Shown) { v.DrawnProgress = -1f; return; }
        if (s.Texture != v.DrawnTexture)
        {
            v.Node.MaterialOverride = HoardingMaterial(s.Texture);
            v.DrawnTexture = s.Texture;
        }
        if (s.Progress == v.DrawnProgress) return;
        v.DrawnProgress = s.Progress;
        var panels = s.Geometry.Panels;
        v.Mesh.ClearSurfaces();
        if (panels.Count == 0) return;
        var pos = new Vector3[panels.Count * 4];
        var uv = new Vector2[panels.Count * 4];
        var normal = new Vector3[panels.Count * 4];
        var index = new int[panels.Count * 6];
        for (int i = 0; i < panels.Count; i++)
        {
            var p = panels[i];
            float f = s.Height(i);
            int b = i * 4;
            // v0 over v2 at u 0, v1 over v3 at u 1; the tops keep v = 1 (0x169608 at build), the bottoms move.
            pos[b] = new Vector3(p.Top0.X, f, p.Top0.Y); uv[b] = new Vector2(0, 1);
            pos[b + 1] = new Vector3(p.Top1.X, f, p.Top1.Y); uv[b + 1] = new Vector2(1, 1);
            pos[b + 2] = new Vector3(p.Top0.X, 0, p.Top0.Y); uv[b + 2] = new Vector2(0, 1 - f);
            pos[b + 3] = new Vector3(p.Top1.X, 0, p.Top1.Y); uv[b + 3] = new Vector2(1, 1 - f);
            var n = new Vector3(p.Normal.X, p.Normal.Y, p.Normal.Z);
            normal[b] = normal[b + 1] = normal[b + 2] = normal[b + 3] = n;
            int k = i * 6;
            index[k] = b; index[k + 1] = b + 2; index[k + 2] = b + 1;
            index[k + 3] = b + 1; index[k + 4] = b + 2; index[k + 5] = b + 3;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = pos;
        arrays[(int)Mesh.ArrayType.Normal] = normal;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.Index] = index;
        v.Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        v.Rebuilds++;
    }

    /// <summary>Texture handle k's material. ⚠ `rawNormals: false`: the normals above are the game-space
    /// unit vectors `0x1696a0` writes as `n x 127`, which the shader scales back by 127 -- the same lighting
    /// arithmetic the ride models get from their byte normals. ⚠ `cull_disabled` for the two windings.</summary>
    ShaderMaterial HoardingMaterial(HoardingTexture t)
    {
        if (_hoardingMaterials == null)
        {
            _hoardingMaterials = new ShaderMaterial[4];
            _hoardingTextures = new ImageTexture[4];
            foreach (HoardingTexture k in Enum.GetValues<HoardingTexture>())
            {
                ImageTexture tex = null;
                string path = $"/Generic/MiscMesh/textures/{k}.ssh";
                try
                {
                    if (_lib?.ReadGeneric(path) is { } raw)
                    {
                        var ssh = new Ssh(raw);
                        var img = Image.CreateFromData(ssh.Width, ssh.Height, false, Image.Format.Rgba8, ssh.Pixels);
                        img.GenerateMipmaps();
                        tex = ImageTexture.CreateFromImage(img);
                    }
                }
                catch (Exception e) { GD.Print($"[hoarding] {path} would not decode: {e.Message} -- drawn untextured"); }
                if (tex == null) GD.Print($"[hoarding] DATA.WAD has no {path} -- drawn untextured");
                var m = new ShaderMaterial { Shader = Ps2Materials.Shader(false, "cull_disabled", rawNormals: false, linearFilter: Ps2Materials.Bilinear) };
                m.SetShaderParameter("albedo_tex", tex);
                m.SetShaderParameter("has_tex", tex != null);
                Ps2Materials.BindLight(m);
                _hoardingMaterials[(int)k] = m;
                _hoardingTextures[(int)k] = tex;
            }
        }
        return _hoardingMaterials[(int)t];
    }

    /// <summary>A demolished ride's fence goes with its model (it is the model's child); this forgets it.</summary>
    void RemoveHoarding(int id)
    {
        if (!_hoardings.Remove(id, out var v)) return;
        if (IsInstanceValid(v.Node)) v.Node.QueueFree();
    }

    /// <summary>A new park: the old park's models, and so their fences, are going.</summary>
    void ClearHoardings() => _hoardings.Clear();
}
