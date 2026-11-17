using Ravensguard.Content;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.WorldGen.ChunkGen;

/// <summary>Dev aid, off unless RAVENS_DUMP names an output folder: writes top-down colour maps (PPM) of interesting places so generated layouts can be eyeballed without the game.</summary>
public class ChunkMapDump(ChunkWorldFixture fx) : IClassFixture<ChunkWorldFixture>
{
    private static (byte R, byte G, byte B) Colour(GroundKind g) => g switch
    {
        GroundKind.Grass => (90, 140, 60), GroundKind.Meadow => (110, 160, 70), GroundKind.ForestFloor => (60, 100, 50), GroundKind.LeafLitter => (110, 90, 50),
        GroundKind.Dirt => (150, 120, 80), GroundKind.Cobble => (170, 170, 175), GroundKind.Sand => (220, 200, 140), GroundKind.Mud => (100, 80, 60),
        GroundKind.Marsh => (70, 110, 90), GroundKind.Gravel => (150, 150, 140), GroundKind.Rock => (120, 120, 120), GroundKind.Scree => (140, 135, 130),
        GroundKind.Peat => (70, 55, 45), GroundKind.Water => (90, 150, 210), GroundKind.DeepWater => (40, 90, 170), GroundKind.Ford => (140, 170, 190),
        GroundKind.Bridge => (140, 100, 60), GroundKind.Furrow => (120, 85, 50), GroundKind.Sown => (150, 170, 70), GroundKind.Crop => (210, 190, 80),
        GroundKind.Stubble => (180, 160, 100), GroundKind.Fallow => (130, 150, 80), _ => (0, 0, 0),
    };

    private void Dump(string dir, string name, int cx, int cy, int radiusChunks)
    {
        var s = new ChunkStreamer(fx.Generator);
        int n = radiusChunks * 2 + 1, px = 4, size = n * 32;
        var img = new byte[size * px * size * px * 3];
        void Set(int x, int y, (byte R, byte G, byte B) c, int w = 1)
        {
            for (int dy = 0; dy < px * w; dy++) for (int dx = 0; dx < px * w; dx++)
            {
                int X = x * px + dx, Y = y * px + dy; if (X >= size * px || Y >= size * px) continue;
                int i = (Y * size * px + X) * 3; img[i] = c.R; img[i + 1] = c.G; img[i + 2] = c.B;
            }
        }
        var pieces = fx.Generator.Library.Pieces; var pal = fx.Generator.Palette;
        for (int j = -radiusChunks; j <= radiusChunks; j++)
            for (int i = -radiusChunks; i <= radiusChunks; i++)
            {
                var c = s.LoadSync(cx + i, cy + j);
                int bx = (i + radiusChunks) * 32, by = (j + radiusChunks) * 32;
                for (int ly = 0; ly < 32; ly++) for (int lx = 0; lx < 32; lx++)
                {
                    int code = c.TileAt(lx, ly, 0);
                    var col = Colour(TileCode.Ground(code));
                    var k = pieces.KindOf(pal.Id(TileCode.Structure(code)));
                    if (k is PieceKind.Wall or PieceKind.Post) col = (60, 40, 30);
                    else if (k is PieceKind.Door) col = (200, 120, 40);
                    else if (k is PieceKind.Window) col = (120, 160, 200);
                    else if (k != null && k != PieceKind.Ground) col = (210, 180, 140);
                    if (TileCode.Fixture(code) != 0) col = (160, 90, 60);
                    Set(bx + lx, by + ly, col);
                }
                foreach (var p in c.Level(0).Props.Values)
                {
                    var col = p.Kind switch { PropKind.Tree => ((byte)20, (byte)70, (byte)20), PropKind.Bush or PropKind.Hedge => ((byte)40, (byte)110, (byte)40), PropKind.Rocks or PropKind.Boulder => ((byte)100, (byte)100, (byte)100),
                        PropKind.Hurdle => ((byte)150, (byte)110, (byte)60), PropKind.Well => ((byte)30, (byte)60, (byte)150), PropKind.Stall => ((byte)200, (byte)50, (byte)50),
                        PropKind.FruitTree => ((byte)200, (byte)90, (byte)60), PropKind.Headstone => ((byte)235, (byte)235, (byte)235), PropKind.GraveMound => ((byte)120, (byte)90, (byte)70),
                        PropKind.LowWall => ((byte)90, (byte)90, (byte)95), PropKind.Rail => ((byte)110, (byte)70, (byte)30), PropKind.Gate => ((byte)255, (byte)255, (byte)0), _ => ((byte)230, (byte)40, (byte)200) };
                    Set(bx + p.X, by + p.Y, col, p.Kind == PropKind.Tree ? 1 : 1);
                }
            }
        using var f = File.Create(Path.Combine(dir, name + ".ppm"));
        var head = System.Text.Encoding.ASCII.GetBytes($"P6\n{size * px} {size * px}\n255\n");
        f.Write(head); f.Write(img);
    }

    [Fact]
    public void Dump_maps()
    {
        var dir = Environment.GetEnvironmentVariable("RAVENS_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var r = fx.Region;
        var t = RegionSpawn.LargestTown(r)!;
        Dump(dir, "town", t.CenterX / 32, t.CenterY / 32, 8);
        var v = r.Villages.OrderByDescending(x => x.TargetPop).First();
        Dump(dir, "village", v.CenterX / 32, v.CenterY / 32, 6);
        var f = r.Farmsteads.First();
        Dump(dir, "farm", f.CenterX / 32, f.CenterY / 32, 4);
        var cr = r.RoadEdges.SelectMany(e => e.Crossings).OrderByDescending(c => c.WidthCm).First();
        Dump(dir, "river", cr.At.X / 32, cr.At.Y / 32, 6);
        var site = r.Sites.FirstOrDefault(x => x.Kind == SiteKind.Barrow) ?? r.Sites[0];
        File.WriteAllText(Path.Combine(dir, "coords.txt"), $"farm {f.CenterX},{f.CenterY}\nbridge {cr.At.X},{cr.At.Y}\ntown {t.CenterX},{t.CenterY}\nvillage {v.CenterX},{v.CenterY}\n");
        Dump(dir, "site", site.X / 32, site.Y / 32, 3);
    }
}
