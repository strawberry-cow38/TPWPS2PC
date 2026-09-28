namespace TPW.PS2.Data;

/// <summary>What feeds the rule object's 79 state variables: the producer dispatch `0x10DE38` (jump table
/// `0x359860`) for the indices the port produces outside the rule object -- 0..51, 54, 55. Indices 52, 53,
/// 56..78 are the rule object's own (<see cref="AdvisorScheduler.Produce"/>). <see cref="AdvisorProducers"/>
/// is the port's wiring.</summary>
public interface IAdvisorProducers
{
    /// <summary>The value the producer for <paramref name="index"/> stores (as s16).</summary>
    short Produce(int index);
    /// <summary>Producer 53's `month + 12·year`, capped at 30000 (the v4 arithmetic, `0x10E40C..0x10E44C`).</summary>
    int Months { get; }
    /// <summary>Producer 53's latch test `0x104BD0()` (the v24 function, upgrade research %).</summary>
    int UpgradeResearchPercent { get; }
}

/// <summary>⭐⭐ THE RULE OBJECT AND ITS SCHEDULER (0xEC bytes at `adv+0x264`; loader `0x10DA78`, scheduler
/// `0x10DC50`, counter add `0x10DDD8`, producers `0x10DE38`), findings/advisor-rules.md §2.2, §4, §5, §6
/// -- READ against the MIPS (`0x10DA78..0x10DE34`, `0x10DE38..0x10E4B0`).
///
/// <code>
///   Step (advisor idle, flags bit 3):
///     !warm: up to 5 × { produce(cv); cv = (cv+1) % 79; cv == 0 → warm = 1, stop }; !warm → return
///      warm: produce(cv); cv = (cv+1) % 79
///     ring head != tail → return                   // the cursor does NOT advance
///     day = cal+0x10 (u32); r = rules[cr]
///     r.next &lt; day (unsigned, strict):
///         v78 = (s16)(day − (u16)r.lastFail)      // sw, then lhu: the width quirk
///         res = interpret(r)                       // 0 END, 1 compare failed, 2 held-blocked
///         res 0 → r.next = day + (u16)r.delay;  res 1 → r.lastFail = day;  res 2 → nothing
///     cr = (cr+1) % 106                            // also when not due
/// </code>
/// ⭐ So calls 1..15 refresh five variables each, call 16 refreshes the last four (75..78), finishes the
/// warm-up AND considers rule 0 in the same call, and every call after refreshes one and considers one.
///
/// ⭐ PARK START IS THE ONLY RESET: the object is built fresh at every park start (<see cref="AdvisorScheduler(AdvisorRules, IAdvisorProducers, Func{uint})"/>),
/// every rule `next = 0`, `lastFail = day`, variables and counters 0, cursors 0, warm-up from the top.
/// ⭐ A RESUMED park builds it at day 0 and restores the calendar AFTERWARDS (`0x151688` then `0x15172C`),
/// so every rule is due at once and sees `v78 = (s16)D`: construct the advisor before a restored calendar
/// is applied. ⚠ NOT SAVED: nothing saves the rule object (research §2.2, §11), so it has no save shape.
///
/// ⭐ THE DAY is `ParkClock.TotalDays` (`cal+0x10`): 0x4000 units a sim pass, 60 passes a day, 2.4 s a day at
/// the console's 25 passes a second (findings/clock-rate.md). Every rule delay is in these days.</summary>
public sealed class AdvisorScheduler
{
    /// <summary>`0x10DE60` `sltiu 0x4F`: 79 variables, v78 last.</summary>
    public const int VariableCount = 79;
    /// <summary>`0x10DDD8` `slti 0x16`: 22 event counters, which variables 56..77 snapshot.</summary>
    public const int CounterCount = 22;
    /// <summary>`v78` (`+0x9C`): days since the rule's last failed comparison, written by the scheduler.</summary>
    public const int ElapsedVariable = 78;
    /// <summary>The first counter snapshot: v(56 + j) = counter j.</summary>
    public const int FirstCounterVariable = 56;
    /// <summary>`0x10DCB4` `sltiu 5`: refreshes per call during the warm-up.</summary>
    public const int WarmUpRefreshes = 5;
    /// <summary>`0x10DE04`/`0x10DE1C`: the counter clamp.</summary>
    public const int CounterLimit = 30000;

    readonly AdvisorRules _rules;
    readonly IAdvisorProducers _producers;
    readonly Func<uint> _day;
    readonly uint[] _next, _lastFail;

    /// <summary>`0x10DA78`: every variable, both cursors, the warm flag, the v53 latch and the counters 0;
    /// the 106 programs loaded; `next = 0`, `lastFail = day` for every rule (the day read ONCE per rule,
    /// here all the same).</summary>
    /// <param name="day">`0x16B218(0x16AE90())` = `cal+0x10`, <see cref="ParkClock.TotalDays"/>.</param>
    public AdvisorScheduler(AdvisorRules rules, IAdvisorProducers producers, Func<uint> day)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _producers = producers ?? throw new ArgumentNullException(nameof(producers));
        _day = day ?? throw new ArgumentNullException(nameof(day));
        RuleCount = rules.Rules.Count;                                     // +0xD4 = size / 12 (u8)
        _next = new uint[RuleCount];
        _lastFail = new uint[RuleCount];
        for (int i = 0; i < RuleCount; i++) { _next[i] = 0; _lastFail[i] = _day(); }
        Last = new StepReport(0, false, -1, false, null, Array.Empty<AdvisorRules.Effect>());
    }

    /// <summary>`+0x00 + 2i`, the 79 state variables (s16). v78 is the scheduler's.</summary>
    public short[] Variables { get; } = new short[VariableCount];
    /// <summary>`+0x9E + 2j`, the 22 event counters (s16), which only <see cref="AddCounter"/> and the VM's
    /// SET (the mirror, `0x10E60C`) write.</summary>
    public short[] Counters { get; } = new short[CounterCount];
    /// <summary>`+0xCA`, the variable refresh cursor 0..78.</summary>
    public int VariableCursor { get; private set; }
    /// <summary>`+0xCC`, the rule cursor 0..105.</summary>
    public int RuleCursor { get; private set; }
    /// <summary>`+0xCE`, the warm-up done.</summary>
    public bool Warm { get; private set; }
    /// <summary>`+0xD4`, 106 on this disc.</summary>
    public int RuleCount { get; }
    /// <summary>`+0xE0`: producer 53's latch -- the month index when upgrades were first researched.</summary>
    public int UpgradeMonthLatch { get; private set; }
    /// <summary>`+0xE4` bit 0: that latch is set.</summary>
    public bool UpgradeLatched { get; private set; }

    /// <summary>A rule's runtime `next` day (`header+4`, u32).</summary>
    public uint Next(int rule) => _next[rule];
    /// <summary>A rule's runtime last-failure day (`header+8`, written as a u32, READ back as a u16).</summary>
    public uint LastFail(int rule) => _lastFail[rule];
    /// <summary>For the audit: put a rule's two runtime words where a test needs them (the width quirk needs
    /// a day past 65535, which no park reaches in a run).</summary>
    internal void SetTimes(int rule, uint next, uint lastFail) { _next[rule] = next; _lastFail[rule] = lastFail; }

    /// <summary>What the last <see cref="Step"/> did, for the audit and the log.</summary>
    public readonly record struct StepReport(int Refreshed, bool Gated, int ConsideredRule, bool Due,
                                             AdvisorRules.Result? Result, IReadOnlyList<AdvisorRules.Effect> Effects);
    public StepReport Last { get; private set; }
    /// <summary>Calls made, rules evaluated (due), and the per-result counts -- instrumentation.</summary>
    public long Calls { get; private set; }
    public long Evaluations { get; private set; }

    /// <summary>⭐ `0x10DC50`, one call. <paramref name="ringEmpty"/> is the advisor's `head == tail`
    /// (`[0x2AA720]+0xA8/+0xA9`); <paramref name="textUi"/> and <paramref name="message"/> are opcodes 8 and 7
    /// (`0x1073F0`, `0x107CC0`), run in the program's order once it has returned (nothing between them
    /// reads what they change, so the order of effects is the interpreter's).</summary>
    public void Step(Func<bool> ringEmpty, Action<ushort> textUi, Action<ushort> message)
    {
        Calls++;
        int refreshed = 0;
        if (!Warm)
        {
            for (int k = 0; k < WarmUpRefreshes; k++)
            {
                Produce(VariableCursor); refreshed++;
                VariableCursor = (VariableCursor + 1) % VariableCount;
                if (VariableCursor == 0) { Warm = true; break; }
            }
            if (!Warm) { Last = new StepReport(refreshed, false, -1, false, null, Array.Empty<AdvisorRules.Effect>()); return; }
        }
        else
        {
            Produce(VariableCursor); refreshed++;
            VariableCursor = (VariableCursor + 1) % VariableCount;
        }
        if (!ringEmpty()) { Last = new StepReport(refreshed, true, -1, false, null, Array.Empty<AdvisorRules.Effect>()); return; }
        uint day = _day();
        int cr = RuleCursor;
        bool due = _next[cr] < day;                                        // sltu, strict
        AdvisorRules.Result? result = null;
        IReadOnlyList<AdvisorRules.Effect> effects = Array.Empty<AdvisorRules.Effect>();
        if (due)
        {
            Variables[ElapsedVariable] = unchecked((short)(day - (ushort)_lastFail[cr]));   // lhu +8
            var e = _rules.Evaluate(cr, Variables, Counters);
            Evaluations++;
            result = e.Result; effects = e.Effects;
            if (e.Result == AdvisorRules.Result.Completed) _next[cr] = unchecked(day + _rules.Rules[cr].DelayDays);  // lhu +2
            else if (e.Result == AdvisorRules.Result.ConditionFailed) _lastFail[cr] = day;  // sw +8
            foreach (var fx in e.Effects)
            {
                if (fx.Code == AdvisorRules.Op.TextUi) textUi?.Invoke(fx.MessageId);
                else if (fx.Code == AdvisorRules.Op.Message) message?.Invoke(fx.MessageId);
            }
        }
        RuleCursor = (RuleCursor + 1) % RuleCount;
        Last = new StepReport(refreshed, false, cr, due, result, effects);
    }

    /// <summary>⭐ `0x10DE38(ro, i)`: store variable i. `i ≥ 79` does nothing (`sltiu 0x4F`); 78 has no
    /// producer (the common return `0x10E498`). The rule object's own:
    /// <code>
    ///   52        default path 0x10E484 with index 52 − 56 = −4: +0x9E − 8 = +0x96 = VARIABLE 75
    ///   53        0x10E40C: s = min(month + 12·year, 30000) (unsigned); latched ? v53 = s − (u16)latch
    ///             : 0x104BD0() != 0 ? { latch = s, bit 0 set, v53 = 0 } : v53 = 0; then FALLS INTO
    ///             0x10E484 (no break, MIPS 0x10E458/0x10E484): v53 = +0x98 = VARIABLE 76
    ///   56..77    0x10E484: counter i − 56
    /// </code>
    /// ⭐ So v52 is a copy of v75 (the ticket counter's snapshot) and v53 of v76 (the stink bomb's): the
    /// shipped quirks the rules 1, 68 and 87 read. Everything else is <see cref="IAdvisorProducers"/>.</summary>
    public void Produce(int i)
    {
        if ((uint)i >= VariableCount) return;
        switch (i)
        {
            case ElapsedVariable: return;
            case 52:
                Variables[52] = Variables[75];
                return;
            case 53:
                int s = _producers.Months;
                if (UpgradeLatched) Variables[53] = unchecked((short)(s - (ushort)UpgradeMonthLatch));
                else if (_producers.UpgradeResearchPercent != 0)
                { UpgradeMonthLatch = unchecked((ushort)s); UpgradeLatched = true; Variables[53] = 0; }
                else Variables[53] = 0;
                Variables[53] = Variables[76];                             // fall-through into 0x10E484
                return;
        }
        if (i >= FirstCounterVariable) { Variables[i] = Counters[i - FirstCounterVariable]; return; }
        Variables[i] = _producers.Produce(i);
    }

    /// <summary>⭐ `0x10DDD8(ro, j, d)`: `0 ≤ j &lt; 22` (signed), `c = (s16)(c + d)`, then clamped to
    /// ±30000 (MIPS `0x10DDD8..0x10DE28`). The advisor's `0x1073C0` gates it on flags bit 3.</summary>
    public void AddCounter(int j, int d)
    {
        if (j < 0 || j >= CounterCount) return;
        short c = unchecked((short)(Counters[j] + d));
        if (c > CounterLimit) c = CounterLimit;
        if (c < -CounterLimit) c = -CounterLimit;
        Counters[j] = c;
    }
}
