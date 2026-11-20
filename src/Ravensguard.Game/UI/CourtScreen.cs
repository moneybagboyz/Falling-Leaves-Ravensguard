using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Law;

namespace Ravensguard.Game.UI;

/// <summary>
/// The Court panel (crime-and-justice 4.7, 6): a lord, or his steward, judges on the court day. The left column lists the cases on the docket (accused, crime); the right shows the selected case: the proof the
/// court has, what the culture's law gives at his count of convictions (and why), and every table the lord may put in its place, the harsher ones marked. "Pronounce" gives the chosen sentence (carried out the
/// next morning in the square); "Acquit" lets him go; "Adjourn" leaves the rest to the steward. A view over the barrier's <see cref="CourtView"/>; the router's Back closes it (what is left waits for the next sitting).
/// </summary>
public partial class CourtScreen : Control
{
    readonly ScreenRouter _router;
    readonly Func<CourtView?> _data;
    readonly Action<int, string> _judge;
    readonly Action<int> _acquit;
    readonly Action _adjourn;
    VBoxContainer _cases = null!, _detail = null!;
    Label _status = null!;
    CourtView? _shown;
    int _selected;
    string _alt = "";
    double _refresh;
    int _shotWait;
    /// <summary>Dev: when set, the panel saves its own picture to this path and quits.</summary>
    public string? ShotPath { get; init; }

    public CourtScreen(ScreenRouter router, Func<CourtView?> data, Action<int, string> judge, Action<int> acquit, Action adjourn) { _router = router; _data = data; _judge = judge; _acquit = acquit; _adjourn = adjourn; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 12);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 4); margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T("jl_court_title")));
        _status = UiKit.Hint("", true); root.AddChild(_status);
        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 8); root.AddChild(cols);
        _cases = new VBoxContainer(); _cases.AddThemeConstantOverride("separation", 2);
        var lp = UiKit.Panel(UiKit.Scroll(_cases), 0, 0); lp.SizeFlagsHorizontal = SizeFlags.ExpandFill; lp.SizeFlagsStretchRatio = 0.8f; lp.SizeFlagsVertical = SizeFlags.ExpandFill; cols.AddChild(lp);
        _detail = new VBoxContainer(); _detail.AddThemeConstantOverride("separation", 2);
        var rp = UiKit.Panel(UiKit.Scroll(_detail), 0, 0); rp.SizeFlagsHorizontal = SizeFlags.ExpandFill; rp.SizeFlagsVertical = SizeFlags.ExpandFill; cols.AddChild(rp);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End }; buttons.AddThemeConstantOverride("separation", 8);
        var pron = new Button { Text = L.T("jl_court_pronounce"), CustomMinimumSize = new Vector2(150, 32) }; pron.Pressed += () => { if (Current() is { } c) { _judge(c.Crime, _alt); _alt = ""; } }; buttons.AddChild(pron);
        var acq = new Button { Text = L.T("jl_court_acquit"), CustomMinimumSize = new Vector2(120, 32) }; acq.Pressed += () => { if (Current() is { } c) _acquit(c.Crime); }; buttons.AddChild(acq);
        var adj = new Button { Text = L.T("jl_court_adjourn"), CustomMinimumSize = new Vector2(150, 32) }; adj.Pressed += () => { _adjourn(); _router.Pop(); }; buttons.AddChild(adj);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 120, 32); back.Pressed += _router.Pop; buttons.AddChild(back);
        root.AddChild(buttons);
        Build();
    }

    /// <summary>Dev (smoke, shots): a made-up docket of three cases.</summary>
    public static CourtView Demo()
    {
        static Expected E(string key, string kind, int step, long mites = 0, int days = 0, int lashes = 0, int sev = 4, int priors = 0, int mercy = 0, bool clergy = false, bool lords = false) => new(key, step, kind, mites, days, lashes, 0, 0, "gaol", sev, priors, mercy, clergy, lords, "stocks_then_hand");
        var alts = new List<Expected> { E("fine", "fine", 0, 580, sev: 1), E("gaol", "gaol", 0, days: 15, sev: 2), E("flogging", "flogging", 0, lashes: 20, sev: 3), E("stocks_then_hand", "flogging", 1, lashes: 20, priors: 1), E("outlawry", "outlawry", 0, sev: 5, lords: true), E("hanging", "hanging", 0, sev: 6, lords: true) };
        var cases = new List<CaseView>
        {
            new(1, 11, "Aldric Brook", "theft", 240, 3, 2, false, false, true, E("stocks_then_hand", "flogging", 1, lashes: 20, priors: 1), alts),
            new(2, 12, "Maud Fenn", "assault", 0, 1, 0, false, false, false, E("fine", "fine", 0, 100, sev: 1), alts),
            new(3, 13, "Wat Corby", "poaching", 90, 0, 0, true, false, true, E("fine_or_hand", "hand", 2, priors: 2), alts),
        };
        return new CourtView(0, 4, cases);
    }

    CaseView? Current() => _shown is { Cases.Count: > 0 } v ? v.Cases[Math.Clamp(_selected, 0, v.Cases.Count - 1)] : null;

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_refresh > 0.5) { _refresh = 0; if (!ReferenceEquals(_data(), _shown)) Build(); }
        if (ShotPath == null || !Visible || ++_shotWait != 12) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[court] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    void Build()
    {
        _shown = _data();
        foreach (var c in _cases.GetChildren()) { _cases.RemoveChild(c); c.QueueFree(); }
        foreach (var c in _detail.GetChildren()) { _detail.RemoveChild(c); c.QueueFree(); }
        if (_shown == null || _shown.Cases.Count == 0) { _status.Text = L.T("jl_court_empty"); return; }
        _status.Text = L.F("jl_court_status", _shown.Cases.Count);
        _selected = Math.Clamp(_selected, 0, _shown.Cases.Count - 1);
        for (int i = 0; i < _shown.Cases.Count; i++)
        {
            int k = i; var c = _shown.Cases[i];
            var b = new Button { Text = L.F("jl_court_case", c.AccusedName, JusticeText.Crime(c.Kind)), ToggleMode = true, ButtonPressed = i == _selected, Alignment = HorizontalAlignment.Left, ClipText = true, CustomMinimumSize = new Vector2(0, 28) };
            b.Pressed += () => { _selected = k; _alt = ""; Build(); };
            _cases.AddChild(b);
        }
        var cur = _shown.Cases[_selected];
        void Line(string t, bool hint = false) { var l = hint ? UiKit.Hint(t, true) : UiKit.Body(t); l.AutowrapMode = TextServer.AutowrapMode.WordSmart; _detail.AddChild(l); }
        Line(L.F("jl_court_accused", cur.AccusedName, JusticeText.Crime(cur.Kind)));
        if (cur.ValueMites > 0) Line(L.F("jl_court_value", Ravensguard.Sim.Trade.Quotes.Money(cur.ValueMites)));
        Line(JusticeText.Proof(cur.Witnesses, cur.Attending, cur.InAct, cur.Confessed) + (cur.Proved ? "" : "  " + L.T("jl_court_no_proof")));
        Line(L.T("jl_court_law"), true);
        Line("  " + JusticeText.Line(cur.Kind, cur.Expected));
        Line(L.T("jl_court_instead"), true);
        foreach (var a in cur.Alternatives.Where(a => a.Key != cur.Expected.Key).Take(12))
        {
            var key = a.Key; bool chosen = _alt == key;
            var b = new Button { Text = (a.LordsLaw ? "! " : "  ") + L.T(a.KeyText) + ": " + JusticeText.What(a), ToggleMode = true, ButtonPressed = chosen, Alignment = HorizontalAlignment.Left, ClipText = true, CustomMinimumSize = new Vector2(0, 24) };
            b.Pressed += () => { _alt = _alt == key ? "" : key; Build(); };
            _detail.AddChild(b);
        }
        if (_alt.Length > 0) Line(L.F("jl_court_chosen", L.T("jl_pun_" + _alt)), true);
    }
}
