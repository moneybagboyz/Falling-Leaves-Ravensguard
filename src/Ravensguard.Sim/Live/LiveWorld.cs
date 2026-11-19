using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Codex;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen.Chunks;

namespace Ravensguard.Sim.Live;

/// <summary>Immutable view of the live world at the end of one tick. The presentation layer reads only this (plan 2.1a, 2.4).</summary>
public sealed record LiveSnapshot(
    long Tick, GameTime Time, SimSpeed Speed, HeroSnapshot Hero,
    Ambient Ambient, LightFrame Light, int HeroLight, int HeroVision,
    int OriginX = 0, int OriginY = 0);

/// <summary>
/// The live sim for the street view: clock, level grid, hero and lights, stepped at a fixed 20 Hz (plan 2.5).
/// One <see cref="Step"/> is 1/20 s of real time. The game clock advances by the speed multiplier; the hero covers
/// speed x multiplier x dt of ground per step in sub-steps of at most a quarter metre, so 8x never tunnels through a wall.
/// All arithmetic is integer or Q16.16, so the same inputs give the same positions on every machine.
/// </summary>
public sealed class LiveWorld
{
    public const int TickHz = 20;
    public const double TickSeconds = 1.0 / TickHz;
    public static readonly Fixed WalkSpeed = Fixed.FromRatio(3, 2), JogSpeed = Fixed.FromInt(3), SprintSpeed = Fixed.FromInt(5);
    public static readonly Fixed DtPerTick = Fixed.FromRatio(1, TickHz);
    public static readonly Fixed Radius = Fixed.FromRatio(3, 10);
    private static readonly Fixed MaxSubStep = Fixed.FromRatio(1, 4);
    private static readonly Fixed Diag = Fixed.FromRaw(46341); // 1/sqrt(2)
    private static readonly Fixed Arrive = Fixed.FromRatio(1, 16);

    public LevelGrid Grid { get; private set; }
    public HamletMap Map { get; private set; }
    public LightGrid Lights { get; private set; }
    public SimClock Clock { get; }
    public LevelPathfinder Pathfinder { get; private set; }
    /// <summary>Streamed region mode: the chunk streamer and the window of it the sim currently stands in (null for the hamlet and test grids).</summary>
    public ChunkStreamer? Streamer { get; }
    public WorldWindow? Window { get; private set; }
    /// <summary>World cell of local (0,0): zero outside streamed mode. Hero cells in a snapshot are local; add this for world cells.</summary>
    public int OriginX => Window?.OriginX ?? 0;
    public int OriginY => Window?.OriginY ?? 0;
    /// <summary>Chunks the hero has moved the window across (rebuilds), for diagnostics.</summary>
    public int WindowSwaps { get; private set; }
    private int _lightRev;
    /// <summary>
    /// Window rebuilds run on a worker by default (the hero keeps walking in the old window meanwhile). Set before the first step; tests that need
    /// the exact moment of a swap switch it off.
    /// </summary>
    public bool SynchronousStreaming { get; set; }
    private Task<WorldWindow>? _pending;
    private int _pendingCx, _pendingCy;
    /// <summary>Chunks stamped into the window per tick while it catches up after a swap.</summary>
    private const int SyncChunksPerTick = 2;
    /// <summary>The hero's chunk may be this far (Chebyshev) from the window's centre before the window is rebuilt around it.</summary>
    public const int RecentreDistance = 1;
    private LightFramePair _lightPair;
    private readonly List<LightSource> _dyn = new(1);
    public int LatitudeTenths { get; }

    // ---- live play state that is saved with the world (WorldSave.CaptureLive)
    /// <summary>The Codex of this world (unlocked entries are saved). Null until <see cref="AttachCodex"/>.</summary>
    public CodexBook? Codex { get; private set; }
    /// <summary>The region tiles the hero has been near, marked in the tick.</summary>
    public ExploredTiles Explored { get; private set; } = new();
    private readonly List<CodexEntry> _opened = [];
    private int _lastVisitX = int.MinValue, _lastVisitY = int.MinValue, _lastExploreTile = -1;
    /// <summary>The hero's world cell must change by this many metres before the Codex is asked again.</summary>
    private const int VisitStepM = 8;
    public long Tick { get; private set; }
    public LiveSnapshot Snapshot { get; private set; }

    // hero
    private Fixed _x, _y, _px, _py;
    private int _z, _pz;
    private Facing _facing = Facing.South;
    private bool _moving, _torch;
    private MoveMode _mode = MoveMode.Jog;
    private MoveInput _input;
    private List<Cell3>? _path;
    private int _pathIdx, _stuck;
    private Cell3? _portalLock;
    private LightFrame? _frame;
    private int _lastMinute = -1;
    private bool _lightDirty = true, _usePending;

    public LiveWorld(HamletMap map, Cell3 start, GameTime startTime, int latitudeTenths = 480)
    {
        Map = map; Grid = map.Grid; LatitudeTenths = latitudeTenths;
        Lights = map.BuildLightGrid(); _lightPair = new LightFramePair(Lights);
        Pathfinder = new LevelPathfinder(Grid);
        Clock = new SimClock(startTime) { Speed = SimSpeed.X1 };
        _x = Fixed.FromInt(start.X) + Fixed.Half; _y = Fixed.FromInt(start.Y) + Fixed.Half; _z = start.Z;
        _px = _x; _py = _y; _pz = _z;
        Snapshot = Publish();
    }

    /// <summary>
    /// Streamed region mode (plan 2.3): the live bubble is a window of generated chunks around the hero. <paramref name="start"/> is in world
    /// cells (0..31999). The hero's surrounding chunks are loaded before the first step; further chunks stream in time-sliced.
    /// </summary>
    public LiveWorld(ChunkStreamer streamer, Cell3 start, GameTime startTime, int latitudeTenths = 480, CodexBook? codex = null)
        : this(streamer, start, startTime, latitudeTenths, codex, null) { }

    /// <summary>Resumes a saved run (see <c>WorldSave.Restore</c>): the hero stands exactly where the save left it, with the saved clock, torch, explored tiles and Codex progress.</summary>
    public LiveWorld(ChunkStreamer streamer, LiveResume resume, int latitudeTenths = 480, CodexBook? codex = null)
        : this(streamer, resume.Cell, resume.Time, latitudeTenths, codex, resume) { }

    private LiveWorld(ChunkStreamer streamer, Cell3 start, GameTime startTime, int latitudeTenths, CodexBook? codex, LiveResume? resume)
    {
        Streamer = streamer; LatitudeTenths = latitudeTenths;
        int cx = ChunkStreamer.FloorDiv(start.X, 32), cy = ChunkStreamer.FloorDiv(start.Y, 32);
        streamer.EnsureReady(cx, cy); // a start in a region that is not built yet builds it first (the one place the hero may wait)
        streamer.LoadAround(cx, cy, streamer.Settings.LoadRadius);
        Window = new WorldWindow(streamer, cx, cy);
        Grid = Window.Grid; Map = Window.Map; Lights = Window.Lights; _lightRev = Window.LightRevision; _lightPair = new LightFramePair(Lights);
        Pathfinder = new LevelPathfinder(Grid);
        Clock = resume == null ? new SimClock(startTime) { Speed = SimSpeed.X1 } : new SimClock(resume.Time, resume.MsRemainder) { Speed = resume.Speed };
        if (resume == null) { _x = Fixed.FromInt(start.X - Window.OriginX) + Fixed.Half; _y = Fixed.FromInt(start.Y - Window.OriginY) + Fixed.Half; }
        else
        {
            _x = Fixed.FromRaw((int)(resume.WorldXRaw - ((long)Window.OriginX << Fixed.FracBits)));
            _y = Fixed.FromRaw((int)(resume.WorldYRaw - ((long)Window.OriginY << Fixed.FracBits)));
            _facing = resume.Facing; _torch = resume.Torch; Explored = resume.Explored.Clone();
            if (resume.Motion is { } m) RestoreMotion(m);
            ResumedBody = resume.Body; WantedSpeed = resume.Wanted;
        }
        _z = start.Z;
        _px = _x; _py = _y; _pz = _z;
        if (codex != null) { AttachCodex(codex); if (resume?.Codex != null) codex.Apply(resume.Codex); }
        NoteWalked();
        Snapshot = Publish();
    }

    /// <summary>Test constructor: any grid, no scenery.</summary>
    public LiveWorld(LevelGrid grid, LightGrid lights, Cell3 start, GameTime startTime, int latitudeTenths = 480)
    {
        Map = null!; Grid = grid; Lights = lights; LatitudeTenths = latitudeTenths; _lightPair = new LightFramePair(lights);
        Pathfinder = new LevelPathfinder(grid);
        Clock = new SimClock(startTime) { Speed = SimSpeed.X1 };
        _x = Fixed.FromInt(start.X) + Fixed.Half; _y = Fixed.FromInt(start.Y) + Fixed.Half; _z = start.Z;
        _px = _x; _py = _y; _pz = _z;
        Snapshot = Publish();
    }

    // ---------------- commands (applied at the next step) ----------------

    public void SetInput(MoveInput input) { _input = input; }
    public void SetSpeed(SimSpeed s) => Clock.Speed = s;
    /// <summary>The speed the hero asked for (the keys 1, 2, 4, 8), which the travel gate may hold the clock below. Hero state (review finding 6: it was held in the view node): recorded, saved and hashed.</summary>
    public SimSpeed WantedSpeed { get; private set; } = SimSpeed.X8;
    public void SetWantedSpeed(SimSpeed s) => WantedSpeed = s;
    public void SetTorch(bool on) { if (_torch != on) { _torch = on; _lightDirty = true; } }
    public void ToggleTorch() => SetTorch(!_torch);
    public bool Torch => _torch;
    public void ClearPath() { _path = null; _stuck = 0; }

    /// <summary>Plans a route (levels and portals included) and starts following it. False when there is none.</summary>
    public bool CommandMoveTo(Cell3 target)
    {
        var from = new Cell3(_x.Floor(), _y.Floor(), _z);
        var r = Pathfinder.FindPath(from, target);
        if (r == null || r.Cells.Count < 2) { _path = null; return r != null; }
        _path = r.Cells; _pathIdx = 1; _stuck = 0;
        return true;
    }

    /// <summary>
    /// Auto-travel (docs/specs/phase3/travel-encounters-camp.md 4.2): puts the hero at a point of the travel route, given in region-frame millimetres. The route is a pure function of game time, so the
    /// streamer simply follows (the window recentres as for any walk); the step is a metre or so a tick, never a jump. Clears any path or input target.
    /// </summary>
    public void TravelPlace(long xMm, long yMm, Facing facing)
    {
        long wx = (xMm << Fixed.FracBits) / 1000, wy = (yMm << Fixed.FracBits) / 1000;
        _x = Fixed.FromRaw((int)(wx - ((long)OriginX << Fixed.FracBits))); _y = Fixed.FromRaw((int)(wy - ((long)OriginY << Fixed.FracBits)));
        _facing = facing; _path = null; _moving = true;
    }

    /// <summary>Uses the portal under the hero's feet (stairs, ladder, trapdoor), if any.</summary>
    public bool UsePortal()
    {
        var here = new Cell3(_x.Floor(), _y.Floor(), _z);
        bool has = false;
        foreach (int pid in Grid.PortalsAt(Grid.Index(here.X, here.Y, here.Z))) if (Grid.Portals[pid].Enabled) has = true;
        _usePending |= has;
        return has;
    }

    // ---------------- the 20 Hz step ----------------

    public void Step()
    {
        Tick++;
        _px = _x; _py = _y; _pz = _z;
        if (Streamer != null) StreamStep();
        if (Fight != null && Clock.Speed != SimSpeed.Paused)
        {
            // a fight never lengthens a tick (overview 6.1): one fight step is one game second at any speed; the view steps the bubble `speed` times a frame instead
            var sp = Clock.Speed; Clock.Speed = SimSpeed.X1; Clock.Advance(TickSeconds); Clock.Speed = sp;
        }
        else Clock.Advance(TickSeconds);
        Wounds?.AdvanceTo(Clock.Now.Seconds);          // bleeding, pain and consciousness at 20 Hz while anyone has an open wound, in or out of a fight (wounds 5)
        if (Fight != null && Clock.Speed != SimSpeed.Paused) Fight.StepWithAi();
        int mult = (int)Clock.Speed;
        _moving = false;
        if (_usePending) { _usePending = false; TryPortalHere(force: true); }
        if (!_rideCmds.IsEmpty) while (_rideCmds.TryDequeue(out int rc)) RunRideCommand(rc);   // every command since the last step, in the order given
        if (Fight != null) mult = 0;                    // the hero's feet belong to the fight while it lasts
        if (mult > 0) { if (Ride is { IsMounted: true } or { IsDriving: true } or { Busy: true }) MoveRidden(mult); else MoveHero(mult); }
        NoteWalked();
        Snapshot = Publish();
    }

    // ---------------- the fight in the bubble (P4-C)

    /// <summary>The fight the hero is in (melee 5: stepped on the live tick, after the wounds, at one game second a step); null outside a fight.</summary>
    public Ravensguard.Sim.Combat.Melee2.FightWorld? Fight { get; private set; }
    /// <summary>The hero's fighter index in <see cref="Fight"/> (the player's inputs drive it).</summary>
    public int FightHero { get; private set; } = -1;
    /// <summary>Rebuilds a fight from its id when a replay reaches the tick it started (the session recorder keeps each fight's starting record).</summary>
    public Func<long, Ravensguard.Sim.Combat.Melee2.FightWorld?>? FightFactory { get; set; }

    public void AttachFight(Ravensguard.Sim.Combat.Melee2.FightWorld fight, int heroFighter) { Fight = fight; FightHero = heroFighter; }
    public void EndFight() { Fight = null; FightHero = -1; }

    /// <summary>A replayed fight start: the factory gives back the fight as it was when it began.</summary>
    public void StartFightById(long id, int heroFighter) { if (FightFactory?.Invoke(id) is { } f) AttachFight(f, heroFighter); }

    /// <summary>One player combat input for the hero's fighter (it takes effect at the next step).</summary>
    public void FightInput(Ravensguard.Sim.Combat.Melee2.FightInput i) { if (Fight != null && FightHero >= 0) Fight.Input(FightHero, i); }

    // ---------------- Codex and map exploration (part of the live state, advanced by the tick) ----------------

    /// <summary>Hands the world's Codex to the live sim: it is opened from the tick (<see cref="VisitNear"/>) and saved with the run.</summary>
    public void AttachCodex(CodexBook book) => Codex = book;

    /// <summary>The wounds both tiers share (the world state's ledger); stepped by the live clock every tick.</summary>
    public Ravensguard.Sim.Combat.Wounds.WoundLedger? Wounds { get; private set; }
    public void AttachWounds(Ravensguard.Sim.Combat.Wounds.WoundLedger ledger) => Wounds = ledger;

    /// <summary>Entries opened by the hero's walking since the last call (the view toasts them). Clears the list.</summary>
    public IReadOnlyList<CodexEntry> TakeOpenedCodexEntries()
    {
        if (_opened.Count == 0) return [];
        var r = _opened.ToArray(); _opened.Clear();
        return r;
    }

    /// <summary>
    /// The hero stands at region cell (<paramref name="worldX"/>, <paramref name="worldY"/>): landmarks within <paramref name="radiusM"/> open their Codex pages.
    /// The tick calls this as the hero walks; the opened entries wait in <see cref="TakeOpenedCodexEntries"/>. Returns how many pages opened.
    /// </summary>
    public int VisitNear(int worldX, int worldY, int radiusM = 90)
    {
        if (Codex == null) return 0;
        var opened = Codex.Visit(worldX, worldY, radiusM);
        _opened.AddRange(opened);
        return opened.Count;
    }

    /// <summary>The hero's cell in region cells (the window origin added back).</summary>
    public (int X, int Y) WorldCell => (OriginX + _x.Floor(), OriginY + _y.Floor());

    private void NoteWalked()
    {
        var (wx, wy) = WorldCell;
        int tile = (wy / ExploredTiles.TileM) * ExploredTiles.Size + wx / ExploredTiles.TileM;
        if (tile != _lastExploreTile) { _lastExploreTile = tile; Explored.Mark(wx, wy); }
        if (Math.Abs(wx - _lastVisitX) >= VisitStepM || Math.Abs(wy - _lastVisitY) >= VisitStepM)
        {
            _lastVisitX = wx; _lastVisitY = wy;
            VisitNear(wx, wy);
        }
    }

    /// <summary>The live state a save keeps; a copy, so the caller can encode it later while the sim goes on.</summary>
    public LiveResume CaptureResume()
    {
        long wx = ((long)OriginX << Fixed.FracBits) + _x.Raw, wy = ((long)OriginY << Fixed.FracBits) + _y.Raw;
        return new LiveResume(wx, wy, _z, _facing, _torch, Clock.Now, Clock.GameMsRemainder, Clock.Speed, Explored.Clone(), Codex?.ToProgress(), Motion: CaptureMotion(), Body: BodySource?.Invoke(), Wanted: WantedSpeed);
    }

    /// <summary>The hero's body for a save (core temperature, exposure, wet clothes): the game's clothing model answers it. Null: the save keeps none.</summary>
    public Func<Ravensguard.Sim.Clothing.HeroBody?>? BodySource { get; set; }
    /// <summary>The body a resumed save carried, for the game to put back on the hero once it is dressed (null when none).</summary>
    public Ravensguard.Sim.Clothing.HeroBody? ResumedBody { get; set; }

    /// <summary>The walk in progress in region cells (the window origin added), or <see cref="HeroMotion.Idle"/>.</summary>
    public HeroMotion CaptureMotion()
    {
        int ox = OriginX, oy = OriginY;
        int[] path = [];
        if (_path != null) { path = new int[_path.Count * 3]; for (int i = 0; i < _path.Count; i++) { path[i * 3] = _path[i].X + ox; path[i * 3 + 1] = _path[i].Y + oy; path[i * 3 + 2] = _path[i].Z; } }
        int[] pl = _portalLock is { } p ? [p.X + ox, p.Y + oy, p.Z] : [];
        var m = new HeroMotion(path, _path == null ? 0 : _pathIdx, _stuck, pl, _mode);
        return m.IsIdle ? HeroMotion.Idle : m;
    }

    private void RestoreMotion(HeroMotion m)
    {
        int ox = OriginX, oy = OriginY;
        if (m.PathXyz.Length >= 3)
        {
            var path = new List<Cell3>(m.PathXyz.Length / 3);
            for (int i = 0; i + 2 < m.PathXyz.Length; i += 3) path.Add(new Cell3(m.PathXyz[i] - ox, m.PathXyz[i + 1] - oy, m.PathXyz[i + 2]));
            _path = path; _pathIdx = Math.Clamp(m.PathIdx, 0, path.Count);
        }
        _stuck = m.Stuck; _mode = m.Mode;
        if (m.PortalLock.Length == 3) _portalLock = new Cell3(m.PortalLock[0] - ox, m.PortalLock[1] - oy, m.PortalLock[2]);
    }

    /// <summary>
    /// Streams chunks around the hero and keeps the window around the hero. The window is rebuilt only when the hero is more than
    /// <see cref="RecentreDistance"/> chunk from its centre (hysteresis, so walking back and forth over a seam does not rebuild), and the next window is
    /// prepared on a worker as soon as the hero leaves the centre chunk, so the swap itself is a handful of reference changes.
    /// </summary>
    private void StreamStep()
    {
        var w = Window!;
        int hcx = ChunkStreamer.FloorDiv(w.OriginX + _x.Floor(), 32), hcy = ChunkStreamer.FloorDiv(w.OriginY + _y.Floor(), 32);
        Streamer!.Update(hcx, hcy, background: !SynchronousStreaming);
        int dist = Math.Max(Math.Abs(hcx - w.CenterChunkX), Math.Abs(hcy - w.CenterChunkY));
        if (_pending != null && Math.Max(Math.Abs(hcx - _pendingCx), Math.Abs(hcy - _pendingCy)) > RecentreDistance) _pending = null; // the hero went another way; the finished window is no use
        if (dist >= 1 && _pending == null && !SynchronousStreaming)
        {
            // centred one chunk further the way the hero is heading, so the swap (at two chunks out) leaves the hero in the middle of the new window
            int pcx = hcx + Math.Sign(hcx - w.CenterChunkX), pcy = hcy + Math.Sign(hcy - w.CenterChunkY);
            _pendingCx = pcx; _pendingCy = pcy;
            var chunks = WorldWindow.CopyLoaded(Streamer, pcx, pcy);
            var st = Streamer;
            _pending = Task.Run(() => new WorldWindow(st, pcx, pcy, chunks));
        }
        if (dist > RecentreDistance)
        {
            WorldWindow? nw = null;
            if (_pending is { } task && (task.IsCompleted || dist >= WorldWindow.Chunks / 2)) // at the window's edge the hero has to wait for it
            {
                nw = task.GetAwaiter().GetResult(); nw.Attach();
            }
            else if (_pending == null) nw = new WorldWindow(Streamer, hcx, hcy);
            if (nw != null) { _pending = null; SwapWindow(nw); return; }
        }
        w.Sync(SyncChunksPerTick);
        if (w.LightRevision != _lightRev) { _lightRev = w.LightRevision; _lightDirty = true; }
    }

    private void SwapWindow(WorldWindow nw)
    {
        var w = Window!;
        int dx = nw.OriginX - w.OriginX, dy = nw.OriginY - w.OriginY;
        _x -= Fixed.FromInt(dx); _y -= Fixed.FromInt(dy); _px -= Fixed.FromInt(dx); _py -= Fixed.FromInt(dy);
        if (_portalLock is { } pl) _portalLock = new Cell3(pl.X - dx, pl.Y - dy, pl.Z);
        if (_path != null)
        {
            var moved = new List<Cell3>(_path.Count);
            foreach (var c in _path) moved.Add(new Cell3(c.X - dx, c.Y - dy, c.Z));
            int cut = moved.FindIndex(c => !nw.Grid.InBounds(c.X, c.Y, c.Z));
            if (cut >= 0) moved.RemoveRange(cut, moved.Count - cut);
            _path = moved.Count > _pathIdx ? moved : null;
        }
        nw.Sync(SyncChunksPerTick); // chunks that finished loading while the window was being built
        Window = nw; Grid = nw.Grid; Map = nw.Map; Lights = nw.Lights; _lightRev = nw.LightRevision;
        Pathfinder.Rebind(Grid); // same size: reuse the per-cell search arrays
        _lightPair = new LightFramePair(Lights);
        _frame = null; _lightDirty = true; WindowSwaps++;
    }

    /// <summary>The hero's load speed factor in permille (<c>Encumbrance.SpeedPm</c>, trade spec 4A.3): 1000 unloaded, 0 cannot walk. Set by the game from the hero's carried mass; an exact fixed-point factor on every gait.</summary>
    public int LoadSpeedPm { get; set; } = 1000;

    private Fixed SpeedOf(MoveMode m)
    {
        Fixed v = m switch { MoveMode.Walk => WalkSpeed, MoveMode.Sprint => SprintSpeed, _ => JogSpeed };
        int pm = Math.Clamp(LoadSpeedPm, 0, 1000);
        return pm == 1000 ? v : v * Fixed.FromRatio(pm, 1000);
    }

    private void MoveHero(int mult)
    {
        Fixed budget = SpeedOfInput() * DtPerTick * mult;
        if (_input.Any) StepInput(budget);
        else if (_path != null) FollowPath(budget);
        if (!_input.Any && _path == null) _mode = MoveMode.Jog;
    }

    /// <summary>Walks the held direction for up to <paramref name="budget"/> metres in sub-steps of at most a quarter metre; stops at the first step a wall refuses.</summary>
    private void StepInput(Fixed budget)
    {
        _path = null;
        int dx = _input.Dx, dy = _input.Dy;
        Fixed vx = Fixed.FromInt(dx), vy = Fixed.FromInt(dy);
        if (dx != 0 && dy != 0) { vx = vx * Diag; vy = vy * Diag; }
        SetFacing(dx, dy);
        Fixed left = budget;
        while (left > Fixed.Zero)
        {
            Fixed s = Fixed.Min(left, MaxSubStep);
            bool moved = TryMove(vx * s, vy * s);
            if (moved) _moving = true;
            else break;
            left -= s;
            AutoPortal();
        }
    }

    // ---------------- riding and driving (horses-and-carts spec 4.1, 4.4) ----------------

    /// <summary>The hero's horses, carts and ride (null until <see cref="AttachRide"/>). The ride is stepped here, one game second at a time, so stamina, wheels and the stuck rolls follow game time, never the tick.</summary>
    public RideBook? Ride { get; private set; }
    public void AttachRide(RideBook book) { Ride = book; SyncRide(); }

    /// <summary>The hero's world position in millimetres (the window's origin added).</summary>
    public (long XMm, long YMm) WorldMm => (((((long)OriginX << Fixed.FracBits) + _x.Raw) * 1000) >> Fixed.FracBits, ((((long)OriginY << Fixed.FracBits) + _y.Raw) * 1000) >> Fixed.FracBits);

    private void SyncRide() { if (Ride != null) { var (x, y) = WorldMm; Ride.PlaceHero(x, y); } }

    private byte RideKind() => Ride == null ? (byte)0 : Ride.IsMounted ? (byte)1 : Ride.IsDriving ? (byte)2 : (byte)0;

    /// <summary>A hero's command about animals and carts (E, hitch, drive, push, unload, dig); applied at the next step so a replay repeats it. Codes in <see cref="RideCode"/>.</summary>
    /// <summary>Any thread may call it: commands queue (review finding 13: a single field lost the first of two given between steps) and all run at the next step, first given first.</summary>
    public void RideCommand(int code) { if (code != 0) _rideCmds.Enqueue(code); }
    private readonly System.Collections.Concurrent.ConcurrentQueue<int> _rideCmds = new();
    /// <summary>Ride commands given and not yet run.</summary>
    public int PendingRideCommands => _rideCmds.Count;
    /// <summary>What the last <see cref="RideCommand"/> came to, as a message key and a number for the view's toast (empty when it was silent); <see cref="RideMsgSerial"/> changes with each.</summary>
    public string LastRideKey { get; private set; } = "";
    public int LastRideArg { get; private set; }
    public int RideMsgSerial { get; private set; }
    public MountResult LastRideResult { get; private set; }

    private void Say(string key, int arg = 0) { LastRideKey = key; LastRideArg = arg; RideMsgSerial++; }

    private void RunRideCommand(int c)
    {
        if (c == 0 || Ride == null) return;
        long now = Clock.Now.Seconds; SyncRide();
        switch (c)
        {
            case RideCode.Interact:
                LastRideResult = Ride.Interact(now);
                if (LastRideResult != MountResult.Ok) Say("RIDE_NO_" + LastRideResult.ToString().ToUpperInvariant());
                break;
            case RideCode.Hitch: { var r = Ride.HitchNearest(); Say("RIDE_HITCH_" + r.ToString().ToUpperInvariant()); break; }
            case RideCode.Unhitch: Ride.UnhitchAll(); Say("RIDE_UNHITCHED"); break;
            case RideCode.Drive:
                if (Ride.IsDriving) Ride.StopDriving();
                else if (!Ride.StartDriving()) Say("RIDE_NO_CART");
                break;
            case RideCode.Push: Say(Ride.Push(1, now) ? "RIDE_PUSH_FREE" : "RIDE_PUSH_STUCK"); break;
            case RideCode.Unload: Say("RIDE_UNLOADED", Ride.UnloadToHeap(100)); break;
            case RideCode.Reload: Say("RIDE_RELOADED", Ride.ReloadFromHeap()); break;
            case RideCode.Dig: Ride.Dig(now); Say("RIDE_DIG"); break;
        }
    }

    private void MoveRidden(int mult)
    {
        var rb = Ride!;
        SyncRide();
        long last = Clock.Now.Seconds;
        for (int i = 0; i < mult; i++)
        {
            long sec = last - mult + 1 + i;
            if (rb.Busy)
            {
                if (_input.Any) rb.BreakOffMount(); else rb.TickBusy();
                continue;
            }
            var step = rb.Plan(_input.Sprint, _input.Walk, sec);
            Fixed ox = _x, oy = _y;
            bool wants = _input.Any || _path != null;
            if (wants && !step.Blocked && step.SpeedMmPerGs > 0)
            {
                Fixed budget = Fixed.FromRatio(step.SpeedMmPerGs, 1000);
                if (_input.Any) StepInput(budget); else FollowPath(budget);
            }
            else if (_input.Any) SetFacing(_input.Dx, _input.Dy);
            long movedMm = (long)Length(_x - ox, _y - oy).Raw * 1000 >> Fixed.FracBits;
            SyncRide();
            if (movedMm > 0) _moving = true;
            rb.Account(step, movedMm, sec, _input.Sprint);
        }
        _mode = _input.Sprint ? MoveMode.Sprint : _input.Walk ? MoveMode.Walk : MoveMode.Jog;
        if (!_input.Any && _path == null) _mode = MoveMode.Jog;
    }

    private Fixed SpeedOfInput()
    {
        _mode = _input.Sprint ? MoveMode.Sprint : _input.Walk ? MoveMode.Walk : MoveMode.Jog;
        if (!_input.Any && _path != null) _mode = MoveMode.Jog;
        return SpeedOf(_mode);
    }

    private void SetFacing(int dx, int dy)
    {
        if (Math.Abs(dx) > Math.Abs(dy) || (Math.Abs(dx) == Math.Abs(dy) && dx != 0 && dy == 0)) _facing = dx < 0 ? Facing.West : Facing.East;
        else if (dy != 0) _facing = dy < 0 ? Facing.North : Facing.South;
        else if (dx != 0) _facing = dx < 0 ? Facing.West : Facing.East;
    }

    private void FollowPath(Fixed budget)
    {
        var path = _path!;
        Fixed left = budget;
        int guard = 0;
        while (left > Fixed.Zero && _pathIdx < path.Count && guard++ < 64)
        {
            var next = path[_pathIdx];
            if (next.Z != _z)
            {
                // portal hop: we are at the centre of the portal cell we just reached
                _z = next.Z; _x = Fixed.FromInt(next.X) + Fixed.Half; _y = Fixed.FromInt(next.Y) + Fixed.Half;
                _portalLock = new Cell3(next.X, next.Y, next.Z); _pathIdx++; _lightDirty = true;
                continue;
            }
            Fixed tx = Fixed.FromInt(next.X) + Fixed.Half, ty = Fixed.FromInt(next.Y) + Fixed.Half;
            Fixed dx = tx - _x, dy = ty - _y;
            Fixed len = Length(dx, dy);
            if (len <= Arrive) { _x = tx; _y = ty; _pathIdx++; _stuck = 0; continue; }
            Fixed s = Fixed.Min(Fixed.Min(left, MaxSubStep), len);
            Fixed mx = dx * s / len, my = dy * s / len;
            SetFacingFixed(dx, dy);
            if (TryMove(mx, my)) { _moving = true; _stuck = 0; left -= s; }
            else if (++_stuck > 10) { _path = null; return; }
            else return;
        }
        if (_pathIdx >= path.Count) _path = null;
    }

    private void SetFacingFixed(Fixed dx, Fixed dy)
    {
        if (dx.Abs() >= dy.Abs()) _facing = dx < Fixed.Zero ? Facing.West : Facing.East;
        else _facing = dy < Fixed.Zero ? Facing.North : Facing.South;
    }

    /// <summary>Moves by (mx,my), each axis on its own so the hero slides along walls. True if any movement happened.</summary>
    private bool TryMove(Fixed mx, Fixed my)
    {
        bool moved = false;
        if (mx != Fixed.Zero && Free(_x + mx, _y)) { _x += mx; moved = true; }
        if (my != Fixed.Zero && Free(_x, _y + my)) { _y += my; moved = true; }
        return moved;
    }

    /// <summary>The hero is a 0.6 m square: all four corners must stand on open cells of the current level.</summary>
    public bool Free(Fixed x, Fixed y)
    {
        int x0 = (x - Radius).Floor(), x1 = (x + Radius).Floor(), y0 = (y - Radius).Floor(), y1 = (y + Radius).Floor();
        return Grid.IsOpen(x0, y0, _z) && Grid.IsOpen(x1, y0, _z) && Grid.IsOpen(x0, y1, _z) && Grid.IsOpen(x1, y1, _z);
    }

    private void AutoPortal() => TryPortalHere(force: false);

    private bool TryPortalHere(bool force)
    {
        var here = new Cell3(_x.Floor(), _y.Floor(), _z);
        if (!force && _portalLock == here) return false;
        if (_portalLock is { } l && l != here) _portalLock = null;
        foreach (int pid in Grid.PortalsAt(Grid.Index(here.X, here.Y, here.Z)))
        {
            var p = Grid.Portals[pid];
            if (!p.Enabled) continue;
            var to = p.A == here ? p.B : p.A;
            _z = to.Z; _x = Fixed.FromInt(to.X) + Fixed.Half; _y = Fixed.FromInt(to.Y) + Fixed.Half;
            _portalLock = to; _lightDirty = true;
            return true;
        }
        return false;
    }

    /// <summary>Square root of dx^2+dy^2 in Q16.16 using integer maths only.</summary>
    public static Fixed Length(Fixed dx, Fixed dy)
    {
        long sq = (long)dx.Raw * dx.Raw + (long)dy.Raw * dy.Raw;
        return Fixed.FromRaw((int)ISqrt(sq));
    }

    private static long ISqrt(long n)
    {
        if (n <= 0) return 0;
        long x = (long)Math.Sqrt(n); // seed only; corrected below so the result is exact floor(sqrt(n))
        while (x * x > n) x--;
        while ((x + 1) * (x + 1) <= n) x++;
        return x;
    }

    // ---------------- snapshot ----------------

    private LiveSnapshot Publish()
    {
        var t = Clock.Now;
        var amb = LightModel.AmbientAt(t, LatitudeTenths);
        int minute = (int)(t.Seconds / 60);
        int cx = _x.Floor(), cy = _y.Floor();
        bool flicker = _torch && (Tick & 1) == 0;
        if (_frame == null || minute != _lastMinute || _lightDirty || _torch)
        {
            if (_frame == null || minute != _lastMinute || _lightDirty || flicker)
            {
                _dyn.Clear();
                if (_torch)
                {
                    int flick = (int)(Hash(Tick / 2) % 21) - 10;
                    _dyn.Add(new LightSource(cx, cy, _z, LightModel.TorchRadius, LightModel.TorchIntensity + flick));
                }
                _frame = _lightPair.Next(amb, _dyn);
                _lastMinute = minute; _lightDirty = false;
            }
        }
        int heroLight = _frame!.Total(cx, cy, _z);
        var hero = new HeroSnapshot(_x, _y, _z, _px, _py, _pz, _facing, _moving, _mode, _torch, _path == null ? 0 : Math.Max(0, _path.Count - _pathIdx)) { Ride = RideKind() };
        return new LiveSnapshot(Tick, t, Clock.Speed, hero, amb, _frame, heroLight, LightModel.VisionRadius(heroLight), OriginX, OriginY);
    }

    private static ulong Hash(long v)
    {
        unchecked
        {
            ulong h = (ulong)v * 0x9E3779B97F4A7C15UL;
            h ^= h >> 29; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 32;
            return h;
        }
    }
}
