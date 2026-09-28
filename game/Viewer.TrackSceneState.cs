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
        TS(ReferenceEquals(r.Owner,this) && ReferenceEquals(r.Sim,_sim),"capture registry");
        // EditTrack opens with null, and is resumable. PlaceHeld's continuation captures local
        // entrance/build UI state; it has no stable callback discriminator. Never serialize it.
        TS(_afterTrack==null,"active placement continuation _afterTrack is unclassified; finish the track tool before saving");
        TS(_trackTool==null || _tracks.Values.Contains(_trackTool),"unowned tool");
        var nodes=new List<PlacedNodeState>(); var ids=new Dictionary<Node3D,int>();
        var known=new Dictionary<Node3D,(AnimatedModel Model,TrackAssetOrigin Origin)>();
        foreach(var v in _tracks.Values) {
            foreach(var m in v.PieceModels.Values.Concat(v.Cars.Values.Select(c=>c.Model)).Where(m=>m!=null)) {
                TS(v.Origins.TryGetValue(m,out var origin),"missing source origin");known.Add(m.Root,(m,origin));
            }
        }
        int Visit(Node3D n) {
            TS(IsInstanceValid(n) && !n.IsQueuedForDeletion() && !ids.ContainsKey(n),"node ownership/lifetime");
            TS(nodes.Count<100000,"node budget"); int id=nodes.Count;ids.Add(n,id);nodes.Add(null);
            AnimatedModel.Snapshot renderer=null;int[] children;
            if(known.TryGetValue(n,out var owner)) {
                TS(n.GetChildren().SequenceEqual(owner.Model.Surfaces().Select(s=>(Node)s.Node)),"surface order");
                var binding=b.IdentifyRenderer(owner.Model,owner.Origin);
                renderer=owner.Model.CaptureState(binding.ModelAssetId,binding.AnimationAssetId,binding.UvRewriteId);
                TS(renderer.ModelFingerprint==PSHash(binding.Model),"renderer identity");children=Array.Empty<int>();
            } else {
                TS(n.GetType()==typeof(Node3D) && n.GetMetaList().Count==0,"unknown holder subtree");
                children=n.GetChildren().Select(c=>{TS(c is Node3D,"non spatial child");return Visit((Node3D)c);}).ToArray();
            }
            nodes[id]=new(id,n.Name.ToString().StartsWith("@")?"":n.Name.ToString(),PSPack(n.Transform),n.Visible,n.TopLevel,children,renderer);
            return id;
        }
        var views=new List<TrackViewState>();
        foreach(var v in _tracks.Values) {
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
        return new(1,nodes.ToArray(),views.ToArray(),_trackTool?.Id,_trackLegOk);
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
        TS(s.Version==1 && s.Nodes!=null && s.Tracks!=null && s.Tracks.Length<=10000,"envelope");
        TS(s.Tracks.Select(v=>v.Id).Distinct().Count()==s.Tracks.Length,"duplicate view");
        TS(s.Tool==null || s.Tracks.Any(v=>v.Id==s.Tool),"tool identity");
        var tree=StagePlacedScene(new(1,s.Nodes,s.Tracks.Select(v=>new PlacedRootState(v.Id,v.Frame)).ToArray(),
            Array.Empty<PlacedScriptState>(),Array.Empty<PlacedBuildState>(),Array.Empty<PlacedMeshState>(),-1),b.Assets);
        return new(){State=s,Tree=tree};
    }

    /// <summary>Join only to a detached freshly hydrated world. No build, car creation, simulation
    /// tick, or frame sampling. Parent joins audio before future presentation and supplies its
    /// fresh asset library/terrain/services as usual. All callbacks below close over new objects.</summary>
    public void CompleteTrackScene(StagedTrackScene stage, WorldCoreRegistry r, TrackSceneBindings b)
    {
        TS(!IsInsideTree() && GetParent()==null && ReferenceEquals(r.Owner,this),"detached target");
        TS(!stage.Tree.Joined && _tracks.Count==0 && _trackTool==null && _afterTrack==null,"fresh target");
        var tree=stage.Tree;var s=stage.State;var views=new List<TrackRideView>();
        AnimatedModel ModelAt(int id)=>id==-1?null:tree.Models.TryGetValue(id,out var m)?m:throw new InvalidDataException("TRACK model reference");
        foreach(var saved in s.Tracks) {
            var ride=r.Resolve<ParkRide>(saved.Ride);var sim=r.Resolve<TrackRideSim>(saved.Sim);var layout=r.Resolve<TrackLayout>(saved.Layout);
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
