using Ravensguard.Sim.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>P7-EA3b: the second audit run's harness (the scripted L3 iron works, the money and works columns, the waived and control-relative gates). The five-seed numbers are in docs/reviews/phase-7-audit.md; these cases keep the fixture honest.</summary>
public class WarLoadSecondRunTests(ITestOutputHelper output)
{
    /// <summary>Three peace years (about a minute): the L3 works is founded on day 1 of Y2 at a town of the route by a young owner, every stage at tier 3, coin and mass exact every day, the money columns add up (the supply moves only by the border, the mints and the harness's own seed).</summary>
    [Fact, Trait("Category", "Soak")]
    public void L3IronWorks_IsFoundedNearTheRoute_AndTheMoneyColumnsAddUp()
    {
        using var run = AuditWorld.Create();
        var soak = new WarLoadSoak(run, 3, control: true) { L3 = null };
        soak.L3 = new L3IronWorks(run, L3Sale.Caravan);
        soak.Run(r => output.WriteLine(WarLoadSoak.Format(r)));
        Assert.True(soak.Ok, soak.FirstFailure);
        var l3 = soak.L3!;
        Assert.NotNull(l3.Works); Assert.True(l3.BillMites > 0, l3.Note); Assert.True(l3.SeedMites >= 3_000_000);
        Assert.All(l3.Works!.Stages, s => Assert.Equal(3, s.Tier));
        output.WriteLine(l3.Note); foreach (var r in soak.Rows) output.WriteLine(WarLoadSoak.FormatEx(r) + " | " + WarLoadSoak.FormatBorder(r));
        // from Y2 on, the year's change of the supply is exactly what crossed the border plus what the mints struck less what was melted, less the harness's one-off seed
        for (int y = 1; y < soak.Rows.Count; y++)
        {
            var a = soak.Rows[y - 1]; var b = soak.Rows[y];
            long grew = (b.MoneySupply + b.HarnessSeedMites) - (a.MoneySupply + a.HarnessSeedMites);
            long parts = b.ImmigMites - b.EmigMites + b.MintMites - b.MeltMites + (b.HarnessSeedMites - a.HarnessSeedMites);
            Assert.Equal(parts, grew);
        }
        Assert.Equal(0, soak.Rows[0].WorksOpen);                                // nothing before the works is founded
        Assert.True(soak.Rows[1].WorksOpen >= 1);
    }

    /// <summary>WL-6 is waived by the user (overview 10e) and WL-10 is read against the control at the same year (EA_F10, accepted).</summary>
    [Fact]
    public void Gates_Wl6IsWaived_Wl10IsReadAgainstTheControl()
    {
        WarYearRow Row(int y, long harvest, long wages) => new() { Year = y, Alive = 25_000, HarvestT = harvest, WagesPerDay = wages, MoneySupply = 100_000_000, SolventPct = 95, CoinOk = true, MassOk = true, Idle = 10 };
        var war = Enumerable.Range(1, 12).Select(y => Row(y, y == 8 ? 2_000 : 2_700, y <= 5 ? 1_000_000 : 1_200_000)).ToList();
        var ctl = Enumerable.Range(1, 12).Select(y => Row(y, 2_700, y <= 5 ? 1_000_000 : 1_200_000)).ToList();
        var gates = WarGates.Evaluate(war, ctl);
        Assert.Equal(AuditState.Info, gates.Single(g => g.Id == "WL-6").State);
        Assert.Equal(AuditState.Pass, gates.Single(g => g.Id == "WL-10").State);      // +20% from the Y3-Y5 mean, 0% from the control
    }
}
