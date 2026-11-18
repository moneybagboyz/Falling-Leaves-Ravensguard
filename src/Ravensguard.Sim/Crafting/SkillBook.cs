using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Crafting;

/// <summary>
/// Skills by use (trade-inventory-character spec 4.5): micro-levels for the people whose skills are tracked, so a skill rises by small, exact steps as it is used. A person without a row
/// reads their whole level from <see cref="Base"/> (the person table, or the hero record), and the first use starts the row from that. Gain per use is
/// <c>0.4 x d x (1 - s/100)^2</c> levels in integer micro-levels; no gain when the work is 30 levels too easy, at most 12 uses an hour, half above 800 permille of fatigue, double with a master.
/// </summary>
public sealed class SkillBook
{
    private readonly SortedDictionary<(ulong Person, byte Skill), long> _micro = [];
    private readonly SortedDictionary<(ulong Person, byte Skill), (long Hour, int Uses)> _uses = [];

    /// <summary>The level of a person with no row yet (0 when not set).</summary>
    public Func<EntityId, Skill, int>? Base { get; set; }

    public int Count => _micro.Count;

    public long Micro(EntityId p, Skill s)
        => _micro.TryGetValue((p.Value, (byte)s), out var m) ? m : (Base?.Invoke(p, s) ?? 0) * CraftMath.MicroPerLevel;

    public int Level(EntityId p, Skill s) => (int)(Micro(p, s) / CraftMath.MicroPerLevel);

    public void Set(EntityId p, Skill s, long micro) => _micro[(p.Value, (byte)s)] = Math.Clamp(micro, 0, CraftMath.MaxMicro);

    /// <summary>
    /// One meaningful use of <paramref name="s"/> on work of the given difficulty. Returns the micro-levels gained (0 when too easy, over the cap level, or the hour's twelve uses are spent).
    /// </summary>
    public long Use(EntityId p, Skill s, int dx100, int difficulty, long nowSec, int fatiguePm = 0, bool masterTeaching = false, int capLevel = 100)
    {
        if (s == Skill.None) return 0;
        long cur = Micro(p, s);
        int level = (int)(cur / CraftMath.MicroPerLevel);
        if (level >= capLevel || level > difficulty + CraftMath.TooEasyMargin) return 0;
        var key = (p.Value, (byte)s); long hour = nowSec / 3600;
        (long Hour, int Uses) u = _uses.TryGetValue(key, out var e) && e.Hour == hour ? e : (hour, 0);
        if (u.Uses >= CraftMath.UsesPerHour) return 0;
        _uses[key] = (hour, u.Uses + 1);
        long gain = CraftMath.GainMicro(cur, dx100);
        if (fatiguePm > 800) gain /= 2;
        if (masterTeaching) gain *= 2;
        Set(p, s, cur + gain);
        return gain;
    }

    // ---- save support ----
    public (ulong Person, byte Skill, long Micro)[] MicroRows() => [.. _micro.Select(kv => (kv.Key.Person, kv.Key.Skill, kv.Value))];
    public (ulong Person, byte Skill, long Hour, int Uses)[] UseRows() => [.. _uses.Select(kv => (kv.Key.Person, kv.Key.Skill, kv.Value.Hour, kv.Value.Uses))];
    public void RestoreMicro(ulong person, byte skill, long micro) => _micro[(person, skill)] = micro;
    public void RestoreUse(ulong person, byte skill, long hour, int uses) => _uses[(person, skill)] = (hour, uses);
}
