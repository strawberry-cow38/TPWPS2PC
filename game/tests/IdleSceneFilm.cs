using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Queue item 5: films cow tools' --idle-scene crowd from ONE fixed camera aimed at the crowd.
/// The same file runs on main (A: today's id%6 idle choice) and on tinyclaw/native-idle-all
/// (B: TPW_IDLE_NATIVE=1 turns on the dispatcher for every guest). Frames go to $TPW_IDLE_FILM.
/// Guests still Dawdle between stands in this scene, so each frame logs how many are standing.</summary>
public partial class IdleSceneFilm : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string n) => typeof(Viewer).GetField(n, Hidden);
    static T Field<T>(Viewer v,string n)=>(T)(Member(n) ?? throw new MissingMemberException(n)).GetValue(v);
    static void Set(Viewer v,string n,object o)=>(Member(n) ?? throw new MissingMemberException(n)).SetValue(v,o);
    static object Call(Viewer v,string n,params object[] args)
    {
        var m=typeof(Viewer).GetMethod(n,Hidden)??throw new MissingMethodException(n);
        args=args.Concat(m.GetParameters().Skip(args.Length).Select(p=>p.DefaultValue)).ToArray();
        return m.Invoke(v,args);
    }
    public override async void _Ready()
    {
        Viewer viewer=null;int exit=2;string summary="";
        try
        {
            string film=System.Environment.GetEnvironmentVariable("TPW_IDLE_FILM") ?? throw new InvalidOperationException("TPW_IDLE_FILM unset");
            bool native=System.Environment.GetEnvironmentVariable("TPW_IDLE_NATIVE")=="1";
            string mode=native?"B":"A";
            System.IO.Directory.CreateDirectory(film);
            viewer=new Viewer{Name="Viewer"};
            Set(viewer,"_idleScene",true);
            if(native){Set(viewer,"_nativeGuestAnimation",true);Set(viewer,"_nativeIdleAll",true);}
            AddChild(viewer);viewer.SetProcess(false);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            for(int i=0;i<200;i++)Call(viewer,"StepPark",.04);
            var walk=Field<GuestWalk>(viewer,"_guests");
            if(walk==null||walk.Guests.Count==0)throw new InvalidOperationException("idle scene seeded no guests");
            foreach(var layer in viewer.FindChildren("*","CanvasLayer",true,false).OfType<CanvasLayer>())layer.Visible=false;
            foreach(var ui in viewer.GetChildren().OfType<Control>())ui.Visible=false;
            var actors=Field<Dictionary<int,Node3D>>(viewer,"_actors");
            Set(viewer,"_freeCam",false); // this harness owns the camera for the capture
            var camera=Field<Camera3D>(viewer,"_cam");
            Call(viewer,"PlaceActors",1f);
            // Aim at where the guests ARE (actor origins). The first cut merged every mesh AABB under
            // the actors, which reached something far away and framed the whole park with nobody in it.
            // ...and at the BIGGEST cluster: the scene's open cells can come in separate patches (JUNGLE-1:
            // 8 guests by the gate, 4 on a patch 44 cells away), and the mean of both is empty grass.
            var all=walk.Guests.Where(g=>actors.ContainsKey(g.Id)).Select(g=>actors[g.Id].GlobalPosition).ToList();
            var seed=all.OrderByDescending(p=>all.Count(q=>q.DistanceTo(p)<6f)).First();
            var spots=all.Where(q=>q.DistanceTo(seed)<6f).ToList();
            var at=spots.Aggregate(Vector3.Zero,(a,b)=>a+b)/spots.Count+new Vector3(0,0.3f,0);
            float spread=spots.Max(p=>new Vector2(p.X-at.X,p.Z-at.Z).Length());
            float reach=Math.Clamp(spread*1.6f,1.8f,9f);
            GD.Print($"[idlefilm] aiming at {at} over {spots.Count} guests, spread {spread:F2}, reach {reach:F2}");
            var anims=native?Field<Dictionary<Guest,NativeGuestAnimation>>(viewer,"_nativeAnimations"):null;
            int standing=0,held=0,inactive=0,idleClip=0,other=0,frames=0;
            var log=new System.Text.StringBuilder();
            for(int t=0;t<120;t++)
            {
                Call(viewer,"StepPark",.04);Call(viewer,"PlaceActors",1f);
                camera.GlobalPosition=at+new Vector3(-0.55f,0.42f,-0.75f)*reach;camera.LookAt(at);
                int s=0,h=0,f=0,c=0,o=0;
                foreach(var g in walk.Guests.Where(g=>g.State!=GuestState.Walking))
                {
                    s++;
                    if(anims!=null&&anims.TryGetValue(g,out var a))
                    { if(a.Held!=null)h++; else if(a.Slot==NativeAnimationDescriptor.Inactive)f++; else if(a.Slot==2)c++; else o++; }
                }
                standing+=s;held+=h;inactive+=f;idleClip+=c;other+=o;
                if(t%4!=0)continue;
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var image=GetViewport().GetTexture().GetImage();
                if(image.SavePng(System.IO.Path.Combine(film,$"{mode}-{frames:D3}.png"))!=Error.Ok)throw new InvalidOperationException("frame save failed");
                log.AppendLine($"{mode}-{frames:D3} tick={t} guests={walk.Guests.Count} standing={s}"+(native?$" holding={h} neverPosed={f} idleClip={c} otherClip={o}":""));
                frames++;
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(film,$"{mode}-frames.txt"),log.ToString());
            summary=$"mode={mode} guests={walk.Guests.Count} frames={frames} standingGuestTicks={standing}"
                +(native?$" holding={held} neverPosed={inactive} idleClip={idleClip} otherClip={other}":"");
            exit=0;
        }
        catch(Exception e){GD.PrintErr($"IDLE SCENE FILM FAIL: {e}");}
        finally
        {
            if(viewer!=null&&IsInstanceValid(viewer)){Field<RideSounds>(viewer,"_sounds")?.Clear();viewer.QueueFree();}
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        if(exit==0)GD.Print($"IDLE SCENE FILM PASS {summary}; generated frames, not yet inspected");
        GetTree().Quit(exit);
    }
}
