using Ravensguard.Content;
using Ravensguard.Sim.Characters;

namespace Ravensguard.Sim.Autopilot;

/// <summary>Why a means was ruled out (ambitions spec 3.3): <c>None</c> means it may be chosen.</summary>
public enum MeansBar : byte { None, Crime, Spend, Risk, Home, Skill, Unavailable, NoWay }

/// <summary>One means for the limiting condition, scored under the person's limits. <see cref="Score"/> is 0 when <see cref="Bar"/> is not <c>None</c>.</summary>
public readonly record struct MeansCandidate(MeansDef Means, int Order, int Rate, int Score, MeansBar Bar);

/// <summary>What the planner reads of the person and the limits (all plain numbers; no clock, no world).</summary>
public sealed class PlanInput
{
    public required LimitSet Limits { get; init; }
    public required AutopilotState State { get; init; }
    public int Day { get; init; }
    public long Purse { get; init; }
    /// <summary>The person's level in a skill (snake_case id).</summary>
    public Func<string, int> SkillOf { get; init; } = _ => 0;
    /// <summary>The learned rate of each means id in pm a day; a means not in it uses its content prior.</summary>
    public IReadOnlyDictionary<string, int> Rates { get; init; } = new Dictionary<string, int>();
    /// <summary>Whether the world has a way to carry the means out here and now (a route known, a stall owned, a desk for a purchase); null: every means with a directive can be.</summary>
    public Func<MeansDef, ConditionRefDef, bool>? Available { get; init; }
    public int RiskWeightPm { get; init; } = 500;
}

/// <summary>
/// P8-AT-2 (ambitions spec 3.3, 3.5): the pure part of the <c>PursueAmbition</c> planner. It picks which condition of the current step to push (the lowest in progress), lists the means of the table for it, rules out the ones that break a limit
/// (the spending cap, no crimes, the risk limit, home by nightfall) and scores the rest: <c>score = rate x weight_pm / 1000 - risk_pm x risk_weight / 1000</c>, all integers. The same function serves the hero, a companion and any NPC with an ambition.
/// </summary>
public static class AmbitionPlanner
{
    /// <summary>The running rate of a means, pm a day: <c>(3 x rate + day_delta + 2) / 4</c>, clamped to 0..1,000.</summary>
    public static int NextRate(int rate, int dayDelta) => Math.Clamp((3 * rate + Math.Clamp(dayDelta, 0, AmbitionRules.Full) + 2) / 4, 0, AmbitionRules.Full);

    public static int Score(int rate, int weightPm, int riskPm, int riskWeightPm) => rate * weightPm / 1000 - riskPm * riskWeightPm / 1000;

    /// <summary>The unmet condition of the step with the lowest progress (the first on a tie); null when the step is done.</summary>
    public static ConditionRefDef? Limiting(AmbitionStepDef step, AmbitionFacts f)
    {
        ConditionRefDef? best = null; int lo = AmbitionRules.Full;
        foreach (var c in step.Conditions)
        {
            int p = AmbitionRules.Progress(c, f);
            if (p < lo) { lo = p; best = c; }
        }
        return best;
    }

    /// <summary>The means for a condition, in content order, each scored under the limits.</summary>
    public static List<MeansCandidate> Candidates(IReadOnlyList<MeansDef> table, ConditionRefDef cond, PlanInput inp)
    {
        var list = new List<MeansCandidate>();
        for (int i = 0; i < table.Count; i++)
        {
            var m = table[i];
            if (m.Condition != cond.Id) continue;
            if (m.Directive == "train" && cond.Args.TryGetValue("skill", out var sk) && inp.SkillOf(sk) >= (cond.Args.TryGetValue("level", out var lv) && int.TryParse(lv, out int l) ? l : 0)) continue;
            int rate = inp.Rates.TryGetValue(m.Id, out int r) ? r : m.PriorPmDay;
            var bar = Bar(m, cond, inp);
            int score = bar == MeansBar.None ? Score(rate, m.WeightPm, m.RiskPm, inp.RiskWeightPm) : 0;
            list.Add(new MeansCandidate(m, i, rate, Math.Max(0, score), bar));
        }
        return list;
    }

    /// <summary>The limit that rules a means out, in a fixed order (crime, then the spending cap, risk, home by nightfall, skill, availability).</summary>
    public static MeansBar Bar(MeansDef m, ConditionRefDef cond, PlanInput inp)
    {
        var l = inp.Limits;
        if (m.Directive == "none") return MeansBar.NoWay;
        if (l.NoCrimes && m.Tags.Contains("crime")) return MeansBar.Crime;
        if (m.SpendMites > 0 && LimitGuard.CheckSpend(l, inp.State, inp.Day, inp.Purse, m.SpendMites) != LimitVerdict.Allowed) return MeansBar.Spend;
        if (m.RiskPm > l.MaxRiskPm) return MeansBar.Risk;
        if (l.HomeByNightfall && m.Tags.Contains("far")) return MeansBar.Home;
        if (m.NeedsSkill.Length > 0 && inp.SkillOf(m.NeedsSkill) < m.NeedsLevel) return MeansBar.Skill;
        if (inp.Available != null && !inp.Available(m, cond)) return MeansBar.Unavailable;
        return MeansBar.None;
    }

    /// <summary>The best candidate: the highest score above 0; a tie goes to the earlier row of the content (the tie draw on stream 0x800 would only come after, and content order is total).</summary>
    public static MeansCandidate? Best(IReadOnlyList<MeansCandidate> cands)
    {
        MeansCandidate? best = null;
        foreach (var c in cands)
            if (c.Score > 0 && (best == null || c.Score > best.Value.Score || (c.Score == best.Value.Score && c.Order < best.Value.Order))) best = c;
        return best;
    }

    /// <summary>The reason nothing scores, one line ("every way breaks your spending cap").</summary>
    public static string Reason(IReadOnlyList<MeansCandidate> cands)
    {
        if (cands.Count == 0) return "there is no way to move it";
        var bars = cands.Select(c => c.Bar).Distinct().OrderBy(b => (int)b).ToList();
        if (bars.Count == 1) return bars[0] switch
        {
            MeansBar.NoWay => "it needs you: the AI has no way to move it",
            MeansBar.None => "every way scores nothing",
            var b => "every way " + Phrase(b),
        };
        return "no way fits: " + string.Join(", ", bars.Where(b => b != MeansBar.None).Select(b => "one " + Phrase(b)));
    }

    private static string Phrase(MeansBar b) => b switch
    {
        MeansBar.Crime => "is a crime",
        MeansBar.Spend => "breaks your spending cap",
        MeansBar.Risk => "is above your risk limit",
        MeansBar.Home => "keeps you out after dark",
        MeansBar.Skill => "needs more skill",
        MeansBar.Unavailable => "is not open to you here",
        _ => "is closed to the AI",
    };
}
