using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TPWPS2Viewer;

/// <summary>⭐⭐ THE PERFORMANCE BENCHMARK. Master, 2026-09-28: "perf isnt horrible, but its
/// definitely been creeping slightly as time goes on".
///
/// ⭐ "Creeping" is two different faults wearing one word, and they want opposite responses:
/// <list type="bullet">
/// <item>COST accumulating across the project's history -- every feature adds per-frame work.
/// Expected; answered by profiling and optimising.</item>
/// <item>DEGRADATION within a single run -- something growing or leaking while you play. A bug;
/// gets worse the longer the session, and no amount of optimising fixes it.</item>
/// </list>
/// A benchmark that reports one number cannot tell them apart, so this reports a TREND: the same
/// measurements over the first and last quarter of one steady-state run.
///
/// ⚠ FRAME TIME IN MILLISECONDS, not fps. fps is an average of a reciprocal -- 60 to 55 and 30 to
/// 28 are the same proportional loss but look wildly different -- and it hides spikes entirely.
/// Milliseconds are linear and two of them can be subtracted.
///
/// ⚠⚠ IT MUST BE ABLE TO SAY "NOTHING IS DEGRADING". A benchmark run to confirm a suspicion will
/// confirm it; the verdict below is stated against explicit thresholds, and a flat result prints
/// as flat.</summary>
public partial class Viewer
{
    sealed class BenchSample
    {
        public double T;             // seconds since the benchmark began
        public double FrameMs;       // from the frame's own delta
        public double EngineFps;     // the engine's own count -- a SECOND opinion on FrameMs
        public double ProcessMs;     // the CPU side of it (_Process)
        public double PhysicsMs;
        public long Nodes, Orphans, Objects, Resources;
        public long DrawCalls, Primitives;
        public long StaticMemKb;
    }

    readonly List<BenchSample> _bench = new();
    double _benchFor, _benchElapsed, _benchNext;
    long _benchFrames;
    bool _benchRunning;

    /// <summary>⚠ Discarded before sampling starts: the first seconds of any run are loading,
    /// shader compilation and the first-frame cost of everything, and averaging them in makes the
    /// beginning of every run look slow and so manufactures a "it gets faster" trend.</summary>
    const double BenchWarmup = 3.0;
    const double BenchInterval = 0.25;

    void StartBenchmark(double seconds)
    {
        _benchFor = seconds;
        _benchElapsed = 0;
        _benchNext = BenchWarmup;
        _benchRunning = true;
        _benchFrames = 0;
        _bench.Clear();
        GD.Print($"[bench] running {seconds:F0}s (discarding the first {BenchWarmup:F0}s as warm-up), "
               + $"sampling every {BenchInterval:F2}s");
    }

    void TickBenchmark(double delta)
    {
        if (!_benchRunning) return;
        _benchElapsed += delta;
        if (_benchElapsed < BenchWarmup) return;
        _benchFrames++;
        if (_benchElapsed >= _benchFor) { _benchRunning = false; ReportBenchmark(); return; }
        _benchNext -= delta;
        if (_benchNext > 0) return;
        _benchNext = BenchInterval;

        double M(Performance.Monitor m) => Performance.GetMonitor(m);
        _bench.Add(new BenchSample
        {
            T = _benchElapsed - BenchWarmup,
            // ⚠ The frame's own delta, not 1/fps: fps is already smoothed by the engine and would
            // flatten exactly the spikes worth seeing.
            FrameMs = delta * 1000.0,
            EngineFps = Engine.GetFramesPerSecond(),
            ProcessMs = M(Performance.Monitor.TimeProcess) * 1000.0,
            PhysicsMs = M(Performance.Monitor.TimePhysicsProcess) * 1000.0,
            Nodes = (long)M(Performance.Monitor.ObjectNodeCount),
            Orphans = (long)M(Performance.Monitor.ObjectOrphanNodeCount),
            Objects = (long)M(Performance.Monitor.ObjectCount),
            Resources = (long)M(Performance.Monitor.ObjectResourceCount),
            DrawCalls = (long)M(Performance.Monitor.RenderTotalDrawCallsInFrame),
            Primitives = (long)M(Performance.Monitor.RenderTotalPrimitivesInFrame),
            StaticMemKb = (long)(M(Performance.Monitor.MemoryStatic) / 1024.0),
        });
    }

    static double Median(IEnumerable<double> xs)
    {
        var a = xs.OrderBy(x => x).ToArray();
        return a.Length == 0 ? 0 : a.Length % 2 == 1 ? a[a.Length / 2]
                                                    : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2.0;
    }

    static double Pct(IEnumerable<double> xs, double p)
    {
        var a = xs.OrderBy(x => x).ToArray();
        if (a.Length == 0) return 0;
        int i = Math.Clamp((int)Math.Round(p * (a.Length - 1)), 0, a.Length - 1);
        return a[i];
    }

    void ReportBenchmark()
    {
        if (_bench.Count < 8) { GD.Print($"[bench] only {_bench.Count} samples -- too few to judge"); return; }

        // ⚠⚠ CALIBRATE BEFORE BELIEVING ANY OF IT. The first run reported a 1.27 ms frame and a
        // 19 ms _Process, which cannot both be true. Three independent measures of the same thing
        // are printed side by side, and the report says plainly when they disagree rather than
        // picking the flattering one:
        //   delta        -- the frame's own delta, what the game is stepped by
        //   engine fps   -- the engine's own counter, converted
        //   wall clock   -- frames actually counted over real elapsed seconds
        double medDelta = Median(_bench.Select(s => s.FrameMs));
        double medEngine = Median(_bench.Select(s => s.EngineFps));
        double engineMs = medEngine > 0 ? 1000.0 / medEngine : 0;
        double wallMs = _benchFrames > 0 ? (_bench[^1].T - _bench[0].T) * 1000.0 / _benchFrames : 0;
        GD.Print($"[bench] CALIBRATION  delta {medDelta:F2} ms | engine {engineMs:F2} ms ({medEngine:F0} fps) "
               + $"| wall {wallMs:F2} ms over {_benchFrames} frames");
        double spread = new[] { medDelta, engineMs, wallMs }.Where(x => x > 0).DefaultIfEmpty(0).Max()
                      - new[] { medDelta, engineMs, wallMs }.Where(x => x > 0).DefaultIfEmpty(0).Min();
        GD.Print(spread <= Math.Max(0.5, medDelta * 0.25)
            ? "[bench] calibration OK: the three agree, so frame time is trustworthy"
            : $"[bench] ⚠ CALIBRATION DISAGREES by {spread:F2} ms -- one of these instruments is "
              + "lying and the frame-time figures below should NOT be quoted until it is found");
        int q = _bench.Count / 4;
        var first = _bench.Take(q).ToList();
        var last = _bench.Skip(_bench.Count - q).ToList();

        // ⚠ MEDIAN, not mean: one stall from a background process on a shared machine moves a
        // mean and leaves a median alone, and this box is shared.
        double f0 = Median(first.Select(s => s.FrameMs)), f1 = Median(last.Select(s => s.FrameMs));
        double p95 = Pct(_bench.Select(s => s.FrameMs), 0.95);
        double allMed = Median(_bench.Select(s => s.FrameMs));

        GD.Print("[bench] ================ RESULT ================");
        GD.Print($"[bench] samples {_bench.Count} over {_bench[^1].T:F1}s");
        GD.Print($"[bench] frame time  median {allMed:F2} ms ({(allMed > 0 ? 1000.0 / allMed : 0):F0} fps)"
               + $"   95th pct {p95:F2} ms");
        // ⚠ NOT QUOTED AS A PER-FRAME COST. Godot's TIME_PROCESS does not reconcile with the
        // three agreeing frame-time measures above (19 ms against a 1.3 ms frame), so whatever it
        // counts, it is not this frame's process time. Printed raw, labelled unreconciled, and
        // never turned into a percentage of the frame.
        GD.Print($"[bench]   TIME_PROCESS monitor {Median(_bench.Select(s => s.ProcessMs)):F2} "
               + $"(⚠ unreconciled with frame time -- do not read as per-frame ms)");
        GD.Print($"[bench]   draw calls {Median(_bench.Select(s => (double)s.DrawCalls)):F0}"
               + $"   primitives {Median(_bench.Select(s => (double)s.Primitives)):F0}");

        // ⭐ THE TREND -- the whole reason this exists.
        double drift = f1 - f0, driftPct = f0 > 0 ? drift / f0 * 100.0 : 0;
        long n0 = first[0].Nodes, n1 = last[^1].Nodes;
        long o0 = first[0].Orphans, o1 = last[^1].Orphans;
        long ob0 = first[0].Objects, ob1 = last[^1].Objects;
        // ⚠⚠ MEMORY SAWTOOTHS, SO ENDPOINTS ARE MEANINGLESS. The first version of this compared
        // the first sample to the last and declared a leak -- both had landed near a peak of the
        // GC cycle, which on this run swings 188 MB every few seconds. A real leak raises the
        // FLOOR the collector returns to; a sawtooth does not. So the test is the floor per third.
        long floor0 = first.Min(x => x.StaticMemKb);
        long floorMid = _bench.Skip(_bench.Count / 3).Take(_bench.Count / 3).Min(x => x.StaticMemKb);
        long floorEnd = last.Min(x => x.StaticMemKb);
        long m0 = floor0, m1 = floorEnd;
        // ⭐ And the number that is actually actionable: how much garbage is made per second and
        // per frame. On a fast machine a huge churn is invisible in frame time and still the thing
        // that will hurt on a slower one.
        long alloc = 0;
        for (int i = 1; i < _bench.Count; i++)
            if (_bench[i].StaticMemKb > _bench[i - 1].StaticMemKb)
                alloc += _bench[i].StaticMemKb - _bench[i - 1].StaticMemKb;
        double span = Math.Max(0.001, _bench[^1].T - _bench[0].T);
        double allocMbS = alloc / 1024.0 / span;
        double allocKbFrame = _benchFrames > 0 ? (double)alloc / _benchFrames : 0;

        GD.Print($"[bench] TREND first quarter {f0:F2} ms -> last quarter {f1:F2} ms "
               + $"({drift:+0.00;-0.00;0.00} ms, {driftPct:+0.0;-0.0;0.0}%)");
        GD.Print($"[bench]   nodes {n0} -> {n1} ({n1 - n0:+#;-#;0})   orphans {o0} -> {o1} ({o1 - o0:+#;-#;0})");
        GD.Print($"[bench]   objects {ob0} -> {ob1} ({ob1 - ob0:+#;-#;0})");
        GD.Print($"[bench]   GC FLOOR by third: {floor0} -> {floorMid} -> {floorEnd} KB "
               + $"(a leak raises the floor; a sawtooth does not)");
        GD.Print($"[bench]   ALLOCATION {allocMbS:F0} MB/s = {allocKbFrame:F0} KB per frame");

        // ⚠ Stated against thresholds decided BEFORE the numbers were read, so a flat run reports
        // as flat rather than as whatever the reader hoped to find. 5% of frame time is below the
        // run-to-run noise on a shared machine; a node count that only ever grows is not noise.
        bool timeCrept = driftPct > 5.0;
        bool nodesGrew = n1 - n0 > 16;
        // ⚠ Against the FLOOR, and generous: 32 MB, because the floor itself is sampled off a
        // sawtooth and a few MB of jitter there is not evidence of anything.
        bool memGrew = m1 - m0 > 32768;
        GD.Print("[bench] ---------------- VERDICT ----------------");
        GD.Print(timeCrept
            ? $"[bench] FRAME TIME DEGRADED within the run: +{driftPct:F1}% over {_bench[^1].T:F0}s"
            : $"[bench] frame time did NOT degrade within the run ({driftPct:+0.0;-0.0;0.0}%, under the 5% noise floor)");
        GD.Print(nodesGrew || memGrew
            ? $"[bench] LEAKING: nodes {n1 - n0:+#;-#;0}, orphans {o1 - o0:+#;-#;0}, and the GC "
              + $"floor rose {m1 - m0:+#;-#;0} KB -- the collector is not getting it back"
            : $"[bench] NOT leaking: nodes flat and the GC floor returns to {floorEnd} KB");
        GD.Print(allocMbS > 20
            ? $"[bench] ⚠ HIGH CHURN: {allocMbS:F0} MB/s ({allocKbFrame:F0} KB/frame) of garbage. "
              + "Invisible in frame time on a fast machine; it is what costs on a slow one, and it "
              + "is what grows as per-frame allocations are added feature by feature."
            : $"[bench] allocation is modest ({allocMbS:F0} MB/s)");
        if (!timeCrept && !nodesGrew && !memGrew)
            GD.Print("[bench] ⭐ So any creep master is seeing is COST ACROSS THE PROJECT, not "
                   + "degradation within a session -- profile the frame, do not hunt a leak.");
        GD.Print("[bench] ========================================");

        // The raw series, so a reader can see the shape rather than trust the summary.
        GD.Print("[bench] t,frame_ms,process_ms,nodes,orphans,objects,draw_calls,static_kb");
        foreach (var s in _bench)
            GD.Print($"[bench] {s.T:F2},{s.FrameMs:F2},{s.ProcessMs:F2},{s.Nodes},{s.Orphans},"
                   + $"{s.Objects},{s.DrawCalls},{s.StaticMemKb}");
        GetTree().Quit();
    }
}
