using System.Diagnostics;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Content;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.RegionsB;

public class ProbeTests(ITestOutputHelper o)
{
    [Fact]
    public void Probe()
    {
        var w = SeamWorld.Get(1234);
        var rc = w.BlockOrigin.Offset(1, 1);
        long m0 = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();
        var region = RegionGenerator.Generate(w.Continent, w.History, rc, w.Settings);
        o.WriteLine($"region gen {sw.ElapsedMilliseconds} ms"); sw.Restart();
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        o.WriteLine($"landmarks+people {sw.ElapsedMilliseconds} ms, people {pop.People.Count} hh {pop.Households.Count}"); sw.Restart();
        long m1 = GC.GetTotalMemory(true);
        o.WriteLine($"region+pop MB {(m1 - m0) / 1e6:0.0}");
        var econ = EconomyState.Create(pop, w.Content, 1234, true);
        o.WriteLine($"econ create {sw.ElapsedMilliseconds} ms"); sw.Restart();
        var weather = RegionWeather.ForContinent(w.Continent);
        var names = new NamePools(w.Content.All<NamePoolDef>(), pop.Region.Seed ^ 0x50455031UL);
        var start = GameTime.FromCalendar(pop.NowYear, Season.Spring, 1);
        var sim = new SimWorld(1234, pop, econ, names: names, state: new AbstractState { HourDone = start.TotalHours - 1 }, env: new SeasonalEnvironment(1234));
        VitalSeeding.SeedPregnancies(sim, start);
        var ew = EconomyWorld.Create(sim, w.Content, new RegionFarmWeather(weather, weather.Fronts.Long));
        o.WriteLine($"econ world {sw.ElapsedMilliseconds} ms"); sw.Restart();
        long m2 = GC.GetTotalMemory(true);
        o.WriteLine($"econ MB {(m2 - m1) / 1e6:0.0}");
        var t = new WorldTicker(sim);
        sw.Restart();
        t.AdvanceDays(2);
        o.WriteLine($"2 days hourly {sw.ElapsedMilliseconds} ms");
        GC.KeepAlive(ew);
    }
}
