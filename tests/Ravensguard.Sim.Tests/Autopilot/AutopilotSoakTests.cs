using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Travel;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Autopilot;

/// <summary>AP-11, the year soak (a game year is 60 days): the hero on autopilot lives and stays solvent in a peaceful region.</summary>
public class AutopilotSoakTests(ITestOutputHelper output)
{
    [Theory, Trait("Category", "Soak")]
    [InlineData(0UL)]      // the soak world of seed 1234
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void AP11_TheHeroLivesAGameYearOnAutopilot_AliveAndSolvent_InAPeacefulRegion(ulong seed)
    {
        using var w = seed == 0 ? ApWorld.Soak(heroCrowns: 30) : ApWorld.Seeded(seed, heroCrowns: 30);
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Work(days: 20), Directive.Train(Skill.Smithing, 30), Directive.Work());
        st.Limits.SpendCapMitesPerDay = 200; st.Limits.NoCrimes = true; st.Limits.PurseReserveMites = 0;
        st.PresetWatch(); for (int i = 0; i < st.Policy.Length; i++) if ((InterruptKind)i != InterruptKind.BigChoice) st.Policy[i] = InterruptPolicy.Notify;
        w.Ap.Extras.Add(new TaggedAction("take_bread", 1200, ActionTag.Crime));                  // a crime on offer all year: the limit must keep it off the table
        long start = w.Ctx.Coin, minCoin = long.MaxValue; var cd = new List<long>(); long smith0 = w.Ctx.Craft.State.Book.Micro(w.Hero, Skill.Smithing);
        var days = new List<DaySummary>();
        w.RunHours(24 * TimeConst.DaysPerYear + 1, h => { minCoin = Math.Min(minCoin, w.Ctx.Coin); if (st.Summaries.Count > 0 && (days.Count == 0 || days[^1].Day != st.Summaries[^1].Day)) days.Add(st.Summaries[^1]); });
        long earned = days.Sum(d => d.Earned), spent = days.Sum(d => d.Spent);
        output.WriteLine($"seed {seed}: coin {start} -> {w.Ctx.Coin} (min {minCoin}), earned {earned}, spent {spent}, jobs {days.Sum(d => d.JobsDone)}, meals {days.Sum(d => d.Meals)}, smithing {smith0 / 1e6:0.0} -> {w.Ctx.Craft.State.Book.Micro(w.Hero, Skill.Smithing) / 1e6:0.0}, log {w.Book.Log.Lines.Count}");
        Assert.Equal(1000, st.HealthPm); Assert.False(st.Bleeding);                                // alive and well
        Assert.True(minCoin >= 0 && minCoin >= st.Limits.PurseReserveMites, $"min coin {minCoin}");
        Assert.True(days.Count >= TimeConst.DaysPerYear - 2, $"{days.Count} daily summaries");
        var hungry = days.Skip(1).Where(d => d.NutritionCd < 1000).ToList();
        Assert.True(hungry.Count == 0, $"{hungry.Count} days under 1000 cd: {string.Join(", ", hungry.Take(6).Select(d => $"day {d.Day % 60}: {d.NutritionCd} cd, {d.Meals} meals"))}");
        Assert.True(earned >= spent * 105 / 100, $"income {earned} must be at least 1.05 x spending {spent}");
        Assert.Equal(0, w.Book.Log.Violations); Assert.Equal(0, w.Book.Log.PlanViolations); Assert.Equal(0, w.Book.Log.Crimes);
        Assert.Equal(0, w.Book.Log.CountInterrupts(InterruptKind.Stuck));
        Assert.True(w.Ctx.Craft.State.Book.Level(w.Hero, Skill.Smithing) >= 30, "the training directive reached its level");
        Assert.True(w.Econ.Ledger.Audit().IsConserved);
        Assert.All(days, d => Assert.True(d.Spent <= 200 + 5, $"day {d.Day} spent {d.Spent} over the cap of 200"));
    }
}
