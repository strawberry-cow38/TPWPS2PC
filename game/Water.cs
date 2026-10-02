using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>Materials on the terrain's authored water meshes. The opaque A_SEA meshes
/// have no supplied morph/UV track and remain static here. This says nothing about the
/// separate, EE-generated moving park-water layer: see ProceduralParkWaterView and
/// findings/ocean-waves.md. River UV scrolling below remains the pre-existing approximation.
/// Native procedural water owns neither these materials nor their shared texture clock.</summary>
public sealed class Water
{
    /// <summary>One texel of a 64-high texture per console frame.</summary>
    public const float ScrollTexelsPerTick = 1f;
    public const float AssumedTextureHeight = 64f;

    /// <summary>Which way the RIVER's scroll runs, in degrees, where 0 is straight down the V axis.
    /// ⚠ Somebody's EYES, not a reading. The row roll settles the SPEED -- one texel a console
    /// frame, measured on the PSX -- and nothing found so far states the direction.
    /// ⭐ The river's real motion is eleven authored UV tracks in `terrain_1.aps`; this constant
    /// stands in for them until they are replayed, which is the next real improvement here.</summary>
    const float RiverScrollDegrees = 90f;

    /// <summary>The opaque terrain sea base's scroll, texels per console frame. ZERO: this layer
    /// has no supplied animation; the separate native drawable is handled by ProceduralParkWaterView. Left as a switch rather than deleted so the PSX's one-row-per-frame
    /// roll can be put back by someone who can see a console, without another guess in the source.</summary>
    const float SeaScrollTexelsPerTick = 0f;

    readonly List<ShaderMaterial> _moving = new();
    public int Surfaces => _moving.Count;
    public string Report { get; private set; } = "no water";

    /// <summary>⭐⭐ SHARED WITH THE MODELS, and it now includes `dk_water`. The stem list lives in
    /// <see cref="TextureMotion.WaterStems"/> so the terrain and a placed shop cannot disagree
    /// about what water is -- they did, and the shadowed water under the jungle bridge is what
    /// fell through the gap. See that type for the correlation measurement.</summary>
    static string[] WaterTextures => TextureMotion.WaterStems;

    /// <summary>Replace the water surfaces' materials with moving ones. ⚠ Takes the surfaces as
    /// the model built them, so a surface this does not recognise keeps exactly what it had.</summary>
    readonly Func<string, bool> _soft;

    public Water(Node3D terrain, Model model, Func<string, bool> soft = null)
    {
        _soft = soft;
        if (terrain == null || model == null) return;
        int sea = 0, flow = 0;
        // ⚠ The first pass over the surfaces is gone with the wave it served: it existed only to
        // find the highest sea plane, and there is no highest -- the sea is ONE surface sitting at
        // two heights over ground that does not overlap, so "the top plane" never named anything.
        foreach (var mi in terrain.GetChildren().OfType<MeshInstance3D>())
        {
            // The surfaces are named "<mesh>#<material>" by AnimatedModel.
            string mesh = mi.Name.ToString().Split('#')[0];
            if (mi.MaterialOverride is not ShaderMaterial had) continue;
            if (had.GetShaderParameter("albedo_tex").AsGodotObject() is not ImageTexture tex) continue;

            bool isSea = mesh.StartsWith("A_SEA", StringComparison.OrdinalIgnoreCase);
            // ⚠ THE MATERIAL'S OWN NAME, off the model, by the index in the surface's name. Asking
            // the bound ImageTexture for a path gives an EMPTY string -- it was built in memory and
            // has none -- so a texture-name test against it would have matched nothing at all and
            // looked exactly like "this terrain has no river".
            string material = MaterialOf(model, mi.Name.ToString());
            if (!isSea && !WaterTextures.Any(w => material.Contains(w, StringComparison.OrdinalIgnoreCase)))
                continue;

            float riverPerSecond = Env("TPW_WATER_SCROLL",
                ScrollTexelsPerTick * ConsoleClock.TicksPerSecond / AssumedTextureHeight);
            float seaPerSecond = Env("TPW_SEA_SCROLL",
                SeaScrollTexelsPerTick * ConsoleClock.TicksPerSecond / AssumedTextureHeight);

            Vector2 scroll;
            if (isSea)
            {
                // ⭐⭐⭐ ZERO unless somebody switches it on. Not "a scroll at angle 0" -- an angle
                // with no speed still reads as a decision about direction, and there is no
                // direction to decide: the console gives this surface no motion channel at all.
                float radians = Mathf.DegToRad(Env("TPW_SEA_ANGLE", 0f));
                scroll = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * seaPerSecond;
            }
            else
            {
                float radians = Mathf.DegToRad(Env("TPW_WATER_ANGLE", RiverScrollDegrees));
                scroll = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * riverPerSecond;
            }
            // No vertex motion on these authored terrain meshes. Native procedural water is a
            // separate drawable above them, not an invented displacement of this base layer.
            var wave = Vector3.Zero;
            var made = Ps2Materials.Water(tex, _soft?.Invoke(material) ?? false, scroll, wave);
            mi.MaterialOverride = made;
            _moving.Add(made);
            if (isSea) sea++; else flow++;
        }
        Report = _moving.Count == 0
            ? "no water surfaces in this terrain"
            : $"{sea} opaque sea-base surfaces STATIC (no scroll, no vertex motion),"
              + $" {flow} flowing at {RiverScrollDegrees:F0} degrees";
    }

    /// <summary>The material a surface wears, by the index AnimatedModel put in its node name.
    /// ⚠ BY NAME is the weak half: the mesh name settles the sea, but the river and the falls are
    /// recognised by what they are PAINTED with, because nothing else found so far tells them
    /// apart.</summary>
    static string MaterialOf(Model model, string surfaceName)
    {
        int hash = surfaceName.LastIndexOf('#');
        if (hash < 0 || !int.TryParse(surfaceName[(hash + 1)..], out int i)) return "";
        return i >= 0 && i < model.Materials.Count ? model.Materials[i] ?? "" : "";
    }

    static float Env(string key, float fallback)
        => float.TryParse(System.Environment.GetEnvironmentVariable(key), out var v) ? v : fallback;

    // ⚠ THERE IS NO `Advance` HERE ANY MORE, DELIBERATELY. Water no longer owns the clock: every
    // moving texture in the park shares `Ps2Materials.TextureTime`, terrain and placed models
    // alike, so a second entry point that set the same clock would just walk the same material
    // list twice a frame and invite the two to drift apart in a later edit.

    /// <summary>What the surfaces actually hold, and where they are. ⚠ A frame diff that shows
    /// nothing moving cannot tell "the water is not advancing" from "the water is off screen";
    /// this says which, in numbers.</summary>
    public string State(Node3D terrain)
    {
        if (_moving.Count == 0) return "no water";
        // ⚠ The surfaces no longer all share a scroll, so report the SPREAD rather than the
        // first one -- quoting one material's vector as "the" scroll is how a sea and a river
        // going different ways would read as a single number.
        float t = Ps2Materials.TextureTime;
        var vectors = _moving.Select(m => (Vector2)m.GetShaderParameter("uv_scroll")).ToList();
        var scroll = vectors[0];
        string spread = string.Join(" ", vectors.Select(v => $"({v.X:F3},{v.Y:F3})").Distinct());
        var box = new Aabb();
        bool first = true;
        foreach (var mi in terrain.GetChildren().OfType<MeshInstance3D>())
        {
            if (mi.MaterialOverride is not ShaderMaterial m || !_moving.Contains(m)) continue;
            var b = mi.GetAabb();
            b.Position += mi.GlobalPosition;
            if (first) { box = b; first = false; } else box = box.Merge(b);
        }
        // ⚠ THE WHOLE VECTOR, not its V component. Reporting scroll.Y alone printed "-0.000/s"
        // the moment the direction turned ninety degrees into U, which reads exactly like a scroll
        // that has stopped -- an instrument that names the wrong axis is worse than none.
        return $"time {t:F2}s, scrolls {spread} at {scroll.Length():F3}/s,"
             + $" {_moving.Count} surfaces spanning "
             + $"x {box.Position.X:F0}..{box.End.X:F0} z {box.Position.Z:F0}..{box.End.Z:F0}";
    }
}
