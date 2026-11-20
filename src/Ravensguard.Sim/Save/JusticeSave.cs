using MessagePack;
using Ravensguard.Sim.Law;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "justice" section (P6-C1, crime-and-justice 3 and 4.4 to 4.7; no format bump, absent while no one has been held, sentenced or given a law of their own, so a world that never used it keeps its
/// bytes): the gaol rows, the sentences, the lords' laws, the hero's docket, the marks left on bodies and the feuds. Content ids (keys, kinds) are strings.
/// </summary>
[MessagePackObject]
public sealed class JusticeSave
{
    public const string SectionName = "justice";

    /// <summary>Per gaol row: person, fief, settlement, since day, gaoler, flags (1 in the act, 2 confessed), unfed, fed days, arrest entry, then the crime ids.</summary>
    [Key(0)] public long[][] Gaol { get; set; } = [];
    /// <summary>Per sentence: crime, person, step, mites, days, lashes, display, feud, day, due day, end day, fief, settlement, presider, flags (1 by hero, 2 clergy, 4 lord's law, 8 started), mercy, entry, state.</summary>
    [Key(1)] public long[][] Sentences { get; set; } = [];
    /// <summary>Per sentence: key, kind, unpaid.</summary>
    [Key(2)] public string[][] SentenceText { get; set; } = [];
    /// <summary>Per law: fief.</summary>
    [Key(3)] public long[] LawFiefs { get; set; } = [];
    /// <summary>Per law: crime kind, punishment key.</summary>
    [Key(4)] public string[][] LawText { get; set; } = [];
    /// <summary>The docket's crime ids, then restituted, then clergy, then confessed, each preceded by its count.</summary>
    [Key(5)] public long[] Lists { get; set; } = [];
    [Key(6)] public int DocketDay { get; set; } = -1;
    /// <summary>Per body mark: person, day, crime, entry.</summary>
    [Key(7)] public long[][] Marks { get; set; } = [];
    /// <summary>Per body mark: kind, part.</summary>
    [Key(8)] public string[][] MarkText { get; set; } = [];
    /// <summary>Per feud: culprit, kin, until, crime.</summary>
    [Key(9)] public long[][] Feuds { get; set; } = [];
    [Key(10)] public long HeroExecuted { get; set; }
    [Key(11)] public int LastDay { get; set; } = -1;
    /// <summary>Per band captive unfed: person, days.</summary>
    [Key(12)] public long[][] BandUnfed { get; set; } = [];

    public static JusticeSave Capture(JusticeBook b)
    {
        var lists = new List<long> { b.Docket.Count };
        lists.AddRange(b.Docket.Select(x => (long)x));
        foreach (var set in new[] { b.Restituted, b.Clergy, b.Confessed }) { lists.Add(set.Count); lists.AddRange(set.Select(x => (long)x)); }
        return new JusticeSave
        {
            Gaol = [.. b.Gaol.Values.Select(g => new long[] { (long)g.Person, g.Fief, g.Settlement, g.SinceDay, (long)g.Gaoler, (g.InAct ? 1 : 0) | (g.Confessed ? 2 : 0), g.Unfed, g.FedDays, (long)g.ArrestEntry }.Concat(g.Crimes.Select(c => (long)c)).ToArray())],
            Sentences = [.. b.Sentences.Values.Select(s => new long[]
            {
                s.Crime, (long)s.Person, s.Step, s.Mites, s.Days, s.Lashes, s.Display, s.Feud, s.Day, s.DueDay, s.EndDay, s.Fief, s.Settlement, (long)s.Presider,
                (s.ByHero ? 1 : 0) | (s.Clergy ? 2 : 0) | (s.LordsLaw ? 4 : 0) | (s.Started ? 8 : 0), s.Mercy, (long)s.Entry, (long)s.State,
            })],
            SentenceText = [.. b.Sentences.Values.Select(s => new[] { s.Key, s.Kind, s.Unpaid })],
            LawFiefs = [.. b.Laws.Keys.Select(k => (long)k.Fief)],
            LawText = [.. b.Laws.Select(kv => new[] { kv.Key.Kind, kv.Value })],
            Lists = [.. lists], DocketDay = b.DocketDay,
            Marks = [.. b.Marks.Select(m => new long[] { (long)m.Person, m.Day, m.Crime, (long)m.Entry })],
            MarkText = [.. b.Marks.Select(m => new[] { m.Kind, m.Part })],
            Feuds = [.. b.Feuds.Select(f => new long[] { (long)f.Culprit, (long)f.Kin, f.Until, f.Crime })],
            BandUnfed = [.. b.BandUnfed.Select(kv => new long[] { (long)kv.Key, kv.Value })],
            HeroExecuted = (long)b.HeroExecuted, LastDay = b.LastDay,
        };
    }

    public JusticeBook Restore()
    {
        if (Sentences.Length != SentenceText.Length || LawFiefs.Length != LawText.Length || Marks.Length != MarkText.Length) throw new SaveException("Corrupt 'justice' section in save.");
        var b = new JusticeBook { DocketDay = DocketDay, HeroExecuted = (ulong)HeroExecuted, LastDay = LastDay };
        foreach (var v in Gaol)
        {
            if (v.Length < 9) throw new SaveException("Corrupt gaol row in save.");
            var g = new GaolRow { Person = (ulong)v[0], Fief = (int)v[1], Settlement = (int)v[2], SinceDay = (int)v[3], Gaoler = (ulong)v[4], InAct = (v[5] & 1) != 0, Confessed = (v[5] & 2) != 0, Unfed = (int)v[6], FedDays = (int)v[7], ArrestEntry = (ulong)v[8] };
            for (int k = 9; k < v.Length; k++) g.Crimes.Add((int)v[k]);
            b.Gaol[g.Person] = g;
        }
        for (int i = 0; i < Sentences.Length; i++)
        {
            var v = Sentences[i]; var t = SentenceText[i];
            if (v.Length < 18 || t.Length < 3) throw new SaveException("Corrupt sentence in save.");
            var s = new Sentence
            {
                Crime = (int)v[0], Person = (ulong)v[1], Step = (int)v[2], Mites = v[3], Days = (int)v[4], Lashes = (int)v[5], Display = (int)v[6], Feud = (int)v[7], Day = (int)v[8], DueDay = (int)v[9], EndDay = (int)v[10],
                Fief = (int)v[11], Settlement = (int)v[12], Presider = (ulong)v[13], ByHero = (v[14] & 1) != 0, Clergy = (v[14] & 2) != 0, LordsLaw = (v[14] & 4) != 0, Started = (v[14] & 8) != 0, Mercy = (sbyte)v[15],
                Entry = (ulong)v[16], State = (SentenceState)v[17], Key = t[0], Kind = t[1], Unpaid = t[2],
            };
            b.Sentences[s.Crime] = s;
        }
        for (int i = 0; i < LawFiefs.Length; i++) b.Laws[((int)LawFiefs[i], LawText[i][0])] = LawText[i][1];
        int at = 0;
        if (Lists.Length > 0)
            foreach (var set in new[] { b.Docket, b.Restituted, b.Clergy, b.Confessed })
            {
                if (at >= Lists.Length) throw new SaveException("Corrupt 'justice' lists in save.");
                int n = (int)Lists[at++];
                for (int k = 0; k < n; k++) set.Add((int)Lists[at++]);
            }
        for (int i = 0; i < Marks.Length; i++) b.Marks.Add(new BodyMark { Person = (ulong)Marks[i][0], Day = (int)Marks[i][1], Crime = (int)Marks[i][2], Entry = (ulong)Marks[i][3], Kind = MarkText[i][0], Part = MarkText[i][1] });
        foreach (var u in BandUnfed) b.BandUnfed[(ulong)u[0]] = (int)u[1];
        foreach (var f in Feuds) b.Feuds.Add(new FeudRow { Culprit = (ulong)f[0], Kin = (ulong)f[1], Until = (int)f[2], Crime = (int)f[3] });
        return b;
    }
}
