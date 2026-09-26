using Godot;
using System;
using System.Linq;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The park's sky, from the two textures each world ships in its own `/Sky/` folder.
///
/// ⭐ The game loads them per world in `0x149958`, which calls
/// `FUN_00231908(skyObject, "Data/&lt;World&gt;/Sky/", "&lt;World&gt;_back.tga", "&lt;World&gt;_front2.tga")`
/// with a different triple for each of the four. So the sky is a BACK layer and a FRONT layer,
/// named that way by the engine, and the names are what this looks for.
///
/// ⭐⭐ `&lt;World&gt;_back.tga` IS A POLAR PROJECTION, not a panorama. Jungle's is 256x256, fully
/// opaque, and a radial gradient: dark blue in the MIDDLE going cyan at the EDGE. That is the
/// sky seen from below with the centre of the image straight up -- which is why it is square and
/// why sampling it as a panorama would produce a smear. `&lt;World&gt;_front2.tga` is the same size and
/// is almost entirely partial alpha (Jungle: 45,481 partly clear of 45,481 that are not clear at
/// all), so it is the cloud sheet that composites over it.
///
/// ⚠ NOT DONE: how fast the clouds move. Nothing here animates.
///
/// ⚠⚠ CORRECTION to what this comment first said. The call the loader makes right after the
/// sky, `FUN_00220fa0(x, 2.2, 5.875, flag)` with a per-world x of 23.125 / 37.125 / 31.125 /
/// 29.125, is NOT a cloud routine and those are not cloud parameters. It loads
/// `data/generic/weather/raindrop.ssh` and `snowflake.ssh`, `data/generic/extra/justwater.ssh`
/// and `logo.ssh` (or `logoam.ssh`), and lays them over a quad running x-6.125..x+18.875 and
/// z-20.875..z+0.525 -- 25 by 21 units reaching back to z = -15.0, which is the ticket booths.
/// It is the weather-and-extras setup at the park entrance, and the floats are a POSITION.
/// I called it clouds because it sat next to the sky in the loader, which is not a reason.</summary>
public static class SkyDome
{
    /// <summary>`shader_type sky` so Godot draws it behind everything at no depth cost.</summary>
    const string Code = @"
shader_type sky;

uniform sampler2D back : source_color, filter_linear;
uniform sampler2D front : source_color, filter_linear;
uniform sampler2D cloud : source_color, filter_linear, repeat_enable;
uniform bool has_front = false;
uniform bool has_cloud = false;
// ⭐ The console scrolls TWO cloud layers, the near one at DOUBLE the far one's rate
// (0x232328). Both are UV offsets in 0..1, wrapped by the caller.
uniform vec2 drift_far = vec2(0.0);
uniform vec2 drift_near = vec2(0.0);
// ⭐⭐ The eased weather factor `f = 2t - t*t` that 0x23efe8 hands to BOTH the lighting and the
// sky. See `Grey` for why a shader multiply reproduces the console's palette rewrite exactly
// here rather than merely approximately.
uniform float grey = 0.0;

// ⭐ POLAR, not equirectangular. The texture's centre is straight up and its edge is the horizon,
// so the lookup is the direction's horizontal part scaled by how far down from the zenith it is.
// ⚠ Below the horizon there is no texture at all -- the game never points the camera there (it
// looks down at 39-82 degrees) -- so the horizon ring is held rather than mirrored, which would
// put a second sky under the island.
vec2 polar(vec3 dir, vec2 slide) {
    float down = acos(clamp(dir.y, -1.0, 1.0)) / 1.5707963;   // 0 at the zenith, 1 at the horizon
    vec2 h = dir.xz;
    float l = length(h);
    if (l < 1e-5) return vec2(0.5);
    h /= l;
    return vec2(0.5) + h * min(down, 1.0) * 0.5 + slide;
}

void sky() {
    float down = acos(clamp(EYEDIR.y, -1.0, 1.0)) / 1.5707963;
    // ⚠ The BACKDROP DOES NOT SCROLL. It is a radial gradient, not a sheet -- sliding it would
    // walk the bright spot across the sky. Only the two cloud layers move.
    vec3 c = texture(back, polar(EYEDIR, vec2(0.0))).rgb;
    // ⚠ A POLAR MAP DEGENERATES AT ITS EDGE. Every direction near the horizon lands on the same
    // ring of texels, so the clouds smear into vertical streaks exactly where the park camera
    // spends its time. They are faded out over the last quarter instead: overhead the sheet reads
    // as clouds, at the horizon the backdrop's own colour carries it.
    float fade = smoothstep(1.0, 0.72, down);
    if (has_front) {
        vec4 f = texture(front, polar(EYEDIR, drift_far));
        c = mix(c, f.rgb, f.a * fade);
    }
    if (has_cloud) {
        vec4 k = texture(cloud, polar(EYEDIR, drift_near));
        c = mix(c, k.rgb, k.a * fade);
    }
    // ⭐⭐ THE WEATHER GREY. 0x232700 walks the sky's 256-entry palette and multiplies each RGB by
    // `(1 - f) + f * (a * 0.2 + 0.3)`. Both paletted sky textures ship with EVERY palette entry at
    // alpha 128 -- PS2 opaque -- so `a` is 1.0 throughout and the whole expression collapses to
    // `1 - 0.5f`, the same number for every texel. A per-pixel multiply is therefore not an
    // approximation of the palette rewrite here; it is the identical operation.
    c *= 1.0 - 0.5 * grey;
    COLOR = c;
}
";

    static Shader _shader;
    static Shader Shader => _shader ??= new Shader { Code = Code };

    /// <summary>Find `/Sky/&lt;anything&gt;_back.tga` and `_front2.tga` in the open archive and build
    /// the sky. Returns null when the archive has no `/Sky/` folder, which is not an error --
    /// only the four park worlds carry one.</summary>
    /// <summary>⭐ The scroll rate, straight out of `0x23efe8`: a UNIT wind direction times
    /// **0.01 UV per second**, with the near layer at double it (`0x232328`). ⚠ The heading turns
    /// over time on the console and the rate it turns at is NOT read, so the port holds it.</summary>
    public const float DriftPerSecond = 0.01f;

    /// <summary>The eased weather curve `f = 2t - t*t` that `0x23efe8` computes once and hands to
    /// both the lighting and the sky, where `t = 0.8 * amount`.</summary>
    public static float Eased(float amount)
    {
        float t = 0.8f * Mathf.Clamp(amount, 0f, 1f);
        return 2f * t - t * t;
    }

    /// <summary>Advance the two cloud layers and set the weather grey. ⚠ Wrapped into 0..1 here,
    /// as `0x232328` does, so the offsets never grow until float precision eats the detail.</summary>
    public static void Step(ShaderMaterial mat, ref Vector2 drift, double delta, float amount,
                            float headingRadians)
    {
        if (mat == null) return;
        var wind = new Vector2(-Mathf.Cos(headingRadians), Mathf.Sin(headingRadians));
        drift += wind * DriftPerSecond * (float)delta;
        drift = new Vector2(Mathf.PosMod(drift.X, 1f), Mathf.PosMod(drift.Y, 1f));
        mat.SetShaderParameter("drift_far", drift);
        mat.SetShaderParameter("drift_near", drift * 2f);
        mat.SetShaderParameter("grey", Eased(amount));
    }

    public static Sky Build(AssetLibrary lib, out string report)
    {
        report = "no /Sky/ in this archive";
        var images = lib.Images(includeSsh: false);
        WadArchive.Entry Pick(string suffix) => images.FirstOrDefault(
            e => e.Path.Contains("/Sky/", StringComparison.OrdinalIgnoreCase)
              && e.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        var backEntry = Pick("_back.tga");
        if (backEntry == null) return null;
        var frontEntry = Pick("_front2.tga");
        // ⭐⭐ THE THIRD TEXTURE, WHICH THIS PORT NEVER LOADED. Every world ships `*_cloud.tga`
        // beside the other two and it is the ONLY one with real alpha: `back` and `front2` are
        // 256x256 PALETTED (ssh type 2) with every palette entry at alpha 128, while the cloud
        // sheet is 64x64 RGB+ALPHA (type 5) averaging alpha 39. It is the sheet you can see
        // through, so it is the near layer the console scrolls at double rate.
        var cloudEntry = Pick("_cloud.tga");

        ImageTexture Load(WadArchive.Entry e)
        {
            if (e == null) return null;
            var t = new Targa(lib.Read(e));
            var img = Image.CreateFromData(t.Width, t.Height, false, Image.Format.Rgba8, t.Pixels);
            return ImageTexture.CreateFromImage(img);
        }

        var mat = new ShaderMaterial { Shader = Shader };
        var back = Load(backEntry);
        mat.SetShaderParameter("back", back);
        var front = Load(frontEntry);
        mat.SetShaderParameter("front", front);
        mat.SetShaderParameter("has_front", front != null);
        var cloud = Load(cloudEntry);
        mat.SetShaderParameter("cloud", cloud);
        mat.SetShaderParameter("has_cloud", cloud != null);
        report = $"{backEntry.Path} {back.GetWidth()}x{back.GetHeight()}"
               + (frontEntry != null ? $" + {frontEntry.Path}" : "  (no front layer)")
               + (cloudEntry != null ? $" + {cloudEntry.Path} {cloud.GetWidth()}x{cloud.GetHeight()}"
                                     : "  (no cloud sheet)");
        return new Sky { SkyMaterial = mat };
    }
}
