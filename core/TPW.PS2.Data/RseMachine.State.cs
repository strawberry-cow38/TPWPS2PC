using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public sealed partial class RseMachine
{
    internal IRseHost SnapshotHost => _host;
    // Count both copied state and declaration-backed allocations on staging; keep heads lazy.
    internal long SnapshotValueCount => 2L * (_variables.Length + (long)_stack.Length + _walks.Length
        + _bounce.Length + _limbo.Length) + (_heads?.Length ?? Math.Max(0, _host?.HeadSlots ?? 0));

    public const int StateVersion = 1;

    /// <summary>ExternalCallerManaged contains NO stream state. Save and restore that shared
    /// stream separately, and explicitly acknowledge doing so on RestoreState.</summary>
    public enum RandomStateKind { OwnedSnapshotRandom, ExternalCallerManaged }

    /// <summary>VM-only state: no bytecode, host, directory, delegate, or live references.
    /// Required includes nullable members: absent is not null. Arrays are detached on capture
    /// and restore. Heads == null preserves the not-yet-allocated lazy table.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class State
    {
        public required int Version { get; init; }
        public required string ProgramKey { get; init; }
        public required int VariableCount { get; init; }
        public required int StackSize { get; init; }
        public required int WalkCapacity { get; init; }
        public required int BounceCapacity { get; init; }
        public required int LimboCapacity { get; init; }
        public required int SliceBudget { get; init; }
        public required int CodeWords { get; init; }
        public required int Pc { get; init; }
        public required int LastValue { get; init; }
        public required long Time { get; init; }
        public required string Name { get; init; }
        public required string Fault { get; init; }
        public required bool Critical { get; init; }
        public required RseYield Yield { get; init; }
        public required int CallTop { get; init; }
        public required int GuestTop { get; init; }
        public required long? WaitUntil { get; init; }
        public required long? AnimationUntil { get; init; }
        public required int? TriggerSlot { get; init; }
        public required int LoopSlot { get; init; }
        public required int LoopVariant { get; init; }
        public required uint Timer { get; init; }
        public required short BounceBase { get; init; }
        public required short Bouncing { get; init; }
        public required short BumpRate { get; init; }
        public required short SparkFrom { get; init; }
        public required short SparkTo { get; init; }
        public required short FloatA { get; init; }
        public required short FloatB { get; init; }
        public required int FloatFor { get; init; }
        public required long FloatFrom { get; init; }
        public required byte Turbo { get; init; }
        public required int BounceNode { get; init; }
        public required int LimboUsed { get; init; }
        public required bool TimedWalk { get; init; }
        public required bool AttemptedWalk { get; init; }
        public required int[] Variables { get; init; }
        public required int[] Stack { get; init; }
        public required int[] Heads { get; init; }
        public required WalkState[] Walks { get; init; }
        public required BouncerState[] Bounce { get; init; }
        public required LimboSlotState[] Limbo { get; init; }
        public required string ParentId { get; init; }
        public required string ChildId { get; init; }
        public required string SoundChildId { get; init; }
        public required RandomStateKind RandomKind { get; init; }
        public required SnapshotRandom.State Random { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public readonly struct WalkState
    {
        public required int Guest { get; init; }
        public required short A { get; init; }
        public required short B { get; init; }
        public required short C { get; init; }
        public required short D { get; init; }
        public required short Kind { get; init; }
        public required short Extra { get; init; }
        public required short Angle { get; init; }
        public required short State { get; init; }
        public required long Start { get; init; }
        public required long End { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public readonly struct BouncerState
    {
        public required int Guest { get; init; }
        public required int Node { get; init; }
        public required long End { get; init; }
        public required long Start { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public readonly struct LimboSlotState
    {
        public required int Guest { get; init; }
        public required long Due { get; init; }
    }

    /// <summary>Call at a scheduler boundary, not concurrently with ticks. programKey must
    /// identify the exact asset revision, not merely matching declarations. identify returns
    /// stable, nonempty, unique IDs. Null links never invoke it.</summary>
    public State CaptureState(string programKey, Func<RseMachine, string> identify)
    {
        if (string.IsNullOrWhiteSpace(programKey)) throw new ArgumentException("A program key is required.", nameof(programKey));
        var ids = new Dictionary<RseMachine, string>();
        string Id(RseMachine machine)
        {
            if (machine == null) return null;
            if (ids.TryGetValue(machine, out var existing)) return existing;
            if (identify == null) throw new ArgumentNullException(nameof(identify));
            string id = identify(machine);
            if (string.IsNullOrWhiteSpace(id) || ids.ContainsValue(id))
                throw new ArgumentException("Machine IDs must be nonempty and unique.", nameof(identify));
            ids.Add(machine, id);
            return id;
        }
        return new State
        {
            Version = StateVersion, ProgramKey = programKey,
            VariableCount = Program.VariableCount,
            StackSize = Program.StackSize,
            WalkCapacity = Program.WalkCapacity,
            BounceCapacity = Program.BounceCapacity,
            LimboCapacity = Program.LimboCapacity,
            SliceBudget = Program.SliceBudget,
            CodeWords = Program.CodeWords,
            Pc = Pc,
            LastValue = LastValue,
            Time = Time,
            Name = Name,
            Fault = Fault,
            Critical = Critical,
            Yield = Yield,
            CallTop = _callTop,
            GuestTop = _guestTop,
            WaitUntil = _waitUntil,
            AnimationUntil = _animationUntil,
            TriggerSlot = _triggerSlot,
            LoopSlot = _loopSlot,
            LoopVariant = _loopVariant,
            Timer = _timer,
            BounceBase = _bounceBase,
            Bouncing = _bouncing,
            BumpRate = _bumpRate,
            SparkFrom = _sparkFrom,
            SparkTo = _sparkTo,
            FloatA = _floatA,
            FloatB = _floatB,
            FloatFor = _floatFor,
            FloatFrom = _floatFrom,
            Turbo = _turbo,
            BounceNode = _bounceNode,
            LimboUsed = _limboUsed,
            TimedWalk = _timedWalk,
            AttemptedWalk = _attemptedWalk,
            Variables = (int[])_variables.Clone(), Stack = (int[])_stack.Clone(),
            Heads = _heads == null ? null : (int[])_heads.Clone(),
            Walks = _walks.Select(x => new WalkState { Guest = x.Guest, A = x.A, B = x.B, C = x.C, D = x.D, Kind = x.Kind, Extra = x.Extra, Angle = x.Angle, State = x.State, Start = x.Start, End = x.End }).ToArray(),
            Bounce = _bounce.Select(x => new BouncerState { Guest = x.Guest, Node = x.Node, End = x.End, Start = x.Start }).ToArray(),
            Limbo = _limbo.Select(x => new LimboSlotState { Guest = x.Guest, Due = x.Due }).ToArray(),
            ParentId = Id(Parent), ChildId = Id(Child), SoundChildId = Id(SoundChild),
            RandomKind = _ownedRandom == null ? RandomStateKind.ExternalCallerManaged : RandomStateKind.OwnedSnapshotRandom,
            Random = _ownedRandom?.CaptureState(),
        };
    }

    /// <summary>First allocate ALL machines with the allocation-only constructors, then hydrate
    /// each by resolving IDs from that table. Does not tick, spawn, initialize scripts, read host
    /// properties or replay effects. Host/directory state (including head capacity) belongs to
    /// the enclosing save. externalRandomRestored acknowledges separately restoring the shared
    /// RNG; it does not serialize a delegate.
    ///
    /// All local data is validated/copied before resolver callbacks or mutation. Resolution
    /// completes before commit; missing/throwing resolvers leave this VM unchanged. Callbacks
    /// must be pure lookups, never mutate or tick. For graph-wide atomicity hydrate a staging
    /// graph before publishing it. Not thread-safe.</summary>
    public void RestoreState(State state, string expectedProgramKey, Func<string, RseMachine> resolve,
                             bool externalRandomRestored = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        void Require(bool condition, string message)
        {
            if (!condition) throw new ArgumentException(message, nameof(state));
        }
        Require(state.Version == StateVersion, "Unsupported RSE state version.");
        Require(!string.IsNullOrWhiteSpace(expectedProgramKey) &&
            string.Equals(state.ProgramKey, expectedProgramKey, StringComparison.Ordinal), "RSE program key mismatch.");
        Require(state.VariableCount == Program.VariableCount, "RSE VariableCount declaration mismatch.");
        Require(state.StackSize == Program.StackSize, "RSE StackSize declaration mismatch.");
        Require(state.WalkCapacity == Program.WalkCapacity, "RSE WalkCapacity declaration mismatch.");
        Require(state.BounceCapacity == Program.BounceCapacity, "RSE BounceCapacity declaration mismatch.");
        Require(state.LimboCapacity == Program.LimboCapacity, "RSE LimboCapacity declaration mismatch.");
        Require(state.SliceBudget == Program.SliceBudget, "RSE SliceBudget declaration mismatch.");
        Require(state.CodeWords == Program.CodeWords, "RSE CodeWords declaration mismatch.");
        Require(state.Variables != null && state.Variables.Length == _variables.Length, "Invalid variable array.");
        Require(state.Stack != null && state.Stack.Length == _stack.Length, "Invalid full stack array.");
        Require(state.Walks != null && state.Walks.Length == _walks.Length, "Invalid walk array.");
        Require(state.Bounce != null && state.Bounce.Length == _bounce.Length, "Invalid bounce array.");
        Require(state.Limbo != null && state.Limbo.Length == _limbo.Length, "Invalid limbo array.");
        Require(state.GuestTop >= 0 && state.GuestTop <= state.CallTop && state.CallTop <= _stack.Length,
            "Overlapping or out-of-range stack tops.");
        Require(state.Name != null && Enum.IsDefined(state.Yield), "Invalid name/yield.");
        Require(state.Time >= 0 && state.Time <= uint.MaxValue, "Invalid VM time.");
        // End-of-image can be reached at a slice boundary or fault: preserve it, so the next
        // visit faults exactly as an uninterrupted VM would. Unused stack cells are opaque.
        bool Address(int pc) => pc == Program.CodeWords || Program.Instructions.Any(i => i.Address == pc);
        Require(Address(state.Pc), "Invalid PC.");
        var variables = (int[])state.Variables.Clone();
        var stack = (int[])state.Stack.Clone();
        for (int i = state.CallTop; i < stack.Length; i++)
            Require(Address(stack[i]), "Invalid call return address.");
        var heads = state.Heads == null ? null : (int[])state.Heads.Clone();
        // Do not infer counts from guest IDs: LIMBO/BOUNCE accept guest zero, counters can
        // wrap, and inactive slots intentionally retain stale data. Signed effect parameters
        // and deadlines are script arithmetic, not wall-clock dates; do not normalize them.
        var walks = state.Walks.Select(x => new Walk { Guest = x.Guest, A = x.A, B = x.B, C = x.C, D = x.D, Kind = x.Kind, Extra = x.Extra, Angle = x.Angle, State = x.State, Start = x.Start, End = x.End }).ToArray();
        var bounce = state.Bounce.Select(x => new Bouncer { Guest = x.Guest, Node = x.Node, End = x.End, Start = x.Start }).ToArray();
        var limbo = state.Limbo.Select(x => new LimboSlot { Guest = x.Guest, Due = x.Due }).ToArray();
        foreach (var w in walks)
            Require(w.State >= 0 && w.State <= 4 && w.Angle >= 0 && w.Angle <= 4095, "Invalid walk state/angle.");
        foreach (var id in new[] { state.ParentId, state.ChildId, state.SoundChildId })
            Require(id == null || !string.IsNullOrWhiteSpace(id), "Empty machine link ID.");
        Require(Enum.IsDefined(state.RandomKind), "Invalid random ownership marker.");
        SnapshotRandom random = null;
        if (state.RandomKind == RandomStateKind.OwnedSnapshotRandom)
        {
            Require(_ownedRandom != null && state.Random != null, "Owned random state requires an owned RNG destination.");
            random = SnapshotRandom.FromState(state.Random);
        }
        else
        {
            Require(_ownedRandom == null && state.Random == null, "External RNG marker cannot contain an owned stream.");
            if (!externalRandomRestored)
                throw new NotSupportedException("External RNG state is not in this snapshot. Restore the caller-owned shared stream and explicitly acknowledge it.");
        }
        var links = new Dictionary<string, RseMachine>(StringComparer.Ordinal);
        bool hasLinks = state.ParentId != null || state.ChildId != null || state.SoundChildId != null;
        if (hasLinks && resolve == null) throw new ArgumentNullException(nameof(resolve));
        RseMachine Link(string id)
        {
            if (id == null) return null;
            if (links.TryGetValue(id, out var existing)) return existing;
            var machine = resolve(id);
            Require(machine != null, $"Unresolved machine ID: {id}");
            Require(!links.ContainsValue(machine), "Distinct IDs resolved to the same machine.");
            links.Add(id, machine);
            return machine;
        }
        var parent = Link(state.ParentId);
        var child = Link(state.ChildId);
        var soundChild = Link(state.SoundChildId);

        // Commit: no callbacks, allocations, or validation below this line.
        Array.Copy(variables, _variables, variables.Length);
        Array.Copy(stack, _stack, stack.Length);
        Array.Copy(walks, _walks, walks.Length);
        Array.Copy(bounce, _bounce, bounce.Length);
        Array.Copy(limbo, _limbo, limbo.Length);
        _heads = heads;
        _ownedRandom = random;
        Parent = parent; Child = child; SoundChild = soundChild;
        Pc = state.Pc;
        LastValue = state.LastValue;
        Time = state.Time;
        Name = state.Name;
        Fault = state.Fault;
        Critical = state.Critical;
        Yield = state.Yield;
        _callTop = state.CallTop;
        _guestTop = state.GuestTop;
        _waitUntil = state.WaitUntil;
        _animationUntil = state.AnimationUntil;
        _triggerSlot = state.TriggerSlot;
        _loopSlot = state.LoopSlot;
        _loopVariant = state.LoopVariant;
        _timer = state.Timer;
        _bounceBase = state.BounceBase;
        _bouncing = state.Bouncing;
        _bumpRate = state.BumpRate;
        _sparkFrom = state.SparkFrom;
        _sparkTo = state.SparkTo;
        _floatA = state.FloatA;
        _floatB = state.FloatB;
        _floatFor = state.FloatFor;
        _floatFrom = state.FloatFrom;
        _turbo = state.Turbo;
        _bounceNode = state.BounceNode;
        _limboUsed = state.LimboUsed;
        _timedWalk = state.TimedWalk;
        _attemptedWalk = state.AttemptedWalk;
    }
}
