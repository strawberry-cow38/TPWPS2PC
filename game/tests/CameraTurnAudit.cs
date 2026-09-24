using Godot;
using System;

namespace TPWPS2Viewer.Tests;

/// <summary>The park camera's turn input, against the console's once-a-frame pad sample.
///
/// ⭐⭐ THIS EXISTS FOR ONE REPORT: master, playing, "if i spam either q and e, sometimes ill go
/// backwards on my rotation". The reversal is the CONSOLE'S -- `0x14f820` eases along the short
/// way round, so a target more than half a turn ahead returns the other side -- but the console
/// can only ever add ONE quarter per direction per frame, so it takes sustained frame-perfect
/// mashing to get there. The port added a quarter per key EVENT and could blow past the half turn
/// inside a single frame, which is why it happened so easily.</summary>
public partial class CameraTurnAudit : Node
{
    int _checks;
    static int Ground(int x, int z) => 0;

    public override void _Ready()
    {
        try
        {
            // ⭐ A frame at the game's own rate. Step takes the console's frame time.
            const int Frame = 0x1000;

            // ⚠ THE CHECK THAT REJECTS THE BUG. Eight presses inside one frame must move the
            // target exactly as far as one, because the pad cannot report eight edges in a frame.
            // Before the latch this added 8 * 0x400 = two full turns and the ease reversed.
            var many = new GameCamera(); many.Reset();
            for (int i = 0; i < 8; i++) many.Turn(1);
            many.Step(Frame, Ground);
            var one = new GameCamera(); one.Reset();
            one.Turn(1);
            one.Step(Frame, Ground);
            if (many.TargetYaw != one.TargetYaw)
                throw new Exception($"eight presses in a frame gave target {many.TargetYaw}, one press gives {one.TargetYaw}");
            if (many.Yaw != one.Yaw)
                throw new Exception($"eight presses in a frame gave yaw {many.Yaw}, one press gives {one.Yaw}");
            _checks++;

            // ⭐⭐ THE THRESHOLD, MEASURED, NOT ASSUMED. Presses this far apart must never reverse.
            //
            // ⚠⚠ AND THE TEST DELIBERATELY DOES NOT ASSERT "never reverses at any rate", because
            // THE CONSOLE REVERSES TOO. `0x14f820` eases along the short way and its wrap does
            // `yaw += 0x1000; diff -= 0x1000`, which is visually a turn backwards. Simulating that
            // exact integer arithmetic: presses every 1..5 frames reverse, every 6 or more never
            // do. So faster than about six a second is retail behaviour and is left alone; what
            // was ours was having no per-frame ceiling at all.
            int Worst(int everyFrames)
            {
                var cam = new GameCamera(); cam.Reset();
                int previous = cam.Yaw, worst = 0;
                for (int f = 0; f < 120; f++)
                {
                    if (f % everyFrames == 0) cam.Turn(1);
                    cam.Step(Frame, Ground);
                    int d = cam.Yaw - previous;
                    if (d < -GameCamera.TurnUnits / 2) d += GameCamera.TurnUnits;
                    else if (d > GameCamera.TurnUnits / 2) d -= GameCamera.TurnUnits;
                    worst = Math.Min(worst, d);
                    previous = cam.Yaw;
                }
                return worst;
            }
            for (int every = 6; every <= 20; every += 2)
                if (Worst(every) < 0)
                    throw new Exception($"a press every {every} frames turned back by {Worst(every)}");
            _checks++;

            // ⭐ A single press, the overwhelmingly common case, must land a clean quarter forward.
            var once = new GameCamera(); once.Reset();
            once.Turn(1);
            int before = once.Yaw, back = 0;
            for (int f = 0; f < 60; f++)
            {
                once.Step(Frame, Ground);
                int d = once.Yaw - before;
                if (d < -GameCamera.TurnUnits / 2) d += GameCamera.TurnUnits;
                back = Math.Min(back, d);
                before = once.Yaw;
            }
            if (back < 0) throw new Exception($"a single press turned back by {back}");
            if (once.Yaw != GameCamera.QuarterTurn)
                throw new Exception($"a single press settled at {once.Yaw}, not one quarter ({GameCamera.QuarterTurn})");
            _checks++;

            // ⭐ Both directions in one frame cancel, as the two independent `if`s at 0x14f820 do.
            var both = new GameCamera(); both.Reset();
            both.Turn(1); both.Turn(-1);
            both.Step(Frame, Ground);
            if (both.TargetYaw != 0)
                throw new Exception($"pressing both ways in one frame moved the target to {both.TargetYaw}");
            _checks++;

            // ⚠ A press must not survive a reset, or the camera turns the moment it is re-aimed.
            var stale = new GameCamera(); stale.Reset();
            stale.Turn(1); stale.Reset();
            stale.Step(Frame, Ground);
            if (stale.TargetYaw != 0)
                throw new Exception($"a press survived Reset and moved the target to {stale.TargetYaw}");
            _checks++;

            GD.Print($"CAMERA TURN PASS: {_checks} checks; a frame's presses latch to one quarter per "
                   + "direction, a single press settles exactly one quarter on, and presses 6+ frames "
                   + "apart never reverse (faster than that is the console's own behaviour)");
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PrintErr("CAMERA TURN FAIL: " + ex); GetTree().Quit(2); }
    }
}
