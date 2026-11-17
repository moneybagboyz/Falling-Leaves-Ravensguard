using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

/// <summary>
/// Layered paper-doll description. Ramp-valued fields are ramp names from palette.jsonc, hair is a colour name.
/// Layer order: body, under (shirt), over (body colour, dress, apron, gambeson), armour (mail), cover (surcoat, robe, fur),
/// head gear, held items, wounds.
/// </summary>
public sealed record PersonOpts
{
    public bool Dark { get; init; }
    public bool Fem { get; init; }
    public string? Legs { get; init; }
    public string? Shirt { get; init; }
    public int Sleeve { get; init; } = 12;
    public string? Body { get; init; }
    public bool Apron { get; init; }
    public bool Gambeson { get; init; }
    public bool Mail { get; init; }
    public string? Surcoat { get; init; }
    public bool Emblem { get; init; } = true;
    public string? Robe { get; init; }
    public bool Fur { get; init; }
    public bool Beard { get; init; }
    public string? Hair { get; init; }
    public bool Coif { get; init; }
    public string? Hood { get; init; }
    public bool Helm { get; init; }
    public bool Hat { get; init; }
    public bool Spear { get; init; }
    public bool Sword { get; init; }
    public bool Axe { get; init; }
    public bool Hammer { get; init; }
    public bool Bow { get; init; }
    public bool Carry { get; init; }
    public string? Shield { get; init; }
    // wounds
    public (int X, int Y)[]? Blood { get; init; }
    public bool BandageArm { get; init; }
    public bool Limp { get; init; }
    public bool NoLeftArm { get; init; }
}

/// <summary>32x64 front-view characters (port of person(o)).</summary>
public sealed class People
{
    private readonly ColorLookup P;
    private readonly RampLookup R;
    private static readonly ShadeOptions Top = new() { TopHi = true };
    public People(Palette pal) { P = pal.P; R = pal.R; }

    private uint[] Rp(params string[] names) => names.Select(n => P[n]).ToArray();

    public PixelBuffer Person(PersonOpts o)
    {
        var b = new PixelBuffer(32, 64);
        var sk = o.Dark ? Rp("br1", "k1", "k2", "k3") : R["skin"];
        bool fem = o.Fem, noLA = o.NoLeftArm;
        var legs = o.Legs != null ? R[o.Legs] : R["brown"];
        var boot = Rp("br1", "br1", "br2", "br3");
        // legs + feet
        b.Shade(11, 38, 4, 18, legs); b.Shade(17, 38, 4, 18, legs); b.Shade(10, 55, 5, 5, boot); b.Shade(17, 55, 5, 5, boot);
        // arms and torso skin
        if (!noLA) { b.Shade(7, 22, 3, 15, sk); b.Shade(7, 37, 3, 3, sk); }
        b.Shade(22, 22, 3, 15, sk); b.Shade(22, 37, 3, 3, sk); b.Shade(10, 21, 12, 17, sk);
        // shirt (linen under layer)
        var shirt = o.Shirt != null ? R[o.Shirt] : R["linen"];
        b.Shade(10, 21, 12, 17, shirt);
        if (!noLA) b.Shade(7, 22, 3, o.Sleeve, shirt);
        b.Shade(22, 22, 3, o.Sleeve, shirt);
        if (fem)
        {
            var dress = o.Body != null ? R[o.Body] : R["green"];
            b.Shade(9, 21, 14, 36, dress, new ShadeOptions { Mask = (i, j) => j < 16 ? (i > 0 && i < 13) : true });
            b.Rect(10, 35, 12, 1, P["br1"]);
        }
        if (o.Body != null && !fem)
        {
            var body = R[o.Body];
            b.Shade(10, 21, 12, 24, body); if (!noLA) b.Shade(7, 22, 3, 7, body); b.Shade(22, 22, 3, 7, body);
            b.Rect(10, 36, 12, 1, P["br1"]); b.Set(15, 36, P["au3"]);
        }
        if (o.Apron) { b.Shade(12, 27, 8, 22, R["leather"]); b.Rect(12, 26, 8, 1, P["w1"]); b.Set(11, 27, P["w1"]); b.Set(20, 27, P["w1"]); }
        if (o.Gambeson)
        {
            b.Shade(10, 21, 12, 25, R["linen"]);
            for (int j = 24; j < 45; j += 3) b.Rect(10, j, 12, 1, P["ln1"]);
            if (!noLA) b.Shade(7, 22, 3, 15, R["linen"]);
            b.Shade(22, 22, 3, 15, R["linen"]);
        }
        if (o.Mail)
        {
            for (int j = 21; j < 48; j++)
                for (int i = 9; i < 23; i++)
                {
                    if (j > 44 && (i < 10 || i > 21)) continue;
                    b.Set(i, j, ((i + j) % 2) != 0 ? P["m3"] : P["m2"]);
                }
            for (int j = 22; j < 36; j++)
            {
                bool odd = (j % 2) != 0;
                if (!noLA) { b.Set(7, j, odd ? P["m3"] : P["m2"]); b.Set(8, j, odd ? P["m2"] : P["m4"]); b.Set(9, j, P["m2"]); }
                b.Set(22, j, odd ? P["m3"] : P["m2"]); b.Set(23, j, odd ? P["m2"] : P["m3"]); b.Set(24, j, P["m1"]);
            }
            b.Rect(9, 21, 14, 1, P["m4"]);
        }
        if (o.Surcoat != null)
        {
            b.Shade(11, 23, 10, 26, R[o.Surcoat]); b.Rect(11, 36, 10, 1, P["br1"]);
            if (o.Emblem) { b.Rect(15, 27, 2, 5, P["out"]); b.Rect(13, 28, 6, 2, P["out"]); b.Set(14, 26, P["out"]); b.Set(17, 26, P["out"]); }
        }
        if (o.Robe != null)
        {
            var rr = R[o.Robe];
            b.Shade(9, 21, 14, 37, rr); if (!noLA) b.Shade(7, 22, 3, 15, rr); b.Shade(22, 22, 3, 15, rr); b.Rect(9, 34, 14, 1, P["pl3"]);
        }
        if (o.Fur) { b.Rect(9, 21, 14, 3, P["pl4"]); for (int i = 9; i < 23; i += 2) b.Set(i, 23, P["pl2"]); }
        // neck + head
        b.Shade(14, 19, 4, 3, sk); b.Shade(11, 7, 10, 12, sk, Top);
        b.Clear(11, 7); b.Clear(20, 7); b.Clear(11, 18); b.Clear(20, 18); b.Set(10, 12, sk[2]); b.Set(21, 12, sk[1]);
        b.Set(13, 13, P["out"]); b.Set(18, 13, P["out"]); b.Set(15, 16, sk[0]); b.Set(16, 16, sk[0]);
        var hair = P[o.Hair ?? "hb"];
        if (o.Beard) { b.Rect(12, 16, 8, 3, hair); b.Set(15, 16, sk[0]); }
        if (!o.Helm && o.Hood == null && !o.Coif)
        {
            b.Rect(11, 6, 10, 3, hair); b.Rect(10, 7, 1, 5, hair); b.Rect(21, 7, 1, 5, hair);
            if (fem) { b.Rect(10, 8, 1, 13, hair); b.Rect(21, 8, 1, 13, hair); b.Rect(9, 12, 1, 8, hair); b.Rect(22, 12, 1, 8, hair); }
        }
        if (o.Coif)
            for (int j = 6; j < 21; j++)
                for (int i = 9; i < 23; i++)
                {
                    if (j >= 10 && j <= 18 && i >= 12 && i <= 19) continue;
                    if ((i == 9 || i == 22) && j < 8) continue;
                    b.Set(i, j, ((i + j) % 2) != 0 ? P["m3"] : P["m2"]);
                }
        if (o.Hood != null)
        {
            var h = R[o.Hood];
            for (int j = 4; j < 22; j++)
                for (int i = 9; i < 23; i++)
                {
                    if (j >= 9 && j <= 18 && i >= 12 && i <= 19) continue;
                    if (j < 6 && (i < 11 || i > 20)) continue;
                    b.Set(i, j, h[i < 11 ? 3 : i > 20 ? 1 : 2]);
                }
        }
        if (o.Helm) { b.Shade(10, 3, 12, 7, R["iron"], Top); b.Rect(10, 9, 12, 1, P["m1"]); b.Rect(15, 9, 2, 6, P["m3"]); b.Set(13, 4, P["m5"]); }
        if (o.Hat) { b.Rect(8, 6, 16, 2, P["au2"]); b.Shade(11, 2, 10, 5, Rp("au1", "au2", "f3", "f4")); }
        // held things
        if (o.Spear)
        {
            b.Rect(5, 1, 2, 59, P["w3"]); b.Rect(5, 1, 1, 59, P["w4"]); b.Rect(4, 1, 4, 4, P["m4"]); b.Set(5, 1, P["m5"]); // starts at y=1 so the outline ring fits inside the cell b.Rect(4, 37, 4, 3, sk[2]);
        }
        if (o.Sword) { b.Rect(7, 39, 2, 1, P["au2"]); b.Rect(5, 40, 6, 1, P["au3"]); b.Rect(7, 41, 2, 14, P["m4"]); b.Rect(8, 41, 1, 14, P["m5"]); b.Set(7, 55, P["m3"]); }
        if (o.Axe) { b.Rect(7, 27, 2, 24, P["w3"]); b.Shade(2, 26, 6, 7, R["iron"]); }
        if (o.Hammer) { b.Rect(24, 36, 2, 12, P["w3"]); b.Shade(22, 46, 6, 4, R["iron"]); }
        if (o.Bow)
        {
            for (int y = 18; y < 52; y++) { int x = 4 - Round(3 * Math.Sin((y - 18) / 34.0 * Math.PI)); b.Set(x, y, P["w3"]); }
            b.Line(4, 18, 4, 51, P["pl3"]);
        }
        if (o.Carry) { b.Shade(9, 28, 14, 9, R["skin"], Top); b.Rect(9, 32, 14, 1, P["k1"]); }
        if (o.Shield != null)
        {
            var sc = R[o.Shield];
            for (int y = 24; y < 48; y++)
            {
                int w = y < 36 ? 6 : Math.Max(0, 6 - Round((y - 35) * .55));
                for (int x = 25 - w; x < 25 + w; x++) b.Set(x, y, sc[x < 23 ? 3 : x > 27 ? 1 : 2]); // centred on x=25 so the right edge (x=30) keeps its outline inside the cell
            }
            b.Rect(24, 25, 2, 20, P["au2"]); b.Rect(20, 30, 10, 2, P["au2"]); b.Set(24, 31, P["au3"]);
        }
        // wounds
        if (o.Blood != null)
            foreach (var (x, y) in o.Blood)
            {
                b.Set(x, y, P["bl3"]); b.Set(x + 1, y, P["bl2"]); b.Set(x, y + 1, P["bl2"]); b.Set(x + 1, y + 2, P["bl1"]); b.Set(x - 1, y + 1, P["bl2"]);
            }
        if (o.BandageArm) { b.Rect(22, 27, 3, 4, P["pl4"]); b.Set(23, 28, P["bl3"]); b.Rect(22, 29, 3, 1, P["pl2"]); }
        if (o.Limp) { for (int j = 24; j < 37; j++) b.Set(8, j + (j > 30 ? 1 : 0), P["bl2"]); b.Set(7, 39, P["bl3"]); b.Set(8, 40, P["bl2"]); }
        if (noLA) { b.Set(8, 22, P["bl3"]); b.Set(9, 22, P["bl2"]); b.Set(8, 23, P["bl2"]); b.Set(9, 23, P["bl1"]); }
        b.Outline(P["out"]);
        return b;
    }

    public static PersonOpts Soldier(string col, PersonOpts? extra = null) =>
        (extra ?? new PersonOpts()) with { Mail = true, Surcoat = col, Helm = true, Spear = true, Shield = col, Legs = "brown" };

    /// <summary>Preset characters: id, options. Mirrors the concept page's town cast plus soldiers and wound states.</summary>
    public static IEnumerable<(string Id, PersonOpts O)> Presets()
    {
        yield return ("smith", new() { Apron = true, Hammer = true, Beard = true, Hair = "hbr" });
        yield return ("guard_gambeson", new() { Gambeson = true, Shield = "red", Hair = "hb", Beard = true });
        yield return ("woman_apron", new() { Fem = true, Body = "green", Apron = true, Carry = true, Hair = "hr" });
        yield return ("hero", new() { Gambeson = true, Sword = true, Hair = "hbr", BandageArm = true });
        yield return ("peasant_hat", new() { Body = "brown", Hat = true });
        yield return ("woman_red", new() { Fem = true, Body = "red", Hair = "hbl" });
        yield return ("elder_blue", new() { Body = "blue", Hair = "hg", Beard = true });
        yield return ("woman_linen_dark", new() { Fem = true, Body = "linen", Hair = "hb", Dark = true });
        yield return ("traveller_hood", new() { Body = "green", Hood = "brown" });
        yield return ("porter_dark", new() { Body = "brown", Carry = true, Dark = true });
        yield return ("man_red", new() { Body = "red", Hair = "hbl" });
        yield return ("spearman_red", new() { Gambeson = true, Surcoat = "red", Helm = true, Spear = true });
        yield return ("woman_blue", new() { Fem = true, Body = "blue", Hair = "hbr" });
        yield return ("furred_elder", new() { Body = "linen", Fur = true, Hair = "hg" });
        yield return ("man_beard", new() { Body = "brown", Beard = true, Hair = "hb" });
        yield return ("woman_apron_red", new() { Fem = true, Body = "red", Apron = true, Hair = "hbr" });
        yield return ("woman_linen", new() { Fem = true, Body = "linen", Hair = "hb" });
        yield return ("soldier_red", Soldier("red"));
        yield return ("soldier_blue", Soldier("blue"));
        yield return ("soldier_green", Soldier("green"));
        yield return ("coif_axeman", new() { Mail = true, Coif = true, Axe = true, Shirt = "linen", Legs = "brown", Hair = "hb" });
        yield return ("archer", new() { Body = "green", Hood = "green", Bow = true, Legs = "brown" });
        yield return ("priest_robe", new() { Robe = "linen", Hair = "hg", Beard = true });
        yield return ("mage_robe", new() { Robe = "blue", Hood = "blue" });
        yield return ("wounded_bleeding", new() { Gambeson = true, Sword = true, Hair = "hbr", Blood = new[] { (14, 28), (17, 40) } });
        yield return ("wounded_limp", new() { Body = "brown", Hair = "hb", Limp = true, Blood = new[] { (12, 44) } });
        yield return ("wounded_one_arm", new() { Body = "red", Hair = "hbl", NoLeftArm = true, BandageArm = true });
        yield return ("soldier_hurt", Soldier("red", new() { Blood = new[] { (14, 30) }, BandageArm = true }));
    }
}
