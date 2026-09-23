namespace TPW.PS2.Data;

/// <summary>One ride standing in the park, running its own script.
///
/// ⭐ THE SCRIPT IS WHAT THE RIDE IS. A placed ride with no `.rse` is a statue: the disc keeps a
/// ride's behaviour -- when it loads, how long it runs, when it lets people on and off -- in a
/// compiled RSE program beside its model, and <see cref="RseMachine"/> is the interpreter for it.
/// So a ride here is a machine, a host for the animation it asks to play, and the cells it
/// occupies; everything else about it is the script's business.</summary>
public sealed class ParkRide
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public ParkCell Origin { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>The door cells, in park coordinates, or null where the shape declares none.</summary>
    public ParkCell? Entrance { get; init; }
    public ParkCell? Exit { get; init; }

    public RseMachine Machine { get; init; }
    public RsePreviewHost Host { get; init; }

    /// <summary>Which of the script's variables this program actually declares. ⚠ NOT every ride
    /// declares every one -- a sideshow has no VAR_SPACELEFT -- and RseProgram.VariableIndex
    /// THROWS on a name it does not know, so asking blindly kills the ride that is least like the
    /// one the code was written against.</summary>
    public IReadOnlySet<string> Variables { get; init; } = new HashSet<string>();

    public bool Has(string name) => Variables.Contains(name);
    public int Get(string name) => Has(name) ? Machine[name] : 0;
    public void Set(string name, int value) { if (Has(name)) Machine[name] = value; }

    /// <summary>What the renderer should be drawing: the animation slot, which variant of it, and
    /// how far through. ⚠ The FRAME is the host's, in APS frames, so the view never has to know
    /// how long anything is.</summary>
    public int Slot => Host?.AnimationSlot ?? -1;
    public int Variant => Host?.AnimationVariant ?? -1;
    public float Frame => Host?.Frame ?? 0f;

    public string Fault => Machine?.Fault;
}

/// <summary>The park, ticking. Rides, their scripts, and the ground they stand on.
///
/// ⭐⭐ ENGINE-FREE, DELIBERATELY. Nothing here references Godot, touches a file, or asks what time
/// it is: the caller supplies the milliseconds and the sim is a pure function of them. That is why
/// `tools/TPW.PS2.VisitorAudit` can run the whole visitor cycle as a console app, and it is the
/// line to hold -- the renderer reads this, this never reads the renderer.
///
/// ⚠ A FIXED TICK. The caller may hand in any delta; the sim consumes it in whole
/// <see cref="TickMilliseconds"/> steps and keeps the remainder, so the same sequence of events
/// comes out at any frame rate. Master's standing rule for this port is console speed with
/// everything interpolated, and the interpolation belongs to the view.</summary>
public sealed class ParkSim
{
    /// <summary>The console runs its logic at 25 a second; the RSE's own clock is milliseconds.</summary>
    public const long TickMilliseconds = 40;

    /// <summary>The variables a ride's script is asked for, when it declares them.</summary>
    public static readonly string[] RideVariables =
    {
        "VAR_LETMEON", "VAR_LETMEOFF", "VAR_CAPACITY", "VAR_DURATION", "VAR_ONRIDE",
        "VAR_RIDECLOSED", "VAR_BROKEN", "VAR_RUNNING", "VAR_SPACELEFT", "VAR_STARTNOW",
    };

    public ParkPaths Paths { get; }
    public long Time { get; private set; }
    long _carry;

    readonly List<ParkRide> _rides = new();
    public IReadOnlyList<ParkRide> Rides => _rides;

    public ParkSim(ParkPaths paths) { Paths = paths; }

    /// <summary>Put a ride in, with its script running. Returns null when the ride has no usable
    /// script -- which is not an error: plenty of scenery has none, and the caller draws it
    /// standing still.
    ///
    /// ⚠ A BAD SCRIPT MUST NOT TAKE THE PARK WITH IT. One ride's `.rse` failing to parse is that
    /// ride not running, not the park failing to load, so the throw is caught here and reported
    /// through <paramref name="fault"/>.</summary>
    public ParkRide Add(int id, string name, ParkCell origin, int width, int height,
                        byte[] script, Animation animation, int capacity,
                        ParkCell? entrance, ParkCell? exit, out string fault)
    {
        fault = null;
        if (script == null || script.Length == 0) { fault = "no script"; return null; }
        RseProgram program;
        RsePreviewHost host;
        RseMachine machine;
        var declared = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            program = new RseProgram(script);
            host = new RsePreviewHost(animation);
            machine = new RseMachine(program, host);
            foreach (var v in RideVariables)
            {
                // ⚠ VariableIndex throws on a name the program does not declare, so each one is
                // asked for separately and the answer recorded rather than assumed.
                try { program.VariableIndex(v); declared.Add(v); }
                catch (Exception) { }
            }
        }
        catch (Exception e) { fault = e.Message; return null; }

        var ride = new ParkRide
        {
            Id = id, Name = name, Origin = origin, Width = width, Height = height,
            Entrance = entrance, Exit = exit,
            Machine = machine, Host = host, Variables = declared,
        };
        // ⭐ The opening state the console's own demo uses: a capacity from the ride's .sam, one
        // cycle, and CLOSED until something opens it. A ride that starts open runs to nobody.
        ride.Set("VAR_CAPACITY", capacity > 0 ? capacity : 1);
        ride.Set("VAR_DURATION", 1);
        ride.Set("VAR_RIDECLOSED", 1);
        try { machine.RunSlice(0); }
        catch (Exception e) { fault = e.Message; return null; }
        _rides.Add(ride);
        return ride;
    }

    public void Remove(int id) => _rides.RemoveAll(r => r.Id == id);
    public void Clear() { _rides.Clear(); Time = 0; _carry = 0; }

    /// <summary>Open or close a ride. ⭐ Closed is the state a ride is BUILT in; opening it is what
    /// starts the cycle the script describes.</summary>
    public void SetOpen(int id, bool open)
    {
        foreach (var r in _rides) if (r.Id == id) r.Set("VAR_RIDECLOSED", open ? 0 : 1);
    }

    /// <summary>Advance by a real delta, in whole ticks, keeping the remainder.</summary>
    public int Advance(double deltaSeconds)
    {
        _carry += (long)Math.Round(deltaSeconds * 1000.0);
        int ticks = 0;
        // ⚠ A CEILING. A caller that stalls for a second must not make the park run a thousand
        // steps to catch up; it runs a few and loses the rest, exactly as a dropped frame should.
        while (_carry >= TickMilliseconds && ticks < 8)
        {
            _carry -= TickMilliseconds;
            Time += TickMilliseconds;
            StepOnce();
            ticks++;
        }
        if (_carry > TickMilliseconds * 8) _carry = 0;
        return ticks;
    }

    void StepOnce()
    {
        foreach (var r in _rides)
        {
            if (r.Machine == null || r.Machine.Fault != null) continue;
            try
            {
                r.Host.AdvanceTo(Time);
                r.Machine.RunSlice(Time);
            }
            catch (Exception)
            {
                // ⚠ One ride's script throwing stops THAT ride. RseMachine records a Fault of its
                // own for the faults it knows about; this is for the ones it does not, and the
                // alternative is a single bad ride taking the whole park's tick with it.
            }
        }
    }
}
