using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Crafting;

/// <summary>
/// The numbers of crafting (basic-crafting spec 4.2 to 4.6) as pure integer functions: no floats, durations in game seconds, factors in permille, floor division. Everything the screen shows
/// and everything the runner does comes from these, so the two always agree.
/// </summary>
public static class CraftMath
{
    public const int Rolls = 41;       // Range(-20, +20)

    /// <summary>Time factor in permille: 50 above the difficulty is 20 percent faster, 30 below it 12 percent slower.</summary>
    public static int SkillFactorPm(int skill, int difficulty) => 1000 - Math.Clamp(skill - difficulty, -30, 50) * 4;

    /// <summary>Time factor of one proficiency: <c>1 + (mult - 1) x (1000 - Pm)/1000</c>, in permille.</summary>
    public static int ProfTimePm(int timeMultPm, int pm) => 1000 + (timeMultPm - 1000) * (1000 - Math.Clamp(pm, 0, 1000)) / 1000;

    /// <summary>Skill points lost below full proficiency: <c>penalty x (1000 - Pm)/1000</c>.</summary>
    public static int ProfSkillPenalty(int penalty, int pm) => penalty * (1000 - Math.Clamp(pm, 0, 1000)) / 1000;

    /// <summary>Chance of failure at the critical step in permille: <c>100 + 20 x (difficulty - skill)</c>, clamped to 0..800, and none from five levels over the difficulty.</summary>
    public static int FailPm(int skill, int difficulty) => skill >= difficulty + 5 ? 0 : Math.Clamp(100 + 20 * (difficulty - skill), 0, 800);

    /// <summary>Share of the components a failure wastes, percent: <c>30 + (difficulty - skill)</c> clamped to 30..100.</summary>
    public static int WastePct(int skill, int difficulty) => Math.Clamp(30 + (difficulty - skill), 30, 100);

    /// <summary>Goods (food, bundles) have no tier; they scale yield by quality: 700, 850, 1000, 1000, 1000 permille.</summary>
    public static int YieldPm(ItemQuality q) => q switch { ItemQuality.Crude => 700, ItemQuality.Poor => 850, _ => 1000 };

    /// <summary>How many of the 41 rolls land in each tier, for <c>score = effectiveSkill + roll + bonus</c> (the expected quality shown on the screen).</summary>
    public static int[] QualityCounts(int effectiveSkill, int toolBonus, int otherBonus, int tierDrop = 0)
    {
        var n = new int[5];
        for (int roll = -20; roll <= 20; roll++)
            n[(int)Drop(ItemQualityRules.FromScore(effectiveSkill + roll + Math.Clamp(toolBonus, -10, 10) + otherBonus), tierDrop)]++;
        return n;
    }

    public static ItemQuality Drop(ItemQuality q, int tiers) => (ItemQuality)Math.Max(0, (int)q - Math.Max(0, tiers));

    /// <summary>Seconds of one step: base x proficiencies x tool (1500 for an improvised tool) x skill, all permille, rounded half up.</summary>
    public static long StepSeconds(long baseSec, int profPm, int toolPm, int skillPm)
        => (baseSec * profPm * toolPm * skillPm + 500_000_000L) / 1_000_000_000L;

    /// <summary>A batch of <paramref name="n"/> takes <c>1 + (n - 1) x scale</c> times as long for the attended steps.</summary>
    public static long BatchSeconds(long sec, int n, int scalePm) => sec * (1000 + (Math.Max(1, n) - 1) * scalePm) / 1000;

    // ---- the unattended window (spec 4.5) ----

    /// <summary>Tier lost for collecting at <paramref name="now"/>: 0 until the deadline, 1 in the first half of the grace window, 2 in the second; -1 once ruined.</summary>
    public static int LateTiers(long now, long deadlineSec, long ruinSec)
    {
        if (now > ruinSec) return -1;
        if (now <= deadlineSec) return 0;
        return (now - deadlineSec) * 2 <= ruinSec - deadlineSec ? 1 : 2;
    }

    // ---- skills by use (trade spec 4.5) ----

    public const long MicroPerLevel = 1_000_000, MaxMicro = 100 * MicroPerLevel;
    public const int UsesPerHour = 12, TooEasyMargin = 30;

    /// <summary>Gain of one use in micro-levels: <c>d_x100 x t^2 / 25,000</c> with <c>t = (100,000,000 - s)/10,000</c>; this is <c>0.4 x d x (1 - s/100)^2</c> levels.</summary>
    public static long GainMicro(long skillMicro, int dx100)
    {
        long t = (MaxMicro - Math.Clamp(skillMicro, 0, MaxMicro)) / 10_000;
        return dx100 * t * t / 25_000;
    }

    /// <summary>Weight of a use of a recipe: <c>max(25, difficulty) x 2</c> (a 25-difficulty recipe is 50, the hardest 200).</summary>
    public static int UseWeight(int difficulty) => Math.Max(25, difficulty) * 2;

    // ---- learning (spec 4.6), hours in tenths ----

    public static int LearnHoursX10(int difficulty) => 40 + difficulty;
    public static int WatchHoursX10(int difficulty) => 60 + difficulty;
    public static int ManuscriptHoursX10(int difficulty) => 20 + difficulty;
    /// <summary>Cost of a master's session at the skilled wage, rounded half up.</summary>
    public static long TeachCostMites(int difficulty, int wagePerHour = 35) => (LearnHoursX10(difficulty) * (long)wagePerHour + 5) / 10;
    public static int WatchProbPm(int wits, int skill) => Math.Min(950, 400 + 5 * wits + 3 * skill);
    /// <summary>A master must know the recipe at <c>difficulty + 10</c>.</summary>
    public static bool CanTeach(int masterSkill, int difficulty) => masterSkill >= difficulty + 10;
    /// <summary>Taking apart teaches the recipe from <c>difficulty - 10</c>.</summary>
    public static bool CanDisassemble(int skill, int difficulty) => skill >= difficulty - 10;
    /// <summary>What a disassembly gives back of one recorded component, grams: 70 percent, floor.</summary>
    public static long ReturnG(long grams) => grams * 70 / 100;
}
