using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;

namespace Ravensguard.Sim.Tests.PopulationRates;

/// <summary>Marriage gated on dwellings (G-P8), kin exclusion, the stem family, dowry, famine detection and its cause, and the rule that the poor die first.</summary>
public class MarriageAndFamineTests
{
    private static readonly VitalRates AllPropose = VitalRates.Default with { MarryChancePctPerSeason = 100, ChronicleEveryEvent = true };

    /// <summary>Ten families, each a full house (father, mother, one daughter of 20 and one son of 23 in different houses), then the vacancies.</summary>
    private static (Synth S, SimWorld W) TenCouples(int vacancies)
    {
        var s = new Synth();
        for (int i = 0; i < 10; i++)
        {
            int girl = s.Household(3, (Sex.Male, 50), (Sex.Female, 48), (Sex.Female, 20));       // beds 3: full, so the stem family cannot take a groom in
            s.Marry(s.MemberOf(girl, 0), s.MemberOf(girl, 1));
            s.People.Father[s.MemberOf(girl, 2)] = s.MemberOf(girl, 0); s.People.Mother[s.MemberOf(girl, 2)] = s.MemberOf(girl, 1);
            int boy = s.Household(3, (Sex.Male, 50), (Sex.Female, 48), (Sex.Male, 23));
            s.Marry(s.MemberOf(boy, 0), s.MemberOf(boy, 1));
            s.People.Father[s.MemberOf(boy, 2)] = s.MemberOf(boy, 0); s.People.Mother[s.MemberOf(boy, 2)] = s.MemberOf(boy, 1);
        }
        s.Vacancies(vacancies);
        return (s, s.Build(AllPropose));
    }

    [Fact] // G-P8
    public void G_P8_TenMatchedCouplesAndThreeVacantDwellings_MakeExactlyThreeWeddings()
    {
        var (s, w) = TenCouples(3);
        int hh0 = w.Households.Count;
        var ob = new RegionOutbox(w.Ids);
        int weddings = Marriage.Run(w, GameTime.FromGameHours(w.State.HourDone), ob, seasonal: true);
        Assert.Equal(3, weddings);
        Assert.Equal(hh0 + 3, w.Households.Count);                                               // Households +3
        Assert.Equal(3, w.Vital.Marriages);
        var p = w.People;
        var wed = Enumerable.Range(0, p.Count).Where(i => p.Spouse[i] >= 0 && p.Age(w, i) < 30).ToList();
        Assert.Equal(6, wed.Count);
        // the three lowest man ids marry (all gaps are equal), each the woman matched to him in id order
        var grooms = wed.Where(i => p.Sex[i] == Sex.Male).OrderBy(i => i).ToList();
        var allGrooms = Enumerable.Range(0, p.Count).Where(i => p.Sex[i] == Sex.Male && w.AgeOn(i, w.State.HourDone / 24) == 23).OrderBy(i => i).Take(3).ToList();
        Assert.Equal(allGrooms, grooms);
        // seven couples wait: both of each pair at MarriageWait 1
        var waiting = Enumerable.Range(0, p.Count).Where(i => p.MarriageWait[i] > 0).ToList();
        Assert.Equal(14, waiting.Count);
        Assert.All(waiting, i => Assert.Equal(1, p.MarriageWait[i]));
        // vacancies are used up, every new household is a real one with a lot
        int vacant = 0;
        for (int l = 0; l < w.Population.LotHousehold[s.Settlement].Length; l++) if (w.Population.LotBeds[s.Settlement][l] > 0 && w.Population.LotHousehold[s.Settlement][l] < 0) vacant++;
        Assert.Equal(0, vacant);
        for (int hh = hh0; hh < w.Households.Count; hh++)
        {
            Assert.Equal(2, w.Households.MemberCount[hh]); Assert.Equal(hh, w.Population.LotHousehold[s.Settlement][w.Households.Lot[hh]]);
            Assert.Equal(DeedKind.Rented, w.Households.Deed[hh]); Assert.NotEqual(-1, w.Households.Head[hh]);
        }
        Assert.Equal(3, w.Chronicle.ByType("marriage").Count() + ob.Entries.Count(e => e.TypeId == "marriage") - w.Chronicle.ByType("marriage").Count());
    }

    [Fact]
    public void WithoutAnyVacancy_NoOneMarries_AndTheWaitingCountsGrowEachSeason()
    {
        var (_, w) = TenCouples(0);
        for (int season = 1; season <= 3; season++)
        {
            Assert.Equal(0, Marriage.Run(w, GameTime.FromGameHours(w.State.HourDone), new RegionOutbox(w.Ids), seasonal: true));
            Assert.Equal(season, w.People.MarriageWait.Take(w.People.Count).Max());
        }
    }

    [Fact]
    public void ACoupleWedsWhenAHousholdWithAFreeBedTakesTheBrideIn_WithoutAVacancy()
    {
        var s = new Synth();
        int widower = s.Household(6, (Sex.Male, 45), (Sex.Male, 9));                             // a head with room (beds 6, two members)
        int girl = s.Household(3, (Sex.Male, 50), (Sex.Female, 48), (Sex.Female, 25));
        s.Marry(s.MemberOf(girl, 0), s.MemberOf(girl, 1));
        var w = s.Build(AllPropose);
        Assert.Equal(1, Marriage.Run(w, GameTime.FromGameHours(w.State.HourDone), new RegionOutbox(w.Ids), seasonal: true));
        int bride = s.MemberOf(girl, 2);
        Assert.Equal(widower, w.People.Household[bride]);
        Assert.Equal(3, w.Households.MemberCount[widower]); Assert.Equal(2, w.Households.MemberCount[girl]);
    }

    [Fact]
    public void SiblingsAndFirstCousinsDoNotMarry()
    {
        var s = new Synth();
        int grand = s.Household(6, (Sex.Male, 72), (Sex.Female, 70));
        int h1 = s.Household(3, (Sex.Male, 48), (Sex.Female, 45), (Sex.Female, 20), (Sex.Male, 23));       // a brother and a sister
        int h2 = s.Household(3, (Sex.Male, 46), (Sex.Female, 44), (Sex.Male, 22));                          // a male first cousin of the sister
        s.Marry(s.MemberOf(grand, 0), s.MemberOf(grand, 1)); s.Marry(s.MemberOf(h1, 0), s.MemberOf(h1, 1)); s.Marry(s.MemberOf(h2, 0), s.MemberOf(h2, 1));
        var p = s.People;
        foreach (int h in new[] { h1, h2 }) { p.Father[s.MemberOf(h, 0)] = s.MemberOf(grand, 0); p.Mother[s.MemberOf(h, 0)] = s.MemberOf(grand, 1); }
        foreach (int k in new[] { 2, 3 }) { p.Father[s.MemberOf(h1, k)] = s.MemberOf(h1, 0); p.Mother[s.MemberOf(h1, k)] = s.MemberOf(h1, 1); }
        p.Father[s.MemberOf(h2, 2)] = s.MemberOf(h2, 0); p.Mother[s.MemberOf(h2, 2)] = s.MemberOf(h2, 1);
        s.Vacancies(4);
        var w = s.Build(AllPropose);
        Assert.Equal(0, Marriage.Run(w, GameTime.FromGameHours(w.State.HourDone), new RegionOutbox(w.Ids), seasonal: true));
        Assert.Equal(-1, p.Spouse[s.MemberOf(h1, 2)]);
    }

    [Fact]
    public void ADowryOfFivePercentAndAShareOfStockStartTheNewHousehold_AndCoinIsConserved()
    {
        var (s, w) = TenCouples(1);
        for (int hh = 0; hh < 20; hh++) Synth.Fund(w, hh, 10_000, 100_000);
        var before = Synth.CoinTotals(w);
        int hh0 = w.Households.Count;
        Assert.Equal(1, Marriage.Run(w, GameTime.FromGameHours(w.State.HourDone), new RegionOutbox(w.Ids), seasonal: true));
        int created = hh0;
        Assert.Equal(500, w.Economy.HouseholdCoin(created).Mites);                               // 5 percent of the bride's parents' 10,000
        Assert.Equal(20_000, w.Economy.HouseholdStore[created].Qty("wheat"));                    // 10 percent of each family's 100 kg
        Assert.Equal(before, Synth.CoinTotals(w));
        Assert.True(w.Economy.MassAuditHolds());
        Assert.Equal(w.Households.IdOf(created), w.Economy.Ledger.OwnerOf(w.Economy.HouseholdPurse[created]));
    }

    // ---------------------------------------------------------------- famine

    private static (Synth S, SimWorld W) HungryTown(int households, VitalRates? rates = null)
    {
        var s = new Synth();
        for (int i = 0; i < households; i++) s.Household(8, (Sex.Male, 35), (Sex.Female, 33), (Sex.Male, 10), (Sex.Female, 6), (Sex.Male, 3));
        var w = s.Build(rates ?? VitalRates.Default with { ChronicleEveryEvent = true });
        for (int i = 0; i < households; i++) Synth.Fund(w, i, 0, 3 * 5_500);                    // three days of food
        return (s, w);
    }

    [Fact]
    public void FiveDaysOfHungerInAFifthOfTheHouseholds_RecordAFamine_WithNoRecordedCauseItIsMarketDrift()
    {
        var (_, w) = HungryTown(20);
        using var t = new WorldTicker(w, Synth.Registry());
        t.AdvanceDays(12);
        var f = w.Chronicle.ByType("famine").ToList();
        Assert.Single(f);
        Assert.Equal(CauseKind.MarketDrift, f[0].Causes[0].Kind);
        Assert.Contains("hungry_households", f[0].Data.Keys);
        Assert.Equal(1, t.Snapshot.Famines);
    }

    [Fact]
    public void AWeatherEntryInTheLast120Days_IsTheNamedCauseOfTheFamine_AndHungerDeathsPointAtIt()
    {
        var (s, w) = HungryTown(20);
        var now = GameTime.FromGameHours(w.State.HourDone);
        var drought = w.Ids.Allocate(IdKind.Event);
        w.Chronicle.Add(new ChronicleEntry(drought, now - Duration.FromGameDays(20), "harvest_failure", [], [new Cause(CauseKind.Weather, null, "drought")], new Dictionary<string, string>()));
        using var t = new WorldTicker(w, Synth.Registry());
        t.AdvanceDays(80);
        var famine = w.Chronicle.ByType("famine").First();
        Assert.Equal(CauseKind.Weather, famine.Causes[0].Kind); Assert.Equal(drought, famine.Causes[0].EventId);
        var starved = w.Chronicle.ByType("death").Where(e => e.Data["cause"] == "Hunger").ToList();
        Assert.NotEmpty(starved);
        Assert.Contains(starved, e => e.Causes[0].Kind == CauseKind.Famine && e.Causes[0].EventId == famine.Id);   // linked to the famine entry
        Assert.True(w.Chronicle.CauseCoverage() >= 0.9);
    }

    [Fact]
    public void ThePoorDieFirst_BecauseTheyHaveNoStores()
    {
        var s = new Synth();
        for (int i = 0; i < 40; i++) s.Household(8, (Sex.Male, 35), (Sex.Female, 33), (Sex.Male, 10), (Sex.Female, 6), (Sex.Male, 3));
        var w = s.Build();
        for (int i = 0; i < 40; i += 2) Synth.Fund(w, i, 0, 400_000);                            // even households: a year of grain; odd ones: nothing
        using var t = new WorldTicker(w, Synth.Registry(seasonal: false));
        t.AdvanceDays(60);
        int richDead = 0, poorDead = 0;
        for (int p = 0; p < w.People.Count; p++) if (w.People.Status[p] == PersonStatus.Dead) { if (w.People.Household[p] % 2 == 0) richDead++; else poorDead++; }
        Assert.True(poorDead >= 8, $"poor dead {poorDead}");
        Assert.True(richDead <= 2, $"rich dead {richDead}");
        Assert.True(w.Vital.DeathsByCause[(int)DeathCause.Hunger] >= poorDead * 0.8);
    }

    [Fact]
    public void Malnutrition_RaisesDiseaseDeathsOfTheOutbreakInTheHungry()
    {
        var r = VitalRates.Default;
        var fed = VitalMath.Hazard(r, 30, 3000, 100, false, false, 2500);                        // a severe outbreak hits the fed only through the age/nutrition filter in the system
        var hungry = VitalMath.Hazard(r, 30, 600, 100, false, false, 2500);
        Assert.True(hungry.TotalPpm > 2 * fed.TotalPpm);
        Assert.True(hungry.Disease > fed.Disease);
    }
}

internal static class PersonTableTestExt
{
    public static int Age(this PersonTable p, SimWorld w, int i) => w.AgeOn(i, w.State.HourDone / 24);
}
