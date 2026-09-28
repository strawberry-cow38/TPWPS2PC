#nullable enable
using System.Text.Json.Serialization;
namespace TPW.PS2.Data;

public sealed partial class ParkStaff
{
    int DefaultRandom(int n)=>n<=0?0:_ownedRandom.Next(n);
    IEnumerable<ParkRide> DefaultPlacedRides()=>Visitors.Sim.Rides;
    IEnumerable<StaffFeature> DefaultFeatures()=>Visitors.Sim.Rides.Select(StaffFeature.Of).Where(f=>f!=null);
    bool DefaultPaused(Guest g)=>Visitors.Staff?.IsWatching(g.Id)==true;
    int DefaultMechanicCount()=>Count(StaffKind.Mechanic);
    void DefaultResearchAdvisor(int id)=>Advisor?.Invoke(id);
    public Func<Guest,bool> StatePaused=>DefaultPaused;
    void RequireSnapshotReady(){if(!_snapshotReady)throw new InvalidOperationException("Staff snapshot shell is not hydrated.");}
    public StaffMember StateMember(StaffKind kind,int slot)=>_slots[kind][slot];

    public sealed class StateBindings
    {
        public required GuestWalk.GuestGraph GuestGraph {get;init;}
        public required string PathsKey {get;init;}
        public required string ClockKey {get;init;}
        public required string ActivationsKey {get;init;}
        public Func<object,string>? IdentifyReference {get;init;}
        public Func<string,object>? ResolveReference {get;init;}
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PoolState
    {
        public required StaffKind Kind {get;init;}
        public required int[] Free {get;init;}
        public required int[] Active {get;init;}
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RoomState
    {
        public required string Key {get;init;}
        public required string? Ride {get;init;}
        public required int X {get;init;}
        public required int Z {get;init;}
        public required int EntryX {get;init;}
        public required int EntryZ {get;init;}
        public required byte Flags {get;init;}
        public required byte FeatureStatus {get;init;}
        public required byte PolledStatus {get;init;}
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version {get;init;}
        public required string PathsKey {get;init;}
        public required string ClockKey {get;init;}
        public required string ActivationsKey {get;init;}
        public required SnapshotRandom.State? OwnedRandom {get;init;}
        public required string? ExternalRandomKey {get;init;}
        public required uint Now {get;init;}
        public required ulong PoolEpoch {get;init;}
        public required StaffMember.Snapshot[] Members {get;init;}
        public required PoolState[] Pools {get;init;}
        public required string[] MapOrder {get;init;}
        public required string? HireHeld {get;init;}
        public required bool[] Striking {get;init;}
        public required int[] StrikeStamp {get;init;}
        public required byte[] StrikeStage {get;init;}
        public required bool ParkRunning {get;init;}
        public required bool DebugEveryoneStrikes {get;init;}
        public required int TrainingsBought {get;init;}
        public required StaffCandidateDatabase.State? Candidates {get;init;}
        public required ResearchManager.State? Research {get;init;}
        public required ParkLitter.State Litter {get;init;}
        public required NativeTileView.State Tiles {get;init;}
        public required StaffRouteService.Snapshot Requests {get;init;}
        public required SecuritySnapshot Security {get;init;}
        public required RoomState[] Rooms {get;init;}
        public required IntMapLayout RoomsLayout {get;init;}
        // Fixed named callback slots; null is deliberately different from a default provider.
        public required string?[] Callbacks {get;init;}
    }
    static string MemberKey(StaffMember m)=>$"member/{(int)m.Kind}/{m.PoolSlot}";
    Dictionary<string,object> LocalObjects()
    {
        var d=new Dictionary<string,object>(StringComparer.Ordinal) {
            ["staff"]=this,["random"]=Random,["paused"]=(Func<Guest,bool>)DefaultPaused,
            ["placed"]=(Func<IEnumerable<ParkRide>>)DefaultPlacedRides,
            ["features"]=(Func<IEnumerable<StaffFeature>>)DefaultFeatures,
            ["research-mechanics"]=(Func<int>)DefaultMechanicCount,
            ["research-advisor"]=(Action<int>)DefaultResearchAdvisor
        };
        foreach(var m in _slots.Values.SelectMany(a=>a))d.Add(MemberKey(m),m);
        if(_candidates!=null)foreach(var k in StaffTables.PoolBuildOrder)for(int i=0;i<StaffTables.PoolSize;i++)
            d.Add($"candidate/{(int)k}/{i}",_candidates.For(k,i));
        if(Litter!=null)foreach(var item in Litter.Slots)d.Add("litter/"+item.PoolSlot,item);
        for(int i=0;i<ParkEffectors.Capacity;i++)d.Add("effector/"+i,Effectors.SnapshotSlot(i));
        if(_research!=null)for(int i=0;i<ResearchManager.SlotCount;i++)d.Add("project/"+i,_research.ProjectAt(i));
        return d;
    }
    Func<object,string> Identifier(StateBindings b)
    {
        var objects=LocalObjects();
        return value=> {
            foreach(var p in objects)if(ReferenceEquals(p.Value,value) || value is Delegate && p.Value.Equals(value))return "local/"+p.Key;
            string? key=b.IdentifyReference?.Invoke(value);
            Require(Key(key),"unregistered external reference");return "external/"+key;
        };
    }
    object Resolve(string id,StateBindings b)
    {
        Require(Key(id),"reference key");
        if(id.StartsWith("local/",StringComparison.Ordinal) && LocalObjects().TryGetValue(id[6..],out var local))return local;
        if(id.StartsWith("external/",StringComparison.Ordinal))return b.ResolveReference?.Invoke(id[9..])
            ?? throw new ArgumentException("Missing staff external reference: "+id);
        throw new ArgumentException("Unknown staff local reference: "+id);
    }
    Delegate?[] Callbacks()=>new Delegate?[]{Features,AnimationReady,Sound,HandleSound,HandlePlaying,Advisor,UiSound,Focus,StaffRoomAmbience,QueueMouth};
    // Includes external targets/providers without invoking any lazy getter or world callback.
    public IReadOnlyList<object> ReferencedStateObjects=>_slots.Values.SelectMany(a=>a).SelectMany(m=>m.ReferencedSnapshotObjects)
        .Concat(ReferencedSecurityObjects).Concat(Callbacks().Where(d=>d!=null).Cast<object>())
        .Concat(_rooms.SelectMany(r=>new object?[]{r.Key,r.Value.Feature.Ride}).Where(o=>o!=null).Cast<object>())
        .Distinct(ReferenceEqualityComparer.Instance).ToArray();
    static bool Key(string? id)=>!string.IsNullOrWhiteSpace(id)&&id.Length<=1024;
    static void Require(bool ok,string why){if(!ok)throw new ArgumentException("Invalid ParkStaff state: "+why);}
    StaffRouteService.SnapshotBindings RequestBindings(StateBindings b)=>new(){PathsKey=b.PathsKey,TilesKey="staff/tiles",PoolKey="walk/native-routes",Staff=this,
        Owners=_slots.Values.SelectMany(a=>a).ToDictionary(m=>"local/"+MemberKey(m))};

    public State CaptureState(StateBindings b)
    {
        RequireSnapshotReady();Require(ReferenceEquals(b.GuestGraph.Walk,Visitors.Walk),"walking graph");
        var id=Identifier(b);
        var s=new State{Version=1,PathsKey=b.PathsKey,ClockKey=b.ClockKey,ActivationsKey=b.ActivationsKey,
            OwnedRandom=_ownedRandom?.CaptureState(),ExternalRandomKey=_ownedRandom==null?"external/"+b.IdentifyReference!(Random):null,
            Now=Now,PoolEpoch=_poolEpoch,Members=_slots.Values.SelectMany(a=>a).Select(m=>m.CaptureState(id)).ToArray(),
            Pools=_slots.Keys.Select(k=>new PoolState{Kind=k,Free=_free[k].Select(m=>m.PoolSlot).ToArray(),Active=_active[k].Select(m=>m.PoolSlot).ToArray()}).ToArray(),
            MapOrder=_mapList.Select(m=>"local/"+MemberKey(m)).ToArray(),HireHeld=_hireHeld==null?null:id(_hireHeld),
            Striking=(bool[])_striking.Clone(),StrikeStamp=(int[])_strikeStamp.Clone(),StrikeStage=(byte[])_strikeStage.Clone(),
            ParkRunning=ParkRunning,DebugEveryoneStrikes=DebugEveryoneStrikes,TrainingsBought=TrainingsBought,
            Candidates=_candidates?.CaptureState(),Research=_research?.CaptureState(d=>id(d)),
            Litter=Litter.CaptureState(m=>id(m),d=>id(d)),Tiles=Tiles.CaptureState(b.PathsKey,d=>id(d)),
            Requests=RouteRequests.CaptureState(RequestBindings(b)),Security=CaptureSecurityState(id),
            RoomsLayout=_rooms.CaptureLayout(),Rooms=_rooms.Select(r=>new RoomState{Key=id(r.Key),Ride=r.Value.Feature.Ride==null?null:id(r.Value.Feature.Ride),
                X=r.Value.Feature.Origin.X,Z=r.Value.Feature.Origin.Z,EntryX=r.Value.Feature.Entry.X,EntryZ=r.Value.Feature.Entry.Z,
                Flags=r.Value.Feature.Flags,FeatureStatus=r.Value.Feature.Status,PolledStatus=r.Value.Status}).ToArray(),
            Callbacks=Callbacks().Select(d=>d==null?null:id(d)).ToArray()};
        ValidateStructure(s,b);return s;
    }
    static void ValidateStructure(State s,StateBindings b)
    {
        ArgumentNullException.ThrowIfNull(s);ArgumentNullException.ThrowIfNull(b);
        Require(s.Version==1 && Key(s.PathsKey)&&Key(s.ClockKey)&&Key(s.ActivationsKey)
            &&s.PathsKey==b.PathsKey&&s.ClockKey==b.ClockKey&&s.ActivationsKey==b.ActivationsKey,"version/resource identities");
        Require((s.OwnedRandom!=null)!=(s.ExternalRandomKey!=null),"RNG ownership");
        Require(s.Members?.Length==25 &&s.Pools?.Length==5&&s.MapOrder!=null&&s.MapOrder.Length<=25
            &&s.Striking?.Length==6&&s.StrikeStamp?.Length==6&&s.StrikeStage?.Length==6
            &&s.Rooms!=null&&s.Rooms.Length<=100_000&&s.Callbacks?.Length==10,"table bounds");
        Require(s.Members!.All(m=>m!=null)&&s.Members.Select(m=>(m.Kind,m.PoolSlot)).Distinct().Count()==25,"member identities");
        var members=s.Members.ToDictionary(m=>(m.Kind,m.PoolSlot));
        var kinds=new HashSet<StaffKind>();var active=new HashSet<string>();
        foreach(var p in s.Pools!) {
            Require(p!=null &&Enum.IsDefined(p.Kind)&&kinds.Add(p.Kind)&&p.Free!=null&&p.Active!=null,"pool identity");
            var all=p.Free!.Concat(p.Active!).ToArray();Require(all.Length==5&&all.Distinct().Count()==5&&all.All(i=>i>=0&&i<5),"pool partition");
            foreach(int i in all)Require(members.TryGetValue((p.Kind,i),out var m)&&m.Active==p.Active!.Contains(i),"member active flags");
            foreach(int i in p.Active!)active.Add($"local/member/{(int)p.Kind}/{i}");
        }
        Require(s.MapOrder!.Distinct().Count()==s.MapOrder.Length&&active.SetEquals(s.MapOrder),"map membership");
        Require(s.HireHeld==null||active.Contains(s.HireHeld),"held member");
        if(s.Candidates!=null)StaffCandidateDatabase.ValidateState(s.Candidates);
        if(s.Research!=null)ResearchManager.ValidateState(s.Research);
        ParkLitter.ValidateState(s.Litter);NativeTileView.ValidateState(s.Tiles);
    }
    /// <summary>Allocates unpublished shells only. Root must freeze and stage the referenced walk,
    /// sim, clock, activation owner and providers first; bindings must NEVER target the live world.</summary>
    public static ParkStaff AllocateState(State s,ParkVisitors visitors,ParkClock clock,NativeActivationSequence activations,StateBindings b)
    {
        ValidateStructure(s,b);Require(ReferenceEquals(visitors.Walk,b.GuestGraph.Walk),"walking resource");
        Func<int,int>? external=null;
        if(s.ExternalRandomKey!=null){Require(s.ExternalRandomKey.StartsWith("external/"),"external RNG key");
            external=b.ResolveReference?.Invoke(s.ExternalRandomKey[9..]) as Func<int,int>;Require(external!=null,"external RNG binding");}
        var p=new ParkStaff(visitors,clock,activations,external,true);
        if(s.OwnedRandom!=null)p._ownedRandom=SnapshotRandom.FromState(s.OwnedRandom);
        p._candidates=s.Candidates==null?null:StaffCandidateDatabase.FromState(s.Candidates);
        p._research=s.Research==null?null:new ResearchManager();
        p.Tiles=NativeTileView.FromState(s.Tiles,b.PathsKey,p.Paths,key=>(Delegate)p.Resolve(key,b));
        p.RouteRequests=new StaffRouteService(p.Paths,p.Tiles,p.Routes);
        p.Litter=ParkLitter.AllocateState(s.Litter,p.Random,p.Activations,key=>(Delegate)p.Resolve(key,b));
        return p;
    }
    /// <summary>Finish after references can resolve all staged members, rides and controller cycles.
    /// Invalid data only affects this unpublished shell. No Hire, Step, search, research or events replay.</summary>
    public void HydrateState(State s,StateBindings b)
    {
        Require(!_snapshotReady,"already hydrated");ValidateStructure(s,b);
        object R(string key)=>Resolve(key,b);
        T? Ref<T>(string? key) where T:class {if(key==null)return null;var value=R(key) as T;Require(value!=null,"binding type");return value;}
        // Resolve/preflight all external references before state writes.
        var cb=s.Callbacks;
        var features=Ref<Func<IEnumerable<StaffFeature>>>(cb[0]);var ready=Ref<Func<StaffMember,bool>>(cb[1]);
        var sound=Ref<Action<StaffMember,int,int>>(cb[2]);var handles=Ref<Action<StaffMember,int,int,int>>(cb[3]);
        var playing=Ref<Func<StaffMember,int,bool>>(cb[4]);var advisor=Ref<Action<int>>(cb[5]);var ui=Ref<Action<int>>(cb[6]);
        var focus=Ref<Action<StaffMember>>(cb[7]);var ambience=Ref<Action<StaffFeature,bool>>(cb[8]);var mouth=Ref<Func<ParkRide,ParkCell?>>(cb[9]);
        var held=Ref<StaffMember>(s.HireHeld);
        var rooms=s.Rooms.Select(r=>{Require(r!=null,"null room");var key=R(r.Key);var ride=Ref<ParkRide>(r.Ride);
            return (key,feature:new StaffFeature(new(r.X,r.Z),new(r.EntryX,r.EntryZ),r.Flags,r.FeatureStatus,ride,key),status:r.PolledStatus);}).ToArray();
        Require(rooms.Select(r=>r.key).Distinct().Count()==rooms.Length,"duplicate room key");
        var roomProbe=new SnapshotReferenceMap<object,int>(EqualityComparer<object>.Default);
        foreach(var r in rooms)roomProbe.Add(r.key,0);roomProbe.RestoreLayout(s.RoomsLayout);
        foreach(var m in s.Members)_slots[m.Kind][m.PoolSlot].ValidateSnapshot(m,R);
        var used=new HashSet<int>();
        void Chain(NativeGuestRoute.State? route) {if(route==null)return;NativeGuestRoute.ValidateState(route,Routes);
            for(int at=route.SlotIndex;at!=-1;at=Routes.Next(at))Require(used.Add(at),"shared route slot has multiple owners");}
        foreach(var g in b.GuestGraph.Snapshot.Guests)Chain(g.NativeLease?.Cursor);
        foreach(var m in s.Members)Chain(m.Route);
        ValidateSecuritySnapshot(s.Security,R);
        RouteRequests.ValidateSnapshot(s.Requests,RequestBindings(b));
        if(s.Research!=null)_research.RestoreState(s.Research,key=>(Delegate)R(key));
        Litter.HydrateClaimants(s.Litter,key=>Ref<StaffMember>(key)!);
        foreach(var m in s.Members)_slots[m.Kind][m.PoolSlot].RestoreState(m,R);
        foreach(var p in s.Pools){_free[p.Kind].Clear();_free[p.Kind].AddRange(p.Free.Select(i=>_slots[p.Kind][i]));
            _active[p.Kind].Clear();_active[p.Kind].AddRange(p.Active.Select(i=>_slots[p.Kind][i]));}
        _mapList.Clear();_mapList.AddRange(s.MapOrder.Select(key=>Ref<StaffMember>(key)!));_hireHeld=held;
        _rooms.Clear();foreach(var r in rooms)_rooms.Add(r.key,(r.feature,r.status));
        _rooms.RestoreLayout(s.RoomsLayout);
        s.Striking.CopyTo(_striking,0);s.StrikeStamp.CopyTo(_strikeStamp,0);s.StrikeStage.CopyTo(_strikeStage,0);
        Now=s.Now;_poolEpoch=s.PoolEpoch;ParkRunning=s.ParkRunning;DebugEveryoneStrikes=s.DebugEveryoneStrikes;TrainingsBought=s.TrainingsBought;
        Features=features;AnimationReady=ready;Sound=sound;HandleSound=handles;HandlePlaying=playing;Advisor=advisor;
        UiSound=ui;Focus=focus;StaffRoomAmbience=ambience;QueueMouth=mouth;
        RouteRequests.RestoreState(s.Requests,RequestBindings(b));RestoreSecurityState(s.Security,R);
        _snapshotReady=true;
    }
}
