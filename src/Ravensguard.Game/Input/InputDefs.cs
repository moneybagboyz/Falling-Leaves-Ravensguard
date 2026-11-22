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
            S = InputContext.CalledShot, B = InputContext.Build, U = InputContext.Menu, E = InputContext.BlueprintEditor, T = InputContext.Trade, R = InputContext.Mounted, H = InputContext.Choice,
            V = InputContext.VehicleDesigner,
            Lg = InputContext.Ledger, Tr = InputContext.Treatment, Fc = InputContext.Faction, Ta = InputContext.Tactical, Ow = InputContext.OrderWheel;

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
        // P7-BB-D (battle-bubble-and-formations 9, overview Q9): F1-F4 select formations 1-4, Shift+F1-F4 add to the selection, 0 opens the tactical pause overlay (Phase 4's squad shapes are on the order wheel)
        A(W, "formation_add_1", K(Key.F1, Mods.Shift));
        A(W, "formation_add_2", K(Key.F2, Mods.Shift));
        A(W, "formation_add_3", K(Key.F3, Mods.Shift));
        A(W, "formation_add_4", K(Key.F4, Mods.Shift));
        A(W, "tactical_overlay", K(Key.Key0));
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
        A(W, "cart_hitch", K(Key.U));       // horses-and-carts spec 6: hitch the nearest animal to the nearest cart, or unhitch the team
        A(W, "cart_unload", K(Key.Backslash)); // drop 100 kg of the cart's load on the ground (a stuck cart); Y is the autopilot panel (overview 5A)
        A(W, "cart_reload", K(Key.Bracketright));
        A(W, "cart_dig", K(Key.Bracketleft)); // a shovel under the wheel: a fifth off the mud
        A(W, "camp", K(Key.Comma));      // travel spec 6: make camp, or open the camp panel (Q > Make camp once the Q menu exists)
        A(W, "forage", K(Key.Period));   // travel spec 4.4: forage the tile (Q > Forage)
        A(W, "hunt", K(Key.Semicolon));  // travel spec 4.5: an hour's hunt for game on this tile (Q > Hunt)
        A(W, "snare", K(Key.Apostrophe)); // travel spec 4.5: set a snare here, or take up the one beside you (Q > Snare)
        A(W, "autopilot", K(Key.Y)); // the autopilot panel (autopilot-v1 spec 6; overview 5A: Y is free in World)
        A(W, "make", K(Key.N)); // crafting screen (basic-crafting spec 4.7); N is free in World, CombatStance, CalledShot and Build
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
        A(W, "controls_overlay", K(Key.Slash, Mods.Shift), K(Key.F10)); // ? is Shift+Slash, which is not a "?" on every layout (German, French): F10 always works
        A(W, "quicksave", K(Key.F5));
        A(W, "quickload", K(Key.F9));
        A(W, "report_bug", K(Key.F8));
        A(W, "hint_more", K(Key.Enter)); // onboarding spec 3.1: opens the Codex page of the hint on screen (the router's own Enter, menu.accept, is a different context)
        // Phase 4 (overview 6A): watch mode and the combat log; F6, F7 and their Shift forms were free in World
        A(W, "watch_toggle", K(Key.F6));
        A(W, "stance_next", K(Key.F6, Mods.Shift));
        A(W, "combat_log", K(Key.F7));
        A(W, "last_report", K(Key.F7, Mods.Shift));

        // Mounted (the hero rides or drives; stacks on World): O toggles the walk, Shift is the gallop, E dismounts
        A(R, "walk_toggle", K(Key.O));

        // Choice (the numbered lists: talk window, Q menu, road encounter, camp panel): live beside World, where it takes the digits from the speed keys while a list is open
        for (int i = 1; i <= 9; i++) A(H, "pick_" + i, K(Key.Key0 + i));

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
        A(C, "formation_add_1", K(Key.F1, Mods.Shift));
        A(C, "formation_add_2", K(Key.F2, Mods.Shift));
        A(C, "formation_add_3", K(Key.F3, Mods.Shift));
        A(C, "formation_add_4", K(Key.F4, Mods.Shift));
        A(C, "tactical_overlay", K(Key.Key0));
        A(C, "battle_pause", K(Key.Space)); // P7-BB-D: with formations in the fight, Space pauses into the overlay; Space or 0 resumes
        A(C, "zoom_in", K(Key.Equal), M(MouseButton.WheelUp)); // P7-BB-C: the battle view steps through the zoom bands (Command, Field, Street, Close) as the world view does
        A(C, "zoom_out", K(Key.Minus), M(MouseButton.WheelDown));
        A(C, "combat_stance", K(Key.X));
        A(C, "throw", K(Key.G));
        A(C, "sprint", K(Key.Shift));
        A(C, "reload", K(Key.Key5));      // overview 6A: reload or nock (] is the cart reload)
        A(C, "attack_next", K(Key.Key6)); // cycle the attack type (Auto, then the weapon's attacks)
        A(C, "watch_toggle", K(Key.F6));
        A(C, "stance_next", K(Key.F6, Mods.Shift));
        A(C, "combat_log", K(Key.F7));
        A(C, "last_report", K(Key.F7, Mods.Shift));
        A(C, "back", K(Key.Escape));

        // Called shot (paused)
        A(S, "part_prev", K(Key.Up));
        A(S, "part_next", K(Key.Down));
        A(S, "type_prev", K(Key.Left));
        A(S, "type_next", K(Key.Right));
        A(S, "strike", K(Key.F));
        A(S, "cancel", K(Key.Escape));

        // Build
        A(B, "place", M(MouseButton.Left), M(MouseButton.Left, Mods.Shift)); // P5-C: Shift with a drag draws an outline (read as a modifier, as the blueprint editor does)
        A(B, "rotate_ccw", K(Key.Z));
        A(B, "rotate_cw", K(Key.X));
        A(B, "cancel", K(Key.Escape));
        A(B, "remove_ghost", M(MouseButton.Right));
        // P5-C (overview 6): build mode's own keys; the World keys other than time, camera, level, overlay, save and bug report are off while it is open
        A(B, "pan_up", K(Key.W));
        A(B, "pan_down", K(Key.S));
        A(B, "pan_left", K(Key.A));
        A(B, "pan_right", K(Key.D));
        A(B, "piece_prev", K(Key.Comma));
        A(B, "piece_next", K(Key.Period));
        A(B, "category_prev", K(Key.Bracketleft));
        A(B, "category_next", K(Key.Bracketright));
        A(B, "plan_library", K(Key.P));
        A(B, "mirror", K(Key.M));
        A(B, "roof_over", K(Key.R));
        A(B, "deconstruct", K(Key.Delete));
        A(B, "commit", K(Key.Enter));
        A(B, "undo", K(Key.Z, Mods.Ctrl));
        A(B, "save_blueprint", K(Key.S, Mods.Ctrl));
        A(B, "site_panel", K(Key.J));
        A(B, "close", K(Key.B));

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
        A(U, "continue", K(Key.K)); // main menu: the latest run
        A(U, "load", K(Key.V));     // main menu and world library: pick a saved run
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
        A(U, "dev_blueprints", K(Key.B)); // dev-only entry: the blueprint editor (debug builds)

        // Trade screen (trade-inventory-character spec 6, overview 5A): a modal context, so N and Tab mean something else here. Shift (x10) and Ctrl (all) are read with Left/Right.
        A(T, "up", K(Key.Up));
        A(T, "down", K(Key.Down));
        A(T, "less", K(Key.Left));
        A(T, "more", K(Key.Right));
        A(T, "confirm", K(Key.Enter));
        A(T, "pane", K(Key.Tab));
        A(T, "notebook", K(Key.N));
        A(T, "cancel", K(Key.Escape));

        // The Ledger (P5-F, businesses spec 6, overview 6): a modal screen alone with Global; sortable tables of every property, business, site, field, stall, vehicle, share and steward.
        A(Lg, "up", K(Key.Up));
        A(Lg, "down", K(Key.Down));
        A(Lg, "left", K(Key.Left));
        A(Lg, "right", K(Key.Right));
        A(Lg, "next_tab", K(Key.Tab));
        A(Lg, "sort", K(Key.S));
        A(Lg, "search", K(Key.Slash));
        A(Lg, "alerts", K(Key.A));
        A(Lg, "open", K(Key.Enter));
        A(Lg, "back", K(Key.Escape));

        // The treatment screen (P6-B1, medicine 4.1, overview 6): a modal screen alone with Global; the paper-doll's parts, the verbs of a part, the patients in reach.
        A(Tr, "up", K(Key.Up));
        A(Tr, "down", K(Key.Down));
        A(Tr, "left", K(Key.Left));
        A(Tr, "right", K(Key.Right));
        A(Tr, "accept", K(Key.Enter));
        A(Tr, "dress_all", K(Key.D));
        A(Tr, "next_patient", K(Key.Tab));
        A(Tr, "back", K(Key.Escape));

        // The Faction screen (P6-D2, diplomacy-and-ai-lords 6, overview 6): a modal screen alone with Global, mirroring the Ledger; the realm, who thinks what of the hero, treaties and wars, his standing and what he may ask.
        A(Fc, "up", K(Key.Up));
        A(Fc, "down", K(Key.Down));
        A(Fc, "left", K(Key.Left));
        A(Fc, "right", K(Key.Right));
        A(Fc, "next_tab", K(Key.Tab));
        A(Fc, "act", K(Key.Enter));
        A(Fc, "search", K(Key.Slash));
        A(Fc, "back", K(Key.Escape));

        // The tactical pause overlay (P7-BB-D, battle-bubble-and-formations 9): modal, alone with Global; the game is paused under it and an order placed in it is sent when it resumes
        A(Ta, "select", M(MouseButton.Left));          // select a block; drag it to place a move
        A(Ta, "frontage", M(MouseButton.Right));       // drag: start to end sets the width and the facing, perpendicular to the line
        A(Ta, "queue", K(Key.Shift));                  // held: the placement queues after the order in force
        A(Ta, "formation_1", K(Key.F1));
        A(Ta, "formation_2", K(Key.F2));
        A(Ta, "formation_3", K(Key.F3));
        A(Ta, "formation_4", K(Key.F4));
        A(Ta, "formation_add_1", K(Key.F1, Mods.Shift));
        A(Ta, "formation_add_2", K(Key.F2, Mods.Shift));
        A(Ta, "formation_add_3", K(Key.F3, Mods.Shift));
        A(Ta, "formation_add_4", K(Key.F4, Mods.Shift));
        A(Ta, "next_formation", K(Key.Tab));
        A(Ta, "order_wheel", K(Key.R));
        A(Ta, "captain", K(Key.C));
        A(Ta, "undo", K(Key.Z, Mods.Ctrl));
        A(Ta, "clear_queue", K(Key.Delete));
        A(Ta, "level_up", K(Key.Pageup));
        A(Ta, "level_down", K(Key.Pagedown));
        A(Ta, "pan_up", K(Key.Up));
        A(Ta, "pan_down", K(Key.Down));
        A(Ta, "pan_left", K(Key.Left));
        A(Ta, "pan_right", K(Key.Right));
        A(Ta, "zoom_in", K(Key.Equal), M(MouseButton.WheelUp));
        A(Ta, "zoom_out", K(Key.Minus), M(MouseButton.WheelDown));
        A(Ta, "resume", K(Key.Space), K(Key.Key0));
        A(Ta, "close", K(Key.Escape));                 // closes the overlay and stays paused

        // The order wheel (P7-BB-D): modal while R is held (or from the overlay); three rings (Orders, Shape, Missiles); release R or Enter issues to the selection, Esc cancels
        A(Ow, "up", K(Key.Up));
        A(Ow, "down", K(Key.Down));
        A(Ow, "left", K(Key.Left));
        A(Ow, "right", K(Key.Right));
        A(Ow, "ring_next", K(Key.Tab));
        A(Ow, "issue", K(Key.Enter), M(MouseButton.Left));
        A(Ow, "cancel", K(Key.Escape));

        // Blueprint editor (dev tool, plan 2.11). A standalone context like Menu: only Global stays live beside it.
        A(E, "primary", M(MouseButton.Left), M(MouseButton.Left, Mods.Shift)); // Shift on rectangles = outline only
        A(E, "secondary", M(MouseButton.Right)); // erase with any tool
        A(E, "zoom_in", K(Key.Equal), M(MouseButton.WheelUp));
        A(E, "zoom_out", K(Key.Minus), M(MouseButton.WheelDown));
        A(E, "pan_up", K(Key.Up));
        A(E, "pan_down", K(Key.Down));
        A(E, "pan_left", K(Key.Left));
        A(E, "pan_right", K(Key.Right));
        A(E, "tool_paint", K(Key.B));
        A(E, "tool_rect", K(Key.R));
        A(E, "tool_line", K(Key.L));
        A(E, "tool_flood", K(Key.F));
        A(E, "tool_erase", K(Key.E));
        A(E, "tool_eyedropper", K(Key.I));
        A(E, "layer_structure", K(Key.Key1));
        A(E, "layer_fixtures", K(Key.Key2));
        A(E, "layer_rooms", K(Key.Key3));
        A(E, "level_up", K(Key.Pageup));
        A(E, "level_down", K(Key.Pagedown));
        A(E, "copy_below", K(Key.D));
        A(E, "clear_level", K(Key.Delete));
        A(E, "add_room", K(Key.A));
        A(E, "delete_room", K(Key.Backspace));
        A(E, "piece_prev", K(Key.Comma));
        A(E, "piece_next", K(Key.Period));
        A(E, "rotate", K(Key.T));
        A(E, "mirror_x", K(Key.M));
        A(E, "mirror_y", K(Key.U));
        A(E, "resize", K(Key.X));
        A(E, "preview_wealth", K(Key.W));
        A(E, "preview_style", K(Key.H));
        A(E, "toggle_regions", K(Key.V));
        A(E, "toggle_grid", K(Key.G));
        A(E, "fit", K(Key.Home));
        A(E, "undo", K(Key.Z, Mods.Ctrl));
        A(E, "redo", K(Key.Y, Mods.Ctrl));
        A(E, "new", K(Key.N, Mods.Ctrl));
        A(E, "open", K(Key.O, Mods.Ctrl));
        A(E, "save", K(Key.S, Mods.Ctrl));
        A(E, "save_as", K(Key.F2));
        A(E, "close", K(Key.Escape));

        // Vehicle designer (P5-G, overview 6): a standalone context like the blueprint editor, alone with Global
        A(V, "place", M(MouseButton.Left));
        A(V, "remove", M(MouseButton.Right));
        A(V, "zoom_in", K(Key.Equal), M(MouseButton.WheelUp));
        A(V, "zoom_out", K(Key.Minus), M(MouseButton.WheelDown));
        A(V, "pan_up", K(Key.Up));
        A(V, "pan_down", K(Key.Down));
        A(V, "pan_left", K(Key.Left));
        A(V, "pan_right", K(Key.Right));
        A(V, "part_prev", K(Key.Comma));
        A(V, "part_next", K(Key.Period));
        A(V, "category_prev", K(Key.Bracketleft));
        A(V, "category_next", K(Key.Bracketright));
        A(V, "deck_up", K(Key.Pageup));
        A(V, "deck_down", K(Key.Pagedown));
        A(V, "rotate", K(Key.T));
        A(V, "mirror", K(Key.M));
        A(V, "undo", K(Key.Z, Mods.Ctrl));
        A(V, "redo", K(Key.Y, Mods.Ctrl));
        A(V, "new", K(Key.N, Mods.Ctrl));
        A(V, "open", K(Key.O, Mods.Ctrl));
        A(V, "save", K(Key.S, Mods.Ctrl));
        A(V, "stats_pane", K(Key.Tab));
        A(V, "build", K(Key.Enter));
        A(V, "close", K(Key.Escape));
    }
}
