using System.Buffers.Binary;

namespace TPW.PS2.Data;

/// <summary>The first operand of a sound `EVENT`/`ADDOBJ`. The values are the `OBJ_SOUND_*`
/// constants the shipped `.rss` sources spell out, recovered by aligning every source statement
/// with its `.rse` instruction across all 351 pairs (1 and 2 are the particle kinds,
/// <see cref="ParticleLibrary"/>). 10 carries no name in any source and is not listed.
///
/// ⭐ EACH GROUP IS ONE `*SFX.MAP`, and the data proves the assignment: every one of the 31 distinct
/// `GLO_RID` ids on the disc is an event of `GLOBAL/RIDESFX.MAP`, all 22 `GLO_KID` ids are in
/// `KIDSSFX.MAP`, all 8 `GLO_BMP` ids in `RIDES/BUMPSFX.MAP`, and 461 of 467 `LOC_RID` cues in the
/// world's own park ride map -- while the same ids hit the other maps at chance. ⚠ `LOC_AMB`,
/// `GLO_AMB` and `GLO_UI` mostly do NOT resolve on this disc (gates, seaplane, "park closed"):
/// their ids are in no shipped map. <see cref="SoundCatalogue.Resolve"/> returns null for those
/// rather than a neighbour's clip, and the audit prints them.</summary>
public enum SoundGroup
{
    LocalRide = 3, LocalAmbient = 4, GlobalRide = 5, GlobalKids = 6, GlobalStaff = 7,
    GlobalAmbient = 8, GlobalUi = 9, GlobalBumper = 11,
}

/// <summary>A `*SFX.MAP` -- the event-to-clip index beside a `.SDT` bank. Record sizes and the
/// depth-first layout come from the loader (`0x249d38` → `0x24b770` → `0x24b810` → `0x24b8d0` →
/// `0x24a030`, findings/formats.md); the meaning of the fields below comes from the data:
///
/// ⭐⭐ AN EVENT IS KEYED BY THE u16 AT THE START OF ITS L2 RECORD, and that key is the script's
/// third operand. `EVT_RIDE_APE` is 8 in the jungle scripts and `JUNGLE/PARK1/RIDESFX.MAP`'s first
/// event is id 8 → `apeoooooC.vag`; the haunted toilet's `EVT_BOG3`, commented `; PISS` in its own
/// source, is 50 and `KIDSSFX.MAP` event 50 → `wee1.vag`. Nobody chose those; the files agree.
///
/// Every 16-byte clip entry: `+0 u32` sound index (1-based into the bank), `+4 u16` selection
/// threshold, `+8 u32` milliseconds (equal to the bank header's own length on 939 of 939), `+0xC
/// u16` bank (1-based into the sibling `*BANK.MAP`'s list -- the jungle ride map's bank 2 is
/// `Sound1\Amb`, so a ride event can play from the ambient bank).
///
/// ⚠ THE THRESHOLD IS READ FROM ITS SHAPE, NOT FROM A CONSUMER: a set of four carries `0x3fff,
/// 0x7ffe, 0xbffd, 0xfffc`, a set of three `0x5555, 0xaaaa, 0xffff`, and `apegrunt6/7 + blank` is
/// `0x4ccc, 0x9998, 0xfffe` -- monotone, cumulative, ending at 0xffff, so a variant is chosen by a
/// random u16 against the running total. Treat the exact draw as unverified.</summary>
public sealed class SfxMap
{
    public sealed record Clip(int Sound, int Threshold, int Milliseconds, int Bank);
    /// <summary>An 8-byte link: another set of the same event (1-based), taken when the engine's
    /// scalar is inside `Low..High`. Only the `/AUDIO/RIDES/` maps carry these -- `GRCSFX.MAP`
    /// chains 32 go-kart engine states by a 0..100 speed band -- and no script ever reaches
    /// them; they are kept for the ride-engine layer.</summary>
    public sealed record Link(int Target, int Low, int High);
    public sealed class Set
    {
        public List<Clip> Clips { get; } = new();
        public List<Link> Links { get; } = new();
        /// <summary>The 26 bytes of the 42-byte record between the counts and the pointers. Two
        /// byte pairs read as percentages (`64 64`, `55 55`, `3f 3f` and `32 32`) and the loader
        /// adjusts `+0x1e` in place; nothing here has been walked to a consumer.</summary>
        public byte[] Raw { get; init; } = Array.Empty<byte>();
    }
    public sealed class Event
    {
        public int Id { get; init; }
        public ushort Flags { get; init; }
        /// <summary>`+0xC` and `+0x12` of the L2 record: 3300/4600/5700 and small counts, shape
        /// only -- carried, not interpreted.</summary>
        public ushort Word0C { get; init; }
        public ushort Word12 { get; init; }
        public List<Set> Sets { get; } = new();
    }

    static readonly byte[] SfxGuid = Convert.FromHexString("002c61e9d031d211b40900b0c993f203");
    static readonly byte[] BankGuid = Convert.FromHexString("012c61e9d031d211b40900a0c993f203");

    public IReadOnlyList<Event> Events { get; }
    public IReadOnlyDictionary<int, Event> ById { get; }
    public ushort RootFlags { get; }

    public SfxMap(byte[] d)
    {
        if (d == null || d.Length < 0x1C + 24 || !d.AsSpan(0, 16).SequenceEqual(SfxGuid))
            throw new InvalidDataException("not a SFX.MAP: type GUID mismatch");
        int roots = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x18));
        // ⚠ Every map on the disc has exactly one root; a second would shift every offset below
        // and the loader's depth-first walk would still read it, so refuse rather than misread.
        if (roots != 1) throw new InvalidDataException($"SFX.MAP declares {roots} roots; only one is understood");
        int q = 0x1C;
        int events = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(q));
        RootFlags = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(q + 8));
        q += 24;
        var l2 = new List<(Event Event, int Sets)>(events);
        for (int j = 0; j < events; j++, q += 20)
        {
            l2.Add((new Event
            {
                Id = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(q)),
                Word0C = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(q + 0xC)),
                Flags = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(q + 0x10)),
                Word12 = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(q + 0x12)),
            }, (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(q + 4))));
        }
        foreach (var (ev, sets) in l2)
        {
            var l3 = new List<(Set Set, int Clips, int Links)>(sets);
            for (int k = 0; k < sets; k++, q += 42)
                l3.Add((new Set { Raw = d[(q + 0xC)..(q + 0x26)] },
                        BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(q)),
                        (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(q + 4))));
            foreach (var (set, clips, links) in l3)
            {
                for (int m = 0; m < clips; m++, q += 16)
                    set.Clips.Add(new Clip((int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(q)),
                                           BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(q + 4)),
                                           (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(q + 8)),
                                           BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(q + 12))));
                for (int m = 0; m < links; m++, q += 8)
                    set.Links.Add(new Link((int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(q)), d[q + 6], d[q + 7]));
                ev.Sets.Add(set);
            }
        }
        // ⭐ Exact consumption is the check: 42 of 42 maps on the disc end precisely where the
        // walk does, so a map that does not was read with the wrong record sizes.
        if (q != d.Length) throw new InvalidDataException($"SFX.MAP walk consumed {q} of {d.Length} bytes");
        Events = l2.Select(x => x.Event).ToList();
        var byId = new Dictionary<int, Event>();
        foreach (var e in Events) byId.TryAdd(e.Id, e);   // a duplicated id keeps its first record, as a linear search would
        ById = byId;
    }

    /// <summary>The bank list of a `*BANK.MAP`: header, `count` 11-byte records the loader
    /// (`0x249758`) keeps verbatim, then `count` length-prefixed names. A name is the folder the
    /// bank was built from -- `Sound1\Ride`, `sound\Coast` -- and its last component names the
    /// file: `RIDEHD.SDT`, `COASTHD.SDT`, beside the map. ⚠ It used to be read as a single
    /// trailing string, which is right for 36 of the 42 and silently drops the second and third
    /// banks of the six that have them.</summary>
    public static IReadOnlyList<string> BankNames(byte[] d)
    {
        if (d == null || d.Length < 0x1C || !d.AsSpan(0, 16).SequenceEqual(BankGuid))
            throw new InvalidDataException("not a BANK.MAP: type GUID mismatch");
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x18));
        int q = 0x1C + count * 11;
        var names = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            int len = (int)BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(q));
            if (len < 0 || q + 4 + len > d.Length) throw new InvalidDataException("BANK.MAP name runs off the file");
            names.Add(System.Text.Encoding.Latin1.GetString(d, q + 4, len).TrimEnd('\0'));
            q += 4 + len;
        }
        if (q != d.Length) throw new InvalidDataException($"BANK.MAP walk consumed {q} of {d.Length} bytes");
        return names;
    }
}

/// <summary>What a script's sound cue means on this disc: `(group, id)` → the map it lives in →
/// its clips by name. One instance per world and park, because a world's `LOC_*` groups are the
/// maps under `/AUDIO/{WORLD}/PARK{n}/` and the two parks' ride maps differ (jungle: 70 events
/// against 54).
///
/// ⚠ THIS IS THE SCRIPT-DRIVEN HALF ONLY. The track rides' `EventMap.rse` sound child is a table
/// of five event ids and five parameter ids that the ride ENGINE reads, and those ids are in no
/// shipped map by equality -- see findings. Nothing here resolves them, on purpose.</summary>
public sealed class SoundCatalogue
{
    public sealed record ResolvedClip(int Set, string Bank, int Index, string Name, int Milliseconds, int Threshold);
    public sealed record Resolved(SoundGroup Group, int Id, string Map, IReadOnlyList<ResolvedClip> Clips, int Sets);

    readonly Disc _disc;
    readonly Dictionary<string, Disc.Entry> _files;
    readonly Dictionary<string, (SfxMap Map, IReadOnlyList<string> Banks)> _maps = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, SoundBank> _banks = new(StringComparer.OrdinalIgnoreCase);
    public string World { get; }
    public int Park { get; }

    public SoundCatalogue(Disc disc, string world, int park = 1)
    {
        _disc = disc; World = world.ToUpperInvariant(); Park = park;
        _files = new Dictionary<string, Disc.Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in disc.Files()) if (!f.IsDirectory) _files[f.Path] = f;
    }

    public static string MapFor(SoundGroup group, string world, int park) => group switch
    {
        SoundGroup.LocalRide => $"/AUDIO/{world}/PARK{park}/RIDESFX.MAP",
        SoundGroup.LocalAmbient => $"/AUDIO/{world}/PARK{park}/AMBSFX.MAP",
        SoundGroup.GlobalRide => "/AUDIO/GLOBAL/RIDESFX.MAP",
        SoundGroup.GlobalKids => "/AUDIO/GLOBAL/KIDSSFX.MAP",
        SoundGroup.GlobalStaff => "/AUDIO/GLOBAL/STAFSFX.MAP",
        SoundGroup.GlobalAmbient => "/AUDIO/GLOBAL/AMBSFX.MAP",
        SoundGroup.GlobalUi => "/AUDIO/GLOBAL/UISFX.MAP",
        SoundGroup.GlobalBumper => "/AUDIO/RIDES/BUMPSFX.MAP",
        _ => null,
    };

    public static bool IsSoundGroup(int kind) => Enum.IsDefined(typeof(SoundGroup), kind);

    byte[] Read(string path) => _files.TryGetValue(path, out var e) ? _disc.Read(e.Extent, e.Size) : null;

    (SfxMap Map, IReadOnlyList<string> Banks) MapAt(string path)
    {
        if (_maps.TryGetValue(path, out var m)) return m;
        var bytes = Read(path);
        var bank = Read(path[..^7] + "BANK.MAP");
        m = bytes == null ? (null, null) : (new SfxMap(bytes), bank == null ? Array.Empty<string>() : SfxMap.BankNames(bank));
        _maps[path] = m;
        return m;
    }

    /// <summary>The `.SDT` a bank name denotes: `<last folder>HD.SDT` in the map's own directory.
    /// The loader (`0x249758`) finds an already-loaded bank by this NAME, which is how the global
    /// ambient map's bank 2, `Sound\Ride`, is the same `RIDEHD.SDT` the ride map opens.</summary>
    SoundBank BankFor(string mapPath, string bankName)
    {
        string last = bankName.Replace('\\', '/').Split('/')[^1];
        string dir = mapPath[..(mapPath.LastIndexOf('/') + 1)];
        string path = dir + last.ToUpperInvariant() + "HD.SDT";
        if (_banks.TryGetValue(path, out var b)) return b;
        var bytes = Read(path);
        b = bytes == null ? null : new SoundBank(bytes);
        _banks[path] = b;
        return b;
    }

    /// <summary>Null when the group is not a sound group, the map is not on the disc, or the map
    /// has no event with that id -- which the caller must show, not swallow.</summary>
    public Resolved Resolve(int kind, int id) => IsSoundGroup(kind) ? Resolve((SoundGroup)kind, id) : null;

    public Resolved Resolve(SoundGroup group, int id)
    {
        string path = MapFor(group, World, Park);
        if (path == null) return null;
        var (map, banks) = MapAt(path);
        if (map == null || !map.ById.TryGetValue(id, out var ev)) return null;
        var clips = new List<ResolvedClip>();
        for (int s = 0; s < ev.Sets.Count; s++)
            foreach (var c in ev.Sets[s].Clips)
            {
                string bankName = c.Bank >= 1 && c.Bank <= banks.Count ? banks[c.Bank - 1] : null;
                var bank = bankName == null ? null : BankFor(path, bankName);
                var sound = bank != null && c.Sound >= 1 && c.Sound <= bank.Sounds.Count ? bank.Sounds[c.Sound - 1] : null;
                string file = bankName == null ? $"bank{c.Bank}?" : bankName.Replace('\\', '/').Split('/')[^1].ToUpperInvariant() + "HD.SDT";
                clips.Add(new ResolvedClip(s, file, c.Sound, sound?.Name ?? "??", c.Milliseconds, c.Threshold));
            }
        return new Resolved(group, id, path, clips, ev.Sets.Count);
    }

    /// <summary>The bank a resolved clip lives in, for whoever decodes it. Null when the bank
    /// file is not on the disc.</summary>
    public SoundBank BankOf(Resolved r, ResolvedClip c)
    {
        var (_, banks) = MapAt(r.Map);
        foreach (var name in banks)
            if (name.Replace('\\', '/').Split('/')[^1].ToUpperInvariant() + "HD.SDT" == c.Bank) return BankFor(r.Map, name);
        return null;
    }
}
