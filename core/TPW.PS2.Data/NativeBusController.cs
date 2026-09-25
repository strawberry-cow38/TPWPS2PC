namespace TPW.PS2.Data;

/// <summary>Native bus 14C22C..350 and its bounded type15/channel0 playback.
/// Animation time is pause-gated elapsed milliseconds; countdown time is the separate
/// signed 397640 delta. The caller must not substitute one for the other.
/// No batch size, audio graph, traffic simulation or scenario defaults are invented here.</summary>
public sealed class NativeBusController
{
    readonly Animation animation;
    readonly IReadOnlyList<(int Count, int Offset)> sections;
    readonly Action<Animation.Record> bind;
    readonly Action<float> sample;
    readonly Action<int, uint> stateCommand;
    readonly Action<int> requestBatch;
    uint startClock;
    int appliedState = -1;
    public int State { get; private set; }
    /// <summary>Batch requests the 30-guest departure-pressure test (14C2EC) refused while the park was
    /// open. Instrumentation for the soak; nothing reads it to decide.</summary>
    public int PressureVetoes { get; private set; }
    public int AppliedState => appliedState;
    public int OuterRemaining { get; private set; }
    public int DwellRemaining { get; private set; }
    public Animation.Record Record { get; private set; }
    public bool Active => Record != null; // channel section !=12; initial channel is12
    public bool EndHold { get; private set; }
    public float Frame { get; private set; }
    public int RejectedCommands { get; private set; }
    public const int DwellCountdown = 0x50000, OuterCountdown = 0xC8000;

    public NativeBusController(Animation animation, uint initialRandomRaw, uint clock,
        Action<Animation.Record> bind, Action<float> sample, Action<int,uint> stateCommand,
        Action<int> requestBatch)
    {
        this.animation = animation ?? throw new ArgumentNullException(nameof(animation));
        sections = animation.Sections();
        if (sections.Count != 12 || sections[5].Count != 3)
            throw new InvalidDataException("native bus requires the verified twelve-section/three-main-record resource");
        this.bind = bind ?? throw new ArgumentNullException(nameof(bind));
        this.sample = sample ?? throw new ArgumentNullException(nameof(sample));
        this.stateCommand = stateCommand ?? throw new ArgumentNullException(nameof(stateCommand));
        this.requestBatch = requestBatch ?? throw new ArgumentNullException(nameof(requestBatch));
        // 149C74 DIVU /149C8C MFHI /149C94 SW: unshifted remainder.
        // Reload C8000 being numerically 200<<12 does NOT scale this initializer.
        OuterRemaining = (int)(initialRandomRaw % 200);
        ApplyState(clock); // creation147760 calls state0 before controller updates
    }

    static float UnsignedFloat(uint v) => v <= int.MaxValue ? (float)(int)v : (float)(int)((v >> 1) | (v & 1)) * 2;
    public static float ElapsedFrames(uint clock, uint start) => UnsignedFloat(unchecked(clock - start)) * 30 / 1000;

    /// <summary>Renderer-only fractional sampling of the current authored record. This does
    /// not execute Update, change Frame/EndHold, consume countdowns or invoke callbacks.
    /// It may reach the record endpoint before the next simulation tick, but cannot bind
    /// the next record or admit guests. Held/rejected state0 retains its existing pose.</summary>
    public float PresentationFrame(uint activeMilliseconds, float fractionalMilliseconds = 0)
    {
        if (!float.IsFinite(fractionalMilliseconds) || fractionalMilliseconds < 0 || fractionalMilliseconds > 1)
            throw new ArgumentOutOfRangeException(nameof(fractionalMilliseconds));
        if (!Active || EndHold) return Frame;
        return Math.Min(Record.DurationFrames,
            ElapsedFrames(activeMilliseconds, startClock) + fractionalMilliseconds * (30f / 1000));
    }

    void ApplyState(uint clock)
    {
        if (appliedState == State) return;
        stateCommand(State, clock);
        int section = State == 0 ? 12 : 5, variant = State == 0 ? 0 : State - 1;
        // Crucial caller guard17C7B0..E0: special12 is rejected for these APS files.
        // Therefore state0 does NOT invoke lower-level cleanup, reset pose or reveal meshes.
        if (section >= sections.Count || variant >= sections[section].Count) RejectedCommands++;
        else
        {
            // In this controller every new record follows inactive/end-hold, so 1ABC80's
            // pending-request branch cannot be reached. Do not implement a guessed FIFO.
            if (Active && !EndHold) throw new InvalidOperationException("bus attempted to replace unfinished native playback");
            Record = animation.ReadRecord(sections[section].Offset + variant * 0x1c);
            Record.Slot = section;
            startClock = clock; Frame = 0; EndHold = false;
            bind(Record); // activation is not itself a frame-zero sample
        }
        appliedState = State;
    }

    public int Update(uint activeMilliseconds, int countdownDelta, int traffic,
        bool open, bool specialObjectAbsent, int flaggedGuestCount)
    {
        // 147828 runs before either countdown, so animations keep advancing during waits.
        if (Active && !EndHold)
        {
            Frame = ElapsedFrames(activeMilliseconds, startClock);
            if (Record.DurationFrames < Frame)
            {
                EndHold = true;
                Frame = Record.DurationFrames; // ordinary nonloop endpoint1AC298/1AC360
            }
            sample(Frame);
        }
        if (OuterRemaining > 0) { OuterRemaining = unchecked(OuterRemaining - countdownDelta); return traffic; }
        if (DwellRemaining > 0) { DwellRemaining = unchecked(DwellRemaining - countdownDelta); return traffic; }
        ApplyState(activeMilliseconds);
        // The state command is BEFORE this guard: blocked traffic does not prevent 5:0 binding.
        if (State == 1 && traffic != 0 && traffic != 2) return traffic;
        traffic = State == 2 ? 2 : 0;
        if (!EndHold && Active) return traffic;
        if (State == 2)
        {
            if (open && specialObjectAbsent && flaggedGuestCount < 30) requestBatch(0);
            else if (open && specialObjectAbsent) PressureVetoes++;
            DwellRemaining = DwellCountdown; // also when the batch is denied
        }
        if (++State > 3) { State = 0; OuterRemaining = OuterCountdown; }
        return traffic;
    }
}
