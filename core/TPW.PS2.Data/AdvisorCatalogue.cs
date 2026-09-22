using System.Buffers.Binary;
using System.Text;

namespace TPW.PS2.Data;

/// <summary>Advisor message metadata embedded in the European SLES_500.32 executable.
/// Region of the text database and language of the audio are separate, explicit choices.</summary>
public sealed class AdvisorCatalogue
{
    public const int MessageCount = 275, BlankTextRow = 310;
    public const uint TableAddress = 0x2a6ac8;
    public record Voice(ushort SoundId, byte AnimationSelector, byte UnknownByte, string LipStem, uint SavedLipPointer)
    {
        public int? SoundIndex => SoundId == 0 ? null : SoundId - 1;
    }
    public record Message(int Id, string SymbolicKey, ushort TextRow, byte VariantCount,
                          byte InitialVariant, IReadOnlyList<Voice> Voices)
    {
        public bool HasText => TextRow != BlankTextRow;
    }
    public record Binding(Message Message, Voice Voice, SoundBank.Sound Sound, string Subtitle,
                          string LipPath, LipTrack Lip, bool SoundStemMatches);
    public IReadOnlyList<Message> Messages { get; }

    public AdvisorCatalogue(byte[] executable)
    {
        var elf = new Elf(executable);
        // Profile signatures from consumers, not a scan for a conveniently sized table.
        foreach (var (address, word) in new[] { (0x106420u, 0x2a020113u), (0x107918u, 0x24040038u),
                                               (0x107928u, 0x24426ac8u), (0x263934u, 0x2610ffffu) })
            if (elf.U32(address) != word) throw new InvalidDataException("Unsupported advisor executable profile (expected SLES_500.32)");
        var messages = new List<Message>();
        for (int i = 0; i < MessageCount; i++)
        {
            uint p = TableAddress + (uint)i * 56;
            var d = elf.Bytes(p, 56);
            string key = elf.String(BinaryPrimitives.ReadUInt32LittleEndian(d));
            ushort row = BinaryPrimitives.ReadUInt16LittleEndian(d[4..]);
            byte count = d[6], selected = d[7];
            if (string.IsNullOrEmpty(key) || count > 4 || (count > 0 && selected >= count))
                throw new InvalidDataException($"Invalid advisor message {i}");
            var voices = new List<Voice>();
            for (int j = 0; j < 4; j++)
            {
                var v = d[(8 + j * 12)..];
                voices.Add(new Voice(BinaryPrimitives.ReadUInt16LittleEndian(v), v[2], v[3],
                    elf.String(BinaryPrimitives.ReadUInt32LittleEndian(v[4..])),
                    BinaryPrimitives.ReadUInt32LittleEndian(v[8..])));
            }
            messages.Add(new Message(i, key, row, count, selected, voices.AsReadOnly()));
        }
        Messages = messages.AsReadOnly();
    }

    public static AdvisorCatalogue Load(Disc disc)
    {
        var file = disc.Files().SingleOrDefault(e => e.Path.Equals("/SLES_500.32", StringComparison.OrdinalIgnoreCase));
        if (file == null) throw new InvalidDataException("Advisor catalogue supports the European SLES_500.32 executable only");
        return new AdvisorCatalogue(disc.Read(file.Extent, file.Size));
    }

    /// <summary>Checks identities, including shared id.dat ordering. Blank runtime text entries
    /// intentionally need not match their descriptive key.</summary>
    public void ValidateText(TextDatabase text)
    {
        if (text == null || text.Keys.Length <= BlankTextRow || text.Keys[BlankTextRow] != "STR_GIZMO_CPP_BLANK")
            throw new InvalidDataException("Advisor blank text sentinel differs");
        foreach (var m in Messages)
            if (m.TextRow >= text.Keys.Length || (m.HasText && text.Keys[m.TextRow] != m.SymbolicKey))
                throw new InvalidDataException($"Advisor message {m.Id}: text row does not identify {m.SymbolicKey}");
    }

    /// <summary>Bind the numeric sound selector independently of the lip stem. Missing lips and
    /// mismatched names remain visible (retail message 268 has exactly such a defect).</summary>
    public Binding Bind(int messageId, int variant, string audioLanguage, SoundBank bank,
                        WadArchive lips, TextDatabase text, string textLanguage)
    {
        if (audioLanguage is not ("English" or "French" or "German")) throw new ArgumentException("Unknown speech language", nameof(audioLanguage));
        var message = Messages[messageId];
        if (variant < 0 || variant >= message.VariantCount) throw new ArgumentOutOfRangeException(nameof(variant));
        var voice = message.Voices[variant];
        SoundBank.Sound sound = null;
        if (voice.SoundIndex is int index)
        {
            if (index >= bank.Sounds.Count) throw new InvalidDataException($"Advisor sound {voice.SoundId} exceeds bank");
            sound = bank.Sounds[index];
        }
        string path = voice.LipStem == null ? null : $"/{audioLanguage}/{voice.LipStem}.lip";
        var entry = path == null ? null : lips.Entries.SingleOrDefault(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        return new Binding(message, voice, sound, message.HasText ? text.Text(textLanguage, message.TextRow) : null,
            entry?.Path ?? path, entry == null ? null : new LipTrack(lips.Read(entry)),
            sound != null && Path.GetFileNameWithoutExtension(sound.Name).Equals(voice.LipStem, StringComparison.OrdinalIgnoreCase));
    }

    // ELF32 PT_LOAD mapping: no native disassembler or subprocess in the port.
    sealed class Elf
    {
        readonly byte[] _data;
        readonly List<(uint Address, uint Offset, uint Size)> _segments = new();
        public Elf(byte[] data)
        {
            _data = data;
            if (data.Length < 52 || !data.AsSpan(0, 6).SequenceEqual(new byte[] { 127, 69, 76, 70, 1, 1 }))
                throw new InvalidDataException("Expected little-endian ELF32");
            uint ph = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(28));
            int stride = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(42));
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(44));
            if (stride < 32 || (ulong)ph + (ulong)stride * (uint)count > (ulong)data.Length)
                throw new InvalidDataException("Invalid ELF program headers");
            for (int i = 0; i < count; i++)
            {
                var h = data.AsSpan((int)ph + i * stride, 32);
                if (BinaryPrimitives.ReadUInt32LittleEndian(h) != 1) continue;
                uint offset = BinaryPrimitives.ReadUInt32LittleEndian(h[4..]);
                uint address = BinaryPrimitives.ReadUInt32LittleEndian(h[8..]);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(h[16..]);
                if ((ulong)offset + size > (ulong)data.Length) throw new InvalidDataException("Truncated ELF segment");
                _segments.Add((address, offset, size));
            }
        }
        public ReadOnlySpan<byte> Bytes(uint address, int length)
        {
            foreach (var s in _segments)
                if (address >= s.Address && (ulong)address + (uint)length <= (ulong)s.Address + s.Size)
                    return _data.AsSpan((int)(s.Offset + address - s.Address), length);
            throw new InvalidDataException($"Unmapped ELF address 0x{address:x}");
        }
        public uint U32(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(address, 4));
        public string String(uint address)
        {
            if (address == 0) return null;
            var bytes = new List<byte>();
            for (int i = 0; i < 256; i++)
            {
                byte value = Bytes(checked(address + (uint)i), 1)[0];
                if (value == 0) return Encoding.Latin1.GetString(bytes.ToArray());
                bytes.Add(value);
            }
            throw new InvalidDataException("Unterminated advisor string");
        }
    }
}
