using Godot;
using TPW.PS2.Data;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TPWPS2Viewer;

public sealed partial class RideSounds
{
    // Asset IDs are dictionary keys only. FromState NEVER resolves events, decodes bytes,
    // evaluates providers, touches the audio buses, or uses a save string as a filename.
    public sealed record EventKey(int Park, int Kind, int Id);
    public sealed record OwnerKey(int Ride, int Tag);
    public sealed record EventBinding(SoundCatalogue Catalogue, SoundCatalogue.Resolved Event);
    public sealed class SnapshotBindings
    {
        public required string AssetsId { get; init; }
        public required string ServicesId { get; init; }
        public required SoundCatalogue Park1 { get; init; }
        public required SoundCatalogue Park2 { get; init; }
        public required IReadOnlyDictionary<EventKey, EventBinding> Events { get; init; }
        // Include null cache entries too: undecodable streams must remain undecodable.
        public required IReadOnlyDictionary<string, AudioStreamWav> Streams { get; init; }
        public required IReadOnlyDictionary<OwnerKey, Func<Vector3?>> Moving { get; init; }
        public required Func<int, int, int> ParameterValue { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required string AssetsId { get; init; }
        public required string ServicesId { get; init; }
        public required bool Park1Present { get; init; }
        public required bool Park2Present { get; init; }
        public required bool HasParameter { get; init; }
        public required SnapshotRandom.State Random { get; init; }
        public required uint GraphRandom { get; init; }
        public required int Serial { get; init; }
        // Cued, Resolved, Unresolved, StartedVoices, SilentVoices, Verdicts
        public required int[] Counters { get; init; }
        public required string[] Census { get; init; }
        public required string[] CachedStreams { get; init; }
        public required string[] NullStreams { get; init; }
        public required string[] StreamFingerprints { get; init; }
        public required string[] CatalogueIds { get; init; }
        public required OwnerKey[] Moving { get; init; }
        public required RepeatState[] Repeats { get; init; }
        public required VoiceState[] Voices { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RepeatState
    {
        public required EventKey Asset { get; init; }
        public required string Fingerprint { get; init; }
        public required int Ride { get; init; }
        public required int Tag { get; init; }
        public required int Kind { get; init; }
        public required int Set { get; init; }
        public required bool ByClipLength { get; init; }
        public required float[] At { get; init; }
        public required double Interval { get; init; }
        public required double Due { get; init; }
        public required string Head { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record VoiceState
    {
        public required int Ride { get; init; }
        public required int Tag { get; init; }
        public required int Serial { get; init; }
        public required string Name { get; init; }
        public required string Line { get; init; }
        public required string Stream { get; init; }
        public required string PlayerName { get; init; }
        public required string Bus { get; init; }
        public required bool Positional { get; init; }
        public required float[] At { get; init; }
        public required bool Loop { get; init; }
        public required bool OneShot { get; init; }
        public required int Frames { get; init; }
        public required bool? PlayingAt1 { get; init; }
        public required float MaxPosition { get; init; }
        public required int FirstAdvanceFrame { get; init; }
        public required double Elapsed { get; init; }
        public required bool Finished { get; init; }
        public required bool Verdict { get; init; }
        public required bool Fading { get; init; }
        public required float Db { get; init; }
        public required float Volume { get; init; }
        public required bool Playing { get; init; }
        public required bool HasPlayback {get;init;}
        public required bool Paused { get; init; }
        public required float Seek { get; init; }
    }
    static void RequireState(bool ok)
    { if (!ok) throw new InvalidDataException("Invalid or unsupported RideSounds continuation/bindings"); }
    static string CatalogueId(SoundCatalogue c) => c==null ? null : $"{c.World}:{c.Park}";
    static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    static string EventFingerprint(SoundCatalogue.Resolved e) => Hash(JsonSerializer.Serialize(new {
        e.Group,e.Id,e.Map,e.Clips,e.Sets,e.Word0C,e.Flags,
        Source=e.Source==null ? null : new { e.Source.Id,e.Source.Flags,e.Source.Word0C,e.Source.Word12,
            Sets=e.Source.Sets.Select(t=>new {t.Weight,t.Clips,t.Links}).ToArray() }
    }));
    static string StreamFingerprint(AudioStreamWav w) => w==null ? "null" : Hash(JsonSerializer.Serialize(new {
        w.Format,w.MixRate,w.Stereo,w.LoopMode,w.LoopBegin,w.LoopEnd,
        Digest=Convert.ToHexString(SHA256.HashData(w.Data))
    }));
    static bool Text(string s) => s != null && s.Length <= 16384;
    static float[] XYZ(Vector3 v) => new[] { v.X, v.Y, v.Z };
    static Vector3 Vector(float[] p)
    { RequireState(p != null && p.Length == 3 && p.All(float.IsFinite)); return new(p[0],p[1],p[2]); }
    EventKey Key(Repeater t)
    {
        int park = ReferenceEquals(t.Catalogue, _park1) ? 1 : ReferenceEquals(t.Catalogue, _park2) ? 2 : 0;
        RequireState(park != 0 && t.Catalogue != null);
        return new(park, (int)t.Event.Group, t.Event.Id);
    }
    /// <summary>Trusted in-memory asset manifest for capture, or same-process tests. On a cold
    /// load the world loader must independently preload these IDs from its trusted catalogue;
    /// never construct this registry by loading save-supplied paths. Treat assets as immutable.
    /// Replace Moving and ParameterValue with NEW-world providers when staging another world.</summary>
    public SnapshotBindings CaptureBindings(string assetsId, string servicesId) => new()
    {
        AssetsId=assetsId, ServicesId=servicesId, Park1=_park1, Park2=_park2,
        Events=_repeats.GroupBy(Key).ToDictionary(g=>g.Key,g=>new EventBinding(g.First().Catalogue,g.First().Event)),
        Streams=new Dictionary<string,AudioStreamWav>(_streams),
        Moving=_moving.ToDictionary(p=>new OwnerKey(p.Key.Ride,p.Key.Tag),p=>p.Value), ParameterValue=ParameterValue
    };
    public Snapshot CaptureState(string assetsId, string servicesId)
    {
        var s = new Snapshot {
            Version=1, AssetsId=assetsId, ServicesId=servicesId, Park1Present=_park1!=null, Park2Present=_park2!=null,
            HasParameter=ParameterValue!=null, Random=_rng.CaptureState(), GraphRandom=_graphRng.CurrentState,
            Serial=_serial, Counters=new[]{Cued,Resolved,Unresolved,StartedVoices,SilentVoices,Verdicts},
            Census=Census.ToArray(), CachedStreams=_streams.Keys.ToArray(),
            CatalogueIds=new[]{CatalogueId(_park1),CatalogueId(_park2)},
            StreamFingerprints=_streams.Values.Select(StreamFingerprint).ToArray(),
            NullStreams=_streams.Where(p=>p.Value==null).Select(p=>p.Key).ToArray(),
            Moving=_moving.Keys.Select(k=>new OwnerKey(k.Ride,k.Tag)).ToArray(),
            Repeats=_repeats.Select(t=>new RepeatState {Asset=Key(t),Fingerprint=EventFingerprint(t.Event),Ride=t.Ride,Tag=t.Tag,Kind=t.Kind,Set=t.Set,
                ByClipLength=t.ByClipLength,At=XYZ(t.At),Interval=t.IntervalSeconds,Due=t.Due,Head=t.Head}).ToArray(),
            Voices=_voices.Select(v=> {
                var p3=v.Player as AudioStreamPlayer3D; var p2=v.Player as AudioStreamPlayer;
                RequireState(p3!=null || p2!=null);
                var stream=(AudioStream)(p3!=null?p3.Stream:p2.Stream);
                string key=_streams.FirstOrDefault(p=>ReferenceEquals(p.Value,stream)).Key;
                RequireState(key!=null && stream!=null);
                return new VoiceState {Ride=v.Ride,Tag=v.Tag,Serial=v.Serial,Name=v.Name,Line=v.Line,Stream=key,
                    PlayerName=v.Player.Name.ToString(),Bus=(p3!=null?p3.Bus:p2.Bus).ToString(),Positional=p3!=null,
                    At=XYZ(p3?.Position ?? Vector3.Zero),Loop=v.Loop,OneShot=v.OneShot,Frames=v.Frames,
                    PlayingAt1=v.PlayingAt1,MaxPosition=v.MaxPosition,FirstAdvanceFrame=v.FirstAdvanceFrame,
                    Elapsed=v.Elapsed,Finished=v.Finished,Verdict=v.Verdict,Fading=v.Fading,Db=v.Db,
                    Volume=p3!=null?p3.VolumeDb:p2.VolumeDb,Playing=IsPlaying(v.Player),HasPlayback=p3!=null?p3.HasStreamPlayback():p2.HasStreamPlayback(),
                    Paused=p3!=null?p3.StreamPaused:p2.StreamPaused,Seek=Position(v.Player)};
            }).ToArray()
        };
        Validate(s,CaptureBindings(assetsId,servicesId)); return s;
    }
    static void Validate(Snapshot s, SnapshotBindings b)
    {
        ArgumentNullException.ThrowIfNull(s); ArgumentNullException.ThrowIfNull(b);
        RequireState(s.Version==1 && Text(s.AssetsId) && Text(s.ServicesId) && s.AssetsId==b.AssetsId && s.ServicesId==b.ServicesId &&
            s.Park1Present==(b.Park1!=null) && s.Park2Present==(b.Park2!=null) && s.HasParameter==(b.ParameterValue!=null));
        RequireState(s.CatalogueIds is {Length:2} && s.CatalogueIds[0]==CatalogueId(b.Park1) && s.CatalogueIds[1]==CatalogueId(b.Park2));
        _=SnapshotRandom.FromState(s.Random);
        RequireState(s.Serial>=0 && s.Counters is {Length:6} && s.Counters.All(n=>n>=0) &&
            (long)s.Counters[1]+s.Counters[2]==s.Counters[0] && (long)s.Counters[3]+s.Counters[4]==s.Counters[5]);
        RequireState(s.Census!=null && s.Census.Length<=1000000 && s.Census.All(Text) &&
            s.CachedStreams!=null && s.CachedStreams.Length<=100000 && s.CachedStreams.All(Text) &&
            s.CachedStreams.Distinct().Count()==s.CachedStreams.Length && b.Streams!=null && s.CachedStreams.All(b.Streams.ContainsKey));
        RequireState(s.StreamFingerprints!=null && s.StreamFingerprints.Length==s.CachedStreams.Length &&
            s.CachedStreams.Select(k=>StreamFingerprint(b.Streams[k])).SequenceEqual(s.StreamFingerprints));
        RequireState(s.NullStreams!=null && s.NullStreams.All(Text) && s.NullStreams.Distinct().Count()==s.NullStreams.Length &&
            s.NullStreams.All(s.CachedStreams.Contains) && s.CachedStreams.All(k=>(b.Streams[k]==null)==s.NullStreams.Contains(k)));
        RequireState(s.Moving!=null && s.Moving.Length<=100000 && s.Moving.All(k=>k!=null) &&
            s.Moving.Distinct().Count()==s.Moving.Length && b.Moving!=null && s.Moving.All(k=>b.Moving.TryGetValue(k,out var f)&&f!=null));
        RequireState(s.Repeats!=null && s.Repeats.Length<=100000 && s.Voices!=null && s.Voices.Length<=100000 && b.Events!=null);
        foreach(var t in s.Repeats) {
            RequireState(t!=null && t.Asset!=null && b.Events.ContainsKey(t.Asset));
            var a=b.Events[t.Asset]; var e=a?.Event;
            RequireState(e!=null && t.Fingerprint==EventFingerprint(e) && a.Catalogue!=null && t.Asset.Park is 1 or 2 &&
                ReferenceEquals(a.Catalogue,t.Asset.Park==1?b.Park1:b.Park2) &&
                t.Asset.Kind==(int)e.Group && t.Kind==t.Asset.Kind && t.Asset.Id==e.Id &&
                t.Set>=0 && t.Set<Math.Max(1,e.Sets) && t.ByClipLength==SfxEventMachine.IsGraph(e.Source) &&
                (e.Flags&RepeatFlag)!=0 && e.Word0C>0 && t.Interval==e.Word0C/1000.0 &&
                double.IsFinite(t.Due) && Text(t.Head));
            Vector(t.At);
        }
        var serials=new HashSet<int>();
        foreach(var v in s.Voices) {
            RequireState(v!=null && v.Serial>0 && v.Serial<=s.Serial && serials.Add(v.Serial) &&
                Text(v.Name)&&Text(v.Line)&&Text(v.PlayerName)&&Text(v.Bus)&&Text(v.Stream) &&
                s.CachedStreams.Contains(v.Stream) && b.Streams[v.Stream]!=null && v.Loop!=v.OneShot &&
                v.Frames>=0 && v.FirstAdvanceFrame>=-1 && v.FirstAdvanceFrame<=v.Frames &&
                double.IsFinite(v.Elapsed)&&v.Elapsed>=0 && float.IsFinite(v.MaxPosition)&&v.MaxPosition>=0 &&
                float.IsFinite(v.Db)&&float.IsFinite(v.Volume)&&float.IsFinite(v.Seek)&&v.Seek>=0);
            Vector(v.At);
            var wav=b.Streams[v.Stream];
            RequireState(v.Seek<=wav.GetLength()+0.05 && (wav.LoopMode!=AudioStreamWav.LoopModeEnum.Disabled)==v.Loop);
            // A stopped player's nonzero cursor cannot be restored without Play: fail closed.
            RequireState(v.Playing || v.Paused && v.HasPlayback || v.Seek==0);
        }
    }
    RideSounds(Snapshot s, SnapshotBindings b)
    {
        _park1=b.Park1; _park2=b.Park2; _rng=SnapshotRandom.FromState(s.Random);
        _graphRng.RestoreState(s.GraphRandom); _root=new Node3D {Name="RideSounds"};
        _serial=s.Serial; Cued=s.Counters[0];Resolved=s.Counters[1];Unresolved=s.Counters[2];
        StartedVoices=s.Counters[3];SilentVoices=s.Counters[4];Verdicts=s.Counters[5];Census.AddRange(s.Census);
        ParameterValue=b.ParameterValue;
        foreach(var k in s.CachedStreams) _streams.Add(k,b.Streams[k]);
        foreach(var k in s.Moving) _moving.Add((k.Ride,k.Tag),b.Moving[k]);
        foreach(var t in s.Repeats) _repeats.Add(new Repeater {Ride=t.Ride,Tag=t.Tag,Kind=t.Kind,Set=t.Set,
            ByClipLength=t.ByClipLength,At=Vector(t.At),IntervalSeconds=t.Interval,Due=t.Due,Head=t.Head,
            Catalogue=b.Events[t.Asset].Catalogue,Event=b.Events[t.Asset].Event});
        try {
            foreach(var v in s.Voices) {
                Node p=v.Positional
                    ? new AudioStreamPlayer3D {Name=v.PlayerName,Stream=_streams[v.Stream],Bus=v.Bus,VolumeDb=v.Volume,
                        Position=Vector(v.At),MaxDistance=MaxDistance,Autoplay=false}
                    : new AudioStreamPlayer {Name=v.PlayerName,Stream=_streams[v.Stream],Bus=v.Bus,VolumeDb=v.Volume,Autoplay=false};
                _root.AddChild(p);
                _voices.Add(new Voice {Ride=v.Ride,Tag=v.Tag,Serial=v.Serial,Name=v.Name,Line=v.Line,Player=p,
                    Loop=v.Loop,OneShot=v.OneShot,Frames=v.Frames,PlayingAt1=v.PlayingAt1,MaxPosition=v.MaxPosition,
                    FirstAdvanceFrame=v.FirstAdvanceFrame,Elapsed=v.Elapsed,Finished=v.Finished,Verdict=v.Verdict,Fading=v.Fading,Db=v.Db});
            }
        } catch { _root.Free(); throw; }
    }
    /// <summary>Only this handle is exposed before acceptance. It cannot Cue, Tick or Play.
    /// CommitAfterWorldAccepted attaches and starts actual players, exactly once. Dispose an
    /// abandoned stage. Caller must quiesce old world and install new providers before commit.</summary>
    public sealed class Staged : IDisposable
    {
        RideSounds _value;
        readonly (bool Playing,bool Paused,bool HasPlayback,float Seek)[] _playback;
        internal Staged(Snapshot s, SnapshotBindings b) {
            _playback=s.Voices.Select(v=>(v.Playing,v.Paused,v.HasPlayback,v.Seek)).ToArray();
            _value=new RideSounds(s,b);
        }
        public bool IsDetachedAndStopped => _value!=null && _value._root.GetParent()==null && _value._voices.All(v=>!IsPlaying(v.Player));
        public RideSounds CommitAfterWorldAccepted(Node3D newParent)
        {
            if(_value==null) throw new InvalidOperationException("Stage consumed");
            if(newParent==null || !newParent.IsInsideTree()) throw new InvalidOperationException("Accepted parent must be in tree");
            var value=_value;
            newParent.AddChild(value._root);
            for(int i=0;i<value._voices.Count;i++) {
                var v=value._voices[i]; var s=_playback[i];
                if(v.Player is AudioStreamPlayer3D p3) {
                    p3.Finished+=()=>v.Finished=true; p3.StreamPaused=s.Paused;
                    if(s.Playing || s.Paused && s.HasPlayback) p3.Play(s.Seek); p3.StreamPaused=s.Paused;
                } else if(v.Player is AudioStreamPlayer p2) {
                    p2.Finished+=()=>v.Finished=true; p2.StreamPaused=s.Paused;
                    if(s.Playing || s.Paused && s.HasPlayback) p2.Play(s.Seek); p2.StreamPaused=s.Paused;
                }
            }
            _value=null; return value;
        }
        public void Dispose() { _value?._root.Free(); _value=null; }
    }
    public static Staged FromState(Snapshot s, SnapshotBindings bindings)
    { Validate(s,bindings); return new Staged(s,bindings); }
}
