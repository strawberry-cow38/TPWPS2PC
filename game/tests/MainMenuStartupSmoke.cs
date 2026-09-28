using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>No --menu/--map/--mode: test the real default, then confirm New Game -> Main Game.
/// Pass an explicit --map/--mode instead for the direct-park compatibility control.</summary>
public partial class MainMenuStartupSmoke : Node
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(Viewer v,string name)=>(T)(typeof(Viewer).GetField(name,Hidden)
        ??throw new MissingMemberException(name)).GetValue(v);
    int checks;
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
            }
            else
            {
                Check(menu is {Open:true,Visible:true},"bare launch opens the real Main Menu without --menu");
                Check(!Field<bool>(viewer,"_lobbyMode"),"lobby is not entered until Main Game is chosen");
                menu.Confirm(); // New Game -> submenu; don't call OnMenuChosen directly
                Check(menu.Open&&!Field<bool>(viewer,"_lobbyMode"),"New Game opens its submenu rather than jumping into a park");
                menu.Confirm(); // Main Game -> Chosen -> OnMenuChosen -> EnterLobby
                Check(!menu.Open&&Field<bool>(viewer,"_lobbyMode"),"Main Game enters lobby and closes main menu");
                Check(Field<Node3D>(viewer,"_lobbyRoot") is {Visible:true},"actual 3D lobby is present");
                Check(Field<LobbySlots>(viewer,"_lobbySlots")!=null,"authored lobby slot table loaded");
            }
            Field<RideSounds>(viewer,"_sounds")?.Clear();
            viewer.QueueFree();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            GD.Print($"MAIN MENU STARTUP SMOKE PASS checks={checks}; direct={direct}");GetTree().Quit(0);
        }
        catch(Exception e){GD.PrintErr($"MAIN MENU STARTUP SMOKE FAILED checks={checks}: {e}");GetTree().Quit(2);}
    }
}
