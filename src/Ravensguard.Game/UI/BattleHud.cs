using System;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.World.Combat;
using Ravensguard.Sim.Battle;

namespace Ravensguard.Game.UI;

/// <summary>
/// The battle HUD of the command layer (P7-BB-D, battle-bubble-and-formations 9): a strip with the two sides' standing and fallen and a morale bar each, the key hint, and at the left the cards of
/// formations F1 to F4 (and "N more") with shape, men, cohesion, morale and the order in force; a click selects. A view of the fight's formation book through <see cref="BattleCommand"/>; it commands nothing itself
/// beyond selecting. Shown only while the hero's side has formations.
/// </summary>
public partial class BattleHud : Control
{
    readonly BattleCommand _cmd;
    Font _font = null!;
    Strip _strip = null!; Cards _cards = null!;

    public BattleHud(BattleCommand cmd) { _cmd = cmd; Name = "BattleHud"; }

    /// <summary>The mouse is over one of the HUD's boxes (the click is the card's, not the sword arm's).</summary>
    public bool Covers(Vector2 p) => Visible && IsInsideTree() && (new Rect2(_strip.GlobalPosition, _strip.Size).HasPoint(p) || (_cards.Visible && new Rect2(_cards.GlobalPosition, _cards.Size).HasPoint(p)));

    public override void _Ready()
    {
        _font = GD.Load<Font>("res://assets/fonts/PixelifySans.ttf") ?? ThemeDB.FallbackFont;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _strip = new Strip(this) { Name = "Strip", Position = new Vector2(8, 108), CustomMinimumSize = new Vector2(540, 44), Size = new Vector2(540, 44), MouseFilter = MouseFilterEnum.Ignore };
        _cards = new Cards(this) { Name = "Cards", Position = new Vector2(8, 158), MouseFilter = MouseFilterEnum.Stop };
        AddChild(_strip); AddChild(_cards);
        Layout();
        GetViewport().SizeChanged += Layout;
    }

    public override void _ExitTree() { if (GetViewport() != null) GetViewport().SizeChanged -= Layout; }

    void Layout()
    {
        var vp = GetViewportRect().Size;
        float w = Math.Min(540, vp.X - 16);
        _strip.CustomMinimumSize = _strip.Size = new Vector2(w, 44);
        // the cards sit under the strip; at least four fit above the log feed and the watch box of the bottom corners
        _cards.Size = _cards.CustomMinimumSize = new Vector2(Math.Min(236, vp.X - 16), 4 * (CardH + 3) + 18);
    }

    const float CardH = 42;

    public override void _Process(double delta)
    {
        bool on = _cmd.Active && _cmd.Mine().Count > 0;
        Visible = on;
        if (on) { _strip.QueueRedraw(); _cards.QueueRedraw(); }
    }

    static Color Bar(float f, bool good) => good ? new Color(0.35f, 0.75f, 0.3f).Lerp(new Color(0.85f, 0.25f, 0.2f), 1 - Math.Clamp(f, 0, 1)) : new Color(0.7f, 0.7f, 0.75f);

    int MoralePct(System.Collections.Generic.IEnumerable<FormationRow> rows)
    {
        long sum = 0; int n = 0;
        foreach (var r in rows) foreach (int id in r.Members) { var f = _cmd.W.Fighters[id]; if (!f.Active) continue; sum += f.Morale; n++; }
        return n == 0 ? 0 : (int)Math.Clamp(sum / n, 0, 100);
    }

    sealed partial class Strip(BattleHud h) : Control
    {
        public override void _Draw()
        {
            var cmd = h._cmd; var w = cmd.W; var f = h._font;
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.62f));
            DrawRect(new Rect2(Vector2.Zero, Size), PixelTheme.Rim, false, 1);
            float x = 8;
            foreach (bool mine in new[] { true, false })
            {
                var rows = (mine ? cmd.Mine() : cmd.Foes());
                int up = rows.Sum(r => cmd.Standing(r)), total = rows.Sum(r => r.StartCount), down = Math.Max(0, total - up);
                int mor = h.MoralePct(rows);
                float half = (Size.X - 16) / 2;
                DrawString(f, new Vector2(x, 17), L.F(mine ? "bcmd_side_you" : "bcmd_side_foe", up, down), HorizontalAlignment.Left, half - 8, 13, mine ? PixelTheme.Body : new Color(0.95f, 0.7f, 0.6f));
                DrawRect(new Rect2(x, 22, half - 70, 6), new Color(0.15f, 0.13f, 0.12f));
                DrawRect(new Rect2(x, 22, (half - 70) * mor / 100f, 6), Bar(mor / 100f, true));
                DrawString(f, new Vector2(x + half - 64, 29), L.F("bcmd_morale", mor), HorizontalAlignment.Left, 64, 11, PixelTheme.Dim);
                x += half;
            }
            DrawString(f, new Vector2(8, 41), L.F("bcmd_hint", InputBootstrap.Hint("combatstance.formation_1").Trim('[', ']') + "-" + InputBootstrap.Hint("combatstance.formation_4").Trim('[', ']'),
                InputBootstrap.Hint("combatstance.order_wheel").Trim('[', ']'), InputBootstrap.Hint("combatstance.tactical_overlay").Trim('[', ']')), HorizontalAlignment.Left, Size.X - 16, 12, PixelTheme.Gold);
        }
    }

    sealed partial class Cards(BattleHud h) : Control
    {
        System.Collections.Generic.List<FormationRow> Shown(out int more)
        {
            var rows = h._cmd.Mine();
            var shown = rows.Where(r => r.Id is >= 1 and <= 4).Take(4).ToList();
            more = rows.Count - shown.Count;
            return shown;
        }

        public override void _Draw()
        {
            var cmd = h._cmd; var w = cmd.W; var f = h._font;
            var shown = Shown(out int more);
            for (int i = 0; i < shown.Count; i++)
            {
                var r = shown[i]; float y = i * (CardH + 3);
                bool sel = cmd.IsSelected(r);
                var box = new Rect2(0, y, Size.X, CardH);
                DrawRect(box, new Color(0, 0, 0, 0.66f));
                DrawRect(box, sel ? PixelTheme.Gold : PixelTheme.Rim, false, sel ? 2 : 1);
                int up = cmd.Standing(r);
                string shape = L.T("bcmd_shape_" + FormationRules.Snake(r.Shape.ToString()));
                DrawString(f, new Vector2(6, y + 15), $"[F{r.Id}] {shape}", HorizontalAlignment.Left, Size.X - 90, 13, sel ? PixelTheme.Gold : PixelTheme.Body);
                DrawString(f, new Vector2(Size.X - 84, y + 15), L.F("bcmd_card_men", up, r.StartCount), HorizontalAlignment.Right, 78, 13, PixelTheme.Body);
                int coh = Math.Clamp(r.Cohesion / 10, 0, 100), mor = h.MoralePct([r]);
                DrawRect(new Rect2(6, y + 20, 52, 4), new Color(0.15f, 0.13f, 0.12f)); DrawRect(new Rect2(6, y + 20, 52 * coh / 100f, 4), Bar(coh / 100f, false));
                DrawRect(new Rect2(62, y + 20, 52, 4), new Color(0.15f, 0.13f, 0.12f)); DrawRect(new Rect2(62, y + 20, 52 * mor / 100f, 4), Bar(mor / 100f, true));
                string ord = r.Order.Kind == OrderKind.None ? L.T("bcmd_idle") : L.T(BattleCommand.Name(r.Order.Kind));
                if (r.PendingCount > 0) ord += " +" + r.PendingCount;
                DrawString(f, new Vector2(6, y + 37), ord, HorizontalAlignment.Left, Size.X - 12, 12, PixelTheme.Dim);
                DrawString(f, new Vector2(118, y + 26), L.F("bcmd_card_stats", coh, mor), HorizontalAlignment.Left, Size.X - 122, 11, PixelTheme.Dim);
            }
            if (more > 0) DrawString(f, new Vector2(4, shown.Count * (CardH + 3) + 13), L.F("bcmd_more", more), HorizontalAlignment.Left, Size.X - 8, 12, PixelTheme.Dim);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
            var shown = Shown(out _);
            int i = (int)(mb.Position.Y / (CardH + 3));
            if (i >= 0 && i < shown.Count) { h._cmd.SelectRow(shown[i].Id, mb.ShiftPressed); AcceptEvent(); }
        }
    }
}
