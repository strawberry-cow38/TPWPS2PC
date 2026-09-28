using System.Security.Cryptography;
using System.Text;

namespace TPW.PS2.Data;

/// <summary>Explicit identity registry. Populate capture and staged-restore registries separately with
/// the SAME stable IDs. Assets, producers, clocks, ride instances, head adapters and delegates are
/// external graph nodes, never serialized values. Resolve never invokes a delegate. Registrations must
/// include tombstoned rides still referenced by stale ring/stack slots. The root owns their lifetime.</summary>
public sealed class AdvisorStateBindings
{
    readonly Dictionary<string, object> values = new(StringComparer.Ordinal);
    readonly Dictionary<object, string> ids = new(ReferenceEqualityComparer.Instance);
    public void Add(string id, object value)
    {
        if (string.IsNullOrWhiteSpace(id) || value == null || values.ContainsKey(id) || ids.ContainsKey(value))
            throw new ArgumentException("Duplicate or invalid advisor binding");
        values.Add(id, value); ids.Add(value, id);
    }
    public string Identify(object value) => value == null ? null : ids.TryGetValue(value, out var id)
        ? id : throw new InvalidDataException("Unregistered advisor reference: " + value.GetType().Name);
    public T Resolve<T>(string id) where T : class => id == null ? null : values.TryGetValue(id, out var value) && value is T typed
        ? typed : throw new InvalidDataException("Unknown or incorrectly typed advisor reference: " + id);
    internal T Required<T>(string id) where T : class => Resolve<T>(id) ?? throw new InvalidDataException("Missing advisor binding");
    internal static string Hash(Action<BinaryWriter> write)
    {
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true)) write(writer);
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }
}

public sealed partial class AdvisorScheduler
{
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required string RulesId { get; init; }
        public required string RulesFingerprint { get; init; }
        public required string ProducersId { get; init; }
        public required string DayId { get; init; }
        public required short[] Variables { get; init; }
        public required short[] Counters { get; init; }
        public required uint[] Next { get; init; }
        public required uint[] LastFail { get; init; }
        public required int VariableCursor { get; init; }
        public required int RuleCursor { get; init; }
        public required bool Warm { get; init; }
        public required int UpgradeMonthLatch { get; init; }
        public required bool UpgradeLatched { get; init; }
        public required long Calls { get; init; }
        public required long Evaluations { get; init; }
        public required Report Last { get; init; }
    }
    public sealed record Report([property: System.Text.Json.Serialization.JsonRequired] int Refreshed, [property: System.Text.Json.Serialization.JsonRequired] bool Gated, [property: System.Text.Json.Serialization.JsonRequired] int ConsideredRule, [property: System.Text.Json.Serialization.JsonRequired] bool Due, [property: System.Text.Json.Serialization.JsonRequired] AdvisorRules.Result? Result, [property: System.Text.Json.Serialization.JsonRequired] AdvisorRules.Effect[] Effects);
    public static string Fingerprint(AdvisorRules rules) => AdvisorStateBindings.Hash(w =>
    {
        w.Write(rules.Rules.Count);
        foreach (var r in rules.Rules)
        {
            w.Write(r.WordOffset); w.Write(r.DelayDays); w.Write(r.SavedNextDay); w.Write(r.SavedLastFailureDay);
            w.Write(r.Instructions.Count);
            foreach (var i in r.Instructions) { w.Write(i.WordOffset); w.Write((short)i.Code); w.Write(i.A); w.Write(i.B); }
        }
    });
    /// <summary>CORE, not native save. Caller must freeze the entire park and all bindings between passes;
    /// never capture from producer/effect callbacks or concurrently with Step/AddCounter/Produce.</summary>
    public Snapshot CaptureState(AdvisorStateBindings bindings, bool quiescent)
    {
        if (!quiescent || _stepping) throw new InvalidOperationException("Advisor capture requires a quiescent park");
        return new Snapshot { Version = 1, RulesId = bindings.Identify(_rules), RulesFingerprint = Fingerprint(_rules),
            ProducersId = bindings.Identify(_producers), DayId = bindings.Identify(_day),
            Variables = (short[])Variables.Clone(), Counters = (short[])Counters.Clone(), Next = (uint[])_next.Clone(),
            LastFail = (uint[])_lastFail.Clone(), VariableCursor = VariableCursor, RuleCursor = RuleCursor, Warm = Warm,
            UpgradeMonthLatch = UpgradeMonthLatch, UpgradeLatched = UpgradeLatched, Calls = Calls, Evaluations = Evaluations,
            Last = new(Last.Refreshed, Last.Gated, Last.ConsideredRule, Last.Due, Last.Result, Last.Effects.ToArray()) };
    }
    public static AdvisorScheduler FromState(Snapshot s, AdvisorStateBindings b)
    {
        if (s == null || s.Version != 1) throw new InvalidDataException("Advisor scheduler version");
        var rules = b.Required<AdvisorRules>(s.RulesId);
        var producers = b.Required<IAdvisorProducers>(s.ProducersId); var day = b.Required<Func<uint>>(s.DayId);
        int n = rules.Rules.Count;
        if (s.RulesFingerprint != Fingerprint(rules) || s.Variables?.Length != VariableCount || s.Counters?.Length != CounterCount ||
            s.Next?.Length != n || s.LastFail?.Length != n || (uint)s.VariableCursor >= VariableCount || (uint)s.RuleCursor >= n ||
            (uint)s.UpgradeMonthLatch > ushort.MaxValue || s.Calls < 0 || s.Evaluations < 0 || s.Evaluations > s.Calls ||
            s.Last == null || s.Last.Refreshed < 0 || s.Last.Refreshed > 5 || s.Last.ConsideredRule < -1 || s.Last.ConsideredRule >= n ||
            (s.Last.Result.HasValue && !Enum.IsDefined(s.Last.Result.Value)) || s.Last.Effects == null ||
            s.Last.Effects.Any(e => e == null || e.Code is not (AdvisorRules.Op.Message or AdvisorRules.Op.TextUi) || e.MessageId >= AdvisorCatalogue.MessageCount))
            throw new InvalidDataException("Invalid advisor scheduler state");
        return new AdvisorScheduler(s, rules, producers, day);
    }
    // No day reads, producers, interpretation or constructor initialization.
    AdvisorScheduler(Snapshot s, AdvisorRules rules, IAdvisorProducers producers, Func<uint> day)
    {
        _rules = rules; _producers = producers; _day = day; RuleCount = rules.Rules.Count;
        _next = (uint[])s.Next.Clone(); _lastFail = (uint[])s.LastFail.Clone();
        s.Variables.CopyTo(Variables, 0); s.Counters.CopyTo(Counters, 0);
        VariableCursor = s.VariableCursor; RuleCursor = s.RuleCursor; Warm = s.Warm;
        UpgradeMonthLatch = s.UpgradeMonthLatch; UpgradeLatched = s.UpgradeLatched;
        Calls = s.Calls; Evaluations = s.Evaluations;
        Last = new(s.Last.Refreshed, s.Last.Gated, s.Last.ConsideredRule, s.Last.Due, s.Last.Result, s.Last.Effects.ToArray());
    }
    bool _stepping;
    /// <summary>Expose the identity of the clock delegate for root binding registration, not its value.</summary>
    public Func<uint> DayBinding => _day;
}
