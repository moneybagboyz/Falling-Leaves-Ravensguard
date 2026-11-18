using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Farming;

public class FarmingSoakTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Soak_TenYears_Report(bool mildWeather)
    {
        var sw = Stopwatch.StartNew();
        var soak = new FarmingSoak(weather: mildWeather ? new FakeFarmWeather() : null);
        output.WriteLine($"setup {sw.ElapsedMilliseconds} ms; people {soak.People}, fields {EconFixture.Pop.Region.Fields.Length}, parcels {EconFixture.Pop.Region.Parcels.Count}");
        sw.Restart();
        soak.Run(10);
        output.WriteLine($"10 years in {sw.ElapsedMilliseconds} ms");
        foreach (var y in soak.Years)
        {
            double demand = y.PopulationDays * FarmingSoak.MeasuresPerPersonDay * 1100;
            output.WriteLine($"year {y.Year}: harvest {y.HarvestG / 1100} m, seed {y.SeedG / 1100}, spoiled {y.SpoiledG / 1100}, eaten {y.EatenG / 1100}, short {y.ShortG / 1100} m, hungry days {y.HungryDays}, stock {y.StockG / 1100}; net/demand {(y.HarvestG - y.SeedG - y.SpoiledG) / demand - 1:P1}");
        }
        output.WriteLine($"sow refused: seed {soak.Sys.Diagnostics.SowRefusedSeed}, fertility {soak.Sys.Diagnostics.SowRefusedFertility}");
        foreach (var sm in soak.Sys.State.Summaries)
            output.WriteLine($"summary {sm.Year}: gross0 {sm.Gross0G / 1100} m harvest {sm.HarvestG / 1100} threshed {sm.ThreshedG / 1100} sown {sm.SownAcres} unharvested {sm.UnharvestedAcres} lost m: " + string.Join(" ", Enum.GetValues<LossCause>().Select(c => $"{c}={sm.LostG[(int)c] / 1100}")));
        foreach (var e in soak.Chron.All) output.WriteLine($"{e.TypeId} {e.Causes[0].Kind} {e.Causes[0].Note} {string.Join(",", e.Data.Select(k => k.Key + "=" + k.Value))}");
    }
}
