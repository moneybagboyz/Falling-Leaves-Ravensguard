using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Rule;

/// <summary>
/// Customs at the realm's gates (rule-and-income 4.4): a caravan that sells or buys across the border pays the realm a share of the declared value (3% by default, 10% on wool leaving, 5% on wine entering; a ruler may set
/// a rate of 0 to 15%), a real transfer from the caravan's account to the realm's treasury (<c>Customs</c>). Above 8% the gate sees fewer caravans, 10% fewer for each point. The realm's collector handles the money, so a
/// corrupt one keeps his share. Smuggling is not built (the cut order drops it first).
/// </summary>
public static class RuleCustoms
{
    /// <summary>The rate in percent on a good moving a way: the ruler's own rate when he set one, else the content row for the good, else the default (RI-9).</summary>
    public static int RatePct(RuleBook b, int realm, string good, bool export)
    {
        if (b.Customs.TryGetValue(realm, out int set)) return set;
        foreach (var r in b.CustomsRates)
        {
            if (r.Good != good) continue;
            int v = export ? r.ExportPct : r.ImportPct;
            if (v >= 0) return v;
        }
        return b.Settings.CustomsDefaultPct;
    }

    /// <summary>The realm a caravan of this home market belongs to, -1 for none.</summary>
    public static int RealmOf(SimWorld w, int settlement)
    {
        var region = w.Population.Region; if ((uint)settlement >= (uint)region.Settlements.Count) return -1;
        var st = region.Settlements[settlement];
        return st.FiefIndex >= 0 && st.FiefIndex < region.Fiefs.Count && region.Fiefs[st.FiefIndex].Realm >= 0 ? region.Fiefs[st.FiefIndex].Realm : st.Realm;
    }

    /// <summary>
    /// A caravan's declared goods cross the gate (the continent tick's gate trade): the duty on <paramref name="valueMites"/> of <paramref name="good"/> is paid from the caravan's <paramref name="account"/> to the realm's
    /// treasury, up to what the account holds, and handled by the realm's collector. Returns what was paid; 0 when the world keeps no rule book.
    /// </summary>
    public static long Charge(SimWorld w, PurseId account, int settlement, string good, long valueMites, bool export)
    {
        var b = w.Get<RuleBook>(); if (b == null || valueMites <= 0) return 0;
        int realm = RealmOf(w, settlement); if (realm < 0) return 0;
        var c = new RuleCtx { W = w, B = b, Ew = w.Get<EconomyWorld>() };
        var treasury = c.PurseOf(Party.OfRealm(realm)); if (treasury.IsNone) return 0;
        long duty = RuleMath.Duty(valueMites, RatePct(b, realm, good, export)); if (duty <= 0) return 0;
        var led = w.Economy.Ledger; long pay = Math.Min(duty, led.Balance(account).Mites);
        if (pay <= 0 || !led.Transfer(account, treasury, new Coin(pay), TransferReason.Customs)) return 0;
        var collector = b.Offices.FirstOrDefault(o => o.InOffice && o.Kind == OfficeKind.CustomsCollector && o.Realm == realm && o.Fief < 0);
        if (collector != null) RuleOfficials.Handle(c, collector, Party.OfRealm(realm), pay, duty);
        return pay;
    }

    /// <summary>
    /// Whether a merchant of this market plans a trip today (4.4): above the free rate the gate's caravans come in fewer (10% fewer a point), staggered by day and business with no draw (a rate of 12% lets 60% of the
    /// days through). With the default rate every day passes.
    /// </summary>
    public static bool AllowTrip(SimWorld w, int settlement, int day, int business)
    {
        var b = w.Get<RuleBook>(); if (b == null) return true;
        int realm = RealmOf(w, settlement); if (realm < 0) return true;
        int pct = RuleMath.CaravanPct(b.Settings, b.CustomsPct(realm)); if (pct >= 100) return true;
        return ((day * 7L + business * 13L) % 100 + 100) % 100 < pct;
    }
}
