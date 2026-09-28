using System.Text.Json.Serialization;
namespace TPW.PS2.Data;

public sealed partial class Handyman
{
    // Handyman has NO own mutable fields: deadline, claim target and mode are all base state.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record JobSnapshot
    {
        public required int Version { get; init; }
    }
    internal JobSnapshot CaptureJobState() => new() { Version = 1 };
    internal static void ValidateJobSnapshot(JobSnapshot s)
    {
        if (s == null || s.Version != 1) throw new ArgumentException("Invalid Handyman job snapshot");
    }
}
