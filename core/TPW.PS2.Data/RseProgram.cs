using System.Buffers.Binary;
using System.Text;

namespace TPW.PS2.Data;

/// <summary>RSSE executable image. Offsets follow the PS2 loader at 0x1bfdf8,
/// not a scan for opcode-looking words. See findings/rse-vm.md.</summary>
public sealed class RseProgram
{
    public readonly record struct Operand(uint Word)
    {
        public int Tag => (int)(Word >> 24);
        public int Index => (int)(Word & 0xffffff);
        public int Immediate => unchecked((short)Word);
        public override string ToString() => Tag switch
        {
            0x40 => $"v{Index}", 0x20 => $"@{Index}", 0x10 => $"s+{Index}",
            _ => Immediate.ToString()
        };
    }

    public sealed record Instruction(int Address, RseOpcode Opcode, IReadOnlyList<Operand> Operands)
    {
        public int Next => Address + 1 + Operands.Count;
        public override string ToString() => $"{Address}: {Opcode} {string.Join(" ", Operands)}";
    }

    public int VariableCount { get; }
    public int StackSize { get; }
    public int SliceBudget { get; }
    public int LimboCapacity { get; }
    public int BounceCapacity { get; }
    public int WalkCapacity { get; }
    public int CodeWords { get; }
    public IReadOnlyList<string> VariableNames { get; }
    public IReadOnlyList<Instruction> Instructions { get; }
    readonly Dictionary<int, Instruction> _code = new();
    readonly byte[] _strings;

    public RseProgram(byte[] data)
    {
        if (data.Length < 56 || !data.AsSpan(0, 4).SequenceEqual("RSSE"u8)
            || U32(data, 4) != 0x10f51 || !data.AsSpan(32, 16).SequenceEqual("Pad Pad Pad Pad "u8))
            throw new InvalidDataException("Invalid RSSE header/version");
        VariableCount = Size(data, 8); StackSize = Size(data, 12); SliceBudget = Size(data, 16);
        LimboCapacity = Size(data, 20); BounceCapacity = Size(data, 24); WalkCapacity = Size(data, 28);
        CodeWords = Size(data, 48);
        int end = checked(52 + CodeWords * 4);
        if (end > data.Length - 4 || SliceBudget == 0) throw new InvalidDataException("Invalid RSSE code extent/slice budget");
        int pos = end;
        _strings = Block(data, ref pos);
        var names = new string[VariableCount];
        for (int i = 0; i < names.Length; i++) names[i] = Decode(Block(data, ref pos));
        if (pos != data.Length) throw new InvalidDataException("Trailing bytes after RSSE variable names");
        VariableNames = Array.AsReadOnly(names);
        var instructions = new List<Instruction>();
        for (int word = 0; word < CodeWords;)
        {
            int address = word;
            uint raw = U32(data, 52 + word++ * 4);
            if ((raw >> 24) != 0x80 || !RseOpcodes.Arity.TryGetValue((RseOpcode)(raw & 0xffffff), out int arity))
                throw new InvalidDataException($"Unknown RSSE instruction 0x{raw:x8} at {address}");
            if (word + arity > CodeWords) throw new InvalidDataException($"Truncated RSSE instruction at {address}");
            var operands = new Operand[arity];
            for (int i = 0; i < arity; i++)
            {
                var op = operands[i] = new Operand(U32(data, 52 + word++ * 4));
                if (op.Tag is not (0 or 0x10 or 0x20 or 0x40)) throw new InvalidDataException($"Invalid operand tag at {word - 1}");
                if (op.Tag == 0x40 && op.Index >= VariableCount) throw new InvalidDataException($"Invalid variable at {word - 1}");
                if (op.Tag == 0x10) StringAt(op.Index);
            }
            var ins = new Instruction(address, (RseOpcode)(raw & 0xffffff), Array.AsReadOnly(operands));
            _code.Add(address, ins); instructions.Add(ins);
        }
        foreach (var ins in instructions)
            foreach (var op in ins.Operands)
                if (op.Tag == 0x20 && !_code.ContainsKey(op.Index))
                    throw new InvalidDataException($"Branch into a non-instruction: {ins}");
        Instructions = instructions.AsReadOnly();
    }

    public Instruction At(int address) => _code.TryGetValue(address, out var ins) ? ins
        : throw new InvalidDataException($"RSSE PC {address} is not an instruction");
    public int VariableIndex(string name)
    {
        for (int i = 0; i < VariableNames.Count; i++)
            if (VariableNames[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
        throw new KeyNotFoundException($"RSSE variable '{name}' is not declared");
    }
    public string StringAt(int offset)
    {
        if (offset < 0 || offset >= _strings.Length || (offset != 0 && _strings[offset - 1] != 0))
            throw new InvalidDataException($"RSSE string offset {offset} is not a string boundary");
        int end = Array.IndexOf(_strings, (byte)0, offset);
        if (end < 0) throw new InvalidDataException("Unterminated RSSE string");
        return Encoding.ASCII.GetString(_strings, offset, end - offset);
    }
    static uint U32(byte[] d, int p) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p, 4));
    static int Size(byte[] d, int p)
    {
        uint n = U32(d, p);
        if (n > 1_000_000) throw new InvalidDataException($"RSSE size out of bounds at {p}");
        return (int)n;
    }
    static byte[] Block(byte[] d, ref int p)
    {
        if (p > d.Length - 4) throw new InvalidDataException("Truncated RSSE string length");
        int n = Size(d, p); p += 4;
        if (n > d.Length - p) throw new InvalidDataException("Truncated RSSE string block");
        var result = d.AsSpan(p, n).ToArray(); p += n; return result;
    }
    static string Decode(byte[] d)
    {
        if (d.Length == 0) return "";
        if (d[^1] != 0 || d.AsSpan(0, d.Length - 1).Contains((byte)0))
            throw new InvalidDataException("Invalid RSSE variable name");
        return Encoding.ASCII.GetString(d, 0, d.Length - 1);
    }
}
