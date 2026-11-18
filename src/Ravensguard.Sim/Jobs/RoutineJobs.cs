using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Jobs;

/// <summary>
/// The jobs the households of a settlement put up each morning (live-behaviour spec 4.8, hour 0 of the day): about three per hundred households, paid by the household from its own purse, so a day's postings
/// are a draw on real coin and never a creation of it. Which household, which kind and how many hours come from stream <c>0x303</c> keyed on the settlement and the day, so the board is the same on every run.
/// A town lists them on its board at the square; a village's are heard from the reeve and the innkeeper.
/// </summary>
public static class RoutineJobs
{
    public static int PostDay(JobTable table, JobEnv env, Population pop, int region, int settlement, long day, ulong seed)
    {
        var econ = env.Econ; if (econ == null || settlement < 0 || settlement >= pop.Region.Settlements.Count) return 0;
        var rs = pop.Region.Settlements[settlement];
        var homes = new List<int>();
        for (int h = 0; h < pop.Households.Count && h < econ.HouseholdCount; h++) if (pop.Households.Settlement[h] == settlement) homes.Add(h);
        if (homes.Count == 0) return 0;
        int doy = new GameTime(day * 86400L).DayOfYear;
        int want = Math.Min(env.Tuning.BoardCap, (homes.Count * 3 + 99) / 100 * Travel.FairCalendar.JobBoardMult(doy, rs.Class));          // a fair multiplies the day's postings
        int stalls = Math.Min(Math.Max(0, env.Tuning.BoardCap - want), Travel.FairCalendar.StallBonus(doy, rs.Class) / 4);                    // and a fair needs hands to mind its stalls
        want += stalls;
        long now = day * ScheduleTuningDay, posted = 0;
        for (int k = 0; k < want; k++)
        {
            int h = homes[DetRng.Range(seed, day, (ulong)settlement, RngStreams.RoutineJobs, (uint)(k * 4), 0, homes.Count)];
            int kind = DetRng.Range(seed, day, (ulong)settlement, RngStreams.RoutineJobs, (uint)(k * 4 + 1), 0, rs.Parcels.Count > 0 ? 3 : 2);
            long hours = 4 + DetRng.Range(seed, day, (ulong)settlement, RngStreams.RoutineJobs, (uint)(k * 4 + 2), 0, 5);
            var sq = rs.Plan.Square; long sx = (sq.W > 0 ? sq.CenterX : rs.CenterX) * 1000L, sy = (sq.W > 0 ? sq.CenterY : rs.CenterY) * 1000L;
            long off = DetRng.Range(seed, day, (ulong)settlement, RngStreams.RoutineJobs, (uint)(k * 4 + 3), -6000, 6001);
            var poster = pop.Households.IdOf(h);
            OddJobKind ok = k >= want - stalls ? OddJobKind.TendShop : kind switch { 0 => OddJobKind.Guard, 1 => OddJobKind.TendShop, _ => OddJobKind.FarmWork };
            long wage = ok == OddJobKind.Guard ? JobPay.Guard(env.Tuning, env.UnskilledWage, hours) : JobPay.Labour(env.Tuning, env.UnskilledWage, hours);
            var job = table.Post(env, ok, poster, econ.HouseholdPurse[h], region, settlement, now, now + 2 * 86400L, wage, j =>
            {
                j.P0 = sx + off; j.P1 = sy + off / 2; j.P4 = hours; j.SrcOwner = poster; j.MinAge = 14;
                if (ok == OddJobKind.FarmWork)
                {
                    int pi = rs.Parcels[DetRng.Range(seed, day, (ulong)settlement, RngStreams.RoutineJobs, (uint)(k * 4 + 3), 0, rs.Parcels.Count)];
                    var pc = pop.Region.Parcels[pi]; j.P0 = ((long)pc.TileX * RegionSettings.TileM + RegionSettings.TileM / 2) * 1000; j.P1 = ((long)pc.TileY * RegionSettings.TileM + RegionSettings.TileM / 2) * 1000; j.P5 = pi;
                }
                j.Title = ok switch { OddJobKind.Guard => "Stand watch", OddJobKind.TendShop => k >= want - stalls ? "Mind a fair stall" : "Mind a stall", _ => "Field work" };
            });
            if (job != null) posted++;
        }
        return (int)posted;
    }

    private const long ScheduleTuningDay = 86400;
}
