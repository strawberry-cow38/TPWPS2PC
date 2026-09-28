using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public sealed partial class RsePreviewHost
{
    public const int StateVersion = 1;

    /// <summary>Logical preview state only. AssetKey is caller-owned identity (prefer a content
    /// hash/versioned key); no APS bytes, records, renderer, delegates or event subscriptions.
    /// All members, including nullable Current/Queued/LastEffect, must be present in JSON.
    /// Callers must serialize access to this host while capturing/restoring.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class State
    {
        public required int Version { get; init; }
        public required string AssetKey { get; init; }
        public required long Time { get; init; }
        public required PlaybackState Current { get; init; }
        public required PlaybackState Queued { get; init; }
        public required Dictionary<int, PlaybackState> Channels { get; init; }
        public required int HeadSlots { get; init; }
        public required Dictionary<int, int> Seats { get; init; }
        public required WalkerState[] Walkers { get; init; }
        public required HiddenState[] Visibility { get; init; }
        public required EffectState LastEffect { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class PlaybackState
    {
        public required int Slot { get; init; }
        public required int Variant { get; init; }
        public required long Start { get; init; }
        public required bool Loop { get; init; }
        public required int Speed { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public readonly struct WalkerState
    {
        public required long Time { get; init; }
        public required int Guest { get; init; }
        public required int FromNode { get; init; }
        public required int ToNode { get; init; }
        public required int Mode { get; init; }
        public required int PerMille { get; init; }
        public required int Angle { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public readonly struct HiddenState
    {
        public required long Time { get; init; }
        public required int Guest { get; init; }
        public required bool Visible { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class EffectState
    {
        public required long Time { get; init; }
        public required RseOpcode Opcode { get; init; }
        public required int[] Arguments { get; init; }
    }

    /// <summary>Capture every mutable logical field, cloning all collections. Seats have no
    /// retained timestamp in the host (HeadChanged is an event, not an event history).
    /// _slots is immutable asset binding; NodeSource and all events remain external.</summary>
    public State CaptureState(string assetKey)
    {
        CheckAssetKey(assetKey);
        PlaybackState Save(Playback p)
        {
            if (p == null) return null;
            if (!_slots.TryGetValue(p.Record.Slot, out var records) || p.Variant < 0 ||
                p.Variant >= records.Length || !ReferenceEquals(records[p.Variant], p.Record))
                throw new ArgumentException("Playback does not belong to this host's animation.");
            return new PlaybackState { Slot = p.Record.Slot, Variant = p.Variant,
                Start = p.Start, Loop = p.Loop, Speed = p.Speed };
        }
        var state = new State
        {
            Version = StateVersion, AssetKey = assetKey, Time = Time,
            Current = Save(Current), Queued = Save(_queued),
            Channels = _channels.ToDictionary(p => p.Key, p => Save(p.Value)),
            HeadSlots = HeadSlots, Seats = _seats.ToDictionary(p => p.Key, p => p.Value),
            Walkers = _walkers.Select(p => new WalkerState
            {
                Time = p.Value.Time, Guest = p.Value.Guest, FromNode = p.Value.FromNode,
                ToNode = p.Value.ToNode, Mode = p.Value.Mode, PerMille = p.Value.PerMille, Angle = p.Value.Angle
            }).ToArray(),
            Visibility = _visible.Select(p => new HiddenState
                { Time = p.Value.Time, Guest = p.Value.Guest, Visible = p.Value.Visible }).ToArray(),
            LastEffect = LastEffect == null ? null : new EffectState
                { Time = LastEffect.Time, Opcode = LastEffect.Opcode, Arguments = LastEffect.Arguments.ToArray() }
        };
        // Same validation on both boundaries, without mutating or calling external services.
        PrepareState(state, assetKey);
        return state;
    }

    static void CheckAssetKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("An explicit animation asset key is required.");
    }

    sealed record Prepared(Playback Current, Playback Queued, Dictionary<int, Playback> Channels,
        Dictionary<int, int> Seats, Dictionary<int, Walker> Walkers, Dictionary<int, Hidden> Visibility,
        Effect Effect, long Time, int HeadSlots);

    Prepared PrepareState(State state, string expectedAssetKey)
    {
        CheckAssetKey(expectedAssetKey);
        if (state == null) throw new ArgumentNullException(nameof(state));
        void Require(bool ok, string message)
        {
            if (!ok) throw new ArgumentException("Invalid RsePreviewHost state: " + message, nameof(state));
        }
        Require(state.Version == StateVersion, "version");
        Require(string.Equals(state.AssetKey, expectedAssetKey, StringComparison.Ordinal), "asset identity");
        Require(state.Time >= 0, "time");
        Require(state.HeadSlots >= 0, "head slots");
        Require(state.Channels != null && state.Seats != null && state.Walkers != null && state.Visibility != null,
            "null collection");
        bool Timestamp(long t) => t >= 0 && t <= state.Time;
        Playback Load(PlaybackState p, bool queued = false)
        {
            if (p == null) return null;
            Require(p.Start >= 0 && (queued ? p.Start > state.Time : p.Start <= state.Time), "playback start");
            Require(p.Speed > 0, "playback speed");
            Require(_slots.TryGetValue(p.Slot, out var records) && p.Variant >= 0 && p.Variant < records.Length,
                "animation slot/variant is absent from bound animation");
            var play = new Playback(records[p.Variant], p.Variant, p.Start, p.Loop, p.Speed);
            try
            {
                Require(play.Record.DurationFrames >= 0, "negative asset duration");
                _ = checked(play.Start + Wall(play));
            }
            catch (OverflowException) { throw new ArgumentException("Playback time overflow.", nameof(state)); }
            return play;
        }
        var current = Load(state.Current);
        var queued = Load(state.Queued, true);
        Require(queued == null || (current is { Loop: false } && queued.Start == current.Start + Wall(current)),
            "pending playback must follow current one-shot");
        var channels = new Dictionary<int, Playback>();
        foreach (var pair in state.Channels)
        {
            Require(pair.Key != 0 && pair.Value != null, "channel zero/null playback");
            channels.Add(pair.Key, Load(pair.Value));
        }
        var seats = new Dictionary<int, int>(state.Seats);
        // Public host methods allow arbitrary guest/node/seat/channel IDs. Do not invent model
        // bounds: HeadSlots may have changed after seating, and guest IDs are caller-defined.
        Require(seats.Values.All(g => g != 0), "zero guest is an absent seat");
        var walkers = new Dictionary<int, Walker>();
        foreach (var w in state.Walkers)
        {
            Require(Timestamp(w.Time), "walker timestamp");
            Require(walkers.TryAdd(w.Guest, new Walker(w.Time, w.Guest, w.FromNode, w.ToNode,
                w.Mode, w.PerMille, w.Angle)), "duplicate walker");
        }
        var visible = new Dictionary<int, Hidden>();
        foreach (var v in state.Visibility)
        {
            Require(Timestamp(v.Time), "visibility timestamp");
            Require(visible.TryAdd(v.Guest, new Hidden(v.Time, v.Guest, v.Visible)), "duplicate visibility");
        }
        Effect effect = null;
        if (state.LastEffect is { } e)
        {
            Require(Timestamp(e.Time) && e.Arguments != null, "effect timestamp/arguments");
            // TryEffect accepts any opcode/argument sequence; this is a logical record, not a
            // request to execute it. Copy even though the exposed runtime list is read-only.
            effect = new Effect(e.Time, e.Opcode, Array.AsReadOnly((int[])e.Arguments.Clone()));
        }
        return new Prepared(current, queued, channels, seats, walkers, visible, effect, state.Time, state.HeadSlots);
    }

    /// <summary>Validate and resolve against this host's bound Animation, then replace logical
    /// state only. Never plays animation, emits effects, moves walkers, changes heads through
    /// callbacks, or queries NodeSource. Existing bindings/subscribers survive untouched.
    /// Caller supplies expected asset identity independently of the saved payload.</summary>
    public void RestoreState(State state, string expectedAssetKey)
    {
        var p = PrepareState(state, expectedAssetKey);
        // Allocate before committing logical state; keep the existing public dictionary views.
        _channels.EnsureCapacity(p.Channels.Count); _seats.EnsureCapacity(p.Seats.Count);
        _walkers.EnsureCapacity(p.Walkers.Count); _visible.EnsureCapacity(p.Visibility.Count);
        Time = p.Time; HeadSlots = p.HeadSlots; Current = p.Current; _queued = p.Queued; LastEffect = p.Effect;
        _channels.Clear(); foreach (var pair in p.Channels) _channels.Add(pair.Key, pair.Value);
        _seats.Clear(); foreach (var pair in p.Seats) _seats.Add(pair.Key, pair.Value);
        _walkers.Clear(); foreach (var pair in p.Walkers) _walkers.Add(pair.Key, pair.Value);
        _visible.Clear(); foreach (var pair in p.Visibility) _visible.Add(pair.Key, pair.Value);
    }
}
