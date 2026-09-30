using System.Buffers.Binary;
using TPW.PS2.Data;

/// <summary>⭐ THE MUSIC (strawberry, 2026-09-30: "music. everywhere"). The core half of <c>GameMusic</c>: the
/// instructions that decide what the park music can hear, every world's two music maps, and
/// <see cref="MusicSequencer"/> run against them -- with the control that shows the park's guest value
/// WOULD steer the levels if it went to the event's own selector, so "level 1 forever" is visibly the
/// console's wiring and not a sequencer that cannot move.</summary>
static class MusicChecks
{
    static readonly string[] Worlds = { "JUNGLE", "HALLOW", "FANTASY", "SPACE" };

    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "music: " + label);
        var exe = disc.Files().Single(f => f.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        Executable(disc.Read(exe.Extent, exe.Size), Check);
        Maps(disc, Check);
        Sequencer(disc, Check);
        Stops(disc, Check);
        Decodes(disc, Check);
    }

    static void Executable(byte[] elf, Action<bool, string> Check)
    {
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(off, 4));
        int U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(off, 2));
        uint At(uint va)
        {
            int ph = checked((int)U32(28));
            for (int i = 0; i < U16(44); i++)
            {
                int p = ph + i * U16(42);
                if (U32(p) == 1 && va >= U32(p + 8) && va < U32(p + 8) + U32(p + 16)) return U32(checked((int)(U32(p + 4) + va - U32(p + 8))));
            }
            throw new InvalidDataException($"0x{va:x} is not file-backed");
        }
        static uint Jal(uint target) => 0x0C000000u | (target >> 2);
        Check(At(0x151c58) == Jal(0x111e08) && At(0x111e1c) == Jal(0x111d40) && At(0x111e20) == 0x24060002,
              "the park writes the guest value as selector 2: 0x151C58 calls 0x111E08, whose delay slot is `addiu a2, zero, 2`");
        Check(At(0x244f30) == 0x90620013 && At(0x244f34) == 0x90640012 && At(0x244f44) == 0xa0a20000
              && At(0x24c5ac) == 0x90450004 && At(0x24c5b0) == Jal(0x24c1f0),
              "the chooser steers on the event's own Word12: 0x244E08 writes event+0x12 to rec[0], 0x24C590 hands rec[4] to 0x24C1F0");
        Check(At(0x2462cc) == 0x90430000 && At(0x2462d0) == 0x54a30004 && At(0x2462d8) == 0xa0460004,
              "a parameter lands only where the selector matches: 0x2462A0 compares rec[i] and stores rec[4 + i]");
        Check(At(0x2474a8) == Jal(0x24c5e0),
              "flags 4 | 2 | 0x400 make the graph class: 0x2474A8 calls 0x24C5E0 (vtable 0x371450)");
    }

    // Every world: one park event (graph, Word12 4, bands ending at the knob's 90, no per-set slot equal to 2)
    // and one lobby event (one set, three clips, no steering).
    static void Maps(Disc disc, Action<bool, string> Check)
    {
        var park = new List<string>(); var lobby = new List<string>();
        bool parkOk = true, lobbyOk = true;
        foreach (var w in Worlds)
        {
            var cat = new SoundCatalogue(disc, w);
            var p = cat.Resolve(SoundGroup.NativeMusic, 2)?.Source;
            var seq = p == null ? null : new MusicSequencer(p);
            int top = p?.Sets.SelectMany(s => s.Links).Select(l => l.High).DefaultIfEmpty(-1).Max() ?? -1;
            bool slots = p != null && p.Sets.All(s => s.Raw.Length >= 0x10 && s.Raw[0x0A] == 0 && s.Raw[0x0E] == 0);
            parkOk &= seq is { IsGraph: true, SteeringSelector: 4 } && top == 90 && slots;
            park.Add(p == null ? $"{w} none" : $"{w} {p.Sets.Count} sets, flags 0x{p.Flags:x}, Word12 {p.Word12}, top band {top}");
            var l = cat.Resolve(SoundGroup.NativeLobbyMusic, 6)?.Source;
            lobbyOk &= l != null && l.Sets.Count == 1 && l.Sets[0].Clips.Count == 3 && l.Word12 == 0 && !new MusicSequencer(l).IsGraph;
            lobby.Add(l == null ? $"{w} none" : $"{w} {l.Sets.Sum(s => s.Clips.Count)} clips flags 0x{l.Flags:x}");
        }
        Check(parkOk, $"park music, event 2 in all four worlds: {string.Join("; ", park)} -- steered by selector 4, never 2");
        Check(lobbyOk, $"lobby music, event 6 in all four worlds: {string.Join("; ", lobby)}");
    }

    static void Sequencer(Disc disc, Action<bool, string> Check)
    {
        var rows = new List<string>(); bool stuck = true, control = true;
        foreach (var w in Worlds)
        {
            var ev = new SoundCatalogue(disc, w).Resolve(SoundGroup.NativeMusic, 2)?.Source;
            if (ev == null) { stuck = control = false; continue; }
            // The console: a full park (100 guests -> 90) written as selector 2.
            var seq = new MusicSequencer(ev);
            bool took = seq.SetParameter(MusicSequencer.ParkGuestSelector, 90);
            var sets = Run(seq, 60);
            stuck &= !took && seq.SteeringValue == 0 && sets.All(s => s == 0);
            // The control: the same value on the event's OWN selector goes to the top level, and half way to a middle one.
            var full = new MusicSequencer(ev); full.SetParameter(full.SteeringSelector, 90);
            var half = new MusicSequencer(ev); half.SetParameter(half.SteeringSelector, 45);
            int topSet = ev.Sets.Count - 1, fullSet = Run(full, 5).Last(), halfSet = Run(half, 5).Last();
            control &= fullSet == topSet && halfSet > 0 && halfSet < topSet;
            rows.Add($"{w} {fullSet + 1}/{halfSet + 1}");
        }
        Check(stuck, "a full park written as selector 2: no slot takes it, the chooser reads 0, and 60 clips in a row are all level 1 in every world");
        Check(control, $"the control, the same 90 on the event's own selector 4 plays the top level and 45 a middle one (level at 90/45: {string.Join(", ", rows)}) -- the machine can move, the console's knob cannot reach it");

        // The lobby: three clips, never the same twice running once playing (0x245A88's flag), and every clip heard.
        var lob = new SoundCatalogue(disc, "JUNGLE").Resolve(SoundGroup.NativeLobbyMusic, 6)?.Source;
        if (lob == null) { Check(false, "lobby: no jungle event 6"); return; }
        var s2 = new MusicSequencer(lob);
        var clips = new List<int> { s2.Start()!.Value.Clip };
        for (int i = 0; i < 300; i++) clips.Add(s2.Next()!.Value.Clip);
        int repeats = clips.Zip(clips.Skip(1)).Count(p => p.First == p.Second);
        var fresh = new MusicSequencer(lob);
        var starts = Enumerable.Range(0, 301).Select(_ => fresh.Start()!.Value.Clip).ToList();
        int startRepeats = starts.Zip(starts.Skip(1)).Count(p => p.First == p.Second);
        Check(repeats == 0 && clips.Distinct().Count() == 3 && startRepeats > 30,
              $"lobby clips: 0 of 300 follow themselves ({repeats}) and all 3 play; the control -- the same draw WITHOUT the flag -- repeats {startRepeats} times");
    }

    // strawberry, 2026-09-30: "music just... stops?". Two ways the port's music could die that the console's
    // could not (or barely): a guest count past the pool, and a clip draw past a set's last threshold. Each is
    // shown dying without its guard, which is what makes the guarded run's zero mean something.
    static void Stops(Disc disc, Action<bool, string> Check)
    {
        bool capped = true, control = true; var rows = new List<string>();
        int clampedStops = 0, rawStops = 0, runs = 0;
        foreach (var w in Worlds)
        {
            var ev = new SoundCatalogue(disc, w).Resolve(SoundGroup.NativeMusic, 2)?.Source;
            if (ev == null) { capped = control = false; continue; }
            // 150 guests, what three presses of the debug panel's +50 reach.
            var seq = new MusicSequencer(ev);
            seq.SetParameter(seq.SteeringSelector, MusicSequencer.GuestValue(150));
            var sets = Run(seq, 20);
            capped &= sets.Count == 20 && sets.Last() == ev.Sets.Count - 1;
            var raw = new MusicSequencer(ev);
            raw.Start();
            raw.SetParameter(raw.SteeringSelector, (int)(150 * 90f / 100f));
            control &= raw.Next() == null && (raw.Stopped ?? "").StartsWith("no band");
            rows.Add($"{w} {sets.Count}");
            // Two hours of a filling park, 200 times: the value climbs 0..90 over the first 270 clips.
            for (uint seed = 1; seed <= 200; seed++)
                foreach (bool clamp in new[] { true, false })
                {
                    var q = new MusicSequencer(ev, seed) { ClampDraws = clamp };
                    var st = q.Start();
                    for (int i = 0; i < 400 && st != null; i++) { q.SetParameter(q.SteeringSelector, MusicSequencer.GuestValue(i / 3)); st = q.Next(); }
                    if (clamp) { runs++; if (st == null) clampedStops++; } else if (st == null) rawStops++;
                }
        }
        Check(capped && control && MusicSequencer.GuestValue(150) == 90,
              $"past the pool: 150 guests make value {MusicSequencer.GuestValue(150)}, and 20 clips play on the top level in every world ({string.Join(", ", rows)}); the control, the uncapped 135, is in no band and the chooser stops");
        Check(clampedStops == 0 && rawStops > 0,
              $"a missed clip draw: {clampedStops} of {runs} steered two-hour runs stop with ClampDraws; the control, the console's picker, stops {rawStops}");
    }

    static List<int> Run(MusicSequencer seq, int n)
    {
        var sets = new List<int>();
        var first = seq.Start();
        if (first != null) sets.Add(first.Value.Set);
        for (int i = 1; i < n; i++) { var s = seq.Next(); if (s == null) break; sets.Add(s.Value.Set); }
        return sets;
    }

    // One park clip and one lobby clip per world, decoded the way GameMusic decodes them, and the codec lead
    // re-measured on them: a loud edge rings symmetrically through the synthesis filter, so
    // (first + last - length) / 2 is where the music starts.
    static void Decodes(Disc disc, Action<bool, string> Check)
    {
        var rows = new List<string>(); var leads = new List<double>(); bool ok = true;
        foreach (var w in Worlds)
        {
            var cat = new SoundCatalogue(disc, w);
            foreach (var (g, id) in new[] { (SoundGroup.NativeMusic, 2), (SoundGroup.NativeLobbyMusic, 6) })
            {
                var r = cat.Resolve(g, id);
                var c = r?.Clips.FirstOrDefault();
                var bank = c == null ? null : cat.BankOf(r, c);
                var s = bank != null && c.Index >= 1 && c.Index <= bank.Sounds.Count ? bank.Sounds[c.Index - 1] : null;
                var dec = s is { IsMpeg: true } ? Mpeg.DecodeToPcm16(bank.Data[s.Start..s.End]) : null;
                string where = $"{w} {(g == SoundGroup.NativeMusic ? "park" : "lobby")} {c?.Name}";
                if (dec is not { Rate: 22050, Channels: 2 } d) { ok = false; rows.Add($"{where} UNDECODED"); continue; }
                int n = d.Pcm.Length / 4;
                var (start, count) = MusicClip.Content(n, c.Milliseconds, d.Rate);
                bool fits = start + count <= n && Math.Abs(count / 22.05 - c.Milliseconds) < 1 && n - count - start > 0;
                ok &= fits;
                int A(int i) => Math.Max(Math.Abs((int)BitConverter.ToInt16(d.Pcm, i * 4)), Math.Abs((int)BitConverter.ToInt16(d.Pcm, i * 4 + 2)));
                int first = Enumerable.Range(0, n).First(i => A(i) > 2), last = Enumerable.Range(0, n).Last(i => A(i) > 2);
                int L = (int)Math.Round(c.Milliseconds * 22.05);
                if (first < 1400 && last > n - 1500) leads.Add((first + last - L + 1) / 2.0);
                rows.Add($"{where} {n / 22.05:F0} ms decoded, {count / 22.05:F0} played{(fits ? "" : " DOES NOT FIT")}");
            }
        }
        leads.Sort();
        double median = leads.Count == 0 ? double.NaN : leads[leads.Count / 2];
        Check(ok, $"the first clip of each map decodes as 22050 Hz stereo and its map's length fits after the {MusicClip.CodecLead}-sample lead: {string.Join(", ", rows)}");
        Check(leads.Count >= 3 && Math.Abs(median - MusicClip.CodecLead) <= 120,
              $"the lead, re-measured on the {leads.Count} of them loud at both ends: median {median:F0} samples against {MusicClip.CodecLead} ({string.Join(" ", leads.Select(x => x.ToString("F0")))})");
    }
}
