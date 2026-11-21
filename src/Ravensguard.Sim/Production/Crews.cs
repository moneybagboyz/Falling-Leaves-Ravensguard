using Ravensguard.Content;

namespace Ravensguard.Sim.Production;

/// <summary>A person's role on a batch (economies-of-scale 3): derived each hour from skill and age, never saved.</summary>
public enum CrewRole : byte { Master, Journeyman, Apprentice, Hand }

/// <summary>A recipe's worker-hours split by grade, in permille summing 1,000 (3): the master's steps, the journeyman's steps, the apprentice's and hand's steps.</summary>
public readonly record struct Grades(int M, int J, int A)
{
    public static readonly Grades Plain = new(0, 0, 1000);
    /// <summary>Whether the recipe divides its labour at all (any master or journeyman steps).</summary>
    public bool Divided => M + J > 0;
}

/// <summary>
/// Division of labour, pure rules (P7-ES1, economies-of-scale 4.1, 5.1, 5.2): roles, the elapsed time and cost of a batch by crew, the master's reach, a station's crowding, the span of control. Integer arithmetic only;
/// <see cref="ProductionSystem"/> applies them hour by hour (<see cref="Rate"/>), the tests and the Crew view read the closed forms.
/// </summary>
public static class Crews
{
    /// <summary>The role of a person at a batch: a master (skill at least the master's and an adult), a journeyman (the journeyman's skill up to it), an apprentice (under 16), else a hand.</summary>
    public static CrewRole RoleOf(ScaleSettingsDef s, int skill, int ageYears)
    {
        if (ageYears < 16) return CrewRole.Apprentice;
        if (skill >= s.MasterSkill) return CrewRole.Master;
        if (skill >= s.JourneymanSkill) return CrewRole.Journeyman;
        return CrewRole.Hand;
    }

    /// <summary>The wage class a role is paid at, never above the recipe's own class (the price solver's labour cost stays the ceiling): master, skilled, unskilled, unskilled (an apprentice under 16 earns half of it).</summary>
    public static WageClass WageOf(CrewRole role, WageClass recipe)
    {
        var by = role switch { CrewRole.Master => WageClass.Master, CrewRole.Journeyman => WageClass.Skilled, _ => WageClass.Unskilled };
        return (WageClass)Math.Min((int)by, (int)recipe);
    }

    /// <summary>How many stations a master serves the M steps of: <c>2 + Leadership / 30</c>, at most 4 (4.1, ES-3).</summary>
    public static int Reach(ScaleSettingsDef s, int leadership) => Math.Min(s.ReachMax, s.ReachBase + Math.Max(0, leadership) / Math.Max(1, s.ReachPerLeadership));

    /// <summary>How many workers an overseer oversees: <c>6 + Leadership / 10</c>, at most 16 (5.2).</summary>
    public static int Span(ScaleSettingsDef s, int leadership) => Math.Min(s.SpanMax, s.SpanBase + Math.Max(0, leadership) / Math.Max(1, s.SpanPerLeadership));

    /// <summary>A crew larger than its overseers' summed span works at <c>max(floor, 1,000 - 25 x excess)</c> permille (5.2, ES-6); within the span, 1,000.</summary>
    public static int SpeedPm(ScaleSettingsDef s, int workers, int spanSum)
    {
        int excess = Math.Max(0, workers - spanSum);
        return Math.Max(s.SpanFloorPm, 1000 - s.SpanPerExcessPm * excess);
    }

    /// <summary>Effective workers at a station in permille: <c>1,000 x min(n, k) + 500 x min(max(0, n - k), k)</c> (5.1, ES-7: 5 at an anvil of k 3 is 4,000).</summary>
    public static int EffectivePm(int n, int k) => 1000 * Math.Min(n, k) + 500 * Math.Min(Math.Max(0, n - k), k);

    /// <summary>The weight in permille of the worker standing <paramref name="position"/> (0-based) at a station of crew limit k: full up to k, half to 2k, none beyond.</summary>
    public static int SlotPm(int position, int k) => position < k ? 1000 : position < 2 * k ? 500 : 0;

    /// <summary>
    /// The elapsed working minutes of one batch (4.1): <c>T = max(H_M / n_M, (H_M + H_J) / (n_M + n_J), H / (n_M + n_J + n_A))</c>, rounded up. The hours are worker-minutes of the batch split by grade;
    /// with no master the journeymen do the M steps at <paramref name="unsupervisedPm"/> (and with neither, the hands do the M and J steps at it). Needs at least one person.
    /// </summary>
    public static long ElapsedMinutes(long hM, long hJ, long hA, int nM, int nJ, int nA, int unsupervisedPm = 1500)
    {
        if (nM + nJ + nA <= 0) return long.MaxValue;
        if (nM == 0)
        {
            if (nJ > 0) { hJ += (hM * unsupervisedPm + 999) / 1000; hM = 0; }
            else { hA += ((hM + hJ) * unsupervisedPm + 999) / 1000; hM = hJ = 0; }
        }
        long t = 0;
        if (hM > 0) t = Math.Max(t, CeilDiv(hM, nM));
        if (hM + hJ > 0 && nM + nJ > 0) t = Math.Max(t, CeilDiv(hM + hJ, nM + nJ));
        t = Math.Max(t, CeilDiv(hM + hJ + hA, nM + nJ + nA));
        return t;
    }

    /// <summary>The batch's wage cost when everyone on it works its whole elapsed time (4.1, ES-1's first two rows): <c>T x (sum of the hourly wages)</c>.</summary>
    public static long CostForElapsed(long elapsedMinutes, int nM, int nJ, int nA, int wageM = 50, int wageJ = 35, int wageA = 15)
        => elapsedMinutes * ((long)nM * wageM + (long)nJ * wageJ + (long)nA * wageA) / 60;

    /// <summary>The wage cost per piece when every step is done by its own grade (4.1, ES-1's third row): the minutes of each grade at its wage.</summary>
    public static long CostByGrade(long hM, long hJ, long hA, int wageM = 50, int wageJ = 35, int wageA = 15)
        => (hM * wageM + hJ * wageJ + hA * wageA) / 60;

    /// <summary>
    /// The batch progress an hour adds (micro-ppm of <see cref="Station.Complete"/>) from the crew's capacities, each the sum of its members' one-worker-hour progress (already weighted by crowding and a master's share):
    /// <c>min(c_M / m, (c_M + c_J) / (m + j), c_all / (m + j + a))</c> with the grades as fractions of the batch (a visiting master's <paramref name="visitM"/> serves the M steps only, never the total), rounded up so a batch completes in exactly its hours. With no master the journeymen take the M steps at
    /// <paramref name="unsupervisedPm"/>; with neither, the hands take the M and J steps at it. For an undivided recipe this is the plain sum, exactly Phase 2's rule.
    /// </summary>
    public static long Rate(long cM, long cJ, long cA, Grades g, int unsupervisedPm = 1500, long visitM = 0)
    {
        int m = g.M, j = g.J, a = g.A; long mCap = cM + visitM;
        if (mCap == 0 && m > 0)
        {
            if (cJ > 0) { j += (int)(((long)m * unsupervisedPm + 999) / 1000); m = 0; }
            else { a += (int)(((long)(m + j) * unsupervisedPm + 999) / 1000); m = j = 0; }
        }
        long best = CeilDiv((cM + cJ + cA) * 1000, m + j + a);
        if (m > 0) best = Math.Min(best, CeilDiv(mCap * 1000, m));
        if (j > 0) best = Math.Min(best, CeilDiv((mCap + cJ) * 1000, m + j));
        return best;
    }

    private static long CeilDiv(long a, long b) => (a + b - 1) / b;
}
