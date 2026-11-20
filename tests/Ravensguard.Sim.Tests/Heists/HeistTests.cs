using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Heists;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.Tests.Combat.Resolver2;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Heists;

/// <summary>
/// Heists and jailbreaks (docs/specs/phase6/heists-and-jailbreaks.md, P6-D3): HJ-1 generation, HJ-2 shifts, HJ-3 hearing, HJ-4 investigate then alert, HJ-5 keys, HJ-6 the bribe, HJ-7 disguise, HJ-9 the jailbreak, HJ-12 the save.
/// The scenes are hand-built on the real templates (content/base); the world-level checks (HJ-1 on seed 1234, HJ-9 through the desk, HJ-10, HJ-11) are in <see cref="HeistWorldTests"/>.
/// </summary>
public class HeistTests(ITestOutputHelper output)
{
    internal static readonly Lazy<ContentDatabase> Db = new(V2Fixture.Load);
    internal static HeistRules Rules => HeistRules.From(Db.Value);
    internal const ulong Seed = 1234;

    /// <summary>A day number's hour as seconds: day 5 at the given hour.</summary>
    internal static long At(int hour, int day = 5, int minute = 0) => day * 86_400L + hour * 3600L + minute * 60;

    internal static HeistRun Gaol(HeistBook b, int tier, HeistKind kind, long sec, int weather = 0, IReadOnlyList<ulong>? real = null, IReadOnlyList<ulong>? captives = null, Func<ulong, int>? honesty = null) =>
        HeistSites.Start(b, Rules, Seed, 3, kind, tier, sec, weather, real ?? [], captives ?? [], honesty)!;

    // ------------------------------------------------------------------ HJ-1

    [Fact]
    public void HJ1_EveryTemplateIsWellFormed_EveryFloorReachable_EveryKindHasOne()
    {
        var r = Rules;
        Assert.Equal(13, r.Templates.Count);
        foreach (HeistKind k in Enum.GetValues<HeistKind>()) Assert.Contains(r.Templates, t => HeistMap.ParseKind(t.Kind) == k);
        foreach (var t in r.Templates)
        {
            Assert.All(t.Rows, row => Assert.Equal(t.Rows[0].Length, row.Length));
            var m = r.Map(t.Id)!;
            Assert.True(m.Exit.X >= 0, t.Id + ": no way in");
            var f = m.Field(m.Exit.X, m.Exit.Y, culvert: true);
            for (int y = 0; y < m.H; y++)
                for (int x = 0; x < m.W; x++)
                {
                    var tile = m.Tile(x, y);
                    if (tile is HTile.Wall or HTile.Rock or HTile.Chest) continue;
                    // a floor cell is reachable from the way in: through its doors, or (a gaol's back passage, the drain's far end) through the portal's own site
                    if (f[m.Index(x, y)] < 0 && !(t.Kind == "gaol" && m.PortalB.X >= 0 && (x, y) == m.PortalB) && !(m.PortalB.X >= 0 && f[m.Index(m.PortalB.X, m.PortalB.Y)] < 0))
                        Assert.Fail($"{t.Id}: ({x},{y}) {tile} is not reachable");
                }
            if (m.Kind == HeistKind.Gaol) { Assert.NotEmpty(m.Slots); Assert.Contains(m.Doors, d => d.Cell); }
            if (m.Kind is HeistKind.Strongroom or HeistKind.Vault) Assert.True(m.Chest.X >= 0);
            if (m.Kind is HeistKind.Gaol or HeistKind.Strongroom or HeistKind.Vault) Assert.NotEmpty(m.Beds);
            if (m.Kind != HeistKind.Drain && m.Kind != HeistKind.Camp && m.Guards > 1) Assert.True(m.Waypoints.Count >= 2 || m.Posts.Count >= 1, t.Id + ": nowhere for a guard to be");
            if (m.Kind == HeistKind.Drain) Assert.True(m.PortalA.X >= 0);
        }
        // a template's hash and a made site are the same on every run
        var b1 = new HeistBook(); var b2 = new HeistBook();
        foreach (var k in new[] { HeistKind.Gaol, HeistKind.Strongroom, HeistKind.Vault, HeistKind.Undercroft, HeistKind.Drain })
        {
            var a = HeistSites.Open(b1, r, Seed, 7, k, 1)!; var c = HeistSites.Open(b2, r, Seed, 7, k, 1)!;
            Assert.Equal(a.Map!.Hash(), c.Map!.Hash()); Assert.Equal(a.Template, c.Template); Assert.Equal(a.Extras, c.Extras);
        }
        Assert.Equal(b1.Hash(), b2.Hash());
    }

    // ------------------------------------------------------------------ HJ-2

    [Fact]
    public void HJ2_TheNightShiftIsHalfTheMen_AQuarterSleepAfterTwo_AndALoopTakesSixToTwelveMinutes()
    {
        var r = Rules;
        var men = GuardRoster.Build(99, [], 8);
        Assert.Equal(4, GuardRoster.OnWatch(men, 10).Count);                    // the day men
        Assert.Equal(4, GuardRoster.OnWatch(men, 22).Count);                    // the night men: half of the 8
        Assert.Empty(GuardRoster.SleepingNightMen(men, 22)); Assert.Equal(4, GuardRoster.OnWatch(men, 1).Count);
        Assert.Single(GuardRoster.SleepingNightMen(men, 3));                    // 25% of the night men after 02:00
        Assert.Equal(3, GuardRoster.OnWatch(men, 3).Count);
        Assert.Equal(4, GuardRoster.OnWatch(men, 7).Count);
        // the real watch's rule (Guards.OnDuty) and the roster agree
        // each night's loop is 6 to 12 game minutes, in every template that has one
        foreach (var t in r.Templates)
        {
            var m = r.Map(t.Id)!; if (m.Waypoints.Count < 2) continue;
            for (int night = 0; night < 40; night++)
            {
                int period = GuardRoster.PeriodS(r, Seed, 77, night);
                Assert.InRange(period, 360, 720);
                int loop = GuardRoster.LoopSeconds(m, period);
                Assert.InRange(loop, 360 - m.Waypoints.Count, 720);
            }
        }
        // the period differs between nights (the jitter is drawn)
        Assert.True(Enumerable.Range(0, 20).Select(n => GuardRoster.PeriodS(r, Seed, 77, n)).Distinct().Count() > 10);
        // laid out for a night run: the sleeping night man is in a bed, the keeper asleep with the keys, the rest on their feet
        var b = new HeistBook();
        var run = Gaol(b, 2, HeistKind.Gaol, At(3));
        var s = run.Here;
        Assert.Contains(s.Guards, g => g.Keeper && g.State == GState.Asleep && g.Keys.Count > 0);
        Assert.Contains(s.Guards, g => !g.Keeper && g.State == GState.Asleep);
        Assert.Contains(s.Guards, g => g.State == GState.Post);
        Assert.Contains(s.Guards, g => g.State == GState.Dwell);
        var day = Gaol(new HeistBook(), 2, HeistKind.Gaol, At(10));
        Assert.DoesNotContain(day.Here.Guards, g => g.State == GState.Asleep);
        Assert.Equal(4, day.Here.Guards.Count);
    }

    // ------------------------------------------------------------------ HJ-3

    [Fact]
    public void HJ3_Hearing_ALockPickedThroughAWallIsHeardAtTwoMetres_InHeavyRainAtOnePointFour_ASleeperAtThirtyPercent()
    {
        var s = Rules.S;
        Assert.Equal(200, Noise.RangeCm(s, s.NoisePickM, 0, 1, false));                       // 4 m x 0.5
        Assert.Equal(140, Noise.RangeCm(s, s.NoisePickM, 2, 1, false));                       // heavy rain x 0.7 x 0.5
        Assert.Equal(120, Noise.RangeCm(s, s.NoisePickM, 0, 0, true));                        // a sleeper: x 0.3
        Assert.Equal(400, Noise.RangeCm(s, s.NoisePickM, 0, 0, false));
        Assert.Equal(340, Noise.RangeCm(s, s.NoisePickM, 1, 0, false));                       // light rain x 0.85
        Assert.Equal(200, Noise.RangeCm(s, s.NoisePickM, 3, 0, false));                       // a storm x 0.5
        Assert.Equal(250, Noise.RangeCm(s, 40, 3, 4, false));                                 // a shout (40 m) in a storm through four walls: 40 x 0.5 x 0.0625
        Assert.True(Noise.Heard(s, s.NoisePickM, 0, 1, false, 200)); Assert.False(Noise.Heard(s, s.NoisePickM, 0, 1, false, 201));
        Assert.True(Noise.Heard(s, s.NoisePickM, 2, 1, false, 140)); Assert.False(Noise.Heard(s, s.NoisePickM, 2, 1, false, 141));
        // the sources of the spec's table
        Assert.Equal([6, 15, 2, 8, 4, 25, 30, 40, 10], new[] { NoiseKind.Walk, NoiseKind.Run, NoiseKind.Sneak, NoiseKind.Door, NoiseKind.Pick, NoiseKind.Break, NoiseKind.Fight, NoiseKind.Shout, NoiseKind.Stone }.Select(k => Noise.MetresOf(s, k)));
        Assert.Equal(0, Noise.Loudness(2)); Assert.Equal(1, Noise.Loudness(6)); Assert.Equal(2, Noise.Loudness(15));
        // on a live map: a pick through the wall of the gaol's guardroom is heard by the sleeper only at close range, and by an awake guard through the wall at half range
        var b = new HeistBook();
        var run = Gaol(b, 1, HeistKind.Gaol, At(3));
        var gs = run.Here.Guards;
        output.WriteLine(string.Join(" ", gs.Select(g => $"{g.State}@{g.X},{g.Y}")));
        Assert.Equal(1, run.WallsBetween(run.Here, 2, 1, 6, 1));                                 // the wall between two cells
        Assert.Equal(0, run.WallsBetween(run.Here, 2, 1, 2, 4));                                 // the bars of a cell's door let sound and sight through
    }
}
