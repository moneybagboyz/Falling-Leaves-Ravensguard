using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>
/// The first of Phase 7's new scenarios (abstract-war-and-calibration 4.5), as kits for v2: <b>E</b> two foot lines of 200, the wider by 40% (the flank wrap), <b>F</b> 60 knights charge 200
/// unbraced levies, <b>G</b> 60 knights charge 150 braced pikes in a schiltron. Every man is a named person (for the prisoner and loot rows). They are separate from
/// <see cref="AbstractScenarios.Scenario"/> so the Phase 4 loops (and their pinned hashes) do not meet them; H, I and the live references are AW-C's.
/// </summary>
public static class AbstractWarScenarios
{
    public enum Scenario : byte { E, F, G }

    private static int Skill(ulong seed, int side, int i, int level) => Math.Clamp(level - 5 + (int)(DetRng.NextUInt(seed, 0, (ulong)(side * 1000 + i), 7, 1) % 11), 0, 100);

    private static FighterKit Man(string name, int team, int i, WeaponInstance w, WornItem[] worn, string? shield, int skill, int x, int y) =>
        new() { Name = name, Team = team, Person = (ulong)(team * 100_000 + i + 1), Weapon = w, Worn = worn, Shield = shield, Skill = skill, X = x, Y = y, FaceX = team == 0 ? FightMath.One : -FightMath.One, Courage = 70 };

    public static BattleInput InputOf(Scenario s, ulong seed)
    {
        var kits = new List<FighterKit>();
        var mounted = new HashSet<int>();
        var padded = FightCalibration.Padded.Worn.ToArray();
        switch (s)
        {
            case Scenario.E:
                for (int i = 0; i < 200; i++)             // 40 files, 5 ranks, 1.2 m a file
                    kits.Add(Man($"Left {i + 1}", 0, i, FightCalibration.Sword, padded, "heater", Skill(seed, 0, i, 40), -4000 - i / 40 * 1000, (i % 40 - 20) * 1200 + 600));
                for (int i = 0; i < 200; i++)             // 56 files: 40% wider
                    kits.Add(Man($"Right {i + 1}", 1, i, FightCalibration.Sword, padded, "heater", Skill(seed, 1, i, 40), 4000 + i / 56 * 1000, (i % 56 - 28) * 1200 + 600));
                break;
            case Scenario.F:
            case Scenario.G:
                for (int i = 0; i < 60; i++)
                {
                    mounted.Add(i);
                    kits.Add(Man($"Knight {i + 1}", 0, i, MountedCalibration.Lance, FightCalibration.Mailed.Worn.ToArray(), "heater", 50 + i % 15, -40_000 - i / 30 * 2000, (i % 30 - 15) * 1500 + 750));
                }
                if (s == Scenario.F)
                    for (int i = 0; i < 200; i++)         // unbraced levies: axes, no polearm to brace
                        kits.Add(Man($"Levy {i + 1}", 1, i, new WeaponInstance("axe", "iron"), padded, null, Skill(seed, 1, i, 30), i / 40 * 1000, (i % 40 - 20) * 1000 + 500));
                else
                    for (int i = 0; i < 150; i++)         // the schiltron: 15 files by 10 ranks, 0.7 m apart, spears braced
                        kits.Add(Man($"Pike {i + 1}", 1, i, FightCalibration.Spear, padded, null, Skill(seed, 1, i, 35), i / 15 * 700, (i % 15 - 7) * 700));
                break;
        }
        return new BattleInput(kits, seed, seed, 8 * 3600, "scenario " + s) { Mounted = mounted, Leaders = true, FirstAid = false, SideKinds = new Dictionary<int, SideKind> { [0] = SideKind.Army, [1] = SideKind.Army } };
    }
}
