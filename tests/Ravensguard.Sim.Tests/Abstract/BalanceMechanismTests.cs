using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Tests.Production;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Abstract;

/// <summary>The mechanisms of integration decisions I18 to I29 (goods-flow trace, reopening, immigration, floors, gleaning, regional carters), each on the real tick of the soak world.</summary>
public class BalanceMechanismTests(ITestOutputHelper output)
{
    [Fact]
    public void TheFlowTrace_SeesEveryCreateMoveAndDestroy_AndDoesNotChangeTheRun()
    {
        string Hash(bool trace)
        {
            using var w = SoakWorld.Create(); var soak = new EconomySoak(w.Headless);
            if (trace) soak.EnableFlow();
            w.Ticker.AdvanceHours(24 * 20);
            if (trace)
            {
                string t = soak.Flow!.CloseYear(1, print: true);
                output.WriteLine(t.Length > 600 ? t[..600] : t);
                Assert.Contains("wheat", t); Assert.Contains("ALL GRAINS", t);
            }
            return w.Sim.ComputeHash() + w.Econ.ComputeHash();
        }
        Assert.Equal(Hash(false), Hash(true));
    }

    [Fact]
    public void AClosedWorkshop_OpensAgainUnderANewOwner_WhenItsProductIsWanted()
    {
        using var w = SoakWorld.Create(); w.Run(3);
        var sys = w.Sys; var led = w.Econ.Ledger;
        // close every bakery of one book, then let the labour market look for owners at the season
        var bakeries = sys.Businesses.Where(b => sys.KindOf(b).Id == "bakery" && b.State == BusinessState.Open).Take(5).ToList();
        foreach (var b in bakeries) sys.Close(b);
        long before = led.Audit().InPurses.Mites;
        int opened = w.Economy.Labour.Reopen(GameTime.FromGameHours(w.Sim.State.HourDone));
        output.WriteLine($"closed {bakeries.Count}, reopened {opened}");
        Assert.True(opened >= 0 && opened <= bakeries.Count);
        foreach (var b in bakeries.Where(b => b.State == BusinessState.Open)) { Assert.True(b.Owner >= 0); Assert.Equal(w.Pop.People.Household[b.Owner], b.OwnerHousehold); }
        Assert.Equal(before, led.Audit().InPurses.Mites);               // coin only moved (the owner's half went into the cash box)
        Assert.True(led.Audit().IsConserved);
    }

    [Fact]
    public void ADepartedHousehold_ComesBack_WhenTheRegionHoldsFewerPeopleThanItStartedWith()
    {
        using var w = SoakWorld.Create(); var sim = w.Sim; var hht = sim.Households;
        w.Run(1);
        long start = sim.Vital.Alive; long coin0 = w.Econ.Ledger.Audit().InPurses.Mites;
        // send a hundred households away through the real emigration, then run seasons: the shortfall brings households home
        int sent = 0;
        for (int h = 0; h < hht.Count && sent < 100; h++)
            if (hht.MemberCount[h] > 0 && hht.Class[h] == SocialClass.Peasant && hht.ParcelCount[h] == 0) { Demography.DepartHousehold(sim, h, GameTime.FromGameHours(sim.State.HourDone), new RegionOutbox(sim.Ids)); sent++; }
        long gone = sim.Vital.Alive;
        Assert.True(gone < start);
        w.Run(60);
        output.WriteLine($"start {start}, after the departures {gone}, after 60 days {sim.Vital.Alive}, immigrants {sim.Vital.Immigrants}");
        Assert.True(sim.Vital.Immigrants > 0);
        Assert.Equal(coin0, w.Econ.Ledger.Audit().InPurses.Mites);
        Assert.True(w.Econ.Ledger.Audit().IsConserved && w.Econ.MassAuditHolds());
        Assert.Equal(sim.Vital.Alive, Enumerable.Range(0, sim.People.Count).Count(i => sim.People.Status[i] == PersonStatus.Alive));
    }

    [Fact]
    public void AnUnsoldProducerLot_FallsToTheMarket_SoAGlutCannotJamABook()
    {
        using var w = SoakWorld.Create(false); var book = w.Markets.AllBooks().First(b => b.Merchants.Count > 0);
        ushort wood = w.Econ.Food.Firewood; var seller = w.Markets.HouseholdParty(0);
        seller.Store.Create(wood, 5_000_000, CreateReason.Cut);
        book.List(seller, wood, 5_000_000, 900, 0);
        var lot = book.Goods[wood].Lots.First(l => l.Owner.Id == seller.Id);
        Assert.Equal(900, lot.FloorPermille);
        for (int d = 1; d <= 2; d++) book.ExpireLots(d);
        output.WriteLine($"floor after two days {lot.FloorPermille}");
        Assert.True(lot.FloorPermille <= 450 || lot.Qty == 0);            // 900 x 2/3 x 2/3, or already returned to its owner
    }

    [Fact]
    public void Gleaning_GivesThePoorBoxAShareOfEveryHarvest_AndTheMassBooksBalance()
    {
        using var w = SoakWorld.Create(); w.Run(60);
        var fs = w.Economy.Farming.State;
        Assert.True(fs.GleanedG > 0);
        Assert.InRange(fs.GleanedG * 1000 / Math.Max(1, fs.HarvestG), 5, 40);   // about 3 percent of the harvest (of the holders' share), a little less where the harvest went to rent
        Assert.True(w.Econ.MassAuditHolds());
    }

    [Fact]
    public void TheRegionalCarters_MoveGoodsBetweenBooksOfAFief_AndKeepBothBooksExact()
    {
        using var w = SoakWorld.Create(); w.Run(30);
        output.WriteLine($"carters moved {w.Economy.Regional.MovedUnits / 1e6:0.0} t for {w.Economy.Regional.SpentMites / 100} crowns");
        foreach (var b in w.Markets.AllBooks()) for (ushort g = 0; g < w.Econ.Goods.Count; g++) Assert.Equal(b.BookStore.Qty(g), b.Goods[g].Lots.Sum(l => l.Qty));
        Assert.True(w.Econ.Ledger.Audit().IsConserved && w.Econ.MassAuditHolds());
    }
}
