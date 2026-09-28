using Godot;
using TPW.PS2.Data;
using System.Text.Json;
using System.Reflection;
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
        v.SoundHandler=(car,evt)=>TrackCarSound(v,car,evt);v.Sim.SoundCue+=v.SoundHandler;
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
    internal void TrackSmokeServices(AssetLibrary lib){_lib=lib;_sounds=new RideSounds(this,null,null);RebindTrackSceneSounds();}
    internal int TrackSmokeCues=>_sounds.Cued;
    internal void TrackSmokeOwnership(Viewer old) {
        foreach(var v in _tracks.Values) {
            var prior=old._tracks[v.Id];
            if(ReferenceEquals(v.Sim,prior.Sim)||ReferenceEquals(v.Layout,prior.Layout)||ReferenceEquals(v.Ride,prior.Ride)
                ||ReferenceEquals(v.Frame,prior.Frame)||v.Frame.GetParent()!=this)throw new Exception("restored owner alias");
            foreach(var c in v.Cars)if(!v.Sim.Cars.Contains(c.Key)||prior.Cars.ContainsKey(c.Key)||c.Value.Node.GetParent()!=v.Frame)
                throw new Exception("restored car identity");
            if(v.PieceModels.Values.Any(prior.PieceModels.Values.Contains)||v.Origins.Count!=v.PieceModels.Count+v.Cars.Values.Count(c=>c.Model!=null))
                throw new Exception("restored renderer owner");
        }
    }
    internal void TrackSmokeFree(){
        // These constructor-owned roots have never been parented by _Ready in this shell.
        foreach(var root in new[]{_weather.Root,_flags.Root,_thoughts.Root})
            if(IsInstanceValid(root)&&root.GetParent()==null)root.Free();
        foreach(var v in _tracks.Values)if(v.SoundHandler!=null)v.Sim.SoundCue-=v.SoundHandler;
        _tracks.Clear();_lib=null;Free(); // Synchronously free child frames; do not leave QueueFree work at exit.
    }
}
}
namespace TPWPS2Viewer.Tests {
public partial class ViewerTrackSceneSaveSmoke:Node {
    public partial class ShellViewer:Viewer {public override void _Ready(){} public override void _Process(double delta){}}
    static Action<TrackCar,int> SoundHandlers(TrackRideSim sim)=>(Action<TrackCar,int>)typeof(TrackRideSim)
        .GetField("SoundCue",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sim); // test-only event census
    sealed record Cut(Viewer.TrackSceneState Scene,ParkSim.State Sim);
    static string Json(object o)=>JsonSerializer.Serialize(o);
    static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    public override void _Ready(){
        try {
            using var lib=new AssetLibrary("/home/ec2-user/tpw-ps2/tpw_ps2.bin");lib.OpenWad("/DATA/JUNGLE.WAD");
            foreach(bool kart in new[]{false,true})Run(lib,kart);
            GD.Print("VIEWER TRACK SCENE SAVE PASS: real boat/kart assets, actual Viewer fields FILE, CORE hydration, non-flowing owners, edit tool, source preservation, restored identity, Advance/PresentTracks, stock callback rebinding and refusal");GetTree().Quit();
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
            var takeHead=ride.Track.TakeHead;ride.Track.TakeHead=()=>null;for(uint tick=0;tick<80;tick++)ride.Track.Step(tick);
            int nextGuest=42;ride.Track.TakeHead=()=>nextGuest++;
            for(uint boarding=80;boarding<=160;boarding+=20)ride.Track.Step(boarding);
            ride.Track.TakeHead=takeHead;
            Check(ride.Track.Status==TrackRideStatus.Running,"nonvacuous moving-car fixture");
            // Only source constructs track/cars. All loaded CORE comes from snapshots below.
            var ts=ride.Track.CaptureState();
            Check(ts.Cars.Length>0,"source cars");
            a=new ShellViewer();var ar=a.TrackSmokeRegistry(sim);a.TrackSmokeSeed(lib,ride,dir,prefix);
            var meshes=new Dictionary<string,Model>();var animations=new Dictionary<string,Aps>();
            AnimatedModel.SnapshotBindings Asset(string m,string an)=>new(){ModelAssetId=m,AnimationAssetId=an,Model=meshes[m],Animation=an==""?null:animations[an],Texture=_=>(null,false)};
            Viewer.TrackSceneBindings Bind()=>new(){
                ValidateSoundCueOwner=(owner,expected)=>{if(SoundHandlers(owner)!=expected)throw new InvalidDataException("unclassified sound callback");},
                ValidateOrigin=(origin,snapshot)=>{
                    if(origin.Wad!=lib.WadName || origin.Model!=snapshot.ModelAssetId || (origin.Animation??"")!=snapshot.AnimationAssetId)
                        throw new InvalidDataException("untrusted origin/asset association");
                },IdentifyRenderer=(m,o)=>{
                if(!meshes.ContainsKey(o.Model)){var asset=lib.Rides.Single(x=>x.Model?.Path==o.Model);meshes.Add(o.Model,new Model(lib.Read(asset.Model)));if(o.Animation!=null&&!animations.ContainsKey(o.Animation))animations.Add(o.Animation,new Aps(lib.Read(asset.Animation)));}
                return Asset(o.Model,o.Animation??"");},Assets=new(){
                IdentifyRenderer=_=>throw new Exception(),ResolveRenderer=s=>Asset(s.ModelAssetId,s.AnimationAssetId),
                IdentifyMesh=m=>meshes.First(p=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(p.Value.D))==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(m.D))).Key,
                ResolveMesh=id=>meshes[id],IdentifyAnimation=_=>"station",ResolveAnimation=_=>anim}};
            var bindings=Bind();
            ParkSim.StateBindings sb=new(){IdentifyTrackGround=_=>"ground",ResolveTrackGround=_=>new TrackGround(),Scripts=new(){IdentifyProgram=p=>new("station",p,null),ResolveProgram=_=>new("station",new RseProgram(lib.Read(station.Script)),null),IdentifyAnimation=_=>"station",ResolveAnimation=_=>anim}};
            var cut=new Cut(a.CaptureTrackScene(ar,bindings),sim.CaptureState(sb));
            string source=Json(cut);
            Check(kart || cut.Scene.Tracks.Single().Flowing.Length==1,"nonvacuous boat flowing owner");
            Check(cut.Scene.Tracks.Single().Renderers.Length==2,"non-flowing owner retained");
            ParkSaveFile.Write(file,cut);var disk=ParkSaveFile.Read<Cut>(file);
            // Reload renderer assets from trusted source table, not any file-provided path.
            foreach(var key in meshes.Keys.ToArray()){var asset=lib.Rides.Single(x=>x.Model?.Path==key);meshes[key]=new Model(lib.Read(asset.Model));}
            foreach(var key in animations.Keys.ToArray()){
                var asset=lib.Rides.First(x=>x.Animation?.Path==key);animations[key]=new Aps(lib.Read(asset.Animation));
            }
            // Invalid cuts fail before allocation, including omission of an otherwise valid model.
            void Refuse(Viewer.TrackSceneState bad,string why){bool rejectedCut=false;try{using var rejected=Viewer.StageTrackScene(bad,bindings);}catch(InvalidDataException){rejectedCut=true;}Check(rejectedCut,why);}
            var view=disk.Scene.Tracks.Single();
            Refuse(disk.Scene with {Tracks=new[]{view with {Renderers=view.Renderers.Take(1).ToArray()}}},"unclassified renderer refusal");
            Refuse(disk.Scene with {Tracks=new[]{view with {Flowing=new[]{view.Renderers[0].Model,view.Renderers[0].Model}}}},"duplicate flow refusal");
            var badRenderers=view.Renderers.ToArray();badRenderers[0]=badRenderers[0] with {Origin=badRenderers[0].Origin with {Wad="forged"}};
            Refuse(disk.Scene with {Tracks=new[]{view with {Renderers=badRenderers}}},"source origin refusal");
            var badNodes=disk.Scene.Nodes.ToArray();
            int rendererNode=view.Renderers[0].Model;
            badNodes[rendererNode]=badNodes[rendererNode] with {Visible=!badNodes[rendererNode].Visible};
            Refuse(disk.Scene with {Nodes=badNodes},"renderer root cut mismatch refusal");
            var badCars=view.Cars.ToArray();badCars[0]=badCars[0] with {WasYaw=float.NaN};
            Refuse(disk.Scene with {Tracks=new[]{view with {Cars=badCars}}},"finite interpolation refusal");
            Refuse(disk.Scene with {Tracks=new[]{view with {Cars=view.Cars.Concat(new[]{view.Cars[0]}).ToArray()}}},"duplicate car refusal");
            using var stage=Viewer.StageTrackScene(disk.Scene,bindings);var sim2=ParkSim.FromState(disk.Sim,null,sb);
            b=new ShellViewer();var br=b.TrackSmokeRegistry(sim2);b.CompleteTrackScene(stage,br,bindings);
            Check(Json(cut.Scene)==Json(b.CaptureTrackScene(br,bindings)),"FILE exact scene/no frame evaluation");
            Check(source==Json(new Cut(a.CaptureTrackScene(ar,bindings),sim.CaptureState(sb))),"capture/stage did not mutate source");
            Check(Json(disk.Sim)==Json(sim2.CaptureState(sb)),"no CORE replay on restore");
            b.TrackSmokeOwnership(a);
            a.TrackSmokeServices(lib);b.TrackSmokeServices(lib);
            AddChild(a);AddChild(b); // Shell only: no ordinary Viewer startup or process.
            int oldCues=a.TrackSmokeCues,newCues=b.TrackSmokeCues;
            SoundHandlers(sim2.Rides.Single().Track)(sim2.Rides.Single().Track.Cars[0],15);
            Check(a.TrackSmokeCues==oldCues && b.TrackSmokeCues==newCues+1,"sound callback targets new viewer only");
            foreach(float alpha in new[]{0f,.25f,.75f,1f}){a.TrackSmokePresent(alpha);b.TrackSmokePresent(alpha);Check(Json(a.CaptureTrackScene(ar,bindings))==Json(b.CaptureTrackScene(br,bindings)),"future presentation");}
            var distances=ride.Track.Cars.Select(c=>c.Distance).ToArray();
            for(int tick=0;tick<24;tick++) {
                Check(sim.Advance(.04)>0 && sim2.Advance(.04)>0,"actual native Advance");
                foreach(float alpha in new[]{.125f,.625f}) {
                    a.TrackSmokePresent(alpha);b.TrackSmokePresent(alpha);
                    Check(Json(a.CaptureTrackScene(ar,bindings))==Json(b.CaptureTrackScene(br,bindings)),"future native Advance/presentation "+tick);
                }
                Check(Json(sim.CaptureState(sb))==Json(sim2.CaptureState(sb)),"future CORE "+tick);
            }
            Check(!distances.SequenceEqual(ride.Track.Cars.Select(c=>c.Distance)),"native cars actually moved");
            Action<TrackCar,int> extra=(_,_)=>{};ride.Track.SoundCue+=extra;
            bool extraRefused=false;try{a.CaptureTrackScene(ar,bindings);}catch(InvalidDataException){extraRefused=true;}finally{ride.Track.SoundCue-=extra;}
            Check(extraRefused,"unknown event subscriber refusal");
            a.TrackSmokeCallback(true);bool refused=false;try{a.CaptureTrackScene(ar,bindings);}catch(InvalidDataException){refused=true;}Check(refused,"callback refusal");a.TrackSmokeCallback(false);
        }finally{if(System.IO.File.Exists(file))System.IO.File.Delete(file);a?.TrackSmokeFree();b?.TrackSmokeFree();}
    }
}
}
