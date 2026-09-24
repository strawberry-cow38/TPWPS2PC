using Godot;
using TPW.PS2.Data;
using Aps=TPW.PS2.Data.Animation;
namespace TPWPS2Viewer.Tests;

/// <summary>Explicit native hide lists through the shipping RSE presenter and model binder.</summary>
public partial class AnimationHideListAudit : Node3D
{
    int checks;
    void Check(bool value,string why){if(!value)throw new Exception(why);checks++;}
    static MeshInstance3D[] Named(AnimatedModel drawn,string name)=>drawn.Root.GetChildren().OfType<MeshInstance3D>()
        .Where(m=>m.Name.ToString().StartsWith(name+"#",StringComparison.OrdinalIgnoreCase)).ToArray();
    void Hidden(AnimatedModel drawn,string name,bool hidden)
    {
        var parts=Named(drawn,name);
        Check(parts.Length>0,"fixture has actual "+name+" surfaces");
        Check(parts.All(p=>p.Visible!=hidden),name+(hidden?" hidden":" visible"));
    }
    public override void _Ready()
    {
        try
        {
            using var lib=new AssetLibrary(OS.GetEnvironment("TPW_PS2_DISC"));
            lib.OpenWad("/DATA/JUNGLE.WAD");
            byte[] Read(string ext)=>lib.Read(lib.Wad.Find("/Rides/Bouncy/bouncy"+ext));
            var model=new Model(Read(".mps"));var animation=new Aps(Read(".aps"));
            var create=animation.Records().First(r=>r.Slot==0);
            Check(create.IndexCount==19,"Bouncy create has nineteen explicit entries");
            var nodes=AnimationNodeVisibility.IndexNodes(animation,create).ToArray();
            Check(nodes.Contains(6)&&nodes.Contains(7),"egg and shell selected by explicit list");
            Check(!Enumerable.Range(0,create.TrackCount).Select(i=>animation.TrackNode(animation.TrackAt(create,i))).Intersect(nodes).Any(),"control: Bouncy indices distinct from tracked nodes");
            var drawn=new AnimatedModel(model,animation,create,_=>(null,false));AddChild(drawn.Root);
            try
            {
                Hidden(drawn,"egg",true);Hidden(drawn,"shell06",true); // before first frame sample
                Hidden(drawn,"jb_floor",false); // no track AND no hide-list entry: must remain visible
                drawn.SetFrame(create.DurationFrames/2f);
                Hidden(drawn,"egg",true);Hidden(drawn,"shell06",true);
                Check(drawn.Root.GetChildren().OfType<MeshInstance3D>().Any(p=>p.Visible),"create has other drawn geometry, not whole-model hiding");
                var noList=animation.ReadRecord(create.Offset);noList.Slot=create.Slot;noList.IndexCount=0;
                drawn.UseRecord(noList);
                Hidden(drawn,"egg",false);Hidden(drawn,"shell06",false); // old cleanup precedes any sample
                drawn.UseRecord(create);
                Hidden(drawn,"egg",true);Hidden(drawn,"shell06",true);
                // New-list protection is not guessed from class/name. A visible protected
                // shell stays visible; unprotected egg still hides under identical inputs.
                drawn.UseRecord(noList);
                int off=model.Meshes[6].Offset;uint flags=BitConverter.ToUInt32(model.D,off);
                BitConverter.GetBytes(flags|0x80000000u).CopyTo(model.D,off);
                drawn.UseRecord(create);
                Hidden(drawn,"shell06",false);Hidden(drawn,"egg",true);
                BitConverter.GetBytes(flags).CopyTo(model.D,off);
                drawn.UseRecord(noList);drawn.UseRecord(create);
                Hidden(drawn,"shell06",true);
            }
            finally{drawn.Root.Free();}
            // Positive data-width discriminator: upper halfword is not part of LHU count.
            var wide=(byte[])animation.D.Clone();BitConverter.GetBytes((ushort)0xbeef).CopyTo(wide,create.Offset+0x0e);
            Check(new Aps(wide).ReadRecord(create.Offset).IndexCount==19,"consumer count is unsigned16, not headerword32");
            var preview=new RseRidePreview(new RseProgram(Read(".rse")),animation);
            using var presenter=new RseModelPresenter(this,model,animation,_=>(null,false));
            var selected=new HashSet<int>();
            for(int ms=0;ms<=60000;ms+=100)
            {
                preview.Tick(ms);presenter.Update(preview.Host);
                if(presenter.Record==null)continue;
                selected.Add(presenter.Record.Slot);
                Hidden(presenter.Drawn,"egg",true);Hidden(presenter.Drawn,"shell06",true);
                if(selected.Contains(0) && selected.Count>=2) break; // creation/first transition, not this fixture's FIFO unload model
            }
            Check(selected.Contains(0)&&selected.Count>=2,"real RSE presenter traverses create and later records");
            GD.Print($"ANIMATION HIDE LIST PASS: {checks} checks; Bouncy constructor, transitions and real RSE presenter");
            GetTree().Quit();
        }
        catch(Exception e){GD.PrintErr("ANIMATION HIDE LIST FAIL: "+e);GetTree().Quit(2);}
    }
}
