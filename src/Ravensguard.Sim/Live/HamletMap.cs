using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Live;

public enum GroundKind : byte { None, Grass, Dirt, Cobble }

/// <summary>Scenery that is not a building piece: trees, wells, stalls, braziers. Wide = a 64 px sprite spanning X and X+1; otherwise centred on its cell.</summary>
public sealed record Prop(int X, int Y, int Z, string Sprite, bool Wide = false, bool Blocks = true, int LightRadius = 0, int LightIntensity = 0);

/// <summary>
/// The static look of the world next to the walkable <see cref="LevelGrid"/>: which piece stands in each cell, the ground outdoors and the scenery.
/// Immutable after building; the view reads it, never writes it (plan 2.1a).
/// </summary>
public sealed class HamletMap
{
    public LevelGrid Grid { get; }
    public PieceCatalog Pieces { get; }
    public string?[] Structure { get; }
    public string?[] Fixture { get; }
    public GroundKind[] Ground { get; }
    public byte[] GroundVariant { get; }
    public List<Prop> Props { get; } = new();
    public List<PlacedBuilding> Buildings { get; } = new();

    public HamletMap(LevelGrid grid, PieceCatalog pieces)
    {
        Grid = grid; Pieces = pieces;
        int n = grid.CellCount;
        Structure = new string?[n]; Fixture = new string?[n];
        Ground = new GroundKind[n]; GroundVariant = new byte[n];
    }

    public int Idx(int x, int y, int z) => Grid.Index(x, y, z);
    public string? StructureAt(int x, int y, int z) => Grid.InBounds(x, y, z) ? Structure[Idx(x, y, z)] : null;
    public string? FixtureAt(int x, int y, int z) => Grid.InBounds(x, y, z) ? Fixture[Idx(x, y, z)] : null;
    public GroundKind GroundAt(int x, int y, int z) => Grid.InBounds(x, y, z) ? Ground[Idx(x, y, z)] : GroundKind.None;
    public PieceKind? KindAt(int x, int y, int z) => Pieces.KindOf(StructureAt(x, y, z));

    /// <summary>Wall, window or post: the cell is part of a wall line (decides cap versus front face and door orientation).</summary>
    public bool IsWallish(int x, int y, int z) => KindAt(x, y, z) is PieceKind.Wall or PieceKind.Window or PieceKind.Post;

    /// <summary>Light sources that never move: lit hearths and forges, braziers and sconces.</summary>
    public IEnumerable<LightSource> StaticLights()
    {
        var g = Grid;
        for (int z = g.MinZ; z <= g.MaxZ; z++)
            for (int y = 0; y < g.Height; y++)
                for (int x = 0; x < g.Width; x++)
                {
                    var f = Fixture[Idx(x, y, z)];
                    if (f == "hearth") yield return new LightSource(x, y, z, 6, 215);
                    else if (f == "forge") yield return new LightSource(x, y, z, 7, 255);
                }
        foreach (var p in Props)
            if (p.LightRadius > 0) yield return new LightSource(p.X, p.Y, p.Z, p.LightRadius, p.LightIntensity);
    }

    /// <summary>Opaque cells are walls; roofed cells are the floors inside buildings (also the upper storeys).</summary>
    public LightGrid BuildLightGrid()
    {
        var g = Grid; var lg = new LightGrid(g);
        for (int z = g.MinZ; z <= g.MaxZ; z++)
            for (int y = 0; y < g.Height; y++)
                for (int x = 0; x < g.Width; x++)
                {
                    var k = Pieces.KindOf(Structure[Idx(x, y, z)]);
                    if (k == PieceKind.Wall) lg.SetOpaque(x, y, z, true);
                    if (k is { } kk && PieceCatalog.IsRoomKind(kk)) lg.SetIndoor(x, y, z, true);
                }
        lg.SetStaticLights(StaticLights());
        return lg;
    }
}
