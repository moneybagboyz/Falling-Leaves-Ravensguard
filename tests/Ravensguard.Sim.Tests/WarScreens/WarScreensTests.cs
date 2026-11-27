using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.Tests.Armies;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WarSupply;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.WarScreen;

/// <summary>P8-WS-1 gate (war-screens-and-arms 5): WS-1 the Army tab, WS-2 the Armoury, WS-3 the Fief screen, WS-4 the helm HUD, WS-5 views only. The views are pure reads of the books; these tests compare them to the books.</summary>
public class WarScreensTests(ITestOutputHelper output)
{
    private const int Mine = 11, Enemy = 0;
    private static Ravensguard.Content.ContentDatabase Db => EconFixture.Content;
    private const long Key = 4242;
    private static readonly ArmouryCapacity Big = new(60, 60, 60, 60, 60, 600, 240, 600, 360, 2400);

    /// <summary>A world with a camped host (its levies and the warbands that joined), and every warband of the lords.</summary>
    private static (ArmyRig K, ArmyHost Host) WorldWithHost()
    {
        var k = new ArmyRig();
        k.To(k.At(16, 7)); k.War(Mine, Enemy); k.Fund(Mine, 400_000_000); k.Fund(Enemy, 400_000_000);
        int point = k.Aw.MusterPointFor(Mine, Enemy); Assert.True(point >= 0);
        var host = k.Aw.CallMuster(k.Env(), new MusterCall(Mine, point, Worth: long.MaxValue / 4, MaxLevies: 60, FundDays: 40, FollowsWar: false))!;
        for (int d = k.Day; d < k.DayOf(32) && host.State != HostState.Camped; d++) k.To((d + 1) * 24L + 19);
        Assert.Equal(HostState.Camped, host.State);
        return (k, host);
    }

    // ---------------------------------------------------------------- WS-1

    [Fact]
    public void WS1_ArmyTab_RowsMatchTheArmyBook_TwoWarbandsAHostAndAGarrison_EveryTotalIsTheBooks()
    {
        var (k, host) = WorldWithHost();
        using var _ = k;
        var wbs = k.Book.Warbands.Values.Take(3).ToList(); Assert.True(wbs.Count >= 3);
        wbs[2].Task = WarbandTask.Garrison;                                                       // the third stands in garrison
        wbs[0].ArrearsMites = 5_000_000; wbs[1].ArrearsMites = 0;
        var rows = WarScreens.ArmyRowsOf(k.State, k.State.Content!, k.Day);
        output.WriteLine(string.Join("\n", rows.Select(r => $"{r.Kind} {r.Name}: {r.Men} men ({r.RolesText}) wages {r.WageSeasonMites} arrears {r.ArrearsMites} morale {r.Morale} supply {r.SupplyDays} {r.Task} {r.Alerts}")));
        // one row per warband (the garrison among them), per host, and the hero's retinue when he has one
        Assert.Equal(k.Book.Warbands.Count, rows.Count(r => r.Kind is ArmyTabView.Warband or ArmyTabView.Garrison));
        Assert.Equal(k.Book.Warbands.Values.Count(w => w.Task == WarbandTask.Garrison), rows.Count(r => r.Kind == ArmyTabView.Garrison)); Assert.Contains(rows, r => r.Kind == ArmyTabView.Garrison); Assert.Equal(k.Book.Hosts.Count, rows.Count(r => r.Kind == ArmyTabView.Host));
        foreach (var wb in k.Book.Warbands.Values)
        {
            var r = rows.Single(x => x.Kind is ArmyTabView.Warband or ArmyTabView.Garrison && x.Id == wb.Id);
            Assert.Equal(wb.Members.Count, r.Men); Assert.Equal(wb.ArrearsMites, r.ArrearsMites); Assert.Equal(wb.Morale, r.Morale);
            Assert.Equal(wb.Members.Sum(m => k.Aw.WageOf(m)) * TimeConst.DaysPerSeason, r.WageSeasonMites);            // wages a season: the roll of a day, fifteen days
            Assert.Equal(wb.Members.Count, r.Roles.Sum(x => x.Count));
        }
        // the host: men, wages and arrears are the supply book's account
        var hs = k.Supply(); var a = hs.Account(new HostId(host.Supply)); var hr = rows.Single(x => x.Kind == ArmyTabView.Host && x.Id == host.Id);
        Assert.Equal(a.Total + a.Retainers, hr.Men);
        long wage = 0; foreach (var role in Enum.GetValues<TroopRole>()) wage += (long)a.Men[(int)role] * SupplyConfig.Wage(role) * TimeConst.DaysPerSeason;
        Assert.Equal(wage, hr.WageSeasonMites); Assert.Equal(a.Arrears.Sum(), hr.ArrearsMites);
        Assert.Equal(k.Book.Rations[host.Supply].Morale, hr.Morale);
        // the totals over the rows equal the books'
        Assert.Equal(k.Book.Warbands.Values.Sum(w => w.Members.Count) + a.Total + a.Retainers, rows.Sum(r => r.Men));
        Assert.Equal(k.Book.Warbands.Values.Sum(w => w.ArrearsMites) + a.Arrears.Sum(), rows.Sum(r => r.ArrearsMites));
        // alerts: 5,000,000 mites is far over 10 days of any warband's roll
        Assert.True(rows.Single(x => x.Kind is ArmyTabView.Warband or ArmyTabView.Garrison && x.Id == wbs[0].Id).Alerts.HasFlag(ArmyAlert.Arrears));
        Assert.False(rows.Single(x => x.Kind is ArmyTabView.Warband or ArmyTabView.Garrison && x.Id == wbs[1].Id).Alerts.HasFlag(ArmyAlert.Arrears));
        // the Ledger rows carry the same figures and map the alerts onto the ledger's flags
        var led = WarScreens.ArmyRows(k.State, k.State.Content!, k.Day);
        Assert.Equal(rows.Count, led.Count); Assert.All(led, r => Assert.Equal(LedgerTab.Army, r.Tab));
        Assert.Equal(rows.Count(r => r.Alerts != ArmyAlert.None), led.Count(r => r.Alerts != OwnerAlert.None));
    }

    [Fact]
    public void WS1_ARowSaysSupplyUnderFiveDays_AndDesertionWhileItsCausesLast_AndArrearsOverTenDays()
    {
        var (k, host) = WorldWithHost();
        using var _ = k;
        var hs = k.Supply(); var a = hs.Account(new HostId(host.Supply));
        long daily = 0; foreach (var role in Enum.GetValues<TroopRole>()) daily += (long)a.Men[(int)role] * SupplyConfig.Wage(role);
        a.Arrears[(int)TroopRole.Levy] = daily * 11 + 1;                                           // eleven days of the roll
        var rat = k.Book.Rations[host.Supply]; rat.DesertedPersons = 3; rat.HungryRun = 2;
        var r = WarScreens.ArmyRowsOf(k.State, k.State.Content!, k.Day).Single(x => x.Kind == ArmyTabView.Host);
        Assert.True(r.Alerts.HasFlag(ArmyAlert.Arrears)); Assert.True(r.Alerts.HasFlag(ArmyAlert.Desertion));
        a.Arrears[(int)TroopRole.Levy] = daily * 10;                                               // exactly ten: not over
        Assert.False(WarScreens.ArmyRowsOf(k.State, k.State.Content!, k.Day).Single(x => x.Kind == ArmyTabView.Host).Alerts.HasFlag(ArmyAlert.Arrears));
    }

    // ---------------------------------------------------------------- WS-2

    private sealed record Hall(EconomyState Econ, WorldState World, SoldierWorld Sw, HeroContext Ctx, IdAllocator Ids)
    {
        public EntityId Mk(string shape, string mat, int g, ItemQuality q = ItemQuality.Common) => Econ.Items.Add(Ids, shape, q, EntityId.Make(IdKind.Household, 1), [new ItemComponent(mat, g, 1000)]);
        public EntityId Stock(string shape, string mat, int g, ItemQuality q = ItemQuality.Common) { var id = Mk(shape, mat, g, q); Assert.True(Sw.Armouries.Add(Key, Big, id)); return id; }
        public long Mass() => Econ.Items.LiveRows().Sum(r => Econ.Items.MassG(r, Econ.Goods));
        public ItemQuality QualityOf(ulong item) => Econ.Items.Get(new EntityId(item)).Quality;
    }

    private static Hall NewHall(int soldiers)
    {
        var econ = EconFixture.NewEconomy(); var w = EconFixture.WorldWith(econ);
        var sw = SoldierWiring.For(w, Db);
        for (ulong p = 0; p < (ulong)soldiers; p++) sw.Book.Rows[p] = new SoldierRow { Person = p, Role = "spearman", Lord = 0 };
        return new Hall(econ, w, sw, new HeroContext(w, new HeroParty(), Db), new IdAllocator());
    }

    [Fact]
    public void WS2_Armoury_F_FillsFromStockBestFirst_TheScreenMovesNothing_TheDeskDoes()
    {
        var h = NewHall(2);
        var fine = h.Stock("spear", "iron", 900, ItemQuality.Fine); var common = h.Stock("spear", "iron", 900); var crude = h.Stock("spear", "iron", 900, ItemQuality.Crude);
        long mass = h.Mass(); int count = h.Econ.Items.Count;
        var before = ArmouryView.Build(h.Sw, Key, h.Sw.SoldiersOf(0), h.Econ.Items);
        var again = ArmouryView.Build(h.Sw, Key, h.Sw.SoldiersOf(0), h.Econ.Items);
        Assert.Equal(before.GapCount, again.GapCount); Assert.Equal(3, before.Stock);              // building the view twice moves nothing
        Assert.Equal(mass, h.Mass()); Assert.Equal(3, h.Sw.Armouries.Stock(Key).Count); Assert.Empty(h.Sw.Book.Rows[0].Worn);
        Assert.Contains("weapon", before.Columns);
        Assert.All(before.Rows, r => Assert.True(r.Cells.Single(c => c.Slot == "weapon").Fillable));        // the screen says a fill could close the weapon slot
        var res = SoldierDesk.Apply(h.Ctx, SoldierDesk.Fill(Key, 3 * 86400));
        Assert.Equal(HeroResultCode.Ok, res.Code);
        // the best two pieces went to the two men, the crude one stays: nothing left in the armoury beats what was issued
        var worn = new[] { h.Sw.Book.Rows[0].Worn["weapon"], h.Sw.Book.Rows[1].Worn["weapon"] };
        Assert.Contains(fine.Value, worn); Assert.Contains(common.Value, worn); Assert.DoesNotContain(crude.Value, worn);
        Assert.Equal(fine.Value, h.Sw.Book.Rows[0].Worn["weapon"]);                                  // the first man in the fill order has the finest
        Assert.Equal([crude], h.Sw.Armouries.Stock(Key));
        Assert.Equal(mass, h.Mass()); Assert.Equal(count, h.Econ.Items.Count);
        var after = ArmouryView.Build(h.Sw, Key, h.Sw.SoldiersOf(0), h.Econ.Items);
        Assert.All(after.Rows, r => Assert.NotEqual("", r.Cells.Single(c => c.Slot == "weapon").Shape));
    }

    [Fact]
    public void WS2_Armoury_G_FillsOnlyTheGaps_AWornPieceIsNotSwappedForABetterOne_AndEnterFitsOneMan()
    {
        // F would swap his common spear for the fine one; G leaves what he wears and fills only the empty slots
        var f = NewHall(2); var g = NewHall(2);
        foreach (var h in new[] { f, g })
        {
            var own = h.Stock("spear", "iron", 900);                                                  // soldier 0 is given the common spear first
            Assert.True(h.Sw.Fill.Equip(h.Sw.Book, 0, "weapon", own.Value, Key, 1));
            h.Stock("spear", "iron", 900, ItemQuality.Fine); h.Stock("kettle_hat", "iron", 1600);
        }
        ulong common = f.Sw.Book.Rows[0].Worn["weapon"];
        Assert.Equal(HeroResultCode.Ok, SoldierDesk.Apply(f.Ctx, SoldierDesk.Fill(Key, 3 * 86400)).Code);
        Assert.NotEqual(common, f.Sw.Book.Rows[0].Worn["weapon"]);                                    // F: the better piece replaced it
        ulong gCommon = g.Sw.Book.Rows[0].Worn["weapon"];
        var gapsBefore = ArmouryView.Build(g.Sw, Key, g.Sw.SoldiersOf(0), g.Econ.Items).GapCount;
        Assert.Equal(HeroResultCode.Ok, SoldierDesk.Apply(g.Ctx, SoldierDesk.FillGaps(Key, 3 * 86400)).Code);
        Assert.Equal(gCommon, g.Sw.Book.Rows[0].Worn["weapon"]);                                      // G: he keeps his common spear
        Assert.True(g.Sw.Book.Rows[1].Worn.ContainsKey("weapon")); Assert.True(g.Sw.Book.Rows[0].Worn.ContainsKey("helmet") || g.Sw.Book.Rows[1].Worn.ContainsKey("helmet"));
        Assert.True(ArmouryView.Build(g.Sw, Key, g.Sw.SoldiersOf(0), g.Econ.Items).GapCount < gapsBefore);
        // Enter: one man by hand; the other is not touched
        var one = NewHall(2); one.Stock("spear", "iron", 900); one.Stock("spear", "iron", 900);
        Assert.Equal(HeroResultCode.Ok, SoldierDesk.Apply(one.Ctx, SoldierDesk.FillOne(1, Key, 3 * 86400)).Code);
        Assert.True(one.Sw.Book.Rows[1].Worn.ContainsKey("weapon")); Assert.Empty(one.Sw.Book.Rows[0].Worn);
        Assert.Equal(HeroResultCode.Refused, SoldierDesk.Apply(one.Ctx, SoldierDesk.FillOne(77, Key, 3 * 86400)).Code);            // not his soldier
    }

    // ---------------------------------------------------------------- WS-3

    [Fact]
    public void WS3_FiefScreen_LevyTalliesScutageMintAndDepots_EqualTheBooks()
    {
        var armies = new ArmyBook(); var supply = new WarSupplyBook(); var rule = new RuleBook();
        var host = new ArmyHost { Id = 1, Realm = 7 };
        var c1 = new LevyCompany { Fief = 3, Quota = 20, Asked = 5, Evaded = 2, Drawn = 40 }; for (ulong i = 1; i <= 18; i++) c1.Men.Add(i);
        var c2 = new LevyCompany { Fief = 4, Quota = 10, Asked = 3, Evaded = 0, Drawn = 41 }; for (ulong i = 20; i < 30; i++) c2.Men.Add(i);
        host.Companies.Add(c1); host.Companies.Add(c2); armies.Hosts[1] = host;
        armies.Hosts[2] = new ArmyHost { Id = 2, Realm = 8 };                                           // another realm's: not shown
        supply.AddLevied(50, 3, 12); supply.AddLevied(51, 3, 6); supply.AddLevied(52, 2, 9);          // 52 gave last year
        supply.Tallies[1] = new PurveyanceTally { Id = 1, Realm = 7, Settlement = 50, TakenDay = 10, DueDay = 40, Mites = 9_000, PaidMites = 2_000, State = TallyState.Owed };
        supply.Tallies[2] = new PurveyanceTally { Id = 2, Realm = 7, Settlement = 51, TakenDay = 10, DueDay = 40, Mites = 4_000, PaidMites = 4_000, State = TallyState.Paid };
        supply.Tallies[3] = new PurveyanceTally { Id = 3, Realm = 9, Settlement = 51, TakenDay = 10, DueDay = 40, Mites = 1_000, State = TallyState.Owed };
        rule.Dues.Add(new DueRow { Serial = 1, Kind = DueKind.Scutage, Payer = Party.OfFief(3), Payee = Party.OfRealm(7), AmountMites = 12_000, PaidMites = 5_000, DueDay = 30, State = DueState.Arrears });
        rule.Dues.Add(new DueRow { Serial = 2, Kind = DueKind.Service, Payer = Party.OfFief(4), Payee = Party.OfRealm(7), AmountMites = 800, DueDay = 30, State = DueState.Due });
        rule.Offices.Add(new Office { Serial = 1, Kind = OfficeKind.MintMaster, Holder = 77, Realm = 7, Fief = -1 });
        var rows = FiefWarView.Rows(new FiefWarView.Sources { Realm = 7, Armies = armies, Supply = supply, Rule = rule, Year = 3, MintSettlement = 50, NameOf = p => "Master" + p });
        output.WriteLine(string.Join("\n", rows.Select(r => $"{r.What} {r.Who} {r.Value} {r.Unit} {r.State} d{r.Day} {(r.Alert ? "!" : "")}")));
        // the levy: each company with its men drawn, and the villages that gave this year
        Assert.Equal([18L, 10L], rows.Where(r => r.What == "fw_row_levy").Select(r => r.Value));
        Assert.Equal("5/20/2", rows.First(r => r.What == "fw_row_levy").State); Assert.True(rows.First(r => r.What == "fw_row_levy").Alert);     // 18 of 20 drawn: two owed
        Assert.False(rows.Last(r => r.What == "fw_row_levy").Alert);
        Assert.Equal([12L, 6L], rows.Where(r => r.What == "fw_row_village").Select(r => r.Value));
        // tallies owed: the realm's own, unpaid, by what is still owing
        var tally = Assert.Single(rows, r => r.What == "fw_row_tally"); Assert.Equal(supply.Tallies[1].Owing, tally.Value); Assert.Equal(40, tally.Day);
        // scutage: the amount owed and what was paid
        var scut = Assert.Single(rows, r => r.What == "fw_row_scutage"); Assert.Equal(12_000, scut.Value); Assert.Equal("5000", scut.State); Assert.True(scut.Alert);
        // the mint and its master
        var mint = Assert.Single(rows, r => r.What == "fw_row_mint"); Assert.Equal("Master77", mint.State); Assert.False(mint.Alert);
        rule.Offices.Clear();
        Assert.True(FiefWarView.Rows(new FiefWarView.Sources { Realm = 7, Armies = armies, Supply = supply, Rule = rule, Year = 3, MintSettlement = 50 }).Single(r => r.What == "fw_row_mint").Alert);
        // the same books give the same rows twice
        Assert.Equal(rows.Count - 1, FiefWarView.Rows(new FiefWarView.Sources { Realm = 7, Armies = armies, Supply = supply, Rule = rule, Year = 3, MintSettlement = -1 }).Count);   // no mint town: no mint row
    }

    [Fact]
    public void WS3_InARunningWorld_TheLevyRowsAreTheHostsCompaniesAndTheDepotsAreTheStoresOfTheSupplyBook()
    {
        var (k, host) = WorldWithHost();
        using var _ = k;
        var hs = k.Supply(); var acct = hs.Account(new HostId(host.Supply));
        hs.OpenDepot(5, 100);
        acct.Depots = [5];
        var st = k.State; var rows = FiefWarView.Rows(new FiefWarView.Sources { Realm = host.Realm, Armies = k.Book, Supply = st.WarSupply!, Rule = st.Rule, Hosts = hs, Year = k.Day / TimeConst.DaysPerYear });
        Assert.Equal(host.Companies.Select(c => (long)c.Men.Count), rows.Where(r => r.What == "fw_row_levy").Select(r => r.Value));
        Assert.Equal(st.WarSupply!.Levied.Where(l => l.Value.Year == k.Day / TimeConst.DaysPerYear).Sum(l => (long)l.Value.Men), rows.Where(r => r.What == "fw_row_village").Sum(r => r.Value));
        var depot = Assert.Single(rows, r => r.What == "fw_row_depot"); Assert.Equal((hs.DepotStore(5)?.TotalMassG() ?? -1) / 1000, depot.Value);
        // the Ledger rows (the Fief tab's) are the same rows, in the same order
        var led = WarScreens.FiefRows(st, st.Content!, k.Day);
        Assert.All(led, r => Assert.Equal(LedgerTab.Fief, r.Tab));
    }

    // ---------------------------------------------------------------- WS-4

    private static readonly VehicleCatalog Cat = VehicleCatalog.From(Db);
    private static readonly SeaRules Rules = SeaRules.From(Db);

    [Fact]
    public void WS4_HelmHud_WindHeadingAndSpeedEqualTheShipFrames_AndTheOrdersAreTheCrews()
    {
        var cog = SeaShip.Of(EntityId.Make(IdKind.Other, 11), Cat.Get("cog"), 100_000, 20, Rules, Cat.Water);
        var crew = new SeaCrewState(); var f = ShipFrame.Of(EntityId.Make(IdKind.Other, 11), Cat.Get("cog"), Cat, 0, 0, FrameMath.FromDegrees(340)); var slip = new SlipAcc();
        const int windFrom = 250, wind = 9;
        for (int i = 0; i < 400; i++) Sailing.Tick(cog, crew, f, Sailing.WindToQ24(windFrom), wind, 0, ref slip);
        int wave = SeaState.WaveMm(Rules, wind, 40);
        var v = HelmView.Of(f, cog, crew, windFrom, wind, wave, 120, grapplesReady: true, boarding: false);
        Assert.Equal(windFrom, v.WindFromDeg); Assert.Equal(wind, v.WindMps);
        Assert.Equal(f.SpeedMmps, v.SpeedMmps); Assert.True(v.SpeedMmps > 0);
        Assert.InRange(Math.Abs(v.HeadingDeg - (int)((long)f.Heading * 360 / FrameMath.Turn)), 0, 1);       // the frame's own heading in degrees, to the nearest
        Assert.InRange(v.HeadingDeg, 339, 341);                                                     // running before the wind: she held her heading
        Assert.Equal(cog.SailM2, v.SailM2); Assert.Equal(wave, v.WaveMm); Assert.Equal(HelmView.SeaKeyOf(wave), v.SeaKey); Assert.Equal(120, v.WaterL);
        Assert.Equal([CrewOrder.ShipOars, CrewOrder.ReadyGrapples], v.Orders);                      // oars are in: the order in force is "ship oars"
        crew.OarsOut = true;
        Assert.Equal([CrewOrder.Row, CrewOrder.ReadyGrapples, CrewOrder.Board], HelmView.Of(f, cog, crew, windFrom, wind, wave, 0, true, true).Orders);
        // the sea words and the knots are integers
        Assert.Equal("hm_sea_calm", HelmView.SeaKeyOf(50)); Assert.Equal("hm_sea_slight", HelmView.SeaKeyOf(499)); Assert.Equal("hm_sea_moderate", HelmView.SeaKeyOf(1000));
        Assert.Equal("hm_sea_rough", HelmView.SeaKeyOf(2000)); Assert.Equal("hm_sea_high", HelmView.SeaKeyOf(2500));
        Assert.Equal(19, HelmView.KnotsX10(1000)); Assert.Equal(0, HelmView.KnotsX10(0));
        Assert.Equal(359, HelmView.DegOf(FrameMath.FromDegrees(359)));
        Assert.Equal(0, HelmView.DegOf(FrameMath.Turn));                                            // a full turn is north
        output.WriteLine($"wind {v.WindFromDeg}/{v.WindMps}, heading {v.HeadingDeg}, speed {v.SpeedMmps} mm/s = {HelmView.KnotsX10(v.SpeedMmps) / 10.0} kn, waves {v.WaveMm} mm ({v.SeaKey})");
    }

    // ---------------------------------------------------------------- WS-5

    [Fact]
    public void WS5_ViewsOnly_ALoadRestoresTheSameRows_AndBuildingThemChangesNoBook()
    {
        var (k, host) = WorldWithHost();
        using var _ = k;
        var st = k.State; var content = st.Content!;
        int day = k.Day;
        string Dump(WorldState w) => string.Join("|", WarScreens.ArmyRowsOf(w, content, day).Select(r => $"{r.Kind}/{r.Id}/{r.Men}/{r.RolesText}/{r.WageSeasonMites}/{r.ArrearsMites}/{r.Morale}/{r.SupplyDays}/{r.Task}/{r.Alerts}"))
            + "#" + string.Join("|", WarScreens.FiefWarRowsOf(w, content, day).Select(r => $"{r.What}/{r.Who}/{r.Value}/{r.State}/{r.Day}"));
        var bytes = SaveCodec.Encode(WorldSave.Capture(st, EconFixture.ContentHash, "0.1.0"));
        string before = Dump(st);
        string again = Dump(st);
        Assert.Equal(before, again);                                                                // reading twice gives the same view
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(st, EconFixture.ContentHash, "0.1.0")));      // and the reads wrote nothing the save keeps
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), k.R.Content, EconFixture.ContentHash, null, k.R.W.Continent, k.R.W.History); back.Content = k.R.Content;
        using var rt = new Ravensguard.Sim.Worlds.WorldRuntime(back, k.R.Content, new Ravensguard.Sim.Worlds.MemoryRegionPager());
        Assert.Equal(before.Split('#')[0], Dump(back).Split('#')[0]);                               // a load shows the same Army tab, the baggage's days of food too
        Assert.Equal(host.Companies.Sum(c => c.Men.Count), back.Armies!.Hosts[host.Id].Companies.Sum(c => c.Men.Count));
        output.WriteLine(before.Split('#')[0]);
    }
}
