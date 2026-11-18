using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rumours;

namespace Ravensguard.Sim.Tests.Dialogue;

/// <summary>Shared pieces of the talk tests: the repo root, en.csv as a text source, and a small hand-made town.</summary>
public static class TalkKit
{
    public static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Ravensguard.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    private static Dictionary<string, string>? _csv;
    public static Dictionary<string, string> EnCsv() => _csv ??= ParseCsv(File.ReadAllText(Path.Combine(Root(), "src", "Ravensguard.Game", "localization", "en.csv"), Encoding.UTF8));
    public static ITextSource Text() => new DictText(EnCsv());

    /// <summary>The first column is the key; a field may be quoted with "" for a quote inside.</summary>
    public static Dictionary<string, string> ParseCsv(string text)
    {
        var res = new Dictionary<string, string>(StringComparer.Ordinal);
        int i = 0; bool header = true;
        while (i < text.Length)
        {
            var fields = new List<string>(); var sb = new StringBuilder(); bool q = false;
            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (q) { if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
                else if (c == '"') q = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else if (c == '\n') { i++; break; }
                else if (c != '\r') sb.Append(c);
            }
            fields.Add(sb.ToString());
            if (header) { header = false; continue; }
            if (fields.Count >= 2 && fields[0].Length > 0) res[fields[0]] = fields[1];
        }
        return res;
    }

    public static ContentDatabase LoadBase()
    {
        var r = new ModLoader(ContentTypeRegistry.Default, BaseVocabulary.Create()).Load([Path.Combine(Root(), "content", "base")]);
        if (!r.Success) throw new InvalidOperationException(r.Report.ToString());
        return r.Database;
    }

    /// <summary>A context in which every parameter can be named.</summary>
    public static TalkContext Rich()
    {
        var x = new TalkContext { Job = "smith", HeroName = "Aldric Vane", Place = "Ashby", FeastName = "Harvest feast", Mood = 0, Familiarity = 20, TrustPm = 100 };
        ParamResolver.Fill(x, Text());
        foreach (var (k, v) in new[] { ("price", "4 crowns"), ("goods", "flour"), ("unit", "sack"), ("person2", "Edith"), ("teller", "Bram"), ("rumour", "a fire at Ashby."), ("n", "3"), ("number", "5"), ("who", "Osric"), ("about", "famine") }) x.Params[k] = v;
        return x;
    }

    /// <summary>A town of <paramref name="people"/> adults living in settlement 0 (households of four, ten workplaces) and an empty chronicle; names are settlement names.</summary>
    public static (TalkWorld W, Chronicle C) Town(int people, ulong seed = 7, int settlements = 1, string[]? names = null)
    {
        var pt = new PersonTable(people * settlements, 1000); var chron = new Chronicle();
        for (int s = 0; s < settlements; s++)
            for (int k = 0; k < people; k++)
            {
                int i = pt.Add();
                pt.Given[i] = "P" + i; pt.Family[i] = "F" + (i / 4); pt.Byname[i] = ""; pt.BirthDay[i] = -40 * TimeConst.DaysPerYear; pt.Household[i] = i / 4; pt.HomeSettlement[i] = s; pt.WorkSettlement[i] = s;
                pt.WorkLot[i] = (short)(k % 10); pt.HomeLot[i] = (short)(k / 4); pt.Status[i] = PersonStatus.Alive; pt.Occupation[i] = k % 25 == 0 ? Occupation.Innkeeper : k % 40 == 1 ? Occupation.Reeve : Occupation.Farmer;
                pt.Father[i] = pt.Mother[i] = pt.Spouse[i] = -1; pt.DeathDay[i] = -1;
            }
        names ??= [.. Enumerable.Range(0, settlements).Select(s => s == 0 ? "Ashby" : "Town" + s)];
        return (new TalkWorld { People = pt, SettlementNames = names, Chronicle = chron, Seed = seed }, chron);
    }

    public static EntityId AddEntry(Chronicle c, string type, long day, string settlement, params (string K, string V)[] data)
    {
        var id = EntityId.Make(IdKind.Event, (ulong)(c.Count + 1));
        var d = new Dictionary<string, string> { ["settlement"] = settlement }; foreach (var (k, v) in data) d[k] = v;
        c.Add(new ChronicleEntry(id, new GameTime(day * TimeConst.SecondsPerDay + 6 * 3600), type, [], [new Cause(CauseKind.Fire, null, "a lamp left in a barn")], d));
        return id;
    }
}
