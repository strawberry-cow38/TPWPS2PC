using System.Buffers.Binary;
using TPW.PS2.Data;

// Explicit raw-disc/model/native-ELF audit. Not a retail framebuffer or proof of script reachability.
static class ParticleFittingChecks
{
    sealed record Case(string World,string Script,int Pc,RseOpcode Op,int Node,int Effect,uint[] Flags,bool Match);
    static readonly Case[] Cases={
        new("JUNGLE","/Rides/Bumper/bumper.RSE",151,RseOpcode.ADDOBJ,6,16,new uint[]{},false),
        new("JUNGLE","/Sideshow/pong/pong.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411},false),
        new("JUNGLE","/Sideshow/sgpuzzle/sgpuzzle.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411,0x00811},false),
        new("JUNGLE","/Sideshow/sgwhack/sgwhack.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411},false),
        new("HALLOW","/Sideshow/pong/pong.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411},false),
        new("HALLOW","/Sideshow/sgpuzzle/sgpuzzle.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411,0x00811},false),
        new("HALLOW","/Sideshow/sgshy/Sgshy.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411},false),
        new("HALLOW","/Sideshow/sgstrtst/sgstrtst.RSE",32,RseOpcode.EVENT,1,74,new uint[]{},false),
        new("HALLOW","/Sideshow/sgwhack/sgwhack.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411},false),
        new("SPACE","/Sideshow/pong/pong.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411},false),
        new("SPACE","/Sideshow/sgpuzzle/sgpuzzle.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411,0x00811},false),
        new("SPACE","/Sideshow/sgwhack/sgwhack.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411},false),
        new("FANTASY","/Rides/b_drip/B_DRIP.RSE",69,RseOpcode.ADDOBJ,1,16,new uint[]{0x00431,0x1400031,0x00811},false),
        new("FANTASY","/Rides/candy_c/Candy_c.RSE",69,RseOpcode.ADDOBJ,1,16,new uint[]{0x1400031,0x00431,0x00811},false),
        new("FANTASY","/Sideshow/pong/pong.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411},false),
        new("FANTASY","/Sideshow/sgpuzzle/sgpuzzle.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411,0x00811},false),
        new("FANTASY","/Sideshow/sgshy/sgshy.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411},false),
        new("FANTASY","/Sideshow/sgstrtst/sgstrtst.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411,0x00811},false),
        new("FANTASY","/Sideshow/sgwhack/sgwhack.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x00411},false),
        new("SPACE","/Sideshow/sgstrtst/sgstrtst.RSE",32,RseOpcode.EVENT,1,74,new uint[]{0x811,0x111},true),
    };
    public static void Run(Disc disc,Action<bool,string> check)
    {
        void Check(bool ok,string why)=>check(ok,"particle fitting: "+why);
        var archives=new Dictionary<string,WadArchive>();
        WadArchive Wad(string name)
        {
            if(archives.TryGetValue(name,out var cached)) return cached;
            var f=disc.Files().Single(e=>e.Path.Equals($"/DATA/{name}.WAD",StringComparison.OrdinalIgnoreCase));
            return archives[name]=new WadArchive(disc.Read(f.Extent,f.Size));
        }
        int mask=0,missing=0,smoke=0,accepted=0;
        foreach(var c in Cases)
        {
            var wad=Wad(c.World);var scripts=Wad(c.World switch{"JUNGLE"=>"JRSE","HALLOW"=>"HRSE","FANTASY"=>"FRSE",_=>"SRSE"});
            var me=wad.Find(Path.ChangeExtension(c.Script,".mps"));
            var se=scripts.Find(c.Script)??wad.Find(c.Script);
            Check(me!=null && se!=null,$"{c.World}:{c.Script} exact model/script paths exist");
            if(me==null || se==null)continue;
            var raw=wad.Read(me);var model=new Model(raw);
            var program=new RseProgram((scripts.Find(c.Script)!=null?scripts:wad).Read(se));
            var ins=program.At(c.Pc);
            Check(ins.Opcode==c.Op && ins.Operands.Count>=3 && ins.Operands.Take(3).All(o=>o.Tag==0)
                && ins.Operands[0].Immediate==(c.Op==RseOpcode.EVENT?1:2) && ins.Operands[1].Immediate==c.Node && ins.Operands[2].Immediate==c.Effect,
                $"{c.World}:{c.Script} word{c.Pc} literal {c.Op} particle request node{c.Node}/effect{c.Effect}");
            int count=BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(0x36,2));
            int table=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x74,4)));
            uint U32(int off)=>BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(off,4));
            var flags=new List<uint>();int nativeIndex=-1;
            for(int i=0;i<count;i++)
            {
                int at=table+20*i;
                if(U32(at+4)!=(uint)c.Node)continue;
                flags.Add(U32(at));
                if(nativeIndex<0 && (U32(at)&0x100)!=0)nativeIndex=i;
            }
            Check(flags.SequenceEqual(c.Flags),$"{c.World}:{c.Script} ALL same-ID raw flags match independent census literals");
            Check((nativeIndex>=0)==c.Match,$"{c.World}:{c.Script} literal raw native100 predicate {(c.Match?"ACCEPTS":"REJECTS")}");
            var port=model.FindFitting(c.Node,0x100);
            Check((port!=null)==c.Match,$"{c.World}:{c.Script} port lookup agrees, no fallback mask widening");
            if(nativeIndex>=0) Check(port==model.Fittings[nativeIndex],$"{c.World}:{c.Script} first accepted ordinal, not first same-ID record");
            if(c.Match)accepted++;else if(flags.Count==0)missing++;else if(c.Effect==74)mask++;else smoke++;
        }
        Check(mask==15 && smoke==2 && missing==2 && accepted==1,"exact population:15 SideShowWin masks +2 Smoke2 masks +2 missing IDs; SPACE strength positive control1");
        Check((0x411u&0x100)==0 && (0x431u&0x100)==0 && (0x1400031u&0x100)==0 && (0x811u&0x100)==0
            && (0x111u&0x100)!=0,"literal flag classes distinguish particle space from mere node existence");
        Executable(disc,Check);
    }
    static void Executable(Disc disc,Action<bool,string> Check)
    {
        var f=disc.Files().Single(e=>e.Path.Equals("/SLES_500.32",StringComparison.OrdinalIgnoreCase));var elf=disc.Read(f.Extent,f.Size);
        uint U32(int off)=>BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off,4));
        int U16(int off)=>BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(off,2));
        uint Word(uint addr){int ph=(int)U32(28);for(int i=0;i<U16(44);i++){int p=ph+i*U16(42);uint va=U32(p+8),size=U32(p+16);if(U32(p)==1 && addr>=va && addr+4<=va+size)return U32((int)(U32(p+4)+addr-va));}throw new InvalidDataException("missing ELF address");}
        Check(Word(0x1BBF8C)==0x0C06E4E2 && Word(0x1BBF90)==0x24080100 && Word(0x1BC01C)==0x0C06E4E2 && Word(0x1BC020)==0x24080100,
            "native kind1/kind2 both call1B9388 with space100 in delay slot");
        Check(Word(0x1B93AC)==0x00E0302D && Word(0x1B93B4)==0x0100282D && Word(0x1B93D0)==0x0C07C7DE,
            "native resolver forwards node ID and supplied space to1F1F78");
        Check(Word(0x1F1F7C)==0x3C0303DA && Word(0x1F1F80)==0x34631F83 && Word(0x1F1F8C)==0x00A32024 && Word(0x1F1F90)==0x34E71F82 && Word(0x1F1F94)==0x00A4380B,
            "native fallback3DA1F82 only when space shares no bit with3DA1F83;100 is NOT widened");
        Check(Word(0x1F1F78)==0x8C820008 && Word(0x1F1F88)==0x8C420004
            && Word(0x1F1F98)==0x94480036 && Word(0x1F1FA8)==0x8C4A0074 && Word(0x1F1FAC)==0x24090014
            && Word(0x1F1FBC)==0x8C620004 && Word(0x1F1FC0)==0x14460007
            && Word(0x1F1FC8)==0x8C620000 && Word(0x1F1FCC)==0x00471024 && Word(0x1F1FD0)==0x50400004
            && Word(0x169A78)==0x03E00008 && Word(0x169A7C)==0
            && Word(0x169BF8)==0x96500036 && Word(0x169C00)==0x8E510074
            && Word(0x169C0C)==0x0C05A69E && Word(0x169C18)==0x0C05A602 && Word(0x169C24)==0x26310014,
            "native searches raw file20-byte table: count36/table74/ID+4/flags+0 AND mask");
        Check(Word(0x1F1FF0)==0x3C02FFFF && Word(0x1F1FF4)==0x03E00008 && Word(0x1F1FF8)==0x3442FFFF
            && Word(0x1B93E0)==0x1043001F && Word(0x1B9464)==0x0000102D
            && Word(0x1BBF94)==0x104002D4 && Word(0x1BC024)==0x104002B0,
            "native failed fitting returns0 and skips both particle spawn paths, no alternate-space retry");
        Check(Word(0x1BCB44)==0x0C06EFCA && Word(0x1BCBA0)==0x24050100 && Word(0x1BCBBC)==0x24050100,
            "native ADDOBJ object wrapper also dispatches and attaches particle kinds in100");
    }
}
