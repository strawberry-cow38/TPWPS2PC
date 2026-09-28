using Godot;
using TPW.PS2.Data;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Aps = TPW.PS2.Data.Animation;
namespace TPWPS2Viewer;

public partial class Viewer
{
    public sealed record CoasterAssetOrigin(string Wad, string Model, string Animation);
    public sealed class CoasterSceneBindings
    {
        public required Func<AnimatedModel,CoasterAssetOrigin,AnimatedModel.SnapshotBindings> IdentifyRenderer {get;init;}
        public required PlacedSceneBindings Assets {get;init;}
        public required Func<Texture2D,string> IdentifyTexture {get;init;}
        public required Func<string,Texture2D> ResolveTexture {get;init;}
    }
    public sealed record CoasterMaterialState(string Shader, string Texture, string TextureHash, int Priority, Dictionary<string,byte[]> Parameters);
    public sealed record CoasterSurfaceState(byte[] Arrays, int Material);
    public sealed record CoasterSegmentState(string Owner, int Node, CoasterSurfaceState[] Surfaces);
    public sealed record CoasterPylonState(string Owner,int Holder,int Model,CoasterAssetOrigin Origin);
    public sealed record CoasterCarState(string Car,int Holder,int Model,CoasterAssetOrigin Origin,string Mesh,string Hash,float[] Was,float[] Now);
    public sealed record CoasterViewState(int Id,string Ride,string Sim,string Track,string Dir,int Price,int Frame,
        CoasterSegmentState[] Segments,CoasterPylonState[] Pylons,CoasterCarState[] Cars,int[] Slots,int[] Red,
        int WinchSeen,long SeenTime,int[] Voices,int[][] Cells,CoasterStats Stats);
    public sealed record CoasterToolState(int? View,int Mode,string Ghost,int[] GhostAt,bool GhostOk,bool FromStation,string Why,
        int[][] Field,int FieldZ,bool FieldDone,int Height,int Bank,int Pick,double Hold,Dictionary<int,float> PlacedHeight);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CoasterSceneState(int Version,PlacedNodeState[] Nodes,Dictionary<int,Dictionary<string,byte[]>> Metadata,
        CoasterMaterialState[] Materials,CoasterViewState[] Views,CoasterToolState Tool);
    static void CS(bool ok,string why) {if(!ok)throw new InvalidDataException("Viewer COASTER SCENE: "+why);}
    static string CSHash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
    static string CSTexHash(Texture2D t)=>t==null?null:CSHash(t.GetImage().GetData());
    static byte[] CSPack(Variant v)=>GD.VarToBytes(v);
    static Variant CSRead(byte[] b) {CS(b!=null&&b.Length<=64*1024*1024,"CPU data budget");return GD.BytesToVar(b);}
    static Shader CSShader(string hash) {
        foreach(bool soft in new[]{false,true})foreach(bool linear in new[]{false,true}) {
            var s=Ps2Materials.Shader(soft,"cull_disabled",false,linear,false,true);
            if(CSHash(System.Text.Encoding.UTF8.GetBytes(s.Code))==hash)return s;
        }
        throw new InvalidDataException("COASTER unknown shader (not executable save data)");
    }
    // A fresh provider factory; callers resolve model/APS/texture assets from a trusted manifest.
    public Action<Model.Mesh,IReadOnlyList<System.Numerics.Vector3>,List<Vector2>> CoasterSquareLatticeProvider(Model model,Aps anim,string owner)
        => SquareLattice(model,anim,anim.Records().First(r=>r.Slot==3),owner);

    public CoasterSceneState CaptureCoasterScene(WorldCoreRegistry r,CoasterSceneBindings b)
    {
        CS(ReferenceEquals(r.Owner,this)&&ReferenceEquals(r.Sim,_sim),"registry");
        CS(_afterCoaster==null,"unclassified active _afterCoaster placement continuation; finish tool before save");
        CS(_coasterTool==null||_coasters.Values.Contains(_coasterTool),"unowned tool");
        var materials=new List<CoasterMaterialState>();var mids=new Dictionary<Material,int>();
        int Mat(Material m) {
            if(m==null)return -1;if(mids.TryGetValue(m,out int id))return id;
            CS(m is ShaderMaterial && m.NextPass==null,"unsupported material root");var sm=(ShaderMaterial)m;
            string hash=CSHash(System.Text.Encoding.UTF8.GetBytes(sm.Shader.Code));CSShader(hash);
            var tex=sm.GetShaderParameter("albedo_tex").AsGodotObject() as Texture2D;
            string tid=tex==null?null:b.IdentifyTexture(tex);CS(tex==null||CSTexHash(tex)==CSTexHash(b.ResolveTexture(tid)),"texture mutation/binding");
            var pars=new Dictionary<string,byte[]>();
            foreach(var p in sm.Shader.GetShaderUniformList()) {
                string key=p.AsGodotDictionary()["name"].AsString();if(key=="albedo_tex")continue;var val=sm.GetShaderParameter(key);
                CS(val.VariantType is Variant.Type.Nil or Variant.Type.Bool or Variant.Type.Float or Variant.Type.Int or Variant.Type.Vector2 or Variant.Type.Vector3,"uniform type");pars.Add(key,CSPack(val));
            }
            id=materials.Count;mids.Add(m,id);materials.Add(new(hash,tid,CSTexHash(tex),m.RenderPriority,pars));return id;
        }
        var nodes=new List<PlacedNodeState>();var ids=new Dictionary<Node3D,int>();var meta=new Dictionary<int,Dictionary<string,byte[]>>();
        var known=new Dictionary<Node3D,(AnimatedModel Model,CoasterAssetOrigin Origin)>();
        var segments=_coasters.Values.SelectMany(v=>v.Segments.Values).ToHashSet();
        foreach(var v in _coasters.Values)foreach(var m in v.PylonModels.Values.Concat(v.Cars.Values.Select(c=>c.Model)).Where(m=>m!=null)) {
            CS(v.Origins.ContainsKey(m),"missing exact asset origin");known.Add(m.Root,(m,v.Origins[m]));
        }
        int Visit(Node3D n) {
            CS(IsInstanceValid(n)&&!n.IsQueuedForDeletion()&&!ids.ContainsKey(n)&&nodes.Count<100000,"node lifetime/ownership/budget");
            int id=nodes.Count;ids.Add(n,id);nodes.Add(null);AnimatedModel.Snapshot renderer=null;int[] children;
            var metadata=new Dictionary<string,byte[]>();
            foreach(var key in n.GetMetaList()) {string k=key.ToString();var val=n.GetMeta(key);
                CS(k=="rest_top"&&val.VariantType==Variant.Type.Float || k=="track_dummy"&&val.VariantType==Variant.Type.Vector3,"unsupported metadata "+k);metadata.Add(k,CSPack(val));}
            if(metadata.Count>0)meta.Add(id,metadata);
            if(known.TryGetValue(n,out var owner)) {
                CS(n.GetChildren().SequenceEqual(owner.Model.Surfaces().Select(s=>(Node)s.Node)),"renderer child order");
                var a=b.IdentifyRenderer(owner.Model,owner.Origin);renderer=owner.Model.CaptureState(a.ModelAssetId,a.AnimationAssetId,a.UvRewriteId);
                CS(renderer.ModelFingerprint==PSHash(a.Model),"renderer binding");children=Array.Empty<int>();
            } else if(n is MeshInstance3D mi && segments.Contains(mi)) {
                CS(mi.GetChildCount()==0&&mi.MaterialOverride==null&&mi.MaterialOverlay==null&&mi.Skeleton.IsEmpty,"segment overrides/children");children=Array.Empty<int>();
            } else {CS(n.GetType()==typeof(Node3D),"unsupported retained node "+n.Name);children=n.GetChildren().Select(c=>{CS(c is Node3D,"nonspatial child");return Visit((Node3D)c);}).ToArray();}
            nodes[id]=new(id,n.Name.ToString().StartsWith("@")?"":n.Name.ToString(),PSPack(n.Transform),n.Visible,n.TopLevel,children,renderer);return id;
        }
        var views=new List<CoasterViewState>();
        foreach(var v in _coasters.Values) {
            CS(v.Id==v.Ride.Id&&ReferenceEquals(v.Ride.Coaster,v.Sim)&&ReferenceEquals(v.Sim.Track,v.Track),"CORE identity");CS(v.PylonModels.Count==v.Pylons.Count&&v.Origins.Count==v.PylonModels.Count+v.Cars.Values.Count(c=>c.Model!=null),"unowned retained renderer");int frame=Visit(v.Frame);
            var ss=v.Segments.Select(p=> {
                var m=p.Value;CS(m.Mesh is ArrayMesh && ((ArrayMesh)m.Mesh).GetBlendShapeCount()==0,"segment mesh type");var surf=new List<CoasterSurfaceState>();
                for(int i=0;i<m.Mesh.GetSurfaceCount();i++) {CS(((ArrayMesh)m.Mesh).SurfaceGetPrimitiveType(i)==Mesh.PrimitiveType.Triangles&&m.GetSurfaceOverrideMaterial(i)==null,"surface primitive/override");surf.Add(new(CSPack(m.Mesh.SurfaceGetArrays(i)),Mat(m.Mesh.SurfaceGetMaterial(i))));}
                return new CoasterSegmentState(r.Id(p.Key),ids[m],surf.ToArray());
            }).ToArray();
            var py=v.Pylons.Select(p=>{CS(v.PylonModels.ContainsKey(p.Key),"discarded pylon renderer");var m=v.PylonModels[p.Key];return new CoasterPylonState(r.Id(p.Key),ids[p.Value],ids[m.Root],v.Origins[m]);}).ToArray();
            var cars=v.Cars.Select(p=>{var c=p.Value;string mesh=c.Mesh==null?null:b.Assets.IdentifyMesh(c.Mesh);CS(mesh==null||PSHash(c.Mesh)==PSHash(b.Assets.ResolveMesh(mesh)),"car mesh binding");return new CoasterCarState(r.Id(p.Key),ids[c.Node],c.Model==null?-1:ids[c.Model.Root],c.Model==null?null:v.Origins[c.Model],mesh,c.Mesh==null?null:PSHash(c.Mesh),PSPack(new(c.WasB,c.Was)),PSPack(new(c.NowB,c.Now)));}).ToArray();
            views.Add(new(v.Id,r.Id(v.Ride),r.Id(v.Sim),r.Id(v.Track),v.Dir,v.Price,frame,ss,py,cars,v.Slots.Select(Mat).ToArray(),v.Red.Select(Mat).ToArray(),v.WinchSeen,v.SeenTime,v.Voices.ToArray(),v.Cells.Select(c=>new[]{c.X,c.Y}).ToArray(),v.Stats));
        }
        return new(1,nodes.ToArray(),meta,materials.ToArray(),views.ToArray(),new(_coasterTool?.Id,(int)_coasterMode,r.Id(_coasterGhost),new[]{_coasterGhostAt.X,_coasterGhostAt.Y},_coasterGhostOk,_coasterFromStation,_coasterWhy,_coasterField.Select(c=>new[]{c.X,c.Z}).ToArray(),_coasterFieldZ,_coasterFieldDone,_coasterStartHeight,_coasterStartBank,_coasterPick,_coasterHold,new(_placedHeight)));
    }
    public sealed class StagedCoasterScene:IDisposable {
        internal CoasterSceneState State;internal StagedPlacedScene Tree;internal Material[] Materials;
        public Node3D TakeFrame(int rideId)=>Tree.TakePlacement(rideId);
        public void Dispose()=>Tree.Dispose();
    }
    public static StagedCoasterScene StageCoasterScene(CoasterSceneState s,CoasterSceneBindings b) {
        CS(s!=null&&s.Version==1&&s.Views!=null&&s.Views.Length<=10000&&s.Materials!=null&&s.Materials.Length<=100000&&s.Metadata!=null&&s.Tool!=null,"envelope");
        var tree=StagePlacedScene(new(1,s.Nodes,s.Views.Select(v=>new PlacedRootState(v.Id,v.Frame)).ToArray(),Array.Empty<PlacedScriptState>(),Array.Empty<PlacedBuildState>(),Array.Empty<PlacedMeshState>(),-1),b.Assets);
        try {
            var mats=s.Materials.Select(m=> {
                var tex=m.Texture==null?null:b.ResolveTexture(m.Texture);CS(tex==null||tex is ImageTexture,"texture class");CS(CSTexHash(tex)==m.TextureHash,"texture fingerprint");
                var sm=Ps2Materials.Animated(tex as ImageTexture,false,"cull_disabled",Vector2.Zero,0,Vector3.Zero);sm.Shader=CSShader(m.Shader);sm.RenderPriority=m.Priority;
                var names=sm.Shader.GetShaderUniformList().Select(p=>p.AsGodotDictionary()["name"].AsString()).Where(n=>n!="albedo_tex").ToHashSet();CS(names.SetEquals(m.Parameters.Keys),"uniform keys");
                foreach(var p in m.Parameters){var val=CSRead(p.Value);CS(val.VariantType is Variant.Type.Nil or Variant.Type.Bool or Variant.Type.Float or Variant.Type.Int or Variant.Type.Vector2 or Variant.Type.Vector3,"uniform type");sm.SetShaderParameter(p.Key,val);}return (Material)sm;
            }).ToArray();
            Material Mat(int i){CS(i>=-1&&i<mats.Length,"material reference");return i==-1?null:mats[i];}
            var replaced=new HashSet<int>();
            foreach(var v in s.Views)foreach(var seg in v.Segments) {
                CS(replaced.Add(seg.Node)&&seg.Surfaces!=null&&seg.Surfaces.Length<=64,"segment references/surface budget");var old=tree.Nodes[seg.Node];CS(old.GetChildCount()==0&&!tree.Models.ContainsKey(seg.Node)&&old.GetParent()==tree.Nodes[v.Frame],"segment ownership");
                CS(seg.Surfaces.Length>0,"empty segment");var mesh=new ArrayMesh();foreach(var surface in seg.Surfaces) {
                    var data=CSRead(surface.Arrays);CS(data.VariantType==Variant.Type.Array,"mesh arrays");var arrays=data.AsGodotArray();CS(arrays.Count==(int)Mesh.ArrayType.Max,"mesh array count");
                    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);mesh.SurfaceSetMaterial(mesh.GetSurfaceCount()-1,Mat(surface.Material));
                }
                var node=new MeshInstance3D{Mesh=mesh,Name=old.Name,Transform=old.Transform,Visible=old.Visible,TopLevel=old.TopLevel};var parent=old.GetParent();int index=old.GetIndex();parent.RemoveChild(old);parent.AddChild(node);parent.MoveChild(node,index);old.Free();tree.Nodes[seg.Node]=node;
            }
            foreach(var p in s.Metadata)foreach(var kv in p.Value) {var val=CSRead(kv.Value);CS(kv.Key=="rest_top"&&val.VariantType==Variant.Type.Float||kv.Key=="track_dummy"&&val.VariantType==Variant.Type.Vector3,"metadata type");tree.Nodes[p.Key].SetMeta(kv.Key,val);}
            return new(){State=s,Tree=tree,Materials=mats};
        }catch {tree.Dispose();throw;}
    }
    public void CompleteCoasterScene(StagedCoasterScene stage,WorldCoreRegistry r,CoasterSceneBindings b) {
        CS(!IsInsideTree()&&GetParent()==null&&ReferenceEquals(r.Owner,this)&&!stage.Tree.Joined&&_coasters.Count==0&&_coasterTool==null&&_afterCoaster==null,"fresh detached target");
        var tree=stage.Tree;var s=stage.State;var views=new List<CoasterView>();
        Material[] Mats(int[] ids){CS(ids!=null&&ids.Length==6,"slots");return ids.Select(i=>{CS(i>=-1&&i<stage.Materials.Length,"slot index");return i==-1?null:stage.Materials[i];}).ToArray();}
        foreach(var saved in s.Views) {
            var ride=r.Resolve<ParkRide>(saved.Ride);var sim=r.Resolve<CoasterSim>(saved.Sim);var track=r.Resolve<CoasterTrack>(saved.Track);
            CS(ride.Id==saved.Id&&ReferenceEquals(ride.Coaster,sim)&&ReferenceEquals(sim.Track,track),"CORE identity join");
            var v=new CoasterView{Id=saved.Id,Ride=ride,Sim=sim,Track=track,Dir=saved.Dir,Price=saved.Price,Frame=tree.Nodes[saved.Frame],Slots=Mats(saved.Slots),Red=Mats(saved.Red),WinchSeen=saved.WinchSeen,SeenTime=saved.SeenTime,Stats=saved.Stats};
            CS(v.Frame.GetParent()==null||v.Frame.GetParent()==this,"frame parent");
            foreach(var seg in saved.Segments){var owner=r.Resolve<CoasterNode>(seg.Owner);track.GetNodeId(owner);v.Segments.Add(owner,(MeshInstance3D)tree.Nodes[seg.Node]);}
            foreach(var p in saved.Pylons) {var n=r.Resolve<CoasterNode>(p.Owner);track.GetNodeId(n);var holder=tree.Nodes[p.Holder];var m=tree.Models[p.Model];CS(holder.GetParent()==v.Frame&&m.Root.GetParent()==holder&&p.Origin!=null,"pylon ownership");v.Pylons.Add(n,holder);v.PylonModels.Add(n,m);v.Origins.Add(m,p.Origin);}
            foreach(var c in saved.Cars) {var car=r.Resolve<CoasterCar>(c.Car);var holder=tree.Nodes[c.Holder];var m=c.Model==-1?null:tree.Models[c.Model];CS(sim.Trains.Any(t=>t.Cars.Contains(car))&&holder.GetParent()==v.Frame&&(m==null||m.Root.GetParent()==holder&&c.Origin!=null),"car ownership");var mesh=c.Mesh==null?null:b.Assets.ResolveMesh(c.Mesh);CS(mesh==null?c.Hash==null:PSHash(mesh)==c.Hash,"mesh fingerprint");var was=PSUnpack(c.Was);var now=PSUnpack(c.Now);v.Cars.Add(car,new(){Node=holder,Model=m,Mesh=mesh,Was=was.Origin,WasB=was.Basis,Now=now.Origin,NowB=now.Basis});if(m!=null)v.Origins.Add(m,c.Origin);}
            foreach(var c in saved.Cells){CS(c.Length==2&&v.Cells.Add((c[0],c[1])),"cell");}foreach(int voice in saved.Voices){CS(sim.Trains.Any(t=>CoasterVoice(v,t)==voice)&&v.Voices.Add(voice),"voice train/tag");}views.Add(v);
        }
        var t=s.Tool;CS(t.Mode is 0 or 1&&t.GhostAt.Length==2&&double.IsFinite(t.Hold),"tool values");
        _sim=r.Sim;_park=r.Park;
        foreach(var v in views){if(v.Frame.GetParent()==null)AddChild(v.Frame);_coasters.Add(v.Id,v);v.ScreamHandler=(tr,evt)=>CoasterScream(v,tr,evt);v.Sim.Scream+=v.ScreamHandler;}
        _coasterTool=t.View is int id?_coasters[id]:null;_coasterMode=(CoasterMode)t.Mode;_coasterGhost=t.Ghost==null?null:r.Resolve<CoasterNode>(t.Ghost);_coasterGhostAt=(t.GhostAt[0],t.GhostAt[1]);_coasterGhostOk=t.GhostOk;_coasterFromStation=t.FromStation;_coasterWhy=t.Why;
        _coasterField.Clear();foreach(var c in t.Field){CS(c.Length==2,"field cell");_coasterField.Add(new(c[0],c[1]));}_coasterFieldZ=t.FieldZ;_coasterFieldDone=t.FieldDone;_coasterStartHeight=t.Height;_coasterStartBank=t.Bank;_coasterPick=t.Pick;_coasterHold=t.Hold;_placedHeight.Clear();foreach(var p in t.PlacedHeight){CS(float.IsFinite(p.Value),"height cache");_placedHeight.Add(p.Key,p.Value);}
        tree.Joined=true;RebindCoasterSceneSounds();
    }
    public void RebindCoasterSceneSounds() {
        ChainCoasterParameters();foreach(var v in _coasters.Values)foreach(var tr in v.Sim.Trains)if(v.Voices.Contains(CoasterVoice(v,tr))&&v.Cars.TryGetValue(tr.Cars[0],out var c)){var node=c.Node;_sounds?.Follow(CoasterVoice(v,tr),RumbleTag,()=>IsInstanceValid(node)?node.GlobalPosition:null);}
    }
}
