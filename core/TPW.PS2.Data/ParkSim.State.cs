using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;

namespace TPW.PS2.Data;

public sealed partial class ParkSim
{
    public const int ScriptedStateVersion = 1;
    public const int ScriptedGraphLimit = 4096, ScriptedValueLimit = 1_000_000;

    /// <summary>Trusted, preloaded immutable asset identity, NOT the script's runtime NAME.
    /// SiblingScope identifies the source folder/revision; null disables future spawning.</summary>
    public sealed record ScriptAsset(string Key, RseProgram Program, string SiblingScope);

    /// <summary>Pure lookup/binding callbacks only: no IO, gameplay, ticking or effect replay.
    /// Keys must identify exact asset revisions. IdentifyProgram must return the supplied program.
    /// ResolveChild takes source asset key and SPAWN operand (not runtime NAME), and must stay
    /// within its source SiblingScope. Null means absent. Equal keys resolve to one shared program;
    /// VM instances always have independent identity. Unknown keys must throw; null animation
    /// is only a valid explicit headless binding.
    /// Paths and staff belong to the enclosing owner snapshot; supply fresh owners on restore.</summary>
    public sealed class ScriptedBindings
    {
        public Func<RseProgram, ScriptAsset> IdentifyProgram { get; init; }
        public Func<string, ScriptAsset> ResolveProgram { get; init; }
        public Func<string, string, ScriptAsset> ResolveChild { get; init; }
        public Func<RsePreviewHost, string> IdentifyAnimation { get; init; }
        public Func<string, Animation> ResolveAnimation { get; init; }
        public Func<RideDefinition, string> IdentifyDefinition { get; init; }
        public Func<string, RideDefinition> ResolveDefinition { get; init; }
        // Called only after the staged graph validates. Install subscriptions/NodeSource, never emit.
        public Action<ParkSim, IReadOnlyDictionary<string, RsePreviewHost>> BindCallbacks { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class ScriptedState
    {
        public required int Version { get; init; }
        public required long Time { get; init; }
        public required long Carry { get; init; }
        public required ParkRide.State[] Rides { get; init; }
        public required MachineState[] Machines { get; init; }
        public required HostState[] Hosts { get; init; }
        public required HandleState[] Handles { get; init; }
        public required int HandleAllocator { get; init; }
        public required int[] Upgrades { get; init; }
        public required short[] AdvisorCounters { get; init; }
        public required long WearEvents { get; init; }
        public required int UpgradesPastLastTier { get; init; }
        public required ParkFinances.State Finances { get; init; }
        public required SnapshotRandom.State Random { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class MachineState
    {
        public required string Id { get; init; }
        public required string HostId { get; init; }
        public required string SiblingScope { get; init; }
        public required RseMachine.State Vm { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class HostState
    {
        public required string Id { get; init; }
        public required RsePreviewHost.State Host { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class HandleState
    {
        public required int Handle { get; init; }
        // Null is a tombstone. The allocator and tombstones survive removal and repeated saves.
        public required string MachineId { get; init; }
    }

    readonly ConditionalWeakTable<RseMachine, ScriptAsset> _scriptedAssets = new();
    readonly ConditionalWeakTable<RsePreviewHost, string> _scriptedAnimations = new();
    readonly ConditionalWeakTable<RideDefinition, string> _scriptedDefinitions = new();
    readonly HashSet<int> _scriptedDeadHandles = new();
    static void StateRequire(bool ok, string message)
    {
        if (!ok) throw new ArgumentException("Invalid scripted park state: " + message);
    }
    static string StateKey(string key)
    {
        StateRequire(!string.IsNullOrWhiteSpace(key) && key.Length <= 1024, "missing/oversized key");
        return key;
    }
    static ScriptAsset CheckAsset(ScriptAsset a, string key = null)
    {
        StateRequire(a != null && a.Program != null, "missing program binding");
        StateKey(a.Key); if (a.SiblingScope != null) StateKey(a.SiblingScope);
        StateRequire(key == null || key == a.Key, "program asset identity mismatch");
        return a;
    }

    /// <summary>At a scheduler boundary. Includes the complete parent/child/sound closure, not
    /// the scheduler's depth-limited Chain. Unsupported native track/coaster owners and external
    /// VM RNGs fail closed. Does not read lazy ride getters or change source state.</summary>
    public ScriptedState CaptureScriptedState(ScriptedBindings bindings, Func<StaffMember, string> identifyStaff = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        // Preflight live counts BEFORE copying queues/VM tables or consulting asset resolvers.
        long budget = ScriptedValueLimit;
        void Bound(long count, long limit = ScriptedValueLimit)
        { StateRequire(count >= 0 && count <= limit && (budget -= count) >= 0, "capture collection bound"); }
        Bound(_rides.Count, ScriptedGraphLimit); Bound(_byHandle.Count + (long)_scriptedDeadHandles.Count);
        Bound(_upgrades.Count, UpgradeListCapacity); Bound(_advisorCounters.Length); Bound(Finances.IncomeByCategory.Count);
        foreach (var ride in _rides) Bound(ride.SnapshotValueCount);
        var machines = new Dictionary<RseMachine, string>();
        var hosts = new Dictionary<RsePreviewHost, string>();
        var pending = new Queue<RseMachine>();
        void Add(RseMachine m)
        {
            if (m == null || machines.ContainsKey(m)) return;
            StateRequire(machines.Count < ScriptedGraphLimit, "machine graph bound");
            Bound(1); Bound(m.SnapshotValueCount);
            machines.Add(m, "m" + machines.Count); pending.Enqueue(m);
        }
        string Host(IRseHost host)
        {
            if (host == null) return null;
            if (host is not RsePreviewHost h) throw new NotSupportedException("Unknown RSE host type.");
            if (!hosts.ContainsKey(h))
            { StateRequire(hosts.Count < ScriptedGraphLimit, "host graph bound"); Bound(1); Bound(h.SnapshotValueCount); hosts.Add(h, "h" + hosts.Count); }
            return hosts[h];
        }
        foreach (var r in _rides)
        {
            if (r.Track != null || r.Coaster != null) throw new NotSupportedException("Track/Coaster owners are not supported by scripted saves.");
            Add(r.Machine); Host(r.Host);
        }
        while (pending.TryDequeue(out var m)) { Host(m.SnapshotHost); Add(m.Parent); Add(m.Child); Add(m.SoundChild); }
        var staff = new Dictionary<StaffMember, string>();
        string Staff(StaffMember member)
        {
            if (staff.TryGetValue(member, out var id)) return id;
            id = StateKey(identifyStaff?.Invoke(member));
            StateRequire(!staff.ContainsValue(id), "duplicate staff identity"); staff.Add(member, id); return id;
        }
        var result = new ScriptedState
        {
            Version = ScriptedStateVersion, Time = Time, Carry = _carry,
            Rides = _rides.Select(r => r.CaptureState(r.Definition == null ? null :
                (_scriptedDefinitions.TryGetValue(r.Definition, out var key) ? key : StateKey(bindings.IdentifyDefinition?.Invoke(r.Definition))),
                r.Machine == null ? null : machines[r.Machine], Host(r.Host), null, null, Staff)).ToArray(),
            Machines = machines.Select(pair =>
            {
                var a = CheckAsset(_scriptedAssets.TryGetValue(pair.Key, out var known) ? known : bindings.IdentifyProgram?.Invoke(pair.Key.Program));
                StateRequire(ReferenceEquals(a.Program, pair.Key.Program), "capture program binding mismatch");
                var vm = pair.Key.CaptureState(a.Key, m => machines[m]);
                if (vm.RandomKind != RseMachine.RandomStateKind.OwnedSnapshotRandom)
                    throw new NotSupportedException("External VM random providers are not supported.");
                return new MachineState { Id = pair.Value, HostId = Host(pair.Key.SnapshotHost), SiblingScope = a.SiblingScope, Vm = vm };
            }).ToArray(),
            Hosts = hosts.Select(pair => new HostState { Id = pair.Value, Host = pair.Key.CaptureState(
                _scriptedAnimations.TryGetValue(pair.Key, out var key) ? key : StateKey(bindings.IdentifyAnimation?.Invoke(pair.Key))) }).ToArray(),
            Handles = _byHandle.Select(p => new HandleState { Handle = p.Key,
                MachineId = machines.GetValueOrDefault(p.Value) }).Concat(_scriptedDeadHandles.Select(h =>
                new HandleState { Handle = h, MachineId = null })).OrderBy(h => h.Handle).ToArray(),
            HandleAllocator = _handle, Upgrades = _upgrades.Select(r =>
            { StateRequire(_rides.Contains(r), "upgrade outside ride graph"); return r.Id; }).ToArray(),
            AdvisorCounters = (short[])_advisorCounters.Clone(), WearEvents = WearEvents,
            UpgradesPastLastTier = UpgradesPastLastTier, Finances = Finances.CaptureState(), Random = _random.CaptureState()
        };
        // Validate using the same staged path, without installing callbacks or changing the source.
        BuildScriptedState(result, Paths, bindings, id => staff.Single(p => p.Value == id).Key, false);
        return result;
    }

    /// <summary>Allocation-only staged restore. Publishes only a fully validated new park; no Add,
    /// Create, RunSlice, service, booking, or state replay. Trusted binders must be effect-free.
    /// Paths/staff are external owner bindings, not captured here. Assets are never serialized.</summary>
    public static ParkSim FromScriptedState(ScriptedState state, ParkPaths paths, ScriptedBindings bindings,
        Func<string, StaffMember> resolveStaff = null) => BuildScriptedState(state, paths, bindings, resolveStaff, true);

    static ParkSim BuildScriptedState(ScriptedState s, ParkPaths paths, ScriptedBindings b,
        Func<string, StaffMember> resolveStaff, bool bind)
    {
        ArgumentNullException.ThrowIfNull(s); ArgumentNullException.ThrowIfNull(b);
        StateRequire(s.Version == ScriptedStateVersion && s.Time >= 0 && s.Time % TickMilliseconds == 0
            && s.Carry >= 0 && s.Carry <= TickMilliseconds * 8 && s.HandleAllocator >= 0
            && s.HandleAllocator < int.MaxValue && s.WearEvents >= 0 && s.UpgradesPastLastTier >= 0, "version/clock/allocator/counters");
        int budget = ScriptedValueLimit;
        void Bound(int count, int limit = ScriptedValueLimit)
        { StateRequire(count >= 0 && count <= limit && (budget -= count) >= 0, "collection bound"); }
        void ArrayBound<T>(T[] a, int limit = ScriptedValueLimit) => Bound(a?.Length ?? -1, limit);
        ArrayBound(s.Rides, ScriptedGraphLimit); ArrayBound(s.Machines, ScriptedGraphLimit);
        ArrayBound(s.Hosts, ScriptedGraphLimit); ArrayBound(s.Handles); ArrayBound(s.Upgrades, UpgradeListCapacity);
        ArrayBound(s.AdvisorCounters, 22);
        StateRequire(s.AdvisorCounters.Length == 22 && s.AdvisorCounters.All(n => n is >= -30000 and <= 30000), "advisor counters");
        var park = new ParkSim(paths) { Time = s.Time, _carry = s.Carry,
            WearEvents = s.WearEvents, UpgradesPastLastTier = s.UpgradesPastLastTier };
        StateRequire(s.Finances != null, "finance"); Bound(s.Finances.IncomeByCategory?.Count ?? -1);
        park.Finances.RestoreState(s.Finances); park._random.RestoreState(s.Random);
        s.AdvisorCounters.CopyTo(park._advisorCounters, 0);
        var hosts = new Dictionary<string, RsePreviewHost>(StringComparer.Ordinal);
        foreach (var h in s.Hosts)
        {
            StateRequire(h?.Host != null, "host record"); StateKey(h.Id); StateKey(h.Host.AssetKey);
            Bound(h.Host.Channels?.Count ?? -1); Bound(h.Host.Seats?.Count ?? -1);
            ArrayBound(h.Host.Walkers); ArrayBound(h.Host.Visibility); Bound(h.Host.HeadSlots);
            if (h.Host.LastEffect != null) ArrayBound(h.Host.LastEffect.Arguments);
            StateRequire(b.ResolveAnimation != null, "animation registry missing");
            var host = new RsePreviewHost(b.ResolveAnimation(h.Host.AssetKey));
            StateRequire(hosts.TryAdd(h.Id, host), "duplicate host ID");
            host.RestoreState(h.Host, h.Host.AssetKey); park._scriptedAnimations.Add(host, h.Host.AssetKey);
        }
        T Lookup<T>(Dictionary<string, T> table, string id) where T : class
        { StateKey(id); StateRequire(table.TryGetValue(id, out var value), "dangling reference: " + id); return value; }
        var assets = new Dictionary<string, ScriptAsset>(StringComparer.Ordinal);
        ScriptAsset Asset(string key)
        {
            StateKey(key);
            if (!assets.TryGetValue(key, out var a))
            {
                a = CheckAsset(b.ResolveProgram?.Invoke(key), key);
                StateRequire(!assets.Values.Any(v => ReferenceEquals(v.Program, a.Program)), "distinct asset keys alias one program");
                assets.Add(key, a);
            }
            return a;
        }
        RseMachine Allocate(ScriptAsset a, RsePreviewHost host)
        {
            RseMachine Spawn(string name)
            {
                var child = b.ResolveChild?.Invoke(a.Key, name);
                if (child == null) return null;
                CheckAsset(child); var canonical = Asset(child.Key);
                StateRequire(ReferenceEquals(child.Program, canonical.Program) && child.SiblingScope == canonical.SiblingScope
                    && child.SiblingScope == a.SiblingScope, "sibling asset/scope mismatch");
                return Allocate(canonical, host);
            }
            var machine = new RseMachine(a.Program, host, spawn: a.SiblingScope == null ? null : Spawn, directory: park);
            park._scriptedAssets.Add(machine, a); return machine;
        }
        var machines = new Dictionary<string, RseMachine>(StringComparer.Ordinal);
        foreach (var m in s.Machines)
        {
            StateRequire(m?.Vm != null, "machine record"); StateKey(m.Id);
            var v = m.Vm; ArrayBound(v.Variables); ArrayBound(v.Stack); ArrayBound(v.Walks);
            ArrayBound(v.Bounce); ArrayBound(v.Limbo); if (v.Heads != null) ArrayBound(v.Heads);
            StateRequire(v.RandomKind == RseMachine.RandomStateKind.OwnedSnapshotRandom, "external VM RNG");
            var a = Asset(v.ProgramKey);
            StateRequire(a.SiblingScope == m.SiblingScope, "source sibling scope mismatch");
            StateRequire(a.SiblingScope == null || b.ResolveChild != null, "missing sibling resolver");
            // Each VM owns a lazy head table even when many VMs share the same host.
            // Reserve prospective allocation without materializing that observable lazy field.
            if (v.Heads == null && m.HostId != null) Bound(Lookup(hosts, m.HostId).HeadSlots);
            // Bound trusted declarations too, before the VM constructor allocates its tables.
            Bound(a.Program.VariableCount); Bound(a.Program.StackSize);
            Bound(checked(Math.Max(0, a.Program.WalkCapacity) * 2));
            Bound(Math.Max(0, a.Program.BounceCapacity)); Bound(Math.Max(0, a.Program.LimboCapacity));
            StateRequire(machines.TryAdd(m.Id, Allocate(a, m.HostId == null ? null : Lookup(hosts, m.HostId))), "duplicate machine ID");
        }
        foreach (var m in s.Machines) machines[m.Id].RestoreState(m.Vm, m.Vm.ProgramKey, id => Lookup(machines, id));
        var definitions = new Dictionary<string, RideDefinition>(StringComparer.Ordinal);
        var staff = new Dictionary<string, StaffMember>(StringComparer.Ordinal);
        StaffMember Staff(string id)
        {
            StateKey(id);
            if (!staff.TryGetValue(id, out var member))
            {
                member = resolveStaff?.Invoke(id);
                StateRequire(member != null && !staff.ContainsValue(member), "missing/aliased staff binding"); staff.Add(id, member);
            }
            return member;
        }
        var rides = new Dictionary<int, ParkRide>();
        foreach (var r in s.Rides)
        {
            StateRequire(r != null, "ride record");
            if (r.TrackId != null || r.CoasterId != null) throw new NotSupportedException("Track/Coaster owners are not supported by scripted saves.");
            ArrayBound(r.Queue); ArrayBound(r.Left); ArrayBound(r.Ejected); ArrayBound(r.Variables);
            var machine = Lookup(machines, r.MachineId); var host = Lookup(hosts, r.HostId);
            StateRequire(ReferenceEquals(machine.SnapshotHost, host), "ride/VM host mismatch");
            foreach (var name in r.Variables) { StateKey(name); machine.Program.VariableIndex(name); }
            RideDefinition definition = null;
            if (r.DefinitionKey != null)
            {
                StateKey(r.DefinitionKey);
                if (!definitions.TryGetValue(r.DefinitionKey, out definition))
                {
                    definition = b.ResolveDefinition?.Invoke(r.DefinitionKey);
                    StateRequire(definition != null && !definitions.ContainsValue(definition), "missing/aliased definition");
                    definitions.Add(r.DefinitionKey, definition); park._scriptedDefinitions.Add(definition, r.DefinitionKey);
                }
            }
            var ride = ParkRide.FromState(r, definition, machine, host, null, null, Staff);
            StateRequire(rides.TryAdd(r.Id, ride), "duplicate placement ID"); park._rides.Add(ride);
        }
        // Reject injected orphan nodes, but allow arbitrary cycles and links back through parents.
        var reachable = new HashSet<RseMachine>(); var todo = new Stack<RseMachine>(park._rides.Select(r => r.Machine));
        while (todo.TryPop(out var m))
            if (m != null && reachable.Add(m)) { todo.Push(m.Parent); todo.Push(m.Child); todo.Push(m.SoundChild); }
        StateRequire(reachable.Count == machines.Count && hosts.Values.All(h => reachable.Any(m => ReferenceEquals(m.SnapshotHost, h))), "unreachable graph record");
        var handles = new HashSet<int>();
        foreach (var h in s.Handles)
        {
            StateRequire(h != null && h.Handle > 0 && h.Handle <= s.HandleAllocator && handles.Add(h.Handle), "duplicate/invalid handle");
            if (h.MachineId == null) park._scriptedDeadHandles.Add(h.Handle);
            else
            {
                var m = Lookup(machines, h.MachineId);
                StateRequire(park._handles.TryAdd(m, h.Handle), "multiple handles for one VM"); park._byHandle.Add(h.Handle, m);
            }
        }
        park._handle = s.HandleAllocator;
        var upgrades = new HashSet<int>();
        foreach (int id in s.Upgrades)
        {
            StateRequire(upgrades.Add(id) && rides.ContainsKey(id), "duplicate/dangling upgrade"); park._upgrades.Add(rides[id]);
        }
        if (bind) b.BindCallbacks?.Invoke(park, hosts);
        return park;
    }
}
