using System.Collections.Generic;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>The world library: list, open legends, start a new game in a world, delete (with an in-page confirmation, no modal).</summary>
public partial class WorldsScreen : Control
{
    readonly ScreenRouter _router;
    readonly bool _legendsMode;
    List<WorldInfo> _worlds = new();
    int _sel;
    string? _confirming;
    VBoxContainer _rows = null!;
    Label _empty = null!;
    Button _legends = null!, _play = null!, _delete = null!;

    public WorldsScreen(ScreenRouter router, bool legendsMode) { _router = router; _legendsMode = legendsMode; }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 24);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);
        root.AddChild(UiKit.Title(L.T(_legendsMode ? "WL_TITLE_LEGENDS" : "WL_TITLE")));
        root.AddChild(UiKit.Hint(L.T("WL_HELP"), true));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_rows);
        root.AddChild(scroll);
        _empty = UiKit.Body(L.T("WL_EMPTY"));
        root.AddChild(_empty);

        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 8);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 180);
        back.Pressed += _router.Pop;
        _delete = UiKit.Key(L.T("WL_DELETE"), "menu.delete", 220);
        _delete.Pressed += AskDelete;
        _legends = UiKit.Key(L.T("WL_LEGENDS"), "menu.legends", 240);
        _legends.Pressed += OpenLegends;
        _play = UiKit.Key(L.T("WL_PLAY"), "menu.accept", 260);
        _play.Pressed += Play;
        bottom.AddChild(back); bottom.AddChild(_delete);
        bottom.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        bottom.AddChild(_legends);
        if (!_legendsMode) bottom.AddChild(_play);
        root.AddChild(bottom);
        Reload();
    }

    void Reload()
    {
        _worlds = NewGameServices.Library.List();
        _sel = Mathf.Clamp(_sel, 0, System.Math.Max(0, _worlds.Count - 1));
        Rebuild();
    }

    void Rebuild()
    {
        foreach (var c in _rows.GetChildren()) c.QueueFree();
        _empty.Visible = _worlds.Count == 0;
        for (int i = 0; i < _worlds.Count; i++)
        {
            var w = _worlds[i];
            int idx = i;
            var s = w.File.Summary;
            if (_confirming == w.Id)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);
                var msg = new Label { Text = L.F("WL_CONFIRM", w.Name, w.Runs.Count), ThemeTypeVariation = "WarnLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
                var yes = new Button { Text = L.T("WL_CONFIRM_YES"), CustomMinimumSize = new Vector2(140, 36) };
                var no = new Button { Text = L.T("WL_CONFIRM_NO"), CustomMinimumSize = new Vector2(140, 36) };
                yes.Pressed += () => { NewGameServices.Library.Delete(w.Id); NewGameServices.Forget(w.Id); _confirming = null; Reload(); };
                no.Pressed += () => { _confirming = null; Rebuild(); };
                row.AddChild(msg); row.AddChild(yes); row.AddChild(no);
                _rows.AddChild(UiKit.Panel(row));
                no.CallDeferred(Control.MethodName.GrabFocus);
                continue;
            }
            var b = new Button { ToggleMode = true, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 54), ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            b.Text = $"{w.Name}\n{L.F("WL_ROW", w.Seed, s.Years, s.RealmsAlive, s.Settlements, s.Ruins, w.Runs.Count)}   {w.File.Created}";
            b.AddThemeStyleboxOverride("pressed", PixelTheme.PanelBox(new Color("4b3d31"), PixelTheme.Gold, 6));
            b.AddThemeColorOverride("font_pressed_color", PixelTheme.Gold);
            b.ButtonPressed = i == _sel;
            b.Pressed += () => { _sel = idx; Rebuild(); };
            b.FocusEntered += () => { if (_sel != idx) { _sel = idx; foreach (var ch in _rows.GetChildren()) if (ch is Button o && o != b) o.SetPressedNoSignal(false); b.SetPressedNoSignal(true); } };
            _rows.AddChild(b);
        }
        bool any = _worlds.Count > 0;
        _legends.Disabled = !any; _play.Disabled = !any; _delete.Disabled = !any;
    }

    WorldInfo? Current => _sel >= 0 && _sel < _worlds.Count ? _worlds[_sel] : null;

    void AskDelete() { if (Current is { } w) { _confirming = w.Id; Rebuild(); } }
    void OpenLegends() { if (Current is { } w) NewGameFlow.OpenLegends(_router, w); }
    void Play() { if (Current is { } w) _router.Push(new QuickStartScreen(_router, w.Id)); }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (_confirming != null) return;
        if (Actions.Pressed(e, "menu.delete")) AskDelete();
        else if (Actions.Pressed(e, "menu.legends")) OpenLegends();
        else if (Actions.Pressed(e, "menu.accept") && !_legendsMode) Play();
        else if (Actions.Pressed(e, "menu.accept")) OpenLegends();
        else return;
        GetViewport().SetInputAsHandled();
    }
}
