using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.WorldGen.Chunks;

public sealed partial class ChunkGenerator
{
    private const int N = RegionData.Size;

    private static int FloorDiv(int a, int b) { int q = a / b; return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q; }

    /// <summary>Value noise 0..65535 at a world cell, wavelength in metres.</summary>
    private static int Noise(ulong seed, int wx, int wy, int wavelength, int octaves)
        => DetNoise.Fbm(seed, Fixed.FromRatio(wx, wavelength), Fixed.FromRatio(wy, wavelength), octaves).Raw;

    /// <summary>Bilinear sample of a 64 m tile layer at a world cell, in 1/64 units (a layer value of 100 reads 6400).</summary>
    private static int Bilinear(byte[] a, int wx, int wy)
    {
        int fx = wx - 32, fy = wy - 32;
        int tx = FloorDiv(fx, 64), ty = FloorDiv(fy, 64), ax = fx - tx * 64, ay = fy - ty * 64;
        int x0 = Math.Clamp(tx, 0, N - 1), x1 = Math.Clamp(tx + 1, 0, N - 1), y0 = Math.Clamp(ty, 0, N - 1), y1 = Math.Clamp(ty + 1, 0, N - 1);
        int v = a[y0 * N + x0] * (64 - ax) * (64 - ay) + a[y0 * N + x1] * ax * (64 - ay) + a[y1 * N + x0] * (64 - ax) * ay + a[y1 * N + x1] * ax * ay;
        return v >> 6;
    }

    private static bool Outside(int wx, int wy) => (uint)wx >= RegionData.Meters || (uint)wy >= RegionData.Meters;

    // ------------------------------------------------------------------ ground from biome, soil and forest

    private void PaintBase(Buf b)
    {
        var r = Region;
        ulong nWarp = DetRng.Mix(_seed ^ StWarp), nPatch = DetRng.Mix(_seed ^ StBase), nWarp2 = DetRng.Mix(_seed ^ (StWarp + 100));
        for (int j = 0; j < B; j++)
        {
            int wy = b.Y0 + j;
            for (int i = 0; i < B; i++)
            {
                int wx = b.X0 + i, idx = j * B + i;
                if (Outside(wx, wy)) { b.Ground[idx] = GroundKind.DeepWater; b.Flags[idx] = Buf.Water | Buf.Deep; continue; }
                int ox = (Noise(nWarp, wx, wy, 23, 2) - 32768) * 30 >> 15, oy = (Noise(nWarp2, wx, wy, 23, 2) - 32768) * 30 >> 15;
                int tx = Math.Clamp((wx + ox) >> 6, 0, N - 1), ty = Math.Clamp((wy + oy) >> 6, 0, N - 1), t = ty * N + tx;
                int n = Noise(nPatch, wx, wy, 11, 2) >> 8;  // 0..255
                int forest = r.Forest[t], fert = r.Fertility[t], slope = r.SlopePermille[t];
                var g = GroundKind.Grass;
                switch (r.Biomes[t])
                {
                    case Biome.Grassland: case Biome.RiverValley:
                        g = n > 205 && fert > 40 ? GroundKind.Meadow : n < 14 && fert < 60 ? GroundKind.Dirt : GroundKind.Grass; break;
                    case Biome.Steppe:
                        g = n > 225 ? GroundKind.Gravel : n < 50 ? GroundKind.Meadow : n > 190 ? GroundKind.Dirt : GroundKind.Grass; break;
                    case Biome.TemperateForest:
                        g = forest >= 45 ? (n > 130 ? GroundKind.LeafLitter : GroundKind.ForestFloor)
                          : forest >= 18 ? (n > 150 ? GroundKind.LeafLitter : GroundKind.Grass)
                          : n > 215 ? GroundKind.Meadow : GroundKind.Grass; break;
                    case Biome.Taiga:
                        g = forest >= 35 ? (n > 170 ? GroundKind.Gravel : GroundKind.ForestFloor) : n > 200 ? GroundKind.Gravel : GroundKind.Grass; break;
                    case Biome.Tundra:
                        g = n > 175 ? GroundKind.Gravel : n < 40 ? GroundKind.Peat : GroundKind.Grass; break;
                    case Biome.Desert:
                        g = n > 215 ? GroundKind.Gravel : n < 45 ? GroundKind.Dirt : GroundKind.Sand; break;
                    case Biome.Marsh:
                        g = n > 150 ? GroundKind.Marsh : n > 85 ? GroundKind.Mud : n < 25 ? GroundKind.Peat : GroundKind.Grass; break;
                    case Biome.Mountain:
                        g = slope > 220 ? GroundKind.Scree : n > 150 ? GroundKind.Rock : n > 95 ? GroundKind.Gravel : GroundKind.Grass; break;
                    default:
                        g = n > 128 ? GroundKind.Sand : GroundKind.Mud; break; // sea or lake bed shows only where the shore noise dips
                }
                b.Ground[idx] = g;
                b.Variant[idx] = (byte)(H(StBase, wx, wy) & 7);
            }
        }
    }

    // ------------------------------------------------------------------ fields: strips of crop and furrow, headlands against neighbours

    private void PaintFields(Buf b)
    {
        var r = Region;
        int tx0 = Math.Max(0, (b.X0) >> 6), tx1 = Math.Min(N - 1, (b.X0 + B - 1) >> 6), ty0 = Math.Max(0, (b.Y0) >> 6), ty1 = Math.Min(N - 1, (b.Y0 + B - 1) >> 6);
        bool any = false;
        for (int ty = ty0; ty <= ty1 && !any; ty++) for (int tx = tx0; tx <= tx1; tx++) if (_crop[ty * N + tx] != 0) { any = true; break; }
        if (!any) return;
        for (int j = 0; j < B; j++)
        {
            int wy = b.Y0 + j;
            for (int i = 0; i < B; i++)
            {
                int wx = b.X0 + i;
                if (Outside(wx, wy)) continue;
                int tx = wx >> 6, ty = wy >> 6, t = ty * N + tx;
                int crop = _crop[t];
                if (crop == 0) continue;
                int parcel = r.ParcelMap[t], lx = wx & 63, ly = wy & 63;
                bool Diff(int ntx, int nty) => (uint)ntx >= N || (uint)nty >= N || r.ParcelMap[nty * N + ntx] != parcel;
                bool head = (lx < 2 && Diff(tx - 1, ty)) || (lx > 61 && Diff(tx + 1, ty)) || (ly < 2 && Diff(tx, ty - 1)) || (ly > 61 && Diff(tx, ty + 1));
                int idx = j * B + i;
                b.Flags[idx] |= Buf.Field;
                if (head) continue;
                bool horizontal = (H(StBase, parcel, 7) & 1) == 0;
                int row = ((horizontal ? wy : wx) + parcel * 7) % 3;
                var c = (Crop)(crop - 1);
                if (c == Crop.Fallow) { b.Ground[idx] = GroundKind.Fallow; b.Variant[idx] = (byte)(H(StBase, wx, wy) & 3); continue; }
                if (row == 0) { b.Ground[idx] = GroundKind.Furrow; b.Variant[idx] = (byte)(H(StBase, wx, wy) & 3); }
                else { b.Ground[idx] = c is Crop.Peas or Crop.Beans ? GroundKind.Sown : GroundKind.Crop; b.Variant[idx] = (byte)c; }
            }
        }
    }

    // ------------------------------------------------------------------ lakes, the sea, rivers and streams

    private void PaintWater(Buf b)
    {
        var r = Region;
        // lakes and sea: a bilinear field over the 64 m tiles, wobbled by noise so shores are not blocky
        int tx0 = Math.Max(0, (b.X0 - 64) >> 6), tx1 = Math.Min(N - 1, (b.X0 + B + 64) >> 6), ty0 = Math.Max(0, (b.Y0 - 64) >> 6), ty1 = Math.Min(N - 1, (b.Y0 + B + 64) >> 6);
        bool anyLake = false;
        for (int ty = ty0; ty <= ty1 && !anyLake; ty++) for (int tx = tx0; tx <= tx1; tx++) if ((r.Water[ty * N + tx] & (WaterFlags.Lake | WaterFlags.Sea)) != 0) { anyLake = true; break; }
        if (anyLake)
        {
            var field = LakeField();
            ulong nl = DetRng.Mix(_seed ^ 0x4C414B45);
            for (int j = 0; j < B; j++)
                for (int i = 0; i < B; i++)
                {
                    int wx = b.X0 + i, wy = b.Y0 + j;
                    if (Outside(wx, wy)) continue;
                    int v = Bilinear(field, wx, wy) * 255 / (100 * 64) + ((Noise(nl, wx, wy, 17, 2) - 32768) >> 9); // 0..255 +- 64
                    int idx = j * B + i;
                    if (v > 128)
                    {
                        b.Ground[idx] = v > 175 ? GroundKind.DeepWater : GroundKind.Water; b.Flags[idx] |= (byte)(Buf.Water | (v > 175 ? Buf.Deep : 0));
                        b.Variant[idx] = (byte)(H(StBase, wx, wy) & 3);
                    }
                    else if (v > 100 && b.Ground[idx] != GroundKind.Marsh) { b.Ground[idx] = v > 114 ? GroundKind.Mud : GroundKind.Sand; }
                }
        }
        // rivers and streams
        var segs = _segTmp.Value!;
        _waters.Query(b.X0, b.Y0, b.X0 + B, b.Y0 + B, segs);
        foreach (int si in segs)
        {
            var s = _waters.Segs[si];
            int bx0 = (int)(Math.Min(s.Ax, s.Bx) / 100) - s.MaxHalfM, bx1 = (int)(Math.Max(s.Ax, s.Bx) / 100) + s.MaxHalfM;
            int by0 = (int)(Math.Min(s.Ay, s.By) / 100) - s.MaxHalfM, by1 = (int)(Math.Max(s.Ay, s.By) / 100) + s.MaxHalfM;
            bool river = s.Class == (byte)WaterKind.River;
            for (int wy = Math.Max(by0, b.Y0); wy <= Math.Min(by1, b.Y0 + B - 1); wy++)
                for (int wx = Math.Max(bx0, b.X0); wx <= Math.Min(bx1, b.X0 + B - 1); wx++)
                {
                    Dist(s, wx * 100L + 50, wy * 100L + 50, out long d2, out long half2, out _);
                    if (d2 > half2) continue;
                    int idx = b.I(wx, wy);
                    bool deep = river && d2 * 10000 <= 3025 * half2;
                    if (deep || b.Ground[idx] != GroundKind.DeepWater) b.Ground[idx] = deep ? GroundKind.DeepWater : GroundKind.Water;
                    b.Flags[idx] |= (byte)(Buf.Water | (b.Ground[idx] == GroundKind.DeepWater ? Buf.Deep : 0));
                    b.Variant[idx] = (byte)(H(StBase, wx, wy) & 3);
                }
        }
    }

    private byte[]? _lakeField;
    /// <summary>Per tile 100 where a lake or the sea lies, else 0 (built once).</summary>
    private byte[] LakeField()
    {
        var f = _lakeField;
        if (f != null) return f;
        f = new byte[RegionData.Tiles];
        for (int i = 0; i < f.Length; i++) if ((Region.Water[i] & (WaterFlags.Lake | WaterFlags.Sea)) != 0) f[i] = 100;
        return _lakeField = f;
    }

    private static readonly ThreadLocal<List<int>> _segTmp = new(() => []);

    // ------------------------------------------------------------------ settlement paving: squares and streets

    private void PaintSettlements(Buf b)
    {
        if (!_settlementsAt.TryGetValue(Key(b.X0 + Pad >> 5, b.Y0 + Pad >> 5), out var list)) return;
        foreach (int si in list)
        {
            var s = Region.Settlements[si];
            var kind = s.Class == SettlementClass.Town ? GroundKind.Cobble : GroundKind.Dirt;
            var plan = s.Plan;
            if (plan.Square.W > 0) Pave(b, plan.Square, kind);
            foreach (var st in plan.Streets)
            {
                int w = Math.Max(2, st.WidthM);
                int x0 = Math.Min(st.A.X, st.B.X) - w / 2, y0 = Math.Min(st.A.Y, st.B.Y) - w / 2;
                Pave(b, new RectM(x0, y0, Math.Abs(st.A.X - st.B.X) + w, Math.Abs(st.A.Y - st.B.Y) + w), kind);
            }
        }
    }

    private static void Pave(Buf b, RectM q, GroundKind kind)
    {
        for (int wy = Math.Max(q.Y, b.Y0); wy < Math.Min(q.Bottom, b.Y0 + B); wy++)
            for (int wx = Math.Max(q.X, b.X0); wx < Math.Min(q.Right, b.X0 + B); wx++)
            {
                int idx = b.I(wx, wy);
                if ((b.Flags[idx] & Buf.Water) != 0) { b.Ground[idx] = GroundKind.Bridge; b.Flags[idx] &= unchecked((byte)~Buf.Deep); }
                else b.Ground[idx] = kind;
                b.Flags[idx] |= Buf.Paved | Buf.Road;
                b.Variant[idx] = (byte)((wx * 31 + wy * 17) & 3);
            }
    }

    // ------------------------------------------------------------------ roads: dirt, cobbled highways, fords and bridges where they meet water

    private void PaintRoads(Buf b)
    {
        var segs = _segTmp.Value!;
        _roads.Query(b.X0, b.Y0, b.X0 + B, b.Y0 + B, segs);
        foreach (int si in segs)
        {
            var s = _roads.Segs[si];
            int bx0 = (int)(Math.Min(s.Ax, s.Bx) / 100) - s.MaxHalfM, bx1 = (int)(Math.Max(s.Ax, s.Bx) / 100) + s.MaxHalfM;
            int by0 = (int)(Math.Min(s.Ay, s.By) / 100) - s.MaxHalfM, by1 = (int)(Math.Max(s.Ay, s.By) / 100) + s.MaxHalfM;
            var kind = s.Class == (byte)RoadClass.Highway ? GroundKind.Cobble : GroundKind.Dirt;
            for (int wy = Math.Max(by0, b.Y0); wy <= Math.Min(by1, b.Y0 + B - 1); wy++)
                for (int wx = Math.Max(bx0, b.X0); wx <= Math.Min(bx1, b.X0 + B - 1); wx++)
                {
                    Dist(s, wx * 100L + 50, wy * 100L + 50, out long d2, out long half2, out _);
                    if (d2 > half2) continue;
                    int idx = b.I(wx, wy);
                    if ((b.Flags[idx] & Buf.Water) != 0)
                    {
                        var ck = CrossingAt(wx, wy);
                        bool bridge = ck is CrossingKind.Bridge || (ck == null && b.Ground[idx] == GroundKind.DeepWater);
                        b.Ground[idx] = bridge ? GroundKind.Bridge : GroundKind.Ford;
                        b.Flags[idx] &= unchecked((byte)~Buf.Deep);
                    }
                    else if ((b.Flags[idx] & Buf.Paved) == 0 || kind == GroundKind.Cobble) b.Ground[idx] = kind;
                    b.Flags[idx] |= Buf.Road;
                    b.Variant[idx] = (byte)((wx * 31 + wy * 17) & 3);
                }
        }
    }

    /// <summary>Kind of the nearest declared crossing within 120 m, or null.</summary>
    private CrossingKind? CrossingAt(int wx, int wy)
    {
        long best = 120L * 120; CrossingKind? kind = null;
        for (int by = wy / 256 - 1; by <= wy / 256 + 1; by++)
            for (int bx = wx / 256 - 1; bx <= wx / 256 + 1; bx++)
                if (_crossAt.TryGetValue(Key(bx, by), out var l))
                    foreach (var (at, k) in l)
                    {
                        long dx = at.X - wx, dy = at.Y - wy, d = dx * dx + dy * dy;
                        if (d < best) { best = d; kind = k; }
                    }
        return kind;
    }
}
