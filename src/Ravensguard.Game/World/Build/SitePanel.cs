using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World.Build;

/// <summary>
/// P5-C: the site panel (J in build mode; build-mode-and-construction 6): why the site works or stands still in plain words ("Stopped: no thatch on site"), the piece-hours done of the
/// total, the materials needed, in the pile and on the way (what is missing in red), the crew (who, labourer or skilled, wage, hours today, at work or idle), the wages in escrow and the days
/// they last, the rooms the building will have (RoomDetector v2 over the planned cells), and Pause / Resume, Hire a labourer or a skilled hand, Unload your pack, Work by hand.
/// A view of the barrier's construction view; every button is one construction command through the hero's desk. Fits 960 x 540.
/// </summary>
public partial class SitePanel : Control
{
    public sealed class Acts
    {
        public Action<bool>? Pause;
        public Action<bool>? Hire;
        public Action? Unload;
        public Action<bool>? HandWork;
        /// <summary>P6-B6 (holdings 3.8): buy what the pile lacks and have it carted; post haul jobs for what the household holds elsewhere.</summary>
        public Action? BuyDeliver;
        public Action? PostHaul;
    }

    readonly ScreenRouter _router;
    readonly Func<ConstructionView?> _view;
    readonly EntityId _site;
    readonly Func<List<PlannedRoom>?> _rooms;
    readonly Acts _act;
    readonly Func<bool> _hand;
    /// <summary>A plan's name by its id (the content's or the player's); null: the id made readable.</summary>
    public Func<string, string?>? PlanName { get; init; }
    Label _title = null!, _state = null!, _progress = null!, _wages = null!;
    GridContainer _mats = null!;
    VBoxContainer _crew = null!, _roomBox = null!;
    Button _pause = null!, _handBtn = null!;
    ConstructionView? _shown; bool _roomsShown; double _acc;

    public SitePanel(ScreenRouter router, Func<ConstructionView?> view, EntityId site, Func<List<PlannedRoom>?> rooms, Acts act, Func<bool> hand)
    { _router = router; _view = view; _site = site; _rooms = rooms; _act = act; _hand = hand; }

    static T Small<T>(T c, int size = 13) where T : Control { c.AddThemeFontSizeOverride("font_size", size); return c; }
    static Label Cell(string t, bool right = false, Color? col = null)
    {
        var l = Small(new Label { Text = t, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, ClipText = true, CustomMinimumSize = new Vector2(right ? 70 : 150, 0) });
        if (col is { } c) l.AddThemeColorOverride("font_color", c);
        return l;
    }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 5); margin.AddChild(root);
        _title = UiKit.Title(""); root.AddChild(_title);
        _state = Small(UiKit.Body("", wrap: true)); root.AddChild(_state);
        _progress = Small(UiKit.Hint("")); root.AddChild(_progress);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 12); root.AddChild(body);

        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; left.AddThemeConstantOverride("separation", 3); body.AddChild(left);
        left.AddChild(Small(UiKit.Hint(L.T("BUILD_SITE_MATERIALS"))));
        _mats = new GridContainer { Columns = 4 }; _mats.AddThemeConstantOverride("h_separation", 8); _mats.AddThemeConstantOverride("v_separation", 0);
        var mp = UiKit.Panel(UiKit.Scroll(_mats)); mp.SizeFlagsVertical = SizeFlags.ExpandFill; left.AddChild(mp);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 3); body.AddChild(right);
        right.AddChild(Small(UiKit.Hint(L.T("BUILD_SITE_CREW"))));
        _crew = new VBoxContainer(); _crew.AddThemeConstantOverride("separation", 0);
        var cp = UiKit.Panel(UiKit.Scroll(_crew)); cp.SizeFlagsVertical = SizeFlags.ExpandFill; right.AddChild(cp);
        _wages = Small(UiKit.Body("", wrap: true)); right.AddChild(_wages);
        right.AddChild(Small(UiKit.Hint(L.T("BUILD_ROOMS_HEAD"))));
        _roomBox = new VBoxContainer(); _roomBox.AddThemeConstantOverride("separation", 0);
        var rp = UiKit.Panel(UiKit.Scroll(_roomBox)); rp.SizeFlagsVertical = SizeFlags.ExpandFill; right.AddChild(rp);

        var bar = new HBoxContainer(); bar.AddThemeConstantOverride("separation", 6); root.AddChild(bar);
        Button B(string t, Action a, int w = 140) { var b = Small(new Button { Text = t, CustomMinimumSize = new Vector2(w, 30), ClipText = true }); b.Pressed += a; bar.AddChild(b); return b; }
        _pause = B(L.T("BUILD_SITE_PAUSE"), () => { if (Site() is { } s) _act.Pause?.Invoke(!(s.State == SiteState.Paused || s.State == SiteState.Abandoned)); });
        B(L.T("BUILD_SITE_HIRE"), () => _act.Hire?.Invoke(false));
        B(L.T("BUILD_SITE_HIRE_SKILLED"), () => _act.Hire?.Invoke(true), 150);
        B(L.T("BUILD_SITE_UNLOAD"), () => _act.Unload?.Invoke(), 150);
        _handBtn = B("", () => { _act.HandWork?.Invoke(!_hand()); _shown = null; });
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 120, 30); back.Pressed += _router.Pop; bar.AddChild(back);
        var bar2 = new HBoxContainer(); bar2.AddThemeConstantOverride("separation", 6); root.AddChild(bar2);
        Button B2(string t, Action a, int w = 200) { var b = Small(new Button { Text = t, CustomMinimumSize = new Vector2(w, 30), ClipText = true }); b.Pressed += a; bar2.AddChild(b); return b; }
        B2(L.T("hw_q_buy_deliver"), () => _act.BuyDeliver?.Invoke(), 260);
        B2(L.T("hw_q_post_haul"), () => _act.PostHaul?.Invoke(), 200);
        Build();
    }

    SiteView? Site() => _view()?.Sites.FirstOrDefault(s => s.Id == _site);

    public override void _Process(double delta)
    {
        _acc += delta;
        if (_acc < 0.4) return;
        _acc = 0;
        if (!ReferenceEquals(_view(), _shown) || (!_roomsShown && _rooms() != null)) Build();
    }

    static string Kg(long g) => BuildCatalog.Kg(g);

    /// <summary>Why the site stands still, in plain words (spec 6).</summary>
    static string StateText(SiteView s)
    {
        if (s.State == SiteState.Finished) return L.T("BUILD_SITE_FINISHED_STATE");
        if (s.State == SiteState.Abandoned) return L.T("BUILD_SITE_ABANDONED");
        string missing = s.Materials.Where(m => m.InPileG < m.NeedG).Select(m => m.Name).FirstOrDefault() ?? "";
        return s.Pause switch
        {
            PauseReason.NoMaterials => L.F("BUILD_SITE_NO_MATERIALS", missing.ToLowerInvariant()),
            PauseReason.NoWages => L.T("BUILD_SITE_NO_WAGES"),
            PauseReason.Rain => L.T("BUILD_SITE_RAIN"),
            PauseReason.Night => L.T("BUILD_SITE_NIGHT"),
            PauseReason.Owner => L.T("BUILD_SITE_STOPPED"),
            _ => s.Crew == 0 ? L.T("BUILD_SITE_NO_CREW") : L.F("BUILD_SITE_WORKING", s.Working, s.Crew),
        };
    }

    void Build()
    {
        var v = _view(); _shown = v;
        var s = Site();
        if (s == null) { _title.Text = L.T("BUILD_SITE_TITLE_NONE"); _state.Text = L.T("BUILD_NO_SITE"); return; }
        _title.Text = L.F("BUILD_SITE_TITLE", s.Blueprint.Length > 0 ? PlanName?.Invoke(s.Blueprint) ?? UiKit.Pretty(s.Blueprint.StartsWith("player.", StringComparison.Ordinal) ? s.Blueprint[7..] : s.Blueprint) : L.T("BUILD_SITE_OWN_DESIGN"));
        _state.Text = StateText(s);
        _state.RemoveThemeColorOverride("font_color");
        if (s.Pause != PauseReason.None || s.State != SiteState.Active) _state.AddThemeColorOverride("font_color", new Color(1f, 0.72f, 0.4f));
        long pct = s.TotalUnits > 0 ? s.DoneUnits * 100 / s.TotalUnits : 100;
        _progress.Text = L.F("BUILD_SITE_PROGRESS", BuildCatalog.Hours(s.DoneUnits / 3_600_000), BuildCatalog.Hours(s.TotalUnits / 3_600_000), pct, s.Built, s.Ghosts, Kg(s.PileG));
        _pause.Text = L.T(s.State is SiteState.Paused or SiteState.Abandoned ? "BUILD_SITE_RESUME" : "BUILD_SITE_PAUSE");
        _handBtn.Text = L.T(_hand() ? "BUILD_SITE_HAND_STOP" : "BUILD_SITE_HAND");

        foreach (var c in _mats.GetChildren()) { _mats.RemoveChild(c); c.QueueFree(); }
        var dim = PixelTheme.Dim;
        _mats.AddChild(Cell(L.T("BUILD_SITE_COL_GOOD"), col: dim)); _mats.AddChild(Cell(L.T("BUILD_SITE_COL_NEED"), true, dim));
        _mats.AddChild(Cell(L.T("BUILD_SITE_COL_PILE"), true, dim)); _mats.AddChild(Cell(L.T("BUILD_SITE_COL_WAY"), true, dim));
        if (s.Materials.Count == 0) { _mats.AddChild(Cell(L.T("BUILD_SITE_NOTHING_NEEDED"), col: dim)); for (int i = 0; i < 3; i++) _mats.AddChild(Cell("")); }
        foreach (var m in s.Materials)
        {
            bool short_ = m.InPileG + m.OnTheWayG < m.NeedG;
            var col = short_ ? new Color(1f, 0.45f, 0.38f) : m.InPileG >= m.NeedG ? new Color(0.55f, 0.95f, 0.55f) : (Color?)null;
            _mats.AddChild(Cell(m.Name, col: col)); _mats.AddChild(Cell(Kg(m.NeedG) + " kg", true)); _mats.AddChild(Cell(Kg(m.InPileG) + " kg", true, col)); _mats.AddChild(Cell(Kg(m.OnTheWayG) + " kg", true));
        }

        foreach (var c in _crew.GetChildren()) { _crew.RemoveChild(c); c.QueueFree(); }
        var crew = v!.Crew.Where(c => c.Site == _site).ToList();
        if (crew.Count == 0) _crew.AddChild(Small(UiKit.Body(L.T("BUILD_SITE_NO_HANDS"))));
        foreach (var c in crew)
            _crew.AddChild(Small(new Label { Text = L.F("BUILD_SITE_CREW_ROW", c.Name.Length > 0 ? c.Name : "-", L.T("BUILD_CREW_" + c.Class.ToString().ToUpperInvariant()), c.WageMites > 0 ? Quotes.Money(c.WageMites) : "-",
                (c.WorkedTodaySec / 360) / 10.0, L.T(c.Working ? "BUILD_CREW_AT_WORK" : "BUILD_CREW_IDLE")), ClipText = true }));
        long perDay = crew.Sum(c => c.WageMites);
        _wages.Text = perDay > 0 ? L.F("BUILD_SITE_WAGES", Quotes.Money(s.EscrowMites), Quotes.Money(perDay), s.EscrowMites / Math.Max(1, perDay)) : L.F("BUILD_SITE_WAGES_NONE", Quotes.Money(s.EscrowMites));

        foreach (var c in _roomBox.GetChildren()) { _roomBox.RemoveChild(c); c.QueueFree(); }
        var rooms = _rooms(); _roomsShown = rooms != null;
        if (rooms == null) _roomBox.AddChild(Small(UiKit.Hint(L.T("ld_waiting"))));
        else if (rooms.Count == 0) _roomBox.AddChild(Small(UiKit.Body(L.T("BUILD_ROOMS_NONE"))));
        else foreach (var r in rooms) _roomBox.AddChild(Small(UiKit.Body(PlanLibraryPanel.RoomLine(r))));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || _router.HasModal) return;
        if (Actions.Pressed(e, "build.site_panel")) { _router.Pop(); GetViewport().SetInputAsHandled(); }
    }
}
