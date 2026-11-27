using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Abstract;

/// <summary>
/// The phases of one abstract hour step, in the binding order of overview section 4 / abstract spec 3.2. A system lists the phases it wants in
/// <see cref="IAbstractSystem.Phases"/>; the ticker calls <see cref="IAbstractSystem.Step"/> once per hour per listed phase that is due.
/// </summary>
[Flags]
public enum TickPhase : byte
{
    None = 0,
    /// <summary>H0: sample the environment (weather per settlement cell).</summary>
    H0Environment = 1,
    /// <summary>H1: caravans and migrants advance, arrivals, tolls, loss rolls.</summary>
    H1Caravans = 2,
    /// <summary>H2: work credit: businesses, extraction jobs, farm tasks.</summary>
    H2Work = 4,
    /// <summary>H3: market session (systems skip the hours outside 06-18 themselves).</summary>
    H3Market = 8,
    /// <summary>H4: the daily slice: households and businesses whose index mod 24 equals the hour of day.</summary>
    H4DailySlice = 16,
    /// <summary>H5: hour 0 only: settlement daily globals (famine and outbreak checks, weekly marriage catch-up, news).</summary>
    H5DailyGlobals = 32,
    /// <summary>H6: every hour of a season's first day (day of year 1, 16, 31, 46); a system slices its settlements by <see cref="TickContext.IsSeasonSlice"/>.</summary>
    H6Seasonal = 64,
    /// <summary>H7: after everything else; builds outbox entries that need the finished hour.</summary>
    H7Outbox = 128,
    All = 255,
}

/// <summary>
/// Explicit ordering of systems inside a phase (ascending; ties by name, ordinal). The numbers are the contract between the Phase 2 specs: a new system picks
/// the constant of the slot the spec names, so the order never depends on registration order or on reflection order.
/// </summary>
public static class SystemOrder
{
    // H0
    public const int Environment = 0;
    // H1
    public const int Caravans = 1_000;
    // Phase 7 slots (overview 8.4; registered by their own parts, constants here so the order never depends on registration)
    // H1 after caravans: hosts, ships, sieges, then the host supply slot and the armies' daily step (both act only at hour 18)
    public const int Armies = 1_100, Ships = 1_120, Sieges = 1_140, HostSupply = 1_150, ArmiesDaily = 1_160;
    // H2 work credit: businesses, then extraction, then farm tasks (overview section 4)
    public const int Production = 2_000, Extraction = 2_100, FarmTasks = 2_200;
    // H3
    public const int Markets = 3_000;
    // H4 slice order (households spec 3.2: plan work, eat, drink, heat, wear, nutrition; then vital rolls, births before deaths)
    public const int Provisioning = 4_000, FarmSales = 4_050, HouseholdNeeds = 4_100, VitalRolls = 4_200, Wounds = 4_250, ProductionDaily = 4_300, MarketDaily = 4_400;
    // H5 hour 0: farming (weather, field events, the day's labour, harvest) first; then population; store spoilage, parish, labour market before the markets'
    // daily globals (structural demand sees today's staff and stock) and production's vacancy counters last (Phase 2 integration, decision I1)
    public const int DailyGlobalsFarming = 4_900, DailyGlobalsPopulation = 5_000, DailyGlobalsSpoilage = 5_020, DailyGlobalsParish = 5_030, DailyGlobalsLabour = 5_040,
        DailyGlobalsMarkets = 5_100, DailyGlobalsProduction = 5_150, DailyGlobalsTaxes = 5_200,
        // Phase 7 (overview 8.4), H5 after taxes: the mint's queue, the soldiers' day, the sieges' day
        MintDaily = 5_210, SoldiersDaily = 5_230, SiegesDaily = 5_240;
    // H6 seasonal steps S1..S9 (overview section 4 table); several systems may share a step and then order by name
    public const int S1Farming = 6_100, S2Grazing = 6_200, S3Extraction = 6_300, S4Taxes = 6_400, S5Businesses = 6_450, S5MintPrices = 6_500, S6Population = 6_600, S6Growth = 6_650, S7Migration = 6_700,
        S8Caravans = 6_800, S9Summary = 6_900,
        /// <summary>P6-D2 (diplomacy-and-ai-lords 4.6): the realms' diplomacy and the lords' planning, after the continent's summary; a fief an hour of a season's first day.</summary>
        S10Politics = 6_950,
        /// <summary>P7 (overview 8.4): rulers' campaigns, musters and summons, the war score's season, winter quarters; after <see cref="S10Politics"/>.</summary>
        S11War = 6_970;
    // Phase 8 slots (overview 3, P8-0A; constants only, registered by their own parts). Hour-phase slots sit in free gaps of the phases above.
    public const int WarTollHour = 1_170, AmbitionHourly = 4_450, AmbitionPlanner = 5_250, WarTollSeason = 6_980;
    // Phase 8 performance 3.2 (P7R-13): the season-day systems run in fixed slices over hours 0 to 5, each in its own slot (growth, continent trade, weather pre-warm, caravans)
    public const int SliceGrowth = 6_651, SliceContinentTrade = 6_701, SliceWeatherPrewarm = 6_751, SliceCaravans = 6_801;
    // H7
    public const int Outbox = 7_000;
}

/// <summary>One simulation system that the abstract tick schedules (farming, markets, production, population ...).</summary>
public interface IAbstractSystem
{
    /// <summary>Unique, stable name (also the timing key and the tie-break of the order).</summary>
    string Name { get; }
    /// <summary>Phases this system takes part in.</summary>
    TickPhase Phases { get; }
    /// <summary>Position inside every phase it runs in; use the <see cref="SystemOrder"/> constants.</summary>
    int Order { get; }
    /// <summary>One call per hour per due phase; <see cref="TickContext.Phase"/> says which. Runs on the worker thread: touch only <see cref="TickContext.World"/> and the outbox.</summary>
    void Step(TickContext ctx);
}

/// <summary>
/// Marks a class with a public parameterless constructor as a system to register by reflection (<see cref="AbstractSystemRegistry.Discover"/>).
/// The class still states its own <see cref="IAbstractSystem.Order"/>, so discovery order never matters.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AbstractSystemAttribute : Attribute { }

/// <summary>Convenience base for a system: pass the identity to the constructor and override <see cref="Step"/>.</summary>
public abstract class AbstractSystem(string name, TickPhase phases, int order) : IAbstractSystem
{
    public string Name { get; } = name;
    public TickPhase Phases { get; } = phases;
    public int Order { get; } = order;
    public abstract void Step(TickContext ctx);
}

/// <summary>The environment of an hour step (abstract spec 2.5 <c>HourEnv</c>): what the vital and needs kernels need from the weather.</summary>
public interface IHourEnvironment
{
    /// <summary>Mean temperature of a game day in whole degrees Celsius.</summary>
    int TMeanC(long day);
}

/// <summary>A fixed temperature (tests).</summary>
public sealed class ConstantEnvironment(int tMeanC) : IHourEnvironment
{
    public int TMeanC(long day) => tMeanC;
}

/// <summary>
/// A deterministic temperate year with no weather source: a piecewise-linear curve through the season anchors plus a per-day wobble of at most 3 degrees,
/// integer maths only. Used by headless runs and tests; the game uses <see cref="RegionWeatherEnvironment"/>.
/// </summary>
public sealed class SeasonalEnvironment(ulong seed) : IHourEnvironment
{
    private static readonly (int Doy, int T)[] Anchors = [(1, 6), (23, 18), (38, 10), (53, -4), (61, 6)];

    public int TMeanC(long day)
    {
        int doy = (int)(((day % TimeConst.DaysPerYear) + TimeConst.DaysPerYear) % TimeConst.DaysPerYear) + 1;
        int t = Anchors[^1].T;
        for (int i = 0; i + 1 < Anchors.Length; i++)
        {
            var (d0, t0) = Anchors[i]; var (d1, t1) = Anchors[i + 1];
            if (doy < d0 || doy >= d1) continue;
            t = t0 + (t1 - t0) * (doy - d0) / (d1 - d0);
            break;
        }
        return t + DetRng.Range(seed, day, 0, RngStreams.EnvJitter, 0, -3, 4);
    }
}

/// <summary>The real daily mean temperature of one region cell from <see cref="RegionWeather"/>.</summary>
public sealed class RegionWeatherEnvironment(RegionWeather weather, int cell) : IHourEnvironment
{
    public int TMeanC(long day) => weather.DailyMeanTemp(cell, day).Floor();
}
