namespace TPW.PS2.Data;

/// <summary>Services owned by the game, not the script. Unavailable services must fail explicitly.</summary>
public interface IRseHost
{
    void AdvanceTo(long milliseconds);
    int PlayAnimation(int slot, int variant, bool loop);
    int AnimationSlot { get; }
    void FlushAnimation();
    bool TryEffect(RseOpcode opcode, IReadOnlyList<int> arguments);

    /// <summary>TRIGANIM_CH's extra argument: the same one-shot as TRIGANIM, on a numbered
    /// channel. `0x1bda84` differs from TRIGANIM only in passing that channel to `0x1abc80`,
    /// so a host with a single channel may forward channel 0 and reject the rest.</summary>
    int PlayAnimationOn(int channel, int slot, int variant, bool loop);

    /// <summary>Where one of this script's nodes is, in the space the walk system asks for
    /// (`0x800` for the park, `0x80` for a node on the ride's own model -- `0x1b9388`).
    ///
    /// ⚠ RETURNING FALSE IS ALLOWED AND MEANS "I DO NOT KNOW", not "the node is at the origin".
    /// A walk whose endpoints are unknown still runs its whole state machine; only its DURATION
    /// falls back to the floor, and <see cref="RseMachine.WalksAreTimed"/> says so out loud.</summary>
    bool TryNodePosition(int node, int space, out float x, out float y, out float z);

    /// <summary>Where a walking guest is right now: between <paramref name="fromNode"/> and
    /// <paramref name="toNode"/>, <paramref name="perMille"/> of the way, facing
    /// <paramref name="angle"/> (the VM's 12-bit turn), in the ticker's mode
    /// (`0x1ba7f0`: 0 on the ground, 1 stepping off, 2 carried by the ride).</summary>
    void WalkerPose(int guest, int fromNode, int toNode, int mode, int perMille, int angle);
}

public enum RseYield { Budget, EndSlice, Unlock, Wait, Animation }

/// <summary>Single-instance, deterministic slice interpreter. The caller supplies game time and
/// changes host variables between slices. No wall clock, threads, processes or native code.</summary>
public sealed class RseMachine
{
    public RseProgram Program { get; }
    public int Pc { get; private set; }
    public int LastValue { get; private set; }
    public long Time { get; private set; }
    public string Name { get; private set; } = "";
    public bool Critical { get; private set; }
    public string Fault { get; private set; }
    public RseYield Yield { get; private set; }
    readonly int[] _variables, _stack;
    int _callTop, _guestTop;
    long? _waitUntil, _animationUntil;
    int? _triggerSlot;
    int _loopSlot = -1, _loopVariant = -1;
    uint _timer;
    readonly IRseHost _host;
    readonly Func<int> _random;
    readonly Func<string, RseMachine> _spawn;
    readonly Walk[] _walks;
    short _bounceBase, _bouncing;
    bool _timedWalk;

    /// <summary>⭐⭐ A SCRIPT IS NOT ALONE. `SPAWNCHILD` (`0x1be91c`) loads a second program and
    /// hangs it off this one at instance `+0x0c`; `SPAWNSOUND` (`0x1be9b4`) does the same into
    /// `+0x14`; the child's `+0x10` points back here. SETVARINCHILD/GETVARINCHILD and their
    /// parent-facing twins then reach straight into each other's variable arrays, which is how a
    /// ride tells its effects script what it is doing.
    ///
    /// ⚠ ONE CHILD SLOT, AND SPAWNCHILD OVERWRITES IT. The handler assigns `+0x0c` without
    /// looking at what was there, so a second SPAWNCHILD replaces the first and the old child is
    /// simply dropped. Modelled as written; do not "improve" it into a list.</summary>
    public RseMachine Child { get; private set; }
    public RseMachine SoundChild { get; private set; }
    public RseMachine Parent { get; private set; }

    /// <summary>How many guests this script can have walking at once. The loader allocates
    /// TWICE the declared `#setwalk` capacity (`0x1bff78..0x1bffa8`).</summary>
    public int WalkSlots => _walks.Length;

    /// <summary>⚠ FALSE MEANS EVERY WALK RAN AT THE FLOOR. Walk duration is the distance between
    /// two of the script's nodes, and a host that cannot place a node (see
    /// <see cref="IRseHost.TryNodePosition"/>) leaves every leg at the 100 ms minimum. The
    /// handshake is still the game's -- WALKON, the 1-2-3-4 states, WALKGET -- but the TIMING is
    /// not, and anything measuring how long a ride cycle takes must check this first.</summary>
    public bool WalksAreTimed => _timedWalk;

    /// <summary>The bounce count BOUNCING reads (`0x1bb880` returns instance `+0x6c`) and the
    /// rest height BOUNCESETBASE writes (`+0x6e`). ⚠ The count only ever moves when BOUNCE and
    /// UNBOUNCE exist, and they do not yet, so BOUNCING truthfully answers zero for a script
    /// that never bounced anything and would LIE for one that did -- which is why BOUNCE is not
    /// quietly stubbed alongside it.</summary>
    public int BounceBase => _bounceBase;

    public RseMachine(RseProgram program, IRseHost host = null, Func<int> random = null,
                      Func<string, RseMachine> spawn = null)
    {
        Program = program; _host = host; _spawn = spawn;
        _variables = new int[program.VariableCount]; _stack = new int[program.StackSize];
        _callTop = _stack.Length;
        // 0x1bff78 doubles the declared capacity before allocating.
        _walks = new Walk[Math.Max(0, program.WalkCapacity) * 2];
        var rng = new Random(1);
        _random = random ?? (() => rng.Next());
    }

    /// <summary>One guest being moved by this script. 32 bytes on the PS2 at instance `+0x2c`;
    /// the field offsets below are that layout, and the state numbers are the ticker's
    /// (`0x1bade8`).</summary>
    struct Walk
    {
        public int Guest;                      // +0x10
        public short A, B, C, D;               // +0x00 +0x02 +0x04 +0x06: on-route, then off-route
        public short Kind, Extra, Angle, State;// +0x16 +0x1a +0x14 +0x18
        public long Start, End;                // +0x08 +0x0c
    }
    public int this[int index] { get => _variables[index]; set => _variables[index] = value; }
    public int this[string name] { get => this[Program.VariableIndex(name)]; set => this[Program.VariableIndex(name)] = value; }
    public IReadOnlyList<int> Variables => Array.AsReadOnly(_variables);
    public int GuestCount => _guestTop;
    /// <summary>Guest identities in HUSH order, excluding the shared call-stack region.</summary>
    public IReadOnlyList<int> GuestIds => Array.AsReadOnly(_stack[.._guestTop]);

    /// <summary>One scheduled visit, using the authored instruction budget (+0x10).
    /// Critical sections suspend that budget; a separate hard limit faults runaway locks.</summary>
    public RseYield RunSlice(long milliseconds, int hardLimit = 100_000)
    {
        if (Fault != null) throw new InvalidOperationException(Fault);
        if (milliseconds < Time || milliseconds > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if (hardLimit < 1) throw new ArgumentOutOfRangeException(nameof(hardLimit));
        Time = milliseconds;
        Critical = false; // The PS2 scheduler resets its global lock on each visit (0x1bfbb8).
        try
        {
            _host?.AdvanceTo(Time);
            // The walk table advances with the instance, not with the instruction stream: a
            // script that is parked in WAIT still has its riders moving.
            StepWalks();
            int budget = Program.SliceBudget;
            for (int steps = 0; steps < hardLimit; steps++)
            {
                var ins = Program.At(Pc);
                var a = ins.Operands;
                int V(int i) => Value(a[i]);
                void Store(int i, int value, bool optional = false)
                {
                    if (a[i].Tag == 0x40) this[a[i].Index] = value;
                    else if (!optional) throw new InvalidDataException($"Expected destination variable: {ins}");
                }
                void Result(int value, bool optional = false) { LastValue = value; Store(0, value, optional); }
                int next = ins.Next;
                bool branch = false;
                switch (ins.Opcode)
                {
                    case RseOpcode.NOP: break;
                    case RseOpcode.NAME:
                        if (a[0].Tag != 0x10) throw new InvalidDataException("NAME requires a string offset");
                        Name = Program.StringAt(a[0].Index); break;
                    case RseOpcode.COPY: Result(V(1)); break;
                    case RseOpcode.ADD: Result(unchecked(V(0) + V(1))); break;
                    case RseOpcode.SUB: Result(unchecked(V(1) - V(2)), true); break;
                    case RseOpcode.DIV:
                    case RseOpcode.MOD:
                        int x = V(1), y = V(2);
                        Result(y == 0 ? 0 : ins.Opcode == RseOpcode.DIV
                            ? unchecked((int)((long)x / y)) : unchecked((int)((long)x % y)), true);
                        break;
                    case RseOpcode.TEST: if (a[0].Tag == 0x40) LastValue = V(0); break;
                    case RseOpcode.CMP: if (a[0].Tag == 0x40) LastValue = unchecked(V(0) - V(1)); break;
                    case RseOpcode.GETTIME: Result(unchecked((int)Time), true); break;
                    case RseOpcode.SETTIMER: _timer = unchecked((uint)Time + (uint)V(0)); break;
                    case RseOpcode.GETTIMER: Result(Math.Max(0, unchecked((int)(_timer - (uint)Time))), true); break;
                    case RseOpcode.RAND:
                        int bound = a[1].Immediate;
                        if (bound < 0) throw new InvalidDataException("Negative RAND bound");
                        Result((int)((uint)_random() % ((uint)bound + 1)), true); break;
                    case RseOpcode.BRANCH: branch = true; break;
                    case RseOpcode.BRANCH_Z: branch = LastValue == 0; break;
                    case RseOpcode.BRANCH_NZ: branch = LastValue != 0; break;
                    case RseOpcode.BRANCH_NV: branch = LastValue < 0; break;
                    case RseOpcode.BRANCH_PV: branch = LastValue > 0; break;
                    case RseOpcode.JSR:
                        if (_callTop <= _guestTop) throw new InvalidDataException("RSSE call stack overflow");
                        _stack[--_callTop] = next; branch = true; break;
                    case RseOpcode.RETURN:
                        if (_callTop == _stack.Length) throw new InvalidDataException("RSSE call stack underflow");
                        next = _stack[_callTop++]; Program.At(next); break;
                    case RseOpcode.HUSH:
                        if (_guestTop >= _callTop) throw new InvalidDataException("RSSE guest stack overflow");
                        _stack[_guestTop++] = LastValue = V(0); break;
                    case RseOpcode.HOP:
                        if (_guestTop > 0) Result(_stack[--_guestTop], true); break;
                    case RseOpcode.CRIT_LOCK: Critical = true; break;
                    case RseOpcode.CRIT_UNLOCK:
                        Critical = false; Pc = next; return Yield = RseYield.Unlock;
                    case RseOpcode.ENDSLICE: Pc = next; return Yield = RseYield.EndSlice;
                    case RseOpcode.WAIT:
                        if (_waitUntil == null) { _waitUntil = Time + V(0); return Yield = RseYield.Wait; }
                        if (Time < _waitUntil) return Yield = RseYield.Wait;
                        _waitUntil = null; break;
                    case RseOpcode.FLUSHANIM: Host().FlushAnimation(); break;
                    case RseOpcode.LOOPANIM:
                        if (_loopSlot == V(0) && _loopVariant == V(1)) break;
                        Host().PlayAnimation(V(0), V(1), true);
                        _loopSlot = V(0); _loopVariant = V(1); _animationUntil = null; break;
                    case RseOpcode.WAITANIM:
                        if (_waitUntil == null)
                        {
                            int duration = Host().PlayAnimation(V(0), V(1), false);
                            // The PS2 has an unsigned conversion path here. Do not silently
                            // replace its underflow with a friendly signed clamp.
                            if (duration < 300) throw new NotSupportedException("WAITANIM duration below 300ms: PS2 unsigned underflow path is not modeled");
                            _waitUntil = Time + Math.Max(300, duration - 300);
                            _loopSlot = -1;
                            _animationUntil = null;
                            return Yield = RseYield.Animation;
                        }
                        if (Time < _waitUntil) return Yield = RseYield.Animation;
                        _waitUntil = null; break;
                    case RseOpcode.TRIGANIM:
                    case RseOpcode.TRIGWAITANIM:
                        if (_triggerSlot != null)
                        {
                            if (Host().AnimationSlot != _triggerSlot) return Yield = RseYield.Animation;
                            _triggerSlot = null; break;
                        }
                        LastValue = Math.Max(300, Host().PlayAnimation(V(0), V(1), false) - 300);
                        _loopSlot = -1;
                        Store(2, LastValue, true); _animationUntil = Time + LastValue;
                        if (ins.Opcode == RseOpcode.TRIGWAITANIM)
                        {
                            _triggerSlot = V(0);
                            // Revisit the instruction to wait for the requested slot to start.
                            next = Pc;
                        }
                        break;
                    case RseOpcode.WAIT4ANIM:
                        if (_animationUntil != null && Time < _animationUntil) return Yield = RseYield.Animation;
                        _animationUntil = null; break;
                    case RseOpcode.ADDOBJ: case RseOpcode.KILLOBJ: case RseOpcode.FADEOBJ:
                    case RseOpcode.EVENT: case RseOpcode.ADDHEAD: case RseOpcode.DELHEAD:
                    case RseOpcode.STARTSCREAM: case RseOpcode.STOPSCREAM:
                    case RseOpcode.SINGLESCREAM: case RseOpcode.SCREAMLEVEL:
                    case RseOpcode.REPAIREFFECT: case RseOpcode.SETREVERB: case RseOpcode.DIPMUSIC:
                        if (!Host().TryEffect(ins.Opcode, a.Select(Value).ToArray()))
                            throw new NotSupportedException($"Host rejected {ins}");
                        break;

                    // ⭐⭐ A CHILD IS A SECOND PROGRAM, NOT A PRESENTATION REQUEST. The operand is
                    // tag 0x10 -- a STRING -- and `0x1be91c` copies the script's own directory
                    // (`+0x38`), appends that string, and loads it. All twelve spawns on this
                    // disc name a real sibling `.rse`: Coaster1 spawns ITS OWN `EventMap.rse`,
                    // one of seven files with that name, which is exactly why the lookup is by
                    // directory and not by name across the archive.
                    case RseOpcode.SPAWNCHILD:
                    case RseOpcode.SPAWNSOUND:
                        if (a[0].Tag != 0x10) throw new InvalidDataException($"Expected a script name: {ins}");
                        var spawned = Spawn(Program.StringAt(a[0].Index));
                        if (ins.Opcode == RseOpcode.SPAWNSOUND) SoundChild = spawned;
                        else { Child = spawned; if (spawned != null) spawned.Parent = this; }
                        break;
                    case RseOpcode.REMOVECHILD: Child = null; break;
                    case RseOpcode.SETVARINCHILD: Poke(Child, V(0), V(1)); break;
                    case RseOpcode.SETVARINPARENT: Poke(Parent, V(0), V(1)); break;
                    case RseOpcode.GETVARINCHILD: Peek(Child, a[0], V(1)); break;
                    case RseOpcode.GETVARINPARENT: Peek(Parent, a[0], V(1)); break;

                    // TRIGANIM with a channel. `0x1bda84` is TRIGANIM's body with the fourth
                    // operand handed to the animation call, and the same -300/floor-300 duration.
                    case RseOpcode.TRIGANIM_CH:
                        LastValue = Math.Max(300, Host().PlayAnimationOn(V(3), V(0), V(1), false) - 300);
                        _loopSlot = -1;
                        Store(2, LastValue, true); _animationUntil = Time + LastValue;
                        break;

                    // ⭐ THE GUEST HANDSHAKE. A ride HUSHes the guest the host put in VAR_LETMEON,
                    // WALKONs it to a seat, and much later HOPs it, WALKOFFs it and WALKGETs it
                    // back before announcing it in VAR_LETMEOFF. Totem passes VAR_ONRIDE as the
                    // destination node, so the Nth rider walks to the Nth seat.
                    case RseOpcode.WALKON:
                        WalkOn(V(0), V(1), V(2), V(3), V(4), V(5), V(6)); break;
                    case RseOpcode.WALKOFF: WalkOff(V(0)); break;
                    case RseOpcode.WALKGET: Result(WalkGet(), true); break;

                    case RseOpcode.BOUNCESETBASE: _bounceBase = (short)V(0); break;
                    case RseOpcode.BOUNCING: Result(_bouncing, true); break;

                    default: throw new NotSupportedException($"Unimplemented RSSE instruction: {ins}");
                }
                if (branch)
                {
                    if (a[0].Tag != 0x20) throw new InvalidDataException($"Expected code address: {ins}");
                    next = a[0].Index;
                }
                Pc = next;
                if (!Critical && --budget <= 0) return Yield = RseYield.Budget;
            }
            throw new InvalidDataException("RSSE hard instruction limit exceeded (runaway critical section)");
        }
        catch (Exception ex)
        {
            Fault = $"RSSE at word {Pc}, t={Time}: {ex.Message}";
            throw new InvalidOperationException(Fault, ex);
        }
    }
    int Value(RseProgram.Operand a) => a.Tag == 0x40 ? this[a.Index]
        : a.Tag == 0 ? a.Immediate : throw new InvalidDataException($"Expected numeric operand, got {a}");
    IRseHost Host() => _host ?? throw new NotSupportedException("This instruction requires an RSSE host");

    /// <summary>Load a sibling script. ⚠ A NULL CHILD IS THE GAME'S OWN ANSWER: `0x1be91c` stores
    /// whatever the loader returned, zero included, and every later opcode checks the handle
    /// against zero. A script that spawns something we cannot resolve keeps running with no
    /// child, exactly as it would on a disc missing that file.</summary>
    RseMachine Spawn(string name)
    {
        if (_spawn == null) throw new NotSupportedException($"SPAWNCHILD \"{name}\" needs a script resolver");
        return _spawn(name);
    }

    // Both directions share `LAB_001bead4`/`LAB_001beb50`: no handle, or an index past the other
    // program's variable count, is a silent no-op -- not a fault.
    void Poke(RseMachine other, int index, int value)
    {
        LastValue = value;
        if (other != null && (uint)index < (uint)other._variables.Length) other._variables[index] = value;
    }
    void Peek(RseMachine other, RseProgram.Operand destination, int index)
    {
        if (destination.Tag != 0x40 || other == null) return;
        if ((uint)index >= (uint)other._variables.Length) return;
        LastValue = other._variables[index];
        this[destination.Index] = LastValue;
    }

    /// <summary>⚠ ONE UNIT A SECOND, TRUNCATED FIRST. `0x1bb180` computes `(int)distance * 1000`
    /// -- the cast runs BEFORE the scale -- so a 2.7 unit walk is timed at 2000 ms, not 2700, and
    /// anything under one unit falls to the 100 ms floor. Kept as written.</summary>
    int WalkMilliseconds(int from, int to, int kind)
    {
        // The ride-side node of a `kind == 4` walk lives in the model's space, not the park's.
        if (_host != null
            && _host.TryNodePosition(from, 0x800, out float ax, out float ay, out float az)
            && _host.TryNodePosition(to, kind == 4 ? 0x80 : 0x800, out float bx, out float by, out float bz))
        {
            _timedWalk = true;
            float dx = bx - ax, dy = by - ay, dz = bz - az;
            int ms = (int)MathF.Sqrt(dx * dx + dy * dy + dz * dz) * 1000;
            return ms == 0 ? 100 : ms;
        }
        return 100;
    }

    /// <summary>The VM's own atan2, in twelfth-circle units. `0x1b9138` takes (dz, dx).</summary>
    static short Bearing(float dz, float dx) =>
        (short)(((int)MathF.Round(MathF.Atan2(dz, dx) * 4096f / MathF.Tau)) & 0xFFF);

    short BearingBetween(int from, int to, int kind)
    {
        if (_host == null
            || !_host.TryNodePosition(from, 0x800, out float ax, out _, out float az)
            || !_host.TryNodePosition(to, kind == 4 ? 0x80 : 0x800, out float bx, out _, out float bz))
            return 0;
        return Bearing(bz - az, bx - ax);
    }

    void WalkOn(int guest, int a, int b, int c, int d, int kind, int extra)
    {
        // First FREE slot wins, and a full table silently drops the walk (`0x1bb180` falls out of
        // its loop without doing anything). The guest is then never harvested by WALKGET.
        for (int i = 0; i < _walks.Length; i++)
        {
            if (_walks[i].State != 0) continue;
            _walks[i] = new Walk
            {
                Guest = guest,
                A = (short)a, B = (short)b, C = (short)c, D = (short)d,
                Kind = (short)kind, Extra = (short)extra,
                Angle = BearingBetween(a, b, kind),
                Start = Time, End = Time + WalkMilliseconds(a, b, kind),
                State = 1,
            };
            return;
        }
    }

    /// <summary>Send a guest back out: the walk slot is re-aimed along the pair of nodes WALKON
    /// stashed for exactly this (`C` to `D`) and restarted in state 3 (`0x1bb3f0`).</summary>
    void WalkOff(int guest)
    {
        for (int i = 0; i < _walks.Length; i++)
        {
            if (_walks[i].State == 0 || _walks[i].Guest != guest) continue;
            ref var w = ref _walks[i];
            // ⚠ THE SPACES SWAP ROUND on the way out: leaving a `kind == 4` ride starts at a node
            // on the MODEL and ends on the park, the mirror of the way in.
            w.Angle = w.Kind == 4 ? BearingBetween(w.D, w.C, 4) : BearingBetween(w.C, w.D, 0);
            w.Start = Time;
            w.End = Time + (w.Kind == 4 ? WalkMilliseconds(w.D, w.C, 4) : WalkMilliseconds(w.C, w.D, 0));
            w.State = 3;
            return;
        }
    }

    /// <summary>Hand back one guest who has finished walking off, and free the slot. Zero when
    /// nobody has arrived -- which is what the scripts spin on (`WALKGET v1; BRANCH_Z`).</summary>
    int WalkGet()
    {
        for (int i = 0; i < _walks.Length; i++)
        {
            if (_walks[i].State != 4) continue;
            int guest = _walks[i].Guest;
            _walks[i].State = 0;
            _walks[i].Guest = 0;
            return guest;
        }
        return 0;
    }

    /// <summary>⭐⭐ THE WALK IS A FOUR-STATE MACHINE AND STATE 1 DOES NOT GO TO 4. `0x1bade8`:
    /// 1 (walking in) finishes into 2 (aboard, held at the ride's node), and only WALKOFF starts
    /// 3 (walking out), which finishes into 4 (arrived, waiting to be collected). Reading WALKGET
    /// alone suggests a guest becomes collectable as soon as they arrive; they do not, and a host
    /// built on that guess would hand every rider straight back without them ever riding.</summary>
    void StepWalks()
    {
        for (int i = 0; i < _walks.Length; i++)
        {
            ref var w = ref _walks[i];
            if (w.State == 0) continue;
            long span = w.End - w.Start;
            int t = span <= 0 ? 1000 : (int)Math.Clamp((Time - w.Start) * 1000 / span, 0, 1000);
            switch (w.State)
            {
                case 1:
                    _host?.WalkerPose(w.Guest, w.A, w.B, w.Kind == 4 ? 2 : 0, t, w.Angle);
                    if (t >= 1000) { w.State = 2; w.Start = Time; }
                    break;
                case 2:
                    // Aboard: re-placed every tick so a moving ride carries its riders with it.
                    _host?.WalkerPose(w.Guest, w.A, w.B, w.Kind == 4 ? 2 : 0, 1, w.Angle);
                    break;
                case 3:
                    _host?.WalkerPose(w.Guest, w.C, w.D, w.Kind == 4 ? 1 : 0, t, w.Angle);
                    if (t >= 1000) w.State = 4;
                    break;
                case 4:
                    _host?.WalkerPose(w.Guest, w.D, w.D, 0, 0, w.Angle);
                    break;
            }
        }
    }
}
