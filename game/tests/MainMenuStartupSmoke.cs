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
    // ⭐ Every screen waits a moment before it takes input (InputSettle). A test that clicks the frame a
    // screen opens is now testing the swallow, so each real input waits for its screen to settle first,
    // and each screen also gets one input in its first moment that must do nothing.
    async System.Threading.Tasks.Task Settled(Func<bool> ready,string what)
    { await WaitUntil(ready,10); Check(ready(),what+" settles and takes input"); }
    // A screen point that picks the lobby's CURRENT island, found by asking the lobby's own picker.
    static Vector2? CurrentIsland(Viewer v,Vector2 size)
    {
        var pick=typeof(Viewer).GetMethod("LobbyPick",Hidden); var rec=typeof(Viewer).GetMethod("LobbyRecordOfModel",Hidden);
        int want=Field<int>(v,"_lobbyRecord");
        for(float y=size.Y*.1f;y<size.Y*.9f;y+=8) for(float x=size.X*.1f;x<size.X*.9f;x+=8)
        {
            var at=new Vector2(x,y);
            if((int)rec.Invoke(v,new object[]{(int)pick.Invoke(v,new object[]{at})})==want) return at;
        }
        return null;
    }
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
                    ulong eaFrom=Time.GetTicksMsec();
                    for(int i=0;i<5;i++) await Frame();
                    // ⚠ Teeth: the old EA lasted two frames ("the EA logo appears for a split second").
                    Check(frontend.CurrentStage==FrontendScreen.Stage.EaLogo,"EA image is still up after five frames");
                    await KeyPress(Key.Enter);
                    Check(frontend.CurrentStage==FrontendScreen.Stage.EaLogo,"a key in EA's first moment does not skip it");
                    await WaitUntil(()=>frontend.CurrentStage==FrontendScreen.Stage.Language,15);
                    ulong eaShown=Time.GetTicksMsec()-eaFrom;
                    Check(frontend.CurrentStage==FrontendScreen.Stage.Language,"EA image advances to real language screen");
                    Check(eaShown>=FrontendScreen.EaSeconds*1000,$"EA held at least {FrontendScreen.EaSeconds}s ({eaShown} ms)");
                    await KeyPress(Key.Down);
                    Check(frontend.LanguageIndex==0 && frontend.CurrentStage==FrontendScreen.Stage.Language,"a key in the language screen's first moment does nothing");
                    await Settled(()=>frontend.InputReady,"language screen");
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
                    await KeyPress(Key.Enter);
                    Check(frontend.CurrentStage==FrontendScreen.Stage.Legal && menu is not {Open:true},"a key in the legal screen's first moment does not leave it");
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
                    await Settled(()=>frontend.InputReady,"legal screen");
                    await KeyPress(Key.Enter); menu=Field<MainMenu>(viewer,"_mainMenu");
                    // ⚠ Teeth, in the menu's first moment -- before the movie checks below give it time to settle.
                    Check(menu is {Open:true} && !menu.InputReady,"the main menu has only just opened");
                    await Click(menu.RowRect(0).GetCenter());
                    Check(menu.TopPage && menu.Open,"a click in the menu's first moment does not open New Game");
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
                        Check(frontend.CurrentStage==FrontendScreen.Stage.Movie && completed==0,"a key in the movie's first moment does not skip it");
                        await Settled(()=>frontend.InputReady,"playing movie");
                        var playing=(VideoStreamPlayer)typeof(FrontendScreen).GetField("_video",Hidden).GetValue(frontend);
                        Check(playing.IsPlaying() && playing.StreamPosition>0,$"the movie was actually playing before the skip took (position {playing.StreamPosition:F2}s)");
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
                await Settled(()=>menu.InputReady,"main menu");
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
                    await Settled(()=>menu.InputReady,"main menu page");
                    await Click(menu.RowRect(1).GetCenter());
                    Check(menu.SelectedRow==1 && menu.Open,"Load row clickable without pretending save/load exists");
                    await Click(new Vector2(4,4));
                    Check(menu.SelectedRow==1 && !Field<bool>(viewer,"_lobbyMode"),"background click doesn't confirm selection or reach park");
                    await Click(menu.RowRect(0).GetCenter());
                    Check(menu.Open&&!menu.TopPage&&!Field<bool>(viewer,"_lobbyMode"),"mouse New Game opens submenu");
                    // ⚠ Teeth for the double click: its second half lands on the NEW page's Main Game.
                    await Click(menu.RowRect(0).GetCenter());
                    Check(menu.Open&&!Field<bool>(viewer,"_lobbyMode"),"a click in the New Game page's first moment does not choose Main Game");
                    await Settled(()=>menu.InputReady,"New Game page");
                    await Click(menu.RowRect(2).GetCenter()); // Exit -> top, not blank
                    Check(menu.Open&&menu.SelectedRow==0,"submenu Exit returns to top menu");
                }
                GetWindow().Size=original; await Frame(); await Frame();
                await Settled(()=>menu.InputReady,"main menu page");
                await Click(menu.RowRect(0).GetCenter());
                await Settled(()=>menu.InputReady,"New Game page");
                await Click(menu.RowRect(0).GetCenter()); // MainGame, not direct handler call
                Check(!menu.Open&&Field<bool>(viewer,"_lobbyMode"),"Main Game enters lobby and closes main menu");
                Check(Field<Node3D>(viewer,"_lobbyRoot") is {Visible:true},"actual 3D lobby is present");
                Check(Field<LobbySlots>(viewer,"_lobbySlots")!=null,"authored lobby slot table loaded");
                beforeTicks=Field<int>(viewer,"_parkTicks");beforeDayUnits=calendar.Accumulator;
                viewer._Process(1.0);
                Check(Field<int>(viewer,"_parkTicks")==beforeTicks&&calendar.Accumulator==beforeDayUnits,
                    "lobby display time also leaves the park paused");
                // ⭐⭐ Master's case (main menu -> lobby, 2026-10-01): a WHOLE click arriving the moment the
                // lobby is up -- what a click made while it loaded becomes. It must neither select nor confirm.
                var island=CurrentIsland(viewer,GetViewport().GetVisibleRect().Size);
                Check(island!=null,"found a screen point on the current island (so the click below can land)");
                int recordBefore=Field<int>(viewer,"_lobbyRecord");
                // ⚠ The stall IS the bug's shape: the lobby builds synchronously, so the frame after it
                // opens is a long one, and a settle that counted real time would read that stall as time
                // on screen and take the queued click. Without this the clamp went untested (a mutation
                // to raw delta passed).
                OS.DelayMsec(1200); await Frame();
                await Click(island.Value);
                Check(!Field<bool>(viewer,"_lobbyPrompt") && Field<int>(viewer,"_lobbyRecord")==recordBefore,
                    "a click in the lobby's first moment does not confirm the island");
                await KeyPress(Key.Enter);
                Check(!Field<bool>(viewer,"_lobbyPrompt"),"Enter in the lobby's first moment does not raise the prompt");
                await Settled(()=>viewer.LobbyInputReady,"lobby");
                // The control: the same click once settled DOES confirm, so the two checks above are not vacuous.
                await Click(island.Value);
                Check(Field<bool>(viewer,"_lobbyPrompt"),"the same click once the lobby has settled raises the prompt");
                await KeyPress(Key.Enter);
                Check(Field<bool>(viewer,"_lobbyPrompt") && Field<bool>(viewer,"_lobbyMode"),"Enter in the prompt's first moment does not answer it");
                await Settled(()=>viewer.LobbyInputReady,"lobby prompt");
                await KeyPress(Key.Right); await KeyPress(Key.Enter);   // Cancel, through real input
                Check(!Field<bool>(viewer,"_lobbyPrompt") && Field<bool>(viewer,"_lobbyMode"),"prompt Cancel returns to the lobby");
                // Go through the actual prompt, then the world's movie, then LoadMap.
                await KeyPress(Key.Enter);
                Check(Field<bool>(viewer,"_lobbyPrompt"),"lobby confirm asks before entering");
                await Settled(()=>viewer.LobbyInputReady,"lobby prompt");
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
                    else { await Settled(()=>activeIntro.InputReady,"world movie"); await KeyPress(Key.Escape); }
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
