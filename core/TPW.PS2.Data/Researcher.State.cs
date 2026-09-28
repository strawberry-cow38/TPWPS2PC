using System.Text.Json.Serialization;
namespace TPW.PS2.Data;

public sealed partial class Researcher
{
    // Research manager/project and audio ownership are external to this member.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record JobSnapshot
    {
        public required int Version { get; init; }
        public required long Quanta { get; init; }
    }
    internal JobSnapshot CaptureJobState() => new() { Version = 1, Quanta = Quanta };
    internal static void ValidateJobSnapshot(JobSnapshot s)
    {
        if (s == null || s.Version != 1 || s.Quanta < 0)
            throw new ArgumentException("Invalid Researcher job snapshot");
    }
    internal void RestoreJobState(JobSnapshot s) => Quanta = s.Quanta;
}
