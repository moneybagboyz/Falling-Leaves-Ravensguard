using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.Tests.Autopilot;
using Ravensguard.Sim.WarSupply;

namespace Ravensguard.Sim.Tests.WarSupplyPlay;

/// <summary>P8-WS-2 in play: the running world's tick calls the arms supply (the steward's order on a season's first day, the arms day), and the desk verbs hire and dismiss the steward.</summary>
public class ArmsInPlayWiringTests
{
    private const long Armoury = 0;

    [Fact]
    public void TheTick_PlacesTheStewardsSeasonOrder_AndTheArmsDayClosesItAtItsDeadline()
    {
        using var w = ApWorld.Soak(heroCrowns: 50_000);
        w.State.Content = w.Content;
        ArmsPlayLink.Attach(w.Headless.World, w.State);                                   // the runtime does this on start
        Assert.Null(w.State.WarSupply);
        w.Desk.Issue(SoldierDesk.Steward(Armoury, 0, 10, w.HourDone * 3600));
        w.Desk.ApplyDue(w.Ctx, w.HourDone);
        var arms = w.State.WarSupply!.Arms;
        Assert.Single(arms.Stewards);
        Assert.Empty(arms.Jobs);

        for (int h = 0; h < 24 * 16 && arms.Jobs.Count == 0; h++) w.Ticker.AdvanceHours(1);
        Assert.Single(arms.Jobs);                                                          // ArmsSeasonSystem, on the tick worker, at a season's first day
        int contract = arms.Jobs.Keys.Single(); long deadline = w.State.Contracts!.Get(contract)!.DeadlineDay;
        while (w.HourDone / 24 <= deadline + 1 && arms.Jobs.ContainsKey(contract)) w.Ticker.AdvanceHours(24);
        Assert.False(arms.Jobs.ContainsKey(contract), "ArmsDaySystem closed the unfilled order at its deadline");
    }

    [Fact]
    public void TheDeskVerbs_HireAndDismissTheSteward_AndCommissionAnArmourer()
    {
        using var w = ApWorld.Soak(heroCrowns: 50_000);
        w.State.Content = w.Content;
        w.Desk.Issue(SoldierDesk.Steward(Armoury, 20_000, 5, w.HourDone * 3600));
        w.Desk.Issue(SoldierDesk.Commission(Armoury, 3, 20_000, w.HourDone * 3600));
        w.Desk.ApplyDue(w.Ctx, w.HourDone);
        var arms = w.State.WarSupply!.Arms;
        Assert.Equal(20_000, arms.Stewards[w.Hero.Value].BudgetMites);
        Assert.Contains(arms.Jobs.Values, j => j.Kind == 1);                                // the commission is a refit contract
        w.Desk.Issue(SoldierDesk.DismissSteward(w.HourDone * 3600));
        w.Desk.ApplyDue(w.Ctx, w.HourDone);
        Assert.Empty(arms.Stewards);
    }
}
