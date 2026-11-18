using Ravensguard.Sim.Core;
using Ravensguard.Sim.Carts;

namespace Ravensguard.Sim.Animals;

/// <summary>Feed, water, shoes and lameness of an animal (horses-and-carts spec 4.2): distance-driven wear that never depends on the game speed, and daily bookkeeping in game days.</summary>
public static class AnimalCare
{
    /// <summary>
    /// What a metre of travel does: odometer, long-term fatigue (1 permille per 40 m at trot or gallop, x1.5 off the road, x2 in mud), shoe wear (1 permille per <c>shoe_life_km</c> of road metres, x2 rough,
    /// x0.2 on grass), a lost shoe (stream 0x353 keyed on the animal and the hundred metres of odometer: 5 in a million a hundred metres on a road, 40 in mud), and lameness from going on a bare or
    /// worn hoof over hard ground (3 km a level, 2 km after a lost shoe).
    /// </summary>
    public static void OnMove(Animal a, Breed b, long movedMm, Surface s, bool brisk, ulong seed)
    {
        long before = a.OdometerMm; a.OdometerMm += movedMm;
        if (brisk)
        {
            int mult = s is Surface.MudRoad or Surface.DeepMud ? 2000 : s is Surface.Grass or Surface.Sand or Surface.DeepSnow ? 1500 : 1000;
            a.FatigueU += movedMm * mult / 40;
        }
        a.ShoeWearU += movedMm * SurfaceModel.ShoeWearTenths(s);
        long step = (long)b.Def.ShoeLifeKm * 10_000;
        while (a.ShoeWearU >= step)
        {
            a.ShoeWearU -= step;
            for (int i = 0; i < 4; i++) if (a.Shoes[i] > 0) a.Shoes[i]--;
        }
        int ppm = s is Surface.DeepMud ? 40 : s is Surface.Sand ? 40 : 5;
        for (long k = before / 100_000 + 1; k <= a.OdometerMm / 100_000; k++)
        {
            if (DetRng.Range(seed, 0, a.Id.Value, RngStreams.ShoeLoss, (uint)(k * 2), 0, 1_000_000) >= ppm) continue;
            int i = DetRng.Range(seed, 0, a.Id.Value, RngStreams.ShoeLoss, (uint)(k * 2 + 1), 0, 4);
            for (int j = 0; j < 4; j++) { int h = (i + j) & 3; if (a.Shoes[h] > 0) { a.Shoes[h] = 0; break; }
            }
        }
        if (SurfaceModel.IsHard(s) && a.LowestShoe < 200)
        {
            a.LameHardMm += movedMm;
            long need = a.AnyShoeLost ? 2_000_000 : 3_000_000;
            while (a.LameHardMm >= need) { a.LameHardMm -= need; if (a.Lameness < 3) a.Lameness++; }
        }
    }

    /// <summary>One game day of feeding: <paramref name="suppliedG"/> grams of hay-equivalent (oats count 1.8) against the ration; the reserve moves by the shortfall or surplus and never rises above one day (spec 4.2, H-10).</summary>
    public static void Feed(Animal a, Breed b, long suppliedG, bool working)
    {
        long need = working ? b.WorkRationG : b.IdleRationG;
        int fedPm = need <= 0 ? 1000 : (int)Math.Min(2000, suppliedG * 1000 / need);
        a.FeedCenti = Math.Min(100, a.FeedCenti + (fedPm - 1000) / 10);
        if (a.FeedCenti <= 0) { a.FeedCenti = 0; a.HealthPm = Math.Max(0, a.HealthPm - 50); }
        else if (fedPm >= 1000) a.HealthPm = Math.Min(1000, a.HealthPm + 10);
    }

    /// <summary>The ration, hay-equivalent grams, of hay and oats plus what it grazed (1.0 kg an hour up to 8 hours on grass).</summary>
    public static long Ration(long hayG, long oatsG, int grazeHours) => hayG + oatsG * 18 / 10 + Math.Min(8, Math.Max(0, grazeHours)) * 1000L;

    /// <summary>Drinks <paramref name="litres"/> (10 L per 30 s at a river, trough or well): water returns by the share of the day's need.</summary>
    public static void Drink(Animal a, Breed b, int litres)
    {
        a.WaterPpm = (int)Math.Min(1_000_000, a.WaterPpm + litres * 1_000_000L / Math.Max(1, b.Def.WaterLDay));
        a.ThirstHours = 0;
    }

    /// <summary>One game hour of thirst: water falls to nothing over 24 hours; the hours since the last drink are counted.</summary>
    public static void Hour(Animal a)
    {
        if (!a.Alive) return;
        a.WaterPpm = Math.Max(0, a.WaterPpm - 41_667);
        a.ThirstHours++;
    }

    /// <summary>
    /// The day's changes for an animal that rested: fatigue falls 500 permille a night with feed (more rises by 200 if it went 8 hours without water), a stabled and fed animal heals one lameness level
    /// every 3 days, and a horse unusable for 15 days is put down. <paramref name="day"/> is the game day.
    /// </summary>
    public static void Day(Animal a, long day, bool stabled)
    {
        if (!a.Alive || a.LastDay == day) return;
        a.LastDay = day;
        if (a.ThirstHours >= 8) a.FatigueU += 200L * 1_000_000;
        if (a.FeedCenti > 0) a.FatigueU = Math.Max(0, a.FatigueU - 500L * 1_000_000);
        if (a.Lameness > 0 && stabled && a.FeedCenti >= 50)
        {
            if (++a.LameDays >= 3) { a.LameDays = 0; a.Lameness--; }
        }
        a.Lame3Days = a.Lameness >= 3 ? a.Lame3Days + 1 : 0;
        if (a.Lame3Days >= 15 || a.HealthPm <= 0) { a.State = AnimalState.Dead; a.Vehicle = EntityId.None; }
    }

    /// <summary>Reshoes an animal (a farrier's half hour): every hoof back to 1000; the lameness from a bare hoof clears one level.</summary>
    public static void Reshoe(Animal a) { for (int i = 0; i < 4; i++) a.Shoes[i] = 1000; a.ShoeWearU = 0; a.LameHardMm = 0; }
}
