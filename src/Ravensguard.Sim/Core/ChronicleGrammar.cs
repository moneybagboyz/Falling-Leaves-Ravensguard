using System.Text;

namespace Ravensguard.Sim.Core;

/// <summary>
/// The grammar of a language for generated text (P8-LC-2, localisation 3.3): the CLDR plural category of a whole number, and the forms of a name ({person.gen}). Integer and string
/// work only, no culture-dependent formatting, so the same save reads the same in every language and never touches the sim hash. A language file supplies one of these; English is
/// <see cref="English"/>, the tests' language is <see cref="Test"/>. Template syntax: <c>{x}</c>, <c>{x.gen}</c> (a form), <c>{x.gender|m:..|f:..|other:..}</c> and
/// <c>{n.plural|one:..|few:..|many:..|other:..}</c> (a branch's text may hold plain tokens, not another select).
/// </summary>
public abstract class ChronGrammar
{
    /// <summary>The CLDR categories this language uses (every language has other).</summary>
    public abstract IReadOnlyList<string> PluralSet { get; }
    /// <summary>The category of a whole number (CLDR operands on integers: i = |n|, v = 0).</summary>
    public abstract string Plural(long n);
    /// <summary>The named form of a name ("gen", the possessive); an unknown form leaves the name as it is.</summary>
    public abstract string Form(string form, string name, char gender);

    public static readonly ChronGrammar English = new EnglishGrammar();
    public static readonly ChronGrammar Test = new TestGrammar();

    sealed class EnglishGrammar : ChronGrammar
    {
        static readonly string[] Set = ["one", "other"];
        public override IReadOnlyList<string> PluralSet => Set;
        public override string Plural(long n) => n == 1 || n == -1 ? "one" : "other";
        public override string Form(string form, string name, char gender) =>
            form == "gen" && name.Length > 0 ? (name[^1] is 's' or 'S' ? name + "'" : name + "'s") : name;
    }

    /// <summary>A made-up language for LC-6 and LC-7: Slavic-like plurals (one, few, many; other for zero) and a possessive suffix by the name's ending, with an exception.</summary>
    sealed class TestGrammar : ChronGrammar
    {
        static readonly string[] Set = ["one", "few", "many", "other"];
        public override IReadOnlyList<string> PluralSet => Set;
        public override string Plural(long n)
        {
            long a = n < 0 ? -n : n, m10 = a % 10, m100 = a % 100;
            if (a == 0) return "other";
            if (m10 == 1 && m100 != 11) return "one";
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return "few";
            return "many";
        }
        public override string Form(string form, string name, char gender)
        {
            if (form != "gen" || name.Length == 0 || name == "Ravensguard") return name;   // the exception is indeclinable
            char c = char.ToLowerInvariant(name[^1]);
            if (c == 'a') return name[..^1] + "ae";
            if (c == 'e') return name + "s";
            if (c is 's' or 'x') return name + "is";
            return name + (gender == 'f' ? "ae" : "us");
        }
    }

    static int Close(string t, int open)
    {
        int d = 0;
        for (int i = open; i < t.Length; i++) { if (t[i] == '{') d++; else if (t[i] == '}' && --d == 0) return i; }
        return -1;
    }

    static Dictionary<string, string> Branches(string s)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in s.Split('|')) { int c = part.IndexOf(':'); if (c > 0) d[part[..c]] = part[(c + 1)..]; }
        return d;
    }

    /// <summary>
    /// Resolves the selects of a template. <paramref name="category"/> gets a select's head ("n.plural", "who.gender") and returns the category of that parameter's value, or null when it
    /// cannot say (then the <c>other</c> branch is taken, as it is for a category the select lacks). Tokens and forms outside selects are left for the caller.
    /// </summary>
    public static string Select(string template, Func<string, string?> category)
    {
        if (template.IndexOf('|') < 0) return template;
        var sb = new StringBuilder(); int i = 0;
        while (i < template.Length)
        {
            if (template[i] != '{') { sb.Append(template[i++]); continue; }
            int close = Close(template, i);
            if (close < 0) { sb.Append(template, i, template.Length - i); break; }
            string body = template.Substring(i + 1, close - i - 1); int bar = body.IndexOf('|');
            if (bar < 0) sb.Append(template, i, close - i + 1);
            else
            {
                var br = Branches(body[(bar + 1)..]);
                string want = category(body[..bar]) ?? "other";
                sb.Append(br.TryGetValue(want, out var t) ? t : br.GetValueOrDefault("other", ""));
            }
            i = close + 1;
        }
        return sb.ToString();
    }

    /// <summary>
    /// The template check (LC-5 to LC-7): every <c>{token}</c> or <c>{token.form}</c> names a parameter in <paramref name="paramNames"/>, every select has an <c>other</c> branch, its axis is
    /// plural or gender, and its categories are in this language's <see cref="PluralSet"/> (plural) or m, f, other (gender). Returns the problems, empty when sound.
    /// </summary>
    public List<string> Check(string template, IReadOnlyCollection<string> paramNames)
    {
        var errs = new List<string>(); int i = 0;
        while ((i = template.IndexOf('{', i)) >= 0)
        {
            int close = Close(template, i);
            if (close < 0) { errs.Add("unclosed {"); break; }
            string body = template.Substring(i + 1, close - i - 1); i = close + 1;
            int bar = body.IndexOf('|'); string head = bar < 0 ? body : body[..bar];
            int dot = head.IndexOf('.'); string name = dot < 0 ? head : head[..dot], axis = dot < 0 ? "" : head[(dot + 1)..];
            if (!paramNames.Contains(name)) errs.Add($"token '{name}' resolves to no parameter");
            if (bar < 0) continue;
            if (axis != "plural" && axis != "gender") { errs.Add($"select '{head}' has an unknown axis"); continue; }
            var br = Branches(body[(bar + 1)..]);
            if (!br.ContainsKey("other")) errs.Add($"select '{head}' has no other");
            foreach (var (k, v) in br)
            {
                if (axis == "plural" ? !PluralSet.Contains(k) : k is not ("m" or "f" or "other")) errs.Add($"select '{head}' uses the category '{k}', which the language does not have");
                foreach (var e in Check(v, paramNames)) errs.Add(e);
            }
        }
        return errs;
    }
}
