using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Live;

/// <summary>
/// The walkable ground the live people see: the loaded window of the street view (or a test grid). Cells are metres of the continent frame (the bridge's own
/// coordinates), so it does not matter which region a person's record belongs to. Implemented over a <see cref="LevelGrid"/> and its pathfinder.
/// </summary>
public interface ILiveGround
{
    /// <summary>True when the cell is inside the loaded grid (outside it nothing is known about the ground, so people there walk straight).</summary>
    bool Covers(int cx, int cy);
    bool IsOpen(int cx, int cy);
    /// <summary>A walkable route over the ground level, as (x, y) cell pairs from <paramref name="ax"/>,<paramref name="ay"/> to <paramref name="bx"/>,<paramref name="by"/>; null when there is none.</summary>
    (int X, int Y)[]? Route(int ax, int ay, int bx, int by);
    /// <summary>True when the cell is open on level <paramref name="z"/>.</summary>
    bool IsOpen(int cx, int cy, int z);
    /// <summary>A walkable route between two cells that may be on different levels (through stairs, ladders and other portals): (x, y, level) from start to goal; null when there is none.</summary>
    (int X, int Y, int Z)[]? Route3(int ax, int ay, int az, int bx, int by, int bz);
}

/// <summary>Ground over one level of a <see cref="LevelGrid"/> whose local (0,0) is continent metre (<see cref="OffsetX"/>, <see cref="OffsetY"/>).</summary>
public sealed class GridGround(LevelGrid grid, LevelPathfinder finder, int offsetX, int offsetY, int z = 0) : ILiveGround
{
    /// <summary>Cells one route may expand before it is given up (a 200 m route over open ground expands a few hundred).</summary>
    public const int MaxExpanded = 1500;
    public int OffsetX { get; } = offsetX;
    public int OffsetY { get; } = offsetY;
    public bool Covers(int cx, int cy) => grid.InBounds(cx - OffsetX, cy - OffsetY, z);
    public bool IsOpen(int cx, int cy) => grid.IsOpen(cx - OffsetX, cy - OffsetY, z);

    public bool IsOpen(int cx, int cy, int z) => grid.IsOpen(cx - OffsetX, cy - OffsetY, z);

    public (int X, int Y, int Z)[]? Route3(int ax, int ay, int az, int bx, int by, int bz)
    {
        var a = new Cell3(ax - OffsetX, ay - OffsetY, az); var b = new Cell3(bx - OffsetX, by - OffsetY, bz);
        if (!grid.InBounds(a.X, a.Y, az) || !grid.InBounds(b.X, b.Y, bz)) return null;
        var r = finder.FindPathBounded(a, b, MaxExpanded * 2);
        if (r == null || r.Cells.Count == 0) return null;
        var cells = new (int, int, int)[r.Cells.Count];
        for (int i = 0; i < cells.Length; i++) cells[i] = (r.Cells[i].X + OffsetX, r.Cells[i].Y + OffsetY, r.Cells[i].Z);
        return cells;
    }

    public (int X, int Y)[]? Route(int ax, int ay, int bx, int by)
    {
        var a = new Cell3(ax - OffsetX, ay - OffsetY, z); var b = new Cell3(bx - OffsetX, by - OffsetY, z);
        if (!grid.InBounds(a.X, a.Y, z) || !grid.InBounds(b.X, b.Y, z)) return null;
        // bounded: a goal that cannot be reached (a walled-in spot) must not flood the window; such a person walks straight until the ground around them is loaded
        var r = finder.FindPathBounded(a, b, MaxExpanded);
        if (r == null || r.Cells.Count == 0) return null;
        foreach (var c in r.Cells) if (c.Z != z) return null;          // a route through stairs belongs to the cross-floor work of live-behaviour step 3
        var cells = new (int, int)[r.Cells.Count];
        for (int i = 0; i < cells.Length; i++) cells[i] = (r.Cells[i].X + OffsetX, r.Cells[i].Y + OffsetY);
        return cells;
    }
}

/// <summary>One level of a ground seen as a plain 2D ground (for string pulling along a stretch of a route that stays on a level).</summary>
internal sealed class LevelView(ILiveGround g, int z) : ILiveGround
{
    public bool Covers(int cx, int cy) => g.Covers(cx, cy);
    public bool IsOpen(int cx, int cy) => g.IsOpen(cx, cy, z);
    public bool IsOpen(int cx, int cy, int zz) => g.IsOpen(cx, cy, zz);
    public (int X, int Y)[]? Route(int ax, int ay, int bx, int by) => null;
    public (int X, int Y, int Z)[]? Route3(int ax, int ay, int az, int bx, int by, int bz) => null;
}

/// <summary>Straightens a cell route into the fewest straight legs that stay on open ground (string pulling), so a person walks the way a person walks and not along the octile staircase.</summary>
public static class PathSmoother
{
    public static (int X, int Y)[] Smooth(ILiveGround g, (int X, int Y)[] cells)
    {
        if (cells.Length <= 2) return cells;
        var res = new List<(int, int)> { cells[0] };
        int anchor = 0;
        while (anchor < cells.Length - 1)
        {
            int far = anchor + 1;
            while (far + 1 < cells.Length && Clear(g, cells[anchor], cells[far + 1])) far++;
            res.Add(cells[far]); anchor = far;
        }
        return [.. res];
    }

    /// <summary>True when the straight line between the cell centres crosses only open cells and never cuts a corner.</summary>
    public static bool Clear(ILiveGround g, (int X, int Y) a, (int X, int Y) b)
    {
        int dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y), sx = a.X < b.X ? 1 : -1, sy = a.Y < b.Y ? 1 : -1;
        int x = a.X, y = a.Y, err = dx - dy;
        while (x != b.X || y != b.Y)
        {
            int e2 = 2 * err; bool mx = e2 > -dy, my = e2 < dx;
            if (mx && my) { if (!g.IsOpen(x + sx, y) || !g.IsOpen(x, y + sy)) return false; }
            if (mx) { err -= dy; x += sx; }
            if (my) { err += dx; y += sy; }
            if (!g.IsOpen(x, y)) return false;
        }
        return true;
    }

    /// <summary>String-pulls a route that changes level: each stretch on one level is straightened on its own; the cells where the level changes stay (a person climbs at the stair, not beside it).</summary>
    public static (int X, int Y, int Z)[] Smooth3(ILiveGround g, (int X, int Y, int Z)[] cells)
    {
        var res = new List<(int, int, int)>(); int i = 0;
        while (i < cells.Length)
        {
            int j = i; while (j + 1 < cells.Length && cells[j + 1].Z == cells[i].Z) j++;
            var run = new (int X, int Y)[j - i + 1];
            for (int k = i; k <= j; k++) run[k - i] = (cells[k].X, cells[k].Y);
            var sm = Smooth(new LevelView(g, cells[i].Z), run);
            foreach (var c in sm) res.Add((c.X, c.Y, cells[i].Z));
            i = j + 1;
        }
        return [.. res];
    }

    /// <summary>The open cell on level <paramref name="z"/> nearest to (<paramref name="x"/>, <paramref name="y"/>) within <paramref name="radius"/>; the cell itself when none is open.</summary>
    public static (int X, int Y) Snap3(ILiveGround g, int x, int y, int z, int radius = 6)
    {
        if (!g.Covers(x, y) || g.IsOpen(x, y, z)) return (x, y);
        for (int r = 1; r <= radius; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (g.Covers(x + dx, y + dy) && g.IsOpen(x + dx, y + dy, z)) return (x + dx, y + dy);
                }
        return (x, y);
    }

    /// <summary>The open cell nearest to (<paramref name="x"/>, <paramref name="y"/>) within <paramref name="radius"/> (ring by ring, row-major inside a ring, so the choice is deterministic); the cell itself when it is outside the ground or nothing is open near it.</summary>
    public static (int X, int Y) Snap(ILiveGround g, int x, int y, int radius = 6)
    {
        if (!g.Covers(x, y) || g.IsOpen(x, y)) return (x, y);
        for (int r = 1; r <= radius; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (g.Covers(x + dx, y + dy) && g.IsOpen(x + dx, y + dy)) return (x + dx, y + dy);
                }
        return (x, y);
    }
}
