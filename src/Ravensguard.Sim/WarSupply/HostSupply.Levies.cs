using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.WarSupply;

/// <summary>P7-AM-B: the few calls the muster makes of the army economy beyond <see cref="HostSupply.Muster"/> and <see cref="HostSupply.Release"/>.</summary>
public sealed partial class HostSupply
{
    /// <summary>The realm's treasury purse (none when the realm has no treasury in the ledger).</summary>
    public PurseId RealmTreasury(int realm) => RealmPurse(realm);

    /// <summary>
    /// <see cref="HarvestRisk"/> with its price: the kilograms of harvest the fief's households lose if <paramref name="menAbsent"/> men miss <paramref name="days"/> days of the reap window, and what they are worth at the region's staple
    /// mid (mites). The ruler weighs the second against what the rest of his campaign is worth (armies-on-the-map 4.10); the chronicle names it.
    /// </summary>
    public (long Kg, long Mites) HarvestRiskPriced(int fief, int menAbsent, int days)
    {
        long kg = HarvestRisk(fief, menAbsent, days);
        return (kg, MitesOf(kg * 1000, StapleMidMicro()));
    }

    /// <summary>Pays the levies' arrears as far as the chest goes while the men are still on the host's books (so the coin reaches their own households, as the day's wage does), before they are released.</summary>
    public void PayDownArrears(HostId h) { var a = Acc(h); PayArrears(a, Resolve(a), null); }

    /// <summary>The pay chest's balance (mites) and tomorrow's pay (mites): what the ruler reads to decide whether to keep paying levies past their owed days.</summary>
    public (long Chest, long PayTomorrow) PayState(HostId h, int day)
    {
        var a = Acc(h);
        return (_econ.Ledger.Balance(Resolve(a).Chest).Mites, NeedsOf(a, day).PayMites);
    }
}
