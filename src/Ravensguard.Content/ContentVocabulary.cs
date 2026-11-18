using System.Text.Json.Nodes;

namespace Ravensguard.Content;

/// <summary>Small typed vocabulary of known condition and effect ids (not scripting).</summary>
public sealed class ContentVocabulary
{
    private readonly HashSet<string> _conditions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _effects = new(StringComparer.Ordinal);

    public ContentVocabulary RegisterCondition(params string[] ids) { foreach (var i in ids) _conditions.Add(i); return this; }
    public ContentVocabulary RegisterEffect(params string[] ids) { foreach (var i in ids) _effects.Add(i); return this; }
    private readonly SortedDictionary<string, HashSet<string>> _argValues = new(StringComparer.Ordinal);
    /// <summary>
    /// Restricts one argument of an effect to a closed set (case-insensitive), checked at load: an effect naming another value is an error rather than something the game quietly
    /// turns into a default (review finding 12: <c>start_contract</c> made a guard job of any kind it did not know).
    /// </summary>
    public ContentVocabulary RegisterEffectArg(string effect, string arg, params string[] values)
    {
        var key = effect + "." + arg;
        if (!_argValues.TryGetValue(key, out var set)) _argValues[key] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in values) set.Add(v);
        return this;
    }
    /// <summary>The allowed values of an effect's argument, or null when it is free.</summary>
    public IReadOnlySet<string>? EffectArgValues(string effect, string arg) => _argValues.GetValueOrDefault(effect + "." + arg);
    /// <summary>Stable text of the registered ids (sorted), for cache keys.</summary>
    public string Fingerprint() =>
        "c:" + string.Join(",", _conditions.Order(StringComparer.Ordinal)) + ";e:" + string.Join(",", _effects.Order(StringComparer.Ordinal))
        + (_argValues.Count == 0 ? "" : ";a:" + string.Join(",", _argValues.Select(kv => kv.Key + "=" + string.Join("|", kv.Value.Select(v => v.ToLowerInvariant()).Order(StringComparer.Ordinal)))));
    public bool IsCondition(string id) => _conditions.Contains(id);
    public bool IsEffect(string id) => _effects.Contains(id);

    /// <summary>Checks every "conditions"/"effects" array (at any depth) in <paramref name="node"/>.</summary>
    public void Validate(JsonNode? node, SourceLocation location, string owner, ContentReport report)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, value) in o)
                {
                    if (key is "conditions" or "effects" && value is JsonArray arr)
                        CheckList(arr, key == "conditions", location, owner, report);
                    Validate(value, location, owner, report);
                }
                break;
            case JsonArray a:
                foreach (var v in a) Validate(v, location, owner, report);
                break;
        }
    }

    private void CheckList(JsonArray arr, bool conditions, SourceLocation loc, string owner, ContentReport report)
    {
        var kind = conditions ? "condition" : "effect";
        foreach (var item in arr)
        {
            if (item is not JsonObject o || o["id"] is not JsonValue idv || !idv.TryGetValue<string>(out var id))
            {
                report.Error(loc, $"{owner}: {kind} entries must be objects with a string \"id\"");
                continue;
            }
            if (!(conditions ? IsCondition(id) : IsEffect(id)))
                report.Error(loc, $"{owner}: unknown {kind} id '{id}'");
            else if (!conditions && o["args"] is JsonObject args)
                foreach (var (arg, value) in args)
                    if (EffectArgValues(id, arg) is { } allowed && value is JsonValue av && av.TryGetValue<string>(out var sv) && !allowed.Contains(sv))
                        report.Error(loc, $"{owner}: effect '{id}' has {arg} '{sv}', which is not one of {string.Join(", ", allowed.Order(StringComparer.Ordinal))}");
        }
    }
}
