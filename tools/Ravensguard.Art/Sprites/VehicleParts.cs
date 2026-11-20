namespace Ravensguard.Art.Sprites;

/// <summary>
/// P5-G (vehicle-designer 6, overview 3 step 4): the parts a designed vehicle is drawn from, one 1 m cell (32 px) each, seen from above, and the river boats' template sprites. Every part has four
/// drawn views (<c>e</c>: the vehicle faces east, its rows run left to right from the rear; <c>n</c>, <c>w</c>, <c>s</c> turned a quarter each) and four diagonals baked from the east view by a 45 degree
/// nearest-neighbour turn (<c>ne</c>, <c>se</c>, <c>sw</c>, <c>nw</c>, 46 px). The game stacks a cell's parts ground up, the wheels last at the vehicle's outer side (flipped across for the right-hand column),
/// so any design the player makes is drawn from these.
/// </summary>
public sealed class VehicleParts
{
    public const int Cell = 32, Diag = 46;
    private readonly ColorLookup P;
    private readonly RampLookup R;
    private static readonly ShadeOptions Top = new() { TopHi = true };
    public VehicleParts(Palette pal) { P = pal.P; R = pal.R; }

    /// <summary>Every part id, row for row with <c>content/base/vehicles/parts.jsonc</c> (a test compares the two).</summary>
    public static readonly string[] Parts =
    [
        "frame_plank", "axle_wood", "wheel_solid", "wheel_spoked", "iron_tyre", "shaft_pair", "pole_yoke", "pole_beam", "traces", "cargo_bed", "cargo_box", "barrow_tray", "cargo_rack", "seat_bench",
        "tilt_hoops", "tilt_cloth", "runner", "handle_bars", "iron_fitting",
        "axle_pivot", "axle_iron_bound", "wheel_dished", "brake_shoe", "drag_shoe", "hull_plank", "keel", "gunwale", "deck_plank", "ferry_ramp", "thwart", "rowlock", "quant_pole_rack", "steering_oar",
        "rudder", "tow_post", "mast", "sail_square", "fish_well", "cabin_box", "stall_counter", "mobile_hearth",
    ];

    public static readonly string[] Views = ["e", "n", "w", "s"];
    public static readonly string[] Diagonals = ["ne", "se", "sw", "nw"];

    /// <summary>A part in one of the eight views.</summary>
    public PixelBuffer Draw(string part, string view)
    {
        var e = East(part);
        return view switch
        {
            "e" => e.Outline(P["out"]),
            "n" => Turn(e, 3).Outline(P["out"]),
            "w" => Turn(e, 2).Outline(P["out"]),
            "s" => Turn(e, 1).Outline(P["out"]),
            "ne" => Rot45(e, -1).Outline(P["out"]),
            "se" => Rot45(e, 1).Outline(P["out"]),
            "sw" => Rot45(Turn(e, 2), -1).Outline(P["out"]),
            "nw" => Rot45(Turn(e, 2), 1).Outline(P["out"]),
            _ => e.Outline(P["out"]),
        };
    }

    /// <summary>A quarter turn clockwise <paramref name="q"/> times (y down: east becomes south).</summary>
    public static PixelBuffer Turn(PixelBuffer s, int q)
    {
        var b = s;
        for (int k = 0; k < (q & 3); k++)
        {
            var t = new PixelBuffer(b.H, b.W);
            for (int y = 0; y < b.H; y++) for (int x = 0; x < b.W; x++) { uint c = b.Get(x, y); if (c != 0) t.Data[x * t.W + (b.H - 1 - y)] = c; }
            b = t;
        }
        return b;
    }

    /// <summary>The 45 degree bake: each target pixel samples the source at the inverse turn (nearest, never blended, so only palette colours appear).</summary>
    public static PixelBuffer Rot45(PixelBuffer s, int dir)
    {
        var b = new PixelBuffer(Diag, Diag);
        double c = Math.Sqrt(0.5), sn = dir * Math.Sqrt(0.5), cx = (Diag - 1) / 2.0, sx = (s.W - 1) / 2.0, sy = (s.H - 1) / 2.0;
        for (int y = 0; y < Diag; y++)
            for (int x = 0; x < Diag; x++)
            {
                double dx = x - cx, dy = y - cx;
                int u = (int)Math.Floor(c * dx + sn * dy + sx + 0.5), v = (int)Math.Floor(-sn * dx + c * dy + sy + 0.5);
                uint col = s.Get(u, v); if (col != 0 && (col >> 24) != 0) b.Data[y * Diag + x] = col;
            }
        return b;
    }

    // ---------------------------------------------------------------------------------------------- the east views

    /// <summary>A part facing east, without its outline: the rear at the left, the front at the right; across the vehicle is up and down (wheels sit at the top edge, the outer side).</summary>
    public PixelBuffer East(string part)
    {
        var b = new PixelBuffer(Cell, Cell);
        uint[] wood = R["wood"], iron = R["iron"], linen = R["linen"], stone = R["stone"], water = R["water"];
        switch (part)
        {
            case "frame_plank": b.Shade(1, 4, 30, 5, wood, Top); b.Shade(1, 23, 30, 5, wood, Top); b.Shade(4, 9, 4, 14, wood); b.Shade(24, 9, 4, 14, wood); break;
            case "axle_wood": b.Shade(13, 1, 6, 30, wood, Top); b.Rect(14, 1, 4, 2, P["w1"]); break;
            case "axle_iron_bound": b.Shade(13, 1, 6, 30, wood, Top); for (int y = 3; y < 30; y += 6) b.Rect(13, y, 6, 2, P["m3"]); break;
            case "axle_pivot": b.Shade(13, 1, 6, 30, wood, Top); b.Ellipse(16, 16, 5, 5, P["m2"], true); b.Ellipse(16, 16, 2, 2, P["m4"], true); b.Rect(10, 15, 12, 2, P["m3"]); break;
            case "wheel_solid": b.Shade(5, 1, 22, 6, wood, Top); b.Rect(14, 2, 4, 4, P["w1"]); break;
            case "wheel_spoked": b.Shade(4, 1, 24, 6, wood, Top); for (int x = 6; x < 27; x += 4) b.Rect(x, 2, 1, 4, P["w5"]); b.Rect(14, 2, 4, 4, P["m3"]); break;
            case "wheel_dished": b.Shade(3, 1, 26, 7, wood, Top); for (int x = 5; x < 28; x += 4) b.Rect(x, 2, 1, 5, P["w5"]); b.Rect(14, 2, 4, 5, P["m3"]); break;
            case "iron_tyre": b.Rect(4, 1, 24, 1, P["m2"]); b.Rect(4, 6, 24, 1, P["m2"]); b.Rect(3, 1, 1, 6, P["m3"]); b.Rect(28, 1, 1, 6, P["m3"]); break;
            case "brake_shoe": b.Shade(23, 7, 5, 4, wood); b.Rect(23, 7, 5, 1, P["m3"]); break;
            case "drag_shoe": b.Shade(2, 7, 7, 4, iron, Top); b.Line(8, 9, 14, 14, P["m2"]); break;
            case "shaft_pair": b.Shade(1, 7, 30, 3, wood, Top); b.Shade(1, 22, 30, 3, wood, Top); b.Rect(28, 6, 3, 5, P["m3"]); b.Rect(28, 21, 3, 5, P["m3"]); break;
            case "pole_beam": b.Shade(1, 14, 30, 4, wood, Top); b.Rect(1, 17, 30, 1, P["w1"]); break;
            case "pole_yoke": b.Shade(12, 2, 6, 28, wood, Top); b.Ellipse(15, 8, 4, 3, P["m3"], false); b.Ellipse(15, 24, 4, 3, P["m3"], false); b.Shade(1, 14, 12, 4, wood); break;
            case "traces": b.Line(1, 8, 30, 14, P["br3"]); b.Line(1, 24, 30, 18, P["br3"]); b.Rect(27, 14, 4, 5, P["m3"]); break;
            case "cargo_bed": b.Shade(1, 2, 30, 28, wood); for (int x = 6; x < 30; x += 6) b.Rect(x, 3, 1, 26, P["w2"]); b.Shade(1, 1, 30, 3, wood, Top); b.Shade(1, 28, 30, 3, wood, Top); break;
            case "cargo_box": b.Shade(2, 3, 28, 26, wood); b.Rect(3, 4, 26, 24, P["w2"]); b.Shade(5, 6, 22, 20, wood); b.Rect(2, 3, 28, 2, P["w5"]); break;
            case "barrow_tray": b.Shade(4, 5, 24, 22, wood); b.Rect(6, 7, 20, 18, P["w2"]); b.Shade(8, 9, 16, 14, wood); break;
            case "cargo_rack": b.Shade(1, 2, 30, 28, R["thatch"]); for (int x = 3; x < 30; x += 5) b.Rect(x, 1, 2, 30, P["w2"]); b.Rect(1, 1, 30, 2, P["w4"]); b.Rect(1, 29, 30, 2, P["w4"]); break;
            case "seat_bench": b.Shade(18, 3, 10, 26, wood, Top); b.Rect(18, 3, 2, 26, P["w5"]); break;
            case "tilt_hoops": for (int x = 4; x < 30; x += 8) { b.Rect(x, 1, 2, 30, P["w4"]); b.Rect(x, 1, 2, 1, P["w1"]); } break;
            case "tilt_cloth": b.Shade(1, 1, 30, 30, linen, Top); for (int x = 4; x < 30; x += 8) b.Rect(x, 1, 1, 30, P["ln1"]); b.Rect(1, 15, 30, 2, P["ln3"]); break;
            case "runner": b.Shade(1, 3, 30, 4, wood, Top); b.Shade(1, 25, 30, 4, wood, Top); b.Rect(27, 3, 4, 1, P["m3"]); b.Rect(27, 28, 4, 1, P["m3"]); break;
            case "handle_bars": b.Shade(1, 6, 26, 3, wood, Top); b.Shade(1, 23, 26, 3, wood, Top); b.Rect(1, 5, 4, 5, P["br2"]); b.Rect(1, 22, 4, 5, P["br2"]); break;
            case "iron_fitting": foreach (var (x, y) in new[] { (2, 2), (26, 2), (2, 26), (26, 26) }) { b.Rect(x, y, 4, 4, P["m3"]); b.Rect(x, y, 4, 1, P["m4"]); } break;
            case "hull_plank":
                b.Shade(1, 1, 30, 30, wood); for (int y = 5; y < 30; y += 5) b.Rect(1, y, 30, 1, P["w2"]);
                for (int x = 7; x < 30; x += 9) b.Rect(x, 1, 1, 30, P["w1"]); break;
            case "keel": b.Shade(1, 13, 30, 6, wood, Top); b.Rect(1, 15, 30, 2, P["w1"]); break;
            case "gunwale": b.Shade(1, 1, 30, 4, wood, Top); b.Rect(1, 4, 30, 1, P["w1"]); break;
            case "deck_plank": b.Shade(1, 1, 30, 30, wood, Top); for (int x = 4; x < 30; x += 4) b.Rect(x, 1, 1, 30, P["w2"]); for (int y = 8; y < 30; y += 12) b.Rect(1, y, 30, 1, P["m2"]); break;
            case "ferry_ramp": b.Shade(1, 1, 30, 30, wood); for (int x = 3; x < 30; x += 5) b.Rect(x, 1, 2, 30, P["w5"]); b.Rect(1, 1, 3, 30, P["m3"]); break;
            case "thwart": b.Shade(12, 1, 8, 30, wood, Top); b.Rect(12, 1, 8, 1, P["w5"]); break;
            case "rowlock": b.Rect(14, 1, 4, 2, P["m3"]); b.Shade(15, 1, 2, 14, wood); b.Shade(11, 12, 10, 4, wood, Top); break;
            case "quant_pole_rack": for (int y = 8; y < 26; y += 5) { b.Shade(1, y, 30, 2, wood, Top); b.Rect(29, y, 2, 2, P["m3"]); } break;
            case "steering_oar": b.Shade(1, 3, 22, 3, wood, Top); b.Shade(1, 2, 9, 7, wood, Top); b.Rect(20, 4, 3, 4, P["m3"]); break;
            case "rudder": b.Shade(1, 13, 10, 6, wood, Top); b.Shade(10, 15, 18, 2, wood); b.Rect(24, 14, 4, 4, P["m3"]); break;
            case "tow_post": b.Ellipse(16, 16, 6, 6, P["w2"], true); b.Ellipse(15, 15, 4, 4, P["w4"], true); b.Ellipse(16, 16, 7, 7, P["br3"], false); break;
            case "mast": b.Ellipse(16, 16, 5, 5, P["w2"], true); b.Ellipse(15, 15, 3, 3, P["w4"], true); b.Rect(4, 15, 24, 2, P["br3"]); break;
            case "sail_square": b.Shade(10, 1, 10, 30, linen, Top); for (int y = 6; y < 30; y += 8) b.Rect(10, y, 10, 1, P["ln1"]); b.Rect(14, 1, 2, 30, P["w2"]); break;
            case "fish_well": b.Shade(3, 3, 26, 26, wood); b.Shade(6, 6, 20, 20, water); b.Rect(10, 12, 5, 2, P["s4"]); b.Rect(17, 18, 5, 2, P["s4"]); break;
            case "cabin_box": b.Shade(1, 1, 30, 30, R["shingle"], Top); for (int y = 6; y < 30; y += 6) b.Rect(1, y, 30, 1, P["sh1"]); b.Rect(1, 15, 30, 2, P["w1"]); break;
            case "stall_counter": b.Shade(2, 2, 28, 12, R["red"], Top); for (int x = 2; x < 30; x += 6) b.Rect(x, 2, 3, 12, P["ln3"]); b.Shade(2, 16, 28, 12, wood, Top); break;
            case "mobile_hearth": b.Shade(3, 3, 26, 26, stone, Top); b.Shade(8, 8, 16, 16, R["red"]); b.Rect(12, 12, 8, 8, P["au2"]); b.Rect(14, 14, 4, 4, P["au3"]); break;
            default: b.Shade(4, 4, 24, 24, wood); break;
        }
        return b;
    }

    // ---------------------------------------------------------------------------------------------- the river boats' template sprites

    /// <summary>A boat template: hull cells, beam, its stern-to-bow rows, and what stands in it.</summary>
    public sealed record BoatSpec(string Id, int Beam, int Length, bool Keel, int[] Thwarts, bool Deck, bool Ramps, bool FishWell, bool Rudder, bool SteeringOar, int Rise);

    public static readonly BoatSpec[] Boats =
    [
        new("rowing_boat",  2, 5,  true,  [1, 3], false, false, false, false, false, 20),
        new("fishing_boat", 2, 6,  true,  [1, 4], false, false, true,  false, true,  20),
        new("barge",        3, 10, false, [],     true,  false, false, true,  false, 24),
        new("ferry",        3, 6,  false, [],     true,  true,  false, false, false, 20),
    ];

    public static (int W, int H) Size(BoatSpec s) => (s.Length * Cell + 8, s.Rise + s.Beam * Cell + 4);
    public static (int W, int H) SizeNS(BoatSpec s) => (s.Rise + s.Beam * Cell + 4, s.Length * Cell + 8);

    /// <summary>A boat facing east (bow at the right) from above with its near side showing: planked hull, gunwales, thwarts, a deck, ramps, a well, a rudder or a steering oar.</summary>
    public PixelBuffer Boat(BoatSpec s)
    {
        var (w, h) = Size(s); var b = new PixelBuffer(w, h);
        int gx = 4, gy = s.Rise, len = s.Length * Cell, wid = s.Beam * Cell;
        uint[] wood = R["wood"];
        bool Inside(int x, int y)
        {
            // a fine bow and a narrower stern for a keeled boat; square ends for a flat
            double u = (double)x / len, v = Math.Abs((y - wid / 2.0) / (wid / 2.0));
            if (!s.Keel) return y >= 1 && y < wid - 1 && x >= (s.Ramps ? 0 : 2) && x < len - (s.Ramps ? 0 : 2);
            double half = u > 0.7 ? (1 - u) / 0.3 : u < 0.12 ? 0.75 + u / 0.48 : 1.0;
            return v <= half * 0.96;
        }
        b.Shade(gx, gy + wid - 2, len, 5, wood);                                                          // the near side of the hull
        for (int y = 0; y < wid; y++) for (int x = 0; x < len; x++) if (Inside(x, y)) b.Set(gx + x, gy + y, (y % 6 == 0) ? P["w2"] : (x % 9 == 0 ? P["w1"] : P["w3"]));
        for (int y = 0; y < wid; y++) for (int x = 0; x < len; x++) if (Inside(x, y) && (!Inside(x, y - 2) || !Inside(x, y + 2) || !Inside(x - 2, y) || !Inside(x + 2, y))) b.Set(gx + x, gy + y, P["w5"]);
        if (s.Deck) for (int x = 6; x < len - 4; x += 4) b.Rect(gx + x, gy + 3, 1, wid - 6, P["w2"]);
        if (s.Keel) b.Rect(gx + 4, gy + wid / 2 - 1, len - 10, 2, P["w1"]);
        foreach (int t in s.Thwarts) b.Shade(gx + t * Cell + 10, gy + 4, 8, wid - 8, wood, Top);
        if (s.Thwarts.Length > 0) foreach (int t in s.Thwarts) { b.Shade(gx + t * Cell + 2, gy - 6, 3, 10, wood); b.Shade(gx + t * Cell + 2, gy + wid - 4, 3, 7, wood); }
        if (s.FishWell) { b.Shade(gx + 2 * Cell + 6, gy + 8, 2 * Cell - 12, wid - 16, R["water"]); b.Rect(gx + 2 * Cell + 6, gy + 8, 2 * Cell - 12, 1, P["w1"]); }
        if (s.Ramps) foreach (int x0 in new[] { 0, len - 10 }) { b.Shade(gx + x0, gy + 2, 10, wid - 4, wood, Top); for (int y = 4; y < wid - 4; y += 4) b.Rect(gx + x0, gy + y, 10, 1, P["w5"]); }
        if (s.Rudder) { b.Shade(gx - 3, gy + wid / 2 - 2, 7, 4, wood, Top); b.Rect(gx + 2, gy + wid / 2 - 1, 8, 2, P["w1"]); }
        if (s.SteeringOar) { b.Line(gx + 2, gy + 6, gx + 20, gy + 2, P["w2"]); b.Shade(gx - 2, gy + 4, 6, 4, wood); }
        if (s.Deck && !s.Ramps) for (int k = 0; k < 3; k++) b.Shade(gx + 3 * Cell + k * 2 * Cell, gy + 6, 18, wid - 12, R["brown"], Top);   // sacks on deck
        return b.Outline(P["out"]);
    }

    /// <summary>The boat seen bow up (north) or bow down (south): the east view turned, the near side kept at the bottom.</summary>
    public PixelBuffer BoatNS(BoatSpec s, bool north) => Turn(Boat(s), north ? 3 : 1);
}
