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
    /// <summary>The woodland households gather fuel in and woodwards cut for the market (review finding 4).</summary>
    public Woodland Woodland { get; }
    /// <summary>The seasonal spending of households above their reserve (integration decision I37).</summary>
    public HouseholdSpending Spending { get; }
    /// <summary>Taxes, rents, tolls, treasuries and unrest (taxes spec, Phase 2 scope).</summary>
    public Ravensguard.Sim.Taxes.TaxSystem Taxes { get; }
    /// <summary>Background growth: cottages, assarted fields and new businesses built by the abstract world (plan 4A.2).</summary>
    public Ravensguard.Sim.Growth.GrowthSystem Growth { get; }
    public EconomyCounters Counters { get; } = new();
    /// <summary>P5-F (businesses spec 4.3): the hiring hour in every settlement's square.</summary>
    public DayLabour DayLabour { get; }
    /// <summary>Farming writes its chronicle here; the farming system moves the new entries to the tick outbox after each daily step.</summary>
    public Chronicle FarmChronicle { get; }
    internal int FarmChronicleDrained;

    public EconomyWorld(SimWorld world, FarmingSystem farming, MarketSystem markets, ProductionSystem production, ResourceLayer resources, ProductionContent content, Chronicle farmChronicle)
    {
        World = world; Farming = farming; Markets = markets; Production = production; Resources = resources; Content = content; FarmChronicle = farmChronicle;
        Labour = new LabourMarket(production, world.Population, world.Economy, world.Seed, p => world.Children.Of(p));
        Parish = new ParishRelief(world, markets);
        Woodland = new Woodland(world, resources, content.Extractions.FirstOrDefault(k => k.Id == "firewood_cutting"));
        world.Economy.FuelGatherer = Woodland;
        // the continent sells what no recipe of the region makes, and the materials the region's recipes consume (iron, leather, cloth, malt...) when a book is short of
        // them (integration decision I36): only the finished goods of the region's own workshops are never imported
        var inputs = content.Recipes.Recipes.SelectMany(r => r.Inputs.Select(i => i.Good)).ToHashSet();
        Trade = new ContinentTrade(this, markets.AllBooks, world.Economy, content.Recipes.Recipes.SelectMany(r => r.Outputs.Select(o => o.Good)).Where(g => !inputs.Contains(g)));
        Regional = new RegionalTrade(markets, world.Economy, world.Population.Region);
        Spending = new HouseholdSpending(world, this, content.Spending);
        Taxes = new Ravensguard.Sim.Taxes.TaxSystem(world, this, farming, production, content.Tax);
        farming.BuyFromMarket = (hh, good, qty) => markets.BookForHousehold(hh).Buy(markets.HouseholdParty(hh), good, qty, 3000, (int)(world.State.HourDone / 24)).Qty;
        farming.CountIdleHands = true;
        farming.IsEmployedElsewhere = Labour.IsAwayFromFarm;
        farming.ManorLordOf = Labour.ManorLordOf;
        Growth = new Ravensguard.Sim.Growth.GrowthSystem(this);
        DayLabour = new DayLabour(production, world.Population, world.Economy, content.Ownership, Labour);
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
        // there is no continent float any more (continent tick v1): the neighbours' coin lives in the continent's summaries, and caravans carry it across the gates
        ew.Install();
        return ew;
    }

    /// <summary>Puts this region into a continent: its caravans trade with that continent's summaries and its migrants cross through it.</summary>
    public void BindContinent(Ravensguard.Sim.Economy.Markets.ContinentLink link) { Trade.Bind(link); World.Set(link); }

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
