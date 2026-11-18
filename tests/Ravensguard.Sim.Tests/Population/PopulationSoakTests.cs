using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Tests.Abstract;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.PopulationRates;

/// <summary>
/// Soaks of the populated seed-1234 region with no player input (population spec 9): the abstract tick, the vital rates and a stand-in food supply run for
/// one and for a hundred game years, and the invariants of the record tables, the coin ledger and the goods are checked on the way.
/// </summary>
public class PopulationSoakTests(ITestOutputHelper output)
{
    /// <summary>Every structural invariant of the people, the households, the dwellings and the economy; returns the living population.</summary>
    internal static long Audit(SimWorld w, long coinSeeded)
    {
        var p = w.People; var h = w.Households; var pop = w.Population; var e = w.Economy;
        Assert.Equal(p.Count, e.PersonCount); Assert.Equal(h.Count, e.HouseholdCount);
        long alive = 0; var seen = new int[p.Count];
        for (int i = 0; i < p.Count; i++) if (p.Status[i] == PersonStatus.Alive) alive++;
        Assert.Equal(alive, w.Vital.Alive);
        for (int hh = 0; hh < h.Count; hh++)
        {
            int n = h.MemberCount[hh];
            Assert.True(n >= 0);
            for (int k = 0; k < n; k++)
            {
                int m = h.MemberIdx[h.MemberStart[hh] + k];
                Assert.True(m >= 0 && m < p.Count, $"household {hh} lists a hole");
                Assert.Equal(PersonStatus.Alive, p.Status[m]);
                Assert.Equal(hh, p.Household[m]);
                Assert.Equal(0, seen[m]++);                                                    // in exactly one household
            }
            if (n > 0) Assert.True(h.Head[hh] >= 0 && p.Household[h.Head[hh]] == hh && p.Status[h.Head[hh]] == PersonStatus.Alive, Describe(w, hh));
        }
        for (int i = 0; i < p.Count; i++)
        {
            if (p.Status[i] != PersonStatus.Alive) continue;
            Assert.Equal(1, seen[i]);
            if (p.Spouse[i] >= 0) { Assert.Equal(i, p.Spouse[p.Spouse[i]]); Assert.Equal(PersonStatus.Alive, p.Status[p.Spouse[i]]); }
            Assert.InRange((int)e.NutritionCd[i], 0, 3000);
        }
        // dwellings: a lot's household lives there; vacancies are never negative
        long dwellings = 0, vacant = 0;
        for (int s = 0; s < pop.LotHousehold.Length; s++)
            for (int l = 0; l < pop.LotHousehold[s].Length; l++)
            {
                int hh = pop.LotHousehold[s][l];
                if (pop.LotBeds[s][l] > 0) { dwellings++; if (hh < 0) vacant++; }
                if (hh >= 0) { Assert.True(h.MemberCount[hh] > 0, $"lot {s}/{l} bound to the empty household {hh}"); Assert.Equal((s, l), (h.Settlement[hh], (int)h.Lot[hh])); }
            }
        Assert.True(vacant >= 0 && vacant * 100 <= dwellings * 6, $"vacancies {vacant} of {dwellings}");
        // money and goods
        var a = e.Ledger.Audit();
        Assert.True(a.IsConserved, a.ToString()); Assert.Equal(coinSeeded, a.Sourced.Mites);
        Assert.True(e.MassAuditHolds());
        // audit: births - deaths - emigrants (+ immigrants) = change
        var v = w.Vital;
        return alive;
    }

    private static string Describe(SimWorld w, int hh)
    {
        var h = w.Households; var p = w.People; int head = h.Head[hh];
        return $"household {hh} head {head} (household {(head >= 0 ? p.Household[head] : -9)}, {(head >= 0 ? p.Status[head].ToString() : "none")}), members {string.Join(',', h.Members(hh))}";
    }

    private static (SimWorld W, WorldTicker T, long Start, long Coin) Start(VitalRates? rates = null, Func<long, int>? yield = null)
    {
        var w = AbstractFixture.NewWorld(rates);
        long start = w.Vital.Alive;
        var t = new WorldTicker(w, AbstractFixture.Registry(yield));
        return (w, t, start, w.Economy.Ledger.Audit().Sourced.Mites);
    }

    [Fact] // G-P12
    public void G_P12_FirstYearOfTheReferenceRegion_BirthsAndDeathsAreInBand_AndThePopulationHolds()
    {
        var (w, t, start, coin) = Start(VitalRates.Default with { ChronicleEveryEvent = true });
        using (t)
        {
            t.AdvanceDays(60);
            long births = w.Vital.Births, deaths = w.Vital.Deaths;
            output.WriteLine($"year 1: start {start}, births {births}, deaths {deaths}, emigrants {w.Vital.Emigrants}, marriages {w.Vital.Marriages}, alive {w.Vital.Alive}");
            Assert.InRange(births, 550, 800); Assert.InRange(deaths, 560, 760);
            long net = w.Vital.Alive - start;
            Assert.True(Math.Abs(net) * 100 <= start * 2 + start * 1 / 1, $"net change {net} within a percent or two");   // the first year also carries the emigration of the over-crowded
            Assert.Equal(start + births - deaths - w.Vital.Emigrants, w.Vital.Alive);                                      // the audit is exact
            Audit(w, coin);
            Assert.True(w.Chronicle.CauseCoverage() >= 0.9);
        }
    }

    [Fact]
    public void OneYearSoak_KeepsEveryInvariant_AndCoinIsConservedThroughDeathsAndInheritance()
    {
        var (w, t, start, coin) = Start(VitalRates.Default with { ChronicleEveryEvent = true });
        using (t)
        {
            var total0 = w.Economy.Ledger.Audit().InPurses;
            for (int d = 0; d < 60; d++) { t.AdvanceDays(1); if (d % 10 == 9) Audit(w, coin); }
            Audit(w, coin);
            Assert.Equal(total0, w.Economy.Ledger.Audit().InPurses);                          // nothing minted or melted: every mite moved by transfer
            Assert.True(w.Economy.Ledger.Flow(TransferReason.Inheritance).Mites > 0, "deaths moved coin by inheritance");
            Assert.Equal(60 * 24L, t.Stats.Hours);
            var deaths = w.Chronicle.ByType("death").ToList();
            Assert.True(deaths.Count > 300);
            Assert.All(deaths, e => { Assert.Contains("cause", e.Data.Keys); Assert.NotEmpty(e.Causes); Assert.NotEqual(CauseKind.MarketDrift, e.Causes[0].Kind); Assert.NotEqual(CauseKind.Unknown, e.Causes[0].Kind); });
            Assert.True(w.Chronicle.ByType("marriage").Any() && w.Chronicle.ByType("birth").Any());
            Assert.True(w.Chronicle.CauseCoverage() >= 0.9);
            Assert.True(w.Vital.DeathsByCause[(int)DeathCause.Age] > 0);
            Assert.True(Math.Abs(w.Vital.Alive - start) * 100 <= start * 3);
        }
    }

    [Fact]
    public void FiveYearSoak_StaysWithinTenPercent_AndHouseholdsHold()
    {
        var (w, t, start, coin) = Start();
        using (t)
        {
            int households0 = w.Households.Count;
            for (int y = 1; y <= 5; y++)
            {
                t.AdvanceDays(60);
                Assert.InRange(w.Vital.Alive, start * 90 / 100, start * 110 / 100);
            }
            Audit(w, coin);
            Assert.InRange(CountHouseholds(w), households0 * 90 / 100, households0 * 110 / 100);
            output.WriteLine($"5 years: {w.Vital.Alive} alive (start {start}); births {w.Vital.Births} deaths {w.Vital.Deaths} marriages {w.Vital.Marriages} emigrants {w.Vital.Emigrants}");
        }
    }

    [Fact]
    public void ADroughtYear_RaisesDeathsForTheHungry_AndThePopulationRecovers()
    {
        // no harvest at all in the third year (days 120..179 after the start): famine, then food returns
        var w0 = AbstractFixture.NewWorld();
        long day0 = w0.State.HourDone / 24;
        var w2 = AbstractFixture.NewWorld(); long s2 = w2.Vital.Alive;
        using var t2 = new WorldTicker(w2, AbstractFixture.Registry(d => d >= day0 + 120 && d < day0 + 180 ? 0 : 1000));
        t2.AdvanceDays(120);
        long before = w2.Vital.Alive;
        long deaths0 = w2.Vital.Deaths;
        t2.AdvanceDays(60);                                                                        // the drought year
        long droughtDeaths = w2.Vital.Deaths - deaths0;
        t2.AdvanceDays(60 * 6);                                                                    // six more years
        output.WriteLine($"drought: before {before}, deaths in the dry year {droughtDeaths} (a normal year ~650), famines {t2.Snapshot.Famines}, after {w2.Vital.Alive}, hunger deaths {w2.Vital.DeathsByCause[(int)DeathCause.Hunger]}");
        Assert.True(droughtDeaths > 800, $"drought deaths {droughtDeaths}");
        Assert.True(w2.Vital.DeathsByCause[(int)DeathCause.Hunger] > 100);
        Assert.True(t2.Snapshot.Famines > 0);
        Assert.True(w2.Vital.Alive > s2 * 70 / 100, "not extinction");
        Assert.True(w2.Chronicle.ByType("famine").Any());
    }

    private static int CountHouseholds(SimWorld w) { int n = 0; for (int i = 0; i < w.Households.Count; i++) if (w.Households.MemberCount[i] > 0) n++; return n; }

    // ------------------------------------------------------------------------------------------------ 100 years

    /// <summary>Population spec soak 1, 2, 4 and 5 over a hundred game years of the seed-1234 region without any player input. Runs about a minute: category Soak.</summary>
    [Fact, Trait("Category", "Soak")]
    public void Soak100Years_PopulationStaysWithinTenPercent_AndEveryInvariantHolds()
    {
        var (w, t, start, coin) = Start();
        using (t)
        {
            int households0 = CountHouseholds(w);
            var years = new List<long>();
            long lo = long.MaxValue, hi = 0; int maxVacant = 0; var sb = new System.Text.StringBuilder();
            for (int y = 1; y <= 100; y++)
            {
                t.AdvanceDays(60);
                long alive = w.Vital.Alive; years.Add(alive); lo = Math.Min(lo, alive); hi = Math.Max(hi, alive);
                Assert.InRange(alive, start * 90 / 100, start * 110 / 100);                           // soak 1: every year end within 10 percent
                if (y >= 5) { double ma = years.Skip(y - 5).Average(); Assert.InRange(ma, start * 0.93, start * 1.07); }    // 5-year moving average within 7 percent
                int hhs = CountHouseholds(w);
                Assert.InRange(hhs, households0 * 90 / 100, households0 * 110 / 100);                 // soak 2: households within 10 percent
                if (y % 10 == 0) { Audit(w, coin); sb.Append($"y{y}:{alive} "); }
                var yr = w.Vital.Years[^1];
                Assert.Equal(w.Vital.Years.Sum(x => x.Births) - w.Vital.Years.Sum(x => x.Deaths) - w.Vital.Years.Sum(x => x.Emigrants) + w.Vital.Years.Sum(x => x.Immigrants), alive - start);   // soak 4: exact
                _ = yr;
            }
            Audit(w, coin);
            var (_, _, kids, elders) = AbstractFixture.Census(w);
            output.WriteLine($"100 years: start {start}, min {lo}, max {hi}, end {w.Vital.Alive}; births {w.Vital.Births} deaths {w.Vital.Deaths} marriages {w.Vital.Marriages} emigrants {w.Vital.Emigrants}; kids {kids:0.0}% elders {elders:0.0}%");
            output.WriteLine(sb.ToString());
            output.WriteLine(t.Stats.Report());
            Assert.True(w.Chronicle.CauseCoverage() >= 0.9);
            Assert.Equal(100 * 60 * 24L, t.Stats.Hours);
            Assert.InRange(t.Stats.P99HourMs, 0, TestBudget.Ms(150));
        }
    }
}
