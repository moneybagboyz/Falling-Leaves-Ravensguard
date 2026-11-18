using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;

namespace Ravensguard.Sim.Tests.PopulationRates;

/// <summary>The pure vital-rate golden tests of population-and-vital-rates.md section 8 (G-P1 to G-P6): hand-computed values, asserted exactly or within the stated tolerance.</summary>
public class VitalGoldenTests
{
    private static readonly VitalRates R = VitalRates.Default;

    [Theory] // G-P1
    [InlineData(0, 3333)] [InlineData(1, 500)] [InlineData(4, 500)] [InlineData(5, 133)] [InlineData(14, 133)] [InlineData(15, 200)] [InlineData(49, 200)]
    [InlineData(50, 500)] [InlineData(64, 500)] [InlineData(65, 1666)] [InlineData(100, 1666)]
    public void G_P1_DailyDeathPpmFromTheTable(int age, int ppm) => Assert.Equal(ppm, VitalMath.BasePpm(R, age));

    [Fact] // G-P2
    public void G_P2_AdultWithHungerCrowdingAndTownDisease_Is528PpmADay()
    {
        var h = VitalMath.Hazard(R, 30, nutritionCd: 800, crowdingPct: 140, town: true, cold: false, outbreakMultPermille: 1000);
        Assert.Equal(528, h.TotalPpm);                                    // 200 x 2.0 x 1.1 x 1.2
        Assert.Equal(528, VitalMath.DeathPpm(R, 30, 800, 140, true, false, 1000));
        Assert.Equal(200, h.Other);                                       // the age-30 base
        Assert.Equal(200, h.Hunger);                                      // the reserve doubled the base
        Assert.Equal(128, h.Disease + (h.TotalPpm - h.Other - h.Hunger - h.Disease - h.Age - h.Cold)); // crowding 40 + town disease 88
    }

    [Theory] // G-P3
    [InlineData(3333, 1815)] [InlineData(200, 119)] [InlineData(1666, 952)]
    public void G_P3_EffectiveYearlyMortalityOfTheLiteralRule(int dailyPpm, int pctX100) => Assert.InRange(VitalMath.EffectiveYearlyPctX100(dailyPpm), pctX100 - 1, pctX100 + 1);

    [Fact] // G-P4
    public void G_P4_StationaryLifeTableOfTheRates()
    {
        // survival through each year of age at the daily hazards; people alive in an age year = births x the mean of survival at its two ends
        int max = R.MaxAgeYears;
        var surv = new double[max + 2]; surv[0] = 1;
        for (int a = 0; a <= max; a++) surv[a + 1] = surv[a] * Math.Pow(1 - VitalMath.BasePpm(R, a) / 1e6, TimeConst.DaysPerYear);
        surv[max + 1] = 0;
        var alive = new double[max + 1];
        for (int a = 0; a <= max; a++) alive[a] = (surv[a] + surv[a + 1]) / 2;
        double e0 = alive.Sum(), kids = alive[..15].Sum() / e0, mid = alive[15..50].Sum() / e0, old = alive[50..].Sum() / e0;
        // the spec quotes 38.7 / 28.8 / 49.8 / 21.5 / 645.8; the discrete table of this model gives 38.2 / 28.7 / 50.1 / 21.2 / 654 (same rates, mid-year convention)
        Assert.InRange(e0, 38.0, 39.0);
        Assert.InRange(kids * 100, 28.0, 29.5); Assert.InRange(mid * 100, 49.0, 50.5); Assert.InRange(old * 100, 21.0, 22.0);
        double births = 25_000 / e0;
        Assert.InRange(births, 640, 665);                                   // 2.58 percent of 25,000
    }

    [Fact] // G-P5
    public void G_P5_AboutFiftyEightPercentOfWomenMustBeMarriedAtEquilibrium()
    {
        int max = R.MaxAgeYears;
        var surv = new double[max + 2]; surv[0] = 1;
        for (int a = 0; a <= max; a++) surv[a + 1] = surv[a] * Math.Pow(1 - VitalMath.BasePpm(R, a) / 1e6, TimeConst.DaysPerYear);
        var alive = new double[max + 1];
        for (int a = 0; a <= max; a++) alive[a] = (surv[a] + surv[a + 1]) / 2;
        double e0 = alive.Sum(), women1844 = alive[18..45].Sum() / 2 / e0;     // half of each age is female
        double birthsPerWoman = 1 / e0 / 2 / women1844 * 2;                     // births per year / women 18-44
        double perMarriedYear = TimeConst.DaysPerYear / (200.0 + R.PregnancyDays + R.PostpartumDays);
        Assert.Equal(0.231, perMarriedYear, 3);                                 // one birth every 260 days
        Assert.InRange(birthsPerWoman / perMarriedYear * 100, 56, 60);          // spec 57.7
    }

    [Fact] // G-P6
    public void G_P6_ConceptionPpmFallsWithHunger()
    {
        Assert.Equal(5000, VitalMath.ConceptionPpm(R, 3000, 100));
        Assert.Equal(5000, VitalMath.ConceptionPpm(R, 2400, 100));
        Assert.Equal(4000, VitalMath.ConceptionPpm(R, 2399, 100));              // 1500..2399: x0.8
        Assert.Equal(2500, VitalMath.ConceptionPpm(R, 1000, 100));              // 500..1499: x0.5
        Assert.Equal(1000, VitalMath.ConceptionPpm(R, 400, 100));               // under 500: x0.2, expected wait 1,000 days instead of 200
        Assert.Equal(4000, VitalMath.ConceptionPpm(R, 3000, 151));              // crowding over 150: x0.8
        Assert.Equal(800, VitalMath.ConceptionPpm(R, 400, 160));
    }

    [Fact]
    public void DeathMultiplierIsCapped_AndHungerCreditsTheStarvationRate()
    {
        var capped = VitalMath.Hazard(R, 30, 0, 200, true, true, 2500);
        Assert.Equal(200 * 6000 / 1000 + 5000, capped.TotalPpm);                // base x6 cap + 0.5 percent a day of starvation
        Assert.True(capped.Hunger >= 5000);
        var infant = VitalMath.Hazard(R, 0, 0, 100, false, false, 1000);
        Assert.Equal(3333 * 3 + 10_000, infant.TotalPpm);                       // under 5 the starvation rate doubles
        Assert.Equal(VitalMath.DeathPpm(R, 70, 3000, 100, false, false, 1000), VitalMath.BasePpm(R, 70));
        Assert.Equal(DeathCause.Age, VitalMath.Hazard(R, 70, 3000, 100, false, false, 1000).Pick(0));
    }

    [Fact]
    public void HazardComponentsAlwaysSumToTheTotal_AndPickOnlyAssignsPositiveShares()
    {
        foreach (int age in new[] { 0, 3, 10, 30, 55, 80 })
            foreach (int cd in new[] { 3000, 1500, 600, 0 })
                foreach (bool cold in new[] { false, true })
                {
                    var h = VitalMath.Hazard(R, age, cd, 130, age % 2 == 0, cold, 1500);
                    Assert.Equal(h.TotalPpm, h.Age + h.Other + h.Hunger + h.Disease + h.Cold);
                    for (int u = 0; u < h.TotalPpm; u += Math.Max(1, h.TotalPpm / 37))
                    {
                        var c = h.Pick(u);
                        Assert.True(c != DeathCause.None);
                        if (cd >= 2000) Assert.NotEqual(DeathCause.Hunger, c);
                        if (!cold) Assert.NotEqual(DeathCause.Cold, c);
                    }
                }
    }

    [Fact]
    public void Roll_UsesPpmResolution()
    {
        int hits = 0;
        for (int i = 0; i < 200_000; i++) if (VitalMath.Roll(7, i, 99, 0x200, 0, 133)) hits++;      // 133 ppm of 200,000 = 26.6
        Assert.InRange(hits, 10, 50);
        Assert.False(VitalMath.Roll(7, 1, 1, 1, 0, 0)); Assert.True(VitalMath.Roll(7, 1, 1, 1, 0, 1_000_000));
    }
}
