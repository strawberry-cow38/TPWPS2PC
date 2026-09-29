using System.Reflection;
using System.IO;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>No --menu/--map/--mode: drive actual boot/language/movie/legal, then mouse through the menu.
/// Pass an explicit --map/--mode instead for the direct-park compatibility control.</summary>
public partial class MainMenuStartupSmoke : Node
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(Viewer v,string name)=>(T)(typeof(Viewer).GetField(name,Hidden)
        ??throw new MissingMemberException(name)).GetValue(v);
    int checks;
    async System.Threading.Tasks.Task Frame()
    { await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
    async System.Threading.Tasks.Task WaitUntil(Func<bool> ready, double seconds)
    {
        ulong until=Time.GetTicksMsec()+(ulong)(seconds*1000);
        while(!ready() && Time.GetTicksMsec()<until) await Frame();
    }
    async System.Threading.Tasks.Task KeyPress(Key key)
    {
        Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=true});
        await Frame();
        Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=false});
        await Frame();
    }
    async System.Threading.Tasks.Task Click(Vector2 at)
    {
        Input.ParseInputEvent(new InputEventMouseMotion {Position=at,GlobalPosition=at}); await Frame();
        Input.ParseInputEvent(new InputEventMouseButton {Position=at,GlobalPosition=at,ButtonIndex=MouseButton.Left,Pressed=true});
        await Frame();
        Input.ParseInputEvent(new InputEventMouseButton {Position=at,GlobalPosition=at,ButtonIndex=MouseButton.Left,Pressed=false});
        await Frame();
    }
    void Check(bool value,string why){if(!value)throw new InvalidOperationException(why);checks++;}
    public override async void _Ready()
    {
        Viewer viewer=null;
        try
        {
            Check(DisplayServer.GetName()!="headless","rendering display required");
            viewer=new Viewer{Name="Viewer"};AddChild(viewer);viewer.SetProcess(false);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            bool direct=OS.GetCmdlineUserArgs().Any(a=>a.StartsWith("--map=")||a.StartsWith("--mode="));
            var menu=Field<MainMenu>(viewer,"_mainMenu");
            if(direct)
            {
                Check(menu is not {Open:true},"explicit content request bypasses the menu");
                Check(Field<Model>(viewer,"_terrainModel")!=null&&Field<int>(viewer,"_loadedMap")>=0,"explicit park really loaded");
                var clock=Field<ParkClock>(viewer,"_calendar");int before=clock.Accumulator;
                viewer._Process(.04);
                Check(clock.Accumulator!=before,"visible playing park still advances (counter-control to a permanently paused gate)");
            }
            else
            {
                bool bypass=OS.GetCmdlineUserArgs().Any(a=>a=="--menu"||a.StartsWith("--menu-go="));
                var frontend=Field<FrontendScreen>(viewer,"_frontend");
                if(!bypass)
                {
                    Check(frontend is {Active:true},"bare launch runs cold boot, not a direct menu");
                    for(int i=0;i<5 && frontend.CurrentStage!=FrontendScreen.Stage.Language;i++) await Frame();
                    Check(frontend.CurrentStage==FrontendScreen.Stage.Language,"EA image advances to real language screen");
                    Check(frontend.LanguageIndex==0,"English is retail default, not OS inferred");
                    int ticks=Field<int>(viewer,"_parkTicks");
                    var clock=Field<ParkClock>(viewer,"_calendar"); int accumulator=clock.Accumulator;
                    viewer._Process(1);
                    Check(Field<int>(viewer,"_parkTicks")==ticks && clock.Accumulator==accumulator,"language blocks park/calendar");
                    await KeyPress(Key.Up); Check(frontend.LanguageIndex==0,"language clamps lower bound");
                    await KeyPress(Key.Down); Check(frontend.LanguageIndex==1,"German is second");
                    await KeyPress(Key.Down); await KeyPress(Key.Down);
                    Check(frontend.LanguageIndex==2,"French is third and upper bound clamps");
                    await KeyPress(Key.Enter);
                    Check(Field<int>(viewer,"_language")==2,"real confirm propagates language into Viewer");
                    await WaitUntil(()=>frontend.CurrentStage!=FrontendScreen.Stage.Movie,30);
                    Check(frontend.CurrentStage==FrontendScreen.Stage.Legal,"BFLOGO ends into legal screen");
                    typeof(FrontendScreen).GetField("_phaseTime",Hidden).SetValue(frontend,0.0);
                    frontend._Process(159/25.0);
                    Check(!frontend.LegalPromptVisible,"legal prompt remains hidden for first 159 frontend updates");
                    frontend._Process(1/25.0);
                    Check(frontend.LegalPromptVisible,"legal prompt appears at update 160");
                    await Frame(); await Frame();
                    var art=(System.Collections.Generic.Dictionary<string,ImageTexture>)typeof(FrontendScreen).GetField("_art",Hidden).GetValue(frontend);
                    foreach(string name in new[]{"/EAGames/EAGAMES.ssh","/Lang/LangUK.ssh","/Lang/LangGER.ssh","/Lang/LangFRE.ssh","/Mainmenu/Mainback1.ssh"})
                        Check(art.TryGetValue(name,out var tex)&&tex!=null&&tex.GetWidth()==512&&tex.GetHeight()==512,"retail art actually decoded and drawn: "+name);
                    bool movies=OS.GetCmdlineUserArgs().Any(a=>a.StartsWith("--movies-dir="));
                    Check(frontend.LastMovieResult==(movies?"finished":"missing"),"actual decoder Finished or explicit missing-file fallback");
                    Check(Field<MainMenu>(viewer,"_mainMenu") is not {Open:true},"movie completion does not skip legal");
                    await KeyPress(Key.Enter); menu=Field<MainMenu>(viewer,"_mainMenu");
                    Check(menu.Language=="fre","main menu uses chosen text table");
                    Check((string)typeof(Viewer).GetProperty("AdvisorAudioLanguage",Hidden).GetValue(viewer)=="French","advisor voice and lip language selected too");
                    // The movie runner gets a real skip and a stale Finished signal; neither
                    // may call the continuation twice or skip the next scene.
                    int completed=0;
                    frontend.PlayMovie("BFLOGO",()=>completed++);
                    if(movies)
                    {
                        Check(frontend.CurrentStage==FrontendScreen.Stage.Movie,"test has actual playing movie, not vacuous missing path");
                        await KeyPress(Key.Escape);
                        Check(frontend.LastMovieResult=="skipped","Escape skips through actual input");
                        var player=(VideoStreamPlayer)typeof(FrontendScreen).GetField("_video",Hidden).GetValue(frontend);
                        player.EmitSignal(VideoStreamPlayer.SignalName.Finished); await Frame();
                    }
                    Check(completed==1,"skip/missing and late Finished continue exactly once");
                    foreach(var pair in new[]{(0,"DINO"),(1,"FRANK"),(2,"FLOWER"),(3,"SPACEMAN")})
                        Check(FrontendScreen.WorldMovie(pair.Item1)==pair.Item2,"world clip table "+pair.Item1);
                    Check(FrontendScreen.WorldMovie(4)==null,"ALIEN not a fifth playable world");
                    Check(FrontendScreen.ResolveMovie(Path.GetTempPath(),"tpw-no-such-movie")==null,"missing file refuses lookup");
                }
                Check(menu is {Open:true,Visible:true},"cold boot reaches real Main Menu");
                Check(!Field<bool>(viewer,"_lobbyMode"),"lobby is not entered until Main Game is chosen");
                var calendar=Field<ParkClock>(viewer,"_calendar");
                int beforeTicks=Field<int>(viewer,"_parkTicks"), beforeDayUnits=calendar.Accumulator;
                viewer._Process(1.0); // the actual frame path, with the menu open
                Check(Field<int>(viewer,"_parkTicks")==beforeTicks&&calendar.Accumulator==beforeDayUnits,
                    "menu display time does not advance the hidden park or calendar");
                // Actual GUI input at drawn geometry, including two different viewport shapes.
                var original=GetWindow().Size;
                foreach(var size in new[]{new Vector2I(960,540),new Vector2I(640,720)})
                {
                    GetWindow().Size=size; await Frame(); await Frame();
                    await Click(menu.RowRect(1).GetCenter());
                    Check(menu.SelectedRow==1 && menu.Open,"Load row clickable without pretending save/load exists");
                    await Click(new Vector2(4,4));
                    Check(menu.SelectedRow==1 && !Field<bool>(viewer,"_lobbyMode"),"background click doesn't confirm selection or reach park");
                    await Click(menu.RowRect(0).GetCenter());
                    Check(menu.Open&&!Field<bool>(viewer,"_lobbyMode"),"mouse New Game opens submenu");
                    await Click(menu.RowRect(2).GetCenter()); // Exit -> top, not blank
                    Check(menu.Open&&menu.SelectedRow==0,"submenu Exit returns to top menu");
                }
                GetWindow().Size=original; await Frame(); await Frame();
                await Click(menu.RowRect(0).GetCenter());
                await Click(menu.RowRect(0).GetCenter()); // MainGame, not direct handler call
                Check(!menu.Open&&Field<bool>(viewer,"_lobbyMode"),"Main Game enters lobby and closes main menu");
                Check(Field<Node3D>(viewer,"_lobbyRoot") is {Visible:true},"actual 3D lobby is present");
                Check(Field<LobbySlots>(viewer,"_lobbySlots")!=null,"authored lobby slot table loaded");
                beforeTicks=Field<int>(viewer,"_parkTicks");beforeDayUnits=calendar.Accumulator;
                viewer._Process(1.0);
                Check(Field<int>(viewer,"_parkTicks")==beforeTicks&&calendar.Accumulator==beforeDayUnits,
                    "lobby display time also leaves the park paused");
                // Go through the actual prompt, then the world's movie, then LoadMap.
                await KeyPress(Key.Enter);
                Check(Field<bool>(viewer,"_lobbyPrompt"),"lobby confirm asks before entering");
                await KeyPress(Key.Enter);
                var activeIntro=Field<FrontendScreen>(viewer,"_frontend");
                string directory=(string)typeof(Viewer).GetMethod("MovieDirectory",Hidden).Invoke(viewer,null);
                bool worldMovie=FrontendScreen.ResolveMovie(directory,"DINO")!=null;
                if(worldMovie)
                {
                    Check(activeIntro is {CurrentStage:FrontendScreen.Stage.Movie,MovieStem:"DINO"},"selected park gates DINO, not boot/attract clip");
                    Check(Field<bool>(viewer,"_lobbyMode"),"park not entered before movie completes");
                    int n=Field<int>(viewer,"_parkTicks"), day=calendar.Accumulator;
                    viewer._Process(.25);
                    Check(n==Field<int>(viewer,"_parkTicks")&&day==calendar.Accumulator,"movie blocks actual sim tick");
                    if(OS.GetCmdlineUserArgs().Contains("--frontend-full-movies"))
                    {
                        await WaitUntil(()=>!activeIntro.Active,90);
                        Check(activeIntro.LastMovieResult=="finished","world movie naturally reaches Finished");
                    }
                    else await KeyPress(Key.Escape);
                }
                Check(!Field<bool>(viewer,"_lobbyMode") && Field<int>(viewer,"_loadedMap")>=0,"movie continuation actually enters selected park");
                // ⚠ A loaded map is not a VISIBLE one: cold boot and the menu both hide the park scene, and a second
                // hide once wiped the first one's record, so the park loaded with its root still hidden (2026-09-29).
                var parkRoot=Field<Park>(viewer,"_park")?.Root;
                var stillHidden=((System.Collections.IList)typeof(Viewer).GetField("_hiddenForLobby",Hidden).GetValue(viewer)).Count;
                Check(parkRoot!=null&&parkRoot.IsVisibleInTree()&&stillHidden==0,
                    $"entering the park puts back everything cold boot and the menu hid: the park root is {(parkRoot?.IsVisibleInTree()==true?"visible":"HIDDEN")}, {stillHidden} still listed");
                Check(activeIntro is {Active:false},"movie releases input when entering park");
                typeof(Viewer).GetMethod("EnterMainMenu",Hidden).Invoke(viewer,null);
                Check(menu.Open && !activeIntro.Active,"returning to menu does not replay cold boot");

            }
            Field<RideSounds>(viewer,"_sounds")?.Clear();
            viewer.QueueFree();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GD.Print($"MAIN MENU STARTUP SMOKE PASS checks={checks}; direct={direct}");GetTree().Quit(0);
        }
        catch(Exception e){GD.PrintErr($"MAIN MENU STARTUP SMOKE FAILED checks={checks}: {e}");GetTree().Quit(2);}
    }
}
