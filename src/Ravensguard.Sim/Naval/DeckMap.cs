using Ravensguard.Sim.Carts;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Naval;

/// <summary>What a cell of a ship's deck is (ships-and-naval-war 4.6): nothing (water, or air over the hull), a floor to walk on, a gunwale or stem (blocks movement, a low rail), a mast (blocks), a castle's parapet (blocks movement and missiles), a ladder or a boarding plank (open), a cabin or other solid (blocks both).</summary>
public enum DeckKind : byte { None, Floor, Gunwale, Mast, Parapet, Ladder, Plank, Obstacle }

/// <summary>
/// P7-SH-A: a ship's decks as a grid of its own (ships-and-naval-war 3.1, 4.6), read from the vehicle's part rows: one layer a deck (deck 0 the hull's deck, 1 the castles, 2 the fighting top), one cell a metre, columns across the beam and rows
/// from the stern to the bow. Local millimetres have their origin at the centre of the grid. A deck is also a <see cref="LevelGrid"/> (levels are decks; a ship's ladder is a ladder portal to the cell above), so the shared pathfinder walks it.
/// Only fitted, unbroken parts count. Integer only; the same rows always give the same map.
/// </summary>
public sealed class DeckMap
{
    public int Width { get; }
    public int Length { get; }
    public int Decks { get; }
    private readonly DeckKind[] _k;
    /// <summary>The walkable cells as a level grid (z is the deck), with the ladders as portals.</summary>
    public LevelGrid Grid { get; }
    /// <summary>Boarding plank cells: (x, y) on deck 0 and the outward step (dx, dy) a plank is run out along (across the beam from a side cell, along the keel from a stem or stern cell).</summary>
    public IReadOnlyList<(int X, int Y, int Dx, int Dy)> Planks { get; }
    private LevelPathfinder? _path;

    private DeckMap(int w, int l, int decks, DeckKind[] k, LevelGrid grid, List<(int, int, int, int)> planks)
    {
        Width = w; Length = l; Decks = decks; _k = k; Grid = grid; Planks = planks;
    }

    private int At(int x, int y, int deck) => (deck * Length + y) * Width + x;
    public bool InRange(int x, int y, int deck) => (uint)x < (uint)Width && (uint)y < (uint)Length && (uint)deck < (uint)Decks;
    public DeckKind Kind(int x, int y, int deck) => InRange(x, y, deck) ? _k[At(x, y, deck)] : DeckKind.None;
    public static bool IsWalkable(DeckKind k) => k is DeckKind.Floor or DeckKind.Ladder or DeckKind.Plank;
    public bool Walkable(int x, int y, int deck) => IsWalkable(Kind(x, y, deck));
    /// <summary>A cell that stops a body (a gunwale, a mast, a parapet, a solid).</summary>
    public static bool Blocks(DeckKind k) => k is DeckKind.Gunwale or DeckKind.Mast or DeckKind.Parapet or DeckKind.Obstacle;

    // ------------------------------------------------------------------ local millimetres

    /// <summary>Local millimetres of the centre of cell column <paramref name="x"/>.</summary>
    public int CentreX(int x) => x * 1000 + 500 - Width * 500;
    /// <summary>Local millimetres of the centre of cell row <paramref name="y"/>.</summary>
    public int CentreY(int y) => y * 1000 + 500 - Length * 500;
    public int CellX(int lxMm) => FloorDiv(lxMm + Width * 500, 1000);
    public int CellY(int lyMm) => FloorDiv(lyMm + Length * 500, 1000);
    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    /// <summary>Whether a local point is inside the grid's rectangle (the ship's footprint).</summary>
    public bool Inside(int lxMm, int lyMm) => (uint)(lxMm + Width * 500) < (uint)(Width * 1000) && (uint)(lyMm + Length * 500) < (uint)(Length * 1000);
    public DeckKind KindAtMm(int lxMm, int lyMm, int deck) => Inside(lxMm, lyMm) ? Kind(CellX(lxMm), CellY(lyMm), deck) : DeckKind.None;

    /// <summary>
    /// Whether a body's corner at local (lx, ly) is stopped. A rider is stopped by a gunwale, mast, parapet or solid, never by empty space (he can step over the side); a body that is not riding is stopped by the whole footprint: a hull is solid
    /// from outside, so he boards by a plank or a ladder, not through the planking.
    /// </summary>
    public bool BlocksMoveMm(int lxMm, int lyMm, int deck, bool rider)
    {
        if (!Inside(lxMm, lyMm)) return false;
        return rider ? Blocks(KindAtMm(lxMm, lyMm, deck)) : true;
    }

    /// <summary>A missile is stopped by a parapet or a solid on the deck it flies along (the crenel rule of sieges-and-engines 4.4 belongs to the naval fight).</summary>
    public bool BlocksMissileMm(int lxMm, int lyMm, int deck) => KindAtMm(lxMm, lyMm, deck) is DeckKind.Parapet or DeckKind.Obstacle;

    // ------------------------------------------------------------------ walking

    /// <summary>The shared pathfinder over the decks (cells are metres, z is the deck): a path from one deck cell to another, up and down the ladders.</summary>
    public LevelPathfinder Pathfinder => _path ??= new LevelPathfinder(Grid);
    public PathResult? Path(Cell3 from, Cell3 to) => Pathfinder.FindPath(from, to);

    // ------------------------------------------------------------------ building

    /// <summary>The decks of a vehicle's part rows (<paramref name="width"/> columns, <paramref name="length"/> rows from the stern).</summary>
    public static DeckMap Build(IReadOnlyList<PartRow> rows, VehicleCatalog cat, int width, int length)
    {
        int decks = 1; foreach (var r in rows) if (r.Built && r.Deck + 1 > decks) decks = r.Deck + 1;
        var floor = new bool[decks * length * width]; var castle = new bool[floor.Length];
        var kind = new DeckKind[floor.Length];
        int A(int x, int y, int d) => (d * length + y) * width + x;
        var planks = new List<(int, int, int, int)>();
        // the parts of every cell: what makes it a floor, what blocks, a ladder, a plank
        var blocker = new DeckKind[floor.Length]; var ladder = new bool[floor.Length]; var plank = new bool[floor.Length]; var present = new bool[floor.Length];
        foreach (var r in rows)
        {
            if (!r.Built || r.X < 0 || r.X >= width || r.Y < 0 || r.Y >= length) continue;
            var tags = cat.PartList[r.Part].Tags; int i = A(r.X, r.Y, r.Deck);
            if (r.Deck == 0 ? tags.Contains("hull") || tags.Contains("deck") : tags.Contains("castle") || tags.Contains("top") || tags.Contains("deck")) { floor[i] = true; present[i] = true; }
            if (tags.Contains("castle")) castle[i] = true;
            if (tags.Contains("gunwale") || tags.Contains("stem")) blocker[i] = DeckKind.Gunwale;
            if (tags.Contains("mast") && blocker[i] == DeckKind.None) { blocker[i] = DeckKind.Mast; present[i] = true; }
            if (tags.Contains("cabin")) blocker[i] = DeckKind.Obstacle;
            if (tags.Contains("ladder")) { ladder[i] = true; present[i] = true; }
            if (tags.Contains("plank")) { plank[i] = true; present[i] = true; }
        }
        for (int d = 0; d < decks; d++)
            for (int y = 0; y < length; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = A(x, y, d);
                    if (!present[i]) { kind[i] = DeckKind.None; continue; }
                    if (ladder[i]) kind[i] = DeckKind.Ladder;
                    else if (plank[i]) kind[i] = DeckKind.Plank;
                    else if (blocker[i] != DeckKind.None) kind[i] = blocker[i];
                    else if (castle[i] && !(d > 0 && ladder[A(x, y, d - 1)]) && IsEdge(present, width, length, x, y, d)) kind[i] = DeckKind.Parapet;    // the cell a ladder comes up through is the gap in the parapet
                    else kind[i] = floor[i] ? DeckKind.Floor : DeckKind.None;
                }
        for (int y = 0; y < length; y++)
            for (int x = 0; x < width; x++)
                if (plank[A(x, y, 0)])
                {
                    int dx = x == 0 ? -1 : x == width - 1 ? 1 : 0, dy = dx != 0 ? 0 : y == 0 ? -1 : y == length - 1 ? 1 : 0;
                    planks.Add((x, y, dx, dy));
                }
        var grid = new LevelGrid(width, length, 0, decks - 1);
        for (int d = 0; d < decks; d++)
            for (int y = 0; y < length; y++)
                for (int x = 0; x < width; x++)
                    if (IsWalkable(kind[A(x, y, d)])) grid.SetOpen(x, y, d, true);
        for (int d = 0; d + 1 < decks; d++)
            for (int y = 0; y < length; y++)
                for (int x = 0; x < width; x++)
                    if (kind[A(x, y, d)] == DeckKind.Ladder && IsWalkable(kind[A(x, y, d + 1)])) grid.AddPortal(PortalKind.Ladder, new Cell3(x, y, d), new Cell3(x, y, d + 1), 20);
        return new DeckMap(width, length, decks, kind, grid, planks);
    }

    /// <summary>A castle cell with an orthogonal neighbour that is not a part of the castle's deck stands on the open edge: its parapet.</summary>
    private static bool IsEdge(bool[] present, int w, int l, int x, int y, int d)
    {
        bool Has(int xx, int yy) => (uint)xx < (uint)w && (uint)yy < (uint)l && present[(d * l + yy) * w + xx];
        return !Has(x - 1, y) || !Has(x + 1, y) || !Has(x, y - 1) || !Has(x, y + 1);
    }
}
