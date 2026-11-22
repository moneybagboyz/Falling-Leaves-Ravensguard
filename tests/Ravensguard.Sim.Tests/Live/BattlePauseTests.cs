using Ravensguard.Sim.Live;
using Ravensguard.Sim.Tests.Abstract;

namespace Ravensguard.Sim.Tests.Live;

/// <summary>
/// Phase 7 BB-C, BB-16 (battle-bubble-and-formations 4.1): while a battle bubble is open the ambient simulation in the chunks next to its box pauses: the live people there hold where they stand and keep their
/// jobs (nobody is folded back, no hour is lost from the abstract clock); when it closes they move on from the schedule again. Nobody is made or lost either way.
/// </summary>
public class BattlePauseTests
{
    [Fact]
    public void BB16_LivePeopleInPausedChunksHold_AndMoveAgainWhenTheBubbleCloses_NobodyIsFoldedBack()
    {
        var rig = new PeopleRig(); var b = new PeopleBridge(); long now = rig.At(7, 0);                  // the morning rush: people walk
        var ground = rig.Ground(walls: false);
        b.Enqueue(rig.Scan(b, now)); while (b.Queued > 0) b.Promote(now, ground);
        int live = b.Count; Assert.True(live >= 50, $"{live} promoted");
        for (long t = now; t < now + 600; t++) b.Step(t, ground, rig.Centre);
        // a battle opens beside them: every live person is in a paused chunk
        b.PausedAt = (x, y) => true;
        var held = b.People.ToDictionary(p => p.Id, p => (p.XMm, p.YMm));
        for (long t = now + 600; t < now + 1200; t++) b.Step(t, ground, rig.Centre);
        foreach (var p in b.People) Assert.Equal(held[p.Id], (p.XMm, p.YMm));                           // nobody moved
        Assert.Equal(live, b.Count); Assert.Empty(b.TakeFolds());                                      // nobody folded back
        // a person outside the paused chunks goes on: pause only the left half of the square
        long cx = rig.CentreXMm;
        b.PausedAt = (x, y) => x < cx;
        var before = b.People.ToDictionary(p => p.Id, p => (p.XMm, p.YMm));
        for (long t = now + 1200; t < now + 1500; t++) b.Step(t, ground, rig.Centre);
        int movedFree = 0, movedHeld = 0;
        foreach (var p in b.People) if (before[p.Id] != (p.XMm, p.YMm)) { if (before[p.Id].XMm < cx) movedHeld++; else movedFree++; }
        Assert.Equal(0, movedHeld);
        Assert.True(movedFree > 0, "people in the free chunks should walk on");
        // the bubble closes: everyone moves from the schedule again
        b.PausedAt = null;
        var again = b.People.ToDictionary(p => p.Id, p => (p.XMm, p.YMm));
        for (long t = now + 1500; t < now + 2100; t++) b.Step(t, ground, rig.Centre);
        Assert.Contains(b.People, p => again[p.Id] != (p.XMm, p.YMm));
        Assert.Equal(live, b.Count);
    }
}
