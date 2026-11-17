using System.Collections.Generic;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Debug;

namespace Ravensguard.Game.Debug;

/// <summary>Dev-only panel pinned to the right edge that shows any IInspectable as collapsible sections. Toggled by global.inspector (F12).</summary>
public partial class SimInspector : CanvasLayer
{
    public static SimInspector? Instance { get; private set; }
    const int Width = 320;

    Control _root = null!;
    Label _title = null!;
    VBoxContainer _body = null!;
    IInspectable? _target;
    readonly HashSet<string> _collapsed = new();

    public bool IsOpen => _root.Visible;

    public override void _Ready()
    {
        Instance = this;
        Layer = 99;
        var panel = new PanelContainer { Visible = false, Theme = PixelTheme.Build(), TextureFilter = Control.TextureFilterEnum.Nearest };
        _root = panel;
        panel.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        panel.OffsetLeft = -Width; panel.OffsetRight = 0;
        panel.GrowHorizontal = Control.GrowDirection.Begin;
        AddChild(panel);
        var v = new VBoxContainer();
        panel.AddChild(v);
        _title = new Label { Text = L.T("INSPECTOR_TITLE"), ThemeTypeVariation = "TitleLabel" };
        v.AddChild(_title);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.AddChild(scroll);
        _body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_body);
        Rebuild();
    }

    /// <summary>Show a target (and open the panel). Pass null to clear.</summary>
    public void Show(IInspectable? target) { _target = target; _collapsed.Clear(); Rebuild(); _root.Visible = true; }

    void Rebuild()
    {
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        _title.Text = _target?.InspectTitle ?? L.T("INSPECTOR_TITLE");
        if (_target == null) { _body.AddChild(new Label { Text = L.T("INSPECTOR_EMPTY"), ThemeTypeVariation = "HintLabel" }); return; }
        foreach (var s in _target.Inspect()) AddSection(s);
    }

    void AddSection(InspectSection s)
    {
        bool open = !_collapsed.Contains(s.Title);
        var header = new Button { Text = (open ? "- " : "+ ") + s.Title, Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None };
        header.Pressed += () => { if (!_collapsed.Remove(s.Title)) _collapsed.Add(s.Title); Rebuild(); };
        _body.AddChild(header);
        if (!open) return;
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        foreach (var r in s.Rows)
        {
            grid.AddChild(new Label { Text = r.Label, ThemeTypeVariation = "HintLabel" });
            grid.AddChild(new Label { Text = r.Value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.Arbitrary, CustomMinimumSize = new Vector2(120, 0) });
        }
        _body.AddChild(grid);
    }

    public override void _Input(InputEvent e)
    {
        if (!e.IsActionPressed("global.inspector")) return;
        _root.Visible = !_root.Visible;
        GetViewport().SetInputAsHandled();
    }
}

/// <summary>Sample target for eyeballing the panel (--dev --inspector-demo).</summary>
public sealed class DemoInspectable : IInspectable
{
    public string InspectTitle => "Person#42 Edda";
    public IEnumerable<InspectSection> Inspect()
    {
        yield return InspectSection.Of("Identity", ("name", "Edda Ashford"), ("age", "31"), ("home", "Settlement#3"));
        yield return InspectSection.Of("Needs", ("hunger", "0.62"), ("rest", "0.88"), ("warmth", "0.40"));
    }
}
