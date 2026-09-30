using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>Ordinary PS2 mesh lighting, computed before interpolation and texture modulation.
/// Repeat UVs, 16/255 cutout, mip filtering and depth-prepass soft alpha retain the viewer's
/// existing material policy. Lighting does not depend on Godot's fragment normal/side check.
/// UV and colour interpolation still use Godot's perspective correction; this is not a GS rasterizer.</summary>
public static class Ps2Materials
{
    // Used only by standalone callers that have no disc. The viewer always configures from ELF.
    public static readonly Lighting UnconfiguredNeutral = new(new(1), new(0), new(0, -1, 0), new(0), new(0), 0);
    public static Lighting Lighting { get; set; } = UnconfiguredNeutral;

    /// <summary>⭐ BILINEAR FILTERING, ON BY DEFAULT -- master's call, and the console's.
    /// The GS filters per texture (TEX1's MMAG/MMIN), and the ground and the water were already
    /// sampled linear here while every model was sampled nearest, so a ride read crunchier than
    /// the terrain it stood on. This is one switch over all of it, so turning it off gives a
    /// genuinely global nearest look rather than half a scene.
    ///
    /// ⚠ SETTING IT ALONE CHANGES NOTHING ALREADY BUILT -- a ShaderMaterial holds its Shader
    /// and the filter is baked into the shader's sampler hint. Call <see cref="Refilter"/>.</summary>
    public static bool Bilinear { get; set; } = true;
    public const float DefaultWeatherAmount = 0; // no weather simulation connected yet
    static readonly Dictionary<string, Shader> Shaders = new();

    /// <param name="clamp">⭐ For a surface whose UVs map a texture ONCE, 0 to 1, with no tiling
    /// inside the quad -- the park's ground tiles. With `repeat_enable` a linear sample at the very
    /// edge of such a quad wraps and blends in the OPPOSITE edge of the tile, which on a path tile
    /// means the grass baked down its sides bleeding into the dirt where two tiles meet. Repeat
    /// buys such a surface nothing and costs it that seam.</param>
    public static Shader Shader(bool soft, string cull, bool rawNormals = true, bool linearFilter = false,
                                bool clamp = false, bool animated = false)
    {
        string key = $"{soft}/{cull}/{rawNormals}/{linearFilter}/{clamp}/{animated}";
        EnsureClock();
        if (Shaders.TryGetValue(key, out var found)) return found;
        return Shaders[key] = new Shader { Code = $$"""
shader_type spatial;
render_mode unshaded, {{cull}}{{(soft ? ", depth_prepass_alpha" : "")}};

// Deliberately no source_color: GS modulates texture bytes, not linear-light RGB.
uniform sampler2D albedo_tex : {{(linearFilter ? "filter_linear_mipmap" : "filter_nearest_mipmap")}}, {{(clamp ? "repeat_disable" : "repeat_enable")}};
uniform bool has_tex = true;
// ⭐ A texture the disc does not have draws UNTEXTURED on the console, not missing: the material
// loader keeps the null handle (0x22762c stores it whatever 0x2351a8 returned) and the model
// renderer still submits the strip (0x227f88 -> 0x22a068), leaving out the texture's flag word and
// bit 0x10 -- which it sets on every textured draw and which is where the GS PRIM register keeps
// TME (INFERRED from that layout; 0x40, tested right after, is PRIM's ABE). With TME off the GS
// writes the lit vertex colour as it is, and under this shader's modulate that is a texel of 128.
// Was 0.72, a viewer default with no console behind it. wr_flap.ssh (the water ride's ramp flaps)
// is the case that showed it: no file of that stem is anywhere on the disc.
uniform vec3 fallback_colour = vec3(0.5019608); // 128 / 255: a uniform default must be a literal
uniform float cutout = 0.0627;
uniform vec3 ps2_ambient;
uniform vec3 ps2_directional;
uniform vec3 ps2_ray;
varying vec3 ps2_colour;
{{(animated ? """
// ⭐⭐ MOVING TEXTURES. The engine's own name for this is SCROLLING TEXTURES: an attraction can
// switch it off with `TurnOffScrollingTextures` (0x2e7ccc), the sibling of the frame-replacement
// player's `TurnOffAnimatingTextures` (0x2e7c90), and a texture carries an `fScrollRate`
// (0x2acf98) beside its filename. See TextureMotion for the whole reading.
// `uv_scroll` translates in UV a second; `uv_spin` turns about the texture's MIDDLE, which is
// where a centred spiral like the coconut's drink has to turn. `water_wave` lifts the surface on
// a travelling sine -- the sea alone, which the terrain names A_SEA_01..06 itself.
// ⚠ The scroll's DIRECTION, the spin's rate and the wave's size and speed are chosen, not read:
// no per-texture rate could be recovered from this disc (TextureMotion says why).
uniform float uv_time = 0.0;
uniform vec2 uv_scroll = vec2(0.0, 0.0);
uniform float uv_spin = 0.0;           // radians a second, about uv_spin_center
uniform vec2 uv_spin_center = vec2(0.5, 0.5);
uniform vec3 water_wave = vec3(0.0);   // amplitude, wavelength, speed
""" : "")}}

void vertex() {
    // EE transforms the world ray into the mesh basis and normalises the ray, not N.
    // ps2_ray already has the game's Z reflected into Godot world space.
    vec3 local_ray = transpose(mat3(MODEL_MATRIX)) * ps2_ray;
    float ray_length = length(local_ray);
    local_ray = ray_length > 0.0 ? local_ray / ray_length : vec3(0.0);
    vec3 raw_normal = {{(rawNormals ? "CUSTOM0.xyz" : "NORMAL * 127.0")}};
    float n_dot_l = max(dot(raw_normal, -local_ray), 0.0);
    ps2_colour = floor(min(vec3(255.0), ps2_ambient * 128.0 + ps2_directional * n_dot_l));
{{(animated ? """
    if (water_wave.x > 0.0 && water_wave.y > 0.0) {
        vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        float phase = (w.x + w.z) / water_wave.y + uv_time * water_wave.z;
        VERTEX.y += water_wave.x * sin(phase);
    }
""" : "")}}
}

{{OutputColour}}

void fragment() {
    vec2 uv = UV;
{{(animated ? """
    // ⚠ SPIN FIRST, THEN SCROLL. Rotating an already-scrolled UV turns the scroll's direction
    // with it, which would make a twisting surface also wander off in a circle.
    if (uv_spin != 0.0) {
        // ⚠ ABOUT THE PATCH'S OWN CENTRE. A texture patch need not start at UV zero -- the
        // Coconut's drink lives at U 1..2 -- so a hardwired (0.5, 0.5) pivots outside it.
        float a = uv_spin * uv_time;
        vec2 c = uv - uv_spin_center;
        uv = vec2(c.x * cos(a) - c.y * sin(a), c.x * sin(a) + c.y * cos(a)) + uv_spin_center;
    }
    uv += uv_scroll * uv_time;
""" : "")}}
    vec4 c = has_tex ? texture(albedo_tex, uv) : vec4(fallback_colour, 1.0);
    if (c.a < cutout) discard;
    vec3 encoded = floor(clamp(c.rgb * 255.0 * ps2_colour / 128.0, vec3(0.0), vec3(255.0))) / 255.0;
    ALBEDO = output_colour(encoded);
    {{(soft ? "ALPHA = c.a;" : "")}}
}
""" };
    }

    /// <summary>The GS's framebuffer bytes through Godot's output: shared by every shader here, so a model and a
    /// particle that the console draws with the same bytes come out with the same pixels.</summary>
    const string OutputColour = """
vec3 output_colour(vec3 encoded) {
    // Compatibility 4.6.2 converts ALBEDO with a cubic, then outputs with a pure power
    // (drivers/gles3/shaders/tonemap_inc.glsl). Invert that cubic, not a fitted gain.
    // Passing encoded straight through turns byte 20 into 16. The render audit catches it.
    if (OUTPUT_IS_SRGB) {
        vec3 target = pow((encoded + vec3(0.055)) / 1.055, vec3(2.4));
        vec3 x = max(encoded, vec3(0.05));
        for (int i = 0; i < 6; i++) {
            vec3 value = x * (x * (x * 0.305306011 + 0.682171111) + 0.012522878);
            vec3 derivative = x * (x * 0.915918033 + 1.364342222) + 0.012522878;
            x -= (value - target) / derivative;
        }
        return x;
    }
    return mix(pow((encoded + vec3(0.055)) / 1.055, vec3(2.4)),
               encoded / 12.92, lessThanEqual(encoded, vec3(0.04045)));
}
""";

    static Shader _particle;
    /// <summary>⭐⭐ A PARTICLE, MODULATED THE WAY THE GS DOES IT (strawberry, 2026-09-29: the prank stink is
    /// "gray in ours"). Particles reach the same renderer as models -- `0x220878` hands each one to `0x233458`,
    /// which submits it through `0x22a068` with its ramp colour as the prim colour -- so they get the same
    /// modulate the model shader above already does: texel byte x colour byte / 128, where 0x80 is 1.0.
    ///
    /// ⚠ StandardMaterial3D did neither half of that. It linearised the texture (source_color) and multiplied
    /// by the raw vertex colour, so a ramp step of 0xD7 scaled a dark texel by 0.84 in LINEAR light, and the
    /// colour curve all but vanished into the sprite's grey: YellowStink's centre came out (79, 85, 0) where the
    /// console writes (144, 168, 0). Every ramp in the library was drawn at roughly half its strength.
    ///
    /// ⭐ Alpha needs no x2: the SSH decoder already doubled the texture's 0..0x80 alpha to 0..255, so
    /// `tex.a * ramp.a` is the GS's `At * Af / 128` already.
    ///
    /// The vertex half is StandardMaterial3D's own BILLBOARD_PARTICLES + keep-scale + particle animation code
    /// (Godot 4.6 scene/resources/material.cpp), so nothing about where or how big a particle is changes.</summary>
    public static Shader ParticleShader => _particle ??= new Shader { Code = $$"""
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_opaque;

// Deliberately no source_color: the GS modulates texture bytes.
uniform sampler2D albedo_tex : filter_linear_mipmap, repeat_enable;
uniform int frames_h = 1;
// ⚠ An UNTEXTURED effect (Sparks and ten others) draws with TME off, where the GS writes the colour as it is --
// a texel of 128 under this modulate, as in the model shader. The dot it is given is only its shape.
uniform bool textured = true;

void vertex() {
    mat4 mat_world = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]),
                          normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
    mat_world = mat_world * mat4(vec4(cos(INSTANCE_CUSTOM.x), -sin(INSTANCE_CUSTOM.x), 0.0, 0.0),
                                 vec4(sin(INSTANCE_CUSTOM.x), cos(INSTANCE_CUSTOM.x), 0.0, 0.0),
                                 vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
    MODELVIEW_MATRIX = VIEW_MATRIX * mat_world;
    MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
                                               vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0),
                                               vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0),
                                               vec4(0.0, 0.0, 0.0, 1.0));
    MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
    // One pass through the strip over the particle's life, no loop (RideParticles sets AnimSpeed 1).
    float total = float(max(frames_h, 1));
    float frame = clamp(floor(INSTANCE_CUSTOM.z * total), 0.0, total - 1.0);
    UV /= vec2(total, 1.0);
    UV += vec2(frame / total, 0.0);
}

{{OutputColour}}

void fragment() {
    vec4 t = texture(albedo_tex, UV);
    vec3 texel = textured ? t.rgb * 255.0 : vec3(128.0);
    vec3 encoded = floor(clamp(texel * (COLOR.rgb * 255.0) / 128.0, vec3(0.0), vec3(255.0))) / 255.0;
    ALBEDO = output_colour(encoded);
    ALPHA = t.a * COLOR.a;
}
""" };

    public static void BindLight(ShaderMaterial material)
    {
        var light = Lighting.AtWeather(DefaultWeatherAmount);
        material.SetShaderParameter("ps2_ambient", V(light.Ambient));
        material.SetShaderParameter("ps2_directional", V(light.Directional));
        var ray = light.RayDirection;
        material.SetShaderParameter("ps2_ray", new Vector3(ray.X, ray.Y, -ray.Z));
    }

    public static ShaderMaterial Ground(ImageTexture texture, Color? fallback = null)
    {
        var material = new ShaderMaterial
        { Shader = Shader(false, "cull_back", rawNormals: false, linearFilter: Bilinear, clamp: true) };
        material.SetShaderParameter("albedo_tex", texture);
        material.SetShaderParameter("has_tex", texture != null);
        if (fallback is { } colour) material.SetShaderParameter("fallback_colour", new Vector3(colour.R, colour.G, colour.B));
        BindLight(material);
        return material;
    }

    /// <summary>A water surface: the ordinary ground material plus a scroll and, for the sea, a
    /// travelling sine. ⚠ It repeats rather than clamps -- a scrolled UV leaves 0..1 by design,
    /// which is the one place on the ground where repeat is the right answer.</summary>
    /// <param name="soft">⚠ CARRY THE TRANSLUCENCY THROUGH. `wr_water3` is partly clear on 4031
    /// of its 4096 texels and `jri_sur1` the same -- a river is SEE-THROUGH. Building its moving
    /// material without that flag swapped a translucent surface for an opaque one, which is
    /// exactly what master saw: "river also lost its transparency".</param>
    public static ShaderMaterial Water(ImageTexture texture, bool soft, Vector2 scroll, Vector3 wave)
        => Animated(texture, soft, "cull_back", scroll, 0f, wave);

    /// <summary>A surface whose TEXTURE moves: a scroll, a twist, or the sea's sine.
    ///
    /// ⭐⭐ ONE CLOCK FOR ALL OF THEM. The river is terrain and the coconut's drink is a placed
    /// shop, built by different code and ticked from different places; every material made here
    /// is registered so that one assignment to <see cref="TextureTime"/> reaches all of them and
    /// they stay in phase, with no caller left to forget one.
    ///
    /// ⚠⚠ THIS WAS A `global uniform` FOR ABOUT AN HOUR AND THAT VERSION DID NOT WORK. A global
    /// shader parameter added at runtime read back 0 immediately after being set, which would
    /// have frozen every moving texture including the river that already worked -- silently, with
    /// no error. Only the running-engine audit caught it. Per-material uniforms are what the
    /// water always used; this is that path widened, not a new one taken on trust.</summary>
    /// <param name="soft">⚠ CARRY THE TRANSLUCENCY THROUGH. `wr_water3` is partly clear on 4031
    /// of its 4096 texels -- a river is SEE-THROUGH -- and building its moving material without
    /// that flag is what master saw as "river also lost its transparency".</param>
    public static ShaderMaterial Animated(ImageTexture texture, bool soft, string cull,
                                          Vector2 scroll, float spin, Vector3 wave)
    {
        var material = new ShaderMaterial
        { Shader = Shader(soft, cull, rawNormals: false, linearFilter: Bilinear, animated: true) };
        material.SetShaderParameter("albedo_tex", texture);
        material.SetShaderParameter("has_tex", texture != null);
        material.SetShaderParameter("uv_scroll", scroll);
        material.SetShaderParameter("uv_spin", spin);
        material.SetShaderParameter("uv_spin_center", new Vector2(0.5f, 0.5f));
        material.SetShaderParameter("water_wave", wave);
        material.SetShaderParameter(UvTime, _textureTime);
        BindLight(material);
        Moving.Add(new WeakReference<ShaderMaterial>(material));
        return material;
    }

    /// <summary>Every moving-texture material alive, held WEAKLY and pruned as it is used --
    /// a park reload throws its materials away, and a strong list here would keep the materials
    /// of every park ever loaded alive behind it.</summary>
    static readonly System.Collections.Generic.List<WeakReference<ShaderMaterial>> Moving = new();

    /// <summary>Kept so callers that forced the old global's registration still compile. Now a
    /// no-op: there is nothing global left to register, which is the whole repair.</summary>
    public static void EnsureClock() { }

    /// <summary>⚠⚠ CACHED, BECAUSE A STRING LITERAL HERE IS AN ALLOCATION. `SetShaderParameter`
    /// takes a `StringName`, and Godot 4's C# binding has an implicit conversion from `string` that
    /// CONSTRUCTS A NEW StringName every call -- a managed object with a finalizer, plus its native
    /// side. The setter below runs over EVERY moving material EVERY FRAME, so the literal was
    /// minting one throwaway StringName per material per frame and posting each to the finalizer
    /// queue. One static instance is the same call with none of that.
    ///
    /// ⭐ The trap is general: any SetShaderParameter, Set or Call with a string literal on a
    /// per-frame path is doing this. The SETUP paths are left alone -- they run once and the
    /// clarity is worth more there than the bytes.</summary>
    static readonly StringName UvTime = "uv_time";

    static float _textureTime;

    /// <summary>Seconds fed to every moving texture. ⭐ Advanced from the console's clock in
    /// SECONDS, because a scroll and a sine are continuous -- there is nothing to quantise.</summary>
    public static float TextureTime
    {
        get => _textureTime;
        set
        {
            _textureTime = value;
            for (int i = Moving.Count - 1; i >= 0; i--)
            {
                if (Moving[i].TryGetTarget(out var m) && GodotObject.IsInstanceValid(m))
                    m.SetShaderParameter(UvTime, value);
                else Moving.RemoveAt(i);
            }
        }
    }

    /// <summary>Enrol a material built elsewhere into the texture clock. ⚠ AnimatedModel builds
    /// its own ShaderMaterial per surface rather than calling <see cref="Animated"/>, so without
    /// this a shop's drink would carry a perfectly good `uv_spin` and never be told the time.</summary>
    public static void Register(ShaderMaterial material)
    {
        if (material == null) return;
        material.SetShaderParameter(UvTime, _textureTime);
        Moving.Add(new WeakReference<ShaderMaterial>(material));
    }

    /// <summary>How many moving-texture materials are alive. ⭐ An instrument: water that is not
    /// moving can be asked whether it has no moving materials or a stopped clock.</summary>
    public static int MovingMaterials => Moving.Count;

    /// <summary>Move every material already standing in a tree onto the current
    /// <see cref="Bilinear"/> setting, and answer how many moved.
    ///
    /// ⭐ THE CACHE IS THE LOOKUP. Every shader here is keyed by the flags it was built from,
    /// so the variant a live material is ON can be read straight back out of that key -- flip the
    /// one field and ask for the matching shader. No registry of materials to keep in step, and
    /// nothing to leak when a park is thrown away and rebuilt.</summary>
    public static int Refilter(Node root)
    {
        if (root == null) return 0;
        var byShader = new Dictionary<Shader, string>();
        foreach (var kv in Shaders) byShader[kv.Value] = kv.Key;
        int changed = 0;

        bool Swap(Material m)
        {
            if (m is not ShaderMaterial sm || sm.Shader == null) return false;
            if (!byShader.TryGetValue(sm.Shader, out var key)) return false;
            var f = key.Split('/');
            if (f.Length != 6) return false;
            var want = Shader(bool.Parse(f[0]), f[1], bool.Parse(f[2]), Bilinear, bool.Parse(f[4]), bool.Parse(f[5]));
            if (want == sm.Shader) return false;
            sm.Shader = want;
            return true;
        }

        void Walk(Node n)
        {
            if (n is MeshInstance3D mi)
            {
                if (Swap(mi.MaterialOverride)) changed++;
                for (int i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++)
                    if (Swap(mi.GetSurfaceOverrideMaterial(i))) changed++;
                // ⚠ And the materials on the MESH itself, which a MaterialOverride hides but
                // does not replace -- the ground sets them that way.
                if (mi.Mesh != null)
                    for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                        if (Swap(mi.Mesh.SurfaceGetMaterial(i))) changed++;
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }

        Walk(root);
        return changed;
    }

    static Vector3 V(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);
}
