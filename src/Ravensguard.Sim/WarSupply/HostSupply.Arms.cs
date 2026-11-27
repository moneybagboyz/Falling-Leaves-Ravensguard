using Ravensguard.Sim.Continental;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Land;

namespace Ravensguard.Sim.WarSupply;

/// <summary>P7-AE3: the arms trade's seam to the continent and the ledger (army-economy 4.10). The arms industry itself is <see cref="ArmsWorld"/>.</summary>
public sealed partial class HostSupply
{
    /// <summary>The arms industry of the world, made on demand by the wiring (null: none; the season's trade is then skipped).</summary>
    public Func<ArmsWorld?>? ArmsSource { get; set; }

    /// <summary>The realms the continent knows (those a summary region names), in id order, with whether any of their regions is at war (the continent's war flags).</summary>
    public IReadOnlyList<(int Realm, bool AtWar)> ArmsRealms()
    {
        var link = Economy.World.Get<ContinentLink>(); if (link == null) return [];
        var war = new SortedDictionary<int, bool>();
        foreach (var s in link.State.Summaries) { if (s.Realm == 255) continue; war[s.Realm] = war.GetValueOrDefault(s.Realm) || s.WarFlags != 0; }
        return [.. war.Select(kv => (kv.Key, kv.Value))];
    }

    /// <summary>
    /// A caravan's payment, by the ledger only: between two realms with treasuries it is a <c>Trade</c> transfer; an importer with a treasury buying from a realm beyond the ledger pays it out as emigrant coin (a sink, carried by
    /// the continent link); an exporter with a treasury selling to a realm beyond the ledger is not paid, so the caravan does not run; two realms beyond the ledger move no coin. False: the caravan does not run.
    /// </summary>
    public bool PayForArms(int exporter, int importer, long mites)
    {
        var led = _econ.Ledger; var from = RealmPurse(importer); var to = RealmPurse(exporter);
        if (to.IsNone) return from.IsNone;
        if (mites <= 0) return true;
        if (!from.IsNone) return led.Balance(from).Mites >= mites && led.Transfer(from, to, new Coin(mites), TransferReason.Trade);
        return false;
    }

    /// <summary>
    /// The season's arms trade (S5): it runs only while some realm is at war (a peaceful continent keeps its arms trade out of the saved book) and only with an arms industry to run it. Returns the caravans.
    /// An importer with a treasury buying from a realm beyond the ledger pays out as emigrant coin.
    /// </summary>
    public ArmsTradeResult? ArmsSeason(int day)
    {
        var realms = ArmsRealms(); if (!realms.Any(r => r.AtWar)) return null;                                            // peace builds no arms world
        var arms = ArmsSource?.Invoke(); if (arms == null) return null;
        var link = Economy.World.Get<ContinentLink>();
        return arms.Season(day, realms, (e, i, m) =>
        {
            if (RealmPurse(e).IsNone && !RealmPurse(i).IsNone)
            {
                var led = _econ.Ledger; var from = RealmPurse(i);
                if (m <= 0) return true;
                if (link == null || led.Balance(from).Mites < m || !led.Sink(from, new Coin(m), SinkReason.Emigrate)) return false;
                link.Mine.EmigrantCoin += m; return true;
            }
            return PayForArms(e, i, m);
        });
    }
}
