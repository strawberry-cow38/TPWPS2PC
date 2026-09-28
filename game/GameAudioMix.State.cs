using System.Text.Json.Serialization;
namespace TPWPS2Viewer;
public sealed partial class GameAudioMix
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version {get;init;}
        public required int Music {get;init;}
        public required int Sfx {get;init;}
        public required int MusicTarget {get;init;}
        public required int SfxTarget {get;init;}
        public required float MusicGain {get;init;}
        public required float SfxGain {get;init;}
    }
    public Snapshot CaptureState()=>new(){Version=1,Music=Music,Sfx=Sfx,MusicTarget=MusicTarget,SfxTarget=SfxTarget,MusicGain=MusicGain,SfxGain=SfxGain};
    /// <summary>Stage ramp state without changing GLOBAL AudioServer buses or GAME settings.
    /// Root calls PublishGains only after accepting the entire loaded world.</summary>
    public void RestoreState(Snapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if(s.Version!=1||new[]{s.Music,s.Sfx,s.MusicTarget,s.SfxTarget}.Any(v=>v < -1||v>128)
            ||new[]{s.Music,s.Sfx,s.MusicTarget,s.SfxTarget}.Count(v=>v<0) is not (0 or 4)
            ||!float.IsFinite(s.MusicGain)||!float.IsFinite(s.SfxGain)||s.MusicGain<0||s.SfxGain<0)
            throw new InvalidDataException("Invalid audio-mix snapshot");
        Music=s.Music;Sfx=s.Sfx;MusicTarget=s.MusicTarget;SfxTarget=s.SfxTarget;MusicGain=s.MusicGain;SfxGain=s.SfxGain;
    }
    public void PublishGains(){Apply(MusicBus,MusicGain);Apply(SfxBus,SfxGain);}
}
