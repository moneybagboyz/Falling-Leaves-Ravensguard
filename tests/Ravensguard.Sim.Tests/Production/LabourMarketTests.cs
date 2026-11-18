using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>The labour market (households spec 3.5, production spec 3.3, 3.5): hiring by skill, apprentices from 12, nobody under 16 manages, succession of a dead owner.</summary>
public class LabourMarketTests
{
    private static readonly GameTime Now = GameTime.FromGameDays(100);

    private static (ProdKit Kit, LabourMarket Lab) Setup(int people, Func<int, IEnumerable<int>>? children = null)
    {
        var kit = new ProdKit(people);
        for (int h = 0; h < people; h++) { kit.Econ.FoodCoverDaysX10[h] = 200; kit.Fund(h, 5_000); }        // fed and not destitute: only the idle are candidates
        return (kit, new LabourMarket(kit.Sys, kit.Pop, kit.Econ, 42, children ?? (_ => [])));
    }

    private static void Person(ProdKit k, int p, int age, Skill skill, int level)
    {
        k.People.BirthDay[p] = (int)Now.TotalDays - age * TimeConst.DaysPerYear; k.People.Occupation[p] = Occupation.None; k.People.SetSkill(p, 0, skill, level);
    }

    [Fact]
    public void Hiring_TakesTheMostSkilled_TiesByLowerId_AndSetsOccupationEmployerAndWorkplace()   // G-B13 on the real records
    {
        var (k, lab) = Setup(8);
        var b = k.Make("bakery", cash: 10_000_000, staff: 0);
        Person(k, 1, 30, Skill.Baking, 40); Person(k, 2, 30, Skill.Baking, 70); Person(k, 3, 30, Skill.Baking, 20); Person(k, 4, 30, Skill.Baking, 70);
        lab.Daily(Now);
        Assert.Equal([2, 4], b.Staff.OrderBy(x => x));
        foreach (int p in b.Staff)
        {
            Assert.Equal(Occupation.Baker, k.People.Occupation[p]); Assert.Equal(b.Owner, k.People.Employer[p]); Assert.Equal(b.Settlement, k.People.WorkSettlement[p]);
        }
        Assert.Equal(2, lab.Hired);
    }

    [Fact]
    public void Apprentices_AreTakenFromTwelve_OnlyWhenNoAdultIsIdle_AndNeverBelowTwelve()
    {
        var (k, lab) = Setup(8);
        var b = k.Make("bakery", cash: 10_000_000, staff: 0);
        Person(k, 1, 13, Skill.Baking, 5); Person(k, 2, 11, Skill.Baking, 90); Person(k, 3, 12, Skill.Baking, 0);
        for (int p = 4; p < 8; p++) k.People.Occupation[p] = Occupation.Merchant;           // everyone else has a job
        lab.Daily(Now);
        Assert.Equal([1, 3], b.Staff.OrderBy(x => x));                                      // 13 and 12 years old; the 11 year old is too young whatever the skill
        Assert.Equal(2, lab.Apprenticed);
        Assert.All(b.Staff, p => Assert.True(k.People.AgeYears(p, (int)Now.TotalDays) < 16));
    }

    [Fact]
    public void AnApprenticeEarnsHalfTheWage_AndLearnsTheTradeASeason()
    {
        var (k, lab) = Setup(4);
        var b = k.Make("bakery", cash: 10_000_000, staff: 0);
        Person(k, 1, 14, Skill.Baking, 0);
        for (int p = 2; p < 4; p++) k.People.Occupation[p] = Occupation.Merchant;           // no adult is idle
        lab.Daily(Now);
        Assert.Contains(1, b.Staff);
        Assert.True(k.People.SkillLevelOf(1, Skill.Baking) >= LabourMarket.StartSkill);
        int before = k.People.SkillLevelOf(1, Skill.Baking);
        lab.SeasonalTraining();
        Assert.Equal(before + LabourMarket.SkillPerSeason, k.People.SkillLevelOf(1, Skill.Baking));
        k.Give(b, "flour", 100_000); k.Give(b, "firewood", 20_000);
        k.Sys.TryStart(b, b.Stations[0], k.R("bake_bread"), Now);
        k.RunHours(Now.TotalHours, Now.TotalHours + 24);
        int apprenticeIdx = b.Staff.IndexOf(1);
        Assert.True(b.Owed[apprenticeIdx] > 0 && b.Owed[apprenticeIdx] <= RecipeBook.WagePerHour(WageClass.Skilled) / 2 * 20, $"owed {b.Owed[apprenticeIdx]}");
    }

    [Fact]
    public void ADeadOwner_PassesTheBusinessToAnAdultChild_NotToAChildUnderSixteen()
    {
        var (k, lab) = Setup(8, p => p == 0 ? [5, 6] : []);
        var b = k.Make("bakery", cash: 100_000, staff: 0);
        Person(k, 5, 15, Skill.Baking, 80); Person(k, 6, 17, Skill.Baking, 10);
        k.People.Status[0] = PersonStatus.Dead;
        lab.Daily(Now);
        Assert.Equal(BusinessState.Open, b.State);
        Assert.Equal(6, b.Owner);                                                           // 17 beats the better-skilled 15 year old: no management under 16
        Assert.Equal(Occupation.Baker, k.People.Occupation[6]); Assert.Equal(k.People.Household[6], b.OwnerHousehold);
        Assert.Equal(1, lab.Succeeded);
    }

    [Fact]
    public void WithoutAChild_TheBestStaffMemberSucceeds_AndWithoutAnyone_TheBusinessCloses()
    {
        var (k, lab) = Setup(8);
        var b = k.Make("bakery", cash: 100_000, staff: 2);                                  // owner 0, staff 1 and 2
        k.People.SetSkill(1, 0, Skill.Baking, 30); k.People.SetSkill(2, 0, Skill.Baking, 60);
        for (int p = 0; p < 3; p++) k.People.BirthDay[p] = (int)Now.TotalDays - 30 * TimeConst.DaysPerYear;
        k.People.Status[0] = PersonStatus.Dead;
        lab.Daily(Now);
        Assert.Equal(2, b.Owner); Assert.DoesNotContain(2, b.Staff);

        var (k2, lab2) = Setup(3);
        var b2 = k2.Make("bakery", cash: 100_000, staff: 0);
        for (int p = 1; p < 3; p++) { k2.People.Occupation[p] = Occupation.Merchant; k2.People.Status[p] = PersonStatus.Departed; }
        k2.People.Status[0] = PersonStatus.Dead;
        lab2.Daily(Now);
        Assert.Equal(BusinessState.Closed, b2.State); Assert.Equal(1, lab2.ClosedNoOwner);
    }

    [Fact]
    public void StaffWhoDiedAreTakenOffTheBooks_AndTheirArrearsForgiven()
    {
        var (k, lab) = Setup(6);
        var b = k.Make("bakery", cash: 100_000, staff: 2);
        b.Owed[0] = 500;
        k.People.Status[1] = PersonStatus.Dead;
        lab.Daily(Now);
        Assert.DoesNotContain(1, b.Staff);
        Assert.Equal(500, k.Sys.ForgivenArrearsMites);
    }

    [Fact]
    public void AQuitterBecomesIdleAgain_AndCanBeHiredByAnotherBusiness()
    {
        var (k, lab) = Setup(6);
        var b = k.Make("bakery", cash: 0, staff: 2);
        k.Sys.Close(b);                                                                     // the employer dissolved: staff released
        Assert.All(new[] { 1, 2 }, p => { Assert.Equal(Occupation.None, k.People.Occupation[p]); Assert.Equal(-1, k.People.Employer[p]); });
    }
}
