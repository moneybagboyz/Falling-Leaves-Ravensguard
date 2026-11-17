using System.Collections.Generic;
using Godot;

namespace Ravensguard.Game.Inputs;

/// <summary>Default key maps per context. Mirrors docs/REDESIGN_PLAN.md section 2.8 (one line per action; the check-keymap tool parses the A(...) lines for localisation).</summary>
public static class InputDefs
{
    static Binding K(Key k, Mods m = Mods.None) => Binding.K(k, m);
    static Binding M(MouseButton b, Mods m = Mods.None) => Binding.M(b, m);

    static readonly List<InputAction> _all = new();
    public static IReadOnlyList<InputAction> All => _all;

    static void A(InputContext c, string name, params Binding[] d) => _all.Add(new InputAction(c, name, d));

    static InputDefs()
    {
        const InputContext G = InputContext.Global, W = InputContext.World, C = InputContext.CombatStance,
            S = InputContext.CalledShot, B = InputContext.Build, U = InputContext.Menu;

        // Global
        A(G, "console", Binding.P(Key.Quoteleft)); // physical: the backtick key position on any layout
        A(G, "inspector", K(Key.F12)); // dev-only: sim inspector panel

        // World
        A(W, "move_up", K(Key.W));
        A(W, "move_down", K(Key.S));
        A(W, "move_left", K(Key.A));
        A(W, "move_right", K(Key.D));
        A(W, "act", K(Key.E), M(MouseButton.Left));
        A(W, "context_menu", K(Key.Q), M(MouseButton.Right));
        A(W, "select_cycle", K(Key.Tab));
        A(W, "pause", K(Key.Space));
        A(W, "speed_1", K(Key.Key1));
        A(W, "speed_2", K(Key.Key2));
        A(W, "speed_4", K(Key.Key3));
        A(W, "speed_8", K(Key.Key4));
        A(W, "called_shot", K(Key.T));
        A(W, "order_wheel", K(Key.R));
        A(W, "formation_1", K(Key.F1));
        A(W, "formation_2", K(Key.F2));
        A(W, "formation_3", K(Key.F3));
        A(W, "formation_4", K(Key.F4));
        A(W, "panel_character", K(Key.C));
        A(W, "panel_inventory", K(Key.I));
        A(W, "panel_squad", K(Key.P));
        A(W, "panel_journal", K(Key.J));
        A(W, "panel_ledger", K(Key.L));
        A(W, "panel_faction", K(Key.F));
        A(W, "panel_codex", K(Key.K));
        A(W, "panel_chronicle", K(Key.H));
        A(W, "build", K(Key.B));
        A(W, "map", K(Key.M));
        A(W, "camera_up", K(Key.Up));
        A(W, "camera_down", K(Key.Down));
        A(W, "camera_left", K(Key.Left));
        A(W, "camera_right", K(Key.Right));
        A(W, "zoom_in", K(Key.Equal), M(MouseButton.WheelUp));
        A(W, "zoom_out", K(Key.Minus), M(MouseButton.WheelDown));
        A(W, "camera_home", K(Key.Home));
        A(W, "back", K(Key.Escape));
        A(W, "sprint", K(Key.Shift));
        A(W, "sneak", K(Key.Z));
        A(W, "combat_stance", K(Key.X));
        A(W, "throw", K(Key.G));
        A(W, "torch", K(Key.V));
        A(W, "level_up", K(Key.Pageup));
        A(W, "level_down", K(Key.Pagedown));
        A(W, "nameplates", K(Key.Alt));
        A(W, "controls_overlay", K(Key.Slash, Mods.Shift));
        A(W, "quicksave", K(Key.F5));
        A(W, "quickload", K(Key.F9));
        A(W, "report_bug", K(Key.F8));

        // Combat stance: the mouse becomes the weapon arm
        A(C, "move_up", K(Key.W));
        A(C, "move_down", K(Key.S));
        A(C, "move_left", K(Key.A));
        A(C, "move_right", K(Key.D));
        A(C, "attack", M(MouseButton.Left));
        A(C, "block", M(MouseButton.Right));
        A(C, "dodge_up", K(Key.W, Mods.Ctrl));
        A(C, "dodge_down", K(Key.S, Mods.Ctrl));
        A(C, "dodge_left", K(Key.A, Mods.Ctrl));
        A(C, "dodge_right", K(Key.D, Mods.Ctrl));
        A(C, "context_menu", K(Key.Q));
        A(C, "called_shot", K(Key.T));
        A(C, "order_wheel", K(Key.R));
        A(C, "formation_1", K(Key.F1));
        A(C, "formation_2", K(Key.F2));
        A(C, "formation_3", K(Key.F3));
        A(C, "formation_4", K(Key.F4));
        A(C, "combat_stance", K(Key.X));

        // Called shot (paused)
        A(S, "part_prev", K(Key.Up));
        A(S, "part_next", K(Key.Down));
        A(S, "type_prev", K(Key.Left));
        A(S, "type_next", K(Key.Right));
        A(S, "strike", K(Key.F));
        A(S, "cancel", K(Key.Escape));

        // Build
        A(B, "place", M(MouseButton.Left));
        A(B, "rotate_ccw", K(Key.Z));
        A(B, "rotate_cw", K(Key.X));
        A(B, "cancel", K(Key.Escape));
        A(B, "remove_ghost", M(MouseButton.Right));

        // Menu
        A(U, "up", K(Key.Up));
        A(U, "down", K(Key.Down));
        A(U, "left", K(Key.Left));
        A(U, "right", K(Key.Right));
        A(U, "accept", K(Key.Enter));
        A(U, "back", K(Key.Escape));
        A(U, "new_game", K(Key.N));
        A(U, "settings", K(Key.S));
        A(U, "quit", K(Key.Q));
        // New game flow: Quick Start, world creation, embark map, legends viewer, world library
        A(U, "legends", K(Key.L));
        A(U, "worlds", K(Key.W));
        A(U, "choose_region", K(Key.C));
        A(U, "advanced", K(Key.A));
        A(U, "ironman", K(Key.I));
        A(U, "sex", K(Key.X));
        A(U, "world_cycle", K(Key.G));
        A(U, "reroll_name", K(Key.R));
        A(U, "reroll_seed", K(Key.Y));
        A(U, "delete", K(Key.Delete));
        A(U, "auto_pick", K(Key.P));
        A(U, "overlay_next", K(Key.Tab));
        A(U, "overlay_1", K(Key.Key1));
        A(U, "overlay_2", K(Key.Key2));
        A(U, "overlay_3", K(Key.Key3));
        A(U, "overlay_4", K(Key.Key4));
        A(U, "overlay_5", K(Key.Key5));
        A(U, "toggle_rivers", K(Key.Key6));
        A(U, "toggle_towns", K(Key.Key7));
        A(U, "toggle_legends", K(Key.Key8));
        A(U, "search", K(Key.Slash));
        A(U, "filter_type", K(Key.T));
        A(U, "filter_realm", K(Key.E));
        A(U, "previous", K(Key.Backspace));
        A(U, "follow_cause", K(Key.F));
        A(U, "follow_effect", K(Key.D));
        A(U, "open_here", K(Key.O));
    }
}
