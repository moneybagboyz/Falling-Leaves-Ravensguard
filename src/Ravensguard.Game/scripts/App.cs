using Godot;

namespace Ravensguard.Game;

/// <summary>Root node. Boots the simulation and the screen router (filled in by the game-shell task).</summary>
public partial class App : Node
{
    public override void _Ready()
    {
        GD.Print("Falling Leaves: Ravensguard — shell booted");
        if (OS.GetCmdlineUserArgs().Contains("--smoke")) GetTree().Quit();
    }
}
