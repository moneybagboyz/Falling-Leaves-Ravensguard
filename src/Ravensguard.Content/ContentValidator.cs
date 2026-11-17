using System.Reflection;

namespace Ravensguard.Content;

/// <summary>Post-load validation: [ContentRef] cross-references and vocabulary use.</summary>
public static class ContentValidator
{
    public static void Validate(ContentDatabase db, ContentVocabulary? vocabulary, ContentReport report)
    {
        foreach (var e in db.Entries)
        {
            if (vocabulary != null) vocabulary.Validate(e.Json, e.Location, $"{e.Type} '{e.Id}'", report);
            if (e.Value == null) continue;
            foreach (var prop in e.Value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<ContentRefAttribute>();
                if (attr == null) continue;
                string targetType;
                try { targetType = db.Registry.NameOf(attr.Target); }
                catch (ArgumentException ex) { report.Error(e.Location, $"{e.Type} '{e.Id}'.{prop.Name}: {ex.Message}"); continue; }

                var jsonName = System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(prop.Name);
                var loc = e.PropertyLocations.TryGetValue(jsonName, out var pl) ? pl : e.Location;
                var value = prop.GetValue(e.Value);
                IEnumerable<string?> refs = value switch
                {
                    null => [],
                    string s => [s],
                    IEnumerable<string> list => list,
                    _ => [],
                };
                foreach (var r in refs)
                {
                    if (string.IsNullOrEmpty(r)) continue;
                    bool ok;
                    try { ok = db.Contains(targetType, db.Resolve(targetType, r)); }
                    catch (ContentException) { ok = false; }
                    if (!ok) report.Error(loc, $"{e.Type} '{e.Id}'.{jsonName}: unknown {targetType} id '{r}'");
                }
            }
        }
    }
}
