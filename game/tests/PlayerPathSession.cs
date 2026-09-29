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

/// <summary>Player-path driver. Keys/clicks enter through ParseInputEvent; world aiming
/// moves the actual window cursor with Viewport.WarpMouse. Live fields and hit boxes are READ ONLY. No paused processing, manual tick calls,
/// fixture spawns, cursor overrides, OpenGate, forced needs or menu handler invocations.</summary>
public partial class PlayerPathSession : Node
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    Viewer viewer;
    StreamWriter trace;
    string output, language;
    int sequence, observations, defects;
    bool interactive;
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

    async Task MenuContains(string fragment)
    {
        await Frame(2);var rows=Read<List<string>>(Panel,"_menu");
        int index=rows.FindIndex(x=>x.Contains(fragment,StringComparison.OrdinalIgnoreCase));
        Log("menu-search",new {fragment,index,rows=rows.ToArray()});
        Check(index>=0,"menu contains "+fragment);
        // Real wheel events page long lists; no writes to panel selection/scroll state.
        for(int n=0;n<30 && Panel.MenuRowScreenBox(index).Size.Y<=0;n++)
            await Click(Panel.PanelOrigin+new Vector2(250,300)*Panel.PanelScale,MouseButton.WheelDown);
        var box=Panel.MenuRowScreenBox(index);Check(box.Size.Y>0,"requested row is on screen");
        await Click(box.GetCenter());
    }
    async Task AimCell(int x,int y)
    {
        var park=Read<Park>(viewer,"_park");var camera=Read<Camera3D>(viewer,"_cam");
        var world=park.CellCentre(x,y);var screen=camera.UnprojectPosition(world);
        var area=new Rect2(new Vector2(20,20),GetViewport().GetVisibleRect().Size-new Vector2(40,40));
        for(int attempt=0;attempt<6 && (!area.HasPoint(screen)||camera.IsPositionBehind(world));attempt++)
        {
            // Pan by actual held keys if the chosen cell is beyond the current view.
            Key direction=screen.Y<area.Position.Y?Key.W:Key.S;
            Log("camera-key-hold",new {key=direction.ToString(),frames=6});
            Input.ParseInputEvent(new InputEventKey{Pressed=true,Keycode=direction,PhysicalKeycode=direction});await Frame(6);
            Input.ParseInputEvent(new InputEventKey{Pressed=false,Keycode=direction,PhysicalKeycode=direction});await Frame(6);
            screen=camera.UnprojectPosition(world);
        }
        Log("aim",new {x,y,screenX=screen.X,screenY=screen.Y,behind=camera.IsPositionBehind(world)});
        Check(!camera.IsPositionBehind(world)&&area.HasPoint(screen),"target cell is visible, not clicking offscreen");
        Input.ParseInputEvent(new InputEventMouseMotion{Position=screen,GlobalPosition=screen});await Frame(4);
        var observed=GetViewport().GetMousePosition();
        Log("pointer-after-parsed-motion",new {wantedX=screen.X,wantedY=screen.Y,observedX=observed.X,observedY=observed.Y});
        // ParseInputEvent does not move the OS cursor (Godot4.6 Input docs). World picking
        // reads the viewport pointer, unlike GUI clicks reading event.Position. Move the
        // actual window pointer; never substitute the game's cursor or call its picker.
        GetViewport().WarpMouse(screen);await Frame(4);
        observed=GetViewport().GetMousePosition();
        Log("pointer-after-os-move",new {wantedX=screen.X,wantedY=screen.Y,observedX=observed.X,observedY=observed.Y});
        Check(observed.DistanceTo(screen)<2,"actual window pointer reached target");
        Check(Read<(int X,int Y)?>(viewer,"_cursorOverride")==null,"no cursor override");
    }
    async Task ClickCell(int x,int y)
    {
        await AimCell(x,y);
        var camera=Read<Camera3D>(viewer,"_cam");var park=Read<Park>(viewer,"_park");
        await Click(camera.UnprojectPosition(park.CellCentre(x,y)));
    }
    async Task BuildFirstRide()
    {
        Check(language=="eng","build tour currently names visible English catalogue entries");
        var park=Read<Park>(viewer,"_park");var paths=Read<PathTool>(viewer,"_paths");
        var mouth=Read<List<ParkCell>>(viewer,"_mouth");
        Check(mouth is {Count:>0},"normal park owns an entrance mouth");
        var start=mouth.OrderByDescending(c=>c.Z).ThenBy(c=>c.X).First();
        int spineX=start.X;
        Log("entrance-cells",Enumerable.Range(start.Z,8).Select(z=>new {x=spineX,z,playable=park.IsPlayable(spineX,z),description=paths.Describe(spineX,z)}));
        int firstZ=Enumerable.Range(start.Z,8).First(z=>park.IsPlayable(spineX,z)&&park.Vacant(spineX,z));
        int endZ=firstZ+10;
        Log("entrance",new {mouth=mouth.Select(c=>new {c.X,c.Z}),spineX,firstZ,endZ});
        await AimCell(spineX,firstZ);await KeyPress(Key.P);await KeyPress(Key.P);
        Check(Read<int>(viewer,"_runX")==spineX,"path run starts from actual pointer");
        int before=paths.Laid;
        await AimCell(spineX,endZ);await KeyPress(Key.P);
        Check(paths.Laid>before,"real path input lays a connected spine");
        await KeyPress(Key.Escape);await Shot("path-spine");
        await KeyPress(Key.Tab);await LaptopRow(801);await MenuContains("Rides (");await MenuContains("Crazy Ape");
        var placement=Read<Placement>(viewer,"_place");
        Check(placement.Active,"catalogue click arms ride placement");
        // Observe valid footprint/stub geometry; rotate by actual key events only. Pick a site
        // with both doors facing the path spine so the connecting legs stay short and visible.
        (int X,int Y)? chosen=null;
        for(int turn=0;turn<4 && chosen==null;turn++)
        {
            var candidates=new List<(int X,int Y,int Score)>();
            for(int y=start.Z+2;y<=endZ-1;y++)
                for(int x=spineX-7;x<=spineX+7;x++)
                {
                    if(!placement.Fits(park,x,y))continue;
                    var stubs=placement.Stubs(park,x,y).ToArray();
                    if(stubs.Length<2 || stubs.Any(c=>c.Y<=start.Z||c.Y>endZ||Math.Abs(c.X-spineX)>3))continue;
                    var body=placement.Cells(park,x,y).ToArray();
                    bool clear=stubs.All(stub=>Enumerable.Range(Math.Min(stub.X,spineX),Math.Abs(stub.X-spineX)+1)
                        .All(cx=>!body.Any(b=>b.X==cx&&b.Y==stub.Y)&&park.Vacant(cx,stub.Y)));
                    if(clear)candidates.Add((x,y,stubs.Sum(c=>Math.Abs(c.X-spineX))));
                }
            if(candidates.Count>0){var c=candidates.OrderBy(c=>c.Score).First();chosen=(c.X,c.Y);}
            else await KeyPress(Key.R);
        }
        Check(chosen!=null,"a visible plausible ride site beside spine exists");
        var site=chosen.Value;
        Log("placement-observation",new {name=placement.Display,site.X,site.Y,turn=placement.Turns});
        int count=park.Placed.Count;
        await ClickCell(site.X,site.Y);
        Check(park.Placed.Count==count+1,"ordinary left click purchases and places ride");
        await Frame(8);await Shot("ride-placed");
        // Placement itself opens its owned queue. Follow its actual run, not a detached queue.
        for(int leg=0;leg<3 && Read<bool>(viewer,"_toolOpen");leg++)
        {
            await Frame(8);int fromX=Read<int>(viewer,"_runX"),fromY=Read<int>(viewer,"_runY");
            Log("automatic-door-tool",new {kind=Read<PathTool.Kind>(viewer,"_toolKind").ToString(),fromX,fromY});
            Check(fromX>=0&&fromY>=0,"placement starts queue/exit run at actual door");
            await ClickCell(spineX,fromY);
        }
        if(Read<bool>(viewer,"_toolOpen"))await KeyPress(Key.Escape);
        await Shot("ride-connected");
        ulong finish=Time.GetTicksMsec()+(interactive?10000UL:90000UL);
        while(Time.GetTicksMsec()<finish)
        {
            await ToSignal(GetTree().CreateTimer(10),SceneTreeTimer.SignalName.Timeout);
            var sim=Read<ParkSim>(viewer,"_sim");var guests=Read<GuestWalk>(viewer,"_guests");
            Log("normal-observation",new {ticks=Read<int>(viewer,"_parkTicks"),guests=guests?.Guests.Count,
                money=sim.Finances.Balance,rides=sim.Rides.Select(r=>new {r.Id,r.Name,r.Customers,r.Takings})});
        }
        await Shot("ride-after-90s");
    }

    void Observe()
    {
        var park=Read<Park>(viewer,"_park");var sim=Read<ParkSim>(viewer,"_sim");
        var guests=Read<GuestWalk>(viewer,"_guests");var placement=Read<Placement>(viewer,"_place");
        var visitors=Read<ParkVisitors>(viewer,"_visitors");
        var staff=Read<ParkStaff>(viewer,"_staff");
        var queues=Read<NativeRideQueues>(viewer,"_rideQueues");
        var seated=Read<Dictionary<int,(Transform3D At,string Where,Vector3 Forward,string Part,float PartTop)>>(viewer,"_seated");
        Log("state",new {
            ticks=Read<int>(viewer,"_parkTicks"),laptop=Panel.Open,menu=Read<List<string>>(Panel,"_menu").ToArray(),
            panelTitle=Read<string>(Panel,"_title"),selected=Panel.Selected,scroll=Panel.ScrollRow,
            held=placement.Display,heldActive=placement.Active,turns=placement.Turns,
            pathTool=Read<bool>(viewer,"_toolOpen"),kind=Read<PathTool.Kind>(viewer,"_toolKind").ToString(),
            runX=Read<int>(viewer,"_runX"),runY=Read<int>(viewer,"_runY"),money=sim?.Finances.Balance,
            placed=park.Placed.Select(p=>new {p.Id,p.Name,p.X,p.Y}),
            rides=sim?.Rides.Select(r=>new {r.Id,r.Name,r.Customers,r.Takings,
                scriptOnRide=r.Has("VAR_ONRIDE")?(int?)r.OnRide:null,scriptQueue=r.Queue.Count,
                toiletCondition=r.ProvidesRelief?(int?)r.Condition:null}),
            seated=seated.Select(p=>new {guest=p.Key,where=p.Value.Where,
                position=new[]{p.Value.At.Origin.X,p.Value.At.Origin.Y,p.Value.At.Origin.Z}}),
            nativeQueues=queues==null?null:new {queues.Joined,queues.Boarded,queues.Released,queues.Quits,queues.Impatient},
            visitorCounters=visitors==null?null:new {visitors.Boardings,visitors.Rides,visitors.Purchases,
                visitors.Relieved,visitors.LitterDropped,visitors.LitterBinned,visitors.Vomited},
            plans=visitors?.Plans.Values.Select(p=>new {p.Guest,p.RideId,intent=p.Intent.ToString()}),
            needs=visitors?.Needs.All.Select(p=>new {guest=p.Key,p.Value.Cash,p.Value.Hunger,
                p.Value.Thirst,p.Value.Toilet,p.Value.Litter}),
            litter=staff==null?null:new {staff.Litter.Count,staff.Litter.Made,staff.Litter.Swept},
            staff=staff?.Members.Select(m=>new {kind=m.Kind.ToString(),m.Active,m.Held,m.Mode,m.State,m.LogicalRequest}),
            guests=guests?.Guests.Select(g=>new {g.Id,state=g.State.ToString()}),
            padLocked=Read<ParkAdvisor>(viewer,"_parkAdvisor")?.PadLocked
        });
    }
    async Task InteractiveLoop()
    {
        string commands=Path.Combine(output,"commands.jsonl");File.WriteAllText(commands,"");
        int consumed=0;Log("interactive-ready",new {commands});Observe();
        ulong nextObservation=Time.GetTicksMsec()+5000;
        bool ridingShot=false,litterShot=false;
        ulong deadline=Time.GetTicksMsec()+45*60*1000UL; // bounded session, not an orphaned renderer
        while(Time.GetTicksMsec()<deadline)
        {
            await Frame();
            if(Time.GetTicksMsec()>=nextObservation)
            {
                Observe();nextObservation=Time.GetTicksMsec()+5000;
                var seats=Read<System.Collections.IDictionary>(viewer,"_seated");
                if(!ridingShot&&!Panel.Open&&seats.Count>0)
                { ridingShot=true;await Shot("natural-seated-guests-observed"); }
                var visitors=Read<ParkVisitors>(viewer,"_visitors");
                if(!litterShot&&!Panel.Open&&visitors?.LitterDropped>0)
                { litterShot=true;await Shot("natural-guest-litter-counter-positive"); }
            }
            var text=File.ReadAllText(commands);
            // The producer may be appending as a frame reads. Ignore an incomplete last line;
            // never turn a partially written input command into a game-failure report.
            int complete=text.LastIndexOf('\n');
            if(complete<0)continue;
            var lines=text[..complete].Split('\n');
            while(consumed<lines.Length)
            {
                string line=lines[consumed++];if(string.IsNullOrWhiteSpace(line))continue;
                Log("command",line);
                using var doc=JsonDocument.Parse(line);var c=doc.RootElement;
                string op=c.GetProperty("op").GetString();
                switch(op)
                {
                    case "key": await KeyPress(Enum.Parse<Key>(c.GetProperty("key").GetString(),true)); break;
                    case "menu": await MenuContains(c.GetProperty("text").GetString()); break;
                    case "click": await Click(new Vector2(c.GetProperty("x").GetSingle(),c.GetProperty("y").GetSingle()),
                        c.TryGetProperty("button",out var button)?Enum.Parse<MouseButton>(button.GetString(),true):MouseButton.Left); break;
                    case "aim-cell": await AimCell(c.GetProperty("x").GetInt32(),c.GetProperty("y").GetInt32()); break;
                    case "click-cell": await ClickCell(c.GetProperty("x").GetInt32(),c.GetProperty("y").GetInt32()); break;
                    case "back": await Back(); break;
                    case "close": await CloseLaptop(); break;
                    case "shot":
                        string name=c.GetProperty("name").GetString();
                        Check(name.All(ch=>char.IsLetterOrDigit(ch)||ch=='-'),"safe screenshot filename");
                        await Shot(name);break;
                    case "wait":
                        double seconds=c.GetProperty("seconds").GetDouble();Check(seconds>=0&&seconds<=120,"bounded real-time wait");
                        await ToSignal(GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);break;
                    case "state": break;
                    case "quit": return;
                    default: throw new InvalidDataException("unknown input command "+op);
                }
                Observe();Log("command-done",new {consumed,op});
            }
        }
        Log("session-timeout","45 minute real-time guard reached");
    }

    CpuParticles3D[] ContinuousNodes()
    {
        var owner=Read<RideParticles>(viewer,"_burst");
        if(owner==null)return Array.Empty<CpuParticles3D>();
        var map=Read<System.Collections.IDictionary>(owner,"_continuous");
        return map.Values.Cast<CpuParticles3D>().ToArray();
    }
    void ObserveParticles(string phase)
    {
        var owner=Read<RideParticles>(viewer,"_burst");
        var root=owner==null?null:Read<Node3D>(owner,"_root");
        var nodes=ContinuousNodes();
        Log("particle-observation",new {phase,count=nodes.Length,rootChildren=root?.GetChildCount(),
            nodes=nodes.Select(n=>IsInstanceValid(n)?(object)new {valid=true,id=n.GetInstanceId(),
                emitting=n.Emitting,inTree=n.IsInsideTree(),visible=n.IsVisibleInTree(),
                position=n.IsInsideTree()?new[]{n.GlobalPosition.X,n.GlobalPosition.Y,n.GlobalPosition.Z}:null}
                :new {valid=false})});
    }
    async Task WorldResetTour(bool configuredOpen)
    {
        Check(language=="eng","focused reset tour names English catalogue rows");
        int sourceMap=Read<int>(viewer,"_loadedMap");
        await LaptopRow(801);await MenuContains("Shops (");await MenuContains("Drinks Shop");
        await ClickCell(38,32);
        Check(Read<Park>(viewer,"_park").Placed.Count==1,"Drinks Shop purchased through ordinary placement");
        await Wait(()=>ContinuousNodes().Any(n=>IsInstanceValid(n)&&n.Emitting&&n.IsVisibleInTree()),60,
            "normal Create produced a visible, emitting continuous source (nonvacuous)");
        var originals=ContinuousNodes();
        var originalIds=originals.Select(n=>n.GetInstanceId()).ToArray();
        var effectAt=originals[0].GlobalPosition; // COPY, not a later lookup into a freed object
        ObserveParticles("source-active");await Shot("continuous-source-active");
        await Wait(()=>Read<ParkAdvisor>(viewer,"_parkAdvisor")?.PadLocked!=true,60,"input available for Close Park");
        await KeyPress(Key.Tab);await LaptopRow(844);
        await Wait(()=>Read<bool>(viewer,"_lobbyMode"),15,"Close Park returns to lobby");
        await KeyPress(Key.Left);await KeyPress(Key.Up);
        Check(Read<int>(viewer,"_lobbyRecord")==1,"real lobby navigation selected JUNGLE second park");
        await Shot("reset-destination-selected");await KeyPress(Key.Enter);await KeyPress(Key.Enter);
        var frontend=Read<FrontendScreen>(viewer,"_frontend");
        if(frontend.CurrentStage==FrontendScreen.Stage.Movie)await KeyPress(Key.Enter);
        await Wait(()=>!Read<bool>(viewer,"_lobbyMode")&&Read<int>(viewer,"_loadedMap")!=sourceMap,30,
            "ordinary lobby confirmation loaded a different park");
        await Frame(3);
        Check(Read<Park>(viewer,"_park").Placed.Count==0,"destination has no placed source shop");
        ObserveParticles("destination-after-deferred-free");
        Finding("continuous entries after map switch","0",ContinuousNodes().Length.ToString(),"cow tools particle cleanup");
        int retained=originals.Count(n=>IsInstanceValid(n));
        Finding("old emitter instances still valid","0",retained.ToString(),"cow tools particle cleanup");
        Log("source-identities-after-switch",new {originalIds,retained});
        Finding("fresh destination open state",configuredOpen.ToString(),Read<bool>(viewer,"_laptopParkOpen").ToString(),"cow tools park state");
        Check(Read<AnimatedModel>(viewer,"_terrain").Root.IsVisibleInTree(),"destination terrain effectively visible");
        await AimCell(38,32);
        var camera=Read<Camera3D>(viewer,"_cam");var projected=camera.UnprojectPosition(effectAt);
        var basis=camera.GlobalBasis;var origin=camera.GlobalPosition;
        Log("old-effect-projection",new {effectAt=new[]{effectAt.X,effectAt.Y,effectAt.Z},x=projected.X,y=projected.Y,
            camera=new[]{basis.X.X,basis.X.Y,basis.X.Z,basis.Y.X,basis.Y.Y,basis.Y.Z,basis.Z.X,basis.Z.Y,basis.Z.Z,origin.X,origin.Y,origin.Z},
            fov=camera.Fov,viewport=new[]{GetViewport().GetVisibleRect().Size.X,GetViewport().GetVisibleRect().Size.Y}});
        await ToSignal(GetTree().CreateTimer(3),SceneTreeTimer.SignalName.Timeout);
        ObserveParticles("destination-after-three-simulation-seconds");await Shot("empty-destination-at-old-emitter");
        await KeyPress(Key.Tab);
        await Wait(()=>Panel.Open,10,"destination laptop opens");
        bool hasOpen=Read<List<string>>(Panel,"_menu").Contains(Text(617));
        Finding("fresh destination offers Open Park",(!configuredOpen).ToString(),hasOpen.ToString(),"cow tools park state");
        await Shot("destination-open-park-row");
        if(hasOpen)
        {
            await LaptopRow(617);
            Check(Read<bool>(viewer,"_laptopParkOpen"),"new park can be opened independently through its own row");
        }
    }
    async Task EndSession(bool strict,string scope)
    {
        Log("complete",new {observations,automaticMismatches=defects,scope});
        GD.Print($"PLAYER PATH COMPLETE observations={observations} automaticMismatches={defects} language={language}");
        viewer.QueueFree();await Frame(2);GetTree().Quit(strict&&defects>0?2:0);
    }

    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();
            bool worldReset=args.Contains("--play-tour=world-reset");
            bool configuredOpen=args.Contains("--laptop-park-open");
            output=args.FirstOrDefault(a=>a.StartsWith("--play-out="))?[11..]
                ??throw new ArgumentException("--play-out=required");
            language=args.FirstOrDefault(a=>a.StartsWith("--play-language="))?[16..]??"fre";
            Check(language is "fre" or "ger" or "eng","known language");
            if(Directory.Exists(output))throw new IOException("use a fresh artifact directory");
            Directory.CreateDirectory(output);trace=new StreamWriter(Path.Combine(output,"input-observations.jsonl"));
            interactive=args.Contains("--play-interactive");
            Log("scope",new {language,build=args.FirstOrDefault(a=>a.StartsWith("--play-build="))?[13..]??"UNSPECIFIED",processing="normal",actions="ParseInputEvent keys/clicks + actual window pointer movement; no game-state writes",visualReview="pending"});
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
            if(worldReset)
            {
                await KeyPress(Key.Right);
                Check(Read<int>(viewer,"_lobbyRecord")==4,"real lobby navigation selected FANTASY first park");
            }
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
            Check(Read<bool>(viewer,"_laptopParkOpen")==configuredOpen,"fresh park open state matches explicit launch policy");
            if(!configuredOpen)await LaptopRow(617); // Open Park, actual displayed translated row
            Check(Read<bool>(viewer,"_laptopParkOpen"),"Open Park click changed actual state");
            if(!Panel.Open)await KeyPress(Key.Tab);
            await Shot("laptop-main-open");
            if(worldReset)
            {
                await WorldResetTour(configuredOpen);
                await EndSession(args.Contains("--play-expect-clean"),"real input world reset: particles, open state, terrain visibility");
                return;
            }
            if(args.Contains("--play-tour=build"))
            {
                await CloseLaptop();
                await BuildFirstRide();
                if(interactive)await InteractiveLoop();
                await EndSession(args.Contains("--play-expect-clean"),"normal first-park build/service journey; inspect per-event evidence");
                return;
            }
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
            if(interactive)await InteractiveLoop();
            await EndSession(args.Contains("--play-expect-clean"),"cold boot / open park / research / cleaner candidate; no building or service-use claims");
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
