using System;
using Godot;
using Ravensguard.Sim.Naval;

namespace Ravensguard.Game.UI;

/// <summary>
/// The helm HUD (P8-WS-1, war screens 3.1; the Mounted context at the helm): a box at the lower left with the wind (from, degrees and m/s, with an arrow), the heading, the speed over ground, the sail set, the sea state,
/// the water in the hull and the crew orders in force (row, ship oars, ready grapples, board). It draws a <see cref="HelmView"/> the ship's frame and state built; it keeps nothing and sends nothing.
/// 960 x 540 is its floor: 252 x 118 pixels.
/// </summary>
public partial class HelmHud : Control
{
    public const int W = 252, H = 118;
    readonly Func<HelmView?> _view;
    Font _font = null!;
    HelmView? _shown;

    public HelmHud(Func<HelmView?> view) { _view = view; Name = "HelmHud"; }

    public override void _Ready()
    {
        _font = GD.Load<Font>("res://assets/fonts/PixelifySans.ttf") ?? ThemeDB.FallbackFont;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = Size = new Vector2(W, H);
        Layout(); GetViewport().SizeChanged += Layout;
    }

    public override void _ExitTree() { if (GetViewport() != null) GetViewport().SizeChanged -= Layout; }

    void Layout() { var vp = GetViewportRect().Size; Position = new Vector2(8, Math.Max(8, vp.Y - H - 120)); }

    public override void _Process(double delta)
    {
        var v = _view(); bool changed = !Equals(v, _shown) && !(v != null && _shown != null && v.Equals(_shown));
        _shown = v; Visible = v != null;
        if (changed || v != null) QueueRedraw();
    }

    /// <summary>The lines the HUD writes, in order (also what the self-test reads).</summary>
    public static string[] Lines(HelmView v)
    {
        string orders = v.Orders.Count == 0 ? L.T("hm_orders_none") : string.Join(", ", System.Linq.Enumerable.Select(v.Orders, o => L.T("hm_order_" + o.ToString().ToLowerInvariant())));
        return
        [
            L.F("hm_wind", v.WindFromDeg, v.WindMps),
            L.F("hm_heading", v.HeadingDeg),
            L.F("hm_speed", v.SpeedMmps / 1000, v.SpeedMmps / 100 % 10, HelmView.KnotsX10(v.SpeedMmps) / 10, HelmView.KnotsX10(v.SpeedMmps) % 10),
            L.F("hm_sail", v.SailM2),
            L.F("hm_sea", L.T(v.SeaKey), v.WaveMm / 1000, v.WaveMm / 100 % 10),
            L.F("hm_water", v.WaterL),
            L.F("hm_orders", orders),
        ];
    }

    public override void _Draw()
    {
        if (_shown is not { } v) return;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.62f));
        DrawRect(new Rect2(Vector2.Zero, Size), PixelTheme.Rim, false, 1);
        DrawString(_font, new Vector2(8, 15), L.T("hm_title"), HorizontalAlignment.Left, -1, 13, PixelTheme.Gold);
        var lines = Lines(v);
        for (int i = 0; i < lines.Length; i++) DrawString(_font, new Vector2(8, 31 + i * 14), lines[i], HorizontalAlignment.Left, W - 60, 12, i == 5 && v.WaterL > 0 ? new Color(0.95f, 0.45f, 0.35f) : Colors.White);
        // the compass: the wind blows toward the arrow's head (it comes from the bearing named)
        var c = new Vector2(W - 28, 32); const float R = 20;
        DrawArc(c, R, 0, Mathf.Tau, 24, PixelTheme.Rim, 1);
        Vector2 Dir(int deg) { float a = Mathf.DegToRad(deg - 90); return new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }
        DrawLine(c, c + Dir(v.HeadingDeg) * R, new Color(0.85f, 0.8f, 0.5f), 2);                                // the heading
        var to = Dir((v.WindFromDeg + 180) % 360); DrawLine(c - to * R * 0.8f, c + to * R * 0.8f, new Color(0.5f, 0.75f, 0.95f), 1);      // the wind
        DrawCircle(c + to * R * 0.8f, 2, new Color(0.5f, 0.75f, 0.95f));
    }
}
