using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

public sealed partial class ParkSim
{
    public const int StateVersion = 1;
    // State is the ParkSim owner graph, not a complete Viewer/world save. Paths, visitors,
    // staff, and presentation are separate owners. Ground/type bindings must be staged/pure.
    public sealed class StateBindings
    {
        public ScriptedBindings Scripts { get; init; }
        public Func<TrackGround, string> IdentifyTrackGround { get; init; }
        public Func<string, TrackGround> ResolveTrackGround { get; init; }
        public Func<CoasterType, string> IdentifyCoasterType { get; init; }
        public Func<string, CoasterType> ResolveCoasterType { get; init; }
        public Func<CoasterTrack, string> IdentifyCoasterGround { get; init; }
        public Func<string, Func<int, int, int>> ResolveCoasterGround { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version { get; init; }
        public required ScriptedState Core { get; init; }
        public required TrackLayoutRecord[] TrackLayouts { get; init; }
        public required TrackRideRecord[] TrackRides { get; init; }
        public required CoasterTrackRecord[] CoasterTracks { get; init; }
        public required CoasterRecord[] Coasters { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TrackLayoutRecord
    {
        public required string Id { get; init; }
        public required TrackLayout.State Layout { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TrackRideRecord
    {
        public required string Id { get; init; }
        public required string LayoutId { get; init; }
        public required TrackRideSim.State Simulation { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CoasterTrackRecord
    {
        public required string Id { get; init; }
        public required string GroundKey { get; init; }
        public required CoasterTrack.State Track { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CoasterRecord
    {
        public required string Id { get; init; }
        public required string TrackId { get; init; }
        public required CoasterSim.State Simulation { get; init; }
    }

    public State CaptureState(StateBindings bindings, Func<StaffMember,string> identifyStaff = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        StateRequire(_rides.Count <= ScriptedGraphLimit, "ride collection bound");
        var tracks = new Dictionary<TrackRideSim,string>(); var coasters = new Dictionary<CoasterSim,string>();
        var layouts = new Dictionary<TrackLayout,string>(); var rings = new Dictionary<CoasterTrack,string>();
        foreach (var ride in _rides)
        {
            if (ride.Track != null)
            {
                tracks.TryAdd(ride.Track,"t"+tracks.Count);layouts.TryAdd(ride.Track.Track,"l"+layouts.Count);
            }
            if (ride.Coaster != null)
            {
                coasters.TryAdd(ride.Coaster,"c"+coasters.Count);rings.TryAdd(ride.Coaster.Track,"r"+rings.Count);
            }
        }
        long captureBudget = ScriptedValueLimit;
        void Charge(long n) { StateRequire(n>=0 && (captureBudget-=n)>=0, "native capture allocation budget"); }
        foreach(var t in tracks.Keys) Charge(56L+t.Cars.Count*32L);
        foreach(var l in layouts.Keys) Charge(l.Pieces.Count*32L+l.Waypoints.Count*2L+l.Upgrades.Count*2L);
        foreach(var c in coasters.Keys) Charge(c.SnapshotValueCount);
        foreach(var r in rings.Keys) Charge(r.StateNodesForSim().Count*384L);
        return new State
        {
            Version=StateVersion, Core=CaptureGraph(bindings.Scripts,identifyStaff,tracks,coasters),
            TrackLayouts=layouts.Select(p=>new TrackLayoutRecord {Id=p.Value,
                Layout=p.Key.CaptureState(StateKey(bindings.IdentifyTrackGround?.Invoke(p.Key.Ground)))}).ToArray(),
            TrackRides=tracks.Select(p=>new TrackRideRecord {Id=p.Value,LayoutId=layouts[p.Key.Track],Simulation=p.Key.CaptureState()}).ToArray(),
            CoasterTracks=rings.Select(p=>new CoasterTrackRecord {Id=p.Value,
                GroundKey=StateKey(bindings.IdentifyCoasterGround?.Invoke(p.Key)),
                Track=p.Key.CaptureState(StateKey(bindings.IdentifyCoasterType?.Invoke(p.Key.Type)))}).ToArray(),
            Coasters=coasters.Select(p=>new CoasterRecord {Id=p.Value,TrackId=rings[p.Key.Track],Simulation=p.Key.CaptureState()}).ToArray()
        };
    }

    public static ParkSim FromState(State state, ParkPaths paths, StateBindings bindings,
        Func<string,StaffMember> resolveStaff = null) => BuildFullState(state,paths,bindings,resolveStaff,false);
    public static ParkSim AllocateState(State state,ParkPaths paths,StateBindings bindings)
        =>BuildFullState(state,paths,bindings,null,true);
    static ParkSim BuildFullState(State state,ParkPaths paths,StateBindings bindings,Func<string,StaffMember> resolveStaff,bool deferStaff)
    {
        ArgumentNullException.ThrowIfNull(state); ArgumentNullException.ThrowIfNull(bindings);
        StateRequire(state.Version == StateVersion && state.Core != null, "ParkSim envelope version/core");
        void CheckArray<T>(T[] a) => StateRequire(a != null && a.Length <= ScriptedGraphLimit,"native owner table bound");
        CheckArray(state.TrackLayouts);CheckArray(state.TrackRides);CheckArray(state.CoasterTracks);CheckArray(state.Coasters);
        long budget=ScriptedValueLimit;
        void Charge(long n) { StateRequire(n>=0 && (budget-=n)>=0, "native restore allocation budget"); }
        foreach(var item in state.TrackLayouts)
        {
            StateRequire(item?.Layout?.Pieces!=null && item.Layout.Waypoints!=null && item.Layout.Upgrades!=null,"layout collections");
            Charge(item.Layout.Pieces.Length*32L+item.Layout.Waypoints.Length*2L+item.Layout.Upgrades.Length*2L);
        }
        foreach(var item in state.TrackRides)
        { StateRequire(item?.Simulation?.Cars!=null,"track car collection"); Charge(56L+item.Simulation.Cars.Length*32L); }
        foreach(var item in state.CoasterTracks)
        { StateRequire(item?.Track?.Nodes!=null,"coaster node collection"); Charge(item.Track.Nodes.Length*384L); }
        foreach(var item in state.Coasters)
        {
            StateRequire(item?.Simulation?.Trains!=null,"train collection");
            foreach(var t in item.Simulation.Trains)
            {
                StateRequire(t?.Cars!=null,"car collection");Charge(32);
                foreach(var c in t.Cars) { StateRequire(c?.Riders!=null,"rider collection");Charge(32L+c.Riders.Length); }
            }
        }
        T Lookup<T>(Dictionary<string,T> map,string id) where T:class
        { StateKey(id);StateRequire(map.TryGetValue(id,out var value),"missing native owner: "+id);return value; }
        var grounds=new Dictionary<string,TrackGround>(StringComparer.Ordinal);
        var layouts=new Dictionary<string,TrackLayout>(StringComparer.Ordinal);
        foreach(var item in state.TrackLayouts)
        {
            StateRequire(item?.Layout!=null,"missing track layout");StateKey(item.Id);string key=StateKey(item.Layout.GroundKey);
            if(!grounds.TryGetValue(key,out var ground))
            {
                ground=bindings.ResolveTrackGround?.Invoke(key);
                StateRequire(ground!=null,"missing track ground binding");grounds.Add(key,ground);
            }
            StateRequire(layouts.TryAdd(item.Id,TrackLayout.FromState(item.Layout,key,ground)),"duplicate track layout ID");
        }
        var tracks=new Dictionary<string,TrackRideSim>(StringComparer.Ordinal);var usedLayouts=new HashSet<string>();
        foreach(var item in state.TrackRides)
        {
            StateRequire(item?.Simulation!=null,"missing track sim");StateKey(item.Id);
            StateRequire(tracks.TryAdd(item.Id,TrackRideSim.FromState(item.Simulation,Lookup(layouts,item.LayoutId))),"duplicate track sim ID");
            usedLayouts.Add(item.LayoutId);
        }
        var rings=new Dictionary<string,CoasterTrack>(StringComparer.Ordinal);
        foreach(var item in state.CoasterTracks)
        {
            StateRequire(item?.Track!=null,"missing coaster track");StateKey(item.Id);StateKey(item.GroundKey);StateKey(item.Track.TypeKey);
            var type=bindings.ResolveCoasterType?.Invoke(item.Track.TypeKey);
            var ground=bindings.ResolveCoasterGround?.Invoke(item.GroundKey);
            StateRequire(type!=null&&ground!=null,"missing coaster type/ground binding");
            StateRequire(rings.TryAdd(item.Id,CoasterTrack.FromState(item.Track,item.Track.TypeKey,type,ground)),"duplicate coaster track ID");
        }
        var coasters=new Dictionary<string,CoasterSim>(StringComparer.Ordinal);var usedRings=new HashSet<string>();
        foreach(var item in state.Coasters)
        {
            StateRequire(item?.Simulation!=null,"missing coaster sim");StateKey(item.Id);
            StateRequire(coasters.TryAdd(item.Id,CoasterSim.FromState(item.Simulation,Lookup(rings,item.TrackId))),"duplicate coaster sim ID");
            usedRings.Add(item.TrackId);
        }
        StateRequire(usedLayouts.Count==layouts.Count&&usedRings.Count==rings.Count,"unowned layout/track record");
        return BuildScriptedState(state.Core,paths,bindings.Scripts,resolveStaff,true,tracks,coasters,deferStaff);
    }

    // Equivalent callbacks to AttachTrack/AttachCoaster, WITHOUT Rebuilt/SetOpen/Sync, defaults,
    // events or other simulation actions. In particular preserve cached parent status verbatim.
    void BindRestoredVehicles()
    {
        foreach(var ride in _rides)
        {
            if(ride.Track is {} track)
            {
                track.TakeHead=()=>ride.TryTakeFromQueue(out int guest)?guest:null;
                track.Wear=()=>ApplyWear(ride,Tick);track.BreakdownCheck=()=>TrackBreakdown(ride);
                track.Released+=ride.Leaves;
            }
            if(ride.Coaster is {} coaster)
            {
                coaster.TakeHead=()=>ride.TryTakeFromQueue(out int guest)?guest:null;
                coaster.Wear=()=>ApplyWear(ride,Tick);coaster.BreakdownCheck=()=>CoasterBreakdown(ride);
                coaster.Released+=ride.Leaves;
            }
        }
    }
}
