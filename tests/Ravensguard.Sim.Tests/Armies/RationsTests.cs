using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Unrest;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Armies;

/// <summary>P7-AM-D gate: AM-11 (rations, from the final rows), AM-12 (forage parties) and AM-13 (desertion), with the hunger, the joined warbands' rations, the after-battle fold-back and the save.</summary>
public class RationsTests(ITestOutputHelper output)
{
    private const int Mine = 11, Enemy = 0;

    private static (ArmyRig K, ArmyHost Host, HostId Hid) Camped(long worth = long.MaxValue / 4, int levies = 60)
    {
        var k = new ArmyRig();
        k.To(k.At(16, 7)); k.War(Mine, Enemy); k.Fund(Mine, 400_000_000); k.Fund(Enemy, 400_000_000);
        int point = k.Aw.MusterPointFor(Mine, Enemy); Assert.True(point >= 0);
        var host = k.Aw.CallMuster(k.Env(), new MusterCall(Mine, point, Worth: worth, MaxLevies: levies, FundDays: 40, FollowsWar: false))!;
        for (int d = k.Day; d < k.DayOf(32) && host.State != HostState.Camped; d++) k.To((d + 1) * 24L + 19);
        Assert.Equal(HostState.Camped, host.State);
        return (k, host, new HostId(host.Supply));
    }

    private static void Drain(ArmyRig k, HostId hid) => DrainOf(k.State, hid.Value);

    /// <summary>The host's chest is emptied back into the realm's treasury (the host is out of coin: nothing bought, nothing paid).</summary>
    private static void DrainOf(WorldState st, int hostId)
    {
        var led = st.Economy!.Ledger;
        if (!led.TryFind(PurseKind.Chest, HostIds.Chest(1_000_000 + st.Armies!.Hosts.Values.Single(h => h.Supply == hostId).Id), out var chest) || !led.TryFind(PurseKind.Treasury, Ravensguard.Sim.Land.LandWorld.RealmTreasuryId(Mine), out var tr)) return;
        long bal = led.Balance(chest).Mites;
        if (bal > 0) led.Transfer(chest, tr, new Coin(bal), TransferReason.Hoard);
    }

    /// <summary>Every man of the host: the levies by row, then the men of the warbands that joined it.</summary>
    private static List<ulong> Everyone(ArmyRig k, ArmyHost host, HostId hid)
    {
        var pt = k.State.Population.People; var l = k.Supply().Persons(hid).Select(r => pt.IdOf(r).Value).ToList();
        foreach (var j in host.Warbands) if (k.Book.Warbands.TryGetValue(j.Warband, out var wb) && wb.Task == WarbandTask.JoinHost && wb.Here == host.Point && wb.Stops.Count == 0) l.AddRange(wb.Members);
        return l;
    }

    // ---------------------------------------------------------------- AM-11

    [Fact]
    public void AM11_Rations_1000Men120Horses_FromTheFinalRows_AndAJoinedWarbandsMenEatAtTheHostsTable_NotPaidByItsChest()
    {
        using var k = new ArmyRig();
        k.To(k.At(16, 7)); var hs = k.Supply(); var econ = k.State.Economy!; var led = econ.Ledger;
        var chest = led.Open(PurseKind.Chest, HostIds.Chest(9001)); var bag = econ.OpenStore(StoreKind.Sealed, HostIds.Baggage(9001));
        var hid = hs.Register(new HostSpec(Mine, EntityId.None, chest, bag, new Roster([(TroopRole.Spearman, 1000)]), 0, new AnimalCounts(120, 0, 0, 0), 30, new SupplyPolicy()));
        int day = k.Day; var n = hs.Needs(hid, day);
        output.WriteLine($"1,000 men and 120 riding horses: staple {n.StapleG / 1000} kg, relish {n.RelishG / 1000} kg, hay {n.FodderHayG / 1000} kg, oats {n.FodderOatsG / 1000} kg, firewood {n.FirewoodG / 1000} kg, pay {n.PayMites} mites");
        Assert.Equal(1_000_000, n.StapleG); Assert.Equal(150_000, n.RelishG);
        Assert.Equal(120 * 8000, n.FodderHayG); Assert.Equal(120 * 2500, n.FodderOatsG);             // the final rows (species.jsonc: a riding horse 8 kg of hay and 2.5 kg of oats)
        Assert.Equal(1_000 * 250, n.PayMites);
        Assert.Equal(15_000_000, 15 * n.StapleG); Assert.Equal(30, (int)(15 * n.StapleG / 500_000));  // 15 days need 15 t: 30 carts of 500 kg
        // a joined warband's men eat and burn wood at the host's table, and the host's chest pays them nothing
        hs.SetRetainers(hid, 40); var m = hs.Needs(hid, day);
        Assert.Equal(1_040_000, m.StapleG); Assert.Equal(1_040 * 150, m.RelishG); Assert.Equal(n.PayMites, m.PayMites);
        long mult = n.FirewoodG / (1000L * SupplyConfig.FirewoodG); Assert.Equal(n.FirewoodG + 40L * SupplyConfig.FirewoodG * mult, m.FirewoodG);
        // the real thing: the ruler's warband rides to the point and joins; the day after, its men are on the host's account
        k.Fund(Mine, 400_000_000); k.War(Mine, Enemy);
        int point = k.Aw.MusterPointFor(Mine, Enemy);
        var host = k.Aw.CallMuster(k.Env(), new MusterCall(Mine, point, Worth: long.MaxValue / 4, MaxLevies: 40, FundDays: 20))!;
        k.To(k.At(26, 19));
        var hid2 = new HostId(host.Supply); var a = hs.Account(hid2);
        int joined = 0; foreach (var j in host.Warbands) if (k.Book.Warbands.TryGetValue(j.Warband, out var wb) && wb.Task == WarbandTask.JoinHost && wb.Here == host.Point && wb.Stops.Count == 0) joined += wb.Members.Count;
        output.WriteLine($"host of {a.Total} levies; {host.Warbands.Count} warbands joined, {joined} men at the point, on the host's account {a.Retainers}");
        Assert.Equal(joined, a.Retainers);
        var need = hs.Needs(hid2, k.Day); Assert.Equal((long)(a.Total + a.Retainers) * 1000, need.StapleG);
        Assert.Equal(0, a.Men.Where((c, i) => i != (int)TroopRole.Levy).Sum());                         // no retinue in the roster: the warband is the lord's to pay
        Assert.True(led.Audit().IsConserved); Assert.True(econ.MassAuditHolds());
    }

    // ---------------------------------------------------------------- AM-13

    [Fact]
    public void AM13_TheRule_ArrearsEightDaysAndHungry_Is20_ALevyAtTheHarvestPlus30_ARetinueHalf_TheDrillAndTheCeiling()
    {
        var r = new Ravensguard.Content.ArmyRulesDef { Id = "x" };
        Assert.Equal(20, ArmyWorld.DesertionPm(r, 8, true, false, 0, 60, false, 0, false));                  // 2 x (8 - 3) + 10
        Assert.Equal(50, ArmyWorld.DesertionPm(r, 8, true, true, 0, 60, false, 0, false));                   // a levy at the harvest +30
        Assert.Equal(10, ArmyWorld.DesertionPm(r, 8, true, false, 0, 60, false, 0, true));                   // a retinue halves it
        Assert.Equal(0, ArmyWorld.DesertionPm(r, 3, false, false, 0, 60, false, 0, false));                  // three days of arrears are borne
        Assert.Equal(2, ArmyWorld.DesertionPm(r, 4, false, false, 0, 60, false, 0, false));
        Assert.Equal(55, ArmyWorld.DesertionPm(r, 0, false, false, 5, 20, true, 0, false));                  // 5 x 5 days past the owed, the host's heart gone (+10), a defeat (+20)
        Assert.Equal(25, ArmyWorld.DesertionPm(r, 8, true, true, 0, 60, false, 1000, false));                // 20 + 30, less 5 x (1,000 / 200) for a drilled man
        Assert.Equal(0, ArmyWorld.DesertionPm(r, 8, true, false, 0, 60, false, 1000, false));               // never below nothing
        Assert.Equal(200, ArmyWorld.DesertionPm(r, 100, true, true, 40, 0, true, 0, false));                 // never above the ceiling
    }

    [Fact]
    public void AM13_AnUnpaidHungryHost_Deserts_ByTheRule_ToHomeOrTheRoads_RealPeopleOnTheRolls_TheDeadStayDead()
    {
        var (k, host, hid) = Camped(); using var _k = k;
        var hs = k.Supply(); var aw = k.Aw; var pt = k.State.Population.People; var econ = k.State.Economy!; var led = econ.Ledger;
        // run until the lord pays on (the owed days are past), then the chest is emptied each day: no food bought, no pay
        for (int d = k.Day; d < k.DayOf(40) && !host.Paying; d++) k.To((d + 1) * 24L + 19);
        Assert.True(host.Paying, "the owed days are done and the lord pays on");
        var start = Everyone(k, host, hid); int levies0 = hs.Persons(hid).Count, day0 = k.Day; long p0 = aw.RationsOf(hid.Value).DesertedPersons, w0 = k.Entries("desertion_wave").Where(w => w.Subjects.Contains(new EntityId(host.Commander))).Sum(w => w.Params!.Single(p => p.Name == "men").Value);
        output.WriteLine($"day {day0} (doy {GameTime.FromGameHours(day0 * 24L).DayOfYear}): the lord pays his levy on; {levies0} levies and {start.Count - levies0} men of {host.Warbands.Count} joined warbands, morale {aw.RationsOf(hid.Value).Morale}");
        bool OnRolls(ulong m) => hs.IsOnCampaign(aw.RowOf(m)) || host.Warbands.Any(j => k.Book.Warbands.TryGetValue(j.Warband, out var wb) && wb.Task == WarbandTask.JoinHost && wb.Members.Contains(m));
        var log = new List<string>(); int days = 0; List<ulong> left = []; int dead = 0; long waveMen = 0;
        for (int d = 0; d < 14; d++)
        {
            foreach (ulong m in Everyone(k, host, hid)) if (!start.Contains(m)) start.Add(m);              // warbands arrive at the point day by day: every man ever on the rolls is followed
            int day = k.Day + 1; k.To(day * 24L + 17); Drain(k, hid); k.To(day * 24L + 19);
            if (!hs.Exists(hid)) { log.Add($"day {day}: the host is no more ({host.State})"); break; }
            var st = aw.RationsOf(hid.Value); var res = hs.Last(hid)!; var acc = hs.Account(hid); days++;
            left = start.Where(m => !OnRolls(m)).ToList(); dead = left.Count(m => !pt.IsAlive(aw.RowOf(m))); waveMen = k.Entries("desertion_wave").Where(w => w.Subjects.Contains(new EntityId(host.Commander))).Sum(w => w.Params!.Single(p => p.Name == "men").Value);
            log.Add($"day {day} (doy {GameTime.FromGameHours(day * 24L).DayOfYear}): fed {st.FedPm / 10}%, unpaid {res.UnpaidDays[(int)TroopRole.Levy]}d, morale {st.Morale}, {acc.Men[(int)TroopRole.Levy]} levies on the rolls, gone so far {st.DesertedPersons}");
            Assert.Equal(st.DesertedPersons - p0, left.Count - dead);                                  // what the rolls lost is what the rule says left
            Assert.Equal(st.DesertedPersons + st.DesertedCounts, waveMen - w0 + p0);                  // and the chronicle's waves say the same
        }
        foreach (var l in log) output.WriteLine(l);
        Assert.True(days >= 5, "the host lasted some days"); Assert.True(left.Count - dead > 0, "men left");
        // every deserter is a person of the world: alive, in a band or walking home on a company's record, his household's labour away until he arrives
        int toBand = 0, toHome = 0, walking = 0;
        foreach (ulong m in left)
        {
            int r = aw.RowOf(m); if (pt.Status[r] != PersonStatus.Alive) continue;
            var band = k.Book.BandWith(m);
            if (band != null) { toBand++; Assert.Equal(Occupation.Bandit, pt.Occupation[r]); Assert.True(k.Book.IsAway(m)); }
            else { toHome++; if (k.Book.IsAway(m)) walking++; }
        }
        output.WriteLine($"after {days} days: {left.Count - dead} of {start.Count} left: {toHome} home ({walking} still walking), {toBand} to the roads in {k.Book.Bands.Count} bands; {dead} died");
        foreach (var bd in k.Book.Bands.Values) output.WriteLine($"band {bd.Id}: {bd.Members.Count} men near settlement {bd.Lair} (formed day {bd.FormedDay}, {bd.Origin})");
        Assert.True(toHome + toBand > 0);
        // the wave is in the chronicle with its cause, and the band's entry stands on the wave
        var waves = k.Entries("desertion_wave").Where(w => w.Subjects.Contains(new EntityId(host.Commander))).ToList(); Assert.NotEmpty(waves);
        Assert.All(waves, w => Assert.Contains(w.Causes, c => c.Kind == CauseKind.Desertion && c.EventId?.Value == host.Entry));
        foreach (var bf in k.Entries("bandits_formed")) Assert.Contains(bf.Causes, c => c.Kind == CauseKind.Desertion && waves.Any(w => w.Id == c.EventId));
        // hunger is the nutrition reserve: the men's reserve is down
        Assert.Contains(start, m => econ.NutritionCd[aw.RowOf(m)] < HouseholdNeeds.MaxNutritionCd);
        // coin and mass hold; no person made or lost
        Assert.True(led.Audit().IsConserved); Assert.True(econ.MassAuditHolds());
        Assert.All(start, m => Assert.True(aw.RowOf(m) >= 0));
    }

    // ---------------------------------------------------------------- AM-12

    [Fact]
    public void AM12_ForageParties_ATenthOfTheMenByARotation_EnemyLandIsPlundered_EveryRobbedHouseholdBearsForaged_TheStripLimitCutsTheYield()
    {
        var (k, host, hid) = Camped(levies: 100); using var _k = k;
        var hs = k.Supply(); var aw = k.Aw; var region = k.State.Population.Region; var econ = k.State.Economy!; var ub = k.State.Unrest!;
        var a = hs.Account(hid); int men = a.Total + a.Retainers;
        // the party: a tenth of the men, each a real man of the host, a different tenth on the next day, never a wounded man
        var p1 = aw.ForageParty(host, 30); var p2 = aw.ForageParty(host, 31);
        Assert.Equal(men / 10, p1.Count); Assert.Equal(p1.Count, p1.Distinct().Count()); Assert.NotEqual(p1, p2);
        Assert.All(p1, m => Assert.True(hs.IsOnCampaign(aw.RowOf(m)) || k.Book.Warbands.Values.Any(w => w.Members.Contains(m))));
        var all = Enumerable.Range(0, 10).SelectMany(i => aw.ForageParty(host, 100 + i)).Distinct().Count(); Assert.True(all >= Math.Min(men, p1.Count * 10 - 1), "in ten days every man has had his turn");
        // the ground: a village of the enemy realm; the host stands in it and the policy follows by the next day's rations
        int enemyVillage = -1; long best = 0;
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            int fi = region.Settlements[s].FiefIndex; if (fi < 0 || !k.Rule.Fiefs.TryGetValue(fi, out var f) || f.Realm != Enemy) continue;
            int hh = aw.Households.Count == 0 ? 0 : Enumerable.Range(0, aw.Households.Count).Count(i => aw.Households.MemberCount[i] > 0 && aw.Households.Settlement[i] == s);
            if (hh > best) { best = hh; enemyVillage = s; }
        }
        Assert.True(enemyVillage >= 0);
        Assert.Equal(ForagePolicy.None, aw.PolicyAt(host, host.Point)); Assert.Equal(ForagePolicy.EnemyLand, aw.PolicyAt(host, enemyVillage));
        hs.ForageDrawOverride = 1000;
        // stand in the village, with an empty chest and the baggage empty so the foragers feed the host
        int nextDay = k.Day + 1; k.To(nextDay * 24L + 17); hs.Update(hid, hs.PlaceAt(enemyVillage), HostMode.Camp); Drain(k, hid); k.To(nextDay * 24L + 19);
        Assert.Equal(ForagePolicy.EnemyLand, hs.Account(hid).Forage);
        var cutSeen = new List<int>(); long forageG = 0; var res0 = hs.Last(hid)!;
        for (int d = 0; d < 4; d++)
        {
            int day = k.Day + 1; k.To(day * 24L + 17); Drain(k, hid); k.To(day * 24L + 19);
            var res = hs.Last(hid)!; forageG += res.DeliveredFrom(SupplySource.Forage); cutSeen.Add(hs.Account(hid).ForageCutPm);
            output.WriteLine($"day {day}: forage delivered {res.DeliveredFrom(SupplySource.Forage) / 1000} kg of the {res.Needed.StapleG / 1000} kg wanted, cut {hs.Account(hid).ForageCutPm} pm, strip days {aw.RationsOf(hid.Value).StripDays}");
        }
        Assert.True(forageG > 0, "the foragers brought in grain from the enemy village's stores");
        Assert.Equal(0, cutSeen[0]); Assert.Equal(667, cutSeen[^1]);                                              // the same ground for three days yields a third
        Assert.NotEmpty(k.Entries("army_foraged"));
        // each robbed household bears the grievance
        var robbed = ub.Rows.Where(kv => kv.Value.Any(r => r.Kind == "foraged")).Select(kv => kv.Key).ToList(); Assert.NotEmpty(robbed);
        Assert.True(econ.MassAuditHolds()); Assert.True(econ.Ledger.Audit().IsConserved);
    }

    // ---------------------------------------------------------------- after a battle

    [Fact]
    public void FoldBack_TheDeadOffTheRollsTheirHouseholdsAggrieved_TheCapturedAndTheLeftBehindAwayTheWoundedCarried_MoraleAndTheRetreat()
    {
        var (k, host, hid) = Camped(levies: 80); using var _k = k;
        var hs = k.Supply(); var aw = k.Aw; var pt = k.State.Population.People; var ub = k.State.Unrest!; var a = hs.Account(hid); a.Carts = 2;
        var men = hs.Persons(hid).Select(r => pt.IdOf(r).Value).ToList(); int n = aw.FitMen(host); Assert.True(men.Count >= 60 && n >= men.Count);
        var ob = new RegionOutbox(k.R.Sim.Ids);
        var killed = men.Take(5).ToList(); var captured = men.Skip(5).Take(4).ToList(); var hurt = men.Skip(9).Take(10).ToList(); var fled = men.Skip(19).Take(3).ToList();
        foreach (ulong m in killed) Demography.Kill(k.R.Sim, aw.RowOf(m), DeathCause.Violence, GameTime.FromGameHours(k.Now), ob);
        var cmds = new List<BattleCommand>();
        cmds.AddRange(killed.Select(m => new BattleCommand(m, BattleCommandKind.Status, SoldierStatus.Dead)));
        cmds.AddRange(captured.Select(m => new BattleCommand(m, BattleCommandKind.Status, SoldierStatus.Captured)));
        cmds.AddRange(hurt.Select(m => new BattleCommand(m, BattleCommandKind.Status, SoldierStatus.Down)));
        cmds.AddRange(fled.Select(m => new BattleCommand(m, BattleCommandKind.Status, SoldierStatus.Fled)));
        int morale0 = aw.RationsOf(hid.Value).Morale; int day = k.Day;
        var fr = aw.FoldBack(k.Env(), host, cmds, won: false);
        output.WriteLine($"before {fr.Before}: dead {fr.Dead}, captured {fr.Captured}, carried {fr.Carried}, left behind {fr.LeftBehind}, routed {fr.Routed}; {fr.Left} on the rolls; retreat {fr.Retreating}, disbanded {fr.Disbanded}");
        Assert.Equal(n, fr.Before); Assert.Equal(5, fr.Dead); Assert.Equal(4, fr.Captured); Assert.Equal(8, fr.Carried); Assert.Equal(2, fr.LeftBehind); Assert.Equal(3, fr.Routed);
        Assert.Equal(n - 5 - 4 - 2, fr.Left);
        Assert.All(killed, m => { Assert.False(hs.IsOnCampaign(aw.RowOf(m))); Assert.False(pt.IsAlive(aw.RowOf(m))); Assert.Contains(ub.Live(k.State.Population.Households.IdOf(pt.Household[aw.RowOf(m)]).Value, day), r => r.Kind == "levy_death"); });
        Assert.All(captured, m => { Assert.False(hs.IsOnCampaign(aw.RowOf(m))); Assert.True(pt.IsAlive(aw.RowOf(m))); Assert.True(k.Book.IsAway(m)); });
        var st = aw.RationsOf(hid.Value); Assert.Equal(8, st.Wounded.Count); Assert.Equal(Math.Clamp(morale0 - 25, 0, 100), st.Morale); Assert.Equal(day, st.DefeatDay);
        Assert.Equal(aw.FitMen(host), fr.Left - 8);
        // the defeat's three days weigh on the rule; the wounded are fit again after the carry days
        k.To((day + 11) * 24L + 19); Assert.Empty(aw.RationsOf(hid.Value).Wounded);
        // a win lifts the morale; a host that lost most of its men is disbanded where it stands, the levies walking home
        var all = Everyone(k, host, hid);
        var win = aw.FoldBack(k.Env(), host, [], won: true); Assert.False(win.Retreating); Assert.True(aw.RationsOf(hid.Value).Morale >= Math.Min(100, st.Morale + 15 - 12));
        var crush = all.Take(all.Count * 9 / 10).Select(m => new BattleCommand(m, BattleCommandKind.Status, SoldierStatus.Captured)).ToList();
        var end = aw.FoldBack(k.Env(), host, crush, won: false);
        Assert.True(end.Disbanded); Assert.Equal(HostState.Disbanding, host.State);
        Assert.Contains(k.Entries("levy_released"), e => e.Causes.Any(c => c.NoteKey == "ch_note_am_defeat"));
        Assert.True(k.State.Economy!.Ledger.Audit().IsConserved);
    }

    [Fact]
    public void FoldBack_AHostThatLostMoreThanHalfTurnsBackToItsMusterPoint()
    {
        var (k, host, hid) = Camped(levies: 80); using var _k = k;
        var hs = k.Supply(); var aw = k.Aw; var pt = k.State.Population.People; var region = k.State.Population.Region;
        int other = -1; for (int s = 0; s < region.Settlements.Count && other < 0; s++) if (s != host.Point && aw.PathOf(s, host.Point) != null && aw.DistM(s, host.Point) > 3000) other = s;
        Assert.True(other >= 0); hs.Update(hid, hs.PlaceAt(other), HostMode.Camp);
        var men = Everyone(k, host, hid);
        var lose = men.Take(men.Count * 6 / 10).Select(m => new BattleCommand(m, BattleCommandKind.Status, SoldierStatus.Captured)).ToList();
        var fr = aw.FoldBack(k.Env(), host, lose, won: false);
        Assert.True(fr.Retreating); Assert.False(fr.Disbanded); Assert.Equal(HostState.Retreating, host.State);
        Assert.True(k.Book.Marches.TryGetValue(hid.Value, out var m) && m.Stops.Last() == host.Point);
    }

    // ---------------------------------------------------------------- saves

    [Fact]
    public void SaveLoadContinue_WithAHungryHostLosingMen_EqualsTheUninterruptedRun()
    {
        var (k, host, hid) = Camped(); using var _k = k;
        var hs = k.Supply();
        for (int d = k.Day; d < k.DayOf(40) && !host.Paying; d++) k.To((d + 1) * 24L + 19);
        for (int d = 0; d < 4; d++) { int day = k.Day + 1; k.To(day * 24L + 17); Drain(k, hid); k.To(day * 24L + 19); }
        Assert.True(k.Book.Rations.Count > 0);
        k.To((k.Day + 1) * 24L + 17); Drain(k, hid);                                              // the next day's drain is done; the slot is still to run
        var bytes = SaveCodec.Encode(WorldSave.Capture(k.State, "h", "0.1")); long target = (k.Day + 1 + 8) * 24L + 20;
        for (long h = k.Now + 1; h <= target; h++) { k.To(h); if (h % 24 == 17) Drain(k, hid); }
        var want = k.Book.Hash(); var wantSection = SaveCodec.SerializeCompressed(ArmySave.Capture(k.Book)); var wantSupply = k.State.WarSupply!.Hash(); var wantChron = k.State.Chronicle.Hash();
        Assert.True(k.Book.Rations[hid.Value].DesertedPersons > 0, "men left in the continued run");
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), k.R.Content, "h", null, k.R.W.Continent, k.R.W.History); st.Content = k.R.Content;
        using (var rt = new WorldRuntime(st, k.R.Content, new MemoryRegionPager()))
        {
            for (long h = rt.HourDone + 1; h <= target; h++)
            {
                rt.AdvanceTo(GameTime.FromGameHours(h));
                if (h % 24 == 17) DrainOf(st, hid.Value);
            }
            Assert.Equal(want, st.Armies!.Hash()); Assert.Equal(wantSection, SaveCodec.SerializeCompressed(ArmySave.Capture(st.Armies!)));
            Assert.Equal(wantSupply, st.WarSupply!.Hash()); Assert.Equal(wantChron, st.Chronicle.Hash());
            Assert.True(st.Economy!.Ledger.Audit().IsConserved);
        }
        output.WriteLine($"saved mid-hunger; {k.Book.Rations[hid.Value].DesertedPersons} men gone by the end in both runs (hash {want:x})");
    }
}
