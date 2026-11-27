using System.Security.Cryptography;

namespace Ravensguard.Sim.WarSupply;

/// <summary>An arms order a lord's steward placed at a season (army-economy 4.10): so many pieces of a shape for so many mites, not yet delivered.</summary>
public sealed record ArmsOrderRow(ulong Lord, int Day, string Shape, int Count, long Mites);

/// <summary>One arms caravan of the trade (kept as a short log): from realm, to realm, grams, mites, day, and whether it ran (0) or was stopped at the gate by an export ban (1).</summary>
public sealed record ArmsShipRow(int From, int To, long Grams, long Mites, int Day, byte Outcome);

/// <summary>One line of an arms job (P8-WS-2): a shape, how many, the price of each in mites, how many are in the making (<see cref="Making"/>) or made and on the road (<see cref="Made"/>, which a robbery takes back) and how many have arrived (<see cref="Delivered"/>).</summary>
public sealed class ArmsLine { public string Shape = ""; public int Count, Making, Made, Delivered; public long Price; }

/// <summary>
/// An arms order or a refit commission as a supply contract (P8-WS-2): the contract it stands on, whose it is, the armoury it is for, the maker who took it and the maker's purse, and its lines.
/// <see cref="Phase"/>: 0 open (no maker yet), 1 taken (the maker starts what is left as its materials allow). <see cref="Kind"/>: 0 an order (made and carted), 1 a commission (fitted at the armoury).
/// </summary>
public sealed class ArmsJob
{
    public int Contract; public byte Kind; public ulong Lord; public int Purse, Realm, Settlement; public long Armoury;
    public ulong Maker; public int MakerPurse = -1; public byte Phase;
    public List<ArmsLine> Lines = [];
}

/// <summary>
/// Goods or kit on the road (P8-WS-2): an order's pieces (items held by the transit's owner id, <see cref="ArmsSupply.TransitId"/>) or a trade's bar iron (a transit store of the same owner). <see cref="State"/>: 0 on the road,
/// 1 held at the gate (an export ban), 2 robbed, 3 arrived. <see cref="Mode"/>: 0 cart, 1 ship. <see cref="Kind"/>: 0 an order, 1 the trade.
/// </summary>
public sealed class ArmsTransit
{
    public int Id, Job; public byte Kind, Mode, State; public ulong From, To; public int FromRealm, ToRealm, Depart, Arrive;
    public long Grams; public int Good = -1; public long Qty; public List<ulong> Items = [];
}

/// <summary>A hired steward (P8-WS-2): a lord's seasonal order, placed within a budget in mites a season against the armoury's gaps.</summary>
public sealed class ArmsSteward { public ulong Lord; public long Armoury, BudgetMites; public int Kits, LastSeason = -1; }

/// <summary>
/// P7-AE3: the arms industry's saved state, in the optional <c>warsupply</c> section (key 12; absent while empty): orders placed and not delivered, the refit queue, the arms trade's realm stocks, bans and caravan log, and
/// the stats. All integers; every collection is iterated in key order.
/// </summary>
public sealed class ArmsBook
{
    public const int StatCount = 10;
    public List<ArmsOrderRow> Orders { get; } = [];
    /// <summary>Item id to the armoury (room key) it is refitted for, in item id order.</summary>
    public SortedDictionary<ulong, long> Refit { get; } = [];
    /// <summary>Realm to the grams of arms it holds (the continent's arms class, notional beyond the ledger).</summary>
    public SortedDictionary<int, long> Stock { get; } = [];
    /// <summary>Realm to its arms price index (permille of the base price), as of the last season.</summary>
    public SortedDictionary<int, int> Index { get; } = [];
    /// <summary>Export bans: key is <c>(exporter &lt;&lt; 32) | importer</c>.</summary>
    public SortedSet<long> Bans { get; } = [];
    public List<ArmsShipRow> Log { get; } = [];
    public const int LogCap = 16;
    /// <summary>0 grams shipped, 1 mites paid for them, 2 caravans stopped at the gate, 3 pieces ordered, 4 loot kept, 5 loot refitted, 6 grams of scrap salvaged, 7 grams of bar forged, 8 loot the holder kept (no place in the armoury; no sale), 9 grams of rags.</summary>
    public long[] Stats = new long[StatCount];

    /// <summary>P8-WS-2 (optional saved keys; empty while nothing is ordered): jobs by contract id, the road by transit id, the stewards by lord, the next transit id and the stats (0 orders placed, 1 pieces delivered, 2 mites paid, 3 mites cut for lateness, 4 pieces robbed, 5 grams of bar shipped, 6 pieces fitted by commission, 7 contracts stopped by a ban, 8 pieces short at the close).</summary>
    public SortedDictionary<int, ArmsJob> Jobs { get; } = [];
    public SortedDictionary<int, ArmsTransit> Transit { get; } = [];
    public SortedDictionary<ulong, ArmsSteward> Stewards { get; } = [];
    public int NextTransit = 1;
    public const int SupplyStatCount = 9;
    public long[] SupplyStats = new long[SupplyStatCount];
    public bool SupplyEmpty => Jobs.Count == 0 && Transit.Count == 0 && Stewards.Count == 0 && NextTransit == 1 && SupplyStats.All(x => x == 0);

    public static long BanKey(int exporter, int importer) => ((long)exporter << 32) | (uint)importer;
    public bool Banned(int exporter, int importer) => Bans.Contains(BanKey(exporter, importer));

    public bool IsEmpty => Orders.Count == 0 && Refit.Count == 0 && Stock.Count == 0 && Index.Count == 0 && Bans.Count == 0 && Log.Count == 0 && Stats.All(x => x == 0) && SupplyEmpty;

    /// <summary>Folded into <c>SupplyHash</c> only while not empty.</summary>
    public void Fold(IncrementalHash h)
    {
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        L(-77);
        foreach (var o in Orders) { L((long)o.Lord); L(o.Day); L(o.Shape.Length); h.AppendData(System.Text.Encoding.UTF8.GetBytes(o.Shape)); L(o.Count); L(o.Mites); }
        foreach (var (i, a) in Refit) { L((long)i); L(a); }
        foreach (var (r, g) in Stock) { L(r); L(g); }
        foreach (var (r, x) in Index) { L(r); L(x); }
        foreach (long k in Bans) L(k);
        foreach (var s in Log) { L(s.From); L(s.To); L(s.Grams); L(s.Mites); L(s.Day); L(s.Outcome); }
        foreach (long x in Stats) L(x);
        if (SupplyEmpty) return;                                                                                            // a world with no P8-WS-2 rows hashes as it did
        L(-78);
        foreach (var j in Jobs.Values)
        {
            L(j.Contract); L(j.Kind); L((long)j.Lord); L(j.Purse); L(j.Realm); L(j.Settlement); L(j.Armoury); L((long)j.Maker); L(j.MakerPurse); L(j.Phase);
            foreach (var l in j.Lines) { L(l.Shape.Length); h.AppendData(System.Text.Encoding.UTF8.GetBytes(l.Shape)); L(l.Count); L(l.Making); L(l.Made); L(l.Delivered); L(l.Price); }
        }
        foreach (var t in Transit.Values)
        {
            L(t.Id); L(t.Job); L(t.Kind); L(t.Mode); L(t.State); L((long)t.From); L((long)t.To); L(t.FromRealm); L(t.ToRealm); L(t.Depart); L(t.Arrive); L(t.Grams); L(t.Good); L(t.Qty);
            foreach (var i in t.Items) L((long)i);
        }
        foreach (var w in Stewards.Values) { L((long)w.Lord); L(w.Armoury); L(w.BudgetMites); L(w.Kits); L(w.LastSeason); }
        L(NextTransit); foreach (long x in SupplyStats) L(x);
    }
}
