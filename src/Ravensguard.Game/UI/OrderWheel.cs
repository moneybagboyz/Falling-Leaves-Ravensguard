using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.World.Combat;
using Ravensguard.Sim.Battle;

namespace Ravensguard.Game.UI;

/// <summary>
/// The order wheel (R held; battle-bubble-and-formations 4.8, plan 2.8): three rings, Orders (Advance, Charge, Hold, Retreat, Follow me, Flank left, Flank right, Rally), Shape (Line, Shield wall, Loose, Column,
/// Set against cavalry, Wedge) and Missiles (Fire at will, Hold fire, Shoot at...). The mouse (direction picks the sector, distance the ring) or the arrows pick; Tab turns to the next ring; releasing the key,
/// Enter or a click issues to the selection (with nothing selected, to formation 1); Esc cancels. Each sector's tooltip says when the order would arrive and by what signal. The Shape ring is where
/// Phase 4's squad shapes went when F1 to F4 became formations (overview Q9); with no army the squad takes them as before. A view: the order leaves through <see cref="BattleCommand"/>.
/// </summary>
public partial class OrderWheel : Control
{
    readonly BattleCommand _cmd;
    readonly string _holdAction;
    readonly Action _closed;
    readonly Font _font;
    int _ring, _sector = -1;
    static readonly float[] Inner = [62, 140, 196], Outer = [136, 192, 232];

    public OrderWheel(BattleCommand cmd, string holdAction, Action closed)
    {
        _cmd = cmd; _holdAction = holdAction; _closed = closed; Name = "OrderWheel";
        _font = GD.Load<Font>("res://assets/fonts/PixelifySans.ttf") ?? ThemeDB.FallbackFont;
    }

    public int Ring => _ring;
    public int Sector => _sector;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ActiveContexts.Enter(InputContext.OrderWheel);
    }

    public override void _ExitTree() { ActiveContexts.Exit(InputContext.OrderWheel); }

    float Zm { get { var s = Size; return Math.Clamp((Math.Min(s.X, s.Y) - 96) / 480f, 0.5f, 1f); } }
    Vector2 Centre => new(Size.X / 2, Size.Y / 2 - 12 * Zm);

    public void Pick(int ring, int sector) { _ring = Math.Clamp(ring, 0, 2); _sector = sector; QueueRedraw(); }

    /// <summary>Gives the picked sector's order to the selection and closes the wheel; false when nothing is picked or the sector is off.</summary>
    public bool Issue()
    {
        if (_done) return false;
        var items = _cmd.Ring(_ring);
        if (_sector < 0 || _sector >= items.Count || items[_sector].Off) return false;
        _cmd.Give(items[_sector], Input.IsKeyPressed(Key.Shift));
        Close();
        return true;
    }

    bool _done;
    /// <summary>Closes at once (the key's release may follow in the same frame and must not give the order twice).</summary>
    public void Close() { if (_done) return; _done = true; SetProcessInput(false); Visible = false; _closed(); QueueFree(); }

    public override void _Input(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventMouseMotion mm) { Hover(mm.Position); AcceptEvent(); return; }
        if (Actions.Released(e, _holdAction)) { Issue(); GetViewport().SetInputAsHandled(); return; }
        if (e is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true })
        {
            if (Actions.Pressed(e, "orderwheel.cancel")) { Close(); GetViewport().SetInputAsHandled(); return; }
            if (Actions.Pressed(e, "orderwheel.ring_next")) { _ring = (_ring + 1) % 3; _sector = -1; QueueRedraw(); GetViewport().SetInputAsHandled(); return; }
            int n = _cmd.Ring(_ring).Count;
            if (Actions.Pressed(e, "orderwheel.left") || Actions.Pressed(e, "orderwheel.up")) { _sector = _sector < 0 ? n - 1 : (_sector + n - 1) % n; QueueRedraw(); GetViewport().SetInputAsHandled(); return; }
            if (Actions.Pressed(e, "orderwheel.right") || Actions.Pressed(e, "orderwheel.down")) { _sector = _sector < 0 ? 0 : (_sector + 1) % n; QueueRedraw(); GetViewport().SetInputAsHandled(); return; }
            if (Actions.Pressed(e, "orderwheel.issue")) { if (e is InputEventMouseButton mb) Hover(mb.Position); Issue(); GetViewport().SetInputAsHandled(); return; }
            if (e is InputEventMouseButton) GetViewport().SetInputAsHandled();
        }
    }

    void Hover(Vector2 p)
    {
        var d = p - Centre; float r = d.Length() / Zm;
        if (r < Inner[0] - 8) { _sector = -1; QueueRedraw(); return; }
        int ring = r < Outer[0] + 2 ? 0 : r < Outer[1] + 2 ? 1 : 2;
        int n = _cmd.Ring(ring).Count;
        float a = Mathf.Atan2(d.Y, d.X) + Mathf.Pi / 2 + Mathf.Pi / n;          // sector 0 is centred straight up, clockwise
        a = ((a % Mathf.Tau) + Mathf.Tau) % Mathf.Tau;
        _ring = ring; _sector = Math.Min(n - 1, (int)(a / Mathf.Tau * n));
        QueueRedraw();
    }

    public override void _Draw()
    {
        var sz = Size; float s = Zm; var c = Centre;
        DrawRect(new Rect2(Vector2.Zero, sz), new Color(0, 0, 0, 0.55f));
        for (int ring = 0; ring < 3; ring++)
        {
            var items = _cmd.Ring(ring); int n = items.Count;
            for (int i = 0; i < n; i++)
            {
                float a0 = -Mathf.Pi / 2 + (i - 0.5f) * Mathf.Tau / n, a1 = a0 + Mathf.Tau / n;
                bool on = ring == _ring && i == _sector;
                var fill = items[i].Off ? new Color(0.14f, 0.13f, 0.12f, 0.85f) : on ? new Color(0.55f, 0.42f, 0.14f, 0.96f) : ring == _ring ? new Color(0.2f, 0.17f, 0.14f, 0.93f) : new Color(0.13f, 0.11f, 0.1f, 0.88f);
                DrawColoredPolygon(Ann(c, Inner[ring] * s, Outer[ring] * s, a0 + 0.01f, a1 - 0.01f), fill);
                float am = (a0 + a1) / 2, rm = (Inner[ring] + Outer[ring]) / 2 * s;
                var p = c + new Vector2(Mathf.Cos(am), Mathf.Sin(am)) * rm;
                var txt = L.T(items[i].Key);
                float w = ring == 0 ? 80 : 150;
                var col = items[i].Off ? PixelTheme.Dim : on ? Colors.White : PixelTheme.Body;
                DrawString(_font, p + new Vector2(-w / 2, 5), txt, HorizontalAlignment.Center, w, 13, col);
            }
        }
        DrawCircle(c, Inner[0] * s - 8, new Color(0.1f, 0.09f, 0.08f, 0.92f));
        DrawString(_font, c + new Vector2(-45, -2), L.T(_cmd.RingNames[_ring]), HorizontalAlignment.Center, 90, 14, PixelTheme.Gold);
        DrawString(_font, c + new Vector2(-45, 16), $"{_ring + 1}/3", HorizontalAlignment.Center, 90, 12, PixelTheme.Dim);
        // the tooltip, the target and the keys
        string tip = Tip();
        float y = Math.Min(sz.Y - 64, c.Y + (Outer[2] + 4) * s);
        DrawRect(new Rect2(sz.X / 2 - 330, y - 2, 660, 62), new Color(0, 0, 0, 0.78f));
        DrawString(_font, new Vector2(0, y + 14), tip, HorizontalAlignment.Center, sz.X, 15, Colors.White);
        var rows = _cmd.Targets();
        string to = rows.Count == 0 ? L.T("bcmd_squad") : string.Join(", ", rows.ConvertAll(r => r.Id.ToString()));
        DrawString(_font, new Vector2(0, y + 32), L.F("bcmd_wheel_target", to) + (_cmd.Selected.Count == 0 && rows.Count > 0 ? "  (" + L.T("bcmd_wheel_none") + ")" : ""), HorizontalAlignment.Center, sz.X, 13, PixelTheme.Dim);
        DrawString(_font, new Vector2(0, y + 50), L.F("bcmd_wheel_hint", InputBootstrap.Hint("orderwheel.ring_next"), InputBootstrap.Hint("combatstance.order_wheel"), InputBootstrap.Hint("orderwheel.issue"), InputBootstrap.Hint("orderwheel.cancel")),
            HorizontalAlignment.Center, sz.X, 12, PixelTheme.Dim);
    }

    string Tip()
    {
        var items = _cmd.Ring(_ring);
        if (_sector < 0 || _sector >= items.Count) return "";
        var it = items[_sector];
        string name = L.T(it.Key);
        if (it.Off) return L.F("bcmd_tip_off", name);
        if (_cmd.Targets().Count == 0) return L.F("bcmd_tip_squad", name);
        if (_cmd.Preview(it.Kind == OrderKind.None ? OrderKind.Hold : it.Kind) is not { } p) return name;
        return L.F("bcmd_tip", name, (p.Ticks / 20.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), L.T("bcmd_signal_" + p.Signal.ToString().ToLowerInvariant()));
    }

    static Vector2[] Ann(Vector2 c, float r0, float r1, float a0, float a1)
    {
        int steps = Math.Max(3, (int)((a1 - a0) / 0.12f));
        var pts = new Vector2[(steps + 1) * 2];
        for (int i = 0; i <= steps; i++)
        {
            float a = a0 + (a1 - a0) * i / steps; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            pts[i] = c + d * r1; pts[pts.Length - 1 - i] = c + d * r0;
        }
        return pts;
    }
}
