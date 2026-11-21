using Ravensguard.Game.UI.NewGame;
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World.Crawl;

/// <summary>
/// The hero underground (exploration-and-sites 7, P4-H): a thin view over a <see cref="SiteCrawl"/>. Only what the carried lights reach is seen; what was seen once stays on the
/// auto-map, drawn dim. The HUD shows the light left, the level, the air warning, being lost, the way out and the load, and the surgeon when a wound's window is open; warnings
/// (a trap found, the roof creaking, the candle guttering) come up as lines. M opens the auto-map (PgUp/PgDn change level), Q the verbs. Every input goes through
/// <see cref="Send"/> (the street view records it). No game state lives here: zoom, the level looked at and the open panel only.
/// </summary>
public partial class CrawlView : Node2D
{
    public SiteCrawl C { get; private set; }
    public Action<CrawlInput> Send { get; }
    public string SiteName { get; set; } = "";
    /// <summary>The Q menu (the street view pushes its QMenu); null: no menu.</summary>
    public Action? OpenMenu { get; set; }
    /// <summary>True when the view steps the crawl itself (the demo, screenshots).</summary>
    public bool OwnsStepping { get; set; }
    public float Cell { get; set; } = 16f;
    public bool MapOpen { get; private set; }
    public bool ModalOpen => MapOpen;
    /// <summary>A shot or the demo hides the key line.</summary>
    public bool ShowHints { get; set; } = true;

    Font _font = null!;
    int _viewLevel = -1, _mapLevel;
    bool _sneak;
    int _lastDir = -2, _lastFlags = -1;
    double _t, _acc;
    readonly List<(string Text, Color Col, double Age)> _lines = [];

    public CrawlView(SiteCrawl crawl, Action<CrawlInput> send) { C = crawl; Send = send; }

    public void Replace(SiteCrawl crawl) { C = crawl; _viewLevel = -1; _lines.Clear(); }

    public override void _Ready()
    {
        _font = GD.Load<Font>("res://assets/fonts/PixelifySans.ttf") ?? ThemeDB.FallbackFont;
        ZIndex = 10;
    }

    // ------------------------------------------------------------------ the frame

    public override void _Process(double delta)
    {
        _t += delta;
        if (OwnsStepping)
        {
            _acc += delta;
            int n = 0;
            while (_acc >= 0.05 && n < 8) { _acc -= 0.05; C.Step(); n++; }
            if (_acc > 0.2) _acc = 0;
        }
        if (!ModalOpen && C.Fight == null) PollMove();
        foreach (var e in C.TakeEvents()) Say(e);
        for (int i = _lines.Count - 1; i >= 0; i--) { var l = _lines[i]; _lines[i] = l with { Age = l.Age + delta }; if (_lines[i].Age > 9) _lines.RemoveAt(i); }
        if (_viewLevel < 0 || C.Hero.Level != _viewLevel && !MapOpen) _viewLevel = C.Hero.Level;
        QueueRedraw();
    }

    void PollMove()
    {
        int dx = (Actions.HeldLoose("world.move_right") ? 1 : 0) - (Actions.HeldLoose("world.move_left") ? 1 : 0);
        int dy = (Actions.HeldLoose("world.move_down") ? 1 : 0) - (Actions.HeldLoose("world.move_up") ? 1 : 0);
        int dir = dx == 0 && dy == 0 ? -1 : DirOf(dx, dy);
        int flags = (Actions.HeldLoose("world.sprint") ? 1 : 0) | (_sneak ? 4 : 0);
        if (dir == _lastDir && flags == _lastFlags) return;
        _lastDir = dir; _lastFlags = flags;
        Send(new CrawlInput(CrawlInputKind.Move, dir, flags));
    }

    static int DirOf(int dx, int dy) => (dx, dy) switch { (1, 0) => 0, (1, 1) => 1, (0, 1) => 2, (-1, 1) => 3, (-1, 0) => 4, (-1, -1) => 5, (0, -1) => 6, _ => 7 };

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || e is not (InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true })) return;
        if (C.Fight != null) return;                                   // the combat stance has the keys
        if (MapOpen)
        {
            // a modal: only its own keys, everything else waits (the input guard)
            if (Actions.Pressed(e, "world.map") || Actions.Pressed(e, "world.back")) MapOpen = false;
            else if (Actions.Pressed(e, "world.level_up")) _mapLevel = Math.Max(0, _mapLevel - 1);
            else if (Actions.Pressed(e, "world.level_down")) _mapLevel = Math.Min(C.Map.Levels - 1, _mapLevel + 1);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (Actions.Pressed(e, "world.torch")) Send(new CrawlInput(CrawlInputKind.Torch));
        else if (Actions.Pressed(e, "world.act") && e is InputEventKey) Send(new CrawlInput(CrawlInputKind.Act));
        else if (Actions.Pressed(e, "world.map")) { MapOpen = true; _mapLevel = C.Hero.Level; }
        else if (Actions.Pressed(e, "world.sneak")) { _sneak = !_sneak; _lastFlags = -1; Say(L.T(_sneak ? "ex_ui_sneak_on" : "ex_ui_sneak_off"), PixelTheme.Dim); }
        else if (Actions.Pressed(e, "world.level_up")) _viewLevel = Math.Max(0, _viewLevel - 1);
        else if (Actions.Pressed(e, "world.level_down")) _viewLevel = Math.Min(C.Map.Levels - 1, _viewLevel + 1);
        else if (Actions.Pressed(e, "world.context_menu")) OpenMenu?.Invoke();
        else if (Actions.Pressed(e, "world.zoom_in")) Cell = Math.Min(32, Cell + 4);
        else if (Actions.Pressed(e, "world.zoom_out")) Cell = Math.Max(8, Cell - 4);
        else if (Actions.Pressed(e, "world.camera_home")) _viewLevel = C.Hero.Level;
        else return;
        GetViewport().SetInputAsHandled();
    }

    public void OpenMap(int level) { MapOpen = true; _mapLevel = Math.Clamp(level, 0, C.Map.Levels - 1); }
    public void CloseMap() => MapOpen = false;

    // ------------------------------------------------------------------ the lines

    public void Say(string text, Color col) { _lines.Add((text, col, 0)); if (_lines.Count > 6) _lines.RemoveAt(0); }

    void Say(CrawlEvent e)
    {
        var warn = PixelTheme.Warn; var body = PixelTheme.Body; var dim = PixelTheme.Dim; var gold = PixelTheme.Gold;
        string trap = e.Text.Length > 0 ? L.T("ex_trap_" + e.Text) : "";
        string? msg = e.Kind switch
        {
            CrawlEventKind.Entered => L.T("ex_ev_entered"),
            CrawlEventKind.LevelChanged => L.F("ex_ev_level", -(e.A + 1)),
            CrawlEventKind.DoorOpened => L.T("ex_ev_door"),
            CrawlEventKind.Looted => e.B > 0 ? L.F("ex_ev_looted", UiKit.Money(e.B)) : L.T("ex_ev_looted_empty"),
            CrawlEventKind.TabooRobbed => L.T("ex_ev_taboo"),
            CrawlEventKind.AirWarning => L.T(e.A >= 600 ? "ex_ev_air_gutter" : "ex_ev_air_bad"),
            CrawlEventKind.AirClear => L.T("ex_ev_air_clear"),
            CrawlEventKind.FlameDied => L.T("ex_ev_flame_died"),
            CrawlEventKind.TorchGuttering => L.T("ex_ev_torch_low"),
            CrawlEventKind.TorchOut => L.T("ex_ev_torch_out"),
            CrawlEventKind.TorchLit => L.T("ex_ev_torch_lit"),
            CrawlEventKind.NoLight => L.T("ex_ev_no_light"),
            CrawlEventKind.TrapDetected => L.F("ex_ev_trap_found", trap),
            CrawlEventKind.TrapTriggered => L.F("ex_ev_trap_sprung", trap),
            CrawlEventKind.TrapDisarmed => L.T("ex_ev_trap_disarmed"),
            CrawlEventKind.TrapDisarmFailed => L.T("ex_ev_trap_disarm_failed"),
            CrawlEventKind.TrapCannotDisarm => L.T("ex_ev_trap_cannot"),
            CrawlEventKind.Fell => L.T("ex_ev_fell"),
            CrawlEventKind.Held => L.T("ex_ev_held"),
            CrawlEventKind.Freed => L.T("ex_ev_freed"),
            CrawlEventKind.CaveInCreak => L.T("ex_ev_creak"),
            CrawlEventKind.CaveIn => L.T("ex_ev_cave_in"),
            CrawlEventKind.Dug => L.T("ex_ev_dug"),
            CrawlEventKind.WaterRising => L.F("ex_ev_water_rising", e.B),
            CrawlEventKind.Swimming => L.T("ex_ev_swimming"),
            CrawlEventKind.Sinking => L.T("ex_ev_sinking"),
            CrawlEventKind.Unconscious => L.T("ex_ev_unconscious"),
            CrawlEventKind.Woke => L.T("ex_ev_woke"),
            CrawlEventKind.Died => L.T("ex_ev_died"),
            CrawlEventKind.MonsterWoke => L.F(e.B == 1 ? "ex_ev_beast_woke" : "ex_ev_monster_woke", e.Text),
            CrawlEventKind.Noticed => L.F("ex_ev_noticed", e.Text.Length > 0 ? e.Text : L.T("ex_someone")),
            CrawlEventKind.FightStarted => L.F("ex_ev_fight", e.Text.Length > 0 ? e.Text : L.T("ex_someone")),
            CrawlEventKind.FightEnded => L.T("ex_ev_fight_over"),
            CrawlEventKind.MonsterSlain => L.F("ex_ev_slain", e.Text),
            CrawlEventKind.LadderPulled => L.T(e.B == 1 ? "ex_ev_ladder_up" : "ex_ev_ladder_down"),
            CrawlEventKind.LadderUp => L.T("ex_ev_ladder_missing"),
            CrawlEventKind.ClimbedRope => L.T("ex_ev_rope"),
            CrawlEventKind.RopeFell => L.T("ex_ev_rope_fell"),
            CrawlEventKind.RetraceStarted => L.T("ex_ev_retrace"),
            CrawlEventKind.RetraceDone => L.T("ex_ev_retrace_done"),
            CrawlEventKind.RetraceStopped => L.T("ex_ev_retrace_stopped"),
            CrawlEventKind.Marked => L.T("ex_ev_marked"),
            CrawlEventKind.RestStarted => L.T("ex_ev_rest"),
            CrawlEventKind.RestStopped => L.T("ex_ev_rest_stopped"),
            CrawlEventKind.CannotRest => L.T("ex_ev_cannot_rest"),
            CrawlEventKind.Lost => L.T("ex_ev_lost"),
            CrawlEventKind.Found => L.T("ex_ev_found"),
            CrawlEventKind.RatsAtFood => L.T("ex_ev_rats"),
            CrawlEventKind.Busy => L.F(e.A == 1 ? "ex_ev_disarming" : "ex_ev_digging", e.B),
            CrawlEventKind.Burning => L.T("ex_ev_burning"),
            CrawlEventKind.NothingHere => L.T("ex_ev_nothing"),
            CrawlEventKind.Blocked => L.T("ex_ev_blocked"),
            CrawlEventKind.Treated => L.T("ex_ev_treated"),
            _ => null,
        };
        if (msg == null) return;
        var col = e.Kind switch
        {
            CrawlEventKind.AirWarning or CrawlEventKind.TrapDetected or CrawlEventKind.TrapTriggered or CrawlEventKind.CaveInCreak or CrawlEventKind.CaveIn or CrawlEventKind.Sinking
                or CrawlEventKind.Unconscious or CrawlEventKind.Died or CrawlEventKind.MonsterWoke or CrawlEventKind.FightStarted or CrawlEventKind.TorchOut or CrawlEventKind.FlameDied or CrawlEventKind.TabooRobbed => warn,
            CrawlEventKind.Looted or CrawlEventKind.MonsterSlain or CrawlEventKind.TrapDisarmed => gold,
            CrawlEventKind.DoorOpened or CrawlEventKind.Marked or CrawlEventKind.NothingHere => dim,
            _ => body,
        };
        Say(msg, col);
    }

    // ------------------------------------------------------------------ drawing

    static readonly Color Rock = new("2a2420"), Wall = new("5a5550"), Floor = new("4a4038"), Door = new("6b4a2a"), Water = new("2b4a6a"), Rubble = new("5e5246"),
        Hole = new("0a0806"), Ore = new("6a5a3a"), Stair = new("8a7a60"), Box = new("7a5a30"), Pillar = new("6e6862"), Trap = new("d94a3a");

    Vector2 Origin(int hx, int hy)
    {
        var sz = GetViewportRect().Size;
        return new Vector2(sz.X / 2 - (hx / 1000f) * Cell, sz.Y / 2 - (hy / 1000f) * Cell);
    }

    public override void _Draw()
    {
        var sz = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, sz), new Color(0.02f, 0.015f, 0.01f));
        var m = C.Map; var h = C.Hero; var f = C.Frame;
        int level = Math.Clamp(_viewLevel < 0 ? h.Level : _viewLevel, 0, m.Levels - 1);
        int z = SiteMap.Z(level);
        var o = Origin(h.X, h.Y);
        int x0 = Math.Max(0, (int)((-o.X) / Cell) - 1), y0 = Math.Max(0, (int)((-o.Y) / Cell) - 1);
        int x1 = Math.Min(m.W - 1, (int)((sz.X - o.X) / Cell) + 1), y1 = Math.Min(m.H - 1, (int)((sz.Y - o.Y) / Cell) + 1);
        float flick = 0.94f + 0.06f * Mathf.Sin((float)_t * 11f) * Mathf.Sin((float)_t * 7.3f + 1f);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int cell = m.Index(level, x, y);
                int light = f.Total(x, y, z);
                bool known = C.Known.Known(cell);
                if (!known && light < 30) continue;
                var t = m.Tiles[cell];
                var baseC = TileColor(t, m.Walls, x, y);
                float b = 0.22f + 0.78f * Math.Min(1f, light / 170f);           // the eye adapts: a torch's edge still reads
                Color c;
                if (light >= 20) c = new Color(baseC.R * b * flick * 1.15f, baseC.G * b * flick * 0.95f, baseC.B * b * flick * 0.72f);
                else c = new Color(baseC.R * 0.25f, baseC.G * 0.27f, baseC.B * 0.34f);
                var r = new Rect2(o.X + x * Cell, o.Y + y * Cell, Cell, Cell);
                DrawRect(r, c);
                DrawDetail(t, r, c, m, cell);
                if (m.TrapAt(cell) is { Detected: true } tr) DrawTrap(r, tr.Armed);
            }
        // the people and beasts in sight; the party
        foreach (var n in m.Inhabitants)
        {
            if (n.Level != level || n.State is InhabitantState.Away or InhabitantState.Fled) continue;
            int cell = C.CellOf(n); int nx = n.X / 1000, ny = n.Y / 1000;
            int light = f.Total(nx, ny, z);
            long dx = n.X - h.X, dy = n.Y - h.Y;
            bool near = dx * dx + dy * dy <= 1600L * 1600 && h.Level == level;
            bool seen = near || h.Level == level && light >= 30 && dx * dx + dy * dy <= (long)SiteLight.VisionRadiusMm(light) * SiteLight.VisionRadiusMm(light) && AutoMap.Clear(m, level, h.X / 1000, h.Y / 1000, nx, ny);
            if (!seen) continue;
            var p = new Vector2(o.X + n.X / 1000f * Cell, o.Y + n.Y / 1000f * Cell);
            var col = n.State == InhabitantState.Dead ? new Color(0.35f, 0.3f, 0.28f) : n.Monster == "draugr" ? new Color("9cc0a0") : n.Monster.Length > 0 ? new Color("4a3a40") : n.Hostile ? new Color("c87038") : new Color("a0a070");
            DrawCircle(p, Cell * 0.42f, new Color(0, 0, 0, 0.6f));
            DrawCircle(p, Cell * 0.34f, col);
            if (n.State is InhabitantState.Dormant or InhabitantState.Asleep) DrawString(_font, p + new Vector2(Cell * 0.3f, -Cell * 0.3f), "z", HorizontalAlignment.Left, -1, 12, PixelTheme.Dim);
            string label = n.State == InhabitantState.Dead ? "" : n.Name.Length > 0 ? n.Name : L.T("ex_folk_" + n.Role);
            if (label.Length > 0 && Cell >= 12) DrawString(_font, p + new Vector2(-60, -Cell * 0.55f), label, HorizontalAlignment.Center, 120, 12, col.Lightened(0.3f));
        }
        for (int i = C.Members.Count - 1; i >= 0; i--)
        {
            var mb = C.Members[i];
            if (mb.Level != level) continue;
            var p = new Vector2(o.X + mb.X / 1000f * Cell, o.Y + mb.Y / 1000f * Cell);
            DrawCircle(p, Cell * 0.42f, new Color(0, 0, 0, 0.7f));
            DrawCircle(p, Cell * 0.34f, mb.Dead ? new Color(0.3f, 0.2f, 0.2f) : mb.Hero ? PixelTheme.Gold : new Color("7aa0c8"));
            var face = new Vector2(mb.FX, mb.FY).Normalized();
            DrawLine(p, p + face * Cell * 0.5f, new Color(0.1f, 0.08f, 0.05f), 2);
            if (mb.Light.Lit) DrawCircle(p + new Vector2(Cell * 0.3f, -Cell * 0.25f), Cell * 0.13f, new Color(1f, 0.75f, 0.3f) * flick);
        }
        DrawHud(sz);
        if (MapOpen) DrawMap(sz);
    }

    static Color TileColor(SiteTile t, string walls, int x, int y)
    {
        int n = (x * 73856093 ^ y * 19349663) & 15;
        float j = 0.92f + n * 0.01f;
        var c = t switch
        {
            SiteTile.Rock => Rock, SiteTile.Wall => walls == "rock" ? Rock.Lightened(0.15f) : Wall, SiteTile.Floor => Floor, SiteTile.Door => Door, SiteTile.DoorOpen => Floor,
            SiteTile.StairsUp or SiteTile.StairsDown => Stair, SiteTile.ShaftDown or SiteTile.ShaftUp => Floor, SiteTile.Pillar => Pillar, SiteTile.Container => Floor,
            SiteTile.Water => Water, SiteTile.Rubble => Rubble, SiteTile.Hole => Hole, SiteTile.Ore => Ore, _ => Rock,
        };
        return new Color(c.R * j, c.G * j, c.B * j);
    }

    void DrawDetail(SiteTile t, Rect2 r, Color lit, SiteMap m, int cell)
    {
        float k = Cell;
        var dark = new Color(lit.R * 0.55f, lit.G * 0.55f, lit.B * 0.55f);
        var light = new Color(Math.Min(1, lit.R * 1.35f), Math.Min(1, lit.G * 1.35f), Math.Min(1, lit.B * 1.35f));
        switch (t)
        {
            case SiteTile.Wall when m.Walls != "rock":
                DrawLine(r.Position + new Vector2(0, k * 0.5f), r.Position + new Vector2(k, k * 0.5f), dark, 1);
                DrawLine(r.Position + new Vector2(k * 0.5f, 0), r.Position + new Vector2(k * 0.5f, k * 0.5f), dark, 1);
                break;
            case SiteTile.Floor when Cell >= 12:
                DrawRect(new Rect2(r.Position, new Vector2(k, 1)), dark); DrawRect(new Rect2(r.Position, new Vector2(1, k)), dark);
                break;
            case SiteTile.Door:
                DrawRect(new Rect2(r.Position + new Vector2(k * 0.15f, k * 0.15f), new Vector2(k * 0.7f, k * 0.7f)), light);
                DrawRect(new Rect2(r.Position + new Vector2(k * 0.45f, k * 0.15f), new Vector2(1, k * 0.7f)), dark);
                break;
            case SiteTile.DoorOpen:
                DrawRect(new Rect2(r.Position, new Vector2(k * 0.18f, k)), new Color(Door.R * lit.R * 2, Door.G * lit.G * 2, Door.B * lit.B * 2));
                break;
            case SiteTile.StairsUp or SiteTile.StairsDown:
                for (int i = 1; i < 4; i++) DrawLine(r.Position + new Vector2(0, k * i / 4f), r.Position + new Vector2(k, k * i / 4f), dark, 1);
                DrawString(_font, r.Position + new Vector2(k * 0.2f, k * 0.85f), t == SiteTile.StairsUp ? "<" : ">", HorizontalAlignment.Left, -1, (int)Math.Max(9, k * 0.8f), light);
                break;
            case SiteTile.ShaftDown or SiteTile.ShaftUp:
                DrawLine(r.Position + new Vector2(k * 0.3f, 0), r.Position + new Vector2(k * 0.3f, k), light, 1);
                DrawLine(r.Position + new Vector2(k * 0.7f, 0), r.Position + new Vector2(k * 0.7f, k), light, 1);
                for (int i = 1; i < 4; i++) DrawLine(r.Position + new Vector2(k * 0.3f, k * i / 4f), r.Position + new Vector2(k * 0.7f, k * i / 4f), light, 1);
                break;
            case SiteTile.Pillar:
                DrawCircle(r.GetCenter(), k * 0.38f, light);
                break;
            case SiteTile.Container:
            {
                var box = m.ContainerAt(cell);
                bool open = box?.Opened == true;
                var bc = new Color(Box.R * lit.R * 2.2f, Box.G * lit.G * 2.2f, Box.B * lit.B * 2.2f);
                if (box?.Kind is ContainerKind.Niche) { DrawRect(new Rect2(r.Position + new Vector2(k * 0.1f, k * 0.25f), new Vector2(k * 0.8f, k * 0.5f)), dark); DrawRect(new Rect2(r.Position + new Vector2(k * 0.2f, k * 0.35f), new Vector2(k * 0.6f, k * 0.3f)), new Color(0.75f * lit.R * 2, 0.72f * lit.G * 2, 0.62f * lit.B * 2)); }
                else if (box?.Kind is ContainerKind.Bier) DrawRect(new Rect2(r.Position + new Vector2(k * 0.05f, k * 0.2f), new Vector2(k * 0.9f, k * 0.6f)), light);
                else { DrawRect(new Rect2(r.Position + new Vector2(k * 0.15f, k * 0.25f), new Vector2(k * 0.7f, k * 0.55f)), bc); DrawRect(new Rect2(r.Position + new Vector2(k * 0.15f, k * 0.25f), new Vector2(k * 0.7f, k * 0.12f)), open ? dark : light); }
                break;
            }
            case SiteTile.Water:
                DrawLine(r.Position + new Vector2(k * 0.15f, k * 0.4f), r.Position + new Vector2(k * 0.5f, k * 0.35f), light, 1);
                DrawLine(r.Position + new Vector2(k * 0.45f, k * 0.7f), r.Position + new Vector2(k * 0.85f, k * 0.65f), light, 1);
                break;
            case SiteTile.Rubble:
                DrawCircle(r.Position + new Vector2(k * 0.3f, k * 0.35f), k * 0.16f, light); DrawCircle(r.Position + new Vector2(k * 0.68f, k * 0.6f), k * 0.2f, dark);
                break;
            case SiteTile.Ore:
                DrawCircle(r.Position + new Vector2(k * 0.4f, k * 0.45f), k * 0.12f, new Color(0.9f * lit.R * 2, 0.7f * lit.G * 2, 0.3f * lit.B * 2));
                break;
        }
    }

    void DrawTrap(Rect2 r, bool armed)
    {
        var c = armed ? Trap : PixelTheme.Dim;
        var a = r.Position + new Vector2(r.Size.X * 0.5f, r.Size.Y * 0.15f);
        DrawPolyline([a, r.Position + new Vector2(r.Size.X * 0.15f, r.Size.Y * 0.85f), r.Position + new Vector2(r.Size.X * 0.85f, r.Size.Y * 0.85f), a], c, 2);
    }

    // ------------------------------------------------------------------ the HUD

    void Panel(Rect2 r) { DrawRect(r, new Color(PixelTheme.Fill, 0.88f)); DrawRect(r, PixelTheme.Rim, false, 1); }

    void Text(Vector2 p, string s, Color c, int size = 14) { DrawString(_font, p + new Vector2(1, 1), s, HorizontalAlignment.Left, -1, size, new Color(0, 0, 0, 0.8f)); DrawString(_font, p, s, HorizontalAlignment.Left, -1, size, c); }

    void DrawHud(Vector2 sz)
    {
        var s = C.Snapshot; var h = C.Hero; var K = C.C.K;
        var lines = new List<(string, Color)>();
        lines.Add((SiteName, PixelTheme.Gold));
        lines.Add((L.F("ex_hud_level", -(s.Level + 1)), PixelTheme.Body));
        string lightName = L.T(s.Light switch { LightKind.Candle => "ex_light_candle", LightKind.Lantern => "ex_light_lantern", _ => "ex_light_torch" });
        lines.Add((s.Lit ? L.F("ex_hud_light", lightName, Math.Max(1, (s.LightLeftS + 59) / 60)) : L.T("ex_hud_dark"), s.Lit ? PixelTheme.Body : PixelTheme.Warn));
        lines.Add((L.F("ex_hud_spares", s.Torches), PixelTheme.Dim));
        if (s.AirWarning) lines.Add((L.T(s.Air >= 600 ? "ex_hud_air_gutter" : s.Air >= 400 ? "ex_hud_air_bad" : "ex_hud_air_foul"), PixelTheme.Warn));
        if (s.WaterCm > K.SwimCm) lines.Add((L.T("ex_hud_swimming"), PixelTheme.Warn));
        else if (s.WaterCm >= K.WadeSlowCm) lines.Add((L.F("ex_hud_wading", s.WaterCm), PixelTheme.Body));
        if (s.Lost) lines.Add((L.T("ex_hud_lost"), PixelTheme.Warn));
        if (s.ExitM >= 0) lines.Add((L.F("ex_hud_exit", s.ExitM, Math.Max(1, (s.ExitS + 59) / 60)), PixelTheme.Body));
        else lines.Add((L.T("ex_hud_no_exit"), PixelTheme.Warn));
        lines.Add((L.F("ex_hud_load", s.CarriedKg, s.CapacityKg), s.CarriedKg > s.CapacityKg ? PixelTheme.Warn : PixelTheme.Dim));
        if (s.Busy.Length > 0) lines.Add((L.T("ex_hud_busy_" + s.Busy), PixelTheme.Body));
        if (s.Retracing) lines.Add((L.T("ex_hud_retracing"), PixelTheme.Body));
        if (s.Resting) lines.Add((L.T("ex_hud_resting"), PixelTheme.Body));
        if (SurgeonLine() is { } sl) lines.Add((sl, PixelTheme.Warn));
        float w = 300, lh = 17;
        Panel(new Rect2(8, 8, w, 10 + lines.Count * lh));
        for (int i = 0; i < lines.Count; i++) Text(new Vector2(16, 24 + i * lh), lines[i].Item1, lines[i].Item2);
        // the light left as a bar
        if (s.Lit)
        {
            float full = s.Light == LightKind.Candle ? K.CandleBurnS : s.Light == LightKind.Lantern ? K.LanternBurnS : K.TorchBurnS;
            float fr = Math.Clamp(s.LightLeftS / full, 0, 1);
            DrawRect(new Rect2(16 + 200, 24 + 2 * lh - 10, 80, 8), new Color(0.15f, 0.1f, 0.08f));
            DrawRect(new Rect2(16 + 200, 24 + 2 * lh - 10, 80 * fr, 8), fr < 0.1f ? PixelTheme.Warn : new Color(1f, 0.7f, 0.3f));
        }
        if (s.AirWarning)
        {
            // the flame icon of the warning
            var fp = new Vector2(w - 8, 24 + 4 * lh - 6);
            DrawCircle(fp, 5, new Color(1f, 0.55f, 0.2f)); DrawCircle(fp + new Vector2(0, -4), 3, new Color(1f, 0.85f, 0.4f));
        }
        // the lines of what happened
        float by = sz.Y - 34 - _lines.Count * lh;
        if (_lines.Count > 0) Panel(new Rect2(8, by - 6, 520, _lines.Count * lh + 10));
        for (int i = 0; i < _lines.Count; i++)
        {
            var (t, c, age) = _lines[i];
            float a = age > 7 ? (float)Math.Max(0, 1 - (age - 7) / 2) : 1;
            Text(new Vector2(16, by + 10 + i * lh), t, new Color(c, a), 13);
        }
        if (ShowHints) Text(new Vector2(8, sz.Y - 12), L.T("ex_ui_keys"), PixelTheme.Dim, 12);
    }

    /// <summary>The surgeon line (4.10): shown while the hero has a wound whose window is open (a tendon or a fracture within a day of the blow, a bleeding wound).</summary>
    string? SurgeonLine()
    {
        var sg = C.Surgeon;
        if (!sg.Known) return null;
        var b = C.Hero.Book; long now = C.GameSec;
        bool open = b.Records.Any(w => w.Cat is Ravensguard.Sim.Combat.Wounds.WoundCat.Tendon or Ravensguard.Sim.Combat.Wounds.WoundCat.Bone && w.Grade >= 1 && now - w.Born < 86_400 && !w.Is(Ravensguard.Sim.Combat.Wounds.Aid.Resolved))
            || b.RateMlPerS > 0;
        if (!open) return null;
        return L.F("ex_hud_surgeon", sg.Town, sg.DaysX100Horse / 10 / 10.0, sg.DaysX100Foot / 10 / 10.0);
    }

    // ------------------------------------------------------------------ the auto-map (M)

    void DrawMap(Vector2 sz)
    {
        var m = C.Map; var K = C.Known;
        float pw = Math.Min(sz.X - 40, 820), ph = Math.Min(sz.Y - 40, 480);
        var rect = new Rect2((sz.X - pw) / 2, (sz.Y - ph) / 2, pw, ph);
        DrawRect(new Rect2(Vector2.Zero, sz), new Color(0, 0, 0, 0.55f));
        Panel(rect);
        Text(rect.Position + new Vector2(14, 22), L.F("ex_map_title", SiteName), PixelTheme.Gold, 16);
        // the level tabs
        float tx = rect.Position.X + 14;
        for (int l = 0; l < m.Levels; l++)
        {
            string tab = L.F("ex_hud_level", -(l + 1));
            var tr = new Rect2(tx, rect.Position.Y + 32, 86, 20);
            DrawRect(tr, l == _mapLevel ? PixelTheme.Rim : new Color(PixelTheme.Border, 0.8f));
            Text(tr.Position + new Vector2(8, 15), tab, l == _mapLevel ? PixelTheme.Gold : PixelTheme.Dim, 13);
            tx += 92;
        }
        float area = Math.Min(pw - 200, ph - 80);
        float k = Math.Max(2, (float)Math.Floor(area / Math.Max(m.W, m.H)));
        var o = rect.Position + new Vector2(14, 62);
        DrawRect(new Rect2(o, new Vector2(m.W * k, m.H * k)), new Color(0.05f, 0.04f, 0.03f));
        for (int y = 0; y < m.H; y++)
            for (int x = 0; x < m.W; x++)
            {
                int cell = m.Index(_mapLevel, x, y);
                if (!K.Known(cell)) continue;
                var t = m.Tiles[cell];
                var c = t switch
                {
                    SiteTile.Rock => new Color("3a3028"), SiteTile.Wall => new Color("6a645c"), SiteTile.Water => new Color("3a6a9a"), SiteTile.Rubble => new Color("7a6a50"),
                    SiteTile.StairsUp or SiteTile.StairsDown or SiteTile.ShaftDown or SiteTile.ShaftUp => PixelTheme.Gold, SiteTile.Container => new Color("a07840"),
                    SiteTile.Door => new Color("8a5a30"), SiteTile.Hole => new Color("000000"), SiteTile.Pillar => new Color("6a645c"), _ => new Color("b8a888"),
                };
                DrawRect(new Rect2(o.X + x * k, o.Y + y * k, k, k), c);
                if (m.TrapAt(cell) is { Detected: true }) DrawRect(new Rect2(o.X + x * k, o.Y + y * k, k, k), Trap);
            }
        // the frontier shimmers: unknown cells beside known floor
        float sh = 0.25f + 0.2f * Mathf.Sin((float)_t * 4f);
        for (int y = 1; y < m.H - 1; y++)
            for (int x = 1; x < m.W - 1; x++)
            {
                int cell = m.Index(_mapLevel, x, y);
                if (K.Known(cell)) continue;
                bool edge = K.Known(cell - 1) && SiteTiles.Walkable(m.Tiles[cell - 1]) || K.Known(cell + 1) && SiteTiles.Walkable(m.Tiles[cell + 1])
                    || K.Known(cell - m.W) && SiteTiles.Walkable(m.Tiles[cell - m.W]) || K.Known(cell + m.W) && SiteTiles.Walkable(m.Tiles[cell + m.W]);
                if (edge) DrawRect(new Rect2(o.X + x * k, o.Y + y * k, k, k), new Color(0.8f, 0.7f, 0.5f, sh));
            }
        foreach (var pin in K.Pins.Where(p => p.Level == _mapLevel)) { var (_, px, py) = m.At(pin.Cell); DrawCircle(new Vector2(o.X + (px + 0.5f) * k, o.Y + (py + 0.5f) * k), k * 0.9f, new Color("e0e0ff")); }
        foreach (var mb in C.Members.Where(x => x.Level == _mapLevel))
            DrawCircle(new Vector2(o.X + mb.X / 1000f * k, o.Y + mb.Y / 1000f * k), Math.Max(2, k * 0.9f), mb.Hero ? PixelTheme.Gold : new Color("7aa0c8"));
        // the legend
        float lx = o.X + m.W * k + 18, ly = o.Y + 10;
        void Leg(Color c, string key) { DrawRect(new Rect2(lx, ly - 9, 10, 10), c); Text(new Vector2(lx + 16, ly), L.T(key), PixelTheme.Body, 13); ly += 18; }
        Leg(new Color("b8a888"), "ex_map_floor"); Leg(PixelTheme.Gold, "ex_map_way"); Leg(new Color("a07840"), "ex_map_box"); Leg(new Color("3a6a9a"), "ex_map_water");
        Leg(new Color("7a6a50"), "ex_map_rubble"); Leg(Trap, "ex_map_trap"); Leg(new Color("e0e0ff"), "ex_map_pin");
        ly += 8;
        Text(new Vector2(lx, ly), L.F("ex_map_known", K.CountLevel(_mapLevel)), PixelTheme.Dim, 13); ly += 18;
        var (em, es) = C.ExitTime(C.Hero);
        if (em >= 0) Text(new Vector2(lx, ly), L.F("ex_hud_exit", em, Math.Max(1, (es + 59) / 60)), PixelTheme.Dim, 13);
        Text(new Vector2(rect.Position.X + 14, rect.End.Y - 10), L.T("ex_map_keys"), PixelTheme.Dim, 12);
    }
}
