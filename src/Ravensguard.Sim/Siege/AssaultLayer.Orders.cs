#pragma warning disable CS0675   // hash mixing: sign extension of a small id is deterministic and harmless here
using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Siege;

/// <summary>A fighter's march along waypoints (world mm) that the assault gives him: the breach party through a breach, a sally to the gate. He walks them in order, each within <see cref="Stop"/> mm of the point, and then the brain has him again.</summary>
public sealed class MarchOrder
{
    public int Fighter, Index, Stop = 1200;
    public List<(long X, long Y)> Points = [];
}

public sealed partial class AssaultLayer
{
    private readonly List<MarchOrder> _orders = [];
    public IReadOnlyList<MarchOrder> Orders => _orders;

    /// <summary>Gives each fighter the same waypoints to walk (a party through a breach): while he walks them the layer overrides his brain's moves, not his blows.</summary>
    public void Order(IEnumerable<int> fighters, IEnumerable<(long X, long Y)> points, int stopMm = 1200)
    {
        var pts = points.ToList();
        foreach (int id in fighters.OrderBy(i => i)) { _orders.RemoveAll(o => o.Fighter == id); _orders.Add(new MarchOrder { Fighter = id, Points = [.. pts], Stop = stopMm }); }
    }

    private void OrdersStep()
    {
        for (int i = 0; i < _orders.Count; i++)
        {
            var o = _orders[i]; var f = World.Fighters[o.Fighter];
            if (!f.Active) { _orders.RemoveAt(i--); continue; }
            if (!Free(f)) continue;
            var (px, py) = o.Points[o.Index];
            if (DistTo(f, px, py) <= o.Stop) { if (++o.Index >= o.Points.Count) { _orders.RemoveAt(i--); continue; } (px, py) = o.Points[o.Index]; }
            March(f, px, py, 300);
        }
    }

    private ulong HashOrders(ulong h)
    {
        foreach (var o in _orders) { h = FightMath.Mix(h, o.Fighter | (long)o.Index << 24 | (long)o.Stop << 40); foreach (var (x, y) in o.Points) { h = FightMath.Mix(h, x); h = FightMath.Mix(h, y); } }
        return h;
    }
}
