using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Tests.Combat.Melee2;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>P7-SG-B: an engine's landing meets the people of a live fight through the one resolver (sieges-and-engines 4.2: a 100 kg stone at 45 m/s is a blunt blow far beyond any armour).</summary>
[Collection("fight")]
public class EngineFightTests(FightFixture fx)
{
    [Fact]
    public void AStoneLandingStrikesEveryoneWithinTheSplash_ThroughTheResolver_AndNoOneOutsideIt()
    {
        var w = fx.World();
        var a = w.Add(fx.Kit("A", 1, 0, 0)); var b = w.Add(fx.Kit("B", 1, 1000, 0)); var far = w.Add(fx.Kit("C", 1, 6000, 0));
        var f = EngineTests.Field.Make();
        f.W.People = EngineFight.People(w, originX: 100_000, originY: 200_000);
        // the landing is in world millimetres; the fight's box has its origin at (100 m, 200 m)
        var rep = f.Book.Impact(100_000, 200_000, 0, 4_500_000, "ch_note_sg_stone", 0, f.W, 77);
        Assert.Equal(2, rep.Wounded);
        Assert.Contains(w.Events, e => e.Kind == FightEventKind.Hit && e.Target == a.Id);
        Assert.Contains(w.Events, e => e.Kind == FightEventKind.Hit && e.Target == b.Id);
        Assert.DoesNotContain(w.Events, e => e.Target == far.Id);
        Assert.True(a.Book.Dead || !a.Book.Conscious || a.Book.PainX100 > 0);
        Assert.Equal(0, far.Book.PainX100);
        // the same landing in the same fight state gives the same wounds: a second fight built alike ends alike
        var w2 = fx.World();
        w2.Add(fx.Kit("A", 1, 0, 0)); w2.Add(fx.Kit("B", 1, 1000, 0)); w2.Add(fx.Kit("C", 1, 6000, 0));
        EngineFight.People(w2, 100_000, 200_000)(100_000, 200_000, 1500, 4_500_000, 77, false);
        Assert.Equal(w.Events.Select(e => (e.Kind, e.Target, e.Part)), w2.Events.Select(e => (e.Kind, e.Target, e.Part)));
        // a bolt is edged and far lighter: one man is hurt, not necessarily killed
        var w3 = fx.World(); var man = w3.Add(fx.Kit("D", 1, 0, 0));
        int n = EngineFight.People(w3)(0, 0, 1500, 60_000, 5, true);
        Assert.InRange(n, 0, 1);
        Assert.Contains(w3.Events, e => e.Target == man.Id);
    }
}
