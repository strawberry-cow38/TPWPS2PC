namespace TPW.PS2.Data;

/// <summary>A directed, destination-only entrance edge. The original kind7 route arm
/// requires the destination pointer and outgoing link, not globally open footprint ground.
/// Owner/liveness scoping is the port's adapter; an occupied terminal always retains its
/// escape edge even if its building closes or disappears.</summary>
public sealed class GuestTerminal
{
    public ParkRide Owner { get; }
    public ParkCell Approach { get; }
    public ParkCell Entry { get; }
    readonly Func<bool> _canEnter;
    public bool CanEnter => _canEnter();

    public GuestTerminal(ParkRide owner, ParkCell approach, ParkCell entry, Func<bool> canEnter)
    {
        if (Math.Abs((long)approach.X-entry.X)+Math.Abs((long)approach.Z-entry.Z)!=1)
            throw new ArgumentException("A terminal is exactly one cardinal edge");
        Owner=owner ?? throw new ArgumentNullException(nameof(owner));
        Approach=approach; Entry=entry;
        _canEnter=canEnter ?? throw new ArgumentNullException(nameof(canEnter));
    }
}
