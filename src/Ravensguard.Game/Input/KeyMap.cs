using System.Collections.Generic;
using System.Linq;

namespace Ravensguard.Game.Inputs;

public readonly record struct Conflict(Binding Binding, InputAction A, InputAction B)
{
    public override string ToString() => $"{A.Context}: '{A.Id}' and '{B.Id}' both use {Binding.Describe()}";
}

/// <summary>Pure model of the current bindings of every action. No engine state; Describe() aside.</summary>
public sealed class KeyMap
{
    readonly Dictionary<string, List<Binding>> _bindings = new();
    public IReadOnlyList<InputAction> Actions { get; }

    public KeyMap(IReadOnlyList<InputAction> actions)
    {
        Actions = actions;
        ResetAll();
    }

    public static KeyMap CreateDefault() => new(InputDefs.All);

    public IReadOnlyList<Binding> Get(string id) => _bindings.TryGetValue(id, out var l) ? l : new List<Binding>();

    public void Set(string id, IEnumerable<Binding> bindings) => _bindings[id] = bindings.Distinct().ToList();

    public void ResetAll()
    {
        foreach (var a in Actions) _bindings[a.Id] = a.Defaults.ToList();
    }

    public void ResetAction(InputAction a) => _bindings[a.Id] = a.Defaults.ToList();

    /// <summary>Replaces the action's binding of the same kind (key or mouse), or adds one.</summary>
    public void Rebind(string id, Binding nb)
    {
        var list = _bindings[id];
        var i = list.FindIndex(b => b.Kind == nb.Kind);
        if (i >= 0) list[i] = nb; else list.Add(nb);
    }

    static bool Overlaps(InputContext a, InputContext b) =>
        a == b || a == InputContext.Global || b == InputContext.Global;

    /// <summary>Same binding on two actions within one context, or within a context and Global.</summary>
    public List<Conflict> FindConflicts()
    {
        var result = new List<Conflict>();
        for (int i = 0; i < Actions.Count; i++)
            for (int j = i + 1; j < Actions.Count; j++)
            {
                var a = Actions[i]; var b = Actions[j];
                if (!Overlaps(a.Context, b.Context)) continue;
                foreach (var bind in Get(a.Id).Intersect(Get(b.Id))) result.Add(new Conflict(bind, a, b));
            }
        return result;
    }

    /// <summary>Other actions that would clash if <paramref name="action"/> used <paramref name="binding"/>.</summary>
    public List<InputAction> FindClashes(InputAction action, Binding binding) =>
        Actions.Where(o => o.Id != action.Id && Overlaps(o.Context, action.Context) && Get(o.Id).Contains(binding)).ToList();
}
