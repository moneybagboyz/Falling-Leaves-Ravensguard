using System.Diagnostics;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Phase7Overview;

/// <summary>
/// OV-1 (overview 4, "2,000 soldiers hold 60 fps on the reference PC"), SIM SIDE ONLY. The 60 fps and the frame p99 of 16.6 ms need the reference PC and its GPU: this machine has none (llvmpipe), so the frame run is NOT measured here
/// and stays open for the user's machine (P7-G1). What this test holds is the bubble's tick budget at the size of the gate: 2,000 soldiers in contact, tick average within 25 ms (<see cref="TestBudget"/>: 5x on CI). The 6,000-tick
/// run with the worst tick of a minute (33 ms) is under the <c>Soak</c> trait; the 300 horses and the 0-allocation tick are CV-13's and BB-5's own tests.
/// </summary>
[Collection(TimingCollection.Name)]
public class Ov1BudgetTests(FightFixture fx, ITestOutputHelper output) : IClassFixture<FightFixture>
{
    private (double Avg, double Worst) Run(int ticks)
    {
        var w = FightCalibration.Skirmish(fx.C, 5, 1000); w.RecordEvents = false; w.UseGrid = true; w.LodCentre = (-7000, 0);          // 2,000 soldiers, the hero at one end
        var warm = FightCalibration.Skirmish(fx.C, 3, 30); warm.RecordEvents = false;
        for (int i = 0; i < 700 && !warm.IsOver; i++) warm.StepWithAi();
        for (int i = 0; i < 40; i++) w.StepWithAi();
        double sum = 0, worst = 0, minute = 0; int run = 0, mt = 0; var sw = new Stopwatch();
        for (int i = 0; i < ticks && !w.IsOver; i++)
        {
            sw.Restart(); w.StepWithAi(); double ms = sw.Elapsed.TotalMilliseconds; sum += ms; run++; minute = Math.Max(minute, ms);
            if (++mt == 1200) { worst = Math.Max(worst, minute); minute = 0; mt = 0; }
        }
        return (sum / Math.Max(1, run), Math.Max(worst, minute));
    }

    [Fact]
    public void OV1_SimSide_TwoThousandSoldiers_TickAverageWithin25ms_TheFrameRunOnTheReferencePcIsNotMeasuredHere()
    {
        double avg = TestBudget.BestPass(25, 3, () => Run(300).Avg);
        output.WriteLine($"2,000 soldiers, 300 ticks: average {avg:0.00} ms a tick (gate 25 ms on the reference PC, x5 on CI). The 60 fps and the frame p99 need the user's GPU: unmeasured.");
        Assert.True(avg <= TestBudget.Ms(25), $"average {avg:0.00} ms");
    }

    [Fact, Trait("Category", "Soak")]
    public void OV1_SimSide_TwoThousandSoldiers_6000Ticks_AverageWithin25ms_WorstTickOfAMinuteWithin33ms()
    {
        var (avg, worst) = Run(6000);
        output.WriteLine($"2,000 soldiers, 6,000 ticks: average {avg:0.00} ms, worst tick of a minute {worst:0.0} ms (gates 25 and 33)");
        Assert.True(avg <= TestBudget.Ms(25)); Assert.True(worst <= TestBudget.Ms(33));
    }
}
