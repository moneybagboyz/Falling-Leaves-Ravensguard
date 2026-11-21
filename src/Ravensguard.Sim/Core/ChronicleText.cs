using System.Globalization;
using Ravensguard.Content;

namespace Ravensguard.Sim.Core;

/// <summary>What a chronicle parameter holds (chronicle-full 3): ids for the things (the view looks up the current name), numbers, or a text key.</summary>
public enum ChronParamKind : byte { Person, Place, Item, Good, Mites, Count, Days, Entry, TextKey }

/// <summary>One typed parameter of a chronicle entry or a cause's note: <see cref="Name"/> fills <c>{name}</c> in the key's sentence. Ids and numbers live in <see cref="Value"/>, a nested key in <see cref="Key"/>.</summary>
public readonly record struct ChronParam(string Name, ChronParamKind Kind, long Value = 0, string Key = "")
{
    public static ChronParam Person(string name, EntityId id) => new(name, ChronParamKind.Person, (long)id.Value);
    public static ChronParam Place(string name, EntityId id) => new(name, ChronParamKind.Place, (long)id.Value);
    public static ChronParam Item(string name, EntityId id) => new(name, ChronParamKind.Item, (long)id.Value);
    public static ChronParam Good(string name, int goodId) => new(name, ChronParamKind.Good, goodId);
    public static ChronParam Mites(string name, long mites) => new(name, ChronParamKind.Mites, mites);
    public static ChronParam Count(string name, long n) => new(name, ChronParamKind.Count, n);
    public static ChronParam Days(string name, long n) => new(name, ChronParamKind.Days, n);
    public static ChronParam Entry(string name, EntityId id) => new(name, ChronParamKind.Entry, (long)id.Value);
    public static ChronParam TextKey(string name, string key) => new(name, ChronParamKind.TextKey, 0, key);
}

/// <summary>A row of <c>chronicle/types.jsonc</c> as the sim reads it (<see cref="ChronicleTypes"/>).</summary>
public sealed class ChronicleTypeRow
{
    public required string Id { get; init; }
    public required string TextKey { get; init; }
    public required ChronicleTypeDef Def { get; init; }
    public string SpecificRequires => Def.SpecificRequires;
    public IReadOnlySet<string> NoteKeys { get; init; } = new HashSet<string>();
}

/// <summary>The chronicle type table (chronicle-full 3): one row per type id, loaded from content; validated by <see cref="Validate"/> (CI).</summary>
public sealed class ChronicleTypes
{
    private readonly Dictionary<string, ChronicleTypeRow> _rows = new(StringComparer.Ordinal);
    public IReadOnlyCollection<ChronicleTypeRow> Rows => _rows.Values;
    public ChronicleTypeRow? Get(string typeId) => _rows.GetValueOrDefault(typeId);
    public bool Has(string typeId) => _rows.ContainsKey(typeId);

    public static ChronicleTypes From(ContentDatabase db) => Of(db.All<ChronicleTypeDef>());

    /// <summary>The table of the given rows (a mod's, or a test's), in id order; a later row of the same id replaces an earlier one.</summary>
    public static ChronicleTypes Of(IEnumerable<ChronicleTypeDef> defs)
    {
        var t = new ChronicleTypes();
        foreach (var d in defs.OrderBy(x => x.Id, StringComparer.Ordinal))
            t._rows[d.Id] = new ChronicleTypeRow { Id = d.Id, TextKey = d.TextKey, Def = d, NoteKeys = new HashSet<string>(d.NoteKeys, StringComparer.Ordinal) };
        return t;
    }

    public static readonly string[] ParamTypes = ["person", "place", "item", "good", "mites", "money", "count", "days", "entry", "text_key"];
    public static readonly string[] SpecificRules = ["event", "note", "either"];

    public static ChronParamKind KindOf(string type) => type switch
    {
        "person" => ChronParamKind.Person, "place" => ChronParamKind.Place, "item" => ChronParamKind.Item, "good" => ChronParamKind.Good, "mites" or "money" => ChronParamKind.Mites,
        "count" => ChronParamKind.Count, "days" => ChronParamKind.Days, "entry" => ChronParamKind.Entry, "text_key" => ChronParamKind.TextKey,
        _ => throw new ArgumentException("unknown chronicle parameter type " + type),
    };

    /// <summary>
    /// The content validator (chronicle-full 3, CH-1): every row's key is in <paramref name="knownKeys"/> (en.csv), its parameter types and names are valid and unique, its cause
    /// kinds are CauseKind names, its rumour row exists, its specificity rule is one of the three, a <c>note</c> or <c>either</c> row lists its note keys (also in en.csv) and a type
    /// is not both a root and a chance type. Returns the problems, empty when sound.
    /// </summary>
    public List<string> Validate(ContentDatabase db, Func<string, bool> knownKeys)
    {
        var errs = new List<string>();
        var kinds = Enum.GetNames<CauseKind>().ToHashSet(StringComparer.Ordinal);
        foreach (var r in _rows.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var d = r.Def; string who = $"chronicle_type '{r.Id}'";
            if (!knownKeys(d.TextKey)) errs.Add($"{who}: text key '{d.TextKey}' is not in en.csv");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in d.Params)
            {
                if (!ParamTypes.Contains(p.Type)) errs.Add($"{who}: parameter '{p.Name}' has unknown type '{p.Type}'");
                if (string.IsNullOrWhiteSpace(p.Name) || !names.Add(p.Name)) errs.Add($"{who}: parameter name '{p.Name}' is empty or repeated");
            }
            foreach (var k in d.CauseKinds) if (!kinds.Contains(k)) errs.Add($"{who}: unknown cause kind '{k}'");
            if (d.Rumour.Length > 0 && !db.Contains("rumour_type", d.Rumour)) errs.Add($"{who}: unknown rumour_type '{d.Rumour}'");
            if (!SpecificRules.Contains(d.SpecificRequires)) errs.Add($"{who}: specific_requires '{d.SpecificRequires}' is not event, note or either");
            if (d.SpecificRequires != "event" && d.NoteKeys.Count == 0) errs.Add($"{who}: specific_requires '{d.SpecificRequires}' needs note_keys");
            foreach (var nk in d.NoteKeys) if (!knownKeys(nk)) errs.Add($"{who}: note key '{nk}' is not in en.csv");
            if (d.Chance && d.Root) errs.Add($"{who}: a type is a root or a chance type, not both");
        }
        return errs;
    }

    /// <summary>
    /// Formats an entry for the view (chronicle-full 4.1): a keyed entry through <paramref name="tr"/> with its parameters named by <paramref name="nameOf"/> (ids to current names); a
    /// legacy entry (a v8 save's, or a writer not yet moved) shows what it carried: its English <c>text</c> or <c>title</c> data, else its first cause's note, else its type id.
    /// </summary>
    public static string Format(ChronicleEntry e, Func<string, string> tr, Func<ChronParamKind, long, string> nameOf)
    {
        if (e.TextKey.Length > 0) return Fill(tr(e.TextKey), e.Params, tr, nameOf);
        if (e.Data.TryGetValue("text", out var t) && t.Length > 0) return t;
        if (e.Data.TryGetValue("title", out var ti) && ti.Length > 0) return ti;
        foreach (var c in e.Causes) if (c.Note.Length > 0) return c.Note;
        return e.TypeId;
    }

    /// <summary>A cause's note for the view: its key with parameters, else the legacy English note ("" when it has neither).</summary>
    public static string FormatNote(Cause c, Func<string, string> tr, Func<ChronParamKind, long, string> nameOf) =>
        c.NoteKey.Length > 0 ? Fill(tr(c.NoteKey), c.NoteParams, tr, nameOf) : c.Note;

    /// <summary>
    /// Fills the named holes of a sentence from the typed parameters. A text-key parameter is itself a sentence, filled from the same parameters (so "{how} at the fight {where}"
    /// can nest {weapon} and {place}), to a depth of three.
    /// </summary>
    private static string Fill(string template, IReadOnlyList<ChronParam>? ps, Func<string, string> tr, Func<ChronParamKind, long, string> nameOf, int depth = 0)
    {
        if (ps == null || ps.Count == 0 || template.IndexOf('{') < 0) return template;
        var sb = new System.Text.StringBuilder(template);
        foreach (var p in ps)
        {
            string tag = "{" + p.Name + "}";
            if (template.IndexOf(tag, StringComparison.Ordinal) < 0) continue;
            string v = p.Kind switch
            {
                ChronParamKind.Mites => MoneyText.Format(p.Value, MoneyStyle.Amount, MoneyText.Words(tr)),
                ChronParamKind.Count or ChronParamKind.Days => p.Value.ToString(CultureInfo.InvariantCulture),
                ChronParamKind.TextKey => depth < 3 ? Fill(tr(p.Key), ps, tr, nameOf, depth + 1) : tr(p.Key),
                _ => nameOf(p.Kind, p.Value),
            };
            sb.Replace(tag, v);
        }
        return sb.ToString();
    }


    // ---------- save encoding (additive keys, chronicle-full 3) ----------

    /// <summary>Flattened for the save: name, then the kind letter and payload ("a" + id for a person, "T" + key for a text key), in the entry's own order.</summary>
    public static string[] Encode(IReadOnlyList<ChronParam>? ps)
    {
        if (ps == null || ps.Count == 0) return [];
        var o = new string[ps.Count * 2];
        for (int i = 0; i < ps.Count; i++)
        {
            var p = ps[i]; o[i * 2] = p.Name;
            o[i * 2 + 1] = p.Kind == ChronParamKind.TextKey ? "T" + p.Key : ((char)('a' + (int)p.Kind)).ToString() + p.Value.ToString(CultureInfo.InvariantCulture);
        }
        return o;
    }

    public static IReadOnlyList<ChronParam>? Decode(string[] flat)
    {
        if (flat.Length < 2) return null;
        var l = new List<ChronParam>(flat.Length / 2);
        for (int i = 0; i + 1 < flat.Length; i += 2)
        {
            string v = flat[i + 1];
            if (v.Length > 0 && v[0] == 'T') { l.Add(new ChronParam(flat[i], ChronParamKind.TextKey, 0, v[1..])); continue; }
            l.Add(new ChronParam(flat[i], (ChronParamKind)(v[0] - 'a'), long.Parse(v.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture)));
        }
        return l;
    }
}
