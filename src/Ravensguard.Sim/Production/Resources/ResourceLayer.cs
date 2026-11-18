using System.Security.Cryptography;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Production;

/// <summary>Resource kinds a tile can hold (resources spec 2.1; Salt is added for sea pans).</summary>
public enum ResourceKind : byte { Fish, Wood, Game, Peat, Forage, Pasture, Clay, Sand, Stone, Salt }

/// <summary>
/// Physical stocks of a region, one row per (tile, kind) that can hold stock, sorted by tile then kind (resources spec 2.1). Stocks are grams (wood: grams of
/// logs; fish and forage and peat: grams; game: micro-animals, 1,000,000 per animal; pasture: milli-acres of grazing). Everything is derived from the region layers by a pure
/// function, so two builds in any order are identical (G-R13). Work removes stock through <see cref="Take"/>; seasons regrow it.
/// </summary>
public sealed class ResourceLayer
{
    private int[] _tile = new int[1024];
    private ResourceKind[] _kind = new ResourceKind[1024];
    private long[] _stock = new long[1024], _cap = new long[1024];
    private short[] _lastDay = new short[1024];
    public int Count { get; private set; }

    public int TileOf(int row) => _tile[row];
    public ResourceKind KindOf(int row) => _kind[row];
    public long StockG(int row) => _stock[row];
    public long CapG(int row) => _cap[row];
    public int LastDay(int row) => _lastDay[row];

    private static long Key(int tile, ResourceKind k) => ((long)tile << 4) | (long)k;
    private long KeyAt(int row) => Key(_tile[row], _kind[row]);

    /// <summary>Appends a row; callers add rows in ascending (tile, kind) order.</summary>
    public int Add(int tile, ResourceKind kind, long stock, long cap)
    {
        if (Count > 0 && Key(tile, kind) <= KeyAt(Count - 1)) throw new InvalidOperationException("resource rows must be added in ascending (tile, kind) order");
        if (Count == _tile.Length)
        {
            int n = Count * 2;
            Array.Resize(ref _tile, n); Array.Resize(ref _kind, n); Array.Resize(ref _stock, n); Array.Resize(ref _cap, n); Array.Resize(ref _lastDay, n);
        }
        _tile[Count] = tile; _kind[Count] = kind; _stock[Count] = Math.Clamp(stock, 0, cap); _cap[Count] = cap;
        return Count++;
    }

    /// <summary>Row of (tile, kind) or -1.</summary>
    public int Find(int tile, ResourceKind kind)
    {
        long k = Key(tile, kind); int lo = 0, hi = Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1; long v = KeyAt(mid);
            if (v == k) return mid;
            if (v < k) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }

    /// <summary>Removes up to <paramref name="g"/> from a row (never below zero); returns what was taken.</summary>
    public long Take(int row, long g, int day)
    {
        long t = Math.Min(Math.Max(0, g), _stock[row]);
        _stock[row] -= t; _lastDay[row] = (short)day;
        return t;
    }

    /// <summary>Adds up to <paramref name="g"/> to a row, never above cap; returns what was added.</summary>
    public long Add(int row, long g)
    {
        long a = Math.Min(Math.Max(0, g), _cap[row] - _stock[row]);
        _stock[row] += a; return a;
    }

    public void SetStock(int row, long g) => _stock[row] = Math.Clamp(g, 0, _cap[row]);

    /// <summary>Sum of stock and of cap over all rows of a kind.</summary>
    public (long Stock, long Cap) Totals(ResourceKind kind)
    {
        long s = 0, c = 0;
        for (int i = 0; i < Count; i++) if (_kind[i] == kind) { s += _stock[i]; c += _cap[i]; }
        return (s, c);
    }

    // ---------- derivation ----------

    /// <summary>Builds the layer from the region (resources spec 2.1). Pure in the region and the seed; <paramref name="settlementCentres"/> feeds the pasture rule.</summary>
    public static ResourceLayer Build(RegionData r, ResourceSettingsDef cfg, ulong seed)
    {
        var layer = new ResourceLayer();
        var pasture = PastureTiles(r);
        var veinStone = new HashSet<int>(); var veinPeat = new HashSet<int>();
        for (int t = 0; t < RegionData.Tiles; t++)
        {
            int m = r.Mineral[t];
            if (m == (int)MineralType.Stone + 1) veinStone.Add(t); else if (m == (int)MineralType.Peat + 1) veinPeat.Add(t);
        }
        long woodPerPoint = (long)cfg.WoodKgPerM3 * 1000;                        // grams of logs per forest point (1 m3 per point per acre)
        for (int t = 0; t < RegionData.Tiles; t++)
        {
            byte w = r.Water[t]; var biome = r.Biomes[t];
            bool field = r.ParcelMap[t] != 0, built = r.Built[t] != 0;
            // kinds in ascending enum order so rows come out sorted
            if ((w & (WaterFlags.Lake | WaterFlags.River | WaterFlags.Sea)) != 0)
            {
                long cap = (w & WaterFlags.Lake) != 0 ? 150_000 : (w & WaterFlags.Sea) != 0 ? 200_000 : 40_000;
                int pct = 70 + (int)(DetRng.Next(seed, 0, (ulong)t, 0x240, 1) % 31);
                layer.Add(t, ResourceKind.Fish, cap * pct / 100, cap);
            }
            bool wood = r.Forest[t] >= 30 && !field && !built && (w & (WaterFlags.Lake | WaterFlags.Sea)) == 0;
            if (wood) { long cap = r.Forest[t] * woodPerPoint; layer.Add(t, ResourceKind.Wood, cap * 90 / 100, cap); }
            if (wood)
            {
                long densityMilli = biome switch { Biome.Taiga => 100_000, Biome.TemperateForest => 80_000, _ => 50_000 };
                layer.Add(t, ResourceKind.Game, densityMilli, densityMilli);
            }
            if (biome == Biome.Marsh || veinPeat.Contains(t)) layer.Add(t, ResourceKind.Peat, 150_000_000, 150_000_000);
            if (wood || biome == Biome.Marsh) layer.Add(t, ResourceKind.Forage, 1600, 1600);
            if (pasture.Contains(t)) layer.Add(t, ResourceKind.Pasture, 1000, 1000);
            if (biome == Biome.RiverValley && !field && !built && (w & WaterFlags.River) == 0) layer.Add(t, ResourceKind.Clay, 5_000_000_000, 5_000_000_000);
            if ((w & WaterFlags.River) != 0) layer.Add(t, ResourceKind.Sand, 5_000_000_000, 5_000_000_000);
            if (veinStone.Contains(t)) layer.Add(t, ResourceKind.Stone, 5_000_000_000, 5_000_000_000);
            if ((w & WaterFlags.Sea) != 0) layer.Add(t, ResourceKind.Salt, 5_000_000_000, 5_000_000_000);
        }
        return layer;
    }

    /// <summary>
    /// Pasture and meadow tiles (resources spec 2.1, decision R3): grass or valley tiles that are neither field nor built, within 2.5 km of a settlement, fertility
    /// at least 40. A pure function of the region.
    /// </summary>
    public static HashSet<int> PastureTiles(RegionData r)
    {
        var set = new HashSet<int>();
        int radius = 2500 / RegionData.TileM;
        foreach (var s in r.Settlements)
        {
            int cx = s.CenterX / RegionData.TileM, cy = s.CenterY / RegionData.TileM;
            for (int y = Math.Max(0, cy - radius); y <= Math.Min(RegionData.Size - 1, cy + radius); y++)
                for (int x = Math.Max(0, cx - radius); x <= Math.Min(RegionData.Size - 1, cx + radius); x++)
                {
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy > radius * radius) continue;
                    int t = RegionData.Index(x, y);
                    var b = r.Biomes[t];
                    if (b is not (Biome.Grassland or Biome.RiverValley or Biome.Steppe)) continue;
                    if (r.ParcelMap[t] != 0 || r.Built[t] != 0 || r.Fertility[t] < 40 || (r.Water[t] & (WaterFlags.Lake | WaterFlags.Sea | WaterFlags.River)) != 0) continue;
                    set.Add(t);
                }
        }
        return set;
    }

    // ---------- regrowth (S1 and S3) ----------

    /// <summary>
    /// Seasonal regrowth (resources spec 3.3), integer and floor: wood +143,836 g per acre while below cap, fish +25% of the deficit (x0.4 in winter), game +1.2% of stock
    /// per season, peat 12.5 kg, and the annual forage and pasture refill on day 1 of the year.
    /// </summary>
    public void Regrow(GameTime now, ResourceSettingsDef cfg)
    {
        var season = now.Season; bool yearStart = now.DayOfYear == 1;
        for (int i = 0; i < Count; i++)
        {
            long stock = _stock[i], cap = _cap[i];
            switch (_kind[i])
            {
                case ResourceKind.Wood: if (stock < cap) _stock[i] = Math.Min(cap, stock + cfg.WoodRegrowGPerAcreSeason); break;
                case ResourceKind.Fish:
                {
                    long deficit = cap - stock;
                    long add = season == Season.Winter ? deficit * cfg.FishRegrowPct * cfg.FishWinterRegrowPermille / 100_000 : deficit * cfg.FishRegrowPct / 100;
                    _stock[i] = Math.Min(cap, stock + add); break;
                }
                case ResourceKind.Game: _stock[i] = Math.Min(cap, stock + stock * cfg.GameGrowthPctYear * TimeConst.DaysPerYear * 100 / (100L * 36500 * 4)); break;
                case ResourceKind.Peat: _stock[i] = Math.Min(cap, stock + cfg.PeatRegrowGPerAcreYear / 4); break;
                case ResourceKind.Forage: case ResourceKind.Pasture: if (yearStart) _stock[i] = cap; break;
            }
        }
    }

    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        L(Count);
        for (int i = 0; i < Count; i++) { L(_tile[i]); L((long)_kind[i]); L(_stock[i]); L(_cap[i]); }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    // ---------- save support ----------

    public (int[] Tile, byte[] Kind, long[] Stock, long[] Cap, short[] LastDay) Export()
    {
        var t = new int[Count]; var k = new byte[Count]; var s = new long[Count]; var c = new long[Count]; var d = new short[Count];
        Array.Copy(_tile, t, Count); for (int i = 0; i < Count; i++) k[i] = (byte)_kind[i];
        Array.Copy(_stock, s, Count); Array.Copy(_cap, c, Count); Array.Copy(_lastDay, d, Count);
        return (t, k, s, c, d);
    }

    public static ResourceLayer Import(int[] tile, byte[] kind, long[] stock, long[] cap, short[] lastDay)
    {
        var l = new ResourceLayer();
        for (int i = 0; i < tile.Length; i++) { int row = l.Add(tile[i], (ResourceKind)kind[i], stock[i], cap[i]); l._stock[row] = stock[i]; l._lastDay[row] = lastDay[i]; }
        return l;
    }
}
