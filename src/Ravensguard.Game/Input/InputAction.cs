using System;
using Godot;

namespace Ravensguard.Game.Inputs;

public enum InputContext { Global, World, CombatStance, CalledShot, Build, Menu }

public enum BindingKind { Key, Mouse }

[Flags] public enum Mods { None = 0, Shift = 1, Ctrl = 2, Alt = 4 }

/// <summary>One physical binding: a key or mouse button plus modifiers. Value type; Code is a Godot Key / MouseButton.</summary>
public readonly record struct Binding(BindingKind Kind, int Code, Mods Mods = Mods.None)
{
    public static Binding K(Key k, Mods m = Mods.None) => new(BindingKind.Key, (int)k, m);
    public static Binding M(MouseButton b, Mods m = Mods.None) => new(BindingKind.Mouse, (int)b, m);

    public string Serialize() => $"{(Kind == BindingKind.Key ? 'K' : 'M')}:{Code}:{(int)Mods}";

    public static bool TryParse(string s, out Binding b)
    {
        b = default;
        var p = s.Split(':');
        if (p.Length != 3 || p[0].Length != 1 || !int.TryParse(p[1], out var code) || !int.TryParse(p[2], out var mods)) return false;
        if (p[0] == "K") b = new(BindingKind.Key, code, (Mods)mods);
        else if (p[0] == "M") b = new(BindingKind.Mouse, code, (Mods)mods);
        else return false;
        return true;
    }

    /// <summary>Human-readable text, e.g. "Ctrl+W", "Mouse Left". Uses the engine for key names.</summary>
    public string Describe()
    {
        var prefix = (Mods.HasFlag(Mods.Ctrl) ? "Ctrl+" : "") + (Mods.HasFlag(Mods.Shift) ? "Shift+" : "") + (Mods.HasFlag(Mods.Alt) ? "Alt+" : "");
        if (Kind == BindingKind.Key)
        {
            if ((Key)Code == Key.Slash && Mods == Mods.Shift) return "?";
            return prefix + OS.GetKeycodeString((Key)Code);
        }
        var name = (MouseButton)Code switch
        {
            MouseButton.Left => "Left",
            MouseButton.Right => "Right",
            MouseButton.Middle => "Middle",
            MouseButton.WheelUp => "Wheel Up",
            MouseButton.WheelDown => "Wheel Down",
            var o => o.ToString(),
        };
        return prefix + "Mouse " + name;
    }
}

/// <summary>An action definition: a context, a name and its default bindings (typically one key and/or one mouse button).</summary>
public sealed record InputAction(InputContext Context, string Name, Binding[] Defaults)
{
    /// <summary>Godot InputMap action name, e.g. "world.move_up".</summary>
    public string Id => $"{Context.ToString().ToLowerInvariant()}.{Name}";

    /// <summary>Translation key, e.g. "ACTION_WORLD_MOVE_UP".</summary>
    public string LocKey => "ACTION_" + Id.Replace('.', '_').ToUpperInvariant();
}
