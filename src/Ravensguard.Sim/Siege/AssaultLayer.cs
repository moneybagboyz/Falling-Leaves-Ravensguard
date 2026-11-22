#pragma warning disable CS0675   // hash mixing: sign extension of a small id is deterministic and harmless here
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Siege;

/// <summary>What a cell of the walls is to a fighter on one of its levels (cached until the walls change).</summary>
[Flags]
internal enum CellBits : byte { None = 0, Open = 1, Solid = 2, Merlon = 4, Gate = 8, Rubble = 16 }

/// <summary>Why a fighter is held in place by the assault (a ladder, a tower, a torch): the layer keeps him there, standing still and unable to fight, until it is over.</summary>
public enum HoldKind : byte { None, Raise, Climb, Push, Board, Torch }

/// <summary>One held fighter: what he is doing, what it belongs to (a ladder, a tower, a sally) and the tick it ends at (0: until the object ends it).</summary>
public struct Hold { public HoldKind Kind; public int Ref; public long Until; }

/// <summary>
/// P7-SG-C, the assault on several levels (sieges-and-engines 4.4): the walls of a battle bubble's fight as the chunk store has them at every level (z 0, the wall-walk at +1, towers), with ladders, a siege tower's drawbridge, a breach and
/// a broken gate as the ways between them; height and merlons for the missiles; murder holes; sallies; and what loses the wall. It is the fight's <see cref="ILevelWalls"/> and is stepped once a tick from the fight (<see cref="FightWorld.Assault"/>).
/// <para>A fighter's place has a level (0 ground, 1 the wall-walk or a tower's top deck, ...) and, on a ladder, a rise of up to a level; his floor is <c>level x level_mm + rise</c> millimetres. Cells are 1 m and are read from <see cref="WallWorld"/>,
/// the one wall rule's store, so a breach opened by an engine (SG-A's rule) is open to the next tick's feet, and rubble walks at half pace. A shut gate is a wall until it is broken or a sally opens it.</para>
/// Every blow goes through the one resolver (<see cref="FightWorld.FallFrom"/>, <see cref="FightWorld.StoneFalls"/>, <see cref="FightWorld.Scald"/>); every draw is keyed (ladder and try on 0x7C1, a missile and the merlon's cell on 0x7C2). Integers only; every walk is in ascending id order.
/// </summary>
public sealed partial class AssaultLayer : ILevelWalls
{
    public static readonly AssaultRulesDef DefaultRules = new() { Id = "assault_rules" };

    public FightWorld World { get; }
    public WallWorld Walls { get; }
    public EngineBook? Engines { get; set; }
    public EngineWorld? EngineW { get; set; }
    public AssaultRulesDef R { get; }
    /// <summary>Where the fight's (0, 0) is in the world (mm): the fight's frame is the bubble's, the walls' cells are the world's.</summary>
    public long OriginX { get; }
    public long OriginY { get; }
    public int AttackTeam { get; }
    public int DefendTeam { get; }
    /// <summary>A person's Athletics (default 30); the fight does not carry the skill.</summary>
    public Func<Fighter, int> AthleticsOf { get; set; } = _ => 30;
    public SiegeChronicle? Chronicle { get; set; }
    /// <summary>The siege entry the assault hangs from (none: a root) and the place (a settlement or castle) it is for.</summary>
    public EntityId Cause { get; set; }
    public EntityId Place { get; set; }
    /// <summary>A box of cells (x0, y0, x1, y1) that is the inside of the place: attackers standing in it count for the rule of a breach with men inside (null: that rule is off).</summary>
    public (int X0, int Y0, int X1, int Y1)? Inside { get; set; }

    public AssaultLayer(FightWorld world, WallWorld walls, int attackTeam, int defendTeam, long originX = 0, long originY = 0, AssaultRulesDef? rules = null)
    {
        World = world; Walls = walls; AttackTeam = attackTeam; DefendTeam = defendTeam; OriginX = originX; OriginY = originY;
        R = rules ?? world.Content.Db.All<AssaultRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? DefaultRules;
        world.Assault = this; world.Walls = this;
        walls.Reinforce = (x, y, z) => z == 0 && _beams.Contains(ConstructionIds.CellKey(x, y, 0)) ? R.GateBeamThreshold : 0;
    }

    // ------------------------------------------------------------------ where people stand (levels)

    private int[] _level = new int[64], _rise = new int[64];
    private int _ctx;
    private void Ensure(int id) { if (id >= _level.Length) { int n = Math.Max(id + 1, _level.Length * 2); Array.Resize(ref _level, n); Array.Resize(ref _rise, n); } }

    public int LevelOf(int fighter) => fighter >= 0 && fighter < _level.Length ? _level[fighter] : 0;
    public int RiseOf(int fighter) => fighter >= 0 && fighter < _rise.Length ? _rise[fighter] : 0;
    public void SetLevel(int fighter, int level) { Ensure(fighter); _level[fighter] = level; _rise[fighter] = 0; }

    /// <summary>Puts a fighter at a world point of a level (a defender on the walk, an archer in a tower).</summary>
    public void PutAt(int fighter, int level, long worldXMm, long worldYMm)
    {
        SetLevel(fighter, level);
        var f = World.Fighters[fighter];
        f.X = (int)(worldXMm - OriginX); f.Y = (int)(worldYMm - OriginY);
    }

    public int BaseMm(int fighter) => LevelOf(fighter) * R.LevelMm + RiseOf(fighter);

    // ------------------------------------------------------------------ the cells

    private readonly Dictionary<long, CellBits> _cells = [];
    private int _seenVersion = -1;
    private readonly SortedSet<long> _openGates = [];
    private readonly SortedSet<long> _beams = [];
    private readonly SortedSet<long> _deck = [];          // cells of level >= 1 that a tower's deck or its lowered drawbridge makes walkable (cell key with z)

    private static int CellOf(long mm) => EngineBook.CellOf(mm);

    /// <summary>Opens a gate cell (a sally leaves by it) or shuts it again; a gate that is not shut is no wall to the feet and the missiles.</summary>
    public void SetGate(int x, int y, bool open) { long k = ConstructionIds.CellKey(x, y, 0); if (open ? _openGates.Add(k) : _openGates.Remove(k)) _seenVersion = -1; }
    public bool IsGateOpen(int x, int y) => _openGates.Contains(ConstructionIds.CellKey(x, y, 0));

    /// <summary>A beam is set behind a gate cell: the gate's impact threshold rises by the content's 300 (the wall rule's <see cref="WallWorld.Reinforce"/>).</summary>
    public void SetBeam(int x, int y, bool on) { long k = ConstructionIds.CellKey(x, y, 0); if (on) _beams.Add(k); else _beams.Remove(k); }
    public bool HasBeam(int x, int y) => _beams.Contains(ConstructionIds.CellKey(x, y, 0));

    internal CellBits Cell(int x, int y, int z)
    {
        if (_seenVersion != Walls.Version) { _cells.Clear(); _seenVersion = Walls.Version; }
        long key = ConstructionIds.CellKey(x, y, z);
        if (_cells.TryGetValue(key, out var c)) return c;
        c = Compute(x, y, z, key);
        _cells[key] = c;
        return c;
    }

    private CellBits Compute(int x, int y, int z, long key)
    {
        var bits = CellBits.None;
        string? piece = Walls.StructureAt(x, y, z);
        var stat = Walls.Walls.Stat(piece);
        var kind = piece == null ? null : Walls.Costs.Get(piece)?.Kind;
        bool gate = kind == Ravensguard.Content.PieceKind.Door && stat != null;
        bool open = Walls.IsOpen(x, y, z);
        if (gate) { bits |= CellBits.Gate; open = _openGates.Contains(ConstructionIds.CellKey(x, y, 0)) && z == 0; }
        if (z >= 1 && _deck.Contains(key)) open = true;
        if (open) bits |= CellBits.Open;
        else if (kind == Ravensguard.Content.PieceKind.Wall || gate) bits |= piece == "wall_crenellated" ? CellBits.Merlon : CellBits.Solid;
        if (Walls.IsRubble(x, y, z)) bits |= CellBits.Rubble;
        return bits;
    }

    // ---- IFightWalls (the fight's own mm, the context fighter's level) ----

    public void Using(int fighterId) => _ctx = fighterId;

    public bool BlocksMove(int xMm, int yMm) => (Cell(CellOf(xMm + OriginX), CellOf(yMm + OriginY), LevelOf(_ctx)) & CellBits.Open) == 0;

    public bool BlocksMissile(int xMm, int yMm)
    {
        var c = Cell(CellOf(xMm + OriginX), CellOf(yMm + OriginY), LevelOf(_ctx));
        return (c & (CellBits.Solid | CellBits.Merlon)) != 0 && (c & CellBits.Open) == 0;
    }

    public bool CanStrike(int attacker, int target) => Math.Abs(BaseMm(attacker) - BaseMm(target)) < R.LevelMm / 2;

    public bool MissileStopped(int xMm, int yMm, int zMm, int shooter, uint serial)
    {
        int level = zMm < 0 ? 0 : Math.Min(3, zMm / R.LevelMm);
        int cx = CellOf(xMm + OriginX), cy = CellOf(yMm + OriginY);
        var c = Cell(cx, cy, level);
        if ((c & CellBits.Open) != 0) return false;
        if ((c & CellBits.Solid) != 0) return true;
        if ((c & CellBits.Merlon) == 0) return false;
        // a merlon stops a missile from below with the content's share (0x7C2: the projectile and the cell), and is no hindrance to a shooter on its own floor or above
        if (BaseMm(shooter) >= level * R.LevelMm) return false;
        return DetRng.Range(World.Seed, serial, (ulong)ConstructionIds.CellKey(cx, cy, level), RngStreams.MissileMeetsMerlon, 0, 0, 1000) < R.MerlonPm;
    }

    public int FootingPm(int fighter)
    {
        var f = World.Fighters[fighter];
        return (Cell(CellOf(f.X + OriginX), CellOf(f.Y + OriginY), LevelOf(fighter)) & CellBits.Rubble) != 0 ? R.RubbleWalkPm : 1000;
    }

    // ------------------------------------------------------------------ holds (people kept in place by a ladder, a tower or a torch)

    private readonly SortedDictionary<int, Hold> _holds = [];

    public bool IsHeld(int fighter) => _holds.ContainsKey(fighter);

    private void HoldFor(int fighter, HoldKind kind, int reference, long until) => _holds[fighter] = new Hold { Kind = kind, Ref = reference, Until = until };
    private void Release(int fighter) => _holds.Remove(fighter);

    private bool Free(Fighter f) => f.Active && !_holds.ContainsKey(f.Id);

    /// <summary>Marches a free fighter toward a world point (a ladder's foot, an engine, home): the fight's own movement does the walking against the walls. Within <paramref name="stopMm"/> he stands.</summary>
    private void March(Fighter f, long worldXMm, long worldYMm, int stopMm = 800)
    {
        long dx = worldXMm - OriginX - f.X, dy = worldYMm - OriginY - f.Y;
        if (FightMath.Len(dx, dy) <= stopMm) { f.Cmd.MoveX = 0; f.Cmd.MoveY = 0; return; }
        var (nx, ny) = FightMath.Norm(dx, dy);
        f.Cmd.MoveX = nx; f.Cmd.MoveY = ny; f.Cmd.Sprint = false;
    }

    private long DistTo(Fighter f, long worldXMm, long worldYMm) => FightMath.Len(worldXMm - OriginX - f.X, worldYMm - OriginY - f.Y);

    private static long CentreMm(int cell) => cell * 1000L + 500;

    private void KeepHeld()
    {
        List<int>? gone = null;
        foreach (var (id, h) in _holds)
        {
            var f = World.Fighters[id];
            if (!f.Active) { (gone ??= []).Add(id); continue; }
            f.Act = FAct.Stagger; f.ActTicks = Math.Max(f.ActTicks, 2);
            f.Cmd.ClearAll();
        }
        if (gone != null) foreach (int id in gone) Dropped(id);
    }

    /// <summary>A held fighter is no longer fit to be (he died, went down or fled): off the ladder he falls from where he was.</summary>
    private void Dropped(int id)
    {
        var h = _holds[id]; _holds.Remove(id);
        if (h.Kind == HoldKind.Climb && Ladder(h.Ref) is { } l) { l.Climbers.RemoveAll(c => c.Id == id); Fell(l, id, RiseOf(id) * R.LadderLenMm / Math.Max(1, R.LevelMm)); }
        if (h.Kind == HoldKind.Push && Ladder(h.Ref) is { } p && p.Pusher == id) p.Pusher = -1;
        _rise[id] = 0;
    }

    // ------------------------------------------------------------------ the tick

    private bool _held10, _heldBreach;
    public bool WalkLost => _held10;
    public bool BreachLost => _heldBreach;

    /// <summary>One tick, before the fight moves anyone: the ladders, the towers, the holes, the sallies, the engines, then the people they hold.</summary>
    public void Run()
    {
        int t = World.Tick;
        if (Engines != null) foreach (var e in Engines.Engines.Values.ToList()) Engines.Advance(e, World.GameSec, EngineW ?? NoWorld);
        TowersStep(t);
        LaddersStep(t);
        HolesStep(t);
        SalliesStep(t);
        OrdersStep();
        KeepHeld();
        if (t % 20 == 0) WallRules(t);
    }

    private static readonly EngineWorld NoWorld = new();

    /// <summary>Losing the wall (4.4): once attackers hold the walk along <c>held_walk_cells</c> cells (10 m), or <c>held_breach_men</c> of them (20) stand inside the place, the defenders' morale takes 15 points, once for each.</summary>
    private void WallRules(int t)
    {
        if (!_held10)
        {
            var walk = new HashSet<long>();
            foreach (var f in World.Fighters)
                if (f.Team == AttackTeam && f.Active && LevelOf(f.Id) >= 1 && RiseOf(f.Id) == 0) walk.Add(ConstructionIds.CellKey(CellOf(f.X + OriginX), CellOf(f.Y + OriginY), LevelOf(f.Id)));
            if (walk.Count >= R.HeldWalkCells) { _held10 = true; World.MoraleBlow(DefendTeam, R.HeldMoraleX100); }
        }
        if (!_heldBreach && Inside is { } b)
        {
            int n = 0;
            foreach (var f in World.Fighters)
            {
                if (f.Team != AttackTeam || !f.Active || LevelOf(f.Id) != 0) continue;
                int cx = CellOf(f.X + OriginX), cy = CellOf(f.Y + OriginY);
                if (cx >= b.X0 && cx <= b.X1 && cy >= b.Y0 && cy <= b.Y1) n++;
            }
            if (n >= R.HeldBreachMen) { _heldBreach = true; if (!_held10) World.MoraleBlow(DefendTeam, R.HeldMoraleX100); }
        }
    }

    // ------------------------------------------------------------------ the end

    /// <summary>The chronicle entry of an assault when it is over (<c>siege_assault</c>): who won the wall, how many each side lost. A root unless the siege entry is the <see cref="Cause"/>.</summary>
    public EntityId Finish(string outcomeKey)
    {
        int lostA = 0, lostB = 0;
        foreach (var f in World.Fighters) if (f.Act is FAct.Dead) { if (f.Team == AttackTeam) lostA++; else if (f.Team == DefendTeam) lostB++; }
        if (Chronicle == null) return default;
        return Chronicle(new GameTime(World.GameSec), "siege_assault", "ch_sg_assault",
            [ChronParam.Place("place", Place), ChronParam.TextKey("outcome", outcomeKey), ChronParam.Count("lost_a", lostA), ChronParam.Count("lost_b", lostB)], [],
            [new Cause(CauseKind.Siege, Cause == default ? null : Cause, "", "ch_note_sg_assault")],
            [("outcome", outcomeKey), ("lost_a", lostA.ToString()), ("lost_b", lostB.ToString())]);
    }

    // ------------------------------------------------------------------ hash

    public ulong Hash(ulong h)
    {
        h = FightMath.Mix(h, 0x5347_43L);
        for (int i = 0; i < World.Fighters.Count; i++) if (LevelOf(i) != 0 || RiseOf(i) != 0) { h = FightMath.Mix(h, i); h = FightMath.Mix(h, LevelOf(i) | (long)RiseOf(i) << 8); }
        foreach (var (id, hd) in _holds) { h = FightMath.Mix(h, id); h = FightMath.Mix(h, (long)hd.Kind | (long)hd.Ref << 8 | hd.Until << 32); }
        foreach (var l in _ladders) h = HashLadder(h, l);
        foreach (var tw in _towers) h = HashTower(h, tw);
        foreach (var ho in _holes) h = HashHole(h, ho);
        foreach (var s in _sallies) h = HashSally(h, s);
        h = HashOrders(h);
        foreach (long g in _openGates) h = FightMath.Mix(h, g);
        foreach (long b in _beams) h = FightMath.Mix(h, b ^ 0x4245);
        h = FightMath.Mix(h, (_held10 ? 1 : 0) | (_heldBreach ? 2 : 0));
        return h;
    }
}
