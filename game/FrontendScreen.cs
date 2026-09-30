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
    readonly Dictionary<string,Image> _artPixels = new(StringComparer.OrdinalIgnoreCase);
    Rect2[] _langBands;   // option rows in ART space (0..512), derived from the art itself
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
    /// <summary>⭐⭐ THE THREE OPTION ROWS, DERIVED FROM THE ART RATHER THAN GUESSED. The old note
    /// on _GuiInput was right to refuse invented hotspots -- the words are baked into the .ssh and
    /// there is no per-item geometry anywhere in the data. But the three language arts are the SAME
    /// PICTURE with a DIFFERENT ROW HIGHLIGHTED, so the pixels that differ between them ARE the
    /// rows. LangUK vs LangGER changes English and Deutsch; LangUK vs LangFRE changes English and
    /// Francais; the union is all three, top to bottom, which is `LanguageIndex` order.
    ///
    /// ⚠ So this is measured off the shipped art every run, not a rect I read off a screenshot and
    /// typed in. If the art is ever replaced the hotspots follow it, and if the art is missing this
    /// returns empty and the mouse simply does nothing rather than selecting a guessed band.</summary>
    Rect2[] LanguageBands()
    {
        if(_langBands!=null) return _langBands;
        var a=ArtImage($"/Lang/{LanguageArt[0]}.ssh");
        var b=ArtImage($"/Lang/{LanguageArt[1]}.ssh");
        var c=ArtImage($"/Lang/{LanguageArt[2]}.ssh");
        if(a==null||b==null||c==null) return _langBands=Array.Empty<Rect2>();
        int w=Math.Min(a.GetWidth(),Math.Min(b.GetWidth(),c.GetWidth()));
        int h=Math.Min(a.GetHeight(),Math.Min(b.GetHeight(),c.GetHeight()));
        // Per row: does anything differ, and over which x span.
        var lo=new int[h]; var hi=new int[h]; var any=new bool[h];
        for(int y=0;y<h;y++){ lo[y]=int.MaxValue; hi[y]=int.MinValue; }
        for(int y=0;y<h;y++)
            for(int x=0;x<w;x++)
            {
                var pa=a.GetPixel(x,y); var pb=b.GetPixel(x,y); var pc=c.GetPixel(x,y);
                if(Same(pa,pb) && Same(pa,pc)) continue;
                // ⚠⚠ THE MAP CHANGES TOO, AND IT IS THE BIGGER DIFFERENCE. Each art carries its own
                // country silhouette -- UK, Germany, France -- so a plain "what differs" diff
                // returns one band covering x 0..246, y 48..488: the map, with the three word rows
                // buried inside it. Measured, not guessed: that is exactly what the first run
                // reported. So only a change in TEXT counts -- the words are bright white (chosen)
                // or bright yellow (not chosen), and the map is dark navy.
                if(!Bright(pa) && !Bright(pb) && !Bright(pc)) continue;
                any[y]=true; if(x<lo[y])lo[y]=x; if(x>hi[y])hi[y]=x;
            }
        // Group consecutive changed rows into bands, ignoring stray single-row noise.
        var bands=new List<Rect2>(); int start=-1;
        for(int y=0;y<=h;y++)
        {
            bool on=y<h&&any[y];
            if(on&&start<0) start=y;
            else if(!on&&start>=0)
            {
                int x0=int.MaxValue,x1=int.MinValue;
                for(int k=start;k<y;k++){ if(lo[k]<x0)x0=lo[k]; if(hi[k]>x1)x1=hi[k]; }
                if(y-start>=4 && x1>x0) bands.Add(new Rect2(x0,start,x1-x0+1,y-start));
                start=-1;
            }
        }
        // ⚠ Exactly three or nothing: two bands would mean a pairing assumption I have not earned,
        // and silently mapping the mouse onto the wrong option is worse than an inert mouse.
        if(bands.Count!=3)
        {
            // ⚠ Say WHAT was found, not just that it was wrong: one band spanning the whole
            // block means the rows merged and need splitting; a band covering the image means the
            // arts differ in more than the highlight. The count alone cannot tell those apart.
            GD.Print($"[frontend] language hotspots: {bands.Count} changed bands, not 3 -- mouse selection off. "
                   + "bands: " + (bands.Count==0 ? "(none)" :
                     string.Join(", ", bands.ConvertAll(r=>$"y {r.Position.Y:F0}..{r.Position.Y+r.Size.Y:F0} x {r.Position.X:F0}..{r.Position.X+r.Size.X:F0}"))));
            return _langBands=Array.Empty<Rect2>();
        }
        bands.Sort((p,q)=>p.Position.Y.CompareTo(q.Position.Y));
        GD.Print("[frontend] language hotspots from art: "
               + string.Join(", ", bands.ConvertAll(r=>$"[{r.Position.X:F0},{r.Position.Y:F0} {r.Size.X:F0}x{r.Size.Y:F0}]")));
        return _langBands=bands.ToArray();
    }
    /// <summary>A language word: bright, and not the dark navy of the country map. White is the
    /// chosen row and yellow the others, so both must pass and blue must not.</summary>
    static bool Bright(Color p)=>(p.R+p.G)*0.5f>0.55f && p.B<0.8f*Mathf.Max(p.R,p.G);
    static bool Same(Color p,Color q)=>Mathf.Abs(p.R-q.R)<0.02f&&Mathf.Abs(p.G-q.G)<0.02f&&Mathf.Abs(p.B-q.B)<0.02f;
    Image ArtImage(string name){ Art(name); return _artPixels.TryGetValue(name,out var i)?i:null; }

    /// <summary>Which option the pointer is over, or -1. ⚠ The art is drawn into a 512-square that
    /// is letterboxed inside the window, so the pointer has to be taken back through the same
    /// origin/scale _Draw uses -- testing window pixels against art rects would drift with the
    /// window size and be wrong on every aspect but one.</summary>
    int LanguageAt(Vector2 mouse)
    {
        var bands=LanguageBands();
        if(bands.Length==0) return -1;
        var view=GetViewportRect().Size;
        float scale=Mathf.Min(view.X,view.Y)/512f;
        if(scale<=0f) return -1;
        var art=(mouse-(view-Vector2.One*(512*scale))/2)/scale;
        for(int i=0;i<bands.Length;i++)
        {
            var r=bands[i];
            // ⭐ Full-width rows: the pointer only has to be at the right HEIGHT, which is how a
            // menu row behaves. Requiring the exact glyph box would make the words feel like
            // hairlines to hit.
            if(art.Y>=r.Position.Y-2 && art.Y<=r.Position.Y+r.Size.Y+2) return i;
        }
        return -1;
    }

    public void KeyInput(InputEventKey key)
    {
        // ⭐⭐ ANY KEY SKIPS A CUTSCENE. Master: "any key / mouse button to skip cutscenes".
        // ⚠ Taken BEFORE the switch, so a movie is not quietly waiting for the two keycodes that
        // happened to be wired -- a player mashing anything to get past a logo is the case this is
        // for, and Escape alone did not serve it.
        if(CurrentStage==Stage.Movie){ Confirm(); return; }
        switch(key.Keycode)
        {
            case Key.Up: case Key.Left: MoveLanguage(-1); break;
            case Key.Down: case Key.Right: MoveLanguage(1); break;
            case Key.Enter: case Key.KpEnter: case Key.Space: Confirm(); break;
        }
    }
    public override void _GuiInput(InputEvent input)
    {
        // ⭐⭐ MOUSE ON THE LANGUAGE SCREEN. The old note here refused invented hotspots over the
        // baked art, and it was right to -- so the rows are DERIVED from the art instead (see
        // LanguageBands). Hover highlights, click picks. Master: "add mouse hover n click
        // functionality to the language select screen".
        //
        // ⭐ Hover needs no new drawing: the highlight IS the art, so moving LanguageIndex under
        // the pointer repaints the selected row for free and hover and keyboard cannot disagree.
        if(CurrentStage==Stage.Language)
        {
            if(input is InputEventMouseMotion motion)
            {
                int over=LanguageAt(motion.Position);
                if(over>=0 && over!=LanguageIndex){ LanguageIndex=over; QueueRedraw(); }
            }
            else if(input is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left} click)
            {
                int on=LanguageAt(click.Position);
                // ⚠ A click OFF the rows does nothing. Confirming whatever happened to be
                // selected would turn a misclick on the map into a language choice.
                if(on>=0){ LanguageIndex=on; QueueRedraw(); Confirm(); }
            }
            AcceptEvent(); return;
        }
        // ⭐⭐ ANY MOUSE BUTTON SKIPS A CUTSCENE, not just the left one. Master: "any key /
        // mouse button to skip cutscenes". Legal keeps its click-to-advance as before.
        if(input is InputEventMouseButton {Pressed:true} button
            && (CurrentStage==Stage.Movie || (CurrentStage==Stage.Legal && button.ButtonIndex==MouseButton.Left)))
            Confirm();
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
                var img=Image.CreateFromData(ssh.Width,ssh.Height,false,Image.Format.Rgba8,ssh.Pixels);
                _artPixels[name]=img;
                image=ImageTexture.CreateFromImage(img);
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
            // ⚠ It only offers the mouse when the hotspots actually derived from the art. If the
            // art is missing the mouse does nothing, and a hint promising it would be a lie on
            // screen -- the one place a wrong instruction is unmissable.
            var hint=_font.Render(LanguageBands().Length==3
                ? "Up/Down or mouse: language   Enter or click: continue"
                : "Up/Down: language   Enter: continue");
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
