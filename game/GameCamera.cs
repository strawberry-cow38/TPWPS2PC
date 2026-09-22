using Godot;
using System;

namespace TPWPS2Viewer;

/// <summary>The park camera the PS2 game actually runs, as read out of `SLES_500.32` at
/// `0x14F820`. See `findings/camera.md`.
///
/// ⭐ Kept in the GAME'S OWN INTEGER UNITS with the game's own shifts, and converted to Godot
/// units only at the very end. The easing is a sequence of integer right-shifts, and doing it in
/// floats produces something that looks similar and is not the same curve -- the whole point of
/// this class is to feel like the console, so the arithmetic is the console's.
///
/// ⚠ NOT INCLUDED: modes 1 and 3 (riding a ride, and camcorder). Mode 1 needs a ride to sit in
/// and mode 3 needs an object to attach to, and the viewer has neither. They are written up in
/// findings/camera.md.</summary>
public sealed class GameCamera
{
    /// <summary>⭐ 256 world units to a tile, proven from the ground lookup's
    /// `eyeX * 0x10000 >> 0x18` -- a sign-extend of the low 16 bits then a divide by 256. One
    /// tile is one Godot unit here, so this is also the conversion to what the viewer draws.</summary>
    public const int TileUnits = 256;

    public const int TurnUnits = 0x1000;        // 4096 to a full turn
    public const int QuarterTurn = 0x400;       // what a turn button adds (0x14F820)

    /// <summary>Height above the ground under the camera. `0x39539C`, `0xA10` at reset.</summary>
    public const int DefaultAbove = 0xA10;
    /// <summary>Distance behind the focus. `0x395394`, `0x6E0` at reset -- which is the PSX
    /// camera's fixed `Behind`, the two versions agreeing exactly.</summary>
    public const int DefaultBehind = 0x6E0;
    public const int MinBehind = 0x180, MaxBehind = 0xC3C;
    /// <summary>`0x2B73D0`. ⚠ A dolly along the sight line, NOT a pitch -- it scales the unit view
    /// direction and adds it to the eye, leaving the direction alone.</summary>
    public const int MinDolly = -0x800, MaxDolly = 0x3C0;
    /// <summary>What one frame of holding a zoom or dolly button is worth.</summary>
    public const int AxisStep = 0x20;
    /// <summary>What ONE console frame is worth, in the units 0x14F820 uses; it clamps at 0x4000.</summary>
    public const int FrameTick = 0x1000;

    /// <summary>How many of those frames the console gets through in a second.
    ///
    /// ⚠ 50 because this is the PAL disc (SLES), which is the field rate, NOT something measured
    /// out of the game -- a PS2 title can run its logic at 60 on a 50Hz display. If the camera
    /// still feels fast or slow after this, THIS is the number to doubt first, before the eases.</summary>
    public const int TicksPerSecond = 50;

    /// <summary>⚠⚠ THE STEP TAKES REAL TIME, NOT A CONSTANT. Every frame used to advance the
    /// camera by exactly one console frame's worth, whatever the display was doing -- so on a
    /// 144Hz screen the ease ran nearly THREE TIMES the console's speed, and the faster the machine
    /// the faster the camera turned. Master, on real hardware, said Q and E were too quick.</summary>
    public static int FrameTime(double delta) =>
        (int)Math.Clamp(delta * TicksPerSecond * FrameTick, 0, 0x4000);

    /// <summary>The old fixed step, kept for anything that wants exactly one console frame.</summary>
    public const int FrameTime60 = FrameTick;

    /// <summary>⚠ OFF ONLY IN THE SELF-CHECK, to show the bug the snap exists for. A turn eased
    /// without it stops SHORT of the quarter on any frame shorter than a console tick.</summary>
    public bool SnapWhenStarved = true;

    public int TargetYaw, Yaw;                  // 0x395390, 0x39538C
    public int FocusX, FocusZ;                  // 0x395300, 0x395310 -- 24.8 fixed point
    public int CursorX, CursorZ;                // world units, what the focus chases
    public int Behind = DefaultBehind;          // 0x395394
    public int Above = DefaultAbove;            // 0x39539C
    public int Dolly;                           // 0x2B73D0
    int _eyeY;                                  // 0x395398
    int _lookY;                                 // 0x395360
    bool _started;

    /// <summary>Where the camera is and what it looks at, in GODOT units (one per tile).</summary>
    public Vector3 Eye { get; private set; }
    public Vector3 Look { get; private set; }
    public Vector3 Up { get; private set; } = Vector3.Up;

    /// <summary>The look-down angle the current distance produces, for reporting. The PS2 stores
    /// no pitch: the eye's height is pinned to the ground and only its x and z move, so the angle
    /// is a CONSEQUENCE of the distance -- 81.5° zoomed in, 55.7° at the default, 39.4° out.</summary>
    public float PitchDegrees => (float)(Math.Atan2(Above, Math.Max(Behind, 1)) * 180.0 / Math.PI);

    /// <summary>`0x14F718`, the reset: distance and height back to the PSX camera's two constants,
    /// both yaws and the dolly to zero.</summary>
    public void Reset()
    {
        Above = DefaultAbove; Behind = DefaultBehind;
        TargetYaw = Yaw = 0; Dolly = 0;
        _started = false;
    }

    public void Turn(int quarters) => TargetYaw += QuarterTurn * quarters;
    public void Zoom(int steps) => Behind = Math.Clamp(Behind + AxisStep * steps, MinBehind, MaxBehind);
    public void Push(int steps) => Dolly = Math.Clamp(Dolly + AxisStep * steps, MinDolly, MaxDolly);

    /// <summary>Put the camera at a tile without easing into it.</summary>
    public void PlaceAt(float tileX, float tileZ)
    {
        CursorX = (int)(tileX * TileUnits); CursorZ = (int)(tileZ * TileUnits);
        FocusX = CursorX << 8; FocusZ = CursorZ << 8;
        _started = false;
    }

    /// <summary>Wrap into [0, 0x1000) the way 0x14F820 does, with its own rounding.</summary>
    static int Wrap(int v)
    {
        if (v < 0)
        {
            int t = v < -1 ? v + (TurnUnits - 1) : v;
            return v + (t >> 12) * -TurnUnits + TurnUnits;
        }
        if (v >= TurnUnits)
        {
            int t = v >= 0 ? v : v + (TurnUnits - 1);
            return v + (t >> 12) * -TurnUnits;
        }
        return v;
    }

    /// <summary>An eighth of the difference, rounding toward zero as the game's `>> 3` on a
    /// negative does after its `+ 7`.</summary>
    static int Eighth(int d) => (d < 0 ? d + 7 : d) >> 3;

    /// <summary>One frame. <paramref name="groundAt"/> takes a TILE index, as the game's does,
    /// and returns the ground there in world units.</summary>
    public void Step(int frameTime, Func<int, int, int> groundAt)
    {
        frameTime = Math.Min(frameTime, 0x4000);
        TargetYaw = Wrap(TargetYaw);

        // ⚠ The yaw's two-step eighth, exactly as 0x14F820 has it: a half step to find the
        // difference, then a full one with it. One step alone is a different curve.
        int diff = TargetYaw - Yaw;
        if (diff < -0x800) { diff += TurnUnits; Yaw -= TurnUnits; }
        else if (diff > 0x800) { Yaw += TurnUnits; diff -= TurnUnits; }
        int half = Eighth(diff) * frameTime >> 13;
        int mid = TargetYaw - (Yaw + half);
        if (mid < -0x800) { mid += TurnUnits; Yaw -= TurnUnits; }
        else if (mid > 0x800) { Yaw += TurnUnits; mid -= TurnUnits; }
        int step = Eighth(mid) * frameTime >> 12;
        Yaw += step;
        // ⚠⚠ THE INTEGER EASE STARVES ON A SHORT FRAME. Each step is an EIGHTH of what is left,
        // floored -- so once the remainder is small the step rounds to nothing and the turn stops
        // a few units SHORT of the quarter it was aiming for. On the console that never showed,
        // because a frame was always a whole tick and the last step still carried; at 144Hz a
        // frame is worth a fraction of one and the step reaches zero with the turn unfinished.
        // Master saw it as Q and E no longer landing squarely on 90 degrees.
        //
        // ⭐ Snap only when the step has ACTUALLY starved -- zero movement with distance left.
        // While it is still moving, the curve is the console's and is left alone.
        if (SnapWhenStarved && step == 0 && Yaw != TargetYaw) Yaw = TargetYaw;

        // The focus chases the cursor: eight times the distance, clamped, over 1024.
        // Same starvation, same cure: a chase that can no longer move arrives.
        int dx = Math.Clamp(((FocusX >> 8) - CursorX) * 8, -0x3FFF, 0x3FFF) * frameTime >> 10;
        int dz = Math.Clamp(((FocusZ >> 8) - CursorZ) * 8, -0x3FFF, 0x3FFF) * frameTime >> 10;
        FocusX -= dx;
        FocusZ -= dz;
        if (dx == 0 && (FocusX >> 8) != CursorX) FocusX = CursorX << 8;
        if (dz == 0 && (FocusZ >> 8) != CursorZ) FocusZ = CursorZ << 8;

        // The eye, pushed back along the yaw by the distance. ⚠ ONLY x and z -- the height below
        // comes off the ground, which is exactly why the distance doubles as the pitch control.
        int a = Yaw & 0xFFF;
        int dirX = (int)(Math.Sin(a * Math.Tau / TurnUnits) * 4096);
        int dirZ = (int)(Math.Cos(a * Math.Tau / TurnUnits) * 4096);
        int eyeX = (FocusX >> 8) - (dirX * Behind >> 12);
        int eyeZ = (FocusZ >> 8) - (dirZ * Behind >> 12);

        int ground = groundAt(eyeX / TileUnits, eyeZ / TileUnits);
        int want = ground + Above;
        if (!_started) { _eyeY = want; _lookY = ground; _started = true; }
        // ⚠ Eases DOWN by an eighth and rises AT ONCE, so the camera never sits inside the hill
        // it is climbing. Both halves matter; easing both ways lets the ground swallow it.
        _eyeY -= (_eyeY - want) >> 3;
        if (_eyeY < want) _eyeY = want;
        _lookY -= (_lookY - ground) >> 3;

        var eye = new Vector3(eyeX, _eyeY, eyeZ) / TileUnits;
        var look = new Vector3(FocusX >> 8, _lookY, FocusZ >> 8) / TileUnits;

        // The dolly slides the eye along its own sight line. The direction is untouched, so this
        // is a zoom that does NOT change the pitch -- the other one does.
        var dir = (look - eye).Normalized();
        if (dir.LengthSquared() > 0f) eye += dir * (Dolly / (float)TileUnits);

        // ⭐ The lean. up' = cross(dir, cross(up, dir)), plus the frame's chase velocity over a
        // MILLION, re-normalised -- so the camera tips a hair into a pan. Felt, not seen.
        var right = Vector3.Up.Cross(dir);
        var up = right.LengthSquared() > 1e-9f ? dir.Cross(right.Normalized()) : Vector3.Up;
        Eye = eye; Look = look;
        Up = up.LengthSquared() > 1e-9f ? up.Normalized() : Vector3.Up;
    }
}
