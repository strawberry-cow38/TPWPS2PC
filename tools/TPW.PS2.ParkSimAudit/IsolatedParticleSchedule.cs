using System;
using System.Collections.Generic;

/// <summary>
/// AUDIT ONLY: parameterized, native-code-derived isolated normal-mode schedule.
/// Not native execution, hardware observation, or a full particle simulation.
/// No Root/CpuParticles dependency. Assumes admitted, unpaused, successful allocation,
/// and no child/end/death emission or destructive attractor. Rates, burst and cap
/// are already density-scaled; cap is signed and may be zero or negative.
/// </summary>
public static class IsolatedParticleSchedule
{
    public enum Phase { Spawn, PreParticleUpdate, OwnEmitterTick }

    public sealed record Receipt(
        int Tick, Phase Phase, long EmitterRemaining,
        int LiveBeforeEmission, short CountdownBeforeEmission,
        int Births, int Deaths, int LiveAfterParticles, short Countdown,
        int CumulativeBirths, bool Freed);

    public sealed record Trace(
        IReadOnlyList<Receipt> Receipts, IReadOnlyList<int> BirthTicks,
        bool Freed, int OwnTicksExecuted, int LiveAtEnd, long EmitterRemaining,
        short Countdown);

    /// <summary>
    /// lifeByBirth receives the zero-based cumulative birth ordinal, including burst.
    /// Its deterministic return is initial particle remaining life (zero and negative
    /// are accepted), NOT a replacement for the native shared global RNG stream.
    /// Tick 0 records the uncapped burst, with no aging. Optionally one separate
    /// particle pass at tick 0 precedes own ticks 1..ownTicks. Each own tick ages
    /// every survivor and newborn; zero emitter life remains emission-eligible.
    /// q1..q4 are signed rate bytes in authored order: phase >=3 selects q1,
    /// 2 selects q2, 1 selects q3, and 0 selects q4; initial zero selects q1.
    /// The caller chooses a finite tick budget, including for immortal emitters.
    /// Freed is completion, not budget exhaustion; no receipts follow freeing.
    /// </summary>
    public static Trace Run(
        int emitterLife, bool immortal, int burst, int maxLive, short countdown,
        sbyte q1, sbyte q2, sbyte q3, sbyte q4, bool dieWithEmitter,
        Func<int, int> lifeByBirth, int ownTicks, int preParticleUpdates = 0)
    {
        if (burst < 0) throw new ArgumentOutOfRangeException(nameof(burst));
        if (ownTicks < 0) throw new ArgumentOutOfRangeException(nameof(ownTicks));
        if (preParticleUpdates < 0 || preParticleUpdates > 1)
            throw new ArgumentOutOfRangeException(nameof(preParticleUpdates));
        if (lifeByBirth == null) throw new ArgumentNullException(nameof(lifeByBirth));

        var particles = new List<long>();
        var receipts = new List<Receipt>();
        var birthTicks = new List<int>();
        var rates = new[] { q1, q2, q3, q4 };
        long remaining = emitterLife;
        bool freed = false;
        int executed = 0;

        void Birth(int tick)
        {
            int life = lifeByBirth(birthTicks.Count);
            particles.Add(life);
            birthTicks.Add(tick);
        }

        int ParticlePass()
        {
            int deaths = 0;
            // Compact in place: decrement first; death marking is NOT removal.
            int survivors = 0;
            for (int i = 0; i < particles.Count; i++)
            {
                long life = particles[i] - 1;
                if (life < 0) { deaths++; continue; }
                if (remaining < 0 && dieWithEmitter) life = -1;
                particles[survivors++] = life;
            }
            particles.RemoveRange(survivors, particles.Count - survivors);
            return deaths;
        }

        void Record(int tick, Phase phase, int before, short beforeCountdown,
                    int births, int deaths)
        {
            receipts.Add(new Receipt(tick, phase, remaining, before, beforeCountdown,
                births, deaths, particles.Count, countdown, birthTicks.Count, freed));
        }

        for (int i = 0; i < burst; i++) Birth(0);
        Record(0, Phase.Spawn, 0, countdown, birthTicks.Count, 0);
        if (preParticleUpdates == 1)
        {
            int before = particles.Count;
            int deaths = ParticlePass();
            Record(0, Phase.PreParticleUpdate, before, countdown, 0, deaths);
        }

        for (int i = 0; i < ownTicks; i++)
        {
            int tick = i + 1;
            executed++;
            if (!immortal) remaining--;
            int before = particles.Count;
            short beforeCountdown = countdown;
            int births = 0;
            if (remaining < 0 && before == 0)
            {
                freed = true;
                Record(tick, Phase.OwnEmitterTick, before, beforeCountdown, 0, 0);
                break;
            }
            if (remaining >= 0)
            {
                long phase = emitterLife == 0 ? 3 : remaining * 4 / emitterLife;
                int quarter = phase switch { 0 => 3, 1 => 2, 2 => 1, _ => 0 };
                int rate = rates[quarter];
                // One signed-cap gate for the entire emission operation.
                if (before < maxLive)
                {
                    if (rate < 0)
                    {
                        countdown = unchecked((short)(countdown - 1));
                        if (countdown <= 0)
                        {
                            births = 1;
                            countdown = unchecked((short)-rate);
                        }
                    }
                    else births = rate;
                    for (int b = 0; b < births; b++) Birth(tick);
                }
            }
            int deaths = ParticlePass();
            Record(tick, Phase.OwnEmitterTick, before, beforeCountdown, births, deaths);
        }
        return new Trace(receipts.AsReadOnly(), birthTicks.AsReadOnly(), freed,
            executed, particles.Count, remaining, countdown);
    }
}
