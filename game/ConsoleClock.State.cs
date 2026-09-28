using System.Text.Json.Serialization;
namespace TPWPS2Viewer;
public sealed partial class ConsoleClock
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version {get;init;}
        public required double Carry {get;init;}
        public required float Alpha {get;init;}
    }
    public Snapshot CaptureState()=>new(){Version=1,Carry=_carry,Alpha=Alpha};
    public static void ValidateState(Snapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if(s.Version!=1||!double.IsFinite(s.Carry)||s.Carry<0||s.Carry>=TickSeconds||!float.IsFinite(s.Alpha)
            ||s.Alpha!=(float)Math.Clamp(s.Carry/TickSeconds,0,1))throw new InvalidDataException("Invalid console clock state");
    }
    public void RestoreState(Snapshot s){ValidateState(s);_carry=s.Carry;Alpha=s.Alpha;}
}
