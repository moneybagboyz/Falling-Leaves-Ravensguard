using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Watch;

namespace Ravensguard.Sim.Battle;

// Morale at formation scale (battle-bubble-and-formations 4.6) and the hooks the brain and the morale step call. The per-soldier model of Phase 4 is unchanged; what is added is read from the
// formation: a flanked formation (a tenth of its men took a side or rear blow in the last 5 s), the rout cascade, the wall bonus at cohesion 600, the fallen captain felt by the whole formation,
// the hero's aura and where the routed run to. The brain's side: a member steers to his slot until an enemy is within 3 m, then fights on a leash.
public sealed partial class FormationBook
{
    // ------------------------------------------------------------------ the formation terms (every 10 ticks per row)

    private void MoraleStep(FightWorld w, FormationRow r, int tick)
    {
        var D = w.MoraleK.D;
        var R = Rules.R;
        if (r.ActiveNow > 0)
        {
            int hit = 0;
            for (int i = 0; i < r.Members.Length; i++)
                if (r.Slot[i] >= 0 && r.FlankBlow[i] > tick - D.FlankedForS * 20) hit++;
            if (hit * 1000 >= R.FlankedSharePm * r.ActiveNow)
            {
                int until = tick + D.FlankedForS * 20;
                for (int i = 0; i < r.Members.Length; i++)
                {
                    if (r.Slot[i] < 0) continue;
                    var f = w.Fighters[r.Members[i]];
                    if (!f.M.Has(Combat.Morale.MoraleFlags.Inited)) continue;
                    if (f.M.FlankedUntil < until) { f.M.FlankedUntil = until; f.FlankedUntil = until; }
                }
            }
        }
        int share = r.TotalNow > 0 ? r.RoutingNow * 1000 / r.TotalNow : 0;
        if (!r.Cascaded && share >= R.RoutShare)
        {
            r.Cascaded = true;
            Penalise(w, r, R.RoutCascadeX100, tick);
            int hw = HalfWidth(r);
            foreach (var o in Rows)
            {
                if (o == r || o.Side != r.Side || o.ActiveNow == 0) continue;
                long d = FightMath.Len(o.AnchorX - r.AnchorX, o.AnchorY - r.AnchorY);
                if (d <= R.NeighbourCascadeRadiusM * 1000L + hw + HalfWidth(o)) Penalise(w, o, R.NeighbourCascadeX100, tick);
            }
        }
        else if (r.Cascaded && share < R.RoutShare / 2) r.Cascaded = false;
    }

    private int HalfWidth(FormationRow r) => (r.Files - 1) * Rules.Shape(r.Shape, r.Kind).LateralMm / 2;

    private static void Penalise(FightWorld w, FormationRow r, int x100, int tick)
    {
        for (int i = 0; i < r.Members.Length; i++)
        {
            var f = w.Fighters[r.Members[i]];
            if (!f.Active || f.Routing || !f.M.Has(Combat.Morale.MoraleFlags.Inited)) continue;
            f.M.PenaltyX100 += x100; f.M.LastLossTick = tick;
        }
    }

    // ------------------------------------------------------------------ hooks from morale

    /// <summary>A blow from the side or behind landed on a member (the morale step's hit hook).</summary>
    public void NoteFlankBlow(Fighter f, int tick)
    {
        if (f.FmRow >= 0 && f.FmRow < Rows.Count && f.FmMember < Rows[f.FmRow].FlankBlow.Length) Rows[f.FmRow].FlankBlow[f.FmMember] = tick;
    }

    /// <summary>A formation's captain fell: every member of it, wherever he stands, takes the leader-down penalty (members within the radius have it already from the Phase 4 rule).</summary>
    public void CaptainFell(FightWorld w, Fighter fallen)
    {
        if (fallen.FmRow < 0 || fallen.FmRow >= Rows.Count) return;
        var r = Rows[fallen.FmRow];
        if (r.Captain != fallen.Id) return;
        var D = w.MoraleK.D; long rl = D.LeaderDownRadiusM * 1000L;
        for (int i = 0; i < r.Members.Length; i++)
        {
            var o = w.Fighters[r.Members[i]];
            if (o.Id == fallen.Id || !o.Active || !o.M.Has(Combat.Morale.MoraleFlags.Inited) || w.DistMm(o, fallen) <= rl) continue;
            o.M.PenaltyX100 += D.LeaderDownX100; o.M.LastLossTick = w.Tick;
        }
    }

    /// <summary>The shield wall and schiltron bonus (+5) for a member standing in a formation of that shape with cohesion at 600 or more.</summary>
    public bool WallBonus(Fighter f)
    {
        if (f.FmRow < 0 || f.FmRow >= Rows.Count) return false;
        var r = Rows[f.FmRow];
        return r.Shape is Formation.ShieldWall or Formation.Schiltron && r.Cohesion >= Rules.R.CohesionMinPm && r.Slot[f.FmMember] >= 0 && f.Active;
    }

    /// <summary>The hero's aura (4.6): 10 m + Leadership / 5 m; zero for anyone else.</summary>
    public int AuraMm(Fighter f) => f.Id == Hero && Hero >= 0 ? Rules.R.AuraBaseM * 1000 + HeroLeadership * 1000 / Math.Max(1, Rules.R.AuraLeadershipDiv) : 0;

    /// <summary>A routed soldier's heading: away from the enemy, and toward his side's entry point (the road he came by). Null when the side has no entry point.</summary>
    public (int X, int Y)? EntryDir(Fighter f)
    {
        if (!Entry.TryGetValue(f.Team, out var e)) return null;
        return FightMath.Norm(e.X - f.X, e.Y - f.Y);
    }

    // ------------------------------------------------------------------ hooks from the brain

    public void ClearPace(Fighter f)
    {
        if (f.FmRow < 0 || f.FmRow >= Rows.Count) return;
        Rows[f.FmRow].Pace[f.FmMember] = 0; f.FmSteer = false;
    }

    /// <summary>The pace a member keeps while steering to his slot (mm a second; 0: his own walk).</summary>
    public int PaceOf(Fighter f) => f.FmRow >= 0 && f.FmRow < Rows.Count ? Rows[f.FmRow].Pace[f.FmMember] : 0;

    /// <summary>
    /// A member's footwork (4.4): with no enemy within 3 m he steers to his slot, at the anchor's pace plus what closes his gap, and stands facing the front when there; a missile man with a
    /// foe in range stays to shoot. True: the brain has nothing more to do for him this think.
    /// </summary>
    public bool Steer(FightWorld w, Fighter f, int tick)
    {
        if (f.FmRow < 0 || f.FmRow >= Rows.Count) return false;
        var r = Rows[f.FmRow]; int mi = f.FmMember; int s = r.Slot[mi];
        if (s < 0) return false;
        if (f.Ranged && f.Stance.UseRanged && (f.Loaded || f.Ammo > 0) && w.AnyEnemyWithin(f, w.K.ArcherMaxMm)) return false;
        if (w.AnyEnemyWithin(f, Rules.R.ContactMm)) return false;
        var c = f.Cmd;
        SlotWorld(r, s, tick, out int sx, out int sy);
        long dx = sx - f.X, dy = sy - f.Y, d = FightMath.Len(dx, dy);
        f.FmSteer = true;
        int ranks = Math.Max(1, (r.LayoutN + r.Files - 1) / r.Files);
        int stagger = ranks <= 1 ? 0 : r.Rank(s) * Rules.R.StaggerTicks / (ranks - 1);
        if (tick < r.OrderGo + stagger || d <= 150)
        {
            c.MoveX = c.MoveY = 0; c.Sprint = false; r.Pace[mi] = 0;
            c.FaceX = r.FaceX; c.FaceY = r.FaceY;
            if (r.Shape == Formation.ShieldWall && f.Shield != null) c.Block = true;
            return true;
        }
        var (ux, uy) = FightMath.Norm(dx, dy);
        c.MoveX = ux; c.MoveY = uy;
        c.Sprint = d > 6000 && f.Stamina > 3000;
        r.Pace[mi] = (int)Math.Min(w.K.WalkMmps, Math.Max(300, r.PaceNow + d * 2));
        if (d > 2000) { c.FaceX = ux; c.FaceY = uy; } else { c.FaceX = r.FaceX; c.FaceY = r.FaceY; }
        return true;
    }

    /// <summary>How far a member in contact may stray from his slot (4.4): his shape's leash (2 m), 6 m in loose order or a charge, 1 m on a hold, none while braced.</summary>
    public int LeashMm(FightWorld w, FormationRow r, Fighter f)
    {
        var def = Rules.Shape(r.Shape, r.Kind);
        int leash = def.LeashMm;
        if (r.Order.Kind == OrderKind.Charge || r.Shape == Formation.Loose) leash = Math.Max(leash, 6000);
        if (r.Order.Kind == OrderKind.Hold) leash = Math.Min(leash, 1000);
        if (w.Braced(f)) leash = 0;
        return leash;
    }

    /// <summary>The leash (4.4) applied to a fighting member's move after the brain has chosen it: beyond the leash he walks back to his slot; a step that would leave it is not taken.</summary>
    public void Leash(FightWorld w, Fighter f, int tick)
    {
        if (f.FmSteer || f.FmRow < 0 || f.FmRow >= Rows.Count) return;
        var r = Rows[f.FmRow]; int s = r.Slot[f.FmMember];
        if (s < 0) return;
        var c = f.Cmd;
        if (c.MoveX == 0 && c.MoveY == 0) return;
        int leash = LeashMm(w, r, f);
        SlotWorld(r, s, tick, out int sx, out int sy);
        long dx = f.X - sx, dy = f.Y - sy;
        long d = FightMath.Len(dx, dy);
        if (d > leash)
        {
            if (leash == 0 && d <= 150) { c.MoveX = c.MoveY = 0; return; }
            var (ux, uy) = FightMath.Norm(-dx, -dy);
            c.MoveX = ux; c.MoveY = uy; c.Sprint = false;
            return;
        }
        long nx = dx + ((long)c.MoveX * 150 >> 10), ny = dy + ((long)c.MoveY * 150 >> 10);
        if (FightMath.Len(nx, ny) > leash && FightMath.Len(nx, ny) > d) { c.MoveX = c.MoveY = 0; c.Sprint = false; }
    }

    public bool HoldsFire(Fighter f) => f.FmRow >= 0 && f.FmRow < Rows.Count && Rows[f.FmRow].Fire == FireMode.Hold;

    /// <summary>
    /// Whether a formation's archer may shoot at a target (4.4): never at a melee his own men are in within 5 m of the aim point, and under a shoot-at-target order only at that formation.
    /// </summary>
    public bool SafeShot(FightWorld w, Fighter f, Fighter t)
    {
        if (f.FmRow < 0 || f.FmRow >= Rows.Count) return true;
        var r = Rows[f.FmRow];
        if (r.Fire == FireMode.AtTarget && r.FireTargetRow >= 0 && t.FmRow != r.FireTargetRow) return false;
        long safe = Rules.R.ArcherSafeMm;
        foreach (int id in w.Near(0, t, safe))
        {
            var o = w.Fighters[id];
            if (o.Team == f.Team && o.Id != f.Id && o.Active && w.DistMm(o, t) <= safe) return false;
        }
        return true;
    }

    /// <summary>A shoot-at-target order (the overlay's "shoot at ..."): the formation's missile men shoot only at that enemy formation.</summary>
    public void ShootAt(FormationRow r, int enemyRow) { r.Fire = FireMode.AtTarget; r.FireTargetRow = enemyRow; }
}
