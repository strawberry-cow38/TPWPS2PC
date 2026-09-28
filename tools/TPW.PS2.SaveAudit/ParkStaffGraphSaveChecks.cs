using System.Text.Json;
using TPW.PS2.Data;

public static class ParkStaffGraphSaveChecks
{
    static string Json<T>(T value)=>JsonSerializer.Serialize(value);
    public sealed record Bundle(ParkPaths.State Paths,GuestWalk.State Walk,ParkVisitors.State Visitors,
        VisitorNeeds.State Needs,ParkStaff.State Staff,ParkClock.State Clock,NativeActivationSequence.State Activations,
        ParkSim.ScriptedState Sim,ParkManagement.State Management,ParkAwards.State Awards);
    sealed class World
    {
        internal ParkPaths Paths=null!;internal GuestWalk Walk=null!;internal ParkSim Sim=null!;
        internal ParkVisitors Visitors=null!;internal VisitorNeeds Needs=null!;internal ParkStaff Staff=null!;
        internal ParkManagement Management=null!;internal ParkAwards Awards=new();
        internal ParkClock Clock=new();internal NativeActivationSequence Activations=new(13,"staff-graph");
        internal readonly List<string> Events=new();
        internal readonly Dictionary<string,object> External=new();
        internal World() { External["sound"]=(Action<StaffMember,int,int>)((m,b,e)=>Events.Add($"s/{m.Kind}/{m.PoolSlot}/{b}/{e}"));
            External["advisor"]=(Action<int>)(m=>Events.Add("a/"+m));
            External["litter-added"]=(Action<LitterItem>)(i=>Events.Add("l/"+i.PoolSlot));
            External["litter-removed"]=(Action<LitterItem>)(i=>Events.Add("r/"+i.PoolSlot)); }
        internal string Id(object o) {
            if(ReferenceEquals(o,Staff))return "staff";
            if(ReferenceEquals(o,Clock))return "clock";
            if(ReferenceEquals(o,Awards))return "awards";
            if(ReferenceEquals(o,Sim.Finances))return "finances";
            if(ReferenceEquals(o,Needs))return "needs";
            if(o is ParkRide ride&&Sim.Rides.Contains(ride))return "ride/"+ride.Id;
            if(o is StaffMember m&&ReferenceEquals(m,Staff.StateMember(m.Kind,m.PoolSlot)))return $"member/{(int)m.Kind}/{m.PoolSlot}";
            if(o is Delegate d){if(d.Equals(Staff.StatePaused))return "paused";
                if(d.Equals(Visitors.StateForwardSound))return "forward-sound";
                if(d.Equals(Visitors.StateNearbyLitter))return "nearby-litter";}
            foreach(var p in External)if(Equals(p.Value,o))return p.Key;
            throw new ArgumentException("Unknown staff graph binding "+o.GetType());
        }
        internal object Resolve(string id)=>id.StartsWith("ride/")?Sim.Rides.Single(r=>"ride/"+r.Id==id):
            id.StartsWith("member/")?Staff.StateMember((StaffKind)int.Parse(id.Split('/')[1]),int.Parse(id.Split('/')[2])):id switch {"staff"=>Staff,"clock"=>Clock,"awards"=>Awards,"finances"=>Sim.Finances,"needs"=>Needs,"paused"=>Staff.StatePaused,
            "forward-sound"=>Visitors.StateForwardSound,"nearby-litter"=>Visitors.StateNearbyLitter,_=>External[id]};
        internal GuestWalk.GuestGraph Graph()=>Walk.CaptureGraph("grid",new(){IdentifyPaused=p=>Id(p)},Visitors.ReferencedGuests);
        internal ParkStaff.StateBindings Bind(GuestWalk.GuestGraph g)=>new(){GuestGraph=g,PathsKey="grid",ClockKey="clock",
            ActivationsKey="activations",IdentifyReference=Id,ResolveReference=Resolve};
        internal ParkVisitors.StateBindings VisitorBind(GuestWalk.GuestGraph g)=>new(){GuestGraph=g,IdentifyReference=Id,ResolveReference=Resolve};
        internal VisitorNeeds.StateBindings NeedBind()=>new(){IdentifySounded=s=>Id(s),IdentifyNearbyLitter=s=>Id(s),
            ResolveSounded=id=>(Action<int,int>)Resolve(id),ResolveNearbyLitter=id=>(Func<int,(int Plain,int Vomit)>)Resolve(id)};
        internal ParkStaff.State Save()=>Staff.CaptureState(Bind(Graph()));
        internal void Tick(){Visitors.Step(.04,()=>new(5,5));Management.Advance(0x4000);}
    }
    static readonly byte[] Script=NativeRideQueueSaveChecks.Script();
    static ParkSim.ScriptedBindings Scripts()=>new(){IdentifyProgram=p=>new("fixture",p,null!),
        ResolveProgram=id=>id=="fixture"?new(id,new RseProgram(Script),null!):throw new ArgumentException(),
        IdentifyAnimation=_=>"headless",ResolveAnimation=_=>null!};
    static World New(bool hire=true)
    {
        var w=new World{Paths=new(StaffMemberSaveChecks.Terrain())};w.Walk=new(w.Paths);w.Sim=new(w.Paths);
        w.Visitors=new(w.Sim,w.Walk){AutoService=false};w.Needs=new();w.Visitors.Needs=w.Needs;
        w.Staff=new(w.Visitors,w.Clock,w.Activations);w.Visitors.Staff=w.Staff;
        w.Management=new(w.Clock,w.Awards){Staff=w.Staff,Finances=w.Sim.Finances,Advisor=(Action<int>)w.External["advisor"]};
        w.Clock.Set(29,0,3);
        w.Staff.Sound=(Action<StaffMember,int,int>)w.External["sound"];w.Staff.Advisor=(Action<int>)w.External["advisor"];
        w.Staff.Litter.Added=(Action<LitterItem>)w.External["litter-added"];w.Staff.Litter.Removed=(Action<LitterItem>)w.External["litter-removed"];
        if(hire) {
            foreach(var k in StaffTables.PoolBuildOrder){var m=w.Staff.Hire(k,0);if(m==null||!w.Staff.Drop(m,new ParkCell(2,2)))throw new Exception("staff graph hire");}
            w.Staff.Litter.Drop(new NativeGuestMotion.Point(640,640),false);
            w.Staff.Litter.Drop(new NativeGuestMotion.Point(1152,1152),true);
            w.Visitors.Arrive(new(1,1),new(6,6));
        }
        return w;
    }
    static World Load(World source,Action<bool,string> check,Func<ParkStaff.State,ParkStaff.State>? mutate=null)
    {
        var graph=source.Graph();var capture=new Bundle(source.Paths.CaptureState("grid"),graph.Snapshot,
            source.Visitors.CaptureState(source.VisitorBind(graph)),source.Needs.CaptureState(source.NeedBind()),
            source.Staff.CaptureState(source.Bind(graph)),source.Clock.CaptureState(),source.Activations.CaptureState(),source.Sim.CaptureScriptedState(Scripts(),m=>source.Id(m)),source.Management.CaptureState(source.Id),source.Awards.CaptureState());
        string path=Path.Combine(Path.GetTempPath(),"tpw-staff-"+Guid.NewGuid().ToString("N")+".save");Bundle state;
        try{ParkSaveFile.Write(path,capture);state=ParkSaveFile.Read<Bundle>(path);}finally{File.Delete(path);File.Delete(path+".bak");}
        if(mutate!=null)state=state with {Staff=mutate(state.Staff)};
        var w=new World{Paths=ParkPaths.FromState(state.Paths,"grid",StaffMemberSaveChecks.Terrain())};
        var g=GuestWalk.AllocateState(state.Walk,"grid",w.Paths);w.Walk=g.Walk;w.Sim=ParkSim.AllocateScriptedState(state.Sim,w.Paths,Scripts());
        w.Clock.RestoreState(state.Clock);w.Activations=NativeActivationSequence.FromState(state.Activations);
        w.Visitors=ParkVisitors.AllocateState(state.Visitors,w.Sim,w.VisitorBind(g));
        w.Needs=VisitorNeeds.FromState(state.Needs,w.NeedBind());
        w.Staff=ParkStaff.AllocateState(state.Staff,w.Visitors,w.Clock,w.Activations,w.Bind(g));
        bool simBlocked=false;try{w.Sim.Advance(.04);}catch(InvalidOperationException){simBlocked=true;}
        check(simBlocked,"cyclic sim shell cannot tick before staff references bind");
        bool blocked=false;try{w.Staff.Update();}catch(InvalidOperationException){blocked=true;}check(blocked,"unpublished shell blocks update");
        g.Hydrate(new(){ResolvePaused=id=>(Func<Guest,bool>)w.Resolve(id)});
        w.Staff.HydrateState(state.Staff,w.Bind(g));w.Visitors.HydrateStateBindings(w.VisitorBind(g));
        w.Sim.HydrateStaffState(id=>(StaffMember)w.Resolve(id));
        w.Awards.RestoreState(state.Awards);w.Management=new(w.Clock,w.Awards);w.Management.RestoreState(state.Management,w.Resolve);
        check(w.Events.Count==0,"no hire/RNG/litter/advisor/sound replay");
        w.Events.AddRange(source.Events);
        check(Json(source.Save())==Json(w.Save()),"staff FILE graph roundtrip");
        check(ReferenceEquals(w.Staff.Routes,w.Walk.NativeRoutes)&&ReferenceEquals(w.Staff.Litter.Activations,w.Activations),"shared resources");
        return w;
    }
    public static int Run()
    {
        int checks=0;void C(bool ok,string name){checks++;if(!ok)throw new Exception("staff graph: "+name);}
        var virgin=New(false);var fresh=Load(virgin,C);
        C(virgin.Save().Candidates==null&&fresh.Save().Candidates==null,"lazy candidate database stays absent");
        foreach(var w in new[]{virgin,fresh}){w.Staff.Hire(StaffKind.Handyman,2);w.Staff.Litter.Drop(new(384,384),false);}
        C(Json(virgin.Save())==Json(fresh.Save()),"future candidate/variant RNG and held hire match");
        var source=New();for(int i=0;i<3;i++)source.Tick();var loaded=Load(source,C);
        bool pending=source.Staff.RouteRequests.Pending>0;bool claimed=source.Staff.Litter.Active.Any(l=>l.Claimant!=null);
        for(int i=0;i<420;i++) {
            if(i==70)foreach(var w in new[]{source,loaded}){var m=w.Staff.Active(StaffKind.Mechanic).Single();w.Staff.Fire(m);
                var replacement=w.Staff.Hire(StaffKind.Mechanic,1)!;w.Staff.Drop(replacement,new ParkCell(4,4));}
            if(i==150)foreach(var w in new[]{source,loaded})w.Staff.Litter.Drop(new(896,896),false);
            source.Tick();loaded.Tick();pending|=source.Staff.RouteRequests.Pending>0;claimed|=source.Staff.Litter.Active.Any(l=>l.Claimant!=null);
            C(Json(source.Save())==Json(loaded.Save())&&Json(source.Graph().Snapshot)==Json(loaded.Graph().Snapshot)
                &&Json(source.Needs.CaptureState(source.NeedBind()))==Json(loaded.Needs.CaptureState(loaded.NeedBind()))
                &&Json(source.Management.CaptureState(source.Id))==Json(loaded.Management.CaptureState(loaded.Id))
                &&Json(source.Sim.Finances.CaptureState())==Json(loaded.Sim.Finances.CaptureState())&&source.Events.SequenceEqual(loaded.Events),"real visitor/staff continuation "+i);
            if(i==25||i==71||i==151)loaded=Load(loaded,C);
        }
        C(source.Management.MonthChanges>0&&source.Management.WeeklyPasses>0,"calendar wages and weekly pass ran");
        C(pending&&claimed&&source.Staff.Litter.Swept>0,"nonvacuous pending work, claims and actual cleaning");
        C(source.Staff.Members.OfType<Researcher>().Single().Quanta>0,"real researcher updated");
        var cyclic=New();
        var ride=cyclic.Sim.Add(10,"mechanic cycle",new(4,4),1,1,Script,null!,3,new(4,3),new(5,3),out var fault)!;
        C(ride!=null&&fault==null,"cyclic ride fixture");
        var mechanic=(Mechanic)cyclic.Staff.Active(StaffKind.Mechanic).Single();
        C(mechanic.Dispatch(ride,false),"actual dispatch creates bidirectional ride/member links");
        var cycleCopy=Load(cyclic,C);var restoredRide=cycleCopy.Sim.Rides.Single();
        var restoredMechanic=(Mechanic)cycleCopy.Staff.Active(StaffKind.Mechanic).Single();
        C(ReferenceEquals(restoredRide.AssignedMechanic,restoredMechanic)&&ReferenceEquals(restoredMechanic.Target,restoredRide),
            "ride/member cycle binds only restored identities");
        for(int i=0;i<160;i++){cyclic.Tick();cycleCopy.Tick();
            C(Json(cyclic.Save())==Json(cycleCopy.Save())&&Json(cyclic.Sim.CaptureScriptedState(Scripts(),m=>cyclic.Id(m)))
                ==Json(cycleCopy.Sim.CaptureScriptedState(Scripts(),m=>cycleCopy.Id(m))),"dispatched mechanic continuation "+i);}
        void Bad(Func<ParkStaff.State,ParkStaff.State> mutate,string why) {
            string before=Json(source.Save());int events=source.Events.Count;bool rejected=false;
            try{Load(source,(_,_)=>{},mutate);}catch(ArgumentException){rejected=true;}
            C(rejected&&before==Json(source.Save())&&events==source.Events.Count,"invalid staged graph leaves source unchanged: "+why);
        }
        Bad(s=>s with{Version=9},"version");Bad(s=>s with{PathsKey="wrong"},"resource identity");
        Bad(s=>s with{OwnedRandom=null,ExternalRandomKey=null},"missing RNG owner");
        Bad(s=>s with{MapOrder=new[]{s.MapOrder[0],s.MapOrder[0]}},"map membership");
        Bad(s=>s with{HireHeld="local/member/99/0"},"held identity");
        Bad(s=>s with{Security=s.Security with{WatchingLayout=new(){Slots=new int?[]{123},FreeBottomFirst=Array.Empty<int>()}}},"watcher allocation history");
        // Force one genuine allocated staff route, then offer that chain to a second owner.
        var routeOwner=source.Staff.Active(StaffKind.Handyman).Single();
        C(source.Staff.RouteRequests.Submit(routeOwner,new(640,640),new(1408,1408),0x11),"route collision fixture admitted");
        source.Staff.RouteRequests.Pump();
        C(source.Save().Members.Any(m=>m.Route?.SlotIndex>=0),"route collision fixture owns real output slots");
        Bad(s=>{var rows=s.Members.ToArray();var first=rows.First(m=>m.Route?.SlotIndex>=0);
            int other=Array.FindIndex(rows,m=>m.Kind!=first.Kind&&m.Active);
            rows[other]=rows[other] with{Route=first.Route,RouteEpoch=first.RouteEpoch};return s with{Members=rows};},"two staff share one output chain");
        return checks;
    }
}
