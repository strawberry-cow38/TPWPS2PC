using System.Numerics;
using TPW.PS2.Data;

/// <summary>Roller coasters: <see cref="CoasterTrack"/> is the node ring, the spline and the
/// placement rules, <see cref="CoasterSim"/> the trains (findings/coasters.md and its five detail
/// files). These checks hold the port to the executable's numbers.
///
/// ⚠ The rule checks come in PAIRS across the boundary (2 cells refused, 3 taken; 45° refused off
/// the station, 26.6° taken), so a rule that never fires shows up as its partner failing. The lift
/// check has a flat control on which the chain must cover everything.</summary>
static class CoasterChecks
{
    static readonly ParkCell ExitCell = new(40, 41), EntryCell = new(35, 41);
    static readonly (int X, int Z)[] Oval =
        { (46, 41), (51, 45), (51, 51), (46, 55), (40, 55), (34, 55), (29, 51), (29, 46), (31, 41) };

    sealed class Ground : CoasterTrack.IGround
    {
        public readonly List<CoasterTrack> Tracks = new();
        public readonly HashSet<ParkCell> Blocked = new();
        public bool InGrid(ParkCell c) => c.X >= 0 && c.Z >= 0 && c.X < 128 && c.Z < 128;
        public bool EmptyLand(ParkCell c) => !Blocked.Contains(c);
        public CoasterNode CoasterNodeAt(ParkCell c) => Tracks.Select(t => t.BottomAt(c)).FirstOrDefault(n => n != null);
        public float Clearance(ParkCell c, CoasterTrack own) => 0;
    }

    static CoasterType Temple => CoasterType.All[0];

    /// <summary>What the tool does: the ghost at the cell, validated before anything is laid.</summary>
    static bool Try(CoasterTrack t, CoasterTrack.IGround g, int x, int z, int h = 375, bool clearance = true)
    {
        var ghost = t.LinkGhost(new ParkCell(x, z), h, 0);
        bool ok = t.IsValid(ghost, g, clearance);
        t.UnlinkGhost(ghost);
        return ok;
    }

    static CoasterTrack Build(CoasterType type, int[] heights, out List<bool> valid, Ground g = null)
    {
        var t = new CoasterTrack(type, ExitCell, 3, EntryCell);
        g ??= new Ground();
        g.Tracks.Add(t);
        valid = new List<bool>();
        for (int i = 0; i < Oval.Length; i++)
        {
            int h = heights?[i] ?? type.ExitHeight;
            valid.Add(Try(t, g, Oval[i].X, Oval[i].Z, h));
            t.AddPylon(new ParkCell(Oval[i].X, Oval[i].Z), h, 0, false, CoasterNodeKind.Normal);
        }
        valid.Add(Try(t, g, EntryCell.X, EntryCell.Z, 0, false));
        return t;
    }

    static bool Near(Vector3 a, Vector3 b, float eps = 1e-3f) => Vector3.Distance(a, b) <= eps;

    public static void Run(Action<bool, string> check)
    {
        void Check(bool ok, string m) => check(ok, "coaster: " + m);

        // ---- the table
        var all = CoasterType.All;
        // ⚠ NINE, not ten: coaster-building.md §4.6 says "the ten marked yes", and the survey's table it
        // points at marks nine -- the three troughs, Dare Devil and The Shocker cannot loop.
        Check(all.Length == 14 && all.Count(c => c.Loops) == 9
              && all.GroupBy(c => c.Style).ToDictionary(x => x.Key, x => x.Count()) is var st
              && st['A'] == 5 && st['B'] == 3 && st['G'] == 2 && st['C'] == 1 && st['D'] == 1 && st['E'] == 1 && st['F'] == 1,
              "14 coasters, 9 can loop, styles A5 B3 G2 and C D E F one each (coaster-geometry.md §7)");
        int Rail(CoasterType c, int h) => -0x100 + c.Attach(h);
        var moon = all.First(c => c.IsMoonshot);
        Check(Rail(all[0], 375) == 305 && Rail(all[1], 575) == 511 && Rail(moon, 1125) == 1011 && Rail(moon, 335) == 300
              && Rail(all[13], 1470) == 1425,
              $"station rail heights through the additive pylon pose: Temple 305, Chak Atak 511, Moonshot 1011/300, Shocker 1425 "
              + $"[{Rail(all[0], 375)} {Rail(all[1], 575)} {Rail(moon, 1125)}/{Rail(moon, 335)} {Rail(all[13], 1470)}]");

        // ---- headings (0x19aa48)
        Check(CoasterTrack.HeadingOf(0, 5) == 0 && CoasterTrack.HeadingOf(5, 0) == 0x400 && CoasterTrack.HeadingOf(0, -5) == 0x800
              && CoasterTrack.HeadingOf(-5, 0) == 0xc00 && CoasterTrack.HeadingOf(4, 4) == 0x200 && CoasterTrack.HeadingOf(4, -4) == 0x600,
              "headings: +z 0, +x 0x400, -z 0x800, -x 0xc00, diagonals 0x200/0x600");

        // ---- the spline on the flat oval
        var flat = Build(Temple, null, out var flatValid);
        Check(flatValid.All(v => v), $"the test oval lays with every ghost valid, the closing one on the entry cell included [{string.Join(",", flatValid)}]");
        var closer = flat.AddPylon(EntryCell, 0, 0, false, CoasterNodeKind.Normal);
        Check(closer == null && flat.Closed && flat.Pylons.Count == 9 && flat.SegmentCount == 11,
              "a pylon on the entry cell closes the ring and adds no node (0x121d68 → 0x120868)");
        bool ends = flat.Nodes().All(n => n.Kind != CoasterNodeKind.Normal
            || Near(CoasterTrack.Spline(n, 0).Pos, n.P[1]) && Near(CoasterTrack.Spline(n, 1).Pos, n.P[2]));
        Check(ends, "every segment passes through its two control points at t = 0 and t = 1");
        bool c1 = true;
        for (int k = 1; k < flat.SegmentCount; k++)
        {
            var a = flat.SegmentNode(k); var b = a.Next;
            if (b == null) continue;
            c1 &= Near(CoasterTrack.Spline(a, 1).Tangent, CoasterTrack.Spline(b, 0).Tangent, 1e-3f);
        }
        Check(c1, "the curve is C1 across every node: the tangent leaving a segment is the one entering the next");
        // A level straight needs four collinear, equal-height control points: a line of pylons.
        var line = new CoasterTrack(Temple, ExitCell, 3, EntryCell);
        foreach (int x in new[] { 46, 52, 58, 64 }) line.AddPylon(new ParkCell(x, 41), 375, 0, false, CoasterNodeKind.Normal);
        var straight = line.Pylons[2];   // window (46,41) (52,41) (58,41) (64,41)
        var mid = straight.Samples[8];
        Check(MathF.Abs(mid.Normal.Y - 1) < 1e-3f && MathF.Abs(Vector3.Dot(Vector3.Normalize(mid.Side), Vector3.Normalize(mid.Tangent))) < 1e-3f,
              $"on a level straight the up vector is +y and the side is square to the tangent [{mid.Normal}]");
        Check(MathF.Abs(straight.Length - 6f * 17f / 16f) < 0.01f,
              $"a 6-cell straight measures 6 x 17/16: the 17th chord is extrapolated past t = 1 (0x19b208) [{straight.Length:F4}]");
        // Bank: positive bank lowers the +S edge.
        var banked = Build(Temple, null, out _);
        banked.Pylons[5].Bank = 0x100;
        banked.Recompute();
        var bs = banked.Pylons[5].Samples[16].Side;
        Check(bs.Y < -0.3f && flat.Pylons[5].Samples[16].Side.Y == 0,
              $"a bank of +0x100 tilts the side vector down at that node (S = (cosθcosβ, sinβ, −sinθcosβ), β = −bank) [{bs.Y:F3}]");

        // ---- the loop (0x19bdec)
        var lt = new CoasterTrack(Temple, ExitCell, 3, EntryCell);
        var g0 = new Ground(); g0.Tracks.Add(lt);
        lt.AddPylon(new ParkCell(46, 41), 375, 0, false, CoasterNodeKind.Normal);
        var lead = lt.AddPylon(new ParkCell(52, 41), 0, 0, true, CoasterNodeKind.LeadIn);
        // approach along +x: the loop node goes z+1 (coaster-building.md §4.6)
        var loop = lt.AddPylon(new ParkCell(52, 42), 0, 0, true, CoasterNodeKind.Loop);
        var after = lt.AddPylon(new ParkCell(58, 42), 375, 0, false, CoasterNodeKind.Normal);
        var top = CoasterTrack.Spline(loop, 0.5f).Pos;
        var q = CoasterTrack.Spline(loop, 0.25f).Pos;
        var p1 = loop.P[1]; var p2 = loop.P[2];
        Check(MathF.Abs(top.Y - ((p1.Y + p2.Y) / 2 + 6)) < 1e-3f && MathF.Abs(top.X - (p1.X + p2.X) / 2) < 1e-3f,
              $"the loop is inverted 6 cells above the lerp at t = 1/2 [{top} from {p1}..{p2}]");
        Check(MathF.Abs(q.X - (0.75f * p1.X + 0.25f * p2.X + 3)) < 1e-3f && MathF.Abs(q.Y - (0.75f * p1.Y + 0.25f * p2.Y + 3)) < 1e-3f,
              $"a quarter of the way round it is 3 cells ahead along the approach and 3 up [{q}]");
        var outT = Vector3.Normalize(CoasterTrack.Spline(after, 0).Tangent);
        Check(MathF.Abs(outT.X - 1) < 1e-3f && Near(CoasterTrack.Spline(after, 0).Pos, loop.P[2]),
              $"the lead-out leaves the loop node straight along the approach direction [{outT}]");
        Check(lead.Heading == 0x400 && loop.Heading == ((CoasterTrack.HeadingOf(0, 1) + 0x400) & 0xfff),
              "a loop node's heading is its chord's + 0x400; the lead-in keeps its own");

        // ---- placement rules (0x1216d8)
        bool FirstAt(int x, int z, out CoasterTrack t)
        {
            t = new CoasterTrack(Temple, ExitCell, 3, EntryCell);
            var g = new Ground(); g.Tracks.Add(t);
            return Try(t, g, x, z);
        }
        Check(!FirstAt(42, 41, out _) && FirstAt(43, 41, out _) && FirstAt(48, 41, out _) && !FirstAt(49, 41, out _)
              && !FirstAt(48, 42, out _),
              "3..8 cells from the previous node: 2 and 9 refused, 3 and 8 taken, (8,1) refused as √65 > 8");
        Check(!FirstAt(45, 46, out _) && FirstAt(46, 44, out _),
              "off the station the turn must be under 45°: 45° refused, 26.6° taken");
        CoasterTrack Two(int x, int z, out bool ok)
        {
            var t = new CoasterTrack(Temple, ExitCell, 3, EntryCell);
            var g = new Ground(); g.Tracks.Add(t);
            t.AddPylon(new ParkCell(46, 41), 375, 0, false, CoasterNodeKind.Normal);
            ok = Try(t, g, x, z);
            return t;
        }
        Two(46, 47, out bool right90); Two(47, 47, out bool under90); Two(51, 45, out bool at38);
        Check(!right90 && under90 && at38, "after the first pylon the turn must be under 90°: 90° refused, 80.5° and 38.7° taken");
        {
            var t = new CoasterTrack(Temple, ExitCell, 3, EntryCell);
            var g = new Ground(); g.Tracks.Add(t);
            g.Blocked.Add(new ParkCell(46, 41));
            bool blocked = Try(t, g, 46, 41);
            g.Blocked.Clear();
            bool clearNow = Try(t, g, 46, 41);
            Check(!blocked && clearNow, "a pylon needs empty land: refused on an occupied cell, taken once it is cleared");
        }
        {
            var t = new CoasterTrack(Temple, ExitCell, 3, EntryCell);
            var other = new CoasterTrack(Temple, new ParkCell(47, 30), 3, new ParkCell(42, 30));
            var g = new Ground(); g.Tracks.Add(t); g.Tracks.Add(other);
            other.AddPylon(new ParkCell(47, 40), 375, 0, false, CoasterNodeKind.Normal);
            bool nextTo = Try(t, g, 46, 41);
            bool clearOf = Try(t, g, 45, 41);
            Check(!nextTo && clearOf, "no coaster node, of any coaster, in the 8 cells around a pylon; one cell further is fine");
        }
        {
            // Stacking needs a way back to a pylon's cell with every turn under 90°: a pentagon.
            // The fifth side lands on p0, which is not the new node's prev, so the stack rule runs.
            (int, int)[] penta = { (46, 41), (51, 44), (51, 50), (46, 52), (42, 47) };
            CoasterTrack Penta(int h0, out CoasterTrack.IGround g)
            {
                var t = new CoasterTrack(Temple, ExitCell, 3, EntryCell);
                var gg = new Ground(); gg.Tracks.Add(t); g = gg;
                for (int i = 0; i < penta.Length; i++)
                    t.AddPylon(new ParkCell(penta[i].Item1, penta[i].Item2), i == 0 ? h0 : 375, 0, false, CoasterNodeKind.Normal);
                return t;
            }
            var t1 = Penta(375, out var g1);
            bool legs = t1.Pylons.All(n => t1.IsValid(n, g1, false));
            bool second = Try(t1, g1, 46, 41, 375, false);
            var s1 = t1.AddPylon(new ParkCell(46, 41), 375, 0, false, CoasterNodeKind.Normal);
            second &= s1.Below == t1.Pylons[0];
            t1.RemoveLast();
            // A third on that cell: lay the second, then a ghost back onto it from the same side.
            t1.AddPylon(new ParkCell(46, 41), 375, 0, false, CoasterNodeKind.Normal);
            var third = new CoasterNode { CellX = 46, CellZ = 41, Height = 375, Prev = t1.Pylons[4] };
            t1.Recompute(third);
            bool thirdRefused = !t1.IsValid(third, g1, false);
            Check(legs && second && thirdRefused,
                  $"a pylon stacks on this coaster's own pylon, two to a cell: the second taken, a third refused [{legs} {second} {thirdRefused}]");
            var t2 = Penta(900, out var g2);
            var t3 = Penta(600, out var g3);
            Check(!Try(t2, g2, 46, 41, 900, false) && Try(t3, g3, 46, 41, 900, false),
                  "a stack whose heights sum past 0x600 is refused (900 + 900), and 600 + 900 is taken (0x19a6e8)");
        }
        {
            // Closing: the last pylon's heading must be within 90° of the station's.
            var t = new CoasterTrack(Temple, ExitCell, 3, EntryCell);
            var g = new Ground(); g.Tracks.Add(t);
            foreach (var (x, z) in Oval.Take(7)) t.AddPylon(new ParkCell(x, z), 375, 0, false, CoasterNodeKind.Normal);
            var bad = t.AddPylon(new ParkCell(29, 45), 375, 0, false, CoasterNodeKind.Normal);   // heading -z, 90° off
            var ghostBad = new CoasterNode { CellX = EntryCell.X, CellZ = EntryCell.Z, Prev = bad };
            bad.Next = ghostBad; t.Recompute(ghostBad);
            bool refused = !t.IsValid(ghostBad, g, false);
            bad.Next = null; t.RemoveLast();
            var good = t.AddPylon(new ParkCell(31, 44), 375, 0, false, CoasterNodeKind.Normal);
            var ghostGood = new CoasterNode { CellX = EntryCell.X, CellZ = EntryCell.Z, Prev = good };
            good.Next = ghostGood; t.Recompute(ghostGood);
            bool taken = t.IsValid(ghostGood, g, false);
            good.Next = null; t.Recompute();
            Check(refused && taken, "closing needs the last pylon's heading within 90° of the station's: exactly 90° off refused, 74° off taken");
        }
        {
            var t = new CoasterTrack(Temple, ExitCell, 3, EntryCell);
            int laid = 0;
            for (int i = 0; i < 40; i++) if (t.AddPylon(new ParkCell(46 + 3 * (i % 8), 41 + 3 * (i / 8)), 375, 0, false, CoasterNodeKind.Normal) != null) laid++;
            Check(laid == 32, $"32 pylons at most (0x121dac: TRACK PYLON OVERFLOW) [{laid}]");
            var r = Build(Temple, null, out _);
            r.AddPylon(EntryCell, 0, 0, false, CoasterNodeKind.Normal);
            bool wasClosed = r.Closed;
            r.RemoveLast();
            Check(wasClosed && !r.Closed && r.Pylons.Count == 8 && r.Entry.Prev == null,
                  "undo on a closed ring reopens it and removes the last pylon");
        }

        // ---- trains
        int Count(int pylons, int cars) { int c = (pylons / 3 + 2) / cars; if (!(1 < c)) c = 2; if (!(c < 7)) c = 6; return c; }
        {
            var t = Build(Temple, null, out _); t.AddPylon(EntryCell, 0, 0, false, CoasterNodeKind.Normal);
            var sim = new CoasterSim(t);
            sim.Step();
            bool noneClosed = sim.Trains.Count == 0;
            sim.SetOpen(true); sim.Step();
            Check(noneClosed && sim.Trains.Count == Count(9, 4) && Count(9, 1) == 5 && Count(30, 1) == 6 && Count(30, 4) == 3,
                  $"trains spawn once open and closed: clamp((pylons/3+2)/cars, 2, 6) [{sim.Trains.Count}]");
        }
        int[] hills = { 375, 800, 1200, 1200, 400, 100, 300, 375, 375 };
        {
            var t = Build(Temple, hills, out var hv); t.AddPylon(EntryCell, 0, 0, false, CoasterNodeKind.Normal);
            var sim = new CoasterSim(t);
            sim.MarkLift();
            string Marks(CoasterTrack tk) => string.Join(" ", tk.Nodes().Select(n => new string(n.Samples.Select(x => x.Winch ? '#' : '.').ToArray())));
            bool stationChained = t.Exit.Samples.All(s => s.Winch) && t.Pylons[0].Samples.All(s => s.Winch);
            bool crestChained = t.Pylons[2].Samples.All(s => s.Winch);
            bool dropFree = t.Pylons[4].Samples.Skip(4).Take(8).All(s => !s.Winch);
            Check(hv.All(v => v) && stationChained && crestChained && dropFree,
                  "the lift is found by physics (0x1239d8): the station and departure are always chained, the climb to the "
                  + $"crest is chained, the drop is not [{Marks(t)}]");
            // ⭐ THE JOIN QUIRK (0x1aee48..0x1aee68, MIPS): crossing into the next segment the look-ahead
            // subtracts the NEXT segment's length and divides by the CURRENT one's, so going into a
            // longer segment it lands up to a cell BEHIND the car. On a climb that point is lower, the
            // "drop" is positive, the speed jumps past 0.04 and the chain lets go for a stretch.
            // Precondition printed beside it: the next segment must be the longer one.
            float l0 = t.Pylons[0].Length, l1 = t.Pylons[1].Length;
            Check(l1 > l0 + 0.3f && t.Pylons[1].Samples.Any(s => !s.Winch) && t.Pylons[1].Samples[0].Winch,
                  $"the join quirk releases the chain on the climb into a longer segment ({l0:F2} -> {l1:F2} cells), "
                  + "so the lift texture is patchy exactly where the console's would be");
        }
        // ---- the test lap (0x122d48), statistics and rating
        {
            Check(CoasterSim.Rate(0.4f, 60f, 2f) == (0x256, true) && CoasterSim.Rate(0.4f, 60f, 4f) == (0x2bc, false)
                  && CoasterSim.Rate(0.5f, 60f, 2f).Ultimate == false && CoasterSim.Rate(0.2f, 50f, 1f).Row == 0x156
                  && CoasterSim.Rate(0.7f, 80f, 7f).Row == 0x2bf && CoasterSim.Rate(1.2f, 60f, 3f).Row == 0x278,
                  "the rating: Ultimate needs lateral < 0.5, speed 55..70 and 2 or 3 drops; otherwise [lateral][speed][drops] "
                  + "picks Too Slow / Average / A Bit Wild / Feeling Ill (0x2acda0)");
            var open = Build(Temple, hills, out _);
            Check(new CoasterSim(open).TestLap() == CoasterStats.None, "an open ring's test lap records nothing");
            var t = Build(Temple, hills, out _); t.AddPylon(EntryCell, 0, 0, false, CoasterNodeKind.Normal);
            var sim = new CoasterSim(t);
            var lap = sim.TestLap();
            float len = t.Pylons.Where(n => n.Kind != CoasterNodeKind.Loop).Sum(n => n.Length);
            Check(lap.Drops == 1 && MathF.Abs(lap.Length - len) < 1e-3f && lap.SteepestDrop is >= 15 and <= 60
                  && lap.Duration > 1 && lap.MaxSpeed > 20 && lap.MaxVertPos >= 0 && lap.MaxVertNeg <= 0 && lap.MaxLat > 0,
                  $"the hill oval's lap: 1 drop (the crest after a >256 climb), length = the pylon segments only, "
                  + $"steepest {lap.SteepestDrop} deg, {lap.Duration:F1} s, {lap.MaxSpeed:F1} kph, +{lap.MaxVertPos:F2}/{lap.MaxVertNeg:F2} vert, {lap.MaxLat:F2} lat");
            Check(sim.Trains.Count == 2 && sim.Trains.All(tr => tr.State == CoasterTrainState.Run && tr.Chain),
                  "after the lap the service trains are spawned afresh");
        }
        {
            var t = Build(Temple, hills, out _); t.AddPylon(EntryCell, 0, 0, false, CoasterNodeKind.Normal);
            var sim = new CoasterSim(t);
            var queue = new Queue<int>(Enumerable.Range(1, 40));
            var boardTicks = new List<int>();
            int tick = 0;
            sim.TakeHead = () => { if (queue.Count == 0) return null; boardTicks.Add(tick); return queue.Dequeue(); };
            var released = new List<int>();
            sim.Released += released.Add;
            sim.SetOpen(true);
            float maxV = 0, minGap = float.MaxValue;
            int arrivals = 0;
            var lastState = new Dictionary<CoasterTrain, CoasterTrainState>();
            var arrivedAt = new Dictionary<CoasterTrain, (int Tick, int Riders)>();
            var unloadSpans = new List<(int Span, int Riders, int Cars)>();
            bool lateOk = true;
            for (tick = 0; tick < 12000; tick++)
            {
                sim.Step();
                foreach (var tr in sim.Trains)
                {
                    maxV = Math.Max(maxV, tr.PrevSpeed);
                    var was = lastState.TryGetValue(tr, out var s) ? s : tr.State;
                    if (was == CoasterTrainState.Run && tr.State == CoasterTrainState.Dwell)
                    {
                        arrivals++;
                        arrivedAt[tr] = (tick, tr.Cars.Sum(c => c.Riders.Count));
                    }
                    if (was != CoasterTrainState.Wait && was != CoasterTrainState.Board && tr.State == CoasterTrainState.Wait
                        && arrivedAt.TryGetValue(tr, out var a))
                        unloadSpans.Add((tick - a.Tick, a.Riders, tr.Cars.Length));
                    lastState[tr] = tr.State;
                }
                if (sim.Trains.Count >= 2)
                    foreach (var tr in sim.Trains)
                    {
                        var ahead = sim.Trains[(tr.Index - 1 + sim.Trains.Count) % sim.Trains.Count];
                        float gap = ahead.Pos - tr.Pos;
                        while (gap < 0) gap += t.SegmentCount;
                        while (gap > t.SegmentCount) gap -= t.SegmentCount;
                        minGap = Math.Min(minGap, gap);
                    }
            }
            Check(arrivals > 10 && maxV > 0.1f, $"trains lap the hill oval on gravity: {arrivals} arrivals, top speed {maxV:F3} cells/tick");
            Check(minGap >= 0.75f - 0.25f, $"a train holds while under 0.75 segment behind the one ahead [closest {minGap:F3}]");
            var loaded = unloadSpans.Where(u => u.Riders > 0).ToList();
            lateOk = loaded.Count > 0 && loaded.All(u => u.Span == 11 + 11 * (u.Riders + u.Cars));
            Check(lateOk, $"unloading costs 11 ticks per rider and per car, after a 10-tick dwell: arrival→wait = 11 + 11(r + c) "
                          + $"[{string.Join(" ", loaded.Take(4).Select(u => $"{u.Span}@r{u.Riders}c{u.Cars}"))}]");
            var gaps = boardTicks.Zip(boardTicks.Skip(1), (a, b) => b - a).ToList();
            Check(boardTicks.Count > 0 && gaps.All(d => d >= 11),
                  $"boarding takes at most one guest per 11 ticks [{boardTicks.Count} boarded, closest {(gaps.Count > 0 ? gaps.Min() : -1)}]");
            int aboard = sim.Trains.Sum(tr => tr.Cars.Sum(c => c.Riders.Count));
            Check(released.Count > 0 && released.Count + aboard + queue.Count == 40 && sim.Riders == aboard,
                  $"every guest is queued, aboard or released: {released.Count} + {aboard} + {queue.Count} = 40");
            Check(sim.Status == 10, "boarding sets the coaster's status to 10 (loading)");
            t.Pylons[3].Valid = false;
            sim.Step();
            var spawnAt = Enumerable.Range(0, sim.Trains.Count).Select(i => (float)(t.Pylons.Count + 2 - i)).ToList();
            sim.Step();
            Check(sim.Riders == 0 && released.Count + queue.Count == 40
                  && sim.Trains.Select(tr => tr.Pos).SequenceEqual(spawnAt) && sim.Trains.All(tr => tr.Cars.All(c => c.Riders.Count == 0)),
                  "an invalid pylon puts every rider off at once; the status tick respawns empty trains that are deleted "
                  + "before they move, every update (0x1238c0 before 0x122af8)");
        }
    }

    /// <summary>Through ParkSim with a real coaster script: the `COAST` handler is a stub, so the
    /// guests only ride because the native trains board them.</summary>
    public static void RunParkSim(ParkPaths paths, WadArchive wad, string world, Action<bool, string> check)
    {
        void Check(bool ok, string m) => check(ok, "coaster: park: " + m);
        static string Dir(string p) => p[..(p.LastIndexOf('/') + 1)];
        var script = wad.Entries.FirstOrDefault(e => e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)
            && !e.Name.StartsWith("eventmap", StringComparison.OrdinalIgnoreCase)
            && wad.Entries.Any(m => Dir(m.Path).Equals(Dir(e.Path), StringComparison.OrdinalIgnoreCase)
                                    && m.Name.Equals("stdpylon.mps", StringComparison.OrdinalIgnoreCase)));
        if (script == null) { Check(false, $"{world}: no coaster script beside a stdpylon.mps"); return; }
        string folder = Dir(script.Path).TrimEnd('/'); folder = folder[(folder.LastIndexOf('/') + 1)..];
        var type = CoasterType.ForFolder(folder);
        if (type == null) { Check(false, $"{world}: {folder} is not in the coaster table"); return; }
        string why = null;
        List<int> Ride(bool attach, out ParkRide ride)
        {
            var sim = new ParkSim(paths);
            var at = new ParkCell(36, 40);
            ride = sim.Add(1, script.Name, at, 4, 3, wad.Read(script), null, 4, at.Offset(1, -1), at.Offset(2, -1), out var fault,
                          sibling: n => wad.Entries.FirstOrDefault(x => Dir(x.Path).Equals(Dir(script.Path), StringComparison.OrdinalIgnoreCase)
                                                                     && x.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) is { } e ? wad.Read(e) : null);
            why = fault;
            if (ride == null) return null;
            sim.SetOpen(1, true);
            if (attach)
            {
                var t = Build(type, null, out _);
                sim.AttachCoaster(1, t);
                bool openRing = !ride.CoasterTrackClosed;
                t.AddPylon(EntryCell, 0, 0, false, CoasterNodeKind.Normal);
                sim.Advance(ParkSim.TickMilliseconds / 1000.0);
                Check(openRing && ride.CoasterTrackClosed && ride.DestinationState is 2 or 10,
                      $"{world}: the ride learns its ring is closed (native +0x148) and its status is the coaster's ({ride.DestinationState})");
            }
            for (int g = 1; g <= 4; g++) ride.Join(g);
            var left = new List<int>();
            for (int s = 0; s < 3000 && left.Count < 4; s++)
            {
                sim.Advance(8 * ParkSim.TickMilliseconds / 1000.0);
                left.AddRange(ride.Left); ride.ClearLeft();
            }
            return left;
        }
        var with = Ride(true, out var r1);
        if (with == null) { Check(false, $"{world}: {script.Path} would not start: {why}"); return; }
        Check(with.OrderBy(g => g).SequenceEqual(new[] { 1, 2, 3, 4 }) && r1.Queue.Count == 0,
              $"{world}: {type.Name} with its track boards all four queued guests and hands them back [{string.Join(",", with)}]");
        var without = Ride(false, out _);
        Check(without != null && without.Count == 0,
              $"control: the same script with no track boards nobody (its COAST handler is a stub) ({without?.Count ?? -1} returned)");
    }
}
