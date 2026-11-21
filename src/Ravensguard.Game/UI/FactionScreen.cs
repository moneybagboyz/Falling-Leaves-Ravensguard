using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Society;

namespace Ravensguard.Game.UI;

/// <summary>What the Faction screen may ask the world: ask, swear, give (the street view turns each into a hero command and toasts the answer).</summary>
public sealed class FactionActions
{
    public Action<ActionLine>? Do { get; init; }
}

/// <summary>
/// The Faction screen (F; P6-D2, diplomacy-and-ai-lords 6): five tabs. Realm (the realm, its ruler, the chain of lords, the fiefs and the treasuries' bands, your vassals), Opinions (what the people who matter think of you, with the three
/// modifiers behind each), Treaties and wars (expiry, war score, weariness, what the lords did lately), Your standing (the same lines as the Standing tab) and Actions (swear fealty, ask for a manor or a fief, give a gift, renounce your
/// oath), each with the terms in words ("He will say no: what he thinks of you -20 (needs 30)"). Up and Down move, Tab turns the page, Enter acts, / searches the Opinions, Esc closes. A modal screen (the Faction context, alone with
/// Global). A view over the barrier's <see cref="FactionView"/>, refreshed while it is open; fits 960 x 540.
/// </summary>
public partial class FactionScreen : Control, IHandlesBack
{
    public enum Tab { Realm, Opinions, Treaties, Standing, Actions }

    readonly ScreenRouter _router;
    readonly Func<FactionView?> _data;
    readonly Action _refresh;
    readonly FactionActions _acts;
    Tab _tab = Tab.Realm;
    int _row;
    string _search = "";
    LineEdit? _searchBox;
    Label _rank = null!, _status = null!;
    HBoxContainer _tabs = null!;
    VBoxContainer _body = null!;
    ScrollContainer _scroll = null!;
    FactionView? _shown;
    double _acc;
    int _shotWait;
    public string? ShotPath { get; init; }
    public Tab StartTab { init => _tab = value; }
    /// <summary>The rows of the current tab that can be picked (the Actions tab's), for the keys and the smoke.</summary>
    public int Picks { get; private set; }

    public FactionScreen(ScreenRouter router, Func<FactionView?> data, Action refresh, FactionActions acts) { _router = router; _data = data; _refresh = refresh; _acts = acts; }

    public override void _Ready()
    {
        ActiveContexts.Enter(InputContext.Faction);
        var margin = UiKit.Margin(this, 12);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 4); margin.AddChild(root);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8);
        var title = UiKit.Title(L.T("dp_title")); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; head.AddChild(title);
        _rank = UiKit.Hint(""); head.AddChild(_rank);
        root.AddChild(head);
        _tabs = new HBoxContainer(); _tabs.AddThemeConstantOverride("separation", 4); root.AddChild(_tabs);
        foreach (var t in Enum.GetValues<Tab>())
        {
            var b = new Button { Text = L.T("dp_tab_" + t.ToString().ToLowerInvariant()), ToggleMode = true, CustomMinimumSize = new Vector2(0, 24), FocusMode = FocusModeEnum.None };
            var tt = t; b.Pressed += () => { _tab = tt; _row = 0; Build(); };
            _tabs.AddChild(b);
        }
        _status = UiKit.Hint(""); root.AddChild(_status);
        _body = new VBoxContainer(); _body.AddThemeConstantOverride("separation", 2);
        _scroll = UiKit.Scroll(_body);
        var p = UiKit.Panel(_scroll, 0, 0); p.SizeFlagsVertical = SizeFlags.ExpandFill; root.AddChild(p);
        root.AddChild(UiKit.Hint(L.T("dp_keys")));
        _refresh();
        Build();
    }

    public override void _ExitTree() => ActiveContexts.Exit(InputContext.Faction);

    public override void _Process(double delta)
    {
        _acc += delta;
        if (_acc > 0.5) { _acc = 0; _refresh(); var v = _data(); if (!ReferenceEquals(v, _shown)) Build(); }
        if (ShotPath == null || !Visible || _shown == null || ++_shotWait != 12) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[faction] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    static Label Line(string text, bool dim = false, bool gold = false)
    {
        var l = UiKit.Body(text, true);
        if (dim) l.ThemeTypeVariation = "HintLabel";
        if (gold) l.AddThemeColorOverride("font_color", PixelTheme.Gold);
        return l;
    }

    static string Terms(IReadOnlyList<Term> terms) => string.Join("; ", terms.Where(t => !t.Met).Select(t => L.F(t.Key, t.Value, t.Need)));

    void Build()
    {
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        var v = _data(); _shown = v; Picks = 0;
        int i = 0; foreach (var c in _tabs.GetChildren()) if (c is Button b) b.ButtonPressed = (Tab)i++ == _tab;
        if (v == null) { _rank.Text = ""; _status.Text = L.T("dp_waiting"); return; }
        _rank.Text = L.T("dp_rank") + ": " + L.T(v.RankKey);
        _status.Text = L.T("dp_intro");
        switch (_tab)
        {
            case Tab.Realm: BuildRealm(v); break;
            case Tab.Opinions: BuildOpinions(v); break;
            case Tab.Treaties: BuildTreaties(v); break;
            case Tab.Standing: BuildStanding(v); break;
            default: BuildActions(v); break;
        }
    }

    void BuildRealm(FactionView v)
    {
        if (v.RealmName.Length == 0) { _body.AddChild(Line(L.T("dp_realm_none"))); return; }
        _body.AddChild(Line(L.F("dp_realm_of", v.RealmName, v.RulerName), gold: true));
        if (v.Chain.Count > 0) _body.AddChild(Line(L.T("dp_chain") + ": " + string.Join(" > ", v.Chain), true));
        _body.AddChild(Line(L.T("dp_fiefs"), true));
        foreach (var f in v.Fiefs.Take(24))
            _body.AddChild(Line(L.F("dp_fief_row", f.Name, f.Vacant ? L.T("dp_fief_vacant") : f.Mine ? L.T("dp_fief_yours") : f.Holder, L.T(f.TreasuryKey)), gold: f.Mine));
        if (v.Fiefs.Count > 24) _body.AddChild(Line("...", true));
        _body.AddChild(Line(L.T("dp_vassals"), true));
        if (v.Vassals.Count == 0) _body.AddChild(Line(L.T("dp_vassals_none")));
        foreach (var n in v.Vassals) _body.AddChild(Line(n));
    }

    void BuildOpinions(FactionView v)
    {
        _body.AddChild(Line(L.T("dp_op_head") + (_search.Length > 0 ? "   /" + _search : ""), true));
        var rows = v.Regard.Where(o => _search.Length == 0 || o.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();
        if (rows.Count == 0) _body.AddChild(Line(L.T("dp_op_none")));
        foreach (var o in rows.Take(14))
        {
            var l = Line(L.F("dp_op_value", o.Name, (o.Value > 0 ? "+" : "") + o.Value), gold: o.Value >= 30);
            if (o.Value <= -30) l.AddThemeColorOverride("font_color", new Color(0.9f, 0.45f, 0.4f));
            _body.AddChild(l);
            string why = o.Mods.Count == 0 ? L.T("dp_op_why_none") : string.Join("; ", o.Mods.Select(m => L.F("dp_op_why", L.T(m.KindKey), (m.Amount > 0 ? "+" : "") + m.Amount, m.Day)));
            _body.AddChild(Line("  " + why, true));
        }
    }

    void BuildTreaties(FactionView v)
    {
        _body.AddChild(Line(L.T("dp_treaties_head"), true));
        if (v.Treaties.Count == 0) _body.AddChild(Line(L.T("dp_treaties_none")));
        foreach (var t in v.Treaties.Take(8))
            _body.AddChild(Line((t.ExpiryDay >= 0 ? L.F("dp_treaty_row", L.T(t.KindKey), t.A, t.B, t.ExpiryDay) : L.F("dp_treaty_open", L.T(t.KindKey), t.A, t.B)) + (t.Tribute > 0 ? "; " + L.F("dp_treaty_tribute", UiKit.Money(t.Tribute)) : "")));
        _body.AddChild(Line(L.T("dp_wars_head"), true));
        if (v.Wars.Count == 0) _body.AddChild(Line(L.T("dp_wars_none")));
        foreach (var w in v.Wars.Take(6)) _body.AddChild(Line(L.F("dp_war_row", w.A, w.B, L.T(w.CbKey), (w.Score > 0 ? "+" : "") + w.Score, w.Seasons, w.WearinessA, w.WearinessD)));
        _body.AddChild(Line(L.T("dp_log_head"), true));
        if (v.Log.Count == 0) _body.AddChild(Line(L.T("dp_log_none")));
        foreach (var l in v.Log.Take(6)) _body.AddChild(Line(L.F("dp_log_row", l.Day, l.Lord, L.T(l.ActionKey), L.T(l.NoteKey)), true));
    }

    void BuildStanding(FactionView v)
    {
        var lines = v.Standing.Lines.Where(l => l.Group.Kind is GroupKind.Realm or GroupKind.Fief).Take(14).ToList();
        if (lines.Count == 0) _body.AddChild(Line(L.T("st_none")));
        foreach (var line in lines)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            string kind = L.T("st_kind_" + line.Group.Kind.ToString().ToLowerInvariant());
            row.AddChild(new Label { Text = line.Name.Length > 0 ? $"{kind}: {line.Name}" : kind, CustomMinimumSize = new Vector2(250, 0), ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill });
            row.AddChild(new Label { Text = L.T(Standing.BandKey(line.Band)), CustomMinimumSize = new Vector2(110, 0) });
            row.AddChild(new Label { Text = (line.ValuePm > 0 ? "+" : "") + line.ValuePm, CustomMinimumSize = new Vector2(52, 0), HorizontalAlignment = HorizontalAlignment.Right });
            _body.AddChild(row);
        }
    }

    void BuildActions(FactionView v)
    {
        _body.AddChild(Line(L.T("dp_actions_head"), true));
        if (v.Actions.Count == 0) { _body.AddChild(Line(L.T("dp_actions_none"))); return; }
        Picks = v.Actions.Count; _row = Math.Clamp(_row, 0, Picks - 1);
        for (int k = 0; k < v.Actions.Count; k++)
        {
            var a = v.Actions[k];
            var label = Line((k == _row ? "> " : "  ") + L.F("dp_act_" + a.Id, a.TargetName), gold: k == _row);
            _body.AddChild(label);
            string verdict = a.Id is "gift" or "giftr" or "renounce" ? "" : a.Ok ? L.T("dp_act_yes") : L.F("dp_act_no", Terms(a.Terms));
            if (verdict.Length > 0) _body.AddChild(Line("      " + verdict, true));
            int kk = k;
            label.MouseFilter = MouseFilterEnum.Stop;
            label.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) { _row = kk; if (mb.DoubleClick) Act(); else Build(); } };
        }
        _body.AddChild(Line(L.T("dp_act_hint"), true));
    }

    void Act()
    {
        if (_tab != Tab.Actions || _data() is not { } v || _row < 0 || _row >= v.Actions.Count) return;
        _acts.Do?.Invoke(v.Actions[_row]);
    }

    void Search()
    {
        if (_searchBox != null) return;
        _tab = Tab.Opinions; _row = 0;
        _searchBox = new LineEdit { PlaceholderText = L.T("lg_search"), Text = _search, CustomMinimumSize = new Vector2(240, 26) };
        UiKit.CaptureKeys(_searchBox);
        _searchBox.TextSubmitted += t => { _search = t.Trim(); _searchBox?.QueueFree(); _searchBox = null; Build(); };
        _status.GetParent().AddChild(_searchBox);
        _searchBox.GrabFocus();
    }

    void Turn(int d) { int n = Enum.GetValues<Tab>().Length; _tab = (Tab)(((int)_tab + d + n) % n); _row = 0; Build(); }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || _router.HasModal || _searchBox != null) return;
        if (Actions.Pressed(e, "faction.back")) _router.Pop();
        else if (Actions.Pressed(e, "faction.up")) { if (_tab == Tab.Actions) { _row = Math.Max(0, _row - 1); Build(); } else _scroll.ScrollVertical -= 40; }
        else if (Actions.Pressed(e, "faction.down")) { if (_tab == Tab.Actions) { _row = Math.Min(Math.Max(0, Picks - 1), _row + 1); Build(); } else _scroll.ScrollVertical += 40; }
        else if (Actions.Pressed(e, "faction.left")) Turn(-1);
        else if (Actions.Pressed(e, "faction.right") || Actions.Pressed(e, "faction.next_tab")) Turn(1);
        else if (Actions.Pressed(e, "faction.act")) Act();
        else if (Actions.Pressed(e, "faction.search")) Search();
        else return;
        GetViewport().SetInputAsHandled();
    }
}
