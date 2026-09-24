namespace TPW.PS2.Data;

/// <summary>APS-backed single-channel preview host. It queues an animation behind an unfinished
/// one-shot and replaces loops immediately. Effects are surfaced as events for a renderer/audio
/// host; this preview explicitly records them without rendering sound, particles or guest heads.</summary>
public sealed class RsePreviewHost : IRseHost
{
    public sealed record Playback(Animation.Record Record, int Variant, long Start, bool Loop, int Speed = 1000);
    public sealed record Effect(long Time, RseOpcode Opcode, IReadOnlyList<int> Arguments);
    readonly Dictionary<int, Animation.Record[]> _slots;
    Playback _queued;
    public Playback Current { get; private set; }
    public long Time { get; private set; }
    public int AnimationSlot => Current?.Record.Slot ?? -1;
    /// <summary>Which animation slots this model actually carries, and how many variants each
    /// has. ⭐ A script asking for a slot the model does not have is the difference between "the
    /// disc is like that" and "the loader dropped it", and nothing else distinguishes them: a
    /// missing record and a mis-parsed one both look like silence at the call site.</summary>
    public IReadOnlyList<(int Slot, int Variants)> AvailableSlots =>
        _slots.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value.Length)).ToList();
    public int AnimationVariant => Current?.Variant ?? -1;
    public event Action<Effect> EffectRequested;
    public Effect LastEffect { get; private set; }

    public RsePreviewHost(Animation animation) => _slots = animation?.Records()
        .GroupBy(r => r.Slot).ToDictionary(g => g.Key, g => g.ToArray()) ?? new();

    // 0x1abbb4 -> 0x297b68 converts the float to an unsigned integer by truncation.
    static int Duration(Animation.Record r) => checked((int)(r.DurationFrames * 1000f / Animation.Fps));
    /// <summary>How long it actually takes on the clock: the record's length divided by the rate
    /// TRIGANIMSPEED asked for. ⚠ NOT what the opcode hands back to the script -- see
    /// <see cref="IRseHost.PlayAnimationSpeed"/>.</summary>
    static int Wall(Playback p) => p.Speed <= 0 ? Duration(p.Record)
        : checked((int)((long)Duration(p.Record) * 1000 / p.Speed));
    public float Frame
    {
        get
        {
            if (Current == null) return 0;
            double frame = (Time - Current.Start) * Animation.Fps / 1000d * Current.Speed / 1000d;
            int duration = Current.Record.DurationFrames;
            return (float)(Current.Loop && duration > 0 ? frame % duration : Math.Min(frame, duration));
        }
    }
    public void AdvanceTo(long milliseconds)
    {
        if (milliseconds < Time) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        Time = milliseconds;
        if (_queued != null && Time >= _queued.Start) { Current = _queued; _queued = null; }
    }
    /// <summary>How long a request for an animation the model does not have takes. ⭐⭐ THE
    /// CONSOLE DOES NOT TREAT THIS AS AN ERROR. `0x1abc80` looks the record up through
    /// `0x1ab518` and, when that returns nothing, simply adds 1000 to the answer and plays
    /// nothing; the other arm of the same function falls back to the same 1000 when playback
    /// reports zero.
    ///
    /// ⚠⚠ THIS USED TO THROW, and it was the single biggest thing stopping scripts on this disc.
    /// Every `/features/` bin, speaker, fountain, camera, tower and portaloo opens with the
    /// standard `WAITANIM 0 0` prologue on a model that has no Create animation at all -- 38 of
    /// them across the four worlds -- along with rides asking for an Unload or Load slot they do
    /// not carry. A rock with no build animation is not a broken rock. The VM's rule that an
    /// unavailable service must fail explicitly still stands; this is not an unavailable service,
    /// it is a service whose answer has now been read.</summary>
    public const int MissingAnimationMilliseconds = 1000;

    public int PlayAnimation(int slot, int variant, bool loop) => PlayAnimation(slot, variant, loop, 1000);

    public int PlayAnimationSpeed(int slot, int variant, int speedPerMille) =>
        PlayAnimation(slot, variant, false, speedPerMille);

    int PlayAnimation(int slot, int variant, bool loop, int speed)
    {
        // ⚠⚠ NO FALLBACK HERE, AND THE ABSENCE IS THE DECODE. Every feature script opens
        // `WAITANIM 0 0` while almost nothing on the disc carries a slot 0 -- 30 scripts against
        // 0 records in JUNGLE -- so the request finds nothing and the machine moves on. That
        // looked like a defect and this port grew a fallback to slot 1 for it.
        //
        // ⭐⭐ IT IS NOT A DEFECT. Master, having watched the console: "in the real game that
        // animation never plays." Nothing playing IS the behaviour, and the fallback was making
        // the port do something retail does not. Reverted.
        //
        // ⚠ Exactly two feature `.aps` on the disc carry a slot 1 (`s_plant`, `Toilet`) and
        // nothing ever asks for it. Recorded because an unrequested record is a loose end, not
        // because anything should play it.
        if (!_slots.TryGetValue(slot, out var records) || variant < 0 || variant >= records.Length)
        {
            // Nothing is started and nothing already playing is disturbed.
            long unfinished = Current is { Loop: false }
                ? Math.Max(0, Current.Start + Wall(Current) - Time) : 0;
            return checked((int)unfinished) + MissingAnimationMilliseconds;
        }
        if (loop && Current is { Loop: true } && AnimationSlot == slot && AnimationVariant == variant && _queued == null)
            return Duration(Current.Record);
        long start = Time;
        if (Current is { Loop: false }) start = Math.Max(start, Current.Start + Wall(Current));
        var play = new Playback(records[variant], variant, start, loop, speed <= 0 ? 1000 : speed);
        if (start > Time) _queued = play;
        else { Current = play; _queued = null; }
        return checked((int)(start - Time) + Duration(play.Record));
    }
    // 0x1abc08 writes the pending slot sentinel (12); it does not stop current playback.
    public void FlushAnimation() => _queued = null;
    public bool TryEffect(RseOpcode opcode, IReadOnlyList<int> arguments)
    {
        // Accepted only for the explicitly listed presentation services in RseMachine.
        LastEffect = new Effect(Time, opcode, Array.AsReadOnly(arguments.ToArray()));
        EffectRequested?.Invoke(LastEffect);
        return true;
    }

    /// <summary>⭐⭐ THE OTHER CHANNELS ARE REAL, AND THEY ARE NOT DRAWN. Inca Totem asks for
    /// `TRIGANIM_CH 5 1 0 1`, `5 4 0 2` and `5 7 0 3` -- three variants of one slot playing at
    /// once, one per totem head. The script genuinely needs all three to run, so they are kept
    /// here and their durations are honest.
    ///
    /// ⚠⚠ BUT A RENDERER THAT DRAWS ONLY <see cref="Current"/> SHOWS ONE THIRD OF THAT RIDE, and
    /// it will look finished while it is not. That is what <see cref="Channels"/> is for: a view
    /// can ask how many are running and say so, rather than the ride quietly losing two heads.
    /// Accepting a channel means the script no longer stops -- it does not mean the ride is
    /// drawn right.</summary>
    readonly Dictionary<int, Playback> _channels = new();
    public IReadOnlyDictionary<int, Playback> Channels => _channels;

    public int PlayAnimationOn(int channel, int slot, int variant, bool loop)
    {
        if (channel == 0) return PlayAnimation(slot, variant, loop);
        // Same rule as channel 0: a record the model does not have costs a second and plays
        // nothing, rather than stopping the script.
        if (!_slots.TryGetValue(slot, out var records) || variant < 0 || variant >= records.Length)
            return MissingAnimationMilliseconds;
        _channels[channel] = new Playback(records[variant], variant, Time, loop);
        return Duration(records[variant]);
    }

    /// <summary>How much longer this channel has, or -1 when it is idle or done. ⚠ A LOOP NEVER
    /// FINISHES, so it reports the time left in the current turn of the loop and never -1 --
    /// otherwise a script waiting for a looping animation to end would sail straight past it.</summary>
    public int AnimationRemainingOn(int channel)
    {
        var p = channel == 0 ? Current : (_channels.TryGetValue(channel, out var c) ? c : null);
        if (p == null) return -1;
        int length = Duration(p.Record);
        if (p.Loop) return length <= 0 ? 0 : checked((int)(length - (Time - p.Start) % length));
        long ends = p.Start + length;
        return Time >= ends ? -1 : checked((int)(ends - Time));
    }

    /// <summary>How far through its own animation a channel is, in APS frames.</summary>
    public float FrameOn(int channel)
    {
        if (!_channels.TryGetValue(channel, out var p)) return 0;
        double frame = (Time - p.Start) * Animation.Fps / 1000d;
        int duration = p.Record.DurationFrames;
        return (float)(p.Loop && duration > 0 ? frame % duration : Math.Min(frame, duration));
    }

    /// <summary>Where the caller says a node is, or null when it does not know. ⭐ A viewer that
    /// has the placed model can answer this from `Model.FindFitting(node, space)` and the node's
    /// world transform; a headless caller leaves it null and every walk runs at the floor, which
    /// is what <see cref="RseMachine.WalksAreTimed"/> reports.
    ///
    /// ⚠ ENGINE-FREE ON PURPOSE. A delegate rather than a model reference, so this file still
    /// knows nothing about Godot or about how a ride is drawn.</summary>
    public Func<int, int, (float X, float Y, float Z)?> NodeSource { get; set; }

    public bool TryNodePosition(int node, int space, out float x, out float y, out float z)
    {
        if (NodeSource?.Invoke(node, space) is { } p) { x = p.X; y = p.Y; z = p.Z; return true; }
        x = y = z = 0f;
        return false;
    }

    public sealed record Walker(long Time, int Guest, int FromNode, int ToNode, int Mode, int PerMille, int Angle);
    public event Action<Walker> WalkerMoved;
    /// <summary>Where each guest this script is moving was last put. The preview draws nothing;
    /// it records, so an audit can show a rider actually crossed from node to node.</summary>
    public IReadOnlyDictionary<int, Walker> Walkers => _walkers;
    readonly Dictionary<int, Walker> _walkers = new();

    public void WalkerPose(int guest, int fromNode, int toNode, int mode, int perMille, int angle)
    {
        var w = new Walker(Time, guest, fromNode, toNode, mode, perMille, angle);
        _walkers[guest] = w;
        WalkerMoved?.Invoke(w);
    }

    /// <summary>How many seats the thing being previewed has. ⭐ Set from the model's `0x80`
    /// fittings -- `Model.Fittings.Count(f => (f.Flags & 0x80) != 0)` -- because that is the list
    /// ADDHEAD's own handler indexes with `slot + 1`. Left at zero the ride seats nobody, which
    /// is what a preview with no model to ask should say.</summary>
    public int HeadSlots { get; set; }

    public sealed record Seat(long Time, int Slot, int Guest);
    public event Action<Seat> HeadChanged;
    /// <summary>Who is in which seat. The preview draws nobody; it records, so an audit can show
    /// that a ride running with eight aboard really did seat eight.</summary>
    public IReadOnlyDictionary<int, int> Seats => _seats;
    readonly Dictionary<int, int> _seats = new();

    public void HeadAt(int slot, int guest)
    {
        if (guest == 0) _seats.Remove(slot); else _seats[slot] = guest;
        HeadChanged?.Invoke(new Seat(Time, slot, guest));
    }

    public sealed record Hidden(long Time, int Guest, bool Visible);
    /// <summary>Which guests this script has taken out of sight, and when. The preview draws
    /// nobody; it records, so an audit can show a guest actually went into the shop.</summary>
    public IReadOnlyDictionary<int, Hidden> Visibility => _visible;
    readonly Dictionary<int, Hidden> _visible = new();
    public void GuestVisible(int guest, bool visible) => _visible[guest] = new Hidden(Time, guest, visible);
}

/// <summary>Small, explicit host scenario for the preview: open after construction, offer two
/// guest IDs, acknowledge each unloading ID, then close. The bytecode owns the ride lifecycle.</summary>
public sealed class RseRidePreview
{
    public RseMachine Machine { get; }
    public RsePreviewHost Host { get; }
    public int Boarded { get; private set; }
    public int Unloaded { get; private set; }
    public bool Completed => Unloaded == 2 && Machine[5] == 0;
    bool _offered;
    public RseRidePreview(RseProgram program, Animation animation)
    {
        Host = new RsePreviewHost(animation); Machine = new RseMachine(program, Host);
        Machine[2] = 2; Machine[3] = 1; Machine[6] = 1;
    }
    public void Tick(long milliseconds)
    {
        if (milliseconds >= 5000 && Unloaded == 0) Machine[6] = 0;
        if (_offered && Machine[0] == 0) { Boarded++; _offered = false; }
        if (!_offered && Boarded < 2 && milliseconds >= 6000 + Boarded * 1000)
        {
            Machine[0] = 101 + Boarded; _offered = true;
        }
        if (Machine[1] != 0)
        {
            int expected = 102 - Unloaded;
            if (Machine[1] != expected) throw new InvalidDataException($"Unloading guest {Machine[1]}, expected {expected}");
            Machine[1] = 0; Unloaded++; Machine[6] = 1;
        }
        Machine.RunSlice(milliseconds);
    }
}
