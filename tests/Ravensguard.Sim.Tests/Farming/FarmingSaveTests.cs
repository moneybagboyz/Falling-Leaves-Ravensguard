using Ravensguard.Sim.Core;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.Farming;

public class FarmingSaveTests
{
    private static byte[] Bytes(WorldState w) => SaveCodec.Encode(WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0"));
    private static WorldState Load(byte[] b) => WorldSave.Restore(SaveCodec.Decode(b), EconFixture.Content, EconFixture.ContentHash);

    [Fact]
    public void FarmingSection_RoundTripsBytesAndState_AndResumesIdentically()
    {
        // a lived-in farming world: 70 days of the real region (spring work, harvest, threshing, a year boundary, seed loans, coin rent)
        var soak = new FarmingSoak(weather: new FakeFarmWeather());
        var sys = soak.Sys; int y = EconFixture.Pop.NowYear;
        for (int d = 1; d <= 60; d++) { var t = Synth.At(y, d); sys.StepDay(t); soak.Econ.ApplyDailySpoilage(1, t.TotalHours, 10); }
        for (int d = 1; d <= 20; d++) { var t = Synth.At(y + 1, d); sys.StepDay(t); soak.Econ.ApplyDailySpoilage(1, t.TotalHours, 10); }
        Assert.NotEmpty(sys.State.Summaries);

        var world = EconFixture.WorldWith(soak.Econ); world.Farming = sys.State;
        var bytes = Bytes(world);
        var loaded = Load(bytes);
        Assert.NotNull(loaded.Farming); Assert.NotNull(loaded.Economy);
        Assert.Equal(bytes, Bytes(loaded));                                    // byte-identical round trip
        Assert.Equal(sys.State.ComputeHash(), loaded.Farming!.ComputeHash());
        Assert.Equal(soak.Econ.ComputeHash(), loaded.Economy!.ComputeHash());
        Assert.Equal(sys.State.Summaries.Count, loaded.Farming.Summaries.Count);
        Assert.Contains(WorldSave.FarmingSection, SaveCodec.Decode(bytes).Sections.Keys);

        // the restored world carries on exactly as the original does
        var resumed = new FarmingSystem(EconFixture.Pop.Region, EconFixture.Pop, loaded.Economy, FarmingSettings.FromContent(EconFixture.Content), new FakeFarmWeather(), new Chronicle(), new IdAllocator(), EconFixture.Seed, loaded.Farming);
        for (int d = 21; d <= 45; d++)
        {
            var t = Synth.At(y + 1, d);
            sys.StepDay(t); soak.Econ.ApplyDailySpoilage(1, t.TotalHours, 10);
            resumed.StepDay(t); loaded.Economy.ApplyDailySpoilage(1, t.TotalHours, 10);
        }
        Assert.Equal(sys.State.ComputeHash(), resumed.State.ComputeHash());
        Assert.Equal(soak.Econ.ComputeHash(), loaded.Economy.ComputeHash());
    }

    [Fact]
    public void WorldWithoutFarming_KeepsItsBytes_AndAMismatchedSectionIsRejected()
    {
        var plain = EconFixture.WorldWith(EconFixture.NewEconomy());
        var file = WorldSave.Capture(plain, EconFixture.ContentHash, "0.1.0");
        Assert.DoesNotContain(WorldSave.FarmingSection, file.Sections.Keys);          // additive section: absent when there is no farming
        Assert.Null(Load(SaveCodec.Encode(file)).Farming);

        var soak = new FarmingSoak(weather: new FakeFarmWeather());
        var world = EconFixture.WorldWith(soak.Econ); world.Farming = soak.Sys.State;
        var section = FarmingSave.Capture(soak.Sys.State);
        section.FieldCount += 1;
        Assert.Throws<SaveException>(() => section.Restore(EconFixture.Pop.Region));
    }
}
