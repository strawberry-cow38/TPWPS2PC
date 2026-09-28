using Godot;
using TPW.PS2.Data;
using System.Text.Json.Serialization;

namespace TPWPS2Viewer;

public partial class Viewer
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TrackAssetOrigin(string Wad, string Model, string Animation);
    public sealed class TrackSceneBindings
    {
        // The origin is retained at creation, not guessed from a renderer fingerprint. The
        // coordinator registers these exact paths in its trusted cold manifest policy.
        public required Func<AnimatedModel, TrackAssetOrigin, AnimatedModel.SnapshotBindings> IdentifyRenderer { get; init; }
        public required PlacedSceneBindings Assets { get; init; }
        // Must compare against the trusted manifest origin for BOTH asset IDs, not just hashes.
        // Equal bytes at different source paths are not interchangeable for future library loads.
        public required Action<TrackAssetOrigin, AnimatedModel.Snapshot> ValidateOrigin { get; init; }
        // Fail closed unless the invocation list is exactly the supplied stock handler (null
        // means empty, on a fresh target). Core currently needs a public census accessor for the
        // production adapter; do not use reflection/private-layout tricks outside test fixtures.
        public required Action<TrackRideSim, Action<TrackCar, int>> ValidateSoundCueOwner { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TrackRendererState(int Holder, int Model, TrackAssetOrigin Origin);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TrackCarViewState(string Car, int Node, int Model, string Mesh, string MeshHash,
        float[] Was, float[] Now, float WasYaw, float NowYaw, int Tag, TrackAssetOrigin Origin);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TrackViewState(int Id, string Ride, string Sim, string Layout, string Dir, string Prefix,
        int Price, int Frame, int[] Pieces, TrackRendererState[] Renderers, int[] Flowing, int FlowFrame,
        bool WarnedOpenLoop, TrackCarViewState[] Cars, int NextTag, int[][] Cells, int[][] FloorCut, long SeenTime);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TrackSceneState(int Version, PlacedNodeState[] Nodes, TrackViewState[] Tracks, int? Tool, bool LegOk);

    static void TS(bool ok, string why) { if (!ok) throw new InvalidDataException("Viewer TRACK SCENE: " + why); }
    static float[] TSVec(Vector3 v) => new[] {v.X,v.Y,v.Z};
    static Vector3 TSVec(float[] a) { TS(a != null && a.Length==3 && a.All(float.IsFinite),"car vector");return new(a[0],a[1],a[2]); }

    public TrackSceneState CaptureTrackScene(WorldCoreRegistry r, TrackSceneBindings b)
    {
        ArgumentNullException.ThrowIfNull(b);ArgumentNullException.ThrowIfNull(r);
        TS(ReferenceEquals(r.Owner,this) && ReferenceEquals(r.Sim,_sim),"capture registry");
        // EditTrack opens with null; its owner reference is covered here, but ghost/camera/HUD
        // state still requires the outer tool join. PlaceHeld's continuation captures local
        // entrance/build UI state; it has no stable callback discriminator. Never serialize it.
        TS(_afterTrack==null,"active placement continuation _afterTrack is unclassified; finish the track tool before saving");
        TS(_trackTool==null || _tracks.Values.Contains(_trackTool),"unowned tool");
        var nodes=new List<PlacedNodeState>(); var ids=new Dictionary<Node3D,int>();
        var known=new Dictionary<Node3D,(AnimatedModel Model,TrackAssetOrigin Origin)>();
        foreach(var pair in _tracks) {
            var v=pair.Value;
            TS(pair.Key==v.Id && IsInstanceValid(v.Frame) && v.Frame.GetParent()==this,"track/frame owner");
            TS(v.SoundHandler!=null,"missing stock sound handler");
            b.ValidateSoundCueOwner(v.Sim,v.SoundHandler);
            var owners=v.PieceModels.Values.Concat(v.Cars.Values.Select(c=>c.Model)).Where(m=>m!=null).ToArray();
            TS(owners.Length==owners.Distinct().Count() && v.Origins.Keys.ToHashSet().SetEquals(owners),"renderer/origin coverage");
            foreach(var m in v.PieceModels.Values.Concat(v.Cars.Values.Select(c=>c.Model)).Where(m=>m!=null)) {
                TS(v.Origins.TryGetValue(m,out var origin),"missing source origin");known.Add(m.Root,(m,origin));
            }
        }
        int Visit(Node3D n) {
            TS(IsInstanceValid(n) && !n.IsQueuedForDeletion() && !ids.ContainsKey(n),"node ownership/lifetime");
            TS(nodes.Count<100000,"node budget"); int id=nodes.Count;ids.Add(n,id);nodes.Add(null);
            AnimatedModel.Snapshot renderer=null;int[] children;
            if(known.TryGetValue(n,out var owner)) {
                TS(n.GetMetaList().Count==0,"renderer metadata");
                TS(n.GetChildren().SequenceEqual(owner.Model.Surfaces().Select(s=>(Node)s.Node)),"surface order");
                var binding=b.IdentifyRenderer(owner.Model,owner.Origin);
                renderer=owner.Model.CaptureState(binding.ModelAssetId,binding.AnimationAssetId,binding.UvRewriteId);
                TS(renderer.ModelFingerprint==PSHash(binding.Model) && renderer.AnimationFingerprint==(binding.Animation==null?"":Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(binding.Animation.D))),"renderer identity");
                b.ValidateOrigin(owner.Origin,renderer);children=Array.Empty<int>();
            } else {
                TS(n.GetType()==typeof(Node3D) && n.GetMetaList().Count==0,"unknown holder subtree");
                children=n.GetChildren().Select(c=>{TS(c is Node3D,"non spatial child");return Visit((Node3D)c);}).ToArray();
            }
            nodes[id]=new(id,n.Name.ToString().StartsWith("@")?"":n.Name.ToString(),PSPack(n.Transform),n.Visible,n.TopLevel,children,renderer);
            return id;
        }
        var views=new List<TrackViewState>();
        foreach(var v in _tracks.Values) {
            TS(r.Sim.Rides.Contains(v.Ride),"ride registry membership");
            TS(v.Cars.Keys.All(v.Sim.Cars.Contains),"retired car renderer requires retained CORE identity; capture after ordinary presentation");
            TS(v.Id==v.Ride.Id && ReferenceEquals(v.Ride.Track,v.Sim) && ReferenceEquals(v.Layout,v.Sim.Track),"shared CORE identity");
            int frame=Visit(v.Frame);
            int ModelId(AnimatedModel m)=>m==null?-1:ids[m.Root];
            var cars=v.Cars.Select(p=> {
                var c=p.Value; string mesh=c.Mesh==null?null:b.Assets.IdentifyMesh(c.Mesh);
                TS(mesh==null || PSHash(c.Mesh)==PSHash(b.Assets.ResolveMesh(mesh)),"car mesh binding");
                return new TrackCarViewState(r.Id(p.Key),ids[c.Node],ModelId(c.Model),mesh,c.Mesh==null?null:PSHash(c.Mesh),
                    TSVec(c.Was),TSVec(c.Now),c.WasYaw,c.NowYaw,c.Tag,c.Model==null?null:v.Origins[c.Model]);
            }).ToArray();
            views.Add(new(v.Id,r.Id(v.Ride),r.Id(v.Sim),r.Id(v.Layout),v.Dir,v.Prefix,v.Price,frame,
                v.Pieces.Select(n=>ids[n]).ToArray(),v.PieceModels.Select(p=>new TrackRendererState(ids[p.Key],ModelId(p.Value),v.Origins[p.Value])).ToArray(),
                v.Flowing.Select(ModelId).ToArray(),v.FlowFrame,v.WarnedOpenLoop,cars,v.NextTag,
                v.Cells.Select(c=>new[]{c.X,c.Y}).ToArray(),v.FloorCut.Select(c=>new[]{c.X,c.Y}).ToArray(),v.SeenTime));
        }
        var state=new TrackSceneState(1,nodes.ToArray(),views.ToArray(),_trackTool?.Id,_trackLegOk);
        ValidateTrackScene(state,b);
        return state;
    }

    // Validate classification before allocating any Godot objects. Every serialized node has
    // exactly one role; accepting an unclassified renderer would lose its owner on recapture.
    static void ValidateTrackScene(TrackSceneState s, TrackSceneBindings b)
    {
        TS(s.Version==1 && s.Nodes!=null && s.Nodes.Length<=100000 && s.Tracks!=null && s.Tracks.Length<=10000,"envelope");
        TS(s.Tracks.All(v=>v!=null) && s.Tracks.Select(v=>v.Id).Distinct().Count()==s.Tracks.Length,"duplicate/null view");
        TS(s.Tool==null || s.Tracks.Any(v=>v.Id==s.Tool),"tool identity");
        var parent=new int[s.Nodes.Length];Array.Fill(parent,-1);
        for(int i=0;i<s.Nodes.Length;i++) {
            var n=s.Nodes[i];TS(n!=null && n.Id==i && n.Name!=null && n.Children!=null && n.ActivationSerial==null,"node envelope");
            PSUnpack(n.Transform);
            TS(n.Renderer==null || n.Children.Length==0,"renderer children");
            foreach(int c in n.Children){TS(c>i && c<s.Nodes.Length && parent[c]==-1,"ordered unique topology");parent[c]=i;}
            if(n.Renderer is {} m)
                TS(n.Transform.SequenceEqual(m.RootTransform ?? Array.Empty<float>()) && n.Visible==m.RootVisible && n.TopLevel==m.RootTopLevel,"renderer/root cut mismatch");
        }
        var used=new HashSet<int>();var rides=new HashSet<string>();var sims=new HashSet<string>();var layouts=new HashSet<string>();
        void Node(int id,int owner,bool renderer) {
            TS(id>=0 && id<s.Nodes.Length && used.Add(id),"node role/reference");
            TS(parent[id]==owner && (s.Nodes[id].Renderer!=null)==renderer,"node role/parent");
        }
        void Text(string text)=>TS(!string.IsNullOrWhiteSpace(text) && text.Length<=1024,"identity/path");
        void Model(int id,int holder,TrackAssetOrigin origin) {
            Node(id,holder,true);TS(origin!=null,"missing origin");Text(origin.Wad);Text(origin.Model);
            if(origin.Animation!=null)Text(origin.Animation);
            var m=s.Nodes[id].Renderer;
            TS((origin.Animation==null)==(m.AnimationAssetId==""),"animation origin");
            b.ValidateOrigin(origin,m);
        }
        foreach(var v in s.Tracks) {
            Text(v.Ride);Text(v.Sim);Text(v.Layout);Text(v.Dir);Text(v.Prefix);
            TS(rides.Add(v.Ride)&&sims.Add(v.Sim)&&layouts.Add(v.Layout),"shared native owner");
            TS(v.Pieces!=null && v.Renderers!=null && v.Flowing!=null && v.Cars!=null && v.Cells!=null && v.FloorCut!=null,"view arrays");
            TS(v.Cars.Length<=TrackRideSim.SnapshotCarLimit && v.FlowFrame>=-1 && v.SeenTime>=-1 && v.NextTag>=CarSoundTagBase,"view clocks/tags");
            Node(v.Frame,-1,false);
            foreach(int id in v.Pieces)Node(id,v.Frame,false);
            var holders=new HashSet<int>();var models=new HashSet<int>();
            foreach(var p in v.Renderers) {
                TS(p!=null && v.Pieces.Contains(p.Holder) && holders.Add(p.Holder) && models.Add(p.Model),"piece renderer coverage");
                Model(p.Model,p.Holder,p.Origin);
            }
            TS(v.Flowing.Distinct().Count()==v.Flowing.Length && v.Flowing.All(models.Contains),"flow owner/order");
            var cars=new HashSet<string>();var tags=new HashSet<int>();
            foreach(var c in v.Cars) {
                TS(c!=null,"null car");Text(c.Car);
                TS(cars.Add(c.Car) && tags.Add(c.Tag) && c.Tag>=CarSoundTagBase && c.Tag<v.NextTag,"car identity/tag");
                Node(c.Node,v.Frame,false);
                TSVec(c.Was);TSVec(c.Now);TS(float.IsFinite(c.WasYaw)&&float.IsFinite(c.NowYaw),"car yaw");
                if(c.Model!=-1)Model(c.Model,c.Node,c.Origin);else TS(c.Origin==null,"orphan origin");
                TS((c.Mesh==null)==(c.MeshHash==null),"mesh binding");
                if(c.Mesh!=null)Text(c.Mesh);
            }
            foreach(var cells in new[]{v.Cells,v.FloorCut}) {
                TS(cells.Length<=100000 && cells.All(c=>c!=null&&c.Length==2),"cells");
                TS(cells.Select(c=>(c[0],c[1])).Distinct().Count()==cells.Length,"duplicate cells");
            }
        }
        TS(used.Count==s.Nodes.Length,"unclassified scene node/renderer");
    }

    public sealed class StagedTrackScene : IDisposable
    {
        internal TrackSceneState State;
        internal StagedPlacedScene Tree;
        // Factory for the parent's ordered Viewer-child topology. Frames are not Park placements;
        // station placed nodes remain owned by the separate PlacedScene factory.
        public Node3D TakeFrame(int rideId)=>Tree.TakePlacement(rideId);
        public void Dispose()=>Tree.Dispose();
    }
    public static StagedTrackScene StageTrackScene(TrackSceneState s, TrackSceneBindings b)
    {
        ArgumentNullException.ThrowIfNull(s);ArgumentNullException.ThrowIfNull(b);
        ValidateTrackScene(s,b);
        var tree=StagePlacedScene(new(1,s.Nodes,s.Tracks.Select(v=>new PlacedRootState(v.Id,v.Frame)).ToArray(),
            Array.Empty<PlacedScriptState>(),Array.Empty<PlacedBuildState>(),Array.Empty<PlacedMeshState>(),-1),b.Assets);
        return new(){State=s,Tree=tree};
    }

    /// <summary>Join only to a detached freshly hydrated world. No build, car creation, simulation
    /// tick, or frame sampling. Parent joins audio before future presentation and supplies its
    /// fresh asset library/terrain/services as usual. All callbacks below close over new objects.</summary>
    public void CompleteTrackScene(StagedTrackScene stage, WorldCoreRegistry r, TrackSceneBindings b)
    {
        ArgumentNullException.ThrowIfNull(stage);ArgumentNullException.ThrowIfNull(r);ArgumentNullException.ThrowIfNull(b);
        TS(!IsInsideTree() && GetParent()==null && ReferenceEquals(r.Owner,this) && r.Sim!=null,"detached target");
        TS(!stage.Tree.Joined && _tracks.Count==0 && _trackTool==null && _afterTrack==null,"fresh target");
        var tree=stage.Tree;var s=stage.State;ValidateTrackScene(s,b);var views=new List<TrackRideView>();
        foreach(var node in s.Nodes) {
            var actual=tree.Nodes[node.Id];
            TS(IsInstanceValid(actual) && !actual.IsQueuedForDeletion(),"staged node lifetime");
            TS(PSPack(actual.Transform).SequenceEqual(node.Transform) && actual.Visible==node.Visible && actual.TopLevel==node.TopLevel,"staged node changed");
            if(node.Renderer==null)
                TS(actual.GetChildren().SequenceEqual(node.Children.Select(id=>(Node)tree.Nodes[id])),"staged child order changed");
        }
        AnimatedModel ModelAt(int id)=>id==-1?null:tree.Models.TryGetValue(id,out var m)?m:throw new InvalidDataException("TRACK model reference");
        foreach(var saved in s.Tracks) {
            var ride=r.Resolve<ParkRide>(saved.Ride);var sim=r.Resolve<TrackRideSim>(saved.Sim);var layout=r.Resolve<TrackLayout>(saved.Layout);
            b.ValidateSoundCueOwner(sim,null);
            TS(r.Sim.Rides.Contains(ride),"ride registry membership");
            TS(ride.Id==saved.Id && ReferenceEquals(ride.Track,sim) && ReferenceEquals(sim.Track,layout),"CORE identity join");
            TS(saved.Pieces!=null && saved.Renderers!=null && saved.Flowing!=null && saved.Cars!=null && saved.Cells!=null && saved.FloorCut!=null,"view arrays");
            var v=new TrackRideView {Id=saved.Id,Ride=ride,Sim=sim,Layout=layout,Dir=saved.Dir,Prefix=saved.Prefix,Price=saved.Price,
                Frame=tree.Nodes[saved.Frame],FlowFrame=saved.FlowFrame,WarnedOpenLoop=saved.WarnedOpenLoop,NextTag=saved.NextTag,SeenTime=saved.SeenTime};
            TS(v.Frame.GetParent()==null || v.Frame.GetParent()==this,"frame factory parent");
            foreach(int id in saved.Pieces) {var n=tree.Nodes[id];TS(n.GetParent()==v.Frame && !v.Pieces.Contains(n),"piece ownership");v.Pieces.Add(n);}
            foreach(var p in saved.Renderers) {
                var m=ModelAt(p.Model);var holder=tree.Nodes[p.Holder];
                TS(m!=null && v.Pieces.Contains(holder) && m.Root.GetParent()==holder && p.Origin!=null,"piece renderer ownership");
                v.PieceModels.Add(holder,m);v.Origins.Add(m,p.Origin);
            }
            foreach(int id in saved.Flowing) {var m=ModelAt(id);TS(m!=null && v.PieceModels.Values.Contains(m),"flow owner");v.Flowing.Add(m);}
            foreach(var c in saved.Cars) {
                var car=r.Resolve<TrackCar>(c.Car);var n=tree.Nodes[c.Node];var m=ModelAt(c.Model);
                TS(sim.Cars.Contains(car) && n.GetParent()==v.Frame && !v.Pieces.Contains(n),"car owner");
                TS(m==null || m.Root.GetParent()==n && c.Origin!=null,"car renderer owner");
                TS(float.IsFinite(c.WasYaw)&&float.IsFinite(c.NowYaw),"car yaw");
                var mesh=c.Mesh==null?null:b.Assets.ResolveMesh(c.Mesh);TS(mesh==null?c.MeshHash==null:PSHash(mesh)==c.MeshHash,"mesh fingerprint");
                v.Cars.Add(car,new(){Node=n,Model=m,Mesh=mesh,Was=TSVec(c.Was),Now=TSVec(c.Now),WasYaw=c.WasYaw,NowYaw=c.NowYaw,Tag=c.Tag});
                if(m!=null)v.Origins.Add(m,c.Origin);
            }
            void Cells(int[][] cells,HashSet<(int,int)> target) {foreach(var c in cells){TS(c!=null&&c.Length==2,"cell");TS(target.Add((c[0],c[1])),"duplicate cell");}}
            Cells(saved.Cells,v.Cells);Cells(saved.FloorCut,v.FloorCut);views.Add(v);
        }
        _sim=r.Sim;_park=r.Park;
        foreach(var v in views) {
            if(v.Frame.GetParent()==null)AddChild(v.Frame);
            _tracks.Add(v.Id,v);
            v.SoundHandler=(car,evt)=>TrackCarSound(v,car,evt);v.Sim.SoundCue+=v.SoundHandler;
        }
        _trackTool=s.Tool is int tool?_tracks[tool]:null;_trackLegOk=s.LegOk;
        tree.Joined=true;
        RebindTrackSceneSounds();
    }

    /// <summary>Call again after the parent audio join if it installs RideSounds after SCENE.
    /// Registers only fresh position providers; does not replay any cue.</summary>
    public void RebindTrackSceneSounds()
    {
        foreach(var v in _tracks.Values)
            foreach(var c in v.Cars.Values) {var node=c.Node;_sounds?.Follow(v.Id,c.Tag,()=>IsInstanceValid(node)?node.GlobalPosition:null);}
    }
}
