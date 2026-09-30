using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>⭐⭐⭐ THE PARK'S ONE WIND -- a heading that WANDERS, read out of the executable.
///
/// Master, 2026-09-30: "does the wind direction for the flags actually ever change?" It did not,
/// twice over: <see cref="EntranceFlags.WindDegrees"/> was a settable float that nothing in the
/// tree ever assigned (so: 0 for ever), and the sky was handed a `const float SkyWindHeading =
/// 0.5045f`. Two winds, disagreeing with each other, neither moving.
///
/// ⭐⭐ THERE IS NO AUTHORED TURN RATE, which is why looking for one found nothing. `0x23eef0` is
/// an ease-to-target:
///
///     obj[0x48] = ticks;                          // countdown
///     obj[0x44] = (target - obj[0x40]) / ticks;   // rate = (target - current) / duration
///
/// and `0x23efe8` integrates `angle += rate * dt` while the countdown lasts, clamping dt to what
/// remains (`pminw`) so it lands exactly on the target and never overshoots. What the game
/// authors is a TARGET and a DURATION; the rate is the leftover division.
///
/// ⭐⭐ AND THE TARGET IS A RANDOM WALK. `0x14efb0`, the weather tick:
///
///     DAT_002b73c8 += ((float)(Count &amp; 0x1ff) - 256.0) * 0.00390625;
///     FUN_0023eef0(DAT_002b73c8, 0x342e28, 5000);
///
/// `0.00390625` is exactly 1/256 and `Count &amp; 0x1ff` is 0..511, so each roll nudges the heading by
/// `(rand - 256) / 256` -- up to **±1 radian (±57°)** -- and it eases there over 5000. The wind
/// wanders; it does not rotate steadily.
///
/// ⭐ It is rolled in the SAME function, two lines from, the weather amount (`rand/255` over the
/// same 5000 on the neighbouring channel), which is the mechanism behind `findings/sky.md`'s "the
/// clouds blow in a wind direction, and the weather turns it".
///
/// ⚠ THE CALL CADENCE OF `0x14efb0` IS NOT READ. The console rolls a new target from inside its
/// weather tick; this re-rolls when the previous ease finishes, which keeps the heading wandering
/// continuously and is self-consistent with a 5000-long ease, but it is THIS PORT'S choice of when
/// rather than the console's. If the weather sim is ever wired properly, the roll belongs on its
/// tick and this should follow it.
///
/// ⚠ 5000 is in the units of `DAT_002f07b0`, the step handed to the sky. `0x2f07a8` -- the next
/// word along -- is the known millisecond counter, so this treats it as MILLISECONDS. Inferred
/// from adjacency, not read, and named here so it is a stated assumption and not a silent one.</summary>
public sealed class Wind
{
    /// <summary>`FUN_0023eef0(..., 5000)` -- the ease length, in the sky step's own units (ms).</summary>
    public const float SlewMilliseconds = 5000f;

    /// <summary>`(Count &amp; 0x1ff) - 256`, scaled by `0.00390625` = 1/256: a step in ±1 radian.</summary>
    public const float StepScale = 1f / 256f;

    /// <summary>The heading the clouds and the flags both use, in radians.
    /// ⭐ Starts where the port's old constant was, so a park that never gets a roll looks exactly
    /// as it did before this existed rather than snapping to zero.</summary>
    public float Heading { get; private set; } = 0.5045f;

    float _target = 0.5045f, _rate, _remaining;
    uint _rng = 0x2f07b0;     // any fixed seed; the console's own `Count` source is not read

    /// <summary>The console's LCG, the one <see cref="ParticleTemplate.RngMultiplier"/> already
    /// records -- used rather than `System.Random` so a run is reproducible and so the generator
    /// is the disc's, even though the SEED is not.</summary>
    int Next() { _rng = unchecked(_rng * (uint)ParticleTemplate.RngMultiplier + (uint)ParticleTemplate.RngIncrement); return (int)(_rng >> 8); }

    /// <summary>Roll the next heading and start easing to it: `target += (rand(512) - 256) / 256`.</summary>
    public void Retarget()
    {
        // ⚠⚠ WRAPPED HERE, AND ONLY HERE. The console lets `DAT_002b73c8` accumulate for ever and
        // gets away with it: the angle only ever reaches a 256-entry sine TABLE, via
        // `(uint)(angle * 40.743664) & 0xff`, so the wrap is free and exact in integer maths. Our
        // consumers are float `sin`/`cos`, which lose precision once the heading has wandered into
        // the thousands of radians -- a slow decay rather than a bug, but a real one over a long
        // session. Wrapping at the ROLL BOUNDARY is invisible because `Heading` has just landed on
        // `_target`, so both move by the same turn and nothing jumps; wrapping mid-slew would
        // snap every flag in the park.
        if (Mathf.Abs(_target) > Mathf.Tau)
        {
            float turns = Mathf.Floor(_target / Mathf.Tau);
            _target -= turns * Mathf.Tau;
            Heading -= turns * Mathf.Tau;
        }
        float was = _target;
        _target += ((Next() & 0x1ff) - 256) * StepScale;
        _remaining = SlewMilliseconds;
        _rate = (_target - Heading) / SlewMilliseconds;
        // ⚠ Printed on the ROLL, not per frame: once every 5 s, and it is the only way to see that
        // the heading is wandering at all -- a wind that never moved looked exactly like this one
        // in every render for as long as the port has existed.
        GD.Print($"[wind] heading {Mathf.RadToDeg(Heading):F1} deg -> target "
               + $"{Mathf.RadToDeg(_target):F1} ({Mathf.RadToDeg(_target - was):+0.0;-0.0} deg step) "
               + $"over {SlewMilliseconds / 1000f:F0}s");
    }

    /// <summary>One frame. ⚠ `delta` in SECONDS (Godot's), converted here -- the console's step is
    /// in the same units as its 5000, and mixing the two is how an ease ends up 1000x too fast.</summary>
    public void Step(double delta)
    {
        if (_remaining <= 0f) { Retarget(); return; }
        // ⭐ Clamped to what remains, as `pminw` does, so the heading lands ON the target.
        float step = Mathf.Min((float)delta * 1000f, _remaining);
        _remaining -= step;
        Heading += _rate * step;
        if (_remaining <= 0f) Heading = _target;   // exact at the end, no float drift
    }
}
