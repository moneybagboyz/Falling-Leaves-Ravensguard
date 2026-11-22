using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.World.Combat;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Watch;

namespace Ravensguard.Game.Debug;

/// <summary>
/// The command UI part of <c>--smoke</c> (P7-BB-D): a battle of formations on a <see cref="FightView"/> driven by the real keys. At 960x540 and again at 1280x720 the battle HUD, the order wheel and the tactical pause
/// overlay fit the window (the same fit check as every screen); F1 to F4 select formations and no longer send Phase 4's squad shapes, Shift+F1 to F4 add; R opens the wheel and its Shape ring issues; key 0 pauses
/// into the overlay (the fight does not step), a placement is drafted, Ctrl+Z takes it back, Space sends it at the paused tick (BB-11: its delay runs from resume); Esc closes the overlay and the game stays paused.
/// With no army the squad is formation 1 and the wheel's shapes are Phase 4's (BB-17's keys are checked by <c>--check-keymap</c> and the self-test).
/// </summary>
public static class BattleCommandSmoke
{
    static async Task Frames(SceneTree t, int n) { for (int i = 0; i < n; i++) await t.ToSignal(t, SceneTree.SignalName.ProcessFrame); }

    static void Key(Key k, bool shift = false, bool ctrl = false, bool pressed = true) =>
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = pressed, ShiftPressed = shift, CtrlPressed = ctrl });

    static void Need(bool ok, string what) { if (!ok) throw new Exception("command UI: " + what); }

    public static async Task Run(SceneTree tree)
    {
        var root = tree.Root; var oldSize = root.Size;
        var holder = new Node { Name = "BattleCommandSmoke" };
        try
        {
            root.AddChild(holder);
            var w = BattleScenario.Make(120, 30, 51, hero: true);
            int hero = w.Fighters.FindIndex(f => f.Player);
            var view = new FightView(w, hero, i => w.Input(hero, i)) { OwnsStepping = true, Name = "FightView" };
            holder.AddChild(view);
            await Frames(tree, 3);
            view.Advance(60);

            // F1 to F4 select, Shift adds, and the Phase 4 squad shapes are not sent by them any more
            root.Size = new Vector2I(960, 540); await Frames(tree, 3);
            var vr = view.GetViewportRect();
            Key(Godot.Key.F1); await Frames(tree, 2);
            Need(view.Cmd.Selected.SequenceEqual(new byte[] { 1 }), "F1 did not select formation 1");
            Key(Godot.Key.F2, shift: true); await Frames(tree, 2);
            Need(view.Cmd.Selected.SequenceEqual(new byte[] { 1, 2 }), "Shift+F2 did not add formation 2");
            Key(Godot.Key.F3); await Frames(tree, 2);
            Need(view.Cmd.Selected.SequenceEqual(new byte[] { 3 }), "F3 did not select formation 3");
            Need(w.OrdersOf(0).Formation == Formation.Auto, "an F-key still sends a Phase 4 squad shape");
            Need(view.BHud.Visible, "the battle HUD is not shown with formations in the fight");
            Phase3Smoke.Fit(view.BHud, "the battle HUD", vr);
            Key(Godot.Key.F1, pressed: false); Key(Godot.Key.F2, pressed: false); Key(Godot.Key.F3, pressed: false);

            // R: the wheel opens, fits, Tab turns the ring, the Shape ring issues to the selection
            Key(Godot.Key.R); await Frames(tree, 3);
            var wheel = view.Wheel ?? throw new Exception("command UI: R did not open the order wheel");
            Phase3Smoke.Fit(wheel, "the order wheel", vr);
            Key(Godot.Key.Tab); await Frames(tree, 2);
            Need(wheel.Ring == 1, "Tab did not turn to the Shape ring");
            wheel.Pick(1, 1);
            var row3 = w.Formations!.Find(0, 3)!;
            int before = row3.PendingCount;
            Need(wheel.Issue(), "the wheel did not issue the shield wall");
            Key(Godot.Key.R, pressed: false); await Frames(tree, 2);
            Need(view.Wheel == null, "the wheel stayed open after it issued");
            Need(row3.PendingCount == before + 1 && row3.Pending.Take(row3.PendingCount).Any(o => o.Kind == OrderKind.ShieldWall), "the shield wall order did not reach formation 3");
            Need(row3.Pending.Take(row3.PendingCount).Any(o => o.Kind == OrderKind.ShieldWall), "the shield wall order is not on its way");

            // 0: the overlay; the fight does not step under it
            Key(Godot.Key.Key0); await Frames(tree, 3);
            var ov = view.Overlay ?? throw new Exception("command UI: 0 did not open the tactical overlay");
            Need(view.TacticalPaused, "the overlay did not pause the battle");
            int t0 = w.Tick; await Frames(tree, 12);
            Need(w.Tick == t0, "the fight stepped under the overlay");
            Phase3Smoke.Fit(ov, "the tactical overlay", vr);
            int delay = (view.Cmd.Preview(OrderKind.MoveTo) ?? throw new Exception("command UI: no delay preview for the selection")).Ticks;   // the sim's own figure at the paused tick: the men and the captain do not move under the pause
            var c3 = view.Cmd.CentreOf(row3)!.Value;
            view.Cmd.MoveTo(3, c3.X + 20_000, c3.Y, false);
            Need(view.Cmd.Draft.Count == 1, "a placement was not drafted");
            int pend = row3.PendingCount;
            Need(row3.PendingCount == pend, "a drafted placement reached the sim before the resume");
            Key(Godot.Key.Z, ctrl: true); await Frames(tree, 2);
            Need(view.Cmd.Draft.Count == 0, "Ctrl+Z did not take the placement back");
            view.Cmd.MoveTo(3, c3.X + 20_000, c3.Y, false);
            Key(Godot.Key.R); await Frames(tree, 3);
            Need(ov.WheelOpen, "R did not open the wheel in the overlay");
            Phase3Smoke.Fit(ov, "the overlay with the wheel", vr);
            Key(Godot.Key.Escape); await Frames(tree, 2);
            Need(!ov.WheelOpen && view.Overlay != null, "Esc did not cancel only the wheel");
            Key(Godot.Key.Space); await Frames(tree, 3);                           // resume: what was placed goes out at the paused tick
            Need(!view.TacticalPaused && view.Overlay == null, "Space did not resume");
            Need(view.Cmd.Draft.Count == 0, "the draft was not sent");
            var moved = row3.Pending.Take(row3.PendingCount).Where(o => o.Kind == OrderKind.MoveTo).ToList();
            Need(moved.Count == 1 && moved[0].IssuedTick == t0 && moved[0].ArriveTick - moved[0].IssuedTick == delay, $"the resumed order's delay ran from {moved.FirstOrDefault().IssuedTick}, not the paused tick {t0}, or took {moved.FirstOrDefault().ArriveTick - moved.FirstOrDefault().IssuedTick} not {delay}");
            for (int i = 0; i < 90 && w.Tick == t0; i++) await Frames(tree, 1);
            Need(w.Tick > t0, "the fight did not run on after the resume");

            // Esc closes the overlay and the game stays paused; Space then resumes
            Key(Godot.Key.Key0); await Frames(tree, 3);
            Need(view.Overlay != null, "0 did not open the overlay again");
            Key(Godot.Key.Escape); await Frames(tree, 3);
            Need(view.Overlay == null && view.TacticalPaused, "Esc did not close the overlay and leave the game paused");
            Key(Godot.Key.Space); await Frames(tree, 3);
            Need(!view.TacticalPaused, "Space did not resume after Esc");

            // 1280 x 720: the same three fit again
            root.Size = new Vector2I(1280, 720); await Frames(tree, 3);
            vr = view.GetViewportRect();
            Phase3Smoke.Fit(view.BHud, "the battle HUD at 1280x720", vr);
            Key(Godot.Key.R); await Frames(tree, 3);
            Phase3Smoke.Fit(view.Wheel ?? throw new Exception("command UI: R did not open the wheel at 1280x720"), "the order wheel at 1280x720", vr);
            view.Wheel!.Close(); await Frames(tree, 2); Key(Godot.Key.R, pressed: false);
            Key(Godot.Key.Key0); await Frames(tree, 3);
            Phase3Smoke.Fit(view.Overlay ?? throw new Exception("command UI: 0 did not open the overlay at 1280x720"), "the tactical overlay at 1280x720", vr);
            view.Overlay!.Resume(); await Frames(tree, 2);
            root.Size = new Vector2I(960, 540); await Frames(tree, 3);
            view.QueueFree(); await Frames(tree, 2);

            // no army: the squad is formation 1; the wheel's Shape ring carries Phase 4's shapes
            var sk = FightCalibration.Skirmish(FightDemo.Content(), 7, 8);
            sk.Fighters[1].Player = true;
            sk.LodCentre = (sk.Fighters[1].X, sk.Fighters[1].Y);
            var sv = new FightView(sk, 1, i => sk.Input(1, i)) { OwnsStepping = true, Name = "SquadView" };
            holder.AddChild(sv); await Frames(tree, 3);
            Key(Godot.Key.F1); await Frames(tree, 2);
            Need(sv.Cmd.SquadSelected && !sv.BHud.Visible, "with no army F1 should select the squad and show no formation cards");
            Key(Godot.Key.F1, pressed: false);
            Key(Godot.Key.R); await Frames(tree, 3);
            var sw2 = sv.Wheel ?? throw new Exception("command UI: R did not open the wheel with no army");
            Phase3Smoke.Fit(sw2, "the order wheel with no army", sv.GetViewportRect());
            sw2.Pick(1, 1);                                                          // Shield wall
            Need(sw2.Issue(), "the squad's shield wall was not issued");
            Key(Godot.Key.R, pressed: false); await Frames(tree, 2);
            Need(sk.OrdersOf(sk.Fighters[1].Team).Formation == Formation.ShieldWall, "the wheel did not send Phase 4's shield wall to the squad");
            Key(Godot.Key.Key0); await Frames(tree, 2);
            Need(sv.Overlay == null && !sv.TacticalPaused, "the overlay opened for a squad with no formations");
            GD.Print("[smoke]   the command UI: F1-F4 select, the wheel and the overlay fit 960x540 and 1280x720, a pause gives no free time, Esc leaves it paused, no army: the squad is formation 1");
        }
        finally
        {
            holder.QueueFree();
            root.Size = oldSize; await Frames(tree, 2);
        }
    }
}
