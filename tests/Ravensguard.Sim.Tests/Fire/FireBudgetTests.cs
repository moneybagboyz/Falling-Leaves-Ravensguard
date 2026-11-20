using System.Diagnostics;
using Ravensguard.Sim.Burning;
using Ravensguard.Sim.Tests.Building;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Fire;

/// <summary>The fire's budgets (overview 9.1, fire 9): 400 burning cells at most 0.6 ms a live tick on average, nothing at all when nothing burns, the abstract hour at most 0.3 ms.</summary>
[Collection(TimingCollection.Name)]
public class FireBudgetTests(BuildingFixture fx, ITestOutputHelper output) : IClassFixture<BuildingFixture>
{
    /// <summary>A town of 30 thatched houses with 400 of their cells burning and fuel for the whole run, wind 5 m/s, people in the flames and fighters at the buckets.</summary>
    private FireRig Blaze()
    {
        var r = FireRig.Make(fx, 5);
        r.Wx = new FireWeather(5000, 270, 0, false, false);
        for (int i = 0; i < 30; i++) r.House((i % 10) * 14, (i / 10) * 14);
        r.Fw.WaterSources = () => [(-20, 5)];
        for (int i = 0; i < 12; i++) r.Fighters.Add(new FireFighter(100UL + (ulong)i, i, -4, 0, i % 4 == 0));
        int n = 0;
        for (int h = 0; h < 30 && n < 400; h++)
            for (int y = 1; y < 5 && n < 400; y++)
                for (int x = 1; x < 7 && n < 400; x++)
                    if (r.Fw.Ignite((h % 10) * 14 + x, (h / 10) * 14 + y, 1, r.Sec, FireCause.Test)) { r.Fw.CellAt((h % 10) * 14 + x, (h / 10) * 14 + y, 1)!.FuelLeftS = 1_000_000; n++; }
        Assert.Equal(400, r.Fw.Book.Cells.Count);
        return r;
    }

    [Fact]
    public void FourHundredBurningCells_CostUnderAHalfMillisecondATick_AndNothingBurningCostsNothing()
    {
        var warm = Blaze(); warm.Run(20);
        double ms = TestBudget.BestPass(0.6, 3, () =>
        {
            var r = Blaze();
            var sw = Stopwatch.StartNew();
            r.Run(100);                                                             // 100 sim-seconds = 2,000 live ticks at 1x
            return sw.Elapsed.TotalMilliseconds / 2000.0;
        });
        output.WriteLine($"400 burning cells: {ms:F3} ms a tick on average (budget 0.6)");
        Assert.True(ms < TestBudget.Ms(0.6), $"{ms:F3} ms a tick");
        // nothing burns: the step is not even entered
        var idle = FireRig.Make(fx, 1);
        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < 100_000; i++) idle.Fw.AdvanceTo(i * 20L);
        double perCall = sw2.Elapsed.TotalMilliseconds / 100_000;
        output.WriteLine($"idle: {perCall * 1000:F3} us a call");
        Assert.True(perCall < 0.001);
    }

    [Fact]
    public void TheAbstractHour_WithAnEntireTownOfBuildings_CostsUnderThreeTenthsOfAMillisecond()
    {
        var r = FireRig.Make(fx, 3);
        for (int i = 0; i < 40; i++) r.House((i % 10) * 14, (i / 10) * 14);
        var first = r.Rooms.StructureAt(2, 2, 0);
        Assert.True(r.Fw.IgniteBuilding(first, 0, FireCause.Hearth));
        r.Fw.HourStep(0);                                                           // warm
        double ms = TestBudget.BestPass(0.3, 5, () =>
        {
            var q = FireRig.Make(fx, 3);
            for (int i = 0; i < 40; i++) q.House((i % 10) * 14, (i / 10) * 14);
            q.Fw.IgniteBuilding(q.Rooms.StructureAt(2, 2, 0), 0, FireCause.Hearth);
            var sw = Stopwatch.StartNew();
            q.Fw.HourStep(0);
            return sw.Elapsed.TotalMilliseconds;
        });
        output.WriteLine($"abstract fire hour, 40 buildings: {ms:F3} ms (budget 0.3)");
        Assert.True(ms < TestBudget.Ms(0.3), $"{ms:F3} ms");
    }
}
