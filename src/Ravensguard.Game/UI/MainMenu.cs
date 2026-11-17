using Godot;

namespace Ravensguard.Game.UI;

public partial class MainMenu : Control
{
    readonly ScreenRouter _router;
    public MainMenu(ScreenRouter router) { _router = router; }

    public override void _Ready()
    {
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        v.CustomMinimumSize = new Vector2(320, 0);
        center.AddChild(v);
        v.AddChild(new Label { Text = L.T("MENU_TITLE"), ThemeTypeVariation = "TitleLabel", HorizontalAlignment = HorizontalAlignment.Center });
        v.AddChild(new Label { Text = L.T("MENU_SUBTITLE"), ThemeTypeVariation = "HintLabel", HorizontalAlignment = HorizontalAlignment.Center });
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });

        var newGame = ScreenRouter.HintButton(L.T("MENU_NEW_GAME"), "menu.new_game");
        newGame.Pressed += DoNewGame;
        var settings = ScreenRouter.HintButton(L.T("MENU_SETTINGS"), "menu.settings");
        settings.Pressed += DoSettings;
        var quit = ScreenRouter.HintButton(L.T("MENU_QUIT"), "menu.quit");
        quit.Pressed += () => GetTree().Quit();
        v.AddChild(newGame); v.AddChild(settings); v.AddChild(quit);
        newGame.GrabFocus();
    }

    void DoNewGame() => _router.Push(new World.StreetView(_router));
    void DoSettings() => _router.Push(new SettingsScreen(_router));

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (e.IsActionPressed("menu.new_game")) DoNewGame();
        else if (e.IsActionPressed("menu.settings")) DoSettings();
        else if (e.IsActionPressed("menu.quit")) GetTree().Quit();
        else return;
        GetViewport().SetInputAsHandled();
    }
}
