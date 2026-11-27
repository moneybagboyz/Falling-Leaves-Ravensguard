using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Bondage;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.Tests.Armies;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.LiveWar;

/// <summary>P8-LW-4 gate: LW-13 (a lost battle puts the yielded in pens with rank classes; people exact) and LW-14 (feeding, sale, cede and parole broken move coin, food and people exactly and write an entry).</summary>
public class LiveWarPrisonerTests(ITestOutputHelper output)
{
    private static readonly AftermathRules R = AftermathRules.Default;

    private static List<ulong> Spare(ArmyRig k, int n, ulong not)
    {
        var aw = k.Aw; var pt = aw.People; var l = new List<ulong>(); int day = k.Day;
        for (int i = 0; i < pt.Count && l.Count < n; i++)
        {
            var id = pt.IdOf(i).Value;
            if (!pt.IsAlive(i) || pt.Household[i] < 0 || id == not || aw.IsHero(id) || aw.Book.IsAway(id) || aw.Soldiers!.Book.Rows.ContainsKey(id) || pt.AgeYears(i, day) < 18) continue;
            l.Add(id);
        }
        return l;
    }

    [Fact]
    public void LW13_ALostBattle_PutsTheYieldedInPens_WithRankClasses_PeopleExact()
    {
        using var k = new ArmyRig(); k.Raised(); var aw = k.Aw; var sw = aw.Soldiers!;
        ulong captor = k.WarbandOfFief(0).Lord; var men = Spare(k, 60, captor); Assert.Equal(60, men.Count);
        int alive = Enumerable.Range(0, aw.People.Count).Count(aw.People.IsAlive);
        var rep = aw.TakePrisoners(k.Env(), 77, captor, pen: 0, guards: 3, men)!;            // 3 guards hold 30
        output.WriteLine($"held {rep.Held} ransom {rep.Ransom} common {rep.Common} let go {rep.LetGo}");
        Assert.Equal(30, rep.Held); Assert.Equal(30, rep.LetGo); Assert.Equal(0, rep.Killed); Assert.Equal(rep.Ransom + rep.Common, rep.Held);
        Assert.Equal(rep.Held, sw.Book.Prisoners.Count);
        foreach (var p in sw.Book.Prisoners.Values)
        {
            Assert.Equal(PrisonerPens.Sort(new Taken(p.Person, p.ValueMites >= R.D.RankManAtArms ? CaptiveRank.ManAtArms : CaptiveRank.Levy, p.PayerPurse >= 0 && p.Class == PrisonerClass.Ransom)), p.Class == PrisonerClass.Ransom ? PrisonerClass.Ransom : PrisonerClass.Common);
            Assert.True(aw.People.IsAlive(aw.RowOf(p.Person)), "a prisoner is a person who is still alive"); Assert.Equal((long)captor, p.Captor);
        }
        Assert.Equal(alive, Enumerable.Range(0, aw.People.Count).Count(aw.People.IsAlive));       // nobody died, nobody was made
        var taken = k.Entries("prisoners_taken").Single(); Assert.NotEmpty(taken.Causes);
        // the same men are not taken twice
        Assert.Null(aw.TakePrisoners(k.Env(), 78, captor, 0, 3, sw.Book.Prisoners.Keys));
        Assert.Equal(rep.Held, sw.Book.Prisoners.Count);
    }

    [Fact]
    public void LW14_PenHooks_FeedingAKilogramADay_FromTheCaptorsStore_AMassExactMove()
    {
        using var k = new ArmyRig(); k.Raised(); var aw = k.Aw; var sw = aw.Soldiers!; var eco = k.State.Economy!;
        ulong captor = k.WarbandOfFief(0).Lord; int cr = aw.RowOf(captor); int hh = aw.People.Household[cr];
        var grain = eco.Food.Staples.First(eco.Food.IsGrain); long unit = Math.Max(1, eco.Goods.MassG(grain, 1));
        var store = eco.HouseholdStore[hh]; store.Destroy(grain, store.Qty(grain), DestroyReason.Lost);
        store.CreateUpTo(grain, 100_000 / unit, CreateReason.Seed);
        var men = Spare(k, 20, captor);
        aw.TemperOf = _ => Commander.Greedy;                                                    // not paroled at once: a greedy man puts the common to work, so they sit in the pen and eat
        var rep = aw.TakePrisoners(k.Env(), 79, captor, 0, 2, men)!; Assert.Equal(20, rep.Held);
        var work = sw.Book.Prisoners.Values.Count(p => p.Work != "" && p.Class == PrisonerClass.Common);
        foreach (var p in sw.Book.Prisoners.Values) { p.Class = PrisonerClass.Common; p.PayerPurse = -1; p.Work = "camp"; }          // all common, so none is ransomed out of the pen today
        long before = store.Qty(grain) * unit, eaten0 = eco.Mass.Destroyed(grain, DestroyReason.Eaten);
        var chron = Ravensguard.Sim.Rule.RuleChron.Of(k.State.Chronicle, k.State.Ids);
        PrisonerPens.Day(sw, R, chron, k.State.Seed, k.Day + 1);
        long used = before - store.Qty(grain) * unit; int fed = sw.Book.Prisoners.Values.Count(p => p.UnfedDays == 0);
        output.WriteLine($"{fed} fed, {used} g of grain used");
        Assert.Equal(20, fed);
        Assert.InRange(used, fed * PenWiring.FeedG, fed * PenWiring.FeedG + unit * fed);                         // a kilogram a man, rounded up to the unit
        Assert.Equal(used, (eco.Mass.Destroyed(grain, DestroyReason.Eaten) - eaten0) * unit);                  // every gram fed is a gram destroyed as eaten: no grain appeared or vanished
        Assert.True(eco.MassAuditHolds());
        // a captor with an empty store feeds nobody: unfed two days, they go free
        foreach (var g in eco.Food.Staples) store.Destroy(g, store.Qty(g), DestroyReason.Lost);
        PrisonerPens.Day(sw, R, chron, k.State.Seed, k.Day + 2); PrisonerPens.Day(sw, R, chron, k.State.Seed, k.Day + 3);
        Assert.DoesNotContain(sw.Book.Prisoners.Values, p => p.ParoleUntil < 0 && p.Class == PrisonerClass.Common);
    }

    [Fact]
    public void LW14_PenHooks_ASaleWhereTheLawAllows_MovesCoinThroughTheLedger_AndBondsTheMan_AndNoMarketNoSale()
    {
        using var k = new ArmyRig(); k.Raised(); var aw = k.Aw; var sw = aw.Soldiers!; var eco = k.State.Economy!; var led = eco.Ledger; var ht = aw.Households; var region = k.State.Population.Region;
        ulong captor = k.WarbandOfFief(0).Lord; var men = Spare(k, 6, captor); var cp = eco.HouseholdPurse[aw.People.Household[aw.RowOf(captor)]];
        var chron = Ravensguard.Sim.Rule.RuleChron.Of(k.State.Chronicle, k.State.Ids);
        int town = Enumerable.Range(0, region.Settlements.Count).First(s => Enumerable.Range(0, ht.Count).Count(h => ht.MemberCount[h] > 0 && ht.Settlement[h] == s && ht.Beds[h] - ht.MemberCount[h] >= 1) >= 3);
        var taken = men.Select(m => new Taken(m, CaptiveRank.Levy)).ToList();
        int day = k.Day + 1; var cc = k.State.Content!;

        // under the realm's own law there is no market: the man is put to work, nothing moves
        PrisonerPens.Take(sw, R, chron, 1, 5, day, (long)captor, -1, town, cp, 1, Commander.Greedy, taken);
        long total = led.Audit().InPurses.Mites, captor0 = led.Balance(cp).Mites;
        Assert.Equal(men.Count > 10 ? 10 : men.Count, PrisonerPens.Settle(sw, chron, 1, day, (long)captor, -1, Commander.Greedy, saleLegal: true));
        Assert.Equal(0, k.State.Bonds?.Bonds.Count ?? 0); Assert.Equal(total, led.Audit().InPurses.Mites); Assert.Equal(captor0, led.Balance(cp).Mites);
        Assert.All(sw.Book.Prisoners.Values, p => Assert.Equal("camp", p.Work));

        // the town's culture is of the north, whose law has a market for war captives: a rich household buys
        foreach (var p in sw.Book.Prisoners.Keys.ToList()) sw.Book.Prisoners.Remove(p);
        region.Settlements[town].Culture = Enumerable.Range(0, 64).First(i => Ravensguard.Sim.Faith.ReligionBook.CultureOf(cc, i)?.Id == "north");
        Assert.True(Slavery.HasMarket(BondWiring.World(k.State, cc).LawAt(town)));
        var rich = Enumerable.Range(0, ht.Count).Where(h => ht.MemberCount[h] > 0 && ht.Settlement[h] == town && ht.Beds[h] - ht.MemberCount[h] >= 1).Take(3).ToList();
        foreach (int h in rich) led.Mint(eco.HouseholdPurse[h], new Coin(50_000_000), SourceReason.Seed);
        total = led.Audit().InPurses.Mites; captor0 = led.Balance(cp).Mites; int bonds0 = k.State.Bonds?.Bonds.Count ?? 0;
        PrisonerPens.Take(sw, R, chron, 1, 6, day, (long)captor, -1, town, cp, 1, Commander.Greedy, taken);
        int n = PrisonerPens.Settle(sw, chron, 1, day, (long)captor, -1, Commander.Greedy, saleLegal: true);
        int sold = men.Count(m => !sw.Book.Prisoners.ContainsKey(m)), bonds = (k.State.Bonds?.Bonds.Count ?? 0) - bonds0;
        output.WriteLine($"{n} given a fate, {sold} sold, {bonds} bonds, captor purse {led.Balance(cp).Mites - captor0} mites richer");
        Assert.True(sold > 0 && bonds == sold, "each man sold is a bonded man of the buyer");
        Assert.True(led.Balance(cp).Mites > captor0, "the price went to the captor's purse");
        Assert.Equal(total, led.Audit().InPurses.Mites);                                                      // coin only moved purse to purse
        foreach (var m in men.Where(m => !sw.Book.Prisoners.ContainsKey(m))) Assert.True(k.State.Bonds!.Bonds.ContainsKey(m) && aw.People.IsAlive(aw.RowOf(m)));
    }

    [Fact]
    public void LW14_PenHooks_ALordWhoCannotPay_CedesHisFief_AndABrokenParoleCostsStanding()
    {
        using var k = new ArmyRig(); k.Raised(); var aw = k.Aw; var sw = aw.Soldiers!; var rb = k.Rule;
        var fiefs = rb.Fiefs.Values.Where(f => f.Holder > 0 && !f.Forfeited).OrderBy(f => f.Fief).ToList();
        var lordFief = fiefs[0]; var captorFief = fiefs.First(f => f.Realm != lordFief.Realm);
        ulong lord = (ulong)lordFief.Holder, captor = (ulong)captorFief.Holder; int realmBefore = lordFief.Realm;
        var cp = k.State.Economy!.HouseholdPurse[aw.People.Household[aw.RowOf(captor)]]; var poor = k.State.Economy.Ledger.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 9001));
        sw.Book.Prisoners[lord] = new PrisonerRow { Person = lord, Captor = (long)captor, Pen = 0, Since = 1, Class = PrisonerClass.Ransom, ValueMites = R.D.RankBaron, DueDay = 1, PayerPurse = poor.Index, CaptorPurse = cp.Index, PayerIsLord = true, Entry = 5 };
        var chron = Ravensguard.Sim.Rule.RuleChron.Of(k.State.Chronicle, k.State.Ids);
        PrisonerPens.Day(sw, R, chron, k.State.Seed, 3); Assert.True(sw.Book.Prisoners.ContainsKey(lord), "not yet: the ransom is not quartered");
        PrisonerPens.Day(sw, R, chron, k.State.Seed, 1 + R.D.QuarterAfterDays + 1);
        Assert.False(sw.Book.Prisoners.ContainsKey(lord)); Assert.Equal(captorFief.Realm, lordFief.Realm); Assert.NotEqual(realmBefore, lordFief.Realm);
        Assert.Contains(k.Entries("ransom_ceded"), e => e.Causes.Any(c => c.EventId?.Value == 5));
        // a broken parole: of 400 men paroled to the same day about one in ten break it, each with an entry caused by the capture and 200 standing lost
        int broken = 0;
        for (int i = 0; i < 400; i++)
        {
            ulong p = EntityId.Make(IdKind.Person, (ulong)(900_000 + i)).Value;
            sw.Book.Prisoners[p] = new PrisonerRow { Person = p, Captor = (long)captor, Pen = 0, Since = 1, Class = PrisonerClass.Common, ParoleUntil = 100, Entry = 5 };
        }
        PrisonerPens.Day(sw, R, chron, k.State.Seed, 99);
        foreach (var e in k.Entries("parole_broken")) { Assert.Contains(e.Causes, c => c.EventId?.Value == 5); broken++; }
        output.WriteLine($"{broken} of 400 broke their parole");
        Assert.InRange(broken, 15, 70);                                                                       // 10% of 400
        Assert.Equal(broken, sw.Book.Standing.Count(kv => kv.Value == -PenWiring.ParoleStandingLoss));
        Assert.DoesNotContain(sw.Book.Prisoners.Keys, p => p >= EntityId.Make(IdKind.Person, 900_000).Value && sw.Book.Prisoners[p].ParoleUntil == 100);
    }
}
