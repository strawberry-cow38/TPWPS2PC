using Godot;
using TPW.PS2.Data;
using System.Text.Json;
using Aps=TPW.PS2.Data.Animation;
namespace TPWPS2Viewer {
public partial class Viewer {
    internal WorldCoreRegistry TrackSmokeRegistry(ParkSim sim) {
        _sim=sim;var r=new WorldCoreRegistry {Owner=this,Sim=sim};
        foreach(var ride in sim.Rides) {
            r.Register("ride/"+ride.Id,ride);r.Register("sim/"+ride.Id,ride.Track);r.Register("layout/"+ride.Id,ride.Track.Track);
            for(int i=0;i<ride.Track.Cars.Count;i++)r.Register($"car/{ride.Id}/{i}",ride.Track.Cars[i]);
        }
        return r;
    }
    internal void TrackSmokeSeed(AssetLibrary lib,ParkRide ride,string dir,string prefix) {
        _lib=lib;
        var v=new TrackRideView {Id=ride.Id,Ride=ride,Sim=ride.Track,Layout=ride.Track.Track,Dir=dir,Prefix=prefix,Price=71,Frame=new Node3D {Name="track"},FlowFrame=7,SeenTime=123,WarnedOpenLoop=true};
        AddChild(v.Frame);_tracks.Add(v.Id,v);
        foreach(string stem in new[]{prefix+"trcks",prefix+"trckb"}) {
            var node=TrackModel(v,stem,out var model,out _,out var anim);
            if(node==null)throw new Exception("real track asset missing "+stem);
            v.Frame.AddChild(node);node.Position=new Vector3(v.Pieces.Count,2,3);v.Pieces.Add(node);v.PieceModels.Add(node,model);
            if(v.Pieces.Count==1 && anim?.Records().FirstOrDefault(r=>r.Slot==5) is {} main){model.UseRecord(main);model.SetFrame(7);v.Flowing.Add(model);}
        }
        v.Cells.Add((7,9));v.FloorCut.Add((8,10));
        foreach(var car in v.Sim.Cars) {
            var node=TrackModel(v,prefix+(car.IsKart?"blue":"ring"),out var model,out var mesh);
            if(node==null)throw new Exception("real car asset missing");v.Frame.AddChild(node);
            v.Cars.Add(car,new(){Node=node,Model=model,Mesh=mesh,Was=new(1,2,3),Now=new(4,5,6),WasYaw=.2f,NowYaw=.9f,Tag=v.NextTag++});
        }
        _trackTool=v;_trackLegOk=true;
    }
    internal void TrackSmokePresent(float alpha)=>PresentTracks(alpha);
    internal void TrackSmokeCallback(bool active)=>_afterTrack=active?()=>{}:null;
    internal void TrackSmokeFree(){ClearTrackViews();_lib=null;Free();}
}
}
namespace TPWPS2Viewer.Tests {
public partial class ViewerTrackSceneSaveSmoke:Node {
    sealed record Cut(Viewer.TrackSceneState Scene,ParkSim.State Sim);
    static string Json(object o)=>JsonSerializer.Serialize(o);
    static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    public override void _Ready(){
        try {
            using var lib=new AssetLibrary("/home/ec2-user/tpw-ps2/tpw_ps2.bin");lib.OpenWad("/DATA/JUNGLE.WAD");
            foreach(bool kart in new[]{false,true})Run(lib,kart);
            GD.Print("VIEWER TRACK SCENE SAVE PASS: real boat/kart assets, actual Viewer fields FILE, CORE hydration, non-flowing owners, edit tool, future PresentTracks, callback refusal");GetTree().Quit();
        }catch(Exception e){GD.PrintErr("VIEWER TRACK SCENE SAVE FAIL: "+e);GetTree().Quit(2);}
    }
    void Run(AssetLibrary lib,bool kart){
        Viewer a=null,b=null;string file=System.IO.Path.Combine(OS.GetUserDataDir(),"track-scene-smoke.sav");
        try {
            string folder=kart?"gokarts":"wateride";
            var straight=lib.Rides.First(x=>x.Model!=null && x.Model.Path.Contains(folder,StringComparison.OrdinalIgnoreCase)&&x.Model.Path.EndsWith("trcks.mps",StringComparison.OrdinalIgnoreCase));
            string path=straight.Model.Path,dir=path[..(path.LastIndexOf('/')+1)],prefix=path[(path.LastIndexOf('/')+1)..^"trcks.mps".Length];
            var station=lib.Rides.First(x=>x.Name.EndsWith("/monkey.mps",StringComparison.OrdinalIgnoreCase));
            var anim=new Aps(lib.Read(station.Animation));var sim=new ParkSim(null);
            var ride=sim.Add(71,"track",new ParkCell(10,10),4,4,lib.Read(station.Script),anim,1,null,null,out var fault);Check(ride!=null,"station "+fault);
            var layout=new TrackLayout(new ParkCell(10,10),0,new TrackGround());
            var e=layout.ExitCell;var ret=layout.ReturnCell;
            foreach(var c in new[]{e.Offset(6,0),e.Offset(6,8),ret.Offset(-6,8),ret.Offset(-6,0),ret})layout.Add(c);
            sim.AttachTrack(ride.Id,layout,2,kart);
            ride.Track.TakeHead=()=>null;for(uint tick=0;tick<80;tick++)ride.Track.Step(tick);
            ride.Track.TakeHead=()=>42;ride.Track.Step(80);
            // Only source constructs track/cars. All loaded CORE comes from snapshots below.
            var ts=ride.Track.CaptureState();
            Check(ts.Cars.Length>0,"source cars");
            a=new Viewer();var ar=a.TrackSmokeRegistry(sim);a.TrackSmokeSeed(lib,ride,dir,prefix);
            var meshes=new Dictionary<string,Model>();var animations=new Dictionary<string,Aps>();
            var modelIds=new Dictionary<Model,string>(ReferenceEqualityComparer.Instance);
            AnimatedModel.SnapshotBindings Asset(string m,string an)=>new(){ModelAssetId=m,AnimationAssetId=an,Model=meshes[m],Animation=an==""?null:animations[an],Texture=_=>(null,false)};
            Viewer.TrackSceneBindings Bind()=>new(){IdentifyRenderer=(m,o)=>{
                if(!meshes.ContainsKey(o.Model)){var asset=lib.Rides.Single(x=>x.Model?.Path==o.Model);meshes.Add(o.Model,new Model(lib.Read(asset.Model)));if(o.Animation!=null&&!animations.ContainsKey(o.Animation))animations.Add(o.Animation,new Aps(lib.Read(asset.Animation)));}
                return Asset(o.Model,o.Animation??"");},Assets=new(){
                IdentifyRenderer=_=>throw new Exception(),ResolveRenderer=s=>Asset(s.ModelAssetId,s.AnimationAssetId),
                IdentifyMesh=m=>meshes.First(p=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(p.Value.D))==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(m.D))).Key,
                ResolveMesh=id=>meshes[id],IdentifyAnimation=_=>"station",ResolveAnimation=_=>anim}};
            var bindings=Bind();
            ParkSim.StateBindings sb=new(){IdentifyTrackGround=_=>"ground",ResolveTrackGround=_=>new TrackGround(),Scripts=new(){IdentifyProgram=p=>new("station",p,null),ResolveProgram=_=>new("station",new RseProgram(lib.Read(station.Script)),null),IdentifyAnimation=_=>"station",ResolveAnimation=_=>anim}};
            var cut=new Cut(a.CaptureTrackScene(ar,bindings),sim.CaptureState(sb));
            ParkSaveFile.Write(file,cut);var disk=ParkSaveFile.Read<Cut>(file);
            // Reload renderer assets from trusted source table, not any file-provided path.
            foreach(var key in meshes.Keys.ToArray()){var asset=lib.Rides.Single(x=>x.Model?.Path==key);meshes[key]=new Model(lib.Read(asset.Model));}
            using var stage=Viewer.StageTrackScene(disk.Scene,bindings);var sim2=ParkSim.FromState(disk.Sim,null,sb);
            b=new Viewer();var br=b.TrackSmokeRegistry(sim2);b.CompleteTrackScene(stage,br,bindings);
            Check(Json(cut.Scene)==Json(b.CaptureTrackScene(br,bindings)),"FILE exact scene");
            foreach(float alpha in new[]{0f,.25f,.75f,1f}){a.TrackSmokePresent(alpha);b.TrackSmokePresent(alpha);Check(Json(a.CaptureTrackScene(ar,bindings))==Json(b.CaptureTrackScene(br,bindings)),"future presentation");}
            a.TrackSmokeCallback(true);bool refused=false;try{a.CaptureTrackScene(ar,bindings);}catch(InvalidDataException){refused=true;}Check(refused,"callback refusal");a.TrackSmokeCallback(false);
        }finally{if(System.IO.File.Exists(file))System.IO.File.Delete(file);a?.TrackSmokeFree();b?.TrackSmokeFree();}
    }
}
}
