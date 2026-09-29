using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Player-path driver. Every gameplay action enters through ParseInputEvent.
/// Live fields and hit-box methods are READ ONLY. No paused processing, manual tick calls,
/// fixture spawns, cursor overrides, OpenGate, forced needs or menu handler invocations.</summary>
public partial class PlayerPathSession : Node
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    Viewer viewer;
    StreamWriter trace;
    string output, language;
    int sequence, observations, defects;
    static T Read<T>(object owner,string name)=>(T)(owner.GetType().GetField(name,Hidden)
        ??throw new MissingMemberException(owner.GetType().Name+"."+name)).GetValue(owner);
    void Log(string kind,object value)
    {
        string line=JsonSerializer.Serialize(new {at=Time.GetTicksMsec(),kind,value});
        trace?.WriteLine(line); trace?.Flush(); GD.Print("PLAYER PATH "+line);
    }
    void Check(bool yes,string what)
    { observations++;Log("check",new {yes,what});if(!yes)throw new InvalidOperationException(what); }
    void Finding(string name,string expected,string actual,string owner)
    {
        bool mismatch=expected!=actual;if(mismatch) defects++;
        Log("finding",new {name,expected,actual,mismatch,owner});
    }
    async Task Frame(int n=1)
    { for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
    async Task Wait(Func<bool> ready,int seconds,string why)
    {
        ulong end=Time.GetTicksMsec()+(ulong)seconds*1000;
        while(!ready()&&Time.GetTicksMsec()<end)await Frame();
        Check(ready(),why);
    }
    async Task KeyPress(Key key)
    {
        Log("key",key.ToString());
        Input.ParseInputEvent(new InputEventKey{Pressed=true,Keycode=key,PhysicalKeycode=key});await Frame(2);
        Input.ParseInputEvent(new InputEventKey{Pressed=false,Keycode=key,PhysicalKeycode=key});await Frame(2);
    }
    async Task Click(Vector2 at,MouseButton button=MouseButton.Left)
    {
        Log("click",new {x=at.X,y=at.Y,button=button.ToString()});
        Input.ParseInputEvent(new InputEventMouseMotion{Position=at,GlobalPosition=at});await Frame(2);
        Input.ParseInputEvent(new InputEventMouseButton{Position=at,GlobalPosition=at,ButtonIndex=button,Pressed=true});await Frame(2);
        Input.ParseInputEvent(new InputEventMouseButton{Position=at,GlobalPosition=at,ButtonIndex=button,Pressed=false});await Frame(2);
    }
    async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        string path=Path.Combine(output,$"{++sequence:00}-{name}.png");
        var image=GetViewport().GetTexture().GetImage();
        Check(image.SavePng(path)==Error.Ok,"screenshot saved "+name);
        Log("screenshot",new {path,width=image.GetWidth(),height=image.GetHeight(),visualReview="pending"});
    }
    LaptopShopScreen Panel=>Read<LaptopShopScreen>(viewer,"_shopPanel");
    string Text(int row)=>Read<TextDatabase>(viewer,"_text").Text(language,row);
    async Task LaptopRow(int textId)
    {
        await Frame(2);
        var rows=Read<List<string>>(Panel,"_menu");string label=Text(textId);
        int index=rows.FindIndex(x=>x==label);
        Log("menu",new {label,index,rows=rows.ToArray()});
        Check(index>=0,"visible menu contains "+label);
        Rect2 box=Panel.MenuRowScreenBox(index);
        Check(box.Size.X>0&&box.Size.Y>0,"menu row has drawn hitbox");
        await Click(box.GetCenter());
    }
    async Task Back()
    { await Click(Panel.PanelOrigin+new Vector2(411,77)*Panel.PanelScale); }
    async Task CloseLaptop()
    { await Click(Panel.PanelOrigin+new Vector2(411,121)*Panel.PanelScale); }

    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();
            output=args.FirstOrDefault(a=>a.StartsWith("--play-out="))?[11..]
                ??throw new ArgumentException("--play-out=required");
            language=args.FirstOrDefault(a=>a.StartsWith("--play-language="))?[16..]??"fre";
            Check(language is "fre" or "ger" or "eng","known language");
            if(Directory.Exists(output))throw new IOException("use a fresh artifact directory");
            Directory.CreateDirectory(output);trace=new StreamWriter(Path.Combine(output,"input-observations.jsonl"));
            Log("scope",new {language,build=args.FirstOrDefault(a=>a.StartsWith("--play-build="))?[13..]??"UNSPECIFIED",processing="normal",actions="Input.ParseInputEvent only",visualReview="pending"});
            Check(DisplayServer.GetName()!="headless","rendered session required");
            Check(!args.Any(a=>a.StartsWith("--map=")||a.StartsWith("--mode=")||a=="--menu"||a.Contains("guest-test")),"no direct launch/setup flags");
            viewer=new Viewer{Name="Viewer"};AddChild(viewer);
            var frontend=Read<FrontendScreen>(viewer,"_frontend");
            await Wait(()=>frontend?.CurrentStage==FrontendScreen.Stage.Language,20,"normal cold boot reaches language");
            await Shot("language-default");
            int down=language=="eng"?0:language=="ger"?1:2;
            for(int i=0;i<down;i++)await KeyPress(Key.Down);
            await Shot("language-selected");await KeyPress(Key.Enter);
            await Wait(()=>frontend.CurrentStage==FrontendScreen.Stage.Movie||frontend.CurrentStage==FrontendScreen.Stage.Legal,10,"language confirmed");
            if(frontend.CurrentStage==FrontendScreen.Stage.Movie)await KeyPress(Key.Enter); // player skip; retail completion separately tested
            await Wait(()=>frontend.CurrentStage==FrontendScreen.Stage.Legal,10,"legal screen");
            await Shot("legal");await KeyPress(Key.Enter);
            await Wait(()=>Read<MainMenu>(viewer,"_mainMenu") is {Open:true},10,"main menu open");
            var menu=Read<MainMenu>(viewer,"_mainMenu");await Shot("menu");
            await Click(menu.RowRect(0).GetCenter());await Click(menu.RowRect(0).GetCenter());
            await Wait(()=>Read<bool>(viewer,"_lobbyMode"),20,"main game enters lobby");
            await Shot("lobby");await KeyPress(Key.Enter);await Shot("lobby-confirm");await KeyPress(Key.Enter);
            if(frontend.CurrentStage==FrontendScreen.Stage.Movie)await KeyPress(Key.Enter);
            await Wait(()=>!Read<bool>(viewer,"_lobbyMode")&&Read<int>(viewer,"_loadedMap")>=0,30,"entered park via lobby prompt");
            await Shot("park-closed");
            var terrain=Read<AnimatedModel>(viewer,"_terrain");
            Finding("terrain visible after cold boot","True",(terrain?.Root.IsVisibleInTree()==true).ToString(),"astraclaw / tinyclaw fixing");
            Log("visibility",new {terrainVisible=terrain?.Root.Visible,terrainInTree=terrain?.Root.IsVisibleInTree(),parkVisible=Read<Park>(viewer,"_park").Root.Visible});
            // Greeting may legitimately hold the pad. Observe normal completion, never force it.
            await Wait(()=>Read<ParkAdvisor>(viewer,"_parkAdvisor")?.PadLocked!=true,60,"advisor releases normal input");
            await KeyPress(Key.Tab);await Wait(()=>Panel is {Open:true},10,"Tab opens laptop");
            await Shot("laptop-main-closed");
            Check(!Read<bool>(viewer,"_laptopParkOpen"),"fresh park starts closed");
            await LaptopRow(617); // Open Park, actual displayed translated row
            Check(Read<bool>(viewer,"_laptopParkOpen"),"Open Park click changed actual state");
            if(!Panel.Open)await KeyPress(Key.Tab);
            await Shot("laptop-main-open");
            await LaptopRow(1042); // Research
            await Wait(()=>Read<LaptopScreen>(Panel,"_spec")==LaptopScreen.Research,10,"Research page opened");
            await Shot("research");
            // Read the same pure lookup used by DrawSpec, not re-rendering our expected strings.
            var rowLookup=typeof(LaptopShopScreen).GetMethod("Row",Hidden);
            foreach(var row in LaptopScreen.Research.Rows)
                Finding("research label "+row.TextId,Text(row.TextId),(string)rowLookup.Invoke(Panel,new object[]{row.TextId}),"cow tools");
            await Back();await LaptopRow(291); // Hire
            await LaptopRow(StaffTables.PurchaseTextRow(StaffKind.Handyman));
            await Wait(()=>Read<LaptopScreen>(Panel,"_spec")==LaptopScreen.Hire,10,"cleaner candidate opened");
            await Shot("hire-candidate");
            var staff=Read<ParkStaff>(viewer,"_staff");
            var candidate=staff.Candidates.Available(StaffKind.Handyman).First();
            Finding("candidate name",Text(candidate.NameRow),Read<string>(Panel,"_title"),"cow tools");
            foreach(var row in LaptopScreen.Hire.Rows)
                Finding("candidate label "+row.TextId,Text(row.TextId),(string)rowLookup.Invoke(Panel,new object[]{row.TextId}),"cow tools");
            await CloseLaptop();Check(!Panel.Open,"Close button exits candidate page");
            await Shot("park-open-after-pages");
            Log("complete",new {observations,defects,scope="cold boot / open park / research / cleaner candidate; no building or service-use claims"});
            GD.Print($"PLAYER PATH COMPLETE observations={observations} defects={defects} language={language}");
            viewer.QueueFree();await Frame(2);GetTree().Quit(0);
        }
        catch(Exception e)
        {
            Log("blocked",e.ToString());
            if(viewer!=null)try{await Shot("blocked");}catch(Exception shot){GD.PrintErr(shot.Message);}
            GD.PrintErr("PLAYER PATH BLOCKED: "+e);GetTree().Quit(2);
        }
        finally{trace?.Dispose();}
    }
}
