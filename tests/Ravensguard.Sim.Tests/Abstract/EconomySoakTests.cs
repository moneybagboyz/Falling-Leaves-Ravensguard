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
        long seeded = led.Audit().Seeded.Mites;
        soak.Run(1, r => output.WriteLine(EconomySoak.Format(r)));
        var row = soak.Rows[0];
        output.WriteLine(EconomySoak.Header()); output.WriteLine($"1 year in {sw.ElapsedMilliseconds} ms");

        // invariants
        Assert.True(led.Audit().IsConserved); Assert.Equal(seeded, led.Audit().Seeded.Mites); Assert.Equal(0, led.Audit().Minted.Mites); Assert.Equal(0, led.Audit().Sunk.Mites);
        Assert.True(w.Econ.MassAuditHolds()); Assert.Equal(0, w.Sys.MassViolations);
        for (int p = 0; p < led.PurseCount; p++) Assert.True(led.Balance(new PurseId(p)).Mites >= 0);
        foreach (var b in w.Markets.AllBooks()) for (ushort g = 0; g < w.Econ.Goods.Count; g++) Assert.Equal(b.BookStore.Qty(g), b.Goods[g].Lots.Sum(l => l.Qty));
        Assert.Equal(sim.Vital.Alive, Enumerable.Range(0, sim.People.Count).Count(i => sim.People.Status[i] == PersonStatus.Alive));

        // the economy runs: bread and ale are made, workshops finish batches, merchants and businesses are open, people are fed
        Assert.True(row.BreadT > 0 && row.AleKl > 0, $"bread {row.BreadT} t, ale {row.AleKl} kl");
        Assert.True(row.WorkshopCompletionPct >= 40, $"workshops completing a batch {row.WorkshopCompletionPct}%");
        Assert.True(row.MerchantsSolventPct >= 30, $"merchants solvent {row.MerchantsSolventPct}%");
        Assert.True(row.HungryPct < 2.0, $"hungry household-days {row.HungryPct}%");
        Assert.InRange(row.Population, 25_963 * 90 / 100, 25_963 * 110 / 100);
        Assert.True(w.Economy.Labour.Hired > 0 && w.Economy.Farming.State.HarvestG >= 0);
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

    [Fact]
    public void HourlyTickTimings_AreReported_AndStayUnderTheBudget()
    {
        using var w = SoakWorld.Create();
        w.Ticker.AdvanceHours(24);                                          // warm-up (JIT, first slices)
        w.Ticker.Stats.Reset();
        w.Ticker.AdvanceHours(24 * 30);
        var st = w.Ticker.Stats;
        output.WriteLine($"30 days, seed 1234, everything running: median {st.MedianHourMs:0.00} ms, p99 {st.P99HourMs:0.00} ms, max {st.MaxHourMs:0.0} ms, hour 0 median {st.MedianHour0Ms:0.0} ms, season-day max {st.MaxSeasonDayHourMs:0.0} ms");
        Assert.True(st.P99HourMs < 150, $"p99 {st.P99HourMs} ms");         // abstract spec budget: 150 ms per hourly step on the reference PC
    }

    /// <summary>One hundred game years of seed 1234 on the abstract tick (about 6 minutes); prints the table of <c>WorldTool soak --years 100</c>.</summary>
    [Fact, Trait("Category", "Soak")]
    public void HundredYears_RealSystems_KeepEveryInvariant_AndReportTheTargets()
    {
        using var w = SoakWorld.Create(); var soak = new EconomySoak(w.Headless);
        output.WriteLine(EconomySoak.Header());
        long seeded = w.Econ.Ledger.Audit().Seeded.Mites; var sw = Stopwatch.StartNew();
        soak.Run(100, r =>
        {
            if (r.Year == 1 || r.Year % 5 == 0) output.WriteLine(EconomySoak.Format(r));
            Assert.True(r.CoinConserved && r.MassOk && r.MassViolations == 0, $"audit failed in year {r.Year}");
            Assert.Equal(seeded, r.CoinSources); Assert.Equal(0, r.CoinSinks);
            Assert.True(r.Population > 0, $"the region died out in year {r.Year}");
        });
        output.WriteLine($"100 years in {sw.Elapsed.TotalSeconds:0} s");
        output.WriteLine(EconomySoak.Summary(soak)); output.WriteLine(soak.Ticker.Stats.Report());
        Assert.True(soak.Ticker.Stats.P99HourMs < 150);
        Assert.True(w.Sys.Businesses.Any(b => b.State == BusinessState.Open));
        // the plan's targets, as the soak reports them
        var rows = soak.Rows;
        output.WriteLine($"TARGETS population {rows[^1].Population} vs {rows[0].Population} start (+-10%: {(Math.Abs(rows[^1].Population - 25_963) <= 2_596 ? "met" : "NOT met")}); under 15 share {rows[^1].Under15Pct:0.0}% (26-33%); staples in band {rows.Average(r => r.BandPct):0.0}% of book-days (90%); workshops completing {rows.Average(r => r.WorkshopCompletionPct):0}% (90%)");
    }
}
