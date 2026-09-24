namespace TPW.PS2.Data;

/// <summary>Identity-bound compiled candidate inputs for 20C138. No SAM effect fallback,
/// no value45 substitute, no substitution of public approach for inside connection A.</summary>
public static class PlacedDestination
{
    public static ParkCell? Entry(ParkRide ride)
    {
        var record=ride.Definition?.CompiledEntry;
        if(record==null || ride.PlacementTurns is not int turns) return null;
        int w=record.Width,h=record.Depth,x=record.ConnectionA.X,z=record.ConnectionA.Z;
        for(int i=0;i<(turns&3);i++) { (x,z)=(h-1-z,x); (w,h)=(h,w); }
        if(w!=ride.Width || h!=ride.Height) return null;
        // Native doesn't test IsPresent before origin addition. Invalid resulting bounds
        // are rejected by the scorer; preserve signed16 sums instead of adding a new rule.
        return new ParkCell(unchecked((short)(ride.Origin.X+x)),unchecked((short)(ride.Origin.Z+z)));
    }

    public static GuestDestinationScore.Candidate? Read(ParkRide ride, bool reliefOccupied)
    {
        var record=ride.Definition?.CompiledEntry;
        if(record==null || ride.DestinationEntry is not {} entry || ride.Value is not int value) return null;
        var shop=record.Shop;
        bool relief=record.Kind==AssetResourceDatabase.AssetKind.Feature && (record.RawFeatureFlags.GetValueOrDefault()&1)!=0;
        return new GuestDestinationScore.Candidate(entry,(int)record.Kind,value,
            shop?.HungerReduction??0,shop?.ThirstReduction??0,relief,
            !ride.ReliefUsesOccupancy || !reliefOccupied,shop?.Product??-1);
    }

    /// <summary>Pool iterator1E5AF0/1E5CA8, family chain from369E00. New activations are
    /// prepended by each allocator. Sim.Rides records activation order, not reused IDs.</summary>
    public static IEnumerable<ParkRide> InNativeOrder(IReadOnlyList<ParkRide> rides)
    {
        foreach(int kind in new[]{3,6,1,7,4,5,2})
            for(int i=rides.Count-1;i>=0;i--)
                if((int?)rides[i].Definition?.CompiledEntry?.Kind==kind) yield return rides[i];
    }
}
