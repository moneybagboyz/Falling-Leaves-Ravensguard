using Ravensguard.Sim.Core;
using Ravensguard.Sim.Production.Industry;
using Xunit;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>P8-EA-1: a probe of IN-20's run (print only; by hand with FL_EA1_PROBE=1): every 30 days the town book's bricks, firewood, logs and planks, and each works' stage nets over the last 30 days.</summary>
public partial class IndustryNpcTests
{
    [Fact, Trait("Category", "Soak")]
    public void Ea1Probe_IN20_TheBooksAndTheStages()
    {
        if (Environment.GetEnvironmentVariable("FL_EA1_PROBE") != "1") { output.WriteLine("skipped: FL_EA1_PROBE=1"); return; }
        using var rig = new Rig();
        rig.FreeHands(160);
        var works = new[] { rig.Found("iron", IronCrew), rig.Found("clay", ClayCrew), rig.Found("wood", WoodCrew) };
        var cat = rig.State.Economy!.Goods; var book = rig.State.Markets!.BookForSettlement(rig.Town);
        string G(string g) { var id = cat.IdOf(g); var gb = book.Goods[id]; return $"{g} d{gb.Demand / 1000}kg s{gb.StockUnits / 1000}kg {book.MidRatioPermille(id)}pm"; }
        for (int d = 1; d <= 360; d++)
        {
            rig.RunDay();
            if (d % 30 != 0) continue;
            output.WriteLine($"day {d}: {G("bricks")}, {G("firewood")}, {G("straw")}, {G("logs")}, {G("timber_planks")}, {G("sawdust")}, {G("iron_bloom")}, {G("iron_ore")}");
            foreach (var x in works)
                output.WriteLine($"   {x.Chain}: net30 {rig.NetOver(x, 30)} | " + string.Join(" | ", x.Stages.Select(s => $"{rig.Iw.KindOf(s).Id.Replace("stage_", "")} in {s.Sum(WorkStage.Income, rig.Day, 30)} w {s.Sum(WorkStage.Wages, rig.Day, 30)} i {s.Sum(WorkStage.Inputs, rig.Day, 30)} u {s.Sum(WorkStage.Upkeep, rig.Day, 30)}")));
        }
    }
}
