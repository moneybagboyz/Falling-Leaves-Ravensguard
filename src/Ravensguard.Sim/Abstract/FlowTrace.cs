using System.Text;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;

namespace Ravensguard.Sim.Abstract;

/// <summary>
/// The goods-flow trace of the soak (<c>soak --flow</c>): for each staple chain good it records, per game year, what is created and by whom (holder kind x reason),
/// the moves from one holder kind to another (market sales and purchases show as moves to and from the market), what is destroyed (eaten, spoiled, sown, toll...) and
/// what is held at the year end by each holder kind, with the part that sat there for the last 60 days (the minimum of the ten six-day samples). Diagnostics only.
/// </summary>
public sealed class FlowTrace : IFlowTrace
{
    public static readonly string[] Goods = ["wheat", "rye", "barley", "oats", "flour", "bread", "malt", "ale"];
    private static readonly string[] Names =
    [
        "gentry", "burgher", "peasant-landed", "peasant-landless", "barn", "parish", "fodder", "market", "mill", "bakery", "brewery", "tavern", "merchant", "manor", "other-business", "other",
    ];
    private const int KGentry = 0, KBurgher = 1, KPeasantLanded = 2, KPeasantLandless = 3, KBarn = 4, KParish = 5, KFodder = 6, KMarket = 7, KMill = 8, KBakery = 9, KBrewery = 10, KTavern = 11, KMerchant = 12, KManor = 13, KOtherBiz = 14, KOther = 15;
    private readonly int K = Names.Length;
    private readonly EconomyWorld _ew; private readonly SimWorld _w;
    private readonly int[] _tracked;               // good id -> index in Goods, -1 if untracked
    private readonly long[,,] _created, _destroyed;
    private readonly long[,,] _moved;
    private readonly List<long[,]> _samples = [];
    private readonly int[] _bizKind;

    public FlowTrace(SimWorld w, EconomyWorld ew)
    {
        _w = w; _ew = ew; var cat = w.Economy.Goods;
        _tracked = new int[cat.Count]; Array.Fill(_tracked, -1);
        for (int i = 0; i < Goods.Length; i++) if (cat.TryIdOf(Goods[i], out ushort g)) _tracked[g] = i;
        _created = new long[K, Goods.Length, MassLedger.CreateReasons]; _destroyed = new long[K, Goods.Length, MassLedger.DestroyReasons]; _moved = new long[K, K, Goods.Length];
        _bizKind = [];
        w.Economy.Mass.Trace = this;
    }

    private int KindOf(Store s)
    {
        var id = s.Owner;
        if (id.Kind == IdKind.Household)
        {
            int h = _w.Households.IndexOf(id); if (h < 0) return KOther;
            return _w.Households.Class[h] switch { SocialClass.Gentry => KGentry, SocialClass.Burgher => KBurgher, _ => _w.Households.ParcelCount[h] > 0 ? KPeasantLanded : KPeasantLandless };
        }
        ulong c = id.Counter;
        if ((c >> 40) == 0xFA) return (int)((c >> 32) & 0xFF) switch { 1 => KBarn, 2 => KParish, 3 => KFodder, _ => KOther };
        if (EconIds.IsBusiness(id))
        {
            var prod = _ew.Production; int i = EconIds.BusinessIndex(id); if (i < 0 || i >= prod.Businesses.Count) return KOtherBiz;
            return prod.KindOf(prod.Businesses[i]).Id switch { "mill" => KMill, "bakery" => KBakery, "brewery" => KBrewery, "tavern" => KTavern, "merchant_stall" => KMerchant, "manor" => KManor, _ => KOtherBiz };
        }
        if (c >= EconIds.MarketBase && c < EconIds.SiteBase) return KMarket;
        return KOther;
    }

    public void Created(Store s, ushort good, CreateReason why, long qty) { int t = _tracked[good]; if (t >= 0) _created[KindOf(s), t, (int)why] += qty; }
    public void Destroyed(Store s, ushort good, DestroyReason why, long qty) { int t = _tracked[good]; if (t >= 0) _destroyed[KindOf(s), t, (int)why] += qty; }
    public void Moved(Store from, Store to, ushort good, long qty) { int t = _tracked[good]; if (t >= 0) _moved[KindOf(from), KindOf(to), t] += qty; }

    private long[,] Holdings()
    {
        var h = new long[K, Goods.Length]; var e = _w.Economy; var hht = _w.Households;
        void Add(Store s) { int k = -1; foreach (var (g, q) in s.Rows()) { int t = _tracked[g]; if (t < 0) continue; if (k < 0) k = KindOf(s); h[k, t] += q; } }
        for (int i = 0; i < hht.Count; i++) if (hht.MemberCount[i] > 0) Add(e.HouseholdStore[i]);
        foreach (var s in e.OtherStores.Values) Add(s);
        return h;
    }

    /// <summary>Called every sixth day; keeps the last ten samples.</summary>
    public void Sample() { _samples.Add(Holdings()); if (_samples.Count > 10) _samples.RemoveAt(0); }

    /// <summary>The year's table; clears the counters for the next year.</summary>
    public string CloseYear(int year, bool print)
    {
        var sb = new StringBuilder(); var end = Holdings();
        string T(long q) => (q / 1e6).ToString("0.0");
        var cat = _w.Economy.Goods;
        for (int t = 0; t < Goods.Length && print; t++)
        {
            sb.AppendLine($"[flow y{year}] {Goods[t]} (tonnes; ale in kilolitres)");
            var c = new List<string>();
            for (int k = 0; k < K; k++) for (int r = 0; r < MassLedger.CreateReasons; r++) if (_created[k, t, r] >= 50_000) c.Add($"{Names[k]}:{(CreateReason)r} {T(_created[k, t, r])}");
            sb.AppendLine("   created   " + string.Join(" | ", c));
            var m = new List<(long q, string s)>();
            for (int a = 0; a < K; a++) for (int b = 0; b < K; b++) if (a != b && _moved[a, b, t] >= 50_000) m.Add((_moved[a, b, t], $"{Names[a]}>{Names[b]} {T(_moved[a, b, t])}"));
            sb.AppendLine("   moved     " + string.Join(" | ", m.OrderByDescending(x => x.q).Take(14).Select(x => x.s)));
            var d = new List<string>();
            for (int k = 0; k < K; k++) for (int r = 0; r < MassLedger.DestroyReasons; r++) if (_destroyed[k, t, r] >= 50_000) d.Add($"{Names[k]}:{(DestroyReason)r} {T(_destroyed[k, t, r])}");
            sb.AppendLine("   destroyed " + string.Join(" | ", d));
            var st = new List<string>();
            for (int k = 0; k < K; k++)
            {
                long min = end[k, t]; foreach (var smp in _samples) min = Math.Min(min, smp[k, t]);
                if (end[k, t] >= 50_000) st.Add($"{Names[k]} {T(end[k, t])} (60d {T(min)})");
            }
            sb.AppendLine("   held      " + string.Join(" | ", st));
        }
        Array.Clear(_created); Array.Clear(_destroyed); Array.Clear(_moved);
        return sb.ToString().TrimEnd();
    }
}
