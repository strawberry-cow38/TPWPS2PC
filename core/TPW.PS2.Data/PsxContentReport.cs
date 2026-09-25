using System.Text;

namespace TPW.PS2.Data;

/// <summary>What the optional PSX disc holds, as text for the player to read.
///
/// ⚠ A REPORT, NOT AN IMPORT. It says what the reader can see and nothing from the disc goes
/// into the game. Every line comes from <see cref="PsxDisc"/>; anything the reader does not
/// decode yet (models, textures, sounds) is named as not read rather than left out, so a short
/// list cannot be mistaken for a complete one.
///
/// ⚠ Maps are named by FOLIO entry, not by world. Which world owns which map is decided by theme
/// tables built at boot (0x8002ED50), and those have not been read.</summary>
public static class PsxContentReport
{
    static readonly (PsxAttractionType Type, string Label)[] Order =
    {
        (PsxAttractionType.RollerCoaster, "Roller coasters"), (PsxAttractionType.Ride, "Rides"),
        (PsxAttractionType.TrackRide, "Track rides"), (PsxAttractionType.TourRide, "Tour rides"),
        (PsxAttractionType.Shop, "Shops"), (PsxAttractionType.Sideshow, "Sideshows"),
        (PsxAttractionType.Feature, "Features"), (PsxAttractionType.TrackUpgrade, "Track upgrades"),
    };

    /// <summary>The report for the image at <paramref name="path"/>. Never throws: an image that
    /// cannot be read gets a report that says why.</summary>
    public static string Describe(string path)
    {
        var id = PsxDisc.Identify(path);
        if (!id.Readable) return $"PSX disc not read: {id.Message}\n{path}";
        try
        {
            using var disc = PsxDisc.Open(path);
            return Describe(disc);
        }
        catch (Exception e) { return $"PSX disc identified, but could not be read: {e.Message}\n{path}"; }
    }

    public static string Describe(PsxDisc disc)
    {
        var id = disc.Identity;
        var s = new StringBuilder();
        s.Append($"Theme Park World (PSX), {id.BootId ?? "no boot id"}");
        s.AppendLine(id.Status == PsxDisc.Status.Ok ? "" : ": an unrecognised build, read but not checked against known answers");
        s.AppendLine($"{id.Path}");
        s.AppendLine(id.Layout == "2048" ? "2048-byte sectors (.iso)" : $"raw 2352-byte sectors ({id.Layout})");
        s.AppendLine($"FOLIO.GAZ: {disc.Folio.Entries.Count} entries; English text: {disc.Text.Count} strings");

        var attractions = disc.Attractions;
        s.AppendLine();
        s.AppendLine($"{attractions.Count} attraction records");
        foreach (var (type, label) in Order)
        {
            var of = attractions.Where(a => a.Type == type).ToList();
            if (of.Count == 0) continue;
            // The same attraction has a record per world, so names repeat: list each once, with a count.
            var names = of.GroupBy(a => string.IsNullOrEmpty(a.Name) ? $"(text {a.TextId})" : a.Name)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key);
            s.AppendLine($"  {label} ({of.Count}): {string.Join(", ", names)}");
        }

        var maps = disc.Maps;
        s.AppendLine();
        s.AppendLine($"{maps.Count} park maps");
        for (int i = 0; i < maps.Count; i++)
        {
            var m = maps[i];
            int buildable = 0, path = 0;
            for (int z = 0; z < m.Height; z++)
                for (int x = 0; x < m.Width; x++)
                {
                    if (m.Buildable(x, z)) buildable++;
                    if (m.InBounds(x, z) && m.Type(x, z) == 2) path++;
                }
            s.AppendLine($"  map {i + 1} (FOLIO entry {m.Folio}): {m.Width}×{m.Height} tiles, {buildable} buildable, {path} pre-laid path");
        }

        s.AppendLine();
        s.AppendLine("Read: attraction records (type, name, footprint, entrance and exit), park maps (tiles and build flags), English text.");
        s.AppendLine("Not read yet: models, textures, sounds. Nothing from this disc is copied into the game.");
        return s.ToString();
    }
}
