using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Tests.Abstract;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.PopulationRates;

/// <summary>The world setting <c>aging_pace</c> (phase 6 overview 10a, difficulty-sliders.md): the ageing clock scales, the per-year chances do not.</summary>
public class AgingPaceTests(ITestOutputHelper output)
{
    private static int DaysToAge(int pace, int age)
    {
        var pt = new PersonTable(1, 1) { AgingPace = pace }; pt.Add(); pt.BirthDay[0] = 0;
        int d = 0; while (pt.AgeYears(0, d) < age) d++;
        return d;
    }

    [Fact]
    public void AChildAtFourTimesComesOfAgeInAQuarterOfTheGameDays()
    {
        int d1 = DaysToAge(1, 16), d4 = DaysToAge(4, 16), d2 = DaysToAge(2, 16), d3 = DaysToAge(3, 16);
        Assert.Equal(16 * TimeConst.DaysPerYear, d1);
        Assert.Equal(d1 / 4, d4); Assert.Equal(d1 / 2, d2); Assert.Equal(d1 / 3, d3);
        // old age comes at the same factor: 60 at 4x after 15 years
        Assert.Equal(60 * TimeConst.DaysPerYear / 4, DaysToAge(4, 60));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void SeededAgesAreTheRequestedAgesAtEveryPace(int pace)
    {
        var pt = new PersonTable(1, 1) { AgingPace = pace }; pt.Add();
        for (int age = 0; age <= 90; age += 3)
            for (int extra = 0; extra < TimeConst.DaysPerYear; extra += 7)
            {
                pt.BirthDay[0] = PersonTable.BirthDayFor(70_000, age, extra, pace);
                Assert.Equal(age, pt.AgeYears(0, 70_000));
            }
    }

    [Fact]
    public void ThePaceOneDefaultLeavesTheOldArithmeticUnchanged()
    {
        var pt = new PersonTable(1, 1); pt.Add(); pt.BirthDay[0] = 100;
        Assert.Equal(1, pt.AgingPace);
        Assert.Equal((5000 - 100) / TimeConst.DaysPerYear, pt.AgeYears(0, 5000));
        Assert.Equal(5000 - 17 * TimeConst.DaysPerYear - 9, PersonTable.BirthDayFor(5000, 17, 9, 1));
    }

    [Fact]
    public void TheVitalRateSoakStillHoldsAtFourTimesPace()
    {
        // a short targeted run (one game year), not the 100-year soak: same people, ages rebased so everyone is as old as before, ageing four times as fast
        var w = AbstractFixture.NewWorld();
        var pt = w.People; int now = (int)GameTime.FromCalendar(w.Population.NowYear, Season.Spring, 1).TotalDays;
        for (int i = 0; i < pt.Count; i++) pt.BirthDay[i] = now - (now - pt.BirthDay[i]) / 4;
        pt.AgingPace = 4;
        long start = w.Vital.Alive;
        using var t = new WorldTicker(w, AbstractFixture.Registry());
        long coin = w.Economy.Ledger.Audit().Sourced.Mites;
        // one living person of about 20 now, to watch the clock
        int p = Enumerable.Range(0, pt.Count).First(i => pt.Status[i] == PersonStatus.Alive && pt.AgeYears(i, now) == 20);
        t.AdvanceDays(60);
        output.WriteLine($"4x year 1: start {start}, births {w.Vital.Births}, deaths {w.Vital.Deaths}, alive {w.Vital.Alive}");
        Assert.InRange(w.Vital.Births, 450, 900); Assert.InRange(w.Vital.Deaths, 450, 900);
        Assert.True(Math.Abs(w.Vital.Alive - start) * 100 <= start * 4, $"net change {w.Vital.Alive - start}");
        PopulationSoakTests.Audit(w, coin);
        // ageing ran at the faster clock: a year of game time is four years of age
        if (pt.Status[p] == PersonStatus.Alive) Assert.Equal(24, pt.AgeYears(p, now + 60));
    }
}
