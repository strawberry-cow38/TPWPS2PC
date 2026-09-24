using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Opt-in research controller, actual bus birth, actual placement and actor consumers.
/// Explicit fee10 and one-batch fixture. NOT a native pathfinder/readiness/departure parity test.</summary>
public partial class NativeEntranceFlowSmoke : Node3D
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
            Set(viewer,"_experimentalEntrance",true);
            Set(viewer,"_experimentalEntranceFee",10); // explicit money-consumer fixture, not scenario default
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
                        try {Call(viewer,"PlaceHeld");}finally{Set(viewer,"_cursorOverride",null);}
                        placed=park.Placed.Count==1;
                    }
            Call(viewer,"CloseTool");
            Check(placed,"actual build menu/PlaceHeld registered a positive-value native attraction");
            int t=0;
            while(Field<int>(viewer,"_busAdmitted")==0&&t++<1200)Call(viewer,"StepPark",.04);
            var walk=Field<GuestWalk>(viewer,"_guests");
            var visitors=Field<ParkVisitors>(viewer,"_visitors");
            var flow=Field<NativeEntranceFlow>(viewer,"_entranceFlow");
            Check(flow!=null&&walk.Guests.Count>0,"normal bus batch automatically acquired research entrance owners");
            var batch=walk.Guests.ToArray();
            Check(batch.All(g=>flow.Owns(g)&&visitors.Plans[g.Id].Intent==VisitorIntent.Entering),"every birth owns its pending entrance route before ordinary AI");
            var cash=batch.ToDictionary(g=>g.Id,g=>visitors.Needs.Of(g.Id).Cash);
            var actors=Field<Dictionary<int,Node3D>>(viewer,"_actors");
            var bodies=batch.ToDictionary(g=>g.Id,g=>actors[g.Id]);
            var sim=Field<ParkSim>(viewer,"_sim");int income=sim.Finances.TotalIncome;
            Set(viewer,"_guestCap",0); // isolate this real batch, never manually spawn/remove a guest
            Check(walk.NativeRoutes.Available==NativeRoutePool.Capacity,
                "bus birth takes an empty lease, not an unsubmitted route allocation");
            int peak=0,staging=0,peakSlots=0;
            for(int i=0;i<2500&&batch.Any(flow.Owns);i++)
            {
                Call(viewer,"StepPark",.04);
                peak=Math.Max(peak,flow.Counts.Group0+flow.Counts.Group1);
                staging=Math.Max(staging,flow.StagingPending);
                peakSlots=Math.Max(peakSlots,walk.NativeRoutes.AllocatedCount);
                Check(batch.All(g=>walk.Guests.Any(live=>ReferenceEquals(g,live))),"same bus-born identities survive each incoming update");
                Check(batch.All(g=>ReferenceEquals(actors[g.Id],bodies[g.Id])),"same actual rendered actors survive movement/group transfer");
                if(i%16==0)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            Check(staging>0&&peak>0,"actual bus guests contributed staging and incoming memberships");
            Check(!batch.Any(flow.Owns),"all actual bus guests completed mode13 handoff, no artificial timeout release");
            Check(Field<int>(viewer,"_entranceRejected")==0&&Field<int>(viewer,"_entranceAccepted")==batch.Length,"fee10 positive-value fixture accepted exactly this batch");
            Check(batch.All(g=>visitors.Needs.Of(g.Id).Cash==cash[g.Id]-10),"each guest charged once, including retries/queue reposition");
            Check(sim.Finances.TotalIncome-income==batch.Length*10,"actual park finance receives exactly the corresponding entrance money");
            Check(peakSlots>0 && walk.NativeRoutes.Available==NativeRoutePool.Capacity,
                "actual rendered guest consumed and returned the park's shared output slots");
            Check(flow.Counts==(0,0)&&flow.StagingPending==0,"completed incoming batch leaves no counted memberships or staging pressure");
            Check(batch.All(g=>!g.HasNativeRoute),"normal visitor owner receives same guests after centre handoff");
            Call(viewer,"ResetNativeBus");
            Check(Field<NativeEntranceFlow>(viewer,"_entranceFlow")==null&&walk.BeforeStep==null,"reset removes controller tick hook and pending results");
            exit=0;
        }
        catch(Exception e){GD.PrintErr($"NATIVE ENTRANCE FLOW SMOKE FAIL checks={checks}: {e}");}
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
        if(exit==0)GD.Print($"NATIVE ENTRANCE FLOW SMOKE PASS checks={checks}; actual bus/placement/queues/fee/actors; explicit fee10 one-batch research adapters; NOT native pathfinder/departure parity");
        GetTree().Quit(exit);
    }
}
