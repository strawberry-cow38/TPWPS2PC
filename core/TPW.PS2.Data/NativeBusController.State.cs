#nullable enable
using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public sealed partial class NativeBusController
{
    public sealed class SnapshotBindings
    {
        /// <summary>Stable identity of the externally staged callback owners. Their state is not in this DTO.</summary>
        public required string ServicesId { get; init; }
        public required Action<Animation.Record> Bind { get; init; }
        public required Action<float> Sample { get; init; }
        public required Action<int, uint> StateCommand { get; init; }
        public required Action<int> RequestBatch { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required string ServicesId { get; init; }
        public required string AnimationFingerprint { get; init; }
        /// <summary>Index within section 5, or -1 for the initial inactive channel.</summary>
        public required int RecordVariant { get; init; }
        public required int State { get; init; }
        public required int AppliedState { get; init; }
        public required uint StartClock { get; init; }
        public required int OuterRemaining { get; init; }
        public required int DwellRemaining { get; init; }
        public required bool EndHold { get; init; }
        public required float Frame { get; init; }
        public required int PressureVetoes { get; init; }
        public required int RejectedCommands { get; init; }
    }

    public Snapshot CaptureState(string servicesId)
    {
        NativeAnimationSnapshotAssets.Identity(servicesId);
        int variant = -1;
        if (Record != null)
        {
            for (int i = 0; i < sections[5].Count; i++)
                if (Record.Offset == sections[5].Offset + i * 0x1c) variant = i;
            if (variant < 0 || Record.Slot != 5) throw new InvalidDataException("bus record is not in its supplied APS");
            NativeAnimationSnapshotAssets.SameRecord(Record,
                NativeAnimationSnapshotAssets.Record(animation, 5, variant));
        }
        var s = new Snapshot { Version = 1, ServicesId = servicesId,
            AnimationFingerprint = NativeAnimationSnapshotAssets.Fingerprint(animation), RecordVariant = variant,
            State = State, AppliedState = appliedState, StartClock = startClock,
            OuterRemaining = OuterRemaining, DwellRemaining = DwellRemaining, EndHold = EndHold,
            Frame = Frame, PressureVetoes = PressureVetoes, RejectedCommands = RejectedCommands };
        ValidateState(s, animation);
        return s;
    }

    public static void ValidateState(Snapshot s, Animation animation)
    {
        ArgumentNullException.ThrowIfNull(s);
        NativeAnimationSnapshotAssets.Identity(s.ServicesId);
        NativeAnimationSnapshotAssets.Match(s.AnimationFingerprint, NativeAnimationSnapshotAssets.Fingerprint(animation));
        var sections = NativeAnimationSnapshotAssets.Sections(animation);
        if (s.Version != 1 || sections.Count != 12 || sections[5].Count != 3 ||
            (uint)s.State > 3 || (uint)s.AppliedState > 3 ||
            (s.State != s.AppliedState && s.State != (s.AppliedState + 1) % 4) ||
            s.RecordVariant < -1 || s.RecordVariant > 2 || !float.IsFinite(s.Frame) || s.Frame < 0)
            throw new InvalidDataException("invalid bus snapshot");
        if (s.RecordVariant == -1)
        {
            if (s.AppliedState != 0 || s.EndHold || s.Frame != 0 || s.StartClock != 0)
                throw new InvalidDataException("invalid inactive bus channel");
        }
        else
        {
            var r = NativeAnimationSnapshotAssets.Record(animation, 5, s.RecordVariant);
            if (s.RecordVariant != (s.AppliedState == 0 ? 2 : s.AppliedState - 1) ||
                s.Frame > r.DurationFrames || (s.EndHold && s.Frame != r.DurationFrames) ||
                (s.State != s.AppliedState && !s.EndHold) ||
                (s.AppliedState == 0 && !s.EndHold))
                throw new InvalidDataException("invalid bus record position");
        }
        // Both clocks/countdowns and instrumentation are native wrapping integers, not elapsed wall time.
    }

    /// <summary>Pure restore: never calls creation ApplyState, Bind, Sample, StateCommand or RequestBatch.
    /// Rendering must rebuild its own pose explicitly after the entire staged graph is accepted.</summary>
    public static NativeBusController FromState(Snapshot s, Animation animation, SnapshotBindings bindings)
    {
        ValidateState(s, animation);
        ArgumentNullException.ThrowIfNull(bindings);
        NativeAnimationSnapshotAssets.Identity(bindings.ServicesId);
        if (bindings.ServicesId != s.ServicesId) throw new InvalidDataException("bus callback owner identity mismatch");
        ArgumentNullException.ThrowIfNull(bindings.Bind);
        ArgumentNullException.ThrowIfNull(bindings.Sample);
        ArgumentNullException.ThrowIfNull(bindings.StateCommand);
        ArgumentNullException.ThrowIfNull(bindings.RequestBatch);
        return new NativeBusController(s, animation, bindings);
    }

    NativeBusController(Snapshot s, Animation asset, SnapshotBindings b)
    {
        animation = asset; sections = NativeAnimationSnapshotAssets.Sections(asset);
        bind = b.Bind; sample = b.Sample; stateCommand = b.StateCommand; requestBatch = b.RequestBatch;
        State = s.State; appliedState = s.AppliedState; startClock = s.StartClock;
        OuterRemaining = s.OuterRemaining; DwellRemaining = s.DwellRemaining;
        EndHold = s.EndHold; Frame = s.Frame; PressureVetoes = s.PressureVetoes; RejectedCommands = s.RejectedCommands;
        Record = s.RecordVariant < 0 ? null! : NativeAnimationSnapshotAssets.Record(asset, 5, s.RecordVariant);
    }
}
