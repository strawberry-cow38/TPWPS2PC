#nullable enable
using System.Text.Json.Serialization;
namespace TPW.PS2.Data;

public sealed partial class StaffCandidate
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required StaffKind Kind { get; init; }
        public required int Slot { get; init; }
        public required int NameRow { get; init; }
        public required int RecordConstant { get; init; }
        public required int PayGrade { get; init; }
        public required int Motivation { get; init; }
        public required bool Available { get; init; }
    }
    // Typed recreation preserves immutable authored data, independent of a disc or future table changes.
    StaffCandidate(State s)
    {
        Kind = s.Kind; Slot = s.Slot; NameRow = s.NameRow; RecordConstant = s.RecordConstant;
        PayGrade = s.PayGrade; Motivation = s.Motivation; Available = s.Available;
    }
    public State CaptureState() => new() { Version = 1, Kind = Kind, Slot = Slot, NameRow = NameRow,
        RecordConstant = RecordConstant, PayGrade = PayGrade, Motivation = Motivation, Available = Available };
    public static void ValidateState(State s)
    {
        ArgumentNullException.ThrowIfNull(s);
        StaffLeafBindings.Require(s.Version == 1 && Enum.IsDefined(s.Kind) && (uint)s.Slot < StaffTables.PoolSize,
            "candidate version/identity");
    }
    public static StaffCandidate FromState(State s) { ValidateState(s); return new(s); }
}
public sealed partial class StaffCandidateDatabase
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required StaffCandidate.State[] Records { get; init; }
    }
    StaffCandidateDatabase(StaffCandidate[] records) => _records = records;
    public State CaptureState() => new() { Version = 1, Records = _records.Select(r => r.CaptureState()).ToArray() };
    public static void ValidateState(State s)
    {
        ArgumentNullException.ThrowIfNull(s);
        StaffLeafBindings.Require(s.Version == 1 && s.Records != null && s.Records.Length == 5 * StaffTables.PoolSize,
            "candidate database schema/length");
        for (int i = 0; i < s.Records!.Length; i++) {
            StaffCandidate.ValidateState(s.Records[i]);
            StaffLeafBindings.Require((int)s.Records[i].Kind == i / StaffTables.PoolSize
                && s.Records[i].Slot == i % StaffTables.PoolSize, "candidate database order");
        }
    }
    public static StaffCandidateDatabase FromState(State s)
    { ValidateState(s); return new(s.Records.Select(StaffCandidate.FromState).ToArray()); }
}
