using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public sealed partial class NativeGuestRoute
{
    public const int StateVersion = 1;

    // Point is deliberately not serialized directly: missing X/Z must not become zero.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PointState
    {
        public required short X { get; init; }
        public required short Z { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required PointState Position { get; init; }
        public required ulong Generation { get; init; }
        public required bool Disposed { get; init; }
        public required int ExecutionState { get; init; }
        public required int SlotIndex { get; init; }
        public required bool Finished { get; init; }
        public required bool Failed { get; init; }
        public required int FacingQuarterTurns { get; init; }
    }

    /// <summary>The pool is a separate shared owner; targets and freed-slot bits live there.</summary>
    public State CaptureState() => new()
    {
        Version = StateVersion, Position = new() { X = Position.X, Z = Position.Z },
        Generation = generation, Disposed = Disposed, ExecutionState = ExecutionState,
        SlotIndex = SlotIndex, Finished = Finished, Failed = Failed, FacingQuarterTurns = FacingQuarterTurns
    };

    /// <summary>Adopt a staged pool without allocating, freeing, resetting or stepping it.
    /// The enclosing graph must additionally enforce exclusive ownership between cursors.</summary>
    public static NativeGuestRoute FromState(State state, NativeRoutePool pool)
    {
        ValidateState(state, pool);
        return new NativeGuestRoute(state, pool);
    }

    private NativeGuestRoute(State s, NativeRoutePool pool)
    {
        Pool = pool; generation = s.Generation; Disposed = s.Disposed;
        Position = new(s.Position.X, s.Position.Z); ExecutionState = s.ExecutionState;
        SlotIndex = s.SlotIndex; Finished = s.Finished; Failed = s.Failed;
        FacingQuarterTurns = s.FacingQuarterTurns;
    }

    internal static void ValidateState(State s, NativeRoutePool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        void Require(bool ok, string why)
        { if (!ok) throw new ArgumentException("Invalid native cursor state: " + why); }
        Require(s != null && s.Version == StateVersion && s.Position != null, "schema/position");
        Require(s.SlotIndex >= -1 && s.SlotIndex < NativeRoutePool.Capacity
            && s.ExecutionState is 0 or 2 or 3 && (uint)s.FacingQuarterTurns <= 3, "cursor bounds");
        Require(s.Failed == (s.ExecutionState == 0) && !(s.Finished && s.Failed)
            && (!s.Finished || s.ExecutionState == 2), "execution flags");
        Require(!(s.Disposed || s.Failed || s.Finished) || s.SlotIndex == -1, "terminal slot");
        if (s.SlotIndex < 0) return;
        Require(s.Generation == pool.ResetGeneration, "stale epoch");
        var seen = new HashSet<int>();
        for (int slot = s.SlotIndex; slot != -1;)
        {
            Require(pool.IsAllocated(slot) && seen.Add(slot), "inactive/cyclic chain");
            // Read raw successor so an inactive next becomes a DTO validation error.
            int next = (int)(pool.ReadWord(slot) & 0x7ff);
            Require(next == 0x7ff || next < NativeRoutePool.Capacity, "link bounds");
            slot = next == 0x7ff ? -1 : next;
        }
    }
}
