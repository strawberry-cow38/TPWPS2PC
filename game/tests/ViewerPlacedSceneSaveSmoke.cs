using Godot;
using TPW.PS2.Data;
using System.Text.Json;
using Aps = TPW.PS2.Data.Animation;
using Registry = TPWPS2Viewer.SaveAssetRegistry;
namespace TPWPS2Viewer
{

// Test-only partial access, no reflection or production mutation API.
public partial class Viewer
{
    internal WorldCoreRegistry PlacedSmokeRegistry(Park park, ParkRide ride, ParkSim sim)
    {
        _park=park; _sim=sim;
        var r=new WorldCoreRegistry { Owner=this, Park=park, Sim=sim };
        r.Register("ride/test",ride); return r;
    }
    internal void PlacedSmokeSeed(ParkRide ride, AnimatedModel model, Model mesh, Aps anim)
    { _scripted.Add((ride,model,anim,5,0)); _rideMeshes.Add(ride.Id,mesh); _current=model; }
    internal AnimatedModel PlacedSmokeModel()=>_scripted.Single().Model;
    internal void PlacedSmokeDispose()=>DisposeUnpublishedActorWorld();
    internal void PlacedSmokeRefusal(Action capture)
    {
        _tracks.Add(900,new TrackRideView());
        try { capture(); throw new Exception("track should refuse"); }
        catch(InvalidDataException) { }
        finally { _tracks.Clear(); }
        _coasters.Add(901,new CoasterView());
        try { capture(); throw new Exception("coaster should refuse"); }
        catch(InvalidDataException) { }
        finally { _coasters.Clear(); }
    }
}
}
namespace TPWPS2Viewer.Tests
{
public partial class ViewerPlacedSceneSaveSmoke : Node
{
    static void Check(bool ok,string why) { if(!ok)throw new Exception(why); }
    static string Json(object o)=>JsonSerializer.Serialize(o);
    sealed record FileCut(Registry.Manifest Assets,Viewer.PlacedSceneState Scene,Park.State Park,ParkSim.State Sim);
    public override void _Ready()
    {
        Viewer a=null,b=null; string file=System.IO.Path.Combine(OS.GetUserDataDir(),"placed-scene-smoke.sav");
        try {
            using var lib=new AssetLibrary("/home/ec2-user/tpw-ps2/tpw_ps2.bin");
            lib.OpenWad("/DATA/JUNGLE.WAD");
            var asset=lib.Rides.First(x=>x.Name.EndsWith("/monkey.mps",StringComparison.OrdinalIgnoreCase));
            var mesh=new Model(lib.Read(asset.Model)); var anim=new Aps(lib.Read(asset.Animation));
            var model=new AnimatedModel(mesh,anim,anim.Records().First(r=>r.Slot==5),_=>(null,false));
            model.SetFrame(13.25f); model.Root.Visible=false;
            var holder=new Node3D {Name="RetainedHolder"};
            holder.SetMeta("represented_activation_serial",123L);
            holder.AddChild(new Node3D {Name="before",Position=new Vector3(1,2,3)});
            holder.AddChild(model.Root);
            holder.AddChild(new Node3D {Name="after",Visible=false});
            var park=new Park(); park.Build(8,8);
            Check(park.TryPlace(holder,new Park.Footprint(1,1,new bool[,]{{true}},-1,-1),41,"ride",2,2),"place fixture");
            // Only source constructs/runs Create. Loaded simulation below hydrates from the FILE.
            ParkSim Sim(out ParkRide ride) {
                var s=new ParkSim(null);
                ride=s.Add(709,"ride",new ParkCell(2,2),1,1,lib.Read(asset.Script),anim,1,null,null,out var fault);
                Check(ride!=null,"disc RSE fixture: "+fault); return s;
            }
            var sim=Sim(out var ride); a=new Viewer(); var ar=a.PlacedSmokeRegistry(park,ride,sim);
            a.PlacedSmokeSeed(ride,model,mesh,anim);
            var modelOrigin=new Registry.Origin(lib.WadName,asset.Model.Path,Registry.AssetKind.Model);
            var animOrigin=new Registry.Origin(lib.WadName,asset.Animation.Path,Registry.AssetKind.Animation);
            var scriptOrigin=new Registry.Origin(lib.WadName,asset.Script.Path,Registry.AssetKind.Program);
            var policy=new[]{modelOrigin,animOrigin,scriptOrigin};
            using var registry=Registry.Capture("/home/ec2-user/tpw-ps2/tpw_ps2.bin",policy);
            string modelId=registry.RegisterModel(modelOrigin,mesh),animId=registry.RegisterAnimation(animOrigin,anim);
            string programId=registry.RegisterProgram(scriptOrigin,ride.Machine.Program,lib.Read(asset.Script));
            ParkSim.StateBindings SimBindings(Registry assets)=>new(){Scripts=new(){
                // This source was added without a sibling catalogue. Preserve that NULL scope,
                // don't enable spawning just because the cold loader knows a directory.
                IdentifyProgram=p=>new(assets.Identify(p),p,null),ResolveProgram=id=>new(id,assets.SharedProgram(id),null),
                IdentifyAnimation=_=>animId,ResolveAnimation=assets.SharedAnimation}};
            Viewer.PlacedSceneBindings Bind(Model m,Aps an)=>new() {
                IdentifyRenderer=_=>new(){ModelAssetId=modelId,AnimationAssetId=animId,Model=m,Animation=an,Texture=_=>(null,false)},
                ResolveRenderer=_=>new(){ModelAssetId=modelId,AnimationAssetId=animId,Model=m,Animation=an,Texture=_=>(null,false)},
                IdentifyMesh=_=>modelId,ResolveMesh=id=>id==modelId?m:throw new Exception("mesh ID"),
                IdentifyAnimation=_=>animId,ResolveAnimation=id=>id==animId?an:throw new Exception("animation ID") };
            var bindings=Bind(mesh,anim);
            var state=a.CapturePlacedScene(ar,bindings);
            Check(state.Scripted.Single().RuntimeId==709 && state.Placements.Single().PlacedId==41,"different ID namespaces");
            var ps=park.CaptureState(new(){IdentifyNode=(id,n)=>"placement/"+id});
            ParkSaveFile.Write(file,new FileCut(registry.Export(),state,ps,sim.CaptureState(SimBindings(registry))));
            var disk=ParkSaveFile.Read<FileCut>(file);
            using var cold=Registry.Open("/home/ec2-user/tpw-ps2/tpw_ps2.bin",policy,disk.Assets);
            var fresh=Bind(cold.PrivateModel(modelId),cold.SharedAnimation(animId));
            using var stage=Viewer.StagePlacedScene(disk.Scene,fresh);
            var restored=Park.FromState(disk.Park,new(){BuildNode=key=>key=="placement/41"?stage.TakePlacement(41):throw new Exception("factory key")});
            var sim2=ParkSim.FromState(disk.Sim,null,SimBindings(cold));var ride2=sim2.Rides.Single(); b=new Viewer(); var br=b.PlacedSmokeRegistry(restored,ride2,sim2);
            b.CompletePlacedScene(stage,br,fresh);
            Check(Json(state)==Json(b.CapturePlacedScene(br,fresh)),"FILE retained state and holder child order");
            Check(!ReferenceEquals(a.PlacedSmokeModel(),b.PlacedSmokeModel()),"fresh renderer");
            for(int i=0;i<6;i++) {
                sim.Advance(.04);sim2.Advance(.04);
                Check(Json(sim.CaptureState(SimBindings(registry)))==Json(sim2.CaptureState(SimBindings(cold))),"cold simulation continuation "+i);
                model.SetFrame(14.5f+i); b.PlacedSmokeModel().SetFrame(14.5f+i);
                Check(Json(a.CapturePlacedScene(ar,bindings))==Json(b.CapturePlacedScene(br,fresh)),"future render sample "+i);
            }
            a.PlacedSmokeRefusal(()=>a.CapturePlacedScene(ar,bindings));
            var extra=new MeshInstance3D(); holder.AddChild(extra);
            bool refused=false;try { a.CapturePlacedScene(ar,bindings); } catch(InvalidDataException) { refused=true; }
            Check(refused,"unowned static child cannot disappear"); extra.Free();
            GD.Print("VIEWER PLACED SCENE SAVE PASS: cold asset manifest+CORE+placement+scene FILE (no load Create), activation metadata, 6 future samples; explicit track/coaster/unknown-child refusal. NOT full world.");
            GetTree().Quit();
        } catch(Exception e) { GD.PrintErr("VIEWER PLACED SCENE SAVE FAIL: "+e);GetTree().Quit(2); }
        finally { if(System.IO.File.Exists(file))System.IO.File.Delete(file); a?.PlacedSmokeDispose();b?.PlacedSmokeDispose(); }
    }
}
}
