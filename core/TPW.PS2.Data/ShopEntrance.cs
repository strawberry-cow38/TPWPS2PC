namespace TPW.PS2.Data;

/// <summary>Compiled connection A, in the port placement frame. Native rotations have
/// the opposite numbering sense; use the same geometric rotation as placement, not a
/// native rotation number copied unchanged. Refuse a geometry mismatch rather than guessing.</summary>
public static class ShopEntrance
{
    public static ParkCell? Inside(AssetResourceDatabase.Entry record, ParkCell origin,
                                   int turns, int width, int depth, ParkCell? approach)
    {
        if (record == null || record.Kind != AssetResourceDatabase.AssetKind.Shop || approach == null) return null;
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
