#nullable enable
using System.Text.Json.Serialization;
using Point = TPW.PS2.Data.NativeGuestMotion.Point;
using Flow = TPW.PS2.Data.NativeEntranceFlow;
namespace TPW.PS2.Data;

/// <summary>The Viewer's deferred route-result queue. A submit's result is delivered at the
/// next pump, never regenerated on load: paths may have changed since its search. Stale and
/// duplicate results, including inactive Guest objects, are deliberately retained in order.
/// Capture at a quiescent boundary, not while a pump's returned batch is being processed.</summary>
public sealed class NativeEntranceMailbox
{
    readonly Queue<Flow.RouteResult> _pending = new();
    public int Count => _pending.Count;
    public IReadOnlyList<Guest> ReferencedGuests => _pending.Select(r=>r.Guest)
        .Distinct<Guest>(ReferenceEqualityComparer.Instance).ToArray();
    public void Enqueue(Flow.RouteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);ArgumentNullException.ThrowIfNull(result.Guest);
        _pending.Enqueue(result);
    }
    public Flow.RouteResult[] Drain() { var ready=_pending.ToArray();_pending.Clear();return ready; }
    public void Clear()=>_pending.Clear();
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PointState
    {
        public required short X {get;init;}
        public required short Z {get;init;}
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ResultState
    {
        public required ulong Token {get;init;}
        public required int GuestGraphId {get;init;}
        public required PointState[]? Waypoints {get;init;}
        public required string? Failure {get;init;}
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version {get;init;}
        public required ResultState[] Pending {get;init;}
    }
    public State CaptureState(GuestWalk.GuestGraph graph)
    {
        var state=new State {Version=1,Pending=_pending.Select(r=>new ResultState {
            Token=r.Token,GuestGraphId=graph.GuestGraphId(r.Guest),Failure=r.Failure,
            Waypoints=r.Waypoints?.Select(p=>new PointState {X=p.X,Z=p.Z}).ToArray() }).ToArray()};
        ValidateState(state,graph);return state;
    }
    public static void ValidateState(State s,GuestWalk.GuestGraph graph)
    {
        ArgumentNullException.ThrowIfNull(s);ArgumentNullException.ThrowIfNull(graph);
        if(s.Version!=1 || s.Pending==null || s.Pending.Length>100_000)
            throw new ArgumentException("Invalid entrance mailbox version/count.");
        long points=0;
        foreach(var r in s.Pending)
        {
            if(r==null || !graph.GuestsByGraphId.ContainsKey(r.GuestGraphId)
                || r.Failure?.Length>16384 || (points+=r.Waypoints?.Length??0)>1_000_000
                || r.Waypoints?.Any(p=>p==null)==true)
                throw new ArgumentException("Invalid entrance mailbox result.");
            // No token liveness test: a result can be stale, repeated or failed independently
            // of the controller's current token. Null and empty waypoints are NOT equivalent.
        }
    }
    public static NativeEntranceMailbox FromState(State s,GuestWalk.GuestGraph graph)
    {
        ValidateState(s,graph);
        var box=new NativeEntranceMailbox();
        foreach(var r in s.Pending) box._pending.Enqueue(new(r.Token,graph.GuestByGraphId(r.GuestGraphId),
            r.Waypoints?.Select(p=>new Point(p.X,p.Z)).ToArray(),r.Failure));
        return box;
    }
}
