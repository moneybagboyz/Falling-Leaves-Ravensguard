using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>P7-AE3 gate: AE-15 to AE-19 and AE-22: kit fill and orders, kit time and iron, loot sorting, forge_scrap, the arms trade and its bans, and a 30-day campaign's determinism and saves.</summary>
public class ArmsIndustryTests(ITestOutputHelper output)
{
    private static ContentDatabase Db => EconFixture.Content;
    private static LargeBuildingRulesDef Rules => Db.All<LargeBuildingRulesDef>().First();
    private const long Key = 5151;
    private static readonly ArmouryCapacity Huge = new(600, 600, 600, 60, 600, 6000, 2400, 6000, 3600, 2400);

    private sealed class Rig
    {
        public EconomyState Econ = EconFixture.NewEconomy();
        public IdAllocator Ids = new();
        public WorksBook Works = new();
        public Armouries Arm;
        public ArmsBook Book = new();
        public ArmsWorld Arms;
        public Rig() { Arm = new Armouries(Works, Econ.Items, Econ.Goods, Db, Rules); Arms = new ArmsWorld(Book, Db, Econ.Items, Arm, Econ.Goods, EconFixture.Seed); }
        public static EntityId Household(int n) => EntityId.Make(IdKind.Household, (ulong)n);
        public EntityId Item(string shape, string material, int massG, ItemQuality q = ItemQuality.Common, int cond = 1000) =>
            Econ.Items.Add(Ids, shape, q, Household(1), [new ItemComponent(material, massG, (short)cond)], conditionPermille: cond);
        public EntityId Stock(string shape, string material, int massG, ItemQuality q = ItemQuality.Common, int cond = 1000)
        {
            var id = Item(shape, material, massG, q, cond); Assert.True(Arm.Add(Key, Huge, id), shape); return id;
        }
        public Store Sealed(int n) => Econ.OpenStore(StoreKind.Sealed, EntityId.Make(IdKind.Other, (ulong)n));
    }

    /// <summary>AE-15: 960 levy kits against an armoury of 400 spears, 300 hats and 200 shields: the best available issued, the gaps 560, 660, 760 and 960 (the gambeson is the soldier's own cloth, no armoury holds it), and the orders inside the arms budget.</summary>
    [Fact]
    public void AE15_KitFill_BestAvailableIssued_GapsListed_OrdersWithinTheBudget()
    {
        var r = new Rig();
        for (int i = 0; i < 400; i++) r.Stock("spear", "iron", 1500, i < 100 ? ItemQuality.Fine : ItemQuality.Common);
        for (int i = 0; i < 300; i++) r.Stock("kettle_hat", "iron", 1500);
        for (int i = 0; i < 200; i++) r.Stock("round_shield", "oak", 4500);
        var chron = new Chronicle(); var chronW = RuleChron.Of(chron, r.Ids);
        // the orders are placed against the gap before the fill: 10,000,000 mites of receipts, 10% for arms
        var o = r.Arms.Order(77, Key, 960, 10_000_000, 100, chronW);
        Assert.Equal(560, o.Gaps["spear"]); Assert.Equal(660, o.Gaps["kettle_hat"]); Assert.Equal(760, o.Gaps["round_shield"]); Assert.Equal(960, o.Gaps["gambeson"]);
        Assert.Equal(1_000_000, o.BudgetMites); Assert.True(o.Mites <= o.BudgetMites);
        Assert.Equal(560, o.Ordered["spear"]);                                                                           // 224,000
        Assert.Equal((1_000_000 - 224_000) / 1600, o.Ordered["kettle_hat"]);                                             // what the rest of the budget buys
        Assert.False(o.Ordered.ContainsKey("gambeson")); Assert.Equal(960, o.Deferred["gambeson"]);
        Assert.Equal(o.Mites, r.Book.Orders.Sum(x => x.Mites)); Assert.Equal(o.Ordered.Values.Sum(), r.Book.Stats[3]);
        var e = Assert.Single(chron.ByType("arms_ordered")); Assert.Contains(e.Causes, c => c.Kind == CauseKind.Supply);
        // the fill: the best first (the fine spears), every piece of the armoury issued, the rest gaps
        var holder = Rig.Household(9);
        var res = r.Arms.Fill(Key, 960, holder, 101);
        Assert.Equal(900, res.Items.Count);
        Assert.Equal(560, res.Gaps["spear"]); Assert.Equal(660, res.Gaps["kettle_hat"]); Assert.Equal(760, res.Gaps["round_shield"]); Assert.Equal(960, res.Gaps["gambeson"]);
        int fine = res.Items.Count(i => r.Econ.Items.Get(i).Quality == ItemQuality.Fine); Assert.Equal(100, fine);
        Assert.Empty(r.Arm.Stock(Key));
    }

    /// <summary>AE-16: 960 levy kits take 2,112 kg of iron and 6,115 smith-hours (the pattern runs with practice) and 11,904 hours of other work (the gambesons the slowest part).</summary>
    [Fact]
    public void AE16_KitTimeAndIron_For960LevyKits()
    {
        var r = new Rig(); var c = r.Arms.KitCost(960);
        Assert.Equal(2_112_000, c.IronG); Assert.Equal(6115, c.SmithHours);
        Assert.Equal(11_904 + 960 * 3, c.OtherHours);                                                                      // 11,904 tailoring hours and 2,880 of carpentry
        var plain = r.Arms.KitCost(960, practised: false); Assert.True(plain.SmithMinutes > c.SmithMinutes);
        output.WriteLine($"960 kits: {c.IronG / 1000} kg of iron, {c.SmithHours} smith-hours, {c.OtherHours} other hours, {c.Mites} mites");
        // the iron of the forged pieces is what the pattern recipes consume
        foreach (var p in r.Arms.Rules.LevyKit.Where(p => p.Recipe.Length > 0))
        {
            var rec = Db.All<CraftRecipeDef>().Single(x => x.Id == p.Recipe); var run = Patterns.Of(Db.All<ScaleSettingsDef>().First(), p.Recipe)!;
            double ironKg = rec.Components.SelectMany(x => x).Where(x => x.Good == "iron_bar").Sum(x => x.Kg);
            Assert.Equal(p.IronG, (int)Math.Round(ironKg * 1000 / run.RunMax));
        }
    }

    /// <summary>AE-17: 50 items from a field, 20 at 600 or more, 18 at 200 to 600, 12 below 200 holding 30 kg of iron: 20 to the armoury, 18 to refit, 21 kg of scrap iron. The refit then raises the 18 to their tier's cap and stocks them.</summary>
    [Fact]
    public void AE17_Loot_20ToTheArmoury_18ToRefit_12Salvaged_21KgOfScrap()
    {
        var r = new Rig(); var items = new List<EntityId>();
        for (int i = 0; i < 20; i++) items.Add(r.Item("spear", "iron", 1500, cond: 800 + i));
        for (int i = 0; i < 18; i++) items.Add(r.Item("kettle_hat", "iron", 1500, cond: 400 + i));
        for (int i = 0; i < 12; i++) items.Add(r.Item("axe", "iron", 2500, cond: 100 + i));
        var store = r.Sealed(1); var holder = Rig.Household(5);
        var res = r.Arms.Sort(3, holder, Key, Huge, items, store);
        Assert.Equal(20, res.ToArmoury); Assert.Equal(18, res.ToRefit); Assert.Equal(12, res.Salvaged); Assert.Equal(0, res.Kept);
        Assert.Equal(21_000, res.ScrapG); Assert.Equal(21_000, store.Qty("scrap_iron"));
        Assert.Equal(20, r.Arm.Stock(Key).Count); Assert.Equal(18, r.Book.Refit.Count);
        Assert.Equal(items.Skip(20).Take(18).Select(i => i.Value).OrderBy(x => x), r.Book.Refit.Keys);                       // the refit queue holds exactly the 18 mid-condition items
        Assert.All(r.Book.Refit.Values, a => Assert.Equal(Key, a));
        Assert.Equal(12, items.Skip(38).Count(i => r.Econ.Items.IndexOf(i) < 0));                                         // the salvaged items are gone
        // the strip is the 0x752 stream: the same battle strips the same way, another battle another way
        var r2 = new Rig(); var items2 = new List<EntityId>(); for (int i = 0; i < 20; i++) items2.Add(r2.Item("spear", "iron", 1500, cond: 800 + i));
        r2.Arms.Sort(3, holder, Key, Huge, items2, r2.Sealed(1));
        for (int i = 0; i < 20; i++) Assert.Equal(r.Econ.Items.Get(items[i]).ConditionPermille, r2.Econ.Items.Get(items2[i]).ConditionPermille);
        Assert.All(items.Take(20), i => Assert.InRange((int)r.Econ.Items.Get(i).ConditionPermille, 720, 819));
        // refit: three armourers do three a day, each to the Common cap of 850
        int days = 0; while (r.Book.Refit.Count > 0 && days < 30) { r.Arms.RunRefit(3, _ => Huge); days++; }
        Assert.Equal(6, days); Assert.Equal(38, r.Arm.Stock(Key).Count); Assert.Equal(18, r.Book.Stats[5]);
        Assert.All(items.Skip(20).Take(18), i => Assert.Equal(850, r.Econ.Items.Get(i).ConditionPermille));
        Assert.True(r.Econ.MassAuditHolds());
    }

    /// <summary>AE-18: forge_scrap makes 8 kg of bar from 10 kg of scrap and 6 kg of charcoal in 8 hours; the recipe's content says the same.</summary>
    [Fact]
    public void AE18_ForgeScrap_10KgScrapAnd6KgCharcoal_Make8KgBar_In8Hours()
    {
        var rec = Db.All<CraftRecipeDef>().Single(x => x.Id == "forge_scrap");
        Assert.Equal(10, rec.Components.SelectMany(x => x).Single(x => x.Good == "scrap_iron").Kg); Assert.Equal(6, rec.Components.SelectMany(x => x).Single(x => x.Good == "charcoal").Kg);
        Assert.Equal(8, rec.Results.Single(x => x.Good == "iron_bar").Kg); Assert.Equal(8, rec.Steps.Sum(s => s.Hours));
        var r = new Rig(); var s = r.Sealed(2);
        s.Create(r.Econ.Goods.IdOf("scrap_iron"), 25_000, CreateReason.Seed); s.Create(r.Econ.Goods.IdOf("charcoal"), 13_000, CreateReason.Seed);
        var (batches, hours) = r.Arms.ForgeScrap(s, 10);
        Assert.Equal(2, batches); Assert.Equal(16, hours);
        Assert.Equal(16_000, s.Qty("iron_bar")); Assert.Equal(5_000, s.Qty("scrap_iron")); Assert.Equal(1_000, s.Qty("charcoal"));
        Assert.Equal((0, 0), r.Arms.ForgeScrap(s, 10));
        Assert.True(r.Econ.MassAuditHolds());
    }

    /// <summary>AE-19: a realm at war and a neutral realm with arms stock, the pair drawn: the belligerent's index rises, a caravan runs (stock and coin move by the ledger), and an export ban stops the next one at the gate.</summary>
    [Fact]
    public void AE19_ArmsTrade_IndexRises_ACaravanRuns_AnExportBanStopsIt()
    {
        using var run = AuditWorld.Create(); var hs = run.Supply; var led = run.Econ.Ledger;
        var arms = new ArmsWorld(hs.Book.Arms, Db, run.Econ.Items, null, run.Econ.Goods, run.Seed);
        var warring = led.Open(PurseKind.Treasury, LandWorld.RealmTreasuryId(7)); var neutral = led.Open(PurseKind.Treasury, LandWorld.RealmTreasuryId(3));
        led.Mint(warring, new Coin(50_000_000), SourceReason.Seed);
        hs.Book.Arms.Stock[3] = arms.Rules.TargetG * 2; hs.Book.Arms.Stock[7] = arms.Rules.TargetG;
        int peace = arms.IndexOf(7, false), war = arms.IndexOf(7, true);
        Assert.True(war > peace * 2, $"war {war} peace {peace}");                                                          // x3 demand
        IReadOnlyList<(int, bool)> realms = [(3, false), (7, true)];
        int day = 0; ArmsTradeResult? ran = null; long coin0 = led.Balance(neutral).Mites, stock3 = hs.Book.Arms.Stock[3];
        for (; day < 200 * 90 && (ran == null || ran.Shipments.Count == 0); day += 90) ran = arms.Season(day, realms, hs.PayForArms);
        Assert.NotNull(ran); var ship = Assert.Single(ran!.Shipments);
        Assert.Equal((3, 7), (ship.From, ship.To)); Assert.True(ship.Grams > 0 && ship.Mites > 0);
        Assert.Equal(coin0 + ship.Mites, led.Balance(neutral).Mites); Assert.Equal(stock3 - ship.Grams, hs.Book.Arms.Stock[3]);
        Assert.True(hs.Book.Arms.Index[7] >= war / 2 && hs.Book.Arms.Index[7] < war, "the war realm's index fell after buying but stays high");
        Assert.True(run.CoinConserved);
        // the ban: stopped at the gate, nothing moves
        hs.Book.Arms.Bans.Add(ArmsBook.BanKey(3, 7)); long held = hs.Book.Arms.Stock[3], coin1 = led.Balance(neutral).Mites; int stopped = 0;
        for (int k = 0; k < 200 && stopped == 0; k++) { var t = arms.Season(day, realms, hs.PayForArms); Assert.Empty(t.Shipments); stopped += t.Stopped; day += 90; }
        Assert.True(stopped > 0, "a drawn caravan was stopped at the gate");
        Assert.Equal(held, hs.Book.Arms.Stock[3]); Assert.Equal(coin1, led.Balance(neutral).Mites); Assert.True(hs.Book.Arms.Stats[2] > 0);
        Assert.True(run.CoinConserved); Assert.True(run.MassOk);
        // the book round-trips and hashes
        var back = SaveCodec.Deserialize<WarSupplySave>(SaveCodec.Serialize(WarSupplySave.Capture(hs.Book))).Restore();
        Assert.Equal(hs.Book.Hash(), back.Hash()); Assert.Equal(hs.Book.Arms.Stock, back.Arms.Stock); Assert.Equal(hs.Book.Arms.Bans, back.Arms.Bans); Assert.Equal(hs.Book.Arms.Log, back.Arms.Log);
    }

    /// <summary>A section without arms keeps its older shape: the arms key is null and the book is empty.</summary>
    [Fact]
    public void EmptyArmsBook_IsNotSaved_AndDoesNotMoveTheHash()
    {
        var b = new WarSupplyBook(); var h0 = b.Hash();
        Assert.Null(WarSupplySave.Capture(b).Arms); Assert.True(b.Arms.IsEmpty);
        b.Arms.Stats[3] = 1; Assert.NotEqual(h0, b.Hash()); Assert.NotNull(WarSupplySave.Capture(b).Arms);
    }

    // ------------------------------------------------------------------ AE-22

    private sealed record CampaignEnd(string Hash, long Coin, bool CoinOk, bool MassOk, string Section);

    private static CampaignEnd Campaign(bool saveAtDay15, ITestOutputHelper? output = null)
    {
        using var run = AuditWorld.Create(); var hs = run.Supply; var led = run.Econ.Ledger; var (start, gate) = AuditWorld.Stops(run);
        var h = AuditWorld.Host(run, 31, new Roster([(TroopRole.Spearman, 300)]), 4000, followers: 20);
        var a = hs.Account(h); a.Realm = hs.RealmOf(start); a.Purveyance = true; a.Forage = ForagePolicy.EnemyLand; a.Carts = 4;
        hs.Chron = RuleChron.Of(run.State.Chronicle, run.State.Ids);
        hs.SiegeBlockade(gate, true, h);                                                                                  // a siege of the far town
        var arm = new Armouries(new WorksBook(), run.Econ.Items, run.Econ.Goods, Db, Rules);
        var arms = new ArmsWorld(hs.Book.Arms, Db, run.Econ.Items, arm, run.Econ.Goods, run.Seed);
        int day0 = run.NextSlotDay;
        for (int d = day0; d < day0 + 30; d++)
        {
            run.BeforeSlot(d);
            hs.Update(h, hs.PlaceAt(start), HostMode.Camp); hs.Supply(h, d);
            if (d == day0 + 9) ArmsOps(run, hs, arms, arm, h, d);
            run.AfterSlot(d);
            Assert.True(run.CoinConserved, $"coin day {d - day0}"); Assert.True(run.MassOk, $"mass day {d - day0}");
            if (saveAtDay15 && d == day0 + 14) RoundTrip(run, hs);
        }
        output?.WriteLine($"purveyed {hs.Book.Stats[0] / 1000} kg, tallies {hs.Book.Tallies.Count}, foraged {hs.Book.Stats[6] / 1000} kg, sutlers {hs.Book.Sutlers.Count}, arms orders {hs.Book.Arms.Orders.Count}");
        return new CampaignEnd(hs.Book.Hash(), led.Audit().InPurses.Mites, run.CoinConserved, run.MassOk, run.SaveHash());
    }

    private static void ArmsOps(WarLoadRun run, HostSupply hs, ArmsWorld arms, Armouries arm, HostId h, int day)
    {
        var led = run.Econ.Ledger; var ids = run.State.Ids; var items = run.Econ.Items; var owner = EntityId.Make(IdKind.Household, 1);
        for (int i = 0; i < 5; i++) { var id = items.Add(ids, "spear", ItemQuality.Common, owner, [new ItemComponent("iron", 1500, 1000)]); Assert.True(arm.Add(Key, Huge, id)); }
        arms.Order(0, Key, 20, 5_000_000, day, hs.Chron);
        var loot = new List<EntityId>();
        for (int i = 0; i < 6; i++) loot.Add(items.Add(ids, i % 2 == 0 ? "spear" : "axe", ItemQuality.Common, owner, [new ItemComponent("iron", 2000, (short)(150 + 120 * i))], conditionPermille: 150 + 120 * i));
        arms.Sort(day, owner, Key, Huge, loot, hs.BaggageOf(h));
        led.Open(PurseKind.Treasury, LandWorld.RealmTreasuryId(11)); var rich = led.Open(PurseKind.Treasury, LandWorld.RealmTreasuryId(12));
        led.Mint(rich, new Coin(10_000_000), SourceReason.Seed);
        hs.Book.Arms.Stock[11] = arms.Rules.TargetG * 2; hs.Book.Arms.Stock[12] = arms.Rules.TargetG / 2;
        for (int k = 0; k < 40; k++) arms.Season(day + 90 * k, [(11, false), (12, true)], hs.PayForArms);
        Assert.True(hs.Book.Arms.Stats[0] > 0, "an arms caravan ran in the campaign");
    }

    private static void RoundTrip(WarLoadRun run, HostSupply hs)
    {
        var file = WorldSave.Capture(run.State, "h", "0.1"); var bytes = SaveCodec.Encode(file);
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), run.State.Content!, "h", null, run.State.Continent, run.State.History);
        st.Content = run.State.Content;
        using var rt = new WorldRuntime(st, st.Content!, new MemoryRegionPager()); WarLoadRun.AttachLinks(rt);
        var again = WorldSave.Capture(st, "h", "0.1");
        Assert.Equal(file.Sections.Keys.OrderBy(k => k), again.Sections.Keys.OrderBy(k => k));
        foreach (var k in file.Sections.Keys) Assert.True(file.Sections[k].Span.SequenceEqual(again.Sections[k].Span), $"section {k} differs after load and save");
        var hs2 = st.HomeRecords!.Sim.Get<HostSupply>()!; Assert.Equal(hs.Book.Hash(), hs2.Book.Hash()); Assert.Equal(hs.Book.Arms.Stats, hs2.Book.Arms.Stats);
    }

    /// <summary>AE-22: a 30-day campaign with purveyance, forage, sutlers and a siege, and the arms industry working in it: two runs end with equal SupplyHash and equal world, save-load-save at day 15 is identical, coin and mass exact every day.</summary>
    [Fact]
    public void AE22_ThirtyDayCampaign_SupplyHashEqual_SaveRoundTripAtDay15_CoinAndMassExact()
    {
        var a = Campaign(saveAtDay15: true, output); var b = Campaign(saveAtDay15: false);
        Assert.Equal(a.Hash, b.Hash); Assert.Equal(a.Coin, b.Coin); Assert.Equal(a.Section, b.Section);
        Assert.True(a.CoinOk && a.MassOk && b.CoinOk && b.MassOk);
    }

    private static readonly ArmouryCapacity None = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>P7R-18: an item that does not fit stays queued as it was and uses no armourer; one that can never be stocked is dropped; a restored one counts against the armourers.</summary>
    [Fact]
    public void P7R18_RefitRestoresOnlyWhatFits_CountsIt_AndDropsWhatCanNeverBeStocked()
    {
        var r = new Rig();
        var blocked = r.Item("kettle_hat", "iron", 1500, cond: 300); var fits = r.Item("kettle_hat", "iron", 1500, cond: 300); var never = r.Item("gambeson", "linen", 900, cond: 300);
        r.Book.Refit[blocked.Value] = Key; r.Book.Refit[fits.Value] = Key + 1; r.Book.Refit[never.Value] = Key + 2;
        int done = r.Arms.RunRefit(1, a => a == Key ? None : Huge);
        Assert.Equal(1, done);                                                                                               // the blocked one used no armourer
        Assert.Equal(0, r.Arms.RunRefit(1, a => a == Key ? None : Huge));                                                  // the next day: nothing fits, and the unstockable one is dropped
        Assert.Equal(300, r.Econ.Items.Get(blocked).ConditionPermille); Assert.Contains(blocked.Value, r.Book.Refit.Keys);
        Assert.DoesNotContain(never.Value, r.Book.Refit.Keys);                                                               // no kind: never stocked, dropped
        Assert.Contains(fits, r.Arm.Stock(Key + 1)); Assert.True(r.Econ.Items.Get(fits).ConditionPermille > 300);
        Assert.Equal(1, r.Book.Stats[5]);
    }

    /// <summary>P7R-19: with a full store the salvage and the forge create only what fits, count only that, and the mass audit holds.</summary>
    [Fact]
    public void P7R19_SalvageAndForge_ConserveMass_WithAFullStore()
    {
        var r = new Rig(); var full = r.Econ.OpenStore(StoreKind.Sealed, EntityId.Make(IdKind.Other, 31), capacityMassG: 5_000, enforce: true);
        Assert.True(full.Create(r.Econ.Goods.IdOf("scrap_iron"), 5_000, CreateReason.Seed));
        var item = r.Item("axe", "iron", 2500, cond: 100);
        var res = r.Arms.Sort(3, Rig.Household(5), Key, Huge, [item], full);
        Assert.Equal(1, res.Salvaged); Assert.Equal(0, res.ScrapG); Assert.Equal(5_000, full.Qty("scrap_iron"));            // nothing fitted, nothing counted
        Assert.Equal(res.ScrapG, r.Book.Stats[6]); Assert.True(r.Econ.MassAuditHolds());
        var forge = r.Econ.OpenStore(StoreKind.Sealed, EntityId.Make(IdKind.Other, 32), capacityMassG: 40_000, enforce: true);
        forge.Create(r.Econ.Goods.IdOf("scrap_iron"), 25_000, CreateReason.Seed); forge.Create(r.Econ.Goods.IdOf("charcoal"), 13_000, CreateReason.Seed);
        Assert.Equal((0, 0), r.Arms.ForgeScrap(forge, 10));                                                                  // no room for 8 kg of bar: nothing is burnt
        Assert.Equal(25_000, forge.Qty("scrap_iron")); Assert.Equal(13_000, forge.Qty("charcoal")); Assert.Equal(0, r.Book.Stats[7]);
        Assert.True(r.Econ.MassAuditHolds());
    }

    /// <summary>P7R-20: a second call in the season orders nothing more (pending orders are off the gaps and on the budget); a later season drops the stale rows.</summary>
    [Fact]
    public void P7R20_TwoOrdersInASeason_DoNotDoubleOrder_AndStaleRowsAreDropped()
    {
        var r = new Rig(); var chron = RuleChron.Of(new Chronicle(), r.Ids);
        var a = r.Arms.Order(77, Key, 100, 10_000_000, 100, chron);
        Assert.True(a.Mites > 0); int rows = r.Book.Orders.Count; long cost = r.Book.Orders.Sum(x => x.Mites);
        var b = r.Arms.Order(77, Key, 100, 10_000_000, 101, chron);
        Assert.Empty(b.Ordered); Assert.Equal(0, b.Mites); Assert.Equal(rows, r.Book.Orders.Count); Assert.Equal(cost, r.Book.Orders.Sum(x => x.Mites));
        int next = 100 + TimeConst.DaysPerSeason;
        var c = r.Arms.Order(77, Key, 100, 10_000_000, next, chron);
        Assert.True(c.Mites > 0); Assert.All(r.Book.Orders, o => Assert.Equal(next / TimeConst.DaysPerSeason, o.Day / TimeConst.DaysPerSeason));
    }

    /// <summary>P7R-23: a duplicated id is sorted once, an item already on a stand is skipped, and the item with no place is the holder's (kept, no coin moves).</summary>
    [Fact]
    public void P7R23_Sort_TakesEachItemOnce_SkipsStocked_AndNoPlaceMeansTheHolderKeepsIt()
    {
        var r = new Rig(); var holder = Rig.Household(5); var store = r.Sealed(1);
        var spear = r.Item("spear", "iron", 1500, cond: 1000); var onStand = r.Stock("spear", "iron", 1500);
        var res = r.Arms.Sort(3, holder, Key, Huge, [spear, spear, onStand], store);
        Assert.Equal(1, res.ToArmoury); Assert.Equal(0, res.Kept); Assert.Contains(spear, r.Arm.Stock(Key));
        Assert.Equal(2, r.Arm.Stock(Key).Count); Assert.Equal(1, r.Book.Stats[4]);
        var more = r.Item("spear", "iron", 1500, cond: 1000); long coin = r.Econ.Ledger.Audit().InPurses.Mites;
        var res2 = r.Arms.Sort(4, holder, Key + 9, None, [more], store);
        Assert.Equal(1, res2.Kept); Assert.Equal(holder, r.Econ.Items.Get(more).Owner); Assert.Equal(coin, r.Econ.Ledger.Audit().InPurses.Mites);
    }

    /// <summary>P7R-25: a damaged arms section is refused, as its siblings are.</summary>
    [Fact]
    public void P7R25_ArmsSaveRestore_RefusesRaggedRowsAndBadShapeIndexes()
    {
        Assert.Throws<InvalidDataException>(() => new ArmsSave { Orders = [1, 2, 3] }.Restore(new ArmsBook()));
        Assert.Throws<InvalidDataException>(() => new ArmsSave { Refit = [1] }.Restore(new ArmsBook()));
        Assert.Throws<InvalidDataException>(() => new ArmsSave { Log = [1, 2, 3, 4, 5] }.Restore(new ArmsBook()));
        Assert.Throws<InvalidDataException>(() => new ArmsSave { Shapes = ["spear"], Orders = [1, 2, 3, 4, 5] }.Restore(new ArmsBook()));
        new ArmsSave { Shapes = ["spear"], Orders = [1, 2, 3, 4, 0] }.Restore(new ArmsBook());
    }

    /// <summary>P7R-24: peace builds no arms world; a caravan to a war realm bought from beyond the ledger is paid as emigrant coin; the 0-mite payment returns true in the lambda as in PayForArms.</summary>
    [Fact]
    public void P7R24_ArmsSeason_PeaceBuildsNothing_AndTheEmigrantCoinPathPays()
    {
        using var run = AuditWorld.Create(); var hs = run.Supply; var led = run.Econ.Ledger;
        var st = Ravensguard.Sim.Continental.ContinentState.Standalone_(run.Seed, 0, 100, 1_000_000);
        var link = new Ravensguard.Sim.Economy.Markets.ContinentLink(st, 0); hs.Economy.World.Set(link);
        st.Summaries[1].Realm = 3; st.Summaries[2].Realm = 7;
        var importer = led.Open(PurseKind.Treasury, LandWorld.RealmTreasuryId(7)); led.Mint(importer, new Coin(50_000_000), SourceReason.Seed);
        int built = 0; ArmsWorld? arms = null;
        hs.ArmsSource = () => { built++; return arms ??= new ArmsWorld(hs.Book.Arms, Db, run.Econ.Items, null, run.Econ.Goods, run.Seed); };
        Assert.Null(hs.ArmsSeason(0)); Assert.Equal(0, built);                                                               // peace
        st.Summaries[2].WarFlags = 1;
        var rules = Db.All<ArmsRulesDef>().First();
        hs.Book.Arms.Stock[3] = rules.TargetG * 2; hs.Book.Arms.Stock[7] = rules.TargetG;
        long coin0 = led.Balance(importer).Mites, em0 = link.Mine.EmigrantCoin; ArmsTradeResult? ran = null;
        for (int day = 0; day < 200 * 90 && (ran == null || ran.Shipments.Count == 0); day += 90) ran = hs.ArmsSeason(day);
        Assert.True(built > 0); var ship = Assert.Single(ran!.Shipments);
        Assert.True(ship.Mites > 0); Assert.Equal(coin0 - ship.Mites, led.Balance(importer).Mites); Assert.Equal(em0 + ship.Mites, link.Mine.EmigrantCoin);
    }
}
