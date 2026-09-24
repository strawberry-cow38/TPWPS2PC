using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;
using Outcome = TPW.PS2.Data.NativeGuestRoute.Outcome;

/// <summary>
/// Managed cursor controls from findings/native-guest-motion.md, not a shipping
/// pathfinder, pool-capacity or entrance-lifecycle integration claim.
/// </summary>
static class NativeGuestRouteChecks
{
    public static void Run(Action<bool, string> check)
    {
        void Check(bool ok, string why) => check(ok, "native guest route cursor: " + why);
        bool State(NativeGuestRoute route, Point position, Point? target, int state,
            int slot, bool finished = false, bool failed = false)
            => route.Position == position && route.CurrentTarget == target
                && route.ExecutionState == state && route.SlotIndex == slot
                && route.Finished == finished && route.Failed == failed;

        Point start = new(128, 128), end = new(192, 128);
        var terminal = new NativeGuestRoute(start, new[] { end });
        Check(State(terminal, start, end, 3, 0), "initial live slot in walking state");
        Check(terminal.Step(127, 0x4000, 4, 4, true) == Outcome.Pending
            && State(terminal, end, end, 2, 0), "terminal phase 1: arrival retains slot, not completion");
        Check(terminal.Step(127, 0x4000, 4, 4, false) == Outcome.Pending
            && State(terminal, end, null, 3, -1), "terminal phase 2: release even when not ready, no dispatch");
        Check(terminal.Step(127, 0x4000, 4, 4, false) == Outcome.Pending
            && State(terminal, end, null, 2, -1), "terminal phase 3: no-slot transition precedes readiness");
        Check(terminal.Step(127, 0x4000, 4, 4, false) == Outcome.Completed
            && State(terminal, end, null, 2, -1, finished: true), "terminal phase 4: only now complete");
        Check(terminal.Step(127, 0x4000, 0, 0, true) == Outcome.Idle
            && State(terminal, end, null, 2, -1, finished: true)
            && terminal.FacingQuarterTurns == 1, "completed route is inert even with invalid bounds");
        Check(terminal.Step(-1, -1, 0, 0, false) == Outcome.Idle, "completion emitted once only");

        var equal = new NativeGuestRoute(start, new[] { start });
        Check(equal.Step(5, 0, 4, 4, false) == Outcome.Pending
            && State(equal, start, start, 3, 0) && equal.FacingQuarterTurns == 0,
            "equal target still waits for readiness without changing facing");
        Check(equal.Step(5, 0, 4, 4, true) == Outcome.Pending
            && State(equal, start, start, 2, 0) && equal.FacingQuarterTurns == 2,
            "equal-target phase 1: zero delta arrives, zero displacement faces 2");
        Check(equal.Step(5, 0, 4, 4, false) == Outcome.Pending
            && State(equal, start, null, 3, -1), "equal-target phase 2: release");
        Check(equal.Step(5, 0, 4, 4, false) == Outcome.Pending
            && State(equal, start, null, 2, -1), "equal-target phase 3: no-slot walk");
        Check(equal.Step(5, 0, 4, 4, false) == Outcome.Completed
            && State(equal, start, null, 2, -1, finished: true), "equal-target phase 4: dispatch");
        Check(equal.Step(5, 0, 4, 4, true) == Outcome.Idle, "equal-target dispatch occurs once");

        var empty = new NativeGuestRoute(start, Array.Empty<Point>());
        Check(State(empty, start, null, 3, -1), "empty route begins with no slot in state 3");
        Check(empty.Step(5, 0, 0, 0, false) == Outcome.Pending
            && State(empty, start, null, 2, -1) && empty.FacingQuarterTurns == 0,
            "empty no-slot path bypasses readiness and bounds, without selecting facing");
        Check(empty.Step(5, 0, 0, 0, false) == Outcome.Completed
            && State(empty, start, null, 2, -1, finished: true), "empty route dispatches on second call");
        Check(empty.Step(5, 0, 0, 0, false) == Outcome.Idle, "empty completion is once only");

        Point second = new(192, 192);
        var multi = new NativeGuestRoute(start, new[] { end, second });
        Check(multi.Step(127, 0x4000, 4, 4, true) == Outcome.Pending
            && State(multi, end, end, 2, 1), "intermediate arrival does not spend leftover step on next target");
        Check(multi.Step(127, 0x4000, 4, 4, true) == Outcome.Pending
            && State(multi, end, second, 3, 0) && multi.FacingQuarterTurns == 1,
            "intermediate release follows the saved pool successor only, with no movement or facing change");
        Check(multi.Step(127, 0x4000, 0, 0, false) == Outcome.Pending
            && State(multi, end, second, 3, 0) && multi.FacingQuarterTurns == 1,
            "unready live slot freezes all state before facing or bounds checks");
        Check(multi.Step(15, 0x4000, 4, 4, true) == Outcome.Pending
            && State(multi, new(192, 143), second, 3, 0) && multi.FacingQuarterTurns == 0,
            "next ready step uses only its own budget, not either preceding update");
        Check(multi.Step(127, 0x4000, 4, 4, true) == Outcome.Pending
            && State(multi, second, second, 2, 0), "second waypoint also retains slot on arrival");
        Check(multi.Step(127, 0x4000, 4, 4, true) == Outcome.Pending
            && State(multi, second, null, 3, -1), "second slot releases without completing");
        Check(multi.Step(127, 0x4000, 4, 4, true) == Outcome.Pending
            && State(multi, second, null, 2, -1), "multi-waypoint no-slot phase remains separate");
        Check(multi.Step(127, 0x4000, 4, 4, true) == Outcome.Completed
            && State(multi, second, null, 2, -1, finished: true), "multi-waypoint dispatch follows all phases");

        // Literal slot rounding controls, including signed cell-byte wrap.
        foreach (var row in new (Point Input, Point Expected)[] {
            (new(31, 32), new(0, 64)), (new(95, 96), new(64, 128)),
            (new(-33, -32), new(-64, 0)),
            (new(0x12a1, 0x22a0), new(0x12c0, 0x22c0)),
            (new(short.MaxValue, short.MinValue), new(short.MinValue, short.MinValue)) })
        {
            var rounded = new NativeGuestRoute(new(131, 133), new[] { row.Input });
            Check(State(rounded, new(131, 133), row.Expected, 3, 0),
                $"slot-codec target rounding {row.Input}, current position remains unquantized");
        }

        Point queueStart = new(0x1280, 0x2380);
        Point[] queuePoints = { new(0x1280, 0x2340), new(0x1280, 0x2300), new(0x1280, 0x22c0) };
        var queue = new NativeGuestRoute(queueStart, queuePoints);
        for (int i = 0; i < queuePoints.Length; i++)
        {
            Check(queue.CurrentTarget == queuePoints[i] && queue.SlotIndex == queuePoints.Length - 1 - i
                && queue.ExecutionState == 3, $"64-unit queue point {i} keeps quarter-cell position");
            Check(queue.Step(15, 0x4000, 128, 128, true) == Outcome.Pending
                && queue.Position == new Point(queueStart.X, (short)(queueStart.Z - i * 64 - 15))
                && queue.ExecutionState == 3, $"queue segment {i} takes 15 raw units first");
            for (int j = 0; j < 3; j++) queue.Step(15, 0x4000, 128, 128, true);
            Check(queue.Step(15, 0x4000, 128, 128, true) == Outcome.Pending
                && State(queue, queuePoints[i], queuePoints[i], 2, queuePoints.Length - 1 - i),
                $"queue segment {i} arrives on fifth step, clamping final four units");
            Check(queue.Step(127, 0x4000, 128, 128, true) == Outcome.Pending
                && State(queue, queuePoints[i], i + 1 < queuePoints.Length ? queuePoints[i + 1] : null,
                    3, i + 1 < queuePoints.Length ? queuePoints.Length - 2 - i : -1),
                $"queue segment {i} release never carries movement");
        }

        foreach (sbyte speed in new sbyte[] { -128, -1, 0, 4, 5, 15, 29 })
        {
            var signed = new NativeGuestRoute(start, new[] { end });
            Check(signed.Step(speed, 0x4000, 4, 4, true) == Outcome.Pending
                && State(signed, new((short)(128 + Math.Max(5, (int)speed)), 128), end, 3, 0),
                $"signed active speed {speed} uses native minimum 5");
        }
        var tiny = new NativeGuestRoute(start, new[] { end });
        for (int i = 0; i < 100; i++) tiny.Step(15, 128, 4, 4, true);
        Check(State(tiny, start, end, 3, 0) && tiny.FacingQuarterTurns == 1,
            "tiny deltas accumulate no remainder; facing still selected before stepping");
        Check(tiny.Step(15, 0, 4, 4, true) == Outcome.Pending && State(tiny, start, end, 3, 0),
            "zero delta away from target does not arrive");
        Check(tiny.Step(15, -1, 4, 4, true) == Outcome.Pending && State(tiny, end, end, 2, 0),
            "negative native delta retains arithmetic helper behavior");

        foreach (var row in new (Point Start, Point Target, int Columns, int Rows, int Facing)[] {
            (new(0, 128), new(-64, 192), 4, 4, 3),
            (new(128, 0), new(192, -64), 4, 4, 1),
            (new(1022, 128), new(1088, 192), 4, 4, 1),
            (new(128, 1022), new(192, 1088), 4, 4, 1) })
        {
            var outside = new NativeGuestRoute(row.Start, new[] { row.Target, start });
            Check(outside.Step(15, 0x4000, row.Columns, row.Rows, true) == Outcome.Failed
                && State(outside, row.Start, null, 0, -1, failed: true)
                && outside.FacingQuarterTurns == row.Facing,
                "bounds failure clears entire route, selects facing but commits neither coordinate");
            Check(outside.Step(127, -1, 128, 128, true) == Outcome.Idle
                && State(outside, row.Start, null, 0, -1, failed: true)
                && outside.FacingQuarterTurns == row.Facing,
                "failed route never resumes or reports completion when bounds change");
            Check(outside.Step(5, 0, 128, 128, false) == Outcome.Idle,
                "failure emitted once only, independent of readiness");
        }

        foreach (var row in new (Point Target, int Facing)[] {
            (new(64, 192), 3), (new(64, 64), 3),
            (new(192, 192), 1), (new(192, 64), 1),
            (new(128, 192), 0), (new(128, 64), 2), (new(128, 128), 2) })
        {
            var facing = new NativeGuestRoute(start, new[] { row.Target });
            facing.Step(15, 0, 4, 4, true);
            Check(facing.FacingQuarterTurns == row.Facing && facing.Position == start,
                $"direction {row.Target}: x priority, then z, including equal target");
        }

        var input = new List<Point> { new(159, 160), new(223, 224) };
        var copy = new NativeGuestRoute(start, input);
        Check(input.Count == 2 && input[0] == new Point(159, 160) && input[1] == new Point(223, 224),
            "construction quantizes a copy without mutating caller list");
        Check(copy.CurrentTarget == new Point(128, 192), "copied first target is quantized");
        input[0] = new(0, 0);
        input.Clear();
        input.Add(new(0, 0));
        Check(copy.CurrentTarget == new Point(128, 192), "caller replacement and resizing cannot change active target");
        copy.Step(127, 0x4000, 4, 4, true);
        copy.Step(127, 0x4000, 4, 4, true);
        Check(State(copy, new(128, 192), new(192, 256), 3, 0),
            "later target also snapshotted and quantized at construction");
        Check(input.Count == 1 && input[0] == new Point(0, 0), "stepping never writes caller list");
    }
}
