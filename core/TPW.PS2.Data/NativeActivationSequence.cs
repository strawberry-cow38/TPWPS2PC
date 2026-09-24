namespace TPW.PS2.Data;

/// <summary>1093B0 shared activation post-increment, NOT Guest.Id or a pool ordinal.
/// The caller must supply and describe the sequence origin. Mirroring only represented
/// port lifetimes is useful, but does not establish the native game's complete history.</summary>
public sealed class NativeActivationSequence
{
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
