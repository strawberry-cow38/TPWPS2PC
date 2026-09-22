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
    public const float DefaultWeatherAmount = 0; // no weather simulation connected yet
    static readonly Dictionary<string, Shader> Shaders = new();

    public static Shader Shader(bool soft, string cull, bool rawNormals = true, bool linearFilter = false)
    {
        string key = $"{soft}/{cull}/{rawNormals}/{linearFilter}";
        if (Shaders.TryGetValue(key, out var found)) return found;
        return Shaders[key] = new Shader { Code = $$"""
shader_type spatial;
render_mode unshaded, {{cull}}{{(soft ? ", depth_prepass_alpha" : "")}};

// Deliberately no source_color: GS modulates texture bytes, not linear-light RGB.
uniform sampler2D albedo_tex : {{(linearFilter ? "filter_linear_mipmap" : "filter_nearest_mipmap")}}, repeat_enable;
uniform bool has_tex = true;
uniform vec3 fallback_colour = vec3(0.72); // named viewer default for missing textures
uniform float cutout = 0.0627;
uniform vec3 ps2_ambient;
uniform vec3 ps2_directional;
uniform vec3 ps2_ray;
varying vec3 ps2_colour;

void vertex() {
    // EE transforms the world ray into the mesh basis and normalises the ray, not N.
    // ps2_ray already has the game's Z reflected into Godot world space.
    vec3 local_ray = transpose(mat3(MODEL_MATRIX)) * ps2_ray;
    float ray_length = length(local_ray);
    local_ray = ray_length > 0.0 ? local_ray / ray_length : vec3(0.0);
    vec3 raw_normal = {{(rawNormals ? "CUSTOM0.xyz" : "NORMAL * 127.0")}};
    float n_dot_l = max(dot(raw_normal, -local_ray), 0.0);
    ps2_colour = floor(min(vec3(255.0), ps2_ambient * 128.0 + ps2_directional * n_dot_l));
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
    vec4 c = has_tex ? texture(albedo_tex, UV) : vec4(fallback_colour, 1.0);
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
        var material = new ShaderMaterial { Shader = Shader(false, "cull_back", rawNormals: false, linearFilter: true) };
        material.SetShaderParameter("albedo_tex", texture);
        material.SetShaderParameter("has_tex", texture != null);
        if (fallback is { } colour) material.SetShaderParameter("fallback_colour", new Vector3(colour.R, colour.G, colour.B));
        BindLight(material);
        return material;
    }

    static Vector3 V(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);
}
