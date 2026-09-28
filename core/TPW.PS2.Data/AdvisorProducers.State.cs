using System.Text.Json.Serialization;
namespace TPW.PS2.Data;
public sealed partial class AdvisorProducers
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version {get;init;}
        public required string Clock {get;init;}
        public required string Sim {get;init;}
        public required string Staff {get;init;}
        public required string Visitors {get;init;}
        public required string ParkOpen {get;init;}
        public required string Placements {get;init;}
        public required string Variety {get;init;}
        public required string ResearchPercent {get;init;}
        public required string UpgradesInUse {get;init;}
        public required string UpgradeResearchPercent {get;init;}
    }
    // Producer quantities are computed, not cached. Persist its resource topology/hooks;
    // captured closure VALUES belong to the world/provider owner, not delegate serialization.
    public Snapshot CaptureState(AdvisorStateBindings b)=>new(){Version=1,Clock=b.Identify(Clock),Sim=b.Identify(Sim),
        Staff=b.Identify(Staff),Visitors=b.Identify(Visitors),ParkOpen=b.Identify(ParkOpen),Placements=b.Identify(Placements),
        Variety=b.Identify(Variety),ResearchPercent=b.Identify(ResearchPercent),UpgradesInUse=b.Identify(UpgradesInUse),
        UpgradeResearchPercent=b.Identify(UpgradeResearchPercentHook)};
    public static AdvisorProducers FromState(Snapshot s,AdvisorStateBindings b)
    {
        ArgumentNullException.ThrowIfNull(s);ArgumentNullException.ThrowIfNull(b);
        if(s.Version!=1)throw new InvalidDataException("Invalid advisor producer version");
        var clock=b.Required<ParkClock>(s.Clock);var sim=b.Resolve<ParkSim>(s.Sim);
        var staff=b.Resolve<ParkStaff>(s.Staff);var visitors=b.Resolve<ParkVisitors>(s.Visitors);
        if(staff!=null && (!ReferenceEquals(staff.Sim,sim)||!ReferenceEquals(staff.Visitors,visitors)||!ReferenceEquals(staff.Clock,clock))
            ||visitors!=null&&!ReferenceEquals(visitors.Sim,sim))throw new InvalidDataException("Advisor producer resource identity mismatch");
        var result=new AdvisorProducers(clock,sim,staff,visitors){ParkOpen=b.Resolve<Func<bool>>(s.ParkOpen),
            Placements=b.Resolve<Func<IEnumerable<AdvisorPlacement>>>(s.Placements),Variety=b.Resolve<Func<int,int>>(s.Variety),
            ResearchPercent=b.Resolve<Func<int,int>>(s.ResearchPercent),UpgradesInUse=b.Resolve<Func<int>>(s.UpgradesInUse),
            UpgradeResearchPercentHook=b.Resolve<Func<int>>(s.UpgradeResearchPercent)};
        if(!ReferenceEquals(result.Sim,sim)||!ReferenceEquals(result.Visitors,visitors))
            throw new InvalidDataException("Advisor producer constructor fallback differs from saved graph");
        return result;
    }
}
