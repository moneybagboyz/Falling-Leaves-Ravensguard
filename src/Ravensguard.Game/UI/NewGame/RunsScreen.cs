using System.Collections.Generic;
using Godot;
using Ravensguard.Game.Inputs;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>Pick a saved run to carry on from: all worlds (main menu Load) or one world (world library). Newest first; Enter loads the selected one.</summary>
public partial class RunsScreen : Control
{
    readonly ScreenRouter _router;
    readonly string? _worldId;
    List<RunEntry> _runs = new();
    int _sel;
    VBoxContainer _rows = null!;
    Label _empty = null!;
    Button _load = null!;

    public RunsScreen(ScreenRouter router, string? worldId = null) { _router = router; _worldId = worldId; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 24);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);
        _runs = RunCatalog.List(NewGameServices.Library, _worldId);
        string title = _worldId != null && _runs.Count > 0 ? L.F("RUNS_TITLE_WORLD", _runs[0].WorldName) : L.T("RUNS_TITLE");
        root.AddChild(UiKit.Title(title));
        root.AddChild(UiKit.Hint(L.T("RUNS_HELP"), true));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_rows);
        root.AddChild(scroll);
        _empty = UiKit.Body(L.T("RUNS_EMPTY"));
        root.AddChild(_empty);
        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 8);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 180);
        back.Pressed += _router.Pop;
        _load = UiKit.Key(L.T("RUNS_LOAD"), "menu.accept", 260);
        _load.Pressed += LoadSelected;
        bottom.AddChild(back);
        bottom.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        bottom.AddChild(_load);
        root.AddChild(bottom);
        Rebuild();
    }

    void Rebuild()
    {
        foreach (var c in _rows.GetChildren()) c.QueueFree();
        _empty.Visible = _runs.Count == 0;
        for (int i = 0; i < _runs.Count; i++)
        {
            var r = _runs[i];
            int idx = i;
            var b = new Button { ToggleMode = true, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 54), ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            string world = _worldId == null ? r.WorldName + "   " : "";
            b.Text = L.F("RUNS_ROW", RunCatalog.Title(r), world + RunCatalog.GameDate(r), r.Saved.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            b.AddThemeStyleboxOverride("pressed", PixelTheme.PanelBox(new Color("4b3d31"), PixelTheme.Gold, 6));
            b.AddThemeColorOverride("font_pressed_color", PixelTheme.Gold);
            b.ButtonPressed = i == _sel;
            b.Disabled = !r.Readable;
            b.Pressed += () => { _sel = idx; Rebuild(); };
            _rows.AddChild(b);
        }
        _load.Disabled = Current is not { Readable: true };
    }

    RunEntry? Current => _sel >= 0 && _sel < _runs.Count ? _runs[_sel] : null;

    void LoadSelected() { if (Current is { Readable: true } r) RunLoadScreen.Open(_router, r); }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.Pressed(e, "menu.accept")) LoadSelected();
        else if (Actions.Pressed(e, "menu.down") && _sel < _runs.Count - 1) { _sel++; Rebuild(); }
        else if (Actions.Pressed(e, "menu.up") && _sel > 0) { _sel--; Rebuild(); }
        else return;
        GetViewport().SetInputAsHandled();
    }
}
