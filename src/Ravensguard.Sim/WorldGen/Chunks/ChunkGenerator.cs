using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.WorldGen.Chunks;

/// <summary>The output of generating one chunk: every level that has content, and a hash of that content.</summary>
public sealed class GeneratedChunk
{
    public int ChunkX { get; init; }
    public int ChunkY { get; init; }
    public SortedDictionary<int, ChunkContent> Levels { get; } = [];
    /// <summary>SHA-256 (hex, 32 chars) over every level's tiles and objects in key order.</summary>
    public string ContentHash { get; set; } = "";
}

/// <summary>
/// Plan 4A.4 "split between world generation and chunks": turns the region (tile layers, polylines, settlement plans, parcels) plus the
/// population into the cells of one 32 x 32 m chunk, on first visit. Rasterises streets, roads, rivers and lakes, field furrows and hedges,
/// places each lot's building from its blueprint (clipped per chunk, so a house across a seam is whole), scatters trees, bushes and rocks,
/// and marks site entrances. Every result is a pure function of (region seed, chunk id, stream): the order chunks are visited in never matters.
/// Thread-safe: only internal caches are shared.
/// </summary>
public sealed partial class ChunkGenerator
{
    /// <summary>Bump when the output for the same region changes; snapshots keep the version they were made with (plan 2.2).</summary>
    public const int Version = 1;
    public const int Size = ChunkContent.Size;
    public const int ChunksPerSide = RegionData.Meters / Size;
    private const int Pad = 4;
    private const int B = Size + 2 * Pad;

    public RegionData Region { get; }
    public Population Population { get; }
    public BuildingLibrary Library { get; }
    public PiecePalette Palette { get; }
    /// <summary>Lowest and highest building level the generator can emit (from the blueprints).</summary>
    public int MinZ { get; }
    public int MaxZ { get; }

    private readonly ulong _seed;
    private readonly RoomDetector _detector;
    private readonly Dictionary<long, List<int>> _settlementsAt = [];
    private readonly Dictionary<long, List<(int S, int Lot)>> _lotsAt = [];
    private readonly Dictionary<long, List<int>> _sitesAt = [];
    private readonly SegIndex _roads = new(), _waters = new();
    private readonly Dictionary<long, List<(PointM At, CrossingKind Kind)>> _crossAt = [];
    private readonly byte[] _crop;
    private readonly string[] _settlementStyle;
    private readonly ConcurrentDictionary<long, Stamp?> _stamps = new();
    private readonly ConcurrentDictionary<(string, string, int), BuildingGrid?> _grids = new();
    private readonly ConcurrentDictionary<int, List<FixedProp>> _fixed = new();

    private static readonly ConditionalWeakTable<RegionData, ChunkGenerator> Cache = new();

    public ChunkGenerator(RegionData region, Population population, ContentDatabase content)
    {
        Region = region; Population = population;
        Library = BuildingLibrary.FromDatabase(content);
        Palette = new PiecePalette(Library.Pieces);
        _detector = Library.Detector;
        _seed = DetRng.Mix(region.Seed ^ 0x43484E4B47454EUL);
        int minZ = 0, maxZ = 0;
        foreach (var bp in Library.Blueprints)
        {
            if (bp.Def.Kind != BlueprintKind.Building) continue;
            foreach (var l in bp.Def.Levels) { minZ = Math.Min(minZ, l.Z); maxZ = Math.Max(maxZ, l.Z); }
        }
        MinZ = Math.Max(minZ, ChunkKey.MinZ); MaxZ = Math.Min(maxZ, ChunkKey.MaxZ);

        _crop = new byte[RegionData.Tiles];
        foreach (var f in region.Fields) _crop[f.Tile] = (byte)((int)f.Crop + 1);
        _settlementStyle = new string[region.Settlements.Count];
        for (int s = 0; s < region.Settlements.Count; s++) _settlementStyle[s] = StyleFor(region.Settlements[s]);

        BuildIndices();
    }

    /// <summary>The generator for a region, built once and shared (it only holds caches). Rebuilt if the content or population differ.</summary>
    public static ChunkGenerator For(RegionData region, Population population, ContentDatabase content)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(region, out var g) && ReferenceEquals(g.Population, population)) return g;
            g = new ChunkGenerator(region, population, content);
            Cache.AddOrUpdate(region, g);
            return g;
        }
    }

    /// <summary>The ground level (z = 0) of a chunk on its first visit.</summary>
    public static ChunkContent Generate(RegionData region, Population population, ContentDatabase content, int chunkX, int chunkY)
        => For(region, population, content).Generate(chunkX, chunkY);

    public ChunkContent Generate(int chunkX, int chunkY) => GenerateLevels(chunkX, chunkY).Levels[0];

    // ------------------------------------------------------------------ determinism helpers

    internal const uint StTree = 1, StBush = 2, StRock = 3, StBase = 4, StHedge = 5, StLot = 6, StFixed = 7, StSite = 8, StWarp = 9, StYard = 10, StFurnish = 11;

    /// <summary>64 random bits keyed by (seed, stream, world cell): the same no matter which chunk asks.</summary>
    internal ulong H(uint stream, long x, long y) => DetRng.Next(_seed, (x << 32) ^ (y & 0xFFFFFFFFL), 0, stream, 0);

    internal static long Key(int x, int y) => ((long)x << 32) | (uint)y;

    // ------------------------------------------------------------------ indices

    private static void AddTo<T>(Dictionary<long, List<T>> d, long k, T v)
    {
        if (!d.TryGetValue(k, out var l)) d[k] = l = [];
        l.Add(v);
    }

    private static int ChunkOf(int m) => Math.Clamp(Math.DivRem(m, Size, out int r) - (r < 0 ? 1 : 0), 0, ChunksPerSide - 1);

    private void BuildIndices()
    {
        var r = Region;
        for (int s = 0; s < r.Settlements.Count; s++)
        {
            var p = r.Settlements[s].Plan;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            void Cover(RectM q) { x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.Right); y1 = Math.Max(y1, q.Bottom); }
            for (int i = 0; i < p.Lots.Count; i++)
            {
                var lot = p.Lots[i];
                Cover(lot.Rect);
                int lx0 = ChunkOf(lot.Rect.X - 2), lx1 = ChunkOf(lot.Rect.Right + 2), ly0 = ChunkOf(lot.Rect.Y - 2), ly1 = ChunkOf(lot.Rect.Bottom + 2);
                for (int cy = ly0; cy <= ly1; cy++) for (int cx = lx0; cx <= lx1; cx++) AddTo(_lotsAt, Key(cx, cy), (s, i));
            }
            if (p.Square.W > 0) Cover(p.Square);
            foreach (var st in p.Streets) Cover(new RectM(Math.Min(st.A.X, st.B.X) - st.WidthM, Math.Min(st.A.Y, st.B.Y) - st.WidthM, Math.Abs(st.A.X - st.B.X) + 2 * st.WidthM, Math.Abs(st.A.Y - st.B.Y) + 2 * st.WidthM));
            if (x0 == int.MaxValue) continue;
            int bx0 = ChunkOf(x0 - 4), bx1 = ChunkOf(x1 + 4), by0 = ChunkOf(y0 - 4), by1 = ChunkOf(y1 + 4);
            for (int cy = by0; cy <= by1; cy++) for (int cx = bx0; cx <= bx1; cx++) AddTo(_settlementsAt, Key(cx, cy), s);
        }
        foreach (var e in r.RoadEdges)
        {
            for (int i = 0; i + 1 < e.Points.Length; i++)
                _roads.Add(e.Id, e.Points[i], e.Points[i + 1], e.WidthCm, e.WidthCm, (byte)e.Class);
            foreach (var c in e.Crossings) AddTo(_crossAt, Key(c.At.X / 256, c.At.Y / 256), (c.At, c.Kind));
        }
        foreach (var w in r.Waterways)
            for (int i = 0; i + 1 < w.Points.Length; i++)
                _waters.Add(w.Id, w.Points[i], w.Points[i + 1], w.WidthCm[i], w.WidthCm[i + 1], (byte)w.Kind);
        for (int i = 0; i < r.Sites.Count; i++) AddTo(_sitesAt, Key(ChunkOf(r.Sites[i].X), ChunkOf(r.Sites[i].Y)), i);
    }

    /// <summary>Building style id of a settlement: the culture's style where the content names one, else by landscape (hill stone, northern logs, clay brick, coastal planks).</summary>
    private string StyleFor(RegionSettlement s)
    {
        var b = Region.Biomes[Math.Clamp(s.TileY * RegionData.Size + s.TileX, 0, RegionData.Tiles - 1)];
        string id = b switch
        {
            Biome.Mountain => "hillfolk",
            Biome.Taiga or Biome.Tundra => "northern",
            Biome.TemperateForest => "northern",
            Biome.RiverValley or Biome.Marsh => "vale_brick",
            Biome.Steppe => "common",
            _ => "common",
        };
        if (s.Class == SettlementClass.Town && b == Biome.RiverValley) id = "vale_brick";
        return Library.Styles.ContainsKey(id) ? id : Library.Styles.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault() ?? "common";
    }

    // ------------------------------------------------------------------ polyline index

    /// <summary>Polyline segments (region metres, widths in cm) bucketed on a 128 m grid so a chunk finds the few that touch it.</summary>
    private sealed class SegIndex
    {
        public const int BucketM = 128;
        public struct Seg { public int Owner; public long Ax, Ay, Bx, By; public int Wa, Wb; public byte Class; public int MaxHalfM; }
        public readonly List<Seg> Segs = [];
        private readonly Dictionary<long, List<int>> _buckets = [];

        public void Add(int owner, PointM a, PointM b, int wa, int wb, byte cls)
        {
            int half = Math.Max(wa, wb) / 200 + 3;
            var s = new Seg { Owner = owner, Ax = a.X * 100L, Ay = a.Y * 100L, Bx = b.X * 100L, By = b.Y * 100L, Wa = wa, Wb = wb, Class = cls, MaxHalfM = half };
            int id = Segs.Count; Segs.Add(s);
            int x0 = (Math.Min(a.X, b.X) - half) / BucketM, x1 = (Math.Max(a.X, b.X) + half) / BucketM;
            int y0 = (Math.Min(a.Y, b.Y) - half) / BucketM, y1 = (Math.Max(a.Y, b.Y) + half) / BucketM;
            for (int y = Math.Max(0, y0); y <= y1; y++) for (int x = Math.Max(0, x0); x <= x1; x++) AddTo(_buckets, Key(x, y), id);
        }

        /// <summary>Segment ids whose buckets touch the metre rectangle, each once, ascending.</summary>
        public void Query(int x0, int y0, int x1, int y1, List<int> into)
        {
            into.Clear();
            for (int by = Math.Max(0, y0 / BucketM); by <= Math.Max(0, y1) / BucketM; by++)
                for (int bx = Math.Max(0, x0 / BucketM); bx <= Math.Max(0, x1) / BucketM; bx++)
                    if (_buckets.TryGetValue(Key(bx, by), out var l)) into.AddRange(l);
            into.Sort();
            int w = 0;
            for (int i = 0; i < into.Count; i++) if (i == 0 || into[i] != into[i - 1]) into[w++] = into[i];
            into.RemoveRange(w, into.Count - w);
        }
    }

    /// <summary>Squared distance (cm^2) from a point to a segment and the squared half-width there, all integer.</summary>
    private static void Dist(in SegIndex.Seg s, long px, long py, out long d2, out long half2, out long half)
    {
        long dx = s.Bx - s.Ax, dy = s.By - s.Ay, len2 = dx * dx + dy * dy;
        long tn = len2 == 0 ? 0 : Math.Clamp((px - s.Ax) * dx + (py - s.Ay) * dy, 0, len2);
        long cx = len2 == 0 ? s.Ax : s.Ax + dx * tn / len2, cy = len2 == 0 ? s.Ay : s.Ay + dy * tn / len2;
        long w = len2 == 0 ? s.Wa : s.Wa + (long)(s.Wb - s.Wa) * tn / len2;
        long ex = px - cx, ey = py - cy;
        d2 = ex * ex + ey * ey; half = w / 2; half2 = half * half;
    }

    // ------------------------------------------------------------------ the chunk

    /// <summary>All levels of a chunk (ground level plus building storeys and cellars), deterministic for (region, population, content, x, y).</summary>
    public GeneratedChunk GenerateLevels(int chunkX, int chunkY)
    {
        var res = new GeneratedChunk { ChunkX = chunkX, ChunkY = chunkY };
        var buf = new Buf(chunkX * Size - Pad, chunkY * Size - Pad);
        if ((uint)chunkX >= ChunksPerSide || (uint)chunkY >= ChunksPerSide)
        {
            var off = new ChunkContent();
            for (int i = 0; i < Size * Size; i++) off.Tiles[i] = TileCode.Make(GroundKind.DeepWater, 0, 0, 0);
            res.Levels[0] = off;
            res.ContentHash = HashOf(res);
            return res;
        }
        PaintBase(buf);
        PaintFields(buf);
        PaintWater(buf);
        PaintSettlements(buf);
        PaintRoads(buf);
        PlaceBuildings(buf);
        var props = new List<PendingProp>();
        CollectProps(buf, props);
        EmitLevels(buf, props, res);
        res.ContentHash = HashOf(res);
        return res;
    }

    private static string HashOf(GeneratedChunk g)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b, v); h.AppendData(b); }
        foreach (var (z, c) in g.Levels)
        {
            L(z);
            foreach (var (i, t) in c.Tiles) { L(i); L(t); }
            foreach (var (id, o) in c.Objects) { L((long)id); h.AppendData(o.Data); }
        }
        return Convert.ToHexString(h.GetHashAndReset())[..32];
    }

    /// <summary>Working arrays for one chunk plus a 4 m apron, so neighbour checks (a tree beside a road) agree across seams.</summary>
    private sealed class Buf
    {
        public const byte Road = 1, Water = 2, Build = 4, Yard = 8, Field = 16, Prop = 32, Paved = 64, Deep = 128;
        public readonly int X0, Y0;
        public readonly GroundKind[] Ground = new GroundKind[B * B];
        public readonly byte[] Variant = new byte[B * B];
        public readonly byte[] Flags = new byte[B * B];
        public readonly int[] Structure = new int[B * B], Fixture = new int[B * B];
        /// <summary>Building storeys other than the ground floor, chunk-local 32 x 32 (palette numbers).</summary>
        public readonly SortedDictionary<int, (int[] Structure, int[] Fixture)> Upper = [];
        public readonly List<Stamp> Stamps = [];
        public Buf(int x0, int y0) { X0 = x0; Y0 = y0; }
        public int I(int wx, int wy) => (wy - Y0) * B + (wx - X0);
        public bool In(int wx, int wy) => (uint)(wx - X0) < B && (uint)(wy - Y0) < B;
        public bool InChunk(int wx, int wy) => (uint)(wx - X0 - Pad) < Size && (uint)(wy - Y0 - Pad) < Size;
    }
}
