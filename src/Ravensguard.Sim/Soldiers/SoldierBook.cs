namespace Ravensguard.Sim.Soldiers;

/// <summary>
/// One piece of kit a soldier was issued from an armoury (3.3 <c>Issued</c>): which item and which armoury (the key of its room's origin cell) it came from, so a discharge can hand it back.
/// </summary>
public readonly record struct IssuedItem(ulong Item, long Armoury);

/// <summary>
/// A soldier (soldiers-and-companies 3.3): the person's role, who pays him (<see cref="Lord"/>, -1 for none; the hero is person 0, so 0 is a real lord), the template he is kitted from ("" means the role's own), what he
/// wears by slot (<see cref="Worn"/>: items of his own and issued ones) and what was issued to him from an armoury. Items are ordinary rows of the item table owned by the person; this row only says where each sits.
/// </summary>
public sealed class SoldierRow
{
    /// <summary>The person's id value (the hero's is 0: never read 0 as nobody).</summary>
    public ulong Person;
    public string Role = "";
    public long Lord = -1;
    public string Template = "";
    public int PromotedDay;
    /// <summary>What he is owed in wages, in mites (P7-SC-B: a lord's purse could not pay him some day).</summary>
    public long ArrearsMites;
    /// <summary>Drill hours by weapon family (3.3), whole hours; shield drill is under "Shields".</summary>
    public SortedDictionary<string, int> DrillHours { get; } = [];
    /// <summary>Slot name to item id, sorted: what he wears or carries in each template slot.</summary>
    public SortedDictionary<string, ulong> Worn { get; } = [];
    public List<IssuedItem> Issued { get; } = [];

    public SoldierRow Clone()
    {
        var c = new SoldierRow { Person = Person, Role = Role, Lord = Lord, Template = Template, PromotedDay = PromotedDay, ArrearsMites = ArrearsMites };
        foreach (var (k, v) in DrillHours) c.DrillHours[k] = v;
        foreach (var (k, v) in Worn) c.Worn[k] = v;
        c.Issued.AddRange(Issued);
        return c;
    }
}

/// <summary>A unit's formation drill (0..1000) and the last day it drilled (P7-SC-B 4.4).</summary>
public sealed class UnitDrill { public int Value; public int LastDay; }

/// <summary>A promotion offered at a season's start (4.5), waiting for the lord to accept or refuse.</summary>
public readonly record struct PromotionOffer(ulong Person, string Role, int Day);

/// <summary>A refit job queued for an item of an armoury before it is issued to a soldier (4.2): the armour is let out or taken in toward the wearer's body, then issued.</summary>
public sealed record RefitJob(ulong Item, ulong Person, long Armoury, string Slot, int ToCm3, int Day);

/// <summary>
/// P7-SC-C (4.6): a mercenary company, a group of real people (<see cref="Members"/>, sorted person ids; each also has a soldier row with no lord) with a captain, a purse (a ledger chest opened at the first hire, -1 before), a reputation
/// (fidelity, -1000..1000), a lair and, while hired, an employer (his id, his purse), the days the pay has been short and the contract that stands for the hire.
/// </summary>
public sealed class MercCompany
{
    public long Id;
    public string Name = "";
    public ulong Captain;
    public List<ulong> Members { get; } = [];
    public int Purse = -1;
    public int Reputation;
    public int Settlement = -1;
    /// <summary>The contract of the hire (-1 for none, also after a turn).</summary>
    public int ContractId = -1;
    /// <summary>The employer lord's id (-1 while between contracts) and his purse (a ledger index).</summary>
    public long Employer = -1;
    public int EmployerPurse = -1;
    public int HiredDay = -1, EndDay = -1;
    /// <summary>Days in a row the pay was short, and what the employer owes the company, mites.</summary>
    public int ArrearsDays;
    public long OwedMites;
    /// <summary>The company changed sides once already; its wage is then x1.5 (<see cref="WageMultPm"/>).</summary>
    public bool Turned;
    public int WageMultPm = 1000;
    public int Morale;
    public bool FillGaps;
    public int Kept;
    public bool Hired => Employer >= 0;
}

/// <summary>
/// P7-SC-A: the soldiers' state (3.3), the optional <c>soldiers</c> section: soldier rows by person and the refit jobs waiting for an armourer. Empty while no soldier row exists, so a world without soldiers keeps its
/// bytes. Everything is kept sorted, so equal states give equal bytes and equal hashes (<c>SoldierHash</c>, overview 7).
/// </summary>
public sealed class SoldierBook
{
    public SortedDictionary<ulong, SoldierRow> Rows { get; } = [];
    public List<RefitJob> Refits { get; } = [];
    /// <summary>P7-SC-B: skills by use for the soldiers (micro-levels over the person table's whole levels; the table is its <c>Base</c>, set by <see cref="SoldierWorld"/>).</summary>
    public Ravensguard.Sim.Crafting.SkillBook Skills { get; } = new();
    /// <summary>Formation drill by unit id (a formation, a contingent, a company).</summary>
    public SortedDictionary<long, UnitDrill> Units { get; } = [];
    /// <summary>Promotions offered and not yet answered, by person.</summary>
    public SortedDictionary<ulong, PromotionOffer> Offers { get; } = [];

    /// <summary>P7-SC-C: the mercenary companies by id, and the standing employers have with companies (a lord who broke faith: -200).</summary>
    public SortedDictionary<long, MercCompany> Companies { get; } = [];
    public SortedDictionary<long, int> Standing { get; } = [];

    public bool IsEmpty => Companies.Count == 0 && Rows.Count == 0 && Refits.Count == 0 && Skills.Count == 0 && Units.Count == 0 && Offers.Count == 0;
    public SoldierRow? Of(ulong person) => Rows.GetValueOrDefault(person);

    /// <summary>The hash of the section, fixed order, integers only; zero while empty.</summary>
    public ulong Hash()
    {
        if (IsEmpty) return 0;
        ulong h = 14695981039346656037UL;
        void M(long v) { unchecked { h = (h ^ (ulong)v) * 1099511628211UL; } }
        void S(string s) { foreach (char c in s) M(c); M(-9); }
        foreach (var (p, r) in Rows)
        {
            M((long)p); S(r.Role); M(r.Lord); S(r.Template); M(r.PromotedDay);
            M(r.Worn.Count); foreach (var (slot, it) in r.Worn) { S(slot); M((long)it); }
            M(r.Issued.Count); foreach (var i in r.Issued) { M((long)i.Item); M(i.Armoury); }
        }
        M(-1);
        foreach (var j in Refits.OrderBy(j => j.Item)) { M((long)j.Item); M((long)j.Person); M(j.Armoury); S(j.Slot); M(j.ToCm3); M(j.Day); }
        if (Skills.Count == 0 && Units.Count == 0 && Offers.Count == 0 && !Rows.Values.Any(r => r.ArrearsMites != 0 || r.DrillHours.Count > 0)) return h;
        M(-2);
        foreach (var (p, r) in Rows) { M((long)p); M(r.ArrearsMites); foreach (var (f, n) in r.DrillHours) { S(f); M(n); } M(-3); }
        foreach (var (p, sk, m) in Skills.MicroRows()) { M((long)p); M(sk); M(m); }
        M(-4);
        foreach (var (u, d) in Units) { M(u); M(d.Value); M(d.LastDay); }
        M(-5);
        foreach (var (p, o) in Offers) { M((long)p); S(o.Role); M(o.Day); }
        if (Companies.Count == 0 && Standing.Count == 0) return h;
        M(-6);
        foreach (var (id, c) in Companies)
        {
            M(id); S(c.Name); M((long)c.Captain); M(c.Purse); M(c.Reputation); M(c.Settlement); M(c.ContractId); M(c.Employer); M(c.EmployerPurse); M(c.HiredDay); M(c.EndDay);
            M(c.ArrearsDays); M(c.OwedMites); M(c.Turned ? 1 : 0); M(c.WageMultPm); M(c.Morale); M(c.FillGaps ? 1 : 0); M(c.Kept);
            M(c.Members.Count); foreach (var m in c.Members) M((long)m);
        }
        M(-7);
        foreach (var (l, v) in Standing) { M(l); M(v); }
        return h;
    }
}
