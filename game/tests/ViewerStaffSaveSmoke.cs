using Godot;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using TPW.PS2.Data;
using Aps=TPW.PS2.Data.Animation;
namespace TPWPS2Viewer.Tests;

public partial class ViewerStaffSaveSmoke : Node
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static T F<T>(Viewer v,string n)=>(T)typeof(Viewer).GetField(n,Flags).GetValue(v);
    static void Set(Viewer v,string n,object x)=>typeof(Viewer).GetField(n,Flags).SetValue(v,x);
    static void Put(object o,string n,object x)=>o.GetType().GetField(n).SetValue(o,x);
    static T Get<T>(object o,string n)=>(T)o.GetType().GetField(n).GetValue(o);
    static string Json(object o)=>JsonSerializer.Serialize(o);
    int checks;
    void C(bool b,string s){checks++;if(!b)throw new Exception(s);}
    static Model Terrain() {
        var d=new byte[0x240];void U(int a,uint v)=>BitConverter.GetBytes(v).CopyTo(d,a);void F(int a,float v)=>BitConverter.GetBytes(v).CopyTo(d,a);
        U(0,Model.Magic);U(0x44,0x140);U(0x48,0x80);d[0x30]=1;U(0x14c,8);U(0x150,8);F(0x158,1);
        for(int i=0;i<64;i++)d[0x170+i*2+1]=1;U(0xd4,0x220);System.Text.Encoding.ASCII.GetBytes("heightfield\0").CopyTo(d,0x220);
        for(int i=0;i<4;i++)F(0x90+i*20,1);F(0x100,8*1.004f);F(0x108,8*1.004f);
        var m=new Model(d);m.Materials.Add("sentinel");m.Materials.Add("jpa_str1");return m;
    }
    public override void _Ready() {
        var viewers=new List<Viewer>();string file=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"ViewerStaffSaveSmoke.json");
        try {
            using var lib=new AssetLibrary("/home/ec2-user/tpw-ps2/tpw_ps2.bin");lib.OpenWad("/DATA/DATA.WAD");
            var asset=lib.Rides.First(r=>r.Name.Contains("handy",StringComparison.OrdinalIgnoreCase)&&r.Animation!=null);
            string path=asset.Name,apath=System.IO.Path.ChangeExtension(path,".aps");
            var model=new Model(lib.Read(asset.Model));var aps=new Aps(lib.Read(asset.Animation));var table=NativeLogicalAnimationTable.Read(lib.Disc);
            var la=lib.Rides.First(r=>r.Name.EndsWith("litter1.mps",StringComparison.OrdinalIgnoreCase));var lm=new Model(lib.Read(la.Model));
            Viewer New(){var v=new Viewer();viewers.Add(v);return v;}
            (ParkStaff Staff,ParkVisitors Visitors,StaffMember Member,LitterItem Litter) Core(){var paths=new ParkPaths(Terrain());var visitors=new ParkVisitors(new ParkSim(paths),new GuestWalk(paths));var staff=new ParkStaff(visitors,new ParkClock(),new NativeActivationSequence(0,"smoke"),_=>0);return(staff,visitors,staff.Free(StaffKind.Handyman)[0],staff.Litter.Drop(new(384,384),false));}
            var source=New();var sc=Core();var target=New();var tc=Core();var litterOwners=new Dictionary<Node3D,AnimatedModel>();
            Viewer.StaffStateBindings Bind(ParkStaff staff,ParkVisitors visitors,StaffMember member,LitterItem litter)=>new(){Staff=staff,Visitors=visitors,MemberId=m=>ReferenceEquals(m,member)?"member/object":throw new Exception("unknown member"),Member=id=>id=="member/object"?member:throw new Exception("bad member ID"),LitterId=l=>ReferenceEquals(l,litter)?"litter/object":throw new Exception("unknown litter"),Litter=id=>id=="litter/object"?litter:throw new Exception("bad litter ID"),ModelRegistry=null,LitterRenderer=n=>litterOwners[n],Characters=new(){GuestAtSlot=_=>throw new Exception(),GuestId=_=>throw new Exception(),Guest=_=>throw new Exception(),Models=new Dictionary<string,Viewer.GuestModelAsset>{{"staff-model",new(path,model)},{"litter-model",new(la.Name,lm)}},Animations=new Dictionary<string,Viewer.GuestAnimationAsset>{{"staff-aps",new(apath,aps)}},Textures=new Dictionary<string,Viewer.GuestTextureAsset>(),Texture=(_,_) =>(null,false),LogicalTable=table}};
            var sb=Bind(sc.Staff,sc.Visitors,sc.Member,sc.Litter);var tb=Bind(tc.Staff,tc.Visitors,tc.Member,tc.Litter);
            Set(source,"_staff",sc.Staff);Set(source,"_staffVisitors",sc.Visitors);Set(source,"_logicalAnimations",table);Set(source,"_hireHeld",sc.Member);Set(source,"_hireCarried",true);
            F<HashSet<int>>(source,"_staffModelMisses").Add(918);
            F<Dictionary<string,Model>>(source,"_charModels").Add(path,model);F<Dictionary<string,Model>>(source,"_charModels").Add(la.Name,lm);F<Dictionary<string,Aps>>(source,"_charAnims").Add(apath,aps);
            var root=new Node3D{Name="staff",Position=new(2,3,4)};source.AddChild(root);Set(source,"_staffRoot",root);
            var records=aps.Records().GroupBy(r=>r.Slot).SelectMany(g=>g.Select((r,i)=>(Key:(g.Key,i),Record:r))).ToDictionary(x=>x.Key,x=>x.Record);
            var walk=records[(0,0)];var draw=new AnimatedModel(model,aps,walk,_=>(null,false),skeletalHideLists:true);draw.SetFrame(3);
            var node=new Node3D{Name="actual-staff",Visible=false,Position=new(1,2,3)};node.AddChild(draw.Root);root.AddChild(node);
            var actor=Activator.CreateInstance(typeof(Viewer).GetNestedType("StaffActor",BindingFlags.NonPublic),true);
            var native=new NativeGuestAnimation(table,(s,v)=>records.TryGetValue((s,v),out var r)?r.DurationFrames:null){Requested=9};
            foreach(var pair in new Dictionary<string,object>{{"Member",sc.Member},{"Serial",(uint)73},{"Node",node},{"Drawn",draw},{"Anim",aps},{"Animation",native},{"Records",records},{"Showing",walk},{"ModelPath",path},{"Prev",new Vector3(9,8,7)},{"Yaw",1.23f}})Put(actor,pair.Key,pair.Value);
            F<IDictionary>(source,"_staffActors").Add(sc.Member,actor);
            var ln=new Node3D{Name="actual-litter",Position=new(4,0,5)};var ld=new AnimatedModel(lm,null,null,_=>(null,false));ld.SetFrame(0);ln.AddChild(ld.Root);root.AddChild(ln);litterOwners.Add(ln,ld);
            F<SnapshotReferenceMap<LitterItem,(Node3D,uint,int,string)>>(source,"_litterActors").Add(sc.Litter,(ln,81,sc.Litter.ModelId,la.Name));
            var rng=F<NewlibRand>(source,"_staffAnimationRand");for(int i=0;i<8;i++){native.Push();native.Update(40,rng.Next);}
            var saved=source.CaptureStaffPresentation(sb);string frozen=Json(saved);ParkSaveFile.Write(file,saved);
            var disk=ParkSaveFile.Read<Viewer.StaffPresentationState>(file);target.RestoreRuntimeState(source.CaptureRuntimeState());
            // Existing cache entries model the guest-first join; ownership must not change.
            var cache=F<Dictionary<string,Model>>(target,"_charModels");cache.Add(path,model);
            uint before=F<NewlibRand>(target,"_staffAnimationRand").CaptureState();var join=target.RestoreStaffPresentation(disk,tb);
            foreach(var p in join.LitterRenderers)litterOwners.Add(join.Litter[p.Key],p.Value);
            C(before==F<NewlibRand>(target,"_staffAnimationRand").CaptureState(),"restore consumed shared RNG");
            C(ReferenceEquals(cache,F<Dictionary<string,Model>>(target,"_charModels"))&&ReferenceEquals(cache[path],model),"readonly shared cache ownership");
            C(Json(target.CaptureStaffPresentation(tb))==frozen,"file/actual Viewer roundtrip");
            var restored=F<IDictionary>(target,"_staffActors")[tc.Member];C(!ReferenceEquals(sc.Member,tc.Member)&&Get<StaffMember>(restored,"Member")==tc.Member,"caller registry identity");
            C(ReferenceEquals(Get<Aps.Record>(restored,"Showing"),Get<AnimatedModel>(restored,"Drawn").Record),"Showing/Drawn/Records identity");
            var trng=F<NewlibRand>(target,"_staffAnimationRand");var tn=Get<NativeGuestAnimation>(restored,"Animation");
            for(int i=0;i<20;i++){native.Push();tn.Push();native.Update(40,rng.Next);tn.Update(40,trng.Next);typeof(Viewer).GetMethod("PoseStaff",Flags).Invoke(source,new[]{actor,(object).35f});typeof(Viewer).GetMethod("PoseStaff",Flags).Invoke(target,new[]{restored,(object).35f});C(Json(source.CaptureStaffPresentation(sb))==Json(target.CaptureStaffPresentation(tb)),"future actual PoseStaff/native continuation");}
            C(Json(saved)==frozen,"checkpoint mutated by future sampling");
            void Reject(Viewer.StaffPresentationState bad,string why) {
                var v=New();v.RestoreRuntimeState(source.CaptureRuntimeState());
                // Match the saved cut except for the specifically damaged field.
                var current=source.CaptureStaffPresentation(sb);bad=bad with {Random=current.Random};
                bool failed=false;try{v.RestoreStaffPresentation(bad,tb);}catch(Exception){failed=true;}
                C(failed,why);C(F<IDictionary>(v,"_staffActors").Count==0&&F<Node3D>(v,"_staffRoot")==null&&v.GetChildCount()==0,"failed stage remains unpublished");
            }
            Reject(saved with {Actors=new[]{saved.Actors[0] with {Member="unregistered/object"}}},"unknown member rejected instead of slot lookup");
            Reject(saved with {Actors=new[]{saved.Actors[0] with {DrawnRecord=999999}}},"invalid record rejected");
            Reject(saved with {Litter=new[]{saved.Litter[0] with {Litter="unregistered/litter"}}},"unknown litter rejected");
            Reject(saved with {Actors=new[]{saved.Actors[0] with {Yaw=float.NaN}}},"nonfinite yaw rejected");
            // Lazy root remains null, rather than manufacturing EnsureStaff topology.
            var empty=New();var eb=Bind(null,null,sc.Member,sc.Litter);Set(empty,"_logicalAnimations",table);var es=empty.CaptureStaffPresentation(eb);var fresh=New();fresh.RestoreStaffPresentation(es,eb);C(F<Node3D>(fresh,"_staffRoot")==null,"lazy root");
            GD.Print($"VIEWER STAFF SAVE SMOKE PASS {checks} checks");
        }catch(Exception e){GD.PrintErr(e);GetTree().Quit(1);return;}
        finally{if(System.IO.File.Exists(file))System.IO.File.Delete(file);foreach(var v in viewers){F<Weather>(v,"_weather").Root.Free();F<EntranceFlags>(v,"_flags").Root.Free();F<ThoughtBubbles>(v,"_thoughts").Root.Free();v.Free();}}
        GetTree().Quit();
    }
}
