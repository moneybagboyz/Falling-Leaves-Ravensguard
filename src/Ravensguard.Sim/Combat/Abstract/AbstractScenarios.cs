using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>
/// The equivalence scenarios of abstract-battles-v1 4.4 as fighter kits (one list per seed), fought either live (<see cref="Tactical"/>: a headless <see cref="FightWorld"/> with the
/// real brain, morale and mounted layer) or abstractly (<see cref="AbstractBattle"/> built from the same kits). Skills vary +-5 by (seed, side, index) around the scenario's level,
/// weapons alternate as the spec lists them.
/// <list type="bullet">
/// <item>A: 20 militia (gambeson, spear and club, skill 30) against 20 bandits (leather, sword and axe, skill 40), lines 14 m apart.</item>
/// <item>B: 30 men-at-arms (mail over padding, sword and heater, skill 60) against 50 levy (gambeson, spear, skill 30).</item>
/// <item>C: 20 longbowmen and 10 spears (padded) against 25 raiders charging (leather, axe and sword) over 60 m of open ground.</item>
/// <item>D: 12 lancers on coursers (mail, heater) against 20 sword-and-heater foot (padded, no polearm) on dry field (the charge) and on mud (<see cref="Scenario.DMud"/>: no charge).</item>
/// </list>
/// </summary>
public static class AbstractScenarios
{
    public enum Scenario : byte { A, B, C, D, DMud, AFlanked, ANoLeader }

    public static readonly Scenario[] Gate = [Scenario.A, Scenario.B, Scenario.C, Scenario.D, Scenario.DMud];

    private static readonly WornItem[] Padded = [new("tunic", "linen"), new("gambeson", "linen")];
    private static readonly WornItem[] Leather = [new("tunic", "wool"), new("cuir_bouilli", "leather"), new("leather_bracers", "leather"), new("cap", "leather")];
    private static readonly WornItem[] Mailed = [new("tunic", "linen"), new("gambeson", "linen"), new("mail_hauberk", "iron"), new("helm", "iron")];

    private static int Skill(ulong seed, int side, int i, int level) => Math.Clamp(level - 5 + (int)(DetRng.NextUInt(seed, 0, (ulong)(side * 1000 + i), 7, 1) % 11), 0, 100);

    private static FighterKit Foot(string name, int team, WeaponInstance? w, WornItem[] worn, string? shield, int skill, int x, int y) =>
        new() { Name = name, Team = team, Weapon = w, Worn = worn, Shield = shield, Skill = skill, X = x, Y = y, FaceX = team == 0 ? FightMath.One : -FightMath.One, Courage = 70 };

    /// <summary>The scenario's two sides as kits (team 0 first), with the mounted riders flagged in <paramref name="mounted"/> (indices into the list).</summary>
    public static List<FighterKit> Kits(Scenario s, ulong seed, out HashSet<int> mounted, out FightTerrain? terrain)
    {
        var k = new List<FighterKit>();
        mounted = []; terrain = null;
        switch (s)
        {
            case Scenario.A:
            case Scenario.AFlanked:
            case Scenario.ANoLeader:
                for (int i = 0; i < 20; i++)
                    k.Add(Foot($"Militia {i + 1}", 0, i % 2 == 0 ? new WeaponInstance("spear", "iron") : new WeaponInstance("club", "oak"), Padded, null, Skill(seed, 0, i, 30),
                        i % 2 == 0 ? -7600 : -7000, (i - 10) * 1400 + 700));
                for (int i = 0; i < 20; i++)
                {
                    int y = (i - 10) * 1400 + 700, x = 7000;
                    if (s == Scenario.AFlanked && i >= 14) { x = -7000 + 2000 * (i - 14); y = -16_000 - 1200 * (i % 2); }     // six bandits come round the militia's left
                    k.Add(Foot($"Bandit {i + 1}", 1, i % 2 == 0 ? new WeaponInstance("arming_sword", "iron") : new WeaponInstance("axe", "iron"), Leather, null, Skill(seed, 1, i, 40), x, y));
                }
                break;
            case Scenario.B:
                for (int i = 0; i < 30; i++)
                    k.Add(Foot($"Man-at-arms {i + 1}", 0, new WeaponInstance("arming_sword", "steel"), Mailed, "heater", Skill(seed, 0, i, 60), -7000 - (i / 15) * 1500, (i % 15 - 7) * 1300));
                for (int i = 0; i < 50; i++)
                    k.Add(Foot($"Levy {i + 1}", 1, new WeaponInstance("spear", "iron"), Padded, null, Skill(seed, 1, i, 30), 7000 + (i / 25) * 1500, (i % 25 - 12) * 900));
                break;
            case Scenario.C:
                for (int i = 0; i < 10; i++)
                    k.Add(Foot($"Spear {i + 1}", 0, new WeaponInstance("spear", "iron"), Padded, null, Skill(seed, 0, i, 40), -27_000, (i - 5) * 1500 + 750));
                for (int i = 0; i < 20; i++)
                    k.Add(Foot($"Archer {i + 1}", 0, null, Padded, null, Skill(seed, 0, 100 + i, 45), -31_000 - (i % 2) * 1000, (i - 10) * 1100 + 550)
                        with { Launcher = "longbow", Ammo = "bodkin_arrow", AmmoCount = 24, ArcherySkill = Skill(seed, 0, 100 + i, 45) });
                for (int i = 0; i < 25; i++)
                    k.Add(Foot($"Raider {i + 1}", 1, i % 2 == 0 ? new WeaponInstance("axe", "iron") : new WeaponInstance("arming_sword", "iron"), Leather, null, Skill(seed, 1, i, 40), 33_000, (i - 12) * 1300)
                        with { Stance = "aggressive" });
                break;
            case Scenario.D:
            case Scenario.DMud:
                for (int i = 0; i < 12; i++)
                {
                    int skill = 30 + (int)(DetRng.NextUInt(seed, 0, (ulong)i, 7, 0) % 35);
                    k.Add(Foot($"Rider {i + 1}", 0, MountedCalibration.Lance, FightCalibration.Mailed.Worn.ToArray(), "heater", skill, -40_000, (i * 2 - 11) * 1500));
                    mounted.Add(i);
                }
                for (int i = 0; i < 20; i++)
                {
                    int skill = 30 + (int)(DetRng.NextUInt(seed, 0, (ulong)(100 + i), 7, 0) % 35);
                    k.Add(Foot($"Foot {i + 1}", 1, FightCalibration.Sword, FightCalibration.Padded.Worn.ToArray(), "heater", skill, 0, (i * 2 - 19) * 800));
                }
                terrain = new FightTerrain { Base = s == Scenario.DMud ? Footing.Mud : Footing.Field };
                break;
        }
        return k;
    }

    /// <summary>The riders' mount (D): a courser at Riding 40 + skill/3, as MT-12's skirmish.</summary>
    public static MountKit MountFor(FighterKit rider) => new() { Riding = 40 + rider.Skill / 3 };

    /// <summary>The live fight of a scenario and seed: every kit added (riders on coursers), the terrain set, captains appointed (morale 4.1 g) unless the scenario has none.</summary>
    public static FightWorld Tactical(FightContent c, Scenario s, ulong seed)
    {
        var kits = Kits(s, seed, out var mounted, out var terrain);
        var w = new FightWorld(c, seed, seed, 8 * 3600) { RecordEvents = false };
        for (int i = 0; i < kits.Count; i++)
        {
            if (mounted.Contains(i)) w.AddMounted(kits[i], MountFor(kits[i]));
            else w.Add(kits[i]);
        }
        if (terrain != null) w.UseTerrain(terrain);
        if (s != Scenario.ANoLeader) w.AppointCaptains();
        return w;
    }
}
