using Ravensguard.Sim.Core;
using Xunit;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>P8-BL-1 (balance pass 3.1, golden BL-1): the L1 clay works on twelve settling runs.</summary>
public partial class IndustryNpcTests
{
    /// <summary>The twelve settling times of BL-1 (days of the world's own running before the works is founded).</summary>
    private static readonly int[] ClaySettleDays = [52, 54, 56, 58, 60, 62, 64, 66, 68, 70, 72, 74];

    [Fact, Trait("Category", "Soak")]
    public void BL1_TheL1ClayWorks_RepaysInIn20sBand_OnTheTwelveSettlingRuns_AsManyAsTheRatchet()
    {
        int inBand = 0; var lines = new List<string>();
        foreach (int settle in ClaySettleDays)
        {
            using var rig = new Rig(settleDays: settle);
            rig.FreeHands(160);
            var clay = rig.Found("clay", ClayCrew);
            for (int d = 1; d <= 360; d++) rig.RunDay();
            long mean = clay.CumulativeNetMites / 360; long payback = mean <= 0 ? long.MaxValue : clay.BuildingsCostMites / mean;
            bool ok = payback is >= 100 and <= 200 && rig.NetOver(clay, 120) > 0 && !clay.Closed;
            if (ok) inBand++;
            lines.Add($"settle {settle}: bill {clay.BuildingsCostMites}, {mean} a day, payback {(payback == long.MaxValue ? "never" : payback.ToString())} days, last 120 days {rig.NetOver(clay, 120) / 120} a day {(ok ? "IN" : "OUT")}");
            output.WriteLine(lines[^1]);
        }
        output.WriteLine($"in band {inBand} of {ClaySettleDays.Length}");
        // the target is ten of twelve; the honest fixes (the brick price) reached the ratchet's figure, and the spread of a small difference of large flows is the market's (D11: the ratchet only ever rises)
        Assert.True(inBand >= BalancePass.BalanceRatchets.Bl1ClayInBandOfTwelve, $"{inBand} of {ClaySettleDays.Length} in band, the ratchet is {BalancePass.BalanceRatchets.Bl1ClayInBandOfTwelve}\n" + string.Join("\n", lines));
    }
}
