using Ravensguard.Content;

namespace Ravensguard.Sim.Onboarding;

/// <summary>The onboarding content of a database: hints, the first hour of each origin and the tuning row (onboarding spec 3).</summary>
public sealed class OnboardingContent
{
    public IReadOnlyList<HintDef> Hints { get; }
    public IReadOnlyDictionary<string, FirstHourDef> FirstHours { get; }
    public OnboardingSettingsDef Settings { get; }

    private OnboardingContent(IReadOnlyList<HintDef> h, IReadOnlyDictionary<string, FirstHourDef> f, OnboardingSettingsDef s) { Hints = h; FirstHours = f; Settings = s; }

    public static OnboardingContent From(ContentDatabase db)
    {
        var hints = db.All<HintDef>().OrderBy(h => h.Id, StringComparer.Ordinal).ToList();
        var fh = db.All<FirstHourDef>().ToDictionary(f => f.Origin, StringComparer.Ordinal);
        var s = db.All<OnboardingSettingsDef>().FirstOrDefault() ?? new OnboardingSettingsDef { Id = "onboarding" };
        return new OnboardingContent(hints, fh, s);
    }

    public FirstHourDef? Of(string origin) => FirstHours.GetValueOrDefault(origin);
}
