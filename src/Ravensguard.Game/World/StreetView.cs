using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.Debug;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.Tools.BlueprintEditor;
using Ravensguard.Game.UI;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.World;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.World;

/// <summary>
/// The first playable street view (plan 2.7, 2.8). The sim (<see cref="LiveWorld"/>) owns every bit of state; this node
/// reads its snapshot each frame and draws: terrain and fixtures as TileMapLayers from the baked atlases, scenery and the hero as
/// y-sorted sprites, and the light grid as a multiplicative overlay. Buildings are roofless; the level below shows through openings one shade darker.
/// Zoom is whole-number only: 1x is Street (32 px/m), 2x is Close (64 px/m).
/// </summary>
public partial class StreetView : Control, IHandlesBack
{
    const int Tile = 32;
    const double Step = LiveWorld.TickSeconds;
    const int LightPatchW = 128, LightPatchH = 96;
    int _lightW, _lightH;

    readonly ScreenRouter? _router;
    readonly StreetOptions _opt;
    StreetSource? _source;
    LiveWorld _world = null!;
    HamletMap Map => _world.Map;
    ArtAtlas _art = null!;
    readonly Calendar _calendar = new();

    /// <summary>Zoom bands (REDESIGN_PLAN 2.7): Close 64 px/m (2x pixels), Street 32 px/m, Field 8 px/m, Command 2 px/m.</summary>
    public enum Band { Close, Street, Field, Command }

    // view state (camera only; nothing here is game state)
    int _zoom = 1, _viewZ;
    Band _band = Band.Street;
    Vector2 _pan;
    double _acc;
    long _shownTick = -1;
    SimSpeed _speedBeforePause = SimSpeed.X1;

    // scene
    SubViewportContainer _box = null!;
    SubViewport _vp = null!;
    Camera2D _cam = null!;
    TileMapLayer _belowT = null!, _belowO = null!, _baseG = null!, _baseT = null!, _baseO = null!, _decal = null!;
    Node2D _ysort = null!, _street = null!;
    Seasonal _seasons = null!;
    ShaderMaterial _matProp = null!;
    WeatherFeed _wfeed = null!;
    WeatherOverlay _wover = null!;
    WeatherView _wx = WeatherView.Calm(10);
    float _wxDim = 1f;
    FarLayer? _far;
    CaravanSprites? _caravanSprites;
    readonly CaravanBridge _caravanBridge = new();
    // the LODBridge for people (plan 2.3): townsfolk of the abstract records, promoted into the bubble around the hero and drawn as rig figures
    readonly PeopleBridge _lod = new();
    PeopleSprites? _lodSprites;
    ClothingFeed? _clothes;
    System.Threading.Tasks.Task<ScanResult>? _lodScan;
    long _lodScanTick; bool _lodFaultShown;
    // odd jobs (live-behaviour spec 4.5): the table belongs to the tick worker; the view holds copies, hands claims over at a barrier and shows the work panel
    System.Threading.Tasks.Task<JobSnapshot>? _jobTask;
    JobSnapshot? _jobSnap;
    long _jobSec = long.MinValue; bool _jobFaultShown;
    RegionMap? _regionMap;
    ColorRect _ambient = null!, _ground = null!;
    ShaderMaterial _ambientMat = null!;
    readonly List<FarPerson> _people = new();
    RigAtlas _rig = null!;
    RigActor _heroSprite = null!;
    string _heroLookKey = "";
    Sprite2D _torchSprite = null!, _lightSprite = null!;
    ImageTexture _lightTex = null!;
    byte[] _lightBytes = null!;
    readonly Dictionary<string, (int Src, Vector2I Cell)> _terrainCells = new(), _objectCells = new(), _decalCells = new();

    // hud
    // the player character (hero record, plan 3 item 1); the Codex and the explored map are the sim's (LiveWorld.Codex, LiveWorld.Explored)
    Ravensguard.Sim.Characters.HeroRecord? _hero;
    Label _dateLabel = null!, _phaseLabel = null!, _levelLabel = null!, _hintLabel = null!, _perfLabel = null!, _weatherLabel = null!, _bandLabel = null!;
    TextureRect _weatherIcon = null!;
    Label _condLabel = null!;
    Minimap? _minimap;
    readonly List<Label> _levelBoxes = new();

    // timing / shot
    readonly Stopwatch _sw = new();
    double _tickMsSum, _tickMsMax, _drawMsSum, _drawMsMax, _drawMsMaxWarm; long _tickCount, _drawCount;
    int _frames; double _benchT; bool _quitting; double _stepMsFrame;
    static readonly byte[] Tone = BuildTone();

    /// <summary>The run being played (null for the dev hamlet and seed starts): the pause menu saves through it.</summary>
    public RunSession? Run { get; set; }
    Ravensguard.Sim.Diagnostics.SessionRecorder _rec = null!;

    /// <summary>Where the tester is, for an F8 report: tick, game time, speed, hero cell and view level.</summary>
    System.Collections.Generic.Dictionary<string, string> WhereForReport() => new()
    {
        ["screen"] = "street", ["tick"] = _world.Tick.ToString(), ["game_time"] = _world.Clock.Now.ToString(), ["speed"] = _world.Clock.Speed.ToString(),
        ["hero_cell"] = $"{_world.OriginX + _world.Snapshot.Hero.CellX},{_world.OriginY + _world.Snapshot.Hero.CellY},{_world.Snapshot.Hero.Z}", ["view_level"] = _viewZ.ToString(),
    };
    /// <summary>The abstract world ticking beside the live one (null for the hamlet): every region's economy, advanced one game hour at a time on its own worker.</summary>
    public WorldRuntime? Runtime => _source?.Runtime;
    // frame cost of the abstract tick on this thread, and whether its failure was shown
    double _pumpMsSum, _pumpMsMax; long _pumpCount; bool _runtimeFaultShown;
    System.Runtime.GCLatencyMode _gcMode = System.Runtime.GCLatencyMode.Interactive;
    /// <summary>Called once when this view leaves the tree (the owner clears what it kept for the hand-off).</summary>
    public Action? OnExit { get; set; }

    public StreetView(ScreenRouter? router, StreetOptions? opt = null, StreetSource? source = null) { _router = router; _opt = opt ?? new StreetOptions(); _source = source; }

    /// <summary>
    /// The street view over a generated region (plan 2.2, 2.3): chunks generate on first visit around the hero and stream as they walk.
    /// <paramref name="spawn"/> is in world cells (0..31999); null picks a paved cell in the largest town's square.
    /// Pass the save's <paramref name="store"/> to resume (visited chunks come back from snapshot plus deltas).
    /// </summary>
    public static StreetView ForRegion(Region region, Population population, ContentDatabase content, ScreenRouter? router = null, StreetOptions? opt = null,
        ChunkStore? store = null, (int X, int Y, int Z)? spawn = null, Ravensguard.Sim.Characters.HeroRecord? hero = null, Ravensguard.Sim.Codex.CodexBook? codex = null,
        Ravensguard.Sim.WorldGen.History.WorldHistory? history = null, Continent? continent = null, Ravensguard.Sim.Live.LiveResume? resume = null, RegionSettings? regionSettings = null, WorldRuntime? runtime = null)
        => new(router, opt, StreetSource.ForRegion(region, population, content, store, spawn is { } p ? new Cell3(p.X, p.Y, p.Z) : null, opt?.Poi, hero, codex, history, continent, resume, regionSettings, runtime));

    // ------------------------------------------------------------------ setup

    public override void _Ready()
    {
        _art = ArtAtlas.Shared;
        _rig = RigAtlas.Shared;
        _source ??= _opt.RegionSeed is { } seed ? StreetSource.FromSeed(seed, _opt.Poi) : StreetSource.Hamlet();
        _hero = _source.Hero;
        if (_opt.CaravanShot) PrepareCaravanShot();
        var season = _opt.Season switch { "summer" => Season.Summer, "autumn" => Season.Autumn, "winter" => Season.Winter, _ => Season.Spring };
        // a run opens when history ended (the abstract tick's clock starts there); dev flags that name a date or hour pick it within that year
        var open = _source.StartTime;
        bool dated = _opt.Day != null || _opt.Hour != null || _opt.Season != null;
        var t0 = open is { } ot && !dated ? ot : GameTime.FromCalendar(open?.Year ?? 1, season, _opt.Day ?? 8, _opt.Hour ?? 9, 0);
        _world = _source.CreateWorld(_opt.Start is { } s ? new Cell3(s.X, s.Y, s.Z) : null, t0);
        StartRuntime();
        if (_opt.Speed is 1 or 2 or 4 or 8) _world.SetSpeed((SimSpeed)_opt.Speed);
        var start = _world.Snapshot.Hero.Cell;
        DevContext.Clock = _world.Clock;
        if (_source.Resume == null && (_opt.Torch ?? IsNight(t0))) _world.SetTorch(true); // a resumed run keeps the torch it was saved with
        if (_world.Clock.Speed == SimSpeed.Paused) _world.SetSpeed(SimSpeed.X1); // saved from the pause menu: play on
        _rec = new Ravensguard.Sim.Diagnostics.SessionRecorder(_world); // player commands and record hashes for deterministic replay of a bug report (testers spec 3.6)
        Diagnostics.GameDiagnostics.Recorder = _rec;
        Diagnostics.GameDiagnostics.NoteSessionStart(Run?.LastPath);
        Diagnostics.GameDiagnostics.WhereProvider = WhereForReport;
        _zoom = _opt.Zoom;
        _band = _opt.Band switch { "close" => Band.Close, "field" => Band.Field, "command" => Band.Command, "street" => Band.Street, _ => _opt.Zoom >= 2 ? Band.Close : Band.Street };
        _viewZ = _opt.ViewZ ?? start.Z;
        if (_opt.Pan is { } p) _pan = new Vector2(p.X, p.Y) * Tile;

        _seasons = new Seasonal(_art);
        _wover = new WeatherOverlay(_art);
        _wfeed = new WeatherFeed(_source.Region, _source.Weather, WeatherView.Parse(_opt.Weather));
        if (_source.Region is { } rg)
        {
            _regionMap = new RegionMap(rg);
            _regionMap.Bind(_world.Explored);
        }
        BuildScene();
        BuildHud();
        AttachRide();
        if (Runtime is { } wrt) _world.AttachWounds(wrt.State.Wounds ??= new Ravensguard.Sim.Combat.Wounds.WoundLedger(wrt.State.Seed, wrt.State.Clock.Now.Seconds));   // bleeding runs at 20 Hz in the live tier too (Phase 4 P4-B)
        AttachTravel();
        Resized += FitViewport;
        GetWindow().SizeChanged += FitViewport;
        FitViewport();
        RebuildLevel(now: true); // the first picture is complete; later level switches fill in over a few frames
        _rec.Step();
        if (_opt.BenchSeconds > 0) _perfLabel.Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
        EditorLibrary.ContentReloaded += OnBlueprintsReloaded;
        GD.Print($"[street] ready ({_source.Name}): {Map.Props.Count} props, {Map.Buildings.Count} buildings, {_world.Grid.CellCount} cells, level {_viewZ}, hero at world ({_world.OriginX + start.X},{_world.OriginY + start.Y})");
    }

    public override void _ExitTree()
    {
        // the run's own save (if any) was already queued behind whatever the worker was doing; leaving waits for it, a moment at most
        try { Runtime?.Dispose(); } catch (Exception ex) { GD.PushWarning("runtime shutdown: " + ex.Message); }
        if (Runtime != null) { try { System.Runtime.GCSettings.LatencyMode = _gcMode; } catch (InvalidOperationException) { } }
        ActiveContexts.Exit(InputContext.Choice);
        if (GetWindow() != null) GetWindow().SizeChanged -= FitViewport;
        if (DevContext.Clock == _world?.Clock) DevContext.Clock = null;
        if (Diagnostics.GameDiagnostics.Recorder == _rec) { Diagnostics.GameDiagnostics.Recorder = null; Diagnostics.GameDiagnostics.WhereProvider = null; }
        EditorLibrary.ContentReloaded -= OnBlueprintsReloaded;
        var exit = OnExit; OnExit = null;
        exit?.Invoke();
    }

    /// <summary>
    /// Dev (--shot-caravan): steps the abstract world, a game hour at a time, until one of the home region's caravans is on its road well inside the region, then points the run at that
    /// moment (season, day, hour) and starts the hero on the road a few metres behind the lead wagon, so a screenshot shows the LODBridge's cart, animals and people.
    /// </summary>
    void PrepareCaravanShot()
    {
        if (_source?.Runtime is not { } rt || rt.State.ContinentState is not { } st) { GD.PushWarning("[street] --shot-caravan needs a seed start with a world runtime"); return; }
        var home = rt.State.HomeRegion;
        for (int i = 0; i < 24 * 45; i++)
        {
            rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1));
            foreach (var c in st.Caravans.All)
            {
                if (c.State != CaravanState.Outbound || c.Region != c.Home || c.PrevDist >= c.Dist || (c.Fielded < 2 && i < 24 * 20)) continue;
                long len = c.Path.LengthMm; if (c.Dist < 2_500_000 || c.Dist > len - 2_500_000) continue;
                var lead = c.Path.At(c.Dist); var behind = c.Path.At(c.Dist - 8_000);
                int lx = (int)(lead.XMm / 1000 - home.OriginXM), ly = (int)(lead.YMm / 1000 - home.OriginYM);
                if (lx < 400 || ly < 400 || lx > RegionSettings.RegionM - 400 || ly > RegionSettings.RegionM - 400) continue;
                var t = new GameTime(c.AnchorSec);
                _opt.Season = t.Season switch { Season.Summer => "summer", Season.Autumn => "autumn", Season.Winter => "winter", _ => "spring" };
                _opt.Day = t.DayOfSeason; _opt.Hour = t.Hour;
                _opt.Start = ((int)(behind.XMm / 1000 - home.OriginXM), (int)(behind.YMm / 1000 - home.OriginYM), 0);
                _opt.Pan = (lead.HeadX * 10 / 1024, lead.HeadY * 10 / 1024);                        // the camera looks along the road at the train
                GD.Print($"[street] caravan {c.Id} at region ({lx},{ly}), {t}: {c.Vehicles.Length} vehicles, {c.Crew.Length} crew, state {c.State}");
                return;
            }
        }
        GD.PushWarning("[street] --shot-caravan: no caravan on the road within 45 game days");
    }

    /// <summary>Starts the tick worker and puts the hero's region where the live world stands (a loaded game resumes across a border).</summary>
    void StartRuntime()
    {
        if (Runtime is not { } rt) return;
        if (_opt.AllNeighbours) foreach (var n in rt.State.HomeRegion.Neighbours()) rt.Manager.Request(n, 0); // dev shot: every neighbour built and ticking
        // the tick worker and the region builders allocate (a region is 30-40 MB): without this a blocking full collection of a 400 MB heap lands on a frame (a 120 ms frame was seen with nine regions)
        try { _gcMode = System.Runtime.GCSettings.LatencyMode; System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency; } catch (InvalidOperationException) { /* no concurrent GC: leave it */ }
        var (wx, wy) = _world.WorldCell;
        rt.Manager.BuildFailed += (rc, e) => GD.PushWarning($"[street] region {rc} failed to build after {rt.Manager.MaxBuildAttempts} tries and is given up on: {e.GetBaseException().Message}"); // once per region
        rt.PlaceHero(rt.State.HomeRegion.OriginXM + wx, rt.State.HomeRegion.OriginYM + wy);
        rt.Start();
        rt.Pump(_world.Clock.Now, rt.State.HomeRegion.OriginXM + wx, rt.State.HomeRegion.OriginYM + wy); // the first call pays for JIT (up to 60 ms seen): here, not on a frame
        Ravensguard.Sim.Diagnostics.GcSettle.Run(); // the built world is copied out of the young generations now, not by a collection in the middle of a tick
        AttachHero();                                // the hero's desk on the runtime's barrier (trade spec 4.7)
        AttachAutopilot();                           // directives, limits and interrupts on the same barrier (autopilot spec 6)
        AttachTalk();                                // rumours seeded and spread at the same barrier (dialogue spec 4.9)
    }

    /// <summary>The per-frame call to the tick runtime: tells it the clock and where the hero is. Costs microseconds; the hours themselves run on the worker.</summary>
    void PumpRuntime()
    {
        if (Runtime is not { } rt) return;
        var (wx, wy) = _world.WorldCell;
        long t0 = Stopwatch.GetTimestamp();
        rt.Pump(_world.Clock.Now, rt.State.HomeRegion.OriginXM + wx, rt.State.HomeRegion.OriginYM + wy);
        double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        _pumpMsSum += ms; _pumpMsMax = Math.Max(_pumpMsMax, ms); _pumpCount++;
        if (rt.Fault != null && !_runtimeFaultShown)
        {
            _runtimeFaultShown = true;
            GD.PushWarning("world tick failed: " + rt.Fault);
            Diagnostics.GameDiagnostics.ReportWorkerFault(rt.Fault, "region-tick");
            _router?.ShowToast(L.F("TICK_FAILED", rt.Fault.GetBaseException().Message), 8f);
        }
    }

    // ---- fire-and-forget barrier jobs: a fault is logged, shown once in a while as a toast and kept in the HUD, never dropped silently

    readonly System.Collections.Concurrent.ConcurrentQueue<string> _taskFaults = new();
    Label? _faultLabel; int _taskFaultTotal; double _taskFaultToastAt = -100;

    /// <summary>A world job at the next barrier whose result nobody awaits (a directive, a fold-back, a bout): faults are observed by <see cref="Watch"/>.</summary>
    Task<T> Bar<T>(WorldRuntime rt, Func<T> job, [System.Runtime.CompilerServices.CallerMemberName] string what = "") => Watch(rt.AtBarrier(job), what);

    Task<T> Watch<T>(Task<T> task, string what) { ((Task)task).ContinueWith(t => NoteTaskFault(t, what), TaskContinuationOptions.OnlyOnFaulted); return task; }

    void NoteTaskFault(Task t, string what)
    {
        string msg = t.Exception?.GetBaseException().Message ?? "?";
        Diagnostics.GameDiagnostics.Warn("sim", $"{what} failed: {msg}");     // worker thread: the log is thread safe, the UI is touched on the main thread only (PumpTaskFaults)
        Interlocked.Increment(ref _taskFaultTotal);
        if (_taskFaults.Count < 8) _taskFaults.Enqueue(msg);
    }

    void PumpTaskFaults()
    {
        if (_faultLabel != null)
        {
            bool tick = Runtime?.Fault != null;
            _faultLabel.Visible = tick || _taskFaultTotal > 0;
            if (_faultLabel.Visible) _faultLabel.Text = L.T(tick ? "HUD_TICK_FAULT" : "HUD_TASK_FAULT");
        }
        if (_taskFaults.IsEmpty) return;
        while (_taskFaults.TryDequeue(out var m)) if (_frames / 60.0 - _taskFaultToastAt > 8) { _taskFaultToastAt = _frames / 60.0; _router?.ShowToast(L.F("TASK_FAILED", Diagnostics.GameDiagnostics.Redactor.Redact(m)), 5f); }
    }

    void OnBlueprintsReloaded(BuildingLibrary _) => _router?.ShowToast(L.T("STREET_BLUEPRINTS_RELOADED"), 4f);

    static bool IsNight(GameTime t) => LightModel.AmbientAt(t, 480).Phase == DayPhase.Night;

    public static string ContentDir()
    {
        foreach (var dir in new[]
        {
            ProjectSettings.GlobalizePath("res://../../content/base"),
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".", "content/base"),
        })
        {
            var full = System.IO.Path.GetFullPath(dir);
            if (System.IO.Directory.Exists(full)) return full;
        }
        throw new InvalidOperationException("content/base not found");
    }

    public static BuildingLibrary LoadLibrary()
    {
        var r = new ModLoader(ContentTypeRegistry.Default).Load(EditorLibrary.ModFolders()); // base plus the user blueprint mod: a saved blueprint shows up on the next use
        if (!r.Success) throw new InvalidOperationException("content failed to load: " + r.Report);
        return BuildingLibrary.FromDatabase(r.Database);
    }

    void BuildScene()
    {
        _box = new SubViewportContainer { Name = "WorldBox", Stretch = false, MouseFilter = MouseFilterEnum.Ignore, TextureFilter = TextureFilterEnum.Nearest };
        AddChild(_box);
        _vp = new SubViewport { Name = "World", HandleInputLocally = false, GuiDisableInput = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Size = new Vector2I(1280, 720) };
        _box.AddChild(_vp);
        // behind everything: a neutral ground colour, so a cell whose chunk has not arrived yet (or lies off the map) reads as open country, never as a black hole
        var under = new CanvasLayer { Name = "Ground", Layer = -10 };
        _ground = new ColorRect { Color = new Color(0.36f, 0.43f, 0.31f), MouseFilter = MouseFilterEnum.Ignore };
        _ground.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        under.AddChild(_ground);
        _vp.AddChild(under);
        var root = new Node2D { Name = "Root" };
        _vp.AddChild(root);
        _street = new Node2D { Name = "Street" };
        root.AddChild(_street);

        var ts = new TileSet { TileSize = new Vector2I(Tile, Tile) };
        AddSource(ts, "terrain_32", "terrain.", _terrainCells, 0);
        AddSource(ts, "objects_32", "object.", _objectCells, 1);
        if (_art.HasAtlas("terrain2_32")) AddSource(ts, "terrain2_32", "terrain.", _terrainCells, 2, "autotile.");
        if (_art.HasAtlas("overlays_32")) AddSource(ts, "overlays_32", "overlay.puddle", _decalCells, 3);
        var shade = new Color(0.58f, 0.58f, 0.62f);
        TileMapLayer Layer(string name, ShaderMaterial? mat)
        {
            var l = new TileMapLayer { Name = name, TileSet = ts, TextureFilter = TextureFilterEnum.Nearest, RenderingQuadrantSize = 16, Material = mat };
            _street.AddChild(l);
            return l;
        }
        // season shader: ground gets snow, ice and wet; objects and props only the colour swap; the level below is the same one shade darker
        var gMat = _seasons.Create(ground: true);
        var gBelow = _seasons.Create(ground: true, shade: shade);
        var oMat = _seasons.Create(ground: false);
        var oBelow = _seasons.Create(ground: false, shade: shade);
        _matProp = _seasons.Create(ground: false, perSprite: true);
        _belowT = Layer("BelowTerrain", gBelow);
        _belowO = Layer("BelowObjects", oBelow);
        _baseG = Layer("Ground", gMat);
        _baseT = Layer("Terrain", null);
        _decal = Layer("Decals", _seasons.CreateDecal());
        _baseO = Layer("Objects", oMat);

        _ysort = new Node2D { Name = "Sprites", YSortEnabled = true };
        _street.AddChild(_ysort);
        _heroSprite = new RigActor(_rig, "hero"); // stacked layers from rig_32x64.png
        _ysort.AddChild(_heroSprite);
        _caravanSprites = new CaravanSprites(_ysort, _rig, _art);
        _clothes = _source?.Content != null ? new ClothingFeed(_source.Content) : null;
        _lodSprites = new PeopleSprites(_ysort, _rig, _clothes);
        _torchSprite = new Sprite2D { Texture = _art.Texture("item.torch"), Centered = false, Offset = new Vector2(-3, -34), TextureFilter = TextureFilterEnum.Nearest, Visible = false };
        _heroSprite.AddChild(_torchSprite);
        if (_hero != null) _heroSprite.AddChild(new Label { Name = "Nameplate", Text = _hero.Name, Position = new Vector2(-60, -84), CustomMinimumSize = new Vector2(120, 0), HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "HintLabel", MouseFilter = MouseFilterEnum.Ignore, ZIndex = 50 });

        // light: one texel per tile, filtered linearly so light fades smoothly, multiplied over everything below it
        // the hamlet lights its whole grid; a streamed window is 224 m across, so the light texture is a 128 x 96 m patch that follows the hero
        int w = _world.Streamer != null ? LightPatchW : _world.Grid.Width, h = _world.Streamer != null ? LightPatchH : _world.Grid.Height;
        _lightW = w; _lightH = h;
        _lightBytes = new byte[w * h * 4];
        _lightTex = ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rgba8, _lightBytes));
        var shader = new Shader { Code = "shader_type canvas_item;\nrender_mode blend_mul, unshaded;\nvoid fragment() { COLOR = vec4(texture(TEXTURE, UV).rgb, 1.0); }\n" };
        _lightSprite = new Sprite2D
        {
            Name = "Light", Texture = _lightTex, Centered = false, Scale = new Vector2(Tile, Tile), TextureFilter = TextureFilterEnum.Linear,
            Material = new ShaderMaterial { Shader = shader }, ZIndex = 100,
        };
        _street.AddChild(_lightSprite);

        // Field and Command: cached chunk images, drawn instead of the street layers
        _far = new FarLayer(_art, _seasons, _world.Streamer, _world.Streamer == null ? Map : null) { Name = "Far", Visible = false };
        root.AddChild(_far);
        var amb = new CanvasLayer { Name = "FarAmbient", Layer = 4 };
        _ambientMat = new ShaderMaterial { Shader = new Shader { Code = "shader_type canvas_item;\nrender_mode blend_mul, unshaded;\nuniform vec4 tint = vec4(1.0);\nvoid fragment() { COLOR = vec4(tint.rgb, 1.0); }\n" } };
        _ambient = new ColorRect { Color = Colors.White, Material = _ambientMat, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        amb.AddChild(_ambient);
        _vp.AddChild(amb);
        _vp.AddChild(_wover.Node);

        _cam = new Camera2D { Name = "Camera", AnchorMode = Camera2D.AnchorModeEnum.DragCenter };
        root.AddChild(_cam);
        _cam.MakeCurrent();
    }

    /// <summary>Adds an atlas as a TileSet source. Ids starting with <paramref name="prefix"/> (or <paramref name="prefix2"/>) are listed in <paramref name="cells"/>.</summary>
    void AddSource(TileSet ts, string atlas, string prefix, Dictionary<string, (int, Vector2I)> cells, int sourceId, string? prefix2 = null)
    {
        var src = new TileSetAtlasSource { Texture = _art.Atlas(atlas), TextureRegionSize = new Vector2I(Tile, Tile) };
        foreach (var (id, r) in _art.All)
        {
            if (r.Atlas != atlas || r.W != Tile || r.H != Tile) continue;
            var c = new Vector2I(r.X / Tile, r.Y / Tile);
            src.CreateTile(c);
            if (!cells.ContainsKey(id)) cells[id] = (sourceId, c);
        }
        ts.AddSource(src, sourceId);
    }

    void FitViewport()
    {
        float f = GetWindow().ContentScaleFactor;
        var phys = new Vector2I(Math.Max(64, (int)(Size.X * f)) & ~1, Math.Max(64, (int)(Size.Y * f)) & ~1);
        _vp.Size = phys;
        _box.Scale = new Vector2(1f / f, 1f / f);
        _box.Position = Vector2.Zero;
        _shownTick = -1;
    }

    // ------------------------------------------------------------------ hud

    void BuildHud()
    {
        var hud = new Control { Name = "Hud", MouseFilter = MouseFilterEnum.Ignore };
        hud.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(hud);

        var top = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(12, 12) };
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 2);
        top.AddChild(v);
        _dateLabel = new Label { ThemeTypeVariation = "TitleLabel" };
        _dateLabel.AddThemeFontSizeOverride("font_size", 20);
        _phaseLabel = new Label();
        v.AddChild(_dateLabel); v.AddChild(_phaseLabel);
        // the time widget's weather row: a pixel glyph, the weather in words and the temperature in degrees C
        var wrow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        wrow.AddThemeConstantOverride("separation", 6);
        _weatherIcon = new TextureRect { CustomMinimumSize = new Vector2(32, 32), StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, TextureFilter = TextureFilterEnum.Nearest };
        _weatherLabel = new Label { VerticalAlignment = VerticalAlignment.Center };
        wrow.AddChild(_weatherIcon); wrow.AddChild(_weatherLabel);
        v.AddChild(wrow);
        // the hero's body: a warning in words, hours before cold or heat matters (clothing spec 6), empty when all is well
        _condLabel = new Label { Visible = false, Modulate = new Color(1f, 0.82f, 0.45f), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(290, 0) };   // wraps: a long warning must not widen the panel under the travel banner
        v.AddChild(_condLabel);
        _bandLabel = new Label { ThemeTypeVariation = "HintLabel" };
        v.AddChild(_bandLabel);
        if (_hero != null) v.AddChild(new Label { Text = L.F("STREET_HERO_HINT", _hero.Name, H("world.panel_character"), H("world.panel_codex")), ThemeTypeVariation = "HintLabel" });
        BuildAutopilotHud(v);
        _faultLabel = new Label { Visible = false, Modulate = new Color(1f, 0.55f, 0.4f), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(290, 0) };
        v.AddChild(_faultLabel);
        hud.AddChild(top);
        BuildPartyBar(hud); BuildTalkHud(hud); BuildOnboardingHud(hud);

        if (_regionMap != null)
        {
            _minimap = new Minimap(_regionMap, HeroWorld, () => OpenMap());
            _minimap.AnchorLeft = 1; _minimap.AnchorRight = 1; _minimap.AnchorTop = 1; _minimap.AnchorBottom = 1;
            _minimap.OffsetLeft = -(Minimap.Pixels + 24); _minimap.OffsetRight = -12; _minimap.OffsetTop = -(Minimap.Pixels + 24 + 48); _minimap.OffsetBottom = -(48);
            _minimap.GrowHorizontal = GrowDirection.Begin; _minimap.GrowVertical = GrowDirection.Begin;
            hud.AddChild(_minimap);
        }

        var lv = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        lv.AnchorLeft = 1; lv.AnchorRight = 1; lv.OffsetRight = -12; lv.OffsetTop = 12; lv.GrowHorizontal = GrowDirection.Begin;
        var lvb = new VBoxContainer();
        lvb.AddThemeConstantOverride("separation", 0);
        lv.AddChild(lvb);
        lvb.AddChild(new Label { Text = L.T("STREET_LEVEL_TITLE"), ThemeTypeVariation = "HintLabel", HorizontalAlignment = HorizontalAlignment.Center });
        for (int z = _world.Grid.MaxZ; z >= _world.Grid.MinZ; z--)
        {
            var b = new Label { HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(64, 0) };
            _levelBoxes.Add(b);
            lvb.AddChild(b);
        }
        _levelLabel = new Label { ThemeTypeVariation = "HintLabel", HorizontalAlignment = HorizontalAlignment.Center };
        lvb.AddChild(_levelLabel);
        hud.AddChild(lv);

        _hintLabel = new Label { ThemeTypeVariation = "HintLabel", Text = HintText() };
        _hintLabel.AnchorTop = 1; _hintLabel.AnchorBottom = 1; _hintLabel.AnchorRight = 1; _hintLabel.OffsetLeft = 12; _hintLabel.OffsetRight = -12; _hintLabel.OffsetTop = -44; _hintLabel.OffsetBottom = -4; _hintLabel.GrowVertical = GrowDirection.Begin;
        _hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart; _hintLabel.VerticalAlignment = VerticalAlignment.Bottom;   // the key line wraps to the window instead of running off its edge
        _hintLabel.AddThemeColorOverride("font_outline_color", PixelTheme.Border);
        _hintLabel.AddThemeConstantOverride("outline_size", 4);
        hud.AddChild(_hintLabel);

        _perfLabel = new Label { Visible = false, Position = new Vector2(12, 90) };
        hud.AddChild(_perfLabel);
    }

    static string H(string id) => InputBootstrap.Hint(id).Trim('[', ']');

    static string HintText() => string.Format(L.T("STREET_HINTS"),
        $"{H("world.move_up")}{H("world.move_left")}{H("world.move_down")}{H("world.move_right")}", H("world.pause"), H("world.torch"),
        $"{H("world.level_up")}/{H("world.level_down")}", $"{H("world.zoom_in")}/{H("world.zoom_out")}", H("world.camera_home"), H("world.map"));

    (float X, float Y) HeroWorld()
    {
        var h = _world.Snapshot.Hero;
        return (_world.Snapshot.OriginX + h.X.ToFloat(), _world.Snapshot.OriginY + h.Y.ToFloat());
    }

    void OpenCharacter()
    {
        if (_router == null || _hero == null || _source?.Content == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _router.Push(new CharacterScreen(_router, _hero, _source.Content, () => _heroLink?.View, HeroBodyLine) { ShotPath = _opt.Panel == "character" ? _opt.ShotPath : null, OpenProperty = _heroLink != null ? () => OpenLand(true) : null });   // P5-A2: the deeds
    }

    CraftingSession? _crafting;

    /// <summary>N: the crafting screen (basic-crafting spec 4.7). The session (the hero's pack, skills by use, unfinished work) lives as long as the street view; it keeps to itself, off the tick worker's economy.</summary>
    void OpenCrafting()
    {
        if (_router == null || _hero == null || _source?.Content == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        int light = _world.Clock.Now.Hour is < 6 or >= 20 ? 120 : 255;
        // P5-E: the stations come from the room the hero stands in (and within 6 m), read afresh each time the screen opens
        _crafting = NewCraftingSession(light, CraftReachHere(light));
        _crafting.NowSec = _world.Clock.Now.Seconds; _crafting.Site.Light = light;
        _router.Push(new CraftingScreen(_router, _crafting) { ShotPath = _opt.Panel == "crafting" ? _opt.ShotPath : null, StartRecipe = _opt.Panel == "crafting" ? _opt.PanelEntry : null });
    }

    void OpenCodex()
    {
        if (_router == null || _world.Codex is not { } codex) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        _router.Push(new CodexScreen(_router, codex, _source?.History)
        {
            ShotPath = _opt.Panel == "codex" ? _opt.ShotPath : null, StartCategory = _opt.PanelCategory, StartEntry = _opt.PanelEntry,
        });
    }

    void OpenMap(bool picker = false)
    {
        if (_router == null || _regionMap == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        UI.ContinentOverview? ov = _source?.Regions != null && _source.Continent != null && _source.History != null && _world.Streamer is { } st
            ? new UI.ContinentOverview(_source.Continent, _source.History, _source.Regions, st.Home, HeroWorld) : null;
        _router.Push(new WorldMapScreen(_router, _regionMap, HeroWorld, ov) { CaravanMarks = CaravanMarksNow, RegionOrigin = Runtime == null ? (0, 0) : (Runtime.State.HomeRegion.OriginXM, Runtime.State.HomeRegion.OriginYM), TravelPlan = _travel == null ? null : PreviewTravel, TravelStart = _travel == null ? null : StartTravel, PreselectNth = picker || _opt.Travel == "picker" ? 7 : -1, ShotPath = _opt.MapShot ? _opt.ShotPath : null, StartScale = Math.Max(1, Math.Abs(_opt.MapScale)), StartContinent = _opt.MapScale <= 0 });
    }

    void UpdateHud(LiveSnapshot s)
    {
        var t = s.Time;
        _dateLabel.Text = $"{t.Hour:00}:{t.Minute:00}  {_calendar.Describe(t)}";
        _weatherIcon.Texture = PixelIcons.Weather(_wx.State, s.Ambient.Phase == DayPhase.Night);
        _weatherLabel.Text = $"{L.T(WeatherFeed.Name(_wx.State))}  {Mathf.Round(_wx.TempC):0} °C";
        string cond = RescueHud() ?? _clothes?.HudLine() ?? "";                    // P4 close-out: while the player is the rescuing companion
        _condLabel.Visible = cond.Length > 0; _condLabel.Text = cond;
        _bandLabel.Text = L.T(_band switch { Band.Close => "BAND_CLOSE", Band.Street => "BAND_STREET", Band.Field => "BAND_FIELD", _ => "BAND_COMMAND" });
        string phase = s.Ambient.Phase switch { DayPhase.Dawn => "STREET_PHASE_DAWN", DayPhase.Dusk => "STREET_PHASE_DUSK", DayPhase.Night => "STREET_PHASE_NIGHT", _ => "STREET_PHASE_DAY" };
        string speed = s.Speed == SimSpeed.Paused ? L.T("STREET_PAUSED") : $"{(int)s.Speed}x";
        string torch = s.Hero.Torch ? "  " + L.T("STREET_TORCH_ON") : "";
        string where = Runtime is { } rt ? $"  R({rt.Hero.Region.X},{rt.Hero.Region.Y})" : ""; // the region the hero stands in (changes the moment a border is crossed)
        _phaseLabel.Text = $"{L.T(phase)}  {speed}{torch}{where}";
        for (int i = 0; i < _levelBoxes.Count; i++)
        {
            int z = _world.Grid.MaxZ - i;
            bool hero = z == s.Hero.Z, view = z == _viewZ;
            _levelBoxes[i].Text = (view ? "> " : "  ") + (z > 0 ? $"+{z}" : z.ToString()) + (hero ? " @" : "  ");
            _levelBoxes[i].AddThemeColorOverride("font_color", view ? PixelTheme.Gold : hero ? PixelTheme.Body : PixelTheme.Dim);
        }
        _levelLabel.Text = _viewZ == s.Hero.Z ? "" : L.T("STREET_LEVEL_VIEWING");
    }

    // ------------------------------------------------------------------ level tiles

    // Tiles live in world cells (1 cell = 1 tile layer cell), so a streamed window can move under them without redrawing: a chunk is
    // drawn when it arrives (or its content changes) and erased when it leaves the window.
    readonly Dictionary<(int X, int Y), int> _drawn = new();
    sealed class PropNode { public Sprite2D Node = null!; public string Base = ""; public float H; public string Shown = ""; }
    readonly Dictionary<(int X, int Y), List<PropNode>> _chunkProps = new();
    // fences, hedges and walls are props in the sim but one tile line in the view: world cell -> autotile set ("*" = a gate the line runs into)
    readonly Dictionary<(int X, int Y), string> _lines = new();
    readonly Dictionary<(int X, int Y), List<(int X, int Y)>> _chunkLines = new();
    readonly Dictionary<string, string?> _terrainPick = new(), _objectPick = new(), _propPick = new();
    HamletMap? _syncedMap; int _syncedRev = -1;

    // Tile drawing is time-sliced: a new or changed chunk is queued and drawn a band of rows at a time, nearest the camera first, within DrawBudgetMs per frame
    // (one 32 x 32 chunk is ~2.3 ms; seven arrive at once when the hero crosses a chunk edge, and a level switch redraws 49).
    const double DrawBudgetMs = 3.0;
    const int BandRows = 8, PropBatch = 48;
    readonly List<(int X, int Y)> _todo = new(), _erase = new();
    sealed class Job { public int X, Y, Ver, Row, PropIdx = -1; public List<Prop>? Props; }
    Job? _cur;
    Dictionary<(int X, int Y), List<Prop>>? _propsByChunk; HamletMap? _propsMap; int _propsRev = -1;
    readonly Stopwatch _drawSw = new();

    /// <summary>True when every chunk of the window is drawn.</summary>
    bool TilesSettled => _todo.Count == 0 && _erase.Count == 0 && _cur == null && _syncedMap == Map && _syncedRev == Map.StreamRevision;

    /// <summary>The view level changed (or first draw): forget everything and queue the current map's chunks at this level. <paramref name="now"/> draws them all before returning (the first draw); otherwise they fill in over the next frames, the ones under the camera first.</summary>
    void RebuildLevel(bool now = false)
    {
        var sw = Stopwatch.StartNew();
        _baseT.Clear(); _baseO.Clear(); _belowT.Clear(); _belowO.Clear(); _baseG.Clear(); _decal.Clear();
        foreach (var l in _chunkProps.Values) foreach (var n in l) n.Node.QueueFree();
        _chunkProps.Clear(); _drawn.Clear(); _lines.Clear(); _chunkLines.Clear();
        _todo.Clear(); _erase.Clear(); _cur = null;
        _syncedMap = null;
        SyncTiles(now ? double.MaxValue : DrawBudgetMs);
        _shownTick = -1;
        GD.Print($"[street] level {_viewZ} tiles {(now ? "rebuilt" : "queued")} in {sw.Elapsed.TotalMilliseconds:F1} ms ({_drawn.Count} chunks drawn, {_todo.Count} queued)");
    }

    /// <summary>Chunk coordinates (fractional) of the spot the view looks at: the hero plus the camera pan.</summary>
    (float X, float Y) FocusChunk()
    {
        var s = _world.Snapshot;
        float wx = s.OriginX + s.Hero.X.ToFloat() + _pan.X / Tile, wy = s.OriginY + s.Hero.Y.ToFloat() + _pan.Y / Tile;
        return (wx / 32f, wy / 32f);
    }

    /// <summary>Queues chunks that are new or changed in the current window, erases those that left it, and draws queued work until <paramref name="budgetMs"/> is spent (always at least one band).</summary>
    void SyncTiles(double budgetMs)
    {
        var m = Map;
        if (m.ChunkVersions == null)
        {
            if (m == _syncedMap && m.StreamRevision == _syncedRev) return;
            _syncedMap = m; _syncedRev = m.StreamRevision;
            if (_drawn.Count > 0) return; // the hamlet never changes
            DrawCells(m, 0, 0, m.Grid.Width, m.Grid.Height);
            var all = new List<Prop>(m.Props);
            AddProps(m, (0, 0), all);
            _drawn[(0, 0)] = 0;
            return;
        }
        if (m != _syncedMap || m.StreamRevision != _syncedRev)
        {
            _syncedMap = m; _syncedRev = m.StreamRevision;
            foreach (var k in _drawn.Keys) if (!m.ChunkVersions.ContainsKey(k) && !_erase.Contains(k)) _erase.Add(k);
            _todo.Clear();
            foreach (var (k, ver) in m.ChunkVersions)
                if (!(_drawn.TryGetValue(k, out int dv) && dv == ver)) _todo.Add(k);
            if (_cur is { } c && (!m.ChunkVersions.TryGetValue((c.X, c.Y), out int cv) || cv != c.Ver)) { ClearChunkObjects((c.X, c.Y)); _cur = null; } // changed or gone while half drawn: start over from the queue
            if (_cur is { } c2) _todo.Remove((c2.X, c2.Y));
        }
        if (_todo.Count == 0 && _erase.Count == 0 && _cur == null) return;
        _drawSw.Restart();
        while (true)
        {
                        if (_erase.Count > 0)
            {
                var k = _erase[^1]; _erase.RemoveAt(_erase.Count - 1);
                if (!m.ChunkVersions.ContainsKey(k) && _drawn.ContainsKey(k)) EraseChunk(k);
            }
            else
            {
                if (_cur == null)
                {
                    if (_todo.Count == 0) break;
                    var (fx, fy) = FocusChunk();
                    int best = 0; float bd = float.MaxValue;
                    for (int i = 0; i < _todo.Count; i++)
                    {
                        float dx = _todo[i].X + 0.5f - fx, dy = _todo[i].Y + 0.5f - fy, d = dx * dx + dy * dy;
                        if (d < bd) { bd = d; best = i; }
                    }
                    var nk = _todo[best]; _todo.RemoveAt(best);
                    _cur = new Job { X = nk.X, Y = nk.Y, Ver = m.ChunkVersions[nk] };
                }
                var cur = _cur!;
                var key = (cur.X, cur.Y);
                if (cur.Row < 32)
                {
                    int lx = cur.X * 32 - m.OriginX, ly = cur.Y * 32 - m.OriginY;
                    DrawCells(m, lx, ly + cur.Row, lx + 32, ly + cur.Row + BandRows);
                    cur.Row += BandRows;
                }
                else
                {
                    if (cur.PropIdx >= 0 && (_propsMap != m || _propsRev != m.StreamRevision)) cur.PropIdx = -1; // the window moved under a half-placed chunk: its props are in the wrong frame, place them again
                    if (cur.PropIdx < 0)
                    {
                        ClearChunkObjects(key); // a redrawn chunk: its old props and fence pieces go as the new ones arrive
                        if (_propsMap != m || _propsRev != m.StreamRevision)
                        {
                            _propsByChunk = new(); _propsMap = m; _propsRev = m.StreamRevision;
                            foreach (var p in m.Props)
                            {
                                var ck = (Div(p.X + m.OriginX, 32), Div(p.Y + m.OriginY, 32));
                                if (!_propsByChunk.TryGetValue(ck, out var l)) _propsByChunk[ck] = l = new();
                                l.Add(p);
                            }
                        }
                        cur.Props = _propsByChunk!.GetValueOrDefault(key);
                        cur.PropIdx = 0;
                    }
                    // props go in batches (a town chunk holds a thousand sprites and fence pieces)
                    int end = Math.Min(cur.Props?.Count ?? 0, cur.PropIdx + PropBatch);
                    if (cur.Props != null && cur.PropIdx < end) AddProps(m, key, cur.Props, cur.PropIdx, end);
                    cur.PropIdx = end;
                    if (cur.PropIdx >= (cur.Props?.Count ?? 0))
                    {
                        _drawn[key] = cur.Ver;
                        _cur = null;
                    }
                }
            }
            if (_drawSw.Elapsed.TotalMilliseconds >= budgetMs) break;
        }
    }

    static int Div(int a, int b) { int q = a / b; return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q; }

    string? Pick(string id, Dictionary<string, (int Src, Vector2I Cell)> cells, Dictionary<string, string?> memo)
    {
        if (memo.TryGetValue(id, out var r)) return r;
        r = null;
        foreach (var alt in TileSprites.Alternatives(id)) if (cells.ContainsKey(alt)) { r = alt; break; }
        return memo[id] = r;
    }

    /// <summary>Walls, doors, floors and stairs live on the Terrain layer (no season shader: nothing seasonal about them); everything else is ground and takes snow, ice and rain.</summary>
    static bool IsStructureId(string id) =>
        id.StartsWith("terrain.wall") || id.StartsWith("terrain.door") || id.StartsWith("terrain.stairs") || id.StartsWith("terrain.void") || id.StartsWith("terrain.floor")
        || id is "terrain.flagstone" or "terrain.wood_floor" or "terrain.earth_floor" or "terrain.feat";

    static double Hash2(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)s * 1442695041u;
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return h / 4294967296.0;
        }
    }

    void DrawCells(HamletMap m, int x0, int y0, int x1, int y1)
    {
        var g = m.Grid; int z = _viewZ, ox = m.OriginX, oy = m.OriginY;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var c = new Vector2I(x + ox, y + oy);
                _baseT.EraseCell(c); _baseG.EraseCell(c); _baseO.EraseCell(c); _belowT.EraseCell(c); _belowO.EraseCell(c); _decal.EraseCell(c);
                var t = TileSprites.Terrain(m, x, y, z);
                if (t != null)
                {
                    if (Pick(t, _terrainCells, _terrainPick) is { } tp)
                    {
                        var (src, cell) = _terrainCells[tp];
                        (IsStructureId(tp) ? _baseT : _baseG).SetCell(c, src, cell);
                        // puddles sit on open ground; the shader shows them when the ground is wet and hides them under snow or ice
                        if (z == 0 && !IsStructureId(tp) && _decalCells.Count > 0 && PuddleChance(tp) is { } chance)
                        {
                            double h = Hash2(c.X, c.Y, 811);
                            if (h < chance) { var (psrc, pcell) = _decalCells[$"overlay.puddle.{(int)(h * 1000) % 3}"]; _decal.SetCell(c, psrc, pcell); }
                        }
                    }
                    if (TileSprites.Object(m, x, y, z) is { } o && Pick(o, _objectCells, _objectPick) is { } op) { var (osrc, ocell) = _objectCells[op]; _baseO.SetCell(c, osrc, ocell); }
                    continue;
                }
                for (int dz = 1; dz <= 2 && z - dz >= g.MinZ; dz++) // open air: look down
                {
                    var bt = TileSprites.Terrain(m, x, y, z - dz);
                    if (bt == null) continue;
                    if (Pick(bt, _terrainCells, _terrainPick) is { } btp) { var (bsrc, bcell) = _terrainCells[btp]; _belowT.SetCell(c, bsrc, bcell); }
                    if (TileSprites.Object(m, x, y, z - dz) is { } bo && Pick(bo, _objectCells, _objectPick) is { } bop) { var (bosrc, bocell) = _objectCells[bop]; _belowO.SetCell(c, bosrc, bocell); }
                    break;
                }
            }
    }

    /// <summary>How likely a ground tile is to hold a puddle (ground that does not soak water in: paths, roads, trodden grass). Null for tiles that never do (water, fields, sand).</summary>
    static double? PuddleChance(string id) =>
        id.StartsWith("autotile.path_dirt") || id.StartsWith("terrain.dirt") || id.StartsWith("terrain.mud") || id.StartsWith("autotile.track") || id.StartsWith("autotile.mud") ? 0.16
        : id.StartsWith("terrain.cobble") || id.StartsWith("autotile.road_cobble") ? 0.09
        : id.StartsWith("terrain.grass") || id.StartsWith("terrain.meadow") ? 0.045
        : null;

    void AddProps(HamletMap m, (int X, int Y) key, List<Prop> props) => AddProps(m, key, props, 0, props.Count);

    void AddProps(HamletMap m, (int X, int Y) key, List<Prop> props, int from, int to)
    {
        if (!_chunkProps.TryGetValue(key, out var list)) _chunkProps[key] = list = new List<PropNode>();
        List<(int X, int Y)>? lineCells = null;
        for (int pi = from; pi < to; pi++)
        {
            var p = props[pi];
            if (p.Z != _viewZ) continue;
            int px = p.X + m.OriginX, py = p.Y + m.OriginY;
            if (!p.Wide && TileSprites.LineFamily(p.Sprite) is { } fam && _terrainCells.Count > 0)
            {
                _lines[(px, py)] = fam; (lineCells ??= new()).Add((px, py));
                continue;
            }
            if (TileSprites.LineConnector(p.Sprite)) { _lines[(px, py)] = "*"; (lineCells ??= new()).Add((px, py)); }
            float h = (float)Hash2(px, py, 313);
            var id = PropSprite(p.Sprite, h);
            if (id == null) continue;
            var tex = _art.Texture(id);
            var size = tex.GetSize();
            float cx = p.Wide ? (px + 1) * Tile : px * Tile + Tile / 2f;
            var sp = new Sprite2D { Texture = tex, Centered = false, Offset = new Vector2(-size.X / 2f, -size.Y), Position = new Vector2(cx, (py + 1) * Tile), TextureFilter = TextureFilterEnum.Nearest, Material = _matProp };
            _ysort.AddChild(sp);
            list.Add(new PropNode { Node = sp, Base = p.Sprite, H = h, Shown = id });
        }
        if (lineCells != null)
        {
            if (_chunkLines.TryGetValue(key, out var have)) have.AddRange(lineCells); else _chunkLines[key] = lineCells;
            foreach (var c in lineCells)
            {
                RefreshLine(c);
                RefreshLine((c.X, c.Y - 1)); RefreshLine((c.X + 1, c.Y)); RefreshLine((c.X, c.Y + 1)); RefreshLine((c.X - 1, c.Y)); // a neighbour in an earlier chunk now has something to join
            }
        }
    }

    /// <summary>Draws one cell of a fence, hedge or wall run with the autotile piece that matches which neighbours are part of the same run.</summary>
    void RefreshLine((int X, int Y) c)
    {
        if (!_lines.TryGetValue(c, out var fam) || fam == "*") return;
        bool Joined(int dx, int dy) => _lines.TryGetValue((c.X + dx, c.Y + dy), out var f) && (f == fam || f == "*");
        int mask = (Joined(0, -1) ? 1 : 0) | (Joined(1, 0) ? 2 : 0) | (Joined(0, 1) ? 4 : 0) | (Joined(-1, 0) ? 8 : 0);
        if (Pick($"autotile.{fam}.{TileSprites.MaskName(mask)}", _terrainCells, _terrainPick) is not { } id) return;
        var cell = new Vector2I(c.X, c.Y);
        var (src, tile) = _terrainCells[id];
        _baseG.SetCell(cell, src, tile);
        _decal.EraseCell(cell);
    }

    /// <summary>The sprite a prop shows right now. Generator trees (object.tree.N) become species trees: summer foliage, or the bare winter sprite once the leaves are down
    /// (autumn colours come from the shader); props with a snow-capped variant take it under snow.</summary>
    string? PropSprite(string baseId, float h)
    {
        var st = _seasons.State;
        string id = baseId;
        int dot = baseId.LastIndexOf('.');
        if (baseId.StartsWith("object.tree.") && dot > 0 && int.TryParse(baseId.AsSpan(dot + 1), out int n))
        {
            string sp = h < 0.18f ? "pine" : (n % 3) switch { 0 => "oak", 1 => "ash", _ => "birch" };
            bool bare = st.Season == 3 ? st.Frac < 0.85f + 0.15f * h : st.Season == 2 && st.Frac > 0.55f + 0.45f * h;
            if (sp != "pine" || !bare)
            {
                string cand = $"object.tree.{sp}.{(bare ? "winter" : "summer")}";
                if (_art.Has(cand)) return cand;
            }
            else if (_art.Has("object.tree.pine.winter")) return "object.tree.pine.winter";
        }
        if (baseId == "object.tree.apple.summer")
        {
            // orchard trees follow the year: blossom in spring, leaves in summer, ripe apples in autumn, bare wood once the leaves are down
            bool bare = st.Season == 3 || (st.Season == 2 && st.Frac > 0.55f + 0.45f * h);
            string cand = bare ? "object.tree.apple.winter" : st.Season switch { 0 => "object.tree.apple.spring", 2 => "object.tree.apple.autumn", _ => "object.tree.apple.summer" };
            if (_art.Has(cand)) return cand;
        }
        if (baseId is "object.bush" or "object.hedge")
        {
            // hedges and bushes: bare twigs once the leaves are down, a snow-capped round bush under snow
            bool bare = st.Season == 3 ? st.Frac < 0.85f + 0.15f * h : st.Season == 2 && st.Frac > 0.55f + 0.45f * h;
            if (bare) { string b = st.Snow > 0.3f && h < 0.5f ? "object.bush_round_snow" : "object.bush_bare"; if (_art.Has(b)) return b; }
        }
        if (st.Snow > 0.3f && _art.Has(baseId + "_snow")) return baseId + "_snow";
        return PickProp(id);
    }

    string? PickProp(string id)
    {
        if (_propPick.TryGetValue(id, out var r)) return r;
        r = null;
        foreach (var alt in TileSprites.Alternatives(id)) if (_art.Has(alt)) { r = alt; break; }
        return _propPick[id] = r;
    }

    string _propState = "";
    /// <summary>When the season, snow cover or tree-leaf state moved enough to change which sprite a prop shows, swaps the textures.</summary>
    void RefreshProps()
    {
        var st = _seasons.State;
        string key = $"{st.Season}:{(int)(st.Frac * 40)}:{(st.Snow > 0.3f ? 1 : 0)}";
        if (key == _propState) return;
        _propState = key;
        foreach (var l in _chunkProps.Values)
            foreach (var pn in l)
            {
                var id = PropSprite(pn.Base, pn.H);
                if (id == null || id == pn.Shown) continue;
                pn.Shown = id;
                var tex = _art.Texture(id); var size = tex.GetSize();
                pn.Node.Texture = tex; pn.Node.Offset = new Vector2(-size.X / 2f, -size.Y);
            }
    }

    void EraseChunk((int X, int Y) k)
    {
        int cx = k.X * 32, cy = k.Y * 32;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                var c = new Vector2I(cx + x, cy + y);
                _baseT.EraseCell(c); _baseG.EraseCell(c); _baseO.EraseCell(c); _belowT.EraseCell(c); _belowO.EraseCell(c); _decal.EraseCell(c);
            }
        ClearChunkObjects(k);
        _drawn.Remove(k);
    }

    void ClearChunkObjects((int X, int Y) k)
    {
        if (_chunkProps.Remove(k, out var l)) foreach (var n in l) n.Node.QueueFree();
        if (_chunkLines.Remove(k, out var lc)) foreach (var c in lc) _lines.Remove(c);
    }

    // ------------------------------------------------------------------ frame

    public override void _Process(double delta)
    {
        _frames++;
        if (_toSettings && _router?.Top == this) { _toSettings = false; ResumeAfterMenu(); } // back from Settings
        var bench = _opt.BenchSeconds > 0;
        if (bench) { _benchT += delta; ScriptedWalk(); }
        if (!_quitting) HandleHeldInput(delta);

        _acc += Math.Min(delta, 0.25) * FightTimeScale();       // P4-D: slow motion and hit-stop give a fight step less real time (steps per real second only, FL-9)
        int steps = 0; _stepMsFrame = 0;
        int fightSteps = FightStepsPerSlice();                  // P4-F: a watched fight at 2x-8x steps the bubble that many times a slice, never a longer step (watch-mode 4.1)
        while (_acc >= Step && steps < 5)
        {
            _sw.Restart();
            for (int k = 0; k < fightSteps; k++)
            {
                _rec.Step();
                StepPeople();
                RideTick();
                TravelTick();
            }
            double ms = _sw.Elapsed.TotalMilliseconds;
            _lastFightSliceMs = ms;
            _tickMsSum += ms; _tickMsMax = Math.Max(_tickMsMax, ms); _tickCount++; _stepMsFrame += ms;
            _acc -= Step; steps++;
        }
        if (steps == 5) _acc = 0;
        foreach (var opened in _world.TakeOpenedCodexEntries()) _router?.ShowToast(L.F("CODEX_NEW_ENTRY", opened.Title)); // the sim opens pages as the hero walks
        PumpRuntime();
        PumpTaskFaults();
        PumpPeople();
        PumpJobs();
        HeroTick(delta);
        AutopilotTick(delta);
        TalkTick(delta); BarkShotStep();
        OnboardingTick(delta);
        PollSave();
        if (Run != null && !_leaving && _saveTask == null && (_sinceSave += delta) >= AutosaveSeconds) StartSave(SaveKind.Autosave, quiet: true);

        _sw.Restart();
        Render((float)(_acc / Step));
        double dms = _sw.Elapsed.TotalMilliseconds;
        if (bench && dms > 16) GD.Print($"[street] frame {_frames} draw {dms:F1} ms (tick steps {steps}, step ms {_stepMsFrame:F1})");
        _drawMsSum += dms; _drawMsMax = Math.Max(_drawMsMax, dms); _drawCount++;
        if (_frames > 3) _drawMsMaxWarm = Math.Max(_drawMsMaxWarm, dms); // frame 1 to 3 pay for JIT and first-use setup

        if (_opt.Click is { } c && _frames == 2) ClickMove(new Vector2(c.X, c.Y));
        if (_opt.MapShot && _frames == (_opt.Frames > 12 ? _opt.Frames : 3)) OpenMap(); // --shot-frames n (> 12) waits that long first, so neighbour regions are built when the map opens
        if (_opt.Panel == "character" && _frames == (_heroLink != null ? 90 : 3)) OpenCharacter();   // with a world: once the first barrier's hero view is in
        if (_opt.Panel == "codex" && _frames == 3) OpenCodex();
        if (_opt.Panel == "crafting" && _frames == 3) OpenCrafting();
        if (_opt.Panel == "jobs" && _frames == 420) OpenWork();    // dev: the work panel once the first posting has come back from the tick worker
        HeroShots(); TalkShots(); ApShots();
        LandTick(delta);                                    // P5-A2: the land view, land answers, land screenshots
        BusinessTick(delta);                                // P5-F: the Ledger, businesses and prospecting: views, answers, where the hero is, screenshots
        BuildTick();                                        // P5-B: construction brought up to the clock, built pieces laid into the street, ghosts drawn
        BuildModeTick(delta);                               // P5-C: build mode (StreetView.BuildMode.cs): the preview, the palette, answers to construction commands, the hero's hands
        if (_opt.IsShot && _opt.Weather == "storm" && _frames == Math.Max(2, _opt.Frames - 1)) _wover.Strike(); // dev: catch a lightning flash in the picture
        if (_opt.IsShot && !_opt.MapShot && _opt.Panel == null && !_quitting && _frames >= _opt.Frames && ShotReady()) Shoot();
        if (bench && _benchT >= _opt.BenchSeconds) Finish();
        if (bench && _perfLabel.Visible && _frames % 30 == 0)
            _perfLabel.Text = $"tick {_tickMsSum / Math.Max(1, _tickCount):F3} ms (max {_tickMsMax:F2})  draw {_drawMsSum / Math.Max(1, _drawCount):F3} ms (max {_drawMsMax:F2})  fps {Engine.GetFramesPerSecond()}{RuntimeLine()}";
    }

    void HandleHeldInput(double delta)
    {
        int dx = (Actions.HeldLoose("world.move_right") ? 1 : 0) - (Actions.HeldLoose("world.move_left") ? 1 : 0);
        int dy = (Actions.HeldLoose("world.move_down") ? 1 : 0) - (Actions.HeldLoose("world.move_up") ? 1 : 0);
        if (_opt.BenchSeconds > 0) return;
        if ((dx != 0 || dy != 0) && ApActive) ApTakeControl(L.T("AP_TOOK_CONTROL"));                   // any movement key takes the hero back at once (autopilot spec 4.5)
        if ((dx != 0 || dy != 0) && _travel?.Journey is { State: Ravensguard.Sim.Travel.JourneyState.Travelling or Ravensguard.Sim.Travel.JourneyState.Paused }) TakeControl();   // a movement key takes the hero back from auto-travel
        _rec.SetInput(LoadInput(new MoveInput(dx, dy, Actions.HeldLoose("world.sprint"), HeroWalkInput())));
        var pan = new Vector2(
            (Actions.HeldLoose("world.camera_right") ? 1 : 0) - (Actions.HeldLoose("world.camera_left") ? 1 : 0),
            (Actions.HeldLoose("world.camera_down") ? 1 : 0) - (Actions.HeldLoose("world.camera_up") ? 1 : 0));
        // the same screen speed in every band: 10 m/s at Street, 40 m/s at Field, 160 m/s at Command
        float speed = _band switch { Band.Command => 16f, Band.Field => 4f, _ => 1f };
        if (pan != Vector2.Zero) _pan += pan * (float)(delta * 10 * Tile * speed);
        ClampPan();
    }

    void ScriptedWalk()
    {
        // benchmark: sprint around the plaza in a square, torch on
        int phase = (int)(_benchT / 1.5) % 4;
        var (dx, dy) = phase switch { 0 => (1, 0), 1 => (0, 1), 2 => (-1, 0), _ => (0, -1) };
        _rec.SetInput(new MoveInput(dx, dy, true));
        if (_frames == 2) _rec.SetTorch(true);
    }

    Vector2 _lastFeet; float _stridePhase; double _idleT;
    /// <summary>Walk or jog frames from distance travelled (so feet match the ground), idle frames from time.</summary>
    void AnimateHero(HeroSnapshot h, Vector2 feet)
    {
        float moved = h.Moving && h.Z == h.PrevZ ? feet.DistanceTo(_lastFeet) : 0f;
        _lastFeet = feet;
        if (moved > 0f)
        {
            bool walk = h.Mode == MoveMode.Walk;
            _stridePhase += moved / (walk ? 46f : 66f); // pixels per full 4-frame cycle (two steps)
            _heroSprite.Pose(h.Facing, walk ? "walk" : "jog", (int)(_stridePhase * 4f));
        }
        else
        {
            _stridePhase = 0f;
            _idleT += GetProcessDeltaTime();
            _heroSprite.Pose(h.Facing, "idle", (int)(_idleT / 0.7) % 2);
        }
    }

    void Render(float alpha)
    {
        var s = _world.Snapshot;
        var h = s.Hero;
        if (h.Z != h.PrevZ && _viewZ == h.PrevZ) { _viewZ = h.Z; RebuildLevel(); } // the view follows the hero between levels
        float fx = h.PrevX.ToFloat() + (h.X.ToFloat() - h.PrevX.ToFloat()) * alpha;
        float fy = h.PrevY.ToFloat() + (h.Y.ToFloat() - h.PrevY.ToFloat()) * alpha;
        if (h.Z != h.PrevZ) { fx = h.X.ToFloat(); fy = h.Y.ToFloat(); }
        StreamAhead(Math.Max(0.0, (_world.Streamer?.Settings.BudgetMs ?? 0) - _stepMsFrame));
        SyncTiles(DrawBudgetMs);
        var feet = new Vector2(Mathf.Round((fx + s.OriginX) * Tile), Mathf.Round((fy + s.OriginY) * Tile));
        _heroSprite.Position = feet;
        _heroSprite.Visible = h.Z == _viewZ;
        AnimateHero(h, feet);
        RideRender(h, feet);
        TravelRender();
        _torchSprite.Visible = h.Torch;
        _torchSprite.Position = new Vector2(h.Facing == Facing.West ? -16 : 6, 0);

        bool far = _band >= Band.Field && _far != null;
        _street.Visible = !far;
        if (_far != null) _far.Visible = far;
        _zoom = _band == Band.Close ? 2 : 1;
        _cam.Zoom = new Vector2(_zoom, _zoom);
        if (!far) _cam.Position = new Vector2(Mathf.Round(feet.X + _pan.X), Mathf.Round(feet.Y - 16 + _pan.Y));
        else
        {
            // Field (8 px/m) and Command (2 px/m): the camera moves over the cached chunk images; metres in, pixels out
            bool cmd = _band == Band.Command; float ppm = cmd ? 2f : 8f;
            var heroM = new Vector2(feet.X, feet.Y) / Tile;
            var centre = heroM + _pan / Tile;
            _cam.Position = (centre * ppm).Round();
            _people.Clear();
            _people.Add(new FarPerson(heroM.X, heroM.Y, "blue", true));
            _far!.Refresh(centre, new Vector2(_vp.Size.X, _vp.Size.Y), cmd, _people);
        }
        _ambient.Visible = far;
        if (far) _ambient.Size = new Vector2(_vp.Size.X, _vp.Size.Y);

        SyncCaravans(s);
        SyncPeople(alpha);
        if (s.Tick != _shownTick) { _shownTick = s.Tick; UpdateWorld(s); UpdateLight(s); UpdateHud(s); }
        _wover.Update(GetProcessDeltaTime(), _wx, _vp.Size, _band == Band.Close ? 2 : 1);
    }

    /// <summary>
    /// The LODBridge for caravans: the bubble around the hero holds the caravans of the abstract world whose road position comes within 150 m (and keeps them to 200 m); each is drawn from its
    /// record's own position function, so what is on screen is where the abstract tick says the caravan is.
    /// </summary>
    /// <summary>Lead positions of the caravans on the road, for the world map.</summary>
    IReadOnlyList<(long XMm, long YMm)> CaravanMarksNow()
    {
        var list = new List<(long, long)>();
        if (Runtime is not { } rt) return list;
        long now = _world.Clock.Now.Seconds;
        foreach (var c in rt.Caravans) { var p = CaravanMotion.PositionOf(c, now); list.Add((p.XMm, p.YMm)); }
        return list;
    }

    /// <summary>Where the hero stands, in continent millimetres: the centre of the bubble the people bridge fills.</summary>
    (long X, long Y)[] LodCentres()
    {
        var home = Runtime!.State.HomeRegion; var (wx, wy) = _world.WorldCell;
        return [((home.OriginXM + (long)wx) * 1000 + 500, (home.OriginYM + (long)wy) * 1000 + 500)];
    }

    /// <summary>The loaded window as walkable ground for the people (continent metres), or null in the hamlet.</summary>
    GridGround? LodGround()
    {
        if (Runtime is not { } rt || _world.Streamer == null) return null;
        var home = rt.State.HomeRegion;
        return new GridGround(_world.Grid, _world.Pathfinder, (int)(home.OriginXM + (long)_world.OriginX), (int)(home.OriginYM + (long)_world.OriginY));
    }

    /// <summary>The 20 Hz step of the promoted people: each follows its plan, and those who stayed beyond 200 m for 5 real seconds are given back to the records (fold-back, at the next barrier).</summary>
    void StepPeople()
    {
        if (Runtime is not { } rt || _world.Streamer == null) return;
        FeedFollowers(rt);
        if (_lod.Count == 0) return;
        _lod.Step(_world.Clock.Now.Seconds, LodGround(), LodCentres());
        var folds = _lod.TakeFolds();
        if (folds.Count > 0) _ = Watch(RuntimePeople.FoldAsync(rt, folds), "fold-back");
        var exposure = _lod.TakeExposureDeaths();
        if (exposure.Count > 0) _ = Watch(RuntimePeople.ExposureDeathsAsync(rt, exposure, _world.Clock.Now.Seconds), "exposure");
    }

    /// <summary>The party walks behind the hero (Phase 3 cleanup B, item 3): the hero's trail is fed every tick, the squad's members who follow are promoted into the bubble and walk it, those told to wait stay where they stand.</summary>
    void FeedFollowers(WorldRuntime rt)
    {
        var home = rt.State.HomeRegion; var (hx, hy) = _world.WorldMm;
        _lod.NoteHero(home.OriginXM * 1000 + hx, home.OriginYM * 1000 + hy, _world.Snapshot.Hero.Z);
        _lod.FollowMinStepMm = 150L * Math.Max(1, (int)_world.Clock.Speed);
        if (_heroLink?.View is { } v) _lod.SetFollowers([.. v.Squad.Where(m => m.Active).Select(m => (m.Person, m.Order == Ravensguard.Sim.Trade.SquadOrder.Follow))]);
    }

    /// <summary>Per frame: asks the tick worker every half second (10 ticks) who the bubble should hold, takes the answer, and promotes up to 50 of them nearest first.</summary>
    void PumpPeople()
    {
        if (Runtime is not { } rt || _world.Streamer == null) return;
        long now = _world.Clock.Now.Seconds;
        if (_lodScan is { IsCompleted: true } done)
        {
            _lodScan = null;
            if (done.IsCompletedSuccessfully) _lod.Enqueue(done.Result);
            else if (!_lodFaultShown) { _lodFaultShown = true; GD.PushWarning("people bridge scan failed: " + done.Exception?.GetBaseException().Message); }
        }
        if (_lodScan == null && _world.Tick - _lodScanTick >= 10 && _world.Clock.Speed != SimSpeed.Paused)
        {
            _lodScanTick = _world.Tick;
            _lodScan = RuntimePeople.ScanAsync(rt, _lod.MakeScanInput(LodCentres(), now));
        }
        if (_clothes != null && _lod.Clothing == null) { var home = rt.State.HomeRegion; _lod.AttachClothing(_clothes.Book, _clothes.Ambient(_wfeed, home.OriginXM, home.OriginYM)); }
        if (_lod.Queued > 0) _lod.Promote(now, LodGround());
    }

    static readonly Ravensguard.Sim.Core.EntityId HeroId = Ravensguard.Sim.Economy.EconomyState.HeroOwner;

    /// <summary>Every second of real time: hands the people's claims and the hero's to the tick worker, gets copies of the jobs back, and sends the live ones to the people who work them. A hero who strays far from the work of his job loses it.</summary>
    void PumpJobs()
    {
        if (Runtime is not { } rt || _world.Streamer == null) return;
        long now = _world.Clock.Now.Seconds;
        if (_jobTask is { IsCompleted: true } done)
        {
            _jobTask = null;
            if (done.IsCompletedSuccessfully)
            {
                _jobSnap = done.Result; _lod.SetJobs(done.Result.Live, done.Result.Open);
                if (done.Result.LostJob is { } lost) LoseJob(lost, now);
            }
            else if (!_jobFaultShown) { _jobFaultShown = true; GD.PushWarning("job step failed: " + done.Exception?.GetBaseException().Message); }
        }
        // every JobStrideSec game seconds (never every so many ticks: the stride in game time is the same at 1x and 8x, and the away rule is the sim's, keyed on game seconds)
        if (_jobTask == null && (now - _jobSec >= JobStrideSec) && _world.Clock.Speed != SimSpeed.Paused)
        {
            _jobSec = now;
            var home = rt.State.HomeRegion; var c = LodCentres()[0];
            var sight = new HeroSight(c.X - home.OriginXM * 1000, c.Y - home.OriginYM * 1000, Ob is { HasFirstJob: true } first ? first.FirstJob : null);   // the mentor's first contract is forgiving: it is not lost by lagging behind its carrier
            _jobTask = RuntimeJobs.StepAsync(rt, now, _lod.TakeClaims().ToList(), sight);
        }
    }

    const long JobStrideSec = 30;

    /// <summary>The sim's away rule said the hero has lost his job (game minutes away from its work): the abandon goes through the hero desk, recorded like any command.</summary>
    void LoseJob(Ravensguard.Sim.Core.EntityId job, long now)
    {
        if (_heroLink == null) return;
        _heroLink.Issue(new HeroCommand { Kind = HeroCommandKind.JobAbandon, IssuedSec = now, Job = job, Tag = "away", Origin = CommandOrigin.Human });
        _router?.ShowToast(L.T("JOBS_LOST"), 4f);
    }

    JobPanelData WorkData()
    {
        var d = new JobPanelData { NowSec = _world.Clock.Now.Seconds };
        if (Runtime is not { } rt) return d;
        var home = rt.State.HomeRegion; var c = LodCentres()[0];
        d.HeroX = c.X - home.OriginXM * 1000; d.HeroY = c.Y - home.OriginYM * 1000;
        d.Mine = _jobSnap?.Live.FirstOrDefault(j => j.Worker == HeroId);
        var near = _lod.People.Select(p => (P: p, D: PathPlan.Dist(p.XMm - c.X, p.YMm - c.Y))).Where(t => t.D < 4000 && t.P.Z == _world.Snapshot.Hero.Z).OrderBy(t => t.D).FirstOrDefault();
        if (near.P.Name != null) { d.NearName = near.P.Name; d.NearWhy = _lod.Why(near.P.Id); }
        var region = rt.State.Population.Region; int best = -1; long bd = long.MaxValue;
        for (int i = 0; i < region.Settlements.Count; i++) { long dd = PathPlan.Dist(region.Settlements[i].CenterX * 1000L - d.HeroX, region.Settlements[i].CenterY * 1000L - d.HeroY); if (dd < bd) { bd = dd; best = i; } }
        if (best >= 0 && bd < 400_000) { d.PlaceName = region.Settlements[best].Name; d.Board = region.Settlements[best].Class == Ravensguard.Sim.WorldGen.Region.SettlementClass.Town; d.Open = _jobSnap?.Open.Where(j => j.Settlement == best).ToList() ?? []; }
        return d;
    }

    void OpenWork()
    {
        if (_router == null || Runtime == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        Raise("job_board_seen");
        _router.Push(new JobPanel(_router, WorkData, TakeJob, AbandonJob) { ShotPath = _opt.Panel == "jobs" ? _opt.ShotPath : null });
    }

    void TakeJob(Ravensguard.Sim.Jobs.Job j)
    {
        var d = WorkData();
        _heroLink?.Issue(new HeroCommand { Kind = HeroCommandKind.JobClaim, IssuedSec = _world.Clock.Now.Seconds, Job = j.Id, X = d.HeroX, Y = d.HeroY, Origin = CommandOrigin.Human });
    }

    void AbandonJob(Ravensguard.Sim.Jobs.Job j) => _heroLink?.Issue(new HeroCommand { Kind = HeroCommandKind.JobAbandon, IssuedSec = _world.Clock.Now.Seconds, Job = j.Id, Origin = CommandOrigin.Human });

    void SyncPeople(float alpha)
    {
        if (_lodSprites == null || Runtime is not { } rt) return;
        _lodSprites.Sync(_lod.People, rt.State.HomeRegion.OriginXM, rt.State.HomeRegion.OriginYM, alpha, GetProcessDeltaTime(), _band < Band.Field, Actions.Held("world.nameplates"), _heroSprite.Position, _viewZ);
    }

    void SyncCaravans(LiveSnapshot s)
    {
        if (_caravanSprites == null) return;
        if (Runtime is not { } rt) return;
        var home = rt.State.HomeRegion;
        var (wx, wy) = _world.WorldCell;
        var live = _caravanBridge.Update(rt.Caravans, (home.OriginXM + (long)wx) * 1000, (home.OriginYM + (long)wy) * 1000, s.Time.Seconds);
        _caravanSprites.Sync(live, home.OriginXM, home.OriginYM, GetProcessDeltaTime(), _band < Band.Field);
    }

    /// <summary>The hero's chunks stream around the hero; a panned camera can look past them. Chunks under (and just ahead of) the camera in the window load now, the nearest first,
    /// within the streamer's own per-frame budget less what the sim step already spent (a chunk under the camera always loads, at least one per frame).</summary>
    void StreamAhead(double budgetMs)
    {
        var st = _world.Streamer; var win = _world.Window;
        if (st == null || win == null || _band >= Band.Field) return;
        int ccx = Div((int)Math.Floor(_cam.Position.X / Tile), 32), ccy = Div((int)Math.Floor(_cam.Position.Y / Tile), 32);
        // look ahead in the direction the camera sits from the hero as well
        int ax = Math.Sign(_pan.X), ay = Math.Sign(_pan.Y);
        long t0 = Stopwatch.GetTimestamp(); int loaded = 0;
        for (int r = 0; r <= 1; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int cx = ccx + dx + (r == 1 ? ax : 0), cy = ccy + dy + (r == 1 ? ay : 0);
                    if (!win.Contains(cx, cy) || st.IsLoaded(cx, cy)) continue;
                    if (loaded > 0 || r > 0)
                        if (Stopwatch.GetElapsedTime(t0).TotalMilliseconds >= budgetMs) return;
                    st.LoadSync(cx, cy);
                    loaded++;
                }
    }

    /// <summary>Once per sim tick: the weather at the hero, the season and snow state for the shaders, the props' season sprites.</summary>
    void UpdateWorld(LiveSnapshot s)
    {
        var t = s.Time;
        float wx = s.OriginX + s.Hero.X.ToFloat(), wy = s.OriginY + s.Hero.Y.ToFloat();
        _wx = _wfeed.Sample(t, (int)wx, (int)wy);
        _wxDim = WeatherOverlay.Dim(_wx);
        _seasons.Apply(SeasonState.Of(t, _wx.SnowCover, _wx.Freeze, _wx.Wet));
        StepHeroBody(s);
        RefreshProps();
    }

    /// <summary>The hero's clothes and core temperature, once per sim tick (the model itself runs once per game minute): where they stand (under a roof, by a lit hearth) and what they are doing.</summary>
    void StepHeroBody(LiveSnapshot s)
    {
        if (_clothes == null || _hero == null) return;
        long sec = s.Time.Seconds;
        EnsureHeroClothes(sec);                      // the hero wears the inventory's worn clothes (trade spec 3.2), not a wardrobe of their own
        var c = s.Hero.Cell;
        bool indoors = _world.Lights.IsIndoor(c.X, c.Y, c.Z);
        // a hearth is a static light: strong light on a roofed cell with no torch of the hero's own is a fire within reach
        bool fire = CampFireNear() || indoors && !s.Hero.Torch && s.Light != null && c.X >= 0 && c.Y >= 0 && c.X < s.Light.Width && c.Y < s.Light.Height && c.Z >= s.Light.MinZ && c.Z <= s.Light.MaxZ && s.Light.Point(c.X, c.Y, c.Z) >= 120;
        var act = !s.Hero.Moving ? Ravensguard.Sim.Clothing.Activity.Rest : s.Hero.Mode == MoveMode.Sprint ? Ravensguard.Sim.Clothing.Activity.Sprint : Ravensguard.Sim.Clothing.Activity.Walk;
        // P4 close-out: below ground the body feels the site's own still, dry air (the constants' underground_temp_c, 8 C) under rock, not the weather above
        if (Underground && _source?.Content != null)
        {
            int under = Ravensguard.Sim.Sites.SiteWiring.ContentOf(_source.Content).K.UndergroundTempC;
            _clothes.StepHero(sec, _wx with { TempC = under, Precip = 0, PrecipMmH = 0, Snowing = false, WindMps = 0, Wet = 0, Freeze = 0 }, true, false, act);
        }
        else _clothes.StepHero(sec, _wx, indoors, fire, act);
        // the hero is drawn with the cloak and hood they actually wear (the rest of the figure is the hero's own art until the character screen dresses them piece by piece)
        var look = _clothes.LookOf(_clothes.Hero, Ravensguard.Sim.People.Occupation.None);
        if (look.Key != _heroLookKey) { _heroLookKey = look.Key; RebuildHeroSprite(look); }
    }

    void RebuildHeroSprite(ClothingFeed.Look look)
    {
        var layers = new System.Collections.Generic.List<string> { "body", "hose", "linen", "gambeson" };
        foreach (var l in new[] { "cloak", "hood" }) if (System.Array.IndexOf(look.Layers, l) >= 0) layers.Add(l);
        layers.Add("sword");
        var neu = new RigActor(_rig, look.Layers.Length > 0 && System.Array.IndexOf(look.Layers, "cloak") >= 0 ? "traveller" : "hero", layers);
        var tints = new System.Collections.Generic.Dictionary<string, Color>();
        foreach (var l in new[] { "cloak", "hood" }) if (look.Tints.TryGetValue(l, out var c)) tints[l] = c;
        neu.Dress(tints);
        var old = _heroSprite;
        var np = old.GetNodeOrNull("Nameplate");
        if (_torchSprite.GetParent() == old) old.RemoveChild(_torchSprite);
        if (np != null) old.RemoveChild(np);
        _ysort.AddChild(neu); neu.Position = old.Position;
        neu.AddChild(_torchSprite); if (np != null) neu.AddChild(np);
        _heroSprite = neu; old.QueueFree();
        EnsureSingleNameplate();
    }

    /// <summary>
    /// The hero's name label is exactly one node under the hero sprite. A rebuild of the sprite (a new cloak or hood) moves the label across, and a second label from an earlier build, or one parked on the
    /// retiring sprite, drew the name twice a pixel or two apart: every Label under the sprite beyond the first "Nameplate" goes, and the label stays on the whole pixel.
    /// </summary>
    void EnsureSingleNameplate()
    {
        bool seen = false;
        foreach (var n in _heroSprite.GetChildren().OfType<Label>().ToList())
        {
            if (!seen) { seen = true; n.Name = "Nameplate"; n.Position = new Vector2(-60, -84); continue; }
            _heroSprite.RemoveChild(n); n.QueueFree();
        }
    }

    /// <summary>Ambient tint of the open air: cool moonlight to warm daylight, an orange cast while the sun is low, then the weather's dimming (cloud, rain, fog).</summary>
    (float R, float G, float B) AmbientTint(LiveSnapshot s)
    {
        float sf = s.Ambient.Sun / 255f;
        float tr = Mathf.Lerp(0.50f, 1.0f, sf), tg = Mathf.Lerp(0.62f, 0.98f, sf), tb = Mathf.Lerp(1.0f, 0.92f, sf);
        if (s.Ambient.Phase is DayPhase.Dawn or DayPhase.Dusk)
        {
            float k = 4f * sf * (1f - sf) * 0.55f;
            tr = Mathf.Lerp(tr, 1.0f, k); tg = Mathf.Lerp(tg, 0.70f, k); tb = Mathf.Lerp(tb, 0.50f, k);
        }
        float d = _wxDim;
        return (tr * d, tg * d, tb * Math.Min(1f, d * 1.05f));
    }

    // ------------------------------------------------------------------ light overlay

    void UpdateLight(LiveSnapshot s)
    {
        var g = _world.Grid; int w = _lightW, h = _lightH, z = _viewZ, gw = g.Width;
        var f = s.Light;
        int hx = s.Hero.CellX, hy = s.Hero.CellY;
        // the patch of the light grid shown: all of it for the hamlet, a hero-centred 128 x 96 for a streamed window
        int px0 = _world.Streamer != null ? Math.Clamp(hx - w / 2, 0, Math.Max(0, g.Width - w)) : 0;
        int py0 = _world.Streamer != null ? Math.Clamp(hy - h / 2, 0, Math.Max(0, g.Height - h)) : 0;
        _lightSprite.Position = new Vector2((px0 + s.OriginX) * Tile, (py0 + s.OriginY) * Tile);
        double hfx = s.Hero.X.ToDouble(), hfy = s.Hero.Y.ToDouble();
        var (tr, tg, tb) = AmbientTint(s);
        _ambientMat.SetShaderParameter("tint", new Color(Math.Max(tr, 0.16f), Math.Max(tg, 0.17f), Math.Max(tb, 0.24f)));
        _ground.Color = new Color(0.36f * Math.Max(tr, 0.16f), 0.43f * Math.Max(tg, 0.17f), 0.31f * Math.Max(tb, 0.24f));
        const float wr = 1.0f, wg = 0.62f, wb = 0.30f; // firelight
        int viewZ = z;
        var buf = _lightBytes;
        int baseOff = f.LevelOffset(viewZ);
        var amb = f.AmbientSpan; var pt = f.PointSpan;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = baseOff + (y + py0) * gw + x + px0, o = (y * w + x) * 4;
                int a = amb[i], p = pt[i], total = Math.Min(255, a + p);
                double dx = x + px0 + 0.5 - hfx, dy = y + py0 + 0.5 - hfy;
                int r = LightModel.VisionRadius(total);
                if (a >= 128) r = Math.Max(r, 96); // open daylight: distance hides nothing outdoors, so no dark wedge at the edge of a panned view
                double d2 = dx * dx + dy * dy;
                float vis = 1f;
                if (d2 > (double)r * r) vis = 0f;
                else if (d2 > (double)(r - 2) * (r - 2) && r > 2) vis = (float)((r - Math.Sqrt(d2)) / 2.0);
                float af = a / 255f, pf = p / 255f;
                float cr = tr * af + wr * pf, cg = tg * af + wg * pf, cb = tb * af + wb * pf;
                float floor = viewZ < 0 ? 0.015f : 0.03f;
                buf[o] = Tone[(int)(Mathf.Clamp(Math.Max(cr * vis, floor), 0f, 1f) * 255f)];
                buf[o + 1] = Tone[(int)(Mathf.Clamp(Math.Max(cg * vis, floor), 0f, 1f) * 255f)];
                buf[o + 2] = Tone[(int)(Mathf.Clamp(Math.Max(cb * vis, floor * 1.4f), 0f, 1f) * 255f)];
                buf[o + 3] = 255;
            }
        }
        _lightTex.Update(Image.CreateFromData(w, h, false, Image.Format.Rgba8, buf));
    }

    static byte[] BuildTone()
    {
        var t = new byte[256];
        for (int i = 0; i < 256; i++) t[i] = (byte)Math.Clamp((int)Math.Round(Math.Pow(i / 255.0, 0.72) * 255.0), 0, 255);
        return t;
    }

    // ------------------------------------------------------------------ input

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (_router is { HasModal: true }) return; // the pause menu (or any dialog) owns the keys; Esc is the router's (it closes the menu) and F8 is the App's
        if (AftermathInput(e)) { GetViewport().SetInputAsHandled(); return; }   // P4-D hook: the defeat and captivity screens' guard, the yield verb
        if (SuccessionInput(e)) { GetViewport().SetInputAsHandled(); return; }  // P4 close-out: the succession screen's guard (StreetView.Succession.cs)
        if (ObInput(e)) { GetViewport().SetInputAsHandled(); return; }
        if (BuildInput(e)) { GetViewport().SetInputAsHandled(); return; }       // P5-C: build mode's keys and clicks (StreetView.BuildMode.cs)
        if (e is InputEventKey { Pressed: true } && Actions.Pressed(e, "world.back") && (_campOpen || _travel?.Journey is { State: Ravensguard.Sim.Travel.JourneyState.Travelling or Ravensguard.Sim.Travel.JourneyState.Paused }) && TravelInput(e)) { GetViewport().SetInputAsHandled(); return; }
        if (_router != null && Actions.Pressed(e, "world.back"))
        {
            // Esc belongs to this view: the pause menu. With the menu already open the router closes it (Resume) and this view stays.
            if (_router.HasModal) return; // the router closes the menu (and Resume follows)
            if (!_leaving) OpenPauseMenu();
            GetViewport().SetInputAsHandled(); return;
        }
        if (e is InputEventMouseButton { Pressed: true, ShiftPressed: true } wheel && (wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown))
        {
            ChangeView(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
            AcceptEvent(); return;
        }
        if (e is InputEventMouseButton { Pressed: true } click && Actions.Pressed(e, "world.act")) // exact: Shift+click is reserved for selection
        {
            ClickMove(click.Position - GlobalPosition);
            GetViewport().SetInputAsHandled(); return;
        }
        if (RideInput(e) || TravelInput(e)) { GetViewport().SetInputAsHandled(); return; }
        if (Actions.Pressed(e, "world.pause")) TogglePause();
        else if (Actions.Pressed(e, "world.speed_1")) _rec.SetSpeed(SimSpeed.X1);
        else if (Actions.Pressed(e, "world.speed_2")) _rec.SetSpeed(SimSpeed.X2);
        else if (Actions.Pressed(e, "world.speed_4")) _rec.SetSpeed(SimSpeed.X4);
        else if (Actions.Pressed(e, "world.speed_8")) _rec.SetSpeed(SimSpeed.X8);
        else if (Actions.Pressed(e, "world.torch")) _rec.ToggleTorch();
        else if (Actions.Pressed(e, "world.level_up")) ChangeView(1);
        else if (Actions.Pressed(e, "world.level_down")) ChangeView(-1);
        else if (Actions.Pressed(e, "world.zoom_in")) SetBand(_band - 1);
        else if (Actions.Pressed(e, "world.zoom_out")) SetBand(_band + 1);
        else if (Actions.Pressed(e, "world.map")) OpenMap();
        else if (Actions.Pressed(e, "world.build")) OpenBuildMode();             // P5-C: B opens build mode
        else if (Actions.Pressed(e, "world.panel_character")) OpenCharacter();
        else if (Actions.Pressed(e, "world.panel_codex")) OpenCodex();
        else if (Actions.Pressed(e, "world.make")) OpenCrafting();
        else if (Actions.Pressed(e, "world.panel_inventory")) OpenInventory();
        else if (Actions.Pressed(e, "world.panel_squad")) OpenSquad();
        else if (Actions.Pressed(e, "world.autopilot")) OpenAutopilot();
        else if (Actions.Pressed(e, "world.panel_journal")) OpenJournal();
        else if (Actions.Pressed(e, "world.panel_ledger")) OpenLedger();             // P5-F: L opens the Ledger
        else if (Actions.Pressed(e, "world.combat_log")) OpenCombatLog();         // P4-D: F7 and Shift+F7 outside a fight (the last fight's log and report)
        else if (Actions.Pressed(e, "world.last_report")) OpenLastReport();
        else if (Actions.Pressed(e, "world.context_menu")) OpenQMenu();
        else if (Actions.Pressed(e, "world.quicksave")) QuickSave();
        else if (Actions.Pressed(e, "world.quickload")) QuickLoad();
        else if (Actions.Pressed(e, "world.camera_home")) { _pan = Vector2.Zero; if (_viewZ != _world.Snapshot.Hero.Z) { _viewZ = _world.Snapshot.Hero.Z; RebuildLevel(); } }
        else if (e is InputEventKey && Actions.Pressed(e, "world.act")) { if (ApActive && _heroLink != null && NearPerson() != null) ApTakeControl(L.T("AP_TOOK_CONTROL")); if (!TryRescueAct() && !TryTalkNear() && !TryPracticeYard() && !TryEnterSite()) _rec.UsePortal(); }   // P4-H: TryEnterSite (StreetView.Crawl.cs); P4 close-out: TryRescueAct (StreetView.Captivity.cs)
        else return;
        GetViewport().SetInputAsHandled();
    }

    /// <summary>One step along Close, Street, Field, Command. Pixel art only zooms at whole steps, so each band is its own scale.</summary>
    void SetBand(Band b)
    {
        b = (Band)Math.Clamp((int)b, (int)Band.Close, (int)Band.Command);
        if (b == _band) return;
        _band = b;
        ClampPan();
    }

    void ClampPan()
    {
        float lim = _band switch { Band.Command => 1500f, Band.Field => 400f, _ => 24f } * Tile;
        _pan = _pan.Clamp(new Vector2(-lim, -lim), new Vector2(lim, lim));
    }

    // ------------------------------------------------------------------ pause menu (Esc)

    bool _menuPaused, _toSettings, _leaving;

    SimSpeed EffectiveSpeed => _menuPaused ? _speedBeforePause : _world.Clock.Speed;

    void OpenPauseMenu()
    {
        if (_router == null) return;
        if (_world.Clock.Speed != SimSpeed.Paused) { _speedBeforePause = _world.Clock.Speed; _rec.SetSpeed(SimSpeed.Paused); _menuPaused = true; }
        var buttons = new List<(string, Action?)> { (L.T("PAUSE_RESUME"), null) };
        if (Run != null) buttons.Add((L.T("PAUSE_SAVE"), () => StartSave(SaveKind.Manual)));
        buttons.Add((L.T("PAUSE_SETTINGS"), () => { _toSettings = true; _router.Push(new SettingsScreen(_router)); }));
        buttons.Add((L.T("PAUSE_QUIT"), QuitToMenu));
        string body = Run is { Ironman: true } ? L.T("PAUSE_BODY_IRONMAN") : L.T("PAUSE_BODY");
        _router.ShowModal(L.T("PAUSE_TITLE"), body, () => CallDeferred(nameof(ResumeAfterMenu)), buttons.ToArray());
    }

    /// <summary>Runs after the pause modal closed: Resume, Save and Esc all end here; Settings and Quit keep the sim paused (Settings until this view shows again).</summary>
    void ResumeAfterMenu()
    {
        if (!_menuPaused || _toSettings || _leaving || (_router?.HasModal ?? false)) return;
        _menuPaused = false;
        _rec.SetSpeed(_speedBeforePause);
    }

    // ------------------------------------------------------------------ saving (plan 2.11)
    // The snapshot is taken here on the main thread (a few ms); encoding and the file write run on a worker, and PollSave reports the result.

    /// <summary>Real seconds between autosaves (plan 2.11: every 15 minutes).</summary>
    public double AutosaveSeconds { get; set; } = 15 * 60;
    /// <summary>The text of the last failed save (null when the last save worked); for the self-test.</summary>
    public string? LastSaveError { get; private set; }
    /// <summary>True while a save is being written.</summary>
    public bool Saving => _saveTask != null || _saveQueue.Count > 0;

    sealed record SaveJob(SaveKind Kind, bool Quiet, Action? Then, Action<string>? OnFail);
    Task<string>? _saveTask;
    SaveJob? _saveJob;
    readonly Queue<SaveJob> _saveQueue = new();
    double _sinceSave;

    /// <summary>Starts a save of the run: a snapshot now, the write in the background. <paramref name="then"/> runs after it succeeded; <paramref name="onFail"/> replaces the default error dialog.
    /// Ironman runs always write their one Ironman slot. False when this view has no run to save.</summary>
    public bool StartSave(SaveKind kind, bool quiet = false, Action? then = null, Action<string>? onFail = null)
    {
        if (Run == null || _world.Streamer == null) return false;
        var job = new SaveJob(kind, quiet, then, onFail);
        if (_saveTask != null) { _saveQueue.Enqueue(job); return true; }
        Begin(job);
        return true;
    }

    void Begin(SaveJob job)
    {
        _sinceSave = 0; _lastSaveReal = _obReal;
        if (job.Kind == SaveKind.Autosave) { _rec.Rebase(); Diagnostics.GameDiagnostics.NoteAutosaveBegin(_rec.Commands.BaseTick); } // the replay trace restarts at this autosave (testers spec 3.6)
        try
        {
            // the hero's state now (main thread), the world's records copied on the tick worker between two hours, then encoded and written on the thread pool
            var run = Run!;
            var task = run.SnapshotAsync(_world).ContinueWith(c => run.WriteAsync(c.GetAwaiter().GetResult(), job.Kind), TaskScheduler.Default).Unwrap();
            task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); // observed: PollSave shows the error (or nobody is left to, when the view was left)
            _saveTask = task; _saveJob = job;
        }
        catch (Exception ex) { SaveFailed(job, ex.Message); }
    }

    void PollSave()
    {
        if (_saveTask is { IsCompleted: true } t && _saveJob is { } job)
        {
            _saveTask = null; _saveJob = null;
            if (t.IsFaulted || t.IsCanceled) SaveFailed(job, t.Exception?.GetBaseException().Message ?? "cancelled");
            else
            {
                LastSaveError = null;
                if (job.Kind == SaveKind.Autosave) Diagnostics.GameDiagnostics.NoteAutosaveDone(t.Result);
                if (job.Kind == SaveKind.Autosave && !job.Quiet) _router?.ShowToast(L.T("SAVE_AUTOSAVED"));
                else if (!job.Quiet) _router?.ShowToast(L.F("PAUSE_SAVED", System.IO.Path.GetFileName(t.Result)));
                job.Then?.Invoke();
            }
        }
        if (_saveTask == null && _saveQueue.Count > 0) Begin(_saveQueue.Dequeue());
    }

    void SaveFailed(SaveJob job, string message)
    {
        LastSaveError = message;
        GD.PushWarning("save failed: " + message);
        if (job.OnFail != null) { job.OnFail(message); return; }
        if (job.Kind == SaveKind.Autosave) { _router?.ShowToast(L.F("SAVE_AUTOSAVE_FAILED", message), 5f); return; }
        _router?.ShowModal(L.T("PAUSE_SAVE_FAILED_TITLE"), L.F("PAUSE_SAVE_FAILED", message), ResumeAfterMenuDeferred, (L.T("UI_OK"), null));
    }

    void QuickSave()
    {
        if (_router == null) return;
        if (Run == null || _world.Streamer == null) _router.ShowToast(L.T("SAVE_NO_RUN"));
        else if (Run.Ironman) _router.ShowToast(L.T("SAVE_QUICK_IRONMAN"));
        else StartSave(SaveKind.Manual);
    }

    /// <summary>F9: loads the run's manual slot (what F5 and the pause menu's Save write), never in Ironman. The run in play is left as it is; the load screen takes its place.</summary>
    void QuickLoad()
    {
        if (_router == null || _leaving) return;
        if (Run == null || _world.Streamer == null) { _router.ShowToast(L.T("SAVE_NO_RUN")); return; }
        if (Run.Ironman) { _router.ShowToast(L.T("SAVE_QUICKLOAD_IRONMAN")); return; }
        if (Saving) { _router.ShowToast(L.T("SAVE_QUICKLOAD_BUSY")); return; }
        var entry = Run.ManualEntry();
        if (entry == null) { _router.ShowToast(L.T("SAVE_QUICKLOAD_NONE")); return; }
        _leaving = true; // no autosave on the way out: the run being left is the one being replaced
        _router.ReturnToMainMenu();
        RunLoadScreen.Open(_router, entry);
    }

    /// <summary>Self-test hook: the F9 path without the key event.</summary>
    public void DebugQuickLoad() => QuickLoad();

    /// <summary>The window is closing: write an autosave (Ironman: its slot) now, so the run is not lost. Waits for it, a second or two at most.</summary>
    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest || Run == null || _world.Streamer == null || _leaving) return;
        try
        {
            if (_saveTask != null) { try { _saveTask.GetAwaiter().GetResult(); } catch { /* the failure was reported; this save replaces it */ } }
            Run.WriteAsync(Run.SnapshotAsync(_world).GetAwaiter().GetResult(), SaveKind.Autosave).GetAwaiter().GetResult();
        }
        catch (Exception ex) { GD.PushWarning("save on quit failed: " + ex.Message); }
    }

    void QuitToMenu()
    {
        if (_router == null) return;
        if (Run == null) { LeaveToMenu(); return; }
        if (Run.Ironman)
        {
            // an Ironman run saves itself on the way out; if that fails, the player decides
            _leaving = true; // the sim stays paused while it is written
            StartSave(SaveKind.Manual, quiet: true, then: LeaveToMenu, onFail: msg =>
            {
                _leaving = false;
                _router.ShowModal(L.T("PAUSE_SAVE_FAILED_TITLE"), L.F("PAUSE_SAVE_FAILED", msg), ResumeAfterMenuDeferred, (L.T("PAUSE_QUIT_ANYWAY"), LeaveToMenu), (L.T("UI_CANCEL"), null));
            });
            return;
        }
        _router.ShowModal(L.T("PAUSE_QUIT_TITLE"), L.T("PAUSE_QUIT_BODY"), ResumeAfterMenuDeferred,
            (L.T("PAUSE_SAVE_QUIT"), () =>
            {
                _leaving = true;
                StartSave(SaveKind.Manual, quiet: true, then: LeaveToMenu, onFail: msg =>
                {
                    _leaving = false;
                    _router.ShowModal(L.T("PAUSE_SAVE_FAILED_TITLE"), L.F("PAUSE_SAVE_FAILED", msg), ResumeAfterMenuDeferred, (L.T("UI_OK"), null));
                });
            }),
            (L.T("PAUSE_QUIT_NOSAVE"), () => { StartSave(SaveKind.Autosave, quiet: true); LeaveToMenu(); }), // the manual slot is left alone; the rotating autosave keeps the run safe
            (L.T("UI_CANCEL"), null));
    }

    void ResumeAfterMenuDeferred() => CallDeferred(nameof(ResumeAfterMenu));

    void LeaveToMenu()
    {
        _leaving = true;
        _router!.ReturnToMainMenu();
    }

    /// <summary>Self-test hooks: the live world, and the way out of the run (to the main menu) without the dialogs.</summary>
    public LiveWorld DebugWorld => _world;
    public void DebugLeave() => LeaveToMenu();

    /// <summary>Self-test hook: press Esc as the player would. True when the pause menu is up and this view is still the top screen.</summary>
    public bool DebugEsc() { var k = new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true }; _UnhandledInput(k); return _router != null && _router.HasModal && _router.Top == this; }

    void TogglePause()
    {
        if (_world.Clock.Speed == SimSpeed.Paused) _rec.SetSpeed(_speedBeforePause);
        else { _speedBeforePause = _world.Clock.Speed; _rec.SetSpeed(SimSpeed.Paused); }
    }

    void ChangeView(int dz)
    {
        int z = Math.Clamp(_viewZ + dz, _world.Grid.MinZ, _world.Grid.MaxZ);
        if (z == _viewZ) return;
        _viewZ = z;
        RebuildLevel();
    }

    /// <summary>Screen position (this control's local pixels) to the cell of the grid the sim runs on. The camera works in world cells; the sim's grid is the streamed window, whose cell (0,0) is world cell (OriginX, OriginY).</summary>
    Vector2I ScreenToCell(Vector2 local)
    {
        float f = GetWindow().ContentScaleFactor;
        var vpPos = local * f - new Vector2(_vp.Size.X, _vp.Size.Y) / 2f;
        var world = _cam.Position + vpPos / _zoom;
        float unit = _band switch { Band.Command => 2f, Band.Field => 8f, _ => Tile };
        return new Vector2I(Mathf.FloorToInt(world.X / unit) - _world.OriginX, Mathf.FloorToInt(world.Y / unit) - _world.OriginY);
    }

    /// <summary>Inverse of <see cref="ScreenToCell"/>: the centre of a window cell on screen.</summary>
    Vector2 CellToScreen(int cx, int cy)
    {
        float f = GetWindow().ContentScaleFactor;
        float unit = _band switch { Band.Command => 2f, Band.Field => 8f, _ => Tile };
        var world = new Vector2(cx + _world.OriginX + 0.5f, cy + _world.OriginY + 0.5f) * unit;
        return ((world - _cam.Position) * _zoom + new Vector2(_vp.Size.X, _vp.Size.Y) / 2f) / f;
    }

    /// <summary>Walk to the clicked cell. True when the hero set off.</summary>
    bool ClickMove(Vector2 local)
    {
        if (ApActive) ApTakeControl(L.T("AP_TOOK_CONTROL"));                                              // a click on the ground is the player's own order
        var c = ScreenToCell(local);
        if (!_world.Grid.InBounds(c.X, c.Y, _viewZ)) return false;
        if (_viewZ != _world.Snapshot.Hero.Z) { _router?.ShowToast(L.T("STREET_WRONG_LEVEL")); return false; }
        if (!_rec.CommandMoveTo(new Cell3(c.X, c.Y, _viewZ))) { _router?.ShowToast(L.T("STREET_NO_PATH")); return false; }
        return true;
    }

    /// <summary>Self-test hook: click on screen the cells around the hero until one accepts a path. Returns (accepted, cell clicked, tick steps taken, cells moved).</summary>
    public (bool Accepted, Vector2I Clicked, int Moved) DebugClickAround(int steps = 40)
    {
        var h0 = _world.Snapshot.Hero;
        foreach (int d in new[] { 4, -4, 6, -6, 3, -3, 8, -8 })
        {
            var cell = new Vector2I(h0.CellX + d, h0.CellY);
            var back = ScreenToCell(CellToScreen(cell.X, cell.Y));
            if (back != cell) return (false, cell, -1); // the screen mapping does not round-trip
            if (!ClickMove(CellToScreen(cell.X, cell.Y))) continue;
            for (int i = 0; i < steps; i++) _rec.Step();
            var h1 = _world.Snapshot.Hero;
            return (true, cell, Math.Abs(h1.CellX - h0.CellX) + Math.Abs(h1.CellY - h0.CellY));
        }
        return (false, new Vector2I(h0.CellX, h0.CellY), 0);
    }

    // ------------------------------------------------------------------ shot / bench

    /// <summary>A screenshot waits for the weather lookup and, at Field and Command, for the visible chunk images to finish baking (or the settle limit).</summary>
    bool ShotReady() => _frames > _opt.Settle || ((!_opt.PeopleShot || _lod.Count >= 10) && _wfeed.Ready && TilesSettled && (_band < Band.Field || _far == null || _far.Settled));

    void Shoot()
    {
        _quitting = true;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_opt.ShotPath!) ?? ".");
        var err = img.SavePng(_opt.ShotPath!);
        if (_opt.Weather == "storm") GD.Print($"[street] lightning flash alpha {_wover.FlashAlpha:0.00}, flashes enabled: {Accessibility.FlashesEnabled}");
        GD.Print($"[street] screenshot {_opt.ShotPath} ({img.GetWidth()}x{img.GetHeight()}): {err}");
        Finish();
    }

    /// <summary>One line about the abstract tick for the perf label and the exit report: main-thread cost per frame, and what the worker did (read from the barrier snapshot).</summary>
    string RuntimeLine()
    {
        if (Runtime is not { } rt) return "";
        var sn = rt.Snapshot;
        return $"  world pump {_pumpMsSum / Math.Max(1, _pumpCount) * 1000:F1} us (max {_pumpMsMax:F2} ms)  hour {sn.Hour} step {sn.HourMs:F0} ms (max {sn.MaxHourMs:F0})  regions {sn.Resident} paged {sn.Paged}";
    }

    void Finish()
    {
        if (Runtime != null) GD.Print("[street] world tick:" + RuntimeLine());
        var hh = _world.Snapshot.Hero;
        GD.Print($"[street] hero at ({hh.X:0.00},{hh.Y:0.00}) z{hh.Z}, path left {hh.PathRemaining}, t={_world.Snapshot.Time}");
        GD.Print($"[street] ticks {_tickCount}: avg {_tickMsSum / Math.Max(1, _tickCount):F3} ms, max {_tickMsMax:F3} ms; draw {_drawCount} frames: avg {_drawMsSum / Math.Max(1, _drawCount):F3} ms, max {_drawMsMax:F3} ms (max after warm-up {_drawMsMaxWarm:F3} ms)");
        GetTree().Quit();
    }
}
