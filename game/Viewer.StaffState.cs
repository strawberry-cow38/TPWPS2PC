using Godot;
using System.Text.Json.Serialization;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

public partial class Viewer
{
    public sealed class StaffStateBindings
    {
        public required ParkStaff Staff { get; init; } // rebuilt core, or null for lazy owner
        public required ParkVisitors Visitors { get; init; }
        public required Func<StaffMember,string> MemberId { get; init; }
        public required Func<string,StaffMember> Member { get; init; }
        public required Func<LitterItem,string> LitterId { get; init; }
        public required Func<string,LitterItem> Litter { get; init; }
        public required GuestStateBindings Characters { get; init; }
        public required NativeModelRegistry ModelRegistry { get; init; }
        // The current litter dictionary retains only Node, not AnimatedModel. A caller-owned
        // renderer registry MUST retain that owner at creation; never reconstruct by sampling.
        public Func<Node3D,AnimatedModel> LitterRenderer { get; init; } // capture only
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record StaffRecordState(int Slot,int Variant,int Record);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record StaffActorState(string Member,uint Serial,GuestNodeState Node,string Parent,
        string Model,string Animation,AnimatedModel.Snapshot Drawn,int DrawnRecord,
        NativeGuestAnimation.Snapshot Native,StaffRecordState[] Records,int Showing,float[] Prev,float Yaw);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record StaffLitterState(string Litter,uint Serial,int ModelId,string Model,GuestNodeState Node,
        string Parent,AnimatedModel.Snapshot Drawn);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record StaffPresentationState(int Version,bool Core,bool Registry,bool LogicalTable,
        GuestNodeState Root,string RootParent,int World,int Park,uint Random,string HireHeld,bool HireCarried,
        short[] Cursor,int[] ModelMisses,string[] Models,string[] Animations,string[] Textures,
        GuestRecordState[] Records,StaffActorState[] Actors,StaffLitterState[] Litter)
    {
        public required IntMapLayout ActorsLayout {get;init;}
        public required IntMapLayout LitterLayout {get;init;}
    }
    public sealed record StaffPresentationJoin(IReadOnlyDictionary<string,Node3D> Members,
        IReadOnlyDictionary<string,Node3D> Litter,IReadOnlyDictionary<string,AnimatedModel> LitterRenderers);

    static void SS(bool ok,string why) { if(!ok)throw new InvalidDataException("Viewer staff state: "+why); }
    NativeGuestAnimation.SnapshotBindings SSNative(Aps aps,string id,Dictionary<(int Slot,int Variant),Aps.Record> records,
        NativeLogicalAnimationTable table) => new() {Animation=aps,Table=table,Random=_staffAnimationRand,
        RandomId="viewer/staff-animation",DurationId=id,
        Duration=(s,v)=>records.TryGetValue((s,v),out var r)?r.DurationFrames:null};
    string SSParent(Node n) {
        if(n==null)return null;
        var p=n.GetParent();
        if(p==_staffRoot&&p!=null)return "staff";
        if(p==_guestRoot&&p!=null)return "guest";
        if(p==this)return "viewer";
        throw new InvalidDataException("Viewer staff state: unsupported parent");
    }
    /// <summary>Quiescent actual render join. Security copies/particles fail closed. Core, audio,
    /// management/tool UI, registry assets and future-loading libraries belong to the world owner.</summary>
    public StaffPresentationState CaptureStaffPresentation(StaffStateBindings b)
    {
        ArgumentNullException.ThrowIfNull(b);
        SS(_copyActors.Count==0&&_stinkHolders.Count==0&&!_stinkLibraryFailed&&_stinkLibrary==null&&_stinkWad==null,
            "security copy/particle owners not supported");
        SS(ReferenceEquals(b.Staff,_staff)&&ReferenceEquals(b.Visitors,_staffVisitors)&&ReferenceEquals(b.ModelRegistry,_modelRegistry),"core/registry binding");
        var c=b.Characters;
        SS(ReferenceEquals(c.LogicalTable,_logicalAnimations),"logical table binding");
        string M(string path)=>path==null?null:GSId(c.Models.Single(x=>x.Value.Path.Equals(path,StringComparison.OrdinalIgnoreCase)&&_charModels.TryGetValue(path,out var m)&&ReferenceEquals(m,x.Value.Model)).Key);
        string A(Aps a)=>a==null?null:GSId(c.Animations.Single(x=>ReferenceEquals(x.Value.Animation,a)).Key);
        string Member(StaffMember m) {if(m==null)return null;string id=GSId(b.MemberId(m));SS(ReferenceEquals(b.Member(id),m),"member registry alias");return id;}
        var records=new List<GuestRecordState>();var ids=new Dictionary<Aps.Record,int>(ReferenceEqualityComparer.Instance);
        int R(Aps.Record r,Aps a) {
            if(r==null)return -1;
            string aid=A(a);SS(aid!=null,"record without APS");
            if(ids.TryGetValue(r,out int i)){SS(records[i].Animation==aid,"record asset alias");return i;}
            var raw=a.Records().SingleOrDefault(x=>x.Offset==r.Offset);
            SS(raw!=null&&raw.Slot==r.Slot&&raw.Flags==r.Flags&&raw.DurationFrames==r.DurationFrames&&raw.Tracks==r.Tracks&&raw.TrackCount==r.TrackCount&&raw.Small==r.Small&&raw.SmallCount==r.SmallCount&&raw.Index==r.Index&&raw.IndexCount==r.IndexCount,"mutated record");
            ids.Add(r,records.Count);records.Add(new(aid,r.Offset));return records.Count-1;
        }
        var actors=new List<StaffActorState>();
        foreach(var (member,a) in _staffActors) {
            SS(a!=null&&ReferenceEquals(member,a.Member),"actor member linkage");
            GSNode(a.Node);SS(a.Node!=null&&a.Node.GetChildCount()==(a.Drawn==null?0:1)&& (a.Drawn==null||a.Drawn.Root.GetParent()==a.Node),"actor topology");
            string mid=M(a.ModelPath),aid=A(a.Anim);
            actors.Add(new(Member(member),a.Serial,GSNode(a.Node),SSParent(a.Node),mid,aid,
                a.Drawn?.CaptureState(mid,aid??""),R(a.Drawn?.Record,a.Anim),
                a.Animation?.CaptureState(SSNative(a.Anim,aid,a.Records,_logicalAnimations)),
                a.Records?.Select(x=>new StaffRecordState(x.Key.Slot,x.Key.Variant,R(x.Value,a.Anim))).ToArray(),
                R(a.Showing,a.Anim),GSPack(a.Prev),a.Yaw));
        }
        var litter=new List<StaffLitterState>();
        foreach(var (item,l) in _litterActors) {
            string id=GSId(b.LitterId(item));SS(ReferenceEquals(b.Litter(id),item),"litter registry alias");
            GSNode(l.Node);SS(l.Node!=null,"null litter node");
            var drawn=l.Node.GetChildCount()==0?null:(_litterDrawn.TryGetValue(l.Node,out var owned)?owned:b.LitterRenderer?.Invoke(l.Node));
            SS(l.Node.GetChildCount()==(drawn==null?0:1)&&(drawn==null||drawn.Root.GetParent()==l.Node),"litter renderer owner not registered/topology");
            string mid=M(l.Path);
            litter.Add(new(id,l.Serial,l.ModelId,mid,GSNode(l.Node),SSParent(l.Node),drawn?.CaptureState(mid,"")));
        }
        SS(_staffRoot==null||_staffRoot.GetChildCount()==actors.Count(x=>x.Parent=="staff")+litter.Count(x=>x.Parent=="staff"),"extra staff root children");
        return new(1,_staff!=null,_modelRegistry!=null,_logicalAnimations!=null,GSNode(_staffRoot),SSParent(_staffRoot),
            _staffWorld,_staffPark,_staffAnimationRand.CaptureState(),Member(_hireHeld),_hireCarried,
            _cursorPointOverride is {} p?new[]{p.X,p.Z}:null,_staffModelMisses.ToArray(),
            _charModels.Select(x=>M(x.Key)).ToArray(),
            _charAnims.Select(x=>GSId(c.Animations.Single(a=>a.Value.Path.Equals(x.Key,StringComparison.OrdinalIgnoreCase)&&ReferenceEquals(a.Value.Animation,x.Value)).Key)).ToArray(),
            _charTex.Select(x=>GSId(c.Textures.Single(t=>t.Value.Key==x.Key&&t.Value.Texture==x.Value.Tex&&t.Value.Soft==x.Value.Soft).Key)).ToArray(),
            records.ToArray(),actors.ToArray(),litter.ToArray()) {
                ActorsLayout=_staffActors.CaptureLayout(),LitterLayout=_litterActors.CaptureLayout()};
    }

    /// <summary>Run after RuntimeState and (when used) guest restore. Reuses readonly cache owners;
    /// existing entries must agree by identity. No Create/Hire/Ensure/Play or callback replay.
    /// Does not install core callbacks: world coordinator must bind them once after all joins.</summary>
    public StaffPresentationJoin RestoreStaffPresentation(StaffPresentationState s,StaffStateBindings b)
    {
        ArgumentNullException.ThrowIfNull(s);ArgumentNullException.ThrowIfNull(b);
        SS(!IsInsideTree()&&GetParent()==null&&(_staff==null||ReferenceEquals(_staff,b.Staff))&&(_staffVisitors==null||ReferenceEquals(_staffVisitors,b.Visitors))&&_staffRoot==null&&_staffActors.Count==0&&_litterActors.Count==0&&_staffModelMisses.Count==0&&_copyActors.Count==0&&_stinkHolders.Count==0,"requires fresh detached staff owner");
        SS(s.Version==1&&s.Random==_staffAnimationRand.CaptureState(),"version / restore matching RuntimeState first");
        SS(s.Core==(b.Staff!=null)&&s.Registry==(b.ModelRegistry!=null)&&(!s.Core||b.Visitors!=null),"core binding");
        var c=b.Characters;
        SS(!s.LogicalTable||c.LogicalTable!=null,"logical table binding");
        SS(_logicalAnimations==null||(s.LogicalTable&&ReferenceEquals(_logicalAnimations,c.LogicalTable)),"shared logical table conflict");
        string[] List(string[] a) {SS(a!=null&&a.Length<=100000&&a.Distinct().Count()==a.Length,"asset list");foreach(var id in a)GSId(id);return a;}
        var models=List(s.Models).ToDictionary(id=>id,id=>c.Models[id]);
        var anims=List(s.Animations).ToDictionary(id=>id,id=>c.Animations[id]);
        var textures=List(s.Textures).ToDictionary(id=>id,id=>c.Textures[id]);
        SS(models.Values.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()==models.Count&&anims.Values.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()==anims.Count&&textures.Values.Select(x=>x.Key).Distinct().Count()==textures.Count,"cache path collision");
        foreach(var v in models.Values)SS(v.Model!=null&&(!_charModels.TryGetValue(v.Path,out var had)||ReferenceEquals(had,v.Model)),"model cache conflict");
        foreach(var v in anims.Values)SS(!_charAnims.TryGetValue(v.Path,out var had)||ReferenceEquals(had,v.Animation),"APS cache conflict");
        foreach(var v in textures.Values)SS(!_charTex.TryGetValue(v.Key,out var had)||(had.Tex==v.Texture&&had.Soft==v.Soft),"texture cache conflict");
        SS(s.Actors!=null&&s.Actors.Length<=100000&&s.Litter!=null&&s.Litter.Length<=100000&&s.Records!=null&&s.Records.Length<=1000000&&s.ModelMisses!=null&&s.ModelMisses.Length<=100000,"collection bounds");
        Aps A(string id)=>id==null?null:anims[GSId(id)].Animation;
        var records=s.Records.Select(r=>A(r.Animation)?.Records().SingleOrDefault(x=>x.Offset==r.Offset)??throw new InvalidDataException("staff record asset")).ToArray();
        Aps.Record R(int i,string aid) {SS(i>=-1&&i<records.Length,"record index");if(i<0)return null;SS(s.Records[i].Animation==aid,"record APS mismatch");return records[i];}
        var members=new Dictionary<string,Node3D>();var litterNodes=new Dictionary<string,Node3D>();var litterDrawn=new Dictionary<string,AnimatedModel>();
        var stagedActors=new SnapshotReferenceMap<StaffMember,StaffActor>(ReferenceEqualityComparer.Instance);
        var stagedLitter=new SnapshotReferenceMap<LitterItem,(Node3D Node,uint Serial,int ModelId,string Path)>(ReferenceEqualityComparer.Instance);
        StaffMember Member(string id) {if(id==null)return null;var m=b.Member(GSId(id));SS(m!=null&&b.MemberId(m)==id,"member identity");return m;}
        var held=Member(s.HireHeld);
        SS(s.Core||held==null&&s.Actors.Length==0&&s.Litter.Length==0,"render without core");
        SS(s.Cursor==null||s.Cursor.Length==2,"cursor point");
        var root=GSNode(s.Root);var allocated=new List<Node3D>();if(root!=null)allocated.Add(root);
        var attach=new List<(Node3D Node,string Parent)>();
        var rendererRecords=new HashSet<int>();
        Node Parent(string id)=>id switch {"viewer"=>this,"guest"=>_guestRoot??throw new InvalidDataException("staff guest parent absent"),"staff"=>root??throw new InvalidDataException("staff root absent"),_=>throw new InvalidDataException("staff parent ID")};
        AnimatedModel Draw(AnimatedModel.Snapshot snap,string mid,string aid) {
            SS(mid!=null&&snap.ModelAssetId==mid&&snap.AnimationAssetId==(aid??""),"drawn asset mismatch");
            return AnimatedModel.FromState(snap,new(){ModelAssetId=mid,AnimationAssetId=aid??"",Model=models[mid].Model,Animation=A(aid),Texture=mat=>c.Texture(mid,mat),ShareReadOnlyModel=true});
        }
        try {
            if(root!=null){SS(s.RootParent!="staff","root cycle");Parent(s.RootParent);attach.Add((root,s.RootParent));}else SS(s.RootParent==null,"null root parent");
            foreach(var e in s.Actors) {
                var m=Member(e.Member);SS(m!=null&&float.IsFinite(e.Yaw),"member/yaw");
                var node=GSNode(e.Node);SS(node!=null,"actor node");allocated.Add(node);Parent(e.Parent);attach.Add((node,e.Parent));
                var a=new StaffActor{Member=m,Serial=e.Serial,Node=node,Anim=A(e.Animation),ModelPath=e.Model==null?null:models[e.Model].Path,Prev=GSVector(e.Prev),Yaw=e.Yaw};
                if(e.Drawn!=null) {
                    SS(e.Drawn.RecordOffset==(R(e.DrawnRecord,e.Animation)?.Offset??-1),"drawn record mismatch");
                    a.Drawn=Draw(e.Drawn,e.Model,e.Animation);node.AddChild(a.Drawn.Root);
                    if(e.DrawnRecord>=0){SS(rendererRecords.Add(e.DrawnRecord),"cross-renderer record alias unsupported");records[e.DrawnRecord]=a.Drawn.Record;}
                }else SS(e.DrawnRecord==-1,"record without renderer");
                stagedActors.Add(m,a);members.Add(e.Member,node);
            }
            foreach(var e in s.Actors) {
                var a=stagedActors[Member(e.Member)];
                SS(e.Records==null||e.Records.Length<=65536,"actor record count");
                a.Records=e.Records?.ToDictionary(r=>(r.Slot,r.Variant),r=>R(r.Record,e.Animation));
                SS(a.Records==null||a.Records.Values.All(r=>r!=null),"null duration record");
                a.Showing=R(e.Showing,e.Animation);
                if(e.Native!=null){SS(s.LogicalTable&&a.Records!=null&&a.Anim!=null,"native dependencies");a.Animation=NativeGuestAnimation.FromState(e.Native,SSNative(a.Anim,e.Animation,a.Records,c.LogicalTable));}
            }
            foreach(var e in s.Litter) {
                var item=b.Litter(GSId(e.Litter));SS(item!=null&&b.LitterId(item)==e.Litter,"litter identity");
                var node=GSNode(e.Node);SS(node!=null,"litter node");allocated.Add(node);Parent(e.Parent);attach.Add((node,e.Parent));
                if(e.Drawn!=null){var d=Draw(e.Drawn,e.Model,null);node.AddChild(d.Root);litterDrawn.Add(e.Litter,d);}
                stagedLitter.Add(item,(node,e.Serial,e.ModelId,e.Model==null?null:models[e.Model].Path));litterNodes.Add(e.Litter,node);
            }
        }catch {foreach(var n in allocated)if(IsInstanceValid(n)&&n.GetParent()==null)n.Free();throw;}
        // Dictionary holes matter: future rehire fills a free slot and TickStaffAnimations
        // consumes its shared RNG in enumeration order. Use unused existing pool members as
        // construction placeholders, never invoke a staff constructor/Hire to build a hole.
        var spareMembers=b.Staff==null?Array.Empty<StaffMember>():StaffTables.PoolBuildOrder.SelectMany(k=>Enumerable.Range(0,StaffTables.PoolSize).Select(i=>b.Staff.StateMember(k,i))).Where(m=>!stagedActors.ContainsKey(m)).ToArray();
        var spareLitter=b.Staff==null?Array.Empty<LitterItem>():b.Staff.Litter.Slots.Where(l=>!stagedLitter.ContainsKey(l)).ToArray();
        int mh=0,lh=0;
        StaffMember MemberHole()=>mh<spareMembers.Length?spareMembers[mh++]:throw new InvalidDataException("staff actor holes exceed pool");
        LitterItem LitterHole()=>lh<spareLitter.Length?spareLitter[lh++]:throw new InvalidDataException("litter actor holes exceed pool");
        try {stagedActors.RestoreLayout(s.ActorsLayout,MemberHole);stagedLitter.RestoreLayout(s.LitterLayout,LitterHole);}
        catch {foreach(var n in allocated)if(IsInstanceValid(n)&&n.GetParent()==null)n.Free();throw;}
        mh=lh=0;
        // Validation/allocation complete. Readonly collection instances are never replaced.
        foreach(var a in attach)Parent(a.Parent).AddChild(a.Node);
        foreach(var v in models.Values)_charModels[v.Path]=v.Model;
        foreach(var v in anims.Values)_charAnims[v.Path]=v.Animation;
        foreach(var v in textures.Values)_charTex[v.Key]=(v.Texture,v.Soft);
        foreach(var a in stagedActors)_staffActors.Add(a.Key,a.Value);
        foreach(var l in stagedLitter)_litterActors.Add(l.Key,l.Value);
        _staffActors.RestoreLayout(s.ActorsLayout,MemberHole);_litterActors.RestoreLayout(s.LitterLayout,LitterHole);
        foreach(var pair in litterDrawn)_litterDrawn.Add(litterNodes[pair.Key],pair.Value);
        _staffModelMisses.UnionWith(s.ModelMisses);_staffRoot=root;_staff=b.Staff;_staffVisitors=b.Visitors;
        _modelRegistry=b.ModelRegistry;_staffWorld=s.World;_staffPark=s.Park;
        _logicalAnimations=s.LogicalTable?c.LogicalTable:null;_hireHeld=held;_hireCarried=s.HireCarried;
        _cursorPointOverride=s.Cursor==null?null:new TPW.PS2.Data.NativeGuestMotion.Point(s.Cursor[0],s.Cursor[1]);
        return new(members,litterNodes,litterDrawn);
    }
}
