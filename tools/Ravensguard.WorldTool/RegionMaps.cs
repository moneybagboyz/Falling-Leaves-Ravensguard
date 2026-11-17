using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.WorldTool;

/// <summary>Debug rendering of a region: terrain, water, forest, fields, roads, settlements and sites; plus a plan view of the largest town.</summary>
public static class RegionMaps
{
    private const int N = Region.Size;

    private static uint Rgb(int r, int g, int b) => (uint)((Math.Clamp(r, 0, 255) << 16) | (Math.Clamp(g, 0, 255) << 8) | Math.Clamp(b, 0, 255));
    private static uint Shade(uint c, int d) => Rgb((int)(c >> 16 & 255) + d, (int)(c >> 8 & 255) + d, (int)(c & 255) + d);

    private static uint BiomeColor(Biome b) => b switch
    {
        Biome.Sea => Rgb(40, 80, 140), Biome.Lake => Rgb(70, 130, 190), Biome.Tundra => Rgb(190, 200, 190), Biome.Taiga => Rgb(80, 120, 85),
        Biome.TemperateForest => Rgb(110, 150, 80), Biome.Grassland => Rgb(150, 185, 95), Biome.Steppe => Rgb(190, 180, 110), Biome.Desert => Rgb(225, 200, 140),
        Biome.Marsh => Rgb(120, 150, 120), Biome.Mountain => Rgb(150, 140, 130), Biome.RiverValley => Rgb(135, 190, 100), _ => Rgb(100, 100, 100),
    };

    private sealed class Canvas(int w, int h)
    {
        public readonly int W = w, H = h;
        public readonly uint[] Px = new uint[w * h];
        public void Set(int x, int y, uint c) { if ((uint)x < W && (uint)y < H) Px[y * W + x] = c; }
        public void Rect(int x, int y, int rw, int rh, uint c) { for (int j = 0; j < rh; j++) for (int i = 0; i < rw; i++) Set(x + i, y + j, c); }
        public void Outline(int x, int y, int rw, int rh, uint c)
        {
            for (int i = 0; i < rw; i++) { Set(x + i, y, c); Set(x + i, y + rh - 1, c); }
            for (int j = 0; j < rh; j++) { Set(x, y + j, c); Set(x + rw - 1, y + j, c); }
        }
        public void Line(int x0, int y0, int x1, int y1, uint c, int thick)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            while (true)
            {
                int o = thick / 2;
                for (int j = -o; j < thick - o; j++) for (int i = -o; i < thick - o; i++) Set(x0 + i, y0 + j, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }

    public static void Write(Region r, WorldHistory h, string path, string townPath, int scale)
    {
        int S = N * scale;
        var cv = new Canvas(S, S);
        double k = scale / (double)Region.TileM; // pixels per metre
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int t = y * N + x;
                uint c = BiomeColor(r.Biomes[t]);
                int hc = r.HeightCm[t];
                int hNW = x > 0 && y > 0 ? r.HeightCm[t - N - 1] : hc, hSE = x < N - 1 && y < N - 1 ? r.HeightCm[t + N + 1] : hc;
                int shade = Math.Clamp((hNW - hSE) / 18, -40, 40);
                if (r.Water[t] == 0 || (r.Water[t] & (WaterFlags.Stream | WaterFlags.River)) != 0)
                {
                    int forest = r.Forest[t];
                    if (forest > 30 && r.Biomes[t] is not (Biome.Mountain or Biome.Desert)) c = Rgb(70 + (100 - forest) / 4, 115 + (100 - forest) / 4, 65);
                    if (r.Biomes[t] == Biome.Mountain) c = Shade(c, (hc - 60000) / -2500);
                    c = Shade(c, shade);
                }
                else if ((r.Water[t] & WaterFlags.Sea) != 0) c = Rgb(35, 75, 135);
                if (r.ParcelMap[t] > 0)
                {
                    var p = r.Parcels[r.ParcelMap[t] - 1];
                    c = p.Slot switch { 0 => Rgb(228, 200, 85), 1 => Rgb(165, 196, 70), _ => Rgb(165, 135, 95) };
                    c = Shade(c, shade / 2 + ((x + y) & 1) * 7);
                    if (p.Owner.Kind == OwnerKind.Demesne) c = Shade(c, -14);
                }
                for (int j = 0; j < scale; j++) for (int i = 0; i < scale; i++) cv.Px[(y * scale + j) * S + x * scale + i] = c;
            }
        // realm borders
        for (int y = 0; y < N - 1; y++)
            for (int x = 0; x < N - 1; x++)
            {
                int t = y * N + x;
                if (r.RealmMap[t] != r.RealmMap[t + 1] || r.RealmMap[t] != r.RealmMap[t + N]) cv.Set(x * scale, y * scale, Rgb(120, 40, 120));
            }
        int P(int m) => (int)(m * k);
        foreach (var w in r.Waterways)
        {
            bool river = w.Kind == WaterKind.River;
            for (int i = 1; i < w.Points.Length; i++)
                cv.Line(P(w.Points[i - 1].X), P(w.Points[i - 1].Y), P(w.Points[i].X), P(w.Points[i].Y), river ? Rgb(40, 100, 190) : Rgb(100, 160, 220), river ? Math.Max(2, scale) : 1);
        }
        foreach (var e in r.RoadEdges)
        {
            uint col = e.Class switch { RoadClass.Highway => Rgb(110, 70, 40), RoadClass.Road => Rgb(135, 95, 60), RoadClass.Track => Rgb(160, 130, 95), _ => Rgb(185, 160, 125) };
            int th = e.Class >= RoadClass.Road ? 3 : e.Class == RoadClass.Track ? 2 : 1;
            for (int i = 1; i < e.Points.Length; i++) cv.Line(P(e.Points[i - 1].X), P(e.Points[i - 1].Y), P(e.Points[i].X), P(e.Points[i].Y), col, th);
            foreach (var cr in e.Crossings) cv.Rect(P(cr.At.X) - 1, P(cr.At.Y) - 1, 3, 3, cr.Kind == CrossingKind.Bridge ? Rgb(60, 40, 20) : Rgb(230, 230, 230));
        }
        foreach (var n in r.RoadNodes) if (n.Kind == RoadNodeKind.Gate) cv.Rect(P(n.X) - 3, P(n.Y) - 3, 7, 7, Rgb(255, 255, 255));
        foreach (var s in r.Settlements)
        {
            int px = P(s.CenterX), py = P(s.CenterY);
            switch (s.Class)
            {
                case SettlementClass.Town: cv.Rect(px - 4, py - 4, 9, 9, Rgb(200, 30, 30)); cv.Outline(px - 4, py - 4, 9, 9, Rgb(255, 255, 255)); break;
                case SettlementClass.Village: cv.Rect(px - 2, py - 2, 5, 5, Rgb(70, 40, 30)); break;
                default: cv.Set(px, py, Rgb(90, 50, 30)); cv.Set(px + 1, py, Rgb(90, 50, 30)); break;
            }
        }
        foreach (var s in r.Sites)
        {
            int px = P(s.X), py = P(s.Y);
            uint col = s.Kind switch { SiteKind.Ruin => Rgb(150, 40, 190), SiteKind.Cave => Rgb(20, 20, 20), SiteKind.Mine => Rgb(255, 140, 0), SiteKind.Barrow => Rgb(255, 255, 255), _ => Rgb(230, 20, 20) };
            if (s.MonsterLair) cv.Outline(px - 4, py - 4, 9, 9, Rgb(255, 0, 255));
            cv.Rect(px - 2, py - 2, 5, 5, col);
        }
        Png.Write(path, S, S, cv.Px);

        var town = r.Towns.OrderByDescending(t => t.TargetPop).FirstOrDefault() ?? r.Settlements.OrderByDescending(t => t.TargetPop).FirstOrDefault();
        if (town != null) WriteTown(r, town, townPath);
    }

    private static void WriteTown(Region r, RegionSettlement s, string path)
    {
        const int Span = 500, Z = 2;
        int x0 = s.CenterX - Span / 2, y0 = s.CenterY - Span / 2;
        var cv = new Canvas(Span * Z, Span * Z);
        for (int py = 0; py < Span * Z; py++)
            for (int px = 0; px < Span * Z; px++)
            {
                int mx = x0 + px / Z, my = y0 + py / Z;
                uint c = Rgb(120, 170, 80);
                if (mx >= 0 && my >= 0 && mx < Region.Meters && my < Region.Meters)
                {
                    int t = (my / Region.TileM) * N + mx / Region.TileM;
                    c = BiomeColor(r.Biomes[t]);
                    if (r.ParcelMap[t] > 0) c = r.Parcels[r.ParcelMap[t] - 1].Slot switch { 0 => Rgb(232, 210, 110), 1 => Rgb(190, 205, 105), _ => Rgb(176, 160, 110) };
                    if ((r.Water[t] & (WaterFlags.River | WaterFlags.Stream | WaterFlags.Lake)) != 0) c = Rgb(70, 130, 200);
                    if (r.Road[t] != 0) c = Shade(c, -6);
                }
                cv.Px[py * Span * Z + px] = c;
            }
        int X(int m) => (m - x0) * Z; int Y(int m) => (m - y0) * Z;
        foreach (var e in r.RoadEdges)
            for (int i = 1; i < e.Points.Length; i++) cv.Line(X(e.Points[i - 1].X), Y(e.Points[i - 1].Y), X(e.Points[i].X), Y(e.Points[i].Y), Rgb(150, 115, 80), Math.Max(2, e.WidthCm / 100 * Z));
        foreach (var st in s.Plan.Streets) cv.Line(X(st.A.X), Y(st.A.Y), X(st.B.X), Y(st.B.Y), Rgb(165, 135, 100), st.WidthM * Z);
        if (s.Plan.Square.W > 0) cv.Rect(X(s.Plan.Square.X), Y(s.Plan.Square.Y), s.Plan.Square.W * Z, s.Plan.Square.H * Z, Rgb(200, 180, 140));
        foreach (var l in s.Plan.Lots)
        {
            uint col = l.Use switch
            {
                LotUse.Cottage => Rgb(150, 100, 70), LotUse.Longhouse => Rgb(120, 80, 50), LotUse.Burgage => Rgb(180, 90, 70), LotUse.Manor => Rgb(60, 60, 140),
                LotUse.Chapel or LotUse.Church => Rgb(240, 240, 240), LotUse.Mill => Rgb(80, 80, 80), LotUse.Tavern => Rgb(220, 150, 40), LotUse.Smithy => Rgb(40, 40, 40), _ => Rgb(110, 110, 70),
            };
            cv.Outline(X(l.Rect.X), Y(l.Rect.Y), l.Rect.W * Z, l.Rect.H * Z, Shade(col, -20));
            int bw = Math.Max(4, l.Rect.W * 6 / 10), bh = Math.Max(4, l.Rect.H * (l.IsDwelling ? 4 : 6) / 10);
            cv.Rect(X(l.Rect.CenterX) - bw * Z / 2, Y(l.Rect.CenterY) - bh * Z / 2, bw * Z, bh * Z, col);
        }
        Png.Write(path, Span * Z, Span * Z, cv.Px);
    }
}
