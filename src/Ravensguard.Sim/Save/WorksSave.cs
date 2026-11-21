using MessagePack;

namespace Ravensguard.Sim.Save;

/// <summary>A warehouse lease (large-buildings 5): who rents how many m3 of which warehouse (its key is the room's origin cell), for what a season, since when and how many seasons are paid.</summary>
public sealed record WarehouseLease(long Warehouse, int Tenant, int Owner, int M3, int RentMites, int FromSeason, int PaidSeasons);

/// <summary>A depot's policy: the tonnes the lord wants kept (0: none), whether grain older than the rotation age is sold, and whether the target has been reported reached.</summary>
public sealed record DepotPolicy(long Depot, long TargetKg, bool Rotation, bool Filled);

/// <summary>A lot of grain in a depot, by the season it came in; the depot's store holds the goods, the lots say how old they are (oldest first).</summary>
public sealed record DepotLot(long Depot, string Good, long Grams, int Season);

/// <summary>An item lent out of an armoury: which armoury, which item, who holds it and since which day (the issue book).</summary>
public sealed record IssueRow(long Armoury, ulong Item, ulong Holder, int Day);

/// <summary>
/// P7-LB1 / P7-LB2: the works' state (large-buildings 5): the sluices that are shut (slot keys of built sluice pieces), warehouse leases, depot policies and lots, the items each armoury holds and its issue book, the
/// business premises links (a business and a cell of its building) and the barracks' upkeep owners. Empty while nothing is set. A building is named by the key of its room's origin cell
/// (<c>RoomWorld.Key</c>). Everything is saved sorted, so equal states give equal bytes.
/// </summary>
public sealed class WorksBook
{
    public SortedSet<long> SluiceShut { get; } = [];
    public List<WarehouseLease> Leases { get; } = [];
    public SortedDictionary<long, DepotPolicy> Depots { get; } = [];
    public List<DepotLot> Lots { get; } = [];
    public SortedDictionary<long, SortedSet<ulong>> ArmouryStock { get; } = [];
    public List<IssueRow> Issues { get; } = [];
    /// <summary>Business id value to a cell key of its premises (a cell of the building its works stand in).</summary>
    public SortedDictionary<ulong, long> Premises { get; } = [];
    /// <summary>Barracks key to the entity whose store pays its straw.</summary>
    public SortedDictionary<long, ulong> BarracksOwner { get; } = [];
    /// <summary>The last day each armoury's care ran (a day is never done twice).</summary>
    public SortedDictionary<long, int> CareDay { get; } = [];

    public bool IsEmpty => SluiceShut.Count == 0 && Leases.Count == 0 && Depots.Count == 0 && Lots.Count == 0 && ArmouryStock.Values.All(s => s.Count == 0) && Issues.Count == 0
        && Premises.Count == 0 && BarracksOwner.Count == 0 && CareDay.Count == 0;

    /// <summary>Whether anything beyond the sluices is set (the sluices alone are the LB1 section).</summary>
    public bool HasBuildings => !IsEmpty && (Leases.Count > 0 || Depots.Count > 0 || Lots.Count > 0 || ArmouryStock.Values.Any(s => s.Count > 0) || Issues.Count > 0 || Premises.Count > 0 || BarracksOwner.Count > 0 || CareDay.Count > 0);

    /// <summary>The section's hash (<c>WorksHash</c>, overview 7): fixed order, integers only. Zero while empty.</summary>
    public ulong Hash()
    {
        if (IsEmpty) return 0;
        ulong h = 14695981039346656037UL;
        void M(long v) { unchecked { h = (h ^ (ulong)v) * 1099511628211UL; } }
        M(SluiceShut.Count); foreach (var k in SluiceShut) M(k);
        M(-1);
        foreach (var l in Leases.OrderBy(l => l.Warehouse).ThenBy(l => l.Tenant)) { M(l.Warehouse); M(l.Tenant); M(l.Owner); M(l.M3); M(l.RentMites); M(l.FromSeason); M(l.PaidSeasons); }
        M(-2);
        foreach (var (k, d) in Depots) { M(k); M(d.TargetKg); M(d.Rotation ? 1 : 0); M(d.Filled ? 1 : 0); }
        M(-3);
        foreach (var l in Lots) { M(l.Depot); foreach (char c in l.Good) M(c); M(l.Grams); M(l.Season); }
        M(-4);
        foreach (var (k, s) in ArmouryStock) { M(k); M(s.Count); foreach (var i in s) M((long)i); }
        M(-5);
        foreach (var i in Issues) { M(i.Armoury); M((long)i.Item); M((long)i.Holder); M(i.Day); }
        M(-6);
        foreach (var (k, v) in Premises) { M((long)k); M(v); }
        M(-7);
        foreach (var (k, v) in BarracksOwner) { M(k); M((long)v); }
        M(-8);
        foreach (var (k, v) in CareDay) { M(k); M(v); }
        return h;
    }
}

[MessagePackObject]
public sealed class LeaseSave
{
    [Key(0)] public long Warehouse { get; set; }
    [Key(1)] public int Tenant { get; set; }
    [Key(2)] public int Owner { get; set; }
    [Key(3)] public int M3 { get; set; }
    [Key(4)] public int RentMites { get; set; }
    [Key(5)] public int FromSeason { get; set; }
    [Key(6)] public int PaidSeasons { get; set; }
}

[MessagePackObject]
public sealed class DepotSave
{
    [Key(0)] public long Depot { get; set; }
    [Key(1)] public long TargetKg { get; set; }
    [Key(2)] public bool Rotation { get; set; }
    [Key(3)] public bool Filled { get; set; }
}

[MessagePackObject]
public sealed class LotSave
{
    [Key(0)] public long Depot { get; set; }
    [Key(1)] public string Good { get; set; } = "";
    [Key(2)] public long Grams { get; set; }
    [Key(3)] public int Season { get; set; }
}

[MessagePackObject]
public sealed class ArmourySave
{
    [Key(0)] public long Armoury { get; set; }
    [Key(1)] public ulong[] Items { get; set; } = [];
    [Key(2)] public int CareDay { get; set; }
}

[MessagePackObject]
public sealed class IssueSave
{
    [Key(0)] public long Armoury { get; set; }
    [Key(1)] public ulong Item { get; set; }
    [Key(2)] public ulong Holder { get; set; }
    [Key(3)] public int Day { get; set; }
}

[MessagePackObject]
public sealed class PremisesSave
{
    [Key(0)] public ulong Business { get; set; }
    [Key(1)] public long Cell { get; set; }
}

[MessagePackObject]
public sealed class BarracksSave
{
    [Key(0)] public long Barracks { get; set; }
    [Key(1)] public ulong Owner { get; set; }
}

/// <summary>The optional "works" section (a name of <c>Phase7Seeding.SectionNames</c>), written only while the book is not empty, so a world with nothing set keeps its bytes. P7-LB2 appends keys 1 to 7; a section written by LB1 (key 0 only) reads with them empty.</summary>
[MessagePackObject]
public sealed class WorksSave
{
    public const string SectionName = "works";

    [Key(0)] public long[] SluiceShut { get; set; } = [];
    [Key(1)] public LeaseSave[] Leases { get; set; } = [];
    [Key(2)] public DepotSave[] Depots { get; set; } = [];
    [Key(3)] public LotSave[] Lots { get; set; } = [];
    [Key(4)] public ArmourySave[] Armouries { get; set; } = [];
    [Key(5)] public IssueSave[] Issues { get; set; } = [];
    [Key(6)] public PremisesSave[] Premises { get; set; } = [];
    [Key(7)] public BarracksSave[] Barracks { get; set; } = [];

    public static WorksSave Capture(WorksBook b)
    {
        var s = new WorksSave { SluiceShut = [.. b.SluiceShut] };
        if (!b.HasBuildings) return s;
        s.Leases = [.. b.Leases.OrderBy(l => l.Warehouse).ThenBy(l => l.Tenant).Select(l => new LeaseSave { Warehouse = l.Warehouse, Tenant = l.Tenant, Owner = l.Owner, M3 = l.M3, RentMites = l.RentMites, FromSeason = l.FromSeason, PaidSeasons = l.PaidSeasons })];
        s.Depots = [.. b.Depots.Values.Select(d => new DepotSave { Depot = d.Depot, TargetKg = d.TargetKg, Rotation = d.Rotation, Filled = d.Filled })];
        s.Lots = [.. b.Lots.Select(l => new LotSave { Depot = l.Depot, Good = l.Good, Grams = l.Grams, Season = l.Season })];
        var keys = b.ArmouryStock.Keys.Union(b.CareDay.Keys).OrderBy(k => k);
        s.Armouries = [.. keys.Select(k => new ArmourySave { Armoury = k, Items = b.ArmouryStock.TryGetValue(k, out var it) ? [.. it] : [], CareDay = b.CareDay.GetValueOrDefault(k) })];
        s.Issues = [.. b.Issues.Select(i => new IssueSave { Armoury = i.Armoury, Item = i.Item, Holder = i.Holder, Day = i.Day })];
        s.Premises = [.. b.Premises.Select(p => new PremisesSave { Business = p.Key, Cell = p.Value })];
        s.Barracks = [.. b.BarracksOwner.Select(p => new BarracksSave { Barracks = p.Key, Owner = p.Value })];
        return s;
    }

    public WorksBook Restore()
    {
        var b = new WorksBook();
        foreach (var k in SluiceShut) b.SluiceShut.Add(k);
        foreach (var l in Leases) b.Leases.Add(new WarehouseLease(l.Warehouse, l.Tenant, l.Owner, l.M3, l.RentMites, l.FromSeason, l.PaidSeasons));
        foreach (var d in Depots) b.Depots[d.Depot] = new DepotPolicy(d.Depot, d.TargetKg, d.Rotation, d.Filled);
        foreach (var l in Lots) b.Lots.Add(new DepotLot(l.Depot, l.Good, l.Grams, l.Season));
        foreach (var a in Armouries) { if (a.Items.Length > 0) b.ArmouryStock[a.Armoury] = [.. a.Items]; if (a.CareDay != 0) b.CareDay[a.Armoury] = a.CareDay; }
        foreach (var i in Issues) b.Issues.Add(new IssueRow(i.Armoury, i.Item, i.Holder, i.Day));
        foreach (var p in Premises) b.Premises[p.Business] = p.Cell;
        foreach (var p in Barracks) b.BarracksOwner[p.Barracks] = p.Owner;
        return b;
    }
}
