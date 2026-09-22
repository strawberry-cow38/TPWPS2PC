namespace TPW.PS2.Data;

/// <summary>Services owned by the game, not the script. Unavailable services must fail explicitly.</summary>
public interface IRseHost
{
    void AdvanceTo(long milliseconds);
    int PlayAnimation(int slot, int variant, bool loop);
    int AnimationSlot { get; }
    void FlushAnimation();
    bool TryEffect(RseOpcode opcode, IReadOnlyList<int> arguments);
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

    public RseMachine(RseProgram program, IRseHost host = null, Func<int> random = null)
    {
        Program = program; _host = host;
        _variables = new int[program.VariableCount]; _stack = new int[program.StackSize];
        _callTop = _stack.Length;
        var rng = new Random(1);
        _random = random ?? (() => rng.Next());
    }
    public int this[int index] { get => _variables[index]; set => _variables[index] = value; }
    public int this[string name] { get => this[Program.VariableIndex(name)]; set => this[Program.VariableIndex(name)] = value; }
    public IReadOnlyList<int> Variables => Array.AsReadOnly(_variables);
    public int GuestCount => _guestTop;

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
}
