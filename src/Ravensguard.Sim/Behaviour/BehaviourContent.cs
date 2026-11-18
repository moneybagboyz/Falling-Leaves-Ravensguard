using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ravensguard.Content;

namespace Ravensguard.Sim.Behaviour;

/// <summary>
/// The behaviour content in one place: the utility profile, the day plans and the crowd and odd-job tuning (live-behaviour spec 3.4, 3.5). <see cref="Default"/> is the shipped
/// <c>content/base/behaviour</c> folder, embedded in the assembly so the pure schedule functions need no content folder; <see cref="From"/> reads a loaded
/// <see cref="ContentDatabase"/> (a mod's changes), falling back to the default for any type it lacks.
/// </summary>
public sealed class BehaviourContent
{
    public UtilityProfileDef Utility { get; init; } = new() { Id = "default" };
    public IReadOnlyList<ScheduleDef> Schedules { get; init; } = [];
    public CrowdTuningDef Crowd { get; init; } = new() { Id = "crowd" };
    public OddJobTuningDef OddJobs { get; init; } = new() { Id = "odd_jobs" };

    private static readonly Lazy<BehaviourContent> DefaultBook = new(LoadEmbedded);
    public static BehaviourContent Default => DefaultBook.Value;

    public ScheduleDef? Plan(string id) { foreach (var s in Schedules) if (s.Id == id) return s; return null; }

    /// <summary>The plan that lists the occupation (the first in file order wins).</summary>
    public ScheduleDef? PlanFor(string occupation) { foreach (var s in Schedules) if (s.Occupations.Contains(occupation)) return s; return null; }

    public static BehaviourContent From(ContentDatabase db)
    {
        var d = Default;
        var util = db.TryGet<UtilityProfileDef>("default", out var u) ? u : d.Utility;
        var plans = db.All<ScheduleDef>().OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        // file order matters for occupations (first wins); the content database gives ids in load order, which for the base folder is file order
        var ordered = db.All<ScheduleDef>().ToList();
        return new BehaviourContent
        {
            Utility = util, Schedules = ordered.Count > 0 ? ordered : d.Schedules,
            Crowd = db.TryGet<CrowdTuningDef>("crowd", out var c) ? c : d.Crowd, OddJobs = db.TryGet<OddJobTuningDef>("odd_jobs", out var o) ? o : d.OddJobs,
        };
    }

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private static BehaviourContent LoadEmbedded()
    {
        var asm = typeof(BehaviourContent).Assembly;
        UtilityProfileDef? util = null; var plans = new List<ScheduleDef>(); CrowdTuningDef? crowd = null; OddJobTuningDef? odd = null;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("behaviour/", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            var arr = JsonNode.Parse(s, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsArray();
            foreach (var n in arr)
            {
                var o = n!.AsObject(); string type = o["type"]!.GetValue<string>(); o.Remove("type");
                switch (type)
                {
                    case "utility_profile": util = o.Deserialize<UtilityProfileDef>(Opts); break;
                    case "schedule": plans.Add(o.Deserialize<ScheduleDef>(Opts)!); break;
                    case "crowd_tuning": crowd = o.Deserialize<CrowdTuningDef>(Opts); break;
                    case "odd_job_tuning": odd = o.Deserialize<OddJobTuningDef>(Opts); break;
                }
            }
        }
        return new BehaviourContent { Utility = util ?? new() { Id = "default" }, Schedules = plans, Crowd = crowd ?? new() { Id = "crowd" }, OddJobs = odd ?? new() { Id = "odd_jobs" } };
    }
}

/// <summary>
/// Anchor expressions of a day plan: <c>sunrise</c>, <c>sunset</c>, <c>noon</c>, <c>HH:MM</c>, another anchor's name, <c>winter</c> (one hour in the short days, else nothing), plus or minus <c>H:MM</c> or a term,
/// <c>min(a, b)</c>, <c>max(a, b)</c>. Values are minutes after midnight; integer only.
/// </summary>
public sealed class AnchorEnv
{
    public int Sunrise, Sunset, DayLength;
    private readonly IReadOnlyDictionary<string, string> _anchors;
    private readonly Dictionary<string, int> _done = new(StringComparer.Ordinal);
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);
    public AnchorEnv(IReadOnlyDictionary<string, string> anchors, int sunrise, int sunset) { _anchors = anchors; Sunrise = sunrise; Sunset = sunset; DayLength = sunset - sunrise; }

    public int Get(string name)
    {
        if (_done.TryGetValue(name, out int v)) return v;
        if (!_anchors.TryGetValue(name, out var expr)) throw new KeyNotFoundException($"anchor '{name}' is not defined");
        if (!_busy.Add(name)) throw new InvalidOperationException($"anchor '{name}' refers to itself");
        v = Eval(expr); _busy.Remove(name); _done[name] = v; return v;
    }

    public int Eval(string expr) { int pos = 0; int v = Sum(expr, ref pos); Skip(expr, ref pos); if (pos != expr.Length) throw new FormatException($"unexpected '{expr[pos]}' in '{expr}'"); return v; }

    private static void Skip(string s, ref int p) { while (p < s.Length && s[p] == ' ') p++; }

    private int Sum(string s, ref int p)
    {
        int v = Term(s, ref p);
        while (true)
        {
            Skip(s, ref p);
            if (p < s.Length && (s[p] == '+' || s[p] == '-')) { char op = s[p++]; int r = Term(s, ref p); v = op == '+' ? v + r : v - r; }
            else return v;
        }
    }

    private int Term(string s, ref int p)
    {
        Skip(s, ref p);
        int st = p;
        if (p < s.Length && char.IsDigit(s[p]))
        {
            int h = 0; while (p < s.Length && char.IsDigit(s[p])) h = h * 10 + (s[p++] - '0');
            int m = 0; if (p < s.Length && s[p] == ':') { p++; int digits = 0; while (p < s.Length && char.IsDigit(s[p])) { m = m * 10 + (s[p++] - '0'); digits++; } if (digits != 2) throw new FormatException($"minutes need two digits in '{s}'"); }
            return h * 60 + m;
        }
        while (p < s.Length && (char.IsLetter(s[p]) || s[p] == '_')) p++;
        string id = s[st..p];
        if (id.Length == 0) throw new FormatException($"expected a term at {st} in '{s}'");
        Skip(s, ref p);
        if ((id is "min" or "max") && p < s.Length && s[p] == '(')
        {
            p++; int a = Sum(s, ref p); Skip(s, ref p);
            if (p >= s.Length || s[p] != ',') throw new FormatException($"expected ',' in '{s}'");
            p++; int b = Sum(s, ref p); Skip(s, ref p);
            if (p >= s.Length || s[p] != ')') throw new FormatException($"expected ')' in '{s}'");
            p++; return id == "min" ? Math.Min(a, b) : Math.Max(a, b);
        }
        return id switch { "sunrise" => Sunrise, "sunset" => Sunset, "noon" => 720, "winter" => DayLength < 600 ? 60 : 0, _ => Get(id) };
    }
}
