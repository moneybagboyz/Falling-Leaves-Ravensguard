using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Siege;

namespace Ravensguard.Game.World.Combat;

/// <summary>One sector of the order wheel: its text key, the formation order it gives, and the Phase 4 squad input it stands for when the hero has no army (a squad is formation 1).</summary>
public sealed record WheelItem(string Key, OrderKind Kind, Formation SquadShape = Formation.Auto, SquadOrderKind? SquadOrder = null, bool Off = false, AssaultOrder? Assault = null);

/// <summary>
/// The command layer of the battle view (P7-BB-D, battle-bubble-and-formations 4.8): which formations the player has selected (a view choice, not game state), the order wheel's rings, and how an order
/// leaves the UI. Every order goes out as a recorded fight input through <see cref="FightView.Send"/> (<see cref="FormationCommands"/>), where the sim issues it with the hero's Leadership delay; while the
/// tactical overlay is open it is drafted instead and sent when the game resumes, so the delay runs from the paused tick and a pause gives no free time (BB-11). It reads the fight; it never writes it.
/// </summary>
public sealed class BattleCommand
{
    public const double DoubleTapSeconds = 0.4;
    public static readonly OrderKind[] Rings0 = [OrderKind.Advance, OrderKind.Charge, OrderKind.Hold, OrderKind.Retreat, OrderKind.FollowMe, OrderKind.FlankLeft, OrderKind.FlankRight, OrderKind.Rally];

    readonly FightView _v;
    readonly SortedSet<byte> _selected = [];
    readonly Dictionary<byte, (int X, int Y, int Tick)> _ghosts = [];
    double _tapAt = -10; int _tapKey;
    int _observedTick = -1, _lostSeen;

    public BattleCommand(FightView v) { _v = v; }

    /// <summary>The overlay is open: placements are drafted, not sent.</summary>
    public bool Staging { get; set; }
    public OrderDraft Draft { get; } = new();
    /// <summary>With no formations the squad itself is formation 1 (overview Q9); F1 selects it and the wheel's shapes are Phase 4's.</summary>
    public bool SquadSelected { get; private set; }
    public IReadOnlyCollection<byte> Selected => _selected;
    public FightWorld W => _v.W;
    public int Team => _v.HeroF?.Team ?? 0;
    public FormationBook? Book => W.Formations;
    /// <summary>The battle bubble the view shows (counts of the reserve and the waves), or null in an ordinary fight.</summary>
    public BattleBubble? Bubble => _v.Bubble;

    public static int Standing(FightWorld w, FormationRow r) { int n = 0; foreach (int id in r.Members) if (w.Fighters[id].Active) n++; return n; }
    public int Standing(FormationRow r) => Standing(W, r);

    /// <summary>The hero's side's formations that still have men on their feet, by number.</summary>
    public List<FormationRow> Mine()
    {
        var l = new List<FormationRow>();
        if (Book == null) return l;
        foreach (var r in Book.Rows) if (r.Side == Team && Standing(r) > 0) l.Add(r);
        l.Sort((a, b) => a.Id.CompareTo(b.Id));
        return l;
    }

    public List<FormationRow> Foes()
    {
        var l = new List<FormationRow>();
        if (Book == null) return l;
        foreach (var r in Book.Rows) if (r.Side != Team && Standing(r) > 0) l.Add(r);
        return l;
    }

    /// <summary>True when the view has something to command: a formation of the hero's side, or (no army) the squad of the hero's side.</summary>
    public bool Active => _v.HeroF != null && (Book != null && Book.Rows.Any(r => r.Side == Team));

    // ------------------------------------------------------------------ selection

    /// <summary>F1 to F4 (and Shift+F1 to F4 with <paramref name="add"/>): formation <paramref name="k"/> of the side, which is its number if there is one, else the k-th. Twice within 0.4 s: true, centre on it.</summary>
    public bool Select(int k, bool add, double now)
    {
        var rows = Mine();
        bool dbl = _tapKey == k && now - _tapAt <= DoubleTapSeconds;
        _tapKey = k; _tapAt = now;
        if (rows.Count == 0)
        {
            if (k == 1 && _v.HeroF != null) SquadSelected = true;
            return false;
        }
        var r = rows.FirstOrDefault(x => x.Id == k) ?? (k <= rows.Count ? rows[k - 1] : null);
        if (r == null) { _v.Hud.Toast(L.T("bcmd_toast_none")); return false; }
        SelectRow(r.Id, add);
        return dbl;
    }

    public void SelectRow(byte id, bool add)
    {
        SquadSelected = false;
        if (!add) _selected.Clear();
        _selected.Add(id);
    }

    public bool IsSelected(FormationRow r) => _selected.Contains(r.Id);

    /// <summary>Tab in the overlay: the next formation of the side, 5 and up included.</summary>
    public void Next()
    {
        var rows = Mine(); if (rows.Count == 0) return;
        int at = rows.FindIndex(r => _selected.Contains(r.Id));
        SelectRow(rows[(at + 1) % rows.Count].Id, false);
    }

    /// <summary>The formations an order goes to: the selection, else formation 1 (battle 4.8: "with nothing selected, the order goes to formation 1").</summary>
    public List<FormationRow> Targets()
    {
        var rows = Mine();
        var sel = rows.Where(r => _selected.Contains(r.Id)).ToList();
        if (sel.Count == 0 && rows.Count > 0) sel.Add(rows.FirstOrDefault(r => r.Id == 1) ?? rows[0]);
        return sel;
    }

    public (int X, int Y)? CentreOf(FormationRow r)
    {
        long sx = 0, sy = 0; int n = 0;
        foreach (int id in r.Members) { var f = W.Fighters[id]; if (!f.Active) continue; sx += f.X; sy += f.Y; n++; }
        return n == 0 ? null : ((int)(sx / n), (int)(sy / n));
    }

    // ------------------------------------------------------------------ the wheel's rings

    public IReadOnlyList<string> RingNames { get; } = ["bcmd_ring_orders", "bcmd_ring_shape", "bcmd_ring_missiles"];

    /// <summary>The sectors of ring 0 (Orders), 1 (Shape) or 2 (Missiles). Without an army the shapes are Phase 4's four and the orders a squad cannot take are shown but off.</summary>
    public List<WheelItem> Ring(int ring)
    {
        var items = RingFormations(ring);
        if (ring == 1) items.AddRange(AssaultItems());
        return items;
    }

    /// <summary>P8-LW-1: in a live assault the Shape ring also holds the assault orders of the hero's side: ladders, tower, ram and hold the breach for the attackers; push ladders, man the breach and sally for the defenders.</summary>
    public List<WheelItem> AssaultItems()
    {
        if (W.Assault is not { Live: true } a || _v.HeroF is not { } hero) return [];
        return hero.Team == a.AttackTeam
            ? [new("bcmd_assault_ladders", OrderKind.None, Assault: AssaultOrder.LaddersToWall), new("bcmd_assault_tower", OrderKind.None, Assault: AssaultOrder.TowerForward), new("bcmd_assault_ram", OrderKind.None, Assault: AssaultOrder.RamGate), new("bcmd_assault_hold", OrderKind.None, Assault: AssaultOrder.HoldBreach)]
            : [new("bcmd_assault_push", OrderKind.None, Assault: AssaultOrder.PushLadders), new("bcmd_assault_man", OrderKind.None, Assault: AssaultOrder.ManBreach), new("bcmd_assault_sally", OrderKind.None, Assault: AssaultOrder.Sally)];
    }

    /// <summary>An assault order waiting for the stretch of wall the player draws (RMB-drag a line, or click a point) before it is sent.</summary>
    public AssaultOrder? PendingAssault { get; private set; }

    /// <summary>A wheel item of the assault: the orders that need no stretch go at once (to the middle of the wall), the others wait for the player to draw one.</summary>
    public void GiveAssault(AssaultOrder k)
    {
        if (W.Assault is not { Live: true } a) return;
        if (k is AssaultOrder.TowerForward or AssaultOrder.RamGate or AssaultOrder.Sally)
        {
            var b = a.Inside ?? (0, 0, 0, 0); int x = k == AssaultOrder.Sally || a.FromWest ? b.X0 : b.X1, y = (b.Y0 + b.Y1) / 2;
            SendAssault(k, x, y, x, y); PendingAssault = null; return;
        }
        PendingAssault = k;
    }

    void SendAssault(AssaultOrder k, int x0, int y0, int x1, int y1) => Place(0, AssaultCommands.Order(k, x0, y0, x1, y1));

    /// <summary>The drawn line (or point) of the player is the stretch of the pending assault order: it goes out and the order is no longer pending. False when none was pending.</summary>
    bool TakePending((int X, int Y) a, (int X, int Y) b)
    {
        if (PendingAssault is not { } k || W.Assault is not { } la) return false;
        var (x0, y0) = la.CellAt(a.X, a.Y); var (x1, y1) = la.CellAt(b.X, b.Y);
        PendingAssault = null; SendAssault(k, x0, y0, x1, y1);
        return true;
    }

    List<WheelItem> RingFormations(int ring)
    {
        bool squad = Mine().Count == 0;
        switch (ring)
        {
            case 0: return Rings0.Select(k => new WheelItem(Name(k), k, Off: squad)).ToList();
            case 1:
                return squad
                    ? [new("bcmd_shape_line", OrderKind.Line, Formation.Line), new("bcmd_shape_shield_wall", OrderKind.ShieldWall, Formation.ShieldWall), new("bcmd_shape_loose", OrderKind.Loose, Formation.Loose), new("bcmd_shape_fall_back", OrderKind.None, Formation.FallBack)]
                    : [new("bcmd_shape_line", OrderKind.Line), new("bcmd_shape_shield_wall", OrderKind.ShieldWall), new("bcmd_shape_loose", OrderKind.Loose), new("bcmd_shape_column", OrderKind.Column), new("bcmd_order_set_against_cavalry", OrderKind.SetAgainstCavalry),
                       new("bcmd_shape_wedge", OrderKind.None, Off: true)];                        // the wedge is the cavalry layer's
            default:
                return [new("bcmd_order_fire_at_will", OrderKind.FireAtWill, SquadOrder: SquadOrderKind.Clear), new("bcmd_order_hold_fire", OrderKind.HoldFire, SquadOrder: SquadOrderKind.HoldFire), new("bcmd_order_shoot_at", OrderKind.None, Off: true)];
        }
    }

    public static string Name(OrderKind k) => "bcmd_order_" + FormationRules.Snake(k.ToString());

    // ------------------------------------------------------------------ giving orders

    /// <summary>Gives a wheel item to the selection (or, with no army, the squad).</summary>
    public void Give(WheelItem it, bool queue = false)
    {
        if (it.Assault is { } ao) { GiveAssault(ao); return; }
        if (it.Off) return;
        var rows = Targets();
        if (rows.Count == 0)
        {
            if (it.SquadShape != Formation.Auto) _v.Send(new FightInput(FightInputKind.Formation, (int)it.SquadShape));
            else if (it.SquadOrder is { } so)
            {
                bool hold = W.OrdersOf(Team).HoldFire;
                if (so == SquadOrderKind.HoldFire != hold) _v.Send(new FightInput(FightInputKind.Order, (int)SquadOrderKind.HoldFire, -1));      // the squad's hold fire is a toggle
            }
            return;
        }
        foreach (var r in rows) Place(r.Id, FormationCommands.Order(r.Id, it.Kind, queue: queue));
    }

    /// <summary>An order at an enemy formation (a click on its block): <paramref name="enemyRowIndex"/> is its index in the book.</summary>
    public void Order(byte row, OrderKind kind, int enemyRowIndex, bool queue) => Place(row, FormationCommands.At(row, kind, enemyRowIndex, queue));

    /// <summary>LMB-drag a block to a point: a <see cref="OrderKind.MoveTo"/>.</summary>
    public void MoveTo(byte row, int x, int y, bool queue) { if (TakePending((x, y), (x, y))) return; Place(row, FormationCommands.Order(row, OrderKind.MoveTo, x, y, queue)); }

    /// <summary>RMB-drag a line: stand there, as wide as the line, facing across it toward where the formation faces now.</summary>
    public void Draw(byte row, (int X, int Y) a, (int X, int Y) b, bool queue)
    {
        if (TakePending(a, b)) return;
        var r = Book?.Find(Team, row); if (r == null) return;
        long dx = b.X - a.X, dy = b.Y - a.Y;
        int len = (int)FightMath.Len(dx, dy);
        if (len < 3000) { MoveTo(row, (a.X + b.X) / 2, (a.Y + b.Y) / 2, queue); return; }
        var (nx, ny) = FightMath.Norm(-dy, dx);
        if (FightMath.Dot(nx, ny, r.FaceX, r.FaceY) < 0) { nx = -nx; ny = -ny; }
        Place(row, FormationCommands.Order(row, OrderKind.MoveTo, (a.X + b.X) / 2, (a.Y + b.Y) / 2, queue), FormationCommands.Face(row, nx, ny, len));
    }

    public void ToggleCaptain(byte row)
    {
        var r = Book?.Find(Team, row); if (r == null) return;
        Place(row, FormationCommands.Captain(row, !r.CaptainAlways));
    }

    void Place(byte row, params FightInput[] inputs)
    {
        if (Staging) Draft.Place(row, inputs);
        else foreach (var i in inputs) _v.Send(i);
    }

    /// <summary>The overlay resumes: what was placed goes out now, at the tick the game was paused at.</summary>
    public void Commit() { Staging = false; Draft.Commit(_v.Send); }

    /// <summary>Ticks and signal an order of this kind would take to reach the first target now (the wheel's tooltip). Null: nothing to command.</summary>
    public (int Ticks, OrderSignal Signal)? Preview(OrderKind kind)
    {
        var rows = Targets(); var hero = _v.HeroF;
        if (rows.Count == 0 || hero == null || Book == null) return null;
        var o = FormationOrder.Of(kind);
        int ticks = Book.DelayOf(W, rows[0], ref o, hero.Id, Book.HeroLeadership, out _);
        return (ticks, o.Signal);
    }

    // ------------------------------------------------------------------ what the overlay may show of the enemy

    /// <summary>The enemy formations the hero's side sees now (a man of his within 300 m), and the last-seen ghosts of the rest. Presentation memory only: it resets with the view.</summary>
    public void Observe(out List<(FormationRow Row, int X, int Y)> seen, out List<(FormationRow Row, int X, int Y, int AgeTicks)> ghosts)
    {
        seen = []; ghosts = [];
        var foes = Foes();
        var mine = new List<Fighter>();
        foreach (var r in Mine()) foreach (int id in r.Members) { var f = W.Fighters[id]; if (f.Active) mine.Add(f); }
        foreach (var e in foes)
        {
            var c = CentreOf(e); if (c == null) continue;
            bool vis = false;
            foreach (var f in mine) { long dx = f.X - c.Value.X, dy = f.Y - c.Value.Y; if (dx * dx + dy * dy <= 300_000L * 300_000L) { vis = true; break; } }
            if (vis) { seen.Add((e, c.Value.X, c.Value.Y)); _ghosts[e.Id] = (c.Value.X, c.Value.Y, W.Tick); }
            else if (_ghosts.TryGetValue(e.Id, out var g)) ghosts.Add((e, g.X, g.Y, W.Tick - g.Tick));
        }
        _observedTick = W.Tick;
    }

    /// <summary>The count of orders lost to a fallen runner since the last call (a toast).</summary>
    public int NewLost()
    {
        int n = Book?.Lost.Count ?? 0, d = n - _lostSeen; _lostSeen = n; return d;
    }
}
