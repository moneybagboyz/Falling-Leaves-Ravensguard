using Ravensguard.Content;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Building;

/// <summary>Where a blueprint ended up: footprint, exterior doors and the portals it created, in world cells.</summary>
public sealed record PlacedBuilding(int X, int Y, int Width, int Height, List<Cell3> ExteriorDoors, List<Portal> Portals);

/// <summary>Writes a blueprint into a <see cref="LevelGrid"/>: floors, doors, stairs and ladders open, walls and furniture closed, stairs/ladders as portals.</summary>
public static class BlueprintPlacer
{
    public const int StairCost = 30, LadderCost = 40;

    public static PlacedBuilding Place(BuildingGrid blueprint, PieceCatalog pieces, LevelGrid world, int originX, int originY, int quarterTurns = 0, bool mirror = false)
    {
        var g = blueprint.Transform(quarterTurns, mirror);
        if (originX < 0 || originY < 0 || originX + g.Width > world.Width || originY + g.Height > world.Height)
            throw new ArgumentOutOfRangeException(nameof(originX), "blueprint does not fit in the world grid");
        var detector = new RoomDetector(pieces, []);
        var doors = new List<Cell3>();
        var portals = new List<Portal>();
        foreach (var (z, lv) in g.Levels)
        {
            if (z < world.MinZ || z > world.MaxZ) throw new ArgumentOutOfRangeException(nameof(world), $"world has no level z={z}");
            for (int y = 0; y < lv.Height; y++)
                for (int x = 0; x < lv.Width; x++)
                {
                    int i = lv.Idx(x, y);
                    var kind = pieces.KindOf(lv.Structure[i]);
                    if (kind == null) continue;
                    var fixture = pieces.KindOf(lv.Fixture[i]);
                    bool open = PieceCatalog.IsWalkable(kind.Value) && !(fixture is { } f && PieceCatalog.IsSolidFixture(f));
                    world.SetOpen(originX + x, originY + y, z, open);
                    if (kind == PieceKind.Door && detector.IsExteriorDoor(lv, x, y)) doors.Add(new Cell3(originX + x, originY + y, z));
                    var here = new Cell3(originX + x, originY + y, z);
                    var up = new Cell3(here.X, here.Y, z + 1);
                    if (kind == PieceKind.StairsUp && z + 1 <= world.MaxZ && g.Level(z + 1) != null && pieces.KindOf(g.StructureAt(x, y, z + 1)) == PieceKind.StairsDown)
                        portals.Add(world.AddPortal(PortalKind.Stairs, here, up, StairCost));
                    else if (kind == PieceKind.Ladder && z + 1 <= world.MaxZ && g.Level(z + 1) != null && pieces.KindOf(g.StructureAt(x, y, z + 1)) == PieceKind.Trapdoor)
                        portals.Add(world.AddPortal(PortalKind.Ladder, here, up, LadderCost));
                }
        }
        return new PlacedBuilding(originX, originY, g.Width, g.Height, doors, portals);
    }
}
