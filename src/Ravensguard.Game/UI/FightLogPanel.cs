using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Game.World.Combat;
using Ravensguard.Sim.Combat.Flavour;
using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Game.UI;

/// <summary>
/// The combat log (feedback-flavour-log 4.2, F7): the lines of the fight as prose, a timestamp and a severity mark (a colour and a shape: [.] graze, [o] wound, [+] grievous, [#] mortal,
/// [X] fatal), filtered by the toggles at its top (hero only, my side, kills and serious wounds, misses and defences, treatment, beasts only, free text), auto-scrolling (hovering pauses it),
/// the text size 1x to 3x. A line expands to its layer trace and the plain wound list. While a fight runs it shows as a small feed at the bottom of the view; F7 opens it as a panel you can
/// click. Pure view: the log and the formatter are the sim layer's, and every string goes through <c>tr()</c>.
/// </summary>
public partial class FightLogPanel : PanelContainer
{
    readonly Func<FightLog> _log;
    readonly FlavourBook _book;
    readonly bool _screen;
    LogFilter _filter;
    VBoxContainer _rows = null!, _bar = null!;
    ScrollContainer _scroll = null!;
    LineEdit _search = null!;
    Label _count = null!;
    readonly Dictionary<LogFilterFlags, CheckBox> _boxes = [];
    int _builtFor = -1, _builtSerial = -1; string _builtFilter = "";
    readonly HashSet<int> _open = [];
    bool _hover, _pinned;
    double _t;
    bool _expanded;

    /// <summary>True while the panel is the interactive one (F7): it takes the mouse and Esc closes it.</summary>
    public bool Expanded => _expanded;
    public Action? Closed { get; set; }
    /// <summary>The filters (the demo's screenshots set them).</summary>
    public LogFilter Filter => _filter;

    public FightLogPanel(Func<FightLog> log, FlavourBook book, bool asScreen = false)
    {
        _log = log; _book = book; _screen = asScreen;
        _filter = Settings.LogFilter.Length > 0 ? LogFilter.Parse(Settings.LogFilter) : LogFilter.ForFight();
        Name = "FightLogPanel";
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 3); AddChild(v);
        _bar = new VBoxContainer(); _bar.AddThemeConstantOverride("separation", 2); v.AddChild(_bar);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8); _bar.AddChild(head);
        head.AddChild(UiKit.Title(L.T("fl_ui_log_title")));
        _count = new Label { ThemeTypeVariation = "HintLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill }; head.AddChild(_count);
        var pin = new CheckBox { Text = L.T("fl_ui_pin"), ButtonPressed = _pinned, FocusMode = FocusModeEnum.None }; pin.Toggled += on => _pinned = on; head.AddChild(pin);
        var smaller = new Button { Text = "A-", FocusMode = FocusModeEnum.None, TooltipText = L.T("fl_ui_size") }; smaller.Pressed += () => SetSize(Settings.LogTextSize - 1); head.AddChild(smaller);
        var bigger = new Button { Text = "A+", FocusMode = FocusModeEnum.None, TooltipText = L.T("fl_ui_size") }; bigger.Pressed += () => SetSize(Settings.LogTextSize + 1); head.AddChild(bigger);
        var close = new Button { Text = L.T("fl_ui_close"), FocusMode = FocusModeEnum.None }; close.Pressed += Close; head.AddChild(close);
        var filters = new HFlowContainer(); filters.AddThemeConstantOverride("h_separation", 6); _bar.AddChild(filters);
        foreach (var (f, key) in new[] { (LogFilterFlags.HeroOnly, "fl_ui_f_hero"), (LogFilterFlags.MySide, "fl_ui_f_side"), (LogFilterFlags.Serious, "fl_ui_f_serious"), (LogFilterFlags.Misses, "fl_ui_f_misses"),
            (LogFilterFlags.Treatment, "fl_ui_f_treat"), (LogFilterFlags.BeastsOnly, "fl_ui_f_beasts") })
        {
            var cb = new CheckBox { Text = L.T(key), ButtonPressed = _filter.Has(f), FocusMode = FocusModeEnum.None };
            var flag = f;
            cb.Toggled += on => { _filter.Flags = on ? _filter.Flags | flag : _filter.Flags & ~flag; Settings.LogFilter = _filter.Serialise(); Settings.Save(); Rebuild(); };
            _boxes[f] = cb; filters.AddChild(cb);
        }
        _search = new LineEdit { PlaceholderText = L.T("fl_ui_search"), CustomMinimumSize = new Vector2(150, 0), Text = _filter.Search };
        UiKit.CaptureKeys(_search);
        _search.TextChanged += t => { _filter.Search = t; Rebuild(); };
        filters.AddChild(_search);
        _scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.AddChild(_scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _rows.AddThemeConstantOverride("separation", 2); _scroll.AddChild(_rows);
        _scroll.MouseEntered += () => _hover = true; _scroll.MouseExited += () => _hover = false;
        SetExpanded(_screen);
        Rebuild();
    }

    void SetSize(int s) { Settings.LogTextSize = Math.Clamp(s, 1, 3); Settings.Save(); Rebuild(); }

    static int FontSize => Accessibility.LogTextSize switch { 3 => 22, 2 => 17, _ => 13 };

    /// <summary>The small feed (the last lines, no controls) or the interactive panel.</summary>
    public void SetExpanded(bool on)
    {
        _expanded = on || _screen;
        _bar.Visible = _expanded;
        MouseFilter = _expanded ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        _scroll.MouseFilter = _expanded ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        _scroll.VerticalScrollMode = _expanded ? ScrollContainer.ScrollMode.Auto : ScrollContainer.ScrollMode.ShowNever;
        Layout();
        Rebuild();
    }

    public void Toggle() => SetExpanded(!_expanded);

    public void Close()
    {
        if (_screen) { Closed?.Invoke(); return; }
        SetExpanded(false);
        Closed?.Invoke();
    }

    /// <summary>Lays the panel out for 960x540 and larger: a feed along the bottom-left, or a panel that covers the left two thirds of the screen.</summary>
    public void Layout()
    {
        var vp = GetViewportRect().Size;
        if (vp.X < 200) vp = new Vector2(960, 540);
        if (_screen) { SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); return; }
        float w = _expanded ? Math.Min(vp.X * 0.66f, 640) : Math.Min(vp.X * 0.62f, 560);
        if (_expanded)
        {
            SetAnchorsPreset(LayoutPreset.TopLeft); GrowVertical = GrowDirection.End;
            Position = new Vector2(8, 8); Size = new Vector2(w, vp.Y - 16);
        }
        else
        {
            // the feed sits on the bottom edge and grows upward with its lines
            AnchorLeft = 0; AnchorRight = 0; AnchorTop = 1; AnchorBottom = 1; GrowVertical = GrowDirection.Begin;
            OffsetLeft = 8; OffsetRight = 8 + w; OffsetBottom = -8; OffsetTop = -8;
            CustomMinimumSize = new Vector2(w, 0); Size = new Vector2(w, 0);
        }
        var style = new StyleBoxFlat { BgColor = _expanded ? new Color(0.08f, 0.07f, 0.07f, 1f) : new Color(0, 0, 0, 0.5f), BorderColor = PixelTheme.Rim, ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 4, ContentMarginBottom = 4 };
        if (_expanded) { style.SetBorderWidthAll(2); }
        AddThemeStyleboxOverride("panel", style);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (!_expanded && IsInsideTree()) FitFeed();
        if (_t < 0.2) return;
        _t = 0;
        var log = _log();
        int serial = log.Lines.Count == 0 ? -1 : log.Lines[^1].Serial;
        if (serial != _builtSerial || log.Lines.Count != _builtFor) Rebuild();
    }

    /// <summary>Keeps the panel from letting go of the mouse to the combat inputs while it is open, and closes on Esc.</summary>
    public override void _Input(InputEvent e)
    {
        if (!_expanded || !Visible) return;
        if (e is InputEventKey { Pressed: true } && Actions.Pressed(e, "menu.back") && !_screen) { Close(); GetViewport().SetInputAsHandled(); }
        else if (e is InputEventMouse m && GetGlobalRect().HasPoint(m.Position)) { /* the Controls under it get the event; the fight view ignores it while the panel is open */ }
    }

    public bool Covers(Vector2 p) => _expanded && Visible && GetGlobalRect().HasPoint(p);

    Color Col(int sev) => FightFeedback.SeverityColour(sev);

    static string Mark(int sev) => sev switch { 0 => "[.]", 1 => "[o]", 2 => "[+]", 3 => "[#]", _ => "[X]" };

    string TextOf(FightLog log, int i) => FlavourFormatter.Format(log, i, _book, GameText.Instance, Accessibility.ReduceBloodEnabled);

    void Rebuild()
    {
        if (_rows == null) return;
        var log = _log();
        _builtFor = log.Lines.Count; _builtSerial = log.Lines.Count == 0 ? -1 : log.Lines[^1].Serial;
        foreach (var (f, cb) in _boxes) if (cb.ButtonPressed != _filter.Has(f)) cb.SetPressedNoSignal(_filter.Has(f));
        foreach (var c in _rows.GetChildren()) { _rows.RemoveChild(c); c.QueueFree(); }
        // the feed shows the newest lines of the filter; the panel the last 600
        var idx = _filter.Apply(log, _filter.Search.Length > 0 ? i => TextOf(log, i) : null);
        int show = _expanded ? 600 : Math.Max(3, 5 - Accessibility.LogTextSize / 2);
        if (idx.Count > show) idx = idx.GetRange(idx.Count - show, show);
        _count.Text = L.F("fl_ui_lines", idx.Count, log.Lines.Count);
        int fs = FontSize;
        if (idx.Count == 0)
            _rows.AddChild(new Label { Text = L.T(log.Lines.Count == 0 ? "fl_ui_log_empty" : "fl_ui_log_none_shown"), ThemeTypeVariation = "HintLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        foreach (int i in idx)
        {
            var l = log.Lines[i];
            string line = $"{FightLog.Clock(l.Tick - log.StartTick)} {Mark(Math.Max(0, (int)l.Severity))} {TextOf(log, i)}";
            var lab = new Label { Text = line, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = _expanded ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore };
            lab.AddThemeFontSizeOverride("font_size", fs);
            lab.AddThemeColorOverride("font_color", Col(Math.Max(0, (int)l.Severity)));
            lab.AddThemeColorOverride("font_outline_color", Colors.Black); lab.AddThemeConstantOverride("outline_size", _expanded ? 0 : 3);
            if (_expanded && l.Blow >= 0) { int serial = l.Serial; lab.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { if (!_open.Remove(serial)) _open.Add(serial); Rebuild(); } }; }
            _rows.AddChild(lab);
            if (_expanded && _open.Contains(l.Serial)) AddDetail(log, i, fs);
        }
        if (!_hover || _pinned == false) CallDeferred(nameof(ScrollEnd));
        CallDeferred(nameof(FitFeed));
    }

    /// <summary>The feed is as tall as its lines (the panel is as tall as the screen allows).</summary>
    void FitFeed()
    {
        if (!IsInstanceValid(_scroll)) return;
        if (_expanded) { _scroll.CustomMinimumSize = Vector2.Zero; return; }
        float h = _rows.GetCombinedMinimumSize().Y;
        _scroll.CustomMinimumSize = new Vector2(0, Math.Min(Math.Max(h, 18), 230));
    }

    void AddDetail(FightLog log, int i, int fs)
    {
        void Row(string t, Color c) { var r = new Label { Text = "    " + t, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill }; r.AddThemeFontSizeOverride("font_size", Math.Max(11, fs - 2)); r.AddThemeColorOverride("font_color", c); _rows.AddChild(r); }
        Row(L.T("fl_ui_trace_title") + ":", PixelTheme.Gold);
        foreach (var t in FlavourFormatter.TraceTable(log, i, GameText.Instance)) Row(t, PixelTheme.Body);
        var wounds = FlavourFormatter.WoundList(log, i, GameText.Instance);
        if (wounds.Count > 0) { Row(L.T("fl_ui_wounds_title") + ":", PixelTheme.Gold); foreach (var w in wounds) Row(w, PixelTheme.Body); }
    }

    void ScrollEnd() { if (IsInstanceValid(_scroll) && !(_hover && _expanded)) _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue; }
}
