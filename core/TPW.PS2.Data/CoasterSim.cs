using System.Numerics;

namespace TPW.PS2.Data;

/// <summary>Train state `+0x258` (findings/coaster-trains.md §3).</summary>
public enum CoasterTrainState { Run = 0, Dwell = 1, Unload = 2, Wait = 3, Board = 4 }

/// <summary>A car (0x8c bytes): where it is on the ring, its speed, its riders, and the pose
/// `0x1aeb00` last wrote.</summary>
public sealed class CoasterCar
{
    public CoasterNode Node;
    /// <summary>Spline parameter on <see cref="Node"/>'s segment.</summary>
    public float T;
    /// <summary>`+0x78`, cells per tick.</summary>
    public float Speed;
    public readonly List<int> Riders = new();
    /// <summary>Position in cells; unit side, up and forward.</summary>
    public Vector3 Pos, Side, Up, Fwd;
    /// <summary>`+0x80`: where the car would be after this step on a straight line at its old speed
    /// (written by the look-ahead, read only by the test lap's statistics).</summary>
    public Vector3 Pred;
}

/// <summary>⭐ The test lap's record (`+0x14c..+0x168`), as the stats screen shows it
/// (findings/coaster-operation.md §5). Scaled per-step displacements, not physical g.</summary>
public sealed record CoasterStats(float Duration, float Length, float MaxSpeed, float Drops, float SteepestDrop,
                                  float MaxVertPos, float MaxVertNeg, float MaxLat, int RatingRow, bool Ultimate)
{
    public static readonly CoasterStats None = new(0, 0, 0, 0, 0, 0, 0, 0, 0, false);
}

/// <summary>A train (0x2f4 bytes).</summary>
public sealed class CoasterTrain
{
    public int Index;
    /// <summary>`+0x28c`-ish: position in segments from the exit node's; car 0 stands here.</summary>
    public float Pos;
    /// <summary>`+0x2b4`: the segment car 0 is on.</summary>
    public CoasterNode Node;
    /// <summary>`+0x254`: chain (lift) mode; `+0x2bc`: held behind the train ahead.</summary>
    public bool Chain, Held;
    /// <summary>`+0x2b0`: the speed last set, for the loop hold.</summary>
    public float PrevSpeed;
    public CoasterTrainState State;
    /// <summary>`+0x2a4`, `+0x2a8` (the car being unloaded or boarded), `+0x2ac`.</summary>
    public int Timer, Cursor, Attempts;
    public CoasterCar[] Cars;
}

/// <summary>⭐⭐ A ROLLER COASTER'S TRAINS, run as the console runs them (findings/coaster-trains.md,
/// coaster-operation.md). Gravity is `speed += 0.04 × height dropped` per car and the train takes
/// the mean; friction 0.1 % a step, 4 % on the approach and station segments; below 0.02 the chain
/// holds it at exactly 0.04; a loop keeps the speed it entered with; a train holds while less than
/// 0.75 segment behind the one ahead. At the station it unloads one rider every 11 ticks, then
/// boards one per 11 ticks, and leaves when the train behind is held.
///
/// ⚠ ONE STEP PER PARK TICK, NOT SCALED. The console's D is 0x4000 every tick and physics ignores
/// it; a timed state (the 0x27100 timer) lasts 10 ticks.</summary>
public sealed class CoasterSim
{
    const int D = 0x4000, TimerStart = 0x27100;

    public CoasterTrack Track { get; }
    public CoasterType Type => Track.Type;
    public List<CoasterTrain> Trains { get; } = new();

    /// <summary>`+0x9a`: 2 and 10 open (the same state), 3 closed by the player, 4 broken,
    /// 5 reliability 0 (trains freeze). 11 never occurs (coaster-operation.md §1).</summary>
    public int Status { get; private set; } = 3;

    /// <summary>The front of the queue, if anyone is there. Set by <see cref="ParkSim.AttachCoaster"/>.</summary>
    public Func<int?> TakeHead { get; set; }
    /// <summary>A rider stepped off at the exit (`0x117e08`).</summary>
    public event Action<int> Released;
    /// <summary>`+0x120`: riders aboard every train.</summary>
    public int Riders { get; private set; }
    /// <summary>Bumped whenever the winch flags change, so a viewer knows to rebuild meshes.</summary>
    public int WinchVersion { get; private set; }

    public CoasterSim(CoasterTrack track) { Track = track; }

    public void SetOpen(bool open) => Status = open ? 2 : 3;

    /// <summary>One coaster update (`0x122a48`): the trains (`0x1238c0`), then the status tick,
    /// which spawns when the ring is closed and there are none (`0x122af8`, valid NOT checked).</summary>
    public void Step()
    {
        if (!(Track.Closed && Track.Valid)) RemoveTrains();
        if (Status != 5)
            foreach (var tr in Trains.ToList())
                switch (tr.State)
                {
                    case CoasterTrainState.Run: Run(tr); break;
                    case CoasterTrainState.Dwell:
                        tr.Timer -= D;
                        if (tr.Timer <= 0) SetState(tr, CoasterTrainState.Unload);
                        break;
                    case CoasterTrainState.Unload: Unload(tr); break;
                    case CoasterTrainState.Wait: Wait(tr); break;
                    case CoasterTrainState.Board: Board(tr); break;
                }
        if (Status is 2 or 10 or 4 && Track.Closed && Trains.Count == 0) Spawn();
    }

    static void SetState(CoasterTrain tr, CoasterTrainState s)
    {
        tr.State = s;
        if (s is CoasterTrainState.Dwell or CoasterTrainState.Wait) tr.Timer = TimerStart;
    }

    /// <summary>`0x1224c8`: every rider to the exit at once, then no trains.</summary>
    public void RemoveTrains()
    {
        foreach (var tr in Trains)
            foreach (var car in tr.Cars)
            {
                foreach (var g in car.Riders) { Riders--; Released?.Invoke(g); }
                car.Riders.Clear();
            }
        Trains.Clear();
    }

    /// <summary>`0x1230b0`: mark the lift, then `clamp((pylons/3 + 2)/cars, 2, 6)` trains, one
    /// segment apart back from the station, train 0 in front, all in chain mode at rest.</summary>
    public void Spawn()
    {
        MarkLift();
        RemoveTrains();
        int nc = Type.CarsPerTrain;
        int count = (Track.Pylons.Count / 3 + 2) / nc;
        if (!(1 < count)) count = 2;
        if (!(count < 7)) count = 6;
        float p = Track.Pylons.Count + 2.0f;
        for (int i = 0; i < count; i++)
        {
            var tr = NewTrain(i);
            Trains.Add(tr);
            PlaceCars(tr, p);
            p -= 1.0f;
            if (p < 0) p += Track.SegmentCount;
        }
    }

    CoasterTrain NewTrain(int index)
    {
        var tr = new CoasterTrain { Index = index, Chain = true, State = CoasterTrainState.Run, Cars = new CoasterCar[Type.CarsPerTrain] };
        for (int k = 0; k < tr.Cars.Length; k++) tr.Cars[k] = new CoasterCar();
        return tr;
    }

    /// <summary>⭐ `0x1239d8`: one train from position 1.0, run on physics alone; wherever it is in
    /// chain mode the track gets the chain texture. Plus the station and departure segments always.</summary>
    public void MarkLift()
    {
        if (!(Track.Closed && Track.Valid)) return;
        for (var n = Track.Exit; n != null && n != Track.Entry; n = n.Next) CoasterTrack.ClearWinch(n);
        Track.MarkWinch(0, 2.0f);
        var saved = Trains.ToList();
        Trains.Clear();
        var tr = NewTrain(0);
        Trains.Add(tr);
        PlaceCars(tr, 1.0f);
        for (int guard = 0; guard < 200000; guard++)
        {
            float was = tr.Pos;
            Run(tr);
            if (!(tr.Pos > was)) break;
            if (tr.Chain) Track.MarkWinch(was, tr.Pos);
        }
        Trains.Clear();
        Trains.AddRange(saved);
        WinchVersion++;
    }

    /// <summary>⭐ The physics step, state 0 (`0x1b0518`).</summary>
    void Run(CoasterTrain tr)
    {
        int L = Track.SegmentCount, nc = tr.Cars.Length;
        if (Trains.Count >= 2)
        {
            var ahead = Trains[(tr.Index - 1 + Trains.Count) % Trains.Count];
            float gap = ahead.Pos - tr.Pos;
            while (gap < 0) gap += L;
            while (gap > L) gap -= L;
            if (gap < 0.75f)
            {
                foreach (var c in tr.Cars) c.Speed = 0;
                PlaceCars(tr, tr.Pos);
                tr.Held = true;
                return;
            }
        }
        tr.Held = false;
        float sum = 0;
        foreach (var c in tr.Cars) sum += CarGravity(c);
        float m = sum / nc;
        float f = tr.Node == Track.Entry || tr.Node == Track.Exit ? 0.04f : 0.001f;
        float v = m - m * f;
        if (!tr.Chain) { if (v <= 0.02f) { v = 0.04f; tr.Chain = true; } }
        else if (v > 0.04f) tr.Chain = false;
        else v = 0.04f;
        if (tr.Node.Kind == CoasterNodeKind.Loop && v >= 0.08f) v = tr.PrevSpeed;
        foreach (var c in tr.Cars) c.Speed = v;
        tr.PrevSpeed = v;
        PlaceCars(tr, tr.Pos + v / tr.Node.Length);
        if (tr.Pos >= L + 0.75f)
        {
            tr.Cursor = 0;
            SetState(tr, CoasterTrainState.Dwell);
            tr.Pos -= L;
        }
    }

    /// <summary>`0x1aed90`. ⚠ Two quirks kept on purpose: crossing into the next segment subtracts
    /// the NEXT segment's length and then divides by the CURRENT one's.</summary>
    float CarGravity(CoasterCar car)
    {
        var node = car.Node;
        float len0 = node.Length;
        var (p0, _, fwd) = CoasterTrack.Spline(node, car.T);
        if (fwd.LengthSquared() > 0) fwd = Vector3.Normalize(fwd);
        car.Pred = p0 + car.Speed * fwd;
        float d = car.T * len0 + car.Speed;
        for (int guard = 0; len0 <= d && guard < 64 && node.Next != null; guard++)
        {
            node = node.Next;
            car.Node = node;
            d -= node.Length;
        }
        var p1 = CoasterTrack.Spline(node, len0 > 0 ? d / len0 : 0).Pos;
        car.Speed += (p0.Y - p1.Y) * 0.04f;
        return car.Speed;
    }

    /// <summary>⭐ `0x122d48`: the test lap. One train from position 1.0 on the real physics, car 0's
    /// statistics every step (`0x1aeec0`), then the service trains again and the per-segment figures
    /// over pylons 0..n−1 (`0x19d5f0`), then the rating (`0x122ed0`).
    /// ⚠ The console seeds the "did the position increase" test from the CALLER's `$f21`, never
    /// initialised here; the port seeds it with the start position, which is the intended reading.</summary>
    public CoasterStats TestLap()
    {
        if (!(Track.Closed && Track.Valid)) return CoasterStats.None;
        RemoveTrains();
        var tr = NewTrain(0);
        Trains.Add(tr);
        PlaceCars(tr, 1.0f);
        float duration = 0, maxSpeed = 0, vPos = 0, vNeg = 0, lat = 0;
        float last = tr.Pos;
        for (int guard = 0; guard < 200000; guard++)
        {
            Run(tr);
            var c = tr.Cars[0];
            var delta = c.Pos - c.Pred;
            duration += 0.033333335f;
            maxSpeed = Math.Max(maxSpeed, c.Speed * 175f);
            float vert = Vector3.Dot(c.Up, delta) * 100f;
            vPos = Math.Max(vPos, vert);
            vNeg = Math.Min(vNeg, vert * 0.25f);
            lat = Math.Max(lat, MathF.Abs(Vector3.Dot(c.Side, delta)) * 20f);
            if (!(last < tr.Pos)) break;
            last = tr.Pos;
        }
        RemoveTrains();
        Spawn();
        float length = 0, drops = 0, steep = 0;
        for (var n = Track.Exit.Next; n != null && n != Track.Entry; n = n.Next)
        {
            if (n == Track.Exit || n.Kind == CoasterNodeKind.Loop) continue;
            for (int i = 1; i <= 16; i++)
            {
                var a = n.Samples[i - 1].Pos; var b = n.Samples[i].Pos;
                float dy = b.Y - a.Y;
                if (dy >= 0) continue;
                float dx = b.X - a.X, dz = b.Z - a.Z;
                int deg = (int)(Math.Atan2(-dy, Math.Sqrt(dx * dx + dz * dz)) * 360.0 * 0.5 / (float)Math.PI);
                steep = Math.Max(steep, deg);
            }
            length += n.Length;
            var prev = n.Prev ?? n; var pp = prev.Prev ?? prev;
            if (StackHeight(n) - StackHeight(prev) < 256 && StackHeight(prev) - StackHeight(pp) > 256) drops++;
        }
        var (row, ultimate) = Rate(lat, maxSpeed, drops);
        return new CoasterStats(duration, length, maxSpeed, drops, steep, vPos, vNeg, lat, row, ultimate);
    }

    /// <summary>`0x19a1d8`: a node's height plus the stack below it.</summary>
    static int StackHeight(CoasterNode n) { int h = 0; for (var s = n; s != null; s = s.Below) h += s.Height; return h; }

    /// <summary>`0x2acda0`, [lateral·9 + speed·3 + drops]: the rating's text row.</summary>
    static readonly int[] RatingRows =
    {
        0x156, 0x3b, 0x223,   0x5d, 0x2bc, 0x107,   0x98, 0x290, 0x141,
        0x2ca, 0xf8, 0x3a7,   0x12a, 0x329, 0x1fa,  0x1f1, 0x388, 0x2bf,
        0x10b, 0x41c, 0x1d7,  0x25, 0x278, 0xd5,    0x56, 0x24f, 0xfb,
    };

    /// <summary>`0x122ed0`: "Ultimate Rollercoaster" (row 0x256) for lateral under 0.5, speed 55–70
    /// and 2 or 3 drops; otherwise one of 27 texts.</summary>
    public static (int Row, bool Ultimate) Rate(float lat, float speed, float drops)
    {
        if (lat < 0.5f && 55.0f <= speed && speed <= 70.0f && 1.0f < drops && drops < 4.0f) return (0x256, true);
        int L = lat < 0.5f ? 0 : lat < 1.0f ? 1 : 2;
        int S = speed <= 50 ? 0 : speed <= 75 ? 1 : 2;
        int D = drops <= 1 ? 0 : drops <= 6 ? 1 : 2;
        return (RatingRows[L * 9 + S * 3 + D], false);
    }

    /// <summary>`0x1b19e0`: car 0 at the train position, each further car `spacing` behind in
    /// t × length, stepping back across segments.</summary>
    void PlaceCars(CoasterTrain tr, float p)
    {
        tr.Pos = p;
        var node = Track.Exit;
        for (int guard = 0; p >= 1.0f && guard < 4096 && node.Next != null; guard++) { p -= 1.0f; node = node.Next; }
        tr.Node = node;
        float d = p * node.Length;
        foreach (var car in tr.Cars)
        {
            PoseCar(car, node, node.Length > 0 ? d / node.Length : 0);
            d -= Type.CarSpacing;
            for (int guard = 0; d < 0 && node.Prev != null && guard < 64; guard++) { node = node.Prev; d += node.Length; }
        }
    }

    /// <summary>`0x1aeb00`: up = fwd × side, side = up × fwd, all normalised.</summary>
    static void PoseCar(CoasterCar car, CoasterNode node, float t)
    {
        var (pos, side, fwd) = CoasterTrack.Spline(node, t);
        var up = Vector3.Cross(fwd, side);
        side = Vector3.Cross(up, fwd);
        car.Pos = pos;
        car.Side = side.LengthSquared() > 0 ? Vector3.Normalize(side) : Vector3.UnitX;
        car.Up = up.LengthSquared() > 0 ? Vector3.Normalize(up) : Vector3.UnitY;
        car.Fwd = fwd.LengthSquared() > 0 ? Vector3.Normalize(fwd) : Vector3.UnitZ;
        car.Node = node;
        car.T = t;
    }

    /// <summary>State 2 (`0x1b16f0`): one rider off the car at the cursor, or on to the next car if
    /// it was empty; every pop and every empty check costs a dwell.</summary>
    void Unload(CoasterTrain tr)
    {
        if (tr.Cursor < tr.Cars.Length)
        {
            var car = tr.Cars[tr.Cursor];
            if (car.Riders.Count > 0)
            {
                int g = car.Riders[0];
                car.Riders.RemoveAt(0);
                Riders--;
                Released?.Invoke(g);
            }
            else tr.Cursor++;
            SetState(tr, CoasterTrainState.Dwell);
            return;
        }
        tr.Cursor = 0;
        tr.Attempts = 40;
        SetState(tr, CoasterTrainState.Wait);
    }

    /// <summary>State 3 (`0x1b1778`).</summary>
    void Wait(CoasterTrain tr)
    {
        tr.Timer -= D;
        if (tr.Timer <= 0)
        {
            tr.Attempts--;
            SetState(tr, tr.Attempts < 0 ? CoasterTrainState.Run : CoasterTrainState.Board);
            return;
        }
        if (Behind(tr).Held) SetState(tr, CoasterTrainState.Run);
    }

    CoasterTrain Behind(CoasterTrain tr) => Trains[(tr.Index + 1) % Trains.Count];

    /// <summary>State 4 (`0x1b1858`): one guest into the car at the cursor if the front of the queue
    /// is there; otherwise leave if the train behind is held.</summary>
    void Board(CoasterTrain tr)
    {
        if (tr.Cursor < tr.Cars.Length)
        {
            int? head = TakeHead?.Invoke();
            if (head is { } g)
            {
                if (Status != 4) Status = 10;
                var car = tr.Cars[tr.Cursor];
                car.Riders.Add(g);
                Riders++;
                if (Type.Seats <= car.Riders.Count) tr.Cursor++;
                SetState(tr, CoasterTrainState.Wait);
                return;
            }
            if (!Behind(tr).Held) { SetState(tr, CoasterTrainState.Wait); return; }
        }
        SetState(tr, CoasterTrainState.Run);
    }
}
