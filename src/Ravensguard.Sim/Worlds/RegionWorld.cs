using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Worlds;

/// <summary>Levels of detail of a region (seamless-regions spec 2.2): who is ticked how, and what stays in memory.</summary>
public enum RegionLevel : byte
{
    /// <summary>L0: the region with the hero. Hourly abstract tick, chunks.</summary>
    Live = 0,
    /// <summary>L1: generated, in the ring around the hero. Hourly abstract tick.</summary>
    Detailed = 1,
    /// <summary>L2: generated, resident, records only. Whole days at the day boundary.</summary>
    Daily = 2,
    /// <summary>L3: generated before, written to the pager; not ticked, not in memory.</summary>
    Paged = 3,
    /// <summary>L4: never generated; only the continent's numbers.</summary>
    Abstract = 4,
}

/// <summary>The things every region's records are built from: seed, content and the continent-wide weather. Immutable after construction; safe to share between workers.</summary>
public sealed class RegionServices
{
    public ulong Seed { get; }
    public ContentDatabase Content { get; }
    public Continent Continent { get; }
    public RegionWeather Weather { get; }
    public FarmingSettings FarmSettings { get; }
    public RegionCoord Home { get; }
    /// <summary>The world's history: the baseline a saved region is diffed against is the population regenerated from it (note N24). Null where it is not at hand: sections are then saved whole.</summary>
    public Ravensguard.Sim.WorldGen.History.WorldHistory? History { get; set; }

    public RegionServices(ulong seed, ContentDatabase content, Continent continent, RegionCoord home)
    {
        Seed = seed; Content = content; Continent = continent; Home = home;
        Weather = RegionWeather.ForContinent(continent);
        FarmSettings = FarmingSettings.FromContent(content);
    }

    /// <summary>The sim seed of a region: the world seed for the home region (so a one-region world is unchanged), a mix of seed and region id for every other.</summary>
    public ulong SeedFor(RegionCoord at) => at == Home ? Seed : DetRng.Mix(Seed ^ 0x52454731UL ^ ((ulong)(at.Id + 1) << 32));

    public IHourEnvironment EnvFor(RegionCoord at)
    {
        int cell = Math.Min(Continent.Size - 1, at.CellY + 8) * Continent.Size + Math.Min(Continent.Size - 1, at.CellX + 8);
        return new CachedEnvironment(new RegionWeatherEnvironment(Weather, cell));
    }

    public IFarmWeather FarmWeatherFor() => new RegionFarmWeather(Weather, Weather.Fronts.Long);
    public NamePools NamesFor(RegionData r) => new(Content.All<NamePoolDef>(), r.Seed ^ 0x50455031UL);
}

/// <summary>
/// The records of one generated region (spec 2.2): its people and households, economy, fields and herds, businesses and markets, tick state, chronicle and id
/// counters, wrapped in the <see cref="SimWorld"/> the abstract tick runs on. Everything in it carries this region's id in its entity ids (E2). The terrain
/// (<see cref="Population.Region"/>) is derived data, not a record.
/// </summary>
public sealed class RegionWorld : IDisposable
{
    /// <summary>Hours replayed with every phase when a region catches up; older hours of a long gap run coarse (daily globals and seasons only).</summary>
    public const int FullReplayHours = 72;

    public RegionCoord Coord { get; }
    public RegionServices Services { get; }
    public SimWorld Sim { get; }
    public EconomyWorld Economy { get; }
    public Chronicle Chronicle { get; }
    public IdAllocator Ids { get; }
    public RegionLevel Level { get; set; } = RegionLevel.Detailed;
    /// <summary>Chunk entries read from the section that still have to go into the world's chunk store (set by <see cref="Restore"/>, taken once by the record set).</summary>
    public byte[]? PendingChunks { get; set; }
    private WorldTicker? _ticker;

    private RegionWorld(RegionCoord at, RegionServices sv, SimWorld sim, EconomyWorld ew, Chronicle chron, IdAllocator ids)
    {
        Coord = at; Services = sv; Sim = sim; Economy = ew; Chronicle = chron; Ids = ids;
    }

    public Population Population => Sim.Population;
    public long HourDone => Sim.State.HourDone;
    public WorldTicker Ticker => _ticker ??= new WorldTicker(Sim, AbstractSystemRegistry.CreateDefault(), new TickerOptions { RecordTimings = false });

    // ------------------------------------------------------------------ creation

    /// <summary>The records of a region at the end of history: its generated population with a fresh economy, at the opening hour of play.</summary>
    public static RegionWorld CreateFresh(RegionServices sv, RegionCoord at, Population pop)
    {
        var seed = sv.SeedFor(at);
        var econ = EconomyState.Create(pop, sv.Content, seed, true);
        var ids = IdAllocator.ForRegion(at.Id); var chron = new Chronicle();
        var start = GameTime.FromCalendar(pop.NowYear, Season.Spring, 1);
        var sim = new SimWorld(seed, pop, econ, chron, ids, new AbstractState { HourDone = start.TotalHours - 1 }, sv.NamesFor(pop.Region), sv.EnvFor(at));
        sim.Rates = VitalRates.Default with { ChevagePct = 50 };
        VitalSeeding.SeedPregnancies(sim, start);
        var ew = EconomyWorld.Create(sim, sv.Content, sv.FarmWeatherFor());
        return new RegionWorld(at, sv, sim, ew, chron, ids);
    }

    /// <summary>The home region of a loaded or new game: the world state's own records, with farming, production and markets from their sections.</summary>
    public static RegionWorld ForHome(RegionServices sv, WorldState w)
    {
        if (w.Economy == null || w.Farming == null || w.Production == null || w.Markets == null)
            throw new InvalidOperationException("The home region needs the economy, farming, production and markets of the world state.");
        var at = sv.Home;
        var sim = SimWorld.From(w, sv.NamesFor(w.Population.Region), sv.EnvFor(at));
        sim.Rates = VitalRates.Default with { ChevagePct = 50 };
        var chron = new Chronicle();
        var farming = new FarmingSystem(w.Population.Region, w.Population, w.Economy, sv.FarmSettings, sv.FarmWeatherFor(), chron, w.Ids, sv.SeedFor(at), w.Farming);
        var ew = new EconomyWorld(sim, farming, w.Markets, w.Production, w.Production.Resources!, w.Production.Content, chron);
        ew.Install();
        return new RegionWorld(at, sv, sim, ew, w.Chronicle, w.Ids);
    }

    // ------------------------------------------------------------------ save and restore

    /// <summary>Copies the records into a section (calling thread). <paramref name="chunks"/> adds the region's share of the world's chunk store.</summary>
    public RegionSectionData Capture(ChunkStore? chunks = null)
    {
        var pop = Population;
        return new RegionSectionData
        {
            RegionId = Coord.Id,
            People = WorldSave.EncodePeople(pop), Lots = WorldSave.EncodeLots(pop),
            Economy = EconomySave.Capture(Sim.Economy), Abstract = AbstractSave.Capture(Sim.State, pop), Farming = FarmingSave.Capture(Economy.Farming.State),
            Production = ProductionSave.Capture(Economy.Production), Markets = MarketsSave.Capture(Economy.Markets),
            Chronicle = WorldSave.EncodeChronicle(Chronicle), IdState = Ids.GetState(),
            Chunks = chunks == null ? [] : chunks.ToSection(k => ChunkRegions.Of(Services.Home, k) == Coord),
            Baseline = Services.History == null ? null : () => WorldSave.EncodePeople(Populator.Populate(pop.Region, Services.History, Services.Content)),
        };
    }

    /// <summary>The section as bytes, people diffed against the baseline when the services know the history (the same bytes whether the region is paged out or saved while resident).</summary>
    public byte[] CaptureBytes(ChunkStore? chunks = null) { var d = Capture(chunks); d.Compact(); return SaveCodec.SerializeCompressed(d); }

    /// <summary>Rebuilds a region's records from its section over the regenerated terrain. Does not tick: catching up is <see cref="AdvanceTo"/>.</summary>
    public static RegionWorld Restore(RegionServices sv, RegionCoord at, RegionData terrain, RegionSectionData d, Population? baseline = null)
    {
        if (d.RegionId != at.Id) throw new SaveException($"A region section for region {d.RegionId} was found where region {at.Id} was expected.");
        var people = d.People;
        if (people.Diff != null)
        {
            var basePop = baseline ?? (sv.History != null ? Populator.Populate(terrain, sv.History, sv.Content) : throw new SaveException("The people of this region are saved as a difference from the generated ones, and the history to generate them again is missing."));
            people = PeopleDiffer.Expand(people, WorldSave.EncodePeople(basePop));
        }
        var pop = WorldSave.DecodePeople(people, d.Lots, terrain);
        var seed = sv.SeedFor(at);
        var econ = d.Economy.Restore(pop, sv.Content);
        var abs = d.Abstract.Restore(pop);
        var chron = WorldSave.DecodeChronicle(d.Chronicle);
        var ids = IdAllocator.FromState(d.IdState);
        var sim = new SimWorld(seed, pop, econ, chron, ids, abs, sv.NamesFor(terrain), sv.EnvFor(at));
        sim.Rates = VitalRates.Default with { ChevagePct = 50 };
        var farmChron = new Chronicle();
        var farming = new FarmingSystem(terrain, pop, econ, sv.FarmSettings, sv.FarmWeatherFor(), farmChron, ids, seed, d.Farming.Restore(terrain));
        var content = ProductionContent.Load(sv.Content, econ.Goods);
        var markets = new Economy.Markets.MarketSystem(econ, pop, seed);
        var prod = d.Production.Restore(econ, pop, content, seed, markets);
        d.Markets.Restore(markets, prod);
        var ew = new EconomyWorld(sim, farming, markets, prod, prod.Resources!, content, farmChron);
        ew.Install();
        return new RegionWorld(at, sv, sim, ew, chron, ids) { PendingChunks = d.Chunks };
    }

    // ------------------------------------------------------------------ ticking

    /// <summary>
    /// Steps the region to <paramref name="hour"/>. A gap of up to <see cref="FullReplayHours"/> runs hour by hour with every phase; a longer one first runs the
    /// older hours coarse (<c>H5 | H6 | H7</c>), so a region seen again after years still passes its seasons, famines and price drift without a year of full steps.
    /// Pure in (records, hour): the same records and target always give the same records.
    /// </summary>
    public void AdvanceTo(long hour)
    {
        long gap = hour - HourDone;
        if (gap <= 0) return;
        var t = Ticker;
        if (gap > FullReplayHours)
        {
            long coarseTo = hour - FullReplayHours;
            t.PhaseMask = TickPhase.H5DailyGlobals | TickPhase.H6Seasonal | TickPhase.H7Outbox;
            t.AdvanceTo(GameTime.FromGameHours(coarseTo));
            t.PhaseMask = TickPhase.All;
        }
        t.AdvanceTo(GameTime.FromGameHours(hour));
    }

    /// <summary>Digest of everything the region's records hold (people, vital columns, economy, chronicle size, counters).</summary>
    public string RecordsHash() => Sim.ComputeHash();

    public void Dispose() { _ticker?.Dispose(); _ticker = null; }
}

/// <summary>Which region a chunk of the streaming frame lies in (the frame's origin is the north-west corner of the home region, spec N7).</summary>
public static class ChunkRegions
{
    public static RegionCoord Of(RegionCoord home, ChunkKey k)
        => home.Offset(FloorDiv(k.X, WorldGen.Chunks.ChunkStreamer.RegionChunks), FloorDiv(k.Y, WorldGen.Chunks.ChunkStreamer.RegionChunks));
    private static int FloorDiv(int a, int b) { int q = a / b; return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q; }
}
