using NLayer;

// The acceptance gate for any MP2 decoder change. It never touches the disc: a known tone is
// encoded by ffmpeg four ways and each decode is fitted against ffmpeg's own.
//
// ⚠ All four rows matter. MPEG-1 is here even though it already passes, because a patch that fixes
// LSF while breaking MPEG-1 would otherwise read as a clean win. And STEREO is here because the
// disc's 442 stereo sounds -- every music bank -- go down the mode path, so a mono-only gate would
// pass while the music stayed broken. The stereo signal is 440 Hz left against 660 Hz right, so a
// channel collapse or swap cannot hide inside it either.
static (int n, double gain, double residual, double peak) Fit(string mp2, string refRaw, int ch)
{
    var f = new MpegFile(new MemoryStream(File.ReadAllBytes(mp2)));
    var buf = new float[f.SampleRate * f.Channels];
    var nl = new List<float>(); int got;
    while ((got = f.ReadSamples(buf, 0, buf.Length)) > 0) for (int i = 0; i < got; i++) nl.Add(buf[i]);
    var rb = File.ReadAllBytes(refRaw);
    int n = Math.Min(nl.Count, rb.Length / 2);
    double num = 0, den = 0;
    for (int i = 0; i < n; i++) { double r = BitConverter.ToInt16(rb, i * 2); num += r * nl[i]; den += (double)nl[i] * nl[i]; }
    double k = num / den, res = 0, sig = 0;
    for (int i = 0; i < n; i++)
    {
        double r = BitConverter.ToInt16(rb, i * 2), e = r - k * nl[i];
        res += e * e; sig += r * r;
    }
    double pk = 0; foreach (var v in nl) pk = Math.Max(pk, Math.Abs(v));
    return (n, k, 100 * Math.Sqrt(res / sig), pk);
}

var rows = new (string Label, string Mp2, string Ref, int Ch)[]
{
    ("MPEG-1  LII  44100 mono",   "mpeg1.mp2",    "ref1.raw",    1),
    ("MPEG-1  LII  44100 STEREO", "st_mpeg1.mp2", "st_ref1.raw", 2),
    ("MPEG-2  LII  22050 mono",   "mpeg2.mp2",    "ref2.raw",    1),
    ("MPEG-2  LII  22050 STEREO", "st_mpeg2.mp2", "st_ref2.raw", 2),
};
Console.WriteLine("  {0,-26} {1,8} {2,10} {3,10} {4,7}  {5}", "case", "samples", "gain", "residual", "peak", "verdict");
int bad = 0;
foreach (var r in rows)
{
    var (n, k, res, pk) = Fit(r.Mp2, r.Ref, r.Ch);
    bool ok = res < 1.0;
    if (!ok) bad++;
    Console.WriteLine("  {0,-26} {1,8} {2,10:F1} {3,9:F2}% {4,7:F3}  {5}",
        r.Label, n, k, res, pk, ok ? "PASS" : "*** FAIL ***");
}
Console.WriteLine($"\n  {rows.Length - bad} of {rows.Length} pass (threshold: residual < 1% of signal)");
// ⚠ Exit non-zero when a row fails. A gate that always returns 0 gates nothing, and this one is
// meant to be the thing that says whether an MP2 decoder change worked.
return bad == 0 ? 0 : 2;
