using System.Text.Json.Serialization;
namespace TPW.PS2.Data;

public sealed partial class ReliefServiceClock
{
    public const int StateVersion = 1;
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required uint Deadline { get; init; }
        public required uint LastTick { get; init; }
        public required bool Finishing { get; init; }
        public required bool Completed { get; init; }
    }
    public State CaptureState() => new() { Version = StateVersion, Deadline = Deadline,
        LastTick = LastTick, Finishing = Finishing, Completed = Completed };
    private ReliefServiceClock(State s)
    { Deadline = s.Deadline; LastTick = s.LastTick; Finishing = s.Finishing; Completed = s.Completed; }
    public static ReliefServiceClock FromState(State state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Version != StateVersion || state.Completed && !state.Finishing)
            throw new ArgumentException("Invalid relief clock state.");
        return new ReliefServiceClock(state);
    }
}
