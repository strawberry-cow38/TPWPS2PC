using System.Text;

namespace TPW.PS2.Data;

/// <summary>Explicit small park construction on SPACE/FANTASY/HALLOW. The disc supplies the terrain,
/// palette, footprint, capacity, bytecode and APS; this scenario supplies placement, paths,
/// arrivals and one-cycle duration. The shipped terrain has no placed path network.</summary>
public sealed class VisitorScenario
{
    public string World { get; }
    public string RideStem { get; }
    public string TerrainPath { get; }
    public Model Terrain { get; }
    public Model RideModel { get; }
    public Animation RideAnimation { get; }
    public RideDefinition Definition { get; }
    public ParkCell RideOrigin { get; }
    public int RideWidth { get; }
    public int RideHeight { get; }
    public VisitorSimulation Simulation { get; }
    public IReadOnlyList<ParkCell> LaidCells { get; }

    public VisitorScenario(WadArchive wad, string world, int terrainNumber = 1)
    {
        World = world.ToUpperInvariant();
        RideStem = World switch { "SPACE" => "/Rides/orbiter/orbiter", "FANTASY" => "/Rides/bugstv/bugstv",
            "HALLOW" => "/rides/candle/Candle",
            _ => throw new ArgumentException("Visitor scenario supports SPACE, FANTASY and HALLOW") };
        if (terrainNumber is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(terrainNumber));
        TerrainPath = $"/terrain/terrain_{terrainNumber}.mps";
        byte[] Read(string path) => wad.Read(wad.Find(path) ?? throw new InvalidDataException($"Missing {World}{path}"));
        Terrain = new Model(Read(TerrainPath)); RideModel = new Model(Read(RideStem + ".mps"));
        RideAnimation = new Animation(Read(RideStem + ".aps"));
        Definition = RideDefinition.Parse(Encoding.ASCII.GetString(Read(RideStem + ".sam")), World + RideStem + ".sam");
        var shape = Definition.Shape ?? throw new InvalidDataException("Missing footprint");
        RideWidth = shape.Max(r => r.Length); RideHeight = shape.Length;
        var footprint = shape.SelectMany((row, z) => row.Select((ch, x) => (ch, cell: new ParkCell(x, z))))
            .Where(p => !char.IsWhiteSpace(p.ch)).ToArray();
        ParkCell entry = footprint.Single(p => p.ch == '2').cell;
        if (entry.Z != RideHeight - 1) throw new NotSupportedException("Scenario needs a south-edge entrance");
        var paths = new ParkPaths(Terrain);
        ParkCell head = default, spawn = default, exit = default;
        ParkCell[] queue = null, publicPath = null, rideCells = null;
        bool found = false;
        foreach (var origin in paths.Cells.OrderBy(c => Math.Pow(c.X + RideWidth / 2d - paths.Field.Width / 2d, 2)
            + Math.Pow(c.Z + RideHeight / 2d - paths.Field.Height / 2d, 2)).ThenBy(c => c.Z).ThenBy(c => c.X))
        {
            head = origin.Offset(entry.X, RideHeight); spawn = head.Offset(-4, 6); exit = head.Offset(1, 0);
            queue = Enumerable.Range(0, 4).Select(z => head.Offset(0, z)).ToArray();
            publicPath = Enumerable.Range(-4, 9).Select(x => head.Offset(x, 6))
                .Concat(Enumerable.Range(4, 2).Select(z => head.Offset(0, z)))
                .Concat(Enumerable.Range(1, 4).Select(x => head.Offset(x, 0)))
                .Concat(Enumerable.Range(1, 5).Select(z => head.Offset(4, z))).Distinct().ToArray();
            rideCells = footprint.Select(p => origin.Offset(p.cell.X, p.cell.Z)).ToArray();
            if (rideCells.Concat(queue).Concat(publicPath).Any(c => !paths.CanBuild(c) || paths.Kind(c) != ParkPathKind.None)) continue;
            RideOrigin = origin; found = true; break;
        }
        if (!found)
            throw new InvalidOperationException("No eligible site for the complete ride/path layout");
        paths.Occupy(rideCells);
        foreach (var c in publicPath) paths.Lay(c, paths.MaterialIndex("jpa_squ1.ssh"));
        foreach (var c in queue) paths.Lay(c, paths.MaterialIndex("jpa_que1.ssh"));
        LaidCells = Array.AsReadOnly(publicPath.Concat(queue).ToArray());
        byte[] script = Read(RideStem + ".rse");
        Simulation = new VisitorSimulation(paths, new RseProgram(script), RideAnimation,
            Definition.UpgradeCapacity(0) ?? throw new InvalidDataException("Missing SAM capacity"), spawn, queue, exit);
        Simulation.Host.HeadSlots = RideModel.Fittings.Count(f => (f.Flags & 0x80) != 0);
        Simulation.Schedule(101, "Ada", "/Chars/Girl1a/girl1a.mps", 0);
        Simulation.Schedule(202, "Ben", "/Chars/Boy1a/boy1a.mps", 1500);
        Simulation.Schedule(303, "Cy", "/Chars/Boy2a/boy2a.mps", 3000);
        Simulation.Schedule(404, "Dee", "/Chars/Girl2a/girl2a.mps", 4500);
    }
    /// <summary>Fixed steps preserve identical behavior across caller frame rates. Time below the
    /// next 100ms boundary is retained by the caller. The scenario opens at 5s.</summary>
    public void AdvanceTo(long milliseconds)
    {
        if (milliseconds < Simulation.Time || milliseconds > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        while (Simulation.Time + VisitorSimulation.StepMilliseconds <= milliseconds)
        {
            if (Simulation.Time + VisitorSimulation.StepMilliseconds == 5000) Simulation.SetRideOpen(true);
            Simulation.Step();
        }
    }
}
