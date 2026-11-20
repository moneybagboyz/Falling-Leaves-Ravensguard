using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Law;

/// <summary>Where a sentence stands: waiting to be carried out (or served), done, unpaid (turned into gaol or outlawry), avoided (the convict was gone before dawn) or pardoned.</summary>
public enum SentenceState : byte { Pending, Done, Unpaid, Avoided, Pardoned }

/// <summary>
/// One sentence (crime-and-justice 4.6): the table key and step the court chose, what it comes to (mites, days, lashes), when it was pronounced and when it is carried out, and the entries that explain it.
/// A sentence is carried out the next morning at hour <c>CarryOutHour</c>; a weregild runs on to its term, a gaol term to its end.
/// </summary>
public sealed class Sentence
{
    public int Crime;
    public ulong Person;
    public string Key = "", Kind = "";
    public int Step;
    public long Mites;
    public int Days, Lashes, Display, Feud;
    public string Unpaid = "gaol";
    public int Day, DueDay, EndDay = -1;
    public int Fief = -1, Settlement = -1;
    /// <summary>Who judged: the lord, his steward, or the hero (person id value; 0 with <see cref="ByHero"/> when it was the hero).</summary>
    public ulong Presider;
    public bool ByHero, Clergy, LordsLaw;
    public sbyte Mercy;
    public ulong Entry;
    public SentenceState State;
    /// <summary>The carry-out has started (a gaol term counting, a weregild's term running).</summary>
    public bool Started;
}

/// <summary>One person held in a fief's gaol (crime-and-justice 4.5): the crimes he is held for, who keeps the keys, whether he was taken in the act or has confessed, the days unfed.</summary>
public sealed class GaolRow
{
    public ulong Person;
    public int Fief = -1, Settlement = -1, SinceDay;
    public ulong Gaoler;
    public bool InAct, Confessed;
    public int Unfed, FedDays;
    public ulong ArrestEntry;
    public List<int> Crimes = [];
}

/// <summary>A mark the court left on a body (a hand, a brand, a flogging, a day in the stocks): the entry that says why, so the wound's cause chain reaches the crime.</summary>
public sealed class BodyMark
{
    public ulong Person;
    public string Kind = "", Part = "";
    public int Day, Crime;
    public ulong Entry;
}

/// <summary>A blood price's feud: until the day the victim's kin may kill the culprit without a crime.</summary>
public sealed class FeudRow
{
    public ulong Culprit, Kin;
    public int Until, Crime;
}

/// <summary>
/// The records of the courts and the gaol (the optional "justice" section, P6-C1, crime-and-justice 3, 4.4 to 4.7): who is held, the sentences, the lords' own laws, the docket of the hero's court, the marks
/// left on bodies and the feuds. Crimes themselves live in <see cref="CrimeBook"/>; this book is what the arrest, the cell and the court add. Absent while empty (no format bump).
/// </summary>
public sealed class JusticeBook
{
    public readonly SortedDictionary<ulong, GaolRow> Gaol = [];
    public readonly SortedDictionary<int, Sentence> Sentences = [];
    /// <summary>A fief's own law (the law panel): (fief, crime kind) to a punishment key.</summary>
    public readonly SortedDictionary<(int Fief, string Kind), string> Laws = new(Comparer<(int Fief, string Kind)>.Create((a, b) => a.Fief != b.Fief ? a.Fief.CompareTo(b.Fief) : string.CompareOrdinal(a.Kind, b.Kind)));
    /// <summary>The cases the hero's court has before it, by crime id; <see cref="DocketDay"/> is the court day they were laid out on (-1: none).</summary>
    public readonly SortedSet<int> Docket = [];
    public int DocketDay = -1;
    /// <summary>Crimes whose accused paid full restitution before sentence, claimed benefit of clergy, or pleaded guilty.</summary>
    public readonly SortedSet<int> Restituted = [], Clergy = [], Confessed = [];
    public readonly List<BodyMark> Marks = [];
    public readonly List<FeudRow> Feuds = [];
    /// <summary>Days in a row a band's captive went unfed (CJ-16: two and he is let go), by person.</summary>
    public readonly SortedDictionary<ulong, int> BandUnfed = [];
    /// <summary>The entry of the hero's hanging (0: none), so the succession screen can say why.</summary>
    public ulong HeroExecuted;
    public int LastDay = -1;

    public bool IsEmpty => Gaol.Count == 0 && Sentences.Count == 0 && Laws.Count == 0 && Docket.Count == 0 && Restituted.Count == 0 && Clergy.Count == 0 && Confessed.Count == 0 && Marks.Count == 0
        && Feuds.Count == 0 && BandUnfed.Count == 0 && HeroExecuted == 0 && LastDay == -1 && DocketDay == -1;

    public bool Held(ulong person) => Gaol.ContainsKey(person);

    /// <summary>The marks a person carries from the court, in the order they were given.</summary>
    public IEnumerable<BodyMark> MarksOf(ulong person) => Marks.Where(m => m.Person == person);

    /// <summary>The <c>LawHash</c> share of the courts: every row in key order.</summary>
    public ulong Hash()
    {
        ulong h = 0x84222325CBF29CE4UL;
        void M(ulong v) => h = DetRng.Mix(h ^ (v + 0x9E3779B97F4A7C15UL));
        void S(string s) { foreach (char c in s) M(c); M(0xFF); }
        foreach (var (p, g) in Gaol)
        {
            M(p); M((ulong)(uint)g.Fief); M((ulong)(uint)g.Settlement); M((ulong)(uint)g.SinceDay); M(g.Gaoler); M(g.InAct ? 1UL : 0); M(g.Confessed ? 1UL : 0); M((ulong)(uint)g.Unfed); M((ulong)(uint)g.FedDays); M(g.ArrestEntry);
            foreach (int c in g.Crimes) M((ulong)(uint)c);
        }
        foreach (var (id, s) in Sentences)
        {
            M((ulong)(uint)id); M(s.Person); S(s.Key); S(s.Kind); M((ulong)(uint)s.Step); M((ulong)s.Mites); M((ulong)(uint)s.Days); M((ulong)(uint)s.Lashes); M((ulong)(uint)s.Display); M((ulong)(uint)s.Feud); S(s.Unpaid);
            M((ulong)(uint)s.Day); M((ulong)(uint)s.DueDay); M((ulong)(uint)s.EndDay); M((ulong)(uint)s.Fief); M((ulong)(uint)s.Settlement); M(s.Presider); M(s.ByHero ? 1UL : 0); M(s.Clergy ? 1UL : 0); M(s.LordsLaw ? 1UL : 0);
            M((ulong)(long)s.Mercy); M(s.Entry); M((ulong)s.State); M(s.Started ? 1UL : 0);
        }
        foreach (var ((f, k), v) in Laws) { M((ulong)(uint)f); S(k); S(v); }
        foreach (int c in Docket) M((ulong)(uint)c); M((ulong)(uint)DocketDay);
        foreach (int c in Restituted) M((ulong)(uint)c); M(0xA1);
        foreach (int c in Clergy) M((ulong)(uint)c); M(0xA2);
        foreach (int c in Confessed) M((ulong)(uint)c); M(0xA3);
        foreach (var m in Marks) { M(m.Person); S(m.Kind); S(m.Part); M((ulong)(uint)m.Day); M((ulong)(uint)m.Crime); M(m.Entry); }
        foreach (var f in Feuds) { M(f.Culprit); M(f.Kin); M((ulong)(uint)f.Until); M((ulong)(uint)f.Crime); }
        foreach (var (p, n) in BandUnfed) { M(p); M((ulong)(uint)n); }
        M(HeroExecuted); M((ulong)(uint)LastDay);
        return h;
    }
}
