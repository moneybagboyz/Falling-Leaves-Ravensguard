using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Abstract;

/// <summary>Caches the day's mean temperature (the systems ask for it many times an hour) in front of another environment.</summary>
public sealed class CachedEnvironment(IHourEnvironment inner) : IHourEnvironment
{
    private long _day = long.MinValue; private int _t;
    public int TMeanC(long day) { if (day != _day) { _t = inner.TMeanC(day); _day = day; } return _t; }
}

/// <summary>
/// A whole Phase 2 world built from nothing but a seed and the base content folder: continent, history, the embark region, its people, the economy and
/// the abstract world with every economy system installed, driven by the region's own weather. What the soak tool and the integration tests start from.
/// </summary>
public sealed class HeadlessWorld
{
    public required SimWorld World { get; init; }
    public required EconomyWorld Economy { get; init; }
    public required ContentDatabase Content { get; init; }
    public required Continent Continent { get; init; }
    public required WorldHistory History { get; init; }
    public required ContinentSettings ContinentSettings { get; init; }
    public required HistorySettings HistorySettings { get; init; }
    public required RegionSettings RegionSettings { get; init; }
    public required int CellX { get; init; }
    public required int CellY { get; init; }
    public required Population Population { get; init; }
    public required RegionWeather Weather { get; init; }

    public WorldTicker NewTicker(AbstractSystemRegistry? registry = null, TickerOptions? options = null) => new(World, registry ?? AbstractSystemRegistry.CreateDefault(), options);

    /// <summary>Builds the world of <paramref name="seed"/>; <paramref name="contentDir"/> is the folder with mod.json (null: found above the working directory).</summary>
    public static HeadlessWorld Create(ulong seed, string? contentDir = null, ContentDatabase? content = null, int carryOverPct = 45)
    {
        string dir = contentDir ?? ContinentSettings.FindBaseContent() ?? throw new InvalidOperationException("content/base not found");
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = seed;
        var rs = RegionSettings.LoadDefaults(dir); rs.Seed = seed;
        var db = content;
        if (db == null)
        {
            var load = new ModLoader(ContentTypeRegistry.Default).Load([dir]);
            if (!load.Success) throw new InvalidOperationException(load.Report.ToString());
            db = load.Database;
        }
        var c = ContinentGenerator.Generate(cs);
        var h = HistoryGenerator.Generate(c, hs);
        var pick = EmbarkSite.AutoPick(c, h);
        var region = RegionGenerator.Generate(c, h, pick.CellX, pick.CellY, rs);
        LandmarkPlacer.Place(region, h, db);
        var pop = Populator.Populate(region, h, db);
        return FromPopulation(seed, pop, db, c, h, cs, hs, rs, pick.CellX, pick.CellY, carryOverPct);
    }

    /// <summary>The abstract world and economy over an already populated region (tests that share a generated region clone the population first).</summary>
    public static HeadlessWorld FromPopulation(ulong seed, Population pop, ContentDatabase db, Continent c, WorldHistory h, ContinentSettings cs, HistorySettings hs, RegionSettings rs,
        int cellX, int cellY, int carryOverPct = 45, bool seedGoods = true)
    {
        var weather = RegionWeather.ForContinent(c);
        int cell = Math.Min(Continent.Size - 1, cellY + 8) * Continent.Size + Math.Min(Continent.Size - 1, cellX + 8);
        var econ = EconomyState.Create(pop, db, seed, seedGoods);
        var names = new NamePools(db.All<NamePoolDef>(), pop.Region.Seed ^ 0x50455031UL);
        var env = new CachedEnvironment(new RegionWeatherEnvironment(weather, cell));
        var start = GameTime.FromCalendar(pop.NowYear, Season.Spring, 1);
        var w = new SimWorld(seed, pop, econ, names: names, state: new AbstractState { HourDone = start.TotalHours - 1 }, env: env);
        w.Rates = VitalRates.EconomyDefault;
        VitalSeeding.SeedPregnancies(w, start);
        var ew = EconomyWorld.Create(w, db, new RegionFarmWeather(weather, weather.Fronts.Long), carryOverPct);
        // the continent round the region: summaries, roads from the history's trade routes, the weather's say on every harvest; the region's ticker runs its season tick (single-region world)
        int homeId = RegionCoord.OfCell(cellX, cellY).Id;
        var cont = Ravensguard.Sim.Continental.ContinentState.Create(seed, h, homeId, (int)w.Vital.Alive, econ.Ledger.Audit().InPurses.Mites, standalone: true);
        Ravensguard.Sim.Continental.ContinentWiring.Attach(cont, c, h, weather);
        ew.BindContinent(new Ravensguard.Sim.Economy.Markets.ContinentLink(cont, homeId));
        Ravensguard.Sim.Continental.ContinentTick.BindPrices(econ.Goods);
        Ravensguard.Sim.Continental.ContinentTick.Run(cont, [new Ravensguard.Sim.Continental.ResidentRegion(homeId, ew)], GameTime.FromGameHours(w.State.HourDone + 1));
        return new HeadlessWorld { World = w, Economy = ew, Content = db, Continent = c, History = h, ContinentSettings = cs, HistorySettings = hs, RegionSettings = rs, CellX = cellX, CellY = cellY, Population = pop, Weather = weather };
    }
}
