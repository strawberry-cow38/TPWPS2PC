namespace TPW.PS2.Data;

public sealed partial class ParkAdvisor
{
    bool _updating;
    bool _hydrated = true;
    public LipTrack LipTrackBinding => _lip?.Track;
    public sealed record LipState([property: System.Text.Json.Serialization.JsonRequired] string TrackId, [property: System.Text.Json.Serialization.JsonRequired] string Fingerprint, [property: System.Text.Json.Serialization.JsonRequired] int Next, [property: System.Text.Json.Serialization.JsonRequired] bool Active, [property: System.Text.Json.Serialization.JsonRequired] bool Stepped);
    public sealed record RingSlot([property: System.Text.Json.Serialization.JsonRequired] ushort Id, [property: System.Text.Json.Serialization.JsonRequired] AdvisorRecordType Kind, [property: System.Text.Json.Serialization.JsonRequired] string ObjectId);
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required string CatalogueId { get; init; }
        public required string CatalogueFingerprint { get; init; }
        public required string ClockId { get; init; }
        public required AdvisorScheduler.Snapshot Scheduler { get; init; }
        public required AdvisorMessageStack.CoreState Stack { get; init; }
        public required RingSlot[] Ring { get; init; }
        public required byte[] Counts { get; init; }
        public required byte[] Variants { get; init; }
        public required AdvisorRecordType PendingKind { get; init; }
        public required string PendingObjectId { get; init; }
        public required LipState Lip { get; init; }
        public required uint? OwnedRandom { get; init; }
        public required bool UseOwnedRandom { get; init; }
        public required bool TestPark { get; init; }
        public required byte Flags { get; init; }
        public required byte SavedFlags { get; init; }
        public required AdvisorState State { get; init; }
        public required short Countdown { get; init; }
        public required int RingHead { get; init; }
        public required int RingTail { get; init; }
        public required bool PendingSet { get; init; }
        public required ushort PendingId { get; init; }
        public required int Overflows { get; init; }
        public required ushort CurrentId { get; init; }
        public required bool Modal { get; init; }
        public required bool PadLocked { get; init; }
        public required bool CancelLabelShown { get; init; }
        public required bool SkipLatched { get; init; }
        public required bool EnterPlayed { get; init; }
        public required int Costume { get; init; }
        public required bool Ducking { get; init; }
        public required bool Speaking { get; init; }
        public required ushort SpeechMessage { get; init; }
        public required int SpeechVariant { get; init; }
        public required ushort SpeechSoundId { get; init; }
        public required int SpeechLengthMs { get; init; }
        public required int SpeechElapsedMs { get; init; }
        public required bool LipGate { get; init; }
        public required int MouthShape { get; init; }
        public required long MouthChanges { get; init; }
        public required long Ticks { get; init; }
        public required int MillisecondsPerTick { get; init; }
        public required bool SkipHeld { get; init; }
        public required string SpeechLengthId { get; init; }
        public required string HeadId { get; init; }
        public required string GoalsAlreadyAchievedId { get; init; }
        public required string GoldTicketsEarnedId { get; init; }
        public required string GoalNoticesId { get; init; }
        public required string GameOverId { get; init; }
        public required string UiSoundId { get; init; }
        public required string SubmittedId { get; init; }
        public required string PlayedId { get; init; }
        public required string LipsId { get; init; }
        public required string RandId { get; init; }
        public required string SpeechStoppedId { get; init; }
    }
    public static string Fingerprint(AdvisorCatalogue catalogue) => AdvisorStateBindings.Hash(w =>
    {
        w.Write(catalogue.Messages.Count);
        foreach (var m in catalogue.Messages)
        {
            w.Write(m.Id); w.Write(m.SymbolicKey ?? ""); w.Write(m.TextRow); w.Write(m.VariantCount); w.Write(m.InitialVariant);
            foreach (var v in m.Voices) { w.Write(v.SoundId); w.Write(v.AnimationSelector); w.Write(v.UnknownByte);
                w.Write(v.LipStem ?? ""); w.Write(v.SavedLipPointer); }
        }
    });
    static string LipFingerprint(LipTrack t) => AdvisorStateBindings.Hash(w =>
    { w.Write(t.Microseconds.Count); foreach (var mark in t.Microseconds) w.Write(mark); });
    /// <summary>CORE complete continuation, not native reset-on-load. Capture only between simulation/UI
    /// passes with the whole graph frozen. No concurrent Submit, Press, head updates or delegate mutation.
    /// External Head and non-default Rand are referenced nodes: root must snapshot their mutable state,
    /// never just bind a fresh adapter. Clock/producers and ride instances likewise belong to the root.
    /// Default mouth RNG and lip playback cursor are owned here. No GAME settings are captured or changed.</summary>
    public Snapshot CaptureState(AdvisorStateBindings b, bool quiescent)
    {
        if (!quiescent || _updating || !_hydrated) throw new InvalidOperationException("Advisor capture requires quiescence");
        bool ownRand = _mouthRand != null && Rand == (Func<int>)_mouthRand.Next;
        return new Snapshot { Version = 1, CatalogueId = b.Identify(_catalogue), CatalogueFingerprint = Fingerprint(_catalogue),
            ClockId = b.Identify(Clock), Scheduler = Scheduler?.CaptureState(b, true), Stack = Stack.CaptureCoreState(b, true),
            Ring = _ring.Select(r => new RingSlot(r.Id, r.Kind, b.Identify(r.Object))).ToArray(),
            Counts = (byte[])_count.Clone(), Variants = (byte[])_variant.Clone(), PendingKind = _pendingKind,
            PendingObjectId = b.Identify(_pendingObject), OwnedRandom = _mouthRand?.CaptureState(), UseOwnedRandom = ownRand,
            Lip = _lip == null ? null : new(b.Identify(_lip.Track), LipFingerprint(_lip.Track), _lip.NextIndex, _lip.Active, _lip.Stepped),
            TestPark = TestPark,
            Flags = Flags,
            SavedFlags = SavedFlags,
            State = State,
            Countdown = Countdown,
            RingHead = RingHead,
            RingTail = RingTail,
            PendingSet = PendingSet,
            PendingId = PendingId,
            Overflows = Overflows,
            CurrentId = CurrentId,
            Modal = Modal,
            PadLocked = PadLocked,
            CancelLabelShown = CancelLabelShown,
            SkipLatched = SkipLatched,
            EnterPlayed = EnterPlayed,
            Costume = Costume,
            Ducking = Ducking,
            Speaking = Speaking,
            SpeechMessage = SpeechMessage,
            SpeechVariant = SpeechVariant,
            SpeechSoundId = SpeechSoundId,
            SpeechLengthMs = SpeechLengthMs,
            SpeechElapsedMs = SpeechElapsedMs,
            LipGate = LipGate,
            MouthShape = MouthShape,
            MouthChanges = MouthChanges,
            Ticks = Ticks,
            MillisecondsPerTick = MillisecondsPerTick,
            SkipHeld = SkipHeld,
            SpeechLengthId = b.Identify(SpeechLength),
            HeadId = b.Identify(Head),
            GoalsAlreadyAchievedId = b.Identify(GoalsAlreadyAchieved),
            GoldTicketsEarnedId = b.Identify(GoldTicketsEarned),
            GoalNoticesId = b.Identify(GoalNotices),
            GameOverId = b.Identify(GameOver),
            UiSoundId = b.Identify(UiSound),
            SubmittedId = b.Identify(Submitted),
            PlayedId = b.Identify(Played),
            LipsId = b.Identify(Lips),
            RandId = ownRand ? null : b.Identify(Rand),
            SpeechStoppedId = b.Identify(SpeechStopped),
        };
    }
    /// <summary>Allocate before binding closures that reference this advisor. Not usable until Hydrate.
    /// Does not run the park constructor, initialize variants, read the clock or draw random numbers.</summary>
    public static ParkAdvisor AllocateShell() => new ParkAdvisor();
    ParkAdvisor() { _hydrated = false; }
    public static ParkAdvisor FromState(Snapshot state, AdvisorStateBindings bindings)
    { var shell = AllocateShell(); shell.Hydrate(state, bindings); return shell; }
    /// <summary>Validate and stage first, then write only this unpublished shell. Root rebinds
    /// ParkSim.ObjectRemoved and other subscriptions AFTER the entire graph validates; never call Attach.
    /// No game constructor, enqueue, step, audio, head command or event is replayed.</summary>
    public void Hydrate(Snapshot s, AdvisorStateBindings b)
    {
        if (_hydrated) throw new InvalidOperationException("Hydrate requires a fresh shell");
        if (s == null || s.Version != 1 || !Enum.IsDefined(s.State) || s.Ring?.Length != RingSlots ||
            (uint)s.RingHead >= RingSlots || (uint)s.RingTail >= RingSlots || s.Counts?.Length != AdvisorCatalogue.MessageCount ||
            s.Variants?.Length != AdvisorCatalogue.MessageCount || s.MillisecondsPerTick <= 0 || s.Ticks < 0 ||
            s.MouthChanges < 0 || s.Overflows < 0 || s.Costume < -1 || s.Costume > 255 || (uint)s.MouthShape > 4 ||
            s.SpeechElapsedMs < 0 || s.SpeechLengthMs < 0 || (uint)s.SpeechVariant > 3 ||
            s.SpeechMessage >= AdvisorCatalogue.MessageCount || s.CurrentId >= AdvisorCatalogue.MessageCount || s.PendingId >= AdvisorCatalogue.MessageCount || !Enum.IsDefined(s.PendingKind) ||
            s.Ring.Any(r => r == null || r.Id>=AdvisorCatalogue.MessageCount || !Enum.IsDefined(r.Kind)) || (s.UseOwnedRandom && (!s.OwnedRandom.HasValue || s.RandId != null)))
            throw new InvalidDataException("Invalid ParkAdvisor snapshot");
        for (int i = 0; i < s.Counts.Length; i++)
            if (s.Counts[i] > 4 || (s.Counts[i] == 0 ? s.Variants[i] != 0 : s.Variants[i] >= s.Counts[i]))
                throw new InvalidDataException("Invalid advisor variant");
        var catalogue = b.Required<AdvisorCatalogue>(s.CatalogueId);
        if (s.CatalogueFingerprint != Fingerprint(catalogue)) throw new InvalidDataException("Advisor catalogue mismatch");
        var clock = b.Required<ParkClock>(s.ClockId);
        var ring = s.Ring.Select(r => (r.Id, r.Kind, b.Resolve<object>(r.ObjectId))).ToArray();
        var pending = b.Resolve<object>(s.PendingObjectId);
        var scheduler = s.Scheduler == null ? null : AdvisorScheduler.FromState(s.Scheduler, b);
        var stack = AdvisorMessageStack.FromCoreState(s.Stack, b);
        LipTrack.Playback lip = null;
        if (s.Lip != null)
        {
            var track = b.Required<LipTrack>(s.Lip.TrackId);
            if (s.Lip.Fingerprint != LipFingerprint(track) || s.Lip.Next < 0 || s.Lip.Next > track.Microseconds.Count)
                throw new InvalidDataException("Invalid advisor lip progress");
            lip = LipTrack.Playback.FromState(track,s.Lip.Next,s.Lip.Active,s.Lip.Stepped);
        }
        var bindSpeechLength = b.Resolve<Func<int, int, ushort, int>>(s.SpeechLengthId);
        var bindHead = b.Resolve<IAdvisorHead>(s.HeadId);
        var bindGoalsAlreadyAchieved = b.Resolve<Func<bool>>(s.GoalsAlreadyAchievedId);
        var bindGoldTicketsEarned = b.Resolve<Func<int>>(s.GoldTicketsEarnedId);
        var bindGoalNotices = b.Resolve<Action>(s.GoalNoticesId);
        var bindGameOver = b.Resolve<Action>(s.GameOverId);
        var bindUiSound = b.Resolve<Action<int>>(s.UiSoundId);
        var bindSubmitted = b.Resolve<Action<AdvisorRequest, bool>>(s.SubmittedId);
        var bindPlayed = b.Resolve<Action<AdvisorPlayback>>(s.PlayedId);
        var bindLips = b.Resolve<Func<int, int, LipTrack>>(s.LipsId);
        var bindRand = b.Resolve<Func<int>>(s.RandId);
        var bindSpeechStopped = b.Resolve<Action>(s.SpeechStoppedId);
        // Commit: every lookup and every validation above is complete.
        _catalogue = catalogue; Clock = clock; Scheduler = scheduler; Stack = stack;
        Array.Copy(ring, _ring, RingSlots); _count = (byte[])s.Counts.Clone(); _variant = (byte[])s.Variants.Clone();
        _pendingKind = s.PendingKind; _pendingObject = pending; _lip = lip;
        TestPark = s.TestPark;
        Flags = s.Flags;
        SavedFlags = s.SavedFlags;
        State = s.State;
        Countdown = s.Countdown;
        RingHead = s.RingHead;
        RingTail = s.RingTail;
        PendingSet = s.PendingSet;
        PendingId = s.PendingId;
        Overflows = s.Overflows;
        CurrentId = s.CurrentId;
        Modal = s.Modal;
        PadLocked = s.PadLocked;
        CancelLabelShown = s.CancelLabelShown;
        SkipLatched = s.SkipLatched;
        EnterPlayed = s.EnterPlayed;
        Costume = s.Costume;
        Ducking = s.Ducking;
        Speaking = s.Speaking;
        SpeechMessage = s.SpeechMessage;
        SpeechVariant = s.SpeechVariant;
        SpeechSoundId = s.SpeechSoundId;
        SpeechLengthMs = s.SpeechLengthMs;
        SpeechElapsedMs = s.SpeechElapsedMs;
        LipGate = s.LipGate;
        MouthShape = s.MouthShape;
        MouthChanges = s.MouthChanges;
        Ticks = s.Ticks;
        MillisecondsPerTick = s.MillisecondsPerTick;
        SkipHeld = s.SkipHeld;
        SpeechLength = bindSpeechLength;
        Head = bindHead;
        GoalsAlreadyAchieved = bindGoalsAlreadyAchieved;
        GoldTicketsEarned = bindGoldTicketsEarned;
        GoalNotices = bindGoalNotices;
        GameOver = bindGameOver;
        UiSound = bindUiSound;
        Submitted = bindSubmitted;
        Played = bindPlayed;
        Lips = bindLips;
        Rand = bindRand;
        SpeechStopped = bindSpeechStopped;
        if (s.OwnedRandom is uint random) _mouthRand = new NewlibRand(random);
        if (s.UseOwnedRandom) Rand = _mouthRand.Next;
        _hydrated = true;
    }
}

/// <summary>The root can snapshot an external timed head's channel with this DTO; no animation is replayed.
/// For a game head, the root/view owns the additional model state and must stage it separately.</summary>
public sealed partial class AdvisorHeadChannel
{
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required int Record { get; init; }
        public required bool Looping { get; init; }
        public required bool EndHeld { get; init; }
        public required int ElapsedMs { get; init; }
        public required int Queued { get; init; }
        public required int QueuedFlags { get; init; }
        public required int Starts { get; init; }
    }
    public Snapshot CaptureState() => new() { Version = 1, Record = Record, Looping = Looping, EndHeld = EndHeld,
        ElapsedMs = ElapsedMs, Queued = Queued, QueuedFlags = QueuedFlags, Starts = Starts };
    /// <summary>Only on an unpublished staged head; authored length delegate is separately bound by its owner.</summary>
    public void RestoreState(Snapshot s)
    {
        static bool Valid(int r) => r == None || r is >= 0 and <= 6 || r == 13 || r == 14;
        if (s == null || s.Version != 1 || !Valid(s.Record) || !Valid(s.Queued) || s.ElapsedMs < 0 ||
            s.Starts < 0 || (s.QueuedFlags & ~3) != 0) throw new InvalidDataException("Invalid advisor head channel");
        Record = s.Record; Looping = s.Looping; EndHeld = s.EndHeld; ElapsedMs = s.ElapsedMs;
        Queued = s.Queued; QueuedFlags = s.QueuedFlags; Starts = s.Starts;
    }
}
