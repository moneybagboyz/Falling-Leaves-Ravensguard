using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Carts;

/// <summary>What the vehicle pass needs of the running world (P5-G hook): the ride book (made by the game or the desk when first needed), the crafting context of the commissions, the waterways.</summary>
public sealed class VehicleService
{
    public required Func<RideBook?> Book { get; init; }
    public Func<CraftContext?> Craft { get; init; } = () => null;
    public Func<int, WaterPath?> Paths { get; init; } = _ => null;
}

/// <summary>
/// Vehicles in the abstract tick (vehicle-designer 4.6): every hour the owners' sites get their crews' hours (H2), the commissioned vehicles collect their made parts and the makers fit them, and boats
/// under way move along their waterways (H1). Runs in the outbox phase after crafting (the parts a maker finished this hour are collected the same hour), under the ride book's lock. A no-op while there
/// are no sites, commissions or voyages.
/// </summary>
[AbstractSystem]
public sealed class VehicleSystem : AbstractSystem
{
    public VehicleSystem() : base("vehicles", TickPhase.H7Outbox, SystemOrder.Outbox + 120) { }

    public override void Step(TickContext ctx)
    {
        var svc = ctx.World.Get<VehicleService>(); if (svc?.Book() is not { } b) return;
        if (b.Works.IsEmpty) return;
        lock (b.Gate) Hour(b, svc.Craft(), svc.Paths, ctx.World.Economy.Items, ctx.Hour);
    }

    /// <summary>One hour of every site, commission and voyage, in id order.</summary>
    public static void Hour(RideBook b, CraftContext? craft, Func<int, WaterPath?> paths, Goods.ItemTable items, long hour)
    {
        foreach (var s in b.Works.Sites.Values.ToList()) VehicleSites.SiteHour(b, items, s, hour);
        if (craft != null) foreach (var c in b.Works.Commissions.Values.ToList()) VehicleSites.CommissionHour(b, craft, c, hour);
        Voyages.Hour(b, paths, hour);
    }
}

/// <summary>Installs the vehicle pass on the home region (P5-G hook in <see cref="WorldRuntime"/>).</summary>
public static class VehicleWiring
{
    public static VehicleService Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        var paths = new Dictionary<int, WaterPath>();
        var reg = state.Population.Region;
        WaterPath? Path(int id)
        {
            if (paths.TryGetValue(id, out var p)) return p;
            var w = reg.Waterways.FirstOrDefault(x => x.Id == id); if (w == null) return null;
            return paths[id] = new WaterPath(w);
        }
        var svc = new VehicleService
        {
            Book = () => state.Mounts,
            Craft = () => home.Sim.Get<CraftService>()?.Context,
            Paths = Path,
        };
        home.Sim.Set(svc);
        return svc;
    }
}
