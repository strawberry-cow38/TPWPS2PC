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
    /// <summary>Inside compiled shop entrance, only for a validated placed footprint.
    /// Entrance remains the public approach stub; synthetic unplaced fixtures omit this.</summary>
    public ParkCell? ServiceEntry { get; init; }

    /// <summary>The ride's own .sam, and with it everything the game authored about what this
    /// thing does to a visitor. ⚠ NULLABLE and must stay so: the audits build bare rides from a
    /// script alone, and a ride with no definition is a ride that satisfies no want -- not a
    /// crash, and not a ride that satisfies every want.</summary>
    public RideDefinition Definition { get; init; }

    /// <summary>⭐ A lavatory, by the only mark the game gives one. See
    /// <see cref="RideDefinition.ProvidesRelief"/>.</summary>
    public bool ProvidesRelief => Definition?.ProvidesRelief ?? false;
    public bool Sells => Definition?.Sells ?? false;

    /// ⚠⚠ LAVATORIES ONLY. `+0xb4` does NOT mean the same thing on every placed object: on a
    /// SHOP the sale recorder `FUN_001D1E68` does `+0xb4 += 1` as a CUSTOMER COUNT, unbounded,
    /// while the lavatory path clamps the same offset to 0..100 and resets it to 100. Two classes,
    /// one offset, two meanings -- so this property is only meaningful where
    /// <see cref="ProvidesRelief"/> holds, and nothing writes it anywhere else. An earlier version
    /// of this note called it "the console's own facility record" without that qualifier, which
    /// would have invited someone to read a shop's condition and get a number that means
    /// something else entirely.
    ///
    /// <summary>⭐⭐ THE LAVATORY'S CONDITION, `+0xb4` on that class's record, and
    /// this is its representation rather than an equivalent of mine. It starts at **100**
    /// (`FUN_001302d8` constructs it there, `FUN_00130678` places it from the ride template's
    /// byte 0xe), FALLS with use, and is reset to 100 by `FUN_00130978` -- which also stamps a
    /// time at `+0xa8`, so that call is a SERVICING, not an initialisation.
    ///
    /// ⚠ The port first modelled this as mess ACCUMULATING upward, which is arithmetically the
    /// same and structurally the wrong way round: the console depletes a condition toward a floor
    /// of zero. Keeping its direction means a cleaner is `= 100` rather than `-= something`, and
    /// a future "how dirty is this" reading does not have to know what full looked like.
    ///
    /// ⭐⭐ AND IT IS READ BACK AFTER ALL, which an earlier note here said it was not. The
    /// reader is `FUN_00130938` -- a one-line `return facility[0xb4]` -- called by the lavatory
    /// arm of `FUN_0020EDD8` right after the wear: below **50** the guest loses 10 happiness,
    /// gains 10 sickness and gets the angry bubble. ⚠ The census that missed it looked for `lb`
    /// and `lbu` AT the offset, and this read is inside an accessor, so it could not have found
    /// it. The note hedged correctly ("not found, not not-there") and the hedge was the only
    /// thing standing between that census and a wrong finding.</summary>
    public int Condition { get; private set; } = 100;

    /// <summary>`+0xBA`, the shop's quality, which `FUN_001D1B08` folds into how much a guest
    /// wants what it sells: `(quality &gt;&gt; 2) + 75`.
    ///
    /// ⭐⭐ IT STARTS AT **100**, and finding that out was the whole exercise. `FUN_001D16C8`,
    /// which builds a shop from its compiled record, calls the setter `FUN_001D1F58(shop, 100)`
    /// two lines after writing the price -- so `(100 &gt;&gt; 2) + 75` = **100** and a new shop's
    /// base is its `BaseCostOfGoods` exactly.
    ///
    /// ⚠⚠ THIS PORT ALMOST SHIPPED IT AS 0, with a comment arguing that 0 was "the console's own
    /// arithmetic on an unset field". It is not an unset field; it is explicitly initialised, and
    /// at 0 the base falls to three quarters and **NO GUEST EVER BUYS ANYTHING** -- measured: a
    /// guest at hunger 80 scored 25 against a price of 30. ⭐ The audit caught it as nine failing
    /// purchase cases, which is the only reason the reasoning got re-examined instead of shipped.
    ///
    /// ⭐⭐ AND WHAT MOVES IT IS **THE PLAYER**. `0x1D7070` -- the third call site, left as "still
    /// unread" for an hour -- is the shop management screen writing back:
    /// `FUN_001D1F58(shop, ui[0xa82])`, beside `shop[0xb8] = ui[0xd2c]` (the price) and
    /// `FUN_001D1FC0(shop, ui[0xbba])`. Three controls on one screen. ⭐ The screen's setup
    /// (`0x1D6D28`) clamps each to 0..100 and the price to **1..500**.</summary>
    public int Quality { get; set; } = 100;

    /// <summary>`+0xAC`, the shop screen's **other** 0..100 control, default 0.
    ///
    /// ⚠⚠ THIS FILE CALLED IT "the shop's running customer count" AND IT IS NOT ONE.
    /// `FUN_001D1FC0` is a plain setter and the shop screen drives it from a UI control -- a
    /// counter nobody increments and the player types into is not a counter. ⭐ The real customer
    /// count is `+0xB4`: `FUN_001D1E00` returns `shop[0xb0] / shop[0xb4]` clamped to 100, which
    /// is the average-satisfaction rating this repo already decoded (`+0xB0` accumulates
    /// satisfaction, `+0xB4` counts who it was delivered to).
    ///
    /// ⭐ The mistake was carrying a meaning ACROSS OFFSETS by analogy -- `+0xB4` really is a
    /// customer count, so `+0xAC` "must be one too". Reading the setter settles in one line what
    /// an hour of inference got backwards, which is this repo's own rule about parsers and bytes.
    ///
    /// **What it does:** it is subtracted, quartered, from the want base and therefore from the
    /// COST too -- `record[0x2e] * ((quality &gt;&gt; 2) + 75 - (this &gt;&gt; 2)) / 100` is both. So
    /// turning it up makes a sale cheaper to supply and less attractive to buy. ⚠ Named for its
    /// offset rather than that behaviour: "cutting corners" fits the arithmetic and is not a
    /// label anybody read.</summary>
    public int Setting0xAC { get; set; }

    /// <summary>`+0xBC` and `+0xC0`: what this facility has taken in gross, and what it has made
    /// after the cost of its goods. `FUN_001D18E8` adds the full price to one and the margin to
    /// the other on every sale, whichever way the margin goes.</summary>
    public int Takings { get; private set; }
    public int Profit { get; private set; }

    /// <summary>⭐ One sale, as `FUN_001D18E8` books it. <paramref name="margin"/> may be
    /// negative -- a shop priced below its cost of goods loses money and the console debits the
    /// park for it rather than clamping.</summary>
    public void Book(int price, int margin) { Takings += price; Profit += margin; }

    /// <summary>Use wears it down, floored at zero -- `FUN_00130948`, whose ONLY caller is the
    /// relief path at `0x20ef48`.</summary>
    public void Wear(int amount) => Condition = Math.Max(0, Condition - Math.Max(0, amount));

    /// <summary>Put it back to new. ⭐ The console's `FUN_00130978`; the hook a cleaner wants.</summary>
    public void Service() => Condition = 100;

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

    readonly Queue<int> _queue = new();
    readonly List<int> _left = new();

    /// <summary>A guest joins the back of this ride's queue. They are not on the ride and the
    /// script has not seen them yet; <see cref="ParkSim"/> offers them when the ride asks.</summary>
    public void Join(int guest) => _queue.Enqueue(guest);
    public IReadOnlyCollection<int> Queue => _queue;
    internal bool TryTakeFromQueue(out int guest) => _queue.TryDequeue(out guest);

    /// <summary>Guests the ride has finished with, oldest first, since they were last read.</summary>
    public IReadOnlyList<int> Left => _left;
    internal void Leaves(int guest) => _left.Add(guest);
    public void ClearLeft() => _left.Clear();

    /// <summary>How many riders the script believes it has. ⭐ The script keeps this itself --
    /// king.RSE does `ADD VAR_ONRIDE 1` as it boards and `-1` as they go -- so it is the honest
    /// answer to "did anyone actually get on", not a count the host maintains and then checks.</summary>
    public int OnRide => Get("VAR_ONRIDE");
    public int SpaceLeft => Get("VAR_SPACELEFT");
    public bool Running => Get("VAR_RUNNING") != 0;

    /// <summary>What the renderer should be drawing: the animation slot, which variant of it, and
    /// how far through. ⚠ The FRAME is the host's, in APS frames, so the view never has to know
    /// how long anything is.</summary>
    public int Slot => Host?.AnimationSlot ?? -1;
    public int Variant => Host?.AnimationVariant ?? -1;
    public float Frame => Host?.Frame ?? 0f;

    /// <summary>⚠ A CHILD'S FAULT IS THE RIDE'S FAULT. A ride whose effects script died is not a
    /// working ride, and reporting only the parent's state hid exactly that: the parent runs its
    /// cycle forever while the thing that makes it look like anything is stopped.</summary>
    public string Fault => ParkSim.Chain(Machine).Select(m => m.Fault).FirstOrDefault(f => f != null);
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
public sealed class ParkSim : IRseDirectory
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

    /// <summary>⭐⭐ THE PARK'S MONEY. See <see cref="ParkFinances"/> -- until this existed a
    /// guest's cash was debited and went nowhere at all.</summary>
    public ParkFinances Finances { get; } = new();

    public ParkSim(ParkPaths paths) { Paths = paths; }

    /// <summary>Put a ride in, with its script running. Returns null when the ride has no usable
    /// script -- which is not an error: plenty of scenery has none, and the caller draws it
    /// standing still.
    ///
    /// ⚠ A BAD SCRIPT MUST NOT TAKE THE PARK WITH IT. One ride's `.rse` failing to parse is that
    /// ride not running, not the park failing to load, so the throw is caught here and reported
    /// through <paramref name="fault"/>.</summary>
    /// <param name="sibling">Resolves a script named by SPAWNCHILD/SPAWNSOUND against the ride's
    /// own folder, which is how the game finds it. Null means this ride cannot spawn children,
    /// and a script that tries will fault rather than pretend it worked.</param>
    public ParkRide Add(int id, string name, ParkCell origin, int width, int height,
                        byte[] script, Animation animation, int capacity,
                        ParkCell? entrance, ParkCell? exit, out string fault,
                        Func<string, byte[]> sibling = null, int headSlots = 0,
                        RideDefinition definition = null, int? placementTurns = null)
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
            host = new RsePreviewHost(animation) { HeadSlots = headSlots };
            // ⭐ THE CHILD SHARES THE PARENT'S ANIMATION. `0x1be91c` copies the parent's `+0xc8`
            // -- its animation context -- into the child, so an effects script drives the same
            // model its ride does. That is also why TRIGANIM_CH exists: they need channels to
            // stay out of each other's way.
            RseMachine Spawn(string child)
            {
                var bytes = sibling?.Invoke(child);
                if (bytes == null || bytes.Length == 0) return null;
                return new RseMachine(new RseProgram(bytes), host, spawn: Spawn, directory: this);
            }
            machine = new RseMachine(program, host, spawn: sibling == null ? null : Spawn, directory: this);
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
            Definition = definition,
            ServiceEntry = placementTurns is int turns
                ? ShopEntrance.Inside(definition?.CompiledEntry,origin,turns,width,height,entrance) : null,
        };
        // ⭐ The opening state the console's own demo uses: a capacity from the ride's .sam, one
        // cycle, and CLOSED until something opens it. A ride that starts open runs to nobody.
        ride.Set("VAR_CAPACITY", capacity > 0 ? capacity : 1);
        ride.Set("VAR_DURATION", 1);
        ride.Set("VAR_RIDECLOSED", 1);
        // ⚠⚠ THE PARK'S CLOCK, NOT ZERO. This used to be `RunSlice(0)`, so EVERY ride ran its
        // opening slice at time zero however late it was placed -- and that slice is where the
        // script plays its Create animation. The playback's Start was therefore 0 while the park
        // was at, say, 40 s, and `Frame` = (Time - Start) * Fps / 1000 came out past the record's
        // end and clamped there: the ride appeared with Create already finished and dropped
        // straight into its running loop.
        //
        // ⭐ Only the FIRST ride placed looked right, because for it the park clock really was ~0
        // and the two agreed by accident. Reproduced before fixing: two Crazy Apes, one at t=0 and
        // one at t=40000, entered slot 0 at frame 1.2 and frame 215.0 respectively -- 215 being
        // the last frame of a 215-frame Create.
        try { machine.RunSlice(Time); }
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
            if (r.Machine == null) continue;
            r.Host.AdvanceTo(Time);
            Handshake(r);
            // ⭐ A SPAWNED CHILD IS ITS OWN SCHEDULED SCRIPT, not something the parent steps. The
            // PS2's scheduler visits every live instance, children included, so they are visited
            // here too -- and a child faulting leaves its parent running.
            foreach (var m in Chain(r.Machine))
            {
                if (m.Fault != null) continue;
                try { m.RunSlice(Time); }
                catch (Exception)
                {
                    // ⚠ One script throwing stops THAT script. RseMachine records a Fault of its
                    // own for the faults it knows about; this is for the ones it does not, and
                    // the alternative is a single bad ride taking the whole park's tick with it.
                }
            }
        }
    }

    /// <summary>⭐⭐ THE WHOLE GUEST CONTRACT, AND IT IS TWO VARIABLES. Read straight off
    /// king.RSE, which is the clearest copy of a shape every ride repeats:
    ///
    /// <code>
    ///  40  TEST VAR_LETMEON ; BRANCH_NZ @47      -- spin until the HOST puts a guest here
    ///  52  HUSH VAR_LETMEON ; WALKON ...
    ///  65  COPY VAR_LETMEON 0                    -- the script zeroes it: "taken"
    /// ...
    /// 233  WALKGET VAR_LETMEOFF                  -- the script puts the leaver here
    /// 237  TEST VAR_LETMEOFF ; BRANCH_NZ @237    -- spin until the HOST zeroes it: "collected"
    /// </code>
    ///
    /// So the host offers by writing and the script accepts by clearing, in one direction; the
    /// script offers by writing and the host accepts by clearing, in the other. Neither side ever
    /// has to be told how long anything takes.
    ///
    /// ⚠ BOTH SPINS ARE TIGHT LOOPS WITH NO ENDSLICE. They burn the script's instruction budget
    /// and yield on it, so the handshake must happen BETWEEN slices -- which is why this runs
    /// before the machine rather than inside it.</summary>
    static void Handshake(ParkRide ride)
    {
        if (ride.Has("VAR_LETMEON") && ride.Get("VAR_LETMEON") == 0
            && ride.TryTakeFromQueue(out int boarding))
            ride.Set("VAR_LETMEON", boarding);

        int leaving = ride.Get("VAR_LETMEOFF");
        if (leaving != 0) { ride.Leaves(leaving); ride.Set("VAR_LETMEOFF", 0); }
    }

    /// <summary>⭐ EVERY LIVE MACHINE IN THE PARK, children included, which is what the console's
    /// own instance list holds and therefore what FINDSCRIPTRAND searches.</summary>
    public IEnumerable<RseMachine> Machines => _rides.SelectMany(r => Chain(r.Machine));

    readonly Dictionary<RseMachine, int> _handles = new();
    readonly Dictionary<int, RseMachine> _byHandle = new();
    int _handle;

    /// <summary>⚠ HANDED OUT ON DEMAND AND NEVER REUSED. A handle a script is holding must not
    /// come to mean a different ride later, which is what recycling the numbers would do.</summary>
    public int HandleOf(RseMachine machine)
    {
        if (machine == null) return 0;
        if (_handles.TryGetValue(machine, out int h)) return h;
        _handles[machine] = ++_handle; _byHandle[_handle] = machine;
        return _handle;
    }

    /// <summary>⚠ Only a machine that is still in the park. A handle to a ride that has been
    /// removed resolves to null and SETREMOTEVAR quietly does nothing, as it does on the console
    /// for an instance that has gone.</summary>
    public RseMachine ByHandle(int handle) =>
        _byHandle.TryGetValue(handle, out var m) && Machines.Contains(m) ? m : null;

    /// <summary>A random live machine with this name. ⚠ The NAME is the script's own, set by its
    /// NAME opcode from its string pool -- not the ride's display name and not its file.</summary>
    public RseMachine FindRandom(string name)
    {
        var all = Machines.Where(m => m.Fault == null
            && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return all.Length == 0 ? null : all[_random.Next(all.Length)];
    }
    readonly Random _random = new(11);

    /// <summary>A machine and everything it has spawned, parents before children. ⚠ The walk is
    /// depth-limited because nothing stops a script spawning a script that spawns it back.</summary>
    public static IEnumerable<RseMachine> Chain(RseMachine machine, int depth = 0)
    {
        if (machine == null || depth > 8) yield break;
        yield return machine;
        foreach (var m in Chain(machine.Child, depth + 1)) yield return m;
        foreach (var m in Chain(machine.SoundChild, depth + 1)) yield return m;
    }
}
