using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.CloseOut;

/// <summary>
/// The Phase 5 overview's integration tests that the steps left to the close-out (docs/specs/phase5/00-phase5-overview.md section 4): OV-3 (claim a clearing and build a homestead), the Phase 5 part
/// of OV-4 (the concept book's first hour: the lot near 450 crowns, a market stall rented with about 190 crowns and its first takings), OV-5 (an Ironman hero with a smithy, a leased field and a
/// half-built barn dies; his son of 17 carries on) and OV-6 (a 30-day scripted run, saved, loaded and saved at five points). OV-1 is <c>WagonEndToEndTests</c>, OV-2 <c>SmithyEndToEndTests</c>.
/// Every one is a whole <see cref="WorldRuntime"/> of a new game (seed 1234) and every step a command through the hero's desk, as the game sends them; the test's faucets are named where used.
/// </summary>
public class Phase5OverviewTests(ITestOutputHelper output)
{
    private static (SeamWorld W, WorldState State) Game()
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = ContinentSettings.FindBaseContent()!;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var h = HeroRecord.FromOrigin("Aldric Vane", false, w.Content.All<OriginDef>().OrderBy(o => o.Order).First());
        var st = NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, h, null);
        st.Content = w.Content;
        return (w, st);
    }

    /// <summary>The hero's desk on a runtime stepped on the test's thread (every command applied at the next barrier, as the game's are).</summary>
    private sealed class Play(WorldRuntime rt, HeroContext ctx)
    {
        public readonly HeroDesk Desk = new();
        public HeroResult Do(HeroCommand c)
        {
            var cmd = Desk.Issue(c with { IssuedSec = Math.Max(c.IssuedSec, rt.HourDone * 3600) });
            Desk.ApplyDue(ctx, rt.HourDone + 1);
            return Desk.View!.Results.Last(r => r.Seq == cmd.Seq);
        }
        public long Sec => (rt.HourDone + 1) * 3600;
        public void Days(int days) => rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1 + days * 24L));
        public void Hours(int hours) => rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1 + hours));
    }

    private static int DayOfYear(long sec) => (int)(sec / 86400 % 60) + 1;

    private static SaveFile SaveAt(WorldRuntime rt) => rt.AtBarrier(() => WorldSave.Capture(rt.State, "h", "0.1")).GetAwaiter().GetResult();

    // ------------------------------------------------------------------ OV-3: claim a clearing and build a homestead

    [Fact]
    public void OV3_Claim_wild_land_build_a_cottage_by_hand_and_with_two_hired_hands_and_the_claim_becomes_a_Freehold_deed()
    {
        var (w, s) = Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var ctx = new HeroContext(s, s.Party!, w.Content);
        var p = new Play(rt, ctx);
        var lw = s.HomeRecords!.Sim.Get<LandWorld>()!; var cw = s.HomeRecords.Sim.Get<ConstructionWorld>()!;
        var e = s.Economy!; var reg = s.Population.Region;
        e.Ledger.Mint(ctx.Purse, Coin.FromCrowns(100), SourceReason.Seed);                         // the test's purse: the hands' wages
        long seeded = e.Ledger.Audit().Seeded.Mites;

        // 1. wild land outside every fief, open and level, with room for 24 tiles and a place within the hiring radius (the hands walk out from there)
        int N = RegionData.Size; (int X, int Y) at = default; long best = long.MaxValue;
        for (int t = 0; t < RegionData.Tiles; t++)
        {
            if (!LandRules.OpenGround(reg, t) || LandRules.LandFief(reg, lw.Rules, t) != -1) continue;
            int x = t % N * RegionData.TileM + RegionData.TileM / 2, y = t / N * RegionData.TileM + RegionData.TileM / 2;
            long near = reg.Settlements.Min(c => (long)(c.CenterX - x) * (c.CenterX - x) + (long)(c.CenterY - y) * (c.CenterY - y));
            if (near >= best || near > (long)cw.Rules.HireRadiusM * cw.Rules.HireRadiusM * 2 / 3) continue;
            if (lw.Grow(t, [], 24, x, y, u => lw.ClaimTileOk(u, -1, null)).Length < 24) continue;
            best = near; at = (x, y);
        }
        Assert.True(best < long.MaxValue, "no wild land near a place in seed 1234");
        // 1b. P6-B6 (holdings 3.2, HW-2): the banner is made at the bench (a pole and a cloth, 0.5 h; the test's faucet gathers the pole and the cloth) and planted by the stake
        var noBanner = p.Do(LandDesk.Stake(at.X, at.Y, false, p.Sec));
        Assert.False(noBanner.Ok);
        ushort banner = e.Goods.IdOf("claim_banner");
        var brec = ctx.Recipes["make_claim_banner"];
        foreach (var slot in brec.Slots) ctx.Pack.Create(slot.Alts[0].Good, slot.Alts[0].Qty, CreateReason.Import);
        var bstart = p.Do(new HeroCommand { Kind = HeroCommandKind.CraftStart, Recipe = brec.Index, Site = new CraftSiteSpec(255, true, ["knife"], [], []) });
        Assert.True(bstart.Ok, bstart.Text);
        var bjob = ctx.Craft.State.Jobs.Values.Single(j => j.Owner == ctx.Party.Hero);
        Assert.True(p.Do(new HeroCommand { Kind = HeroCommandKind.CraftWork, Job = bjob.Id, UntilSec = p.Sec + 3600 }).Ok);
        Assert.Equal(1, ctx.Pack.Qty(banner));
        var staked = p.Do(LandDesk.Stake(at.X, at.Y, false, p.Sec));
        Assert.Equal(0, ctx.Pack.Qty(banner));
        Assert.True(staked.Ok, staked.Text);
        var claim = s.Land!.Claims.Single();
        Assert.Equal((ClaimState.Open, 8, -1, false), (claim.State, claim.Tiles.Length, claim.Fief, claim.Squatting));
        output.WriteLine($"staked a claim of {claim.Tiles.Length} tiles at {at.X},{at.Y} (the nearest place {Math.Sqrt(best):F0} m away)");

        // 2. the cottage placed on the claim through the desk; the hero carries its materials to the pile (the test's faucet: what he would have bought and carted)
        int cx = at.X - 4, cy = at.Y - 4;
        var plan = p.Do(ConstructionDesk.Plan("test_cottage", cx, cy, 0, false, p.Sec, cx - 2, cy));
        Assert.True(plan.Ok, plan.Text);
        var site = s.Construction!.Sites.Values.Single(x => x.Owner == ctx.Party.Hero);
        // the plan is the walls, the floor and the roof: a home needs a hearth and two beds, which the hero places inside (build mode's furniture, on the same site)
        foreach (var (piece, fx, fy) in new[] { ("hearth_central", cx + 4, cy + 2), ("straw_pallet", cx + 1, cy + 1), ("straw_pallet", cx + 1, cy + 3) })
        {
            var f = p.Do(ConstructionDesk.Place(piece, fx, fy, fx, fy, 0, "rect", 0, p.Sec, ConstructionIds.SerialOf(site.Id)));
            Assert.True(f.Ok, f.Text);
        }
        long needH = cw.GhostsOf(site.Id).Sum(g => g.WorkNeed);
        output.WriteLine("needed: " + string.Join(", ", cw.Needed(site).Select(kv => $"{e.Goods.StringId(kv.Key)} {kv.Value}")));
        foreach (var (g, q) in cw.Needed(site))
        {
            ctx.Pack.Create(g, q, CreateReason.Import);
            var d = p.Do(ConstructionDesk.Deliver(site.Id, e.Goods.StringId(g), q, site.PileX, site.PileY, p.Sec));
            Assert.True(d.Ok, d.Text);
        }
        Assert.DoesNotContain(cw.Needed(site), kv => cw.PileOf(site).Qty(kv.Key) < kv.Value);

        // 3. two hired hands, and the hero by hand four hours every morning
        var hire = p.Do(ConstructionDesk.Hire(site.Id, 2, false, p.Sec));
        Assert.True(hire.Ok, hire.Text);
        Assert.Equal(2, site.Crew.Count);
        long heroSec = 0, toppedG = 0; int days = 0;
        while (site.State != SiteState.Finished && days < 60)
        {
            p.Days(1); days++;
            // what the site panel says is missing (thatch and logs rot a little on an open pile) is brought, as a player tops up his pile
            long topped = 0;
            foreach (var (g2, q2) in cw.Needed(site))
            {
                long lack = q2 - cw.PileOf(site).Qty(g2); if (lack <= 0) continue;
                ctx.Pack.Create(g2, lack, CreateReason.Import); topped += lack;
                Assert.True(p.Do(ConstructionDesk.Deliver(site.Id, e.Goods.StringId(g2), lack, site.PileX, site.PileY, p.Sec)).Ok);
            }
            toppedG += topped;
            if (days % 3 == 0) output.WriteLine($"day {days}: {site.State} {site.Pause}, crew {site.Crew.Count}, pieces {site.PiecesBuilt}, coin {ctx.Coin}; short: {string.Join(", ", cw.Needed(site).Where(kv => cw.PileOf(site).Qty(kv.Key) < kv.Value).Select(kv => $"{e.Goods.StringId(kv.Key)} {cw.PileOf(site).Qty(kv.Key)}/{kv.Value}"))}");
            long day0 = (p.Sec / 86400 - 1) * 86400;
            var g = cw.GhostsOf(site.Id).Where(x => x.State is GhostState.Ready or GhostState.InProgress).OrderBy(x => x.Z).ThenBy(x => x.Id).FirstOrDefault();
            if (g == null) continue;
            long before = s.Construction.HeroWorkedTo;
            var wk = p.Do(ConstructionDesk.Work(g.Id, day0 + 8 * 3600, day0 + 12 * 3600, g.X, g.Y, g.Z));
            if (wk.Ok) heroSec += Math.Max(0, s.Construction.HeroWorkedTo - Math.Max(before, day0 + 8 * 3600));
        }
        output.WriteLine($"the cottage was built in {days} days ({site.PiecesBuilt} pieces); the hero worked {heroSec / 3600.0:F1} h of it by hand and topped up {toppedG / 1000.0:F1} kg that rotted on the pile; claim {claim.State}, {claim.Tiles.Length} tiles");
        Assert.Equal(SiteState.Finished, site.State);
        Assert.True(heroSec > 0, "the hero built some of it by hand");
        Assert.InRange(days, 4, 40);

        int stId = cw.Rooms.StructureAt(cx + 1, cy + 1, 0);
        var facts = stId > 0 ? cw.Rooms.Facts(stId) : null;
        output.WriteLine($"structure {stId}: dwelling {facts?.IsDwelling} ({facts?.DwellingKind}), beds {facts?.Beds}, hearth rooms {facts?.HearthRooms}, rooms {string.Join(", ", (facts?.Rooms ?? []).Select(x => x.ToString()))}");
        // 4. the dwelling stands: the claim is a Freehold deed of 16 tiles, the hero's
        Assert.Equal(ClaimState.Deeded, claim.State);
        var deed = s.Land.Deeds[claim.DeedId];
        Assert.Equal((Tenure.Freehold, 0L, HoldingKind.Claim, ctx.Party.Hero), (deed.Tenure, deed.DuesMitesPerSeason, deed.Kind, deed.Holder));
        Assert.Equal(16, claim.Tiles.Length);
        Assert.Contains(s.Chronicle.All, x => x.TypeId == "claim_improved");
        // 4b. P6-B6 (holdings 3.1, HW-1): the acre cleared by the hero four hours a morning and two hired hands (90 worker-hours), then sown in the window: the claim grows to 24 tiles
        var hw = s.HomeRecords!.Sim.Get<Ravensguard.Sim.Holdings.HoldingsWorld>()!;
        var cl = p.Do(Ravensguard.Sim.Holdings.HoldingsDesk.Clear(claim.Serial, -1, p.Sec));
        Assert.True(cl.Ok, cl.Text);
        var clearing = hw.Book.Clearings.Single();
        output.WriteLine($"clearing {clearing.Serial}: tile {clearing.Tile}, {clearing.Trees} trees, {clearing.NeedMilli / 1000} worker-hours");
        var hh = p.Do(Ravensguard.Sim.Holdings.HoldingsDesk.Hire(clearing.Serial, 2, p.Sec));
        Assert.True(hh.Ok, hh.Text);
        int clearDays = 0;
        while (clearing.State == Ravensguard.Sim.Holdings.ClearingState.Working && clearDays < 30)
        {
            long d0 = (p.Sec / 86400) * 86400 + 86400;
            p.Days(1); clearDays++;
            long dayStart = (p.Sec / 86400 - 1) * 86400;
            p.Do(Ravensguard.Sim.Holdings.HoldingsDesk.Work(clearing.Serial, dayStart + 8 * 3600, dayStart + 12 * 3600, p.Sec));
            _ = d0;
        }
        output.WriteLine($"the acre was cleared in {clearDays} days: hero {clearing.HeroMilli / 1000.0:F1} h, {clearing.Felled} trees felled, {clearing.LogsG / 1000} kg of logs and {clearing.FirewoodG / 1000} kg of firewood at the stumps, field {clearing.Parcel}");
        Assert.Equal(Ravensguard.Sim.Holdings.ClearingState.Cleared, clearing.State);
        Assert.Equal(90_000, clearing.DoneMilli);
        Assert.True(clearing.HeroMilli > 0 && clearing.HeroMilli < 90_000, "the hero and the hands share the 90 worker-hours");
        var farm = s.HomeRecords.Economy.Farming;
        int hero_hh = Ravensguard.Sim.Construction.ConstructionDesk.HouseholdOf(ctx);
        Assert.Equal(hero_hh, farm.HolderOf(clearing.Parcel));
        // sown in the window (the test waits for it as a player would), the seed from the pack (the test's faucet stands for what he bought at the market)
        int guard = 0;
        while (farm.SowingSeasonAt(DayOfYear(p.Sec)) == null && guard++ < 70) p.Days(1);
        foreach (var gid in new[] { "barley", "oats", "peas", "beans", "wheat", "rye" }) ctx.Pack.Create(e.Goods.IdOf(gid), 20_000, CreateReason.Import);
        var sow = p.Do(Ravensguard.Sim.Holdings.HoldingsDesk.Sow(clearing.Serial, p.Sec));
        Assert.True(sow.Ok, sow.Text);
        Assert.Equal(24, claim.Tiles.Length);
        Assert.Equal(1, claim.SownAcres);
        Assert.Equal(Ravensguard.Sim.Holdings.ClearingState.Sown, clearing.State);
        output.WriteLine($"sown with {clearing.Crop} on day {p.Sec / 86400}; the claim is {claim.Tiles.Length} tiles, a {s.Land.Deeds[claim.DeedId].Tenure} deed");
        Assert.Equal(Tenure.Freehold, s.Land.Deeds[claim.DeedId].Tenure);
        // on to the harvest: it goes to the hero's barn (the hired hands of the village reap it, paid by his household)
        var barn = e.OtherStores[Ravensguard.Sim.Farming.FarmIds.Barn(hero_hh)];
        ushort grain = e.Goods.IdOf(clearing.Crop);
        Func<long> inBarn = () => barn.Qty(grain) + e.HouseholdStore[hero_hh].Qty(grain);
        long barnBefore = inBarn();
        e.Ledger.Mint(e.HouseholdPurse[hero_hh], Coin.FromCrowns(5), SourceReason.Seed);          // the household's purse for the harvest hands (the test's faucet: he pays them out of his takings)
        seeded = e.Ledger.Audit().Seeded.Mites;
        for (int d = 0; d < 130 && inBarn() <= barnBefore; d++) p.Days(1);
        output.WriteLine($"harvest in the hero's barn after day {p.Sec / 86400}: {(inBarn() - barnBefore) / 1000} kg of {clearing.Crop}");
        Assert.True(inBarn() > barnBefore, "the acre's harvest goes to the hero's barn");

        // 5. coin and mass exact; the world saves and loads with the deed
        var audit = e.Ledger.Audit();
        Assert.True(audit.IsConserved); Assert.Equal(seeded, audit.Seeded.Mites); Assert.Equal(0, audit.Minted.Mites);
        Assert.True(e.MassAuditHolds());
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(SaveAt(rt))), w.Content, "h", null, w.Continent, w.History);
        Assert.Equal(s.Land.ComputeHash(), back.Land!.ComputeHash());
        output.WriteLine($"cottage {needH / 3_600_000_000.0:F1} worker-hours of pieces (the plan 250.5, the hearth and the pallets on top)");
        Assert.InRange(needH / 3_600_000_000.0, 250.5, 256.0);
    }

    // ------------------------------------------------------------------ OV-4 (Phase 5 part): the lot near 450 crowns, a stall rented with about 190 crowns, the first takings

    [Fact]
    public void OV4_The_first_hours_Phase_5_steps_a_lot_near_450_crowns_on_the_market_a_stall_rented_and_its_first_takings()
    {
        var (w, s) = Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var ctx = new HeroContext(s, s.Party!, w.Content);
        var p = new Play(rt, ctx);
        var lw = s.HomeRecords!.Sim.Get<LandWorld>()!; var ow = s.HomeRecords.Sim.Get<OwnershipWorld>()!;
        var e = s.Economy!; var reg = s.Population.Region;
        // about 190 crowns in the purse (what the first days earn in the concept book: hides hauled, a guard contract, a deserter hired)
        long have = e.Ledger.Balance(ctx.Purse).Mites;
        if (have < 19_000) e.Ledger.Mint(ctx.Purse, new Coin(19_000 - have), SourceReason.Seed);
        long seeded = e.Ledger.Audit().Seeded.Mites;
        int home = s.Population.People.HomeSettlement[s.Hero!.PersonIndex];
        int town = reg.Settlements[home].Class == SettlementClass.Town ? home : reg.Settlements.FindIndex(x => x.Class == SettlementClass.Town);

        // the reeve's notice board: a lot near 450 crowns (a burgage plot is 462)
        int lotTown = Enumerable.Range(0, reg.Settlements.Count).Where(i => reg.Settlements[i].Class == SettlementClass.Town && lw.Listings(i).Any(l => l.PriceMites is >= 30_000 and <= 60_000))
            .OrderBy(i => (long)(reg.Settlements[i].CenterX - reg.Settlements[home].CenterX) * (reg.Settlements[i].CenterX - reg.Settlements[home].CenterX) + (long)(reg.Settlements[i].CenterY - reg.Settlements[home].CenterY) * (reg.Settlements[i].CenterY - reg.Settlements[home].CenterY)).ThenBy(i => i).DefaultIfEmpty(-1).First();
        Assert.True(lotTown >= 0, "no town of the region lists a lot near 450 crowns");
        var lots = lw.Listings(lotTown);
        for (int i = 0; i < reg.Settlements.Count; i++) if (reg.Settlements[i].Class == SettlementClass.Town) output.WriteLine($"{reg.Settlements[i].Name} ({(i == home ? "home" : "")}): {string.Join(", ", lw.Listings(i).Select(l => $"{l.Lot}:{l.AreaM2}m2 {Quotes.Money(l.PriceMites)}"))}");
        output.WriteLine($"home {reg.Settlements[home].Name} {reg.Settlements[home].Class}: {string.Join(", ", lw.Listings(home).Select(l => $"{l.Lot}:{l.AreaM2}m2 {Quotes.Money(l.PriceMites)}"))}");
        Assert.NotEmpty(lots);
        Assert.Contains(lots, l => l.PriceMites is >= 30_000 and <= 60_000);
        output.WriteLine($"the nearest notice board with a plot near 450 crowns: {reg.Settlements[lotTown].Name}, {string.Join(", ", lots.Take(5).Select(l => Quotes.Money(l.PriceMites)))} (the home town lists only houses: phase-5.md finding S10)");

        // nails bought to sell (at the market; the test's faucet when the new game's book has none yet)
        ushort nails = e.Goods.IdOf("nails");
        const long Qty = 10_000;
        var book = ctx.Book(town);
        bool bought = false;
        if (book != null && book.Goods[nails].StockUnits >= Qty)
        {
            book.Sync(nails);
            var q = Quotes.Buy(book, nails, Qty);
            var r = p.Do(new HeroCommand { Kind = HeroCommandKind.Buy, Settlement = town, Good = nails, Qty = Qty, Limit = q.TotalMites + 5, IssuedSec = p.Sec, Origin = CommandOrigin.Human });
            bought = r.Ok;
            output.WriteLine("bought nails: " + r.Text);
        }
        if (!bought) { ctx.Pack.Create(nails, Qty, CreateReason.Import); seeded = e.Ledger.Audit().Seeded.Mites; }

        // rent a market stall, stock it, mind it for a market morning
        var view = LandDesk.View(lw, ctx.Party.Hero, town, reg.Settlements[town].CenterX, reg.Settlements[town].CenterY, (int)(p.Sec / 86400));
        Assert.NotEmpty(view.FreeStalls);
        long coin0 = ctx.Coin;
        var rent = p.Do(LandDesk.Stall(town, view.FreeStalls[0], false, p.Sec));
        Assert.True(rent.Ok, rent.Text);
        long subject = LandIds.StallSubject(town, view.FreeStalls[0]);
        var opened = p.Do(BusinessDesk.Stall(town, view.FreeStalls[0], -1, p.Sec));                 // his own stall, no business behind it
        Assert.True(opened.Ok, opened.Text);
        var stocked = p.Do(BusinessDesk.Stock(subject, "nails", Qty, p.Sec));
        Assert.True(stocked.Ok, stocked.Text);
        int day = (int)(p.Sec / 86400) + 1;
        while (ow.FairDay(town, day)) day++;
        long from = (long)day * 86400 + 8 * 3600;
        rt.AdvanceTo(GameTime.FromGameHours(from / 3600 + 8));
        long coin1 = ctx.Coin;
        var mind = p.Do(BusinessDesk.Mind(subject, from, from + 8 * 3600));
        Assert.True(mind.Ok, mind.Text);
        var stall = ow.Book.Stalls[subject];
        output.WriteLine($"rent {Quotes.Money(coin0 - coin1)} (the hero had {Quotes.Money(coin0)}); minded 8 hours: sold {stall.SoldSeason} of {Qty}, takings {Quotes.Money(stall.TakingsSeason)}");
        Assert.True(stall.SoldSeason > 0, "the first takings");
        Assert.True(stall.TakingsSeason > 0);
        Assert.True(ctx.Coin > coin1);
        var audit = e.Ledger.Audit();
        Assert.True(audit.IsConserved); Assert.Equal(seeded, audit.Seeded.Mites); Assert.Equal(0, audit.Minted.Mites);
        Assert.True(e.MassAuditHolds());
    }

    // ------------------------------------------------------------------ OV-5: inheritance of property completes Ironman succession

    [Fact]
    public void OV5_An_Ironman_hero_with_a_smithy_a_leased_field_and_a_half_built_barn_dies_and_his_son_of_17_carries_on_with_all_three()
    {
        var st = Ravensguard.Sim.Tests.Hero.InheritanceTests.Game(1234, "disgraced_squire");
        using var rt = new WorldRuntime(st, st.Content!, new MemoryRegionPager());
        var r = Ravensguard.Sim.Tests.Hero.InheritanceTests.Rig.Of(st);
        var ctx = r.Ctx; var p = new Play(rt, ctx);
        var e = st.Economy!; var reg = st.Population.Region; var cw = st.HomeRecords!.Sim.Get<ConstructionWorld>()!; var lw = st.HomeRecords.Sim.Get<LandWorld>()!;
        r.Sec = p.Sec;
        // his son of 17, his only kin (the first of the Ironman order: no choice is offered)
        int son = r.Person(Sex.Male, 17);
        r.Pt.Father[son] = r.HeroRow; st.Population.ResetKin();
        st.Succession!.Kin.Add(new HeroKinLink(r.Pt.IdOf(son), KinRelation.Child));
        r.SetCoin(ctx.Purse, 300_000);
        // the smithy, with a smith owed a day's wage
        var smithy = r.Shop("smithy", r.Town, r.HeroRow);
        int smith = r.Person(Sex.Male, 30);
        r.Prod.Employ(smithy, smith, Occupation.Smith);
        // a leased field (from the lord, through the desk)
        int parcel = Enumerable.Range(0, reg.Parcels.Count).FirstOrDefault(lw.Leasable, -1);
        if (parcel < 0)
        {
            // seed 1234's lords let no field today: a tenant's field near the town falls back to its lord (as LandSaveTests stages it), who lets it
            parcel = Enumerable.Range(0, reg.Parcels.Count).First(x => reg.Parcels[x].Owner.Kind == OwnerKind.Tenant && LandRegistry.LordHousehold(st.Population, reg.Parcels[x].Settlement) >= 0);
            int lord = LandRegistry.LordHousehold(st.Population, reg.Parcels[parcel].Settlement);
            int old = st.Population.ParcelHolder[parcel]; if (old >= 0) st.Population.Households.ParcelCount[old]--;
            st.Population.ParcelHolder[parcel] = lord; st.Population.Households.ParcelCount[lord]++;
            st.HomeRecords.Economy.Farming.State.Parcels.Holder[parcel] = lord;
        }
        Assert.True(lw.Leasable(parcel));
        var lease = p.Do(LandDesk.LeaseField(parcel, p.Sec));
        Assert.True(lease.Ok, lease.Text);
        var field = st.Land!.Deeds.Values.Single(d => d.Holder == ctx.Party.Hero && d.IsLease);
        // a barn on a lot he bought, half built by a hired crew
        int lotTown = Enumerable.Range(0, reg.Settlements.Count).First(i => lw.Listings(i).Any(l => l.Lot < 0));
        var lot = lw.Listings(lotTown).First(l => l.Lot < 0);
        Assert.True(p.Do(LandDesk.Buy(lot, p.Sec)).Ok);
        var plan = p.Do(ConstructionDesk.Plan("barn", lot.Rect.X + 1, lot.Rect.Y + 1, 0, false, p.Sec));
        Assert.True(plan.Ok, plan.Text);
        var barn = st.Construction!.Sites.Values.Single(x => x.Owner == ctx.Party.Hero);
        foreach (var (g, q) in cw.Needed(barn)) cw.PileOf(barn).Create(g, q, CreateReason.Import);       // the test's faucet: the materials on the pile
        Assert.True(p.Do(ConstructionDesk.Hire(barn.Id, 4, false, p.Sec)).Ok);
        int ghosts = cw.GhostsOf(barn.Id).Count();
        while (barn.PiecesBuilt * 3 < ghosts && barn.State == SiteState.Active) p.Days(1);
        int built = barn.PiecesBuilt;
        Assert.True(built > 0 && barn.State == SiteState.Active, $"{built} of {ghosts}");
        output.WriteLine($"the barn is half built ({built} of {ghosts} pieces); the field is leased till day {field.LeaseEndDay}");

        // he dies (Ironman: the first heir, no choice); the son carries on
        smithy.Owed[smithy.Staff.IndexOf(smith)] = 400;                                       // the day's wage his smith is owed when he dies
        long total = r.TotalCoin; int deeds = st.Land.Deeds.Count;
        r.Sec = p.Sec;
        var died = r.Die(r.Pt.IdOf(son));
        Assert.True(died.Ok, died.Text);
        var est = st.Succession.Estates.Last();
        Assert.True(est.FuneralMites > 0);
        Assert.Contains(est.Debts, d => d.Kind == DebtKind.Wage && d.PaidMites == 400);
        Assert.Equal(son, st.Hero!.PersonIndex);
        Assert.Equal(son, smithy.Owner);
        Assert.Equal(ctx.Party.Hero, field.Holder); Assert.Equal(DeedCause.Inherited, field.Cause);
        Assert.Equal(ctx.Party.Hero, barn.Owner);
        Assert.Equal(PauseReason.Owner, barn.Pause);
        Assert.Equal(total, r.TotalCoin); Assert.Equal(deeds, st.Land.Deeds.Count);
        // the barn waits for him; it resumes when he orders it
        p.Days(1);
        Assert.Equal(built, barn.PiecesBuilt);
        var resume = p.Do(ConstructionDesk.Pause(barn.Id, false, p.Sec));
        Assert.True(resume.Ok, resume.Text);
        if (barn.Crew.Count == 0) Assert.True(p.Do(ConstructionDesk.Hire(barn.Id, 4, false, p.Sec)).Ok);
        p.Days(2);
        output.WriteLine($"the son resumed the barn: {barn.PiecesBuilt} pieces now (was {built}); coin {Quotes.Money(ctx.Coin)}");
        Assert.True(barn.PiecesBuilt > built, "the barn goes on under the son");
        Assert.True(e.Ledger.Audit().IsConserved);
        Assert.True(e.MassAuditHolds());
    }

    // ------------------------------------------------------------------ OV-6: a 30-day scripted run, saved, loaded and saved at five points

    [Fact]
    public void OV6_A_30_day_run_with_construction_a_collapse_crafting_a_business_a_voyage_and_a_succession_saves_loads_and_saves_the_same_bytes_at_five_points()
    {
        var (w, s) = Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        // the scripted state of the resume test (a lot and a stall; a smithy house half built by a crew; a civic contract and a growth site; a commission, an apprenticeship, a guild, a fire;
        // a business with a steward, a stall and a mine; a commissioned wagon and a barge on its run; a will and an NPC estate mid-settlement)
        Ravensguard.Sim.Tests.Abstract.WorldRuntimeResumeTests.Play(rt, w.Content);
        var ctx = new HeroContext(s, s.Party!, w.Content);
        var p = new Play(rt, ctx);
        var cw = s.HomeRecords!.Sim.Get<ConstructionWorld>()!;
        // a collapse: a floor on one post, the post knocked out (on open ground by the start place: the land rule is lifted for the stage, as the dev console does)
        var hs = s.Population.Region.Settlements[s.Population.People.HomeSettlement[s.Hero!.PersonIndex]];
        int ox = hs.CenterX + 40, oy = hs.CenterY + 40;
        var may = cw.May; cw.May = null;
        var cells = new List<PlaceCell> { new(ox + 1, oy + 1, 0, "post_timber") };
        for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) cells.Add(new PlaceCell(ox + x, oy + y, 1, "floor_plank"));
        var placed = cw.Place(ctx.Party.Hero, PurseKind.Person, -1, cells, p.Sec);
        cw.May = may;
        Assert.True(placed.Ok, placed.Key);
        cw.DevStock(placed.Site, p.Sec); cw.DevFinish(placed.Site, 99, 0, 0, p.Sec);
        var knocked = cw.Destroy(ox + 1, oy + 1, 0, p.Sec, ctx.Party.Hero, "knocked out");
        Assert.True(knocked.Ok, knocked.Key);
        Assert.NotEmpty(s.Construction!.Groans);
        // an heir for the succession on day 15
        var pt = s.Population.People; int day0 = (int)(p.Sec / 86400);
        int heir = Enumerable.Range(0, pt.Count).First(i => pt.Status[i] == PersonStatus.Alive && pt.HomeSettlement[i] == pt.HomeSettlement[s.Hero.PersonIndex] && pt.Household[i] >= 0
            && pt.AgeYears(i, day0) is >= 18 and < 30 && pt.Occupation[i] is Occupation.None or Occupation.Labourer && !s.Production!.Businesses.Any(b => b.Owner == i || b.Staff.Contains(i)));
        pt.Father[heir] = s.Hero.PersonIndex; s.Population.ResetKin();
        s.Succession!.Kin.Add(new HeroKinLink(pt.IdOf(heir), KinRelation.Child));

        var trail = new List<string>();
        for (int point = 1; point <= 5; point++)
        {
            p.Days(6);
            if (point == 3)
            {
                var died = p.Do(SuccessionDesk.HeirCommand(pt.IdOf(heir), hs.CenterX * 1000L, hs.CenterY * 1000L, p.Sec));
                Assert.True(died.Ok, died.Text);
                Assert.Equal(heir, s.Hero!.PersonIndex);
            }
            var file = SaveAt(rt);
            var bytes = SaveCodec.Encode(file);
            var back = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, "h", null, w.Continent, w.History);
            back.Content = w.Content;
            using var again = new WorldRuntime(back, w.Content, new MemoryRegionPager());
            var file2 = SaveAt(again);
            var differ = file.Sections.Keys.Union(file2.Sections.Keys).Where(k => !(file.Sections.TryGetValue(k, out var a) && file2.Sections.TryGetValue(k, out var b) && a.Span.SequenceEqual(b.Span))).ToList();
            Assert.True(differ.Count == 0, $"point {point} (day {p.Sec / 86400 - day0}): sections differ after a load: {string.Join(", ", differ)}");
            Assert.Equal(bytes, SaveCodec.Encode(file2));
            string h = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
            trail.Add(h);
            output.WriteLine($"point {point}, day {p.Sec / 86400 - day0}: {bytes.Length:N0} bytes, {file.Sections.Count} sections, {h}");
        }
        // what the 30 days did: the collapse fell, the smithy house grew, the hero's line went on, coin and mass exact
        Assert.Empty(s.Construction.Groans);
        Assert.Contains(s.Construction.Sites.Values, x => x.PiecesBuilt > 0);
        Assert.Contains(s.Chronicle.All, x => x.TypeId == "estate_settled");
        Assert.True(s.Economy!.Ledger.Audit().IsConserved);
        Assert.True(s.Economy.MassAuditHolds());
        output.WriteLine("trail " + string.Join(" ", trail));
    }
}
