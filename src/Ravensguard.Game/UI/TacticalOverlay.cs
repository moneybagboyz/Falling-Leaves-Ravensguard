using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Game.World.Combat;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Game.UI;

/// <summary>
/// The tactical pause overlay (key 0; battle-bubble-and-formations 4.8, 9): the game is paused and the formations are drawn as blocks with their facing, cohesion and morale, their orders in force and on the way
/// as arrows with countdowns; only the enemies the side can see are drawn, the rest as ghosts of where they were last seen. LMB selects a block, dragging it places a move; RMB drags a frontage (start to end
/// sets the width and the facing across the line); clicking an enemy block orders an attack on it; Shift queues; Ctrl+Z takes back the last placement; Space or 0 resumes; Esc closes the overlay and the game
/// stays paused. Placements are drafted and go out as recorded inputs when the game resumes, at the tick it was paused at, so each order's delay runs from resume and pausing gives no free time (BB-11).
/// Left the list of every formation, right the selected formation's card, bottom the key bar. A view: it reads the fight's book and snapshot and commands only through <see cref="BattleCommand"/>.
/// </summary>
public partial class TacticalOverlay : Control
{
    readonly BattleCommand _cmd; readonly FightView _view;
    readonly Font _font;
    readonly BattleSnapshot _snap = new();
    int _snapTick = -1;
    Vector2 _centreM; float _ppm = 1f;
    ListBox _list = null!; VBoxContainer _card = null!; Label _keys = null!, _banner = null!;
    Button _captain = null!;
    MapView _map = null!;
    OrderWheel? _wheel;
    // pointer state
    byte _dragRow; bool _dragging; Vector2 _dragFrom, _dragNow; bool _rmb;
    List<(FormationRow Row, int X, int Y)> _seen = []; List<(FormationRow Row, int X, int Y, int AgeTicks)> _ghosts = [];
    string _cardKey = "";

    public TacticalOverlay(BattleCommand cmd, FightView view)
    {
        _cmd = cmd; _view = view; Name = "TacticalOverlay";
        _font = GD.Load<Font>("res://assets/fonts/PixelifySans.ttf") ?? ThemeDB.FallbackFont;
    }

    public bool WheelOpen => _wheel != null && IsInstanceValid(_wheel) && !_wheel.IsQueuedForDeletion();

    // ------------------------------------------------------------------ build

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ActiveContexts.Enter(InputContext.Tactical);
        // the live view dims behind the map and the panels (the game is paused; the HUD of the fight is not the overlay's)
        AddChild(new ColorRect { Color = new Color(0.03f, 0.03f, 0.025f, 0.97f), MouseFilter = MouseFilterEnum.Ignore, Name = "Dim" });
        GetChild<ColorRect>(0).SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _map = new MapView(this) { Name = "Map", MouseFilter = MouseFilterEnum.Ignore, ClipContents = true };
        _map.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); _map.OffsetLeft = 206; _map.OffsetTop = 34; _map.OffsetRight = -252; _map.OffsetBottom = -46;
        AddChild(_map);
        _banner = new Label { Text = L.T("bcmd_ov_paused"), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _banner.AddThemeColorOverride("font_color", PixelTheme.Gold); _banner.AddThemeFontSizeOverride("font_size", 15);
        _banner.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide); _banner.OffsetLeft = 8; _banner.OffsetRight = -8; _banner.OffsetTop = 4; _banner.OffsetBottom = 28;
        AddChild(_banner);
        // the list of every formation, at the left
        var left = new PanelContainer { Name = "ListPanel", MouseFilter = MouseFilterEnum.Stop };
        left.SetAnchorsAndOffsetsPreset(LayoutPreset.LeftWide); left.OffsetLeft = 8; left.OffsetRight = 200; left.OffsetTop = 34; left.OffsetBottom = -46;
        left.AddThemeStyleboxOverride("panel", PixelTheme.PanelBox(new Color(0.08f, 0.07f, 0.06f, 1f), PixelTheme.Rim, 6));
        var lv = new VBoxContainer(); lv.AddThemeConstantOverride("separation", 4); left.AddChild(lv);
        lv.AddChild(UiKit.Title(L.T("bcmd_ov_list")));
        _list = new ListBox(this) { CustomMinimumSize = new Vector2(170, 40) };
        var sc = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(176, 0) };
        sc.AddChild(_list); lv.AddChild(sc);
        AddChild(left);
        // the selected formation's card, at the right
        var right = new PanelContainer { Name = "CardPanel", MouseFilter = MouseFilterEnum.Stop };
        right.SetAnchorsAndOffsetsPreset(LayoutPreset.RightWide); right.OffsetLeft = -244; right.OffsetRight = -8; right.OffsetTop = 34; right.OffsetBottom = -46;
        right.AddThemeStyleboxOverride("panel", PixelTheme.PanelBox(new Color(0.08f, 0.07f, 0.06f, 1f), PixelTheme.Rim, 6));
        var rsc = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _card = new VBoxContainer { CustomMinimumSize = new Vector2(214, 0) }; _card.AddThemeConstantOverride("separation", 3);
        rsc.AddChild(_card); right.AddChild(rsc);
        AddChild(right);
        // the key bar, at the bottom
        _keys = new Label { MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart, VerticalAlignment = VerticalAlignment.Center };
        _keys.AddThemeFontSizeOverride("font_size", 13); _keys.AddThemeColorOverride("font_color", PixelTheme.Gold);
        _keys.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide); _keys.OffsetLeft = 8; _keys.OffsetRight = -8; _keys.OffsetTop = -42; _keys.OffsetBottom = -6;
        AddChild(_keys);
        _cmd.Staging = true;
        Refresh(true);
        FitAll();
        InputBootstrap.Hints.Changed += Rekey;
        Rekey();
    }

    public override void _ExitTree() { ActiveContexts.Exit(InputContext.Tactical); InputBootstrap.Hints.Changed -= Rekey; }

    string K(string id) => InputBootstrap.Hint(id).Trim('[', ']');

    void Rekey()
    {
        if (_keys == null) return;
        _keys.Text = L.F("bcmd_ov_keys", K("tactical.select"), K("tactical.frontage"), K("tactical.queue"), K("tactical.undo"), K("tactical.clear_queue"), K("tactical.order_wheel"), K("tactical.resume"), K("tactical.close"));
    }

    // ------------------------------------------------------------------ the map (world mm to pixels)

    Rect2 MapRect => new(206, 34, Math.Max(100, Size.X - 206 - 252), Math.Max(100, Size.Y - 34 - 46));
    Vector2 ToPx(float xMm, float yMm) { var r = MapRect; return r.GetCenter() + (new Vector2(xMm, yMm) / 1000f - _centreM) * _ppm; }
    Vector2 ToMm(Vector2 px) { var r = MapRect; var m = (px - r.GetCenter()) / _ppm + _centreM; return m * 1000f; }

    /// <summary>Zooms and pans so every block drawn (ours, the seen enemy, the ghosts) is in the map.</summary>
    public void FitAll()
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        void Add(float x, float y, float pad) { x0 = Math.Min(x0, x - pad); x1 = Math.Max(x1, x + pad); y0 = Math.Min(y0, y - pad); y1 = Math.Max(y1, y + pad); }
        foreach (var m in Marks(true)) Add(m.CentreX / 1000f, m.CentreY / 1000f, 22);
        foreach (var s in _seen) Add(s.X / 1000f, s.Y / 1000f, 22);
        foreach (var g in _ghosts) Add(g.X / 1000f, g.Y / 1000f, 22);
        if (x0 > x1) { x0 = -50; x1 = 50; y0 = -30; y1 = 30; }
        var r = MapRect;
        _centreM = new Vector2((x0 + x1) / 2, (y0 + y1) / 2);
        _ppm = Math.Clamp(Math.Min(r.Size.X / Math.Max(20, x1 - x0), r.Size.Y / Math.Max(20, y1 - y0)), 0.2f, 12f);
        QueueRedraw();
    }

    IEnumerable<FormationMark> Marks(bool mine)
    {
        for (int i = 0; i < _snap.FormCount; i++) { var m = _snap.Forms[i]; if ((m.Team == _cmd.Team) == mine) yield return m; }
    }

    void Retake()
    {
        if (_snapTick == _cmd.W.Tick) return;
        BattleSnapshot.Capture(_cmd.W, _snap); _snapTick = _cmd.W.Tick;
        _cmd.Observe(out _seen, out _ghosts);
    }

    // ------------------------------------------------------------------ the frame

    public override void _Process(double delta)
    {
        Retake();
        Refresh(false);
        QueueRedraw(); _map.QueueRedraw();
    }

    string CardKey()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var r in _cmd.Targets()) sb.Append(r.Id).Append(':').Append(r.Order.Kind).Append(r.QueueCount).Append(r.PendingCount).Append(r.CaptainAlways).Append(_cmd.Standing(r)).Append(_cmd.W.Tick / 20);
        sb.Append(_cmd.Draft.Count).Append(_cmd.Selected.Count).Append(_cmd.Mine().Count);
        return sb.ToString();
    }

    void Refresh(bool force)
    {
        var key = CardKey();
        if (!force && key == _cardKey) return;
        _cardKey = key;
        _list.Refresh();
        foreach (var c in _card.GetChildren()) { _card.RemoveChild(c); c.QueueFree(); }
        var rows = _cmd.Targets();
        if (rows.Count == 0) { _card.AddChild(Line(L.T("bcmd_ov_nothing"), PixelTheme.Dim)); return; }
        var r = rows[0]; var w = _cmd.W;
        string shape = L.T("bcmd_shape_" + FormationRules.Snake(r.Shape.ToString()));
        _card.AddChild(UiKit.Title($"F{r.Id}  {shape}"));
        if (rows.Count > 1) _card.AddChild(Line("+" + string.Join(", ", rows.Skip(1).Select(x => "F" + x.Id)), PixelTheme.Dim));
        var cap = r.Captain >= 0 && w.Fighters[r.Captain].Active ? w.Fighters[r.Captain] : null;
        _card.AddChild(Line(cap == null ? L.T("bcmd_ov_no_captain") : L.F("bcmd_ov_captain", cap.Name, Math.Max(0, cap.Kit.Tactics)), PixelTheme.Body));
        _card.AddChild(Line(L.F("bcmd_ov_drill", r.Drill / 10), PixelTheme.Body));
        _card.AddChild(Line(L.F("bcmd_ov_men", _cmd.Standing(r), r.StartCount), PixelTheme.Body));
        int mor = 0, n = 0; foreach (int id in r.Members) { var f = w.Fighters[id]; if (f.Active) { mor += f.Morale; n++; } }
        _card.AddChild(Line(L.F("bcmd_ov_state", Math.Clamp(r.Cohesion / 10, 0, 100), n == 0 ? 0 : Math.Clamp(mor / n, 0, 100)), PixelTheme.Body));
        _card.AddChild(UiKit.Hint(L.T("bcmd_ov_orders")));
        _card.AddChild(Line(L.F("bcmd_ov_now", OrderText(r.Order)), PixelTheme.Gold));
        for (int i = 0; i < r.QueueCount; i++) _card.AddChild(Line(L.F("bcmd_ov_queued", OrderText(r.Queue[i])), PixelTheme.Body));
        for (int i = 0; i < r.PendingCount; i++)
            _card.AddChild(Line(L.F("bcmd_ov_pending", OrderText(r.Pending[i]), (FormationBook.ArrivesIn(w, r.Pending[i]) / 20.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)), new Color(0.6f, 0.85f, 1f)));
        foreach (var d in _cmd.Draft.Of(r.Id)) _card.AddChild(Line(L.F("bcmd_ov_draft", DraftText(d)), new Color(0.6f, 1f, 0.6f)));
        if (_cmd.NewLost() > 0) _card.AddChild(Line(L.T("bcmd_ov_lost"), PixelTheme.Warn));
        _captain = new Button { Text = L.F(r.CaptainAlways ? "bcmd_ov_captain_on" : "bcmd_ov_captain_off", K("tactical.captain")), FocusMode = FocusModeEnum.None, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(210, 40) };
        _captain.AddThemeFontSizeOverride("font_size", 12);
        _captain.Pressed += () => { _cmd.ToggleCaptain(r.Id); Refresh(true); };
        _card.AddChild(_captain);
    }

    Label Line(string t, Color c)
    {
        var l = new Label { Text = t, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(210, 0) };
        l.AddThemeFontSizeOverride("font_size", 14); l.AddThemeColorOverride("font_color", c);
        return l;
    }

    static string OrderText(FormationOrder o) => o.Kind == OrderKind.None ? L.T("bcmd_order_none") : L.T(BattleCommand.Name(o.Kind));
    static string DraftText(FightInput[] ins) => ins.Length == 0 ? "" : ins[0].Kind == FightInputKind.FormOrder ? L.T(BattleCommand.Name((OrderKind)((ins[0].A >> 8) & 0x1F))) : ins[0].Kind == FightInputKind.FormCaptain ? L.T("bcmd_ov_captain_on").Split(':')[0] : "";

    // ------------------------------------------------------------------ drawing

    static readonly Color Mine = new(0.35f, 0.62f, 0.95f), Foe = new(0.9f, 0.38f, 0.32f);

    Vector2[] Box(FormationMark m, float cx, float cy, float frontPx, float depthPx)
    {
        var c = ToPx(cx, cy); Vector2 f = new(m.FaceX / 1024f, m.FaceY / 1024f), l = new(-f.Y, f.X);
        return [c - f * depthPx / 2 - l * frontPx / 2, c - f * depthPx / 2 + l * frontPx / 2, c + f * depthPx / 2 + l * frontPx / 2, c + f * depthPx / 2 - l * frontPx / 2];
    }

    void Arrow(CanvasItem ci, Vector2 a, Vector2 b, Color c, float w = 2, bool dashed = false)
    {
        if (dashed) ci.DrawDashedLine(a, b, c, w, 6); else ci.DrawLine(a, b, c, w);
        var d = (b - a); if (d.LengthSquared() < 4) return; d = d.Normalized();
        var p = new Vector2(-d.Y, d.X);
        ci.DrawColoredPolygon([b, b - d * 9 + p * 4.5f, b - d * 9 - p * 4.5f], c);
    }

    void DrawMap(CanvasItem c)
    {
        c.DrawSetTransform(-MapRect.Position);
        var r = MapRect; var w = _cmd.W; var f = _font;
        c.DrawRect(r, new Color(0.13f, 0.17f, 0.11f, 0.94f));
        c.DrawRect(r, PixelTheme.Rim, false, 1);
        // a 50 m grid, so the distances read
        float step = 50 * _ppm;
        if (step >= 8)
        {
            var o = ToPx(0, 0);
            for (float x = r.Position.X + ((o.X - r.Position.X) % step + step) % step; x < r.End.X; x += step) c.DrawLine(new Vector2(x, r.Position.Y), new Vector2(x, r.End.Y), new Color(1, 1, 1, 0.05f));
            for (float y = r.Position.Y + ((o.Y - r.Position.Y) % step + step) % step; y < r.End.Y; y += step) c.DrawLine(new Vector2(r.Position.X, y), new Vector2(r.End.X, y), new Color(1, 1, 1, 0.05f));
        }
        // ghosts of the enemy last seen where, then the enemy in sight, then ours
        foreach (var g in _ghosts)
        {
            var mk = MarkOf(g.Row.Index); if (mk == null) continue;
            var pts = Box(mk.Value, g.X, g.Y, Math.Max(10, mk.Value.FrontMm / 1000f * _ppm), Math.Max(8, mk.Value.DepthMm / 1000f * _ppm));
            c.DrawPolyline([.. pts, pts[0]], new Color(Foe, 0.45f), 1);
            c.DrawString(f, ToPx(g.X, g.Y) + new Vector2(-30, 4), L.F("bcmd_ov_ghost", g.AgeTicks / 20), HorizontalAlignment.Center, 60, 10, new Color(Foe, 0.7f));
        }
        foreach (var s in _seen) DrawBlock(c, MarkOf(s.Row.Index), s.Row, false);
        foreach (var m in Marks(true)) DrawBlock(c, m, w.Formations!.Rows[m.Row], true);
        // orders: in force, queued, on the way and drafted
        foreach (var m in Marks(true))
        {
            var row = w.Formations!.Rows[m.Row];
            var from = ToPx(m.CentreX, m.CentreY);
            bool sel = _cmd.IsSelected(row);
            var cur = from;
            if (OrderPoint(row, row.Order) is { } p0) { var t = ToPx(p0.X, p0.Y); Arrow(c, from, t, new Color(0.3f, 0.9f, 0.95f, sel ? 0.95f : 0.55f)); cur = t; }
            for (int i = 0; i < row.QueueCount; i++) if (OrderPoint(row, row.Queue[i]) is { } q) { var t = ToPx(q.X, q.Y); Arrow(c, cur, t, new Color(0.3f, 0.9f, 0.95f, 0.45f), 1, true); cur = t; }
            for (int i = 0; i < row.PendingCount; i++)
                if (OrderPoint(row, row.Pending[i]) is { } q)
                {
                    var t = ToPx(q.X, q.Y); Arrow(c, from, t, new Color(0.65f, 0.85f, 1f, 0.8f), 1, true);
                    c.DrawString(f, t + new Vector2(-30, -6), (FormationBook.ArrivesIn(w, row.Pending[i]) / 20.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s", HorizontalAlignment.Center, 60, 11, new Color(0.75f, 0.9f, 1f));
                }
            var dcur = from;
            foreach (var d in _cmd.Draft.Of(row.Id))
            {
                if (d.Length == 0 || d[0].Kind != FightInputKind.FormOrder) continue;
                var kind = (OrderKind)((d[0].A >> 8) & 0x1F);
                (int X, int Y)? tp = FormationCommands.TakesFormation(kind) ? EnemyPoint(d[0].B - 1) : kind is OrderKind.MoveTo or OrderKind.Retreat ? (d[0].B, d[0].C) : null;
                if (tp is not { } pt) continue;
                var t = ToPx(pt.X, pt.Y); Arrow(c, dcur, t, new Color(0.55f, 1f, 0.55f), 2); dcur = t;
                if (d.Length > 1 && d[1].Kind == FightInputKind.FormFace)
                {
                    var fc = new Vector2((short)(d[1].B & 0xFFFF), (short)(d[1].B >> 16)) / 1024f; var lat = new Vector2(-fc.Y, fc.X) * d[1].C / 1000f * _ppm / 2;
                    c.DrawLine(t - lat, t + lat, new Color(0.55f, 1f, 0.55f), 3); c.DrawLine(t, t + fc * 16, new Color(0.55f, 1f, 0.55f), 2);
                }
            }
        }
        // the drag in progress
        if (_dragging)
        {
            if (_rmb)
            {
                c.DrawLine(_dragFrom, _dragNow, new Color(1f, 0.85f, 0.35f), 3);
                var mid = (_dragFrom + _dragNow) / 2; var d = _dragNow - _dragFrom;
                c.DrawString(f, mid + new Vector2(-40, -8), $"{d.Length() / _ppm:0} m", HorizontalAlignment.Center, 80, 12, PixelTheme.Gold);
            }
            else Arrow(c, _dragFrom, _dragNow, new Color(0.55f, 1f, 0.55f), 2);
        }
        if (_cmd.Mine().Count == 0) c.DrawString(f, r.Position + new Vector2(0, r.Size.Y / 2), L.T("bcmd_ov_nothing"), HorizontalAlignment.Center, r.Size.X, 16, PixelTheme.Dim);
    }

    FormationMark? MarkOf(int rowIndex) { for (int i = 0; i < _snap.FormCount; i++) if (_snap.Forms[i].Row == rowIndex) return _snap.Forms[i]; return null; }

    (int X, int Y)? EnemyPoint(int rowIndex)
    {
        var rows = _cmd.W.Formations?.Rows; if (rows == null || rowIndex < 0 || rowIndex >= rows.Count) return null;
        return (rows[rowIndex].AnchorX, rows[rowIndex].AnchorY);
    }

    (int X, int Y)? OrderPoint(FormationRow row, FormationOrder o)
    {
        switch (o.Kind)
        {
            case OrderKind.MoveTo: return (o.TargetX, o.TargetY);
            case OrderKind.Retreat: return o.TargetX == int.MinValue ? null : (o.TargetX, o.TargetY);
            case OrderKind.Advance or OrderKind.Charge or OrderKind.AttackFormation or OrderKind.FlankLeft or OrderKind.FlankRight:
                if (o.TargetFormation > 0 && EnemyPoint(o.TargetFormation - 1) is { } e) return e;
                return row.TgtRow >= 0 ? EnemyPoint(row.TgtRow) : null;
            default: return null;
        }
    }

    void DrawBlock(CanvasItem ci, FormationMark? mk, FormationRow row, bool mine)
    {
        if (mk is not { } m) return;
        var f = _font; var col = mine ? Mine : Foe;
        bool sel = mine && _cmd.IsSelected(row);
        float fp = Math.Max(12, m.FrontMm / 1000f * _ppm), dp = Math.Max(8, m.DepthMm / 1000f * _ppm);
        var pts = Box(m, m.CentreX, m.CentreY, fp, dp);
        ci.DrawColoredPolygon(pts, new Color(col, sel ? 0.6f : 0.4f));
        ci.DrawPolyline([.. pts, pts[0]], sel ? PixelTheme.Gold : col, sel ? 3 : 1.5f);
        var c = ToPx(m.CentreX, m.CentreY); var fc = new Vector2(m.FaceX, m.FaceY) / 1024f;
        Arrow(ci, c, c + fc * (dp / 2 + 12), new Color(1, 1, 1, 0.85f), 2);
        ci.DrawString(f, c + new Vector2(-fp / 2, -dp / 2 - 5), (mine ? "F" : "E") + row.Id + "  " + m.Active + "/" + m.Total, HorizontalAlignment.Left, Math.Max(80, fp), 12, Colors.White);
        // cohesion and morale under the block
        int coh = Math.Clamp(row.Cohesion / 10, 0, 100); long ms = 0; int n = 0; foreach (int id in row.Members) { var q = _cmd.W.Fighters[id]; if (q.Active) { ms += q.Morale; n++; } }
        int mor = n == 0 ? 0 : (int)Math.Clamp(ms / n, 0, 100);
        var b0 = c + new Vector2(-14, dp / 2 + 5 + Math.Abs(fc.X) * 0);
        ci.DrawRect(new Rect2(b0, new Vector2(28, 3)), new Color(0, 0, 0, 0.6f)); ci.DrawRect(new Rect2(b0, new Vector2(28 * coh / 100f, 3)), new Color(0.75f, 0.75f, 0.8f));
        ci.DrawRect(new Rect2(b0 + new Vector2(0, 4), new Vector2(28, 3)), new Color(0, 0, 0, 0.6f)); ci.DrawRect(new Rect2(b0 + new Vector2(0, 4), new Vector2(28 * mor / 100f, 3)), new Color(0.4f, 0.8f, 0.35f));
    }

    // ------------------------------------------------------------------ input

    (FormationRow Row, bool Mine)? BlockAt(Vector2 p)
    {
        var rows = _cmd.W.Formations?.Rows; if (rows == null) return null;
        foreach (var (mine, set) in new[] { (true, Marks(true).ToList()), (false, _seen.Select(s => MarkOf(s.Row.Index)).Where(x => x != null).Select(x => x!.Value).ToList()) })
            foreach (var m in set)
            {
                var c = ToPx(m.CentreX, m.CentreY); var d = p - c; Vector2 fc = new(m.FaceX / 1024f, m.FaceY / 1024f), l = new(-fc.Y, fc.X);
                float fp = Math.Max(12, m.FrontMm / 1000f * _ppm), dp = Math.Max(8, m.DepthMm / 1000f * _ppm);
                if (Math.Abs(d.Dot(l)) <= fp / 2 + 3 && Math.Abs(d.Dot(fc)) <= dp / 2 + 3) return (rows[m.Row], mine);
            }
        return null;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (WheelOpen) return;
        if (e is InputEventMouseMotion mm)
        {
            _dragNow = mm.Position;
            if (_dragging) QueueRedraw();
            return;
        }
        if (e is not InputEventMouseButton mb) return;
        bool queue = mb.ShiftPressed;
        if (mb.ButtonIndex == MouseButton.Left && mb.Pressed && MapRect.HasPoint(mb.Position))
        {
            var hit = BlockAt(mb.Position);
            if (hit is { Mine: true } h) { _cmd.SelectRow(h.Row.Id, false); _dragRow = h.Row.Id; _dragging = true; _rmb = false; _dragFrom = mb.Position; _dragNow = mb.Position; Refresh(true); }
            else if (hit is { Mine: false } en && _cmd.Selected.Count > 0)
            {
                foreach (var t in _cmd.Targets()) _cmd.Order(t.Id, OrderKind.AttackFormation, en.Row.Index, queue);
                Refresh(true);
            }
            AcceptEvent();
        }
        else if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed && _dragging && !_rmb)
        {
            _dragging = false;
            if ((mb.Position - _dragFrom).Length() > 6 && MapRect.HasPoint(mb.Position))
            { var mm2 = ToMm(mb.Position); _cmd.MoveTo(_dragRow, (int)mm2.X, (int)mm2.Y, queue); Refresh(true); }
            QueueRedraw(); AcceptEvent();
        }
        else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed && MapRect.HasPoint(mb.Position) && _cmd.Targets().Count > 0)
        { _dragging = true; _rmb = true; _dragFrom = mb.Position; _dragNow = mb.Position; AcceptEvent(); }
        else if (mb.ButtonIndex == MouseButton.Right && !mb.Pressed && _dragging && _rmb)
        {
            _dragging = false; _rmb = false;
            if ((mb.Position - _dragFrom).Length() > 6)
            {
                var a = ToMm(_dragFrom); var b = ToMm(mb.Position);
                _cmd.Draw(_cmd.Targets()[0].Id, ((int)a.X, (int)a.Y), ((int)b.X, (int)b.Y), queue);
                Refresh(true);
            }
            QueueRedraw(); AcceptEvent();
        }
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || WheelOpen) return;
        if (e is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true })
        {
            if (Actions.Pressed(e, "tactical.resume")) { Resume(); GetViewport().SetInputAsHandled(); return; }
            if (Actions.Pressed(e, "tactical.close")) { Close(); GetViewport().SetInputAsHandled(); return; }
            for (int k = 1; k <= 4; k++)
            {
                if (Actions.Pressed(e, "tactical.formation_add_" + k)) { _cmd.Select(k, true, Time.GetTicksMsec() / 1000.0); Refresh(true); GetViewport().SetInputAsHandled(); return; }
                if (Actions.Pressed(e, "tactical.formation_" + k)) { Pick(k); GetViewport().SetInputAsHandled(); return; }
            }
            if (Actions.Pressed(e, "tactical.next_formation")) { _cmd.Next(); Refresh(true); GetViewport().SetInputAsHandled(); return; }
            if (Actions.Pressed(e, "tactical.order_wheel")) { OpenWheel(); GetViewport().SetInputAsHandled(); return; }
            if (Actions.Pressed(e, "tactical.captain")) { foreach (var r in _cmd.Targets().Take(1)) _cmd.ToggleCaptain(r.Id); Refresh(true); GetViewport().SetInputAsHandled(); return; }
            if (Actions.Pressed(e, "tactical.undo")) { _cmd.Draft.Undo(); Refresh(true); GetViewport().SetInputAsHandled(); return; }
            if (Actions.Pressed(e, "tactical.clear_queue")) { foreach (var r in _cmd.Targets()) _cmd.Draft.Clear(r.Id); Refresh(true); GetViewport().SetInputAsHandled(); return; }
            if (Actions.Pressed(e, "tactical.zoom_in") && e is InputEventKey) { _ppm = Math.Min(14f, _ppm * 1.25f); GetViewport().SetInputAsHandled(); return; }
            if (Actions.Pressed(e, "tactical.zoom_out") && e is InputEventKey) { _ppm = Math.Max(0.15f, _ppm / 1.25f); GetViewport().SetInputAsHandled(); return; }
            if (e is InputEventMouseButton wb && MapRect.HasPoint(wb.Position))
            {
                if (Actions.Pressed(e, "tactical.zoom_in")) { _ppm = Math.Min(14f, _ppm * 1.15f); GetViewport().SetInputAsHandled(); return; }
                if (Actions.Pressed(e, "tactical.zoom_out")) { _ppm = Math.Max(0.15f, _ppm / 1.15f); GetViewport().SetInputAsHandled(); return; }
            }
            float pan = 60 / _ppm;
            if (Actions.Pressed(e, "tactical.pan_up")) { _centreM.Y -= pan; GetViewport().SetInputAsHandled(); return; }
            if (Actions.Pressed(e, "tactical.pan_down")) { _centreM.Y += pan; GetViewport().SetInputAsHandled(); return; }
            if (Actions.Pressed(e, "tactical.pan_left")) { _centreM.X -= pan; GetViewport().SetInputAsHandled(); return; }
            if (Actions.Pressed(e, "tactical.pan_right")) { _centreM.X += pan; GetViewport().SetInputAsHandled(); return; }
            // PgUp and PgDn (levels of a wall-walk or a deck) have nothing to change in a fight on one level; the bubble's walls (BB-C) are theirs
        }
    }

    /// <summary>F1 to F4 in the overlay: select the formation and centre the map on it when pressed twice within 0.4 s.</summary>
    void Pick(int k)
    {
        if (_cmd.Select(k, false, Time.GetTicksMsec() / 1000.0) && _cmd.Targets().Count > 0 && _cmd.CentreOf(_cmd.Targets()[0]) is { } c) { _centreM = new Vector2(c.X, c.Y) / 1000f; }
        Refresh(true);
    }

    public void SelectKey(int k, bool add) { if (add) { _cmd.Select(k, true, Time.GetTicksMsec() / 1000.0); Refresh(true); } else Pick(k); }

    public void OpenWheel()
    {
        if (WheelOpen) return;
        _wheel = new OrderWheel(_cmd, "tactical.order_wheel", () => { Refresh(true); QueueRedraw(); });
        AddChild(_wheel);
    }

    /// <summary>Esc: the overlay closes and the game stays paused with the placements kept; the view's key (0) opens it again, Space resumes.</summary>
    public void Close() { SetProcessInput(false); Visible = false; _view.OverlayClosed(this); QueueFree(); }

    /// <summary>Space or 0: what was placed goes out now and the game runs on.</summary>
    public void Resume() { SetProcessInput(false); Visible = false; _view.ResumeBattle(); QueueFree(); }

    public IReadOnlyList<(FormationRow Row, int X, int Y)> Seen => _seen;
    public IReadOnlyList<(FormationRow Row, int X, int Y, int AgeTicks)> Ghosts => _ghosts;

    // ------------------------------------------------------------------ the list

    sealed partial class ListBox(TacticalOverlay o) : Control
    {
        const float RowH = 40;
        public void Refresh() { CustomMinimumSize = new Vector2(168, Math.Max(40, o._cmd.Mine().Count * RowH)); QueueRedraw(); }

        public override void _Draw()
        {
            var cmd = o._cmd; var f = o._font; var rows = cmd.Mine();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i]; float y = i * RowH; bool sel = cmd.IsSelected(r);
                DrawRect(new Rect2(0, y, Size.X, RowH - 3), sel ? new Color(0.3f, 0.24f, 0.1f, 0.9f) : new Color(0.14f, 0.12f, 0.1f, 0.9f));
                if (sel) DrawRect(new Rect2(0, y, Size.X, RowH - 3), PixelTheme.Gold, false, 2);
                string shape = L.T("bcmd_shape_" + FormationRules.Snake(r.Shape.ToString()));
                DrawString(f, new Vector2(5, y + 15), (r.Id <= 4 ? $"[F{r.Id}] " : $"{r.Id} ") + shape, HorizontalAlignment.Left, Size.X - 50, 13, sel ? PixelTheme.Gold : PixelTheme.Body);
                DrawString(f, new Vector2(Size.X - 46, y + 15), L.F("bcmd_card_men", cmd.Standing(r), r.StartCount), HorizontalAlignment.Right, 42, 12, PixelTheme.Body);
                string ord = r.Order.Kind == OrderKind.None ? L.T("bcmd_idle") : L.T(BattleCommand.Name(r.Order.Kind));
                DrawString(f, new Vector2(5, y + 31), ord, HorizontalAlignment.Left, Size.X - 10, 11, PixelTheme.Dim);
            }
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
            var rows = o._cmd.Mine(); int i = (int)(mb.Position.Y / RowH);
            if (i >= 0 && i < rows.Count) { o._cmd.SelectRow(rows[i].Id, mb.ShiftPressed); o.Refresh(true); AcceptEvent(); }
        }
    }

    sealed partial class MapView(TacticalOverlay o) : Control
    {
        public override void _Draw() => o.DrawMap(this);
    }
}
