using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Armies;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Naval;

/// <summary>P7-SH-C (ships-and-naval-war 10): SH-9 sea edges, SH-10 raid planning, SH-11 the landing (raiders as real persons from real ships, the village raided, the bell, the beach fight, the withdrawal), and the naval section's round trip.</summary>
public class SeaRaidTests(ITestOutputHelper output)
{
    private static readonly SeaRules Rules = SeaRules.From(EconFixture.Content);

    // ------------------------------------------------------------------ SH-9

    /// <summary>A grid with two seas: rows 0-7 a bay between region 0 (x 0-3) and region 1 (x 12-15), row 8 a wall of land no region owns, rows 9-15 another bay between regions 2 and 3.</summary>
    private static (bool[] Ocean, Func<int, int> Region) TwoSeas()
    {
        const int N = 16; var o = new bool[N * N];
        for (int y = 0; y < N; y++) for (int x = 4; x < 12; x++) o[y * N + x] = y != 8;
        int Region(int c) { int x = c % N, y = c / N; return y == 8 ? -1 : y < 8 ? (x < 8 ? 0 : 1) : (x < 8 ? 2 : 3); }
        return (o, Region);
    }

    [Fact]
    public void SH9_SeaEdges_AcrossABay_TheBfsLength_AndDifferentSeasAreNotJoined()
    {
        var (ocean, region) = TwoSeas();
        var edges = SeaEdges.Build(16, ocean, region);
        output.WriteLine(string.Join(", ", edges.Select(e => $"{e.A}-{e.B}:{e.LengthKm}km")));
        // eight cells of water between the coasts: 16 km of sea path; the two bays never meet
        Assert.Equal([new SeaEdge(0, 1, 16), new SeaEdge(2, 3, 16)], edges);
        Assert.Null(SeaEdges.Find(edges, 0, 2)); Assert.Null(SeaEdges.Find(edges, 1, 3)); Assert.Null(SeaEdges.Find(edges, 0, 3));
        Assert.Equal(16, SeaEdges.Find(edges, 1, 0)!.LengthKm);
        // the same mask gives the same edges (pure), and a bay wider than 64 km joins nothing
        Assert.Equal(edges, SeaEdges.Build(16, ocean, region));
        const int W = 48; var wide = new bool[W * W];
        for (int y = 0; y < W; y++) for (int x = 4; x < 44; x++) wide[y * W + x] = true;                    // 40 cells: 80 km
        Assert.Empty(SeaEdges.Build(W, wide, c => c % W < 24 ? 0 : 1));
        const int V = 40; var near = new bool[V * V];
        for (int y = 0; y < V; y++) for (int x = 4; x < 36; x++) near[y * V + x] = true;                    // 32 cells: exactly 64 km
        Assert.Equal([new SeaEdge(0, 1, 64)], SeaEdges.Build(V, near, c => c % V < 20 ? 0 : 1));
    }

    [Fact]
    public void SH9_TheGeneratedContinent_HasSeaEdges_BetweenRegionsOfTheSameWater()
    {
        var edges = SeaEdges.Of(Ravensguard.Sim.Tests.Seamless.SeamWorld.Get(1234).Continent);
        output.WriteLine($"{edges.Count} sea edges; first {string.Join(", ", edges.Take(4).Select(e => $"{e.A}-{e.B}:{e.LengthKm}"))}");
        Assert.NotEmpty(edges);
        Assert.All(edges, e => { Assert.True(e.A < e.B); Assert.InRange(e.LengthKm, 2, 64); Assert.Equal(0, e.LengthKm % 2); });
        Assert.Equal(edges, SeaEdges.Of(Ravensguard.Sim.Tests.Seamless.SeamWorld.Get(1234).Continent));
    }

    // ------------------------------------------------------------------ SH-10

    private static RaiderRealm Northmark(int ships = 4) => new(3, 0, "north", ships, 5);

    [Fact]
    public void SH10_RaidPlanning_TheRichestWeaklyGuardedVillage_AtWar_AndALongshipFleetsSize()
    {
        var targets = new List<RaidTarget>
        {
            new(9, 1, 7, 90_000_000, 40, 100),      // rich but a warband sits there
            new(9, 2, 7, 60_000_000, 4, 100),       // the richest weakly guarded
            new(9, 3, 7, 20_000_000, 2, 100),       // poor
            new(9, 4, 3, 99_000_000, 0, 100),       // the raiders' own realm: never
        };
        var raid = SeaRaids.Plan(1234, 8, Northmark(), targets, v => v == 7, Rules, 24 * 400, 1)!;
        output.WriteLine($"ships {raid.PlanShips} men {raid.PlanMen} land hour {raid.LandHour} (dawn hour {raid.LandHour % 24}) target {raid.Settlement}");
        Assert.Equal(2, raid.Settlement); Assert.Equal(7, raid.Victim); Assert.True(raid.War);
        Assert.InRange(raid.PlanShips, 1, 4);
        Assert.InRange(raid.PlanMen, 30 * raid.PlanShips, 60 * raid.PlanShips);
        Assert.Equal(Rules.D.LandHourOfDay, raid.LandHour % 24);
        Assert.Equal(400 + 1, raid.LandHour / 24);                                                 // 100 km is a day's sail
        // the score is value over the days sailed, less the risk of the men known near
        Assert.Equal(60_000_000 / 1 - 4L * Rules.D.RiskMitesPerGuard, SeaRaids.Score(Rules, targets[1]));
        // a two-ship fleet never sends more than two
        for (int season = 0; season < 40; season++) Assert.True((SeaRaids.Plan(1234, season, Northmark(2), targets, v => true, Rules, 0, 1)?.PlanShips ?? 1) <= 2);
        // the same inputs, the same raid; the plan is the 0x7D3 stream
        var again = SeaRaids.Plan(1234, 8, Northmark(), targets, v => v == 7, Rules, 24 * 400, 1)!;
        Assert.Equal((raid.PlanShips, raid.PlanMen, raid.Settlement), (again.PlanShips, again.PlanMen, again.Settlement));
        // a realm of another culture never sails
        Assert.Null(SeaRaids.Plan(1234, 8, new RaiderRealm(3, 1, "kingdom", 4, 5), targets, v => true, Rules, 0, 1));
        Assert.Null(SeaRaids.Plan(1234, 8, Northmark(0), targets, v => true, Rules, 0, 1));
    }

    [Fact]
    public void SH10_InPeace_ARaiderCultureRaidsAboutThreeSeasonsInTen_OverviewQ7()
    {
        var targets = new List<RaidTarget> { new(9, 2, 7, 60_000_000, 4, 100) };
        int raids = 0; const int seasons = 2000;
        for (int s = 0; s < seasons; s++) if (SeaRaids.Plan(1234, s, Northmark(), targets, _ => false, Rules, 0, 1) != null) raids++;
        output.WriteLine($"{raids} raids in {seasons} peaceful seasons (300 permille asked)");
        Assert.InRange(raids, 500, 700);
        // at war it is every season
        for (int s = 0; s < 50; s++) Assert.NotNull(SeaRaids.Plan(1234, s, Northmark(), targets, _ => true, Rules, 0, 1));
        // a strong guard keeps them off (known armed men over the weak line)
        Assert.Null(SeaRaids.Plan(1234, 1, Northmark(), [new RaidTarget(9, 2, 7, 60_000_000, Rules.D.WeakGuards + 1, 100)], _ => true, Rules, 0, 1));
    }

    [Fact]
    public void SH10_TheVictimGetsARaidCasusBelli_OncePerRaidInPlay()
    {
        using var k = new ArmyRig(); k.Raised();
        int victim = k.Rule.Fiefs[0].Realm, raider = k.Rule.Fiefs[21].Realm; Assert.NotEqual(victim, raider);
        var pc = PoliticsEngine.Context(k.R.Sim, k.Pol, k.Rule, RuleChron.Of(k.State.Chronicle, k.State.Ids), k.Day);
        var raid = new SeaRaid { Realm = raider, Victim = victim };
        var cb = SeaRaids.GrantCb(pc, raid)!;
        Assert.Equal((victim, raider, CbKind.Raid), (cb.Holder, cb.Against, cb.Kind));
        Assert.Contains(k.Pol.Cbs, c => c == cb);
        Assert.Null(SeaRaids.GrantCb(pc, raid));                                                   // one unused grievance already stands
        Assert.Single(k.Pol.Cbs, c => c.Kind == CbKind.Raid && c.Holder == victim && c.Against == raider);
        Assert.Contains(k.Entries("casus_belli_given"), en => en.Id.Value == cb.Entry);
    }

    // ------------------------------------------------------------------ SH-11

    private static long Coin(EconomyState econ)
    {
        long t = 0; foreach (var kind in Enum.GetValues<PurseKind>()) t += econ.Ledger.TotalOf(kind).Mites;
        return t;
    }

    [Fact]
    public void SH11_TheLanding_TwoLongshipsAtDawn_SeventyMenAsPersons_TheVillageRaided_TheBell_TheBeachFight_TheWithdrawal()
    {
        using var k = new ArmyRig(); k.Raised();
        var aw = k.Aw; var econ = k.State.Economy!; var region = k.State.Population.Region; var rides = Ravensguard.Sim.Carts.VehicleDesk.BookOf(k.State, EconFixture.Content); var pt = k.State.Population.People;
        Assert.NotNull(aw.Sea);
        int victim = k.Rule.Fiefs[0].Realm, raider = k.Rule.Fiefs[21].Realm;
        var decl = RuleChron.Of(k.State.Chronicle, k.State.Ids).Chron(GameTime.FromGameHours(k.Now), "h_war_declared", "ch_h_war_declared", [], [], [new Cause(CauseKind.Diplomacy, null, "", "ch_note_h_cb_insult")]);
        k.Pol.Wars.Add(new War { Serial = k.Pol.NextWar++, Attacker = raider, Defender = victim, StartDay = k.Day, Cb = CbKind.Insult, DeclEntry = decl.Value });
        // the village: a victim's village with a household or two and a warband in its fief
        int village = -1;
        foreach (var f in k.Rule.Fiefs.Values.Where(f => f.Realm == victim).OrderBy(f => f.Fief))
            foreach (int s in region.Fiefs[f.Fief].Settlements)
                if (village < 0 && region.Settlements[s].Class != SettlementClass.Town && k.Book.WarbandOfFief(f.Fief) is { Members.Count: > 0 } && k.Aw.Households.MemberCount.Select((m, h) => (m, h)).Count(x => x.m > 0 && k.Aw.Households.Settlement[x.h] == s) >= 3) village = s;
        Assert.True(village >= 0, "a victim's village with households and a warband in its fief");
        var st = region.Settlements[village];
        int north = Enumerable.Range(0, 64).First(i => Ravensguard.Sim.Faith.ReligionBook.CultureOf(EconFixture.Content, i)?.Id == "north");
        long landHour = (k.Now / 24 + 1) * 24 + Rules.D.LandHourOfDay;
        var raid = new SeaRaid
        {
            Realm = raider, Victim = victim, Region = k.State.HomeRegion.Id, Settlement = village, Culture = north, War = true, PlanShips = 2, PlanMen = 70, PlannedHour = k.Now,
            BeachXmm = (st.CenterX + 1500L) * 1000, BeachYmm = st.CenterY * 1000L, LandHour = landHour,
        };
        var book = k.State.Naval ??= new NavalBook();
        var pc = PoliticsEngine.Context(k.R.Sim, k.Pol, k.Rule, RuleChron.Of(k.State.Chronicle, k.State.Ids), k.Day);
        aw.Enlist(k.Env(), book, raid, pc);
        Assert.Equal(1, raid.Id); Assert.Equal(RaidState.Sailing, raid.State); Assert.Empty(raid.Ships);
        Assert.Contains(k.Pol.Cbs, c => c.Kind == CbKind.Raid && c.Holder == victim && c.Against == raider);
        long alive0 = k.Aw.W.Vital.Alive; Assert.True(econ.MassAuditHolds());

        // before dawn: nothing on the beach
        k.To(landHour - 1); Assert.Equal(RaidState.Sailing, raid.State); Assert.DoesNotContain(rides.Vehicles.Rows, v => v.Water == Ravensguard.Sim.Carts.WaterState.Aground);

        // dawn: two longships on the sand with seventy men, made as people
        k.To(landHour);
        Assert.Equal(RaidState.Landed, raid.State);
        Assert.Equal(2, raid.Ships.Count);
        foreach (ulong s in raid.Ships)
        {
            var v = rides.Vehicles.Find(new EntityId(s))!; Assert.Equal("longship", rides.Templates.At(v.Template).Id);
            Assert.Equal(Ravensguard.Sim.Carts.WaterState.Aground, v.Water);
            Assert.InRange(Math.Abs(v.XMm - raid.BeachXmm), 0, 30_000); Assert.InRange(Math.Abs(v.YMm - raid.BeachYmm), 0, 30_000);
        }
        Assert.Equal(70, raid.Raiders.Count + raid.Guards.Count);
        Assert.InRange(raid.Guards.Count, 2 * Rules.D.GuardsMin, 2 * Rules.D.GuardsMax);                  // 6 to 10 stay with each ship
        foreach (ulong m in raid.Raiders.Concat(raid.Guards))
        {
            int r = pt.IndexOf(new EntityId(m)); Assert.True(r >= 0); Assert.Equal(PersonStatus.Alive, pt.Status[r]);
            Assert.Equal(north, pt.Culture[r]); Assert.Equal(Sex.Male, pt.Sex[r]); Assert.Equal(-1, pt.Household[r]);
            Assert.InRange(pt.AgeYears(r, (int)(k.Now / 24)), 18, 39);
        }
        Assert.InRange(k.Aw.W.Vital.Alive, alive0 + 60, alive0 + 90);                          // seventy more (the day's births ride along)
        Assert.Equal(raid.Raiders.Count + raid.Guards.Count, raid.Raiders.Concat(raid.Guards).Distinct().Count());

        // the run to the village, the raid: goods and coin moved, captives taken (the law of the north), a torch put
        var ledger = econ.Ledger; var grain0 = raid.TakenG;
        k.To(landHour + Rules.D.RunHours);
        Assert.Equal(RaidState.Raiding, raid.State);
        output.WriteLine($"raided {raid.Households} households: {raid.TakenG} g, {raid.TakenCoin} mites, {raid.Captives.Count} captives, {raid.Burned} burned; defender warband {raid.Defender} fights at hour +{raid.FightHour - landHour}; withdraw at +{raid.WithdrawHour - landHour}");
        Assert.True(raid.Households >= 1); Assert.True(raid.TakenG > 0 || raid.TakenCoin > 0);
        Assert.InRange(raid.Captives.Count, 1, Rules.D.CaptivesMax);
        foreach (ulong c in raid.Captives) Assert.True(k.State.Captivity!.Captives.ContainsKey(c));
        long inHolds = raid.Ships.Sum(s => rides.Vehicles.Find(new EntityId(s))!.Cargo.TotalMassG());
        Assert.True(inHolds > 0, "the grain lies in the ships' holds, real mass");
        Assert.True(econ.MassAuditHolds());
        // coin moved from the households' purses into the first ship's strongbox by a ledger transfer (never minted): the box holds exactly what the raid says it took
        Assert.True(ledger.TryFind(PurseKind.Chest, new EntityId(raid.Ships[0]), out var box)); Assert.Equal(raid.TakenCoin, ledger.Balance(box).Mites); Assert.True(raid.TakenCoin > 0);
        var fire = k.R.Sim.Get<Ravensguard.Sim.Burning.FireWorld>();
        output.WriteLine($"fire world {(fire == null ? "absent" : fire.Profiles().Count + " buildings")}");
        if (fire != null && fire.Profiles().Any(p => p.FlammableCells > 0)) Assert.True(raid.Burned >= 1, "a torch to a building");
        Assert.Equal(raid.LootHour, raid.BellHour);
        var bell = SeaRaids.BellHeard(Rules, st.CenterX * 1000L, st.CenterY * 1000L, (st.CenterX + 2900L) * 1000, st.CenterY * 1000L);
        Assert.True(bell); Assert.False(SeaRaids.BellHeard(Rules, st.CenterX * 1000L, st.CenterY * 1000L, (st.CenterX + 3100L) * 1000, st.CenterY * 1000L));
        Assert.Contains(k.Book.Reports, n => n.Realm == raider && n.Place == village);             // the lords hear of it
        Assert.True(raid.Defender >= 0 && raid.FightHour > raid.LootHour);

        // the withdrawal: they break off as the riders come, and are met on the beach
        var link = k.R.Sim.Get<Ravensguard.Sim.Economy.Markets.ContinentLink>();
        long emCoin0 = link?.Mine.EmigrantCoin ?? 0, emPers0 = link?.Mine.EmigrantPersons ?? 0, left0 = link?.Mine.TotalLeft ?? 0, sunk0 = ledger.Sunk(SinkReason.Emigrate).Mites, emig0 = k.Aw.W.Vital.Emigrants, coin0 = ledger.Audit().InPurses.Mites;
        ushort bar = econ.Goods.IdOf("iron_bar"); rides.Vehicles.Find(new EntityId(raid.Ships[0]))!.Cargo.CreateUpTo(bar, 5_000 / Math.Max(1, econ.Goods.MassG(bar, 1)), Ravensguard.Sim.Goods.CreateReason.Seed);       // P8-LW-4: a plunder that is not grain
        for (long h = landHour + 2; h <= landHour + 12 && !raid.Over; h++) k.To(h);
        Assert.True(raid.Over, "the raid ends within the day");
        output.WriteLine($"end {raid.State} at +{raid.EndHour - landHour}h; fought {raid.Fought}; raiders left {raid.Raiders.Count}, dead {raid.Dead}");
        Assert.True(raid.Fought, "the warband reached the beach before the ships pushed off");
        Assert.True(econ.MassAuditHolds()); Assert.Equal(0, ledger.Balance(box).Mites);                 // P7R-6/7: the strongbox is emptied either way
        var entry = k.Entries("sea_raid").Single();
        Assert.Contains(entry.Causes, c => c.Kind == CauseKind.War && c.EventId == decl);
        Assert.Equal(entry.Id.Value, raid.Entry);
        if (raid.State == RaidState.Caught)
        {
            var caught = k.Entries("raiders_caught").Single(); Assert.Contains(caught.Causes, c => c.EventId == entry.Id);
            Assert.All(raid.Ships, s => Assert.Equal(Ravensguard.Sim.Carts.WaterState.Aground, rides.Vehicles.Find(new EntityId(s))!.Water));
            Assert.Empty(raid.Raiders);
            Assert.Equal(coin0, ledger.Audit().InPurses.Mites);                                      // P7R-7: the box went to the captor, nothing left the books
        }
        else
        {
            // P7R-6: the loot and the men left through the emigration paths: coin sunk as emigrated, the grain exported, the people ledger counted
            int gone = raid.Raiders.Count + raid.Guards.Count;
            Assert.Equal(raid.TakenCoin, ledger.Sunk(SinkReason.Emigrate).Mites - sunk0); Assert.Equal(coin0 - raid.TakenCoin, ledger.Audit().InPurses.Mites);
            Assert.Equal(0, raid.Ships.Sum(s => rides.Vehicles.Find(new EntityId(s))!.Cargo.Rows().Where(r => econ.Food.IsGrain(r.Item1)).Sum(r => econ.Goods.MassG(r.Item1, r.Item2))));   // no plundered grain left in the hulls
            Assert.Equal(0, raid.Ships.Sum(s => rides.Vehicles.Find(new EntityId(s))!.Cargo.TotalMassG()));                                        // P8-LW-4: the non-grain cargo left as an export too
            Assert.Equal(gone, k.Aw.W.Vital.Emigrants - emig0);
            if (link != null) { Assert.Equal(raid.TakenCoin, link.Mine.EmigrantCoin - emCoin0); Assert.Equal(gone, link.Mine.EmigrantPersons - emPers0); Assert.Equal(gone, link.Mine.TotalLeft - left0); }
            Assert.Equal(RaidState.Gone, raid.State); Assert.Empty(k.Entries("raiders_caught"));
            Assert.All(raid.Ships, s => Assert.Equal(Ravensguard.Sim.Carts.WaterState.Afloat, rides.Vehicles.Find(new EntityId(s))!.Water));
            Assert.All(raid.Raiders.Concat(raid.Guards), m => Assert.Equal(PersonStatus.Departed, pt.Status[pt.IndexOf(new EntityId(m))]));
        }
        // a raid is never started twice for the same hour, and nothing else lands
        k.To(landHour + 30); Assert.Equal(raid.State, book.Raids[1].State); Assert.Equal(2, raid.Ships.Count);
    }

    [Fact]
    public void SH11_TheNavalSection_IsAbsentWhileNoRaid_AndSaveLoadSaveIsByteIdentical()
    {
        using var k = new ArmyRig(); k.Raised();
        var plain = WorldSave.Capture(k.State, "h", "0.1");
        Assert.True(!plain.Sections.ContainsKey(NavalSave.SectionName) || k.State.Naval!.Raids.Count == 0);                       // P8-LW-4: the running sea (barges, boats) may keep the section; no raid is in it
        var aw = k.Aw; var region = k.State.Population.Region;
        int village = Enumerable.Range(0, region.Settlements.Count).First(s => region.Settlements[s].Class != SettlementClass.Town && region.Settlements[s].Realm >= 0);
        var st = region.Settlements[village];
        var raid = new SeaRaid { Realm = 3, Victim = region.Settlements[village].Realm, Settlement = village, Culture = 0, PlanShips = 1, PlanMen = 31, BeachXmm = (st.CenterX + 1500L) * 1000, BeachYmm = st.CenterY * 1000L, LandHour = k.Now + 3, PlannedHour = k.Now };
        var book = k.State.Naval ??= new NavalBook();
        aw.Enlist(k.Env(), book, raid);
        k.To(k.Now + 4);                                                                           // landed and raiding by now
        Assert.NotEqual(RaidState.Sailing, raid.State);
        var file = WorldSave.Capture(k.State, "h", "0.1");
        Assert.Contains(NavalSave.SectionName, file.Sections.Keys);
        var bytes = SaveCodec.Encode(file);
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), k.R.Content, "h", null, k.R.W.Continent, k.R.W.History); back.Content = k.R.Content;
        Assert.Equal(book.Hash(), back.Naval!.Hash());
        Assert.NotEqual(0UL, book.Hash());
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, "h", "0.1")));                // save, load, save: identical
        output.WriteLine($"naval section {file.Sections[NavalSave.SectionName].Length} bytes, raid {raid.State}");
    }

    [Fact]
    public void SH11_AShipsCompanyTooSmallForTheRiders_IsCaughtOnTheBeach_TheShipsLieOnTheSand_AndTheChronicleSaysSo()
    {
        using var k = new ArmyRig(); k.Raised();
        var aw = k.Aw; var region = k.State.Population.Region; var rides = Ravensguard.Sim.Carts.VehicleDesk.BookOf(k.State, EconFixture.Content);
        int victim = k.Rule.Fiefs[0].Realm, raider = k.Rule.Fiefs[21].Realm;
        int village = -1;
        foreach (var f in k.Rule.Fiefs.Values.Where(f => f.Realm == victim).OrderBy(f => f.Fief))
            foreach (int s in region.Fiefs[f.Fief].Settlements)
                if (village < 0 && region.Settlements[s].Class != SettlementClass.Town && k.Book.WarbandOfFief(f.Fief) is { Members.Count: >= 3 }) village = s;
        Assert.True(village >= 0); var st = region.Settlements[village];
        long landHour = (k.Now / 24 + 1) * 24 + Rules.D.LandHourOfDay;
        // one ship, ten men, six to ten of them guards: a handful of raiders against the lord's warband
        var raid = new SeaRaid { Realm = raider, Victim = victim, Settlement = village, Culture = 0, PlanShips = 1, PlanMen = 10, BeachXmm = (st.CenterX + 1500L) * 1000, BeachYmm = st.CenterY * 1000L, LandHour = landHour, PlannedHour = k.Now };
        var book = k.State.Naval ??= new NavalBook(); aw.Enlist(k.Env(), book, raid);
        for (long h = k.Now + 1; h <= landHour + 12 && !raid.Over; h++) k.To(h);
        output.WriteLine($"{raid.State}: fought {raid.Fought}, raiders left {raid.Raiders.Count}, dead {raid.Dead}, guards {raid.Guards.Count}");
        Assert.True(raid.Fought);
        Assert.Equal(RaidState.Caught, raid.State); Assert.Empty(raid.Raiders);
        // P7R-7: a caught raid's strongbox is the captor's, not the beached ship's
        var led = k.State.Economy!.Ledger; Assert.True(led.TryFind(PurseKind.Chest, new EntityId(raid.Ships[0]), out var box));
        Assert.Equal(0, led.Balance(box).Mites); Assert.True(raid.TakenCoin > 0);
        Assert.True(led.Balance(led.Open(PurseKind.Treasury, region.Fiefs[region.Settlements[village].FiefIndex].Id)).Mites >= raid.TakenCoin);
        Assert.All(raid.Ships, s => Assert.Equal(Ravensguard.Sim.Carts.WaterState.Aground, rides.Vehicles.Find(new EntityId(s))!.Water));
        var entry = k.Entries("sea_raid").Single(); var caught = k.Entries("raiders_caught").Single();
        Assert.Contains(caught.Causes, c => c.Kind == CauseKind.Raid && c.EventId == entry.Id);
        Assert.Contains(entry.Causes, c => c.Kind == CauseKind.Raid && c.NoteKey == "ch_note_sh_raid_culture");      // in peace: the culture's own raiding is the cause
        Assert.True(k.State.Economy!.MassAuditHolds());
    }
}
