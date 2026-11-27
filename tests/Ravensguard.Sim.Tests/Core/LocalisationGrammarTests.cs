using Ravensguard.Sim.Core;
using Ravensguard.Sim.Tests.Economy;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Core;

/// <summary>P8-LC-2 (localisation 3.3): LC-5 tokens, LC-6 selects, LC-7 name forms. English and the test language; no culture, integers and strings only.</summary>
public class LocalisationGrammarTests(ITestOutputHelper output)
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Ravensguard.Sim", "Core", "RngStreams.cs"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    static Dictionary<string, string> English()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(Path.Combine(Root(), "src", "Ravensguard.Game", "localization", "en.csv")))
        {
            int c = line.IndexOf(','); if (c <= 0) continue;
            string v = line[(c + 1)..];
            if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1].Replace("\"\"", "\"");
            d[line[..c]] = v;
        }
        return d;
    }

    static readonly string[] Names = ["Aldric Thorne", "Maud", "Ravensguard", "Hollis", "Wexford", "Isolde", "Brannock", "Eadgyth", "Oswin", "Marge"];

    // LC-5: every template token of every chronicle type resolves, and 1,000 generated entries leave no brace or bar, in both languages
    [Fact]
    public void LC5_EveryTokenResolves_For1000GeneratedEntries_InEachLanguage()
    {
        var types = ChronicleTypes.Of(EconFixture.Content.All<Ravensguard.Content.ChronicleTypeDef>());
        var en = English(); var rows = types.Rows.Where(r => en.ContainsKey(r.TextKey)).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        var bad = new List<string>(); int withForms = 0;
        foreach (var r in rows)
        {
            string t = en[r.TextKey];
            bool forms = t.Contains('|') || t.Contains(".gen}");
            if (forms) withForms++;
            // a select or a form must name a real parameter and carry an other; the plain {token} rows of the old writers are the CH tests' business
            if (forms)
                foreach (var g in new[] { ChronGrammar.English, ChronGrammar.Test })
                    foreach (var e in g.Check(t, r.Def.Params.Select(p => p.Name).ToList())) bad.Add($"{r.Id}: {e}");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad.Take(10)));
        Assert.True(withForms >= 4, "the English rows use the new forms");

        int n = 0; var rng = new Random(5);
        foreach (var g in new[] { ChronGrammar.English, ChronGrammar.Test })
            for (int i = 0; i < 1000; i++)
            {
                var r = rows[i % rows.Count];
                var ps = r.Def.Params.Select(p => p.Type switch
                {
                    "person" or "place" or "item" or "entry" => new ChronParam(p.Name, ChronicleTypes.KindOf(p.Type), 1 + rng.Next(Names.Length)),
                    "count" or "days" or "good" => new ChronParam(p.Name, ChronicleTypes.KindOf(p.Type), rng.Next(0, 30)),
                    "mites" or "money" => ChronParam.Mites(p.Name, rng.Next(0, 100000)),
                    _ => ChronParam.TextKey(p.Name, "ch_ui_root"),
                }).ToList();
                string line = ChronicleTypes.Fill(en[r.TextKey], ps, k => en.GetValueOrDefault(k, k), (kind, v) => Names[(int)v % Names.Length], g, id => (id & 1) == 0 ? 'f' : 'm');
                Assert.DoesNotContain('|', line);
                n++;
            }
        output.WriteLine($"{n} entries rendered, {rows.Count} templates, {withForms} with forms");
    }

    // LC-6: gender and plural selects take the right branch for each category of the test language
    [Theory]
    [InlineData(0, "other"), InlineData(1, "one"), InlineData(2, "few"), InlineData(4, "few"), InlineData(5, "many"), InlineData(11, "many"), InlineData(12, "many"),
     InlineData(14, "many"), InlineData(21, "one"), InlineData(22, "few"), InlineData(25, "many"), InlineData(101, "one"), InlineData(111, "many"), InlineData(-3, "few")]
    public void LC6_PluralCategories_TestLanguage(long n, string cat)
    {
        Assert.Equal(cat, ChronGrammar.Test.Plural(n));
        string t = "{n} {n.plural|one:A|few:B|many:C|other:D}";
        string got = ChronicleTypes.Fill(t, [ChronParam.Count("n", n)], k => k, (_, _) => "", ChronGrammar.Test);
        Assert.Equal($"{n} " + cat switch { "one" => "A", "few" => "B", "many" => "C", _ => "D" }, got);
    }

    [Fact]
    public void LC6_English_OneAndOther_GenderSelect_MissingFallsToOther_AndTheCheck()
    {
        string t = "{n} {n.plural|one:day|other:days}";
        Assert.Equal("1 day", ChronicleTypes.Fill(t, [ChronParam.Count("n", 1)], k => k, (_, _) => ""));
        Assert.Equal("0 days", ChronicleTypes.Fill(t, [ChronParam.Count("n", 0)], k => k, (_, _) => ""));
        Assert.Equal("2 days", ChronicleTypes.Fill(t, [ChronParam.Count("n", 2)], k => k, (_, _) => ""));
        string g = "{w} lost {w.gender|m:his|f:her|other:their} way";
        var p = new[] { ChronParam.Person("w", new EntityId(7)) };
        Assert.Equal("Ann lost her way", ChronicleTypes.Fill(g, p, k => k, (_, _) => "Ann", null, id => 'f'));
        Assert.Equal("Bob lost his way", ChronicleTypes.Fill(g, p, k => k, (_, _) => "Bob", null, id => 'm'));
        Assert.Equal("Cy lost their way", ChronicleTypes.Fill(g, p, k => k, (_, _) => "Cy", null, null));
        Assert.Contains(ChronGrammar.English.Check("{n.plural|one:a}", ["n"]), e => e.Contains("no other"));
        Assert.Contains(ChronGrammar.English.Check("{x}", ["n"]), e => e.Contains("resolves to no parameter"));
        Assert.Contains(ChronGrammar.English.Check("{n.plural|few:a|other:b}", ["n"]), e => e.Contains("does not have"));
        Assert.Empty(ChronGrammar.Test.Check("{n.plural|one:a|few:b|many:c|other:d} {n}", ["n"]));
        Assert.Equal(["one", "other"], ChronGrammar.English.PluralSet);
    }

    // LC-7: the possessive of 200 generated names, English and the test language's suffix rule with its exception
    [Fact]
    public void LC7_NameForms_For200Names()
    {
        string[] first = ["Aldric", "Maud", "Hollis", "Eadgyth", "Oswin", "Marge", "Wulfric", "Edda", "Thomas", "Bess"];
        string[] last = ["Thorne", "Fenn", "Marsh", "Cox", "Ashby", "Reeve", "Hale", "Moss", "Wick", "Dane"];
        int n = 0;
        for (int i = 0; i < 10; i++) for (int j = 0; j < 10; j++) for (int k = 0; k < 2; k++, n++)
        {
            string name = first[i] + " " + last[j]; char gen = k == 0 ? 'm' : 'f';
            string en = ChronGrammar.English.Form("gen", name, gen);
            Assert.Equal(last[j][^1] == 's' ? name + "'" : name + "'s", en);
            string tl = ChronGrammar.Test.Form("gen", name, gen);
            char c = last[j][^1];
            string want = c == 'a' ? name[..^1] + "ae" : c == 'e' ? name + "s" : c is 's' or 'x' ? name + "is" : name + (gen == 'f' ? "ae" : "us");
            Assert.Equal(want, tl);
            Assert.Equal(tl + " hand", ChronicleTypes.Fill("{w.gen} hand", [ChronParam.Person("w", new EntityId(3))], x => x, (_, _) => name, ChronGrammar.Test, _ => gen));
        }
        Assert.Equal(200, n);
        Assert.Equal("Ravensguard", ChronGrammar.Test.Form("gen", "Ravensguard", 'm'));      // the exception
        Assert.Equal("Wex's", ChronGrammar.English.Form("gen", "Wex", 'm'));
        Assert.Equal("James'", ChronGrammar.English.Form("gen", "James", 'm'));
        Assert.Equal("Thorne", ChronGrammar.English.Form("nom", "Thorne", 'm'));             // an unknown form leaves the name
    }
}
