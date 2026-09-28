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
    // ============================ allocation attribution ============================

    /// <summary>⭐ WHERE the garbage comes from, exactly. `GC.GetAllocatedBytesForCurrentThread`
    /// is a precise byte counter, not a sampling profiler -- bracketing a call gives its real
    /// allocation, so this attributes the churn instead of guessing at likely-looking code.
    ///
    /// ⚠ Off unless `--alloc-probe` is passed: the counter itself is cheap but the dictionary
    /// work per frame is not free, and a profiler that changes what it measures is worthless.</summary>
    bool _allocProbe;
    readonly Dictionary<string, long> _allocBy = new();
    long _allocFrames, _allocFrameTotal;

    /// <summary>⚠⚠ A STACK, NOT A SINGLE MARK. The first version kept one `_allocMark`, so the
    /// moment a bracketed section contained another one the inner Begin overwrote the outer's
    /// start: StepPark then reported 0.83 KB while its own children summed to 13, and a sibling
    /// measured after it read 0.00 where it had read 5.17 the run before. Numbers that move for
    /// no reason are the instrument, not the subject.
    ///
    /// ⭐ With a stack an inner section is correct AND the outer still sees the whole span,
    /// including its children -- so a parent's figure can be checked against the sum of its
    /// children, which is a free consistency test on the profiler itself.</summary>
    readonly Stack<long> _allocMarks = new();

    void AllocBegin() { if (_allocProbe) _allocMarks.Push(GC.GetAllocatedBytesForCurrentThread()); }

    /// <summary>⚠⚠ THE SHALLOWEST DEPTH each name was ever closed at. Without it the
    /// "everything else" line LIED: `attributed` summed StepPark AND PresentNativeBus AND TickPark,
    /// but the last two are INSIDE the first, so their bytes were counted twice and the unattributed
    /// remainder came out 22 KB/frame when it was really 31. An instrument whose own gap is
    /// under-reported sends the next reader to look in the wrong place -- and the gap is the single
    /// most useful number in the table, because it is the part nobody has explained yet.
    ///
    /// ⚠ MINIMUM, not last: a section that is top-level on some frames and nested on others is
    /// top-level for the purpose of the total, or its bytes would go unaccounted entirely.</summary>
    readonly Dictionary<string, int> _allocDepth = new();

    void AllocEnd(string name)
    {
        if (!_allocProbe || _allocMarks.Count == 0) return;
        long d = GC.GetAllocatedBytesForCurrentThread() - _allocMarks.Pop();
        int depth = _allocMarks.Count;              // 0 = not inside another bracket
        _allocBy.TryGetValue(name, out long had);
        _allocBy[name] = had + d;
        if (!_allocDepth.TryGetValue(name, out int seen) || depth < seen) _allocDepth[name] = depth;
    }

    /// <summary>⭐⭐ INSIDE `_Process` VERSUS EVERYTHING ELSE ON THE MAIN THREAD. Every bracket
    /// added inside `_Process` came back at ~0 while 45% of the frame stayed unexplained, which
    /// says the bytes are not in the body at all -- they are in what the engine does around it
    /// (`_Draw`, `_Input`, signal dispatch, the marshalling of every property we set). Guessing
    /// which would have cost a build cycle per guess; this splits the frame in two and says which
    /// half to look in.
    ///
    /// ⚠ IMMUNE TO `_Process`'s EARLY RETURNS, which is what broke the first whole-frame bracket.
    /// The outside-gap is only accumulated when the PREVIOUS frame actually reached the bottom, so
    /// a frame that returned early contributes to neither side rather than dumping its whole cost
    /// into "outside". Both frame counts are printed: if they disagree, the instrument says so
    /// itself instead of quietly reporting a number for fewer frames than it claims.</summary>
    long _allocTop, _allocBottom, _allocInside, _allocOutside, _allocInFrames, _allocOutFrames;
    bool _allocReachedBottom;

    /// <summary>⭐⭐ CUMULATIVE MARKS DOWN `_Process`, to localise cost that is inside the method
    /// but outside every bracket. Brackets answer "what does this call cost"; they cannot answer
    /// "which statement did I forget to bracket", and after bracketing what looked like every
    /// statement the gap was still 60% of the frame -- which means a branch I believed was skipped
    /// is not. The step between two consecutive marks is the cost of the span between them, so the
    /// gap has nowhere left to hide.
    ///
    /// ⚠ Each label keeps its OWN frame count, because a mark past an early return is reached
    /// fewer times than one before it, and dividing them all by the same N would invent a
    /// difference. A count that differs from the first mark's is printed, not smoothed over.</summary>
    readonly Dictionary<string, long> _allocAt = new(), _allocAtN = new();
    readonly List<string> _allocMarkOrder = new();

    void AllocMark(string label)
    {
        if (!_allocProbe || _allocTop == 0) return;
        long d = GC.GetAllocatedBytesForCurrentThread() - _allocTop;
        _allocAt.TryGetValue(label, out long had); _allocAt[label] = had + d;
        _allocAtN.TryGetValue(label, out long n); _allocAtN[label] = n + 1;
        if (!_allocMarkOrder.Contains(label)) _allocMarkOrder.Add(label);
    }

    void AllocFrameTop()
    {
        if (!_allocProbe) return;
        // ⚠ Hygiene: an early return from inside a bracketed section leaves marks on the stack,
        // and a stale mark would make the NEXT frame's section read as the span between frames.
        _allocMarks.Clear();
        long now = GC.GetAllocatedBytesForCurrentThread();
        if (_allocReachedBottom) { _allocOutside += now - _allocBottom; _allocOutFrames++; }
        _allocReachedBottom = false;
        _allocTop = now;
    }

    void AllocFrameBottom()
    {
        if (!_allocProbe || _allocTop == 0) return;
        long now = GC.GetAllocatedBytesForCurrentThread();
        _allocInside += now - _allocTop; _allocInFrames++;
        _allocBottom = now; _allocReachedBottom = true;
    }

    /// <summary>⚠⚠ THE WHOLE-FRAME TOTAL IS MEASURED FROM THE SAMPLER, NOT FROM A PAIR OF
    /// BRACKETS AROUND `_Process`. The bracketed version reported MINUS 27 KB/frame, which is
    /// impossible: `_Process` has early returns -- the shot paths, the laptop film, the quit --
    /// so the opening bracket ran, the frame left, the closing bracket never did, and the next
    /// frame subtracted again.
    ///
    /// Differencing the counter between two SAMPLES cannot be broken that way: whatever happened
    /// in between, including frames that returned early, is inside the difference.</summary>
    long _allocLastCounter;
    long _allocFramesAtLastSample;

    void AllocSample(long framesNow)
    {
        if (!_allocProbe) return;
        long now = GC.GetAllocatedBytesForCurrentThread();
        if (_allocLastCounter == 0)
        {
            // ⚠⚠ EVERY ACCUMULATOR STARTS HERE, AT THE FIRST SAMPLE. The brackets and marks used to
            // count from the moment the probe was switched on -- park load, shader warm-up, the lot
            // -- while the divisor counted only benchmark frames. The table then printed a section
            // at 115.6% of the frame and an "everything else" of MINUS 24 KB/frame. A percentage
            // over 100 and a negative remainder are not findings, they are two windows being
            // divided by each other, and either one alone would have looked plausible.
            _allocBy.Clear(); _allocDepth.Clear();
            _allocAt.Clear(); _allocAtN.Clear(); _allocMarkOrder.Clear();
            _allocInside = _allocOutside = _allocInFrames = _allocOutFrames = 0;
        }
        if (_allocLastCounter != 0)
        {
            _allocFrameTotal += now - _allocLastCounter;
            _allocFrames += framesNow - _allocFramesAtLastSample;
        }
        _allocLastCounter = now;
        _allocFramesAtLastSample = framesNow;
    }

    void ReportAllocations()
    {
        if (!_allocProbe || _allocFrames == 0) return;
        GD.Print($"[alloc] ============ {_allocFrames} frames ============");
        double whole = (double)_allocFrameTotal / _allocFrames;
        GD.Print($"[alloc] WHOLE FRAME {whole / 1024.0:F1} KB/frame");
        long attributed = 0;
        foreach (var kv in _allocBy.OrderByDescending(k => k.Value))
        {
            bool nested = _allocDepth.GetValueOrDefault(kv.Key) > 0;
            // ⚠ Only TOP-LEVEL sections add up. A nested one is already inside its parent's figure.
            if (!nested) attributed += kv.Value;
            double per = (double)kv.Value / _allocFrames;
            GD.Print($"[alloc]   {(nested ? "  + " + kv.Key : kv.Key),-24} {per / 1024.0,8:F2} KB/frame  "
                   + $"({per / Math.Max(1, whole) * 100,5:F1}%){(nested ? "  (inside a parent above)" : "")}");
        }
        // ⚠ THE UNATTRIBUTED REMAINDER IS PRINTED. A profiler that only lists what it measured
        // implies the list is the whole story; naming the gap is what stops that.
        double rest = whole - (double)attributed / _allocFrames;
        GD.Print($"[alloc]   {"(everything else)",-22} {rest / 1024.0,8:F2} KB/frame  "
               + $"({rest / Math.Max(1, whole) * 100,5:F1}%)  <- not bracketed");
        if (_allocMarkOrder.Count > 0)
        {
            GD.Print("[alloc] ---- CUMULATIVE DOWN _Process (step = that span's own cost) ----");
            // ⚠ Sorted by label, not by first-seen: the accumulators reset at the first SAMPLE,
            // which happens part-way down _Process, so the first mark recorded is whichever comes
            // next -- and a "cumulative" column that starts in the middle prints a negative step.
            // The labels are numbered for exactly this.
            _allocMarkOrder.Sort(StringComparer.Ordinal);
            double prev = 0; long n0 = _allocAtN.GetValueOrDefault(_allocMarkOrder[0], 1);
            foreach (var label in _allocMarkOrder)
            {
                long n = Math.Max(1, _allocAtN.GetValueOrDefault(label));
                double cum = (double)_allocAt.GetValueOrDefault(label) / n;
                GD.Print($"[alloc]   {label,-26} cum {cum / 1024.0,8:F2}   step {(cum - prev) / 1024.0,8:F2} KB/frame"
                       + (n != n0 ? $"   ⚠ reached {n} times, not {n0}" : ""));
                prev = cum;
            }
        }
        if (_allocInFrames > 0 && _allocOutFrames > 0)
        {
            double inside = (double)_allocInside / _allocInFrames, outside = (double)_allocOutside / _allocOutFrames;
            GD.Print($"[alloc] ---- WHERE IN THE FRAME (main thread) ----");
            GD.Print($"[alloc]   inside _Process           {inside / 1024.0,8:F2} KB/frame  over {_allocInFrames} frames");
            GD.Print($"[alloc]   outside _Process          {outside / 1024.0,8:F2} KB/frame  over {_allocOutFrames} frames");
            GD.Print($"[alloc]   (outside = _Draw, _Input, signal dispatch, engine<->C# marshalling)");
        }
        else GD.Print("[alloc] ⚠ no inside/outside split: _Process never reached both ends");
        GD.Print("[alloc] ==========================================");
    }

    sealed class BenchSample
    {
        public double T;             // seconds since the benchmark began
        public double FrameMs;       // from the frame's own delta
        public double EngineFps;     // the engine's own count -- a SECOND opinion on FrameMs
        public double ProcessMs;     // the CPU side of it (_Process)
        public double PhysicsMs;
        public long Nodes, Orphans, Objects, Resources;
        public long DrawCalls, Primitives;
        /// <summary>⚠⚠ GODOT'S NATIVE HEAP (`Performance.MEMORY_STATIC`) -- the C++ side: meshes,
        /// vertex buffers, textures, servers. It is NOT the .NET managed heap and NOTHING a C# fix
        /// does moves it. Two verdicts used to be drawn off this field while calling it "GC floor"
        /// and "ALLOCATION", which made a 31% cut in managed garbage read as no change at all.</summary>
        public long StaticMemKb;
        /// <summary>The .NET managed heap in use right now (`GC.GetTotalMemory(false)`) -- what
        /// rises and falls as a sawtooth, and whose FLOOR is the actual leak test.</summary>
        public long ManagedHeapKb;
        /// <summary>Total bytes EVER allocated by .NET on every thread (`GC.GetTotalAllocatedBytes`),
        /// monotonic. Differenced between samples this is the real managed churn.</summary>
        public long ManagedAllocKb;
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
        AllocSample(_benchFrames);

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
            ManagedHeapKb = GC.GetTotalMemory(false) / 1024,
            ManagedAllocKb = GC.GetTotalAllocatedBytes(true) / 1024,
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
        long mFloor0 = first.Min(x => x.ManagedHeapKb);
        long mFloorMid = _bench.Skip(_bench.Count / 3).Take(_bench.Count / 3).Min(x => x.ManagedHeapKb);
        long mFloorEnd = last.Min(x => x.ManagedHeapKb);
        long m0 = floor0, m1 = floorEnd;
        // ⭐ And the number that is actually actionable: how much garbage is made per second and
        // per frame. On a fast machine a huge churn is invisible in frame time and still the thing
        // that will hurt on a slower one.
        // ⚠⚠ THE OLD "ALLOCATION" FIGURE WAS AN ARTIFACT AND IS GONE. It summed the POSITIVE
        // deltas of Godot's static-memory monitor at 4 Hz and divided by the frame count. But that
        // monitor is a coarsely-sampled sawtooth with a 40 MB amplitude (the floor by third below
        // swings 89 -> 128 -> 116 MB as textures and buffers come and go), and summing the rises of
        // a sawtooth you sample 4 times a second is not a per-frame measurement of anything -- it
        // reported ~187 KB/frame, i.e. 2 GB over a 17 s run, which no draw path is doing.
        //
        // ⭐ You cannot see per-frame native churn through a 4 Hz memory gauge, so this no longer
        // pretends to. What IS measurable is the RANGE and the FLOOR, which answer the question
        // that matters (is native memory growing?) without inventing a rate. Per-frame C++ cost
        // has to be read from draw calls and frame time, or from a native profiler.
        double span = Math.Max(0.001, _bench[^1].T - _bench[0].T);
        long nativeLo = _bench.Min(x => x.StaticMemKb), nativeHi = _bench.Max(x => x.StaticMemKb);
        // ⭐⭐ THE MANAGED FIGURE, WHICH IS A DIFFERENT SUBSYSTEM AND IS ACTUALLY MEASURABLE.
        // GetTotalAllocatedBytes is MONOTONIC across every thread, so two endpoints ARE the total
        // -- no rise-summing, no sampling artifact, nothing to sum wrongly.
        // ⚠ `precise: true`: the default skips each thread's unretired allocation context, which
        // made this read LOWER than the main thread's own exact counter -- an all-thread total
        // below one thread's share is impossible, and that impossibility is what caught it.
        long managed = Math.Max(0, _bench[^1].ManagedAllocKb - _bench[0].ManagedAllocKb);
        double managedMbS = managed / 1024.0 / span;
        double managedKbFrame = _benchFrames > 0 ? (double)managed / _benchFrames : 0;

        GD.Print($"[bench] TREND first quarter {f0:F2} ms -> last quarter {f1:F2} ms "
               + $"({drift:+0.00;-0.00;0.00} ms, {driftPct:+0.0;-0.0;0.0}%)");
        GD.Print($"[bench]   nodes {n0} -> {n1} ({n1 - n0:+#;-#;0})   orphans {o0} -> {o1} ({o1 - o0:+#;-#;0})");
        GD.Print($"[bench]   objects {ob0} -> {ob1} ({ob1 - ob0:+#;-#;0})");
        GD.Print($"[bench]   GODOT NATIVE FLOOR by third: {floor0} -> {floorMid} -> {floorEnd} KB "
               + $"(a leak raises the floor; a sawtooth does not)");
        GD.Print($"[bench]   MANAGED HEAP FLOOR by third: {mFloor0} -> {mFloorMid} -> {mFloorEnd} KB");
        // ⚠⚠ TWO SUBSYSTEMS, LABELLED. The first is Godot's C++ heap -- per-frame mesh rebuilds,
        // vertex buffers, AddSurfaceFromArrays -- and no C# change touches it. The second is .NET
        // garbage. They differ by several times over and mixing them up sends the next reader to
        // the wrong file, which is exactly what happened here.
        GD.Print($"[bench]   GODOT NATIVE RANGE {nativeLo} -> {nativeHi} KB over the run "
               + $"({nativeHi - nativeLo} KB of swing; a RATE cannot be read from a 4 Hz gauge)");
        GD.Print($"[bench]   MANAGED (.NET) CHURN {managedMbS:F0} MB/s = {managedKbFrame:F0} KB per frame "
               + "(all threads; the per-section [alloc] table below is the main thread's share)");

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
            : $"[bench] NOT leaking (native): nodes flat and Godot's native floor returns to {floorEnd} KB");
        // ⚠ The managed heap gets its OWN leak test, because the old one never checked it: a rising
        // native floor and a rising GC floor are different bugs in different languages.
        GD.Print(mFloorEnd - mFloor0 > 32768
            ? $"[bench] ⚠ MANAGED HEAP GROWING: floor {mFloor0} -> {mFloorEnd} KB ({mFloorEnd - mFloor0:+#;-#;0})"
            : $"[bench] managed heap NOT growing: floor {mFloor0} -> {mFloorEnd} KB ({mFloorEnd - mFloor0:+#;-#;0})");
        GD.Print(managedMbS > 20
            ? $"[bench] ⚠ HIGH MANAGED CHURN: {managedMbS:F0} MB/s ({managedKbFrame:F0} KB/frame) of .NET "
              + "garbage. Invisible in frame time on a fast machine; it is what costs on a slow one, "
              + "and it is what grows as per-frame allocations are added feature by feature."
            : $"[bench] managed allocation is modest ({managedMbS:F0} MB/s)");
        GD.Print($"[bench] native memory swings {nativeHi - nativeLo} KB and its floor ends at "
               + $"{floorEnd} KB -- swing is the resource cache breathing, not a per-frame rate. "
               + "The only honest per-frame C++ signals here are draw calls and frame time.");
        if (!timeCrept && !nodesGrew && !memGrew)
            GD.Print("[bench] ⭐ So any creep master is seeing is COST ACROSS THE PROJECT, not "
                   + "degradation within a session -- profile the frame, do not hunt a leak.");
        GD.Print("[bench] ========================================");
        ReportAllocations();

        // The raw series, so a reader can see the shape rather than trust the summary.
        GD.Print("[bench] t,frame_ms,process_ms,nodes,orphans,objects,draw_calls,godot_native_kb,managed_heap_kb,managed_alloc_kb");
        foreach (var s in _bench)
            GD.Print($"[bench] {s.T:F2},{s.FrameMs:F2},{s.ProcessMs:F2},{s.Nodes},{s.Orphans},"
                   + $"{s.Objects},{s.DrawCalls},{s.StaticMemKb},{s.ManagedHeapKb},{s.ManagedAllocKb}");
        GetTree().Quit();
    }
}
