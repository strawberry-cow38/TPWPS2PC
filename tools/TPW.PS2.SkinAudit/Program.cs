using System.Numerics;
using TPW.PS2.Data;

// ⭐⭐ THE SKIN, WITH NO ENGINE. Reads every character in DATA.WAD's /Chars, skins its BIND POSE
// with bone matrices taken from the model's own hierarchy, and demands the mesh's own authored
// vertices back -- the check that a wrong matrix convention, a wrong bone index space, a wrong
// weight table or a wrong run list fails by thousands of units instead of producing a guest who
// is subtly inside out. Then it samples a record the way the game's sampler does and shows a
// named bone MOVE between two frames, the way the walking was proved.
//
// ⚠ WHAT THE CONTROL CAN AND CANNOT HOLD. The game never poses a character in its bind pose and
// stores no bind matrices; the rotations below come from the helper hierarchy through a rule
// MEASURED on the data (Model.SkinBindRotation says how and how well), and the translations are
// not in the file at all, so each (mesh, bone) translation is solved by least squares against
// the vertices. That leaves the rotations, the index space, the weights, the run list and the
// row-vector arithmetic under test -- a wrong one of those cannot be absorbed by 3 free numbers
// per bone -- and it means this audit is NOT evidence about the bind translations.
//
// ⚠ IT NEEDS THE OWNER'S DISC. Nothing is committed and nothing is cached.

if (args.Length < 1) { Console.Error.WriteLine("Usage: SkinAudit /path/to/disc.bin"); return 2; }
int bad = 0;
void Check(bool ok, string line) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + line); if (!ok) bad++; }

// ⭐ The threshold is a unit of the mesh's own vertex space, on characters 14,000-60,000 units
// across. The bind closes at a hundredth of a unit when everything is right; the wrong index
// space misses by 6,000 and more. Anything in between is a finding, not a tolerance.
const double Threshold = 1.0;
// ⚠ The sampler's matrix array on the PS2 stack holds 35 slots (afStack_9c0, 560 floats).
const int GameSlots = 35;

using var disc = new Disc(args[0]);
var dataEntry = disc.Files().Single(f => f.Path.Equals("/DATA/DATA.WAD", StringComparison.OrdinalIgnoreCase));
var wad = new WadArchive(disc.Read(dataEntry.Extent, dataEntry.Size));
var models = wad.Entries.Where(e => e.Path.StartsWith("/Chars/", StringComparison.OrdinalIgnoreCase)
                                 && e.Path.EndsWith(".mps", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
Console.WriteLine($"DATA.WAD: {models.Count} character models under /Chars");

double worstAll = 0; string worstWho = "-";
int skinnedMeshes = 0, unskinnedMeshes = 0, characters = 0, moved = 0, sharedOnly = 0;
foreach (var entry in models)
{
    string stem = entry.Path[..^4], leaf = stem[(stem.LastIndexOf('/') + 1)..];
    Model model;
    try { model = new Model(wad.Read(entry)); }
    catch (Exception ex) { Check(false, $"{leaf}: model would not load: {ex.Message}"); continue; }
    characters++;
    var apsEntry = wad.Entries.FirstOrDefault(e => e.Path.Equals(stem + ".aps", StringComparison.OrdinalIgnoreCase));
    Animation aps = null;
    try { if (apsEntry != null) aps = new Animation(wad.Read(apsEntry)); }
    catch (Exception ex) { Check(false, $"{leaf}: .aps would not load: {ex.Message}"); }

    string HelperName(int h) => model.Meshes.Count + h < model.Meshes.Count + model.HelperCount
        ? NameOf(model, model.SkinBoneNode(h)) : "?";
    bool hasBip = Enumerable.Range(0, model.HelperCount).Any(h => HelperName(h) == "Bip01");

    // ---- the bind-pose control, per mesh ----
    var world = model.WorldTransforms();
    var bindT = new Dictionary<(int Mesh, int Bone), Vector3>();   // solved translations, for the runtime comparison below
    var skins = new Dictionary<int, Model.Skin>();
    double worst = 0; string worstAt = "-"; int verts = 0; var bonesUsed = new SortedSet<int>();
    double weightDev = 0, slotSpread = 0;
    foreach (var mesh in model.Meshes)
    {
        Model.Skin skin;
        try { skin = model.ReadSkin(mesh); }
        catch (InvalidDataException ex) { Check(false, $"{leaf}/{mesh.Name}: {ex.Message}"); continue; }
        if (skin == null) { unskinnedMeshes++; continue; }
        skinnedMeshes++;
        skins[mesh.Index] = skin;
        var map = model.AnimVertexMap(mesh);
        var (pos, _, _) = model.Vertices(mesh);
        if (map == null || map.Length != pos.Count || map.Max() + 1 != skin.VertexCount)
        {
            Check(false, $"{leaf}/{mesh.Name}: skin has {skin.VertexCount} animated vertices but the mesh+0x98 run list maps "
                       + (map == null ? "nothing" : $"{map.Max() + 1} over {map.Length} slots"));
            continue;
        }
        // Every strip slot of one animated vertex must hold the same authored position -- the
        // run list is what says they are one vertex.
        var firstSlot = new int[skin.VertexCount]; Array.Fill(firstSlot, -1);
        for (int s = 0; s < map.Length; s++)
        {
            if (firstSlot[map[s]] < 0) firstSlot[map[s]] = s;
            else slotSpread = Math.Max(slotSpread, (pos[s] - pos[firstSlot[map[s]]]).Length());
        }
        for (int i = 0; i < skin.VertexCount; i++)
        {
            float sum = 0;
            for (int j = skin.First[i]; j < skin.First[i] + skin.Count[i]; j++) sum += skin.Weight[j];
            weightDev = Math.Max(weightDev, Math.Abs(sum - 1f));
        }
        var bones = skin.Bones.ToList();
        foreach (var b in bones) bonesUsed.Add(b);
        var rot = bones.ToDictionary(b => b, b => model.SkinBindRotation(b));
        // Solve the per-bone translations by least squares, one coordinate at a time:
        // authored_c - sum(w * (p x R_b)_c) = sum over bones of (sum of that bone's weights) * T_b,c
        int nb = bones.Count; var col = bones.Select((b, k) => (b, k)).ToDictionary(t => t.b, t => t.k);
        var ata = new double[nb, nb]; var atb = new double[3, nb];
        for (int i = 0; i < skin.VertexCount; i++)
        {
            var row = new double[nb]; var rest = pos[firstSlot[i]];
            for (int j = skin.First[i]; j < skin.First[i] + skin.Count[i]; j++)
            {
                row[col[skin.Bone[j]]] += skin.Weight[j];
                rest -= skin.Weight[j] * Vector3.TransformNormal(skin.Position[j], rot[skin.Bone[j]]);
            }
            for (int r = 0; r < nb; r++)
            {
                if (row[r] == 0) continue;
                for (int c = 0; c < nb; c++) ata[r, c] += row[r] * row[c];
                atb[0, r] += row[r] * rest.X; atb[1, r] += row[r] * rest.Y; atb[2, r] += row[r] * rest.Z;
            }
        }
        var t = new Vector3[nb];
        for (int c = 0; c < 3; c++)
        {
            var sol = Solve(ata, Enumerable.Range(0, nb).Select(r => atb[c, r]).ToArray());
            for (int r = 0; r < nb; r++) t[r][c] = (float)sol[r];
        }
        var pose = new Matrix4x4[model.HelperCount];
        Array.Fill(pose, Matrix4x4.Identity);
        foreach (var b in bones)
        {
            var m = rot[b]; m.M41 = t[col[b]].X; m.M42 = t[col[b]].Y; m.M43 = t[col[b]].Z;
            pose[b] = m; bindT[(mesh.Index, b)] = t[col[b]];
        }
        // ⭐ The same Deform the viewer draws with, over EVERY strip slot -- fitted vertices included,
        // because the fit only had 3 numbers per bone to spend and the rotations are not its to change.
        var skinned = new Vector3[skin.VertexCount];
        for (int i = 0; i < skin.VertexCount; i++) skinned[i] = skin.Deform(i, pose);
        for (int s = 0; s < map.Length; s++)
        {
            double err = (skinned[map[s]] - pos[s]).Length();
            verts++;
            if (err > worst) { worst = err; worstAt = $"{mesh.Name} slot {s} (bone{(skin.Count[map[s]] > 1 ? "s" : "")} {string.Join("+", Enumerable.Range(skin.First[map[s]], skin.Count[map[s]]).Select(j => HelperName(skin.Bone[j])))})"; }
        }
    }
    if (worst > worstAll) { worstAll = worst; worstWho = leaf; }
    Console.WriteLine($"{leaf,-12} meshes {model.Meshes.Count} ({skins.Count} skinned) helpers {model.HelperCount,2} bones {bonesUsed.Count,2} slots {verts,4}"
                    + $" | bind worst {worst,9:F3} at {worstAt}");
    Check(hasBip, $"{leaf}: the hierarchy has a Bip01 to stop the bind chain at");
    Check(skins.Count > 0, $"{leaf}: at least one mesh is skinned");
    Check(weightDev <= 1e-3, $"{leaf}: every vertex's weights sum to 1 (worst |sum-1| {weightDev:E1})");
    Check(slotSpread <= 1e-3, $"{leaf}: every run of the mesh+0x98 list is one authored vertex (worst spread {slotSpread:F3})");
    Check(worst <= Threshold, $"{leaf}: skinning the bind pose returns the authored vertices (worst {worst:F3} units, threshold {Threshold})");

    // ---- the tracks: coverage, index space, and the proof of motion ----
    if (aps == null) { Console.WriteLine($"  {leaf}: no .aps beside the model"); continue; }
    var records = aps.Records().Where(r => r.Skeletal).ToList();
    var live = records.Where(r => !r.Shared).ToList();
    Console.WriteLine($"  {records.Count} skeletal records, {live.Count} with tracks in this file" + (live.Count < records.Count ? $" ({records.Count - live.Count} shared, flag 0x80: tracks live in another character's file)" : ""));
    if (live.Count == 0) { sharedOnly++; continue; }
    int uncovered = 0, lateStart = 0, maxNode = -1, overSlots = 0;
    foreach (var rec in live)
    {
        var tracks = aps.SkeletalTracks(rec);
        var keyed = new HashSet<int>(tracks.Select(x => x.Node));
        uncovered += bonesUsed.Count(b => !keyed.Contains(b));
        lateStart += tracks.Count(x => (x.Rot.Count > 0 && x.Rot[0].Time != 0) || (x.Pos.Count > 0 && x.Pos[0].Time != 0));
        maxNode = Math.Max(maxNode, tracks.Max(x => x.Node));
        overSlots += tracks.Count(x => x.Node >= GameSlots);
    }
    Check(uncovered == 0, $"{leaf}: every skin bone is keyed by every record (a bone the record leaves alone would be stack garbage on the PS2) -- {uncovered} misses");
    Check(maxNode < model.HelperCount, $"{leaf}: track node indices ({maxNode} at most) stay inside the {model.HelperCount} helpers");
    Console.WriteLine($"  tracks starting after frame 0: {lateStart}; tracks indexing at or past the game's {GameSlots} stack slots: {overSlots}");

    // ⭐ THE PROOF. Slot 1's first record is the walk on every kid (feet in anti-phase, a pelvis
    // bob); elsewhere take the first record with tracks. A foot at two frames, in vertex units
    // and through the mesh's world matrix (model units, before the viewer's Z mirror).
    var walk = live.FirstOrDefault(r => r.Slot == 1) ?? live[0];
    var wt = aps.SkeletalTracks(walk);
    int foot = Enumerable.Range(0, model.HelperCount).FirstOrDefault(h => HelperName(h).EndsWith("L Foot", StringComparison.OrdinalIgnoreCase), -1);
    if (foot < 0 || wt.All(x => x.Node != foot)) foot = wt.OrderByDescending(x => x.Pos.Count).First().Node;
    float f0 = 0, f1 = Math.Max(1, walk.DurationFrames / 2f);
    var p0 = SkeletalPose.At(wt, f0, model.HelperCount)[foot].Translation;
    var p1 = SkeletalPose.At(wt, f1, model.HelperCount)[foot].Translation;
    var bodyMesh = skins.Keys.Select(i => model.Meshes[i]).OrderByDescending(m => skins[m.Index].VertexCount).First();
    var w = world[bodyMesh.Offset];
    Vector3 W(Vector3 v) => Vector3.Transform(v, w);
    double dist = (p1 - p0).Length();
    Console.WriteLine($"  {walk.SlotName} slot {walk.Slot} ({walk.DurationFrames} frames): {HelperName(foot)} at frame {f0} = ({p0.X:F0}, {p0.Y:F0}, {p0.Z:F0})"
                    + $" -> model ({W(p0).X:F3}, {W(p0).Y:F3}, {W(p0).Z:F3}); at frame {f1} = ({p1.X:F0}, {p1.Y:F0}, {p1.Z:F0})"
                    + $" -> model ({W(p1).X:F3}, {W(p1).Y:F3}, {W(p1).Z:F3}); moved {dist:F0} units");
    // and the skinned mesh itself, not just a bone: the body's centroid at the same two frames
    var bs = skins[bodyMesh.Index];
    Vector3 Centroid(float f) { var pose = SkeletalPose.At(wt, f, model.HelperCount); var c = Vector3.Zero; for (int i = 0; i < bs.VertexCount; i++) c += bs.Deform(i, pose); return c / bs.VertexCount; }
    var c0 = Centroid(f0); var c1 = Centroid(f1);
    Console.WriteLine($"  {bodyMesh.Name} skinned centroid: frame {f0} ({c0.X:F0}, {c0.Y:F0}, {c0.Z:F0}), frame {f1} ({c1.X:F0}, {c1.Y:F0}, {c1.Z:F0})");
    bool footKeyed = wt.First(x => x.Node == foot).Pos.Count > 1;
    Check(!footKeyed || dist > 10, $"{leaf}: the sampled {HelperName(foot)} moves between frames {f0} and {f1} ({dist:F0} units)");
    if (dist > 10) moved++;

    // Informational: how close does a record's frame 0 come to the bind pose? (No record is a
    // T-pose, so this can only corroborate the runtime layout, not prove it.)
    int pelvis = Enumerable.Range(0, model.HelperCount).FirstOrDefault(h => HelperName(h).EndsWith("Pelvis", StringComparison.OrdinalIgnoreCase), -1);
    var pelvisMesh = bindT.Keys.Where(k => k.Bone == pelvis).Select(k => k.Mesh).DefaultIfEmpty(-1).First();
    if (pelvis >= 0 && pelvisMesh >= 0)
    {
        var bind = model.SkinBindRotation(pelvis);
        double bestR = 9, bestT = 1e9; string at = "-";
        foreach (var rec in live)
        {
            var tr = aps.SkeletalTracks(rec).FirstOrDefault(x => x.Node == pelvis);
            if (tr == null) continue;
            var m = Model.BoneMatrix(SkeletalPose.RotationAt(tr, 0), SkeletalPose.PositionAt(tr, 0));
            double dr = MaxAbs(m, bind), dt = (m.Translation - bindT[(pelvisMesh, pelvis)]).Length();
            if (dr + dt / 10000 < bestR + bestT / 10000) { bestR = dr; bestT = dt; at = $"{rec.SlotName} slot {rec.Slot}"; }
        }
        Console.WriteLine($"  runtime vs bind, {HelperName(pelvis)}: nearest frame 0 is {at}: rotation elements within {bestR:F3}, translation {bestT:F0} units");
    }
}

Console.WriteLine($"\n{characters} characters, {skinnedMeshes} skinned meshes ({unskinnedMeshes} unskinned), worst bind error {worstAll:F3} units on {worstWho}, threshold {Threshold}");
Console.WriteLine($"{moved} characters showed a bone moving between two frames; {sharedOnly} carry only shared records and were not sampled");
Check(characters > 0, "at least one character was audited (otherwise every check above is vacuous)");
Console.WriteLine(bad == 0 ? "PASS" : $"FAIL: {bad}");
return bad == 0 ? 0 : 1;

static string NameOf(Model model, int node)
{
    int o = model.NodeOffset(node);
    int p = (int)BitConverter.ToUInt32(model.D, o + 0x54);
    if (p <= 0 || p >= model.D.Length) return "?";
    int e = p; while (e < model.D.Length && model.D[e] != 0) e++;
    return System.Text.Encoding.Latin1.GetString(model.D, p, e - p);
}

static double MaxAbs(Matrix4x4 a, Matrix4x4 b) => new[]
{
    a.M11 - b.M11, a.M12 - b.M12, a.M13 - b.M13, a.M21 - b.M21, a.M22 - b.M22, a.M23 - b.M23,
    a.M31 - b.M31, a.M32 - b.M32, a.M33 - b.M33,
}.Max(x => Math.Abs(x));

// Gaussian elimination with partial pivoting; a bone whose weights are all but zero gets a
// whisper of ridge so the system stays solvable and its (meaningless) translation stays finite.
static double[] Solve(double[,] a, double[] b)
{
    int n = b.Length;
    var m = new double[n, n + 1];
    for (int r = 0; r < n; r++) { for (int c = 0; c < n; c++) m[r, c] = a[r, c]; m[r, r] += 1e-9; m[r, n] = b[r]; }
    for (int c = 0; c < n; c++)
    {
        int p = c;
        for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[p, c])) p = r;
        for (int k = 0; k <= n; k++) (m[c, k], m[p, k]) = (m[p, k], m[c, k]);
        for (int r = 0; r < n; r++)
        {
            if (r == c || m[c, c] == 0) continue;
            double f = m[r, c] / m[c, c];
            for (int k = c; k <= n; k++) m[r, k] -= f * m[c, k];
        }
    }
    var x = new double[n];
    for (int r = 0; r < n; r++) x[r] = m[r, r] == 0 ? 0 : m[r, n] / m[r, r];
    return x;
}
