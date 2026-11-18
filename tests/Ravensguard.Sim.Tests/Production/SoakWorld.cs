using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>
/// Seed 1234's region with its own private population (the soak mutates employers, owners, households) and the whole Phase 2 economy installed in an abstract
/// world: the real <see cref="WorldTicker"/> with every registered system drives it, so harvest, wages, hiring, markets and needs are the production code and no
/// longer stand-ins. The tests read the economy through the same names as before (<see cref="Pop"/>, <see cref="Econ"/>, <see cref="Markets"/>, <see cref="Sys"/>, <see cref="Layer"/>).
/// </summary>
public sealed class SoakWorld : IDisposable
{
    public const ulong Seed = 1234;
    public HeadlessWorld Headless { get; private init; } = null!;
    public WorldTicker Ticker { get; private set; } = null!;
    public SimWorld Sim => Headless.World;
    public EconomyWorld Economy => Headless.Economy;
    public Population Pop => Headless.Population;
    public EconomyState Econ => Sim.Economy;
    public MarketSystem Markets => Economy.Markets;
    public ProductionSystem Sys => Economy.Production;
    public ResourceLayer Layer => Economy.Resources;
    public int Day { get; private set; }

    public static SoakWorld Create(bool withTicker = true)
    {
        var b = EconFixture.Shared;
        var region = RegionGenerator.Generate(b.Continent, b.History, b.CellX, b.CellY, b.Rs);
        LandmarkPlacer.Place(region, b.History, EconFixture.Content);
        var pop = Populator.Populate(region, b.History, EconFixture.Content);
        var hw = HeadlessWorld.FromPopulation(Seed, pop, EconFixture.Content, b.Continent, b.History, b.Cs, b.Hs, b.Rs, b.CellX, b.CellY);
        var w = new SoakWorld { Headless = hw };
        if (withTicker) w.Ticker = hw.NewTicker();
        return w;
    }

    /// <summary>Runs <paramref name="days"/> game days from the current day on the real tick; calls <paramref name="daily"/> after each day's hour 23.</summary>
    public void Run(int days, Action<int, GameTime>? daily = null)
    {
        for (int d = 0; d < days; d++, Day++)
        {
            Ticker.AdvanceHours(24);
            daily?.Invoke(Day, GameTime.FromGameHours(Sim.State.HourDone));
        }
    }

    public void Dispose() => Ticker?.Dispose();
}
