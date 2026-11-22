using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Melee2;

public sealed partial class FightWorld
{
    /// <summary>
    /// P7-SG-B (sieges-and-engines 4.2): an engine's shot lands at (x, y) in the fight's own millimetres. Everyone standing within <paramref name="radiusMm"/> of it is struck, in fighter order, by a missile of
    /// <paramref name="momentumMilli"/> through the one resolver (<see cref="ResolverV2.ResolveImpact"/>: the same layers, tissues and wounds as an arrow; the resolver clamps the momentum, which is far more than any armour stops):
    /// a stone lands as a thrown rock (blunt), a bolt as a crossbow quarrel. The part struck is drawn by area from the engine's stream (0x7C0) keyed by <paramref name="key"/> and the fighter. Returns the people struck.
    /// </summary>
    public int EngineImpact(long x, long y, int radiusMm, long momentumMilli, ulong key, bool bolt)
    {
        int struck = 0;
        WeaponV2? w = null; int attack = 0;
        foreach (var t in Fighters)
        {
            if (t.Act is FAct.Dead or FAct.Fled or FAct.Surrendered) continue;
            if (FightMath.Len(t.X - x, t.Y - y) > radiusMm) continue;
            if (w == null)
            {
                w = bolt ? Content.AmmoWeapon(Content.Ammo["quarrel"]) : Content.ImprovisedWeapon(Content.Improvised["rock"]);
                var at = w.Attacks;
                for (int i = 0; i < at.Length; i++) if (bolt ? at[i].Type != AttackType.Blunt : at[i].Type == AttackType.Blunt) { attack = i; break; }
            }
            var plan = t.Plan;
            long sum = 0; foreach (var p in plan.Parts) if (!p.IsHosted) sum += p.RelPpm;
            long roll = sum <= 0 ? 0 : (long)(DetRng.NextUInt(Seed, Tick, key ^ ((ulong)(t.Id + 1) << 24), RngStreams.EngineShotLanding, 0x2000_0000u | (uint)t.Id) % (ulong)sum);
            int part = 0;
            foreach (var p in plan.Parts) { if (p.IsHosted) continue; roll -= p.RelPpm; if (roll < 0) { part = p.Index; break; } }
            var s = new StrikeV2
            {
                Strength = 50, Skill = 50, SizeCm3 = t.Kit.SizeCm3, SpeciesSizeCm3 = plan.SizeCm3, MomentumPm = 1000, Weapon = w, Attack = attack, Style = AttackStyle.Normal,
                Plan = plan, DefenderSizeCm3 = t.Kit.SizeCm3, DefenderSkill = t.Skill, Worn = t.Worn, WornCount = t.Worn.Length, MissingMask = t.MissingMask, Prone = false,
                TargetPart = part, TargetGap = -1, Facing = Facing.Front, Missile = true, ExtraMomentumMilli = momentumMilli,
            };
            Resolver.ResolveImpact(s, new BlowKey(Seed, Tick, key | 0xC000_0000UL, (uint)t.Id + 1), _r);
            if (_r.WoundCount == 0) { Emit(FightEventKind.Stopped, -1, t.Id, part, (int)(momentumMilli / 1000)); continue; }
            struck++;
            t.Book.Apply(t.Plan, _r, GameSec, Tick, key | 1UL);
            RefreshBody(t);
            MoraleOnHit(t, Facing.Front);
            Emit(FightEventKind.Hit, -1, t.Id, _r.Part, _r.WoundCount, (_r.Severed ? 1 : 0) | (_r.Redirected ? 2 : 0) | ((int)Facing.Front << 4));
            if (t.Act is FAct.Drawing or FAct.Aiming) t.Act = FAct.Idle;
            if (t.Book.Dead) Kill(t);
            else if (!t.Book.Conscious && t.Act != FAct.Down) GoDown(t);
        }
        return struck;
    }
}
