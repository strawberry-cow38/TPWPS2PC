using System.Numerics;
using TPW.PS2.Data;

/// <summary>⭐ SURFACE FITTINGS -- the seats that ride their parent's FACE (Model.SurfaceFrame, `0x1F1248` and
/// `0x1F0D70`), held against the one independent reference the disc carries: every surface fitting's HELPER
/// NODE, whose bind matrix the exporter wrote. The game never reads that matrix for these fittings once the
/// parent moves its vertices -- it recomputes from the face -- so at the bind pose the two should agree, and
/// where they do the formula is right for a reason rather than by assertion.
///
/// ⚠ THE ORIENTATION DOES NOT AGREE EVERYWHERE, AND THE CHECK SAYS HOW MUCH. Median 0.3 degrees, but up to
/// 23 on Wormhole, whose seats turn about all three axes; single-axis seats agree to 0.0. Of the 96 Euler
/// orders and sign conventions the READ one is the ONLY one that keeps every seat within 30 degrees (the
/// next best reaches 66), so the residual is the exporter's, not the composition's: the game draws the
/// formula's answer, and so does the port.</summary>
static class SurfaceSeatChecks
{
    public static void Run(Disc disc, Action<bool, string> check)
    {
        void Check(bool ok, string label) => check(ok, "surface seat: " + label);
        int surfaces = 0, onHelper = 0, flippedOff = 0, turned = 0, gated = 0, turnedGated = 0;
        var ungated = new SortedSet<string>();
        var models = new HashSet<string>();
        var errRead = new List<float>(); var errBare = new List<float>(); var errReversed = new List<float>();
        string worstPoint = null; float worstPointD = 0;
        (Model M, Model.Fitting F, List<Vector3> Pos, Matrix4x4 W)? witness = null;

        foreach (var w in disc.Files().Where(f => !f.IsDirectory && f.Path.EndsWith(".WAD", StringComparison.OrdinalIgnoreCase)))
        {
            WadArchive wad;
            try { wad = new WadArchive(disc.Read(w.Extent, w.Size)); } catch { continue; }
            foreach (var e in wad.Entries)
            {
                if (WadArchive.IsAlias(e) || !e.Path.EndsWith(".mps", StringComparison.OrdinalIgnoreCase)) continue;
                Model m; IReadOnlyList<Model.Fitting> fits;
                try { m = new Model(wad.Read(e)); fits = m.Fittings; } catch { continue; }
                if (!fits.Any(f => f.OnSurface != null)) continue;
                var apsEntry = wad.Entries.FirstOrDefault(x => x.Path.Equals(Path.ChangeExtension(e.Path, ".aps"), StringComparison.OrdinalIgnoreCase));
                Animation anim = null;
                try { if (apsEntry != null) anim = new Animation(wad.Read(apsEntry)); } catch { }
                foreach (var f in fits)
                {
                    if (f.OnSurface is not { } sf) continue;
                    int p = m.NodeParent(f.Node);
                    if (p < 0 || p >= m.Meshes.Count) continue;
                    if (!m.BindWorld.TryGetValue(m.Meshes[p].Offset, out var pw) || !m.BindWorld.TryGetValue(m.NodeOffset(f.Node), out var hw)) continue;
                    var pos = m.Vertices(m.Meshes[p]).Pos;
                    if (m.SurfaceFrame(f, pos, pos, pw) is not { } s) continue;
                    surfaces++; models.Add(w.Path + e.Path);
                    bool live = anim != null && anim.AnimatesVertices(p);
                    if (live) gated++; else ungated.Add(Path.GetFileNameWithoutExtension(e.Path));
                    if (live && (f.Flags & 0x40000) != 0) turnedGated++;
                    // THE POINT: the helper's bind origin is where the exporter put the seat.
                    var origin = new Vector3(hw.M41, hw.M42, hw.M43);
                    float d = (s.Point - origin).Length();
                    if (d < 1e-3f) onHelper++;
                    else if (d > worstPointD) { worstPointD = d; worstPoint = $"{e.Path} {m.NodeName(f.Node)} {d:F4}"; }
                    // The control: the same seat with its normal the other way. Every seat lifted off its face
                    // (h != 0) must leave the helper, or the point test cannot see a wrong winding.
                    if (sf.H != 0 && m.SurfaceFrame(f with { OnSurface = sf with { H = -sf.H } }, pos, pos, pw) is { } flip
                        && (flip.Point - origin).Length() > 1e-3f) flippedOff++;
                    if (!s.Turned) continue;
                    turned++;
                    var helper = Rows(Unit(hw.M11, hw.M12, hw.M13), Unit(hw.M21, hw.M22, hw.M23), Unit(hw.M31, hw.M32, hw.M33));
                    errRead.Add(Angle(Rows(s.X, s.Y, s.Z), helper));
                    // The controls: the face frame with no turn at all, and the turn in the opposite order.
                    if (m.SurfaceFrame(f with { Angles = Vector3.Zero }, pos, pos, pw) is { } bare)
                    {
                        var face = Rows(bare.X, bare.Y, bare.Z);
                        errBare.Add(Angle(face, helper));
                        var a = f.Angles;
                        var reversed = Matrix4x4.CreateRotationX(a.X) * Matrix4x4.CreateRotationY(a.Y) * Matrix4x4.CreateRotationZ(a.Z) * face;
                        errReversed.Add(Angle(reversed, helper));
                    }
                    if (witness == null && sf.H != 0) witness = (m, f, pos, pw);
                }
            }
        }

        Check(surfaces >= 300 && models.Count >= 36, $"{surfaces} surface fittings on {models.Count} models across the disc");
        Check(onHelper == surfaces, $"the point: {onHelper} of {surfaces} sit on their helper's bind origin to 1e-3"
                                    + (worstPoint == null ? "" : $" (worst {worstPoint})"));
        Check(flippedOff > 0 && flippedOff >= surfaces / 4,
              $"the point control: with the normal flipped, {flippedOff} seats leave their helper -- a wrong winding is visible");
        // ⭐ THE GATE (`0x1F1248` wants the parent's runtime 0x200000, which `0x1AABB0` sets from any record's
        // 0x41000 track). Both sides are populated: 270 parents move their vertices, and 32 fittings on cars
        // and floors (caterbd, gforce, snake, mammtunn, a backup bug) keep their helper's matrix in the game.
        Check(gated > 0 && ungated.Count > 0 && turnedGated == turned,
              $"the gate: {gated} of {surfaces} parents move their vertices in some record, {surfaces - gated} do not ({string.Join(" ", ungated)}); "
              + $"{turnedGated} of {turned} turned seats are on a moving parent");
        errRead.Sort(); errBare.Sort(); errReversed.Sort();
        float Median(List<float> l) => l.Count == 0 ? float.NaN : l[l.Count / 2];
        float Max(List<float> l) => l.Count == 0 ? float.NaN : l[^1];
        Check(turned >= 130 && Median(errRead) < 1f && Max(errRead) < 30f,
              $"the turn: {turned} turned seats, median {Median(errRead):F2} deg and at most {Max(errRead):F1} from their helper's axes");
        Check(Median(errBare) > 20f && Max(errBare) > 90f,
              $"the turn control: WITHOUT the fitting's angles the face frame is a median {Median(errBare):F1} deg off (max {Max(errBare):F0}) -- the angles are load-bearing");
        Check(Max(errReversed) > 45f,
              $"the order control: Rx Ry Rz instead of Rz Ry Rx puts a seat {Max(errReversed):F0} deg off -- the order is visible");

        // ⚠ THE FACING BIT IS READ FROM THE `facing` LIST. A morph that replaces the positions leaves the low
        // bit of vertex 2's y as arithmetic noise; toggling that one bit in the DRAWN copy must change nothing
        // when the bind copy is passed as `facing`, and must flip the face when it is not.
        if (witness is { } wv)
        {
            var sf = wv.F.OnSurface.Value;
            int at = wv.M.BatchVertexBase(wv.M.Meshes[wv.M.NodeParent(wv.F.Node)], sf.Batch) + sf.FirstVertex + 2;
            var noisy = new List<Vector3>(wv.Pos);
            var v2 = noisy[at];
            noisy[at] = new Vector3(v2.X, BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v2.Y) ^ 1), v2.Z);
            var clean = wv.M.SurfaceFrame(wv.F, wv.Pos, wv.Pos, wv.W);
            var kept = wv.M.SurfaceFrame(wv.F, noisy, wv.Pos, wv.W);
            var lost = wv.M.SurfaceFrame(wv.F, noisy, noisy, wv.W);
            Check(clean is { } c && kept is { } k && lost is { } l
                  && Vector3.Dot(c.Y, k.Y) > 0.999f && Vector3.Dot(c.Y, l.Y) < -0.9f,
                  $"the facing bit: one bit of noise in the drawn vertex 2's y leaves the face alone when the bind list is the "
                  + $"source, and turns it over when the drawn list is ({wv.M.NodeName(wv.F.Node)})");
        }
        else Check(false, "the facing bit: no turned seat lifted off its face to test on");
    }

    static Vector3 Unit(float x, float y, float z) => Vector3.Normalize(new Vector3(x, y, z));
    static Matrix4x4 Rows(Vector3 a, Vector3 b, Vector3 c) => new(a.X, a.Y, a.Z, 0, b.X, b.Y, b.Z, 0, c.X, c.Y, c.Z, 0, 0, 0, 0, 1);
    /// <summary>The angle of the rotation taking one orthonormal frame to the other, in degrees.</summary>
    static float Angle(Matrix4x4 a, Matrix4x4 b)
    {
        var r = a * Matrix4x4.Transpose(b);
        return MathF.Acos(Math.Clamp((r.M11 + r.M22 + r.M33 - 1f) / 2f, -1f, 1f)) * 180f / MathF.PI;
    }
}
