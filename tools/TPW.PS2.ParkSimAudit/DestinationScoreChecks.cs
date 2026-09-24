using TPW.PS2.Data;

static class DestinationScoreChecks
{
    public static void Run(Disc disc, Action<bool,string> check)
    {
        void Check(bool ok,string name)=>check(ok,"native destination score: "+name);
        var executable=disc.Files().Single(f=>f.Path.Equals("/SLES_500.32",StringComparison.OrdinalIgnoreCase));
        var elf=disc.Read(executable.Extent,executable.Size); // memory only; no extracted asset
        int Word(uint address)
        {
            uint U32(int p)=>System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(p,4));
            ushort U16(int p)=>System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(p,2));
            for(int i=0;i<U16(44);i++)
            {
                int ph=checked((int)U32(28)+i*U16(42));
                if(U32(ph)==1 && address>=U32(ph+8) && (ulong)address+4<=(ulong)U32(ph+8)+U32(ph+16))
                    return unchecked((int)U32(checked((int)(U32(ph+4)+address-U32(ph+8)))));
            }
            throw new InvalidDataException($"Scorer table address {address:x} outside ELF load segments");
        }
        bool needTable=true,reliefTable=true;
        for(int row=0;row<11;row++) for(int column=0;column<11;column++)
            needTable &= GuestDestinationScore.Need(column*10,row*10)==Word((uint)(0x36ce88+44*row+4*column));
        for(int need=0;need<=100;need++)
            reliefTable &= GuestDestinationScore.Relief(need,true)==Word((uint)(0x36d070+4*((need+5)/5)));
        Check(needTable,"all121 literal need entries agree with owner's executable PT_LOAD data");
        Check(reliefTable,"every legal relief input agrees with executable table, including negative words");
        var fresh=new GuestDestinationScore.Wants(0,0,0,0,80,50);
        var here=new ParkCell(20,20);
        var loo=new GuestDestinationScore.Candidate(here,2,0,0,0,true,true);
        int Score(GuestDestinationScore.Wants w,GuestDestinationScore.Candidate c,ParkCell? at=null)
            =>GuestDestinationScore.Evaluate(w,c,at??here,64,64);
        Check(Score(fresh,loo)==-1,"fresh guest's nearest toilet scores negative, not merely below an invented urgency bar");
        Check(Score(fresh with {Toilet=90},loo)==35,"need90 relief can win below91: no urgency filter in native score");
        Check(Score(fresh with {Sickness=90},loo)==16,"sick guest can need relief with empty bladder");
        Check(Score(fresh with {Toilet=100},loo with {ReliefAvailable=false})==-1,"unavailable feature rejects despite maximal need");
        Check(Score(fresh,loo with {Relief=false})==-1,"non-relief feature rejects instead of gaining proximity points");
        Check(GuestDestinationScore.Relief(34,true)==-20 && GuestDestinationScore.Relief(35,true)==0,
            "relief34/35 pins negative-to-zero boundary");
        Check(GuestDestinationScore.Relief(40,true)==1 && GuestDestinationScore.Relief(90,true)==100,
            "relief40/90 pins lookup bins rather than interpolation");
        Check(GuestDestinationScore.Relief(100,false)==0,"disabled relief is zero even for a desperate guest");
        foreach(var (need,input,want) in new[]{(99,5,0),(99,10,25),(99,25,36),(100,25,44),(20,90,3)})
            Check(GuestDestinationScore.Need(need,input)==want,$"need{need}/input{input} exact table word {want}");
        var shop=new GuestDestinationScore.Candidate(here,4,0,25,5,false,true,0);
        var hungry=fresh with {Hunger=90,Thirst=90};
        Check(Score(hungry,shop,new ParkCell(15,17))==15,"compiled shop inputs and Manhattan8 give94 plus108 over13");
        Check(Score(fresh,shop)==7,"inactive need callbacks do not remove denominator weights");
        Check(Score(fresh,shop with {Entry=new ParkCell(63,20)})==-1,"last map column is excluded by native upper bound");
        Check(Score(fresh,shop with {Entry=new ParkCell(62,20)})>=0,"penultimate column is valid control");
        Check(Score(fresh,shop with {Entry=new ParkCell(65556,20)})==7,"entry sum narrows to signed16 before bounds test");
        foreach(var (product,happy,want) in new[]{(2,50,7),(2,51,6),(2,100,25),(3,51,6),
                                                 (6,55,7),(6,56,6),(6,75,14),(7,100,7)})
            Check(Score(fresh with {Happiness=happy},shop with {HungerEffect=0,ThirstEffect=0,Product=product})==want,
                  $"product{product} happy{happy} literal score{want}");
        var ride=new GuestDestinationScore.Candidate(here,3,50,0,0,false,true);
        Check(Score(fresh with {PreferredIntensity=30},ride)==11 &&
              Score(fresh with {PreferredIntensity=70},ride)==11,"taste mismatch uses absolute distance on both sides");
        Check(Score(fresh with {PreferredIntensity=0},ride)==7 &&
              Score(fresh with {PreferredIntensity=101},ride)==7,"mismatch50 and51 both saturate");
        Check(Score(fresh,ride with {Value=0})==7,"zero value disables taste denominator too");
        Check(Score(fresh with {PreferredIntensity=0},ride with {Value=-1})==14,
              "negative nonzero value still enables taste term");
        var a=new object(); var b=new object(); var c=new object();
        var history=new object[4];
        GuestDestinationScore.Remember(a,history);
        Check(history.SequenceEqual(new object[]{a,null,null,null}),"first selection replaces only sentinel history");
        GuestDestinationScore.Remember(b,history);
        Check(history.SequenceEqual(new[]{b,a,a,a}),"forward propagation is not a plausible FIFO rewrite");
        GuestDestinationScore.Remember(c,history); GuestDestinationScore.Remember(c,history);
        Check(history.All(x=>ReferenceEquals(x,c)),"repeated selection retains four matching entries");
        Check(GuestDestinationScore.WithHistory(97,c,history)==0,"all four matches divide97 sequentially to0");
        Check(GuestDestinationScore.WithHistory(-7,a,new[]{b,a,b,b})==-1,"history signed division truncates instead of flooring");
        Check(GuestDestinationScore.WithHistory(97,new object(),history)==97,"replacement instance does not inherit old identity's penalty");
        int draws=0;
        var negative=GuestDestinationScore.Choose(new[]{a,b},_=>-1,()=>{draws++;return 1;});
        Check(negative.Candidate==null && negative.Score==0 && draws==0,"all negative means no destination and no tie RNG");
        var zeroNo=GuestDestinationScore.Choose(new[]{a},_=>0,()=>0);
        var zeroYes=GuestDestinationScore.Choose(new[]{a},_=>0,()=>1);
        Check(zeroNo.Candidate==null && zeroYes.Candidate==a,"zero tie may select or leave no target");
        draws=0;
        var tie=GuestDestinationScore.Choose(new[]{a,b,c},_=>10,()=>{draws++;return 1;});
        Check(tie.Candidate==c && draws==2,"sequential tied candidates each replace on nonzero draw");
        var higher=GuestDestinationScore.Choose(new[]{a,b},x=>x==a?9:10,()=>throw new Exception("unexpected tie draw"));
        Check(higher.Candidate==b && higher.Score==10,"strictly better candidate wins without random consumption");
        bool threw=false;
        try { GuestDestinationScore.Need(-1,10); } catch(ArgumentOutOfRangeException) { threw=true; }
        Check(threw,"unsupported malformed lookup refuses rather than clamping");
    }
}
