using MessagePack;
using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Unrest;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.WorldGen.Region;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Armies;

internal static class MusterRigX
{
    /// <summary>The absolute game hour of an hour of a day of the year, in the first year of play (the rig starts on the last day of a year).</summary>
    public static long At(this ArmyRig k, int doy, int hour, int year = 0)
    {
        int doy0 = GameTime.FromGameHours(k.H0).DayOfYear;
        long nextYear = k.H0 / 24 - (doy0 - 1) + TimeConst.DaysPerYear * (1 + year);
        return (nextYear + doy - 1) * 24 + hour;
    }
    public static int DayOf(this ArmyRig k, int doy, int year = 0) => (int)(k.At(doy, 0, year) / 24);
    public static HostSupply Supply(this ArmyRig k) => k.Aw.Supply!;
    /// <summary>Coin to a realm's treasury (a seeded mint: the audit counts it as a source).</summary>
    public static void Fund(this ArmyRig k, int realm, long mites)
    {
        var led = k.State.Economy!.Ledger;
        Assert.True(led.TryFind(PurseKind.Treasury, Ravensguard.Sim.Land.LandWorld.RealmTreasuryId(realm), out var p));
        led.Mint(p, new Coin(mites), SourceReason.Seed);
    }
    /// <summary>The realm of most fiefs against the realm of two: a war declared now (its declaration an entry of the chronicle).</summary>
    public static War War(this ArmyRig k, int mine = 11, int enemy = 0)
    {
        var decl = RuleChron.Of(k.State.Chronicle, k.State.Ids).Chron(GameTime.FromGameHours(k.Now), "h_war_declared", "ch_h_war_declared", [], [], [new Cause(CauseKind.Diplomacy, null, "", "ch_note_h_cb_insult")]);
        var w = new War { Serial = k.Pol.NextWar++, Attacker = mine, Defender = enemy, StartDay = k.Day, Cb = CbKind.Insult, DeclEntry = decl.Value };
        k.Pol.Wars.Add(w); return w;
    }
    public static long Mites(this ArmyRig k, int household) => k.State.Economy!.Ledger.Balance(k.State.Economy.HouseholdPurse[household]).Mites;
}

/// <summary>P7-AM-B gate: AM-3 (the levy draw), AM-4 (evasion), AM-5 (the harvest cost), AM-6 (muster timing) and AM-18 (winter), with the owed days, the pay after them, the release and walk home, the vassals, the AI's call and the save.</summary>
public class MusterTests(ITestOutputHelper output)
{
    private const int Mine = 11, Enemy = 0;

    private static (ArmyRig K, War W, int Point) Rig(long treasury = 400_000_000, int year = 0)
    {
        var k = new ArmyRig();
        k.To(k.At(16, 7, year));                                                                         // the first day of summer, after the AI's hour: no war yet, so no call of its own
        var w = k.War(Mine, Enemy); k.Fund(Mine, treasury); k.Fund(Enemy, treasury);
        int point = k.Aw.MusterPointFor(Mine, Enemy); Assert.True(point >= 0);
        return (k, w, point);
    }

    /// <summary>The households keep a spear, a round shield and a tunic each: the world's households hold goods, not made pieces, so these are real items, owned by the household, for the levy to bring.</summary>
    private static void GiveKit(ArmyRig k, Func<int, bool> only)
    {
        var ht = k.State.Population.Households; var items = k.State.Economy!.Items;
        for (int h = 0; h < ht.Count; h++)
        {
            if (ht.MemberCount[h] == 0 || ht.Settlement[h] < 0 || !only(h)) continue;
            var owner = ht.IdOf(h);
            items.Add(k.State.Ids, "spear", ItemQuality.Common, owner, [new ItemComponent("iron", 900, 1000)]);
            items.Add(k.State.Ids, "round_shield", ItemQuality.Common, owner, [new ItemComponent("oak", 3000, 1000)]);
            items.Add(k.State.Ids, "tunic", ItemQuality.Common, owner, [new ItemComponent("wool", 900, 1000)]);
        }
    }

    private static bool Able(ArmyRig k, int row, int day) => k.State.Population.People.Sex[row] == Sex.Male && k.State.Population.People.IsAlive(row) && k.State.Population.People.AgeYears(row, day) is >= 16 and <= 45;

    // ---------------------------------------------------------------- AM-3

    [Fact]
    public void AM3_TheLevyDraw_AtWarOneInFiveHouseholds_RealMenOneAHousehold_ByVillage_TheOnlyWorkerLast_WithTheirGear()
    {
        var (k, war, point) = Rig(); using var _k = k;
        Assert.Equal(62, k.Aw.Ew!.Taxes.LevyQuota(312, true)); Assert.Equal(31, k.Aw.Ew.Taxes.LevyQuota(312, false));
        var aw = k.Aw; var pt = k.State.Population.People; var ht = k.State.Population.Households; var region = k.State.Population.Region; var econ = k.State.Economy!;
        int fief = Enumerable.Range(0, region.Fiefs.Count).Where(f => aw.FiefsOfRealm(Mine).Any(x => x.Fief == f)).OrderByDescending(aw.HouseholdsOfFief).First();
        int hh = aw.HouseholdsOfFief(fief), quota = aw.QuotaOf(fief, true); Assert.Equal(hh / 5, quota);
        GiveKit(k, h => region.Settlements[ht.Settlement[h]].FiefIndex == fief);
        int people0 = pt.Count; var itemsBefore = econ.Items.LiveRows().Select(r => econ.Items.At(r).Id).ToHashSet();
        var host = aw.CallMuster(k.Env(), new MusterCall(Mine, point));
        Assert.NotNull(host); Assert.Equal(HostState.Summoned, host!.State); Assert.Equal(k.DayOf(16), host.SummonsDay);
        var co = host.Companies.Single(c => c.Fief == fief); Assert.Equal(quota, co.Quota);
        k.To(k.At(17, 19));                                                                         // the daily step of the day after the summons
        Assert.True(co.IsDrawn); Assert.Equal(k.DayOf(17), co.Drawn);
        output.WriteLine($"fief {fief}: {hh} households, quota {quota}, drew {co.Men.Count} (asked {co.Asked}, evaded {co.Evaded}); the realm's host: {host.Companies.Count} companies, {host.Companies.Sum(c => c.Men.Count)} men");
        Assert.Equal(quota, co.Men.Count);
        var day = k.Day;
        var rows = co.Men.Select(m => aw.RowOf(m)).ToList();
        Assert.All(rows, r => { Assert.True(r >= 0 && r < people0, "a person of the world"); Assert.True(Able(k, r, day)); Assert.Equal(fief, region.Settlements[ht.Settlement[pt.Household[r]]].FiefIndex); Assert.True(k.Supply().IsOnCampaign(r)); });
        Assert.Equal(rows.Count, rows.Select(r => pt.Household[r]).Distinct().Count());              // one man a household
        // by village, in the lord's order, never past the cap; the only worker of a household is asked after the households that have another
        int seat = region.Fiefs[fief].SeatSettlement; var perVillage = rows.GroupBy(r => ht.Settlement[pt.Household[r]]).ToDictionary(g => g.Key, g => g.Count());
        var households = Enumerable.Range(0, ht.Count).Where(i => ht.MemberCount[i] > 0 && ht.Settlement[i] >= 0 && region.Settlements[ht.Settlement[i]].FiefIndex == fief).GroupBy(i => ht.Settlement[i]).ToDictionary(g => g.Key, g => g.ToList());
        int cap = k.Supply().Economy.Farming.Settings.Def.LevyVillageCapPct;
        Assert.All(perVillage, kv => Assert.True(kv.Value <= Math.Max(1, households[kv.Key].Count * cap / 100)));
        Assert.True(perVillage.Count < households.Count, "some villages were not asked");
        int Workers(int h) => ht.Members(h).Count(m => pt.IsAlive(m) && pt.AgeYears(m, day) is >= 16 and < 60);
        int sole = 0;
        foreach (var (v, drawn) in perVillage)
        {
            var mine = rows.Where(r => ht.Settlement[pt.Household[r]] == v).Select(r => pt.Household[r]).ToList();
            if (!mine.Any(h => Workers(h) <= 1)) continue;
            sole++;
            foreach (int h in households[v].Where(h => Workers(h) >= 2)) Assert.True(mine.Contains(h) || !ht.Members(h).Any(m => Able(k, m, day) && !aw.IsStaff(m) && !k.Supply().IsOnCampaign(m)), "a household with another worker gave before the only worker's");
        }
        output.WriteLine($"{sole} villages gave the only worker of a household, after every household that had a second worker");
        // the gear: real items of the household, moved to the man; none made
        Assert.True(econ.MassAuditHolds());
        int armed = 0; var classes = new Dictionary<string, int>();
        foreach (ulong m in co.Men)
        {
            var row = k.State.Soldiers!.Of(m); Assert.NotNull(row); Assert.Equal("levy", row!.Role); Assert.Equal((long)host.Commander, row.Lord);
            classes[row.Template] = classes.GetValueOrDefault(row.Template) + 1;
            foreach (var (slot, item) in row.Worn) { Assert.Equal(m, econ.Items.Get(new EntityId(item)).Owner.Value); Assert.Contains(new EntityId(item), itemsBefore); armed++; }
        }
        output.WriteLine($"gear by wealth: {string.Join(", ", classes.OrderBy(c => c.Key).Select(c => $"{c.Key} {c.Value}"))}; {armed} pieces moved from households to men");
        Assert.True(armed > 0, "some of the men brought something");
        // the chronicle: the call is a root caused by the war's declaration; each fief's draw is caused by the call
        var called = k.Entries("muster_called").Single(); Assert.Equal(host.Entry, called.Id.Value);
        Assert.Contains(called.Causes, c => c.Kind == CauseKind.War && c.EventId?.Value == war.DeclEntry);
        var drawnEntry = k.Entries("levy_drawn").Single(e => e.Params!.Any(p => p.Name == "men" && p.Value == co.Men.Count) && e.Subjects.Contains(region.Settlements[seat].Id));
        Assert.Contains(drawnEntry.Causes, c => c.EventId?.Value == host.Entry);
        Assert.True(k.State.Economy!.Ledger.Audit().IsConserved);
    }

    [Fact]
    public void AM3_InPeaceTheLevyIsOneInTenHouseholds_AndTheOwedDaysEnd_TheLordWontPay_TheMenWalkHome()
    {
        using var k = new ArmyRig();
        k.To(k.At(3, 7));
        k.Fund(Mine, 400_000_000);
        int point = k.Aw.MusterPointFor(Mine, Enemy);
        var host = k.Aw.CallMuster(k.Env(), new MusterCall(Mine, point, MaxLevies: 60))!;
        Assert.Null(k.Pol.WarOf(Mine)); Assert.Equal(-1, host.War);
        int left = 60; foreach (var c in host.Companies) { Assert.Equal(Math.Min(k.Aw.HouseholdsOfFief(c.Fief) / 10, left), c.Quota); left -= c.Quota; }
        Assert.Contains(k.Entries("muster_called").Single().Causes, c => c.Kind == CauseKind.Campaign && c.NoteKey == "ch_note_am_muster_called");
        k.To(k.At(4, 19));
        Assert.Equal(60, host.Companies.Sum(c => c.Men.Count)); var hid = new HostId(host.Supply); var hs = k.Supply();
        long chest0 = k.State.Economy!.Ledger.Balance(hs.ChestOf(hid)).Mites; Assert.True(chest0 > 0);
        // ten days of service: nothing is paid in them
        k.To(k.At(13, 19));
        Assert.NotEqual(HostState.Disbanding, host.State); Assert.Equal(0, hs.Account(hid).Arrears.Sum()); Assert.Equal(0, hs.Last(hid)!.PaidTotal);
        var menAll = host.Companies.SelectMany(c => c.Men).ToList();
        k.To(k.At(14, 19));
        Assert.Equal(HostState.Disbanding, host.State);                                           // the ten days were done and the lord, with no campaign behind the call, would not pay on
        var rel = k.Entries("levy_released").Single(); Assert.Contains(rel.Causes, c => c.NoteKey == "ch_note_am_service_over" && c.EventId?.Value == host.Entry);
        Assert.False(hs.Exists(hid)); Assert.Contains(menAll, m => k.Book.IsAway(m));
        int homeDay = host.Companies.Max(c => c.HomeDay);
        k.To((homeDay + 1) * 24L + 1);
        Assert.Empty(k.Book.Hosts); Assert.All(menAll, m => Assert.False(k.Book.IsAway(m)));
        Assert.True(k.State.Economy.Ledger.Audit().IsConserved);
        output.WriteLine($"called doy 3, drawn doy 4, released doy 14, home by day +{homeDay - host.SummonsDay}");
    }

    // ---------------------------------------------------------------- AM-4

    [Fact]
    public void AM4_Evasion_AggrievedHouseholdsAtRungTwoHideTheirMen_TheNextHouseholdGives_TheDrawAddsNoGrievance()
    {
        var (k, war, point) = Rig(); using var _k = k;
        var aw = k.Aw; var region = k.State.Population.Region; var ht = k.State.Population.Households; var pt = k.State.Population.People; var econ = k.State.Economy!;
        // a village of ten households or more that is the first its fief's lord asks (nearest his seat, not a farmstead)
        var cands = new List<(int V, int Fief, List<int> Hh)>();
        foreach (var f in aw.FiefsOfRealm(Mine))
        {
            int seat = region.Fiefs[f.Fief].SeatSettlement; long D(int s) { long dx = region.Settlements[s].CenterX - region.Settlements[seat].CenterX, dy = region.Settlements[s].CenterY - region.Settlements[seat].CenterY; return dx * dx + dy * dy; }
            var by = Enumerable.Range(0, ht.Count).Where(i => ht.MemberCount[i] > 0 && ht.Settlement[i] >= 0 && region.Settlements[ht.Settlement[i]].FiefIndex == f.Fief).GroupBy(i => ht.Settlement[i]).ToDictionary(g => g.Key, g => g.ToList());
            var first = by.Keys.OrderBy(s => s == seat ? 2 : by[s].Count < SupplyConfig.LevyVillageMinHouseholds ? 1 : 0).ThenBy(D).ThenBy(s => s).First();
            if (by[first].Count >= 10 && first != seat && aw.QuotaOf(f.Fief, true) > 0 && cands.Count < 8) cands.Add((first, f.Fief, by[first]));
        }
        Assert.NotEmpty(cands);
        var host = aw.CallMuster(k.Env(), new MusterCall(Mine, point, MaxLevies: 100_000))!;
        k.To(k.At(17, 16));                                                                         // after the day's pass, before the draw at 18:00
        var ub = k.State.Unrest!; var ctx = UnrestWiring.Context(k.R.Sim, ub, k.Rule, null, k.Day, k.Now);
        int v = -1, fief = -1; List<int> hhs = [], flagged = [], hidden = [];
        foreach (var cd in cands)                                                                   // the first village where at least one of the four hides his man at this season's draw
        {
            flagged = cd.Hh.Take(cd.Hh.Count <= 13 ? 4 : (cd.Hh.Count * 35 + 99) / 100).ToList();      // four households of a village of ten to thirteen, else a third of it
            hhs = cd.Hh; v = cd.V; fief = cd.Fief;
            foreach (int h in hhs) econ.Grievances[h] = flagged.Contains(h) ? Grievance.Hunger | Grievance.Crowding : Grievance.None;
            ub.Of(v).Rung = 2; hidden = flagged.Where(h => UnrestEngine.EvadesLevy(ctx, v, h)).ToList();
            if (hidden.Count >= 1) break;
            foreach (int h in hhs) econ.Grievances[h] = Grievance.None; ub.Of(v).Rung = 0;
        }
        var rowsBefore = ub.Rows.ToDictionary(kv => kv.Key, kv => kv.Value.Select(x => (x.Kind, x.Points, x.UntilDay)).ToList());
        var flagsBefore = hhs.Select(h => econ.Grievances[h]).ToList();
        output.WriteLine($"village {v} of fief {fief}: {hhs.Count} households, {flagged.Count} aggrieved, {hidden.Count} hide their men; the tax's draw hides {flagged.Count(h => UnrestEngine.Evades(ctx, v, h))}");
        Assert.True(hidden.Count >= 1, "at this season's draw at least one of the four hides his man");
        k.To(k.At(17, 19));
        var co = host.Companies.Single(c => c.Fief == fief); Assert.True(co.IsDrawn);
        Assert.InRange(co.Evaded, 1, hidden.Count);                   // the households asked that hid their man (one with no able man is never asked)
        Assert.True(hidden.Count(h => ht.Members(h).Any(m => Able(k, m, k.Day) && !aw.IsStaff(m))) >= 1);
        foreach (int h in hidden) Assert.DoesNotContain(co.Men, m => pt.Household[aw.RowOf(m)] == h);
        Assert.Contains(k.Entries("levy_drawn"), e => e.Params!.Any(p => p.Name == "evaded" && p.Value == co.Evaded) && e.Params!.Any(p => p.Name == "men" && p.Value == co.Men.Count));
        // the next household gave in the hider's place, and the draw wrote no grievance: the rows are the rows, the flags are the flags
        Assert.Equal(co.Quota, co.Men.Count);
        foreach (var (key, list) in ub.Rows) if (rowsBefore.TryGetValue(key, out var before)) Assert.Equal(before, list.Select(x => (x.Kind, x.Points, x.UntilDay)).ToList());
        Assert.Equal(rowsBefore.Count, ub.Rows.Count);
        Assert.Equal(flagsBefore, hhs.Select(h => econ.Grievances[h]).ToList());
        // a fief whose every household hides its man owes fewer: the draw takes what the households give, no more
        var drawnFromV = co.Men.Count(m => ht.Settlement[pt.Household[aw.RowOf(m)]] == v);
        Assert.True(drawnFromV <= Math.Max(1, hhs.Count * 50 / 100));
    }

    // ---------------------------------------------------------------- AM-5

    [Fact]
    public void AM5_FiftyLeviesHeldThroughTheHarvest_TheirHouseholdsLoseTheirHands_TheHeldEntryNamesTheShortfall_TheyArePaidAfterTheirTenDays()
    {
        var (k, war, point) = Rig(year: 1); using var _k = k;
        var aw = k.Aw; aw.NoDesertion = true; var hs = k.Supply(); var econ = k.State.Economy!; var pt = k.State.Population.People;
        var host = aw.CallMuster(k.Env(), new MusterCall(Mine, point, Worth: long.MaxValue / 4, MaxLevies: 50, FundDays: 40, FollowsWar: false))!;
        k.To(k.At(17, 19, 1));
        var hid = new HostId(host.Supply); Assert.Equal(50, host.Companies.Sum(c => c.Men.Count));
        var rows = host.Companies.SelectMany(c => c.Men).Select(aw.RowOf).ToList(); var farm = k.R.Sim.Get<EconomyWorld>()!.Farming;
        Assert.All(rows, r => Assert.True(farm.IsEmployedElsewhere!(r), "the planner counts him away"));
        var households = rows.Select(r => pt.Household[r]).Distinct().ToList();
        // the harvest decision of day 25: held, the entry names the shortfall (kg and money) the way HarvestRisk and the staple's price give it
        k.To(k.At(25, 19, 1));
        var held = k.Entries("host_held_for_harvest").Single(); Assert.Contains(held.Causes, c => c.EventId?.Value == host.Entry && c.NoteKey == "ch_note_am_hold_worth");
        long kg = 0, mites = 0; foreach (var c in host.Companies) { var (a, b) = hs.HarvestRiskPriced(c.Fief, c.Men.Count, aw.Rules.ReapWindowDays); kg += a; mites += b; }
        Assert.Equal(50, held.Params!.Single(p => p.Name == "men").Value);
        Assert.True(mites > 0 && kg > 0, "the second year has a harvest behind it to measure the loss by"); Assert.InRange(held.Params!.Single(p => p.Name == "shortfall").Value, mites - mites / 5 - 1, mites + mites / 5 + 1);       // the same function a day later (the harvest's own figure moves a little with the day's prices)
        output.WriteLine($"day 25: kept 50 levies, the {held.Params!.Single(p => p.Name == "kg").Value} kg of the reap window ({kg} now) priced at {held.Params!.Single(p => p.Name == "shortfall").Value} mites");
        // through the window they stay away, and from their eleventh day of service they are paid: a ledger transfer to their own households' purses
        long chest0 = econ.Ledger.Balance(hs.ChestOf(hid)).Mites;
        k.To(k.At(27, 19, 1));
        Assert.True(host.Paying); Assert.All(rows, r => Assert.True(hs.IsOnCampaign(r)));
        k.To(k.At(29, 19, 1));
        var day29 = hs.Last(hid)!; Assert.Equal(50L * 150, day29.Paid[(int)TroopRole.Levy]); Assert.Equal(0, day29.Arrears[(int)TroopRole.Levy]);
        long chestNow = econ.Ledger.Balance(hs.ChestOf(hid)).Mites; Assert.True(chestNow < chest0, "the chest paid out food and wages");
        Assert.True(econ.Ledger.Audit().IsConserved);
        // and the grain: the control without the muster reaps at least as much, and the villages nobody was drawn from reap as before
        var drawnVillages = rows.Select(r => pt.Household[r]).Select(h => k.State.Population.Households.Settlement[h]).ToHashSet();
        k.To(k.At(36, 1, 1)); var withHost = Reaped(k);
        var (ctl, _, _) = Rig(year: 1); using var _c = ctl; ctl.To(ctl.At(36, 1, 1)); var without = Reaped(ctl);       // the control is the same world to the hour: the same year, the same war declared and treasuries funded, only the call to muster left out (a declaration and a mint alone move the harvest by percent, so a bare world is no control)
        long dW = 0, dC = 0, uW = 0, uC = 0;
        foreach (var s in withHost.Keys) { if (drawnVillages.Contains(s)) { dW += withHost[s]; dC += without[s]; } else { uW += withHost[s]; uC += without[s]; } }
        output.WriteLine($"{drawnVillages.Count} drawn villages reaped {dW} acres against the control's {dC}; the others {uW} against {uC}");
        Assert.True(dW <= dC, "men kept from the fields reap no more than the control");
        Assert.InRange(uW * 1000, uC * 995, uC * 1005 + 1);
    }

    private static Dictionary<int, long> Reaped(ArmyRig k)
    {
        var P = k.R.Sim.Get<EconomyWorld>()!.Farming.State.Parcels; var region = k.State.Population.Region; var d = new Dictionary<int, long>();
        for (int s = 0; s < region.Settlements.Count; s++) { long a = 0; foreach (int p in region.Settlements[s].Parcels) a += P.ReapDone[p]; d[s] = a; }
        return d;
    }

    [Fact]
    public void AM5_WithNoCampaignBehindTheCall_TheLevyGoesHomeForTheHarvestOnDay25_TheirKitBackWithTheirHouseholds()
    {
        var (k, war, point) = Rig(); using var _k = k;
        var aw = k.Aw; var hs = k.Supply(); var econ = k.State.Economy!;
        GiveKit(k, _ => true);
        var host = aw.CallMuster(k.Env(), new MusterCall(Mine, point, MaxLevies: 80, FollowsWar: false))!;
        k.To(k.At(17, 19)); var men = host.Companies.SelectMany(c => c.Men).ToList(); Assert.Equal(80, men.Count);
        Assert.All(men, m => Assert.NotNull(k.State.Soldiers!.Of(m)));
        var worn = men.SelectMany(m => k.State.Soldiers!.Of(m)!.Worn.Values.Select(i => (Man: m, Item: i))).ToList(); Assert.NotEmpty(worn);
        long coin0 = econ.Ledger.Balance(hs.RealmTreasury(Mine)).Mites; var hid = new HostId(host.Supply);
        k.To(k.At(25, 19));
        Assert.Equal(HostState.Disbanding, host.State);
        var rel = k.Entries("levy_released").Single(); Assert.Contains(rel.Causes, c => c.NoteKey == "ch_note_am_harvest" && c.EventId?.Value == host.Entry);
        Assert.Empty(k.Entries("host_held_for_harvest"));
        Assert.All(men, m => { Assert.Null(k.State.Soldiers!.Of(m)); Assert.False(hs.IsOnCampaign(aw.RowOf(m))); });
        // (a levy who deserted before the release took to the roads with a band, and a band man keeps his kit: FormBand's rule. Desertion is not switched off in this test, and which few men go is the world's draw, so they are told apart by the band book, not assumed away)
        var deserted = worn.Where(w => k.Book.BandWith(w.Man) != null).ToList(); var released = worn.Except(deserted).ToList();
        Assert.True(released.Select(w => w.Man).Distinct().Count() * 10 >= men.Count * 9, "nine in ten of the levy were released with the host, not gone to the roads");
        Assert.All(released, w => Assert.Equal(EntityIdKind(k, aw.HouseholdOf(w.Man)), econ.Items.Get(new EntityId(w.Item)).Owner));          // the kit is back in his household's things
        Assert.All(deserted, w => Assert.Equal(new EntityId(w.Man), econ.Items.Get(new EntityId(w.Item)).Owner));                      // and a deserter's is still his own
        Assert.False(hs.Exists(hid));
        // the unspent coin went back to the treasury; none was made or lost
        Assert.True(econ.Ledger.Balance(hs.RealmTreasury(Mine)).Mites >= coin0 - 60_000_000);
        Assert.True(econ.Ledger.Audit().IsConserved); Assert.True(econ.MassAuditHolds());
        // they are home when they have walked: their labour was theirs again then
        Assert.Contains(men, m => k.Book.IsAway(m));
        int home = host.Companies.Max(c => c.HomeDay); output.WriteLine($"released day 25 of the year, the last of them home on day {home - k.DayOf(25)} later");
        k.To((home + 1) * 24L + 1);
        Assert.All(men, m => Assert.False(k.Book.IsAway(m)));
        Assert.False(k.Book.Hosts.ContainsKey(host.Id));                                              // (the realms at war may call new hosts of their own on the first day of autumn: P8-WT-1)
    }

    private static EntityId EntityIdKind(ArmyRig k, int household) => k.State.Population.Households.IdOf(household);

    // ---------------------------------------------------------------- AM-6

    [Fact]
    public void AM6_MusterTiming_SummonsOnDay16_LeviesAtTheirSeatsOn17_TheHostFormsAtEightyPercentOrThreeDaysAfterTheFirst()
    {
        var (k, war, point) = Rig(); using var _k = k;
        var aw = k.Aw;
        var host = aw.CallMuster(k.Env(), new MusterCall(Mine, point))!;
        int d16 = k.DayOf(16); Assert.Equal(d16, host.SummonsDay);
        Assert.All(host.Companies, c => Assert.Equal(d16, c.Heard));                                // a region's fiefs hear within the day
        k.To(k.At(17, 17)); Assert.Equal(HostState.Summoned, host.State); Assert.All(host.Companies, c => Assert.False(c.IsDrawn));
        k.To(k.At(17, 19));
        Assert.All(host.Companies, c => { Assert.Equal(d16 + 1, c.Drawn); Assert.Equal(c.Drawn + aw.WalkDays(c.Seat, point), c.Arrives); });
        int men = host.Companies.Sum(c => c.Men.Count); Assert.True(men > 100, $"{men} men");
        Assert.Equal(host.Expected - host.Warbands.Sum(j => j.Men), men);
        // the host forms on the day eight in ten of the expected men are at the point, or three days after the first column came (the rule itself is tested on made columns below)
        var day0 = host.Companies.Min(c => c.Arrives);
        k.To((day0 + 4) * 24L + 1);
        Assert.Equal(HostState.Camped, host.State); Assert.InRange(host.FormedDay, day0, day0 + 3); Assert.Equal(day0, host.FirstArrival);
        output.WriteLine($"{men} men from {host.Companies.Count} fiefs; first column on day +{day0 - d16}, formed on day +{host.FormedDay - d16}; walks {string.Join(",", host.Companies.Select(c => c.Arrives - c.Drawn).Distinct().OrderBy(x => x))} days");
        var formed = k.Entries("host_formed").Single(); Assert.Contains(formed.Causes, c => c.EventId?.Value == host.Entry);
        Assert.True(formed.Params!.Single(p => p.Name == "men").Value >= host.Companies.Where(c => host.FormedDay >= c.Arrives).Sum(c => c.Men.Count));
        // the vassals were called: each served (his warband rode to the point) or paid scutage; the ruler's own warband came
        Assert.NotEmpty(k.Pol.Wars); Assert.True(k.Rule.Dues.Any(d => d.Kind is DueKind.Service or DueKind.Scutage), "the call went out to the vassals");
        foreach (var j in host.Warbands) { var wb = k.Book.Warbands[j.Warband]; Assert.Equal(WarbandTask.JoinHost, wb.Task); Assert.Contains(k.Book.Log, l => l.Warband == wb.Id && l.Task == WarbandTask.JoinHost && l.Cause == "ch_note_am_join_host"); }
        output.WriteLine($"the daily step of the armies: mean {aw.DayCost.MeanMs:F2} ms, worst {aw.DayCost.MaxMs:F1} ms over {aw.DayCost.Calls} days (the draw of {host.Companies.Count} fiefs, one daily step)"); Assert.True(aw.DayCost.MaxMs < 2000, $"the draw day took {aw.DayCost.MaxMs:F0} ms");
        output.WriteLine($"{host.Warbands.Count} warbands joined ({host.Warbands.Sum(j => j.Men)} men), {k.Rule.Dues.Count(d => d.Kind == DueKind.Service)} service dues, {k.Rule.Dues.Count(d => d.Kind == DueKind.Scutage)} scutage dues");
    }

    [Fact]
    public void AM6_TheHostFormsWhenEightyPercentAreThere_OrThreeDaysAfterTheFirstColumn_NotBefore()
    {
        var (k, war, point) = Rig(); using var _k = k;
        var aw = k.Aw; var hs = k.Supply(); var pt = k.State.Population.People;
        var ids = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i)).Take(100).Select(i => pt.IdOf(i).Value).ToList();
        ArmyHost Make(int[] sizes, int[] arrives)
        {
            var h = new ArmyHost { Id = 500 + k.Book.Hosts.Count, Realm = Mine, Point = point, State = HostState.Mustering, SummonsDay = 0 };
            int at = 0;
            for (int i = 0; i < sizes.Length; i++) { var c = new LevyCompany { Fief = i, Seat = point, Quota = sizes[i], Drawn = 1, Arrives = arrives[i] }; c.Men.AddRange(ids.Skip(at).Take(sizes[i])); at += sizes[i]; h.Companies.Add(c); h.Expected += sizes[i]; }
            return h;
        }
        // four companies of 25: three arrive by day 5 (75 of 100, not enough), the fourth on day 6: eighty is met only with the fourth... but the third day after the first (day 7) is later: it forms on day 6
        var a = Make([25, 25, 25, 25], [3, 4, 5, 20]);
        for (int d = 1; d <= 20; d++) { aw.Gather(k.Env(), hs, a, d); if (a.State == HostState.Camped) { Assert.Equal(6, d); break; } }
        Assert.Equal(HostState.Camped, a.State); Assert.Equal(6, a.FormedDay);                      // the third day after the first column (day 3) is day 6: three days, though 75 per cent
        var b = Make([40, 40, 20], [2, 2, 9]);                                                     // eighty in the field on day 2: formed that day
        for (int d = 1; d <= 10; d++) { aw.Gather(k.Env(), hs, b, d); if (b.State == HostState.Camped) { Assert.Equal(2, d); break; } }
        Assert.Equal(2, b.FormedDay);
        var c3 = Make([30, 30, 40], [4, 6, 30]);                                                   // 60 per cent by day 6, the third column far off: the third day after the first, day 7
        for (int d = 1; d <= 10; d++) { aw.Gather(k.Env(), hs, c3, d); if (c3.State == HostState.Camped) { Assert.Equal(7, d); break; } }
        Assert.Equal(7, c3.FormedDay);
    }

    // ---------------------------------------------------------------- AM-18

    [Fact]
    public void AM18_Winter_OnDay46TheLeviesAreReleasedAndWalkHome_AHostBesiegingStays_TheWarbandsGarrison()
    {
        var (k, war, point) = Rig(); using var _k = k;
        var aw = k.Aw; aw.NoDesertion = true; var hs = k.Supply();
        int pointE = aw.MusterPointFor(Enemy, Mine);
        var hostA = aw.CallMuster(k.Env(), new MusterCall(Mine, point, Worth: long.MaxValue / 4, MaxLevies: 60, FundDays: 40, FollowsWar: false))!;
        var hostB = aw.CallMuster(k.Env(), new MusterCall(Enemy, pointE, Worth: long.MaxValue / 4, MaxLevies: 20, FundDays: 40, FollowsWar: false))!;
        Assert.NotNull(hostB);
        k.To(k.At(17, 19));
        Assert.Equal(60, hostA.Companies.Sum(c => c.Men.Count)); Assert.True(hostB.Companies.Sum(c => c.Men.Count) > 0);
        hostB.State = HostState.Besieging;                                                         // the siege is on
        var menA = hostA.Companies.SelectMany(c => c.Men).ToList(); var menB = hostB.Companies.SelectMany(c => c.Men).ToList();
        k.To(k.At(45, 19));
        Assert.Equal(HostState.Camped, hostA.State); Assert.True(hostA.Paying);
        menA = hostA.Companies.SelectMany(c => c.Men).ToList(); Assert.InRange(menA.Count, 50, 60);       // a few may have died in the month
        Assert.All(menA, m => Assert.True(hs.IsOnCampaign(aw.RowOf(m))));
        menB = hostB.Companies.SelectMany(c => c.Men).ToList();
        k.To(k.At(46, 19));
        Assert.Equal(HostState.Disbanding, hostA.State); Assert.Equal(HostState.Besieging, hostB.State);
        var rel = k.Entries("levy_released").Single(e => e.Causes.Any(c => c.EventId?.Value == hostA.Entry)); Assert.Contains(rel.Causes, c => c.NoteKey == "ch_note_am_winter_release");
        Assert.All(menA, m => Assert.False(hs.IsOnCampaign(aw.RowOf(m))));
        Assert.All(menB, m => Assert.True(hs.IsOnCampaign(aw.RowOf(m)), "the besiegers keep their levies"));
        Assert.Contains(menA, m => k.Book.IsAway(m));
        k.To(k.At(46, 19) + 24 * 10);
        Assert.All(menA, m => Assert.False(k.Book.IsAway(m)));
        // the warbands winter at their lords' seats
        Assert.All(k.Book.Warbands.Values.Where(w => w.Here >= 0), w => Assert.True(w.Task is WarbandTask.JoinHost or WarbandTask.Garrison or WarbandTask.Idle or WarbandTask.Escort or WarbandTask.Patrol or WarbandTask.Visit or WarbandTask.ChaseBandits or WarbandTask.Raid));
        Assert.DoesNotContain(k.Book.Warbands.Values, w => w.Task == WarbandTask.JoinHost && k.Book.HostOfWarband(w.Id) == null);
        Assert.Contains(k.Book.Log, l => l.Cause == "ch_note_am_winter" && l.Task == WarbandTask.Garrison);
        Assert.True(k.State.Economy!.Ledger.Audit().IsConserved);
    }

    [Fact]
    public void PeaceSendsTheHostHome_TheWarsEndIsTheReleasesCause()
    {
        var (k, war, point) = Rig(); using var _k = k;
        var host = k.Aw.CallMuster(k.Env(), new MusterCall(Mine, point, MaxLevies: 40, Worth: long.MaxValue / 4, FundDays: 40))!; Assert.Equal(war.Serial, host.War);
        k.To(k.At(19, 19)); Assert.NotEqual(HostState.Disbanding, host.State);
        war.EndDay = k.Day;                                                                         // the loser sued
        k.To(k.At(20, 19)); Assert.Equal(HostState.Disbanding, host.State);
        Assert.Contains(k.Entries("levy_released").Single().Causes, c => c.NoteKey == "ch_note_am_war_over" && c.EventId?.Value == host.Entry);
        Assert.True(k.State.Economy!.Ledger.Audit().IsConserved);
    }

    // ---------------------------------------------------------------- the AI's call

    [Fact]
    public void TheAIsCall_ARealmAtWarWithTheCoinToCarryTheLevyCallsItOnTheFirstDayOfSummer_AnEmptyTreasuryDoesNot()
    {
        using var k = new ArmyRig();
        k.To(k.At(16, 5));
        k.War(Mine, Enemy); k.Fund(Mine, 4_000_000_000);                                            // the realm of most fiefs is rich, the other realm's treasury is what it is
        k.To(k.At(16, 8));
        var host = k.Book.HostOfRealm(Mine); Assert.NotNull(host);
        Assert.Equal(k.DayOf(16), host!.SummonsDay); Assert.Equal(k.Aw.MusterPointFor(Mine, Enemy), host.Point);
        var called = k.Entries("muster_called").Single(e => e.Id.Value == host.Entry); Assert.Contains(called.Causes, c => c.Kind == CauseKind.War);
        output.WriteLine($"realm 11 called {host.Expected} men to {host.Point}; hosts: {string.Join(", ", k.Book.Hosts.Values.Select(h => $"realm {h.Realm}"))}");
        // at war but with an empty treasury, or in peace with a full one: nobody calls
        using var q = new ArmyRig(); q.To(q.At(16, 5)); q.War(Mine, Enemy);
        var qled = q.State.Economy!.Ledger; var qp = q.Supply().RealmTreasury(Mine); qled.Sink(qp, new Coin(qled.Balance(qp).Mites), SinkReason.Burn);          // the treasury is empty
        q.To(q.At(16, 8)); Assert.Null(q.Book.HostOfRealm(Mine));
        using var p = new ArmyRig(); p.To(p.At(15, 5)); p.Fund(Mine, 4_000_000_000); p.To(p.At(16, 8)); Assert.Empty(p.Book.Hosts);
    }

    // ---------------------------------------------------------------- saves

    [Fact]
    public void SaveLoadContinue_WithAHostMustering_EqualsTheUninterruptedRun_AndAnOlderSectionReadsWithNoHosts()
    {
        var (k, war, point) = Rig(); using var _k = k;
        var host = k.Aw.CallMuster(k.Env(), new MusterCall(Mine, point, Worth: long.MaxValue / 4, MaxLevies: 120, FundDays: 40, FollowsWar: false))!;
        k.To(k.At(18, 6));                                                                          // drawn, walking, not formed
        Assert.Equal(HostState.Mustering, host.State);
        var bytes = SaveCodec.Encode(WorldSave.Capture(k.State, "h", "0.1")); long h2 = k.At(30, 20);
        k.To(h2);
        var want = k.Book.Hash(); var wantSection = SaveCodec.SerializeCompressed(ArmySave.Capture(k.Book)); var wantSupply = k.State.WarSupply!.Hash(); var wantChron = k.State.Chronicle.Hash();
        Assert.Equal(HostState.Camped, host.State); Assert.NotEmpty(k.Entries("host_formed"));
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), k.R.Content, "h", null, k.R.W.Continent, k.R.W.History); st.Content = k.R.Content;
        Assert.Single(st.Armies!.Hosts); Assert.Equal(HostState.Mustering, st.Armies.Hosts[host.Id].State);
        using (var rt = new WorldRuntime(st, k.R.Content, new MemoryRegionPager()))
        {
            rt.AdvanceTo(GameTime.FromGameHours(h2));
            Assert.Equal(want, st.Armies!.Hash()); Assert.Equal(wantSection, SaveCodec.SerializeCompressed(ArmySave.Capture(st.Armies)));
            Assert.Equal(wantSupply, st.WarSupply!.Hash()); Assert.Equal(wantChron, st.Chronicle.Hash());
            Assert.True(st.Economy!.Ledger.Audit().IsConserved);
        }
        output.WriteLine($"saved at day 18 with {host.Companies.Sum(c => c.Men.Count)} men walking; after 12 more days the armies' hash {want:x} and the supply book's {wantSupply} in both runs");
        // a section written before the muster (ten keys) reads with no hosts, and writes back what it read
        var obj = MessagePackSerializer.Deserialize<object[]>(MessagePackSerializer.Serialize(ArmySave.Capture(k.Book)));
        var older = MessagePackSerializer.Serialize(obj.Take(10).ToArray());
        var read = MessagePackSerializer.Deserialize<ArmySave>(older); var back = read.Restore();
        Assert.Empty(back.Hosts); Assert.Equal(1, back.NextHost); Assert.Equal(k.Book.Warbands.Count, back.Warbands.Count);
        Assert.Equal(k.Book.Warbands.Values.Select(w => (w.Id, w.Members.Count)), back.Warbands.Values.Select(w => (w.Id, w.Members.Count)));
    }

    [Fact]
    public void TwoRunsOfTheSameCall_AreTheSameHostTheSameHash()
    {
        static ulong Run()
        {
            var (k, _, point) = Rig(); using (k)
            {
                k.Aw.CallMuster(k.Env(), new MusterCall(Mine, point, Worth: 1_000_000, MaxLevies: 100)); k.To(k.At(22, 3));
                return k.Book.Hash();
            }
        }
        ulong a = Run(), b = Run(); Assert.NotEqual(0UL, a); Assert.Equal(a, b);
    }
}
