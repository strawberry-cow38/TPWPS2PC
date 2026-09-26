using TPW.PS2.Data;

/// <summary>Native track rides: <see cref="TrackLayout"/> lays the pieces and bakes the samples,
/// <see cref="TrackRideSim"/> runs boarding, laps and unloading. The rules are the executable's
/// (findings/track-rides.md and its four detail files); these checks hold the port to them.
///
/// ⚠ Every geometry check has its CONTROL on the same fixture: the other bend hand has no b2 jump,
/// and the same loop without the crossing leg has no crossing piece. So a check that passes
/// because the rule never fired shows up as its control failing.</summary>
static class TrackRideChecks
{
    static readonly ParkCell Station = new(40, 40);

    /// <summary>A rectangle out of the exit and back into the entry, turning to one side.
    /// Every leg is even, so no holes.</summary>
    static TrackLayout Loop(int rot, int side, TrackGround ground)
    {
        var l = new TrackLayout(Station, rot, ground);
        var e = l.ExitCell; var r = l.ReturnCell;
        int ox = Math.Sign(e.X - r.X), oz = Math.Sign(e.Z - r.Z);
        int sx = -oz * side, sz = ox * side;
        foreach (var c in new[] { e.Offset(6 * ox, 6 * oz), e.Offset(6 * ox + 8 * sx, 6 * oz + 8 * sz),
                                  e.Offset(-8 * ox + 8 * sx, -8 * oz + 8 * sz), e.Offset(-8 * ox, -8 * oz), r })
            l.Add(c);
        return l;
    }

    static List<int> Steps(TrackLayout l)
    {
        var steps = new List<int>();
        for (int d = 0; d < l.Length; d += 64)
        {
            var a = l.Position(d, 128); var b = l.Position(d + 64, 128);
            steps.Add((int)Math.Round(Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Z - a.Z, 2))));
        }
        return steps;
    }

    public static void Run(Action<bool, string> check)
    {
        void Check(bool ok, string m) => check(ok, "track ride: " + m);
        var jungle = new TrackGround { World = 0, Park = 0 };

        // ---- geometry: the loop closes and the centre line is continuous
        for (int rot = 0; rot < 4; rot++)
            foreach (int side in new[] { 1, -1 })
            {
                var l = Loop(rot, side, jungle);
                var steps = Steps(l);
                int b2 = l.Pieces.Count(p => p.Type is >= 16 and <= 19), b = l.Pieces.Count(p => p.Type is >= 12 and <= 15);
                int jumps = steps.Count(s => s is >= 225 and <= 228), zeros = steps.Count(s => s == 0);
                bool rest = steps.All(s => s == 128 || s is >= 99 and <= 100 || s is >= 225 and <= 228 || s == 0);
                Check(l.Closed && l.Pieces.Count == 22 && rest && jumps == b2 && zeros == b2 && b + b2 == 4,
                      $"rot {rot} side {side}: closed, 22 pieces, straights step 128 and bends ~100 along the centre line,"
                      + $" and each of the {b2} b2 bends has exactly one 226 jump and one 0 step (0x1FDBA8 samples b2 at 1/4..1, not 0..3/4)"
                      + $" [pieces {string.Join(",", l.Pieces.Select(p => p.Type))}; jumps {jumps} zeros {zeros}]");
            }
        // Control: which hand is b2 depends on the turn side; one side of every rotation has none.
        Check(Enumerable.Range(0, 4).All(rot => Loop(rot, 1, jungle).Pieces.All(p => p.Type is < 16 or > 19)
                                             != Loop(rot, -1, jungle).Pieces.All(p => p.Type is < 16 or > 19)),
              "control: for every rotation exactly one turning side builds b2 bends, so the jump count above is not vacuous");

        {
            var l = Loop(0, 1, jungle);
            var s0 = l.Pieces[0].Samples[0];
            Check((s0.P1Z + s0.P2Z) / 2 == Station.Z * 256 + 512 && s0.P1X == Station.X * 256 && l.Pieces[0].Type == 2 && l.Pieces[1].Type == 10,
                  "the station piece (type 2) runs its lanes along the exit row, centred 2 cells in, and is followed by the hidden connector (type 10)");
            Check(l.Pieces.Where(p => p.Type is >= 4 and <= 7).All(p => p.Samples.All(s => s.Height == 0x50)),
                  "flat track sits 80 units above the station's level in JUNGLE park 1");
            int weight = l.Weight;
            var sim = new TrackRideSim(l);
            Check(weight == 22 && sim.Excitement(80) == Math.Min(100, (80 + 11) * (0xc00 * 0x1000 >> 12) >> 12),
                  $"Excitement: (base + weight/2) x speed factor (Speed 50 clamps to 0.75) x duration factor (5 is 1.0) = {sim.Excitement(80)}");
            l.RemoveLast();
            sim.Rebuilt();
            Check(!l.Closed && sim.Status == TrackRideStatus.Closed,
                  "removing the closing waypoint opens the loop, and an open loop leaves the ride Closed (status 3)");
        }

        // ---- crossings: a figure eight crosses its own straight on an exact anchor
        {
            TrackLayout Eight(bool cross)
            {
                var l = new TrackLayout(Station, 0, jungle);
                var e = l.ExitCell;
                var way = cross
                    ? new[] { e.Offset(6, 0), e.Offset(6, 8), e.Offset(2, 8), e.Offset(2, -6), e.Offset(-8, -6), e.Offset(-8, 0), l.ReturnCell }
                    : new[] { e.Offset(6, 0), e.Offset(6, 8), e.Offset(-8, 8), e.Offset(-8, 0), l.ReturnCell };
                foreach (var c in way) l.Add(c);
                return l;
            }
            var eight = Eight(true);
            var at = eight.ExitCell.Offset(2, 0);
            var there = eight.Pieces.Where(p => p.Anchor == at).Select(p => p.Type).OrderBy(t => t).ToList();
            // The old straight heads +x, so it becomes 0x14 + 2 = 22 (hidden); the new leg heads -z, so
            // 0x14 + 1 = 21, which is one of the two types that carry the x mesh.
            Check(eight.Closed && there.Count == 2 && there[0] == 21 && there[1] == 22
                  && eight.Pieces.Count(p => TrackPieces.Type(p.Type).Shape == 4) == 1,
                  $"a figure eight crosses its own straight: the old +x piece becomes 22 (hidden) and the new -z one 21 (the x mesh), so one x is drawn [{string.Join(",", there)}]");
            Check(!Eight(false).Pieces.Any(p => p.Type is >= 20 and <= 23),
                  "control: the same loop without the crossing leg has no crossing piece");
            Check(Steps(eight).All(s => s == 128 || s is >= 99 and <= 100 || s is >= 225 and <= 228 || s == 0),
                  "the figure eight's centre line is continuous through the crossing");
        }

        // ---- bridges: a path under the first leg
        foreach (int n in new[] { 1, 2, 3 })
        {
            var path = new HashSet<ParkCell>();
            var probe = new TrackLayout(Station, 0, jungle);
            for (int i = 0; i < n; i++) { var c = probe.ExitCell.Offset(2 * i, 0); path.Add(c); }
            var ground = new TrackGround { World = 0, Park = 0, Bridged = path.Contains };
            var l = Loop(0, 1, ground);
            var span = l.Pieces.Where(p => p.Type is >= 24 and <= 39).Select(p => TrackPieces.Type(p.Type).Shape).ToList();
            var want = n == 1 ? new[] { 3 } : n == 2 ? new[] { 9, 11 } : new[] { 9, 10, 11 };
            Check(span.SequenceEqual(want) && l.Pieces.Where(p => p.Type is >= 24 and <= 39).All(p => p.Samples.Skip(p.Type is >= 32 ? 0 : 1).All(s => s.Height == 0x100)),
                  $"a path under {n} block(s) of a leg becomes {(n == 1 ? "h" : n == 2 ? "h_u h_d" : "h_u h_a h_d")}, with the deck at 256 [{string.Join(",", span)}]");
        }

        // ---- operation
        RunSim(check, karts: true);
        RunSim(check, karts: false);
    }

    /// <summary>⭐ Through <see cref="ParkSim"/> with the ride's REAL script: the world's track-ride
    /// `.rse` still runs (it spins on BUMP_ISTRACKVALID for ever), but once the track is attached the
    /// VAR_LETMEON handshake is skipped and the cars take the queue themselves.
    /// ⚠ The control is the same ride with no track: it must board nobody, or this proves nothing.</summary>
    public static void RunParkSim(ParkPaths paths, WadArchive wad, string world, Action<bool, string> check)
    {
        void Check(bool ok, string m) => check(ok, "track ride: park: " + m);
        static string Dir(string p) => p[..(p.LastIndexOf('/') + 1)];
        var script = wad.Entries.FirstOrDefault(e => e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)
            && !e.Name.StartsWith("eventmap", StringComparison.OrdinalIgnoreCase)
            && wad.Entries.Any(m => Dir(m.Path).Equals(Dir(e.Path), StringComparison.OrdinalIgnoreCase)
                                    && m.Name.EndsWith("_trcks.mps", StringComparison.OrdinalIgnoreCase)));
        if (script == null) { Check(false, $"{world}: no track ride script beside a *_trcks.mps"); return; }
        string why = null;
        List<int> Ride(bool attach, out ParkRide ride, out int letMeOn)
        {
            var sim = new ParkSim(paths);
            ride = sim.Add(1, script.Name, Station, 4, 3, wad.Read(script), null, 4, Station.Offset(-1, 2), Station.Offset(4, 2), out var fault,
                          sibling: n => wad.Entries.FirstOrDefault(x => Dir(x.Path).Equals(Dir(script.Path), StringComparison.OrdinalIgnoreCase)
                                                                     && x.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) is { } e ? wad.Read(e) : null);
            letMeOn = -1;
            why = fault;
            if (ride == null) return null;
            sim.SetOpen(1, true);
            if (attach) sim.AttachTrack(1, Loop(0, 1, new TrackGround { World = 0, Park = 0 }), seed: 5);
            for (int g = 1; g <= 4; g++) ride.Join(g);
            var left = new List<int>();
            // ⚠ Advance runs at most 8 ticks a call (its catch-up ceiling), so feed it 8 at a time.
            for (int s = 0; s < 1500 && left.Count < 4; s++)
            {
                sim.Advance(8 * ParkSim.TickMilliseconds / 1000.0);
                left.AddRange(ride.Left); ride.ClearLeft();
            }
            letMeOn = ride.Get("VAR_LETMEON");
            return left;
        }
        var withTrack = Ride(true, out var r1, out int lmo1);
        if (withTrack == null) { Check(false, $"{world}: {script.Path} would not start: {why}"); return; }
        Check(withTrack.OrderBy(g => g).SequenceEqual(new[] { 1, 2, 3, 4 }) && lmo1 == 0 && r1.Queue.Count == 0,
              $"{world}: {script.Path} with its track attached boards all four queued guests and hands them back, and the script's VAR_LETMEON is never written [{string.Join(",", withTrack)}]");
        Check(r1.DestinationState is 2 or 10 or 11 && r1.CachedTrackWeight == 22,
              $"{world}: the ride's destination state follows the track status ({r1.DestinationState}) and Excitement's cached weight is the laid track's (22)");
        var without = Ride(false, out var r0, out _);
        Check(without != null && without.Count == 0,
              $"control: the same script with no track boards nobody who comes back ({without?.Count ?? -1} returned)");
    }

    static void RunSim(Action<bool, string> check, bool karts)
    {
        string kind = karts ? "karts" : "boats";
        void Check(bool ok, string m) => check(ok, $"track ride: {kind}: " + m);
        var ground = karts ? new TrackGround { World = 0, Park = 0 } : new TrackGround { World = 0, Park = 1 };
        var l = Loop(0, 1, ground);
        var sim = new TrackRideSim(l, seed: 3);
        Check(sim.Karts == karts, $"JUNGLE park {(karts ? 1 : 2)} runs {kind} (0x2EE528)");

        var queue = new Queue<int>();
        sim.TakeHead = () => queue.Count > 0 ? queue.Dequeue() : null;
        var boardedAt = new List<uint>(); var released = new List<int>();
        sim.Boarded += (g, c) => boardedAt.Add(0);
        sim.Released += released.Add;
        uint tick = 0;
        void Run(int n) { for (int i = 0; i < n; i++) sim.Step(++tick); }

        Run(40);
        Check(sim.Status == TrackRideStatus.Loading, "a fresh closed loop runs its empty cycle through and settles in Loading (10)");

        // No timeout: three guests for a capacity of four wait for ever.
        for (int g = 1; g <= 3; g++) queue.Enqueue(g);
        Run(2000);
        Check(sim.Status == TrackRideStatus.Loading && sim.Cars.Count == 3 && sim.Riders == 3 && sim.Cars.All(c => c.Distance > l.Length - 700),
              $"three guests, capacity four: three cars, one guest each, and after 2000 ticks it has still not launched (no timeout) [{string.Join(" | ", sim.Cars)}]");
        if (karts)
            Check(sim.Cars.Select(c => c.Lateral).SequenceEqual(new short[] { 64, 192, 64 })
                  && sim.Cars.Select(c => (int)c.Distance).SequenceEqual(new[] { l.Length - 100, l.Length - 200, l.Length - 300 }),
                  "the kart grid: 100 apart behind the line, lanes 64 and 192 alternating (0x203F10)");
        else
            Check(sim.Cars.Select(c => (int)c.Distance).SequenceEqual(new[] { l.Length - 200, l.Length - 400, l.Length - 600 }),
                  "boats wait 200 apart behind the line (0x203360)");

        // The fourth fills it; boarding runs on multiples of 20.
        queue.Enqueue(4);
        uint before = tick;
        while (sim.Status == TrackRideStatus.Loading && tick < before + 100) sim.Step(++tick);
        Check(sim.Status == TrackRideStatus.Running && sim.Cars.Count == 4 && tick % 20 == 0,
              $"the fourth guest fills it and it launches on a boarding tick (tick {tick})");
        uint launched = tick;
        while (sim.Status == TrackRideStatus.Running) sim.Step(++tick);
        Check(tick - launched == 2 * sim.Duration && sim.Status == TrackRideStatus.Unloading,
              $"Running (2) lasts 2 x Duration = {2 * sim.Duration} updates, then Unloading (11) while the cars drive on (0x200518)");

        var maxLap = new Dictionary<int, int>();
        bool lateralOk = true;
        while (sim.Status == TrackRideStatus.Unloading && tick < launched + 20000)
        {
            sim.Step(++tick);
            foreach (var c in sim.Cars)
            {
                maxLap[c.Guest ?? -1] = Math.Max(maxLap.GetValueOrDefault(c.Guest ?? -1, -1), c.Lap);
                if (!karts && (c.Lateral < 128 - 12 || c.Lateral > 128 + 12)) lateralOk = false;
            }
        }
        Check(sim.Status == TrackRideStatus.Loading && released.OrderBy(g => g).SequenceEqual(new[] { 1, 2, 3, 4 }),
              $"every rider comes off after the ride, and it goes back to Loading ({tick - launched} ticks, {(tick - launched) / 25.0:0} s at 25 a second)");
        Check(maxLap.Values.All(v => v == sim.Duration),
              $"each car ran Duration = {sim.Duration} laps and no more [{string.Join(",", maxLap.Values)}]");
        if (!karts)
            Check(lateralOk, "boats wander only a little either side of the centre line (the ±10 wobble, chased at 2 a tick)");

        // A rebuild unloads everyone at once.
        released.Clear();
        for (int g = 11; g <= 14; g++) queue.Enqueue(g);
        Run(100);
        int aboard = sim.Riders;
        sim.RemoveLastWaypoint();
        Check(aboard == 4 && released.Count == 4 && sim.Cars.Count == 0 && sim.Status == TrackRideStatus.Closed,
              "editing the track mid-ride puts every rider off at once (0x2009C0 starts with the unload loop) and an open loop leaves it Closed");
        queue.Enqueue(21);
        Run(200);
        Check(sim.Cars.Count == 0 && queue.Count == 1, "a Closed ride boards nobody");
    }
}
