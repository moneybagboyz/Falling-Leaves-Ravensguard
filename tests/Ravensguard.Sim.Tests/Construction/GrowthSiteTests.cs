using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Construction;

/// <summary>P5-D: background growth made visible (spec 4.9): BC-17, the lord's cottage posted as civic work the hero takes by hand, and the unvisited chunk that stays Phase 3.</summary>
public class GrowthSiteTests(ITestOutputHelper output)
{
    private static long Now0 => (long)EconFixture.Pop.NowYear * TimeConst.DaysPerYear * 86400 + 3 * TimeConst.DaysPerSeason * 86400L;     // the first day of a season, 00:00

    private static (int S, RectM Rect, byte Facing) Plot(ContractRig r)
    {
        var reg = r.L.Region;
        for (int s = 0; s < reg.Settlements.Count; s++)
            if (reg.Settlements[s].Class == SettlementClass.Village && RegionGrowth.FindPlot(reg, s, 16, 38, 30, [], out var rect, out byte f)) return (s, rect, f);
        throw new InvalidOperationException("no village has room");
    }

    /// <summary>A household of the settlement that is not the lord's, with adults and coin.</summary>
    private static int Household(ContractRig r, int s)
    {
        var hht = r.L.W.Pop.Households; int lord = r.Growth.LordOf(s);
        for (int h = 0; h < hht.Count; h++)
            if (h != lord && hht.MemberCount[h] >= 2 && hht.Settlement[h] == s && hht.Class[h] != SocialClass.Gentry) return h;
        throw new InvalidOperationException("no household");
    }

    /// <summary>A cottage project of Phase 3 a season old, 40% done, every material bought (the tuning's 400 kg of poles, 1.5 t of clay, 3 t of thatch).</summary>
    private static BuildProject Project(ContractRig r, int s, RectM rect, byte f, int payer, long hoursDone = 100, string blueprint = "test_cottage")
    {
        var g = r.Growth; var t = g.Tuning;
        var p = new BuildProject
        {
            Serial = g.State.NextSerial++, Kind = ProjectKind.Cottage, Settlement = s, StartDay = (int)(Now0 / 86400) - TimeConst.DaysPerSeason, Payer = payer, Rect = rect, Facing = f,
            Use = LotUse.Cottage, Dwelling = DwellingType.Cottage, Blueprint = blueprint, Wealth = 0, Beds = t.CottageBeds, HoursNeeded = 250, HoursDone = hoursDone,
            MaterialG = [.. t.CottageMaterials.Select(m => (long)m.Kg * 1000)],
        };
        g.State.Projects.Add(p);
        return p;
    }

    [Fact]
    public void BC17_AGrowthCottage40PercentDone_InAVisitedChunk_IsASite_AndFinishesAsTheSameLotRecord()
    {
        using var r = ContractRig.Make();
        using var twin = ContractRig.Make();                                   // the same world where the player never came: Phase 3
        var (s, rect, f) = Plot(r);
        int hh = Household(r, s);
        var p = Project(r, s, rect, f, hh); Project(twin, s, rect, f, hh);
        r.Visit(new RectM(rect.X - 8, rect.Y - 8, rect.W + 16, rect.H + 16));
        long t = Now0 + 6 * 3600;
        r.W.GrowthSites!.LinkSeen(new GameTime(t));
        var link = r.W.Book.Growth[p.Serial];
        var site = r.W.Site(link.Site)!;
        Assert.Equal("test_cottage", site.Blueprint);
        Assert.NotEmpty(r.W.GhostsOf(site.Id));
        // built pieces' hours = 100.2 of 250.5 (the piece in hand counts what was done on it)
        long builtUnits = r.W.Book.Built.Values.Where(b => b.Site == site.Id).Sum(b => r.W.Costs[b.Piece].WorkUnits) + r.W.GhostsOf(site.Id).Sum(g => g.WorkDone);
        long planUnits = builtUnits + r.W.LabourUnits(site);
        Assert.Equal(250_500L * 3_600_000, planUnits);
        Assert.Equal(100_200L * 3_600_000, builtUnits);
        int walls = r.W.Book.Built.Values.Count(b => b.Site == site.Id);
        var started = r.W.GhostsOf(site.Id).Where(g => g.Materialised).ToList();
        output.WriteLine($"{walls} pieces built, {started.Count} started ({string.Join(",", started.Select(g => g.Piece))}); ghosts {r.W.GhostsOf(site.Id).Count()}");
        Assert.Equal(15, walls); Assert.Single(started);
        // pile = bought grams minus the built pieces' (and the started piece's) materials
        var pile = r.W.PileOf(site); var goods = r.L.W.Econ.Goods;
        Assert.Equal(400_000 - 16 * 16_000, pile.Qty(goods.IdOf("logs")));
        Assert.Equal(1_500_000 - 16 * 79_000, pile.Qty(goods.IdOf("clay")));
        Assert.Equal(3_000_000, pile.Qty(goods.IdOf("thatch")));
        // the visited chunk learns of the built walls as ID-keyed deltas (never regenerated)
        var key = new ChunkKey(ChunkStreamer.FloorDiv(rect.X + rect.W / 2, 32), ChunkStreamer.FloorDiv(rect.Y + rect.H / 2, 32), 0);
        Assert.True(r.Chunks.Keys.Sum(k => r.Chunks.DeltaOpCount(k)) >= 15);
        r.AssertMass(); r.AssertCoin();

        // the household's own and hired hands build the rest (Phase 3's hours a day); what Phase 3 never bought (planks, nails, a kilo of clay) the household brings
        foreach (var (g, q) in new[] { ("timber_planks", 30_000L), ("nails", 300L), ("clay", 1_000L) }) pile.Create(goods.IdOf(g), q, CreateReason.Import);
        r.W.Refresh(site);
        output.WriteLine($"crew {site.Crew.Count}: {site.Crew.Count(m => m.Class == CrewClass.Follower)} own, {site.Crew.Count(m => m.Class == CrewClass.Labourer)} hired");
        Assert.Contains(site.Crew, m => m.Class == CrewClass.Follower && m.DayCapSec == 2 * 3600);
        Assert.Contains(site.Crew, m => m.Class == CrewClass.Labourer && m.DayCapSec == 4 * 3600);
        long seasonEnd = Now0 + TimeConst.DaysPerSeason * 86400L;
        r.Run(ref t, Now0 + 30 * 86400L);
        Assert.Equal(SiteState.Finished, site.State);
        long done = r.W.Book.Built.Values.Where(b => b.Site == site.Id).Max(b => b.BuiltSec);
        output.WriteLine($"finished on day {(done - Now0) / 86400.0:F1} of the season; wages paid {site.PaidMites}");
        // the next season: both worlds finish the cottage as Phase 3 does
        foreach (var w in new[] { r, twin }) w.Growth.Seasonal(new GameTime(seasonEnd), w.Outbox(), -1);
        var mine = r.Growth.State.Lots.Single(l => l.Rect == rect);
        var theirs = twin.Growth.State.Lots.Single(l => l.Rect == rect);
        Assert.Equal(theirs, mine);
        Assert.Empty(r.W.Book.Growth);
        Assert.DoesNotContain(r.W.Book.Built.Values, b => b.Site == site.Id);              // the structure record is dropped: it equals the generated lot
        // the chunk holds the generated house (the deltas of the build and of the finished lot)
        var now = r.Chunks.Rebuild(key)!; var gen = r.Gen.GenerateLevels(key.X, key.Y).Levels[0];
        for (int i = 0; i < 1024; i++)
        {
            int wx = key.X * 32 + i % 32, wy = key.Y * 32 + i / 32;
            if (wx >= rect.X && wx < rect.Right && wy >= rect.Y && wy < rect.Bottom) Assert.Equal(TileCode.Structure(gen.Tiles.GetValueOrDefault(i)), TileCode.Structure(now.Tiles.GetValueOrDefault(i)));
        }
        r.AssertMass(); r.AssertCoin();
    }

    [Fact]
    public void AGrowthProject_InAChunkNobodyVisited_StaysPhase3()
    {
        using var r = ContractRig.Make();
        var (s, rect, f) = Plot(r);
        var p = Project(r, s, rect, f, Household(r, s));
        r.W.GrowthSites!.LinkSeen(new GameTime(Now0));
        Assert.Empty(r.W.Book.Growth);
        Assert.Empty(r.W.Book.Sites);
        r.Growth.Seasonal(new GameTime(Now0 + TimeConst.DaysPerSeason * 86400L), r.Outbox(), -1);
        Assert.Contains(r.Growth.State.Lots, l => l.Rect == rect);
        Assert.DoesNotContain(r.Growth.State.Projects, x => x.Serial == p.Serial);
    }

    [Fact]
    public void TheLordsCottage_IsPostedOnTheBoard_TheHeroTakesItAndBuildsByHand_ThenABuilderFinishesIt()
    {
        using var r = ContractRig.Make(crowns: 100);
        var (s, rect, f) = Plot(r);
        int lord = r.Growth.LordOf(s);
        var p = Project(r, s, rect, f, lord, hoursDone: 0);
        r.Visit(new RectM(rect.X - 8, rect.Y - 8, rect.W + 16, rect.H + 16));
        long t = Now0 + 6 * 3600;
        r.W.GrowthSites!.LinkSeen(new GameTime(t));
        var c = r.W.Board().Single();
        var site = r.W.Site(c.Site)!;
        Assert.Equal(ContractKind.Civic, c.Kind);
        Assert.Empty(site.Crew);                                                    // nobody builds until someone takes the work
        Assert.Equal(c.LabourMites, r.Led.Balance(r.Led.Open(PurseKind.Chest, c.Escrow)).Mites);   // the wages in escrow
        output.WriteLine($"posted: wages {c.LabourMites} for {r.W.LabourUnits(site) / 3_600_000_000.0:F1} h");
        // the hero takes it as a job and works by hand: a stranger to the land may build there because he is the contractor
        Assert.True(r.W.Take(r.Hero, PurseKind.Person, c.Id, t).Ok);
        Assert.Equal(ContractState.Signed, c.State);
        long coin0 = r.Led.Balance(r.L.HeroPurse).Mites;
        var tools = r.W.ToolQualities(["axe", "spade"]);
        int pieces = 0;
        for (int day = 0; day < 3; day++)
        {
            long from = Now0 + day * 86400L + 6 * 3600, until = from + 12 * 3600;
            while (from < until)
            {
                var g = r.W.GhostsOf(site.Id).Where(x => x.State is GhostState.Ready or GhostState.InProgress).OrderBy(x => x.Z).ThenBy(x => x.Id).FirstOrDefault();
                if (g == null) break;
                var res = r.W.HeroWork(r.Hero, PurseKind.Person, -1, g.Id, from, Math.Min(until, from + 3600), g.X, g.Y, g.Z, tools);
                Assert.True(res.Ok, res.Key);
                from += 3600;
            }
            pieces = site.PiecesBuilt;
            r.Run(ref t, Now0 + (day + 1) * 86400L);
        }
        output.WriteLine($"the hero built {pieces} pieces by hand in three days");
        Assert.True(pieces > 3);
        // he gives it up: back on the board; after the notice a builder of the region takes it and finishes it
        Assert.True(r.W.Drop(r.Hero, c.Id, t).Ok);
        Assert.Equal(ContractState.Open, c.State);
        r.Run(ref t, t + (r.W.ContractRules.NoticeDays + 1) * 86400L);
        Assert.Equal(ContractState.Signed, c.State);
        Assert.NotEqual(r.Hero, c.Contractor);
        foreach (var (g, q) in new[] { ("timber_planks", 30_000L), ("nails", 300L), ("clay", 10_000L) }) r.W.PileOf(site).Create(r.L.W.Econ.Goods.IdOf(g), q, CreateReason.Import);
        r.W.Refresh(site);
        r.Run(ref t, t + 20 * 86400L);
        Assert.Equal(SiteState.Finished, site.State);
        Assert.Equal(ContractState.Done, c.State);
        Assert.Equal(coin0, r.Led.Balance(r.L.HeroPurse).Mites);                   // he gave it up before half the work: unpaid
        Assert.Equal(c.LabourMites, c.ReleasedMites);
        r.AssertMass(); r.AssertCoin();
    }
}
