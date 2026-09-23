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

if (args.Length < 1) { Console.Error.WriteLine("Usage: SkinAudit /path/to/disc.bin | /path/to/DATA.WAD"); return 2; }
int bad = 0;
void Check(bool ok, string line) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + line); if (!ok) bad++; }

// ⭐ The threshold is a unit of the mesh's own vertex space, on characters 14,000-60,000 units
// across. The bind closes at a hundredth of a unit when everything is right; the wrong index
// space misses by 6,000 and more. Anything in between is a finding, not a tolerance.
const double Threshold = 1.0;
// ⚠ The sampler's matrix array on the PS2 stack holds 35 slots (afStack_9c0, 560 floats).
const int GameSlots = 35;

// A bare DATA.WAD is accepted too, for a machine that holds the archive but not the image.
WadArchive wad;
if (args[0].EndsWith(".wad", StringComparison.OrdinalIgnoreCase)) wad = new WadArchive(File.ReadAllBytes(args[0]));
else
{
    using var disc = new Disc(args[0]);
    var dataEntry = disc.Files().Single(f => f.Path.Equals("/DATA/DATA.WAD", StringComparison.OrdinalIgnoreCase));
    wad = new WadArchive(disc.Read(dataEntry.Extent, dataEntry.Size));
}
var models = wad.Entries.Where(e => e.Path.StartsWith("/Chars/", StringComparison.OrdinalIgnoreCase)
                                 && e.Path.EndsWith(".mps", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
Console.WriteLine($"DATA.WAD: {models.Count} character models under /Chars");

double worstAll = 0; string worstWho = "-";
int skinnedMeshes = 0, unskinnedMeshes = 0, characters = 0, moved = 0, sharedOnly = 0, restPoses = 0;
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
    double worst = 0, worstSingle = 0, worstBlend = 0; string worstAt = "-"; int verts = 0; var bonesUsed = new SortedSet<int>();
    double weightDev = 0, slotSpread = 0;
    var bindPose = new Dictionary<int, Matrix4x4[]>();   // per mesh: the bind matrices (rotation from the model, translation solved)
    var meshWorst = new Dictionary<string, (double Single, double Blend)>();
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
        bindPose[mesh.Index] = pose;
        meshWorst[mesh.Name] = (0.0, 0.0);
        // ⭐ The same Deform the viewer draws with, over EVERY strip slot -- fitted vertices included,
        // because the fit only had 3 numbers per bone to spend and the rotations are not its to change.
        var skinned = new Vector3[skin.VertexCount];
        for (int i = 0; i < skin.VertexCount; i++) skinned[i] = skin.Deform(i, pose);
        for (int s = 0; s < map.Length; s++)
        {
            double err = (skinned[map[s]] - pos[s]).Length();
            verts++;
            // ⭐ Single-influence vertices and blended ones are scored apart: a bone's rotation is
            // wrong if the single ones miss; only the blended ones missing says the SKIN is not
            // one rigid transform per bone -- a mesh edited after it was weighted, which is the
            // data's business and not the reader's.
            if (skin.Count[map[s]] == 1) worstSingle = Math.Max(worstSingle, err); else worstBlend = Math.Max(worstBlend, err);
            var mw = meshWorst[mesh.Name];
            meshWorst[mesh.Name] = skin.Count[map[s]] == 1 ? (Math.Max(mw.Single, err), mw.Blend) : (mw.Single, Math.Max(mw.Blend, err));
            if (err > worst) { worst = err; worstAt = $"{mesh.Name} slot {s} (bone{(skin.Count[map[s]] > 1 ? "s" : "")} {string.Join("+", Enumerable.Range(skin.First[map[s]], skin.Count[map[s]]).Select(j => HelperName(skin.Bone[j])))})"; }
        }
    }
    // ⭐⭐ WHICH OF TWO THINGS A BLENDED MISS IS. On a consistent skin every influence of a vertex,
    // taken ALONE through its own bone's transform, lands on that vertex (boy1a: 0.01 on all 265
    // influences) -- the exporter built each one from the same authored point. So fit each bone
    // from its SINGLE-influence vertices only, where the bone byte is beyond doubt, and score the
    // influences of BLENDED vertices with it, first influence and later ones apart. If the FIRST
    // influence misses too, no misreading of the later bone bytes can be the cause; and if no
    // fitted bone brings a later influence within 5 units, no re-mapping of them can be either.
    // Measured: girl2a's Head as a first influence misses by 315, guard's Head by 172, boy2a's
    // hands as later influences by 250-315, and 0 of 170 later influences re-map anywhere --
    // those skins carry per-influence offsets that cancel only in the weighted blend (guard's
    // Spine and Hand blends are consistent at 0.01 while its Head and Foot blends are not, a
    // per-VERTEX property, which is what Physique's deformable vertices would leave behind).
    // The bone bytes are used here only as group labels: nothing below rests on the helper-index
    // reading or on the bind rotation rule.
    double firstMiss = 0, laterMiss = 0, laterAny = 0; int firstN = 0, laterN = 0, laterRemap = 0, fitsMade = 0;
    foreach (var mesh in model.Meshes)
    {
        if (!skins.TryGetValue(mesh.Index, out var sk)) continue;
        var map = model.AnimVertexMap(mesh); var (pos, _, _) = model.Vertices(mesh);
        if (map == null) continue;
        var slotOf = new int[sk.VertexCount]; Array.Fill(slotOf, -1);
        for (int s = 0; s < map.Length; s++) if (slotOf[map[s]] < 0) slotOf[map[s]] = s;
        var single = new Dictionary<int, List<(Vector3 P, Vector3 V)>>();
        for (int i = 0; i < sk.VertexCount; i++)
        {
            if (sk.Count[i] != 1 || slotOf[i] < 0) continue;
            // ⚠ Not GetValueOrDefault(key, dict[key] = new()): C# evaluates that second argument
            // FIRST, so it replaced every list with an empty one on each add and nothing fitted.
            if (!single.TryGetValue(sk.Bone[sk.First[i]], out var list)) single[sk.Bone[sk.First[i]]] = list = new();
            list.Add((sk.Position[sk.First[i]], pos[slotOf[i]]));
        }
        var fits = new Dictionary<int, Matrix4x4>();
        // ⚠ A bone whose few single vertices are near-coplanar fits an affine map with a wild
        // basis (handyman: a 700,000-unit "error" from one). A rigid bone's fit has unit rows;
        // anything else is the fit's ill-conditioning, not the skin's, and is left out.
        foreach (var (b, pairs) in single)
            if (pairs.Count >= 4 && FitAffine(pairs) is { } f && RowLengths(f).All(l => l > 0.8 && l < 1.25)) fits[b] = f;
        fitsMade += fits.Count;
        if (fits.Count == 0) continue;
        for (int i = 0; i < sk.VertexCount; i++)
        {
            if (sk.Count[i] < 2 || slotOf[i] < 0) continue;
            var v = pos[slotOf[i]];
            for (int j = sk.First[i]; j < sk.First[i] + sk.Count[i]; j++)
            {
                bool first = j == sk.First[i];
                if (fits.TryGetValue(sk.Bone[j], out var f))
                {
                    double e = (Vector3.Transform(sk.Position[j], f) - v).Length();
                    if (first) { firstN++; firstMiss = Math.Max(firstMiss, e); } else { laterN++; laterMiss = Math.Max(laterMiss, e); }
                }
                if (!first)
                {
                    double best = fits.Values.Min(m => (Vector3.Transform(sk.Position[j], m) - v).Length());
                    laterAny++; if (best < 5) laterRemap++;
                }
            }
        }
    }
    // ⚠⚠ ATTRIBUTION. This line used to print only for a failing rig and ABOVE that rig's own
    // "ok/FAIL <name>" lines, so read in sequence it looked like the tail of the PREVIOUS rig's
    // block: the 314.72 / 350.88 / 407.47 that were once quoted as boy1a / girl1a / gnome are
    // boy2a's, girl2a's and guard's own numbers, and "it never runs on a failing character" was
    // the same slip inverted -- it ran ONLY on them. It now prints for EVERY rig, named, directly
    // under that rig's bind verdict, so the discrimination is in one run: on rigs that pass the
    // bind check every influence lands alone at 0.00-0.01 (boy1a, girl1a, girl3a, gnome, HallowKid,
    // JungleKid: first and later influences alike); on girl2a the FIRST influence of a blended
    // vertex misses its own bone by 315 and on guard by 172, so no misreading of the later bone
    // bytes can be the cause; and 0 of 92 (girl2a) / 0 of 78 (boy2a) later influences land within
    // 5 units of ANY fitted bone, so no re-mapping of them can be either. A free per-bone affine
    // fit over all of a bone's influences cannot close them (boy2a Spine 700). Only the weighted
    // blend lands, and on guard per VERTEX (Spine and Hand blends exact, Head and Foot blends not)
    // -- which an indexing error cannot produce and per-influence offsets baked by the exporter
    // (Physique's deformable vertices: a hypothesis, not a finding) would. Vampire shows the same
    // shape at a size that still cancels (offsets of 13, bind 0.023), so the offsets alone are not
    // the FAIL; the residual they leave is.
    string influenceLine = fitsMade == 0
        ? $"       {leaf}: per-influence check: no bone has 4+ single-influence vertices with a well-conditioned fit -- not scored"
        : $"       {leaf}: per-influence check (bones fitted from single-influence vertices: {fitsMade}): blended vertices' FIRST influence vs its own bone's fit: "
          + (firstN > 0 ? $"worst {firstMiss:F2} over {firstN}" : "none led by a fitted bone")
          + $"; later influences: " + (laterN > 0 ? $"worst {laterMiss:F2} over {laterN}" : "none on a fitted bone")
          + $"; later influences ANY fitted bone maps within 5 units: {laterRemap} of {laterAny}"
          // ⚠ The verdict describes what THIS line measures and no more. Vampire PASSES the bind
          // check at 0.023 with influences that miss alone by 13: per-influence offsets exist
          // there too, and cancel. What separates a failing rig is the bind residual the blend
          // LEAVES (girl2a 3.9, guard 5.8, boy2a 220 from offsets of 170-410), which is the
          // number above this line, not this one.
          + (firstN + laterN == 0 ? ""
             : Math.Max(firstMiss, laterMiss) <= Threshold ? " -- every scored influence lands alone"
             : $" -- influences do not land alone (offsets that cancel in the blend down to the bind residual {worst:F2})"
               + (firstN > 0 && firstMiss > Threshold ? ", first influences included: not a misread later bone byte" : ""));
    if (worst > worstAll) { worstAll = worst; worstWho = leaf; }
    Console.WriteLine($"{leaf,-12} meshes {model.Meshes.Count} ({skins.Count} skinned) helpers {model.HelperCount,2} bones {bonesUsed.Count,2} slots {verts,4}"
                    + $" | bind worst {worst,9:F3} (single-bone {worstSingle:F3}, blended {worstBlend:F3}) at {worstAt}");
    Check(hasBip, $"{leaf}: the hierarchy has a Bip01 to stop the bind chain at");
    Check(skins.Count > 0, $"{leaf}: at least one mesh is skinned");
    Check(weightDev <= 1e-3, $"{leaf}: every vertex's weights sum to 1 (worst |sum-1| {weightDev:E1})");
    // ⚠ 0.1, not zero: a strip slot's X and Y words carry the ADC and facing flags in their low
    // bit, so two slots of one vertex differ by an ulp or two -- a few thousandths at 15,000.
    Check(slotSpread <= 0.1, $"{leaf}: every run of the mesh+0x98 list is one authored vertex (worst spread {slotSpread:F3}, an ulp of flag bits)");
    Check(worst <= Threshold, $"{leaf}: skinning the bind pose returns the authored vertices (worst {worst:F3} units, threshold {Threshold})");
    // ⚠ NAMED, NOT FILTERED. A rig that misses says which mesh and whether its single-bone
    // vertices (the rotation's business) or only its blended ones (the skin's own consistency)
    // are the ones off.
    if (worst > Threshold)
        Console.WriteLine("       per mesh (single-bone / blended): " + string.Join("; ", meshWorst.Select(kv => $"{kv.Key} {kv.Value.Single:F2} / {kv.Value.Blend:F2}")));
    Console.WriteLine(influenceLine);

    // ---- the tracks: coverage, index space, and the proof of motion ----
    if (aps == null) { Console.WriteLine($"  {leaf}: no .aps beside the model"); continue; }
    var records = aps.Records().Where(r => r.Skeletal).ToList();
    var live = records.Where(r => !r.Shared).ToList();
    Console.WriteLine($"  {records.Count} skeletal records, {live.Count} with tracks in this file" + (live.Count < records.Count ? $" ({records.Count - live.Count} shared, flag 0x80: tracks live in another character's file)" : ""));
    if (live.Count == 0) { sharedOnly++; continue; }
    int lateStart = 0, maxNode = -1, overSlots = 0, withLists = 0;
    var unkeyed = new SortedSet<string>();
    foreach (var rec in live)
    {
        var tracks = aps.SkeletalTracks(rec);
        var keyed = new HashSet<int>(tracks.Select(x => x.Node));
        foreach (var b in bonesUsed) if (!keyed.Contains(b)) unkeyed.Add(HelperName(b));
        lateStart += tracks.Count(x => (x.Rot.Count > 0 && x.Rot[0].Time != 0) || (x.Pos.Count > 0 && x.Pos[0].Time != 0));
        maxNode = Math.Max(maxNode, tracks.Max(x => x.Node));
        overSlots += tracks.Count(x => x.Node >= GameSlots);
        if (rec.SmallCount > 0) withLists++;
    }
    Check(maxNode < model.HelperCount, $"{leaf}: track node indices ({maxNode} at most) stay inside the {model.HelperCount} helpers");
    // ⚠ A bone a record does not key keeps whatever its stack slot held on the PS2 and the
    // identity here. The disc does this only on prop bones (a placard, a hammer, a rock), so
    // it is reported by name rather than failed: the reader is not what is wrong with it.
    Console.WriteLine($"  tracks starting after frame 0: {lateStart}; tracks at or past the game's {GameSlots} stack slots: {overSlots};"
                    + $" records with per-mesh show/hide lists (FUN_001a8c30): {withLists} of {live.Count};"
                    + (unkeyed.Count == 0 ? " every skin bone keyed by every record" : $" bones some record leaves unkeyed: {string.Join(", ", unkeyed)}"));

    // ⭐ THE PROOF. Slot 1's first record is the walk on every kid (feet in anti-phase, a pelvis
    // bob) and its left foot is the bone to watch; a rig without a slot 1 gets whichever bone of
    // whichever record travels FURTHEST between frame 0 and the record's midpoint, so that a
    // still bone in a still record can never pass this by standing still. Vertex units, and
    // through the mesh's world matrix (model units, before the viewer's Z mirror).
    Animation.Record walk = live.FirstOrDefault(r => r.Slot == 1);
    List<Animation.SkeletalTrack> wt = walk == null ? null : aps.SkeletalTracks(walk);
    int foot = Enumerable.Range(0, model.HelperCount).FirstOrDefault(h => HelperName(h).EndsWith("L Foot", StringComparison.OrdinalIgnoreCase), -1);
    if (walk == null || foot < 0 || wt.All(x => x.Node != foot))
    {
        double far = -1;
        foreach (var rec in live)
        {
            var tr = aps.SkeletalTracks(rec);
            var a = SkeletalPose.At(tr, 0, model.HelperCount); var b = SkeletalPose.At(tr, Math.Max(1, rec.DurationFrames / 2f), model.HelperCount);
            foreach (var x in tr)
            {
                double d = (b[x.Node].Translation - a[x.Node].Translation).Length();
                if (d > far) { far = d; walk = rec; wt = tr; foot = x.Node; }
            }
        }
    }
    float f0 = 0, f1 = Math.Max(1, walk.DurationFrames / 2f);
    var p0 = SkeletalPose.At(wt, f0, model.HelperCount)[foot].Translation;
    var p1 = SkeletalPose.At(wt, f1, model.HelperCount)[foot].Translation;
    var bodyMesh = skins.Keys.Select(i => model.Meshes[i]).OrderByDescending(m => skins[m.Index].VertexCount).First();
    var w = world[bodyMesh.Offset];
    Vector3 W(Vector3 v) => Vector3.Transform(v, w);
    double dist = (p1 - p0).Length();
    string slotLabel = Animation.SlotNames.ContainsKey(walk.Slot) ? $"slot {walk.Slot} {walk.SlotName}" : $"slot {walk.Slot}";
    Console.WriteLine($"  {slotLabel} ({walk.DurationFrames} frames): {HelperName(foot)} at frame {f0} = ({p0.X:F0}, {p0.Y:F0}, {p0.Z:F0})"
                    + $" -> model ({W(p0).X:F3}, {W(p0).Y:F3}, {W(p0).Z:F3}); at frame {f1} = ({p1.X:F0}, {p1.Y:F0}, {p1.Z:F0})"
                    + $" -> model ({W(p1).X:F3}, {W(p1).Y:F3}, {W(p1).Z:F3}); moved {dist:F0} units");
    // and the skinned mesh itself, not just a bone: the body's centroid at the same two frames
    var bs = skins[bodyMesh.Index];
    Vector3 Centroid(float f) { var pose = SkeletalPose.At(wt, f, model.HelperCount); var c = Vector3.Zero; for (int i = 0; i < bs.VertexCount; i++) c += bs.Deform(i, pose); return c / bs.VertexCount; }
    var c0 = Centroid(f0); var c1 = Centroid(f1);
    Console.WriteLine($"  {bodyMesh.Name} skinned centroid: frame {f0} ({c0.X:F0}, {c0.Y:F0}, {c0.Z:F0}), frame {f1} ({c1.X:F0}, {c1.Y:F0}, {c1.Z:F0})");
    Check(dist > 10, $"{leaf}: the sampled {HelperName(foot)} moves between frames {f0} and {f1} of {slotLabel} ({dist:F0} units)");
    if (dist > 10) moved++;

    // ⭐⭐ THE CONTROL WITH NOTHING SOLVED. Skin frame 0 of every record straight through the
    // game's own matrices -- keys in, Model.BoneMatrix, Skin.Deform, nothing fitted -- and
    // compare with the authored vertices. A record whose frame 0 is the rest pose must give
    // them back, and on the disc several rigs' Idle does exactly that; the kids' records all
    // start mid-gait, so for them this can only report how far off the nearest is.
    double nearest = double.MaxValue; string nearestRec = "-";
    foreach (var rec in live)
    {
        var tr = aps.SkeletalTracks(rec);
        var pose = SkeletalPose.At(tr, 0, model.HelperCount);
        double err = 0;
        foreach (var mesh in model.Meshes)
        {
            if (!skins.TryGetValue(mesh.Index, out var sk)) continue;
            var map = model.AnimVertexMap(mesh); var (pos, _, _) = model.Vertices(mesh);
            if (map == null) continue;
            var ev = new Vector3[sk.VertexCount];
            for (int i = 0; i < ev.Length; i++) ev[i] = sk.Deform(i, pose);
            for (int s = 0; s < map.Length; s++) err = Math.Max(err, (ev[map[s]] - pos[s]).Length());
        }
        if (err < nearest) { nearest = err; nearestRec = Animation.SlotNames.ContainsKey(rec.Slot) ? $"slot {rec.Slot} {rec.SlotName}" : $"slot {rec.Slot}"; }
    }
    Console.WriteLine($"  frame 0 skinned through the game's matrices alone, nearest record to the authored vertices: {nearestRec}, worst vertex {nearest:F1} units");
    if (nearest < 50) restPoses++;

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
Console.WriteLine($"{restPoses} characters have a record whose frame 0, skinned through the game's matrices with nothing solved, lands on the authored vertices within 50 units");
Check(characters > 0, "at least one character was audited (otherwise every check above is vacuous)");
Check(restPoses > 0, "at least one rig proves the whole runtime pipeline with nothing fitted (a record's frame 0 returns its authored vertices)");
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

// Least-squares affine map p -> v (row-vector: v = p * M), as three 4-unknown solves.
static Matrix4x4? FitAffine(List<(Vector3 P, Vector3 V)> pairs)
{
    var ata = new double[4, 4]; var atb = new double[3, 4];
    foreach (var (p, v) in pairs)
    {
        double[] row = { p.X, p.Y, p.Z, 1.0 };
        for (int i = 0; i < 4; i++) { for (int j = 0; j < 4; j++) ata[i, j] += row[i] * row[j]; atb[0, i] += row[i] * v.X; atb[1, i] += row[i] * v.Y; atb[2, i] += row[i] * v.Z; }
    }
    var cols = new double[3][];
    for (int c = 0; c < 3; c++) cols[c] = Solve(ata, Enumerable.Range(0, 4).Select(i => atb[c, i]).ToArray());
    var m = Matrix4x4.Identity;
    m.M11 = (float)cols[0][0]; m.M12 = (float)cols[1][0]; m.M13 = (float)cols[2][0];
    m.M21 = (float)cols[0][1]; m.M22 = (float)cols[1][1]; m.M23 = (float)cols[2][1];
    m.M31 = (float)cols[0][2]; m.M32 = (float)cols[1][2]; m.M33 = (float)cols[2][2];
    m.M41 = (float)cols[0][3]; m.M42 = (float)cols[1][3]; m.M43 = (float)cols[2][3];
    return m;
}

static double[] RowLengths(Matrix4x4 m) => new[]
{
    Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12 + m.M13 * m.M13),
    Math.Sqrt(m.M21 * m.M21 + m.M22 * m.M22 + m.M23 * m.M23),
    Math.Sqrt(m.M31 * m.M31 + m.M32 * m.M32 + m.M33 * m.M33),
};

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
