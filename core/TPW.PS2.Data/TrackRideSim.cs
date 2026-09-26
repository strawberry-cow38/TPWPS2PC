namespace TPW.PS2.Data;

/// <summary>Ride status (+0xA2) as the track ride uses it (findings/track-ride-operation.md §3).</summary>
public enum TrackRideStatus : byte
{
    Running = 2,
    /// <summary>The loop is open, or the player closed the ride. Nobody boards or gets off.</summary>
    Closed = 3,
    Loading = 10,
    Unloading = 11,
}

/// <summary>A car on a track ride: one 0x98-byte object (findings/track-ride-cars.md §2). Fields
/// keep the console's widths where the arithmetic depends on them.</summary>
public sealed class TrackCar
{
    /// <summary>The guest riding it, or null for a race kart.</summary>
    public int? Guest { get; internal set; }
    public bool IsKart { get; internal set; }
    /// <summary>+0x89: its slot at creation.</summary>
    public int Index { get; internal set; }
    /// <summary>+0x50: distance along the track, 256 per piece. u16.</summary>
    public ushort Distance { get; internal set; }
    /// <summary>+0x54: total distance, the sort key. Karts only: boats never write it (§4), and the
    /// console's allocator does not zero, so a boat's is garbage. Zero here, which gives creation order.</summary>
    public int Total { get; internal set; }
    /// <summary>+0x58: laps, starting at −1 so the first crossing of the line makes 0.</summary>
    public sbyte Lap { get; internal set; } = -1;
    /// <summary>+0x5A: 0..256 across the track, 128 the centre line, 0 on sample point P1.</summary>
    public short Lateral { get; internal set; } = 0x7f;
    /// <summary>+0x4A: heading, 4096 a turn, kept in 0..0xFFF.</summary>
    public short Heading { get; internal set; }
    /// <summary>+0x48: speed. Karts in distance units per tick, boats in quarter units.</summary>
    public sbyte Speed { get; internal set; }
    /// <summary>+0x49: the kart's current max speed, the boat's target speed.</summary>
    public byte Target { get; internal set; }
    /// <summary>+0x0C: rank, 1 = the leader.</summary>
    public int Rank { get; internal set; }
    /// <summary>+0x88: done its laps; the unload pass takes it.</summary>
    public bool Finished { get; internal set; }
    /// <summary>+0x35: kart colour 0..3.</summary>
    public int Colour { get; internal set; }

    // Kart (class B)
    internal byte BaseMax, BaseAccel, Accel, State, Timer;
    internal bool Aggressive;
    // Boat (class A)
    internal short PushBack, Wobble, WobbleRate;

    /// <summary>+0x39, the kart state: 0 grid, 1 yield, 2 race, 3 overtake, 4 block, 5 spin, 6 stall.</summary>
    public int KartState => State;
    public override string ToString() => $"{(IsKart ? "kart" : "boat")}#{Index} d={Distance} lap={Lap} lat={Lateral} v={Speed}";
}

/// <summary>⭐⭐ A track ride's native operation, which on PS2 replaces the stubbed `BUMP` script:
/// the status machine, boarding one guest per car, laps, unloading, and the two car classes.
/// Sources: findings/track-ride-operation.md and findings/track-ride-cars.md.
///
/// One <see cref="Step"/> is one ride update (vtable +0x3C, 0x200410): cars first (0x2023B0), then
/// the status tick (0x1E5138). The tick counter is the park's update counter (0x1C4930), passed in,
/// because boarding and unloading run on its multiples of 20 and 10.
///
/// Not here yet: wear, breakdown and repair (the port has no mechanics), and the drive-it-yourself
/// kart race.</summary>
public sealed class TrackRideSim
{
    readonly List<TrackCar> _cars = new();
    readonly Random _rng;

    public TrackLayout Track { get; }
    public bool Karts { get; }
    public TrackRideStatus Status { get; private set; } = TrackRideStatus.Closed;
    public IReadOnlyList<TrackCar> Cars => _cars;
    /// <summary>+0x128. One guest per car, so this is the car count.</summary>
    public int Riders => _cars.Count(c => c.Guest != null);
    /// <summary>+0x12A: counts the status-2 updates.</summary>
    public int RunTimer { get; private set; }

    /// <summary>Settings (0x116120 defaults): Speed 50 (1..100), Capacity 4 (1..8), Duration 5 (1..10).
    /// Duration is the lap target (vtable +0x304 = ride +0xF8), read live.</summary>
    public int Speed { get; set; } = 50;
    public int Capacity { get; set; } = 4;
    public int Duration { get; set; } = 5;

    /// <summary>Asked on a boarding tick: the guest at the head of the queue if they are standing at
    /// the front (guest state 0x12), else null. The sim then owns them until <see cref="Released"/>.</summary>
    public Func<int?> TakeHead { get; set; } = () => null;
    /// <summary>A guest got off (0x117E08: placed on the exit, state 0x16).</summary>
    public event Action<int> Released;
    /// <summary>A guest boarded a new car.</summary>
    public event Action<int, TrackCar> Boarded;

    /// <summary>0x2EE528[world*2 + park]: nonzero is karts. JUNGLE park 1 karts, park 2 boats; the
    /// other three worlds the other way round.</summary>
    public static bool KartsFor(int world, int park) => world == 0 ? park == 0 : park == 1;

    /// <param name="karts">Which car class, when the caller knows the ride (its station folder). The
    /// console reads the per-park table because each park offers exactly one track ride; a port that
    /// lets either be built anywhere must not give a water ride karts.</param>
    public TrackRideSim(TrackLayout track, int seed = 0, bool? karts = null)
    {
        Track = track ?? throw new ArgumentNullException(nameof(track));
        Karts = karts ?? KartsFor(track.Ground.World, track.Ground.Park);
        _rng = new Random(seed);
        Rebuilt();
    }

    /// <summary>`rand(n)` (0x1448E0): 0..n−1.</summary>
    int Rand(int n) => n <= 0 ? 0 : _rng.Next(n);

    /// <summary>The end of 0x2009C0, after the track changed: every car is unloaded first, then
    /// status 2 if the loop is closed, else 3.</summary>
    public void Rebuilt()
    {
        while (_cars.Count > 0) Remove(0);
        SetStatus(Track.Closed ? TrackRideStatus.Running : TrackRideStatus.Closed);
    }

    /// <summary>0x2016E0 then 0x2009C0: add a waypoint and re-lay the track, which unloads everyone.</summary>
    public bool AddWaypoint(ParkCell c)
    {
        bool ok = Track.Add(c);
        Rebuilt();
        return ok;
    }

    /// <summary>0x2017D0 then 0x2009C0. Returns the removed leg's piece count.</summary>
    public int RemoveLastWaypoint()
    {
        int k = Track.RemoveLast();
        Rebuilt();
        return k;
    }

    /// <summary>The player's Open/Close (0x1E2738 sets 2, 0x1E2768 sets 3). Opening an open loop
    /// drops straight back to 3 on the next update (0x200518). ⚠ A closed ride keeps its cars driving
    /// and nobody gets off until it reopens: native, and unlike the script.</summary>
    public void SetOpen(bool open) => SetStatus(open ? TrackRideStatus.Running : TrackRideStatus.Closed);

    void SetStatus(TrackRideStatus s)
    {
        Status = s;
        if (s == TrackRideStatus.Running) RunTimer = 0; // 0x1164A8
    }

    /// <summary>0x202188: Excitement from the record's base (75 or 80 by ride): the track's weight sum
    /// halved, times the speed and duration factors each clamped to 0.75..1.25, capped at 100.</summary>
    public int Excitement(int recordBase)
    {
        if (recordBase == 0) return 0;
        int e = recordBase + (Math.Min(Track.Weight, 255) >> 1);
        int sf = Math.Clamp((Speed << 12) / 100, 0xc00, 0x1400);
        int df = Math.Clamp((Duration << 12) / 5, 0xc00, 0x1400);
        return Math.Min(100, e * (sf * df >> 12) >> 12);
    }

    /// <summary>One ride update at park tick <paramref name="tick"/>.</summary>
    public void Step(uint tick)
    {
        if (_cars.Count > 0 && Status != TrackRideStatus.Loading && Track.Length > 0) StepCars();
        switch (Status)
        {
            case TrackRideStatus.Loading: // 0x200448
                if (tick % 20 != 0) break;
                if (Riders < Capacity)
                {
                    var g = TakeHead();
                    if (g != null) Board(g.Value);
                }
                else SetStatus(TrackRideStatus.Running);
                break;
            case TrackRideStatus.Running: // 0x200518
                if (!Track.Closed) { SetStatus(TrackRideStatus.Closed); break; }
                if (++RunTimer >= 2 * Duration) SetStatus(TrackRideStatus.Unloading);
                break;
            case TrackRideStatus.Unloading: // 0x2005C8
                if (tick % 10 != 0) break;
                if (Riders == 0) { SetStatus(TrackRideStatus.Loading); break; }
                // i advances after a removal, so the car shifted into slot i waits a pass (kept).
                for (int i = 0; i < _cars.Count; i++)
                    if (_cars[i].Finished) Remove(i);
                break;
        }
    }

    /// <summary>0x201AC0: the "room in the last car" test 0x205638 is `return 0`, so every guest
    /// gets a new car.</summary>
    void Board(int guest)
    {
        var car = new TrackCar { Guest = guest, IsKart = Karts };
        int index = _cars.Count;
        _cars.Add(car);
        if (Karts) KartReset(car, index); else BoatInit(car, index);
        Boarded?.Invoke(guest, car);
    }

    /// <summary>0x2022A8: unload one car's guest to the exit and delete it.</summary>
    void Remove(int i)
    {
        var car = _cars[i];
        _cars.RemoveAt(i);
        if (car.Guest is int g) Released?.Invoke(g);
    }

    /// <summary>0x2023B0: sort by total distance, rank, then every hook and every step with the
    /// sorted neighbours behind and ahead, cyclic.</summary>
    void StepCars()
    {
        int n = _cars.Count;
        if (n == 1)
        {
            Hook(_cars[0], null, null);
            StepCar(_cars[0], null, null);
            return;
        }
        var s = _cars.ToArray();
        // Exchange sort, signed and strict: ties keep array order.
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (s[j].Total < s[i].Total) (s[i], s[j]) = (s[j], s[i]);
        for (int i = 0; i < n; i++) s[i].Rank = n - i;
        for (int i = 0; i < n; i++) Hook(s[i], s[(i + n - 1) % n], s[(i + 1) % n]);
        for (int i = 0; i < n; i++) StepCar(s[i], s[(i + n - 1) % n], s[(i + 1) % n]);
    }

    void Hook(TrackCar c, TrackCar behind, TrackCar ahead)
    {
        if (c.IsKart) KartHook(c, behind, ahead); else BoatHook(c, ahead);
    }

    void StepCar(TrackCar c, TrackCar behind, TrackCar ahead)
    {
        if (c.IsKart) KartStep(c, behind, ahead); else BoatStep(c);
    }

    /// <summary>Ease <paramref name="from"/> toward the track yaw by at most <paramref name="rate"/>,
    /// with the console's wrap: a gap of 0x7FF or more lifts the smaller side by a turn.</summary>
    static short Ease(int from, int to, int rate)
    {
        if (Math.Abs(to - from) >= 0x7ff) { if (to < from) to += 0x1000; else from += 0x1000; }
        int step = Math.Clamp(to - from, -rate, rate);
        return (short)((from + step) & 0xfff);
    }

    // ---------------------------------------------------------------- boats (0x36BFB8)

    /// <summary>0x2053A0 then 0x203360.</summary>
    void BoatInit(TrackCar c, int i)
    {
        c.Index = i;
        c.Lap = -1;
        c.Speed = 0;
        c.WobbleRate = (short)(Rand(11) + 1);
        c.Wobble = 0;
        c.PushBack = 0;
        c.Target = (byte)(Speed / 20);
        c.Lateral = 0x7f;
        c.Distance = (ushort)(Track.Length - 200 * (i + 1));
        c.Heading = (short)(Track.Yaw(c.Distance) & 0xfff);
    }

    /// <summary>0x203488: spacing to the sort neighbour ahead. The distance gap is a raw u16
    /// difference with no wrap handling (kept). The crossing rule that follows is dead on PS2: its
    /// partner field +0xAC is looked up with cell coordinates where 0x14A420 wants world units, so it
    /// is always null (findings/track-ride-geometry.md Q3), and boats pass through at crossings.</summary>
    static void BoatHook(TrackCar c, TrackCar ahead)
    {
        if (ahead == null) return;
        int g = Math.Abs(ahead.Distance - c.Distance);
        if (g < 25) { c.Speed = 0; c.PushBack = -10; }
        else if (g < 200) c.PushBack = -5;
    }

    /// <summary>0x203540. The two tilt axes are left out: they are computed but never drawn.</summary>
    void BoatStep(TrackCar c)
    {
        int len = Track.Length;
        int d = c.Distance;
        if (c.PushBack != 0) { d = (ushort)(d + c.PushBack); c.PushBack = 0; }
        d = (ushort)(d + (c.Speed >> 2));
        // ⚠ Pushed back across the line, d wraps to ~65530 and is then taken mod length: a lap and a
        // jump. That is the console's arithmetic; kept.
        if (d >= len)
        {
            c.Lap++;
            d %= len;
            if (Status != TrackRideStatus.Running && c.Lap >= Duration) c.Finished = true;
        }
        c.Distance = (ushort)d;
        if (c.Speed < c.Target) c.Speed++;
        else
        {
            c.Target = (byte)(60 - 2 * Rand(11));
            c.Speed = (sbyte)(c.Target - Rand(11));
        }
        int lt = 0x80 + c.Wobble;
        c.Lateral = (short)(c.Lateral + Math.Clamp(lt - c.Lateral, -2, 2));
        c.Wobble += c.WobbleRate;
        if (c.Wobble < -10) { c.Wobble = -10; c.WobbleRate = (short)(Rand(11) + 1); }
        else if (c.Wobble > 10) { c.Wobble = 10; c.WobbleRate = (short)(-Rand(11) - 1); }
        c.Heading = Ease(c.Heading, Track.Yaw(c.Distance + 0x100), 0x14);
    }

    // ---------------------------------------------------------------- karts (0x36C0A0)

    /// <summary>0x203F10: the grid. 100 units a slot behind the line, lanes 64 and 192 alternating.</summary>
    void KartReset(TrackCar c, int i)
    {
        c.Index = i;
        c.Lap = -1;
        c.Distance = (ushort)(Track.Length - 100 * (i + 1));
        c.Heading = (short)(Track.Yaw(c.Distance) & 0xfff);
        c.Speed = 0;
        c.Finished = false;
        c.Aggressive = Rand(5) == 0;
        c.Colour = Rand(4);
        c.BaseMax = (byte)(12 + 2 * Rand(6));
        c.BaseAccel = (byte)(5 + Rand(11));
        c.Rank = i;
        c.Lateral = (short)(0x40 + 0x80 * (i & 1));
        c.Total = c.Distance;
        SetKartState(c, 0);
    }

    /// <summary>0x2049D0 (guest mode; the race-mode player rules are not here).</summary>
    void SetKartState(TrackCar c, int s)
    {
        switch (s)
        {
            case 0: Rand(11); c.Speed = 0; c.Timer = 1; break;
            case 1: c.Accel = (byte)(c.BaseAccel >> 1); c.Target = (byte)(c.BaseMax >> 1); c.Timer = (byte)(80 + 2 * Rand(11)); break;
            case 2: c.Accel = c.BaseAccel; c.Target = c.BaseMax; c.Timer = 0; break;
            case 3:
                if (c.State != 2) return;
                c.Accel = (byte)(c.BaseAccel << 1); c.Target = (byte)(c.BaseMax + Rand(6)); c.Timer = (byte)(10 + 2 * Rand(11));
                break;
            case 4: c.Timer = 20; break;
            case 5: c.Timer = (byte)(8 + (short)(Track.Yaw(c.Distance) - c.Heading) / 512); break;
            case 6: c.Timer = (byte)(5 + 2 * Rand(11)); break;
        }
        c.State = (byte)s;
    }

    /// <summary>0x204088: contact and overtaking against the car ahead, on predicted positions. The
    /// crossing spin that follows is dead for the same reason as the boats' (partner always null).</summary>
    void KartHook(TrackCar c, TrackCar behind, TrackCar ahead)
    {
        if (c.State is 0 or 5 or 6 || behind == null || ahead == null) return;
        int g = (ahead.Distance + ahead.Speed) - (c.Distance + c.Speed);
        if (ahead.Distance < c.Distance) g += Track.Length;
        if (g < 100)
        {
            if (Math.Abs(c.Lateral - ahead.Lateral) < 20) SetKartState(c, c.Speed >= 21 ? 5 : 6);
        }
        else if (g < 300) SetKartState(c, 3);
    }

    /// <summary>0x204240.</summary>
    void KartStep(TrackCar c, TrackCar behind, TrackCar ahead)
    {
        int len = Track.Length;
        if (c.Finished)
        {
            // Park (5 − rank)·100 past the line and pull to lateral 11. For rank > 5 the goal is
            // behind the line and the u16 distance wraps; the unload pass takes the car within 10
            // ticks. Kept.
            c.Lateral = (short)(c.Lateral + (11 - c.Lateral) / 4);
            int step = Math.Min(c.Target, ((5 - c.Rank) * 100 - c.Distance + 1) / 2);
            c.Distance = (ushort)(c.Distance + step);
            return;
        }
        switch (c.State)
        {
            case 0:
                if (--c.Timer == 0) SetKartState(c, 2);
                break;
            case >= 1 and <= 4:
            {
                c.Total += c.Speed;
                int d = c.Distance + c.Speed;
                if (d >= len)
                {
                    c.Lap++;
                    d %= len;
                    if (c.Lap >= Duration) { c.Finished = true; c.Total += (5 - c.Rank) * 100 - d; }
                }
                c.Distance = (ushort)d;
                int v = c.Speed + c.Accel;
                c.Speed = (sbyte)(v > c.Target ? c.Target - Rand(6) : v);
                if (c.State == 1) { if (--c.Timer == 0) SetKartState(c, 2); }
                else if (c.State == 3) { if (--c.Timer == 0) SetKartState(c, 2); }
                if (c.State == 2 && c.Aggressive && behind != null && behind.State == 3 && Rand(11) == 0) SetKartState(c, 4);
                else if (c.State == 4) { if (--c.Timer == 0) SetKartState(c, 2); }
                int lt = c.State == 3 && ahead != null ? (ahead.Lateral >= 100 ? 0x32 : 0xcd)
                       : c.State == 1 && ahead != null && behind != null ? (behind.Lateral >= 100 ? 0x32 : 0xcd)
                       : c.State == 4 && behind != null && behind.State == 3 ? behind.Lateral
                       : 0x80;
                c.Lateral = (short)(c.Lateral + (lt - c.Lateral + 3) / 4);
                c.Heading = Ease(c.Heading, Track.Yaw(c.Distance + 4 * c.Speed), 4 * Math.Max(0, (int)c.Speed));
                break;
            }
            case 5:
                c.Heading = (short)((c.Heading + 0x200) & 0xfff);
                c.Speed >>= 1;
                if (--c.Timer == 0) SetKartState(c, 6);
                break;
            case 6:
                c.Speed = (sbyte)(c.Speed / 4 * 3);
                if (--c.Timer == 0) SetKartState(c, 1);
                break;
        }
    }
}
