using System.Numerics;
using TPW.PS2.Data;
using TPWPS2Viewer;

try
{
    if (args.Length != 1) throw new ArgumentException("Usage: LightingAudit <disc.bin>");
    using var disc = new Disc(args[0]);
    var light = Lighting.Read(disc);
    void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    Check(light.Ambient == new Vector3(.5f), "clear ambient identity");
    Check(light.Directional == new Vector3(.6f), "directional identity");
    Check(Vector3.Distance(light.RayDirection, new(.3f, -.7f, .6f)) < 1e-6f, "ray identity");
    Check(Vector3.Distance(light.AtWeather(1).Ambient, new(.3752f)) < 1e-6f, "weather ambient");
    Check(Vector3.Distance(light.AtWeather(1).Directional, new(.504f)) < 1e-6f, "weather directional");
    Check(light.VertexColour(new(0, 127, 0), Matrix4x4.Identity) == new Vector3(119), "up normal must give GS RGB 119");
    Check(light.VertexColour(new(0, -127, 0), Matrix4x4.Identity) == new Vector3(64), "down normal must give ambient GS RGB 64");
    Check(Lighting.Modulate(128, 119) == 119 && Lighting.Modulate(255, 64) == 127, "GS 128-is-unity modulation");
    // An independent guard mutation: a plausible .5 -> .25 ambient edit in the executable must fail.
    var entry = disc.Files().Single(f => f.Path == "/SLES_500.32");
    var changed = disc.Read(entry.Extent, entry.Size);
    changed[0x23ec64 - 0xff000] ^= 0x80;
    bool rejected = false;
    try { Lighting.ReadExecutable(changed); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "mutated executable was accepted");
    Console.WriteLine("ELF MUTATION rejected (ambient LUI changed)");
    using var lib = new AssetLibrary(args[0]);
    foreach (string world in new[] { "SPACE", "HALLOW", "JUNGLE", "FANTASY" })
    {
        lib.OpenWad($"/DATA/{world}.WAD");
        var entryModel = lib.Wad.Entries.Single(e => e.Path.Equals("/terrain/terrain_1.mps", StringComparison.OrdinalIgnoreCase));
        var model = new Model(lib.Read(entryModel));
        var transforms = model.WorldTransforms();
        (string Mesh, string Material, Vector3 Raw, float Gs, Vector3 Texel, Vector3 Output) named = world switch {
            "SPACE" => (Mesh: "CLIFFS", Material: "sfl_bas1.ssh", Raw: new Vector3(66,108,-4), Gs: 100f, Texel: new Vector3(101,26,144), Output: new Vector3(78,20,112)),
            "HALLOW" => ("road_rhs01", "grd_ctr1.ssh", new Vector3(0,127,0), 119f, new Vector3(59,58,69), new Vector3(54,53,64)),
            "JUNGLE" => ("A_ROAD", "grd_ctr1.ssh", new Vector3(0,127,0), 119f, new Vector3(59,58,69), new Vector3(54,53,64)),
            _ => ("gatebase01", "jgr_bas1.ssh", new Vector3(0,-127,0), 119f, new Vector3(8,162,4), new Vector3(7,150,3)) };
        foreach (var mesh in new[] { model.Meshes.Single(m => m.Name == named.Mesh) })
        {
            var tri = model.Triangles(mesh)[0];
            var (pos, uv, normals) = model.Vertices(mesh);
            var texture = lib.TextureNear(entryModel.Path, model.Materials[tri.Material]) ?? throw new Exception("missing named surface texture");
            int x = ((int)MathF.Floor(uv[tri.A].X * texture.Width) % texture.Width + texture.Width) % texture.Width;
            int y = ((int)MathF.Floor(uv[tri.A].Y * texture.Height) % texture.Height + texture.Height) % texture.Height;
            int at = (y * texture.Width + x) * 4;
            Vector3 raw = new(MathF.Round(normals[tri.A].X * 127), MathF.Round(normals[tri.A].Y * 127), MathF.Round(normals[tri.A].Z * 127));
            var colour = light.VertexColour(raw, transforms[mesh.Offset]);
            Check(model.Materials[tri.Material] == named.Material && raw == named.Raw && colour == new Vector3(named.Gs), world + " named surface identity");
            Check(new Vector3(texture.Pixels[at],texture.Pixels[at+1],texture.Pixels[at+2]) == named.Texel, world + " named texel identity");
            Check(new Vector3(Lighting.Modulate(texture.Pixels[at],colour.X),Lighting.Modulate(texture.Pixels[at+1],colour.Y),Lighting.Modulate(texture.Pixels[at+2],colour.Z)) == named.Output, world + " lit texel identity");
            Console.WriteLine($"SURFACE {world} {mesh.Name} vertex={tri.A} material={model.Materials[tri.Material]} normal={raw} GS={colour} {texture.SourcePath}[{x},{y}] texel=({texture.Pixels[at]},{texture.Pixels[at+1]},{texture.Pixels[at+2]}) output=({Lighting.Modulate(texture.Pixels[at],colour.X)},{Lighting.Modulate(texture.Pixels[at+1],colour.Y)},{Lighting.Modulate(texture.Pixels[at+2],colour.Z)})");
        }
    }
    Console.WriteLine("LIGHTING DATA PASS");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine("LIGHTING DATA FAIL: " + ex); return 2; }
