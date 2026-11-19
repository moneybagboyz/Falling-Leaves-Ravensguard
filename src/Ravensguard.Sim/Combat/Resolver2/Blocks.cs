using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Resolver2;

/// <summary>
/// The resolver's side of blocks, parries and dodges (resolver-v2 4.8). Timing, the AI's choice and the parry roll are the melee layer's
/// (melee-and-ranged 4.2); this is what a block does to the blow: a capacity that stops it, the excess that goes on to the forearm, stamina,
/// shield wear and the weapon's notch. A rear blow cannot be blocked; a flank blow cannot be shield-blocked.
/// </summary>
public static class Blocks
{
    /// <summary>A held weapon stops <c>110 + skill / 2</c> momentum units; a shield its capacity; +10% per braced neighbour (max 2).</summary>
    public static long CapacityMilli(in BlockInput b, CalibrationV2 c)
    {
        long cap = b.Kind == BlockKind.Shield && b.Shield != null
            ? b.Shield.CapacityMilli
            : (c.WeaponBlockBase + b.BlockerSkill / c.WeaponBlockSkillDivisor) * 1000L;
        int braced = b.BracedNeighbours < 0 ? 0 : b.BracedNeighbours > 2 ? 2 : b.BracedNeighbours;
        return cap * (1000 + c.BracedNeighbourPm * braced) / 1000;
    }

    /// <summary>
    /// Applies the defence to a blow of <paramref name="m"/> milli-units. Outcome <see cref="LayerOutcome.NotCovered"/> means the defence did
    /// not apply (wrong arc, dodge window missed, parry failed) and the blow goes on to the hit roll as usual.
    /// </summary>
    public static BlockOutcome Resolve(in StrikeV2 s, in BlowKey key, long m, CalibrationV2 c, BlowResult? log = null)
    {
        var b = s.Block;
        var none = new BlockOutcome { Outcome = LayerOutcome.NotCovered, ExcessMilli = m };
        switch (b.Kind)
        {
            case BlockKind.Dodge:
                if (!b.InDodgeWindow) return none;
                log?.Drew(RngStreams.MeleeDefence, 0);
                return DetRng.Range(key.Seed, key.FightTick, key.Attacker, RngStreams.MeleeDefence, key.Draw(0), 0, 1000) < 800
                    ? new BlockOutcome { Outcome = LayerOutcome.Dodged, Stopped = true } : none;
            case BlockKind.Parry:
                if (s.Facing == Facing.Rear || !b.ParrySucceeded) return none;
                return new BlockOutcome { Outcome = LayerOutcome.Parried, Stopped = true };
            case BlockKind.Weapon:
            case BlockKind.Shield:
                if (s.Facing == Facing.Rear) return none;
                if (b.Kind == BlockKind.Shield && s.Facing == Facing.Flank) return none;
                if (b.Kind == BlockKind.Shield && b.Shield == null) return none;
                long cap = CapacityMilli(b, c);
                long absorbed = m < cap ? m : cap;
                var o = new BlockOutcome
                {
                    Outcome = LayerOutcome.Blocked,
                    StaminaX100 = (int)(300 + absorbed / 500),               // 3 + M/50 stamina, x100
                };
                if (b.Kind == BlockKind.Shield) o.ShieldLossPm = (int)Math.Min(1000, m * 1000 / b.Shield!.DurabilityMilli);
                else o.NotchPm = (int)(absorbed * c.WeaponNotchPmPer50 / 50_000);
                if (m <= cap) { o.Stopped = true; o.ExcessMilli = 0; }
                else { o.ExcessMilli = m - cap; o.Stagger = true; }
                return o;
            default:
                return none;
        }
    }
}
