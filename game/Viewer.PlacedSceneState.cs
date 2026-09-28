using Godot;
using TPW.PS2.Data;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

public partial class Viewer
{
    // This revision calls the actual collections _scripted and _tracks, not _rides/_trackRides.
    // A SCENE cut only: audio/effects, tools, terrain, guests and publication belong to other joins.
    // In particular never add a staged Viewer to SceneTree: _Ready would create a second world.
    public sealed class PlacedSceneBindings
    {
        // Trusted application registry adapters (e.g. SaveAssetRegistry). No paths from the save
        // are opened here. Resolve must construct NEW-world texture/UV delegates, never old nodes.
        public required Func<AnimatedModel, AnimatedModel.SnapshotBindings> IdentifyRenderer { get; init; }
        public required Func<AnimatedModel.Snapshot, AnimatedModel.SnapshotBindings> ResolveRenderer { get; init; }
        public required Func<Model, string> IdentifyMesh { get; init; }
        public required Func<string, Model> ResolveMesh { get; init; }
        public required Func<Aps, string> IdentifyAnimation { get; init; }
        public required Func<string, Aps> ResolveAnimation { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PlacedNodeState(int Id, string Name, float[] Transform, bool Visible,
        bool TopLevel, int[] Children, AnimatedModel.Snapshot Renderer, long? ActivationSerial = null);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PlacedRootState(int PlacedId, int Node);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PlacedScriptState(string Ride, int RuntimeId, int Model, string Animation, int Slot, int Variant);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PlacedBuildState(int Model, float Frame);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PlacedMeshState(int RuntimeId, string Asset, string Fingerprint);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PlacedSceneState(int Version, PlacedNodeState[] Nodes, PlacedRootState[] Placements,
        PlacedScriptState[] Scripted, PlacedBuildState[] Building, PlacedMeshState[] Meshes, int Current);
    static void PS(bool ok, string why) { if (!ok) throw new InvalidDataException("Viewer placed SCENE: " + why); }
    static string PSHash(Model m) => Convert.ToHexString(SHA256.HashData(m.D));
    static float[] PSPack(Transform3D t) => new[] { t.Basis.X.X,t.Basis.X.Y,t.Basis.X.Z,t.Basis.Y.X,t.Basis.Y.Y,t.Basis.Y.Z,
        t.Basis.Z.X,t.Basis.Z.Y,t.Basis.Z.Z,t.Origin.X,t.Origin.Y,t.Origin.Z };
    static Transform3D PSUnpack(float[] v) {
        PS(v != null && v.Length == 12 && v.All(float.IsFinite), "transform");
        return new(new Basis(new(v[0],v[1],v[2]),new(v[3],v[4],v[5]),new(v[6],v[7],v[8])),new(v[9],v[10],v[11]));
    }
    void PSUnsupported()
    {
        // Fail closed rather than reconstruct stale emitted geometry from the current layout.
        // TrackModel loses non-Flowing AnimatedModel owners; CoasterPylon loses ALL owners.
        PS(_tracks.Count == 0, "track views require retained piece renderer owners (including non-Flowing pieces), car interpolation and ordered Frame topology join");
        PS(_coasters.Count == 0, "coaster views require retained pylon AnimatedModel owners, segment CPU/material state, train/car interpolation and Frame topology join");
        PS(_vehicles.Count == 0, "seaplane/ferry RseModelPresenter retained renderer accessor/state join missing");
        PS(_trackTool == null && _coasterTool == null && _afterTrack == null && _afterCoaster == null, "active track/coaster tool callback");
    }
    /// <summary>Captures real retained renderer objects and holder children, not simulation DTOs.
    /// Unknown/static MeshInstance subtrees are refused: their retained inputs are not provably
    /// immutable. Completed unscripted models whose AnimatedModel owner was discarded are refused.
    /// NativeBus has a separate BusState renderer join; it is NOT included in this cut.</summary>
    public PlacedSceneState CapturePlacedScene(WorldCoreRegistry r, PlacedSceneBindings b)
    {
        ArgumentNullException.ThrowIfNull(b);
        PS(ReferenceEquals(r.Owner,this) && ReferenceEquals(r.Park,_park), "capture registry/park");
        PSUnsupported();
        var known = new Dictionary<Node3D,AnimatedModel>();
        void Know(AnimatedModel m) { if(m != null) { PS(m.Root != null,"renderer root"); known[m.Root]=m; } }
        foreach(var p in _scripted) Know(p.Model);
        foreach(var p in _building) Know(p.Model);
        Know(_current);
        var nodes = new List<PlacedNodeState>(); var ids = new Dictionary<Node3D,int>();
        int Visit(Node3D n) {
            PS(IsInstanceValid(n) && !n.IsQueuedForDeletion(),"invalid node");
            PS(!ids.ContainsKey(n),"shared/cyclic holder topology");
            PS(nodes.Count < 100000,"node budget");
            int id=nodes.Count; ids.Add(n,id); nodes.Add(null);
            AnimatedModel.Snapshot model=null; int[] children;
            if(known.TryGetValue(n,out var m)) {
                PS(n.GetChildren().SequenceEqual(m.Surfaces().Select(x=>(Node)x.Node)),"renderer surface child order changed");
                var a=b.IdentifyRenderer(m); PS(a != null,"unregistered renderer");
                model=m.CaptureState(a.ModelAssetId,a.AnimationAssetId,a.UvRewriteId);
                PS(model.ModelFingerprint==PSHash(a.Model),"renderer asset mismatch");
                children=Array.Empty<int>(); // AnimatedModel owns its complete surface subtree.
            } else {
                PS(n.GetType()==typeof(Node3D) && n.GetMetaList().All(k=>k.ToString()=="represented_activation_serial"),"unknown/static/metadata-bearing subtree: "+n.Name);
                children=n.GetChildren().Select(c=> { PS(c is Node3D,"non-spatial child"); return Visit((Node3D)c); }).ToArray();
            }
            long? activation=null;
            if(n.HasMeta("represented_activation_serial")) {
                var value=n.GetMeta("represented_activation_serial");
                PS(value.VariantType==Variant.Type.Int,"activation serial type");activation=(long)value;
                PS(activation>=0&&activation<=uint.MaxValue,"activation serial bound");
            }
            nodes[id]=new(id,n.Name.ToString().StartsWith("@",StringComparison.Ordinal)?"":n.Name.ToString(),PSPack(n.Transform),n.Visible,n.TopLevel,children,model,activation);
            return id;
        }
        var placedNodes=_park.Placed.Where(p=>p.Node!=null).Select(p=>p.Node).ToArray();
        PS(placedNodes.Length==0 || placedNodes.All(n=>n.GetParent()==placedNodes[0].GetParent()) && placedNodes[0].GetParent()!=null && placedNodes[0].GetParent().GetChildren().SequenceEqual(placedNodes.Cast<Node>()),"Park placement root order/extra children requires Park owner topology accessor");
        var placements=_park.Placed.Where(p=>p.Node!=null).Select(p=>new PlacedRootState(p.Id,Visit(p.Node))).ToArray();
        int ModelId(AnimatedModel m) { PS(m!=null && ids.ContainsKey(m.Root),"renderer not owned by a Park.Placed holder (standalone _current requires preview join)"); return ids[m.Root]; }
        var scripted=_scripted.Select(p=> {
            PS(p.Anim==null || b.ResolveAnimation(b.IdentifyAnimation(p.Anim))==p.Anim,"animation identity");
            return new PlacedScriptState(r.Id(p.Ride),p.Ride.Id,ModelId(p.Model),p.Anim==null?null:b.IdentifyAnimation(p.Anim),p.Slot,p.Variant);
        }).ToArray();
        var building=_building.Select(p=>new PlacedBuildState(ModelId(p.Model),p.Frame)).ToArray();
        var meshes=_rideMeshes.Select(p=> { string id=b.IdentifyMesh(p.Value); PS(PSHash(b.ResolveMesh(id))==PSHash(p.Value),"mesh asset mismatch"); return new PlacedMeshState(p.Key,id,PSHash(p.Value)); }).ToArray();
        return new(1,nodes.ToArray(),placements,scripted,building,meshes,_current==null?-1:ModelId(_current));
    }

    /// <summary>Allocate BEFORE Park.FromState. Pass TakePlacement to Park.StateBindings.BuildNode
    /// via the application's exact placement-key table (terrain goes to its own factory).
    /// No placeholders necessary: these are already complete detached holder subtrees.
    /// Park may overwrite root transforms, so Complete verifies that both cuts agree.
    /// Dispose an abandoned stage BEFORE disposing its Park. After Complete Park owns all nodes.
    /// The caller must discard the entire staged world on any failure, never publish a partial join.</summary>
    public sealed class StagedPlacedScene : IDisposable
    {
        internal readonly PlacedSceneState State;
        internal readonly Dictionary<int,Node3D> Nodes=new();
        internal readonly Dictionary<int,AnimatedModel> Models=new();
        internal readonly Dictionary<int,Node3D> Roots=new();
        readonly HashSet<int> taken=new();
        internal bool Joined;
        internal StagedPlacedScene(PlacedSceneState s) { State=s; }
        public Node3D TakePlacement(int placedId) {
            PS(!Joined && Roots.ContainsKey(placedId) && taken.Add(placedId),"placement factory key reused/unknown");
            return Roots[placedId];
        }
        public void Dispose() {
            if(Joined)return;
            // Detach from a staged Park on failure: that Park must also be discarded.
            foreach(var n in Nodes.Values.Where(n=>IsInstanceValid(n)).ToArray())
                if(IsInstanceValid(n) && (n.GetParent()==null || !Nodes.Values.Contains(n.GetParent()))) {
                    n.GetParent()?.RemoveChild(n); n.Free();
                }
            Nodes.Clear(); Models.Clear(); Roots.Clear();
        }
    }
    public static StagedPlacedScene StagePlacedScene(PlacedSceneState s, PlacedSceneBindings b)
    {
        ArgumentNullException.ThrowIfNull(s); ArgumentNullException.ThrowIfNull(b);
        PS(s.Version==1 && s.Nodes!=null && s.Nodes.Length<=100000 && s.Placements!=null && s.Scripted!=null && s.Building!=null && s.Meshes!=null,"envelope");
        var stage=new StagedPlacedScene(s);
        try {
            var parent=new int[s.Nodes.Length]; Array.Fill(parent,-1);
            for(int i=0;i<s.Nodes.Length;i++) {
                var n=s.Nodes[i]; PS(n!=null && n.Id==i && n.Children!=null && n.Name!=null,"node index/name");
                PSUnpack(n.Transform);
                PS(n.Renderer==null || n.Children.Length==0,"renderer subtree ownership");
                foreach(int c in n.Children) { PS(c>i && c<s.Nodes.Length && parent[c]==-1,"ordered acyclic unique topology"); parent[c]=i; }
            }
            var rootIds=s.Placements.Select(p=>p.Node).ToHashSet();
            PS(rootIds.Count==s.Placements.Length && s.Placements.Select(p=>p.PlacedId).Distinct().Count()==s.Placements.Length,"duplicate placement");
            PS(rootIds.SetEquals(Enumerable.Range(0,parent.Length).Where(i=>parent[i]==-1)),"unowned root");
            foreach(var n in s.Nodes) {
                Node3D node;
                if(n.Renderer!=null) {
                    var m=AnimatedModel.FromState(n.Renderer,b.ResolveRenderer(n.Renderer));
                    stage.Models.Add(n.Id,m); node=m.Root;
                } else node=new Node3D();
                stage.Nodes.Add(n.Id,node);
                PS(n.ActivationSerial==null||n.ActivationSerial>=0&&n.ActivationSerial<=uint.MaxValue,"activation serial bound");
                if(n.ActivationSerial is long serial)node.SetMeta("represented_activation_serial",serial);
                if(n.Name.Length>0)node.Name=n.Name;node.Transform=PSUnpack(n.Transform);node.Visible=n.Visible;node.TopLevel=n.TopLevel;
            }
            foreach(var n in s.Nodes) foreach(int c in n.Children) stage.Nodes[n.Id].AddChild(stage.Nodes[c]);
            foreach(var p in s.Placements) stage.Roots.Add(p.PlacedId,stage.Nodes[p.Node]);
            return stage;
        } catch { stage.Dispose(); throw; }
    }
    /// <summary>Pure late join to restored runtime identities. Placed IDs are NOT runtime IDs.
    /// No Add/Buy/RunCreate/UseRecord/SetFrame replay, no old-world callback copied. Host node/effect
    /// callbacks must be supplied by the coordinator's explicit new-world provider joins.</summary>
    public void CompletePlacedScene(StagedPlacedScene stage, WorldCoreRegistry r, PlacedSceneBindings b)
    {
        PS(!IsInsideTree() && GetParent()==null && ReferenceEquals(r.Owner,this) && r.Park!=null,"detached new-world registry");
        PS(!stage.Joined && _scripted.Count==0 && _building.Count==0 && _rideMeshes.Count==0 && _current==null,"fresh scene target");
        PSUnsupported();
        var s=stage.State;
        PS(r.Park.Placed.Count(p=>p.Node!=null)==stage.Roots.Count,"placement coverage");
        foreach(var p in r.Park.Placed.Where(p=>p.Node!=null)) {
            PS(stage.Roots.TryGetValue(p.Id,out var root) && ReferenceEquals(root,p.Node),"Park factory identity");
            var n=s.Nodes[s.Placements.Single(x=>x.PlacedId==p.Id).Node];
            PS(PSPack(root.Transform).SequenceEqual(n.Transform) && root.Visible==n.Visible && root.TopLevel==n.TopLevel,"Park/SCENE cut mismatch");
        }
        AnimatedModel ModelAt(int id) { PS(stage.Models.ContainsKey(id),"model reference"); return stage.Models[id]; }
        var scripts=s.Scripted.Select(p=> {
            var ride=r.Resolve<ParkRide>(p.Ride); PS(ride.Id==p.RuntimeId,"runtime ride identity");
            var anim=p.Animation==null?null:b.ResolveAnimation(p.Animation); PS(p.Animation==null || anim!=null,"animation asset");
            return (ride,ModelAt(p.Model),anim,p.Slot,p.Variant);
        }).ToArray();
        var builds=s.Building.Select(p=> { PS(float.IsFinite(p.Frame),"build clock"); return (ModelAt(p.Model),p.Frame); }).ToArray();
        var meshes=s.Meshes.Select(p=> { var m=b.ResolveMesh(p.Asset); PS(m!=null && PSHash(m)==p.Fingerprint,"mesh fingerprint"); return (p.RuntimeId,m); }).ToArray();
        PS(meshes.Select(p=>p.RuntimeId).Distinct().Count()==meshes.Length,"duplicate mesh key");
        var current=s.Current==-1?null:ModelAt(s.Current);
        _scripted.AddRange(scripts); _building.AddRange(builds);
        foreach(var p in meshes)_rideMeshes.Add(p.RuntimeId,p.m);
        _current=current; _park=r.Park; _sim=r.Sim;
        stage.Joined=true;
    }
}
