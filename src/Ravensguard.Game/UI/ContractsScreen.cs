using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.UI;

/// <summary>What the contracts screen asks of the street view: the hero's commands, each recorded by the hero desk (P6-C2, <c>ContractDesk</c>).</summary>
public sealed record ContractActions(Action<int> Take, Action<int> Abandon, Action<int, bool> Pin, Action<int> Hand, Action<int> Report, Action<int> Enter);

/// <summary>
/// The Contracts tab of the Journal and the notice board's list (contracts-and-rumours-v2 6): what is posted where you stand, each with its pay, its deadline day, who posted it and why ("the smithy has no charcoal:
/// deliver 300 kg by day 42"); what you have taken, with the evidence of how it went and the soft pin (a direction, never a marker the game places by itself); the fair's tournament and its entry. A view over the
/// barrier's <see cref="ContractsView"/>; every act is a command the desk records.
/// </summary>
public partial class ContractsScreen : Control
{
    readonly ScreenRouter _router; readonly Func<ContractsView?> _data; readonly ContractActions _act; readonly Func<(long X, long Y)> _hero;
    ItemList _list = null!; VBoxContainer _detail = null!, _fair = null!; Button _tabBoard = null!, _tabMine = null!; Label _head = null!;
    ContractsView? _view; bool _mine; int _sel; double _refresh; int _shotWait;
    public string? ShotPath { get; init; }
    /// <summary>Dev: start on the Taken tab.</summary>
    public bool StartMine { get; init; }

    public ContractsScreen(ScreenRouter router, Func<ContractsView?> data, ContractActions act, Func<(long X, long Y)> heroXY) { _router = router; _data = data; _act = act; _hero = heroXY; }

    public override void _Ready()
    {
        _mine = StartMine;
        var margin = UiKit.Margin(this, 12);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 4); margin.AddChild(root);
        var tabs = new HBoxContainer(); tabs.AddThemeConstantOverride("separation", 8); root.AddChild(tabs);
        tabs.AddChild(UiKit.Title(L.T("ct_title")));
        _tabBoard = new Button { Text = L.T("ct_tab_board"), ToggleMode = true, CustomMinimumSize = new Vector2(150, 26) }; _tabBoard.Pressed += () => { _mine = false; _sel = 0; Build(); }; tabs.AddChild(_tabBoard);
        _tabMine = new Button { Text = L.T("ct_tab_mine"), ToggleMode = true, CustomMinimumSize = new Vector2(120, 26) }; _tabMine.Pressed += () => { _mine = true; _sel = 0; Build(); }; tabs.AddChild(_tabMine);
        _head = UiKit.Hint("", true); root.AddChild(_head);
        _fair = new VBoxContainer(); _fair.AddThemeConstantOverride("separation", 2); root.AddChild(_fair);
        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 10); root.AddChild(cols);
        _list = new ItemList { CustomMinimumSize = new Vector2(340, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        _list.ItemSelected += i => { _sel = (int)i; ShowDetail(); };
        cols.AddChild(_list);
        _detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _detail.AddThemeConstantOverride("separation", 4);
        var panel = UiKit.Panel(UiKit.Scroll(_detail), 0, 0); panel.SizeFlagsHorizontal = SizeFlags.ExpandFill; panel.SizeFlagsVertical = SizeFlags.ExpandFill; cols.AddChild(panel);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200, 30); back.Pressed += _router.Pop; root.AddChild(back);
        Build();
    }

    List<ContractRow> Rows => _view == null ? [] : _mine ? _view.Mine : _view.Board;

    static string Kind(string id) => L.T("ct_kind_" + id);
    static string Money(long m) => Quotes.Money(m);
    static string DayName(int day) { var t = new GameTime((long)day * TimeConst.SecondsPerDay); return $"{L.T("SEASON_" + t.Season.ToString().ToUpperInvariant())} {t.DayOfSeason}"; }

    void Build()
    {
        _view = _data();
        _tabBoard.ButtonPressed = !_mine; _tabMine.ButtonPressed = _mine;
        _head.Text = _view == null ? L.T("ct_empty_board") : L.F("ct_head", _view.PlaceName.Length > 0 ? _view.PlaceName : L.T("ct_nowhere"), Money(_view.CoinMites), _view.Day);
        foreach (var c in _fair.GetChildren()) { _fair.RemoveChild(c); c.QueueFree(); }
        if (_view != null)
            foreach (var t in _view.Fairs)
            {
                var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
                string state = t.Paid ? L.T(t.HeroWon ? "ct_fair_won" : "ct_fair_over") : t.Drawn ? L.T("ct_fair_on") : L.F("ct_fair_open", t.FairDay, t.Entrants);
                var l = UiKit.Body(L.F("ct_fair", Money(t.PurseMites), state)); l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(l);
                if (!t.Drawn && !t.Paid) { var b = new Button { Text = t.Entered ? L.T("ct_entered_tag") : L.F("ct_btn_enter", Money(t.FeeMites)), Disabled = t.Entered, CustomMinimumSize = new Vector2(180, 26) }; int id = t.Id; b.Pressed += () => _act.Enter(id); row.AddChild(b); }
                _fair.AddChild(row);
            }
        _list.Clear();
        foreach (var r in Rows) _list.AddItem($"{(r.Pinned ? "* " : "")}{Kind(r.Kind)}  {Money(r.PayMites)}  {(r.DeadlineDay >= 0 ? L.F("ct_deadline", r.DeadlineDay) : "")}");
        _sel = Math.Clamp(_sel, 0, Math.Max(0, Rows.Count - 1));
        if (Rows.Count > 0) _list.Select(_sel);
        ShowDetail();
    }

    void ShowDetail()
    {
        foreach (var c in _detail.GetChildren()) { _detail.RemoveChild(c); c.QueueFree(); }
        void Line(string t, bool hint = false) { var l = hint ? UiKit.Hint(t, true) : UiKit.Body(t); l.AutowrapMode = TextServer.AutowrapMode.WordSmart; _detail.AddChild(l); }
        if (Rows.Count == 0) { Line(L.T(_mine ? "ct_empty_mine" : "ct_empty_board")); return; }
        var r = Rows[Math.Clamp(_sel, 0, Rows.Count - 1)];
        Line(Kind(r.Kind));
        Line(L.F("ct_pay", Money(r.PayMites)) + (r.DeadlineDay >= 0 ? "  ·  " + L.F("ct_deadline", r.DeadlineDay) + " (" + DayName(r.DeadlineDay) + ")" : "  ·  " + L.T("ct_no_deadline")));
        Line(L.F("ct_by", L.T("ct_issuer_" + r.IssuerKind)) + (r.PlaceName.Length > 0 ? "  ·  " + r.PlaceName : ""), true);
        string why = L.T(r.WhyKey); if (why.Contains("{0}")) why = L.F(r.WhyKey, r.WhyArg);
        Line(why);
        if (r.Criminal) Line(L.T("ct_criminal"), true);
        if (r.FromJob) { Line(L.T("ct_bounty_how"), true); return; }
        if (r.Grams > 0) Line(L.F("ct_progress", r.Delivered / 1000, r.Grams / 1000), true);
        if (r.BondMites > 0) Line(L.F("ct_bond", Money(r.BondMites)), true);
        Line(L.T("ct_state_" + r.State.ToString().ToLowerInvariant()), true);
        if (r.Pinned && _view != null)
        {
            var (hx, hy) = _hero(); long dx = r.PlaceXM - hx, dy = r.PlaceYM - hy;
            Line(L.F("ct_pinned_where", r.PlaceName, L.T("ct_dir_" + ContractsView.Direction(dx, dy)), Math.Max(0, PathPlan.Dist(dx, dy) / 1000)), true);
        }
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 6); _detail.AddChild(buttons);
        int id = r.Id;
        if (!_mine && r.State == WorkState.Open) { var b = new Button { Text = L.T("ct_btn_take"), CustomMinimumSize = new Vector2(120, 28) }; b.Pressed += () => _act.Take(id); buttons.AddChild(b); }
        if (_mine && r.State == WorkState.Taken)
        {
            var ab = new Button { Text = L.T("ct_btn_abandon"), CustomMinimumSize = new Vector2(120, 28) }; ab.Pressed += () => _act.Abandon(id); buttons.AddChild(ab);
            if (r.Grams > 0) { var h = new Button { Text = L.T("ct_btn_hand"), CustomMinimumSize = new Vector2(150, 28) }; h.Pressed += () => _act.Hand(id); buttons.AddChild(h); }
            var rp = new Button { Text = L.T("ct_btn_report"), CustomMinimumSize = new Vector2(120, 28) }; rp.Pressed += () => _act.Report(id); buttons.AddChild(rp);
        }
        var pin = new Button { Text = L.T(r.Pinned ? "ct_btn_unpin" : "ct_btn_pin"), CustomMinimumSize = new Vector2(120, 28) }; bool on = !r.Pinned; pin.Pressed += () => _act.Pin(id, on); buttons.AddChild(pin);
        if (r.Evidence.Count > 0)
        {
            Line(L.T("ct_evidence"), true);
            foreach (var (type, day) in r.Evidence) Line("  " + DayName(day) + ": " + L.T("ct_ev_" + type), true);
        }
    }

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_refresh > 1.0) { _refresh = 0; var v = _data(); if (!ReferenceEquals(v, _view)) Build(); }
        if (ShotPath == null || !Visible || ++_shotWait != 12) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[contracts] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    /// <summary>Dev (smoke, shots): a made-up board and a taken contract.</summary>
    public static ContractsView Demo()
    {
        var v = new ContractsView { Day = 41, Settlement = 0, PlaceName = "Oakford", CoinMites = 18_400 };
        IReadOnlyList<(string, int)> ev = [("work_taken", 41)];
        v.Board.Add(new ContractRow(1, "deliver_goods", 660, 47, "business", "ct_why_stock_short", 2, WorkState.Open, false, true, false, 0, "Oakford", 400_000, 300_000, 0, 300_000, 0, false, []));
        v.Board.Add(new ContractRow(2, "guard_caravan", 2_700, 49, "caravan", "ct_why_caravan_leaving", 0, WorkState.Open, false, false, false, 0, "Oakford", 400_000, 300_000, 0, 0, 0, false, []));
        v.Board.Add(new ContractRow(3, "clear_bandit_cave", 6_000, 55, "fief", "ct_why_band_seen", 0, WorkState.Open, false, false, false, 0, "Oakford", 400_000, 300_000, 0, 0, 0, false, []));
        v.Board.Add(new ContractRow(4, "rescue_captive", 3_000, 51, "household", "ct_why_captive_held", 0, WorkState.Open, false, false, false, 0, "Oakford", 400_000, 300_000, 0, 0, 0, false, []));
        v.Board.Add(new ContractRow(-5, "bounty_dead_or_alive", 2_000, -1, "fief", "ct_why_crime", 0, WorkState.Open, false, false, false, 0, "Oakford", 400_000, 300_000, 0, 0, 0, true, []));
        v.Mine.Add(new ContractRow(7, "guard_caravan", 2_700, 49, "caravan", "ct_why_caravan_leaving", 0, WorkState.Taken, true, true, false, 0, "Oakford", 400_000, 300_000, 0, 0, 270, false, ev));
        v.Fairs.Add(new TournamentRow(1, "Oakford", 20_000, 1_000, 55, 3, false, false, false, false, false));
        return v;
    }
}
