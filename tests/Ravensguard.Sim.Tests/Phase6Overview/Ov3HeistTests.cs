using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Heists;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Phase6Overview;

/// <summary>
/// OV-3 (overview 4, "a jailbreak works"): on a whole running game of seed 1234 a companion is jailed for a theft the watch saw (the real law world: the crime, the report, the arrest and the cell of the fief seat's
/// gaol); the hero, trained to the pick, comes through the drain in the small hours (sneaking), picks the Masterwork bars, frees him and leaves unseen: the companion is rescued, the break is found at the next round
/// with nobody named and no bounty and no manhunt. A second run at another seat is seen on the way out (the engine's spotting is HJ-6 and HJ-7; the test names the two guards who saw, as HJ-9 does): a jailbreak
/// crime for the man held and one for the hero, a bounty on each when the witness reports at the next day's first hour, and a one-day manhunt that gives up and leaves the bounties. Every step of the hero's is a recorded command through the desk.
/// </summary>
public class Ov3HeistTests(ITestOutputHelper output)
{
    private sealed class Scene : IDisposable
    {
        public required WorldRuntime Rt; public required WorldState S; public required HeroContext Ctx; public required P6Play P; public required SeamWorld W;
        public int Town;
        public void Dispose() => Rt.Dispose();
        public HeroResult Do(string action) => P.Do(HeistDesk.Do(action, P.Sec));
        public HeistRun Run => S.Heists!.Run!;
    }

    private static readonly string[] ToTheGaol = ["go|5|2", "pry|0", "go|19|4", "pry|1", "go|33|4", "portal", "go|2|4"];
    private static readonly string[] BackOut = ["go|2|2", "free|0", "go|1|10", "portal", "go|0|2"];

    private static Scene Make()
    {
        var (w, s) = P6Game.Game();
        var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        P6Game.AttachGameLinks(rt, w.Content);
        var ctx = new HeroContext(s, s.Party!, w.Content);
        var seats = new HashSet<int>(s.Population.Region.Fiefs.Select(f => f.SeatSettlement));
        int town = HeistSites.Plan(s.Population.Region, s.Population.People).First(x => x.Kind == HeistKind.Drain && seats.Contains(x.Settlement)).Settlement;
        // the hero has learned the pick and the soft step (the test's faucet: a thief's training he has not yet had in this game)
        var sk = (Dictionary<string, int>)s.Hero!.Skills; sk["lockpicking"] = 80; sk["stealth"] = 70;
        return new Scene { Rt = rt, S = s, Ctx = ctx, P = new P6Play(rt, ctx), W = w, Town = town };
    }

    /// <summary>A person of the town (not of the watch, not the hero) who stole in front of a guard and was reported and arrested: held in the gaol of the seat.</summary>
    private static EntityId Jail(Scene sc, int town, int nth, long sec)
    {
        var s = sc.S; var pt = s.Population.People; int day = (int)(sec / 86_400);
        var guards = new HashSet<int>(Guards.Of(pt, town));
        var used = new HashSet<ulong>(s.Justice != null ? s.Justice.Gaol.Keys : []);
        int c = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && !guards.Contains(i) && pt.AgeYears(i, day) >= 18 && i != s.Hero!.PersonIndex && !used.Contains(pt.IdOf(i).Value)).Skip(nth).First();
        int victim = Enumerable.Range(0, pt.Count).First(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && !guards.Contains(i) && i != c && pt.AgeYears(i, day) >= 18);
        int watch = Guards.Of(pt, town)[0];
        var law = LawWorld.From(s, sc.W.Content); var cb = s.Law ??= new CrimeBook(); var jb = s.Justice ??= new JusticeBook();
        var rec = CrimeSystem.Commit(law, cb, new ActSpec("theft", pt.IdOf(c), pt.IdOf(victim), town, sec, 240, 0, "ch_note_crime_greed"), [new WitnessRow(pt.IdOf(watch).Value, WitnessRow.KnewIt)], ActorLook.None);
        CrimeSystem.Report(law, rec, pt.IdOf(watch), sec + 600);
        Gaol.Arrest(law, cb, jb, pt.IdOf(c), town, sec + 1200, inAct: false, pt.IdOf(watch));
        Assert.True(jb.Gaol.ContainsKey(pt.IdOf(c).Value)); Assert.Equal(CrimeState.Held, rec.State);
        return pt.IdOf(c);
    }

    /// <summary>Plays a list of actions, retrying the pick when it slips (a slip is another go), stopping at the first that is refused.</summary>
    private static HeroResult Play(Scene sc, IEnumerable<string> actions)
    {
        HeroResult q = default!;
        foreach (var a in actions)
        {
            q = sc.Do(a);
            for (int again = 0; a.StartsWith("pick|", StringComparison.Ordinal) && q.Ok && sc.S.Heists?.Run is { } r && r.Here.Door[int.Parse(a[5..])] == 0 && again < 5; again++) q = sc.Do(a);
            if (!q.Ok || sc.S.Heists?.Run == null) break;
        }
        return q;
    }

    [Fact]
    public void OV3_AJailbreakWorks_ByTheDrainInTheSmallHours_RescuedUnseen_AndASecondRunSeenGivesTwoBountiesAndAOneDayManhunt_CoinConserved()
    {
        using var sc = Make();
        var s = sc.S; var rt = sc.Rt; var p = sc.P; var e = s.Economy!;
        long seeded = e.Ledger.Audit().Seeded.Mites;
        long h0 = rt.HourDone + (24 - rt.HourDone % 24) % 24 + 24;
        rt.AdvanceTo(GameTime.FromGameHours(h0 - 12));                                                   // noon the day before
        var comp = Jail(sc, sc.Town, 0, rt.HourDone * 3600);
        var cap = s.Captivity!.Find(comp);
        Assert.True(cap is { Held: true });
        var theft = s.Law!.Crimes.Values.Single(c => c.Kind == "theft" && c.Doer == comp.Value);
        rt.AdvanceTo(GameTime.FromGameHours(h0));                                                         // the small hours: the next barrier is one o'clock

        // 1. in by the drain, the first grate, the second, up into the gaol's back passage, to the cell bars; the Masterwork lock; out the same way
        Assert.True(p.Do(HeistDesk.Enter(sc.Town, HeistKind.Drain, 0, p.Sec)).Ok);
        Assert.Equal("drain_long", sc.Run.Here.Template);
        Assert.True(sc.Do("sneak|1").Ok);
        Assert.True(Play(sc, ToTheGaol).Ok); Assert.Equal("gaol_castle", sc.Run.Here.Template);
        Assert.Contains(comp.Value, sc.Run.Here.Slot);
        long t0 = sc.Run.Sec;
        var pick = Play(sc, ["pick|0"]); Assert.True(pick.Ok);
        Assert.True(sc.Run.Sec - t0 >= 240, $"the pick took {sc.Run.Sec - t0} s");
        Assert.Empty(sc.Run.Seen);
        Assert.True(Play(sc, BackOut).Ok); Assert.Equal("drain_long", sc.Run.Here.Template);
        var left = sc.Do("leave");
        Assert.True(left.Ok); output.WriteLine(left.Text.ToString());
        Assert.Contains("unseen", left.Text.ToString());
        Assert.Null(s.Heists?.Run);

        // the companion is rescued, out of the books of the cell; his theft is a flight; the break is a crime nobody saw
        Assert.Equal(CaptiveState.Rescued, s.Captivity.Find(comp)!.State);
        Assert.True(s.Justice?.Gaol.ContainsKey(comp.Value) != true);
        Assert.Equal(CrimeState.Fled, theft.State);
        var jail = s.Law.Crimes.Values.Single(c => c.Kind == "jailbreak");
        Assert.Empty(jail.Witnesses); Assert.False(jail.Known); Assert.Equal(0UL, jail.BountyJob);
        Assert.Empty(s.Heists?.Manhunts ?? []);
        p.Days(1);                                                                                          // the next round finds the empty cell: an entry, no bounty, no hunt
        Assert.Contains(s.Chronicle.All, c => c.TypeId == "crime_discovered");
        Assert.Equal(0UL, jail.BountyJob); Assert.Empty(s.Heists?.Manhunts ?? []);
        Assert.True(e.Ledger.Audit().IsConserved);

        // 2. another seat, another morning: the second break is seen on the way out by two of the watch, one who knew him
        var region = s.Population.Region;
        int town2 = region.Fiefs.Select(f => f.SeatSettlement).Where(t => t != sc.Town && HeistSites.Plan(region, s.Population.People).Contains((t, HeistKind.Drain))).Order().First();
        var comp2 = Jail(sc, town2, 0, rt.HourDone * 3600);
        p.Hours(9);
        Assert.True(p.Do(HeistDesk.Enter(town2, HeistKind.Drain, 0, p.Sec)).Ok);
        Assert.True(Play(sc, ToTheGaol).Ok); Assert.True(Play(sc, ["pick|0"]).Ok); Assert.True(Play(sc, BackOut).Ok);
        var watch = GuardRoster.RealOf(s.Population.People, town2);
        sc.Run.Seen.Add((watch[0], true)); sc.Run.Seen.Add((watch[1], false));                             // two of the watch saw him at the door; the first knew him
        var left2 = sc.Do("leave");
        Assert.True(left2.Ok); output.WriteLine(left2.Text.ToString());
        Assert.DoesNotContain("unseen", left2.Text.ToString());
        var breaks = s.Law.Crimes.Values.Where(c => c.Kind == "jailbreak" && c.Settlement == town2).OrderBy(c => c.Id).ToList();
        Assert.Equal(2, breaks.Count);                                                                      // one for the man held, one for the man who broke him out
        Assert.All(breaks, c => Assert.Equal(2, c.Witnesses.Count));
        var forComp = breaks.Single(c => c.Doer == comp2.Value); var forHero = breaks.Single(c => c.Doer == s.Party!.Hero.Value);
        Assert.True(forComp.Known); Assert.True(forHero.Known);
        var mh = Assert.Single(s.Heists!.Manhunts);
        Assert.False(mh.Ended); Assert.Contains(comp2.Value, mh.Persons); Assert.Contains(s.Party!.Hero.Value, mh.Persons);
        var rules = HeistRules.From(sc.W.Content);
        Assert.Equal(0, mh.RadiusM(rules, mh.StartSec)); Assert.Equal(5_000, mh.RadiusM(rules, mh.StartSec + 5 * 3600)); Assert.Equal(24_000, mh.RadiusM(rules, mh.StartSec + 30 * 3600));
        Assert.True(HeistLink.Seeks(s.Heists, rules, s.Party.Hero.Value, 3_000, mh.StartSec + 5 * 3600)); Assert.False(HeistLink.Seeks(s.Heists, rules, s.Party.Hero.Value, 12_000, mh.StartSec + 5 * 3600));

        // the witnesses report at the next day's first hour: a bounty on each; the hunt gives up when its day is spent (the bounties stay)
        rt.AdvanceTo(GameTime.FromGameHours((rt.HourDone / 24 + 1) * 24 + 1));
        Assert.NotEqual(0UL, forComp.BountyJob); Assert.NotEqual(0UL, forHero.BountyJob);
        Assert.Contains(s.Chronicle.All, c => c.TypeId == "bounty_posted" && c.Subjects.Contains(comp2));
        rt.AdvanceTo(GameTime.FromGameHours(mh.StartSec / 3600 + 26));
        Assert.True(mh.Ended);
        var began = s.Chronicle.All.Single(c => c.TypeId == "manhunt_began"); var ended = s.Chronicle.All.Single(c => c.TypeId == "manhunt_ended");
        Assert.Equal(began.Id, ended.Causes[0].EventId);
        Assert.False(HeistLink.Seeks(s.Heists, rules, s.Party.Hero.Value, 100, mh.StartSec + 30 * 3600));
        Assert.NotEqual(0UL, forHero.BountyJob);
        Assert.True(e.Ledger.Audit().IsConserved); Assert.Equal(seeded, e.Ledger.Audit().Seeded.Mites);

        // the heists' save round trips
        var file = P6Game.SaveAt(rt);
        Assert.Contains(HeistSave.SectionName, file.Sections.Keys);
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), sc.W.Content, P6Game.Hash, null, sc.W.Continent, sc.W.History);
        Assert.Equal(s.Heists.Hash(), back.Heists!.Hash());
    }
}
