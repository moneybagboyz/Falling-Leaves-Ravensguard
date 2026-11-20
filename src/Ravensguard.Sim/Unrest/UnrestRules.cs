using Ravensguard.Content;
using Ravensguard.Sim.Taxes;

namespace Ravensguard.Sim.Unrest;

/// <summary>What bears on one household today besides the event rows (unrest-and-revolt 3): the conditions the economy and the rule already track and the settlement-wide ones.</summary>
public readonly record struct HouseholdInputs(bool Hungry, TaxLevel Level, int LevyPoints, bool Crowded, bool CrimeVictim, bool StandingLow, bool Banned, bool Slavery, bool Dole);

/// <summary>
/// The pure rules of unrest (unrest-and-revolt 3 to 4.3): a household's points by kind, the aggrieved line, a settlement's unrest in permille, the ladder's rungs, the riot's crowd and the revolt's triggers. Integer only,
/// no state; the engine and the golden tests call these.
/// </summary>
public static class UnrestRules
{
    /// <summary>
    /// A household's grievances by kind today (kind, points), in the content's order, and the total capped at <see cref="UnrestSettingsDef.PointsCap"/> (UR-1). Hunger is the economy's flag (cleared while a dole lasts); the tax level
    /// gives High 1 and Crushing 2; the levy rows of the rule book (a subsidy 1, a tallage 2) count as <c>levy</c>; the rest are the event rows still live and the settlement's conditions.
    /// </summary>
    public static List<(string Kind, int Points)> Breakdown(UnrestBook b, in HouseholdInputs i, IEnumerable<GrievanceRow> rows, out int total)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        void Put(string kind, int pts) { if (pts > 0 && b.Kinds.TryGetValue(kind, out _)) map[kind] = Math.Max(map.GetValueOrDefault(kind), pts); }
        int P(string kind) => b.Kinds.TryGetValue(kind, out var d) ? d.Points : 0;
        if (i.Hungry && !i.Dole) Put("hunger", P("hunger"));
        if (i.Level == TaxLevel.Crushing) Put("tax_crushing", P("tax_crushing")); else if (i.Level == TaxLevel.High) Put("tax_high", P("tax_high"));
        if (i.LevyPoints > 0) Put("levy", Math.Min(i.LevyPoints, 2));
        if (i.Crowded) Put("crowding", P("crowding"));
        if (i.CrimeVictim) Put("crime", P("crime"));
        if (i.StandingLow) Put("standing", P("standing"));
        if (i.Banned) Put("banned", P("banned"));
        if (i.Slavery) Put("slavery", P("slavery"));
        foreach (var r in rows) Put(r.Kind, r.Points);
        var list = new List<(string, int)>();
        int sum = 0;
        foreach (var d in b.KindList) if (map.TryGetValue(d.Id, out int pts)) { list.Add((d.Id, pts)); sum += pts; }
        total = Math.Min(sum, b.Settings.PointsCap);
        return list;
    }

    public static bool Aggrieved(UnrestSettingsDef s, int points) => points >= s.AggrievedAt;

    /// <summary>Unrest 0..1000 of a settlement: aggrieved households over households (at least <see cref="UnrestSettingsDef.MinHouseholds"/>), less the fear that holds it after a lost revolt (UR-2).</summary>
    public static int UnrestPm(UnrestSettingsDef s, int aggrieved, int households, bool fear)
    {
        if (households <= 0) return 0;
        int n = Math.Max(households, s.MinHouseholds);
        int u = (int)Math.Min(1000, ((long)aggrieved * 1000 + n / 2) / n);
        return fear ? Math.Max(0, u - s.FearDrop) : u;
    }

    /// <summary>The rung a settlement's unrest reaches: 0 quiet, 1 grumbling, 2 evasion, 3 riot, 4 revolt.</summary>
    public static int RungFor(UnrestSettingsDef s, int unrest) => unrest >= s.Rung4 ? 4 : unrest >= s.Rung3 ? 3 : unrest >= s.Rung2 ? 2 : unrest >= s.Rung1 ? 1 : 0;

    /// <summary>A riot's crowd as a percent of the aggrieved adults: 10 at 450, 40 at 800, linear between and flat beyond.</summary>
    public static int CrowdPct(UnrestSettingsDef s, int unrest)
    {
        if (unrest <= s.CrowdFrom) return s.CrowdMinPct;
        if (unrest >= s.CrowdTo) return s.CrowdMaxPct;
        return s.CrowdMinPct + (s.CrowdMaxPct - s.CrowdMinPct) * (unrest - s.CrowdFrom) / Math.Max(1, s.CrowdTo - s.CrowdFrom);
    }

    /// <summary>The crowd's expected size: its percent of the aggrieved adults, at least <see cref="UnrestSettingsDef.CrowdMinPeople"/> (and never more than there are).</summary>
    public static int CrowdSize(UnrestSettingsDef s, int aggrievedAdults, int unrest) => Math.Min(aggrievedAdults, Math.Max(s.CrowdMinPeople, aggrievedAdults * CrowdPct(s, unrest) / 100));

    /// <summary>Whether a riot starts today: at the riot rung for <see cref="UnrestSettingsDef.RiotDays"/> days, or a trigger struck at <see cref="UnrestSettingsDef.TriggerAt"/> unrest or more (UR-2).</summary>
    public static bool RiotDue(UnrestSettingsDef s, int unrest, int highDays, bool trigger) => (unrest >= s.Rung3 && highDays >= s.RiotDays) || (trigger && unrest >= s.TriggerAt);

    /// <summary>Whether a revolt is raised: the revolt rung held for two seasons, or a riot put down with enough deaths at enough unrest (UR-6).</summary>
    public static bool RevoltDue(UnrestSettingsDef s, int unrest, int topDays, int riotDeaths) =>
        (unrest >= s.Rung4 && topDays >= s.RevoltSeasons * Ravensguard.Sim.Core.TimeConst.DaysPerSeason) || (riotDeaths >= s.RevoltDeaths && unrest >= s.RevoltDeathsAt);

    /// <summary>The target a grievance kind sends a crowd to (the content's <c>target</c>).</summary>
    public static RiotTarget TargetOf(string target) => target switch { "barn" => RiotTarget.Barn, "reeve" => RiotTarget.Reeve, "gaol" => RiotTarget.Gaol, _ => RiotTarget.Manor };

    /// <summary>The battle's strength of a side: its men weighted, with a variance of plus or minus <paramref name="varPct"/> by a draw in 0..2*varPct.</summary>
    public static long Strength(long men, int varDraw, int varPct) => men * (100 - varPct + varDraw);
}
