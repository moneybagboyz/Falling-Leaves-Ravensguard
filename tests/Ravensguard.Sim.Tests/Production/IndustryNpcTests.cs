using Ravensguard.Content;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Production.Industry;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.WorldGen.Region;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>
/// P7-IN5 (industries 5.12, 10): IN-20 (the L1 payback measured in a run on seed 1234 near Ranunfield, and the slider at 1x and 5x), IN-22 (the NPC founder), IN-24 (two years of three works and an own caravan in flight, saved and loaded), and the rules the first
/// full-world run of a first-tier works showed were missing (a starved stage lends its hands, a gate sale books its income, a byproduct passes the packing stage).
/// </summary>
public class IndustryNpcTests(ITestOutputHelper output)
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

    // ================================================================== the rig: a whole runtime on seed 1234 with the hero's works at Ranunfield

    /// <summary>
    /// What IN-20, IN-22 and IN-24 share: the whole runtime of seed 1234, the hero's works at the town (Ranunfield) crewed through a hiring hall (the town's idle hands at the labour market's start skill, because the labour market of a fully employed town has no idle
    /// to give: see industries.md's Notes) and the region's producers answering the works' demand for fuel and fodder (a faucet that keeps the book's stock of charcoal, firewood, straw and hay up, created as seed).
    /// </summary>
    private sealed class Rig : IDisposable
    {
        /// <summary>The skill the labour market gives a new hand (Phase 2's start skill).</summary>
        public const int HireSkill = LabourMarket.StartSkill;
        public SeamWorld W; public WorldState State; public WorldRuntime Rt; public IndustryWorld Iw; public HeroDesk Desk = new(); public HeroContext Ctx; public int Town = 0;
        public readonly List<int> Pool = [];
        private readonly bool _owns = true;

        public Rig(bool npc = false, int settleDays = 60)
        {
            (W, State) = Game();
            Rt = new WorldRuntime(State, W.Content, new MemoryRegionPager());
            Iw = State.HomeRecords!.Sim.Get<IndustryWorld>()!; Iw.NpcDisabled = !npc;
            State.HomeRecords.Economy.Growth.CharcoalCrews = false;      // P7-EA3e: the rig's faucet is the region's burners answering; the growth's own crews would also draw the town's idle hands and move the books the payback is read in
            if (settleDays > 0) Rt.AdvanceTo(GameTime.FromGameHours(Rt.HourDone + 24L * settleDays));
            Ctx = new HeroContext(State, State.Party!, W.Content);
            State.Economy!.Ledger.Mint(Ctx.Purse, new Coin(4_000_000), SourceReason.Seed);
        }

        private Rig(SeamWorld w, WorldRuntime rt, bool npcDisabled)
        {
            W = w; Rt = rt; State = rt.State; Iw = State.HomeRecords!.Sim.Get<IndustryWorld>()!; Iw.NpcDisabled = npcDisabled;
            State.HomeRecords.Economy.Growth.CharcoalCrews = false;      // a runtime loaded from a save: the rig's switch is not saved, so the resumed run is switched the same way as the straight one
            Ctx = new HeroContext(State, State.Party!, W.Content); _owns = false;
        }
        /// <summary>A rig over a runtime that was loaded from a save (it does not own the runtime).</summary>
        public static Rig Wrap(SeamWorld w, WorldRuntime rt, bool npcDisabled) => new(w, rt, npcDisabled);

        public long Sec => Rt.HourDone * 3600;
        public int Day => (int)(Rt.HourDone / 24);
        public void Issue(HeroCommand c) { Desk.Issue(c); Desk.ApplyDue(Ctx, Rt.HourDone); }
        public HeroResult Last => Desk.View!.Results[^1];

        public void FreeHands(int n)
        {
            var pt = State.Population.People; var prod = State.Production!; int freed = 0;
            foreach (var b in prod.Businesses.ToList())
            {
                if (prod.KindOf(b).Stage != null) continue;
                for (int i = b.Staff.Count - 1; i >= 0 && freed < n; i--) { int p = b.Staff[i]; if (pt.Occupation[p] is Occupation.Spinner or Occupation.Labourer && pt.AgeYears(p, Day) >= 18 && pt.Status[p] == PersonStatus.Alive) { Pool.Add(p); prod.DropStaff(b, i, true); freed++; } }
            }
        }

        public Works Found(string chain, params int[] crew)
        {
            Desk.Issue(IndustryDesk.Found(chain, Town, 1_000_000, Sec)); Desk.ApplyDue(Ctx, Rt.HourDone);
            Assert.All(Desk.View!.Results, r => Assert.True(r.Ok, r.Text.ToString()));
            var wk = State.Industry!.Works[^1];
            for (int i = 0; i < crew.Length && i < wk.Stages.Count; i++) Iw.SetCrew(wk.Stages[i].Business, crew[i]);
            wk.BuildingsCostMites = Iw.L1BillMites(chain, Town);                     // what a founder would pay at the town's book today
            Hall(wk);
            return wk;
        }

        /// <summary>The hiring hall: every post of the works' stages that is empty is filled from the pool at the labour market's start skill.</summary>
        public void Hall(Works wk)
        {
            var prod = State.Production!; var pt = State.Population.People;
            foreach (var st in wk.Stages)
            {
                var bz = prod.ById(new EntityId(st.Business))!; if (!Iw.SlotsFor(bz, out var slots)) continue;
                var kd = Iw.KindOf(st);
                foreach (var (occ, cnt, _) in slots)
                {
                    int have = bz.Staff.Count(p => pt.Occupation[p] == occ);
                    for (; have < cnt; have++)
                    {
                        if (Pool.Count == 0) FreeHands(60);
                        if (Pool.Count == 0) return;
                        int p = Pool[^1]; Pool.RemoveAt(Pool.Count - 1);
                        pt.Occupation[p] = occ; pt.SetSkill(p, 0, kd.Skill == Skill.None ? Skill.Farming : kd.Skill, HireSkill); prod.Employ(bz, p, occ);
                    }
                }
            }
        }

        /// <summary>Keeps a good's stock at the town's book at a floor: real goods created as seed and listed at any price (the region's producers answering the demand).</summary>
        public void Stock(string good, long minG)
        {
            var book = State.Markets!.BookForSettlement(Town); var id = State.Economy!.Goods.IdOf(good); var gb = book.Goods[id];
            if (gb.StockUnits >= minG || book.Merchants.Count == 0) return;
            var m = book.Merchants[0]; long add = minG - gb.StockUnits;
            m.Store.Create(id, add, CreateReason.Seed); book.List(m, id, add, 0, Day, true);
        }

        /// <summary>One game day: the faucets (and the hall for the hero's works) at hour 0, then 24 hours.</summary>
        public void RunDay(bool faucet = true, bool hall = true)
        {
            // the region's burners answer 26 days of the book's own demand for charcoal (P7-CN1: the smithies also cupel gold and silver now, which tripled it)
            if (faucet) { Stock("charcoal", Math.Max(12_000_000, 26 * State.Markets!.BookForSettlement(Town).Goods[State.Economy!.Goods.IdOf("charcoal")].Demand)); Stock("firewood", 60_000_000); Stock("straw", 400_000); Stock("hay", 400_000); }
            if (hall) foreach (var wk in State.Industry!.Works) if (!wk.Closed && !Iw.IsNpc(wk)) Hall(wk);
            Rt.AdvanceTo(GameTime.FromGameHours(Rt.HourDone + 24));
        }

        public long NetOver(Works wk, int days) => wk.Stages.Sum(x => x.Net(Day, days));
        public (long Coin, bool Mass) Audit() => (State.Economy!.Ledger.Audit().InPurses.Mites + State.ContinentState!.TotalCoin() - Struck(State.Economy.Ledger), State.Economy.MassAuditHolds());   // P7-CN1: the mints strike the hearths' silver meanwhile
        public void Dispose() { if (_owns) Rt.Dispose(); }
    }

    private static readonly int[] IronCrew = [3, 2, 1, 5, 1], ClayCrew = [2, 2, 3, 1, 2, 1], WoodCrew = [2, 1, 1, 8, 1, 1];

    // ================================================================== the sheets, as the founders read them

    [Fact]
    public void NpcSheet_AtTheBasePrices_IsTheWorkedExample()
    {
        var (w, st) = Game();
        using var rt = new WorldRuntime(st, w.Content, new MemoryRegionPager());
        var iw = rt.State.HomeRecords!.Sim.Get<IndustryWorld>()!; var cat = rt.State.Economy!.Goods;
        iw.NpcPrice = (_, g) => cat.BasePriceMilliMites(g);
        var iron = iw.SheetOf("iron", 0); var clay = iw.SheetOf("clay", 0); var wood = iw.SheetOf("wood", 0);
        output.WriteLine($"{iron}\n{clay}\n{wood}");
        Assert.InRange(iron.NetPerDayMites, 845, 860);                                   // 5.11's 852 a day
        Assert.InRange(clay.NetPerDayMites, 175, 190);                                   // 455 a working day, 45 of 60: 181 a day over the year
        Assert.True(wood.Pays);
        Assert.Equal(iw.BaseBillMites("iron"), iw.L1BillMites("iron", 0));
    }

    // ================================================================== IN-20

    [Fact]
    public void IN20_AWellRunL1Works_RepaysItsBuildings_InOneToTwoHundredDays_InARunOnSeed1234()
    {
        using var rig = new Rig();
        rig.FreeHands(160);
        var works = new[] { rig.Found("iron", IronCrew), rig.Found("clay", ClayCrew), rig.Found("wood", WoodCrew) };
        var coin0 = rig.Audit().Coin;
        output.WriteLine($"bills at the book: {string.Join(", ", works.Select(x => x.Chain + " " + x.BuildingsCostMites))}; founders' sheets: {string.Join("; ", works.Select(x => rig.Iw.SheetOf(x.Chain, rig.Town)))}");
        for (int d = 1; d <= 360; d++)
        {
            rig.RunDay();
            if (d % 90 == 0) { var (coin, mass) = rig.Audit(); Assert.Equal(coin0, coin); Assert.True(mass); }
        }
        foreach (var x in works)
        {
            long mean = x.CumulativeNetMites / 360; long payback = mean <= 0 ? long.MaxValue : x.BuildingsCostMites / mean;
            output.WriteLine($"{x.Chain}: bill {x.BuildingsCostMites}, net over 360 days {x.CumulativeNetMites} = {mean} a day, payback {payback} days ({payback / 60.0:F1} game years); last 120 days {rig.NetOver(x, 120) / 120} a day; cumulative net crossed the bill on day {(x.PaidBackDay < 0 ? -1 : x.PaidBackDay - x.StartedDay)}");
            long inPurse = rig.State.Economy!.Ledger.Balance(rig.State.Production!.ById(new EntityId(x.Id))!.Purse).Mites - 1_000_000;         // the capital went in at founding
            Assert.InRange(inPurse, x.CumulativeNetMites - 150_000, x.CumulativeNetMites + 150_000);                                              // the ring misses nothing the purse saw (what is still on the road is the difference)
            Assert.InRange(payback, 100, 200);                                           // IN-20 / economy-audit I1: 100 to 200 game days
            Assert.True(rig.NetOver(x, 120) > 0, x.Chain + " still earns at the end of the year");
        }
        Assert.Equal(0, works.Count(x => x.Closed));
    }

    /// <summary>The honest slider (5.5): the same iron works at 1x and 5x; the output follows it, the books' prices answer.</summary>
    [Fact]
    public void IN20_Slider_OneAndFiveTimes_TheIronWorks_MakesFiveTimesTheBloom_AndTheMarketAnswers()
    {
        (long Bloom, long Net, long Bill) Run(int pct)
        {
            using var rig = new Rig();
            rig.State.Difficulty = rig.State.Difficulty with { IndustryOutputPct = pct };
            rig.FreeHands(160);
            var wk = rig.Found("iron", IronCrew);                            // the crew bands cap the hands at 5x as at 1x: only the rates and capacities grow
            long made = 0;                                                  // the grams of ore the smeltery took through its furnace, day by day (the region's own bloomeries make bloom too)
            for (int d = 1; d <= 120; d++) { rig.RunDay(); made += rig.Iw.Status(wk.Stages[3].Business)?.MadeG ?? 0; }
            Assert.True(rig.Audit().Mass);
            return (made, wk.CumulativeNetMites, wk.BuildingsCostMites);
        }
        var one = Run(100); var five = Run(500);
        long p1 = one.Net <= 0 ? long.MaxValue : one.Bill * 120 / one.Net, p5 = five.Net <= 0 ? long.MaxValue : five.Bill * 120 / five.Net;
        output.WriteLine($"1x: {one.Bloom / 1000} kg of ore smelted in 120 days, net {one.Net}, payback {(p1 == long.MaxValue ? "never" : p1 + " days")}; 5x: {five.Bloom / 1000} kg, net {five.Net}, payback {(p5 == long.MaxValue ? "never" : p5 + " days")}");
        Assert.True(one.Bloom > 0);
        Assert.InRange(five.Bloom, one.Bloom * 3, one.Bloom * 6);                       // 5x the rates and capacities with the same hands
    }

    // ================================================================== the rules the first full-world run showed were missing

    private sealed class MiniKit
    {
        public ProdKit K; public IndustryBook Book = new(); public IndustryWorld World; public Works W = null!;
        public MiniKit(string chain)
        {
            K = new ProdKit(households: 60, markets: true);
            World = new IndustryWorld(Book, K.Sys, EconFixture.Content, 42);
            K.Fund(0, 2_000_000);
            Assert.True(World.Found(chain, 0, 0, K.Econ.HouseholdPurse[0], 1_000_000, 0).Ok); W = Book.Works[0];
        }
        private int _next = 1;
        public Business B(int stage) => K.Sys.ById(new EntityId(W.Stages[stage].Business))!;
        public void Crew(int stage, int n)
        {
            var k = World.KindOf(W.Stages[stage]); var b = B(stage);
            for (int i = 0; i < n; i++) { int p = _next++; K.People.Occupation[p] = k.Primary; K.People.SetSkill(p, 0, k.Skill == Skill.None ? Skill.Farming : k.Skill, 50); K.Sys.Employ(b, p, k.Primary); }
        }
        public long Have(int stage, string good) => B(stage).Store.Qty(K.Cat.IdOf(good));
        public void Day(long day) => World.Day(W, (int)day, GameTime.FromGameHours(day * 24 + 9));
    }

    [Fact]
    public void AStarvedSawMill_LendsItsHands_SoTheCrewlessYardPassesTheLogs()
    {
        var t = new MiniKit("wood");
        t.Crew(0, 2); t.Crew(1, 1); t.Crew(3, 2);                                          // fellers, the ox driver, a saw mill at its minimum with no logs yet; the yard has no hands of its own
        for (int d = 0; d < 3; d++) t.Day(d);
        Assert.True(t.Have(3, "logs") > 0, "the logs reached the saw mill");
    }

    [Fact]
    public void AByproduct_PassesTheHandlingStages_ToTheGate()
    {
        var t = new MiniKit("wood");
        var cat = t.K.Cat; ushort dust = cat.IdOf("sawdust"), planks = cat.IdOf("timber_planks");
        t.Crew(4, 1); t.Crew(5, 1);                                                         // packing and shipping at one hand each
        t.B(4).Store.Create(dust, 300_000, CreateReason.Seed); t.B(4).Store.Create(planks, 100_000, CreateReason.Seed);
        for (int d = 0; d < 3; d++) t.Day(d);
        Assert.Equal(0, t.Have(4, "sawdust")); Assert.Equal(0, t.Have(5, "sawdust"));      // sold, not stuck in the packing stage's store
        Assert.True(t.K.Markets!.BookForSettlement(0).Goods[dust].StockUnits >= 300_000, "the sawdust reached the book");
    }

    // ================================================================== IN-22: NPC founders

    /// <summary>A world with its first rich household (or one given 600 gp) and a book stocked for the bill, the founders looking at iron at the town only, the sheet's sale prices dialled until it says the payback asked for.</summary>
    private sealed class FounderRig : IDisposable
    {
        public Rig R; public int Household, Owner; public long Days;
        public FounderRig(int targetPaybackDays)
        {
            R = new Rig(npc: true, settleDays: 20);
            var iw = R.Iw; var pop = R.State.Population; var hht = pop.Households; var econ = R.State.Economy!; var led = econ.Ledger;
            int min = iw.Prod.Content.Industries.Settings.NpcMinCapitalMites;
            Household = -1;
            for (int h = 0; h < hht.Count && Household < 0; h++)
                if (hht.MemberCount[h] > 0 && hht.Head[h] >= 0 && pop.People.Status[hht.Head[h]] == PersonStatus.Alive && pop.People.HomeSettlement[hht.Head[h]] == 0 && pop.People.AgeYears(hht.Head[h], R.Day) >= 18 && led.Balance(econ.HouseholdPurse[h]).Mites >= min) Household = h;
            if (Household < 0)
            {
                for (int h = 0; h < hht.Count && Household < 0; h++)
                    if (hht.MemberCount[h] > 0 && hht.Head[h] >= 0 && pop.People.Status[hht.Head[h]] == PersonStatus.Alive && pop.People.HomeSettlement[hht.Head[h]] == 0 && pop.People.AgeYears(hht.Head[h], R.Day) >= 18) Household = h;
                led.Mint(econ.HouseholdPurse[Household], new Coin(600_000), SourceReason.Seed);
            }
            Owner = hht.Head[Household];
            iw.NpcSite = (chain, s) => chain == "iron" && s == 0;
            iw.IdleHands = (_, _) => 100;
            foreach (var (g, kg) in new[] { ("timber_planks", 12_000), ("bricks", 40_000), ("iron_bar", 100) }) R.Stock(g, kg * 1000L);
            var cat = econ.Goods; ushort bloom = cat.IdOf("iron_bloom"), ore = cat.IdOf("iron_ore"); var book = R.State.Markets!.BookForSettlement(0);
            long lo = 1, hi = 1_000_000;                                                    // the sale goods' prices as permille of the book's mid: more means a shorter payback
            var mids = new Dictionary<ushort, long>();                                    // the prices are frozen at the moment of the dial, so the books drifting cannot tip the sheet over
            long Mid(ushort g) { if (!mids.TryGetValue(g, out long m)) mids[g] = m = book.MidMicro(g); return m; }
            long Pay(long ff) { iw.NpcPrice = (_, g) => (g == bloom || g == ore) ? Mid(g) * ff / 1000 : Mid(g); long d = iw.SheetOf("iron", 0).PaybackDays; return d == 0 ? long.MaxValue : d; }
            for (int i = 0; i < 60 && hi - lo > 1; i++) { long mid = (lo + hi) / 2; if (Pay(mid) > targetPaybackDays) lo = mid; else hi = mid; }
            Pay(hi);
            Days = iw.SheetOf("iron", 0).PaybackDays;
        }
        public void Dispose() => R.Dispose();
    }

    [Fact]
    public void IN22_ARichHousehold_FoundsAnIronWorks_At2AndAHalfYearsExpectedPayback_AndDoesNotAtFour()
    {
        using (var t = new FounderRig(900))
        {
            var s = t.R.State; var econ = s.Economy!; var led = econ.Ledger;
            output.WriteLine($"expected payback {t.Days} days ({t.Days / 60.0:F1} game years); owner {t.Owner}, household {t.Household} holds {led.Balance(econ.HouseholdPurse[t.Household]).Mites}");
            Assert.InRange(t.Days, 860, 940);
            long coin0 = led.Audit().InPurses.Mites + s.ContinentState!.TotalCoin() - Struck(led);
            long hh0 = led.Balance(econ.HouseholdPurse[t.Household]).Mites; var cat = econ.Goods;
            long bricks0 = s.Markets!.BookForSettlement(0).Goods[cat.IdOf("bricks")].StockUnits;
            Assert.Empty(s.Industry!.Works);
            for (int d = 0; d < 14; d++) t.R.RunDay(faucet: false, hall: false);
            var w = Assert.Single(s.Industry!.Works);
            Assert.Equal(t.Owner, w.Owner); Assert.Equal("iron", w.Chain); Assert.Equal(0, w.Settlement);
            Assert.True(w.BuildingsCostMites > 100_000, "the bill was paid in coin: " + w.BuildingsCostMites);
            Assert.Equal(coin0, led.Audit().InPurses.Mites + s.ContinentState.TotalCoin() - Struck(led));                   // every mite moved between real purses
            Assert.True(econ.MassAuditHolds());
            Assert.True(s.Markets.BookForSettlement(0).Goods[cat.IdOf("bricks")].StockUnits < bricks0, "the bricks were bought from the book and built in");
            Assert.True(led.Balance(econ.HouseholdPurse[t.Household]).Mites < hh0 - w.BuildingsCostMites + 1_000_000, "the owner paid the bill and the capital");
            var head = s.Production!.ById(new EntityId(w.Id))!;
            Assert.True(led.Balance(head.Purse).Mites > 0, "the works has working capital");
            Assert.Equal(IronCrew[0], w.Stages[0].CrewTarget); Assert.Equal(IronCrew[3], w.Stages[3].CrewTarget);
            Assert.Single(s.Chronicle.ByType("works_founded"));
            output.WriteLine($"founded: bill {w.BuildingsCostMites}, purse {led.Balance(head.Purse).Mites}");
        }
        using (var t = new FounderRig(1440))
        {
            output.WriteLine($"expected payback {t.Days} days ({t.Days / 60.0:F1} game years)");
            Assert.InRange(t.Days, 1380, 1500);
            for (int d = 0; d < 14; d++) t.R.RunDay(faucet: false, hall: false);
            Assert.Empty(t.R.State.Industry!.Works);                                                          // four years is too long a wait
        }
    }

    [Fact]
    public void IN22_TheFounders_NeedTheHands_AndTheSite()
    {
        using (var t = new FounderRig(900))
        {
            t.R.Iw.IdleHands = (_, _) => 3;                                                                    // not enough idle hands to crew an iron works
            for (int d = 0; d < 14; d++) t.R.RunDay(faucet: false, hall: false);
            Assert.Empty(t.R.State.Industry!.Works);
        }
        using (var t = new FounderRig(900))
        {
            t.R.Iw.NpcSite = (c, s) => false;                                                                  // no place works the trade
            for (int d = 0; d < 14; d++) t.R.RunDay(faucet: false, hall: false);
            Assert.Empty(t.R.State.Industry!.Works);
        }
    }

    [Fact]
    public void IN22_AFounder_DrawsADividend_AndShutsAWorksThatEarnsNothing()
    {
        using var t = new FounderRig(900);
        var s = t.R.State; var iw = t.R.Iw; var led = s.Economy!.Ledger;
        for (int d = 0; d < 14; d++) t.R.RunDay(faucet: false, hall: false);
        var w = Assert.Single(s.Industry!.Works); var head = s.Production!.ById(new EntityId(w.Id))!;
        var ownerPurse = s.Economy.HouseholdPurse[t.Household];
        // a month in with a purse far over its reserve: the owner draws the rest
        w.StartedDay -= 40; led.Mint(head.Purse, new Coin(2_000_000), SourceReason.Seed);
        long owner0 = led.Balance(ownerPurse).Mites, purse0 = led.Balance(head.Purse).Mites;
        int pass = (t.R.Day / 6 + 1) * 6;
        iw.NpcPass(pass, GameTime.FromGameHours(pass * 24L + 7));
        Assert.True(led.Balance(ownerPurse).Mites > owner0, "a dividend reached the owner's household");
        Assert.True(led.Balance(head.Purse).Mites < purse0);
        // half a year of losses: he closes it
        w.StartedDay -= 400; w.CumulativeNetMites = -500_000;
        foreach (var st in w.Stages) for (int d = 0; d < 60; d++) st.Add(pass - d, WorkStage.Wages, 2_000);
        iw.Chron = Ravensguard.Sim.Rule.RuleChron.Of(s.Chronicle, s.Ids);
        iw.NpcPass(pass + 6, GameTime.FromGameHours((pass + 6) * 24L + 7));
        Assert.True(w.Closed); Assert.Single(s.Chronicle.ByType("works_closed"));
        Assert.True(head.State == BusinessState.Closed);
        Assert.True(s.Economy.MassAuditHolds());
    }

    // ================================================================== IN-24

    private static SaveFile SaveAt(WorldRuntime rt) => rt.AtBarrier(() => WorldSave.Capture(rt.State, "h", "0.1")).GetAwaiter().GetResult();

    private static SortedDictionary<string, byte[]> Sections(WorldRuntime rt) => rt.AtBarrier(() =>
    {
        var d = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (k, v) in WorldSave.Capture(rt.State, "h", "0.1").Sections) if (k != WorldSave.MetaSection) d[k] = v.ToArray();
        d["~continent"] = SaveCodec.SerializeCompressed(ContinentSave.Capture(rt.State.ContinentState!));
        foreach (var r in rt.Records.Resident) d[$"~records.{r.Coord.Id:D3}"] = System.Text.Encoding.ASCII.GetBytes(r.RecordsHash());
        return d;
    }).GetAwaiter().GetResult();

    /// <summary>IN-24: two years of the hero's three works and the founders (on), an own caravan on the road at the save; the resumed run equals the straight one, and coin and mass are exact throughout.</summary>
    [Fact, Trait("Category", "Soak")]
    public void IN24_TwoYears_ThreeWorks_AnOwnCaravanInFlightAtTheSave_SaveLoadContinueEqualsTheUninterruptedRun()
    {
        const int Days = 720;
        using var straight = new Rig(npc: true);
        straight.FreeHands(60);
        var iron = straight.Found("iron", IronCrew); straight.Found("clay", ClayCrew); straight.Found("wood", WoodCrew);
        var stage = iron.Stages[^1]; var biz = straight.State.Production!.ById(new EntityId(stage.Business))!;
        ushort bar = straight.State.Economy!.Goods.IdOf("iron_bar");
        biz.Store.Create(bar, 1_000_000, CreateReason.Seed);
        // a destination town and a trader, as IN4's rig does
        var reg = straight.State.Population.Region; int dest = -1; long best = long.MaxValue;
        for (int i = 1; i < reg.Settlements.Count; i++)
        {
            if (reg.Settlements[i].Class != SettlementClass.Town && reg.Settlements[i].Class != SettlementClass.Village) continue;
            if (straight.State.Markets!.BookForSettlement(i).Merchants.Count == 0 || straight.State.Markets.BookOf[i] != i) continue;
            if (straight.Iw.Caravans.PathOf(0, new CaravanOrder { Kind = DestKind.Town, Dest = i }, out _) is not { } path || path.LengthMm < 3_000_000) continue;
            if (path.LengthMm < best) { best = path.LengthMm; dest = i; }
        }
        Assert.True(dest >= 0);
        var pt = straight.State.Population.People;
        for (int p = 0; p < pt.Count; p++) if (pt.IsAlive(p) && pt.Status[p] == PersonStatus.Alive && pt.HomeSettlement[p] == 0 && pt.Sex[p] == Sex.Male && pt.AgeYears(p, straight.Day) is >= 25 and < 50 && pt.Occupation[p] == Occupation.None && pt.Household[p] >= 0) { pt.SetSkill(p, 0, Skill.Trading, 60); break; }
        var sid = new EntityId(stage.Business);
        straight.Issue(IndustryDesk.Order(sid, "town", dest, "ox", 1, 2, false, straight.Sec)); Assert.True(straight.Last.Ok, straight.Last.Text.ToString());
        straight.Issue(IndustryDesk.Send(sid, straight.Sec)); Assert.True(straight.Last.Ok, straight.Last.Text.ToString());
        for (int h = 0; h < 40; h++) straight.Rt.AdvanceTo(GameTime.FromGameHours(straight.Rt.HourDone + 1));
        var trip = Assert.Single(straight.State.Industry!.Trips);
        var car = straight.State.ContinentState!.Caravans.Get(new EntityId(trip.Caravan))!;
        Assert.True(car.Active && car.Dist > 0, "the caravan is on the road at the save");
        var file = SaveAt(straight.Rt); var bytes = SaveCodec.Encode(file);
        Assert.Contains(IndustrySave.SectionName, file.Sections.Keys);
        long hourSave = straight.Rt.HourDone; int worksAtSave = straight.State.Industry.Works.Count;
        // after the save the hero's crews are kept by the labour market alone (the same in both runs: it reads only the state); the faucets read only the state too
        for (int d = 0; d < Days; d++)
        {
            straight.RunDay(faucet: true, hall: false);
            if (d % 120 == 119) { var (_, mass) = straight.Audit(); Assert.True(mass); Assert.True(straight.State.Economy!.Ledger.Audit().IsConserved); }
        }
        var want = Sections(straight.Rt); var hashWant = straight.State.Industry.Hash();
        output.WriteLine($"straight run: {straight.State.Industry.Works.Count} works ({straight.State.Industry.Works.Count(w => straight.Iw.IsNpc(w))} founded by NPCs), trips {iron.CaravanTrips}, lost {iron.CaravansLost}, industry hash {hashWant:x}");

        var st = WorldSave.Restore(SaveCodec.Decode(bytes), straight.W.Content, "h", null, straight.W.Continent, straight.W.History);
        st.Content = straight.W.Content;
        using var resumed = new WorldRuntime(st, straight.W.Content, new MemoryRegionPager());
        Assert.Single(resumed.State.Industry!.Trips); Assert.Equal(worksAtSave, resumed.State.Industry.Works.Count);
        var back = Rig.Wrap(straight.W, resumed, npcDisabled: false);
        for (int d = 0; d < Days; d++) back.RunDay(faucet: true, hall: false);
        Assert.Equal(hourSave + 24L * Days, resumed.HourDone);
        var got = Sections(resumed);
        var bad = want.Keys.Union(got.Keys).Where(k => !(want.TryGetValue(k, out var x) && got.TryGetValue(k, out var y) && x.AsSpan().SequenceEqual(y))).ToList();
        output.WriteLine("differing sections: " + string.Join(", ", bad));
        Assert.Equal(hashWant, resumed.State.Industry.Hash());
        Assert.Empty(bad);
        Assert.True(back.Audit().Mass);
        // save, load, save is the same bytes
        var re = SaveAt(resumed); var st2 = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(re)), straight.W.Content, "h", null, straight.W.Continent, straight.W.History);
        Assert.Equal(SaveCodec.SerializeCompressed(IndustrySave.Capture(resumed.State.Industry)), SaveCodec.SerializeCompressed(IndustrySave.Capture(st2.Industry!)));
    }
    /// <summary>Coin the mints struck less coin melted or burned since the start (P7-CN1): the purses' total moves by exactly this.</summary>
    private static long Struck(Ledger led) { var a = led.Audit(); return a.Minted.Mites - a.Melted.Mites - a.Burned.Mites; }
}
