namespace TPW.PS2.Data;

/// <summary>A `.SDT` sound bank. Two variants, told apart by the u16 at +0x02 -- see
/// findings/formats.md. The header is the same 40 bytes either way; version 12345 moves the
/// headers into a table of their own instead of putting each before its own data.</summary>
public sealed class SoundBank
{
    public const int Streams = 12345;

    public sealed class Sound
    {
        public string Name = "";
        public int Start, End;
        /// <summary>The header's tag byte: 0x24 mono MPEG, 0x25 stereo MPEG, 0x80 Sony ADPCM,
        /// 0x00 an empty slot. Cross-checked against every bitstream on the disc: 0x24 is mono on
        /// all 97,667 of its frames and 0x25 stereo on all 169,211, nothing on either
        /// off-diagonal.</summary>
        public byte Tag;
        /// <summary>Length in milliseconds. The header's +0x20 word is that x 44.1 x channels,
        /// which agrees with the matching *SFX.MAP entry on all 939 pairs.</summary>
        public int Milliseconds;
        public bool IsMpeg => Tag == 0x24 || Tag == 0x25;
        public bool IsAdpcm => Tag == 0x80;
        public bool IsEmpty => Tag == 0x00 || End <= Start;
        public int Channels => Tag == 0x25 ? 2 : 1;
    }

    public readonly List<Sound> Sounds = new();
    readonly byte[] _d;
    public byte[] Data => _d;

    public SoundBank(byte[] d)
    {
        _d = d;
        if (d.Length < 8) throw new InvalidDataException("too short for a .SDT");
        int n = BitConverter.ToUInt16(d, 0), ver = BitConverter.ToUInt16(d, 2);
        if (n == 0 || 4 + 4 * n > d.Length) throw new InvalidDataException("bad .SDT count");
        var offs = new int[n];
        for (int i = 0; i < n; i++) offs[i] = (int)BitConverter.ToUInt32(d, 4 + i * 4);
        int htab = ver == Streams ? 4 + 4 * n : -1;
        for (int i = 0; i < n; i++)
        {
            int h = htab >= 0 ? htab + i * 40 : offs[i];
            if (h + 40 > d.Length) break;
            int hs = (int)BitConverter.ToUInt32(d, h), ds = (int)BitConverter.ToUInt32(d, h + 4);
            int a = htab >= 0 ? offs[i] : offs[i] + hs;
            if (a < 0 || a > d.Length) break;
            int lenWord = (int)BitConverter.ToUInt32(d, h + 0x20);
            byte tag = d[h + 0x1B];
            var name = System.Text.Encoding.Latin1.GetString(d, h + 8, 16).Split('\0')[0];
            int chans = tag == 0x25 ? 2 : 1;
            Sounds.Add(new Sound
            {
                Name = name, Tag = tag, Start = a, End = Math.Min(a + ds, d.Length),
                Milliseconds = (int)Math.Round(lenWord / (44.1 * chans)),
            });
        }
    }
}
