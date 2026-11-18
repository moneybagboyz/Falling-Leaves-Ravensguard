using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Trade;

public enum UseOutcome : byte { Gained, TooEasy, CappedThisHour, AtCap, NoSkill }

/// <summary>The meaningful uses the systems name (spec 3.4 <c>uses</c>): skill, difficulty x100 and the level a practice stops at.</summary>
public readonly record struct SkillUse(string Id, Skill Skill, int DifficultyX100, int CapLevel = 100);

/// <summary>
/// Skills by use and attributes by activity (spec 4.5). Skill gain is the crafting spec's <see cref="SkillBook"/> (micro-levels, <c>gain = d x t x t / 25,000</c>,
/// 12 uses an hour, 30 levels too easy); this class names the uses and reports why a use did not count. Attributes grow 20 milli-points per activity day, half above 70.
/// </summary>
public static class Progress
{
    public static readonly SkillUse TradeDeal = new("trade_deal", Skill.Trading, 60);
    public static readonly SkillUse RideGallopMinute = new("ride_gallop_minute", Skill.Riding, 80);
    public static readonly SkillUse DummyHit = new("dummy_hit", Skill.Melee, 40, 30);
    public static readonly SkillUse Talk = new("talk", Skill.Speech, 40);
    public static readonly SkillUse Lead = new("lead_day", Skill.Leadership, 60);

    /// <summary>One use with its outcome. <paramref name="taskDifficulty"/> is the level the work asks for (a deal at the market asks about 40).</summary>
    public static (UseOutcome Outcome, long GainMicro) Use(SkillBook book, EntityId person, in SkillUse use, int taskDifficulty, long nowSec, int fatiguePm = 0, bool master = false)
    {
        if (use.Skill == Skill.None) return (UseOutcome.NoSkill, 0);
        int level = book.Level(person, use.Skill);
        if (level >= use.CapLevel) return (UseOutcome.AtCap, 0);
        if (level > taskDifficulty + CraftMath.TooEasyMargin) return (UseOutcome.TooEasy, 0);
        long g = book.Use(person, use.Skill, use.DifficultyX100, taskDifficulty, nowSec, fatiguePm, master, use.CapLevel);
        return g > 0 ? (UseOutcome.Gained, g) : (UseOutcome.CappedThisHour, 0);
    }

    /// <summary><c>t = (100,000,000 - s) / 10,000</c>, <c>gain = d x t x t / 25,000</c> (T-1: s = 20 levels, d = 100 gives t = 8,000 and 256,000).</summary>
    public static long GainMicro(long skillMicro, int dx100) => CraftMath.GainMicro(skillMicro, dx100);

    public const int AttrGainMilliPerDay = 20, AttrSoftCap = 70, AttrMinMilli = -5000;

    /// <summary>One activity day of an attribute (4 or more hours of matching work): +20 milli-points, +10 at or above 70 (the byte attribute plus the milli delta).</summary>
    public static int AttrDay(int baseAttr, int milli)
    {
        int now = baseAttr + milli / 1000;
        return milli + (now >= AttrSoftCap ? AttrGainMilliPerDay / 2 : AttrGainMilliPerDay);
    }

    /// <summary>The attribute as used: the floor of <c>base + milli / 1000</c>, 1..100.</summary>
    public static int Effective(int baseAttr, int milli) => Math.Clamp(baseAttr + FloorDiv(Math.Max(AttrMinMilli, milli), 1000), 1, 100);

    private static int FloorDiv(int n, int d) { int q = n / d; return n % d != 0 && (n < 0) != (d < 0) ? q - 1 : q; }
}
