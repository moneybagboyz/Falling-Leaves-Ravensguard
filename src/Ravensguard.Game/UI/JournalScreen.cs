using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Rumours;

namespace Ravensguard.Game.UI;

/// <summary>
/// The Journal (J, dialogue spec 4.7, 6): the rumours the hero has heard, each a card with its words, who told you and when, how many people it passed through and how far to trust it (seen
/// first-hand, reliable, hearsay, garbled). "How do you know?" opens the trace: the chain of tellers back to the first person who knew, then the chronicle entry it all began with. The work board is a tab.
/// </summary>
public partial class JournalScreen : Control
{
    readonly ScreenRouter _router; readonly TalkLink _link; readonly Func<IReadOnlyList<string>> _names; readonly Action _openWork;
    ItemList _list = null!; VBoxContainer _detail = null!; JournalView? _view; int _sel; bool _trace;
    int _shotWait; double _refresh;
    /// <summary>Dev: the picture is taken with the trace open and the first card chosen.</summary>
    public string? ShotPath { get; init; }
    public bool StartTrace { get; init; }
    /// <summary>The first hour's list (onboarding spec 6): shown at the top while it is not complete, with the optional "Pin to map?" button; the game never marks the map unasked.</summary>
    public Func<IReadOnlyList<(string Text, bool Done)>>? GettingStarted { get; init; }
    public Func<string?>? PinMentor { get; init; }

    public JournalScreen(ScreenRouter router, TalkLink link, Func<IReadOnlyList<string>> settlementNames, Action openWork) { _router = router; _link = link; _names = settlementNames; _openWork = openWork; _trace = false; }

    public override void _Ready()
    {
        _trace = StartTrace;
        var margin = UiKit.Margin(this, 14);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        var tabs = new HBoxContainer(); tabs.AddThemeConstantOverride("separation", 8); root.AddChild(tabs);
        tabs.AddChild(UiKit.Title(L.T("JOURNAL_TITLE")));
        tabs.AddChild(new Button { Text = L.T("JOURNAL_TAB_RUMOURS"), ToggleMode = true, ButtonPressed = true, CustomMinimumSize = new Vector2(120, 28) });
        var work = new Button { Text = L.T("JOURNAL_TAB_WORK"), CustomMinimumSize = new Vector2(120, 28) };
        work.Pressed += () => { _router.Pop(); _openWork(); };
        tabs.AddChild(work);
        if (GettingStarted?.Invoke() is { Count: > 0 } gs)
        {
            var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 1);
            box.AddChild(UiKit.Title(L.T("ONB_TITLE")));
            foreach (var (t, d) in gs) { var l = new Label { Text = (d ? "[x] " : "[  ] ") + t, ThemeTypeVariation = "HintLabel" }; if (d) l.Modulate = new Color(1, 1, 1, 0.55f); box.AddChild(l); }
            if (PinMentor != null) { var pin = new Button { Text = L.T("ONB_PIN"), SizeFlagsHorizontal = SizeFlags.ShrinkBegin }; pin.Pressed += () => { if (PinMentor() is { } m) { _router.ShowToast(m, 6f); pin.Disabled = true; } }; box.AddChild(pin); }
            root.AddChild(UiKit.Panel(box, 0, 0));
        }
        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 10); root.AddChild(cols);
        _list = new ItemList { CustomMinimumSize = new Vector2(330, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        _list.ItemSelected += i => { _sel = (int)i; _trace = false; ShowCard(); };
        cols.AddChild(_list);
        _detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _detail.AddThemeConstantOverride("separation", 5);
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; scroll.AddChild(_detail);
        var panel = UiKit.Panel(scroll, 0, 0); panel.SizeFlagsHorizontal = SizeFlags.ExpandFill; cols.AddChild(panel);
        var bottom = new HBoxContainer(); bottom.AddThemeConstantOverride("separation", 8); root.AddChild(bottom);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); back.Pressed += _router.Pop; bottom.AddChild(back);
        bottom.AddChild(UiKit.Hint(L.F("JOURNAL_CLOSE_HINT", InputBootstrap.Hint("world.panel_journal"))));
        Refresh(true);
    }

    static string DayName(int day)
    {
        var t = new GameTime((long)day * TimeConst.SecondsPerDay);
        return $"{L.T("SEASON_" + t.Season.ToString().ToUpperInvariant())} {t.DayOfSeason}";
    }

    string Words(JournalCard c) => RumourText.Render(c.Key, c.Place, c.Who, c.About, c.Number, c.Flags, c.PlaceAlt, _names(), GameText.Instance);

    void Refresh(bool force = false)
    {
        var v = _link.View;
        if (!force && ReferenceEquals(v, _view)) return;
        _view = v; _list.Clear();
        if (v != null) foreach (var c in v.Cards) _list.AddItem($"[{L.T("JOURNAL_CONF_" + c.Confidence.ToUpperInvariant())}] {Words(c)}");
        _sel = Math.Clamp(_sel, 0, Math.Max(0, (v?.Cards.Count ?? 1) - 1));
        if (v is { Cards.Count: > 0 }) _list.Select(_sel);
        ShowCard();
    }

    void ShowCard()
    {
        foreach (var c in _detail.GetChildren()) { _detail.RemoveChild(c); c.QueueFree(); }
        if (_view is not { Cards.Count: > 0 }) { _detail.AddChild(UiKit.Body(L.T("JOURNAL_RUMOURS_EMPTY"))); return; }
        var c0 = _view.Cards[_sel];
        _detail.AddChild(UiKit.Body(Words(c0)));
        string when = DayName(c0.Day);
        _detail.AddChild(UiKit.Hint(c0.How == HearHow.Overheard ? L.F("JOURNAL_OVERHEARD", c0.TellerName, when) : L.F("JOURNAL_TOLD_BY", c0.TellerName.Length > 0 ? c0.TellerName : L.T("R_WHO_TRAVELLER"), when), true));
        _detail.AddChild(UiKit.Hint(L.F("JOURNAL_PASSED", c0.PassedThrough) + "  ·  " + L.T("JOURNAL_CONF_" + c0.Confidence.ToUpperInvariant()) + (c0.PartlyTrue ? "  ·  " + L.T("JOURNAL_PARTLY") : ""), true));
        var how = new Button { Text = _trace ? L.T("JOURNAL_HIDE") : L.T("JOURNAL_HOW"), CustomMinimumSize = new Vector2(220, 28) };
        how.Pressed += () => { _trace = !_trace; ShowCard(); };
        _detail.AddChild(how);
        if (!_trace) return;
        _detail.AddChild(new Label { Text = c0.How == HearHow.Overheard ? L.F("TRACE_OVERHEARD", c0.TellerName, "", DayName(c0.Day)).Replace("  ", " ") : L.F("TRACE_HEARD_FROM", c0.TellerName.Length > 0 ? c0.TellerName : L.T("R_WHO_TRAVELLER"), DayName(c0.Day)), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        foreach (var s in c0.Steps)
        {
            string line = s.From.Length > 0 ? L.F("TRACE_TOLD", s.Person, s.From, DayName(s.Day)) : s.How == HearHow.Traveller ? L.F("TRACE_TRAVELLER", s.Person, c0.Place, DayName(s.Day)) : L.F("TRACE_WITNESS", s.Person, DayName(s.Day));
            _detail.AddChild(new Label { Text = "<- " + line, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        }
        if (c0.Source.Found)
        {
            _detail.AddChild(new Label { Text = "<- " + L.F("TRACE_CHRONICLE", c0.Source.Text.Length > 0 ? c0.Source.Text : UiKit.Pretty(c0.Source.TypeId) + (c0.Source.Details.Length > 0 ? $" ({c0.Source.Details})" : ""), DayName(c0.Source.Day)), AutowrapMode = TextServer.AutowrapMode.WordSmart, ThemeTypeVariation = "GoldLabel" });
            foreach (var cause in c0.Source.Causes) _detail.AddChild(UiKit.Hint("      " + L.F("TRACE_CAUSE", cause), true));
        }
        else _detail.AddChild(UiKit.Hint("<- " + L.T("TRACE_NO_SOURCE"), true));
    }

    public override void _Process(double delta)
    {
        if ((_refresh += delta) > 0.25) { _refresh = 0; Refresh(); }
        if (ShotPath == null || ++_shotWait != 12) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[journal] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.Pressed(e, "world.panel_journal")) { _router.Pop(); GetViewport().SetInputAsHandled(); }
    }
}
