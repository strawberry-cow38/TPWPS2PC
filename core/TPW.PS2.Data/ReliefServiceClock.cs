namespace TPW.PS2.Data;

/// <summary>Native relief states35 ->22 -> completion, in guest-update counter units.
/// 20D708 adds522;20E160 uses strict unsigned greater-than. The dispatcher returns
/// after the35 handler, so22 completes on a later update, not in that same call.</summary>
public sealed class ReliefServiceClock
{
    public const uint Duration = 522;
    public uint Deadline { get; }
    public uint LastTick { get; private set; }
    public bool Finishing { get; private set; }
    public bool Completed { get; private set; }
    public ReliefServiceClock(uint enteredAt) { Deadline=unchecked(enteredAt+Duration); LastTick=enteredAt; }

    /// <summary>Call for each actually executed guest update. Repeated observations
    /// at one counter value cannot advance state or award another completion.</summary>
    public bool Advance(uint now)
    {
        if (Completed || now==LastTick) return false;
        LastTick=now;
        if (Finishing) { Completed=true; return true; }
        if (now>Deadline) Finishing=true;
        return false;
    }
}
