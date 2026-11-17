using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.Region;

internal sealed partial class RegionBuilder
{
    /// <summary>Continental cell index under a region tile after a domain warp, so biome and realm borders are organic rather than 2 km blocks.</summary>
    private int[] _warpCell = [];

    private void BuildAttributes()
    {
        _warpCell = new int[Tiles];
        ulong wsA = DetRng.Mix(_seed ^ 0xA1), wsB = DetRng.Mix(_seed ^ 0xB2), fs = DetRng.Mix(_seed ^ 0xC3), ns = DetRng.Mix(_seed ^ 0xD4);
        var biomeNear = new Biome[Tiles];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int t = Idx(x, y);
                int X = _ox + x * TileM + TileM / 2, Y = _oy + y * TileM + TileM / 2;
                var fx = Fixed.FromRatio(X, 3200); var fy = Fixed.FromRatio(Y, 3200);
                var gx = Fixed.FromRatio(X, 700); var gy = Fixed.FromRatio(Y, 700);
                int wx = (int)(((long)DetNoise.Fbm(wsA, fx, fy, 3).Raw - 32768) * 800 >> 15) + (int)(((long)DetNoise.Fbm(wsA + 5, gx, gy, 2).Raw - 32768) * 450 >> 15);
                int wy = (int)(((long)DetNoise.Fbm(wsB, fx, fy, 3).Raw - 32768) * 800 >> 15) + (int)(((long)DetNoise.Fbm(wsB + 5, gx, gy, 2).Raw - 32768) * 450 >> 15);
                int cxi = Math.Clamp((X + wx) / RegionSettings.CellM, 0, Continent.Size - 1);
                int cyi = Math.Clamp((Y + wy) / RegionSettings.CellM, 0, Continent.Size - 1);
                _warpCell[t] = Continent.Index(cxi, cyi);
            }

        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int t = Idx(x, y);
                int X = _ox + x * TileM + TileM / 2, Y = _oy + y * TileM + TileM / 2;
                int cell = _warpCell[t];
                int e = (y + Mg) * E + x + Mg;
                byte water = _r.Water[t];
                int h = _r.HeightCm[t], slope = _r.SlopePermille[t];

                Biome b = _c.Biomes[cell];
                if (b is Biome.Sea or Biome.Lake)
                {
                    // Coarse cell is water but the tile is land: borrow the nearest land biome around the cell.
                    b = Biome.Grassland;
                    int cxi = cell % Continent.Size, cyi = cell / Continent.Size;
                    for (int d = 0; d < 8; d++)
                    {
                        int nx = cxi + Dx8[d], ny = cyi + Dy8[d];
                        if ((uint)nx >= Continent.Size || (uint)ny >= Continent.Size) continue;
                        var nb = _c.Biomes[Continent.Index(nx, ny)];
                        if (nb is not (Biome.Sea or Biome.Lake)) { b = nb; break; }
                    }
                }
                if ((water & WaterFlags.Sea) != 0) b = Biome.Sea;
                else if ((water & WaterFlags.Lake) != 0) b = Biome.Lake;
                else
                {
                    if (slope >= 260 || h >= 160_000) b = Biome.Mountain;
                    else if (b != Biome.Mountain && b != Biome.Desert && b != Biome.Tundra && b != Biome.Marsh && RiverDist[t] <= 2 && (water & WaterFlags.River) == 0 && slope < 120) b = Biome.RiverValley;
                }
                _r.Biomes[t] = b;

                // Forest cover.
                int baseF = b switch
                {
                    Biome.TemperateForest => 75, Biome.Taiga => 68, Biome.Grassland => 16, Biome.Steppe => 4, Biome.RiverValley => 24,
                    Biome.Marsh => 10, Biome.Tundra => 3, Biome.Mountain => 24, Biome.Desert => 1, _ => 0,
                };
                int forest = 0;
                if (baseF > 0)
                {
                    int nn = (int)DetNoise.Fbm(fs, Fixed.FromRatio(X, 1100), Fixed.FromRatio(Y, 1100), 3).Raw;
                    forest = baseF + (nn - 32768) * 110 / 32768;
                    if (RiverDist[t] <= 1) forest /= 2;
                    if (h > 90_000) forest -= (h - 90_000) / 1500;
                    forest = Math.Clamp(forest, 0, 100);
                }
                _r.Forest[t] = (byte)forest;

                // Fertility: continental soil, slope, alluvium, drainage, altitude.
                int fert = 0;
                if ((water & (WaterFlags.Sea | WaterFlags.Lake)) == 0)
                {
                    fert = _eFertBase[e];
                    fert -= slope / 5;
                    int rd = RiverDist[t], wd = WaterDist[t];
                    if (rd <= 3) fert += 10; else if (rd <= 6) fert += 5; else if (wd <= 3) fert += 4;
                    int dr = _eDrainBase[e];
                    if (dr < 30) fert -= 8;
                    if (h > 80_000) fert -= (h - 80_000) / 4000;
                    fert = b switch
                    {
                        Biome.Mountain => fert * 40 / 100, Biome.Desert => fert * 20 / 100, Biome.Tundra => fert * 30 / 100, Biome.Marsh => fert * 55 / 100,
                        Biome.Taiga => fert * 75 / 100, Biome.Steppe => fert * 85 / 100, _ => fert,
                    };
                    fert += (int)DetNoise.Fbm(ns, Fixed.FromRatio(X, 600), Fixed.FromRatio(Y, 600), 2).Raw * 12 / 65536 - 6;
                    fert = Math.Clamp(fert, 0, 100);
                }
                _r.Fertility[t] = (byte)fert;

                int drain = _eDrainBase[e] + slope / 8 - (WaterDist[t] <= 1 ? 6 : 0) - (b == Biome.Marsh ? 25 : 0);
                if ((water & (WaterFlags.Sea | WaterFlags.Lake)) != 0) drain = 0;
                _r.Drainage[t] = (byte)Math.Clamp(drain, 0, 100);

                int owner = _h.FinalOwner.Length == Continent.Cells ? _h.FinalOwner[cell] : Continent.None;
                _r.RealmMap[t] = (water & WaterFlags.Sea) != 0 ? Continent.None : (byte)owner;
            }
    }

    // ---------- realms and wars (data for start safety, plan §3f) ----------
    private void BuildRealmsAndWars()
    {
        var count = new int[256];
        for (int t = 0; t < Tiles; t++) count[_r.RealmMap[t]]++;
        for (int k = 0; k < 255; k++)
            if (count[k] > 0 && k < _h.Realms.Count) _r.Realms.Add(new RegionRealm(k, _h.Realms[k].Name, count[k]));
        int cxm = Region.Meters / 2, cym = Region.Meters / 2;
        foreach (var w in _h.Wars)
        {
            if (!w.Alive) continue;
            var cells = new List<int>();
            if (w.Target >= 0 && w.Target < _h.Settlements.Count) cells.Add(_h.Settlements[w.Target].Cell);
            foreach (var b in _h.Battlefields) if (b.War == w.Index && w.Alive) cells.Add(b.Cell);
            foreach (int cell in cells.Distinct())
            {
                int x = (cell % Continent.Size) * RegionSettings.CellM + RegionSettings.CellM / 2 - _ox;
                int y = (cell / Continent.Size) * RegionSettings.CellM + RegionSettings.CellM / 2 - _oy;
                int d = Dist(x, y, cxm, cym);
                if (d <= 60_000) _r.ActiveWars.Add(new ActiveWarMarker(w.Index, w.Name, cell, x, y, d));
            }
        }
    }
}
