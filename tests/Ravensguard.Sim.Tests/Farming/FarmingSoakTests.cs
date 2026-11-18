using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Farming;

/// <summary>
/// The 10-year headless farming soak of the seed-1234 region (60 days a year, every day stepped, spoilage on, a region-wide "ideal market" eating 0.92 measures per
/// person-day). Two runs: scripted mild weather (the spec's normal-year balance, G-F15) and the region's own weather (frost, drought, winterkill: famine only with causes).
/// </summary>
public class FarmingSoakTests(ITestOutputHelper output)
{
    /// <summary>The spec's G-F15 balance: (harvest - seed) x 0.96 spoilage over demand, minus one.</summary>
    private static double SpecSurplus(SoakYear y, int people) => (y.HarvestG - y.SeedG) * 0.96 / (people * 60.0 * FarmingSoak.MeasuresPerPersonDay * 1100) - 1;

    private void Report(FarmingSoak soak, string title)
    {
        output.WriteLine($"== {title}: people {soak.People}, fields {EconFixture.Pop.Region.Fields.Length}, parcels {EconFixture.Pop.Region.Parcels.Count}");
        foreach (var y in soak.Years)
        {
            double demand = y.PopulationDays * FarmingSoak.MeasuresPerPersonDay * 1100;
            output.WriteLine($"year {y.Year}: harvest {y.HarvestG / 1100} m, seed {y.SeedG / 1100}, spoiled {y.SpoiledG / 1100}, eaten {y.EatenG / 1100}, short {y.ShortG / 1100} m ({y.HungryDays} hungry days), stock {y.StockG / 1100}; "
                + $"sown spring {y.SpringSown} winter {y.WinterSown}; fertility {y.MeanFertility:F1}; surplus (G-F15) {SpecSurplus(y, soak.People):P1}, physical {(y.HarvestG - y.SeedG - y.SpoiledG) / demand - 1:P1}");
        }
        var s = soak.Sys.State;
        foreach (var sm in s.Summaries)
            output.WriteLine($"  loss {sm.Year}: potential {sm.Gross0G / 1100} m; " + string.Join(" ", Enum.GetValues<LossCause>().Where(c => sm.LostG[(int)c] > 0).Select(c => $"{c} {sm.LostG[(int)c] / 1100}")));
        foreach (var e in soak.Chron.All) output.WriteLine($"  chronicle {e.TypeId}: {e.Causes[0].Kind} - {e.Causes[0].Note} ({string.Join(", ", e.Data.Select(k => k.Key + "=" + k.Value))})");
    }

    [Fact]
    public void Soak_MildWeather_NormalYearsHoldTheSpecBalance()
    {
        var sw = Stopwatch.StartNew();
        var soak = new FarmingSoak(FarmingSoak.SpecSettings(), weather: new FakeFarmWeather());
        var start = soak.Sys.State.Fields.Slot.ToArray(); var slotAcres = Enumerable.Range(0, 3).Select(s => soak.Sys.SlotAcres(0, s)).ToArray();
        soak.Run(10);
        Report(soak, "mild weather");
        output.WriteLine($"10 years in {sw.ElapsedMilliseconds} ms");
        var surplus = soak.Years.Select(y => SpecSurplus(y, soak.People)).ToArray();
        output.WriteLine($"mean surplus (G-F15) {surplus.Average():P1}; range {surplus.Min():P1} .. {surplus.Max():P1}");
        Assert.InRange(surplus.Average(), 0.02, 0.10);                      // spec soak 1: normal years +2%..+10% (the spec's own figure is +3.9%)
        Assert.All(surplus, s => Assert.InRange(s, -0.09, 0.15));        // the winter field sown the autumn before swings the year by a few percent
        Assert.True(soak.Econ.MassAuditHolds());
        Assert.All(soak.Years, y => Assert.True(y.HungryDays <= 8, $"year {y.Year}: {y.HungryDays} hungry days"));   // a normal year does not starve (the spoiled share of the stock costs a few days)
        // G-F17 on the real region: three years later (after 9 rotations) every field is where it started, the slot acres of a settlement are unchanged
        Assert.Equal(start, soak.Sys.State.Fields.Slot);
        Assert.Equal(slotAcres, Enumerable.Range(0, 3).Select(s => soak.Sys.SlotAcres(0, s)).ToArray());
        Assert.Equal(0, soak.Sys.State.Fields.Slot.Count(s => s > 2));
    }

    [Fact]
    public void Soak_RegionWeather_FamineYearsHaveWeatherCauses()
    {
        var sw = Stopwatch.StartNew();
        var soak = new FarmingSoak();
        soak.Run(10);
        Report(soak, "the region's own weather");
        output.WriteLine($"10 years in {sw.ElapsedMilliseconds} ms");
        Assert.True(soak.Econ.MassAuditHolds());
        Assert.True(soak.Econ.Ledger.Audit().IsConserved);
        Assert.All(soak.Years, y => { Assert.True(y.HarvestG > 0, $"year {y.Year}"); Assert.True(y.StockG >= 0); });     // no extinction of the harvest
        long demand = soak.People * 60L * 920 * 1100 / 1000;
        var causes = soak.Chron.All.Where(e => e.TypeId == "farm.yield_loss").Select(e => (Year: int.Parse(e.Data["year"]), Cause: e.Data["cause"])).ToList();
        foreach (var y in soak.Years.Where(y => y.ShortG * 100 > demand * 5))            // a famine year: more than 5% of the food missing
            Assert.Contains(causes, c => c.Year >= y.Year - 2 && c.Year <= y.Year);       // ... follows a bad harvest of this or the two years before
        foreach (var e in soak.Chron.All) Assert.NotEqual(CauseKind.Unknown, e.Causes[0].Kind);
        Assert.True(soak.Chron.CauseCoverage() >= 0.9);
        // the weather-cause entries name concrete causes
        Assert.All(soak.Chron.ByType("farm.yield_loss"), e => Assert.Contains(e.Data["cause"], new[] { "Drought", "Frost", "Hail", "Blight", "ColdSummer", "Winterkill", "WetHarvest", "Waterlogged", "Overdue" }));
        // the same inputs give the same world
        string Hash(FarmingSoak s) => s.Sys.State.ComputeHash() + s.Econ.ComputeHash();
        var again = new FarmingSoak(); again.Run(10);
        Assert.Equal(Hash(soak), Hash(again));
    }

    [Fact]
    public void Soak_FarmingIsDeterministic_AndTheSeedMatters()
    {
        string Run(Func<FarmingSoak> make) { var s = make(); s.Run(2); return s.Sys.State.ComputeHash(); }
        var a = Run(() => new FarmingSoak(weather: new FakeFarmWeather())); var b = Run(() => new FarmingSoak(weather: new FakeFarmWeather()));
        Assert.Equal(a, b);
        var dry = new FakeFarmWeather { LongFn = (_, _) => new Ravensguard.Sim.Weather.LongCondition(Ravensguard.Sim.Weather.LongKind.Drought, 1, 60) };
        Assert.NotEqual(a, Run(() => new FarmingSoak(weather: dry)));
    }
}
