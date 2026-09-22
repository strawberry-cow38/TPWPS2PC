using System.Buffers.Binary;

namespace TPW.PS2.Data;

/// <summary>The separate advisor VM in headers.ass/opcodes.ass. See findings/advisor.md.
/// Variables are signed halfwords; their game-state producers are deliberately outside this VM.</summary>
public sealed class AdvisorRules
{
    public enum Op : short { End, Equal, NotEqual, Less, Greater, Add, Set, Message, TextUi, ElapsedGreater }
    public enum Result { Completed, ConditionFailed, ElapsedBlocked }
    public record Instruction(int WordOffset, Op Code, short A = 0, short B = 0);
    public record Rule(int WordOffset, ushort DelayDays, uint SavedNextDay, uint SavedLastFailureDay,
                       IReadOnlyList<Instruction> Instructions);
    public record Effect(Op Code, ushort MessageId);
    public record Evaluation(Result Result, IReadOnlyList<Effect> Effects);
    public IReadOnlyList<Rule> Rules { get; }

    public AdvisorRules(byte[] headers, byte[] opcodes)
    {
        if (headers.Length == 0 || headers.Length % 12 != 0 || headers.Length / 12 > 255 ||
            opcodes.Length == 0 || opcodes.Length % 2 != 0)
            throw new InvalidDataException("Invalid advisor table lengths");
        var words = new short[opcodes.Length / 2];
        for (int i = 0; i < words.Length; i++) words[i] = BinaryPrimitives.ReadInt16LittleEndian(opcodes.AsSpan(i * 2));
        var rules = new List<Rule>();
        int expected = 0;
        for (int h = 0; h < headers.Length; h += 12)
        {
            int start = BinaryPrimitives.ReadInt16LittleEndian(headers.AsSpan(h));
            int end = h + 12 == headers.Length ? words.Length : BinaryPrimitives.ReadInt16LittleEndian(headers.AsSpan(h + 12));
            if (start != expected || end <= start || end > words.Length)
                throw new InvalidDataException($"Advisor header {h / 12}: invalid word span");
            int p = start;
            var instructions = new List<Instruction>();
            while (p < end)
            {
                int at = p;
                short code = words[p++];
                if (code < 0 || code > 9) throw new InvalidDataException($"Unknown advisor opcode {code} at word {at}");
                int operands = code == 0 ? 0 : code <= 6 ? 2 : 1;
                if (p + operands > end) throw new InvalidDataException($"Truncated advisor instruction at word {at}");
                short a = operands > 0 ? words[p++] : (short)0;
                short b = operands > 1 ? words[p++] : (short)0;
                if (code is >= 1 and <= 6 && (a < 0 || a >= 79 || (code == 6 && a == 78)))
                    throw new InvalidDataException($"Invalid advisor variable {a} at word {at}");
                if (code is 7 or 8 && (a < 0 || a >= AdvisorCatalogue.MessageCount))
                    throw new InvalidDataException($"Invalid advisor message {a} at word {at}");
                instructions.Add(new Instruction(at, (Op)code, a, b));
                if (code == 0)
                {
                    if (p != end) throw new InvalidDataException($"Unconsumed words after advisor END at {at}");
                    break;
                }
            }
            if (instructions[^1].Code != Op.End) throw new InvalidDataException($"Advisor rule {h / 12} has no END");
            rules.Add(new Rule(start, BinaryPrimitives.ReadUInt16LittleEndian(headers.AsSpan(h + 2)),
                BinaryPrimitives.ReadUInt32LittleEndian(headers.AsSpan(h + 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(headers.AsSpan(h + 8)), instructions.AsReadOnly()));
            expected = end;
        }
        Rules = rules.AsReadOnly();
    }

    /// <summary>Execute one rule, preserving effects and stores before a failed guard.
    /// Set mirrors variables 56..77 to event counters 0..21; Add does not.
    /// Caller supplies variable 78 (elapsed days) and handles scheduling/queues.</summary>
    public Evaluation Evaluate(int rule, Span<short> variables, Span<short> eventCounters)
    {
        if (variables.Length != 79 || eventCounters.Length != 22) throw new ArgumentException("Expected 79 variables and 22 event counters");
        var effects = new List<Effect>();
        foreach (var ins in Rules[rule].Instructions)
        {
            bool pass = true;
            switch (ins.Code)
            {
                case Op.End: return new Evaluation(Result.Completed, effects.AsReadOnly());
                case Op.Equal: pass = variables[ins.A] == ins.B; break;
                case Op.NotEqual: pass = variables[ins.A] != ins.B; break;
                case Op.Less: pass = variables[ins.A] < ins.B; break;
                case Op.Greater: pass = variables[ins.A] > ins.B; break;
                case Op.Add: variables[ins.A] = unchecked((short)(variables[ins.A] + ins.B)); break;
                case Op.Set:
                    variables[ins.A] = ins.B;
                    if (ins.A >= 56) eventCounters[ins.A - 56] = ins.B;
                    break;
                case Op.Message: case Op.TextUi: effects.Add(new Effect(ins.Code, (ushort)ins.A)); break;
                case Op.ElapsedGreater:
                    if (variables[78] <= ins.A) return new Evaluation(Result.ElapsedBlocked, effects.AsReadOnly());
                    break;
            }
            if (!pass) return new Evaluation(Result.ConditionFailed, effects.AsReadOnly());
        }
        throw new InvalidDataException("Advisor rule has no END");
    }
}
