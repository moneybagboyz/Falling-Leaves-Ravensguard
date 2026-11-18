using System.Text;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Production;

namespace Ravensguard.Sim.Abstract;

/// <summary>
/// The coin-flow trace of the soak (<c>soak --flow</c> or <c>--coin</c>, integration decision I35): every purse is given a holder kind (lords, other gentry, the parish,
/// merchants' and masters' households, burghers, peasants with and without land, treasuries, the continent, and the cash boxes of manors, merchants, workshops and
/// crews), and for each game year it records the coin held by each kind at the start and the end, the coin each kind received and paid by transfer reason, and the
/// largest flows from one kind to another. It watches the ledger (<see cref="Ledger.Observer"/>) and moves nothing. Diagnostics only.
/// </summary>
public sealed class CoinFlowTrace
{
    public static readonly string[] Names =
    [
        "lord", "gentry", "parish", "merchant-hh", "master-hh", "retainer-hh", "burgher", "peasant-landed", "landless",
        "treasury", "continent", "manor-box", "merchant-box", "workshop-box", "crew-box", "market-other",
    ];
    public const int KLord = 0, KGentry = 1, KParish = 2, KMerchantHh = 3, KMasterHh = 4, KRetainerHh = 5, KBurgher = 6, KPeasant = 7, KLandless = 8,
        KTreasury = 9, KContinent = 10, KManorBox = 11, KMerchantBox = 12, KWorkshopBox = 13, KCrewBox = 14, KOther = 15;
    private const int HouseholdKinds = 9;
    private static readonly int R = Ledger.TransferReasons;
    private readonly int K = Names.Length;
    private readonly SimWorld _w; private readonly EconomyWorld _ew;
    private int[] _kindOf = [];
    private readonly long[,,] _flow;
    private long[] _start;

    public CoinFlowTrace(SimWorld w, EconomyWorld ew)
    {
        _w = w; _ew = ew;
        _flow = new long[K, K, R];
        Refresh();
        _start = Holdings().Held;
        w.Economy.Ledger.Observer = OnTransfer;
    }

    private void OnTransfer(PurseId from, PurseId to, TransferReason why, long mites) => _flow[KindOf(from), KindOf(to), (int)why] += mites;

    private int KindOf(PurseId p)
    {
        if (p.Index >= _kindOf.Length) Refresh();
        return _kindOf[p.Index];
    }

    /// <summary>Re-classifies every purse (households change class, land and trade; businesses change owner). Called every sixth day and at year ends.</summary>
    public void Refresh()
    {
        var led = _w.Economy.Ledger; var hht = _w.Households; var pop = _w.Population; var prod = _ew.Production;
        var hhKind = new int[hht.Count];
        for (int h = 0; h < hht.Count; h++)
            hhKind[h] = hht.Class[h] == SocialClass.Gentry ? KGentry : hht.Class[h] == SocialClass.Burgher ? KBurgher : hht.ParcelCount[h] > 0 ? KPeasant : KLandless;
        for (int h = 0; h < hht.Count; h++)
        {
            if (hhKind[h] is KGentry or KMerchantHh or KMasterHh) continue;
            foreach (int m in hht.Members(h)) if (_ew.Labour.ManorLordOf(m) >= 0) { hhKind[h] = KRetainerHh; break; }
        }
        foreach (var b in prod.Businesses)
        {
            if (b.State != BusinessState.Open || b.OwnerHousehold < 0 || b.OwnerHousehold >= hht.Count || hht.Class[b.OwnerHousehold] == SocialClass.Gentry) continue;
            var k = prod.KindOf(b);
            if (k.Trader) hhKind[b.OwnerHousehold] = KMerchantHh;
            else if (hhKind[b.OwnerHousehold] != KMerchantHh) hhKind[b.OwnerHousehold] = KMasterHh;
        }
        foreach (int h in pop.PriestHousehold) if (h >= 0 && h < hht.Count) hhKind[h] = KParish;
        foreach (int h in pop.FiefLordHousehold) if (h >= 0 && h < hht.Count) hhKind[h] = KLord;
        int n = led.PurseCount;
        if (_kindOf.Length < n) _kindOf = new int[Math.Max(n, _kindOf.Length * 2)];
        for (int i = 0; i < n; i++)
        {
            var p = new PurseId(i); var id = led.OwnerOf(p);
            _kindOf[i] = led.KindOf(p) switch
            {
                PurseKind.Household => hht.IndexOf(id) is int h and >= 0 ? hhKind[h] : KOther,
                PurseKind.Treasury => id == Demography.ContinentOwner ? KContinent : KTreasury,
                PurseKind.Chest => KParish,
                PurseKind.Business => BusinessKind(id),
                _ => KOther,
            };
        }
    }

    private int BusinessKind(EntityId id)
    {
        var prod = _ew.Production; int i = EconIds.BusinessIndex(id);
        if (i < 0 || i >= prod.Businesses.Count) return KOther;
        var k = prod.KindOf(prod.Businesses[i]);
        if (k.Trader) return KMerchantBox;
        if (k.Id == "manor") return KManorBox;
        if (k.Extractions.Length > 0 || k.Id is "mine" or "charcoal_crew") return KCrewBox;
        return KWorkshopBox;
    }

    private (long[] Held, int[] Count, long[] Median, long[] P90) Holdings()
    {
        var led = _w.Economy.Ledger; var held = new long[K];
        for (int i = 0; i < led.PurseCount; i++) held[KindOf(new PurseId(i))] += led.Balance(new PurseId(i)).Mites;
        var per = new List<long>[HouseholdKinds]; for (int k = 0; k < HouseholdKinds; k++) per[k] = [];
        var hht = _w.Households; var e = _w.Economy;
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0) continue;
            int k = KindOf(e.HouseholdPurse[h]); if (k < HouseholdKinds) per[k].Add(led.Balance(e.HouseholdPurse[h]).Mites);
        }
        var cnt = new int[K]; var med = new long[K]; var p90 = new long[K];
        for (int k = 0; k < HouseholdKinds; k++) { per[k].Sort(); cnt[k] = per[k].Count; if (cnt[k] > 0) { med[k] = per[k][cnt[k] / 2]; p90[k] = per[k][cnt[k] * 9 / 10]; } }
        return (held, cnt, med, p90);
    }

    /// <summary>Called every sixth day.</summary>
    public void Sample() => Refresh();

    /// <summary>The year's table (when <paramref name="print"/>); clears the counters for the next year.</summary>
    public string CloseYear(int year, bool print)
    {
        Refresh();
        var (held, cnt, med, p90) = Holdings();
        var sb = new StringBuilder();
        static string C(long mites) => (mites / 100).ToString("N0");
        if (print)
        {
            long total = held.Sum(), moved = 0; foreach (long x in _flow) moved += x;
            sb.AppendLine($"[coin y{year}] money supply {C(total)} crowns, moved by transfers this year {C(moved)} ({(double)moved / Math.Max(1, total):0.0} x the supply)");
            sb.AppendLine($"[coin y{year}] held (crowns, start -> end, share of the supply at the end; households: count, median, p90 per household):");
            for (int k = 0; k < K; k++)
            {
                if (held[k] == 0 && _start[k] == 0) continue;
                string hh = k < HouseholdKinds ? $"  [{cnt[k]} hh, median {C(med[k])}, p90 {C(p90[k])}]" : "";
                sb.AppendLine($"   {Names[k],-14} {C(_start[k]),11} -> {C(held[k]),11}  {100.0 * held[k] / Math.Max(1, total),5:0.0}%{hh}");
            }
            sb.AppendLine($"[coin y{year}] received / paid by reason (crowns; net = received - paid):");
            for (int k = 0; k < K; k++)
            {
                var ins = new long[R]; var outs = new long[R];
                for (int o = 0; o < K; o++) for (int r = 0; r < R; r++) { if (o == k) continue; ins[r] += _flow[o, k, r]; outs[r] += _flow[k, o, r]; }
                long ti = ins.Sum(), to = outs.Sum(); if (ti == 0 && to == 0) continue;
                string Part(long[] a) => string.Join(" ", Enumerable.Range(0, R).Where(r => a[r] >= 100).OrderByDescending(r => a[r]).Select(r => $"{(TransferReason)r} {C(a[r])}"));
                sb.AppendLine($"   {Names[k],-14} in {C(ti),10} [{Part(ins)}] | out {C(to),10} [{Part(outs)}] | net {C(ti - to)}");
            }
            var top = new List<(long Q, string S)>();
            for (int a = 0; a < K; a++) for (int b = 0; b < K; b++) for (int r = 0; r < R; r++) if (a != b && _flow[a, b, r] >= 100) top.Add((_flow[a, b, r], $"{Names[a]}>{Names[b]} {(TransferReason)r} {C(_flow[a, b, r])}"));
            sb.AppendLine($"[coin y{year}] largest flows: " + string.Join(" | ", top.OrderByDescending(x => x.Q).Take(24).Select(x => x.S)));
        }
        Array.Clear(_flow); _start = held;
        return sb.ToString().TrimEnd();
    }
}
