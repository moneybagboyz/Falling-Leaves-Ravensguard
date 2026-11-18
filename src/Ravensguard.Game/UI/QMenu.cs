using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Game.UI.NewGame;

namespace Ravensguard.Game.UI;

/// <summary>
/// The Q menu (travel spec 6, autopilot spec 6): the verbs the hero may do where they stand, as a short numbered list (1 to 9 or a click; Q or Esc closes). A verb is a label, whether it can be done now and a
/// line saying why not. Pure view: StreetView builds the list from what is near and what the hero has and runs the chosen action after the menu closes.
/// </summary>
public partial class QMenu : Control
{
    public sealed record Verb(string Label, Action Do, string? Why = null, bool Heading = false);

    readonly ScreenRouter _router;
    readonly List<Verb> _verbs;
    readonly string _title;
    readonly List<(Button B, Verb V)> _buttons = [];

    public QMenu(ScreenRouter router, string title, List<Verb> verbs) { _router = router; _title = title; _verbs = verbs; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 14);
        margin.AddThemeConstantOverride("margin_left", 380); margin.AddThemeConstantOverride("margin_right", 380);
        var root = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter }; root.AddThemeConstantOverride("separation", 4);
        margin.AddChild(root);
        root.AddChild(UiKit.Title(_title));
        int n = 0;
        foreach (var v in _verbs)
        {
            if (v.Heading) { root.AddChild(UiKit.Hint(v.Label)); continue; }
            n++;
            var b = new Button { Text = $"{n}. {v.Label}", Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 30), Disabled = v.Why != null, TooltipText = v.Why ?? "" };
            if (v.Why != null) b.Text += $"   [{v.Why}]";
            var vv = v; b.Pressed += () => Choose(vv);
            root.AddChild(b); _buttons.Add((b, v));
        }
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 200); back.Pressed += _router.Pop; root.AddChild(back);
    }

    void Choose(Verb v) { if (v.Why != null) return; _router.Pop(); v.Do(); }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        int i = k.Keycode is >= Key.Key1 and <= Key.Key9 ? (int)k.Keycode - (int)Key.Key1 : -1;
        if (i >= 0 && i < _buttons.Count) { Choose(_buttons[i].V); GetViewport().SetInputAsHandled(); }
        else if (k.Keycode == Key.Q) { _router.Pop(); GetViewport().SetInputAsHandled(); }
    }
}
