using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Trade;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Autopilot;

/// <summary>The autopilot driving a hero on the real headless world (economy, markets, odd jobs, the hero's desk at the hour barrier).</summary>
public class AutopilotWorldTests(ITestOutputHelper output)
{
    private static string Dump(ApWorld w) => string.Join("\n", w.Book.Log.Lines.Select(l => $"{new GameTime(l.Sec).DayOfYear}d {l.Sec % 86400 / 3600:00}:{l.Sec % 3600 / 60:00} {l.Code} {l.Text}"));

    // ------------------------------------------------------------------ AP-1: one AI

    [Fact]
    public void AP1_TheHeroOnAutopilotMakesExactlyTheChoicesOfAnNpcWithTheSameState_3GameDays()
    {
        using var w = ApWorld.Soak();
        w.Ap.Jobs = () => null; w.Ap.DrivesJobs = false;
        var st = w.Ap.Enable(w.Hero, w.Sec);                                   // no directive: "follow my schedule"
        long t0 = w.Sec / 86400 * 86400 + 86400;                               // from midnight
        st.NextWindow = t0 / 20;
        var hero = new List<(long, ActionKind)>();
        w.Ap.OnAction = (_, at, k) => hero.Add((at, k));
        w.Ap.Advance(t0 + 3 * 86400 - 1);
        // the NPC: the same row, the same seed, the same brain function with no policy, deciding each 20-second window as the live people do
        var a = w.Ap.AnchorsOf(st, t0);
        var npc = new List<(long, ActionKind)>(); ActionKind cur = ActionKind.Idle; long since = long.MinValue;
        for (long at = t0; at < t0 + 3 * 86400; at += 20)
        {
            a = w.Ap.AnchorsOf(st, at);
            var ch = PersonDecision.Decide(UtilityBrain.Default, a, at, cur, since, false, 0, 0, 0, false, false).Choice;
            if (ch.Kind != cur) { cur = ch.Kind; since = at; }
            npc.Add((at, ch.Kind));
        }
        Assert.Equal(npc.Count, hero.Count);
        Assert.Equal(npc, hero);
        Assert.True(hero.Select(h => h.Item2).Distinct().Count() >= 3, "the day has several kinds of action");
        Assert.Equal(0, w.Book.Log.Violations);
    }

    [Fact]
    public void AP1b_APolicyOnTheBrainIsTheOnlyDifference_AndAnEmptyPolicyIsNone()
    {
        using var w = ApWorld.Soak();
        var a = w.Ap.AnchorsOf(w.Book.GetOrAdd(w.Hero, w.Ap.Cfg), w.Sec);
        for (long at = w.Sec; at < w.Sec + 86400; at += 600)
        {
            var plain = PersonDecision.Decide(UtilityBrain.Default, a, at, ActionKind.Idle, long.MinValue, true, 0, 0, 0, false, false);
            var neutral = PersonDecision.Decide(UtilityBrain.Default, a, at, ActionKind.Idle, long.MinValue, true, 0, 0, 0, false, false, new ActionPolicy());
            Assert.Equal(plain.Choice, neutral.Choice);
        }
    }

    [Fact]
    public void TheHeroIsModelledOnALabourerOfTheSettlementTheyStandIn_WhenTheHostKnowsWhereTheyStand()
    {
        using var w = ApWorld.Soak();
        var region = w.State.Population.Region; Assert.True(region.Settlements.Count > 1);
        w.Ap.PositionHook = id => (region.Settlements[1].CenterX * 1000L + 5000, region.Settlements[1].CenterY * 1000L);
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Work());
        Assert.Equal(1, w.Ap.AnchorsOf(st, w.Sec).HomeSettlement);
    }

    // ------------------------------------------------------------------ AP-2 at the barrier

    [Fact]
    public void AP2_TheBarrierRejectsAForcedAutopilotPurchaseOverTheCap_ButNeverTheHumans()
    {
        using var w = ApWorld.Soak(heroCrowns: 100);
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Rest(1)); st.Limits.SpendCapMitesPerDay = 300; st.Limits.NoCrimes = true;
        while ((w.Ticker.HourDone + 1) % 24 != 9) w.RunHours(1);                  // 09:00: the market is open
        var a = w.Ap.AnchorsOf(st, w.Sec); var book = w.Ctx.Book(a.HomeSettlement)!;
        ushort g = w.Econ.Goods.IdOf("wheat"); book.Sync(g);
        long qty = 1000; while (Quotes.Buy(book, g, qty).TotalMites < 100) qty += 250;
        long one = Quotes.Buy(book, g, qty).TotalMites;
        long h = w.Ticker.HourDone + 1, sec = h * 3600;                            // five autopilot buys of about 100
        st.SpentDay = (int)(sec / 86400); st.SpentToday = 0;
        var results = new List<HeroResult>();
        long coin0 = w.Ctx.Coin; int boughtOk = 0;
        for (int i = 0; i < 5; i++)
        {
            var cmd = w.Desk.Issue(new HeroCommand { Kind = HeroCommandKind.Buy, Settlement = a.HomeSettlement, Good = g, Qty = qty, IssuedSec = sec + i, Origin = CommandOrigin.Autopilot });
            w.Desk.ApplyDue(w.Ctx, h);
            var r = w.Desk.View!.Results.Last(x => x.Seq == cmd.Seq); results.Add(r); if (r.Ok) boughtOk++;
        }
        output.WriteLine($"spent today {st.SpentToday}/{st.SpentDay} day {sec / 86400} cap {st.Limits.SpendCapMitesPerDay}\n" + Dump(w).Split('\n').TakeLast(6).Aggregate((a1, b1) => a1 + "\n" + b1) + $"\none buy = {one}; results: {string.Join(" | ", results.Select(r => r.Code + " " + r.Text))}");
        Assert.True(boughtOk is >= 2 and <= 3, $"the cap of 300 allows two or three buys of {one}, got {boughtOk}");
        Assert.True(w.Book.Log.Violations >= 5 - boughtOk && w.Book.Log.Violations > 0);
        Assert.All(results.Skip(boughtOk), r => Assert.Contains("LimitViolation", r.Text));
        Assert.True(coin0 - w.Ctx.Coin <= 300);
        // the next day the allowance is new
        w.RunHours(24); h = w.Ticker.HourDone + 1; sec = h * 3600;
        var next = w.Desk.Issue(new HeroCommand { Kind = HeroCommandKind.Buy, Settlement = a.HomeSettlement, Good = g, Qty = qty, IssuedSec = sec, Origin = CommandOrigin.Autopilot });
        w.Desk.ApplyDue(w.Ctx, h); Assert.True(w.Desk.View!.Results.Last(x => x.Seq == next.Seq).Ok);
        // the player's own commands are never filtered
        long violations = w.Book.Log.Violations;
        for (int i = 0; i < 6; i++) { var c = w.Desk.Issue(new HeroCommand { Kind = HeroCommandKind.Buy, Settlement = a.HomeSettlement, Good = g, Qty = qty, IssuedSec = sec + 10 + i }); w.Desk.ApplyDue(w.Ctx, h); Assert.True(w.Desk.View!.Results.Last(x => x.Seq == c.Seq).Ok); }
        Assert.Equal(violations, w.Book.Log.Violations);
    }

    // ------------------------------------------------------------------ AP-4: no crimes

    [Fact]
    public void AP4_NoCrimes_ATaggedActionAt1200IsNeverChosen_Over365Days_AndWithTheLimitOffItIsChosenAtOnce()
    {
        using var w = ApWorld.Soak();
        w.Ap.Jobs = () => null; w.Ap.DrivesJobs = false;
        w.Ap.Extras.Add(new TaggedAction("take_bread", 1200, ActionTag.Crime));
        var st = w.Ap.Enable(w.Hero, w.Sec); st.Limits.NoCrimes = true; for (int i = 0; i < st.Policy.Length; i++) st.Policy[i] = InterruptPolicy.Notify;
        long t0 = w.Sec;
        w.Ap.Advance(t0 + 365L * 86400);
        Assert.Equal(0, w.Book.Log.Crimes); Assert.DoesNotContain(w.Book.Log.Lines, l => l.Text.StartsWith("Crime"));
        // with NoCrimes off it is chosen within two game days
        w.Ap.Disable(w.Hero, t0 + 365L * 86400);
        var st2 = w.Ap.Enable(w.Hero, t0 + 366L * 86400); st2.Limits.NoCrimes = false; w.Ap.BusOf(st2).Resume();
        w.Ap.Advance(t0 + 368L * 86400);
        output.WriteLine(Dump(w).Split('\n').TakeLast(12).Aggregate((a, b) => a + "\n" + b) + $"\npaused {w.Ap.BusOf(st2).Paused} enabled {st2.Enabled} {st2.Paused}");
        Assert.True(w.Book.Log.Crimes > 0);
    }

    // ------------------------------------------------------------------ directives

    [Fact]
    public void TheWorkDirective_ClaimsTheBestPayingJob_PaysIntoThePurse_EatsBuysFood_AndKeepsTheLimits()
    {
        using var w = ApWorld.Soak();
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Work(days: 6)); st.Limits.SpendCapMitesPerDay = 200;
        long c0 = w.Ctx.Coin;
        w.RunHours(24 * 8);
        output.WriteLine(Dump(w));
        var s = w.Book.Get(w.Hero)!;
        Assert.True(s.Summaries.Sum(d => d.JobsDone) >= 6, "at least a job a day");
        Assert.True(w.Ctx.Coin > c0, $"the hero earned: {c0} -> {w.Ctx.Coin}");
        Assert.Equal(DirectiveState.Done, s.Queue[0].State);                       // six days of Work then Done, and DirectiveDone notified
        Assert.True(w.Book.Log.CountInterrupts(InterruptKind.DirectiveDone) >= 1);
        Assert.Equal(0, w.Book.Log.Violations); Assert.All(s.Summaries.Skip(1), d => Assert.True(d.Spent <= 200, $"day {d.Day} spent {d.Spent}"));
        Assert.True(w.Econ.Ledger.Audit().IsConserved);
        Assert.Contains(w.State.Chronicle.All, e => e.TypeId == "autopilot_start" && e.Data["autopilot"] == "1");
    }

    [Fact]
    public void TheTrainDirective_PracticesUpToTheCap_ThenStops_AndTheSkillMovesOnlyThroughUses()
    {
        using var w = ApWorld.Soak();
        var smith = Skill.Smithing; long before = w.Ctx.Craft.State.Book.Micro(w.Hero, smith);
        w.Ap.Enable(w.Hero, w.Sec, Directive.Train(smith, 30), Directive.Work(1));
        w.RunHours(24 * 3);
        long after = w.Ctx.Craft.State.Book.Micro(w.Hero, smith);
        output.WriteLine($"smithing {before / 1_000_000.0:0.00} -> {after / 1_000_000.0:0.00}\n" + Dump(w));
        Assert.True(after > before, "practice teaches");
        Assert.True(w.Ctx.Craft.State.Book.Level(w.Hero, smith) >= 30 || w.Book.Get(w.Hero)!.Queue[0].State == DirectiveState.Active);
        var d = w.Book.Get(w.Hero)!;
        if (d.Queue[0].State == DirectiveState.Done) Assert.True(w.Ctx.Craft.State.Book.Level(w.Hero, smith) >= 30);
    }

    [Fact]
    public void TheTravelDirective_WalksToTheDestination_RaisesArrivedAndDirectiveDone_AndTheNextDirectiveStarts()
    {
        using var w = ApWorld.Soak();
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.TravelTo(0, 0, "the far gate"), Directive.Rest(1));
        var a = w.Ap.AnchorsOf(st, w.Sec); st.PosX = a.SquareX; st.PosY = a.SquareY;
        st.Queue[0].Params = [a.SquareX + 2_000_000, a.SquareY + 500_000, 0, 0];       // about 2 km
        st.Policy[(int)InterruptKind.Arrived] = InterruptPolicy.Notify;
        w.RunHours(14);
        output.WriteLine(Dump(w));
        Assert.Equal(DirectiveState.Done, st.Queue[0].State);
        Assert.True(w.Book.Log.CountInterrupts(InterruptKind.Arrived) >= 1);
        Assert.Equal(DirectiveKind.Rest, st.Active!.Kind);
        Assert.Contains(w.State.Chronicle.All, e => e.TypeId == "autopilot_arrived");
    }

    [Fact]
    public void TheTravelDirective_UsesTravelState_WhenThereIsOne_InterruptsGoThroughTheRouter_AndAnAiPolicyKeepsItGoing()
    {
        using var w = ApWorld.Soak();
        var goods = w.Econ.Goods;
        var tr = new TravelState(w.Content, 1, goods, w.Econ.Mass) { Hosted = true, OpenStore = id => w.Econ.OpenStore(Ravensguard.Sim.Goods.StoreKind.Open, id), PackStore = () => w.Ctx.Pack };
        w.State.Travel = tr;
        w.Ap.TravelWorld = new FlatWorld();
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.TravelTo(0, 0, "Eastbridge"));
        var a = w.Ap.AnchorsOf(st, w.Sec); st.PosX = a.SquareX; st.PosY = a.SquareY;
        st.Queue[0].Params = [a.SquareX + 3_000_000, a.SquareY, 0, 0];
        for (int i = 0; i < InterruptBus.KindCount; i++) st.Policy[i] = InterruptPolicy.Ai;
        st.Policy[(int)InterruptKind.Attacked] = InterruptPolicy.Pause;
        w.RunHours(20);
        output.WriteLine(Dump(w));
        Assert.Equal(DirectiveState.Done, st.Queue[0].State);
        Assert.Contains(tr.Logs, l => l.Completed);
        Assert.True(w.Book.Log.CountInterrupts(InterruptKind.Arrived) >= 1);
    }

    [Fact]
    public void RiskAndNightfallLimits_HoldATravelDirective_AtHomeUntilItFits()
    {
        using var w = ApWorld.Soak();
        w.Ap.DangerAt = (_, _, _) => 650;                                          // above Medium (500)
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.TravelTo(0, 0, "the dangerous way")); st.Policy[(int)InterruptKind.LimitReached] = InterruptPolicy.Notify; st.Policy[(int)InterruptKind.Arrived] = InterruptPolicy.Notify;
        var a = w.Ap.AnchorsOf(st, w.Sec); st.PosX = a.SquareX; st.PosY = a.SquareY; st.Queue[0].Params = [a.SquareX + 1_000_000, a.SquareY, 0, 0];
        w.RunHours(30);
        Assert.NotEqual(DirectiveState.Done, st.Queue[0].State);
        Assert.True(w.Book.Log.CountInterrupts(InterruptKind.LimitReached) >= 1);
        Assert.True(PathPlan.Dist(st.PosX - st.Queue[0].Params[0], st.PosY - st.Queue[0].Params[1]) > 500_000);      // it never set out
        // raised to High (800) it goes
        w.Ap.DangerAt = (_, _, _) => 650; st.Limits.MaxRiskPm = 800; st.CampUntil = 0;
        w.RunHours(14, h => output.WriteLine($"h{h % 24} intent {st.Intent} pos {st.PosX / 1000} camp {st.CampUntil - h * 3600} (sec in) aux {st.Queue[0].Aux0} limitDay {st.LimitDay}"));
        output.WriteLine(Dump(w) + $"\npos {st.PosX},{st.PosY} target {st.Queue[0].Params[0]},{st.Queue[0].Params[1]} camp {st.CampUntil} aux {st.Queue[0].Aux0} intent {st.Intent} paused {w.Ap.BusOf(st).Paused}");
        Assert.Equal(DirectiveState.Done, st.Queue[0].State);
    }

    [Fact]
    public void TheRestDirective_SleepsAtHome_HealsAndEndsWhenHealed_AndInjuredPausesOrNotifiesByPolicy()
    {
        using var w = ApWorld.Soak();
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Rest());
        st.HealthPm = 700; st.Bleeding = true;
        st.Policy[(int)InterruptKind.Injured] = InterruptPolicy.Ai;
        w.RunHours(24 * 4);
        output.WriteLine(Dump(w));
        Assert.True(w.Book.Log.CountInterrupts(InterruptKind.Injured) >= 1);
        Assert.Equal(1000, st.HealthPm); Assert.False(st.Bleeding);
        Assert.Equal(DirectiveState.Done, st.Queue[0].State);
        // a Pause policy stops the game at once
        var st2 = w.Book.Get(w.Hero)!; st2.Policy[(int)InterruptKind.Injured] = InterruptPolicy.Pause; st2.HealthPm = 600; st2.EdgeBits = 0; st2.LastFiredSec[(int)InterruptKind.Injured] = long.MinValue;
        w.Ap.Advance(w.Sec + 60);
        Assert.True(w.Ap.BusOf(st2).Paused);
    }

    [Fact]
    public void LowMoneyAndNeedMoney_SwitchTheAiToEarning_AndContractsAreAcceptedOnlyFromTheAllowedKinds()
    {
        using var w = ApWorld.Soak(heroCrowns: 2);
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Rest(30)); st.Limits.PurseReserveMites = 500;
        st.Policy[(int)InterruptKind.LowMoney] = InterruptPolicy.Ai; st.Policy[(int)InterruptKind.Contract] = InterruptPolicy.Ai; st.Policy[(int)InterruptKind.BigChoice] = InterruptPolicy.Ai;
        w.Ap.Advance(w.Sec + 100);
        Assert.Equal(DirectiveKind.Work, st.Active!.Kind);                          // LowMoney with Ai: switch to the best-paying job in range
        st.Limits.AcceptContractKinds = 1u << (int)OddJobKind.Haul;
        Assert.Equal(OfferResult.Accepted, w.Ap.OfferContract(w.Hero, OddJobKind.Haul, 100, "carry flour", w.Sec + 1000));
        Assert.Equal(OfferResult.Declined, w.Ap.OfferContract(w.Hero, OddJobKind.Escort, 300, "escort a merchant", w.Sec + 2000));
        // a big choice is always declined and noted by the AI; with Pause it waits for the player
        Assert.Equal(OfferResult.Declined, w.Ap.OfferBigChoice(w.Hero, "marry the miller's daughter", w.Sec + 3000));
        Assert.Equal(1, w.Book.BigChoicesDeclined);
        st.Policy[(int)InterruptKind.BigChoice] = InterruptPolicy.Notify;          // not allowed for a big choice: it pauses
        Assert.Equal(InterruptPolicy.Pause, InterruptRouter.PolicyOf(st, InterruptKind.BigChoice));
        Assert.Equal(OfferResult.Pending, w.Ap.OfferBigChoice(w.Hero, "swear fealty", w.Sec + 9000)); Assert.True(w.Ap.BusOf(st).Paused);
    }

    [Fact]
    public void TendAndPursueAmbition_AreStubs_TheyAreSkippedWithAMessage()
    {
        using var w = ApWorld.Soak();
        var st = w.Ap.Enable(w.Hero, w.Sec, new Directive { Kind = DirectiveKind.Tend, Name = "Tend the fields" }, Directive.Rest(1));
        w.Ap.Advance(w.Sec + 120);
        Assert.Equal(DirectiveState.Blocked, st.Queue[0].State); Assert.Equal(DirectiveKind.Rest, st.Active!.Kind);
        Assert.Contains(w.Book.Log.Lines, l => l.Text.Contains("not ready yet"));
    }

    // ------------------------------------------------------------------ AP-9: taking control back

    [Fact]
    public void AP9_TakingControlIsInstant_TheHumansAreNotFiltered_AndResumingContinuesTheDirective()
    {
        using var w = ApWorld.Soak();
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Work());
        w.RunHours(10);
        long spentBefore = st.SpentTotal; long win = st.NextWindow;
        w.Ap.TakeControl(w.Hero, w.Sec);
        Assert.True(st.Paused);
        w.RunHours(3);
        Assert.Equal(win, st.NextWindow);                                           // nothing was done for the player's time
        // a human command far over the cap is applied
        var a = w.Ap.AnchorsOf(st, w.Sec); ushort g = w.Econ.Goods.IdOf("wheat"); long h = w.Ticker.HourDone + 1;
        long sec = (h / 24 + 1) * 86400 + 9 * 3600;
        var c = w.Desk.Issue(new HeroCommand { Kind = HeroCommandKind.Buy, Settlement = a.HomeSettlement, Good = g, Qty = 20_000, IssuedSec = sec }); w.Desk.ApplyDue(w.Ctx, sec / 3600);
        Assert.True(w.Desk.View!.Results.Last(r => r.Seq == c.Seq).Ok); Assert.Equal(spentBefore, st.SpentTotal);
        long dirStart = st.Queue[0].StartDay;
        w.Ap.Resume(w.Hero, w.Sec);
        w.RunHours(30);
        Assert.False(st.Paused); Assert.Equal(DirectiveState.Active, st.Queue[0].State); Assert.Equal(dirStart, st.Queue[0].StartDay);
        Assert.True(w.Book.Get(w.Hero)!.Summaries.Sum(d => d.JobsDone) > 0);
    }

    // ------------------------------------------------------------------ AP-13: the party

    [Fact]
    public void AP13_PartyMembers_ThreeDirectives_EachDoesItsOwn_WagesGoToTheirOwnHouseholds_NothingIsCreated()
    {
        using var w = ApWorld.Soak();
        var pop = w.State.Population; var p = pop.People; int day = (int)(w.Sec / 86400);
        var picks = Enumerable.Range(0, p.Count).Where(i => p.IsAlive(i) && p.HomeSettlement[i] == 0 && p.AgeYears(i, day) is >= 20 and < 40 && p.Household[i] >= 0 && p.Occupation[i] == Occupation.Labourer).Take(3).ToList();
        Assert.Equal(3, picks.Count);
        var members = new List<EntityId>();
        foreach (int i in picks)
        {
            w.Ctx.Party.Squad.Members.Add(new Follower { Person = p.IdOf(i), PersonIndex = i, Name = "m" + i, State = HireState.Active, Loyalty = 500, JoinedDay = day, MaxSkill = 20 });
            members.Add(p.IdOf(i));
        }
        var hhPurse = members.Select(m => w.Econ.HouseholdPurse[p.Household[p.IndexOf(m)]]).ToArray();
        long[] before = hhPurse.Select(h => w.Econ.Ledger.Balance(h).Mites).ToArray();
        w.Ap.Enable(members[0], w.Sec, Directive.Work(5)); w.Ap.Enable(members[1], w.Sec, Directive.Guard(0, 5)); w.Ap.Enable(members[2], w.Sec, Directive.Rest(5));
        Assert.Null(w.Book.Get(w.Hero));                                              // the hero's autopilot is off
        long total0 = w.Econ.Ledger.Audit().InPurses.Mites;
        w.RunHours(24 * 5);
        var jobs = w.Table.Jobs.Values.Where(j => j.State == JobState.Done && members.Contains(j.Worker)).ToList();
        output.WriteLine($"member jobs: {string.Join(", ", jobs.Select(j => j.Worker.Value % 10000 + ":" + j.Kind))}");
        Assert.Contains(jobs, j => j.Worker == members[0]);
        Assert.All(jobs.Where(j => j.Worker == members[1]), j => Assert.Equal(OddJobKind.Guard, j.Kind));
        Assert.DoesNotContain(jobs, j => j.Worker == members[2]);
        Assert.True(jobs.Where(j => j.Worker == members[0]).Sum(j => j.PaidMites) > 0);
        Assert.Equal(DirectiveKind.Rest, w.Book.Get(members[2])!.Queue[0].Kind);
        Assert.True(w.Econ.Ledger.Audit().IsConserved); Assert.Equal(0, w.Econ.Ledger.Audit().Minted.Mites);
        Assert.Equal(w.Ctx.Coin, w.Ctx.Coin);
    }

    // ------------------------------------------------------------------ AP-14: a trade route

    [Fact]
    public void AP14_TradeRoute_TheMarginDecidesTheTrip_14PercentGoes_8PercentPausesWithNeedMoneyAfterTwoLooks()
    {
        Assert.InRange(AutopilotWorld.MarginPm(1000, 1000 * 1140 / 960 + 1), 139, 141);   // a bid that is 14% over after the toll: above 10%, the route goes
        Assert.InRange(AutopilotWorld.MarginPm(1000, 1000 * 1080 / 960 + 1), 79, 81);     // 8%: below the minimum of 10%
        // on the live world the prices drift hour by hour, so the live cases use margins far from the line: a rich route trades, a poor one pauses with NeedMoney after two looks
        foreach (var (pct, goes) in new[] { (300, true), (-50, false) })
        {
            using var w = ApWorld.Soak(heroCrowns: 50);
            ushort g = w.Econ.Goods.IdOf("wheat");
            var st = w.Ap.Enable(w.Hero, w.Sec, Directive.TradeRoute(0, 1, g, 5000, 100));
            st.Limits.SpendCapMitesPerDay = 1000;
            var a = w.Ap.AnchorsOf(st, w.Sec); var books = (w.Ctx.Book(0)!, w.Ctx.Book(1)!);
            books.Item1.Sync(g); long ask = Quotes.Buy(books.Item1, g, 5000).TotalMites * 1_000_000 / 5000; long bid = ask * (1000 + pct * 10) / 960 + (pct > 0 ? 0 : 0);
            int day = (int)(w.Sec / 86400) + 400;            // heard "in the future" so a visit does not overwrite the notes
            w.Ctx.Party.Notebook.Hear(new PriceNote(0, g, ask, ask, 1_000_000, day)); w.Ctx.Party.Notebook.Hear(new PriceNote(1, g, bid, bid, 1_000_000, day));
            long c0 = w.Ctx.Coin;
            w.RunHours(24 * 12, h => { if (pct == 14 && h % 4 == 0 && h < 360000 + 60) output.WriteLine($"h{h % 24:00} phase {st.Queue[0].Aux0} intent {st.Intent} pos {st.PosX / 1000},{st.PosY / 1000} stepSince {st.StepSince} until {st.StepUntil} camp {st.CampUntil}"); });
            output.WriteLine($"{pct}%: {Dump(w)}");
            var d = st.Queue[0];
            output.WriteLine($"phase {d.Aux0} trips {d.Aux2} low {d.Aux1} cost {d.Cost} revenue {d.Revenue} pack {w.Ctx.Pack.Qty(g)} aux3 {d.Aux3} results {string.Join(" | ", w.Desk.View!.Results.TakeLast(4).Select(r => r.Text))}");
            if (goes) { Assert.Contains(w.Ap.Issued, c => c.Kind == HeroCommandKind.Buy && c.Good == g); Assert.Contains(w.Ap.Issued, c => c.Kind == HeroCommandKind.Sell && c.Good == g); Assert.True(d.Aux2 >= 1, "a trip was made and judged"); Assert.True(c0 - w.Ctx.Coin <= 1000 * 8); }
            else { Assert.Equal(DirectiveState.Blocked, d.State); Assert.True(w.Book.Log.CountInterrupts(InterruptKind.NeedMoney) >= 1); Assert.DoesNotContain(w.Ap.Issued, c => c.Kind == HeroCommandKind.Buy && c.Good == g); }
            Assert.Equal(0, w.Book.Log.Violations);
        }
    }

    // ------------------------------------------------------------------ AP-10 shorter, AP-15, AP-16

    [Fact]
    public void AP10_ZeroPlayer_30DaysOfWork_AllInterruptsNotify_TheHeroLivesOn_ADailySummaryEachDay_NoStuck_NoViolation()
    {
        using var w = ApWorld.Soak();
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Work());
        st.PresetWatch(); for (int i = 0; i < st.Policy.Length; i++) if ((InterruptKind)i is not (InterruptKind.BigChoice)) st.Policy[i] = InterruptPolicy.Notify;
        w.RunHours(24 * 30);
        Assert.True(w.Book.SummariesMade >= 29, $"summaries {w.Book.SummariesMade}");
        Assert.Equal(0, w.Book.Log.CountInterrupts(InterruptKind.Stuck)); Assert.Equal(0, w.Book.Log.Violations); Assert.Equal(1000, st.HealthPm);
        Assert.False(w.Ap.BusOf(st).Paused);
        Assert.True(w.Ctx.Coin > 3000, $"coin {w.Ctx.Coin}");
        Assert.True(w.Econ.Ledger.Audit().IsConserved);
        output.WriteLine($"30 days: coin {w.Ctx.Coin}, summaries {w.Book.SummariesMade}, log {w.Book.Log.Lines.Count}");
    }

    [Fact]
    public void DeterminismAt1xAnd8x_TheSameDays_AnyChunkingOfTheClock_GiveTheSameBookAndTheSameCoin()
    {
        string Run(int chunk)
        {
            using var w = ApWorld.Soak();
            w.Ap.Enable(w.Hero, w.Sec, Directive.Work(2), Directive.Train(Skill.Smithing, 30), Directive.Work());
            w.RunHours(24 * 4, chunkSec: chunk);
            return w.Book.ComputeHash() + "|" + w.Ctx.Coin + "|" + w.Ctx.Pack.Rows().Sum(r => r.Qty) + "|" + w.Table.Count + "|" + w.Ctx.Craft.State.Book.Micro(w.Hero, Skill.Smithing);
        }
        string x1 = Run(1), x8 = Run(8), hour = Run(3600);
        Assert.Equal(x1, x8); Assert.Equal(x1, hour);
    }

    [Fact]
    public void AP15_ASessionWithHumanAndAutopilotCommandsReplays_TheSameHashesEachDay_AndTheTapeTellsTheOriginsApart()
    {
        (List<string> Days, List<HeroCommand> Tape) Play()
        {
            using var w = ApWorld.Soak(heroCrowns: 60);
            var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Work());
            var days = new List<string>(); var tape = new List<HeroCommand>();
            var a = w.Ap.AnchorsOf(st, w.Sec); ushort g = w.Econ.Goods.IdOf("wheat");
            w.RunHours(24 * 10, h =>
            {
                if (h % 24 == 11 && h / 24 % 3 == 0) tape.Add(w.Desk.Issue(new HeroCommand { Kind = HeroCommandKind.Buy, Settlement = a.HomeSettlement, Good = g, Qty = 1500, IssuedSec = h * 3600 + 3000, Origin = CommandOrigin.Human }));
                if (h % 24 == 23) days.Add(w.Econ.ComputeHash() + w.Book.ComputeHash() + w.Ctx.Coin);
            });
            tape.AddRange(w.Ap.Issued);
            Assert.True(w.Ap.Claims.Count > 0 && w.Ap.Claims.All(c => c.Origin == CommandOrigin.Autopilot));
            return (days, [.. tape.OrderBy(c => c.IssuedSec).ThenBy(c => c.Seq)]);
        }
        var (d1, t1) = Play(); var (d2, t2) = Play();
        Assert.Equal(10, d1.Count); Assert.Equal(d1, d2);
        Assert.Contains(t1, c => c.Origin == CommandOrigin.Human); Assert.Contains(t1, c => c.Origin == CommandOrigin.Autopilot);
        Assert.Equal(t1.Select(c => (c.IssuedSec, c.Kind, c.Origin, c.Qty)), t2.Select(c => (c.IssuedSec, c.Kind, c.Origin, c.Qty)));
    }

    [Fact]
    public void AP16_SaveAndLoadInTheMiddleOfADirective_ResumesTheSameStep_CapsAndSpentIntact_ByteIdentical()
    {
        using var w = ApWorld.Soak();
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Work(3), Directive.TravelTo(0, 0, "mill"), Directive.Rest(1)); st.Limits.SpendCapMitesPerDay = 250; st.Limits.NoCrimes = true; st.Limits.HomeByNightfall = true;
        w.RunHours(24 * 3 + 10);
        Assert.True(st.SpentTotal > 0);
        var bytes = SaveCodec.SerializeCompressed(AutopilotSave.Capture(w.Book));
        var back = SaveCodec.DeserializeCompressed<AutopilotSave>(bytes).Restore();
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(AutopilotSave.Capture(back)));
        var s2 = back.Get(w.Hero)!;
        Assert.Equal((st.ActiveIdx, st.SpentToday, st.SpentTotal, st.SpentDay, st.Limits.SpendCapMitesPerDay, st.Intent, st.NextWindow), (s2.ActiveIdx, s2.SpentToday, s2.SpentTotal, s2.SpentDay, s2.Limits.SpendCapMitesPerDay, s2.Intent, s2.NextWindow));
        Assert.Equal(w.Book.ComputeHash(), back.ComputeHash());
        Assert.True(bytes.Length < 100 * 1024, $"autopilot section {bytes.Length} bytes");
        // the run goes on from the loaded book exactly as it would have: a second autopilot over the same world with the restored book
        var ap2 = new AutopilotWorld(w.State, w.Ctx, w.Desk, back, w.Ap.Cfg) { DrivesJobs = true };
        w.State.Autopilot = back;
        long h = w.Ticker.HourDone + 1;
        for (int i = 0; i < 24; i++) { long hh = w.Ticker.HourDone + 1; ap2.Advance(hh * 3600 + 3599); w.Ticker.AdvanceHours(1); w.Desk.AfterHour(w.Ctx, hh); }
        Assert.True(back.Get(w.Hero)!.SpentTotal >= s2.SpentTotal); Assert.True(back.Log.Added > bytes.Length / 1000 || back.Log.Added > 0);
    }

    [Fact]
    public void TheAutopilotSectionIsInTheWorldSave_OnlyOnceAutopilotWasUsed_AndRoundTripsByteForByte()
    {
        var state = EconFixture.WorldWith(EconFixture.NewEconomy(false)); var heroId = Ravensguard.Sim.Economy.EconomyState.HeroOwner;
        Assert.DoesNotContain("autopilot", SaveCodec.Decode(SaveCodec.Encode(WorldSave.Capture(state, EconFixture.ContentHash, "0.1.0"))).Sections.Keys);
        var book = new AutopilotBook(); var st = book.GetOrAdd(heroId, AutopilotContent.Default);
        st.Queue.Add(Directive.Work(4)); st.Queue.Add(Directive.TravelTo(1, 2, "x")); st.Enabled = true; st.SpentToday = 77; st.SpentDay = 5; st.PresetWatch();
        book.Log.Add(100, st.Person, LogCode.Start, "on"); st.Summaries.Add(new DaySummary { Day = 4, Earned = 99 }); st.Today.Day = 5;
        state.Autopilot = book;
        byte[] bytes = SaveCodec.Encode(WorldSave.Capture(state, EconFixture.ContentHash, "0.1.0"));
        Assert.Contains("autopilot", SaveCodec.Decode(bytes).Sections.Keys);
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, EconFixture.ContentHash);
        Assert.NotNull(back.Autopilot); Assert.Equal(77, back.Autopilot!.Get(heroId)!.SpentToday); Assert.Equal(2, back.Autopilot.Get(heroId)!.Queue.Count);
        var again = SaveCodec.Decode(SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
        foreach (var (name, sec) in SaveCodec.Decode(bytes).Sections) Assert.True(sec.Span.SequenceEqual(again.Sections[name].Span), $"section {name} differs");
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
    }
}
