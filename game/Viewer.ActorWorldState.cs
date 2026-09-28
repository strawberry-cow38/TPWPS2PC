using System.Text.Json.Serialization;
using Godot;
namespace TPWPS2Viewer;
public partial class Viewer
{
    /// <summary>Joined logical world + guest/staff presentation. NOT complete player persistence:
    /// controllers/advisor/bus/audio/tools and remaining scene roots must join before publication.
    /// In particular, adding the staged Viewer to a SceneTree would replay _Ready; no such API is
    /// offered here. Load Game remains unavailable.</summary>
    public sealed class ActorWorldBindings
    {
        public required WorldCoreBindings Core {get;init;}
        public required Func<WorldCoreRegistry,GuestStateBindings> Characters {get;init;}
        public required Func<WorldCoreRegistry,GuestStateBindings,StaffStateBindings> Staff {get;init;}
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ActorWorldState(int Version,WorldCoreState Core,GuestPresentationState Guests,StaffPresentationState Staff);
    public ActorWorldState CaptureActorWorld(ActorWorldBindings b)
    {
        ArgumentNullException.ThrowIfNull(b);
        var core=CaptureWorldCoreState(b.Core,out var registry);
        var characters=b.Characters(registry);var staff=b.Staff(registry,characters);
        return new(1,core,CaptureGuestPresentation(characters),CaptureStaffPresentation(staff));
    }
    public sealed class StagedActorWorld:IDisposable
    {
        public Viewer Viewer {get;}
        public WorldCoreRegistry Registry {get;internal set;}
        internal StagedActorWorld(){Viewer=new Viewer();}
        public void Dispose(){if(IsInstanceValid(Viewer))Viewer.DisposeUnpublishedActorWorld();}
    }
    /// <summary>All state goes to NEW unpublished owners. If any later join rejects, discard
    /// every staged node, including nodes the Viewer constructor creates before _Ready. The
    /// caller's live Viewer, providers, caches and global audio buses are never published into.</summary>
    public static StagedActorWorld StageActorWorld(ActorWorldState s,ActorWorldBindings b)
    {
        ArgumentNullException.ThrowIfNull(s);ArgumentNullException.ThrowIfNull(b);
        WC(s.Version==1&&s.Core!=null&&s.Guests!=null&&s.Staff!=null,"actor-world envelope");
        var stage=new StagedActorWorld();
        try {
            stage.Registry=stage.Viewer.RestoreWorldCoreState(s.Core,b.Core);
            var characters=b.Characters(stage.Registry);
            var guests=stage.Viewer.RestoreGuestPresentation(s.Guests,characters);
            var staff=stage.Viewer.RestoreStaffPresentation(s.Staff,b.Staff(stage.Registry,characters));
            var siblings=new List<(Node3D Node,GuestNodeState State)>();
            foreach(var e in s.Guests.Entries)if(e.Actor!=null)siblings.Add((guests[e.Guest],e.Actor));
            if(s.Staff.Root!=null)siblings.Add((stage.Viewer._staffRoot,s.Staff.Root));
            foreach(var e in s.Staff.Actors)siblings.Add((staff.Members[e.Member],e.Node));
            foreach(var e in s.Staff.Litter)siblings.Add((staff.Litter[e.Litter],e.Node));
            foreach(var group in siblings.Where(x=>x.State.Sibling>=0).GroupBy(x=>x.Node.GetParent())) {
                WC(group.Key!=null&&group.Count()==group.Key.GetChildCount(),"incomplete actor parent topology");
                var order=group.OrderBy(x=>x.State.Sibling).ToArray();
                WC(order.Select(x=>x.State.Sibling).SequenceEqual(Enumerable.Range(0,order.Length)),"actor sibling order");
                foreach(var x in order)group.Key.MoveChild(x.Node,x.State.Sibling);
            }
            return stage;
        }catch{stage.Dispose();throw;}
    }
    void DisposeUnpublishedActorWorld()
    {
        if(IsInsideTree()||GetParent()!=null)throw new InvalidOperationException("Only unpublished stages may be discarded here");
        foreach(var node in new Node[]{_weather.Root,_flags.Root,_thoughts.Root,_park?.Root})
            if(node!=null&&IsInstanceValid(node)&&node.GetParent()==null)node.Free();
        Free();
    }
}
