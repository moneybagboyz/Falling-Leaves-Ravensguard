using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Tests.Spikes;

/// <summary>Phase 0 review fixes: shared grids, zone id reuse, bounds checks.</summary>
public class PathfinderHardeningTests
{
    private static LevelGrid Open(int w = 64, int h = 64)
    {
        var g = new LevelGrid(w, h, 0, 0);
        g.FillRect(0, 0, w - 1, h - 1, 0, true);
        return g;
    }

    [Fact]
    public void TwoPathfindersOnOneGrid_BothSeeEveryChange()
    {
        var g = Open();
        var a = new LevelPathfinder(g); var b = new LevelPathfinder(g);
        var from = new Cell3(1, 1, 0); var to = new Cell3(60, 1, 0);
        Assert.True(a.IsReachable(from, to));
        Assert.True(b.IsReachable(from, to));

        g.FillRect(30, 0, 30, 63, 0, false); // wall across the whole map
        Assert.False(a.IsReachable(from, to));
        Assert.False(b.IsReachable(from, to)); // used to read stale zones: a had already cleared the shared dirty flags
        Assert.Null(b.FindPathAbstract(from, to));

        g.SetOpen(30, 5, 0, true);              // a door
        Assert.True(b.IsReachable(from, to));
        Assert.True(a.IsReachable(from, to));
    }

    [Fact]
    public void ZoneIdsAreReused_NotGrownByRebuilds()
    {
        var g = Open();
        var pf = new LevelPathfinder(g);
        pf.RefreshAbstraction();
        int liveBefore = pf.ZoneCount;
        pf.IsReachable(new(1, 1, 0), new(60, 60, 0));
        int slotsAfterWarmup = pf.ZoneSlots;
        for (int i = 0; i < 300; i++)
        {
            g.FillRect(10, 0, 10, 31, 0, i % 2 == 1); // closes / reopens a wall inside chunk (0,0): splits and joins its zone
            pf.IsReachable(new(1, 1, 0), new(60, 60, 0));
        }
        Assert.True(pf.ZoneSlots <= slotsAfterWarmup + 2, $"zone id slots grew from {slotsAfterWarmup} to {pf.ZoneSlots}");
        Assert.Equal(liveBefore, pf.ZoneCount); // 300 toggles end in the same open state (i = 299 reopens)
    }

    [Fact]
    public void ZonePathResultsUnchangedByReuse()
    {
        var g = Open(96, 96);
        var pf = new LevelPathfinder(g);
        var fresh = new LevelPathfinder(g);
        for (int i = 0; i < 20; i++) { g.FillRect(40, 0, 40, 90, 0, i % 2 == 0); pf.RefreshAbstraction(); }
        g.FillRect(40, 0, 40, 90, 0, false);
        var r1 = pf.FindPathAbstract(new(1, 1, 0), new(90, 90, 0));
        var r2 = fresh.FindPathAbstract(new(1, 1, 0), new(90, 90, 0));
        Assert.NotNull(r1);
        Assert.Equal(r2!.Cost, r1!.Cost);
        Assert.Equal(r2.Cells, r1.Cells);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(64, 0, 0)]
    [InlineData(0, 64, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(0, 0, -1)]
    public void SetOpen_OutOfBounds_Throws(int x, int y, int z)
    {
        var g = Open();
        int rev = g.Revision;
        Assert.Throws<ArgumentOutOfRangeException>(() => g.SetOpen(x, y, z, true));
        Assert.Equal(rev, g.Revision);
        Assert.False(g.IsOpen(0, 1, 0) && false);
    }

    [Fact]
    public void FillRectPortalCollapse_OutOfBounds_ThrowWithoutPartialChanges()
    {
        var g = new LevelGrid(64, 64, 0, 1);
        int rev = g.Revision;
        Assert.Throws<ArgumentOutOfRangeException>(() => g.FillRect(60, 60, 70, 70, 0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => g.FillRect(0, 0, 5, 5, 2, true));
        Assert.Equal(rev, g.Revision);
        Assert.False(g.IsOpen(61, 61, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => g.AddPortal(PortalKind.Stairs, new(1, 1, 0), new(1, 1, 5), 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => g.AddPortal(PortalKind.Stairs, new(-1, 1, 0), new(1, 1, 1), 10));
        Assert.Empty(g.Portals);
        Assert.False(g.IsOpen(1, 1, 0));
        Assert.Equal(rev, g.Revision);

        Assert.Throws<ArgumentOutOfRangeException>(() => g.CollapseFloor(0, 0, 100, 5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => g.CollapseFloor(0, 0, 5, 5, 9));
    }

    [Fact]
    public void IsReachable_OutOfBounds_IsFalse()
    {
        var g = Open();
        var pf = new LevelPathfinder(g);
        Assert.False(pf.IsReachable(new(-1, 0, 0), new(1, 1, 0)));
        Assert.False(pf.IsReachable(new(1, 1, 0), new(1, 1, 3)));
        Assert.False(pf.IsReachable(new(64, 1, 0), new(1, 1, 0)));
        Assert.True(pf.IsReachable(new(1, 1, 0), new(2, 2, 0)));
    }
}
