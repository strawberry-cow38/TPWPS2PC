using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Queue item 7: an ADMITTED bus-born guest who has had enough (20C930's cash&lt;100 arm) leaves
/// through the native state-26 departure -- phase-gated mode14, staging, mode9 to point0 -- instead of
/// vanishing at the gate. Explicit fee10 admission and an explicit cash drop to 50 are the fixture's
/// inputs. Not native startup/search/readiness parity, and not the 20C930 arms after the 26 write.</summary>
public partial class NativeOrdinaryDepartureSmoke : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string n) => typeof(Viewer).GetField(n, Hidden) ?? throw new MissingMemberException(n);
    static T Field<T>(Viewer v,string n)=>(T)Member(n).GetValue(v);
    static void Set(Viewer v,string n,object o)=>Member(n).SetValue(v,o);
    static object Call(Viewer v,string n,params object[] args)
    {
        var m=typeof(Viewer).GetMethod(n,Hidden)??throw new MissingMethodException(n);
        args=args.Concat(m.GetParameters().Skip(args.Length).Select(p=>p.DefaultValue)).ToArray();
        return m.Invoke(v,args);
    }
    int checks;string detail="";
    void Check(bool ok,string why) { if(!ok)throw new InvalidOperationException(why);checks++; }
    public override async void _Ready()
    {
        Viewer viewer=null;int exit=2;
        try
        {
            Check(DisplayServer.GetName()!="headless","rendering display required");
            viewer=new Viewer{Name="Viewer"};
            Set(viewer,"_experimentalEntrance",true);
            Set(viewer,"_experimentalEntranceFee",10); // explicit admission fixture, not scenario default
            AddChild(viewer);viewer.SetProcess(false);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var park=Field<Park>(viewer,"_park");
            var lib=Field<AssetLibrary>(viewer,"_lib");
            var terrain=Field<Model>(viewer,"_terrainModel");
            var entry=Field<ParkEntrance>(viewer,"_entranceTable").Fit(terrain.Field,
                ParkEntrance.WalkwayColumnFromPoles(terrain),out _);
            var path=new List<(int,int)>();
            for(int z=entry.ZEnd;z<entry.ZEnd+8;z++)path.Add((entry.XCol,z));
            Call(viewer,"LayLeg",path,PathTool.Kind.Path,0);Call(viewer,"RefreshFloor");
            Check((bool)Call(viewer,"OpenGate"),"ordinary guest layer opens");
            Call(viewer,"ToggleBuildMenu");Call(viewer,"ShowBuildCategory","Rides");
            var rows=Field<List<int>>(viewer,"_buildRows");
            int chosen=Enumerable.Range(0,rows.Count).First(row=>
            {
                var d=(RideDefinition)Call(viewer,"DefinitionFor",lib.Rides[rows[row]].Model);
                var initial=NativeRideValue.CreateDefaultState(d?.CompiledEntry);
                return d?.Shape!=null && initial.HasValue && NativeRideValue.Calculate(d.CompiledEntry,initial.Value)>8;
            });
            Call(viewer,"ArmFromList",chosen);
            var blueprint=Field<Placement>(viewer,"_place");bool placed=false;
            for(int z=entry.ZEnd+10;z<park.Field.Height-10&&!placed;z++)
                for(int x=12;x<park.Field.Width-10&&!placed;x++)
                    if(blueprint.Fits(park,x,z))
                    {
                        Set(viewer,"_cursorOverride",(x,z));
                        try{Call(viewer,"PlaceHeld");}finally{Set(viewer,"_cursorOverride",null);}
                        placed=park.Placed.Count==1;
                    }
            Call(viewer,"CloseTool");
            Check(placed,"actual build menu/PlaceHeld registered a positive-value native attraction");
            int t=0;
            while(Field<int>(viewer,"_busAdmitted")==0&&t++<1200)Call(viewer,"StepPark",.04);
            var walk=Field<GuestWalk>(viewer,"_guests");
            var visitors=Field<ParkVisitors>(viewer,"_visitors");
            var flow=Field<NativeEntranceFlow>(viewer,"_entranceFlow");
            var catalogue=Field<NativeBusCatalogue>(viewer,"_busCatalogue");
            Check(flow!=null&&walk.Guests.Count>0,"normal bus batch automatically acquired research entrance owners");
            // Several real batches, not one: a first JUNGLE bus brings ONE guest, and one leaver cannot show
            // that phases differ between guests. Every followed guest is tracked from birth, because admitted
            // guests also go home ON THEIR OWN (boredom/happiness) while later buses are still arriving.
            var followed=new HashSet<Guest>(ReferenceEqualityComparer.Instance);
            var serials=new Dictionary<Guest,uint?>(ReferenceEqualityComparer.Instance);
            var handedOver=new HashSet<Guest>(ReferenceEqualityComparer.Instance); // mode13 done: ordinary visitor
            var departing=new HashSet<Guest>(ReferenceEqualityComparer.Instance); // re-owned by TryDepart
            var retired=new HashSet<Guest>(ReferenceEqualityComparer.Instance);
            var requests=Field<Queue<(Guest Guest,int Mode,int Flags,uint Tick)>>(viewer,"_entranceRequests");
            var actors=Field<Dictionary<int,Node3D>>(viewer,"_actors");
            var gridBus=ParkPaths.Centre(catalogue.Point0);
            int wentHome=visitors.WentHome,peakPressure=0,deferred=0,staged=0,natural=0,broke=0;
            bool fixtureApplied=false;
            var seen=new List<(Guest Guest,int Mode,int Flags,uint Tick)>();
            for(int i=0;i<15000&&!(fixtureApplied&&retired.Count==followed.Count);i++)
            {
                foreach(var o in flow.Observations)
                    if(followed.Add(o.Guest))serials[o.Guest]=o.Serial;
                if(followed.Count>=8)Set(viewer,"_guestCap",0); // never spawn or remove a guest by hand
                // THE FIXTURE'S ONE INPUT, once every followed guest is past the booth: 20C930's cash<100
                // arm for whoever is still in the park and not already leaving. The rest is production.
                if(!fixtureApplied&&followed.Count>=8&&followed.All(g=>retired.Contains(g)||handedOver.Contains(g)||departing.Contains(g)))
                {
                    foreach(var g in followed.Where(g=>handedOver.Contains(g)&&!departing.Contains(g)&&walk.IsLive(g)))
                    {var w=visitors.Needs.Of(g.Id);w.Cash=50;visitors.Needs.Set(g.Id,w);broke++;}
                    fixtureApplied=true;
                }
                var live=followed.Where(walk.IsLive).ToArray();
                var positions=live.ToDictionary(g=>g,g=>g.Position,ReferenceEqualityComparer.Instance);
                var wasOwned=live.ToDictionary(g=>g,g=>flow.Owns(g),ReferenceEqualityComparer.Instance);
                Call(viewer,"StepPark",.04);
                foreach(var r in requests)seen.Add(r);
                requests.Clear();
                peakPressure=Math.Max(peakPressure,flow.DeparturePressure);
                foreach(var g in live.Where(walk.IsLive))
                {
                    bool owns=flow.Owns(g);
                    if(!owns&&visitors.Plans[g.Id].Intent!=VisitorIntent.Entering)handedOver.Add(g);
                    // TryDepart's entries are the only Leaving ones with no acceptance at all: a booth
                    // rejection carries Accepted=false, a fresh birth is still Entering.
                    var o=owns?flow.Observations.Single(x=>ReferenceEquals(x.Guest,g)):default;
                    if(owns&&visitors.Plans[g.Id].Intent==VisitorIntent.Leaving&&o.Accepted==null&&departing.Add(g))
                    {
                        Check(o.State is NativeEntranceFlow.State.Rejected or NativeEntranceFlow.State.Pending
                            &&visitors.Plans[g.Id].Intent==VisitorIntent.Leaving&&o.Serial==serials[g],
                            "an admitted leaver re-enters the flow at state 26 with its birth serial, as Leaving");
                        if(!fixtureApplied)natural++;
                    }
                }
                foreach(var o in flow.Observations.Where(o=>departing.Contains(o.Guest)))
                {
                    if(o.DeferredSticky)deferred++;
                    if(o.OutgoingDirection)staged++;
                }
                foreach(var g in live.Where(g=>!walk.IsLive(g)))
                {
                    Check(wasOwned[g],"a guest retires only from the native departure, never the legacy gate walk");
                    Check(positions[g]==gridBus,"retirement occurs at bus point0, not at the park gate");
                    Check(!actors.ContainsKey(g.Id)&&!visitors.Plans.ContainsKey(g.Id)&&!visitors.Needs.Has(g.Id),
                        "actor, plan and needs row disappear together");
                    retired.Add(g);
                }
                if(i%16==0)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            Check(serials.Values.All(s=>s.HasValue),"every bus birth carries a represented activation serial");
            var batch=followed.ToArray();
            Check(fixtureApplied&&departing.Count>=2,$"{departing.Count} admitted guests left through state 26 ({natural} on their own, {broke} made broke)");
            Check(departing.Select(g=>serials[g].Value&63u).Distinct().Count()>1,"the departing guests' activation phases differ");
            var outgoing=seen.Where(r=>departing.Contains(r.Guest)&&r.Mode==14&&r.Flags==0x21).ToArray();
            Check(departing.All(g=>outgoing.Any(r=>ReferenceEquals(r.Guest,g))),"every admitted leaver submitted its mode14 departure request");
            Check(outgoing.All(r=>(serials[r.Guest].Value&63u)==(r.Tick&63u)),
                "mode14 is requested only on the guest's activation phase (210D70's &63 gate)");
            Check(deferred>0&&peakPressure>0,"a phase-deferred leaver latched A4 and counted in the bus's departure pressure");
            Check(staged>0,"leavers staged outgoing before their bus leg");
            Check(retired.Count==batch.Length&&visitors.WentHome-wentHome==batch.Length&&visitors.DiscardedEntranceGuests==0,
                "the whole cohort left through real departures, counted as WentHome, none discarded");
            Check(walk.NativeRoutes.Available==NativeRoutePool.Capacity&&flow.Counts==(0,0)&&flow.StagingPending==0,
                "departures return every shared route slot and leave no membership or staging pressure");
            Call(viewer,"StepPark",.04);
            Check(flow.DeparturePressure==0&&flow.Observations.Count==0,"the following tick clears pressure without resurrecting guests");
            detail=$"followed={batch.Length} admittedLeavers={departing.Count} natural={natural} broke={broke} phases={departing.Select(g=>serials[g].Value&63u).Distinct().Count()} mode14={outgoing.Length} peakPressure={peakPressure}";
            exit=0;
        }
        catch(Exception e){GD.PrintErr($"NATIVE ORDINARY DEPARTURE SMOKE FAIL checks={checks}: {e}");}
        finally
        {
            try
            {
                if(viewer!=null&&IsInstanceValid(viewer))
                {Call(viewer,"ResetNativeBus");Field<RideSounds>(viewer,"_sounds")?.Clear();viewer.QueueFree();}
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
            }
            catch(Exception e){exit=2;GD.PrintErr("NATIVE ORDINARY DEPARTURE cleanup: "+e);}
        }
        if(exit==0)GD.Print($"NATIVE ORDINARY DEPARTURE SMOKE PASS checks={checks}; {detail}; admitted bus-born guests (on their own, or made broke) leave via state26/mode14/mode9 to point0; explicit fee10 and cash50 fixture; NOT the 20C930 arms after the 26 write");
        GetTree().Quit(exit);
    }
}
