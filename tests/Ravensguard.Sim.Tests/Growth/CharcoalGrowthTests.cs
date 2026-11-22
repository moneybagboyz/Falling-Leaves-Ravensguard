using Ravensguard.Sim.Core;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Tests.Production;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Growth;

/// <summary>P7-EA3e (EA_F19): charcoal supply follows demand. Where charcoal is dear and the woods are in reach, new crews of burners open (one a season at most), hire from the labour market, buy their logs from the books (so the woodcutters cut the woodland) and sell charcoal.</summary>
public class CharcoalGrowthTests(ITestOutputHelper output)
{
    private sealed record Result(int Crews, int NewCrews, int Staffed, long CharcoalMade, long LogsMade, int DearBooks, bool CoinOk, bool MassOk, string Sig);

    private static Result Run(bool crews, int days)
    {
        using var w = SoakWorld.Create();
        w.Economy.Growth.CharcoalCrews = crews;
        var kinds = w.Sys.Content.Kinds; int businesses0 = w.Sys.Businesses.Count;
        var ch = w.Econ.Goods.IdOf("charcoal"); var lg = w.Econ.Goods.IdOf("logs");
        w.Run(days);
        int open = 0, fresh = 0, staffed = 0;
        foreach (var b in w.Sys.Businesses)
        {
            if (kinds[b.Kind].Id != "charcoal_crew" || b.State != BusinessState.Open) continue;
            open++;
            if (b.Index >= businesses0)
            {
                fresh++; if (b.Staff.Count > 0) staffed++;
                Assert.Equal(-1, b.Lot); Assert.Equal(4, b.Stations.Count); Assert.True(b.Owner >= 0);
                Assert.True(w.Economy.Woodland.RowsOf(b.Settlement).Count > 0, "a new crew sits where the woods are");
                Assert.True(w.Economy.Woodland.AvailableG(b.Settlement) >= w.Economy.Content.Growth.CharcoalMinWoodG / 4, "and where wood is left to cut");
            }
        }
        int dear = w.Markets.AllBooks().Count(bk => bk.MidRatioPermille(ch) >= w.Economy.Content.Growth.UnmetPricePermille);
        return new Result(open, fresh, staffed, w.Econ.Mass.TotalCreated(ch), w.Econ.Mass.TotalCreated(lg), dear,
            w.Econ.Ledger.Audit().IsConserved, w.Econ.MassAuditHolds(), $"{open}/{fresh}/{staffed}/{w.Econ.Mass.TotalCreated(ch)}/{w.Econ.Mass.TotalCreated(lg)}/{w.Econ.Ledger.Audit().InPurses.Mites}");
    }

    [Fact]
    public void EA3e_DearCharcoal_NewBurnersOpenNearWoodland_HireAndCutRealWood_CoinAndMassExact()
    {
        var off = Run(false, 180); var on = Run(true, 180);
        output.WriteLine($"without: {off}\nwith: {on}");
        Assert.Equal(0, off.NewCrews);
        Assert.InRange(on.NewCrews, 3, 12);                                           // one a season at most: 180 days are twelve seasons, three of them waiting
        Assert.True(on.Staffed >= 2, "the new crews hired hands through the labour market");
        Assert.True(on.CharcoalMade >= off.CharcoalMade * 95 / 100, $"charcoal made {on.CharcoalMade} against {off.CharcoalMade}");      // the supply is limited by what the books absorb, so it does not fall, and need not grow
        Assert.True(on.LogsMade > off.LogsMade, "the woodcutters cut more wood for the burners: the logs come off the woodland rows");
        Assert.True(on.CoinOk && on.MassOk && off.CoinOk && off.MassOk);
    }

    [Fact]
    public void EA3e_TheGrowthIsDeterministic_TwoRunsEqual()
    {
        var a = Run(true, 120); var b = Run(true, 120);
        Assert.Equal(a.Sig, b.Sig);
        Assert.True(a.NewCrews > 0);
    }
}
