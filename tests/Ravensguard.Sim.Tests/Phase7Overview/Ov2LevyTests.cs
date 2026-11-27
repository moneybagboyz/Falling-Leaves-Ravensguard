using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Tests.Armies;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Phase7Overview;

/// <summary>
/// OV-2 (overview 4, "lead levies against an army arriving by road and see the consequences"), on the whole runtime of seed 1234 as <c>AM20_MB</c> builds it: the realm of eleven (the AI) declares on the hero's realm and musters 400
/// men; the hero (the commander of the realm of none) calls 60 levies to his town. The AI column marches by road (<c>host_marched</c>), the hero's scouts write an intel row on it, the hosts meet and the battle is offered to the player's
/// bubble (the hook is taken and declines: the abstract resolver fights it, F1 to F4, R and the pause are the bubble's own gate, BB-C). Afterwards: the levy's dead in their households' books (<c>levy_death</c>), the war score from the
/// battle (<c>war_scored</c> on the declaration), desertion and the supply entries of the march; every entry has a cause; coin, mass and people are exact.
/// <b>Not built, not asserted:</b> the armoury filling spears and shields at the point with its gaps shown and a soldier refitted by hand (only the draw's gear from the households is tested, AM-3), and the live fight with the hero leading.
/// </summary>
public class Ov2LevyTests(ITestOutputHelper output)
{
    private const int Mine = 11, Enemy = 0;

    [Fact]
    public void OV2_TheHeroCallsALevy_AnEnemyHostComesByRoad_TheyMeet_TheConsequencesAreWritten_CoinMassAndPeopleExact()
    {
        using var k = new ArmyRig();
        k.To(k.At(16, 7)); var war = k.War(Mine, Enemy); k.Fund(Mine, 400_000_000); k.Fund(Enemy, 400_000_000);
        var aw = k.Aw; var e = k.Env(); var led = k.State.Economy!.Ledger;
        var pt = k.State.Population;
        ulong hero = aw.CommanderOf(Enemy)!.Value; aw.IsHero = id => id == hero;
        var ai = aw.CallCampaign(e, Mine, Enemy, aw.MusterPointFor(Mine, Enemy), 400, 400); Assert.NotNull(ai);
        var plan = aw.Book.Campaigns[ai!.Id]; Assert.Equal(Enemy, aw.Supply!.RealmOf(plan.Objective));
        var mine = aw.CallMuster(e, new MusterCall(Enemy, plan.Objective, Worth: 100_000_000, MaxLevies: 60)); Assert.NotNull(mine);
        long seeded = led.Sourced(SourceReason.Seed).Mites; long alive0 = pt.People.Count;
        int offered = 0; aw.LiveBattle = (_, _) => { offered++; return null; };

        // the call: a root caused by the declaration; the levy is drawn, men of real households
        var called = k.Entries("muster_called").Single(c => c.Id.Value == mine!.Entry);
        Assert.Contains(called.Causes, c => c.Kind == Ravensguard.Sim.Core.CauseKind.War && c.EventId?.Value == war.DeclEntry);
        long start = k.Now; long? battleAt = null;
        for (long h = start + 1; h <= start + 40 * 24; h++)
        {
            k.To(h);
            if (battleAt == null && k.Entries("battle_report").Any(en => en.Causes.Any(c => c.NoteKey == "ch_note_am_field"))) battleAt = h;
            if (battleAt != null && h > battleAt + 5 * 24) break;
        }
        Assert.NotNull(battleAt);
        output.WriteLine($"hours to the battle {battleAt - start}; hosts {mine!.State}/{ai.State}; bubble offered {offered}");
        Assert.True(offered >= 1, "the hero's host was offered the battle bubble");
        Assert.NotNull(aw.IntelOf(Enemy, ai.Id));                                                       // the scouts saw the column
        var marched = k.Entries("host_marched").Single(); Assert.NotEmpty(marched.Causes);
        var report = k.Entries("battle_report").First(en => en.Causes.Any(c => c.NoteKey == "ch_note_am_field")); Assert.NotEmpty(report.Causes);

        // the consequences, to the end of the season
        k.To(k.At(31, 7));
        var scored = k.Entries("war_scored").ToList(); Assert.NotEmpty(scored); Assert.NotEqual(0, war.Score);
        Assert.Contains(scored, s => s.Causes.Any(c => c.EventId?.Value == war.DeclEntry));
        int levyDead = k.Entries("levy_death").Count(), deserts = k.Entries("desertion_wave").Count(), foraged = k.Entries("army_foraged").Count(), bought = k.Entries("army_bought").Count(), purv = k.Entries("purveyance_taken").Count();
        output.WriteLine($"war score {war.Score} (battles {war.Battles}); levy_death {levyDead}, desertion_wave {deserts}, army_foraged {foraged}, army_bought {bought}, purveyance_taken {purv}");
        Assert.True(levyDead + deserts + foraged + bought + purv > 0, "the march and the battle left entries");
        foreach (var t in new[] { "host_marched", "campaign_planned", "battle_report", "war_scored", "levy_death", "desertion_wave", "army_foraged", "army_bought", "purveyance_taken" })
            foreach (var en in k.Entries(t)) Assert.NotEmpty(en.Causes);                                // every entry has a cause

        Assert.Equal(seeded, led.Sourced(SourceReason.Seed).Mites);                                     // no coin made
        Assert.True(led.Audit().IsConserved); Assert.True(k.State.Economy!.MassAuditHolds());
        var (back, _) = P7Game.RoundTrip(k.State, k.R.W, "OV-2 after the battle");                       // people: the save after the war is stable and the dead stay dead
        Assert.Equal(pt.People.Count, back.Population.People.Count);
        Assert.True(k.State.Population.People.Count >= alive0);
    }
}
