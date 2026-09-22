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
uniform bool has_front = false;
uniform float spin = 0.0;

// ⭐ POLAR, not equirectangular. The texture's centre is straight up and its edge is the horizon,
// so the lookup is the direction's horizontal part scaled by how far down from the zenith it is.
// ⚠ Below the horizon there is no texture at all -- the game never points the camera there (it
// looks down at 39-82 degrees) -- so the horizon ring is held rather than mirrored, which would
// put a second sky under the island.
vec2 polar(vec3 dir, float turn) {
    float down = acos(clamp(dir.y, -1.0, 1.0)) / 1.5707963;   // 0 at the zenith, 1 at the horizon
    vec2 h = dir.xz;
    float l = length(h);
    if (l < 1e-5) return vec2(0.5);
    h /= l;
    h = vec2(h.x * cos(turn) - h.y * sin(turn), h.x * sin(turn) + h.y * cos(turn));
    return vec2(0.5) + h * min(down, 1.0) * 0.5;
}

void sky() {
    float down = acos(clamp(EYEDIR.y, -1.0, 1.0)) / 1.5707963;
    vec3 c = texture(back, polar(EYEDIR, 0.0)).rgb;
    if (has_front) {
        vec4 f = texture(front, polar(EYEDIR, spin));
        // ⚠ A POLAR MAP DEGENERATES AT ITS EDGE. Every direction near the horizon lands on the
        // same ring of texels, so the clouds smear into vertical streaks exactly where the park
        // camera spends its time. They are faded out over the last quarter instead: overhead the
        // sheet reads as clouds, at the horizon the backdrop's own colour carries it.
        c = mix(c, f.rgb, f.a * smoothstep(1.0, 0.72, down));
    }
    COLOR = c;
}
";

    static Shader _shader;
    static Shader Shader => _shader ??= new Shader { Code = Code };

    /// <summary>Find `/Sky/&lt;anything&gt;_back.tga` and `_front2.tga` in the open archive and build
    /// the sky. Returns null when the archive has no `/Sky/` folder, which is not an error --
    /// only the four park worlds carry one.</summary>
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
        report = $"{backEntry.Path} {back.GetWidth()}x{back.GetHeight()}"
               + (frontEntry != null ? $" + {frontEntry.Path}" : "  (no front layer)");
        return new Sky { SkyMaterial = mat };
    }
}
