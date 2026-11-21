using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>P7-EA2: one probe of the running economy (seed 1234, two game years): the border faucet (S-6), the books' demand and cover for the goods the bottleneck rows name (B1-B6) and the market depth an industry's output meets (I2). Quoted in docs/reviews/phase-7-audit.md.</summary>
public class AuditWorldProbeTests(ITestOutputHelper output)
{
    [Fact, Trait("Category", "Soak")]
    public void EA2_Probe_TwoYears_BorderBottlenecksAndMarketDepth()
    {
        using var run = AuditWorld.Create();
        var led = run.Econ.Ledger; var cat = run.Econ.Goods; var markets = run.Economy.Markets; var trade = run.Economy.Trade;
        long people0 = run.State.Abstract!.Vital.Alive, coin0 = led.Audit().InPurses.Mites;
        int day0 = run.NextSlotDay; long peakDemand = 0, peakSupply = 0, peakUnmet = 0; int peakDay = 0;
        for (int y = 1; y <= 4; y++) { for (int d = day0 + (y - 1) * 60; d < day0 + y * 60; d++) { run.AdvanceToDayEnd(d); var l = run.Economy.Farming.LastLabour; if (l.DemandHours > peakDemand) { peakDemand = l.DemandHours; peakSupply = l.SupplyHours; peakUnmet = l.UnmetHours; peakDay = d - day0; } } output.WriteLine($"   year {y}: coin {led.Audit().InPurses.Mites:N0}, immigrate so far {led.Sourced(SourceReason.Immigrate).Mites:N0}, emigrate {led.Sunk(SinkReason.Emigrate).Mites:N0}"); }
        long people = run.State.Abstract!.Vital.Alive, coin = led.Audit().InPurses.Mites;
        output.WriteLine($"S-6 border after 4 game years: people {people0} -> {people}; coin {coin0:N0} -> {coin:N0} ({(double)(coin - coin0) / coin0:P2}); immigrate {led.Sourced(SourceReason.Immigrate).Mites:N0}, emigrate {led.Sunk(SinkReason.Emigrate).Mites:N0}, melt {led.Sunk(SinkReason.Melt).Mites}, mint {led.Sourced(SourceReason.Mint).Mites}");
        output.WriteLine($"   desk exports {trade.DeskExportedMites:N0} mites, desk imports {trade.DeskImportedMites:N0}; all exports {trade.ExportedMites:N0}, imports {trade.ImportedMites:N0}");
        foreach (var e in trade.Link.Mine.Edges) output.WriteLine($"   edge to neighbour {e.Neighbour}: pay budget {e.PayBudgetMites:N0}, coin net in {e.CoinNetIn:N0}, tolls {e.TollMites:N0}");
        long path = people * 5000, band = path * 50 / 1000;
        output.WriteLine($"   the desk's room: coin path {path:N0}, band +-{band:N0}; room to export while coin < path+band = {Math.Max(0, path + band - coin):N0} mites ({Math.Max(0, path + band - coin) / 76_308.0:0.0} days of an L3 iron works' 76,308 a day)");

        output.WriteLine("what the economy made in four years (mass created, kg, by the ledger) and the businesses by kind:");
        foreach (var name in new[] { "iron_ore", "iron_bloom", "iron_bar", "charcoal", "logs", "timber_planks", "firewood", "clay", "bricks", "nails", "horseshoes", "wheat", "bread", "flour", "silver", "galena", "lead_argentiferous" })
            if (cat.TryIdOf(name, out var gid)) output.WriteLine($"   {name,-18} created {run.Econ.Mass.TotalCreated(gid) / 1000.0,12:N0} kg ({(cat.IsPiece(gid) ? "pieces" : "mass")}), destroyed {run.Econ.Mass.TotalDestroyed(gid) / 1000.0,12:N0}");
        foreach (var g in run.Economy.Production.Businesses.GroupBy(b => run.Economy.Production.KindOf(b).Id).OrderBy(g => g.Key, StringComparer.Ordinal)) output.WriteLine($"   business {g.Key,-18} {g.Count(),4} ({g.Count(b => b.State == Ravensguard.Sim.Production.BusinessState.Open)} open)");
        output.WriteLine("good             demand kg/day  stock kg   cover days  mid/ref   (region sum over books)");
        foreach (var name in new[] { "iron_bar", "iron_ore", "charcoal", "bricks", "logs", "timber_planks", "firewood", "clay", "wheat", "rye", "barley", "bread", "flour", "oats", "hay", "salt", "nails", "horseshoes" })
        {
            if (!cat.TryIdOf(name, out var g) || cat.IsPiece(g)) { if (cat.TryIdOf(name, out var gp)) output.WriteLine($"{name,-16} (counted)"); continue; }
            double demand = 0, stock = 0, mid = 0, rf = 0; int n = 0;
            foreach (var b in markets.AllBooks()) { var gb = b.Goods[g]; demand += gb.Demand; stock += gb.StockUnits; if (gb.StockUnits > 0 || gb.DemandEma > 0) { mid += b.MidMicro(g); rf += gb.RefMicro; n++; } }
            output.WriteLine($"{name,-16}{demand / 1000,13:0.0}{stock / 1000,11:0}{(demand > 0 ? stock / demand : 0),12:0.0}{(rf > 0 ? mid / rf : 0),9:0.00}");
        }
        var lv = run.Economy.Farming.State.Livestock;
        output.WriteLine($"B5 draught oxen on the region's farms {lv.Oxen.Sum()}, cows {lv.Cows.Sum()}, sheep {lv.Sheep.Sum()}; a baggage train asks 60 carts (the first cut's carts come from the spec, not from the region: no vehicle inventory exists outside the hero and the caravans)");
        var lab = run.Economy.Farming.LastLabour; output.WriteLine($"B6 the busiest farm day of four years (day {peakDay} of the run): demand {peakDemand:N0} h, supply {peakSupply:N0} h ({(double)peakSupply / Math.Max(1, peakDemand):0.0}x), unmet {peakUnmet:N0} h; 1,040 levies at 8 h are {1040 * 8:N0} h a day = {1040 * 8.0 / Math.Max(1, peakSupply):P0} of that day's supply");
        output.WriteLine($"B6 farm labour last day: demand {lab.DemandHours} h, supply {lab.SupplyHours} h, unmet {lab.UnmetHours} h; totals demand {run.Economy.Farming.TotalDemandH:N0} h, unmet {run.Economy.Farming.TotalUnmetH:N0} h");
        long silverG = run.Econ.Mass.TotalCreated(cat.IdOf("silver")); double silverMites = silverG / 1000.0 * cat.Def(cat.IdOf("silver")).BasePriceMitesPerKg, perYear = silverMites / 4 / coin0;
        output.WriteLine($"S-2 realised: {silverG / 1000.0:0} kg of silver made in four years = {silverMites:N0} mites = {perYear:P2} of the starting money supply a year (target 1-2%); it sits as a good, no mint strikes it");
        Assert.InRange(perYear, 0.0, 0.03);          // pinned: the two generated silver mines give about 2% a year today; CN1 and the deposit grades (F-4) must keep it in 1-2% when the mint is wired
        Assert.True(run.CoinConserved && run.MassOk);
    }
}
