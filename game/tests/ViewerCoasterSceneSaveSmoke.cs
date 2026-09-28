using Godot;
using TPW.PS2.Data;
using System.Text.Json;
using Aps=TPW.PS2.Data.Animation;
namespace TPWPS2Viewer {
public partial class Viewer {
    internal WorldCoreRegistry CoasterSmokeRegistry(ParkSim sim) {
        _sim=sim;var r=new WorldCoreRegistry {Owner=this,Sim=sim};
        foreach(var ride in sim.Rides) {
            var t=ride.Coaster.Track;r.Register("ride/"+ride.Id,ride);r.Register("sim/"+ride.Id,ride.Coaster);r.Register("track/"+ride.Id,t);
            for(int i=0;i<t.CaptureState("type").Nodes.Length;i++)r.Register("node/"+i,t.ResolveNodeId(i));
            int n=0;foreach(var car in ride.Coaster.Trains.SelectMany(t=>t.Cars))r.Register("car/"+n++,car);
        }return r;
    }
    internal void CoasterSmokeSeed(AssetLibrary lib,ParkRide ride,string dir) {
        _lib=lib;var v=new CoasterView {Id=ride.Id,Ride=ride,Sim=ride.Coaster,Track=ride.Coaster.Track,Dir=dir,Price=71,Frame=new Node3D {Name="coaster"},SeenTime=_sim.Time,WinchSeen=ride.Coaster.WinchVersion};
        AddChild(v.Frame);_coasters.Add(v.Id,v);LoadCoasterMaterials(v);
        foreach(var n in v.Track.Nodes()) {
            if(CoasterSegment(v,n) is {} seg){v.Frame.AddChild(seg);v.Segments.Add(n,seg);}
            if(!n.IsStation&&CoasterPylon(v,n) is {} py){v.Frame.AddChild(py);v.Pylons.Add(n,py);}
        }
        foreach(var car in v.Sim.Trains.SelectMany(t=>t.Cars)) {var c=CoasterCarModel(v)??throw new Exception("real car missing");v.Frame.AddChild(c.Node);c.Was=new(1,2,3);c.Now=new(4,5,6);c.WasB=c.NowB=Basis.Identity;v.Cars.Add(car,c);}
        if(v.PylonModels.Count==0||v.Segments.Count==0)throw new Exception("real pylon/segment missing");
        _coasterTool=v;_coasterField.Add(new(7,9));_coasterHold=.01;v.Cells.Add((7,9));
    }
    internal void CoasterSmokeLibrary(AssetLibrary lib)=>_lib=lib;
    internal Func<string,(ImageTexture Tex,bool Soft)> CoasterSmokeTextures(string owner)=>name=>TextureNear(owner,name);
    internal void CoasterSmokePresent(float alpha)=>PresentCoasters(alpha);
    internal void CoasterSmokeCallback(bool active)=>_afterCoaster=active?()=>{}:null;
    internal void CoasterSmokeFree(){_lib=null;Free();}

}
}
namespace TPWPS2Viewer.Tests {
public partial class ViewerCoasterSceneSaveSmoke:Node {
    sealed record Cut(Viewer.CoasterSceneState Scene,ParkSim.State Sim);
    static string Json(object o)=>JsonSerializer.Serialize(o);
    static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    public override void _Ready(){
        try {
            using var lib=new AssetLibrary("/home/ec2-user/tpw-ps2/tpw_ps2.bin");lib.OpenWad("/DATA/JUNGLE.WAD");
            Run(lib);
            GD.Print("VIEWER COASTER SCENE SAVE PASS: real MineCart pylons/cars, segment CPU, FILE, CORE hydration, tool, future interpolation, callback refusal; no full-world/audio/tick coverage");GetTree().Quit();
        }catch(Exception e){GD.PrintErr("VIEWER COASTER SCENE SAVE FAIL: "+e);GetTree().Quit(2);}
    }
    void Run(AssetLibrary lib){
        Viewer a=null,b=null;string file=System.IO.Path.Combine(OS.GetUserDataDir(),"coaster-scene-smoke.sav");
        try {
            var type=CoasterType.ForFolder("MineCart");
            var assetCar=lib.Rides.First(x=>x.Model!=null&&x.Model.Path.EndsWith("/MineCart/"+type.CarModel+".mps",StringComparison.OrdinalIgnoreCase));
            string path=assetCar.Model.Path,dir=path[..(path.LastIndexOf('/')+1)];
            var station=lib.Rides.First(x=>x.Name.EndsWith("/monkey.mps",StringComparison.OrdinalIgnoreCase));
            var anim=new Aps(lib.Read(station.Animation));var sim=new ParkSim(null);
            var ride=sim.Add(71,"track",new ParkCell(10,10),4,4,lib.Read(station.Script),anim,1,null,null,out var fault);Check(ride!=null,"station "+fault);
            var track=new CoasterTrack(type,new ParkCell(10,10),0,new ParkCell(11,10)){GroundY=(_,_)=>0};
            foreach(var c in new[]{new ParkCell(10,5),new ParkCell(4,5),new ParkCell(4,16),new ParkCell(11,16),new ParkCell(11,10)})track.AddPylon(c,256,0,false,CoasterNodeKind.Normal);
            sim.AttachCoaster(ride.Id,track);ride.Coaster.Spawn();
            a=new Viewer();var ar=a.CoasterSmokeRegistry(sim);a.CoasterSmokeSeed(lib,ride,dir);
            var meshes=new Dictionary<string,Model>();var animations=new Dictionary<string,Aps>();
            var textures=new Dictionary<string,Texture2D>();
            var modelIds=new Dictionary<Model,string>(ReferenceEqualityComparer.Instance);
            AnimatedModel.SnapshotBindings Asset(string m,string an)=>new(){ModelAssetId=m,AnimationAssetId=an,Model=meshes[m],Animation=an==""?null:animations[an],Texture=(b??a).CoasterSmokeTextures(m),UvRewriteId=m.EndsWith("stdpylon.mps",StringComparison.OrdinalIgnoreCase)?"square":null,UvRewrite=m.EndsWith("stdpylon.mps",StringComparison.OrdinalIgnoreCase)?(b??a).CoasterSquareLatticeProvider(meshes[m],animations[an],m):null};
            Viewer.CoasterSceneBindings Bind()=>new(){IdentifyTexture=t=>{string id=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(t.GetImage().GetData()));textures[id]=ImageTexture.CreateFromImage(t.GetImage());return id;},ResolveTexture=id=>textures[id],IdentifyRenderer=(m,o)=>{
                if(!meshes.ContainsKey(o.Model)){var asset=lib.Rides.Single(x=>x.Model?.Path==o.Model);meshes.Add(o.Model,new Model(lib.Read(asset.Model)));if(o.Animation!=null&&!animations.ContainsKey(o.Animation))animations.Add(o.Animation,new Aps(lib.Read(asset.Animation)));}
                var result=Asset(o.Model,o.Animation??"");return result;},Assets=new(){
                IdentifyRenderer=_=>throw new Exception(),ResolveRenderer=s=>Asset(s.ModelAssetId,s.AnimationAssetId),
                IdentifyMesh=m=>meshes.First(p=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(p.Value.D))==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(m.D))).Key,
                ResolveMesh=id=>meshes[id],IdentifyAnimation=_=>"station",ResolveAnimation=_=>anim}};
            var bindings=Bind();
            ParkSim.StateBindings sb=new(){IdentifyCoasterType=_=>"type",ResolveCoasterType=_=>type,IdentifyCoasterGround=_=>"ground",ResolveCoasterGround=_=>(_,_)=>0,Scripts=new(){IdentifyProgram=p=>new("station",p,null),ResolveProgram=_=>new("station",new RseProgram(lib.Read(station.Script)),null),IdentifyAnimation=_=>"station",ResolveAnimation=_=>anim}};
            var cut=new Cut(a.CaptureCoasterScene(ar,bindings),sim.CaptureState(sb));
            ParkSaveFile.Write(file,cut);var disk=ParkSaveFile.Read<Cut>(file);
            // Reload renderer assets from trusted source table, not any file-provided path.
            foreach(var key in meshes.Keys.ToArray()){var asset=lib.Rides.Single(x=>x.Model?.Path==key);meshes[key]=new Model(lib.Read(asset.Model));}
            b=new Viewer();b.CoasterSmokeLibrary(lib);using var stage=Viewer.StageCoasterScene(disk.Scene,bindings);var sim2=ParkSim.FromState(disk.Sim,null,sb);
            var br=b.CoasterSmokeRegistry(sim2);b.CompleteCoasterScene(stage,br,bindings);
            Check(Json(cut.Scene)==Json(b.CaptureCoasterScene(br,bindings)),"FILE exact scene");
            foreach(float alpha in new[]{0f,.25f,.75f,1f}){a.CoasterSmokePresent(alpha);b.CoasterSmokePresent(alpha);Check(Json(a.CaptureCoasterScene(ar,bindings))==Json(b.CaptureCoasterScene(br,bindings)),"future presentation");}
            a.CoasterSmokeCallback(true);bool refused=false;try{a.CaptureCoasterScene(ar,bindings);}catch(InvalidDataException){refused=true;}Check(refused,"callback refusal");a.CoasterSmokeCallback(false);
        }finally{if(System.IO.File.Exists(file))System.IO.File.Delete(file);a?.CoasterSmokeFree();b?.CoasterSmokeFree();}
    }
}
}
