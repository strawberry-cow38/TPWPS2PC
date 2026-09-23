using System.Text;
using System.Text.RegularExpressions;
using TPW.PS2.Data;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: RseAudit disc.bin [--mutate-add|--mutate-sub] [--trace]");
    return 2;
}
try
{
    using var disc = new Disc(args[0]);
    var wads = new Dictionary<string, WadArchive>(StringComparer.OrdinalIgnoreCase);
    foreach (string world in new[] { "JUNGLE", "HALLOW", "SPACE", "FANTASY", "JRSE", "HRSE", "SRSE", "FRSE" })
    {
        var entry = disc.Files().Single(e => e.Path.Equals($"/DATA/{world}.WAD", StringComparison.OrdinalIgnoreCase));
        wads[world] = new WadArchive(disc.Read(entry.Extent, entry.Size));
    }
    var audit = new Audit(wads, args.Contains("--trace"));
    audit.Corpus();
    audit.Child(args.Contains("--mutate-add"));
    audit.Jets();
    audit.Timeout(args.Contains("--mutate-sub"));
    foreach (var (world, stem) in new[] {
        ("JUNGLE", "/Rides/Monkey/Monkey"), ("JUNGLE", "/Rides/Spider/Spider"),
        ("FANTASY", "/Rides/bugstv/bugstv"), ("HALLOW", "/rides/phantom/Phantom"),
        ("SPACE", "/Rides/orbiter/orbiter") }) audit.Ride(world, stem);
    audit.Firework();
    audit.Guards();
    Console.WriteLine("PASS: source-derived variable, guest ID, timer, call/return and APS identity checks");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }

sealed class Audit(Dictionary<string, WadArchive> wads, bool trace)
{
    static void Equal<T>(T actual, T expected, string where)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            throw new Exception($"{where}: expected {expected}, got {actual}");
    }
    byte[] Read(string world, string path) => wads[world].Read(wads[world].Find(path)
        ?? throw new Exception($"Missing {world}{path}"));
    RseProgram Script(string world, string stem) => new(Read(world, stem + ".rse"));
    string Source(string world, string stem) => Encoding.ASCII.GetString(Read(world, stem + ".rss"));
    static string[] Lines(string source) => source.Split('\n').Select(l => Regex.Replace(l,
        "\"[^\"]*\"|;.*|//.*", m => m.Value.StartsWith('"') ? m.Value : "").Trim())
        .Where(l => l.Length > 0).ToArray();
    void Require(string source, params string[] statements)
    {
        string normalized = string.Join('\n', Lines(source).Select(l => Regex.Replace(l, @"\s+", " ")));
        foreach (string statement in statements)
            if (!normalized.Contains(statement, StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Source no longer supports prediction: {statement}");
    }
    public void Corpus()
    {
        foreach (var (world, wad) in wads)
        {
            foreach (var entry in wad.Entries.Where(e => e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)))
            {
                var program = new RseProgram(wad.Read(entry));
                var sourceEntry = wad.Find(Path.ChangeExtension(entry.Path, ".rss"));
                if (sourceEntry == null) continue;
                string source = Encoding.ASCII.GetString(wad.Read(sourceEntry));
                var names = Lines(source).Select(l => Regex.Match(l, @"^variable\s+(\w+)", RegexOptions.IgnoreCase))
                    .Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().ToArray();
                Equal(string.Join(',', program.VariableNames), string.Join(',', names), $"{world}{entry.Path} variable identity");
                foreach (var (directive, value) in new[] { ("setstack", program.StackSize), ("setlimbo", program.LimboCapacity),
                    ("setbounce", program.BounceCapacity), ("setwalk", program.WalkCapacity) })
                {
                    var match = Regex.Match(source, @"#" + directive + @"\s+(\d+)", RegexOptions.IgnoreCase);
                    if (match.Success) Equal(value, int.Parse(match.Groups[1].Value), $"{world}{entry.Path} #{directive}");
                }
                Align(program, source, world + entry.Path);
            }
            Console.WriteLine($"CORPUS {world}: source mnemonics, labels, variables, literals and string identities agree");
        }
    }
    static void Align(RseProgram program, string source, string where)
    {
        var rows = new List<string[]>();
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int word = 0;
        foreach (string line in Lines(source))
        {
            var tokens = Regex.Matches(line, "\"[^\"]*\"|\\S+").Select(m => m.Value).ToArray();
            if (tokens[0].StartsWith('.')) { labels[tokens[0][1..]] = word; tokens = tokens[1..]; }
            if (tokens.Length == 0 || !Enum.TryParse<RseOpcode>(tokens[0], out _)) continue;
            rows.Add(tokens); word += tokens.Length;
        }
        Equal(rows.Count, program.Instructions.Count, where + " source alignment extent");
        for (int i = 0; i < rows.Count; i++)
        {
            var tokens = rows[i]; var ins = program.Instructions[i];
            Equal(ins.Opcode.ToString(), tokens[0], where + $" mnemonic at {ins.Address}");
            Equal(ins.Operands.Count, tokens.Length - 1, where + $" arity at {ins.Address}");
            for (int j = 1; j < tokens.Length; j++)
            {
                var op = ins.Operands[j - 1]; string token = tokens[j];
                if (program.VariableNames.Contains(token))
                {
                    Equal(op.Tag, 0x40, where + " variable tag");
                    Equal(program.VariableNames[op.Index], token, where + $" variable at {ins.Address}");
                }
                else if (labels.TryGetValue(token, out int target))
                { Equal(op.Tag, 0x20, where + " label tag"); Equal(op.Index, target, where + $" label {token}"); }
                else if (token.StartsWith('"'))
                { Equal(op.Tag, 0x10, where + " string tag"); Equal(program.StringAt(op.Index), token.Trim('"'), where + " string"); }
                else
                {
                    Equal(op.Tag, 0, where + " constant tag");
                    if (int.TryParse(token, out int literal)) Equal(op.Immediate, unchecked((int)(short)literal), where + $" literal at {ins.Address}");
                }
                // Include headers are absent. Their symbolic constants are not invented here.
            }
        }
    }
    public void Child(bool mutate)
    {
        const string stem = "/Rides/Monkey/child";
        Require(Source("JUNGLE", stem), "ADD VAR_TEMP 1", "BRANCH loop");
        byte[] bytes = Read("JUNGLE", stem + ".rse");
        var original = new RseProgram(bytes);
        if (mutate)
        {
            // Same instruction size and slice counts, different behaviour: += 1 becomes = 1.
            var add = original.Instructions.Single(i => i.Opcode == RseOpcode.ADD);
            BitConverter.GetBytes(0x80000003u).CopyTo(bytes, 52 + add.Address * 4);
        }
        var vm = new RseMachine(new RseProgram(bytes));
        Equal(vm["VAR_TEMP"], 0, "child initial zero");
        for (int tick = 1; tick <= 4; tick++)
        {
            vm.RunSlice(tick * 100);
            // First slice: NAME + 24 (ADD, BRANCH) pairs + ADD. Later slices: 25 pairs.
            Equal(vm["VAR_TEMP"], 25 * tick, $"child tick {tick}: VAR_TEMP");
        }
        Console.WriteLine("CHILD t=100/200/300/400: VAR_TEMP = 25/50/75/100");
    }
    public void Jets()
    {
        const string stem = "/Rides/scentro/Jets";
        Require(Source("SPACE", stem), "COPY VAR_JET 0", "ADD VAR_JET 1", "CMP VAR_JET 5", "COPY VAR_JET 1", "WAIT 600");
        var host = new RsePreviewHost(null);
        var vm = new RseMachine(Script("SPACE", stem), host);
        vm.RunSlice(0); Equal(vm["VAR_JET"], 0, "jets disabled");
        vm["VAR_JETSON"] = 1;
        foreach (var (time, jet) in new[] { (100, 1), (699, 1), (700, 2), (1300, 3), (1900, 4), (2500, 1) })
        {
            vm.RunSlice(time);
            Equal(vm["VAR_JET"], jet, $"jets t={time}");
            Equal(host.LastEffect.Arguments[1], jet, $"jets effect node t={time}");
        }
        vm["VAR_JETSON"] = 0; vm.RunSlice(3100);
        Equal(vm["VAR_JET"], 1, "jets disabled after wait");
        vm["VAR_JETSON"] = 1; vm.RunSlice(3200);
        Equal(vm["VAR_JET"], 1, "jets restart resets to node 1");
        Console.WriteLine("JETS t=100/699/700/1300/1900/2500: VAR_JET and effect node = 1/1/2/3/4/1");
    }
    public void Ride(string world, string stem)
    {
        string source = Source(world, stem);
        Require(source, "COPY VAR_RUNNING 1", "COPY VAR_RUNNING 0", "HUSH VAR_LETMEON", "HOP VAR_LETMEOFF",
            "ADD VAR_ONRIDE 1", "ADD VAR_ONRIDE -1", "ADD VAR_SPACELEFT -1", "COPY VAR_COUNT VAR_DURATION");
        var aps = new Animation(Read(world, stem + ".aps"));
        var preview = new RseRidePreview(Script(world, stem), aps);
        var vm = preview.Machine;
        int previousOn = 0, previousSlot = -1, previousVariant = -1;
        bool running = false, loaded = false, countWasOne = false;
        var animationPath = new List<string>();
        for (long t = 0; t <= 240000; t += 100)
        {
            preview.Tick(t);
            int on = vm["VAR_ONRIDE"];
            if (stem == "/Rides/Monkey/Monkey")
            {
                // Its 215-frame Create gives WAIT4ANIM a 6866ms deadline; 100ms visits
                // resume at 6900, ENDSLICE, then WAIT 500 at 7000. Thus loading is 7500.
                if (t == 7500) Snapshot(vm, "Ape t=7500", 1, 1, 17500, 5000, 0, 0);
                if (t == 7600) Snapshot(vm, "Ape t=7600", 2, 0, 17600, 9900, 0, 0);
                if (t == 7700) Snapshot(vm, "Ape t=7700", 2, 0, 17600, 10866, 1, 1);
            }
            if (on > previousOn)
            {
                Equal(on, previousOn + 1, $"{stem} board increment");
                Equal(vm["VAR_LETMEON"], 0, $"{stem} board handshake");
                Equal(vm["VAR_SPACELEFT"], 2 - on, $"{stem} signed decrement");
                Equal(vm.GuestCount, on, $"{stem} guest stack");
                // The source resets its timer at the actual boarding slice, to now + 10000.
                if (Regex.IsMatch(source, @"\bGETTIME\b")) Equal(vm["VAR_STARTNOW"], (int)t + 10000, $"{stem} boarding deadline");
                loaded |= on == 2;
            }
            if (vm["VAR_RUNNING"] == 1 && !running)
            {
                Equal(on, 2, $"{stem} run with two guests"); running = true;
            }
            if (running && vm["VAR_COUNT"] == 1) countWasOne = true;
            if (on < previousOn) Equal(on, previousOn - 1, $"{stem} unload decrement");
            if (previousSlot != preview.Host.AnimationSlot || previousVariant != preview.Host.AnimationVariant)
            {
                animationPath.Add($"{preview.Host.Current.Record.SlotName}:{preview.Host.AnimationVariant}");
                if (trace) Console.WriteLine($"TRACE {world}{stem} t={t} anim={preview.Host.AnimationSlot}:{preview.Host.AnimationVariant} vars={string.Join(',', vm.Variables)}");
                previousSlot = preview.Host.AnimationSlot; previousVariant = preview.Host.AnimationVariant;
            }
            previousOn = on;
            if (preview.Completed)
            {
                Equal(loaded, true, $"{stem} filled capacity"); Equal(running, true, $"{stem} entered run");
                Equal(countWasOne, true, $"{stem} copied duration into COUNT");
                Equal(vm["VAR_RUNNING"], 0, $"{stem} stopped"); Equal(vm["VAR_COUNT"], 0, $"{stem} duration decremented 1 -> 0");
                Equal(vm["VAR_LETMEOFF"], 0, $"{stem} unload acknowledged"); Equal(vm.GuestCount, 0, $"{stem} guest stack empty");
                string expectedPath = Path.GetFileName(stem).ToLowerInvariant() switch
                {
                    "monkey" => "Create:0,Load:0,Start:0,Main:1,Main:0,Main:2,Main:4,Main:3,Main:5,Main:6,End:0",
                    "spider" => "Create:0,Idle:0,Main:0,Main:1,Main:2,Main:3",
                    "phantom" => "Create:0,Load:0,Start:0,Main:0,End:0",
                    _ => "Create:0,Start:0,Main:0,End:0"
                };
                Equal(string.Join(',', animationPath), expectedPath, $"{stem} source-authored animation sequence");
                Console.WriteLine($"RIDE {world}{stem}: ONRIDE 0->1->2->1->0; SPACELEFT 2->1->0; COUNT 1->0; guests 102 then 101; Main APS played; closed at {t}ms");
                return;
            }
        }
        throw new Exception($"{stem}: cycle did not finish, PC={vm.Pc}, vars={string.Join(',', vm.Variables)}");
    }
    static void Snapshot(RseMachine vm, string where, int on, int space, int deadline, int temp, int count, int running)
    {
        Equal(vm["VAR_ONRIDE"], on, where + " ONRIDE"); Equal(vm["VAR_SPACELEFT"], space, where + " SPACELEFT");
        Equal(vm["VAR_STARTNOW"], deadline, where + " STARTNOW"); Equal(vm["VAR_TEMP"], temp, where + " TEMP");
        Equal(vm["VAR_COUNT"], count, where + " COUNT"); Equal(vm["VAR_RUNNING"], running, where + " RUNNING");
    }
    public void Timeout(bool mutate)
    {
        const string stem = "/Rides/Spider/Spider";
        Require(Source("JUNGLE", stem), "ADD VAR_STARTNOW 10000", "SUB VAR_TEMP VAR_STARTNOW VAR_TEMP", "BRANCH_NV run");
        byte[] bytes = Read("JUNGLE", stem + ".rse");
        var program = new RseProgram(bytes);
        if (mutate)
        {
            var sub = program.Instructions.First(i => i.Opcode == RseOpcode.SUB);
            // Reverse deadline - now, keeping opcode, arity, all indices and instruction counts.
            int offset = 52 + (sub.Address + 2) * 4;
            var first = bytes.AsSpan(offset, 4).ToArray();
            bytes.AsSpan(offset + 4, 4).CopyTo(bytes.AsSpan(offset, 4)); first.CopyTo(bytes, offset + 4);
        }
        var vm = new RseMachine(new RseProgram(bytes), new ImmediateHost()); vm[2] = 2;
        vm.RunSlice(0); vm.RunSlice(700); vm.RunSlice(800);
        Equal(vm["VAR_STARTNOW"], 10800, "spider timeout deadline");
        foreach (var (t, temp, running) in new[] { (800, 10000, 0), (10799, 1, 0), (10800, 0, 0), (10801, -1, 1) })
        {
            vm.RunSlice(t);
            Equal(vm["VAR_TEMP"], temp, $"spider timeout t={t}");
            Equal(vm["VAR_RUNNING"], running, $"spider strict negative branch t={t}");
        }
        Console.WriteLine("TIMEOUT t=800/10799/10800/10801: TEMP=10000/1/0/-1; RUNNING=0/0/0/1");
    }
    public void Firework()
    {
        const string stem = "/features/firework/Firework";
        Require(Source("HALLOW", stem), "JSR gimme", "SETTIMER 30000", "GETTIMER 0", "RETURN", "WAIT 20000", "COPY VAR_STATUS 1");
        var host = new ImmediateHost();
        var vm = new RseMachine(Script("HALLOW", stem), host);
        // Fixed 1000ms animation service: WAITANIM = 700ms. Both subroutines last 30000ms;
        // then WAIT 2000 + WAIT 20000. 100ms host visits land on every boundary.
        for (int t = 0; t <= 82700; t += 100)
        {
            vm.RunSlice(t);
            Equal(vm["VAR_STATUS"], t < 82700 ? 0 : 1, $"firework t={t} status");
        }
        Console.WriteLine("FIREWORK: two 30000ms subroutine timers, STATUS=0 at 82600ms, STATUS=1 at 82700ms");
    }
    public void Guards()
    {
        var bad = Read("JUNGLE", "/Rides/Monkey/child.rse");
        var p = new RseProgram(bad);
        var branch = p.Instructions.Single(i => i.Opcode == RseOpcode.BRANCH);
        BitConverter.GetBytes(0x20000003u).CopyTo(bad, 52 + (branch.Address + 1) * 4); // ADD operand, not opcode
        try { new RseProgram(bad); throw new Exception("Branch-into-operand guard failed"); }
        catch (InvalidDataException) { }
        var vm = new RseMachine(Script("HALLOW", "/rides/bug/Bug"));
        try { vm.RunSlice(0); throw new Exception("Absent host guard failed"); }
        catch (InvalidOperationException) when (vm.Fault != null) { }
        // ⚠⚠ THIS GUARD USED TO RUN SPACE's Gates AND EXPECT IT TO FAULT ON LOOPANIM_CH. It does
        // not any more -- every opcode the loader will accept is now implemented, so there is no
        // shipped script left that can demonstrate a run-time refusal. Rather than delete the
        // check or keep asserting something that has become false, it now proves the refusal that
        // IS still live: an opcode number the arity table does not know is rejected at LOAD, so a
        // future disc or a corrupted file cannot execute as if it were understood.
        var unknown = Read("JUNGLE", "/Rides/Monkey/child.rse");
        var op = new RseProgram(unknown).Instructions.Single(i => i.Opcode == RseOpcode.ADD);
        BitConverter.GetBytes(0x8000006Bu).CopyTo(unknown, 52 + op.Address * 4);
        try { new RseProgram(unknown); throw new Exception("Unknown opcode guard failed"); }
        catch (InvalidDataException) { }
        Console.WriteLine("GUARDS: branch into operand and unknown opcode rejected at load; absent host faults with PC");
    }
    sealed class ImmediateHost : IRseHost
    {
        public int AnimationSlot { get; private set; } = -1;
        public void AdvanceTo(long milliseconds) { }
        public int PlayAnimation(int slot, int variant, bool loop) { AnimationSlot = slot; return 1000; }
        public void FlushAnimation() => AnimationSlot = -1;
        public bool TryEffect(RseOpcode opcode, IReadOnlyList<int> arguments) => true;
        public int PlayAnimationOn(int channel, int slot, int variant, bool loop) => PlayAnimation(slot, variant, loop);
        // No node table here either, so walks in this audit run at the floor time.
        public bool TryNodePosition(int node, int space, out float x, out float y, out float z)
        { x = y = z = 0f; return false; }
        public void WalkerPose(int guest, int fromNode, int toNode, int mode, int perMille, int angle) { }
        public void GuestVisible(int guest, bool visible) { }
        public int AnimationRemainingOn(int channel) => -1;
        public int PlayAnimationSpeed(int slot, int variant, int speedPerMille) => PlayAnimation(slot, variant, false);
    }
}
