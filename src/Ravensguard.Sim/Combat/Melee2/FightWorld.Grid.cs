using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// The fight's neighbour queries (BB-A, battle-bubble-and-formations 2.1, 4.2): every scan that used to visit the whole fighter list to find the few within some radius now asks the
/// <see cref="BattleGrid"/> for the ids that could be that close, ascending, and keeps its own exact test on each. Same order, same tests, so same results, draws and hashes (BB-1);
/// with <see cref="UseGrid"/> false a query returns every id, which is the old scan itself (the oracle of BB-1 and BB-2).
/// <para>Nesting: a scan that holds a result while calling another scan uses a different <c>depth</c> (0 for a top-level scan, 1 and 2 for the shield-wall helpers called from inside one).</para>
/// </summary>
public sealed partial class FightWorld
{
    /// <summary>False: every neighbour scan visits all fighters in id order (the pre-grid behaviour). Test BB-1 runs both and compares fight hashes.</summary>
    public bool UseGrid = true;

    public BattleGrid Grid { get; } = new();

    /// <summary>
    /// The fighters that could be within <paramref name="r"/> mm of (x, y), ascending id (a superset: the caller tests the exact distance). Rebuilds the grid first when it has drifted
    /// or the fight has grown. The span is valid until the next query at the same depth.
    /// </summary>
    public ReadOnlySpan<int> Near(int depth, int x, int y, long r)
    {
        if (!UseGrid) return Grid.Identity(Fighters.Count);
        if (Grid.Stale(Fighters.Count)) Grid.Rebuild(Fighters);
        return Grid.Near(depth, x, y, r);
    }

    public ReadOnlySpan<int> Near(int depth, Fighter c, long r) => Near(depth, c.X, c.Y, r);

    /// <summary><see cref="Near(int, int, int, long)"/> in no particular order, for a scan that does not depend on the order (P7 CV-A: the grid's sort was most of a cavalry scan's cost).</summary>
    public ReadOnlySpan<int> NearUnsorted(int depth, Fighter c, long r)
    {
        if (!UseGrid) return Grid.Identity(Fighters.Count);
        if (Grid.Stale(Fighters.Count)) Grid.Rebuild(Fighters);
        return Grid.NearUnsorted(depth, c.X, c.Y, r);
    }

    /// <summary>The grid rebuilt now (after movement, after separation: the cheap counting sort that keeps the drift, and so the query boxes, small).</summary>
    private void RebuildGrid() { if (UseGrid) Grid.Rebuild(Fighters); }

    /// <summary>The nearest active enemy of <paramref name="f"/> (the lowest id among equals), searching outward in doubling boxes; the same answer as the full scan.</summary>
    public Fighter? NearestEnemyOf(Fighter f)
    {
        long r = 16_000;
        while (true)
        {
            bool all = !UseGrid || r >= (1L << 33);
            var ids = all ? Near(0, f.X, f.Y, 1L << 41) : Near(0, f.X, f.Y, r);
            Fighter? best = null; long bd = long.MaxValue;
            foreach (int id in ids)
            {
                var e = Fighters[id];
                if (e.Team == f.Team || !e.Active) continue;
                long d = DistMm(f, e);
                if (d < bd) { bd = d; best = e; }
            }
            if (all || best != null && bd <= r) return best;
            r *= 2;
        }
    }

    /// <summary>True when some active enemy of <paramref name="f"/> is within <paramref name="mm"/> (a bounded query; nothing farther is ever looked at).</summary>
    internal bool AnyEnemyWithin(Fighter f, long mm)
    {
        foreach (int id in Near(0, f, mm))
        {
            var e = Fighters[id];
            if (e.Team != f.Team && e.Active && DistMm(e, f) <= mm) return true;
        }
        return false;
    }
}
