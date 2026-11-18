using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Abstract;

/// <summary>The hourly budget of the abstract tick (overview section 6, abstract spec G-A10) on the seed-1234 region: about 26,000 people, 5,150 households.</summary>
[Collection(TimingCollection.Name)] // wall-clock budgets: run alone (Phase 3 cleanup)
public class AbstractPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void HourlyTickBudget_MedianAndP99AndBarrierApply()
    {
        var w = AbstractFixture.NewWorld();
        using var t = new WorldTicker(w, AbstractFixture.Registry());
        t.AdvanceDays(3);                                                                  // warm up the JIT, then measure two game months
        Ravensguard.Sim.Diagnostics.GcSettle.Run();                                        // from a settled heap, as the game runs after a load
        t.Stats.Reset();
        t.AdvanceDays(60);
        var s = t.Stats;
        output.WriteLine($"{w.Vital.Alive} people, {w.Households.Count} households");
        output.WriteLine(s.Report());
        Assert.Equal(1440, s.Hours);
        Assert.InRange(s.MedianHourMs, 0, TestBudget.Ms(10));                              // p50 at most 10 ms
        Assert.InRange(s.P99HourMs, 0, TestBudget.Ms(150));                                // hard cap 150 ms
        Assert.InRange(s.MaxSeasonDayHourMs, 0, TestBudget.Ms(150));                       // the busiest hours of a season's first day
        Assert.InRange(s.P99BarrierApplyMs, 0, TestBudget.Ms(5));                          // applying the outbox at the barrier (p99)
        Assert.True(s.Phase(TickPhase.H4DailySlice).Calls == 1440 && s.Systems.ContainsKey("vital-rolls") && s.Systems.ContainsKey("household-needs"));
    }

    [Fact]
    public void TimingInstrumentation_CanBeSwitchedOff()
    {
        var s = new Ravensguard.Sim.Tests.PopulationRates.Synth(); s.Household(6, (Sex.Male, 30)); var w = s.Build();
        using var t = new WorldTicker(w, Ravensguard.Sim.Tests.PopulationRates.Synth.Registry(), new TickerOptions { RecordTimings = false });
        t.AdvanceDays(2);
        Assert.Equal(0, t.Stats.Hours); Assert.Empty(t.Stats.Systems);
    }
}

/// <summary>The abstract section of the save: byte-identical round trip, and a resumed run that matches an uninterrupted one.</summary>
public class AbstractSaveTests
{
    private static SaveFile Capture(WorldState ws) => WorldSave.Capture(ws, EconFixture.ContentHash, "0.1");

    private static void AssertSameBytes(SaveFile a, SaveFile b)
    {
        Assert.Equal(a.Sections.Select(x => x.Key), b.Sections.Select(x => x.Key));
        foreach (var (k, v) in a.Sections) Assert.True(v.Span.SequenceEqual(b.Sections.First(x => x.Key == k).Value.Span), $"section '{k}' differs");
    }

    [Fact]
    public void AbstractStateRoundTripsByteForByte_AndTheWorldComesBackIdentical()
    {
        var w = AbstractFixture.NewWorld(VitalRates_AllChronicle());
        using var t = new WorldTicker(w, AbstractFixture.Registry());
        t.AdvanceDays(75);                                                                 // births, deaths, weddings, outbreaks, a season change, emigrants
        Assert.True(w.Vital.Births > 0 && w.Vital.Deaths > 0 && w.Vital.Marriages > 0);
        var ws = AbstractFixture.AsWorldState(w, GameTime.FromGameHours(w.State.HourDone));
        var file = Capture(ws);
        Assert.Contains(file.Sections, s => s.Key == WorldSave.AbstractSection);
        var back = WorldSave.Restore(file, EconFixture.Content, EconFixture.ContentHash);
        AssertSameBytes(file, Capture(back));
        var w2 = SimWorld.From(back, AbstractFixture.Names(back.Population));
        Assert.Equal(w.ComputeHash(), w2.ComputeHash());
        Assert.Equal(w.State.HourDone, back.Abstract!.HourDone);
        Assert.Equal(w.Vital.Alive, w2.Vital.Alive); Assert.Equal(w.Vital.Births, w2.Vital.Births);
        Assert.Equal(w.Chronicle.Count, back.Chronicle.Count);
        Assert.True(back.Economy!.Ledger.Audit().IsConserved);
    }

    private static Ravensguard.Sim.People.Vital.VitalRates VitalRates_AllChronicle() => Ravensguard.Sim.People.Vital.VitalRates.Default with { ChronicleEveryEvent = true };

    [Fact]
    public void ARunResumedFromASaveEqualsTheUninterruptedRun()
    {
        var a = AbstractFixture.NewWorld();
        using var ta = new WorldTicker(a, AbstractFixture.Registry());
        ta.AdvanceDays(40);
        var ws = AbstractFixture.AsWorldState(a, GameTime.FromGameHours(a.State.HourDone));
        var back = WorldSave.Restore(Capture(ws), EconFixture.Content, EconFixture.ContentHash);
        var b = SimWorld.From(back, AbstractFixture.Names(back.Population));
        using var tb = new WorldTicker(b, AbstractFixture.Registry());
        ta.AdvanceDays(25); tb.AdvanceDays(25);
        Assert.Equal(a.ComputeHash(), b.ComputeHash());
        Assert.Equal(a.People.Count, b.People.Count);
        Assert.Equal(a.Households.Count, b.Households.Count);
    }

    [Fact]
    public void ASaveWithoutTheAbstractSectionStillLoads_AndStartsFreshWithPregnancies()
    {
        var w = AbstractFixture.NewWorld();                                                // not ticked: no state worth saving
        var ws = AbstractFixture.AsWorldState(w, GameTime.FromGameHours(w.State.HourDone));
        ws.Abstract = null;
        var file = Capture(ws);
        Assert.DoesNotContain(file.Sections, s => s.Key == WorldSave.AbstractSection);
        var back = WorldSave.Restore(file, EconFixture.Content, EconFixture.ContentHash);
        Assert.Null(back.Abstract);
        var sw = SimWorld.From(back, AbstractFixture.Names(back.Population));
        Assert.NotNull(back.Abstract);
        Assert.True(Enumerable.Range(0, sw.People.Count).Count(i => sw.People.PregnantUntilDay[i] >= 0) > 300);   // first takeover seeds the pregnancies mid-history
        Assert.Equal(sw.People.Count, sw.Vital.Alive);
    }

    [Fact]
    public void GeneratedPopulationAndEconomyHashesAreUnchangedByTheNewColumns()
    {
        // the golden hashes cover generation only: a freshly cloned population hashes exactly like the one the populator made
        Assert.Equal(EconFixture.Pop.ComputeHash(), EconFixture.Pop.Clone().ComputeHash());
        var e1 = Ravensguard.Sim.Economy.EconomyState.Create(EconFixture.Pop, EconFixture.Content, EconFixture.Seed);
        var e2 = Ravensguard.Sim.Economy.EconomyState.Create(EconFixture.Pop.Clone(), EconFixture.Content, EconFixture.Seed);
        Assert.Equal(e1.ComputeHash(), e2.ComputeHash());
    }
}
