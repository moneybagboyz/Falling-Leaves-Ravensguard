namespace Ravensguard.Sim.Core;

/// <summary>
/// How the Chronicle screen words an entry (chronicle-full 4.1): a keyed entry through <c>tr()</c> with its parameters named by id; a legacy entry (a v8 save's, or a writer not yet moved)
/// as written, else its type's heading with who it concerned. Pure over a translation function and a <see cref="ChronicleView"/>, so a test reads the same words the screen shows.
/// </summary>
public static class ChronicleReader
{
    /// <summary>The names of the parameters' things: ids through the view's names ("someone" for an id the view does not know), goods through <paramref name="goodName"/>.</summary>
    public static Func<ChronParamKind, long, string> Names(ChronicleView v, Func<string, string> goodName, string unknown = "someone") => (kind, value) => kind switch
    {
        ChronParamKind.Good => v.Goods.TryGetValue((int)value, out var g) ? goodName(g) : unknown,
        ChronParamKind.Entry => v.NameOf((ulong)value) is { Length: > 0 } n ? n : unknown,
        _ => v.NameOf((ulong)value) is { Length: > 0 } n ? n : unknown,
    };

    /// <summary>The line an entry shows in the list and the detail pane.</summary>
    public static string Line(ChronicleEntry e, ChronicleTypes types, ChronicleView v, Func<string, string> tr, Func<string, string> goodName)
    {
        var names = Names(v, goodName);
        if (e.TextKey.Length > 0) return ChronicleTypes.Format(e, tr, names);
        if (e.Data.TryGetValue("text", out var t) && t.Length > 0) return t;
        if (e.Data.TryGetValue("title", out var ti) && ti.Length > 0) return ti;
        string heading = types.Get(e.TypeId) is { Def.Legacy: true } row && tr(row.TextKey) is { Length: > 0 } h && h != row.TextKey ? h : Pretty(e.TypeId);
        var who = e.Subjects.Select(s => v.NameOf(s.Value)).Where(n => n.Length > 0).Take(2).ToList();
        if (who.Count == 0 && e.Data.TryGetValue("settlement", out var place) && place.Length > 0) who.Add(place);
        return who.Count == 0 ? heading : heading + ": " + string.Join(", ", who);
    }

    /// <summary>A cause's note (its key with parameters, else the legacy English), or its kind when it has none.</summary>
    public static string Note(Cause c, ChronicleView v, Func<string, string> tr, Func<string, string> goodName)
    {
        string s = ChronicleTypes.FormatNote(c, tr, Names(v, goodName));
        return s.Length > 0 ? s : c.Kind.ToString();
    }

    public static string Pretty(string typeId) => typeId.Length == 0 ? "" : char.ToUpperInvariant(typeId[0]) + typeId[1..].Replace('_', ' ').Replace('.', ' ');
}
