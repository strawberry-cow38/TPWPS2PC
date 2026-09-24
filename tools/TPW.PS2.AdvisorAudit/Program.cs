using System.Buffers.Binary;
using TPW.PS2.Data;
using Op = TPW.PS2.Data.AdvisorRules.Op;
using Result = TPW.PS2.Data.AdvisorRules.Result;

if (args.Length > 0 && args[0] == "--text-self-test")
    return TextTableChecks.Run(args.Contains("--extreme-counts")) == 0 ? 0 : 1;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: dotnet run --project tools/TPW.PS2.AdvisorAudit -- <disc.bin> [--headers=file] [--opcodes=file] [--elf=file] [--list-rules]\n       or: --text-self-test [--extreme-counts]");
    return 2;
}
int failures = TextTableChecks.Run(extremeCounts: true);
void Check(bool ok, string message) { if (!ok) { failures++; Console.Error.WriteLine("FAIL " + message); } }
void Reject(Action action, string message)
{
    try { action(); Check(false, "accepted " + message); }
    catch (InvalidDataException) { }
}
byte[] Words(params short[] words)
{
    var bytes = new byte[words.Length * 2];
    for (int i = 0; i < words.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), words[i]);
    return bytes;
}
byte[] Override(string option, Func<byte[]> original)
{
    var value = args.Skip(1).SingleOrDefault(a => a.StartsWith(option + "=", StringComparison.Ordinal));
    return value == null ? original() : File.ReadAllBytes(value[(option.Length + 1)..]);
}
try
{
    // Signed comparison boundaries, strict elapsed guard, wraparound, and stores before failure.
    var vars = new short[79]; var counters = new short[22];
    AdvisorRules One(params short[] code) => new(new byte[12], Words(code));
    foreach (var (op, value, rhs, expected) in new[] {
        (1, -1, -1, true), (1, -1, 1, false), (2, -1, 1, true), (2, 1, 1, false),
        (3, -2, -1, true), (3, -1, -1, false), (4, 1, -1, true), (4, -1, -1, false) })
    {
        vars[0] = (short)value;
        Check((One((short)op, 0, (short)rhs, 0).Evaluate(0, vars, counters).Result == Result.Completed) == expected,
              $"VM comparison {op}: {value}, {rhs}");
    }
    vars[78] = 120;
    Check(One(9, 120, 0).Evaluate(0, vars, counters).Result == Result.ElapsedBlocked, "elapsed equality must block");
    vars[78] = 121;
    Check(One(9, 120, 0).Evaluate(0, vars, counters).Result == Result.Completed, "elapsed greater must pass");
    vars[0] = short.MaxValue;
    One(5, 0, 1, 0).Evaluate(0, vars, counters);
    Check(vars[0] == short.MinValue, "halfword addition must wrap");
    var ev = One(6, 56, -12, 5, 56, 3, 8, 0, 7, 0, 1, 56, 99, 0).Evaluate(0, vars, counters);
    Check(ev.Result == Result.ConditionFailed && vars[56] == -9 && counters[0] == -12 &&
          ev.Effects.SequenceEqual(new[] { new AdvisorRules.Effect(Op.TextUi, 0), new AdvisorRules.Effect(Op.Message, 0) }),
          "VM side effects must survive subsequent failed guard; only SET mirrors the event counter");
    Reject(() => One(10, 0), "unknown opcode");
    Reject(() => One(7), "truncated operand");
    Reject(() => One(1, 79, 0, 0), "variable outside state");
    Reject(() => One(7, 275, 0), "message outside catalogue");
    Reject(() => One(0, 7, 0, 0), "hidden words after END");
    Reject(() => One(6, 0, 1), "missing END");
    Reject(() => new LipTrack(new byte[4]), "missing LIP terminator");
    Reject(() => new LipTrack(new byte[] { 1, 0, 0, 0, 1, 0, 0, 0, 255, 255, 255, 255 }), "duplicate LIP marks");
    var mouth = new LipTrack.Playback(new LipTrack(new byte[] { 0, 0, 0, 0, 232, 3, 0, 0, 208, 7, 0, 0, 255, 255, 255, 255 }));
    Check(mouth.Advance(0) && !mouth.Advance(10) && mouth.Advance(10) && !mouth.Advance(10) && !mouth.Advance(10),
          "LIP gate starts active, uses strict boundary, advances once per update, disables at terminator");
    var highMark = new LipTrack.Playback(new LipTrack(new byte[] { 0, 0, 0, 128, 255, 255, 255, 255 }));
    Check(highMark.Advance(2147484), "LIP uses signed DIV then unsigned comparison, not unsigned division");

    using var disc = new Disc(args[0]);
    byte[] DiscFile(string path)
    {
        var file = disc.Files().Single(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        return disc.Read(file.Extent, file.Size);
    }
    var data = new WadArchive(DiscFile("/DATA/DATA.WAD"));
    var lips = new WadArchive(DiscFile("/DATA/LIPS.WAD"));
    byte[] WadFile(string path) => data.Read(data.Entries.Single(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));
    byte[] elf = Override("--elf", () => DiscFile("/SLES_500.32"));
    var catalogue = new AdvisorCatalogue(elf);
    byte[] headers = Override("--headers", () => WadFile("/Generic/Advisor/headers.ass"));
    byte[] opcodes = Override("--opcodes", () => WadFile("/Generic/Advisor/opcodes.ass"));
    var rules = new AdvisorRules(headers, opcodes);
    Check(rules.Rules[0].Instructions.Select(i => (i.Code, i.A, i.B)).SequenceEqual(new[] {
        (Op.Greater, (short)4, (short)3), (Op.Equal, (short)0, (short)0), (Op.NotEqual, (short)14, (short)0),
        (Op.TextUi, (short)0, (short)0), (Op.Message, (short)0, (short)0), (Op.End, (short)0, (short)0) }),
        "rule 0 must request OPEN_PARK after its three specific guards");
    Check(rules.Rules[0].WordOffset == 0 && rules.Rules[0].DelayDays == 60 && rules.Rules[1].WordOffset == 14,
          "first two rule boundaries/delay");
    Array.Clear(vars); vars[4] = 4; vars[14] = 1;
    Check(rules.Evaluate(0, vars, counters).Effects.Select(e => catalogue.Messages[e.MessageId].SymbolicKey)
          .SequenceEqual(new[] { "STR_ADVMES_OPEN_PARK", "STR_ADVMES_OPEN_PARK" }), "rule to message identity");
    vars[0] = 1;
    Check(rules.Evaluate(0, vars, counters).Result == Result.ConditionFailed, "variable 0 = 1 blocks rule 0");
    Check(rules.Rules.SelectMany(r => r.Instructions).Where(i => i.Code is Op.Message or Op.TextUi)
          .All(i => i.A < catalogue.Messages.Count), "all rule message operands resolve");
    // CC is not an opcode, duration, or an authored timestamp. Changing only it must be harmless.
    var changedPadding = (byte[])headers.Clone();
    for (int p = 0; p < headers.Length; p += 12) changedPadding.AsSpan(p + 4, 8).Fill(0x5a);
    var paddingRules = new AdvisorRules(changedPadding, opcodes);
    Check(paddingRules.Rules.Zip(rules.Rules).All(pair => pair.First.Instructions.SequenceEqual(pair.Second.Instructions)), "runtime scratch bytes affected decoding");
    Reject(() => new AdvisorRules(headers, opcodes[..^2]), "truncated real opcode stream");

    var expectedLanguages = new Dictionary<string, string[]> {
        ["eur"] = new[] { "ame", "dut", "eng", "fre", "ger", "ita", "jap", "spa", "swe" },
        ["usa"] = new[] { "ame", "dut", "eng", "ger", "ita", "jap", "spa", "swe" },
        ["jap"] = new[] { "dut", "eng", "fre", "ger", "ita", "jap", "spa", "swe" } };
    var texts = new Dictionary<string, TextDatabase>();
    var databases = new Dictionary<string, AssetResourceDatabase>();
    byte[] referenceIds = WadFile("/Text/translations/eur/id.dat");
    foreach (var region in new[] { "eur", "usa", "jap" })
    {
        var text = TextDatabase.Load(data, region) ?? throw new InvalidDataException("Missing text region " + region);
        texts.Add(region, text); catalogue.ValidateText(text);
        var tableNames = data.Entries.Where(e => !WadArchive.IsAlias(e) &&
            e.Path.StartsWith($"/Text/translations/{region}/", StringComparison.OrdinalIgnoreCase) &&
            e.Path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)).Select(e => Path.GetFileNameWithoutExtension(e.Path).ToLowerInvariant());
        // The plain-text build masters share .dat with the compiled binary tables.
        string[] masters = region switch { "eur" => new[] { "final", "finalall", "finalfre", "finalger" },
            "usa" => new[] { "final", "finalame" }, _ => new[] { "final", "finaljap" } };
        Check(tableNames.Order().SequenceEqual(expectedLanguages[region].Append("id").Concat(masters).Order()),
              region + " on-disc table/master identities");
        Check(text.Languages.Keys.Order().SequenceEqual(expectedLanguages[region]), region + " language identities");
        Check(WadFile($"/Text/translations/{region}/id.dat").AsSpan().SequenceEqual(referenceIds), region + " id.dat bytes differ");
        foreach (var (language, strings) in text.Languages)
            Check(strings.Length == text.Keys.Length && catalogue.Messages.Where(m => m.HasText)
                  .All(m => text.Text(language, m.TextRow) != null), $"{region}/{language}: missing translated advisor rows");
        foreach (var language in expectedLanguages[region].Append("id"))
        {
            byte[] raw = WadFile($"/Text/translations/{region}/{language}.dat");
            int n = BinaryPrimitives.ReadInt32LittleEndian(raw);
            int end = checked(4 + 4 * n);
            for (int i = 0; i < n; i++)
            {
                int start = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(4 + 4 * i));
                Check(start == end, $"{region}/{language} row {i}: offset does not follow preceding NUL");
                if (start < 0 || start >= raw.Length) throw new InvalidDataException("Text offset outside file");
                end = Array.IndexOf(raw, (byte)0, start) + 1;
                if (end == 0) throw new InvalidDataException("Text has no terminator");
            }
            Check(end == raw.Length, $"{region}/{language}: unconsumed text bytes");
        }
        string dbaPath = region switch { "eur" => "/arsdb.dba", "usa" => "/arsusdb.dba", _ => "/arsjapdb.dba" };
        var db = new AssetResourceDatabase(WadFile(dbaPath)); databases.Add(region, db);
        foreach (var entry in db.Entries)
            Check(entry.TextRow < text.Keys.Length && text.Keys[entry.TextRow].StartsWith("STR_GRAPHICS_", StringComparison.Ordinal),
                  $"{region} DBA key {entry.Key:x}: asset text identity");
        var bounce = db.Find(215);
        Check(bounce != null && bounce.Offset == 0xcdc && bounce.Payload.Length == 240 &&
              text.Keys[bounce.TextRow] == "STR_GRAPHICS_JUNGLE_RIDES_BOUNCY_BOUNCY" && text.Text("eng", (int)bounce.TextRow) == "Belly Bounce",
              region + " DBA 215 must identify Belly Bounce, not advisor text");
        var duplicates = db.Entries.Where(e => e.Key == uint.MaxValue).ToArray();
        Check(duplicates.Select(e => e.Offset).SequenceEqual(new[] { 36304, 38504 }) && db.Find(uint.MaxValue) == duplicates[0],
              region + " DBA duplicate keys and first-match lookup");
        Reject(() => new AssetResourceDatabase(WadFile(dbaPath)[..^1]), region + " truncated DBA");
        Console.WriteLine($"TEXT {region}: {string.Join(',', text.Languages.Keys.Order())}; all message row/key identities checked");
    }
    var eurDb = databases["eur"]; var usDb = databases["usa"]; var japDb = databases["jap"];
    var regionalDifferences = new HashSet<uint>();
    for (int i = 0; i < eurDb.Entries.Count; i++)
    {
        var e = eurDb.Entries[i]; var u = usDb.Entries[i]; var j = japDb.Entries[i];
        Check(e.Key == u.Key && e.Key == j.Key && e.Offset == u.Offset && e.Offset == j.Offset &&
              e.Payload.Span.SequenceEqual(j.Payload.Span), $"DBA regional identity at directory row {i}");
        if (!e.Payload.Span.SequenceEqual(u.Payload.Span))
        {
            regionalDifferences.Add(e.Key);
            string expectedKey = e.Key switch {
                245 => "STR_GRAPHICS_JUNGLE_SHOPS_ICECREAM_ICECREAM", 151 => "STR_GRAPHICS_HALLOW_SHOPS_ICES_ICES",
                69 => "STR_GRAPHICS_FANTASY_SHOPS_ICECREAM_ICECREAM", 391 => "STR_GRAPHICS_SPACE_SHOPS_ICES_ICES", _ => "" };
            Check(texts["eur"].Keys[e.TextRow] == expectedKey, $"DBA USA difference key {e.Key}: wrong asset identity");
            var expected = e.Payload.ToArray(); expected[0x32] = 15; expected[0x36] = 15;
            Check(e.Payload.Span[0x32] == 25 && e.Payload.Span[0x36] == 10 && expected.AsSpan().SequenceEqual(u.Payload.Span),
                  $"DBA USA difference key {e.Key} changed beyond the observed two bytes");
        }
    }
    Check(regionalDifferences.SetEquals(new uint[] { 245, 151, 69, 391 }), "DBA regional difference identities");

    foreach (var entry in lips.Entries.Where(e => e.Path.EndsWith(".lip", StringComparison.OrdinalIgnoreCase)))
        _ = new LipTrack(lips.Read(entry));
    foreach (var (audioLanguage, textLanguage) in new[] { ("English", "eng"), ("French", "fre"), ("German", "ger") })
    {
        var bank = new SoundBank(DiscFile($"/AUDIO/ADVISOR/{audioLanguage.ToUpperInvariant()}/SPCHHD.SDT"));
        var referenced = new HashSet<int>();
        foreach (var message in catalogue.Messages)
            for (int v = 0; v < message.VariantCount; v++)
            {
                if (message.Voices[v].SoundIndex is not int soundIndex) continue;
                referenced.Add(soundIndex);
                foreach (var region in texts.Keys)
                {
                    var binding = catalogue.Bind(message.Id, v, audioLanguage, bank, lips, texts[region], textLanguage);
                    bool defect = message.Id == 268 && v == 0;
                    Check(defect ? binding.Voice.SoundId == 28 && binding.Voice.LipStem == "PS2_" &&
                          binding.Sound.Name.Equals("PS2_1.mp2", StringComparison.OrdinalIgnoreCase) && binding.Lip == null && !binding.SoundStemMatches
                          : binding.SoundStemMatches && binding.Lip != null,
                          $"{region}/{audioLanguage} message {message.Id} variant {v}: sound/lip identity");
                    Check(binding.Subtitle == (message.HasText ? texts[region].Text(textLanguage, message.TextRow) : null),
                          $"{region}/{audioLanguage} subtitle availability");
                }
            }
        var chain = catalogue.Bind(0, 0, audioLanguage, bank, lips, texts["eur"], textLanguage);
        string expectedSubtitle = audioLanguage switch {
            "English" => "People want to come in but\nyour park is closed. You\nshould think about opening\nup.",
            "French" => "Des visiteurs veulent entrer mais\nle parc est fermé.\nVous devriez l'ouvrir.",
            _ => "Die Leute wollen rein, aber dein\nVergnügungspark ist geschlossen.\nVielleicht solltest du mal drüber\nnachdenken, ob du ihn nicht öffnen\nwillst." };
        foreach (var (region, text) in texts)
            Check(catalogue.Bind(0, 0, audioLanguage, bank, lips, text, textLanguage).Subtitle ==
                  (region == "usa" && audioLanguage == "French" ? null : expectedSubtitle),
                  region + "/" + audioLanguage + " exact OPEN_PARK text and missing-language behavior");
        uint[] expectedMarks = audioLanguage switch {
            "English" => new uint[] { 2226893, 2812380, 4058820 },
            "French" => new uint[] { 2322267, 2899682, 4640090 },
            _ => new uint[] { 3272743, 3767619, 6915192 } };
        Check(chain.Message.SymbolicKey == "STR_ADVMES_OPEN_PARK" && chain.Message.TextRow == 1030 &&
              chain.Voice.SoundId == 48 && chain.Sound.Name == "sp_001.mp2" && chain.Lip.Microseconds.SequenceEqual(expectedMarks),
              audioLanguage + " OPEN_PARK chain including exact lip timeline");
        Console.WriteLine($"CHAIN {audioLanguage}: id.dat[1030] {chain.Message.SymbolicKey} -> message 0/variant 0 -> sound ID 48/index 47 {chain.Sound.Name} -> {chain.LipPath}");
        Console.WriteLine("  " + chain.Subtitle.Replace('\n', ' '));
        Console.WriteLine("UNREFERENCED " + audioLanguage + ": " + string.Join(", ", bank.Sounds.Where((_, i) => !referenced.Contains(i)).Select(s => s.Name)));
        Console.WriteLine("KNOWN DEFECT " + audioLanguage + ": message 268 sound 28=PS2_1.mp2; requested PS2_.lip is absent");
        // Swap valid sound IDs without changing any counts. Identity checks must catch this.
        var swapped = new SoundBank(bank.Data); (swapped.Sounds[47], swapped.Sounds[48]) = (swapped.Sounds[48], swapped.Sounds[47]);
        Check(!catalogue.Bind(0, 0, audioLanguage, swapped, lips, texts["eur"], textLanguage).SoundStemMatches,
              audioLanguage + " same-count sound swap negative control");
    }
    // Change a valid text index to a different valid row, leaving counts and pointers intact.
    var badElf = (byte[])elf.Clone();
    int tableFileOffset = 0x1a7ac8; // fixture mutation only; production maps PT_LOAD.
    BinaryPrimitives.WriteUInt16LittleEndian(badElf.AsSpan(tableFileOffset + 4), 158);
    Reject(() => new AdvisorCatalogue(badElf).ValidateText(texts["eur"]), "same-count text row substitution");
    if (args.Contains("--list-rules"))
        for (int i = 0; i < rules.Rules.Count; i++)
        {
            var r = rules.Rules[i];
            Console.WriteLine($"RULE {i} word {r.WordOffset} delay {r.DelayDays} days: " + string.Join("; ", r.Instructions.Select(ins =>
                ins.Code is Op.Message or Op.TextUi ? $"{ins.Code} {ins.A} ({catalogue.Messages[ins.A].SymbolicKey})" :
                ins.Code == Op.End ? "END" : ins.Code == Op.ElapsedGreater ? $"elapsed > {ins.A}" : $"{ins.Code} v[{ins.A}], {ins.B}")));
        }
    Console.WriteLine($"Decoded {rules.Rules.Count} rules and {catalogue.Messages.Count} message records. Counts are coverage, not proof.");
    Console.WriteLine("LIMITS: no listening/transcription, full game-state producers, queue simulation, animation rendering, or DBA payload semantics beyond the common prefix.");
}
catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + ex); }
Console.WriteLine(failures == 0 ? "PASS advisor audit (identity checks and negative controls)" : $"FAIL advisor audit: {failures}");
return failures == 0 ? 0 : 1;
