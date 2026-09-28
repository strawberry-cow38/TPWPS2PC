using System.Text.Json;
using System.Reflection;
using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;
namespace TPWPS2Viewer.Tests;

public partial class AnimatedModelSaveSmoke : Node
{
    int checks;
    void Check(bool ok,string why) { if(!ok) throw new Exception(why); checks++; }
    static string Json(object value) => JsonSerializer.Serialize(value);
    static Model Asset(AnimatedModel a) => (Model)typeof(AnimatedModel).GetField("_model",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(a);
    public override void _Ready()
    {
        try {
            using var lib=new AssetLibrary("/home/ec2-user/tpw-ps2/tpw_ps2.bin"); // Disc read-only, no extraction
            lib.OpenWad("/DATA/DATA.WAD");
            var guest=lib.Rides.First(r=>r.Name.EndsWith("/boy1a.mps",StringComparison.OrdinalIgnoreCase));
            Run(lib,guest,true);
            // Same constructors/scopes as normal presentation; staff isn't a ride sharing fixture.
            var staff=lib.Rides.First(r=>r.Animation!=null && r.Name.Contains("handyman",StringComparison.OrdinalIgnoreCase));
            Run(lib,staff,true);
            var head=lib.Rides.First(r=>r.Name.EndsWith("/advisor.mps",StringComparison.OrdinalIgnoreCase));
            var immutableHead=new Model(lib.Read(head.Model));
            var headAnimation=new Aps(lib.Read(head.Animation));
            var writableHead=new AnimatedModel(new Model((byte[])immutableHead.D.Clone()),headAnimation,null,_=>(null,false),nativeNodeVisibility:true);
            try {
                writableHead.WriteNodeFlags(0,0x80000010,0);
                var hs=writableHead.CaptureState("head","head-aps");
                var restoredHead=AnimatedModel.FromState(hs,new AnimatedModel.SnapshotBindings {
                    ModelAssetId="head",AnimationAssetId="head-aps",Model=immutableHead,Animation=headAnimation,Texture=_=>(null,false)});
                try {Check(Json(hs)==Json(restoredHead.CaptureState("head","head-aps")),"separate writable head flags, not shared character bytes");}
                finally {restoredHead.Root.Free();}
            } finally {writableHead.Root.Free();}
            lib.OpenWad("/DATA/JUNGLE.WAD");
            Run(lib,lib.Rides.First(r=>r.Name.EndsWith("/monkey.mps",StringComparison.OrdinalIgnoreCase)),false);
            Run(lib,lib.Rides.First(r=>r.Animation!=null && r.Name.Contains("shops/",StringComparison.OrdinalIgnoreCase)),false);
            GD.Print($"ANIMATED MODEL SAVE PASS: {checks} checks");GetTree().Quit();
        } catch(Exception e) { GD.PrintErr("ANIMATED MODEL SAVE FAIL: "+e);GetTree().Quit(2); }
    }
    void Run(AssetLibrary lib,AssetLibrary.RideAssets entry,bool shared)
    {
        var model=new Model(lib.Read(entry.Model));var animation=new Aps(lib.Read(entry.Animation));
        var records=animation.Records().Where(r=>!r.Shared).ToArray();
        var rec=shared?records.First(r=>r.Skeletal):records.First(r=>!r.Skeletal);
        var textures=new Dictionary<string,(ImageTexture,bool)>();
        (ImageTexture,bool) Texture(string name) {
            if(textures.TryGetValue(name,out var found)) return found;
            var image=lib.Texture(entry,name);
            return textures[name]=image==null?(null,false):(ImageTexture.CreateFromImage(Image.CreateFromData(image.Width,image.Height,false,Image.Format.Rgba8,image.Pixels)),image.Translucent);
        }
        string id=lib.WadName+":"+entry.Name;
        var bindings=new AnimatedModel.SnapshotBindings{ModelAssetId=id,AnimationAssetId=id+":aps",Model=model,Animation=animation,Texture=Texture,ShareReadOnlyModel=shared};
        var a=new AnimatedModel(model,animation,rec,Texture,skeletalHideLists:shared);
        // Rides have per-ID assets: deliberately do NOT claim shared-ride isolation.
        var b=new AnimatedModel(shared?model:new Model((byte[])model.D.Clone()),animation,rec,Texture,skeletalHideLists:shared);
        try {
            a.SetFrame(3);b.SetFrame(7);
            if(shared) Check(a.Skeletal&&b.Skeletal&&ReferenceEquals(Asset(a),Asset(b))&&ReferenceEquals(a.LastWorld,b.LastWorld)&&ReferenceEquals(a.LastWorld,model.BindWorld),"PRECONDITION: two skeletal actors share same Model AND LastWorld");
            var world=model.BindWorld.ToDictionary(x=>x.Key,x=>x.Value);
            var locals=model.BindLocals.ToDictionary(x=>x.Key,x=>x.Value);
            var parents=model.BindParents.ToDictionary(x=>x.Key,x=>x.Value);
            var bytes=(byte[])model.D.Clone();
            string bBefore=Json(b.CaptureState(id,id+":aps"));
            AnimatedModel.Snapshot Capture()=>a.CaptureState(id,id+":aps");
            void Compare(AnimatedModel x) {
                Check(Json(Capture())==Json(x.CaptureState(id,id+":aps")),"all CPU inputs, hidden flags, layers, texture/pose/root exact");
                Check(a.TextureChoices.SequenceEqual(x.TextureChoices),"texture output");
                foreach(var mesh in model.Meshes) {
                    Check(a.MeshDrawn(mesh.Index)==x.MeshDrawn(mesh.Index),"visibility output");
                    var p=a.LivePositions(mesh.Offset);var q=x.LivePositions(mesh.Offset);
                    Check(p==null?q==null:p.SequenceEqual(q),"live positions output");
                }
                var src=a.Surfaces().ToArray();var dst=x.Surfaces().ToArray();
                for(int i=0;i<src.Length;i++) Check(src[i].Node.Transform==dst[i].Node.Transform&&src[i].Node.Visible==dst[i].Node.Visible,"actual surface pose/visibility");
            }
            void Cut() {
                var saved=Capture();string frozen=Json(saved);
                string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"tpw-model-"+Guid.NewGuid()+".sav");
                AnimatedModel.Snapshot disk;
                try{ParkSaveFile.Write(path,saved);disk=ParkSaveFile.Read<AnimatedModel.Snapshot>(path);}finally{System.IO.File.Delete(path);}
                var restored=AnimatedModel.FromState(disk,bindings);
                try {
                    Check(ReferenceEquals(Asset(restored),model)==shared && restored.Root.GetParent()==null,"explicit asset ownership and detached root");
                    if(shared) {
                        bool refused=false;try {restored.WriteNodeFlags(0,1,0);}catch(InvalidOperationException){refused=true;}
                        Check(refused,"restored shared actor refuses byte writes");
                    }
                    Compare(restored);
                    Check(Json(b.CaptureState(id,id+":aps"))==bBefore,"restore leaves actor B unchanged");
                    Check(bytes.SequenceEqual(model.D)&&world.All(x=>model.BindWorld[x.Key]==x.Value)&&locals.All(x=>model.BindLocals[x.Key]==x.Value)&&parents.All(x=>model.BindParents[x.Key]==x.Value),"all shared asset bytes/matrix/parent caches unchanged");
                    for(int frame=8;frame<12;frame++) {a.SetFrame(frame);restored.SetFrame(frame);Compare(restored);}
                    Check(Json(saved)==frozen,"source SetFrame cannot mutate checkpoint (scratch copies)");
                    // Mutate deserialized storage: restored ownership must not alias it either.
                    if(disk.Parts.Length>0) disk.Parts[0].Positions[0]+=19;
                    Compare(restored);
                } finally {restored.Root.Free();}
            }
            Cut();
            a.Root.Position=new Vector3(2,3,4);a.Root.Visible=false;
            a.Surfaces().First().Node.Transform=new Transform3D(Basis.Identity,new Vector3(4,5,6));
            Cut(); // retained forced surface transform, then normal future evaluation
            foreach(var next in records.Take(8)) {
                a.UseRecord(next);Cut(); // deliberately UNSAMPLED bind, old part poses retained
                a.SetFrame(Math.Max(0,next.DurationFrames*.35f));Cut();
            }
            if(a.Additive) { a.AddLayer(rec,2);Cut(); }
            a.UvRewrite=(_,_,_)=>{};
            bool callbackRejected=false;try {Capture();}catch(InvalidDataException){callbackRejected=true;}
            Check(callbackRejected,"unsupported UV callback explicitly rejected");a.UvRewrite=null;
            // Rebind a real provider-shaped UV callback without invoking it during staging.
            int sourceCalls=0,restoredCalls=0;
            a.UvRewrite=(_,_,uv)=>{sourceCalls++;for(int i=0;i<uv.Count;i++)uv[i]+=new Vector2(.125f,.25f);};
            a.Rebuild(_=>true);
            var uvState=a.CaptureState(id,id+":aps","test:uv-offset");int before=sourceCalls;
            var uvBindings=new AnimatedModel.SnapshotBindings {ModelAssetId=id,AnimationAssetId=id+":aps",Model=model,Animation=animation,Texture=Texture,
                ShareReadOnlyModel=shared,UvRewriteId="test:uv-offset",UvRewrite=(_,_,uv)=>{restoredCalls++;for(int i=0;i<uv.Count;i++)uv[i]+=new Vector2(.125f,.25f);}};
            var uvRestored=AnimatedModel.FromState(uvState,uvBindings);
            try {
                Check(restoredCalls==0&&sourceCalls==before,"stage does not run either world's UV callback");
                Check(Json(uvState)==Json(uvRestored.CaptureState(id,id+":aps","test:uv-offset")),"retained rewritten UVs exact");
                a.Rebuild(_=>true);uvRestored.Rebuild(_=>true);
                Check(sourceCalls>before&&restoredCalls>0,"future refresh uses both rebound providers");
                Check(Json(a.CaptureState(id,id+":aps","test:uv-offset"))==Json(uvRestored.CaptureState(id,id+":aps","test:uv-offset")),"UV continuation exact");
            }finally{uvRestored.Root.Free();a.UvRewrite=null;}
            // Malformed bounded state fails before any staging root is published.
            bool rejected=false;try {AnimatedModel.FromState(Capture() with {HiddenNodes=new[]{-1}},bindings);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"invalid node rejected");
            GD.Print("ANIMATED MODEL SAVE "+id+": PASS");
        } finally {a.Root.Free();b.Root.Free();}
    }
}
