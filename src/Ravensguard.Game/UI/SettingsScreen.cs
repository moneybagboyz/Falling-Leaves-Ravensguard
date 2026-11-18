using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;

namespace Ravensguard.Game.UI;

public partial class SettingsScreen : Control
{
    readonly ScreenRouter _router;
    readonly KeyMap _map = InputBootstrap.Map;
    readonly Dictionary<string, Button> _buttons = new();
    Label _warn = null!;
    InputAction? _capturing;
    Key _pendingModifier = Key.None;

    public SettingsScreen(ScreenRouter router) { _router = router; }

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var s in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + s, 48);
        AddChild(margin);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        margin.AddChild(v);
        v.AddChild(new Label { Text = L.T("SETTINGS_TITLE"), ThemeTypeVariation = "TitleLabel" });

        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        v.AddChild(tabs);
        var controls = BuildControlsTab();
        controls.Name = L.T("SETTINGS_TAB_CONTROLS");
        tabs.AddChild(controls);
        var display = BuildDisplayTab();
        display.Name = L.T("SETTINGS_TAB_DISPLAY");
        tabs.AddChild(display);
        var privacy = BuildPrivacyTab();
        privacy.Name = L.T("SETTINGS_TAB_PRIVACY");
        tabs.AddChild(privacy);

        var back = ScreenRouter.HintButton(L.T("UI_BACK"), "menu.back");
        back.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        back.Pressed += _router.Pop;
        v.AddChild(back);
    }

    Control BuildControlsTab()
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        var top = new HBoxContainer();
        root.AddChild(top);
        _warn = new Label { ThemeTypeVariation = "WarnLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill, Text = L.T("CONTROLS_HELP") };
        top.AddChild(_warn);
        var reset = new Button { Text = L.T("CONTROLS_RESET_ALL") };
        reset.Pressed += () => { _map.ResetAll(); Persist(); RefreshAll(); _warn.Text = L.T("CONTROLS_RESET_DONE"); };
        top.AddChild(reset);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);

        InputContext? last = null;
        foreach (var a in _map.Actions)
        {
            if (a.Context != last)
            {
                last = a.Context;
                list.AddChild(new Label { Text = L.T("CONTEXT_" + a.Context.ToString().ToUpperInvariant()), ThemeTypeVariation = "TitleLabel" });
            }
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = L.T(a.LocKey), SizeFlagsHorizontal = SizeFlags.ExpandFill });
            var b = new Button { CustomMinimumSize = new Vector2(240, 0) };
            var action = a;
            b.Pressed += () => BeginCapture(action);
            row.AddChild(b);
            _buttons[a.Id] = b;
            list.AddChild(row);
        }
        RefreshAll();
        return root;
    }

    string Describe(InputAction a)
    {
        var l = _map.Get(a.Id);
        return l.Count == 0 ? L.T("CONTROLS_UNBOUND") : string.Join(" / ", l.Select(b => b.Describe()));
    }

    void RefreshAll()
    {
        foreach (var a in _map.Actions) _buttons[a.Id].Text = Describe(a);
    }

    /// <summary>Saves and registers <paramref name="candidate"/> only if it has no conflicts; otherwise leaves everything untouched.</summary>
    bool TryCommit(KeyMap candidate)
    {
        if (candidate.FindConflicts().Count > 0) return false;
        foreach (var a in _map.Actions) _map.Set(a.Id, candidate.Get(a.Id));
        InputBootstrap.Save(_map);
        InputBootstrap.Register(_map); // also refreshes HintButton labels
        return true;
    }

    void Persist()
    {
        InputBootstrap.Save(_map);
        InputBootstrap.Register(_map);
    }

    void BeginCapture(InputAction a)
    {
        if (_capturing != null) _buttons[_capturing.Id].Text = Describe(_capturing);
        _capturing = a;
        _pendingModifier = Key.None;
        _buttons[a.Id].Text = L.T("CONTROLS_PRESS_KEY");
        _warn.Text = L.T("CONTROLS_CANCEL_HINT");
    }

    static bool IsModifier(Key k) => k is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta;

    static bool IsLetter(Key k) => k is >= Key.A and <= Key.Z;

    /// <summary>Only actions whose default is a lone modifier (sprint = Shift, nameplates = Alt) may be bound to one.</summary>
    static bool AllowsLoneModifier(InputAction a) => a.Defaults.Any(d => d.IsKeyboard && IsModifier((Key)d.Code));

    bool IsOverButton() => GetViewport().GuiGetHoveredControl() is BaseButton;

    public override void _Input(InputEvent e)
    {
        if (_capturing == null) return;
        Binding? nb = null;
        if (e is InputEventKey k)
        {
            if (k.Echo) return;
            if (k.Pressed)
            {
                if (k.Keycode == Key.Escape) { EndCapture(); GetViewport().SetInputAsHandled(); return; } // Esc is never bindable
                if (IsModifier(k.Keycode)) { _pendingModifier = k.Keycode; GetViewport().SetInputAsHandled(); return; }
                var m = (k.ShiftPressed ? Mods.Shift : 0) | (k.CtrlPressed ? Mods.Ctrl : 0) | (k.AltPressed ? Mods.Alt : 0);
                // Letters bind by physical position so WASD-style layouts survive AZERTY/QWERTZ.
                var phys = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
                nb = IsLetter(k.Keycode) || IsLetter(phys) ? Binding.P(IsLetter(phys) ? phys : k.Keycode, m) : Binding.K(k.Keycode, m);
            }
            else if (_pendingModifier == k.Keycode && _pendingModifier != Key.None)
            {
                // A modifier pressed and released alone is only meaningful for hold-modifier actions; Meta never.
                var mod = _pendingModifier;
                _pendingModifier = Key.None;
                if (mod == Key.Meta || !AllowsLoneModifier(_capturing)) { GetViewport().SetInputAsHandled(); return; }
                nb = Binding.K(mod);
            }
            else return;
        }
        else if (e is InputEventMouseButton mb && mb.Pressed)
        {
            // Clicks on buttons (other actions, Reset, Back) are UI, not bindings. Plain left-click elsewhere may be bound.
            if (mb.ButtonIndex == MouseButton.Left && IsOverButton()) return;
            if (IsOverButton() && mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown) return;
            var m = (mb.ShiftPressed ? Mods.Shift : 0) | (mb.CtrlPressed ? Mods.Ctrl : 0) | (mb.AltPressed ? Mods.Alt : 0);
            nb = Binding.M(mb.ButtonIndex, m);
        }
        if (nb is not { } binding) return;
        GetViewport().SetInputAsHandled();

        var action = _capturing;
        EndCapture();
        Apply(action, binding);
    }

    /// <summary>Applies a captured binding. Exact clashes offer a swap; shadowing clashes are rejected. A conflicting map is never persisted.</summary>
    void Apply(InputAction action, Binding binding)
    {
        var clashes = _map.FindClashes(action, binding);
        if (clashes.Count == 0)
        {
            var cand = _map.Clone();
            cand.Rebind(action.Id, binding);
            if (!TryCommit(cand)) { _warn.Text = L.F("CONTROLS_REJECTED", binding.Describe(), "?"); return; }
            RefreshAll();
            return;
        }

        var names = string.Join(", ", clashes.Select(c => L.T(c.LocKey)));
        bool swappable = clashes.All(c => _map.Get(c.Id).Contains(binding));
        if (!swappable)
        {
            _warn.Text = L.F("CONTROLS_REJECTED", binding.Describe(), names);
            return;
        }
        _router.ShowModal(L.T("CONTROLS_SWAP_TITLE"), L.F("CONTROLS_SWAP_BODY", binding.Describe(), names),
            (L.T("CONTROLS_SWAP"), () => Swap(action, binding, clashes)),
            (L.T("CONTROLS_KEEP"), () => _warn.Text = L.T("CONTROLS_HELP")));
    }

    void Swap(InputAction action, Binding binding, List<InputAction> clashes)
    {
        var cand = _map.Clone();
        var old = cand.Get(action.Id).FirstOrDefault(b => b.IsKeyboard == binding.IsKeyboard);
        bool hadOld = cand.Get(action.Id).Any(b => b.IsKeyboard == binding.IsKeyboard);
        cand.Rebind(action.Id, binding);
        foreach (var c in clashes)
        {
            cand.Unbind(c.Id, binding);
            if (hadOld) cand.Rebind(c.Id, old); // the clashing action takes over the key this one gave up
        }
        if (TryCommit(cand)) { RefreshAll(); _warn.Text = L.T("CONTROLS_HELP"); }
        else _warn.Text = L.F("CONTROLS_REJECTED", binding.Describe(), string.Join(", ", clashes.Select(c => L.T(c.LocKey))));
    }

    void EndCapture()
    {
        _capturing = null;
        _warn.Text = L.T("CONTROLS_HELP");
        RefreshAll();
    }

    Control BuildDisplayTab()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);

        var row = new HBoxContainer();
        row.AddChild(new Label { Text = L.T("DISPLAY_UI_SCALE"), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var opt = new OptionButton();
        opt.AddItem(L.T("DISPLAY_SCALE_AUTO"));
        for (int s = 1; s <= 3; s++) opt.AddItem($"{s}x");
        opt.Selected = Settings.UiScale;
        opt.ItemSelected += i => { Settings.UiScale = (int)i; Settings.ApplyScale(GetWindow()); Settings.Save(); };
        row.AddChild(opt);
        v.AddChild(row);

        v.AddChild(Toggle("DISPLAY_SCREEN_SHAKE", Settings.ScreenShake, on => Settings.ScreenShake = on));
        v.AddChild(Toggle("DISPLAY_FLASHES", Settings.Flashes, on => Settings.Flashes = on));
        v.AddChild(Toggle("DISPLAY_SLOW_MOTION", Settings.SlowMotion, on => Settings.SlowMotion = on));

        // contextual hints (onboarding spec 3.1, 6): off, minimal or full, auto by default (full for the first hour of the first character); the "seen" marks live in the profile and can be reset
        var hrow = new HBoxContainer();
        hrow.AddChild(new Label { Text = L.T("SETTINGS_HINTS"), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var hopt = new OptionButton();
        foreach (var k in new[] { "SETTINGS_HINTS_AUTO", "SETTINGS_HINTS_OFF", "SETTINGS_HINTS_MIN", "SETTINGS_HINTS_FULL" }) hopt.AddItem(L.T(k));
        hopt.Selected = (int)Settings.Hints.Setting;
        hopt.ItemSelected += i => { Settings.Hints.Setting = (Ravensguard.Sim.Onboarding.HintSetting)i; Settings.Save(); };
        hrow.AddChild(hopt);
        v.AddChild(hrow);
        var hreset = new Button { Text = L.T("SETTINGS_HINTS_RESET"), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        hreset.Pressed += () => { Settings.Hints.ResetSeen(); Settings.Save(); _router.ShowToast(L.T("SETTINGS_HINTS_RESET_DONE")); };
        v.AddChild(hreset);
        return v;
    }

    /// <summary>Privacy (testers spec 6): consent for bug-report files (off until agreed; nothing is ever sent), the log level and where the local files are.</summary>
    Control BuildPrivacyTab()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        var status = new Label { ThemeTypeVariation = "HintLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        var consent = new CheckBox { Text = L.T("PRIVACY_CONSENT"), ButtonPressed = Diagnostics.GameDiagnostics.Consent.IsGranted };
        consent.Toggled += on =>
        {
            if (on) Diagnostics.GameDiagnostics.Consent.Accept(); else Diagnostics.GameDiagnostics.Consent.Withdraw(deleteReports: false);
            Diagnostics.GameDiagnostics.Info("report", "consent " + (on ? "given" : "withdrawn"));
        };
        v.AddChild(consent);
        var debug = new CheckBox { Text = L.T("PRIVACY_DEBUG"), ButtonPressed = Diagnostics.GameDiagnostics.Log?.MinLevel == Ravensguard.Sim.Diagnostics.LogLevel.Debug };
        debug.Toggled += on => { if (Diagnostics.GameDiagnostics.Log is { } log) log.MinLevel = on ? Ravensguard.Sim.Diagnostics.LogLevel.Debug : Ravensguard.Sim.Diagnostics.LogLevel.Info; };
        v.AddChild(debug);
        var logs = new Button { Text = L.T("PRIVACY_LOGS"), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        logs.Pressed += () => OS.ShellOpen(ProjectSettings.GlobalizePath("user://logs"));
        v.AddChild(logs);
        var reports = new Button { Text = L.T("PRIVACY_REPORTS"), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        reports.Pressed += () => { var d = ProjectSettings.GlobalizePath("user://reports"); System.IO.Directory.CreateDirectory(d); OS.ShellOpen(d); };
        v.AddChild(reports);
        var del = new Button { Text = L.T("PRIVACY_DELETE"), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        del.Pressed += () => { Diagnostics.GameDiagnostics.Consent.Withdraw(); consent.SetPressedNoSignal(false); status.Text = L.T("PRIVACY_DELETED"); };
        v.AddChild(del);
        v.AddChild(status);
        return v;
    }

    static CheckBox Toggle(string key, bool value, System.Action<bool> set)
    {
        var c = new CheckBox { Text = L.T(key), ButtonPressed = value };
        c.Toggled += on => { set(on); Settings.Save(); };
        return c;
    }
}
