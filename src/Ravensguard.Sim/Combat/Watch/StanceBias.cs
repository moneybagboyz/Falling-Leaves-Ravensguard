using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Combat.Watch;

/// <summary>
/// How a stance and the side's orders steer the one combat brain (watch-mode 4.3, 4.4; the autopilot way: biases and filters, not a second brain). Each hook is called from
/// <see cref="CombatBrain"/> for every fighter it drives, NPC or watched hero alike, and writes only the fighter's command. No draws.
/// </summary>
public static class StanceBias
{
    /// <summary>The protect stance stands within 2.5 m of its charge, attacks only from within 2.2 m of it, and holds a point 1.5 m from it toward the nearest threat.</summary>
    public const int ProtectKeepMm = 2_500, ProtectAttackMm = 2_200, ProtectOffsetMm = 1_500, FallBackMm = 4_000;

    /// <summary>The target the side's orders or the stance name: the enemy nearest the guarded person, else the side's focus target within 25 m. Null: the brain's own choice.</summary>
    public static Fighter? OrderedTarget(FightWorld w, Fighter f, long pursue)
    {
        if (w.ProtectOf(f) is { } x)
        {
            Fighter? best = null; long bd = long.MaxValue;
            foreach (var o in w.Fighters)
            {
                if (o.Team == f.Team || !o.Active || o.Routing && w.DistMm(x, o) > pursue) continue;
                long d = w.DistMm(x, o);
                if (d < bd || d == bd && best != null && o.Id < best.Id) { bd = d; best = o; }
            }
            return best;
        }
        var orders = w.OrdersOf(f.Team);
        if (orders.Focus >= 0 && orders.Focus < w.Fighters.Count && w.Fighters[orders.Focus] is { } t && t.Team != f.Team && t.Active && w.DistMm(f, t) <= RiskEstimate.RadiusMm) return t;
        return null;
    }

    /// <summary>Protect footwork: out of place (more than 2.5 m from the charge, or the foe not at hand), go to the guard point between the charge and the threat; guard up.</summary>
    public static bool ProtectFootwork(FightWorld w, Fighter f, Fighter x, Fighter t, long dist, int strikeD)
    {
        var c = f.Cmd;
        long dx = w.DistMm(f, x);
        var (tx, ty) = FightMath.Norm(t.X - x.X, t.Y - x.Y);
        int off = x.Routing || dx > ProtectKeepMm ? 0 : ProtectOffsetMm;   // out of place (or the charge runs): straight to the charge; in place: between it and the threat
        int gx = x.X + (int)((long)tx * off >> 10), gy = x.Y + (int)((long)ty * off >> 10);
        long dg = FightMath.Len(gx - f.X, gy - f.Y);
        bool atHand = dist <= strikeD && dx <= ProtectAttackMm;
        if (atHand && dx <= ProtectKeepMm) return false;                 // in place with the foe in reach: the ordinary offence and footwork
        if (dg <= 400 && dx <= ProtectKeepMm) { c.MoveX = c.MoveY = 0; c.Block = c.Block || f.Shield != null; return true; }
        var (ux, uy) = FightMath.Norm(gx - f.X, gy - f.Y);
        c.MoveX = ux; c.MoveY = uy;
        bool far = dg > 1_500 || dx > ProtectKeepMm - 300;
        if (far && !(f.Brain.React is CombatBrain.ReactBlock or CombatBrain.ReactParry && w.Tick + 1 <= f.Brain.HoldBlockUntil)) c.Block = false;   // the guard drops to keep up (a guard walks at half pace)
        c.Sprint = far && f.Stamina > 1500;
        return true;
    }

    /// <summary>"Fall back to me": every fighter of the side but the leader walks to within 4 m of the leader (fighting only what reaches it there).</summary>
    public static bool FallBack(FightWorld w, Fighter f, in TeamOrders o)
    {
        bool fallBack = o.FallBack || o.Formation == Formation.FallBack;
        if (!fallBack || o.Leader < 0 || o.Leader >= w.Fighters.Count || o.Leader == f.Id) return false;
        var l = w.Fighters[o.Leader];
        if (l.Act is FAct.Dead or FAct.Fled) return false;
        long d = w.DistMm(f, l);
        if (d <= FallBackMm) return false;
        var (ux, uy) = FightMath.Norm(l.X - f.X, l.Y - f.Y);
        var c = f.Cmd;
        c.MoveX = ux; c.MoveY = uy; c.Sprint = d > 10_000 && f.Stamina > 3000; c.FaceX = ux; c.FaceY = uy;
        return true;
    }

    /// <summary>
    /// The side's formation (4.4): a shield wall's bearers stand braced shoulder to shoulder and let the enemy come (they close only inside 3 m); loose order keeps 2 m from the
    /// nearest friend; a line is the brain's own footwork. Only a side with a formation set (the hero's) is steered.
    /// </summary>
    public static bool FormationFootwork(FightWorld w, Fighter f, Fighter t, long dist, in TeamOrders o)
    {
        var c = f.Cmd;
        switch (o.Formation)
        {
            case Formation.ShieldWall when f.Shield != null && dist > 3_000:
                c.MoveX = c.MoveY = 0; c.Sprint = false; c.Block = true;
                return true;
            case Formation.Loose:
                {
                    Fighter? near = null; long nd = long.MaxValue;
                    foreach (var o2 in w.Fighters) if (o2.Id != f.Id && o2.Team == f.Team && o2.Active) { long d = w.DistMm(f, o2); if (d < nd) { nd = d; near = o2; } }
                    if (near == null || nd >= 2_000 || dist < 2_500) return false;
                    var (ax, ay) = FightMath.Norm(f.X - near.X, f.Y - near.Y);
                    var (ux, uy) = FightMath.Norm(t.X - f.X, t.Y - f.Y);
                    var (mx, my) = FightMath.Norm(ux + ax, uy + ay);
                    c.MoveX = mx; c.MoveY = my;
                    return true;
                }
        }
        return false;
    }

    /// <summary>Defensive: while closing on a foe more than 6 m away, an archer of the other side within 40 m gets the shield (the facing turns to the archer, the guard comes up).</summary>
    public static void ShieldToArchers(FightWorld w, Fighter f, long dist)
    {
        if (!f.Stance.ShieldToArchers || f.Shield == null || dist <= 6_000) return;
        Fighter? a = null; long ad = 40_001;
        foreach (var o in w.Fighters) if (o.Team != f.Team && o.Active && o.Ranged) { long d = w.DistMm(f, o); if (d < ad) { ad = d; a = o; } }
        if (a == null) return;
        var (ux, uy) = FightMath.Norm(a.X - f.X, a.Y - f.Y);
        f.Cmd.FaceX = ux; f.Cmd.FaceY = uy; f.Cmd.Block = true; f.Cmd.Sprint = false;
    }
}
