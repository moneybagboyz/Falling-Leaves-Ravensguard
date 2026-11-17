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
    public int LatitudeTenths { get; }
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
        Lights = map.BuildLightGrid();
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
    public LiveWorld(ChunkStreamer streamer, Cell3 start, GameTime startTime, int latitudeTenths = 480)
    {
        Streamer = streamer; LatitudeTenths = latitudeTenths;
        int cx = ChunkStreamer.FloorDiv(start.X, 32), cy = ChunkStreamer.FloorDiv(start.Y, 32);
        streamer.LoadAround(cx, cy, streamer.Settings.LoadRadius);
        Window = new WorldWindow(streamer, cx, cy);
        Grid = Window.Grid; Map = Window.Map; Lights = Window.Lights; _lightRev = Window.LightRevision;
        Pathfinder = new LevelPathfinder(Grid);
        Clock = new SimClock(startTime) { Speed = SimSpeed.X1 };
        _x = Fixed.FromInt(start.X - Window.OriginX) + Fixed.Half; _y = Fixed.FromInt(start.Y - Window.OriginY) + Fixed.Half; _z = start.Z;
        _px = _x; _py = _y; _pz = _z;
        Snapshot = Publish();
    }

    /// <summary>Test constructor: any grid, no scenery.</summary>
    public LiveWorld(LevelGrid grid, LightGrid lights, Cell3 start, GameTime startTime, int latitudeTenths = 480)
    {
        Map = null!; Grid = grid; Lights = lights; LatitudeTenths = latitudeTenths;
        Pathfinder = new LevelPathfinder(grid);
        Clock = new SimClock(startTime) { Speed = SimSpeed.X1 };
        _x = Fixed.FromInt(start.X) + Fixed.Half; _y = Fixed.FromInt(start.Y) + Fixed.Half; _z = start.Z;
        _px = _x; _py = _y; _pz = _z;
        Snapshot = Publish();
    }

    // ---------------- commands (applied at the next step) ----------------

    public void SetInput(MoveInput input) { _input = input; }
    public void SetSpeed(SimSpeed s) => Clock.Speed = s;
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
        Clock.Advance(TickSeconds);
        int mult = (int)Clock.Speed;
        _moving = false;
        if (_usePending) { _usePending = false; TryPortalHere(force: true); }
        if (mult > 0) MoveHero(mult);
        Snapshot = Publish();
    }

    /// <summary>Streams chunks around the hero; moves the window when the hero crosses into another chunk; stamps in chunks that finished loading.</summary>
    private void StreamStep()
    {
        var w = Window!;
        int hcx = ChunkStreamer.FloorDiv(w.OriginX + _x.Floor(), 32), hcy = ChunkStreamer.FloorDiv(w.OriginY + _y.Floor(), 32);
        Streamer!.Update(hcx, hcy);
        if (hcx != w.CenterChunkX || hcy != w.CenterChunkY)
        {
            var nw = new WorldWindow(Streamer, hcx, hcy);
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
            Window = nw; Grid = nw.Grid; Map = nw.Map; Lights = nw.Lights; _lightRev = nw.LightRevision;
            Pathfinder = new LevelPathfinder(Grid);
            _frame = null; _lightDirty = true; WindowSwaps++;
        }
        else if (w.Sync() || w.LightRevision != _lightRev) { _lightRev = w.LightRevision; _lightDirty = true; }
    }

    private Fixed SpeedOf(MoveMode m) => m switch { MoveMode.Walk => WalkSpeed, MoveMode.Sprint => SprintSpeed, _ => JogSpeed };

    private void MoveHero(int mult)
    {
        Fixed budget = SpeedOfInput() * DtPerTick * mult;
        if (_input.Any)
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
        else if (_path != null) FollowPath(budget);
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
                List<LightSource>? dyn = null;
                if (_torch)
                {
                    int flick = (int)(Hash(Tick / 2) % 21) - 10;
                    dyn = new List<LightSource> { new(cx, cy, _z, LightModel.TorchRadius, LightModel.TorchIntensity + flick) };
                }
                _frame = Lights.Compute(amb, dyn);
                _lastMinute = minute; _lightDirty = false;
            }
        }
        int heroLight = _frame!.Total(cx, cy, _z);
        var hero = new HeroSnapshot(_x, _y, _z, _px, _py, _pz, _facing, _moving, _mode, _torch, _path == null ? 0 : Math.Max(0, _path.Count - _pathIdx));
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
