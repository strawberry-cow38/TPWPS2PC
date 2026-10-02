using System;
using System.Collections.Generic;

namespace TPW.PS2.Data;

/// <summary>⭐ A native birth plan (<see cref="ParticleTemplate.Plan"/>) cut into the pieces a one-shot
/// CPU emitter can draw. Godot's one-shot emitter births its <c>Amount</c> particles EVENLY over one
/// particle lifetime (times one minus its explosiveness) and has no way to place a single birth, so
/// each run here is a stretch of births it CAN draw: the same number of births on every tick of an
/// evenly spaced series of ticks, spanning no more than one particle life.
///
/// ⚠ An adapter, not a native reading. Within a run, the births of one tick are spread evenly over
/// that tick's gap instead of landing on the same tick, which moves a birth by less than one gap
/// (31 ms per tick of gap). Counts and the tick each group starts on are the plan's exactly.</summary>
public static class ParticleBirthRuns
{
    /// <summary><paramref name="FirstBirth"/> is the plan ordinal of the run's first birth, so a run of
    /// one can take that particle's own planned life.</summary>
    public readonly record struct Run(int FirstTick, int Ticks, int Gap, int PerTick, int FirstBirth)
    {
        public int Count => Ticks * PerTick;
        public int LastTick => FirstTick + (Ticks - 1) * Gap;
    }

    /// <summary>A cap-gated plan is drawn birth by birth up to this many births; past it, as runs.</summary>
    public const int MaxExactBirths = 64;

    /// <summary>⭐ The runs the renderer draws for <paramref name="plan"/>: one run per birth tick when the
    /// live cap gated it (each particle keeps its own planned life, the gaps between them being those
    /// lives), otherwise evenly spaced runs no longer than one particle life.</summary>
    public static List<Run> For(ParticleTemplate t, IsolatedParticleSchedule.Trace plan) =>
        Of(plan.BirthTicks, Math.Max(t.Life, 1), each: t.CapGated(plan) && plan.BirthTicks.Count <= MaxExactBirths);

    /// <param name="birthTicks">The plan's births, in order (ascending ticks, repeats for a burst).</param>
    /// <param name="maxSpanTicks">The longest a run may last: one particle life, in ticks. A run of
    /// <c>n</c> ticks <c>g</c> apart spans <c>n * g</c>.</param>
    /// <param name="each">One run per birth tick, never merged: for a plan the live cap gated, where
    /// each birth is drawn with its own planned life.</param>
    public static List<Run> Of(IReadOnlyList<int> birthTicks, int maxSpanTicks, bool each = false)
    {
        // Group repeats: (tick, births on it, ordinal of the first).
        var groups = new List<(int Tick, int Count, int First)>();
        for (int i = 0; i < birthTicks.Count; i++)
        {
            if (i > 0 && birthTicks[i] < birthTicks[i - 1])
                throw new ArgumentException("birth ticks must not go backwards", nameof(birthTicks));
            if (groups.Count > 0 && groups[^1].Tick == birthTicks[i])
                groups[^1] = (groups[^1].Tick, groups[^1].Count + 1, groups[^1].First);
            else groups.Add((birthTicks[i], 1, i));
        }
        var runs = new List<Run>();
        for (int i = 0; i < groups.Count;)
        {
            var start = groups[i];
            int ticks = 1, gap = 0, j = i + 1;
            while (!each && j < groups.Count && groups[j].Count == start.Count)
            {
                int g = groups[j].Tick - groups[j - 1].Tick;
                if (ticks > 1 && g != gap) break;
                if ((ticks + 1) * g > maxSpanTicks) break;
                gap = g; ticks++; j++;
            }
            runs.Add(new Run(start.Tick, ticks, ticks == 1 ? 0 : gap, start.Count, start.First));
            i = j;
        }
        return runs;
    }
}
