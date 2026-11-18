using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Autopilot;

/// <summary>What a limit check said. <c>Allowed</c> is the only good answer; the rest name the limit.</summary>
public enum LimitVerdict : byte { Allowed, OverDailyCap, OverTotalCap, BelowReserve, CannotAfford, Crime, TooRisky, NotHomeByNight, KindNotAccepted }

/// <summary>One tagged action a test or a later phase adds to the candidate set (the brain's own actions carry no tag): its kind (a name), its utility and its tags.</summary>
public readonly record struct TaggedAction(string Name, int Score, ActionTag Tags);

/// <summary>
/// The limits, checked twice (autopilot spec 4.3): at planning (<see cref="Filter"/> removes actions from the candidate set, <see cref="CheckSpend"/> says whether a purchase fits) and at execution (the barrier calls
/// <see cref="CheckCommand"/> for every command with <c>Origin = Autopilot</c>; a violation is rejected, logged and counted). Pure functions of the state, the limits and the numbers handed in: no clock, no allocation.
/// </summary>
public static class LimitGuard
{
    /// <summary>Whether a candidate may be chosen at all: a crime-tagged action is removed while <see cref="LimitSet.NoCrimes"/> holds.</summary>
    public static bool Allows(LimitSet l, ActionTag tags) => !(l.NoCrimes && (tags & ActionTag.Crime) != 0);

    /// <summary>The brain's candidate set under the limits: the actions a limit forbids are put in the policy's filter mask (the brain's own kinds carry no crime tag; the crime vocabulary of Phase 6 adds the tagged ones).</summary>
    public static void Filter(LimitSet l, ActionPolicy p, bool spendLeft)
    {
        // with the day's allowance used up the autopilot window-shops no more: Shop is removed (Eat is a need and stays: food is bought only if it fits, see CheckSpend)
        if (!spendLeft) p.Forbid(ActionKind.Shop);
    }

    /// <summary>The day's allowance still unspent (long.MaxValue when there is no cap): what the cap leaves, never below zero.</summary>
    public static long AllowanceLeft(LimitSet l, AutopilotState st, int day)
    {
        long left = long.MaxValue;
        if (l.SpendCapMitesPerDay >= 0) left = Math.Min(left, Math.Max(0, l.SpendCapMitesPerDay - SpentOn(st, day)));
        if (l.SpendCapTotalMites >= 0) left = Math.Min(left, Math.Max(0, l.SpendCapTotalMites - st.SpentTotal));
        return left;
    }

    /// <summary>What the person has spent on game day <paramref name="day"/> (the allowance does not carry over: another day counts as zero).</summary>
    public static long SpentOn(AutopilotState st, int day) => st.SpentDay == day ? st.SpentToday : 0;

    /// <summary>Does a purchase of <paramref name="mites"/> on <paramref name="day"/> fit the cap, the lifetime budget and the reserve, given the purse <paramref name="purse"/>?</summary>
    public static LimitVerdict CheckSpend(LimitSet l, AutopilotState st, int day, long purse, long mites)
    {
        if (mites <= 0) return LimitVerdict.Allowed;
        if (l.SpendCapMitesPerDay >= 0 && SpentOn(st, day) + mites > l.SpendCapMitesPerDay) return LimitVerdict.OverDailyCap;
        if (l.SpendCapTotalMites >= 0 && st.SpentTotal + mites > l.SpendCapTotalMites) return LimitVerdict.OverTotalCap;
        if (purse - mites < l.PurseReserveMites) return purse < mites ? LimitVerdict.CannotAfford : LimitVerdict.BelowReserve;
        return LimitVerdict.Allowed;
    }

    /// <summary>Books a spend (planning reserves it, the barrier settles it): rolls the day when it changed.</summary>
    public static void Commit(AutopilotState st, int day, long mites)
    {
        if (st.SpentDay != day) { st.SpentDay = day; st.SpentToday = 0; }
        st.SpentToday += mites; st.SpentTotal += mites;
    }

    /// <summary>Takes back a booked spend (a refused or failed command).</summary>
    public static void Refund(AutopilotState st, int day, long mites)
    {
        if (st.SpentDay == day) st.SpentToday = Math.Max(0, st.SpentToday - mites);
        st.SpentTotal = Math.Max(0, st.SpentTotal - mites);
    }

    // ------------------------------------------------------------------ risk

    /// <summary><c>risk_pm = danger x night factor</c>: the night doubles the region danger (spec 4.3). Clamped to 1000... not clamped: 2 x 450 = 900 must exceed a cap of 800.</summary>
    public static int RiskPm(int dangerPm, bool night) => night ? dangerPm * 2 : dangerPm;

    public static bool RiskOk(LimitSet l, int dangerPm, bool night, bool escorted = false, int escortCutPm = 400)
    {
        long r = RiskPm(dangerPm, night);
        if (escorted) r = r * (1000 - escortCutPm) / 1000;
        return r <= l.MaxRiskPm;
    }

    /// <summary>One candidate route: a name and the danger of each cell along it (pm, 0..1000).</summary>
    public sealed record RouteOption(string Name, int[] DangerPm, int LengthM = 0);

    /// <summary>
    /// A plan is refused if the danger anywhere along its path is above the cap (spec 4.3). Candidates are other routes, later departures (daylight), or an escort. Returns the first acceptable route in order of lowest peak
    /// risk (ties by name), or null: the plan is refused and the caller raises <see cref="Ravensguard.Sim.Travel.InterruptKind.LimitReached"/>.
    /// </summary>
    public static RouteOption? ChooseRoute(LimitSet l, IEnumerable<RouteOption> routes, bool night, bool escorted = false, int escortCutPm = 400)
    {
        RouteOption? best = null; long bestPeak = long.MaxValue;
        foreach (var r in routes.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            long peak = 0; foreach (int d in r.DangerPm) peak = Math.Max(peak, RiskPm(d, night));
            if (escorted) peak = peak * (1000 - escortCutPm) / 1000;
            if (peak > l.MaxRiskPm) continue;
            if (peak < bestPeak) { best = r; bestPeak = peak; }
        }
        return best;
    }

    // ------------------------------------------------------------------ home by nightfall

    /// <summary>
    /// Game seconds of a round trip, rounded up: <paramref name="pathM"/> each way at <paramref name="speedMmPerGs"/> (the jog is 150 mm a game second: 540 m a game hour), plus the minutes spent there.
    /// Spec 4.3 / AP-6: two kilometres each way at the jog is 3.70 hours each way. Integer (review finding 16; it was double minutes): rounding up loses nothing, since the comparison
    /// in <see cref="HomeByNightfallOk"/> is against whole seconds.
    /// </summary>
    public static long RoundTripSec(long pathM, int speedMmPerGs = 150, int stayMinutes = 0)
    {
        int v = Math.Max(1, speedMmPerGs);
        return (2000L * Math.Max(0, pathM) + v - 1) / v + stayMinutes * 60L;
    }

    /// <summary>A plan that starts away from home at minute-of-day <paramref name="startMinute"/> is allowed only if <c>start + round trip &lt;= sunset</c> of that day (minus the content's offset), in whole seconds.</summary>
    public static bool HomeByNightfallOk(int latTenths, int dayOfYear, int startMinute, long roundTripSec, int offsetMin = 0)
        => startMinute * 60L + roundTripSec <= (Daylight.SunsetMinute(latTenths, dayOfYear) - offsetMin) * 60L;

    /// <summary>True when the person is already away at an hour before sunset and should head home now (spec 4.3: sunset minus one hour).</summary>
    public static bool HeadHomeNow(int latTenths, int dayOfYear, int minuteOfDay) => minuteOfDay >= Daylight.SunsetMinute(latTenths, dayOfYear) - 60;
}
