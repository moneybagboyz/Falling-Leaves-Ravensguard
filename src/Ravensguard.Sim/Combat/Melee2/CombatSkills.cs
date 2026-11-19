using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// The weapon-family skills (melee-and-ranged M1, overview Q4): Blades, Axes, Hafted, Polearms, Shields and Throwing, appended to <see cref="Skill"/>; Melee stays for bare hands
/// and Archery for bows. Anyone who has never trained a family reads it as 80% of their Melee (the v6 save migration writes the same into the hero's record and skill book).
/// </summary>
public static class CombatSkills
{
    public const int MigratePm = 800;

    public static readonly Skill[] Families = [Skill.Blades, Skill.Axes, Skill.Hafted, Skill.Polearms, Skill.Shields, Skill.Throwing];

    /// <summary>The skill a weapon family trains (<see cref="FightWorld.FamilyOf"/>): blades, axes, hafted, polearms; archery for launchers; melee for bare hands and natural weapons.</summary>
    public static Skill Of(string family) => family switch
    {
        "blades" => Skill.Blades, "axes" => Skill.Axes, "hafted" => Skill.Hafted, "polearms" => Skill.Polearms, "archery" => Skill.Archery, "improvised" => Skill.Melee, _ => Skill.Melee,
    };

    /// <summary>A family's level from a reader of stored levels: the stored level, or 80% of Melee when the family was never set.</summary>
    public static int Level(Func<Skill, int> stored, Skill s)
    {
        int v = stored(s);
        if (v > 0 || Array.IndexOf(Families, s) < 0) return v;
        return stored(Skill.Melee) * MigratePm / 1000;
    }

    /// <summary>A person row's level of a combat skill (the people table has a few skill slots; families fall back to Melee x0.8).</summary>
    public static int Level(PersonTable t, int row, Skill s) => Level(k => t.SkillLevelOf(row, k), s);
}
