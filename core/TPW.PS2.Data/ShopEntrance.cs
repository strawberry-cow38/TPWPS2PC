namespace TPW.PS2.Data;

/// <summary>Compiled connection A, in the port placement frame. Native rotations have
/// the opposite numbering sense; use the same geometric rotation as placement, not a
/// native rotation number copied unchanged. Refuse a geometry mismatch rather than guessing.
/// The historical class name predates the traced kind2 relief branch: that branch uses
/// exactly the same inside connection A and directed terminal permission as kind4 shops.</summary>
public static class ShopEntrance
{
    public static ParkCell? Inside(AssetResourceDatabase.Entry record, ParkCell origin,
                                   int turns, int width, int depth, ParkCell? approach)
    {
        bool service=record?.Kind==AssetResourceDatabase.AssetKind.Shop
            || (record?.Kind==AssetResourceDatabase.AssetKind.Feature && (record.RawFeatureFlags.GetValueOrDefault()&1)!=0);
        return service ? Connection(record,origin,turns,width,depth,approach) : null;
    }

    /// <summary>The same rotated connection A for any compiled placement. For a ride it is the
    /// entrance connection cell that ride vtable +17C returns (116EC0: the placed origin +74 plus the
    /// rotated record +0xC that 1E1760 reads), where the head of its queue stands.</summary>
    public static ParkCell? Connection(AssetResourceDatabase.Entry record, ParkCell origin,
                                       int turns, int width, int depth, ParkCell? approach)
    {
        if (record == null || approach == null) return null;
        var a=record.ConnectionA;
        int w=record.Width, h=record.Depth, x=a.X, z=a.Z;
        if (!a.IsPresent || x>=w || z>=h || a.Direction>3) return null;
        var dir=a.Direction switch { 0=>(X:0,Z:-1),1=>(X:-1,Z:0),2=>(X:0,Z:1),_=>(X:1,Z:0) };
        for(int i=0;i<(turns&3);i++)
        {
            (x,z)=(h-1-z,x); (w,h)=(h,w); dir=(-dir.Z,dir.X);
        }
        var inside=origin.Offset(x,z);
        return w==width && h==depth && inside.Offset(dir.X,dir.Z)==approach.Value ? inside : null;
    }
}
