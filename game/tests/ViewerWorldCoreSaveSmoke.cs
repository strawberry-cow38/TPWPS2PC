using Godot;
using System.Reflection;
using System.Collections;
using Aps=TPW.PS2.Data.Animation;
using System.Text.Json;
using TPW.PS2.Data;
namespace TPWPS2Viewer.Tests;
public partial class ViewerWorldCoreSaveSmoke:Node
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static T F<T>(Viewer v,string n)=>(T)typeof(Viewer).GetField(n,Flags).GetValue(v);
    static void Set(Viewer v,string n,object x)=>typeof(Viewer).GetField(n,Flags).SetValue(v,x);
    static string Json(object o)=>JsonSerializer.Serialize(o);
    int checks;void C(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Free(Viewer v){foreach(var root in new[]{F<Weather>(v,"_weather").Root,F<EntranceFlags>(v,"_flags").Root,F<ThoughtBubbles>(v,"_thoughts").Root})if(root.GetParent()==null)root.Free();v.Free();}
    public override void _Ready(){var viewers=new List<Viewer>();try{
        using var lib=new AssetLibrary("/home/ec2-user/tpw-ps2/tpw_ps2.bin");
        var file=lib.Disc.Files().Single(f=>f.Path=="/DATA/SPACE.WAD");
        var wad=new WadArchive(lib.Disc.Read(file.Extent,file.Size));var scene=new VisitorScenario(wad,"SPACE");
        byte[] script=wad.Read(wad.Find(scene.RideStem+".rse"));var program=new RseProgram(script);
        var scripts=new ParkSim.ScriptedBindings {
            IdentifyProgram=p=>new("ride-script",p,null),ResolveProgram=k=>k=="ride-script"?new(k,program,null):throw new Exception(k),
            IdentifyAnimation=_=>"ride-aps",ResolveAnimation=k=>k=="ride-aps"?scene.RideAnimation:throw new Exception(k),
            IdentifyDefinition=d=>ReferenceEquals(d,scene.Definition)?"ride-def":throw new Exception(),ResolveDefinition=k=>k=="ride-def"?scene.Definition:throw new Exception(k)};
        Viewer New(){var v=new Viewer();viewers.Add(v);return v;}
        var source=New();var paths=scene.Simulation.Paths;var sim=new ParkSim(paths);var walk=new GuestWalk(paths);
        var visitors=new ParkVisitors(sim,walk){Needs=new VisitorNeeds(41),AutoService=false};
        var head=scene.Simulation.QueueCells[0];var away=head.Offset(0,6);
        var ride=sim.Add(91,scene.Definition.Name,scene.RideOrigin,scene.RideWidth,scene.RideHeight,script,scene.RideAnimation,
            scene.Definition.UpgradeCapacity(0)??1,head,scene.Simulation.ExitPortal,out var fault,
            headSlots:scene.RideModel.Fittings.Count(f=>(f.Flags&0x80)!=0),definition:scene.Definition);
        C(ride!=null&&fault==null,"real disc ride");sim.SetOpen(91,true);
        Set(source,"_walkGrid",paths);Set(source,"_sim",sim);Set(source,"_guests",walk);Set(source,"_visitors",visitors);
        var clock=F<ParkClock>(source,"_calendar");var serials=new NativeActivationSequence(0,"world-smoke");
        Set(source,"_nativeActivations",serials);
        var calls=new Dictionary<Viewer,int>();var randoms=new Dictionary<Viewer,Func<int,int>>();
        Func<int,int> Rand(Viewer v){if(randoms.TryGetValue(v,out var x))return x;var rng=F<SnapshotRandom>(v,"_guestRng");return randoms[v]=n=>{calls[v]=calls.GetValueOrDefault(v)+1;return rng.Next(n);};}
        var staff=new ParkStaff(visitors,clock,serials,Rand(source));visitors.Staff=staff;
        Set(source,"_staff",staff);Set(source,"_staffVisitors",visitors);
        var management=new ParkManagement(clock,F<ParkAwards>(source,"_awards")){Staff=staff,Finances=sim.Finances};Set(source,"_management",management);
        foreach(var kind in new[]{StaffKind.Handyman,StaffKind.Mechanic}){var member=staff.Hire(kind,0);C(member!=null&&staff.Drop(member,away),"hire fixture");}
        staff.Litter.Drop(new NativeGuestMotion.Point((short)(away.X*256),(short)(away.Z*256)),false);
        var rider=visitors.Arrive(head,head);visitors.SendTo(rider,ride);visitors.Arrive(away,away.Offset(2,0));
        for(int i=0;i<8000&&ride.Machine.GuestIds.Count==0;i++)visitors.Step(.04,()=>away);
        C(ride.Machine.GuestIds.Count>0,"nonvacuous saved rider aboard");visitors.Step(.013,()=>away);
        Viewer.WorldCoreBindings Bind(Viewer v)=>new(){TerrainKey="SPACE/terrain_1",Terrain=scene.Terrain,Simulation=new(){Scripts=scripts},
            CaptureProviders=new Dictionary<string,object>{{"guest-rng",Rand(v)}},
            ProviderValues=new Dictionary<string,JsonElement>{{"guest-rng",JsonSerializer.SerializeToElement("runtime/GuestRandom")}},
            RestoreProvider=(id,registry,values)=>id=="guest-rng"&&values[id].GetString()=="runtime/GuestRandom"?Rand(registry.Owner):throw new Exception(id)};
        var bindings=Bind(source);var saved=source.CaptureWorldCoreState(bindings,out var sourceRegistry);string frozen=Json(saved);
        string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"tpw-world-"+Guid.NewGuid()+".save");
        try{ParkSaveFile.Write(path,saved);saved=ParkSaveFile.Read<Viewer.WorldCoreState>(path);}finally{System.IO.File.Delete(path);}
        var target=New();int originalCalls=calls[source];var restored=target.RestoreWorldCoreState(saved,bindings);
        C(restored.Hydrated&&calls.GetValueOrDefault(target)==0&&calls[source]==originalCalls,"allocation+hydrate no random replay");
        C(ReferenceEquals(restored.Staff.Routes,restored.Walk.NativeRoutes)&&ReferenceEquals(restored.Staff.Litter.Activations,restored.Activations),"shared route pool and serial owner");
        var roundtrip=target.CaptureWorldCoreState(Bind(target),out var targetRegistry);
        C(Json(saved)==Json(roundtrip),"actual Viewer core FILE roundtrip");
        C(!ReferenceEquals(restored.Sim,sim)&&!ReferenceEquals(restored.Staff,staff)&&!ReferenceEquals(restored.Walk,walk),"fresh logical owners");
        int before=visitors.Rides;
        for(int tick=0;tick<1000;tick++){
            if(tick==40){foreach(var v in new[]{source,target}){var s=F<ParkStaff>(v,"_staff");var m=s.Hire(StaffKind.Entertainer,0);C(m!=null&&s.Drop(m,away),"future shared guest random candidate draw");}}
            if(tick is 120 or 450){visitors.Arrive(away,away.Offset(1,0));restored.Visitors.Arrive(away,away.Offset(1,0));}
            double dt=tick%3==0?.017:tick%3==1?.023:.08;
            visitors.Step(dt,()=>away);restored.Visitors.Step(dt,()=>away);
            management.Advance(0x4000);restored.Management.Advance(0x4000);
            if(tick%20==0)C(Json(source.CaptureWorldCoreState(Bind(source),out _))==Json(target.CaptureWorldCoreState(Bind(target),out _)),"whole logical continuation tick "+tick);
        }
        C(visitors.Rides>before,"saved rider completes after load");C(Json(saved)==frozen,"frozen DTO unaffected by future world");
        var bad=New();bool rejected=false;try{bad.RestoreWorldCoreState(saved with {Rides=new[]{"guest/0"}},bindings);}catch(InvalidDataException){rejected=true;}
        C(rejected,"registry collision rejected rather than reusing different kind");
        C(Json(saved)==frozen,"failed stage cannot mutate file");
        // Exercise the actual two-way ride/mechanic link in the VIEWER registry, not only owner DTOs.
        var mechanic=(Mechanic)staff.Active(StaffKind.Mechanic).Single();
        C(mechanic.Dispatch(ride,false),"real dispatch before cyclic Viewer cut");
        var cycle=source.CaptureWorldCoreState(Bind(source),out _);var cycleViewer=New();
        var cycleRegistry=cycleViewer.RestoreWorldCoreState(cycle,Bind(source));
        var cycleRide=cycleRegistry.Sim.Rides.Single();var cycleMechanic=(Mechanic)cycleRegistry.Staff.Active(StaffKind.Mechanic).Single();
        C(ReferenceEquals(cycleRide.AssignedMechanic,cycleMechanic)&&ReferenceEquals(cycleMechanic.Target,cycleRide),"Viewer registry mechanic cycle uses only restored objects");
        for(int i=0;i<80;i++){
            visitors.Step(.04,()=>away);cycleRegistry.Visitors.Step(.04,()=>away);
            C(Json(source.CaptureWorldCoreState(Bind(source),out _))==Json(cycleViewer.CaptureWorldCoreState(Bind(cycleViewer),out _)),"Viewer cyclic continuation "+i);
        }
        ActorJoin(source,Bind(source),lib);
        GD.Print($"VIEWER WORLD CORE SAVE PASS: {checks} checks (logical core join, NOT complete Viewer)");GetTree().Quit();
    }catch(Exception e){GD.PrintErr("VIEWER WORLD CORE SAVE FAIL: "+e);GetTree().Quit(2);}finally{foreach(var v in viewers)Free(v);}}
    void ActorJoin(Viewer source,Viewer.WorldCoreBindings core,AssetLibrary lib)
    {
        lib.OpenWad("/DATA/DATA.WAD");
        var guestAsset=lib.Rides.First(r=>r.Name.EndsWith("/boy1a.mps",StringComparison.OrdinalIgnoreCase));
        var staffAsset=lib.Rides.First(r=>r.Name.Contains("handy",StringComparison.OrdinalIgnoreCase)&&r.Animation!=null);
        var litterAsset=lib.Rides.First(r=>r.Name.EndsWith("litter1.mps",StringComparison.OrdinalIgnoreCase));
        var models=new Dictionary<string,Viewer.GuestModelAsset>();var animations=new Dictionary<string,Viewer.GuestAnimationAsset>();
        void Asset(string key,AssetLibrary.RideAssets a){var model=new Model(lib.Read(a.Model));models.Add(key,new(a.Name,model));F<Dictionary<string,Model>>(source,"_charModels").Add(a.Name,model);
            if(a.Animation!=null){var aps=new Aps(lib.Read(a.Animation));string path=System.IO.Path.ChangeExtension(a.Name,".aps");animations.Add(key,new(path,aps));F<Dictionary<string,Aps>>(source,"_charAnims").Add(path,aps);}}
        Asset("guest",guestAsset);Asset("staff",staffAsset);Asset("litter",litterAsset);
        var table=NativeLogicalAnimationTable.Read(lib.Disc);Set(source,"_logicalAnimations",table);
        var root=new Node3D{Name="guests"};source.AddChild(root);Set(source,"_guestRoot",root);
        var staffRoot=new Node3D{Name="staff"};root.AddChild(staffRoot);Set(source,"_staffRoot",staffRoot);
        var selected=F<GuestWalk>(source,"_guests").Guests.Take(2).ToArray();C(selected.Length==2,"joined real guests present");
        foreach(var g in selected){var aps=animations["guest"].Animation;var records=aps.Records().ToArray();var walk=records.First(r=>r.Slot==1);var idle=records.First(r=>r.Slot==2);
            var draw=new AnimatedModel(models["guest"].Model,aps,walk,_=>(null,false));draw.SetFrame(3);
            var actor=new Node3D{Name="guest"+g.Id,Position=new(g.Id,0,2)};actor.AddChild(draw.Root);root.AddChild(actor);
            F<Dictionary<int,Node3D>>(source,"_actors").Add(g.Id,actor);
            F<Dictionary<int,(AnimatedModel,Aps.Record,string)>>(source,"_drawn").Add(g.Id,(draw,null,"own"));
            F<Dictionary<int,(Aps,Aps.Record,Aps.Record,Model)>>(source,"_walkRec").Add(g.Id,(aps,walk,idle,models["guest"].Model));
            F<Dictionary<int,Aps.Record[]>>(source,"_idles").Add(g.Id,records.Where(r=>r.Slot==2).ToArray());
        }
        var staff=F<ParkStaff>(source,"_staff");var member=staff.Active(StaffKind.Handyman).First();
        var apsStaff=animations["staff"].Animation;
        var staffRecords=apsStaff.Records().GroupBy(r=>r.Slot).SelectMany(gr=>gr.Select((r,i)=>(Key:(gr.Key,i),Record:r))).ToDictionary(x=>x.Key,x=>x.Record);
        var record=staffRecords[(0,0)];var renderer=new AnimatedModel(models["staff"].Model,apsStaff,record,_=>(null,false),skeletalHideLists:true);renderer.SetFrame(3);
        var staffNode=new Node3D{Name="staff-actor",Position=new(2,0,3)};staffNode.AddChild(renderer.Root);staffRoot.AddChild(staffNode);
        var native=new NativeGuestAnimation(table,(s,v)=>staffRecords.TryGetValue((s,v),out var r)?r.DurationFrames:null){Requested=9};
        var actorObject=Activator.CreateInstance(typeof(Viewer).GetNestedType("StaffActor",BindingFlags.NonPublic),true);
        foreach(var p in new Dictionary<string,object>{{"Member",member},{"Serial",member.Serial},{"Node",staffNode},{"Drawn",renderer},{"Anim",apsStaff},{"Animation",native},{"Records",staffRecords},{"Showing",record},{"ModelPath",staffAsset.Name},{"Prev",new Vector3(1,0,3)},{"Yaw",.8f}})actorObject.GetType().GetField(p.Key).SetValue(actorObject,p.Value);
        F<IDictionary>(source,"_staffActors").Add(member,actorObject);
        var litter=staff.Litter.Drop(new NativeGuestMotion.Point(384,384),false);
        var litterNode=new Node3D{Name="litter"};var litterDraw=new AnimatedModel(models["litter"].Model,null,null,_=>(null,false));litterDraw.SetFrame(0);litterNode.AddChild(litterDraw.Root);staffRoot.AddChild(litterNode);
        F<Dictionary<LitterItem,(Node3D,uint,int,string)>>(source,"_litterActors").Add(litter,(litterNode,litter.Serial,litter.ModelId,litterAsset.Name));
        F<Dictionary<Node3D,AnimatedModel>>(source,"_litterDrawn").Add(litterNode,litterDraw);
        staffRoot.MoveChild(litterNode,0);root.MoveChild(staffRoot,1);
        var owners=new Dictionary<Viewer,Dictionary<int,Guest>>{{source,selected.ToDictionary(g=>g.Id)}};
        var providers=new Dictionary<Viewer,IReadOnlyDictionary<string,object>>{{source,core.CaptureProviders}};
        Viewer.ActorWorldBindings Bind(Viewer owner)=>new(){Core=new(){TerrainKey=core.TerrainKey,Terrain=core.Terrain,Simulation=core.Simulation,ProviderValues=core.ProviderValues,CaptureProviders=providers[owner],RestoreProvider=core.RestoreProvider,GuestCacheOwners=owners[owner]},
            Characters=r=>{
                owners[r.Owner]=selected.Select(g=>g.Id).ToDictionary(id=>id,id=>r.GuestAtSlot(id));
                providers[r.Owner]=new Dictionary<string,object>{{"guest-rng",r.Resolve("provider/guest-rng")}};
                return new(){GuestAtSlot=r.GuestAtSlot,GuestId=r.GuestId,Guest=r.Guest,Models=models,Animations=animations,Textures=new Dictionary<string,Viewer.GuestTextureAsset>(),Texture=(_,_) =>(null,false),LogicalTable=table};},
            Staff=(r,c)=>new(){Staff=r.Staff,Visitors=r.Visitors,MemberId=r.MemberId,Member=r.Member,LitterId=r.LitterId,Litter=r.Litter,Characters=c,ModelRegistry=null}};
        var initial=Bind(source);var snapshot=source.CaptureActorWorld(initial);string frozen=Json(snapshot);
        string file=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"tpw-actor-world-"+Guid.NewGuid()+".sav");
        try{ParkSaveFile.Write(file,snapshot);snapshot=ParkSaveFile.Read<Viewer.ActorWorldState>(file);}finally{System.IO.File.Delete(file);}
        using var stage=Viewer.StageActorWorld(snapshot,initial);var target=stage.Viewer;
        C(Json(target.CaptureActorWorld(Bind(target)))==frozen,"joined core+guest+staff FILE identity roundtrip");
        C(F<Node3D>(target,"_staffRoot").GetParent()==F<Node3D>(target,"_guestRoot"),"staff root retained beneath guest root");
        C(F<Dictionary<Node3D,AnimatedModel>>(target,"_litterDrawn").Count==1,"litter renderer is an owned live root");
        for(int step=0;step<24;step++){
            foreach(var v in new[]{source,target}){
                Set(v,"_parkTicks",1000+step);
                foreach(var g in selected)typeof(Viewer).GetMethod("Gait",Flags).Invoke(v,new object[]{g.Id,step%6<3,.35f});
                var s=F<ParkStaff>(v,"_staff");var m=s.Active(StaffKind.Handyman).First();var a=F<IDictionary>(v,"_staffActors")[m];
                var n=(NativeGuestAnimation)a.GetType().GetField("Animation").GetValue(a);n.Push();n.Update(40,F<NewlibRand>(v,"_staffAnimationRand").Next);
                typeof(Viewer).GetMethod("PoseStaff",Flags).Invoke(v,new object[]{a,.35f});
                F<ParkVisitors>(v,"_visitors").Step(.04,()=>new ParkCell(20,20));
            }
            C(Json(source.CaptureActorWorld(Bind(source)))==Json(target.CaptureActorWorld(Bind(target))),"joined simulation/guest gait/staff pose continuation "+step);
        }
        C(Json(snapshot)==frozen,"joined checkpoint immutable");
        string sourceBefore=Json(source.CaptureActorWorld(Bind(source)));
        bool rejected=false;try{using var invalid=Viewer.StageActorWorld(snapshot with{Staff=snapshot.Staff with{Actors=snapshot.Staff.Actors.Select(x=>x with{Member="missing-member"}).ToArray()}},initial);}catch(InvalidDataException){rejected=true;}
        C(rejected&&Json(source.CaptureActorWorld(Bind(source)))==sourceBefore,"late staff failure discards stage and leaves source world unchanged");
    }

}
