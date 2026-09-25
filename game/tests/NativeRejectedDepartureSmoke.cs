using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Actual rejected bus cohort walking back to point0 under the explicit represented-activation phase adapter.
/// Fee=int.MaxValue forces cash rejection. Not native startup/search/readiness parity.</summary>
public partial class NativeRejectedDepartureSmoke : Node3D
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
    int checks;
    void Check(bool ok,string why) { if(!ok)throw new InvalidOperationException(why);checks++; }
    public override async void _Ready()
    {
        Viewer viewer=null;int exit=2;
        try
        {
            Check(DisplayServer.GetName()!="headless","rendering display required");
            viewer=new Viewer{Name="Viewer"};
            Set(viewer,"_entranceFee",int.MaxValue); // explicit rejection fixture, not an authored price
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
            Call(viewer,"ShowBuildCategory","Rides");
            var rows=Field<List<int>>(viewer,"_buildRows");
            int chosen=Enumerable.Range(0,rows.Count).First(row=>
            {
                var d=(RideDefinition)Call(viewer,"DefinitionFor",lib.Rides[rows[row]].Model);
                var initial=NativeRideValue.CreateDefaultState(d?.CompiledEntry);
                return d?.Shape!=null && initial.HasValue && NativeRideValue.Calculate(d.CompiledEntry,initial.Value)>8;
            });
            var blueprint=Field<Placement>(viewer,"_place");
            for(int targetCount=1;targetCount<=2;targetCount++)
            {
                Call(viewer,"ArmFromList",chosen);bool placed=false;
                for(int z=entry.ZEnd+10;z<park.Field.Height-10&&!placed;z++)
                    for(int x=12;x<park.Field.Width-10&&!placed;x++)
                        if(blueprint.Fits(park,x,z))
                        {
                            Set(viewer,"_cursorOverride",(x,z));
                            try{Call(viewer,"PlaceHeld");}finally{Set(viewer,"_cursorOverride",null);}
                            placed=park.Placed.Count==targetCount;
                        }
                Check(placed,"real placement consumes a shared activation before guest birth");
            }
            Call(viewer,"CloseTool");
            int t=0;
            while(Field<int>(viewer,"_busAdmitted")==0&&t++<1200)Call(viewer,"StepPark",.04);
            var walk=Field<GuestWalk>(viewer,"_guests");
            var visitors=Field<ParkVisitors>(viewer,"_visitors");
            var flow=Field<NativeEntranceFlow>(viewer,"_entranceFlow");
            Check(flow!=null&&walk.Guests.Count>0,"normal bus batch automatically acquired research entrance owners");
            var batch=walk.Guests.ToArray();
            var sequence=Field<NativeActivationSequence>(viewer,"_nativeActivations");
            var catalogue=Field<NativeBusCatalogue>(viewer,"_busCatalogue");
            Check(batch.All(g=>flow.Owns(g)&&visitors.Plans[g.Id].Intent==VisitorIntent.Entering),
                "every actual bus birth is entrance-owned");
            Check(!sequence.NativeHistoryVerified&&sequence.Origin.Contains("represented"),
                "rendered fixture does not pretend represented activation history is native startup parity");
            Check(batch.Select((g,i)=>flow.Observations.Single(o=>ReferenceEquals(o.Guest,g)).Serial
                ==unchecked(catalogue.ImageInitialActivationCounter+2u+(uint)i)).All(ok=>ok)
                &&flow.Observations.All(o=>o.Serial!=(uint)o.Guest.Id),
                "two actual placed objects precede guest serials; no Guest.Id alias supplies departure phases");
            var cash=batch.ToDictionary(g=>g.Id,g=>visitors.Needs.Of(g.Id).Cash);
            var actors=Field<Dictionary<int,Node3D>>(viewer,"_actors");
            var bodies=batch.ToDictionary(g=>g.Id,g=>actors[g.Id]);
            var sim=Field<ParkSim>(viewer,"_sim");int income=sim.Finances.TotalIncome;
            var retired=new HashSet<int>();int peakPressure=0,peakSlots=0;
            Set(viewer,"_guestCap",0); // isolate real first cohort; no manual guest spawn
            var gridBus=ParkPaths.Centre(catalogue.Point0);
            var worldBus=park.CellCentre(catalogue.Point0.X,catalogue.Point0.Z);
            for(int i=0;i<3000&&retired.Count<batch.Length;i++)
            {
                var live=batch.Where(walk.IsLive).ToArray();
                var positions=live.ToDictionary(g=>g.Id,g=>g.Position);
                var drawn=live.ToDictionary(g=>g.Id,g=>actors[g.Id].Position);
                foreach(var g in live)
                {
                    Check(visitors.Needs.Of(g.Id).Cash==cash[g.Id],"rejected walker retains its cash through every outgoing update");
                    Check(ReferenceEquals(actors[g.Id],bodies[g.Id]),"same real actor survives until native final-route completion");
                }
                Call(viewer,"StepPark",.04);
                peakPressure=Math.Max(peakPressure,flow.DeparturePressure);
                peakSlots=Math.Max(peakSlots,walk.NativeRoutes.AllocatedCount);
                foreach(var g in live.Where(g=>!walk.IsLive(g)))
                {
                    Check(positions[g.Id]==gridBus,"retirement occurs at the bus point, not at the booth or a teleport");
                    Check(Mathf.Abs(drawn[g.Id].X-worldBus.X)<.002f&&Mathf.Abs(drawn[g.Id].Z-worldBus.Z)<.002f,
                        "actual rendered actor reached point0 before its final retirement tick");
                    Check(!actors.ContainsKey(g.Id)&&!visitors.Plans.ContainsKey(g.Id)&&!visitors.Needs.Has(g.Id)
                        &&!flow.Owns(g),"real actor/plan/needs/route owner all disappear together");
                    retired.Add(g.Id);
                }
                if(i%16==0)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            Check(retired.Count==batch.Length&&visitors.WentHome==batch.Length&&visitors.DiscardedEntranceGuests==0,
                "whole rejected cohort completes real departures, not fixture teardown");
            Check(Field<int>(viewer,"_entranceAccepted")==0&&Field<int>(viewer,"_entranceRejected")==batch.Length
                &&sim.Finances.TotalIncome==income,"rejection never counts paid admission or credits entrance fees");
            Check(peakPressure>0&&flow.DeparturePressure>0,"sticky departure pressure was measured including the final removal pass");
            Check(peakSlots>0&&walk.NativeRoutes.Available==NativeRoutePool.Capacity&&flow.Counts==(0,0)
                &&flow.StagingPending==0,"outgoing routes free the shared pool without incoming-membership or staging leaks");
            Check(Field<HashSet<int>>(viewer,"_entranceRequestFlags").Contains(0x21),"Viewer route adapter receives native departure request flags");
            Call(viewer,"StepPark",.04);
            Check(flow.DeparturePressure==0&&flow.Observations.Count==0,"following tick clears departed pressure without resurrecting guests");
            exit=0;
        }
        catch(Exception e){GD.PrintErr($"NATIVE REJECTED DEPARTURE SMOKE FAIL checks={checks}: {e}");}
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
            catch(Exception e){exit=2;GD.PrintErr("NATIVE ENTRANCE FLOW cleanup: "+e);}
        }
        if(exit==0)GD.Print($"NATIVE REJECTED DEPARTURE SMOKE PASS checks={checks}; actual bus-born cohort walks to point0 and retires; explicit rejection price and represented-activation phase adapter; NOT full native startup/search parity");
        GetTree().Quit(exit);
    }
}
