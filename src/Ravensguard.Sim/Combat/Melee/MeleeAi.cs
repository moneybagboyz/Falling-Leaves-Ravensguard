using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Melee;

/// <summary>
/// Simple melee AI (plan §4A.3): approach, attack a chosen zone, react to incoming wind-ups with parry / dodge / block by skill,
/// feint against guards, retreat when stamina is low, run when routing. Writes only <see cref="Fighter.Cmd"/>; deterministic via DetRng.
/// </summary>
public static class MeleeAi
{
    public static void Think(MeleeWorld w, Fighter f)
    {
        var c = f.Cmd;
        c.ClearOneShots();
        c.Move = Vec2.Zero; c.Sprint = false;
        if (!f.CanAct) { c.Block = false; return; }
        var rng = new RngStream(w.Seed, w.Tick, (ulong)f.Id, MeleeWorld.StreamAi);
        c.Block = false;
        int skill = f.Spec.Skill;

        if (f.Routing) { Flee(w, f); return; }

        var tgt = PickTarget(w, f);
        if (tgt == null) { c.Block = false; return; }
        var d = tgt.Pos - f.Pos;
        double dist = d.Length;
        var dir = dist < 1e-6 ? f.Face : d * (1.0 / dist);
        c.Face = dir;
        f.AiTarget = tgt.Id;
        double strikeD = f.Reach + 2 * MeleeRules.BodyRadius;
        double desired = strikeD - 0.25;

        // --- defence: decide once per incoming wind-up, inside the last 0.2 s window ---
        bool threatened = false;
        foreach (var e in w.Fighters)
        {
            if (e.Team == f.Team || e.State != Act.Windup || e.Req == null) continue;
            if (e.Req.TargetId >= 0 && e.Req.TargetId != f.Id) continue;
            if ((e.Pos - f.Pos).Length > e.Reach + 2 * MeleeRules.BodyRadius + 0.5) continue;
            threatened = true;
            int toImpact = Math.Max(0, e.WindupNeeded - e.WindupElapsed - 1);
            int lag = Math.Clamp(3 - skill / 40, 0, 3);
            if (toImpact > MeleeRules.ParryWindowTicks || e.WindupElapsed < lag) continue;
            if (f.AiDecidedEnemy == e.Id && f.AiDecidedStart == e.WindupStartTick) continue;
            f.AiDecidedEnemy = e.Id; f.AiDecidedStart = e.WindupStartTick;
            if (f.State is not (Act.Idle or Act.Block)) continue;
            double r = rng.Range(0, 1000) / 1000.0;
            double pParry = Math.Min(0.85, 0.15 + skill / 130.0), pDodge = 0.12, pBlock = 0.35;
            bool canPress = f.State == Act.Idle; // a held block cannot be re-pressed inside the same tick
            if (r < pParry && canPress) { c.Block = true; f.AiBlockUntil = w.Tick + toImpact + 5; }
            else if (r < pParry + pDodge && w.Tick >= f.DodgeReadyTick && f.Stamina > 15)
            {
                var side = new Vec2(-dir.Y, dir.X);
                c.Dodge = (rng.Range(0, 2) == 0 ? side : side * -1.0) + dir * -0.5;
            }
            else if (r < pParry + pDodge + pBlock) { c.Block = true; f.AiBlockUntil = w.Tick + toImpact + 6; }
        }
        if (!c.Block) c.Block = w.Tick < f.AiBlockUntil;

        // --- feint against a guard ---
        if (f.State == Act.Windup && f.Req is { AutoRelease: true } && tgt.State == Act.Block && f.WindupElapsed * 2 < f.WindupNeeded
            && skill >= 30 && rng.Range(0, 1000) < 120)
        {
            c.Feint = true; f.AiNextAttackTick = w.Tick + 2;
            return;
        }

        // --- offence: opportunistic after the foe's blow, patient while a blow is incoming ---
        bool exhausted = f.Stamina < 22;
        bool foeRecovering = tgt.State is Act.Recover or Act.Stagger;
        bool foeCommitted = tgt.State == Act.Windup && tgt.Req != null && (tgt.Req.TargetId < 0 || tgt.Req.TargetId == f.Id);
        double aggr = 0.35 + 0.5 * (DetRng.NextUInt(w.Seed, 0, (ulong)f.Id, 5, 0) % 100) / 100.0; // per-fighter temperament
        bool wantAttack = foeRecovering ? rng.Range(0, 100) < 85
            : foeCommitted ? rng.Range(0, 100) < (int)(6 * aggr)
            : rng.Range(0, 100) < (int)(10 * aggr);
        if (f.State is Act.Idle or Act.Block && !f.WeaponArmLost && !exhausted && dist <= strikeD && w.Tick >= f.AiNextAttackTick
            && wantAttack && !(threatened && w.Tick < f.AiBlockUntil))
        {
            var req = new AttackRequest { TargetId = tgt.Id, PartId = PickPart(f, tgt, ref rng), AutoRelease = true };
            req.AttackId = PickAttack(w, f, tgt, req.PartId);
            c.Start = req;
            c.Block = false;
            f.AiNextAttackTick = w.Tick + 4 + rng.Range(0, 16 - skill / 15);
            return;
        }

        // --- footwork ---
        if (exhausted && dist < strikeD + 1.5 && f.State is Act.Idle or Act.Block)
        {
            c.Move = dir * -1.0; c.Block = true; f.AiBlockUntil = w.Tick + 2; // back off and guard to regain wind (5/s while guarding)
        }
        else if (dist > desired)
        {
            c.Move = dir;
            c.Sprint = dist > strikeD + 3 && f.Stamina > 40;
        }
        else if (f.Class == WeaponClass.Spear && dist < 1.3) c.Move = dir * -1.0;
    }

    private static Fighter? PickTarget(MeleeWorld w, Fighter f)
    {
        Fighter? cur = f.AiTarget >= 0 ? w.Fighters[f.AiTarget] : null;
        double Dist(Fighter o) => (o.Pos - f.Pos).Length;
        if (cur != null && cur.Active && !(cur.Routing && Dist(cur) > 10) && w.Tick % 20 != 0) return cur;
        Fighter? best = null; double bd = double.MaxValue;
        foreach (var o in w.Fighters)
        {
            if (o.Team == f.Team || !o.Active) continue;
            double dd = Dist(o);
            if (o.Routing && dd > 10) continue;
            if (dd < bd) { bd = dd; best = o; }
        }
        return best;
    }

    private static void Flee(MeleeWorld w, Fighter f)
    {
        var c = f.Cmd;
        c.Block = false;
        Vec2 away = Vec2.Zero;
        foreach (var e in w.Enemies(f))
        {
            if (!e.Active) continue;
            var d = f.Pos - e.Pos; double l = d.Length;
            if (l < 25) away += d * (1.0 / (l * l + 1));
        }
        if (away.IsZero) away = f.Face * -1.0;
        c.Move = away.Normalized(); c.Face = c.Move; c.Sprint = true;
    }

    /// <summary>Zone choice: skill/150 of the time it picks by vulnerability (weak armour cover, big area), otherwise by area alone.</summary>
    public static string PickPart(Fighter f, Fighter t, ref RngStream rng)
    {
        bool smart = rng.Range(0, 150) < f.Spec.Skill;
        var wts = smart ? t.VulnWeights : t.AreaWeights;
        double sum = 0; foreach (var x in wts) sum += x;
        double roll = rng.Range(0, 1_000_000) / 1e6 * sum;
        for (int i = 0; i < wts.Length; i++) { roll -= wts[i]; if (roll <= 0) return t.PartIds[i]; }
        return t.PartIds[^1];
    }

    /// <summary>
    /// Attack choice by situation. Against hard armour (an armour-layer piece covering the zone: plate, helm, mail) prefer blunt,
    /// then point, then edge; against soft or no armour prefer edge, then point, then blunt. Ties go to the faster attack,
    /// then to the attack id (ordinal), so the choice is deterministic.
    /// </summary>
    public static string PickAttack(MeleeWorld w, Fighter f, Fighter t, string part)
    {
        var attacks = f.WeaponStats.Shape.Attacks;
        bool hard = HardArmourOn(w, t, part);
        AttackDef? best = null; int bestRank = int.MaxValue;
        foreach (var a in attacks)
        {
            int rank = hard
                ? a.Type switch { AttackType.Blunt => 0, AttackType.Point => 1, _ => 2 }
                : a.Type switch { AttackType.Edge => 0, AttackType.Point => 1, _ => 2 };
            if (best == null || rank < bestRank
                || (rank == bestRank && (a.Velocity > best.Velocity
                    || (a.Velocity == best.Velocity && string.CompareOrdinal(a.Id, best.Id) < 0))))
            { best = a; bestRank = rank; }
        }
        return best!.Id;
    }

    private static bool HardArmourOn(MeleeWorld w, Fighter t, string part)
    {
        string baseId = t.Plan.ById.TryGetValue(part, out var p) ? p.BaseId : part;
        foreach (var worn in t.Spec.Worn)
        {
            var sh = w.Tables.Armour(worn.ShapeId, worn.MaterialId).Shape;
            if (sh.Layer == ArmourLayer.Armour && sh.Coverage.TryGetValue(baseId, out var cov) && cov >= 0.5) return true;
        }
        return false;
    }
}
