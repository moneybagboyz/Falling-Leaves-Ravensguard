using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Soldiers;

/// <summary>
/// P7-SC-B (soldiers-and-companies 4.4): skills by fighting and drilling, no experience levels. Every gain is a <see cref="Progress.Use"/> over the soldiers' <see cref="SoldierBook.Skills"/> (the Phase 3 rule: micro-levels,
/// twelve uses an hour, nothing when the work is 30 levels too easy, nothing past the practice's cap). Integer only; no draw. Formation drill is a unit's 0..1000 that the battle spec reads.
/// </summary>
public static class Growth
{
    private static EntityId Id(ulong p) => new(p);

    /// <summary>
    /// A fight's meaningful uses of one weapon family (blows landed or blocked, parries, hits), counted up to the rules' most a fight holds and spread over the fight's hours from <paramref name="startSec"/>, twelve an hour.
    /// Each is difficulty <paramref name="opponentSkill"/>. Returns the micro-levels gained.
    /// </summary>
    public static long Fight(SoldierWorld sw, ulong person, Skill family, int opponentSkill, int uses, long startSec)
    {
        var r = sw.Roles.Rules; long gain = 0;
        var use = new SkillUse("fight_" + family, family, r.FightDx100);
        int n = Math.Min(uses, r.FightUsesPerFight);
        for (int i = 0; i < n; i++) gain += Progress.Use(sw.Book.Skills, Id(person), use, opponentSkill, startSec + (long)(i / 12) * 3600).GainMicro;
        return gain;
    }

    /// <summary>
    /// One game day's drill of a weapon family: at most the rules' hours a day (2), each hour the rules' uses at difficulty 40 and cap 40 (35 for the shield). The row's drill hours count whole hours drilled.
    /// Returns the hours that counted.
    /// </summary>
    private static readonly Dictionary<Skill, (string Use, string Name)> DrillNames = Enum.GetValues<Skill>().ToDictionary(k => k, k => ("drill_" + k, k.ToString()));
    private static (string Use, string Name) Names(Skill f) => DrillNames.TryGetValue(f, out var n) ? n : ("drill_" + f, f.ToString());

    public static int Drill(SoldierWorld sw, ulong person, Skill family, int hours, int day)
    {
        var r = sw.Roles.Rules; int h = Math.Clamp(hours, 0, r.DrillHoursDay);
        var (useName, famName) = Names(family);
        var use = new SkillUse(useName, family, r.DrillDx100, family == Skill.Shields ? r.DrillShieldCap : r.DrillCap);
        for (int k = 0; k < h; k++)
            for (int u = 0; u < r.DrillUsesPerHour; u++) Progress.Use(sw.Book.Skills, Id(person), use, 40, (long)day * 86400 + (8 + k) * 3600);
        if (h > 0 && sw.Book.Of(person) is { } row)
        {
            int before = row.DrillHours.Values.Sum(); row.DrillHours.TryGetValue(famName, out int n); row.DrillHours[famName] = n + h;
            GrowAttribute(sw, person, before, before + h, r.DrillHoursDay);
        }
        return h;
    }

    /// <summary>P8-LW-4: each 30 drill days (hours drilled over the rules' hours a day, all families) adds 1 Strength, then 1 Toughness, then Strength again, to the cap of the attribute table (100). Integer, no draw.</summary>
    public const int DrillDaysPerPoint = 30, AttrCap = 100;
    private static void GrowAttribute(SoldierWorld sw, ulong person, int hoursBefore, int hoursAfter, int hoursDay)
    {
        int per = Math.Max(1, DrillDaysPerPoint * Math.Max(1, hoursDay)), crossed = hoursAfter / per - hoursBefore / per;
        if (crossed <= 0 || sw.People() is not { } pt) return;
        int row = pt.IndexOf(new EntityId(person)); if (row < 0) return;
        for (int k = hoursBefore / per + 1; k <= hoursAfter / per; k++)
        {
            int a = row * PersonTable.AttrCount + (k % 2 == 1 ? PersonTable.StrengthIdx : PersonTable.ToughnessIdx);
            if (pt.Attr[a] < AttrCap) pt.Attr[a]++;
        }
    }

    /// <summary>
    /// Sparring with a better partner (<c>SparBout</c>): trains to the partner's skill less the margin, at most the ceiling (60); a partner no better teaches nothing. Same hours a day as drill. Returns the hours that counted.
    /// </summary>
    public static int Spar(SoldierWorld sw, ulong person, Skill family, int partnerLevel, int hours, int day)
    {
        var r = sw.Roles.Rules; int h = Math.Clamp(hours, 0, r.DrillHoursDay);
        int cap = Math.Min(partnerLevel - r.SparMargin, r.SparMaxLevel);
        var use = new SkillUse("spar_" + family, family, r.SparDx100, cap);
        for (int k = 0; k < h; k++)
            for (int u = 0; u < r.SparUsesPerHour; u++) Progress.Use(sw.Book.Skills, Id(person), use, partnerLevel, (long)day * 86400 + (13 + k) * 3600);
        return h;
    }

    // ---------------------------------------------------------------- formation drill

    /// <summary>Hours in formation add the rules' gain to a unit's drill (0..1000).</summary>
    public static int FormationHours(SoldierWorld sw, long unit, int hours, int day)
    {
        if (hours <= 0) return sw.Book.Units.TryGetValue(unit, out var u0) ? u0.Value : 0;
        if (!sw.Book.Units.TryGetValue(unit, out var u)) sw.Book.Units[unit] = u = new UnitDrill { LastDay = day };
        u.Value = Math.Clamp(u.Value + hours * sw.Roles.Rules.FormationHourGain, 0, 1000); u.LastDay = day;
        return u.Value;
    }

    /// <summary>The day's loss: every unit that did not drill on <paramref name="day"/> loses the rules' points (to 0); a unit at 0 is dropped from the book.</summary>
    public static void FormationDay(SoldierWorld sw, int day)
    {
        foreach (var k in sw.Book.Units.Keys.ToList())
        {
            var u = sw.Book.Units[k]; if (u.LastDay >= day) continue;
            u.Value = Math.Max(0, u.Value - sw.Roles.Rules.FormationDayLoss);
            if (u.Value == 0) sw.Book.Units.Remove(k);
        }
    }

    public static int DrillOf(SoldierWorld sw, long unit) => sw.Book.Units.TryGetValue(unit, out var u) ? u.Value : 0;

    /// <summary>Ticks a man waits to close a gap in his rank: base less drill / div, at least 1 (battle spec 4.3: 40 at drill 0, 28 at 300, 16 at 600).</summary>
    public static int CloseRanksTicks(TroopRoles roles, int drill) => Math.Max(1, roles.Rules.CloseRanksBaseTicks - drill / Math.Max(1, roles.Rules.CloseRanksDrillDiv));
}
