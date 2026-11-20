using Ravensguard.Sim.Core;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Tests.Phase6Overview;

/// <summary>Bugs the Phase 6 overview tests found, each with its own test (Phase 6 close-out G1).</summary>
public class G1FixesTests
{
    /// <summary>
    /// Found by OV-6's resume check: the per-settlement hearing counts (standing 4.1) were kept by increments (the listener's home when he heard) but rebuilt after a load from the listeners' homes of the
    /// day of the load, so a person who had moved home since made the loaded world's standing differ from the one that never stopped. A hearing now keeps the listener's home when it was heard (an
    /// additive key of the talk section) and the rebuild counts from it: a load counts exactly what the increments counted.
    /// </summary>
    [Fact]
    public void TheHearingCountsRebuiltAfterALoad_AreTheOnesTheIncrementsMade_EvenWhenAListenerHasMovedHomeSince()
    {
        int[] homes = [0, 0, 1, 1];
        var ts = new TalkState(); var live = ts.Rumours;
        live.EnsureCounts(p => homes[p]);
        var id = new EntityId(900);
        Assert.True(live.Add(new Rumour { Id = id, Source = id, Kind = RumourKind.Crime, TypeId = "theft", Key = "R_RIOT", Settlement = 0, BornHour = 10, ExpiresDay = 30, LifeDays = 20, InterestPm = 500 }));
        foreach (int listener in new[] { 0, 1, 2 })
            Assert.True(live.AddHearing(id, new Hearing { Listener = listener, Teller = -1, Hour = 10, How = HearHow.Witnessed, PlaceAlt = -1 }, 6));
        Assert.Equal(new SortedDictionary<int, int> { [0] = 2, [1] = 1 }, live.HeardBySettlement(id));

        homes[1] = 1;                                                                    // listener 1 moves to settlement 1 after hearing it
        var saved = TalkSave.Capture(ts);
        var back = SaveCodec.DeserializeCompressed<TalkSave>(SaveCodec.SerializeCompressed(saved)).Restore();
        back.Rumours.EnsureCounts(p => homes[p]);
        Assert.Equal(live.HeardBySettlement(id), back.Rumours.HeardBySettlement(id));    // two at home 0 and one at 1, as counted when they heard
        Assert.Equal(live.Hash(), back.Rumours.Hash());

        // a hearing from an older save has no home recorded: it is counted where the listener lives now (what the rebuild always did)
        var old = TalkSave.Capture(ts);
        foreach (var h in old.Hearings) h.HomeAt = 0;
        var oldBack = old.Restore(); oldBack.Rumours.EnsureCounts(p => homes[p]);
        Assert.Equal(new SortedDictionary<int, int> { [0] = 1, [1] = 2 }, oldBack.Rumours.HeardBySettlement(id));
    }
}
