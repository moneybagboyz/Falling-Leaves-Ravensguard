using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

/// <summary>
/// Fire and burning (docs/specs/phase6/fire-and-burning.md 6): transparent 32x32 overlays laid over the burning cell, in exact palette colours only (the game adds the glow and the smoke's
/// translucency as a layer modulate): flames at three intensities in four looping frames, smoke puffs in four frames, and what a fire leaves on the ground (char, ash, cracked daub,
/// smouldering turf, scorch, charred rubble). Also the 16x16 bucket and fire hook.
/// </summary>
public sealed class Fire
{
    private readonly ColorLookup P;
    private readonly RampLookup R;
    public Fire(Palette pal) { P = pal.P; R = pal.R; }
    public Fire(ColorLookup p, RampLookup r) { P = p; R = r; }

    private static double Periodic(int x, int y, int seed, int cells)
    {
        double fx = x * cells / 32.0, fy = y * cells / 32.0; int xi = (int)Math.Floor(fx), yi = (int)Math.Floor(fy);
        double xf = fx - xi, yf = fy - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
        double H(int a, int c) => Hash(((a % cells) + cells) % cells, ((c % cells) + cells) % cells, seed);
        double A = H(xi, yi), B = H(xi + 1, yi), C = H(xi, yi + 1), D = H(xi + 1, yi + 1);
        return A + (B - A) * u + (C - A) * v + (A - B - C + D) * u * v;
    }

    /// <summary>Flames rising from the bottom of the cell: intensity 1 is a few low licks, 2 a bright bed of fire, 3 a roaring column to the top. Frames flicker and loop seamlessly (frame 0 follows frame 3).</summary>
    public PixelBuffer Flame(int intensity, int frame)
    {
        var b = new PixelBuffer(32, 32);
        double maxH = intensity switch { 1 => 11, 2 => 19, _ => 29 };
        double span = intensity switch { 1 => 11.5, 2 => 14.0, _ => 15.5 };                  // half-width of the burning bed
        double ph = frame * Math.PI / 2;                                                     // every harmonic below is an integer multiple, so frame 4 equals frame 0
        for (int x = 0; x < 32; x++)
        {
            double u = (x - 15.5) / span;
            if (Math.Abs(u) >= 1) continue;
            double env = 1 - u * u * u * u;                                                   // a flat-topped bed that falls off at the edges
            double wob = 0.5 + 0.28 * Math.Sin(x * 0.9 + ph * 1) + 0.2 * Math.Sin(x * 1.7 - ph * 2 + intensity) + 0.14 * Math.Sin(x * 0.45 + ph * 1 + 2.0) + 0.12 * (Hash(x, intensity, 1401) - .5);
            double hh = Math.Max(2, maxH * env * Math.Clamp(wob + 0.2, 0.15, 1.0));
            int top = 31 - (int)Math.Round(hh);
            for (int y = Math.Max(0, top); y < 32; y++)
            {
                double rel = (31 - y) / Math.Max(1.0, hh);                                    // 0 at the base, 1 at the tip
                double core = env * (1 - Math.Abs(u) * .4);
                uint c = rel > .86 ? (Hash(x, y, 1403) < .6 ? P["f1"] : P["f2"])
                       : rel > .62 ? P["f2"]
                       : rel > .3 || core < .55 ? (core > .6 ? P["f3"] : P["f2"])
                       : core > .5 ? P["f4"] : P["f3"];
                if (rel > .93 && Hash(x, y, 1404 + frame) < .45) continue;                    // ragged tips
                b.Set(x, y, c);
            }
        }
        // embers and a few sparks above the flame at the higher intensities
        if (intensity >= 2)
            for (int i = 0; i < intensity * 2; i++)
            {
                int ex = 4 + (int)(Hash(i, 3, 1411) * 24), ey = 31 - (int)maxH - 2 - (int)(Hash(i, 5, 1412) * 4) - ((frame + i) % 4) * 2;
                if (ey >= 0 && ey < 32 && !b.Opaque(ex, ey)) b.Set(ex, ey, i % 2 == 0 ? P["f3"] : P["f4"]);
            }
        return b;
    }

    /// <summary>A plume of smoke: grey blobs that rise and spread over four looping frames, dithered thin toward the top.</summary>
    public PixelBuffer Smoke(int frame)
    {
        var b = new PixelBuffer(32, 32);
        for (int i = 0; i < 5; i++)
        {
            double t = ((frame + i * 0.8) % 4) / 4.0;                                  // each blob's life, 0 low to 1 high
            double cx = 8 + i * 4 + Math.Sin(t * Math.PI * 2 + i * 1.3) * 3, cy = 28 - t * 25, r = 4.5 + t * 5.5;
            for (int y = Math.Max(0, (int)(cy - r) - 1); y < Math.Min(32, (int)(cy + r) + 2); y++)
                for (int x = Math.Max(0, (int)(cx - r) - 1); x < Math.Min(32, (int)(cx + r) + 2); x++)
                {
                    double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                    if (d > 1) continue;
                    if (Hash(x, y, 1421 + i) < t * t * 0.7) continue;                   // the higher the thinner: more holes
                    b.Set(x, y, d > 0.8 ? P["s2"] : d > 0.45 ? P["s3"] : (x + y) % 2 == 0 ? P["s3"] : P["s4"]);
                }
        }
        return b;
    }

    /// <summary>Burnt ground: a ragged black patch with a few dull coals. 0 and 1 differ in shape.</summary>
    public PixelBuffer Char(int v)
    {
        var b = new PixelBuffer(32, 32); int s = 1431 + v * 17;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double n = Periodic(x, y, s, 4) * .7 + Hash(x, y, s + 1) * .3;
                if (n < .30 + (v == 1 ? .06 : 0)) continue;
                double l = Periodic(x, y, s + 5, 4);
                b.Set(x, y, l < .3 ? P["dk1"] : l < .62 ? P["dk2"] : l < .86 ? P["s1"] : P["w1"]);
                if (Hash(x, y, s + 9) < .012) b.Set(x, y, P["f1"]);
            }
        return b;
    }

    /// <summary>Ash: a pale grey drift over scorched ground.</summary>
    public PixelBuffer Ash(int v)
    {
        var b = new PixelBuffer(32, 32); int s = 1441 + v * 13;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double n = Periodic(x, y, s, 4) * .7 + Hash(x, y, s + 1) * .3;
                if (n < .36) continue;
                double l = Periodic(x, y, s + 5, 4);
                b.Set(x, y, l < .3 ? P["s2"] : l < .65 ? P["s3"] : l < .9 ? P["s4"] : P["s5"]);
            }
        return b;
    }

    /// <summary>Soot on stone that did not burn: a faint scorch climbing from the base.</summary>
    public PixelBuffer Scorch()
    {
        var b = new PixelBuffer(32, 32);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double h = (y - 8) / 24.0;
                if (h <= 0 || Hash(x, y, 1451) > h * .95) continue;
                b.Set(x, y, h > .7 ? P["dk2"] : P["s1"]);
            }
        return b;
    }

    /// <summary>Cracked daub: a net of dark cracks over a brown stain.</summary>
    public PixelBuffer Cracked()
    {
        var b = new PixelBuffer(32, 32);
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) if (Periodic(x, y, 1461, 4) > .5) b.Set(x, y, Hash(x, y, 1462) < .4 ? P["br2"] : P["br3"]);
        b.Line(3, 4, 14, 12, P["dk1"]); b.Line(14, 12, 12, 24, P["dk1"]); b.Line(14, 12, 27, 9, P["dk1"]); b.Line(20, 18, 28, 28, P["dk1"]); b.Line(5, 26, 12, 24, P["dk2"]);
        return b;
    }

    /// <summary>Turf that smoulders: a dull dark mat with the odd red coal and a thin line of ash.</summary>
    public PixelBuffer Smoulder()
    {
        var b = new PixelBuffer(32, 32);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                if (Periodic(x, y, 1471, 4) < .38) continue;
                double l = Hash(x, y, 1472);
                b.Set(x, y, l < .07 ? P["f1"] : l < .45 ? P["dk2"] : l < .8 ? P["s1"] : P["s2"]);
            }
        return b;
    }

    /// <summary>What is left of a fallen building: charred beams over black rubble. A prop-sized pile.</summary>
    public PixelBuffer CharredRubble()
    {
        var b = new PixelBuffer(32, 32);
        for (int y = 14; y < 31; y++)
            for (int x = 2; x < 30; x++)
            {
                double dx = (x - 16) / 14.0, dy = (y - 24) / 8.0;
                if (dx * dx + dy * dy > 1 + (Hash(x, y, 1481) - .5) * .5) continue;
                double l = Hash(x, y, 1482);
                b.Set(x, y, l < .3 ? P["dk1"] : l < .55 ? P["dk2"] : l < .8 ? P["s1"] : P["s2"]);
            }
        b.Line(4, 22, 20, 14, P["w1"]); b.Line(5, 23, 21, 15, P["dk2"]); b.Line(12, 27, 28, 19, P["w1"]); b.Line(13, 28, 29, 20, P["dk2"]); b.Line(8, 18, 25, 26, P["w2"]);
        b.Set(9, 20, P["f1"]); b.Set(22, 23, P["f1"]); b.Set(16, 26, P["f2"]);
        return b;
    }

    // ---- 16x16 items (the bucket and the fire hook of the response, fire 4.4) ----

    public PixelBuffer Bucket(bool full)
    {
        var b = new PixelBuffer(16, 16);
        for (int y = 5; y < 13; y++) { int w = 5 + (y - 5) / 3; for (int x = 8 - w; x < 8 + w; x++) b.Set(x, y, (x + y) % 5 == 0 ? P["w2"] : x < 8 ? P["w3"] : P["w2"]); }
        b.Rect(3, 7, 10, 1, P["m3"]); b.Rect(3, 11, 10, 1, P["m3"]);
        b.Line(4, 5, 8, 1, P["m4"]); b.Line(8, 1, 12, 5, P["m4"]);
        if (full) b.Rect(4, 5, 8, 1, P["wa3"]);
        return b;
    }

    public PixelBuffer Hook()
    {
        var b = new PixelBuffer(16, 16);
        b.Line(3, 14, 12, 3, P["w3"]); b.Line(4, 14, 13, 3, P["w2"]);
        b.Line(12, 3, 14, 5, P["m4"]); b.Line(14, 5, 12, 7, P["m4"]); b.Line(12, 7, 11, 6, P["m3"]); b.Set(13, 2, P["m5"]);
        return b;
    }
}
