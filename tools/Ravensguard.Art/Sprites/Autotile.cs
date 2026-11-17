using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

/// <summary>
/// Edge autotiles. Mask bits: N=1, E=2, S=4, W=8; a set bit means the neighbour on that side is the same
/// terrain (connected). Unconnected sides get a ragged rim where the base terrain shows through, so masks
/// give ends, straights, corners, T-junctions, crossings and isolated pieces (16 tiles per set).
/// </summary>
public static class Autotile
{
    public const int N = 1, E = 2, S = 4, W = 8;

    public static PixelBuffer Build(PixelBuffer baseTile, PixelBuffer overlay, int mask, uint rim, int seed)
    {
        var b = new PixelBuffer(32, 32);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                int worst = int.MaxValue; // min over open edges of (distance - ragged threshold)
                if ((mask & N) == 0) worst = Math.Min(worst, y - Rag(x, 0, seed));
                if ((mask & S) == 0) worst = Math.Min(worst, 31 - y - Rag(x, 1, seed));
                if ((mask & W) == 0) worst = Math.Min(worst, x - Rag(y, 2, seed));
                if ((mask & E) == 0) worst = Math.Min(worst, 31 - x - Rag(y, 3, seed));
                uint c = worst == int.MaxValue || worst > 0 ? overlay.Get(x, y) : worst == 0 ? rim : baseTile.Get(x, y);
                b.Set(x, y, c);
            }
        return b;
    }

    private static int Rag(int along, int side, int seed) => 3 + (int)(Hash(along / 2, side, seed) * 3);

    public static string MaskName(int m) =>
        (m == 0 ? "o" : "") + ((m & N) != 0 ? "n" : "") + ((m & E) != 0 ? "e" : "") + ((m & S) != 0 ? "s" : "") + ((m & W) != 0 ? "w" : "");
}
