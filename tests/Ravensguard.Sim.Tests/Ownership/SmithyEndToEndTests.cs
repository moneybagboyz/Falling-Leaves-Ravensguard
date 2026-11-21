using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Ownership;

/// <summary>
/// OV-2 (overview 4, the Phase 5 done-when "buy a lot, build a two-storey smithy, hire a smith, supply iron and see profit while away"), end to end on a whole <see cref="WorldRuntime"/> of a new game,
/// every step a command through the hero's desk as the game sends them: buy a town plot, place <c>smithy_house</c> from the library on it and have a builder build it under contract, open a smithy
/// in its workshop, hire a smith at 400 a day, unload 200 kg of iron bars into its store, order nails, leave the region for two seasons; on return the Ledger's season rows show revenue, costs and
/// profit, the profit sits in the home chest, and the coin is exact to the mite.
/// </summary>
public class SmithyEndToEndTests(ITestOutputHelper output)
{
    private static (SeamWorld W, WorldState State) Game()
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

    /// <summary>The save, taken at a barrier (as the game takes it).</summary>
    private static SaveFile SaveAt(WorldRuntime rt) => rt.AtBarrier(() => WorldSave.Capture(rt.State, "h", "0.1")).GetAwaiter().GetResult();

    [Fact]
    public void OV2_Buy_a_lot_build_a_two_storey_smithy_hire_a_smith_supply_iron_and_see_profit_while_away()
    {
        var (w, s) = Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var ctx = new HeroContext(s, s.Party!, w.Content);
        var desk = new HeroDesk();
        var lw = s.HomeRecords!.Sim.Get<LandWorld>()!; var cw = s.HomeRecords.Sim.Get<ConstructionWorld>()!; var ow = s.HomeRecords.Sim.Get<OwnershipWorld>()!;
        var e = s.Economy!; var reg = s.Population.Region;
        e.Ledger.Mint(ctx.Purse, Coin.FromCrowns(3000), SourceReason.Seed);                       // the test's purse: the coin a long first year earns (the plot, the builder, the capital)
        long minted = e.Ledger.Audit().Seeded.Mites;
        HeroResult Do(HeroCommand c)
        {
            var cmd = desk.Issue(c with { IssuedSec = Math.Max(c.IssuedSec, rt.HourDone * 3600) });
            desk.ApplyDue(ctx, rt.HourDone + 1);
            return desk.View!.Results.Last(r => r.Seq == cmd.Seq);
        }
        long Sec() => (rt.HourDone + 1) * 3600;
        void RunDays(int days) => rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1 + days * 24L));

        // 1. buy a town plot (462 crowns for a burgage plot at the lot market)
        int town = reg.Settlements.FindIndex(x => x.Class == SettlementClass.Town && lw.Listings(reg.Settlements.IndexOf(x)).Any(l => l.Lot < 0));
        var listing = lw.Listings(town).First(l => l.Lot < 0);
        var buy = Do(LandDesk.Buy(listing, Sec()));
        Assert.True(buy.Ok, buy.Text);
        var deed = s.Land!.Deeds.Values.Single(d => d.Holder == ctx.Party.Hero && d.Kind == HoldingKind.Lot);
        int lotIndex = LandIds.SplitLot(deed.Subject).Lot;
        output.WriteLine($"bought the plot at {reg.Settlements[town].Name} for {Quotes.Money(buy.Mites)} ({listing.Rect.W} x {listing.Rect.H} m)");

        // 2. place the two-storey smithy from the library on it and have a builder build it under contract
        var plan = Do(ConstructionDesk.Plan("smithy_house", listing.Rect.X + 1, listing.Rect.Y + 1, 0, false, Sec()));
        Assert.True(plan.Ok, plan.Text);
        var site = s.Construction!.Sites.Values.Single(x => x.Owner == ctx.Party.Hero);
        Assert.Contains(cw.GhostsOf(site.Id), g => g.Z == 1);                                       // two storeys
        // the builder: the town's own builder under contract (the spec's way), commissioned through the desk at his bid. A new game's books are empty for its first days, so the town's
        // merchant holds the plan's goods and the hero brings the anvil's iron (the test's stock, as BC-16 stocks the builder's yard: Construction/ContractSmithyTests)
        Ravensguard.Sim.Tests.Construction.ContractSmithyTests.StockForContract(cw, site, town, Sec());
        var bid = cw.Bids(site.Id, 4).First(b => b.Builder.Settlement == town);
        var signed = Do(ConstructionDesk.Commission(site.Id, bid.Builder.Id, 4, Sec()));
        Assert.True(signed.Ok, signed.Text);
        var contract = s.Construction.Contracts.Values.Single(x => x.Kind == ContractKind.Commission);
        long ghosts = cw.GhostsOf(site.Id).Count();
        int days = 0, starved = 0;
        while (contract.State == ContractState.Signed && days < 60) { RunDays(1); days++; if (site.Pause == PauseReason.NoMaterials) starved++; }
        output.WriteLine($"{bid.Builder.Name} built the smithy for {Quotes.Money(contract.TotalMites)} in {days} days (bid {bid.ExpectedDaysX10 / 10.0}; {site.PiecesBuilt} pieces of {ghosts}), the hero has {Quotes.Money(ctx.Coin)}");
        Assert.Equal(ContractState.Done, contract.State);
        Assert.Equal(SiteState.Finished, site.State);
        // days the site stood waiting for goods the town's market did not have are the market's, not the builder's (ContractSmithyTests: after R1 cheaper timber lets more cottages buy the thatch)
        Assert.InRange(days - starved, bid.ExpectedDaysX10 / 10, bid.ExpectedDaysX10 / 10 * 5 / 4 + 1);

        // 3. open a smithy in its workshop
        int structure = 0;
        for (int y = listing.Rect.Y; y < listing.Rect.Bottom && structure == 0; y++)
            for (int x = listing.Rect.X; x < listing.Rect.Right && structure == 0; x++) structure = cw.Rooms.StructureAt(x, y, 0);
        Assert.True(structure > 0);
        var stations = ow.StationsOf(structure);
        output.WriteLine($"stations in its rooms: {string.Join(", ", stations.Select(kv => $"{kv.Key} x{kv.Value}"))}");
        var found = Do(BusinessDesk.Found("smithy", town, lotIndex, structure, 15_000, Sec()));
        Assert.True(found.Ok, found.Text);
        var biz = s.Production!.Businesses.Single(b => b.Owner == s.Hero!.PersonIndex);

        // 4. hire a smith at 400 a day (the most skilled of the town who would come)
        var pt = s.Population.People;
        int smith = Enumerable.Range(0, pt.Count).Where(i => pt.Status[i] == PersonStatus.Alive && pt.HomeSettlement[i] == town && pt.AgeYears(i, (int)(Sec() / 86400)) is >= 18 and < 55
                && !s.Production.Businesses.Any(b => b.Owner == i) && pt.Occupation[i] is not (Occupation.Lord or Occupation.Priest or Occupation.Mayor or Occupation.GuildMaster) && ow.CurrentWage(i, out _) < 400)
            .OrderByDescending(i => pt.SkillLevelOf(i, Skill.Smithing)).ThenBy(i => i).First();
        var hire = Do(BusinessDesk.Hire(biz.Id, smith, 400, Sec()));
        Assert.True(hire.Ok, hire.Text);
        output.WriteLine($"hired {pt.FullName(smith)} (Smithing {pt.SkillLevelOf(smith, Skill.Smithing)}) at 4 cr a day: {hire.Text}");

        // 5. unload 200 kg of iron bars into its store and order nails
        ctx.Pack.Create(e.Goods.IdOf("iron_bar"), 200_000, CreateReason.Import);                     // the bars the hero brought (the test's faucet, as a cart's load would be)
        var sup = Do(BusinessDesk.Supply(biz.Id, "iron_bar", 200_000, Sec()));
        Assert.True(sup.Ok, sup.Text);
        Assert.Equal(200_000, biz.Store.Qty(e.Goods.IdOf("iron_bar")));
        Assert.True(Do(BusinessDesk.Orders(biz.Id, "recipe=forge_nails", Sec())).Ok);

        // 6. leave the region for two seasons
        Assert.True(Do(BusinessDesk.At(-2, Sec())).Ok);
        long chest0 = ow.ChestCoin;
        var h = ow.Book.Of(biz.Id)!; long sent0 = h.SentToChest;
        int guard = 0;
        while (h.History.Count < 2 && guard++ < 50) RunDays(1);
        RunDays(1);
        Assert.True(h.History.Count >= 2, $"{h.History.Count} seasons closed");

        // 7. back: the Ledger's season rows show revenue, costs and profit; the profit sits in the home chest; coin exact
        var ledger = ow.Ledger((int)(Sec() / 86400), cw);
        var row = ledger.Tab(LedgerTab.Businesses).Single();
        foreach (var r in h.History) output.WriteLine($"season {r.Season}: revenue {Quotes.Money(r.Revenue)}, wages {Quotes.Money(r.Wages)}, inputs {Quotes.Money(r.Inputs)}, profit {Quotes.Money(r.Profit)}, paid out {Quotes.Money(r.Paid)}");
        output.WriteLine($"ledger row: {string.Join(" | ", row.Cells)}; chest {Quotes.Money(ow.ChestCoin)} (was {Quotes.Money(chest0)}), box {Quotes.Money(e.Ledger.Balance(biz.Purse).Mites)}, batches {biz.Batches}, nails sold so far, iron left {biz.Store.Qty(e.Goods.IdOf("iron_bar")) / 1000} kg");
        Assert.True(biz.Batches > 0);
        Assert.Contains(h.History, r => r.Revenue > 0);
        Assert.True(h.History.Sum(r => r.Profit) > 0, "profit over the two seasons");
        Assert.True(h.History.All(r => r.Wages > 0));
        Assert.True(ow.ChestCoin > chest0, "the dividend went to the home chest while he was away");
        Assert.Equal(chest0 + h.SentToChest - sent0, ow.ChestCoin);                                  // every mite in the chest is the business's dividend, the Ledger says where
        Assert.Contains(smith, biz.Staff);
        var audit = e.Ledger.Audit();
        Assert.True(audit.IsConserved);
        Assert.Equal(minted, audit.Seeded.Mites);
        Assert.Equal(0, audit.Minted.Mites); Assert.Equal(0, audit.Melted.Mites + audit.Burned.Mites);
        Assert.True(e.MassAuditHolds());
        // and the whole of it saves and loads to the same bytes
        var file = SaveAt(rt);
        Assert.Contains(OwnershipSave.SectionName, file.Sections.Keys);
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), w.Content, "h", null, w.Continent, w.History);
        Assert.Equal(s.Ownership!.ComputeHash(), back.Ownership!.ComputeHash());
    }
}
