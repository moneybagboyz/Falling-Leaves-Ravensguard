using System.Globalization;

namespace Ravensguard.Sim.Trade;

/// <summary>
/// What the hero's desk says, as a string key and its parameters (review finding 14): the sim never makes the English; the game resolves <see cref="Key"/> through its
/// <c>localization/en.csv</c> (<c>{0}</c>, <c>{1}</c> ... are the parameters) by setting <see cref="Resolver"/>. A parameter that is itself a <see cref="HeroText"/> is resolved first;
/// <see cref="Join"/> puts several messages in one answer. Rendering happens when <see cref="Render"/> is called (the game's main thread), never on the tick worker.
/// </summary>
public sealed record HeroText(string Key, IReadOnlyList<object> Args)
{
    public static readonly HeroText Empty = new("", []);
    /// <summary>Messages shown one after another (a craft step's several events); null for a single message.</summary>
    public IReadOnlyList<HeroText>? Parts { get; init; }

    /// <summary>
    /// The game's lookup: key and parameters to text (null when the key is unknown); the game sets it to its translation server. Unset, <see cref="EnCsv"/> is used: the game's
    /// <c>localization/en.csv</c> read from disk where it can be found (a run from the source tree, the tests); failing that a message renders as its key and parameters.
    /// </summary>
    public static Func<string, object[], string?>? Resolver { get; set; }

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Csv = new(() => EnCsv.Load(EnCsv.Find()));

    public static HeroText Of(string key, params object[] args) => new(key, args);
    public static HeroText Join(IEnumerable<HeroText> parts) => new("", []) { Parts = [.. parts] };

    public bool IsEmpty => Parts == null ? Key.Length == 0 : Parts.All(p => p.IsEmpty);

    public string Render()
    {
        if (Parts != null) return string.Join(" ", Parts.Select(p => p.Render()).Where(t => t.Length > 0).Distinct());
        if (Key.Length == 0) return "";
        var a = new object[Args.Count];
        for (int i = 0; i < a.Length; i++) a[i] = Args[i] is HeroText h ? h.Render() : Args[i] is long or int ? Args[i] : Convert.ToString(Args[i], CultureInfo.InvariantCulture) ?? "";
        string? r = null;
        try { r = Resolver != null ? Resolver(Key, a) : Csv.Value.TryGetValue(Key, out var t) ? string.Format(Core.MoneyFormat.Default, t, a) : null; }
        catch (FormatException) { r = null; }
        return r ?? (a.Length == 0 ? Key : Key + "(" + string.Join(", ", a.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture))) + ")");
    }

    public override string ToString() => Render();
}

/// <summary>Reads the game's <c>en.csv</c> (keys,en; quoted fields with doubled quotes) into a key table: the fallback of <see cref="HeroText.Resolver"/>.</summary>
public static class EnCsv
{
    /// <summary>The file, looked for from the working directory and from the assembly's folder upwards (<c>localization/en.csv</c> or <c>src/Ravensguard.Game/localization/en.csv</c>); null when absent.</summary>
    public static string? Find()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                foreach (var rel in new[] { Path.Combine("localization", "en.csv"), Path.Combine("src", "Ravensguard.Game", "localization", "en.csv") })
                {
                    var p = Path.Combine(d.FullName, rel);
                    if (File.Exists(p)) return p;
                }
        }
        return null;
    }

    public static IReadOnlyDictionary<string, string> Load(string? path)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (path == null) return d;
        try
        {
            foreach (var row in Rows(File.ReadAllText(path)).Skip(1))
                if (row.Count >= 2 && row[0].Length > 0) d[row[0]] = row[1];
        }
        catch (IOException) { }
        return d;
    }

    /// <summary>CSV rows: commas separate, a quoted field may hold commas, newlines and doubled quotes.</summary>
    public static IEnumerable<List<string>> Rows(string text)
    {
        var row = new List<string>(); var f = new System.Text.StringBuilder(); bool q = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (q)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { f.Append('"'); i++; } else q = false; }
                else f.Append(c);
            }
            else if (c == '"') q = true;
            else if (c == ',') { row.Add(f.ToString()); f.Clear(); }
            else if (c == '\n') { row.Add(f.ToString().TrimEnd('\r')); f.Clear(); yield return row; row = []; }
            else f.Append(c);
        }
        if (f.Length > 0 || row.Count > 0) { row.Add(f.ToString().TrimEnd('\r')); yield return row; }
    }
}
