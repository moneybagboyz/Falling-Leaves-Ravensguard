using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Siege;

/// <summary>
/// P7-SG wiring of the engine kit for a campaign host (SiegeWorld.KitOf): the engine book, the yard that builds at the camp and the shooting world, over the world's ride book and craft runner, made at the first engine of the first siege (a world that never
/// besieged keeps its bytes). The camp's store is a real chest per siege; what the engines are built of and shoot is the host's baggage and what the quartermaster buys from the books in the host's reach (<see cref="WarSupply.HostSupply.BuyForSiege"/>) or what
/// AE1's convoys carried in: goods moved and bought, never made. Coin goes through the books and the ledger, so coin and mass are conserved.
/// </summary>
public sealed class SiegeKits(WorldState state, ContentDatabase content, SiegeWorld sw)
{
    public const ulong CampSpace = 9;
    /// <summary>Stone for the shot a camp keeps ahead: forty shots of a trebuchet (a hundredweight each is a hundred kilograms).</summary>
    public const long ShotStockKg = 4_000, CounterweightKg = 5_000;

    private EngineYard? _yard; private EngineBook? _yardBook;
    private readonly Dictionary<int, SiegeKit> _kits = [];

    public static EntityId TransitOwner(int siege) => EntityId.Reserved(IdKind.Other, 0, (EntityId.ReservedSpace)(CampSpace + 1), (ulong)siege);
    public static EntityId CampOwner(int siege) => EntityId.Reserved(IdKind.Other, 0, (EntityId.ReservedSpace)CampSpace, (ulong)siege);

    /// <summary>The camp's workshop: the siege train's carpenters bring their tools and benches (light, water at hand); the recipes' tools and stations are theirs, the goods are not.</summary>
    private static CraftSite CampSite(RecipeDb db)
    {
        var s = new CraftSite { Light = 255, Water = true };
        foreach (var t in db.Tools.Keys) s.AddTool(db, t);
        foreach (var st in db.Stations.Keys) s.AddStation(db, st);
        return s;
    }

    public SiegeKit? Of(SiegeRow s)
    {
        var econ = state.Economy; var runner = sw.W.Get<CraftService>()?.Runner; if (econ == null || runner == null) return null;
        var rides = state.Mounts ??= new RideBook(content, econ, state.Seed);
        var book = state.Engines ??= new EngineBook(content, rides, state.Seed);
        if (_yard == null || !ReferenceEquals(_yardBook, book))
        {
            _yard = new EngineYard(rides, runner, CampSite(runner.Db), econ) { Book = book, Chronicle = sw.Writer };          // no wage: the builders are the host's soldiers, paid by its pay chest already
            _yardBook = book; _kits.Clear();
        }
        if (_kits.TryGetValue(s.Id, out var kit) && ReferenceEquals(kit.Book, book)) return kit;
        var camp = econ.OpenStore(StoreKind.Chest, CampOwner(s.Id), 0, 0, false);
        var world = new EngineWorld { Engineering = _ => 60, AmmoOf = _ => camp, Goods = econ.Goods, Chronicle = sw.Writer };
        return _kits[s.Id] = new SiegeKit { Book = book, Yard = _yard, World = world, Camp = camp };
    }
}
