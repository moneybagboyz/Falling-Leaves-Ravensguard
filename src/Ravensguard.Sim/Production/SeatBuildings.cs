using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Production;

/// <summary>One building a realm's seat is meant to have from Phase 7: which plan, in which settlement, turned how many quarter turns (keyed on the world seed and the settlement's id, never the day).</summary>
public readonly record struct SeatBuilding(int Settlement, string Plan, int Turns);

/// <summary>
/// P7-LB2 (large-buildings 4.5, overview 7): which settlements are realm seats and which large buildings each is to be given, as a pure function of the region and the world seed. A realm's seat in the region is
/// the seat of its most populous fief (ties by fief index); it gets an <c>armoury_keep</c> and a <c>granary_depot</c>. Placing them as finished construction on free plots (the Phase 7 seeding step's
/// "armoury_depots") and stocking them (army-economy 4.6, 4.10) are not built here: this is the list the step will walk.
/// </summary>
public static class SeatBuildings
{
    public const string Keep = "armoury_keep", Depot = "granary_depot";

    /// <summary>The settlement index of each realm's seat in the region, by realm id ascending.</summary>
    public static IReadOnlyDictionary<int, int> Seats(Region region)
    {
        var best = new SortedDictionary<int, (int Pop, int Fief)>();
        for (int f = 0; f < region.Fiefs.Count; f++)
        {
            var fi = region.Fiefs[f]; if (fi.Realm < 0 || fi.SeatSettlement < 0 || fi.SeatSettlement >= region.Settlements.Count) continue;
            if (!best.TryGetValue(fi.Realm, out var b) || fi.Pop > b.Pop) best[fi.Realm] = (fi.Pop, f);
        }
        var seats = new SortedDictionary<int, int>();
        foreach (var (realm, (_, f)) in best) seats[realm] = region.Fiefs[f].SeatSettlement;
        return seats;
    }

    /// <summary>The buildings to place: for each seat (settlement ascending) the keep and the depot, each with a turn from the seed and the settlement's id.</summary>
    public static IReadOnlyList<SeatBuilding> Plan(Region region, ulong seed)
    {
        var list = new List<SeatBuilding>();
        foreach (int s in Seats(region).Values.Distinct().OrderBy(s => s))
        {
            ulong id = region.Settlements[s].Id.Value;
            list.Add(new SeatBuilding(s, Keep, (int)(DetRng.Mix(DetRng.Mix(seed ^ 0x4C42324B454550UL) ^ id) % 4)));
            list.Add(new SeatBuilding(s, Depot, (int)(DetRng.Mix(DetRng.Mix(seed ^ 0x4C42324445504FUL) ^ id) % 4)));
        }
        return list;
    }
}
