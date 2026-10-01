using System.Buffers.Binary;
using TPW.PS2.Data;

// AUDIT-ONLY native-code-derived isolated chronology. Not native execution or hardware observation.
static class ParticleEmissionChecks
{
    internal static sbyte NativeRate(sbyte rate, int density)
    {
        if (rate == 0) return 0;
        int value = unchecked(rate * density) >> 10;
        if (value >= 128) value = 127;
        if (value == 0) value = 1;
        return unchecked((sbyte)value);
    }
    static int Life(int basis, int signedHighWord)
    {
        int divisor = basis >> 2;
        return basis + (divisor == 0 ? 0 : signedHighWord % divisor);
    }
    public static void Run(Disc disc, Action<bool,string> check)
    {
        void Check(bool ok,string why) => check(ok,"particle emission: " + why);
        var file = disc.Files().Single(f => f.Path.Equals("/DATA/PARTICLE.WAD",StringComparison.OrdinalIgnoreCase));
        var wad = new WadArchive(disc.Read(file.Extent,file.Size));
        var raw = wad.Read(wad.Find("/Tp2.plb")); var library = new ParticleLibrary(raw);
        var ring = ParticleTemplate.Of(library[63]);
        int I32(int at) => BinaryPrimitives.ReadInt32LittleEndian(library[63].Raw.AsSpan(at,4));
        int I16(int at) => BinaryPrimitives.ReadInt16LittleEndian(library[63].Raw.AsSpan(at,2));
        Check(library[63].Name == "LaserRing" && I32(0x20) == 20 && I32(0x78) == 20
            && I32(0x60) == 1 && I16(0x64) == 1 && I16(0x66) == 0,
            "raw63: emitter20 / particle-base20 / burst1 / cap1 / countdown0");
        Check(library[63].Raw.AsSpan(0x68,4).ToArray().All(b => unchecked((sbyte)b) == -5)
            && I16(2) == 0 && I16(0x26) == 0 && I16(0x0e) == 0 && library[63].Raw[0xc0] == 0
            && library[63].Raw[0xaa] == 0 && I32(0xb4) == -1 && I16(0x10) == -1 && I32(0x98) == -1,
            "raw63: four -5 rates; admitted unpaused mortal normal-mode isolated case; no linked deaths");
        foreach (var (input, expected) in new[] { (-128,-50), (-64,-25), (-5,-2), (-1,-1), (0,0), (1,1), (2,1), (5,1), (10,3), (127,49) })
            Check(NativeRate((sbyte)input,400) == expected,$"literal density400 signed byte {input}->{expected}");
        Check(ParticleTemplate.Of(library[58]).NoDensityScaling,
            "Bubbles58 explicitly skips native density pass; do not apply63's -2 interval to it");
        var lives = Enumerable.Range(short.MinValue,65536).Select(high => Life(20,high)).Distinct().Order().ToArray();
        Check(lives.SequenceEqual(new[] {16,17,18,19,20,21,22,23,24}),
            "signed RNG-high domain gives particle-life16..24, not fixed20 or one-sided20..24");
        Check(Life(20,-4) == 16 && Life(20,-1) == 19 && Life(20,0) == 20 && Life(20,4) == 24,
            "literal signed-remainder lifetime witnesses");
        Check(Life(3,-32768) == 3 && Life(0,32767) == 0,
            "zero lifetime-quarter divisor does not fabricate an RNG remainder");

        // These literal tables are independent hand-worked endpoints, not copied from Run's branches.
        int[][] replacements = {
            new[] {18,19,20,-1,-1,-1,-1,-1,-1},
            new[] {17,18,19,20,-1,-1,-1,-1,-1},
        };
        for (int alignment = 0; alignment <= 1; alignment++)
        for (int firstLife = 16; firstLife <= 24; firstLife++)
        {
            var trace = IsolatedParticleSchedule.Run(20,false,1,1,0,-2,-2,-2,-2,false,
                birth => birth == 0 ? firstLife : 20,70,alignment);
            int replacement = replacements[alignment][firstLife-16];
            int[] expected = replacement < 0 ? new[] {0} : new[] {0,replacement};
            Check(trace.BirthTicks.SequenceEqual(expected),$"isolated63 alignment{alignment}/life{firstLife} exact births[{string.Join(',',expected)}]");
            Check(trace.Freed && trace.LiveAtEnd == 0 && trace.Receipts.Max(r => r.LiveAfterParticles) == 1,
                $"isolated63 alignment{alignment}/life{firstLife} retires without overlapping births");
            Check(trace.Receipts.Where(r => r.Phase == IsolatedParticleSchedule.Phase.OwnEmitterTick
                && r.EmitterRemaining >= 0 && r.LiveBeforeEmission == 1)
                .All(r => r.Births == 0 && r.Countdown == r.CountdownBeforeEmission),
                $"isolated63 alignment{alignment}/life{firstLife} capped countdown is frozen");
        }
        var zero = IsolatedParticleSchedule.Run(20,false,1,1,0,-2,-2,-2,-2,false,_ => 20,40);
        var at20 = zero.Receipts.Single(r => r.Tick == 20);
        var at21 = zero.Receipts.Single(r => r.Tick == 21);
        Check(at20.EmitterRemaining == 0 && at20.LiveBeforeEmission == 1 && at20.LiveAfterParticles == 1,
            "life20 unaligned: emitter and particle zero endpoints survive, no replacement at20");
        Check(at21.EmitterRemaining == -1 && at21.Births == 0 && at21.Deaths == 1 && zero.OwnTicksExecuted == 22,
            "life20 unaligned: emitter expires BEFORE particle dies at21; free at22");
        var aligned = IsolatedParticleSchedule.Run(20,false,1,1,0,-2,-2,-2,-2,false,_ => 20,40,1);
        Check(aligned.BirthTicks.SequenceEqual(new[] {0}) && aligned.OwnTicksExecuted == 21,
            "life20 pre-aged: particle dies at20 AFTER cap check; expired emitter frees at21");
        var turnover = IsolatedParticleSchedule.Run(20,false,1,1,0,-2,-2,-2,-2,false,_ => 1,40);
        Check(turnover.BirthTicks.SequenceEqual(new[] {0,3,6,9,12,15,18}),
            "declared life1 counterexample: seven lifetime births with a one-live gate, never blanket-clamp total");
        var quarters = IsolatedParticleSchedule.Run(4,false,0,100,0,4,3,2,1,false,_ => 0,10);
        Check(quarters.Receipts.Where(r => r.Phase == IsolatedParticleSchedule.Phase.OwnEmitterTick && r.Tick <= 4)
                .Select(r => r.Births).SequenceEqual(new[] {4,3,2,1}),
            "declared distinct quarters: remaining high usesQ1, zero usesQ4");
        var initialZero = IsolatedParticleSchedule.Run(0,true,0,100,0,3,2,1,0,false,_ => 0,1);
        Check(initialZero.BirthTicks.SequenceEqual(new[] {1,1,1}),"initial-life0 rate getter usesQ1");
        var overshoot = IsolatedParticleSchedule.Run(1,false,0,1,0,3,3,3,3,false,_ => 10,2);
        Check(overshoot.Receipts.Single(r => r.Tick == 1).LiveAfterParticles == 3,
            "declared positive-rate counterexample: native checks cap ONCE, not per birth");
        var burst = IsolatedParticleSchedule.Run(1,false,3,1,0,0,0,0,0,false,_ => 10,1);
        Check(burst.Receipts[0].Births == 3 && burst.Receipts[0].LiveAfterParticles == 3
            && burst.BirthTicks.SequenceEqual(new[] {0,0,0}),
            "declared burst counterexample: MaxLive does not clamp initial burst");
        var wrapped = IsolatedParticleSchedule.Run(1,false,0,1,short.MinValue,-2,-2,-2,-2,false,_ => 10,3);
        Check(wrapped.BirthTicks.Count == 0 && wrapped.Countdown == short.MaxValue,
            "declared countdown wraps as signed16 before positivity check");
        Console.WriteLine($"  particle emission comparison (not hardware): managed DensityScaled(-5,400)={ParticleTemplate.DensityScaled(-5,400,true)}, native-byte=-2; managed ExpectedTotal63={ring.ExpectedTotal()}, isolated admitted native-code-derived bounds=1..2; renderer unchanged");
        NativeWords(disc,Check);
    }
    static void NativeWords(Disc disc,Action<bool,string> Check)
    {
        var file=disc.Files().Single(f=>f.Path.Equals("/SLES_500.32",StringComparison.OrdinalIgnoreCase));var elf=disc.Read(file.Extent,file.Size);
        uint U32(int at)=>BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(at,4));
        int U16(int at)=>BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(at,2));
        uint Word(uint address)
        {
            int ph=(int)U32(28);for(int i=0;i<U16(44);i++){int p=ph+i*U16(42);uint va=U32(p+8),size=U32(p+16);if(U32(p)==1&&address>=va&&address+4<=va+size)return U32((int)(U32(p+4)+address-va));}
            throw new InvalidDataException("missing ELF address");
        }
        Check(Word(0x18AFC4)==0x0C0624FE && Word(0x18AFCC)==0x0C062652 && Word(0x18AFD4)==0x0C06276A,
            "native master order: emitter, attractor, particle; no applicable destructive attractor assumed");
        Check(Word(0x18B004)==0x80C30068 && Word(0x18B008)==0x10600009 && Word(0x18B01C)==0x00031A83 && Word(0x18B02C)==0xA0C30068,
            "native rate scaler skips ZERO only, uses signed-byte load and arithmetic shift, including negative rates");
        Check(Word(0x18B718)==0x822200C0 && Word(0x18B720)==0x14400003 && Word(0x18B728)==0x0C062BFC,
            "native NoDensity flag bypasses scaler;63 does not bypass");
        Check(Word(0x18893C)==0x8E030078 && Word(0x188940)==0x00032883 && Word(0x188968)==0x00041403
            && Word(0x188970)==0x0045001A && Word(0x18897C)==0x00001810,
            "native particle life uses signed RNG remainder of baseLife>>2, no 7FFF mask");
        Check(Word(0x189704)==0x86030064 && Word(0x189708)==0x96020008 && Word(0x18970C)==0x0043102A
            && Word(0x189748)==0x2442FFFF && Word(0x189750)==0x1C600005 && Word(0x189754)==0xA6020066,
            "native cap gate precedes countdown; zero/negative countdown emits, short store in delay slot");
        Check(Word(0x18949C)==0x8E020020 && Word(0x1894A0)==0x2442FFFF && Word(0x1894D4)==0x04430039,
            "native emitter decrements before testing; zero is eligible and negative expires");
        Check(Word(0x189EC4)==0x2463FFFF && Word(0x189EC8)==0xAE030020 && Word(0x189ECC)==0x04610015,
            "native particle decrements before testing; zero survives, negative removes");
        Check(Word(0x1895A0)==0x96020008 && Word(0x1895A4)==0x54400071 && Word(0x1895AC)==0x0C0621E8,
            "native expired emitter frees only when its live list is already empty");
        Check(Word(0x18846C)==0x8082006B && Word(0x18847C)==0x8082006A && Word(0x18848C)==0x80820069
            && Word(0x188494)==0x80820068 && Word(0x18849C)==0x80820068,
            "native quarter getter reads Q4 at phase0, Q3 at1, Q2 at2, Q1 otherwise or initial0");
        Check(Word(0x18947C)==0x860300D0 && Word(0x189480)==0xAFA30000,
            "native emitter traversal caches next before spawn; creation-phase alignment matters");
        Check(Word(0x18B948)==0x2450FFFF && Word(0x18B958)==0x0C06222A && Word(0x18B95C)==0x2610FFFF && Word(0x18B960)==0x1612FFFD,
            "native Burst1 makes exactly one immediate allocator call despite delay-slot decrement");
    }
}