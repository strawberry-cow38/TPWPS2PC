using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Actual catalogue population/attachment/DefinitionFor consumers, not just a standalone
/// lookup that can succeed while the viewer still uses authored values. GUI startup is excluded.</summary>
public partial class CompiledShopViewerAudit : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    int _checks, _bad;
    static FieldInfo Member(string name) => typeof(Viewer).GetField(name, Hidden) ?? throw new MissingMemberException("Viewer." + name);
    static T Field<T>(Viewer v, string name) => (T)Member(name).GetValue(v);
    static void Set(Viewer v, string name, object value) => Member(name).SetValue(v, value);
    static object Call(Viewer v, string name, params object[] args) =>
        (typeof(Viewer).GetMethod(name, Hidden) ?? throw new MissingMemberException("Viewer." + name)).Invoke(v, args);
    void Check(bool ok, string label)
    {
        _checks++; if (!ok) _bad++;
        GD.Print($"COMPILED SHOP VIEWER {(ok ? "ok" : "FAIL")}: [{_checks}] {label}");
    }

    public override async void _Ready()
    {
        Viewer viewer = null; Node3D stage = null; AssetLibrary library = null;
        try
        {
            library = new AssetLibrary(OS.GetEnvironment("TPW_PS2_DISC"));
            var dataFile = library.WadFiles().Single(f => f.Path.EndsWith("/DATA.WAD", StringComparison.OrdinalIgnoreCase));
            var data = new WadArchive(library.ReadDisc(dataFile));
            viewer = new Viewer(); Set(viewer, "_lib", library); Set(viewer, "_text", TextDatabase.Load(data, "eur"));
            Call(viewer, "BuildUi"); stage = new Node3D(); AddChild(stage);
            foreach (Node child in viewer.GetChildren()) { viewer.RemoveChild(child); stage.AddChild(child); }
            foreach (var c in new (string World, string Stem, int Authored, int Price, int Cost)[]
                     { ("JUNGLE","Balloon/Balloon",15,45,30), ("HALLOW","vampshop/vampshop",15,45,30),
                       ("FANTASY","fatfairy/fatfairy",10,45,30), ("SPACE","droid/droid",15,50,35) })
            {
                library.OpenWad("/DATA/" + c.World + ".WAD");
                Call(viewer, "IndexRides");
                var catalogue = Field<RideCatalogue>(viewer, "_cat");
                var shops = catalogue.All.Where(d => d.ShopType != null).ToArray();
                Check(shops.Length > 0 && shops.All(d => d.Compiled != null),
                      $"{c.World} IndexRides attaches compiled settings to every populated shop");
                string stem = "/Shops/" + c.Stem;
                var model = library.Rides.Single(r => r.Model.Path.EndsWith(stem + ".mps", StringComparison.OrdinalIgnoreCase)).Model;
                var definition = (RideDefinition)Call(viewer, "DefinitionFor", model);
                Check(definition != null && definition.Source.EndsWith(stem + ".sam", StringComparison.OrdinalIgnoreCase),
                      $"{c.World} DefinitionFor returns the specifically named shop");
                Check(definition?.Int("UsageInfo.HappinessEffect") == c.Authored,
                      $"{c.World} control retains authored happiness {c.Authored}");
                Check(definition?.Compiled != null && definition.HappinessEffect == 10
                      && definition.PricePerUse == c.Price && definition.CostOfGoods == c.Cost,
                      $"{c.World} live definition consumes compiled happiness10 and price/cost {c.Price}/{c.Cost}");
            }
            // Re-index after a world change: no old catalogue reference may be reused.
            var before = Field<RideCatalogue>(viewer, "_cat");
            library.OpenWad("/DATA/JUNGLE.WAD"); Call(viewer, "IndexRides");
            var after = Field<RideCatalogue>(viewer, "_cat");
            Check(!ReferenceEquals(before, after) && after.All.Where(d => d.ShopType != null).All(d => d.Compiled != null),
                  "re-index replaces the catalogue and attaches the new world's compiled settings");
            var balloon = library.Rides.Single(r => r.Model.Path.EndsWith("/Shops/Balloon/Balloon.mps", StringComparison.OrdinalIgnoreCase));
            var live = (RideDefinition)Call(viewer, "DefinitionFor", balloon.Model);
            Check(live?.HappinessEffect == 10 && live.Source.Contains("JUNGLE", StringComparison.OrdinalIgnoreCase),
                  "re-index does not retain SPACE or fall back to Balloon authored15");
            stage.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print(_bad == 0 ? $"COMPILED SHOP VIEWER PASS: {_checks} checks, 0 failures" : $"COMPILED SHOP VIEWER FAIL: {_bad}");
            GetTree().Quit(_bad == 0 ? 0 : 2);
        }
        catch (Exception ex) { GD.PrintErr("COMPILED SHOP VIEWER ERROR: " + ex); GetTree().Quit(2); }
        finally
        {
            if (GodotObject.IsInstanceValid(viewer)) viewer.Free();
            if (GodotObject.IsInstanceValid(stage) && !stage.IsQueuedForDeletion()) stage.QueueFree();
            library?.Dispose();
        }
    }
}
