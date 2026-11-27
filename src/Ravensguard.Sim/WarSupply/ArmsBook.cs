using System.Security.Cryptography;

namespace Ravensguard.Sim.WarSupply;

/// <summary>An arms order a lord's steward placed at a season (army-economy 4.10): so many pieces of a shape for so many mites, not yet delivered.</summary>
public sealed record ArmsOrderRow(ulong Lord, int Day, string Shape, int Count, long Mites);

/// <summary>One arms caravan of the trade (kept as a short log): from realm, to realm, grams, mites, day, and whether it ran (0) or was stopped at the gate by an export ban (1).</summary>
public sealed record ArmsShipRow(int From, int To, long Grams, long Mites, int Day, byte Outcome);

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
    /// <summary>0 grams shipped, 1 mites paid for them, 2 caravans stopped at the gate, 3 pieces ordered, 4 loot kept, 5 loot refitted, 6 grams of scrap salvaged, 7 grams of bar forged, 8 loot sold, 9 grams of rags.</summary>
    public long[] Stats = new long[StatCount];

    public static long BanKey(int exporter, int importer) => ((long)exporter << 32) | (uint)importer;
    public bool Banned(int exporter, int importer) => Bans.Contains(BanKey(exporter, importer));

    public bool IsEmpty => Orders.Count == 0 && Refit.Count == 0 && Stock.Count == 0 && Index.Count == 0 && Bans.Count == 0 && Log.Count == 0 && Stats.All(x => x == 0);

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
    }
}
