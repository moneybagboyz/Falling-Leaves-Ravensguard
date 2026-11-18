namespace Ravensguard.Sim.WorldGen.Region;

/// <summary>
/// The seam margins of a generated region (seamless-regions notes N5, N25): how far from the region's edge its settlements, lots and fields stand, so no lot or field is
/// ever cut by the border between two regions. A generator invariant: every region of the grid, for every seed, must meet <see cref="Violations"/> being empty.
/// </summary>
public readonly record struct RegionSeamMargins(int SettlementCentre, int LotCorner, int FieldTile)
{
    /// <summary>A settlement centre stands at least this far from any edge (the placer keeps 8 tiles of 64 m).</summary>
    public const int MinSettlementCentreM = 480;
    /// <summary>A lot corner stands at least this far from any edge (the spec's floor is 64 m; over both pinned seeds' whole grids the generator keeps 229 m).</summary>
    public const int MinLotCornerM = 200;
    /// <summary>A field tile stands at least this far from any edge (not on the edge row).</summary>
    public const int MinFieldTileM = 64;

    private static int EdgeDistance(int x, int y) => Math.Min(Math.Min(x, y), Math.Min(Region.Meters - 1 - x, Region.Meters - 1 - y));

    /// <summary>The nearest approach to an edge in <paramref name="r"/> (<see cref="int.MaxValue"/> for a kind it has none of).</summary>
    public static RegionSeamMargins Of(Region r)
    {
        int settle = int.MaxValue, lot = int.MaxValue, field = int.MaxValue;
        foreach (var s in r.Settlements)
        {
            settle = Math.Min(settle, EdgeDistance(s.CenterX, s.CenterY));
            foreach (var l in s.Plan.Lots) lot = Math.Min(lot, Math.Min(EdgeDistance(l.Rect.X, l.Rect.Y), EdgeDistance(l.Rect.Right, l.Rect.Bottom)));
        }
        foreach (var f in r.Fields) field = Math.Min(field, EdgeDistance(f.Tile % Region.Size * Region.TileM, f.Tile / Region.Size * Region.TileM));
        return new RegionSeamMargins(settle, lot, field);
    }

    /// <summary>What is wrong, one line each; empty when the region keeps every margin.</summary>
    public IEnumerable<string> Violations()
    {
        if (SettlementCentre < MinSettlementCentreM) yield return $"a settlement centre stands {SettlementCentre} m from an edge (minimum {MinSettlementCentreM})";
        if (LotCorner < MinLotCornerM) yield return $"a lot stands {LotCorner} m from an edge (minimum {MinLotCornerM})";
        if (FieldTile < MinFieldTileM) yield return $"a field stands {FieldTile} m from an edge (minimum {MinFieldTileM})";
    }
}
