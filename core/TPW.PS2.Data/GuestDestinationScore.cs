namespace TPW.PS2.Data;

/// <summary>20C138's integer score, and 20C6A8's sequential choice. Inputs are the results
/// of the candidate's concrete virtual getters, NOT SAM fields chosen by similarity.
/// See findings/native-destination-score.md. This class does not establish enumeration,
/// placed-state eligibility, routing, or the source of the runtime inputs.</summary>
public static class GuestDestinationScore
{
    public readonly record struct Wants(int Hunger, int Thirst, int Toilet, int Sickness,
                                        int Happiness, int PreferredIntensity);
    public readonly record struct Candidate(ParkCell Entry, int Kind, int Value,
        int HungerEffect, int ThirstEffect, bool Relief, bool ReliefAvailable,
        int Product = -1);

    // Signed words at 36CE88. Row = input/10, column = need/10; no interpolation.
    static readonly int[] NeedTable =
    {
        0,0,0,0,0,0,0,0,0,0,0,
        0,0,1,2,5,7,11,15,20,25,31,
        0,0,1,4,7,11,16,21,28,36,44,
        0,0,2,4,8,13,19,26,35,44,54,
        0,0,2,5,10,15,22,30,40,51,63,
        0,0,2,6,11,17,25,34,45,57,70,
        0,0,3,6,12,19,27,37,49,62,77,
        0,0,3,7,13,20,30,40,53,67,83,
        0,0,3,8,14,22,32,43,57,72,89,
        0,0,3,8,15,23,34,46,60,76,94,
        0,1,4,9,16,25,36,49,64,81,100
    };
    static readonly int[] ReliefTable =
        { -20,-20,-20,-20,-20,-20,-20,-20,0,1,2,4,7,11,17,26,37,53,73,100,100,100 };

    public static int Need(int need, int input)
    {
        // Native DIVU has no guard. Refuse malformed data rather than silently change
        // the decoded algorithm by clamping the index to an endpoint.
        uint row=unchecked((uint)input)/10, column=unchecked((uint)need)/10;
        if(row>10 || column>10) throw new ArgumentOutOfRangeException(nameof(input),
            "Native need lookup requires indices within the decoded 11x11 table");
        return NeedTable[row*11+column];
    }
    public static int Relief(int need, bool enabled)
    {
        if(!enabled) return 0;
        uint index=unchecked((uint)(need+5))/5;
        if(index>=ReliefTable.Length) throw new ArgumentOutOfRangeException(nameof(need));
        return ReliefTable[index];
    }

    /// <summary>Early-return branches precede history division. A rejected geometry
    /// must not become a zero-score tie through -1 / historyDivisor.</summary>
    public static bool Admitted(Candidate candidate,int mapWidth,int mapDepth)
    {
        int x=unchecked((short)candidate.Entry.X),z=unchecked((short)candidate.Entry.Z);
        return x>=0 && z>=0 && x<mapWidth-1 && z<mapDepth-1
            && (candidate.Kind!=2 || (candidate.Relief && candidate.ReliefAvailable));
    }

    /// <summary>20C138, before runtime-ID history divisions. Product thresholds are
    /// caller parameters because the executable loads them from mutable globals;
    /// 50/55 are their initial values, not a claim they can never change.</summary>
    public static int Evaluate(Wants wants, Candidate candidate, ParkCell guest,
                               int mapWidth, int mapDepth, int trinketThreshold=50,
                               int giftThreshold=55)
    {
        if(mapWidth<2 || mapDepth<2) throw new ArgumentOutOfRangeException(nameof(mapWidth));
        if(trinketThreshold<0 || trinketThreshold>=100 || giftThreshold<0 || giftThreshold>=100)
            throw new ArgumentOutOfRangeException(nameof(trinketThreshold));
        // Inside connection + origin has already been rotated, then narrowed to s16.
        var entry=new ParkCell(unchecked((short)candidate.Entry.X),unchecked((short)candidate.Entry.Z));
        if(!Admitted(candidate,mapWidth,mapDepth)) return -1;
        int distance=unchecked(Math.Abs(entry.X-guest.X)+Math.Abs(entry.Z-guest.Z));
        int d=100-Math.Clamp(unchecked(distance*100)/(mapWidth+mapDepth),0,100);
        int mismatch=Math.Min(Math.Abs(unchecked(wants.PreferredIntensity-candidate.Value)),50);
        int f=2*(50-mismatch), e=candidate.Value!=0 ? 1 : 0;
        int bonus=0, weight=0;
        if(candidate.Kind==4 && candidate.Product is 2 or 3 or 6)
        {
            int threshold=candidate.Product==6 ? giftThreshold : trinketThreshold;
            if(wants.Happiness>threshold)
            {
                bonus=unchecked((wants.Happiness-threshold)*100)/(100-threshold);
                weight=3;
            }
        }
        int t=Need(wants.Thirst,candidate.ThirstEffect), h=Need(wants.Hunger,candidate.HungerEffect);
        int u=Relief(wants.Toilet,candidate.Relief), k=Relief(wants.Sickness,candidate.Relief);
        return unchecked(d+e*f+3*t+3*h+4*u+2*k+weight*bonus)/(13+e+weight);
    }

    /// <summary>Each of the four matching history slots divides in turn, with signed
    /// truncation towards zero. Repeated entries are significant, not a set.</summary>
    public static int WithHistory<T>(int score, T candidate, IReadOnlyList<T> history)
    {
        if(history.Count!=4) throw new ArgumentException("Native history has four slots",nameof(history));
        for(int i=0;i<4;i++)
            if(EqualityComparer<T>.Default.Equals(candidate,history[i])) score/=5-i;
        return score;
    }

    /// <summary>Literal forward-copy writer20C8D8: [a,b,c,d] becomes [new,a,a,a], NOT
    /// a FIFO. Call only at selection with toilet>=99 and selected kind!=2. Identity
    /// must distinguish placed instances even when the port reuses a numeric ride ID.</summary>
    public static void Remember<T>(T selected, T[] history)
    {
        if(history.Length!=4) throw new ArgumentException("Native history has four slots",nameof(history));
        for(int i=1;i<4;i++) history[i]=history[i-1];
        history[0]=selected;
    }

    /// <summary>Native initial best=0. Negative candidates never win; zero can win by
    /// a tie draw, or leave no destination. Enumeration order matters. This does not
    /// replace ties with uniform sampling, retry failed routes, or test affordability.</summary>
    public static (T Candidate,int Score) Choose<T>(IEnumerable<T> candidates,
        Func<T,int> score, Func<int> random) where T:class
    {
        T best=null; int bestScore=0;
        foreach(var candidate in candidates)
        {
            int value=score(candidate);
            if(value>bestScore || (value==bestScore && (unchecked((uint)random())%2)!=0))
            { best=candidate; bestScore=value; }
        }
        return (best,bestScore);
    }
}
