using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;

namespace Ravensguard.Sim.Abstract;

/// <summary>
/// The Phase 2 economy of one region, bundled for the abstract tick: farming, the market books, businesses with their labour market, the resource layer and the
/// parish, all over the one <see cref="EconomyState"/> and <see cref="Population"/> of a <see cref="SimWorld"/>. <see cref="Install"/> puts it into the world's
/// service bag; the registered economy systems (<see cref="AbstractSystemRegistry.CreateDefault"/>) find it there and do nothing when it is absent, so a world
/// without an economy (the population-only tests) still ticks.
/// </summary>
public sealed class EconomyWorld
{
    public SimWorld World { get; }
    public FarmingSystem Farming { get; }
    public MarketSystem Markets { get; }
    public ProductionSystem Production { get; }
    public ResourceLayer Resources { get; }
    public ProductionContent Content { get; }
    public LabourMarket Labour { get; }
    public ParishRelief Parish { get; }
    public ContinentTrade Trade { get; }
    public RegionalTrade Regional { get; }
    public EconomyCounters Counters { get; } = new();
    /// <summary>Farming writes its chronicle here; the farming system moves the new entries to the tick outbox after each daily step.</summary>
    public Chronicle FarmChronicle { get; }
    internal int FarmChronicleDrained;

    public EconomyWorld(SimWorld world, FarmingSystem farming, MarketSystem markets, ProductionSystem production, ResourceLayer resources, ProductionContent content, Chronicle farmChronicle)
    {
        World = world; Farming = farming; Markets = markets; Production = production; Resources = resources; Content = content; FarmChronicle = farmChronicle;
        Labour = new LabourMarket(production, world.Population, world.Economy, world.Seed, p => world.Children.Of(p));
        Parish = new ParishRelief(world, markets);
        var cp = world.Economy.Ledger.Open(PurseKind.Treasury, Ravensguard.Sim.People.Vital.Demography.ContinentOwner);
        Trade = new ContinentTrade(markets.AllBooks, world.Economy, cp, content.Recipes.Recipes.SelectMany(r => r.Outputs.Select(o => o.Good)));
        Regional = new RegionalTrade(markets, world.Economy, world.Population.Region);
        farming.BuyFromMarket = (hh, good, qty) => markets.BookForHousehold(hh).Buy(markets.HouseholdParty(hh), good, qty, 3000, (int)(world.State.HourDone / 24)).Qty;
        farming.CountIdleHands = true;
        farming.IsEmployedElsewhere = Labour.IsAwayFromFarm;
        Labour.Rebuild();
    }

    /// <summary>
    /// Builds the economy over the world's population and economy: market books, businesses (from the people), the resource layer, farming state and its
    /// opening stock, and the carry-over of last year's harvest (<paramref name="carryOverPct"/> of one year's holder share, reason Seed).
    /// </summary>
    public static EconomyWorld Create(SimWorld world, ContentDatabase db, IFarmWeather weather, int carryOverPct = 45, FarmingSettings? settings = null, int merchantStockPct = 60, int continentFloatPct = 8)
    {
        var pop = world.Population; var econ = world.Economy; ulong seed = world.Seed;
        var content = ProductionContent.Load(db, econ.Goods);
        var layer = ResourceLayer.Build(pop.Region, content.Settings, seed);
        var markets = new MarketSystem(econ, pop, seed);
        var prod = BusinessBuilder.Create(econ, pop, content, seed, markets, layer);
        if (merchantStockPct > 0) markets.SeedMerchantStock(pop.NowDay, 8, merchantStockPct);
        var chron = new Chronicle();
        var farming = new FarmingSystem(pop.Region, pop, econ, settings ?? FarmingSettings.FromContent(db), weather, chron, world.Ids, seed);
        farming.ProvisionInitialStock();
        if (carryOverPct > 0) farming.ProvisionCarryOver(carryOverPct);
        var ew = new EconomyWorld(world, farming, markets, prod, layer, content, chron);
        // the continent's trade float: a seed source like the households' (documented), so it can pay for exports before it has sold any imports
        if (continentFloatPct > 0) { var seeded = econ.Ledger.Audit().Seeded.Mites; econ.Ledger.Mint(ew.Trade.Purse, new Coin(seeded * continentFloatPct / 100), SourceReason.Seed); }
        ew.Install();
        return ew;
    }

    /// <summary>Registers this economy in the world's service bag (<c>world.Get&lt;EconomyWorld&gt;()</c>).</summary>
    public void Install() => World.Set(this);
}

/// <summary>Running totals the economy systems keep for the soak table (not saved; they restart with a loaded world).</summary>
public sealed class EconomyCounters
{
    public long HungryHouseholdDays, HouseholdDays;
    public long CharityG, ParishSoldG, FarmSoldG;
    public long Hired, Apprenticed, Succeeded, ClosedNoOwner, StaffGone;
}
