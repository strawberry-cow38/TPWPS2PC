using System.Numerics;

namespace TPW.PS2.Data;

/// <summary>One quad of a coaster track mesh, in the corner order every style emits
/// (findings/coaster-geometry.md §5.1): c0 = next sample on the first edge, c1 = this sample on the
/// first edge, c2 = next sample on the second edge, c3 = this sample on the second edge; triangles
/// (c0, c1, c2) and (c1, c2, c3). <see cref="Slot"/> is the style's texture slot; <see cref="Scroll"/>
/// marks the bands whose V the console rewrites with the scroll phase every update (fn2).</summary>
public readonly record struct CoasterQuad(int Slot, bool Scroll,
    Vector3 C0, Vector3 C1, Vector3 C2, Vector3 C3,
    Vector2 T0, Vector2 T1, Vector2 T2, Vector2 T3);

/// <summary>⭐ THE SEVEN CROSS-SECTIONS (fn1 of each style, coaster-geometry.md §5.2). Every offset is
/// in cells from the sample's point P along its RAW side vector S and its unit up N. There are no
/// sleepers, rail tubes or supports as geometry: rails, sleepers and chain links are all texture.
/// "Winch" (the lift's chain band) is read only by A and G.</summary>
public static class CoasterMesh
{
    public static List<CoasterQuad> Build(CoasterNode n, char style)
    {
        var quads = new List<CoasterQuad>();
        var s = n.Samples;
        for (int q = 0; q < 16; q++)
        {
            var a = s[q]; var b = s[q + 1];
            bool winch = a.Winch;
            // V at c0/c2 is this sample's END value, at c1/c3 its START value (§5.1).
            float v0 = a.V0, v1 = a.V1, w0 = a.W0, w1 = a.W1;
            void Band(int slot, bool scroll, Func<CoasterSample, Vector3> e1, Func<CoasterSample, Vector3> e2,
                      float u1, float u2, bool fourTimes = false)
            {
                float lo = fourTimes ? w0 : v0, hi = fourTimes ? w1 : v1;
                quads.Add(new CoasterQuad(slot, scroll, e1(b), e1(a), e2(b), e2(a),
                    new Vector2(u1, hi), new Vector2(u1, lo), new Vector2(u2, hi), new Vector2(u2, lo)));
            }
            Func<CoasterSample, Vector3> K(float k) => x => x.Pos + k * x.Side;
            Func<CoasterSample, Vector3> KN(float k, float m) => x => x.Pos + k * x.Side + m * x.Normal;
            switch (style)
            {
                case 'A':
                    Band(0, false, K(-0.3f), K(-0.075f), 0f, 0.425f);
                    if (!winch) Band(0, false, K(-0.075f), K(0.3f), 0.425f, 1f);
                    else
                    {
                        Band(0, false, K(0.075f), K(0.3f), 0.575f, 1f);
                        Band(1, true, K(-0.075f), K(0.075f), 0f, 1f, fourTimes: true);
                    }
                    break;
                case 'B':
                {
                    // Floor: water when level or falling within 1 %, else the uphill texture.
                    float cy = (a.Pos - 0.5f * a.Normal).Y, ny = (b.Pos - 0.5f * b.Normal).Y;
                    Band(ny * 0.99f < cy ? 0 : 1, true, K(-0.3f), K(0.3f), 0f, 1f);
                    Band(2, false, KN(-0.36f, 0.1f), KN(0, -0.5f), 1f, 0f);
                    Band(2, false, KN(0, -0.5f), KN(0.36f, 0.1f), 0f, 1f);
                    break;
                }
                case 'C':
                {
                    var r = new[] { KN(0, 0.05f), K(0.05f), KN(0, -0.05f), K(-0.05f) };
                    for (int f = 0; f < 4; f++) Band(0, false, r[f], r[(f + 1) & 3], 0f, 1f, fourTimes: true);
                    break;
                }
                case 'D':
                    Band(0, false, K(-0.1f), K(0.1f), 0f, 0.5f);
                    Band(0, false, K(0.1f), KN(0, -0.1f), 0.5f, 1f);
                    Band(0, false, KN(0, -0.1f), K(-0.1f), 0.5f, 1f);
                    break;
                case 'E':
                {
                    var e = new[] { K(-0.4f), KN(0, 0.6f), K(0.4f), KN(0, -0.6f) };
                    for (int f = 0; f < 4; f++) Band(0, true, e[f], e[(f + 1) & 3], 0f, 1f);
                    break;
                }
                case 'F':
                    Band(0, false, K(-0.3f), K(-0.1f), 1f, 0f);
                    Band(0, false, K(0.1f), K(0.3f), 0f, 1f);
                    break;
                case 'G':
                    if (!winch) Band(0, false, K(-0.3f), K(0.3f), 0f, 1f);
                    else
                    {
                        Band(0, false, K(-0.3f), K(-0.045f), 0f, 0.425f);
                        Band(0, false, K(0.045f), K(0.3f), 0.575f, 1f);
                        Band(1, true, K(-0.045f), K(0.045f), 0f, 1f, fourTimes: true);
                    }
                    Band(2, false, KN(0, -0.5f), KN(-0.54f, 0.4f), 0f, 0.95f);
                    Band(2, false, KN(0, -0.5f), KN(0.54f, 0.4f), 0f, 0.95f);
                    break;
            }
        }
        return quads;
    }

    /// <summary>`0x19d1e0`: a segment is drawn when it has a prev whose position differs from its own,
    /// and it is not Moonshot's exit (its station segment is hidden).</summary>
    public static bool Visible(CoasterTrack t, CoasterNode n) =>
        n.Prev != null && (n.Prev.X != n.X || n.Prev.Z != n.Z || n.Prev.YBase != n.YBase)
        && !(t.Type.IsMoonshot && n == t.Exit);
}
