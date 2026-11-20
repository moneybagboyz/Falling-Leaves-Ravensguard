using Ravensguard.Content;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Construction;

// P5-B: the live tier's hero on the cells, and the daily rust and rot of things kept in stores that stand on cells (items-quality-and-wear 4.4, now that rooms know Indoor).
public sealed partial class ConstructionWorld
{
    private long _heroCell = long.MinValue;

    /// <summary>The cell the hero stands on in the live tier (the main thread sets it each frame; a fall reads it on the tick worker). Null when not known.</summary>
    public (int X, int Y, int Z)? HeroCell
    {
        get { long v = Interlocked.Read(ref _heroCell); return v == long.MinValue ? null : ConstructionIds.Unpack(v); }
        set => Interlocked.Exchange(ref _heroCell, value is { } c ? ConstructionIds.CellKey(c.X, c.Y, c.Z) : long.MinValue);
    }

    /// <summary>The hero came down with a floor: the street view moves him to the level he landed on.</summary>
    public void HeroLanded(int x, int y, int z) => Event(ConstructionEventKind.HeroFell, Book.AdvancedSec, x, y, z, "", default);

    /// <summary>
    /// One day of rust and rot (items 4.4, the H4 daily slice; run at hour 3) for the item rows kept in stores that stand on cells: a site's pile and rubble, a chest. Indoors (a room
    /// RoomDetector v2 calls Indoor) they stay dry; out of doors they lie damp. Iron rusts 2 pm a day damp (mail twice), leather and wood rot only when wet.
    /// </summary>
    public int CorrosionDay(int day)
    {
        if (Book.Placed.Count == 0) return 0;
        var where = new Dictionary<Core.EntityId, Exposure>();
        foreach (var p in Book.Placed.Values) where[p.Owner] = Rooms.RoomAt(p.X, p.Y, p.Z) is { Indoor: true } ? Exposure.Dry : Exposure.Damp;
        var items = Econ.Items; int lost = 0;
        foreach (int row in items.LiveRows().ToList())
        {
            ref var r = ref items.At(row);
            if (!where.TryGetValue(r.Owner, out var ex) || ex == Exposure.Dry) continue;
            var owner = r.Owner;
            lost += Corrosion.Day(items, row, ex, day, r.Shape.Contains("mail", StringComparison.Ordinal), int.MaxValue, Econ.OtherStores.GetValueOrDefault(owner), MaterialCategory);
        }
        return lost;
    }

    private string? MaterialCategory(string material) => Content != null && Content.TryGet<MaterialDef>(material, out var m) ? m.Category : null;
}
