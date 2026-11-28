using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.Region;

/// <summary>A point in region metres: (0,0) is the north-west corner of the 32 km square, x east, y south.</summary>
public readonly record struct PointM(int X, int Y);
/// <summary>An axis-aligned rectangle in region metres (top-left X,Y and size W,H).</summary>
public readonly record struct RectM(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
    public int CenterX => X + W / 2;
    public int CenterY => Y + H / 2;
    public bool Overlaps(RectM o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
}

public enum WaterKind : byte { Stream, River }
/// <summary>How a waterway ends: off the region edge, in the sea, in a lake, joining another waterway, or continuing as the next chain (stream becomes river).</summary>
public enum WaterEnd : byte { Edge, Sea, Lake, Joins, Continues }

/// <summary>A stream or river as a polyline with a width and depth at every vertex (plan §4A.4). Points run upstream to downstream.</summary>
public sealed class Waterway
{
    public int Id;
    public WaterKind Kind;
    public PointM[] Points = [];
    public int[] WidthCm = [];
    public int[] DepthCm = [];
    /// <summary>Region tile indices the chain passes through (unsmoothed D8 path).</summary>
    public int[] Tiles = [];
    public WaterEnd End;
    /// <summary>Waterway id for Joins/Continues, lake id for Lake, -1 otherwise.</summary>
    public int EndRef = -1;
    public int LengthM;
    /// <summary>Catchment at the downstream end, tenths of km2.</summary>
    public int CatchmentTenthsKm2;
}

public sealed class Lake
{
    public int Id;
    public int Tiles;
    public int SurfaceCm;
    public int MaxDepthCm;
    public PointM Center;
    public int MinTileX, MinTileY, MaxTileX, MaxTileY;
}

public static class WaterFlags
{
    public const byte Stream = 1, River = 2, Lake = 4, Sea = 8;
}

public enum SettlementClass : byte { Town, Village, Farmstead }
public enum DwellingType : byte { None, Cottage, Longhouse, Burgage, Manor }
public enum LotUse : byte { Cottage, Longhouse, Burgage, Manor, Chapel, Church, Smithy, Tavern, Mill, Barn, Granary }

/// <summary>
/// One house plot in a settlement plan. Rect is the whole plot in region metres; Dwelling is the intended
/// dwelling type for a household (None for workplaces and civic lots); BlueprintId names a blueprint in
/// content/base/blueprints (empty when none exists yet, e.g. a town church or a watermill).
/// </summary>
public sealed record Lot(int Index, LotUse Use, DwellingType Dwelling, string BlueprintId, RectM Rect, byte Facing, byte Wealth)
{
    /// <summary>A house that stands empty because the settlement shrank (too little farmland). Not a home for anyone.</summary>
    public bool Vacant { get; init; }
    /// <summary>A lived-in house plot (vacant houses excluded).</summary>
    public bool IsDwelling => Dwelling != DwellingType.None && !Vacant;
}

public sealed record Street(PointM A, PointM B, int WidthM);

public sealed class SettlementPlan
{
    /// <summary>True when the main street runs east-west.</summary>
    public bool Horizontal;
    public RectM Square;
    public int RadiusM;
    public List<Street> Streets { get; } = [];
    public List<Lot> Lots { get; } = [];
    public int ChurchLot = -1, ManorLot = -1, MillLot = -1;
    public int DwellingCount => Lots.Count(l => l.IsDwelling);
}

public sealed class RegionSettlement
{
    public EntityId Id;
    public string Name = "";
    public SettlementClass Class;
    public int TileX, TileY;
    public int CenterX, CenterY;
    /// <summary>Intended resident population (part B creates exactly this many Persons).</summary>
    public int TargetPop;
    public int Households;
    /// <summary>Settlement id in the history when this place came from it, else <see cref="EntityId.None"/>.</summary>
    public EntityId HistoryId;
    /// <summary>Index into <see cref="WorldHistory.Realms"/> or -1.</summary>
    public int Realm = -1;
    public int FiefIndex = -1;
    /// <summary>Culture index of the people here (the realm's culture; picks the building style, plan 2.9).</summary>
    public int Culture;
    public bool OnRiver;
    public int NearWaterway = -1;
    public SettlementPlan Plan = new();
    public int FieldAcresDemand, FieldAcres;
    public List<int> Parcels { get; private set; } = [];
    public int RoadNode = -1;

    /// <summary>A copy that shares the references to everything immutable and owns a fresh parcel list (the plan is replaced by the caller).</summary>
    internal RegionSettlement CloneShallow()
    {
        var c = (RegionSettlement)MemberwiseClone();
        c.Parcels = [.. Parcels];
        return c;
    }
}

public enum Crop : byte { Fallow, Wheat, Rye, Barley, Oats, Peas, Beans }
public enum OwnerKind : byte { Demesne, Glebe, Tenant, Burgher, Freeholder }
/// <summary>Owner placeholder: part B binds LotIndex (a dwelling lot of the settlement) to a real household.</summary>
public readonly record struct ParcelOwner(OwnerKind Kind, int FiefIndex, int LotIndex);

public sealed class Parcel
{
    public EntityId Id;
    public int Settlement;
    public ParcelOwner Owner;
    /// <summary>Three-field rotation slot: 0 winter grain, 1 spring grain, 2 fallow (rotates yearly).</summary>
    public byte Slot;
    public int FieldStart, FieldCount;
    public int AvgFertility;
    public int TileX, TileY;
}

public struct Field
{
    public EntityId Id;
    public int Tile;
    public int Parcel;
    public byte Fertility;
    public byte Slot;
    public Crop Crop;
}

public sealed class Fief
{
    public EntityId Id;
    public string Name = "";
    public int Realm = -1;
    public int SeatSettlement;
    public int ManorLot = -1;
    public List<int> Settlements { get; } = [];
    public int Acres, Households, Pop;
}

public enum RoadClass : byte { None = 0, Lane = 1, Track = 2, Road = 3, Highway = 4 }
public enum RoadNodeKind : byte { Town, Village, Farmstead, Gate }
public enum CrossingKind : byte { Ford, Bridge }
public sealed record RoadNode(int Id, RoadNodeKind Kind, int Settlement, int X, int Y);
public sealed record RoadCrossing(PointM At, CrossingKind Kind, int WaterwayId, int WidthCm);

public sealed class RoadEdge
{
    public int Id, A, B;
    public RoadClass Class;
    public int WidthCm;
    public PointM[] Points = [];
    public int LengthM;
    public List<RoadCrossing> Crossings { get; } = [];
}

public enum SiteKind : byte { Ruin, Cave, Mine, Barrow, Battlefield, Hoard }
public enum MineState : byte { Active, Abandoned, Flooded, Collapsed }

public sealed class Site
{
    public EntityId Id;
    public SiteKind Kind;
    /// <summary>Ruin cause (sacked, razed, abandoned...), cave kind (den, hideout, hermit, smugglers, river), mine or barrow state.</summary>
    public string SubKind = "";
    public string Name = "";
    public int TileX, TileY, X, Y;
    /// <summary>The chronicle event that made this site (ruin event, battle, mine collapse), None for natural places.</summary>
    public EntityId HistoryEvent;
    public EntityId CauseEvent;
    /// <summary>History settlement id for ruins.</summary>
    public EntityId HistorySettlement;
    /// <summary>History artefact for hoards (lost or buried named items).</summary>
    public EntityId Artefact;
    public int Levels = 1;
    public MineralType Mineral;
    public int Tonnage;
    public MineState Mine;
    public int Dead;
    public int Year;
    public bool MonsterLair;
    /// <summary>draugr (barrow dead), cave_beast or bog_thing when <see cref="MonsterLair"/>.</summary>
    public string LairKind = "";
    /// <summary>P8-LM-3: a mine the hero opened on a prospected deposit (made after generation by <c>MineSites</c>; not part of the region hash).</summary>
    public bool HeroOpened;
}

public sealed record RegionVein(MineralType Type, int TileX, int TileY, int RadiusTiles, int Tonnage, int ContinentCell);
public sealed record RegionRealm(int HistoryIndex, string Name, int Tiles);
/// <summary>An alive war near the region; DistanceM is from the region centre to the contested place (may be outside the region).</summary>
public sealed record ActiveWarMarker(int WarIndex, string Name, int Cell, int X, int Y, int DistanceM);
