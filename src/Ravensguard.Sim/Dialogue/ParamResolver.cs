using System.Text;
using System.Text.RegularExpressions;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Dialogue;

/// <summary>
/// Fills a localised line (dialogue spec 3.1). <c>{name}</c> takes a parameter; <c>{he|she}</c> picks by the referent's sex; <c>{#n|one|many}</c> picks by the number in parameter <c>n</c>
/// (one when it is 1). A line is one whole sentence per language, so every language may reorder; nothing is concatenated from translated pieces.
/// </summary>
public static partial class ParamResolver
{
    /// <summary>Every parameter name a line may declare; <see cref="Fill"/> can resolve each from a full context.</summary>
    public static readonly string[] Known =
    [
        "name", "title", "job", "place", "season", "weather", "day_name", "feast", "rumour", "price", "person2", "field_or_yard", "goods", "unit", "n", "days", "teller", "number", "who", "about",
    ];

    [GeneratedRegex(@"\{([^{}]*)\}")] private static partial Regex Token();

    /// <summary>The names a template needs (grammar tokens excluded).</summary>
    public static List<string> ParamsOf(string template)
    {
        var res = new List<string>();
        foreach (Match m in Token().Matches(template))
        {
            string t = m.Groups[1].Value;
            if (t.Contains('|')) { if (t.StartsWith('#')) { string n = t[1..t.IndexOf('|')]; if (!res.Contains(n)) res.Add(n); } continue; }
            if (!res.Contains(t)) res.Add(t);
        }
        return res;
    }

    /// <summary>Substitutes parameters from the bag. A missing name stays as <c>{name}</c> so a test can find it.</summary>
    public static string Format(string template, IReadOnlyDictionary<string, string> bag, bool referentFemale = false)
        => Token().Replace(template, m =>
        {
            string t = m.Groups[1].Value;
            if (t.Contains('|'))
            {
                var parts = t.Split('|');
                if (t.StartsWith('#'))
                {
                    string n = parts[0][1..];
                    if (!bag.TryGetValue(n, out var v) || !int.TryParse(v, out int c)) return m.Value;
                    return parts.Length >= 3 ? (c == 1 ? parts[1] : parts[2]) : parts[1];
                }
                return referentFemale && parts.Length > 1 ? parts[1] : parts[0];
            }
            return bag.TryGetValue(t, out var s) ? s : m.Value;
        });

    public static string Title(RelationTier tier, bool heroFemale, bool rich, ITextSource text)
        => text.T(tier is RelationTier.Friend or RelationTier.Trusted ? "TITLE_FRIEND" : tier == RelationTier.Hated ? "TITLE_HATED" : rich ? (heroFemale ? "TITLE_LADY" : "TITLE_SIR") : (heroFemale ? "TITLE_GOODWIFE" : "TITLE_GOODMAN"));

    /// <summary>Adds every parameter the context can name, for the speaker and the moment (the caller adds <c>rumour</c>, <c>price</c>, <c>goods</c>, <c>unit</c>, <c>teller</c>, <c>person2</c>, <c>n</c> where it has them).</summary>
    public static void Fill(TalkContext x, ITextSource text)
    {
        var p = x.Params;
        p["name"] = x.HeroName.Length > 0 ? x.HeroName.Split(' ')[0] : text.T("TALK_YOU");
        p["title"] = Title(x.Tier, x.HeroFemale, x.HeroRich, text);
        p["job"] = x.Job.Length > 0 ? text.T("JOB_" + x.Job.ToUpperInvariant()) : text.T("JOB_NONE");
        p.TryAdd("place", x.Place.Length > 0 ? x.Place : text.T("R_UNKNOWN_PLACE"));
        p["season"] = text.T("SEASON_" + x.Season.ToString().ToUpperInvariant());
        p["weather"] = text.T(WeatherKey(x.Weather));
        p["feast"] = x.FeastName.Length > 0 ? x.FeastName : text.T("TALK_FEAST_DEFAULT");
        p["days"] = x.DaysToFeast.ToString();
        p["day_name"] = text.T("TALK_DAY_SOON");
        p["field_or_yard"] = text.T(x.JobClass == "outdoor" ? "TALK_FIELD" : "TALK_YARD");
        p.TryAdd("goods", text.T("TALK_GOODS_DEFAULT")); p.TryAdd("unit", text.T("TALK_UNIT_DEFAULT")); p.TryAdd("price", text.T("TALK_PRICE_DEFAULT"));
        p.TryAdd("person2", text.T("TALK_SOMEONE")); p.TryAdd("teller", text.T("R_WHO_TRAVELLER")); p.TryAdd("rumour", ""); p.TryAdd("n", "2");
        p.TryAdd("number", "0"); p.TryAdd("who", text.T("TALK_SOMEONE")); p.TryAdd("about", "");
    }

    public static string WeatherKey(string state) => state switch
    {
        "LightRain" => "WEATHER_RAIN", "HeavyRain" => "WEATHER_HEAVY_RAIN", "Thunderstorm" => "WEATHER_STORM", "Heatwave" => "WEATHER_HEAT",
        _ => "WEATHER_" + state.ToUpperInvariant(),
    };

    /// <summary>The text of a line: its key looked up and formatted with the context's parameters.</summary>
    public static string Say(string key, TalkContext x, ITextSource text, bool referentFemale = false) => Format(text.T(key), x.Params, referentFemale);
}
