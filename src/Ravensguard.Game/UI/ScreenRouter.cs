using System.Collections.Generic;
using Godot;
using Ravensguard.Game.Inputs;

namespace Ravensguard.Game.UI;

/// <summary>Stack of screens (Control scenes) plus an overlay layer for modals and toasts. Esc ("menu.back") pops.</summary>
public partial class ScreenRouter : Control
{
    readonly List<Control> _stack = new();
    Control _screens = null!;
    Control _overlay = null!;
    VBoxContainer _toasts = null!;
    Control? _modal;

    public int Depth => _stack.Count;
    public Control? Top => _stack.Count > 0 ? _stack[^1] : null;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = PixelTheme.Build();
        TextureFilter = TextureFilterEnum.Nearest;
        _screens = new Control { Name = "Screens", MouseFilter = MouseFilterEnum.Ignore };
        _screens.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_screens);
        _overlay = new Control { Name = "Overlay", MouseFilter = MouseFilterEnum.Ignore };
        _overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_overlay);
        _toasts = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        _toasts.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide, LayoutPresetMode.Minsize, 24);
        _toasts.GrowVertical = GrowDirection.Begin;
        _overlay.AddChild(_toasts);
    }

    public void Push(Control screen)
    {
        if (Top is { } prev) SetActive(prev, false);
        _stack.Add(screen);
        screen.SetAnchorsPreset(LayoutPreset.FullRect);
        _screens.AddChild(screen);
        SetActive(screen, true);
    }

    public void Pop()
    {
        if (_stack.Count == 0) return;
        var top = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        top.QueueFree();
        if (Top is { } now) SetActive(now, true);
    }

    public void Replace(Control screen)
    {
        if (_stack.Count > 0)
        {
            var top = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            top.QueueFree();
        }
        Push(screen);
    }

    static void SetActive(Control c, bool active)
    {
        c.Visible = active;
        c.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
    }

    public void ShowToast(string text, float seconds = 2.5f)
    {
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        panel.AddChild(new Label { Text = text });
        _toasts.AddChild(panel);
        var tw = panel.CreateTween();
        tw.TweenInterval(seconds);
        tw.TweenProperty(panel, "modulate:a", 0.0, 0.4);
        tw.TweenCallback(Callable.From(panel.QueueFree));
    }

    /// <summary>Modal dialog with one button per entry. Esc closes it without choosing.</summary>
    public void ShowModal(string title, string body, params (string label, System.Action? onPress)[] buttons)
    {
        CloseModal();
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        dim.AddChild(center);
        var panel = new PanelContainer();
        center.AddChild(panel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        panel.AddChild(v);
        v.AddChild(new Label { Text = title, ThemeTypeVariation = "TitleLabel" });
        v.AddChild(new Label { Text = body, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0) });
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        v.AddChild(row);
        Button? first = null;
        foreach (var (label, act) in buttons)
        {
            var b = new Button { Text = label };
            var cb = act;
            b.Pressed += () => { CloseModal(); cb?.Invoke(); };
            row.AddChild(b);
            first ??= b;
        }
        _overlay.AddChild(dim);
        _modal = dim;
        first?.GrabFocus();
    }

    public void CloseModal()
    {
        _modal?.QueueFree();
        _modal = null;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("menu.back")) return;
        if (_modal != null) { CloseModal(); GetViewport().SetInputAsHandled(); }
        else if (_stack.Count > 1) { Pop(); GetViewport().SetInputAsHandled(); }
    }

    /// <summary>Button with a right-aligned key hint such as "[N]".</summary>
    public static Button HintButton(string text, string actionId)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(280, 0), Alignment = HorizontalAlignment.Left };
        b.AddChild(new Label
        {
            Text = InputBootstrap.Hint(actionId),
            ThemeTypeVariation = "HintLabel",
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right,
            AnchorRight = 1, AnchorBottom = 1, OffsetRight = -10,
        });
        return b;
    }
}
