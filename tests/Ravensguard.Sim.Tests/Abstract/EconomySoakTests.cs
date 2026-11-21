using System.Diagnostics;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Production;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Abstract;

/// <summary>
/// The Phase 2 economy running together on the real abstract tick (harvest, hiring, markets, production, needs, population all registered systems, no stand-ins):
/// a one-year soak that checks every invariant and round-trips every save section, a determinism check and a hundred-year soak (<c>Category=Soak</c>) that prints the
/// yearly table of <c>WorldTool soak</c>. The tuning targets of the plan (price band, hunger, population) are reported against, see the notes of the specs.
/// </summary>
public class EconomySoakTests(ITestOutputHelper output)
{
    [Fact]
    public void OneYear_RealSystems_KeepEveryInvariant_AndEverySaveSectionRoundTrips()
    {
        var sw = Stopwatch.StartNew();
        using var w = SoakWorld.Create(); var sim = w.Sim; var led = w.Econ.Ledger;
        var soak = new EconomySoak(w.Headless);
        long seeded = led.Audit().Seeded.Mites; long start = sim.Vital.Alive;
        soak.Run(1, r => output.WriteLine(EconomySoak.Format(r)));
        var row = soak.Rows[0];
        output.WriteLine(EconomySoak.Header()); output.WriteLine($"1 year in {sw.ElapsedMilliseconds} ms");

        // invariants
        Assert.True(led.Audit().IsConserved); Assert.Equal(seeded, led.Audit().Seeded.Mites); Assert.Equal(0, led.Audit().Minted.Mites); Assert.Equal(0, led.Audit().Melted.Mites + led.Audit().Burned.Mites);
        Assert.True(row.CoinConserved, "the world's coin (the region's ledger and the continent) is exact");   // coin crosses the border with caravans, the desk and migrants (decision I33)
        Assert.True(w.Econ.MassAuditHolds()); Assert.Equal(0, w.Sys.MassViolations);
        { long sum = 0; for (int p = 0; p < led.PurseCount; p++) sum += led.Balance(new PurseId(p)).Mites; Assert.Equal(led.Audit().InPurses.Mites, sum); }   // review finding 16: was "every balance >= 0", which the ledger guarantees
        foreach (var b in w.Markets.AllBooks()) for (ushort g = 0; g < w.Econ.Goods.Count; g++) Assert.Equal(b.BookStore.Qty(g), b.Goods[g].Lots.Sum(l => l.Qty));
        Assert.Equal(sim.Vital.Alive, Enumerable.Range(0, sim.People.Count).Count(i => sim.People.Status[i] == PersonStatus.Alive));

        // the economy runs: bread and ale are made, workshops finish batches, merchants and businesses are open, people are fed
        Assert.True(row.BreadT > 0 && row.AleKl > 0, $"bread {row.BreadT} t, ale {row.AleKl} kl");
        Assert.True(row.WorkshopCompletionPct >= 40, $"workshops completing a batch {row.WorkshopCompletionPct}%");
        Assert.True(row.MerchantsSolventPct >= 60, $"cash-solvent merchants {row.MerchantsSolventPct}%");
        Assert.True(row.BandPct >= 60, $"honest in-band book-days {row.BandPct}%");
        Assert.True(row.HungryPct < 2.0, $"missed-meal household-days {row.HungryPct}%");
        Assert.InRange(row.Population, start * 90 / 100, start * 125 / 100);                  // relative to the generated population (it is 25,6xx, not a fixed number): decision G13, -10 to +25 percent
        Assert.True(w.Economy.Labour.Hired > 0 && w.Economy.Farming.State.HarvestG > 0);
        Assert.True(sim.Chronicle.CauseCoverage() >= 0.9);

        // every section round-trips byte for byte
        var eco = SaveCodec.SerializeCompressed(EconomySave.Capture(w.Econ)); var mk = SaveCodec.SerializeCompressed(MarketsSave.Capture(w.Markets));
        var pr = SaveCodec.SerializeCompressed(ProductionSave.Capture(w.Sys)); var fm = SaveCodec.SerializeCompressed(FarmingSave.Capture(w.Economy.Farming.State));
        var ab = SaveCodec.SerializeCompressed(AbstractSave.Capture(sim.State, sim.Population));
        var econ2 = SaveCodec.DeserializeCompressed<EconomySave>(eco).Restore(w.Pop, EconFixture.Content);
        var markets2 = new MarketSystem(econ2, w.Pop, SoakWorld.Seed);
        var sys2 = SaveCodec.DeserializeCompressed<ProductionSave>(pr).Restore(econ2, w.Pop, ProdKit.Content, SoakWorld.Seed, markets2);
        SaveCodec.DeserializeCompressed<MarketsSave>(mk).Restore(markets2, sys2);
        var farm2 = SaveCodec.DeserializeCompressed<FarmingSave>(fm).Restore(w.Pop.Region);
        var abs2 = SaveCodec.DeserializeCompressed<AbstractSave>(ab).Restore(w.Pop);
        Assert.Equal(w.Econ.ComputeHash(), econ2.ComputeHash());
        Assert.Equal(eco, SaveCodec.SerializeCompressed(EconomySave.Capture(econ2)));
        Assert.Equal(mk, SaveCodec.SerializeCompressed(MarketsSave.Capture(markets2)));
        Assert.Equal(pr, SaveCodec.SerializeCompressed(ProductionSave.Capture(sys2)));
        Assert.Equal(fm, SaveCodec.SerializeCompressed(FarmingSave.Capture(farm2)));
        Assert.Equal(ab, SaveCodec.SerializeCompressed(AbstractSave.Capture(abs2, w.Pop)));
        Assert.Equal(w.Economy.Farming.State.ComputeHash(), farm2.ComputeHash());
        output.WriteLine($"sections: economy {eco.Length / 1024} KB, markets {mk.Length / 1024} KB, production {pr.Length / 1024} KB, farming {fm.Length / 1024} KB, abstract {ab.Length / 1024} KB");
    }

    [Fact]
    public void TheSameSeedGivesTheSameWorld_HoweverTheHoursAreBatched()
    {
        string Run(int hoursPerCall)
        {
            using var w = SoakWorld.Create(); int left = 24 * 12;
            while (left > 0) { int n = Math.Min(hoursPerCall, left); w.Ticker.AdvanceHours(n); left -= n; }
            return w.Sim.ComputeHash() + w.Econ.ComputeHash() + w.Economy.Farming.State.ComputeHash() + Convert.ToHexString(SaveCodec.SerializeCompressed(ProductionSave.Capture(w.Sys)))[..64];
        }
        Assert.Equal(Run(24), Run(7));
    }


    /// <summary>
    /// One hundred game years of seed 1234 on the abstract tick (about 15 minutes); prints the table of <c>WorldTool soak --years 100</c> and asserts the Phase 2 targets
    /// (review finding 16, the thresholds are decision I46): <see cref="AssertTargets"/>.
    /// </summary>
    [Fact, Trait("Category", "Soak")]
    public void HundredYears_RealSystems_KeepEveryInvariant_AndMeetTheTargets()
    {
        using var w = SoakWorld.Create();
        var soak = RunAndAssert(w.Headless, 100, every: 5);
        Assert.Contains(w.Sys.Businesses, b => b.State == BusinessState.Open);
        // BS-18 (P5-F, businesses spec 4.3): with the hiring hour, the landless idle adults at year 100 stay under 800 (Phase 3 measured 2,101; 1,738 before the day labour)
        Assert.True(soak.Rows[^1].Idle < 800, $"landless idle adults at year 100: {soak.Rows[^1].Idle}");
        output.WriteLine($"day labour: {w.Sys.DayLabour.Hired:N0} days hired, {w.Sys.DayLabour.PaidMites / 100:N0} crowns paid, {w.Sys.DayLabour.Workers.Count} labourers on the books");
    }

    /// <summary>Twenty-five game years of seed 1 (a second world, about 4 minutes): the same targets.</summary>
    [Fact, Trait("Category", "Soak")]
    public void TwentyFiveYears_SeedOne_MeetsTheTargets()
    {
        var hw = HeadlessWorld.Create(1, Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!);
        RunAndAssert(hw, 25, every: 1);
    }

    private EconomySoak RunAndAssert(HeadlessWorld hw, int years, int every)
    {
        var soak = new EconomySoak(hw); var led = hw.World.Economy.Ledger;
        output.WriteLine(EconomySoak.Header());
        long start = hw.World.Vital.Alive; var sw = Stopwatch.StartNew();
        soak.Run(years, r =>
        {
            if (r.Year == 1 || r.Year % every == 0) output.WriteLine(EconomySoak.Format(r));
            Assert.True(r.CoinConserved && r.MassOk && r.MassViolations == 0, $"audit failed in year {r.Year}");
            var au = led.Audit();
            Assert.Equal(0, au.Minted.Mites); Assert.Equal(0, au.Melted.Mites + au.Burned.Mites);          // the only sources and sinks are the seed and the border (documented)
            Assert.True(r.Population > 0, $"the region died out in year {r.Year}");
        });
        output.WriteLine($"{years} years in {sw.Elapsed.TotalSeconds:0} s");
        output.WriteLine(EconomySoak.Summary(soak)); output.WriteLine(soak.Ticker.Stats.Report());
        Assert.True(soak.Ticker.Stats.P99HourMs < TestBudget.Ms(150), $"p99 hour {soak.Ticker.Stats.P99HourMs:0.0} ms");   // the plan's hourly budget on the reference PC; shared runners get the suite's x5
        AssertTargets(soak.Rows, start);
        return soak;
    }

    /// <summary>
    /// The Phase 2 done-when targets as the soak measures them (decision I46, changed by decision G13 for background growth): population within -10 percent and +25 percent of the
    /// start every year, and growing at most 0.5 percent a year averaged over each decade (a closing part of a decade counts by its own length); the under-15 share 26 to 33 percent
    /// averaged over the years after the first decade (the first carries the generated population's 35 percent down), and 26 to 35 percent in every later decade
    /// (seed 1234's years 31-40, the children of the births of years 20-30, average 34.2 percent); missed-meal household-days under 2 percent
    /// every year; honest in-band book-days, cash-solvent merchants and workshops completing a batch at least 60 percent averaged over the run, and in every year
    /// at least 40 percent of book-days in band (a drought's dearth: seed 1's harvest falls to 1,400 t in years 15, 19 and 25 and grain sits at 2-2.4 x base) and
    /// 50 percent of the merchants and workshops; the region's own coin growing at most 2 percent a year compound; the world's coin exact every year (checked in the run).
    /// </summary>
    /// <summary>Decision G13: population grows at most 0.5 percent a year, averaged over each decade (the first from the start head count, the last part by its own length).</summary>
    private static void AssertGrowthPerDecade(IReadOnlyList<SoakRow> rows, long start)
    {
        for (int d = 0; d < rows.Count; d += 10)
        {
            int n = Math.Min(10, rows.Count - d); long from = d == 0 ? start : rows[d - 1].Population, to = rows[d + n - 1].Population;
            Assert.True((to - from) * 1000 <= from * 5 * n, $"population grew {(to - from) * 100.0 / from / n:0.00} percent a year over years {d + 1}-{d + n} ({from} to {to})");
        }
    }

    private static void AssertTargets(IReadOnlyList<SoakRow> rows, long start)
    {
        foreach (var r in rows)
        {
            Assert.InRange(r.Population, start * 90 / 100, start * 125 / 100);
            Assert.True(r.HungryPct < 2.0, $"missed meals {r.HungryPct:0.00}% in year {r.Year}");
            Assert.True(r.BandPct >= 40, $"honest band {r.BandPct:0.0}% in year {r.Year}");                  // a drought year's dearth is out of band, and honestly so
            Assert.True(r.MerchantsSolventPct >= 50, $"cash-solvent merchants {r.MerchantsSolventPct:0}% in year {r.Year}");
            Assert.True(r.WorkshopCompletionPct >= 50, $"workshops {r.WorkshopCompletionPct:0}% in year {r.Year}");
        }
        AssertGrowthPerDecade(rows, start);
        for (int d = 10; d + 10 <= rows.Count; d += 10)
        {
            double u15 = rows.Skip(d).Take(10).Average(r => r.Under15Pct);
            Assert.InRange(u15, 26.0, 35.0);                                                       // a decade's echo of an earlier birth boom peaks at 34 percent (seed 1234, years 31-40)
        }
        if (rows.Count > 10) Assert.InRange(rows.Skip(10).Average(r => r.Under15Pct), 26.0, 33.0);
        Assert.True(rows.Average(r => r.BandPct) >= 60, $"honest band mean {rows.Average(r => r.BandPct):0.0}%");
        Assert.True(rows.Average(r => r.MerchantsSolventPct) >= 60, $"cash-solvent merchants mean {rows.Average(r => r.MerchantsSolventPct):0}%");
        Assert.True(rows.Average(r => r.WorkshopCompletionPct) >= 60, $"workshops mean {rows.Average(r => r.WorkshopCompletionPct):0}%");
        Assert.True(EconomySoak.CompoundGrowthPct(rows) <= 2.0, $"region coin grew {EconomySoak.CompoundGrowthPct(rows):0.00}% a year");
    }
}

/// <summary>The hourly tick budget of the soak world (Phase 3 cleanup: in the Timing collection, measured after a warm-up on a settled heap, best of two passes).</summary>
[Collection(TimingCollection.Name)]
public class EconomyTimingTests(ITestOutputHelper output)
{
    [Fact]
    public void HourlyTickTimings_AreReported_AndStayUnderTheBudget()
    {
        using var w = SoakWorld.Create();
        w.Ticker.AdvanceHours(24);                                          // warm-up (JIT, first slices)
        double p99 = TestBudget.BestPass(150, 2, () =>
        {
            w.Ticker.Stats.Reset();
            w.Ticker.AdvanceHours(24 * 30);
            var st = w.Ticker.Stats;
            output.WriteLine($"30 days, seed 1234, everything running: median {st.MedianHourMs:0.00} ms, p99 {st.P99HourMs:0.00} ms, max {st.MaxHourMs:0.0} ms, hour 0 median {st.MedianHour0Ms:0.0} ms, season-day max {st.MaxSeasonDayHourMs:0.0} ms");
            return st.P99HourMs;
        });
        Assert.True(p99 < 150, $"p99 {p99} ms in the best pass");           // abstract spec budget: 150 ms per hourly step on the reference PC
    }
}
