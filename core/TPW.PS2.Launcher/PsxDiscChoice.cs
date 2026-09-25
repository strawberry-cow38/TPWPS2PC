using TPW.PS2.Data;

namespace TPW.PS2.Launcher;

/// <summary>The player's optional PSX disc, remembered between runs.
///
/// ⚠ OPTIONAL, and it must stay that way. With nothing chosen the launcher behaves exactly as it did
/// before this existed: no extra checks, nothing extra passed to the viewer. A chosen image that is
/// not readable as Theme Park World PSX is reported and never handed on.
///
/// The choice is kept as one line of text (the path) in the launcher's folder. The PS2 disc is found
/// by probing well-known folders instead, but a PSX rip has no such convention to probe.</summary>
public sealed class PsxDiscChoice
{
    readonly string _file;
    public PsxDisc.Check Current { get; private set; } = new(PsxDisc.Status.None, null, "No PSX disc chosen (optional).");
    /// <summary>What the reader found, for the launcher to show. Null until a readable disc is chosen.</summary>
    public string Contents { get; private set; }

    public PsxDiscChoice(string file) { _file = file; }

    /// <summary>Re-identify the remembered image, if any. Slow for a real disc (it reads FOLIO), so
    /// the window calls it off the startup path, never from its constructor.</summary>
    public PsxDisc.Check Load()
    {
        string path = File.Exists(_file) ? File.ReadAllText(_file).Trim() : null;
        return string.IsNullOrEmpty(path) ? Current : Choose(path, remember: false);
    }

    public PsxDisc.Check Choose(string path, bool remember = true)
    {
        Current = PsxDisc.Identify(path);
        Contents = null;
        if (Current.Readable)
        {
            try
            {
                using var disc = PsxDisc.Open(path);
                Contents = $"{disc.Attractions.Count} attractions, {disc.Maps.Count} park maps";
            }
            catch (Exception e)
            {
                Current = Current with { Status = PsxDisc.Status.NotTpw, Message = $"Identified, but could not be read: {e.Message}" };
            }
        }
        if (remember && Current.Readable) File.WriteAllText(_file, path);
        return Current;
    }

    public void Clear()
    {
        if (File.Exists(_file)) File.Delete(_file);
        Current = new(PsxDisc.Status.None, null, "No PSX disc chosen (optional).");
        Contents = null;
    }

    /// <summary>The path the viewer gets as <c>TPW_PSX_DISC</c>, or null to pass nothing.</summary>
    public string LaunchPath => Current.Readable ? Current.Path : null;
}
