using TPW.PS2.Data;
using TPWPS2Viewer;

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--list"))
{
    Console.Error.WriteLine("usage: tpwps2textureaudit <disc.bin> [--list]");
    return 1;
}

const int expectedReferences = 6007;
int references = 0, tga = 0, ssh = 0, unresolved = 0, decodeThrew = 0;
int wadCount = 0, wadRead = 0, models = 0, modelRead = 0;
bool list = args.Length == 2;
try
{
    using var library = new AssetLibrary(args[0]);
    var wads = library.Wads();
    wadCount = wads.Count;
    foreach (var wad in wads)
    {
        try { library.OpenWad(wad); wadRead++; }
        catch (Exception ex) { Console.WriteLine($"WAD THREW {wad}: {ex.Message}"); continue; }
        // Do not go through a ride/UI filter, deduplicate material names, or skip aliases.
        // Every MPS and every slot in its material table belongs in the denominator.
        foreach (var entry in library.Wad.Entries.Where(e => e.Path.EndsWith(".mps", StringComparison.OrdinalIgnoreCase)))
        {
            models++;
            Model model;
            try { model = new Model(library.Read(entry)); modelRead++; }
            catch (Exception ex) { Console.WriteLine($"MODEL THREW {wad}{entry.Path}: {ex.Message}"); continue; }
            for (int slot = 0; slot < model.Materials.Count; slot++)
            {
                var material = model.Materials[slot];
                string name = $"{wad}{entry.Path} [{slot}] '{material ?? "<null>"}'";
                references++;
                try
                {
                    var texture = library.TextureNear(entry.Path, material);
                    if (texture == null)
                    {
                        unresolved++;
                        Console.WriteLine($"UNRESOLVED {name}");
                    }
                    else
                    {
                        switch (texture.Format)
                        {
                            case AssetLibrary.TextureFormat.Tga: tga++; break;
                            case AssetLibrary.TextureFormat.Ssh: ssh++; break;
                            default: throw new InvalidDataException($"Unknown texture format {texture.Format}");
                        }
                        if (list) Console.WriteLine($"RESOLVED {name} -> {texture.SourceWad}{texture.SourcePath} ({texture.Format}, {texture.Width}x{texture.Height})");
                    }
                }
                catch (Exception ex)
                {
                    decodeThrew++;
                    Console.WriteLine($"DECODE THREW {name}: {ex.Message}");
                }
            }
        }
    }
}
catch (Exception ex) { Console.Error.WriteLine($"AUDIT THREW: {ex.Message}"); return 1; }

Console.WriteLine($"archives read              {wadRead} of {wadCount}");
Console.WriteLine($"models read                {modelRead} of {models}");
Console.WriteLine($"material references        {references} of {expectedReferences}");
Console.WriteLine($"  resolved to a .tga        {tga} of {references}");
Console.WriteLine($"  resolved to a .ssh        {ssh} of {references}");
Console.WriteLine($"  UNRESOLVED               {unresolved} of {references}");
Console.WriteLine($"  decode threw             {decodeThrew} of {references}");
return references == expectedReferences && wadRead == wadCount && modelRead == models
    && unresolved == 0 && decodeThrew == 0 ? 0 : 2;
