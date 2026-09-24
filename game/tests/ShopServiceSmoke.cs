using Aps = TPW.PS2.Data.Animation;
using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>One real external ice-cream customer, through the Shops menu and Viewer callbacks.
/// Run with normal Viewer disc/startup arguments, --mode=park --map=JUNGLE and
/// --shop-shot=/absolute/outsideGit/new-name.png (serving, plus -before/-after).
/// Default target is named icecream in each world. --shop-target=coconut (JUNGLE),
/// burger or fries (FANTASY/SPACE) exercises the other coordinate-less cases. Compiled EUR
/// identities/effects are fixed by named profiles, never substituted by a similar product.
/// Requires a rendering display. A missing service body is a deferred failure: handback and
/// captures still run, then exit 2. Captures need visual review, not pixel-level sign-off.</summary>
public partial class ShopServiceSmoke : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo Member(string name) => typeof(Viewer).GetField(name, Hidden) ?? throw new MissingMemberException("Viewer." + name);
    static T Field<T>(Viewer v, string name) => (T)Member(name).GetValue(v);
    static void Set(Viewer v, string name, object value) => Member(name).SetValue(v, value);
    static object Call(Viewer v, string name, params object[] args)
    {
        var method = typeof(Viewer).GetMethod(name, Hidden) ?? throw new MissingMemberException("Viewer." + name);
        var parameters = method.GetParameters();
        if (args.Length < parameters.Length)
            args = args.Concat(parameters.Skip(args.Length).Select(p => p.HasDefaultValue ? p.DefaultValue
                : throw new ArgumentException("Missing required argument for Viewer." + name))).ToArray();
        return method.Invoke(v, args);
    }
    static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        GD.Print("SHOP SMOKE ok: " + label);
    }

    int _failures;
    void Check(bool condition, string flag)
    {
        if (condition) GD.Print("SHOP SMOKE ok: " + flag);
        else { _failures++; GD.PrintErr("SHOP SMOKE ASSERT FAIL: " + flag); }
    }

    // Inspect the actual scene tree after FramePostDraw, not the sim's sprite policy or a
    // stale dictionary entry awaiting QueueFree. A head alone is not a service body.
    static bool Live(Node node) => node != null && IsInstanceValid(node)
        && node.IsInsideTree() && !node.IsQueuedForDeletion();
    static IEnumerable<MeshInstance3D> Meshes(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (!Live(child)) continue;
            if (child is MeshInstance3D mesh) yield return mesh;
            foreach (var nested in Meshes(child)) yield return nested;
        }
    }

    public override async void _Ready()
    {
        try
        {
            Require(DisplayServer.GetName() != "headless", "rendering display, not headless fake initialization");
            var args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            string shot = args.LastOrDefault(a => a.StartsWith("--shop-shot="))?["--shop-shot=".Length..]
                ?? throw new ArgumentException("--shop-shot requires an absolute new PNG outside Git");
            if (!System.IO.Path.IsPathFullyQualified(shot)
                || !string.Equals(System.IO.Path.GetExtension(shot), ".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("absolute .png capture path required");
            shot = System.IO.Path.GetFullPath(shot);
            var output = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(shot));
            if (!output.Exists) throw new ArgumentException("create the output directory before running");
            // Reject symlink ancestry as well: lexical 'outside Git' must not write into assets.
            for (var parent = output; parent != null; parent = parent.Parent)
                if (parent.LinkTarget != null || System.IO.File.Exists(System.IO.Path.Combine(parent.FullName, ".git"))
                    || System.IO.Directory.Exists(System.IO.Path.Combine(parent.FullName, ".git")))
                    throw new ArgumentException("capture output must be outside Git and symlink ancestry");
            string Suffix(string suffix) => System.IO.Path.Combine(output.FullName,
                System.IO.Path.GetFileNameWithoutExtension(shot) + suffix + ".png");
            string beforeShot = Suffix("-before"), afterShot = Suffix("-after");
            string approachShot=Suffix("-approach"), departureShot=Suffix("-departure");
            var shots = new[] { beforeShot, approachShot, shot, departureShot, afterShot };
            Require(shots.All(p => !System.IO.Path.Exists(p) && new System.IO.FileInfo(p).LinkTarget == null),
                "all five capture paths are new");

            string world = args.LastOrDefault(a => a.StartsWith("--map="))?["--map=".Length..]
                ?? OS.GetEnvironment("TPW_PS2_MAP");
            if (string.IsNullOrWhiteSpace(world)) world = "JUNGLE";
            world = world.ToUpperInvariant();
            string targetShop = args.LastOrDefault(a => a.StartsWith("--shop-target="))?["--shop-target=".Length..] ?? "icecream";
            (string Stem, int Product, int H, int T, int Happy, int V, int Price, bool Missing, string Row) profile = (world, targetShop) switch
            {
                ("JUNGLE", "coconut") => (Stem: "/Shops/Coconut/Coconut", Product: 1, H: 0, T: 40, Happy: 5, V: 10, Price: 30, Missing: true, Row: "2*"),
                ("FANTASY", "burger") => ("/Shops/sburger/sBurger", 0, 25, 0, 5, 15, 30, true, "*2"),
                ("FANTASY", "fries") => ("/Shops/fries/Fries", 7, 20, 0, 5, 15, 30, true, "*2"),
                ("SPACE", "burger") => ("/Shops/burger/Burger", 0, 25, 0, 10, 10, 30, true, "2*"),
                ("SPACE", "fries") => ("/Shops/fries/fries", 7, 20, 0, 5, 10, 35, true, "*2"),
                ("JUNGLE", "icecream") => ("/Shops/IceCream/IceCream", 4, 25, 5, 5, 10, 30, false, "2*"),
                ("FANTASY", "icecream") => ("/Shops/icecream/icecream", 4, 25, 5, 5, 10, 30, true, "2*"),
                ("HALLOW", "icecream") => ("/Shops/ices/ices", 4, 25, 5, 5, 10, 30, false, "*2"),
                ("SPACE", "icecream") => ("/Shops/ices/ices", 4, 25, 5, 5, 10, 30, false, "2*"),
                _ => throw new ArgumentException("unsupported named world/shop target; no fuzzy profile fallback"),
            };
            string stem = profile.Stem;
            byte product = (byte)profile.Product;
            bool coordinateLess = profile.Missing, drink = product == 1;
            // Normal _Ready loads the disc/terrain/UI. These are startup defaults, not a fake
            // Viewer setup; CLI still goes through Viewer, and actual archive is checked below.
            var viewer = new Viewer { Name = "Viewer" };
            Set(viewer, "_wantMap", world); Set(viewer, "_wantMode", "park"); Set(viewer, "_guestCap", 0);
            AddChild(viewer); viewer.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var park = Field<Park>(viewer, "_park"); var library = Field<AssetLibrary>(viewer, "_lib");
            var terrain = Field<Model>(viewer, "_terrainModel");
            var entrances = Field<ParkEntrance>(viewer, "_entranceTable");
            Require(park?.Field != null && terrain != null && entrances != null && library != null,
                "normal startup loaded real park, library and entrance");
            Require(string.Equals(System.IO.Path.GetFileNameWithoutExtension(library.WadName), world,
                StringComparison.OrdinalIgnoreCase), "actual archive matches requested world " + world);
            Require(park.Placed.Count == 0, "empty fixture before construction");
            var entry = entrances.Fit(terrain.Field, ParkEntrance.WalkwayColumnFromPoles(terrain), out _);
            Require(!entry.Empty, "authored entrance fits");
            var corridor = new List<(int X, int Y)>();
            for (int z = entry.ZEnd; z < entry.ZEnd + 8; z++) corridor.Add((entry.XCol, z));
            Call(viewer, "LayLeg", corridor, PathTool.Kind.Path, 0); Call(viewer, "RefreshFloor");
            Require((bool)Call(viewer, "OpenGate"), "normal guest layer opened");
            var grid = Field<GuestWalk>(viewer, "_guests").Paths;
            Call(viewer, "ToggleBuildMenu"); Call(viewer, "ShowBuildCategory", "Shops");
            var rows = Field<List<int>>(viewer, "_buildRows");
            var matches = Enumerable.Range(0, rows.Count).Where(candidateRow =>
            {
                var model = library.Rides[rows[candidateRow]].Model;
                var d = (RideDefinition)Call(viewer, "DefinitionFor", model);
                return d?.Compiled?.Product == product
                    && string.Equals(System.IO.Path.ChangeExtension(model.Path, null), stem, StringComparison.OrdinalIgnoreCase)
                    && d.Source.EndsWith(stem + ".sam", StringComparison.OrdinalIgnoreCase);
            }).ToArray();
            Require(matches.Length == 1, "Shops menu has exactly one precise named compiled model " + stem);
            int row = matches[0]; var asset = library.Rides[rows[row]];
            var definition = (RideDefinition)Call(viewer, "DefinitionFor", asset.Model);
            Require(definition.Sells && definition.HungerEffect == profile.H && definition.ThirstEffect == profile.T
                && definition.HappinessEffect == profile.Happy && definition.VomitEffect == profile.V && definition.PricePerUse == profile.Price,
                "independent named EUR compiled need amounts, happiness/vomit/price (unsupported profiles fail explicitly)");
            Require(definition.Shape != null, "authored shape exists");
            var footprint = Park.Footprint.From(definition.Shape);
            if (coordinateLess)
            {
                Require(footprint.Width == 2 && footprint.Height == 2
                    && definition.Shape.Select(s => s.Trim()).SequenceEqual(new[] { "**", profile.Row })
                    && !definition.Fields.ContainsKey("UsageInfo.EntryCellStandPosX")
                    && !definition.Fields.ContainsKey("UsageInfo.EntryCellStandPosY"),
                    "named asset is the real coordinate-less 2x2 shop, not a mutated definition");
            }
            else
            {
                var expectedStand = world switch { "HALLOW" => new Vector2(.4f,.5f), "SPACE" => new Vector2(.7f,.7f), _ => new Vector2(.6f,.4f) };
                string entryRow = world == "HALLOW" ? "*2" : "2*";
                Require(footprint.Width == 2 && footprint.Height == 2
                    && definition.Shape.Select(s => s.Trim()).SequenceEqual(new[] { "**", entryRow })
                    && definition.EntryStandX is float sx && Mathf.IsEqualApprox(sx, expectedStand.X)
                    && definition.EntryStandY is float sy && Mathf.IsEqualApprox(sy, expectedStand.Y),
                    "named authored 2x2 entry and per-world stand, not a universal shop default");
            }
            Require(asset.Script != null, "named model has real RSE");
            var program = new RseProgram(library.Read(asset.Script));
            Require(!program.Instructions.Any(i => i.Opcode is RseOpcode.LIMBO or RseOpcode.WALKON),
                "named external shop RSE has no LIMBO or WALKON");
            GD.Print($"SHOP SMOKE fixture world={world} model={asset.Model.Path} definition={definition.Source} script={asset.Script.Path}");

            var blueprint = Field<Placement>(viewer, "_place");
            string turnArg = args.LastOrDefault(a => a.StartsWith("--shop-turn="))?["--shop-turn=".Length..];
            int? requestedTurn = turnArg == null ? null : int.Parse(turnArg);
            Require(requestedTurn == null || requestedTurn is >= 0 and <= 3, "requested turn is in 0..3");
            bool placed = false; int placedTurn = -1;
            for (int turn = 0; turn < 4 && !placed; turn++)
            {
                if (requestedTurn is int requested && turn != requested) continue;
                Call(viewer, "ArmFromList", row); blueprint.Turn(turn);
                for (int z = entry.ZEnd; z < entry.ZEnd + 8 && !placed; z++)
                    for (int x = entry.XCol - 4; x <= entry.XCol + 4 && !placed; x++)
                    {
                        if (!blueprint.Fits(park, x, z)) continue;
                        var stubs = blueprint.Stubs(park, x, z).Where(s => s.Entrance).ToArray();
                        if (stubs.Length != 1 || !ParkPaths.Neighbours(new(stubs[0].X, stubs[0].Y))
                            .Any(c => grid.Open(c) && corridor.Contains((c.X, c.Z)))) continue;
                        int count = park.Placed.Count;
                        Set(viewer, "_cursorOverride", (x, z));
                        try { Call(viewer, "PlaceHeld"); }
                        finally { Set(viewer, "_cursorOverride", null); }
                        placed = park.Placed.Count == count + 1;
                        if (placed) placedTurn = turn;
                    }
            }
            Require(placed && park.Placed.Count == 1, "actual Shops PlaceHeld constructed one shop beside laid corridor");
            Call(viewer, "CloseTool"); Call(viewer, "TickPark");
            var visitors = Field<ParkVisitors>(viewer, "_visitors");
            var sim = Field<ParkSim>(viewer, "_sim"); var ride = sim.Rides.Single();
            Require(ReferenceEquals(ride.Definition, definition) && ride.Machine != null && ride.Host != null
                && ride.Entrance != null && ride.Fault == null, "placed shop owns its real definition, script and entrance");
            Require(visitors.Plans.Count == 0 && visitors.Walk.Guests.Count == 0, "automatic arrivals disabled");
            foreach (string key in visitors.Needs.Rates.Keys.ToArray()) visitors.Needs.Rates[key] = new VisitorNeeds.Rate(0, 0, false);
            visitors.Needs.SecondsPerRise = 1_000_000;
            var mouth = Field<List<ParkCell>>(viewer, "_mouth")[0];
            // Start on the laid corridor beside the entrance, for the same-camera BEFORE control.
            // Arrival supplies identity only; hunger chooses/routes to the actual shop in TickPark.
            var stub = ride.Entrance.Value;
            var start = ParkPaths.Neighbours(stub).First(c => grid.Open(c) && corridor.Contains((c.X, c.Z)));
            var guest = visitors.Arrive(start, mouth); int guestId = guest.Id;
            var initial = new VisitorWants { Hunger = (byte)(drink ? 0 : 91), Thirst = (byte)(drink ? 91 : 0), Toilet = 10, Happiness = 50,
                Sick = 0, Litter = 9, Cash = 1234, PreferredIntensity = 90, Unknown78 = 0, Boredom = 0 };
            visitors.Needs.Set(guestId, initial);
            void Present() { Call(viewer, "PresentScripted", true, 1f); Call(viewer, "PlaceActors", 1f); }
            void Tick() { Call(viewer, "TickPark"); Present(); }

            // Independent placement-derived inside cell; do not use the production compiled
            // transform as its own oracle. Both must agree before interpreting any picture.
            var placementOracle=new Placement(); placementOracle.Arm(definition,targetShop,0,footprint); placementOracle.Turn(placedTurn);
            var entryCell=ride.Origin.Offset(placementOracle.Turned.EntryX,placementOracle.Turned.EntryY);
            Require(ride.ServiceEntry==entryCell && entryCell!=stub,"compiled entrance agrees with independent placed entry, not outside stub");
            var corner=park.CellCorner(entryCell.X,entryCell.Z);
            var standWorld=corner+.5f*(park.CellCorner(entryCell.X+1,entryCell.Z)-corner)
                +.5f*(park.CellCorner(entryCell.X,entryCell.Z+1)-corner);
            standWorld.Y=park.CellY(entryCell.X,entryCell.Z);
            var stubWorld=(Vector3)Call(viewer,"GuestWorld",new Vector3(stub.X+.5f,0,stub.Z+.5f),stub);
            var camera=Field<Camera3D>(viewer,"_cam");
            var target=standWorld+Vector3.Up*.35f;
            var front=stubWorld-standWorld;front.Y=0;
            Require(front.LengthSquared()>1e-6f,"real one-cell approach determines camera side");
            string cameraView = args.LastOrDefault(a => a.StartsWith("--shop-camera="))?["--shop-camera=".Length..] ?? "front";
            Vector3 cameraOffset = cameraView switch
            {
                "front" => front.Normalized() * 4.5f + Vector3.Up * 2.3f,
                "high" => front.Normalized() * 5f + Vector3.Up * 8f,
                "left" => front.Normalized().Rotated(Vector3.Up, -.8f) * 5f + Vector3.Up * 3.5f,
                "right" => front.Normalized().Rotated(Vector3.Up, .8f) * 5f + Vector3.Up * 3.5f,
                _ => throw new ArgumentException("--shop-camera must be front, high, left or right"),
            };
            camera.GlobalPosition = target + cameraOffset;
            camera.LookAt(target); camera.MakeCurrent();
            GD.Print($"SHOP SMOKE camera={cameraView} eye={camera.GlobalPosition} target={target}; diagnostic viewpoint, not default gameplay camera");
            Field<Control>(viewer, "_panel").Visible = false;
            if (args.Contains("--shop-markers"))
            {
                // Optional diagnostic landmark: the actual placed node's origin, NOT a guest-
                // derived position. The magenta dot is an overlay, not part of the disc artwork.
                var modelOrigin = park.Placed.Single().Node.GlobalPosition;
                var marker = new MeshInstance3D { Mesh = new SphereMesh { Radius = .045f, Height = .09f },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.Magenta,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true } };
                AddChild(marker); marker.GlobalPosition = modelOrigin;
                var label = new Label3D { Text = "placed shop origin", FontSize = 24, PixelSize = .003f,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, Modulate = Colors.Magenta };
                AddChild(label); label.GlobalPosition = modelOrigin + Vector3.Up * .3f;
                GD.Print($"SHOP SMOKE MARKER actualPlacedModelOrigin={modelOrigin}; magenta overlay is diagnostic, not gameplay art");
            }
            var fixedCamera = camera.GlobalTransform;
            Require(!camera.IsPositionBehind(target) && GetViewport().GetVisibleRect().HasPoint(camera.UnprojectPosition(target)),
                "camera frames actual shop service point");
            GD.Print($"SHOP SMOKE placement origin={ride.Origin} turn={placedTurn} entry={stub} standWorld={standWorld} start={start}");

            async System.Threading.Tasks.Task Capture(string path)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                // CreateNew also guards against a file appearing after initial validation.
                using var file = new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write);
                var png = image.SavePngToBuffer(); Require(png.Length > 0, "rendered PNG buffer");
                file.Write(png);
                Require(camera.GlobalTransform == fixedCamera, "same camera for " + System.IO.Path.GetFileName(path));
                GD.Print("SHOP SMOKE capture=" + path);
                var physical=visitors.Walk.Guests.FirstOrDefault(g=>g.Id==guestId) ?? guest;
                var point=physical.Position;
                var physicalWorld=(Vector3)Call(viewer,"GuestWorld",new Vector3(point.X,point.Y,point.Z),physical.Cell);
                var actors=Field<Dictionary<int,Node3D>>(viewer,"_actors");
                var actorWorld=actors.TryGetValue(guestId,out var actor) && Live(actor) ? actor.GlobalPosition : new Vector3(float.NaN,float.NaN,float.NaN);
                GD.Print($"SHOP SMOKE POSITION frame={System.IO.Path.GetFileName(path)} cell={physical.Cell} next={physical.Next} progress={physical.Progress} "
                    + $"physicalWorld={physicalWorld} actorWorld={actorWorld} compiledEntryWorld={standWorld} "
                    + $"actorMinusPhysical={actorWorld-physicalWorld} actorMinusEntry={actorWorld-standWorld}");
            }
            int Bodies(string phase)
            {
                var root = Field<Node3D>(viewer, "_guestRoot");
                var actors = Field<Dictionary<int, Node3D>>(viewer, "_actors");
                var live = root.GetChildren().OfType<Node3D>().Where(n => Live(n) && n.IsVisibleInTree()).ToArray();
                var own = live.Where(n => n.Name == $"Guest_{guestId}").ToArray();
                bool HasPart(Node3D n, string part) => Meshes(n).Any(m => m.Mesh != null && m.IsVisibleInTree()
                    && (m.Layers & camera.CullMask) != 0
                    && ((string)m.Name).Contains(part, StringComparison.OrdinalIgnoreCase));
                bool HasBody(Node3D n) => HasPart(n, "body") && HasPart(n, "legs");
                int bodies = own.Count(HasBody);
                bool projected = own.Any(n => !camera.IsPositionBehind(n.GlobalPosition + Vector3.Up * .2f)
                    && GetViewport().GetVisibleRect().HasPoint(camera.UnprojectPosition(n.GlobalPosition + Vector3.Up * .2f)));
                GD.Print($"SHOP SMOKE {phase} guest={guestId} liveActors={live.Length} ownActors={own.Length} "
                    + $"visibleBodies={bodies} totalBodies={live.Count(HasBody)} projected={projected} "
                    + $"dictionaryLive={actors.TryGetValue(guestId, out var a) && Live(a)} walkers={visitors.Walk.Guests.Count}");
                Check(live.Length == own.Length, phase + " no unrelated guest actors");
                if (bodies > 0)
                {
                    Check(projected, phase + " body projects into capture");
                    Check(own.Length == 1, phase + " exactly one live actor for the original identity");
                }
                return bodies;
            }

            Present(); await Capture(beforeShot);
            Check(Bodies("BEFORE") == 1 && visitors.Walk.Guests.Count(g => g.Id == guestId) == 1,
                "BEFORE_ONE_WALKING_BODY");
            var before = visitors.Needs.Of(guestId);
            Check((before with { Thought = initial.Thought }).Equals(initial) && before.Thought == (drink ? Thought.Thirsty : Thought.Hungry),
                "BEFORE_SEED_PRESERVED_WITH_PRESENTED_NEED_THOUGHT");
            bool Accepted() => ReferenceEquals(visitors.QueuedOwner(guestId), ride)
                && !ride.Queue.Contains(guestId) && ride.Get("VAR_LETMEON") != guestId;
            bool serving = false, witnessedApproach = false;
            Vector3[] earlyLegVertices=null;
            bool animatedLegs=false;
            for (int tick = 0; tick < 6000 && !serving && visitors.Rides == 0; tick++)
            {
                Tick(); serving = Accepted();
                if (guest.Cell==stub && guest.Next==entryCell && guest.Progress>0)
                {
                    witnessedApproach=true;
                    Check(visitors.Boardings==0 && visitors.Walk.Guests.Contains(guest),"APPROACH_REMAINS_WALKING_NOT_SERVICE");
                    var actor=Field<Dictionary<int,Node3D>>(viewer,"_actors").GetValueOrDefault(guestId);
                    var p=guest.Position;
                    var expected=(Vector3)Call(viewer,"GuestWorld",new Vector3(p.X,p.Y,p.Z),guest.Cell);
                    Check(Live(actor) && actor.Position.DistanceTo(expected)<.002f,"LIVE_BODY_FOLLOWS_REAL_APPROACH_PROGRESS");
                    var drawn=Field<Dictionary<int,(AnimatedModel Drawn,Aps.Record Sit,string Where)>>(viewer,"_drawn")[guestId].Drawn;
                    var expectedWalk=Field<Dictionary<int,(Aps Anim,Aps.Record Walk,Aps.Record Idle,Model Model)>>(viewer,"_walkRec")[guestId].Walk;
                    Check(expectedWalk!=null && ReferenceEquals(drawn.Record,expectedWalk) && drawn.Skeletal && drawn.Frames>0,
                        "LIVE_APPROACH_SAMPLES_ACTUAL_WALK_ANIMATION");
                    var legVertices=Meshes(actor).Where(m=>((string)m.Name).Contains("legs",StringComparison.OrdinalIgnoreCase))
                        .SelectMany(m=>m.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array()).ToArray();
                    if (guest.Progress==40) earlyLegVertices=legVertices;
                    if (guest.Progress==480)
                    {
                        animatedLegs=earlyLegVertices?.Length>0 && legVertices.Length==earlyLegVertices.Length
                            && !earlyLegVertices.SequenceEqual(legVertices);
                        await Capture(approachShot);
                    }
                }
                if (tick % 8 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Require(witnessedApproach,"actual last-cell walking leg was observed before boarding");
            Check(animatedLegs,"APPROACH_LEG_MESH_ACTUALLY_ANIMATES_NOT_BIND_POSE_TRANSLATION");
            Require(serving, "accepted actual owner is named shop, not queue or VAR_LETMEON offer");
            bool hidden = ride.Host.Visibility.TryGetValue(guestId, out var visibility) && !visibility.Visible;
            bool seat = ride.Host.Seats.Values.Contains(guestId), walk = ride.Host.Walkers.ContainsKey(guestId);
            bool seatedPose = ((System.Collections.IDictionary)Member("_seated").GetValue(viewer)).Contains(guestId);
            bool walkingPose = ((System.Collections.IDictionary)Member("_walking").GetValue(viewer)).Contains(guestId);
            var standing = Field<Dictionary<int, Transform3D>>(viewer, "_standing");
            GD.Print($"SHOP SMOKE SERVING accepted={Accepted()} explicitHostHide={hidden} hostSeat={seat} hostWalk={walk} "
                + $"seatPose={seatedPose} walkPose={walkingPose} standingPose={standing.ContainsKey(guestId)} purchases={visitors.Purchases}");
            Check(!hidden && !seat && !walk && !seatedPose && !walkingPose, "EXTERNAL_SERVICE_NO_HIDE_SEAT_OR_WALK");
            Check(visitors.Purchases == 0 && visitors.Rides == 0 && !visitors.Walk.Guests.Any(g => g.Id == guestId),
                "ACCEPTED_BEFORE_PURCHASE_OFF_WALK");
            await Capture(shot);
            // Deliberately NOT Require: this is the current discriminating rendering failure.
            Check(Bodies("SERVING") == 1, "MISSING_VISIBLE_SERVICE_BODY (accepted external customer must have exactly one full body)");
            var actorsAtService=Field<Dictionary<int,Node3D>>(viewer,"_actors");
            var inward=((Vector3)Call(viewer,"GuestHeading",new Vector3(-placementOracle.Turned.EntryDX,0,-placementOracle.Turned.EntryDY))).Normalized();
            Check(standing.ContainsKey(guestId) && actorsAtService.TryGetValue(guestId,out var held)
                && Live(held) && guest.Cell==entryCell && held.Position.DistanceTo(standWorld)<.002f
                && held.Basis.Z.DistanceTo(inward)<.0001f && held.Basis.Y.DistanceTo(Vector3.Up)<.0001f,
                "LIVE_SERVICE_BODY_RETAINS_PHYSICALLY_REACHED_COMPILED_ENTRY");
            for (int tick = 0; tick < 6000 && visitors.Rides == 0; tick++)
            {
                Tick(); if (tick % 8 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            bool autoDepart=args.Contains("--shop-auto-depart");
            if (!autoDepart) sim.SetOpen(ride.Id, false);
            Present();
            var returned=visitors.Walk.Guests.Single(g=>g.Id==guestId);
            Check(returned.Cell==entryCell && returned.Next==null,"HANDBACK_RETAINS_ENTRY_NO_STUB_TELEPORT");
            if (autoDepart)
            {
                long completedClock=sim.Time;
                for(int i=0;i<120 && returned.Next==null;i++)Tick();
                Check(sim.Time-completedClock<120*ParkSim.TickMilliseconds && returned.Next==stub && returned.Progress>0,
                    "AUTOMATIC_IDLE_ARM_LEAVES_OPEN_SHOP_BEFORE_RESELECTION_DEADLINE");
                Require(returned.Next==stub,"automatic departure really traverses the doorway without an explicit Send");
            }
            else
            {
                Require(visitors.Walk.Send(returned,start),"explicit departure has a public route through the same doorway");
                Tick();
            }
            Check(returned.Next==stub && returned.Progress>0,"DEPARTURE_WALKS_REVERSE_EDGE");
            for(int i=0;i<11;i++)Tick();
            await Capture(departureShot);
            for(int i=0;i<38;i++)Tick();
            var after = visitors.Needs.Of(guestId);
            GD.Print($"SHOP SMOKE AFTER guest={guestId} H={after.Hunger} T={after.Thirst} toilet={after.Toilet} "
                + $"sick={after.Sick} happy={after.Happiness} cash={after.Cash} litter={after.Litter} pref={after.PreferredIntensity} "
                + $"u78={after.Unknown78} u7B={after.Boredom} purchases={visitors.Purchases} rides={visitors.Rides} boardings={visitors.Boardings}");
            Check(visitors.Purchases == 1 && visitors.Rides == 1 && visitors.Boardings == 1 && visitors.Relieved == 0,
                "ONE_REAL_PURCHASE_HANDBACK");
            Check(after.Hunger == (drink ? profile.H : 91 - profile.H) && after.Thirst == (drink ? 91 - profile.T : profile.T)
                && after.Toilet == 10 + (drink ? profile.T : profile.H) && after.Sick == profile.V
                && after.Happiness == 50 + profile.Happy && after.Cash == 1234 - 10 * profile.Price && after.Litter is >= 39 and <= 63,
                "NAMED_PURCHASE_EFFECTS_AND_TENFOLD_COMPILED_PRICE_DEBIT");
            Check(after.PreferredIntensity == initial.PreferredIntensity && after.Unknown78 == initial.Unknown78
                && after.Boredom == initial.Boredom, "UNAFFECTED_VALUES_PRESERVED (thought is recomputed, not frozen)");
            Check(guest.Id == guestId && visitors.Plans.Count == 1 && visitors.Plans.ContainsKey(guestId)
                && visitors.Walk.Guests.Count == 1 && visitors.Walk.Guests.Single().Id == guestId
                && visitors.QueuedOwner(guestId) == null && !standing.ContainsKey(guestId), "ORIGINAL_ID_ONE_WALKER_NO_SERVICE_OWNER");
            Check(ride.Fault == null, "REAL_SCRIPT_NO_FAULT");
            await Capture(afterShot);
            Check(Bodies("AFTER") == 1, "AFTER_ONE_VISIBLE_WALKING_BODY");
            viewer.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"SHOP SMOKE {(_failures == 0 ? "PASS" : "FAIL")} failures={_failures} (rendered captures require visual review)");
            GetTree().Quit(_failures == 0 ? 0 : 2);
        }
        catch (Exception ex) { GD.PrintErr("SHOP SMOKE FAIL: " + ex); GetTree().Quit(2); }
    }
}
