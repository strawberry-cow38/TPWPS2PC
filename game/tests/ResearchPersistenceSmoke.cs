using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Shipping-client COMPONENT fixture, not player or full-world save/load. Two Viewers
/// initialize normally from the same direct-map CLI. Public research methods explicitly seed a
/// partial project; only the research section is transferred to the fresh second park. Research
/// screen navigation uses real input. Private access is read-only except normal audio teardown.
/// No field writes, manual park ticks, scheduler disabling or SaveGameRequested handler.</summary>
public partial class ResearchPersistenceSmoke : Node
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Read<T>(object owner,string name) => (T)(owner.GetType().GetField(name,Hidden)
        ?? throw new MissingFieldException(name)).GetValue(owner);
    Viewer _viewer;
    int _checks;
    ulong _deadline;
    void Check(bool ok,string why)
    { if(!ok) throw new InvalidOperationException(why); _checks++; GD.Print("[research.persistence] ok: "+why); }
    async Task Frames(int n=3)
    { for(int i=0;i<n;i++){if(Time.GetTicksMsec()>_deadline) throw new TimeoutException("120s fixture deadline"); await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);} }
    async Task StartViewer(string name)
    {
        _viewer=new Viewer{Name=name}; AddChild(_viewer);
        ulong end=Time.GetTicksMsec()+30000;
        while((Read<ParkStaff>(_viewer,"_staff")?.Research.Database==null
            || Read<LaptopShopScreen>(_viewer,"_shopPanel")==null) && Time.GetTicksMsec()<end) await Frames();
        Check(Read<ParkStaff>(_viewer,"_staff")?.Research.Database!=null,"normal startup attaches research manager/database");
        await Frames();
    }
    async Task RetireViewer()
    {
        if(_viewer==null || !IsInstanceValid(_viewer)) return;
        foreach(string name in new[]{"ResetNativeBus","StopMusic"})
            (typeof(Viewer).GetMethod(name,Hidden)??throw new MissingMethodException(name)).Invoke(_viewer,null);
        Read<RideSounds>(_viewer,"_sounds")?.Clear();
        _viewer.QueueFree();
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
        Check(!IsInstanceValid(_viewer),"normal queued Viewer teardown completes"); _viewer=null;
    }
    void Key(Key key,bool pressed)
    {
        using var ev=new InputEventKey{Keycode=key,PhysicalKeycode=key,Pressed=pressed};
        Input.ParseInputEvent(ev); Input.FlushBufferedEvents();
    }
    async Task Click(Vector2 at)
    {
        GetViewport().WarpMouse(at);
        using(var motion=new InputEventMouseMotion{Position=at,GlobalPosition=at})
        {Input.ParseInputEvent(motion);Input.FlushBufferedEvents();}
        await Frames();
        foreach(bool pressed in new[]{true,false})
        {
            using var ev=new InputEventMouseButton{Position=at,GlobalPosition=at,ButtonIndex=MouseButton.Left,
                Pressed=pressed,ButtonMask=pressed?MouseButtonMask.Left:(MouseButtonMask)0};
            Input.ParseInputEvent(ev);Input.FlushBufferedEvents();await Frames();
        }
    }
    LaptopShopScreen Panel=>Read<LaptopShopScreen>(_viewer,"_shopPanel");
    async Task Menu(string text)
    {
        var rows=Read<List<string>>(Panel,"_menu");
        int index=rows.FindIndex(r=>r.Contains(text,StringComparison.OrdinalIgnoreCase));
        Check(index>=0,"real menu contains "+text);
        var box=Panel.MenuRowScreenBox(index);
        Check(box.Size.X>0 && box.Size.Y>0,"menu row has drawn hitbox");
        await Click(box.GetCenter());
    }
    public override async void _Ready()
    {
        _deadline=Time.GetTicksMsec()+120000;int exit=2;
        try
        {
            var args=OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            Check(DisplayServer.GetName()!="headless","rendering display required");
            Check(args.Contains("--map=JUNGLE") && args.Contains("--mode=park")
                && args.Any(a=>a.StartsWith("--disc=")),"explicit ordinary JUNGLE/1 startup arguments");
            Check(!args.Contains("--all-researched") && OS.GetEnvironment("TPW_ALL_RESEARCHED")!="1",
                "no research debug override");
            await StartViewer("SourceViewer");
            var source=Read<ParkStaff>(_viewer,"_staff").Research;
            var sourceDb=Read<ResearchDatabase>(_viewer,"_researchDb");
            Check(ReferenceEquals(source.Database,sourceDb) && sourceDb.World==0 && sourceDb.Park==0,
                "source manager shares shipping park database/context");
            Check(sourceDb.Group(3,0,0)>0 && !sourceDb.Available(3,0,0),"fixture ride221 initially locked");
            // Explicit public CORE SETUP, not a player action or naturally hired researcher.
            sourceDb.File(3,0,0,37);source.SetBudget(77);
            Check(source.StartResearch(0,3,0),"explicit partial-project setup uses production StartResearch");
            source.Contribute(1); // declared quantum fixture: exercise production filing, not a manual park tick
            var project=source.Slots[0];
            int filed=sourceDb.Percent(3,0,0);uint required=project.Required,exact=project.Progress;
            uint resumed=unchecked((uint)filed*required)/100u;
            Check(project.Active && required>0 && filed<100 && exact>resumed,
                "live source has incomplete production-filed project");
            var section=ResearchPersistence.Save(source);
            Check(section.Length==98 && section[82]==77 && section[83]==1 && section[84]==3 && section[85]==0,
                "actual shipping manager produces native section and active triple");
            Check(section[0]==filed && section[1]==0,"saved first record is filed percent then captured level");
            await RetireViewer();
            await StartViewer("TargetViewer");
            var target=Read<ParkStaff>(_viewer,"_staff").Research;
            var targetDb=Read<ResearchDatabase>(_viewer,"_researchDb");
            Check(!ReferenceEquals(source,target) && !ReferenceEquals(sourceDb,targetDb),"fresh normal startup creates distinct owners");
            Check(ReferenceEquals(target.Database,targetDb) && target.ActiveCount==0 && target.Quanta==0,
                "fresh target research starts idle before section application");
            ResearchPersistence.Load(target,section);await Frames();
            var restored=target.Slots[0];
            Check(target.Budget==77 && restored.Active && restored.Category==3 && restored.Item==0
                && restored.Required==required && restored.Progress==resumed && target.Quanta==0 && target.Completions==0,
                "component load rejoins live target with budget/identity/DBA work/whole-percent progress, no quantum");
            var advisor=Read<ParkAdvisor>(_viewer,"_parkAdvisor");
            var producers=Read<AdvisorProducers>(advisor.Scheduler,"_producers");
            Check(ReferenceEquals(producers.Database,targetDb),"advisor still shares restored live database, no replacement shadow state");
            Check(targetDb.Percent(3,0,0)==filed && !targetDb.Available(3,0,0),"partial item remains locked with filed percent");
            Panel.CaptureRowDraws=true;
            Key(Godot.Key.Tab,true);await Frames();Key(Godot.Key.Tab,false);await Frames();
            await Menu("Research");await Frames();
            Check(Read<LaptopScreen>(Panel,"_spec")==LaptopScreen.Research && Panel.RowsDrawn.Count==5,
                "real navigation opens five-row shipping Research screen");
            var row=Panel.RowsDrawn[0];
            Check(row.Fraction==(int)restored.Percent && row.WidgetBox!=null && row.LabelAt!=null,
                "restored active percent reaches actual Research bar draw arguments");
            Check(!string.IsNullOrWhiteSpace(row.Text) && !row.Text.Contains("Nothing",StringComparison.OrdinalIgnoreCase),
                "restored active project name reaches actual Research value draw");
            Check(target.Budget==100,"opening Research normally sets budget100 AFTER verifying saved77");
            GD.Print("RESEARCH PERSISTENCE caveat: explicit shipping-client COMPONENT fixture, public filed/project/quantum setup and section application; not player save/load or full-world restoration. Draw receipts are not pixel review.");
            exit=0;
        }
        catch(Exception e){GD.PrintErr($"RESEARCH PERSISTENCE SMOKE FAIL checks={_checks}: {e}");}
        finally
        {
            try{await RetireViewer();}catch(Exception e){exit=2;GD.PrintErr("RESEARCH PERSISTENCE SMOKE FAIL cleanup: "+e);}
        }
        if(exit==0) GD.Print($"RESEARCH PERSISTENCE SMOKE PASS checks={_checks}; explicit client component fixture");
        GetTree().Quit(exit);
    }
}