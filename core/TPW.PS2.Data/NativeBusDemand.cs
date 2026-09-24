namespace TPW.PS2.Data;

/// <summary>16B7B8 object sum and16B5C0 three-way batch bound. Inputs are concrete live
/// object values and native entrance-group counts, not authored effect magnitudes.</summary>
public static class NativeBusDemand
{
    public readonly record struct Attraction(int Kind,uint Key,int Value,byte Tier);
    public static int Score(IEnumerable<Attraction> nativeOrderedObjects,Func<int,int> draw)
    {
        bool any=false;uint total=10;
        foreach(var item in nativeOrderedObjects)
        {
            any=true;int r=draw(10);
            if(r<0 || r>=10) throw new InvalidOperationException("native bus draw outside rand(10)");
            int basis=20+r, extra=0, divisor=2;
            if(item.Kind==5) basis=unchecked(basis+item.Value);
            else if(item.Kind is 1 or 3 or 6 or 7)
            { extra=20; basis=unchecked(basis+item.Value*item.Tier); }
            else if(item.Kind==2) divisor=10;
            total=unchecked(total+unchecked((uint)(basis+extra))/(uint)divisor);
        }
        return any?unchecked((int)total):0;
    }
    public static int Batch(int score,int offset,int divisor,int entranceGroupCount,
        int ceiling,int population,bool loadsOfKids=false)
    {
        if(divisor==0) throw new DivideByZeroException("native bus denominator");
        int scoreBound=unchecked(unchecked(score+offset)*0x1333)/divisor;
        int groupBound=unchecked(20-entranceGroupCount);
        return loadsOfKids ? Math.Min(20,groupBound)
            : Math.Min(Math.Min(scoreBound,groupBound),unchecked(ceiling-population));
    }
}
