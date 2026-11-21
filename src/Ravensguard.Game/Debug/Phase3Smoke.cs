using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Game.World;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Game.Debug;

/// <summary>
/// The Phase 3 part of <c>--smoke</c>: with the street view reached through the real new-game flow (menu, region build; the stack is deeper than one, as in play), at 960x540 it opens every Phase 3 screen the way its key does,
/// checks that nothing lies outside the window and no scrolling body is wider than its box, and that Esc (or Back) returns to the street view; then it proves the barks run, an encounter that opens trade gives the journey
/// back, and the pause menu keeps the screen keys quiet.
/// </summary>
public static class Phase3Smoke
{
    static async Task Frames(SceneTree t, int n) { for (int i = 0; i < n; i++) await t.ToSignal(t, SceneTree.SignalName.ProcessFrame); }
    static async Task<bool> Until(SceneTree t, Func<bool> done, int max) { for (int i = 0; i < max && !done(); i++) await Frames(t, 1); return done(); }

    static void Key(Key k, bool shift = false, bool ctrl = false) => Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true, ShiftPressed = shift, CtrlPressed = ctrl });

    /// <summary>Controls that lie (even partly) outside <paramref name="view"/> and scrolling bodies whose content is wider than the box; empty when the screen fits.</summary>
    public static List<string> Misfits(Control root, Rect2 view)
    {
        var bad = new List<string>();
        void Walk(Node n, bool clipped)
        {
            foreach (var ch in n.GetChildren())
            {
                if (ch is not Control c) { Walk(ch, clipped); continue; }
                if (!c.IsVisibleInTree()) continue;
                var r = c.GetGlobalRect();
                bool scroll = c is ScrollContainer;
                if (!clipped && r.Size.X > 0 && r.Size.Y > 0 && c.Modulate.A > 0.01f
                    && (r.Position.X < view.Position.X - 1 || r.Position.Y < view.Position.Y - 1 || r.End.X > view.End.X + 1 || r.End.Y > view.End.Y + 1))
                    bad.Add($"{Path(c)} {r}");
                if (c is ScrollContainer sc && sc.HorizontalScrollMode == ScrollContainer.ScrollMode.Disabled)
                    foreach (var content in sc.GetChildren())
                        if (content is Control cc && cc.Visible && cc.GetCombinedMinimumSize().X > sc.Size.X + 1) bad.Add($"{Path(sc)}: content needs {cc.GetCombinedMinimumSize().X} px of {sc.Size.X}");
                Walk(c, clipped || scroll);
            }
        }
        Walk(root, false);
        return bad;
    }

    static void Dump(Node n, int d)
    {
        if (n is Control c && c.IsVisibleInTree()) GD.Print($"[dump] {new string(' ', d)}{c.GetType().Name} {c.Name} min {c.GetCombinedMinimumSize().X}x{c.GetCombinedMinimumSize().Y} size {c.Size.X}x{c.Size.Y}{(c is Label l ? " '" + l.Text.Replace('\n', ' ')[..Math.Min(40, l.Text.Length)] + "'" : "")}");
        if (d < 9) foreach (var ch in n.GetChildren()) Dump(ch, d + 1);
    }

    static string Path(Node n) { var parts = new List<string>(); for (var p = n; p != null && parts.Count < 4; p = p.GetParent()) parts.Add(p.Name); return string.Join("<", parts); }

    static void Fit(Control root, string what, Rect2 view)
    {
        var bad = Misfits(root, view);
        if (bad.Count > 0 && System.Environment.GetEnvironmentVariable("RG_SMOKE_DUMP") != null) Dump(root, 0);
        if (bad.Count > 0) throw new Exception($"{what} does not fit {view.Size.X}x{view.Size.Y}: " + string.Join(" | ", bad.GetRange(0, Math.Min(10, bad.Count))));
    }

    /// <summary>
    /// P5-C: build mode at 960 x 540 (build-mode-and-construction 6): B opens it and its palette fits; the World's screen keys stay off under it; P opens the plan library (it fits, Esc comes
    /// back to build mode); J opens the site panel of a staged site (it fits, Esc comes back); Ctrl+S's name dialog is modal (no build key under it) and Esc closes it; B closes build mode.
    /// </summary>
    static async Task BuildMode(SceneTree tree, ScreenRouter router, StreetView sv, Rect2 view)
    {
        Key(Godot.Key.B); await Frames(tree, 6);
        if (!sv.BuildModeOn) throw new Exception("B did not open build mode");
        await Frames(tree, 30);                                                                // the barrier answers the first preview
        Fit(sv.DebugHud!, "build mode's palette", view);
        foreach (var k in new[] { Godot.Key.I, Godot.Key.C, Godot.Key.N, Godot.Key.Y, Godot.Key.K }) { Key(k); await Frames(tree, 2); }
        if (router.Top != sv || !sv.BuildModeOn) throw new Exception($"a World screen key opened {router.Top?.GetType().Name} under build mode");
        Key(Godot.Key.P); await Frames(tree, 8);
        if (router.Top is not World.Build.PlanLibraryPanel) throw new Exception("P did not open the plan library (top " + router.Top?.GetType().Name + ")");
        await Frames(tree, 20);
        Fit(router.Top!, "the plan library", view);
        Key(Godot.Key.Escape); await Frames(tree, 4);
        if (router.Top != sv || !sv.BuildModeOn) throw new Exception("Esc did not return from the plan library to build mode");
        GD.Print("[smoke]   build mode: palette fits, World keys off, the plan library fits, Esc returns");
        // a site beside the hero (the dev console's staging: the land rule lifted for the one call), then its panel
        sv.DebugBuild("half");
        if (!await Until(tree, () => sv.DebugGhostCount > 0, 600)) throw new Exception("the staged site shows no ghosts");
        Key(Godot.Key.J); await Frames(tree, 8);
        if (router.Top is not World.Build.SitePanel) throw new Exception("J did not open the site panel (top " + router.Top?.GetType().Name + ")");
        await Frames(tree, 30);
        Fit(router.Top!, "the site panel", view);
        Key(Godot.Key.Escape); await Frames(tree, 4);
        if (router.Top != sv || !sv.BuildModeOn) throw new Exception("Esc did not return from the site panel to build mode");
        // save as blueprint: the name dialog over the street is modal
        var (hx, hy) = sv.DebugHeroCell;
        var dlg = sv.DebugSaveRect(hx - 4, hy - 4, hx + 4, hy + 4) ?? throw new Exception("the save-as dialog did not open");
        await Frames(tree, 6);
        Fit(sv.DebugHud!, "the save-as dialog", view);
        foreach (var k in new[] { Godot.Key.B, Godot.Key.P, Godot.Key.J, Godot.Key.Delete }) { Key(k); await Frames(tree, 2); }
        if (router.Top != sv || !sv.BuildModeOn || !GodotObject.IsInstanceValid(dlg) || !dlg.IsInsideTree()) throw new Exception("a build key acted under the save-as dialog");
        Key(Godot.Key.Escape); await Frames(tree, 4);
        if (GodotObject.IsInstanceValid(dlg) && dlg.IsInsideTree()) throw new Exception("Esc did not close the save-as dialog");
        if (!sv.BuildModeOn) throw new Exception("Esc on the dialog closed build mode too");
        Key(Godot.Key.B); await Frames(tree, 4);
        if (sv.BuildModeOn) throw new Exception("B did not close build mode");
        GD.Print("[smoke]   build mode: the site panel fits, the save-as dialog is modal, B closes");
    }

    /// <summary>
    /// P5-G: the vehicle designer at 960 x 540 (vehicle-designer 6): Q > Design a vehicle opens it and it fits; a wagon is placed part by part and the stats pane follows; T turns the preview through
    /// the eight facings, M mirrors, Ctrl+Z undoes; Ctrl+O's list is modal (no designer key acts under it) and Esc closes it; Esc closes the designer back to the street.
    /// </summary>
    static async Task VehicleDesigner(SceneTree tree, ScreenRouter router, StreetView sv, Rect2 view)
    {
        var d = sv.OpenDesigner(null) ?? throw new Exception("the vehicle designer did not open");
        await Frames(tree, 8);
        if (router.Top != d) throw new Exception("the designer is not on top");
        Fit(d, "the vehicle designer", view);
        d.DebugLoad("ox_wagon"); await Frames(tree, 3);
        if (!d.Model.Stats().CanBuild) throw new Exception("the ox wagon does not validate in the designer: " + string.Join(", ", d.Model.Stats().Issues.Select(i => i.Code)));
        int rev = d.Model.Revision;
        Key(Godot.Key.M); await Frames(tree, 2);
        if (d.Model.Revision == rev) throw new Exception("M did not mirror the design");
        Key(Godot.Key.Z, ctrl: true); await Frames(tree, 2);
        for (int k = 0; k < 8; k++) { Key(Godot.Key.T); await Frames(tree, 1); }
        Fit(d, "the vehicle designer (stats)", view);
        Key(Godot.Key.O, ctrl: true); await Frames(tree, 6);
        if (router.Top is not QMenu) throw new Exception("Ctrl+O did not open the design list (top " + router.Top?.GetType().Name + ")");
        Fit(router.Top!, "the design list", view);
        int before = d.Model.Revision;
        Key(Godot.Key.M); Key(Godot.Key.N, ctrl: true); await Frames(tree, 2);
        if (d.Model.Revision != before) throw new Exception("a designer key acted under its open list");
        Key(Godot.Key.Escape); await Frames(tree, 4);
        if (router.Top != d) throw new Exception("Esc did not return from the design list to the designer");
        Key(Godot.Key.Escape); await Frames(tree, 4);
        if (router.Top != sv) throw new Exception("Esc did not close the designer (top " + router.Top?.GetType().Name + ")");
        GD.Print("[smoke]   vehicle designer: fits, parts and stats, T/M/Ctrl+Z, the list is modal, Esc returns");
    }

    public static async Task Run(SceneTree tree, ScreenRouter router, StreetView sv)
    {
        var win = tree.Root;
        var oldSize = win.Size;
        win.Size = new Vector2I(960, 540);
        await Frames(tree, 3);
        var view = router.GetViewportRect();
        GD.Print($"[smoke] phase 3 screens at {view.Size.X}x{view.Size.Y} (window {win.Size}), stack depth {router.Depth}");
        try
        {
            if (router.Depth < 2) throw new Exception("the street view was not reached through the real new-game flow (stack depth " + router.Depth + ")");
            if (router.Top != sv) throw new Exception("the street view is not on top");

            // every screen: open as its key does, it fits the window, Esc comes back (the control rects stay inside the viewport)
            foreach (var what in new[] { "trade", "barter", "inventory", "character", "squad", "crafting", "talk", "journal", "autopilot", "map", "picker", "jobs", "codex", "qmenu", "overlay", "land", "deeds", "ledger", "business", "prospect", "will", "treatment", "family", "family_full", "court", "workboard", "workboard_mine", "heist", "faction", "chronicle", "bondage", "bondage_slaves", "bondage_bond" })   // P6-E4: the Chronicle; P6-D3: the heist panel P6-C2: the contracts screen, on the board and on the Taken tab; P6-B1: the treatment screen; P6-B3: the Family panel, empty and full; P5-A2: land, deeds; P5-F: the Ledger, the business panel, prospecting; P5-H: the will panel
            {
                int depth = router.Depth;
                if (!sv.DebugOpen(what)) throw new Exception("unknown screen " + what);
                await Frames(tree, 8);
                if (router.Top == sv) { GD.Print($"[smoke]   {what}: not opened here (a toast was shown instead)"); continue; }
                await Frames(tree, 30);   // the barrier answers; the screens rebuild
                Fit(router.Top!, what, view);
                if (what == "qmenu") { Key(Godot.Key.Key1); await Frames(tree, 3); }   // a numbered pick runs its verb (Choice context)
                Key(Godot.Key.Escape); await Frames(tree, 4);
                if (router.Top != sv && what != "qmenu") { Key(Godot.Key.Escape); await Frames(tree, 4); }
                if (router.Top != sv || router.Depth != depth || router.HasModal) throw new Exception($"{what}: Esc did not return to the street view (top {router.Top?.GetType().Name}, depth {router.Depth} of {depth})");
                GD.Print($"[smoke]   {what}: fits, Esc returns");
            }

            await BuildMode(tree, router, sv, view);                                          // P5-C
            await VehicleDesigner(tree, router, sv, view);                                    // P5-G

            // P5-F: L opens the Ledger (its own context: the street keys are off under it), Tab turns its pages, a row's detail is a modal over it, Esc closes it
            Key(Godot.Key.L); await Frames(tree, 30);
            if (router.Top is not UI.LedgerScreen) throw new Exception("L did not open the Ledger (top " + router.Top?.GetType().Name + ")");
            Fit(router.Top!, "the Ledger", view);
            foreach (var k in new[] { Godot.Key.Tab, Godot.Key.S, Godot.Key.A, Godot.Key.I, Godot.Key.M })
            {
                Key(k); await Frames(tree, 3);
                Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });   // released: S and A are the walk keys outside the Ledger
                await Frames(tree, 1);
            }
            if (router.Top is not UI.LedgerScreen) throw new Exception("a world key opened something over the Ledger");
            Key(Godot.Key.Escape); await Frames(tree, 4);
            if (router.Top != sv) throw new Exception("Esc did not close the Ledger");
            GD.Print("[smoke]   the Ledger: L opens it, the world keys are off under it, Esc closes it");
            // P6-C3 (rule-and-income 6): the Ledger's Fief tab (receipts by source, arrears, grievances, officials, dues, loans) fits 960x540 and its rows read
            Key(Godot.Key.L); await Frames(tree, 30);
            if (router.Top is not UI.LedgerScreen) throw new Exception("L did not open the Ledger for the Fief tab");
            for (int i = 0; i < 7; i++) { Key(Godot.Key.Tab); await Frames(tree, 3); }                 // Businesses -> Fief
            Fit(router.Top!, "the Ledger's Fief tab", view);
            Key(Godot.Key.Escape); await Frames(tree, 4);
            if (router.Top != sv) throw new Exception("Esc did not close the Ledger's Fief tab");
            GD.Print("[smoke]   the Ledger's Fief tab: fits, Esc closes it");
            // P7-IN3 (industries 6): the Ledger's Industry tab over a staged iron works fits 960x540 (flow, detail, balance sheet); its keys move the cursor and send commands without leaving the Ledger; Esc closes it
            sv.DebugIndustry(); await Frames(tree, 150);
            if (router.Top is not UI.LedgerScreen) throw new Exception("the Industry tab did not open (top " + router.Top?.GetType().Name + ")");
            Fit(router.Top!, "the Ledger's Industry tab", view);
            foreach (var k in new[] { Godot.Key.Down, Godot.Key.Right, Godot.Key.Left, Godot.Key.Up, Godot.Key.A, Godot.Key.S })
            {
                Key(k); await Frames(tree, 3);
                Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
                await Frames(tree, 1);
            }
            await Frames(tree, 150);
            if (router.Top is not UI.LedgerScreen) throw new Exception("an Industry key left the Ledger");
            Fit(router.Top!, "the Ledger's Industry tab after its keys", view);
            Key(Godot.Key.Escape); await Frames(tree, 4);
            if (router.Top != sv) throw new Exception("Esc did not close the Ledger's Industry tab");
            GD.Print("[smoke]   the Ledger's Industry tab: fits, its keys stay in the Ledger, Esc closes it");

            // P7-CN2 (coinage 9): the mint counter and the money changer fit 960x540 over a made-up view with every row live; Esc closes each
            foreach (var counter in new[] { "mint", "changer" })
            {
                sv.DebugCoin(counter); await Frames(tree, 20);
                if (router.Top is not UI.CoinPanel) throw new Exception($"the {counter} panel did not open (top " + router.Top?.GetType().Name + ")");
                Fit(router.Top!, $"the {counter} panel", view);
                Key(Godot.Key.Escape); await Frames(tree, 4);
                if (router.Top != sv) throw new Exception($"Esc did not close the {counter} panel");
            }
            GD.Print("[smoke]   the mint counter and the money changer: fit, Esc closes them");

            // P6-D2 (diplomacy-and-ai-lords 6): F opens the Faction screen (its own context: the street keys are off under it); every tab fits 960x540, Tab turns the pages, Esc closes it
            Key(Godot.Key.F); await Frames(tree, 40);
            if (router.Top is not UI.FactionScreen) throw new Exception("F did not open the Faction screen (top " + router.Top?.GetType().Name + ")");
            for (int i = 0; i < 5; i++) { Fit(router.Top!, "the Faction screen, page " + i, view); Key(Godot.Key.Tab); await Frames(tree, 6); }
            foreach (var k in new[] { Godot.Key.Down, Godot.Key.Up, Godot.Key.I, Godot.Key.M })
            {
                Key(k); await Frames(tree, 3);
                Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
                await Frames(tree, 1);
            }
            if (router.Top is not UI.FactionScreen) throw new Exception("a world key opened something over the Faction screen");
            Key(Godot.Key.Escape); await Frames(tree, 4);
            if (router.Top != sv) throw new Exception("Esc did not close the Faction screen");
            GD.Print("[smoke]   the Faction screen: F opens it, every page fits, the world keys are off under it, Esc closes it");

            // P6-B1 (MT-14): the treatment screen on a wounded hero: it fits 960x540, offers verbs (each row readable), the street keys are off under it, Esc closes it
            if (!sv.DebugOpen("treatment")) throw new Exception("the treatment screen is not known to the smoke");
            await Frames(tree, 40);
            if (router.Top is not UI.TreatmentScreen ts) throw new Exception("the treatment screen did not open (top " + router.Top?.GetType().Name + ")");
            Fit(ts, "the treatment screen", view);
            if (ts.Verbs <= 0) throw new Exception("the treatment screen offers no verb on a bleeding hero");
            foreach (var k in new[] { Godot.Key.Down, Godot.Key.Up, Godot.Key.Right, Godot.Key.Left, Godot.Key.Tab, Godot.Key.I, Godot.Key.J, Godot.Key.C, Godot.Key.L, Godot.Key.N })
            {
                Key(k); await Frames(tree, 3);
                Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
                await Frames(tree, 1);
            }
            if (router.Top is not UI.TreatmentScreen) throw new Exception("a world key opened " + router.Top?.GetType().Name + " over the treatment screen");
            Fit(router.Top!, "the treatment screen (after the keys)", view);
            Key(Godot.Key.Escape); await Frames(tree, 4);
            if (router.Top != sv) throw new Exception("Esc did not close the treatment screen");
            GD.Print("[smoke]   the treatment screen: fits, its verbs read, the street keys are off under it, Esc closes it");

            // the bar of the party, the controls overlay on F10 (also ?), the F8 report window
            sv.DebugPartyBar(); await Frames(tree, 2);
            Fit(sv.DebugHud!, "the HUD with a party bar", view);
            Key(Godot.Key.F10); await Frames(tree, 6);
            if (router.Top is not UI.ControlsOverlay) throw new Exception("F10 did not open the controls overlay");
            Fit(router.Top!, "the controls overlay", view);
            Key(Godot.Key.F10); await Frames(tree, 4);
            if (router.Top != sv) throw new Exception("F10 did not close the controls overlay");
            Key(Godot.Key.Slash, shift: true); await Frames(tree, 6);
            if (router.Top is not UI.ControlsOverlay) throw new Exception("? (Shift+/) did not open the controls overlay");
            Key(Godot.Key.Escape); await Frames(tree, 4);
            if (router.Top != sv) throw new Exception("Esc did not close the controls overlay");
            Key(Godot.Key.F8); await Frames(tree, 6);
            if (!Diagnostics.BugReportScreen.IsOpen) throw new Exception("F8 did not open the report window");
            Fit(router.Top!, "the F8 report window", view);
            ((Diagnostics.BugReportScreen)router.Top!).Close(); await Frames(tree, 3);

            // the pause menu owns the keys: N, I, J, Y, comma open nothing under it; Esc closes it
            if (!sv.DebugEsc()) throw new Exception("Esc did not open the pause menu");
            await Frames(tree, 3);
            foreach (var k in new[] { Godot.Key.N, Godot.Key.I, Godot.Key.J, Godot.Key.Y, Godot.Key.C, Godot.Key.P, Godot.Key.Comma, Godot.Key.L }) { Key(k); await Frames(tree, 2); }   // P5-F: L too
            if (router.Top != sv || !router.HasModal) throw new Exception($"a screen key opened something under the pause menu (top {router.Top?.GetType().Name}, modal {router.HasModal})");
            Key(Godot.Key.Escape); await Frames(tree, 4);
            if (router.Top != sv || router.HasModal) throw new Exception("Esc did not close the pause menu");

            // barks and nearby talk run with the real stack (they were gated on the stack being one deep, so they never did)
            // nobody may stand within 12 m of where the hero starts (open country): walk to the nearest live person first, then wait
            if (!await Until(tree, () => sv.DebugWalkToNearestPerson() >= 0, 1200)) throw new Exception("no live person near the hero in 1200 frames");
            if (!await Until(tree, () => sv.DebugBarksSpawned > 0, 3600)) throw new Exception("no bark appeared over the people near the hero in 3600 frames (the stack is " + router.Depth + " deep, nearest person " + sv.DebugWalkToNearestPerson().ToString("0.0") + " m)");
            GD.Print($"[smoke]   barks: {sv.DebugBarksSpawned} so far");

            // a road encounter: trade opens over the journey, Back gives the journey back, the keys 1 to 5 work again
            if (sv.DebugTravelHud != null)
            {
                string name = sv.DebugEncounter("peddler", sv.DebugAnyPerson());
                await Frames(tree, 6);
                if (!sv.DebugEncounterOpen) throw new Exception("the staged encounter did not open (" + name + ")");
                Fit(sv.DebugHud!, "the HUD with an encounter", view);
                Key(Godot.Key.Key1); await Frames(tree, 10);                  // option 1 is Trade
                if (router.Top is UI.TradeScreen)
                {
                    if (!sv.DebugEncounterScreenPending) throw new Exception("the trade window opened but the encounter is not waiting for it");
                    await Frames(tree, 20);
                    Fit(router.Top!, "the road trade window", view);
                    Key(Godot.Key.Escape); await Frames(tree, 10);
                    if (router.Top != sv) throw new Exception("Esc did not close the road trade window");
                    if (!await Until(tree, () => !sv.DebugEncounterScreenPending, 60)) throw new Exception("the encounter never settled after the trade window closed: the journey would stay held");
                    if (sv.DebugJourneyState != JourneyState.Travelling) throw new Exception("the journey did not resume after trade (" + sv.DebugJourneyState + ")");
                    GD.Print("[smoke]   encounter, trade, back: the journey resumed");
                }
                else throw new Exception("key 1 (Trade) did not open the trade window over the encounter (top " + router.Top?.GetType().Name + ")");
                // choose Go on with the key 2..: a fresh encounter, key 3 is Go on for a trader
                sv.DebugEncounter("peddler", sv.DebugAnyPerson()); await Frames(tree, 4);
                Key(Godot.Key.Key3); await Frames(tree, 10);
                if (sv.DebugEncounterOpen) throw new Exception("key 3 (Go on) did not answer the encounter: the digits are dead");
                sv.DebugCamp(); await Frames(tree, 6);
                if (!sv.DebugCampOpen) throw new Exception("the camp did not open");
                Fit(sv.DebugHud!, "the camp panel", view);
                Key(Godot.Key.Escape); await Frames(tree, 4);
            }
            // P4-D hook: the defeat and captivity screens fit 960x540 and keep the screen keys from the street view under them
            for (int which = 0; which < 3; which++)
            {
                var screen = sv.DebugShowAftermath(which); await Frames(tree, 8);
                string what = which == 0 ? "the defeat screen" : which == 1 ? "the captivity screen" : "the gaol cell";             // P6-C1: the gaol cell, with the sentence to expect and the ways out
                Fit(screen, what, view);
                foreach (var k in new[] { Godot.Key.I, Godot.Key.J, Godot.Key.C }) { Key(k); await Frames(tree, 2); }
                if (router.Top != sv) throw new Exception("a screen key opened " + router.Top?.GetType().Name + " under " + what);
                sv.DebugCloseAftermath(); await Frames(tree, 3);
            }
            GD.Print("[smoke]   defeat and captivity screens: fit, keys guarded");
            // P5-H: the succession screen with the estate ("You inherit", "Goes to the lord") fits and keeps the screen keys from the street view under it
            sv.DebugInheritance("succession"); await Frames(tree, 8);
            if (sv.DebugSuccessionView is not { } succ) throw new Exception("the succession screen with the estate did not open");
            Fit(succ, "the succession screen with the estate", view);
            foreach (var k in new[] { Godot.Key.I, Godot.Key.J, Godot.Key.C, Godot.Key.L }) { Key(k); await Frames(tree, 2); }
            if (router.Top != sv) throw new Exception("a screen key opened " + router.Top?.GetType().Name + " under the succession screen");
            sv.DebugCloseSuccession(); await Frames(tree, 3);
            GD.Print("[smoke]   succession screen with the estate: fits, keys guarded");
            // P6-E4 (chronicle-full 4.4, CH-10): H opens the Chronicle (My life); every view fits 960x540; in the Legends F follows a cause back, D a consequence forward, Backspace returns, Esc closes it
            Key(Godot.Key.H); await Frames(tree, 3); await Until(tree, () => router.Top is UI.ChronicleScreen { Browser: not null }, 600);
            if (router.Top is not UI.ChronicleScreen chron) throw new Exception("H did not open the Chronicle (top " + router.Top?.GetType().Name + ")");
            Fit(router.Top!, "the Chronicle, My life", view);
            for (int i = 0; i < 3; i++) { Key(Godot.Key.E); await Frames(tree, 3); Fit(router.Top!, "the Chronicle, view " + chron.Browser?.Scope, view); }
            if (chron.Browser is not { Scope: Ravensguard.Sim.Core.ChronicleScope.Legends } cb) throw new Exception("E did not turn the Chronicle to the Legends (browser " + (chron.Browser == null ? "none" : chron.Browser.Scope + ", " + chron.Browser.Rows.Count + " rows") + ")");
            var linked = cb.Rows.FirstOrDefault(r => cb.Why(r).Any(w => w.Entry != null)) ?? throw new Exception("the Legends hold no entry with a cause");
            cb.Open(linked.Id); chron.DebugRefresh(); await Frames(tree, 2);
            int d0 = cb.TrailDepth; Key(Godot.Key.F); await Frames(tree, 3);
            if (cb.TrailDepth != d0 + 1) throw new Exception("F did not follow the cause back");
            Fit(router.Top!, "the Chronicle after F", view);
            Key(Godot.Key.D); await Frames(tree, 3);
            if (cb.TrailDepth != d0 + 2) throw new Exception("D did not follow the consequence forward");
            Key(Godot.Key.Backspace); await Frames(tree, 3);
            if (cb.TrailDepth != d0 + 1) throw new Exception("Backspace did not return");
            Key(Godot.Key.Escape); await Frames(tree, 4);
            foreach (var k in new[] { Godot.Key.H, Godot.Key.E, Godot.Key.F, Godot.Key.D, Godot.Key.Backspace }) Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });   // released: D is a walk key outside the Chronicle
            await Frames(tree, 1);
            if (router.Top != sv) throw new Exception("Esc did not close the Chronicle");
            GD.Print("[smoke]   the Chronicle: H opens it, every view fits, F and D follow the chain, Backspace returns, Esc closes it");
        }
        finally { win.Size = oldSize; await Frames(tree, 2); }
    }
}
