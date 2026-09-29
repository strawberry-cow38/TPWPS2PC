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

    /// <summary>⭐⭐ THE SLOW-FRAME REPORTER, for a PERIODIC stutter. Master, 2026-09-28: "there
    /// seems to be a stutter every second or so" -- measured as spikes 1.76 s apart, worst frame
    /// 35.12 ms against a 2.38 ms median.
    ///
    /// ⚠ Averages cannot find this. A job that costs 33 ms once every 1.76 s adds under 0.02 ms to
    /// a per-section mean and vanishes into the noise; the alloc table would not show it at all,
    /// because it measures BYTES and this is TIME. What identifies it is the one frame it happens
    /// on, so this times the same bracketed sections and, when a frame exceeds the threshold, prints
    /// that FRAME'S section times rather than any aggregate.
    ///
    /// ⚠ Timestamps only, no Stopwatch objects: `GetTimestamp` is a counter read, so bracketing
    /// costs about the same as the allocation counter already does and does not change what it
    /// measures.</summary>
    readonly Stack<long> _timeMarks = new();
    readonly List<(string Name, double Ms)> _frameTimes = new();
    double _slowFrameMs = 0;            // 0 = off; set by --slow-frames=<ms>
    int _slowFramesSeen;

    /// <summary>⭐⭐ DID A GARBAGE COLLECTION HAPPEN ON THIS FRAME. `GC.CollectionCount(gen)` is a
    /// monotonic counter, so the per-frame DELTA is "a gen-N collection ran during this frame" --
    /// which `ManagedHeapKb` cannot say. `GetTotalMemory` sawtooths, and reading its falls as
    /// collections confuses a collection with a heap that merely shrank.
    ///
    /// ⭐ WHY THIS IS THE NEXT THING TO MEASURE. Master reports the stutter as "every second or
    /// so". Four leads are already closed BY MEASUREMENT (allocation volume, draw calls, the
    /// advisor head, and every bracketed C# section inside `_Process`), and the cost sits OUTSIDE
    /// the brackets. There is no `_PhysicsProcess` anywhere in the game and no repeating `Timer`,
    /// and of the five `_Process` overrides only this one and LaptopShopScreen's (which early-outs
    /// while closed) run in a park -- so "outside" is not some other C# callback. A gen0 pause is
    /// exactly the shape left: it is not inside any bracket, it is periodic, and its period is set
    /// by the allocation RATE. At the measured 23.8 KB/frame a ~2 MB gen0 budget is a collection
    /// roughly every 84 frames, ~1.4 s at 60 fps.
    ///
    /// ⚠⚠ AND IT COMES WITH THE CONTROL THIS RIG HAS TWICE NEEDED. Counting collections only on
    /// slow frames would repeat the `head=1` mistake exactly: 12 of 12 looked damning against a
    /// 33% baseline that turned out to be 33%. So every frame past the warm-up is counted, slow or
    /// not, and the verdict prints BOTH rates. If gen0 runs on the same fraction of slow frames as
    /// of all frames, the GC is eliminated and this instrument was worth building anyway.</summary>
    int _gcPrev0, _gcPrev1, _gcPrev2;
    bool _gcSeeded;
    /// <summary>Frames since the last collection of ANY generation; 0 means one ran on this frame.
    /// The hitch's lag behind its own collection is the whole question, so it is measured.</summary>
    int _framesSinceGc = -1;
    int _gcFramesAll, _gcFrames0, _gcFrames1, _gcFrames2;
    int _gcSlowAll, _gcSlow0, _gcSlow1, _gcSlow2;

    void AllocBegin()
    {
        if (_allocProbe) _allocMarks.Push(GC.GetAllocatedBytesForCurrentThread());
        if (_slowFrameMs > 0) _timeMarks.Push(System.Diagnostics.Stopwatch.GetTimestamp());
    }

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
        if (_slowFrameMs > 0 && _timeMarks.Count > 0)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - _timeMarks.Pop())
                      * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _frameTimes.Add((name, ms));
        }
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

    /// <summary>Called at the END of `_Process` with the frame's own delta: if the frame was slow,
    /// print what was slow IN IT. ⚠ Prints at most 12 frames so a genuinely slow machine does not
    /// turn the log into the bottleneck.</summary>
    void SlowFrameReport(double deltaMs)
    {
        if (_slowFrameMs <= 0) { _frameTimes.Clear(); _timeMarks.Clear(); return; }
        // ⚠⚠ ONLY ONCE THE BENCHMARK IS PAST ITS WARM-UP -- WITHOUT THIS THE REPORTER MEASURES THE
        // PARK LOAD AND NOTHING ELSE. It fired from the first frame and capped at 12 reports, so all
        // twelve were spent on loading before the run ever reached steady state, and the stutter it
        // exists to find was never sampled at all.
        //
        // ⭐ The tell was a CONTROL I nearly did not run: the slow frames reported 117-170 draw
        // calls, and I read that as a spike. The benchmark's own regular sampler reads a flat 245
        // for the entire run -- so those frames had FEWER draws than normal, not more, because the
        // scene was still being built. An instrument that only samples the frames it selects has no
        // baseline to compare them against, and I published "draw calls spike 156 -> 249" off
        // exactly that gap. The regular sampler was the baseline and it was already there.
        if (!_benchRunning || _benchElapsed < BenchWarmup) { _frameTimes.Clear(); _timeMarks.Clear(); return; }
        // ⭐⭐ EVERY frame past the warm-up, slow or not -- this is the BASELINE, and it has to be
        // taken over the same population the slow frames are drawn from or it says nothing. See
        // the field's own note: `head=1 on 12 of 12` was this mistake with a different field.
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        int gcd0 = 0, gcd1 = 0, gcd2 = 0;
        // ⚠ The first counted frame SEEDS and is not scored: differencing against zero would report
        // every collection since process start as though it had all happened in that one frame.
        if (_gcSeeded)
        {
            gcd0 = g0 - _gcPrev0; gcd1 = g1 - _gcPrev1; gcd2 = g2 - _gcPrev2;
            _gcFramesAll++;
            if (gcd0 > 0) _gcFrames0++;
            if (gcd1 > 0) _gcFrames1++;
            if (gcd2 > 0) _gcFrames2++;
        }
        else _gcSeeded = true;
        _gcPrev0 = g0; _gcPrev1 = g1; _gcPrev2 = g2;
        if (gcd0 > 0 || gcd1 > 0 || gcd2 > 0) _framesSinceGc = 0;
        else if (_framesSinceGc >= 0) _framesSinceGc++;
        // ⚠⚠ THE CAP IS A SAMPLING BIAS, NOT JUST A LOG LIMIT. At 12 the reporter records the
        // EARLIEST twelve slow frames and then stops, so "12 of 12 had the advisor head up" can
        // simply mean the head happened to be up early -- not that slow frames prefer it. Against a
        // 33% baseline that reads as a damning correlation and is an artifact of where the cap fell.
        // 400 spans a 75 s run; the timestamp below makes the distribution visible instead of
        // implied.
        // ⚠⚠ COUNTED OUTSIDE THE CAP, AND THIS NEARLY SHIPPED WRONG. The cap stops PRINTING after
        // 400, so if the tally lived inside it the slow set would cover only the run's first
        // stretch while the baseline above covers all of it -- two populations over different
        // spans, which is the same defect as the cap itself, one level down. The print is capped;
        // the counting never is.
        bool slowFrame = deltaMs >= _slowFrameMs;
        if (slowFrame)
        {
            _gcSlowAll++;
            if (gcd0 > 0) _gcSlow0++;
            if (gcd1 > 0) _gcSlow1++;
            if (gcd2 > 0) _gcSlow2++;
        }
        if (slowFrame && _slowFramesSeen < 400)
        {
            _slowFramesSeen++;
            var worst = _frameTimes.Where(t => t.Ms >= 0.5).OrderByDescending(t => t.Ms).Take(6);
            // ⚠ THE BRACKETED TOTAL IS PRINTED BESIDE THE FRAME so the two can be compared. On the
            // first run the load frames showed `frame 133.3 ms -- StepPark 213.2ms`, a section
            // longer than the frame it sits in, which is impossible: Godot CLAMPS `delta`, so on a
            // very long frame the reported delta is not the wall time. The section times are real
            // and the frame figure is the clamped one -- naming both stops the next reader
            // reconciling two numbers that do not describe the same thing.
            // ⚠⚠ TOP-LEVEL ONLY -- I WROTE THIS BUG TWICE IN ONE DAY. The first version summed every
            // entry, so `StepPark 6.3ms` and the `PresentScripted 6.2ms` inside it both counted and
            // the total came out 18.9 ms for a 12.6 ms frame. That is the identical double-count the
            // allocation table had this morning, in a new instrument, caught only because a total
            // larger than its own frame is obviously impossible. `_allocDepth` already records the
            // shallowest depth each name closed at, so reuse it rather than repeat the fix.
            double bracketed = _frameTimes.Where(t => _allocDepth.GetValueOrDefault(t.Name) == 0)
                                          .Sum(t => t.Ms);
            // ⭐⭐ THE ENGINE'S OWN CLOCKS, so "outside my brackets" stops being a guess about WHERE.
            // astraclaw, correctly: time outside the measured C# sections does not by itself prove
            // GPU work -- it is equally consistent with unbracketed C#, engine CPU (scene tree,
            // culling, physics) or a driver/vsync wait. TIME_PROCESS and TIME_PHYSICS_PROCESS are
            // the engine's measurement of its own halves, so printing them beside the frame says
            // which half grew instead of leaving it to inference.
            //
            // ⚠ TIME_PROCESS has never reconciled with frame time in this rig (the benchmark's own
            // summary says so and refuses to read it as per-frame ms). It is quoted here as a
            // RELATIVE signal -- did it spike on this frame -- and must not be subtracted from the
            // frame time as though the two shared units.
            double tProc = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
            double tPhys = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
            long draws = (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            GD.Print($"[slow] t={_benchElapsed - BenchWarmup:F1}s frame {deltaMs:F1} ms "
                   + $"(_Process body {_procMs:F1}, bracketed {bracketed:F1}, "
                   + $"{_procMs - bracketed:F1} unbracketed INSIDE, {deltaMs - _procMs:F1} OUTSIDE) "
                   // ⭐ ADVISOR STATE ON THE LINE, so the head is RULED OUT by correlation rather
                   // than by argument. tinyclaw: it is 32 meshes / 56 materials and its SubViewport
                   // only renders while a message is up (update mode Disabled otherwise,
                   // AdvisorHead.cs:257), so it should read as a PLATEAU lasting the whole message
                   // -- seconds -- not a one-frame spike every 1.76 s. If `head=1` never coincides
                   // with a draw spike, that is the elimination; if it always does, the argument
                   // was wrong. Either way the data settles it instead of two of us reasoning.
                   + $"[engine: process {tProc:F1} physics {tPhys:F1} draws {draws} "
                   + $"head={(_advisorHead?.Overlay?.Visible == true ? 1 : 0)} "
                   // ⭐ Per-frame GC deltas, not heap size: "a gen-N collection ran during this
                   // frame". The rates that judge them are in the verdict, never read off here.
                   + $"gc={gcd0}/{gcd1}/{gcd2} "
                   // ⭐⭐ THE PARK'S OWN CLOCK ON EVERY SLOW LINE. The hitch's WALL period drifts
                   // 3.2 s -> 3.8 s over a run, identically at two resolutions, so "every 3.2 s"
                   // is not a period at all until it is expressed in the units of whatever drives
                   // it. Ticks tell the two apart in one run: constant in ticks means the sim
                   // schedules it and the wall drift is the sim falling behind; drifting in ticks
                   // too means the cause is neither the clock nor the frame count.
                   + $"ticks={_parkTicks} "
                   // ⭐⭐ THE HEAP AND THE OBJECT COUNT ON THE FRAME ITSELF, AND HOW LONG SINCE THE
                   // LAST COLLECTION. `gc=0/0/0` on every hitch said "not the GC" -- but the 4 Hz
                   // sampler shows the managed heap collapsing 120 MB -> 66 MB and Godot's object
                   // count 38k -> 6k at exactly the hitch timestamps, and 24 gen0 collections over
                   // 87 s is one per 3.6 s against a hitch every 3.2-3.8 s. Those cannot both be
                   // true of the same frame, so the delta was being read one frame too early: the
                   // collection increments the counter, and the wave of ~30k native frees it
                   // releases lands after it. `sinceGc` measures that lag instead of assuming it
                   // is zero -- an instrument that only asks about THIS frame cannot see a cause
                   // that arrives on the next one.
                   + $"heapKb={GC.GetTotalMemory(false) / 1024} "
                   + $"objects={(long)Performance.GetMonitor(Performance.Monitor.ObjectCount)} "
                   + $"sinceGc={_framesSinceGc}] -- "
                   + (worst.Any() ? string.Join(", ", worst.Select(t => $"{t.Name} {t.Ms:F1}ms"))
                                  : "NOTHING BRACKETED WAS SLOW (the cost is outside every bracket)"));
        }
        _frameTimes.Clear();
        _timeMarks.Clear();
    }

    /// <summary>⭐⭐ WALL TIME FOR THE WHOLE `_Process` BODY, which is the one split the slow-frame
    /// reporter could not make. It says the bracketed sections cost ~1 ms on a stutter frame and
    /// that 11-53 ms is "outside every bracket" -- but that phrase covers two very different places:
    /// unbracketed C# still INSIDE `_Process`, or the engine's own work after it returns. Timing the
    /// body end to end tells them apart, and only one of them is mine to fix.</summary>
    long _procTop; double _procMs;

    void AllocFrameTop()
    {
        if (_slowFrameMs > 0) _procTop = System.Diagnostics.Stopwatch.GetTimestamp();
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
        if (_slowFrameMs > 0 && _procTop != 0)
            _procMs = (System.Diagnostics.Stopwatch.GetTimestamp() - _procTop)
                    * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
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
        /// <summary>Whether the advisor head was up on this sample -- the baseline for the
        /// slow-frame reporter's `head=` field.</summary>
        public bool HeadUp;
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
            // ⭐ THE ADVISOR'S STATE ON EVERY SAMPLE, NOT ONLY ON SLOW ONES. The slow-frame reporter
            // showed `head=1` on every spike, which looks damning and proves nothing: it only ever
            // samples slow frames, so it cannot say what fraction of ALL frames have the head up.
            // An advisor message runs ~23 s against a 20 s window, so "up on every slow frame" is
            // equally consistent with "up on every frame". This is the baseline that tells them
            // apart -- exactly the control the draw-call claim lacked.
            HeadUp = _advisorHead?.Overlay?.Visible == true,
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
        int headUp = _bench.Count(s => s.HeadUp);
        GD.Print($"[bench]   advisor head up on {headUp} of {_bench.Count} samples "
               + $"({(_bench.Count > 0 ? 100.0 * headUp / _bench.Count : 0):F0}%) "
               + "-- the baseline for [slow]'s head= field");
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
        // ⭐⭐ THE GC VERDICT, AND IT IS A COMPARISON OF TWO RATES -- never a count on its own.
        // "gen0 ran on N slow frames" is worthless without "gen0 ran on M% of ALL frames", which is
        // the lesson the advisor-head field cost: 12 of 12 against a 33% baseline was 33%.
        if (_gcFramesAll > 0 && _gcSlowAll > 0)
        {
            // ⚠⚠ COUNTS FIRST, AND RATES TO THREE PLACES. The first run of this instrument printed
            // both sides as whole-number percentages, and a collection lands on well under 1% of
            // frames at 700 fps -- so BOTH sides read "0%", the ratio test compared 0 > 0, and it
            // announced GC ELIMINATED off a number that could not have said anything else. A rate
            // rounded past its own signal is not a measurement.
            GD.Print($"[bench] GC frames: gen0 {_gcFrames0}/{_gcFramesAll} ({100.0 * _gcFrames0 / _gcFramesAll:F3}%), "
                   + $"gen1 {_gcFrames1} ({100.0 * _gcFrames1 / _gcFramesAll:F3}%), "
                   + $"gen2 {_gcFrames2} ({100.0 * _gcFrames2 / _gcFramesAll:F3}%) "
                   + $"| of {_gcSlowAll} SLOW frames: gen0 {_gcSlow0}, gen1 {_gcSlow1}, gen2 {_gcSlow2}");
            // ⭐⭐ AND THE TEST'S POWER, STATED BEFORE ITS VERDICT. With a base rate this low, the
            // number of coincidences a slow set of this size would show BY CHANCE is often under
            // one -- and then "0 of 200 slow frames had a collection" is the expected result
            // whether the GC is guilty or innocent. An instrument has to say when it cannot decide,
            // or it will keep answering "eliminated" to questions it never tested.
            double expect0 = _gcSlowAll * (double)_gcFrames0 / _gcFramesAll;
            double expectAny = _gcSlowAll * (double)(_gcFrames0 + _gcFrames1 + _gcFrames2) / _gcFramesAll;
            GD.Print($"[bench] GC test power: a slow set of {_gcSlowAll} would show ~{expect0:F1} gen0 "
                   + $"coincidences by chance ({expectAny:F1} of any generation)");
            double all0 = 100.0 * _gcFrames0 / _gcFramesAll, slow0 = 100.0 * _gcSlow0 / _gcSlowAll;
            double all1 = 100.0 * _gcFrames1 / _gcFramesAll, slow1 = 100.0 * _gcSlow1 / _gcSlowAll;
            double all2 = 100.0 * _gcFrames2 / _gcFramesAll, slow2 = 100.0 * _gcSlow2 / _gcSlowAll;
            // ⚠ The threshold is deliberately blunt: a cause should be MUCH commoner on slow frames,
            // not marginally. A ratio near 1 is an elimination and is stated as one, out loud --
            // an instrument that can only confirm is not an instrument. But it may only say either
            // word once the expectation above is big enough for the count to carry information.
            bool implicated = slow0 > all0 * 2 || slow1 > all1 * 2 || slow2 > all2 * 2;
            GD.Print(expectAny < 3.0
                ? $"[bench] ⚠ GC NOT TESTED: {expectAny:F1} expected coincidences is too few to tell "
                  + "guilt from innocence. Lower --slow-frames to widen the slow set, or run longer; "
                  + "do NOT read the counts above as an elimination."
                : implicated
                ? "[bench] ⚠ GC IMPLICATED: a collection is at least twice as likely on a slow frame "
                  + "as on an average one. The period follows the allocation RATE, so the test that "
                  + "confirms it is to change that rate and watch the stutter's period move with it."
                : "[bench] ⭐ GC ELIMINATED: collections are no commoner on slow frames than on any "
                  + "other. Whatever the stutter is, it is not a managed pause -- and the OUTSIDE "
                  + "figure on the [slow] lines still needs a native profiler to attribute.");
        }
        else if (_slowFrameMs > 0)
            GD.Print("[bench] no slow frames past warm-up -- the GC comparison has nothing to judge");
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
