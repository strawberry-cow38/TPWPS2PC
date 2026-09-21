using NLayer;

namespace TPW.PS2.Data;

/// <summary>MPEG audio Layer II, decoded to PCM.
///
/// ⚠⚠ THE ENGINE CANNOT DO THIS ONE. Godot's `AudioStreamMP3` is built around a Layer III decoder
/// and returns a zero-length stream for these, which is 1,849 of the disc's 2,220 sounds -- the
/// music, all of the advisor speech, and most of the effects. NLayer is a pure managed Layer
/// I/II/III decoder, so it works wherever the rest of this does and needs no native build.</summary>
public static class Mpeg
{
    /// <summary>Decode to interleaved signed 16-bit PCM. Returns null if nothing decoded.</summary>
    public static (byte[] Pcm, int Rate, int Channels)? DecodeToPcm16(byte[] data)
    {
        using var ms = new MemoryStream(data);
        var f = new MpegFile(ms);
        int ch = f.Channels, rate = f.SampleRate;
        if (ch <= 0 || rate <= 0) return null;
        var buf = new float[rate * ch];          // a second at a time
        var outBytes = new List<byte>(data.Length * 8);
        int got;
        while ((got = f.ReadSamples(buf, 0, buf.Length)) > 0)
            for (int i = 0; i < got; i++)
            {
                int v = (int)MathF.Round(Math.Clamp(buf[i], -1f, 1f) * 32767f);
                outBytes.Add((byte)(v & 0xFF));
                outBytes.Add((byte)((v >> 8) & 0xFF));
            }
        return outBytes.Count == 0 ? null : (outBytes.ToArray(), rate, ch);
    }
}
