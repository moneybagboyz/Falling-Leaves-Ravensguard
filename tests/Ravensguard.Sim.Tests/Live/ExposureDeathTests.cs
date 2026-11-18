using Ravensguard.Sim.Clothing;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Tests.RegionsB;

namespace Ravensguard.Sim.Tests.Live;

/// <summary>
/// Phase 3 cleanup: a death of cold seen by the live tier (<see cref="PeopleBridge.TakeExposureDeaths"/>) reaches the person record as <see cref="DeathCause.Cold"/>, the population tables
/// and the chronicle, at the region's next barrier.
/// </summary>
public class ExposureDeathTests
{
    [Fact]
    public void A_live_cold_death_is_on_the_person_record_in_the_tables_and_in_the_chronicle()
    {
        var rig = new RegionRig();
        var home = rig.Set.HomeWorld!; var pt = home.Population.People;
        int[] rows = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && home.Population.Age(i) is > 20 and < 60).Take(2).ToArray();
        var cold = pt.IdOf(rows[0]); var heat = pt.IdOf(rows[1]);
        long coldBefore = home.Sim.Vital.DeathsByCause[(int)DeathCause.Cold], alive = home.Sim.Vital.Alive;
        int chron = home.Chronicle.All.Count;
        long now = (home.HourDone + 1) * 3600 - 600;
        // queued in id order whatever order the bridge reported them in; a person of a region not resident is skipped
        var stray = EntityId.MakeIn(IdKind.Person, (home.Coord.Id + 7) % 256, 5);
        Assert.Equal(2, RuntimePeople.QueueExposureDeaths(rig.Set.Resident, [(heat, ExposureDeath.Heat), (stray, ExposureDeath.Cold), (cold, ExposureDeath.Cold)], now));
        Assert.Equal(PersonStatus.Alive, pt.Status[rows[0]]);                    // nothing changes before the barrier
        home.AdvanceTo(home.HourDone + 1);
        Assert.Equal(PersonStatus.Dead, pt.Status[rows[0]]);
        Assert.Equal(DeathCause.Cold, pt.DeathCause[rows[0]]);
        Assert.Equal(DeathCause.Other, pt.DeathCause[rows[1]]);
        Assert.Equal(coldBefore + 1, home.Sim.Vital.DeathsByCause[(int)DeathCause.Cold]);
        Assert.True(home.Sim.Vital.Alive <= alive - 2, "both deaths are counted off the living");
        var deaths = home.Chronicle.All.Skip(chron).Where(e => e.TypeId == "death").ToList();
        Assert.Contains(deaths, e => e.Subjects.Contains(cold) && e.Data["cause"] == "Cold");
        // the deaths travel with the region's section
        var back = Ravensguard.Sim.Worlds.RegionWorld.Restore(rig.Sv, home.Coord, home.Population.Region, Ravensguard.Sim.Save.SaveCodec.DeserializeCompressed<Ravensguard.Sim.Save.RegionSectionData>(home.CaptureBytes()));
        Assert.Equal(DeathCause.Cold, back.Population.People.DeathCause[rows[0]]);
    }
}
