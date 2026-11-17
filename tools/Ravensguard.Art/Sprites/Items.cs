using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

/// <summary>16x16 inventory items.</summary>
public sealed class Items
{
    private readonly ColorLookup P;
    private readonly RampLookup R;
    public Items(Palette pal) { P = pal.P; R = pal.R; }

    private PixelBuffer Item(Action<PixelBuffer> fn) { var b = new PixelBuffer(16, 16); fn(b); b.Outline(P["out"]); return b; }

    public IEnumerable<(string Id, Func<PixelBuffer> Build)> All()
    {
        yield return ("sword", () => Item(b =>
        {
            for (int i = 0; i < 9; i++) { b.Set(12 - i, 3 + i, P["m4"]); b.Set(13 - i, 3 + i, P["m5"]); b.Set(12 - i, 4 + i, P["m3"]); }
            b.Line(2, 10, 6, 14, P["au2"]); b.Set(3, 13, P["w2"]); b.Set(2, 14, P["w2"]);
        }));
        yield return ("axe", () => Item(b =>
        {
            b.Line(4, 14, 11, 3, P["w3"]); b.Shade(9, 1, 5, 7, R["iron"]); b.Set(13, 2, P["m5"]); b.Set(13, 7, P["m2"]);
        }));
        yield return ("spear", () => Item(b =>
        {
            b.Line(2, 14, 11, 5, P["w3"]); b.Set(12, 4, P["m4"]); b.Set(13, 3, P["m5"]); b.Set(14, 2, P["m5"]); b.Set(13, 4, P["m3"]); b.Set(12, 3, P["m3"]);
        }));
        yield return ("shield", () => Item(b =>
        {
            for (int y = 1; y < 15; y++)
            {
                int w = y < 8 ? 6 : Math.Max(0, 6 - (y - 7));
                for (int x = 8 - w; x < 8 + w; x++) b.Set(x, y, x < 8 ? P["r3"] : P["r2"]);
            }
            b.Rect(7, 2, 2, 11, P["au2"]); b.Set(7, 6, P["au3"]);
        }));
        yield return ("helm", () => Item(b =>
        {
            b.Shade(3, 3, 10, 9, R["iron"], new ShadeOptions { TopHi = true }); b.Rect(3, 11, 10, 2, P["au2"]); b.Rect(7, 12, 2, 3, P["m3"]); b.Set(5, 5, P["m5"]);
        }));
        yield return ("bread", () => Item(b =>
        {
            b.Ellipse(8, 9, 6, 4, P["au2"], true); b.Ellipse(8, 8, 5, 3, P["f3"], true);
            foreach (int x in new[] { 5, 8, 11 }) b.Set(x, 7, P["au1"]);
        }));
        yield return ("purse", () => Item(b =>
        {
            b.Ellipse(7, 10, 5, 4, P["w3"], true); b.Rect(5, 4, 5, 3, P["w2"]); b.Rect(5, 6, 5, 1, P["br1"]); b.Ellipse(12, 12, 2, 2, P["au3"], true); b.Set(12, 11, P["f4"]);
        }));
        yield return ("coin", () => Item(b =>
        {
            b.Ellipse(6, 10, 4, 2, P["au2"], true); b.Ellipse(6, 8, 4, 2, P["au3"], true); b.Ellipse(10, 7, 4, 4, P["au3"], true); b.Set(9, 5, P["f4"]); b.Set(10, 7, P["au1"]);
        }));
        yield return ("hide", () => Item(b =>
        {
            for (int y = 3; y < 14; y++)
                for (int x = 3; x < 13; x++)
                {
                    if ((x == 3 || x == 12) && (y == 3 || y == 13)) continue;
                    b.Set(x, y, (x + y) % 5 != 0 ? P["k2"] : P["k1"]);
                }
            b.Set(2, 5, P["k2"]); b.Set(13, 10, P["k2"]);
        }));
        yield return ("iron", () => Item(b =>
        {
            for (int k = 0; k < 2; k++)
            {
                int y0 = 5 + k * 4, x0 = 2 + k * 2;
                for (int i = 0; i < 10; i++) { b.Set(x0 + i, y0, P["m4"]); b.Set(x0 + i, y0 + 1, P["m3"]); b.Set(x0 + i, y0 + 2, P["m2"]); }
            }
        }));
        yield return ("torch", () => Item(b =>
        {
            b.Line(4, 14, 10, 6, P["w3"]); b.Set(11, 5, P["f2"]); b.Set(12, 4, P["f3"]); b.Set(11, 3, P["f3"]); b.Set(12, 2, P["f4"]); b.Set(10, 4, P["f1"]);
        }));
        yield return ("hammer", () => Item(b => { b.Line(3, 14, 10, 5, P["w3"]); b.Shade(8, 2, 7, 4, R["iron"]); }));
        yield return ("ale", () => Item(b =>
        {
            b.Shade(4, 5, 7, 9, R["wood"]); b.Rect(4, 7, 7, 1, P["m2"]); b.Rect(4, 11, 7, 1, P["m2"]); b.Rect(11, 7, 2, 4, P["w2"]); b.Rect(4, 3, 7, 2, P["pl4"]);
        }));
        yield return ("arrow", () => Item(b =>
        {
            b.Line(2, 13, 12, 3, P["w3"]); b.Set(13, 2, P["m4"]); b.Set(12, 2, P["m3"]); b.Set(13, 3, P["m3"]); b.Set(2, 12, P["pl4"]); b.Set(3, 13, P["pl4"]);
        }));
        yield return ("bow", () => Item(b =>
        {
            for (int y = 1; y < 15; y++) { int x = 4 + Round(4 * Math.Sin((y - 1) / 13.0 * Math.PI)); b.Set(x, y, P["w3"]); }
            b.Line(4, 1, 4, 14, P["pl3"]);
        }));
        yield return ("bandage", () => Item(b =>
        {
            b.Ellipse(8, 8, 5, 5, P["pl4"], true); b.Ellipse(8, 8, 2, 2, P["pl2"], true); b.Set(10, 6, P["bl3"]);
        }));
    }
}
