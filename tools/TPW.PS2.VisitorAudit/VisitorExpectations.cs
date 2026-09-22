using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using TPW.PS2.Data;

namespace TPW.PS2.Audits;

// Audit-only oracle, shared by the console and rendered audits. Never runs VisitorScenario,
// VisitorSimulation, Route or the RSSE VM to obtain expected values. Site selection reads the
// original bytes + SAM; motion is distance / speed; lifecycle is the checked RSS path + APS.
public sealed class VisitorExpectations
{
    public string World { get; }
    public int TerrainNumber { get; }
    public string Label => $"{World}/{TerrainNumber}";
    public string Stem { get; }
    public int Width { get; }
    public int Height { get; }
    public int Capacity { get; }
    public int Id { get; }
    public ParkCell Origin { get; }
    public ParkCell Head { get; }
    public ParkCell Spawn => Head.Offset(-4, 6);
    public ParkCell Exit => Head.Offset(1, 0);
    public ParkCell[] Queue { get; }
    public ParkCell[] Public { get; }
    public ParkCell[] Footprint { get; }
    public ParkCell[] Inward { get; }
    public ParkCell[] Outward { get; }
    public string Counts { get; }
    public int Sites { get; }
    public long[] Board { get; }
    public long QueueAt => (Inward.Length - Queue.Length) * 1000L;
    public int Timeout { get; }
    public long RunningAt { get; }
    public long StartAnimationAt { get; }
    public long UnloadAt { get; }
    public long[] Unload { get; }
    public long AdaAck => UnloadAt + 7000;
    public long DepartAt => AdaAck + (Outward.Length - 2) * 1000L;
    public string Evidence => $"{Label}: {Counts}, sites={Sites}, origin={Origin}, spawn={Spawn}, queue={Head}..{Queue[^1]}, exit={Exit}";

    public VisitorExpectations(WadArchive wad, string world, int terrain, Action<string> print)
    {
        World = world; TerrainNumber = terrain;
        Stem = world switch { "SPACE" => "/Rides/orbiter/orbiter", "FANTASY" => "/Rides/bugstv/bugstv",
            _ => throw new ArgumentException("No source derivation for " + world) };
        byte[] Read(string path) => wad.Read(wad.Find(path) ?? throw new Exception("Missing " + path));
        var model = new Model(Read($"/terrain/terrain_{terrain}.mps"));
        var grid = new ParkPaths(model); var f = model.Field;
        // Deliberately bypass both CanBuild and HeightField.Buildable in the oracle. Share only
        // the separately measured scenery projection; check every cell, including nonzero flags.
        bool BitClear(ParkCell c) => (f.Raw0(c.X, c.Z) & 1) == 0;
        bool Eligible(ParkCell c) => grid.Contains(c) && BitClear(c) && !grid.SceneryBlocks(c);
        int expected = grid.Cells.Count(Eligible), actual = grid.Cells.Count(grid.CanBuild);
        Counts = $"buildable={grid.Cells.Count(BitClear)}/{f.Count}, eligible={actual}, expected eligible={expected}, Raw0==0={grid.Cells.Count(c => f.Raw0(c.X, c.Z) == 0)}";
        print($"CENSUS {Label}: {Counts}");
        Require(expected > 0 && actual > 0, $"{Label} bit-0 eligibility is empty; {Counts}");
        foreach (var c in grid.Cells)
            Require(grid.CanBuild(c) == Eligible(c), $"{Label} bit-0 eligibility at {c}, byte0=0x{f.Raw0(c.X, c.Z):X2}; {Counts}");
        var sam = RideDefinition.Parse(Encoding.ASCII.GetString(Read(Stem + ".sam")));
        Width = sam.Shape.Max(r => r.Length); Height = sam.Shape.Length;
        Capacity = sam.UpgradeCapacity(0).Value; Id = sam.Id.Value;
        var local = sam.Shape.SelectMany((row, z) => row.Select((ch, x) => (ch, c: new ParkCell(x, z))))
            .Where(p => !char.IsWhiteSpace(p.ch)).ToArray();
        var entry = local.Single(p => p.ch == '2').c;
        Require(entry.Z == Height - 1, "SAM entrance is not on the south edge");
        ParkCell[] QueueAtHead(ParkCell h) => Enumerable.Range(0, 4).Select(z => h.Offset(0, z)).ToArray();
        ParkCell[] PublicAtHead(ParkCell h) => Enumerable.Range(-4, 9).Select(x => h.Offset(x, 6))
            .Concat(new[] { h.Offset(0, 4), h.Offset(0, 5) })
            .Concat(Enumerable.Range(1, 4).Select(x => h.Offset(x, 0)))
            .Concat(Enumerable.Range(1, 5).Select(z => h.Offset(4, z))).ToArray();
        // Exhaustive mask fit, ranked by integer squared distance of the footprint centre.
        // This never observes the production search's selected origin or its laid cells.
        var sites = new List<(ParkCell Cell, int Distance)>();
        foreach (var c in grid.Cells)
        {
            var h = c.Offset(entry.X, Height);
            var required = local.Select(p => c.Offset(p.c.X, p.c.Z)).Concat(QueueAtHead(h)).Concat(PublicAtHead(h));
            if (!required.All(p => Eligible(p) && (f.Material(p.X, p.Z) == 0
                || ParkPaths.Classify(model.Materials[f.Material(p.X, p.Z)]) == ParkPathKind.None))) continue;
            int dx = 2 * c.X + Width - f.Width, dz = 2 * c.Z + Height - f.Height;
            sites.Add((c, dx * dx + dz * dz));
        }
        Sites = sites.Count;
        Require(Sites > 0, $"{Label}: no complete layout; {Counts}");
        Origin = sites.OrderBy(s => s.Distance).ThenBy(s => s.Cell.Z).ThenBy(s => s.Cell.X).First().Cell;
        Head = Origin.Offset(entry.X, Height);
        Footprint = local.Select(p => Origin.Offset(p.c.X, p.c.Z)).ToArray();
        Queue = QueueAtHead(Head); Public = PublicAtHead(Head);
        Inward = Enumerable.Range(0, 5).Select(x => Spawn.Offset(x, 0))
            .Concat(Enumerable.Range(1, 6).Select(z => Spawn.Offset(4, -z))).ToArray();
        Outward = Enumerable.Range(1, 4).Select(x => Head.Offset(x, 0))
            .Concat(Enumerable.Range(1, 6).Select(z => Head.Offset(4, z)))
            .Concat(Enumerable.Range(1, 8).Select(x => Head.Offset(4 - x, 6))).ToArray();
        // 10 inward edges, 1s/edge. A following guest reserves its predecessor's cell only
        // after it clears: 2s spacing dominates the cohort's 1.5s requested arrival interval.
        Board = Enumerable.Range(0, 4).Select(i => (Inward.Length - 1) * 1000L + i * 2000L).ToArray();
        string source = string.Join('\n', Encoding.ASCII.GetString(Read(Stem + ".rss")).Split('\n')
            .Select(l => Regex.Replace(Regex.Replace(l.Split(';')[0].Trim(), @"^\.\w+\s+", ""), @"\s+", " "))
            .Where(l => l.Length > 0));
        void Has(string s) => Require(source.Contains(s, StringComparison.Ordinal), Label + " RSS no longer supports: " + s);
        foreach (string s in new[] { "TEST VAR_SPACELEFT\nBRANCH_Z run", "HUSH VAR_LETMEON\nADDHEAD VAR_LETMEON\nCOPY VAR_LETMEON 0\nADD VAR_ONRIDE 1\nADD VAR_SPACELEFT -1\nBRANCH_Z go",
            "COPY VAR_COUNT VAR_DURATION", "ADD VAR_COUNT -1\nBRANCH_PV runlp", "COPY VAR_RUNNING 0", "HOP VAR_LETMEOFF\nDELHEAD VAR_LETMEOFF\nCRIT_UNLOCK",
            "TEST VAR_LETMEOFF\nBRANCH_NZ wait2\nADD VAR_ONRIDE -1" }) Has(s);
        bool bugs = world == "FANTASY";
        Timeout = int.Parse(Regex.Match(source, bugs ? @"SETTIMER (\d+)" : @"ADD VAR_STARTNOW (\d+)").Groups[1].Value);
        int preStart = 0, unloadPause = 0;
        if (bugs)
        {
            Require(Capacity == 4, "Bugs TV SAM capacity must fill with this cohort");
            Has("GETTIMER 10000\nBRANCH_Z run");
            Has("COPY VAR_RUNNING 1\nWAIT 1000\nCOPY VAR_COUNT VAR_DURATION");
            Has("ADD VAR_ONRIDE -1\nWAIT 300\nBRANCH_NZ next");
            Has("WAITANIM ANIM_Start 0"); Has("WAITANIM ANIM_Main 0"); Has("WAITANIM ANIM_End 0");
            preStart = 1000; unloadPause = 300;
        }
        else
        {
            Require(Capacity == 10, "Orbiter SAM capacity must exceed this cohort");
            Has("GETTIME VAR_STARTNOW\nADD VAR_STARTNOW 10000");
            Has("SUB VAR_TEMP VAR_STARTNOW VAR_TEMP\nBRANCH_NV run");
            Has("COPY VAR_ANIMSET 0"); Has("WAITANIM ANIM_Start VAR_ANIMSET");
            Has("TRIGWAITANIM ANIM_Main VAR_ANIMSET 0"); Has("WAIT4ANIM"); Has("WAITANIM ANIM_End VAR_ANIMSET");
        }
        // Full branch resumes after CRIT_UNLOCK; Orbiter's negative timeout is strict.
        RunningAt = Board[^1] + (bugs ? 0 : Timeout) + 100;
        StartAnimationAt = RunningAt + preStart;
        var aps = new Animation(Read(Stem + ".aps"));
        long call = StartAnimationAt, animationEnd = call;
        foreach (int slot in new[] { 4, 5, 6 })
        {
            int frames = aps.Records().First(r => r.Slot == slot).DurationFrames;
            // Engine truncates frames/30 to ms; next one-shot queues behind the full end,
            // while WAITANIM/WAIT4ANIM resume 300ms early (minimum 300), on a 100ms tick.
            animationEnd = Math.Max(call, animationEnd) + frames * 1000 / 30;
            call = ((call + Math.Max(300, animationEnd - call - 300) + 99) / 100) * 100;
        }
        UnloadAt = call;
        // LIFO. First guest clears at +1s; each follower clears +2s later because both
        // current and next cells are reserved. RSS pause delays HOP, not the clearance.
        Unload = new[] { UnloadAt, UnloadAt + 1000 + unloadPause, UnloadAt + 3000 + unloadPause, UnloadAt + 5000 + unloadPause };
        print($"DERIVED {Evidence}; boards={string.Join(',', Board)}, running={RunningAt}..{UnloadAt}, Ada unload/ack/depart={Unload[^1]}/{AdaAck}/{DepartAt}");
    }
    static void Require(bool b, string message) { if (!b) throw new Exception(message); }
    public void CheckLayout(VisitorScenario s)
    {
        Require(s.RideOrigin == Origin && s.Simulation.Entrance == Spawn && s.Simulation.ExitPortal == Exit,
            "SAM/grid placement mismatch: " + Evidence);
        Require(s.RideWidth == Width && s.RideHeight == Height && s.Definition.Id == Id && s.Simulation.Machine["VAR_CAPACITY"] == Capacity,
            "SAM ride identity/capacity mismatch: " + Evidence);
        Require(s.Simulation.QueueCells.SequenceEqual(Queue) && s.LaidCells.ToHashSet().SetEquals(Public.Concat(Queue)),
            "laid path identities differ from layout: " + Evidence);
        foreach (var c in Footprint) Require(!s.Simulation.Paths.CanBuild(c), "ride did not occupy " + c);
        foreach (var c in Public) Require(s.Simulation.Paths.Kind(c) == ParkPathKind.Path, "public path missing at " + c);
        foreach (var c in Queue) Require(s.Simulation.Paths.Kind(c) == ParkPathKind.Queue, "queue missing at " + c);
    }
    public sealed record Pose(VisitorState State, ParkCell Cell, ParkCell? Next, int Progress)
    {
        public bool Visible => State is VisitorState.Walking or VisitorState.Queuing or VisitorState.Alighting or VisitorState.Leaving;
        public Vector3 Position => Next is ParkCell next ? Vector3.Lerp(ParkPaths.Centre(Cell), ParkPaths.Centre(next), Progress / 1000f) : ParkPaths.Centre(Cell);
    }
    public Pose AdaAt(long time)
    {
        if (time < 100) return new(VisitorState.Outside, default, null, 0);
        if (time < Board[0]) return Along(Inward, time, time < QueueAt ? VisitorState.Walking : VisitorState.Queuing);
        if (time < Unload[^1]) return new(VisitorState.Riding, Head, null, 0);
        if (time >= DepartAt) return new(VisitorState.Departed, Spawn, null, 0);
        return Along(Outward, Math.Max(0, time - (AdaAck - 1000)), time < AdaAck ? VisitorState.Alighting : VisitorState.Leaving);
    }
    static Pose Along(ParkCell[] route, long elapsed, VisitorState state)
    {
        int index = (int)(elapsed / 1000), progress = (int)(elapsed % 1000);
        return new(state, route[index], progress == 0 ? null : route[index + 1], progress);
    }
}
