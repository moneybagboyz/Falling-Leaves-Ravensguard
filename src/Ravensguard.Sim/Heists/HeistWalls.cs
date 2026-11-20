using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Heists;

/// <summary>
/// The walls of a heist site for an indoor fight (heists-and-jailbreaks 4.4): the fight runs on the site's real map, one-metre cells in millimetres from the map's corner (plus an origin where the fight is placed). A wall, rock and the chest stop a body; so do
/// a shut door, a grate and a cell's bars; a missile and the sight of a blow are stopped by walls, rock and shut doors (the bars let them through). Reads the run's door states, so a door a guard holds shut or the hero has broken changes the fight.
/// </summary>
public sealed class HeistWalls(HeistMap map, byte[] doors, int originXMm = 0, int originYMm = 0) : IFightWalls
{
    private HTile At(int xMm, int yMm, out HDoor? door)
    {
        int cx = FloorDiv(xMm - originXMm), cy = FloorDiv(yMm - originYMm);
        door = null;
        if (!map.In(cx, cy)) return HTile.Rock;
        var t = map.Tile(cx, cy);
        if (t is HTile.Door or HTile.CellDoor) door = map.DoorAt(cx, cy);
        return t;
    }

    private static int FloorDiv(int mm) => mm >= 0 ? mm / 1000 : -((-mm + 999) / 1000);

    private bool Shut(HDoor? d) => d != null && (d.Index >= doors.Length || doors[d.Index] < 2);

    public bool BlocksMove(int xMm, int yMm)
    {
        var t = At(xMm, yMm, out var d);
        return t is HTile.Wall or HTile.Rock or HTile.Chest || (t is HTile.Door or HTile.CellDoor && Shut(d));
    }

    public bool BlocksMissile(int xMm, int yMm)
    {
        var t = At(xMm, yMm, out var d);
        return t is HTile.Wall or HTile.Rock || (t == HTile.Door && d is { Grate: false } && Shut(d));
    }
}
