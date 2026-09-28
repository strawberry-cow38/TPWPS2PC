using Godot;
using System.Reflection;
using System.Text.Json;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;
namespace TPWPS2Viewer.Tests;

// Bounded real-Godot owner join; deliberately never invokes Viewer._Ready or replays the park.
public partial class ViewerGuestSaveSmoke : Node
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static T F<T>(Viewer v,string name)=>(T)typeof(Viewer).GetField(name,Flags).GetValue(v);
    static void Set(Viewer v,string name,object value)=>typeof(Viewer).GetField(name,Flags).SetValue(v,value);
    static void Call(Viewer v,string name,params object[] args)=>typeof(Viewer).GetMethod(name,Flags).Invoke(v,args);
    static string Json(object o)=>JsonSerializer.Serialize(o);
    int checks;
    void C(bool ok,string why) {checks++;if(!ok)throw new Exception(why);}
    static void Dispose(Viewer v) {
        F<Weather>(v,"_weather").Root.Free();F<EntranceFlags>(v,"_flags").Root.Free();F<ThoughtBubbles>(v,"_thoughts").Root.Free();v.Free();
    }
    public override void _Ready()
    {
        var viewers=new List<Viewer>();
        try {
            using var lib=new AssetLibrary("/home/ec2-user/tpw-ps2/tpw_ps2.bin");lib.OpenWad("/DATA/DATA.WAD");
            var asset=lib.Rides.First(r=>r.Name.EndsWith("/boy1a.mps",StringComparison.OrdinalIgnoreCase));
            string path="/Chars/Boy1a/boy1a.mps",apsPath="/Chars/Boy1a/boy1a.aps";
            var table=NativeLogicalAnimationTable.Read(lib.Disc);
            Viewer New() {var v=new Viewer();viewers.Add(v);return v;}
            Viewer.GuestStateBindings Bind(Guest[] guests,Model m,Aps a)=>new() {
                GuestAtSlot=slot=>guests.Take(2).Single(g=>g.Id==slot),GuestId=g=>"guest-object-"+Array.IndexOf(guests,g),
                Guest=id=>guests[int.Parse(id["guest-object-".Length..])],
                Models=new Dictionary<string,Viewer.GuestModelAsset>{{"boy-model",new(path,m)}},
                Animations=new Dictionary<string,Viewer.GuestAnimationAsset>{{"boy-aps",new(apsPath,a)},{"missing-aps",new("/trusted/missing.aps",null)}},
                Textures=new Dictionary<string,Viewer.GuestTextureAsset>{{"missing-texture",new(path+"|missing",null,false)}},
                Texture=(_,_) =>(null,false),LogicalTable=table};
            Guest[] Guests()=>new[]{new Guest{Id=2},new Guest{Id=10},new Guest{Id=2}}; // ID reuse: inactive old body is a DISTINCT registry object
            var source=New();var sg=Guests();var model=new Model(lib.Read(asset.Model));var aps=new Aps(lib.Read(asset.Animation));var sb=Bind(sg,model,aps);
            F<Dictionary<string,Model>>(source,"_charModels").Add(path,model);
            var ac=F<Dictionary<string,Aps>>(source,"_charAnims");ac.Add(apsPath,aps);ac.Add("/trusted/missing.aps",null);
            F<Dictionary<string,(ImageTexture,bool)>>(source,"_charTex").Add(path+"|missing",(null,false));
            var root=new Node3D{Name="guests",Position=new(3,2,1)};source.AddChild(root);Set(source,"_guestRoot",root);Set(source,"_logicalAnimations",table);
            Set(source,"_nativeGuestAnimation",true);Set(source,"_nativeIdleAll",true);Set(source,"_parkTicks",333);
            Set(source,"_mouth",new List<ParkCell>{new(2,3),new(4,5)});Set(source,"_guestPool",new List<ParkCell>{new(6,7)});
            Set(source,"_guestPoolAt",731L);Set(source,"_guestPoolLaid",9);Set(source,"_idleScene",true);Set(source,"_idleSeeded",true);
            var lengths=aps.Records().GroupBy(r=>r.Slot).SelectMany(g=>g.Select((r,i)=>(g.Key,i,r.DurationFrames))).ToDictionary(x=>(x.Key,x.i),x=>x.DurationFrames);
            var native=F<Dictionary<Guest,NativeGuestAnimation>>(source,"_nativeAnimations");var rng=F<NewlibRand>(source,"_nativeAnimationRand");
            foreach(var g in sg)native.Add(g,new NativeGuestAnimation(table,(s,v)=>lengths.TryGetValue((s,v),out int n)?n:null){Requested=11,Stamp=137});
            bool held=false;
            for(int tick=0;tick<1000;tick++) {
                foreach(var n in native.Values){n.Push();n.Update(40,rng.Next);}
                if(native.Values.Any(n=>n.Held!=null)){held=true;break;}
            }
            C(held,"real native held pose reached within bounded fixture");
            foreach(var g in sg.Take(2)) {
                var recs=aps.Records().ToArray();var walk=recs.First(r=>r.Slot==1);var idles=recs.Where(r=>r.Slot==2).ToArray();var sit=recs.First(r=>r.Slot==3);
                var draw=new AnimatedModel(model,aps,walk,_=>(null,false));draw.SetFrame(g.Id==2?3:7);
                var actor=new Node3D{Name="Guest_"+g.Id,Position=new(g.Id,.5f,-3),Rotation=new(0,.3f,0)};actor.AddChild(draw.Root);root.AddChild(actor);
                draw.Root.Position=new(.1f,.2f,.3f);
                F<Dictionary<int,Node3D>>(source,"_actors").Add(g.Id,actor);
                F<Dictionary<int,(AnimatedModel,Aps.Record,string)>>(source,"_drawn").Add(g.Id,(draw,sit,"trusted own file"));
                F<Dictionary<int,(Aps,Aps.Record,Aps.Record,Model)>>(source,"_walkRec").Add(g.Id,(aps,walk,idles[1],model));
                F<Dictionary<int,Aps.Record[]>>(source,"_idles").Add(g.Id,idles);
                F<Dictionary<int,Aps.Record>>(source,"_gaitRec").Add(g.Id,walk);
                F<Dictionary<int,Aps.Record>>(source,"_nativeDrawn").Add(g.Id,walk);
                F<Dictionary<int,int>>(source,"_gaitFrom").Add(g.Id,123);F<Dictionary<int,int>>(source,"_idleSince").Add(g.Id,201);
                F<Dictionary<int,int>>(source,"_guestDwell").Add(g.Id,37);
                F<Dictionary<int,Vector3>>(source,"_guestPrev").Add(g.Id,new(8,.125f,9));
                var body=draw.Root.GetChildren().OfType<MeshInstance3D>().Skip(1).ToList();body.ForEach(n=>n.Visible=false);
                F<Dictionary<int,(Vector3,Vector3,List<MeshInstance3D>,string)>>(source,"_parts").Add(g.Id,(new(.1f,.2f,.3f),new(.4f,.5f,.6f),body,"head"));
                F<HashSet<int>>(source,"_headOnly").Add(g.Id);
            }
            var d=F<Dictionary<int,(AnimatedModel Drawn,Aps.Record Sit,string Where)>>(source,"_drawn");
            C(ReferenceEquals(d[2].Drawn.LastWorld,d[10].Drawn.LastWorld),"two real actors share BindWorld before snapshot");
            // Unsampled switch: retained mesh pose differs from current bound record.
            d[10].Drawn.UseRecord(F<Dictionary<int,Aps.Record[]>>(source,"_idles")[10][0]);
            var runtime=source.CaptureRuntimeState();uint beforeRng=rng.CaptureState();
            var snapshot=source.CaptureGuestPresentation(sb);string frozen=Json(snapshot);
            C(beforeRng==rng.CaptureState(),"capture draws no RNG");
            string file=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"guest-join-"+Guid.NewGuid().ToString("N")+".save");
            Viewer.GuestPresentationState loaded;
            try{ParkSaveFile.Write(file,snapshot);loaded=ParkSaveFile.Read<Viewer.GuestPresentationState>(file);}finally{System.IO.File.Delete(file);}
            C(Json(snapshot)==frozen,"retained source checkpoint unchanged after file write");
            var a=New();var b=New();var ag=Guests();var bg=Guests();
            var ab=Bind(ag,new Model(lib.Read(asset.Model)),new Aps(lib.Read(asset.Animation)));
            var bb=Bind(bg,new Model(lib.Read(asset.Model)),new Aps(lib.Read(asset.Animation)));
            a.RestoreRuntimeState(runtime);b.RestoreRuntimeState(runtime);
            var nodes=a.RestoreGuestPresentation(loaded,ab);b.RestoreGuestPresentation(loaded,bb);
            C(Json(a.CaptureGuestPresentation(ab))==frozen&&Json(b.CaptureGuestPresentation(bb))==frozen,"actual dictionaries FILE roundtrip, retained caches and unsampled switch");
            C(nodes.Count==2&&nodes["guest-object-0"]!=d[2].Drawn.Root.GetParent()&&nodes.Values.All(n=>!n.IsInsideTree()),"fresh detached guest/node associations");
            var an=F<Dictionary<Guest,NativeGuestAnimation>>(a,"_nativeAnimations");var bn=F<Dictionary<Guest,NativeGuestAnimation>>(source,"_nativeAnimations");
            C(an.Count==3&&an.ContainsKey(ag[0])&&an.ContainsKey(ag[2])&&!an.ContainsKey(sg[0]),"same display Id retained as distinct guest identities");
            var ar=F<NewlibRand>(a,"_nativeAnimationRand");var br=F<NewlibRand>(source,"_nativeAnimationRand");
            C(ar.CaptureState()==beforeRng&&br.CaptureState()==beforeRng,"restore no RNG consumption");
            for(int tick=0;tick<160;tick++) {
                for(int i=0;i<3;i++){int want=tick%40<20?13:11;an[ag[i]].Requested=want;bn[sg[i]].Requested=want;an[ag[i]].Push();bn[sg[i]].Push();an[ag[i]].Update(40,ar.Next);bn[sg[i]].Update(40,br.Next);}
                foreach(int id in new[]{2,10}) {Call(a,"NativeDrawnRecord",id,.375f);Call(source,"NativeDrawnRecord",id,.375f);}
                C(Json(a.CaptureGuestPresentation(ab))==Json(source.CaptureGuestPresentation(sb)),"native/gait render continuation "+tick);
                C(ar.CaptureState()==br.CaptureState(),"shared stream continuation");
            }
            // Exercise actual ordinary Gait and its record-reference cache, not just dispatcher DTOs.
            Set(a,"_nativeGuestAnimation",false);Set(source,"_nativeGuestAnimation",false);
            for(int tick=0;tick<24;tick++) {
                Set(a,"_parkTicks",500+tick);Set(source,"_parkTicks",500+tick);
                foreach(int id in new[]{2,10}){Call(a,"Gait",id,tick%8<4,.25f);Call(source,"Gait",id,tick%8<4,.25f);}
                C(Json(a.CaptureGuestPresentation(ab))==Json(source.CaptureGuestPresentation(sb)),"ordinary gait continuation");
            }
            void Reject(Viewer.GuestPresentationState bad,string why) {
                var target=New();target.RestoreRuntimeState(runtime);bool failed=false;
                try{target.RestoreGuestPresentation(bad,ab);}catch(Exception){failed=true;}
                C(failed&&F<Dictionary<int,Node3D>>(target,"_actors").Count==0&&F<Node3D>(target,"_guestRoot")==null
                    &&F<NewlibRand>(target,"_nativeAnimationRand").CaptureState()==beforeRng,why);
            }
            Reject(loaded with{Random=beforeRng+1},"RNG mismatch rejects without mutation");
            Reject(loaded with{Entries=loaded.Entries.Append(loaded.Entries[0]).ToArray()},"duplicate guest/slot rejects staged topology");
            Reject(loaded with{Records=loaded.Records.Select((r,i)=>i==0?r with{Offset=int.MaxValue}:r).ToArray()},"bad record rejects");
            var changed=(Viewer.GuestPresentationEntry[])loaded.Entries.Clone();changed[0]=changed[0] with{Previous=new[]{float.NaN,0f,0f}};
            Reject(loaded with{Entries=changed},"late malformed pose cleans staged nodes, no join");
            d[2].Drawn.SetFrame(12);C(Json(snapshot)==frozen,"checkpoint remains isolated throughout source continuation and sampling");
            GD.Print($"VIEWER GUEST SAVE PASS: {checks} checks (actual dictionaries, not wholeworld)");GetTree().Quit();
        } catch(Exception e) {GD.PrintErr("VIEWER GUEST SAVE FAIL: "+e);GetTree().Quit(2);}
        finally {foreach(var v in viewers)Dispose(v);}
    }
}
