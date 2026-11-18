using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Trade;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.Weather;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Autopilot;

/// <summary>Autopilot v1 (docs/specs/phase3/autopilot-v1.md section 7): the pure rules first (limits, risk, nightfall, the interrupt table), then the autopilot on the real headless world.</summary>
public class AutopilotRulesTests
{
    private static AutopilotState Row() => new() { Person = EntityId.Make(IdKind.Person, 7) };

    // ------------------------------------------------------------------ AP-2, AP-3: spending

    [Fact]
    public void AP2_SpendCap_300aDay_FivePurchasesOf100_FirstThreeFit_TheRestDoNot_AndTheAllowanceResetsAtMidnight()
    {
        var st = Row(); var l = new LimitSet { SpendCapMitesPerDay = 300 };
        var results = new List<LimitVerdict>();
        for (int i = 0; i < 5; i++) { var v = LimitGuard.CheckSpend(l, st, 10, 10_000, 100); results.Add(v); if (v == LimitVerdict.Allowed) LimitGuard.Commit(st, 10, 100); }
        Assert.Equal([LimitVerdict.Allowed, LimitVerdict.Allowed, LimitVerdict.Allowed, LimitVerdict.OverDailyCap, LimitVerdict.OverDailyCap], results);
        Assert.Equal(0, LimitGuard.AllowanceLeft(l, st, 10));
        Assert.Equal(LimitVerdict.Allowed, LimitGuard.CheckSpend(l, st, 11, 10_000, 100));       // 00:00: a new day's allowance, nothing carries over
        Assert.Equal(300, LimitGuard.AllowanceLeft(l, st, 11));
        // planning removes Shop when the day's allowance is gone
        var p = new ActionPolicy(); LimitGuard.Filter(l, p, spendLeft: false); Assert.True(p.Forbids(ActionKind.Shop)); Assert.False(p.Forbids(ActionKind.Eat));
    }

    [Fact]
    public void AP3_Reserve_PurseOf500_ReserveOf400_NothingThatLeavesLessThan400()
    {
        var st = Row(); var l = new LimitSet { SpendCapMitesPerDay = -1, PurseReserveMites = 400 };
        Assert.Equal(LimitVerdict.Allowed, LimitGuard.CheckSpend(l, st, 1, 500, 100));
        Assert.Equal(LimitVerdict.BelowReserve, LimitGuard.CheckSpend(l, st, 1, 500, 101));
        Assert.Equal(LimitVerdict.CannotAfford, LimitGuard.CheckSpend(l, st, 1, 50, 100));
        var total = new LimitSet { SpendCapMitesPerDay = -1, SpendCapTotalMites = 250 };
        LimitGuard.Commit(st, 1, 200); Assert.Equal(LimitVerdict.OverTotalCap, LimitGuard.CheckSpend(total, st, 2, 9999, 100));
    }

    // ------------------------------------------------------------------ AP-5: risk

    [Fact]
    public void AP5_Risk_TwoRoutes450And300_MaxRisk400_TheQuietOneWins_BothAbove_Refused_AtNightTheDoubledRouteIsRefusedToo()
    {
        var l = new LimitSet { MaxRiskPm = 400 };
        var a = new LimitGuard.RouteOption("a", [100, 450, 200]); var b = new LimitGuard.RouteOption("b", [150, 300, 100]);
        Assert.Equal("b", LimitGuard.ChooseRoute(l, [a, b], night: false)!.Name);
        Assert.Null(LimitGuard.ChooseRoute(l, [a], night: false));                       // 450 anywhere along it: refused (LimitReached)
        Assert.Null(LimitGuard.ChooseRoute(l, [b], night: true));                        // 300 x 2 = 600 > 400
        Assert.NotNull(LimitGuard.ChooseRoute(l, [b], night: true, escorted: true, escortCutPm: 400));   // an escort cuts the risk by 40% (600 -> 360)
        Assert.Null(LimitGuard.ChooseRoute(new LimitSet { MaxRiskPm = 200 }, [a, b], night: false));       // both above the cap: the plan is refused
        // the presets of the content
        var s = AutopilotContent.Default.Settings; Assert.Equal((200, 500, 800), (s.RiskLowPm, s.RiskMediumPm, s.RiskHighPm));
    }

    // ------------------------------------------------------------------ AP-6: nightfall

    [Fact]
    public void AP6_HomeByNightfall_2kmEachWay_AtTheJog_Is7p41Hours_Leave0830InMidwinter_Yes_0900_No_Midsummer1100_Yes()
    {
        long rt = LimitGuard.RoundTripSec(2000);
        Assert.Equal(26_667, rt);                                                                // 7.41 h: 3.70 h each way, rounded up to the second
        int winter = 53, summer = 23, lat = 480;
        Assert.InRange(Daylight.SunsetMinute(lat, winter), 15 * 60 + 55, 16 * 60 + 1);           // sunset 15:58
        Assert.InRange(Daylight.SunsetMinute(lat, summer), 20 * 60 - 3, 20 * 60 + 5);            // sunset 20:01
        Assert.True(LimitGuard.HomeByNightfallOk(lat, winter, 8 * 60 + 30, rt));                // back 15:55
        Assert.False(LimitGuard.HomeByNightfallOk(lat, winter, 9 * 60, rt));
        Assert.True(LimitGuard.HomeByNightfallOk(lat, summer, 11 * 60, rt));
        Assert.True(LimitGuard.HeadHomeNow(lat, winter, Daylight.SunsetMinute(lat, winter) - 60));
        Assert.False(LimitGuard.HeadHomeNow(lat, winter, Daylight.SunsetMinute(lat, winter) - 61));
    }

    // ------------------------------------------------------------------ AP-7: every interrupt kind, three policies

    [Fact]
    public void AP7_TheListHas23Kinds_EachRaisesOnce_HonoursPauseNotifyAi_AndWritesALogLine()
    {
        var kinds = Enum.GetValues<InterruptKind>();
        Assert.Equal(23, kinds.Length); Assert.Equal(InterruptBus.KindCount, kinds.Length);
        foreach (var kind in kinds)
            foreach (var pol in new[] { InterruptPolicy.Pause, InterruptPolicy.Notify, InterruptPolicy.Ai })
            {
                var log = new AutopilotLog(); var router = new InterruptRouter(log); var st = Row(); st.Enabled = true; var bus = new InterruptBus { Autopilot = true };
                st.Policy[(int)kind] = pol; if (kind == InterruptKind.BigChoice && pol == InterruptPolicy.Notify) { }
                var r = router.Raise(st, bus, kind, 1000, 5, 6, "test");
                var eff = kind == InterruptKind.BigChoice && pol == InterruptPolicy.Notify ? InterruptPolicy.Pause : pol;     // a big choice cannot be set to Notify
                Assert.False(r.Duplicate); Assert.Equal(eff, r.Policy);
                Assert.Equal(1, log.CountInterrupts(kind)); Assert.Equal(1, st.Today.Interrupts);
                Assert.Equal(eff == InterruptPolicy.Pause, bus.Paused);
                Assert.Equal(eff == InterruptPolicy.Notify, r.Toast);
                if (eff == InterruptPolicy.Ai) Assert.NotEqual(AiResponse.None, r.Response); else Assert.Equal(AiResponse.None, r.Response);
                // the same kind again at once is one event
                Assert.True(router.Raise(st, bus, kind, 1100, 5, 6, "again").Duplicate); Assert.Equal(1, log.CountInterrupts(kind));
                // and a quarter hour later it is a new one
                Assert.False(router.Raise(st, bus, kind, 1000 + InterruptRouter.DedupeGameSeconds + 5, 5, 6, "later").Duplicate);
            }
        // a kind the build does not know is Pause
        var s2 = Row(); Assert.Equal(InterruptPolicy.Pause, InterruptRouter.PolicyOf(s2, (InterruptKind)200));
        // Ai in manual play is refused by the bus; Attacked is never quiet
        var b = new InterruptBus(); Assert.False(b.Set(InterruptKind.Hunger, InterruptPolicy.Ai)); Assert.False(b.Set(InterruptKind.Attacked, InterruptPolicy.Notify, quiet: true));
        // the defaults are the table of the spec
        foreach (var k in new[] { InterruptKind.Hostile, InterruptKind.Attacked, InterruptKind.Fatigue, InterruptKind.Horse, InterruptKind.CartStuck, InterruptKind.BlockedPath, InterruptKind.Arrived, InterruptKind.Contract, InterruptKind.Threatened, InterruptKind.Injured, InterruptKind.BigChoice, InterruptKind.LimitReached, InterruptKind.Stuck })
            Assert.Equal(InterruptPolicy.Pause, InterruptBus.DefaultPolicy(k));
        foreach (var k in new[] { InterruptKind.Weather, InterruptKind.Cold, InterruptKind.Heat, InterruptKind.Hunger, InterruptKind.Dusk, InterruptKind.Companion, InterruptKind.Found, InterruptKind.LowMoney, InterruptKind.DirectiveDone, InterruptKind.NeedMoney })
            Assert.Equal(InterruptPolicy.Notify, InterruptBus.DefaultPolicy(k));
    }

    [Fact]
    public void AP7_TravelRaisesItsFifteenKindsFromTheRealConditions_AndTheRouterSeesEachOne()
    {
        var goods = new Ravensguard.Sim.Goods.GoodCatalog(EconFixture.Content);
        var t = new TravelState(EconFixture.Content, 1, goods, new Ravensguard.Sim.Goods.MassLedger(goods.Count));
        var st = Row(); st.Enabled = true; for (int i = 0; i < st.Policy.Length; i++) st.Policy[i] = InterruptPolicy.Ai;
        var log = new AutopilotLog(); var router = new InterruptRouter(log); t.Bus.Autopilot = true;
        for (int i = 0; i < InterruptBus.KindCount; i++) t.Bus.Set((InterruptKind)i, InterruptPolicy.Ai);
        var s = FlatWorld.Default(); s.Weather = WeatherState.Thunderstorm; s.Daytime = true;
        var h = TravelStatus.Calm(); h.HostileM = 40; h.HostileName = "Wolf"; h.Attacked = true; h.ColdStressPm = 500; h.HeatStressPm = 500; h.HoursSinceMeal = 14; h.FoodLow = true; h.FatiguePm = 900;
        h.HorseTired = true; h.CartStuck = true; h.CompanionTrouble = true; h.FoundWithinM = 10;
        t.Evaluate(s, h, 100_000, 0, 0);
        var seen = t.Bus.Log.Select(r => r.Kind).Distinct().ToHashSet();
        foreach (var k in new[] { InterruptKind.Hostile, InterruptKind.Attacked, InterruptKind.Weather, InterruptKind.Cold, InterruptKind.Heat, InterruptKind.Hunger, InterruptKind.Fatigue, InterruptKind.Horse, InterruptKind.CartStuck, InterruptKind.Companion, InterruptKind.Found })
            Assert.Contains(k, seen);
        foreach (var rec in t.Bus.Log) Assert.NotEqual(AiResponse.None, router.Observe(st, rec));
        Assert.Equal(t.Bus.Log.Count, log.Count(LogCode.Interrupt));
        // dusk, arrival, a blocked path and a contract come from their own conditions
        t.Bus.Edge(InterruptKind.Dusk, true, 1, 0, 0); t.Bus.Raise(InterruptKind.Arrived, 2, 0, 0); t.Bus.Raise(InterruptKind.BlockedPath, 3, 0, 0); t.Bus.Raise(InterruptKind.Contract, 4, 0, 0);
        Assert.Equal(15, t.Bus.Log.Select(r => r.Kind).Distinct().Count());
    }

    [Fact]
    public void AP8_ABigChoiceWithoutADeclineBranchFailsContentValidation()
    {
        Assert.Empty(AutopilotContentChecks.BigChoiceProblems([new("marry", true, true), new("hello", false, false)]));
        var p = AutopilotContentChecks.BigChoiceProblems([new("swear_fealty", true, false)]).ToList(); Assert.Single(p); Assert.Contains("swear_fealty", p[0]);
    }

    [Fact]
    public void TheContentLoads_AndTheReservedStreamIsNeverDrawn()
    {
        var c = AutopilotContent.Default;
        Assert.Equal(500, c.Settings.DefaultMaxRiskPm); Assert.Equal(300, c.Settings.DefaultSpendCapMitesPerDay); Assert.Equal(20, c.Settings.StuckGameHours); Assert.Equal(2000, c.Settings.LogLinesMax);
        Assert.True(c.Activities.Knows(Ravensguard.Sim.People.Skill.Smithing));
        Assert.Contains(c.Activities.UsesFor(Ravensguard.Sim.People.Skill.Smithing, 10), u => u.Recipe == "practice_billet" && u.CapLevel == 30 && u.DX100 == 45);
        Assert.False(c.Activities.CanPractise(Ravensguard.Sim.People.Skill.Smithing, 30));           // above 30: real work
        // content in the loaded database agrees with the embedded default
        Assert.Equal(c.Settings.SummaryHour, AutopilotContent.From(EconFixture.Content).Settings.SummaryHour);
        // stream 0x380 is reserved: no autopilot source draws it
        var dir = Path.GetDirectoryName(typeof(AutopilotRulesTests).Assembly.Location)!;
        string root = dir; while (root != null && !Directory.Exists(Path.Combine(root, "src", "Ravensguard.Sim", "Autopilot"))) root = Path.GetDirectoryName(root)!;
        foreach (var f in Directory.GetFiles(Path.Combine(root!, "src", "Ravensguard.Sim", "Autopilot"), "*.cs")) Assert.DoesNotContain("AutopilotReserved", File.ReadAllText(f), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePolicyMask_RemovesActions_AndAddsBias_WithNoEffectWhenNeutral()
    {
        var brain = UtilityBrain.Default;
        var n = new NeedInputs { HungerMilliHours = 15_000, FatiguePm = 100 };
        var c = new ChoiceContext { Scheduled = ActionKind.Work, HasBed = true, CanEat = true, WorkOpen = true, Seed = 5, GameSec = 100, Person = 9 };
        var plain = brain.Choose(n, c);
        c.Policy = new ActionPolicy(); Assert.Equal(plain, brain.Choose(n, c));                  // a neutral policy changes nothing
        c.Policy.Forbid(plain.Kind); Assert.NotEqual(plain.Kind, brain.Choose(n, c).Kind);       // a limit removes the action before scoring
        c.Policy.Clear(); c.Policy.Bias(ActionKind.Idle, 5000); Assert.Equal(ActionKind.Idle, brain.Choose(n, c).Kind);
    }
}

/// <summary>AP-17 (spec 9): the limit check is cheap and allocation-free, and thirteen persons on autopilot cost little per window.</summary>
[Collection(TimingCollection.Name)]
public class AutopilotBudgetTests(ITestOutputHelper output)
{
    [Fact]
    public void AP17_TheLimitGuard_Takes5MicrosecondsOrLess_AndAllocatesNothing()
    {
        var st = new AutopilotState { Person = EntityId.Make(IdKind.Person, 3) }; var l = new LimitSet { SpendCapMitesPerDay = 1_000_000, PurseReserveMites = 10 };
        for (int i = 0; i < 1000; i++) LimitGuard.CheckSpend(l, st, 5, 50_000, 7);
        var sw = System.Diagnostics.Stopwatch.StartNew(); long before = GC.GetAllocatedBytesForCurrentThread(); int n = 2_000_000; long acc = 0;
        for (int i = 0; i < n; i++) acc += (long)LimitGuard.CheckSpend(l, st, 5, 50_000, 7 + i % 3) + (LimitGuard.RiskOk(l, 300, false) ? 1 : 0);
        long alloc = GC.GetAllocatedBytesForCurrentThread() - before; double us = sw.Elapsed.TotalMilliseconds * 1000 / n;
        output.WriteLine($"LimitGuard {us:0.000} us per check, {alloc} bytes allocated ({acc})");
        Assert.True(us <= 5, $"{us} us"); Assert.Equal(0, alloc);
    }

    [Fact]
    public void AP17_ThirteenPersonsOnAutopilot_CostUnder2MillisecondsEachForAGameDay_OfPlanning_AndLittlePerWindow()
    {
        using var w = ApWorld.Soak();
        var pop = w.State.Population; var p = pop.People; int day = (int)(w.Sec / 86400);
        var picks = Enumerable.Range(0, p.Count).Where(i => p.IsAlive(i) && p.AgeYears(i, day) is >= 18 and < 50 && p.Household[i] >= 0).Take(12).ToList();
        foreach (int i in picks) { w.Ctx.Party.Squad.Members.Add(new Ravensguard.Sim.Trade.Follower { Person = p.IdOf(i), PersonIndex = i, Name = "m" + i, State = Ravensguard.Sim.Trade.HireState.Active, MaxSkill = 20 }); w.Ap.Enable(p.IdOf(i), w.Sec, Directive.Work()); }
        w.Ap.Enable(w.Hero, w.Sec, Directive.Work());
        Assert.Equal(13, w.Book.States.Count);
        w.Ap.Advance(w.Sec + 600);                                                         // warm up
        long t0 = w.Sec + 600; var sw = System.Diagnostics.Stopwatch.StartNew();
        w.Ap.Jobs = () => null; w.Ap.DrivesJobs = false;                                  // the table's own cost is not the autopilot's
        w.Ap.Advance(t0 + 86400);
        double msPerPersonDay = sw.Elapsed.TotalMilliseconds / 13, usPerWindow = sw.Elapsed.TotalMilliseconds * 1000 / (13.0 * 4320);
        output.WriteLine($"13 persons, one game day: {msPerPersonDay:0.0} ms per person-day, {usPerWindow:0.0} us per person-window (20 game seconds); per sim tick at 1x: {usPerWindow * 13 / 20:0.0} us");
        Assert.True(usPerWindow * 13 / 20 <= 100, "the autopilot's share of a tick at 1x for 13 persons");
        Assert.True(msPerPersonDay <= 400);
    }
}
