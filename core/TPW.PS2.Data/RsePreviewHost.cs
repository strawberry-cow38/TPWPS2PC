namespace TPW.PS2.Data;

/// <summary>APS-backed single-channel preview host. It queues an animation behind an unfinished
/// one-shot and replaces loops immediately. Effects are surfaced as events for a renderer/audio
/// host; this preview explicitly records them without rendering sound, particles or guest heads.</summary>
public sealed class RsePreviewHost : IRseHost
{
    public sealed record Playback(Animation.Record Record, int Variant, long Start, bool Loop);
    public sealed record Effect(long Time, RseOpcode Opcode, IReadOnlyList<int> Arguments);
    readonly Dictionary<int, Animation.Record[]> _slots;
    Playback _queued;
    public Playback Current { get; private set; }
    public long Time { get; private set; }
    public int AnimationSlot => Current?.Record.Slot ?? -1;
    public int AnimationVariant => Current?.Variant ?? -1;
    public event Action<Effect> EffectRequested;
    public Effect LastEffect { get; private set; }

    public RsePreviewHost(Animation animation) => _slots = animation?.Records()
        .GroupBy(r => r.Slot).ToDictionary(g => g.Key, g => g.ToArray()) ?? new();

    // 0x1abbb4 -> 0x297b68 converts the float to an unsigned integer by truncation.
    static int Duration(Animation.Record r) => checked((int)(r.DurationFrames * 1000f / Animation.Fps));
    public float Frame
    {
        get
        {
            if (Current == null) return 0;
            double frame = (Time - Current.Start) * Animation.Fps / 1000d;
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

    public int PlayAnimation(int slot, int variant, bool loop)
    {
        if (!_slots.TryGetValue(slot, out var records) || variant < 0 || variant >= records.Length)
        {
            // Nothing is started and nothing already playing is disturbed.
            long unfinished = Current is { Loop: false }
                ? Math.Max(0, Current.Start + Duration(Current.Record) - Time) : 0;
            return checked((int)unfinished) + MissingAnimationMilliseconds;
        }
        if (loop && Current is { Loop: true } && AnimationSlot == slot && AnimationVariant == variant && _queued == null)
            return Duration(Current.Record);
        long start = Time;
        if (Current is { Loop: false }) start = Math.Max(start, Current.Start + Duration(Current.Record));
        var play = new Playback(records[variant], variant, start, loop);
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

    /// <summary>⚠ THE PREVIEW CANNOT PLACE A NODE. The APS gives this host frames, not the park's
    /// node table, so every walk here runs at the minimum leg time. Said through the return value
    /// rather than by inventing an origin, so `RseMachine.WalksAreTimed` stays honest.</summary>
    public bool TryNodePosition(int node, int space, out float x, out float y, out float z)
    {
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
