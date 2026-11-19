using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Tests.Combat.Resolver2;

/// <summary>The duel harness (tools/Ravensguard.Calibrate) on a small sample: the resolver-v2 8 targets the resolver owns.</summary>
[Collection("resolver2")]
public class CalibrationTests(V2Fixture f)
{
    [Fact]
    public void DuelHarness_V2MeetsTheResolverTargets()
    {
        var sword = new WeaponInstance("arming_sword", "steel");
        var swords = Calibration.Run(f.Tables, new("sword vs sword", sword, Calibration.Padded, sword, Calibration.Padded), 150);
        Assert.InRange(swords.MeanS, 10, 20);                                            // was 8.0 s on v1
        Assert.InRange(swords.WinAPct, 40, 60);
        Assert.True(swords.SeversPerDuel <= 0.12, $"{swords.SeversPerDuel} severs per duel");
        var spear = Calibration.Run(f.Tables, new("spear vs sword", new("spear", "steel"), Calibration.Padded, sword, Calibration.Unarmoured), 150);
        Assert.True(spear.WinAPct >= 55, $"spear wins {spear.WinAPct}%");                // was 34% on v1
        var plate = Calibration.Run(f.Tables, new("sword vs plate", sword, Calibration.Padded, sword, Calibration.Plated), 100);
        Assert.True(plate.MailConverted * 10 >= plate.BlowsOnMailEdged * 9, "mail turns cuts into blunt blows");
        Assert.True(plate.PlateStopped > 0 && plate.PlateStoppedOut * 50 <= plate.PlateStoppedIn, "rigid plate soaks at least 98% of a stopped blow");
        var ranking = Calibration.MetalRanking(f.T, 500);
        for (int i = 1; i < ranking.Count; i++) Assert.True(ranking[i].CutsPerMille > ranking[i - 1].CutsPerMille, string.Join(", ", ranking));
    }
}
