using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public partial struct VisitorWants
{
    public const int StateVersion = 1;

    // These are ALL the fields of VisitorWants, not a selection of public UI readouts.
    // There are no pending audio, intent, mask or native guest byte arrays in this type.
    // GuestWalk/ParkVisitors own those parts of a guest, separately from this value.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required byte Happiness { get; init; }
        public required byte Sick { get; init; }
        public required byte Hunger { get; init; }
        public required byte Toilet { get; init; }
        public required byte Thirst { get; init; }
        public required byte Litter { get; init; }
        public required byte Unknown78 { get; init; }
        public required byte Boredom { get; init; }
        public required int Cash { get; init; }
        public required Thought Thought { get; init; }
        public required byte PreferredIntensity { get; init; }
    }

    public readonly State CaptureState() => new()
    {
        Version = StateVersion, Happiness = Happiness, Sick = Sick, Hunger = Hunger,
        Toilet = Toilet, Thirst = Thirst, Litter = Litter, Unknown78 = Unknown78,
        Boredom = Boredom, Cash = Cash, Thought = Thought, PreferredIntensity = PreferredIntensity
    };

    public static VisitorWants FromState(State state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Version != StateVersion || !Enum.IsDefined(state.Thought))
            throw new ArgumentException("Unsupported or invalid VisitorWants state.", nameof(state));
        // Do NOT Clamp: Set accepts raw bytes. A save is not another need setter/decision.
        // Cash is a signed word and PreferredIntensity is a value, not an index into Preferences.
        return new VisitorWants
        {
            Happiness = state.Happiness, Sick = state.Sick, Hunger = state.Hunger,
            Toilet = state.Toilet, Thirst = state.Thirst, Litter = state.Litter,
            Unknown78 = state.Unknown78, Boredom = state.Boredom, Cash = state.Cash,
            Thought = state.Thought, PreferredIntensity = state.PreferredIntensity
        };
    }
}

public sealed partial class VisitorNeeds
{
    public const int StateVersion = 1;
    public const int StateGuestLimit = 100_000, StateRateLimit = 4_096;

    /// <summary>Pure external identity bindings, NOT simulation calls. An ID identifies the
    /// entire delegate/subscription bundle. Resolve against the staged external owners; do not
    /// run the delegate here. No delegates, closures or guest objects are serialized. Guest keys
    /// are the actual integer IDs accepted by Set/Spawn, including zero and negative integers.
    /// Callers must freeze owners/config while capturing or restoring, bound incoming JSON,
    /// and publish a restored graph only after all its owners/bindings have succeeded.</summary>
    public sealed class StateBindings
    {
        public Func<Action<int, int>, string> IdentifySounded { get; init; }
        public Func<string, Action<int, int>> ResolveSounded { get; init; }
        public Func<Func<int, (int Plain, int Vomit)>, string> IdentifyNearbyLitter { get; init; }
        public Func<string, Func<int, (int Plain, int Vomit)>> ResolveNearbyLitter { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GuestState
    {
        public required int Id { get; init; }
        public required VisitorWants.State Wants { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RateState
    {
        public required byte Base { get; init; }
        public required byte Spread { get; init; }
        public required bool High { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RateEntryState
    {
        public required string Key { get; init; }
        // Null values and missing/custom keys are legal in the mutable public dictionary.
        // Preserve them rather than silently supplying defaults (including their failure modes).
        public required RateState Value { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required GuestState[] Guests { get; init; }
        public required IntMapLayout GuestLayout { get; init; }
        public required SnapshotRandom.State Random { get; init; }
        public required int[] HoldingBubble { get; init; }
        public required int BubbleBudget { get; init; }
        public required int Unknown78Bar { get; init; }
        public required int SickBar { get; init; }
        public required int ToiletBar { get; init; }
        public required int HungerBar { get; init; }
        public required int ThirstBar { get; init; }
        public required RateEntryState[] Rates { get; init; }
        public required double SecondsPerRise { get; init; }
        public required double SinceRise { get; init; }
        public required double SecondsPerTick { get; init; }
        public required double SinceTick { get; init; }
        public required long Tick { get; init; }
        public required int HungerTicks { get; init; }
        public required int ThirstTicks { get; init; }
        /// <summary>Fingerprint by exact bytes of GAME/config scope VisitorNeeds.Preferences.
        /// Validation only: restoring this owner never installs or modifies a global table.</summary>
        public required byte[] Preferences { get; init; }
        public required string SoundedId { get; init; }
        public required string NearbyLitterId { get; init; }
    }

    public State CaptureState(StateBindings bindings = null)
    {
        var layout=_byGuest.CaptureLayout();
        if(_byGuest.Count>StateGuestLimit||Rates.Count>StateRateLimit)
            throw new ArgumentException("Needs capture exceeds owner bounds.");
        var state = new State
        {
            Version = StateVersion,
            Guests = _byGuest.Select(p => new GuestState { Id = p.Key, Wants = p.Value.CaptureState() }).ToArray(),
            GuestLayout = layout,
            Random = _rng.CaptureState(),
            // Set iteration order has no consumers; canonical order makes comparisons unambiguous.
            HoldingBubble = _holdingBubble.OrderBy(id => id).ToArray(), BubbleBudget = BubbleBudget,
            Unknown78Bar = Unknown78Bar, SickBar = SickBar, ToiletBar = ToiletBar,
            HungerBar = HungerBar, ThirstBar = ThirstBar,
            Rates = Rates.Select(p => new RateEntryState { Key = p.Key,
                Value = p.Value == null ? null : new RateState
                    { Base = p.Value.Base, Spread = p.Value.Spread, High = p.Value.High } }).ToArray(),
            SecondsPerRise = SecondsPerRise, SinceRise = _sinceRise,
            SecondsPerTick = SecondsPerTick, SinceTick = _sinceTick, Tick = _tick,
            HungerTicks = _hungerTicks, ThirstTicks = _thirstTicks,
            Preferences = (byte[])Preferences.Clone(),
            SoundedId = IdentifyBinding(Sounded, bindings?.IdentifySounded, nameof(Sounded)),
            NearbyLitterId = IdentifyBinding(NearbyLitter, bindings?.IdentifyNearbyLitter, nameof(NearbyLitter))
        };
        ValidateState(state);
        return state;
    }

    /// <summary>Fresh ownership only. Fully validates and copies before resolving external
    /// bindings; never touches the source owner, Preferences or external simulation state.
    /// Construction does not Spawn/Decide/Step/Reconcile, draw randomness or emit sounds.
    /// Sounded and NearbyLitter are rebound by pure lookup after validation, not replayed.</summary>
    public static VisitorNeeds FromState(State state, StateBindings bindings = null)
    {
        ValidateState(state);
        // Copy all owned mutable containers BEFORE calling even trusted identity resolvers.
        var restored = new VisitorNeeds(SnapshotRandom.FromState(state.Random))
        {
            BubbleBudget = state.BubbleBudget, Unknown78Bar = state.Unknown78Bar,
            SickBar = state.SickBar, ToiletBar = state.ToiletBar,
            HungerBar = state.HungerBar, ThirstBar = state.ThirstBar,
            SecondsPerRise = state.SecondsPerRise, _sinceRise = state.SinceRise,
            SecondsPerTick = state.SecondsPerTick, _sinceTick = state.SinceTick,
            _tick = state.Tick, _hungerTicks = state.HungerTicks, _thirstTicks = state.ThirstTicks
        };
        foreach (var guest in state.Guests)
            restored._byGuest.Add(guest.Id, VisitorWants.FromState(guest.Wants));
        restored._byGuest.RestoreLayout(state.GuestLayout);
        restored._holdingBubble.UnionWith(state.HoldingBubble);
        restored.Rates.Clear();
        foreach (var entry in state.Rates)
            restored.Rates.Add(entry.Key, entry.Value == null ? null :
                new Rate(entry.Value.Base, entry.Value.Spread, entry.Value.High));
        string soundedId = state.SoundedId, litterId = state.NearbyLitterId;
        var sounded = ResolveBinding(soundedId, bindings?.ResolveSounded, nameof(Sounded));
        var litter = ResolveBinding(litterId, bindings?.ResolveNearbyLitter, nameof(NearbyLitter));
        restored.Sounded = sounded;
        restored.NearbyLitter = litter;
        return restored;
    }

    private VisitorNeeds(SnapshotRandom random) => _rng = random;

    static string IdentifyBinding<T>(T value, Func<T, string> identify, string name) where T : Delegate
    {
        if (value == null) return null;
        if (identify == null) throw new ArgumentException($"Missing identity binding for {name}.");
        string id = identify(value);
        if (string.IsNullOrWhiteSpace(id) || id.Length > 1024)
            throw new ArgumentException($"Invalid identity binding for {name}.");
        return id;
    }
    static T ResolveBinding<T>(string id, Func<string, T> resolve, string name) where T : Delegate
    {
        if (id == null) return null;
        return resolve?.Invoke(id) ?? throw new ArgumentException($"Unresolved {name} binding: {id}.");
    }

    static void ValidateState(State state)
    {
        ArgumentNullException.ThrowIfNull(state);
        void Require(bool ok, string why)
        {
            if (!ok) throw new ArgumentException("Invalid VisitorNeeds state: " + why, nameof(state));
        }
        Require(state.Version == StateVersion, "version");
        Require(state.Guests != null && state.Guests.Length <= StateGuestLimit, "guest array");
        Require(state.HoldingBubble != null && state.HoldingBubble.Length <= state.Guests.Length, "bubble array");
        Require(state.Rates != null && state.Rates.Length <= StateRateLimit, "rate array");
        Require(state.Preferences != null && state.Preferences.AsSpan().SequenceEqual(Preferences),
            "GAME/config Preferences table differs; owner restore cannot change it");
        Require(double.IsFinite(state.SecondsPerRise) && state.SecondsPerRise > 0 &&
                double.IsFinite(state.SecondsPerTick) && state.SecondsPerTick > 0, "periods");
        // Do not insist the carry is below the current period: callers can change a period
        // between steps. Nor constrain budgets/bars: signed overrides and held > budget are legal.
        Require(double.IsFinite(state.SinceRise) && state.SinceRise >= 0 &&
                double.IsFinite(state.SinceTick) && state.SinceTick >= 0 && state.Tick >= 0, "clocks");
        Require(state.HungerTicks >= 0 && state.HungerTicks < HungerTicks &&
                state.ThirstTicks >= 0 && state.ThirstTicks < ThirstTicks, "appetite cadence");
        Require(state.Random != null, "random");
        _ = SnapshotRandom.FromState(state.Random);
        var ids = new HashSet<int>();
        foreach (var guest in state.Guests)
        {
            Require(guest != null && ids.Add(guest.Id) && guest.Wants != null, "duplicate ID/null guest");
            _ = VisitorWants.FromState(guest.Wants);
        }
        var holding = new HashSet<int>();
        foreach (int id in state.HoldingBubble)
            Require(ids.Contains(id) && holding.Add(id), "unknown/duplicate bubble guest ID");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in state.Rates)
            Require(entry != null && entry.Key != null && entry.Key.Length <= 1024 && keys.Add(entry.Key),
                "null/duplicate rate key");
        foreach (string id in new[] { state.SoundedId, state.NearbyLitterId })
            Require(id == null || (!string.IsNullOrWhiteSpace(id) && id.Length <= 1024), "binding ID");
    }
}
