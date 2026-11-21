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
    /// <summary>Slot name to item id, sorted: what he wears or carries in each template slot.</summary>
    public SortedDictionary<string, ulong> Worn { get; } = [];
    public List<IssuedItem> Issued { get; } = [];

    public SoldierRow Clone()
    {
        var c = new SoldierRow { Person = Person, Role = Role, Lord = Lord, Template = Template, PromotedDay = PromotedDay };
        foreach (var (k, v) in Worn) c.Worn[k] = v;
        c.Issued.AddRange(Issued);
        return c;
    }
}

/// <summary>A refit job queued for an item of an armoury before it is issued to a soldier (4.2): the armour is let out or taken in toward the wearer's body, then issued.</summary>
public sealed record RefitJob(ulong Item, ulong Person, long Armoury, string Slot, int ToCm3, int Day);

/// <summary>
/// P7-SC-A: the soldiers' state (3.3), the optional <c>soldiers</c> section: soldier rows by person and the refit jobs waiting for an armourer. Empty while no soldier row exists, so a world without soldiers keeps its
/// bytes. Everything is kept sorted, so equal states give equal bytes and equal hashes (<c>SoldierHash</c>, overview 7).
/// </summary>
public sealed class SoldierBook
{
    public SortedDictionary<ulong, SoldierRow> Rows { get; } = [];
    public List<RefitJob> Refits { get; } = [];

    public bool IsEmpty => Rows.Count == 0 && Refits.Count == 0;
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
        return h;
    }
}
