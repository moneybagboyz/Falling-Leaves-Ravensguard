using System.Security.Cryptography;
using System.Text;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.Region;

/// <summary>
/// Stage 3 output (plan §1A): one 32 x 32 km embark region at region-tile resolution (500 x 500 tiles of 64 m, about an acre).
/// Tile arrays are indexed y * Size + x; y grows southward. Polylines and plans use region metres (0..32000).
/// </summary>
public sealed class Region
{
    public const int Size = RegionSettings.Tiles;
    public const int Tiles = Size * Size;
    public const int TileM = RegionSettings.TileM;
    public const int Meters = RegionSettings.RegionM;
    public const double AcresPerTile = TileM * (double)TileM / 4046.86;

    public ulong Seed { get; init; }
    /// <summary>Continental cell of the north-west corner (the region covers 16 x 16 cells).</summary>
    public int CellX { get; init; }
    public int CellY { get; init; }

    /// <summary>Terrain height in centimetres above sea level (sea is 0 or below).</summary>
    public int[] HeightCm { get; private set; } = new int[Tiles];
    public short[] SlopePermille { get; } = new short[Tiles];
    public Biome[] Biomes { get; } = new Biome[Tiles];
    /// <summary>Soil fertility 0..100 (slope, drainage, alluvium).</summary>
    public byte[] Fertility { get; } = new byte[Tiles];
    /// <summary>Tree cover 0..100 before per-chunk tree placement.</summary>
    public byte[] Forest { get; private set; } = new byte[Tiles];
    /// <summary>0..100, 100 = freely drained.</summary>
    public byte[] Drainage { get; private set; } = new byte[Tiles];
    /// <summary><see cref="WaterFlags"/> bit set per tile.</summary>
    public byte[] Water { get; } = new byte[Tiles];
    /// <summary>Lake id + 1 per tile, 0 for none.</summary>
    public ushort[] LakeMap { get; private set; } = new ushort[Tiles];
    /// <summary>Catchment above each tile in tiles (flow accumulation, includes inflow from upstream cells).</summary>
    public int[] Catchment { get; private set; } = new int[Tiles];
    /// <summary>D8 receiver tile, -1 where water leaves the region or reaches the sea.</summary>
    public int[] FlowTo { get; private set; } = new int[Tiles];
    /// <summary><see cref="RoadClass"/> per tile.</summary>
    public byte[] Road { get; } = new byte[Tiles];
    /// <summary>1 when a settlement lot or square overlaps the tile.</summary>
    public byte[] Built { get; private set; } = new byte[Tiles];
    /// <summary>Realm index in <see cref="WorldHistory.Realms"/> per tile, 255 for none.</summary>
    public byte[] RealmMap { get; private set; } = new byte[Tiles];
    /// <summary>Fief index + 1 per tile (nearest member settlement), 0 for none.</summary>
    public ushort[] FiefMap { get; private set; } = new ushort[Tiles];
    /// <summary>Parcel index + 1 per field tile, 0 for none.</summary>
    public int[] ParcelMap { get; private set; } = new int[Tiles];
    /// <summary>MineralType + 1 where a vein lies under the tile, 0 for none.</summary>
    public byte[] Mineral { get; } = new byte[Tiles];

    public List<Waterway> Waterways { get; } = [];
    public List<Lake> Lakes { get; } = [];
    public List<RoadNode> RoadNodes { get; } = [];
    public List<RoadEdge> RoadEdges { get; } = [];
    /// <summary>Border crossings of the continental road network on this region's four edges (N, E, S, W order, sorted by tile); the neighbour across each edge holds the mirror.</summary>
    public List<RegionGate> Gates { get; } = [];
    public List<RegionSettlement> Settlements { get; private set; } = [];
    public List<Fief> Fiefs { get; } = [];
    public List<Parcel> Parcels { get; private set; } = [];
    public Field[] Fields { get; set; } = [];
    public List<Site> Sites { get; } = [];
    public List<RegionVein> Veins { get; } = [];
    public List<RegionRealm> Realms { get; } = [];
    public List<ActiveWarMarker> ActiveWars { get; } = [];
    /// <summary>Authored landmarks placed by <see cref="LandmarkPlacer"/> (a separate phase after generation; null until it has run). Not part of <see cref="ComputeHash"/>; see <see cref="LandmarkPlacer.ComputeHash"/>.</summary>
    public IReadOnlyList<PlacedLandmark>? Landmarks { get; set; }
    /// <summary>The landmark placer version this region's world uses (<see cref="RegionSettings.LandmarkPlacer"/>); not part of <see cref="ComputeHash"/>.</summary>
    public int PlacerVersion { get; init; }

    /// <summary>
    /// What the region looked like when background growth first changed it (cottages added to plans, fields and parcels appended): the lot count of every plan, the parcel count of every
    /// settlement, the totals and the hash of the generated region. Null for a region nobody has grown. The generated region is a pure function of seed and content and is
    /// never stored; growth is saved apart (<c>GrowthSave</c>) and replayed over a freshly generated region, so <see cref="ComputeHash"/> keeps answering with the generated hash.
    /// </summary>
    public sealed record GrowthBaseline(Lot[][] Lots, int[] SettlementParcels, int Parcels, int Fields, string Hash, int[] ParcelMap, byte[] Built, byte[] Forest, int[]? Streets = null);
    public GrowthBaseline? Baseline { get; private set; }
    /// <summary>Bumped by every change growth makes; the chunk generator rebuilds its indices when it differs from the version it was made at.</summary>
    public int GrowthVersion { get; private set; }

    /// <summary>Records the generated state before the first change growth makes (idempotent). Must be called on a region that has not been changed.</summary>
    public void PinGrowthBaseline()
    {
        if (Baseline != null) return;
        Baseline = new GrowthBaseline([.. Settlements.Select(s => s.Plan.Lots.ToArray())], [.. Settlements.Select(s => s.Parcels.Count)], Parcels.Count, Fields.Length, ComputeHash(), (int[])ParcelMap.Clone(), (byte[])Built.Clone(), (byte[])Forest.Clone(), [.. Settlements.Select(s => s.Plan.Streets.Count)]);
    }

    public void NoteGrown() => GrowthVersion++;

    /// <summary>
    /// Guards what background growth changes (the plans' lots, <see cref="Built"/>, <see cref="ParcelMap"/>, <see cref="Forest"/>, <see cref="Parcels"/>, <see cref="Fields"/>, the population's
    /// per-lot arrays) against the chunk generators that read them on the thread pool (review finding 1). Growth holds the write lock for a whole change, from reading the visited chunks
    /// before it to writing their deltas after it (<see cref="Ravensguard.Sim.Growth.RegionGrowth.Change"/>); a generator holds a read lock for a whole chunk, and the streamer holds one
    /// while it checks that a chunk made on a worker is still of the current <see cref="GrowthVersion"/> and records it. Recursive, so a generator called inside a change reads freely.
    /// </summary>
    public ReaderWriterLockSlim GrowthLock { get; } = new(LockRecursionPolicy.SupportsRecursion);

    /// <summary>Holds <see cref="GrowthLock"/> for reading until disposed.</summary>
    public ReadScope ReadGrowth() { GrowthLock.EnterReadLock(); return new ReadScope(GrowthLock); }
    /// <summary>Holds <see cref="GrowthLock"/> for writing until disposed.</summary>
    public WriteScope WriteGrowth() { GrowthLock.EnterWriteLock(); return new WriteScope(GrowthLock); }
    public readonly struct ReadScope(ReaderWriterLockSlim l) : IDisposable { public void Dispose() => l.ExitReadLock(); }
    public readonly struct WriteScope(ReaderWriterLockSlim l) : IDisposable { public void Dispose() => l.ExitWriteLock(); }

    /// <summary>
    /// The region as generated: plans with only their generated lots, settlements with only their generated parcels and the generated fields. A copy that shares every tile layer
    /// (the populator reads none that growth changes); returns the region itself when it has not grown. Used so the baseline people of a save are always regenerated from the
    /// generated region, whatever has been built since. With <paramref name="ownLayers"/> the copy owns its tile layers (a world that will grow it, as a trail does, must not change the baseline's).
    /// </summary>
    public Region GeneratedView(bool ownLayers = false)
    {
        var b = Baseline;
        if (b == null) return this;
        var c = (Region)MemberwiseClone();
        c.Baseline = null;
        c.Settlements = [];
        for (int i = 0; i < Settlements.Count; i++)
        {
            var s = Settlements[i]; var n = (RegionSettlement)s.CloneShallow();
            var plan = new SettlementPlan { Horizontal = s.Plan.Horizontal, Square = s.Plan.Square, RadiusM = s.Plan.RadiusM, ChurchLot = s.Plan.ChurchLot, ManorLot = s.Plan.ManorLot, MillLot = s.Plan.MillLot };
            plan.Streets.AddRange(b.Streets is { } bs && i < bs.Length ? s.Plan.Streets.Take(bs[i]) : s.Plan.Streets);     // P6-D4: the streets as generated
            plan.Lots.AddRange(b.Lots[i]);                      // the lots as generated (a vacant house refitted since stands vacant here)
            n.Plan = plan;
            n.Parcels.Clear();
            for (int k = 0; k < b.SettlementParcels[i] && k < s.Parcels.Count; k++) n.Parcels.Add(s.Parcels[k]);
            n.FieldAcres = n.Parcels.Sum(p => Parcels[p].FieldCount);
            c.Settlements.Add(n);
        }
        c.ParcelMap = ownLayers ? (int[])b.ParcelMap.Clone() : b.ParcelMap; c.Built = ownLayers ? (byte[])b.Built.Clone() : b.Built; c.Forest = ownLayers ? (byte[])b.Forest.Clone() : b.Forest;
        c.Parcels = [.. Parcels.Take(b.Parcels)];
        c.Fields = Fields.AsSpan(0, Math.Min(b.Fields, Fields.Length)).ToArray();
        return c;
    }

    private string? _trimmedHash;
    /// <summary>True once <see cref="TrimDerived"/> has released the layers nothing reads after generation.</summary>
    public bool IsTrimmed => _trimmedHash != null;

    /// <summary>
    /// Releases the layers that only generation (and the region map of the home region) reads: height, drainage, lake and fief maps, catchment, flow and realm maps,
    /// 4.5 MB of the 11 MB a region takes. The hash is computed first and kept, so <see cref="ComputeHash"/> still answers. For regions that only tick and stream
    /// (neighbours); never for the home region, whose map draws heights and borders.
    /// </summary>
    public void TrimDerived()
    {
        if (_trimmedHash != null) return;
        _trimmedHash = ComputeHash();
        HeightCm = []; Drainage = []; LakeMap = []; Catchment = []; FlowTo = []; RealmMap = []; FiefMap = [];
    }

    public static int Index(int x, int y) => y * Size + x;
    public static bool InBounds(int x, int y) => (uint)x < Size && (uint)y < Size;

    public IEnumerable<RegionSettlement> Towns => Settlements.Where(s => s.Class == SettlementClass.Town);
    public IEnumerable<RegionSettlement> Villages => Settlements.Where(s => s.Class == SettlementClass.Village);
    public IEnumerable<RegionSettlement> Farmsteads => Settlements.Where(s => s.Class == SettlementClass.Farmstead);
    public int TotalPopulation => Settlements.Sum(s => s.TargetPop);
    public int TotalHouseholds => Settlements.Sum(s => s.Households);
    public int FieldAcres => Fields.Length;

    /// <summary>Distance in metres from a region point to the nearest active war marker, int.MaxValue when none (plan §3f: starts keep 10 km away).</summary>
    public int NearestActiveWarM(int x, int y)
    {
        long best = long.MaxValue;
        foreach (var w in ActiveWars) best = Math.Min(best, Dist2(w.X - x, w.Y - y));
        return best == long.MaxValue ? int.MaxValue : (int)Isqrt(best);
    }

    /// <summary>Distance in metres to the nearest monster lair, int.MaxValue when none (plan §3f: starts keep 3 km away).</summary>
    public int NearestLairM(int x, int y)
    {
        long best = long.MaxValue;
        foreach (var s in Sites) if (s.MonsterLair) best = Math.Min(best, Dist2(s.X - x, s.Y - y));
        return best == long.MaxValue ? int.MaxValue : (int)Isqrt(best);
    }

    /// <summary>True when a start at this point keeps the plan's safety margins from active wars and monster lairs.</summary>
    public bool IsSafeStart(int x, int y, int warSafeM = 10_000, int lairSafeM = 3_000)
        => NearestActiveWarM(x, y) >= warSafeM && NearestLairM(x, y) >= lairSafeM;

    internal static long Dist2(long dx, long dy) => dx * dx + dy * dy;
    internal static long Isqrt(long v)
    {
        if (v <= 0) return 0;
        long r = (long)Math.Sqrt(v);
        while (r * r > v) r--;
        while ((r + 1) * (r + 1) <= v) r++;
        return r;
    }

    /// <summary>SHA-256 over every layer and list, integers little-endian, for determinism tests.</summary>
    public string ComputeHash()
    {
        if (Baseline != null) return Baseline.Hash;
        if (_trimmedHash != null) return _trimmedHash;
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        void S(string s) { L(s.Length); h.AppendData(Encoding.UTF8.GetBytes(s)); }
        void Id(EntityId id) => L((long)id.Value);
        void Ints(int[] a) { var t = new byte[a.Length * 4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(t, 0); for (int i = 0; i < a.Length; i++) System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(t.AsSpan(i * 4), a[i]); h.AppendData(t); }
        void Shorts(short[] a) { var t = new byte[a.Length * 2]; for (int i = 0; i < a.Length; i++) System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(t.AsSpan(i * 2), a[i]); h.AppendData(t); }
        void UShorts(ushort[] a) { var t = new byte[a.Length * 2]; for (int i = 0; i < a.Length; i++) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(t.AsSpan(i * 2), a[i]); h.AppendData(t); }
        void Pts(PointM[] p) { L(p.Length); foreach (var q in p) { L(q.X); L(q.Y); } }

        L((long)Seed); L(CellX); L(CellY);
        Ints(HeightCm); Shorts(SlopePermille);
        foreach (var b in Biomes) h.AppendData([(byte)b]);
        h.AppendData(Fertility); h.AppendData(Forest); h.AppendData(Drainage); h.AppendData(Water); UShorts(LakeMap);
        Ints(Catchment); Ints(FlowTo); h.AppendData(Road); h.AppendData(Built); h.AppendData(RealmMap); UShorts(FiefMap); Ints(ParcelMap); h.AppendData(Mineral);
        foreach (var w in Waterways)
        {
            L(w.Id); L((long)w.Kind); Pts(w.Points); foreach (var v in w.WidthCm) L(v); foreach (var v in w.DepthCm) L(v);
            L(w.Tiles.Length); foreach (var t in w.Tiles) L(t); L((long)w.End); L(w.EndRef); L(w.LengthM); L(w.CatchmentTenthsKm2);
        }
        foreach (var l in Lakes) { L(l.Id); L(l.Tiles); L(l.SurfaceCm); L(l.MaxDepthCm); L(l.Center.X); L(l.Center.Y); L(l.MinTileX); L(l.MinTileY); L(l.MaxTileX); L(l.MaxTileY); }
        foreach (var g in Gates) { L((long)g.Side); L(g.Tile); L((long)g.Class); L(g.RouteId); L(g.BearingX); L(g.BearingY); }
        foreach (var n in RoadNodes) { L(n.Id); L((long)n.Kind); L(n.Settlement); L(n.X); L(n.Y); }
        foreach (var e in RoadEdges)
        {
            L(e.Id); L(e.A); L(e.B); L((long)e.Class); L(e.WidthCm); Pts(e.Points); L(e.LengthM);
            foreach (var c in e.Crossings) { L(c.At.X); L(c.At.Y); L((long)c.Kind); L(c.WaterwayId); L(c.WidthCm); }
        }
        foreach (var s in Settlements)
        {
            Id(s.Id); S(s.Name); L((long)s.Class); L(s.TileX); L(s.TileY); L(s.CenterX); L(s.CenterY); L(s.TargetPop); L(s.Households);
            Id(s.HistoryId); L(s.Realm); L(s.FiefIndex); L(s.Culture); L(s.NearWaterway); L(s.OnRiver ? 1 : 0); L(s.FieldAcresDemand); L(s.FieldAcres); L(s.RoadNode);
            var p = s.Plan;
            L(p.Horizontal ? 1 : 0); L(p.Square.X); L(p.Square.Y); L(p.Square.W); L(p.Square.H); L(p.RadiusM); L(p.ChurchLot); L(p.ManorLot); L(p.MillLot);
            foreach (var st in p.Streets) { L(st.A.X); L(st.A.Y); L(st.B.X); L(st.B.Y); L(st.WidthM); }
            foreach (var lot in p.Lots) { L(lot.Index); L((long)lot.Use); L((long)lot.Dwelling); S(lot.BlueprintId); L(lot.Rect.X); L(lot.Rect.Y); L(lot.Rect.W); L(lot.Rect.H); L(lot.Facing); L(lot.Wealth); L(lot.Vacant ? 1 : 0); }
            foreach (var q in s.Parcels) L(q);
        }
        foreach (var f in Fiefs) { Id(f.Id); S(f.Name); L(f.Realm); L(f.SeatSettlement); L(f.ManorLot); foreach (var q in f.Settlements) L(q); L(f.Acres); L(f.Households); L(f.Pop); }
        foreach (var p in Parcels) { Id(p.Id); L(p.Settlement); L((long)p.Owner.Kind); L(p.Owner.FiefIndex); L(p.Owner.LotIndex); L(p.Slot); L(p.FieldStart); L(p.FieldCount); L(p.AvgFertility); L(p.TileX); L(p.TileY); }
        foreach (var f in Fields) { Id(f.Id); L(f.Tile); L(f.Parcel); L(f.Fertility); L(f.Slot); L((long)f.Crop); }
        foreach (var s in Sites)
        {
            Id(s.Id); L((long)s.Kind); S(s.SubKind); S(s.Name); L(s.TileX); L(s.TileY); L(s.X); L(s.Y); Id(s.HistoryEvent); Id(s.CauseEvent); Id(s.HistorySettlement); Id(s.Artefact);
            L(s.Levels); L((long)s.Mineral); L(s.Tonnage); L((long)s.Mine); L(s.Dead); L(s.Year); L(s.MonsterLair ? 1 : 0); S(s.LairKind);
        }
        foreach (var v in Veins) { L((long)v.Type); L(v.TileX); L(v.TileY); L(v.RadiusTiles); L(v.Tonnage); L(v.ContinentCell); }
        foreach (var r in Realms) { L(r.HistoryIndex); S(r.Name); L(r.Tiles); }
        foreach (var w in ActiveWars) { L(w.WarIndex); S(w.Name); L(w.Cell); L(w.X); L(w.Y); L(w.DistanceM); }
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
