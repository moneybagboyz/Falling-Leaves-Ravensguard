using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Law;

/// <summary>
/// The guards' shifts (crime-and-justice 4.4): the watch of a settlement works in two shifts, day (06 to 18) and night (18 to 06), the men alternating by person id, and at night a quarter of the night men
/// sleep after 02:00. Pure functions of the people table and the hour (no draw), so a test and the running game agree on who is awake. A fief's gaoler is the first guard, by id, of its seat.
/// </summary>
public static class Guards
{
    public enum Shift : byte { Day, Night }

    /// <summary>The living guards living in a settlement, in person id order (the table's row order is the id order).</summary>
    public static List<int> Of(PersonTable pt, int settlement)
    {
        var l = new List<int>();
        var occ = pt.Occupation; var home = pt.HomeSettlement;
        for (int i = 0; i < pt.Count; i++)
            if (occ[i] == Occupation.Guard && home[i] == settlement && pt.IsAlive(i)) l.Add(i);        // the cheap tests first: most rows are not guards (and the dead rows are many)
        return l;
    }

    /// <summary>P8-PF-3: <see cref="Of"/> for every settlement in one pass over the table (the same lists, the same order): a caller that needs them all does not walk the table once a settlement.</summary>
    public static List<int>[] AllBySettlement(PersonTable pt, int settlements)
    {
        var all = new List<int>[settlements];
        for (int s = 0; s < settlements; s++) all[s] = [];
        var occ = pt.Occupation; var home = pt.HomeSettlement;
        for (int i = 0; i < pt.Count; i++)
            if (occ[i] == Occupation.Guard && (uint)home[i] < (uint)settlements && pt.IsAlive(i)) all[home[i]].Add(i);
        return all;
    }

    /// <summary>The guard's shift: the first of the settlement's guards (by id) works the day, the next the night, and so on.</summary>
    public static Shift ShiftOf(IReadOnlyList<int> guards, int row)
    {
        for (int k = 0; k < guards.Count; k++) if (guards[k] == row) return k % 2 == 0 ? Shift.Day : Shift.Night;
        return Shift.Day;
    }

    /// <summary>Whether the day is the hour of day's daylight shift (06 to 18).</summary>
    public static bool IsDayHour(int hourOfDay) => hourOfDay is >= 6 and < 18;

    /// <summary>The guards awake and on watch at an hour of day: the shift's men, but a quarter of the night men (every fourth) sleep from 02:00 until the shift ends.</summary>
    public static List<int> OnDuty(PersonTable pt, int settlement, int hourOfDay)
    {
        var all = Of(pt, settlement); var on = new List<int>();
        bool day = IsDayHour(hourOfDay); int nightK = 0;
        for (int k = 0; k < all.Count; k++)
        {
            bool dayMan = k % 2 == 0;
            if (day == dayMan)
            {
                if (!dayMan)
                {
                    int nk = nightK++;
                    if (hourOfDay is >= 2 and < 6 && nk % 4 == 3) continue;                 // asleep: every fourth night man after 02:00
                }
                on.Add(all[k]);
            }
        }
        return on;
    }

    /// <summary>The gaoler of a settlement: its first guard, by id (the man whose honesty a bribe tests), or -1.</summary>
    public static int Gaoler(PersonTable pt, int settlement) { var g = Of(pt, settlement); return g.Count == 0 ? -1 : g[0]; }

    /// <summary>How many guards a settlement should have (about one in sixty in a town, one a manor in a village): the seeding's figure, for the Law panel.</summary>
    public static int Wanted(int population) => Math.Max(1, population / 60);
}
