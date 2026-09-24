using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;
namespace TPWPS2Viewer.Tests;

/// <summary>Native controller through the real model adapter, with two explicit clocks.</summary>
public partial class NativeBusAudit : Node
{
    int checks;
    void Check(bool value,string why) { if(!value) throw new Exception(why);checks++; }
    public override void _Ready()
    {
        try
        {
            using var lib=new AssetLibrary(OS.GetEnvironment("TPW_PS2_DISC"));
            foreach(string world in new[]{"JUNGLE","HALLOW","SPACE","FANTASY"})
            {
                lib.OpenWad("/DATA/"+world+".WAD");
                foreach(string stem in new[]{"bus1","bus2"})
                {
                    var ride=lib.Rides.Single(r=>r.Name.Equals($"features/{stem}/{stem}.mps",StringComparison.OrdinalIgnoreCase));
                    var model=new TPW.PS2.Data.Model(lib.Read(ride.Model));var anim=new Aps(lib.Read(ride.Animation));
                    Run(model,anim,world+"/"+stem);
                }
            }
            GD.Print($"NATIVE BUS PASS: {checks} checks, eight buses, native controller and model adapter");
            GetTree().Quit();
        }
        catch(Exception e) { GD.PrintErr("NATIVE BUS FAIL: "+e);GetTree().Quit(2); }
    }
    void Run(TPW.PS2.Data.Model model,Aps anim,string name)
    {
        var commands=new List<int>();var batches=new List<int>();
        var bus=new NativeBus(model,anim,_=>(null,false),199,0,(s,t)=>commands.Add(s),p=>batches.Add(p));
        AddChild(bus.Root);
        try
        {
            var c=bus.Controller;
            var surfaces=bus.Root.GetChildren().OfType<MeshInstance3D>().ToArray();
            Check(surfaces.Length>0,"real bus surfaces exist");
            bool Shown()=>surfaces.All(s=>s.Visible);
            bool Hidden()=>surfaces.All(s=>!s.Visible);
            int Tick(uint ms,int traffic=0,int delta=0x4000,bool open=true,bool absent=true,int flagged=0)
                =>bus.Update(ms,delta,traffic,open,absent,flagged);
            Check(Shown(),"hidden spline helper does not hide descendants initially");
            Check(c.RejectedCommands==1 && !c.Active && commands.SequenceEqual(new[]{0}),"creation state0 reaches wrapper but section12 is rejected");
            Tick(0,1);
            Check(c.State==0 && c.OuterRemaining<0,"first subtraction crossing zero skips state work");
            Tick(0);
            Check(c.State==1 && c.AppliedState==0,"state0 advances without animation");
            Check(Tick(1000,1)==1 && c.AppliedState==1 && c.State==1 && c.Active,"traffic blocks advancement, not the state1 command");
            Check(Shown(),"activation is not a frame-zero sample");
            Tick(1000,1);Check(Hidden(),"first sample applies zero hide key");
            Tick(1066,1);Check(Hidden(),"frame below two remains hidden");
            Tick(1067,1);Check(Shown(),"frame two reveals each actual mesh");
            Tick(8333,1);Check(!c.EndHold && c.State==1,"7333ms still before 220-frame endpoint");
            Tick(8334,1);Check(c.EndHold && c.Frame==220 && c.State==1,"endpoint clamped; blocked traffic retains completed state1");
            Tick(9000,1);Check(commands.Count==2,"blocked state1 does not restart animation");
            Tick(9000);Check(c.State==2 && c.AppliedState==1,"unblocking permits next logical state only");
            Check(Tick(9000)==2 && c.AppliedState==2 && !c.EndHold,"state2 selects second record and coordination2");
            Tick(17666);Check(!c.EndHold && batches.Count==0,"260 frames not complete at8666ms");
            Tick(17667);Check(c.State==3 && c.DwellRemaining==0x50000 && batches.SequenceEqual(new[]{0}),"one batch request at point0 on state2 completion");
            for(int i=0;i<20;i++) { Tick(17667);Check(c.AppliedState==2 && batches.Count==1,"every dwell subtraction skips commands and repeats no batch"); }
            Check(c.DwellRemaining==0,"twenty native countdown steps, not milliseconds");
            Tick(17667);Check(c.AppliedState==3 && c.Record.Offset==anim.Sections()[5].Offset+2*0x1c,"next call binds departure");
            Tick(24933);Check(Shown(),"departure before hide key remains shown");
            Tick(24934);Check(Hidden(),"departure frame218 hides all mesh nodes");
            Tick(25000);Check(!c.EndHold,"departure not yet beyond220");
            Tick(25001);Check(c.State==0 && c.AppliedState==3 && c.OuterRemaining==0xc8000 && Hidden(),"departure ends hidden; logical wrap does not issue state0 early");
            var endPose=surfaces.Select(s=>s.Transform).ToArray();
            var body=model.Meshes[0];
            var endWorld=bus.Model.LastWorld[body.Offset];
            var expectedOrigin=new Vector3(endWorld.M41,endWorld.M42,endWorld.M43);
            Check(surfaces.Where(s=>s.Name.ToString().StartsWith(body.Name+"#"))
                .All(s=>s.Transform.Origin.DistanceTo(expectedOrigin)<.0001f),
                "hidden body still receives endpoint pose, not last visible frame");
            for(int i=0;i<50;i++) { Tick(25001);Check(c.AppliedState==3,"outer subtraction must not issue stop or reset pose"); }
            Check(c.OuterRemaining==0,"fifty outer subtractions");
            Tick(25001);Check(c.RejectedCommands==2 && c.Active && c.EndHold && Hidden(),"state0 rejects section12, preserves departure hiding");
            Check(surfaces.Select(s=>s.Transform).SequenceEqual(endPose),"rejected stop does not restore bind matrices");
            Tick(25001);Check(c.AppliedState==1 && Shown(),"ordinary restart clears old unprotected mesh hiding before next sample");
            Tick(25001);Check(Hidden(),"restart then actually samples hide key, not static reveal");
            Tick(25068);Check(Shown(),"second visit really emerges");
            Check(commands.SequenceEqual(new[]{0,1,2,3,0,1}) && batches.Count==1,"whole command order and nonvacuous one-batch control");
            // Native self-hidden and subtree-pruning are distinct fields, not inherited0x10.
            uint root=model.Meshes[0].Parent;var saved=BitConverter.ToUInt32(model.D,(int)root);
            BitConverter.GetBytes(saved|0x20u).CopyTo(model.D,(int)root);
            bus.Model.SetFrame(3);Check(Hidden(),"root subtree-pruning bit suppresses descendants");
            BitConverter.GetBytes(saved).CopyTo(model.D,(int)root);
            bus.Model.SetFrame(3);Check(Shown(),"root self-hidden alone still allows descendants");
        }
        finally { bus.Root.Free(); }
        foreach(var gates in new[]{(false,true,0),(true,false,0),(true,true,30),(true,true,29)})
        {
            int released=0;
            var c=new NativeBusController(anim,0,0,_=>{},_=>{},(_,_)=>{},_=>released++);
            c.Update(0,0x4000,0,gates.Item1,gates.Item2,gates.Item3); // state0 ->1
            c.Update(0,0x4000,0,gates.Item1,gates.Item2,gates.Item3); // bind approach
            c.Update(7334,0x4000,0,gates.Item1,gates.Item2,gates.Item3); // ->2
            c.Update(7334,0x4000,0,gates.Item1,gates.Item2,gates.Item3); // bind middle
            c.Update(16001,0x4000,0,gates.Item1,gates.Item2,gates.Item3); // ->3
            Check(c.State==3 && c.DwellRemaining==0x50000,"denied release still enters dwell");
            Check(released==(gates==(true,true,29)?1:0),"all three release gates and passing boundary29");
            c.Update(16001,0,0,true,true,0);Check(c.DwellRemaining==0x50000,"zero countdown delta does not spend wait");
            c.Update(16001,-1,0,true,true,0);Check(c.DwellRemaining==0x50001,"signed native delta is not silently repaired");
        }
        // Synthetic short duration preserves the real type15 resource shape and makes exact
        // equality representable on an integer-ms clock; the next millisecond must finish.
        var bytes=(byte[])anim.D.Clone();BitConverter.GetBytes(3u).CopyTo(bytes,anim.Sections()[5].Offset+4);
        var exact=new NativeBusController(new Aps(bytes),0,0,_=>{},_=>{},(_,_)=>{},_=>{});
        exact.Update(0,0x4000,0,true,true,0);exact.Update(0,0x4000,0,true,true,0);
        exact.Update(100,0,0,true,true,0);Check(exact.Frame==3 && !exact.EndHold,"exact length is not completed");
        exact.Update(101,0,0,true,true,0);Check(exact.EndHold && exact.Frame==3,"strict endpoint samples clamped length");
        Check(NativeBusController.ElapsedFrames(0,uint.MaxValue-99)==3,"unsigned millisecond wrap");
        Presentation(model,anim,name);
        GD.Print($"NATIVE BUS {name}: phases, visibility, clocks, wrapper refusal and release gates PASS");
    }
    void Presentation(TPW.PS2.Data.Model model,Aps anim,string name)
    {
        int commands=0,batches=0;
        var bus=new NativeBus(model,anim,_=>(null,false),0,1000,(_,_)=>commands++,_=>batches++);
        AddChild(bus.Root);
        try
        {
            var c=bus.Controller;
            int node=model.NodeOffset(0);
            var initial=bus.Model.LastWorld[node];
            bus.Present(1000,.5f);
            Check(bus.Model.LastWorld[node]==initial && commands==1 && !c.Active,
                "inactive presentation does not invent an animation or command");
            bus.Update(1000,0x4000,0,true,true,0); // state0 ->1
            bus.Update(1000,0x4000,0,true,true,0); // bind approach
            bus.Update(2000,0x4000,0,true,true,0); // frame30
            Check(c.Frame==30 && c.AppliedState==1,"presentation control starts on moving approach");
            object Snapshot()=>(c.State,c.AppliedState,c.Frame,c.EndHold,c.Record,
                c.OuterRemaining,c.DwellRemaining,c.RejectedCommands,commands,batches);
            var simulation=Snapshot();
            var before=bus.Model.LastWorld[node];
            Check(Math.Abs(c.PresentationFrame(2010,.5f)-30.315f)<.0001f,
                "fractional render time samples authored thirty-fps frame independently");
            bus.Present(2010,.5f);
            var middle=bus.Model.LastWorld[node];
            Check(middle!=before,"presentation moves real body matrix without a tick");
            Check(Snapshot().Equals(simulation),"presentation cannot change controller/countdowns/callbacks");
            bus.Present(2020);
            var end=bus.Model.LastWorld[node];
            Check(end!=middle,"second render sample moves between the same two park ticks");
            bus.Present(2020);
            Check(bus.Model.LastWorld[node]==end && Snapshot().Equals(simulation),
                "same-time render is stable and consumes nothing");
            Check(c.PresentationFrame(1000000)==220,"render frame clamps to authored endpoint");
            bus.Present(1000000);
            Check(Snapshot().Equals(simulation) && batches==0 && commands==2,
                "even a far-future render cannot finish a phase or admit a batch");
            bus.Update(8334,0x4000,0,true,true,0); // true simulation completion
            Check(c.EndHold && c.Frame==220 && c.State==2,"simulation alone completes the first phase");
            var held=bus.Model.LastWorld[node];
            bus.Present(9000,.75f);
            Check(bus.Model.LastWorld[node]==held && c.PresentationFrame(9000,.75f)==220,
                "held endpoint does not extrapolate through wait/record boundary");
            var previous=c.Record;
            bus.Update(9000,0x4000,0,true,true,0); // actual next record bind
            bus.Present(9000);
            Check(c.Record!=previous && ReferenceEquals(bus.Model.Record,c.Record)
                && c.PresentationFrame(9000)==0,"new phase starts from its own clock/record, not old frame220");
            Check(commands==3 && batches==0,"all render calls preserve native command/batch counts");
        }
        finally { bus.Root.Free(); }
        var wrapped=new NativeBusController(anim,0,uint.MaxValue-99,_=>{},_=>{},(_,_)=>{},_=>{});
        wrapped.Update(uint.MaxValue-99,0x4000,0,true,true,0);
        wrapped.Update(uint.MaxValue-99,0x4000,0,true,true,0);
        Check(Math.Abs(wrapped.PresentationFrame(0,.5f)-3.015f)<.0001f,
            "fractional renderer respects unsigned native clock wrap");
        GD.Print($"NATIVE BUS {name}: fractional presentation, endpoint clamp and unchanged simulation PASS");
    }

}
