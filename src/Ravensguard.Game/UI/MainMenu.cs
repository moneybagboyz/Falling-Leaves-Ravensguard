using Godot;
using Ravensguard.Game.Inputs;

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
        var worlds = ScreenRouter.HintButton(L.T("MENU_WORLDS"), "menu.worlds");
        worlds.Pressed += DoWorlds;
        var legends = ScreenRouter.HintButton(L.T("MENU_LEGENDS"), "menu.legends");
        legends.Pressed += DoLegends;
        var settings = ScreenRouter.HintButton(L.T("MENU_SETTINGS"), "menu.settings");
        settings.Pressed += DoSettings;
        var quit = ScreenRouter.HintButton(L.T("MENU_QUIT"), "menu.quit");
        quit.Pressed += () => GetTree().Quit();
        v.AddChild(newGame); v.AddChild(worlds); v.AddChild(legends); v.AddChild(settings); v.AddChild(quit);
        if (OS.IsDebugBuild()) // dev tools exist in debug builds only
        {
            var dev = ScreenRouter.HintButton(L.T("MENU_DEV_BLUEPRINTS"), "menu.dev_blueprints");
            dev.Pressed += DoBlueprints;
            v.AddChild(dev);
            v.MoveChild(dev, quit.GetIndex());
        }
        newGame.GrabFocus();
    }

    void DoNewGame() => _router.Push(new NewGame.QuickStartScreen(_router));
    void DoWorlds() => _router.Push(new NewGame.WorldsScreen(_router, legendsMode: false));
    void DoLegends() => _router.Push(new NewGame.WorldsScreen(_router, legendsMode: true));
    void DoSettings() => _router.Push(new SettingsScreen(_router));
    void DoBlueprints() { var msg = Tools.BlueprintEditor.BlueprintEditorScreen.Launch(_router, null); if (!msg.StartsWith("blueprint editor opened")) _router.ShowToast(msg); }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.Pressed(e, "menu.new_game")) DoNewGame();
        else if (Actions.Pressed(e, "menu.worlds")) DoWorlds();
        else if (Actions.Pressed(e, "menu.legends")) DoLegends();
        else if (Actions.Pressed(e, "menu.settings")) DoSettings();
        else if (Actions.Pressed(e, "menu.quit")) GetTree().Quit();
        else if (OS.IsDebugBuild() && Actions.Pressed(e, "menu.dev_blueprints")) DoBlueprints();
        else return;
        GetViewport().SetInputAsHandled();
    }
}
