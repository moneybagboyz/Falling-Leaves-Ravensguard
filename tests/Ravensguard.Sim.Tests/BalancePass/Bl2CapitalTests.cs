using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.WorldGen;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.BalancePass;

/// <summary>
/// P8-BL-1 (EA_F18, balance pass 5 BL-2): the scripted L3 iron works of the war-load harness (the audit's own fixture: war, caravans to a neighbouring region, no fuel faucet, seed money of 3 million with 1 million top-ups) on five seeds. With the charcoal, ore
/// and barrels bought on 30 days' terms the works' capital need (the deepest its purse has sunk below the capital put into it; the Industry tab shows it beside the bill) should be four times its bill or less on three of the five seeds.
/// </summary>
public class Bl2CapitalTests(ITestOutputHelper output)
{
    private static readonly ulong[] Seeds = [1234, 42, 7, 1, 99];
    private const int Years = 8;

    [Fact, Trait("Category", "Soak")]
    public void BL2_TheL3sCapitalNeed_WithThirtyDayTerms_IsFourTimesItsBillOrLess_OnAsManySeedsAsTheRatchet()
    {
        int under = 0; var lines = new List<string>();
        foreach (ulong seed in Seeds)
        {
            using var run = WarLoadRun.Create(seed, ContinentSettings.FindBaseContent()!);
            var soak = new WarLoadSoak(run, Years) { L3 = new L3IronWorks(run, L3Sale.Caravan) };
            soak.Run();
            var w = soak.L3!.Works!; long bill = soak.L3.BillMites, need = w.CapitalNeedMites;
            Assert.True(soak.Ok, "coin and mass exact every day on seed " + seed);
            Assert.True(bill > 0 && need > 0, $"the tab's capital need is read on seed {seed}");
            bool ok = need <= 4 * bill; if (ok) under++;
            lines.Add($"seed {seed}: bill {bill:N0}, capital need {need:N0} = {need * 10 / bill / 10.0:0.0} x the bill, top-ups {soak.L3.TopUps} of seed money {soak.L3.SeedMites:N0}, {(ok ? "within" : "over")} 4x");
            output.WriteLine(lines[^1]);
        }
        output.WriteLine($"{under} of {Seeds.Length} seeds within 4x the bill (target 3)");
        Assert.True(under >= BalanceRatchets.Bl2SeedsWithinFourTimesTheBill, $"{under} of {Seeds.Length}, the ratchet is {BalanceRatchets.Bl2SeedsWithinFourTimesTheBill}\n" + string.Join("\n", lines));
    }
}
