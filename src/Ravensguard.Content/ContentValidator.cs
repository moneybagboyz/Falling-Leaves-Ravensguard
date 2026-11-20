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
            if (e.Value is DialogueDef dd) DialogueGraph.Check(dd, e.Location, report);
            CheckQualities(e, report);
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

    /// <summary>
    /// Item quality fields (<see cref="ItemQualityFieldAttribute"/>, on the def or on the records of one of its lists): one of the five tier names; an old DF name is read through the
    /// fixed map with one warning naming the file, the line and the field (items-quality-and-wear 6, IQ-13); anything else is an error.
    /// </summary>
    private static void CheckQualities(ContentEntry e, ContentReport report)
    {
        void One(object owner, PropertyInfo p, string path, SourceLocation loc)
        {
            if (p.GetValue(owner) is not string s || s.Length == 0) return;
            if (!QualityNames.TryParse(s, out int tier, out bool alias, out bool named))
                report.Error(loc, $"{e.Type} '{e.Id}'.{path}: unknown item quality '{s}' (one of {string.Join(", ", QualityNames.Tiers)})");
            else if (alias)
                report.Warning(loc, $"{e.Type} '{e.Id}'.{path}: quality '{s}' is a name of the old seven-tier scale, read as '{QualityNames.Tiers[tier]}'{(named ? " (a named masterwork)" : "")}; write the new name");
        }
        foreach (var prop in e.Value!.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var json = System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(prop.Name);
            var loc = e.PropertyLocations.TryGetValue(json, out var pl) ? pl : e.Location;
            if (prop.GetCustomAttribute<ItemQualityFieldAttribute>() != null) { One(e.Value, prop, json, loc); continue; }
            if (prop.PropertyType == typeof(string) || prop.GetValue(e.Value) is not System.Collections.IList list) continue;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not { } item) continue;
                foreach (var q in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (q.GetCustomAttribute<ItemQualityFieldAttribute>() != null)
                        One(item, q, $"{json}[{i}].{System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(q.Name)}", loc);
            }
        }
    }
}
