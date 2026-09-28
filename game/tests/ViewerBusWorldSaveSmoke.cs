using Godot;
using System.Reflection;
using System.Text.Json;
using TPW.PS2.Data;
using Aps=TPW.PS2.Data.Animation;
namespace TPWPS2Viewer.Tests;

public partial class ViewerBusWorldSaveSmoke : Node
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Set(Viewer v,string n,object x)=>typeof(Viewer).GetField(n,Flags).SetValue(v,x);
    static T Get<T>(Viewer v,string n)=>(T)typeof(Viewer).GetField(n,Flags).GetValue(v);
    static string Json(object x)=>JsonSerializer.Serialize(x);
    int checks;
    void Check(bool b,string why){if(!b)throw new Exception(why);checks++;}
    void Reject(Action action,string why){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception(why);}
    static Viewer.WorldCoreRegistry Registry(Viewer v)=>new(){Owner=v,Sim=new ParkSim(null)};
    public override void _Ready()
    {
        try {
            using var lib=new AssetLibrary("/home/ec2-user/tpw-ps2/tpw_ps2.bin");
            foreach(string world in new[]{"JUNGLE","HALLOW","SPACE","FANTASY"}) {
                lib.OpenWad("/DATA/"+world+".WAD");
                foreach(string terrain in new[]{"terrain_1","terrain_2"}) {
                    var catalogue=NativeBusCatalogue.Read(lib.Disc,NativeParkSelection.Ordinary(lib.WadName,terrain));
                    string stem=catalogue.Selection.BusStem;
                    var asset=lib.Rides.Single(x=>x.Name.Equals($"features/{stem}/{stem}.mps",StringComparison.OrdinalIgnoreCase));
                    Run(new Model(lib.Read(asset.Model)),new Aps(lib.Read(asset.Animation)),catalogue,world+"/"+stem);
                }
            }
            GD.Print($"VIEWER BUS WORLD SAVE PASS: {checks} checks; eight real bus assets; actual fields/new callbacks; physical audio rejected");
            GetTree().Quit();
        } catch(Exception e){GD.PrintErr("VIEWER BUS WORLD SAVE FAIL: "+e);GetTree().Quit(2);}
    }
    void Run(Model model,Aps animation,NativeBusCatalogue catalogue,string id)
    {
        var source=new Viewer();var target=new Viewer();var empty=new Viewer();
        var oldNode=new Node3D();var newNode=new Node3D();NativeBus original=null;Node3D restoredRoot=null;
        try {
            var sr=Registry(source);var tr=Registry(target);
            var bindings=new Viewer.BusStateBindings {CatalogueAssetId=id+"/catalogue",Catalogue=catalogue,
                ModelAssetId=id+".mps",Model=model,AnimationAssetId=id+".aps",Animation=animation,Texture=_=>(null,false)};
            var nil=empty.CaptureBusState(Registry(empty),bindings);
            Check(nil.Bus==null&&nil.CatalogueAssetId==null&&nil.Placements.Length==0,"actual null preserved");
            original=new NativeBus(model,animation,_=>(null,false),17,0,(_,_)=>{},_=>{});
            Set(source,"_nativeBus",original);Set(source,"_nativeBusMesh",model);Set(source,"_busCatalogue",catalogue);
            Set(source,"_busLoadFailed",true);Set(source,"_busSourceKey",id);Set(source,"_busTraffic",7);
            Set(source,"_busParameterStamp",31u);Set(source,"_busBatches",5);
            sr.Register("fixture/node",oldNode);tr.Register("fixture/node",newNode);
            var ride=new ParkRide{Id=71};var copyRide=new ParkRide{Id=71};
            ((List<ParkRide>)sr.Sim.Rides).Add(ride);((List<ParkRide>)tr.Sim.Rides).Add(copyRide);sr.Register("fixture/ride",ride);tr.Register("fixture/ride",copyRide);
            var placements=Get<List<(Node3D Node,int RuntimeId,RideDefinition Definition)>>(source,"_busPlacements");
            placements.Add((oldNode,71,null));placements.Add((null,-9,null));placements.Add((oldNode,71,null));
            uint clock=0;
            while(!original.Controller.Active&&clock<200000){original.Update(clock,0x4000,0,true,true,0);clock+=40;}
            Check(original.Controller.Active,"nonvacuous bind");
            Reject(()=>source.CaptureBusState(sr,bindings),"unsampled bind safe point must reject");
            original.Present(clock,.5f);
            var runtime=source.CaptureRuntimeState();
            var state=source.CaptureBusState(sr,bindings);
            string file=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"bus-world-"+Guid.NewGuid()+".sav");
            try{ParkSaveFile.Write(file,state);state=ParkSaveFile.Read<Viewer.BusState>(file);}finally{System.IO.File.Delete(file);}
            target.RestoreRuntimeState(runtime);
            Reject(()=>target.StageBusState(state with {CatalogueFingerprint="wrong"},tr,bindings),"wrong catalogue accepted");
            target.StageBusState(state,tr,bindings);
            Check(ReferenceEquals(Get<NativeBusCatalogue>(target,"_busCatalogue"),catalogue),"catalogue installed before entrance services");
            Check(Json(target.CaptureRuntimeState())==Json(runtime),"no RNG or runtime command during stage");
            restoredRoot=target.CompleteBusStateJoin(tr);
            Check(restoredRoot.GetParent()==null,"unpublished root");
            Check(Json(state)==Json(target.CaptureBusState(tr,bindings)),"exact actual adapter/placement roundtrip");
            var restored=Get<NativeBus>(target,"_nativeBus");
            Check(!ReferenceEquals(restored,original),"new adapter");
            foreach(string callback in new[]{"stateCommand","requestBatch"}) {
                var d=(Delegate)typeof(NativeBusController).GetField(callback,Flags).GetValue(restored.Controller);
                Check(ReferenceEquals(d.Target,target),"controller callback bound to NEW Viewer: "+callback);
            }
            Check(ReferenceEquals(Get<List<(Node3D Node,int RuntimeId,RideDefinition Definition)>>(target,"_busPlacements")[0].Node,newNode),"new node identity");
            // Trigger a real controller state transition: the target's own callback must
            // alter target scalars, never the source or its intentionally inert delegates.
            restored.Update(100000,0x4000,0,true,true,0);restored.Present(100000);
            Check(Json(source.CaptureRuntimeState())==Json(runtime),"new callbacks never touch source");
            // Explicit missing physical-owner policy, not an inferred audio restart.
            Set(source,"_busAudioLive",true);
            Reject(()=>source.CaptureBusState(sr,bindings),"live audio without owner accepted");
            GD.Print("VIEWER BUS WORLD SAVE "+id+": PASS");
        } finally {restoredRoot?.Free();original?.Root.Free();oldNode.Free();newNode.Free();foreach(var v in new[]{source,target,empty}){
                foreach(var root in new[]{Get<Weather>(v,"_weather").Root,Get<EntranceFlags>(v,"_flags").Root,Get<ThoughtBubbles>(v,"_thoughts").Root})if(root.GetParent()==null)root.Free();v.Free();}}
    }
}
