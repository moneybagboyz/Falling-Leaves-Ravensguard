using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.Tests.Faith;
using Xunit;

namespace Ravensguard.Sim.Tests.Law;

/// <summary>Phase 6 review S4: the hero's lordship is read from the rule book (D2's grants write <c>FiefRow.Holder</c>), so the law sees him as the lord and the NPC lord stops judging.</summary>
public class HeroFiefTests
{
    [Fact]
    public void S4_AHeroGrantedAFief_IsItsLordInTheLaw_AndTheNpcLordNoLongerJudgesIt()
    {
        using var r = new FaithRig();
        var rb = r.State.Rule!; var held = rb.Fiefs.Values.First(f => f.Holder > 0 && !f.Forfeited); var other = rb.Fiefs.Values.First(f => f.Fief != held.Fief && f.Holder > 0 && !f.Forfeited);
        var before = LawWorld.From(r.State, r.Content);
        Assert.False(before.HeroLordOf(held.Fief)); Assert.NotEqual(EntityId.None, before.LordOf(held.Fief));
        held.Holder = (long)r.Hero.Value;                                                         // what D2's GrantFief does
        var w = LawWorld.From(r.State, r.Content);
        Assert.True(w.HeroLordOf(held.Fief));                                                    // old code looked for a land-book deed nobody writes
        Assert.Equal(EntityId.None, w.LordOf(held.Fief));                                        // the NPC lord does not judge or pocket the fines
        Assert.False(w.HeroLordOf(other.Fief)); Assert.NotEqual(EntityId.None, w.LordOf(other.Fief));
        held.Forfeited = true; Assert.False(LawWorld.From(r.State, r.Content).HeroLordOf(held.Fief));   // a forfeited fief is not his
    }
}
