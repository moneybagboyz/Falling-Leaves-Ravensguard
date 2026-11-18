using System.Diagnostics;
using System.Text;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>
/// A one-year headless run of seed 1234's region with markets, production, extraction and daily household consumption (production spec 9, money spec 10),
/// then the markets and production sections saved, loaded and saved again byte for byte. The soak reports price bands and bankruptcies; it fails only
/// on broken invariants (coin, mass, negative balances, mass violations), not on tuning.
/// </summary>
public class SoakTests(ITestOutputHelper output)
{
    [Fact]
    public void OneYear_Seed1234_KeepsEveryInvariant_AndRoundTripsItsSaveSections()
    {
        var sw = Stopwatch.StartNew();
        var w = SoakWorld.Create();
        w.Harvest(45);                                                   // last year's carry-over
        var ledger = w.Econ.Ledger; var cat = w.Econ.Goods;
        long coin0 = ledger.Audit().InPurses.Mites, seeded = ledger.Sourced(SourceReason.Seed).Mites;
        long hh = w.Pop.Households.Count;
        output.WriteLine($"world: {w.Pop.People.Count} people, {hh} households, {w.Markets.AllBooks().Count()} books, {w.Sys.Businesses.Count} businesses, {w.Layer.Count} resource rows, built in {sw.ElapsedMilliseconds} ms");

        // price samples per (good): mid / base ratio per book and day, where the good has traded in that book
        int goods = cat.Count;
        var ratios = new List<double>[goods]; for (int g = 0; g < goods; g++) ratios[g] = [];
        var atCap = new long[goods]; var samples = new long[goods];
        w.Run(60, (day, t) =>
        {
            Assert.True(ledger.Audit().IsConserved, $"coin audit failed on day {day}");
            if (day % 10 == 9) Assert.True(w.Econ.MassAuditHolds(), $"mass audit failed on day {day}");
            foreach (var b in w.Markets.AllBooks())
                for (ushort g = 0; g < goods; g++)
                {
                    var gb = b.Goods[g]; if (gb.LastTradedDay < 0) continue;
                    double r = b.MidRatioPermille(g) / 1000.0 * gb.RefMicro / gb.BaseRefMicro;   // mid over the content base price
                    ratios[g].Add(r); samples[g]++; if (b.MidRatioPermille(g) >= 3990) atCap[g]++;
                }
        });
        output.WriteLine($"60 days simulated in {sw.ElapsedMilliseconds} ms");

        // ---- invariants
        Assert.True(ledger.Audit().IsConserved);
        Assert.Equal(seeded, ledger.Audit().Seeded.Mites);                 // nothing minted, nothing created but the seed
        Assert.Equal(0, ledger.Audit().Minted.Mites);
        Assert.True(w.Econ.MassAuditHolds());
        Assert.Equal(0, w.Sys.MassViolations);
        for (int p = 0; p < ledger.PurseCount; p++) Assert.True(ledger.Balance(new PurseId(p)).Mites >= 0);
        foreach (var b in w.Markets.AllBooks())
            for (ushort g = 0; g < goods; g++) Assert.Equal(b.BookStore.Qty(g), b.Goods[g].Lots.Sum(l => l.Qty));

        // ---- report: flows
        var rep = new StringBuilder();
        rep.AppendLine($"coin: {coin0 / 100.0:N0} crowns at start; trade {ledger.Flow(TransferReason.Trade).Mites / 100.0:N0}, wages {ledger.Flow(TransferReason.Wage).Mites / 100.0:N0}, tolls {ledger.Flow(TransferReason.MarketToll).Mites / 100.0:N0}, dividends {ledger.Flow(TransferReason.Dividend).Mites / 100.0:N0}, stipends {ledger.Flow(TransferReason.Stipend).Mites / 100.0:N0} crowns; {ledger.Flow(TransferReason.Trade).Mites > 0}");
        rep.AppendLine($"batches: {w.Sys.BatchesFinished} finished, {w.Sys.BatchesStarted} started; quits {w.Sys.QuitCount}; forgiven arrears {w.Sys.ForgivenArrearsMites} mites");

        // ---- report: price bands
        rep.AppendLine("price band per good, mid / content base price over book-days with trade: min p10 median p90 max | days at the 4x cap");
        int goodsReported = 0;
        for (int g = 0; g < goods; g++)
        {
            if (ratios[g].Count < 20) continue;
            var s = ratios[g].OrderBy(x => x).ToList();
            double P(double q) => s[(int)Math.Min(s.Count - 1, q * s.Count)];
            rep.AppendLine($"  {cat.StringId((ushort)g),-18} {s[0],6:0.00} {P(0.1),6:0.00} {P(0.5),6:0.00} {P(0.9),6:0.00} {s[^1],6:0.00} | {100.0 * atCap[g] / samples[g],5:0.0}% of {samples[g]}");
            goodsReported++;
            Assert.InRange(s[0], 0.29 * 0.5, 100); Assert.InRange(P(0.5), 0.1, 80);
        }
        Assert.True(goodsReported >= 10);

        // ---- report: staples and fuel
        foreach (var id in new[] { "wheat", "flour", "bread", "firewood", "ale" })
        {
            ushort g = cat.IdOf(id);
            long prod = w.Sys.ProducedUnits[g], cons = w.Sys.ConsumedUnits[g];
            rep.AppendLine($"  {id,-9} produced {prod / 1e6:N1} t, used by recipes {cons / 1e6:N1} t, region stock {w.Markets.AllBooks().Sum(b => b.Goods[g].StockUnits) / 1e6:N1} t on books");
        }

        // ---- report: businesses, bankruptcies, arrears
        var kinds = w.Sys.Businesses.GroupBy(b => w.Sys.Content.Kinds[b.Kind].Id).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        int closed = w.Sys.Businesses.Count(b => b.State == BusinessState.Closed), losing = w.Sys.Businesses.Count(b => b.State == BusinessState.Open && b.LossSeasons >= 2);
        int idle = w.Sys.Businesses.Count(b => b.State == BusinessState.Open && w.Sys.Content.Kinds[b.Kind].Stations.Length > 0 && b.Batches == 0 && !w.Sys.Content.Kinds[b.Kind].Trader);
        long arrears = w.Sys.Businesses.Sum(b => b.ArrearsMites), monthly = Math.Max(1, w.Sys.Businesses.Sum(b => b.WagesSeason) * 2);
        rep.AppendLine($"businesses: {w.Sys.Businesses.Count} ({string.Join(", ", kinds.Select(k => $"{k.Key} {k.Count()}"))})");
        rep.AppendLine($"  bankrupt (closed): {closed}; two or more loss seasons: {losing}; workshops that never finished a batch: {idle}; wage arrears {arrears} mites vs a month of wages {monthly}");
        var trading = w.Sys.Businesses.Where(b => w.Sys.Content.Kinds[b.Kind].Trader).ToList();
        rep.AppendLine($"  merchants: {trading.Count}, median cash {trading.Select(b => ledger.Balance(b.Purse).Mites).OrderBy(x => x).ElementAtOrDefault(trading.Count / 2) / 100.0:N0} crowns");
        long hungry = 0; for (int h = 0; h < w.Pop.Households.Count; h++) if ((w.Econ.Grievances[h] & Grievance.Hunger) != 0) hungry++;
        rep.AppendLine($"households with the hunger grievance at the end: {hungry} of {hh}");
        rep.AppendLine($"extracted: logs {w.Sys.ProducedUnits[cat.IdOf("logs")] / 1e6:N1} t, firewood {w.Sys.ProducedUnits[cat.IdOf("firewood")] / 1e6:N1} t, fish {w.Sys.ProducedUnits[cat.IdOf("fish_fresh")] / 1e6:N1} t, charcoal {w.Sys.ProducedUnits[cat.IdOf("charcoal")] / 1e6:N1} t");
        var (ws, wc) = w.Layer.Totals(ResourceKind.Wood); var (fs, fc) = w.Layer.Totals(ResourceKind.Fish);
        rep.AppendLine($"resources: wood {100.0 * ws / wc:0.0}% of cap, fish {(fc == 0 ? 0 : 100.0 * fs / fc):0.0}% of cap");
        output.WriteLine(rep.ToString());
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "ravensguard_soak_report.txt"), rep.ToString());

        // ---- save sections: byte-identical round trip
        var ecoBytes = SaveCodec.SerializeCompressed(EconomySave.Capture(w.Econ));
        var mBytes = SaveCodec.SerializeCompressed(MarketsSave.Capture(w.Markets));
        var pBytes = SaveCodec.SerializeCompressed(ProductionSave.Capture(w.Sys));
        var econ2 = SaveCodec.DeserializeCompressed<EconomySave>(ecoBytes).Restore(w.Pop, EconFixture.Content);
        var markets2 = new MarketSystem(econ2, w.Pop, SoakWorld.Seed);
        var sys2 = SaveCodec.DeserializeCompressed<ProductionSave>(pBytes).Restore(econ2, w.Pop, ProdKit.Content, SoakWorld.Seed, markets2);
        SaveCodec.DeserializeCompressed<MarketsSave>(mBytes).Restore(markets2, sys2);
        Assert.Equal(w.Econ.ComputeHash(), econ2.ComputeHash());
        Assert.Equal(ecoBytes, SaveCodec.SerializeCompressed(EconomySave.Capture(econ2)));
        Assert.Equal(mBytes, SaveCodec.SerializeCompressed(MarketsSave.Capture(markets2)));
        Assert.Equal(pBytes, SaveCodec.SerializeCompressed(ProductionSave.Capture(sys2)));
        output.WriteLine($"save sections: economy {ecoBytes.Length / 1024} KB, markets {mBytes.Length / 1024} KB, production {pBytes.Length / 1024} KB; total {sw.ElapsedMilliseconds} ms");
    }
}
