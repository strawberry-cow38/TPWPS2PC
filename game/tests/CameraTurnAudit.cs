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
            // ⭐⭐ AT EVERY RATE, INCLUDING EVERY SINGLE FRAME. This is the guarantee the console
            // does NOT give: on 0x14F820 anything closer than six frames apart reverses. Master
            // asked for it twice ("still happens if i spam"), so the target is kept unwrapped and
            // the ease always runs toward it. An earlier version of this test asserted exactly
            // this and FAILED at frame 2, which is how the console's threshold got measured.
            for (int every = 1; every <= 20; every++)
                if (Worst(every) < 0)
                    throw new Exception($"a press every {every} frames turned back by {Worst(every)}");
            _checks++;

            // ⭐ And spamming must actually GO somewhere: four presses is four quarters, a whole
            // turn, not four presses collapsing into one.
            var four = new GameCamera(); four.Reset();
            for (int i = 0; i < 4; i++) { four.Turn(1); four.Step(Frame, Ground); }
            for (int f = 0; f < 400 && four.Yaw != four.TargetYaw; f++) four.Step(Frame, Ground);
            if (four.Yaw != 0)
                throw new Exception($"four quarters should land back at 0, landed at {four.Yaw}");
            _checks++;

            // ⭐ The ceiling: however hard it is spammed, the camera never owes more than one turn.
            var capped = new GameCamera(); capped.Reset();
            for (int f = 0; f < 30; f++) { for (int i = 0; i < 5; i++) capped.Turn(1); capped.Step(Frame, Ground); }
            int owed = capped.TargetYaw - capped.Yaw;
            if (owed > GameCamera.MaxPendingTurn || owed < -GameCamera.MaxPendingTurn)
                throw new Exception($"after heavy spam the camera still owes {owed}, over the {GameCamera.MaxPendingTurn} cap");
            _checks++;

            // ⭐ Three presses is three quarters, and it must arrive the way it was asked.
            var three = new GameCamera(); three.Reset();
            for (int i = 0; i < 3; i++) { three.Turn(1); three.Step(Frame, Ground); }
            for (int f = 0; f < 400 && three.Yaw != three.TargetYaw; f++) three.Step(Frame, Ground);
            if (three.Yaw != 3 * GameCamera.QuarterTurn)
                throw new Exception($"three quarters landed at {three.Yaw}, not {3 * GameCamera.QuarterTurn}");
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

            // ⭐⭐ NO FLASHBACKS. Master: "sometimes the camera has a flashback of where it was
            // once oriented for a split second".
            //
            // The invariant is exact, not statistical: PoseAt(1) is built from `_cur`, and after
            // the next Step that same pose IS `_prev`, so PoseAt(0) must land on the identical
            // point. Any whole-turn bookkeeping that shifts one without the other shows up here
            // as a jump, which is precisely what a one-frame flashback is.
            //
            // ⚠ It spams hard enough to wind the yaw past TWO turns, because one turn was already
            // survivable -- PoseAt used to correct by exactly one, so a single turn of drift hid
            // the fault and only a deeper wind-up exposed it.
            var glitch = new GameCamera(); glitch.Reset();
            // ⚠ WARM UP FIRST. Before the first Step the camera is not posed at all and PoseAt
            // hands back whatever the fields happen to hold, which is a startup artifact and not
            // the fault being hunted. Measuring it would have "reproduced" the bug on frame 0
            // forever and hidden whether the real one was still there.
            glitch.Step(Frame, Ground);
            float worstJump = 0f;
            int worstFrame = -1;
            for (int f = 0; f < 200; f++)
            {
                if (f < 40) for (int i = 0; i < 3; i++) glitch.Turn(1);   // wind it well past two turns
                var endOfTick = glitch.PoseAt(1f);
                glitch.Step(Frame, Ground);
                var startOfNext = glitch.PoseAt(0f);
                float jump = Mathf.Max(endOfTick.Eye.DistanceTo(startOfNext.Eye),
                                       endOfTick.Look.DistanceTo(startOfNext.Look));
                if (jump > worstJump) { worstJump = jump; worstFrame = f; }
            }
            if (worstJump > 0.001f)
                throw new Exception($"camera jumped {worstJump:F4} at frame {worstFrame} across a tick boundary -- a flashback");
            _checks++;

            // ⭐⭐ MASTER'S ACTUAL REPRO: "happens when i move after spamming q/e". Moving to a
            // cell that has a facing calls Face() (Viewer.cs), which OVERWRITES a target the
            // player's presses were still working through, while GlideTo drags the focus at the
            // same time. Both at once is the case the earlier checks never covered.
            var moved = new GameCamera(); moved.Reset();
            moved.Step(Frame, Ground);
            float movedJump = 0f; int movedFrame = -1;
            var aims = new (float X, float Z)[] { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, -1) };
            for (int f = 0; f < 300; f++)
            {
                if (f % 20 < 6) for (int i = 0; i < 4; i++) moved.Turn(1);       // spam
                if (f % 20 == 6)                                                 // then move
                {
                    var aim = aims[(f / 20) % aims.Length];
                    moved.Face(aim.X, aim.Z);
                    moved.GlideTo(8 + (f % 5) * 3, 5 + (f % 7) * 2);
                }
                var endOfTick = moved.PoseAt(1f);
                moved.Step(Frame, Ground);
                var startOfNext = moved.PoseAt(0f);
                float jump = Mathf.Max(endOfTick.Eye.DistanceTo(startOfNext.Eye),
                                       endOfTick.Look.DistanceTo(startOfNext.Look));
                if (jump > movedJump) { movedJump = jump; movedFrame = f; }
            }
            if (movedJump > 0.001f)
                throw new Exception($"moving after spamming jumped {movedJump:F4} at frame {movedFrame} -- the flashback");
            _checks++;

            GD.Print($"CAMERA TURN PASS: {_checks} checks; a frame's presses latch to one quarter per "
                   + "direction, a single press settles exactly one quarter on, spam queues whole "
                   + "quarters, NO press rate reverses, and no tick boundary jumps even when Face and "
                   + "GlideTo interrupt a spam");
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PrintErr("CAMERA TURN FAIL: " + ex); GetTree().Quit(2); }
    }
}
