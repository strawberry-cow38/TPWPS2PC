using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The terrain's water: every surface SCROLLS its texture. Nothing morphs.
///
/// ⭐⭐ THE TERRAIN NAMES ITS OWN WATER. `A_SEA_01..06` are meshes in every world's terrain file
/// and they all wear one material (`jri_lak2` in both jungle and space); the river and pond
/// surfaces wear `wr_water3`, `jri_sur1`, `jri_sur4`, `jri_lak1`. So which surfaces move is READ
/// off the model, not decided by me.
///
/// ⭐ The motion is the PSX's, measured there: a flagged sprite is rolled by ONE ROW PER FRAME and
/// re-uploaded, which is a V scroll of one texel a frame -- not palette cycling and not mesh
/// morphing, both ruled out by a VRAM diff of a running park. One texel of a 64-high texture per
/// console frame at 25 a second is 25/64 of the texture a second.
///
/// ⚠⚠ WHAT IS CHOSEN, NOT READ: the PS2's own flag for "this surface is water" has not been found
/// -- the material descriptor's spare bytes do not single it out, since `wr_water3` shares its
/// group with a roof and a flower, making that group alpha rather than water. The two scroll
/// ANGLES are chosen too, and stay on environment switches so they can be corrected by someone
/// who can see the game rather than guessed at twice.
///
/// ⭐⭐ THE SEA'S SINE IS GONE, 2026-10-01. It was invented -- amplitude, wavelength and speed all
/// picked by me -- and it contradicted the only measurement anyone has: the PSX rolls the TEXTURE
/// and a VRAM diff ruled mesh morphing OUT. The "only the top plane waves" guard that was supposed
/// to contain it never fired either, because the six `A_SEA` meshes TILE at one height rather than
/// stacking. See the note at the surface loop for the measurements.</summary>
public sealed class Water
{
    /// <summary>One texel of a 64-high texture per console frame.</summary>
    public const float ScrollTexelsPerTick = 1f;
    public const float AssumedTextureHeight = 64f;

    /// <summary>Which way each scroll runs, in degrees, where 0 is straight down the V axis --
    /// the direction a rolled sprite moves on the PSX.
    ///
    /// ⭐⭐ THE SEA AND THE RIVER DO NOT AGREE, and that is the point. One angle for both was the
    /// mistake: turning the river turned the sea with it, and master had already said the sea was
    /// right where it started. They are laid out differently, so they get an angle each.
    ///
    /// ⚠ Both are somebody's EYES, not a reading. The row roll settles the SPEED -- one texel a
    /// console frame, measured on the PSX -- and nothing found so far states the direction.</summary>
    const float SeaScrollDegrees = 0f;
    const float RiverScrollDegrees = 90f;

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
        // find the highest sea plane, and the planes turned out to tile at one height.
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

            float perSecond = Env("TPW_WATER_SCROLL",
                ScrollTexelsPerTick * ConsoleClock.TicksPerSecond / AssumedTextureHeight);
            float radians = Mathf.DegToRad(isSea
                ? Env("TPW_SEA_ANGLE", SeaScrollDegrees)
                : Env("TPW_WATER_ANGLE", RiverScrollDegrees));
            var scroll = new Vector2(Mathf.Sin(radians) * perSecond, Mathf.Cos(radians) * perSecond);
            // ⭐⭐⭐ THE SEA DOES NOT WAVE, AND THE ONE PIECE OF EVIDENCE WE HAVE SAYS SO.
            //
            // Master, 2026-10-01: "kill all the sea stuff we have rn and reimplement it properly,
            // assume everything previous was wrong." It was wrong, in three separate ways:
            //
            // ⚠ (1) THERE IS NO LOWER SEA LAYER. Measured, this session: all six `A_SEA` meshes in
            // every world sit at the SAME height (jungle: y = -2.640, 1.0 span, one material).
            // They are not stacked -- they TILE, edge to edge, around the island: `A_SEA_02` ends
            // at x = 67.7 and `_05` begins there; `_03` ends there and `_06` begins. So the
            // "only the top plane rolls" rule I shipped on 2026-09-24 could never fire, because
            // the test was `centre.Y < the highest sea Y` and they are all the highest. The fix
            // master asked for was reported as done and did nothing for a week.
            //
            // ⚠ (2) WHAT IS ACTUALLY BELOW THE SEA IS NOT SEA. The 13 other water surfaces sit at
            // -0.496 (ponds and river, ABOVE the sea), -1.5/-2.0, -2.782 (`RIVERBED_*B`, which are
            // degenerate -- zero X extent, so vertical strips), -6.65 (`falls02`) and -13.5. Ponds
            // overlap the sea tiles in XZ, so a still pond over a waving sea is what shears.
            //
            // ⚠⚠ (3) AND THE WAVE ITSELF WAS NEVER THE CONSOLE'S. The only measured motion is the
            // PSX's, and it is a TEXTURE SCROLL: a flagged sprite rolled one row per frame and
            // re-uploaded -- with **mesh morphing explicitly ruled out by a VRAM diff of a running
            // park** (see the class note). A vertex sine is the one thing that evidence excludes,
            // and this port shipped it anyway with amplitude, wavelength and speed all invented.
            //
            // So the sea scrolls and does not morph. If the PS2 turns out to morph where the PSX
            // did not, that is a finding to bring back with a capture -- not a number to re-guess.
            // ⚠ PROBE: what ARE the stacked sea planes? Master asked what the layers are and why,
            // and the port only ever knew "the top one waves". Printed once per surface.
            {
                var ab = mi.GetAabb();
                var c = ab.GetCenter() + mi.Position;
                GD.Print($"[sea] {(isSea ? "SEA " : "flow")} {mi.Name,-16} centre ({c.X,8:F1},{c.Y,7:F3},{c.Z,8:F1})  "
                       + $"size {ab.Size.X,7:F1} x {ab.Size.Z,7:F1}  "
                       + $"x [{c.X - ab.Size.X / 2,7:F1}..{c.X + ab.Size.X / 2,7:F1}]  "
                       + $"z [{c.Z - ab.Size.Z / 2,7:F1}..{c.Z + ab.Size.Z / 2,7:F1}]  {material}");
            }
            var wave = Vector3.Zero;      // no vertex motion: see the note above
            // ⚠ The material's OWN translucency, from the same resolver the model used. A river
            // drawn opaque is not a river.
            var made = Ps2Materials.Water(tex, _soft?.Invoke(material) ?? false, scroll, wave);
            mi.MaterialOverride = made;
            _moving.Add(made);
            if (isSea) sea++; else flow++;
        }
        Report = _moving.Count == 0
            ? "no water surfaces in this terrain"
            : $"{sea} sea surfaces scrolling at {SeaScrollDegrees:F0} degrees (no vertex motion),"
              + $" {flow} flowing at {RiverScrollDegrees:F0}";
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
