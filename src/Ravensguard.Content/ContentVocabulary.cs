using System.Text.Json.Nodes;

namespace Ravensguard.Content;

/// <summary>Small typed vocabulary of known condition and effect ids (not scripting).</summary>
public sealed class ContentVocabulary
{
    private readonly HashSet<string> _conditions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _effects = new(StringComparer.Ordinal);

    public ContentVocabulary RegisterCondition(params string[] ids) { foreach (var i in ids) _conditions.Add(i); return this; }
    public ContentVocabulary RegisterEffect(params string[] ids) { foreach (var i in ids) _effects.Add(i); return this; }
    /// <summary>Stable text of the registered ids (sorted), for cache keys.</summary>
    public string Fingerprint() =>
        "c:" + string.Join(",", _conditions.Order(StringComparer.Ordinal)) + ";e:" + string.Join(",", _effects.Order(StringComparer.Ordinal));
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
        }
    }
}
