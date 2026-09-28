using Godot;
using TPW.PS2.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TPWPS2Viewer;

/// <summary>Independent disc-backed sound closure. Paths come only from trusted disc metadata,
/// never from the save. MAP/SDT are ISO members, not WAD entries in SaveAssetRegistry.
/// The disc identity uses exactly that registry's length/SHA256 convention. Keep this registry
/// alive for the lifetime of its restored catalogues (future cues lazily read the disc).</summary>
public sealed class SaveSoundRegistry : IDisposable
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record FileAsset(string Id, long Length, string Sha256);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Manifest(int Version, SaveAssetRegistry.DiscIdentity Disc, string World, FileAsset[] Files);
    readonly AssetLibrary library;
    readonly Dictionary<RideSounds.EventKey,RideSounds.EventBinding> events = new();
    readonly Dictionary<string,(SoundCatalogue Cat,SoundCatalogue.Resolved Event,SoundCatalogue.ResolvedClip Clip,bool Loop)> clips = new();
    readonly SoundCatalogue[] parks;
    readonly Manifest manifest;
    public string AssetsId { get; }
    public SaveAssetRegistry.DiscIdentity Disc => manifest.Disc;
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static void Require(bool ok,string why) { if(!ok) throw new InvalidDataException("Sound assets: "+why); }
    public static SaveSoundRegistry Capture(string trustedDiscPath,string trustedWorld) => new(trustedDiscPath,trustedWorld,null);
    public static SaveSoundRegistry Open(string trustedDiscPath,string trustedWorld,Manifest saved)
    { ArgumentNullException.ThrowIfNull(saved);return new(trustedDiscPath,trustedWorld,saved); }
    SaveSoundRegistry(string path,string world,Manifest saved)
    {
        Require(world!=null && world.Length is >0 and <32 && world.All(c=>c is >= 'A' and <= 'Z' or >= '0' and <= '9'),"trusted world");
        SaveAssetRegistry.DiscIdentity identity;
        using(var f=File.OpenRead(path)) identity=new(f.Length,Convert.ToHexString(SHA256.HashData(f)));
        library=new AssetLibrary(path);
        try {
            // Complete audio closure, including currently unused banks, for future cues. Absence is
            // covered by the full disc digest and the exact inventory, not a saved failure claim.
            var files=library.Disc.Files().Where(f=>!f.IsDirectory && f.Path.StartsWith("/AUDIO/",StringComparison.OrdinalIgnoreCase)
                && (f.Path.EndsWith(".MAP",StringComparison.OrdinalIgnoreCase)||f.Path.EndsWith(".SDT",StringComparison.OrdinalIgnoreCase)))
                .OrderBy(f=>f.Path,StringComparer.Ordinal).ToArray();
            var assets=files.Select(f=>new FileAsset("sound-file/v1/"+Hash(Encoding.UTF8.GetBytes(f.Path)),f.Size,
                Hash(library.Disc.Read(f.Extent,f.Size)))).ToArray();
            manifest=new(1,identity,world,assets);
            Require(saved==null || JsonSerializer.Serialize(saved)==JsonSerializer.Serialize(manifest),"disc/world/file ID/hash closure mismatch");
            AssetsId="sound-assets/v1/"+Hash(JsonSerializer.SerializeToUtf8Bytes(manifest));
            parks=new[]{new SoundCatalogue(library.Disc,world,1),new SoundCatalogue(library.Disc,world,2)};
            for(int p=1;p<=2;p++) foreach(var group in Enum.GetValues<SoundGroup>()) {
                var map=SoundCatalogue.MapFor(group,world,p);
                var file=files.SingleOrDefault(f=>string.Equals(f.Path,map,StringComparison.OrdinalIgnoreCase));
                if(file==null)continue;
                var parsed=new SfxMap(library.Disc.Read(file.Extent,file.Size));
                foreach(int id in parsed.ById.Keys) {
                    // Asset parsing only: no game resolver, Cue, Start, provider, random draw or Play.
                    var e=parks[p-1].Resolve(group,id);
                    events.Add(new(p,(int)group,id),new(parks[p-1],e));
                    foreach(var c in e.Clips) foreach(bool loop in new[]{false,true})
                        clips.TryAdd($"{e.Map}|{c.Bank}|{c.Index}|{loop}",(parks[p-1],e,c,loop));
                }
            }
        } catch {library.Dispose();throw;}
    }
    public Manifest Export() => manifest with {Files=manifest.Files.ToArray()};
    public void WriteManifest(Stream destination) => JsonSerializer.Serialize(destination,manifest);
    public static Manifest ReadManifest(Stream source)
    {
        using var b=new MemoryStream();var chunk=new byte[8192];int n;
        while((n=source.Read(chunk))>0) {Require(b.Length+n<=4*1024*1024,"manifest bound");b.Write(chunk,0,n);}
        return JsonSerializer.Deserialize<Manifest>(b.ToArray(),new JsonSerializerOptions{MaxDepth=16})
            ?? throw new InvalidDataException("Null sound manifest");
    }
    public RideSounds.SnapshotBindings Bind(RideSounds.Snapshot state,string servicesId,
        IReadOnlyDictionary<RideSounds.OwnerKey,Func<Vector3?>> newMoving,Func<int,int,int> newParameter)
    {
        Require(state!=null && state.AssetsId==AssetsId && state.ServicesId==servicesId,"owner identity");
        Require(state.CachedStreams!=null && state.CachedStreams.Length<=100000,"stream bound");
        var streams=new Dictionary<string,AudioStreamWav>();
        foreach(string key in state.CachedStreams) {
            Require(key!=null && clips.ContainsKey(key),"unverified stream ID");
            var c=clips[key]; streams.Add(key,RideSounds.DecodeVerifiedClip(c.Cat,c.Event,c.Clip,c.Loop));
        }
        return new() {AssetsId=AssetsId,ServicesId=servicesId,Park1=state.Park1Present?parks[0]:null,
            Park2=state.Park2Present?parks[1]:null,Events=events,Streams=streams,Moving=newMoving,ParameterValue=newParameter};
    }
    public void Dispose()=>library.Dispose();
}

public sealed partial class RideSounds
{
    // Reuse the actual Stream helper, including its null failure and loop semantics. This
    // disposable decoder shell is detached and never owns a player or evaluates gameplay.
    internal static AudioStreamWav DecodeVerifiedClip(SoundCatalogue cat,SoundCatalogue.Resolved e,SoundCatalogue.ResolvedClip c,bool loop)
    {
        var parent=new Node3D();
        var decoder=new RideSounds(parent,cat,null);
        try { return decoder.Stream(cat,e,c,loop); }
        finally { parent.Free(); }
    }
    internal static RideSounds ColdShell(Snapshot s,SnapshotBindings b) { Validate(s,b);return new RideSounds(s,b); }
    internal void DiscardColdShell()=>_root.Free();
    internal void PublishColdShell(Node3D parent,Snapshot s)
    {
        if(_root.GetParent()!=null || !parent.IsInsideTree())throw new InvalidOperationException("Sound shell publication order");
        parent.AddChild(_root);
        for(int i=0;i<_voices.Count;i++) {
            var v=_voices[i];var saved=s.Voices[i];
            if(v.Player is AudioStreamPlayer3D p) {
                p.Finished+=()=>v.Finished=true;
                p.StreamPaused=saved.Paused;
                if(saved.Playing || saved.Paused&&saved.HasPlayback)p.Play(saved.Seek);
                p.StreamPaused=saved.Paused;
            } else if(v.Player is AudioStreamPlayer q) {
                q.Finished+=()=>v.Finished=true;
                q.StreamPaused=saved.Paused;
                if(saved.Playing || saved.Paused&&saved.HasPlayback)q.Play(saved.Seek);
                q.StreamPaused=saved.Paused;
            }
        }
    }
}
