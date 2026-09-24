using TPW.PS2.Data;
using TPWPS2Viewer;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: dotnet run --project tools/TPW.PS2.TextureAnimationAudit -- <disc.bin>");
    return 1;
}
int failures = 0, models = 0, slots = 0, names = 0, multiple = 0, choices = 0, missing = 0;
int apsFiles = 0, records = 0, tracks = 0, keys = 0, sampled = 0, paired = 0;
void Check(bool ok, string message) { if (!ok) { failures++; Console.WriteLine("FAIL " + message); } }
try
{
    // Delayed first key, repeated timestamps, nonuniform spacing, reverse seeking, last-key hold.
    var synthetic = new Animation.TextureTrack(0, new[] {
        new Animation.TextureKey(2, 4), new Animation.TextureKey(7, 1),
        new Animation.TextureKey(7, 3), new Animation.TextureKey(19, 2) });
    foreach (var (time, expected) in new[] { (0f, 9), (1.999f, 9), (2f, 4), (6.999f, 4),
        (7f, 3), (19f, 2), (100f, 2), (3f, 4) })
        Check(synthetic.Sample(time, 9) == expected, $"synthetic sample {time}");

    using var lib = new AssetLibrary(args[0]);
    var wads = lib.Wads();
    Check(wads.Count == 16, $"archive census: {wads.Count} != measured 16");
    foreach (var wad in wads)
    {
        lib.OpenWad(wad);
        var modelByPath = new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);
        int wm = 0, wc = 0, wk = 0;
        foreach (var entry in lib.Wad.Entries.Where(e => e.Path.EndsWith(".mps", StringComparison.OrdinalIgnoreCase)))
        {
            var model = new Model(lib.Read(entry)); modelByPath[entry.Path] = model;
            models++; slots += model.Materials.Count;
            for (int m = 0; m < model.MaterialTextures.Count; m++)
            {
                var frames = model.MaterialTextures[m]; names += frames.Length;
                if (frames.Length <= 1) continue;
                multiple++; wm++;
                foreach (var frame in frames)
                {
                    choices++;
                    var image = lib.TextureNear(entry.Path, frame);
                    if (image == null) { missing++; Check(false, $"missing {wad}{entry.Path} [{m}] {frame}"); }
                    else Check(image.Pixels.Length == image.Width * image.Height * 4, $"pixel buffer {image.SourcePath}");
                }
            }
        }
        foreach (var entry in lib.Wad.Entries.Where(e => e.Path.EndsWith(".aps", StringComparison.OrdinalIgnoreCase)))
        {
            apsFiles++;
            var anim = new Animation(lib.Read(entry));
            var targets = lib.Rides.Where(r => r.Animation?.Path.Equals(entry.Path, StringComparison.OrdinalIgnoreCase) == true)
                .Select(r => modelByPath[r.Model.Path]).ToList();
            foreach (var record in anim.Records())
            {
                var timelines = anim.TextureTracks(record);
                if ((record.Flags & 2) == 0 || record.Skeletal)
                {
                    Check(timelines.Count == 0, $"non-texture record {wad}{entry.Path} 0x{record.Offset:x}");
                    continue;
                }
                records++; tracks += timelines.Count; wc += timelines.Count;
                Check(timelines.Count == record.SmallCount, $"track count {entry.Path} 0x{record.Offset:x}");
                Check(targets.Count > 0, $"unpaired texture animation {wad}{entry.Path}");
                foreach (var timeline in timelines)
                {
                    keys += timeline.Keys.Length; wk += timeline.Keys.Length;
                    foreach (var model in targets)
                    {
                        paired++;
                        Check(timeline.Material < model.MaterialTextures.Count && timeline.Keys.All(k =>
                            k.TextureIndex < model.MaterialTextures[timeline.Material].Length),
                            $"material/index range {wad}{entry.Path} 0x{record.Offset:x} slot {timeline.Material}");
                    }
                    // Expected value comes directly from the key, not from another sampler.
                    // Check before, at, after each distinct boundary, in reverse order as a seek test.
                    var distinctTimes = timeline.Keys.Select(k => k.Time).Distinct().Reverse();
                    foreach (var time in distinctTimes)
                    {
                        var expected = timeline.Keys.Last(k => k.Time == time).TextureIndex;
                        Check(timeline.Sample(time, -1) == expected, $"at key {wad}{entry.Path} {time}");
                        Check(timeline.Sample(time + 0.75f, -1) == expected, $"hold {wad}{entry.Path} {time}");
                        if (time > 0)
                        {
                            int previous = -1;
                            foreach (var k in timeline.Keys) if (k.Time < time) previous = k.TextureIndex;
                            Check(timeline.Sample(time - 0.25f, -1) == previous, $"before key {wad}{entry.Path} {time}");
                        }
                        sampled++;
                    }
                }
            }
        }
        Console.WriteLine($"{wad}: {wm} multi-texture materials, {wc} tracks, {wk} keys");
    }
    Check(models == 496 && slots == 6007 && names == 6351 && multiple == 74,
        $"MPS census {models}/{slots}/{names}/{multiple}, expected 496/6007/6351/74");
    Check(apsFiles == 374 && records == 185 && tracks == 287 && keys == 13665,
        $"APS census {apsFiles}/{records}/{tracks}/{keys}, expected 374/185/287/13665");
    Console.WriteLine($"MPS: {models} models, {slots} slots, {names} names, {multiple} multi-texture slots");
    Console.WriteLine($"Multi-texture choices: {choices - missing}/{choices} decoded through AssetLibrary");
    Console.WriteLine($"APS: {apsFiles} files, {records} texture records, {tracks} tracks, {keys} keys");
    Console.WriteLine($"Sampler: {sampled} distinct boundaries; {paired} track/model range checks; {failures} failures");
    // ---- Scrolling textures: which surfaces MOVE -------------------------------------------
    // ⭐⭐ THESE ARE CONTROLS, NOT DECORATION. Every "moves" line below would have PASSED before
    // the scroll list gained `dk_water`, except the dk_water3 one, which is the defect master
    // reported: the shadowed water under the jungle bridge stood still while the river it is a
    // copy of slid underneath it. And every "still" line is the other half -- a rule that calls
    // everything water is not a rule. A test that only asserts the new behaviour cannot fail for
    // the old reason.
    foreach (var (name, moves) in new[] {
        ("wr_water3.ssh", true), ("dk_water3.ssh", true), ("jri_lak2.ssh", true),
        ("jri_sur1.ssh", true), ("justwater.ssh", true),
        ("jbr_log1.ssh", false), ("m_grass.ssh", false), ("jro_mid1.ssh", false),
        ("flower_1.ssh", false), ("cn_nut.ssh", false), ("cn_stick.ssh", false) })
        Check(TextureMotion.IsWater(name) == moves, $"water rule {name} should be {(moves ? "water" : "still")}");
    foreach (var (name, swirls) in new[] {
        ("cn_nut2a.ssh", true), ("FDrink_Liquid.ssh", true),
        ("cn_nut.ssh", false), ("wr_water3.ssh", false), ("cn_umber2.ssh", false) })
        Check(TextureMotion.IsSwirl(name) == swirls, $"swirl rule {name} should be {(swirls ? "swirl" : "still")}");
    // A swirl twists and does not travel; water travels and does not twist.
    var nut = TextureMotion.ForModelTexture("cn_nut2a.ssh");
    Check(nut.Spin != 0f && nut.ScrollU == 0f && nut.ScrollV == 0f, "coconut liquid twists without travelling");
    var river = TextureMotion.ForModelTexture("dk_water3.ssh");
    Check(river.Spin == 0f && river.ScrollV != 0f, "dark water travels without twisting");
    Check(!TextureMotion.ForModelTexture("m_grass.ssh").Moves, "grass does not move");

    // ⭐ The two water textures must be PRESENT AND PAIRED on the jungle terrain, or the rule
    // above is about a name that no longer exists. This reads the disc rather than trusting it.
    {
        lib.OpenWad("/DATA/JUNGLE.WAD");
        var terrain = lib.Wad.Entries.FirstOrDefault(e => e.Path.EndsWith("terrain_1.mps", StringComparison.OrdinalIgnoreCase));
        Check(terrain != null, "JUNGLE terrain_1.mps present");
        if (terrain != null)
        {
            var tm = new Model(lib.Read(terrain));
            bool lit = tm.Materials.Any(m => m != null && m.Contains("wr_water3", StringComparison.OrdinalIgnoreCase));
            bool dark = tm.Materials.Any(m => m != null && m.Contains("dk_water3", StringComparison.OrdinalIgnoreCase));
            Check(lit && dark, $"JUNGLE terrain carries both waters (lit {lit}, dark {dark})");
            // ⭐ NAME WHAT MATCHED, not how many. A bare count passes for the wrong three
            // materials and reads exactly like the right three.
            var moving = tm.Materials.Where(m => TextureMotion.IsWater(m)).OrderBy(m => m).ToList();
            Console.WriteLine($"JUNGLE terrain_1 moving water: {string.Join(", ", moving)}");
            Check(moving.Count == 3 && moving.Any(m => m.StartsWith("dk_water3", StringComparison.OrdinalIgnoreCase))
                  && moving.Any(m => m.StartsWith("wr_water3", StringComparison.OrdinalIgnoreCase))
                  && moving.Any(m => m.StartsWith("jri_lak2", StringComparison.OrdinalIgnoreCase)),
                $"JUNGLE terrain moving water is [{string.Join(", ", moving)}], expected dk_water3/wr_water3/jri_lak2");
        }
    }
    // ---- The 0x10000 UV-animation channel ---------------------------------------------------
    // ⭐⭐ THIS IS THE REAL MOVING-TEXTURE MECHANISM and every line here would have failed before
    // it was found: the port animated nothing from this channel at all.
    {
        lib.OpenWad("/DATA/JUNGLE.WAD");
        (Model M, Animation A, string Mesh)[] Load(string stem)
        {
            var me = lib.Wad.Entries.First(e => e.Path.Contains(stem, StringComparison.OrdinalIgnoreCase)
                && e.Path.EndsWith(".mps", StringComparison.OrdinalIgnoreCase));
            var ae = lib.Wad.Entries.First(e => e.Path.Contains(stem, StringComparison.OrdinalIgnoreCase)
                && e.Path.EndsWith(".aps", StringComparison.OrdinalIgnoreCase));
            return new[] { (new Model(lib.Read(me)), new Animation(lib.Read(ae)), (string)null) };
        }

        // ⭐ THE FOUNTAIN IS THE CONTROL FOR MASTER'S REPORT. Its water is flagged in its own data
        // and matches NO texture-name rule, so a name-based port animates it never. If this
        // passes and the fountain still stands still, the fault is downstream of the data.
        foreach (var (stem, node) in new[] {
            ("Shops/Coconut/coconut", "cn_stall"), ("Features/Fountain/fountain", "wf_water"),
            ("Features/MamFount/mamfount", "mf_water1") })
        {
            var (model, aps, _) = Load(stem)[0];
            bool found = false;
            foreach (var rec in aps.Records())
            {
                if (rec.Skeletal || rec.Tracks == 0) continue;
                for (int t = 0; t < rec.TrackCount; t++)
                {
                    int off = aps.TrackAt(rec, t);
                    if ((aps.TrackFlags(off) & 0x10000) == 0) continue;
                    if (!string.Equals(model.NodeName(aps.TrackNode(off)), node, StringComparison.OrdinalIgnoreCase)) continue;
                    var uvk = aps.UvTrack(off);
                    Check(uvk != null && uvk.Count > 0, $"{stem}: {node} 0x10000 track decodes");
                    var mesh = model.Meshes.FirstOrDefault(x => string.Equals(x.Name, node, StringComparison.OrdinalIgnoreCase));
                    Check(mesh != null && mesh.UvAnimList != 0, $"{stem}: {node} has a +0x9c run list");
                    var map = mesh == null ? null : model.UvVertexMap(mesh);
                    Check(map != null, $"{stem}: {node} +0x9c run list decodes");
                    if (uvk != null && map != null)
                        Check(map.Max() + 1 == uvk.Count,
                            $"{stem}: {node} run list names {map.Max() + 1} groups, track has {uvk.Count} entries");
                    found = true;
                    break;
                }
                if (found) break;
            }
            Check(found, $"{stem}: {node} carries a 0x10000 track");
            // ⚠ SAY SO OUT LOUD. A Check that never runs prints nothing and passes, which is
            // exactly how a "verified" fountain would stand still.
            Console.WriteLine($"UV channel: {stem} node '{node}' 0x10000 track {(found ? "FOUND" : "MISSING")}");
        }

        // ⭐ The Coconut's drink is a CIRCLE about the patch centre, and the sampler must trace it.
        {
            var (model, aps, _) = Load("Shops/Coconut/coconut")[0];
            var main = aps.Records().First(r => r.Slot == 5);
            int track = aps.TrackAt(main, 0);
            var cocoKeys = aps.UvTrack(track);
            var moving = cocoKeys.Where(k => k.Length > 10).ToList();
            Check(moving.Count > 0, $"Coconut has moving UV groups ({moving.Count} of {cocoKeys.Count})");
            foreach (var k in moving)
            {
                double cu = 1.5, cv = 0.5;
                var radii = k.Select(x => Math.Sqrt((x.U - cu) * (x.U - cu) + (x.V - cv) * (x.V - cv))).ToList();
                Check(radii.All(r => Math.Abs(r - 0.5) < 0.01), $"Coconut UV group is a circle of r=0.5 about (1.5,0.5)");
                // The loop closes: first and last key are the same point.
                Check(Math.Abs(k[0].U - k[^1].U) < 1e-4 && Math.Abs(k[0].V - k[^1].V) < 1e-4,
                    "Coconut UV loop closes on its first key");
            }
            // The sampler must LERP, not step: halfway between two keys sits on the chord.
            var g = moving[0];
            var (mu, mv) = Animation.SampleUv(g, (g[0].Time + g[1].Time) / 2f);
            Check(Math.Abs(mu - (g[0].U + g[1].U) / 2f) < 1e-4 && Math.Abs(mv - (g[0].V + g[1].V) / 2f) < 1e-4,
                "SampleUv interpolates linearly between keys");
            var (su, sv) = Animation.SampleUv(g, g[0].Time);
            Check(Math.Abs(su - g[0].U) < 1e-6, "SampleUv returns the key exactly at its own time");
            Console.WriteLine($"UV channel: Coconut {moving.Count} moving groups of {cocoKeys.Count}, "
                + $"{g.Length} keys, circle r=0.5 about (1.5,0.5), linear sampler verified");
        }
    }
    Console.WriteLine("Scope: data, sampler and production resolver. Godot material binding requires game/tests/TextureAnimationAudit.tscn.");
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
return failures == 0 ? 0 : 2;
