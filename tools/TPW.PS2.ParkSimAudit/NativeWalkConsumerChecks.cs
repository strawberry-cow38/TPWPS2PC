using TPW.PS2.Data;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;

/// <summary>Real walking/coordinator consumers of explicit caller-owned routes.
/// Not a native pathfinder, entrance admission, fee or queue-capacity proof.</summary>
static class NativeWalkConsumerChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string why) => check(ok, "native walk consumer: " + why);
        // Thought selection may react to the unhappy control even when rates are frozen.
        // Compare numeric needs/cash, not an unrelated HUD icon remaining at its spawn value.
        bool SameNeeds(VisitorWants actual, VisitorWants expected)
        { actual.Thought = expected.Thought; return actual.Equals(expected); }
        var archive = disc.Files().Single(f => f.Path.Equals("/DATA/JUNGLE.WAD", StringComparison.OrdinalIgnoreCase));
        var wad = new WadArchive(disc.Read(archive.Extent, archive.Size));
        var terrain = new Model(wad.Read(wad.Find("/terrain/terrain_1.mps")));
        var executable = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        var entrance = ParkEntrance.ReadExecutable(disc.Read(executable.Extent, executable.Size));
        ParkPaths Ground()
        {
            var p = new ParkPaths(terrain);
            p.SetEntrance(entrance);
            return p;
        }
        var paths = Ground();
        var entry = entrance.Fit(terrain.Field, ParkEntrance.WalkwayColumnFromPoles(terrain), out _);
        var start = new ParkCell(entry.XCol, entry.ZEnd - 2);
        var neighbour = start.Offset(0, -1);
        Check(!entry.Empty && paths.Open(start) && paths.Open(neighbour), "authored corridor fixture is legal (not a claimed native queue point)");
        Point centre = new(checked((short)(start.X * 256 + 128)), checked((short)(start.Z * 256 + 128)));
        Point quarter = new(centre.X, (short)(centre.Z - 64));
        var owner = new object(); var wrong = new object();
        int speeds = 0, deltas = 0, readiness = 0;
        bool ready = true;
        // 0x4000 is the native fixed-point delta, not decimal 4000 or seconds.
        var inputs = new NativeMotionInputs(() => { speeds++; return 15; },
            () => { deltas++; return 0x4000; }, () => { readiness++; return ready; });
        var walk = new GuestWalk(paths);
        var guest = walk.Spawn(start, start);
        var terminal = new GuestTerminal(new ParkRide { Id = 17 }, start, neighbour, () => true);
        var control = walk.Spawn(start, start);
        Check(walk.SendToTerminal(control, terminal), "terminal steal control is genuinely reachable");
        walk.Remove(control.Id);
        Check(walk.BeginNativeRoute(guest, owner, new[] { quarter }, inputs), "takes existing identity");
        var initial = guest.Position;
        Check(!walk.BeginNativeRoute(guest, wrong, new[] { centre }, inputs)
            && walk.NativeRouteState(guest, wrong) == null && !walk.ReleaseNativeRoute(guest, wrong), "wrong owner cannot inspect, replace or release");
        Check(!walk.Send(guest, neighbour) && !walk.SendToTerminal(guest, terminal)
            && guest.Position == initial, "public destinations cannot steal active lease");
        ready = false; walk.Step();
        Check(guest.Position == initial && readiness == 1 && speeds == 0 && deltas == 0,
            "false readiness blocks movement and speed/delta reads through normal Step");
        ready = true;
        for (int tick = 1; tick <= 5; tick++)
        {
            walk.Step();
            var expected = new Point(centre.X, (short)(centre.Z - Math.Min(64, tick * 15)));
            Check(walk.NativeRouteState(guest, owner) is { } state && state.Position == expected
                && !state.Finished && state.ExecutionState == (tick == 5 ? 2 : 3)
                && guest.Position.X == expected.X / 256f && guest.Position.Z == expected.Z / 256f
                && guest.Progress == 0 && guest.Steps == 0 && guest.Route == null,
                $"Step {tick}: exact raw displacement, no legacy double step");
            Check(!walk.ReleaseNativeRoute(guest, owner), $"Step {tick}: arrival is not release permission");
        }
        Check(guest.HasNativeRoute && guest.NativeHeading == -System.Numerics.Vector3.UnitZ,
            "at fractional waypoint still owned with native facing");
        for (int handoff = 1; handoff <= 3; handoff++)
        {
            walk.Step();
            Check(walk.NativeRouteState(guest, owner) is { } state && state.Position == quarter
                && state.SlotIndex == -1 && state.ExecutionState == (handoff == 1 ? 3 : 2)
                && state.Finished == (handoff == 3) && guest.HasNativeRoute,
                $"handoff {handoff}: deferred completion retains owner");
            Check(speeds == 5 && deltas == 5 && readiness == 6, "state2/no-slot never read input callbacks");
        }
        var fractional = guest.Position;
        Check(!walk.ReleaseNativeRoute(guest, owner) && guest.Position == fractional
            && !walk.Send(guest, neighbour) && !walk.SendToTerminal(guest, terminal),
            "finished quarter cannot snap or be stolen by public destinations");
        walk.Step();
        Check(speeds == 5 && deltas == 5 && readiness == 6 && guest.Position == fractional, "finished owned cursor is inert");
        Check(walk.BeginNativeRoute(guest, owner, new[] { centre }, inputs) && guest.Position == fractional,
            "same owner replaces fractional route without snapping");
        walk.Step();
        var exact = guest.Position;
        Check(walk.NativeRouteState(guest, owner)?.Position == new Point(centre.X, (short)(quarter.Z + 15)),
            "replacement begins at exact fractional coordinate");
        Check(walk.BeginNativeRoute(guest, owner, new[] { centre }, inputs) && guest.Position == exact,
            "replacement also preserves unquantized 15-raw intermediate position");
        for (int i = 0; i < 4; i++) walk.Step();
        Check(walk.NativeRouteState(guest, owner) is { Position: var at, Finished: false } && at == centre
            && !walk.ReleaseNativeRoute(guest, owner), "even centre arrival must finish handoff updates");
        for (int i = 0; i < 3; i++) walk.Step();
        Check(!walk.ReleaseNativeRoute(guest, wrong) && walk.ReleaseNativeRoute(guest, owner)
            && !guest.HasNativeRoute && guest.Position == initial, "only finished centre releases without teleport");

        // Empty routes exercise no-slot before there has ever been a movement/readiness read.
        int readsBefore = speeds + deltas + readiness;
        Check(walk.BeginNativeRoute(guest, owner, Array.Empty<Point>(), inputs), "empty route acquired");
        walk.Step(); walk.Step();
        Check(walk.NativeRouteState(guest, owner) is { Finished: true }
            && speeds + deltas + readiness == readsBefore, "initial no-slot bypasses all callbacks");
        int oldId = guest.Id;
        walk.Remove(oldId);
        Check(!guest.HasNativeRoute && walk.NativeRouteState(guest, owner) == null
            && !walk.ReleaseNativeRoute(guest, owner) && !walk.BeginNativeRoute(guest, owner, new[] { quarter }, inputs),
            "remove invalidates lease and detached reference");
        var newcomer = walk.Spawn(start, start);
        Check(newcomer.Id != oldId && !newcomer.HasNativeRoute
            && walk.NativeRouteState(newcomer, owner) == null && newcomer.Position == initial,
            "new identity after removal inherits no route or owner");
        walk.Remove(newcomer.Id);
        var reused = walk.Readmit(oldId, start, start);
        Check(!reused.HasNativeRoute && walk.NativeRouteState(reused, owner) == null && reused.Position == initial,
            "readmitted numeric identity inherits no lease");
        Check(walk.BeginNativeRoute(reused, owner, new[] { quarter }, inputs), "readmitted guest may acquire fresh lease");
        walk.Clear();
        var fresh = walk.Spawn(start, start);
        Check(!reused.HasNativeRoute && walk.NativeRouteState(reused, owner) == null
            && !walk.ReleaseNativeRoute(reused, owner)
            && !walk.BeginNativeRoute(reused, owner, new[] { quarter }, inputs)
            && fresh.Id == oldId && !fresh.HasNativeRoute && walk.NativeRouteState(fresh, owner) == null,
            "clear invalidates old lease; reset ID does not inherit it");

        foreach (bool unhappy in new[] { false, true })
        {
            var grid = Ground();
            var v = new ParkVisitors(new ParkSim(grid), new GuestWalk(grid), () => 0)
                { Needs = new VisitorNeeds(71) { SecondsPerRise = 1_000_000 } };
            foreach (var key in v.Needs.Rates.Keys.ToArray()) v.Needs.Rates[key] = new(0, 0, false);
            var g = v.Arrive(start, start);
            var wants = new VisitorWants { Cash = 1234, Happiness = (byte)(unhappy ? 0 : 80), Hunger = 12, Thirst = 13, Toilet = 14 };
            v.Needs.Set(g.Id, wants);
            int id = g.Id, wanderCalls = 0;
            ParkCell Wander() { wanderCalls++; return neighbour; }
            Check(v.Needs.WantsToGoHome(id) == unhappy, "needs departure control is active");
            Check(v.BeginEntranceRoute(g, owner, new[] { quarter }, inputs), "coordinator takes entrance ownership");
            var plan = v.Plans[id];
            Check(!v.BeginEntranceRoute(g, wrong, new[] { centre }, inputs)
                && !v.ReleaseEntranceRoute(g, wrong) && v.Plans[id] == plan, "coordinator refuses wrong owner without plan mutation");
            for (int tick = 1; tick <= 12; tick++)
            {
                v.Step(.04, Wander);
                Check(v.Walk.NativeRouteState(g, owner) is { } state
                    && state.Position == new Point(centre.X, (short)(centre.Z - Math.Min(64, tick * 15)))
                    && state.Finished == (tick >= 8) && g.HasNativeRoute
                    && v.Walk.Guests.Count == 1 && ReferenceEquals(v.Walk.Guests[0], g)
                    && v.Plans[id] == plan && plan.Intent == VisitorIntent.Entering
                    && v.WentHome == 0 && wanderCalls == 0 && SameNeeds(v.Needs.Of(id), wants),
                    $"coordinator unhappy={unhappy} tick{tick}: exact single step; Idle cannot steal, including after completion");
            }
            var before = g.Position;
            Check(!v.ReleaseEntranceRoute(g, owner) && g.Position == before && v.Plans[id] == plan,
                "coordinator fractional release refuses without teleport or plan change");
            Check(v.BeginEntranceRoute(g, owner, new[] { centre }, inputs) && g.Position == before,
                "coordinator replacement keeps exact position");
            for (int i = 0; i < 8; i++) v.Step(.04, Wander);
            Check(v.ReleaseEntranceRoute(g, owner) && g.Id == id && ReferenceEquals(v.Walk.Guests.Single(), g)
                && SameNeeds(v.Needs.Of(id), wants) && !g.HasNativeRoute
                && v.Plans[id].Intent == VisitorIntent.Wandering && g.Position == initial,
                "finished centre handback retains same identity, cash and numeric needs");
            v.Step(0, Wander);
            Check(unhappy ? v.WentHome == 1 || v.Plans[id].Intent == VisitorIntent.Leaving : wanderCalls > 0,
                "positive control: ordinary home/wander decision resumes only after release");
        }
    }
}
