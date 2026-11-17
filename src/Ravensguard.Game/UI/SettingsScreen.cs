using System.Collections.Generic;
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
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
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
        return l.Count == 0 ? L.T("CONTROLS_UNBOUND") : string.Join(" / ", System.Linq.Enumerable.Select(l, b => b.Describe()));
    }

    void RefreshAll()
    {
        foreach (var a in _map.Actions) _buttons[a.Id].Text = Describe(a);
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

    public override void _Input(InputEvent e)
    {
        if (_capturing == null) return;
        Binding? nb = null;
        if (e is InputEventKey k)
        {
            if (k.Echo) return;
            if (k.Pressed)
            {
                if (k.Keycode == Key.Escape) { EndCapture(); GetViewport().SetInputAsHandled(); return; }
                if (IsModifier(k.Keycode)) { _pendingModifier = k.Keycode; GetViewport().SetInputAsHandled(); return; }
                var m = (k.ShiftPressed ? Mods.Shift : 0) | (k.CtrlPressed ? Mods.Ctrl : 0) | (k.AltPressed ? Mods.Alt : 0);
                nb = Binding.K(k.Keycode, m);
            }
            else if (_pendingModifier == k.Keycode && _pendingModifier != Key.None)
                nb = Binding.K(k.Keycode); // a modifier pressed and released alone (e.g. Shift to sprint)
            else return;
        }
        else if (e is InputEventMouseButton mb && mb.Pressed)
        {
            var m = (mb.ShiftPressed ? Mods.Shift : 0) | (mb.CtrlPressed ? Mods.Ctrl : 0) | (mb.AltPressed ? Mods.Alt : 0);
            nb = Binding.M(mb.ButtonIndex, m);
        }
        if (nb is not { } binding) return;
        GetViewport().SetInputAsHandled();

        var action = _capturing;
        var clashes = _map.FindClashes(action, binding);
        _map.Rebind(action.Id, binding);
        Persist();
        EndCapture();
        if (clashes.Count > 0)
            _warn.Text = L.F("CONTROLS_CONFLICT", binding.Describe(), string.Join(", ", System.Linq.Enumerable.Select(clashes, c => L.T(c.LocKey))));
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
        for (int s = 1; s <= 3; s++) opt.AddItem($"{s}x");
        opt.Selected = Settings.UiScale - 1;
        opt.ItemSelected += i => { Settings.UiScale = (int)i + 1; Settings.ApplyScale(GetWindow()); Settings.Save(); };
        row.AddChild(opt);
        v.AddChild(row);

        v.AddChild(Toggle("DISPLAY_SCREEN_SHAKE", Settings.ScreenShake, on => Settings.ScreenShake = on));
        v.AddChild(Toggle("DISPLAY_FLASHES", Settings.Flashes, on => Settings.Flashes = on));
        v.AddChild(Toggle("DISPLAY_SLOW_MOTION", Settings.SlowMotion, on => Settings.SlowMotion = on));
        return v;
    }

    static CheckBox Toggle(string key, bool value, System.Action<bool> set)
    {
        var c = new CheckBox { Text = L.T(key), ButtonPressed = value };
        c.Toggled += on => { set(on); Settings.Save(); };
        return c;
    }
}
