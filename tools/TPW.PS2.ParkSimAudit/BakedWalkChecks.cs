using System.Numerics;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

/// <summary>⭐⭐ THE BAKED-VERTEX WALK, section 0 of every character, as <c>0x1a7e18</c> plays it
/// (findings/baked-walk.md). Reads every `/Chars` .aps through <see cref="Aps.BakedVertexTracks"/>
/// and checks the table against its record, its mesh and itself, then that the legs of two staff
/// models actually swing.
///
/// ⚠ EVERY MEASUREMENT HAS A CONTROL THAT MUST FAIL, because the failure of each looks like a
/// quiet pass: a static pose (the port before this channel) must not swing, an identity group map
/// must not close on the authored vertices, shuffled frames must not be continuous, and an
/// interpolating sampler must not be frame-exact.
///
/// ⚠⚠ A CORRELATION OVER ZERO VARIANCE IS NOT NaN IN PRACTICE. On the static control the L/R
/// forward means are constant up to double rounding, and the Pearson of that residue read -1.000 on
/// Researcher and Guard and +1.000 on Handyman -- a PASS for "anti-phase" on a figure that does not
/// move. So swing is required first and the correlation is only read when the legs moved.
///
/// NOT exercised here: the Viewer drawing it (BakedWalkFilm renders and measures the drawn
/// positions) and the guest/staff logical dispatch, which still draws guests' section 0 as
/// section 1.</summary>
static class BakedWalkChecks
{
    const int ExpectedRecords = 24, ExpectedTracks = 81;

    public static void Run(WadArchive data, Action<bool, string> check)
    {
        void Check(bool ok, string why) => check(ok, "baked walk: " + why);

        var stems = data.Entries.Where(e => e.Path.StartsWith("/Chars/", StringComparison.OrdinalIgnoreCase)
                                         && e.Path.EndsWith(".aps", StringComparison.OrdinalIgnoreCase))
                                .Select(e => e.Path[..^4]).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        (Aps Aps, Model Model) Load(string stem) =>
            (new Aps(data.Read(data.Find(stem + ".aps"))), new Model(data.Read(data.Find(stem + ".mps"))));

        // ---- census: every character's section 0 is one baked record ----
        int records = 0, tracks = 0, frameRule = 0, groupRule = 0, closed = 0, allTracks = 0;
        double worstClosure = 0;
        foreach (var stem in stems)
        {
            var (aps, model) = Load(stem);
            var s0 = aps.Records().Where(r => r.Slot == 0).ToList();
            if (s0.Count != 1 || s0[0].Skeletal || s0[0].Flags != 1) continue;
            records++;
            allTracks += s0[0].TrackCount;
            foreach (var t in aps.BakedVertexTracks(s0[0]))
            {
                tracks++;
                // header+4 / header+6 is the table's frame count; the clock reads 0..duration.
                if (t.Frames == s0[0].DurationFrames + 1) frameRule++;
                var map = t.Node < model.Meshes.Count ? model.AnimVertexMap(model.Meshes[t.Node]) : null;
                if (map != null && map.Max() + 1 == t.Groups) groupRule++;
                double c = Enumerable.Range(0, t.Groups).Max(g => (t.Position(t.Frames - 1, g) - t.Position(0, g)).Length());
                if (c <= 5) closed++;
                worstClosure = Math.Max(worstClosure, c);
            }
        }
        Check(records == ExpectedRecords, $"every /Chars .aps has ONE flags-0x01 record in section 0 ({records} of {stems.Count}, expect {ExpectedRecords})");
        Check(tracks == ExpectedTracks && allTracks == tracks,
            $"every track of those records is a 0x40000 baked track ({tracks} baked of {allTracks}, expect {ExpectedTracks})");
        Check(frameRule == tracks, $"each table holds the record's duration + 1 frames ({frameRule} of {tracks})");
        Check(groupRule == tracks, $"each table's groups equal its mesh's +0x98 run-list groups ({groupRule} of {tracks})");
        Check(closed == tracks, $"the extra frame closes the loop: last within 5 units of first ({closed} of {tracks}, worst {worstClosure:F1})");

        // ---- Handyman: frame 0 IS the authored mesh, through the run list ----
        var (hAps, hModel) = Load("/Chars/Handyman/handyman");
        var hRec = hAps.Records().Single(r => r.Slot == 0);
        var hTracks = hAps.BakedVertexTracks(hRec);
        double BindMiss(Func<Aps.BakedVertexTrack, int[], int, int> groupOf)
        {
            double worst = 0;
            foreach (var t in hTracks)
            {
                var mesh = hModel.Meshes[t.Node];
                var map = hModel.AnimVertexMap(mesh);
                var (bind, _, _) = hModel.Vertices(mesh);
                for (int v = 0; v < bind.Count; v++)
                    worst = Math.Max(worst, (t.Position(0, groupOf(t, map, v)) - bind[v]).Length());
            }
            return worst;
        }
        double real = BindMiss((t, map, v) => map[v]);
        double identity = BindMiss((t, map, v) => v % t.Groups);
        Check(real < 2, $"Handyman frame 0 through mesh+0x98 lands on all three meshes' authored vertices (worst {real:F2} units, int16 rounding)");
        Check(identity > 1000, $"MUTATION: a vertex-order group map misses them ({identity:F0} units) -- the closure is evidence for the map");

        // ---- the legs swing in anti-phase ----
        foreach (var (stem, minSwing) in new[] { ("/Chars/Handyman/handyman", 3000.0), ("/Chars/FatMechanic/FatMechanic", 3000.0),
                                                ("/Chars/Boy1a/boy1a", 3000.0), ("/Chars/junglekid/JungleKid", 3000.0) })
        {
            var (aps, model) = Load(stem);
            var rec = aps.Records().Single(r => r.Slot == 0);
            string who = stem[(stem.LastIndexOf('/') + 1)..];
            var legs = Legs.Of(aps, rec);
            var gait = legs.Measure(f => f);
            var still = legs.Measure(_ => 0);
            Check(gait.Moves(minSwing) && gait.Corr < -0.85,
                $"{who} {model.Meshes[legs.Track.Node].Name}: lowest-third L/R forward means swing {gait.SwingL:F0}/{gait.SwingR:F0} at correlation {gait.Corr:F3} over {legs.Loop} frames");
            Check(!(still.Moves(minSwing) && still.Corr < -0.85),
                $"CONTROL {who}: the same measurement on a static pose fails (swing {still.SwingL:F0}/{still.SwingR:F0}, correlation {still.Corr:F3} is residue)");
            double step = legs.MeanStep(Enumerable.Range(0, legs.Loop).ToArray());
            var rng = new Random(7);
            double shuffled = legs.MeanStep(Enumerable.Range(0, legs.Loop).OrderBy(_ => rng.Next()).ToArray());
            Check(step < shuffled / 2, $"{who}: consecutive frames are continuous ({step:F0} units per frame; SHUFFLED order {shuffled:F0}, must be over twice that)");
        }

        // ---- frame-exact, the way __fixsfsi truncates ----
        var body = hTracks.Single(t => hModel.Meshes[t.Node].Name.Equals("handyman", StringComparison.OrdinalIgnoreCase));
        bool Same(Vector3[] a, Vector3[] b) => a.Zip(b).All(p => p.First == p.Second);
        Check(Same(body.Sample(3.0f), body.Sample(3.999f)) && !Same(body.Sample(3.999f), body.Sample(4.0f)),
            "frame 3.999 draws frame 3 exactly and 4.0 changes it: truncated, no interpolation");
        var lerp = new Vector3[body.Groups];
        for (int g = 0; g < body.Groups; g++) lerp[g] = Vector3.Lerp(body.Position(3, g), body.Position(4, g), 0.5f);
        Check(!Same(lerp, body.Sample(3.0f)), "MUTATION: an interpolating sampler would differ at 3.5 (the check above can see it)");
        Check(Same(body.Sample(hRec.DurationFrames), Enumerable.Range(0, body.Groups).Select(g => body.Position(body.Frames - 1, g)).ToArray()),
            $"frame == duration ({hRec.DurationFrames}) reads the extra last frame: the held pose 0x1ad268 poses");
        Check(Throws(() => body.Sample(body.Frames)) && Throws(() => body.Sample(-0.5f)),
            "a frame past the table or before it is refused, not wrapped or clamped");
    }

    static bool Throws(Action a) { try { a(); return false; } catch (ArgumentOutOfRangeException) { return true; } }

    /// <summary>The leg measurement: on the lowest drawn mesh (hide-list props excluded), the groups
    /// in the lowest third of its frame-0 height (Y is up), split left/right at their median X, and
    /// the mean Z (forward) of each side per frame over one loop (the closure frame excluded).</summary>
    sealed class Legs
    {
        public Aps.BakedVertexTrack Track;
        public int[] Left, Right;
        public int Loop;

        public static Legs Of(Aps aps, Aps.Record rec)
        {
            var hidden = AnimationNodeVisibility.IndexNodes(aps, rec).ToHashSet();
            var t = aps.BakedVertexTracks(rec).Where(x => !hidden.Contains(x.Node))
                       .OrderBy(x => Enumerable.Range(0, x.Groups).Min(g => x.Position(0, g).Y)).First();
            var p0 = Enumerable.Range(0, t.Groups).Select(g => t.Position(0, g)).ToArray();
            float lo = p0.Min(p => p.Y), hi = p0.Max(p => p.Y);
            var low = Enumerable.Range(0, t.Groups).Where(g => p0[g].Y < lo + (hi - lo) / 3).ToArray();
            float mid = low.Select(g => p0[g].X).OrderBy(x => x).ElementAt(low.Length / 2);
            return new Legs { Track = t, Loop = t.Frames - 1,
                              Left = low.Where(g => p0[g].X < mid).ToArray(), Right = low.Where(g => p0[g].X >= mid).ToArray() };
        }

        public readonly record struct Gait(double SwingL, double SwingR, double Corr)
        {
            public bool Moves(double min) => SwingL > min && SwingR > min;
        }

        public Gait Measure(Func<int, int> frameOf)
        {
            double[] Side(int[] gs) => Enumerable.Range(0, Loop).Select(f => gs.Average(g => (double)Track.Position(frameOf(f), g).Z)).ToArray();
            var l = Side(Left); var r = Side(Right);
            return new Gait(l.Max() - l.Min(), r.Max() - r.Min(), Pearson(l, r));
        }

        public double MeanStep(int[] order) => Enumerable.Range(0, order.Length - 1).Average(i =>
            Enumerable.Range(0, Track.Groups).Average(g => (double)(Track.Position(order[i + 1], g) - Track.Position(order[i], g)).Length()));

        static double Pearson(double[] a, double[] b)
        {
            double ma = a.Average(), mb = b.Average(), ab = 0, aa = 0, bb = 0;
            for (int i = 0; i < a.Length; i++) { ab += (a[i] - ma) * (b[i] - mb); aa += (a[i] - ma) * (a[i] - ma); bb += (b[i] - mb) * (b[i] - mb); }
            return ab / Math.Sqrt(aa * bb);
        }
    }
}
