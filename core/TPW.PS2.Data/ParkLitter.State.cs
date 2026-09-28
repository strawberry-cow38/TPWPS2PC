#nullable enable
using System.Text.Json.Serialization;
namespace TPW.PS2.Data;
public sealed partial class ParkLitter
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ItemState
    {
        public required int PoolSlot { get; init; }
        public required int Variant { get; init; }
        public required uint Serial { get; init; }
        public required short PositionX { get; init; }
        public required short PositionZ { get; init; }
        public required bool Vomit { get; init; }
        public required string? ClaimantKey { get; init; }
        public required bool Shown { get; init; }
        public required bool Active { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required ItemState[] Slots { get; init; }
        public required int[] Free { get; init; }
        public required int[] Active { get; init; }
        public required int Made { get; init; }
        public required int RefusedFull { get; init; }
        public required int Swept { get; init; }
        public required string? AddedKey { get; init; }
        public required string? RemovedKey { get; init; }
    }
    /// <summary>Includes stale inactive slots for the world identity registry.</summary>
    public IReadOnlyList<LitterItem> Slots => _slots;
    public IReadOnlyList<LitterItem> Free => _free;
    public NativeActivationSequence Activations => _activations;
    public Func<int, int> RandomSource => _random;
    public State CaptureState(Func<StaffMember, string> staffId, Func<Delegate, string> callbackId)
    {
        ArgumentNullException.ThrowIfNull(staffId); ArgumentNullException.ThrowIfNull(callbackId);
        var s = new State {
            Version = 1, Made = Made, RefusedFull = RefusedFull, Swept = Swept,
            Free = _free.Select(i => i.PoolSlot).ToArray(), Active = _active.Select(i => i.PoolSlot).ToArray(),
            AddedKey = StaffLeafBindings.Key(Added, callbackId), RemovedKey = StaffLeafBindings.Key(Removed, callbackId),
            Slots = _slots.Select(i => new ItemState { PoolSlot = i.PoolSlot, Variant = i.Variant,
                Serial = i.Serial, PositionX = i.Position.X, PositionZ = i.Position.Z, Vomit = i.Vomit,
                ClaimantKey = i.Claimant == null ? null : staffId(i.Claimant), Shown = i.Shown, Active = i.Active }).ToArray()
        };
        ValidateState(s); return s;
    }
    public static void ValidateState(State s)
    {
        ArgumentNullException.ThrowIfNull(s);
        StaffLeafBindings.Require(s.Version == 1 && s.Slots != null && s.Slots.Length == Capacity
            && s.Free != null && s.Active != null && s.Free.Length + s.Active.Length == Capacity, "litter schema/lists");
        for (int i = 0; i < Capacity; i++) {
            var v = s.Slots![i];
            StaffLeafBindings.Require(v != null && v.PoolSlot == i, "litter slot identity");
            StaffLeafBindings.Require(v!.ClaimantKey == null || StaffLeafBindings.ValidKey(v.ClaimantKey), "claimant key");
        }
        var seen = new HashSet<int>();
        foreach (var (list, active) in new[] { (s.Free!, false), (s.Active!, true) })
            foreach (int i in list)
                StaffLeafBindings.Require((uint)i < Capacity && seen.Add(i) && s.Slots![i].Active == active, "litter membership");
        StaffLeafBindings.Require((s.AddedKey == null || StaffLeafBindings.ValidKey(s.AddedKey))
            && (s.RemovedKey == null || StaffLeafBindings.ValidKey(s.RemovedKey)), "litter callback keys");
    }
    // Deliberately NOT the gameplay constructor: no variant rolls and no activation.
    ParkLitter(State s, Func<int, int> random, NativeActivationSequence activations,
        Action<LitterItem>? added, Action<LitterItem>? removed)
    {
        _random = random; _activations = activations; Added = added!; Removed = removed!;
        Made = s.Made; RefusedFull = s.RefusedFull; Swept = s.Swept;
        for (int i = 0; i < Capacity; i++) {
            var v = s.Slots[i];
            _slots[i] = new LitterItem(i, v.Variant) { Serial = v.Serial, Position = new(v.PositionX, v.PositionZ),
                Vomit = v.Vomit, Shown = v.Shown, Active = v.Active };
        }
        _free.AddRange(s.Free.Select(i => _slots[i])); _active.AddRange(s.Active.Select(i => _slots[i]));
    }
    /// <summary>Stage slots before staff targets are resolved. Claimants remain null until HydrateClaimants;
    /// never publish the partial graph. Supply the world's exact shared RNG delegate and sequence owner.</summary>
    public static ParkLitter AllocateState(State s, Func<int, int> random, NativeActivationSequence activations,
        Func<string, Delegate> resolveCallback)
    {
        ValidateState(s); ArgumentNullException.ThrowIfNull(random); ArgumentNullException.ThrowIfNull(activations);
        ArgumentNullException.ThrowIfNull(resolveCallback);
        var added = StaffLeafBindings.Resolve<Action<LitterItem>>(s.AddedKey, resolveCallback);
        var removed = StaffLeafBindings.Resolve<Action<LitterItem>>(s.RemovedKey, resolveCallback);
        return new(s, random, activations, added, removed);
    }
    /// <summary>Resolve every external staff ID before writing any reverse link; no claim/sweep replay.</summary>
    public void HydrateClaimants(State s, Func<string, StaffMember> resolveStaff)
    {
        ValidateState(s); ArgumentNullException.ThrowIfNull(resolveStaff);
        var refs = new StaffMember?[Capacity];
        for (int i = 0; i < Capacity; i++) {
            StaffLeafBindings.Require(_slots[i].Variant == s.Slots[i].Variant, "litter shell variant");
            if (s.Slots[i].ClaimantKey is { } key) {
                refs[i] = resolveStaff(key); StaffLeafBindings.Require(refs[i] != null, "unresolved claimant: " + key);
            }
        }
        for (int i = 0; i < Capacity; i++) _slots[i].Claimant = refs[i]!;
    }
    public static ParkLitter FromState(State s, Func<int, int> random, NativeActivationSequence activations,
        Func<string, StaffMember> resolveStaff, Func<string, Delegate> resolveCallback)
    {
        var result = AllocateState(s, random, activations, resolveCallback);
        result.HydrateClaimants(s, resolveStaff); return result;
    }
}
