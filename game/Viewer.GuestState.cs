using Godot;
using System.Text.Json.Serialization;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer;

public partial class Viewer
{
    // Actual Viewer guest adapter. Not the GuestWalk/Visitors owner, nor a whole-world loader.
    // Integer-keyed legacy caches must be assigned to their CURRENT/retained Guest by the
    // caller registry. We never search GuestWalk by display ID (IDs can be reused).
    public sealed record GuestModelAsset(string Path, Model Model);
    public sealed record GuestAnimationAsset(string Path, Aps Animation);
    public sealed record GuestTextureAsset(string Key, ImageTexture Texture, bool Soft);
    public sealed class GuestStateBindings
    {
        public required Func<int, Guest> GuestAtSlot { get; init; } // capture only, including retained cache owners
        public required Func<Guest, string> GuestId { get; init; }
        public required Func<string, Guest> Guest { get; init; }
        // Trusted registry: IDs -> preloaded immutable assets AND trusted cache paths/keys.
        // A null Animation is an explicitly retained failed/missing cache entry.
        public required IReadOnlyDictionary<string, GuestModelAsset> Models { get; init; }
        public required IReadOnlyDictionary<string, GuestAnimationAsset> Animations { get; init; }
        public required IReadOnlyDictionary<string, GuestTextureAsset> Textures { get; init; }
        public required Func<string, string, (ImageTexture Tex, bool Soft)> Texture { get; init; } // model asset ID, material
        public required NativeLogicalAnimationTable LogicalTable { get; init; } // null iff not loaded
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GuestNodeState(string Name, float[] Transform, bool Visible, bool TopLevel, int Sibling = -1);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GuestRecordState(string Animation, int Offset);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GuestWalkRecordState(string Model, string Animation, int Walk, int Idle);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GuestPartsState(float[] Feet, float[] HeadAt, int[] BodyChildren, string Head);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GuestSeatState(float[] At, string Where, float[] Forward, string Part, float PartTop);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GuestWalkingState(float[] At, string Where);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GuestPresentationEntry
    {
        public required string Guest { get; init; }
        public bool ActorEntry { get; init; } // present null is the no-retry failure latch
        public GuestNodeState Actor { get; init; }
        public AnimatedModel.Snapshot Drawn { get; init; }
        public int DrawnRecord { get; init; } = -1;
        public int Sit { get; init; } = -1;
        public string Where { get; init; }
        public GuestWalkRecordState Walk { get; init; }
        public int[] Idles { get; init; }
        public int? IdleSince { get; init; }
        public int? GaitRecord { get; init; } // null absent, -1 present null
        public int? GaitFrom { get; init; }
        public int? NativeDrawn { get; init; }
        public GuestPartsState Parts { get; init; }
        public bool HeadOnly { get; init; }
        public bool Posed { get; init; }
        public float[] Previous { get; init; }
        public int? Dwell { get; init; }
        public GuestSeatState Seated { get; init; }
        public GuestWalkingState Walking { get; init; }
        public float[] Standing { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GuestNativeState(string Guest, string Animation, NativeGuestAnimation.Snapshot State);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record GuestPresentationState
    {
        public required int Version { get; init; }
        public required GuestNodeState Root { get; init; }
        public required string[] Models { get; init; }
        public required string[] Animations { get; init; }
        public required string[] Textures { get; init; }
        public required GuestRecordState[] Records { get; init; }
        public required GuestPresentationEntry[] Entries { get; init; }
        public required GuestNativeState[] Native { get; init; }
        public required bool LogicalTableLoaded { get; init; }
        public required uint Random { get; init; }
        public required bool NativeEnabled { get; init; }
        public required bool NativeIdleAll { get; init; }
        public required int NativeWaits { get; init; }
        public required int[][] Mouth { get; init; }
        public required int[][] Pool { get; init; }
        public required int PoolLaid { get; init; }
        public required long PoolAt { get; init; }
        public required bool IdleScene { get; init; }
        public required bool IdleSeeded { get; init; }
        public required int IdleCount { get; init; }
    }
    static void GSRequire(bool ok, string why) { if (!ok) throw new InvalidDataException("Viewer guest state: " + why); }
    static string GSId(string id) { GSRequire(!string.IsNullOrWhiteSpace(id) && id.Length <= 1024,"registry ID"); return id; }
    static float[] GSPack(Vector3 v) => new[]{v.X,v.Y,v.Z};
    static float[] GSPack(Transform3D t) => new[]{t.Basis.X.X,t.Basis.X.Y,t.Basis.X.Z,t.Basis.Y.X,t.Basis.Y.Y,t.Basis.Y.Z,
        t.Basis.Z.X,t.Basis.Z.Y,t.Basis.Z.Z,t.Origin.X,t.Origin.Y,t.Origin.Z};
    static void GSFloats(float[] v, int n) => GSRequire(v!=null && v.Length==n && v.All(float.IsFinite),"finite vector/transform");
    static Vector3 GSVector(float[] v) { GSFloats(v,3); return new(v[0],v[1],v[2]); }
    static Transform3D GSTransform(float[] v) { GSFloats(v,12); return new(new Basis(new(v[0],v[1],v[2]),new(v[3],v[4],v[5]),new(v[6],v[7],v[8])),new(v[9],v[10],v[11])); }
    static GuestNodeState GSNode(Node3D n) { if(n==null)return null; GSRequire(IsInstanceValid(n)&&!n.IsQueuedForDeletion(),"stale/queued node: capture after cleanup or retain owner explicitly"); return new(n.Name,GSPack(n.Transform),n.Visible,n.TopLevel,n.GetParent()==null||n.GetParent() is Viewer?-1:n.GetIndex()); }
    static Node3D GSNode(GuestNodeState s) => s==null?null:new Node3D {Name=s.Name,Transform=GSTransform(s.Transform),Visible=s.Visible,TopLevel=s.TopLevel};
    static int[][] GSCells(List<ParkCell> c)=>c?.Select(x=>new[]{x.X,x.Z}).ToArray();
    static List<ParkCell> GSCells(int[][] c) { if(c==null)return null; GSRequire(c.Length<=1000000&&c.All(x=>x!=null&&x.Length==2),"cells"); return c.Select(x=>new ParkCell(x[0],x[1])).ToList(); }
    static NativeGuestAnimation.SnapshotBindings GSNative(Aps a, string id, NativeLogicalAnimationTable table, NewlibRand rng)
    {
        GSRequire(a!=null&&table!=null,"native animation requires cached own APS and logical table");
        var lengths=a.Records().GroupBy(r=>r.Slot).SelectMany(g=>g.Select((r,i)=>(g.Key,i,r.DurationFrames))).ToDictionary(x=>(x.Key,x.i),x=>x.DurationFrames);
        return new(){Animation=a,Table=table,Random=rng,RandomId="viewer/native-animation",DurationId=id,
            Duration=(s,v)=>lengths.TryGetValue((s,v),out int n)?n:null};
    }

    /// <summary>No asset loading, OwnAnimation, RNG, sampling, or world mutation. Registries must
    /// already know cache paths and even retired Guest objects. Throws rather than silently drop
    /// unsupported topology/stale freed renderers. Run at a quiescent presentation boundary.</summary>
    public GuestPresentationState CaptureGuestPresentation(GuestStateBindings b)
    {
        ArgumentNullException.ThrowIfNull(b);
        // These roots belong to other coordinators. Do not claim that diagnostics-driven film /
        // scripted test fixtures or standing-service placement providers are part of this owner.
        GSRequire(!_guestTest && _walkFilm==0 && _rideFilm==0 && _guestAt==null && _guestTestRide==null,
            "active guest/film test harness unsupported");
        string ModelId(string path,Model m)=>GSId(b.Models.Single(x=>x.Value.Path.Equals(path,StringComparison.OrdinalIgnoreCase)&&ReferenceEquals(x.Value.Model,m)).Key);
        string AnimId(string path,Aps a)=>GSId(b.Animations.Single(x=>x.Value.Path.Equals(path,StringComparison.OrdinalIgnoreCase)&&ReferenceEquals(x.Value.Animation,a)).Key);
        string M(Model m)=>ModelId(_charModels.First(x=>ReferenceEquals(x.Value,m)).Key,m);
        string A(Aps a)=>a==null?null:AnimId(_charAnims.First(x=>ReferenceEquals(x.Value,a)).Key,a);
        var records=new List<GuestRecordState>();var recordIds=new Dictionary<Aps.Record,int>(ReferenceEqualityComparer.Instance);
        int R(Aps.Record r,Aps a) {
            if(r==null)return -1;
            GSRequire(a!=null,"record without animation");
            if(recordIds.TryGetValue(r,out int index)) { GSRequire(records[index].Animation==A(a),"record asset alias"); return index; }
            var raw=a.Records().SingleOrDefault(x=>x.Offset==r.Offset);
            GSRequire(raw!=null&&raw.Slot==r.Slot&&raw.Flags==r.Flags&&raw.DurationFrames==r.DurationFrames&&raw.Tracks==r.Tracks
                &&raw.TrackCount==r.TrackCount&&raw.Small==r.Small&&raw.SmallCount==r.SmallCount&&raw.Index==r.Index&&raw.IndexCount==r.IndexCount,"mutated/nonasset record");
            recordIds.Add(r,records.Count);records.Add(new(A(a),r.Offset));return records.Count-1;
        }
        var slots=new SortedSet<int>();
        void Keys<T>(Dictionary<int,T> d)=>slots.UnionWith(d.Keys);
        Keys(_actors);Keys(_drawn);Keys(_walkRec);Keys(_idles);Keys(_idleSince);Keys(_gaitRec);Keys(_gaitFrom);Keys(_nativeDrawn);
        Keys(_parts);slots.UnionWith(_headOnly);slots.UnionWith(_posed);Keys(_guestPrev);Keys(_guestDwell);Keys(_seated);Keys(_walking);Keys(_standing);
        var entries=new List<GuestPresentationEntry>();var seen=new HashSet<string>();
        foreach(int slot in slots) {
            var g=b.GuestAtSlot(slot);GSRequire(g!=null&&g.Id==slot,"unresolved legacy cache slot");string id=GSId(b.GuestId(g));
            GSRequire(seen.Add(id)&&ReferenceEquals(b.Guest(id),g),"guest registry alias");
            bool hasActor=_actors.TryGetValue(slot,out var actor);_drawn.TryGetValue(slot,out var drawn);_walkRec.TryGetValue(slot,out var walk);
            GSRequire(drawn.Drawn==null || (_walkRec.ContainsKey(slot)&&actor!=null&&IsInstanceValid(actor)
                &&drawn.Drawn.Root.GetParent()==actor&&actor.GetChildCount()==1),"orphan/stale drawn or unsupported actor topology");
            GSRequire(actor==null || (IsInstanceValid(actor)&&actor.GetParent()==_guestRoot&&drawn.Drawn!=null),"actor topology");
            var render=drawn.Drawn?.CaptureState(M(walk.Model),A(walk.Anim)??"");
            GuestPartsState parts=null;
            if(_parts.TryGetValue(slot,out var p)) {
                GSRequire(drawn.Drawn!=null,"parts without drawn");
                GSRequire(p.Body.All(n=>IsInstanceValid(n)&&n.GetParent()==drawn.Drawn.Root),"body surface outside renderer");
                parts=new(GSPack(p.Feet),GSPack(p.HeadAt),p.Body.Select(n=>n.GetIndex()).ToArray(),p.Head);
            }
            entries.Add(new(){Guest=id,ActorEntry=hasActor,Actor=GSNode(actor),Drawn=render,
                DrawnRecord=R(drawn.Drawn?.Record,walk.Anim),Sit=R(drawn.Sit,walk.Anim),Where=drawn.Where,
                Walk=_walkRec.ContainsKey(slot)?new(M(walk.Model),A(walk.Anim),R(walk.Walk,walk.Anim),R(walk.Idle,walk.Anim)):null,
                Idles=_idles.TryGetValue(slot,out var ii)?ii.Select(r=>R(r,walk.Anim)).ToArray():null,
                IdleSince=_idleSince.TryGetValue(slot,out int si)?si:null,GaitFrom=_gaitFrom.TryGetValue(slot,out int gf)?gf:null,
                GaitRecord=_gaitRec.TryGetValue(slot,out var gr)?R(gr,walk.Anim):null,
                NativeDrawn=_nativeDrawn.TryGetValue(slot,out var nr)?R(nr,walk.Anim):null,Parts=parts,
                HeadOnly=_headOnly.Contains(slot),Posed=_posed.Contains(slot),Previous=_guestPrev.TryGetValue(slot,out var pv)?GSPack(pv):null,
                Dwell=_guestDwell.TryGetValue(slot,out int dw)?dw:null,
                Seated=_seated.TryGetValue(slot,out var seat)?new(GSPack(seat.At),seat.Where,GSPack(seat.Forward),seat.Part,seat.PartTop):null,
                Walking=_walking.TryGetValue(slot,out var walking)?new(GSPack(walking.At),walking.Where):null,
                Standing=_standing.TryGetValue(slot,out var standing)?GSPack(standing):null});
        }
        // Staff/litter are captured by StaffPresentation, not silently swallowed into guest state.
        var guestChildren=new HashSet<Node>(_actors.Values.Where(x=>x!=null));
        if(_staffRoot!=null&&_staffRoot.GetParent()==_guestRoot)guestChildren.Add(_staffRoot);
        foreach(var a in _staffActors.Values)if(a.Node!=null&&a.Node.GetParent()==_guestRoot)guestChildren.Add(a.Node);
        foreach(var l in _litterActors.Values)if(l.Node!=null&&l.Node.GetParent()==_guestRoot)guestChildren.Add(l.Node);
        GSRequire(_guestRoot==null ? _actors.Values.All(x=>x==null) :
            _guestRoot.GetChildren().All(guestChildren.Contains)&&_guestRoot.GetChildCount()==guestChildren.Count,"extra guest root children");
        var natives=new List<GuestNativeState>();
        foreach(var (g,n) in _nativeAnimations) {
            string id=GSId(b.GuestId(g));GSRequire(ReferenceEquals(b.Guest(id),g),"native guest registry alias");
            // Existing implementation constructs duration from this own cache path. Never load it here.
            GSRequire(g.Id>0,"native guest model slot");
            string path=System.IO.Path.ChangeExtension(GuestModels[(g.Id-1)%GuestModels.Length],".aps");
            GSRequire(_charAnims.TryGetValue(path,out var own)&&own!=null,"native own APS absent: unsupported empty-duration provider");
            string aid=AnimId(path,own);natives.Add(new(id,aid,n.CaptureState(GSNative(own,aid,_logicalAnimations,_nativeAnimationRand))));
        }
        return new(){Version=1,Root=GSNode(_guestRoot),Models=_charModels.Select(x=>ModelId(x.Key,x.Value)).ToArray(),
            Animations=_charAnims.Select(x=>AnimId(x.Key,x.Value)).ToArray(),
            Textures=_charTex.Select(x=>GSId(b.Textures.Single(t=>t.Value.Key==x.Key&&t.Value.Texture==x.Value.Tex&&t.Value.Soft==x.Value.Soft).Key)).ToArray(),
            Records=records.ToArray(),Entries=entries.ToArray(),Native=natives.ToArray(),LogicalTableLoaded=_logicalAnimations!=null,
            Random=_nativeAnimationRand.CaptureState(),NativeEnabled=_nativeGuestAnimation,NativeIdleAll=_nativeIdleAll,NativeWaits=NativeAnimationWaits,
            Mouth=GSCells(_mouth),Pool=GSCells(_guestPool),PoolLaid=_guestPoolLaid,PoolAt=_guestPoolAt,
            IdleScene=_idleScene,IdleSeeded=_idleSeeded,IdleCount=_idleCount};
    }

    /// <summary>Join into a fresh, unpublished Viewer AFTER RestoreRuntimeState (shared RNG).
    /// All graph validation/render allocation occurs before touching its dictionaries. The returned
    /// registry maps saved Guest object IDs to fresh actor nodes, including explicit null failures.
    /// Root becomes a child of this detached Viewer; publication/Ready suppression is root-owned.
    /// GuestWalk/Visitors, standing-place ride/node providers, thoughts, scene/camera/tools and
    /// character-library future loading are external roots. No gameplay API or frame replay runs.</summary>
    public IReadOnlyDictionary<string, Node3D> RestoreGuestPresentation(GuestPresentationState s, GuestStateBindings b)
    {
        ArgumentNullException.ThrowIfNull(s);ArgumentNullException.ThrowIfNull(b);
        GSRequire(!IsInsideTree() && GetParent()==null,"requires detached unpublished Viewer");
        GSRequire(_guestRoot==null&&_actors.Count==0&&_drawn.Count==0&&_walkRec.Count==0&&_idles.Count==0&&_idleSince.Count==0
            &&_gaitRec.Count==0&&_gaitFrom.Count==0&&_nativeDrawn.Count==0&&_nativeAnimations.Count==0&&_parts.Count==0
            &&_headOnly.Count==0&&_posed.Count==0&&_guestPrev.Count==0&&_guestDwell.Count==0&&_seated.Count==0&&_walking.Count==0
            &&_standing.Count==0&&_charModels.Count==0&&_charAnims.Count==0&&_charTex.Count==0,"guest dictionaries not empty");
        GSRequire(s.Version==1&&s.Entries!=null&&s.Entries.Length<=100000&&s.Native!=null&&s.Native.Length<=100000
            &&s.Records!=null&&s.Records.Length<=1000000,"version/collection bounds");
        GSRequire(s.Random==_nativeAnimationRand.CaptureState()&&s.NativeEnabled==_nativeGuestAnimation
            &&s.NativeIdleAll==_nativeIdleAll&&s.NativeWaits==NativeAnimationWaits,"restore matching RuntimeState first (one shared RNG)");
        GSRequire(!s.LogicalTableLoaded||b.LogicalTable!=null,"logical table binding");
        string[] Ids(string[] ids) { GSRequire(ids!=null&&ids.Length<=100000&&ids.Distinct().Count()==ids.Length,"asset list");foreach(var id in ids)GSId(id);return ids; }
        var models=Ids(s.Models).ToDictionary(id=>id,id=>b.Models[id]);
        var anims=Ids(s.Animations).ToDictionary(id=>id,id=>b.Animations[id]);
        var textures=Ids(s.Textures).ToDictionary(id=>id,id=>b.Textures[id]);
        GSRequire(models.Values.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()==models.Count
            &&anims.Values.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()==anims.Count
            &&textures.Values.Select(x=>x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()==textures.Count,"cache path collision");
        Model M(string id)=>models[GSId(id)].Model??throw new InvalidDataException("null model");
        Aps A(string id)=>id==null?null:anims[GSId(id)].Animation;
        var records=new Aps.Record[s.Records.Length];
        for(int i=0;i<records.Length;i++) {
            var r=s.Records[i];GSRequire(r!=null&&A(r.Animation)!=null,"record asset");
            records[i]=A(r.Animation).Records().SingleOrDefault(x=>x.Offset==r.Offset);
            GSRequire(records[i]!=null,"record offset not in trusted asset");
        }
        Aps.Record R(int id) { GSRequire(id>=-1&&id<records.Length,"record reference");return id<0?null:records[id]; }
        void RecordAsset(int id,string aid) { R(id);if(id>=0)GSRequire(s.Records[id].Animation==aid,"record belongs to wrong APS"); }
        var mouth=GSCells(s.Mouth);var pool=GSCells(s.Pool);
        var publish=new List<Action>();var registry=new Dictionary<string,Node3D>();var guests=new HashSet<Guest>(ReferenceEqualityComparer.Instance);var slots=new HashSet<int>();
        var nativeGuests=new HashSet<Guest>(ReferenceEqualityComparer.Instance);
        var root=GSNode(s.Root);
        var rendererRecords=new HashSet<int>();
        try {
            // Renderers first: their restored Record objects become canonical entries in the
            // identity table, so gait/idle caches preserve exact reference-equality relationships.
            var renderers=new Dictionary<string,AnimatedModel>();
            foreach(var e in s.Entries) {
                GSRequire(e!=null,"null entry");GSId(e.Guest);
                var g=b.Guest(e.Guest);GSRequire(g!=null&&b.GuestId(g)==e.Guest&&guests.Add(g)&&slots.Add(g.Id),"guest/slot collision");
                GSRequire(e.ActorEntry||e.Actor==null,"actor without entry");
                var actor=GSNode(e.Actor);
                if(actor!=null) {
                    if(root==null) { actor.Free();throw new InvalidDataException("actor without guest root"); }
                    root.AddChild(actor);
                }
                if(e.ActorEntry)registry.Add(e.Guest,actor);
                if(e.Drawn!=null) {
                    GSRequire(actor!=null&&e.Walk!=null,"drawn without actor/assets");
                    GSRequire(e.Drawn.ModelAssetId==e.Walk.Model&&e.Drawn.AnimationAssetId==(e.Walk.Animation??""),"renderer/walk asset disagreement");
                    RecordAsset(e.DrawnRecord,e.Walk.Animation);
                    GSRequire(e.Drawn.RecordOffset==(R(e.DrawnRecord)?.Offset??-1),"drawn record disagreement");
                    var drawn=AnimatedModel.FromState(e.Drawn,new(){ModelAssetId=e.Drawn.ModelAssetId,AnimationAssetId=e.Drawn.AnimationAssetId,
                        Model=M(e.Walk.Model),Animation=A(e.Walk.Animation),Texture=material=>b.Texture(e.Walk.Model,material),ShareReadOnlyModel=true});
                    actor.AddChild(drawn.Root);renderers.Add(e.Guest,drawn);
                    if(e.DrawnRecord>=0) {
                        // Normal MakeActor creates per-actor record objects. A cross-actor shared
                        // record identity cannot be injected through generic FromState: fail closed.
                        GSRequire(rendererRecords.Add(e.DrawnRecord),"cross-renderer Record object alias unsupported");
                        records[e.DrawnRecord]=drawn.Record;
                    }
                } else GSRequire(actor==null&&e.Parts==null&&e.DrawnRecord==-1&&e.Sit==-1,"actor/parts without renderer");
            }
            foreach(var e in s.Entries) {
                int slot=b.Guest(e.Guest).Id;
                renderers.TryGetValue(e.Guest,out var drawn);
                string aid=e.Walk?.Animation;
                if(e.ActorEntry)publish.Add(()=>_actors.Add(slot,registry[e.Guest]));
                if(drawn!=null) {RecordAsset(e.Sit,aid);var sit=R(e.Sit);publish.Add(()=>_drawn.Add(slot,(drawn,sit,e.Where)));}
                if(e.Walk is {} w) {
                    RecordAsset(w.Walk,aid);RecordAsset(w.Idle,aid);var walk=R(w.Walk);var idle=R(w.Idle);var model=M(w.Model);var anim=A(aid);
                    publish.Add(()=>_walkRec.Add(slot,(anim,walk,idle,model)));
                }
                if(e.Idles!=null) {GSRequire(e.Idles.Length<=65536,"idle count");foreach(int id in e.Idles)RecordAsset(id,aid);var ids=e.Idles.Select(R).ToArray();publish.Add(()=>_idles.Add(slot,ids));}
                if(e.IdleSince is int si)publish.Add(()=>_idleSince.Add(slot,si));
                if(e.GaitFrom is int gf)publish.Add(()=>_gaitFrom.Add(slot,gf));
                if(e.GaitRecord is int gr) {RecordAsset(gr,aid);var rec=R(gr);publish.Add(()=>_gaitRec.Add(slot,rec));}
                if(e.NativeDrawn is int nr) {RecordAsset(nr,aid);var rec=R(nr);publish.Add(()=>_nativeDrawn.Add(slot,rec));}
                if(e.Parts is {} p) {
                    var feet=GSVector(p.Feet);var head=GSVector(p.HeadAt);
                    GSRequire(p.BodyChildren!=null&&p.BodyChildren.Length<=65536&&p.BodyChildren.Distinct().Count()==p.BodyChildren.Length,"body refs");
                    var body=new List<MeshInstance3D>();
                    foreach(int i in p.BodyChildren) {
                        GSRequire(i>=0&&i<drawn.Root.GetChildCount()&&drawn.Root.GetChild(i) is MeshInstance3D,"body child ref");
                        body.Add((MeshInstance3D)drawn.Root.GetChild(i));
                    }
                    publish.Add(()=>_parts.Add(slot,(feet,head,body,p.Head)));
                }
                if(e.HeadOnly)publish.Add(()=>_headOnly.Add(slot));if(e.Posed)publish.Add(()=>_posed.Add(slot));
                if(e.Previous!=null) {var prev=GSVector(e.Previous);publish.Add(()=>_guestPrev.Add(slot,prev));}
                if(e.Dwell is int dw)publish.Add(()=>_guestDwell.Add(slot,dw));
                if(e.Seated is {} seat) {
                    var at=GSTransform(seat.At);var forward=GSVector(seat.Forward);GSRequire(float.IsFinite(seat.PartTop),"seat top");
                    publish.Add(()=>_seated.Add(slot,(at,seat.Where,forward,seat.Part,seat.PartTop)));
                }
                if(e.Walking is {} walking) {var at=GSTransform(walking.At);publish.Add(()=>_walking.Add(slot,(at,walking.Where)));}
                if(e.Standing!=null) {var at=GSTransform(e.Standing);publish.Add(()=>_standing.Add(slot,at));}
            }
            foreach(var n in s.Native) {
                GSRequire(n!=null&&s.LogicalTableLoaded,"native without table");GSId(n.Guest);
                var g=b.Guest(n.Guest);GSRequire(g!=null&&b.GuestId(g)==n.Guest&&nativeGuests.Add(g),"native guest identity");
                var animation=NativeGuestAnimation.FromState(n.State,GSNative(A(n.Animation),n.Animation,b.LogicalTable,_nativeAnimationRand));
                publish.Add(()=>_nativeAnimations.Add(g,animation));
            }
            // From here on, no resolver/asset/validation calls. Only fresh in-memory dictionary join.
            foreach(var x in models.Values)_charModels.Add(x.Path,x.Model);
            foreach(var x in anims.Values)_charAnims.Add(x.Path,x.Animation);
            foreach(var x in textures.Values)_charTex.Add(x.Key,(x.Texture,x.Soft));
            foreach(var action in publish)action();
            _logicalAnimations=s.LogicalTableLoaded?b.LogicalTable:null;
            _mouth=mouth;_guestPool=pool;_guestPoolLaid=s.PoolLaid;_guestPoolAt=s.PoolAt;
            _idleScene=s.IdleScene;_idleSeeded=s.IdleSeeded;_idleCount=s.IdleCount;
            _guestRoot=root;if(root!=null)AddChild(root);
            return registry;
        } catch { if(root!=null&&IsInstanceValid(root))root.Free();throw; }
    }
}
