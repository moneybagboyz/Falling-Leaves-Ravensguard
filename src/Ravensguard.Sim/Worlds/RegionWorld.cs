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
    /// <summary>L2: generated, resident, records only. Stepped in slices of a few hours, each region on its own phase (<see cref="Ravensguard.Sim.Abstract.RegionScheduler.DailySliceHours"/>).</summary>
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
    /// <summary>The abstract continent every region of this world trades and migrates through (continent tick v1); regions bind to it when they are made. Null in a bare test world.</summary>
    public Ravensguard.Sim.Continental.ContinentState? AbstractContinent { get; set; }
    private readonly object _continentGate = new();

    /// <summary>
    /// The continent of this world, made from the first region that asks (normally the home region: its head count and coin set the continent's per-head values) when none was
    /// given: over the history's roads when the history is at hand, else a small synthetic one. A continent made here is driven by whoever steps the regions (the scheduler), never by a region's own ticker.
    /// </summary>
    public Ravensguard.Sim.Continental.ContinentState EnsureContinent(Ravensguard.Sim.Abstract.EconomyWorld ew, int regionId)
    {
        lock (_continentGate)
        {
            if (AbstractContinent != null) return AbstractContinent;
            var sim = ew.World; int pop = (int)Math.Max(1, sim.Vital.Alive); long coin = sim.Economy.Ledger.Audit().InPurses.Mites;
            var st = History != null ? Ravensguard.Sim.Continental.ContinentState.Create(Seed, History, Home.Id, pop, coin) : Ravensguard.Sim.Continental.ContinentState.Standalone_(Seed, Home.Id, pop, coin, driven: false);
            if (History != null) Ravensguard.Sim.Continental.ContinentWiring.Attach(st, Continent, History, Weather);
            Ravensguard.Sim.Continental.ContinentTick.BindPrices(sim.Economy.Goods);
            Ravensguard.Sim.Continental.ContinentTick.Run(st, [new Ravensguard.Sim.Continental.ResidentRegion(regionId, ew)], GameTime.FromGameHours(sim.State.HourDone + 1));
            return AbstractContinent = st;
        }
    }

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
        sim.Rates = VitalRates.EconomyDefault;
        VitalSeeding.SeedPregnancies(sim, start);
        var ew = EconomyWorld.Create(sim, sv.Content, sv.FarmWeatherFor());
        ew.BindContinent(new Ravensguard.Sim.Economy.Markets.ContinentLink(sv.EnsureContinent(ew, at.Id), at.Id));
        return new RegionWorld(at, sv, sim, ew, chron, ids);
    }

    /// <summary>The home region of a loaded or new game: the world state's own records, with farming, production and markets from their sections.</summary>
    public static RegionWorld ForHome(RegionServices sv, WorldState w)
    {
        if (w.Economy == null || w.Farming == null || w.Production == null || w.Markets == null)
            throw new InvalidOperationException("The home region needs the economy, farming, production and markets of the world state.");
        var at = sv.Home;
        var sim = SimWorld.From(w, sv.NamesFor(w.Population.Region), sv.EnvFor(at));
        sim.Rates = VitalRates.EconomyDefault;
        var chron = new Chronicle();
        var farming = new FarmingSystem(w.Population.Region, w.Population, w.Economy, sv.FarmSettings, sv.FarmWeatherFor(), chron, w.Ids, sv.SeedFor(at), w.Farming);
        var ew = new EconomyWorld(sim, farming, w.Markets, w.Production, w.Production.Resources!, w.Production.Content, chron);
        ew.Install();
        ew.BindContinent(new Ravensguard.Sim.Economy.Markets.ContinentLink(sv.EnsureContinent(ew, at.Id), at.Id));
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
            Baseline = Services.History == null ? null : () => Populator.Populate(pop.Region, Services.History, Services.Content),
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
            BaselineGuard.Verify($"Region {at}", d.BaselineHash, d.GeneratorVersion, basePop);
            people = PeopleDiffer.Expand(people, WorldSave.EncodePeople(basePop));
        }
        var pop = WorldSave.DecodePeople(people, d.Lots, terrain);
        var seed = sv.SeedFor(at);
        var econ = d.Economy.Restore(pop, sv.Content);
        var abs = d.Abstract.Restore(pop);
        var chron = WorldSave.DecodeChronicle(d.Chronicle);
        var ids = IdAllocator.FromState(d.IdState);
        var sim = new SimWorld(seed, pop, econ, chron, ids, abs, sv.NamesFor(terrain), sv.EnvFor(at));
        sim.Rates = VitalRates.EconomyDefault;
        var farmChron = new Chronicle();
        var farming = new FarmingSystem(terrain, pop, econ, sv.FarmSettings, sv.FarmWeatherFor(), farmChron, ids, seed, d.Farming.Restore(terrain));
        var content = ProductionContent.Load(sv.Content, econ.Goods);
        var markets = new Economy.Markets.MarketSystem(econ, pop, seed);
        var prod = d.Production.Restore(econ, pop, content, seed, markets);
        d.Markets.Restore(markets, prod);
        var ew = new EconomyWorld(sim, farming, markets, prod, prod.Resources!, content, farmChron);
        ew.Install();
        ew.BindContinent(new Ravensguard.Sim.Economy.Markets.ContinentLink(sv.EnsureContinent(ew, at.Id), at.Id));
        return new RegionWorld(at, sv, sim, ew, chron, ids) { PendingChunks = d.Chunks };
    }

    // ------------------------------------------------------------------ ticking

    /// <summary>The last game hour whose step runs coarse (<c>H5 | H6 | H7</c>); every later hour runs every phase. <see cref="long.MinValue"/>: none (the default, exact hourly truth).</summary>
    public long CoarseUntilHour { get; private set; } = long.MinValue;

    /// <summary>
    /// Sets how a long gap is crossed: given the game hour <paramref name="anchorHour"/> the region was asked for at (a paging request, a traveller coming near), the hours up to
    /// the last day boundary at or before <c>anchorHour - </c><see cref="FullReplayHours"/> run coarse and all later hours run every phase. The choice is made per absolute hour,
    /// so it does not depend on where <see cref="AdvanceTo"/> is stopped on the way, nor on the hour the region happened to join the scheduler (note N28).
    /// </summary>
    public void SetCatchUpAnchor(long anchorHour)
    {
        long edge = anchorHour - FullReplayHours;
        long dayFloor = edge - (((edge % 24) + 24) % 24);
        CoarseUntilHour = dayFloor;
    }

    /// <summary>
    /// Steps the region to <paramref name="hour"/>, one game hour at a time. Hours up to <see cref="CoarseUntilHour"/> (set by <see cref="SetCatchUpAnchor"/>; none by default) run
    /// coarse, so a region seen again after years still passes its seasons, famines and price drift without a year of full steps; every other hour runs every phase.
    /// Which phases an hour runs depends on that hour alone, so the records at any hour are a pure function of (records, anchor, hour): the same section gives the same
    /// result whatever intermediate hours it was stepped to. Coarse hours are an approximation of hourly truth, by design (spec 3.2, N14); with no anchor the result is exact.
    /// </summary>
    public void AdvanceTo(long hour)
    {
        if (hour <= HourDone) return;
        var t = Ticker;
        if (HourDone < CoarseUntilHour)
        {
            t.PhaseMask = TickPhase.H5DailyGlobals | TickPhase.H6Seasonal | TickPhase.H7Outbox;
            try { t.AdvanceTo(GameTime.FromGameHours(Math.Min(hour, CoarseUntilHour))); }
            finally { t.PhaseMask = TickPhase.All; }
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
