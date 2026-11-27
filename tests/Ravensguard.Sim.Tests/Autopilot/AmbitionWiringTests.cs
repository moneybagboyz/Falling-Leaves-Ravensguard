using Ravensguard.Content;
using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Tests.Autopilot;

/// <summary>P8-AT-2 wiring: the game's MeansHook and FactsHook (<see cref="AutopilotWorld.InstallAmbitionHooks"/>) read the books and issue the desk's commands.</summary>
public class AmbitionWiringTests
{
    private static ConditionRefDef Cond(string id, params (string, string)[] args) => new() { Id = id, Args = args.ToDictionary(a => a.Item1, a => a.Item2) };

    [Fact]
    public void TheGameHooks_AreSet_TheFactsReadTrustRankAndFief_AndTalkIsIssuedToTheDesk()
    {
        using var w = ApWorld.Soak(heroCrowns: 200);
        Assert.Null(w.Ap.MeansHook); Assert.Null(w.Ap.FactsHook);
        w.Ap.InstallAmbitionHooks();
        Assert.NotNull(w.Ap.MeansHook); Assert.NotNull(w.Ap.FactsHook);

        var pt = w.State.Population.People;
        int row = Enumerable.Range(0, pt.Count).First(r => pt.IsAlive(r) && pt.HomeSettlement[r] >= 0 && pt.Occupation[r] != Occupation.None);
        var id = pt.IdOf(row); string kind = pt.Occupation[row].ToString().ToLowerInvariant();
        Assert.False(w.Ap.FactsOf(w.Hero).Trust.GetValueOrDefault(kind) >= 30);
        w.State.Party!.Know(id).TrustPm = 300;
        var f = w.Ap.FactsOf(w.Hero);
        Assert.True(f.Trust[kind] >= 30, "trust of a kind comes from the people the hero knows");
        Assert.Contains("free_person", f.Ranks);
        Assert.Equal(0, f.FiefRank);

        // the means: a talk at the place the person lives
        w.Ap.Tree = new AmbitionTree([new AmbitionDef { Id = "t", Name = "t", Description = "t", Tier = "middle", Steps = [new AmbitionStepDef { Id = "s0", Name = "s0", Conditions = [Cond("knows_person", ("kind", kind), ("min_trust", "90"))] }] }]);
        w.State.Hero!.AmbitionId = "t"; w.State.Hero.Milestone = 0;
        var st = w.Ap.Enable(w.Hero, w.Sec, Directive.Pursue());
        var s = w.State.Population.Region.Settlements[pt.HomeSettlement[row]];
        st.PosX = s.CenterX * 1000L; st.PosY = s.CenterY * 1000L;
        var talk = AutopilotContent.Default.Means.First(m => m.Id == "person_talk");
        Assert.True(w.Ap.MeansHook!(w.Hero, talk));
        Assert.Contains(w.Desk.PendingCommands(), c => c.Kind == HeroCommandKind.Talk && c.Origin == CommandOrigin.Autopilot);
    }
}
