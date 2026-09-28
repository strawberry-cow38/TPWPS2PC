using Godot;
using System.Reflection;
using System.Text.Json;
using TPW.PS2.Data;
namespace TPWPS2Viewer.Tests;

// Real Viewer field cut, using its production route adapter/mailbox and service factories.
// Includes the actual bus adapter and production prior -> entrance -> queue tick chain.
public partial class ViewerNativeWorldSaveSmoke : Node
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static T F<T>(Viewer v,string n)=>(T)typeof(Viewer).GetField(n,Flags).GetValue(v);
    static void Set(Viewer v,string n,object x)=>typeof(Viewer).GetField(n,Flags).SetValue(v,x);
    static T Call<T>(Viewer v,string n)=>(T)typeof(Viewer).GetMethod(n,Flags).Invoke(v,null);
    static string Json(object x)=>JsonSerializer.Serialize(x);
    static void Check(bool b,string why){if(!b)throw new Exception(why);}
    public override void _Ready()
    {
        var source=new Viewer();var target=new Viewer();
        try {
            using var lib=new AssetLibrary("/home/ec2-user/tpw-ps2/tpw_ps2.bin");
            var f=lib.Disc.Files().Single(x=>x.Path=="/DATA/SPACE.WAD");
            var wad=new WadArchive(lib.Disc.Read(f.Extent,f.Size));var scene=new VisitorScenario(wad,"SPACE");
            var catalogue=NativeBusCatalogue.Read(lib.Disc,new(3,0));
            var paths=scene.Simulation.Paths;var sim=new ParkSim(paths);var walk=new GuestWalk(paths);
            var visitors=new ParkVisitors(sim,walk){Needs=new VisitorNeeds(41),AutoService=false};
            Set(source,"_walkGrid",paths);Set(source,"_sim",sim);Set(source,"_guests",walk);Set(source,"_visitors",visitors);
            source.BindNativeControllerAssets(catalogue,null);
            lib.OpenWad("/DATA/SPACE.WAD");string stem=catalogue.Selection.BusStem;
            var asset=lib.Rides.Single(x=>x.Name.Equals($"features/{stem}/{stem}.mps",StringComparison.OrdinalIgnoreCase));
            var model=new Model(lib.Read(asset.Model));var animation=new TPW.PS2.Data.Animation(lib.Read(asset.Animation));
            var command=(Action<int,uint>)typeof(Viewer).GetMethod("NativeBusState",Flags).CreateDelegate(typeof(Action<int,uint>),source);
            var arrival=(Action<int>)typeof(Viewer).GetMethod("AdmitBusBatch",Flags).CreateDelegate(typeof(Action<int>),source);
            var bus=new NativeBus(model,animation,_=>(null,false),17,0,command,arrival);bus.Present(0);
            Set(source,"_nativeBus",bus);Set(source,"_nativeBusMesh",model);
            Set(source,"_nativeActivations",new NativeActivationSequence(0,"native-world-smoke"));
            var flow=new NativeEntranceFlow(visitors,Call<NativeEntranceFlow.Services>(source,"CreateWorldEntranceServices"));
            var queues=new NativeRideQueues(visitors,Call<NativeRideQueues.Services>(source,"CreateWorldQueueServices"));
            Set(source,"_entranceFlow",flow);Set(source,"_entranceWalk",walk);Set(source,"_entranceVisitors",visitors);
            Set(source,"_rideQueues",queues);Set(source,"_rideQueueWalk",walk);Set(source,"_rideQueueVisitors",visitors);
            Action<uint> entranceHook=Call<Action<uint>>(source,"CreateWorldEntranceTickHook");
            Action<uint> queueHook=Call<Action<uint>>(source,"CreateWorldQueueTickHook");
            Set(source,"_entranceTickHook",entranceHook);Set(source,"_rideQueueTickHook",queueHook);
            Set(source,"_rideQueuePriorTick",entranceHook);walk.BeforeStep=queueHook;
            visitors.NativeDeparture=g=>flow.TryDepart(g);
            visitors.NativeQueueMouth=r=>queues.Mouth(r);visitors.NativeQueueArrival=(g,r)=>queues.Arrive(g,r);
            var start=scene.Simulation.QueueCells[0];
            var guest=visitors.Arrive(start,start);flow.Add(guest,15,1);
            flow.Tick(1,2,0);
            byte[] script=wad.Read(wad.Find(scene.RideStem+".rse"));var program=new RseProgram(script);
            var ride=sim.Add(91,scene.Definition.Name,scene.RideOrigin,scene.RideWidth,scene.RideHeight,script,scene.RideAnimation,
                scene.Definition.UpgradeCapacity(0)??1,start,scene.Simulation.ExitPortal,out var fault,
                headSlots:scene.RideModel.Fittings.Count(x=>(x.Flags&0x80)!=0),definition:scene.Definition);
            Check(ride!=null&&fault==null,"native queue real ride");sim.SetOpen(91,true);
            var shape=new NativeQueueShape(start,scene.Simulation.QueueCells.ToArray(),0);
            F<Dictionary<ParkRide,NativeQueueShape>>(source,"_queueShapes").Add(ride,shape);Set(source,"_queueShapesTick",0);
            var queued=visitors.Arrive(start,start);visitors.SendTo(queued,ride);Check(queues.Arrive(queued,ride),$"actual native ride queue arrival takes={visitors.Takes(ride)} closed={ride.Get("VAR_RIDECLOSED")} broken={ride.Get("VAR_BROKEN")} let={ride.Has("VAR_LETMEON")} spot={NativeQueueSpots.Spot(shape,0)} fault={ride.Fault}");
            var scripts=new ParkSim.ScriptedBindings {
                IdentifyProgram=p=>new("ride-rse",p,null),ResolveProgram=k=>new(k,program,null),
                IdentifyAnimation=_=>"ride-aps",ResolveAnimation=_=>scene.RideAnimation,
                IdentifyDefinition=_=>"ride-def",ResolveDefinition=_=>scene.Definition };
            var bindings=new Viewer.WorldCoreBindings {TerrainKey="SPACE/terrain_1",Terrain=scene.Terrain,Bus=_=>new(){CatalogueAssetId="SPACE/catalogue",Catalogue=catalogue,ModelAssetId="bus/model",Model=model,AnimationAssetId="bus/aps",Animation=animation,Texture=_=>(null,false)},
                Simulation=new(){Scripts=scripts},BindNativeControllerProviders=r=>r.Owner.BindNativeControllerAssets(catalogue,null)};
            var saved=source.CaptureWorldCoreState(bindings,out var oldRegistry);
            Check(saved.NativeControllers.Entrance.Entries.Any(x=>x.Token!=null),"pending route required");
            Check(saved.Walk.Guests.Any(g=>g.NativeLease!=null),"live native lease required");
            Check(saved.NativeControllers.Queues.Members.Length>0,"NONVACUOUS occupied ride queue saved");
            string file=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"tpw-native-world-"+Guid.NewGuid()+".save");
            try{ParkSaveFile.Write(file,saved);saved=ParkSaveFile.Read<Viewer.WorldCoreState>(file);}finally{System.IO.File.Delete(file);}
            var restored=target.RestoreWorldCoreState(saved,bindings);
            Check(Json(saved)==Json(target.CaptureWorldCoreState(bindings,out _)),"exact JSON roundtrip/no replay");
            var fresh=restored.Guest(oldRegistry.Id(guest));var freshFlow=F<NativeEntranceFlow>(target,"_entranceFlow");
            var lease=saved.Walk.Guests.Single(g=>g.GraphId==saved.NativeControllers.Entrance.Entries.First().GuestGraphId).NativeLease;
            Check(!ReferenceEquals(guest,fresh)&&ReferenceEquals(restored.Resolve(lease.OwnerId),freshFlow.StateOwner)
                &&ReferenceEquals(restored.Resolve(lease.InputsId),freshFlow.StateInputs(fresh)),"fresh shared lease identity");
            Check(ReferenceEquals(F<Action<uint>>(target,"_rideQueuePriorTick"),F<Action<uint>>(target,"_entranceTickHook")),"hook chain identity");
            for(uint tick=2;tick<120;tick++) {
                // Invoke the production callback chain via the real walk clock, not direct
                // calls that could pass even when BeforeStep is unbound or duplicated.
                foreach(var v in new[]{source,target}){
                    var adapter=F<NativeBus>(v,"_nativeBus");adapter.Update(tick*40,0x4000,F<int>(v,"_busTraffic"),true,true,0);adapter.Present(tick*40);
                    F<GuestWalk>(v,"_guests").Step();
                }
                Check(Json(source.CaptureWorldCoreState(bindings,out _))==Json(target.CaptureWorldCoreState(bindings,out _)),"continued native requests "+tick);
            }
            GD.Print("VIEWER NATIVE WORLD SAVE PASS (FILE pending entrance/lease, actual bus and tick-chain continuation; occupied ride queue)");GetTree().Quit();
        } catch(Exception e){GD.PrintErr("VIEWER NATIVE WORLD SAVE FAIL: "+e);GetTree().Quit(2);}
        finally {
            foreach(var v in new[]{source,target}) {
                foreach(var root in new[]{F<Weather>(v,"_weather").Root,F<EntranceFlags>(v,"_flags").Root,F<ThoughtBubbles>(v,"_thoughts").Root,F<NativeBus>(v,"_nativeBus")?.Root})if(root!=null&&root.GetParent()==null)root.Free();
                v.Free();
            }
        }
    }
}
