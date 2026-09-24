using Godot;
using TPW.PS2.Data;
using V = System.Numerics.Vector3;
using M = System.Numerics.Matrix4x4;
using Aps = TPW.PS2.Data.Animation;
namespace TPWPS2Viewer.Tests;

/// <summary>Checks model-owned paths through the shipping AnimatedModel consumer, not just its decoder.</summary>
public partial class ModelPathAudit : Node
{
    int checks;
    void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
    static bool Near(V a, V b, float eps = .002f) => V.Distance(a, b) < eps;
    static V Vec(Godot.Vector3 v) => new(v.X,v.Y,v.Z);
    public override void _Ready()
    {
        try
        {
            Arithmetic();
            using var lib = new AssetLibrary(OS.GetEnvironment("TPW_PS2_DISC"));
            int otherTracks = 0, otherMotion = 0;
            foreach (string world in new[]{"JUNGLE","HALLOW","SPACE","FANTASY"})
            {
                lib.OpenWad("/DATA/"+world+".WAD");
                foreach(var ride in lib.Rides.Where(r=>r.Animation != null && !r.Name.Contains("/bus", StringComparison.OrdinalIgnoreCase)))
                {
                    var a = new Aps(lib.Read(ride.Animation));
                    var records = a.Records().Where(r=>!r.Skeletal && !r.Shared).ToArray();
                    if(!records.Any(r=>Enumerable.Range(0,r.TrackCount).Any(i=>(a.TrackFlags(a.TrackAt(r,i))&0x201)==0x200))) continue;
                    var m = new Model(lib.Read(ride.Model));
                    foreach(var r in records)
                    {
                        var channels=Enumerable.Range(0,r.TrackCount).Select(i=>a.TrackAt(r,i))
                            .Select(t=>(Node:a.TrackNode(t),Channel:ModelPathChannel.Read(m,a,r,t)))
                            .Where(p=>p.Channel!=null).ToArray();
                        if(channels.Length==0) continue;
                        var drawn=new AnimatedModel(m,a,r,_=>(null,false));AddChild(drawn.Root);
                        try
                        {
                            drawn.SetFrame(0);var first=drawn.LastWorld.ToDictionary(p=>p.Key,p=>p.Value);
                            foreach(float frame in new[]{r.DurationFrames*.25f,r.DurationFrames*.5f,(float)r.DurationFrames})
                            {
                                drawn.SetFrame(frame);
                                foreach(var p in channels)
                                {
                                    var actual=drawn.LastWorld[m.NodeOffset(p.Node)];
                                    Check(float.IsFinite(actual.M11+actual.M22+actual.M33+actual.M41+actual.M42+actual.M43),"other carrier finite transform "+ride.Name);
                                    if(V.Distance(first[m.NodeOffset(p.Node)].Translation,actual.Translation)>.01f) otherMotion++;
                                }
                            }
                            otherTracks+=channels.Length;
                        }
                        finally { drawn.Root.Free(); }
                    }
                }
                foreach (string stem in new[]{"bus1","bus2"})
                {
                    var ride = lib.Rides.Single(r => r.Name.Equals($"features/{stem}/{stem}.mps",StringComparison.OrdinalIgnoreCase));
                    var model = new Model(lib.Read(ride.Model));
                    var anim = new Aps(lib.Read(ride.Animation));
                    foreach (var rec in anim.Records().Where(r=>r.Slot==5)) Run(model,anim,rec,world+"/"+stem);
                }
            }
            Check(otherTracks>0 && otherMotion>0,"non-bus carriers exercise shipping consumer too");
            GD.Print($"MODEL PATH OTHER: {otherTracks} tracks, {otherMotion} moving samples");
            GD.Print($"MODEL PATH PASS: {checks} checks, eight buses / four worlds / three records");
            GetTree().Quit();
        }
        catch(Exception e) { GD.PrintErr("MODEL PATH FAIL: "+e); GetTree().Quit(2); }
    }
    void Arithmetic()
    {
        var points = new[]{V.Zero,new V(1,0,0),new V(2,0,0),new V(3,0,0),new V(4,0,0),new V(5,0,0)};
        var open = new ModelPathChannel(2,0x600,points,new float[]{0,50});
        var closed = new ModelPathChannel(3,0x600,points,new float[]{0,50});
        Check(open.SpanCount==1 && closed.SpanCount==2,"topology flag, not divisibility");
        Check(Near(open.Sample(1).Position,new V(1.5f,0,0)),"literal open cubic midpoint");
        Check(Near(closed.Sample(1).Position,new V(3,0,0)),"closed uses extra span");
        Check(open.Percentage(2)==50 && closed.Percentage(2)==0,"open holds final sample; closed endpoint wraps");
        var wrap = new ModelPathChannel(2,0x600,points,new float[]{95,5});
        Check(wrap.Percentage(.5f)==0 && wrap.Percentage(0)==95,"shortest percentage wrap");
        var tie = new ModelPathChannel(2,0x600,points,new float[]{0,50});
        Check(tie.Percentage(.5f)==25,"exact fifty does not wrap");
        var scaled=M.CreateScale(7); var applied=open.Apply(scaled,1);
        Check(Near(new(applied.M31,applied.M32,applied.M33),V.UnitX),"derivative facing");
        Check(MathF.Abs(new V(applied.M11,applied.M12,applied.M13).Length()-1)<.0001,"facing writes unit basis, not old bind scale");
        Check(open.Apply(scaled,3)==scaled,"out-of-duration skips channel");
        var noFacing = new ModelPathChannel(2,0x200,points,new float[]{0,50},true).Apply(scaled,1);
        Check(noFacing.M11==7 && Near(noFacing.Translation,new V(1.5f,0,0)),"translation without facing");
    }
    void Run(Model model,Aps anim,Aps.Record rec,string name)
    {
        int track=Enumerable.Range(0,rec.TrackCount).Select(i=>anim.TrackAt(rec,i)).Single(t=>(anim.TrackFlags(t)&0x200)!=0);
        int node=anim.TrackNode(track),off=model.NodeOffset(node);
        var channel=ModelPathChannel.Read(model,anim,rec,track);
        Check(channel!=null && channel.CurveFlags==2 && channel.Points.Count==45 && channel.SpanCount==14,"bus open 14-span source control");
        // Parent-field oracle: poison the child's same offset, then the real parent's.
        ushort old=BitConverter.ToUInt16(model.D,off+0x52);
        BitConverter.GetBytes((ushort)65535).CopyTo(model.D,off+0x52);
        Check(ModelPathChannel.Read(model,anim,rec,track)!=null,"child index is not binding");
        BitConverter.GetBytes(old).CopyTo(model.D,off+0x52);
        int parent=model.NodeOffset(model.NodeParent(node)); old=BitConverter.ToUInt16(model.D,parent+0x52);
        BitConverter.GetBytes((ushort)65535).CopyTo(model.D,parent+0x52);
        bool refused=false; try { ModelPathChannel.Read(model,anim,rec,track); } catch(InvalidDataException) { refused=true; }
        BitConverter.GetBytes(old).CopyTo(model.D,parent+0x52);
        Check(refused,"bad parent index must refuse");
        var drawn=new AnimatedModel(model,anim,rec,_=>(null,false)); AddChild(drawn.Root);
        try
        {
            var body=model.Meshes.Single(m=>m.Index==node);
            var surfaces=drawn.Root.GetChildren().OfType<MeshInstance3D>().Where(m=>m.Name.ToString().StartsWith(body.Name+"#")).ToArray();
            Check(surfaces.Length>0,"actual body surfaces required");
            int drawnSamples=0; V first=default; bool moved=false,turned=false; V firstDirection=default;
            foreach(int frame in new[]{0,1,rec.DurationFrames/4,rec.DurationFrames/2,rec.DurationFrames-1,rec.DurationFrames})
            {
                // Independent direct-file oracle, exact integer frames: no production sampler.
                int indirect=BitConverter.ToInt32(anim.D,track+0x1c),samples=BitConverter.ToInt32(anim.D,indirect);
                float percentage=BitConverter.ToSingle(anim.D,samples+Math.Min(frame,rec.DurationFrames-1)*4);
                double u=14*((percentage+1000)%100)/100.0; int j=(int)u; double t=u-j;
                int desc=BitConverter.ToInt32(model.D,0x78),ptr=BitConverter.ToInt32(model.D,desc+8);
                V P(int k)=>new(BitConverter.ToSingle(model.D,ptr+k*12),BitConverter.ToSingle(model.D,ptr+k*12+4),BitConverter.ToSingle(model.D,ptr+k*12+8));
                var a=P(j*3);var b=P(j*3+1);var c=P(j*3+2);var d=P(j*3+3);
                var pos=(float)Math.Pow(1-t,3)*a+(float)(3*t*(1-t)*(1-t))*b+(float)(3*t*t*(1-t))*c+(float)(t*t*t)*d;
                var f=V.Normalize((float)(3*(1-t)*(1-t))*(b-a)+(float)(6*t*(1-t))*(c-b)+(float)(3*t*t)*(d-c));
                var right=V.Normalize(V.Cross(V.UnitY,f));var up=V.Cross(f,right);
                var local=new M(right.X,right.Y,right.Z,0,up.X,up.Y,up.Z,0,f.X,f.Y,f.Z,0,pos.X,pos.Y,pos.Z,1);
                var expected=local*model.WorldTransforms()[parent];
                drawn.SetFrame(frame);
                if(frame==0) { first=expected.Translation; firstDirection=f; }
                moved |= V.Distance(first,expected.Translation)>1;
                turned |= V.Distance(firstDirection,f)>.2f;
                Check(Near(drawn.LastWorld[off].Translation,expected.Translation),$"{name} rec{rec.Offset:x} frame{frame} world position");
                Check(Near(new(drawn.LastWorld[off].M31,drawn.LastWorld[off].M32,drawn.LastWorld[off].M33),new(expected.M31,expected.M32,expected.M33)),"actual facing");
                foreach(var surface in surfaces.Where(s=>s.Visible))
                {
                    drawnSamples++;
                    Check(Near(Vec(surface.Transform.Origin),expected.Translation),"body surface position");
                    Check(Near(Vec(surface.Transform.Basis.Z),new(expected.M31,expected.M32,expected.M33)),"body surface facing");
                }
            }
            Check(moved,"record must move, not pass in bind pose");
            Check(drawnSamples>0,"rendered assertions cannot pass on wholly invisible body");
            if(rec.DurationFrames==220) Check(turned,"approach/departure must turn, not only translate");
            GD.Print($"MODEL PATH {name} record@{rec.Offset:x} moves={moved} turns={turned}");
            drawn.UseRecord(null);drawn.SetFrame(0);
            Check(!drawn.OverriddenNodes.Contains(node),"record switch clears path binding");
        }
        finally { drawn.Root.Free(); }
    }
}
