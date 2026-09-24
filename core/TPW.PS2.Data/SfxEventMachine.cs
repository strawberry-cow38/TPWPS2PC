namespace TPW.PS2.Data;

/// <summary>⭐⭐ AN SFX EVENT IS A LOOPING STATE MACHINE, NOT A SOUND. Master heard it first --
/// "there's a bunch of sounds that should loop with the loop starting/ending with overlapping
/// sounds" -- and the data agrees: of 1,233 events across the disc's 31 `*SFX.MAP`s, **68 are
/// multi-set graphs and every one of them is cyclic**.
///
/// A set is a pool of clips plus links to other sets. Each link carries a low/high band. After a
/// set plays, the machine reads one numbered sound parameter -- the event's own
/// <see cref="SfxMap.Event.Word12"/> -- and moves to a set chosen among the links whose band
/// contains that value. So the parameter steers the loop, continuously.
///
/// ⭐ READ from `FUN_0024C1F0`, the transition chooser, found by scanning for code that reads a
/// link's `+6` and `+7` as bytes off one base:
///
/// <code>
///   if (current == null) current = event.Sets[0];                    // state[0xC] &lt;- event[+8]
///   total = sum of target.Weight over links whose band contains v;   // pass 1
///   if (total has no contributors) { stop; }                         // returns 0 -- THE LOOP ENDS
///   rng = rng * 0x19660D + 0x3C6EF35F;                               // pass 2
///   acc = 0;
///   for each link with band containing v:
///       acc += target.Weight;
///       if (rotl(rng,19) % total &lt;= acc) { current = target; break; }
/// </code>
///
/// ⚠⚠ THREE THINGS THAT ARE EASY TO GET WRONG AND ARE ALL READ HERE:
/// <list type="bullet">
/// <item>It does **not** take the first matching link. It is a **weighted random draw among every
/// matching link**, weighted by the TARGET set's own weight.</item>
/// <item>When **no** link's band contains the value the machine **STOPS** -- that is how a loop
/// ends, not by falling through or holding.</item>
/// <item>A link's target is a **relocated pointer** at runtime, and in the FILE it is a **1-based
/// set number**. That is not inferred from the values landing in `1..Sets.Count` -- it is the
/// loader's own arithmetic, `FUN_0024B8D0`:
/// <code>*link = setsBase + (*link - 1) * 0x2A;</code>
/// `0x2A` is the 42-byte set record and the `- 1` is the index base. Reading it as 0-based puts
/// the last link of every event past the end.</item>
/// </list>
///
/// ⚠ WHAT THIS DOES NOT SAY: **when** the next clip starts relative to the current one ending.
/// `FUN_0024C1F0` chooses what comes next and never says when. Whether clips overlap, butt up or
/// leave a gap needs the scheduler, which is not traced -- so this class exposes the ORDER and the
/// caller decides the timing. astraclaw pushed back on exactly this and was right to.</summary>
public static class SfxEventMachine
{
    /// <summary>`FUN_0024C1F0`'s own generator: `x = x*0x19660D + 0x3C6EF35F`, drawn as
    /// `rotl(x, 19) % total`. Kept exact so a test can reproduce a sequence.</summary>
    public sealed class Rng
    {
        uint _s;
        public Rng(uint seed = 1) { _s = seed; }
        public uint Next() { _s = unchecked(_s * 0x19660Du + 0x3C6EF35Fu); return (_s << 19) | (_s >> 13); }
    }

    /// <summary>Where playback begins: the event's first set, or null for an event with none.</summary>
    public static int? Start(SfxMap.Event ev) => ev == null || ev.Sets.Count == 0 ? null : 0;

    /// <summary>The set index a link points at. ⚠ The file stores a **1-based** set number.</summary>
    public static int? TargetIndex(SfxMap.Event ev, SfxMap.Link link)
    {
        int i = link.Target - 1;
        return i >= 0 && i < ev.Sets.Count ? i : null;
    }

    /// <summary>The next set, or **null when the loop ends** because no link's band contains
    /// <paramref name="value"/>. <paramref name="rng"/> is drawn once, as the console does.</summary>
    public static int? Next(SfxMap.Event ev, int current, int value, Rng rng)
    {
        if (ev == null || current < 0 || current >= ev.Sets.Count) return null;
        var links = ev.Sets[current].Links;
        long total = 0;
        foreach (var l in links)
            if (l.Low <= value && value <= l.High && TargetIndex(ev, l) is { } t)
                total += ev.Sets[t].Weight;
        if (total <= 0)
        {
            // ⚠ The console divides by the total without guarding it (`trap(7)` on zero), so a
            // graph whose matching targets all weigh nothing would fault there. Treating it as
            // "stop" is this port refusing to reproduce a division fault, and is the one place
            // here that deviates -- deliberately, and said out loud.
            return null;
        }
        long draw = (long)(rng.Next() % (ulong)total), acc = 0;
        foreach (var l in links)
        {
            if (l.Low > value || value > l.High) continue;
            if (TargetIndex(ev, l) is not { } t) continue;
            acc += ev.Sets[t].Weight;
            if (draw <= acc) return t;
        }
        return null;
    }

    /// <summary>Whether this event is a loop at all -- more than one set, and some set links on.
    /// A single-set event with no links is an ordinary one-shot and needs none of this.</summary>
    public static bool IsGraph(SfxMap.Event ev)
        => ev != null && ev.Sets.Count > 1 && ev.Sets.Any(s => s.Links.Count > 0);
}
