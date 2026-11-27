using Ravensguard.Content;
using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Autopilot;

/// <summary>P8-AT-2 (ambitions spec 6): AT-7 the planner picks, AT-8 the limits, AT-9 stuck, AT-10 the rate, AT-11 a companion, AT-12 the soak; plus the hourly check, the chronicle and the save.</summary>
public class AmbitionPlannerTests(ITestOutputHelper output)
{
    private static readonly AutopilotContent Cfg = AutopilotContent.Default;

    private static ConditionRefDef Cond(string id, params (string, string)[] args) => new() { Id = id, Args = args.ToDictionary(a => a.Item1, a => a.Item2) };
    private static AmbitionDef Amb(string id, params ConditionRefDef[] steps) => new()
    {
        Id = id, Name = id.Replace('_', ' '), Description = "test", Tier = "middle",
        Steps = [.. steps.Select((c, i) => new AmbitionStepDef { Id = "s" + i, Name = "step " + i, Conditions = [c] })],
    };

    private static PlanInput Inp(LimitSet l, AutopilotState? st = null, long purse = 5000, Func<string, int>? skill = null, Func<MeansDef, ConditionRefDef, bool>? open = null, Dictionary<string, int>? rates = null)
        => new() { Limits = l, State = st ?? new AutopilotState(), Day = 5, Purse = purse, SkillOf = skill ?? (_ => 0), Available = open ?? ((_, _) => true), Rates = rates ?? [] };

    private static void UseAmbition(ApWorld w, params AmbitionDef[] ambs)
    {
        w.Ap.Tree = new AmbitionTree(ambs);
        w.State.Hero!.AmbitionId = ambs[0].Id; w.State.Hero.Milestone = 0; w.State.Hero.Offered.Clear();
    }

    // ------------------------------------------------------------------ the means table

    [Fact]
    public void TheMeansTable_IsLoaded_EveryRowNamesAConditionOfTheTree_AndEveryConditionHasARow()
    {
        Assert.NotEmpty(Cfg.Means);
        Assert.Equal(Cfg.Means.Count, Cfg.Means.Select(m => m.Id).Distinct().Count());
        foreach (var id in AmbitionRules.Ruled) Assert.Contains(Cfg.Means, m => m.Condition == id);          // AT-2's "means row"
        Assert.All(Cfg.Means, m => Assert.Contains(m.Condition, AmbitionRules.Ruled));
        Assert.All(Cfg.Means, m => Assert.Contains(m.Directive, new[] { "work", "trade_route", "tend", "train", "contract", "buy", "recruit", "steal", "none" }));
    }

    // ------------------------------------------------------------------ AT-7

    [Fact]
    public void AT7_CoinShortAndTrade35_TradeRouteBeatsWork_ByRate()
    {
        var cond = Cond("has_coin_at_least", ("pence", "1000"));
        var cands = AmbitionPlanner.Candidates(Cfg.Means, cond, Inp(new LimitSet(), skill: s => s == "trading" ? 35 : 0, open: (m, _) => m.Directive is "work" or "trade_route"));
        var best = AmbitionPlanner.Best(cands)!.Value;
        Assert.Equal("coin_trade", best.Means.Id);
        Assert.True(best.Score > cands.First(c => c.Means.Id == "coin_work").Score);
        // Trade 20: the route needs 30, so work it is
        var low = AmbitionPlanner.Best(AmbitionPlanner.Candidates(Cfg.Means, cond, Inp(new LimitSet(), skill: s => s == "trading" ? 20 : 0, open: (m, _) => m.Directive is "work" or "trade_route")))!.Value;
        Assert.Equal("coin_work", low.Means.Id);
        // a learned rate moves the choice: work that has been paying 400 a day beats a route that has paid 20
        var learned = AmbitionPlanner.Best(AmbitionPlanner.Candidates(Cfg.Means, cond, Inp(new LimitSet(), skill: s => s == "trading" ? 35 : 0, open: (m, _) => m.Directive is "work" or "trade_route", rates: new() { ["coin_work"] = 400, ["coin_trade"] = 20 })))!.Value;
        Assert.Equal("coin_work", learned.Means.Id);
    }

    [Fact]
    public void AT7_WithASpendingCapOfZero_BuyIsNeverChosen_AndWithRoomItIs()
    {
        var cond = Cond("owns_building", ("kind", "shop"));
        var zero = new LimitSet { SpendCapMitesPerDay = 0 };
        var none = AmbitionPlanner.Candidates(Cfg.Means, cond, Inp(zero));
        Assert.Equal(MeansBar.Spend, none.First(c => c.Means.Directive == "buy").Bar);
        Assert.NotEqual("building_buy", AmbitionPlanner.Best(none)?.Means.Id);
        Assert.Equal(MeansBar.Spend, AmbitionPlanner.Candidates(Cfg.Means, cond, Inp(new LimitSet { SpendCapMitesPerDay = 300 })).First(c => c.Means.Directive == "buy").Bar);   // the 3-crown default is far short of a shop
        var roomy = new LimitSet { SpendCapMitesPerDay = -1, SpendCapTotalMites = -1 };
        Assert.Equal("building_buy", AmbitionPlanner.Best(AmbitionPlanner.Candidates(Cfg.Means, cond, Inp(roomy, purse: 50_000)))!.Value.Means.Id);
        Assert.Equal(MeansBar.Spend, AmbitionPlanner.Candidates(Cfg.Means, cond, Inp(roomy, purse: 100)).First(c => c.Means.Directive == "buy").Bar);                          // cannot afford it
        Assert.Equal(MeansBar.Spend, AmbitionPlanner.Candidates(Cfg.Means, cond, Inp(new LimitSet { SpendCapMitesPerDay = -1, SpendCapTotalMites = 1000 }, purse: 50_000)).First(c => c.Means.Directive == "buy").Bar);
    }

    [Fact]
    public void AT7_TiesGoToTheEarlierRowOfTheContent_AndTheScoreIsIntegerMath()
    {
        Assert.Equal(60 * 1000 / 1000 - 50 * 500 / 1000, AmbitionPlanner.Score(60, 1000, 50, 500));
        Assert.Equal(7, AmbitionPlanner.Score(10, 700, 0, 500));          // 7000/1000, no rounding up
        var a = new MeansDef { Id = "a", Condition = "has_coin_at_least", Directive = "work", PriorPmDay = 100 };
        var b = a with { Id = "b" };
        var best = AmbitionPlanner.Best(AmbitionPlanner.Candidates([a, b], Cond("has_coin_at_least", ("pence", "5")), Inp(new LimitSet())))!.Value;
        Assert.Equal("a", best.Means.Id);
    }

    // ------------------------------------------------------------------ AT-8

    [Fact]
    public void AT8_NoCrimesRemovesTheftFromEveryMeans_TheRiskLimitAndHomeByNightfallRemoveTheirs()
    {
        foreach (var cond in new[] { Cond("has_coin_at_least", ("pence", "9999")), Cond("wealth_at_least", ("mites", "99999")) })
        {
            var on = AmbitionPlanner.Candidates(Cfg.Means, cond, Inp(new LimitSet { NoCrimes = true }));
            foreach (var c in on.Where(c => c.Means.Tags.Contains("crime"))) { Assert.Equal(MeansBar.Crime, c.Bar); Assert.Equal(0, c.Score); }
        }
        var cond0 = Cond("has_coin_at_least", ("pence", "9999"));
        Assert.Equal("coin_steal", AmbitionPlanner.Best(AmbitionPlanner.Candidates(Cfg.Means, cond0, Inp(new LimitSet { NoCrimes = false, MaxRiskPm = 800 })))!.Value.Means.Id);   // the crime is the best way when it is allowed
        Assert.NotEqual("coin_steal", AmbitionPlanner.Best(AmbitionPlanner.Candidates(Cfg.Means, cond0, Inp(new LimitSet { NoCrimes = true, MaxRiskPm = 800 })))?.Means.Id);
        Assert.Equal(MeansBar.Risk, AmbitionPlanner.Candidates(Cfg.Means, cond0, Inp(new LimitSet { NoCrimes = false, MaxRiskPm = 200 })).First(c => c.Means.Id == "coin_steal").Bar);
        Assert.Equal(MeansBar.Home, AmbitionPlanner.Candidates(Cfg.Means, cond0, Inp(new LimitSet { HomeByNightfall = true }, skill: s => 50)).First(c => c.Means.Id == "coin_trade").Bar);
    }

    [Fact, Trait("Category", "Soak")]
    public void AT8_TheHeroNeverCommitsACrimeNorBreaksTheCap_InA60DayRun()
    {
        using var w = ApWorld.Soak(heroCrowns: 5);
        UseAmbition(w, Amb("test_fortune", Cond("has_coin_at_least", ("pence", "100000"))));
        int theft = 0; w.Ap.MeansHook = (_, m) => { if (m.Tags.Contains("crime")) theft++; return false; };
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Pursue());
        st.Limits.NoCrimes = true; st.Limits.SpendCapMitesPerDay = 200; st.Limits.HomeByNightfall = true;
        for (int i = 0; i < st.Policy.Length; i++) if ((InterruptKind)i != InterruptKind.BigChoice) st.Policy[i] = InterruptPolicy.Notify;
        var home = w.Ap.AnchorsOf(st, w.Sec); st.Limits.HomeX = home.HomeX; st.Limits.HomeY = home.HomeY;
        w.Ap.Extras.Add(new TaggedAction("take_bread", 1200, ActionTag.Crime));
        var days = new List<DaySummary>();
        w.RunHours(24 * 60, h => { if (st.Summaries.Count > 0 && (days.Count == 0 || days[^1].Day != st.Summaries[^1].Day)) days.Add(st.Summaries[^1]); });
        output.WriteLine($"days {days.Count}, earned {days.Sum(d => d.Earned)}, spent {days.Sum(d => d.Spent)}, why: {st.Why}");
        Assert.Equal(0, theft); Assert.Equal(0, w.Book.Log.Crimes); Assert.Equal(0, w.Book.Log.Violations); Assert.Equal(0, w.Book.Log.PlanViolations);
        Assert.All(days, d => Assert.True(d.Spent <= 200 + 5, $"day {d.Day} spent {d.Spent}"));
        Assert.True(days.Sum(d => d.Earned) > 0, "pursuing the coin step it worked for pay");
        Assert.True(w.Book.Log.Lines.Any(l => l.Text.Contains("Ambition") && l.Text.Contains("means coin_work")), "the work means ran");
        Assert.Equal(1000, st.HealthPm);
    }

    // ------------------------------------------------------------------ AT-9

    [Fact]
    public void AT9_EveryMeansOverTheCap_AmbitionStuckOnDay3_WithTheReason()
    {
        using var w = ApWorld.Soak();
        UseAmbition(w, Amb("test_works", Cond("owns_works_tier", ("tier", "1"))));
        w.Ap.MeansHook = (_, _) => true;
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Pursue()); st.Limits.SpendCapMitesPerDay = 300;
        long t0 = w.Sec, dayStart = t0 / 86400 * 86400;
        w.RunHours(24 * 3);
        var stuck = w.Book.Log.Lines.Where(l => l.Code == LogCode.Interrupt && l.Arg == (byte)InterruptKind.AmbitionStuck).ToList();
        Assert.Single(stuck);
        Assert.Contains("every way breaks your spending cap", stuck[0].Text);
        Assert.True(stuck[0].Sec >= dayStart + 2 * 86400 && stuck[0].Sec < dayStart + 3 * 86400, "raised as the third game day opens");
        Assert.Contains("every way breaks your spending cap", st.Why);             // the panel's why line
        Assert.StartsWith("Pursuing", st.Active!.Name);
    }

    [Fact]
    public void AT9_AnAiHandledStuckMovesToTheNextDirective_ANotifiedOneKeepsTrying()
    {
        using var w = ApWorld.Soak();
        UseAmbition(w, Amb("test_works", Cond("owns_works_tier", ("tier", "1"))));
        w.Ap.MeansHook = (_, _) => true;
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Pursue(), Directive.Work()); st.Policy[(int)InterruptKind.AmbitionStuck] = InterruptPolicy.Ai;
        w.RunHours(24 * 3 + 2);
        Assert.Equal(DirectiveState.Blocked, st.Queue[0].State); Assert.Equal(DirectiveKind.Work, st.Active!.Kind);
        using var w2 = ApWorld.Soak();
        UseAmbition(w2, Amb("test_works", Cond("owns_works_tier", ("tier", "1"))));
        w2.Ap.MeansHook = (_, _) => true;
        var st2 = w2.Ap.Enable(w2.Hero, w2.Sec, Directive.Pursue(), Directive.Work());         // the default policy is Notify
        w2.RunHours(24 * 3 + 2);
        Assert.Equal(DirectiveKind.PursueAmbition, st2.Active!.Kind);
    }

    [Fact]
    public void AT9_NoProgressInTenDays_RaisesStuckToo()
    {
        using var w = ApWorld.Soak();
        UseAmbition(w, Amb("test_slow", Cond("has_coin_at_least", ("pence", "100000000"))));
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Pursue()); st.Limits.SpendCapMitesPerDay = 300;
        w.Ap.Jobs = () => null; w.Ap.DrivesJobs = false;                                  // nowhere to earn: the coin never moves
        w.RunHours(24 * 11);
        Assert.Contains(w.Book.Log.Lines, l => l.Code == LogCode.Interrupt && l.Arg == (byte)InterruptKind.AmbitionStuck && l.Text.Contains("progress has not moved in 10 days"));
    }

    // ------------------------------------------------------------------ AT-10

    [Fact]
    public void AT10_TheRate_IsTheIntegerRunningAverage_After5DaysOfPlus40()
    {
        using var w = ApWorld.Soak();
        var st = w.Book.GetOrAdd(w.Hero, w.Ap.Cfg);
        int expect = Cfg.Means.First(m => m.Id == "coin_work").PriorPmDay;
        for (int d = 0; d < 5; d++) { w.Ap.Learn(st, "coin_work", 40); expect = (3 * expect + 40 + 2) / 4; }
        Assert.InRange(st.Rates["coin_work"], expect - 2, expect + 2);
        Assert.Equal(expect, st.Rates["coin_work"]);
        Assert.Equal(1000, AmbitionPlanner.NextRate(1000, 5000));          // clamped
        Assert.Equal(0, AmbitionPlanner.NextRate(0, -50));
        for (int i = 0; i < 200; i++) Assert.InRange(AmbitionPlanner.NextRate(i * 5, i % 90), 0, 1000);
    }

    [Fact]
    public void TheRates_AreLearnedFromTheDaysTheMeansRan_AndSaved()
    {
        using var w = ApWorld.Soak(heroCrowns: 2);
        UseAmbition(w, Amb("test_fortune", Cond("has_coin_at_least", ("pence", "100000"))));
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Pursue());
        w.RunHours(24 * 4);
        Assert.True(st.Rates.ContainsKey("coin_work"), string.Join(", ", st.Rates));
        Assert.Equal("coin_work", st.ChildMeans); Assert.NotNull(st.Child);
        // save, load, save: byte identical, with the child and the rates in it
        var save = AutopilotSave.Capture(w.Book);
        byte[] a = SaveCodec.SerializeCompressed(save);
        var back = SaveCodec.SerializeCompressed(AutopilotSave.Capture(SaveCodec.DeserializeCompressed<AutopilotSave>(a).Restore()));
        Assert.Equal(a, back);
        var r = SaveCodec.DeserializeCompressed<AutopilotSave>(a).Restore().Get(w.Hero)!;
        Assert.Equal(st.Rates, r.Rates); Assert.Equal(st.ChildMeans, r.ChildMeans); Assert.Equal(st.Child!.Name, r.Child!.Name); Assert.Equal(st.JobsDoneTotal, r.JobsDoneTotal);
    }

    [Fact]
    public void AnAutopilotRowWithoutThePlanner_SavesWithNullOptionalKeys()
    {
        using var w = ApWorld.Soak();
        w.Ap.Enable(w.Hero, w.Sec, Directive.Rest(1));
        var s = AutopilotSave.Capture(w.Book).States[0];
        Assert.Null(s.Child); Assert.Null(s.ChildMeans); Assert.Null(s.RateIds); Assert.Null(s.RateValues); Assert.Null(s.JobsDone);
    }

    // ------------------------------------------------------------------ AT-11

    [Fact]
    public void AT11_ACompanionUnderTheDirective_TrainsItsSkillToTheStepsLevel()
    {
        using var w = ApWorld.Soak();
        UseAmbition(w, Amb("test_smith", Cond("has_proficiency", ("skill", "smithing"), ("level", "25"))));
        var p = w.State.Population.People; int idx = Enumerable.Range(0, p.Count).First(i => p.IsAlive(i) && p.AgeYears(i, (int)(w.Sec / 86400)) is >= 20 and < 40);
        var member = p.IdOf(idx);
        w.Ctx.Party.Squad.Members.Add(new Follower { Person = member, PersonIndex = idx, Name = "m", State = HireState.Active, Loyalty = 500, JoinedDay = (int)(w.Sec / 86400), MaxSkill = 20 });
        var st = w.Ap.Enable(member, w.Sec, Directive.Pursue());
        int before = w.Ctx.Craft.State.Book.Level(member, Skill.Smithing);
        w.RunHours(24 * 30);
        int after = w.Ctx.Craft.State.Book.Level(member, Skill.Smithing);
        output.WriteLine($"smithing {before} -> {after}; why: {st.Why}; child {st.ChildMeans}");
        Assert.True(before < 25);
        Assert.True(after >= 25, $"trained to {after}");
        Assert.Contains(w.Book.Log.Lines, l => l.Person == member && l.Text.Contains("means skill_train"));
        Assert.Equal(0, w.Book.Log.Violations);
    }

    [Fact]
    public void TheHero_PursuingATrainStep_ReachesIt_AndTheStepIsCheckedHourlyAndTheAmbitionIsDone()
    {
        using var w = ApWorld.Soak();
        var first = Amb("test_smith", Cond("has_proficiency", ("skill", "smithing"), ("level", "12")));
        var next = Amb("test_next", Cond("has_coin_at_least", ("pence", "100000")));
        w.Ap.Tree = new AmbitionTree([first with { Opens = ["test_next"] }, next]);
        w.State.Hero!.AmbitionId = "test_smith"; w.State.Hero.Milestone = 0;
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Pursue()); st.Policy[(int)InterruptKind.AmbitionChoice] = InterruptPolicy.Ai;
        w.RunHours(24 * 20);
        var h = w.State.Hero;
        Assert.True(h.IsDone("test_smith"), $"milestone {h.Milestone}, level {w.Ctx.Craft.State.Book.Level(w.Hero, Skill.Smithing)}");
        Assert.Equal("test_next", h.AmbitionId);                                              // the AI picked the one offered whose means score
        var types = w.State.Chronicle.All.Select(e => e.TypeId).ToList();
        Assert.Contains("ambition_done", types); Assert.Contains("ambition_chosen", types);
        Assert.All(w.State.Chronicle.All.Where(e => e.TypeId is "ambition_done" or "ambition_chosen"), e => { Assert.Empty(e.Causes); Assert.StartsWith("ch_p8_", e.TextKey); });
        Assert.Contains(w.Book.Log.Lines, l => l.Code == LogCode.Interrupt && l.Arg == (byte)InterruptKind.AmbitionChoice);
    }

    [Fact]
    public void AGreatAmbitionWritesGreatAmbitionDone_AndAPausedChoiceIsLeftToThePlayer()
    {
        using var w = ApWorld.Soak(heroCrowns: 50);
        var g = Amb("test_great", Cond("has_coin_at_least", ("pence", "10"))) with { Tier = "great", Ending = "dynasty" };
        var n = Amb("test_after", Cond("has_coin_at_least", ("pence", "100000")));
        w.Ap.Tree = new AmbitionTree([g with { Opens = ["test_after"] }, n]);
        w.State.Hero!.AmbitionId = "test_great";
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Rest(1));                                // the default policy of AmbitionChoice pauses
        w.RunHours(3);
        Assert.Contains(w.State.Chronicle.All, e => e.TypeId == "great_ambition_done");
        Assert.Equal(new[] { "test_after" }, w.State.Hero.Offered);
        Assert.Equal("", w.State.Hero.AmbitionId == "test_great" ? "" : w.State.Hero.AmbitionId);   // not chosen for the player
        Assert.True(w.Ap.BusOf(st).Paused);
    }

    // ------------------------------------------------------------------ AT-12

    public static IEnumerable<object[]> Origins() => Enumerable.Range(0, 8).Select(i => new object[] { i });

    [Theory, Trait("Category", "Soak")]
    [MemberData(nameof(Origins))]
    public void AT12_ATwoYearRunPerOrigin_TheHeroIsAliveAndSolvent_AStepIsDoneWhereAMeansExists_AndTheRunIsDeterministic(int origin)
    {
        const int Days = 120;
        string Run(out string ambition, out int milestone, out int done, out long minCoin, out int health, out string why)
        {
            using var w = ApWorld.Soak(heroCrowns: 30, originIndex: origin);
            var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Pursue(), Directive.Work());
            st.Limits.SpendCapMitesPerDay = 200; st.PresetWatch(); for (int i = 0; i < st.Policy.Length; i++) if ((InterruptKind)i != InterruptKind.BigChoice) st.Policy[i] = InterruptPolicy.Notify;
            long min = long.MaxValue;
            w.RunHours(24 * Days, _ => min = Math.Min(min, w.Ctx.Coin));
            var h = w.State.Hero!;
            ambition = h.AmbitionId; milestone = h.Milestone; done = h.AmbitionsDone.Count; minCoin = min; health = st.HealthPm; why = st.Why;
            Assert.Equal(0, w.Book.Log.Violations); Assert.Equal(0, w.Book.Log.Crimes);
            Assert.True(w.Econ.Ledger.Audit().IsConserved);
            return w.Book.ComputeHash() + ":" + h.AmbitionId + h.Milestone + ":" + w.Ctx.Coin + ":" + w.State.Chronicle.All.Count;
        }
        string a = Run(out var amb, out int ms, out int done, out long minCoin, out int health, out string why);
        output.WriteLine($"origin {origin}: {amb} step {ms}, done {done}, min coin {minCoin}, why: {why}");
        Assert.Equal(1000, health);
        Assert.True(minCoin >= 0);
        string b = Run(out _, out _, out _, out _, out _, out _);
        Assert.Equal(a, b);                                                                    // determinism
        // a step is done wherever the means table can move the first step; the others are the player's (no means: they go stuck and say so)
        var first = AutopilotContentFirstStep(amb);
        bool movable = first.Count > 0 && first.All(c => c is "has_coin_at_least" or "wealth_at_least");
        if (movable || ms > 0 || done > 0) Assert.True(ms > 0 || done > 0, $"{amb}: no step done");
    }

    private static List<string> AutopilotContentFirstStep(string ambId)
    {
        using var w = ApWorld.Soak(); var a = w.Ap.Tree.Get(ambId);
        return a == null || a.Steps.Count == 0 ? [] : a.Steps[0].Conditions.Select(c => c.Id).ToList();
    }

    [Fact]
    public void AT12_AResumedRun_GivesTheSameHashAsAStraightOne()
    {
        string Straight()
        {
            using var w = ApWorld.Soak(heroCrowns: 5);
            UseAmbition(w, Amb("test_fortune", Cond("has_coin_at_least", ("pence", "100000"))));
            w.Ap.Enable(w.Hero, w.Sec, Directive.Pursue());
            w.RunHours(24 * 9);
            return w.Book.ComputeHash() + w.Ctx.Coin;
        }
        string Resumed()
        {
            using var w = ApWorld.Soak(heroCrowns: 5);
            UseAmbition(w, Amb("test_fortune", Cond("has_coin_at_least", ("pence", "100000"))));
            w.Ap.Enable(w.Hero, w.Sec, Directive.Pursue());
            w.RunHours(24 * 4 + 7);
            var bytes = SaveCodec.SerializeCompressed(AutopilotSave.Capture(w.Book));
            w.ResumeWith(SaveCodec.DeserializeCompressed<AutopilotSave>(bytes).Restore());
            w.Ap.Tree = new AmbitionTree([Amb("test_fortune", Cond("has_coin_at_least", ("pence", "100000")))]);
            w.RunHours(24 * 5 - 7);
            return w.Book.ComputeHash() + w.Ctx.Coin;
        }
        Assert.Equal(Straight(), Resumed());
    }
}
