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
        // ⚠⚠ A GRADIENT CANNOT BE EMPTIED, AND THE OLD CODE'S COMMENT DESCRIBED AN INTENT IT DID
        // NOT ACHIEVE. It was `RemovePoint(1); RemovePoint(0);` to clear Godot's two default
        // points before adding the effect's own -- but `remove_point` refuses at
        // `points.size() <= 1`, so the SECOND call always failed and left one default point
        // behind, blended into every burst. It failed loudly in Godot's log
        // ("Condition \"points.size() <= 1\" is true") and silently in the picture, because an
        // ERROR is printed and execution carries on.
        //
        // ⭐ Assigning Offsets and Colors REPLACES the ramp wholesale, so there is nothing to
        // remove. Offsets first: it sizes the point array, and Colors then fills it.
        var offsets = new float[e.Ramp.Length];
        var colors = new Color[e.Ramp.Length];
        for (int i = 0; i < e.Ramp.Length; i++)
        {
            var (r, gr, b, a) = e.ColourAt((i + 0.5f) / e.Ramp.Length);
            offsets[i] = i / (float)(e.Ramp.Length - 1);
            colors[i] = new Color(r / 255f, gr / 255f, b / 255f, a / 255f);
        }
        g.Offsets = offsets;
        g.Colors = colors;
        return g;
    }

    /// <summary>Put one burst of <paramref name="id"/> at <paramref name="where"/>.</summary>
    public ParticleEffect Emit(int id, Vector3 where)
    {
        var e = _library?[id];
        if (e == null || e.Ramp.All(c => c == 0)) return null;
        // ⚠⚠ NOT WHILE THE HOLDER IS OUT OF THE TREE. `Emitting = true` makes Godot ask for the
        // node's global transform, and a node whose ANCESTOR chain is detached answers that with
        // `get_global_transform: !is_inside_tree()` -- an engine error, not an exception, so it
        // scrolls past and the burst silently never happens.
        //
        // ⭐ astraclaw's rendered audit caught it against the construction puff, which fires from
        // ride placement -- and placement runs in scenes that build their park before the holder
        // is attached. The check belongs HERE rather than at that one call site: every caller has
        // the same exposure and only this function knows what it is about to touch.
        if (_root == null || !GodotObject.IsInstanceValid(_root) || !_root.IsInsideTree()) return null;

        // ⭐⭐ THE RECORD IS NOW READ FROM THE CODE THAT RUNS IT (findings/particles.md,
        // ParticleTemplate). Everything this block used to guess was wrong:
        //   +0x74 is the START SIZE, not a lifetime      (+0xa6 is the end size)
        //   +0x78 is the particle's LIFE IN TICKS, not a count; a tick is 31 ms
        //   there is NO count field at all -- emission is a burst plus per-quarter rate bytes,
        //   capped by a max-live, and the whole lot scaled by the retail density 400/1024
        //
        // ⭐ That last part is master's bug. ApeSnot emits about EIGHT particles over 0.31 s;
        // this code was drawing SEVENTY-FIVE at once, which is why bursts read as solid paint
        // however transparent each one was. "piles", exactly.
        var t = ParticleTemplate.Of(e);
        int count = Math.Clamp(t.ExpectedTotal(), 1, 200);
        float life = Math.Clamp(t.Life * ParticleTemplate.TickMilliseconds / 1000f, 0.05f, 8f);
        // Drawn width is size/5120 CELLS and one cell is one unit here; start and end differ, so
        // the scale range is the record's own taper rather than an invented 0.5..1 spread.
        float size0 = t.StartSize / 5120f, size1 = t.EndSize / 5120f;
        float size = Math.Max(size0, size1);
        // The emitter's own life decides how long the rate bytes keep firing; a one-shot burst
        // finishing inside that is the common case. ⚠ 37 effects are IMMORTAL and never stop.
        float emitterSeconds = t.Immortal ? life
            : Math.Clamp(t.EmitterLife * ParticleTemplate.TickMilliseconds / 1000f, 0f, 8f);

        var p = new CpuParticles3D
        {
            Amount = count,
            Lifetime = life,
            OneShot = true,
            // ⭐ Explosiveness is now DERIVED: the record says how much is a burst at spawn and
            // how much trickles out over the emitter's life, so the fraction born at once is the
            // burst's share of the total rather than a number I picked.
            Explosiveness = t.Burst > 0 && count > 0
                ? Mathf.Clamp(ParticleTemplate.DensityScaled(t.Burst, ParticleTemplate.RetailDensity) / (float)count, 0f, 1f)
                : (emitterSeconds <= 0f ? 1f : 0.1f),
            Emitting = false,
            ColorRamp = RampOf(e),
            Direction = Vector3.Up,
            Spread = 35f,
            InitialVelocityMin = size * 1.5f,
            InitialVelocityMax = size * 3.5f,
            Gravity = new Vector3(0, -1.5f, 0),
            ScaleAmountMin = Math.Max(0.01f, Math.Min(size0, size1)),
            ScaleAmountMax = Math.Max(0.02f, Math.Max(size0, size1)),
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
            // ⚠ NOT ADDITIVE FOR EVERYTHING -- see ParticleEffect.Additive. Master, on the live
            // park: "still way too opaque everywhere". Additive blending cannot darken, so it
            // saturates towards white over a bright scene and never reads as translucent.
            // ⚠ CANDIDATE, AND THE POLARITY IS NOW THE OTHER WAY ROUND. Bit 2 of +0x70 is READ
            // as far as draw flag 0x20; what 0x20 means at the GS was not read. It is SET on
            // Fire, Flames, explosions, Twinkle, Sparkle and ApeSnot and CLEAR on Smoke, Steam,
            // Splash, Bubbles and MumboPuff -- so on the names it is ADDITIVE, which is the
            // opposite of what I shipped in 951e184 (that drew Smoke additive and Fire alpha).
            // A name is not a read; see findings/particles.md.
            BlendMode = t.AdditiveBit ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
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
