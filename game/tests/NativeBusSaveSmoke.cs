using System.Text.Json;
using Godot;
using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;
namespace TPWPS2Viewer.Tests;

public partial class NativeBusSaveSmoke : Node
{
    int checks;
    void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
    public override void _Ready()
    {
        try
        {
            var settings=new GameSettings();var mix=new GameAudioMix();
            mix.Step(settings,true);mix.Step(settings,true);mix.Step(settings,true);
            var state=JsonSerializer.Deserialize<GameAudioMix.Snapshot>(JsonSerializer.Serialize(mix.CaptureState()));
            float before=AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(GameAudioMix.SfxBus));
            var mixCopy=new GameAudioMix();mixCopy.RestoreState(state);
            Check(before==AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(GameAudioMix.SfxBus)),"mixer stage does not touch global audio buses");
            Check(JsonSerializer.Serialize(mix.CaptureState())==JsonSerializer.Serialize(mixCopy.CaptureState()),"mixer exact mid-duck state");
            for(int i=0;i<20;i++){mix.Step(settings,i<8);mixCopy.Step(settings,i<8);
                Check(JsonSerializer.Serialize(mix.CaptureState())==JsonSerializer.Serialize(mixCopy.CaptureState()),"mixer continuation without restart");}
            // Owner disc is opened read-only by AssetLibrary/Disc. No extraction.
            using var lib = new AssetLibrary("/home/ec2-user/tpw-ps2/tpw_ps2.bin");
            foreach (string world in new[] { "JUNGLE", "HALLOW", "SPACE", "FANTASY" })
            {
                lib.OpenWad("/DATA/" + world + ".WAD");
                foreach (string terrain in new[] { "terrain_1", "terrain_2" })
                {
                    var selection = NativeParkSelection.Ordinary(lib.WadName, terrain);
                    var catalogue = NativeBusCatalogue.Read(lib.Disc, selection);
                    var stem = catalogue.Selection.BusStem;
                    var asset = lib.Rides.Single(r => r.Name.Equals($"features/{stem}/{stem}.mps", StringComparison.OrdinalIgnoreCase));
                    Run(new TPW.PS2.Data.Model(lib.Read(asset.Model)), new Aps(lib.Read(asset.Animation)), world + "/" + stem);
                }
            }
            GD.Print($"NATIVE BUS SAVE PASS: {checks} checks; eight real assets; cut/rebuild/update, no restore events");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PrintErr("NATIVE BUS SAVE FAIL: " + e); GetTree().Quit(2); }
    }

    void Run(TPW.PS2.Data.Model model, Aps animation, string id)
    {
        var events = new List<string>(); var restoredEvents = new List<string>();
        var source = new NativeBus(model, animation, _ => (null, false), 0, 0,
            (s,t) => events.Add($"s{s}@{t}"), n => events.Add($"b{n}"));
        AddChild(source.Root);
        source.Root.TopLevel = true;
        source.Root.Position = new Vector3(7, 11, -13);
        source.Root.RotateY(.31f);
        var bindings = new NativeBus.SnapshotBindings { ModelAssetId = id+".mps", AnimationAssetId = id+".aps", ServicesId = "park/bus",
            Model = new TPW.PS2.Data.Model((byte[])model.D.Clone()), Animation = new Aps((byte[])animation.D.Clone()),
            Texture = _ => (null,false), StateCommand = (s,t) => restoredEvents.Add($"s{s}@{t}"), ArrivalBatch = n => restoredEvents.Add($"b{n}") };
        NativeBus.Snapshot Capture() => source.CaptureState(id+".mps",id+".aps","park/bus");
        void Reject(NativeBus.Snapshot bad)
        {
            int children = GetChildCount(), calls = restoredEvents.Count;
            bool rejected = false;
            try { var unexpected = NativeBus.FromState(bad, bindings); unexpected.Root.Free(); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && children == GetChildCount() && calls == restoredEvents.Count, "malformed/reference rejected before visible publish or event");
        }
        void Compare(NativeBus other)
        {
            Check(JsonSerializer.Serialize(Capture()) == JsonSerializer.Serialize(other.CaptureState(id+".mps",id+".aps","park/bus")), "exact logical/retained state");
            Check(source.Root.Transform.IsEqualApprox(other.Root.Transform) && source.Root.Visible == other.Root.Visible && source.Root.TopLevel == other.Root.TopLevel, "root retained");
            var a = source.Model.Surfaces().ToArray(); var b = other.Model.Surfaces().ToArray();
            Check(a.Length > 0 && a.Length == b.Length, "real surfaces");
            for (int i=0;i<a.Length;i++)
            {
                Check(a[i].Node.Visible == b[i].Node.Visible, "native visibility");
                var x=a[i].Node.Transform; var y=b[i].Node.Transform;
                Check(x.Origin.DistanceTo(y.Origin)<.0001f && x.Basis.IsEqualApprox(y.Basis), "surface pose tolerance 1e-4 origin / Godot basis approx");
            }
            Check(source.Model.TextureChoices.SequenceEqual(other.Model.TextureChoices), "texture choices");
            Check(source.Model.CaptureHiddenNodes().SequenceEqual(other.Model.CaptureHiddenNodes()),"all retained hidden flags, including helpers");
            Check(source.Model.LastWorld.Count == other.Model.LastWorld.Count && source.Model.LastWorld.All(p => other.Model.LastWorld[p.Key] == p.Value), "exact CPU world matrices");
        }
        NativeBus clone = null;
        try
        {
            var initial = Capture();
            Reject(initial with { Version = 2 });
            Reject(initial with { ModelAssetId = "missing" });
            Reject(initial with { AnimationAssetId = "missing" });
            Reject(initial with { Controller = initial.Controller with { ServicesId = "wrong-owner" } });
            Reject(initial with { Controller = initial.Controller with { Frame = float.NaN } });
            Reject(initial with { RootTransform = new float[2] });
            Reject(initial with { HiddenNodes = new[]{-1} });
            Reject(initial with { ModelFingerprint = "wrong" });
            Reject(initial with { Controller = initial.Controller with { AnimationFingerprint = "wrong" } });
            void Cut()
            {
                string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"tpw-bus-"+Guid.NewGuid().ToString("N")+".save");
                NativeBus.Snapshot s;
                try { ParkSaveFile.Write(path,Capture());s=ParkSaveFile.Read<NativeBus.Snapshot>(path); }
                finally { System.IO.File.Delete(path);System.IO.File.Delete(path+".bak"); }
                int calls=restoredEvents.Count;
                var next=NativeBus.FromState(s,bindings);
                Check(next.Root.GetParent()==null && !ReferenceEquals(next.Model,source.Model) && !ReferenceEquals(next.Controller,source.Controller), "fresh detached graph");
                Check(calls==restoredEvents.Count, "restore has no state/batch callback replay");
                if(clone!=null) { RemoveChild(clone.Root); clone.Root.Free(); }
                clone=next; AddChild(clone.Root); Compare(clone);
            }
            Cut();
            // Pin retained flags independently of any currently visible surface: an
            // unlisted helper can otherwise escape an image/mesh-only roundtrip check.
            var originalHidden=source.Model.CaptureHiddenNodes();
            int probe=Enumerable.Range(0,model.Meshes.Count+model.HelperCount).First(n=>!originalHidden.Contains(n));
            source.Model.RestoreHiddenNodes(originalHidden.Append(probe).ToArray());Cut();
            source.Model.RestoreHiddenNodes(originalHidden);Cut();
            var variants=new HashSet<int>();
            int traffic=0;
            for(int i=0;i<900;i++)
            {
                uint clock=(uint)(i*40);
                int mark=events.Count, otherMark=restoredEvents.Count;
                int next=source.Update(clock,0x4000,traffic,true,true,0);
                int other=clone.Update(clock,0x4000,traffic,true,true,0);
                Check(next==other && events.Skip(mark).SequenceEqual(restoredEvents.Skip(otherMark)), "Update return/events continuation");
                traffic=next;
                variants.Add(source.Controller.CaptureState("park/bus").RecordVariant);
                // Bind itself deliberately retains the old pose; snapshots reject that cut.
                if(source.Controller.Active && source.Controller.Frame==0 && i<3)
                {
                    bool rejected=false; try { Capture(); } catch(InvalidDataException) { rejected=true; }
                    Check(rejected,"unsampled bind cut explicitly rejected");
                }
                source.Present(clock,.5f); clone.Present(clock,.5f);
                Compare(clone);
                if(i%37==0 || source.Controller.EndHold && i%13==0) Cut();
            }
            Check(events.Count(e=>e.StartsWith("b"))>0,"nonvacuous arrival occurred after restore");
            Check(new[]{0,1,2}.All(variants.Contains),"all three real bus records exercised");
            source.Root.Visible=false; Cut();
            GD.Print($"NATIVE BUS SAVE {id}: PASS");
        }
        finally { clone?.Root.Free(); source.Root.Free(); }
    }
}
