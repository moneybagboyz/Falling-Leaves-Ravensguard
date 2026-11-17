using Godot;

namespace Ravensguard.Game.Inputs;

/// <summary>Holds the live KeyMap, persists it to user://input.cfg and mirrors it into Godot's InputMap.</summary>
public static class InputBootstrap
{
    public const string ConfigPath = "user://input.cfg";
    public static KeyMap Map { get; private set; } = KeyMap.CreateDefault();

    public static void Init()
    {
        Map = KeyMap.CreateDefault();
        Load(Map);
        Register(Map);
    }

    public static void Load(KeyMap map, string path = ConfigPath)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(path) != Error.Ok) return;
        foreach (var a in map.Actions)
        {
            if (!cfg.HasSectionKey(a.Context.ToString(), a.Name)) continue;
            var raw = cfg.GetValue(a.Context.ToString(), a.Name).AsString();
            var list = new System.Collections.Generic.List<Binding>();
            foreach (var part in raw.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
                if (Binding.TryParse(part, out var b)) list.Add(b);
            map.Set(a.Id, list);
        }
    }

    public static void Save(KeyMap map, string path = ConfigPath)
    {
        var cfg = new ConfigFile();
        foreach (var a in map.Actions)
            cfg.SetValue(a.Context.ToString(), a.Name, string.Join(",", System.Linq.Enumerable.Select(map.Get(a.Id), b => b.Serialize())));
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
    }

    public static InputEvent ToEvent(Binding b)
    {
        bool shift = b.Mods.HasFlag(Mods.Shift), ctrl = b.Mods.HasFlag(Mods.Ctrl), alt = b.Mods.HasFlag(Mods.Alt);
        if (b.Kind == BindingKind.Key)
            return new InputEventKey { Keycode = (Key)b.Code, ShiftPressed = shift, CtrlPressed = ctrl, AltPressed = alt };
        return new InputEventMouseButton { ButtonIndex = (MouseButton)b.Code, ShiftPressed = shift, CtrlPressed = ctrl, AltPressed = alt };
    }

    /// <summary>Short key hint such as "[N]" for the first binding of a Menu/World action.</summary>
    public static string Hint(string actionId)
    {
        var l = Map.Get(actionId);
        return l.Count == 0 ? "" : "[" + l[0].Describe() + "]";
    }
}
