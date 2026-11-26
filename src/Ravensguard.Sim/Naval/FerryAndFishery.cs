using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Naval;

/// <summary>
/// A ferry business at a crossing (4.3, the Phase 5 carry-over): a ferry boat on its rope, a ferryman (Seamanship or Fishing 10 or more) and his purse. Each crossing takes the Phase 5 fares (15 mites a person
/// or animal, 40 a cart) from the passenger through the ledger (<see cref="Ferry.Cross"/>) and the day's takings are kept for the daily step; a passenger who cannot pay is not carried.
/// </summary>
public sealed class FerryBusiness
{
    public int Crossing { get; }
    public EntityId Ferry { get; }
    public EntityId Ferryman { get; set; }
    public PurseId Purse { get; }
    public long WidthMm { get; }
    public int Crossings { get; private set; }
    public long Takings { get; private set; }
    public long Seconds { get; private set; }

    public FerryBusiness(int crossing, EntityId ferry, EntityId ferryman, PurseId purse, long widthMm) { Crossing = crossing; Ferry = ferry; Ferryman = ferryman; Purse = purse; WidthMm = widthMm; }

    public static bool CanOperate(int seamanship, int fishing) => Math.Max(seamanship, fishing) >= 10;

    /// <summary>Carries a party across if it can pay; the fare goes to the ferryman's purse.</summary>
    public FerryCrossing Take(Ledger led, PurseId payer, int people, int animals, int carts, WaterTuning wt)
    {
        var x = Carts.Ferry.Cross(led, payer, Purse, WidthMm, people, animals, carts, wt);
        if (!x.Paid) return x;
        Crossings++; Takings += x.Fee; Seconds += x.Seconds;
        return x;
    }

    /// <summary>The daily step (H5 hour 0): the takings of the day are reported and reset (they are already in the ferryman's purse).</summary>
    public long Daily() { long t = Takings; Takings = 0; return t; }
}

/// <summary>
/// The fishery's daily run (4.3, H5 hour 0): each fishing boat goes out with its crew and takes 12 kg a crew member from the water's stock (x0.4 in winter) into its well (<see cref="Fishing.Day"/>), and the
/// catch lands as goods in the harbour's or the bank's store. Boats in id order, so the stock is shared the same way every run.
/// </summary>
public static class FisheryRun
{
    /// <summary>One day: returns the grams landed. <paramref name="crew"/> is a boat's crew count (at most the rowlocks it has, at least 1).</summary>
    public static long Day(VehicleTable vehicles, IEnumerable<(VehicleRow Boat, int Crew)> boats, ushort fish, bool winter, ref long stockG, Store landing, WaterTuning wt)
    {
        long landed = 0;
        foreach (var (boat, crew) in boats.OrderBy(b => b.Boat.Id.Value))
        {
            Fishing.Day(boat, vehicles, fish, crew, winter, ref stockG, wt);
            landed += Store.MoveUpTo(boat.Cargo, landing, fish, boat.Cargo.Qty(fish));
        }
        return landed;
    }
}
