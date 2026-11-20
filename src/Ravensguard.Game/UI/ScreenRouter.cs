using System.Collections.Generic;
using Godot;
using Ravensguard.Game.Inputs;

namespace Ravensguard.Game.UI;

/// <summary>Marker for a screen that handles Esc ("menu.back" / its own back action) itself, e.g. the street view's pause menu. The router never pops such a screen on back.</summary>
public interface IHandlesBack { }

/// <summary>Stack of screens (Control scenes) plus an overlay layer for modals and toasts. Esc ("menu.back") pops, unless the top screen is <see cref="IHandlesBack"/>.</summary>
public partial class ScreenRouter : Control
{
    readonly List<Control> _stack = new();
    Control _screens = null!;
    Control _overlay = null!;
    VBoxContainer _toasts = null!;
    Control? _modal;
    System.Action? _modalClosed;

    public bool HasModal => _modal != null;
    public int Depth => _stack.Count;
    public Control? Top => _stack.Count > 0 ? _stack[^1] : null;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme = PixelTheme.Build();
        TextureFilter = TextureFilterEnum.Nearest;
        var backdrop = new ColorRect { Name = "Backdrop", Color = PixelTheme.Backdrop, MouseFilter = MouseFilterEnum.Ignore };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);
        _screens = new Control { Name = "Screens", MouseFilter = MouseFilterEnum.Ignore };
        _screens.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_screens);
        _overlay = new Control { Name = "Overlay", MouseFilter = MouseFilterEnum.Ignore };
        _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
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
        screen.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
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

    /// <summary>Pops every screen above the main menu, or (when the stack does not start with one, e.g. a dev street-view launch) replaces the stack with a fresh main menu.</summary>
    public void ReturnToMainMenu()
    {
        CloseModal();
        while (_stack.Count > 1) Pop();
        if (_stack.Count == 0 || _stack[0] is not MainMenu) Replace(new MainMenu(this));
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
    public void ShowModal(string title, string body, params (string label, System.Action? onPress)[] buttons) => ShowModal(title, body, null, buttons);

    /// <summary>Modal dialog that also tells <paramref name="onClosed"/> when it closes for any reason (a button or Esc), before the pressed button's own callback runs.</summary>
    public void ShowModal(string title, string body, System.Action? onClosed, params (string label, System.Action? onPress)[] buttons) => ShowModalCore(title, body, onClosed, false, buttons);

    /// <summary>Modal dialog for a long report: the body is wide and scrolls, so it reads at 960 x 540 however many lines it has.</summary>
    public void ShowReportModal(string title, string body, System.Action? onClosed, params (string label, System.Action? onPress)[] buttons) => ShowModalCore(title, body, onClosed, true, buttons);

    void ShowModalCore(string title, string body, System.Action? onClosed, bool scroll, (string label, System.Action? onPress)[] buttons)
    {
        CloseModal();
        _modalClosed = onClosed;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.AddChild(center);
        var panel = new PanelContainer();
        center.AddChild(panel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        panel.AddChild(v);
        v.AddChild(new Label { Text = title, ThemeTypeVariation = "TitleLabel" });
        var bodyLabel = new Label { Text = body, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(scroll ? 620 : 360, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (scroll)
        {
            var sc = new ScrollContainer { CustomMinimumSize = new Vector2(640, 280), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            sc.AddChild(bodyLabel);
            v.AddChild(sc);
        }
        else v.AddChild(bodyLabel);
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
        var cb = _modalClosed; _modalClosed = null;
        cb?.Invoke();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Actions.Pressed(e, "menu.back")) return;
        if (_modal != null) { CloseModal(); GetViewport().SetInputAsHandled(); }
        else if (_stack.Count > 1 && Top is not IHandlesBack) { Pop(); GetViewport().SetInputAsHandled(); }
    }

    /// <summary>Button with a right-aligned key hint such as "[N]". The hint follows rebinds.</summary>
    public static Button HintButton(string text, string actionId)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(320, 40), Alignment = HorizontalAlignment.Left, ClipText = true };
        var hint = new Label
        {
            Text = InputBootstrap.Hint(actionId),
            ThemeTypeVariation = "HintLabel",
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right,
            AnchorRight = 1, AnchorBottom = 1, OffsetRight = -8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.AddChild(hint);
        // the label text never runs under the hint: every state's stylebox keeps the hint's width (plus a gap) free on the right
        var orig = new System.Collections.Generic.Dictionary<string, StyleBox>();
        bool readied = false;
        void Pad()
        {
            if (!readied || !GodotObject.IsInstanceValid(b) || !b.IsInsideTree()) return; // the theme is only resolved once the button is ready
            if (b.Size.X > 0 && b.Size.X < 190) hint.AddThemeFontSizeOverride("font_size", 12); // a narrow button gets a smaller hint, so label and hint both fit
            else hint.RemoveThemeFontSizeOverride("font_size");
            float w = hint.GetThemeFont("font").GetStringSize(hint.Text, HorizontalAlignment.Left, -1, hint.GetThemeFontSize("font_size")).X + 12;
            foreach (var st in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
            {
                if (!orig.TryGetValue(st, out var sb)) { sb = b.GetThemeStylebox(st); if (sb == null) continue; orig[st] = sb; } // the theme's own boxes, read once
                var d = (StyleBox)sb.Duplicate();
                d.ContentMarginRight = Mathf.Max(sb.ContentMarginRight, w);
                b.AddThemeStyleboxOverride(st, d);
            }
        }
        b.Ready += () => { readied = true; Pad(); };
        b.Resized += Pad;
        System.Action refresh = () => { if (GodotObject.IsInstanceValid(hint)) { hint.Text = InputBootstrap.Hint(actionId); Pad(); } };
        InputBootstrap.Hints.Changed += refresh;
        b.TreeExiting += () => InputBootstrap.Hints.Changed -= refresh;
        return b;
    }
}
