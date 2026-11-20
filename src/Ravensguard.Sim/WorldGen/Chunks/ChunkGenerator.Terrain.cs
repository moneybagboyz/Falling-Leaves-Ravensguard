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
            if (plan.Square.W > 0)
            {
                if (s.Class == SettlementClass.Town) Pave(b, plan.Square, kind);
                else Green(b, plan.Square);
            }
            foreach (var st in plan.Streets)
            {
                var skind = s.Class == SettlementClass.Town && st.WidthM <= 3 ? GroundKind.Dirt : kind; // a town's back lanes are beaten earth, not cobbles
                int w = Math.Max(2, st.WidthM);
                int x0 = Math.Min(st.A.X, st.B.X) - w / 2, y0 = Math.Min(st.A.Y, st.B.Y) - w / 2;
                Pave(b, new RectM(x0, y0, Math.Abs(st.A.X - st.B.X) + w, Math.Abs(st.A.Y - st.B.Y) + w), skind);
            }
        }
    }

    /// <summary>A village green: short grass kept clear of trees, with a pond at one side (a muddy rim round a patch of shallow water).</summary>
    private void Green(Buf b, RectM q)
    {
        bool horiz = q.W >= q.H;
        int L = horiz ? q.W : q.H, S = horiz ? q.H : q.W, cx = q.CenterX, cy = q.CenterY;
        int pu = 9, pv = S / 2 - 4, rx = 5, ry = 3; // pond centre and radii in (u, v)
        for (int wy = Math.Max(q.Y, b.Y0); wy < Math.Min(q.Bottom, b.Y0 + B); wy++)
            for (int wx = Math.Max(q.X, b.X0); wx < Math.Min(q.Right, b.X0 + B); wx++)
            {
                int idx = b.I(wx, wy);
                if ((b.Flags[idx] & Buf.Water) != 0) continue;
                int u = horiz ? wx - cx : wy - cy, v = horiz ? wy - cy : wx - cx;
                // ellipse test scaled to integers: ((u-pu)/rx)^2 + ((v-pv)/ry)^2
                int e = (u - pu) * (u - pu) * ry * ry + (v - pv) * (v - pv) * rx * rx, full = rx * rx * ry * ry;
                b.Flags[idx] |= Buf.Yard;
                if (e <= full) { b.Ground[idx] = GroundKind.Water; b.Flags[idx] |= Buf.Water; b.Variant[idx] = (byte)(H(StBase, wx, wy) & 3); }
                else if (e <= full * 14 / 10) { b.Ground[idx] = GroundKind.Mud; b.Variant[idx] = (byte)(H(StBase, wx, wy) & 1); }
                else { b.Ground[idx] = GroundKind.Grass; b.Variant[idx] = (byte)(H(StBase, wx, wy) & 7); }
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

    /// <summary>P5-B: whether a road (or a highway) of the region's network covers this world cell (build mode refuses a piece over a road; build-mode-and-construction 4.1).</summary>
    public bool IsRoadCell(int wx, int wy)
    {
        var segs = new List<int>();
        _roads.Query(wx, wy, wx + 1, wy + 1, segs);
        foreach (int si in segs)
        {
            Dist(_roads.Segs[si], wx * 100L + 50, wy * 100L + 50, out long d2, out long half2, out _);
            if (d2 <= half2) return true;
        }
        return false;
    }

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
                        if (InDeck(wx, wy)) { b.Ground[idx] = GroundKind.Bridge; b.Flags[idx] &= unchecked((byte)~Buf.Deep); }
                        else
                        {
                            var ck = CrossingAt(wx, wy);
                            // beside a deck the road wades (a plank where it is too deep)
                            b.Ground[idx] = b.Ground[idx] == GroundKind.DeepWater ? GroundKind.Bridge : GroundKind.Ford;
                            b.Flags[idx] &= unchecked((byte)~Buf.Deep);
                        }
                    }
                    else if ((b.Flags[idx] & Buf.Paved) == 0 || kind == GroundKind.Cobble) b.Ground[idx] = kind;
                    b.Flags[idx] |= Buf.Road;
                    b.Variant[idx] = (byte)((wx * 31 + wy * 17) & 3);
                }
        }
        PaintDecks(b);
    }

    /// <summary>Lays the planks of every bridge deck that touches the buffer over whatever water lies under it.</summary>
    private void PaintDecks(Buf b)
    {
        int mx = b.X0 + B / 2, my = b.Y0 + B / 2;
        foreach (var k in DecksNear(mx, my))
        {
            int r = k.HalfLen + k.HalfW + 1;
            for (int wy = Math.Max((int)k.Cy - r, b.Y0); wy <= Math.Min((int)k.Cy + r, b.Y0 + B - 1); wy++)
                for (int wx = Math.Max((int)k.Cx - r, b.X0); wx <= Math.Min((int)k.Cx + r, b.X0 + B - 1); wx++)
                {
                    int idx = b.I(wx, wy);
                    if ((b.Flags[idx] & Buf.Water) == 0) continue;
                    var (u, v) = DeckUV(k, wx, wy);
                    if (Math.Abs(u) > k.HalfLen || Math.Abs(v) > k.HalfW) continue;
                    b.Ground[idx] = GroundKind.Bridge; b.Flags[idx] &= unchecked((byte)~Buf.Deep); b.Flags[idx] |= Buf.Road;
                    b.Variant[idx] = (byte)((wx * 31 + wy * 17) & 3);
                }
        }
    }


    /// <summary>A bridge deck: a rectangle laid along the road across the water (centre, unit direction scaled by 1024, half length and half width in metres).</summary>
    private sealed record Deck(long Cx, long Cy, int Dx, int Dy, int HalfLen, int HalfW);

    private Deck? MakeDeck(RoadEdge e, RoadCrossing c)
    {
        if (e.Points.Length < 2) return null;
        // the road segment nearest the crossing gives the deck its centre and direction
        double best = double.MaxValue, bx = c.At.X, by = c.At.Y, dx = 1, dy = 0;
        for (int i = 0; i + 1 < e.Points.Length; i++)
        {
            double ax = e.Points[i].X, ay = e.Points[i].Y, ex = e.Points[i + 1].X - ax, ey = e.Points[i + 1].Y - ay, l2 = ex * ex + ey * ey;
            if (l2 == 0) continue;
            double t = Math.Clamp(((c.At.X - ax) * ex + (c.At.Y - ay) * ey) / l2, 0, 1), px = ax + ex * t, py = ay + ey * t, d = (px - c.At.X) * (px - c.At.X) + (py - c.At.Y) * (py - c.At.Y);
            if (d < best) { best = d; bx = px; by = py; double l = Math.Sqrt(l2); dx = ex / l; dy = ey / l; }
        }
        // how slantwise the river runs under the road lengthens the deck
        double sin = 1;
        var w = Region.Waterways.FirstOrDefault(x => x.Id == c.WaterwayId);
        if (w != null && w.Points.Length >= 2)
        {
            double wd = double.MaxValue;
            for (int i = 0; i + 1 < w.Points.Length; i++)
            {
                double ax = w.Points[i].X, ay = w.Points[i].Y, ex = w.Points[i + 1].X - ax, ey = w.Points[i + 1].Y - ay, l2 = ex * ex + ey * ey;
                if (l2 == 0) continue;
                double t = Math.Clamp(((bx - ax) * ex + (by - ay) * ey) / l2, 0, 1), px = ax + ex * t, py = ay + ey * t, d = (px - bx) * (px - bx) + (py - by) * (py - by);
                if (d < wd) { wd = d; double l = Math.Sqrt(l2); sin = Math.Abs(dx * ey / l - dy * ex / l); }
            }
        }
        int half = (int)Math.Clamp(c.WidthCm / 200.0 / Math.Max(0.4, sin) + 6, 8, 48);
        int halfW = Math.Max(2, e.WidthCm / 200);
        return new Deck((long)Math.Round(bx), (long)Math.Round(by), (int)Math.Round(dx * 1024), (int)Math.Round(dy * 1024), half, halfW);
    }

    /// <summary>The deck-relative position of a cell: u along the road, v across it (metres, cell centre).</summary>
    private static (int U, int V) DeckUV(Deck k, int wx, int wy)
    {
        long rx = 2L * wx + 1 - 2 * k.Cx, ry = 2L * wy + 1 - 2 * k.Cy;
        return ((int)((rx * k.Dx + ry * k.Dy) / 2048), (int)((-rx * k.Dy + ry * k.Dx) / 2048));
    }

    private IEnumerable<Deck> DecksNear(int wx, int wy)
    {
        for (int by = wy / 256 - 1; by <= wy / 256 + 1; by++)
            for (int bx = wx / 256 - 1; bx <= wx / 256 + 1; bx++)
                if (_decks.TryGetValue(Key(bx, by), out var l)) foreach (var k in l) yield return k;
    }

    private bool InDeck(int wx, int wy)
    {
        foreach (var k in DecksNear(wx, wy))
        {
            var (u, v) = DeckUV(k, wx, wy);
            if (Math.Abs(u) <= k.HalfLen && Math.Abs(v) <= k.HalfW) return true;
        }
        return false;
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
