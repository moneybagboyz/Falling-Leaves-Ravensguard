using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.RegionsB;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Abstract;

/// <summary>
/// Save, load, continue must equal the uninterrupted run (review finding 2): the whole economy (markets with their pending orders, labour market, parish and fief
/// state, continent float, farming's working tables) is in the sections, so a region captured after N hours and restored runs M more hours to the same records.
/// Both runs step in whole days of at most 24 hours (a gap over <see cref="RegionWorld.FullReplayHours"/> would run its older hours coarse and the two runs would not be the same schedule).
/// </summary>
public class ResumeDeterminismTests(ITestOutputHelper output)
{
    private static void Step(RegionWorld rw, long to)
    {
        while (rw.HourDone < to) rw.AdvanceTo(Math.Min(rw.HourDone + 24, to));
    }

    [Theory]
    [InlineData(0, 12)] [InlineData(0, 40)] [InlineData(24, 12)] [InlineData(24, 40)] [InlineData(700, 12)] [InlineData(700, 40)]
    public void SaveLoadContinue_GivesTheSameRecordsAsTheUninterruptedRun(int n, int m)
    {
        var straight = new RegionRig(); var rwA = straight.Set.TryGet(straight.Home)!;
        long h0 = rwA.HourDone;
        Step(rwA, h0 + n + m);

        var rigB = new RegionRig(); var rwB = rigB.Set.TryGet(rigB.Home)!;
        Step(rwB, h0 + n);
        var bytes = rwB.CaptureBytes();
        var back = RegionWorld.Restore(rigB.Sv, rigB.Home, rwB.Population.Region, SaveCodec.DeserializeCompressed<RegionSectionData>(bytes));
        Step(back, h0 + n + m);

        output.WriteLine($"N {n} M {m}: straight {rwA.RecordsHash()[..12]}, resumed {back.RecordsHash()[..12]}");
        Assert.Equal(rwA.RecordsHash(), back.RecordsHash());
        Assert.Equal(rwA.Economy.Markets.AllBooks().Sum(b => (long)b.PendingOrders), back.Economy.Markets.AllBooks().Sum(b => (long)b.PendingOrders));
        Assert.Equal(rwA.Sim.Economy.ComputeHash(), back.Sim.Economy.ComputeHash());
        Assert.Equal(SaveCodec.SerializeCompressed(MarketsSave.Capture(rwA.Economy.Markets)), SaveCodec.SerializeCompressed(MarketsSave.Capture(back.Economy.Markets)));
        Assert.Equal(SaveCodec.SerializeCompressed(ProductionSave.Capture(rwA.Economy.Production)), SaveCodec.SerializeCompressed(ProductionSave.Capture(back.Economy.Production)));
        Assert.Equal(SaveCodec.SerializeCompressed(FarmingSave.Capture(rwA.Economy.Farming.State)), SaveCodec.SerializeCompressed(FarmingSave.Capture(back.Economy.Farming.State)));
        Assert.Equal(rwA.CaptureBytes(), back.CaptureBytes());
    }

    /// <summary>
    /// Review finding 7: the cases above stop at hour 740, before any year boundary (a game year is 1,440 hours), so the state the year's start reads or resets (the
    /// reeve's re-let, the herds' year, the annual seed provision, the yearly tallies) was never checked. One straight run is saved at hour 1,400 (N = 1,400, M = 100:
    /// across the boundary) and at hour 1,440 (on the boundary itself, M = 40), and each resumed copy must match the straight run byte for byte.
    /// </summary>
    [Fact]
    public void SaveLoadContinue_AcrossTheYearBoundary_GivesTheSameRecords()
    {
        // the straight run (its own rig: the continent the region trades with is world state, saved with the world, not in the region's section)
        var straight = new RegionRig(); var rwA = straight.Set.TryGet(straight.Home)!;
        long h0 = rwA.HourDone;
        Step(rwA, h0 + 1480); var at1480 = rwA.CaptureBytes(); string hash1480 = rwA.RecordsHash();
        Step(rwA, h0 + 1500); var at1500 = rwA.CaptureBytes(); string hash1500 = rwA.RecordsHash();

        void Resume(long n, long to, byte[] want, string wantHash, string label)
        {
            var rig = new RegionRig(); var rw = rig.Set.TryGet(rig.Home)!;
            Step(rw, h0 + n);
            var back = RegionWorld.Restore(rig.Sv, rig.Home, rw.Population.Region, SaveCodec.DeserializeCompressed<RegionSectionData>(rw.CaptureBytes()));
            Step(back, to);
            output.WriteLine($"{label}: straight {wantHash[..12]}, resumed {back.RecordsHash()[..12]}");
            Assert.Equal(wantHash, back.RecordsHash());
            Assert.Equal(want, back.CaptureBytes());
        }
        Resume(1400, h0 + 1500, at1500, hash1500, "N 1400 M 100");
        Resume(1440, h0 + 1480, at1480, hash1480, "N 1440 M 40");
    }
}
