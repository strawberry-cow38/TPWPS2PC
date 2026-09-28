using Godot;
using TPW.PS2.Data;
using System.Security.Cryptography;
using Registry = TPWPS2Viewer.SaveAssetRegistry;
using Aps = TPW.PS2.Data.Animation;

namespace TPWPS2Viewer.Tests;

public partial class SaveAssetRegistrySmoke : Node
{
    const string DiscPath = "/home/ec2-user/tpw-ps2/tpw_ps2.bin";
    int checks;
    void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
    void Reject(Action action, string why) {
        try { action(); } catch (InvalidDataException) { checks++; return; }
        throw new Exception("Accepted " + why);
    }
    static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    public override void _Ready()
    {
        string file = Path.Combine(Path.GetTempPath(), "asset-manifest-" + Guid.NewGuid() + ".json");
        try {
            var before = new FileInfo(DiscPath); long length = before.Length; var written = before.LastWriteTimeUtc;
            var policy = new List<Registry.Origin>();
            Registry.Origin Origin(string wad, string entry, Registry.AssetKind kind) {
                var o = new Registry.Origin(wad, entry, kind); policy.Add(o); return o;
            }
            Registry.Origin ride, anim, terrain, character, characterAnim, definition, program;
            byte[] rideBytes, animBytes, terrainBytes, charBytes, charAnimBytes, samBytes, programBytes;
            RideDefinition sourceDefinition, compiledDefinition;Registry.Origin compiledOrigin;
            using (var lib = new AssetLibrary(DiscPath)) {
                lib.OpenWad("/DATA/JUNGLE.WAD");
                var r = lib.Rides.First(r => r.Name.EndsWith("/monkey.mps", StringComparison.OrdinalIgnoreCase));
                ride = Origin(lib.WadName, r.Model.Path, Registry.AssetKind.Model); rideBytes = lib.Read(r.Model);
                anim = Origin(lib.WadName, r.Animation.Path, Registry.AssetKind.Animation); animBytes = lib.Read(r.Animation);
                var t = lib.TerrainModels().First(); terrain = Origin(lib.WadName, t.Path, Registry.AssetKind.Model); terrainBytes = lib.Read(t);
                var sam = lib.Wad.Entries.Single(e => e.Path.Equals(Path.ChangeExtension(r.Model.Path, ".sam"), StringComparison.OrdinalIgnoreCase));
                definition = Origin(lib.WadName, sam.Path, Registry.AssetKind.RideDefinition); samBytes = lib.Read(sam);
                var catalogue = new RideCatalogue(); catalogue.AddWad(lib.Wad, lib.WadName);
                sourceDefinition = catalogue.All.Single(d => d.Source == lib.WadName + sam.Path);
                var another=new RideCatalogue();another.AddWad(lib.Wad,lib.WadName);compiledDefinition=another.All.Single(d=>d.Source==lib.WadName+sam.Path);
                compiledOrigin=Origin(lib.WadName,sam.Path,Registry.AssetKind.CompiledDefinition);
                program = Origin(lib.WadName, r.Script.Path, Registry.AssetKind.Program); programBytes = lib.Read(r.Script);
                // Application-owned catalogue policy from the disc directory, never from saved input.
                string dir = r.Script.Path[..(r.Script.Path.LastIndexOf('/') + 1)];
                foreach (var e in lib.Wad.Entries.Where(e => !WadArchive.IsAlias(e) && e.Path.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && e.Path.EndsWith(".rse", StringComparison.OrdinalIgnoreCase)))
                    Origin(lib.WadName, e.Path, Registry.AssetKind.Program);
                lib.OpenWad("/DATA/DATA.WAD");
                new CompiledAssets(new AssetResourceDatabase(lib.Read(lib.Wad.Find("/arsdb.dba"))),TextDatabase.Load(lib.Wad,"eur")).Attach(new[]{compiledDefinition},out var report);
                Check(compiledDefinition.CompiledEntry!=null,"nonvacuous compiled identity attached");
                var c = lib.Rides.First(r => r.Name.EndsWith("/boy1a.mps", StringComparison.OrdinalIgnoreCase));
                character = Origin(lib.WadName, c.Model.Path, Registry.AssetKind.Model); charBytes = lib.Read(c.Model);
                characterAnim = Origin(lib.WadName, c.Animation.Path, Registry.AssetKind.Animation); charAnimBytes = lib.Read(c.Animation);
            }
            string rideId, animId, terrainId, charId, charAnimId, defId, programId, compiledId;
            using (var source = Registry.Capture(DiscPath, policy)) {
                var model = new Model(rideBytes);
                rideId = source.RegisterModel(ride, model); Check(source.Identify(model) == rideId, "source identity");
                animId = source.RegisterAnimation(anim, new Aps(animBytes));
                terrainId = source.RegisterModel(terrain, new Model(terrainBytes));
                charId = source.RegisterModel(character, new Model(charBytes));
                charAnimId = source.RegisterAnimation(characterAnim, new Aps(charAnimBytes));
                defId = source.RegisterDefinition(definition, sourceDefinition, samBytes);
                compiledId=source.RegisterDefinition(compiledOrigin,compiledDefinition,samBytes);Check(compiledId!=defId,"compiled and authored-only variants are separate asset IDs");
                var actualProgram = new RseProgram(programBytes);
                programId = source.RegisterProgram(program, actualProgram, programBytes);
                Reject(() => source.RegisterModel(terrain, model), "mislabelled terrain");
                Reject(() => source.Register(ride with { Archive = "/DATA/SPACE.WAD" }, rideBytes), "wrong archive");
                Reject(() => source.Register(ride with { Entry = "/../../tpw_ps2.bin" }, rideBytes), "traversal");
                Reject(() => source.ResolveProgram(programId), "unverified program catalogue");
                source.VerifyProgramCatalogue(programId);
                Check(source.ResolveProgram(programId).SiblingScope != null, "verified program scope");
                Check(ReferenceEquals(source.IdentifyProgram(actualProgram).Program, actualProgram), "capture binds actual VM program");
                using var output = File.Create(file); source.WriteManifest(output);
            }
            // No source registry or AssetLibrary remains. Only a manifest FILE feeds the fresh loader.
            Registry.Manifest saved;
            using (var input = File.OpenRead(file)) saved = Registry.ReadManifest(input);
            // Forge a SELF-CONSISTENT ID/hash pair: rejection must come from reading actual
            // disc bytes, not merely from disagreeing metadata within the saved manifest.
            var forged=saved.Assets[0];string fakeHash=new string('0',64);
            string fakeId="disc-asset/v1/"+Hash(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(
                new{Disc=saved.Disc,Origin=forged.Origin,Length=forged.Length,Sha256=fakeHash})));
            Reject(()=>{using var bad=Registry.Open(DiscPath,policy,saved with{Assets=saved.Assets.Select((e,i)=>i==0?e with{Id=fakeId,Sha256=fakeHash}:e).ToArray()});},"self-consistent forged content hash");
            var rawOrigin=ride with{Kind=Registry.AssetKind.Raw};
            using(var broad=Registry.Capture(DiscPath,policy.Append(rawOrigin))) {
                broad.Register(rawOrigin,rideBytes);var foreign=broad.Export();
                Reject(()=>{using var bad=Registry.Open(DiscPath,policy,foreign);},"existing disc asset outside exact allowed origin/kind policy");
            }
            using (var cold = Registry.Open(DiscPath, policy, saved)) {
                Check(cold.Disc.Length == length, "full disc length");
                Check(cold.SharedDefinition(compiledId).CompiledEntry.Payload.ToArray().SequenceEqual(compiledDefinition.CompiledEntry.Payload.ToArray()),"cold exact compiled DBA payload, not SAM substitution");
                Check(cold.SharedDefinition(defId).CompiledEntry==null,"SAM-only asset remains SAM-only");
                Check(cold.Describe(rideId).Sha256 == Hash(rideBytes), "AnimatedModel-compatible model SHA256");
                Check(cold.Describe(animId).Sha256 == Hash(animBytes), "AnimatedModel-compatible animation SHA256");
                Check(Hash(cold.SharedModel(terrainId).D) == Hash(terrainBytes), "cold terrain");
                Check(ReferenceEquals(cold.SharedModel(charId), cold.SharedModel(charId)), "shared character identity");
                Check(ReferenceEquals(cold.SharedAnimation(charAnimId), cold.SharedAnimation(charAnimId)), "shared APS identity");
                var chars = cold.CharacterModels(new[] { charId });
                Check(ReferenceEquals(chars[character.Entry], cold.SharedModel(charId)), "character path join");
                var rides = cold.PrivateRideModels(new Dictionary<int, string> { [11] = rideId, [12] = rideId });
                rides[11].D[0] ^= 1;
                Check(!ReferenceEquals(rides[11], rides[12]) && Hash(rides[12].D) == Hash(rideBytes), "private ride isolation");
                Check(Hash(cold.SharedModel(rideId).D) == Hash(rideBytes), "immutable source after private mutation");
                var copy = cold.CopyBytes(rideId); copy[0] ^= 1;
                Check(Hash(cold.CopyBytes(rideId)) == Hash(rideBytes), "raw defensive copies");
                Check(cold.SharedDefinition(defId).Source == sourceDefinition.Source && ReferenceEquals(cold.SharedDefinition(defId), cold.SharedDefinition(defId)), "definition identity");
                cold.VerifyProgramCatalogue(programId);
                Check(ReferenceEquals(cold.ResolveProgram(programId).Program, cold.SharedProgram(programId)), "actual program identity");
                Check(cold.ResolveChild(programId, Path.GetFileName(program.Entry)).Key == programId, "verified sibling exact lookup");
                Reject(() => cold.ResolveChild(programId, "../bad.rse"), "sibling traversal");
                Reject(() => cold.SharedAnimation(rideId), "wrong kind");
                Reject(() => cold.CopyBytes("/DATA/JUNGLE.WAD"), "path as ID");
                cold.SharedModel(charId).D[0] ^= 1;
                Reject(() => cold.SharedModel(charId), "shared byte mutation");
            }
            void Bad(Registry.Manifest m, string why) => Reject(() => { using var bad = Registry.Open(DiscPath, policy, m); }, why);
            Bad(saved with { Disc = saved.Disc with { Length = length + 1 } }, "disc length");
            Bad(saved with { Disc = saved.Disc with { Sha256 = new string('0', 64) } }, "disc fingerprint");
            Registry.Manifest Replace(Registry.AssetEntry e) => saved with { Assets = saved.Assets.Select(x => x.Id == e.Id ? e : x).ToArray() };
            var first = saved.Assets.First();
            Bad(Replace(first with { Sha256 = new string('0', 64) }), "content fingerprint");
            Bad(Replace(first with { Origin = first.Origin with { Archive = "/DATA/SPACE.WAD" } }), "manifest wrong archive");
            Bad(Replace(first with { Origin = first.Origin with { Entry = "/missing.mps" } }), "manifest wrong path");
            var after = new FileInfo(DiscPath);
            Check(after.Length == length && after.LastWriteTimeUtc == written, "disc not written");
            GD.Print($"SAVE ASSET REGISTRY PASS: {checks} checks; manifest file -> fresh disc registry"); GetTree().Quit();
        } catch (Exception e) { GD.PrintErr("SAVE ASSET REGISTRY FAIL: " + e); GetTree().Quit(2); }
        finally { if (File.Exists(file)) File.Delete(file); }
    }
}
