using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Travel;
using Xunit;

namespace Ravensguard.Sim.Tests.Core;

/// <summary>P8-0A (Phase 8 overview 3, 6, 7, 8): the shared plumbing. Enums grow append-only, the 0x800 streams are registered, the new slots, the save registry and the empty trail.</summary>
public class Phase8PlumbingTests
{
    private static void Appended<T>(T lastOld, params string[] added) where T : struct, Enum
    {
        var names = Enum.GetNames<T>();
        int at = Array.IndexOf(names, lastOld.ToString());
        Assert.True(at >= 0);
        Assert.Equal(added, names.Skip(at + 1).Take(added.Length));
        Assert.Equal(at + 1 + added.Length, names.Length);
        for (int i = 0; i < added.Length; i++) Assert.Equal(at + 1 + i, Convert.ToInt32(Enum.Parse<T>(added[i])));
    }

    [Fact]
    public void EnumAppends_FollowOverview8_InOrder()
    {
        Appended(InterruptKind.WatchOver, "BellRung", "AmbitionChoice", "AmbitionStuck", "EndingOffered");
        Appended(CauseKind.Naval, "WarFlight", "WarReturn", "MonsterLair");
        Appended(DirectiveState.Blocked, "SubDirective");
        Appended(SourceReason.Immigrate, "DevOnly");
        Assert.Equal(26, (int)InterruptKind.WatchOver); Assert.Equal(31, InterruptBus.KindCount); Assert.Equal(InterruptBus.KindCount, Enum.GetValues<InterruptKind>().Length);
        Assert.Equal(31, AutopilotState.DefaultPolicies().Length);
        Assert.Equal(Enum.GetValues<SourceReason>().Length, Ledger.SourceReasons);
    }

    [Fact]
    public void Phase8Streams_AreRegistered_AndTheReserveStaysEmpty()
    {
        var all = typeof(RngStreams).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(uint)).Select(f => (uint)f.GetRawConstantValue()!).ToArray();
        uint[] table = [0x800, 0x810, 0x820, 0x828, 0x830, 0x831, 0x850, 0x851, 0x858, 0x859, 0x880, 0x881];
        Assert.Equal(table, all.Where(v => v is >= 0x800u and <= 0x8FFu).Order().ToArray());
        Assert.Equal(0x800u, RngStreams.AmbitionMeansTies); Assert.Equal(0x831u, RngStreams.LairMonsterBehaviour); Assert.Equal(0x881u, RngStreams.WarReturn);
    }

    [Fact]
    public void SystemOrder_HoldsThePhase8Slots_InsideTheirPhases()
    {
        Assert.True(SystemOrder.ArmiesDaily < SystemOrder.WarTollHour && SystemOrder.WarTollHour < SystemOrder.Production);
        Assert.True(SystemOrder.MarketDaily < SystemOrder.AmbitionHourly && SystemOrder.AmbitionHourly < SystemOrder.DailyGlobalsFarming);
        Assert.True(SystemOrder.SiegesDaily < SystemOrder.AmbitionPlanner && SystemOrder.AmbitionPlanner < SystemOrder.S1Farming);
        Assert.True(SystemOrder.S11War < SystemOrder.WarTollSeason && SystemOrder.WarTollSeason < SystemOrder.Outbox);
        var slices = new[] { SystemOrder.SliceGrowth, SystemOrder.SliceContinentTrade, SystemOrder.SliceWeatherPrewarm, SystemOrder.SliceCaravans };
        Assert.Equal(slices.Order(), slices);
        Assert.Equal(4, slices.Distinct().Count());
    }

    [Fact]
    public void SaveRegistry_HasTheEndingSection_AndTrailIsEmptyUntilFilled()
    {
        Assert.Equal(["ending"], Phase8Saves.SectionNames);
        Assert.Equal(10, Phase8Saves.AdditiveKeys.Length);
        Assert.True(Phase8Saves.HasAnySection(["meta", "ending"])); Assert.False(Phase8Saves.HasAnySection(["meta"]));
        Assert.Equal("", Ravensguard.Sim.Diagnostics.Phase8Trail.Hash(1));
        Assert.Empty(Ravensguard.Sim.Diagnostics.Phase8Trail.Parts(1234));
    }
}
