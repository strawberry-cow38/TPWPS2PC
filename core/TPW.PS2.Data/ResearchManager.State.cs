#nullable enable
using System.Text.Json.Serialization;
namespace TPW.PS2.Data;

// Registry callbacks are binding operations only: never invoke a gameplay delegate.
internal static class StaffLeafBindings
{
    internal static void Require(bool ok, string message)
    { if (!ok) throw new ArgumentException("Invalid staff leaf snapshot: " + message); }
    internal static bool ValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 1024;
    internal static string? Key(Delegate? value, Func<Delegate, string> id)
    {
        if (value == null) return null;
        var key = id(value); Require(ValidKey(key), "callback key"); return key;
    }
    internal static T? Resolve<T>(string? key, Func<string, Delegate> resolve) where T : Delegate
    {
        if (key == null) return null;
        Require(ValidKey(key), "callback key");
        var result = resolve(key); Require(result is T, "callback type/unresolved: " + key); return (T)result;
    }
}

public sealed partial class ResearchManager
{
    public const int StateVersion = 1;
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ProjectState
    {
        public required int Slot { get; init; }
        public required int Weight { get; init; }
        public required uint Required { get; init; }
        public required uint Progress { get; init; }
        public required bool Active { get; init; }
        public required bool Complete { get; init; }
        public required int Item { get; init; }
        public required int Category { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required int Budget { get; init; }
        public required bool CompletedFlag { get; init; }
        public required long Quanta { get; init; }
        public required int Completions { get; init; }
        public required ProjectState[] Slots { get; init; }
        public required string? ItemLevelKey { get; init; }
        public required string? MechanicCountKey { get; init; }
        public required string? AnythingLeftToResearchKey { get; init; }
        public required string? ResearchedKey { get; init; }
        public required string? AdvisorKey { get; init; }
        public required string? AllResearchedKey { get; init; }
    }
    /// <summary>Stable shells for the enclosing registry, including inactive projects.</summary>
    public ResearchProject ProjectAt(int slot) => _slots[slot];
    public int ProjectSlot(ResearchProject project)
    {
        int slot = Array.IndexOf(_slots, project);
        if (slot < 0) throw new ArgumentException("Project does not belong to this manager.");
        return slot;
    }
    public State CaptureState(Func<Delegate, string> callbackId)
    {
        ArgumentNullException.ThrowIfNull(callbackId);
        return new State {
            Version = StateVersion, Budget = Budget, CompletedFlag = CompletedFlag,
            Quanta = Quanta, Completions = Completions,
            Slots = _slots.Select(s => new ProjectState { Slot = s.Slot, Weight = s.Weight,
                Required = s.Required, Progress = s.Progress, Active = s.Active, Complete = s.Complete,
                Item = s.Item, Category = s.Category }).ToArray(),
            ItemLevelKey = StaffLeafBindings.Key(ItemLevel, callbackId),
            MechanicCountKey = StaffLeafBindings.Key(MechanicCount, callbackId),
            AnythingLeftToResearchKey = StaffLeafBindings.Key(AnythingLeftToResearch, callbackId),
            ResearchedKey = StaffLeafBindings.Key(Researched, callbackId),
            AdvisorKey = StaffLeafBindings.Key(Advisor, callbackId),
            AllResearchedKey = StaffLeafBindings.Key(AllResearched, callbackId)
        };
    }
    public static void ValidateState(State s)
    {
        ArgumentNullException.ThrowIfNull(s);
        StaffLeafBindings.Require(s.Version == StateVersion && s.Slots != null && s.Slots.Length == SlotCount, "research schema/slots");
        for (int i = 0; i < SlotCount; i++)
            StaffLeafBindings.Require(s.Slots![i] != null && s.Slots[i].Slot == i, "research slot identity");
        foreach (var key in new[] { s.ItemLevelKey, s.MechanicCountKey, s.AnythingLeftToResearchKey,
            s.ResearchedKey, s.AdvisorKey, s.AllResearchedKey })
            StaffLeafBindings.Require(key == null || StaffLeafBindings.ValidKey(key), "research callback key");
    }
    /// <summary>Hydrate unpublished existing shells, never replacing project identities or replaying events.
    /// All bindings resolve before any write. Counters/unsigned progress/stale inactive data are not normalized.</summary>
    public void RestoreState(State s, Func<string, Delegate> resolveCallback)
    {
        ValidateState(s); ArgumentNullException.ThrowIfNull(resolveCallback);
        var level = StaffLeafBindings.Resolve<Func<int,int,int>>(s.ItemLevelKey, resolveCallback);
        var mechanics = StaffLeafBindings.Resolve<Func<int>>(s.MechanicCountKey, resolveCallback);
        var left = StaffLeafBindings.Resolve<Func<bool>>(s.AnythingLeftToResearchKey, resolveCallback);
        var researched = StaffLeafBindings.Resolve<Action<ResearchProject>>(s.ResearchedKey, resolveCallback);
        var advisor = StaffLeafBindings.Resolve<Action<int>>(s.AdvisorKey, resolveCallback);
        var all = StaffLeafBindings.Resolve<Action>(s.AllResearchedKey, resolveCallback);
        for (int i = 0; i < SlotCount; i++) {
            var p = _slots[i]; var v = s.Slots[i];
            p.Weight = v.Weight; p.Required = v.Required; p.Progress = v.Progress;
            p.Active = v.Active; p.Complete = v.Complete; p.Item = v.Item; p.Category = v.Category;
        }
        Budget = s.Budget; CompletedFlag = s.CompletedFlag; Quanta = s.Quanta; Completions = s.Completions;
        ItemLevel = level!; MechanicCount = mechanics!; AnythingLeftToResearch = left!;
        Researched = researched!; Advisor = advisor!; AllResearched = all!;
    }
    public static ResearchManager FromState(State s, Func<string, Delegate> resolveCallback)
    { var manager = new ResearchManager(); manager.RestoreState(s, resolveCallback); return manager; }
}
