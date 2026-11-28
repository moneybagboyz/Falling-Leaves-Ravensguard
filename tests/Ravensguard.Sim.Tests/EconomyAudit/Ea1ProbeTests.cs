using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Tests.CloseOut;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>P8-EA-1: probes of the economy and growth audit (print only; run by hand with FL_EA1_PROBE=1).</summary>
public class Ea1ProbeTests(ITestOutputHelper output)
{
    private static T At<T>(WorldRuntime rt, Func<T> job) => rt.AtBarrier(job).GetAwaiter().GetResult();

    [Fact, Trait("Category", "Soak")]
    public void Probe_TheMinesAndExtractionCrews_YearByYear()
    {
        if (Environment.GetEnvironmentVariable("FL_EA1_PROBE") != "1") { output.WriteLine("skipped: FL_EA1_PROBE=1"); return; }
        int years = int.TryParse(Environment.GetEnvironmentVariable("FL_EA1_YEARS"), out var ny) ? ny : 2;
        foreach (ulong seed in new ulong[] { 1234, 1, 7, 42, 99 })
        {
            var (_, s0) = Phase6SoakTests.Game(seed);
            var reg = s0.Population.Region;
            output.WriteLine($"seed {seed} veins: " + string.Join("; ", reg.Veins.Select((v, i) => $"{i} {v.Type} {v.Tonnage} t r{v.RadiusTiles} at {v.TileX},{v.TileY}")));
            output.WriteLine($"seed {seed} mine sites: " + string.Join("; ", reg.Sites.Where(x => x.Kind == Ravensguard.Sim.WorldGen.Region.SiteKind.Mine).Select(x => $"{x.SubKind} {x.Tonnage} t {x.Mine} at {x.TileX},{x.TileY}")));
        }
        var (w, st) = Phase6SoakTests.Game(1234);
        using var rt = Phase6SoakTests.Boot(w, st);
        long start = rt.HourDone + 1; start += (24 - start % 24) % 24;
        for (int y = 1; y <= years; y++)
        {
            rt.AdvanceTo(GameTime.FromGameHours(start + y * TimeConst.DaysPerYear * 24L));
            var lines = At(rt, () =>
            {
                var hw = rt.Records.HomeWorld!; var prod = hw.Economy.Production; var led = hw.Sim.Economy.Ledger; var p = hw.Sim.People; var l = new List<string>();
                foreach (var b in prod.Businesses)
                {
                    var k = prod.KindOf(b);
                    if (b.Mine < 0) continue;
                    string mine = "";
                    if (b.Mine >= 0) { var m = prod.Mines[b.Mine]; mine = $" mine {m.Mineral} {m.RemainingG / 1_000_000}/{m.InitialG / 1_000_000} t levels {m.Levels} max {m.MaxMiners} state {m.State} closedUntil {m.ClosedUntilDay} wage {b.WageOfferMites} store {b.Store.Qty(MineWorks.OreGood(m.Mineral)!) / 1000} kg book: demand {hw.Economy.Markets.BookForSettlement(b.Settlement).Goods[hw.Sim.Economy.Goods.IdOf(MineWorks.OreGood(m.Mineral)!)].Demand} stock {hw.Economy.Markets.BookForSettlement(b.Settlement).Goods[hw.Sim.Economy.Goods.IdOf(MineWorks.OreGood(m.Mineral)!)].StockUnits / 1000} kg mid {hw.Economy.Markets.BookForSettlement(b.Settlement).MidRatioPermille(hw.Sim.Economy.Goods.IdOf(MineWorks.OreGood(m.Mineral)!))} pm"; }
                    int miners = b.Staff.Count(x => p.Occupation[x] == Occupation.Miner);
                    l.Add($"y{y} {k.Id} #{b.Index} s{b.Settlement} {b.State} staff {b.Staff.Count}/{k.StaffCapacity} miners {miners} unfilled {b.UnfilledDays} loss {b.LossSeasons} cash {led.Balance(b.Purse).Mites} jobs {string.Join(",", b.Jobs.Select(j => j.Kind + ":" + j.Workers))}{mine}");
                }
                return l;
            });
            foreach (var s in lines) output.WriteLine(s);
        }
    }
}
