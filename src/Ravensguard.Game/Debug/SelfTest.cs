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

        RoomChecks(); // P5-A3

        CsvChecks();
        ChoiceChecks();
        CrashBridgeChecks();

        OnboardingChecks();

        GD.Print($"selftest: {(_fails == 0 ? "ok" : _fails + " failure(s)")}");
        return _fails == 0;
    }

    /// <summary>P5-A3: RoomDetector v2 on the real content: the two-room cottage has the same rooms as v1, the hearth room is a yard until roofed, and it houses two beds once furnished.</summary>
    static void RoomChecks()
    {
        try
        {
            var lib = Ravensguard.Game.World.StreetView.LoadLibrary();
            var g = lib.Grid("cottage_two_room");
            var w = lib.NewWorld();
            w.LoadBlueprint(g, 0, 0, roofed: false);
            Check(w.Rooms.Count == lib.Detector.Detect(g).Count, "RoomDetector v2 found a different number of rooms than v1");
            Check(w.Rooms.Any(r => r.Primary?.Id == "yard"), "an unroofed cottage should show a yard");
            w.RoofOver("roof_thatch");
            Check(w.Rooms.All(r => r.Primary?.Id != "yard") && w.Rooms.Any(r => r.Primary?.Id == "kitchen" && r.Indoor), "the roofed cottage should have an indoor hearth room");
        }
        catch (Exception ex) { Check(false, "room detector selftest threw: " + ex); }
    }

    /// <summary>Onboarding (spec O-8, O-10): the controls overlay reads the key map for every context, a rebinding shows in it, every key token a hint names is a real action and every "learn more" has a Codex page.</summary>
    static void OnboardingChecks()
    {
        var map = KeyMap.CreateDefault();
        var all = InputDefs.All.Select(a => a.Id).ToHashSet();
        foreach (var v in UI.ControlsOverlay.Views())
        {
            foreach (var id in v.Actions) Check(all.Contains(id), $"overlay '{v.Id}' names an action that does not exist: {id}");
            Check(v.Actions.Distinct().Count() == v.Actions.Length, $"overlay '{v.Id}' lists an action twice");
            if (v.Backing is { } ctx)
            {
                var want = InputDefs.All.Where(a => a.Context == ctx).Select(a => a.Id).ToHashSet();
                Check(want.IsSubsetOf(v.Actions), $"overlay '{v.Id}' misses actions of its context {ctx}: {string.Join(",", want.Except(v.Actions))}");
                if (v.Id != "mounted") Check(want.SetEquals(v.Actions), $"overlay '{v.Id}' is not exactly the actions of {ctx}");
            }
            var rows = UI.ControlsOverlay.Rows(v, map);
            Check(rows.Count == v.Actions.Length + v.Local.Length && rows.All(r => r.Name.Length > 0 && (r.Keys.Length > 0 || r.Mouse.Length > 0)), $"overlay '{v.Id}' has an empty row");
        }
        // a rebound key shows in the overlay (E rebound to F)
        var rb = KeyMap.CreateDefault(); rb.Set("world.act", new[] { Binding.K(Key.F) });
        var act = UI.ControlsOverlay.Rows(UI.ControlsOverlay.View("world"), rb).FirstOrDefault(r => r.Name == L.T("ACTION_WORLD_ACT"));
        Check(act.Keys == Binding.K(Key.F).Describe(), "a rebound key does not show in the controls overlay: " + act.Keys);
        // the four contexts the open list named
        foreach (var id in new[] { "travel", "camp", "mounted", "crafting" }) Check(UI.ControlsOverlay.Views().Any(v => v.Id == id && UI.ControlsOverlay.Rows(v, map).Count >= 4), $"overlay page '{id}' is missing or thin");
        // hint text: key tokens are real actions
        string csv = ReadRes("res://localization/en.csv"), hints = ReadRes("res://../../content/base/onboarding/hints.jsonc");
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(csv, @"\{@([a-z_.]+)\}")) Check(all.Contains(m.Groups[1].Value), "a text names the key of an unknown action: " + m.Groups[1].Value);
        var rules = UI.CodexRules.FromInput().Select(r => r.Id).ToHashSet();
        int n = 0;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(hints, "\"learn_more\": \"([a-z_.]+)\"")) { n++; Check(rules.Contains(m.Groups[1].Value), "a hint links to a Codex page that does not exist: " + m.Groups[1].Value); }
        Check(n == 40, $"expected 40 hints with a Codex link, found {n}");
    }

    /// <summary>The translation table is a CSV Godot reads with quotes: every row has exactly the two fields (key, text) and no key is twice. An unquoted comma in a text once made a row of three.</summary>
    static void CsvChecks()
    {
        string csv = ReadRes("res://localization/en.csv");
        var rows = ParseCsv(csv);
        Check(rows.Count > 100 && rows[0].SequenceEqual(["keys", "en"]), "en.csv does not start with the keys,en header");
        var bad = rows.Select((r, i) => (r, i)).Where(x => x.r.Count != 2).Select(x => $"row {x.i + 1} ({(x.r.Count > 0 ? x.r[0] : "")}) has {x.r.Count} fields").ToList();
        Check(bad.Count == 0, "en.csv rows with the wrong number of fields: " + string.Join("; ", bad.Take(5)));
        var dup = rows.Skip(1).GroupBy(r => r[0]).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Check(dup.Count == 0, "en.csv keys twice: " + string.Join(", ", dup.Take(5)));
        // the parser itself: quotes, doubled quotes, a comma and a newline in a field
        var t = ParseCsv("a,\"x, y\"\nb,\"say \"\"hi\"\"\"\n");
        Check(t.Count == 2 && t[0].SequenceEqual(["a", "x, y"]) && t[1].SequenceEqual(["b", "say \"hi\""]), "the CSV checker misreads quoted fields");
        // every key the code asks the player's text for with a fixed name exists (the new ones of the Phase 3 fixes)
        var keys = rows.Select(r => r[0]).ToHashSet();
        foreach (var k in new[] { "TALK_LEGEND", "TRADE_GOOD_N", "TRAVEL_CAMP_YOU", "TRAVEL_CLOCK_PAUSED", "SHELTER_TENT", "SURFACE_DEEPMUD", "REPORT_CAT_BUG", "CRASH_SAVED", "HUD_TICK_FAULT", "CONTEXT_CHOICE", "ACTION_CHOICE_PICK_9" })
            Check(keys.Contains(k), "en.csv lacks " + k);
    }

    /// <summary>RFC 4180: comma separated, a field in double quotes may hold commas, quotes (doubled) and line breaks.</summary>
    static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var f = new System.Text.StringBuilder(); bool q = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (q) { if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { f.Append('"'); i++; } else q = false; } else f.Append(c); continue; }
            if (c == '"') { q = true; any = true; }
            else if (c == ',') { row.Add(f.ToString()); f.Clear(); any = true; }
            else if (c == '\r') { }
            else if (c == '\n') { if (any || f.Length > 0) { row.Add(f.ToString()); rows.Add(row); } row = new List<string>(); f.Clear(); any = false; }
            else { f.Append(c); any = true; }
        }
        if (any || f.Length > 0) { row.Add(f.ToString()); rows.Add(row); }
        return rows;
    }

    /// <summary>The numbered-list keys are real, rebindable actions that stay out of the speed keys' way, and the controls overlay has a layout-independent key.</summary>
    static void ChoiceChecks()
    {
        var map = KeyMap.CreateDefault();
        for (int i = 1; i <= 9; i++) Check(map.Get("choice.pick_" + i).Count == 1 && map.Get("choice.pick_" + i)[0] == Binding.K(Key.Key0 + i), "choice.pick_" + i + " is not bound to its digit");
        Check(map.FindConflicts().Count == 0, "the choice digits clash with another action: " + string.Join("|", map.FindConflicts().Select(c => c.ToString())));
        // a list open on the street view takes the digits from the speed keys (and only then)
        Check(ContextRules.Suppresses(InputContext.Choice, map.Find("world.speed_1")!) && !ContextRules.Suppresses(InputContext.Choice, map.Find("world.pause")!), "Choice does not switch off exactly the speed digits");
        // rebinding a pick onto a live world key is reported
        var rb = KeyMap.CreateDefault(); rb.Set("choice.pick_1", new[] { Binding.K(Key.C) });
        Check(rb.FindConflicts().Any(c => c.A.Id == "choice.pick_1" || c.B.Id == "choice.pick_1"), "a pick rebound onto a world key is not reported");
        InputBootstrap.Register(map);
        var one = new InputEventKey { Keycode = Key.Key1, PhysicalKeycode = Key.Key1, Pressed = true };
        var three = new InputEventKey { Keycode = Key.Key3, PhysicalKeycode = Key.Key3, Pressed = true };
        Check(Actions.Pick(one) == 0 && Actions.Pick(three) == 2 && Actions.Pick(three, 2) == -1, "Actions.Pick does not map the digits to entries");
        // the controls overlay opens on a key that does not depend on the layout, too
        var overlay = map.Get("world.controls_overlay");
        Check(overlay.Contains(Binding.K(Key.F10)) && overlay.Contains(Binding.K(Key.Slash, Mods.Shift)), "the controls overlay is not on both ? and F10");
        Check(Actions.PressedAlways(new InputEventKey { Keycode = Key.F10, PhysicalKeycode = Key.F10, Pressed = true }, "world.controls_overlay"), "F10 does not fire the controls overlay");
        // trade quantity: Shift (x10) and Ctrl (all) with Left/Right match the plain action loosely (the exact query never fires with a modifier)
        var shiftRight = new InputEventKey { Keycode = Key.Right, PhysicalKeycode = Key.Right, Pressed = true, ShiftPressed = true };
        Check(!Actions.Pressed(shiftRight, "trade.more") && Actions.PressedLoose(shiftRight, "trade.more"), "Shift+Right does not reach trade.more");
        // a quantity step with Shift is ten steps: read from the event
        Check(shiftRight is InputEventWithModifiers { ShiftPressed: true }, "the event carries its modifiers");
    }

    /// <summary>A C# exception that escapes a main-thread callback arrives at the engine log as a plain error (type 0); the bridge recognises it by what it says and not by its type.</summary>
    static void CrashBridgeChecks()
    {
        Check(Diagnostics.EngineLogBridge.IsManagedException("void Godot.NativeInterop.ExceptionUtils.LogException(System.Exception)", "System.InvalidOperationException: boom\n   at X"), "a System.* exception is not recognised");
        Check(Diagnostics.EngineLogBridge.IsManagedException("Unhandled exception", "bad"), "'Unhandled exception' is not recognised");
        Check(Diagnostics.EngineLogBridge.IsManagedException("", "Ravensguard.Game.FooException: bad"), "an own exception type is not recognised");
        Check(!Diagnostics.EngineLogBridge.IsManagedException("Condition \"x\" is true.", "Parameter is null.") && !Diagnostics.EngineLogBridge.IsManagedException("", "the exception list is empty"), "an ordinary engine error is taken for a crash");
        // the per-kind guard: a worker fault does not use up the report of a later real crash
        Diagnostics.GameDiagnostics.ResetCrashGuard();
        using var _ = new GuardReset();
        Check(Diagnostics.GameDiagnostics.Claim(Diagnostics.GameDiagnostics.FatalKind.Worker) && !Diagnostics.GameDiagnostics.Claim(Diagnostics.GameDiagnostics.FatalKind.Worker), "a worker fault is not reported exactly once");
        Check(Diagnostics.GameDiagnostics.Claim(Diagnostics.GameDiagnostics.FatalKind.Crash) && !Diagnostics.GameDiagnostics.Claim(Diagnostics.GameDiagnostics.FatalKind.Crash), "a crash after a worker fault is not reported exactly once");
    }

    sealed class GuardReset : System.IDisposable { public void Dispose() => Diagnostics.GameDiagnostics.ResetCrashGuard(); }

    static string ReadRes(string path) { try { return System.IO.File.ReadAllText(ProjectSettings.GlobalizePath(path)); } catch (System.Exception e) { GD.PrintErr("ERROR: selftest FAILED: cannot read " + path + ": " + e.Message); _fails++; return ""; } }
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
