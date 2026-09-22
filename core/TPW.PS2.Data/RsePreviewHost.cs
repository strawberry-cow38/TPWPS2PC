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
    public int PlayAnimation(int slot, int variant, bool loop)
    {
        if (!_slots.TryGetValue(slot, out var records) || variant < 0 || variant >= records.Length)
            throw new NotSupportedException($"APS has no animation {slot}:{variant}");
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
