using System.Buffers.Binary;

namespace TPW.PS2.Data;

/// <summary>Ordinary native park selection, not localized scenario labels or the special
/// Jungle override. 149678/17D7E8/111B28 join world WAD, terrain suffix and PARK(s+1).</summary>
public readonly record struct NativeParkSelection(int World, int Variant)
{
    public string BusStem => Variant switch { 0=>"bus1",1=>"bus2",_=>throw new InvalidOperationException("not an ordinary bus variant") };
    public static NativeParkSelection Ordinary(string wad, string terrain)
    {
        string world = Path.GetFileNameWithoutExtension(wad.Replace('\\','/')).ToUpperInvariant();
        int w = world switch { "JUNGLE"=>0,"HALLOW"=>1,"FANTASY"=>2,"SPACE"=>3,_=>-1 };
        string name = Path.GetFileNameWithoutExtension(terrain.Replace('\\','/')).ToLowerInvariant();
        int s = name switch {"terrain_1"=>0,"terrain_2"=>1,_=>-1};
        if(w<0 || s<0) throw new InvalidDataException("ordinary native park requires an identified world WAD and selected terrain_1/terrain_2");
        return new(w,s);
    }
}

/// <summary>158CF0 startup tables, read from the owner's PAL executable. Lists retain native
/// ordering, not sorted DBA keys or filtered build-menu rows. Normal variants only.</summary>
public sealed class NativeBusCatalogue
{
    readonly Dictionary<int,uint[]> keys = new();
    public NativeParkSelection Selection { get; }
    public ParkCell Point0 { get; }
    public ParkCell StagingPoint { get; }
    public ParkCell IncomingQueuePoint { get; }
    public int DemandOffset { get; }
    public int DemandDivisor { get; }
    public int TotalEntries => keys.Values.Sum(k=>k.Length);
    public IReadOnlyList<uint> Keys(int kind) => keys.TryGetValue(kind,out var k) ? Array.AsReadOnly(k) : Array.Empty<uint>();

    readonly record struct Source(int Kind,uint Keys,uint Count,params uint[] ImmediateKeys);
    // Pointers and count-word SOURCES from the initializer, not copies of the game lists.
    static Source[] Sources(int w,int s) => (w,s) switch
    {
        (0,0)=>new[]{new Source(3,0x2b7658,0x2b7678),new Source(6,0x2b76d0,0x2b76d4),new Source(7,0,0x2b76e0),new Source(8,0,0x2b76d8,0x158d28,0x158d34),new Source(1,0x2b76c8,0x2b76cc)},
        (0,1)=>new[]{new Source(3,0x2b76f0,0x2b770c),new Source(6,0x2b7710,0x2b7714),new Source(7,0x2b76e8,0x2b76ec),new Source(8,0,0x2b7718,0x158d60),new Source(1,0x2b7720,0x2b7728)},
        (1,0)=>new[]{new Source(3,0x2b7860,0x2b787c),new Source(6,0x2b7888,0x2b788c),new Source(7,0x2b7880,0x2b7884),new Source(8,0,0x2b7890,0x1590ac),new Source(1,0x2b7898,0x2b78a0)},
        (1,1)=>new[]{new Source(3,0x2b7920,0x2b793c),new Source(6,0x2b7948,0x2b794c),new Source(7,0,0x2b7944),new Source(8,0,0x2b7950,0x1590e0,0x1590f8),new Source(1,0x2b7958,0x2b7964)},
        (2,0)=>new[]{new Source(3,0x2b79e0,0x2b79fc),new Source(6,0x2b7a08,0x2b7a0c),new Source(7,0,0x2b7a04),new Source(8,0,0x2b7a10,0x15931c),new Source(1,0x2b7a18,0x2b7a20)},
        (2,1)=>new[]{new Source(3,0x2b7aa0,0x2b7ab8),new Source(6,0x2b7ac8,0x2b7acc),new Source(7,0x2b7ac0,0x2b7ac4),new Source(8,0,0x2b7ad0,0x159338,0x15935c),new Source(1,0x2b7ad8,0x2b7adc)},
        (3,0)=>new[]{new Source(3,0x2b7b58,0x2b7b78),new Source(6,0x2b7b88,0x2b7b8c),new Source(7,0,0x2b7b80),new Source(8,0,0x2b7c48,0x159588),new Source(1,0x2b7b90,0x2b7b94)},
        (3,1)=>new[]{new Source(3,0x2b7c10,0x2b7c30),new Source(6,0x2b7c40,0x2b7c44),new Source(7,0x2b7c38,0x2b7c3c),new Source(8,0,0x2b7c50),new Source(1,0x2b7c58,0x2b7c60)},
        _=>throw new ArgumentOutOfRangeException(nameof(s),"not an ordinary native park")
    };

    public static NativeBusCatalogue Read(Disc disc,NativeParkSelection selection)
    {
        var entry=disc.Files().SingleOrDefault(f=>f.Path.Equals("/SLES_500.32",StringComparison.OrdinalIgnoreCase));
        if(entry==null) throw new InvalidDataException("native bus tables require PAL SLES_500.32");
        return new NativeBusCatalogue(disc.Read(entry.Extent,entry.Size),selection);
    }
    public NativeBusCatalogue(byte[] elf,NativeParkSelection selection)
    {
        Selection=selection;
        if(elf.Length<52 || !elf.AsSpan(0,6).SequenceEqual(new byte[]{127,69,76,70,1,1}))
            throw new InvalidDataException("native bus tables require little-endian ELF32");
        ushort U16(int at)=>BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(at,2));
        uint U32(int at)=>BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(at,4));
        var segments=new List<(uint File,uint VA,uint Length)>();
        int ph=checked((int)U32(28));
        for(int i=0;i<U16(44);i++)
        {
            int p=checked(ph+i*U16(42));
            if(U32(p)==1) segments.Add((U32(p+4),U32(p+8),U32(p+16)));
        }
        int Offset(uint address,int bytes)
        {
            foreach(var p in segments)
                if(address>=p.VA && (ulong)address+(uint)bytes<=(ulong)p.VA+p.Length)
                {
                    int o=checked((int)(p.File+address-p.VA));
                    if((long)o+bytes<=elf.Length) return o;
                }
            throw new InvalidDataException($"native bus address{address:x} outside file-backed PT_LOAD");
        }
        uint Word(uint address)=>U32(Offset(address,4));
        foreach(var src in Sources(selection.World,selection.Variant))
        {
            int n=checked((int)Word(src.Count));
            if(n<0 || n>255) throw new InvalidDataException("native category count exceeds byte ordinal");
            var values=new uint[n];
            for(int i=0;i<n;i++)
            {
                if(src.Kind!=8)
                {
                    if(src.Keys==0) throw new InvalidDataException("nonempty native catalog has no key-array source");
                    values[i]=Word(checked(src.Keys+(uint)i*4));
                }
                else
                {
                    if(i>=src.ImmediateKeys.Length) throw new InvalidDataException("upgrade count exceeds traced constructor stores");
                    uint instruction=Word(src.ImmediateKeys[i]);
                    if((instruction>>26)!=9 || ((instruction>>21)&31)!=0 || (instruction&0x8000)!=0)
                        throw new InvalidDataException("upgrade constructor no longer loads a positive ADDIU immediate");
                    values[i]=instruction&0xffff;
                }
            }
            if(values.Distinct().Count()!=n) throw new InvalidDataException("ambiguous native category key identity");
            keys.Add(src.Kind,values);
        }
        if(TotalEntries==0) throw new InvalidDataException("empty native denominator would divide by zero");
        int point=Offset(checked(0x2b71b0u+(uint)selection.World*0x36+(uint)selection.Variant*0x12),6);
        Point0=new ParkCell(elf[point],elf[point+1]); // 14E290(0), not bus position or gate mouth
        StagingPoint=new ParkCell(elf[point+2],elf[point+3]);
        IncomingQueuePoint=new ParkCell(elf[point+4],elf[point+5]);
        DemandOffset=unchecked((int)Word(0x2b9734));DemandDivisor=unchecked((int)Word(0x2b9730));
        if(DemandDivisor==0) throw new InvalidDataException("native demand denominator is zero");
    }
    public bool TryOrdinal(int kind,uint key,out byte ordinal)
    {
        ordinal=0;
        if(!keys.TryGetValue(kind,out var list)) return false;
        int i=Array.IndexOf(list,key);if(i<0) return false;
        ordinal=checked((byte)i);return true;
    }
    public int Ceiling(IEnumerable<(int Kind,uint Key)> placed)
    {
        int represented=placed.Where(p=>TryOrdinal(p.Kind,p.Key,out _)).Distinct().Count();
        return Math.Min(100,25+unchecked(represented*75)/TotalEntries);
    }
}
