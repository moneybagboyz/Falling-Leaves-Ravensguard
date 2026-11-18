using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Tests.PopulationRates;

namespace Ravensguard.Sim.Tests.Abstract;

/// <summary>A recording system: logs "hour:phase:name" for every call and can run an action.</summary>
internal sealed class Probe(string name, TickPhase phases, int order, List<string> log, Action<TickContext>? act = null) : AbstractSystem(name, phases, order)
{
    public override void Step(TickContext ctx) { log.Add($"{ctx.Hour}:{ctx.Phase}:{Name}"); act?.Invoke(ctx); }
}

[AbstractSystem]
public sealed class DiscoveredTestSystem() : AbstractSystem("test-discovered", TickPhase.H7Outbox, SystemOrder.Outbox + 5)
{
    public override void Step(TickContext ctx) { }
}

/// <summary>Phase order, system order, the slice rule and the seasonal/daily gating of the hour step (abstract spec 3.2, overview section 4).</summary>
public class TickOrderingTests
{
    private static SimWorld Tiny() { var s = new Synth(); s.Household(6, (Sex.Male, 30), (Sex.Female, 28)); return s.Build(); }

    private static readonly TickPhase[] AllPhases = [TickPhase.H0Environment, TickPhase.H1Caravans, TickPhase.H2Work, TickPhase.H3Market, TickPhase.H4DailySlice, TickPhase.H5DailyGlobals, TickPhase.H6Seasonal, TickPhase.H7Outbox];

    private static AbstractSystemRegistry ProbeAll(List<string> log)
    {
        var r = new AbstractSystemRegistry();
        foreach (var ph in AllPhases) r.Add(new Probe("p-" + ph, ph, 100, log));
        return r;
    }

    [Fact]
    public void PhasesRunInTheSpecOrderEveryHour_AndH5OnlyAtHourZero()
    {
        var log = new List<string>(); var w = Tiny();
        using var t = new WorldTicker(w, ProbeAll(log));
        long start = w.State.HourDone;
        t.AdvanceHours(24);                                                 // hours 1..24 of the start day: hour 24 is hour 0 of the next day
        for (long h = start + 1; h <= start + 24; h++)
        {
            var phases = log.Where(l => l.StartsWith(h + ":")).Select(l => Enum.Parse<TickPhase>(l.Split(':')[1])).ToList();
            Assert.Equal(phases.OrderBy(p => p).ToList(), phases);                                       // ascending phase order
            Assert.Equal(h % 24 == 0, phases.Contains(TickPhase.H5DailyGlobals));
            Assert.True(phases.Contains(TickPhase.H6Seasonal) == (h < start + 24));                      // the start day is a season's first day (day of year 1); the next is not
            foreach (var p in new[] { TickPhase.H0Environment, TickPhase.H1Caravans, TickPhase.H2Work, TickPhase.H3Market, TickPhase.H4DailySlice, TickPhase.H7Outbox }) Assert.Contains(p, phases);
        }
    }

    [Fact]
    public void SeasonalPhaseRunsEveryHourOfFourDaysAYear_DailyGlobalsOncePerDay()
    {
        var log = new List<string>(); var w = Tiny();
        using var t = new WorldTicker(w, ProbeAll(log));
        t.AdvanceDays(60);
        Assert.Equal(96, log.Count(l => l.Contains(":H6Seasonal:")));       // 23 hours left of day 1, day 16, 31, 46 (24 each) and hour 0 of the next year's day 1
        Assert.Equal(60, log.Count(l => l.Contains(":H5DailyGlobals:")));
        Assert.Equal(1440, log.Count(l => l.Contains(":H4DailySlice:")));
    }

    [Fact]
    public void SystemsOfAPhaseRunByAscendingOrderThenName_WhateverTheRegistrationOrder()
    {
        string Run(params (string Name, int Order)[] regs)
        {
            var log = new List<string>(); var r = new AbstractSystemRegistry();
            foreach (var (n, o) in regs) r.Add(new Probe(n, TickPhase.H4DailySlice, o, log));
            using var t = new WorldTicker(Tiny(), r);
            t.AdvanceHours(1);
            return string.Join(",", log.Select(l => l.Split(':')[2]));
        }
        string a = Run(("c", 300), ("b", 100), ("a", 100)), b = Run(("a", 100), ("b", 100), ("c", 300)), c = Run(("b", 100), ("c", 300), ("a", 100));
        Assert.Equal("a,b,c", a); Assert.Equal(a, b); Assert.Equal(a, c);
    }

    [Fact]
    public void DefaultOrdersFollowTheSpecTables()
    {
        var r = AbstractSystemRegistry.CreateDefault(discover: false);
        var names = r.For(TickPhase.H4DailySlice).Select(s => s.Name).ToList();
        Assert.True(names.IndexOf("household-needs") < names.IndexOf("vital-rolls"), "eat before the vital rolls");
        Assert.True(SystemOrder.Provisioning < SystemOrder.HouseholdNeeds);
        Assert.True(SystemOrder.Production < SystemOrder.Extraction && SystemOrder.Extraction < SystemOrder.FarmTasks && SystemOrder.FarmTasks < SystemOrder.Markets);
        int[] s = [SystemOrder.S1Farming, SystemOrder.S2Grazing, SystemOrder.S3Extraction, SystemOrder.S4Taxes, SystemOrder.S5MintPrices, SystemOrder.S6Population, SystemOrder.S7Migration, SystemOrder.S8Caravans, SystemOrder.S9Summary];
        Assert.Equal(s.OrderBy(x => x), s);
        Assert.Equal(["population-seasonal", "population-migration"], r.For(TickPhase.H6Seasonal).Select(x => x.Name));   // S6 before S7
    }

    [Fact]
    public void RegistryRefusesDuplicatesAndPhaselessSystems_AndFreezesWhenATickerUsesIt()
    {
        var r = new AbstractSystemRegistry(); var log = new List<string>();
        r.Add(new Probe("x", TickPhase.H0Environment, 1, log));
        Assert.Throws<ArgumentException>(() => r.Add(new Probe("x", TickPhase.H1Caravans, 2, log)));
        Assert.Throws<ArgumentException>(() => r.Add(new Probe("y", TickPhase.None, 2, log)));
        using var t = new WorldTicker(Tiny(), r);
        Assert.Throws<InvalidOperationException>(() => r.Add(new Probe("z", TickPhase.H1Caravans, 2, log)));
    }

    [Fact]
    public void AttributedSystemsAreFoundByReflection_AndExplicitLinesWork_WithTheirOwnOrder()
    {
        var r = AbstractSystemRegistry.CreateDefault(discover: false);
        Assert.Null(r.Find("test-discovered"));
        int n = r.Discover(typeof(DiscoveredTestSystem).Assembly);
        Assert.True(n >= 1);
        Assert.NotNull(r.Find("test-discovered"));
        Assert.Equal(0, r.Discover(typeof(DiscoveredTestSystem).Assembly));                           // idempotent by name
        Assert.Equal("test-discovered", r.For(TickPhase.H7Outbox).Last().Name);
    }

    [Fact] // G-A1
    public void G_A1_SliceRule_RunsEachRecordOnceADayAtIndexModTwentyFour()
    {
        var slices = new Dictionary<int, List<int>> { [4825] = [], [713] = [] };
        var r = new AbstractSystemRegistry();
        r.Add(new Probe("slice", TickPhase.H4DailySlice, 1, [], c => { foreach (var (idx, l) in slices) if (c.IsSlice(idx)) l.Add(c.HourOfDay); }));
        using var t = new WorldTicker(Tiny(), r);
        t.AdvanceDays(3);
        Assert.Equal(4825 % 24, 1); Assert.Equal(713 % 24, 17);
        Assert.Equal([1, 1, 1], slices[4825]); Assert.Equal([17, 17, 17], slices[713]);               // household 4,825 at 01:00, business 713 at 17:00
    }

    [Fact]
    public void SeasonSliceFiresOncePerSeasonForEachSettlement()
    {
        int hits = 0; var hours = new List<int>();
        var r = new AbstractSystemRegistry();
        r.Add(new Probe("season", TickPhase.H6Seasonal, 1, [], c => { if (c.IsSeasonSlice(30)) { hits++; hours.Add(c.HourOfDay); } }));
        using var t = new WorldTicker(Tiny(), r);
        t.AdvanceDays(60);
        Assert.Equal(4, hits); Assert.All(hours, h => Assert.Equal(30 % 24, h));
    }
}

/// <summary>Barrier semantics: when commands run, when results become visible, the replay (G-A6, G-A7, G-A8).</summary>
public class BarrierTests
{
    private static (SimWorld W, Synth S) Tiny() { var s = new Synth(); s.Household(6, (Sex.Male, 30), (Sex.Female, 28)); s.Household(6, (Sex.Male, 31), (Sex.Female, 29)); return (s.Build(), s); }

    [Fact] // G-A6
    public void G_A6_CommandsRunInFrameThenSeqOrder_WhateverTheEnqueueOrder_AndBeforeTheHourStep()
    {
        var (w, _) = Tiny(); var log = new List<string>();
        var r = new AbstractSystemRegistry(); r.Add(new Probe("step", TickPhase.H4DailySlice, 1, log));
        using var t = new WorldTicker(w, r);
        t.Handlers.Register(CommandKind.Buy, (in EconCommand c, CommandContext ctx) => { log.Add($"cmd {c.Frame}/{c.Seq}@{ctx.TickKey}"); return CommandResult.Success(ctx.TickKey); });
        t.Commands.Enqueue(new EconCommand(11, 1, CommandKind.Buy, EntityId.None, EntityId.None));
        t.Commands.Enqueue(new EconCommand(10, 2, CommandKind.Buy, EntityId.None, EntityId.None));
        t.Commands.Enqueue(new EconCommand(10, 1, CommandKind.Buy, EntityId.None, EntityId.None));
        long h = w.State.HourDone + 1;
        t.AdvanceHours(1);
        Assert.Equal([$"cmd 10/1@{h}", $"cmd 10/2@{h}", $"cmd 11/1@{h}"], log.Take(3));
        Assert.Contains($"{h}:H4DailySlice:step", log[3]);                  // the step comes after the barrier's commands
        t.AdvanceHours(1);
        Assert.Equal(4, log.Count(l => !l.StartsWith("cmd")) + 3);          // a later hour has no old commands
        Assert.Equal(3, t.CommandLog.Count);
    }

    [Fact]
    public void ACommandWithNoHandlerIsRefusedAndLogged_NotLost()
    {
        var (w, _) = Tiny();
        using var t = new WorldTicker(w, Synth.Registry());
        t.Commands.Enqueue(1, CommandKind.Sell, EntityId.None, EntityId.None);
        t.AdvanceHours(1);
        Assert.Equal(CommandError.NoHandler, t.CommandLog.Single().Result.Error);
    }

    [Fact]
    public void ChronicleEntriesAndEventsOfTheStepAppearOnlyAtTheBarrier_InDraftOrder()
    {
        var (w, _) = Tiny(); var seen = new List<int>(); var delivered = new List<string>();
        var r = new AbstractSystemRegistry();
        r.Add(new Probe("draft", TickPhase.H4DailySlice, 1, [], c =>
        {
            c.Outbox.Chronicle(c.Time, "a", [], [new Cause(CauseKind.Weather)]); var second = c.Outbox.Chronicle(c.Time, "b", [], [new Cause(CauseKind.Weather, null, "after a")]);
            c.Outbox.Publish("hello " + second.Counter); c.Outbox.Count("things", 2);
            seen.Add(c.World.Chronicle.Count);                              // not visible yet
        }));
        using var t = new WorldTicker(w, r);
        t.Events.Subscribe<string>(s => delivered.Add(s));
        t.AdvanceHours(2);
        Assert.Equal([0, 2], seen);
        Assert.Equal(4, w.Chronicle.Count);
        Assert.Equal(["a", "b", "a", "b"], w.Chronicle.All.Select(e => e.TypeId));
        Assert.Equal(2, delivered.Count);
        Assert.Equal(4, t.Counters["things"]);
        Assert.Equal(w.State.HourDone, t.Snapshot.Version);
    }

    [Fact]
    public void PayCoinMovesCoinThroughTheLedger_AndRefusesWhatTheSenderCannotAfford()
    {
        var (w, _) = Tiny();
        var e = w.Economy; var hero = e.SeedHero(100);                      // 100 crowns, the hero's purse
        using var t = new WorldTicker(w, Synth.Registry());
        var before = e.Ledger.Audit();
        t.Commands.Enqueue(1, CommandKind.PayCoin, EconomyState.HeroOwner, w.Households.IdOf(0), 2_500);
        t.Commands.Enqueue(2, CommandKind.PayCoin, EconomyState.HeroOwner, w.Households.IdOf(1), 999_999);
        t.AdvanceHours(1);
        Assert.True(t.CommandLog[0].Result.Ok); Assert.Equal(CommandError.Insufficient, t.CommandLog[1].Result.Error);
        Assert.Equal(2_500, e.HouseholdCoin(0).Mites); Assert.Equal(10_000 - 2_500, e.Ledger.Balance(hero).Mites);
        Assert.True(e.Ledger.Audit().IsConserved); Assert.Equal(before.InPurses, e.Ledger.Audit().InPurses);
    }

    [Fact]
    public void OutboxDeliversEventsOnlyWhenTheBarrierRuns_NotAtDraftTime()
    {
        var (w, _) = Tiny(); int got = 0, atDraft = -1;
        var r = new AbstractSystemRegistry();
        r.Add(new Probe("p", TickPhase.H7Outbox, 1, [], c => { c.Outbox.Publish(42); atDraft = got; }));
        using var t = new WorldTicker(w, r);
        t.Events.Subscribe<int>(i => got += i);
        t.AdvanceHours(1);
        Assert.Equal((0, 42), (atDraft, got));
    }

    // ---- replay and delayed worker (G-A7)

    private static string[] DayHashes(Func<int, SimWorld, WorldTicker, GameTime> day, int days, Func<(SimWorld, WorldTicker)> make)
    {
        var (w, t) = make();
        using (t)
        {
            var hashes = new List<string>();
            for (int d = 0; d < days; d++) { day(d, w, t); hashes.Add(w.ComputeHash()); }
            return [.. hashes];
        }
    }

    private static (SimWorld, WorldTicker) HeroWorld(TickerOptions? opt = null)
    {
        var w = AbstractFixture.NewWorld();
        w.Economy.SeedHero(50);
        var t = new WorldTicker(w, AbstractFixture.Registry(), opt);
        return (w, t);
    }

    private static void Script(int day, WorldTicker t, SimWorld w)
    {
        // forty hero payments over thirty days, some of them larger than the purse can afford
        if (day % 3 == 0 || day % 7 == 0) t.Commands.Enqueue(day * 100, CommandKind.PayCoin, EconomyState.HeroOwner, w.Households.IdOf((day * 37) % w.Households.Count), 120 + day * 11);
        if (day % 5 == 0) t.Commands.Enqueue(day * 100 + 1, CommandKind.PayCoin, w.Households.IdOf(day % 50), EconomyState.HeroOwner, 40);
    }

    [Fact] // G-A7
    public void G_A7_ReplayOfThirtyDaysWithScriptedTrades_ReproducesTheHashEveryDay_EvenWithADelayedWorker()
    {
        var (wA, tA) = HeroWorld();
        string[] a = DayHashes((d, w, t) => { Script(d, t, w); t.AdvanceHours(24); return GameTime.FromGameHours(w.State.HourDone); }, 30, () => (wA, tA));
        Assert.True(tA.CommandLog.Count >= 15);
        Assert.Contains(tA.CommandLog, c => !c.Result.Ok);                                     // the script includes an unaffordable payment
        var log = tA.CommandLog.ToList();

        var (wB, tB) = HeroWorld();
        tB.ScheduleReplay(log);
        string[] b = DayHashes((d, w, t) => { t.AdvanceHours(24); return default; }, 30, () => (wB, tB));
        Assert.Equal(a, b);
        Assert.Equal(log.Select(c => (c.TickKey, c.Command, c.Result)), tB.CommandLog.Select(c => (c.TickKey, c.Command, c.Result)));

        var (wC, tC) = HeroWorld(new TickerOptions { WorkerDelayMs = 1 });
        tC.StartWorker();
        string[] c = DayHashes((d, w, t) => { Script(d, t, w); var end = GameTime.FromGameHours(w.State.HourDone + 24); t.WaitIdle(end); return end; }, 30, () => (wC, tC));
        Assert.Equal(a, c);
    }

    [Fact] // G-A8
    public void G_A8_ASnapshotReaderNeverSeesAMixOfTwoVersions_AndTheVersionOnlyGrows()
    {
        var (w, _) = Tiny();
        using var t = new WorldTicker(w, Synth.Registry(), new TickerOptions { WorkerDelayMs = 1 });
        t.StartWorker();
        long last = -1; bool torn = false, backwards = false; var stop = false;
        var reader = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                var s = t.Snapshot;
                if (s.Version != s.Hour) torn = true;
                if (s.Version < last) backwards = true;
                last = s.Version;
            }
        });
        reader.Start();
        t.WaitIdle(GameTime.FromGameHours(w.State.HourDone + 30));
        Volatile.Write(ref stop, true); reader.Join();
        Assert.False(torn); Assert.False(backwards);
        Assert.Equal(w.State.HourDone, t.Snapshot.Version);
    }
}

/// <summary>The worker thread: same result as synchronous stepping, faults surface, the sync API is refused while it runs.</summary>
public class WorkerTests
{
    [Fact]
    public void WorkerSteppingGivesTheSameWorldAsSynchronousStepping()
    {
        var a = AbstractFixture.NewWorld(); var b = AbstractFixture.NewWorld();
        using var ta = new WorldTicker(a, AbstractFixture.Registry());
        using var tb = new WorldTicker(b, AbstractFixture.Registry());
        ta.AdvanceDays(5);
        tb.StartWorker();
        var target = GameTime.FromGameHours(b.State.HourDone + 5 * 24);
        int pumps = 0;
        while (tb.Pump(target)) { pumps++; Thread.Yield(); }                // the main thread is free between pumps
        Assert.True(pumps > 0);
        Assert.Equal(a.ComputeHash(), b.ComputeHash());
        Assert.Equal(ta.HourDone, tb.HourDone);
    }

    [Fact]
    public void ThePumpDoesNotStartAnHourUntilTheClockHasCrossedIt_AndAppliesWaitingCommandsBetweenHours()
    {
        var s = new Synth(); s.Household(6, (Sex.Male, 30), (Sex.Female, 28)); var w = s.Build();
        using var t = new WorldTicker(w, Synth.Registry());
        t.StartWorker();
        var now = GameTime.FromGameHours(w.State.HourDone);
        Assert.False(t.Pump(now));                                          // nothing owed
        Assert.Equal(now.TotalHours, t.HourDone);
        w.Economy.SeedHero(10);
        t.Commands.Enqueue(1, CommandKind.PayCoin, EconomyState.HeroOwner, w.Households.IdOf(0), 300);
        Assert.False(t.Pump(now));                                          // idle worker: the command runs at once, tick key = the next hour
        Assert.Equal(300, w.Economy.HouseholdCoin(0).Mites);
        Assert.Equal(now.TotalHours + 1, t.CommandLog.Single().TickKey);
    }

    [Fact]
    public void AFaultInASystemSurfacesOnThePump_AndTheSyncApiIsRefusedWhileAWorkerRuns()
    {
        var s = new Synth(); s.Household(6, (Sex.Male, 30)); var w = s.Build();
        var r = new AbstractSystemRegistry();
        r.Add(new Probe("boom", TickPhase.H4DailySlice, 1, [], c => { if (c.Hour % 24 == 3) throw new InvalidOperationException("boom"); }));
        using var t = new WorldTicker(w, r);
        t.StartWorker();
        Assert.Throws<InvalidOperationException>(() => t.AdvanceHours(1));
        var ex = Assert.Throws<InvalidOperationException>(() => t.WaitIdle(GameTime.FromGameHours(w.State.HourDone + 10)));
        Assert.Contains("boom", ex.Message);
    }
}

/// <summary>Same seed, same world, however the hours were batched (overview section 8, abstract spec 5, G-A9).</summary>
public class DeterminismTests
{
    [Fact]
    public void SameSeedSameHashAfterNDays_AndATwoDayStepEqualsFortyEightOneHourSteps()
    {
        string One(Action<WorldTicker> run) { var w = AbstractFixture.NewWorld(); using var t = new WorldTicker(w, AbstractFixture.Registry()); run(t); return w.ComputeHash(); }
        string a = One(t => t.AdvanceHours(24 * 20));
        string b = One(t => { for (int d = 0; d < 20; d++) t.AdvanceDays(1); });
        string c = One(t => { for (int h = 0; h < 24 * 20; h++) t.AdvanceHours(1); });
        string d2 = One(t => t.AdvanceTo(GameTime.FromGameHours((long)EconFixtureStartHour() + 24 * 20)));
        string e = One(t => t.AdvanceDays(20));
        Assert.Equal(a, b); Assert.Equal(a, c); Assert.Equal(a, d2); Assert.Equal(a, e);
    }

    private static long EconFixtureStartHour() => GameTime.FromCalendar(Ravensguard.Sim.Tests.Economy.EconFixture.Pop.NowYear, Season.Spring, 1).TotalHours;

    [Fact] // G-A9
    public void G_A9_AdvanceHours48IsIdenticalToFortyEightSingleStepsAtEveryDayBoundary()
    {
        var w1 = AbstractFixture.NewWorld(); var w2 = AbstractFixture.NewWorld();
        using var t1 = new WorldTicker(w1, AbstractFixture.Registry()); using var t2 = new WorldTicker(w2, AbstractFixture.Registry());
        var day1 = new List<string>(); var day2 = new List<string>();
        for (int d = 0; d < 6; d++)
        {
            t1.AdvanceHours(48); day1.Add(w1.ComputeHash());
            for (int h = 0; h < 48; h++) t2.AdvanceHours(1);
            day2.Add(w2.ComputeHash());
        }
        Assert.Equal(day1, day2);
        Assert.Equal(t1.Counters, t2.Counters);
    }

    [Fact]
    public void TheHashChangesWhenTheWorldDoes_AndDiffersBetweenRegistries()
    {
        var w = AbstractFixture.NewWorld(); using var t = new WorldTicker(w, AbstractFixture.Registry());
        string h0 = w.ComputeHash(); t.AdvanceDays(2); string h1 = w.ComputeHash();
        Assert.NotEqual(h0, h1);
        var w2 = AbstractFixture.NewWorld(); using var t2 = new WorldTicker(w2, AbstractFixture.Registry(yieldPermille: _ => 0));   // no food: a different world
        t2.AdvanceDays(2);
        Assert.NotEqual(h1, w2.ComputeHash());
    }
}
