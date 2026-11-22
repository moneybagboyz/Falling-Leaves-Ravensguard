using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Combat.Mounted;

/// <summary>
/// The cavalry brain (mounted-combat 4.6, <c>CavalryBrain</c>): the same combat brain decides the rider's guard and reactions; this sets his horse's line and his strike. It charges a
/// target with a clear lane when the refusal estimate there is under 40%, strikes at the run (a couched lance or a swing timed to land), rides on past, turns wide and re-forms 40 m away;
/// it flanks a braced line instead of charging into it, holds off when every way in is braced, and rides off when its horse panics (500) or has lost a fifth of its blood. Foot spear and pike
/// bearers brace against a horse closing on them. Writes only fighter commands and the pair's AI fields.
/// </summary>
public static class MountedBrain
{
    public static void Ride(FightWorld w, MountedFighter m, Fighter r)
    {
        var L = w.Mounts!; var D = L.D; var c = r.Cmd;
        var h = w.Fighters[m.Horse];
        c.Start = null; c.Feint = false; c.Throw = null; c.Reload = false;
        c.MoveX = c.MoveY = 0; c.Sprint = false;
        int tick = w.Tick + 1;
        if (m.AiPhase != MountedFighter.AiRetreat && (m.PanicPm >= D.Panic.RetreatAt || h.Book.LossPm >= D.Panic.RetreatLossPm || r.Routing)) { m.AiPhase = MountedFighter.AiRetreat; m.AiSince = tick; }
        if (m.AiPhase == MountedFighter.AiRetreat)
        {
            var (cx, cy, any) = EnemyCentre(w, r);
            var (ax, ay) = any ? FightMath.Norm(r.X - cx, r.Y - cy) : (m.HX, m.HY);
            if (ax == 0 && ay == 0) (ax, ay) = (m.HX, m.HY);
            Go(c, ax, ay, gallop: true);
            if (!any || Nearest(w, r) > 25_000) w.RideOff(m);
            return;
        }
        if (w.Formations != null && r.FmRow >= 0 && w.Formations.CavalryRide(w, m, r)) return;      // [P7 CV-A] a rider of a cavalry formation keeps his slot and strikes what is in reach
        var t = m.AiTarget >= 0 && m.AiTarget < w.Fighters.Count ? w.Fighters[m.AiTarget] : null;
        if (t != null && (!t.Active || w.IsHorse(t) || t.Team == r.Team)) t = null;
        switch (m.AiPhase)
        {
            case MountedFighter.AiCharge:
            {
                if (t == null || m.AiStruck < 0 && Avoid(w, m, r, t)) { t = PickTarget(w, m, r); m.AiTarget = t?.Id ?? -1; }
                if (t == null) { Flank(w, m, r, tick); return; }
                long dx = t.X - r.X, dy = t.Y - r.Y, dist = FightMath.Len(dx, dy);
                var (ux, uy) = FightMath.Norm(dx, dy);
                // line up first: a gallop turns only 60 degrees a second, so off the line by more than 25 degrees inside 25 m the horse is held to a trot to come round
                bool aligned = FightMath.Dot(m.HX, m.HY, ux, uy) >= 928 || dist > 25_000;
                Go(c, ux, uy, gallop: aligned);
                c.FaceX = ux; c.FaceY = uy;
                if (m.AiStruck < 0 && StrikeAt(w, m, r, t, tick, dist)) m.AiStruck = tick;
                bool behind = FightMath.Dot(m.HX, m.HY, ux, uy) < 0 && dist < 8000;
                bool done = m.AiStruck >= 0 && tick > m.AiStruck + 4 && r.Act is not (FAct.Windup or FAct.Strike);
                if (done || behind || tick - m.AiSince > 30 * 20)
                {
                    m.AiPhase = MountedFighter.AiPass; m.AiSince = tick;
                    m.AiX = r.X + (int)((long)m.HX * D.AiOvershootMm >> 10); m.AiY = r.Y + (int)((long)m.HY * D.AiOvershootMm >> 10);
                }
                return;
            }
            case MountedFighter.AiPass:
            {
                Go(c, m.HX, m.HY, gallop: true);
                if (FightMath.Len(m.AiX - r.X, m.AiY - r.Y) < 3000 || tick - m.AiSince > 60) Reform(w, m, r, tick);
                return;
            }
            case MountedFighter.AiReform:
            case MountedFighter.AiFlank:
            case MountedFighter.AiHold:
            {
                long d = FightMath.Len(m.AiX - r.X, m.AiY - r.Y);
                var (ux, uy) = FightMath.Norm(m.AiX - r.X, m.AiY - r.Y);
                if (d > 2500) Go(c, ux, uy, gallop: d > 15_000 || m.AiPhase == MountedFighter.AiFlank && d > 6000);
                else if (w.NearestEnemy(r) is { } e) { var (fx, fy) = FightMath.Norm(e.X - r.X, e.Y - r.Y); c.FaceX = fx; c.FaceY = fy; }
                bool arrived = d < 6000 || tick - m.AiSince > 12 * 20;
                if (arrived && (m.AiPhase != MountedFighter.AiHold || (tick - m.AiSince) % 20 == 0))
                {
                    var nt = PickTarget(w, m, r);
                    if (nt != null) { m.AiPhase = MountedFighter.AiCharge; m.AiTarget = nt.Id; m.AiStruck = -1; m.AiSince = tick; }
                    else if (m.AiPhase == MountedFighter.AiReform) Flank(w, m, r, tick);
                    else if (m.AiPhase == MountedFighter.AiFlank && arrived) Hold(w, m, r, tick);
                }
                return;
            }
        }
    }

    /// <summary>
    /// The rider's blow at <paramref name="t"/> (4.6): when his weapon is free, he starts a couched lance or a swing timed to land where the horse will be when the wind-up ends, inside the reach.
    /// True when the blow was started. The cavalry brain and a cavalry formation's riders both strike through here.
    /// </summary>
    public static bool StrikeAt(FightWorld w, MountedFighter m, Fighter r, Fighter t, int tick, long dist)
    {
        var D = w.Mounts!.D; var c = r.Cmd;
        if (!(r.Act is FAct.Idle or FAct.Block && r.WeaponArm && r.Stamina > 0)) return false;
        bool lance = FightWorld.IsLance(r) && r.Weapon.AttackIndex("couch") >= 0;
        int wind = lance ? Math.Max(1, D.CouchTicks) : w.WindupTicks(r, false);
        // strike so that the blow lands where the horse will be when the wind-up ends (its velocity over the wind-up), inside the reach
        long px = r.X + (long)m.VX * wind / 20, py = r.Y + (long)m.VY * wind / 20;
        long dAt = FightMath.Len(t.X - px, t.Y - py);
        int reach = w.ReachAgainst(r, t);
        if (dAt <= reach + w.K.ReachSlackMm - 300 && dist <= reach + w.K.ReachSlackMm + (long)m.Speed * (wind + 1) / 20)
        {
            int body = t.Plan.IndexOf(t.Plan.Id == "horse" ? "chest" : "upper_body");
            int part = lance && body >= 0 ? body : CombatBrain.PickPart(w, r, t, tick);      // a couched lance is aimed at the body
            int atk = lance ? r.Weapon.AttackIndex("couch") : CombatBrain.AutoAttack(w, r, t, part);
            c.Start = new AttackReq { Attack = atk, Target = t.Id, Part = part, Gap = -1, Auto = true };
            c.Block = false;
            return true;
        }
        return false;
    }

    private static void Go(FightCommand c, int x, int y, bool gallop) { c.MoveX = x; c.MoveY = y; c.Sprint = gallop; if (x != 0 || y != 0) { c.FaceX = x; c.FaceY = y; } }

    private static void Reform(FightWorld w, MountedFighter m, Fighter r, int tick)
    {
        var D = w.Mounts!.D;
        var (cx, cy, any) = EnemyCentre(w, r);
        var (ax, ay) = any ? FightMath.Norm(r.X - cx, r.Y - cy) : (m.HX, m.HY);
        if (ax == 0 && ay == 0) (ax, ay) = (m.HX, m.HY);
        m.AiPhase = MountedFighter.AiReform; m.AiSince = tick; m.AiStruck = -1; m.AiTarget = -1;
        m.AiX = (int)(cx + ((long)ax * D.AiReformMm >> 10)); m.AiY = (int)(cy + ((long)ay * D.AiReformMm >> 10));
        if (!any) { m.AiX = r.X; m.AiY = r.Y; }
    }

    /// <summary>Every way in is braced: ride round to the line's flank (20 m to the side of the braced bearers, a little behind them).</summary>
    private static void Flank(FightWorld w, MountedFighter m, Fighter r, int tick)
    {
        var D = w.Mounts!.D;
        long sx = 0, sy = 0, fx = 0, fy = 0; int n = 0;
        foreach (var b in w.Fighters)
        {
            if (b.Team == r.Team || !b.Active || !FightWorld.CanBrace(b)) continue;
            sx += b.X; sy += b.Y; fx += b.FX; fy += b.FY; n++;
        }
        if (n == 0) { Hold(w, m, r, tick); return; }
        int bx = (int)(sx / n), by = (int)(sy / n);
        var (nx, ny) = FightMath.Norm(fx, fy);
        if (nx == 0 && ny == 0) (nx, ny) = FightMath.Norm(r.X - bx, r.Y - by);
        int px = -ny, py = nx;
        if ((long)px * (r.X - bx) + (long)py * (r.Y - by) < 0) { px = -px; py = -py; }
        m.AiPhase = MountedFighter.AiFlank; m.AiSince = tick; m.AiStruck = -1;
        m.AiX = bx + (int)((long)px * D.AiFlankMm >> 10) - (int)((long)nx * 5000 >> 10);
        m.AiY = by + (int)((long)py * D.AiFlankMm >> 10) - (int)((long)ny * 5000 >> 10);
    }

    /// <summary>Nothing to charge: stand off 25 m from the nearest enemy and look again each second.</summary>
    private static void Hold(FightWorld w, MountedFighter m, Fighter r, int tick)
    {
        var e = w.NearestEnemy(r);
        m.AiPhase = MountedFighter.AiHold; m.AiSince = tick; m.AiStruck = -1;
        if (e == null) { m.AiX = r.X; m.AiY = r.Y; return; }
        var (ux, uy) = FightMath.Norm(r.X - e.X, r.Y - e.Y);
        if (ux == 0 && uy == 0) (ux, uy) = (m.HX, m.HY);
        m.AiX = e.X + (int)((long)ux * 25_000 >> 10); m.AiY = e.Y + (int)((long)uy * 25_000 >> 10);
    }

    /// <summary>The nearest enemy not a horse whose way in is not barred (refusal estimate under 40%); foot before mounted, standing before down; ties to the lower id.</summary>
    public static Fighter? PickTarget(FightWorld w, MountedFighter m, Fighter r)
    {
        // [P7 CV-A] the grid: doubling boxes from 30 m; the penalties only add, so a pick no farther than the box is the pick of the whole field, ties to the lower id as before
        long rad = 30_000;
        while (true)
        {
            bool all = !w.UseGrid || rad >= 1L << 33;
            Fighter? best = null; long bd = long.MaxValue;
            var ids = all ? w.Near(3, r.X, r.Y, 1L << 41) : w.Near(3, r.X, r.Y, rad);
            foreach (int oid in ids)
            {
                var o = w.Fighters[oid];
                if (o.Team == r.Team || !o.Active || w.IsHorse(o)) continue;
                if (Avoid(w, m, r, o)) continue;
                long d = w.DistMm(r, o) + (w.Riding(o) != null ? 5000 : 0) + (o.Act == FAct.Down ? 20_000 : 0);
                // a man whose shield faces the horse will likely catch the point, and a braced point hurts the horse: both count further away
                if (o.Shield != null && o.ShieldArm && w.FacingOf(o, r.X, r.Y) == Combat.Facing.Front) d += 4000;
                if (FightWorld.CanBrace(o) && o.Act == FAct.Bracing) d += 6000;
                // each rider picks his own man: a target another rider of the side is already charging counts 8 m further away
                foreach (var p in w.Mounts!.Pairs) if (p != m && p.Mounted && p.AiPhase == MountedFighter.AiCharge && p.AiTarget == o.Id && w.Fighters[p.Rider].Team == r.Team) d += 8000;
                if (d < bd || d == bd && best != null && o.Id < best.Id) { bd = d; best = o; }
            }
            if (all || best != null && bd <= rad || ids.Length >= w.Fighters.Count) return best;
            rad *= 2;
        }
    }

    /// <summary>The refusal the horse would face charging <paramref name="t"/> from where it is (braced or brace-ready polearms near the target facing the horse), and whether it is too high.</summary>
    public static bool Avoid(FightWorld w, MountedFighter m, Fighter r, Fighter t) => RefusalEstimatePm(w, m, r, t) >= w.Mounts!.D.Refusal.AiAvoidPm;

    public static int RefusalEstimatePm(FightWorld w, MountedFighter m, Fighter r, Fighter t)
    {
        var L = w.Mounts!; var R = L.D.Refusal;
        var h = w.Fighters[m.Horse];
        int n = w.BracedFacing(t, h, r.Team, countReady: true);
        int stakes = L.Terrain.Cells?.CountNear(Footing.Stakes, t.X, t.Y, R.ArcMm) ?? 0;                      // [P7 CV-C] stake rows in front of the target
        if (n == 0 && stakes == 0) return 0;
        return ChargeMath.RefusalPm(n, m.TrainedPm, m.Riding, R.BasePm, R.PerBracedPolearmPm, R.TrainedK, R.TrainedRef, R.RidingK, R.MaxPm, L.Terrain.Thunderstorm ? L.D.StormRefusalPm : 1000, stakes * L.D.StakeRefusalPm);
    }

    /// <summary>A foot spear or pike AI braces when an enemy horse within 30 m closes on it at a trot or faster (or is within 12 m), facing it.</summary>
    public static bool WantsBrace(FightWorld w, Fighter f, out Fighter? face)
    {
        face = null;
        if (!FightWorld.CanBrace(f) || f.Routing) return false;
        var L = w.Mounts!; var D = L.D;
        long bd = long.MaxValue;
        foreach (int rid in w.Near(3, f, D.AiBraceWithinMm))                 // [P7 CV-A] the grid: the riders within 30 m, in rider order as the pairs were
        {
            var m = L.Riding(rid);
            if (m == null) continue;
            var r = w.Fighters[m.Rider];
            if (r.Team == f.Team || !r.Active) continue;
            long d = w.DistMm(f, r);
            if (d > D.AiBraceWithinMm) continue;
            int vc = w.ClosingMmps(m, r, f);
            if (vc < D.BraceMinMmps && d > 12_000) continue;
            if (d < bd) { bd = d; face = r; }
        }
        return face != null;
    }

    /// <summary>
    /// <see cref="WantsBrace"/> for every foot fighter at once, from the riders' side (P7 CV-A): each mounted rider asks the grid for the fighters within 30 m (within 12 m when he is too slow to close at the brace speed) and
    /// offers himself to each bearer he closes on; a bearer keeps the nearest, the first rider in pair order on a tie, exactly as the per-bearer scan chose. Fills <c>BraceFace</c> (the rider's fighter id, -1 none).
    /// </summary>
    public static void BraceScan(FightWorld w)
    {
        var L = w.Mounts!; var D = L.D; int n = w.Fighters.Count;
        if (L.BraceD.Length < n) { L.BraceD = new long[Math.Max(n, L.BraceD.Length * 2)]; L.BraceFace = new int[L.BraceD.Length]; }
        Array.Fill(L.BraceD, long.MaxValue, 0, n); Array.Fill(L.BraceFace, -1, 0, n);
        foreach (var m in L.Pairs)
        {
            if (!m.Mounted) continue;
            var r = w.Fighters[m.Rider];
            if (!r.Active) continue;
            long fast = Math.Max(m.Speed, FightMath.Len(m.VX, m.VY)) * 5 / 4;          // the closing speed cannot beat this (a downhill charge adds a tenth)
            long reach = fast >= D.BraceMinMmps ? D.AiBraceWithinMm : Math.Min(12_000, D.AiBraceWithinMm);
            foreach (int id in w.NearUnsorted(3, r, reach))           // the order does not matter: a bearer keeps the nearest rider, and ties go to the first rider in pair order, which is this outer loop
            {
                var f = w.Fighters[id];
                if (f.Team == r.Team || !FightWorld.CanBrace(f) || f.Routing || !f.Active) continue;
                long d = w.DistMm(f, r);
                if (d > D.AiBraceWithinMm) continue;
                int vc = w.ClosingMmps(m, r, f);
                if (vc < D.BraceMinMmps && d > 12_000) continue;
                if (d < L.BraceD[id]) { L.BraceD[id] = d; L.BraceFace[id] = r.Id; }
            }
        }
    }

    private static (int X, int Y, bool Any) EnemyCentre(FightWorld w, Fighter r)
    {
        long sx = 0, sy = 0; int n = 0;
        foreach (var o in w.Fighters) if (o.Team != r.Team && o.Active && !w.IsHorse(o)) { sx += o.X; sy += o.Y; n++; }
        return n == 0 ? (r.X, r.Y, false) : ((int)(sx / n), (int)(sy / n), true);
    }

    private static long Nearest(FightWorld w, Fighter r)
    {
        long bd = long.MaxValue;
        foreach (var o in w.Fighters) if (o.Team != r.Team && o.Active && !w.IsHorse(o)) bd = Math.Min(bd, w.DistMm(r, o));
        return bd;
    }
}
