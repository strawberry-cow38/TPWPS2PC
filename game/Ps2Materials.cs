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
uniform vec3 fallback_colour = vec3(0.72); // named viewer default for missing textures
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
uniform float uv_spin = 0.0;           // radians a second, about (0.5, 0.5)
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

void fragment() {
    vec2 uv = UV;
{{(animated ? """
    // ⚠ SPIN FIRST, THEN SCROLL. Rotating an already-scrolled UV turns the scroll's direction
    // with it, which would make a twisting surface also wander off in a circle.
    if (uv_spin != 0.0) {
        float a = uv_spin * uv_time;
        vec2 c = uv - vec2(0.5);
        uv = vec2(c.x * cos(a) - c.y * sin(a), c.x * sin(a) + c.y * cos(a)) + vec2(0.5);
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
        material.SetShaderParameter("water_wave", wave);
        material.SetShaderParameter("uv_time", _textureTime);
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
                    m.SetShaderParameter("uv_time", value);
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
        material.SetShaderParameter("uv_time", _textureTime);
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
