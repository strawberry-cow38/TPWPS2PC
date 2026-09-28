using System;
using System.IO;
using System.Linq;
using Godot;

namespace TPWPS2Viewer;

public partial class Viewer
{
    FrontendScreen _frontend;
    bool _coldBoot;
    int _language;
    string TextLanguage => FrontendScreen.TextLanguages[_language];
    string SpeechLanguage => FrontendScreen.AudioLanguages[_language];

    // Explicit override wins, even if missing. Do not silently play an unrelated directory.
    // Last fallback is the owner's existing converted set on the Windows playtest machine;
    // no extraction, environment modification or network access is performed here.
    string MovieDirectory()
    {
        var argument=OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs())
            .LastOrDefault(x=>x.StartsWith("--movies-dir=",StringComparison.Ordinal));
        if(argument!=null) return argument["--movies-dir=".Length..];
        var configured=OS.GetEnvironment("TPW_PS2_MOVIES");
        if(!string.IsNullOrWhiteSpace(configured)) return configured;
        var adjacent=Path.Combine(Path.GetDirectoryName(_discPath)??".","movies");
        if(Directory.Exists(adjacent)) return adjacent;
        const string existingWindowsMovies=@"C:\claude-workspace\movies-ogv";
        if(OperatingSystem.IsWindows() && Directory.Exists(existingWindowsMovies)) return existingWindowsMovies;
        return adjacent;
    }
    FrontendScreen Frontend()
    {
        if(_frontend!=null) return _frontend;
        LoadHudFont();
        _frontend=new FrontendScreen {Name="Frontend"};
        _frontend.Configure(_lib,_hudFont,_text,MovieDirectory());
        _frontend.LanguageChosen+=SelectLanguage;
        _uiRoot.AddChild(_frontend);
        return _frontend;
    }
    void BeginFrontendBoot()
    {
        HideParkScene();
        Frontend().Boot(EnterMainMenu);
        GD.Print("[frontend] cold boot: EA image -> language -> BFLOGO -> legal -> menu");
    }
    void SelectLanguage(int selection)
    {
        if(selection<0 || selection>=3) throw new ArgumentOutOfRangeException(nameof(selection));
        _language=selection;
        _advisorVoice?.Stop();
        _advisorSpeech=null; _advisorSpeechTried=false;
        _advisorBindings.Clear(); _advisorStreams.Clear();
        if(_mainMenu!=null) _mainMenu.Language=TextLanguage;
        GD.Print($"[frontend] language={TextLanguage}, retail={FrontendScreen.RetailLanguages[selection]}, speech/lips={SpeechLanguage}");
    }
    void PlayParkIntro(int world,Action enter)
    {
        var movie=FrontendScreen.WorldMovie(world);
        if(movie==null) { enter(); return; }
        var frontend=Frontend();
        _uiRoot.MoveChild(frontend,_uiRoot.GetChildCount()-1);
        frontend.PlayMovie(movie,enter);
    }
}
