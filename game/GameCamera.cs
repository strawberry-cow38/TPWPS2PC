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

    /// <summary>⭐ HOW FAR THE CURSOR MAY BE DRIVEN, in world tiles. Master: "wire the maps' camera
    /// border limits (where u cant wasd too far away)".
    ///
    /// ⚠⚠ THIS IS A CHOICE, NOT A READING, and it is the first thing in this file that is. The
    /// console's camera does not hold its own cursor: it reads one off an entity at
    /// `0x3951B0 + 0x2C` (see 0x14F820), and of the 53 functions that touch that object NOT ONE
    /// clamps against the park's cell dimensions -- so wherever the real limit lives, it is not
    /// where I looked. The plot's own rectangle is what is used here because it is the border a
    /// player would name, and it is per-map for free.
    ///
    /// ⚠ Unbounded until set, so nothing that does not set it changes behaviour.</summary>
    public float MinTileX = float.NegativeInfinity, MaxTileX = float.PositiveInfinity;
    public float MinTileZ = float.NegativeInfinity, MaxTileZ = float.PositiveInfinity;
    public bool Bounded => !float.IsInfinity(MinTileX);

    /// <summary>Pull the cursor back inside the border. ⚠ On the CURSOR, not the focus: the focus
    /// chases the cursor, so clamping the thing being chased lets the ease carry the view back
    /// rather than yanking it.</summary>
    public void ClampCursor()
    {
        if (!Bounded) return;
        CursorX = Math.Clamp(CursorX, (int)(MinTileX * TileUnits), (int)(MaxTileX * TileUnits));
        CursorZ = Math.Clamp(CursorZ, (int)(MinTileZ * TileUnits), (int)(MaxTileZ * TileUnits));
    }
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

    /// <summary>Turn the camera to look along a world direction, easing there like any other turn.
    ///
    /// ⭐ The view direction is the yaw's own: Step builds the eye at `focus - (sin, cos) * Behind`
    /// and looks at the focus, so the camera looks ALONG `(sin yaw, cos yaw)`. Inverting that is
    /// `atan2(x, z)`, in that order -- x against sin, z against cos -- and not the usual
    /// `atan2(z, x)`, which would be a quarter turn out and look plausible.
    ///
    /// ⚠ Sets the TARGET only. The ease takes the shortest way round by itself.</summary>
    public void Face(float worldX, float worldZ)
    {
        if (worldX == 0f && worldZ == 0f) return;
        double a = Math.Atan2(worldX, worldZ);
        if (a < 0) a += Math.Tau;
        TargetYaw = (int)Math.Round(a * TurnUnits / Math.Tau) & (TurnUnits - 1);
    }

    /// <summary>Send the camera to a tile and let it EASE there. ⭐ Only the cursor moves: the
    /// focus already chases it an eighth of the way per tick, which is the game's own easing and
    /// the reason this is not PlaceAt. Snapping the view onto a ride's door the instant it goes
    /// down would throw away the one thing that tells the player the camera moved at all.</summary>
    public void GlideTo(float tileX, float tileZ)
    {
        CursorX = (int)(tileX * TileUnits); CursorZ = (int)(tileZ * TileUnits);
    }

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
        // ⚠⚠ THE INTEGER EASE STARVES AT THE TAIL. Each step is an EIGHTH of what is left,
        // floored, so once under eight units remain the step rounds to nothing and the turn stops
        // short of its quarter.
        //
        // ⚠ It must not be finished with a JUMP. Closing the gap in one go is a teleport of
        // however much was left, and master could see it -- "the snap is pretty obvious and not
        // pleasant". It CREEPS instead: one unit a tick, 4096 to a turn, so the last fraction of a
        // degree arrives over a few ticks and no frame moves more than the ease already would.
        if (SnapWhenStarved && step == 0 && Yaw != TargetYaw) Yaw += Math.Sign(TargetYaw - Yaw);

        // The focus chases the cursor: eight times the distance, clamped, over 1024.
        // Same starvation, same cure, and the same refusal to jump: the focus creeps the last
        // fraction of a tile rather than arriving in one frame.
        int dx = Math.Clamp(((FocusX >> 8) - CursorX) * 8, -0x3FFF, 0x3FFF) * frameTime >> 10;
        int dz = Math.Clamp(((FocusZ >> 8) - CursorZ) * 8, -0x3FFF, 0x3FFF) * frameTime >> 10;
        FocusX -= dx;
        FocusZ -= dz;
        if (SnapWhenStarved && dx == 0 && (FocusX >> 8) != CursorX)
            FocusX += Math.Sign((CursorX << 8) - FocusX);
        if (SnapWhenStarved && dz == 0 && (FocusZ >> 8) != CursorZ)
            FocusZ += Math.Sign((CursorZ << 8) - FocusZ);

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

        _prev = _cur;
        _cur = new Pose(Yaw, FocusX, FocusZ, Behind, _eyeY, _lookY, Dolly);
        if (!_posed) { _prev = _cur; _posed = true; }
        (Eye, Look, Up) = Build(_cur);
    }

    /// <summary>The scalars a frame is built from. ⭐⭐ THESE are what gets interpolated, not the
    /// eye and the look that come out of them.</summary>
    readonly record struct Pose(int Yaw, int FocusX, int FocusZ, int Behind, int EyeY, int LookY, int Dolly);
    Pose _prev, _cur;
    bool _posed;

    /// <summary>Where the camera is a fraction of the way through the current tick.
    ///
    /// ⚠⚠ LERPING THE EYE AND THE LOOK IS NOT THE SAME THING and it jitters. Those two points
    /// swing round an arc as the camera turns, and a straight line between one tick's pair and the
    /// next cuts the corner -- the eye pulls in toward the focus and springs back out every tick,
    /// and the whole view shears instead of rotating. Master saw it at once: "very very jittery".
    /// The fix is to interpolate the STATE -- the yaw, the focus, the distance -- and build the
    /// frame from that, which is the same arithmetic a tick does and therefore lands on the arc.</summary>
    public (Vector3 Eye, Vector3 Look, Vector3 Up) PoseAt(float t)
    {
        if (!_posed) return (Eye, Look, Up);
        t = Math.Clamp(t, 0f, 1f);
        int from = _prev.Yaw;
        // ⚠ The shortest way round. A tick may have unwrapped the yaw by a whole turn, and lerping
        // across that gap would spin the camera the long way in a single frame.
        if (from - _cur.Yaw > TurnUnits / 2) from -= TurnUnits;
        else if (_cur.Yaw - from > TurnUnits / 2) from += TurnUnits;
        static int L(int a, int b, float f) => a + (int)Math.Round((b - a) * f);
        return Build(new Pose(
            L(from, _cur.Yaw, t), L(_prev.FocusX, _cur.FocusX, t), L(_prev.FocusZ, _cur.FocusZ, t),
            L(_prev.Behind, _cur.Behind, t), L(_prev.EyeY, _cur.EyeY, t),
            L(_prev.LookY, _cur.LookY, t), L(_prev.Dolly, _cur.Dolly, t)));
    }

    /// <summary>The frame those scalars make. Shared by the tick and by everything between two.</summary>
    static (Vector3 Eye, Vector3 Look, Vector3 Up) Build(Pose p)
    {
        int a = p.Yaw & 0xFFF;
        int dirX = (int)(Math.Sin(a * Math.Tau / TurnUnits) * 4096);
        int dirZ = (int)(Math.Cos(a * Math.Tau / TurnUnits) * 4096);
        int eyeX = (p.FocusX >> 8) - (dirX * p.Behind >> 12);
        int eyeZ = (p.FocusZ >> 8) - (dirZ * p.Behind >> 12);

        var eye = new Vector3(eyeX, p.EyeY, eyeZ) / TileUnits;
        var look = new Vector3(p.FocusX >> 8, p.LookY, p.FocusZ >> 8) / TileUnits;

        // The dolly slides the eye along its own sight line. The direction is untouched, so this
        // is a zoom that does NOT change the pitch -- the other one does.
        var dir = (look - eye).Normalized();
        if (dir.LengthSquared() > 0f) eye += dir * (p.Dolly / (float)TileUnits);

        // ⭐ The lean. up' = cross(dir, cross(up, dir)), re-normalised.
        var right = Vector3.Up.Cross(dir);
        var up = right.LengthSquared() > 1e-9f ? dir.Cross(right.Normalized()) : Vector3.Up;
        return (eye, look, up.LengthSquared() > 1e-9f ? up.Normalized() : Vector3.Up);
    }
}
