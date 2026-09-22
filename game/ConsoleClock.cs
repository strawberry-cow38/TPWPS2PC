using System;

namespace TPWPS2Viewer;

/// <summary>⭐⭐ THE PROJECT'S RULE FOR ANYTHING THAT MOVES: run it at the speed the console ran
/// it, in whole console frames, and INTERPOLATE for the screen.
///
/// Master's call, and it is the only arrangement that gets both halves. Stepping a simulation by
/// the real frame time makes it as fast as the machine it is on and makes integer state that came
/// out of the console drift -- the camera's ease steps by an eighth of what is left, floored, and
/// on a fraction of a tick that rounds to nothing and the turn stops short of its quarter. Stepping
/// it once per rendered frame instead ties the speed to the refresh rate. A fixed tick with the
/// leftover carried forward gives the console's speed exactly, on any machine; drawing between the
/// last tick and this one by <see cref="Alpha"/> gives back the smoothness the tick rate loses.
///
/// ⚠ 25 a second is MASTER'S number for this game, not a measurement of mine -- I had it at 50,
/// the PAL field rate, which is a different thing from how often the game's logic actually runs.
/// Everything time-dependent should be on this clock, so when the number is settled one way or the
/// other there is a single place to settle it.</summary>
public sealed class ConsoleClock
{
    public const int TicksPerSecond = 25;
    public const double TickSeconds = 1.0 / TicksPerSecond;

    /// <summary>⚠ A cap, so a long stall does not try to catch up with hundreds of ticks and stall
    /// further. Time is dropped rather than spiralled.</summary>
    public const int MaxTicksPerFrame = 5;

    double _carry;

    /// <summary>How far through the tick the screen is, 0 to 1. What a renderer lerps by.</summary>
    public float Alpha { get; private set; }

    /// <summary>Take a rendered frame's time and say how many whole console frames to run.</summary>
    public int Advance(double delta)
    {
        if (delta > 0) _carry += delta;
        int ticks = (int)(_carry / TickSeconds);
        if (ticks > MaxTicksPerFrame) { _carry = 0; ticks = MaxTicksPerFrame; }
        else _carry -= ticks * TickSeconds;
        Alpha = (float)Math.Clamp(_carry / TickSeconds, 0, 1);
        return ticks;
    }

    /// <summary>⚠ For a capture: a shot renders single frames and must not sit between ticks with
    /// a half-eased camera. Runs one whole tick and leaves nothing over.</summary>
    public void Reset() { _carry = 0; Alpha = 0f; }
}
