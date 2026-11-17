using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.Debug;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.World;

namespace Ravensguard.Game.World;

/// <summary>
/// The first playable street view (plan 2.7, 2.8). The sim (<see cref="LiveWorld"/>) owns every bit of state; this node
/// reads its snapshot each frame and draws: terrain and fixtures as TileMapLayers from the baked atlases, scenery and the hero as
/// y-sorted sprites, and the light grid as a multiplicative overlay. Buildings are roofless; the level below shows through openings one shade darker.
/// Zoom is whole-number only: 1x is Street (32 px/m), 2x is Close (64 px/m).
/// </summary>
public partial class StreetView : Control
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

    // view state (camera only; nothing here is game state)
    int _zoom = 1, _viewZ;
    Vector2 _pan;
    double _acc;
    long _shownTick = -1;
    SimSpeed _speedBeforePause = SimSpeed.X1;

    // scene
    SubViewportContainer _box = null!;
    SubViewport _vp = null!;
    Camera2D _cam = null!;
    TileMapLayer _belowT = null!, _belowO = null!, _baseT = null!, _baseO = null!;
    Node2D _ysort = null!;
    RigAtlas _rig = null!;
    RigActor _heroSprite = null!;
    Sprite2D _torchSprite = null!, _lightSprite = null!;
    ImageTexture _lightTex = null!;
    byte[] _lightBytes = null!;
    readonly Dictionary<string, Vector2I> _terrainCells = new(), _objectCells = new();

    // hud
    Label _dateLabel = null!, _phaseLabel = null!, _levelLabel = null!, _hintLabel = null!, _perfLabel = null!;
    readonly List<Label> _levelBoxes = new();

    // timing / shot
    readonly Stopwatch _sw = new();
    double _tickMsSum, _tickMsMax, _drawMsSum, _drawMsMax; long _tickCount, _drawCount;
    int _frames; double _benchT; bool _quitting;
    static readonly byte[] Tone = BuildTone();

    public StreetView(ScreenRouter? router, StreetOptions? opt = null, StreetSource? source = null) { _router = router; _opt = opt ?? new StreetOptions(); _source = source; }

    /// <summary>
    /// The street view over a generated region (plan 2.2, 2.3): chunks generate on first visit around the hero and stream as they walk.
    /// <paramref name="spawn"/> is in world cells (0..31999); null picks a paved cell in the largest town's square.
    /// Pass the save's <paramref name="store"/> to resume (visited chunks come back from snapshot plus deltas).
    /// </summary>
    public static StreetView ForRegion(Region region, Population population, ContentDatabase content, ScreenRouter? router = null, StreetOptions? opt = null,
        ChunkStore? store = null, (int X, int Y, int Z)? spawn = null)
        => new(router, opt, StreetSource.ForRegion(region, population, content, store, spawn is { } p ? new Cell3(p.X, p.Y, p.Z) : null, opt?.Poi));

    // ------------------------------------------------------------------ setup

    public override void _Ready()
    {
        _art = ArtAtlas.Load();
        _rig = RigAtlas.Load();
        _source ??= _opt.RegionSeed is { } seed ? StreetSource.FromSeed(seed, _opt.Poi) : StreetSource.Hamlet();
        var t0 = GameTime.FromCalendar(1, Season.Spring, 8, _opt.Hour ?? 9, 0);
        _world = _source.CreateWorld(_opt.Start is { } s ? new Cell3(s.X, s.Y, s.Z) : null, t0);
        var start = _world.Snapshot.Hero.Cell;
        DevContext.Clock = _world.Clock;
        if (_opt.Torch ?? IsNight(t0)) _world.SetTorch(true);
        _zoom = _opt.Zoom;
        _viewZ = _opt.ViewZ ?? start.Z;
        if (_opt.Pan is { } p) _pan = new Vector2(p.X, p.Y) * Tile;

        BuildScene();
        BuildHud();
        Resized += FitViewport;
        GetWindow().SizeChanged += FitViewport;
        FitViewport();
        RebuildLevel();
        _world.Step();
        if (_opt.BenchSeconds > 0) _perfLabel.Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
        GD.Print($"[street] ready ({_source.Name}): {Map.Props.Count} props, {Map.Buildings.Count} buildings, {_world.Grid.CellCount} cells, level {_viewZ}, hero at world ({_world.OriginX + start.X},{_world.OriginY + start.Y})");
    }

    public override void _ExitTree()
    {
        if (GetWindow() != null) GetWindow().SizeChanged -= FitViewport;
        if (DevContext.Clock == _world?.Clock) DevContext.Clock = null;
    }

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
        var r = new ModLoader(ContentTypeRegistry.Default).Load([ContentDir()]);
        if (!r.Success) throw new InvalidOperationException("content failed to load: " + r.Report);
        return BuildingLibrary.FromDatabase(r.Database);
    }

    void BuildScene()
    {
        _box = new SubViewportContainer { Name = "WorldBox", Stretch = false, MouseFilter = MouseFilterEnum.Ignore, TextureFilter = TextureFilterEnum.Nearest };
        AddChild(_box);
        _vp = new SubViewport { Name = "World", HandleInputLocally = false, GuiDisableInput = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Size = new Vector2I(1280, 720) };
        _box.AddChild(_vp);
        var root = new Node2D { Name = "Root" };
        _vp.AddChild(root);

        var ts = new TileSet { TileSize = new Vector2I(Tile, Tile) };
        AddSource(ts, "terrain_32", "terrain.", _terrainCells, 0);
        AddSource(ts, "objects_32", "object.", _objectCells, 1);
        TileMapLayer Layer(string name, Color tint)
        {
            var l = new TileMapLayer { Name = name, TileSet = ts, TextureFilter = TextureFilterEnum.Nearest, Modulate = tint, RenderingQuadrantSize = 16 };
            root.AddChild(l);
            return l;
        }
        var shade = new Color(0.58f, 0.58f, 0.62f);
        _belowT = Layer("BelowTerrain", shade);
        _belowO = Layer("BelowObjects", shade);
        _baseT = Layer("Terrain", Colors.White);
        _baseO = Layer("Objects", Colors.White);

        _ysort = new Node2D { Name = "Sprites", YSortEnabled = true };
        root.AddChild(_ysort);
        _heroSprite = new RigActor(_rig, "hero"); // stacked layers from rig_32x64.png
        _ysort.AddChild(_heroSprite);
        _torchSprite = new Sprite2D { Texture = _art.Texture("item.torch"), Centered = false, Offset = new Vector2(-3, -34), TextureFilter = TextureFilterEnum.Nearest, Visible = false };
        _heroSprite.AddChild(_torchSprite);

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
        root.AddChild(_lightSprite);

        _cam = new Camera2D { Name = "Camera", AnchorMode = Camera2D.AnchorModeEnum.DragCenter };
        root.AddChild(_cam);
        _cam.MakeCurrent();
    }

    void AddSource(TileSet ts, string atlas, string prefix, Dictionary<string, Vector2I> cells, int sourceId)
    {
        var src = new TileSetAtlasSource { Texture = _art.Atlas(atlas), TextureRegionSize = new Vector2I(Tile, Tile) };
        foreach (var (id, r) in _art.All)
        {
            if (r.Atlas != atlas || r.W != Tile || r.H != Tile) continue;
            var c = new Vector2I(r.X / Tile, r.Y / Tile);
            src.CreateTile(c);
            cells[id] = c;
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
        hud.AddChild(top);

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
        _hintLabel.AnchorTop = 1; _hintLabel.AnchorBottom = 1; _hintLabel.OffsetLeft = 12; _hintLabel.OffsetTop = -30; _hintLabel.GrowVertical = GrowDirection.Begin;
        _hintLabel.AddThemeColorOverride("font_outline_color", PixelTheme.Border);
        _hintLabel.AddThemeConstantOverride("outline_size", 4);
        hud.AddChild(_hintLabel);

        _perfLabel = new Label { Visible = false, Position = new Vector2(12, 90) };
        hud.AddChild(_perfLabel);
    }

    static string H(string id) => InputBootstrap.Hint(id).Trim('[', ']');

    static string HintText() => string.Format(L.T("STREET_HINTS"),
        $"{H("world.move_up")}{H("world.move_left")}{H("world.move_down")}{H("world.move_right")}", H("world.pause"), H("world.torch"),
        $"{H("world.level_up")}/{H("world.level_down")}", $"{H("world.zoom_in")}/{H("world.zoom_out")}", H("world.camera_home"));

    void UpdateHud(LiveSnapshot s)
    {
        var t = s.Time;
        _dateLabel.Text = $"{t.Hour:00}:{t.Minute:00}  {_calendar.Describe(t)}";
        string phase = s.Ambient.Phase switch { DayPhase.Dawn => "STREET_PHASE_DAWN", DayPhase.Dusk => "STREET_PHASE_DUSK", DayPhase.Night => "STREET_PHASE_NIGHT", _ => "STREET_PHASE_DAY" };
        string speed = s.Speed == SimSpeed.Paused ? L.T("STREET_PAUSED") : $"{(int)s.Speed}x";
        string torch = s.Hero.Torch ? "  " + L.T("STREET_TORCH_ON") : "";
        _phaseLabel.Text = $"{L.T(phase)}  {speed}{torch}";
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
    readonly Dictionary<(int X, int Y), List<Sprite2D>> _chunkProps = new();
    readonly Dictionary<string, string?> _terrainPick = new(), _objectPick = new(), _propPick = new();
    HamletMap? _syncedMap; int _syncedRev = -1;

    /// <summary>The view level changed (or first draw): forget everything and draw the current map's chunks at this level.</summary>
    void RebuildLevel()
    {
        var sw = Stopwatch.StartNew();
        _baseT.Clear(); _baseO.Clear(); _belowT.Clear(); _belowO.Clear();
        foreach (var l in _chunkProps.Values) foreach (var n in l) n.QueueFree();
        _chunkProps.Clear(); _drawn.Clear();
        _syncedMap = null;
        SyncTiles();
        _shownTick = -1;
        GD.Print($"[street] level {_viewZ} tiles rebuilt in {sw.Elapsed.TotalMilliseconds:F1} ms ({_drawn.Count} chunks)");
    }

    /// <summary>Draws chunks that are new or changed in the current window and erases those that left it.</summary>
    void SyncTiles()
    {
        var m = Map;
        if (m == _syncedMap && m.StreamRevision == _syncedRev) return;
        _syncedMap = m; _syncedRev = m.StreamRevision;
        if (m.ChunkVersions == null)
        {
            if (_drawn.Count > 0) return; // the hamlet never changes
            DrawCells(m, 0, 0, m.Grid.Width, m.Grid.Height);
            var all = new List<Prop>(m.Props);
            AddProps(m, (0, 0), all);
            _drawn[(0, 0)] = 0;
            return;
        }
        List<(int X, int Y)>? stale = null;
        foreach (var k in _drawn.Keys) if (!m.ChunkVersions.ContainsKey(k)) (stale ??= new()).Add(k);
        if (stale != null) foreach (var k in stale) EraseChunk(m, k);
        Dictionary<(int X, int Y), List<Prop>>? byChunk = null;
        foreach (var (k, ver) in m.ChunkVersions)
        {
            if (_drawn.TryGetValue(k, out int dv) && dv == ver) continue;
            if (byChunk == null)
            {
                byChunk = new();
                foreach (var p in m.Props)
                {
                    var ck = (Div(p.X + m.OriginX, 32), Div(p.Y + m.OriginY, 32));
                    if (!byChunk.TryGetValue(ck, out var l)) byChunk[ck] = l = new();
                    l.Add(p);
                }
            }
            if (_drawn.ContainsKey(k)) EraseChunk(m, k);
            int lx = k.X * 32 - m.OriginX, ly = k.Y * 32 - m.OriginY;
            DrawCells(m, lx, ly, lx + 32, ly + 32);
            if (byChunk.TryGetValue(k, out var props)) AddProps(m, k, props);
            _drawn[k] = ver;
        }
    }

    static int Div(int a, int b) { int q = a / b; return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q; }

    string? Pick(string id, Dictionary<string, Vector2I> cells, Dictionary<string, string?> memo)
    {
        if (memo.TryGetValue(id, out var r)) return r;
        r = null;
        foreach (var alt in TileSprites.Alternatives(id)) if (cells.ContainsKey(alt)) { r = alt; break; }
        return memo[id] = r;
    }

    void DrawCells(HamletMap m, int x0, int y0, int x1, int y1)
    {
        var g = m.Grid; int z = _viewZ, ox = m.OriginX, oy = m.OriginY;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var c = new Vector2I(x + ox, y + oy);
                _baseT.EraseCell(c); _baseO.EraseCell(c); _belowT.EraseCell(c); _belowO.EraseCell(c);
                var t = TileSprites.Terrain(m, x, y, z);
                if (t != null)
                {
                    if (Pick(t, _terrainCells, _terrainPick) is { } tp) _baseT.SetCell(c, 0, _terrainCells[tp]);
                    if (TileSprites.Object(m, x, y, z) is { } o && Pick(o, _objectCells, _objectPick) is { } op) _baseO.SetCell(c, 1, _objectCells[op]);
                    continue;
                }
                for (int dz = 1; dz <= 2 && z - dz >= g.MinZ; dz++) // open air: look down
                {
                    var bt = TileSprites.Terrain(m, x, y, z - dz);
                    if (bt == null) continue;
                    if (Pick(bt, _terrainCells, _terrainPick) is { } btp) _belowT.SetCell(c, 0, _terrainCells[btp]);
                    if (TileSprites.Object(m, x, y, z - dz) is { } bo && Pick(bo, _objectCells, _objectPick) is { } bop) _belowO.SetCell(c, 1, _objectCells[bop]);
                    break;
                }
            }
    }

    void AddProps(HamletMap m, (int X, int Y) key, List<Prop> props)
    {
        var list = new List<Sprite2D>();
        foreach (var p in props)
        {
            if (p.Z != _viewZ) continue;
            var id = PickProp(p.Sprite);
            if (id == null) continue;
            var tex = _art.Texture(id);
            var size = tex.GetSize();
            int px = p.X + m.OriginX, py = p.Y + m.OriginY;
            float cx = p.Wide ? (px + 1) * Tile : px * Tile + Tile / 2f;
            var sp = new Sprite2D { Texture = tex, Centered = false, Offset = new Vector2(-size.X / 2f, -size.Y), Position = new Vector2(cx, (py + 1) * Tile), TextureFilter = TextureFilterEnum.Nearest };
            _ysort.AddChild(sp);
            list.Add(sp);
        }
        _chunkProps[key] = list;
    }

    string? PickProp(string id)
    {
        if (_propPick.TryGetValue(id, out var r)) return r;
        r = null;
        foreach (var alt in TileSprites.Alternatives(id)) if (_art.Has(alt)) { r = alt; break; }
        return _propPick[id] = r;
    }

    void EraseChunk(HamletMap m, (int X, int Y) k)
    {
        int cx = k.X * 32, cy = k.Y * 32;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                var c = new Vector2I(cx + x, cy + y);
                _baseT.EraseCell(c); _baseO.EraseCell(c); _belowT.EraseCell(c); _belowO.EraseCell(c);
            }
        if (_chunkProps.Remove(k, out var l)) foreach (var n in l) n.QueueFree();
        _drawn.Remove(k);
    }

    // ------------------------------------------------------------------ frame

    public override void _Process(double delta)
    {
        _frames++;
        var bench = _opt.BenchSeconds > 0;
        if (bench) { _benchT += delta; ScriptedWalk(); }
        if (!_quitting) HandleHeldInput(delta);

        _acc += Math.Min(delta, 0.25);
        int steps = 0;
        while (_acc >= Step && steps < 5)
        {
            _sw.Restart();
            _world.Step();
            double ms = _sw.Elapsed.TotalMilliseconds;
            _tickMsSum += ms; _tickMsMax = Math.Max(_tickMsMax, ms); _tickCount++;
            _acc -= Step; steps++;
        }
        if (steps == 5) _acc = 0;

        _sw.Restart();
        Render((float)(_acc / Step));
        double dms = _sw.Elapsed.TotalMilliseconds;
        _drawMsSum += dms; _drawMsMax = Math.Max(_drawMsMax, dms); _drawCount++;

        if (_opt.Click is { } c && _frames == 2) ClickMove(new Vector2(c.X, c.Y));
        if (_opt.IsShot && _frames == _opt.Frames) Shoot();
        if (bench && _benchT >= _opt.BenchSeconds) Finish();
        if (bench && _perfLabel.Visible && _frames % 30 == 0)
            _perfLabel.Text = $"tick {_tickMsSum / Math.Max(1, _tickCount):F3} ms (max {_tickMsMax:F2})  draw {_drawMsSum / Math.Max(1, _drawCount):F3} ms (max {_drawMsMax:F2})  fps {Engine.GetFramesPerSecond()}";
    }

    void HandleHeldInput(double delta)
    {
        int dx = (Actions.HeldLoose("world.move_right") ? 1 : 0) - (Actions.HeldLoose("world.move_left") ? 1 : 0);
        int dy = (Actions.HeldLoose("world.move_down") ? 1 : 0) - (Actions.HeldLoose("world.move_up") ? 1 : 0);
        if (_opt.BenchSeconds > 0) return;
        _world.SetInput(new MoveInput(dx, dy, Actions.HeldLoose("world.sprint"), Actions.Held("world.sneak")));
        var pan = new Vector2(
            (Actions.HeldLoose("world.camera_right") ? 1 : 0) - (Actions.HeldLoose("world.camera_left") ? 1 : 0),
            (Actions.HeldLoose("world.camera_down") ? 1 : 0) - (Actions.HeldLoose("world.camera_up") ? 1 : 0));
        if (pan != Vector2.Zero) _pan += pan * (float)(delta * 10 * Tile);
        _pan = _pan.Clamp(new Vector2(-24 * Tile, -24 * Tile), new Vector2(24 * Tile, 24 * Tile));
    }

    void ScriptedWalk()
    {
        // benchmark: sprint around the plaza in a square, torch on
        int phase = (int)(_benchT / 1.5) % 4;
        var (dx, dy) = phase switch { 0 => (1, 0), 1 => (0, 1), 2 => (-1, 0), _ => (0, -1) };
        _world.SetInput(new MoveInput(dx, dy, true));
        if (_frames == 2) _world.SetTorch(true);
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
        SyncTiles();
        var feet = new Vector2(Mathf.Round((fx + s.OriginX) * Tile), Mathf.Round((fy + s.OriginY) * Tile));
        _heroSprite.Position = feet;
        _heroSprite.Visible = h.Z == _viewZ;
        AnimateHero(h, feet);
        _torchSprite.Visible = h.Torch;
        _torchSprite.Position = new Vector2(h.Facing == Facing.West ? -16 : 6, 0);

        _zoom = Math.Clamp(_zoom, 1, 2);
        _cam.Zoom = new Vector2(_zoom, _zoom);
        _cam.Position = new Vector2(Mathf.Round(feet.X + _pan.X), Mathf.Round(feet.Y - 16 + _pan.Y));

        if (s.Tick != _shownTick) { _shownTick = s.Tick; UpdateLight(s); UpdateHud(s); }
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
        int sun = s.Ambient.Sun;
        // ambient tint: cool moonlight to warm daylight, with an orange cast while the sun is low
        float sf = sun / 255f;
        float tr = Mathf.Lerp(0.50f, 1.0f, sf), tg = Mathf.Lerp(0.62f, 0.98f, sf), tb = Mathf.Lerp(1.0f, 0.92f, sf);
        if (s.Ambient.Phase is DayPhase.Dawn or DayPhase.Dusk)
        {
            float k = 4f * sf * (1f - sf) * 0.55f;
            tr = Mathf.Lerp(tr, 1.0f, k); tg = Mathf.Lerp(tg, 0.70f, k); tb = Mathf.Lerp(tb, 0.50f, k);
        }
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
        if (Actions.Pressed(e, "world.pause")) TogglePause();
        else if (Actions.Pressed(e, "world.speed_1")) _world.SetSpeed(SimSpeed.X1);
        else if (Actions.Pressed(e, "world.speed_2")) _world.SetSpeed(SimSpeed.X2);
        else if (Actions.Pressed(e, "world.speed_4")) _world.SetSpeed(SimSpeed.X4);
        else if (Actions.Pressed(e, "world.speed_8")) _world.SetSpeed(SimSpeed.X8);
        else if (Actions.Pressed(e, "world.torch")) _world.ToggleTorch();
        else if (Actions.Pressed(e, "world.level_up")) ChangeView(1);
        else if (Actions.Pressed(e, "world.level_down")) ChangeView(-1);
        else if (Actions.Pressed(e, "world.zoom_in")) _zoom = 2;
        else if (Actions.Pressed(e, "world.zoom_out")) _zoom = 1;
        else if (Actions.Pressed(e, "world.camera_home")) { _pan = Vector2.Zero; if (_viewZ != _world.Snapshot.Hero.Z) { _viewZ = _world.Snapshot.Hero.Z; RebuildLevel(); } }
        else if (e is InputEventKey && Actions.Pressed(e, "world.act")) _world.UsePortal();
        else return;
        GetViewport().SetInputAsHandled();
    }

    void TogglePause()
    {
        if (_world.Clock.Speed == SimSpeed.Paused) _world.SetSpeed(_speedBeforePause);
        else { _speedBeforePause = _world.Clock.Speed; _world.SetSpeed(SimSpeed.Paused); }
    }

    void ChangeView(int dz)
    {
        int z = Math.Clamp(_viewZ + dz, _world.Grid.MinZ, _world.Grid.MaxZ);
        if (z == _viewZ) return;
        _viewZ = z;
        RebuildLevel();
    }

    void ClickMove(Vector2 local)
    {
        float f = GetWindow().ContentScaleFactor;
        var vpPos = local * f - new Vector2(_vp.Size.X, _vp.Size.Y) / 2f;
        var world = _cam.Position + vpPos / _zoom;
        int cx = Mathf.FloorToInt(world.X / Tile), cy = Mathf.FloorToInt(world.Y / Tile);
        if (!_world.Grid.InBounds(cx, cy, _viewZ)) return;
        if (_viewZ != _world.Snapshot.Hero.Z) { _router?.ShowToast(L.T("STREET_WRONG_LEVEL")); return; }
        if (!_world.CommandMoveTo(new Cell3(cx, cy, _viewZ))) _router?.ShowToast(L.T("STREET_NO_PATH"));
    }

    // ------------------------------------------------------------------ shot / bench

    void Shoot()
    {
        _quitting = true;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_opt.ShotPath!) ?? ".");
        var err = img.SavePng(_opt.ShotPath!);
        GD.Print($"[street] screenshot {_opt.ShotPath} ({img.GetWidth()}x{img.GetHeight()}): {err}");
        Finish();
    }

    void Finish()
    {
        var hh = _world.Snapshot.Hero;
        GD.Print($"[street] hero at ({hh.X:0.00},{hh.Y:0.00}) z{hh.Z}, path left {hh.PathRemaining}, t={_world.Snapshot.Time}");
        GD.Print($"[street] ticks {_tickCount}: avg {_tickMsSum / Math.Max(1, _tickCount):F3} ms, max {_tickMsMax:F3} ms; draw {_drawCount} frames: avg {_drawMsSum / Math.Max(1, _drawCount):F3} ms, max {_drawMsMax:F3} ms");
        GetTree().Quit();
    }
}
