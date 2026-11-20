using Ravensguard.Content;

namespace Ravensguard.Sim.Heists;

/// <summary>The kinds of heist site (heists-and-jailbreaks 3). Append only (saved).</summary>
public enum HeistKind : byte { Gaol, Strongroom, Vault, Undercroft, Drain, Camp }

/// <summary>One cell of a heist map: walls and rock block; a door, a cell's bars, a grate or the chest are things to deal with; the culvert is the drain's floor; the exit and the two portals are the ways between places.</summary>
public enum HTile : byte { Rock, Wall, Floor, Culvert, Door, CellDoor, Grate, Chest, Exit, PortalA, PortalB }  // Grate is kept for old saves; a grate is a Door tile whose HDoor is a grate

/// <summary>A door of a map: where, its lock quality (-1 none: a plain door; 0 Crude to 4 Masterwork), whether it is a cell's bars.</summary>
public sealed record HDoor(int Index, int X, int Y, int Quality, bool Cell)
{
    /// <summary>A drain grate: no lock, pried loose with a tool (30 s, noise 8).</summary>
    public bool Grate => Quality == -2;
}

/// <summary>
/// The static map of a heist site, parsed from a template (<see cref="HeistTemplateDef"/>): tiles, doors in row-major order, the sconces, hiding spots, beds, guard posts, prisoners' slots, patrol waypoints in order, the chest, the way in and the
/// ends of the drain. Pure data: the same template always gives the same map (a save keeps only what play changed).
/// </summary>
public sealed class HeistMap
{
    /// <summary>The patrol waypoints in order; D and G are the drain and the post, so the letters skip them.</summary>
    public const string WaypointGlyphs = "ABCEFHJK";

    public string Template { get; init; } = "";
    public HeistKind Kind { get; init; }
    public int Tier { get; init; }
    public int W { get; init; }
    public int H { get; init; }
    public int CellLock { get; init; }
    public int Guards { get; init; }
    public HTile[] Tiles { get; init; } = [];
    public List<HDoor> Doors { get; } = [];
    public List<(int X, int Y)> Sconces { get; } = [];
    public List<(int X, int Y)> Hides { get; } = [];
    public List<(int X, int Y)> Beds { get; } = [];
    public List<(int X, int Y)> Posts { get; } = [];
    public List<(int X, int Y)> Slots { get; } = [];
    public List<(int X, int Y)> Waypoints { get; } = [];
    public (int X, int Y) Chest { get; set; } = (-1, -1);
    public (int X, int Y) Exit { get; set; } = (-1, -1);
    public (int X, int Y) PortalA { get; set; } = (-1, -1);
    public (int X, int Y) PortalB { get; set; } = (-1, -1);

    public int Index(int x, int y) => y * W + x;
    public bool In(int x, int y) => (uint)x < (uint)W && (uint)y < (uint)H;
    public HTile Tile(int x, int y) => In(x, y) ? Tiles[y * W + x] : HTile.Rock;

    /// <summary>The door standing on a cell, or null.</summary>
    public HDoor? DoorAt(int x, int y) { foreach (var d in Doors) if (d.X == x && d.Y == y) return d; return null; }

    /// <summary>Whether a cell is one a person can stand on (a closed door counts: it opens as he walks into it; a grate and the chest do not).</summary>
    public bool Walkable(int x, int y) => Tile(x, y) is HTile.Floor or HTile.Culvert or HTile.Door or HTile.CellDoor or HTile.Exit or HTile.PortalA or HTile.PortalB;

    public static HeistMap Parse(HeistTemplateDef t)
    {
        int h = t.Rows.Count, w = h == 0 ? 0 : t.Rows.Max(r => r.Length);
        var tiles = new HTile[w * h];
        var m = new HeistMap { Template = t.Id, Kind = ParseKind(t.Kind), Tier = t.Tier, W = w, H = h, CellLock = t.CellLock, Guards = t.Guards, Tiles = tiles };
        var wps = new SortedDictionary<int, (int, int)>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                char ch = x < t.Rows[y].Length ? t.Rows[y][x] : ' ';
                HTile tile = ch switch
                {
                    ' ' => HTile.Rock, '#' => HTile.Wall, '~' => HTile.Culvert, '+' or '1' or '2' or '3' or '4' or '5' or 'g' => HTile.Door, 'c' => HTile.CellDoor, 'T' => HTile.Chest,
                    'x' => HTile.Exit, 'u' => HTile.PortalA, 'D' => HTile.PortalB, _ => HTile.Floor,
                };
                tiles[y * w + x] = tile;
                switch (ch)
                {
                    case '+': m.Doors.Add(new HDoor(m.Doors.Count, x, y, -1, false)); break;
                    case >= '1' and <= '5': m.Doors.Add(new HDoor(m.Doors.Count, x, y, ch - '1', false)); break;
                    case 'g': m.Doors.Add(new HDoor(m.Doors.Count, x, y, -2, false)); break;
                    case 'c': m.Doors.Add(new HDoor(m.Doors.Count, x, y, Math.Clamp(t.CellLock, 0, 4), true)); break;
                    case 's': m.Sconces.Add((x, y)); break;
                    case 'h': m.Hides.Add((x, y)); break;
                    case 'b': m.Beds.Add((x, y)); break;
                    case 'G': m.Posts.Add((x, y)); break;
                    case 'P': m.Slots.Add((x, y)); break;
                    case 'T': m.Chest = (x, y); break;
                    case 'x': m.Exit = (x, y); break;
                    case 'u': m.PortalA = (x, y); break;
                    case 'D': m.PortalB = (x, y); break;
                    default: { int k = WaypointGlyphs.IndexOf(ch); if (k >= 0) wps[k] = (x, y); break; }
                }
            }
        foreach (var kv in wps) m.Waypoints.Add(kv.Value);
        return m;
    }

    public static HeistKind ParseKind(string s) => s switch { "gaol" => HeistKind.Gaol, "strongroom" => HeistKind.Strongroom, "vault" => HeistKind.Vault, "undercroft" => HeistKind.Undercroft, "drain" => HeistKind.Drain, "camp" => HeistKind.Camp, _ => HeistKind.Gaol };

    /// <summary>The walkable neighbours (eight ways, but never cutting a corner through a wall) of a cell, in a fixed order: the pathing of the guards and the staircase of the distance fields.</summary>
    public static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1)];

    /// <summary>The distance (in steps, 8-way without corner cutting) from every cell to <paramref name="tx"/>, <paramref name="ty"/> over the cells a guard may walk (doors and cell bars pass; the culvert and grates do not); -1 where unreachable. Ties are settled by the fixed neighbour order, so a guard following the field always takes the same way.</summary>
    public int[] Field(int tx, int ty, bool culvert = false)
    {
        var d = new int[W * H]; Array.Fill(d, -1);
        if (!In(tx, ty)) return d;
        var q = new Queue<int>(); d[Index(tx, ty)] = 0; q.Enqueue(Index(tx, ty));
        while (q.Count > 0)
        {
            int c = q.Dequeue(); int cx = c % W, cy = c / W;
            foreach (var (dx, dy) in Dirs)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!In(nx, ny) || d[Index(nx, ny)] >= 0 || !GuardCan(nx, ny, culvert)) continue;
                if (dx != 0 && dy != 0 && (!GuardCan(cx + dx, cy, culvert) || !GuardCan(cx, cy + dy, culvert))) continue;
                d[Index(nx, ny)] = d[c] + 1; q.Enqueue(Index(nx, ny));
            }
        }
        return d;
    }

    private bool GuardCan(int x, int y, bool culvert) => Tile(x, y) is HTile.Floor or HTile.Door or HTile.CellDoor or HTile.Exit or HTile.PortalA or HTile.PortalB || (culvert && Tile(x, y) == HTile.Culvert);

    /// <summary>The next cell from (x, y) down the field <paramref name="f"/>: the neighbour with the lowest distance (the first in the fixed order on a tie); the cell itself when it is the target or nothing is nearer.</summary>
    public (int X, int Y) Down(int[] f, int x, int y)
    {
        int best = f[Index(x, y)]; (int, int) at = (x, y);
        if (best < 0) return at;
        foreach (var (dx, dy) in Dirs)
        {
            int nx = x + dx, ny = y + dy;
            if (!In(nx, ny)) continue;
            int v = f[Index(nx, ny)];
            if (v < 0 || v >= best) continue;
            if (dx != 0 && dy != 0 && (f[Index(x + dx, y)] < 0 || f[Index(x, y + dy)] < 0)) continue;
            best = v; at = (nx, ny);
        }
        return at;
    }

    /// <summary>A stable hash of the static map (the template's tiles): the generation test's <c>HeistHash</c> reads it.</summary>
    public ulong Hash(ulong h = 14695981039346656037UL)
    {
        static ulong M(ulong h, long v) { unchecked { h ^= (ulong)v; h *= 1099511628211UL; h ^= h >> 29; return h; } }
        h = M(h, W); h = M(h, H); h = M(h, (long)Kind); h = M(h, CellLock);
        foreach (var t in Tiles) h = M(h, (long)t);
        foreach (var d in Doors) { h = M(h, d.X); h = M(h, d.Y); h = M(h, d.Quality); }
        foreach (var c in Template) h = M(h, c);
        return h;
    }
}
