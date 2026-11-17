using System;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.Debug;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;

namespace Ravensguard.Game;

/// <summary>Root node. Boots settings, input, the screen router and the main menu. UI/input plumbing only; no game state lives in nodes.</summary>
public partial class App : Node
{
    public ScreenRouter Router { get; private set; } = null!;

    ErrorCapture? _capture;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--check-keymap"))
        {
            // Defaults only: this is the CI gate, not the user's personal rebinding. Reports duplicates AND unlisted shadowing.
            var conflicts = KeyMap.CreateDefault().FindConflicts();
            foreach (var c in conflicts) GD.PrintErr("ERROR: keymap conflict: " + c);
            GD.Print($"keymap: {InputDefs.All.Count} actions, {conflicts.Count} conflict(s)");
            GetTree().Quit(conflicts.Count > 0 ? 1 : 0);
            return;
        }
        if (args.Contains("--selftest"))
        {
            GetTree().Quit(SelfTest.Run() ? 0 : 1);
            return;
        }
        bool smoke = args.Contains("--smoke");
        if (smoke) _capture = ErrorCapture.Install();

        int spike = System.Array.IndexOf(args, "--spike"); // e.g. `-- --spike melee`
        if (spike >= 0 && spike + 1 < args.Length && args[spike + 1] == "melee")
        {
            InputBootstrap.Init(); // the spike polls InputMap actions
            Callable.From(() => GetTree().ChangeSceneToFile("res://Spikes/MeleeSpike.tscn")).CallDeferred();
            return;
        }

        Settings.Load();
        var win = GetWindow();
        Settings.ApplyScale(win);
        win.SizeChanged += () => Settings.ApplyScale(win);
        InputBootstrap.Init();

        UI.NewGame.NewGameServices.Configure(args);
        Router = new ScreenRouter { Name = "ScreenRouter" };
        AddChild(Router);
        Router.Push(new MainMenu(Router));
        if (InputBootstrap.StartupNotices.Count > 0)
        {
            GD.PushWarning("input: " + string.Join("; ", InputBootstrap.StartupNotices));
            Router.ShowToast(L.T("CONTROLS_STARTUP_RESET"), 6f);
        }
        if (World.StreetOptions.Parse(args) is { } street) Router.Replace(new World.StreetView(Router, street)); // --shot-street <hour> --shot-out <png>, --street-bench <s>
        // Dev console/inspector exist in debug builds only; --dev is ignored in release exports.
        if (OS.IsDebugBuild())
        {
            var inspector = new Debug.SimInspector { Name = "SimInspector" };
            AddChild(inspector);
            if (args.Contains("--inspector-demo")) inspector.Show(new Debug.DemoInspectable());
            AddChild(new DevConsole { Name = "DevConsole" });
        }

        GD.Print("Falling Leaves: Ravensguard — shell booted");
        if (smoke) RunSmoke();
        else if (System.Array.IndexOf(args, "--ng-shot") >= 0) _ = UI.NewGame.NewGameDebug.Shot(GetTree(), Router, args);
    }

    async void RunSmoke()
    {
        int code = 0;
        try
        {
            async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            await Frames(1); // MainMenu (pushed in _Ready)
            Router.Push(new SettingsScreen(Router)); await Frames(1);
            Router.Pop(); await Frames(1);
            Router.Push(new World.StreetView(Router)); await Frames(2);
            Router.Pop(); await Frames(1);
            await UI.NewGame.NewGameDebug.Smoke(GetTree(), Router);
        }
        catch (Exception ex)
        {
            GD.PrintErr("ERROR: smoke exception: " + ex);
            code = 1;
        }
        if (_capture != null && !_capture.Errors.IsEmpty)
        {
            foreach (var e in _capture.Errors) GD.PrintErr("ERROR: smoke captured: " + e);
            code = 1;
        }
        GD.Print(code == 0 ? "smoke: ok" : "smoke: FAILED");
        // Free every screen before quitting, so text and font resources are released and the exit is clean.
        foreach (var child in GetChildren()) child.QueueFree();
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Quit(code);
    }
}
