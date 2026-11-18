using System;
using Godot;

namespace Ravensguard.Game.Inputs;

public enum InputContext { Global, World, CombatStance, CalledShot, Build, Menu, BlueprintEditor, Trade, Mounted, Choice }

/// <summary>Key = layout keycode (F-keys, digits, symbols); PhysicalKey = scancode position (letters and the console key, so AZERTY/QWERTZ keep WASD); Mouse = button.</summary>
public enum BindingKind { Key, Mouse, PhysicalKey }

[Flags] public enum Mods { None = 0, Shift = 1, Ctrl = 2, Alt = 4 }

/// <summary>One physical binding: a key or mouse button plus modifiers. Value type; Code is a Godot Key / MouseButton.</summary>
public readonly record struct Binding(BindingKind Kind, int Code, Mods Mods = Mods.None)
{
    /// <summary>Keyboard binding. Letters are stored as physical keys so they follow the key position on any layout.</summary>
    public static Binding K(Key k, Mods m = Mods.None) => k is >= Key.A and <= Key.Z ? P(k, m) : new(BindingKind.Key, (int)k, m);
    public static Binding P(Key k, Mods m = Mods.None) => new(BindingKind.PhysicalKey, (int)k, m);
    public bool IsKeyboard => Kind != BindingKind.Mouse;
    /// <summary>Old configs stored letters as layout keys; fold them into physical keys so comparisons stay consistent.</summary>
    public Binding Normalize() => Kind == BindingKind.Key && Code is >= (int)Key.A and <= (int)Key.Z ? this with { Kind = BindingKind.PhysicalKey } : this;
    public bool SameInput(Binding o) => Kind == o.Kind && Code == o.Code;
    /// <summary>True when every modifier of this binding is also in <paramref name="o"/> and o has more (so o is the more specific binding).</summary>
    public bool StrictSubsetOf(Binding o) => SameInput(o) && (Mods & o.Mods) == Mods && Mods != o.Mods;
    public static Binding M(MouseButton b, Mods m = Mods.None) => new(BindingKind.Mouse, (int)b, m);

    public string Serialize() => $"{(Kind == BindingKind.Key ? 'K' : Kind == BindingKind.PhysicalKey ? 'P' : 'M')}:{Code}:{(int)Mods}";

    public static bool TryParse(string s, out Binding b)
    {
        b = default;
        var p = s.Split(':');
        if (p.Length != 3 || p[0].Length != 1 || !int.TryParse(p[1], out var code) || !int.TryParse(p[2], out var mods)) return false;
        if (mods < 0 || mods > 7) return false;
        if (p[0] == "K") b = new Binding(BindingKind.Key, code, (Mods)mods).Normalize();
        else if (p[0] == "P") b = new(BindingKind.PhysicalKey, code, (Mods)mods);
        else if (p[0] == "M") b = new(BindingKind.Mouse, code, (Mods)mods);
        else return false;
        return true;
    }

    /// <summary>Human-readable text, e.g. "Ctrl+W", "Mouse Left". Uses the engine for key names.</summary>
    public string Describe()
    {
        var prefix = (Mods.HasFlag(Mods.Ctrl) ? "Ctrl+" : "") + (Mods.HasFlag(Mods.Shift) ? "Shift+" : "") + (Mods.HasFlag(Mods.Alt) ? "Alt+" : "");
        if (IsKeyboard)
        {
            if ((Key)Code == Key.Slash && Mods == Mods.Shift) return "?";
            var key = (Key)Code;
            if (Kind == BindingKind.PhysicalKey)
            {
                // Show the label the key has on the player's layout (AZERTY shows Z for the physical W position).
                if (DisplayServer.GetName() != "headless") { var l = DisplayServer.KeyboardGetKeycodeFromPhysical(key); if (l != Key.None) key = l; }
            }
            return prefix + OS.GetKeycodeString(key);
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
