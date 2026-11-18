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
}
