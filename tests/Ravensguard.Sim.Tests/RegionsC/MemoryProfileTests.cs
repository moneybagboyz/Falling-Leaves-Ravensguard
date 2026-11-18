using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.RegionsB;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.RegionsC;

/// <summary>Where the memory and the save bytes of a region go (seamless-regions notes N23, N24): the numbers the notes quote, with budgets that catch a regression.</summary>
[Collection(MemoryCollection.Name)]
public class MemoryProfileTests(ITestOutputHelper output)
{
    private static long Mem() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true); }
    private static double Mb(long bytes) => bytes / 1048576.0;

    [Fact]
    public void One_region_costs_about_thirty_megabytes_and_the_derived_terrain_layers_are_released()
    {
        var w = SeamWorld.Get(1234);
        var at = w.BlockOrigin.Offset(1, 1);
        var sv = new RegionServices(1234, w.Content, w.Continent, at);
        long m0 = Mem();
        var region = RegionGenerator.Generate(w.Continent, w.History, at, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        long m1 = Mem();
        var hash = region.ComputeHash();
        region.TrimDerived(); // after the populator, as the manager does
        long m2 = Mem();
        Assert.Equal(hash, region.ComputeHash());
        Assert.True(region.IsTrimmed);
        long m3 = m2;
        var rw = RegionWorld.CreateFresh(sv, at, pop);
        long m4 = Mem();
        rw.AdvanceTo(rw.HourDone + 48);
        long m5 = Mem();
        output.WriteLine($"terrain and people {Mb(m1 - m0):0.0} MB, trimmed by {Mb(m1 - m2):0.0}, people {pop.People.Count}, records {Mb(m4 - m3):0.0} (resource rows {rw.Economy.Resources.Count}), 48 h of ticking +{Mb(m5 - m4):0.0}; total {Mb(m5 - m0):0.0} MB");
        Assert.True(Mb(m1 - m2) > 3.5, "trimming releases the derived layers");
        Assert.True(Mb(m5 - m0) < 38, $"a populated region takes {Mb(m5 - m0):0.0} MB");
        GC.KeepAlive(rw); GC.KeepAlive(region);
    }

    [Fact]
    public void Nine_resident_regions_with_the_game_settings_fit_in_the_budget()
    {
        var rig = new RegionRig(trimTerrain: true);
        long m0 = Mem();
        foreach (var n in rig.Home.Neighbours()) if (n.IsValid) { rig.Mgr.GenerateNow(n); rig.Set.Sync(); }
        long m1 = Mem();
        rig.Sched.Update(RegionRig.Start(rig.Set.HomeWorld!) + Duration.FromGameHours(30), rig.Home);
        long m2 = Mem();
        output.WriteLine($"{rig.Set.ResidentCount - 1} neighbours built: +{Mb(m1 - m0):0.0} MB ({Mb(m1 - m0) / Math.Max(1, rig.Set.ResidentCount - 1):0.0} each); after 30 h of ticks +{Mb(m2 - m1):0.0} MB; {rig.Set.ResidentCount} resident");
        Assert.True(Mb(m2 - m0) / Math.Max(1, rig.Set.ResidentCount - 1) < 32, "a neighbour with its records takes about 25 MB");
        Assert.All(rig.Set.Resident, r => Assert.False(r.Population.Region.HeightCm.Length > 0 && r.Coord != rig.Home, "neighbour terrain is trimmed"));
        GC.KeepAlive(rig);
    }

    [Fact]
    public void A_region_section_of_a_ticked_region_is_under_a_megabyte_and_the_people_are_a_small_part_of_it()
    {
        var w = SeamWorld.Get(1234);
        int checkedRegions = 0;
        foreach (var at in new[] { w.BlockOrigin.Offset(1, 1), w.BlockOrigin.Offset(0, 0), w.BlockOrigin.Offset(2, 1) })
        {
            var sv = new RegionServices(1234, w.Content, w.Continent, at) { History = w.History };
            var region = RegionGenerator.Generate(w.Continent, w.History, at, w.Settings);
            LandmarkPlacer.Place(region, w.History, w.Content);
            var pop = Populator.Populate(region, w.History, w.Content);
            if (pop.Households.Count == 0) { output.WriteLine($"{at}: empty"); continue; }
            var rw = RegionWorld.CreateFresh(sv, at, pop);
            rw.AdvanceTo(rw.HourDone + 48);
            var d = rw.Capture();
            int whole = SaveCodec.SerializeCompressed(d).Length;
            d.Compact();
            int Sz<T>(T v) => SaveCodec.SerializeCompressed(v).Length;
            int total = Sz(d);
            output.WriteLine($"{at}: {pop.People.Count} people. {whole / 1e6:0.00} MB with the people whole, {total / 1e6:0.00} MB as a difference: people {Sz(d.People) / 1000} KB, lots {Sz(d.Lots) / 1000}, economy {Sz(d.Economy) / 1000}, abstract {Sz(d.Abstract) / 1000}, farming {Sz(d.Farming) / 1000}, production {Sz(d.Production) / 1000}, markets {Sz(d.Markets) / 1000}");
            Assert.True(total < 1_000_000, $"{at}: {total / 1e6:0.00} MB");
            Assert.True(Sz(d.People) < 120_000, $"{at}: people {Sz(d.People) / 1000} KB");
            // and it restores to the same people
            var back = SaveCodec.DeserializeCompressed<RegionSectionData>(SaveCodec.SerializeCompressed(d));
            var again = RegionWorld.Restore(sv, at, region, back, Populator.Populate(region, w.History, w.Content));
            Assert.Equal(rw.RecordsHash(), again.RecordsHash());
            Assert.NotEqual("", d.BaselineHash); // the baseline guard is stamped (review phase 2 #5)
            checkedRegions++;
        }
        Assert.True(checkedRegions >= 2, $"only {checkedRegions} populated regions were checked: the test skipped the rest");
    }
}
