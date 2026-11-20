using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.People;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Construction;

/// <summary>P5-D: building contracts (spec 4.7): BC-16 (a builder hired for the wattle cottage), the stages and their escrow, a missed stage, posted civic work the hero takes by hand.</summary>
public class ContractTests(ITestOutputHelper output)
{
    /// <summary>Day 10 of the population's own year at 06:00 (people's ages are counted from the calendar).</summary>
    private static long T0 => (long)Ravensguard.Sim.Tests.Economy.EconFixture.Pop.NowYear * TimeConst.DaysPerYear * 86400 + 10 * 86400L + 6 * 3600;

    /// <summary>The hero's own town lot with <paramref name="plan"/> placed on it (an empty pile).</summary>
    private static EntityId Lot(ContractRig r, string plan = "test_cottage")
    {
        int town = r.L.Town();
        var lot = r.L.L.Listings(town).First(x => x.Lot < 0);
        Assert.True(r.L.L.Buy(r.Hero, PurseKind.Person, town, lot.Lot, lot.Rect, 0, 10, r.L.C).Ok);
        var pr = r.W.PlacePlan(r.Hero, PurseKind.Person, -1, plan, lot.Rect.X + 1, lot.Rect.Y + 1, 0, false, T0);
        Assert.True(pr.Ok, pr.Key + string.Join(",", pr.Refused.Select(x => x.Key)));
        return pr.Site;
    }

    private static double WorkingDays(long from, long to, int dayStartH = 6, int dayH = 10)
    {
        long d = (to - from) / 86400, rem = (to - from) % 86400;
        return d + Math.Min(rem, dayH * 3600L) / (dayH * 3600.0) + (from % 86400 - dayStartH * 3600L) / (dayH * 3600.0) * 0;
    }

    [Fact]
    public void BC16_ACarpentryBusinessBuildsTheCottage_AtItsBid_InAboutSevenDays()
    {
        using var r = ContractRig.Make(crowns: 2000);
        var site = Lot(r);
        var s = r.W.Site(site)!;
        var bids = r.W.Bids(site, 4);
        Assert.NotEmpty(bids);
        var bid = bids.FirstOrDefault(b => b.Builder.Name.Contains("carpentry")) ?? bids[0];
        foreach (var b in bids.Take(4)) output.WriteLine($"bid {b.Builder.Name}: materials {b.MaterialsMites}, labour {b.LabourMites}, haul {b.HaulMites} = {b.TotalMites}; {b.MilliHours / 1000.0} h, crew {b.Crew}, {b.ExpectedDaysX10 / 10.0} days");
        // the bid: materials at the local ask + 250.5 h x 350 x 1.45 / 10 + the haul
        Assert.Equal(250_500, bid.MilliHours);
        Assert.Equal(12_712, bid.LabourMites);
        var book = r.W.Market!(bid.Builder.Settlement)!;
        foreach (var (g, q) in r.W.Needed(s)) output.WriteLine($"  {r.L.W.Econ.Goods.StringId(g)} {q} g: ask {book.AskMicro(g)} micro, ref {book.Goods[g].RefMicro}, stock {book.Goods[g].StockUnits}");
        long mats = r.W.Needed(s).Sum(kv => ConstructionWorld.AskFor(book, kv.Key, kv.Value));
        Assert.Equal(mats, bid.MaterialsMites);
        Assert.True(bid.HaulMites > 0);
        // the builder's yard holds the goods a fresh world's books have none of yet (the test's stock): thatch, planks, nails; poles and clay he gets from the woodland and the pits
        var yard = bid.Builder.Store!;
        foreach (var (g, kg) in new[] { ("thatch", 3000L), ("timber_planks", 30L), ("nails", 1L) }) yard.Create(r.L.W.Econ.Goods.IdOf(g), kg * 1000, Ravensguard.Sim.Goods.CreateReason.Import);
        long coin0 = r.Led.Balance(r.L.HeroPurse).Mites;
        long t = T0;
        var res = r.W.Commission(r.Hero, PurseKind.Person, site, bid.Builder.Id, 4, t);
        Assert.True(res.Ok, res.Key);
        var c = r.W.Book.Contracts.Values.Single();
        Assert.Equal(c.TotalMites / 3 - Math.Min(c.TotalMites / 3, c.MaterialsMites + c.HaulMites), r.Led.Balance(r.Led.Open(PurseKind.Chest, c.Escrow)).Mites);   // a third into escrow at signing, the materials advanced
        Assert.Equal(coin0 - c.TotalMites / 3, r.Led.Balance(r.L.HeroPurse).Mites);
        output.WriteLine($"crew {s.Crew.Count}: {string.Join(", ", s.Crew.Select(m => m.Class + " " + m.PersonIndex))}");
        Assert.Equal(4, s.Crew.Count);
        // dry weather (no weather hook): run until handed over
        for (int d = 1; d <= 14; d++)
        {
            r.Run(ref t, T0 + d * 86400L);
            output.WriteLine($"day {d}: pieces {s.PiecesBuilt}, pile {r.W.PileOf(s).TotalMassG() / 1000} kg, yard {yard.TotalMassG() / 1000} kg, jobs {r.Jobs.Jobs.Values.Count(j => j.Live)} ({r.Jobs.Jobs.Values.Count(j => j.Live && j.State != Ravensguard.Sim.Jobs.JobState.Open)} taken), needed {r.W.Needed(s).Values.Sum() / 1000} kg, {s.Pause}");
        }
        long last = r.W.Book.Built.Values.Where(b => b.Site == site).Max(b => b.BuiltSec);
        double days = WorkingDays(T0, last);
        output.WriteLine($"contract {c.State}: finished at day {last / 86400.0:F2}, {days:F2} working days; paid out {c.ReleasedMites} of {c.TotalMites}; stages {c.Stages}; pieces {s.PiecesBuilt}");
        var goods = r.L.W.Econ.Goods; var bstore = bid.Builder.Store!;
        foreach (var (g, q) in r.W.Needed(s)) output.WriteLine($"  still needed {goods.StringId(g)} {q} g; pile {r.W.PileOf(s).Qty(g)}, builder {bstore.Qty(g)}, book {book.Goods[g].StockUnits}");
        output.WriteLine($"  site {s.State}/{s.Pause}; jobs open {r.Jobs.Jobs.Values.Count(j => j.Live)}");
        Assert.Equal(ContractState.Done, c.State);
        Assert.Equal(80, s.PiecesBuilt);
        Assert.InRange(days, 6.0, 8.0);
        Assert.True(days >= 250.5 / 40, "never sooner than the piece-hours at the crew's speed");
        Assert.Equal(c.TotalMites, c.ReleasedMites);
        Assert.Equal(coin0 - c.TotalMites, r.Led.Balance(r.L.HeroPurse).Mites);
        Assert.Contains(r.L.Chron, x => x.TypeId == "contract_done");
        r.AssertCoin(); r.AssertMass();
    }

    [Fact]
    public void AMissedStage_StopsTheBuilder_WhoKeepsWhatWasPaid_AndTheHalfBuiltHouseStaysTheClients()
    {
        using var r = ContractRig.Make(crowns: 2000);
        var site = Lot(r);
        var s = r.W.Site(site)!;
        var bid = r.W.Bids(site, 4)[0];
        var yard = bid.Builder.Store!;
        foreach (var (g, kg) in new[] { ("thatch", 3000L), ("timber_planks", 30L), ("nails", 1L) }) yard.Create(r.L.W.Econ.Goods.IdOf(g), kg * 1000, Ravensguard.Sim.Goods.CreateReason.Import);
        long t = T0;
        Assert.True(r.W.Commission(r.Hero, PurseKind.Person, site, bid.Builder.Id, 4, t).Ok);
        var c = r.W.Book.Contracts.Values.Single();
        long builder0 = r.Led.Balance(r.Led.Open(PurseKind.Business, bid.Builder.Id)).Mites;
        r.L.Drain();                                                                   // the hero spends everything else: he cannot put up the second third
        r.Run(ref t, T0 + 10 * 86400L);
        Assert.Equal(ContractState.Stopped, c.State);
        Assert.Empty(s.Crew);                                                           // the builder left with his crew
        Assert.Equal(r.Hero, s.Payer);                                                  // the site is the client's again
        Assert.Equal(c.TotalMites / 3, c.ReleasedMites);                              // he keeps the stage paid
        int built = s.PiecesBuilt;
        Assert.InRange(built, 10, 79);                                                  // the half-built house stays
        Assert.Equal(built, r.W.Book.Built.Values.Count(b => b.Site == site));
        Assert.Contains(r.L.Chron, x => x.TypeId == "contract_stopped");
        output.WriteLine($"stopped with {built} pieces built; the builder was paid {c.ReleasedMites} (his purse {builder0} -> {r.Led.Balance(r.Led.Open(PurseKind.Business, bid.Builder.Id)).Mites})");
        r.AssertCoin(); r.AssertMass();
    }

    [Fact]
    public void AContract_AndAGrowthSite_SaveAndLoad_ToTheSameBytes()
    {
        using var r = ContractRig.Make(crowns: 2000);
        var site = Lot(r);
        var bid = r.W.Bids(site, 4)[0];
        long t = T0;
        Assert.True(r.W.Commission(r.Hero, PurseKind.Person, site, bid.Builder.Id, 4, t).Ok);
        r.Run(ref t, T0 + 2 * 86400L + 3 * 3600);
        r.W.Book.Growth[77] = new GrowthLink { Serial = 77, Kind = GrowthSites.KindAssart, Settlement = 1, X = 64, Y = 128, W = 128, H = 64, DoneMilli = 12_000, NeedMilli = 720_000, LinkedSec = t };
        r.W.Site(site)!.Crew[^1].DayCapSec = 4 * 3600;
        var bytes = Ravensguard.Sim.Save.SaveCodec.SerializeCompressed(Ravensguard.Sim.Save.ConstructionSave.Capture(r.W.Book));
        var back = Ravensguard.Sim.Save.SaveCodec.DeserializeCompressed<Ravensguard.Sim.Save.ConstructionSave>(bytes).Restore();
        Assert.Equal(r.W.Book.ComputeHash(), back.ComputeHash());
        var again = Ravensguard.Sim.Save.SaveCodec.SerializeCompressed(Ravensguard.Sim.Save.ConstructionSave.Capture(back));
        Assert.Equal(bytes, again);
        Assert.Single(back.Contracts); Assert.Single(back.Growth);
        Assert.Equal(r.W.Book.Contracts.Values.Single().ReleasedMites, back.Contracts.Values.Single().ReleasedMites);
    }
}
