using Ravensguard.Content;

namespace Ravensguard.Sim.Building;

/// <summary>
/// P7-LB2 (large-buildings 4.4): what the large room types ask of a room beyond its area and tags: the bounding box (a ropewalk's length and width), the windows on its edge (a hall's light), its doors
/// and their tags (an armoury's lockable door, a depot's one door). Both detectors fill it the same way, so the blueprint check and the live rooms agree (RD-1 parity).
/// </summary>
public readonly record struct RoomShape(int Length, int Width, int Windows, int Doors, IReadOnlyCollection<string> DoorTags)
{
    /// <summary>The shape of a set of cells: the longer and shorter side of its bounding box.</summary>
    public static (int Length, int Width) Box(IEnumerable<(int X, int Y)> cells)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (var (x, y) in cells) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
        if (x0 == int.MaxValue) return (0, 0);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        return (Math.Max(w, h), Math.Min(w, h));
    }
}

public static class RoomRules
{
    /// <summary>Whether the type has any large-building rule at all (every rule at its default is off).</summary>
    public static bool Any(RoomTypeDef t) => t.MinLength > 0 || t.MinWidth > 0 || t.MaxWidth > 0 || t.LightPerM2 > 0 || t.DoorTag != null || t.MaxDoors > 0 || t.MinStationCount > 0;

    /// <summary>Whether a room of this area, tags and shape meets the type's large-building rules.</summary>
    public static bool Extra(RoomTypeDef t, int area, IReadOnlyDictionary<string, int> tags, in RoomShape s)
    {
        if (t.MinLength > 0 && s.Length < t.MinLength) return false;
        if (t.MinWidth > 0 && s.Width < t.MinWidth) return false;
        if (t.MaxWidth > 0 && s.Width > t.MaxWidth) return false;
        if (t.MaxDoors > 0 && s.Doors > t.MaxDoors) return false;
        if (t.DoorTag != null && !s.DoorTags.Contains(t.DoorTag)) return false;
        if (t.LightPerM2 > 0 && s.Windows + tags.GetValueOrDefault("lamp") < Math.Max(1, area / t.LightPerM2)) return false;
        if (t.MinStationCount > 0)
        {
            bool any = false;
            foreach (var (k, v) in tags) if (v >= t.MinStationCount && k.StartsWith("station:", StringComparison.Ordinal)) { any = true; break; }
            if (!any) return false;
        }
        return true;
    }
}
