using System.Numerics;

namespace TPW.PS2.Data;

public enum VisitorState { Outside, Walking, Queuing, Riding, Alighting, Leaving, Departed }

public sealed class ParkVisitor
{
    public int Id { get; }
    public string Name { get; }
    public string ModelPath { get; }
    public long ArrivalTime { get; }
    public VisitorState State { get; internal set; }
    public ParkCell Cell { get; internal set; }
    public ParkCell? NextCell { get; internal set; }
    /// <summary>Exact integer motion: 1000 units per cell, 100 units per 100ms step.</summary>
    public int EdgeProgress { get; internal set; }
    public bool Blocked { get; internal set; }
    internal long LastMoveTime;
    public Vector3 Position => NextCell is ParkCell next
        ? Vector3.Lerp(ParkPaths.Centre(Cell), ParkPaths.Centre(next), EdgeProgress / 1000f)
        : ParkPaths.Centre(Cell);
    internal ParkVisitor(int id, string name, string modelPath, long arrival)
    { Id = id; Name = name; ModelPath = modelPath; ArrivalTime = arrival; }
}

/// <summary>One ride and a finite cohort. AI owns only the input mailboxes and park movement;
/// RSSE owns occupancy, stack, loading deadlines, running and APS playback. All time is supplied
/// by the caller. No original guest decision-making or engine walking-service parity is claimed.</summary>
public sealed class VisitorSimulation
{
    public const int StepMilliseconds = 100;
    public ParkPaths Paths { get; }
    public RseMachine Machine { get; }
    public RsePreviewHost Host { get; }
    public ParkCell Entrance { get; }
    public ParkCell ExitPortal { get; }
    /// <summary>Front to back, adjacent queue cells. Front is the boarding portal.</summary>
    public IReadOnlyList<ParkCell> QueueCells { get; }
    readonly List<ParkVisitor> _visitors = new();
    readonly List<int> _queue = new();
    public IReadOnlyList<ParkVisitor> Visitors => _visitors.AsReadOnly();
    public IReadOnlyList<int> Queue => _queue.AsReadOnly();
    public long Time { get; private set; }
    public int? OfferedGuest { get; private set; }
    int? _alighting;
    public string Fault { get; private set; }
    public sealed record Transition(long Time, int GuestId, string Name, string Action, ParkCell Cell,
        int LetMeOn, int LetMeOff, int OnRide, int SpaceLeft, int Running, int StartNow);
    public event Action<Transition> Changed;

    public VisitorSimulation(ParkPaths paths, RseProgram program, Animation animation, int capacity,
        ParkCell entrance, IReadOnlyList<ParkCell> queueCells, ParkCell exitPortal)
    {
        if (capacity <= 0 || capacity > program.StackSize) throw new ArgumentOutOfRangeException(nameof(capacity));
        Paths = paths; Entrance = entrance; ExitPortal = exitPortal;
        QueueCells = Array.AsReadOnly(queueCells.ToArray());
        if (QueueCells.Count == 0 || QueueCells.Distinct().Count() != QueueCells.Count
            || QueueCells.Any(c => paths.Kind(c) != ParkPathKind.Queue)
            || QueueCells.Zip(QueueCells.Skip(1)).Any(p => Math.Abs(p.First.X - p.Second.X) + Math.Abs(p.First.Z - p.Second.Z) != 1)
            || entrance == exitPortal || !paths.Open(entrance) || !paths.Open(exitPortal))
            throw new ArgumentException("Invalid queue or park portals");
        if (paths.Route(entrance, QueueCells[^1], c => paths.Open(c) || c == QueueCells[^1]) == null
            || paths.Route(exitPortal, entrance, c => paths.Open(c)) == null)
            throw new ArgumentException("Disconnected park portals");
        Host = new RsePreviewHost(animation); Machine = new RseMachine(program, Host);
        foreach (string name in new[] { "VAR_LETMEON", "VAR_LETMEOFF", "VAR_CAPACITY", "VAR_DURATION",
            "VAR_ONRIDE", "VAR_RIDECLOSED", "VAR_BROKEN", "VAR_RUNNING", "VAR_SPACELEFT", "VAR_STARTNOW" })
            program.VariableIndex(name);
        Machine["VAR_CAPACITY"] = capacity; Machine["VAR_DURATION"] = 1; Machine["VAR_RIDECLOSED"] = 1;
        Machine.RunSlice(0);
    }
    public ParkVisitor Schedule(int id, string name, string modelPath, long arrival)
    {
        if (id <= 0 || _visitors.Any(v => v.Id == id) || string.IsNullOrWhiteSpace(name) || arrival < Time)
            throw new ArgumentException("Guest needs a unique positive ID, a name and a future arrival");
        var guest = new ParkVisitor(id, name, modelPath, arrival); _visitors.Add(guest); return guest;
    }
    public void SetRideOpen(bool open) => Machine["VAR_RIDECLOSED"] = open ? 0 : 1;
    ParkVisitor Guest(int id) => _visitors.SingleOrDefault(g => g.Id == id)
        ?? throw new InvalidDataException($"Script returned unknown guest ID {id}");
    void Report(ParkVisitor g, string action) => Changed?.Invoke(new(Time, g.Id, g.Name, action, g.Cell,
        Machine["VAR_LETMEON"], Machine["VAR_LETMEOFF"], Machine["VAR_ONRIDE"],
        Machine["VAR_SPACELEFT"], Machine["VAR_RUNNING"], Machine["VAR_STARTNOW"]));
    static bool OnPath(ParkVisitor g) => g.State is VisitorState.Walking or VisitorState.Queuing
        or VisitorState.Alighting or VisitorState.Leaving;
    bool Free(ParkCell c, ParkVisitor except) => _visitors.All(g => g == except || !OnPath(g)
        || (g.Cell != c && g.NextCell != c));

    public void Step()
    {
        if (Fault != null) throw new InvalidOperationException(Fault);
        try { StepCore(); }
        catch (Exception ex) { Fault = $"Visitor simulation t={Time}: {ex.Message}"; throw new InvalidOperationException(Fault, ex); }
    }
    void StepCore()
    {
        Time = checked(Time + StepMilliseconds);
        foreach (var guest in _visitors.Where(OnPath))
            if (!Paths.Walkable(guest.Cell)) throw new InvalidDataException($"Path removed under guest {guest.Name}");
        // A pending offer is withdrawn on closure; never touch the guest stack to do this.
        if (OfferedGuest is int pending && (Machine["VAR_RIDECLOSED"] != 0 || Machine["VAR_BROKEN"] != 0))
        { Machine["VAR_LETMEON"] = 0; OfferedGuest = null; Report(Guest(pending), "offer withdrawn"); }
        foreach (var g in _visitors)
        {
            g.Blocked = false;
            if (g.State == VisitorState.Outside && Time >= g.ArrivalTime && Free(Entrance, g))
            { g.Cell = Entrance; g.State = VisitorState.Walking; Report(g, "spawn"); }
        }
        // Queue order is arrival at the tail, not numerical guest ID or registration order.
        foreach (var g in _visitors.Where(g => g.State is VisitorState.Walking or VisitorState.Leaving or VisitorState.Alighting))
        {
            if (g.State == VisitorState.Walking)
            {
                Move(g, QueueCells[^1], c => Paths.Open(c) || c == QueueCells[^1]);
                if (At(g, QueueCells[^1])) { g.State = VisitorState.Queuing; _queue.Add(g.Id); Report(g, "queue"); }
            }
            else
            {
                Move(g, Entrance, c => Paths.Open(c));
                if (g.State == VisitorState.Alighting && g.Cell != ExitPortal)
                {
                    if (_alighting != g.Id || Machine["VAR_LETMEOFF"] != g.Id)
                        throw new InvalidDataException($"Lost unload ownership for {g.Name} ({g.Id})");
                    Machine["VAR_LETMEOFF"] = 0; _alighting = null;
                    g.State = VisitorState.Leaving; Report(g, "unload acknowledged");
                }
                if (At(g, Entrance)) { g.State = VisitorState.Departed; Report(g, "depart"); }
            }
        }
        for (int i = 0; i < _queue.Count; i++)
        {
            var g = Guest(_queue[i]);
            if (g.Id != OfferedGuest)
                Move(g, QueueCells[Math.Min(i, QueueCells.Count - 1)], c => QueueCells.Contains(c));
        }
        if (OfferedGuest == null && _queue.Count > 0 && Machine["VAR_RIDECLOSED"] == 0
            && Machine["VAR_BROKEN"] == 0 && At(Guest(_queue[0]), QueueCells[0]))
        {
            if (Machine["VAR_LETMEON"] != 0) throw new InvalidDataException("Boarding mailbox already occupied");
            OfferedGuest = _queue[0]; Machine["VAR_LETMEON"] = OfferedGuest.Value;
            Report(Guest(OfferedGuest.Value), "offer");
        }
        Machine.RunSlice(Time);
        if (OfferedGuest is int offered && Machine["VAR_LETMEON"] == 0)
        {
            if (!Machine.GuestIds.Contains(offered))
                throw new InvalidDataException($"Boarding identity lost: {Guest(offered).Name} ({offered}) is absent from RSSE HUSH stack");
            var g = Guest(offered); _queue.RemoveAt(0); g.State = VisitorState.Riding;
            OfferedGuest = null; Report(g, "board");
        }
        int off = Machine["VAR_LETMEOFF"];
        if (off != 0 && _alighting == null)
        {
            var g = Guest(off);
            if (g.State != VisitorState.Riding) throw new InvalidDataException($"Script unloaded {g.Name} from {g.State}");
            if (Free(ExitPortal, g))
            {
                if (!Paths.Walkable(ExitPortal)) throw new InvalidDataException("Exit portal is no longer walkable");
                _alighting = off; g.State = VisitorState.Alighting; g.Cell = ExitPortal;
                g.NextCell = null; g.EdgeProgress = 0; Report(g, "unload requested");
            }
        }
    }
    static bool At(ParkVisitor g, ParkCell cell) => g.Cell == cell && g.NextCell == null;
    void Move(ParkVisitor g, ParkCell target, Func<ParkCell, bool> allowed)
    {
        if (At(g, target) || g.LastMoveTime == Time) return;
        if (g.NextCell == null)
        {
            var route = Paths.Route(g.Cell, target, allowed);
            if (route == null) { g.Blocked = true; return; }
            var next = route[1];
            if (!Free(next, g)) { g.Blocked = true; return; }
            g.NextCell = next;
        }
        if (!Paths.Walkable(g.Cell) || !Paths.Walkable(g.NextCell.Value))
            throw new InvalidDataException($"Path removed under moving guest {g.Name}");
        g.LastMoveTime = Time;
        g.EdgeProgress += 100;
        if (g.EdgeProgress == 1000)
        { g.Cell = g.NextCell.Value; g.NextCell = null; g.EdgeProgress = 0; Report(g, "step"); }
    }
}
