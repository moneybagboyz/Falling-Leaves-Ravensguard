using System.Text.Json;
using System.Text.RegularExpressions;
using Ravensguard.Content;

namespace Ravensguard.Sim.Onboarding;

public enum HintSetting : byte { Auto, Off, Minimal, Full }
public enum HintLevel : byte { Off, Minimal, Full }

/// <summary>
/// What a profile remembers about hints (onboarding spec 3.1): how often each was shown (so a new world does not re-teach what the player knows), the setting, the game minutes played by the first character
/// (the default level is full for the first 60) and whether the first hour was ever completed. Stored in the profile settings file, never in the world save.
/// </summary>
public sealed class HintProfile
{
    public HintSetting Setting { get; set; } = HintSetting.Auto;
    public SortedDictionary<string, int> Seen { get; set; } = new(StringComparer.Ordinal);
    public long FirstCharacterMinutes { get; set; }
    public bool OnboardingComplete { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = false });
    public static HintProfile FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new HintProfile();
        try { return JsonSerializer.Deserialize<HintProfile>(json) ?? new HintProfile(); } catch (JsonException) { return new HintProfile(); }
    }
    public void ResetSeen() { Seen.Clear(); }
}

/// <summary>A hint ready to show: its words with the keys resolved, and the Codex page its "learn more" link opens.</summary>
public sealed record HintToast(string Id, string Text, string LearnMore, int Priority);

/// <summary>
/// The hint engine (onboarding spec 3.1, 4.2). Event driven: the sim and the UI call <see cref="Raise"/> when something happens (a closed list of events); a hint whose trigger matches is queued by priority and shown
/// one at a time, six real seconds each, never while a modal, a dialogue or a fight is up (the caller says so with <c>blocked</c>), at most <c>max_per_game</c> times per profile, and only at the level the setting allows.
/// There is no randomness and no clock of its own: the caller passes real seconds, so a scripted run gives the same toasts every time. Key tokens <c>{@action.id}</c> are turned into the keys bound now by <c>keys</c>.
/// </summary>
public sealed class HintEngine
{
    public const double ShowSeconds = 6, FadeSeconds = 0.5, DropAfterSeconds = 20;
    private static readonly Regex Token = new(@"\{@([a-z_.]+)\}", RegexOptions.Compiled);

    private readonly IReadOnlyList<HintDef> _hints;
    private readonly Func<string, string> _text, _keys;
    private readonly List<(HintDef Def, double At)> _queue = [];
    private double _busyUntil = double.MinValue;
    public HintProfile Profile { get; }
    public OnboardingSettingsDef Tuning { get; }
    /// <summary>Every toast shown, in order: (hint id, real seconds, game seconds the caller gave).</summary>
    public List<(string Id, double Real, long Game)> Shown { get; } = [];
    public long GameNow { get; set; }

    /// <param name="text">Localisation: key to text.</param>
    /// <param name="keys">The key bound now to an action id (<c>world.act</c>), as the player sees it.</param>
    public HintEngine(IReadOnlyList<HintDef> hints, HintProfile profile, Func<string, string> text, Func<string, string> keys, OnboardingSettingsDef? tuning = null)
    {
        _hints = hints; Profile = profile; _text = text; _keys = keys; Tuning = tuning ?? new OnboardingSettingsDef { Id = "onboarding" };
    }

    /// <summary>The level in force: the explicit setting, else full for the first character's first hour on the profile and minimal after.</summary>
    public HintLevel Level => Profile.Setting switch
    {
        HintSetting.Off => HintLevel.Off, HintSetting.Minimal => HintLevel.Minimal, HintSetting.Full => HintLevel.Full,
        _ => Profile.FirstCharacterMinutes < Tuning.FullHintGameMinutes && !Profile.OnboardingComplete ? HintLevel.Full : HintLevel.Minimal,
    };

    public int Pending => _queue.Count;
    public bool Showing(double realNow) => realNow < _busyUntil;

    private bool Allowed(HintDef h) => Level switch { HintLevel.Off => false, HintLevel.Minimal => h.Level == "minimal", _ => true };
    private int SeenCount(string id) => Profile.Seen.GetValueOrDefault(id);

    /// <summary>An event happened. <paramref name="kind"/> narrows it (a hint that names a kind needs the same one); <paramref name="value"/> is the number the hint's <c>min</c> is compared with. Returns how many hints it queued.</summary>
    public int Raise(string ev, string kind = "", long value = 0, double realNow = 0)
    {
        if (Level == HintLevel.Off) return 0;
        int n = 0;
        foreach (var h in _hints)
        {
            if (h.Trigger.Event != ev || !Allowed(h) || SeenCount(h.Id) >= h.MaxPerGame) continue;
            if (h.Trigger.Args.TryGetValue("kind", out var k) && k.Length > 0 && k != kind) continue;
            if (h.Trigger.Args.TryGetValue("min", out var m) && long.TryParse(m, out var min) && value < min) continue;
            if (_queue.Any(q => q.Def.Id == h.Id)) continue;
            _queue.Add((h, realNow)); n++;
        }
        return n;
    }

    /// <summary>The player already did what a hint teaches (pressed the key): it is not shown, and not shown later either.</summary>
    public void Satisfied(string id)
    {
        _queue.RemoveAll(q => q.Def.Id == id);
        Profile.Seen[id] = Math.Max(SeenCount(id), _hints.FirstOrDefault(h => h.Id == id)?.MaxPerGame ?? 1);
    }

    /// <summary>The next toast, or null: nothing waits, one is still up, or the screen is busy (<paramref name="blocked"/>: a modal, a dialogue or combat).</summary>
    public HintToast? Next(double realNow, bool blocked)
    {
        _queue.RemoveAll(q => realNow - q.At > DropAfterSeconds || !Allowed(q.Def) || SeenCount(q.Def.Id) >= q.Def.MaxPerGame);
        if (blocked || realNow < _busyUntil || _queue.Count == 0) return null;
        int best = 0;
        for (int i = 1; i < _queue.Count; i++)
            if (_queue[i].Def.Priority > _queue[best].Def.Priority || (_queue[i].Def.Priority == _queue[best].Def.Priority && string.CompareOrdinal(_queue[i].Def.Id, _queue[best].Def.Id) < 0)) best = i;
        var h = _queue[best].Def; _queue.RemoveAt(best);
        Profile.Seen[h.Id] = SeenCount(h.Id) + 1;
        _busyUntil = realNow + ShowSeconds;
        Shown.Add((h.Id, realNow, GameNow));
        return new HintToast(h.Id, Resolve(_text(h.Text)), h.LearnMore, h.Priority);
    }

    /// <summary>Key tokens to the keys bound now.</summary>
    public string Resolve(string text) => text.IndexOf("{@", StringComparison.Ordinal) < 0 ? text : Token.Replace(text, m => { var k = _keys(m.Groups[1].Value); return k.Length > 0 ? k : "?"; });
}
