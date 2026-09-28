namespace TPW.PS2.Data;

/// <summary>1093B0 shared activation post-increment, NOT Guest.Id or a pool ordinal.
/// The caller must supply and describe the sequence origin. Mirroring only represented
/// port lifetimes is useful, but does not establish the native game's complete history.</summary>
public sealed class NativeActivationSequence
{
    public const int StateSchemaVersion = 1;

    /// <summary>Sequence provenance and counters; serials are not reconstructed from totals.</summary>
    public sealed record State
    {
        public required int SchemaVersion { get; init; }
        public required uint NextSerial { get; init; }
        public required string Origin { get; init; }
        public required bool NativeHistoryVerified { get; init; }
        public required ulong Activations { get; init; }
        public required Dictionary<string, ulong> ActivationsByKind { get; init; }
    }

    /// <summary>Detached snapshot; callers must pause activation and DTO mutation while saving/loading.</summary>
    public State CaptureState() => new()
    {
        SchemaVersion = StateSchemaVersion, NextSerial = NextSerial, Origin = Origin,
        NativeHistoryVerified = NativeHistoryVerified, Activations = Activations,
        ActivationsByKind = new Dictionary<string, ulong>(byKind, StringComparer.Ordinal)
    };

    /// <summary>Validate all fields before creating an independent sequence, without Activate replay.</summary>
    public static NativeActivationSequence FromState(State state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != StateSchemaVersion || string.IsNullOrWhiteSpace(state.Origin)
            || state.ActivationsByKind == null)
            throw new ArgumentException("Invalid activation sequence schema, origin or counts.", nameof(state));
        var counts = new Dictionary<string, ulong>(state.ActivationsByKind, StringComparer.Ordinal);
        ulong total = 0;
        foreach (var pair in counts)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                throw new ArgumentException("Activation family must be explicit.", nameof(state));
            // Totals and per-family counters are ulong words: preserve wrap, including zero.
            total = unchecked(total + pair.Value);
        }
        if (total != state.Activations)
            throw new ArgumentException("Activation total disagrees with family counts.", nameof(state));
        var result = new NativeActivationSequence(state.NextSerial, state.Origin, state.NativeHistoryVerified)
        {
            Activations = state.Activations
        };
        foreach (var pair in counts) result.byKind.Add(pair.Key, pair.Value);
        return result;
    }

    readonly Dictionary<string, ulong> byKind = new();
    public uint NextSerial { get; private set; }
    public string Origin { get; }
    public bool NativeHistoryVerified { get; }
    public ulong Activations { get; private set; }
    public IReadOnlyDictionary<string, ulong> ActivationsByKind => byKind;

    public NativeActivationSequence(uint firstSerial, string origin, bool nativeHistoryVerified = false)
    {
        if (string.IsNullOrWhiteSpace(origin)) throw new ArgumentException("Activation origin must be explicit.", nameof(origin));
        NextSerial = firstSerial; Origin = origin; NativeHistoryVerified = nativeHistoryVerified;
    }

    /// <summary>Every actual activation consumes one value, including reuse. Construction
    /// of pooled storage, lookup, render frames and bus trips are NOT activations.</summary>
    public uint Activate(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("Activation family must be explicit.", nameof(kind));
        uint serial = NextSerial;
        NextSerial = unchecked(serial + 1); // 1093C4 return-delay-slot store, native word wrap
        Activations++;
        byKind[kind] = byKind.GetValueOrDefault(kind) + 1;
        return serial;
    }
}
