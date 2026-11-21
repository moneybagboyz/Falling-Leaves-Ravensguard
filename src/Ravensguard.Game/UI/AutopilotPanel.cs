using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Game.UI;

/// <summary>What the panel needs from the street view: the places a directive may name, a good for a trade route, and the verbs (start, stop, pause or resume, preset).</summary>
public sealed class AutopilotPanelHost
{
    public Func<AutopilotView> View = () => AutopilotView.None;
    public Func<AutopilotSetup> Setup = () => new AutopilotSetup();
    public Func<List<(string Name, long X, long Y, int Settlement)>> Places = () => [];
    public Func<string, ushort> GoodId = _ => 0;
    public Func<IReadOnlyList<Skill>> Skills = () => [];
    public Action<AutopilotSetup> Start = _ => { };
    public Action Stop = () => { };
    public Action PauseResume = () => { };
    public Action<string, AutopilotSetup> Preset = (_, _) => { };
    public Func<int> MaxRiskLow = () => 200, MaxRiskMedium = () => 500, MaxRiskHigh = () => 800;
}

/// <summary>
/// The autopilot panel (Y; autopilot spec 6): left the queue of directives (add, remove, reorder), middle the selected directive and what it ends on, right the limits and the interrupt table (a row per kind with
/// Pause, Notify or AI, and "Reset to defaults"), presets along the bottom (Cautious, Worker, Watch the world), the warning of what the AI will not do, and the state of the run with its last log lines. The panel edits a
/// draft; Start (or Update) hands it to the autopilot at the next barrier. No state lives here beyond the draft.
/// </summary>
public partial class AutopilotPanel : Control
{
    readonly ScreenRouter _router;
    readonly AutopilotPanelHost _h;
    AutopilotSetup _draft;
    int _sel;
    VBoxContainer _queueBox = null!, _midBox = null!, _limitBox = null!, _tableBox = null!, _statusBox = null!;
    Label _warn = null!;
    double _refresh;
    public string? ShotPath { get; init; }

    public AutopilotPanel(ScreenRouter router, AutopilotPanelHost host) { _router = router; _h = host; _draft = host.Setup(); }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 12);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 6); margin.AddChild(root);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 12);
        head.AddChild(UiKit.Title(L.T("AP_TITLE")));
        head.AddChild(new Label { Text = L.T("AP_SUB"), ThemeTypeVariation = "HintLabel", SizeFlagsVertical = SizeFlags.ShrinkCenter });
        root.AddChild(head);

        // everything but the button bar scrolls: at 960x540 the three columns, the warning and the state do not fit one screen, and Back and Start must never be below the fold
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiKit.Scroll(body));
        var cols = new HBoxContainer(); cols.AddThemeConstantOverride("separation", 8); body.AddChild(cols);
        _queueBox = new VBoxContainer(); _queueBox.AddThemeConstantOverride("separation", 3);
        _midBox = new VBoxContainer(); _midBox.AddThemeConstantOverride("separation", 4);
        _limitBox = new VBoxContainer(); _limitBox.AddThemeConstantOverride("separation", 3);
        _tableBox = new VBoxContainer(); _tableBox.AddThemeConstantOverride("separation", 1);
        cols.AddChild(Column(L.T("AP_QUEUE"), _queueBox, 240, 4));
        cols.AddChild(Column(L.T("AP_DIRECTIVE"), _midBox, 200, 3));
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 4 }; right.AddThemeConstantOverride("separation", 6);
        right.CustomMinimumSize = new Vector2(240, 0);
        right.AddChild(UiKit.Hint(L.T("AP_LIMITS"))); right.AddChild(UiKit.Panel(_limitBox));
        right.AddChild(UiKit.Hint(L.T("AP_INTERRUPTS")));
        _tableBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        right.AddChild(UiKit.Panel(_tableBox));
        cols.AddChild(right);

        _warn = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, ThemeTypeVariation = "HintLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill }; body.AddChild(_warn);
        _statusBox = new VBoxContainer(); _statusBox.AddThemeConstantOverride("separation", 0); body.AddChild(UiKit.Panel(_statusBox));

        var bottom = new HBoxContainer(); bottom.AddThemeConstantOverride("separation", 6); root.AddChild(bottom);
        foreach (var (id, key) in new[] { ("cautious", "AP_PRESET_CAUTIOUS"), ("worker", "AP_PRESET_WORKER"), ("watch", "AP_PRESET_WATCH") })
        {
            var pb = new Button { Text = L.T(key), CustomMinimumSize = new Vector2(100, 30) };
            var capture = id; pb.Pressed += () => { _h.Preset(capture, _draft); BuildAll(); }; bottom.AddChild(pb);
        }
        bottom.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var start = new Button { Text = L.T("AP_START"), CustomMinimumSize = new Vector2(110, 30) }; start.Pressed += () => { _h.Start(_draft); _router.ShowToast(L.T("AP_STARTED")); BuildStatus(); }; bottom.AddChild(start);
        var pause = new Button { Text = L.T("AP_PAUSE"), CustomMinimumSize = new Vector2(110, 30) }; pause.Pressed += () => { _h.PauseResume(); }; bottom.AddChild(pause);
        var stop = new Button { Text = L.T("AP_STOP"), CustomMinimumSize = new Vector2(90, 30) }; stop.Pressed += () => { _h.Stop(); }; bottom.AddChild(stop);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 120, 30); back.Pressed += _router.Pop; bottom.AddChild(back);
        BuildAll();
    }

    static Control Column(string title, Control body, int minW, float ratio)
    {
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(minW, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = ratio }; v.AddThemeConstantOverride("separation", 4);
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill; v.AddChild(UiKit.Hint(title)); v.AddChild(UiKit.Panel(body, minW - 16, 0));
        return v;
    }

    void BuildAll() { BuildQueue(); BuildMid(); BuildLimits(); BuildTable(); BuildStatus(); }

    static string Money(long m) => UiKit.Money(m);

    // ---- the queue

    void BuildQueue()
    {
        Clear(_queueBox);
        var q = _draft.Queue;
        if (q.Count == 0) _queueBox.AddChild(UiKit.Body(L.T("AP_QUEUE_EMPTY")));
        for (int i = 0; i < q.Count; i++)
        {
            int idx = i; var d = q[i];
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 3);
            var pick = new Button { Text = $"{i + 1}. {Describe(d)}", Alignment = HorizontalAlignment.Left, SizeFlagsHorizontal = SizeFlags.ExpandFill, ToggleMode = true, ButtonPressed = idx == _sel, ClipText = true, CustomMinimumSize = new Vector2(110, 26) };
            pick.Pressed += () => { _sel = idx; BuildQueue(); BuildMid(); };
            row.AddChild(pick);
            var up = new Button { Text = "^", CustomMinimumSize = new Vector2(26, 26), Disabled = i == 0 }; up.Pressed += () => { (q[idx - 1], q[idx]) = (q[idx], q[idx - 1]); _sel = idx - 1; BuildQueue(); BuildMid(); }; row.AddChild(up);
            var dn = new Button { Text = "v", CustomMinimumSize = new Vector2(26, 26), Disabled = i == q.Count - 1 }; dn.Pressed += () => { (q[idx + 1], q[idx]) = (q[idx], q[idx + 1]); _sel = idx + 1; BuildQueue(); BuildMid(); }; row.AddChild(dn);
            var rm = new Button { Text = "x", CustomMinimumSize = new Vector2(26, 26) }; rm.Pressed += () => { q.RemoveAt(idx); _sel = Math.Clamp(_sel, 0, Math.Max(0, q.Count - 1)); BuildQueue(); BuildMid(); }; row.AddChild(rm);
            _queueBox.AddChild(row);
        }
        _queueBox.AddChild(UiKit.Hint(L.T("AP_ADD")));
        var add = new HFlowContainer(); add.AddThemeConstantOverride("h_separation", 3); add.AddThemeConstantOverride("v_separation", 3);
        void Add(string key, Func<Directive> make) { var b = new Button { Text = L.T(key), CustomMinimumSize = new Vector2(0, 26) }; b.Pressed += () => { if (q.Count < 8) { q.Add(make()); _sel = q.Count - 1; BuildQueue(); BuildMid(); } }; add.AddChild(b); }
        var places = _h.Places();
        Add("AP_ADD_WORK", () => Directive.Work(0, L.T("AP_D_WORK")));
        Add("AP_ADD_GUARD", () => { var d = Directive.Guard(places.Count > 0 ? places[0].Settlement : 0); d.Name = L.T("AP_D_GUARD"); return d; });
        Add("AP_ADD_TRAIN", () => { var s = _h.Skills(); var d = Directive.Train(s.Count > 0 ? s[0] : Skill.Smithing, 30); d.Name = L.F("AP_D_TRAIN", d.Name.Replace("Train ", "")); return d; });
        Add("AP_ADD_REST", () => { var d = Directive.Rest(); d.Name = L.T("AP_D_REST"); return d; });
        Add("AP_ADD_TRAVEL", () => { var p = places.FirstOrDefault(); var d = Directive.TravelTo(p.X, p.Y, p.Name ?? ""); d.Name = L.F("AP_D_TRAVEL", p.Name ?? "?"); return d; });
        Add("AP_ADD_TRADE", () => { var d = Directive.TradeRoute(places.Count > 0 ? places[0].Settlement : 0, places.Count > 1 ? places[1].Settlement : 0, _h.GoodId("wheat"), 20_000, 100); d.Name = L.T("AP_D_TRADE"); return d; });
        _queueBox.AddChild(add);
    }

    string Describe(Directive d) => d.Name.Length > 0 ? d.Name : d.Kind.ToString();

    // ---- the selected directive

    void BuildMid()
    {
        Clear(_midBox);
        if (_sel < 0 || _sel >= _draft.Queue.Count) { _midBox.AddChild(UiKit.Body(L.T("AP_PICK"))); return; }
        var d = _draft.Queue[_sel]; var places = _h.Places();
        _midBox.AddChild(UiKit.Body(L.T("AP_K_" + d.Kind.ToString().ToUpperInvariant())));
        switch (d.Kind)
        {
            case DirectiveKind.Travel:
                _midBox.AddChild(UiKit.Hint(L.T("AP_TO")));
                _midBox.AddChild(PlaceOption(places, () => places.FindIndex(p => p.X == d.Params[0] && p.Y == d.Params[1]), i => { d.Params[0] = places[i].X; d.Params[1] = places[i].Y; d.Name = L.F("AP_D_TRAVEL", places[i].Name); BuildQueue(); }));
                break;
            case DirectiveKind.Guard:
                _midBox.AddChild(UiKit.Hint(L.T("AP_AT")));
                _midBox.AddChild(PlaceOption(places, () => places.FindIndex(p => p.Settlement == (int)d.Target), i => { d.Target = places[i].Settlement; d.TargetKind = 1; }));
                break;
            case DirectiveKind.TradeRoute:
                _midBox.AddChild(UiKit.Hint(L.T("AP_FROM"))); _midBox.AddChild(PlaceOption(places, () => places.FindIndex(p => p.Settlement == (int)d.Target), i => { d.Target = places[i].Settlement; }));
                _midBox.AddChild(UiKit.Hint(L.T("AP_TO"))); _midBox.AddChild(PlaceOption(places, () => places.FindIndex(p => p.Settlement == (int)d.Params[0]), i => { d.Params[0] = places[i].Settlement; }));
                _midBox.AddChild(UiKit.Stepper(L.T("AP_MARGIN"), () => (int)d.Params[3] / 10, v => d.Params[3] = v * 10, 0, 100, 1, v => v + " %"));
                break;
            case DirectiveKind.Train:
                var skills = _h.Skills();
                if (skills.Count > 0)
                {
                    var o = new OptionButton(); foreach (var s in skills) o.AddItem(s.ToString());
                    o.Selected = Math.Max(0, skills.ToList().FindIndex(s => (long)s == d.Target));
                    o.ItemSelected += i => { d.Target = (long)skills[(int)i]; d.UntilValue2 = d.Target; d.Name = L.F("AP_D_TRAIN", skills[(int)i].ToString()); BuildQueue(); };
                    _midBox.AddChild(o);
                }
                _midBox.AddChild(UiKit.Stepper(L.T("AP_LEVEL"), () => (int)d.UntilValue, v => { d.UntilValue = v; d.Params[0] = v; }, 5, 100, 5));
                break;
        }
        if (d.Kind is DirectiveKind.Work or DirectiveKind.Guard or DirectiveKind.Rest)
        {
            _midBox.AddChild(UiKit.Hint(L.T("AP_UNTIL")));
            _midBox.AddChild(UiKit.Stepper(L.T("AP_DAYS"), () => d.Until == UntilKind.Days ? (int)d.UntilValue : 0,
                v => { d.UntilValue = v; d.Until = v > 0 ? UntilKind.Days : d.Kind == DirectiveKind.Rest ? UntilKind.Healed : UntilKind.Forever; }, 0, 60, 1, v => v == 0 ? L.T("AP_FOREVER") : v + ""));
        }
    }

    static OptionButton PlaceOption(List<(string Name, long X, long Y, int Settlement)> places, Func<int> get, Action<int> set)
    {
        var o = new OptionButton(); foreach (var p in places) o.AddItem(p.Name);
        if (places.Count > 0) o.Selected = Math.Max(0, get());
        o.ItemSelected += i => set((int)i);
        return o;
    }

    // ---- limits

    void BuildLimits()
    {
        Clear(_limitBox);
        var l = _draft.Limits;
        _limitBox.AddChild(UiKit.Stepper(L.T("AP_CAP"), () => (int)(l.SpendCapMitesPerDay < 0 ? 0 : l.SpendCapMitesPerDay / 100), v => l.SpendCapMitesPerDay = v == 0 ? -1 : v * 100L, 0, 50, 1, v => v == 0 ? L.T("AP_NO_CAP") : L.F("AP_CROWNS_DAY", v * 100L)));
        _limitBox.AddChild(UiKit.Stepper(L.T("AP_RESERVE"), () => (int)(l.PurseReserveMites / 100), v => l.PurseReserveMites = v * 100L, 0, 100, 1, v => L.F("AP_CROWNS", v * 100L)));
        var risk = new HBoxContainer(); risk.AddThemeConstantOverride("separation", 4);
        risk.AddChild(new Label { Text = L.T("AP_RISK"), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var ro = new OptionButton(); ro.AddItem(L.T("AP_RISK_LOW")); ro.AddItem(L.T("AP_RISK_MED")); ro.AddItem(L.T("AP_RISK_HIGH"));
        ro.Selected = l.MaxRiskPm <= _h.MaxRiskLow() ? 0 : l.MaxRiskPm <= _h.MaxRiskMedium() ? 1 : 2;
        ro.ItemSelected += i => { l.MaxRiskPm = i == 0 ? _h.MaxRiskLow() : i == 1 ? _h.MaxRiskMedium() : _h.MaxRiskHigh(); BuildWarn(); };
        risk.AddChild(ro); _limitBox.AddChild(risk);
        var crimes = new CheckBox { Text = L.T("AP_NOCRIMES"), ButtonPressed = l.NoCrimes }; crimes.Toggled += on => { l.NoCrimes = on; BuildWarn(); }; _limitBox.AddChild(crimes);
        var home = new CheckBox { Text = L.T("AP_HOME"), ButtonPressed = l.HomeByNightfall }; home.Toggled += on => { l.HomeByNightfall = on; if (on && l.HomeX == 0 && l.HomeY == 0) { var p = _h.Places(); if (p.Count > 0) { l.HomeX = p[0].X; l.HomeY = p[0].Y; } } BuildWarn(); }; _limitBox.AddChild(home);
    }

    // ---- the interrupt table

    static readonly string[] PolicyNames = ["AP_POL_PAUSE", "AP_POL_NOTIFY", "AP_POL_AI"];

    void BuildTable()
    {
        Clear(_tableBox);
        var reset = new Button { Text = L.T("AP_RESET"), CustomMinimumSize = new Vector2(0, 26) }; reset.Pressed += () => { _draft.Policy = AutopilotState.DefaultPolicies(); _draft.Quiet = new bool[InterruptBus.KindCount]; BuildTable(); };
        _tableBox.AddChild(reset);
        for (int i = 0; i < InterruptBus.KindCount; i++)
        {
            int k = i; var kind = (InterruptKind)i;
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4);
            string intKey = (int)kind >= (int)InterruptKind.WatchDanger ? "wm_int_" + Ravensguard.Sim.Combat.Flavour.FlavourBook.Snake(kind.ToString()) : "AP_INT_" + kind.ToString().ToUpperInvariant();   // P4-F: the watch kinds' rows
            row.AddChild(new Label { Text = L.T(intKey), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, CustomMinimumSize = new Vector2(100, 0) });
            var o = new OptionButton { CustomMinimumSize = new Vector2(96, 24) };
            foreach (var n in PolicyNames) o.AddItem(L.T(n));
            o.Selected = (int)_draft.Policy[i];
            o.ItemSelected += s => { _draft.Policy[k] = (InterruptPolicy)(int)s; _draft.Quiet[k] = false; };
            row.AddChild(o); _tableBox.AddChild(row);
        }
    }

    // ---- warning and state

    void BuildWarn()
    {
        var v = _h.View();
        _warn.Text = Warn(_draft);
    }

    static string Warn(AutopilotSetup s)
    {
        var l = s.Limits; var parts = new List<string>();
        if (l.SpendCapMitesPerDay >= 0) parts.Add(L.F("AP_W_SPEND", Money(l.SpendCapMitesPerDay)));
        if (l.PurseReserveMites > 0) parts.Add(L.F("AP_W_RESERVE", Money(l.PurseReserveMites)));
        if (l.NoCrimes) parts.Add(L.T("AP_W_CRIME"));
        parts.Add(L.T(l.MaxRiskPm <= 200 ? "AP_W_RISK_LOW" : l.MaxRiskPm <= 500 ? "AP_W_RISK_MED" : "AP_W_RISK_HIGH"));
        if (l.HomeByNightfall) parts.Add(L.T("AP_W_DARK"));
        parts.Add(L.T("AP_W_BIG"));
        return L.F("AP_W_LINE", string.Join(", ", parts));
    }

    void BuildStatus()
    {
        BuildWarn();
        Clear(_statusBox);
        var v = _h.View();
        if (!v.Exists) v = v with { CapPerDay = _draft.Limits.SpendCapMitesPerDay };
        string state = !v.Exists || !v.Enabled ? L.T("AP_STATE_OFF") : v.Paused ? L.T("AP_STATE_PAUSED") : v.AlertPaused ? L.F("AP_STATE_ALERT", v.AlertText) : L.F("AP_STATE_ON", v.Active.Length > 0 ? v.Active : L.T("AP_NEIGHBOUR"));
        _statusBox.AddChild(new Label { Text = L.F("AP_STATE", state, Money(v.SpentToday), v.CapPerDay >= 0 ? Money(v.CapPerDay) : L.T("AP_NO_CAP"), Money(v.Coin)) });
        foreach (var line in v.LogTail.TakeLast(4)) _statusBox.AddChild(new Label { Text = line, ThemeTypeVariation = "HintLabel", ClipText = true });
    }

    static void Clear(Control c) { foreach (var ch in c.GetChildren()) { c.RemoveChild(ch); ch.QueueFree(); } }

    int _shotWait;
    public override void _Process(double delta)
    {
        _refresh += delta; if (_refresh > 1.0) { _refresh = 0; BuildStatus(); }
        if (ShotPath == null || !Visible || ++_shotWait != 12) return;
        var img = GetViewport().GetTexture().GetImage();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
        GD.Print($"[autopilot] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
        GetTree().Quit();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.Pressed(e, "world.autopilot")) _router.Pop(); else return;
        GetViewport().SetInputAsHandled();
    }
}
