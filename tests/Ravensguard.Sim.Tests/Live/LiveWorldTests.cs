using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Tests.Building;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Tests.Live;

public class LiveWorldTests(BuildingFixture fx) : IClassFixture<BuildingFixture>
{
    private static readonly GameTime Noon = GameTime.FromCalendar(1, Season.Spring, 8, 12);
    private static readonly GameTime Midnight = GameTime.FromCalendar(1, Season.Spring, 8, 0);

    /// <summary>40 x 20 open field with a wall column at x=20 (a gap at y=15) and a second level reached by stairs at (5,5).</summary>
    private static LiveWorld Field(Cell3 start, GameTime? t = null)
    {
        var g = new LevelGrid(40, 20, 0, 1);
        g.FillRect(0, 0, 39, 19, 0, true);
        for (int y = 0; y < 20; y++) if (y != 15) g.SetOpen(20, y, 0, false);
        g.FillRect(0, 0, 9, 9, 1, true);
        g.AddPortal(PortalKind.Stairs, new Cell3(5, 5, 0), new Cell3(5, 5, 1), 30);
        return new LiveWorld(g, new LightGrid(g), start, t ?? Noon);
    }

    /// <summary>Steps until the hero has finished its path (at least one step). Returns the ticks used.</summary>
    private static int FollowPath(LiveWorld w, int max)
    {
        int i = 0;
        do { w.Step(); i++; } while (i < max && w.Snapshot.Hero.PathRemaining > 0);
        return i;
    }

    private static void Run(LiveWorld w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }

    [Fact]
    public void HeroCannotWalkThroughWalls()
    {
        var w = Field(new Cell3(15, 5, 0));
        w.SetInput(new MoveInput(1, 0, Sprint: true));
        Run(w, 200);
        var h = w.Snapshot.Hero;
        Assert.True(h.X < Fixed.FromInt(20) - LiveWorld.Radius + Fixed.FromRatio(1, 100), $"x={h.X}");
        Assert.True(h.X > Fixed.FromInt(19), $"hero should have reached the wall, x={h.X}");
        Assert.Equal(0, h.Z);
        // through the gap it can pass
        w.SetInput(new MoveInput(0, 1));
        for (int i = 0; i < 400 && w.Snapshot.Hero.Y < Fixed.FromRatio(31, 2); i++) w.Step();
        Assert.True(w.Snapshot.Hero.Y > Fixed.FromInt(15) && w.Snapshot.Hero.Y < Fixed.FromInt(16), $"y={w.Snapshot.Hero.Y}");
        w.SetInput(new MoveInput(1, 0));
        Run(w, 60);
        Assert.True(w.Snapshot.Hero.X > Fixed.FromInt(21), $"x={w.Snapshot.Hero.X}");
    }

    [Fact]
    public void HeroSlidesAlongAWall()
    {
        var w = Field(new Cell3(18, 5, 0));
        w.SetInput(new MoveInput(1, 1)); // diagonal into the wall: x blocked, y keeps going
        Run(w, 40);
        var h = w.Snapshot.Hero;
        Assert.True(h.Y > Fixed.FromInt(8), $"y={h.Y}");
        Assert.True(h.X < Fixed.FromInt(20));
    }

    [Theory]
    [InlineData(MoveMode.Walk, 1.5)]
    [InlineData(MoveMode.Jog, 3.0)]
    [InlineData(MoveMode.Sprint, 5.0)]
    public void SpeedsMatchTheScaleModel(MoveMode mode, double metresPerSecond)
    {
        var w = Field(new Cell3(2, 12, 0));
        w.SetInput(new MoveInput(1, 0, Sprint: mode == MoveMode.Sprint, Walk: mode == MoveMode.Walk));
        var x0 = w.Snapshot.Hero.X;
        Run(w, 20); // one real second
        double d = (w.Snapshot.Hero.X - x0).ToDouble();
        Assert.InRange(d, metresPerSecond - 0.02, metresPerSecond + 0.02);
        Assert.Equal(mode, w.Snapshot.Hero.Mode);
    }

    [Fact]
    public void DiagonalMovementIsNotFasterThanStraight()
    {
        var a = Field(new Cell3(2, 2, 0)); a.SetInput(new MoveInput(1, 0)); Run(a, 20);
        var b = Field(new Cell3(2, 2, 0)); b.SetInput(new MoveInput(1, 1)); Run(b, 20);
        double da = (a.Snapshot.Hero.X - Fixed.FromRatio(5, 2)).ToDouble();
        double dx = (b.Snapshot.Hero.X - Fixed.FromRatio(5, 2)).ToDouble(), dy = (b.Snapshot.Hero.Y - Fixed.FromRatio(5, 2)).ToDouble();
        Assert.InRange(Math.Sqrt(dx * dx + dy * dy), da - 0.05, da + 0.05);
    }

    [Fact]
    public void MovementIsDeterministic()
    {
        long Hash(LiveWorld w)
        {
            long h = 17;
            var script = new (int dx, int dy, bool sprint)[] { (1, 0, false), (1, 1, true), (0, 1, false), (-1, 0, false), (0, -1, true), (1, -1, false) };
            for (int i = 0; i < 600; i++)
            {
                var (dx, dy, sp) = script[i / 100 % script.Length];
                w.SetInput(new MoveInput(dx, dy, sp));
                if (i == 250) w.SetSpeed(SimSpeed.X4);
                w.Step();
                var s = w.Snapshot.Hero;
                h = h * 31 + s.X.Raw; h = h * 31 + s.Y.Raw; h = h * 31 + s.Z;
            }
            return h;
        }
        Assert.Equal(Hash(Field(new Cell3(3, 3, 0))), Hash(Field(new Cell3(3, 3, 0))));
    }

    [Fact]
    public void PausedWorldStandsStill()
    {
        var w = Field(new Cell3(2, 2, 0));
        w.SetSpeed(SimSpeed.Paused);
        var t0 = w.Snapshot.Time; var x0 = w.Snapshot.Hero.X;
        w.SetInput(new MoveInput(1, 0));
        Run(w, 40);
        Assert.Equal(x0, w.Snapshot.Hero.X);
        Assert.Equal(t0, w.Snapshot.Time);
        w.SetSpeed(SimSpeed.X1);
        Run(w, 20);
        Assert.True(w.Snapshot.Hero.X > x0);
        Assert.Equal(20, (w.Snapshot.Time - t0).GameSeconds); // 1 real s = 20 game s
    }

    [Fact]
    public void FastSpeedMovesFartherButNeverTunnelsThroughWalls()
    {
        var w = Field(new Cell3(2, 12, 0));
        w.SetSpeed(SimSpeed.X8);
        w.SetInput(new MoveInput(1, 0, Sprint: true));
        var x0 = w.Snapshot.Hero.X;
        Run(w, 6);
        Assert.InRange((w.Snapshot.Hero.X - x0).ToDouble(), 11.9, 12.1); // 5 m/s x 8 x 0.3 s
        Run(w, 100);
        Assert.True(w.Snapshot.Hero.X < Fixed.FromInt(20), $"x={w.Snapshot.Hero.X}");
    }

    [Fact]
    public void StairsMoveTheHeroUpOnWalkingOntoThem()
    {
        var w = Field(new Cell3(4, 5, 0));
        w.SetInput(new MoveInput(1, 0));
        Run(w, 8);
        Assert.Equal(1, w.Snapshot.Hero.Z);
        // standing on the paired stairs does not bounce straight back
        Run(w, 2);
        Assert.Equal(1, w.Snapshot.Hero.Z);
    }

    [Fact]
    public void UsePortalClimbsAndDescends()
    {
        var w = Field(new Cell3(5, 5, 0));
        Assert.True(w.UsePortal());
        w.Step();
        Assert.Equal(1, w.Snapshot.Hero.Z);
        Assert.True(w.UsePortal());
        w.Step();
        Assert.Equal(0, w.Snapshot.Hero.Z);
        var off = Field(new Cell3(8, 8, 0));
        Assert.False(off.UsePortal());
    }

    [Fact]
    public void PathFollowingRoutesAroundTheWallThroughTheGap()
    {
        var w = Field(new Cell3(15, 5, 0));
        Assert.True(w.CommandMoveTo(new Cell3(30, 5, 0)));
        FollowPath(w, 2000);
        var h = w.Snapshot.Hero;
        Assert.Equal(0, h.PathRemaining);
        Assert.Equal(30, h.CellX); Assert.Equal(5, h.CellY);
    }

    [Fact]
    public void NoPathToAnEnclosedCell()
    {
        var w = Field(new Cell3(15, 5, 0));
        Assert.False(w.CommandMoveTo(new Cell3(20, 5, 0))); // a wall cell
        Assert.Equal(0, w.Snapshot.Hero.PathRemaining);
    }

    // ---- the real hamlet ----

    private HamletMap Hamlet() => HamletBuilder.Build(fx.Lib);

    [Fact]
    public void HamletBuildsAndTheHeroStartsOnOpenGround()
    {
        var m = Hamlet();
        Assert.Equal(5, m.Buildings.Count);
        Assert.True(m.Grid.IsOpen(HamletBuilder.HeroStart.X, HamletBuilder.HeroStart.Y, 0));
        Assert.Equal(GroundKind.Cobble, m.GroundAt(HamletBuilder.HeroStart.X, HamletBuilder.HeroStart.Y, 0));
        Assert.Equal(96, m.Grid.Width);
    }

    [Fact]
    public void HeroWalksThroughTheTavernStairsToTheUpperFloor()
    {
        var m = Hamlet();
        var w = new LiveWorld(m, HamletBuilder.HeroStart, Noon);
        var target = new Cell3(55, 43, 1); // the upstairs corridor
        Assert.True(m.Grid.IsOpen(55, 43, 1));
        var r = w.Pathfinder.FindPath(HamletBuilder.HeroStart, target);
        Assert.NotNull(r);
        Assert.Contains(r!.PortalsUsed, id => m.Grid.Portals[id].Kind == PortalKind.Stairs);
        Assert.True(w.CommandMoveTo(target));
        int ticks = 0;
        ticks = FollowPath(w, 6000);
        var h = w.Snapshot.Hero;
        Assert.Equal(1, h.Z);
        Assert.Equal(55, h.CellX); Assert.Equal(43, h.CellY);
        Assert.True(ticks < 3000, $"took {ticks} ticks");
    }

    [Fact]
    public void HeroClimbsTheCottageLadderToTheLoft()
    {
        var m = Hamlet();
        var w = new LiveWorld(m, HamletBuilder.HeroStart, Noon);
        var ladder = m.Grid.Portals.First(p => p.Kind == PortalKind.Ladder);
        var loftFloor = new Cell3(ladder.B.X, ladder.B.Y, 1);
        // any open loft cell next to the trapdoor
        Cell3? dest = null;
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            if (m.Grid.IsOpen(loftFloor.X + dx, loftFloor.Y + dy, 1)) { dest = new Cell3(loftFloor.X + dx, loftFloor.Y + dy, 1); break; }
        Assert.NotNull(dest);
        Assert.True(w.CommandMoveTo(dest!.Value));
        FollowPath(w, 6000);
        Assert.Equal(1, w.Snapshot.Hero.Z);
    }

    [Fact]
    public void EveryExteriorDoorOfTheHamletIsReachableFromTheStart()
    {
        var m = Hamlet();
        var pf = new LevelPathfinder(m.Grid);
        foreach (var b in m.Buildings)
            foreach (var d in b.ExteriorDoors)
                Assert.True(pf.FindPath(HamletBuilder.HeroStart, d) != null, $"door {d} unreachable");
        foreach (var b in m.Buildings)
            foreach (var p in b.Portals)
                Assert.True(pf.FindPath(HamletBuilder.HeroStart, p.B) != null, $"portal top {p.B} unreachable");
    }

    [Fact]
    public void BuildingWallsBlockTheHero()
    {
        var m = Hamlet();
        var w = new LiveWorld(m, new Cell3(46, 38, 0), Noon); // north of the smithy and tavern, in the lane
        w.SetInput(new MoveInput(1, 0));
        Run(w, 100); // east along y=38 into the tavern's north wall (x=50)
        Assert.True(w.Snapshot.Hero.X < Fixed.FromInt(50), $"x={w.Snapshot.Hero.X}");
    }

    [Fact]
    public void TorchBrightensTheHeroAtNight()
    {
        var m = Hamlet();
        var w = new LiveWorld(m, new Cell3(20, 20, 0), Midnight);
        int dark = w.Snapshot.HeroLight;
        w.SetTorch(true); w.Step();
        Assert.True(w.Snapshot.HeroLight > dark + 150, $"{dark} -> {w.Snapshot.HeroLight}");
        Assert.True(w.Snapshot.HeroVision > LightModel.VisionRadius(dark));
        w.SetTorch(false); w.Step();
        Assert.Equal(dark, w.Snapshot.HeroLight);
    }

    [Fact]
    public void LitHearthLightsTheRoomButNotBehindWalls()
    {
        var m = Hamlet();
        var w = new LiveWorld(m, HamletBuilder.HeroStart, Midnight);
        var tavern = m.Buildings.First(b => b.Width == 12 && b.Height == 8);
        int hx = tavern.X + 9, hy = tavern.Y + 1; // the hearth fixture
        Assert.Equal("hearth", m.FixtureAt(hx, hy, 0));
        var f = w.Snapshot.Light;
        Assert.True(f.Point(hx + 1, hy + 1, 0) > 100, "kitchen cell beside the hearth is lit");
        Assert.Equal(0, f.Point(hx - 6, hy + 3, 0)); // taproom side, behind the kitchen wall
    }

    [Fact]
    public void SnapshotsAreImmutablePerTick()
    {
        var w = Field(new Cell3(2, 2, 0));
        var s0 = w.Snapshot;
        w.SetInput(new MoveInput(1, 0));
        w.Step();
        var s1 = w.Snapshot;
        Assert.NotSame(s0, s1);
        Assert.Equal(s0.Hero.X, s1.Hero.PrevX);
        Assert.True(s1.Hero.X > s1.Hero.PrevX);
        Assert.Equal(s0.Tick + 1, s1.Tick);
    }
}
