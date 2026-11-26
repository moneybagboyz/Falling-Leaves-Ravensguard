using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Battle;

// Captains (battle-bubble-and-formations 4.7): a formation's captain runs it when the player has not ordered it for 30 s (or when the overlay sets it to "captain"), by his Tactics: what he may do
// is the rows of battle/captains.jsonc, and he re-decides every max(40, 200 - Tactics x 2) ticks. His decisions have no delay (he is on the spot). A rule is a pure test of what the captain
// can see (every enemy fighter, by distance to the anchor) and the one order it gives. The rules fall in four channels (shape, movement, fire, facing); the best score in each channel is carried out,
// ties by stream 0x780 keyed (battle, formation, tick / 40).
public sealed partial class FormationBook
{
    private struct Sense
    {
        public long MinEnemyMm;
        public bool RangedNear, CavNear;
        public int FlankCount, FlankX, FlankY;
    }

    private enum Ch { Shape, Move, Fire, Face }
    private readonly List<(Ch Ch, int Score, FormationOrder O, int Tag)> _cand = [];

    private void Cand(Ch ch, int score, FormationOrder o, int tag = 0) => _cand.Add((ch, score, o, tag));

    private Sense Look(FightWorld w, FormationRow r)
    {
        var s = new Sense { MinEnemyMm = long.MaxValue };
        long missile = Rules.R.MissileRangeM * 1000L;
        long flankR = 40_000;
        long flankNearest = long.MaxValue;
        foreach (var f in w.Fighters)
        {
            if (f.Team == r.Side || !f.Active) continue;
            long dx = f.X - r.AnchorX, dy = f.Y - r.AnchorY;
            long d = FightMath.Len(dx, dy);
            if (d < s.MinEnemyMm) s.MinEnemyMm = d;
            if (d <= missile)
            {
                if (f.Ranged && (f.Loaded || f.Ammo > 0)) s.RangedNear = true;
                if (w.Mounts?.PairOf(f.Id) is { Mounted: true }) s.CavNear = true;
            }
            if (d <= flankR)
            {
                var (ux, uy) = FightMath.Norm(dx, dy);
                if (FightMath.Dot(r.FaceX, r.FaceY, ux, uy) < 512)
                {
                    s.FlankCount++;
                    if (d < flankNearest) { flankNearest = d; s.FlankX = ux; s.FlankY = uy; }
                }
            }
        }
        return s;
    }

    private void CaptainStep(FightWorld w, FormationRow r, int tick)
    {
        if (r.Foot is FootDismounting or FootMounting) return;                    // [P7 CV-B] the men are getting down or up: no new orders
        var cap = w.Fighters[r.Captain];
        int T = Math.Max(0, cap.Kit.Tactics);
        if (!r.CaptainAlways && tick - r.Micro < Rules.R.CaptainTakeoverS * 20) return;
        bool alert = T >= 50 && Rules.CaptainMay("face_flank_attack", T) && (tick + r.Index) % Math.Max(1, Rules.R.FlankAlertTicks) == 0;
        bool plan = tick >= r.NextDecision;
        if (!alert && !plan) return;
        var sense = Look(w, r);
        _cand.Clear();
        if (alert && sense.FlankCount >= 3 && FightMath.Dot(r.FaceX, r.FaceY, sense.FlankX, sense.FlankY) < 512 && r.WantFx == 0 && r.WantFy == 0)
        {
            var o = FormationOrder.Of(OrderKind.Face); o.FaceX = sense.FlankX; o.FaceY = sense.FlankY;
            Cand(Ch.Face, 100, o);
        }
        if (plan)
        {
            r.NextDecision = tick + Math.Max(Rules.R.RedecideMinTicks, Rules.R.RedecideBaseTicks - T * Rules.R.RedecideTacticsMul);
            Plan(w, r, T, sense, tick);
        }
        // the best of each channel; equal scores by the captain-ties stream
        for (int ch = 0; ch < 4; ch++)
        {
            int best = int.MinValue, n = 0;
            foreach (var c in _cand) if ((int)c.Ch == ch) { if (c.Score > best) { best = c.Score; n = 1; } else if (c.Score == best) n++; }
            if (n == 0) continue;
            int pick = n == 1 ? 0 : DetRng.Range(w.Seed, tick / 40, ((ulong)w.FightId << 20) ^ (ulong)(r.Index + 1), RngStreams.CaptainTies, (uint)ch, 0, n);
            foreach (var c in _cand)
            {
                if ((int)c.Ch != ch || c.Score != best) continue;
                if (pick-- > 0) continue;
                var o = c.O; o.Signal = OrderSignal.Captain; o.IssuedTick = w.Tick; o.ArriveTick = w.Tick; o.By = r.Captain; o.Serial = ++_serial;
                if (c.Tag == 1) r.WallByCaptain = true; else if (c.Tag == 2) r.WallByCaptain = false;
                Apply(w, r, o, tick);
                break;
            }
        }
    }

    private bool IsMissileRow(FightWorld w, FormationRow r)
    {
        if (r.Kind is FormationKind.Archers or FormationKind.Crossbows) return true;
        int ranged = 0, n = 0;
        for (int i = 0; i < r.Members.Length; i++) { if (r.Slot[i] < 0) continue; n++; if (w.Fighters[r.Members[i]].Ranged) ranged++; }
        return n > 0 && ranged * 2 > n;
    }

    private void Plan(FightWorld w, FormationRow r, int T, in Sense s, int tick)
    {
        var R = Rules.R;
        bool shapeWall = r.Shape is Formation.ShieldWall or Formation.Schiltron;
        bool missile = IsMissileRow(w, r);
        int enemyRow = NearestEnemyRow(r);
        var enemy = enemyRow >= 0 ? Rows[enemyRow] : null;

        // ---- shape
        if (Rules.CaptainMay("set_against_cavalry", T) && s.CavNear && !shapeWall) Cand(Ch.Shape, 90, FormationOrder.Of(OrderKind.SetAgainstCavalry), 1);
        if (Rules.CaptainMay("wall_against_missiles", T) && s.RangedNear && !shapeWall && !missile)
        {
            int shields = 0;
            for (int i = 0; i < r.Members.Length; i++) if (r.Slot[i] >= 0 && w.Fighters[r.Members[i]].Shield != null) shields++;
            if (shields >= 2) Cand(Ch.Shape, 80, FormationOrder.Of(OrderKind.ShieldWall), 1);
        }
        if (Rules.CaptainMay("wall_against_missiles", T) && r.WallByCaptain && !s.RangedNear && !s.CavNear && shapeWall) Cand(Ch.Shape, 40, FormationOrder.Of(OrderKind.Line), 2);

        // ---- fire
        if (missile)
        {
            bool hold = Rules.CaptainMay("hold_fire_beyond_range", T) && s.MinEnemyMm > R.MissileRangeM * 1000L;
            if (hold && r.Fire != FireMode.Hold) Cand(Ch.Fire, 50, FormationOrder.Of(OrderKind.HoldFire));
            else if (!hold && r.Fire != FireMode.AtWill && Rules.CaptainMay("archers_at_will", T)) Cand(Ch.Fire, 50, FormationOrder.Of(OrderKind.FireAtWill));
        }

        // ---- [P7 CV-B] dismount and mount (cavalry-and-mounted-war 4.5): men-at-arms get down to hold a line against cavalry or in woods or when their horses are hurt, and up again when it is over
        PlanDismount(w, r, T, s);
        if (r.Kind == FormationKind.Cavalry && Rules.CaptainMay("horse_archer_skirmish", T) && IsHorseArcherRow(w, r)) return;        // their movement is SkirmishStep's

        // ---- movement
        if (r.Kind == FormationKind.Cavalry) PlanCavalry(w, r, T);
        bool contactSide = false;
        foreach (var o in Rows) if (o.Side == r.Side && o.InContact) { contactSide = true; break; }
        if (Rules.CaptainMay("pull_back_archers", T) && missile && s.MinEnemyMm < R.PullBackArchersM * 1000L && r.Order.Kind != OrderKind.MoveTo)
        {
            var m = FormationOrder.Of(OrderKind.MoveTo);
            m.TargetX = r.AnchorX - (int)((long)r.FaceX * 30_000 >> 10); m.TargetY = r.AnchorY - (int)((long)r.FaceY * 30_000 >> 10);
            Cand(Ch.Move, 85, m);
        }
        if (Rules.CaptainMay("rally_broken", T) && r.TotalNow > 0 && r.RoutingNow * 1000 >= 250 * r.TotalNow && s.MinEnemyMm > 10_000 && r.Order.Kind != OrderKind.Rally)
            Cand(Ch.Move, 70, FormationOrder.Of(OrderKind.Rally));
        if (Rules.CaptainMay("charge_on_reload", T) && enemy != null && r.Order.Kind != OrderKind.Charge && !r.InContact && !missile && IsMissileRow(w, enemy)
            && FightMath.Len(enemy.AnchorX - r.AnchorX, enemy.AnchorY - r.AnchorY) < 80_000 && LoadedPm(w, enemy) < 300)
        { var c = FormationOrder.Of(OrderKind.Charge); c.TargetFormation = enemy.Index + 1; Cand(Ch.Move, 65, c); }
        if (Rules.CaptainMay("flank_engaged_enemy", T) && enemy != null && enemy.InContact && !r.InContact && contactSide && r.Order.Kind is not (OrderKind.FlankLeft or OrderKind.FlankRight or OrderKind.Charge)
            && FightMath.Len(enemy.AnchorX - r.AnchorX, enemy.AnchorY - r.AnchorY) < 120_000)
        {
            long lat = (long)(enemy.AnchorX - r.AnchorX) * -r.FaceY + (long)(enemy.AnchorY - r.AnchorY) * r.FaceX;
            var f = FormationOrder.Of(lat >= 0 ? OrderKind.FlankLeft : OrderKind.FlankRight); f.TargetFormation = enemy.Index + 1;
            Cand(Ch.Move, 60, f);
        }
        bool reserveHold = Rules.CaptainMay("keep_reserve", T) && !contactSide && IsReserve(r);
        bool outnumbered = Rules.CaptainMay("hold_if_outnumbered", T) && enemy != null && r.Active > 0 && (long)enemy.ActiveNow * 1000 >= (long)r.Active * R.OutnumberedPm;
        if (reserveHold && r.Order.Kind != OrderKind.Hold) Cand(Ch.Move, 55, FormationOrder.Of(OrderKind.Hold));
        if (outnumbered && r.Order.Kind != OrderKind.Hold) Cand(Ch.Move, 45, FormationOrder.Of(OrderKind.Hold));
        if (Rules.CaptainMay("advance_nearest", T) && !reserveHold && !outnumbered && (r.Order.Kind == OrderKind.None || r.Order.Kind == OrderKind.Hold && (r.Order.Signal == OrderSignal.Captain || r.CaptainAlways || tick - r.OrderGo >= Rules.R.CaptainTakeoverS * 20)) && !r.InContact)
        {
            var a = FormationOrder.Of(OrderKind.Advance); a.TargetFormation = enemy != null ? enemy.Index + 1 : 0;
            Cand(Ch.Move, 30, a);
        }
    }

    /// <summary>The side's last formation (highest number) when it has three or more: the reserve, kept back until contact (Tactics 50).</summary>
    private bool IsReserve(FormationRow r)
    {
        int count = 0, top = -1;
        foreach (var o in Rows) if (o.Side == r.Side && o.Active > 0) { count++; if (o.Id > top) top = o.Id; }
        return count >= 3 && r.Id == top;
    }

    private static int LoadedPm(FightWorld w, FormationRow e)
    {
        int n = 0, loaded = 0;
        for (int i = 0; i < e.Members.Length; i++)
        {
            if (e.Slot[i] < 0) continue;
            var f = w.Fighters[e.Members[i]];
            if (!f.Ranged) continue;
            n++; if (f.Loaded || f.Act is FAct.Aiming or FAct.Drawing) loaded++;
        }
        return n == 0 ? 1000 : loaded * 1000 / n;
    }
}
