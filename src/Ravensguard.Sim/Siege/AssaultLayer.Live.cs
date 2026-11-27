using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Siege;

/// <summary>P8-LW-1: the orders of a live assault, on the wheel (Shape ring, assault only). The attackers' four and the defenders' three.</summary>
public enum AssaultOrder : byte { None, LaddersToWall, TowerForward, RamGate, HoldBreach, PushLadders, ManBreach, Sally }

/// <summary>An order on its way: it reaches the layer at <see cref="Due"/> (the battle spec's delay of the voice, the horn or a runner), for a stretch of wall cells (x0, y0)-(x1, y1).</summary>
public sealed class AssaultCmd { public AssaultOrder Kind; public int Team, Due, X0, Y0, X1, Y1; }

/// <summary>An engine the assault is pushing to a point (a tower to the wall, a ram to the gate), a second of push every 20 ticks.</summary>
public sealed class EngineGoal { public ulong Vehicle; public long X, Y; }

public sealed partial class AssaultLayer
{
    /// <summary>The live rules of the spec's LW-1 (two defenders and 6 s to push a ladder, held by two climbers above half height, the hook's 300 permille) and the live AI. Off for the scenes of the earlier tests.</summary>
    public bool Live { get; set; }
    /// <summary>Which teams the layer's AI plays (bit 0 team 0, bit 1 team 1): all but the hero's.</summary>
    public int AiTeams { get; set; }
    /// <summary>The attackers come from the west (their fight frame is west of the wall); false: from the east.</summary>
    public bool FromWest { get; set; } = true;
    public const int PlanEveryTicks = 100, MinStretchCells = 4;

    private readonly List<AssaultCmd> _cmds = [];
    private readonly List<EngineGoal> _goals = [];
    private int _planAt;
    public IReadOnlyList<AssaultCmd> Cmds => _cmds;
    public IReadOnlyList<EngineGoal> Goals => _goals;
    public int PlanAt { get => _planAt; set => _planAt = value; }
    /// <summary>The siege this assault is for (saved, so a restored assault finds its siege).</summary>
    public int SiegeId { get; set; }
    /// <summary>The wall cell a fight-frame point (mm) is over.</summary>
    public (int X, int Y) CellAt(int fxMm, int fyMm) => (CellOf(fxMm + OriginX), CellOf(fyMm + OriginY));
    /// <summary>The ladders the host has timber for (-1: no limit). Each ladder sent takes one.</summary>
    public int LadderBudget { get; set; } = -1;

    /// <summary>An order from the wheel: it is held for <paramref name="delayTicks"/> (the formation book's delay for the hero's Leadership) and then acts. A stretch shorter than 4 cells is no stretch for ladders.</summary>
    public void Command(AssaultOrder kind, int team, int x0, int y0, int x1, int y1, int delayTicks)
        => _cmds.Add(new AssaultCmd { Kind = kind, Team = team, Due = World.Tick + Math.Max(0, delayTicks), X0 = Math.Min(x0, x1), Y0 = Math.Min(y0, y1), X1 = Math.Max(x0, x1), Y1 = Math.Max(y0, y1) });

    private void LiveStep(int t)
    {
        for (int i = 0; i < _cmds.Count; i++) if (_cmds[i].Due <= t) { var c = _cmds[i]; _cmds.RemoveAt(i--); Apply(c); }
        if (t % 20 == 0) GoalsStep();
        if (t >= _planAt) { _planAt = t + PlanEveryTicks; for (int team = 0; team < 2; team++) if ((AiTeams >> team & 1) != 0) Plan(team); }
    }

    // ------------------------------------------------------------------ the push (3.1): two defenders at the top for 6 s, unless two climbers are above half height; a hook holds 300 permille

    private int AboveHalf(SiegeLadder l) { int n = 0; foreach (var c in l.Climbers) if (c.Progress * 2 > R.LadderClimbTicks) n++; return n; }

    /// <summary>Returns true when the ladder has been pushed off (the caller leaves the rest of the tick).</summary>
    private bool PushLive(SiegeLadder l, int t)
    {
        if (l.Pusher >= 0 && !World.Fighters[l.Pusher].Active) { Release(l.Pusher); l.Pusher = -1; }
        if (l.Pusher2 >= 0 && !World.Fighters[l.Pusher2].Active) { Release(l.Pusher2); l.Pusher2 = -1; }
        var (tx, ty) = TopMm(l);
        foreach (var d in World.Fighters)
        {
            if (l.Pusher >= 0 && l.Pusher2 >= 0) break;
            if (d.Team == l.Team || !Free(d) || LevelOf(d.Id) < 1 || RiseOf(d.Id) != 0 || DistTo(d, tx, ty) > R.PushReachMm) continue;
            if (l.Pusher < 0) { l.Pusher = d.Id; HoldFor(d.Id, HoldKind.Push, l.Id, 0); }
            else if (l.Pusher2 < 0) { l.Pusher2 = d.Id; HoldFor(d.Id, HoldKind.Push, l.Id, 0); }
        }
        if (l.Pusher < 0 || l.Pusher2 < 0) { l.PushSince = t; return false; }
        if (AboveHalf(l) >= 2) { l.PushSince = t; return false; }                       // two men above half height hold it against the wall
        if (t - l.PushSince < 2 * R.PushTicks) return false;                              // 6 s
        if (l.Hooked && DetRng.Range(World.Seed, t, (ulong)l.Id, RngStreams.LadderPushes, 0, 0, 1000) < 300) { l.PushTries++; l.PushSince = t; return false; }
        Release(l.Pusher2); l.Pusher2 = -1;
        Topple(l, World.Fighters[l.Pusher]);
        return true;
    }

    // ------------------------------------------------------------------ the orders

    private int Dir => FromWest ? 1 : -1;

    private bool WallCell(int x, int y) { var c = Cell(x, y, 0); return (c & CellBits.Open) == 0 && (c & (CellBits.Solid | CellBits.Merlon | CellBits.Gate)) != 0; }
    private bool Walk(int x, int y) => (Cell(x, y, 1) & CellBits.Open) != 0 && (Cell(x, y, 0) & CellBits.Open) == 0;

    /// <summary>A ladder's foot and top for a wall cell: the top is the cell's walk (or a neighbour's), the foot the open ground cell beside it nearest the attackers' side.</summary>
    private bool TryLadderAt(int x, int y, out int fx, out int fy, out int tx, out int ty)
    {
        fx = fy = tx = ty = 0;
        foreach (var (ox, oy) in new[] { (0, 0), (Dir, 0), (0, 1), (0, -1) })
        {
            int cx = x + ox, cy = y + oy;
            if (!Walk(cx, cy)) continue;
            for (int k = 1; k <= 5; k++)              // the foot is the first open ground outside the wall, in the line of the walk cell (a ladder stands over the thickness of the curtain)
            {
                int gx = cx - k * Dir, gy = cy; var gc = Cell(gx, gy, 0);
                if ((gc & CellBits.Open) != 0 && (gc & CellBits.Gate) == 0) { fx = gx; fy = gy; tx = cx; ty = cy; return true; }
            }
        }
        return false;
    }

    private bool InAnyLadder(int id) { foreach (var l in _ladders) if (l.Stage != LadderStage.Down && (l.Carriers.Contains(id) || l.Queue.Contains(id) || l.Climbers.Exists(c => c.Id == id))) return true; return false; }

    private List<Fighter> Pool(int team, long nearX, long nearY)
    {
        var l = new List<Fighter>();
        foreach (var f in World.Fighters) if (f.Team == team && Free(f) && LevelOf(f.Id) == 0 && !InAnyLadder(f.Id)) l.Add(f);
        l.Sort((a, b) => { long da = DistTo(a, nearX, nearY), db = DistTo(b, nearX, nearY); return da != db ? da.CompareTo(db) : a.Id.CompareTo(b.Id); });
        return l;
    }

    /// <summary>One ladder party at a wall cell: 2 carriers and up to 10 climbers, the nearest free men of the team. False when the cell has no way up or too few men are free.</summary>
    private bool SendLadder(int team, int wx, int wy)
    {
        if (!TryLadderAt(wx, wy, out int fx, out int fy, out int tx, out int ty)) return false;
        var pool = Pool(team, CentreMm(fx), CentreMm(fy));
        if (pool.Count < 3 || LadderBudget == 0) return false;
        if (LadderBudget > 0) LadderBudget--;
        AddLadder(team, fx, fy, tx, ty, pool.Take(2).Select(f => f.Id), pool.Skip(2).Take(10).Select(f => f.Id));
        return true;
    }

    private void Apply(AssaultCmd c)
    {
        switch (c.Kind)
        {
            case AssaultOrder.LaddersToWall:
            {
                int len = Math.Max(c.X1 - c.X0, c.Y1 - c.Y0) + 1; if (len < MinStretchCells) return;
                int n = Math.Clamp(len / MinStretchCells, 1, 3), alongY = c.Y1 - c.Y0 >= c.X1 - c.X0 ? 1 : 0;
                for (int k = 0; k < n; k++)
                {
                    int x = alongY == 1 ? c.X0 : c.X0 + (len * (2 * k + 1)) / (2 * n), y = alongY == 1 ? c.Y0 + (len * (2 * k + 1)) / (2 * n) : c.Y0;
                    SendLadder(c.Team, x, y);
                }
                break;
            }
            case AssaultOrder.TowerForward:
                foreach (var tw in _towers) if (!tw.Fallen && Engines?.Find(new EntityId(tw.Vehicle)) != null) SetGoal(tw.Vehicle, CentreMm(c.X0 - Dir * 2), CentreMm((c.Y0 + c.Y1) / 2));
                break;
            case AssaultOrder.RamGate:
                if (Engines != null) foreach (var e in Engines.Engines.Values) if (e.Kind == EngineKind.Ram) SetGoal(e.Vehicle.Value, CentreMm(c.X0 - Dir), CentreMm((c.Y0 + c.Y1) / 2));
                break;
            case AssaultOrder.HoldBreach: case AssaultOrder.ManBreach:
            {
                var men = World.Fighters.Where(f => f.Team == c.Team && Free(f) && LevelOf(f.Id) == 0).Select(f => f.Id).ToList();
                Order(men, [(CentreMm(c.X0), CentreMm(c.Y0))], 3000);
                break;
            }
            case AssaultOrder.PushLadders:
                foreach (var l in _ladders)
                {
                    if (l.Stage != LadderStage.Standing || l.TopX < c.X0 - 2 || l.TopX > c.X1 + 2 || l.TopY < c.Y0 - 2 || l.TopY > c.Y1 + 2) continue;
                    var (tx, ty) = TopMm(l);
                    var near = World.Fighters.Where(f => f.Team == c.Team && Free(f) && LevelOf(f.Id) >= 1 && RiseOf(f.Id) == 0).OrderBy(f => DistTo(f, tx, ty)).ThenBy(f => f.Id).Take(2).Select(f => f.Id).ToList();
                    if (near.Count > 0) Order(near, [(tx, ty)], 900);
                }
                break;
            case AssaultOrder.Sally:
                if (Engines != null)
                    foreach (var e in Engines.Engines.Values)
                    {
                        if (e.Toppled || _sallies.Exists(s => s.Stage != SallyStage.Done && s.Engine == e.Vehicle.Value)) continue;
                        var men = World.Fighters.Where(f => f.Team == c.Team && Free(f) && LevelOf(f.Id) == 0).OrderBy(f => f.Id).Take(8).Select(f => f.Id).ToList();
                        if (men.Count == 0) break;
                        StartSally(c.Team, men, [(c.X0, c.Y0)], CentreMm(c.X0 + Dir * 3), CentreMm(c.Y0), e.Vehicle);
                        break;
                    }
                break;
        }
    }

    private void SetGoal(ulong vehicle, long x, long y)
    {
        _goals.RemoveAll(g => g.Vehicle == vehicle);
        _goals.Add(new EngineGoal { Vehicle = vehicle, X = x, Y = y });
    }

    private void GoalsStep()
    {
        for (int i = 0; i < _goals.Count; i++)
        {
            var g = _goals[i]; var e = Engines?.Find(new EntityId(g.Vehicle)); var v = e == null ? null : Engines!.VehicleOf(e);
            if (e == null || v == null || e.Toppled || FightMath.Len(g.X - v.XMm, g.Y - v.YMm) < 1500) { _goals.RemoveAt(i--); continue; }
            Engines!.Push(e, g.X, g.Y, 1, Surface.DryRoad, 0);
        }
    }

    // ------------------------------------------------------------------ the AI (3.1): ladders on 2 to 4 stretches, the tower on the widest firm approach, the ram at the gate; defenders a third to each threatened stretch

    private (int Y0, int Y1, int WallX)? Curtain()
    {
        if (Inside is not { } b) return null;
        int x = int.MinValue;
        for (int y = (b.Y0 + b.Y1) / 2; y >= b.Y0 && x == int.MinValue; y -= 2)
            for (int k = 0; k <= 16 && x == int.MinValue; k++) { int cx = FromWest ? b.X0 - 6 + k : b.X1 + 6 - k; if (WallCell(cx, y)) x = cx; }
        return x == int.MinValue ? null : (b.Y0, b.Y1, x);
    }

    /// <summary>The wall cells of the ladder stretches the attackers use: 2 to 4 of them, evenly along the curtain, the ones with a way up.</summary>
    public List<(int X, int Y)> Stretches(int men)
    {
        var res = new List<(int X, int Y)>();
        if (Curtain() is not { } c) return res;
        int want = Math.Clamp(2 + men / 200, 2, 4), span = c.Y1 - c.Y0;
        for (int k = 0; k < want; k++)
        {
            int y0 = c.Y0 + span * (k + 1) / (want + 1); bool put = false;
            for (int o = 0; o < 12 && !put; o++)
                foreach (int y in o == 0 ? new[] { y0 } : new[] { y0 + o, y0 - o })
                {
                    if (y < c.Y0 || y > c.Y1 || res.Exists(r => Math.Abs(r.Y - y) < 3)) continue;
                    if (!WallCell(c.WallX, y) || !TryLadderAt(c.WallX, y, out _, out _, out _, out _)) continue;
                    res.Add((c.WallX, y)); put = true; break;
                }
        }
        return res;
    }

    /// <summary>Men on the ground inside the wall who have no foe at hand go for the nearest one within 60 m (the formations' slots do not bring a line on a street fight inside the town): a march of their own that the brain's blows go on through.</summary>
    private void Engage(int team)
    {
        if (Curtain() is not { } c) return;
        foreach (var f in World.Fighters)
        {
            if (f.Team != team || !Free(f) || LevelOf(f.Id) != 0 || _orders.Exists(o => o.Fighter == f.Id)) continue;
            if (team == AttackTeam && (CellAt(f.X, f.Y).X - c.WallX) * Dir <= 2) continue;
            var e = World.NearestEnemyOf(f); if (e == null) continue;
            long d = FightMath.Len(e.X - f.X, e.Y - f.Y); if (d > 60_000 || d < 2500) continue;
            Order([f.Id], [(e.X + OriginX, e.Y + OriginY)], 1500);
        }
    }

    private void Plan(int team)
    {
        Engage(team);
        if (team == AttackTeam)
        {
            int live = 0; foreach (var f in World.Fighters) if (f.Team == team && f.Active) live++;
            int standing = 0; foreach (var l in _ladders) if (l.Team == team && l.Stage != LadderStage.Down) standing++;
            var stretches = Stretches(live);
            foreach (var (x, y) in stretches)
            {
                if (standing >= stretches.Count) break;
                if (_ladders.Exists(l => l.Team == team && l.Stage != LadderStage.Down && Math.Abs(l.TopY - y) < 3)) continue;
                if (SendLadder(team, x, y)) standing++;
            }
            if (World.Formations is { } book)       // the storming host advances: the captains' own caution (hold when the nearest block is as strong) would keep it at the foot of the wall, so the assault commands it and the captains stay silent for 30 s
                foreach (var row in book.Rows)
                {
                    if (row.Side != team || row.ActiveNow == 0) continue;
                    row.CaptainAlways = false; row.Micro = World.Tick;
                    if (row.Order.Kind != Ravensguard.Sim.Battle.OrderKind.Advance && !row.InContact) book.Apply(World, row, Ravensguard.Sim.Battle.FormationOrder.Of(Ravensguard.Sim.Battle.OrderKind.Advance), World.Tick);
                }
            if (Inside is { } bx && Walls.Breaches(bx.X0, bx.Y0, bx.X1, bx.Y1) is { Count: > 0 } breaches)
            {
                // the host not on a ladder goes through the breach: out in front of it, then in; the brain has them again once they are inside
                var cells = breaches[0].Cells; int cx = (int)cells.Average(q => q.X), cy = (int)cells.Average(q => q.Y);
                var men = World.Fighters.Where(f => f.Team == team && Free(f) && LevelOf(f.Id) == 0 && !InAnyLadder(f.Id) && !_orders.Exists(o => o.Fighter == f.Id)
                    && (CellAt(f.X, f.Y).X - cx) * Dir < 0).Select(f => f.Id).ToList();
                if (men.Count > 0) Order(men, [(CentreMm(cx - Dir * 5), CentreMm(cy)), (CentreMm(cx + Dir * 8), CentreMm(cy))], 2500);
            }
            if (Curtain() is { } c)
            {
                int midY = (c.Y0 + c.Y1) / 2;
                foreach (var tw in _towers) if (!tw.Fallen && !tw.Down && !_goals.Exists(g => g.Vehicle == tw.Vehicle) && Engines?.Find(new EntityId(tw.Vehicle)) != null) SetGoal(tw.Vehicle, CentreMm(c.WallX - Dir * 2), CentreMm(midY));
                if (Engines != null) foreach (var e in Engines.Engines.Values) if (e.Kind == EngineKind.Ram && !e.Toppled && !_goals.Exists(g => g.Vehicle == e.Vehicle.Value)) SetGoal(e.Vehicle.Value, CentreMm(c.WallX - Dir), CentreMm(midY));
            }
        }
        else
        {
            foreach (var l in _ladders)           // the men of the walk go to a standing ladder's top, two to push it
            {
                if (l.Team == team || l.Stage != LadderStage.Standing || l.Pusher >= 0) continue;
                var (tx, ty) = TopMm(l);
                var near = World.Fighters.Where(f => f.Team == team && Free(f) && LevelOf(f.Id) >= 1 && RiseOf(f.Id) == 0 && !_orders.Exists(o => o.Fighter == f.Id)).OrderBy(f => DistTo(f, tx, ty)).ThenBy(f => f.Id).Take(2).Select(f => f.Id).ToList();
                if (near.Count > 0) Order(near, [(tx, ty)], 900);
            }
            foreach (var l in _ladders)           // reinforcements climb the wall stairs to a threatened top that has too few men: the nearest free men of the town within 15 m, up in a step
            {
                if (l.Team == team || l.Stage != LadderStage.Standing) continue;
                var (tx, ty) = TopMm(l);
                int up = World.Fighters.Count(f => f.Team == team && f.Active && LevelOf(f.Id) >= 1 && DistTo(f, tx, ty) <= 4000);
                if (up >= 6) continue;
                foreach (var f in World.Fighters.Where(f => f.Team == team && Free(f) && LevelOf(f.Id) == 0 && !_orders.Exists(o => o.Fighter == f.Id) && DistTo(f, tx, ty) <= 15000).OrderBy(f => DistTo(f, tx, ty)).ThenBy(f => f.Id).Take(Math.Min(2, 6 - up)).ToList())
                    PutAt(f.Id, 1, tx, ty + (f.Id % 5 - 2) * 400);
            }
        }
    }

    /// <summary>
    /// The starting geometry (3.1): the defenders' first men go up on the walk, a third to each threatened stretch (the stretches the ladders will come to), the rest stay at the breach and the gate. Called once when the assault opens, before the first tick.
    /// </summary>
    public void Arrange(IReadOnlyList<int> defenders)
    {
        var stretches = Stretches(World.Fighters.Count(f => f.Team == AttackTeam));
        int third = Math.Max(1, defenders.Count / 3), at = 0;
        for (int s = 0; s < stretches.Count && s < 3 && at < defenders.Count; s++)
        {
            var (x, y) = stretches[s];
            if (!TryLadderAt(x, y, out _, out _, out int tx, out int ty)) continue;
            for (int k = 0; k < third && at < defenders.Count; k++)
            {
                int dy = ty + ((k + 1) / 2) * (k % 2 == 0 ? 1 : -1);
                if (!Walk(tx, dy)) dy = ty;
                PutAt(defenders[at++], 1, CentreMm(tx), CentreMm(dy) + (k % 4 - 1) * 200);
            }
        }
        _planAt = World.Tick;
    }

    private ulong HashLive(ulong h)
    {
        h = FightMath.Mix(h, 0x4C57_31 | (long)AiTeams << 32 | (FromWest ? 1L << 40 : 0)); h = FightMath.Mix(h, _planAt); h = FightMath.Mix(h, LadderBudget);
        foreach (var c in _cmds) h = FightMath.Mix(h, (long)c.Kind | (long)c.Team << 8 | (long)c.Due << 16 ^ c.X0 ^ (long)c.Y0 << 20 ^ (long)c.X1 << 40 ^ (long)c.Y1 << 52);
        foreach (var g in _goals) { h = FightMath.Mix(h, (long)g.Vehicle); h = FightMath.Mix(h, g.X); h = FightMath.Mix(h, g.Y); }
        return h;
    }
}
