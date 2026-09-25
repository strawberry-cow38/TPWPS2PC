using TPW.PS2.Data;

static class NativeEntranceAcceptanceChecks
{
    public static void Run(Action<bool,string> check)
    {
        void C(bool ok,string why)=>check(ok,"native entrance acceptance: "+why);
        // sum100 -> floor(409600/10000)=40, A50/B60/C30; fee is raw tenths.
        C(NativeEntranceAcceptance.Classify(100,300,0)==1,"C equality remains class1");
        C(NativeEntranceAcceptance.Classify(100,310,0)==0,"above C and below A class0");
        C(NativeEntranceAcceptance.Classify(100,490,0)==0,"below A class0");
        C(NativeEntranceAcceptance.Classify(100,500,0)==-1,"A equality class-1");
        C(NativeEntranceAcceptance.Classify(100,590,0)==-1,"below B still accepted class-1");
        C(NativeEntranceAcceptance.Classify(100,600,0)==-2,"B equality rejects");
        C(NativeEntranceAcceptance.Classify(0,0,0)==-2,"zero sum/free price does not invent a positive rating");
        C(NativeEntranceAcceptance.Classify(100,500,5000)==-2,"randomized denominator affects admission");
        C(NativeEntranceAcceptance.Classify(0x100000,0,0)==-2,"value shift wraps to low32 instead of widening");
        C(NativeEntranceAcceptance.Classify(100,309,0)==1,"fee division truncates before comparison");
        bool bad=false;try{NativeEntranceAcceptance.Classify(100,10,5001);}catch(ArgumentOutOfRangeException){bad=true;}
        C(bad,"RNG maximum is exclusive");
        var needs=new VisitorNeeds(1);needs.Spawn(7);var w=needs.Of(7);w.Cash=500;needs.Set(7,w);
        var finance=new ParkFinances();int balance=finance.Balance;
        int values=0,rolls=0,count=0,quotes=0;
        bool result=NativeEntranceAcceptance.TryCharge(needs,7,finance,()=>{quotes++;return 500;},()=>{values++;return 100;},n=>{rolls++;return 0;},()=>count++);
        C(!result&&quotes==1&&values==0&&rolls==0&&count==0,"cash equality refuses before value/RNG/count");
        C(needs.Of(7).Cash==500&&finance.Balance==balance&&finance.TotalIncome==0,"cash rejection has no money effects");
        w.Cash=1000;needs.Set(7,w);quotes=0;
        result=NativeEntranceAcceptance.TryCharge(needs,7,finance,()=>{quotes++;return 600;},()=>100,_=>0,()=>count++);
        C(!result&&quotes==2&&count==0&&needs.Of(7).Cash==1000,"class rejection rereads class fee but never reads charged fee or increments count");
        var ordering=new List<string>();quotes=0;
        result=NativeEntranceAcceptance.TryCharge(needs,7,finance,
            ()=>{ordering.Add("fee");return ++quotes==1?500:510;},
            ()=>{ordering.Add("value");return 100;},n=>{ordering.Add("rng");C(n==5001,"class RNG range matches consumer");return 0;},
            ()=>{ordering.Add("count");count++;C(finance.Balance==balance&&needs.Of(7).Cash==1000,"admission count precedes credit and debit");});
        C(result&&count==1&&quotes==3,"class-1 accepts with exactly one count and three ordered fee reads");
        C(ordering.SequenceEqual(new[]{"fee","value","rng","fee","count","fee"}),"native cash/class/count/charge ordering");
        C(needs.Of(7).Cash==490&&finance.Balance-balance==510&&finance.TotalIncome==510,"returned reread amount debits and credits equally");
        w.Cash=490;C(needs.Of(7).Equals(w),"numeric needs and thought preserved alongside cash update");
        quotes=0;
        result=NativeEntranceAcceptance.TryCharge(needs,7,finance,()=>++quotes==1?100:600,
            ()=>100,_=>0,()=>count++);
        C(!result&&quotes==2&&count==1,"classification uses its own changed fee, not the cheaper cash quote");
        C(needs.Of(7).Cash==490&&finance.TotalIncome==510,"changed class-fee rejection neither charges nor rolls back earlier admission");
        bool absent=false;try{NativeEntranceAcceptance.TryCharge(needs,99,finance,()=>1,()=>100,_=>0,()=>{});}catch(InvalidOperationException){absent=true;}
        C(absent,"missing cash identity refuses rather than creating a guest");
    }
}
