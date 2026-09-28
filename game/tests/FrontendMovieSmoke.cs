using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace TPWPS2Viewer.Tests;

/// <summary>Lifecycle controls using a supplied synthetic Theora, never a copied retail asset.
/// --frontend-fixture= identifies the test file; normal startup uses --movies-dir instead.</summary>
public partial class FrontendMovieSmoke : Node
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    int checks;
    void Check(bool yes,string why) { if(!yes) throw new InvalidOperationException(why); checks++; }
    async Task Frame() => await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    public override async void _Ready()
    {
        string dir=Path.Combine(Path.GetTempPath(),"tpw-frontend-"+Guid.NewGuid().ToString("N"));
        FrontendScreen screen=null;
        try
        {
            Check(DisplayServer.GetName()!="headless","rendering display required");
            var arg=System.Linq.Enumerable.FirstOrDefault(OS.GetCmdlineUserArgs(),x=>x.StartsWith("--frontend-fixture="));
            if(arg==null) throw new InvalidOperationException("--frontend-fixture= must name a synthetic OGV");
            Directory.CreateDirectory(dir);
            File.Copy(arg["--frontend-fixture=".Length..],Path.Combine(dir,"mixed.OGV"));
            File.WriteAllText(Path.Combine(dir,"bad.ogv"),"not a movie");
            var ui=new Control(); AddChild(ui); ui.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            screen=new FrontendScreen(); screen.Configure(null,null,null,dir); ui.AddChild(screen);
            int callbacks=0;
            screen.PlayMovie("absent",()=>callbacks++);
            Check(callbacks==1&&!screen.Active&&screen.LastMovieResult=="missing","missing file continues, with explicit result");
            screen.PlayMovie("bad",()=>callbacks++);
            Check(callbacks==2&&!screen.Active&&screen.LastMovieResult=="unreadable","invalid Ogg does not hang or masquerade as finished");
            screen.PlayMovie("MIXED",()=>callbacks++);
            Check(screen.Active,"case-insensitive lookup really starts player");
            var stale=(Action)typeof(FrontendScreen).GetField("_finishedHandler",Private).GetValue(screen);
            for(int i=0;i<5;i++) await Frame();
            var player=(VideoStreamPlayer)typeof(FrontendScreen).GetField("_video",Private).GetValue(screen);
            Check(player.IsPlaying(),"decoder actually runs");
            Check(Math.Abs(player.Size.X/player.Size.Y-4f/3)<.0001,"converted square-pixel video displayed 4:3 without second SAR correction");
            screen.Confirm();
            Check(callbacks==3&&screen.LastMovieResult=="skipped","skip completes once");
            screen.PlayMovie("MIXED",()=>callbacks++);
            stale();
            Check(callbacks==3&&screen.Active,"old Finished callback cannot finish replacement movie");
            for(int i=0;i<300 && screen.Active;i++) await Frame();
            Check(callbacks==4&&!screen.Active&&screen.LastMovieResult=="finished","replacement reaches genuine Finished");
            stale(); screen.Confirm();
            Check(callbacks==4,"late callbacks and confirm while idle are inert");
            screen.PlayMovie("MIXED",()=>{callbacks++;screen.PlayMovie("MIXED",()=>callbacks++);});
            screen.Confirm();
            Check(callbacks==5&&screen.Active,"continuation can start next movie without CancelMovie destroying it");
            screen.Confirm();
            Check(callbacks==6&&!screen.Active,"chained clip completes exactly once");
            screen.PlayMovie("MIXED",()=>callbacks++);
            ui.QueueFree(); await Frame(); await Frame();
            Check(callbacks==6,"disposing movie owner does not enter a dead scene");
            GD.Print($"FRONTEND MOVIE SMOKE PASS checks={checks}; synthetic=True"); GetTree().Quit(0);
        }
        catch(Exception e) { GD.PrintErr($"FRONTEND MOVIE SMOKE FAILED checks={checks}: {e}"); GetTree().Quit(2); }
        finally { if(Directory.Exists(dir)) Directory.Delete(dir,true); }
    }
}
