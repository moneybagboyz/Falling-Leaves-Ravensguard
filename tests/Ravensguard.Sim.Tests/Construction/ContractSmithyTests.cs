using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Construction;

/// <summary>
/// P5-I (phase-5-open, found by OV-2): a builder commissioned for the two-storey <c>smithy_house</c> on the hero's bought lot in a new game took the escrow and stalled after a few of its
/// 148 pieces. He now buys in the crew's build order a couple of days' work at a time, from the contract's materials chest the signing stage filled, and finishes in the bid's days.
/// </summary>
public class ContractSmithyTests(ITestOutputHelper output)
{
    internal static (SeamWorld W, WorldState State) Game()
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var h = HeroRecord.FromOrigin("Aldric Vane", false, w.Content.All<OriginDef>().OrderBy(o => o.Order).First());
        return (w, NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, h, null));
    }

    /// <summary>
    /// The test's stock for a contract in a new game (as BC-16 stocks the builder's yard): a new game's market books are empty for their first days and the town takes in a few tonnes of
    /// rough stone a day, so the town's first merchant lists twice what the site needs at the lowest floor; the iron for the anvil and the forge the hero brings to the pile himself
    /// (iron bars trade at three to four times their reference price in this region's first two months, above any builder's limit), so the bids leave it out.
    /// </summary>
    internal static void StockForContract(ConstructionWorld cw, ConstructionSite site, int settlement, long sec)
    {
        var e = cw.Econ; ushort iron = e.Goods.IdOf("iron_bar");
        cw.PileOf(site).Create(iron, cw.Needed(site).GetValueOrDefault(iron), CreateReason.Import);
        var book = cw.Market!(settlement)!;
        var m = book.Merchants.OrderBy(x => x.Id).First();
        int day = (int)(sec / 86400);
        foreach (var (g, q) in cw.Needed(site))
        {
            long miss = 2 * (q - cw.PileOf(site).Qty(g));
            if (miss <= 0) continue;
            m.Store.Create(g, miss, CreateReason.Import);
            book.List(m, g, miss, 300, day, merchantLot: true);
        }
        cw.Refresh(site);
    }

    [Fact]
    public void A_builder_commissioned_for_the_smithy_on_a_bought_lot_in_a_new_game_finishes_it_in_the_bids_days()
    {
        var (w, s) = Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var ctx = new HeroContext(s, s.Party!, w.Content);
        var desk = new HeroDesk();
        var lw = s.HomeRecords!.Sim.Get<LandWorld>()!; var cw = s.HomeRecords.Sim.Get<ConstructionWorld>()!;
        var e = s.Economy!; var reg = s.Population.Region;
        e.Ledger.Mint(ctx.Purse, Coin.FromCrowns(4000), SourceReason.Seed);
        long minted = e.Ledger.Audit().Seeded.Mites;
        HeroResult Do(HeroCommand c)
        {
            var cmd = desk.Issue(c with { IssuedSec = Math.Max(c.IssuedSec, rt.HourDone * 3600) });
            desk.ApplyDue(ctx, rt.HourDone + 1);
            return desk.View!.Results.Last(r => r.Seq == cmd.Seq);
        }
        long Sec() => (rt.HourDone + 1) * 3600;

        // OV-2's first steps: a town plot bought at the lot market, smithy_house placed on it from the library
        int town = reg.Settlements.FindIndex(x => x.Class == SettlementClass.Town && lw.Listings(reg.Settlements.IndexOf(x)).Any(l => l.Lot < 0));
        var listing = lw.Listings(town).First(l => l.Lot < 0);
        Assert.True(Do(LandDesk.Buy(listing, Sec())).Ok);
        var plan = Do(ConstructionDesk.Plan("smithy_house", listing.Rect.X + 1, listing.Rect.Y + 1, 0, false, Sec()));
        Assert.True(plan.Ok, plan.Text);
        var site = s.Construction!.Sites.Values.Single(x => x.Owner == ctx.Party.Hero);
        long ghosts = cw.GhostsOf(site.Id).Count();
        Assert.Contains(cw.GhostsOf(site.Id), g => g.Z == 1);
        StockForContract(cw, site, town, Sec());

        // the town's own builder (a carpentry or masonry business of the town) at his bid, crew 4
        var bid = cw.Bids(site.Id, 4).First(b => b.Builder.Settlement == town);
        output.WriteLine($"bid {bid.Builder.Name}: materials {bid.MaterialsMites}, labour {bid.LabourMites}, haul {bid.HaulMites} = {bid.TotalMites}; {bid.MilliHours / 1000.0} h, crew {bid.Crew}, {bid.ExpectedDaysX10 / 10.0} days");
        long coin0 = ctx.Coin;
        long t0 = Sec();
        var res = Do(ConstructionDesk.Commission(site.Id, bid.Builder.Id, 4, t0));
        Assert.True(res.Ok, res.Text);
        var c = s.Construction.Contracts.Values.Single(x => x.Kind == ContractKind.Commission);
        Assert.Equal(coin0 - c.TotalMites / 3, ctx.Coin);                                          // a third at signing
        var chest = e.Ledger.Open(PurseKind.Chest, c.MaterialsChest);
        Assert.Equal(Math.Min(c.TotalMites / 3, c.MaterialsMites + c.HaulMites), c.ReleasedMites);  // advanced into the contract's materials chest, not his cash box
        Assert.InRange(e.Ledger.Balance(chest).Mites, 0, c.ReleasedMites);                          // (he bought what the book held at once)

        int days = 0;
        while (c.State == ContractState.Signed && days < 40)
        {
            rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1 + 24));
            days++;
            output.WriteLine($"day {days}: pieces {site.PiecesBuilt}/{ghosts}, {site.State}/{site.Pause}, crew {site.Crew.Count}, chest {e.Ledger.Balance(chest).Mites}, builder {e.Ledger.Balance(bid.Builder.Party!.Purse).Mites}, stages {c.Stages}, pile {cw.PileOf(site).TotalMassG() / 1000} kg");
            if (site.Pause == PauseReason.NoMaterials) output.WriteLine("   short: " + string.Join(", ", cw.Needed(site).Where(kv => cw.PileOf(site).Qty(kv.Key) < kv.Value).Select(kv => $"{e.Goods.StringId(kv.Key)} {kv.Value - cw.PileOf(site).Qty(kv.Key)} (book {cw.Market!(town)!.Goods[kv.Key].StockUnits}, yard {bid.Builder.Store!.Qty(kv.Key)})")) + " in " + string.Join(",", cw.GhostsOf(site.Id).Where(g => g.Block == GhostBlock.Materials).Select(g => g.Piece)));
        }
        Assert.Equal(ContractState.Done, c.State);
        Assert.Equal(SiteState.Finished, site.State);
        Assert.Equal(ghosts, site.PiecesBuilt);
        long last = cw.Book.Built.Values.Where(b => b.Site == site.Id).Max(b => b.BuiltSec);
        double took = (last - t0) / 86400.0;
        output.WriteLine($"handed over after {took:F1} days (bid {bid.ExpectedDaysX10 / 10.0}); paid {c.ReleasedMites} of {c.TotalMites}");
        // the bid's days (at 90% of the working day) +-10%, the 0x511 pace: never sooner than the piece-hours at the crew's full day, never later than at 80% of it, and a day for
        // the haul (the cart's first loads); BC-16's rule for the cottage. 23.9 days bid, 26.6 taken (the hired masons and the builder's own work at their own skills)
        double fullDay = bid.MilliHours / 1000.0 / (bid.Crew * 10.0);
        Assert.InRange(took, fullDay, fullDay / 0.8 + 1);
        Assert.InRange(bid.ExpectedDaysX10 / 10.0, fullDay, fullDay / 0.8);
        Assert.Equal(c.TotalMites, c.ReleasedMites);
        Assert.Equal(3, c.Stages);                                                                  // the client put up every stage: a third (here the materials), to two thirds, the rest
        Assert.Equal(0, e.Ledger.Balance(chest).Mites);                                             // what the materials left over is the builder's
        var audit = e.Ledger.Audit();
        Assert.True(audit.IsConserved);
        Assert.Equal(minted, audit.Seeded.Mites);
        Assert.True(e.MassAuditHolds());
    }
}
