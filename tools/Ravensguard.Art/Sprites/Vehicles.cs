using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

public enum HitchStyle { None, Pole, Shafts, Traces }
public enum BedStyle { Plank, Rack, Box, Tray, Runner }

/// <summary>
/// One template vehicle drawn at its footprint (horses-and-carts spec 3.3, 6). The world is 32 px to the metre, so a vehicle of <c>Body + Hitch</c> rows of <c>Cols</c> cells is exactly
/// <c>(Body + Hitch) * 32</c> px long and <c>Cols * 32</c> px across on the ground. The view is top-down with a slight tilt toward the viewer: the ground rectangle sits at the bottom of the sprite and everything
/// that stands above it (the bed, the cover, the load, the wheels' tops) is lifted up the sprite by its height, so the near side of the box and the near wheels show. Facing east; the game mirrors it for west.
/// The sprite is <c>4 px</c> of margin wider on each side than the footprint and <c>4 px</c> taller at the bottom; the rest of the height is the rise above the ground rectangle.
/// </summary>
public sealed record VehicleSpec(string Id, int Cols, int Body, int Hitch, HitchStyle Style, BedStyle Bed, bool Cover, int[] AxleRows, int SeatRow, bool Spoked, int Rise);

public sealed class Vehicles
{
    public const int Cell = 32, Pad = 4;
    private readonly ColorLookup P;
    private readonly RampLookup R;
    private static readonly ShadeOptions Top = new() { TopHi = true };
    public Vehicles(Palette pal) { P = pal.P; R = pal.R; }

    /// <summary>The templates, row for row with <c>content/base/vehicles/templates.jsonc</c> (a test compares each sprite's size with its template's rows).</summary>
    public static readonly VehicleSpec[] Specs =
    [
        new("wheelbarrow",   1, 2, 0, HitchStyle.None,   BedStyle.Tray,   false, [], -1, false, 24),
        new("handcart",      1, 2, 0, HitchStyle.None,   BedStyle.Box,    false, [1], -1, false, 26),
        new("cart",          2, 3, 2, HitchStyle.Shafts, BedStyle.Plank,  false, [1], 2, true, 30),
        new("hay_wain",      2, 4, 3, HitchStyle.Pole,   BedStyle.Rack,   false, [1, 3], 3, true, 52),
        new("ox_wagon",      2, 5, 3, HitchStyle.Pole,   BedStyle.Plank,  false, [1, 3], 4, true, 32),
        new("covered_wagon", 2, 5, 2, HitchStyle.Pole,   BedStyle.Plank,  true,  [1, 3], 4, true, 48),
        new("sledge",        2, 2, 1, HitchStyle.Traces, BedStyle.Runner, false, [], -1, false, 24),
    ];

    public static (int W, int H) Size(VehicleSpec s) => ((s.Body + s.Hitch) * Cell + 2 * Pad, s.Rise + s.Cols * Cell + Pad);

    public PixelBuffer Draw(VehicleSpec s, bool loaded = false)
    {
        var (w, h) = Size(s); var b = new PixelBuffer(w, h);
        int gx = Pad, gy = s.Rise, gw = (s.Body + s.Hitch) * Cell, gh = s.Cols * Cell, cy = gy + gh / 2, bx1 = gx + s.Body * Cell;
        // heights above the ground, in px: the deck, the walls on it, and the clearance under the box
        int floor = s.Bed == BedStyle.Runner ? 7 : s.Bed is BedStyle.Tray ? 9 : s.Cols == 1 ? 8 : 16, wall = s.Bed == BedStyle.Runner ? 6 : s.Cols == 1 ? 5 : s.Bed == BedStyle.Rack ? 6 : 8;
        int inset = s.Cols == 1 ? 4 : 3;
        int yTop = gy + inset, yBot = gy + gh - inset;                       // the box's ground edges
        if (s.Bed == BedStyle.Tray) { BarrowDraw(b, s, gx, gy, gw, gh, cy); return b.Outline(P["out"]); }

        Hitch(b, s, gx, gy, gw, gh, cy, bx1);
        if (s.Bed == BedStyle.Runner) Runner(b, gx, bx1, gy, gh);
        // the box: bed rows, then the driver's deck
        int seatX = s.SeatRow >= 0 ? gx + s.SeatRow * Cell : bx1;           // where the deck starts
        int boxX0 = gx + 2, boxX1 = seatX - 1;
        Box(b, s, boxX0, boxX1, yTop, yBot, floor, wall);
        if (s.SeatRow >= 0) Deck(b, seatX, bx1 - 2, yTop, yBot, floor);
        if (s.Bed == BedStyle.Rack) Hay(b, boxX0, boxX1, yTop, yBot, floor, wall);
        else if (loaded && !s.Cover) LoadEast(b, boxX0, boxX1, yTop - floor - wall, yBot - floor - wall);
        if (s.Cover) Cover(b, boxX0, boxX1, yTop, yBot, floor + wall, cy);
        foreach (int row in s.AxleRows) Wheel(b, gx + row * Cell + Cell / 2, yBot + inset - 1, s.Cols == 1 ? 9 : 14, s.Cols == 1 ? 8 : 11, s.Spoked);
        return b.Outline(P["out"]);
    }

    // ---------------------------------------------------------------------------------------------- north and south

    /// <summary>The sprite size of a vehicle seen head-on (north or south): the footprint turned a quarter, plus the same margin and rise.</summary>
    public static (int W, int H) SizeNS(VehicleSpec s) => (s.Cols * Cell + 2 * Pad, s.Rise + (s.Body + s.Hitch) * Cell + Pad);

    /// <summary>
    /// The vehicle facing north (away from the viewer: the hitch is at the top, the rear toward us) or south (toward the viewer: the hitch at the bottom). The same top-down view with a slight tilt as <see cref="Draw"/>:
    /// the ground rectangle sits at the bottom of the sprite, the bed and its load are lifted by their height, the wheels are seen end-on as upright strips either side, and the near face of the bed shows on the side
    /// nearest the viewer. <paramref name="loaded"/> draws sacks and a crate on the bed.
    /// </summary>
    public PixelBuffer DrawNS(VehicleSpec s, bool north, bool loaded)
    {
        var (w, h) = SizeNS(s); var b = new PixelBuffer(w, h);
        int len = (s.Body + s.Hitch) * Cell, wid = s.Cols * Cell, gx = Pad, gy = s.Rise, cx = gx + wid / 2;
        int bodyLen = s.Body * Cell, hitchLen = s.Hitch * Cell;
        int bodyY0 = north ? gy + hitchLen : gy, bodyY1 = bodyY0 + bodyLen;                       // the body's ground rows
        int hitchY0 = north ? gy : bodyY1, hitchY1 = hitchY0 + hitchLen;
        int rearY = north ? gy + len : gy, dir = north ? -1 : 1;                                   // the rear end, and which way the rows count from it
        int floor = s.Bed == BedStyle.Runner ? 7 : s.Bed is BedStyle.Tray ? 9 : s.Cols == 1 ? 8 : 16, wall = s.Bed == BedStyle.Runner ? 6 : s.Cols == 1 ? 5 : s.Bed == BedStyle.Rack ? 6 : 8;
        int inset = s.Cols == 1 ? 4 : 3, t = floor + wall, x0 = gx + inset, x1 = gx + wid - inset, bw = x1 - x0;
        if (s.Bed == BedStyle.Tray) { BarrowNS(b, s, north, loaded, gx, gy, wid, len, cx); return b.Outline(P["out"]); }

        // the hitch lies on the ground ahead of the body, a hand above it
        switch (s.Style)
        {
            case HitchStyle.Pole:
                b.Shade(cx - 2, hitchY0 - 12, 5, hitchLen, R["wood"], Top); b.Rect(cx + 2, hitchY0 - 12, 1, hitchLen, P["w1"]);
                int yokeY = north ? hitchY0 - 12 : hitchY1 - 12 - 4;
                b.Shade(cx - 18, yokeY, 36, 4, R["wood"], Top); b.Rect(cx - 9, yokeY + 1, 2, 3, P["m3"]); b.Rect(cx + 7, yokeY + 1, 2, 3, P["m3"]);
                break;
            case HitchStyle.Shafts:
                foreach (int sx in new[] { cx - 11, cx + 9 }) { b.Shade(sx, hitchY0 - 10, 3, hitchLen, R["wood"], Top); b.Rect(sx + 2, hitchY0 - 10, 1, hitchLen, P["w1"]); b.Rect(sx - 1, north ? hitchY0 - 10 : hitchY1 - 12, 5, 3, P["m3"]); }
                break;
            case HitchStyle.Traces:
                foreach (int sx in new[] { x0 + 4, x1 - 5 }) b.Line(sx, north ? bodyY0 - 6 : bodyY1 - 6, cx, north ? hitchY0 - 4 : hitchY1 - 6, P["br3"]);
                b.Rect(cx - 2, north ? hitchY0 - 6 : hitchY1 - 8, 4, 4, P["m3"]);
                break;
            case HitchStyle.None when s.Bed == BedStyle.Box:                                           // a handcart's handles at the rear
                foreach (int sx in new[] { x0 + 4, x1 - 5 }) { b.Shade(sx, north ? rearY - 15 : rearY - 8, 2, 12, R["wood"], Top); }
                break;
        }
        if (s.Bed == BedStyle.Runner) foreach (int sx in new[] { x0 + 2, x1 - 6 }) { b.Shade(sx, gy + 2, 4, len - 4, R["wood"], Top); b.Rect(sx + 3, gy + 2, 1, len - 4, P["m2"]); }
        // wheels: seen end-on as upright strips either side of the body, one per axle row counted from the rear
        foreach (int row in s.AxleRows)
        {
            int ay = rearY + dir * (row * Cell + Cell / 2), r = s.Cols == 1 ? 8 : 12;
            foreach (int wx in new[] { gx - 3, gx + wid - 2 })
            {
                b.Shade(wx, ay - r - 2, 5, 2 * r + 2, R["wood"], Top); b.Rect(wx, ay - r - 2, 5, 2, P["m2"]); b.Rect(wx, ay + r - 2, 5, 2, P["m2"]); b.Rect(wx + 2, ay - 1, 1, 3, P["m3"]);
                if (s.Spoked) for (int yy = ay - r + 3; yy < ay + r - 2; yy += 4) b.Rect(wx + 1, yy, 3, 1, P["w4"]);
            }
            b.Rect(gx + 1, ay - 1, wid - 2, 2, P["m2"]);                                              // the axle
        }
        // the bed: the frame, the floor inside, the end boards, the near face and the rim
        int yTop = bodyY0 - t, yBot = bodyY1 - t;
        b.Rect(x0 + 2, bodyY1 - 6, bw - 4, 2, P["w1"]);                                               // the frame under the box
        b.Rect(x0, yTop, bw, yBot - yTop + 1, P["w2"]);
        b.Shade(x0 + 2, yTop + 2, bw - 4, yBot - yTop - 3, R["wood"]);
        for (int y = yTop + 6; y < yBot - 2; y += 6) b.Rect(x0 + 2, y, bw - 4, 1, P["w2"]);
        b.Shade(x0, yTop, 3, yBot - yTop + 3, R["wood"], Top); b.Shade(x1 - 3, yTop, 3, yBot - yTop + 3, R["wood"], Top);
        if (s.SeatRow >= 0)                                                                            // the driver's bench across the front end of the body
        {
            int sy = (north ? bodyY0 : bodyY1) - (north ? 0 : 10) - floor - 4;
            b.Shade(x0 + 3, sy, bw - 6, 7, R["wood"], Top); b.Rect(x0 + 3, sy + 5, bw - 6, 1, P["w1"]);
        }
        if (loaded && !s.Cover) LoadNS(b, x0, x1, yTop, yBot);
        if (s.Cover) { b.Shade(x0 + 1, yTop - 10, bw - 2, yBot - yTop + 10, R["linen"], Top); for (int y = yTop - 6; y < yBot; y += 10) b.Rect(x0 + 1, y, bw - 2, 1, P["ln1"]); b.Rect(cx - 1, yTop - 10, 2, yBot - yTop + 10, P["ln3"]); }
        // the face toward the viewer (south side) and the rims
        int faceTop = yBot + 1, faceBot = bodyY1 - floor + 1;
        if (faceBot > faceTop) { Plank(b, x0, faceTop, bw, faceBot - faceTop, 1); }
        b.Rect(x0, yBot, bw, 2, P["w5"]); b.Rect(x0, yTop, bw, 2, P["w5"]);
        return b.Outline(P["out"]);
    }

    /// <summary>A wheelbarrow seen head-on: the tray with its load, the wheel in front, the handles behind.</summary>
    private void BarrowNS(PixelBuffer b, VehicleSpec s, bool north, bool loaded, int gx, int gy, int wid, int len, int cx)
    {
        int rear = north ? gy + len - 6 : gy + 6, front = north ? gy + 8 : gy + len - 6;
        foreach (int sx in new[] { cx - 9, cx + 8 }) b.Shade(sx, Math.Min(rear, rear + (north ? -14 : 0)) , 2, 14, R["wood"], Top);
        int trayY = north ? rear - 30 : rear + 2;
        b.Shade(cx - 11, trayY - 9, 22, 28, R["wood"], Top); b.Rect(cx - 9, trayY - 7, 18, 24, P["w2"]);
        if (loaded) { b.Shade(cx - 8, trayY - 12, 16, 12, R["linen"], Top); b.Shade(cx - 3, trayY - 16, 10, 8, R["wood"], Top); }
        b.Ellipse(cx, front - 6, 3, 7, P["w3"], true); b.Rect(cx - 1, front - 8, 2, 4, P["m2"]);
    }

    private void LoadNS(PixelBuffer b, int x0, int x1, int yTop, int yBot)
    {
        int bw = x1 - x0, mid = (yTop + yBot) / 2;
        b.Shade(x0 + 3, mid - 4, bw / 2 - 4, 14, R["linen"], Top); b.Rect(x0 + 6, mid - 2, 5, 1, P["ln1"]);                 // two sacks side by side
        b.Shade(x0 + bw / 2 + 1, mid - 6, bw / 2 - 4, 16, R["linen"], Top); b.Rect(x0 + bw / 2 + 5, mid - 3, 5, 1, P["ln1"]);
        b.Shade(x0 + bw / 4, yTop + 2, bw / 2, 12, R["wood"], Top); b.Rect(x0 + bw / 4, yTop + 7, bw / 2, 1, P["m2"]);        // a crate behind
    }

    /// <summary>The load seen from the side: sacks along the bed and a crate on top, standing above the rim.</summary>
    private void LoadEast(PixelBuffer b, int x0, int x1, int rimTop, int rimBot)
    {
        int bw = x1 - x0, n = Math.Max(2, bw / 20);
        for (int k = 0; k < n; k++)
        {
            int sx = x0 + 3 + k * (bw - 6) / n;
            b.Shade(sx, rimTop - 3 + (k % 2) * 2, (bw - 6) / n - 2, rimBot - rimTop + 1, R["linen"], Top); b.Rect(sx + 3, rimTop - 1 + (k % 2) * 2, 4, 1, P["ln1"]);
        }
        b.Shade(x0 + bw / 3, rimTop - 8, bw / 3, 8, R["wood"], Top); b.Rect(x0 + bw / 3, rimTop - 4, bw / 3, 1, P["m2"]);
    }

    // ---------------------------------------------------------------------------------------------- parts

    private void Plank(PixelBuffer b, int x, int y, int w, int h, int lines = 0)
    {
        if (w <= 0 || h <= 0) return;
        b.Shade(x, y, w, h, R["wood"], Top);
        for (int i = 1; i <= lines; i++) b.Rect(x, y + i * h / (lines + 1), w, 1, P["w1"]);
    }

    /// <summary>The box seen from the near side: its inside (the far wall's inner face and the floor), the rim, and the near face down to the axle.</summary>
    private void Box(PixelBuffer b, VehicleSpec s, int x0, int x1, int yTop, int yBot, int floor, int wall)
    {
        int t = floor + wall, rimTop = yTop - t, rimBot = yBot - t, bw = x1 - x0;
        if (bw <= 4) return;
        if (s.Bed == BedStyle.Box)
        {
            b.Rect(x0, rimTop, bw, rimBot - rimTop + 1, P["w1"]);                                           // a small open box: inside dark, planks in the near face
            b.Rect(x0 + 2, rimTop + 2, bw - 4, rimBot - rimTop - 3, P["w2"]);
            for (int x = x0 + 3; x < x1 - 3; x += 5) b.Rect(x, rimTop + 3, 1, rimBot - rimTop - 5, P["w1"]);
            Plank(b, x0, rimBot - 1, bw, yBot - 4 - rimBot + 1, 1);
            b.Rect(x0, rimTop, bw, 2, P["w5"]);
            return;
        }
        // far wall's inner face, then the floor
        b.Rect(x0, rimTop, bw, wall + 2, P["w2"]); b.Rect(x0, rimTop + wall - 1, bw, 1, P["w1"]);
        b.Shade(x0 + 2, rimTop + wall, bw - 4, rimBot - rimTop - wall - 2, R["wood"]);
        for (int y = rimTop + wall + 5; y < rimBot - 3; y += 6) b.Rect(x0 + 2, y, bw - 4, 1, P["w2"]);
        if (s.Bed == BedStyle.Plank && s.Cover == false) for (int x = x0 + 8; x < x1 - 8; x += 24) b.Rect(x, rimTop + wall, 1, rimBot - rimTop - wall - 3, P["w2"]);
        // end boards
        b.Shade(x0, rimTop, 3, rimBot - rimTop + 3, R["wood"], Top); b.Shade(x1 - 3, rimTop, 3, rimBot - rimTop + 3, R["wood"], Top);
        // near face and rim
        Plank(b, x0, rimBot - 2, bw, yBot - 5 - (rimBot - 2) + 1, 2);
        b.Rect(x0, rimBot - 2, bw, 3, P["w5"]); b.Rect(x0, rimBot, bw, 1, P["w4"]);
        b.Rect(x0, rimTop, bw, 2, P["w5"]);
        for (int x = x0 + 12; x < x1 - 6; x += 32) b.Rect(x, rimBot + 1, 1, yBot - 6 - rimBot, P["w1"]);   // stakes
        b.Rect(x0 + 2, yBot - 6, bw - 4, 2, P["w1"]);                                                          // the frame under the box
    }

    /// <summary>The driver's platform in front of the bed: a deck with a bench across it.</summary>
    private void Deck(PixelBuffer b, int x0, int x1, int yTop, int yBot, int floor)
    {
        int bw = x1 - x0; if (bw <= 4) return;
        b.Shade(x0, yTop - floor, bw, yBot - yTop + 1, R["wood"], Top);
        for (int y = yTop - floor + 5; y < yBot - floor; y += 6) b.Rect(x0, y, bw, 1, P["w2"]);
        Plank(b, x0, yBot - floor + 1, bw, floor - 5, 1);                                                      // the deck's near face
        int bx = x0 + 9, bh = 9;                                                                                // the bench: a plank across the deck, raised
        b.Shade(bx, yTop - floor - 3, 10, yBot - yTop + 1, R["wood"], Top);
        b.Rect(bx, yBot - floor - 3 + 1, 10, bh - 6, P["w3"]); b.Rect(bx, yBot - floor - 2 + bh - 6, 10, 1, P["w1"]);
        b.Rect(bx - 1, yBot - floor - 3 + 1 + bh - 5, 2, 4, P["w1"]); b.Rect(bx + 9, yBot - floor - 3 + 1 + bh - 5, 2, 4, P["w1"]);
    }

    private void Wheel(PixelBuffer b, int cx, int bottom, int rx, int ry, bool spoked)
    {
        int cy = bottom - ry;
        if (spoked)
        {
            b.Ellipse(cx, cy, rx, ry, P["m2"], true);                                                          // iron tyre
            b.Ellipse(cx, cy, rx - 2, ry - 2, P["w3"], true);                                                  // wooden felloe
            b.Ellipse(cx, cy, rx - 4, ry - 4, P["w1"], true);                                                  // dark between the spokes
            for (int i = 0; i < 12; i++)
            {
                double a = i * Math.PI / 6;
                b.Line(cx, cy, Round(cx + Math.Cos(a) * (rx - 4)), Round(cy + Math.Sin(a) * (ry - 4)), P["w4"]);
            }
            b.Ellipse(cx, cy, 3, 3, P["w2"], true); b.Rect(cx - 1, cy - 1, 3, 3, P["m3"]);                      // hub and nave
            b.Set(cx - rx + 1, cy - 2, P["m4"]); b.Set(cx - rx + 2, cy - ry + 3, P["m4"]);
        }
        else
        {
            b.Ellipse(cx, cy, rx, ry, P["w3"], true); b.Ellipse(cx, cy, rx - 1, ry - 1, P["w4"], true);
            b.Rect(cx - rx + 2, cy, 2 * rx - 3, 1, P["w2"]); b.Rect(cx, cy - ry + 2, 1, 2 * ry - 3, P["w2"]);
            b.Rect(cx - 1, cy - 1, 3, 3, P["m2"]);
        }
    }

    private void Runner(PixelBuffer b, int gx, int bx1, int gy, int gh)
    {
        foreach (int y in new[] { gy + gh - 7, gy + 2 })                                                       // near runner on the ground, far one behind the box
        {
            b.Shade(gx + 1, y, bx1 - gx + 8, 5, R["wood"], Top); b.Rect(gx + 1, y + 4, bx1 - gx + 8, 1, P["m2"]);
            b.Rect(bx1 + 6, y - 2, 3, 3, P["w4"]);                                                              // the turned-up tip
        }
        foreach (int x in new[] { gx + 10, gx + 40 }) b.Rect(x, gy + gh - 12, 3, 6, P["w2"]);                  // knees
    }

    private void Hay(PixelBuffer b, int x0, int x1, int yTop, int yBot, int floor, int wall)
    {
        int t = floor + wall, hw = x1 - x0 - 4;
        var rng = Rng(7301);
        int hy0 = yTop - t - 22, hy1 = yBot - t + 1;
        for (int y = hy0; y < hy1; y++)
            for (int x = x0 + 2; x < x1 - 2; x++)
            {
                double dx = (x - (x0 + x1) / 2.0) / (hw / 2.0), dy = (y - (hy0 + hy1) / 2.0) / ((hy1 - hy0) / 2.0);
                if (Math.Pow(Math.Abs(dx), 4) + dy * dy > 1.0) continue;                                       // a loaf, flat sided
                double n = Hash(x, y, 7303) * .55 + (1 - (y - hy0) / (double)(hy1 - hy0)) * .35;
                b.Set(x, y, R["gold"][n < .3 ? 0 : n < .65 ? 1 : 2]);
            }
        for (int i = 0; i < 26; i++) b.Set(x0 + 3 + (int)(rng() * (x1 - x0 - 6)), yTop - t - 20 + (int)(rng() * (yBot - yTop + 15)), P["au1"]);
        // the near rack: rail, posts and a lower rail over the hay
        b.Rect(x0, yBot - t - 1, x1 - x0, 2, P["w4"]);
        for (int x = x0 + 2; x < x1 - 2; x += 8) b.Rect(x, yBot - t + 1, 2, 5, P["w2"]);
    }

    private void Cover(PixelBuffer b, int x0, int x1, int yTop, int yBot, int t, int cy)
    {
        int top = yTop - t - 12, bot = yBot - t + 3, ridge = (top + bot) / 2 - 6;
        for (int y = top; y <= bot; y++)
        {
            double k = (y - top) / (double)(bot - top);
            var c = y < ridge - 2 ? R["linen"][2] : y < ridge + 2 ? P["pl4"] : k < .6 ? R["linen"][2] : k < .85 ? R["linen"][1] : R["linen"][0];
            b.Rect(x0 + 1, y, x1 - x0 - 2, 1, c);
        }
        b.Rect(x0, top + 3, 1, bot - top - 4, P["ln1"]); b.Rect(x1 - 1, top + 3, 1, bot - top - 4, P["ln1"]);   // rounded ends
        for (int x = x0 + 16; x < x1 - 8; x += 32) { b.Rect(x, top + 1, 1, bot - top, P["ln1"]); b.Rect(x + 1, top + 1, 1, bot - top - 4, P["ln3"]); }    // the hoops under the cloth
        b.Rect(x0 + 1, bot - 2, x1 - x0 - 2, 3, P["ln1"]);                                                      // the lashed hem
        b.Rect(x0 + 1, top, x1 - x0 - 2, 1, P["ln2"]);
        // the open back: a dark mouth
        b.Rect(x0 - 1, top + 6, 3, bot - top - 12, P["w1"]);
    }

    private void Hitch(PixelBuffer b, VehicleSpec s, int gx, int gy, int gw, int gh, int cy, int bx1)
    {
        int endX = gx + gw;
        switch (s.Style)
        {
            case HitchStyle.Pole:
                b.Shade(bx1 - 12, cy - 12, endX - bx1 + 10, 5, R["wood"], Top);                                // the pole, a hand above the ground
                b.Rect(bx1 - 12, cy - 8, endX - bx1 + 10, 1, P["w1"]);
                b.Shade(endX - 6, cy - 6 - 18, 5, 36, R["wood"], Top);                                         // the yoke bar across the end
                b.Rect(endX - 8, cy - 11, 3, 4, P["m3"]);
                for (int y = cy - 6 - 16; y < cy - 6 + 17; y += 8) { b.Rect(endX - 7, y, 7, 1, P["m2"]); }     // yoke straps
                break;
            case HitchStyle.Shafts:
                foreach (int y in new[] { cy - 11 - 10, cy + 11 - 10 })
                {
                    b.Shade(bx1 - 16, y, endX - bx1 + 14, 3, R["wood"], Top); b.Rect(bx1 - 16, y + 2, endX - bx1 + 14, 1, P["w1"]);
                    b.Rect(endX - 4, y - 1, 3, 5, P["m3"]);                                                    // the iron tip, where the horse's harness hangs
                }
                break;
            case HitchStyle.None when s.Bed == BedStyle.Box:                                                   // a handcart's two handles stick out behind
                foreach (int y in new[] { cy - 8 - 7, cy + 7 - 7 }) { b.Shade(gx, y, 26, 2, R["wood"], Top); b.Rect(gx - 1, y - 1, 3, 4, P["w4"]); }
                break;
            case HitchStyle.Traces:
                foreach (int y in new[] { gy + 8 - 6, gy + gh - 8 - 6 }) b.Line(bx1 - 2, y, endX - 2, cy - 6, P["br3"]);
                b.Rect(endX - 4, cy - 8, 4, 5, P["m3"]);
                break;
        }
    }

    private void BarrowDraw(PixelBuffer b, VehicleSpec s, int gx, int gy, int gw, int gh, int cy)
    {
        // handles run out at the back, a leg stands under the middle, the wheel is in front of the tray
        foreach (int y in new[] { cy - 10 - 9, cy + 10 - 9 })
        {
            b.Shade(gx + 1, y, 30, 3, R["wood"], Top); b.Rect(gx + 1, y + 2, 30, 1, P["w1"]); b.Rect(gx, y - 1, 3, 5, P["w4"]);
        }
        foreach (int y in new[] { cy - 10, cy + 8 }) b.Rect(gx + 22, y - 1, 3, 9, P["w2"]);                       // legs
        int x0 = gx + 20, x1 = gx + gw - 12, rimTop = cy - 13 - 9, rimBot = cy + 13 - 9;
        b.Shade(x0, rimTop + 3, x1 - x0, rimBot - rimTop - 1, R["wood"]);                                           // the tray's near side and floor
        b.Rect(x0 + 2, rimTop + 2, x1 - x0 - 4, rimBot - rimTop - 6, P["w2"]);
        for (int y = rimTop + 6; y < rimBot - 2; y += 4) b.Rect(x0 + 2, y, x1 - x0 - 4, 1, P["w1"]);
        b.Rect(x0, rimTop + 1, x1 - x0, 2, P["w5"]); b.Rect(x0, rimBot, x1 - x0, 2, P["w4"]);
        b.Rect(x1 - 1, rimTop + 3, 2, rimBot - rimTop - 3, P["w4"]);
        b.Ellipse(gx + gw - 8, cy - 6, 7, 6, P["w3"], true); b.Ellipse(gx + gw - 8, cy - 6, 5, 4, P["w4"], true); b.Rect(gx + gw - 9, cy - 7, 3, 3, P["m2"]);   // the wheel
        b.Rect(gx + gw - 16, cy - 7, 8, 2, P["w2"]);                                                               // the fork
    }
}
