using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Chunks;

namespace Ravensguard.Sim.Siege;

// P7-SG-A: breaches (the holes torn through a wall that stay until repaired), the opening they make for feet, and repair (sieges-and-engines 4.1).
public sealed partial class WallWorld
{
    /// <summary>A ground-level cell that is part of a wall: a wall, a gate or a window, or the rubble one leaves.</summary>
    private bool WallLike(int x, int y)
    {
        var id = StructureAt(x, y, 0);
        if (id == null) return false;
        if (Walls.IsRubble(id)) return true;
        return Costs.Get(id) is { Kind: PieceKind.Wall or PieceKind.Door or PieceKind.Window };
    }

    private bool IsRubbleCell(int x, int y) => Walls.IsRubble(StructureAt(x, y, 0));

    /// <summary>Whether a cell is rubble the feet cross at the Phase 7 footing <c>Rubble</c> (walk x0.5, no horses at speed).</summary>
    public bool IsRubble(int x, int y, int z) => z == 0 && IsRubbleCell(x, y);

    /// <summary>Whether a cell of the chunk store is open to walk: the rule of the street's grid (rubble is ground; a wall, a shut gate and a stair-less upper cell are not).</summary>
    public bool IsOpen(int x, int y, int z)
    {
        int code = CodeAt(x, y, z);
        var kind = Costs.Get(Palette.Id(TileCode.Structure(code)) ?? "")?.Kind;
        var g = TileCode.Ground(code);
        var fk = Costs.Get(Palette.Id(TileCode.Fixture(code)) ?? "")?.Kind;
        bool solidFixture = fk is { } f && PieceCatalog.IsSolidFixture(f);
        return (kind == null ? z == 0 && TileCode.Walkable(g) : (kind == PieceKind.Ground ? TileCode.Walkable(g) : PieceCatalog.IsWalkable(kind.Value))) && !solidFixture;
    }

    public List<Breach> BreachesNear(int x, int y, int radius = 12) => Breaches(x - radius, y - radius, x + radius, y + radius);

    /// <summary>
    /// The breaches inside a box (inclusive): a connected stretch of ground-level rubble through whose rows (along either axis) every cell of the wall's whole thickness is rubble, with at least
    /// <c>breach_min_width_m</c> such rows side by side. A wall run longer than <c>max_thickness_m</c> is no wall's thickness. Sorted by their first cell; a function of the chunk store alone.
    /// </summary>
    public List<Breach> Breaches(int x0, int y0, int x1, int y1)
    {
        var rubble = new SortedSet<(int Y, int X)>();
        for (int cy = Div(y0); cy <= Div(y1); cy++)
            for (int cx = Div(x0); cx <= Div(x1); cx++)
            {
                if (Content(new ChunkKey(cx, cy, 0)) is not { } c) continue;
                foreach (var (idx, code) in c.Tiles)
                {
                    if (!Walls.IsRubble(Palette.Id(TileCode.Structure(code)))) continue;
                    int x = cx * ChunkContent.Size + idx % ChunkContent.Size, y = cy * ChunkContent.Size + idx / ChunkContent.Size;
                    if (x >= x0 && x <= x1 && y >= y0 && y <= y1) rubble.Add((y, x));
                }
            }
        var found = new List<Breach>();
        var seen = new HashSet<(int, int)>();
        foreach (var (sy, sx) in rubble)
        {
            if (!seen.Add((sx, sy))) continue;
            var comp = new List<(int X, int Y)>(); var queue = new Queue<(int X, int Y)>(); queue.Enqueue((sx, sy));
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue(); comp.Add((x, y));
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
                    if (rubble.Contains((y + dy, x + dx)) && seen.Add((x + dx, y + dy))) queue.Enqueue((x + dx, y + dy));
            }
            var spans = Spans(comp, 'x');
            if (spans.Count == 0) spans = Spans(comp, 'y');
            found.AddRange(spans);
        }
        found.Sort((a, b) => a.Cells[0].Y != b.Cells[0].Y ? a.Cells[0].Y.CompareTo(b.Cells[0].Y) : a.Cells[0].X.CompareTo(b.Cells[0].X));
        return found;
    }

    /// <summary>The breaches of a rubble component when the wall's thickness is walked along <paramref name="axis"/>: the rows (one per coordinate of the other axis) that are rubble through the whole thickness, grouped side by side.</summary>
    private List<Breach> Spans(List<(int X, int Y)> comp, char axis)
    {
        int max = Walls.Rules.MaxThicknessM, minWidth = Walls.Rules.BreachMinWidthM;
        var rows = new SortedDictionary<int, List<(int X, int Y)>>();      // row coordinate -> the cells of its run
        var tried = new HashSet<int>();
        foreach (var (x, y) in comp)
        {
            int row = axis == 'x' ? y : x;
            if (!tried.Add(row)) continue;
            int a = axis == 'x' ? x : y;
            int lo = a, hi = a;
            while (hi - lo < max + 1 && WallLikeAt(lo - 1, row, axis)) lo--;
            while (hi - lo < max + 1 && WallLikeAt(hi + 1, row, axis)) hi++;
            bool through = hi - lo + 1 <= max;
            var cells = new List<(int X, int Y)>();
            for (int i = lo; through && i <= hi; i++)
            {
                var (cx, cy) = axis == 'x' ? (i, row) : (row, i);
                if (!IsRubbleCell(cx, cy)) through = false; else cells.Add((cx, cy));
            }
            if (through) rows[row] = cells;
        }
        var result = new List<Breach>();
        var group = new List<int>();
        void Flush()
        {
            if (group.Count >= minWidth)
            {
                var cells = group.SelectMany(r => rows[r]).OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
                int thick = group.Max(r => rows[r].Count);
                result.Add(new Breach(cells, axis == 'x' ? 'y' : 'x', group.Count, thick));
            }
            group.Clear();
        }
        int prev = int.MinValue;
        foreach (var row in rows.Keys)
        {
            if (group.Count > 0 && row != prev + 1) Flush();
            group.Add(row); prev = row;
        }
        Flush();
        return result;
    }

    private bool WallLikeAt(int a, int row, char axis) => axis == 'x' ? WallLike(a, row) : WallLike(row, a);

    // ------------------------------------------------------------------ repair (4.1)

    /// <summary>What mending the cell takes: the piece that stood there and the share of it to rebuild (a downed cell all of it, a damaged one the share it lost), with its worker hours and materials. Null when nothing there is damaged.</summary>
    public RepairNeed? RepairNeedAt(int x, int y, int z)
    {
        if (HitsOf(KeyOf(x, y, z)) is not { } h || h.Find(Local(x, y)) is not { } e || Costs.Get(e.Piece) is not { } c) return null;
        int share = e.Down || c.Hp <= 0 ? 1000 : (int)Math.Clamp((long)e.Lost * 1000 / c.Hp, 0, 1000);
        return new RepairNeed(e.Piece, share, c.MilliHours * share / 1000, [.. c.Materials.Select(m => (m.Good, m.Grams * share / 1000))], e.Down);
    }

    /// <summary>
    /// Mends the cells (in (z, y, x) order, ground first): a downed cell gets its piece back (rubble cleared) and a damaged one its hit points, once <paramref name="pay"/> has taken what it needs (the caller's labour and goods: the economy's, a later part).
    /// A wall-walk or parapet cell is mended only over a standing wall. Returns the cells mended; one <c>wall_repaired</c> entry follows when any was.
    /// </summary>
    public IReadOnlyList<(int X, int Y, int Z)> Repair(IEnumerable<(int X, int Y, int Z)> cells, long sec, Func<RepairNeed, bool> pay, EntityId cause = default)
    {
        var w = new Work();
        var done = new List<(int X, int Y, int Z)>();
        var mended = new Dictionary<long, string>();
        foreach (var (x, y, z) in cells.OrderBy(c => c.Z).ThenBy(c => c.Y).ThenBy(c => c.X))
        {
            if (!Visited(x, y) || RepairNeedAt(x, y, z) is not { } need) continue;
            var k = KeyOf(x, y, z);
            var hits = HitsFor(w, k);
            if (need.Down)
            {
                string? now = StructureAt(x, y, z);
                if (now != null && !Walls.IsRubble(now)) continue;                          // something else stands there now (construction built over it)
                if (z > 0 && !Stands(x, y, z, need.Piece, mended)) continue;
            }
            if (!pay(need)) continue;
            hits.Remove(Local(x, y));
            if (need.Down) { SetCell(w, x, y, z, need.Piece); mended[ConstructionIds.CellKey(x, y, z)] = need.Piece; }
            done.Add((x, y, z));
        }
        if (done.Count == 0) return done;
        Commit(w);
        Chronicle?.Invoke(new GameTime(sec), "wall_repaired", "ch_sg_repaired", [ChronParam.Count("x", done[0].X), ChronParam.Count("y", done[0].Y), ChronParam.Count("cells", done.Count)], [],
            [new Cause(CauseKind.Siege, cause == default ? null : cause, "", "ch_note_sg_masons")], [("x", done[0].X.ToString()), ("y", done[0].Y.ToString()), ("cells", done.Count.ToString())]);
        return done;
    }

    /// <summary>Whether a piece laid back at an upper cell would stand: a wall-table piece needs a standing wall directly under it (this very repair's included); any other the Phase 5 support rules.</summary>
    private bool Stands(int x, int y, int z, string piece, Dictionary<long, string> mended)
    {
        PieceCost? At(int ax, int ay, int az) => mended.TryGetValue(ConstructionIds.CellKey(ax, ay, az), out var mp) ? Costs.Get(mp) : Construction?.BuiltStructure(ax, ay, az) ?? (StructureAt(ax, ay, az) is { } s ? Costs.Get(s) : null);
        var c = Costs[piece];
        if (Walls.Stat(piece) != null) return At(x, y, z - 1) is { Kind: not PieceKind.Ground } below && (Walls.Stat(below.Id) != null || PieceCosts.IsSupport(below.Kind));
        return Support.Check(x, y, z, c, At).Ok;
    }
}
