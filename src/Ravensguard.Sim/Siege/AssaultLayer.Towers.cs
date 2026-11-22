#pragma warning disable CS0675   // hash mixing: sign extension of a small id is deterministic and harmless here
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Naval;

namespace Ravensguard.Sim.Siege;

/// <summary>
/// A siege tower at the wall as the assault sees it (sieges-and-engines 3.2, 4.4): its top deck is a floor of the wall-walk's level (the cells of its footprint), and when its front stands within the drawbridge's reach of the walk (1.5 m) the bridge is lowered:
/// a two-metre front of cells over the gap and the parapet to the first cell of the walk, open to the feet of anyone on the deck. The tower does not carry people while it rolls (it is pushed up to the wall first, the party boards at its foot, 6 s).
/// </summary>
public sealed class TowerPost
{
    public ulong Vehicle;
    public long X, Y; public int Facing;
    public bool Down, Fallen;
    /// <summary>Deck cells and bridge cells (cell keys at the tower's top level).</summary>
    public List<long> Deck = [], Bridge = [];
    public int Aboard;
}

public sealed partial class AssaultLayer
{
    private readonly List<TowerPost> _towers = [];
    public IReadOnlyList<TowerPost> Towers => _towers;
    public TowerPost? TowerOf(EntityId vehicle) { foreach (var t in _towers) if (t.Vehicle == vehicle.Value) return t; return null; }

    /// <summary>Takes an engine of the tower kind into the assault.</summary>
    public TowerPost AddTower(EntityId vehicle)
    {
        var t = TowerOf(vehicle);
        if (t != null) return t;
        t = new TowerPost { Vehicle = vehicle.Value, X = long.MinValue };
        _towers.Add(t);
        return t;
    }

    /// <summary>The party boards the tower at its foot: they stand 6 s at its ladders, then are on the top deck, a cell each in turn.</summary>
    public void Board(EntityId vehicle, IEnumerable<int> fighters)
    {
        var t = AddTower(vehicle); int idx = _towers.IndexOf(t);
        foreach (int id in fighters) HoldFor(id, HoldKind.Board, idx, World.Tick + R.BoardTicks);
    }

    private int TopZ(EntityId vehicle) => Engines?.Find(vehicle) is { } e ? Math.Max(1, Engines.Stat(e).TopZ) : 1;

    private void TowersStep(int t)
    {
        if (Engines == null) return;
        for (int ti = 0; ti < _towers.Count; ti++)
        {
            var tw = _towers[ti]; var id = new EntityId(tw.Vehicle);
            var e = Engines.Find(id); var v = e == null ? null : Engines.VehicleOf(e);
            if (e == null || v == null) continue;
            int z = TopZ(id);
            if (e.Toppled && !tw.Fallen) { Topple(tw, z); continue; }
            if (tw.Fallen) continue;
            if (tw.X != v.XMm || tw.Y != v.YMm || tw.Facing != v.Facing) Lay(tw, v, z);
            if (!tw.Down) TryBridge(tw, e, v, z);
            // the party that boarded stands on the deck when its time is up
            foreach (var (fid, h) in _holds.ToList())
            {
                if (h.Kind != HoldKind.Board || h.Ref != ti || t < h.Until) continue;
                Release(fid);
                if (tw.Deck.Count == 0) continue;
                var (cx, cy, _) = ConstructionIds.Unpack(tw.Deck[tw.Aboard++ % tw.Deck.Count]);
                var f = World.Fighters[fid]; Ensure(fid);
                _level[fid] = z; _rise[fid] = 0;
                f.X = (int)(CentreMm(cx) - OriginX); f.Y = (int)(CentreMm(cy) - OriginY);
                if (f.Act == FAct.Stagger) { f.Act = FAct.Idle; f.ActTicks = 0; }
            }
        }
    }

    private void SetDeck(IEnumerable<long> cells, bool on) { foreach (long k in cells) { if (on) _deck.Add(k); else _deck.Remove(k); } _seenVersion = -1; }

    /// <summary>The tower stands somewhere new: its deck is the cells of its footprint at the top level; a lowered bridge is raised again.</summary>
    private void Lay(TowerPost tw, VehicleRow v, int z)
    {
        SetDeck(tw.Deck, false); SetDeck(tw.Bridge, false); tw.Bridge = []; tw.Down = false;
        tw.X = v.XMm; tw.Y = v.YMm; tw.Facing = v.Facing;
        var (w, l) = Engines!.Size(v); int reach = (Math.Max(w, l) + 2) / 2 + 1;
        int cx0 = CellOf(v.XMm), cy0 = CellOf(v.YMm);
        tw.Deck = [];
        for (int cy = cy0 - reach; cy <= cy0 + reach; cy++)
            for (int cx = cx0 - reach; cx <= cx0 + reach; cx++)
                if (Engines.CellAt(v, CentreMm(cx), CentreMm(cy)) != null) tw.Deck.Add(ConstructionIds.CellKey(cx, cy, z));
        SetDeck(tw.Deck, true);
    }

    /// <summary>Lowers the drawbridge when the first cell of the walk's level that holds a wall is within its reach of the tower's front: the cells from the front over the gap and the parapet to the walk, two wide, become open floor at that level.</summary>
    private void TryBridge(TowerPost tw, Engine e, VehicleRow v, int z)
    {
        int reachMm = Engines!.Stat(e).BridgeMm;
        var (fx, fy) = Engines.FrontOf(v);
        var (cos, sin) = FrameMath.CosSin(v.Facing << 16);
        var (ax, ay) = FrameMath.ToWorld((int)v.XMm, (int)v.YMm, cos, sin, 0, 1000);
        long dx = ax - v.XMm, dy = ay - v.YMm;                       // one metre ahead, in mm
        long lx = -dy, ly = dx;                                       // one metre to the left
        int hit = -1;
        for (int s = 0; s <= reachMm + 4000; s += 100)
        {
            long px = fx + dx * s / 1000, py = fy + dy * s / 1000;
            int cx = CellOf(px), cy = CellOf(py);
            if (tw.Deck.Contains(ConstructionIds.CellKey(cx, cy, z))) continue;
            if (Walls.StructureAt(cx, cy, z) == null) continue;
            hit = s; break;
        }
        if (hit < 0 || hit > reachMm) return;
        // from the front to the first open walk cell behind the parapet, over the cells between, a metre either side of the centre line
        var cells = new SortedSet<long>(); bool landed = false;
        for (int s = 0; s <= hit + 4000 && !landed; s += 100)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                long px = fx + dx * s / 1000 + lx * side * 500 / 1000, py = fy + dy * s / 1000 + ly * side * 500 / 1000;
                int cx = CellOf(px), cy = CellOf(py);
                long key = ConstructionIds.CellKey(cx, cy, z);
                if (tw.Deck.Contains(key)) continue;
                if (s > hit && (Cell(cx, cy, z) & CellBits.Open) != 0 && Walls.StructureAt(cx, cy, z) != null) landed = true;
                cells.Add(key);
            }
        }
        if (!landed) return;
        tw.Bridge = [.. cells]; tw.Down = true;
        SetDeck(tw.Bridge, true);
    }

    /// <summary>A tower that lost its base falls: its deck and bridge are gone and everyone on the deck drops to the ground with a fall's wounds.</summary>
    private void Topple(TowerPost tw, int z)
    {
        tw.Fallen = true;
        var cells = new HashSet<long>(tw.Deck); foreach (long b in tw.Bridge) cells.Add(b);
        SetDeck(tw.Deck, false); SetDeck(tw.Bridge, false);
        foreach (var f in World.Fighters)
        {
            if (!f.Active || LevelOf(f.Id) < z || RiseOf(f.Id) != 0) continue;
            if (!cells.Contains(ConstructionIds.CellKey(CellOf(f.X + OriginX), CellOf(f.Y + OriginY), LevelOf(f.Id)))) continue;
            int h = LevelOf(f.Id) * R.LevelMm; _level[f.Id] = 0;
            World.FallFrom(f, h, (tw.Vehicle << 8) ^ (ulong)f.Id ^ (ulong)World.Tick << 32);
        }
        tw.Deck = []; tw.Bridge = []; tw.Down = false;
    }

    private ulong HashTower(ulong h, TowerPost t)
    {
        h = FightMath.Mix(h, (long)t.Vehicle); h = FightMath.Mix(h, t.X); h = FightMath.Mix(h, t.Y); h = FightMath.Mix(h, t.Facing | (t.Down ? 1L << 20 : 0) | (t.Fallen ? 1L << 21 : 0) | (long)t.Aboard << 24);
        foreach (long k in t.Deck) h = FightMath.Mix(h, k);
        h = FightMath.Mix(h, -1); foreach (long k in t.Bridge) h = FightMath.Mix(h, k);
        return h;
    }
}
