namespace TPW.PS2.Data;

/// <summary>⭐⭐ ONE PLAYING MUSIC EVENT, AS THE CONSOLE'S EVENT INSTANCE RUNS IT -- which set, which clip, and
/// what the parameters it was handed can and cannot reach. The park's `MUSSFX.MAP` event 2 and the lobby's
/// `LOBMSFX.MAP` event 6 both run through this; the playback is <c>GameMusic</c>'s.
///
/// ⭐ READ, the class and its three virtual steps:
/// <code>
///   0x2474A8  flags &amp; 4, !(flags &amp; 0x10), flags &amp; 2, flags &amp; 0x400  -> 0x24C5E0 (vtable 0x371450, the GRAPH class)
///             ... no 0x400, no 0x100                                -> 0x24D668 (vtable 0x3718E0)
///   init      0x244E08: Word12 != 0 -> a 16-byte record, rec[0] = Word12   (bytes 0..3 selectors, 4..7 values)
///             then vt+0x74(0) -- advance the set -- and vt+0x7C(0) -- pick a clip
///   clip end  0x24D538 (slot +0x0C of BOTH classes): vt+0x74(1), vt+0x7C(1), start the clip
///   +0x74     graph: 0x24C590 -- no record -> 0 (STOP); else 0x24C1F0(rec[4]) (<see cref="SfxEventMachine"/>)
///             other: 0x245D10 -- one set -> set 0; else a weighted draw that will not repeat among 3+
///   +0x7C     0x245A88 -- r = rotl(rng, 19) &gt;&gt; 16, the first clip whose threshold holds it; with the
///             flag and 3+ clips, the last clip drawn again becomes the NEXT one; nothing holds r -> 0 (STOP)
///   +0x64     0x24C3D0 -> 0x2462A0: for i in 0..3, rec[i] == selector -> rec[4 + i] = value
/// </code>
///
/// ⭐⭐⭐ THE PARK'S GUEST KNOB DOES NOT REACH THE CHOOSER, AND THAT IS READ, NOT A GAP. `0x151C00` hands the
/// park music `guests * 90 / 100` through `0x111E08`, whose `addiu a2, zero, 2` makes it **selector 2**. Every
/// world's music event carries **Word12 = 4**, so its record is `{4, 0, 0, 0}` (the per-set bytes at +0x16/+0x1A
/// are zero on all 26 music sets) and selector 2 matches no slot: `rec[4]` stays at the allocation's zero, the
/// chooser reads 0, and every set's first band (0..12 / 0..14 / 0..18) sends it back to **set 0 -- Level 1 --
/// forever**. Nothing else writes the byte: the only two other stores to `rec+4` are the chooser's own "no band
/// -> 0x7F" (`0x24C3AC`, `0x24F68C`), and the three selector-4 call sites of `0x111D40` (`0x20376C`, `0x203BB4`,
/// `0x204618`) all pass a track-ride car's handle. The link bands end at exactly 90 in all four worlds, which
/// is the `* 90 / 100` -- the levels were authored for the knob and the knob was wired to the wrong id.
/// <see cref="SetParameter"/> keeps the console's matching, so the port plays what the PS2 plays; whoever
/// wants the authored levels hands the value to <see cref="SteeringSelector"/> instead of 2.
///
/// ⚠ ADAPTERS, each said once here:
/// - The console draws every sound from ONE generator (`0x342F08`); this one has its own, so the ORDER of
///   draws matches but the sequence does not continue from the rest of the park's sounds.
/// - WHEN the next clip starts is the player's: the instance sets a deadline of clip-length + 250 ms
///   (`0x244E08`'s tail) that reads like a watchdog, and the voice-end path that fires +0x0C is not traced.
///   Stems cut to one tempo are played end to end.</summary>
public sealed class MusicSequencer
{
    public const int GraphMask = 0x0416, GraphFlags = 0x0406;
    /// <summary>`0x111E08`'s selector: the park's guest value goes to parameter 2.</summary>
    public const int ParkGuestSelector = 2;

    readonly byte[] _record;                 // null when Word12 == 0 -- the console allocates none
    readonly SfxEventMachine.Rng _rng;
    int _set = -1, _lastClip = -1, _lastSet = -1;

    public SfxMap.Event Event { get; }
    /// <summary>The console's class split at `0x2474A8`: flags 4 and 2 and 0x400, without 0x10.</summary>
    public bool IsGraph { get; }
    /// <summary>The selector the chooser reads, `rec[0]` = the event's Word12. 0: the event has no record.</summary>
    public int SteeringSelector => _record?[0] ?? 0;
    /// <summary>What the chooser will read: `rec[4]`, or -1 with no record.</summary>
    public int SteeringValue => _record == null ? -1 : _record[4];
    public int Set => _set;
    /// <summary>Why the last step returned null, for the log.</summary>
    public string Stopped { get; private set; }

    public MusicSequencer(SfxMap.Event ev, uint seed = 1)
    {
        Event = ev ?? throw new ArgumentNullException(nameof(ev));
        IsGraph = (ev.Flags & GraphMask) == GraphFlags;
        _rng = new SfxEventMachine.Rng(seed);
        if (ev.Word12 != 0) _record = new byte[8] { (byte)ev.Word12, 0, 0, 0, 0, 0, 0, 0 };
    }

    /// <summary>`0x2462A0`: the value lands in every slot whose selector matches, and nowhere else. Returns
    /// whether any slot took it.</summary>
    public bool SetParameter(int selector, int value)
    {
        if (_record == null || selector == 0) return false;      // 0x2413E0: selector 0 is a no-op
        bool hit = false;
        for (int i = 0; i < 4; i++)
            if (_record[i] == (byte)selector) { _record[4 + i] = (byte)value; hit = true; }
        return hit;
    }

    /// <summary>The first clip (init: +0x74(0), +0x7C(0)).</summary>
    public (int Set, int Clip)? Start() => Step(false);

    /// <summary>The clip after the one that just ended (0x24D538: +0x74(1), +0x7C(1)).</summary>
    public (int Set, int Clip)? Next() => Step(true);

    (int Set, int Clip)? Step(bool again)
    {
        Stopped = null;
        if (!AdvanceSet(again)) return null;
        int? clip = PickClip(again);
        if (clip == null) return null;
        return (_set, clip.Value);
    }

    bool AdvanceSet(bool again)
    {
        if (Event.Sets.Count == 0) { Stopped = "the event has no sets"; return false; }
        if (IsGraph)
        {
            if (_record == null) { Stopped = "a graph event with no parameter record (0x24C590 returns 0)"; return false; }
            int from = _set < 0 ? 0 : _set;                        // 0x24C1F0: current ??= event[+8]
            if (Event.Sets[from].Links.Count == 0) { Stopped = $"set {from} has no links"; _set = -1; return false; }
            var next = SfxEventMachine.Next(Event, from, _record[4], _rng);
            if (next == null)
            {
                Stopped = $"no band of set {from} holds {_record[4]}";
                _record[4] = 0x7F;                                  // 0x24C3AC
                return false;
            }
            _set = next.Value;
            return true;
        }
        // 0x245D10
        if (Event.Sets.Count == 1) { _set = 0; return true; }
        uint r = _rng.Next() >> 16, acc = 0;
        for (int i = 0; i < Event.Sets.Count; i++)
        {
            acc += Event.Sets[i].Weight;
            if (r > acc) continue;
            if (again && Event.Sets.Count >= 3 && i == _lastSet) i = (i + 1) % Event.Sets.Count;
            _set = _lastSet = i;
            return true;
        }
        _set = 0;                                                   // nothing held r: the first set
        return true;
    }

    int? PickClip(bool again)
    {
        var clips = Event.Sets[_set].Clips;
        if (clips.Count == 0) { Stopped = $"set {_set} has no clips"; return null; }
        if (clips.Count == 1) return 0;
        uint r = _rng.Next() >> 16;
        for (int i = 0; i < clips.Count; i++)
        {
            if (r > (uint)clips[i].Threshold) continue;
            if (again && clips.Count >= 3 && i == _lastClip) i = (i + 1) % clips.Count;
            _lastClip = i;
            return i;
        }
        // ⚠ 0x245A88 returns 0 here and the clip-end path starts nothing: the music stops. It can happen
        // wherever a set's last threshold is under 0xFFFF (Level314 ends at 65520, so 15 draws in 65536).
        Stopped = $"draw {r} is past set {_set}'s last threshold {clips[^1].Threshold}";
        return null;
    }
}

/// <summary>⭐ WHERE A MUSIC CLIP'S MUSIC IS, inside what the MP2 decodes to. The map's milliseconds are the
/// source length (the bank header's own on 939 of 939), and every music clip decodes 85..130 ms LONGER: one
/// frame of codec lead in front and frame padding behind. The lead is measured, not assumed -- a sharp
/// edge rings symmetrically through the synthesis filter, so (first + last - length) / 2 over the 316 music
/// clips that are loud at both ends puts the content's first sample at a median of **1152**, exactly one
/// Layer II frame. Played whole, every seam would stumble by that tenth of a second; played as this span,
/// the stems butt at their authored length.
///
/// ⚠ An adapter, not READ: the console's clip-end scheduling is not traced (see <see cref="MusicSequencer"/>),
/// so whether the PS2 plays the padding is not known. The span is the data's own statement of the music.</summary>
public static class MusicClip
{
    public const int CodecLead = 1152;

    /// <summary>The music's first frame and frame count in a clip that decoded to <paramref name="frames"/>.</summary>
    public static (int Start, int Count) Content(int frames, int milliseconds, int rate)
    {
        int start = Math.Min(CodecLead, frames);
        int count = frames - start;
        if (milliseconds > 0) count = Math.Min(count, (int)Math.Round(milliseconds * (double)rate / 1000.0));
        return (start, count);
    }
}
