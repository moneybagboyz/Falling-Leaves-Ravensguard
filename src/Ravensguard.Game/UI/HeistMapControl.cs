using System;
using Godot;
using Ravensguard.Sim.Heists;

namespace Ravensguard.Game.UI;

/// <summary>The map of the heist panel: cells of the site, shaded by their light, with the hero and the guards the hero can see.</summary>
public partial class HeistMapControl : Control
{
    public HeistView? View { get; set; }

    static Color Of(char g) => g switch
    {
        '#' => new Color(0.34f, 0.28f, 0.25f), ' ' => new Color(0.04f, 0.04f, 0.05f), '~' => new Color(0.18f, 0.30f, 0.34f), '+' => new Color(0.55f, 0.38f, 0.20f), '/' => new Color(0.38f, 0.30f, 0.20f),
        '1' or '2' or '3' or '4' or '5' => new Color(0.75f, 0.45f, 0.18f), 'c' => new Color(0.62f, 0.62f, 0.66f), 'o' => new Color(0.40f, 0.40f, 0.44f), 'g' => new Color(0.55f, 0.58f, 0.62f), 'G' => new Color(0.30f, 0.34f, 0.38f),
        'T' => new Color(0.85f, 0.70f, 0.20f), 'x' => new Color(0.30f, 0.65f, 0.40f), 'u' or 'D' => new Color(0.30f, 0.55f, 0.75f), 'S' => new Color(1.0f, 0.82f, 0.35f), 's' => new Color(0.45f, 0.38f, 0.28f),
        'h' => new Color(0.26f, 0.32f, 0.26f), 'b' => new Color(0.45f, 0.35f, 0.40f), 'P' => new Color(0.75f, 0.35f, 0.35f), 'f' => new Color(0.45f, 0.70f, 0.45f), _ => new Color(0.66f, 0.62f, 0.54f),
    };

    static Color OfGuard(GState s) => s switch
    {
        GState.Asleep => new Color(0.55f, 0.55f, 0.70f), GState.Down => new Color(0.40f, 0.40f, 0.40f), GState.Bribed => new Color(0.55f, 0.75f, 0.55f), GState.Investigate or GState.Looking or GState.Fire => new Color(1f, 0.85f, 0.2f),
        GState.Alert or GState.Pursue => new Color(1f, 0.25f, 0.2f), _ => new Color(0.95f, 0.55f, 0.25f),
    };

    static bool Buried(HeistView v, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= v.W || ny >= v.H) continue;
                if (v.Glyphs[ny * v.W + nx] is not ('#' or ' ')) return false;
            }
        return true;
    }

    public override void _Draw()
    {
        var v = View; DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.05f, 0.05f, 0.06f));
        if (v == null || v.W == 0) return;
        float cell = Math.Clamp(Math.Min((Size.X - 4) / v.W, (Size.Y - 4) / v.H), 6f, 28f);
        var o = new Vector2((Size.X - cell * v.W) / 2f, (Size.Y - cell * v.H) / 2f);
        for (int y = 0; y < v.H; y++)
            for (int x = 0; x < v.W; x++)
            {
                int i = y * v.W + x; char g = v.Glyphs[i];
                var r = new Rect2(o + new Vector2(x * cell, y * cell), new Vector2(cell - 1, cell - 1));
                var c = Of(g);
                float lit = v.Light[i] / 255f;
                bool wall = g is '#' or ' ';
                if (g == '#' && Buried(v, x, y)) c = Of(' ');                       // a wall with no floor beside it is solid rock
                if (!wall) c = c.Lerp(Colors.Black, Math.Clamp(0.88f - lit * 0.88f, 0f, 0.88f));
                DrawRect(r, c);
                if (g is '1' or '2' or '3' or '4' or '5') DrawString(ThemeDB.FallbackFont, r.Position + new Vector2(cell * 0.2f, cell * 0.85f), g.ToString(), HorizontalAlignment.Left, -1, (int)(cell * 0.7f), Colors.Black);
            }
        foreach (var gd in v.Guards)
        {
            var p = o + new Vector2((gd.X + 0.5f) * cell, (gd.Y + 0.5f) * cell);
            if (!gd.Visible) { DrawCircle(p, cell * 0.12f, new Color(1, 1, 1, 0.20f)); continue; }
            DrawCircle(p, cell * 0.38f, OfGuard(gd.State)); DrawCircle(p, cell * 0.38f, Colors.Black, false, 1f);
            if (gd.Keeper) DrawString(ThemeDB.FallbackFont, p + new Vector2(-cell * 0.25f, cell * 0.28f), "K", HorizontalAlignment.Left, -1, (int)(cell * 0.7f), Colors.Black);
        }
        var h = o + new Vector2((v.HeroX + 0.5f) * cell, (v.HeroY + 0.5f) * cell);
        DrawCircle(h, cell * 0.42f, new Color(0.35f, 0.75f, 1f)); DrawCircle(h, cell * 0.42f, Colors.White, false, 1.5f);
        if (v.Torch) DrawCircle(h, cell * 0.65f, new Color(1f, 0.8f, 0.3f, 0.18f));
    }
}
