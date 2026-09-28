using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public sealed partial class ParkRide
{
    public const int StateVersion = 1;
    internal long SnapshotValueCount => (long)_queue.Count + _left.Count + _ejected.Count + (Variables?.Count ?? 0);

    /// <summary>Ride-owned state only. Required nullable fields distinguish absent from null.
    /// Referenced assets/sims/staff are saved separately; IDs are coordinator-owned.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class State
    {
        public required int Version { get; init; }
        public required string DefinitionKey { get; init; }
        public required string MachineId { get; init; }
        public required string HostId { get; init; }
        public required string TrackId { get; init; }
        public required string CoasterId { get; init; }
        public required string AssignedMechanicId { get; init; }
        public required int Id { get; init; }
        public required string Name { get; init; }
        public required CellState Origin { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required CellState Entrance { get; init; }
        public required CellState Exit { get; init; }
        public required CellState ServiceEntry { get; init; }
        public required int? PlacementTurns { get; init; }
        public required bool ReliefUsesOccupancy { get; init; }
        public required byte DestinationState { get; init; }
        public required bool CoasterTrackClosed { get; init; }
        public required int Condition { get; init; }
        public required int Quality { get; init; }
        public required int Setting0xAC { get; init; }
        public required int? SalePrice { get; init; }
        public required int Setting0xC0 { get; init; }
        public required int ScreamHandle { get; init; }
        public required int ScreamLevel { get; init; }
        public required int? Speed { get; init; }
        public required int? Duration { get; init; }
        public required ValueState ValueState { get; init; }
        public required int CurrentTier { get; init; }
        public required int? Capacity { get; init; }
        public required byte CachedTrackWeight { get; init; }
        public required int? SideshowPrizeValue { get; init; }
        public required ushort? SideshowPrice { get; init; }
        public required ushort? SideshowWinPercentage { get; init; }
        public required int Takings { get; init; }
        public required int Profit { get; init; }
        public required int Customers { get; init; }
        public required int LastCleanedDay { get; init; }
        public required int Reliability { get; init; }
        public required int? Life { get; init; }
        public required bool ServiceFlag { get; init; }
        public required bool UpgradePending { get; init; }
        public required int NativeRiders { get; init; }
        public required string[] Variables { get; init; }
        public required int[] Queue { get; init; }
        public required int[] Left { get; init; }
        public required int[] Ejected { get; init; }
    }

    // Explicit DTOs enforce required coordinates/value components even with default JSON options.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class CellState
    {
        public required int X { get; init; }
        public required int Z { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class ValueState
    {
        public required int Speed { get; init; }
        public required int Duration { get; init; }
        public required byte CachedTrackWeight { get; init; }
        public required int SideshowPrizeValue { get; init; }
        public required ushort SideshowPrice { get; init; }
        public required ushort SideshowWinPercentage { get; init; }
    }

    static CellState SaveCell(ParkCell? c) => c is {} v ? new CellState { X = v.X, Z = v.Z } : null;
    static ParkCell? LoadCell(CellState c) => c == null ? null : new ParkCell(c.X, c.Z);
    static void Reference(string id, object target, string name)
    {
        if ((id == null) != (target == null) || (id != null && string.IsNullOrWhiteSpace(id)))
            throw new ArgumentException($"ParkRide {name}: missing/unexpected reference or empty ID.");
    }

    /// <summary>Capture at a tick boundary. Never reads lazy getters. Null references need null
    /// IDs; nonnull references need nonblank IDs. Coordinator verifies key/reference identity.</summary>
    public State CaptureState(string definitionKey, string machineId, string hostId, string trackId,
        string coasterId, Func<StaffMember, string> identifyStaff)
    {
        Reference(definitionKey, Definition, nameof(Definition)); Reference(machineId, Machine, nameof(Machine));
        Reference(hostId, Host, nameof(Host)); Reference(trackId, Track, nameof(Track));
        Reference(coasterId, Coaster, nameof(Coaster));
        string staffId = AssignedMechanic == null ? null :
            (identifyStaff ?? throw new ArgumentNullException(nameof(identifyStaff)))(AssignedMechanic);
        Reference(staffId, AssignedMechanic, nameof(AssignedMechanic));
        var state = new State
        {
            Version = StateVersion, DefinitionKey = definitionKey, MachineId = machineId, HostId = hostId,
            TrackId = trackId, CoasterId = coasterId, AssignedMechanicId = staffId,
            Id = Id, Name = Name, Origin = SaveCell(Origin), Width = Width, Height = Height,
            Entrance = SaveCell(Entrance), Exit = SaveCell(Exit), ServiceEntry = SaveCell(ServiceEntry),
            PlacementTurns = PlacementTurns, ReliefUsesOccupancy = ReliefUsesOccupancy,
            DestinationState = DestinationState, CoasterTrackClosed = CoasterTrackClosed,
            Condition = Condition, Quality = Quality, Setting0xAC = Setting0xAC, SalePrice = _salePrice,
            Setting0xC0 = Setting0xC0, ScreamHandle = ScreamHandle, ScreamLevel = ScreamLevel,
            Speed = _speed, Duration = _duration, Capacity = _capacity, CurrentTier = CurrentTier,
            CachedTrackWeight = CachedTrackWeight, SideshowPrizeValue = _prize,
            SideshowPrice = _price, SideshowWinPercentage = _win,
            ValueState = _valueState is {} v ? new ValueState { Speed = v.Speed, Duration = v.Duration,
                CachedTrackWeight = v.CachedTrackWeight, SideshowPrizeValue = v.SideshowPrizeValue,
                SideshowPrice = v.SideshowPrice, SideshowWinPercentage = v.SideshowWinPercentage } : null,
            Takings = Takings, Profit = Profit, Customers = Customers, LastCleanedDay = LastCleanedDay,
            Reliability = Reliability, Life = _life, ServiceFlag = ServiceFlag,
            UpgradePending = UpgradePending, NativeRiders = NativeRiders,
            Variables = Variables?.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            Queue = _queue.ToArray(), Left = _left.ToArray(), Ejected = _ejected.ToArray()
        };
        Validate(state);
        return state;
    }

    static void Validate(State s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.Version != StateVersion) throw new ArgumentException("Unsupported ParkRide state version.");
        if (s.Name == null || s.Origin == null || s.Width <= 0 || s.Height <= 0
            || (long)s.Origin.X + s.Width - 1 > int.MaxValue
            || (long)s.Origin.Z + s.Height - 1 > int.MaxValue
            || s.PlacementTurns is < 0 or > 3 || s.CurrentTier is < 0 or > 2
            || s.Condition is < 0 or > 100 || s.Life is < short.MinValue or > short.MaxValue)
            throw new ArgumentException("Invalid ParkRide dimensions, placement or scalar state.");
        if (s.Queue == null || s.Left == null || s.Ejected == null || s.Variables == null
            || s.Variables.Any(string.IsNullOrWhiteSpace)
            || s.Variables.Distinct(StringComparer.Ordinal).Count() != s.Variables.Length)
            throw new ArgumentException("Invalid ParkRide collections.");
        // Do not clamp public settings, guest IDs or counters: signed/wrapped values are state.
    }

    /// <summary>Build a staged ride without gameplay calls or callbacks (except staff resolution).
    /// The coordinator must validate asset keys and resolved reference identity, and later wire
    /// graph ownership/back-links. This does not restore referenced objects or publish the ride.</summary>
    public static ParkRide FromState(State state, RideDefinition definition, RseMachine machine,
        RsePreviewHost host, TrackRideSim track, CoasterSim coaster, Func<string, StaffMember> resolveStaff)
    {
        Validate(state);
        Reference(state.DefinitionKey, definition, nameof(definition));
        Reference(state.MachineId, machine, nameof(machine)); Reference(state.HostId, host, nameof(host));
        Reference(state.TrackId, track, nameof(track)); Reference(state.CoasterId, coaster, nameof(coaster));
        if (state.AssignedMechanicId != null && string.IsNullOrWhiteSpace(state.AssignedMechanicId))
            throw new ArgumentException("Empty assigned mechanic ID.");
        var staff = state.AssignedMechanicId == null ? null :
            (resolveStaff ?? throw new ArgumentNullException(nameof(resolveStaff)))(state.AssignedMechanicId);
        Reference(state.AssignedMechanicId, staff, nameof(AssignedMechanic));
        var r = new ParkRide
        {
            Id = state.Id, Name = state.Name, Origin = LoadCell(state.Origin).Value,
            Width = state.Width, Height = state.Height, Entrance = LoadCell(state.Entrance),
            Exit = LoadCell(state.Exit), ServiceEntry = LoadCell(state.ServiceEntry),
            Definition = definition, Machine = machine, Host = host, Track = track, Coaster = coaster,
            PlacementTurns = state.PlacementTurns, ReliefUsesOccupancy = state.ReliefUsesOccupancy,
            DestinationState = state.DestinationState, CoasterTrackClosed = state.CoasterTrackClosed,
            Condition = state.Condition, Quality = state.Quality, Setting0xAC = state.Setting0xAC,
            _salePrice = state.SalePrice, Setting0xC0 = state.Setting0xC0,
            ScreamHandle = state.ScreamHandle, ScreamLevel = state.ScreamLevel,
            _speed = state.Speed, _duration = state.Duration, _capacity = state.Capacity,
            CurrentTier = state.CurrentTier, CachedTrackWeight = state.CachedTrackWeight,
            _prize = state.SideshowPrizeValue, _price = state.SideshowPrice, _win = state.SideshowWinPercentage,
            _valueState = state.ValueState is {} v ? new NativeRideValue.State(v.Speed, v.Duration,
                v.CachedTrackWeight, v.SideshowPrizeValue, v.SideshowPrice, v.SideshowWinPercentage) : null,
            Takings = state.Takings, Profit = state.Profit, Customers = state.Customers,
            LastCleanedDay = state.LastCleanedDay, Reliability = state.Reliability, _life = state.Life,
            ServiceFlag = state.ServiceFlag, AssignedMechanic = staff, UpgradePending = state.UpgradePending,
            NativeRiders = state.NativeRiders, Variables = new HashSet<string>(state.Variables, StringComparer.Ordinal)
        };
        foreach (int guest in state.Queue) r._queue.Enqueue(guest);
        r._left.AddRange(state.Left); r._ejected.AddRange(state.Ejected);
        return r;
    }
}
