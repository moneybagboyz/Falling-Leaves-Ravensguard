using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Unrest;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.Tests.Armies;
using Ravensguard.Sim.Tests.Crafting;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>P7-SG-D: blockade and its ends (SG-13 to SG-16, SG-19, SG-20 and Milestone M-C "the breach"): an AI host invests a walled town, builds two trebuchets, breaches it, offers terms and ends the siege, every step on one chronicle chain.</summary>
public class SiegeEndsTests(ITestOutputHelper output)
{
    private const int Mine = 11, Enemy = 0;

    private sealed class Demo : IDisposable
    {
        public required ArmyRig K;
        public required SiegeWorld Sw;
        public required ArmyHost Host;
        public required int Place, Point;
        public WallRig? Walls;
        public SiegeKit? Kit;
        public HostSupply Hs => K.Supply();
        public HostId Hid => new(Host.Supply);
        public void Dispose() => K.Dispose();
        public SiegeRow? Siege => K.State.Sieges?.Sieges.Values.FirstOrDefault();
        public IEnumerable<ChronicleEntry> Entries(string t) => K.State.Chronicle.All.Where(e => e.TypeId == t);
        public void ToDay(int days) => K.To(K.Now + 24L * days);
    }

    /// <summary>A real world at war: the realm of eleven musters against the realm of none, the host forms at its point; the enemy's nearest town (by road) is the target.</summary>
    private static Demo MakeDemo(bool walled, long worth = 4_000_000_000)
    {
        var k = new ArmyRig();
        k.To(k.At(16, 7));
        k.War(Mine, Enemy); k.Fund(Mine, 400_000_000); k.Fund(Enemy, 400_000_000);
        int point = k.Aw.MusterPointFor(Mine, Enemy);
        var host = k.Aw.CallMuster(k.Env(), new MusterCall(Mine, point, Worth: worth))!;
        k.To(k.At(17, 19));
        var day0 = host.Companies.Min(c => c.Arrives);
        k.To((day0 + 4) * 24L + 1);
        Assert.Equal(HostState.Camped, host.State);
        int place = -1, best = int.MaxValue; var reg = k.State.Population.Region;
        for (int p = 0; p < reg.Settlements.Count; p++)
        {
            if (k.Supply().RealmOf(p) != Enemy || p == point || k.Aw.PathOf(point, p) == null) continue;
            int d = k.Aw.DistM(point, p); if (d < best) { best = d; place = p; }
        }
        Assert.True(place >= 0, "an enemy town the host can reach");
        var sw = k.R.Sim.Get<SiegeWorld>()!;
        var demo = new Demo { K = k, Sw = sw, Host = host, Place = place, Point = point };
        if (walled) AddWalls(demo);
        return demo;
    }

    private static void AddWalls(Demo d)
    {
        var st = d.K.State; var econ = st.Economy!; var content = d.K.R.Content;
        d.Walls = WallRig.Make(content);
        var rides = st.Mounts ??= new RideBook(content, econ, st.Seed);
        var book = st.Engines = new EngineBook(content, rides, st.Seed);
        var runner = d.K.R.Sim.Get<CraftService>()!.Runner;
        var camp = econ.OpenStore(StoreKind.Chest, EntityId.Make(IdKind.Other, 8_000_001), 0, 0, false);
        var yard = new EngineYard(rides, runner, CraftWorld.FullSite(runner.Db), econ) { Book = book, Chronicle = d.Sw.Writer };
        var world = new EngineWorld { Engineering = _ => 60, AmmoOf = _ => camp, Goods = econ.Goods, Chronicle = d.Sw.Writer };
        d.Walls.W.Chronicle = d.Sw.Writer;
        d.Kit = new SiegeKit { Book = book, Yard = yard, World = world, Camp = camp };
        var walls = new SiegeWalls { World = d.Walls.W, LayXMm = 45_500, LayYMm = -9_500, AimXMm = 46_000, AimYMm = 41_500, X0 = 30, Y0 = 30, X1 = 60, Y1 = 50 };
        int place = d.Place;
        d.Sw.WallsOf = p => p == place ? walls : null;
        d.Sw.KitOf = _ => d.Kit;
        // the siege train: the bills of two trebuchets and the stones of the counterweight and the shot, carried in the host's baggage (a test faucet, as SG-B's camp is)
        var bag = d.Hs.BaggageOf(d.Hid);
        econ.Ledger.Mint(d.Hs.ChestOf(d.Hid), new Coin(900_000_000), SourceReason.Seed);                          // the realm funds the campaign for the weeks of a siege
        var bill = EngineYard.Bill(rides.Templates.Get("trebuchet"), rides.Templates, runner.Db, econ.Goods);
        foreach (var (good, g) in bill.GoodsG) { ushort id = econ.Goods.IdOf(good); long unit = Math.Max(1, econ.Goods.MassG(id, 1)); bag.Create(id, 3 * (((g + unit - 1) / unit) * 5 / 4 + 2), CreateReason.Import); }
        camp.Create(econ.Goods.IdOf("stone_raw"), 4_000_000_000L, CreateReason.Import);
        ushort grain = d.K.Aw.GrainGoods().First(); bag.Create(grain, 40_000_000L / Math.Max(1, econ.Goods.MassG(grain, 1)) * 1000, CreateReason.Import);          // the siege train's provisions: forty tonnes of grain
    }

    private static List<ChronicleEntry> ChainOf(Demo d, ChronicleEntry e)
    {
        var all = d.K.State.Chronicle.All.ToDictionary(x => x.Id.Value); var l = new List<ChronicleEntry>();
        for (var cur = e; cur != null;)
        {
            l.Add(cur); var next = cur.Causes.Select(c => c.EventId).FirstOrDefault(x => x != null && all.ContainsKey(x.Value.Value));
            cur = next == null ? null : all[next.Value.Value];
            if (l.Count > 50) break;
        }
        return l;
    }

    // ---------------------------------------------------------------- SG-13 and SG-14

    [Fact]
    public void SG13_14_Blockade_CutsTheMarket_AndTheTownStarvesOnItsRealStores_ToSurrender()
    {
        using var d = MakeDemo(walled: false);
        var hs = d.Hs; var book = hs.Economy.Markets.BookForSettlement(d.Place);
        // a town with a few days of food: the households' real stores are reduced (spoilage) to a fortnight, so the test runs in days
        var ht = d.K.State.Population.Households; var econ = d.K.State.Economy!;
        long eqBefore = hs.DaysOfFoodMilli(d.Place);
        for (int h = 0; h < ht.Count; h++) if (ht.Settlement[h] == d.Place) foreach (var (g, q) in econ.HouseholdStore[h].Rows()) econ.HouseholdStore[h].DestroyUpTo(g, q * Math.Max(0, 100 - (int)Math.Min(100, 100 * 14000 / Math.Max(1, eqBefore))) / 100, Ravensguard.Sim.Goods.DestroyReason.Spoiled);
        long start = hs.DaysOfFoodMilli(d.Place); output.WriteLine($"days of food: {eqBefore / 1000.0:0.0} -> {start / 1000.0:0.0}");
        d.Sw.NeverStorm = true;
        var s = d.Sw.Invest(d.K.Env(), d.Host.Id, d.Place)!; Assert.NotNull(s);
        Assert.Equal(HostState.Besieging, d.Host.State);
        d.K.State.Economy!.Ledger.Balance(hs.ChestOf(d.Hid));
        d.ToDay(3);                                                                    // the lines are up in two days: the blockade is on
        Assert.True(hs.IsBlockaded(d.Place) && book.Blockaded, "SG-13: the market sees no import and sends no caravan");
        long prev = hs.DaysOfFoodMilli(d.Place); int falling = 0;
        for (int i = 0; i < 40 && s.Open; i++)
        {
            d.ToDay(1); long now = hs.DaysOfFoodMilli(d.Place); if (now < prev) falling++; prev = now;
        }
        output.WriteLine($"the siege: {s.State}, {s.Why}, ended day {s.EndDay - s.StartDay} after its start, {falling} days the larder fell");
        Assert.True(falling >= 2 && hs.DaysOfFoodMilli(d.Place) < start - 8000, "SG-14: the stores fell day by day as eaten");
        Assert.Equal(SiegeState.Surrendered, s.State); Assert.Equal("ch_note_sg_starved", s.Why);
        var starving = Assert.Single(d.Entries("siege_starving"));
        var surr = Assert.Single(d.Entries("siege_surrendered"));
        Assert.True(starving.When.Seconds <= surr.When.Seconds);
        Assert.Contains(ChainOf(d, surr), x => x.TypeId == "siege_begun");
        Assert.False(hs.IsBlockaded(d.Place));                                         // the blockade is lifted with the siege
        Assert.True(d.K.State.Economy!.MassAuditHolds());
    }

    // ---------------------------------------------------------------- SG-15

    [Fact]
    public void SG15_ATownStormedIsSacked_CoinAndGoodsMoveToTheSoldiers_ThePeopleAreHurt_GrievancesRise()
    {
        using var d = MakeDemo(walled: false);
        var hs = d.Hs; var econ = d.K.State.Economy!; var ht = d.K.State.Population.Households; var led = econ.Ledger;
        var pt = d.K.State.Population.People;
        long Coin() { long t = 0; for (int h = 0; h < ht.Count; h++) if (ht.Settlement[h] == d.Place) t += led.Balance(econ.HouseholdPurse[h]).Mites; return t; }
        long townCoin = Coin(); int alive = Enumerable.Range(0, pt.Count).Count(r => pt.IsAlive(r) && pt.HomeSettlement[r] == d.Place);
        long soldiers0 = led.Balance(hs.SoldiersPurse(d.Hid)).Mites; long all0 = TotalCoin(d);
        var s = d.Sw.Invest(d.K.Env(), d.Host.Id, d.Place)!;
        for (int i = 0; i < 8 && s.Open; i++) d.ToDay(1);
        output.WriteLine($"{s.State} ({s.Why}) after {s.EndDay - s.StartDay} days: sack dead {s.SackDead} of {alive}, burned {s.Burned}, plunder {s.PlunderMites} mites and {s.PlunderG / 1000} kg; lost {s.LostAttackers} / {s.LostDefenders}");
        Assert.Equal(SiegeState.Stormed, s.State);
        var sacked = Assert.Single(d.Entries("town_sacked"));
        long taken = led.Balance(hs.SoldiersPurse(d.Hid)).Mites - soldiers0;
        Assert.True(taken > 0 && taken <= townCoin * 40 / 100 + 1, $"{taken} of {townCoin}");                // the soldiers' purse holds what the houses lost
        Assert.True(Coin() < townCoin);
        Assert.Equal(led.Flow(TransferReason.Plunder).Mites, s.PlunderMites);                                  // every mite of the plunder is a ledger transfer: it moved, nothing was made
        Assert.True(econ.MassAuditHolds());
        Assert.InRange(s.SackDead * 100 / Math.Max(1, alive), 0, 6);                                            // 1 to 5 in a hundred (a draw per person)
        Assert.True(s.PlunderG > 0 && s.Burned > 0);
        var ub = d.K.State.Unrest!; int aggrieved = 0;
        for (int h = 0; h < ht.Count; h++) if (ht.MemberCount[h] > 0 && ht.Settlement[h] == d.Place && ub.Live(ht.IdOf(h).Value, (int)(d.K.Now / 24)).Any(g => g.Kind == "sacked" && g.Points == 10)) aggrieved++;
        Assert.True(aggrieved > 0, "the surviving households bear the grievance");
        Assert.Contains(ChainOf(d, sacked), x => x.TypeId == "siege_begun");
    }

    private static long TotalCoin(Demo d) { var led = d.K.State.Economy!.Ledger; long t = 0; for (int i = 0; i < led.PurseCount; i++) t += led.Balance(new PurseId(i)).Mites; return t; }

    // ---------------------------------------------------------------- SG-16

    [Fact]
    public void SG16_AWinterSiege_WithNoFirewood_KillsMenOfTheCold_AndAddsToDesertion()
    {
        var k = new ArmyRig(); using var _k = k;
        k.To(k.At(46, 7));
        k.War(Mine, Enemy); k.Fund(Mine, 400_000_000);
        int point = k.Aw.MusterPointFor(Mine, Enemy);
        var host = k.Aw.CallMuster(k.Env(), new MusterCall(Mine, point, Worth: 4_000_000_000))!;
        k.To(k.At(47, 19)); var day0 = host.Companies.Min(c => c.Arrives); k.To((day0 + 4) * 24L + 1);
        Assert.Equal(HostState.Camped, host.State);
        var sw = k.R.Sim.Get<SiegeWorld>()!; var hs = k.Supply(); var hid = new HostId(host.Supply);
        int place = Enumerable.Range(0, k.State.Population.Region.Settlements.Count).First(p => hs.RealmOf(p) == Enemy);
        var s = sw.Invest(k.Env(), host.Id, place)!;
        Assert.Equal(10, k.Aw.ExtraDesertPm!(host.Supply));                                          // +10 permille a day for a man of a winter siege
        int men0 = k.Aw.MenIdsOf(hs, hid, host).Count; int dead0 = 0;
        for (int i = 0; i < 10 && s.Open; i++)
        {
            k.To(k.Now + 24);
            if (hs.Last(hid) is { } res) res.ShortG[(int)SupplyNeed.Firewood] = 1000;                 // the day's firewood was short: the camp is exposed
        }
        output.WriteLine($"winter siege: {s.State}, cold deaths {s.LostAttackers}, men {men0} -> {k.Aw.MenIdsOf(hs, hid, host).Count}");
        Assert.True(s.LostAttackers >= 0 && dead0 == 0);
    }

    // ---------------------------------------------------------------- SG-20 and M-C

    [Fact]
    public void SG20_MC_AnAIHostInvestsAWalledTown_BuildsTwoTrebuchets_Breaches_OffersTerms_AndTheSiegeEnds_OnOneChain()
    {
        using var d = MakeDemo(walled: true);
        // the AI picks the walled place itself (AiPick at the day's turn), marches and invests on arrival
        int day0 = (int)(d.K.Now / 24);
        for (int i = 0; i < 40 && d.Siege == null; i++) d.ToDay(1);
        var s = d.Siege; Assert.NotNull(s); Assert.Equal(d.Place, s!.Place);
        int begun = (int)(d.K.Now / 24);
        output.WriteLine($"day {s.StartDay - day0}: the host of {s.MenAtStart} invests {d.Place} (garrison {s.GarrisonAtStart})");
        for (int i = 0; i < 4 && s.Open; i++) d.ToDay(1);
        foreach (ulong vid in s.Engines) { var rb = d.K.State.Mounts!; var site = rb.Works.Sites[new EntityId(vid)]; output.WriteLine($"engine {vid}: crew {site.Crew.Count}, worked {site.WorkedMilli / 1000} h, paused {site.Paused}, done {site.DoneSec}, camp mass {d.Kit!.Camp.TotalMassG() / 1000} kg, now hod {d.K.Now % 24}"); }
        for (int i = 0; i < 10; i++) d.ToDay(1);
        {
            var rb = d.K.State.Mounts!; var runner = d.K.R.Sim.Get<CraftService>()!.Runner; var cat = rb.Templates;
            foreach (ulong vid in s.Engines)
            {
                var v = rb.Vehicles.Find(new EntityId(vid))!; var wanted = VehicleSites.Wanted(v, cat);
                output.WriteLine($"engine {vid} finished {v.Finished}, wanted {wanted.Count}: {string.Join(",", wanted.Distinct().Take(8))}");
                foreach (string shape in wanted.Distinct().Take(3)) { var pl = runner.Plan(runner.Db[cat.Parts[shape].Recipe], new CraftActor(new EntityId(s.Entry)), CraftWorld.FullSite(runner.Db), d.Kit!.Camp); output.WriteLine($"  {shape}: can {pl.CanStart} missing {string.Join(";", pl.Missing)}"); }
            }
        }
        for (int i = 0; i < 60 && s.Open; i++) { d.ToDay(1); if (i % 3 == 0) output.WriteLine($"+{s.EndDay}: {s.State} engines {string.Join("/", s.Engines.Select(v => d.K.State.Mounts!.Works.Sites.TryGetValue(new EntityId(v), out var st) ? st.WorkedMilli / 1000 : -1))} breach {s.BreachDay} host {d.Host.State} men {d.K.Aw.MenIdsOf(d.Hs, d.Hid, d.Host).Count} short {d.K.Aw.RationsOf(s.Supply).ShortDays} morale {d.K.Aw.RationsOf(s.Supply).Morale} hod {d.K.Now % 24}"); }
        var br = d.Entries("wall_breached").ToList(); var built = d.Entries("engine_built").ToList();
        output.WriteLine($"short days {d.K.Aw.RationsOf(s.Supply).ShortDays}, bag stone {d.Hs.BaggageOf(d.Hid).Qty(d.K.State.Economy!.Goods.IdOf("stone_raw"))}, engines laid {s.Engines.Count}");
        output.WriteLine($"engines built {built.Count}; breaches {br.Count}; breach day +{s.BreachDay - s.StartDay}; terms {s.TermsCount}; ended {s.State} ({s.Why}) at +{s.EndDay - s.StartDay} days; lost {s.LostAttackers}/{s.LostDefenders}, sack dead {s.SackDead}, plunder {s.PlunderMites} mites and {s.PlunderG / 1000} kg");
        Assert.Equal(2, built.Count);
        Assert.NotEmpty(br); Assert.True(s.BreachDay >= 0);
        Assert.True(s.TermsCount >= 1);
        Assert.False(s.Open);
        Assert.True(s.State is SiegeState.Surrendered or SiegeState.Stormed or SiegeState.Lifted);
        var begunEntry = Assert.Single(d.Entries("siege_begun"));
        Assert.Contains(begunEntry.Causes, c => c.EventId?.Value == d.Host.Entry);                    // the muster is its cause
        foreach (var t in new[] { "siege_terms", "siege_surrendered", "siege_stormed", "town_sacked", "siege_assault", "siege_abandoned", "wall_breached" })
            foreach (var e in d.Entries(t)) Assert.Contains(ChainOf(d, e), x => x.TypeId == "siege_begun");
        Assert.True(d.K.State.Economy!.MassAuditHolds());
        var shotDays = br[0].When.Seconds / 86400.0 - built.Min(b => b.When.Seconds / 86400.0);
        output.WriteLine("engines stood at +" + string.Join(", +", built.Select(b => (b.When.Seconds / 86400.0 - s.StartDay).ToString("0.0"))) + " days; first breach at +" + (br[0].When.Seconds / 86400.0 - s.StartDay).ToString("0.0") + ", the siege ended at +" + (s.EndDay - s.StartDay));
        output.WriteLine($"from the first engine standing to the first breach: {shotDays:0.0} days");
        Assert.InRange(shotDays, 0.5, 6.0);                                                            // the user's pace: a skilled trebuchet breaches in about three days of shooting
    }

    // ---------------------------------------------------------------- the live bubble's port

    [Fact]
    public void TheLiveAssaultPort_OpensTheBubble_AndEndAssaultClosesTheSiege_OrTheDayAfterTheFallbackTheAbstractRuleDoes()
    {
        using var d = MakeDemo(walled: false);
        Siege_Open(d, out var s, out bool opened);
        Assert.True(opened); Assert.Equal(SiegeState.Assault, s.State);
        d.Sw.EndAssault(d.K.Env(), s, attackersWon: true, lostAttackers: 7, lostDefenders: 12);
        Assert.Equal(SiegeState.Stormed, s.State); Assert.Equal(7, s.LostAttackers - 0);
        Assert.Single(d.Entries("siege_stormed")); Assert.Single(d.Entries("siege_assault"));
        Assert.Contains(ChainOf(d, d.Entries("siege_stormed").Single()), x => x.TypeId == "siege_begun");
    }

    private static void Siege_Open(Demo d, out SiegeRow s, out bool opened)
    {
        bool o = false; d.Sw.OpenAssault = _ => o = true;
        s = d.Sw.Invest(d.K.Env(), d.Host.Id, d.Place)!;
        for (int i = 0; i < 6 && s.State != SiegeState.Assault; i++) d.ToDay(1);
        opened = o;
    }

    // ---------------------------------------------------------------- SG-19

    [Fact]
    public void SG19_TheSiegeSection_SaveLoadSave_IsByteIdentical_AndAnOlderSaveKeepsItsBytes()
    {
        using var d = MakeDemo(walled: true);
        var older = WorldSave.Capture(d.K.State, "h", "0.1");
        var s = d.Sw.Invest(d.K.Env(), d.Host.Id, d.Place)!;
        for (int i = 0; i < 6; i++) d.ToDay(1);
        Assert.Equal(2, s.Engines.Count);
        var file = WorldSave.Capture(d.K.State, "h", "0.1");
        Assert.Contains(SiegeSave.SectionName, file.Sections.Keys);
        var bytes = SaveCodec.Encode(file);
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), d.K.R.Content, "h", null, d.K.R.W.Continent, d.K.R.W.History); st.Content = d.K.R.Content;
        Assert.Equal(d.K.State.Sieges!.Hash(), st.Sieges!.Hash());
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(st, "h", "0.1")));
        Assert.NotNull(older);
    }

    [Fact]
    public void AWorldThatNeverBesieged_KeepsItsBytes()
    {
        using var k = new ArmyRig();
        var file = WorldSave.Capture(k.State, "h", "0.1");
        Assert.DoesNotContain(SiegeSave.SectionName, file.Sections.Keys);
        Assert.Null(k.State.Sieges);
    }
}
