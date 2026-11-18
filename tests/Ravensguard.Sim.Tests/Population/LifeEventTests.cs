using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Tests.Abstract;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.PopulationRates;

/// <summary>Births, pregnancy timing, starvation, inheritance and escheat on small hand-made populations (golden tests G-P7, G-P9, G-P10, G-P13).</summary>
public class LifeEventTests
{
    private static readonly (Sex, int) M30 = (Sex.Male, 30), F28 = (Sex.Female, 28);

    private static WorldTicker Ticker(SimWorld w, AbstractSystemRegistry? r = null) => new(w, r ?? Synth.Registry(needs: false, daily: false, seasonal: false));

    [Fact] // G-P7
    public void G_P7_PregnancyLastsFortyFiveDays_AndTheNextConceptionWaitsFifteen()
    {
        var s = new Synth();
        int hh = s.Household(8, M30, F28);
        s.Marry(s.MemberOf(hh, 0), s.MemberOf(hh, 1));
        // 100 percent a day conception: she conceives the first eligible day
        var w = s.Build(VitalRates.Default with { FertilityPctPerYear = 6000, ChronicleEveryEvent = true });
        int mother = s.MemberOf(hh, 1);
        using var t = Ticker(w);
        long day0 = w.State.HourDone / 24;
        // conceives in the household's slice hour on the first day; hour of day of the slice = household index % 24 = 0 -> hour 0 of day day0+1 (the first step is hour 1 of day0)
        t.AdvanceHours(1);                                                   // hour 0 of day0 is the world's start, already past; step hour index start+1
        long conceived = -1;
        for (int i = 0; i < 24 * 120 && w.People.Count == 2; i++)
        {
            t.AdvanceHours(1);
            if (conceived < 0 && w.People.PregnantUntilDay[mother] >= 0) conceived = (w.State.HourDone) / 24;
        }
        Assert.True(conceived > 0);
        Assert.Equal(3, w.People.Count);                                     // the child is born
        int child = 2;
        Assert.Equal(conceived + 45, w.People.BirthDay[child]);               // 45 days later
        Assert.Equal(w.People.BirthDay[child], w.People.LastBirthDay[mother]);
        Assert.Equal(-1, w.People.PregnantUntilDay[mother]);
        // eligible again only 15 days after the birth: the next pregnancy starts on day birth+15
        long second = -1;
        for (int i = 0; i < 24 * 40 && second < 0; i++) { t.AdvanceHours(1); if (w.People.PregnantUntilDay[mother] >= 0) second = w.State.HourDone / 24; }
        Assert.Equal(w.People.BirthDay[child] + 15, second);
        _ = day0;
    }

    [Fact]
    public void BirthMakesAPersonWithKinLinks_AnId_AndAFullNutritionReserve()
    {
        var s = new Synth();
        int hh = s.Household(8, M30, F28, (Sex.Male, 5));
        int father = s.MemberOf(hh, 0), mother = s.MemberOf(hh, 1);
        s.Marry(father, mother);
        var w = s.Build(VitalRates.Default with { ChronicleEveryEvent = true });
        long day = w.State.HourDone / 24;
        w.People.Father[2] = father; w.People.Mother[2] = mother;                 // the older son is theirs too
        w.People.PregnantUntilDay[mother] = (int)day + 1; w.People.FatherOfUnborn[mother] = father;
        w.Population.Kin.Children(mother);                                   // build the kin graph first: the birth must invalidate it
        using var t = Ticker(w);
        t.AdvanceHours(48);
        Assert.Equal(4, w.People.Count);
        int c = 3;
        var p = w.People;
        Assert.Equal((father, mother, hh), (p.Father[c], p.Mother[c], p.Household[c]));
        Assert.Equal(PersonStatus.Alive, p.Status[c]);
        Assert.Equal(p.Family[father], p.Family[c]);
        Assert.Equal(w.Ids.Peek(IdKind.Person).Counter - 1, p.IdOf(c).Counter);                // the allocator and the table agree on the id
        Assert.Equal(EconomyState.FullNutritionCd, w.Economy.NutritionCd[c]);
        Assert.Equal(4, w.Economy.PersonCount);
        Assert.Equal(4, w.Households.MemberCount[hh]); Assert.Contains(c, w.Households.Members(hh));
        Assert.Contains(c, w.Population.Kin.Children(mother).ToArray());                        // a rebuilt kin graph sees the child
        Assert.Contains(c, w.Population.Kin.Children(father).ToArray());
        Assert.Equal(new[] { 2, 3 }.Length, w.Population.Kin.Siblings(2).Count + 1);            // the older son (index 2) now has a brother
        Assert.Equal(1, w.Vital.Births);
        var birth = w.Chronicle.ByType("birth").Single();
        Assert.Contains(p.IdOf(c), birth.Subjects); Assert.All(birth.Causes, k => Assert.NotEqual(CauseKind.MarketDrift, k.Kind));
    }

    [Fact] // G-P13
    public void G_P13_BirthIdsDoNotDependOnHowTheHoursAreBatched()
    {
        string Run(Action<WorldTicker> advance)
        {
            var w = AbstractFixture.NewWorld();
            using var t = new WorldTicker(w, AbstractFixture.Registry());
            advance(t);
            return string.Join(",", Enumerable.Range(w.Population.People.Count - 40, 40).Select(i => $"{w.People.IdOf(i).Counter}:{w.People.Mother[i]}:{w.People.Sex[i]}:{w.People.Given[i]}"));
        }
        string a = Run(t => t.AdvanceDays(60)), b = Run(t => { for (int i = 0; i < 60; i++) t.AdvanceDays(1); }), c = Run(t => { for (int i = 0; i < 24 * 60; i++) t.AdvanceHours(1); });
        Assert.Equal(a, b); Assert.Equal(a, c);
    }

    [Fact] // G-P10
    public void G_P10_StarvationOfAThousandAdultsIsAboutFiveAndAHalfPercentAMonth()
    {
        var s = new Synth();
        // one adult per household, no food and no children: the reserve is empty (N = 0) the whole month
        var w0 = new Synth();
        for (int i = 0; i < 40; i++) w0.Household(4, (Sex.Male, 30));
        _ = s;
        // the synthetic builder only has 40+ dwelling lots; for 1,000 people use many households of one in the same lots by reusing the table directly
        var p = w0.People; var h = w0.Households;
        for (int i = 40; i < 1000; i++)
        {
            int hh = h.Add(w0.Settlement, -1, Ravensguard.Sim.WorldGen.Region.DwellingType.Cottage, 4);
            h.Head[hh] = p.Count; h.Class[hh] = SocialClass.Peasant; h.Name[hh] = "F" + hh;
            int x = p.Add(); p.Sex[x] = Sex.Male; p.BirthDay[x] = Synth.Day0 - 30 * TimeConst.DaysPerYear - 1; p.Household[x] = hh; p.Family[x] = "F" + hh; h.AddMember(hh, x);
        }
        var w = w0.Build();
        for (int i = 0; i < w.Economy.PersonCount; i++) w.Economy.NutritionCd[i] = 0;
        using var t = new WorldTicker(w, Synth.Registry(needs: false, daily: false, seasonal: false));
        t.AdvanceDays(30);
        int dead = 1000 - (int)w.Vital.Alive;
        // per day p = 200 x 3.0 + 5,000 = 5,600 ppm (crowding of one in four beds is 25 percent: no multiplier)
        double expected = 1000 * (1 - Math.Pow(1 - 0.0056, 30));
        Assert.InRange(dead, expected - 25, expected + 25);
        Assert.Equal(1000L, w.Vital.DeathsByCause.Sum() + w.Vital.Alive);
        Assert.True(w.Vital.DeathsByCause[(int)DeathCause.Hunger] > dead * 0.9, "almost every death of the month is a hunger death");
    }

    // ---------------------------------------------------------------- inheritance (G-P9)

    private static readonly (Sex, int)[] Family = [(Sex.Male, 45), (Sex.Female, 40), (Sex.Male, 19), (Sex.Male, 12), (Sex.Female, 16)];

    [Fact] // G-P9 first half
    public void G_P9_WhenTheHeadDiesTheEldestAdultSonSucceeds_AndNothingMoves()
    {
        var s = new Synth();
        int hh = s.Household(8, Family);
        int head = s.MemberOf(hh, 0), widow = s.MemberOf(hh, 1), son19 = s.MemberOf(hh, 2), son12 = s.MemberOf(hh, 3), daughter16 = s.MemberOf(hh, 4);
        s.Marry(head, widow);
        foreach (int k in new[] { 2, 3, 4 }) { s.People.Father[s.MemberOf(hh, k)] = head; s.People.Mother[s.MemberOf(hh, k)] = widow; }
        var w = s.Build(VitalRates.Default with { ChronicleEveryEvent = true });
        Synth.Fund(w, hh, 5_000, 20_000);
        var coin0 = Synth.CoinTotals(w);
        var ob = new RegionOutbox(w.Ids);
        Demography.Kill(w, head, DeathCause.Disease, GameTime.FromGameHours(w.State.HourDone), ob);
        Assert.Equal(son19, w.Households.Head[hh]);                             // eldest adult son
        Assert.Equal(DeedKind.Rented, w.Households.Deed[hh]);                    // deed unchanged
        Assert.Equal(4, w.Households.MemberCount[hh]);
        Assert.Equal(5_000, w.Economy.HouseholdCoin(hh).Mites); Assert.Equal(20_000, w.Economy.HouseholdStore[hh].Qty("wheat"));
        Assert.Equal(-1, w.People.Spouse[widow]); Assert.Equal(PersonStatus.Dead, w.People.Status[head]); Assert.Equal(DeathCause.Disease, w.People.DeathCause[head]);
        Assert.Equal(coin0, Synth.CoinTotals(w));
        Assert.Equal(w.Households.IdOf(hh), w.Households.IdOf(w.People.Household[son12]));
        _ = daughter16;
    }

    [Fact]
    public void NoAdultSon_TheDaughterThenTheSpouseThenTheEldestAdultSucceed()
    {
        var s = new Synth();
        int a = s.Household(8, (Sex.Male, 45), (Sex.Female, 40), (Sex.Female, 17), (Sex.Male, 12));
        s.Marry(s.MemberOf(a, 0), s.MemberOf(a, 1));
        foreach (int k in new[] { 2, 3 }) { s.People.Father[s.MemberOf(a, k)] = s.MemberOf(a, 0); s.People.Mother[s.MemberOf(a, k)] = s.MemberOf(a, 1); }
        int b = s.Household(8, (Sex.Male, 50), (Sex.Female, 45), (Sex.Male, 10));
        s.Marry(s.MemberOf(b, 0), s.MemberOf(b, 1));
        int c = s.Household(8, (Sex.Male, 50), (Sex.Female, 20), (Sex.Male, 70));
        int headA = s.MemberOf(a, 0), daughterA = s.MemberOf(a, 2), headB = s.MemberOf(b, 0), widowB = s.MemberOf(b, 1), headC = s.MemberOf(c, 0), eldestC = s.MemberOf(c, 2);
        var w = s.Build();
        var ob = new RegionOutbox(w.Ids); var now = GameTime.FromGameHours(w.State.HourDone);
        Demography.Kill(w, headA, DeathCause.Other, now, ob); Assert.Equal(daughterA, w.Households.Head[a]);       // the daughter of 17
        Demography.Kill(w, headB, DeathCause.Other, now, ob); Assert.Equal(widowB, w.Households.Head[b]);          // the widow
        Demography.Kill(w, headC, DeathCause.Other, now, ob); Assert.Equal(eldestC, w.Households.Head[c]);         // the eldest adult member (70)
    }

    [Fact] // G-P9 second half
    public void G_P9_OrphansGoWithCoinAndStockToTheGrandfathersHousehold()
    {
        var s = new Synth();
        int grand = s.Household(10, (Sex.Male, 70), (Sex.Female, 66));
        int home = s.Household(8, (Sex.Male, 40), (Sex.Male, 12), (Sex.Male, 9), (Sex.Female, 7));
        int father = s.MemberOf(home, 0);
        s.People.Father[father] = s.MemberOf(grand, 0); s.People.Mother[father] = s.MemberOf(grand, 1);
        for (int k = 1; k <= 3; k++) s.People.Father[s.MemberOf(home, k)] = father;
        var w = s.Build(VitalRates.Default with { ChronicleEveryEvent = true });
        Synth.Fund(w, home, 9_000, 30_000); Synth.Fund(w, grand, 1_000, 0);
        var coin0 = Synth.CoinTotals(w);
        var kids = new[] { s.MemberOf(home, 1), s.MemberOf(home, 2), s.MemberOf(home, 3) };
        Demography.Kill(w, father, DeathCause.Other, GameTime.FromGameHours(w.State.HourDone), new RegionOutbox(w.Ids));
        foreach (int k in kids) Assert.Equal(grand, w.People.Household[k]);
        Assert.Equal(5, w.Households.MemberCount[grand]); Assert.Equal(0, w.Households.MemberCount[home]);
        Assert.Equal(10_000, w.Economy.HouseholdCoin(grand).Mites); Assert.Equal(0, w.Economy.HouseholdCoin(home).Mites);
        Assert.Equal(30_000, w.Economy.HouseholdStore[grand].Qty("wheat")); Assert.Equal(0, w.Economy.HouseholdStore[home].Qty("wheat"));
        Assert.Equal(-1, w.Population.LotHousehold[s.Settlement][w.Households.Lot[home]]);       // the dwelling is a vacancy again
        Assert.Equal(coin0, Synth.CoinTotals(w));
        Assert.True(w.Economy.MassAuditHolds());
    }

    [Fact]
    public void NoKin_TheEstateEscheatsToTheLordsTreasury_AndTheChildrenBecomeWardsOfTheParish()
    {
        var s = new Synth();
        int parish = s.Household(10, (Sex.Male, 50), (Sex.Female, 48));
        int home = s.Household(8, (Sex.Male, 40), (Sex.Male, 9), (Sex.Female, 7));
        var w = s.Build();
        w.Population.PriestHousehold[s.Settlement] = parish;
        Synth.Fund(w, home, 7_000, 12_000);
        var e = w.Economy; var fief = w.Population.Region.Fiefs[s.Fief];
        var treasury = e.Ledger.Open(PurseKind.Treasury, fief.Id);
        long coinBefore = Synth.CoinTotals(w).Coin;
        Demography.Kill(w, s.MemberOf(home, 0), DeathCause.Other, GameTime.FromGameHours(w.State.HourDone), new RegionOutbox(w.Ids));
        Assert.Equal(7_000, e.Ledger.Balance(treasury).Mites);
        Assert.Equal(12_000, e.OtherStores[fief.Id].Qty("wheat"));
        Assert.Equal(parish, w.People.Household[s.MemberOf(home, 1)]); Assert.Equal(parish, w.People.Household[s.MemberOf(home, 2)]);
        Assert.Equal(coinBefore, Synth.CoinTotals(w).Coin);
        Assert.Equal(1, e.Ledger.Flow(TransferReason.Escheat).Mites > 0 ? 1 : 0);
        Assert.True(e.MassAuditHolds());
    }

    [Fact]
    public void OrphansWithNoKinAndNoParish_StayWithTheEldestChildAsHead_AndAreMarkedVagrant()
    {
        var s = new Synth();
        int home = s.Household(8, (Sex.Male, 40), (Sex.Male, 9), (Sex.Female, 12));
        int eldest = s.MemberOf(home, 2), younger = s.MemberOf(home, 1);
        var w = s.Build();
        Demography.Kill(w, s.MemberOf(home, 0), DeathCause.Other, GameTime.FromGameHours(w.State.HourDone), new RegionOutbox(w.Ids));
        Assert.Equal(eldest, w.Households.Head[home]);
        Assert.Equal(2, w.Households.MemberCount[home]);
        Assert.All(new[] { eldest, younger }, k => Assert.Equal(HomelessReason.Vagrant, w.People.Homeless[k]));
    }

    [Fact]
    public void LastMemberDies_TheEstateGoesToAChildInAnotherHousehold()
    {
        var s = new Synth();
        int elder = s.Household(6, (Sex.Male, 70));
        int son = s.Household(6, (Sex.Male, 35), (Sex.Female, 33));
        s.People.Father[s.MemberOf(son, 0)] = s.MemberOf(elder, 0);
        var w = s.Build();
        Synth.Fund(w, elder, 3_000, 5_000);
        Demography.Kill(w, s.MemberOf(elder, 0), DeathCause.Age, GameTime.FromGameHours(w.State.HourDone), new RegionOutbox(w.Ids));
        Assert.Equal(3_000, w.Economy.HouseholdCoin(son).Mites); Assert.Equal(5_000, w.Economy.HouseholdStore[son].Qty("wheat"));
        Assert.Equal(0, w.Economy.HouseholdCoin(elder).Mites);
    }

    [Fact]
    public void ADeadFiefLordPassesTheTitleToHisHeir()
    {
        var s = new Synth();
        int hh = s.Household(10, (Sex.Male, 50), (Sex.Female, 45), (Sex.Male, 22));
        s.Marry(s.MemberOf(hh, 0), s.MemberOf(hh, 1));
        s.People.Father[s.MemberOf(hh, 2)] = s.MemberOf(hh, 0);
        var w = s.Build();
        int lord = s.MemberOf(hh, 0), heir = s.MemberOf(hh, 2);
        w.Population.FiefLord[s.Fief] = lord; w.Population.FiefLordHousehold[s.Fief] = hh;
        Demography.Kill(w, lord, DeathCause.Violence, GameTime.FromGameHours(w.State.HourDone), new RegionOutbox(w.Ids));
        Assert.Equal(heir, w.Population.FiefLord[s.Fief]);
    }

    [Fact]
    public void PersonDiedCommandWinsOverTheVitalRoll_AndEveryDeathNamesACause()
    {
        var s = new Synth();
        int hh = s.Household(8, (Sex.Male, 30), (Sex.Female, 28));
        var w = s.Build(VitalRates.Default with { ChronicleEveryEvent = true });
        using var t = Ticker(w);
        int victim = s.MemberOf(hh, 0);
        t.Commands.Enqueue(1, CommandKind.PersonDied, EntityId.None, w.People.IdOf(victim), (long)DeathCause.Violence);
        t.AdvanceHours(1);
        Assert.Equal(DeathCause.Violence, w.People.DeathCause[victim]);
        var r = t.CommandLog.Single(); Assert.True(r.Result.Ok);
        t.Commands.Enqueue(2, CommandKind.PersonDied, EntityId.None, w.People.IdOf(victim), (long)DeathCause.Violence);
        t.AdvanceHours(1);
        Assert.Equal(CommandError.NotAlive, t.CommandLog[^1].Result.Error);                    // already dead: the second command is refused, nothing changes twice
        var e = w.Chronicle.ByType("death").Single();
        Assert.Equal(CauseKind.Wound, e.Causes[0].Kind); Assert.Equal("Violence", e.Data["cause"]);
    }
}
