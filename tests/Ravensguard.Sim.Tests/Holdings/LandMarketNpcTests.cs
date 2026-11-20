using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Land;
using Ravensguard.Sim.Tests.Production;
using Ravensguard.Sim.Tests.RegionsB;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Holdings;

/// <summary>
/// P6-D4 (docs/specs/phase6/holdings-and-works-v2.md 3.3 and 3.4): HW-3 the NPC land market (households with a waiting daughter and twice the price buy the lord's plots and build on them, a seller in
/// arrears sells an empty house at 90%, exact ties by stream 0x500, the hero's offer by the trust formula, the scarcity term in every price) and HW-4 zoning and new streets (a place with no free plot and more
/// than three women waiting gets a street of 6 to 10 plots near 450 crowns each; visited chunks get ID-keyed deltas; the street is replayed on load). On the land rig: seed 1234's region and people, the economy, no ticker.
/// </summary>
public class LandMarketNpcTests(ITestOutputHelper output)
{
    private static LandChronicleKeyed Keyed(LandRig rig) => (when, type, key, ps, subjects, causes, data) =>
    {
        var id = rig.W.Sim.Ids.Allocate(IdKind.Event);
        rig.Chron.Add(new ChronicleEntry(id, when, type, subjects, causes, data.ToDictionary(x => x.Key, x => x.Value), TextKey: key, Params: ps));
        return id;
    };

    private static LandRig Rig()
    {
        var rig = LandRig.Make(crowns: 100);
        rig.L.Seed = 1234;
        var g = rig.W.Economy.Growth;
        rig.L.StartBuild = (s, lot, hh, day) => g.StartOn(s, lot, hh, GameTime.FromGameHours(day * 24L));
        return rig;
    }

    /// <summary>A household of place <paramref name="s"/> (not the lord's, not gentry) with a woman who waits to marry, given <paramref name="coin"/> mites more than it has (so the test knows it is the richest).</summary>
    private static int Buyer(LandRig rig, int s, long coin, int skip = 0)
    {
        var hht = rig.W.Pop.Households; var pt = rig.W.Pop.People; int lord = LandRegistry.LordHousehold(rig.W.Pop, s);
        int hh = Enumerable.Range(0, hht.Count).Where(h => hht.Settlement[h] == s && h != lord && hht.MemberCount[h] > 0 && hht.Class[h] != SocialClass.Gentry
            && hht.Members(h).Any(m => pt.Status[m] == PersonStatus.Alive && pt.Sex[m] == Sex.Female && pt.Spouse[m] < 0 && pt.AgeYears(m, rig.Day) is >= 16 and < 35)).Skip(skip).First();
        int girl = hht.Members(hh).First(m => pt.Status[m] == PersonStatus.Alive && pt.Sex[m] == Sex.Female && pt.Spouse[m] < 0 && pt.AgeYears(m, rig.Day) is >= 16 and < 35);
        pt.MarriageWait[girl] = 1;
        if (coin > 0) rig.Led.Mint(rig.W.Econ.HouseholdPurse[hh], new Coin(coin), SourceReason.Seed);
        return hh;
    }

    private static int SeasonDay(LandRig rig) => LandWorld.NextSeasonStart(rig.Day);

    // ------------------------------------------------------------------ HW-3

    [Fact]
    public void HW3_AHouseholdWithAWaitingDaughterAndTwiceThePriceBuysTheLordsPlotAtTheListedPrice_AndBuildsOnIt()
    {
        using var rig = Rig();
        int v = rig.Village(); var hht = rig.W.Pop.Households;
        var list = rig.L.Listings(v); var l = list[0];
        int hh = Buyer(rig, v, l.PriceMites * 5);
        var (lordId, lordKind) = LandRegistry.LordOf(rig.W.Pop, v);
        long lord0 = rig.Bal(lordId, lordKind), buyer0 = rig.Bal(hht.IdOf(hh), PurseKind.Household);
        var sales = LandMarketNpc.Seasonal(rig.L, SeasonDay(rig), rig.C, Keyed(rig));
        var sale = Assert.Single(sales, x => x.Settlement == v);
        Assert.Equal(hht.IdOf(hh), sale.Buyer); Assert.False(sale.Distress); Assert.Equal(l.PriceMites, sale.Price);       // the price the lord listed, scarcity term included
        Assert.Equal(lord0 + l.PriceMites, rig.Bal(lordId, lordKind));
        Assert.Equal(buyer0 - l.PriceMites, rig.Bal(hht.IdOf(hh), PurseKind.Household));
        var deed = rig.L.Book.Deeds.Values.Single(d => d.Holder == hht.IdOf(hh));
        Assert.Equal(DeedCause.Bought, deed.Cause); Assert.Equal(PurseKind.Household, deed.HolderPurse);
        Assert.Contains(rig.Chron, e => e.TypeId == "land_bought");
        // the buyer puts a house up on it: growth's cottage project on that lot, paid by him
        var proj = Assert.Single(rig.W.Economy.Growth.State.Projects, p => p.Payer == hh);
        Assert.Equal(LandIds.SplitLot(deed.Subject).Lot, proj.Lot); Assert.Equal(ProjectKind.Cottage, proj.Kind);
        Assert.True(rig.Led.Audit().IsConserved);
        output.WriteLine($"{hht.Name[hh]} bought a plot of {rig.Region.Settlements[v].Name} for {l.PriceMites}");
    }

    [Fact]
    public void HW3_ACandidateNeedsTwiceThePriceInCoin_AndTheScarcityTermMovesTheListedPriceTheHouseholdsPay()
    {
        using var rig = Rig();
        int v = rig.Village(); var l = rig.L.Listings(v)[0]; var hht = rig.W.Pop.Households; var e = rig.W.Econ;
        int hh = Buyer(rig, v, 0); int lord = LandRegistry.LordHousehold(rig.W.Pop, v);
        const long C = 10_000_000;
        rig.Led.Mint(e.HouseholdPurse[hh], new Coin(C - rig.Bal(hht.IdOf(hh), PurseKind.Household)), SourceReason.Seed);
        Assert.Null(LandMarketNpc.Buyer(rig.L, v, lord, C / 2 + 1, 5, 1, []));                          // 2 x the price is one mite more than he has
        Assert.Equal(hh, LandMarketNpc.Buyer(rig.L, v, lord, C / 2, 5, 1, []));                          // exactly twice: he buys
        e.LandMultPm = 500;                                                                                 // the C4 term: a third of the holdings empty, land at about half
        var half = rig.L.Listings(v)[0];
        Assert.Equal(l.PriceMites * 500 / 1000, half.PriceMites);
        var sale = Assert.Single(LandMarketNpc.Seasonal(rig.L, SeasonDay(rig), rig.C, Keyed(rig)), x => x.Settlement == v);
        Assert.Equal(half.PriceMites, sale.Price); Assert.Equal(hht.IdOf(hh), sale.Buyer);
        Assert.True(rig.Led.Audit().IsConserved);
    }

    [Fact]
    public void HW3_ExactTiesInCoinAreDrawnFromStream0x500_KeyedOnThePlotAndTheSeason_AndTheRicherAlwaysWins()
    {
        using var rig = Rig();
        int v = rig.Village(); var l = rig.L.Listings(v)[0]; var e = rig.W.Econ;
        int a = Buyer(rig, v, 0), b = Buyer(rig, v, 0, skip: 1);
        long top = 1_000_000_000;
        rig.Led.Mint(e.HouseholdPurse[a], new Coin(top - rig.Bal(rig.W.Pop.Households.IdOf(a), PurseKind.Household)), SourceReason.Seed);
        rig.Led.Mint(e.HouseholdPurse[b], new Coin(top - rig.Bal(rig.W.Pop.Households.IdOf(b), PurseKind.Household)), SourceReason.Seed);
        int lord = LandRegistry.LordHousehold(rig.W.Pop, v);
        // the same coin: the winner follows the draw, for a plot and a season; the same on every run; over many seasons each of the two wins
        var winners = Enumerable.Range(0, 40).Select(season => LandMarketNpc.Buyer(rig.L, v, lord, l.PriceMites, 99, season, [])!.Value).ToArray();
        Assert.Equal(winners, Enumerable.Range(0, 40).Select(season => LandMarketNpc.Buyer(rig.L, v, lord, l.PriceMites, 99, season, [])!.Value).ToArray());
        Assert.True(winners.Count(w => w == a) >= 8 && winners.Count(w => w == b) >= 8, $"a won {winners.Count(w => w == a)}, b won {winners.Count(w => w == b)} of 40");
        Assert.All(winners, w => Assert.Contains(w, new[] { a, b }));
        var other = Enumerable.Range(0, 40).Select(season => LandMarketNpc.Buyer(rig.L, v, lord, l.PriceMites, 100, season, [])!.Value).ToArray();
        Assert.NotEqual(winners, other);                                                                  // another plot, another draw
        // one mite more and no draw is needed: the richer wins every season
        rig.Led.Mint(e.HouseholdPurse[b], new Coin(1), SourceReason.Seed);
        Assert.All(Enumerable.Range(0, 40), season => Assert.Equal(b, LandMarketNpc.Buyer(rig.L, v, lord, l.PriceMites, 99, season, [])));
    }

    [Fact]
    public void HW3_AHouseholdInArrearsWithAnEmptyHouseSellsAt90Percent_TheLordIsPaidWhatIsOwed_AndTheBuyerBuilds()
    {
        using var rig = Rig();
        int v = rig.Village(); var hht = rig.W.Pop.Households; var l = rig.L.Listings(v)[0];
        int seller = Buyer(rig, v, l.PriceMites * 3 / 2), buyer = Buyer(rig, v, 0, skip: 1);
        // the seller bought a plot long ago, never built, and owes the lord two seasons' rent
        var bought = rig.L.Buy(hht.IdOf(seller), PurseKind.Household, v, l.Lot, l.Rect, 0, rig.Day, rig.C);
        Assert.True(bought.Ok, bought.Key);
        var d = rig.L.Book.Deeds[bought.Deed]; d.OwedMites = 600; d.ArrearsSeasons = 2;
        long left = rig.Bal(hht.IdOf(seller), PurseKind.Household);                                       // what he has left goes to a chest, so he is not a buyer himself
        if (left > 0) rig.Led.Transfer(rig.W.Econ.HouseholdPurse[seller], rig.Led.Open(PurseKind.Chest, EntityId.Make(IdKind.Other, 999_998)), new Coin(left), TransferReason.Gift);
        long value = rig.L.ValueOf(d), price = value * 900 / 1000;
        rig.Led.Mint(rig.W.Econ.HouseholdPurse[buyer], new Coin(price * 10), SourceReason.Seed);
        var (lordId, lordKind) = LandRegistry.LordOf(rig.W.Pop, v);
        var (payee, payeeKind) = rig.L.Payee(d);
        long s0 = rig.Bal(hht.IdOf(seller), PurseKind.Household), p0 = rig.Bal(payee, payeeKind), b0 = rig.Bal(hht.IdOf(buyer), PurseKind.Household);
        var sales = LandMarketNpc.Seasonal(rig.L, SeasonDay(rig), rig.C, Keyed(rig));
        var sale = Assert.Single(sales, x => x.Distress && x.Settlement == v);
        Assert.Equal(price, sale.Price); Assert.Equal(hht.IdOf(buyer), sale.Buyer);
        Assert.Equal(hht.IdOf(buyer), rig.L.Book.Deeds[d.Id].Holder); Assert.Equal(0, rig.L.Book.Deeds[d.Id].OwedMites);
        Assert.Equal(b0 - price, rig.Bal(hht.IdOf(buyer), PurseKind.Household));
        Assert.Equal(s0 + price - 600, rig.Bal(hht.IdOf(seller), PurseKind.Household));                  // what was owed comes off the price
        Assert.Equal(p0 + 600, rig.Bal(payee, payeeKind));
        Assert.True(900 * value / 1000 == price);
        Assert.Contains(rig.Chron, e => e.TypeId == "land_distress_sale" && e.TextKey == "ch_land_distress_sale" && e.Causes.Any(c => c.NoteKey == "ch_note_distress_arrears"));
        Assert.Contains(rig.W.Economy.Growth.State.Projects, p => p.Payer == buyer && p.Lot == LandIds.SplitLot(d.Subject).Lot);
        Assert.True(rig.Led.Audit().IsConserved);
        _ = lordId; _ = lordKind;
    }

    [Fact]
    public void HW3_TheHerosOfferForAnEmptyHouseIsTakenAtOrAbovePriceTimes1100LessTrustOverTen_AndRefusedBelowOrWhenTheHolderLivesThere()
    {
        using var rig = Rig();
        int v = rig.Village(); var hht = rig.W.Pop.Households; var l = rig.L.Listings(v)[0];
        int holder = Buyer(rig, v, l.PriceMites * 3);
        var bought = rig.L.Buy(hht.IdOf(holder), PurseKind.Household, v, l.Lot, l.Rect, 0, rig.Day, rig.C);
        var d = rig.L.Book.Deeds[bought.Deed]; long value = rig.L.ValueOf(d);
        rig.L.TrustOfHero = _ => 0;
        Assert.Equal(value * 1100 / 1000, rig.L.OfferFloor(d, rig.Hero));                               // a stranger pays 10% over
        rig.L.TrustOfHero = _ => 500; Assert.Equal(value * 1050 / 1000, rig.L.OfferFloor(d, rig.Hero));
        rig.L.TrustOfHero = _ => 1000; Assert.Equal(value, rig.L.OfferFloor(d, rig.Hero));              // a friend, the price itself
        rig.L.TrustOfHero = _ => 500; long floor = rig.L.OfferFloor(d, rig.Hero);
        rig.Led.Mint(rig.HeroPurse, new Coin(floor * 2), SourceReason.Seed);
        var low = rig.L.BidForHouse(rig.Hero, PurseKind.Person, d.Id, floor - 1, rig.Day, rig.C);
        Assert.False(low.Ok); Assert.Equal("ld_offer_low", low.Key);
        long h0 = rig.Coin, s0 = rig.Bal(hht.IdOf(holder), PurseKind.Household);
        var ok = rig.L.BidForHouse(rig.Hero, PurseKind.Person, d.Id, floor, rig.Day, rig.C);
        Assert.True(ok.Ok, ok.Key); Assert.Equal("ld_offer_taken", ok.Key);
        Assert.Equal(rig.Hero, rig.L.Book.Deeds[d.Id].Holder); Assert.Equal(h0 - floor, rig.Coin); Assert.Equal(s0 + floor, rig.Bal(hht.IdOf(holder), PurseKind.Household));
        Assert.True(rig.Led.Audit().IsConserved);
        // the lord's plots are bought from his listings; a house somebody lives in is not sold over his head; a lease is not for sale
        var lordDeed = rig.L.Registry().Deeds.First(x => x.Kind == HoldingKind.Lot && x.Settlement == v && x.Holder == LandRegistry.LordOf(rig.W.Pop, v).Holder && x.Id != d.Id);
        Assert.Equal("ld_offer_use_listing", rig.L.BidForHouse(rig.Hero, PurseKind.Person, lordDeed.Id, 1, rig.Day, rig.C).Key);
        var lived = rig.L.Registry().Deeds.First(x => x.Kind == HoldingKind.Lot && x.HolderPurse == PurseKind.Household && x.Holder != rig.Hero && x.Holder != LandRegistry.LordOf(rig.W.Pop, x.Settlement).Holder && !x.IsLease && !rig.L.LotEmpty(x.Settlement, LandIds.SplitLot(x.Subject).Lot));
        Assert.Equal("ld_offer_lived_in", rig.L.BidForHouse(rig.Hero, PurseKind.Person, lived.Id, 10_000_000, rig.Day, rig.C).Key);
        Assert.Equal(Ravensguard.Sim.Trade.HeroCommandKind.Land, LandDesk.Bid(d.Id, 5, 0).Kind);
        Assert.StartsWith("ld:bid|", LandDesk.Bid(d.Id, 5, 0).Tag);
    }

    // ------------------------------------------------------------------ HW-4

    /// <summary>The first town with no vacant house, every plot it could list reserved by a cottage under way (the town is full), and <paramref name="waiting"/> women waiting for a home.</summary>
    private static int FullTown(LandRig rig, int waiting)
    {
        var r = rig.Region; var gs = rig.W.Economy.Growth.State;
        int s = Enumerable.Range(0, r.Settlements.Count).First(i => r.Settlements[i].Class == SettlementClass.Town && !r.Settlements[i].Plan.Lots.Any(x => x.Vacant));
        var reserved = new List<RectM>(); var t = rig.L.GrowthTuning;
        while (reserved.Count < 400 && RegionGrowth.FindPlot(r, s, t.PlotWidthTownM, t.PlotDepthTownM, t.PlanMarginM, reserved, out var rect, out _)) reserved.Add(rect);
        foreach (var rect in reserved) gs.Projects.Add(new BuildProject { Serial = gs.NextSerial++, Kind = ProjectKind.Cottage, Settlement = s, Lot = -1, Rect = rect, Payer = -1, Dwelling = DwellingType.Burgage });
        var pt = rig.W.Pop.People; var hht = rig.W.Pop.Households; int n = 0;
        for (int p = 0; p < pt.Count && n < waiting; p++)
            if (pt.Status[p] == PersonStatus.Alive && pt.Sex[p] == Sex.Female && pt.Spouse[p] < 0 && pt.Household[p] >= 0 && hht.Settlement[pt.Household[p]] == s) { pt.MarriageWait[p] = 1; n++; }
        Assert.Equal(waiting, n);
        return s;
    }

    [Fact]
    public void HW4_APlaceWithNoFreePlotAndMoreThanThreeWomenWaitingGetsANewStreetOf6To10PlotsNear450Crowns_WithinTwoYears()
    {
        using var rig = Rig(); var r = rig.Region;
        int s = FullTown(rig, 3);
        Assert.Equal(0, rig.L.FreeLots(s));
        // three waiting is not enough, and a place that still has plots lays nothing
        int day0 = rig.Day; var keyed = Keyed(rig); var gs = rig.W.Economy.Growth.State;
        Assert.Empty(Zoning.Seasonal(rig.L, day0, keyed));
        Assert.Empty(gs.Streets);
        var pt = rig.W.Pop.People; var hht = rig.W.Pop.Households;
        int fourth = Enumerable.Range(0, pt.Count).First(p => pt.Status[p] == PersonStatus.Alive && pt.Sex[p] == Sex.Female && pt.Spouse[p] < 0 && pt.MarriageWait[p] == 0 && pt.Household[p] >= 0 && hht.Settlement[pt.Household[p]] == s);
        pt.MarriageWait[fourth] = 1;
        Assert.Equal(4, Zoning.Waiting(rig.W.Pop, s));
        int streets0 = r.Settlements[s].Plan.Streets.Count; var plan = Zoning.Plan(r, s, rig.L.Rules, rig.L.GrowthTuning, day0, gs.Projects.Where(p => p.Settlement == s).Select(p => p.Rect).ToList());
        Assert.NotNull(plan); Assert.Equal(plan, Zoning.Plan(r, s, rig.L.Rules, rig.L.GrowthTuning, day0, gs.Projects.Where(p => p.Settlement == s).Select(p => p.Rect).ToList()));          // a pure function of the region
        // across two game years of seasons (one every 15 days) exactly one street is laid, at the first
        var laid = new List<StreetRecord>();
        for (int day = day0, season = 0; season < 8; season++, day += TimeConst.DaysPerSeason) laid.AddRange(Zoning.Seasonal(rig.L, day, keyed));
        var rec = Assert.Single(laid); Assert.Equal(s, rec.Settlement); Assert.Equal(day0, rec.Day);
        Assert.Equal(plan, rec); Assert.InRange(rec.Day - day0, 0, 2 * TimeConst.DaysPerYear);
        Assert.Equal(streets0 + 1, r.Settlements[s].Plan.Streets.Count); Assert.Equal(rec, Assert.Single(gs.Streets));
        // the street is waste it runs over: not water, not a field, not the commons, nothing built
        Assert.InRange(rec.Plots, 6, 10);
        var t = rig.L.GrowthTuning; var reserved = gs.Projects.Where(p => p.Settlement == s).Select(p => p.Rect).ToList(); var found = new List<RectM>();
        while (found.Count < 40 && RegionGrowth.FindPlot(r, s, t.PlotWidthTownM, t.PlotDepthTownM, t.PlanMarginM, [.. reserved, .. found], out var rect, out _)) found.Add(rect);
        Assert.Equal(rec.Plots, found.Count);                                                          // the plots the planner counted are the plots growth and the lord find
        int lo = Math.Min(rec.A.X, rec.B.X) - 60, hi = Math.Max(rec.A.X, rec.B.X) + 60, lo2 = Math.Min(rec.A.Y, rec.B.Y) - 60, hi2 = Math.Max(rec.A.Y, rec.B.Y) + 60;
        Assert.All(found, q => Assert.True(q.X >= lo && q.Right <= hi && q.Y >= lo2 && q.Bottom <= hi2, "a plot lies along the new street"));
        // and the lord lists them: 462 crowns for an 11 x 28 metre town plot, in the Phase 5 price, with no crowding
        var listings = rig.L.Listings(s);
        Assert.NotEmpty(listings); Assert.All(listings, x => Assert.Equal(-1, x.Lot));
        foreach (var x in listings)
        {
            long expect = LandRules.PlotPrice(rig.L.Rules, x.AreaM2, SettlementClass.Town, LandRules.DemandPm(rig.L.Rules, LandRules.CrowdedPct(rig.L.Rules, hht, s)));
            Assert.Equal(expect, x.PriceMites);
            Assert.InRange(x.PriceMites, 40_000, 56_000);
        }
        Assert.Contains(rig.Chron, e => e.TypeId == "street_laid" && e.TextKey == "ch_street_laid" && e.Causes.Any(c => c.NoteKey == "ch_note_street_no_plots"));
        // no second street at once, even with the plots now reserved again
        Assert.Empty(Zoning.Seasonal(rig.L, day0 + 8 * TimeConst.DaysPerSeason, keyed));
        output.WriteLine($"{r.Settlements[s].Name}: street {rec.A} -> {rec.B}, {rec.Plots} plots, first at {listings[0].PriceMites / 100} crowns");
    }

    [Fact]
    public void HW4_TheStreetIsAChunkDeltaInAVisitedChunk_AndTheRegionHashAndTheBaselinePeopleDoNotMove()
    {
        using var w = SoakWorld.Create(withTicker: false); var reg = w.Pop.Region;
        var gen = new ChunkGenerator(reg, w.Pop, EconFixture.Content);
        var store = new ChunkStore(); var writer = new ChunkDeltaWriter(gen, store);
        var lw = new LandWorld(new LandBook(), w.Pop, w.Econ, EconFixture.Content, () => w.Economy.Growth.State, () => writer, w.Markets) { Seed = 1234 };
        string baseHash = reg.ComputeHash();
        int s = Enumerable.Range(0, reg.Settlements.Count).First(i => reg.Settlements[i].Class == SettlementClass.Town && !reg.Settlements[i].Plan.Lots.Any(x => x.Vacant));
        var rec = Zoning.Plan(reg, s, lw.Rules, lw.GrowthTuning, 0)!;
        // a chunk the street runs through is visited, with a player's edit in it; another that it touches is not
        int cx = (rec.A.X + rec.B.X) / 2 / 32, cy = (rec.A.Y + rec.B.Y) / 2 / 32; var key = new ChunkKey(cx, cy, 0);
        var g0 = gen.GenerateLevels(cx, cy); store.RecordFirstVisit(key, ChunkGenerator.Version, g0.ContentHash, g0.Levels[0]);
        string snapshot = store.GetSnapshot(key)!.ContentHash;
        Zoning.Lay(lw, rec, null);
        Assert.True(writer.OpsWritten > 0 && writer.ChunksChanged >= 1, "the visited chunk learns of the street by a delta");
        Assert.Equal(snapshot, store.GetSnapshot(key)!.ContentHash);                                    // the first visit is never rewritten
        var now = store.Rebuild(key)!; var want = gen.GenerateLevels(cx, cy).Levels[0];
        for (int i = 0; i < 1024; i++) Assert.Equal(want.Tiles.GetValueOrDefault(i), now.Tiles.GetValueOrDefault(i));      // the delta gives the chunk the street the plan gives a first visit
        Assert.Equal(baseHash, reg.ComputeHash());                                                       // the region's hash is the generated one's, whatever was laid since
        var basePop = Ravensguard.Sim.People.Populator.Populate(reg.GeneratedView(), EconFixture.Shared.History, EconFixture.Content);
        Assert.Equal(EconFixture.Pop.ComputeHash(), basePop.ComputeHash());
        Assert.Equal(reg.Settlements[s].Plan.Streets.Count - 1, reg.GeneratedView().Settlements[s].Plan.Streets.Count);
        // a fresh region with the same record replayed has the same plan, the same plots
        using var twin = SoakWorld.Create(withTicker: false);
        var st = new GrowthState(); st.Streets.Add(rec);
        RegionGrowth.Replay(twin.Pop.Region, st); RegionGrowth.Replay(twin.Pop.Region, st);               // twice: replay is idempotent
        Assert.Equal(reg.Settlements[s].Plan.Streets, twin.Pop.Region.Settlements[s].Plan.Streets);
        Assert.Equal(reg.Settlements[s].Plan.RadiusM, twin.Pop.Region.Settlements[s].Plan.RadiusM);
        Assert.True(reg.Built.AsSpan().SequenceEqual(twin.Pop.Region.Built));
    }
}
