using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;

namespace Ravensguard.Sim.Behaviour;

/// <summary>What a person is doing now (live-behaviour spec 3.1). Append only; the numbers are saved nowhere (the action is derived), but tests and logs hash them.</summary>
public enum ActionKind : byte { Sleep, Eat, Drink, Work, Commute, Shop, Socialise, Worship, Shelter, Flee, Rest, Idle, Travel, JobStep, TakeJob,
    /// <summary>Phase 4 (wounds 4.3): first aid on a bleeding neighbour or comrade; scored by <see cref="Ravensguard.Sim.Combat.Wounds.TreatAi"/>, not by the utility profile.</summary>
    Treat,
    /// <summary>Phase 4 (melee-and-ranged 4.7): in a fight, driven by <see cref="Ravensguard.Sim.Combat.Melee2.CombatBrain"/> (an <c>Attacked</c> interrupt puts a person here unless Flee scores higher).</summary>
    Fight,
    /// <summary>Phase 6 (overview 8.1, append only): fire fighting (buckets, pulling down thatch), a riot's rioters and a guard's pursuit; nothing scores them until B5, E1 and B2.</summary>
    FightFire, Riot, Pursue }

/// <summary>
/// The needs of one person at one moment, each in the unit its curve reads: hours since the last meal in thousandths of an hour, the others in permille. Filled from the person's day
/// (<see cref="NeedModel"/>) or by a test.
/// </summary>
public struct NeedInputs
{
    public int HungerMilliHours, FatiguePm, SocialPm, ColdPm, ThreatPm, DutyPm, WageRatioPm, WetPm, HeatPm;
}

/// <summary>What the scorer needs to know about the situation besides the needs: the block the schedule is in, what the person is already doing, and the gates (the place an action needs exists).</summary>
public struct ChoiceContext
{
    public ActionKind Scheduled;
    public ActionKind Current;
    /// <summary>Game minutes since <see cref="Current"/> began.</summary>
    public int MinutesInCurrent;
    public bool HasBed, CanEat, WorkOpen, Dark, HasRoof, HasBoard, CanSocialise, CanFlee, CanShelter;
    public ulong Seed;
    public long GameSec;
    public ulong Person;
    /// <summary>The directive and limits of an autopilot person (null for an NPC): a bias per action and a mask of removed actions.</summary>
    public ActionPolicy? Policy;
}

public readonly record struct Choice(ActionKind Kind, int Score, ActionKind RunnerUp, int RunnerUpScore);

/// <summary>
/// One scoring function for every person in both tiers (live-behaviour spec 4.2): <c>score(a) = sum(w * curve(x)) / sum(w) + schedule bonus + commit bonus + noise</c>, gated to zero when the place
/// the action needs does not exist; the best score wins, ties go to the lower <see cref="ActionKind"/>. Integer only, no allocation per call, and the noise is the counter RNG stream
/// <see cref="RngStreams.UtilityNoise"/> keyed on the game second's 20-second window, the person and the action, so the answer never depends on the order people are decided in or on the game speed.
/// </summary>
public sealed class UtilityBrain
{
    private sealed record Curve(string Id, string From, int[] X, int[] Y);
    private readonly struct Weight(int need, int w) { public readonly int Need = need, W = w; }
    private sealed record Act(ActionKind Kind, Weight[] Weights, int CommitMin, int MinDarkOrFatiguePm, string Place);

    private readonly Curve[] _curves;
    private readonly Act[] _acts;
    public UtilityProfileDef Profile { get; }
    public int HungerEatHours => Profile.HungerEatHours;

    public UtilityBrain(UtilityProfileDef p)
    {
        Profile = p;
        _curves = [.. p.Needs.Select(n => new Curve(n.Id, n.From, [.. n.CurvePm.Select(c => c[0])], [.. n.CurvePm.Select(c => c[1])]))];
        var acts = new List<Act>();
        foreach (var a in p.Actions)
        {
            if (!Enum.TryParse<ActionKind>(a.Kind, out var k)) throw new FormatException($"utility_profile '{p.Id}': unknown action '{a.Kind}'");
            var ws = a.Weights.Select(w => { int i = Array.FindIndex(_curves, c => c.Id == w.Need); if (i < 0) throw new FormatException($"utility_profile '{p.Id}': action {a.Kind} names unknown need '{w.Need}'"); return new Weight(i, w.WeightPm); }).ToArray();
            acts.Add(new Act(k, ws, Math.Max(1, a.CommitMinutes), a.MinDarkOrFatiguePm, a.NeedsPlace));
        }
        _acts = [.. acts];
    }

    public static UtilityBrain Default { get; } = new(BehaviourContent.Default.Utility);

    /// <summary>The minutes a started action of this kind is kept (content: <c>commit_minutes</c>).</summary>
    public int CommitMinutes(ActionKind k) { foreach (var a in _acts) if (a.Kind == k) return a.CommitMin; return 5; }

    /// <summary>Piecewise-linear value of a curve at <paramref name="xMilli"/> (thousandths of the curve's unit), clamped to the first and last point.</summary>
    private static int Eval(Curve c, long xMilli)
    {
        var xs = c.X; var ys = c.Y;
        if (xs.Length == 0) return 0;
        if (xMilli <= xs[0] * 1000L) return ys[0];
        for (int i = 1; i < xs.Length; i++)
            if (xMilli <= xs[i] * 1000L)
            {
                long x0 = xs[i - 1] * 1000L, x1 = xs[i] * 1000L;
                return (int)(ys[i - 1] + (ys[i] - ys[i - 1]) * (xMilli - x0) / (x1 - x0));
            }
        return ys[^1];
    }

    private long Input(int curve, in NeedInputs n) => _curves[curve].From switch
    {
        "hours_since_meal" => n.HungerMilliHours, "fatigue_pm" => n.FatiguePm * 1000L, "social_pm" => n.SocialPm * 1000L, "cold_stress_pm" => n.ColdPm * 1000L,
        "wet_pm" => n.WetPm * 1000L, "heat_stress_pm" => n.HeatPm * 1000L, "threat_pm" => n.ThreatPm * 1000L, "duty_pm" => n.DutyPm * 1000L, "wage_ratio_pm" => n.WageRatioPm * 1000L, _ => 0,
    };

    private bool Gate(Act a, in NeedInputs n, in ChoiceContext c) => a.Kind switch
    {
        ActionKind.Eat => c.CanEat,
        ActionKind.Sleep => c.HasBed && (c.Dark || n.FatiguePm >= a.MinDarkOrFatiguePm || c.Scheduled == ActionKind.Sleep),
        ActionKind.Work => c.WorkOpen,
        ActionKind.Shelter => c.CanShelter && c.HasRoof,
        ActionKind.Flee => c.CanFlee,
        ActionKind.Socialise => c.CanSocialise,
        ActionKind.TakeJob => c.HasBoard,
        _ => true,
    };

    /// <summary>The score of one action (0 when gated out); public for the explanation string and tests.</summary>
    public int Score(int actIndex, in NeedInputs n, in ChoiceContext c)
    {
        var a = _acts[actIndex];
        if (!Gate(a, n, c)) return -1;
        if (c.Policy != null && c.Policy.Forbids(a.Kind)) return -1;
        long num = 0, den = 0;
        foreach (var w in a.Weights) { num += (long)w.W * Eval(_curves[w.Need], Input(w.Need, n)); den += w.W; }
        int s = den > 0 ? (int)(num / den) : 0;
        if (a.Kind == c.Scheduled) s += Profile.ScheduleBonusPm;
        if (a.Kind == c.Current && c.MinutesInCurrent < a.CommitMin) s += Profile.CommitBonusPm;
        if (c.Policy != null) s += c.Policy.BiasPm[(int)a.Kind];
        if (a.Kind == ActionKind.Sleep && n.FatiguePm >= Profile.ExhaustedPm) s += 1500;
        if (a.Kind == ActionKind.Flee && n.ThreatPm > Profile.FleeThreatPm) s += 2000;
        // a person who is far too cold or hot, or wet and getting cold, goes in out of it whatever the day's plan says (content: shelter_override_pm; clothing spec 4.3 and 4.4)
        if (a.Kind == ActionKind.Shelter && Profile.ShelterOverridePm > 0 && (n.ColdPm >= Profile.ShelterOverridePm || n.HeatPm >= Profile.ShelterOverridePm || (n.WetPm >= 700 && n.ColdPm >= Profile.ShelterOverridePm / 3))) s += 1500;
        if (Profile.NoisePm > 0) s += (int)DetRng.Range(c.Seed, c.GameSec / 20, c.Person, RngStreams.UtilityNoise, (uint)actIndex, -Profile.NoisePm, Profile.NoisePm + 1);
        return s;
    }

    public Choice Choose(in NeedInputs n, in ChoiceContext c)
    {
        int best = -1, bestS = int.MinValue, run = -1, runS = int.MinValue;
        for (int i = 0; i < _acts.Length; i++)
        {
            int s = Score(i, n, c);
            if (s < 0) continue;
            if (s > bestS || (s == bestS && _acts[i].Kind < _acts[best].Kind)) { run = best; runS = bestS; best = i; bestS = s; }
            else if (s > runS || (s == runS && _acts[i].Kind < _acts[run].Kind)) { run = i; runS = s; }
        }
        if (best < 0) return new Choice(ActionKind.Idle, 0, ActionKind.Idle, 0);
        return new Choice(_acts[best].Kind, bestS, run >= 0 ? _acts[run].Kind : _acts[best].Kind, run >= 0 ? runS : 0);
    }

    /// <summary>Why the person chose it: the top two contributors, for the Q menu and the inspector; built only when asked.</summary>
    public string Explain(NeedInputs n, ChoiceContext c)
    {
        var ch = Choose(n, c);
        string Why(ActionKind k)
        {
            int idx = Array.FindIndex(_acts, a => a.Kind == k);
            if (idx < 0) return k.ToString();
            var a = _acts[idx];
            var parts = new List<string>();
            if (k == c.Scheduled) parts.Add($"schedule +{Profile.ScheduleBonusPm}");
            foreach (var w in a.Weights) parts.Add(_curves[w.Need].Id switch { "hunger" => $"hungry {n.HungerMilliHours / 1000} h", "fatigue" => $"tired {n.FatiguePm / 10}%", "duty" => "duty", "social" => "lonely", "warmth" => $"cold {n.ColdPm / 10}%", "wetness" => $"wet {n.WetPm / 10}%", "heat" => $"hot {n.HeatPm / 10}%", _ => _curves[w.Need].Id });
            return parts.Count == 0 ? k.ToString() : $"{k} ({string.Join(", ", parts)})";
        }
        return ch.RunnerUp == ch.Kind ? Why(ch.Kind) : $"{Why(ch.Kind)}, then {Why(ch.RunnerUp)} {ch.RunnerUpScore}";
    }

    public static ActionKind OfKind(JobKind k) => k switch
    {
        JobKind.Sleep => ActionKind.Sleep, JobKind.Work => ActionKind.Work, JobKind.Shop => ActionKind.Shop, JobKind.Church => ActionKind.Worship, JobKind.Commute => ActionKind.Commute,
        JobKind.Eat => ActionKind.Eat, JobKind.Travel => ActionKind.Travel, JobKind.Haul or JobKind.GuardCaravan => ActionKind.JobStep, _ => ActionKind.Idle,
    };
}
