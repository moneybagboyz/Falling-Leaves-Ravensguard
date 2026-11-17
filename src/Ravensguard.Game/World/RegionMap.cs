using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Game.World;

/// <summary>
/// The pixel overview of the embark region (REDESIGN_PLAN 2.7, world map): 500 x 500 region tiles of 64 m at 2 px each (1000 x 1000), drawn from the region
/// data: biome colours with hill shading, forest speckle, sea, lakes and rivers, roads by class, field parcels, built tiles in roof colours and realm borders.
/// A second 500 x 500 layer dims the tiles the player has not been near; the set of explored tiles comes from the chunks visited (the chunk store) and from the
/// hero's walk. Settlement symbols and names are drawn over it by the map screen and the minimap.
/// </summary>
public sealed class RegionMap
{
    public const int Tiles = RegionData.Size, Px = 2, Size = Tiles * Px;
    public RegionData Region { get; }
    public ImageTexture? Texture { get; private set; }
    public ImageTexture? Fog { get; private set; }
    public readonly bool[] Explored = new bool[Tiles * Tiles];
    byte[]? _pixels;
    Image? _fogImg;
    bool _fogDirty;
    Task<byte[]>? _building;

    public bool Ready => Texture != null;
    /// <summary>Bumps whenever the image or the fog changed, so views that cache their drawing know to redraw.</summary>
    public int Revision { get; private set; }

    public RegionMap(RegionData region)
    {
        Region = region;
        _building = Task.Run(() => Build(region));
    }

    /// <summary>Call every frame: finishes the texture once the background build is done, and refreshes the fog layer when tiles were explored.</summary>
    public void Poll()
    {
        if (_building is { IsCompleted: true } b)
        {
            _building = null;
            if (b.IsFaulted) { GD.PushWarning("region map failed: " + b.Exception!.GetBaseException().Message); _pixels = new byte[Size * Size * 4]; }
            else _pixels = b.Result;
            Texture = ImageTexture.CreateFromImage(Image.CreateFromData(Size, Size, false, Image.Format.Rgba8, _pixels));
            _fogDirty = true; Revision++;
        }
        if (_fogDirty && Texture != null)
        {
            _fogDirty = false;
            var d = new byte[Tiles * Tiles * 4];
            for (int i = 0; i < Tiles * Tiles; i++) { d[i * 4 + 3] = Explored[i] ? (byte)0 : (byte)72; d[i * 4] = 8; d[i * 4 + 1] = 10; d[i * 4 + 2] = 24; }
            _fogImg = Image.CreateFromData(Tiles, Tiles, false, Image.Format.Rgba8, d);
            if (Fog == null) Fog = ImageTexture.CreateFromImage(_fogImg); else Fog.Update(_fogImg);
            Revision++;
        }
    }

    /// <summary>Marks the tiles within <paramref name="radius"/> of a world position (metres) as explored.</summary>
    public void Explore(float wx, float wy, int radius = 4)
    {
        int tx = (int)(wx / RegionData.TileM), ty = (int)(wy / RegionData.TileM);
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = tx + dx, y = ty + dy;
                if (!RegionData.InBounds(x, y) || dx * dx + dy * dy > radius * radius + 1) continue;
                int i = RegionData.Index(x, y);
                if (!Explored[i]) { Explored[i] = true; _fogDirty = true; }
            }
    }

    /// <summary>Marks every chunk the save already knows (visited) as explored.</summary>
    public void ExploreStore(ChunkStore store)
    {
        foreach (var k in store.Keys)
            if (k.Z == 0) Explore(k.X * 32 + 16, k.Y * 32 + 16, 1);
    }

    public bool IsExplored(int tx, int ty) => RegionData.InBounds(tx, ty) && Explored[RegionData.Index(tx, ty)];

    // ------------------------------------------------------------------ drawing the image

    static readonly uint[] Biome =
    {
        0x2f5f8au, 0x3d6b9au, // sea, lake
        0x93a08au, 0x3c6b48u, 0x3f6e32u, 0x5c8f3eu, 0xa59a56u, 0xd2b872u, 0x5b7d5au, 0x8c8a8eu, 0x62994au, // tundra taiga forest grass steppe desert marsh mountain valley
    };

    static uint Shade(uint c, double k)
    {
        int r = (int)Math.Clamp(((c >> 16) & 255) * k, 0, 255), g = (int)Math.Clamp(((c >> 8) & 255) * k, 0, 255), b = (int)Math.Clamp((c & 255) * k, 0, 255);
        return (uint)(r << 16 | g << 8 | b);
    }

    static double Hash(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)s * 1442695041u;
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return h / 4294967296.0;
        }
    }

    static byte[] Build(RegionData r)
    {
        var px = new byte[Size * Size * 4];
        void Put(int x, int y, uint c) { int i = (y * Size + x) * 4; px[i] = (byte)(c >> 16); px[i + 1] = (byte)(c >> 8); px[i + 2] = (byte)c; px[i + 3] = 255; }
        int H(int x, int y) => r.HeightCm[RegionData.Index(Math.Clamp(x, 0, Tiles - 1), Math.Clamp(y, 0, Tiles - 1))];
        for (int ty = 0; ty < Tiles; ty++)
            for (int tx = 0; tx < Tiles; tx++)
            {
                int i = RegionData.Index(tx, ty);
                byte water = r.Water[i];
                uint c;
                double shade = 1.0 + Math.Clamp((H(tx - 1, ty - 1) - H(tx + 1, ty + 1)) / 2200.0 + (H(tx - 1, ty) - H(tx + 1, ty)) / 4400.0, -0.28, 0.28);
                if ((water & WaterFlags.Sea) != 0) c = Biome[0];
                else if ((water & WaterFlags.Lake) != 0) c = Biome[1];
                else
                {
                    var b = r.Biomes[i];
                    c = b switch { Ravensguard.Sim.WorldGen.Biome.Tundra => Biome[2], Ravensguard.Sim.WorldGen.Biome.Taiga => Biome[3], Ravensguard.Sim.WorldGen.Biome.TemperateForest => Biome[4], Ravensguard.Sim.WorldGen.Biome.Grassland => Biome[5], Ravensguard.Sim.WorldGen.Biome.Steppe => Biome[6], Ravensguard.Sim.WorldGen.Biome.Desert => Biome[7], Ravensguard.Sim.WorldGen.Biome.Marsh => Biome[8], Ravensguard.Sim.WorldGen.Biome.Mountain => Biome[9], _ => Biome[10] };
                    c = Shade(c, shade);
                }
                for (int py = 0; py < Px; py++)
                    for (int pxl = 0; pxl < Px; pxl++)
                    {
                        uint cc = c;
                        if ((water & (WaterFlags.Sea | WaterFlags.Lake)) == 0)
                        {
                            if (r.Forest[i] > 35 && ((pxl + py) & 1) == 0 && Hash(tx, ty, 5 + pxl) < r.Forest[i] / 110.0) cc = Shade(c, 0.74);
                            else if (r.Forest[i] > 35 && Hash(tx * 2 + pxl, ty * 2 + py, 9) < 0.18) cc = Shade(c, 0.88);
                            if ((water & (WaterFlags.River | WaterFlags.Stream)) != 0) cc = (water & WaterFlags.River) != 0 ? 0x3d6b9au : 0x6a9cc4u;
                            else if (r.ParcelMap[i] > 0 && r.Built[i] == 0) cc = ((tx + ty + pxl) & 1) == 0 ? 0xd6bd5eu : 0xa8844eu;
                            if (r.Road[i] > 0 && (water & WaterFlags.River) == 0)
                                cc = r.Road[i] switch { 4 => 0xf0e6b4u, 3 => 0xe2d49au, 2 => 0xc8b27au, _ => 0xb49a68u };
                            if (r.Built[i] != 0)
                            {
                                double h = Hash(tx, ty, 41);
                                uint roof = h < .42 ? 0xaa9048u : h < .62 ? 0x8a6a4au : h < .82 ? 0x75747cu : 0xb33a2cu;
                                cc = (pxl == 1 && py == 1) ? Shade(roof, 0.7) : roof;
                            }
                        }
                        Put(tx * Px + pxl, ty * Px + py, cc);
                    }
            }
        // realm borders: a darkened line on the east and south edge of a tile that borders another realm
        for (int ty = 0; ty < Tiles - 1; ty++)
            for (int tx = 0; tx < Tiles - 1; tx++)
            {
                int i = RegionData.Index(tx, ty); byte a = r.RealmMap[i];
                if (a == 255) continue;
                if (r.RealmMap[i + 1] != a && r.RealmMap[i + 1] != 255) for (int py = 0; py < Px; py++) Blend(px, (tx * Px + 1), ty * Px + py, 0x5a3e28u);
                if (r.RealmMap[i + Tiles] != a && r.RealmMap[i + Tiles] != 255) for (int pxl = 0; pxl < Px; pxl++) Blend(px, tx * Px + pxl, ty * Px + 1, 0x5a3e28u);
            }
        return px;

        static void Blend(byte[] p, int x, int y, uint c)
        {
            int i = (y * Size + x) * 4;
            p[i] = (byte)((p[i] + ((c >> 16) & 255)) / 2); p[i + 1] = (byte)((p[i + 1] + ((c >> 8) & 255)) / 2); p[i + 2] = (byte)((p[i + 2] + (c & 255)) / 2);
        }
    }

    /// <summary>Region tile of a world position in metres.</summary>
    public static (int X, int Y) TileOf(float wx, float wy) => ((int)(wx / RegionData.TileM), (int)(wy / RegionData.TileM));

    /// <summary>The map symbol for a site kind.</summary>
    public static Ravensguard.Game.UI.MapIcon IconOf(SiteKind k) => k switch
    {
        SiteKind.Ruin => Ravensguard.Game.UI.MapIcon.Ruin, SiteKind.Cave => Ravensguard.Game.UI.MapIcon.Cave, SiteKind.Mine => Ravensguard.Game.UI.MapIcon.Mine,
        SiteKind.Barrow => Ravensguard.Game.UI.MapIcon.Barrow, SiteKind.Battlefield => Ravensguard.Game.UI.MapIcon.Battlefield, _ => Ravensguard.Game.UI.MapIcon.Hoard,
    };
}
