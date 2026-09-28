using Godot;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TPWPS2Viewer;

public partial class Viewer
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record SoundProviderState(RideSounds.OwnerKey Owner,string RegistryId);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record SoundState(int Version,string OwnerId,RideSounds.Snapshot Physical,
        SoundProviderState[] Moving,string ParameterRegistryId,bool BusChained);
    /// <summary>Trusted application registry, rebuilt against NEW-world owners, never deserialized
    /// delegates. Values are the new owners' restored fields. Parameter must be the BASE chain,
    /// excluding bus: CompleteBusStateJoin installs that wrapper exactly once.</summary>
    public sealed class SoundServices
    {
        public required string Id {get;init;}
        public required IReadOnlyDictionary<string,Func<Vector3?>> Moving {get;init;}
        public required IReadOnlyDictionary<string,Func<int,int,int>> Parameters {get;init;}
    }
    public RideSounds SoundOwnerForSave => _sounds;
    public string[] UnsupportedPhysicalSoundFields()
    {
        var result=new List<string>();
        if(_toolSfx!=null)result.Add("_toolSfx (ToolSounds streams, players, cache, cursor)");
        if(_player!=null)result.Add("_player (sound-browser/UI/music stream, playback, cursor)");
        if(_advisorVoice!=null)result.Add("_advisorVoice (requires separate AdvisorState/AdvisorVoiceState verified stream join)");
        if(_coasterChainedOn!=null)result.Add("_coasterChainedOn (coaster parameter wrapper and moving-car owner join)");
        return result.ToArray();
    }
    void SoundInventoryGuard()
    {
        var unsupported=UnsupportedPhysicalSoundFields();
        if(unsupported.Length!=0)throw new NotSupportedException("Physical audio is not total: "+string.Join("; ",unsupported));
    }
    static void SoundRequire(bool ok,string why) {if(!ok)throw new InvalidDataException("Viewer sound: "+why);}
    public SoundState CaptureSoundState(SaveSoundRegistry assets,string ownerId,SoundServices services,
        SoundProviderState[] moving,string parameterRegistryId)
    {
        SoundInventoryGuard();
        SoundRequire(!string.IsNullOrWhiteSpace(ownerId)&&services!=null,"owner/services IDs");
        SoundRequire(_busChainedOn==null || ReferenceEquals(_busChainedOn,_sounds),"stale bus chain");
        var physical=_sounds?.CaptureState(assets.AssetsId,services.Id);
        var state=new SoundState(1,ownerId,physical,moving,parameterRegistryId,_busChainedOn!=null);
        var bindings=SoundBindings(state,assets,services);
        if(physical!=null) {
            // Compare actual retained delegates with the registered source delegates; never serialize them.
            var source=_sounds.CaptureBindings(assets.AssetsId,services.Id);
            SoundRequire(source.Moving.All(p=>bindings.Moving.TryGetValue(p.Key,out var f)&&f==p.Value),"unregistered moving closure");
            SoundRequire(state.BusChained || source.ParameterValue==bindings.ParameterValue,"unregistered base parameter closure");
            using var validation=RideSounds.FromState(physical,bindings); // independently decoded disc assets, NOT source bindings
        }
        return state;
    }
    static RideSounds.SnapshotBindings SoundBindings(SoundState s,SaveSoundRegistry assets,SoundServices services)
    {
        SoundRequire(s!=null&&s.Version==1&&s.Moving!=null&&s.Moving.Length<=100000&&services!=null,"version/services/bounds");
        if(s.Physical==null) {SoundRequire(s.Moving.Length==0&&s.ParameterRegistryId==null&&!s.BusChained,"absent owner");return null;}
        var moving=new Dictionary<RideSounds.OwnerKey,Func<Vector3?>>();
        foreach(var p in s.Moving) {
            SoundRequire(p!=null&&p.Owner!=null&&p.RegistryId!=null&&services.Moving.ContainsKey(p.RegistryId),"moving registry ID");
            moving.Add(p.Owner,services.Moving[p.RegistryId]);
        }
        SoundRequire(s.Physical.Moving!=null&&moving.Keys.ToHashSet().SetEquals(s.Physical.Moving),"exact moving inventory");
        Func<int,int,int> parameter=null;
        if(s.ParameterRegistryId!=null) {SoundRequire(services.Parameters.ContainsKey(s.ParameterRegistryId),"parameter registry ID");parameter=services.Parameters[s.ParameterRegistryId];}
        SoundRequire(s.Physical.HasParameter==(parameter!=null),"parameter presence");
        return assets.Bind(s.Physical,services.Id,moving,parameter);
    }
    /// <summary>Call on an unpublished Viewer shell; do not run its normal _Ready. Stage bus first
    /// using handle.Shell and handle.BaseParameter, then CompleteBusStateJoin, then Join. Publication
    /// is explicitly after world acceptance. No AudioServer operations occur here.</summary>
    public SoundWorldStage StageSoundState(SoundState saved,SaveSoundRegistry assets,SoundServices newServices)
    {
        SoundRequire(!IsInsideTree()&&GetParent()==null&&_sounds==null,"fresh unpublished Viewer");SoundInventoryGuard();
        // Freeze mutable DTO arrays before the coordinator receives a handle.
        var s=JsonSerializer.Deserialize<SoundState>(JsonSerializer.Serialize(saved));
        var bindings=SoundBindings(s,assets,newServices);
        return new SoundWorldStage(this,s,bindings);
    }
    public sealed class SoundWorldStage : IDisposable
    {
        Viewer owner;readonly SoundState state;bool joined,committed;
        public RideSounds Shell {get;private set;}
        public Func<int,int,int> BaseParameter {get;}
        public string OwnerId=>state.OwnerId;
        internal SoundWorldStage(Viewer owner,SoundState state,RideSounds.SnapshotBindings bindings)
        {this.owner=owner;this.state=state;BaseParameter=bindings?.ParameterValue;Shell=state.Physical==null?null:RideSounds.ColdShell(state.Physical,bindings);}
        public void Join()
        {
            SoundRequire(owner!=null&&!joined&&!owner.IsInsideTree(),"join order");
            if(owner._stagedBusState!=null)SoundRequire(owner._stagedBusState.SoundOwnerId==state.OwnerId
                &&owner._stagedBusState.ParameterChained==state.BusChained
                &&owner._stagedBusBindings.NewBaseParameter==BaseParameter,"bus identity/base provider mismatch");
            if(state.BusChained)SoundRequire(owner._busStateHooksInstalled&&ReferenceEquals(owner._sounds,Shell)&&ReferenceEquals(owner._busChainedOn,Shell),"CompleteBusStateJoin required");
            else SoundRequire(owner._busChainedOn==null&&(owner._sounds==null||ReferenceEquals(owner._sounds,Shell)),"conflicting sound/bus owner");
            owner._sounds=Shell;joined=true;
        }
        public void CommitAfterWorldAccepted()
        {
            SoundRequire(owner!=null&&joined&&!committed&&owner.IsInsideTree()&&ReferenceEquals(owner._sounds,Shell),"commit order");
            committed=true;Shell?.PublishColdShell(owner,state.Physical);
        }
        public void Dispose()
        {
            if(owner==null)return;
            if(!committed) {if(ReferenceEquals(owner._sounds,Shell))owner._sounds=null;
                if(ReferenceEquals(owner._busChainedOn,Shell))owner._busChainedOn=null;
                Shell?.DiscardColdShell();}
            owner=null;Shell=null;
        }
    }
}
