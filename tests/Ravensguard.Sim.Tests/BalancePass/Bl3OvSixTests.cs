using Ravensguard.Sim.Building;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Production.Industry;
using Ravensguard.Sim.Tests.Phase7Overview;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.BalancePass;

/// <summary>
/// P8-BL-1 (balance pass 3.1, golden BL-3; OV-6's gap): the test hero holds a claim, upgrades the smeltery to L2, sends his own caravan with its quote and checks the payback. The run walks the steps in order and records how far it got
/// (<see cref="Steps"/>); coin and mass are exact after every step. Where a step cannot be taken the run stops there and the ratchet (<see cref="BalanceRatchets.Bl3StepsReached"/>) holds the furthest step reached so far.
/// </summary>
public class Bl3OvSixTests(ITestOutputHelper output)
{
    public static readonly string[] Steps = ["works founded", "claim staked", "upgrade placed on the claim", "builder commissioned", "smeltery at L2", "caravan quote read", "payback read"];
    private const int Town = 0;
    private static readonly int[] IronCrew = [3, 2, 1, 5, 1];

    [Fact, Trait("Category", "Soak")]
    public void BL3_TheHeroHoldsAClaim_UpgradesTheSmelteryToL2_ReadsItsCaravanQuoteAndItsPayback_CoinAndMassExactAtEveryStep()
    {
        var (w, st) = P7Game.Game();
        using var rt = new WorldRuntime(st, w.Content, new MemoryRegionPager());
        var iw = st.HomeRecords!.Sim.Get<IndustryWorld>()!; iw.NpcDisabled = true; st.HomeRecords.Economy.Growth.CharcoalCrews = false;
        rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 24L * 60));
        var ctx = new HeroContext(st, st.Party!, w.Content); var desk = new HeroDesk(); var e = st.Economy!; var led = e.Ledger;
        led.Mint(ctx.Purse, new Coin(6_000_000), SourceReason.Seed);
        long Sec() => rt.HourDone * 3600; int day() => (int)(rt.HourDone / 24);
        HeroResult Do(HeroCommand c) { var cmd = desk.Issue(c); desk.ApplyDue(ctx, rt.HourDone); return desk.View!.Results.Last(r => r.Seq == cmd.Seq); }
        void Exact(string at) { Assert.True(led.Audit().IsConserved, "coin after " + at); Assert.True(e.MassAuditHolds(), "mass after " + at); }

        // the hiring hall and the region's burners, as IN-20 and OV-6 have them
        var pool = new List<int>(); var prod = st.Production!; var pt = st.Population.People;
        foreach (var b in prod.Businesses.ToList())
        {
            if (prod.KindOf(b).Stage != null) continue;
            for (int i = b.Staff.Count - 1; i >= 0 && pool.Count < 200; i--) { int p = b.Staff[i]; if (pt.Occupation[p] is Occupation.Spinner or Occupation.Labourer && pt.AgeYears(p, day()) >= 18 && pt.Status[p] == PersonStatus.Alive) { pool.Add(p); prod.DropStaff(b, i, true); } }
        }
        void Hall(Works wk)
        {
            foreach (var s in wk.Stages)
            {
                var bz = prod.ById(new EntityId(s.Business))!; if (!iw.SlotsFor(bz, out var slots)) continue; var kd = iw.KindOf(s);
                foreach (var (occ, cnt, _) in slots)
                    for (int have = bz.Staff.Count(p => pt.Occupation[p] == occ); have < cnt && pool.Count > 0; have++)
                    { int p = pool[^1]; pool.RemoveAt(pool.Count - 1); pt.Occupation[p] = occ; pt.SetSkill(p, 0, kd.Skill == Skill.None ? Skill.Farming : kd.Skill, LabourMarket.StartSkill); prod.Employ(bz, p, occ); }
            }
        }
        void Stock(string good, long minG)
        {
            var book = st.Markets!.BookForSettlement(Town); var id = e.Goods.IdOf(good); var gb = book.Goods[id];
            if (gb.StockUnits >= minG || book.Merchants.Count == 0) return;
            var m = book.Merchants[0]; long add = minG - gb.StockUnits; m.Store.Create(id, add, CreateReason.Seed); book.List(m, id, add, 0, day(), true);
        }
        void RunDay()
        {
            Stock("charcoal", Math.Max(12_000_000, 26 * st.Markets!.BookForSettlement(Town).Goods[e.Goods.IdOf("charcoal")].Demand)); Stock("firewood", 60_000_000); Stock("straw", 400_000); Stock("hay", 400_000);
            foreach (var wk in st.Industry!.Works) if (!wk.Closed) Hall(wk);
            rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 24));
        }
        int reached = 0; void Reach(int n) { reached = Math.Max(reached, n); output.WriteLine($"step {n}: {Steps[n - 1]} (day {day()})"); Exact(Steps[n - 1]); }

        // 1. the works
        var found = Do(IndustryDesk.Found("iron", Town, 1_000_000, Sec())); Assert.True(found.Ok, found.Text.ToString());
        var works = st.Industry!.Works[^1];
        for (int i = 0; i < IronCrew.Length; i++) iw.SetCrew(works.Stages[i].Business, IronCrew[i]);
        works.BuildingsCostMites = iw.L1BillMites("iron", Town); Hall(works);
        for (int d = 0; d < 30; d++) RunDay();
        Reach(1);

        // 2. a claim: wild land beside a place, a banner (the bench's work is not repeated here: OV-5 of Phase 5 makes one), the stake
        var reg = st.Population.Region; var lw = st.HomeRecords.Sim.Get<LandWorld>()!; var cw = st.HomeRecords.Sim.Get<ConstructionWorld>()!;
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
        if (best == long.MaxValue) { output.WriteLine("no wild land near a place in seed 1234"); goto done; }
        ctx.Pack.Create(e.Goods.IdOf("claim_banner"), 1, CreateReason.Import);
        var staked = Do(LandDesk.Stake(at.X, at.Y, false, Sec())); output.WriteLine($"stake: {staked.Code} {staked.Text}");
        if (!staked.Ok) goto done;
        Reach(2);

        // 3. the upgrade on the claim
        var view = IndustryViews.Build(iw, works.Owner, day()); var wv = view.Works.Single(x => x.Id == works.Id);
        var smelt = wv.Stages.First(s => s.KindId.Contains("smelt", StringComparison.OrdinalIgnoreCase));
        var up = Do(IndustryDesk.Upgrade(new EntityId(smelt.Business), at.X - 4, at.Y - 4, 0, Sec())); output.WriteLine($"upgrade: {up.Code} {up.Text}");
        if (!up.Ok) goto done;
        Reach(3);
        var site = cw.Book.Sites.Values.OrderByDescending(x => x.Id.Value).First(); var bids = cw.Bids(new EntityId(site.Id.Value));
        output.WriteLine($"site {site.Id.Value}: {bids.Count} bids");
        if (bids.Count == 0) goto done;
        var com = Do(ConstructionDesk.Commission(new EntityId(site.Id.Value), bids[0].Builder.Id, 2, Sec())); output.WriteLine($"commission: {com.Code} {com.Text}");
        if (!com.Ok) goto done;
        Reach(4);

        // 4. the builder's months; the works earns meanwhile
        void TopUp()                                                                                               // the hero carries what the pile lacks, as a player tops up his pile (Phase 5's OV test does the same)
        {
            foreach (var (g, q) in cw.Needed(site))
            {
                long lack = q - cw.PileOf(site).Qty(g); if (lack <= 0) continue;
                ctx.Pack.Create(g, lack, CreateReason.Import);
                Do(ConstructionDesk.Deliver(site.Id, e.Goods.StringId(g), lack, site.PileX, site.PileY, Sec()));
            }
        }
        for (int d = 0; d < 240 && st.Industry.Works.Single(x => x.Id == works.Id).Stages.First(s => s.Business == smelt.Business).Tier < 2; d++)
        {
            TopUp(); RunDay(); if (d % 30 == 29) { Exact("day " + d); output.WriteLine($"  day {d}: site {site.State} {site.Pause}, pieces {site.PiecesBuilt}, crew {site.Crew.Count}"); }
        }
        var now = st.Industry.Works.Single(x => x.Id == works.Id);
        if (now.Stages.First(s => s.Business == smelt.Business).Tier < 2) goto done;
        Reach(5);

        // 5. the own caravan: the last stage's standing order to the nearest other town, its quote read, a month of trade, the payback
        for (int d = 0; d < 10; d++) RunDay();
        wv = IndustryViews.Build(iw, works.Owner, day()).Works.Single(x => x.Id == works.Id);
        int dest = Enumerable.Range(1, reg.Settlements.Count - 1).FirstOrDefault(i => reg.Settlements[i].Class == Ravensguard.Sim.WorldGen.Region.SettlementClass.Town && st.Markets!.BookOf[i] == i);
        var seller = wv.Stages[^1];
        var ord = Do(IndustryDesk.Order(new EntityId(seller.Business), "town", dest, "cart", 2, 1, true, Sec())); output.WriteLine($"order: {ord.Code} {ord.Text}");
        wv = IndustryViews.Build(iw, works.Owner, day()).Works.Single(x => x.Id == works.Id);
        var quote = wv.Stages.FirstOrDefault(x => x.Caravan != null)?.Caravan;
        if (quote != null) { output.WriteLine($"quote: ok {quote.Ok} {quote.Why}, to {quote.Dest}, margin {IndustryViews.Silver(quote.MarginMites)}, gate {IndustryViews.Silver(quote.GateNetMites)}"); Reach(6); }
        for (int d = 0; d < 30; d++) RunDay();
        wv = IndustryViews.Build(iw, works.Owner, day()).Works.Single(x => x.Id == works.Id);
        output.WriteLine($"payback {wv.PaybackDays} days, repaid {wv.PaidPm / 10}%, cumulative {IndustryViews.Silver(wv.CumulativeNetMites)}, bottleneck {wv.Bottleneck}, trips {wv.CaravanTrips}, capital need {IndustryViews.Silver(wv.CapitalNeedMites)}");
        if (reached >= 6 && wv.PaybackDays >= 0) Reach(7);

    done:
        output.WriteLine($"reached step {reached} of {Steps.Length}: {(reached == 0 ? "none" : Steps[reached - 1])}");
        Assert.True(reached >= BalanceRatchets.Bl3StepsReached, $"reached step {reached}, the ratchet is {BalanceRatchets.Bl3StepsReached}");
        Exact("the run");
    }
}
