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
    Console.WriteLine("Scope: data, sampler and production resolver. Godot material binding requires game/tests/TextureAnimationAudit.tscn.");
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
return failures == 0 ? 0 : 2;
