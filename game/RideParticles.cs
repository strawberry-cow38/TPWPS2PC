using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The particles a ride's script asks for, made visible.
///
/// ⭐⭐ THE SCRIPT CHOOSES THEM AND THE DISC DESCRIBES THEM. `EVENT 2 1 22` in Crazy Ape's
/// bytecode means "effect 22 at node 1", effect 22 in `Tp2.plb` is called `ApeSnot`, and the
/// sixteen colours it fades through are in that same record. Nothing here picks an effect, a
/// colour or a place: the id comes from the ride's own script, the colour ramp from the game's
/// own library, and the position from the node the instruction named.
///
/// ⚠⚠ THE MOTION IS OURS AND THE SPRITE IS OURS. What is read is the id, the node, the colour
/// ramp and the sprite PAIR (which group, how many frames); what is NOT read is the emitter's
/// velocity model, its spread, its gravity, or which of PARTICLE.WAD's 108 images a sprite group
/// resolves to -- `0x182680` halves that index into an executable-authored table nobody has
/// walked yet. So a burst here is in the right place, at the right moment, in the right colours,
/// and moves the way THIS file says rather than the way the console did. Said plainly because a
/// puff of orange smoke in the right spot looks exactly like a finished feature.</summary>
public sealed class RideParticles
{
    readonly Node3D _root;
    readonly ParticleLibrary _library;
    readonly List<(CpuParticles3D Node, ulong Until)> _live = new();
    static Texture2D _dot;

    public RideParticles(Node3D parent, ParticleLibrary library)
    {
        _library = library;
        _root = new Node3D { Name = "Particles" };
        parent.AddChild(_root);
    }

    public int Spawned { get; private set; }

    /// <summary>A soft round blob, made here. ⚠ NOT the game's sprite: PARTICLE.WAD's images are
    /// indexed through a table in the executable that has not been read, so this stands in for
    /// them and is one grey dot for every effect.</summary>
    static Texture2D Dot()
    {
        if (_dot != null) return _dot;
        const int n = 32;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp(1f - d, 0f, 1f);
                img.SetPixel(x, y, new Color(1, 1, 1, a * a));
            }
        return _dot = ImageTexture.CreateFromImage(img);
    }

    /// <summary>The effect's own sixteen steps, as a Godot ramp.</summary>
    static Gradient RampOf(ParticleEffect e)
    {
        var g = new Gradient();
        // ⚠ Gradient starts with two points of its own; they have to go or they blend into the
        // effect's colours and every burst comes out part white.
        g.RemovePoint(1);
        g.RemovePoint(0);
        for (int i = 0; i < e.Ramp.Length; i++)
        {
            var (r, gr, b, a) = e.ColourAt((i + 0.5f) / e.Ramp.Length);
            g.AddPoint(i / (float)(e.Ramp.Length - 1), new Color(r / 255f, gr / 255f, b / 255f, a / 255f));
        }
        return g;
    }

    /// <summary>Put one burst of <paramref name="id"/> at <paramref name="where"/>.</summary>
    public ParticleEffect Emit(int id, Vector3 where)
    {
        var e = _library?[id];
        if (e == null || e.Ramp.All(c => c == 0)) return null;

        int count = Math.Clamp(e.CountGuess, 4, 120);
        float life = Math.Clamp(e.LifetimeGuess / 1000f, 0.2f, 4f);
        float size = Math.Clamp(e.SizeGuess / 64f, 0.05f, 1.5f);

        var p = new CpuParticles3D
        {
            Amount = count,
            Lifetime = life,
            OneShot = true,
            Explosiveness = 0.65f,
            Emitting = false,
            ColorRamp = RampOf(e),
            Direction = Vector3.Up,
            Spread = 35f,
            InitialVelocityMin = size * 1.5f,
            InitialVelocityMax = size * 3.5f,
            Gravity = new Vector3(0, -1.5f, 0),
            ScaleAmountMin = size * 0.5f,
            ScaleAmountMax = size,
            // ⚠⚠ A CpuParticles3D DRAWS A MESH, NOT A TEXTURE. It has no Texture property at all,
            // and with Mesh left null it emits perfectly happily and renders NOTHING -- the
            // counter goes up, the log looks right, the screen stays empty. Found by emitting a
            // control burst at the camera's focus, owing nothing to the node table or the script:
            // that did not appear either, which said the emitter was wrong rather than the place.
            Mesh = new QuadMesh { Size = Vector2.One },
            // ⚠ And it is culled by an AABB Godot cannot infer for a one-shot, so the burst
            // vanishes the moment its ORIGIN leaves the frame.
            VisibilityAabb = new Aabb(new Vector3(-8, -8, -8), new Vector3(16, 16, 16)),
        };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            // ⚠ BillboardMode.Particles drops ScaleAmount unless this is set.
            BillboardKeepScale = true,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = Dot(),
            DisableReceiveShadows = true,
        };
        ((QuadMesh)p.Mesh).Material = mat;
        // ⚠⚠ POSITION BEFORE AddChild. A one-shot emits at the transform it had when it entered
        // the tree, so setting it afterwards puts the whole burst at the origin.
        p.Position = where;
        _root.AddChild(p);
        p.Emitting = true;
        _live.Add((p, Time.GetTicksMsec() + (ulong)(life * 1000) + 500));
        Spawned++;
        return e;
    }

    /// <summary>Drop the bursts that have finished. ⚠ A freed node answers as if it were alive
    /// right until it throws, so validity is checked and not assumed.</summary>
    public void Step()
    {
        ulong now = Time.GetTicksMsec();
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var (node, until) = _live[i];
            if (node == null || !GodotObject.IsInstanceValid(node)) { _live.RemoveAt(i); continue; }
            if (now < until) continue;
            node.QueueFree();
            _live.RemoveAt(i);
        }
    }

    public void Clear()
    {
        foreach (var (node, _) in _live) if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        _live.Clear();
    }
}
