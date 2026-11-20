using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Crafting;

/// <summary>
/// One store a crafter may draw materials from (crafting-full 4.1 and the user's decision on fetching): the pack and the containers within reach (room or 6 m) are used where they stand; a store
/// elsewhere in the same building that the crafter may use is <see cref="Fetch"/>ed as part of the job, at the cost of walking there and back (<see cref="DistMm"/> one way) as often as the carry
/// limit needs.
/// </summary>
public sealed class CraftSource
{
    public required Store Store { get; init; }
    public EntityId Id { get; init; }
    public string Name { get; init; } = "";
    public bool Fetch { get; init; }
    public long DistMm { get; init; }
}

/// <summary>The stores a recipe draws on, in the order they are used: the pack, then the containers in reach by distance, then the stores to fetch from.</summary>
public sealed class CraftSources : List<CraftSource>
{
    public static CraftSources Of(Store? s)
    {
        var l = new CraftSources();
        if (s != null) l.Add(new CraftSource { Store = s, Name = "pack" });
        return l;
    }

    public long Qty(ushort good) { long q = 0; foreach (var s in this) q += s.Store.Qty(good); return q; }
    public Store? First => Count > 0 ? this[0].Store : null;

    /// <summary>
    /// Seconds to fetch <paramref name="massG"/> grams from a store <paramref name="distMm"/> away: trips of at most <paramref name="carryG"/> each, there and back at <paramref name="walkMmPerSec"/>,
    /// plus <paramref name="handleSec"/> a trip for loading and unloading. Integer, rounded up.
    /// </summary>
    public static (long Seconds, int Trips) FetchTime(long massG, long distMm, long carryG, int walkMmPerSec, int handleSec)
    {
        if (massG <= 0) return (0, 0);
        int trips = (int)((massG + Math.Max(1, carryG) - 1) / Math.Max(1, carryG));
        long walk = (2 * distMm + walkMmPerSec - 1) / Math.Max(1, walkMmPerSec);
        return (trips * (walk + handleSec), trips);
    }
}
