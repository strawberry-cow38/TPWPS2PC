using TPW.PS2.Data;

/// <summary>The SFX event state machine — see <see cref="SfxEventMachine"/>. Checked against the
/// owner's own maps, not fixtures, because the shape being modelled is the disc's.</summary>
static class SfxGraphChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string m) => check(ok, "sfx graph: " + m);

        SfxMap Map(string path)
        {
            var f = disc.Files().Single(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            return new SfxMap(disc.Read(f.Extent, f.Size));
        }
        var kids = Map("/AUDIO/GLOBAL/KIDSSFX.MAP");

        // ---- what is and is not a graph -------------------------------------------------
        Check(!SfxEventMachine.IsGraph(kids.ById[0x69]), "the unlevelled one-shot 0x69 is NOT a graph -- one set, no links");
        Check(SfxEventMachine.IsGraph(kids.ById[0x48]), "the looping scream 0x48 IS a graph");
        Check(SfxEventMachine.Start(kids.ById[0x48]) == 0, "playback starts at the event's first set");

        // ---- ⭐⭐ THE LEVEL ACTUALLY STEERS IT. 0x48's four bands are disjoint, so each level
        // lands in exactly one tier and the answer is deterministic.
        var ev = kids.ById[0x48];
        var rng = new SfxEventMachine.Rng(1);
        int? At(int level) => SfxEventMachine.Next(ev, 0, level, rng);
        Check(At(10) == 0 && At(30) == 1 && At(60) == 2 && At(90) == 3,
              $"level picks the tier: 10->{At(10)} 30->{At(30)} 60->{At(60)} 90->{At(90)} (want 0,1,2,3)");
        // ⭐ THE CONTROL THAT REJECTS "the level does nothing": two levels must disagree, and the
        // clip pools they land in must actually differ.
        int lo = At(10) ?? -1, hi = At(90) ?? -2;
        Check(lo != hi, "a quiet level and a loud one choose DIFFERENT sets");
        Check(!ev.Sets[lo].Clips.Select(c => c.Sound).ToHashSet()
                .SetEquals(ev.Sets[hi].Clips.Select(c => c.Sound)), "and those sets hold different clips");

        // ---- the loop ends when no band contains the value ------------------------------
        Check(SfxEventMachine.Next(ev, 0, 200, rng) == null, "a value outside every band STOPS the loop");
        Check(SfxEventMachine.Next(ev, 0, -5, rng) == null, "and so does one below every band");

        // ---- it really does loop --------------------------------------------------------
        int cur = 0; bool everNull = false;
        for (int i = 0; i < 200 && !everNull; i++)
        {
            var n = SfxEventMachine.Next(ev, cur, 60, rng);
            if (n == null) everNull = true; else cur = n.Value;
        }
        Check(!everNull, "held at one level it runs forever -- 200 steps, never stops");

        // ---- ⭐⭐ THE WEIGHTED DRAW, on the one event where it decides anything ----------
        // 0x47 has 37 sets and several links per set whose bands OVERLAP. A "take the first
        // matching link" implementation -- which is what I was about to write -- would return the
        // same target every time. The console draws among all matches, so this MUST spread.
        var big = kids.ById[0x47];
        Check(big.Sets.Count > 30, $"0x47 is the big one ({big.Sets.Count} sets)");
        int from = Enumerable.Range(0, big.Sets.Count).First(i =>
            big.Sets[i].Links.Count(l => l.Low <= 40 && 40 <= l.High) > 1);
        var seen = new HashSet<int>();
        var r2 = new SfxEventMachine.Rng(12345);
        for (int i = 0; i < 400; i++)
            if (SfxEventMachine.Next(big, from, 40, r2) is { } t) seen.Add(t);
        Check(seen.Count > 1, $"overlapping bands spread across {seen.Count} targets -- NOT first-match");

        // ---- the generator is the console's, exactly ------------------------------------
        var a = new SfxEventMachine.Rng(1); var b = new SfxEventMachine.Rng(1);
        Check(a.Next() == b.Next(), "the same seed gives the same draw");
        var c1 = new SfxEventMachine.Rng(1); var c2 = new SfxEventMachine.Rng(2);
        Check(c1.Next() != c2.Next(), "CONTROL: different seeds do not");

        // ---- and the census the whole thing rests on ------------------------------------
        int graphs = 0, cyclic = 0, oob = 0, events = 0, oneWay = 0;
        foreach (var f in disc.Files().Where(x => x.Path.ToUpperInvariant().EndsWith("SFX.MAP")))
        {
            SfxMap m;
            try { m = new SfxMap(disc.Read(f.Extent, f.Size)); } catch { continue; }
            foreach (var e in m.Events)
            {
                events++;
                if (!SfxEventMachine.IsGraph(e)) continue;
                graphs++;
                foreach (var st in e.Sets)
                    foreach (var l in st.Links)
                        if (SfxEventMachine.TargetIndex(e, l) == null) oob++;
                // ⭐ DOES IT SUSTAIN? Reach everything from set 0, then ask whether any reached
                // set can get back to itself. ⚠ The first version of this asked whether set 0 is
                // reachable again, which is a DIFFERENT and stronger question -- it failed 67/68,
                // and the one exception turned out to be a real shape rather than a bug.
                var vis = new HashSet<int> { 0 }; var stack = new Stack<int>(new[] { 0 });
                while (stack.Count > 0)
                {
                    int n = stack.Pop();
                    foreach (var l in e.Sets[n].Links)
                        if (SfxEventMachine.TargetIndex(e, l) is { } t && vis.Add(t)) stack.Push(t);
                }
                bool sustains = false;
                foreach (int start in vis)
                {
                    var st2 = new Stack<int>(); var seen2 = new HashSet<int>();
                    foreach (var l in e.Sets[start].Links)
                        if (SfxEventMachine.TargetIndex(e, l) is { } t) st2.Push(t);
                    while (st2.Count > 0)
                    {
                        int n = st2.Pop();
                        if (n == start) { sustains = true; break; }
                        if (!seen2.Add(n)) continue;
                        foreach (var l in e.Sets[n].Links)
                            if (SfxEventMachine.TargetIndex(e, l) is { } t) st2.Push(t);
                    }
                    if (sustains) break;
                }
                if (sustains) cyclic++;
                if (!e.Sets[0].Links.Any(l => SfxEventMachine.TargetIndex(e, l) == 0)) oneWay++;
            }
        }
        Check(graphs > 60, $"{events} events on the disc, {graphs} are graphs");
        // ⭐⭐ THE REAL INVARIANT: every graph SUSTAINS -- somewhere in it a set can repeat. That
        // is what makes it a loop rather than a chain, and it is what the port has to honour.
        Check(cyclic == graphs, $"every graph sustains -- some set can repeat ({cyclic}/{graphs})");
        // ⚠ NAME WHAT IT MEASURED: this counts graphs whose FIRST SET does not link to itself,
        // which is not the same as "never returns to set 0" -- that stricter question has exactly
        // one exception. Stated as the former because that is what the loop above computes.
        Check(oneWay > 0, $"{oneWay} of {graphs} graphs have a first set that does not repeat itself");
        // ⭐⭐ And the shape master's ear found before the data did, pinned to the one event that
        // has it: GLOBAL/RIDESFX.MAP event 69 is INTRO -> sustaining BODY -> OUTRO. set0 plays
        // once and always advances; set1 loops on itself while parameter 22 is 0..50; above 50 it
        // exits to set2, which has NO links and therefore stops. A start, a middle and an end.
        var rides = Map("/AUDIO/GLOBAL/RIDESFX.MAP");
        var chain = rides.ById[69];
        Check(chain.Sets.Count == 3 && chain.Word12 == 22
           && chain.Sets[0].Links.Count == 1 && SfxEventMachine.TargetIndex(chain, chain.Sets[0].Links[0]) == 1
           && chain.Sets[1].Links.Any(l => SfxEventMachine.TargetIndex(chain, l) == 1)
           && chain.Sets[1].Links.Any(l => SfxEventMachine.TargetIndex(chain, l) == 2)
           && chain.Sets[2].Links.Count == 0,
              "RIDESFX event 69 is intro -> sustaining body -> outro, and the outro has no way out");
        Check(SfxEventMachine.Next(chain, 1, 20, new SfxEventMachine.Rng(7)) == 1
           && SfxEventMachine.Next(chain, 1, 80, new SfxEventMachine.Rng(7)) == 2,
              "its body sustains while the parameter is low and exits to the outro when it rises");
        Check(SfxEventMachine.Next(chain, 2, 50, new SfxEventMachine.Rng(7)) == null,
              "and the outro ends -- no links, so the machine stops");
        // ⭐⭐ THE PROOF THE TARGETS ARE 1-BASED IN THE FILE: read as 0-based, the last link of
        // every four-set event points past the end. Zero out-of-range means the -1 is right.
        Check(oob == 0, $"no link target falls outside its event ({oob} would mean the index base is wrong)");

        // ---- ⚠⚠ WHICH GRAPHS DEPEND ON A PARAMETER WE CANNOT SOURCE? -------------------
        // Only parameter 6 has a value in this port (the scream level). A graph whose links all
        // span [0..100] runs correctly whatever the parameter reads, because every link always
        // matches. The ones with NARROW bands are the ones a placeholder 0 would misdirect, so
        // they are counted here rather than left as a footnote -- if the number grows, somebody
        // added a graph whose driver is still unknown.
        int blind = 0, blindNarrow = 0;
        var blindParams = new SortedSet<int>();
        foreach (var f in disc.Files().Where(x => x.Path.ToUpperInvariant().EndsWith("SFX.MAP")))
        {
            SfxMap m;
            try { m = new SfxMap(disc.Read(f.Extent, f.Size)); } catch { continue; }
            foreach (var e in m.Events)
            {
                if (!SfxEventMachine.IsGraph(e) || e.Word12 == RideScreams.LevelSelector) continue;
                blind++;
                if (e.Sets.Any(st => st.Links.Any(l => l.Low > 0 || l.High < 100)))
                { blindNarrow++; blindParams.Add(e.Word12); }
            }
        }
        Check(blindNarrow < blind, $"{blind - blindNarrow} of {blind} graphs on an unsourced parameter have full-range links "
            + "-- those run correctly regardless of its value");
        Check(blindNarrow > 0, $"⚠ {blindNarrow} have NARROW bands, so their behaviour depends on a value we do not source "
            + $"(parameters {string.Join(",", blindParams)})");
        // ⭐⭐ BUT FOR PARAMETER 18 -- the ambient beds, the biggest block of graphs -- 0 looks
        // like the real value rather than a placeholder: nothing on the disc writes 18, and the
        // value gates only a handful of links anyway. Measured rather than asserted from the
        // absence alone, because an absence is only as good as the search behind it.
        int at0 = 0, at50 = 0, p18 = 0;
        foreach (var f in disc.Files().Where(x => x.Path.ToUpperInvariant().EndsWith("SFX.MAP")))
        {
            SfxMap m;
            try { m = new SfxMap(disc.Read(f.Extent, f.Size)); } catch { continue; }
            foreach (var e in m.Events.Where(x => SfxEventMachine.IsGraph(x) && x.Word12 == 18))
            {
                p18++;
                foreach (var st in e.Sets)
                {
                    at0 += st.Links.Count(l => l.Low <= 0 && 0 <= l.High);
                    at50 += st.Links.Count(l => l.Low <= 50 && 50 <= l.High);
                }
            }
        }
        Check(p18 > 25, $"parameter 18 drives {p18} graphs -- every park's ambient bed");
        Check(at0 >= at50, $"and value 0 reaches MORE of their links than 50 does ({at0} vs {at50}) "
            + "-- so reading 0 gives the fullest ambience, not a stuck branch");

        // ---- ⭐ the weights, and the assumption the uniform draw rests on ----------------
        // Every set of an event carries the same value, and it is an equal share of 0xFFFF. That
        // is why the weighted draw comes out uniform on this disc. If a map ever breaks this the
        // weighting starts to matter and someone should look -- so it is asserted, not assumed.
        int equalWithin = 0, sumsToWord = 0, graphs2 = 0, decreasing = 0;
        foreach (var f in disc.Files().Where(x => x.Path.ToUpperInvariant().EndsWith("SFX.MAP")))
        {
            SfxMap m;
            try { m = new SfxMap(disc.Read(f.Extent, f.Size)); } catch { continue; }
            foreach (var e in m.Events)
            {
                if (!SfxEventMachine.IsGraph(e)) continue;
                graphs2++;
                var w = e.Sets.Select(x => x.Weight).ToArray();
                if (w.Distinct().Count() == 1) equalWithin++;
                if (Math.Abs((long)w.Sum(x => (long)x) - 0xFFFF) <= w.Length) sumsToWord++;
                if (w.Zip(w.Skip(1)).Any(p2 => p2.Second < p2.First)) decreasing++;
            }
        }
        // ⚠ Both of these started life as "all of them" and were WRONG -- 62 of 68 and 67 of 68.
        // Stated as the split, because the exceptions are the interesting part.
        Check(equalWithin > graphs2 * 3 / 4, $"most graphs share their weight equally ({equalWithin}/{graphs2}) -- those draw uniformly");
        Check(equalWithin < graphs2, $"but {graphs2 - equalWithin} are genuinely WEIGHTED -- the draw is not decorative");
        Check(sumsToWord >= graphs2 - 1, $"and the weights share out 0xFFFF ({sumsToWord}/{graphs2})");
        // ⭐⭐ The worked example, pinned: a park ambient bed whose links are ALL [0..100], so every
        // link always matches and the weight is the ONLY thing choosing. 29126:21845:14563 is
        // about 4:3:2 -- authored probabilities for background variation, which is what stops a
        // park's ambience repeating. Implementing "first match" would play set0 forever.
        var amb = Map("/AUDIO/FANTASY/PARK1/AMBSFX.MAP").ById[180];
        Check(amb.Sets.Count == 3 && amb.Sets.All(x => x.Links.All(l => l.Low == 0 && l.High == 100)),
              "FANTASY ambient 180 has three sets whose links all span the whole range");
        Check(amb.Sets[0].Weight > amb.Sets[1].Weight && amb.Sets[1].Weight > amb.Sets[2].Weight,
              $"and they are weighted {amb.Sets[0].Weight}:{amb.Sets[1].Weight}:{amb.Sets[2].Weight} -- the weight alone decides");
        var spread = new Dictionary<int,int>();
        var r3 = new SfxEventMachine.Rng(99);
        for (int i = 0; i < 3000; i++)
            if (SfxEventMachine.Next(amb, 0, 50, r3) is { } t) spread[t] = spread.GetValueOrDefault(t) + 1;
        Check(spread.Count == 2 && spread[1] > spread[2],
              $"and drawing 3000 times from set0 favours set1 over set2 ({spread.GetValueOrDefault(1)}:{spread.GetValueOrDefault(2)}) as the weights say");
        // ⚠⚠ THE CONTROL AGAINST "DIFFERENCE IT": FUN_0024B8D0 has a path that treats +0x1e as a
        // CUMULATIVE series. These files are not that -- a running total cannot go down, and some
        // of these do. Differencing would give set 0 all the weight and every draw would pick it.
        Check(decreasing > 0, $"{decreasing} graphs have a DECREASING weight series -- proof the stored value is not cumulative");
    }
}
