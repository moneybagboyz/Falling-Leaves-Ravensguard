using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

/// <summary>Tiles added for the first playable street view: wall caps, wattle walls, door frames, stairs, ladder, trapdoor, workbench, brazier.</summary>
public sealed class Hamlet
{
    private readonly ColorLookup P;
    private readonly RampLookup R;
    private static readonly ShadeOptions Top = new() { TopHi = true };
    public Hamlet(Palette pal) { P = pal.P; R = pal.R; }

    private PixelBuffer Obj(int w, int h, Action<PixelBuffer> fn) { var b = new PixelBuffer(w, h); fn(b); b.Outline(P["out"]); return b; }

    /// <summary>Wall cell whose southern neighbour is also wall: the top surface fills the whole tile. mat: timber | stone | wattle.</summary>
    public PixelBuffer WallCap(string mat, bool win)
    {
        var b = new PixelBuffer(32, 32);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                uint c;
                switch (mat)
                {
                    case "stone":
                    {
                        int row = y / 8, off = (row % 2) * 8, bx = (x + off) % 16;
                        int k = 3;
                        if (bx == 0 || y % 8 == 7) k = 1;
                        else if (bx == 1 || y % 8 == 0) k = 4;
                        if (Hash(x, y, 14) < .08 && k == 3) k = 2;
                        c = R["stone"][k];
                        break;
                    }
                    case "wattle":
                    {
                        int k = Hash(x, y, 15) < .25 ? 1 : 2;
                        if (x % 8 == 3 && y % 3 != 2) k = 0;
                        c = R["dirt"][k + 0];
                        break;
                    }
                    default:
                    {
                        int k = 2;
                        if (y % 16 == 0) k = 0; else if (y % 16 == 1) k = 3; // log joints
                        if (x < 2) k = 3;
                        if (x >= 29) k = 0;
                        if (Hash(x, y, 16) < .1 && k == 2) k = 1;
                        c = R["wood"][k];
                        break;
                    }
                }
                b.Set(x, y, c);
            }
        // dark side edges so a vertical run reads as one raised wall
        for (int y = 0; y < 32; y++) { b.Set(0, y, P["out"]); b.Set(31, y, P["out"]); }
        if (win) { b.Rect(12, 8, 8, 16, P["out"]); b.Rect(13, 9, 6, 14, P["wa1"]); b.Rect(13, 9, 1, 14, P["wa3"]); b.Rect(15, 9, 2, 14, P["w2"]); }
        return b;
    }

    /// <summary>Wattle-and-daub wall (front view like the other walls). kind: "" | "win" | "door".</summary>
    public PixelBuffer WallWattle(string kind)
    {
        var b = new PixelBuffer(32, 32);
        for (int j = 0; j < 8; j++)
            for (int i = 0; i < 32; i++)
                b.Set(i, j, R["dirt"][j == 0 ? 3 : j == 7 ? 0 : 2]);
        for (int j = 8; j < 32; j++)
            for (int i = 0; i < 32; i++)
                b.Set(i, j, Hash(i, j, 17) < .22 ? P["pl1"] : P["pl2"]);
        // exposed wattle weave in patches, timber studs
        for (int p = 0; p < 4; p++)
        {
            int x0 = 3 + p * 8, y0 = 12 + (p % 2) * 7;
            for (int j = 0; j < 7; j++) for (int i = 0; i < 6; i++) b.Set(x0 + i, y0 + j, (i + j) % 2 == 0 ? P["d3"] : P["d2"]);
        }
        foreach (int x in new[] { 0, 10, 21, 30 }) b.Rect(x, 8, 2, 24, P["w2"]);
        b.Rect(0, 8, 32, 2, P["w2"]);
        b.Rect(0, 29, 32, 2, P["w1"]);
        if (kind == "win")
        {
            b.Rect(12, 14, 9, 9, P["out"]); b.Rect(13, 15, 7, 7, P["wa1"]); b.Rect(13, 15, 7, 1, P["wa2"]); b.Rect(16, 15, 1, 7, P["w2"]);
        }
        for (int i = 0; i < 32; i++) b.Set(i, 31, P["out"]);
        return b;
    }

    /// <summary>Doorway in a wall running east-west: you pass north-south. Jambs left and right, open leaf against the left jamb.</summary>
    public PixelBuffer DoorH()
    {
        var b = new PixelBuffer(32, 32);
        Planks(b);
        b.Rect(0, 0, 5, 32, P["w1"]); b.Rect(27, 0, 5, 32, P["w1"]);
        b.Rect(0, 0, 2, 32, P["w3"]); b.Rect(27, 0, 2, 32, P["w3"]);
        b.Rect(5, 0, 1, 32, P["out"]); b.Rect(26, 0, 1, 32, P["out"]);
        b.Rect(6, 3, 3, 22, P["w4"]); b.Rect(6, 3, 1, 22, P["w5"]); b.Rect(8, 3, 1, 22, P["w2"]); b.Set(7, 20, P["au3"]);
        b.Rect(5, 0, 22, 2, P["w2"]);
        b.Rect(5, 30, 22, 2, P["w1"]); // threshold
        return b;
    }

    /// <summary>Doorway in a wall running north-south: you pass east-west. Lintel above, sill below, open leaf against the top jamb.</summary>
    public PixelBuffer DoorV()
    {
        var b = new PixelBuffer(32, 32);
        Planks(b);
        b.Rect(0, 0, 32, 8, R["wood"][2]); b.Rect(0, 0, 32, 1, P["w4"]); b.Rect(0, 7, 32, 1, P["out"]);
        b.Rect(0, 25, 32, 7, R["wood"][1]); b.Rect(0, 25, 32, 1, P["out"]); b.Rect(0, 31, 32, 1, P["out"]);
        b.Rect(4, 8, 22, 3, P["w4"]); b.Rect(4, 8, 22, 1, P["w5"]); b.Rect(4, 10, 22, 1, P["w2"]); b.Set(22, 9, P["au3"]);
        for (int x = 0; x < 32; x += 4) b.Set(x, 24, P["w1"]);
        return b;
    }

    private void Planks(PixelBuffer b)
    {
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                int k = 2; if (y % 8 == 0) k = 3; else if (y % 8 == 7) k = 0;
                if ((x + (y / 8) * 11) % 32 == 0) k = 0;
                b.Set(x, y, R["wood"][k]);
            }
    }

    private void FloorBase(PixelBuffer b)
    {
        Planks(b);
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) if ((x + y) % 2 == 0 && Hash(x, y, 20) < .1) b.Set(x, y, P["w1"]);
    }

    /// <summary>Stairs going up: pale treads climbing toward the top of the tile, side rails, up chevron.</summary>
    public PixelBuffer Stairs(bool up)
    {
        var b = new PixelBuffer(32, 32);
        FloorBase(b);
        b.Rect(2, 1, 28, 30, P["out"]);
        for (int s = 0; s < 7; s++)
        {
            int y = 2 + s * 4;
            int lvl = up ? 6 - s : s; // up: bright treads at the top, dark steps below
            int tread = Math.Min(4, 1 + lvl * 4 / 7 + (up ? 1 : 0));
            uint tc = up ? R["wood"][Math.Min(4, 2 + (6 - s) / 3 + 0)] : R["wood"][Math.Max(0, 3 - s / 2)];
            b.Rect(3, y, 26, 3, tc);
            b.Rect(3, y + 3, 26, 1, up ? P["w1"] : P["out"]);
            if (up) b.Rect(3, y, 26, 1, P["w5"]);
        }
        b.Rect(2, 1, 2, 30, P["w2"]); b.Rect(28, 1, 2, 30, P["w1"]); b.Rect(2, 1, 1, 30, P["w4"]);
        uint arrow = up ? P["f4"] : P["f3"];
        int cx = 16;
        if (up) { for (int i = 0; i < 5; i++) { b.Set(cx - i, 12 + i, arrow); b.Set(cx + i, 12 + i, arrow); } }
        else { for (int i = 0; i < 5; i++) { b.Set(cx - i, 20 - i, arrow); b.Set(cx + i, 20 - i, arrow); } }
        return b;
    }

    public PixelBuffer Ladder() => Obj(32, 32, b =>
    {
        b.Rect(8, 1, 3, 30, P["w3"]); b.Rect(21, 1, 3, 30, P["w3"]); b.Rect(8, 1, 1, 30, P["w5"]); b.Rect(23, 1, 1, 30, P["w1"]);
        for (int y = 4; y < 30; y += 5) { b.Rect(11, y, 10, 2, P["w4"]); b.Rect(11, y + 1, 10, 1, P["w2"]); }
    });

    public PixelBuffer Trapdoor() => Obj(32, 32, b =>
    {
        b.Shade(3, 3, 26, 26, R["wood"], Top);
        b.Rect(3, 10, 26, 1, P["w1"]); b.Rect(3, 18, 26, 1, P["w1"]); b.Rect(3, 25, 26, 1, P["w1"]);
        b.Rect(4, 7, 4, 2, P["m2"]); b.Rect(24, 7, 4, 2, P["m2"]);
        b.Ellipse(16, 20, 3, 3, P["m3"], false); b.Set(16, 17, P["m4"]);
    });

    public PixelBuffer Workbench() => Obj(32, 32, b =>
    {
        b.Shade(2, 9, 28, 11, R["wood"], Top); b.Rect(2, 19, 28, 2, P["w1"]); b.Rect(4, 21, 3, 8, P["w2"]); b.Rect(25, 21, 3, 8, P["w2"]);
        b.Rect(10, 12, 8, 1, P["m3"]); b.Rect(10, 13, 2, 3, P["w1"]); // saw
        b.Rect(21, 11, 5, 3, P["w4"]); b.Rect(22, 11, 3, 1, P["w5"]);  // plank
        b.Rect(5, 11, 3, 2, P["m4"]); b.Set(6, 13, P["w2"]);           // chisel
    });

    public PixelBuffer Brazier(bool lit) => Obj(32, 32, b =>
    {
        b.Rect(15, 18, 2, 10, P["m2"]); b.Rect(11, 27, 10, 2, P["m2"]);
        b.Ellipse(16, 17, 8, 4, P["m2"], true); b.Ellipse(16, 17, 6, 3, P["dk2"], true); b.Rect(8, 16, 16, 1, P["m3"]);
        if (lit)
        {
            string[] f = { "f1", "f2", "f3", "f4" };
            for (int i = 11; i < 21; i++)
            {
                int h = 3 + (int)(Hash(i, 3, 5) * 6) - Math.Abs(i - 16) / 2;
                for (int j = 0; j < h; j++) b.Set(i, 17 - j, P[f[Math.Min(3, j * 4 / Math.Max(1, h))]]);
            }
        }
    });

    public PixelBuffer Void()
    {
        var b = new PixelBuffer(32, 32);
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) b.Set(x, y, Hash(x, y, 31) < .12 ? P["dk2"] : P["dk1"]);
        return b;
    }
}
