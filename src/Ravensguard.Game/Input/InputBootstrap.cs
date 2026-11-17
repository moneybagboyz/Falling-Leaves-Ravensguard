using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Ravensguard.Game.Inputs;

/// <summary>Holds the live KeyMap, persists it to user://input.cfg and mirrors it into Godot's InputMap.</summary>
public static class InputBootstrap
{
    public const string ConfigPath = "user://input.cfg";
    public static KeyMap Map { get; private set; } = KeyMap.CreateDefault();

    /// <summary>Problems found by the last Init (bad lines, conflicting bindings reset); shown to the player once by App.</summary>
    public static List<string> StartupNotices { get; } = new();

    public static void Init()
    {
        Map = KeyMap.CreateDefault();
        StartupNotices.Clear();
        StartupNotices.AddRange(Load(Map));
        StartupNotices.AddRange(ResolveConflicts(Map));
        Register(Map);
    }

    /// <summary>Loads user bindings into <paramref name="map"/>. Returns one message per unparsable entry (those actions keep their defaults).</summary>
    public static List<string> Load(KeyMap map, string path = ConfigPath)
    {
        var problems = new List<string>();
        var cfg = new ConfigFile();
        var err = cfg.Load(path);
        if (err == Error.FileNotFound || err == Error.CantOpen) return problems;
        if (err != Error.Ok) { problems.Add($"{path}: could not be read ({err}); defaults used"); return Report(problems); }
        foreach (var a in map.Actions)
        {
            if (!cfg.HasSectionKey(a.Context.ToString(), a.Name)) continue;
            var raw = cfg.GetValue(a.Context.ToString(), a.Name).AsString();
            var list = new List<Binding>();
            bool bad = false;
            foreach (var part in raw.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
            {
                if (Binding.TryParse(part, out var b)) list.Add(b);
                else { bad = true; problems.Add($"{a.Id}: unparsable binding '{part}'"); }
            }
            if (bad) continue; // keep defaults for this action rather than a half-read list
            map.Set(a.Id, list);
        }
        return Report(problems);
    }

    static List<string> Report(List<string> problems)
    {
        foreach (var p in problems) GD.PushWarning("input.cfg: " + p);
        return problems;
    }

    /// <summary>Resets every action that takes part in a conflict to its defaults (repeats until clean; falls back to a full reset). Returns notices.</summary>
    public static List<string> ResolveConflicts(KeyMap map)
    {
        var notices = new List<string>();
        for (int pass = 0; pass < 4; pass++)
        {
            var conflicts = map.FindConflicts();
            if (conflicts.Count == 0) return notices;
            foreach (var c in conflicts)
            {
                notices.Add("conflicting bindings: " + c);
                map.ResetAction(c.A);
                map.ResetAction(c.B);
            }
        }
        if (map.FindConflicts().Count > 0) { map.ResetAll(); notices.Add("conflicts persisted; all bindings reset"); }
        foreach (var n in notices) GD.PushWarning("input.cfg: " + n);
        return notices;
    }

    public static void Save(KeyMap map, string path = ConfigPath)
    {
        var cfg = new ConfigFile();
        foreach (var a in map.Actions)
            cfg.SetValue(a.Context.ToString(), a.Name, string.Join(",", map.Get(a.Id).Select(b => b.Serialize())));
        var err = cfg.Save(path);
        if (err != Error.Ok) GD.PushWarning($"Could not save {path}: {err}");
    }

    /// <summary>Creates one InputMap action per KeyMap action, named "&lt;context&gt;.&lt;name&gt;".</summary>
    public static void Register(KeyMap map)
    {
        foreach (var a in map.Actions)
        {
            if (InputMap.HasAction(a.Id)) InputMap.EraseAction(a.Id);
            InputMap.AddAction(a.Id);
            foreach (var b in map.Get(a.Id)) InputMap.ActionAddEvent(a.Id, ToEvent(b));
        }
        Hints.Raise();
    }

    public static InputEvent ToEvent(Binding b)
    {
        bool shift = b.Mods.HasFlag(Mods.Shift), ctrl = b.Mods.HasFlag(Mods.Ctrl), alt = b.Mods.HasFlag(Mods.Alt);
        return b.Kind switch
        {
            BindingKind.Key => new InputEventKey { Keycode = (Key)b.Code, ShiftPressed = shift, CtrlPressed = ctrl, AltPressed = alt },
            BindingKind.PhysicalKey => new InputEventKey { PhysicalKeycode = (Key)b.Code, ShiftPressed = shift, CtrlPressed = ctrl, AltPressed = alt },
            _ => new InputEventMouseButton { ButtonIndex = (MouseButton)b.Code, ShiftPressed = shift, CtrlPressed = ctrl, AltPressed = alt },
        };
    }

    /// <summary>Short key hint such as "[N]" for the first binding of a Menu/World action.</summary>
    public static string Hint(string actionId)
    {
        var l = Map.Get(actionId);
        return l.Count == 0 ? "" : "[" + l[0].Describe() + "]";
    }

    /// <summary>Raised after bindings change so on-screen hints can refresh.</summary>
    public static class Hints
    {
        public static event System.Action? Changed;
        public static void Raise() => Changed?.Invoke();
    }
}

/// <summary>Which stacked contexts are live (World is implicit). Build is entered from World and suppresses a few World actions.</summary>
public static class ActiveContexts
{
    static readonly HashSet<InputContext> _on = new();
    public static void Enter(InputContext c) => _on.Add(c);
    public static void Exit(InputContext c) => _on.Remove(c);
    public static bool IsOn(InputContext c) => _on.Contains(c);
    public static void Clear() => _on.Clear();

    public static bool Suppressed(string actionId)
    {
        var a = InputBootstrap.Map.Find(actionId);
        if (a == null) return false;
        foreach (var c in _on) if (ContextRules.Suppresses(c, a)) return true;
        return false;
    }
}

/// <summary>
/// The only way gameplay code should query actions. Godot's default action matching also fires when extra modifiers
/// are held (W on Ctrl+W, Left click on Shift+Left click), so everything here uses exact matching.
/// </summary>
public static class Actions
{
    /// <summary>Set while a text field or the dev console owns the keyboard; gameplay queries then return false.</summary>
    public static bool UiCaptured { get; set; }
    static readonly Dictionary<string, List<InputAction>> _shadowers = new();
    static Actions() { InputBootstrap.Hints.Changed += _shadowers.Clear; }

    /// <summary>Event-based press, exact modifiers. Use inside _Input/_UnhandledInput.</summary>
    public static bool Pressed(InputEvent e, string id) => !UiCaptured && !ActiveContexts.Suppressed(id) && e.IsActionPressed(id, false, true);
    public static bool Released(InputEvent e, string id) => !UiCaptured && !ActiveContexts.Suppressed(id) && e.IsActionReleased(id, true);
    /// <summary>Same as Pressed but ignores UiCaptured (for the console/inspector toggles themselves).</summary>
    public static bool PressedAlways(InputEvent e, string id) => e.IsActionPressed(id, false, true);

    /// <summary>Polled state, exact modifiers.</summary>
    public static bool Held(string id) => !UiCaptured && !ActiveContexts.Suppressed(id) && Input.IsActionPressed(id, true);

    /// <summary>
    /// Polled state for movement, camera and held-modifier actions (sprint, nameplates): tolerates extra held Shift/Alt
    /// (sprinting while walking) but yields when a stricter binding of another action is down (Ctrl+W dodges instead of walking).
    /// </summary>
    public static bool HeldLoose(string id)
    {
        if (UiCaptured || ActiveContexts.Suppressed(id) || !Input.IsActionPressed(id)) return false;
        if (!_shadowers.TryGetValue(id, out var list)) _shadowers[id] = list = InputBootstrap.Map.Shadowers(id);
        foreach (var s in list)
            if (!ActiveContexts.Suppressed(s.Id) && Input.IsActionPressed(s.Id, true)) return false;
        return true;
    }
}
