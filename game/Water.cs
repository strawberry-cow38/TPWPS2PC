using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>The terrain's water: the sea rolls on a sine, the river and the falls scroll.
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
/// group with a roof and a flower, making that group alpha rather than water. The sea's wave
/// amplitude, wavelength and speed are not stated anywhere I have found either. Every one of those
/// numbers is on an environment switch so it can be corrected in seconds by someone who can see
/// the game, rather than guessed at twice.</summary>
public sealed class Water
{
    /// <summary>One texel of a 64-high texture per console frame.</summary>
    public const float ScrollTexelsPerTick = 1f;
    public const float AssumedTextureHeight = 64f;

    /// <summary>⚠ CHOSEN. Amplitude in world units, wavelength in world units, radians a second.</summary>
    static readonly Vector3 SeaWave = new(0.06f, 9f, 1.1f);

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

    static readonly string[] WaterTextures = { "wr_water", "jri_sur", "jri_lak", "justwater" };

    /// <summary>Replace the water surfaces' materials with moving ones. ⚠ Takes the surfaces as
    /// the model built them, so a surface this does not recognise keeps exactly what it had.</summary>
    readonly Func<string, bool> _soft;

    public Water(Node3D terrain, Model model, Func<string, bool> soft = null)
    {
        _soft = soft;
        if (terrain == null || model == null) return;
        int sea = 0, flow = 0;
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
            var wave = isSea
                ? new Vector3(Env("TPW_SEA_AMP", SeaWave.X), Env("TPW_SEA_LEN", SeaWave.Y), Env("TPW_SEA_SPEED", SeaWave.Z))
                : Vector3.Zero;
            // ⚠ The material's OWN translucency, from the same resolver the model used. A river
            // drawn opaque is not a river.
            var made = Ps2Materials.Water(tex, _soft?.Invoke(material) ?? false, scroll, wave);
            mi.MaterialOverride = made;
            _moving.Add(made);
            if (isSea) sea++; else flow++;
        }
        Report = _moving.Count == 0
            ? "no water surfaces in this terrain"
            : $"{sea} sea surfaces on a sine scrolling at {SeaScrollDegrees:F0} degrees,"
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

    /// <summary>Move it. ⭐ Fed the console's own clock, like everything else that moves.</summary>
    public void Advance(float seconds)
    {
        foreach (var m in _moving) m.SetShaderParameter("water_time", seconds);
    }

    /// <summary>What the surfaces actually hold, and where they are. ⚠ A frame diff that shows
    /// nothing moving cannot tell "the water is not advancing" from "the water is off screen";
    /// this says which, in numbers.</summary>
    public string State(Node3D terrain)
    {
        if (_moving.Count == 0) return "no water";
        // ⚠ The surfaces no longer all share a scroll, so report the SPREAD rather than the
        // first one -- quoting one material's vector as "the" scroll is how a sea and a river
        // going different ways would read as a single number.
        float t = (float)_moving[0].GetShaderParameter("water_time");
        var vectors = _moving.Select(m => (Vector2)m.GetShaderParameter("water_scroll")).ToList();
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
