using TPW.PS2.Data;

/// <summary>Source joins and arithmetic only; not proof that the viewer admits guests.</summary>
static class BusAdmissionChecks
{
    public static void Run(Disc disc,Action<bool,string> check)
    {
        void Check(bool ok,string why)=>check(ok,"native bus admission inputs: "+why);
        var source=disc.Files().Single(f=>f.Path.Equals("/SLES_500.32",StringComparison.OrdinalIgnoreCase));
        var elf=disc.Read(source.Extent,source.Size);
        var expected=new[]{12,12,12,13,10,11,11,12};
        var worlds=new[]{"JUNGLE","HALLOW","FANTASY","SPACE"};
        var dataSource=disc.Files().Single(f=>f.Path.Equals("/DATA/DATA.WAD",StringComparison.OrdinalIgnoreCase));
        var data=new WadArchive(disc.Read(dataSource.Extent,dataSource.Size));
        var db=new AssetResourceDatabase(data.Read(data.Find("/arsdb.dba")));
        for(int w=0;w<4;w++) for(int s=0;s<2;s++)
        {
            var selection=NativeParkSelection.Ordinary($"/DATA/{worlds[w]}.WAD",$"/terrain/terrain_{s+1}.mps");
            var cat=new NativeBusCatalogue(elf,selection);
            string label=$"{worlds[w]}/{s+1}";
            Check(selection==new NativeParkSelection(w,s),label+" native indices use source resources");
            Check(selection.BusStem==$"bus{s+1}",label+" bus variant matches selected terrain");
            Check(cat.TotalEntries==expected[w*2+s],label+" literal scenario denominator");
            Check(cat.DemandOffset==0 && cat.DemandDivisor==81920,label+" initial mutable demand globals");
            var pairs=new List<(int Kind,uint Key)>();
            foreach(int kind in new[]{3,6,7,8,1})
            {
                var keys=cat.Keys(kind);
                for(int i=0;i<keys.Count;i++)
                {
                    Check(db.Find(keys[i]) is {} record && (int)record.Kind==kind,label+$" key{keys[i]} belongs to native family{kind}");
                    Check(cat.TryOrdinal(kind,keys[i],out byte ordinal) && ordinal==i,label+$" key{keys[i]} retains original ordered ordinal{i}");
                    pairs.Add((kind,keys[i]));
                }
            }
            Check(cat.Ceiling(Array.Empty<(int,uint)>())==25,label+" zero represented entries retains floor25");
            Check(cat.Ceiling(pairs)==100,label+" all entries reaches cap100");
            Check(cat.Ceiling(new[]{pairs[0],pairs[0]})==25+75/cat.TotalEntries,label+" duplicate placements count once");
            Check(!cat.TryOrdinal(3,uint.MaxValue,out _),label+" unknown key is unresolved, not ordinal0");
            var ent=ParkEntrance.ReadExecutable(elf).For(w,s);
            Check(cat.Point0==new ParkCell(ent.XStart,ent.ZRow) && cat.Point0!=new ParkCell(ent.XCol,ent.ZEnd-1),label+" spawn is native point0, not inside gate mouth");
        }
        var jungle2=new NativeBusCatalogue(elf,new(0,1));
        Check(jungle2.TryOrdinal(3,216,out var a) && jungle2.TryOrdinal(3,215,out var b) && a==0 && b==1,"unsorted 216/215 control rejects sorted-key indexing");
        Check(new NativeBusCatalogue(elf,new(2,0)).Keys(8).Count==0,"allocated upgrade initializer with zero count stays excluded");
        bool refused=false;try { NativeParkSelection.Ordinary("LOBBY.WAD","terrain_1.mps"); } catch(InvalidDataException) { refused=true; }
        Check(refused,"unknown source world refuses rather than defaults Jungle");
        refused=false;try { new NativeBusCatalogue(elf,new(0,2)); } catch(ArgumentOutOfRangeException) { refused=true; }
        Check(refused,"special Jungle context not silently ordinary terrain1");
        int draws=0;int Draw(int n){draws++;return 0;}
        int Score(params NativeBusDemand.Attraction[] items)=>NativeBusDemand.Score(items,Draw);
        Check(Score()==0 && draws==0,"empty park has zero demand and consumes no object draw");
        Check(Score(new NativeBusDemand.Attraction(2,0,0,0))==12,"feature 10+(20/10), retains base10");
        Check(Score(new NativeBusDemand.Attraction(4,0,0,0))==20,"shop 10+(20/2)");
        Check(Score(new NativeBusDemand.Attraction(3,0,37,0))==30,"new ride adds unconditional20 even at tier0");
        Check(Score(new NativeBusDemand.Attraction(3,0,37,1))==48,"tier multiplies live value, not age");
        Check(Score(new NativeBusDemand.Attraction(5,0,37,0))==38,"sideshow adds value without ride extra20");
        int before=draws;Score(new NativeBusDemand.Attraction(3,0,0,0),new NativeBusDemand.Attraction(2,0,0,0));Check(draws-before==2,"one random draw per enumerated object");
        Check(NativeBusDemand.Batch(20,0,81920,0,25,0)==1,"actual one-shop score yields one passenger at defaults");
        var backlog=NativeBusDemand.Bounds(140,0,81920,15,25,0);
        Check(backlog==new NativeBusDemand.BatchBounds(8,5,25) && backlog.Requested==5,
            "nonempty entrance group binds BELOW20, exposes all three actual bounds");
        Check(NativeBusDemand.Bounds(140,0,81920,0,25,0).Requested==8,
            "zeroed unported entrance input is permissive: eight instead of five, not neutral");
        Check(NativeBusDemand.Batch(10000,0,81920,0,25,24)==1,"headroom bounds the batch");
        Check(NativeBusDemand.Batch(10000,0,81920,19,100,0)==1,"native entrance lists bound the batch");
        Check(NativeBusDemand.Batch(10000,0,81920,20,100,0)==0,"full entrance lists deny batch");
        Check(NativeBusDemand.Batch(10000,0,81920,0,25,26)==-1,"native negative headroom remains negative");
        Check(NativeBusDemand.Batch(0,0,81920,1,25,100,true)==19,"LoadsOfKids overrides score and headroom but not group count");
        Check(NativeBusDemand.Batch(0x40000000,0,81920,0,int.MaxValue,0)==-13107,"native demand product is signed low32, not widened");
    }
}
