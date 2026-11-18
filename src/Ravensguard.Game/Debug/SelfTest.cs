using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;

namespace Ravensguard.Game.Debug;

/// <summary>Headless checks for the engine-dependent pieces (KeyMap, bindings, settings maths). Run with `-- --selftest`.</summary>
public static class SelfTest
{
    static int _fails;
    static void Check(bool ok, string what)
    {
        if (!ok) { _fails++; GD.PrintErr("ERROR: selftest FAILED: " + what); }
    }

    static InputAction A(InputContext c, string n, params Binding[] d) => new(c, n, d);

    public static bool Run()
    {
        _fails = 0;
        const InputContext W = InputContext.World, B = InputContext.Build, S = InputContext.CalledShot, C = InputContext.CombatStance;

        // defaults are clean
        Check(KeyMap.CreateDefault().FindConflicts().Count == 0, "default keymap has conflicts");

        // shadowing: W vs Ctrl+W in one context is reported unless allowed
        var shadow = new KeyMap(new[] { A(W, "a", Binding.K(Key.W)), A(W, "b", Binding.K(Key.W, Mods.Ctrl)) });
        var sc = shadow.FindConflicts();
        Check(sc.Count == 1 && sc[0].Kind == ConflictKind.Shadow && sc[0].A.Name == "a", "W / Ctrl+W shadow not reported");
        Check(shadow.FindClashes(shadow.Actions[0], Binding.K(Key.W, Mods.Ctrl)).Count == 1, "FindClashes misses shadow");
        // mouse shadow
        var ms = new KeyMap(new[] { A(W, "act", Binding.M(MouseButton.Left)), A(W, "sel", Binding.M(MouseButton.Left, Mods.Shift)) });
        Check(ms.FindConflicts().Count == 1, "LMB / Shift+LMB shadow not reported");
        // the allowed combat pairs stay quiet
        Check(KeyMap.CreateDefault().FindConflicts().All(c => c.Kind != ConflictKind.Shadow), "allowed shadows reported");
        // plain duplicate
        var dup = new KeyMap(new[] { A(W, "a", Binding.K(Key.E)), A(W, "b", Binding.K(Key.E)) });
        Check(dup.FindConflicts().Count == 1 && dup.FindConflicts()[0].Kind == ConflictKind.Duplicate, "duplicate not reported");

        // context matrix
        var ctx = new KeyMap(new[] { A(W, "sneak", Binding.K(Key.Z)), A(B, "rotate_ccw", Binding.K(Key.Z)), A(W, "speed_1", Binding.K(Key.Key1)), A(B, "x", Binding.K(Key.Key1)),
                                     A(W, "cam", Binding.K(Key.Up)), A(S, "part_prev", Binding.K(Key.Up)), A(C, "q", Binding.K(Key.Up)) });
        var cc = ctx.FindConflicts();
        Check(cc.Count == 1 && cc[0].B.Name == "x", "Build suppression / stacking wrong: " + string.Join("|", cc.Select(c => c.ToString())));
        Check(!ContextRules.ActiveTogether(S, W) && !ContextRules.ActiveTogether(C, W) && ContextRules.ActiveTogether(B, W), "ActiveTogether matrix");

        // binding serialization
        foreach (var b in new[] { Binding.K(Key.W), Binding.K(Key.F5), Binding.M(MouseButton.Left, Mods.Shift), Binding.P(Key.Quoteleft) })
            Check(Binding.TryParse(b.Serialize(), out var r) && r == b, "roundtrip " + b.Serialize());
        Check(!Binding.TryParse("garbage", out _) && !Binding.TryParse("K:1:99", out _) && !Binding.TryParse("Z:1:0", out _), "bad binding strings accepted");
        Check(Binding.TryParse("K:" + (int)Key.W + ":0", out var legacy) && legacy.Kind == BindingKind.PhysicalKey, "legacy letter key not normalised");

        // conflict resolution after load
        var bad = KeyMap.CreateDefault();
        bad.Set("world.move_up", new[] { Binding.K(Key.E) });
        var notices = InputBootstrap.ResolveConflicts(bad);
        Check(notices.Count > 0 && bad.FindConflicts().Count == 0 && bad.Get("world.move_up")[0] == Binding.K(Key.W), "ResolveConflicts did not reset");

        // unparsable lines are reported (config written to user://)
        const string path = "user://selftest_input.cfg";
        var cfg = new ConfigFile();
        cfg.SetValue("World", "move_up", "K:oops:0");
        cfg.SetValue("World", "move_down", "P:" + (int)Key.S + ":0");
        cfg.Save(path);
        var lm = KeyMap.CreateDefault();
        var problems = InputBootstrap.Load(lm, path);
        Check(problems.Count == 1 && lm.Get("world.move_up")[0] == Binding.K(Key.W), "Load did not report/keep default for bad line");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));

        // exact matching in the engine
        var map = KeyMap.CreateDefault();
        InputBootstrap.Register(map);
        var ctrlW = new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.W, Pressed = true, CtrlPressed = true };
        var plainW = new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.W, Pressed = true };
        Check(ctrlW.IsActionPressed("combatstance.move_up") , "engine baseline changed: non-exact should match Ctrl+W");
        Check(!ctrlW.IsActionPressed("combatstance.move_up", false, true), "exact match still fires move_up on Ctrl+W");
        Check(ctrlW.IsActionPressed("combatstance.dodge_up", false, true) && plainW.IsActionPressed("combatstance.move_up", false, true), "exact matches missing");
        var shiftClick = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, ShiftPressed = true };
        Check(!shiftClick.IsActionPressed("world.act", false, true), "exact match fires world.act on Shift+LMB");

        // ui scale
        Check(Settings.AutoScale(1280, 720) == 1 && Settings.AutoScale(1920, 1080) == 2 && Settings.AutoScale(3840, 2160) == 3
            && Settings.AutoScale(1920, 540) == 1 && Settings.AutoScale(800, 2000) == 1 && Settings.AutoScale(7680, 4320) == 3, "AutoScale");

        // codex key hints are tokens resolved at display time, so a rebind shows
        Check(UI.CodexRules.ResolveKeys("{@world.map}") != "{@world.map}" && UI.CodexRules.ResolveKeys("plain") == "plain", "CodexRules.ResolveKeys");
        Check(UI.CodexRules.FromInput().All(r => !r.Text.Contains("[")), "codex rule text holds rendered key hints instead of tokens");

        // saved runs: Continue picks the newest save, a loaded run never saves its manual slot over an autosave or the Ironman file, and the keys exist
        var t0 = new System.DateTime(2026, 1, 1, 12, 0, 0, System.DateTimeKind.Utc);
        UI.NewGame.RunEntry E(string w, string slot, int min) => new(w, w, slot, slot, t0.AddMinutes(min), "Aldric", false, slot.StartsWith("autosave_"), 0, true);
        var ordered = UI.NewGame.RunCatalog.Newest([E("a", "run-1", 0), E("b", "autosave_1", 30), E("a", "run-2", 30), E("a", "autosave_2", 10)]);
        Check(ordered.Select(e => e.WorldId + "/" + e.Slot).SequenceEqual(["a/run-2", "b/autosave_1", "a/autosave_2", "a/run-1"]), "RunCatalog.Newest order: " + string.Join(",", ordered.Select(e => e.WorldId + "/" + e.Slot)));
        Check(UI.NewGame.RunCatalog.ManualNameFor("run-3", "Aldric Vane", 5) == "run-3" && UI.NewGame.RunCatalog.ManualNameFor("autosave_2", "Aldric Vane", 5) == "aldric-vane-6"
            && UI.NewGame.RunCatalog.ManualNameFor("ironman", null, 0) == "run-1", "RunCatalog.ManualNameFor");
        var keys = KeyMap.CreateDefault();
        Check(keys.Get("menu.continue").Count > 0 && keys.Get("menu.load").Count > 0 && keys.Get("world.quicksave").Count > 0, "continue / load / quicksave have default keys");

        // F8 (report a bug): one binding, no clash anywhere (World actions are live in every screen), exact matching, and consent is off until given
        var f8 = keys.Get("world.report_bug");
        Check(f8.Count == 1 && f8[0] == Binding.K(Key.F8), "world.report_bug is not F8");
        Check(keys.Actions.Count(a => a.Id != "world.report_bug" && keys.Get(a.Id).Contains(Binding.K(Key.F8))) == 0, "another action is bound to F8");
        Check(new KeyEventProbe().F8Fires(), "F8 key event does not fire world.report_bug");
        var probeConsent = new Ravensguard.Sim.Diagnostics.ConsentStore(ProjectSettings.GlobalizePath("user://selftest_profile"));
        Check(!probeConsent.IsGranted, "report consent is on by default");

        Tools.BlueprintEditor.EditorSelfTest.Run(Check);

        GD.Print($"selftest: {(_fails == 0 ? "ok" : _fails + " failure(s)")}");
        return _fails == 0;
    }
}

/// <summary>Sends a synthetic F8 press through the same exact-match query the game uses.</summary>
file sealed class KeyEventProbe
{
    public bool F8Fires()
    {
        var map = KeyMap.CreateDefault();
        InputBootstrap.Register(map);
        var e = new InputEventKey { Keycode = Key.F8, PhysicalKeycode = Key.F8, Pressed = true };
        return Actions.PressedAlways(e, "world.report_bug");
    }
}
