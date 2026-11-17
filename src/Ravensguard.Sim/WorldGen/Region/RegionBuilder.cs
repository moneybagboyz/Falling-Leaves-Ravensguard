using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.WorldGen.Region;

/// <summary>One-shot builder holding the working arrays for a region; see <see cref="RegionGenerator"/>.</summary>
internal sealed partial class RegionBuilder
{
    internal const int N = Region.Size;
    internal const int Tiles = N * N;
    internal const int TileM = Region.TileM;
    /// <summary>Margin of extra tiles (one continental cell) around the region so upstream terrain and rivers feed in consistently.</summary>
    internal const int Mg = 32;
    internal const int E = N + 2 * Mg;

    private readonly Continent _c;
    private readonly WorldHistory _h;
    private readonly RegionSettings _s;
    private readonly int _cx, _cy;
    private readonly Region _r;
    private readonly ulong _seed;
    private readonly int _ox, _oy;
    private readonly IdAllocator _ids = new();

    public RegionBuilder(Continent c, WorldHistory h, int cx, int cy, RegionSettings s)
    {
        _c = c; _h = h; _s = s; _cx = cx; _cy = cy;
        _seed = DetRng.Mix(s.Seed ^ 0x5265676F6E21UL);
        _ox = cx * RegionSettings.CellM; _oy = cy * RegionSettings.CellM;
        _r = new Region { Seed = s.Seed, CellX = cx, CellY = cy };
        // History ids come from their own allocator, so new settlements start after the highest history id.
        var st = _ids.GetState();
        ulong maxS = 0;
        foreach (var hs in h.Settlements) maxS = Math.Max(maxS, hs.Id.Counter);
        st[(int)IdKind.Settlement] = maxS + 1;
        _ids.SetState(st);
    }

    public Region Build()
    {
        BuildTerrain();
        BuildHydrology();
        BuildAttributes();
        PlanPopulation();
        PlaceSettlements();
        AssignFiefs();
        BuildPlans();
        BuildRoads();
        BuildFields();
        BuildSites();
        BuildRealmsAndWars();
        return _r;
    }

    // ---------- helpers ----------
    internal static int Idx(int x, int y) => y * N + x;
    internal static int Cx(int tile) => tile % N;
    internal static int Cy(int tile) => tile / N;
    internal static int TileCenter(int t) => t * TileM + TileM / 2;
    internal static int TileOf(int metres) => Math.Clamp(metres / TileM, 0, N - 1);
    internal static readonly int[] Dx8 = [1, 1, 0, -1, -1, -1, 0, 1];
    internal static readonly int[] Dy8 = [0, 1, 1, 1, 0, -1, -1, -1];

    internal static int FloorDiv(int a, int b) { int q = a / b; return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q; }

    /// <summary>Deterministic hash draw for a (stream, a, b) key.</summary>
    internal ulong Hash(uint stream, long a, long b = 0) => DetRng.Next(_seed, a, (ulong)b, stream, 0);
    internal int HashRange(uint stream, long a, long b, int lo, int hi) => DetRng.Range(_seed, a, (ulong)b, stream, 0, lo, hi);

    internal static long Dist2(int ax, int ay, int bx, int by) { long dx = ax - bx, dy = ay - by; return dx * dx + dy * dy; }
    internal static int Dist(int ax, int ay, int bx, int by) => (int)Region.Isqrt(Dist2(ax, ay, bx, by));

    // Streams for hashing (kept distinct per purpose).
    internal const uint StVillage = 11, StFarm = 12, StPop = 13, StNames = 14, StLot = 15, StPlan = 16, StField = 17, StSite = 18, StFief = 19, StRoad = 20, StVein = 21;
}
