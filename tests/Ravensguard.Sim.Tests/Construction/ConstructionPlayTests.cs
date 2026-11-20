using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Land;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Construction;

/// <summary>BC-7 (resume by anyone the land allows), the land rule, claims gaining a dwelling, hiring, hauling through the job board with real carry limits, and the decided pace.</summary>
public class ConstructionPlayTests(ITestOutputHelper output)
{
    private const long T0 = ConstructionRig.Day10;

    /// <summary>A construction world over a land rig's economy with the land's hooks, as the runtime wires it.</summary>
    private static ConstructionWorld Over(LandRig lr)
    {
        var cw = new ConstructionWorld(new ConstructionBook(), lr.W.Econ, PieceCosts.Of(EconFixture.Content)) { Pop = lr.W.Pop, Content = EconFixture.Content, Seed = 1234 };
        ConstructionWiring.HookLand(cw, lr.L, lr.W.Pop);
        cw.Chronicle = (when, type, subjects, causes, data) => lr.C(when, type, subjects, causes, data);
        cw.PayeeOf = p => lr.Led.TryFind(PurseKind.Person, p, out var pp) ? pp : null;
        return cw;
    }

    private static void Work(ConstructionWorld cw, EntityId site, int n, ref long t, int days)
    {
        for (int i = 0; i < n; i++) cw.AddWorker(site, EntityId.Make(IdKind.Person, 6_000_000UL + (ulong)i + (ulong)cw.Book.Sites.Count * 50), -1, CrewClass.Skilled, 0, t);
        for (int d = 0; d < days && cw.GhostsOf(site).Any(); d++) { t += 86400; cw.AdvanceTo(t); }
    }

    private static void StockAll(ConstructionWorld cw, EntityId site)
    {
        var s = cw.Site(site)!; var pile = cw.PileOf(s);
        foreach (var (g, q) in cw.Needed(s)) pile.Create(g, q, CreateReason.Import);
        cw.Refresh(s);
    }

    [Fact]
    public void BC7_OwnerDiesMidSite_TheSitePassesWithTheDeed_TheHeirResumes_AStrangerIsRefused()
    {
        using var lr = LandRig.Make(crowns: 2000);
        var cw = Over(lr);
        int town = lr.Town();
        var lot = lr.L.Listings(town).First(x => x.Lot < 0);
        Assert.True(lr.L.Buy(lr.Hero, PurseKind.Person, town, lot.Lot, lot.Rect, 0, 10, lr.C).Ok);
        int x0 = lot.Rect.X + 1, y0 = lot.Rect.Y + 1;
        long t = T0;
        // a stranger may not build on the hero's lot; the hero may
        var stranger = lr.W.Pop.People.IdOf(Enumerable.Range(0, lr.W.Pop.People.Count).First(i => lr.W.Pop.People.IsAlive(i)));
        var no = cw.Place(stranger, PurseKind.Person, -1, [new PlaceCell(x0, y0, 0, "wall_wattle")], t);
        Assert.False(no.Ok);
        Assert.Equal("ld_may_theirs", no.Refused.Single().Key);
        var pr = cw.PlacePlan(lr.Hero, PurseKind.Person, -1, "test_cottage", x0, y0, 0, false, t);
        Assert.True(pr.Ok, pr.Key + string.Join(",", pr.Refused.Select(r => r.Key)));
        Assert.Equal(LandIds.Of(HoldingKind.Lot, lr.L.RegionId, LandIds.LotSubject(town, lr.Region.Settlements[town].Plan.Lots.Count - 1)), cw.Site(pr.Site)!.Deed);
        StockAll(cw, pr.Site);
        Work(cw, pr.Site, 2, ref t, 2);
        int built = cw.Book.Built.Count;
        Assert.True(built > 0 && cw.GhostsOf(pr.Site).Any());
        // the hero dies: his site stops with everything on it; the deed passes to his heir (inheritance moves the deed; P5-H settles estates)
        cw.Orphan(lr.Hero, t);
        Assert.Equal(SiteState.Abandoned, cw.Site(pr.Site)!.State);
        var heir = EntityId.Make(IdKind.Person, 5_555_555);
        lr.L.Book.Deeds[cw.Site(pr.Site)!.Deed].Holder = heir;
        t += 86400; cw.AdvanceTo(t);
        Assert.Equal(built, cw.Book.Built.Count);                                       // half-built work stays
        var refused = cw.SetPaused(stranger, PurseKind.Person, -1, pr.Site, false, t);
        Assert.False(refused.Ok);
        Assert.Equal("ld_may_theirs", refused.Key);
        var resumed = cw.SetPaused(heir, PurseKind.Person, -1, pr.Site, false, t);
        Assert.True(resumed.Ok, resumed.Key);
        Assert.Equal(heir, cw.Site(pr.Site)!.Owner);
        Work(cw, pr.Site, 3, ref t, 20);
        Assert.Empty(cw.GhostsOf(pr.Site));
        Assert.Equal(80, cw.Book.Built.Count);
    }

    [Fact]
    public void AClaimOnWildLand_GainsItsImprovementWhenADwellingStandsOnIt_AndBecomesAFreeholdDeed()
    {
        using var lr = LandRig.Make();
        var cw = Over(lr);
        var (x, y) = lr.ClaimPoint(wild: true);
        var stake = lr.L.Stake(lr.Hero, PurseKind.Person, x, y, false, 10, lr.C);
        Assert.True(stake.Ok, stake.Key);
        var claim = lr.L.Book.Claims.Single();
        Assert.Equal(8, claim.Tiles.Length);
        long t = T0;
        var pr = cw.PlacePlan(lr.Hero, PurseKind.Person, -1, "test_cottage", x, y, 0, false, t);
        Assert.True(pr.Ok, pr.Key + string.Join(",", pr.Refused.Select(r => r.Key)));
        StockAll(cw, pr.Site);
        Work(cw, pr.Site, 6, ref t, 10);
        Assert.Empty(cw.GhostsOf(pr.Site));
        Assert.Equal(0, claim.Improvements);                                               // a roofed house without a hearth and beds is no dwelling
        var furn = cw.Place(lr.Hero, PurseKind.Person, -1, [new PlaceCell(x + 2, y + 2, 0, "hearth_central"), new PlaceCell(x + 5, y + 1, 0, "straw_pallet"), new PlaceCell(x + 6, y + 1, 0, "straw_pallet")], t);
        Assert.True(furn.Ok);
        StockAll(cw, furn.Site);
        Work(cw, furn.Site, 2, ref t, 3);
        Assert.Equal(1, claim.Improvements);
        Assert.Equal(16, claim.Tiles.Length);
        Assert.Equal(ClaimState.Deeded, claim.State);
        Assert.Equal(Tenure.Freehold, lr.L.Book.Deeds[claim.DeedId].Tenure);
        // another piece in the same house does not improve it twice
        var more = cw.Place(lr.Hero, PurseKind.Person, -1, [new PlaceCell(x + 6, y + 3, 0, "straw_pallet")], t);
        StockAll(cw, more.Site); Work(cw, more.Site, 1, ref t, 2);
        Assert.Equal(1, claim.Improvements);
    }

    [Fact]
    public void HiringTakesLabourersAndCraftsmenFromTheSettlementsNearTheSite_AtProductionsWages()
    {
        var r = ConstructionRig.Make(crowns: 100);
        var reg = EconFixture.Pop.Region;
        var town = reg.Settlements.First(s => s.Class == SettlementClass.Town);
        long t = (long)EconFixture.Pop.NowYear * TimeConst.DaysPerYear * 86400 + 7 * 3600;          // people's ages are counted from the calendar
        var pr = r.W.Place(r.Hero, PurseKind.Person, -1, [new PlaceCell(town.CenterX + 3, town.CenterY + 3, 0, "wall_timber")], t);
        Assert.True(pr.Ok);
        var res = r.W.Hire(r.Hero, -1, pr.Site, 3, false, t);
        Assert.True(res.Ok, res.Key);
        var crew = r.W.Site(pr.Site)!.Crew;
        Assert.Equal(3, crew.Count);
        Assert.All(crew, m => { Assert.Equal(CrewClass.Labourer, m.Class); Assert.Equal(150, m.WageMitesPerDay); Assert.True(EconFixture.Pop.People.Occupation[m.PersonIndex] is Occupation.None or Occupation.Labourer); });
        Assert.True(r.W.Hire(r.Hero, -1, pr.Site, 2, true, t).Ok);
        Assert.Equal(2, crew.Count(m => m.Class == CrewClass.Skilled && m.WageMitesPerDay == 350));
        Assert.Equal(5, crew.Select(m => m.Person).Distinct().Count());
        // hired in working hours: the day's wages are put up at once (3 x 150 + 2 x 350)
        Assert.Equal(450 + 700, r.Econ.Ledger.Balance(r.Econ.Ledger.Open(PurseKind.Chest, r.W.Site(pr.Site)!.Escrow)).Mites);
        output.WriteLine(res.Args[1].ToString());
        r.AssertCoin();
    }

    [Fact]
    public void MaterialsAreHauledByPeopleThroughTheJobBoard_InCarriersLoads()
    {
        var r = ConstructionRig.Make(crowns: 50);
        var pr = r.W.Place(r.Hero, PurseKind.Person, -1, [new PlaceCell(1200, 1200, 0, "post_timber"), new PlaceCell(1202, 1200, 0, "post_timber"), new PlaceCell(1204, 1200, 0, "post_timber")], T0);
        Assert.True(pr.Ok);
        // 180 kg of logs in the hero's barn 200 m away; nothing in the pile
        var barn = EntityId.Make(IdKind.Other, 4_444_444);
        r.Econ.OpenStore(StoreKind.Barn, barn, enforce: false).Create(r.G("logs"), 180_000, CreateReason.Import);
        var posted = r.W.PostHaul(pr.Site, barn, 1200, 1400, r.G("logs"), 180_000, T0);
        Assert.True(posted.Ok, posted.Key);
        var jobs = r.Jobs.Jobs.Values.Where(j => j.Kind == OddJobKind.Haul).ToList();
        Assert.Equal(3, jobs.Count);                                                          // 70 + 70 + 40 kg: one carrier's load each
        Assert.All(jobs, j => Assert.True(j.P5 <= 70_000));
        Assert.Equal(70_000, r.W.HaulLoadG);
        long coinBefore = r.Econ.Ledger.Balance(r.HeroPurse).Mites;
        Assert.True(coinBefore < 5000);                                                       // the haul wages are escrowed from the payer
        var crew = r.Crew(pr.Site, 2);
        Assert.All(r.W.GhostsOf(pr.Site), g => Assert.Equal(GhostBlock.Materials, g.Block));
        long t = T0;
        for (int i = 0; i < 48 && r.W.GhostsOf(pr.Site).Any(); i++) { t += 3600; r.W.AdvanceHauls(t); r.W.AdvanceTo(t); }
        Assert.Contains(jobs, j => crew.Contains(j.Worker));                                  // the idle crew carried them
        Assert.All(jobs, j => Assert.Equal(JobState.Done, j.State));
        Assert.Empty(r.W.GhostsOf(pr.Site));
        Assert.Equal(0, r.Econ.OtherStores[barn].Qty(r.G("logs")));
        r.AssertMass(); r.AssertCoin();
    }

    [Fact]
    public void Pace_ACottageAlone_About25Days_WithSix_About4()
    {
        foreach (var (n, lo, hi) in new[] { (1, 24.0, 28.0), (6, 4.0, 5.6) })
        {
            var r = ConstructionRig.Make(); long t = T0;
            var pr = r.W.PlacePlan(r.Hero, PurseKind.Person, -1, "test_cottage", 1300, 1300, 0, false, t);
            r.StockAll(pr.Site);
            r.Crew(pr.Site, n, skill: 30);
            long end = 0;
            for (int h = 0; h < 40 * 24 && r.W.GhostsOf(pr.Site).Any(); h++) { t += 3600; r.W.AdvanceTo(t); }
            end = r.W.Book.Built.Values.Max(b => b.BuiltSec);
            // working days used: whole days plus the part of the last
            double days = (end - T0) / 86400 + ((end - T0) % 86400) / (10.0 * 3600);
            output.WriteLine($"crew of {n}: finished {days:0.00} working days after the start");
            Assert.InRange(days, lo, hi);
        }
    }
}
