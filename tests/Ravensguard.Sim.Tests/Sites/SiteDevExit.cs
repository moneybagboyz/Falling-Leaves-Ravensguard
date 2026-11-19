using Ravensguard.Sim.Sites;
using Ravensguard.Sim.World;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Sites;

[Collection("sites")]
public class SiteDevExit(SiteFixture fx, ITestOutputHelper o)
{
    [Fact(Skip = "dev")]
    public void Exit()
    {
        var m = SiteGen.Generate(fx.S, SiteFixture.Abbey(4401), 59);
        ExFixtures.OneGuard(fx, m);
        var c = TestMaps.Crawl(fx, m, sec: 9 * 3600, seed: 59);
        var h = c.Add(ExFixtures.Hero()); c.ToggleLight(h);
        int goal = ExFixtures.CryptCell(m);
        c.GoTo(h, goal);
        for (int s = 0; s < 20 * 400; s++)
        {
            c.Step();
            foreach (var e in c.TakeEvents()) o.WriteLine($"{s / 20} {e.Kind} {e.A} {e.B} {e.Text} hero lvl {h.Level} cell {h.Cell} tile {m.Tiles[h.Cell]}");
            if (c.Fight == null && h.Path == null && h.CanAct && h.Cell != goal) c.GoTo(h, goal);
            if (h.Cell == goal && c.Fight == null) break;
        }
        var (ml, mx, my) = m.At(m.Mouth);
        o.WriteLine($"hero {h.X},{h.Y} lvl {h.Level} open {c.Grid.IsOpen(h.X / 1000, h.Y / 1000, SiteMap.Z(h.Level))} mouth open {c.Grid.IsOpen(mx, my, SiteMap.Z(ml))} exit {c.ExitTime(h)}");
        var p = c.Pathfinder.FindPath(new Cell3(h.X / 1000, h.Y / 1000, SiteMap.Z(h.Level)), new Cell3(mx, my, SiteMap.Z(ml)));
        o.WriteLine($"path {p?.Cells.Count}");
        for (int l = 0; l < m.Levels; l++) { var (al, ax, ay) = m.At(m.Arrival[l]); var q = c.Pathfinder.FindPath(new Cell3(h.X / 1000, h.Y / 1000, SiteMap.Z(h.Level)), new Cell3(ax, ay, SiteMap.Z(al))); o.WriteLine($"to arrival {l}: {q?.Cells.Count} open {c.Grid.IsOpen(ax, ay, SiteMap.Z(al))}"); }
        foreach (var pt in m.Portals) { var (ul, ux, uy) = m.At(pt.UpperCell); var (ll, lx, ly) = m.At(pt.LowerCell); o.WriteLine($"portal {pt.Id} {ul}:{ux},{uy} -> {ll}:{lx},{ly} en {pt.Enabled} tiles {m.Tiles[pt.UpperCell]} {m.Tiles[pt.LowerCell]}"); }
        foreach (var gp in c.Grid.Portals) o.WriteLine($"grid portal {gp.A} {gp.B} {gp.Enabled}");
        for (int l = 0; l < m.Levels; l++) o.WriteLine(SiteFixture.Draw(m, l));
    }
}
