using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Tests.Armies;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.LiveWar;

/// <summary>P8-LW-4 (the added item): the live road battle, and how the abstract road battle stands against it (D11).</summary>
public class LiveWarRoadTests(ITestOutputHelper output)
{
    private const int Mine = 11, Enemy = 0;

    /// <summary>The AM-20 scene: realm 11 (the AI) calls a host on the player's realm 0 whose ruler is the hero and who calls 60 levies to the town; run until the two hosts are engaged at the battle hour.</summary>
    private static (ArmyRig K, ArmyHost AiHost, ArmyHost HeroHost, ulong Hero, long Due) Scene(bool live)
    {
        var k = new ArmyRig(); k.To(k.At(16, 7));
        k.War(Mine, Enemy); k.Fund(Mine, 400_000_000); k.Fund(Enemy, 400_000_000);
        var aw = k.Aw; var e = k.Env();
        ulong hero = aw.CommanderOf(Enemy)!.Value; aw.IsHero = id => id == hero; aw.HeroId = () => hero;
        var ai = aw.CallCampaign(e, Mine, Enemy, aw.MusterPointFor(Mine, Enemy), 400, 400)!;
        var mine = aw.CallMuster(e, new MusterCall(Enemy, aw.Book.Campaigns[ai.Id].Objective, Worth: 100_000_000, MaxLevies: 60))!;
        aw.RoadBattles.GameDrivesLive = live;
        long start = k.Now, due = 0;
        for (long h = start + 1; h <= start + 40 * 24; h++)
        {
            k.To(h);
            if (live && aw.Book.Hosts.Values.FirstOrDefault(x => x.Commander == hero) is { } hh) { var (px, py) = aw.PositionOfHost(hh, h * 3600); aw.RoadBattles.NoteHero(px / 1000, py / 1000); }
            if (aw.Book.Campaigns.Values.Any(c => c.BattleHour > 0)) { due = aw.Book.Campaigns.Values.Where(c => c.BattleHour > 0).Min(c => c.BattleHour); break; }
        }
        Assert.True(due > 0, "two hosts engaged");
        return (k, ai, mine, hero, due);
    }

    [Fact]
    public void LW4_RoadBattle_TheHeroInTheHost_OpensALiveBubble_ClosedAtTheBarrier_WithTheAbstractFoldAndPrisoners()
    {
        var (k, ai, mine, hero, due) = Scene(true); using var _k = k; var aw = k.Aw;
        long before = aw.People.Count;
        k.To(due);                                                                           // the battle hour: the bubble opens, the campaign waits
        Assert.Single(aw.RoadBattles.Live); var lr = aw.RoadBattles.Live.Values.First();
        Assert.DoesNotContain(k.Entries("battle_report"), en => en.Causes.Any(c => c.NoteKey == "ch_note_am_field"));
        Assert.True(lr.Bubble.Sides.Count == 2 && lr.Bubble.Sides.Sum(s => lr.Bubble.CountsOf(s).Live) <= RoadBattles.LiveCap, "at most 300 live");
        Assert.True(lr.Bubble.Hero >= 0, "the hero is in the fight");
        k.To(due + 3); Assert.Single(aw.RoadBattles.Live);                                   // still open: the hosts stand
        Assert.True(aw.RoadBattles.RunToEnd(k.Env(), ai, mine, 60_000), "the fight ended");
        Assert.Empty(aw.RoadBattles.Live);
        var fought = k.Entries("battle_fought").Where(en => en.Causes.Any(c => c.NoteKey == "ch_note_am_field")).ToList(); Assert.Single(fought);
        Assert.All(fought, en => Assert.NotEmpty(en.Causes));
        Assert.Contains(aw.Book.WarEvents, w => w.Kind == WarEventKind.FieldBattle && w.Entry == fought[0].Id.Value);
        Assert.All(aw.Book.Campaigns.Values, c => Assert.Equal(0, c.BattleHour));
        var killed = k.Entries("death").Where(en => en.Causes.Any(c => c.EventId == fought[0].Id)).ToList();
        output.WriteLine($"road battle: {killed.Count} dead, {k.State.Soldiers!.Prisoners.Count} prisoners");
        Assert.True(killed.Count > 0);
        Assert.True(aw.People.Count >= before, "nobody is removed from the table");
        foreach (var p in k.State.Soldiers!.Prisoners.Values) { Assert.True(aw.RowOf(p.Person) >= 0); Assert.True(aw.People.IsAlive(aw.RowOf(p.Person))); Assert.NotEqual(0UL, p.Entry); }
    }

    /// <summary>The D11 ratchet: the live road battle is the reference; the abstract road battle on the same hosts differs from it by what is written here (the fraction of the two hosts lost, the winner). Neither is changed to match the other.</summary>
    [Fact]
    public void LW4_D11_NamedRatchet_AbstractRoadBattleAgainstTheLiveOne()
    {
        (int Dead, int Captured, int Winner, int Men) Run(bool live)
        {
            var (k, ai, mine, hero, due) = Scene(live); using var _k = k; var aw = k.Aw;
            int men = aw.FitMen(ai) + aw.FitMen(mine);
            if (live) { k.To(due); Assert.True(aw.RoadBattles.RunToEnd(k.Env(), ai, mine, 60_000)); } else k.To(due + 1);
            var ev = aw.Book.WarEvents.Last(w => w.Kind == WarEventKind.FieldBattle);
            int dead = ev.Lost + ev.OwnLost;
            return (dead, k.State.Soldiers!.Prisoners.Count, ev.Doer, men);
        }
        var l = Run(true); var a = Run(false);
        // D11 named ratchet "road battle abstract vs live": the live fight is the reference and is not changed. On this scene both hosts are 534 men, the same side wins, but the abstract fight (at most 120 a side) takes 10 men off the rolls
        // to the live fight's 33 (30%) and 2 prisoners to its 11. The ratchet holds the abstract losses to 20..45% of the live ones and the winner the same; a better abstract fit may only move it nearer 100%.
        Assert.Equal(l.Winner, a.Winner);
        Assert.InRange(a.Dead * 100 / Math.Max(1, l.Dead), 20, 45);
        Assert.True(a.Captured <= l.Captured);
        output.WriteLine($"live: {l.Dead} dead of {l.Men}, {l.Captured} prisoners, winner realm {l.Winner}; abstract: {a.Dead} dead of {a.Men}, {a.Captured} prisoners, winner realm {a.Winner}");
    }
}
