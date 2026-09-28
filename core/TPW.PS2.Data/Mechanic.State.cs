using System.Text.Json.Serialization;
namespace TPW.PS2.Data;

public sealed partial class Mechanic
{
    // Sound handles are external callbacks, not stored fields. Job is the base Target alias.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record JobSnapshot
    {
        public required int Version { get; init; }
        public required int RepairDispatches { get; init; }
        public required int InstallDispatches { get; init; }
    }
    internal JobSnapshot CaptureJobState() => new()
    { Version = 1, RepairDispatches = RepairDispatches, InstallDispatches = InstallDispatches };
    internal static void ValidateJobSnapshot(JobSnapshot s)
    {
        if (s == null || s.Version != 1 || s.RepairDispatches < 0 || s.InstallDispatches < 0)
            throw new ArgumentException("Invalid Mechanic job snapshot");
    }
    internal void RestoreJobState(JobSnapshot s)
    { RepairDispatches = s.RepairDispatches; InstallDispatches = s.InstallDispatches; }
}
