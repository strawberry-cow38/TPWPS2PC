using System.Buffers.Binary;
using TPW.PS2.Data;

// Native/raw-template decision fixtures. This is not particle scheduling or a retail image audit.
static class ParticleChildChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string text) => check(ok, "particle child: " + text);
        var archive = disc.Files().Single(f => f.Path.Equals("/DATA/PARTICLE.WAD", StringComparison.OrdinalIgnoreCase));
        var wad = new WadArchive(disc.Read(archive.Extent, archive.Size));
        var raw = wad.Read(wad.Find("/Tp2.plb"));
        var library = new ParticleLibrary(raw);
        int I32(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
        int I16(byte[] bytes, int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset, 2));
        int At(int id, int offset) => 8 + id * 320 + offset;
        Check(I32(raw, 0) == 105 && I32(raw, 4) == 320 && library.Effects.Count == 105,
            "literal shipped particle table has 105 records of 320 bytes");
        for (int id = 0; id < 105; id++)
        {
            int child = I32(raw, At(id, 0xb4));
            bool native = child >= 0 && child < 105 && child != id && raw[At(id, 0xba)] == 0;
            bool attach = I16(raw, At(id, 0xb8)) != 0;
            (int X, int Y, int Z) offset = native && attach
                ? (I32(raw, At(child, 0x2c)), I32(raw, At(child, 0x30)), I32(raw, At(child, 0x34))) : default;
            bool got = ParticleSpawnLinks.TryParticleChild(library, id, out var request);
            Check(got == native && (!got || request == new ParticleSpawnLinks.ChildRequest(child, offset.X, offset.Y, offset.Z)),
                $"raw record {id} initial particle-child predicate and CHILD-owned offset");
            bool supported = id is 75 or 76 or 77;
            bool rendered = ParticleSpawnLinks.TryTwinkleChild(library, id, out var twinkle);
            Check(rendered == supported && (!rendered || twinkle == new ParticleSpawnLinks.ChildRequest(83, 0, 625, 0)),
                $"raw record {id} bounded Twinkle rollout, not generic child enablement");
        }
        foreach (var (parent, child, attach, dies) in new[] {
            (4,5,1,0), (51,54,0,1), (60,63,1,0), (62,63,1,0),
            (75,83,1,0), (76,83,1,0), (77,83,1,0), (78,85,1,0) })
            Check(I32(raw, At(parent, 0xb4)) == child && I16(raw, At(parent, 0xb8)) == attach
                && I16(raw, At(parent, 0xbc)) == dies && raw[At(parent, 0xba)] == 0,
                $"literal native link {parent}->{child}, attachment{attach}/coupling{dies}; coupling not simulated");
        Check(I32(raw, At(83, 0x98)) == 84 && I32(raw, At(83, 0xb4)) == -1,
            "83->84 is PARTICLE DEATH, never an immediate child request");
        Check(!ParticleSpawnLinks.TryTwinkleChild(library, 60, out _) && !ParticleSpawnLinks.TryTwinkleChild(library, 62, out _),
            "LaserRing63 rollout is deferred; no total/live-cap scheduling change");

        ParticleLibrary Fixture(Action<byte[]> edit)
        {
            var bytes = (byte[])raw.Clone(); edit(bytes); return new ParticleLibrary(bytes);
        }
        void W(byte[] bytes, int id, int offset, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(At(id, offset), 4), value);
        var different = Fixture(bytes => {
            W(bytes, 77, 0x2c, 7); W(bytes, 77, 0x30, 11); W(bytes, 77, 0x34, 13);
            W(bytes, 83, 0x2c, 640); W(bytes, 83, 0x30, 1280); W(bytes, 83, 0x34, -1920);
        });
        Check(ParticleSpawnLinks.TryTwinkleChild(different, 77, out var moved)
            && moved == new ParticleSpawnLinks.ChildRequest(83, 640, 1280, -1920),
            "declared offset fixture uses child words, not parent's distinct velocity");
        var noAttach = Fixture(bytes => BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(At(77, 0xb8), 2), 0));
        Check(ParticleSpawnLinks.TryTwinkleChild(noAttach, 77, out var plain)
            && plain == new ParticleSpawnLinks.ChildRequest(83, 0, 0, 0),
            "declared unattached fixture requests child without initial offset");
        foreach (int child in new[] { -1, 77, 500 })
        {
            var fixture = Fixture(bytes => W(bytes, 77, 0xb4, child));
            Check(!ParticleSpawnLinks.TryParticleChild(fixture, 77, out _), $"declared negative/self/missing child {child} is refused");
        }
        var self = Fixture(bytes => W(bytes, 83, 0xb4, 83));
        Check(!ParticleSpawnLinks.TryTwinkleChild(self, 83, out _), "supported-ID self link cannot recurse");
        var attractor = Fixture(bytes => bytes[At(77, 0xba)] = 1);
        Check(!ParticleSpawnLinks.TryTwinkleChild(attractor, 77, out _), "particle83 is not borrowed from attractor namespace");
        var other = Fixture(bytes => W(bytes, 77, 0xb4, 85));
        Check(ParticleSpawnLinks.TryParticleChild(other, 77, out _) && !ParticleSpawnLinks.TryTwinkleChild(other, 77, out _),
            "other valid child is recognized but explicitly outside renderer rollout");
        Check(!ParticleSpawnLinks.TryTwinkleChild(library, -1, out _) && !ParticleSpawnLinks.TryTwinkleChild(library, 500, out _),
            "missing parent is refused");
        foreach (int id in new[] { 97, 99 })
            Check(I32(raw, At(id, 0xb4)) == -1 && !ParticleSpawnLinks.TryTwinkleChild(library, id, out _),
                $"{id} acquires no invented award or research hook from this child path");

        var file = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        var elf = disc.Read(file.Extent, file.Size);
        uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(at, 4));
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(at, 2));
        uint Word(uint address)
        {
            int ph = (int)U32(28);
            for (int i = 0; i < U16(44); i++)
            {
                int p = ph + i * U16(42); uint va = U32(p + 8), size = U32(p + 16);
                if (U32(p) == 1 && address >= va && address + 4 <= va + size)
                    return U32((int)(U32(p + 4) + address - va));
            }
            throw new InvalidDataException("ELF address missing");
        }
        Check(Word(0x18B974) == 0x822200BA && Word(0x18B978) == 0x10400028 && Word(0x18B97C) == 0x862200B8,
            "native pool and attachment branch: BA is attractor namespace, B8 controls initial offset");
        Check(Word(0x18BA3C) == 0x8CA4002C && Word(0x18BA40) == 0x8C620030 && Word(0x18BA44) == 0x8CC50034
            && Word(0x18BA48) == 0x00042100 && Word(0x18BA4C) == 0x00021100 && Word(0x18BA54) == 0x00052900,
            "native child template XYZ words are shifted4 against position10240, hence cells640");
        Check(Word(0x18BA74) == 0x8C6400B4 && Word(0x18BA78) == 0x10970007 && Word(0x18BA88) == 0x0C062D6A,
            "native kind1 child rejects self then calls nondirectional18B5A8");
        Check(Word(0x18B4CC) == 0x8CA4002C && Word(0x18B4D0) == 0x8C620030 && Word(0x18B4D4) == 0x8CC50034
            && Word(0x18B504) == 0x8C6400B4 && Word(0x18B508) == 0x10940007 && Word(0x18B518) == 0x0C062D6A,
            "native directional parent still requests nondirectional particle child, not inherited direction");
    }
}