using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

/// <summary>Cold boot and park-entry movie adapter. Source: findings/main-menu.md and
/// frontend-startup-inventory.md. No memory-card emulation, attract mode or ending sequence.
/// Movies are the owner's already converted 640x480 square-pixel OGVs, NOT raw MPCs.</summary>
public partial class FrontendScreen : Control
{
    public enum Stage { Idle, EaLogo, Language, Movie, Legal }
    public Stage CurrentStage { get; private set; }
    public bool Active => CurrentStage != Stage.Idle;
    public bool LegalPromptVisible => CurrentStage==Stage.Legal && _phaseTime*25>=160;
    public int LanguageIndex { get; private set; }
    public static readonly string[] TextLanguages = { "eng", "ger", "fre" };
    public static readonly string[] AudioLanguages = { "English", "German", "French" };
    public static readonly int[] RetailLanguages = { 0, 3, 1 };
    static readonly string[] LanguageArt = { "LangUK", "LangGER", "LangFRE" };
    static readonly string[] WorldMovies = { "DINO", "FRANK", "FLOWER", "SPACEMAN" };
    public static string WorldMovie(int world) => world >= 0 && world < 4 ? WorldMovies[world] : null;
    public event Action<int> LanguageChosen;
    readonly Dictionary<string,ImageTexture> _art = new(StringComparer.OrdinalIgnoreCase);
    AssetLibrary _lib;
    FontText _font;
    TextDatabase _text;
    VideoStreamPlayer _video;
    Action _bootDone, _movieDone, _finishedHandler;
    string _directory;
    long _generation;
    double _phaseTime;
    int _eaFrames;
    bool _sawPlaying, _reportedSize;
    public string MovieStem { get; private set; }
    public string LastMovieResult { get; private set; } = "";

    public FrontendScreen() { MouseFilter=MouseFilterEnum.Stop; Visible=false; }
    public void Configure(AssetLibrary lib, FontText font, TextDatabase text, string directory)
    { _lib=lib; _font=font; _text=text; _directory=directory; }
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _video=new VideoStreamPlayer { Expand=true, MouseFilter=MouseFilterEnum.Ignore };
        AddChild(_video); LayoutMovie();
    }
    public override void _Notification(int what)
    { if (what==NotificationResized) { LayoutMovie(); QueueRedraw(); } }
    public override void _ExitTree() { CancelMovie(); _bootDone=null; }

    // EA lasts for boot work on the console, not a traced fixed timeout. Here initialization
    // has already finished synchronously: retain it for two process passes so it can render,
    // then select a language. Do not present this adapter delay as a measured retail timer.
    public void Boot(Action done)
    {
        CancelMovie(); _bootDone=done; LanguageIndex=0; _eaFrames=0;
        ShowStage(Stage.EaLogo);
    }
    void ShowStage(Stage stage)
    { CurrentStage=stage; _phaseTime=0; Visible=stage!=Stage.Idle; QueueRedraw(); }
    void LayoutMovie()
    {
        if(_video==null) return;
        var view=GetViewportRect().Size;
        float scale=Mathf.Min(view.X/640f,view.Y/480f);
        _video.Size=new Vector2(640,480)*scale;
        _video.Position=(view-_video.Size)/2;
    }
    public override void _Process(double delta)
    {
        if(!Active) return;
        _phaseTime+=delta;
        if(CurrentStage==Stage.EaLogo && ++_eaFrames>=2) ShowStage(Stage.Language);
        else if(CurrentStage==Stage.Legal) QueueRedraw();
        else if(CurrentStage==Stage.Movie)
        {
            _sawPlaying |= _video.IsPlaying();
            if(!_reportedSize && _video.GetVideoTexture() is { } texture && texture.GetWidth()>0)
            {
                _reportedSize=true;
                GD.Print($"[frontend] {MovieStem} decoded={texture.GetWidth()}x{texture.GetHeight()}, display=4:3");
                if(texture.GetWidth()==640 && texture.GetHeight()==352)
                    GD.PrintErr("[frontend] raw anamorphic encode selected; the corrected Windows set is movies-ogv, not movies");
            }
            // Broken/undecodable files must not hold the front end forever. This is a PC
            // failure watchdog, not a movie duration or a retail timing claim.
            if(!_sawPlaying && _phaseTime>1) FinishMovie(_generation,"decoder did not start");
        }
    }
    public void MoveLanguage(int by)
    {
        if(CurrentStage!=Stage.Language) return;
        LanguageIndex=Math.Clamp(LanguageIndex+by,0,2); QueueRedraw();
    }
    public void Confirm()
    {
        switch(CurrentStage)
        {
            case Stage.Language:
                LanguageChosen?.Invoke(LanguageIndex);
                PlayMovie("BFLOGO",()=>ShowStage(Stage.Legal)); break;
            case Stage.Movie: FinishMovie(_generation,"skipped"); break;
            case Stage.Legal:
                var done=_bootDone; _bootDone=null; ShowStage(Stage.Idle); done?.Invoke(); break;
        }
    }
    public void KeyInput(InputEventKey key)
    {
        switch(key.Keycode)
        {
            case Key.Up: case Key.Left: MoveLanguage(-1); break;
            case Key.Down: case Key.Right: MoveLanguage(1); break;
            case Key.Enter: case Key.KpEnter: case Key.Space: Confirm(); break;
            case Key.Escape: if(CurrentStage==Stage.Movie) Confirm(); break;
        }
    }
    public override void _GuiInput(InputEvent input)
    {
        // No guessed hotspots over the baked language art. Keyboard selects that screen.
        // A PC click may advance legal/skip a movie; main-menu rows have their own hit tests.
        if(input is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left}
            && CurrentStage is Stage.Movie or Stage.Legal) Confirm();
        AcceptEvent();
    }
    public void PlayMovie(string stem, Action continuation)
    {
        CancelMovie(); ShowStage(Stage.Movie); MovieStem=stem; _movieDone=continuation;
        _sawPlaying=false; _reportedSize=false; long generation=_generation;
        var path=ResolveMovie(_directory,stem);
        if(path==null)
        {
            GD.PrintErr($"[frontend] missing {stem}.ogv in '{_directory}'; set --movies-dir= or TPW_PS2_MOVIES. Continuing without this movie.");
            FinishMovie(generation,"missing"); return;
        }
        try
        {
            using(var f=File.OpenRead(path))
            {
                Span<byte> signature=stackalloc byte[4];
                if(f.Read(signature)!=4 || !signature.SequenceEqual("OggS"u8))
                    throw new InvalidDataException("not an Ogg stream");
            }
            _video.Stream=new VideoStreamTheora { File=path };
            _finishedHandler=()=>FinishMovie(generation,"finished");
            _video.Finished+=_finishedHandler;
            _video.Show(); LayoutMovie(); _video.Play();
            GD.Print($"[frontend] playing {stem}: {path}");
        }
        catch(Exception e)
        { GD.PrintErr($"[frontend] {stem}: {e.Message}"); FinishMovie(generation,"unreadable"); }
    }
    void FinishMovie(long generation,string why)
    {
        if(generation!=_generation || CurrentStage!=Stage.Movie) return;
        var next=_movieDone; _movieDone=null; LastMovieResult=why;
        GD.Print($"[frontend] {MovieStem}: {why}");
        CancelMovie(); ShowStage(Stage.Idle); next?.Invoke();
    }
    void CancelMovie()
    {
        ++_generation; _movieDone=null;
        if(_video==null) return;
        if(_finishedHandler!=null) _video.Finished-=_finishedHandler;
        _finishedHandler=null; _video.Stop(); _video.Hide(); _video.Stream=null;
    }
    public static string ResolveMovie(string directory,string stem)
    {
        if(string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;
        try
        {
            foreach(var file in Directory.EnumerateFiles(directory))
                if(Path.GetExtension(file).Equals(".ogv",StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileNameWithoutExtension(file).Equals(stem,StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(file);
        }
        catch(IOException) { } catch(UnauthorizedAccessException) { }
        return null;
    }
    ImageTexture Art(string name)
    {
        if(_art.TryGetValue(name,out var image)) return image;
        try
        {
            var bytes=_lib?.ReadSide("FRONTEND.WAD",name);
            if(bytes!=null)
            {
                var ssh=new Ssh(bytes);
                image=ImageTexture.CreateFromImage(Image.CreateFromData(ssh.Width,ssh.Height,false,Image.Format.Rgba8,ssh.Pixels));
            }
            else GD.PrintErr($"[frontend] missing FRONTEND.WAD{name}");
        }
        catch(Exception e) { GD.PrintErr($"[frontend] {name}: {e.Message}"); }
        return _art[name]=image;
    }
    public override void _Draw()
    {
        if(!Active) return;
        var view=GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero,view),Colors.Black);
        // Keep the existing front-end square mapping until the owner's outstanding aspect
        // policy decision. This is NOT a claim that retail drew square. Movies alone are 4:3.
        float scale=Mathf.Min(view.X,view.Y)/512f;
        var origin=(view-Vector2.One*(512*scale))/2;
        string path=CurrentStage switch
        {
            Stage.EaLogo=>"/EAGames/EAGAMES.ssh",
            Stage.Language=>$"/Lang/{LanguageArt[LanguageIndex]}.ssh",
            Stage.Legal=>"/Mainmenu/Mainback1.ssh", _=>null
        };
        var art=path==null?null:Art(path);
        if(art!=null) DrawTextureRect(art,new Rect2(origin,Vector2.One*(512*scale)),false);
        if(_font==null) return;
        if(CurrentStage==Stage.Language)
        {
            // Explicit PC navigation hint, not an invented console widget/hotspot.
            var hint=_font.Render("Up/Down: language   Enter: continue");
            float s=Mathf.Min(scale*.55f,view.X/Mathf.Max(1,hint.GetWidth()));
            var size=hint.GetSize()*s;
            DrawTextureRect(hint,new Rect2(new Vector2((view.X-size.X)/2,view.Y-size.Y-8),size),false);
        }
        if(LegalPromptVisible)
        {
            var prompt=_font.Render(_text?.Text(TextLanguages[LanguageIndex],877)??"Press START button to Continue");
            // The delay and origin are read; this uses the existing font adapter, not an
            // independently verified legal-screen font/size reconstruction.
            var size=prompt.GetSize()*scale;
            DrawTextureRect(prompt,new Rect2(origin+new Vector2(250*scale-size.X/2,250*scale),size),false);
        }
    }
}
