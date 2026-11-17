using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;

namespace Ravensguard.Game;

/// <summary>Root node. Boots settings, input, the screen router and the main menu. UI/input plumbing only; no game state lives in nodes.</summary>
public partial class App : Node
{
    public ScreenRouter Router { get; private set; } = null!;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--check-keymap"))
        {
            // Defaults only: this is the CI gate, not the user's personal rebinding.
            var conflicts = KeyMap.CreateDefault().FindConflicts();
            foreach (var c in conflicts) GD.PrintErr("keymap conflict: " + c);
            GD.Print($"keymap: {InputDefs.All.Count} actions, {conflicts.Count} conflict(s)");
            GetTree().Quit(conflicts.Count > 0 ? 1 : 0);
            return;
        }

        Settings.Load();
        Settings.ApplyScale(GetWindow());
        InputBootstrap.Init();

        Router = new ScreenRouter { Name = "ScreenRouter" };
        AddChild(Router);
        Router.Push(new MainMenu(Router));
        if (OS.IsDebugBuild() || args.Contains("--dev")) AddChild(new DevConsole { Name = "DevConsole" });

        GD.Print("Falling Leaves: Ravensguard — shell booted");
        if (args.Contains("--smoke")) Callable.From(() => GetTree().Quit()).CallDeferred();
    }
}
