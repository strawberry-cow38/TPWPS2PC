using System.Buffers.Binary;

namespace TPW.PS2.Data;

public sealed class LipTrack
{
    public IReadOnlyList<uint> Microseconds { get; }
    public LipTrack(byte[] data)
    {
        if (data.Length < 4 || data.Length % 4 != 0 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - 4)) != uint.MaxValue)
            throw new InvalidDataException("LIP requires aligned marks and a final FFFFFFFF");
        var marks = new uint[data.Length / 4 - 1];
        for (int i = 0; i < marks.Length; i++)
        {
            marks[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i * 4));
            if (marks[i] == uint.MaxValue || (i > 0 && marks[i] <= marks[i - 1]))
                throw new InvalidDataException("LIP marks must increase, with no embedded terminator");
        }
        Microseconds = Array.AsReadOnly(marks);
    }

    /// <summary>Original lip gate: initially active, signed integer mark/1000 compared unsigned
    /// strictly below elapsed milliseconds, at most ONE transition per update; terminator disables it.
    /// Active is NOT a specific mouth pose: the game also varies shapes while this gate stays active.</summary>
    public sealed class Playback
    {
        readonly LipTrack _track;
        int _next;
        public bool Active { get; private set; } = true;
        /// <summary>`0x105F30`'s return for the last <see cref="Advance"/>: a mark was consumed, or the
        /// terminator was read (every call once past the end). The advisor's mouth (`0x106B54`) keys on it.</summary>
        public bool Stepped { get; private set; }
        public Playback(LipTrack track) { _track = track; }
        public bool Advance(uint elapsedMilliseconds)
        {
            Stepped = false;
            if (_next == _track.Microseconds.Count) { Active = false; Stepped = true; }
            else if (unchecked((uint)((int)_track.Microseconds[_next] / 1000)) < elapsedMilliseconds)
            { _next++; Active = !Active; Stepped = true; }
            return Active;
        }
    }
}
