using System.Text.Json.Serialization;
namespace TPW.PS2.Data;

public sealed partial class GuestDecisionSchedule
{
    public const int StateVersion = 1;
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GateState
    {
        public required int Guest { get; init; }
        public required uint Deadline { get; init; }
        public required uint LastTick { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required GateState[] Gates { get; init; }
        public required IntMapLayout Layout {get;init;}
    }
    public State CaptureState() => new() { Version = StateVersion, Layout=gates.CaptureLayout(),
        Gates = gates.Select(p => new GateState { Guest = p.Key, Deadline = p.Value.Deadline, LastTick = p.Value.LastTick }).ToArray() };
    /// <summary>The supplied provider is the OWNER's restored stream, not a new stream.
    /// This constructor does not draw, compute deadlines or call Completed.</summary>
    public static GuestDecisionSchedule FromState(State state, Func<int> random)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Version != StateVersion || state.Gates == null || state.Gates.Length > 100_000)
            throw new ArgumentException("Invalid decision schedule schema/bounds.");
        var copy = new Dictionary<int, Gate>();
        foreach (var g in state.Gates)
            if (g == null || !copy.TryAdd(g.Guest, new Gate { Deadline = g.Deadline, LastTick = g.LastTick }))
                throw new ArgumentException("Invalid/duplicate decision gate.");
        var result = new GuestDecisionSchedule(random);
        foreach (var g in copy) result.gates.Add(g.Key, g.Value);
        result.gates.RestoreLayout(state.Layout);
        return result;
    }
}
