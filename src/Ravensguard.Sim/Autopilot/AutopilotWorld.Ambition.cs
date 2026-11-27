using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Sim.Autopilot;

/// <summary>
/// P8-AT-2 (ambitions spec 3.2, 3.3, 5): the <c>PursueAmbition</c> directive and the hourly check of the hero's current step. The same <see cref="AmbitionPlanner"/> serves the hero and the companions under the directive
/// (a companion pursues the hero's step with its own coin and skills). Everything here is integer, keyed on the game second and the stable ids, so it gives the same answers at 1x and 8x and across a save.
/// </summary>
public sealed partial class AutopilotWorld
{
    private AmbitionTree? _tree;
    /// <summary>The ambition tree over the loaded content (built on first use; a test or a mod may set it).</summary>
    public AmbitionTree Tree { get => _tree ??= new AmbitionTree(Ctx.Content.All<AmbitionDef>()); set => _tree = value; }
    /// <summary>Fills facts the autopilot cannot read itself (fief rank, realm, works tier, relics, battles won ...): the host's sources, called after the built-in ones.</summary>
    public Action<EntityId, AmbitionFacts>? FactsHook { get; set; }
    /// <summary>The stall subject the person owns and can tend (<c>Directive.Tend</c>), if any; null: no <c>tend</c> means is open.</summary>
    public Func<EntityId, long?>? StallHook { get; set; }
    /// <summary>Carries out a one-shot means the autopilot has no desk for itself: a purchase (<c>buy</c>), a recruit, a theft. Returns whether it was done. Null: those means are not open. A purchase is booked against the spending cap here.</summary>
    public Func<EntityId, MeansDef, bool>? MeansHook { get; set; }

    /// <summary>The directive the person is acting on this window: the child of an active <c>PursueAmbition</c>, else the active one.</summary>
    private Directive? Eff(AutopilotState st) => st.Active is { Kind: DirectiveKind.PursueAmbition } && st.Child is { State: DirectiveState.Active } c ? c : st.Active;

    // ------------------------------------------------------------------ facts

    /// <summary>What the progress rules read of a person: coin, skills, followers, jobs done and the hero's items; the rest comes from <see cref="FactsHook"/>.</summary>
    public AmbitionFacts FactsOf(EntityId person)
    {
        var f = new AmbitionFacts(); var st = Book.Get(person);
        long coin = st != null ? CoinOf(st) : person == HeroId ? Ctx.Coin : 0;
        f.CoinMites = coin; f.CoinPence = coin / Coin.MitesPerCopper; f.WealthMites = coin;
        f.Contracts = st?.JobsDoneTotal ?? 0;
        foreach (var s in Enum.GetValues<Skill>()) if (s != Skill.None) f.Skills[HeroRecord.SkillId(s)] = Ctx.Craft.State.Book.Level(person, s);
        if (person == HeroId)
        {
            f.Followers = Ctx.Party.Squad.Members.Count(m => m.State == HireState.Active);
            if (Ctx.Hero != null) foreach (var it in Ctx.Hero.Inventory) f.Items[it.Shape] = f.Items.GetValueOrDefault(it.Shape) + it.Count;
        }
        FactsHook?.Invoke(person, f);
        return f;
    }

    // ------------------------------------------------------------------ the hourly check of the hero's step

    /// <summary>The goal of an ambition directive: the hero's current ambition, step and a key that changes when either does.</summary>
    private bool GoalOf(out AmbitionDef amb, out AmbitionStepDef step, out int key)
    {
        amb = null!; step = null!; key = 0;
        var h = Ctx.Hero; if (h == null || h.AmbitionId.Length == 0) return false;
        var a = Tree.Get(h.AmbitionId); if (a == null) return false;
        var s = h.CurrentStep(a); if (s == null) return false;
        amb = a; step = s; key = Tree.All.ToList().IndexOf(a) * 100 + h.Milestone + 1;
        return true;
    }

    /// <summary>
    /// Checks the hero's current step (once an hour while the hero is on autopilot): a step whose conditions hold is done; the last step done finishes the ambition, writes <c>ambition_done</c> (<c>great_ambition_done</c> for a great one),
    /// makes the offer and raises <c>AmbitionChoice</c>, which an AI-handled hero answers by picking the offered ambition its means score best. A hero with no ambition and an open offer picks again.
    /// </summary>
    public void CheckAmbition(AutopilotState st, long at)
    {
        var h = Ctx.Hero; if (h == null || st.Person != HeroId) return;
        int day = (int)(at / 86400);
        var amb = h.AmbitionId.Length > 0 ? Tree.Get(h.AmbitionId) : null;
        if (amb == null)
        {
            if (h.Offered.Count == 0) h.Offered.AddRange(Tree.OfferFor(h.AmbitionsDone.Select(d => d.Id).ToList(), FactsOf(HeroId)));
            if (h.Offered.Count > 0 && InterruptRouter.PolicyOf(st, InterruptKind.AmbitionChoice) == InterruptPolicy.Ai) ChooseAmbitionByScore(st, at);
            return;
        }
        var facts = FactsOf(HeroId);
        for (var step = h.CurrentStep(amb); step != null && AmbitionRules.StepDone(step, facts); step = h.CurrentStep(amb))
        {
            h.CompleteMilestone(amb);
            Book.Log.Add(at, HeroId, LogCode.Note, $"Step done: {step.Name} ({amb.Name})");
        }
        if (h.Milestone < amb.Steps.Count) return;
        var offer = Tree.Offer(amb.Id, h.AmbitionsDone.Select(d => d.Id).ToList(), null, facts);
        h.FinishAmbition(day, offer);
        AmbitionEntry(amb.Tier == "great" ? "great_ambition_done" : "ambition_done", at, amb.Id);
        Book.Log.Add(at, HeroId, LogCode.Note, $"Ambition done: {amb.Name}");
        if (offer.Count > 0) Raise(st, InterruptKind.AmbitionChoice, at, $"{amb.Name} is done: {offer.Count} to choose from");
    }

    /// <summary>The AI's answer to <c>AmbitionChoice</c> (spec 3.1): the offered ambition whose first step the planner scores best under the limits; none scoring leaves the offer open ("none for now").</summary>
    public bool ChooseAmbitionByScore(AutopilotState st, long at)
    {
        var h = Ctx.Hero; if (h == null || h.Offered.Count == 0) return false;
        var facts = FactsOf(HeroId); string pick = ""; int best = 0;
        foreach (var id in h.Offered)
        {
            var a = Tree.Get(id); if (a == null || a.Steps.Count == 0) continue;
            int sc = BestScore(st, a.Steps[0], facts, at);
            if (sc > best) { best = sc; pick = id; }
        }
        if (pick.Length == 0) { Book.Log.Add(at, HeroId, LogCode.Note, "No offered ambition has a way the AI can take: none for now"); return false; }
        h.ChooseAmbition(pick);
        AmbitionEntry("ambition_chosen", at, pick);
        Book.Log.Add(at, HeroId, LogCode.Note, $"Chose the ambition {Tree.Get(pick)!.Name} (score {best})");
        return true;
    }

    private int BestScore(AutopilotState st, AmbitionStepDef step, AmbitionFacts facts, long at)
    {
        var cond = AmbitionPlanner.Limiting(step, facts); if (cond == null) return 1000;
        return AmbitionPlanner.Best(AmbitionPlanner.Candidates(Cfg.Means, cond, PlanInputOf(st, at, facts)))?.Score ?? 0;
    }

    private void AmbitionEntry(string type, long at, string ambition)
    {
        var data = new Dictionary<string, string> { ["autopilot"] = "1", ["ambition"] = ambition };
        if (ChronicleHook != null) { ChronicleHook(HeroId, type, at, data); return; }
        State.Chronicle.Add(new ChronicleEntry(State.Ids.Allocate(IdKind.Event), new GameTime(at), type, [HeroId], [], data, type switch { "ambition_chosen" => "ch_p8_ambition_chosen", "great_ambition_done" => "ch_p8_great_ambition_done", _ => "ch_p8_ambition_done" }, [ChronParam.Person("person", HeroId)]));
    }

    // ------------------------------------------------------------------ the planner at work

    private PlanInput PlanInputOf(AutopilotState st, long at, AmbitionFacts facts)
    {
        bool? route = null;
        bool Open(MeansDef m, ConditionRefDef c) => m.Directive switch
        {
            "work" or "contract" => true,
            "train" => c.Args.TryGetValue("skill", out var sk) && Enum.TryParse<Skill>(sk.Replace("_", ""), true, out var skill) && Cfg.Activities.Knows(skill),
            "tend" => StallHook?.Invoke(st.Person) != null,
            "trade_route" => route ??= BestRoute() != null,
            "buy" or "recruit" or "steal" => MeansHook != null,
            _ => false,
        };
        return new PlanInput
        {
            Limits = st.Limits, State = st, Day = (int)(at / 86400), Purse = CoinOf(st), SkillOf = s => facts.Skills.GetValueOrDefault(s), Rates = st.Rates, Available = Open, RiskWeightPm = Settings.AmbitionRiskWeightPm,
        };
    }

    /// <summary>The best trade the hero's notebook shows: buy at A, sell at B, a margin of at least 10% (100 pm) after the toll; null when none. Ties go to the earlier note.</summary>
    private (int A, int B, ushort Good, int Margin)? BestRoute()
    {
        var notes = Ctx.Party.Notebook.Notes.ToList(); (int A, int B, ushort Good, int Margin)? best = null;
        foreach (var a in notes)
        {
            if (a.AskMicro <= 0 || a.StockUnits <= 0) continue;
            foreach (var b in notes)
            {
                if (b.Good != a.Good || b.Book == a.Book || b.BidMicro <= 0) continue;
                int m = MarginPm(a.AskMicro, b.BidMicro);
                if (m >= 100 && (best == null || m > best.Value.Margin)) best = (a.Book, b.Book, a.Good, m);
            }
        }
        return best;
    }

    /// <summary>Folds one game day's progress into a means' running rate (spec 3.5).</summary>
    public void Learn(AutopilotState st, string meansId, int dayDelta)
    {
        int cur = st.Rates.TryGetValue(meansId, out int r) ? r : Cfg.Means.FirstOrDefault(m => m.Id == meansId)?.PriorPmDay ?? 0;
        st.Rates[meansId] = AmbitionPlanner.NextRate(cur, dayDelta);
    }

    /// <summary>
    /// Runs while a <c>PursueAmbition</c> directive is active: keeps its child's life (starting it, ending it when its time is up), and plans at dawn each game day and when the child has ended: the lowest condition of the
    /// goal step is the one pushed, the best means that breaks no limit runs as the child for at least a day, and nothing scoring for 3 game days (or no progress for 10) raises <c>AmbitionStuck</c> with the reason.
    /// The parent's <c>Params</c>: 0 the day last planned (+1), 1 days with nothing scoring, 2 progress at the last dawn, 3 the day progress last rose (+1); <c>Aux0</c> the best progress seen, <c>Aux1</c> the goal's key (+1).
    /// </summary>
    private void PursueTick(AutopilotState st, Directive p, long at, long coin)
    {
        int day = (int)(at / 86400);
        if (p.State == DirectiveState.Active) p.State = DirectiveState.SubDirective;
        if (st.Child is { } c)
        {
            if (c.State == DirectiveState.Pending) StartDirective(st, c, at, coin);
            if (c.State == DirectiveState.Active && UntilMet(st, c, at, coin)) { c.State = DirectiveState.Done; Book.Log.Add(at, st.Person, LogCode.Directive, $"Finished for now: {c.Name}"); }
            if (c.State is DirectiveState.Done or DirectiveState.Failed or DirectiveState.Blocked) st.Child = null;
        }
        if (!GoalOf(out var amb, out var step, out int key))
        {
            bool picked = Ctx.Hero is { Offered.Count: > 0 } && InterruptRouter.PolicyOf(st, InterruptKind.AmbitionChoice) == InterruptPolicy.Ai && ChooseAmbitionByScore(st, at);
            if (!picked || !GoalOf(out amb, out step, out key))
            {
                p.State = DirectiveState.Blocked; st.Child = null; st.ChildMeans = ""; st.Why = "";
                Book.Log.Add(at, st.Person, LogCode.Directive, "Pursue ambition: no ambition step to pursue: skipped");
                Raise(st, InterruptKind.DirectiveDone, at, $"{p.Name}: nothing to pursue");
                return;
            }
        }
        bool dawn = p.Params[0] != day + 1, ended = st.Child == null && st.ChildMeans.Length > 0;
        if (!dawn && !ended) return;
        var facts = FactsOf(st.Person);
        int prog = AmbitionRules.StepProgress(step, facts);
        if (p.Aux1 != key + 1) { p.Aux1 = key + 1; p.Aux0 = prog; p.Params[2] = prog; p.Params[3] = day + 1; p.Params[1] = 0; }       // a new goal: the trackers start again
        if (dawn)
        {
            if (st.ChildMeans.Length > 0) Learn(st, st.ChildMeans, prog - (int)p.Params[2]);
            p.Params[2] = prog;
            if (prog > p.Aux0) { p.Aux0 = prog; p.Params[3] = day + 1; }
            else if (day + 1 - p.Params[3] >= Settings.AmbitionStillDays)
            {
                p.Params[3] = day + 1;
                if (StuckRaise(st, p, amb, $"progress has not moved in {Settings.AmbitionStillDays} days", at)) return;
            }
        }
        p.Params[0] = day + 1;
        var cond = AmbitionPlanner.Limiting(step, facts);
        if (cond == null) { st.Why = p.Name = $"Pursuing {amb.Name}: {step.Name} is done"; return; }
        var cands = AmbitionPlanner.Candidates(Cfg.Means, cond, PlanInputOf(st, at, facts));
        var best = AmbitionPlanner.Best(cands);
        if (best == null)
        {
            st.Child = null; st.ChildMeans = "";
            if (dawn) p.Params[1]++;
            string reason = AmbitionPlanner.Reason(cands);
            st.Why = p.Name = $"Pursuing {amb.Name}: {reason}";
            if (p.Params[1] >= Settings.AmbitionZeroDays) { p.Params[1] = 0; StuckRaise(st, p, amb, reason, at); }
            return;
        }
        p.Params[1] = 0;
        int ways = cands.Count(x => x.Bar == MeansBar.None);
        var m = best.Value.Means;
        if (st.Child == null || st.ChildMeans != m.Id)
        {
            if (!StartMeans(st, m, cond, amb, at, coin)) { st.Child = null; st.ChildMeans = ""; return; }
        }
        st.Why = p.Name = $"Pursuing {amb.Name}: {Verb(m, cond)} (best of {ways} ways)";
        p.State = DirectiveState.SubDirective;
    }

    private bool StuckRaise(AutopilotState st, Directive p, AmbitionDef amb, string reason, long at)
    {
        var r = Raise(st, InterruptKind.AmbitionStuck, at, $"{amb.Name}: {reason}");
        Book.Log.Add(at, st.Person, LogCode.Note, $"Stuck on {amb.Name}: {reason}");
        if (r.Response != AiResponse.NextDirective) return false;
        p.State = DirectiveState.Blocked; st.Child = null; st.ChildMeans = "";
        return true;
    }

    private static string Verb(MeansDef m, ConditionRefDef cond) => m.Directive switch
    {
        "work" => "working for coin",
        "trade_route" => "trading between the towns",
        "tend" => "tending your stall",
        "train" => "training " + (cond.Args.TryGetValue("skill", out var s) ? s.Replace('_', ' ') : "a skill"),
        "contract" => "taking contracts",
        "buy" => "saving up to buy",
        "recruit" => "looking for followers",
        _ => m.Id.Replace('_', ' '),
    };

    /// <summary>Starts the chosen means as the child: the directive it stands for, or a one-shot through <see cref="MeansHook"/> (then the child is the day's work). Returns false when it could not start.</summary>
    private bool StartMeans(AutopilotState st, MeansDef m, ConditionRefDef cond, AmbitionDef amb, long at, long coin)
    {
        Directive? d = null;
        switch (m.Directive)
        {
            case "work": d = Directive.Work(1, "Work for coin"); break;
            case "contract": d = Directive.Work(1, "Take contracts"); break;
            case "tend": if (StallHook?.Invoke(st.Person) is { } stall) d = Directive.Tend(stall, 1); break;
            case "train":
                if (cond.Args.TryGetValue("skill", out var sk) && Enum.TryParse<Skill>(sk.Replace("_", ""), true, out var skill) && int.TryParse(cond.Args.GetValueOrDefault("level", "0"), out int lv))
                    d = Directive.Train(skill, lv);
                break;
            case "trade_route": if (BestRoute() is { } r) { d = Directive.TradeRoute(r.A, r.B, r.Good, 20_000, 100); d.Name = "Trade route for the ambition"; } break;
            case "buy" or "recruit" or "steal":
                if (MeansHook == null) return false;
                int day = (int)(at / 86400);
                if (m.SpendMites > 0 && LimitGuard.CheckSpend(st.Limits, st, day, coin, m.SpendMites) != LimitVerdict.Allowed) return false;
                if (MeansHook(st.Person, m))
                {
                    if (m.SpendMites > 0) { LimitGuard.Commit(st, day, m.SpendMites); st.Today.Spent += m.SpendMites; }
                    if (m.Tags.Contains("crime")) Book.Log.Crimes++;
                    Book.Log.Add(at, st.Person, LogCode.Action, $"Ambition means: {m.Id}");
                }
                d = Directive.Work(1, "Work for coin");
                break;
        }
        if (d == null) return false;
        st.Child = d; st.ChildMeans = m.Id;
        StartDirective(st, d, at, coin);
        Book.Log.Add(at, st.Person, LogCode.Directive, $"Ambition {amb.Name}: means {m.Id}");
        return true;
    }
}
