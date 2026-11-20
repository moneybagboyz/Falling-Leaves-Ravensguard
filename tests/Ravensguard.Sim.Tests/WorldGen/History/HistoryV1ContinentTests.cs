using Ravensguard.Content;
using Ravensguard.Sim.Continental;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Tests.Politics;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.WorldGen.History;

/// <summary>HR-11: the continent in play runs the shared functions (the realm-level politics of <see cref="PoliticsEngine"/> and the great plague of the continent tick) within the history's targets scaled to 25 years.</summary>
public class HistoryV1ContinentTests(ITestOutputHelper output)
{
    private static GameTime At(int year, int season) => GameTime.FromCalendar(year, (Season)season, 2);

    [Fact]
    public void HR11_TwentyFiveYearsOfTheContinentTick_OnAV1History_ConserveCoin_StayInBudget_AndTheGreatPlagueIsRare()
    {
        var dir = ContinentSettings.FindBaseContent()!;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = 1234; hs.UseRulesV1(dir);
        var history = HistoryGenerator.Generate(ContinentGenerator.Generate(cs), hs);
        Assert.Equal(1, history.RulesVersion);
        int plagues = 0; double worst = 0; long coin0 = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var st = ContinentState.Create(seed, history, 120, 25_000, 25_000L * 5400);
            ContinentTick.Run(st, [], At(250, 0));
            long coin = st.CoinInSummaries(); coin0 = coin;
            long pop0 = st.Summaries.Sum(s => (long)s.Pop);
            for (int i = 1; i < 100; i++)
            {
                var r = ContinentTick.Run(st, [], At(250 + i / 4, i % 4));
                worst = Math.Max(worst, r.Ms);
            }
            plagues += st.Plague.Id;
            Assert.Equal(coin, st.CoinInSummaries());                                                       // coin conserved: trade, migration and the plague never make or lose it
            long pop = st.Summaries.Sum(s => (long)s.Pop);
            Assert.All(st.Summaries, s => { Assert.True(s.Pop >= 0 && s.CoinTotal >= 0); Assert.True(s.Households <= Math.Max(1, s.Pop)); });
            Assert.InRange(pop, pop0 / 3, pop0 * 3);
            output.WriteLine($"seed {seed}: {st.Plague.Id} plagues in 25 years, dead estimate {st.Plague.DeadEstimate:N0}, pop {pop0:N0} -> {pop:N0}");
        }
        output.WriteLine($"{plagues} plagues in 6 x 25 years; worst season {worst:F1} ms; coin {coin0:N0}");
        Assert.InRange(plagues, 0, 3);                                                                     // 0 to 4 in 200 years: about a half in 25, so a few across six worlds
#if !DEBUG
        Assert.True(worst < 500, $"a seasonal continent tick took {worst:F0} ms (budget 500)");
#endif
    }

    [Fact]
    public void HR11_TwentyFiveYearsOfRealmPolitics_WarsAndPeacesStayInTheTargetsScaledToTwentyFiveYears()
    {
        using var rig = new PoliticsRig();
        int day0 = PoliticsRig.NextSeasonStart(rig.Day);
        int realms = rig.C().AliveRealms().Count();
        long warSeasons = 0; int maxAtWar = 0;
        for (int s = 0; s < 100; s++)
        {
            PoliticsEngine.WholeSeason(rig.C(day0 + s * TimeConst.DaysPerSeason));
            int atWar = rig.B.Wars.Count(w => w.Alive);
            warSeasons += atWar; maxAtWar = Math.Max(maxAtWar, atWar);
        }
        int declared = rig.Entries("realm_war_declared").Count(), peaces = rig.Entries("realm_peace").Count();
        var alive = rig.C().AliveRealms().ToList();
        int pairs = realms * (realms - 1) / 2, neighbours = alive.SelectMany(a => alive.Where(b => b > a && rig.C().InContact(a, b))).Count();
        long share = warSeasons * 1000 / Math.Max(1, 100L * pairs);
        output.WriteLine($"{realms} realms, {pairs} pairs ({neighbours} neighbouring): {declared} wars declared, {peaces} peaces, at most {maxAtWar} at once, {share / 10.0:F1}% of the pair-seasons at war");
        Assert.True(declared >= 1);
        Assert.InRange(share, 0, 350);                                                                     // pairs at war <= 35 percent
        Assert.True(peaces <= declared + 3); Assert.True(declared - peaces <= pairs);                         // every war either ended or is still going (and no more than the pairs)
        Assert.InRange(declared * 1000 / Math.Max(1, neighbours), 0, 1500 / 4);                           // 0.5 to 1.5 a century a neighbouring pair, scaled to 25 years: at most 0.375
    }
}
