using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Morale;

/// <summary>The outcome counts of one skirmish (MC-6): how long it ran and how its twenty fighters ended.</summary>
public readonly record struct SkirmishTally(int Seconds, bool Finished, int Deaths, int Downs, int Routs, int Fled, int Surrendered, int Rallies);

/// <summary>
/// The spike's 10 v 10 (docs/spikes/02-melee.md, the MC-6 baseline 5.5 deaths / 4.7 downs / 12.3 routs) on the shipped fight: Wolves with spears, swords and heaters in padding and mail
/// against Crows with axes in mail, mauls and swords in padding, 14 m apart, skills 30-64 by (seed, side, index), each side led by its most skilled fighter (4.1 g).
/// </summary>
public static class MoraleScenarios
{
    public static FightWorld SpikeSkirmish(FightContent c, ulong seed, int perSide = 10)
    {
        var w = new FightWorld(c, seed, seed, 8 * 3600) { RecordEvents = true };
        var sword = new WeaponInstance("arming_sword", "steel"); var spear = new WeaponInstance("spear", "steel");
        var axe = new WeaponInstance("axe", "steel"); var maul = new WeaponInstance("maul", "steel");
        for (int team = 0; team < 2; team++)
            for (int i = 0; i < perSide; i++)
            {
                int x = team == 0 ? -7000 : 7000, y = (i * 2 - (perSide - 1)) * 700;
                int skill = 30 + (int)(DetRng.NextUInt(seed, 0, (ulong)(team * 100 + i), 7, 0) % 35);
                int face = team == 0 ? FightMath.One : -FightMath.One;
                FighterKit k = team == 0
                    ? i % 3 == 0 ? FightCalibration.KitOf($"Wolf {i + 1}", 0, spear, FightCalibration.Padded, null, skill, x - 600, face)
                      : FightCalibration.KitOf($"Wolf {i + 1}", 0, sword, i % 3 == 1 ? FightCalibration.Mailed : FightCalibration.Padded, "heater", skill, x, face)
                    : i % 4 == 0 ? FightCalibration.KitOf($"Crow {i + 1}", 1, axe, FightCalibration.Mailed, null, skill, x, face)
                      : i % 4 == 1 ? FightCalibration.KitOf($"Crow {i + 1}", 1, maul, FightCalibration.Padded, null, skill, x, face)
                      : FightCalibration.KitOf($"Crow {i + 1}", 1, sword, FightCalibration.Padded, i % 2 == 0 ? "heater" : null, skill, x, face);
                w.Add(k with { Y = y });
            }
        w.AppointCaptains();
        return w;
    }

    /// <summary>Runs a skirmish to its end (at most <paramref name="maxS"/> sim-seconds) and counts.</summary>
    public static SkirmishTally Run(FightWorld w, int maxS = 180)
    {
        int t = 0;
        for (; t < maxS * MoraleRules.TicksPerS && !w.IsOver; t++) w.StepWithAi();
        return new SkirmishTally(t / MoraleRules.TicksPerS, w.IsOver, w.Fighters.Count(f => f.Act == FAct.Dead), w.Fighters.Count(f => f.Act == FAct.Down),
            w.Events.Count(e => e.Kind == FightEventKind.Rout), w.Fighters.Count(f => f.Act == FAct.Fled), w.Fighters.Count(f => f.Act == FAct.Surrendered),
            w.Events.Count(e => e.Kind == FightEventKind.Rally));
    }
}
