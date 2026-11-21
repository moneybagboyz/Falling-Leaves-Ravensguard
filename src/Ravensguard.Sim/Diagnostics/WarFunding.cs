using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Taxes;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>
/// How the realm raises the coin of a campaign (P7-EA2's decision for the war-load soak, finding EA_F7). A host of 2,000 costs about 391,000 mites a day (pay 261,000, staple 94,000, fodder 36,000); a month is 11.7 million mites, 9% of
/// all the coin of the region (128 million), while the realm's own treasury holds nothing (4,277 mites at the Y6 muster on the audited tag). The audit's rule, which the military's <c>DaysFunded</c> (army-economy 4.9) follows: the realm
/// raises what the host needs from, in order, (1) <b>aid</b> from the fief treasuries, half of each balance (<see cref="TransferReason.Aid"/>, the feudal aid of the vassals who do not serve in person), (2) a <b>tallage</b> on the
/// households' own purses above a floor, an equal permille of the excess of each, capped at <see cref="TallageCapPermille"/> of it (<see cref="TransferReason.Tallage"/>). Every step is a plain ledger transfer in a fixed order (fiefs
/// in treasury order, households in index order), so coin is conserved and a run is deterministic. What the realm cannot raise, the host goes short of (WL-8: arrears and the stand-in desertion).
/// </summary>
public static class WarFunding
{
    /// <summary>The share of a fief treasury given as aid, per mille.</summary>
    public const int AidPermille = 500;
    /// <summary>A household keeps this much before it is tallaged (mites).</summary>
    public const long TallageFloorMites = 2_000;
    /// <summary>The most one tallage takes of the excess above the floor, per mille.</summary>
    public const int TallageCapPermille = 150;

    /// <summary>Raises up to <paramref name="target"/> mites into <paramref name="realm"/> (counting what it already holds); returns what was moved in aid and in tallage.</summary>
    public static (long Aid, long Tallage) Raise(EconomyWorld ew, PurseId realm, long target)
    {
        var econ = ew.World.Economy; var led = econ.Ledger; long aid = 0, tallage = 0;
        long short1() => target - led.Balance(realm).Mites;
        foreach (var t in ew.Taxes.Treasuries.All)
        {
            if (short1() <= 0) break;
            if (t.Kind != TreasuryKind.Fief) continue;
            long give = Math.Min(short1(), led.Balance(t.Purse).Mites * AidPermille / 1000);
            if (give > 0 && led.Transfer(t.Purse, realm, new Coin(give), TransferReason.Aid)) aid += give;
        }
        long need = short1();
        if (need > 0)
        {
            var hh = ew.World.Population.Households; long excess = 0;
            for (int i = 0; i < hh.Count; i++) { if (hh.MemberCount[i] <= 0) continue; long b = led.Balance(econ.HouseholdPurse[i]).Mites; if (b > TallageFloorMites) excess += b - TallageFloorMites; }
            if (excess > 0)
            {
                long permille = Math.Min(TallageCapPermille, (need * 1000 + excess - 1) / excess);
                for (int i = 0; i < hh.Count; i++)
                {
                    if (hh.MemberCount[i] <= 0) continue;
                    var p = econ.HouseholdPurse[i]; long b = led.Balance(p).Mites; if (b <= TallageFloorMites) continue;
                    long take = (b - TallageFloorMites) * permille / 1000; if (take <= 0) continue;
                    if (led.Transfer(p, realm, new Coin(take), TransferReason.Tallage)) tallage += take;
                }
            }
        }
        return (aid, tallage);
    }
}
