using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

public partial class Viewer
{
    // Physical voices/event machines are NOT recreated by this adapter. Default policy
    // refuses any sound-layer dependency. ExternalPhysicalRegistry is an explicit join
    // contract: the coordinator must capture/restore the entire RideSounds owner first.
    public enum BusAudioJoinPolicy { RejectSoundOwner, ExternalPhysicalRegistry }
    public sealed class BusStateBindings
    {
        public string CatalogueAssetId { get; init; }
        public NativeBusCatalogue Catalogue { get; init; }
        public string ModelAssetId { get; init; }
        public Model Model { get; init; }
        public string AnimationAssetId { get; init; }
        public Aps Animation { get; init; }
        public Func<string,(ImageTexture Tex,bool Soft)> Texture { get; init; }
        public Func<RideDefinition,string> IdentifyDefinition { get; init; }
        public Func<string,RideDefinition> ResolveDefinition { get; init; }
        public BusAudioJoinPolicy AudioPolicy { get; init; }
        // Capture: exact registered physical owner ID. Restore: NEW fully hydrated owner.
        public string SoundOwnerId { get; init; }
        public RideSounds Sounds { get; init; }
        // Required trusted NEW base provider, deliberately never read from ParameterValue:
        // that property may contain the old Viewer's chain. Must include all non-bus hooks.
        public Func<int,int,int> NewBaseParameter { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record BusPlacementState(string NodeId,int RuntimeId,string RideId,string DefinitionAssetId);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record BusState
    {
        public required int Version { get; init; }
        public required NativeBus.Snapshot Bus { get; init; }
        public required string MeshAssetId { get; init; }
        public required string MeshFingerprint { get; init; }
        public required string CatalogueAssetId { get; init; }
        public required string CatalogueFingerprint { get; init; }
        public required BusPlacementState[] Placements { get; init; }
        public required string SoundOwnerId { get; init; }
        public required bool AudioLive { get; init; }
        public required bool ParameterChained { get; init; }
        // Scalars themselves belong to RuntimeState, restored BEFORE this join. This
        // redundant cut witness prevents mixing bus and runtime snapshots from two frames.
        public required string RuntimeWitness { get; init; }
    }
    const string BusServicesId = "viewer/native-bus/services";
    BusState _stagedBusState;
    BusStateBindings _stagedBusBindings;
    bool _busStateHooksInstalled;
    static void BS(bool ok,string why) { if(!ok) throw new InvalidDataException("WORLD bus: "+why); }
    static string BusHash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    static string CatalogueHash(NativeBusCatalogue c)=>c==null?null:BusHash(JsonSerializer.SerializeToUtf8Bytes(new {
        c.Selection,c.Point0,c.StagingPoint,c.IncomingQueuePoint,c.ImageInitialActivationCounter,
        c.DemandOffset,c.DemandDivisor,Keys=Enumerable.Range(1,8).Select(c.Keys).ToArray() }));
    string BusRuntimeWitness()=>JsonSerializer.Serialize(new { _busElapsedMs,_busClockAdvancedForFrame,_busTraffic,
        Parameter=BusParameter20,_busParameterStamp,_busParameterPendingReset,_busBatches,_busAdmitted,
        _busLoadFailed,_busSourceKey,_nativeLoadsOfKids });
    static bool BusAssetId(string id)=>!string.IsNullOrWhiteSpace(id)&&id.Length<=1024;

    /// <summary>Register actual Park node identities after Park allocation on BOTH sides.
    /// Detached/stale retained nodes must instead be explicitly registered by the scene join.
    /// This never calls BusObjects (which prunes state) or RegisterBusPlacement (which activates).</summary>
    public void RegisterBusPlacementNodes(WorldCoreRegistry r)
    {
        BS(ReferenceEquals(r.Owner,this),"registry owner");
        if(r.Park==null)return;
        foreach(var p in r.Park.Placed)
            if(p.Node!=null)r.Register("park/placement-node/"+p.Id,p.Node);
    }
    public BusState CaptureBusState(WorldCoreRegistry r,BusStateBindings b)
    {
        ArgumentNullException.ThrowIfNull(b);
        BS(ReferenceEquals(r.Owner,this),"registry owner");
        BS(_busPlacements.Count<=100000,"placement bound");
        BS(_busChainedOn==null || ReferenceEquals(_busChainedOn,_sounds),"stale chained sound owner needs a separate owner join");
        BS(!_busAudioLive || _sounds!=null,"live audio without physical owner");
        if(_sounds!=null) {
            BS(b.AudioPolicy==BusAudioJoinPolicy.ExternalPhysicalRegistry && ReferenceEquals(b.Sounds,_sounds)
                && BusAssetId(b.SoundOwnerId),"physical RideSounds registry absent; refusing silent audio reset");
        }
        BS(_busCatalogue==null || ReferenceEquals(_busCatalogue,b.Catalogue)&&BusAssetId(b.CatalogueAssetId),"verified catalogue asset");
        BS(_nativeBusMesh==null || ReferenceEquals(_nativeBusMesh,b.Model)&&BusAssetId(b.ModelAssetId),"verified mesh asset");
        BS(_nativeBus==null || _nativeBusMesh!=null,"bus mesh owner");
        var bus=_nativeBus?.CaptureState(b.ModelAssetId,b.AnimationAssetId,BusServicesId);
        BS(bus==null || bus.ModelFingerprint==BusHash(_nativeBusMesh.D),"adapter/mesh mismatch");
        var placements=_busPlacements.Select(p=> {
            BS(p.Node==null || IsInstanceValid(p.Node)&&!p.Node.IsQueuedForDeletion(),"invalid retained node");
            string definition=p.Definition==null?null:b.IdentifyDefinition?.Invoke(p.Definition);
            BS(p.Definition==null || BusAssetId(definition)&&ReferenceEquals(b.ResolveDefinition?.Invoke(definition),p.Definition),"verified definition asset");
            var ride=r.Sim?.Rides.FirstOrDefault(x=>x.Id==p.RuntimeId);
            return new BusPlacementState(r.Id(p.Node),p.RuntimeId,r.Id(ride),definition);
        }).ToArray();
        return new() {Version=1,Bus=bus,MeshAssetId=_nativeBusMesh==null?null:b.ModelAssetId,
            MeshFingerprint=_nativeBusMesh==null?null:BusHash(_nativeBusMesh.D),
            CatalogueAssetId=_busCatalogue==null?null:b.CatalogueAssetId,CatalogueFingerprint=CatalogueHash(_busCatalogue),
            Placements=placements,SoundOwnerId=_sounds==null?null:b.SoundOwnerId,AudioLive=_busAudioLive,
            ParameterChained=_busChainedOn!=null,RuntimeWitness=BusRuntimeWitness()};
    }

    /// <summary>Early PURE binding stage: call after Runtime restore and before entrance
    /// service allocation. No scene publication, commands, ticks, RNG, Bind or Present.</summary>
    public void StageBusState(BusState s,WorldCoreRegistry r,BusStateBindings b)
    {
        ArgumentNullException.ThrowIfNull(s);ArgumentNullException.ThrowIfNull(b);
        BS(!IsInsideTree()&&GetParent()==null&&ReferenceEquals(r.Owner,this),"unpublished target registry required");
        BS(_stagedBusState==null&&_nativeBus==null&&_nativeBusMesh==null&&_busCatalogue==null
            &&_busPlacements.Count==0&&_sounds==null&&_busChainedOn==null&&!_busAudioLive,"fresh bus target required");
        BS(s.Version==1&&s.Placements!=null&&s.Placements.Length<=100000&&s.RuntimeWitness==BusRuntimeWitness(),"version/bounds/runtime cut");
        BS((s.CatalogueAssetId==null)==(s.CatalogueFingerprint==null)
            &&(s.MeshAssetId==null)==(s.MeshFingerprint==null),"asset nullability");
        BS(s.CatalogueAssetId==null || BusAssetId(s.CatalogueAssetId)&&s.CatalogueAssetId==b.CatalogueAssetId
            &&b.Catalogue!=null&&s.CatalogueFingerprint==CatalogueHash(b.Catalogue),"catalogue identity/content");
        BS(s.MeshAssetId==null || BusAssetId(s.MeshAssetId)&&s.MeshAssetId==b.ModelAssetId
            &&b.Model!=null&&s.MeshFingerprint==BusHash(b.Model.D),"mesh identity/content");
        BS(s.Bus==null || s.MeshAssetId==s.Bus.ModelAssetId&&s.MeshFingerprint==s.Bus.ModelFingerprint,"bus mesh join");
        BS(s.SoundOwnerId!=null || !s.AudioLive&&!s.ParameterChained,"audio null owner");
        BS(s.SoundOwnerId==null || b.AudioPolicy==BusAudioJoinPolicy.ExternalPhysicalRegistry
            &&BusAssetId(s.SoundOwnerId)&&s.SoundOwnerId==b.SoundOwnerId&&b.Sounds!=null&&b.NewBaseParameter!=null,
            "explicit hydrated physical sound owner and trusted NEW base parameter provider required");
        NativeBus bus=s.Bus==null?null:NativeBus.FromState(s.Bus,new() {
            ModelAssetId=b.ModelAssetId,AnimationAssetId=b.AnimationAssetId,ServicesId=BusServicesId,
            Model=b.Model,Animation=b.Animation,Texture=b.Texture,StateCommand=NativeBusState,ArrivalBatch=AdmitBusBatch });
        _nativeBus=bus;_nativeBusMesh=s.MeshAssetId==null?null:b.Model;
        _busCatalogue=s.CatalogueAssetId==null?null:b.Catalogue;
        _stagedBusState=s;_stagedBusBindings=b;
    }

    /// <summary>Late pure join after Park/nodes/rides and physical audio hydrate. No Cue/Kill
    /// or parameter evaluation. Returns detached bus root; caller publishes only after every
    /// world join succeeds. Do NOT run Viewer._Ready on a loaded world.</summary>
    public Node3D CompleteBusStateJoin(WorldCoreRegistry r)
    {
        BS(!IsInsideTree()&&GetParent()==null&&ReferenceEquals(r.Owner,this),"unpublished target registry");
        BS(_stagedBusState!=null&&!_busStateHooksInstalled,"stage required / already joined");
        var s=_stagedBusState;var b=_stagedBusBindings;
        var placements=s.Placements.Select(p=> {
            BS(p!=null,"null placement");
            var node=p.NodeId==null?null:r.Resolve<Node3D>(p.NodeId);
            BS(node==null || IsInstanceValid(node)&&!node.IsInsideTree()&&!node.IsQueuedForDeletion(),"fresh placement node");
            var ride=p.RideId==null?null:r.Resolve<ParkRide>(p.RideId);
            BS(ReferenceEquals(ride,r.Sim?.Rides.FirstOrDefault(x=>x.Id==p.RuntimeId)),"runtime ride identity");
            var definition=p.DefinitionAssetId==null?null:b.ResolveDefinition?.Invoke(p.DefinitionAssetId);
            BS(p.DefinitionAssetId==null || BusAssetId(p.DefinitionAssetId)&&definition!=null
                &&b.IdentifyDefinition?.Invoke(definition)==p.DefinitionAssetId,"definition asset resolution");
            return (node,p.RuntimeId,definition);
        }).ToArray();
        _busPlacements.AddRange(placements);
        _sounds=s.SoundOwnerId==null?null:b.Sounds;
        _busAudioLive=s.AudioLive;
        if(s.ParameterChained) {
            var previous=b.NewBaseParameter;
            _sounds.ParameterValue=(ride,parameter)=>ride==BusSoundOwner&&parameter==BusParameterId
                ?BusParameter20:previous(ride,parameter);
            _busChainedOn=_sounds;
        }
        if(s.AudioLive)_sounds.Follow(BusSoundOwner,BusSoundTag,BusSoundPosition);
        _busStateHooksInstalled=true;
        return _nativeBus?.Root;
    }
}
