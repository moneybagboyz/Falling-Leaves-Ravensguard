using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WarSupply;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Armies;

/// <summary>P8-WT-1 gate (war's toll): WT-1 (campaign intensity: 30 days of the host's cost funded raises a host in a campaign season, 29 does not), WT-2 (an AI host short of food purveys) and WT-4 (war flight and return: the poor first, people exact).</summary>
public class WarsTollTests(ITestOutputHelper output)
{
    private const int Mine = 11, Enemy = 0;

    /// <summary>The 30-day plan's budget: its daily cost and the funds the realm has beside its treasury (scutage due, subsidies, the loan room), which the test leaves as they are.</summary>
    private static (long Daily, long Others) Plan(ArmyRig k)
    {
        var hs = k.Supply(); long asked = 0; foreach (var f in k.Aw.FiefsOfRealm(Mine)) asked += k.Aw.QuotaOf(f.Fief, true);
        var b = hs.Budget(Mine, new Roster([(TroopRole.Levy, (int)Math.Min(asked, 100_000))]), CampaignConfig.PayrollDays);
        return (b.DailyCostMites, b.FundsMites - b.TreasuryMites);
    }

    private static void SetTreasury(ArmyRig k, long mites)
    {
        var led = k.State.Economy!.Ledger; var p = k.Supply().RealmTreasury(Mine);
        long have = led.Balance(p).Mites; if (have > 0) led.Sink(p, new Coin(have), SinkReason.Burn);
        led.Mint(p, new Coin(mites), SourceReason.Seed);
    }

    [Fact]
    public void WT1_ARealmWith30DaysOfPayrollRaisesAHostInASpringSeason_With29ItDoesNotAndItsWarbandsRaid()
    {
        long pay, others;
        using (var k = new ArmyRig())
        {
            k.To(k.At(1, 2)); k.War(Mine, Enemy); k.To(k.At(1, 5)); (pay, others) = Plan(k); Assert.True(pay > 0 && others < pay * 29, $"a day costs {pay}, the loan room and dues are {others}");
            SetTreasury(k, pay * 30 - others + pay / 2); k.To(k.At(1, 8));
            Assert.NotNull(k.Book.HostOfRealm(Mine)); output.WriteLine($"pay a day {pay} mites; spring: a host with {k.Book.HostOfRealm(Mine)!.Expected} men expected");
        }
        using (var q = new ArmyRig())
        {
            q.To(q.At(1, 2)); q.War(Mine, Enemy); q.To(q.At(1, 5));                                       // the decision itself: 30 days funded may start, 29 may not
            long asked = 0; foreach (var f in q.Aw.FiefsOfRealm(Mine)) asked += q.Aw.QuotaOf(f.Fief, true);
            var (d29, o29) = Plan(q);
            SetTreasury(q, d29 * 30 - o29 + 1); Assert.True(q.Aw.MayRaise(q.Supply(), Mine, asked));
            SetTreasury(q, d29 * 30 - o29 - d29 / 4); Assert.False(q.Aw.MayRaise(q.Supply(), Mine, asked));
            SetTreasury(q, d29 * 29 - o29 + d29 / 2); Assert.False(q.Aw.MayRaise(q.Supply(), Mine, asked));
        }
        // summer too (autumn's campaigns are weighed against the harvest they would cost): the first day of every season but winter
        using var a = new ArmyRig();
        a.To(a.At(16, 2)); a.War(Mine, Enemy); a.To(a.At(16, 5)); var (d, o) = Plan(a); SetTreasury(a, d * 31); a.To(a.At(16, 8));
        Assert.NotNull(a.Book.HostOfRealm(Mine));
    }

    [Fact]
    public void WT2_AnAIHostWithUnder10DaysOfFoodInItsBaggage_HoldsThePurveyanceWarrant()
    {
        using var k = new ArmyRig();
        k.To(k.At(16, 2)); k.War(Mine, Enemy); k.To(k.At(16, 5)); SetTreasury(k, 4_000_000_000);
        k.To(k.At(21, 20));
        var host = k.Book.HostOfRealm(Mine); Assert.NotNull(host); Assert.NotEqual(0, host!.Supply);
        var hs = k.Supply(); var acc = hs.Account(new HostId(host.Supply));
        var last = hs.Last(new HostId(host.Supply));
        output.WriteLine($"host of {acc.Total} men, {(last?.DaysOfFoodMilli ?? -1) / 1000.0:F1} days of food, purveyance warrant {acc.Purveyance}");
        Assert.True(acc.Purveyance, "an AI lord's host short of food may purvey on his own realm's land");
        Assert.True((last?.DaysOfFoodMilli ?? 0) < 10_000);
    }

    [Fact]
    public void WT4_ASackedSettlementSendsItsPoorFirst_PeopleStayExact_AndAfterPeaceAboutHalfComeHomeEachSeason()
    {
        using var k = new ArmyRig();
        k.To(k.At(14, 20));
        var st = k.State; var v = st.Abstract!.Vital; var ht = st.Population.Households; var pt = st.Population.People; var region = st.Population.Region; var led = st.Economy!.Ledger;
        var touched = new List<int>();
        for (int s = 0; s < region.Settlements.Count; s += 2) if (region.Settlements[s].Class != Ravensguard.Sim.WorldGen.Region.SettlementClass.Town) touched.Add(s);
        var before = new Dictionary<int, long>(); var hhBefore = new Dictionary<int, int>();
        foreach (int s in touched) for (int h = 0; h < ht.Count; h++) if (ht.Settlement[h] == s && ht.MemberCount[h] > 0 && ht.Class[h] != SocialClass.Gentry) { before[h] = led.Balance(st.Economy.HouseholdPurse[h]).Mites; hhBefore[h] = s; }
        long alive0 = v.Alive, emig0 = v.Emigrants, imm0 = v.Immigrants, b0 = v.Births, d0 = v.Deaths; int fled = 0, home = 0;
        bool Exact() => v.Alive == alive0 + (v.Births - b0) - (v.Deaths - d0) + (v.Immigrants - imm0) - (v.Emigrants - emig0);
        k.Aw.OnFlight = (_, _, n) => fled += n; k.Aw.OnReturn = (_, _, n) => home += n;
        foreach (int s in touched) k.Aw.NoteTouch(s, 4, k.Day, 0);
        k.To(k.At(16, 4));                                                                                // the first day of summer, hour 3: the roll
        Assert.True(Exact());                                                                            // nobody left the region: both ends counted
        Assert.True(fled > 0 && v.Emigrants - emig0 >= fled && v.Immigrants - imm0 >= fled);
        var left = hhBefore.Keys.Where(h => ht.Settlement[h] != hhBefore[h]).ToList(); var stayed = hhBefore.Keys.Except(left).ToList();
        double share = left.Count * 100.0 / hhBefore.Count;
        output.WriteLine($"{hhBefore.Count} households of {touched.Count} sacked settlements: {left.Count} fled ({share:F0}%), {fled} people");
        Assert.InRange(share, 45, 80);
        Assert.True(left.Average(h => (double)before[h]) <= stayed.Average(h => (double)before[h]), "the poor go first");
        Assert.True(led.Audit().IsConserved);
        // no war: after a season about half of them have come home, each return a counted arrival
        k.To(k.At(31, 4));
        int back = left.Count(h => ht.Settlement[h] == hhBefore[h]);
        output.WriteLine($"a season on: {back} of {left.Count} households home ({home} people)");
        Assert.InRange(back * 100.0 / left.Count, 30, 70);
        Assert.True(Exact()); Assert.True(led.Audit().IsConserved);
        Assert.Contains(k.Entries("war_flight"), e => e.Causes.Count == 0 || e.Causes.All(c => c.Kind == CauseKind.WarFlight));
        Assert.All(k.Entries("war_return"), e => Assert.All(e.Causes, c => Assert.Equal(CauseKind.WarReturn, c.Kind)));
    }
}
