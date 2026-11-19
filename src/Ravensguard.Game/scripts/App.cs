using System;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.Debug;
using Ravensguard.Game.Diagnostics;
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
        int rp = System.Array.IndexOf(args, "--replay");
        if (rp >= 0 && rp + 1 < args.Length)
        {
            // dev command: replay a bug-report zip headlessly, stop at the crash tick or report the first diverging hash (testers spec 4.5)
            Settings.Load();
            GetTree().Quit(ReplayTool.Run(args[rp + 1], args));
            return;
        }
        int ap = System.Array.IndexOf(args, "--autoplay");
        if (ap >= 0)
        {
            // the first-hour bot (onboarding O-4): every origin through the mentor, the first job, the loan horse and the first ride, headlessly; --autoplay-seeds n (default 1), --autoplay-origin id
            GetTree().Quit(Onboarding.AutoplayTool.Run(args));
            return;
        }
        if (args.Contains("--selftest"))
        {
            GetTree().Quit(SelfTest.Run() ? 0 : 1);
            return;
        }
        bool smoke = args.Contains("--smoke");
        if (smoke) _capture = ErrorCapture.Install();

        if (args.Contains("--fight-demo"))
        {
            // the combat stance on its own (P4-C): a duel or a skirmish on the shipped fight; --shot-out/--shot-what for screenshots
            InputBootstrap.Init();
            AddChild(new World.Combat.FightDemo { Name = "FightDemo" });
            return;
        }
        int spike = System.Array.IndexOf(args, "--spike"); // e.g. `-- --spike melee`
        if (spike >= 0 && spike + 1 < args.Length && args[spike + 1] == "melee")
        {
            InputBootstrap.Init(); // the spike polls InputMap actions
            Callable.From(() => GetTree().ChangeSceneToFile("res://Spikes/MeleeSpike.tscn")).CallDeferred();
            return;
        }

        Settings.Load();
        // the hero desk says its texts as keys (the sim makes no English); they are resolved here, through the translation server
        Ravensguard.Sim.Trade.HeroText.Resolver = (key, a) => { var t = L.T(key); return t == key ? null : string.Format(t, a); };
        GameDiagnostics.Init(GetTree(), args);
        if (args.Contains("--crash-test"))
        {
            // dev: proves the crash handler end to end. An unhandled exception on a worker thread writes the report (a zip with `--crash-test-consent`), then the process ends as in a real crash.
            GameDiagnostics.ShowDialogs = false;
            if (args.Contains("--crash-test-consent")) GameDiagnostics.Consent.Accept();
            var t = new System.Threading.Thread(() => throw new InvalidOperationException("injected crash (--crash-test)")) { Name = "crash-test" };
            t.Start(); t.Join();
        }
        if (args.Contains("--crash-test-callback"))
        {
            // dev: the same end to end, for an exception thrown in a main-thread callback (a timer here). Godot catches it and logs it as a plain error; the bridge must still write the crash report. Exit 0 when it did.
            GameDiagnostics.ShowDialogs = false;
            _ = CrashCallbackProbe();
        }
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
        else if (OS.IsDebugBuild() && System.Array.IndexOf(args, "--bpedit-shot") >= 0) _ = Tools.BlueprintEditor.EditorShot.Run(GetTree(), Router, args);
        else if (System.Array.IndexOf(args, "--ng-shot") >= 0) _ = UI.NewGame.NewGameDebug.Shot(GetTree(), Router, args);
    }

    async Task CrashCallbackProbe()
    {
        GetTree().CreateTimer(0.2).Timeout += () => throw new InvalidOperationException("injected crash (--crash-test-callback)");
        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        bool ok = GameDiagnostics.LastCrashDialog is { } d && d.Length > 0;
        GD.Print(ok ? "crash-test-callback: ok (" + GameDiagnostics.LastCrashDialog!.Split('\n')[0] + ")" : "ERROR: crash-test-callback: the exception in the callback produced no crash report");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>The app is closing normally: the session log says so (a crash never gets here, so the next launch can offer a recovery report).</summary>
    public override void _ExitTree() => GameDiagnostics.CleanExit();

    public override void _Process(double delta) => System.Threading.Interlocked.Increment(ref GameDiagnostics.Frames);

    /// <summary>F8 works everywhere (menus, dialogues, Ironman): the picture is taken first, so the report window is not in it.</summary>
    public override void _Input(InputEvent e)
    {
        // "?" opens the controls overlay for the screen you are on (the street view knows its own context; trade and crafting have theirs)
        if (Router != null && Actions.Pressed(e, "world.controls_overlay") && Router.Top is not UI.ControlsOverlay && !BugReportScreen.IsOpen)
        {
            if (Router.Top is World.StreetView sv && !Router.HasModal) { sv.ShowControls(); GetViewport().SetInputAsHandled(); }
            else if (Router.Top is UI.TradeScreen) { UI.ControlsOverlay.Open(Router, "trade"); GetViewport().SetInputAsHandled(); }
            else if (Router.Top is UI.CraftingScreen) { UI.ControlsOverlay.Open(Router, "crafting"); GetViewport().SetInputAsHandled(); }
        }
        if (Router == null || !Actions.PressedAlways(e, "world.report_bug") || BugReportScreen.IsOpen) return;
        OpenBugReport();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Opens the F8 report screen (the screenshot is captured before it opens).</summary>
    public BugReportScreen? OpenBugReport() => BugReportScreen.Open(Router, GameDiagnostics.TryScreenshot());

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
            // the combat stance (P4-C): a duel on the shipped fight draws and steps for a few frames
            var fight = new World.Combat.FightDemo { Name = "SmokeFight" }; AddChild(fight); await Frames(6);
            fight.QueueFree(); await Frames(1);
            var f8 = new InputEventKey { Keycode = Key.F8, PhysicalKeycode = Key.F8, Pressed = true };
            _Input(f8); await Frames(2); // F8 opens the report window; opening it twice does nothing
            if (!BugReportScreen.IsOpen) throw new Exception("F8 did not open the bug report window");
            int depth = Router.Depth; _Input(f8); await Frames(1);
            if (Router.Depth != depth) throw new Exception("F8 opened a second report window");
            ((BugReportScreen)Router.Top!).Close(); await Frames(2);
            if (BugReportScreen.IsOpen) throw new Exception("the bug report window did not close");
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
