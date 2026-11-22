using System.Collections.Generic;
using System.Linq;

namespace Ravensguard.Game.Inputs;

public enum ConflictKind
{
    /// <summary>Two actions use exactly the same binding in contexts that are active together.</summary>
    Duplicate,
    /// <summary>A's binding is the same key with fewer modifiers than B's, so a non-exact match would fire A on B's input too.</summary>
    Shadow,
}

/// <summary>For Duplicate, <paramref name="Binding"/> is shared. For Shadow, A is the shadowed action (fewer modifiers), B the shadowing one; Binding is A's, Other is B's.</summary>
public readonly record struct Conflict(Binding Binding, InputAction A, InputAction B, ConflictKind Kind = ConflictKind.Duplicate, Binding? Other = null)
{
    public override string ToString() => Kind == ConflictKind.Duplicate
        ? $"{A.Context}/{B.Context}: '{A.Id}' and '{B.Id}' both use {Binding.Describe()}"
        : $"{A.Context}/{B.Context}: '{A.Id}' ({Binding.Describe()}) is shadowed by '{B.Id}' ({Other?.Describe()})";
}

/// <summary>Which contexts are live at the same time, and which World actions a stacked context switches off.</summary>
public static class ContextRules
{
    /// <summary>
    /// Global is always live. World and Build stack (Build and Choice are entered from World and only suppress the World actions
    /// listed in <see cref="BuildSuppressedWorld"/>). CombatStance and CalledShot are entered from World but replace it
    /// completely (they re-declare the world keys they keep), and Menu stands alone. Everything else is mutually exclusive.
    /// </summary>
    public static bool ActiveTogether(InputContext a, InputContext b)
    {
        if (a == b || a == InputContext.Global || b == InputContext.Global) return true;
        return (a, b) is (InputContext.World, InputContext.Build) or (InputContext.Build, InputContext.World) or (InputContext.World, InputContext.Mounted) or (InputContext.Mounted, InputContext.World)
            or (InputContext.World, InputContext.Choice) or (InputContext.Choice, InputContext.World);
    }

    /// <summary>
    /// World actions that stay on while Build is active (P5-C, overview 6: time, camera, level, nameplates, the overlay, save and load, the bug report); every other World action is off,
    /// so Build can take their keys (WASD pans, Z / X rotate, P, M, R, J, B, Enter, Esc, the clicks).
    /// </summary>
    public static readonly IReadOnlySet<string> BuildKeptWorld = new HashSet<string>
    {
        "pause", "speed_1", "speed_2", "speed_4", "speed_8", "camera_up", "camera_down", "camera_left", "camera_right", "zoom_in", "zoom_out", "camera_home",
        "level_up", "level_down", "nameplates", "controls_overlay", "quicksave", "quickload", "report_bug",
    };

    /// <summary>True when <paramref name="lower"/> is switched off while <paramref name="active"/> is on top.</summary>
    public static bool Suppresses(InputContext active, InputAction lower) =>
        (active == InputContext.Build && lower.Context == InputContext.World && !BuildKeptWorld.Contains(lower.Name))
        || (active == InputContext.Choice && lower.Context == InputContext.World && ChoiceSuppressedWorld.Contains(lower.Name));

    /// <summary>World actions that are off while a numbered list (road encounter, camp panel) is open: the choice digits take the speed digits' keys.</summary>
    public static readonly IReadOnlySet<string> ChoiceSuppressedWorld = new HashSet<string> { "speed_1", "speed_2", "speed_4", "speed_8" };

    /// <summary>True when a and b are both live at once with neither suppressing the other.</summary>
    public static bool Coexist(InputAction a, InputAction b) =>
        ActiveTogether(a.Context, b.Context) && !Suppresses(a.Context, b) && !Suppresses(b.Context, a);
}

/// <summary>Pure model of the current bindings of every action. No engine state; Describe() aside.</summary>
public sealed class KeyMap
{
    /// <summary>
    /// Shadowing pairs (shadowed id, shadowing id) that are deliberate. They are only safe because every consumer
    /// matches exactly (<see cref="Act"/>): W moves, Ctrl+W dodges.
    /// </summary>
    public static readonly IReadOnlySet<(string Shadowed, string Shadowing)> AllowedShadows = new HashSet<(string, string)>
    {
        ("combatstance.move_up", "combatstance.dodge_up"),
        ("combatstance.move_down", "combatstance.dodge_down"),
        ("combatstance.move_left", "combatstance.dodge_left"),
        ("combatstance.move_right", "combatstance.dodge_right"),
        // Phase 4: Shift+F6 / Shift+F7 are matched exactly beside F6 / F7
        ("world.watch_toggle", "world.stance_next"), ("world.combat_log", "world.last_report"),
        ("combatstance.watch_toggle", "combatstance.stance_next"), ("combatstance.combat_log", "combatstance.last_report"),
        // P5-C: build mode reads Ctrl+Z (undo) beside Z (rotate) and Ctrl+S (save as blueprint) beside S (pan down) exactly
        ("build.rotate_ccw", "build.undo"), ("build.pan_down", "build.save_blueprint"),
        // P7-BB-D: Shift+F1-F4 add to the selection beside F1-F4 (matched exactly)
        ("world.formation_1", "world.formation_add_1"), ("world.formation_2", "world.formation_add_2"), ("world.formation_3", "world.formation_add_3"), ("world.formation_4", "world.formation_add_4"),
        ("combatstance.formation_1", "combatstance.formation_add_1"), ("combatstance.formation_2", "combatstance.formation_add_2"), ("combatstance.formation_3", "combatstance.formation_add_3"), ("combatstance.formation_4", "combatstance.formation_add_4"),
        ("tactical.formation_1", "tactical.formation_add_1"), ("tactical.formation_2", "tactical.formation_add_2"), ("tactical.formation_3", "tactical.formation_add_3"), ("tactical.formation_4", "tactical.formation_add_4"),
    };

    readonly Dictionary<string, List<Binding>> _bindings = new();
    readonly Dictionary<string, InputAction> _byId;
    public IReadOnlyList<InputAction> Actions { get; }

    public KeyMap(IReadOnlyList<InputAction> actions)
    {
        Actions = actions;
        _byId = actions.ToDictionary(a => a.Id);
        ResetAll();
    }

    public static KeyMap CreateDefault() => new(InputDefs.All);

    public KeyMap Clone()
    {
        var c = new KeyMap(Actions);
        foreach (var a in Actions) c._bindings[a.Id] = _bindings[a.Id].ToList();
        return c;
    }

    public InputAction? Find(string id) => _byId.TryGetValue(id, out var a) ? a : null;

    public IReadOnlyList<Binding> Get(string id) => _bindings.TryGetValue(id, out var l) ? l : new List<Binding>();

    public void Set(string id, IEnumerable<Binding> bindings) => _bindings[id] = bindings.Select(b => b.Normalize()).Distinct().ToList();

    public void ResetAll()
    {
        foreach (var a in Actions) _bindings[a.Id] = a.Defaults.ToList();
    }

    public void ResetAction(InputAction a) => _bindings[a.Id] = a.Defaults.ToList();

    /// <summary>Replaces the action's binding of the same family (keyboard or mouse), or adds one.</summary>
    public void Rebind(string id, Binding nb)
    {
        nb = nb.Normalize();
        var list = _bindings[id];
        var i = list.FindIndex(b => b.IsKeyboard == nb.IsKeyboard);
        if (i >= 0) list[i] = nb; else list.Add(nb);
    }

    /// <summary>Removes <paramref name="b"/> from the action; used when swapping.</summary>
    public void Unbind(string id, Binding b) => _bindings[id].Remove(b);

    /// <summary>Actions that shadow <paramref name="id"/> (same key, strictly more modifiers, live together), allowed or not.</summary>
    public List<InputAction> Shadowers(string id)
    {
        var a = Find(id);
        var res = new List<InputAction>();
        if (a == null) return res;
        foreach (var o in Actions)
            if (o.Id != id && ContextRules.Coexist(a, o) && Get(id).Any(x => Get(o.Id).Any(y => x.StrictSubsetOf(y)))) res.Add(o);
        return res;
    }

    /// <summary>Duplicate bindings and unlisted shadowing between actions that are live at the same time.</summary>
    public List<Conflict> FindConflicts()
    {
        var result = new List<Conflict>();
        for (int i = 0; i < Actions.Count; i++)
            for (int j = i + 1; j < Actions.Count; j++)
            {
                var a = Actions[i]; var b = Actions[j];
                if (!ContextRules.Coexist(a, b)) continue;
                foreach (var x in Get(a.Id))
                    foreach (var y in Get(b.Id))
                    {
                        if (x == y) result.Add(new Conflict(x, a, b));
                        else if (x.StrictSubsetOf(y) && !AllowedShadows.Contains((a.Id, b.Id))) result.Add(new Conflict(x, a, b, ConflictKind.Shadow, y));
                        else if (y.StrictSubsetOf(x) && !AllowedShadows.Contains((b.Id, a.Id))) result.Add(new Conflict(y, b, a, ConflictKind.Shadow, x));
                    }
            }
        return result;
    }

    /// <summary>Other actions that would clash (duplicate or unlisted shadowing, either direction) if <paramref name="action"/> used <paramref name="binding"/>.</summary>
    public List<InputAction> FindClashes(InputAction action, Binding binding)
    {
        binding = binding.Normalize();
        var res = new List<InputAction>();
        foreach (var o in Actions)
        {
            if (o.Id == action.Id || !ContextRules.Coexist(action, o)) continue;
            foreach (var y in Get(o.Id))
            {
                bool hit = y == binding
                    || (binding.StrictSubsetOf(y) && !AllowedShadows.Contains((action.Id, o.Id)))
                    || (y.StrictSubsetOf(binding) && !AllowedShadows.Contains((o.Id, action.Id)));
                if (hit) { res.Add(o); break; }
            }
        }
        return res;
    }
}
