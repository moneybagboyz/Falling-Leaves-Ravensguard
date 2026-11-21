namespace Ravensguard.Art.Sprites;

/// <summary>
/// Battle art for the Field and Command bands (P7-BB-E; REDESIGN_PLAN 2.7, battle-bubble-and-formations 9): one 8x16 mini per troop role per view
/// (front, back, side facing east; west is the side view mirrored by the game), a lying body, a 8x8 Command block icon per role, a shape marker per team
/// (every faction colour also has a shape, plan 2.7 accessibility) and the team ramp strip. Every team-colour pixel is drawn in the <c>red</c> ramp; the
/// crowd shader swaps it for the instance's palette row (the strip <c>mini.ramps</c> is that table: 4 levels x one row per <see cref="Minis.Teams"/>).
/// Roles are the ids of <c>content/base/war/troops.jsonc</c>.
/// </summary>
public sealed class TroopMinis
{
    public static readonly string[] Roles = { "levy", "spearman", "archer", "crossbowman", "sergeant", "man_at_arms", "knight", "engineer", "sailor", "marine" };
    public static readonly string[] Views = { "front", "back", "side" };

    private readonly ColorLookup P;
    private readonly RampLookup R;
    public TroopMinis(Palette pal) { P = pal.P; R = pal.R; }

    // glyphs of the Command block icons, 6x6 inside the 1 px border
    private static readonly Dictionary<string, string[]> Glyphs = new()
    {
        ["levy"] = new[] { "X.X.X.", "X.X.X.", "XXXXX.", "..X...", "..X...", "..X..." },
        ["spearman"] = new[] { "..XX..", "..XX..", "..X...", "..X...", "..X...", "..X..." },
        ["archer"] = new[] { "XX....", ".X.X..", ".X..X.", ".X..X.", ".X.X..", "XX...." },
        ["crossbowman"] = new[] { "......", "XXXXXX", "X..X..", "..XX..", "..X...", "..X..." },
        ["sergeant"] = new[] { "X....X", ".X..X.", "..XX..", "X....X", ".X..X.", "..XX.." },
        ["man_at_arms"] = new[] { ".XXXX.", ".X..X.", ".X..X.", ".X..X.", "..XX..", "......" },
        ["knight"] = new[] { "..XX..", "..XX..", "XXXXXX", "XXXXXX", "..XX..", "..XX.." },
        ["engineer"] = new[] { "XXXX..", "XXXX..", "..X...", "..X...", "..X...", "..X..." },
        ["sailor"] = new[] { "..XX..", "..XX..", ".XXXX.", "..XX..", "X.XX.X", ".XXXX." },
        ["marine"] = new[] { "XXX...", "XXXX..", "XXX.X.", "..XX..", "..X...", "..X..." },
    };

    // team shape markers, 4x4 (the colour-blind aid: a circle, square, diamond, triangle, cross, bar)
    private static readonly string[][] Marks =
    {
        new[] { ".XX.", "XXXX", "XXXX", ".XX." }, new[] { "XXXX", "XXXX", "XXXX", "XXXX" }, new[] { "..X.", ".XXX", "XXXX", ".XXX" },
        new[] { "....", "..X.", ".XXX", "XXXX" }, new[] { ".XX.", "XXXX", "XXXX", ".XX." }, new[] { "....", "XXXX", "XXXX", "...." },
    };

    /// <summary>The team table: 4 levels (dark to light) by one row per team, in <see cref="Minis.Teams"/> order (neutral is the brown ramp).</summary>
    public PixelBuffer Ramps()
    {
        var b = new PixelBuffer(4, Minis.Teams.Length);
        for (int t = 0; t < Minis.Teams.Length; t++)
        {
            var r = Minis.Teams[t] == "neutral" ? R["brown"] : R[Minis.Teams[t]];
            for (int l = 0; l < 4; l++) b.Set(l, t, r[Math.Min(l, r.Length - 1)]);
        }
        return b;
    }

    public PixelBuffer Mark(int team)
    {
        var b = new PixelBuffer(4, 4); var g = Marks[team % Marks.Length];
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) if (g[y][x] == 'X') b.Set(x, y, R["gold"][3]);
        return b;
    }

    /// <summary>A lying body (down, dead, crawling, asleep), 8x8, head to the left.</summary>
    public PixelBuffer Fallen()
    {
        var b = new PixelBuffer(8, 8); var t = R["red"]; uint skin = R["skin"][2];
        for (int x = 0; x < 2; x++) for (int y = 3; y < 6; y++) b.Set(x + 1, y, skin);
        for (int x = 3; x < 6; x++) for (int y = 3; y < 6; y++) b.Set(x, y, y == 5 ? t[1] : t[2]);
        for (int x = 6; x < 8; x++) { b.Set(x, 3, R["leather"][0]); b.Set(x, 4, R["brown"][0]); }
        b.Set(2, 3, R["brown"][1]);
        return b.Outline(P["out"]);
    }

    /// <summary>The Command block icon: the team colour (red ramp) in a dark border, the role's glyph in bone.</summary>
    public PixelBuffer Icon(string role)
    {
        var b = new PixelBuffer(8, 8); var g = Glyphs[role]; uint bone = P["bone"], rim = P["out"];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                bool edge = x == 0 || y == 0 || x == 7 || y == 7;
                b.Set(x, y, edge ? rim : (x + y) % 7 == 0 ? R["red"][1] : R["red"][2]);
            }
        for (int y = 0; y < 6; y++) for (int x = 0; x < 6; x++) if (g[y][x] == 'X') b.Set(x + 1, y + 1, bone);
        return b;
    }

    // ---------------------------------------------------------------------------- troop minis

    private sealed record Look(string Head, string Body, string Shield, string Weapon, string Extra = "");

    private static readonly Dictionary<string, Look> Looks = new()
    {
        ["levy"] = new("hair", "tunic", "none", "fork"),
        ["spearman"] = new("cap", "gambeson", "round", "spear"),
        ["archer"] = new("hood", "tunic", "none", "bow", "quiver"),
        ["crossbowman"] = new("hood", "gambeson", "none", "crossbow", "quiver"),
        ["sergeant"] = new("helm", "surcoat", "round", "sword"),
        ["man_at_arms"] = new("helm", "mail", "kite", "sword"),
        ["knight"] = new("great", "surcoat_mail", "kite", "sword"),
        ["engineer"] = new("leathercap", "apron", "none", "mallet"),
        ["sailor"] = new("kerchief", "stripes", "none", "pin"),
        ["marine"] = new("helm", "gambeson", "round", "axe"),
    };

    public PixelBuffer Troop(string role, string view)
    {
        var lk = Looks[role]; var b = new PixelBuffer(8, 16);
        var tm = R["red"]; uint T1 = tm[1], T2 = tm[2], T3 = tm[3];
        uint skin = R["skin"][2], hair = R["brown"][1], leather = R["leather"][0], wood = R["wood"][2], woodD = R["wood"][1], woodL = R["wood"][3];
        uint iron = R["iron"][2], ironL = R["iron"][3], ironD = R["iron"][1], gold = R["gold"][2], linen = R["linen"][1], bone = P["bone"];
        bool back = view == "back", side = view == "side";
        void S(int x, int y, uint c) => b.Set(x, y, c);
        void Rect(int x0, int y0, int x1, int y1, uint c) { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) S(x, y, c); }

        // torso colour by body style
        uint Torso(int x, int y)
        {
            switch (lk.Body)
            {
                case "gambeson": return (y & 1) == 0 ? T1 : T2;
                case "mail": return (x + y) % 2 == 0 ? iron : ironD;
                case "surcoat": return y >= 9 ? T1 : T2;
                case "surcoat_mail": return y >= 8 ? ((x + y) % 2 == 0 ? iron : ironD) : T2;
                case "apron": return y >= 7 ? (y == 7 ? leather : linen) : T2;
                case "stripes": return (y & 1) == 0 ? linen : T2;
                default: return x == 1 || x == 6 ? T1 : T2;
            }
        }

        // body: head, neck, torso, belt, legs
        int tx0 = side ? 2 : 1, tx1 = side ? 5 : 6;
        for (int y = 6; y <= 9; y++) for (int x = tx0; x <= tx1; x++) S(x, y, Torso(x, y));
        for (int x = tx0; x <= tx1; x++) S(x, 10, lk.Body is "apron" ? leather : lk.Body.Contains("mail") ? ironD : leather);
        // arms
        if (!side) { for (int y = 7; y <= 9; y++) { S(0, y, lk.Body.Contains("mail") ? iron : T1); S(7, y, lk.Body.Contains("mail") ? iron : T1); } S(0, 10, skin); S(7, 10, skin); }
        else { for (int y = 7; y <= 9; y++) S(3, y, T1); S(4, 10, skin); }
        // legs
        uint leg = role is "man_at_arms" or "knight" or "marine" or "sergeant" ? ironD : R["brown"][0];
        for (int y = 11; y <= 13; y++)
        {
            if (side) { S(2, y, leg); S(3, y, leg); S(5, y, leg); S(6, y, leg); }
            else { S(2, y, leg); S(3, y, leg); S(4, y, leg); S(5, y, leg); }
        }
        // two legs with a gap below the crotch (front/back); a stride (side)
        if (!side) { for (int y = 12; y <= 13; y++) { b.Set(3, y, 0); b.Set(4, y, 0); } }
        if (side) { S(2, 14, leather); S(3, 14, leather); S(5, 14, leather); S(6, 14, leather); S(7, 14, leather); }
        else { S(2, 14, leather); S(3, 14, leather); S(4, 14, leather); S(5, 14, leather); }

        // head
        int hx0 = 2, hx1 = 5;
        for (int y = 1; y <= 4; y++) for (int x = hx0; x <= hx1; x++) S(x, y, back ? hair : skin);
        S(3, 5, skin); S(4, 5, skin);
        // hair on top
        for (int x = hx0; x <= hx1; x++) { S(x, 1, hair); }
        if (!back && !side) { S(2, 3, skin); S(3, 3, P["out"]); S(4, 3, P["out"]); S(5, 3, skin); } // eyes
        if (side) { S(5, 3, skin); S(4, 3, P["out"]); S(2, 2, hair); S(2, 3, hair); S(2, 4, hair); }
        switch (lk.Head)
        {
            case "cap": Rect(hx0, 1, hx1, 2, leather); break;
            case "hood": Rect(hx0, 1, hx1, 2, T1); if (!side) { S(hx0, 3, T1); S(hx1, 3, T1); } else S(2, 3, T1); if (back) Rect(hx0, 3, hx1, 4, T1); break;
            case "helm": Rect(hx0, 1, hx1, 2, ironL); S(hx0, 3, iron); S(hx1, 3, iron); if (!side && !back) S(3, 3, ironL); if (back) Rect(hx0, 3, hx1, 4, iron); if (side) S(2, 3, iron); break;
            case "great": Rect(hx0, 1, hx1, 4, ironL); Rect(hx0, 3, hx1, 3, back ? ironL : ironD); S(3, 0, T3); S(4, 0, T3); S(3, 1, T2); S(4, 1, T2); break;
            case "leathercap": Rect(hx0, 1, hx1, 2, leather); break;
            case "kerchief": Rect(hx0, 1, hx1, 2, T3); S(hx1 + (side ? 1 : 0), 3, T2); break;
        }

        // shield
        if (lk.Shield != "none")
        {
            uint rim = lk.Shield == "kite" ? ironL : woodD;
            if (back) { Rect(2, 6, 5, 9, T2); Rect(2, 6, 5, 6, rim); if (lk.Shield == "kite") { S(3, 10, T2); S(4, 10, T2); } }
            else if (side) { Rect(4, 6, 5, lk.Shield == "kite" ? 10 : 9, T2); S(4, 6, rim); S(5, 6, rim); S(5, 9, rim); S(3, 8, gold); }
            else
            {
                if (lk.Shield == "kite") { Rect(0, 6, 2, 9, T2); S(1, 10, T2); S(2, 10, T2); S(0, 6, rim); S(1, 6, rim); S(2, 6, rim); S(0, 7, rim); S(1, 8, gold); }
                else { Rect(0, 6, 2, 9, T2); S(0, 6, rim); S(1, 6, rim); S(2, 6, rim); S(0, 9, rim); S(2, 9, rim); S(1, 7, gold); }
            }
        }
        // quiver on the back
        if (lk.Extra == "quiver" && back) { Rect(2, 5, 3, 9, woodD); S(2, 4, bone); S(3, 4, bone); }
        if (lk.Extra == "quiver" && side) { S(2, 6, woodD); S(2, 7, woodD); S(2, 8, woodD); S(1, 5, bone); }

        // weapon, on the right (the viewer's) side; the side view points it east
        const int wx = 7;
        switch (lk.Weapon)
        {
            case "spear": for (int y = 0; y <= 14; y++) S(wx, y, y <= 1 ? ironL : wood); break;
            case "fork": for (int y = 2; y <= 14; y++) S(wx, y, wood); S(wx, 1, iron); S(wx - 1, 1, iron); S(wx - 1, 2, iron); S(wx, 0, ironL); break;
            case "bow": for (int y = 4; y <= 11; y++) S(wx, y, y == 4 || y == 11 ? woodD : wood); S(wx - 1, 5, woodD); S(wx - 1, 10, woodD); if (side) for (int y = 6; y <= 9; y++) S(wx - 1, y, bone); break;
            case "crossbow": S(wx, 6, wood); S(wx, 7, wood); S(wx, 8, wood); S(wx - 1, 7, woodD); S(wx - 2, 7, iron); if (side) { S(wx - 1, 6, iron); S(wx - 1, 8, iron); } break;
            case "sword": for (int y = 3; y <= 8; y++) S(wx, y, y == 3 ? ironL : iron); S(wx, 9, gold); S(wx - 1, 9, gold); S(wx, 10, woodD); break;
            case "mallet": for (int y = 6; y <= 12; y++) S(wx, y, wood); Rect(wx - 1, 4, wx, 5, woodD); break;
            case "pin": for (int y = 7; y <= 10; y++) S(wx, y, wood); S(wx, 6, woodL); break;
            case "axe": for (int y = 4; y <= 12; y++) S(wx, y, wood); Rect(wx - 2, 4, wx - 1, 6, ironL); S(wx - 2, 6, iron); break;
        }
        if (lk.Weapon == "sword" && lk.Head != "great" && role == "sergeant") S(1, 12, woodD); // a sheath at the hip
        b.Outline(P["out"]);
        return b;
    }
}
