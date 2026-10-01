using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>
/// Rendered shipping-Viewer integration fixture. Run this scene on a rendering display with
/// --disc=... --map=WORLD --mode=park (also accepts the matrix's disc environment and full map label).
/// Fixtures are explicitly UNPLACED ParkRides with real disc-compiled entries, not player-built
/// attractions. ShowSideshowDetails binds them; GUI observations use ParseInputEvent and the
/// Viewer's normally subscribed production handlers. Separately named stale-HANDLER fixtures
/// explicitly invoke callbacks; the shop continuity case declares its own PANEL callback fixture.
/// No scheduler disabling, manual simulation ticks, or writes to Viewer fields. Normal audio owners
/// are explicitly stopped ONLY during teardown, using the existing pointer-smoke cleanup protocol.
/// Draw receipts prove actual draw-site geometry/data, NOT pixel correctness/visual review.
/// </summary>
public partial class SideshowPresentationSmoke : Node3D
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Read<T>(object owner, string field) => (T)(owner.GetType().GetField(field, Private)
        ?? throw new MissingFieldException(owner.GetType().Name, field)).GetValue(owner);
    static readonly MethodInfo BindMethod = typeof(Viewer).GetMethod("ShowSideshowDetails", Private)
        ?? throw new MissingMethodException("Viewer.ShowSideshowDetails");
    Viewer _viewer;
    LaptopShopScreen _panel;
    int _checks;
    ulong _deadline;
    Vector2 _pointer;
    string _shotDir;
    readonly List<(int Row, int By)> _nudges = new();
    readonly List<(int Row, int Percent)> _slides = new();

    void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
    async Task Frames(int count = 3)
    {
        for (int i = 0; i < count; i++)
        {
            if (Time.GetTicksMsec() > _deadline) throw new TimeoutException("90s fixture deadline");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
    void Bind(ParkRide ride) => BindMethod.Invoke(_viewer, new object[] { ride });
    void Shot(string name)
    {
        if (_shotDir == null) return;
        System.IO.Directory.CreateDirectory(_shotDir);
        Check(GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_shotDir,name+".png")) == Error.Ok,
            "optional actual viewport PNG saved: " + name + " (not visually reviewed by this test)");
    }
    void StaleHandlerFixture(string name, int row, int value)
        => (typeof(Viewer).GetMethod(name, Private) ?? throw new MissingMethodException(name))
            .Invoke(_viewer,new object[]{row,value});
    Dictionary<int, Rect2> Hits(string field) => Read<Dictionary<int, Rect2>>(_panel, field);
    void Keys(string field, params int[] expected)
        => Check(Hits(field).Keys.OrderBy(x => x).SequenceEqual(expected.OrderBy(x => x)),
            field + " canonical keys " + string.Join(",", expected));
    Vector2 Native(Vector2 at) => (at - _panel.PanelOrigin) / _panel.PanelScale;
    void Y(Vector2? at, float expected, string label)
        => Check(at.HasValue && Mathf.Abs(Native(at.Value).Y - expected) < .05f, label + " native Y=" + expected);
    void BoxY(Rect2? box, float expected, string label, bool centre = false)
        => Y(box.HasValue ? (centre ? box.Value.GetCenter() : box.Value.Position) : null, expected, label);
    static Vector2 Along(Rect2 box, int percent) => box.Position + new Vector2(box.Size.X * percent / 100f, box.Size.Y / 2f);
    static (int Prize, int Price, int Chance) Settings(ParkRide r) => (r.SideshowPrizeValue,r.SideshowPrice,r.SideshowWinPercentage);
    void ExactWidget(Rect2? box, int x, int y, int w, int h, string label)
    {
        Check(box.HasValue, label + " submitted a rectangle");
        var p=Native(box.Value.Position); var size=box.Value.Size/_panel.PanelScale;
        Check(Mathf.Abs(p.X-x)<.05f && Mathf.Abs(p.Y-y)<.05f
            && Mathf.Abs(size.X-w)<.05f && Mathf.Abs(size.Y-h)<.05f, label + " literal authored rectangle");
    }
    void Motion(Vector2 at, bool held = false)
    {
        var relative = at - _pointer;
        _pointer = at;
        using var ev = new InputEventMouseMotion { Position = at, GlobalPosition = at,
            Relative = relative, ButtonMask = held ? MouseButtonMask.Left : (MouseButtonMask)0 };
        Input.ParseInputEvent(ev);
        Input.FlushBufferedEvents();
    }
    void Button(Vector2 at, bool pressed)
    {
        _pointer = at;
        using var ev = new InputEventMouseButton { Position = at, GlobalPosition = at,
            ButtonIndex = MouseButton.Left, Pressed = pressed,
            ButtonMask = pressed ? MouseButtonMask.Left : (MouseButtonMask)0 };
        Input.ParseInputEvent(ev);
        Input.FlushBufferedEvents();
    }
    async Task Point(Vector2 at)
    {
        Input.WarpMouse(at);
        Motion(at);
        await Frames();
    }
    async Task Click(Rect2 box, bool increase)
    {
        var at = Along(box, increase ? 75 : 25);
        await Point(at);
        int clicks = _panel.GuiClicks;
        Button(at, true);
        Button(at, false);
        await Frames();
        Check(_panel.GuiClicks > clicks, "ParseInputEvent reaches actual panel GUI");
    }
    async Task BeginDrag(ParkRide ride, int percent = 22)
    {
        var settings=Settings(ride);
        var box = Hits("_sliderRects")[6];
        var at = Along(box, percent);
        await Point(at);
        int events = _slides.Count;
        Button(at, true);
        await Frames();
        Check(_slides.Count > events && _slides[^1] == (6, percent), "production canonical6 slider event");
        Check(ride.SideshowWinPercentage == percent && Read<int>(_panel, "_dragSlider") == 6,
            "production chance press writes fixture and refresh preserves drag");
        Check(ride.SideshowPrizeValue==settings.Prize && ride.SideshowPrice==settings.Price, "chance press preserves both money fields");
    }
    async Task DragTo(ParkRide ride, int percent)
    {
        var settings=Settings(ride);
        int before = _slides.Count;
        Motion(Along(Hits("_sliderRects")[6], percent), true);
        await Frames();
        Check(_slides.Count > before && _slides[^1] == (6, percent), "chance drag motion canonical6");
        Check(ride.SideshowWinPercentage == percent && Read<int>(_panel, "_dragSlider") == 6,
            "same subject ShowSideshowDetails refresh keeps chance drag alive");
        Check(ride.SideshowPrizeValue==settings.Prize && ride.SideshowPrice==settings.Price, "chance motion preserves both money fields");
    }
    void EmptyBeforeDraw(string why)
    {
        Keys("_specRows"); Keys("_sliderRects"); Keys("_rowArrows");
        Check(Read<int>(_panel, "_dragSlider") == -1, why + ": cancelled before queued redraw");
    }
    async Task ShopContinuityFixture(AssetResourceDatabase db)
    {
        var entry=db.Entries.First(e=>e.Kind==AssetResourceDatabase.AssetKind.Shop);
        var a=new ParkRide{Id=900010,Name="SMOKE standalone shop A",Definition=new RideDefinition{CompiledEntry=entry},Quality=10};
        var b=new ParkRide{Id=900011,Name="SMOKE standalone shop B",Definition=new RideDefinition{CompiledEntry=entry},Quality=60};
        ParkRide current=a; int events=0;
        // Explicit PANEL fixture callback, not the shipping selected-shop route. The latter
        // normally returns here because this fixture park has no selected/placed shop.
        void Refresh(ShopScreen.Selection which,int pct)
        {
            if(which==ShopScreen.Selection.Quality) current.Quality=pct;
            else if(which==ShopScreen.Selection.Additive) current.Setting0xAC=pct;
            else return;
            events++; _panel.ShowFor(current,current.Name);
        }
        _panel.ShopSettingChanged+=Refresh;
        try
        {
            _panel.ShowFor(a,a.Name); await Frames();
            var box=Read<Dictionary<ShopScreen.Selection,Rect2>>(_panel,"_shopSliders")[ShopScreen.Selection.Quality];
            await Point(Along(box,20)); Button(_pointer,true); await Frames();
            Check(events>0 && a.Quality==20 && Read<ShopScreen.Selection?>(_panel,"_dragShop")==ShopScreen.Selection.Quality,
                "standalone shop panel fixture press survives same-subject ShowFor refresh");
            Motion(Along(box,50),true); await Frames();
            Motion(Along(box,80),true); await Frames();
            Check(events>=3 && a.Quality==80 && b.Quality==60,
                "standalone shop fixture two motions refresh same shop without changing inactive shop");
            current=b; _panel.ShowFor(b,b.Name);
            Check(Read<ShopScreen.Selection?>(_panel,"_dragShop")==null
                && Read<Dictionary<ShopScreen.Selection,Rect2>>(_panel,"_shopSliders").Count==0,
                "shop subject switch cancels drag and old hitboxes before redraw");
            int before=events; Motion(Along(box,90),true); Button(_pointer,false); await Frames();
            Check(events==before && a.Quality==80 && b.Quality==60,"shop switch rejects stale motion");
            box=Read<Dictionary<ShopScreen.Selection,Rect2>>(_panel,"_shopSliders")[ShopScreen.Selection.Quality];
            await Point(Along(box,40)); Button(_pointer,true); await Frames();
            Check(b.Quality==40 && Read<ShopScreen.Selection?>(_panel,"_dragShop")!=null,"second shop drag is nonvacuous");
            _panel.Hide();
            Check(Read<ShopScreen.Selection?>(_panel,"_dragShop")==null
                && Read<Dictionary<ShopScreen.Selection,Rect2>>(_panel,"_shopSliders").Count==0,"shop Hide immediately resets state");
            _panel.ShowFor(b,b.Name); await Frames();
            before=events; Motion(Along(box,95),true); Button(_pointer,false); await Frames();
            Check(events==before && b.Quality==40,"shop Hide/reopen does not revive drag");
        }
        finally{_panel.ShopSettingChanged-=Refresh;Button(_pointer,false);}
    }
    static ParkRide Fixture(AssetResourceDatabase.Entry entry, int id, int prize)
    {
        // Entire setup is fixture-owned: bare definition deliberately has no AssetLibrary identity
        // or preview. Real compiled entry drives Value. Public booking/used/satisfaction methods
        // seed 17 winners, 19 customers and a distinct nonzero satisfaction; no private writes.
        var ride = new ParkRide { Id = id, Name = $"SMOKE unplaced sideshow {id}",
            Definition = new RideDefinition { CompiledEntry = entry },
            SideshowPrizeValue = prize, SideshowPrice = 10, SideshowWinPercentage = 60 };
        for (int i = 0; i < 19; i++)
        {
            ride.Used();
            ride.BookSideshowGame(10, prize, i < 17);
            ride.RecordSatisfaction(23);
        }
        return ride;
    }
    void Receipt(ParkRide ride, bool nonzero)
    {
        int?[] positive = { 115,147,179,211,243,275,307,339,371 };
        int?[] zero = { 115,null,147,179,211,243,null,null,275 };
        int[] ids = { 707,637,949,238,899,743,295,832,190 };
        var ys = nonzero ? positive : zero;
        var rows = _panel.RowsDrawn;
        Check(_panel.Open && _panel.Visible && _panel.IsInsideTree() && rows.Count == 9,
            "actual rendered panel has nine canonical draw receipts");
        Check(ride.Winners == 17 && ride.Customers == 19 && ride.Value.HasValue
            && ride.Value.Value != ride.Satisfaction && ride.Satisfaction == 23,
            "nonvacuous explicit fixture counts and distinct bar producers");
        for (int i = 0; i < 9; i++)
        {
            var row = rows[i];
            Check(row.TextId == ids[i], "canonical text id row " + i);
            if (ys[i] is int y)
            {
                Y(row.LabelAt, y, "label " + i);
                Check(Mathf.Abs(Native(row.LabelAt.Value).X - 45) < .05f, "label X45 row " + i);
                Check(Hits("_specRows").TryGetValue(i, out var band)
                    && Mathf.Abs(Native(band.Position).Y - y) < .05f, "drawn band canonical " + i);
            }
            else Check(row.LabelAt == null && !Hits("_specRows").ContainsKey(i), "hidden label/band " + i);
        }
        int[] shared = nonzero ? new[] { 0,1,2,3 } : new[] { 0,2,3 };
        foreach (int i in shared)
        {
            Y(rows[i].ValueAt, ys[i].Value, "shared value " + i);
            Check(Mathf.Abs(Native(rows[i].ValueAt.Value).X-250)<.05f,"shared values literal X250");
            Check(Mathf.Abs(rows[i].ValueAt.Value.X - rows[0].ValueAt.Value.X) < .05f, "shared value column " + i);
        }
        Check(rows[0].Text == ride.Customers.ToString() && rows[1].CellText == "17"
            && rows[2].Text == Money.Display(ride.Takings) && rows[3].Text == Money.Display(ride.Profit),
            "Viewer-bound shared text values including nonzero hidden Winners");
        Check(nonzero ? rows[1].ValueAt != null : rows[1].ValueAt == null, "Winners value draw visibility independent of count");
        BoxY(rows[4].WidgetBox, 246, "upper bar fixed");
        BoxY(rows[5].WidgetBox, 277, "lower bar fixed");
        ExactWidget(rows[4].WidgetBox,215,246,72,22,"upper bar");
        ExactWidget(rows[5].WidgetBox,215,277,72,22,"lower bar");
        Check(rows[4].Fraction == ride.Satisfaction && rows[5].Fraction == ride.Value,
            "Viewer bar DATA crossed: upper=Satisfaction lower=Value");
        Check(rows[6].CellFraction == ride.SideshowWinPercentage, "Viewer-bound chance input data");
        Check(nonzero ? rows[6].Fraction == ride.SideshowWinPercentage : rows[6].Fraction == 0, "actual chance rendering argument, zero when hidden");
        if (nonzero) { BoxY(rows[6].WidgetBox, 308, "chance fixed"); ExactWidget(rows[6].WidgetBox,215,308,72,22,"chance"); }
        else Check(rows[6].WidgetBox == null, "zero prize does not draw chance widget");
        Y(rows[7].ValueAt, 340, "prize digits fixed even when label hidden");
        Y(rows[8].ValueAt, 372, "price digits fixed despite flowing label");
        Check(Mathf.Abs(Native(rows[7].ValueAt.Value).X-275)<.05f && Mathf.Abs(Native(rows[8].ValueAt.Value).X-275)<.05f,"both scalar anchors literal X275");
        Check(rows[7].Text == ride.SideshowPrizeValue.ToString() && rows[8].Text == ride.SideshowPrice.ToString(),
            "prize/price digit values (no money formatting)");
        if (nonzero) BoxY(rows[7].ArrowBox, 353, "prize arrow centre", true);
        else Check(rows[7].ArrowBox == null && rows[7].LabelAt == null && rows[7].Text == "0",
            "unlabelled zero prize digits drawn WITHOUT arrows");
        BoxY(rows[8].ArrowBox, 384, "price arrow centre", true);
        if(nonzero) ExactWidget(rows[7].ArrowBox,200,341,26,24,"prize arrows");
        ExactWidget(rows[8].ArrowBox,200,372,26,24,"price arrows");
        Keys("_specRows", nonzero ? Enumerable.Range(0,9).ToArray() : new[] { 0,2,3,4,5,8 });
        Keys("_sliderRects", nonzero ? new[] { 6 } : Array.Empty<int>());
        Keys("_rowArrows", nonzero ? new[] { 7,8 } : new[] { 8 });
        foreach (var (key, box) in Hits("_sliderRects")) Check(rows[key].WidgetBox == box, "slider hit equals drawn receipt");
        foreach (var (key, box) in Hits("_rowArrows")) Check(rows[key].ArrowBox == box, "arrow hit equals drawn receipt " + key);
    }

    public override async void _Ready()
    {
        _deadline = Time.GetTicksMsec() + 90000;
        int exit = 2;
        try
        {
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            _shotDir = args.LastOrDefault(a => a.StartsWith("--shotdir="))?[10..];
            Check(DisplayServer.GetName() != "headless", "rendering display required, not headless");
            string requested=args.LastOrDefault(a=>a.StartsWith("--map="))?[6..];
            Check(requested != null && new[]{"JUNGLE","FANTASY","HALLOW","SPACE"}.Any(w=>
                    requested.Equals(w,StringComparison.OrdinalIgnoreCase) || requested.StartsWith(w+" ",StringComparison.OrdinalIgnoreCase))
                && args.Contains("--mode=park")
                && (args.Any(a => a.StartsWith("--disc=")) || !string.IsNullOrWhiteSpace(OS.GetEnvironment("TPW_PS2_DISC"))),
                "required normal park map/mode/disc arguments or matrix disc environment");
            Check(!args.Any(a => a.StartsWith("--shot=") || a.Contains("-film=")
                || a.StartsWith("--sound-census=") || a.EndsWith("-test") || a.EndsWith("-audit")
                || a == "--ghost-press") && string.IsNullOrWhiteSpace(OS.GetEnvironment("TPW_PS2_SHOT")),
                "no alternate Viewer audit/film startup");
            _viewer = new Viewer { Name = "Viewer" };
            AddChild(_viewer);
            ulong readyEnd = Time.GetTicksMsec() + 30000;
            while ((Read<LaptopShopScreen>(_viewer, "_shopPanel") == null
                || Read<object>(_viewer, "_staff") == null) && Time.GetTicksMsec() < readyEnd) await Frames();
            _panel = Read<LaptopShopScreen>(_viewer, "_shopPanel");
            Check(_panel != null && Read<object>(_viewer, "_staff") != null, "panel/staff initialized naturally within30s");
            Check(Read<object>(_viewer, "_mode").ToString() == "Park" && Read<Park>(_viewer, "_park") != null,
                "shipping Viewer naturally loaded requested park");
            _panel.CaptureRowDraws = true;
            _panel.RowNudged += (row, by) => _nudges.Add((row, by));
            _panel.SliderMoved += (row, pct) => _slides.Add((row, pct));
            var db = Read<AssetResourceDatabase>(_viewer, "_arsDb");
            var entry = db?.Entries.FirstOrDefault(e => e.Kind == AssetResourceDatabase.AssetKind.Sideshow);
            Check(entry != null, "actual disc DBA compiled Sideshow record");
            var positive = Fixture(entry, 900001, 30);
            var zero = Fixture(entry, 900002, 0);
            var one = Fixture(entry, 900003, 1);
            GD.Print("SIDESHOW PRESENTATION fixtures: unplaced IDs900001/2/3; disc compiled entry; prizes30/0/1, price10, chance60, Winners17, Customers19, Satisfaction23; no preview assets or player-placement claim");
            Bind(positive); await Frames(); Receipt(positive, true); Shot("sideshow-prize-positive");
            await Click(Hits("_rowArrows")[7], true);
            Check(_nudges[^1] == (7,1) && positive.SideshowPrizeValue == 31 && positive.SideshowPrice == 10,
                "native353 arrows invoke logical7 PRIZE not price");
            await Click(Hits("_rowArrows")[8], true);
            Check(_nudges[^1] == (8,1) && positive.SideshowPrizeValue == 31 && positive.SideshowPrice == 11,
                "native384 arrows invoke logical8 PRICE not prize");
            await Click(Hits("_rowArrows")[7], false);
            await Click(Hits("_rowArrows")[8], false);
            Receipt(positive, true);
            await BeginDrag(positive);
            await DragTo(positive, 44);
            await DragTo(positive, 73);
            // Switch between two NONZERO subjects so cancellation cannot pass merely because
            // controls were hidden. The target's chance must remain its original60.
            Bind(one); EmptyBeforeDraw("nonzero subject identity switch");
            Motion(_pointer, true); Button(_pointer, false); await Frames();
            Check(one.SideshowWinPercentage == 60 && positive.SideshowWinPercentage == 73,
                "nonzero subject switch cancels drag independently of control visibility");
            Receipt(one, true);
            await BeginDrag(one, 60);
            Bind(zero); EmptyBeforeDraw("subject switch to zero");
            int zChance = zero.SideshowWinPercentage;
            Motion(_pointer, true); Button(_pointer, false); await Frames();
            Check(zero.SideshowWinPercentage == zChance, "subject switch cannot carry drag into zero fixture");
            Receipt(zero, false); Shot("sideshow-prize-zero");
            // A separate explicit HANDLER fixture, not GUI evidence: supply stale canonical
            // events even though the hidden controls cannot generate them through the panel.
            var zeroBefore=Settings(zero);
            StaleHandlerFixture("OnLaptopSlider",6,99);
            StaleHandlerFixture("OnRowNudge",7,1);
            Check(Settings(zero)==zeroBefore,"stale handler fixture: zero-prize chance/prize writeback gates independently reject events");
            StaleHandlerFixture("OnRowNudge",8,1);
            Check(zero.SideshowPrice==zeroBefore.Price+1 && zero.SideshowPrizeValue==zeroBefore.Prize
                && zero.SideshowWinPercentage==zeroBefore.Chance,"stale handler fixture: canonical8 changes price ONLY");
            StaleHandlerFixture("OnRowNudge",8,-1); await Frames();
            Bind(one); EmptyBeforeDraw("zero->nonzero subject switch"); await Frames(); Receipt(one, true);
            await BeginDrag(one);
            var chanceBox = Hits("_sliderRects")[6];
            var prizeBox = Hits("_rowArrows")[7];
            var priceBox = Hits("_rowArrows")[8];
            int chanceBefore = one.SideshowWinPercentage;
            // Same subject, production arrow changes 1->0, cancelling its live drag and clearing
            // stale geometry SYNCHRONOUSLY, with no frame/redraw between these assertions/events.
            Button(Along(prizeBox, 25), true);
            Check(one.SideshowPrizeValue == 0 && _nudges[^1] == (7,-1), "production one->zero prize transition");
            EmptyBeforeDraw("same subject control hiding");
            Motion(Along(chanceBox, 91), true);
            Button(_pointer, false);
            int n = _nudges.Count, s = _slides.Count;
            Button(Along(prizeBox, 75), true); Button(_pointer, false);
            Button(Along(chanceBox, 91), true); Button(_pointer, false);
            Check(one.SideshowPrizeValue == 0 && one.SideshowWinPercentage == chanceBefore
                && _nudges.Count == n && _slides.Count == s, "hidden controls reject stale coordinates BEFORE queued redraw");
            await Frames(); Receipt(one, false);
            await Click(prizeBox, true);
            await Click(chanceBox, true);
            Check(one.SideshowPrizeValue == 0 && one.SideshowWinPercentage == chanceBefore,
                "hidden chance/prize cannot change fields AFTER draw");
            await Click(priceBox, true);
            Check(one.SideshowPrice == 11 && _nudges[^1] == (8,1) && one.SideshowPrizeValue==0
                && one.SideshowWinPercentage==chanceBefore, "zero prize still permits fixed price arrow canonical8 ONLY");
            // Explicit fixture-owned setting transition (NOT a player action or Viewer field write).
            one.SideshowPrizeValue = 1;
            Bind(one); EmptyBeforeDraw("same subject fixture zero->one"); await Frames(); Receipt(one, true);
            await BeginDrag(one);
            _panel.ShowMenu(new[] { "SMOKE other screen fixture" }, 0);
            EmptyBeforeDraw("other screen");
            Motion(_pointer, true); Button(_pointer, false); await Frames();
            Check(one.SideshowWinPercentage == 22, "other screen rejects carried chance drag");
            Bind(one); await Frames(); Receipt(one, true);
            Check(Read<int>(_panel, "_dragSlider") == -1, "return from other screen stays reset");
            await BeginDrag(one, 35);
            _panel.Hide(); EmptyBeforeDraw("Hide");
            Check(!_panel.Open && !_panel.Visible, "Hide closes shipping panel");
            Bind(one); EmptyBeforeDraw("reopen"); await Frames();
            Motion(Along(Hits("_sliderRects")[6], 88), true); Button(_pointer, false); await Frames();
            Check(one.SideshowWinPercentage == 35, "Hide/reopen never resurrects drag");
            Receipt(one, true);
            await ShopContinuityFixture(db);
            _panel.CaptureRowDraws=false;
            Check(_panel.RowsDrawn.Count==0,"capture-off immediately removes stale receipts");
            Bind(one); await Frames();
            Check(_panel.RowsDrawn.Count==0,"capture-off stays empty after rendered refresh");
            GD.Print("SIDESHOW PRESENTATION caveat: explicit unplaced integration fixtures and a standalone shop-panel callback fixture, not player placement or selected-shop routing. Receipts are submitted draw arguments/anchors, not pixelproof.");
            exit = 0;
        }
        catch (Exception e) { GD.PrintErr($"SIDESHOW PRESENTATION SMOKE FAIL checks={_checks}: {e}"); }
        finally
        {
            try
            {
                Button(_pointer, false);
                if (_viewer != null && IsInstanceValid(_viewer))
                {
                    // Explicit TEARDOWN ONLY, after the observations. Reuse PointerTilesSmoke's
                    // audio cleanup: stop normal owners before freeing; Dummy playback retirement
                    // needs real time, not just a count of fast ProcessFrames. No GC workaround.
                    foreach (string name in new[]{"ResetNativeBus","StopMusic"})
                        (typeof(Viewer).GetMethod(name,Private) ?? throw new MissingMethodException(name)).Invoke(_viewer,null);
                    Read<RideSounds>(_viewer,"_sounds")?.Clear();
                    _viewer.QueueFree();
                }
                // Cleanup awaits deliberately independent of the expired test deadline.
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree().CreateTimer(0.1),SceneTreeTimer.SignalName.Timeout);
                if (_viewer != null && IsInstanceValid(_viewer)) throw new InvalidOperationException("fixture Viewer did not finish queued teardown");
                _viewer=null; _panel=null;
            }
            catch (Exception e) { exit = 2; GD.PrintErr("SIDESHOW PRESENTATION SMOKE FAIL cleanup: " + e); }
        }
        if (exit == 0) GD.Print($"SIDESHOW PRESENTATION SMOKE PASS checks={_checks}; explicit unplaced integration fixtures");
        GetTree().Quit(exit);
    }
}
