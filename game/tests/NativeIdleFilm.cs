using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Queue item 5, for strawberry's choice: the SAME park and guests filmed with today's id%6 idles
/// (A) and then with the native weighted logical 11 under --native-idle-all (B), from one fixed camera.
/// Frames go to $TPW_IDLE_FILM as A-###.png and B-###.png. It is a generated film until a person has
/// looked at it. Explicit fee10 admission; research flags only.</summary>
public partial class NativeIdleFilm : Node3D
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
            Set(viewer,"_nativeGuestAnimation",true);
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
            string film=System.Environment.GetEnvironmentVariable("TPW_IDLE_FILM");
            Check(!string.IsNullOrEmpty(film),"TPW_IDLE_FILM names the output directory");
            System.IO.Directory.CreateDirectory(film);
            var anims=Field<Dictionary<Guest,NativeGuestAnimation>>(viewer,"_nativeAnimations");
            var actors=Field<Dictionary<int,Node3D>>(viewer,"_actors");
            var followed=new HashSet<Guest>(ReferenceEqualityComparer.Instance);
            // The bus keeps running: admitted guests also go home on their own (5 of 8 did in the
            // departure smoke), so a fixed cohort thins out before the camera rolls.
            for(int i=0;i<9000&&followed.Count<12;i++)
            {
                foreach(var o in flow.Observations)followed.Add(o.Guest);
                Call(viewer,"StepPark",.04);
                if(i%16==0)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            IEnumerable<Guest> Crowd()=>walk.Guests.Where(g=>!flow.Owns(g)).ToArray();
            Check(Crowd().Count()>=4,$"{Crowd().Count()} admitted guests are in the park to film");
            foreach(var layer in viewer.FindChildren("*","CanvasLayer",true,false).OfType<CanvasLayer>())layer.Visible=false;
            foreach(var ui in viewer.GetChildren().OfType<Control>())ui.Visible=false;
            var camera=Field<Camera3D>(viewer,"_cam");
            Aabb Box()=>Crowd().Where(g=>actors.ContainsKey(g.Id))
                .SelectMany(g=>actors[g.Id].FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null))
                .Select(m=>m.GlobalTransform*m.GetAabb()).Aggregate((x,y)=>x.Merge(y));
            string stats="";
            async System.Threading.Tasks.Task Mode(string name,bool native)
            {
                Set(viewer,"_nativeIdleAll",native);
                for(int i=0;i<40;i++){Call(viewer,"StepPark",.04);Call(viewer,"PlaceActors",1f);}
                var box=Box();var at=box.GetCenter();float reach=Math.Max(1.2f,box.Size.Length()*0.8f);
                camera.GlobalPosition=at+new Vector3(-0.7f,0.55f,-0.7f)*reach;camera.LookAt(at);
                int standing=0,held=0,clip=0,frames=0;
                for(int t=0;t<150;t++)
                {
                    Call(viewer,"StepPark",.04);Call(viewer,"PlaceActors",1f);
                    foreach(var g in Crowd().Where(g=>g.State!=GuestState.Walking))
                    {
                        standing++;
                        if(native&&anims.TryGetValue(g,out var a)){if(a.Held!=null)held++;else if(a.Slot==2)clip++;}
                    }
                    if(t%3!=0)continue;
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    using var image=GetViewport().GetTexture().GetImage();
                    Check(image.SavePng(System.IO.Path.Combine(film,$"{name}-{frames++:D3}.png"))==Error.Ok,"saved "+name+" frame");
                }
                if(native)Check(anims.Keys.Any(g=>!flow.Owns(g)),"mode B draws ordinary guests through the dispatcher");
                else Check(!anims.Keys.Any(g=>!flow.Owns(g)),"mode A leaves ordinary guests on today's id%6 idles");
                stats+=$" {name}: standing guest-ticks={standing}"+(native?$" holding-last-pose={held} idle-clip={clip}":"")+$" frames={frames};";
            }
            await Mode("A",false);
            await Mode("B",true);
            detail=stats.Trim();
            exit=0;
        }
        catch(Exception e){GD.PrintErr($"NATIVE IDLE FILM SMOKE FAIL checks={checks}: {e}");}
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
            catch(Exception e){exit=2;GD.PrintErr("NATIVE IDLE FILM cleanup: "+e);}
        }
        if(exit==0)GD.Print($"NATIVE IDLE FILM SMOKE PASS checks={checks}; {detail}; A=id%6 idles, B=native weighted logical 11 (--native-idle-all); generated, not yet inspected");
        GetTree().Quit(exit);
    }
}
