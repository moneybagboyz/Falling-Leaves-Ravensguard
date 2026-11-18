using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ravensguard.Content;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Autopilot;

/// <summary>
/// The autopilot's content in one place (autopilot spec 3.3): the settings row and the training activities. <see cref="Default"/> is the shipped <c>content/base/autopilot</c> folder embedded in the assembly (so pure tests need no
/// content folder); <see cref="From"/> reads a loaded <see cref="ContentDatabase"/> (a mod's changes), falling back to the default.
/// </summary>
public sealed class AutopilotContent
{
    public AutopilotSettingsDef Settings { get; init; } = new() { Id = "default" };
    public ActivityCatalog Activities { get; init; } = new([]);

    private static readonly Lazy<AutopilotContent> DefaultBook = new(LoadEmbedded);
    public static AutopilotContent Default => DefaultBook.Value;

    public static AutopilotContent From(ContentDatabase db)
    {
        var d = Default;
        var acts = db.All<ActivityDef>().ToList();
        return new AutopilotContent
        {
            Settings = db.All<AutopilotSettingsDef>().FirstOrDefault() ?? d.Settings,
            Activities = acts.Count > 0 ? new ActivityCatalog(acts) : d.Activities,
        };
    }

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private static AutopilotContent LoadEmbedded()
    {
        var asm = typeof(AutopilotContent).Assembly;
        AutopilotSettingsDef? settings = null; var acts = new List<ActivityDef>();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("autopilot/", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            var arr = JsonNode.Parse(s, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsArray();
            foreach (var n in arr)
            {
                var o = n!.AsObject(); string type = o["type"]!.GetValue<string>(); o.Remove("type");
                if (type == "autopilot_settings") settings = o.Deserialize<AutopilotSettingsDef>(Opts);
                else if (type == "activity") acts.Add(o.Deserialize<ActivityDef>(Opts)!);
            }
        }
        return new AutopilotContent { Settings = settings ?? new() { Id = "default" }, Activities = new ActivityCatalog(acts) };
    }
}

/// <summary>One practice use the catalog offers for a skill, resolved to what <c>Progress.Use</c> needs.</summary>
public readonly record struct PracticeUse(string Recipe, int DX100, int CapLevel, int Difficulty, IReadOnlyList<string> Needs);

/// <summary>
/// Maps a skill to the ways of training it (spec 3.3): the practice uses up to their cap levels (the practice yard and the practice recipes, up to 30), and above the caps the best real work (<c>Then</c>) with the highest
/// <c>d x gain</c> that fits the limits (the runner claims it through the jobs board).
/// </summary>
public sealed class ActivityCatalog
{
    private readonly Dictionary<Skill, ActivityDef> _bySkill = [];
    public ActivityCatalog(IEnumerable<ActivityDef> defs)
    {
        foreach (var d in defs.OrderBy(d => d.Id, StringComparer.Ordinal))
            if (Enum.TryParse<Skill>(d.Skill, true, out var s) && !_bySkill.ContainsKey(s)) _bySkill[s] = d;
    }
    public bool Knows(Skill s) => _bySkill.ContainsKey(s);
    public ActivityDef? Of(Skill s) => _bySkill.GetValueOrDefault(s);
    public IReadOnlyList<Skill> Skills => [.. _bySkill.Keys.OrderBy(k => (int)k)];

    /// <summary>The uses that still teach a person of <paramref name="level"/>: their cap level is above it, best gain first (then recipe id).</summary>
    public IEnumerable<PracticeUse> UsesFor(Skill s, int level)
    {
        if (!_bySkill.TryGetValue(s, out var a)) yield break;
        foreach (var u in a.Uses.Where(u => u.CapLevel > level).OrderByDescending(u => u.DX100).ThenBy(u => u.Recipe, StringComparer.Ordinal))
            yield return new PracticeUse(u.Recipe, u.DX100, u.CapLevel, Math.Max(10, u.CapLevel / 2), u.Needs);
    }

    /// <summary>True when a practice use is left for this level; false means the skill is trained by real work (<see cref="ActivityDef.Then"/>) from here on.</summary>
    public bool CanPractise(Skill s, int level) => UsesFor(s, level).Any();
}
