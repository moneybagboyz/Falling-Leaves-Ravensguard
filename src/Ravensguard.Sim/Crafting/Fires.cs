using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Crafting;

/// <summary>A lit fire in the world (crafting-full 4.1): where it is, who lit it, and the second its fuel was last burned to. Its fuel (firewood) sits in an economy store keyed by its id.</summary>
public sealed class Fire
{
    public EntityId Id;
    public EntityId Owner;
    public long X, Y;
    public long LitSec, BurnedToSec;
    public bool Out;
}

/// <summary>
/// The lit fires (crafting-full 4.1, P5-E): <c>kindle_fire</c>'s effect places one with the recipe's firewood as its fuel. A fire is a <c>campfire</c> station (heat 2, cooking 1) within 2 m and
/// light 200 within 4 m; it burns 0.5 kg of firewood a game hour (burned as a counted sink, so mass is exact) and goes out when the fuel is gone or in rain over 5 mm an hour. Advanced by
/// game time (the hour pass and whenever it is asked), never per tick: the burn between two times is <c>elapsed x 500 g / 3600</c>, floor, carried by the time burned to.
/// </summary>
public sealed class FireBook
{
    public const long BurnGPerHour = 500, StationMm = 2000, LightMm = 4000;
    public const int LightLevel = 200, RainOutTenthsMm = 50;
    private readonly SortedDictionary<EntityId, Fire> _fires = [];
    public IReadOnlyDictionary<EntityId, Fire> All => _fires;
    public int Count => _fires.Count;

    public Fire Light(EntityId id, EntityId owner, long x, long y, long nowSec)
    {
        var f = new Fire { Id = id, Owner = owner, X = x, Y = y, LitSec = nowSec, BurnedToSec = nowSec };
        _fires[id] = f;
        return f;
    }

    public void Restore(Fire f) => _fires[f.Id] = f;

    /// <summary>Puts more firewood on a fire: grams moved from <paramref name="from"/> into its fuel store. False when it is out or the move fails.</summary>
    public bool Feed(EconomyState econ, EntityId fire, Store from, ushort firewood, long grams)
    {
        if (!_fires.TryGetValue(fire, out var f) || f.Out) return false;
        var fuel = econ.OtherStores.TryGetValue(fire, out var s) ? s : econ.OpenStore(StoreKind.House, fire, 0, 0, false);
        return Store.Move(from, fuel, firewood, grams);
    }

    /// <summary>
    /// Burns every fire to <paramref name="nowSec"/>: fuel goes at 0.5 kg an hour; a fire with no fuel left, or under rain of more than 5 mm an hour (<paramref name="rainTenthsMmPerHour"/> over 50),
    /// goes out (rain leaves the unburnt wood in its store). Returns the fires that went out, in id order.
    /// </summary>
    public List<Fire> Advance(EconomyState econ, ushort firewood, long nowSec, int rainTenthsMmPerHour = 0)
    {
        var outNow = new List<Fire>();
        foreach (var f in _fires.Values)
        {
            if (f.Out || nowSec <= f.BurnedToSec) continue;
            var fuel = econ.OtherStores.GetValueOrDefault(f.Id);
            long have = fuel?.Qty(firewood) ?? 0;
            long elapsed = nowSec - f.BurnedToSec;
            long burn = elapsed * BurnGPerHour / 3600;
            if (burn >= have)
            {
                // the fuel runs out part-way: it went out when the last of it burned
                if (have > 0) fuel!.Destroy(firewood, have, DestroyReason.Burned);
                f.BurnedToSec += have * 3600 / BurnGPerHour; f.Out = true; outNow.Add(f);
                // the empty fuel store stays until the day's sweep (removing a store in the middle of the hour would shift the other systems' store order)
                continue;
            }
            if (burn > 0) { fuel!.Destroy(firewood, burn, DestroyReason.Burned); f.BurnedToSec += burn * 3600 / BurnGPerHour; }
            if (rainTenthsMmPerHour > RainOutTenthsMm) { f.Out = true; outNow.Add(f); }
        }
        return outNow;
    }

    /// <summary>Removes fires that are out (their leftover wood stays in its store, given to <paramref name="give"/> when it says where).</summary>
    public void Sweep(EconomyState econ, Func<EntityId, Store?> give, Action<EntityId>? drop = null)
    {
        foreach (var f in _fires.Values.Where(x => x.Out).ToList())
        {
            if (econ.OtherStores.TryGetValue(f.Id, out var fuel))
            {
                if (give(f.Owner) is { } dest) foreach (var (g, q) in fuel.Rows().ToList()) Store.Move(fuel, dest, g, q);
                if (fuel.RowCount == 0) { if (drop != null) drop(f.Id); else econ.OtherStores.Remove(f.Id); }
            }
            _fires.Remove(f.Id);
        }
    }

    private static bool Within(Fire f, long x, long y, long mm) { long dx = f.X - x, dy = f.Y - y; return dx * dx + dy * dy <= mm * mm; }

    /// <summary>A lit fire within 2 m (the <c>campfire</c> station).</summary>
    public bool StationNear(long x, long y) => _fires.Values.Any(f => !f.Out && Within(f, x, y, StationMm));

    /// <summary>The light a lit fire gives here: 200 within 4 m, else 0.</summary>
    public int LightAt(long x, long y) => _fires.Values.Any(f => !f.Out && Within(f, x, y, LightMm)) ? LightLevel : 0;
}
