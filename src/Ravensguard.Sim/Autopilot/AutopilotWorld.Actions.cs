using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Autopilot;

public sealed partial class AutopilotWorld
{
    /// <summary>Headroom in permille a purchase order allows over its quote (the market moves in the hour before the barrier applies it): 1150 = 15%.</summary>
    public const int SlipPm = 1150;
    private int LatTenths => SunCache.LatitudeOfRow(State.Population.Region.CellY);
    private static int MinuteOfDay(long at) => (int)(at % 86400 / 60);
    private static int DoY(long at) => new GameTime(at).DayOfYear;
    private bool IsNight(long at) { int m = MinuteOfDay(at), doy = DoY(at); return m < Daylight.SunriseMinute(LatTenths, doy) || m >= Daylight.SunsetMinute(LatTenths, doy); }

    // ------------------------------------------------------------------ directives

    private Directive? EnsureActive(AutopilotState st, long at, long coin)
    {
        for (int guard = 0; guard < 12; guard++)
        {
            var d = st.Active; if (d == null) return null;
            if (d.State == DirectiveState.Pending) StartDirective(st, d, at, coin);
            if (d.State is DirectiveState.Done or DirectiveState.Failed or DirectiveState.Blocked) { st.ActiveIdx++; st.ReplanCount = 0; continue; }
            if (UntilMet(st, d, at, coin)) { FinishDirective(st, d, at, "done"); continue; }
            return d;
        }
        return st.Active;
    }

    private void StartDirective(AutopilotState st, Directive d, long at, long coin)
    {
        d.StartDay = (int)(at / 86400); d.StartCoin = coin; d.State = DirectiveState.Active; st.LastProgressSec = at;
        Book.Log.Add(at, st.Person, LogCode.Directive, $"Directive: {d.Name} ({d.Kind})");
        if (d.Kind is DirectiveKind.Tend or DirectiveKind.PursueAmbition)
        {
            d.State = DirectiveState.Blocked;
            Book.Log.Add(at, st.Person, LogCode.Directive, $"{d.Kind} is not ready yet (a later phase): skipped");
            Raise(st, InterruptKind.DirectiveDone, at, $"{d.Name}: blocked");
        }
    }

    private void FinishDirective(AutopilotState st, Directive d, long at, string why)
    {
        d.State = DirectiveState.Done; st.LastProgressSec = at;
        Book.Log.Add(at, st.Person, LogCode.Directive, $"Directive finished: {d.Name} ({why})");
        Raise(st, InterruptKind.DirectiveDone, at, d.Name);
    }

    private bool UntilMet(AutopilotState st, Directive d, long at, long coin) => d.Until switch
    {
        UntilKind.Days => at / 86400 - d.StartDay >= d.UntilValue,
        UntilKind.CoinAtLeast => coin >= d.UntilValue,
        UntilKind.SkillAtLeast => SkillLevel(st.Person, (Skill)d.UntilValue2) >= d.UntilValue,
        UntilKind.Healed => st.HealthPm >= 1000 && !st.Bleeding || (d.Kind == DirectiveKind.Rest && at - d.StartDay * 86400L >= Settings.RestMaxHours * 3600L),
        _ => false,
    };

    private int SkillLevel(EntityId person, Skill s) => Ctx.Craft.State.Book.Level(person, s);

    /// <summary>Raises an interrupt for the person through the router (policy, log, chronicle) and applies what the brain answers when the policy is <c>Ai</c>.</summary>
    public RouteResult Raise(AutopilotState st, InterruptKind kind, long at, string text = "")
    {
        var r = Book.Router.Raise(st, BusOf(st), kind, at, st.PosX, st.PosY, text);
        if (!r.Duplicate && r.Response != AiResponse.None) ApplyAi(st, r.Response, at);
        return r;
    }

    private void ApplyAi(AutopilotState st, AiResponse resp, long at)
    {
        switch (resp)
        {
            case AiResponse.SwitchToWork:
            case AiResponse.SwitchToEarning:
                if (st.Active is not { Kind: DirectiveKind.Work })
                {
                    var w = Directive.Work(0, "Work (earn)"); w.Until = UntilKind.CoinAtLeast; w.UntilValue = Math.Max(st.Limits.PurseReserveMites * 2, st.Limits.PurseReserveMites + 100);
                    st.Queue.Insert(Math.Min(Math.Max(0, st.ActiveIdx), st.Queue.Count), w);
                }
                break;
            case AiResponse.IdleHome: st.CampUntil = (at / 86400 + 1) * 86400; break;      // idle at home; retry next day
            case AiResponse.MakeCamp: st.CampUntil = Math.Max(st.CampUntil, at + 6 * 3600); break;
            case AiResponse.Bandage: st.Bleeding = false; st.CampUntil = Math.Max(st.CampUntil, at + 2 * 3600); break;
            case AiResponse.EatBuyOrForage: if (st.Person == HeroId) { if (!TryEat(st, at)) Provision(st, at, urgent: true); } break;
            case AiResponse.ReplanOnce: st.ReplanCount++; st.LastProgressSec = at; break;
        }
    }

    // ------------------------------------------------------------------ interrupts from the person's own state

    private void Edge(AutopilotState st, InterruptKind kind, bool on, long at, string text)
    {
        uint bit = 1u << (int)kind;
        bool was = (st.EdgeBits & bit) != 0;
        if (on && !was) { st.EdgeBits |= bit; Raise(st, kind, at, text); }
        else if (!on && was) st.EdgeBits &= ~bit;
    }

    private void RaiseStateInterrupts(AutopilotState st, long at, long coin)
    {
        Edge(st, InterruptKind.Injured, st.HealthPm <= 850 || st.Bleeding, at, st.Bleeding ? "Bleeding" : "Hurt");
        Edge(st, InterruptKind.Threatened, st.ThreatPm > 0, at, "A threat is near");
        Edge(st, InterruptKind.LowMoney, st.Limits.PurseReserveMites > 0 && coin < st.Limits.PurseReserveMites, at, $"Purse {Quotes.Money(coin)}");
        if (st.Person == HeroId)
        {
            bool starving = st.LastMealSec != long.MinValue && at - st.LastMealSec >= Brain.HungerEatHours * 3600L && PackFoodDays() < 0.34;
            Edge(st, InterruptKind.Hunger, starving || PackFoodDays() < 1.0, at, starving ? "Time to eat" : "Food is running low");
        }
    }

    private void CheckStuck(AutopilotState st, Directive? d, long at)
    {
        if (d == null || d.Kind == DirectiveKind.Rest) { st.LastProgressSec = at; return; }
        if (st.LastProgressSec == 0) st.LastProgressSec = at;
        if (at - st.LastProgressSec < Settings.StuckGameHours * 3600L) return;
        st.LastProgressSec = at;
        Raise(st, InterruptKind.Stuck, at, $"No progress for {Settings.StuckGameHours} h");
    }

    // ------------------------------------------------------------------ the policy

    private ActionPolicy? _policy;
    private ActionPolicy BuildPolicy(AutopilotState st, Directive? d, long at, long coin, int day, out bool neutral)
    {
        var p = _policy ??= new ActionPolicy(); p.Clear();
        int bias = Settings.DirectiveBiasPm;
        if (d != null)
            switch (d.Kind)
            {
                case DirectiveKind.Work or DirectiveKind.Guard or DirectiveKind.Train:
                    p.Bias(ActionKind.TakeJob, bias); p.Bias(ActionKind.Work, bias); p.Forbid(ActionKind.Socialise); p.Forbid(ActionKind.Worship); break;
                case DirectiveKind.Rest:
                    p.Forbid(ActionKind.Work); p.Forbid(ActionKind.TakeJob); p.Forbid(ActionKind.Socialise); p.Forbid(ActionKind.Worship); p.Bias(ActionKind.Idle, bias); break;
            }
        if (st.CampUntil > at) { p.Forbid(ActionKind.Work); p.Forbid(ActionKind.TakeJob); p.Bias(ActionKind.Idle, bias); }
        LimitGuard.Filter(st.Limits, p, LimitGuard.AllowanceLeft(st.Limits, st, day) > 0);
        neutral = p.IsNeutral;
        return p;
    }

    private ActionPolicy? BuildPolicy(AutopilotState st, Directive? d, long at, long coin, int day)
    {
        var p = BuildPolicy(st, d, at, coin, day, out bool neutral);
        return neutral ? null : p;
    }

    private long _boardKey = long.MinValue; private bool _boardHas;
    private bool BoardHere(in PersonAnchors a, Directive? d)
    {
        var jt = Jobs?.Invoke(); if (jt == null) return false;
        long key = _boardClock / 300;
        if (key != _boardKey) { _boardKey = key; _boardHas = jt.Value.Table.Jobs.Values.Any(j => j.State == JobState.Open); }
        return _boardHas;
    }
    private long _boardClock;

    private bool TrainingByPractice(AutopilotState st, Directive d, long at)
        => d.Kind == DirectiveKind.Train && Cfg.Activities.CanPractise((Skill)d.Target, SkillLevel(st.Person, (Skill)d.Target));

    // ------------------------------------------------------------------ acting

    private void Execute(AutopilotState st, Directive? d, in PersonAnchors a, ActionKind kind, long at, in DecisionResult dr)
    {
        _boardClock = at;
        if (st.Limits.HomeByNightfall && d is not { Kind: DirectiveKind.Rest } && LimitGuard.HeadHomeNow(LatTenths, DoY(at), MinuteOfDay(at)) && JobOfPerson(st.Person) == null && Far(st, st.Limits.HomeX, st.Limits.HomeY, 60_000))
        { Walk(st, st.Limits.HomeX, st.Limits.HomeY, at); return; }
        if (d is { Kind: DirectiveKind.Rest } || st.CampUntil > at) { Rest(st, a, at); if (kind == ActionKind.Eat) MaybeMeal(st, at); return; }
        switch (kind)
        {
            case ActionKind.JobStep: FollowJob(st, at); break;
            case ActionKind.Work or ActionKind.TakeJob: DoWork(st, d, a, kind, at); break;
            case ActionKind.Sleep: Rest(st, a, at); break;
            case ActionKind.Eat: MaybeMeal(st, at); break;
            case ActionKind.Travel: DoMove(st, d, at); break;
            case ActionKind.Flee: Flee(st, at); break;
            default: Loiter(st, a, at); break;
        }
    }

    private void DoExtra(AutopilotState st, TaggedAction x, long at)
    {
        if ((x.Tags & ActionTag.Crime) != 0) { Book.Log.Crimes++; Book.Log.Add(at, st.Person, LogCode.Action, $"Crime: {x.Name}"); if (st.Person == HeroId) st.LastMealSec = at; }
        else Book.Log.Add(at, st.Person, LogCode.Action, x.Name);
        st.Intent = ActionKind.Idle; st.Today.SecByAction[(int)ActionKind.Idle] += WindowSec;
    }

    private bool Far(AutopilotState st, long x, long y, long mm) => PathPlan.Dist(st.PosX - x, st.PosY - y) > mm;

    // ---- movement

    /// <summary>Walks one window toward a point at the jog (the game's hook moves the hero itself and reads the position back).</summary>
    private void Walk(AutopilotState st, long x, long y, long at)
    {
        if (PositionHook != null) { Wanted[st.Person] = (x, y); return; }
        long dx = x - st.PosX, dy = y - st.PosY, dist = PathPlan.Dist(dx, dy), step = (long)JogMmPerGs * WindowSec;
        long ox = st.PosX, oy = st.PosY;
        if (dist <= step) { st.PosX = x; st.PosY = y; }
        else { st.PosX += dx * step / dist; st.PosY += dy * step / dist; }
        long moved = PathPlan.Dist(st.PosX - ox, st.PosY - oy);
        if (moved > 0) { st.Today.DistanceM += moved / 1000; }
    }

    private void Teleport(AutopilotState st, long x, long y)
    {
        if (PositionHook != null) { Wanted[st.Person] = (x, y); return; }
        st.Today.DistanceM += PathPlan.Dist(x - st.PosX, y - st.PosY) / 1000; st.PosX = x; st.PosY = y;
    }

    private void Loiter(AutopilotState st, in PersonAnchors a, long at) => Walk(st, a.SquareX, a.SquareY, at);

    private void Rest(AutopilotState st, in PersonAnchors a, long at)
    {
        Walk(st, a.HomeX, a.HomeY, at);
        if (at % 360 < WindowSec && st.HealthPm < 1000) st.HealthPm++;
        if (st.Bleeding && at - st.IntentSince >= 7200) st.Bleeding = false;
        if (st.HealthPm < 1000 && at % 360 < WindowSec) st.LastProgressSec = at;
    }

    private void Flee(AutopilotState st, long at)
    {
        // away from the threat: toward home, the safest place the autopilot knows
        var a = AnchorsOf(st, at); Walk(st, a.HomeX, a.HomeY, at);
        if (JobOfPerson(st.Person) is { } j && Jobs?.Invoke() is { } jt) jt.Table.Abandon(jt.Env, j.Id, at);
    }

    // ---- jobs

    private Job? JobOfPerson(EntityId person) => Jobs?.Invoke() is { } jt ? jt.Table.JobOf(person) : null;

    private void TrackJob(AutopilotState st, long at)
    {
        var jt = Jobs?.Invoke(); if (jt == null) return;
        var live = jt.Value.Table.JobOf(st.Person);
        if (live != null) { if (st.CurrentJob == EntityId.None) { st.CurrentJob = live.Id; st.LastProgressSec = at; } return; }
        if (st.CurrentJob == EntityId.None) return;
        var j = jt.Value.Table.Get(st.CurrentJob); st.CurrentJob = EntityId.None; st.LastProgressSec = at;
        if (j == null) return;
        if (j.State == JobState.Done)
        {
            st.Today.JobsDone++; st.Today.Earned += j.PaidMites;
            Book.Log.Add(at, st.Person, LogCode.Job, $"Job done: {j.Title}, paid {Quotes.Money(j.PaidMites)}");
            if (j.PaidMites >= 1000) WriteChronicle(st.Person, "autopilot_job_paid", at, new Dictionary<string, string> { ["autopilot"] = "1", ["job"] = j.Title, ["pay"] = j.PaidMites.ToString() });
        }
        else Book.Log.Add(at, st.Person, LogCode.Job, $"Job {j.State}: {j.Title}");
    }

    private void FollowJob(AutopilotState st, long at)
    {
        var j = JobOfPerson(st.Person); if (j == null) return;
        var (x, y, _, _) = JobTable.PositionAt(j, at);
        Teleport(st, x, y);
        st.LastProgressSec = Math.Max(st.LastProgressSec, at - 3600);
    }

    private void DoWork(AutopilotState st, Directive? d, in PersonAnchors a, ActionKind kind, long at)
    {
        if (d is { Kind: DirectiveKind.Train } && TrainingByPractice(st, d, at)) { Practise(st, d, a, at); return; }
        if (JobOfPerson(st.Person) != null) { FollowJob(st, at); return; }
        var job = ChooseJob(st, d, a, kind, at, out bool limited);
        if (job != null) { Claim(st, job, at); return; }
        if (limited && st.LimitDay != (int)(at / 86400)) { st.LimitDay = (int)(at / 86400); Raise(st, InterruptKind.LimitReached, at, "No job fits the limits"); }
        Loiter(st, a, at);
    }

    private Job? ChooseJob(AutopilotState st, Directive? d, in PersonAnchors a, ActionKind kind, long at, out bool limited)
    {
        limited = false;
        var jt = Jobs?.Invoke(); if (jt == null) return null;
        int home = a.HomeSettlement, region = a.Region;
        bool npcRule = d == null;
        // the NPC rule: "gets round to it" once in eight minutes on average (the draw every NPC makes), the oldest posting first
        if (npcRule)
        {
            if (kind != ActionKind.TakeJob || DetRng.Range(a.Seed, at / 20, a.Id.Value, RngStreams.UtilityNoise, 99, 0, 24) != 0) return null;
            return jt.Value.Table.Jobs.Values.Where(j => j.State == JobState.Open && j.Settlement == home && j.Region == region && j.MinAge <= 18 && j.MinSkill == 0).OrderBy(j => j.PostedSec).ThenBy(j => j.Id).FirstOrDefault();
        }
        var dir = d!; Job? best = null; long bestRate = -1; int sunset = Daylight.SunsetMinute(LatTenths, DoY(at));
        foreach (var j in jt.Value.Table.Jobs.Values.Where(j => j.State == JobState.Open && j.Region == region && j.Settlement == (dir.Kind == DirectiveKind.Guard && dir.TargetKind == 1 ? (int)dir.Target : home)).OrderBy(j => j.Id))
        {
            if (j.MinAge > 25 || j.MinSkill > 20) continue;
            if (dir.Kind == DirectiveKind.Guard && j.Kind != OddJobKind.Guard) continue;
            if (!st.Limits.Accepts(j.Kind)) continue;
            if (PathPlan.Dist(st.PosX - j.P0, st.PosY - j.P1) > Settings.MaxCommuteM * 1000L) continue;
            // the limits: risk of the place at that hour, and getting home by nightfall
            var probe = JobProbe(j, at, st.PosX, st.PosY);
            long mid = (at + JobTable.EndSec(probe)) / 2;
            if (!LimitGuard.RiskOk(st.Limits, DangerAt(j.P0, j.P1, mid), IsNight(mid))) { limited = true; continue; }
            if (st.Limits.HomeByNightfall && JobTable.EndSec(probe) > at / 86400 * 86400 + sunset * 60L - Settings.HomeByNightfallOffsetMin * 60L) { limited = true; continue; }
            long dur = Math.Max(1, JobTable.EndSec(probe) - at), rate = j.WageMites * 3600 / dur;
            if (rate > bestRate) { best = j; bestRate = rate; }
        }
        return best;
    }

    private static Job JobProbe(Job j, long at, long x, long y)
    {
        var c = RuntimeJobs.Copy(j); c.ClaimedSec = at; c.StartX = x; c.StartY = y; return c;
    }

    private void Claim(AutopilotState st, Job j, long at)
    {
        if (Jobs?.Invoke() is not { } jt) return;
        int idx = st.Person == HeroId ? -1 : Ctx.Party.Squad.Find(st.Person)?.PersonIndex ?? -1;
        int age = idx >= 0 ? State.Population.People.AgeYears(idx, (int)(at / 86400)) : 25;
        int skill = Ctx.Party.Squad.Find(st.Person)?.MaxSkill ?? 20;
        var req = new ClaimRequest(j.Id, st.Person, idx, at, Claims.Count, st.PosX, st.PosY, skill, age) { Origin = CommandOrigin.Autopilot };
        Claims.Add(req); if (Claims.Count > 4000) Claims.RemoveRange(0, 2000);
        var res = jt.Table.ClaimAll(jt.Env, [req], at, State.Seed);
        if (res.Count > 0 && res[0].Result == ClaimResult.Taken)
        {
            st.CurrentJob = j.Id; st.LastProgressSec = at;
            Book.Log.Add(at, st.Person, LogCode.Job, $"Took a job: {j.Title} for {Quotes.Money(j.WageMites)}");
            jt.Table.Advance(jt.Env, at);
            Teleport(st, JobTable.PositionAt(j, at).X, JobTable.PositionAt(j, at).Y);
        }
    }

    // ---- training

    private void Practise(AutopilotState st, Directive d, in PersonAnchors a, long at)
    {
        Loiter(st, a, at);
        var skill = (Skill)d.Target; int level = SkillLevel(st.Person, skill);
        var use = Cfg.Activities.UsesFor(skill, level).FirstOrDefault(); if (use.Recipe == null) return;
        long every = 3600L / Math.Max(1, Settings.TrainUsesPerHour);
        if (at - st.LastWorkUseSec < every) return;
        st.LastWorkUseSec = at;
        var (outcome, gain) = Progress.Use(Ctx.Craft.State.Book, st.Person, new SkillUse(use.Recipe, skill, use.DX100, use.CapLevel), use.Difficulty, at);
        if (outcome == UseOutcome.Gained) { st.Today.SkillGainMilli += gain / 1000; st.LastProgressSec = at; }
    }

    // ------------------------------------------------------------------ food

    private double PackFoodDays()
    {
        var goods = Ctx.Econ.Goods; long ppm = 0;
        foreach (var (g, q) in Ctx.Pack.Rows()) { int f = goods.FoodMeasuresPpm(g); if (f > 0 && !goods.IsPiece(g)) ppm += q * f / 1000; }
        long day = (long)Cfg_MealMeasures() * 1000;
        return ppm / (double)day;
    }

    private int Cfg_MealMeasures() => (Travel?.Cfg.MealMeasuresPerDay) ?? 1000;

    /// <summary>Eats one third of a person-day from the pack (breakfast, dinner, supper). False when the pack has no food.</summary>
    private bool TryEat(AutopilotState st, long at)
    {
        if (st.Person != HeroId) return true;           // a party member is fed by their household
        var goods = Ctx.Econ.Goods; var store = Ctx.Pack;
        long needPpm = (long)Cfg_MealMeasures() * 1000 / 3, want = needPpm, ate = 0;
        foreach (var (g, qty) in store.Rows().Where(r => goods.FoodMeasuresPpm(r.Good) > 0 && !goods.IsPiece(r.Good)).OrderBy(r => r.Good).ToList())
        {
            if (needPpm <= 0) break;
            long ppmPerKg = goods.FoodMeasuresPpm(g), grams = (needPpm * 1000 + ppmPerKg - 1) / ppmPerKg, take = Math.Min(grams, qty);
            if (take <= 0 || !store.Destroy(g, take, DestroyReason.Eaten)) continue;
            ate += take; needPpm -= take * ppmPerKg / 1000;
        }
        if (ate <= 0) return false;
        st.LastMealSec = at; st.Today.Meals++; st.LastProgressSec = at;
        if (st.NutritionDay != (int)(at / 86400)) { st.NutritionDay = (int)(at / 86400); st.NutritionCd = 0; }
        st.NutritionCd += needPpm <= 0 ? (st.Today.Meals == 1 ? 334 : 333) : (want - needPpm) * 333 / Math.Max(1, want);
        if (Travel is { } tr) tr.LastMealSec = at;
        return true;
    }

    private void MaybeMeal(AutopilotState st, long at)
    {
        if (st.Person != HeroId || st.Today.Meals >= 3) return;                 // three meals a day: breakfast, dinner, supper
        if (st.LastMealSec != long.MinValue && at - st.LastMealSec < 3 * 3600) return;
        if (!TryEat(st, at)) Provision(st, at, urgent: true);
    }

    /// <summary>Chores that go with any action: a meal when a meal time has passed (the day's meal clock of <see cref="NeedModel"/>), and shopping for food when the pack is low and the market is open.</summary>
    private void Chores(AutopilotState st, in PersonAnchors a, long at, long coin, in DecisionResult dr)
    {
        if (st.Person != HeroId) return;
        NeedModel.Read(a, at, out var n, out _);
        if (n.HungerMilliHours + 1000 < st.LastHungerMilli) { if (!TryEat(st, at)) Provision(st, at, urgent: true); }
        st.LastHungerMilli = n.HungerMilliHours;
        int hour = (int)(at % 86400 / 3600); double days = PackFoodDays();
        bool low = days < 1.2, routine = hour is >= 7 and < 15 && days < 2.5;           // a routine top-up in the morning; a low pack is shopped for at any hour the stalls are open
        if ((routine || low && hour is >= 6 and < 18) && (st.LastShopSec == long.MinValue || at - st.LastShopSec >= (low ? 1800 : 3600)) && !_reserved.Values.Any(v => v.Person == st.Person)) Provision(st, at, urgent: low);
    }

    /// <summary>Buys food for a few days at the home market (the cheapest food per measure that the stalls hold), inside the limits. Issues a <c>Buy</c> with <c>Origin = Autopilot</c>; the barrier checks it again.</summary>
    private void Provision(AutopilotState st, long at, bool urgent)
    {
        if (_reserved.Values.Any(v => v.Person == st.Person)) return;                    // a purchase is already waiting for the barrier
        if (urgent && st.LastShopSec != long.MinValue && at - st.LastShopSec < 600) return;
        st.LastShopSec = at;
        int hour = (int)(at % 86400 / 3600), day = (int)(at / 86400);
        // the stalls the hero can reach: the settlement they stand in (within 2.5 km of its centre); on the road there is nobody to buy from
        int market = NearestSettlement(st, 2_500_000); if (market < 0) return;
        var book = Ctx.Book(market); if (book == null || !book.IsOpen(hour)) return;
        var goods = Ctx.Econ.Goods; long dayPpm = (long)Cfg_MealMeasures() * 1000;
        long coin = Ctx.Coin;
        for (int days = Settings.FoodDaysInPack; days >= 1; days--)
        {
            long needPpm = dayPpm * days; ushort bestG = ushort.MaxValue; long bestCost = long.MaxValue, bestQty = 0;
            for (ushort g = 0; g < goods.Count; g++)
            {
                int f = goods.FoodMeasuresPpm(g); if (f <= 0 || goods.IsPiece(g)) continue;
                long grams = (needPpm * 1000 + f - 1) / f;
                book.Sync(g);
                if (book.Goods[g].StockUnits < grams) continue;
                long cost = Quotes.Buy(book, g, grams).TotalMites; if (cost <= 0) continue;
                if (cost < bestCost) { bestCost = cost; bestG = g; bestQty = grams; }
            }
            if (bestG == ushort.MaxValue) continue;
            long maxPay = bestCost * SlipPm / 1000;
            var v = LimitGuard.CheckSpend(st.Limits, st, day, coin, maxPay);
            if (v != LimitVerdict.Allowed) { if (days == 1) NoteCannotBuy(st, v, at, bestCost); continue; }
            var cmd = IssueCmd(new HeroCommand { Kind = HeroCommandKind.Buy, Settlement = market, Good = bestG, Qty = bestQty, Limit = maxPay, IssuedSec = at, Origin = CommandOrigin.Autopilot });
            LimitGuard.Commit(st, day, maxPay); _reserved[cmd.Seq] = (st.Person, day, maxPay);
            Book.Log.Add(at, st.Person, LogCode.Action, $"Buying {days} days of food for {Quotes.Money(bestCost)}");
            return;
        }
    }

    /// <summary>The settlement whose centre is nearest to the person and within <paramref name="withinMm"/>, or -1.</summary>
    private int NearestSettlement(AutopilotState st, long withinMm, (long X, long Y)? at = null)
    {
        long px = at?.X ?? st.PosX, py = at?.Y ?? st.PosY;
        int best = -1; long bd = withinMm;
        var ss = State.Population.Region.Settlements;
        for (int i = 0; i < ss.Count; i++) { long d = PathPlan.Dist(px - ss[i].CenterX * 1000L, py - ss[i].CenterY * 1000L); if (d <= bd) { bd = d; best = i; } }
        return best;
    }

    private void NoteCannotBuy(AutopilotState st, LimitVerdict v, long at, long cost)
    {
        if (PackFoodDays() >= 0.34) return;
        if (v is LimitVerdict.OverDailyCap or LimitVerdict.OverTotalCap or LimitVerdict.BelowReserve) Raise(st, InterruptKind.LimitReached, at, $"Food costs {Quotes.Money(cost)}: {v}");
        else Raise(st, InterruptKind.NeedMoney, at, $"Food costs {Quotes.Money(cost)}");
    }

    // ------------------------------------------------------------------ the barrier: limits and settlement

    /// <summary>
    /// The execution check of autopilot commands (spec 4.3): called by the desk at the barrier for every command with <c>Origin = Autopilot</c>. A command that would break a limit is rejected with a <c>LimitViolation</c> result,
    /// logged and counted; nothing changes. The player's own commands never come here.
    /// </summary>
    private HeroResult? Gate(HeroContext ctx, HeroCommand c)
    {
        var st = Book.Get(ctx.Party.Hero);
        long cost = c.Kind switch
        {
            HeroCommandKind.Buy => ctx.Book(c.Settlement) is { } b && c.Good < b.Goods.Length && c.Qty > 0 ? Synced(b, c.Good, c.Qty) : 0,
            HeroCommandKind.Hire => c.Wage * Math.Max(1, c.Days),
            _ => 0,
        };
        if (c.Limit > 0) cost = Math.Min(cost, c.Limit);                      // above its own limit the desk refuses it anyway (the price moved): no spend, no violation
        if (st == null) return Violation(c, "no autopilot row", 0, null, 0);
        int day = (int)(c.IssuedSec / 86400);
        long res = 0;
        if (_reserved.TryGetValue(c.Seq, out var rv) && rv.Day == day && st.SpentDay == day) { res = rv.Mites; st.SpentToday -= res; st.SpentTotal -= res; }
        var v = cost > 0 ? LimitGuard.CheckSpend(st.Limits, st, day, ctx.Coin, cost) : LimitVerdict.Allowed;
        if (v != LimitVerdict.Allowed) { _reserved.Remove(c.Seq); return Violation(c, v.ToString(), cost, st, c.IssuedSec); }
        if (res > 0) { st.SpentToday += res; st.SpentTotal += res; }
        return null;
    }

    /// <summary>The quote the desk itself would get (it syncs the line to the market's present before quoting).</summary>
    private static long Synced(MarketBook b, ushort good, long qty) { b.Sync(good); return Quotes.Buy(b, good, qty).TotalMites; }

    private HeroResult Violation(HeroCommand c, string why, long cost, AutopilotState? st, long sec)
    {
        Book.Log.Violations++;
        Book.Log.Add(c.IssuedSec, st?.Person ?? EntityId.None, LogCode.Violation, $"LimitViolation: {c.Kind} {why} ({Quotes.Money(cost)}, qty {c.Qty}, good {c.Good})");
        return new HeroResult(c.Seq, c.Kind, HeroResultCode.Refused, "LimitViolation: " + why, cost, 0, c.IssuedSec);
    }

    private void Settled(HeroContext ctx, HeroCommand c, HeroResult r)
    {
        if (c.Origin != CommandOrigin.Autopilot) return;
        var st = Book.Get(ctx.Party.Hero); if (st == null) return;
        if (_reserved.Remove(c.Seq, out var rv))
        {
            if (r.Ok && c.Kind == HeroCommandKind.Buy)
            {
                long diff = r.Mites - rv.Mites;
                if (diff > 0) LimitGuard.Commit(st, rv.Day, diff); else if (diff < 0) LimitGuard.Refund(st, rv.Day, -diff);
                if (st.Today.Day == rv.Day) st.Today.Spent += r.Mites;
            }
            else LimitGuard.Refund(st, rv.Day, rv.Mites);
        }
        else if (r.Ok && c.Kind == HeroCommandKind.Buy && r.Mites > 0)
        {
            // a purchase nobody planned (a forced command): it still counts against the day
            LimitGuard.Commit(st, (int)(c.IssuedSec / 86400), r.Mites);
            if (st.Today.Day == (int)(c.IssuedSec / 86400)) st.Today.Spent += r.Mites;
        }
        if (st.Active is { Kind: DirectiveKind.TradeRoute } tr && r.Ok)
        {
            if (c.Good == (ushort)tr.Params[1]) { if (c.Kind == HeroCommandKind.Buy) tr.Cost += r.Mites; else if (c.Kind == HeroCommandKind.Sell) tr.Revenue += r.Mites; }
        }
        if (r.Ok && r.Mites >= 1000 && c.Kind is HeroCommandKind.Buy or HeroCommandKind.Sell)
            WriteChronicle(st.Person, "autopilot_deal", c.IssuedSec, new Dictionary<string, string> { ["autopilot"] = "1", ["kind"] = c.Kind.ToString(), ["mites"] = r.Mites.ToString() });
    }
}
