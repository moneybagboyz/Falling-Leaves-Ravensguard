using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Succession;

/// <summary>Why a person inherits property (inheritance 4.1): a will, then the law's order (primogeniture: sons, daughters, the spouse, children of 12 to 15, siblings, parents, younger children), else the lord.</summary>
public enum PropertyRule : byte { Will, Son, Daughter, Spouse, MinorChild, Sibling, Parent, YoungChild, Lord }

/// <summary>One property heir in the law's order: the row, the rule, the age on the day.</summary>
public readonly record struct PropertyHeir(int Row, EntityId Person, PropertyRule Rule, int AgeYears);

/// <summary>What kind of holding an estate line is (inheritance 3 <c>Estate</c>). Append only (saved as a byte).</summary>
public enum EstateThing : byte { Deed, Lease, Claim, Charter, Business, Share, Stall, Site, Contract, Vehicle, Animal, Commission, Apprenticeship, Chest, Coin }

/// <summary>Why a debt of an estate is owed (4.2 rule 2). Append only.</summary>
public enum DebtKind : byte { Wage, Steward, Rent, Commission }

/// <summary>
/// One line of a settled estate: what passed (kind, id), its value at the settle (the one valuation), to whom (holder id and purse kind, the person row when there is one), whether it went to the lord
/// (escheat), and whether a creditor has since claimed it (4.2 rule 2, at S6).
/// </summary>
public sealed class EstateEntry
{
    public EstateThing Kind;
    public EntityId Id;
    public long ValueMites;
    public EntityId To;
    public Ravensguard.Sim.Economy.PurseKind ToKind;
    public int ToRow = -1;
    public bool Escheat;
    public EntityId ClaimedBy;
}

/// <summary>One debt of an estate: kind, creditor (holder id and purse kind), what was owed, what has been paid (coin, goods at bid, claimed property), and what it was owed for.</summary>
public sealed class EstateDebt
{
    public DebtKind Kind;
    public EntityId Creditor;
    public Ravensguard.Sim.Economy.PurseKind CreditorKind;
    public long OwedMites, PaidMites;
    public EntityId Subject;
    public long Left => Math.Max(0, OwedMites - PaidMites);
}

/// <summary>
/// The rule of who inherits property (inheritance 4.1, 4.2 rule 4), shared by the hero and every person who dies holding something: a pure function of the person table (no draw: stream 0x457 stays
/// reserved and 0x561 is reserved for Phase 6's disputed inheritances, both asserted never drawn). Eligible: alive (an NPC row, or the hero's row: the hero inherits from his kin), not
/// <paramref name="unavailable"/> (a captive). Ties by birth day, then row.
/// </summary>
public static class EstateSettler
{
    public const int AdultAge = 16, MinorAge = 12;

    /// <summary>The law of a culture index (the person table's byte: name pools in id order, each culture by its pool, as the chunk generator reads it); primogeniture when unknown or elective.</summary>
    public static InheritanceRule LawOf(ContentDatabase? db, int cultureIndex)
    {
        if (db == null) return InheritanceRule.Primogeniture;
        var pools = db.All<NamePoolDef>().Select(p => p.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
        if (pools.Count == 0) return InheritanceRule.Primogeniture;
        var pool = pools[((cultureIndex % pools.Count) + pools.Count) % pools.Count];
        var cu = db.All<CultureDef>().Where(c => c.NamePool == pool).OrderBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
        return cu?.Inheritance == InheritanceRule.Partible ? InheritanceRule.Partible : InheritanceRule.Primogeniture;
    }

    private static bool Living(PersonTable pt, int i) => i >= 0 && i < pt.Count && pt.Status[i] is PersonStatus.Alive or PersonStatus.Hero;

    /// <summary>
    /// The law's order of property heirs of <paramref name="dead"/> (4.1 rule 2): living sons of 16 and over eldest first, then daughters of 16 and over, the spouse, children of 12 to 15 (a guardian
    /// manages, 4.4), siblings of 16 and over (brothers first), parents (father first), then children under 12; an empty list means escheat to the lord. <paramref name="childrenOf"/> overrides the
    /// table's Father and Mother columns (the labour market's kin graph); null reads them.
    /// </summary>
    public static List<PropertyHeir> Heirs(PersonTable pt, int dead, int day, Func<int, IEnumerable<int>>? childrenOf = null, Func<int, bool>? unavailable = null, KinGraph? kin = null)
    {
        var list = new List<PropertyHeir>();
        if (dead < 0 || dead >= pt.Count) return list;
        var seen = new HashSet<int>();
        bool Ok(int i) => i != dead && Living(pt, i) && !(unavailable?.Invoke(i) ?? false);
        void Add(int i, PropertyRule r) { if (seen.Add(i)) list.Add(new PropertyHeir(i, pt.IdOf(i), r, pt.AgeYears(i, day))); }
        IEnumerable<int> Eldest(IEnumerable<int> rows) => rows.OrderBy(i => pt.BirthDay[i]).ThenBy(i => i);
        List<int> children;
        if (childrenOf != null) children = [.. childrenOf(dead).Where(c => c >= 0 && c < pt.Count)];
        else if (kin != null) children = [.. kin.Children(dead).ToArray()];
        else { children = []; for (int i = 0; i < pt.Count; i++) if (i != dead && (pt.Father[i] == dead || pt.Mother[i] == dead)) children.Add(i); }
        int Age(int i) => pt.AgeYears(i, day);
        foreach (int c in Eldest(children.Where(c => Ok(c) && pt.Sex[c] == Sex.Male && Age(c) >= AdultAge))) Add(c, PropertyRule.Son);
        foreach (int c in Eldest(children.Where(c => Ok(c) && pt.Sex[c] != Sex.Male && Age(c) >= AdultAge))) Add(c, PropertyRule.Daughter);
        int sp = pt.Spouse[dead];                                                       // the dead keep the link as a record (Demography.Kill clears only the widowed partner's)
        if (Ok(sp)) Add(sp, PropertyRule.Spouse);
        foreach (int c in Eldest(children.Where(c => Ok(c) && Age(c) is >= MinorAge and < AdultAge))) Add(c, PropertyRule.MinorChild);
        int f = pt.Father[dead], m = pt.Mother[dead];
        if (f >= 0 || m >= 0)
        {
            var sibs = new List<int>();
            if (kin != null) { foreach (int i in kin.Siblings(dead)) if (Ok(i) && Age(i) >= AdultAge) sibs.Add(i); }
            else for (int i = 0; i < pt.Count; i++) if (i != dead && (f >= 0 && pt.Father[i] == f || m >= 0 && pt.Mother[i] == m) && Ok(i) && Age(i) >= AdultAge) sibs.Add(i);
            foreach (int s in Eldest(sibs.Where(s => pt.Sex[s] == Sex.Male))) Add(s, PropertyRule.Sibling);
            foreach (int s in Eldest(sibs.Where(s => pt.Sex[s] != Sex.Male))) Add(s, PropertyRule.Sibling);
        }
        if (Ok(f) && Age(f) >= AdultAge) Add(f, PropertyRule.Parent);
        if (Ok(m) && Age(m) >= AdultAge) Add(m, PropertyRule.Parent);
        foreach (int c in Eldest(children.Where(c => Ok(c) && Age(c) < MinorAge))) Add(c, PropertyRule.YoungChild);
        return list;
    }

    /// <summary>The sons a partible law splits among (4.2 rule 4): living sons of 16 and over, eldest first; empty when there are none (the estate then follows primogeniture).</summary>
    public static List<PropertyHeir> PartibleSons(IReadOnlyList<PropertyHeir> heirs) => [.. heirs.Where(h => h.Rule == PropertyRule.Son)];

    /// <summary>
    /// Partible law's split of real property (4.2 rule 4; IN-9): each holding whole, the most valuable first (ties by the lower id), dealt round robin to the sons eldest first. Returns the son's
    /// index for each holding, in the input order.
    /// </summary>
    public static int[] RoundRobin(IReadOnlyList<(EntityId Id, long Value)> holdings, int sons)
    {
        var res = new int[holdings.Count];
        if (sons <= 0) return res;
        var order = Enumerable.Range(0, holdings.Count).OrderByDescending(i => holdings[i].Value).ThenBy(i => holdings[i].Id).ToList();
        for (int k = 0; k < order.Count; k++) res[order[k]] = k % sons;
        return res;
    }

    /// <summary>Movables split equally among <paramref name="n"/> heirs, the remainder to the eldest (index 0).</summary>
    public static long[] Equally(long mites, int n)
    {
        if (n <= 0) return [];
        var r = new long[n]; long each = mites / n;
        for (int i = 0; i < n; i++) r[i] = each;
        r[0] += mites - each * n;
        return r;
    }

    /// <summary>The nearest adult kin of a minor (4.4, for an NPC heir): the living parent, else an adult sibling, else a grandparent; -1 none.</summary>
    public static int Guardian(PersonTable pt, int minor, int day)
    {
        if (minor < 0 || minor >= pt.Count) return -1;
        foreach (int p in new[] { pt.Mother[minor], pt.Father[minor] }) if (p >= 0 && p < pt.Count && pt.Status[p] == PersonStatus.Alive && pt.AgeYears(p, day) >= AdultAge) return p;
        int f = pt.Father[minor], m = pt.Mother[minor];
        int best = -1;
        for (int i = 0; i < pt.Count; i++)
            if (i != minor && pt.Status[i] == PersonStatus.Alive && pt.AgeYears(i, day) >= AdultAge && (f >= 0 && pt.Father[i] == f || m >= 0 && pt.Mother[i] == m) && (best < 0 || pt.BirthDay[i] < pt.BirthDay[best])) best = i;
        if (best >= 0) return best;
        foreach (int p in new[] { f, m })
            if (p >= 0 && p < pt.Count) foreach (int g in new[] { pt.Father[p], pt.Mother[p] }) if (g >= 0 && g < pt.Count && pt.Status[g] == PersonStatus.Alive) return g;
        return -1;
    }
}
